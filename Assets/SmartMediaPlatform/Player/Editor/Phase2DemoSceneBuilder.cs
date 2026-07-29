#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SmartMediaPlatform.Player.Demo;

namespace SmartMediaPlatform.Player.EditorTools
{
    /// <summary>
    /// Phase2-2 の DemoScene を用意するエディタツール。
    ///
    /// リポジトリには作成済みの <c>Phase2PlaybackDemoScene.unity</c> が入っているが、
    /// Unity のバージョン差でシーンが開けない場合や、スクリプト参照(GUID)が
    /// ずれた場合の復旧手段としてこれを使う。
    /// </summary>
    public static class Phase2DemoSceneBuilder
    {
        private const string SceneFolder = "Assets/SmartMediaPlatform/Player/Scenes";
        private const string ScenePath = SceneFolder + "/Phase2PlaybackDemoScene.unity";

        [MenuItem("Tools/Smart Media Platform/Open Phase2 Playback Demo Scene")]
        public static void OpenDemoScene()
        {
            if (!File.Exists(ScenePath))
            {
                if (!EditorUtility.DisplayDialog(
                        "Smart Media Platform",
                        "Phase2PlaybackDemoScene が見つかりません。新しく作成しますか?",
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

        [MenuItem("Tools/Smart Media Platform/Create Phase2 Playback Demo Scene (再生成)")]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("Phase2PlaybackDemo");
            // RequireComponent により AudioBackendHost と AudioSource も付く。
            go.AddComponent<PlaybackConsoleDemo>();

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[Phase2DemoSceneBuilder] DemoScene を作成しました: {ScenePath}\n"
                      + "Play を押すと Console に再生制御の一連の動作が出力されます(音も鳴ります)。");
        }
    }
}
#endif
