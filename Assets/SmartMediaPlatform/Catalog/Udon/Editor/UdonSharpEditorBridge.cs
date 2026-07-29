#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.Catalog.UdonEditor
{
    /// <summary>
    /// <b>UdonSharp の Inspector 用 API を、名前で呼ぶための小さな窓口。</b>
    ///
    /// <c>UdonSharpBehaviour</c> に自前の Inspector を付けるときは、
    /// 先に <c>UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader</c> を呼ばないと
    /// <b>Program Asset や同期設定の欄が消えます</b>(壊れているように見えます)。
    ///
    /// ただしこの API は U# のバージョンで置き場所が変わるため、
    /// このプロジェクトの他の SDK 連携と同じく<b>リフレクションで名前から探します</b>
    /// (<c>UdonSharpProgramAssetFactory</c> / <c>VRChatVideoPlayerFactory</c> と同じ方針)。
    /// 見つからなければ何もしないので、<b>SDK が変わってもコンパイルは壊れません</b>。
    /// </summary>
    internal static class UdonSharpEditorBridge
    {
        private static bool _looked;
        private static MethodInfo _drawHeader;

        /// <summary>
        /// U# の既定の見出しを描く。
        /// </summary>
        /// <returns>
        /// <b>true なら、これ以上 Inspector を描いてはいけません。</b>
        /// U# が「いまは中身を描く場面ではない」(プロキシ未生成など)と判断した状態です。
        /// </returns>
        public static bool DrawDefaultHeader(UnityEngine.Object target)
        {
            Resolve();
            if (_drawHeader == null) return false;

            try
            {
                // 省略可能な引数も明示的に埋める(Invoke は既定値を補ってくれないため)。
                var parameters = _drawHeader.GetParameters();
                var args = new object[parameters.Length];
                args[0] = target;
                for (int i = 1; i < parameters.Length; i++) args[i] = parameters[i].DefaultValue;

                object result = _drawHeader.Invoke(null, args);
                return result is bool stop && stop;
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[UdonSharpEditorBridge] U# の見出しを描けませんでした: " + e.Message);
                _drawHeader = null;
                return false;
            }
        }

        private static void Resolve()
        {
            if (_looked) return;
            _looked = true;

            Type gui = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                gui = assembly.GetType("UdonSharpEditor.UdonSharpGUI");
                if (gui != null) break;
            }
            if (gui == null) return;

            // bool DrawDefaultUdonSharpBehaviourHeader(UnityEngine.Object, bool, bool)
            // 版によって引数の数が違うので、名前が合う静的メソッドのうち
            // 「1 引数で呼べるもの」を選ぶ。
            foreach (var method in gui.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != "DrawDefaultUdonSharpBehaviourHeader") continue;

                var parameters = method.GetParameters();
                if (parameters.Length == 0) continue;
                if (!parameters[0].ParameterType.IsAssignableFrom(typeof(UnityEngine.Object))
                    && !typeof(UnityEngine.Object).IsAssignableFrom(parameters[0].ParameterType))
                {
                    continue;
                }

                bool callableWithOne = true;
                for (int i = 1; i < parameters.Length; i++)
                {
                    if (!parameters[i].HasDefaultValue) { callableWithOne = false; break; }
                }
                if (!callableWithOne) continue;

                _drawHeader = method;
                return;
            }
        }
    }
}
#endif
