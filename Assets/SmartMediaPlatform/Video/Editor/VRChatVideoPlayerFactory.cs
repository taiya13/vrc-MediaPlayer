#if UNITY_EDITOR && VRC_SDK_VRCSDK3
using System;
using System.Reflection;
using System.Text;
using SmartMediaPlatform.Video.VRChat;
using UnityEngine;
using VRC.SDK3.Video.Components.Base;

namespace SmartMediaPlatform.Video.EditorTools
{
    /// <summary>
    /// <b>シーンに VRChat の動画プレイヤーを置く、共通の組み立て役。</b>Phase4-4 で追加。
    ///
    /// Phase4-3 まで、SDK シーンを作るメニューはそれぞれ
    /// <c>AddComponent&lt;VRCUnityVideoPlayer&gt;()</c> を直接呼んでいました。
    /// <b>Phase4-4 で実機の標準を AVPro にした</b>ので、
    /// 「どちらを置くか」と「画面と音の配線」をここへ集めます。
    ///
    /// <b>AVPro と Unity 版では配線の形が違います。</b>
    /// <list type="bullet">
    /// <item><b>Unity 版</b> … プレイヤー自身が出力先(Renderer / AudioSource)を持つ</item>
    /// <item><b>AVPro</b> … 出力先の側に <c>VRCAVProVideoScreen</c> /
    /// <c>VRCAVProVideoSpeaker</c> を付けて、そこからプレイヤーを指す</item>
    /// </list>
    /// この違いを吸収するのがこのクラスの主な仕事です。
    ///
    /// <b>SDK の型は名前で探します。</b>
    /// <c>VRCVideoPlayerBridge.DetectKind</c> と同じ方針で、
    /// AVPro の名前空間やクラス名が SDK 更新で変わっても<b>コンパイルが壊れません</b>。
    /// 見つからなかったものは <see cref="Result.Report"/> に書き出すので、
    /// Inspector で手当てできます。
    /// </summary>
    public static class VRChatVideoPlayerFactory
    {
        private const string AVProPlayerTypeName = "VRCAVProVideoPlayer";
        private const string AVProScreenTypeName = "VRCAVProVideoScreen";
        private const string AVProSpeakerTypeName = "VRCAVProVideoSpeaker";
        private const string UnityPlayerTypeName = "VRCUnityVideoPlayer";

        /// <summary>組み立て結果。</summary>
        public struct Result
        {
            /// <summary>置いたプレイヤー。置けなければ null。</summary>
            public BaseVRCVideoPlayer Player;

            /// <summary>実際に置いた種類。</summary>
            public VRCVideoPlayerKind Kind;

            /// <summary>画面(映像の出力先)を配線できたか。</summary>
            public bool ScreenWired;

            /// <summary>音の出力先を配線できたか。</summary>
            public bool SpeakerWired;

            /// <summary>何をして何ができなかったかの説明(Console 用)。</summary>
            public string Report;

            /// <summary>プレイヤーを置けたか。</summary>
            public bool Ok => Player != null;
        }

        /// <summary>
        /// 動画プレイヤーを置いて、画面と音を配線する。
        /// </summary>
        /// <param name="target">プレイヤーを付ける GameObject(Host と同じ場所を推奨)。</param>
        /// <param name="preference">どちらを置くか。既定は AVPro。</param>
        /// <param name="screen">映像の出力先。null なら配線しない。</param>
        /// <param name="speaker">音の出力先。null なら配線しない。</param>
        public static Result AddPlayer(
            GameObject target,
            VideoPlayerPreference preference = VideoPlayerPreference.AVPro,
            Renderer screen = null,
            AudioSource speaker = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            var result = new Result();
            var log = new StringBuilder();

            bool wantAVPro = preference != VideoPlayerPreference.Unity;

            if (wantAVPro)
            {
                var avproType = FindTypeByName(AVProPlayerTypeName);
                if (avproType != null)
                {
                    result.Player = target.AddComponent(avproType) as BaseVRCVideoPlayer;
                    result.Kind = VRCVideoPlayerKind.AVPro;
                    log.AppendLine($"  プレイヤー   : {AVProPlayerTypeName}(実機の標準)");
                }
                else
                {
                    log.AppendLine(
                        $"  ※ {AVProPlayerTypeName} が見つかりませんでした。Unity 版で代用します。");
                }
            }

            if (result.Player == null)
            {
                var unityType = FindTypeByName(UnityPlayerTypeName);
                if (unityType == null)
                {
                    log.AppendLine(
                        $"  ※ {UnityPlayerTypeName} も見つかりません。SDK の構成を確認してください。");
                    result.Report = log.ToString();
                    return result;
                }

                result.Player = target.AddComponent(unityType) as BaseVRCVideoPlayer;
                result.Kind = VRCVideoPlayerKind.Unity;
                log.AppendLine($"  プレイヤー   : {UnityPlayerTypeName}");
            }

            if (result.Kind == VRCVideoPlayerKind.AVPro)
            {
                WireAVPro(result.Player, screen, speaker, ref result, log);
            }
            else
            {
                WireUnity(result.Player, screen, speaker, ref result, log);
            }

            if (result.Kind == VRCVideoPlayerKind.AVPro)
            {
                log.AppendLine(
                    "  ※ AVPro は Unity エディタ(ClientSim)では映像を出しません。");
                log.AppendLine(
                    "     エディタで絵を確認したいときは VRChatVideoBackendHost の");
                log.AppendLine(
                    "     Preferred Player を Unity にして作り直してください。");
            }

            result.Report = log.ToString();
            return result;
        }

        /// <summary>映像を映すための板(Quad)を作る。</summary>
        public static Renderer CreateScreen(
            string name = "VideoScreen",
            Vector3? position = null,
            Vector3? scale = null)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.position = position ?? new Vector3(0f, 1.5f, 3f);
            quad.transform.localScale = scale ?? new Vector3(3.2f, 1.8f, 1f);

            // 当たり判定は要らない(プレイヤーがぶつかると邪魔になる)
            var collider = quad.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);

            return quad.GetComponent<Renderer>();
        }

        // ───────── AVPro の配線 ─────────

        /// <summary>
        /// AVPro は<b>出力先の側にコンポーネントを付けて、そこからプレイヤーを指します</b>。
        /// </summary>
        private static void WireAVPro(
            BaseVRCVideoPlayer player, Renderer screen, AudioSource speaker,
            ref Result result, StringBuilder log)
        {
            if (screen != null)
            {
                var screenType = FindTypeByName(AVProScreenTypeName);
                if (screenType != null)
                {
                    var component = screen.gameObject.AddComponent(screenType);
                    result.ScreenWired = TrySetVideoPlayer(component, player);

                    log.AppendLine(result.ScreenWired
                        ? $"  映像の出力先 : {screen.name} に {AVProScreenTypeName} を配線しました"
                        : $"  映像の出力先 : {screen.name} に {AVProScreenTypeName} を付けましたが、"
                          + "参照の設定に失敗しました(Inspector で Video Player を割り当ててください)");
                }
                else
                {
                    log.AppendLine(
                        $"  ※ {AVProScreenTypeName} が見つかりません。"
                        + "映像の出力先は Inspector で設定してください。");
                }
            }

            if (speaker != null)
            {
                var speakerType = FindTypeByName(AVProSpeakerTypeName);
                if (speakerType != null)
                {
                    var component = speaker.gameObject.AddComponent(speakerType);
                    result.SpeakerWired = TrySetVideoPlayer(component, player);

                    log.AppendLine(result.SpeakerWired
                        ? $"  音の出力先   : {AVProSpeakerTypeName} を配線しました"
                        : $"  音の出力先   : {AVProSpeakerTypeName} を付けましたが、"
                          + "参照の設定に失敗しました(Inspector で割り当ててください)");
                }
                else
                {
                    log.AppendLine(
                        $"  ※ {AVProSpeakerTypeName} が見つかりません。"
                        + "音の出力先は Inspector で設定してください。");
                }
            }
        }

        // ───────── Unity 版の配線 ─────────

        /// <summary>
        /// Unity 版は<b>プレイヤー自身が出力先を持ちます</b>。
        /// フィールド名は SDK の版で変わりうるので、候補を順に試します。
        /// </summary>
        private static void WireUnity(
            BaseVRCVideoPlayer player, Renderer screen, AudioSource speaker,
            ref Result result, StringBuilder log)
        {
            if (screen != null)
            {
                result.ScreenWired = TrySetMember(
                    player, screen, "targetMaterialRenderer", "TargetMaterialRenderer");

                log.AppendLine(result.ScreenWired
                    ? $"  映像の出力先 : {screen.name} を割り当てました"
                    : "  映像の出力先 : 自動で割り当てられませんでした"
                      + "(Inspector の Target Material Renderer を設定してください)");
            }

            if (speaker != null)
            {
                result.SpeakerWired = TrySetMember(
                    player, new[] { speaker }, "targetAudioSources", "TargetAudioSources");

                log.AppendLine(result.SpeakerWired
                    ? "  音の出力先   : AudioSource を割り当てました"
                    : "  音の出力先   : 自動で割り当てられませんでした"
                      + "(Inspector の Target Audio Sources を設定してください)");
            }
        }

        // ───────── 反射の道具 ─────────

        /// <summary>
        /// AVPro の Screen / Speaker が持つ「どのプレイヤーを見るか」を設定する。
        /// 名前は SDK の版で変わりうるので候補を順に試します。
        /// </summary>
        private static bool TrySetVideoPlayer(Component component, BaseVRCVideoPlayer player)
        {
            return TrySetMember(
                component, player,
                "videoPlayer", "VideoPlayer", "targetVideoPlayer", "Source", "source");
        }

        private static bool TrySetMember(object target, object value, params string[] names)
        {
            if (target == null || value == null) return false;

            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                foreach (string name in names)
                {
                    var field = type.GetField(name, flags);
                    if (field != null && field.FieldType.IsInstanceOfType(value))
                    {
                        field.SetValue(target, value);
                        return true;
                    }

                    var property = type.GetProperty(name, flags);
                    if (property != null && property.CanWrite
                        && property.PropertyType.IsInstanceOfType(value))
                    {
                        property.SetValue(target, value, null);
                        return true;
                    }
                }
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
    }
}
#endif
