#if UNITY_EDITOR
using System.IO;
using SmartMediaPlatform.Library.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SmartMediaPlatform.Library.EditorTools
{
    /// <summary>
    /// Phase4-1(Media Library)の DemoScene を用意するエディタツール。
    ///
    /// リポジトリには作成済みの <c>Phase4MediaLibraryDemoScene.unity</c> が入っているが、
    /// Unity のバージョン差でシーンが開けない場合や、
    /// スクリプト参照(GUID)がずれた場合の復旧手段としてこれを使う。
    ///
    /// シーンには 2 つのデモが載る。
    /// <list type="bullet">
    /// <item><c>MediaLibraryScreenDemo</c> … Game ビューに一覧を描き、クリックで選べる</item>
    /// <item><c>MediaLibraryConsoleDemo</c> … 同じことを Console にまとめて出す</item>
    /// </list>
    /// どちらも VRChat SDK を必要としない。
    /// </summary>
    public static class Phase4MediaLibrarySceneBuilder
    {
        private const string SceneFolder = "Assets/SmartMediaPlatform/Library/Scenes";
        private const string ScenePath = SceneFolder + "/Phase4MediaLibraryDemoScene.unity";

        [MenuItem("Tools/Smart Media Platform/Open Phase4-1 Media Library Demo Scene")]
        public static void OpenDemoScene()
        {
            if (!File.Exists(ScenePath))
            {
                if (!EditorUtility.DisplayDialog(
                        "Smart Media Platform",
                        "Phase4MediaLibraryDemoScene が見つかりません。新しく作成しますか?",
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

        [MenuItem("Tools/Smart Media Platform/Create Phase4-1 Media Library Demo Scene (再生成)")]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            // Game ビューに描くので、カメラのある既定のシーンから作る
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var go = new GameObject("Phase4MediaLibraryDemo");
            go.AddComponent<MediaLibraryScreenDemo>();
            go.AddComponent<MediaLibraryConsoleDemo>();

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log(
                "[Phase4MediaLibrarySceneBuilder] DemoScene を作成しました: " + ScenePath + "\n"
                + "  Play を押すと Game ビューに一覧が出ます(クリックで選択 → 「▶ この曲を再生する」)。\n"
                + "  同時に Console へ Media Library の動作がまとめて出力されます。");
        }

        // ───────── Phase4-3: Library → Queue → Player ─────────

        private const string FlowScenePath = SceneFolder + "/Phase4PlaybackFlowDemoScene.unity";

        [MenuItem("Tools/Smart Media Platform/Open Phase4-3 Playback Flow Demo Scene")]
        public static void OpenFlowScene()
        {
            if (!File.Exists(FlowScenePath))
            {
                if (!EditorUtility.DisplayDialog(
                        "Smart Media Platform",
                        "Phase4PlaybackFlowDemoScene が見つかりません。新しく作成しますか?",
                        "作成する", "キャンセル"))
                {
                    return;
                }
                CreateFlowScene();
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(FlowScenePath);
        }

        [MenuItem("Tools/Smart Media Platform/Create Phase4-3 Playback Flow Demo Scene (再生成)")]
        public static void CreateFlowScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var go = new GameObject("Phase4PlaybackFlowDemo");
            go.AddComponent<PlaybackFlowScreenDemo>();
            go.AddComponent<PlaybackFlowConsoleDemo>();

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, FlowScenePath);
            AssetDatabase.Refresh();

            Debug.Log(
                "[Phase4MediaLibrarySceneBuilder] Playback Flow の DemoScene を作成しました: "
                + FlowScenePath + "\n"
                + "  Play を押すと Library / 関連動画 / Queue / いま再生中 が並びます。\n"
                + "  左で選んで「▶ 再生」→ Queue に積まれ、「曲を終わらせる(Ended)」で次へ進みます。\n"
                + "  同時に Console へ一連の流れがまとめて出力されます。");
        }

        // ───────── Phase4-4: VideoBackend まで通す(SDK 不要) ─────────

        private const string VideoScenePath = SceneFolder + "/Phase4VideoBackendFlowDemoScene.unity";

        [MenuItem("Tools/Smart Media Platform/Open Phase4-4 Video Backend Flow Demo Scene (SDK 不要)")]
        public static void OpenVideoBackendScene()
        {
            if (!File.Exists(VideoScenePath))
            {
                if (!EditorUtility.DisplayDialog(
                        "Smart Media Platform",
                        "Phase4VideoBackendFlowDemoScene が見つかりません。新しく作成しますか?",
                        "作成する", "キャンセル"))
                {
                    return;
                }
                CreateVideoBackendScene();
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(VideoScenePath);
        }

        [MenuItem("Tools/Smart Media Platform/Create Phase4-4 Video Backend Flow Demo Scene (再生成)")]
        public static void CreateVideoBackendScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("Phase4VideoBackendFlowDemo");
            go.AddComponent<VideoBackendFlowConsoleDemo>();

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, VideoScenePath);
            AssetDatabase.Refresh();

            Debug.Log(
                "[Phase4MediaLibrarySceneBuilder] DemoScene を作成しました: " + VideoScenePath + "\n"
                + "  Play を押すと Library → Queue → Player → VideoBackend の通しが Console に出ます。\n"
                + "  VRChat SDK は不要です(動画プレイヤーの位置に SimulatedVRCVideoPlayer が入ります)。");
        }
    }
}
#endif
