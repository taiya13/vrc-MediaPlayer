#if UNITY_EDITOR
using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.World.EditorTools
{
    /// <summary>
    /// <b>入口。</b><c>Tools > Smart Media Platform > セットアップ</c>。
    ///
    /// <b>なぜ要るのか</b><br/>
    /// メニューが 15 個以上に増えて、
    /// <b>どれを使えばワールドで動くのかが分からなくなりました</b>。
    /// 実際、Prefabs フォルダに置いてある <c>SmartMediaPlayer_NoSDK.prefab</c>
    /// (エディタ確認用・音も映像も出ない)を掴んでしまう事故が起きています。
    ///
    /// この窓は<b>やることを上から順に並べるだけ</b>です。新しい機能はありません。
    /// </summary>
    public sealed class SmartMediaPlatformSetupWindow : EditorWindow
    {
        private const string MenuPath = "Tools/Smart Media Platform/セットアップ";

        private Vector2 _scroll;

        [MenuItem(MenuPath, false, -100)]
        public static void Open()
        {
            var window = GetWindow<SmartMediaPlatformSetupWindow>("Smart Media Platform");
            window.minSize = new Vector2(420f, 480f);
            window.Show();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Smart Media Platform — セットアップ", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            DrawSdkStatus();
            EditorGUILayout.Space();

            DrawStep1();
            EditorGUILayout.Space();
            DrawStepPanels();
            EditorGUILayout.Space();
            DrawStep2();
            EditorGUILayout.Space();
            DrawStep3();
            EditorGUILayout.Space();
            DrawTrouble();

            EditorGUILayout.EndScrollView();
        }

        // ───────── SDK の有無 ─────────

        private static bool HasSdk
        {
            get
            {
#if VRC_SDK_VRCSDK3
                return true;
#else
                return false;
#endif
            }
        }

        private void DrawSdkStatus()
        {
            if (HasSdk)
            {
                EditorGUILayout.HelpBox("VRChat SDK(Worlds)を検出しました。", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                "VRChat SDK(Worlds)が見つかりません。\n"
                + "ワールドで動かすには SDK が必要です。VCC から導入してください。\n"
                + "(SDK なしでも、下の「エディタで仕組みを見る」だけは使えます)",
                MessageType.Warning);
        }

        // ───────── 手順 ─────────

        private void DrawStep1()
        {
            EditorGUILayout.LabelField("1. ワールド用の Prefab を作る", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "制御が Udon なので、アップロードしたワールドでも動きます。\n"
                + "1 回押すと Prefab が 3 つできます(本体 + パネル 2 種)。",
                EditorStyles.wordWrappedLabel);

            GUI.enabled = HasSdk;
            if (GUILayout.Button("Prefab を作る(AVPro・実機用)", GUILayout.Height(26f)))
            {
                Invoke("SmartMediaPlatform.World.EditorTools.UdonSmartMediaPlayerPrefabBuilder",
                       "CreatePrefab");
            }
            if (GUILayout.Button("Unity 版で作る(エディタで映像を確認したいとき)"))
            {
                Invoke("SmartMediaPlatform.World.EditorTools.UdonSmartMediaPlayerPrefabBuilder",
                       "CreateUnityPlayerPrefab");
            }
            GUI.enabled = true;

            EditorGUILayout.HelpBox(
                "SmartMediaPlayer.prefab を Hierarchy へドラッグし、\n"
                + "Screen/Surface と WallPanel を見える位置へ動かしてください。\n"
                + "※ Prefabs フォルダにある SmartMediaPlayer_NoSDK.prefab は\n"
                + "   エディタで仕組みを見るためのものです。音も映像も出ません。",
                MessageType.None);
        }

        private void DrawStepPanels()
        {
            EditorGUILayout.LabelField("1-b. 操作 UI を増やす(任意)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "壁パネル・リモコンは何枚でも置けます。手順は 2 つだけです。",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.HelpBox(
                "1. MediaWallPanel.prefab か MediaRemotePanel.prefab を Hierarchy へドラッグ\n"
                + "2. Inspector の Core に、シーンの SmartMediaPlayer を挿す\n"
                + "\n"
                + "残りの欄(Controller / Session / Store / Screen)は空のままで構いません。\n"
                + "実行時に Core から引いてきます。",
                MessageType.None);

            if (GUILayout.Button("Prefabs フォルダを開く"))
            {
                SelectPrefabFolder();
            }
        }

        private void DrawStep2()
        {
            EditorGUILayout.LabelField("2. URL を入れる", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Hierarchy で SmartMediaPlayer > Catalog を選ぶと、Inspector に\n"
                + "1 行 = 1 本の一覧が出ます。そこへ URL を書いて「反映する」を押してください。",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.HelpBox(
                "VRCUrl は実行時に作れません。ここで焼き込んだ URL だけが再生できます。\n"
                + "同梱サンプルの URL は架空のアドレスなので、そのままでは何も映りません。",
                MessageType.Warning);

            if (GUILayout.Button("シーンの Catalog を選ぶ"))
            {
                SelectCatalog();
            }
        }

        private void DrawStep3()
        {
            EditorGUILayout.LabelField("3. 確かめる", EditorStyles.boldLabel);

            if (GUILayout.Button("VRChat SDK のビルド画面を開く(Build & Test)"))
            {
                EditorApplication.ExecuteMenuItem("VRChat SDK/Show Control Panel");
            }

            if (GUILayout.Button("EditMode テストを開く"))
            {
                EditorApplication.ExecuteMenuItem("Window/General/Test Runner");
            }
        }

        // ───────── 困ったとき ─────────

        private void DrawTrouble()
        {
            EditorGUILayout.LabelField("困ったとき", EditorStyles.boldLabel);

            EditorGUILayout.LabelField(
                "「Source C# script … is null」「U# scripts have compile errors」が出るとき:",
                EditorStyles.wordWrappedLabel);

            GUI.enabled = HasSdk;
            if (GUILayout.Button("壊れた U# プログラムを修復する", GUILayout.Height(24f)))
            {
                Invoke("SmartMediaPlatform.Video.EditorTools.UdonSharpProgramAssetFactory",
                       "RepairBrokenProgramAssetsMenu");
            }
            if (GUILayout.Button("U# の状態を Console に出す"))
            {
                Invoke("SmartMediaPlatform.Video.EditorTools.UdonSharpProgramAssetFactory",
                       "DiagnoseMenu");
            }
            GUI.enabled = true;

            EditorGUILayout.HelpBox(
                "U# のプログラムは元の .cs を GUID で指しています。\n"
                + "スクリプトを .meta ごと入れ替えると行き先を失い、\n"
                + "U# はコンパイル全体を止めます(= 何も動かなくなります)。\n"
                + "上のボタンで繋ぎ直せます。",
                MessageType.None);
        }

        // ───────── 小道具 ─────────

        /// <summary>
        /// SDK 版のメニューは <c>#if VRC_SDK_VRCSDK3</c> の中にあるので、
        /// この窓からは<b>名前で呼びます</b>(SDK が無くてもこの窓はコンパイルできる)。
        /// </summary>
        private static void Invoke(string typeName, string methodName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName);
                if (type == null) continue;

                var method = type.GetMethod(
                    methodName,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method == null) continue;

                method.Invoke(null, null);
                return;
            }

            EditorUtility.DisplayDialog(
                "Smart Media Platform",
                typeName + "." + methodName + " が見つかりませんでした。\n"
                + "VRChat SDK(Worlds)が入っているか確認してください。",
                "OK");
        }

        private static void SelectPrefabFolder()
        {
            const string path = "Assets/SmartMediaPlatform/World/Prefabs";

            var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (folder == null)
            {
                EditorUtility.DisplayDialog(
                    "Smart Media Platform",
                    "Prefabs フォルダが見つかりません。\n先に手順 1 で Prefab を作ってください。",
                    "OK");
                return;
            }

            Selection.activeObject = folder;
            EditorGUIUtility.PingObject(folder);
        }

        private static void SelectCatalog()
        {
            var found = FindCatalogObject();
            if (found == null)
            {
                EditorUtility.DisplayDialog(
                    "Smart Media Platform",
                    "シーンに UdonMediaCatalog が見つかりません。\n"
                    + "先に手順 1 で Prefab を作って、Hierarchy へ置いてください。",
                    "OK");
                return;
            }

            Selection.activeGameObject = found;
            EditorGUIUtility.PingObject(found);
        }

        /// <summary>
        /// シーンから <c>UdonMediaCatalog</c> を探す。
        /// SDK が無い環境でもコンパイルできるよう、型は名前で解決します。
        /// </summary>
        private static GameObject FindCatalogObject()
        {
            Type catalogType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                catalogType = assembly.GetType("SmartMediaPlatform.Catalog.Udon.UdonMediaCatalog");
                if (catalogType != null) break;
            }
            if (catalogType == null) return null;

            var found = UnityEngine.Object.FindObjectOfType(catalogType) as Component;
            return found != null ? found.gameObject : null;
        }
    }
}
#endif
