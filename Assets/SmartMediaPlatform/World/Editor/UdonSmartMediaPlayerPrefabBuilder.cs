#if UNITY_EDITOR && VRC_SDK_VRCSDK3
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Udon;
using SmartMediaPlatform.Catalog.UdonEditor;
using SmartMediaPlatform.Recommendation.Udon;
using SmartMediaPlatform.Video.EditorTools;
using SmartMediaPlatform.Video.VRChat;
using SmartMediaPlatform.World.Udon;
using SmartMediaPlatform.World.Udon.UI;
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.World.EditorTools
{
    /// <summary>
    /// <b>実機で動く Prefab を作るエディタツール。</b>Phase5-2 → Phase5-3。
    ///
    /// <b>Phase5-3 で 3 つに分かれました。</b>
    /// <list type="bullet">
    /// <item><c>SmartMediaPlayer.prefab</c> …… 中身 + 壁パネル。<b>まずこれを 1 つ置く</b></item>
    /// <item><c>MediaWallPanel.prefab</c> …… 壁パネルだけ。2 枚目以降を別の場所に足す用</item>
    /// <item><c>MediaRemotePanel.prefab</c> …… 手持ちリモコン。最小構成の例</item>
    /// </list>
    /// パネル側は <c>Core</c> の欄に置いた <c>SmartMediaPlayer</c> を挿すだけで、
    /// <b>何枚でも、どこにでも</b>足せます。
    ///
    /// <b>Screen と操作 UI は分かれています。</b>
    /// <c>SmartMediaPlayer/Screen</c> はワールドの好きな場所へ動かせますし、
    /// パネルは Prefab ごと別の部屋に置けます。
    ///
    /// <b>なぜ Prefab を同梱せずここで作るのか</b>は Phase5-1 と同じ理由です。
    /// UdonSharp のプログラム(.asset)と UdonBehaviour は
    /// <b>SDK のバージョンごとに中身が変わる</b>ので、固定の Prefab を持つと
    /// 「SDK を更新したら program asset が null」になります(Phase3-3 で実際に踏んだ問題)。
    /// </summary>
    public static class UdonSmartMediaPlayerPrefabBuilder
    {
        private const string PrefabFolder = "Assets/SmartMediaPlatform/World/Prefabs";

        private const string PlayerPrefabPath = PrefabFolder + "/SmartMediaPlayer.prefab";
        private const string WallPanelPrefabPath = PrefabFolder + "/MediaWallPanel.prefab";
        private const string RemotePanelPrefabPath = PrefabFolder + "/MediaRemotePanel.prefab";

        private const string MenuPath =
            "Tools/Smart Media Platform/Create SmartMediaPlayer Prefab (VRChat 実機・Udon)";

        private const string UnityPlayerMenuPath =
            "Tools/Smart Media Platform/Create SmartMediaPlayer Prefab (VRChat 実機・Udon / Unity 版)";

        /// <summary>この Prefab が使う UdonSharpBehaviour。プログラムを先に作る対象。</summary>
        private static Type[] BehaviourTypes()
        {
            var types = new List<Type>
            {
                typeof(UdonMediaCatalog),
                typeof(UdonRecommendationEngine),
                typeof(UdonCatalogStore),
                typeof(UdonMediaScreen),
                typeof(UdonVideoBackend),
                typeof(UdonPlayerSession),
                typeof(UdonMediaController),
                typeof(UdonMediaControlButton),
                typeof(UdonSmartMediaPlayer),
            };

            types.AddRange(UdonMediaPanelBuilder.BehaviourTypes());
            return types.ToArray();
        }

        [MenuItem(MenuPath)]
        public static void CreatePrefab()
        {
            Build(VideoPlayerPreference.AVPro, MenuPath);
        }

        /// <summary>
        /// AVPro はエディタ(ClientSim)で映像を出さないので、
        /// <b>エディタで絵を確認したいとき用</b>の Unity 版も用意しておきます。
        /// </summary>
        [MenuItem(UnityPlayerMenuPath)]
        public static void CreateUnityPlayerPrefab()
        {
            Build(VideoPlayerPreference.Unity, UnityPlayerMenuPath);
        }

        private static void Build(VideoPlayerPreference preference, string menuPath)
        {
            _needsCompile = false;
            _bakedCount = 0;

            // 0. U# のプログラム(.asset)を全部先に用意する。
            //    コンパイル前にコンポーネントを置くと
            //    「the U# program asset on this component is null」の壊れた状態が
            //    Prefab に保存されてしまう(Phase3-3 で踏んだ問題)。
            var report = UdonSharpProgramAssetFactory.EnsureProgramAssets(BehaviourTypes());

            if (report.HasCreated && !UdonSharpProgramAssetFactory.TryCompile())
            {
                string notice = UdonSharpSceneUtility.RecompileInstruction(menuPath);
                Debug.LogWarning("[UdonSmartMediaPlayerPrefabBuilder] " + notice);
                EditorUtility.DisplayDialog("Smart Media Platform", notice, "OK");
                return;
            }

            UdonWorldUiKit.ResetBindFailures();
            UdonMediaPanelBuilder.ResetNeedsCompile();

            var log = new StringBuilder();

            // ── 1. 中身 + 壁パネル(これ 1 つ置けば動く)
            GameObject root = BuildCore(preference, log);
            if (root == null) return;

            var core = root.GetComponent<UdonSmartMediaPlayer>();

            UdonMediaPanel wall = UdonMediaPanelBuilder.BuildWallPanel(root, "WallPanel");
            if (Abort(root, menuPath)) return;

            if (wall != null)
            {
                wall.Core = core;
                wall.transform.localPosition = new Vector3(-2.6f, 1.0f, 0f);
            }

            var player = SavePrefab(root, PlayerPrefabPath);

            // ── 2. 壁パネル単体(2 枚目以降を別の場所に置く用)
            var wallRoot = new GameObject("MediaWallPanel");
            UdonMediaPanel standaloneWall = UdonMediaPanelBuilder.BuildWallPanel(wallRoot, "Panel");
            if (Abort(wallRoot, menuPath)) return;
            if (standaloneWall != null) standaloneWall.PanelName = "Smart Media Player";
            SavePrefab(wallRoot, WallPanelPrefabPath);

            // ── 3. 手持ちリモコン(最小構成の例)
            var remoteRoot = new GameObject("MediaRemotePanel");
            UdonMediaPanel remote = UdonMediaPanelBuilder.BuildRemotePanel(remoteRoot, "Panel");
            if (Abort(remoteRoot, menuPath)) return;
            if (remote != null) remote.PanelName = "リモコン";
            SavePrefab(remoteRoot, RemotePanelPrefabPath);

            Report(log, report, player, menuPath);

            Selection.activeObject = player;
            EditorGUIUtility.PingObject(player);
        }

        // ───────── 中身 ─────────

        /// <summary>
        /// 再生に必要なものだけを組む(操作 UI は含まない)。
        ///
        /// <code>
        /// SmartMediaPlayer         UdonSmartMediaPlayer(配線だけ)
        /// ├── Screen               UdonMediaScreen        ← 映像と音の出力先
        /// │   └── Surface          Renderer + AudioSource
        /// ├── Player               動画プレイヤー + UdonVideoBackend  ← URL を知る唯一の場所
        /// ├── Catalog              UdonMediaCatalog + UdonCatalogStore + UdonRecommendationEngine
        /// ├── Session              UdonPlayerSession      ← 再生の判断
        /// └── Controller           UdonMediaController    ← 操作の窓口(パネルはここに名乗り出る)
        /// </code>
        /// </summary>
        private static GameObject BuildCore(VideoPlayerPreference preference, StringBuilder log)
        {
            var root = new GameObject("SmartMediaPlayer");

            // ── Screen(映像と音の出力先)
            var screenObject = Child(root, "Screen");
            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "Surface";
            surface.transform.SetParent(screenObject.transform, false);
            surface.transform.localPosition = new Vector3(0f, 1.8f, 0f);
            surface.transform.localScale = new Vector3(3.2f, 1.8f, 1f);

            var collider = surface.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);

            var renderer = surface.GetComponent<Renderer>();
            var speaker = surface.AddComponent<AudioSource>();
            speaker.playOnAwake = false;
            speaker.spatialBlend = 1f;      // ワールドに置く前提なので 3D
            speaker.maxDistance = 25f;
            speaker.volume = 0.6f;

            var screen = Add<UdonMediaScreen>(screenObject);
            if (screen != null)
            {
                screen.Surface = renderer;
                screen.Speaker = speaker;
            }
            if (_needsCompile) return root;

            // ── Player(動画プレイヤー + バックエンド)
            //    VRChat の動画イベントは「同じ GameObject の UdonBehaviour」にしか届かないので、
            //    UdonVideoBackend は必ずプレイヤーと同じ場所に置く。
            var playerObject = Child(root, "Player");

            var built = VRChatVideoPlayerFactory.AddPlayer(
                playerObject, preference, renderer, speaker);
            if (!built.Ok)
            {
                Debug.LogError(
                    "[UdonSmartMediaPlayerPrefabBuilder] 動画プレイヤーを置けませんでした。\n"
                    + built.Report);
                UnityEngine.Object.DestroyImmediate(root);
                return null;
            }
            log.Append(built.Report);

            var backend = Add<UdonVideoBackend>(playerObject);
            if (backend != null)
            {
                backend.Player = built.Player;
                backend.Screen = screen;
            }
            if (_needsCompile) return root;

            // ── Catalog(焼き込み済みデータ)+ Store(表示用の窓口)
            var catalogObject = Child(root, "Catalog");

            var catalog = Add<UdonMediaCatalog>(catalogObject);
            if (_needsCompile) return root;

            if (catalog != null)
            {
                IMediaCatalog source = DefaultCatalogProvider.CreateDefaultCatalog();
                var items = new List<MediaItem>(source.GetAll());
                UdonCatalogBaker.Bake(catalog, items);
                _bakedCount = items.Count;
            }

            var store = Add<UdonCatalogStore>(catalogObject);
            if (store != null) store.Catalog = catalog;
            if (_needsCompile) return root;

            var recommendation = Add<UdonRecommendationEngine>(catalogObject);
            if (recommendation != null) recommendation.Catalog = catalog;
            if (_needsCompile) return root;

            // ── Session(再生の判断)
            var sessionObject = Child(root, "Session");
            var session = Add<UdonPlayerSession>(sessionObject);
            if (session != null)
            {
                session.Store = store;
                session.Recommendation = recommendation;
                session.Backend = backend;
            }
            if (backend != null) backend.Session = session;
            if (_needsCompile) return root;

            // ── Controller(操作の窓口)
            var controllerObject = Child(root, "Controller");
            var controller = Add<UdonMediaController>(controllerObject);
            if (controller != null) controller.Session = session;
            if (_needsCompile) return root;

            // ── 根っこ(配線だけ)
            var smartPlayer = Add<UdonSmartMediaPlayer>(root);
            if (smartPlayer != null)
            {
                smartPlayer.Catalog = catalog;
                smartPlayer.Store = store;
                smartPlayer.Recommendation = recommendation;
                smartPlayer.Session = session;
                smartPlayer.Backend = backend;
                smartPlayer.Screen = screen;
                smartPlayer.Controller = controller;
            }

            return root;
        }

        // ───────── 報告 ─────────

        private static void Report(
            StringBuilder log, UdonSharpProgramAssetFactory.Report report,
            GameObject player, string menuPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[UdonSmartMediaPlayerPrefabBuilder] Prefab を 3 つ作成しました。");
            sb.AppendLine("  " + PlayerPrefabPath + "   ← まずこれを置く(中身 + 壁パネル)");
            sb.AppendLine("  " + WallPanelPrefabPath + "     ← 2 枚目の壁パネル");
            sb.AppendLine("  " + RemotePanelPrefabPath + "   ← 手持ちリモコン(最小構成)");
            sb.AppendLine();
            sb.Append(log);
            sb.AppendLine("  U# プログラム : " + report);
            sb.AppendLine("  焼き込み      : " + _bakedCount + " 件(URL は VRCUrl として保存済み)");

            if (UdonWorldUiKit.BindFailures > 0)
            {
                sb.AppendLine("  ボタンの配線  : " + UdonWorldUiKit.BindFailures
                              + " 件を繋げませんでした(上の警告を参照)");
            }
            else
            {
                sb.AppendLine("  ボタンの配線  : すべて繋がりました");
            }

            sb.AppendLine();
            sb.AppendLine("  ワールドへの置き方:");
            sb.AppendLine("    1. SmartMediaPlayer.prefab を Hierarchy へドラッグ");
            sb.AppendLine("    2. Screen/Surface と WallPanel を見える位置へ動かす");
            sb.AppendLine("    3. VRChat SDK > Build & Test");
            sb.AppendLine();
            sb.AppendLine("  操作 UI を増やすとき:");
            sb.AppendLine("    MediaWallPanel / MediaRemotePanel をドラッグして、");
            sb.AppendLine("    Inspector の Core に シーンの SmartMediaPlayer を挿すだけです。");
            sb.AppendLine("    (残りの欄は Core から自動で引いてきます)");
            sb.AppendLine();
            sb.AppendLine("  ※ 同梱カタログの URL は架空のアドレスです。実際に映像を出すには");
            sb.AppendLine("     Catalog の UdonMediaCatalog を選び、Inspector の Urls を");
            sb.AppendLine("     実在する URL に差し替えてください(VRCUrl は実行時に作れません)。");
            sb.AppendLine();
            sb.AppendLine("  ※ EventSystem は置いていません。VRChat が実行時に用意します。");

            Debug.Log(sb.ToString(), player);
        }

        // ───────── 共通 ─────────

        private static bool _needsCompile;
        private static int _bakedCount;

        private static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        /// <summary>UdonSharp のコンポーネントを付ける(プログラムと UdonBehaviour ごと)。</summary>
        private static T Add<T>(GameObject target) where T : Component
        {
            bool needsCompile;
            var component = UdonSharpSceneUtility.AddUdonSharpComponent(
                target, typeof(T), out needsCompile) as T;

            if (needsCompile) _needsCompile = true;
            return component;
        }

        /// <summary>
        /// コンパイル待ちなら、壊れた Prefab を残さずにやり直しを促す。
        /// (Phase3-3 で「program asset が null の状態で保存された」ことがあったため)
        /// </summary>
        private static bool Abort(GameObject root, string menuPath)
        {
            if (!_needsCompile && !UdonMediaPanelBuilder.NeedsCompile) return false;

            if (root != null) UnityEngine.Object.DestroyImmediate(root);

            string notice = UdonSharpSceneUtility.RecompileInstruction(menuPath);
            Debug.LogWarning("[UdonSmartMediaPlayerPrefabBuilder] " + notice);
            EditorUtility.DisplayDialog("Smart Media Platform", notice, "OK");
            return true;
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            Directory.CreateDirectory(PrefabFolder);

            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.Refresh();

            return saved;
        }
    }
}
#endif
