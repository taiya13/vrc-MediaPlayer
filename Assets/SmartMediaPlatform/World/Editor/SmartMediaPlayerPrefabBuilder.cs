#if UNITY_EDITOR
using System.IO;
using System.Text;
using SmartMediaPlatform.Catalog;
using UnityEditor;
using UnityEngine;
#if VRC_SDK_VRCSDK3
using SmartMediaPlatform.Video.EditorTools;
using SmartMediaPlatform.Video.Udon;
using SmartMediaPlatform.Video.VRChat;
using SmartMediaPlatform.World.VRChat;
#endif

namespace SmartMediaPlatform.World.EditorTools
{
    /// <summary>
    /// <b>SmartMediaPlayer.prefab を作るエディタツール。</b>Phase5-1。
    ///
    /// <b>なぜ Prefab をリポジトリに同梱せず、ここで作るのか</b><br/>
    /// 実機版は VRChat SDK 固有のコンポーネント(AVPro の動画プレイヤー、
    /// UdonBehaviour、UdonSharp のプログラム)を含みます。これらは
    /// <b>SDK のバージョンごとに中身が変わる</b>ため、Prefab を固定で持つと
    /// 「SDK を更新したら壊れた」が起きます。
    /// Phase3-3 で実際に踏んだ問題(program asset が null)と同じ理由で、
    /// <b>その場で生成する</b>方式にしています。
    ///
    /// 作る Prefab は 2 種類です。
    /// <list type="bullet">
    /// <item><b>SmartMediaPlayer</b>(SDK 必須)… 実際に動画が出る。ワールドに置く用</item>
    /// <item><b>SmartMediaPlayer_NoSDK</b>(SDK 不要)… ログのみ。仕組みの確認用</item>
    /// </list>
    /// </summary>
    public static class SmartMediaPlayerPrefabBuilder
    {
        private const string PrefabFolder = "Assets/SmartMediaPlatform/World/Prefabs";
        private const string NoSdkPrefabPath = PrefabFolder + "/SmartMediaPlayer_NoSDK.prefab";

        private const string NoSdkMenuPath =
            "Tools/Smart Media Platform/SmartMediaPlayer を作る/エディタ確認用 (SDK 不要・ログのみ)";

        // ───────── SDK 不要版 ─────────

        [MenuItem(NoSdkMenuPath, false, -80)]
        public static void CreateNoSdkPrefab()
        {
            var root = BuildCommonStructure("SmartMediaPlayer_NoSDK", out var screen, out var player);

            // バックエンドはログのみ(SDK が無くても動く)
            player.AddComponent<DummyMediaBackendProvider>();

            var saved = SavePrefab(root, NoSdkPrefabPath);

            Debug.Log(
                "[SmartMediaPlayerPrefabBuilder] Prefab を作成しました: " + NoSdkPrefabPath + "\n"
                + "  Hierarchy へドラッグして Play すれば、選ぶ → Queue → 再生 → 次へ が触れます。\n"
                + "  ※ ログのみで音も映像も出ません。実機で鳴らすには SDK 版を使ってください。",
                saved);

            Select(saved);
        }

#if VRC_SDK_VRCSDK3
        private const string PrefabPath = PrefabFolder + "/SmartMediaPlayer.prefab";

        private const string MenuPath =
            "Tools/Smart Media Platform/SmartMediaPlayer を作る/エディタ確認用 (AVPro)";

        private const string UnityPlayerMenuPath =
            "Tools/Smart Media Platform/SmartMediaPlayer を作る/エディタ確認用 (Unity Video)";

        [MenuItem(MenuPath, false, -82)]
        public static void CreatePrefab()
        {
            Build(VideoPlayerPreference.AVPro, MenuPath);
        }

        /// <summary>
        /// AVPro はエディタ(ClientSim)で映像を出さないため、
        /// <b>エディタで絵を確認したいとき用</b>の Unity 版も用意しておきます。
        /// </summary>
        [MenuItem(UnityPlayerMenuPath, false, -81)]
        public static void CreateUnityPlayerPrefab()
        {
            Build(VideoPlayerPreference.Unity, UnityPlayerMenuPath);
        }

        private static void Build(VideoPlayerPreference preference, string menuPath)
        {
            // 0. UdonSharp のプログラム(.asset)を先に用意する。
            //    コンパイル前にコンポーネントを置くと
            //    「the U# program asset on this component is null」の壊れた状態が
            //    Prefab に保存されてしまう(Phase3-3 で踏んだ問題)。
            var report = UdonSharpProgramAssetFactory.EnsureProgramAssets(
                typeof(UdonVRCVideoEventRelay));

            if (report.HasCreated && !UdonSharpProgramAssetFactory.TryCompile())
            {
                string notice = UdonSharpSceneUtility.RecompileInstruction(menuPath);
                Debug.LogWarning("[SmartMediaPlayerPrefabBuilder] " + notice);
                EditorUtility.DisplayDialog("Smart Media Platform", notice, "OK");
                return;
            }

            var root = BuildCommonStructure("SmartMediaPlayer", out var screen, out var player);

            // ── 動画プレイヤー(既定は AVPro)と、画面 / 音の配線
            var surface = screen.GetComponentInChildren<Renderer>();
            var speaker = screen.GetComponentInChildren<AudioSource>();

            var built = VRChatVideoPlayerFactory.AddPlayer(player, preference, surface, speaker);
            if (!built.Ok)
            {
                Debug.LogError(
                    "[SmartMediaPlayerPrefabBuilder] 動画プレイヤーを置けませんでした。\n"
                    + built.Report);
                Object.DestroyImmediate(root);
                return;
            }

            // ── 実機イベントの経路(Phase3-2)
            var relay = UdonSharpSceneUtility.AddUdonSharpComponent(
                player, typeof(UdonVRCVideoEventRelay), out _);
            player.AddComponent<UdonVideoEventPump>();

            // ── Backend の入れ物 + それを包む Provider
            var host = player.AddComponent<VRChatVideoBackendHost>();
            host.SetPreferredPlayer(preference);
            player.AddComponent<VRChatMediaBackendProvider>();

            // ── URL の焼き込み(実行時に VRCUrl は作れない)
            IMediaCatalog catalog = DefaultCatalogProvider.CreateDefaultCatalog();
            int baked = host.BakeUrlsFrom(catalog);

            var saved = SavePrefab(root, PrefabPath);

            var sb = new StringBuilder();
            sb.AppendLine("[SmartMediaPlayerPrefabBuilder] Prefab を作成しました: " + PrefabPath);
            sb.Append(built.Report);
            sb.AppendLine($"  Udon 中継     : {(relay != null ? relay.GetType().Name : "未追加")}");
            sb.AppendLine($"  U# プログラム : {report}");
            sb.AppendLine($"  焼き込み URL  : {baked} 件");
            sb.AppendLine();
            sb.AppendLine("  ワールドへの置き方:");
            sb.AppendLine("    1. Prefab を Hierarchy へドラッグ");
            sb.AppendLine("    2. Screen/Surface を見える位置へ動かす");
            sb.AppendLine("    3. Play(ClientSim)で確認");
            sb.AppendLine();
            sb.AppendLine("  ※ 同梱カタログの URL は架空のアドレスです。実際に映像を出すには");
            sb.AppendLine("     実在する URL に差し替えてから、Player を選んで");
            sb.AppendLine("     Tools > Smart Media Platform > Bake Catalog Urls Into Selected Video Host");

            if (relay == null)
            {
                sb.AppendLine();
                sb.Append(UdonSharpSceneUtility.ManualSetupInstruction(
                    player, typeof(UdonVRCVideoEventRelay)));
            }

            Debug.Log(sb.ToString(), saved);
            Select(saved);
        }
#endif

        // ───────── 共通の骨格 ─────────

        /// <summary>
        /// <b>SDK の有無にかかわらず同じ形</b>を作る。
        ///
        /// <code>
        /// SmartMediaPlayer          SmartMediaPlayerRoot(組み立てだけ)
        /// ├── Screen                MediaScreen          ← 差し替え可能
        /// │   └── Surface           Quad(Renderer) + AudioSource
        /// ├── Player                (バックエンドの Provider をここへ)  ← 差し替え可能
        /// ├── Controller            MediaController      ← 差し替え可能
        /// └── Catalog               DefaultCatalogProvider ← 差し替え可能
        /// </code>
        ///
        /// <b>UI はここには入りません。</b>Phase5-3 で IMGUI の <c>MediaPlayerUI</c> を
        /// 廃止したためです。操作 UI は Udon 側の <c>UdonMediaPanel</c>
        /// (World Space Canvas + uGUI)が担当し、ClientSim でも動きます。
        /// この Prefab は<b>組み立ての形を確かめるため</b>のものです。
        /// </summary>
        private static GameObject BuildCommonStructure(
            string rootName, out GameObject screen, out GameObject player)
        {
            var root = new GameObject(rootName);
            root.AddComponent<SmartMediaPlayerRoot>();

            // ── Screen(映像と音の出力先)
            screen = new GameObject("Screen");
            screen.transform.SetParent(root.transform, false);
            screen.AddComponent<MediaScreen>();

            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "Surface";
            surface.transform.SetParent(screen.transform, false);
            surface.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            surface.transform.localScale = new Vector3(3.2f, 1.8f, 1f);

            // 当たり判定は外す(ワールドで邪魔になるため)
            var collider = surface.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);

            var speaker = surface.AddComponent<AudioSource>();
            speaker.playOnAwake = false;
            speaker.spatialBlend = 1f;      // ワールドに置く前提なので 3D
            speaker.maxDistance = 25f;

            // ── Player(バックエンド)
            player = new GameObject("Player");
            player.transform.SetParent(root.transform, false);

            // ── Controller(操作の窓口)
            var controller = new GameObject("Controller");
            controller.transform.SetParent(root.transform, false);
            controller.AddComponent<MediaController>();

            // ── Catalog(Catalog Builder の差し込み口)
            var catalog = new GameObject("Catalog");
            catalog.transform.SetParent(root.transform, false);
            catalog.AddComponent<DefaultCatalogProvider>();

            return root;
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            Directory.CreateDirectory(PrefabFolder);

            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();

            return saved;
        }

        private static void Select(GameObject prefab)
        {
            if (prefab == null) return;

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
        }
    }
}
#endif
