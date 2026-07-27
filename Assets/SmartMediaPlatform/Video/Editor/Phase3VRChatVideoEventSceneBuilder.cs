#if UNITY_EDITOR
using System.IO;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Video.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if VRC_SDK_VRCSDK3
using SmartMediaPlatform.Video.Udon;
using SmartMediaPlatform.Video.VRChat;
using VRC.SDK3.Video.Components;
#endif

namespace SmartMediaPlatform.Video.EditorTools
{
    /// <summary>
    /// Phase3-2(VRChat Video Event Bridge)の DemoScene を用意するエディタツール。
    ///
    /// Phase3-1 と同じく 2 種類あります。
    /// <list type="bullet">
    /// <item>
    /// <b>Phase3VRChatVideoEventDemoScene</b>(リポジトリ同梱・SDK 不要)…
    /// <see cref="VRChatVideoEventBridgeConsoleDemo"/> を置いただけのシーン。
    /// イベントを手で流し込むので、SDK が無くても重複排除と Tick 調停を確認できます。
    /// </item>
    /// <item>
    /// <b>Phase3VRChatSdkVideoEventScene</b>(SDK 必須・メニューから生成)…
    /// 本物の <c>VRCUnityVideoPlayer</c> + <c>UdonVRCVideoEventRelay</c>(Udon 中継) +
    /// <c>UdonVideoEventPump</c>(運搬役) を置いたシーン。
    /// <b>実機のイベントが Backend へ届くこと</b>を確認できます。
    /// </item>
    /// </list>
    /// </summary>
    public static class Phase3VRChatVideoEventSceneBuilder
    {
        private const string SceneFolder = "Assets/SmartMediaPlatform/Video/Scenes";
        private const string ConsoleScenePath = SceneFolder + "/Phase3VRChatVideoEventDemoScene.unity";
        private const string SdkScenePath = SceneFolder + "/Phase3VRChatSdkVideoEventScene.unity";

        [MenuItem("Tools/Smart Media Platform/Open Phase3-2 Video Event Demo Scene")]
        public static void OpenDemoScene()
        {
            if (!File.Exists(ConsoleScenePath))
            {
                if (!EditorUtility.DisplayDialog(
                        "Smart Media Platform",
                        "Phase3VRChatVideoEventDemoScene が見つかりません。新しく作成しますか?",
                        "作成する", "キャンセル"))
                {
                    return;
                }
                CreateDemoScene();
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ConsoleScenePath);
        }

        [MenuItem("Tools/Smart Media Platform/Create Phase3-2 Video Event Demo Scene (再生成)")]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("Phase3VRChatVideoEventDemo");
            go.AddComponent<VRChatVideoEventBridgeConsoleDemo>();

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, ConsoleScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[Phase3VRChatVideoEventSceneBuilder] DemoScene を作成しました: {ConsoleScenePath}\n"
                      + "Play を押すと Console にイベントブリッジの動作が出力されます。");
        }

#if VRC_SDK_VRCSDK3
        [MenuItem("Tools/Smart Media Platform/Create Phase3-2 VRChat SDK Video Event Scene (実機イベント)")]
        public static void CreateSdkScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // 1. 映像の出力先
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "VideoScreen";
            screen.transform.position = new Vector3(0f, 1.5f, 3f);
            screen.transform.localScale = new Vector3(3.2f, 1.8f, 1f);

            // 2. 動画プレイヤー本体 + イベントの受け口
            //    VRChat はイベントを「同じ GameObject の UdonBehaviour」へ送るので、
            //    中継(UdonSharp)は必ずこの GameObject に置くこと。
            var playerObject = new GameObject("VRCVideoPlayer");
            var audioSource = playerObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 0f;
            var videoPlayer = playerObject.AddComponent<VRCUnityVideoPlayer>();

            // UdonSharp は「プロキシ + UdonBehaviour + プログラム」の 3 点が揃って初めて動く。
            // 素の AddComponent<T>() ではプロキシしか付かず、
            // 「Unable to find valid U# program asset」になる。
            var relay = UdonSharpSceneUtility.AddUdonSharpComponent(
                playerObject, typeof(UdonVRCVideoEventRelay));

            // 3. Backend + ブリッジ + 運搬役 + 進行役
            var host = playerObject.AddComponent<VRChatVideoBackendHost>();
            var pump = playerObject.AddComponent<UdonVideoEventPump>();
            var demo = playerObject.AddComponent<VRChatVideoEventSceneDemo>();

            // 4. Catalog の URL を VRCUrl へ焼き込む(実行時には作れない)
            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());
            int baked = host.BakeUrlsFrom(catalog);

            EditorUtility.SetDirty(host);

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, SdkScenePath);
            AssetDatabase.Refresh();

            Debug.Log(
                $"[Phase3VRChatVideoEventSceneBuilder] SDK シーンを作成しました: {SdkScenePath}\n"
                + $"  {videoPlayer.GetType().Name} / {pump.GetType().Name} / {demo.GetType().Name}\n"
                + $"  Udon 中継: {(relay != null ? relay.GetType().Name : "未追加")}\n"
                + $"  ベイク済み URL: {baked} 件\n"
                + $"  映像の出力先({screen.name})に RenderTexture / Material を割り当ててください。\n"
                + "  Play を押すと OnVideoReady / OnVideoStart / OnVideoEnd / OnVideoError の\n"
                + "  受理・棄却の回数が Console に出ます。\n"
                + (relay == null
                    ? "\n" + UdonSharpSceneUtility.ManualSetupInstruction(
                        playerObject, typeof(UdonVRCVideoEventRelay))
                    : ""));
        }

#endif
    }
}
#endif
