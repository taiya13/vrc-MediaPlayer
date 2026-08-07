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
    /// <b>実機で動く Prefab を作るエディタツール。</b>Phase5-2 → Phase5-3 → Phase7-3。
    ///
    /// <b>Phase7-3 で「作るものは 1 つ」に戻しました。</b>
    /// 以前は SmartMediaPlayer / MediaWallPanel / MediaRemotePanel の 3 つを
    /// 毎回まとめて作っていましたが、<b>本体の中に壁パネルが入っているのに、
    /// ほぼ同じ壁パネルの Prefab がもう 1 つできる</b>ため、
    /// 「システムが 2 か所に入っているように見える / 両方置いてしまう」事故が
    /// 起きていました。いま作るのは <c>SmartMediaPlayer.prefab</c>(中身 + 壁パネル)
    /// <b>だけ</b>です。2 枚目以降のパネルが欲しいときだけ、
    /// 別メニュー(<see cref="CreateExtraPanels"/>)で作ります。
    ///
    /// <b>置き場所は <c>Assets/SmartMediaPlatform_Data</c> です。</b>Phase7-3。
    /// 以前は <c>Assets/SmartMediaPlatform/World/Prefabs</c> に作っていましたが、
    /// このシステムは更新のたびに <c>Assets/SmartMediaPlatform</c> を
    /// 丸ごと入れ替えてもらう配布方式なので、<b>その中に作った Prefab や設定は
    /// 更新のたびに消えていました</b>。作った物・あなたのデータは全部
    /// <c>SmartMediaPlatform_Data</c> 側に置き、<b>更新で消えない</b>ようにします。
    ///
    /// <b>なぜ Prefab を同梱せずここで作るのか</b>は Phase5-1 と同じ理由です。
    /// UdonSharp のプログラム(.asset)と UdonBehaviour は
    /// <b>SDK のバージョンごとに中身が変わる</b>ので、固定の Prefab を持つと
    /// 「SDK を更新したら program asset が null」になります(Phase3-3 で実際に踏んだ問題)。
    /// </summary>
    public static class UdonSmartMediaPlayerPrefabBuilder
    {
        /// <summary>
        /// 作った Prefab の置き場。<b>SmartMediaPlatform の外</b>です。
        /// 更新手順が「SmartMediaPlatform を削除して入れ替え」なので、
        /// 中に置くと更新のたびに消えてしまいます(Phase7-3 で移動)。
        /// </summary>
        private const string PrefabFolder = "Assets/SmartMediaPlatform_Data/Prefabs";

        private const string PlayerPrefabPath = PrefabFolder + "/SmartMediaPlayer.prefab";
        private const string WallPanelPrefabPath = PrefabFolder + "/MediaWallPanel.prefab";
        private const string RemotePanelPrefabPath = PrefabFolder + "/MediaRemotePanel.prefab";

        private const string MenuPath =
            "Tools/Smart Media Platform/SmartMediaPlayer を作る/VRChat 実機 (AVPro)";

        private const string UnityPlayerMenuPath =
            "Tools/Smart Media Platform/SmartMediaPlayer を作る/VRChat 実機 (Unity Video)";

        /// <summary>1 枚の画面に 2 系統を混ぜるシェーダー(Phase7-5)。</summary>
        private const string CrossfadeShaderName = "SmartMediaPlatform/Crossfade";

        /// <summary>
        /// <b>動画プレイヤーを 2 系統にして曲を混ぜるか。</b>Phase7-6 で false にしました。
        ///
        /// <b>なぜやめたのか</b><br/>
        /// 2 系統にすると、1 枚の画面へ<b>専用のシェーダー</b>で
        /// 2 つの映像を書き込むことになります。この作りは
        /// <list type="bullet">
        /// <item>画面が真っ白になる原因を作った(材質が絡む問題が増える)</item>
        /// <item>音の出口が 2 つに増え、切り替えの前後で鳴り方が読みにくい</item>
        /// </list>
        /// という代償が大きく、得られる滑らかさに見合いませんでした。
        /// <b>1 系統に戻すと、画面は Unlit 1 枚・音は AudioSource 1 つ</b>になります。
        ///
        /// <c>UdonCrossfadeCoordinator</c> は<b>消していません</b>。
        /// ここを true に戻せば、また 2 系統で組み立てます。
        /// </summary>
        private const bool UseCrossfade = false;

        private const string ExtraPanelMenuPath =
            "Tools/Smart Media Platform/操作パネルを追加で作る (2 枚目以降・任意)";

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
                typeof(UdonSyncCoordinator),
                typeof(UdonCrossfadeCoordinator),
                typeof(UdonUserProfile),
                typeof(UdonSmartMediaPlayer),
            };

            types.AddRange(UdonMediaPanelBuilder.BehaviourTypes());
            return types.ToArray();
        }

        [MenuItem(MenuPath, false, -100)]
        public static void CreatePrefab()
        {
            Build(VideoPlayerPreference.AVPro, MenuPath);
        }

        /// <summary>
        /// AVPro はエディタ(ClientSim)で映像を出さないので、
        /// <b>エディタで絵を確認したいとき用</b>の Unity 版も用意しておきます。
        /// </summary>
        [MenuItem(UnityPlayerMenuPath, false, -99)]
        public static void CreateUnityPlayerPrefab()
        {
            Build(VideoPlayerPreference.Unity, UnityPlayerMenuPath);
        }

        private static void Build(VideoPlayerPreference preference, string menuPath)
        {
            _needsCompile = false;
            _bakedCount = 0;
            _syncFailures = 0;

            // 0-a. 使えなくなった U# プログラムを先に片付ける(Phase7-6)。
            //     クラスを 1 つ廃止すると、その .asset だけが
            //     「更新で消えない場所」に取り残されます。中身の C# が無い
            //     プログラムは二度とコンパイルできず、<b>ビルドを丸ごと止めます</b>。
            UdonSharpProgramAssetFactory.CleanupOrphanPrograms(true);

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

            UdonWorldUiKit.ResetCounters();
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

                // 2 列(1.82 m × 1.27 m)の中心を 1.35 m に置くと、
                // 上端 1.99 m / 下端 0.72 m で全部が胸〜目の高さに入る。
                wall.transform.localPosition = new Vector3(-2.8f, 1.35f, 0f);
            }

            var player = SavePrefab(root, PlayerPrefabPath);

            // ── シーンに EventSystem が無いと uGUI が一切動かない。
            //    つまみもスクロールも音量も、押す以外は全部これ頼みです。
            if (UdonWorldUiKit.EnsureEventSystem())
            {
                log.AppendLine("  EventSystem  : シーンに無かったので作りました(uGUI に必須)");
            }
            else
            {
                log.AppendLine("  EventSystem  : すでにあります");
            }

            Report(log, report, player, menuPath);

            Selection.activeObject = player;
            EditorGUIUtility.PingObject(player);
        }

        // ───────── 追加パネル(任意) ─────────

        /// <summary>
        /// <b>2 枚目以降の操作パネルが欲しい人だけ</b>が使うメニュー。Phase7-3。
        ///
        /// 本体(<c>SmartMediaPlayer.prefab</c>)には壁パネルが 1 枚入っているので、
        /// ふつうはこれを押す必要はありません。
        /// ここで作る Prefab は<b>操作 UI だけ</b>で、再生の中身は入っていません。
        /// シーンに置いたら Inspector の <c>Core</c> に、
        /// シーンの SmartMediaPlayer を挿してください。
        /// </summary>
        [MenuItem(ExtraPanelMenuPath, false, -90)]
        public static void CreateExtraPanels()
        {
            _needsCompile = false;

            var report = UdonSharpProgramAssetFactory.EnsureProgramAssets(
                UdonMediaPanelBuilder.BehaviourTypes());

            if (report.HasCreated && !UdonSharpProgramAssetFactory.TryCompile())
            {
                string notice = UdonSharpSceneUtility.RecompileInstruction(ExtraPanelMenuPath);
                Debug.LogWarning("[UdonSmartMediaPlayerPrefabBuilder] " + notice);
                EditorUtility.DisplayDialog("Smart Media Platform", notice, "OK");
                return;
            }

            UdonWorldUiKit.ResetCounters();
            UdonMediaPanelBuilder.ResetNeedsCompile();
            _syncFailures = 0;

            // ── 壁パネル単体
            var wallRoot = new GameObject("MediaWallPanel");
            UdonMediaPanel standaloneWall = UdonMediaPanelBuilder.BuildWallPanel(wallRoot, "Panel");
            if (Abort(wallRoot, ExtraPanelMenuPath)) return;
            if (standaloneWall != null) standaloneWall.PanelName = "Smart Media Player";
            var wallPrefab = SavePrefab(wallRoot, WallPanelPrefabPath);

            // ── 手持ちリモコン(最小構成の例)
            var remoteRoot = new GameObject("MediaRemotePanel");
            UdonMediaPanel remote = UdonMediaPanelBuilder.BuildRemotePanel(remoteRoot, "Panel");
            if (Abort(remoteRoot, ExtraPanelMenuPath)) return;
            if (remote != null) remote.PanelName = "リモコン";
            SavePrefab(remoteRoot, RemotePanelPrefabPath);

            Debug.Log(
                "[UdonSmartMediaPlayerPrefabBuilder] 追加パネルを 2 つ作りました(任意の道具)。\n"
                + "  " + WallPanelPrefabPath + "\n"
                + "  " + RemotePanelPrefabPath + "\n"
                + "  使い方: Hierarchy へドラッグして、Inspector の Core に\n"
                + "  シーンの SmartMediaPlayer を挿すだけです。\n"
                + "  ※ 中身(再生の仕組み)は入っていません。本体 1 つ + パネル何枚でも、が正しい形です。",
                wallPrefab);

            Selection.activeObject = wallPrefab;
            EditorGUIUtility.PingObject(wallPrefab);
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
            AssignScreenMaterial(renderer, log);
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

            // ── Player(動画プレイヤー + バックエンド)を 2 系統(Phase7-5)
            //
            //    VRChat の動画イベントは「同じ GameObject の UdonBehaviour」にしか
            //    届かないので、UdonVideoBackend は必ずプレイヤーと同じ場所に置く。
            //    A と B で別々の GameObject にしてあるのはそのためです。
            //
            //    <b>画面は 1 枚のまま</b>です。A は _MainTex、B は _SecondTex へ
            //    書かせて、シェーダー側で混ぜます(板も描画も増えません)。
            //
            //    <b>音は系統ごとに別の AudioSource</b>が要ります。
            //    1 つを共有すると、フェード中に片方の音量を動かした瞬間
            //    もう片方も一緒に動いてしまい、混ざりません。
            var backend = BuildPlayer(
                root, "Player", preference, renderer, speaker, screen, "_MainTex", log);
            if (backend == null) { UnityEngine.Object.DestroyImmediate(root); return null; }
            if (_needsCompile) return root;

            UdonVideoBackend backendB = null;

            if (UseCrossfade)
            {
                // B 系統の音は、同じ場所に置いた 2 つめの AudioSource から出す。
                var speakerB = surface.AddComponent<AudioSource>();
                speakerB.playOnAwake = false;
                speakerB.spatialBlend = speaker.spatialBlend;
                speakerB.maxDistance = speaker.maxDistance;
                speakerB.volume = 0f;   // 裏は黙って始まる

                backendB = BuildPlayer(
                    root, "PlayerB", preference, renderer, speakerB, screen, "_SecondTex", log);
                if (_needsCompile) return root;

                if (backendB != null) backendB.SetFadeVolume(0f);
            }

            // 表(A)の音は、人が決めた音量から始める。
            if (backend != null) backend.SetFadeVolume(1f);

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

            // ── その人の好み(お気に入り・履歴・再生回数)。Phase7-8。
            //    <b>同期しません。</b>好みは人それぞれで、
            //    他人の履歴が自分のおすすめに混ざるほうが困ります。
            var profileObject = Child(root, "Profile");
            var profile = Add<UdonUserProfile>(profileObject);
            if (profile != null) profile.Catalog = catalog;
            if (recommendation != null) recommendation.Profile = profile;
            if (_needsCompile) return root;

            // ── Crossfade(曲と曲を繋ぐ担当。Phase7-5 / Phase7-6 で既定はオフ)
            //    Session も Backend も、混ぜ方のことは知りません。
            //    <see cref="UseCrossfade"/> が false のときは<b>置きません</b> —
            //    置いておくだけで Session が「混ぜる道」を通ってしまうためです。
            UdonCrossfadeCoordinator crossfade = null;

            if (UseCrossfade)
            {
                var crossfadeObject = Child(root, "Crossfade");
                crossfade = Add<UdonCrossfadeCoordinator>(crossfadeObject);
                if (crossfade != null)
                {
                    crossfade.Screen = screen;
                    crossfade.BackendA = backend;
                    crossfade.BackendB = backendB;
                }
                if (_needsCompile) return root;
            }

            // ── Session(再生の判断)
            var sessionObject = Child(root, "Session");
            var session = Add<UdonPlayerSession>(sessionObject);
            if (session != null)
            {
                session.Store = store;
                session.Recommendation = recommendation;
                session.Backend = backend;
                session.Crossfade = crossfade;
                session.Profile = profile;

                // ── 再生予定が空になったらおすすめへ進む(Phase7-6)。
                //
                //    <b>ここで必ず書き込みます。</b>C# 側の初期値を変えても、
                //    以前に作った Prefab には<b>そのとき保存された値が残ります</b>。
                //    「1 曲を繰り返す」で保存されていると、
                //    直したはずの挙動が実機では古いままになります。
                session.EndBehaviour = UdonPlayerSession.EndBehaviourRecommend;
                session.AutoQueueEnabled = true;
            }

            // 終わりの合図は、両系統から同じ Session へ届く必要がある。
            // 混ぜている最中に古いほうから届いたぶんは Session 側で捌く。
            if (backend != null) backend.Session = session;
            if (backendB != null) backendB.Session = session;
            if (crossfade != null) crossfade.Session = session;
            if (_needsCompile) return root;

            // ── Controller(操作の窓口)
            var controllerObject = Child(root, "Controller");
            var controller = Add<UdonMediaController>(controllerObject);
            if (controller != null) controller.Session = session;
            if (_needsCompile) return root;

            // ── Sync(同期の担当。Phase5-4)
            //    所有権は GameObject ごとに移るので、必ず自前の GameObject に置く。
            //    動画プレイヤーや画面と同居させると、操作するたびに
            //    そちらの所有権まで動いてしまう。
            var syncObject = Child(root, "Sync");
            var sync = Add<UdonSyncCoordinator>(syncObject);
            if (sync != null)
            {
                sync.Session = session;
                sync.Backend = backend;
                sync.Controller = controller;
            }
            if (controller != null) controller.Sync = sync;
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
                smartPlayer.Sync = sync;
                smartPlayer.Crossfade = crossfade;
            }

            return root;
        }

        // ───────── 報告 ─────────

        private static void Report(
            StringBuilder log, UdonSharpProgramAssetFactory.Report report,
            GameObject player, string menuPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[UdonSmartMediaPlayerPrefabBuilder] Prefab を 1 つ作成しました。");
            sb.AppendLine("  " + PlayerPrefabPath);
            sb.AppendLine("  (中身 + 壁パネル。ワールドに置くのはこれ 1 つだけです)");
            sb.AppendLine("  ※ この置き場(SmartMediaPlatform_Data)は更新で消えない場所です。");
            sb.AppendLine();
            sb.Append(log);
            sb.AppendLine("  U# プログラム : " + report);
            sb.AppendLine("  焼き込み      : " + _bakedCount + " 件(見本のデータ)");
            sb.AppendLine();
            sb.AppendLine("  ⚠ 入っているのは見本の URL(example.com)です。実在しません。");
            sb.AppendLine("    このままでは押しても動画は映りません。");
            sb.AppendLine("    Catalog Builder で自分のカタログを選び、");
            sb.AppendLine("    「③ VRCUrl へ焼く」を押してから Build & Test してください。");

            // 「繋げたつもり」を報告しないよう、どちらも読み戻した実数を出す。
            sb.AppendLine("  ボタンの配線  : " + UdonWorldUiKit.BindCount + " 件 OK / "
                          + UdonWorldUiKit.BindFailures + " 件 NG"
                          + (UdonWorldUiKit.BindFailures > 0 ? "  ← 上の警告を参照" : ""));

            sb.AppendLine("  「使う」で押せる: " + UdonWorldUiKit.InteractCount + " 件 OK / "
                          + UdonWorldUiKit.InteractFailures + " 件 NG"
                          + (UdonWorldUiKit.InteractFailures > 0 ? "  ← 上の警告を参照" : ""));

            sb.AppendLine("  VR で押しやすさ: " + (UdonWorldUiKit.SmallTouchTargets == 0
                              ? "すべて " + Mathf.RoundToInt(UdonWorldUiKit.ComfortableTouchMeters * 1000f)
                                + " mm 以上"
                              : UdonWorldUiKit.SmallTouchTargets + " 個が小さすぎます(上の警告を参照)"));

            sb.AppendLine("  Udon へ書き戻し: " + UdonWorldUiKit.SyncedCount + " 件 OK / "
                          + _syncFailures + " 件 NG"
                          + (_syncFailures > 0 ? "  ← 上の警告を参照" : ""));

            sb.AppendLine();
            sb.AppendLine("  ワールドへの置き方:");
            sb.AppendLine("    1. SmartMediaPlayer.prefab を Hierarchy へドラッグ");
            sb.AppendLine("    2. Screen/Surface と WallPanel を見える位置へ動かす");
            sb.AppendLine("    3. VRChat SDK > Build & Test");
            sb.AppendLine();
            sb.AppendLine("  操作 UI を増やしたいとき(任意):");
            sb.AppendLine("    Tools > Smart Media Platform > 操作パネルを追加で作る を押すと、");
            sb.AppendLine("    パネルだけの Prefab(壁 / リモコン)ができます。");
            sb.AppendLine("    ドラッグして Core にシーンの SmartMediaPlayer を挿すだけです。");
            sb.AppendLine();
            sb.AppendLine("  ※ 同梱カタログの URL は架空のアドレスです。実際に映像を出すには");
            sb.AppendLine("     Catalog の UdonMediaCatalog を選び、Inspector の Urls を");
            sb.AppendLine("     実在する URL に差し替えてください(VRCUrl は実行時に作れません)。");
            sb.AppendLine();
            sb.AppendLine("  ※ ボタンは 2 通りで押せます:");
            sb.AppendLine("     uGUI の Button と、Collider +「使う」(Interact)。");
            sb.AppendLine("     ワールド内 uGUI はレイキャストが通らないことがあるので、");
            sb.AppendLine("     実機では「使う」が本命です(二重に効かないよう抑えてあります)。");
            sb.AppendLine();
            sb.AppendLine("  ※ 同期(Phase5-4)は既定で有効です。誰が操作してもよい設定なので、");
            sb.AppendLine("     押した人がその場で操作者(Owner)になります。");
            sb.AppendLine("     マスターだけに絞りたいときは Sync の Access Policy を 1 にしてください。");

            Debug.Log(sb.ToString(), player);
        }

        // ───────── 共通 ─────────

        private static bool _needsCompile;
        private static int _bakedCount;
        private static int _syncFailures;

        /// <summary>
        /// <b>映像を映す面のマテリアルを用意する。</b>Phase7-2。
        ///
        /// <b>これが無いと動画が映りません。</b>Phase7-1 まで、ここは
        /// <c>GameObject.CreatePrimitive</c> が付ける <c>Default-Material</c> のままでした。
        /// 問題が 2 つあります。
        /// <list type="number">
        /// <item><b>Standard シェーダーはライトの影響を受けます。</b>
        ///       置いた場所にライトが当たっていない(ライトベイクをしていない)と、
        ///       動画は流れているのに<b>面が真っ黒</b>になります。
        ///       ワールドによって映ったり映らなかったりするのはこれが理由です</item>
        /// <item><b><c>Default-Material</c> は Unity の組み込みで、全部の Primitive が
        ///       共有しています。</b>動画プレイヤーがそこへテクスチャを書き込むと、
        ///       ワールド内のほかの箱や球にも影響しかねません</item>
        /// </list>
        ///
        /// <b>Unlit にします。</b>映像は自分で光っているものなので、
        /// ライトを当てる必要がそもそもありません。
        /// </summary>
        private static void AssignScreenMaterial(Renderer renderer, StringBuilder log)
        {
            if (renderer == null) return;

            // 材質も「更新で消えない側」に置く(Phase7-3 で SmartMediaPlatform の外へ移動)。
            const string folder = "Assets/SmartMediaPlatform_Data";
            const string path = folder + "/SmartMediaScreen.mat";

            // ── <b>毎回作り直します。</b>Phase7-6。
            //
            //    以前は「すでにあれば使い回す」でした。ところが
            //    <b>作りを変えても古い材質が残り続ける</b>ので、
            //    2 系統用の材質が 1 系統の構成に付いたままになり、
            //    <b>画面が真っ白</b>のまま直りませんでした。
            //    材質は組み立ての一部なので、組み立て直したら作り直します。
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);

            // ── 2 系統で混ぜるときだけ専用シェーダーを使う(Phase7-5)。
            //
            //    1 系統(既定)では Unlit/Texture です。<b>画面に絡む部品が
            //    少ないほど、映らなくなったときに疑う所が減ります。</b>
            bool crossfadeReady = UseCrossfade;
            Shader shader = crossfadeReady ? Shader.Find(CrossfadeShaderName) : null;

            if (shader == null)
            {
                crossfadeReady = false;
                shader = Shader.Find("Unlit/Texture");
            }

            if (shader == null)
            {
                log.AppendLine(
                    "  ※ シェーダーが見つかりません。"
                    + "Surface のマテリアルを手で Unlit にしてください。");
                return;
            }

            var material = new Material(shader);
            material.name = "SmartMediaScreen";

            // ── 動画が来るまでの絵を入れておく。
            //
            //    _MainTex が空だと<b>真っ白</b>になります(Unlit に _Color は無いので、
            //    色を入れても効きません)。Phase7-2 の途中版が
            //    「音は鳴るのに画面が真っ白」だったのはこれが理由です。
            //    黒を入れておけば、映る前は黒い画面になります。
            material.mainTexture = Texture2D.blackTexture;

            if (crossfadeReady)
            {
                // B 系統の欄も黒で埋めておく(こちらが空でも白くなります)。
                material.SetTexture("_SecondTex", Texture2D.blackTexture);
                material.SetFloat("_Blend", 0f);
            }

            if (!AssetDatabase.IsValidFolder(folder)) Directory.CreateDirectory(folder);

            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();

            renderer.sharedMaterial = material;

            log.AppendLine(crossfadeReady
                ? "  画面の材質   : " + path + " を作り直しました(クロスフェード対応 / ライト不要)"
                : "  画面の材質   : " + path + " を作り直しました(Unlit / ライト不要)");
        }

        /// <summary>
        /// 動画プレイヤー 1 系統ぶんを組む(Phase7-5)。
        /// A と B で違うのは<b>名前・音の出口・書き込むテクスチャ欄</b>だけです。
        /// </summary>
        private static UdonVideoBackend BuildPlayer(
            GameObject root, string objectName, VideoPlayerPreference preference,
            Renderer renderer, AudioSource speaker, UdonMediaScreen screen,
            string textureProperty, StringBuilder log)
        {
            var playerObject = Child(root, objectName);

            log.AppendLine("  [" + objectName + "]");

            VRChatVideoPlayerFactory.TextureProperty = textureProperty;
            var built = VRChatVideoPlayerFactory.AddPlayer(
                playerObject, preference, renderer, speaker);
            VRChatVideoPlayerFactory.ResetTextureProperty();

            if (!built.Ok)
            {
                Debug.LogError(
                    "[UdonSmartMediaPlayerPrefabBuilder] 動画プレイヤーを置けませんでした。\n"
                    + built.Report);
                return null;
            }
            log.Append(built.Report);

            var backend = Add<UdonVideoBackend>(playerObject);
            if (backend != null)
            {
                backend.Player = built.Player;
                backend.Screen = screen;
                backend.Speaker = speaker;
                backend.TextureProperty = textureProperty;
            }
            return backend;
        }

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

            // ここが要。コードで代入した値は proxy にしか乗っていないので、
            // 保存する前に裏の UdonBehaviour へ写す。
            // これを飛ばすと Prefab は出来上がるのに中身が空、という壊れ方をする。
            _syncFailures += UdonWorldUiKit.SyncProxies(root);

            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.Refresh();

            return saved;
        }
    }
}
#endif
