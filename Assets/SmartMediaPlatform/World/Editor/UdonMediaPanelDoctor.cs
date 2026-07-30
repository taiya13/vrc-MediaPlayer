#if UNITY_EDITOR && VRC_SDK_VRCSDK3
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

        // ───────── 診断 ─────────

        [MenuItem(DiagnoseMenu)]
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

            if (panels.Length == 0)
            {
                sb.AppendLine("  パネルが 1 枚も見つかりません。");
                sb.AppendLine("  → SmartMediaPlayer.prefab か MediaWallPanel.prefab を");
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

        [MenuItem(RepairMenu)]
        public static void RepairMenuItem()
        {
            UdonMediaPanel[] panels = FindPanels();

            if (panels.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "Smart Media Platform",
                    "シーンにパネルが見つかりません。\n"
                    + "SmartMediaPlayer.prefab を Hierarchy へドラッグしてから実行してください。",
                    "OK");
                return;
            }

            UdonWorldUiKit.ResetCounters();

            int rewired = 0;
            int synced = 0;

            for (int i = 0; i < panels.Length; i++)
            {
                rewired += Rewire(panels[i]);
                synced += SyncPanel(panels[i]);
            }

            EditorSceneManager.MarkAllScenesDirty();

            string message =
                "ボタンを " + rewired + " 個 繋ぎ直しました。\n"
                + "  uGUI(onClick)   : " + UdonWorldUiKit.BindCount + " 個\n"
                + "  「使う」(Interact): " + UdonWorldUiKit.InteractCount + " 個\n"
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
                else if (name == "Secondary")
                {
                    eventName = "ClickSecondary";
                    caption = queue ? "Queue から外す" : "Queue に追加";
                }
                return eventName != null;
            }

            var list = button.GetComponentInParent<UdonMediaListView>();
            if (list != null)
            {
                target = list;
                if (name == "NextPage") { eventName = "NextPage"; caption = "次のページ"; }
                else if (name == "PreviousPage") { eventName = "PreviousPage"; caption = "前のページ"; }
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
                else if (name == "ClearUpcoming") { eventName = "ClearUpcoming"; caption = "Queue を空にする"; }

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

            // Core 側(Session / Controller など)も一緒に写しておく。
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
