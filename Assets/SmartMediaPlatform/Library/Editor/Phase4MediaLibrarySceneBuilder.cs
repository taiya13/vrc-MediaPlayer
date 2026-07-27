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
    }
}
#endif
