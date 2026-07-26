#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SmartMediaPlatform.Integration.Demo;

namespace SmartMediaPlatform.Integration.EditorTools
{
    /// <summary>
    /// Phase1 の DemoScene を用意するエディタツール。
    ///
    /// リポジトリには作成済みの <c>Phase1DemoScene.unity</c> が入っているが、
    /// Unity のバージョン差でシーンファイルが開けない場合や、
    /// スクリプト参照(GUID)がずれてしまった場合の復旧手段としてこれを使う。
    /// シーンを Unity 自身に作らせるので、どのバージョンでも確実に動く。
    /// </summary>
    public static class Phase1DemoSceneBuilder
    {
        private const string SceneFolder = "Assets/SmartMediaPlatform/Integration/Scenes";
        private const string ScenePath = SceneFolder + "/Phase1DemoScene.unity";

        [MenuItem("Tools/Smart Media Platform/Open Phase1 Demo Scene")]
        public static void OpenDemoScene()
        {
            if (!File.Exists(ScenePath))
            {
                if (!EditorUtility.DisplayDialog(
                        "Smart Media Platform",
                        "Phase1DemoScene が見つかりません。新しく作成しますか?",
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

        [MenuItem("Tools/Smart Media Platform/Create Phase1 Demo Scene (再生成)")]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("Phase1IntegrationDemo");
            go.AddComponent<Phase1IntegrationDemo>();

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[Phase1DemoSceneBuilder] DemoScene を作成しました: {ScenePath}\n"
                      + "Play を押すと Console に統合テストの結果が出力されます。");
        }

        /// <summary>
        /// シーンを開かずに統合テストだけ実行する。Play を押す必要すらない確認用。
        /// </summary>
        [MenuItem("Tools/Smart Media Platform/Run Phase1 Integration Test (Console)")]
        public static void RunIntegrationTest()
        {
            var report = new Phase1IntegrationScenario().Run();
            int total = report.PassedCount + report.FailedCount;

            Debug.Log("===== Phase1 Integration Test / Smart Media Platform =====\n"
                      + "Catalog -> Recommendation -> Queue -> Backend\n"
                      + "(実際の再生は行いません / no actual playback)\n"
                      + report);

            if (report.AllPassed)
            {
                Debug.Log($"[Phase1 Integration] SUCCESS — {report.PassedCount}/{total} conditions passed");
            }
            else
            {
                Debug.LogError($"[Phase1 Integration] FAILED — {report.PassedCount}/{total} conditions passed");
            }
        }
    }
}
#endif
