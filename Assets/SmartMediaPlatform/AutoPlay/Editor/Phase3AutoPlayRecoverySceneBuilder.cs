#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using SmartMediaPlatform.AutoPlay.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if VRC_SDK_VRCSDK3
using SmartMediaPlatform.AutoPlay.VRChat;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Video.Data;
using SmartMediaPlatform.Video.EditorTools;
using SmartMediaPlatform.Video.Udon;
using SmartMediaPlatform.Video.VRChat;
using VRC.SDK3.Video.Components;
#endif

namespace SmartMediaPlatform.AutoPlay.EditorTools
{
    /// <summary>
    /// Phase3-4(Playback Orchestration &amp; Recovery)の DemoScene を用意するエディタツール。
    ///
    /// 2 種類あります。
    /// <list type="bullet">
    /// <item>
    /// <b>Console 版</b>(<c>Phase3AutoPlayRecoveryDemoScene</c>)…
    /// SDK 不要。Ended / Error / Timeout / 耐久 を Console にまとめて出します。
    /// </item>
    /// <item>
    /// <b>SDK 版</b>(<c>Phase3VRChatAutoPlayRecoveryScene</c>)…
    /// 実際に動画を再生しながら、失敗しても流れ続けることを確認します。
    /// <b>URL をわざと一部だけ焼き込みます</b> — 焼かれていない動画に当たると
    /// <c>InvalidUrl</c> で失敗するので、実機で URL が切れた状況をそのまま再現できます
    /// (実行時に VRCUrl は作りません)。
    /// </item>
    /// </list>
    /// </summary>
    public static class Phase3AutoPlayRecoverySceneBuilder
    {
        private const string SceneFolder = "Assets/SmartMediaPlatform/AutoPlay/Scenes";
        private const string ScenePath = SceneFolder + "/Phase3AutoPlayRecoveryDemoScene.unity";

        [MenuItem("Tools/Smart Media Platform/Open Phase3-4 Auto Play Recovery Demo Scene")]
        public static void OpenDemoScene()
        {
            if (!File.Exists(ScenePath))
            {
                if (!EditorUtility.DisplayDialog(
                        "Smart Media Platform",
                        "Phase3AutoPlayRecoveryDemoScene が見つかりません。新しく作成しますか?",
                        "作成する", "キャンセル"))
                {
                    return;
                }
                CreateDemoScene();
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/Smart Media Platform/Create Phase3-4 Auto Play Recovery Demo Scene (再生成)")]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("Phase3AutoPlayRecoveryDemo");
            go.AddComponent<AutoPlayRecoveryConsoleDemo>();

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log(
                "[Phase3AutoPlayRecoverySceneBuilder] DemoScene を作成しました: "
                + ScenePath + "\n"
                + "Play を押すと Console に Ended / Error / Timeout / 耐久 の結果が出力されます。");
        }

#if VRC_SDK_VRCSDK3
        private const string SdkScenePath = SceneFolder + "/Phase3VRChatAutoPlayRecoveryScene.unity";
        private const string SdkMenuPath =
            "Tools/Smart Media Platform/Create Phase3-4 VRChat SDK Auto Play Recovery Scene (実際に再生)";

        /// <summary>何本に 1 本の URL を<b>わざと焼かない</b>か(失敗を起こすため)。</summary>
        private const int SkipEveryNthUrl = 3;

        [MenuItem(SdkMenuPath)]
        public static void CreateSdkScene()
        {
            // 0. 先に UdonSharp のプログラム(.asset)を用意する。
            //    シーンを作る前にやるのが要点 — コンパイル前にコンポーネントを置くと
            //    「the U# program asset on this component is null」の壊れた状態が
            //    シーンに保存されてしまう。
            var report = UdonSharpProgramAssetFactory.EnsureProgramAssets(
                typeof(UdonVRCVideoEventRelay));

            if (report.HasCreated && !UdonSharpProgramAssetFactory.TryCompile())
            {
                string notice = UdonSharpSceneUtility.RecompileInstruction(SdkMenuPath);
                Debug.LogWarning("[Phase3AutoPlayRecoverySceneBuilder] " + notice);
                EditorUtility.DisplayDialog("Smart Media Platform", notice, "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // 1. VRCSceneDescriptor(これが無いと ClientSim が起動せず Udon が動かない)
            var world = UdonSharpSceneUtility.EnsureSceneDescriptor();

            // 2. 映像の出力先
            var screen = VRChatVideoPlayerFactory.CreateScreen();

            // 3. 動画プレイヤー + Phase3-2 のイベント経路
            var playerObject = new GameObject("VRCVideoPlayer");
            var audioSource = playerObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 0f;
            // Phase4-4: 実機の標準は AVPro。切り替えは Host の Preferred Player。
            var built = VRChatVideoPlayerFactory.AddPlayer(
                playerObject, VideoPlayerPreference.AVPro, screen, audioSource);

            // UdonSharp は「プロキシ + UdonBehaviour + プログラム」の 3 点が揃って初めて動く。
            var relay = UdonSharpSceneUtility.AddUdonSharpComponent(
                playerObject, typeof(UdonVRCVideoEventRelay), out _);

            var host = playerObject.AddComponent<VRChatVideoBackendHost>();
            playerObject.AddComponent<UdonVideoEventPump>();

            // 4. Phase3-4 の再生制御
            var demo = playerObject.AddComponent<VRChatAutoPlayRecoverySceneDemo>();

            // 5. URL を「一部だけ」焼き込む(実行時には作れない)。
            //    焼かれていない動画に当たると Load が InvalidUrl で失敗し、
            //    Phase3-4 の立て直しが実機の経路で動くところを確認できる。
            IMediaCatalog catalog = new MediaCatalog(new VideoCatalogSource());
            var videos = catalog.FilterByType(MediaType.Video);

            var baked = new List<string>();
            var skipped = new List<string>();
            for (int i = 0; i < videos.Count; i++)
            {
                if (SkipEveryNthUrl > 0 && i % SkipEveryNthUrl == SkipEveryNthUrl - 1)
                {
                    skipped.Add(videos[i].Id);
                    continue;
                }
                baked.Add(videos[i].Url);
            }
            host.Urls.Bake(baked);

            EditorUtility.SetDirty(host);

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, SdkScenePath);
            AssetDatabase.Refresh();

            Debug.Log(
                "[Phase3AutoPlayRecoverySceneBuilder] SDK シーンを作成しました: "
                + SdkScenePath + "\n"
                + built.Report
                + $"  進行役         : {demo.GetType().Name}\n"
                + $"  Udon 中継     : {(relay != null ? relay.GetType().Name : "未追加")}\n"
                + $"  SceneDescriptor: {(world != null ? world.name : "未作成(ClientSim が起動しません)")}\n"
                + $"  U# プログラム : {report}\n"
                + $"  ベイク済み URL: {baked.Count} 件 / 全 {videos.Count} 本\n"
                + $"  わざと焼かなかった動画: {string.Join(", ", skipped)}\n"
                + "     → この動画に当たると InvalidUrl で失敗します。\n"
                + "       それでも再生が止まらないことが Phase3-4 の確認点です。\n"
                
                + "  ※ VideoCatalogSource の URL は架空のアドレスです。実際に映像を出すには\n"
                + "     実在する動画 URL に差し替えてから焼き直してください。"
                + (relay == null
                    ? "\n\n" + UdonSharpSceneUtility.ManualSetupInstruction(
                        playerObject, typeof(UdonVRCVideoEventRelay))
                    : ""));
        }

#endif
    }
}
#endif
