#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

namespace SmartMediaPlatform.Video.EditorTools
{
    /// <summary>
    /// シーン生成ツールから <b>VRChat / UdonSharp のコンポーネントを正しい手順で置く</b>ための補助。
    ///
    /// <b>UdonSharpBehaviour は「ただの MonoBehaviour」ではありません。</b>
    /// 実際に動くには 3 つが揃っている必要があります。
    /// <list type="number">
    /// <item><c>UdonSharpProgramAsset</c>(U# プログラム。<see cref="UdonSharpProgramAssetFactory"/> が作る)</item>
    /// <item>それを差した <c>UdonBehaviour</c></item>
    /// <item>Inspector 用のプロキシ(= <c>UdonSharpBehaviour</c> コンポーネント)</item>
    /// </list>
    ///
    /// <c>GameObject.AddComponent&lt;T&gt;()</c> は 3 番しか付けないので、そのままでは
    /// <c>Unable to find valid U# program asset</c> /
    /// <c>the U# program asset on this component is null</c> になります。
    ///
    /// さらに、<b>ClientSim は VRCSceneDescriptor が無いと起動しません</b>
    /// (<c>Cannot start ClientSim if there is no scene descriptor!</c>)。
    /// ClientSim が起動しなければ Udon は 1 行も実行されず、動画イベントも届きません。
    /// そのため <see cref="EnsureSceneDescriptor"/> も用意しています。
    ///
    /// VRChat SDK / UdonSharp の型名・API 名はバージョンで変わりうるので、
    /// すべてリフレクションで扱います(Phase1-2 の <c>UdonCatalogBaker</c> と同じ方針)。
    /// </summary>
    public static class UdonSharpSceneUtility
    {
        private const string UndoTypeName = "UdonSharpUndo";
        private const string SceneDescriptorTypeName = "VRCSceneDescriptor";

        /// <summary>
        /// UdonSharp のコンポーネントを、プログラム・UdonBehaviour ごと追加する。
        ///
        /// プログラムが無ければ <see cref="UdonSharpProgramAssetFactory"/> が先に作ります。
        /// </summary>
        /// <param name="target">追加先の GameObject。</param>
        /// <param name="behaviourType">追加する UdonSharpBehaviour の型。</param>
        /// <param name="needsCompile">
        /// プログラムを新規作成し、<b>コンパイルが必要</b>な状態のとき true。
        /// このとき呼び出し側は「シーンを作らずにやり直しを促す」のが安全です。
        /// </param>
        /// <returns>追加できたコンポーネント。できなければ null(このとき何も付けません)。</returns>
        public static Component AddUdonSharpComponent(
            GameObject target, Type behaviourType, out bool needsCompile)
        {
            needsCompile = false;
            if (target == null || behaviourType == null) return null;

            // 1. プログラム(.asset)を用意する
            if (!UdonSharpProgramAssetFactory.IsAvailable)
            {
                Debug.LogWarning(
                    "[UdonSharpSceneUtility] UdonSharp が見つかりません。"
                    + $"{behaviourType.Name} は追加しませんでした。\n"
                    + ManualSetupInstruction(target, behaviourType));
                return null;
            }

            var report = UdonSharpProgramAssetFactory.EnsureProgramAssets(behaviourType);
            if (report.Failed > 0)
            {
                Debug.LogWarning(
                    $"[UdonSharpSceneUtility] {behaviourType.Name} のプログラムを作れませんでした。\n"
                    + ManualSetupInstruction(target, behaviourType));
                return null;
            }

            if (report.HasCreated && !UdonSharpProgramAssetFactory.TryCompile())
            {
                // コンパイル前にコンポーネントを付けると
                // 「Could not load the program」の壊れた状態でシーンに残ってしまう
                needsCompile = true;
                return null;
            }

            // 2. プロキシ + UdonBehaviour をまとめて作る
            Type undoType = FindTypeByName(UndoTypeName);
            if (undoType == null)
            {
                Debug.LogWarning(
                    $"[UdonSharpSceneUtility] {UndoTypeName} が見つかりません。"
                    + $"{behaviourType.Name} は追加しませんでした。\n"
                    + ManualSetupInstruction(target, behaviourType));
                return null;
            }

            try
            {
                // UdonSharpUndo.AddComponent(GameObject, Type)
                MethodInfo add = undoType.GetMethod(
                    "AddComponent",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(GameObject), typeof(Type) },
                    null);

                if (add != null)
                {
                    return add.Invoke(null, new object[] { target, behaviourType }) as Component;
                }

                // UdonSharpUndo.AddComponent<T>(GameObject)
                foreach (var candidate in undoType.GetMethods(
                             BindingFlags.Public | BindingFlags.Static))
                {
                    if (candidate.Name != "AddComponent" || !candidate.IsGenericMethodDefinition) continue;

                    var parameters = candidate.GetParameters();
                    if (parameters.Length != 1 || parameters[0].ParameterType != typeof(GameObject)) continue;

                    return candidate
                        .MakeGenericMethod(behaviourType)
                        .Invoke(null, new object[] { target }) as Component;
                }

                Debug.LogWarning(
                    $"[UdonSharpSceneUtility] {UndoTypeName}.AddComponent が見つかりません。"
                    + $"{behaviourType.Name} は追加しませんでした。\n"
                    + ManualSetupInstruction(target, behaviourType));
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[UdonSharpSceneUtility] {behaviourType.Name} を追加できませんでした: "
                    + (e.InnerException != null ? e.InnerException.Message : e.Message) + "\n"
                    + ManualSetupInstruction(target, behaviourType));
            }

            return null;
        }

        /// <summary>
        /// シーンに <c>VRCSceneDescriptor</c> を用意する。
        ///
        /// <b>これが無いと ClientSim が起動せず、Udon が 1 行も動きません。</b>
        /// (Console: <c>Cannot start ClientSim if there is no scene descriptor!</c>)
        /// </summary>
        /// <returns>用意できたら その GameObject。SDK が見つからなければ null。</returns>
        public static GameObject EnsureSceneDescriptor(string objectName = "VRCWorld")
        {
            Type descriptorType = FindTypeByName(SceneDescriptorTypeName);
            if (descriptorType == null)
            {
                Debug.LogWarning(
                    "[UdonSharpSceneUtility] VRCSceneDescriptor が見つかりません。"
                    + "ClientSim が起動しないため、Udon のイベントは届きません。");
                return null;
            }

            var existing = UnityEngine.Object.FindObjectOfType(descriptorType) as Component;
            if (existing != null) return existing.gameObject;

            var world = new GameObject(objectName);
            var descriptor = world.AddComponent(descriptorType);

            // スポーン地点が空だと SDK の検証に引っかかるので、自分自身を入れておく
            TrySetSpawns(descriptor, world.transform);

            return world;
        }

        /// <summary>手動で付け直すときの案内文。</summary>
        public static string ManualSetupInstruction(GameObject target, Type behaviourType)
        {
            string objectName = target != null ? target.name : "(対象の GameObject)";
            string typeName = behaviourType != null ? behaviourType.Name : "(UdonSharpBehaviour)";

            return $"  → Hierarchy で '{objectName}' を選び、Inspector の Add Component から\n"
                   + $"    '{typeName}' を追加してください(UdonSharp が必要な設定を自動で行います)。\n"
                   + "    追加しなくても再生は動きます(VideoEventBridge のポーリングが保険になります)が、\n"
                   + "    実機イベント経由の確認にはなりません。";
        }

        /// <summary>プログラム生成後にやり直しを促す文言。</summary>
        public static string RecompileInstruction(string menuPath)
        {
            return "UdonSharp のプログラムを新しく生成しました。\n"
                   + "Unity のコンパイルが終わってから、もう一度\n"
                   + $"  {menuPath}\n"
                   + "を実行してください(コンパイル前にコンポーネントを置くと壊れた状態になるため、\n"
                   + "今回はシーンを作成していません)。";
        }

        // ───────── 内部 ─────────

        private static void TrySetSpawns(Component descriptor, Transform spawn)
        {
            if (descriptor == null) return;

            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            for (var type = descriptor.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField("spawns", flags);
                if (field == null || field.FieldType != typeof(Transform[])) continue;

                field.SetValue(descriptor, new[] { spawn });
                return;
            }
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
    }
}
#endif
