#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SmartMediaPlatform.Adapter.Demo;

namespace SmartMediaPlatform.Adapter.EditorTools
{
    /// <summary>
    /// Phase2-4 の DemoScene を用意するエディタツール。
    ///
    /// リポジトリには作成済みの <c>Phase2AdapterDemoScene.unity</c> が入っているが、
    /// Unity のバージョン差でシーンが開けない場合や、スクリプト参照(GUID)が
    /// ずれた場合の復旧手段としてこれを使う。
    /// </summary>
    public static class Phase2AdapterSceneBuilder
    {
        private const string SceneFolder = "Assets/SmartMediaPlatform/Adapter/Scenes";
        private const string ScenePath = SceneFolder + "/Phase2AdapterDemoScene.unity";

        [MenuItem("Tools/Smart Media Platform/Open Phase2 Adapter Demo Scene")]
        public static void OpenDemoScene()
        {
            if (!File.Exists(ScenePath))
            {
                if (!EditorUtility.DisplayDialog(
                        "Smart Media Platform",
                        "Phase2AdapterDemoScene が見つかりません。新しく作成しますか?",
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

        [MenuItem("Tools/Smart Media Platform/Create Phase2 Adapter Demo Scene (再生成)")]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("Phase2AdapterDemo");
            go.AddComponent<BackendAdapterConsoleDemo>();

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[Phase2AdapterSceneBuilder] DemoScene を作成しました: {ScenePath}\n"
                      + "Play を押すと Console に Backend Adapter の動作が出力されます。");
        }
    }
}
#endif
