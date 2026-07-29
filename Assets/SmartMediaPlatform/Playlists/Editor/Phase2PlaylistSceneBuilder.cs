#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SmartMediaPlatform.Playlists.Demo;

namespace SmartMediaPlatform.Playlists.EditorTools
{
    /// <summary>
    /// Phase2-3 の DemoScene を用意するエディタツール。
    ///
    /// リポジトリには作成済みの <c>Phase2PlaylistDemoScene.unity</c> が入っているが、
    /// Unity のバージョン差でシーンが開けない場合や、スクリプト参照(GUID)が
    /// ずれた場合の復旧手段としてこれを使う。
    /// </summary>
    public static class Phase2PlaylistSceneBuilder
    {
        private const string SceneFolder = "Assets/SmartMediaPlatform/Playlists/Scenes";
        private const string ScenePath = SceneFolder + "/Phase2PlaylistDemoScene.unity";

        [MenuItem("Tools/Smart Media Platform/Open Phase2 Playlist Demo Scene")]
        public static void OpenDemoScene()
        {
            if (!File.Exists(ScenePath))
            {
                if (!EditorUtility.DisplayDialog(
                        "Smart Media Platform",
                        "Phase2PlaylistDemoScene が見つかりません。新しく作成しますか?",
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

        [MenuItem("Tools/Smart Media Platform/Create Phase2 Playlist Demo Scene (再生成)")]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("Phase2PlaylistDemo");
            go.AddComponent<PlaylistConsoleDemo>();

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[Phase2PlaylistSceneBuilder] DemoScene を作成しました: {ScenePath}\n"
                      + "Play を押すと Console にプレイリストと Auto Queue の動作が出力されます。");
        }
    }
}
#endif
