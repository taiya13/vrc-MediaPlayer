#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

namespace SmartMediaPlatform.Video.EditorTools
{
    /// <summary>
    /// シーン生成ツールから <b>UdonSharp のコンポーネントを正しい手順で追加する</b>ための補助。
    ///
    /// <b>なぜ専用の入口が要るのか</b><br/>
    /// UdonSharpBehaviour は「ただの MonoBehaviour」ではありません。実際に動くには
    /// <list type="number">
    /// <item>その クラス に対応する <c>UdonSharpProgramAsset</c>(U# プログラム)</item>
    /// <item>それを差した <c>UdonBehaviour</c></item>
    /// <item>Inspector 用のプロキシ(= UdonSharpBehaviour コンポーネント)</item>
    /// </list>
    /// の 3 つが揃っている必要があります。
    ///
    /// <c>GameObject.AddComponent&lt;T&gt;()</c> は 3 番だけを付けるので、
    /// <b>「Unable to find valid U# program asset associated with script」</b>になります。
    /// (この状態のコンポーネントを Inspector で選ぶと
    ///  <c>UdonSharpEditorUtility.RunBehaviourSetup</c> が NullReferenceException を出します)
    ///
    /// 3 つをまとめて用意してくれるのが <c>UdonSharpEditor.UdonSharpUndo.AddComponent</c> です。
    /// UdonSharp の API 名はバージョンで変わりうるので、Phase1-2 の
    /// <c>UdonCatalogBaker.SyncUdonSharpProxy</c> と同じくリフレクションで呼びます。
    ///
    /// <b>見つからなければ何も付けません。</b>
    /// 壊れたコンポーネントをシーンに残すより、付けずに手順を案内するほうが安全だからです。
    /// </summary>
    public static class UdonSharpSceneUtility
    {
        private const string UndoTypeName = "UdonSharpEditor.UdonSharpUndo";

        /// <summary>
        /// UdonSharp のコンポーネントを、UdonBehaviour とプログラムごと追加する。
        /// </summary>
        /// <param name="target">追加先の GameObject。</param>
        /// <param name="behaviourType">追加する UdonSharpBehaviour の型。</param>
        /// <returns>
        /// 追加できたコンポーネント。UdonSharp の API が見つからない、
        /// または追加に失敗した場合は <c>null</c>(このとき<b>何も付けません</b>)。
        /// </returns>
        public static Component AddUdonSharpComponent(GameObject target, Type behaviourType)
        {
            if (target == null || behaviourType == null) return null;

            Type undoType = FindType(UndoTypeName);
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

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type != null) return type;
            }
            return null;
        }
    }
}
#endif
