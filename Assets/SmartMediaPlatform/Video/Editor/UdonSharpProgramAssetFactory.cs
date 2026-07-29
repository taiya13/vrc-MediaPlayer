#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.Video.EditorTools
{
    /// <summary>
    /// <b>UdonSharpProgramAsset(U# プログラム)の生成をまとめて引き受ける窓口。</b>
    ///
    /// <b>なぜ必要か</b><br/>
    /// UdonSharpBehaviour を継承しただけでは、シーンに置いても動きません。
    /// クラスごとに対応する <c>UdonSharpProgramAsset</c>(<c>.asset</c> ファイル)が要ります。
    /// これが無いと、コンポーネントを付けた瞬間に次のエラーになります。
    ///
    /// <code>
    /// [UdonSharp] Cannot run serialization on U# behaviour '...', the U# program asset on this
    /// component is null. ... verify that you have a UdonSharpProgramAsset with the script
    /// '...' assigned to it.
    /// </code>
    ///
    /// 通常は Inspector の Add Component から追加したときに UdonSharp が自動生成しますが、
    /// <b>エディタスクリプトからシーンを組み立てる場合は誰も作ってくれません。</b>
    /// このリポジトリには <c>.asset</c> を 1 つも同梱していない(UdonSharp のバージョンに
    /// 依存する内容なので同梱できない)ため、必要になった時点でここが作ります。
    ///
    /// <b>UdonSharp の API 名に依存しません。</b>
    /// 型名・メソッド名はバージョンで変わりうるので、リフレクションで探します
    /// (Phase1-2 の <c>UdonCatalogBaker.SyncUdonSharpProxy</c> と同じ方針)。
    /// <c>MonoScript</c> を保持しているフィールドも、名前ではなく<b>型</b>で探します。
    /// </summary>
    public static class UdonSharpProgramAssetFactory
    {
        private const string MenuRoot = "Tools/Smart Media Platform/UdonSharp/";

        /// <summary>生成・確認の結果。</summary>
        public struct Report
        {
            /// <summary>すでに存在していた件数。</summary>
            public int Existing;

            /// <summary>新しく作った件数。</summary>
            public int Created;

            /// <summary>作れなかった件数(元の .cs が見つからない等)。</summary>
            public int Failed;

            /// <summary>1 つでも新規作成したか。</summary>
            public bool HasCreated => Created > 0;

            public override string ToString()
            {
                return $"既存 {Existing} / 新規 {Created} / 失敗 {Failed}";
            }
        }

        /// <summary>UdonSharp がプロジェクトに入っているか。</summary>
        public static bool IsAvailable => ProgramAssetType != null && BehaviourType != null;

        /// <summary><c>UdonSharpProgramAsset</c> の型。無ければ null。</summary>
        public static Type ProgramAssetType => FindTypeByName("UdonSharpProgramAsset");

        /// <summary><c>UdonSharpBehaviour</c> の型。無ければ null。</summary>
        public static Type BehaviourType => FindTypeByName("UdonSharpBehaviour");

        // ───────── メニュー ─────────

        [MenuItem(MenuRoot + "Create Missing UdonSharp Program Assets")]
        public static void CreateMissingProgramAssetsMenu()
        {
            if (!IsAvailable)
            {
                EditorUtility.DisplayDialog(
                    "Smart Media Platform",
                    "UdonSharp が見つかりません。VRChat SDK(Worlds)を導入してください。",
                    "OK");
                return;
            }

            var report = EnsureAllProgramAssets();
            bool compiled = report.HasCreated && TryCompile();

            string message =
                $"UdonSharp プログラム: {report}\n\n"
                + (report.HasCreated
                    ? (compiled
                        ? "生成してコンパイルしました。"
                        : "生成しました。Unity のコンパイルが終わってから使ってください。")
                    : "不足はありませんでした。");

            Debug.Log("[UdonSharpProgramAssetFactory] " + message.Replace("\n\n", " / "));
            EditorUtility.DisplayDialog("Smart Media Platform", message, "OK");
        }

        [MenuItem(MenuRoot + "Repair Broken UdonSharp Program Assets")]
        public static void RepairBrokenProgramAssetsMenu()
        {
            if (!IsAvailable)
            {
                EditorUtility.DisplayDialog(
                    "Smart Media Platform",
                    "UdonSharp が見つかりません。VRChat SDK(Worlds)を導入してください。",
                    "OK");
                return;
            }

            int repaired = RepairBrokenProgramAssets(out int removed, out var details);

            string message = repaired == 0 && removed == 0
                ? "壊れた U# プログラムはありませんでした。"
                : $"修復 {repaired} 件 / 削除 {removed} 件\n\n{details}";

            Debug.Log("[UdonSharpProgramAssetFactory] " + message);
            EditorUtility.DisplayDialog("Smart Media Platform", message, "OK");
        }

        /// <summary>
        /// <b>「Source C# script … is null」を直す。</b>
        ///
        /// <b>なぜ壊れるのか</b><br/>
        /// プログラム(<c>.asset</c>)は元の <c>.cs</c> を <b>GUID</b> で指しています。
        /// <c>.meta</c> ごと <c>.cs</c> を入れ替えると GUID が変わり、
        /// 前に作られたプログラムは行き先を失って null になります。
        /// U# は壊れたプログラムが 1 つでもあるとコンパイル全体を止めるので、
        /// <b>何も動かなくなります</b>。
        ///
        /// ここでは、ファイル名から元の クラス を推測して繋ぎ直します。
        /// 推測できないものは、放っておくとコンパイルを止め続けるので削除します。
        /// </summary>
        /// <param name="removed">繋ぎ直せずに消したプログラムの数。</param>
        /// <param name="details">Console 用の内訳。</param>
        /// <returns>繋ぎ直した数。</returns>
        public static int RepairBrokenProgramAssets(out int removed, out string details)
        {
            removed = 0;
            details = string.Empty;
            if (!IsAvailable) return 0;

            var scriptMember = FindMonoScriptMember(ProgramAssetType);
            if (scriptMember == null) return 0;

            var log = new StringBuilder();
            int repaired = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:" + ProgramAssetType.Name))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null || !ProgramAssetType.IsInstanceOfType(asset)) continue;

                // 生きているものは触らない
                var current = scriptMember.GetValue(asset) as MonoScript;
                if (current != null && current.GetClass() != null) continue;

                // ファイル名 = クラス名 という前提で繋ぎ直す
                //(EnsureProgramAsset が .cs の隣に同じ名前で作るため)
                string typeName = Path.GetFileNameWithoutExtension(path);
                var type = FindBehaviourTypeByName(typeName);
                var script = type != null ? FindMonoScript(type) : null;

                if (script != null)
                {
                    scriptMember.SetValue(asset, script);
                    EditorUtility.SetDirty(asset);
                    repaired++;
                    log.AppendLine($"  繋ぎ直し: {path} -> {type.FullName}");
                    continue;
                }

                AssetDatabase.DeleteAsset(path);
                removed++;
                log.AppendLine($"  削除    : {path}(対応する UdonSharpBehaviour が見つかりません)");
            }

            if (repaired > 0 || removed > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            details = log.ToString();
            return repaired;
        }

        /// <summary>名前から <c>UdonSharpBehaviour</c> を探す。</summary>
        private static Type FindBehaviourTypeByName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            foreach (var type in FindAllBehaviourTypes())
            {
                if (type.Name == typeName) return type;
            }
            return null;
        }

        [MenuItem(MenuRoot + "Diagnose UdonSharp Setup")]
        public static void DiagnoseMenu()
        {
            Debug.Log(Diagnose());
        }

        // ───────── API ─────────

        /// <summary>
        /// プロジェクト内のすべての <c>UdonSharpBehaviour</c> について、
        /// 足りないプログラムを作る。
        /// </summary>
        public static Report EnsureAllProgramAssets()
        {
            return EnsureProgramAssets(FindAllBehaviourTypes());
        }

        /// <summary>指定した クラス について、足りないプログラムを作る。</summary>
        public static Report EnsureProgramAssets(params Type[] behaviourTypes)
        {
            return EnsureProgramAssets((IEnumerable<Type>)behaviourTypes);
        }

        /// <summary>指定した クラス について、足りないプログラムを作る。</summary>
        public static Report EnsureProgramAssets(IEnumerable<Type> behaviourTypes)
        {
            var report = new Report();
            if (behaviourTypes == null || !IsAvailable) return report;

            bool anyCreated = false;

            foreach (var type in behaviourTypes)
            {
                if (type == null || type.IsAbstract) continue;

                var asset = EnsureProgramAsset(type, out bool created);
                if (asset == null) report.Failed++;
                else if (created) { report.Created++; anyCreated = true; }
                else report.Existing++;
            }

            if (anyCreated)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return report;
        }

        /// <summary>
        /// その クラス のプログラムを取得する。無ければ作る。
        /// </summary>
        /// <returns>プログラム。作れなければ null。</returns>
        public static ScriptableObject EnsureProgramAsset(Type behaviourType, out bool created)
        {
            created = false;
            if (behaviourType == null || !IsAvailable) return null;

            var existing = FindProgramAsset(behaviourType);
            if (existing != null) return existing;

            var script = FindMonoScript(behaviourType);
            if (script == null)
            {
                Debug.LogWarning(
                    $"[UdonSharpProgramAssetFactory] {behaviourType.FullName} の .cs が見つからず、"
                    + "プログラムを作れませんでした。");
                return null;
            }

            var scriptField = FindMonoScriptMember(ProgramAssetType);
            if (scriptField == null)
            {
                Debug.LogWarning(
                    "[UdonSharpProgramAssetFactory] UdonSharpProgramAsset に MonoScript の"
                    + "フィールドが見つかりません。UdonSharp のバージョンを確認してください。");
                return null;
            }

            string scriptPath = AssetDatabase.GetAssetPath(script);
            string assetPath = Path.ChangeExtension(scriptPath, ".asset");

            var asset = ScriptableObject.CreateInstance(ProgramAssetType);
            if (asset == null) return null;

            scriptField.SetValue(asset, script);

            AssetDatabase.CreateAsset(asset, assetPath);
            EditorUtility.SetDirty(asset);
            created = true;

            Debug.Log(
                $"[UdonSharpProgramAssetFactory] {behaviourType.Name} のプログラムを作成しました: "
                + assetPath);
            return asset;
        }

        /// <summary>その クラス に紐づいたプログラムを探す。無ければ null。</summary>
        public static ScriptableObject FindProgramAsset(Type behaviourType)
        {
            if (behaviourType == null || !IsAvailable) return null;

            var scriptMember = FindMonoScriptMember(ProgramAssetType);
            if (scriptMember == null) return null;

            foreach (var guid in AssetDatabase.FindAssets("t:" + ProgramAssetType.Name))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null || !ProgramAssetType.IsInstanceOfType(asset)) continue;

                var script = scriptMember.GetValue(asset) as MonoScript;
                if (script != null && script.GetClass() == behaviourType) return asset;
            }

            return null;
        }

        /// <summary>
        /// U# のコンパイルを走らせる。
        /// 同期コンパイルの API が見つからなければ false(Unity の通常コンパイルに任せる)。
        /// </summary>
        public static bool TryCompile()
        {
            // 版によって置き場所が違うので、名前で総当たりする
            var candidates = new[]
            {
                new KeyValuePair<Type, string>(ProgramAssetType, "CompileAllCsPrograms"),
                new KeyValuePair<Type, string>(FindTypeByName("UdonSharpCompilerV1"), "CompileSync"),
                new KeyValuePair<Type, string>(FindTypeByName("UdonSharpCompilerV1"), "Compile"),
                new KeyValuePair<Type, string>(FindTypeByName("UdonSharpProgramAsset"), "CompileAllCsPrograms"),
            };

            foreach (var candidate in candidates)
            {
                if (candidate.Key == null) continue;
                if (InvokeStatic(candidate.Key, candidate.Value)) return true;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return false;
        }

        /// <summary>いま何が揃っていて何が足りないかを文章にする。</summary>
        public static string Diagnose()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== UdonSharp セットアップ診断 ===");

            if (!IsAvailable)
            {
                sb.AppendLine("UdonSharp が見つかりません(VRChat SDK Worlds を導入してください)。");
                return sb.ToString();
            }

            sb.AppendLine($"UdonSharpBehaviour   : {BehaviourType.FullName}");
            sb.AppendLine($"UdonSharpProgramAsset: {ProgramAssetType.FullName}");

            var types = FindAllBehaviourTypes();
            sb.AppendLine($"プロジェクト内の UdonSharpBehaviour: {types.Count} 件");

            foreach (var type in types)
            {
                var asset = FindProgramAsset(type);
                sb.AppendLine(asset != null
                    ? $"  OK   {type.FullName}  ->  {AssetDatabase.GetAssetPath(asset)}"
                    : $"  未生成 {type.FullName}");
            }

            sb.AppendLine();
            sb.AppendLine("未生成があれば "
                          + "Tools > Smart Media Platform > UdonSharp > "
                          + "Create Missing UdonSharp Program Assets を実行してください。");
            return sb.ToString();
        }

        /// <summary>プロジェクト内の UdonSharpBehaviour をすべて集める。</summary>
        public static List<Type> FindAllBehaviourTypes()
        {
            var result = new List<Type>();
            if (BehaviourType == null) return result;

            foreach (var type in TypeCache.GetTypesDerivedFrom(BehaviourType))
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition) continue;
                result.Add(type);
            }

            result.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            return result;
        }

        // ───────── 内部(リフレクション) ─────────

        /// <summary>
        /// プログラムが持つ <see cref="MonoScript"/> のフィールド/プロパティを、
        /// <b>名前ではなく型で</b>探す(U# の版によって名前が変わるため)。
        /// </summary>
        private static IMemberAccessor FindMonoScriptMember(Type programAssetType)
        {
            if (programAssetType == null) return null;

            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            for (var type = programAssetType; type != null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(flags))
                {
                    if (field.FieldType == typeof(MonoScript)) return new FieldAccessor(field);
                }
            }

            for (var type = programAssetType; type != null; type = type.BaseType)
            {
                foreach (var property in type.GetProperties(flags))
                {
                    if (property.PropertyType == typeof(MonoScript)
                        && property.CanRead && property.CanWrite)
                    {
                        return new PropertyAccessor(property);
                    }
                }
            }

            return null;
        }

        private static MonoScript FindMonoScript(Type behaviourType)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript " + behaviourType.Name))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == behaviourType) return script;
            }

            // 名前で絞れなかった場合の総当たり
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == behaviourType) return script;
            }

            return null;
        }

        /// <summary>省略可能な引数を既定値で埋めて静的メソッドを呼ぶ。</summary>
        private static bool InvokeStatic(Type type, string methodName)
        {
            try
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name != methodName || method.IsGenericMethodDefinition) continue;

                    var parameters = method.GetParameters();
                    var arguments = new object[parameters.Length];
                    bool usable = true;

                    for (int i = 0; i < parameters.Length; i++)
                    {
                        if (parameters[i].HasDefaultValue)
                        {
                            arguments[i] = parameters[i].DefaultValue;
                        }
                        else if (!parameters[i].ParameterType.IsValueType)
                        {
                            arguments[i] = null;
                        }
                        else
                        {
                            usable = false;
                            break;
                        }
                    }

                    if (!usable) continue;

                    method.Invoke(null, arguments);
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[UdonSharpProgramAssetFactory] {type.Name}.{methodName} の呼び出しに失敗: "
                    + (e.InnerException != null ? e.InnerException.Message : e.Message));
            }

            return false;
        }

        private static Type FindTypeByName(string simpleName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }

                if (types == null) continue;

                foreach (var type in types)
                {
                    if (type != null && type.Name == simpleName) return type;
                }
            }
            return null;
        }

        // フィールドとプロパティを同じ形で扱うための小さな抽象
        private interface IMemberAccessor
        {
            object GetValue(object target);
            void SetValue(object target, object value);
        }

        private sealed class FieldAccessor : IMemberAccessor
        {
            private readonly FieldInfo _field;
            public FieldAccessor(FieldInfo field) { _field = field; }
            public object GetValue(object target) => _field.GetValue(target);
            public void SetValue(object target, object value) => _field.SetValue(target, value);
        }

        private sealed class PropertyAccessor : IMemberAccessor
        {
            private readonly PropertyInfo _property;
            public PropertyAccessor(PropertyInfo property) { _property = property; }
            public object GetValue(object target) => _property.GetValue(target);
            public void SetValue(object target, object value) => _property.SetValue(target, value);
        }
    }
}
#endif
