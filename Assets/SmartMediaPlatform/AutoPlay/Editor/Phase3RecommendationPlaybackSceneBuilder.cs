#if UNITY_EDITOR
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
    /// Phase3-3(Recommendation Playback Integration)の DemoScene を用意するエディタツール。
    ///
    /// リポジトリには作成済みの <c>Phase3RecommendationPlaybackDemoScene.unity</c> が
    /// 入っているが、Unity のバージョン差でシーンが開けない場合や、
    /// スクリプト参照(GUID)がずれた場合の復旧手段としてこれを使う。
    ///
    /// VRChat SDK で<b>実際に動画を再生しながら</b>おすすめループを回すシーンは、
    /// <c>Tools > Smart Media Platform > Create Phase3-3 VRChat SDK Auto Play Scene</c>
    /// (SDK 導入時のみ表示)で生成できる。
    /// </summary>
    public static class Phase3RecommendationPlaybackSceneBuilder
    {
        private const string SceneFolder = "Assets/SmartMediaPlatform/AutoPlay/Scenes";
        private const string ScenePath = SceneFolder + "/Phase3RecommendationPlaybackDemoScene.unity";

        [MenuItem("Tools/Smart Media Platform/Open Phase3-3 Recommendation Playback Demo Scene")]
        public static void OpenDemoScene()
        {
            if (!File.Exists(ScenePath))
            {
                if (!EditorUtility.DisplayDialog(
                        "Smart Media Platform",
                        "Phase3RecommendationPlaybackDemoScene が見つかりません。新しく作成しますか?",
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

        [MenuItem("Tools/Smart Media Platform/Create Phase3-3 Recommendation Playback Demo Scene (再生成)")]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("Phase3RecommendationPlaybackDemo");
            go.AddComponent<RecommendationPlaybackConsoleDemo>();

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log(
                "[Phase3RecommendationPlaybackSceneBuilder] DemoScene を作成しました: "
                + ScenePath + "\n"
                + "Play を押すと Console におすすめ再生ループの動作が出力されます。");
        }

#if VRC_SDK_VRCSDK3
        private const string SdkScenePath = SceneFolder + "/Phase3VRChatAutoPlayScene.unity";
        private const string SdkMenuPath =
            "Tools/Smart Media Platform/Create Phase3-3 VRChat SDK Auto Play Scene (実際に再生)";

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
                Debug.LogWarning("[Phase3RecommendationPlaybackSceneBuilder] " + notice);
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
            // 素の AddComponent<T>() ではプロキシしか付かず、
            // 「Unable to find valid U# program asset」になる。
            var relay = UdonSharpSceneUtility.AddUdonSharpComponent(
                playerObject, typeof(UdonVRCVideoEventRelay), out _);

            var host = playerObject.AddComponent<VRChatVideoBackendHost>();
            playerObject.AddComponent<UdonVideoEventPump>();

            // 4. Phase3-3 のおすすめ再生
            var demo = playerObject.AddComponent<VRChatAutoPlaySceneDemo>();

            // 5. 動画カタログの URL を VRCUrl へ焼き込む(実行時には作れない)
            IMediaCatalog catalog = new MediaCatalog(new VideoCatalogSource());
            int baked = host.BakeUrlsFrom(catalog);

            EditorUtility.SetDirty(host);

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, SdkScenePath);
            AssetDatabase.Refresh();

            Debug.Log(
                "[Phase3RecommendationPlaybackSceneBuilder] SDK シーンを作成しました: "
                + SdkScenePath + "\n"
                + built.Report
                + $"  進行役         : {demo.GetType().Name}\n"
                + $"  Udon 中継     : {(relay != null ? relay.GetType().Name : "未追加")}\n"
                + $"  SceneDescriptor: {(world != null ? world.name : "未作成(ClientSim が起動しません)")}\n"
                + $"  U# プログラム : {report}\n"
                + $"  ベイク済み URL: {baked} 件(VideoCatalogSource の動画 10 本)\n"
                
                + "  Play を押すと、おすすめだけで動画が次々に切り替わる様子が Console に出ます。\n"
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
