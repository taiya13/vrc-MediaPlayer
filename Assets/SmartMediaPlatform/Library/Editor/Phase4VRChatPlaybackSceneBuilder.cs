#if UNITY_EDITOR && VRC_SDK_VRCSDK3
using System.IO;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Library.VRChat;
using SmartMediaPlatform.Video.Data;
using SmartMediaPlatform.Video.EditorTools;
using SmartMediaPlatform.Video.Udon;
using SmartMediaPlatform.Video.VRChat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SmartMediaPlatform.Library.EditorTools
{
    /// <summary>
    /// Phase4-4:<b>実際に動画が出る</b>再生フローのシーンを作るエディタツール。
    ///
    /// <code>
    /// MediaLibrary → Queue → PlayerSession → VideoBackendAdapter
    ///     → VRChatVideoBackend → VRCAVProVideoPlayer(実機の標準)
    /// </code>
    ///
    /// <b>SDK 固有のコンポーネントを含むため、シーンはリポジトリに同梱せずここで生成します</b>
    /// (SDK のバージョン差で壊れるのを避けるため)。
    ///
    /// 作るもの:
    /// <list type="number">
    /// <item><c>VRCSceneDescriptor</c>(無いと ClientSim が起動せず Udon が動かない)</item>
    /// <item>映像の出力先(Quad)と音の出力先(AudioSource)</item>
    /// <item><b>AVPro の動画プレイヤー</b>と、その画面 / 音の配線</item>
    /// <item>Udon 中継 + Pump(実機イベントを Backend へ運ぶ)</item>
    /// <item><c>VRChatVideoBackendHost</c> と <c>VRChatPlaybackFlowSceneDemo</c></item>
    /// <item>Catalog の URL を <c>VRCUrl</c> へ焼き込む(実行時には作れない)</item>
    /// </list>
    /// </summary>
    public static class Phase4VRChatPlaybackSceneBuilder
    {
        private const string SceneFolder = "Assets/SmartMediaPlatform/Library/Scenes";
        private const string ScenePath = SceneFolder + "/Phase4VRChatPlaybackScene.unity";

        private const string MenuPath =
            "Tools/Smart Media Platform/Create Phase4-4 VRChat Playback Scene (実際に再生)";

        private const string UnityPlayerMenuPath =
            "Tools/Smart Media Platform/Create Phase4-4 VRChat Playback Scene (Unity 版・エディタで確認)";

        [MenuItem(MenuPath)]
        public static void CreateAVProScene()
        {
            Build(VideoPlayerPreference.AVPro, MenuPath);
        }

        /// <summary>
        /// AVPro はエディタ(ClientSim)で映像を出さないため、
        /// <b>エディタで絵を確認したいとき用</b>の Unity 版も用意しておきます。
        /// </summary>
        [MenuItem(UnityPlayerMenuPath)]
        public static void CreateUnityPlayerScene()
        {
            Build(VideoPlayerPreference.Unity, UnityPlayerMenuPath);
        }

        private static void Build(VideoPlayerPreference preference, string menuPath)
        {
            // 0. 先に UdonSharp のプログラム(.asset)を用意する。
            //    シーンを作る前にやるのが要点 — コンパイル前にコンポーネントを置くと
            //    「the U# program asset on this component is null」の壊れた状態が
            //    シーンに保存されてしまう。
            var report = UdonSharpProgramAssetFactory.EnsureProgramAssets(
                typeof(UdonVRCVideoEventRelay));

            if (report.HasCreated && !UdonSharpProgramAssetFactory.TryCompile())
            {
                string notice = UdonSharpSceneUtility.RecompileInstruction(menuPath);
                Debug.LogWarning("[Phase4VRChatPlaybackSceneBuilder] " + notice);
                EditorUtility.DisplayDialog("Smart Media Platform", notice, "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // 1. VRCSceneDescriptor
            var world = UdonSharpSceneUtility.EnsureSceneDescriptor();

            // 2. 映像と音の出力先
            var screen = VRChatVideoPlayerFactory.CreateScreen();

            var playerObject = new GameObject("VRCVideoPlayer");
            var speaker = playerObject.AddComponent<AudioSource>();
            speaker.spatialBlend = 0f;
            speaker.playOnAwake = false;

            // 3. 動画プレイヤー(既定は AVPro)と配線
            var built = VRChatVideoPlayerFactory.AddPlayer(
                playerObject, preference, screen, speaker);

            if (!built.Ok)
            {
                Debug.LogError(
                    "[Phase4VRChatPlaybackSceneBuilder] 動画プレイヤーを置けませんでした。\n"
                    + built.Report);
                return;
            }

            // 4. 実機イベントの経路(Phase3-2)
            var relay = UdonSharpSceneUtility.AddUdonSharpComponent(
                playerObject, typeof(UdonVRCVideoEventRelay), out _);
            playerObject.AddComponent<UdonVideoEventPump>();

            // 5. Backend の入れ物。好みを合わせておく(自動で探すときの基準になる)
            var host = playerObject.AddComponent<VRChatVideoBackendHost>();
            host.SetPreferredPlayer(preference == VideoPlayerPreference.Unity
                ? VideoPlayerPreference.Unity
                : VideoPlayerPreference.AVPro);

            // 6. Phase4-4 の進行役(Library → Queue → Player)
            var demo = playerObject.AddComponent<VRChatPlaybackFlowSceneDemo>();

            // 7. URL の焼き込み(実行時には作れない)
            IMediaCatalog catalog = new MediaCatalog(new VideoCatalogSource());
            int baked = host.BakeUrlsFrom(catalog);

            EditorUtility.SetDirty(host);

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log(
                "[Phase4VRChatPlaybackSceneBuilder] シーンを作成しました: " + ScenePath + "\n"
                + built.Report
                + $"  Udon 中継      : {(relay != null ? relay.GetType().Name : "未追加")}\n"
                + $"  SceneDescriptor: {(world != null ? world.name : "未作成(ClientSim が起動しません)")}\n"
                + $"  U# プログラム  : {report}\n"
                + $"  ベイク済み URL : {baked} 件\n"
                + $"  進行役         : {demo.GetType().Name}\n"
                + "\n"
                + "  Play を押すと Library / Queue / いま再生中 が出ます。\n"
                + "  左で選んで「▶ 再生」→ VideoBackend → VRChat VideoPlayer まで届きます。\n"
                + "\n"
                + "  ※ VideoCatalogSource の URL は架空のアドレスです。実際に映像を出すには\n"
                + "     VideoCatalogSource の url を実在する動画に差し替えてから、\n"
                + "     Tools > Smart Media Platform > Bake Catalog Urls Into Selected Video Host\n"
                + "     で焼き直してください。"
                + (relay == null
                    ? "\n\n" + UdonSharpSceneUtility.ManualSetupInstruction(
                        playerObject, typeof(UdonVRCVideoEventRelay))
                    : ""));
        }
    }
}
#endif
