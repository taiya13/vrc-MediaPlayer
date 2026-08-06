#if UNITY_EDITOR && VRC_SDK_VRCSDK3
using System;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using SmartMediaPlatform.World.Udon;
using SmartMediaPlatform.World.Udon.UI;

namespace SmartMediaPlatform.World.EditorTools
{
    /// <summary>
    /// <b>パネルのボタンが押せないときの診断と修復。</b>Phase5-3。
    ///
    /// <b>なぜ要るのか</b><br/>
    /// Phase5-3 の最初の版で、Prefab は出来ているのに
    /// <b>ボタンが 1 つも押せない</b>という状態になりました。原因は 2 つで、
    /// どちらも「出来ているように見えて中身が空」という同じ形をしています。
    /// <list type="number">
    /// <item><c>Button.onClick</c> の永続リスナーが保存されていない(Inspector で No Function)</item>
    /// <item>コードで代入した参照が <c>UdonBehaviour</c> へ写されていない
    ///       (<c>UdonSharpBehaviour</c> は Inspector 用の見せかけで、
    ///        エディタスクリプトからの代入は <c>CopyProxyToUdon</c> を呼ばないと届かない)</item>
    /// </list>
    ///
    /// Prefab を作り直せば直りますが、<b>すでにワールドへ置いて位置を調整したあと</b>だと
    /// 作り直しは高くつきます。この道具はシーンに置いたまま繋ぎ直します。
    ///
    /// <b>繋ぎ先は名前で決まります。</b>Prefab ビルダーが付けた名前をそのまま使うので、
    /// GameObject の名前を変えていると対象から外れます(その場合は診断に出ます)。
    /// </summary>
    public static class UdonMediaPanelDoctor
    {
        private const string DiagnoseMenu =
            "Tools/Smart Media Platform/操作 UI の配線を確認する";

        private const string RepairMenu =
            "Tools/Smart Media Platform/操作 UI の配線を繋ぎ直す";

        /// <summary>
        /// <b>すでに置いてある Canvas に当たり判定を足す。</b>Phase7-3。
        ///
        /// VRChat のレーザーは、まず物理の当たり判定を探します。
        /// そこに何も無ければ Canvas は見えていないのと同じで、
        /// <b>つまみもスクロールもホイールも届きません</b>。
        /// Phase7-2 までの Prefab には付いていないので、置き直さずに直せるようにします。
        /// </summary>
        private static int AddCanvasColliders()
        {
            var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            if (canvases == null) return 0;

            int added = 0;

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null || canvas.renderMode != RenderMode.WorldSpace) continue;

                // このシステムのパネルだけを触る(ワールドの別の UI を壊さない)。
                if (canvas.GetComponentInParent<UdonMediaPanel>() == null) continue;
                if (canvas.GetComponent<Collider>() != null) continue;

                var rect = canvas.GetComponent<RectTransform>();
                if (rect == null) continue;

                var box = Undo.AddComponent<BoxCollider>(canvas.gameObject);
                if (box == null) continue;

                box.isTrigger = true;
                box.size = new Vector3(rect.sizeDelta.x, rect.sizeDelta.y, 1f);
                box.center = Vector3.zero;
                added++;
            }

            return added;
        }

        /// <summary>
        /// <b>すでに置いてある Canvas に VRC_UiShape を足す。</b>Phase7-4。
        ///
        /// EventSystem と当たり判定を入れても、実機でつまみ・音量・検索欄が
        /// 反応しない報告が続いたため、<b>原因候補として</b>追加しました。
        /// VRC_UiShape はワールドの Canvas を VRChat のポインター(VR のレーザー・
        /// Desktop のマウス)で操作してよいと明示するコンポーネントで、
        /// これが無いと<b>ボタンは「使う」で動くのに、Slider や InputField の
        /// ドラッグ操作だけが沈黙する</b>という、実際に報告された症状と一致します。
        /// SDK に無ければ何もしません(型を名前で探すため、無くてもコンパイルは壊れません)。
        /// </summary>
        private static int AddUiShapes()
        {
            var panels = FindPanels();
            int added = 0;

            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] == null) continue;
                added += UdonWorldUiKit.AddUiShapeToExistingCanvases(panels[i].gameObject);
            }

            return added;
        }

        private static Type FindUiShapeType()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
                if (types == null) continue;

                foreach (var type in types)
                {
                    if (type == null) continue;
                    if (type.Name == "VRC_UiShape" || type.Name == "VRCUiShape") return type;
                }
            }
            return null;
        }

        private static int CountWorldSpaceCanvases(UdonMediaPanel[] panels)
        {
            int count = 0;
            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] == null) continue;
                var canvases = panels[i].GetComponentsInChildren<Canvas>(true);
                foreach (var c in canvases)
                {
                    if (c != null && c.renderMode == RenderMode.WorldSpace) count++;
                }
            }
            return count;
        }

        private static int CountCanvasesWithUiShape(UdonMediaPanel[] panels, Type shapeType)
        {
            int count = 0;
            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] == null) continue;
                var canvases = panels[i].GetComponentsInChildren<Canvas>(true);
                foreach (var c in canvases)
                {
                    if (c == null || c.renderMode != RenderMode.WorldSpace) continue;
                    if (c.GetComponent(shapeType) != null) count++;
                }
            }
            return count;
        }

        // ───────── 診断 ─────────

        [MenuItem(DiagnoseMenu, false, -60)]
        public static void DiagnoseMenuItem()
        {
            Debug.Log(Diagnose());
        }

        /// <summary>シーンにあるパネルを全部見て、繋がっているかを数える。</summary>
        public static string Diagnose()
        {
            UdonMediaPanel[] panels = FindPanels();

            var sb = new StringBuilder();
            sb.AppendLine("[UdonMediaPanelDoctor] シーンの操作 UI を確認しました。");

            // ── EventSystem。無いと uGUI が一切動きません。
            //    「ボタンは押せるのに、つまみもスクロールも音量も動かない」は
            //    ほぼこれです(ボタンだけは別途「使う」で動いているため)。
            int eventSystems = UdonWorldUiKit.CountEventSystems();
            if (eventSystems == 0)
            {
                sb.AppendLine("  ✗ EventSystem がありません。");
                sb.AppendLine("    → つまみ・スクロール・音量など、押す以外の操作が全部動きません。");
                sb.AppendLine("    → 「操作 UI の配線を繋ぎ直す」を押すと作ります。");
            }
            else if (eventSystems > 1)
            {
                sb.AppendLine("  ✗ EventSystem が " + eventSystems + " 個あります(1 個だけにしてください)。");
                sb.AppendLine("    → 2 つあると、どちらが操作を配るか決まらず動かなくなります。");
            }
            else
            {
                sb.AppendLine("  ✓ EventSystem: 1 個");
            }

            // ── VRC_UiShape。無いと実機でつまみ・音量・検索欄が反応しないことがある
            //    (原因候補。EventSystem/当たり判定だけでは直らなかったための追加調査)。
            Type shapeType = FindUiShapeType();
            if (shapeType == null)
            {
                sb.AppendLine("  ？ VRC_UiShape が見つかりません(使っている SDK の版を確認してください)。");
            }
            else
            {
                int withShape = CountCanvasesWithUiShape(panels, shapeType);
                int totalCanvases = CountWorldSpaceCanvases(panels);

                if (totalCanvases > 0 && withShape < totalCanvases)
                {
                    sb.AppendLine("  ✗ VRC_UiShape が付いていない Canvas があります("
                                  + withShape + " / " + totalCanvases + ")。");
                    sb.AppendLine("    → つまみ・音量・検索欄が実機で反応しない原因候補です。");
                    sb.AppendLine("    → 「操作 UI の配線を繋ぎ直す」を押すと足します。");
                }
                else if (totalCanvases > 0)
                {
                    sb.AppendLine("  ✓ VRC_UiShape: 全 Canvas に付いています");
                }
            }

            if (panels.Length == 0)
            {
                sb.AppendLine("  パネルが 1 枚も見つかりません。");
                sb.AppendLine("  → SmartMediaPlayer.prefab(壁パネル入り)を");
                sb.AppendLine("    Hierarchy へドラッグしてください。");
                return sb.ToString();
            }

            for (int i = 0; i < panels.Length; i++)
            {
                Inspect(panels[i], sb);
            }

            sb.AppendLine();
            sb.AppendLine("  「No Function」や「裏の UdonBehaviour なし」が 1 件でもあるなら、");
            sb.AppendLine("  " + RepairMenu + " を実行してください。");
            return sb.ToString();
        }

        private static void Inspect(UdonMediaPanel panel, StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("  ── " + Path(panel.gameObject));

            // 1. パネル自身の配線
            sb.AppendLine("     Core            : " + (panel.Core != null ? "OK" : "未設定 ← これが無いと何も出ない"));
            sb.AppendLine("     NowPlaying      : " + (panel.NowPlaying != null ? "OK" : "なし"));
            sb.AppendLine("     Transport       : " + (panel.Transport != null ? "OK" : "なし"));
            sb.AppendLine("     Lists           : "
                          + (panel.Lists != null ? panel.Lists.Length + " 本" : "なし"));
            sb.AppendLine("     タブ            : "
                          + (panel.Tabs != null ? panel.Tabs.TabCount + " 枚" : "なし(全部同時に出る)"));

            // 2. 裏の UdonBehaviour があるか
            int behaviours = 0;
            int missingBacking = 0;

            var all = panel.GetComponentsInChildren<UdonSharpBehaviour>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                behaviours++;
                if (UdonSharpEditorUtility.GetBackingUdonBehaviour(all[i]) == null)
                {
                    missingBacking++;
                    Debug.LogWarning("  裏の UdonBehaviour なし: " + Path(all[i].gameObject), all[i]);
                }
            }
            sb.AppendLine("     U# コンポーネント: " + behaviours + " 個 / 裏なし "
                          + missingBacking + " 個"
                          + (missingBacking > 0 ? "  ← 要修復" : ""));

            // 3. ボタンの On Click
            int buttons = 0;
            int wired = 0;
            int noFunction = 0;

            var allButtons = panel.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < allButtons.Length; i++)
            {
                Button button = allButtons[i];
                if (button == null) continue;

                buttons++;
                if (IsWired(button)) wired++;
                else
                {
                    noFunction++;
                    Debug.LogWarning("  On Click が空: " + Path(button.gameObject), button);
                }
            }
            sb.AppendLine("     Button          : " + buttons + " 個 / 繋がっている " + wired
                          + " 個 / No Function " + noFunction + " 個"
                          + (noFunction > 0 ? "  ← 要修復" : ""));

            // 4. 「使う」で押せるか(実機ではこちらが本命)
            int interact = 0;
            for (int i = 0; i < allButtons.Length; i++)
            {
                Button button = allButtons[i];
                if (button == null) continue;

                if (button.GetComponent<BoxCollider>() == null) continue;
                if (button.GetComponent<UdonMediaControlButton>() == null) continue;
                interact++;
            }
            sb.AppendLine("     「使う」で押せる: " + interact + " / " + buttons + " 個"
                          + (interact < buttons ? "  ← 要修復" : ""));

            // 5. 同期(Phase5-4)
            InspectSync(panel, sb);

            // 6. 映像が出るか(Phase7-2)
            InspectScreen(panel, sb);
        }

        /// <summary>同期がつながっているかを見る。</summary>
        private static void InspectSync(UdonMediaPanel panel, StringBuilder sb)
        {
            if (panel.Core == null) return;

            UdonMediaController controller = panel.Controller != null
                ? panel.Controller
                : panel.Core.Controller;

            UdonSyncCoordinator sync = panel.Core.Sync;

            if (sync == null && (controller == null || controller.Sync == null))
            {
                sb.AppendLine("     同期            : なし(1 人用。全員が別々のものを見ます)");
                return;
            }

            if (sync == null) sync = controller.Sync;

            sb.AppendLine("     同期            : "
                          + (sync.Enabled ? "有効" : "無効(Enabled が false)")
                          + " / " + AccessLabel(sync.AccessPolicy));

            if (controller != null && controller.Sync == null)
            {
                sb.AppendLine("       ← 窓口に Sync が挿さっていません(操作が同期しません)");
            }
            if (sync.Session == null) sb.AppendLine("       ← Sync の Session が未設定");
            if (sync.Backend == null) sb.AppendLine("       ← Sync の Backend が未設定(位置合わせができません)");
            if (sync.Controller == null) sb.AppendLine("       ← Sync の Controller が未設定(受信しても画面が更新されません)");
        }

        /// <summary>
        /// <b>映像が出ない原因を探す。</b>Phase7-2。
        ///
        /// 実際に踏んだ原因は<b>マテリアル</b>でした。
        /// <c>CreatePrimitive</c> が付ける <c>Default-Material</c> は
        /// <b>Standard(ライトの影響を受ける)</b>なので、
        /// ライトが当たっていない場所に置くと<b>動画は流れているのに面が真っ黒</b>になります。
        /// ワールドによって映ったり映らなかったりするのはこれが理由です。
        /// </summary>
        private static void InspectScreen(UdonMediaPanel panel, StringBuilder sb)
        {
            if (panel.Core == null) return;

            UdonMediaScreen screen = panel.Core.Screen;
            if (screen == null)
            {
                sb.AppendLine("     映像            : Screen が未設定 ← 映りません");
                return;
            }

            if (screen.Surface == null)
            {
                sb.AppendLine("     映像            : Surface が未設定 ← 映りません");
                return;
            }

            Material material = screen.Surface.sharedMaterial;

            if (material == null)
            {
                sb.AppendLine("     映像            : Surface にマテリアルがありません ← 映りません");
                return;
            }

            string shaderName = material.shader != null ? material.shader.name : "(不明)";
            bool unlit = shaderName.Contains("Unlit") || shaderName.Contains("unlit");

            sb.AppendLine("     映像            : " + shaderName
                          + (unlit ? "" : "  ← ライトが要るシェーダー。暗い場所では真っ黒になります"));

            if (material.name == "Default-Material")
            {
                sb.AppendLine("       ← Unity の組み込みマテリアルのままです。");
                sb.AppendLine("         Prefab を作り直すと Unlit のものが割り当てられます。");
            }

            if (!screen.Surface.enabled)
            {
                sb.AppendLine("       ← Renderer が切られています");
            }

            sb.AppendLine("       ヒント: 板は裏から見ると透明です。");
            sb.AppendLine("               何も映らないときは、反対側に回ってみてください。");
        }

        private static string AccessLabel(int policy)
        {
            if (policy == UdonSyncCoordinator.AccessMasterOnly) return "マスターだけ操作できる";
            if (policy == UdonSyncCoordinator.AccessOwnerOnly) return "いまの持ち主だけ操作できる";
            return "誰でも操作できる";
        }

        /// <summary>その Button が Udon を呼べる状態か。</summary>
        private static bool IsWired(Button button)
        {
            int count = button.onClick.GetPersistentEventCount();

            for (int i = 0; i < count; i++)
            {
                if (button.onClick.GetPersistentTarget(i) == null) continue;
                if (button.onClick.GetPersistentMethodName(i) != "SendCustomEvent") continue;
                return true;
            }
            return false;
        }

        // ───────── 修復 ─────────

        [MenuItem(RepairMenu, false, -59)]
        public static void RepairMenuItem()
        {
            // uGUI が動く前提を先に整える。パネルが無くても、これだけはやる価値がある。
            bool madeEventSystem = UdonWorldUiKit.EnsureEventSystem();

            // すでに置いてあるパネルには当たり判定が無いことがある(Phase7-2 以前)。
            int colliders = AddCanvasColliders();

            // VRC_UiShape が無いと、ボタンは「使う」で動いても
            // つまみ・音量・検索欄が実機で沈黙することがある(Phase7-4)。
            int uiShapes = AddUiShapes();

            UdonMediaPanel[] panels = FindPanels();

            if (panels.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "Smart Media Platform",
                    (madeEventSystem ? "EventSystem を作りました。\n\n" : "")
                    + "シーンにパネルが見つかりません。\n"
                    + "SmartMediaPlayer.prefab を Hierarchy へドラッグしてから実行してください。",
                    "OK");
                return;
            }

            UdonWorldUiKit.ResetCounters();

            int rewired = 0;
            int synced = 0;
            int relabelled = 0;

            for (int i = 0; i < panels.Length; i++)
            {
                rewired += Rewire(panels[i]);
                relabelled += Relabel(panels[i]);
                synced += SyncPanel(panels[i]);
            }

            EditorSceneManager.MarkAllScenesDirty();

            string message =
                (madeEventSystem ? "EventSystem を作りました(uGUI に必須)。\n" : "")
                + (colliders > 0
                    ? "Canvas に当たり判定を " + colliders + " 個 足しました"
                      + "(これが無いとレーザーが届きません)。\n"
                    : "")
                + (uiShapes > 0
                    ? "Canvas に VRC_UiShape を " + uiShapes + " 個 足しました"
                      + "(これが無いと実機でつまみ・音量・検索欄が反応しないことがあります)。\n"
                    : "")
                + "ボタンを " + rewired + " 個 繋ぎ直しました。\n"
                + "  uGUI(onClick)   : " + UdonWorldUiKit.BindCount + " 個\n"
                + "  「使う」(Interact): " + UdonWorldUiKit.InteractCount + " 個\n"
                + "見出しを " + relabelled + " 個 日本語にしました。\n"
                + "Udon へ " + synced + " 個 書き戻しました。\n\n"
                + "シーンを保存してから Build & Test してください。";

            Debug.Log("[UdonMediaPanelDoctor] " + message);
            EditorUtility.DisplayDialog("Smart Media Platform", message, "OK");
        }

        /// <summary>
        /// 名前から繋ぎ先を決めて、<c>Button.onClick</c> を張り直す。
        ///
        /// 送り先は<b>いちばん近い親</b>にいる部品です。
        /// <list type="bullet">
        /// <item><c>Transport/PlayPause</c> → <c>UdonTransportView.TogglePlayPause</c></item>
        /// <item><c>Library/NextPage</c> → <c>UdonMediaListView.NextPage</c></item>
        /// <item><c>Row0/Content/Hit</c> → <c>UdonMediaListRow.Click</c></item>
        /// </list>
        /// </summary>
        private static int Rewire(UdonMediaPanel panel)
        {
            int count = 0;

            var buttons = panel.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null) continue;

                UdonSharpBehaviour target;
                string eventName;
                string caption;
                if (!Resolve(button, out target, out eventName, out caption)) continue;

                ClearUdonListeners(button);
                UdonWorldUiKit.Bind(button, target, eventName);

                // 実機の本命はこちら。すでに付いているなら足さない。
                if (button.GetComponent<BoxCollider>() == null
                    || button.GetComponent<UdonMediaControlButton>() == null)
                {
                    RemoveInteract(button);
                    UdonWorldUiKit.AddInteract(button, target, eventName, caption);
                }

                count++;
            }

            return count;
        }

        /// <summary>
        /// <b>英語の見出しを日本語に直す。</b>Phase6-4。
        ///
        /// <b>すでに置いてある Prefab のためのものです。</b>
        /// <c>UdonMediaListView.HeaderLabel</c> は Phase6-4 から
        /// <b>空にしておくと種類に合わせた日本語</b>が出るようになりました。
        /// 古い Prefab には <c>Library</c> / <c>Queue</c> が焼かれているので、
        /// <b>ここで空に戻して既定に任せます</b>(作り直しは要りません)。
        ///
        /// <b>自分で付けた名前は残します。</b>置き換えるのは
        /// ビルダーが入れていた英語の名前だけです。
        /// </summary>
        private static int Relabel(UdonMediaPanel panel)
        {
            int changed = 0;

            var lists = panel.GetComponentsInChildren<UdonMediaListView>(true);
            for (int i = 0; i < lists.Length; i++)
            {
                UdonMediaListView list = lists[i];
                if (list == null) continue;

                if (!IsBuilderEnglish(list.HeaderLabel)) continue;

                list.HeaderLabel = "";
                if (list.HeaderText != null) list.HeaderText.text = list.EffectiveHeader();

                changed++;
            }

            return changed;
        }

        /// <summary>ビルダーが Phase6-3 まで入れていた見出しか。</summary>
        private static bool IsBuilderEnglish(string label)
        {
            if (string.IsNullOrEmpty(label)) return false;

            return label == "Library" || label == "Queue" || label == "Related" || label == "関連";
        }

        /// <summary>「使う」の部品を一度外す。半端に付いている状態から作り直すため。</summary>
        private static void RemoveInteract(Button button)
        {
            var relay = button.GetComponent<UdonMediaControlButton>();
            if (relay != null)
            {
                var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(relay);
                UnityEngine.Object.DestroyImmediate(relay);
                if (udon != null) UnityEngine.Object.DestroyImmediate(udon);
            }

            var box = button.GetComponent<BoxCollider>();
            if (box != null) UnityEngine.Object.DestroyImmediate(box);
        }

        private static bool Resolve(
            Button button, out UdonSharpBehaviour target, out string eventName, out string caption)
        {
            target = null;
            eventName = null;
            caption = null;

            string name = button.gameObject.name;

            var row = button.GetComponentInParent<UdonMediaListRow>();
            if (row != null)
            {
                target = row;

                bool queue = false;
                var owner = button.GetComponentInParent<UdonMediaListView>();
                if (owner != null) queue = owner.Source == UdonMediaListView.SourceQueue;

                if (name == "Hit")
                {
                    eventName = "Click";
                    caption = queue ? "この曲へ移動" : "再生";
                }
                else if (name == "HeaderHit")
                {
                    // Phase7-2: チャンネルの見出しも同じ Click へ送る。
                    // 見出しか曲かは一覧側が位置から判断する。
                    eventName = "Click";
                    caption = "開く / たたむ";
                }
                else if (name == "Secondary")
                {
                    eventName = "ClickSecondary";
                    caption = queue ? "再生予定から外す" : "再生予定に追加";
                }
                return eventName != null;
            }

            var list = button.GetComponentInParent<UdonMediaListView>();
            if (list != null)
            {
                target = list;
                // Phase5-5 でページ送りからスクロールへ変えた。
                // すでに置いてある Prefab のために、旧名(NextPage / PreviousPage)も受ける。
                if (name == "ScrollDown" || name == "NextPage")
                {
                    eventName = "ScrollDown";
                    caption = "下へ(続けて押すと速い)";
                }
                else if (name == "ScrollUp" || name == "PreviousPage")
                {
                    eventName = "ScrollUp";
                    caption = "上へ(続けて押すと速い)";
                }
                else if (name == "SearchClear")
                {
                    eventName = "ClearSearch";
                    caption = "検索をやめる";
                }
                else if (name == "ScrollHome" || name == "FirstPage")
                {
                    // Phase6-4 で足した「迷子からの復帰」。
                    eventName = "ScrollHome";
                    caption = "再生中 / 先頭へ";
                }
                return eventName != null;
            }

            // Phase7: タブ。一覧より先に見る(タブは一覧の外にあるため)。
            var tabs = button.GetComponentInParent<UdonMediaTabs>();
            if (tabs != null && name.StartsWith("Tab"))
            {
                target = tabs;
                caption = "ここを見る";

                if (name == "Tab0") eventName = "SelectTab0";
                else if (name == "Tab1") eventName = "SelectTab1";
                else if (name == "Tab2") eventName = "SelectTab2";

                return eventName != null;
            }

            var transport = button.GetComponentInParent<UdonTransportView>();
            if (transport != null)
            {
                target = transport;

                if (name == "PlayPause") { eventName = "TogglePlayPause"; caption = "再生 / 一時停止"; }
                else if (name == "Next") { eventName = "Next"; caption = "次へ"; }
                else if (name == "Previous") { eventName = "Previous"; caption = "前へ"; }
                else if (name == "Stop") { eventName = "Stop"; caption = "停止"; }
                else if (name == "VolumeUp") { eventName = "VolumeUp"; caption = "音量を上げる"; }
                else if (name == "VolumeDown") { eventName = "VolumeDown"; caption = "音量を下げる"; }
                else if (name == "ClearUpcoming") { eventName = "ClearUpcoming"; caption = "再生予定を空にする"; }
                else if (name == "More") { eventName = "ToggleMore"; caption = "そのほかの操作"; }

                return eventName != null;
            }

            Debug.LogWarning(
                "[UdonMediaPanelDoctor] " + Path(button.gameObject)
                + " の繋ぎ先が分かりません(名前を変えていませんか)。", button);
            return false;
        }

        /// <summary>
        /// すでにある Udon 向けリスナーを外す。
        /// 張り直しのたびに増えていくと、1 回押しただけで 2 回進むようになるため。
        /// </summary>
        private static void ClearUdonListeners(Button button)
        {
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                string method = button.onClick.GetPersistentMethodName(i);
                if (method != "SendCustomEvent" && !string.IsNullOrEmpty(method)) continue;

                UnityEditor.Events.UnityEventTools.RemovePersistentListener(button.onClick, i);
            }
        }

        private static int SyncPanel(UdonMediaPanel panel)
        {
            int before = UdonWorldUiKit.SyncedCount;

            UdonWorldUiKit.SyncProxies(panel.gameObject);

            // Core 側(Session / Controller / Sync など)も一緒に写しておく。
            // パネルだけ直しても、窓口の参照が空なら何も起きない。
            if (panel.Core != null) UdonWorldUiKit.SyncProxies(panel.Core.gameObject);

            return UdonWorldUiKit.SyncedCount - before;
        }

        // ───────── 共通 ─────────

        private static UdonMediaPanel[] FindPanels()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<UdonMediaPanel>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return UnityEngine.Object.FindObjectsOfType<UdonMediaPanel>(true);
#endif
        }

        private static string Path(GameObject go)
        {
            string path = go.name;

            for (Transform parent = go.transform.parent; parent != null; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }
            return path;
        }
    }
}
#endif
