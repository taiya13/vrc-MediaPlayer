#if UNITY_EDITOR
using System.IO;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Video.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if VRC_SDK_VRCSDK3
using SmartMediaPlatform.Video.VRChat;
using VRC.SDK3.Video.Components;
#endif

namespace SmartMediaPlatform.Video.EditorTools
{
    /// <summary>
    /// Phase3-1(VRChat Video Backend)の DemoScene を用意するエディタツール。
    ///
    /// シーンは 2 種類あります。
    /// <list type="bullet">
    /// <item>
    /// <b>Phase3VRChatVideoDemoScene</b>(リポジトリ同梱・SDK 不要)…
    /// <see cref="VRChatVideoBackendConsoleDemo"/> を置いただけのシーン。
    /// 動画プレイヤーの位置に <see cref="SimulatedVRCVideoPlayer"/> が入るので、
    /// SDK が無い環境でも Console で全シナリオを確認できます。
    /// </item>
    /// <item>
    /// <b>Phase3VRChatSdkVideoScene</b>(SDK 必須・メニューから生成)…
    /// 本物の <c>VRCUnityVideoPlayer</c> を置き、Catalog の URL を <c>VRCUrl</c> へ焼き込んだ
    /// シーン。<b>実際に動画が再生されます。</b>
    /// SDK 固有のコンポーネントを含むため、リポジトリには同梱せずここで生成します
    /// (SDK のバージョン差でシーンが壊れるのを避けるため)。
    /// </item>
    /// </list>
    /// </summary>
    public static class Phase3VRChatVideoSceneBuilder
    {
        private const string SceneFolder = "Assets/SmartMediaPlatform/Video/Scenes";
        private const string ConsoleScenePath = SceneFolder + "/Phase3VRChatVideoDemoScene.unity";
        private const string SdkScenePath = SceneFolder + "/Phase3VRChatSdkVideoScene.unity";

        [MenuItem("Tools/Smart Media Platform/Open Phase3 VRChat Video Demo Scene")]
        public static void OpenDemoScene()
        {
            if (!File.Exists(ConsoleScenePath))
            {
                if (!EditorUtility.DisplayDialog(
                        "Smart Media Platform",
                        "Phase3VRChatVideoDemoScene が見つかりません。新しく作成しますか?",
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

        [MenuItem("Tools/Smart Media Platform/Create Phase3 VRChat Video Demo Scene (再生成)")]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("Phase3VRChatVideoDemo");
            go.AddComponent<VRChatVideoBackendConsoleDemo>();

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, ConsoleScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[Phase3VRChatVideoSceneBuilder] DemoScene を作成しました: {ConsoleScenePath}\n"
                      + "Play を押すと Console に VRChat Video Backend の動作が出力されます。");
        }

#if VRC_SDK_VRCSDK3
        [MenuItem("Tools/Smart Media Platform/Create Phase3 VRChat SDK Video Scene (実際に再生)")]
        public static void CreateSdkScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // 1. 映像の出力先
            var screen = VRChatVideoPlayerFactory.CreateScreen();

            // 2. VRChat の動画プレイヤー(Phase4-4 で AVPro が既定)+ 音声出力
            var playerObject = new GameObject("VRCVideoPlayer");
            var audioSource = playerObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 0f;
            var built = VRChatVideoPlayerFactory.AddPlayer(
                playerObject, VideoPlayerPreference.AVPro, screen, audioSource);

            // 3. Backend のホスト(同じ GameObject に置く)
            var host = playerObject.AddComponent<VRChatVideoBackendHost>();
            var demo = playerObject.AddComponent<VRChatVideoSceneDemo>();

            // 4. Catalog の URL を VRCUrl へ焼き込む(実行時には作れないので今ここで)
            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());
            int baked = host.BakeUrlsFrom(catalog);

            EditorUtility.SetDirty(host);

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, SdkScenePath);
            AssetDatabase.Refresh();

            Debug.Log(
                $"[Phase3VRChatVideoSceneBuilder] SDK シーンを作成しました: {SdkScenePath}\n"
                + built.Report
                + $"  進行役        : {demo.GetType().Name}\n"
                + $"  ベイク済み URL: {baked} 件\n"
                + "  Play を押すと Load → Play → Pause → Resume → Stop → Ended → Error を Console に出力します。\n"
                + "  Unity 版で試す場合は VRChatVideoBackendHost の Preferred Player を Unity にしてください。");
        }

        [MenuItem("Tools/Smart Media Platform/Bake Catalog Urls Into Selected Video Host")]
        private static void BakeUrlsIntoSelected()
        {
            var go = Selection.activeGameObject;
            var host = go != null ? go.GetComponent<VRChatVideoBackendHost>() : null;
            if (host == null)
            {
                EditorUtility.DisplayDialog(
                    "Smart Media Platform",
                    "VRChatVideoBackendHost を持つ GameObject を選択してから実行してください。",
                    "OK");
                return;
            }

            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());
            host.BakeUrlsFrom(catalog);

            EditorUtility.SetDirty(host);
            EditorSceneManager.MarkSceneDirty(host.gameObject.scene);
        }

        [MenuItem("Tools/Smart Media Platform/Bake Catalog Urls Into Selected Video Host", validate = true)]
        private static bool BakeUrlsIntoSelectedValidate()
        {
            var go = Selection.activeGameObject;
            return go != null && go.GetComponent<VRChatVideoBackendHost>() != null;
        }
#endif
    }
}
#endif
