#if UNITY_EDITOR && VRC_SDK_VRCSDK3
using System;
using SmartMediaPlatform.Video.EditorTools;
using SmartMediaPlatform.World.Udon;
using SmartMediaPlatform.World.Udon.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SmartMediaPlatform.World.EditorTools
{
    /// <summary>
    /// <b>操作 UI(パネル)を組み立てるエディタツール。</b>Phase5-3。
    ///
    /// <b>ここが「Controller は差し替え可能」の実体です。</b>
    /// 壁パネルも手持ちリモコンも<b>同じ <see cref="UdonMediaPanel"/> 1 クラス</b>で、
    /// 違うのは「どの表示を、どの大きさで並べたか」だけです。
    /// <list type="bullet">
    /// <item><see cref="BuildWallPanel"/> …… いま鳴っているもの + 操作 + Library / 関連 / Queue</item>
    /// <item><see cref="BuildRemotePanel"/> …… いま鳴っているもの + 操作 + Queue</item>
    /// </list>
    /// タブレットや別レイアウトを足したくなったら、
    /// <b>ここにメソッドを 1 つ増やすだけ</b>です。Udon 側のコードは変わりません。
    ///
    /// <b>組み立てはすべてコードで行います。</b>出来合いの Canvas を Prefab で同梱すると
    /// UdonSharp のプログラム(.asset)が SDK 更新で行方不明になるためです
    /// (Phase3-3 / Phase5-1 で実際に踏んだ問題)。
    /// </summary>
    public static class UdonMediaPanelBuilder
    {
        /// <summary>この Prefab が使う UdonSharpBehaviour。プログラムを先に作る対象。</summary>
        public static Type[] BehaviourTypes()
        {
            return new[]
            {
                typeof(UdonMediaPanel),
                typeof(UdonNowPlayingView),
                typeof(UdonTransportView),
                typeof(UdonMediaListView),
                typeof(UdonMediaListRow),
            };
        }

        /// <summary>コンパイル待ちのため組み立てを中断すべきか。</summary>
        public static bool NeedsCompile { get; private set; }

        public static void ResetNeedsCompile()
        {
            NeedsCompile = false;
        }

        // ───────── 壁パネル ─────────

        /// <summary>
        /// <b>壁に貼る全部入り。</b>900 × 1390 px ≒ 1.17 m × 1.81 m。
        /// </summary>
        public static UdonMediaPanel BuildWallPanel(GameObject parent, string objectName)
        {
            const float W = 900f;
            const float H = 1390f;
            const float Pad = 24f;
            const float CW = W - Pad * 2f;

            GameObject root = NewChild(parent, objectName);
            RectTransform canvas = UdonWorldUiKit.WorldCanvas(root, "Canvas", W, H, 0.0013f);
            RectTransform body = UdonWorldUiKit.Place(canvas, "Body", 0f, 0f, W, H);

            UdonWorldUiKit.Plate(body, "Backplate", 0f, 0f, W, H, UdonWorldUiKit.Backplate)
                .raycastTarget = false;

            Text panelTitle = UdonWorldUiKit.Label(
                body, "PanelTitle", Pad, 24f, CW * 0.5f, 44f, 30,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextPrimary);

            // いま誰が操作しているか(同期していないときは空のまま)
            Text syncOwner = UdonWorldUiKit.Label(
                body, "SyncOwner", Pad + CW * 0.5f, 24f, CW * 0.5f, 44f, 20,
                TextAnchor.MiddleRight, UdonWorldUiKit.TextSecondary);

            var panel = Add<UdonMediaPanel>(root);
            if (panel == null) return null;

            // ── いま鳴っているもの
            var nowPlaying = BuildNowPlaying(body, Pad, 76f, CW, 36, 22, 20);
            if (nowPlaying != null) nowPlaying.SyncText = syncOwner;
            if (NeedsCompile) return panel;

            // ── 操作
            var transport = BuildTransport(body, Pad, 226f, CW, 68f, 52f, 24, 20, true);
            if (NeedsCompile) return panel;

            // ── 3 つの一覧
            var librarySpec = new ListSpec();
            librarySpec.Name = "Library";
            librarySpec.Source = UdonMediaListView.SourceLibrary;
            librarySpec.Header = "Library";
            librarySpec.SecondaryCaption = "＋";
            librarySpec.RowCount = 6;
            librarySpec.Width = CW;

            float libraryHeight;
            var library = BuildList(body, Pad, 368f, librarySpec, out libraryHeight);
            if (NeedsCompile) return panel;

            var relatedSpec = new ListSpec();
            relatedSpec.Name = "Related";
            relatedSpec.Source = UdonMediaListView.SourceRelated;
            relatedSpec.Header = "関連";
            relatedSpec.SecondaryCaption = "＋";
            relatedSpec.RowCount = 3;
            relatedSpec.Width = CW;

            float relatedHeight;
            var related = BuildList(body, Pad, 368f + libraryHeight + 12f, relatedSpec, out relatedHeight);
            if (NeedsCompile) return panel;

            var queueSpec = new ListSpec();
            queueSpec.Name = "Queue";
            queueSpec.Source = UdonMediaListView.SourceQueue;
            queueSpec.Header = "Queue";
            queueSpec.SecondaryCaption = "×";
            queueSpec.PrimaryCaption = "この曲へ移動";
            queueSpec.SecondaryHint = "Queue から外す";
            queueSpec.RowCount = 4;
            queueSpec.Width = CW;

            float queueHeight;
            float queueY = 368f + libraryHeight + 12f + relatedHeight + 12f;
            var queue = BuildList(body, Pad, queueY, queueSpec, out queueHeight);
            if (NeedsCompile) return panel;

            Text status = UdonWorldUiKit.Label(
                body, "Status", Pad, queueY + queueHeight + 14f, CW, 36f, 20,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextSecondary);

            Finish(panel, body, panelTitle, status, "Smart Media Player",
                   nowPlaying, transport, new[] { library, related, queue });

            return panel;
        }

        // ───────── 手持ちリモコン ─────────

        /// <summary>
        /// <b>最小構成。</b>620 × 590 px ≒ 0.50 m × 0.47 m。
        ///
        /// <b>Library も関連も持ちません。</b>それでも動くことが
        /// 「UI がロジックを持っていない」ことの一番わかりやすい証明になります。
        /// </summary>
        public static UdonMediaPanel BuildRemotePanel(GameObject parent, string objectName)
        {
            const float W = 620f;
            const float H = 590f;
            const float Pad = 20f;
            const float CW = W - Pad * 2f;

            GameObject root = NewChild(parent, objectName);
            RectTransform canvas = UdonWorldUiKit.WorldCanvas(root, "Canvas", W, H, 0.0008f);
            RectTransform body = UdonWorldUiKit.Place(canvas, "Body", 0f, 0f, W, H);

            UdonWorldUiKit.Plate(body, "Backplate", 0f, 0f, W, H, UdonWorldUiKit.Backplate)
                .raycastTarget = false;

            Text panelTitle = UdonWorldUiKit.Label(
                body, "PanelTitle", Pad, 16f, CW * 0.45f, 36f, 24,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextPrimary);

            Text syncOwner = UdonWorldUiKit.Label(
                body, "SyncOwner", Pad + CW * 0.45f, 16f, CW * 0.55f, 36f, 16,
                TextAnchor.MiddleRight, UdonWorldUiKit.TextSecondary);

            var panel = Add<UdonMediaPanel>(root);
            if (panel == null) return null;

            var nowPlaying = BuildNowPlaying(body, Pad, 60f, CW, 26, 17, 17);
            if (nowPlaying != null) nowPlaying.SyncText = syncOwner;
            if (NeedsCompile) return panel;

            var transport = BuildTransport(body, Pad, 180f, CW, 60f, 44f, 20, 17, true);
            if (NeedsCompile) return panel;

            var queueSpec = new ListSpec();
            queueSpec.Name = "Queue";
            queueSpec.Source = UdonMediaListView.SourceQueue;
            queueSpec.Header = "Queue";
            queueSpec.SecondaryCaption = "×";
            queueSpec.PrimaryCaption = "この曲へ移動";
            queueSpec.SecondaryHint = "Queue から外す";
            queueSpec.RowCount = 3;
            queueSpec.Width = CW;
            queueSpec.RowHeight = 52f;
            queueSpec.HeaderHeight = 34f;
            queueSpec.HeaderSize = 20;
            queueSpec.TitleSize = 19;
            queueSpec.SubSize = 14;

            float queueHeight;
            var queue = BuildList(body, Pad, 306f, queueSpec, out queueHeight);
            if (NeedsCompile) return panel;

            Text status = UdonWorldUiKit.Label(
                body, "Status", Pad, 306f + queueHeight + 12f, CW, 30f, 16,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextSecondary);

            Finish(panel, body, panelTitle, status, "リモコン",
                   nowPlaying, transport, new[] { queue });

            return panel;
        }

        // ───────── 中身 ─────────

        private static UdonNowPlayingView BuildNowPlaying(
            RectTransform body, float x, float y, float width,
            int titleSize, int artistSize, int metaSize)
        {
            float titleHeight = titleSize * 1.35f;
            float artistHeight = artistSize * 1.4f;
            float metaHeight = metaSize * 1.5f;

            RectTransform section = UdonWorldUiKit.Place(
                body, "NowPlaying", x, y, width,
                titleHeight + artistHeight + 12f + 12f + metaHeight + 6f);

            var view = Add<UdonNowPlayingView>(section.gameObject);
            if (view == null) return null;

            float cursor = 0f;

            view.TitleText = UdonWorldUiKit.Label(
                section, "Title", 0f, cursor, width, titleHeight, titleSize,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextPrimary);
            cursor += titleHeight + 2f;

            view.ArtistText = UdonWorldUiKit.Label(
                section, "Artist", 0f, cursor, width, artistHeight, artistSize,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextSecondary);
            cursor += artistHeight + 8f;

            view.ProgressFill = UdonWorldUiKit.ProgressBar(section, "Progress", 0f, cursor, width, 12f);
            cursor += 12f + 6f;

            float third = (width - 24f) / 3f;

            view.TimeText = UdonWorldUiKit.Label(
                section, "Time", 0f, cursor, third, metaHeight, metaSize,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextSecondary);

            view.QueueCountText = UdonWorldUiKit.Label(
                section, "QueueCount", third + 12f, cursor, third, metaHeight, metaSize,
                TextAnchor.MiddleCenter, UdonWorldUiKit.TextSecondary);

            view.StateText = UdonWorldUiKit.Label(
                section, "State", (third + 12f) * 2f, cursor, third, metaHeight, metaSize,
                TextAnchor.MiddleRight, UdonWorldUiKit.TextSecondary);

            view.TitleText.text = "(何も再生していません)";
            return view;
        }

        private static UdonTransportView BuildTransport(
            RectTransform body, float x, float y, float width,
            float mainHeight, float subHeight, int mainSize, int subSize, bool showClear)
        {
            float gap = Mathf.Round(width * 0.023f);
            float rowGap = 12f;

            RectTransform section = UdonWorldUiKit.Place(
                body, "Transport", x, y, width, mainHeight + rowGap + subHeight);

            var view = Add<UdonTransportView>(section.gameObject);
            if (view == null) return null;

            // ── 1 段目:前へ / 再生・一時停止 / 次へ / 停止
            // 再生ボタンだけ 2 倍幅。いちばん押すものがいちばん大きいのが望ましい。
            float unit = Mathf.Floor((width - gap * 3f) / 5f);

            Text unusedLabel;
            Text playPauseLabel;

            Button previous = UdonWorldUiKit.PushButton(
                section, "Previous", 0f, 0f, unit, mainHeight, "◀◀  前へ", mainSize,
                UdonWorldUiKit.ButtonFace, out unusedLabel);

            Button playPause = UdonWorldUiKit.PushButton(
                section, "PlayPause", unit + gap, 0f, unit * 2f, mainHeight, "▶  再生", mainSize,
                UdonWorldUiKit.ButtonAccent, out playPauseLabel);

            Button next = UdonWorldUiKit.PushButton(
                section, "Next", unit * 3f + gap * 2f, 0f, unit, mainHeight, "次へ  ▶▶", mainSize,
                UdonWorldUiKit.ButtonFace, out unusedLabel);

            float stopX = unit * 4f + gap * 3f;
            Button stop = UdonWorldUiKit.PushButton(
                section, "Stop", stopX, 0f, width - stopX, mainHeight, "■  停止", mainSize,
                UdonWorldUiKit.ButtonFace, out unusedLabel);

            // ── 2 段目:音量 と Queue の掃除
            float subY = mainHeight + rowGap;
            float volumeButton = Mathf.Round(subHeight * 1.7f);
            float volumeText = Mathf.Round(subHeight * 4.2f);

            Button volumeDown = UdonWorldUiKit.PushButton(
                section, "VolumeDown", 0f, subY, volumeButton, subHeight, "−", subSize + 4,
                UdonWorldUiKit.ButtonFace, out unusedLabel);

            Text volumeLabel = UdonWorldUiKit.Label(
                section, "Volume", volumeButton + rowGap, subY, volumeText, subHeight, subSize,
                TextAnchor.MiddleCenter, UdonWorldUiKit.TextSecondary);

            float volumeUpX = volumeButton + rowGap + volumeText + rowGap;
            Button volumeUp = UdonWorldUiKit.PushButton(
                section, "VolumeUp", volumeUpX, subY, volumeButton, subHeight, "＋", subSize + 4,
                UdonWorldUiKit.ButtonFace, out unusedLabel);

            Button clear = null;
            if (showClear)
            {
                float clearX = volumeUpX + volumeButton + rowGap;
                clear = UdonWorldUiKit.PushButton(
                    section, "ClearUpcoming", clearX, subY, width - clearX, subHeight,
                    "Queue を空に", subSize, UdonWorldUiKit.ButtonFace, out unusedLabel);
            }

            view.PlayPauseLabel = playPauseLabel;
            view.VolumeText = volumeLabel;

            UdonWorldUiKit.Wire(previous, view, "Previous", "前へ");
            UdonWorldUiKit.Wire(playPause, view, "TogglePlayPause", "再生 / 一時停止");
            UdonWorldUiKit.Wire(next, view, "Next", "次へ");
            UdonWorldUiKit.Wire(stop, view, "Stop", "停止");
            UdonWorldUiKit.Wire(volumeDown, view, "VolumeDown", "音量を下げる");
            UdonWorldUiKit.Wire(volumeUp, view, "VolumeUp", "音量を上げる");
            if (clear != null) UdonWorldUiKit.Wire(clear, view, "ClearUpcoming", "Queue を空にする");

            return view;
        }

        /// <summary>一覧 1 つぶんの見た目の指定。</summary>
        private sealed class ListSpec
        {
            public string Name;
            public int Source;
            public string Header;
            public string SecondaryCaption = "＋";
            public string PrimaryCaption = "再生";
            public string SecondaryHint = "Queue に追加";
            public int RowCount = 6;
            public float Width = 852f;
            public float RowHeight = 56f;
            public float RowGap = 6f;
            public float HeaderHeight = 38f;
            public int HeaderSize = 24;
            public int TitleSize = 23;
            public int SubSize = 16;
        }

        private static UdonMediaListView BuildList(
            RectTransform body, float x, float y, ListSpec spec, out float consumedHeight)
        {
            float rowsY = spec.HeaderHeight + 8f;
            consumedHeight = rowsY + spec.RowCount * (spec.RowHeight + spec.RowGap) - spec.RowGap;

            RectTransform section = UdonWorldUiKit.Place(
                body, spec.Name, x, y, spec.Width, consumedHeight);

            var view = Add<UdonMediaListView>(section.gameObject);
            if (view == null) return null;

            view.Source = spec.Source;
            view.HeaderLabel = spec.Header;

            view.HeaderText = UdonWorldUiKit.Label(
                section, "Header", 0f, 0f, spec.Width * 0.5f, spec.HeaderHeight, spec.HeaderSize,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextPrimary);
            view.HeaderText.text = spec.Header;

            // ページ送りは見出しの右端に寄せる
            float pageButton = Mathf.Round(spec.HeaderHeight * 1.7f);
            float pageText = Mathf.Round(spec.HeaderHeight * 2.7f);
            float nextX = spec.Width - pageButton;
            float textX = nextX - pageText - 12f;
            float prevX = textX - pageButton - 12f;

            Text unusedLabel;

            Button previousPage = UdonWorldUiKit.PushButton(
                section, "PreviousPage", prevX, 0f, pageButton, spec.HeaderHeight, "‹",
                spec.HeaderSize, UdonWorldUiKit.ButtonFace, out unusedLabel);

            view.PageText = UdonWorldUiKit.Label(
                section, "Page", textX, 0f, pageText, spec.HeaderHeight, spec.SubSize + 2,
                TextAnchor.MiddleCenter, UdonWorldUiKit.TextSecondary);

            Button nextPage = UdonWorldUiKit.PushButton(
                section, "NextPage", nextX, 0f, pageButton, spec.HeaderHeight, "›",
                spec.HeaderSize, UdonWorldUiKit.ButtonFace, out unusedLabel);

            view.PreviousPageButton = previousPage.gameObject;
            view.NextPageButton = nextPage.gameObject;

            UdonWorldUiKit.Wire(previousPage, view, "PreviousPage", "前のページ");
            UdonWorldUiKit.Wire(nextPage, view, "NextPage", "次のページ");

            Text empty = UdonWorldUiKit.Label(
                section, "Empty", 0f, rowsY, spec.Width, spec.RowHeight, spec.SubSize + 2,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextSecondary);
            empty.text = "(まだありません)";
            view.EmptyMessage = empty.gameObject;

            var rows = new UdonMediaListRow[spec.RowCount];
            for (int i = 0; i < spec.RowCount; i++)
            {
                float rowY = rowsY + i * (spec.RowHeight + spec.RowGap);

                UdonMediaListRow row = BuildRow(section, "Row" + i, rowY, spec);
                if (row == null) return view;

                row.Row = i;
                row.List = view;
                rows[i] = row;
            }
            view.Rows = rows;

            return view;
        }

        private static UdonMediaListRow BuildRow(
            RectTransform parent, string name, float y, ListSpec spec)
        {
            float width = spec.Width;
            float height = spec.RowHeight;

            RectTransform rowRect = UdonWorldUiKit.Place(parent, name, 0f, y, width, height);

            var row = Add<UdonMediaListRow>(rowRect.gameObject);
            if (row == null) return null;

            float secondary = Mathf.Round(height * 1.3f);
            float hitWidth = width - secondary - 12f;
            float indexWidth = Mathf.Round(height * 0.9f);
            float durationWidth = Mathf.Round(width * 0.15f);
            float titleX = indexWidth + 20f;
            float titleWidth = hitWidth - titleX - durationWidth - 16f;

            // 中身は「行そのもの」ではなく子に置く。
            // 空行で非アクティブにする対象が UdonBehaviour 本体だと、
            // 二度と書き戻せなくなるため(UdonMediaListRow の注意書きを参照)。
            RectTransform content = UdonWorldUiKit.Place(rowRect, "Content", 0f, 0f, width, height);

            Button hit = UdonWorldUiKit.HitArea(
                content, "Hit", 0f, 0f, hitWidth, height, UdonWorldUiKit.RowFace);

            row.IndexText = UdonWorldUiKit.Label(
                hit.transform, "Index", 8f, 0f, indexWidth, height, spec.SubSize + 2,
                TextAnchor.MiddleCenter, UdonWorldUiKit.TextSecondary);

            row.TitleText = UdonWorldUiKit.Label(
                hit.transform, "Title", titleX, height * 0.08f, titleWidth, height * 0.48f,
                spec.TitleSize, TextAnchor.LowerLeft, UdonWorldUiKit.TextPrimary);

            row.SubText = UdonWorldUiKit.Label(
                hit.transform, "Sub", titleX, height * 0.56f, titleWidth, height * 0.36f,
                spec.SubSize, TextAnchor.UpperLeft, UdonWorldUiKit.TextSecondary);

            row.DurationText = UdonWorldUiKit.Label(
                hit.transform, "Duration", hitWidth - durationWidth - 12f, 0f, durationWidth, height,
                spec.SubSize + 2, TextAnchor.MiddleRight, UdonWorldUiKit.TextSecondary);

            Text secondaryLabel;
            Button secondaryButton = UdonWorldUiKit.PushButton(
                content, "Secondary", hitWidth + 12f, 0f, secondary, height,
                spec.SecondaryCaption, spec.TitleSize, UdonWorldUiKit.ButtonFace, out secondaryLabel);

            // 印は中身より後に置く(半透明でかぶせる)。
            // 先に置くと、行の不透明な面に隠れて見えない。
            Image highlight = UdonWorldUiKit.Plate(
                rowRect, "Highlight", 0f, 0f, width, height, UdonWorldUiKit.RowHighlight);
            highlight.raycastTarget = false;

            // 最初の Refresh が来るまでの 1 フレーム、全行が光って見えないように
            highlight.gameObject.SetActive(false);

            row.Content = content.gameObject;
            row.Highlight = highlight.gameObject;
            row.SecondaryButton = secondaryButton.gameObject;

            UdonWorldUiKit.Wire(hit, row, "Click", spec.PrimaryCaption);
            UdonWorldUiKit.Wire(secondaryButton, row, "ClickSecondary", spec.SecondaryHint);

            return row;
        }

        // ───────── 共通 ─────────

        private static void Finish(
            UdonMediaPanel panel, RectTransform body, Text panelTitle, Text status, string name,
            UdonNowPlayingView nowPlaying, UdonTransportView transport, UdonMediaListView[] lists)
        {
            panel.Body = body.gameObject;
            panel.TitleLabel = panelTitle;
            panel.PanelName = name;
            panel.StatusText = status;
            panel.NowPlaying = nowPlaying;
            panel.Transport = transport;
            panel.Lists = lists;

            if (panelTitle != null) panelTitle.text = name;
            if (status != null) status.text = "行を押すと再生します。";

            // パネルの中の表示にも、状態の 1 行の宛先を教えておく
            // (実行時にも UdonMediaPanel.EnsureBound が同じことをするが、
            //  Inspector を見たときに繋がって見えるほうが分かりやすい)。
            if (transport != null) transport.Panel = panel;

            if (lists == null) return;
            for (int i = 0; i < lists.Length; i++)
            {
                if (lists[i] != null) lists[i].Panel = panel;
            }
        }

        private static GameObject NewChild(GameObject parent, string name)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent.transform, false);
            return go;
        }

        /// <summary>UdonSharp のコンポーネントを付ける(プログラムと UdonBehaviour ごと)。</summary>
        private static T Add<T>(GameObject target) where T : Component
        {
            bool needsCompile;
            var component = UdonSharpSceneUtility.AddUdonSharpComponent(
                target, typeof(T), out needsCompile) as T;

            if (needsCompile) NeedsCompile = true;
            return component;
        }
    }
}
#endif
