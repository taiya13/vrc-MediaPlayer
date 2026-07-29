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
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SmartMediaPlatform.World.EditorTools
{
    /// <summary>
    /// <b>実機で動く SmartMediaPlayer.prefab を作るエディタツール。</b>Phase5-2。
    ///
    /// Phase5-1 の Prefab は <b>アップロードしたワールドでは制御が動きません</b>でした
    /// (独自 MonoBehaviour は実行されず、Udon だけが動くため)。
    /// この Prefab は制御層がすべて <c>UdonSharpBehaviour</c> なので、
    /// <b>ドラッグして Build &amp; Test するだけで動きます</b>。
    ///
    /// <b>なぜ Prefab を同梱せずここで作るのか</b>は Phase5-1 と同じ理由です。
    /// UdonSharp のプログラム(.asset)と UdonBehaviour は
    /// <b>SDK のバージョンごとに中身が変わる</b>ので、固定の Prefab を持つと
    /// 「SDK を更新したら program asset が null」になります(Phase3-3 で実際に踏んだ問題)。
    /// </summary>
    public static class UdonSmartMediaPlayerPrefabBuilder
    {
        private const string PrefabFolder = "Assets/SmartMediaPlatform/World/Prefabs";
        private const string PrefabPath = PrefabFolder + "/SmartMediaPlayer.prefab";

        private const string MenuPath =
            "Tools/Smart Media Platform/Create SmartMediaPlayer Prefab (VRChat 実機・Udon)";

        private const string UnityPlayerMenuPath =
            "Tools/Smart Media Platform/Create SmartMediaPlayer Prefab (VRChat 実機・Udon / Unity 版)";

        /// <summary>この Prefab が使う UdonSharpBehaviour。プログラムを先に作る対象。</summary>
        private static Type[] BehaviourTypes()
        {
            return new[]
            {
                typeof(UdonMediaCatalog),
                typeof(UdonRecommendationEngine),
                typeof(UdonCatalogStore),
                typeof(UdonMediaScreen),
                typeof(UdonVideoBackend),
                typeof(UdonPlayerSession),
                typeof(UdonMediaController),
                typeof(UdonMediaPlayerUI),
                typeof(UdonMediaControlButton),
                typeof(UdonMediaRowButton),
                typeof(UdonSmartMediaPlayer),
            };
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

            var root = new GameObject("SmartMediaPlayer");
            var log = new StringBuilder();
            bool needsCompile;

            // ── 1. Screen(映像と音の出力先)
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

            var screen = Add<UdonMediaScreen>(screenObject, out needsCompile);
            if (Abort(needsCompile, root, menuPath)) return;
            if (screen != null)
            {
                screen.Surface = renderer;
                screen.Speaker = speaker;
            }

            // ── 2. Player(動画プレイヤー + バックエンド)
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
                return;
            }
            log.Append(built.Report);

            var backend = Add<UdonVideoBackend>(playerObject, out needsCompile);
            if (Abort(needsCompile, root, menuPath)) return;
            if (backend != null)
            {
                backend.Player = built.Player;
                backend.Screen = screen;
            }

            // ── 3. Catalog(焼き込み済みデータ)+ Store(表示用の窓口)
            var catalogObject = Child(root, "Catalog");

            var catalog = Add<UdonMediaCatalog>(catalogObject, out needsCompile);
            if (Abort(needsCompile, root, menuPath)) return;

            int baked = 0;
            if (catalog != null)
            {
                IMediaCatalog source = DefaultCatalogProvider.CreateDefaultCatalog();
                var items = new List<MediaItem>(source.GetAll());
                UdonCatalogBaker.Bake(catalog, items);
                baked = items.Count;
            }

            var store = Add<UdonCatalogStore>(catalogObject, out needsCompile);
            if (Abort(needsCompile, root, menuPath)) return;
            if (store != null) store.Catalog = catalog;

            var recommendation = Add<UdonRecommendationEngine>(catalogObject, out needsCompile);
            if (Abort(needsCompile, root, menuPath)) return;
            if (recommendation != null) recommendation.Catalog = catalog;

            // ── 4. Session(再生の判断)
            var sessionObject = Child(root, "Session");
            var session = Add<UdonPlayerSession>(sessionObject, out needsCompile);
            if (Abort(needsCompile, root, menuPath)) return;
            if (session != null)
            {
                session.Store = store;
                session.Recommendation = recommendation;
                session.Backend = backend;
            }
            if (backend != null) backend.Session = session;

            // ── 5. Controller(操作の窓口)
            var controllerObject = Child(root, "Controller");
            var controller = Add<UdonMediaController>(controllerObject, out needsCompile);
            if (Abort(needsCompile, root, menuPath)) return;
            if (controller != null) controller.Session = session;

            // ── 6. UI(画面)
            var ui = BuildUI(root, session, store, controller, out needsCompile);
            if (Abort(needsCompile, root, menuPath)) return;
            if (controller != null) controller.Ui = ui;

            // ── 7. 操作ボタン(Collider + Interact。ワールドでいちばん確実に押せる形)
            BuildControlButtons(root, controller, out needsCompile);
            if (Abort(needsCompile, root, menuPath)) return;

            // ── 8. 根っこ(配線だけ)
            var smartPlayer = Add<UdonSmartMediaPlayer>(root, out needsCompile);
            if (Abort(needsCompile, root, menuPath)) return;
            if (smartPlayer != null)
            {
                smartPlayer.Catalog = catalog;
                smartPlayer.Store = store;
                smartPlayer.Recommendation = recommendation;
                smartPlayer.Session = session;
                smartPlayer.Backend = backend;
                smartPlayer.Screen = screen;
                smartPlayer.Controller = controller;
                smartPlayer.Ui = ui;
            }

            var saved = SavePrefab(root, PrefabPath);

            var sb = new StringBuilder();
            sb.AppendLine("[UdonSmartMediaPlayerPrefabBuilder] Prefab を作成しました: " + PrefabPath);
            sb.Append(log);
            sb.AppendLine("  U# プログラム : " + report);
            sb.AppendLine("  焼き込み      : " + baked + " 件(URL は VRCUrl として保存済み)");
            sb.AppendLine();
            sb.AppendLine("  ワールドへの置き方:");
            sb.AppendLine("    1. Prefab を Hierarchy へドラッグ");
            sb.AppendLine("    2. Screen/Surface を見える位置へ動かす");
            sb.AppendLine("    3. VRChat SDK > Build & Test");
            sb.AppendLine();
            sb.AppendLine("  ※ 同梱カタログの URL は架空のアドレスです。実際に映像を出すには");
            sb.AppendLine("     Catalog の UdonMediaCatalog を選び、Inspector の Urls を");
            sb.AppendLine("     実在する URL に差し替えてください(VRCUrl は実行時に作れません)。");

            Debug.Log(sb.ToString(), saved);

            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
        }

        // ───────── UI ─────────

        /// <summary>
        /// ワールド内の画面を組む。Canvas(World Space)+ Text だけの素朴な作りです。
        /// <see cref="UdonMediaPlayerUI"/> は Text へ書くだけなので、
        /// <b>この見た目を作り直しても制御側は何も変わりません</b>。
        /// </summary>
        private static UdonMediaPlayerUI BuildUI(
            GameObject root, UdonPlayerSession session, UdonCatalogStore store,
            UdonMediaController controller, out bool needsCompile)
        {
            var uiObject = Child(root, "UI");

            var canvasObject = Child(uiObject, "Canvas");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();

            var canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(600f, 800f);
            canvasRect.localScale = new Vector3(0.004f, 0.004f, 0.004f);
            canvasRect.localPosition = new Vector3(-2.4f, 1.6f, 0f);

            float y = 360f;

            var nowPlaying = MakeText(canvasRect, "NowPlaying", ref y, 70f, 26);
            var time = MakeText(canvasRect, "Time", ref y, 34f, 20);
            var libraryHeader = MakeText(canvasRect, "LibraryHeader", ref y, 34f, 22);
            libraryHeader.text = "Library";

            var libraryRows = new Text[8];
            for (int i = 0; i < libraryRows.Length; i++)
            {
                libraryRows[i] = MakeText(canvasRect, "LibraryRow" + i, ref y, 30f, 18);
            }

            var relatedHeader = MakeText(canvasRect, "RelatedHeader", ref y, 34f, 22);
            relatedHeader.text = "関連";

            var relatedRows = new Text[3];
            for (int i = 0; i < relatedRows.Length; i++)
            {
                relatedRows[i] = MakeText(canvasRect, "RelatedRow" + i, ref y, 30f, 18);
            }

            var queueHeader = MakeText(canvasRect, "QueueHeader", ref y, 34f, 22);
            queueHeader.text = "Queue";

            var queueRows = new Text[5];
            for (int i = 0; i < queueRows.Length; i++)
            {
                queueRows[i] = MakeText(canvasRect, "QueueRow" + i, ref y, 30f, 18);
            }

            var status = MakeText(canvasRect, "Status", ref y, 34f, 18);

            var ui = Add<UdonMediaPlayerUI>(uiObject, out needsCompile);
            if (ui == null) return null;

            ui.Session = session;
            ui.Store = store;
            ui.NowPlayingText = nowPlaying;
            ui.TimeText = time;
            ui.LibraryRows = libraryRows;
            ui.RelatedRows = relatedRows;
            ui.QueueRows = queueRows;
            ui.StatusText = status;

            // 一覧の行ボタン。Text の GameObject に付けるのではなく、
            // 「押せる板」を別に用意して Interact で押せるようにする
            // (ワールド内 uGUI のレイキャストは設定を誤ると押せないため)。
            var rowsObject = Child(uiObject, "RowButtons");
            for (int i = 0; i < libraryRows.Length; i++)
            {
                var button = MakeInteractPlate(
                    rowsObject, "PlayRow" + i,
                    new Vector3(-2.4f, 2.6f - i * 0.13f, 0.02f),
                    new Vector3(2.2f, 0.12f, 0.02f));

                var row = Add<UdonMediaRowButton>(button, out needsCompile);
                if (row == null) return ui;

                row.Controller = controller;
                row.Ui = ui;
                row.Action = UdonMediaRowButton.ActionPlayLibrary;
                row.Row = i;
            }

            return ui;
        }

        private static Text MakeText(
            RectTransform parent, string name, ref float y, float height, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(580f, height);
            y -= height;

            var text = go.AddComponent<Text>();
            text.font = BuiltinFont();
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// 既定のフォント。名前が Unity のバージョンで変わっているので分けます
        /// (存在しない名前を渡すと Console にエラーが出るため、条件コンパイルで避ける)。
        /// </summary>
        private static Font BuiltinFont()
        {
#if UNITY_2022_2_OR_NEWER
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
        }

        /// <summary>
        /// 「使う」で押せる板を作る。VRChat では Collider + Interact が
        /// <b>いちばん確実に押せる</b>ので、操作系はこの形で置きます。
        /// </summary>
        private static GameObject MakeInteractPlate(
            GameObject parent, string name, Vector3 localPosition, Vector3 localScale)
        {
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = name;
            plate.transform.SetParent(parent.transform, false);
            plate.transform.localPosition = localPosition;
            plate.transform.localScale = localScale;

            // 見た目は要らない(文字は Canvas 側に出る)。当たり判定だけ残す。
            var plateRenderer = plate.GetComponent<Renderer>();
            if (plateRenderer != null) plateRenderer.enabled = false;

            return plate;
        }

        private static void BuildControlButtons(
            GameObject root, UdonMediaController controller, out bool needsCompile)
        {
            needsCompile = false;

            var holder = Child(root, "Buttons");

            string[] events = { "TogglePlayPause", "Next", "Previous", "Stop" };
            string[] labels = { "再生 / 一時停止", "次へ", "前へ", "停止" };

            for (int i = 0; i < events.Length; i++)
            {
                var plate = MakeInteractPlate(
                    holder, events[i],
                    new Vector3(-0.9f + i * 0.6f, 0.9f, 0f),
                    new Vector3(0.5f, 0.2f, 0.05f));

                var plateRenderer = plate.GetComponent<Renderer>();
                if (plateRenderer != null) plateRenderer.enabled = true;

                var button = Add<UdonMediaControlButton>(plate, out needsCompile);
                if (button == null) return;

                button.Controller = controller;
                button.EventName = events[i];
                button.LabelText = labels[i];
            }
        }

        // ───────── 共通 ─────────

        private static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        /// <summary>UdonSharp のコンポーネントを付ける(プログラムと UdonBehaviour ごと)。</summary>
        private static T Add<T>(GameObject target, out bool needsCompile) where T : Component
        {
            return UdonSharpSceneUtility.AddUdonSharpComponent(
                target, typeof(T), out needsCompile) as T;
        }

        /// <summary>
        /// コンパイル待ちなら、壊れた Prefab を残さずにやり直しを促す。
        /// (Phase3-3 で「program asset が null の状態で保存された」ことがあったため)
        /// </summary>
        private static bool Abort(bool needsCompile, GameObject root, string menuPath)
        {
            if (!needsCompile) return false;

            UnityEngine.Object.DestroyImmediate(root);

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
