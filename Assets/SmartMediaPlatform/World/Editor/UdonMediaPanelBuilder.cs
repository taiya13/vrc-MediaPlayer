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
    /// <b>操作 UI(パネル)を組み立てるエディタツール。</b>Phase5-3 → Phase7。
    ///
    /// <b>ここが「Controller は差し替え可能」の実体です。</b>
    /// 壁パネルも手持ちリモコンも<b>同じ <see cref="UdonMediaPanel"/> 1 クラス</b>で、
    /// 違うのは「どの表示を、どの大きさで並べたか」だけです。
    ///
    /// <b>Phase7 で作り直したところ</b>
    /// <list type="bullet">
    /// <item><b>一覧を 3 本同時 → タブで 1 本ずつ。</b>13 行が同時に見えていたのをやめ、
    ///       1 行に使える面積を 2 倍以上にした。押せる所は 42 → 22 へ</item>
    /// <item><b>再生中を主役に。</b>左半分をまるごと使い、
    ///       73 × 41 cm の絵と 6 cm の曲名を置く</item>
    /// <item><b>大きいボタンは 3 つだけ。</b>停止と「予定を空に」は「…」の中へ</item>
    /// <item><b>行に絵と 2 段組。</b>絵が無いカタログではジャンルの色で塗る</item>
    /// <item><b>鳴っている行は色・縦棒・動く棒の 3 つで示す。</b>
    ///       色だけだと、色が見えにくい人に伝わらない</item>
    /// <item><b>「＋」に「予定へ」と書く。</b>記号だけでは何をするか分からない</item>
    /// <item><b>余白を 3.6 → 5.5 cm へ。</b>開発ツールらしさの大半は詰まった罫線から来る</item>
    /// </list>
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
                typeof(UdonMediaTabs),
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

        // ───────── 寸法(1 px = 0.0013 m)─────────

        private const float Pad = 42f;
        private const float ColumnGap = 36f;
        private const float LeftWidth = 560f;
        private const float RightWidth = 720f;

        // ───────── 壁パネル ─────────

        /// <summary>
        /// <b>壁に貼る全部入り。</b>1400 × 860 px ≒ 1.82 m × 1.12 m。
        ///
        /// <code>
        /// ┌─────────────────────────────────────────────────┐
        /// │ Smart Media Player                操作中: ○○   │
        /// ├──────────────────┬──────────────────────────────┤
        /// │ ┌──────────────┐ │ [すべての曲][おすすめ][予定3]│
        /// │ │  大きな絵     │ │ ┌──┐ 曲名          3:34 ┌─┐ │
        /// │ └──────────────┘ │ │絵│ ch · ジャンル      │＋│ │
        /// │ 曲名(6 cm)        │ └──┘                   └─┘ │
        /// │ チャンネル [J-POP]│  … 5 行 …                   │
        /// │ ━━━━━━━━━━━━━━━  │                              │
        /// │ ◀◀ [ 再生 ] ▶▶ … │ 1〜5 / 42 件      ▲▲ ▲ ▼   │
        /// │ 音量 − ━━━━ ＋    │                              │
        /// └──────────────────┴──────────────────────────────┘
        /// </code>
        /// </summary>
        public static UdonMediaPanel BuildWallPanel(GameObject parent, string objectName)
        {
            const float W = 1400f;
            const float H = 860f;

            float leftX = Pad;
            float rightX = Pad + LeftWidth + ColumnGap;

            GameObject root = NewChild(parent, objectName);
            RectTransform canvas = UdonWorldUiKit.WorldCanvas(root, "Canvas", W, H, 0.0013f);
            RectTransform body = UdonWorldUiKit.Place(canvas, "Body", 0f, 0f, W, H);

            UdonWorldUiKit.Plate(body, "Backplate", 0f, 0f, W, H, UdonWorldUiKit.Backplate)
                .raycastTarget = false;

            var panel = Add<UdonMediaPanel>(root);
            if (panel == null) return null;

            // ── 左:いま鳴っているもの
            float leftY = Pad;

            float nowPlayingHeight;
            var nowPlaying = BuildNowPlaying(
                body, leftX, leftY, LeftWidth, true, out nowPlayingHeight);
            if (NeedsCompile) return panel;
            leftY += nowPlayingHeight + 26f;

            var transport = BuildTransport(body, leftX, leftY, LeftWidth, 116f, true);
            if (NeedsCompile) return panel;
            leftY += 116f + 18f;

            BuildVolume(body, transport, leftX, leftY, LeftWidth, 60f);
            leftY += 60f + 14f;

            Text status = UdonWorldUiKit.Label(
                body, "Status", leftX, leftY, LeftWidth, 36f, 19,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextSecondary);

            // ── 右:これから選ぶもの
            var tabs = BuildTabbedLists(body, panel, rightX, Pad, RightWidth, H - Pad * 2f);
            if (NeedsCompile) return panel;

            // 見出しは絵の上に小さく。パネルの名前より、鳴っている曲のほうが大事。
            Text panelTitle = UdonWorldUiKit.Label(
                body, "PanelTitle", leftX, 12f, 400f, 26f, 16,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextSecondary);

            Text syncOwner = UdonWorldUiKit.Label(
                body, "SyncOwner", rightX, 12f, RightWidth, 26f, 16,
                TextAnchor.MiddleRight, UdonWorldUiKit.TextSecondary);

            if (nowPlaying != null) nowPlaying.SyncText = syncOwner;

            Finish(panel, body, panelTitle, status, "Smart Media Player",
                   nowPlaying, transport, tabs);

            return panel;
        }

        // ───────── 手持ちリモコン ─────────

        /// <summary>
        /// <b>最小構成。</b>700 × 520 px ≒ 0.65 m × 0.48 m。
        ///
        /// <b>一覧を持ちません。</b>手元で見るのは「いま何が鳴っているか」と
        /// 「止める・飛ばす」だけです。曲を選びたくなったら壁パネルへ行きます。
        /// <b>それでも動くことが「UI がロジックを持っていない」ことの証明</b>になります。
        /// </summary>
        public static UdonMediaPanel BuildRemotePanel(GameObject parent, string objectName)
        {
            const float W = 700f;
            const float H = 520f;
            const float RemotePad = 30f;
            const float CW = W - RemotePad * 2f;   // 640

            GameObject root = NewChild(parent, objectName);
            RectTransform canvas = UdonWorldUiKit.WorldCanvas(root, "Canvas", W, H, 0.00095f);
            RectTransform body = UdonWorldUiKit.Place(canvas, "Body", 0f, 0f, W, H);

            UdonWorldUiKit.Plate(body, "Backplate", 0f, 0f, W, H, UdonWorldUiKit.Backplate)
                .raycastTarget = false;

            var panel = Add<UdonMediaPanel>(root);
            if (panel == null) return null;

            float y = RemotePad;

            float nowPlayingHeight;
            var nowPlaying = BuildNowPlaying(body, RemotePad, y, CW, false, out nowPlayingHeight);
            if (NeedsCompile) return panel;
            y += nowPlayingHeight + 22f;

            var transport = BuildTransport(body, RemotePad, y, CW, 104f, false);
            if (NeedsCompile) return panel;
            y += 104f + 20f;

            Text status = UdonWorldUiKit.Label(
                body, "Status", RemotePad, y, CW, 34f, 17,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextSecondary);

            Text syncOwner = UdonWorldUiKit.Label(
                body, "SyncOwner", RemotePad, 8f, CW, 20f, 14,
                TextAnchor.MiddleRight, UdonWorldUiKit.TextSecondary);

            if (nowPlaying != null) nowPlaying.SyncText = syncOwner;

            Finish(panel, body, null, status, "リモコン", nowPlaying, transport, null);
            return panel;
        }

        // ───────── いま鳴っているもの ─────────

        /// <summary>
        /// <paramref name="wide"/> が true なら絵を上に大きく、false なら左に小さく。
        /// リモコンは手に持つので、絵を大きくすると視界を塞ぎます。
        /// </summary>
        private static UdonNowPlayingView BuildNowPlaying(
            RectTransform body, float x, float y, float width, bool wide, out float consumedHeight)
        {
            float artWidth = wide ? width : 240f;
            float artHeight = Mathf.Round(artWidth * 9f / 16f);

            int titleSize = wide ? 46 : 32;
            int artistSize = wide ? 28 : 22;
            int metaSize = wide ? 19 : 16;

            float titleHeight = Mathf.Round(titleSize * 1.35f);
            float artistHeight = Mathf.Round(artistSize * 1.35f);
            float chipHeight = wide ? 30f : 26f;
            float metaHeight = Mathf.Round(metaSize * 1.6f);

            float textTop = wide ? artHeight + 20f : 0f;
            float textLeft = wide ? 0f : artWidth + 24f;
            float textWidth = wide ? width : width - textLeft;

            float textHeight = titleHeight + 2f + artistHeight + 6f + chipHeight;
            float barTop = wide ? textTop + textHeight + 26f : artHeight + 20f;

            float total = barTop + 12f + 6f + metaHeight;

            consumedHeight = total;

            RectTransform section = UdonWorldUiKit.Place(body, "NowPlaying", x, y, width, total);

            var view = Add<UdonNowPlayingView>(section.gameObject);
            if (view == null) return null;

            // ── 絵
            Image artwork = UdonWorldUiKit.Plate(
                section, "Artwork", 0f, 0f, artWidth, artHeight,
                new Color(0.18f, 0.20f, 0.26f, 1f));
            artwork.raycastTarget = false;
            artwork.preserveAspect = true;

            Text artworkFallback = UdonWorldUiKit.Label(
                artwork.transform, "Fallback", 0f, 0f, artWidth, artHeight,
                wide ? 30 : 20, TextAnchor.MiddleCenter, UdonWorldUiKit.TextSecondary);

            view.Artwork = artwork;
            view.ArtworkFallbackText = artworkFallback;

            // ── 曲名 / チャンネル / ジャンル
            float cursor = textTop;

            view.TitleText = UdonWorldUiKit.Label(
                section, "Title", textLeft, cursor, textWidth, titleHeight, titleSize,
                TextAnchor.LowerLeft, UdonWorldUiKit.TextPrimary);
            cursor += titleHeight + 2f;

            view.ArtistText = UdonWorldUiKit.Label(
                section, "Artist", textLeft, cursor, textWidth, artistHeight, artistSize,
                TextAnchor.UpperLeft, UdonWorldUiKit.TextSecondary);
            cursor += artistHeight + 6f;

            Image chip = UdonWorldUiKit.Plate(
                section, "GenreChip", textLeft, cursor, 110f, chipHeight,
                new Color(0.11f, 0.20f, 0.27f, 1f));
            chip.raycastTarget = false;

            view.GenreChip = chip.gameObject;
            view.GenreText = UdonWorldUiKit.Label(
                chip.transform, "Genre", 0f, 0f, 110f, chipHeight, wide ? 17 : 15,
                TextAnchor.MiddleCenter, UdonWorldUiKit.TrackFill);

            // ── 進捗と時間
            view.ProgressFill = UdonWorldUiKit.ProgressBar(section, "Progress", 0f, barTop, width, 12f);

            float third = (width - 24f) / 3f;
            float metaTop = barTop + 12f + 6f;

            view.TimeText = UdonWorldUiKit.Label(
                section, "Time", 0f, metaTop, third, metaHeight, metaSize,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextSecondary);

            view.StateText = UdonWorldUiKit.Label(
                section, "State", third + 12f, metaTop, third, metaHeight, metaSize,
                TextAnchor.MiddleCenter, UdonWorldUiKit.TextSecondary);

            view.RemainingText = UdonWorldUiKit.Label(
                section, "Remaining", (third + 12f) * 2f, metaTop, third, metaHeight, metaSize,
                TextAnchor.MiddleRight, UdonWorldUiKit.TextSecondary);

            view.TitleText.text = "曲を選んでください";
            return view;
        }

        // ───────── 操作 ─────────

        /// <summary>
        /// <b>大きいボタンは 3 つだけ。</b>停止と「予定を空に」は「…」の中です。
        /// 初めて見た人が迷わないよう、<b>いちばん押すものをいちばん大きく</b>します。
        /// </summary>
        private static UdonTransportView BuildTransport(
            RectTransform body, float x, float y, float width, float height, bool wide)
        {
            const float Gap = 16f;
            const float MoreWidth = 52f;

            RectTransform section = UdonWorldUiKit.Place(body, "Transport", x, y, width, height);

            var view = Add<UdonTransportView>(section.gameObject);
            if (view == null) return null;

            // 残りを 前 : 再生 : 次 = 1 : 1.83 : 1 で割る。
            float rest = width - MoreWidth - Gap * 3f;
            float side = Mathf.Floor(rest / 3.83f);
            float main = rest - side * 2f;

            Text unused;
            Text playPauseLabel;

            Button previous = UdonWorldUiKit.PushButton(
                section, "Previous", 0f, 0f, side, height, "◀◀", wide ? 34 : 30,
                UdonWorldUiKit.ButtonFace, out unused);

            Button playPause = UdonWorldUiKit.PushButton(
                section, "PlayPause", side + Gap, 0f, main, height, "",
                wide ? 30 : 26, UdonWorldUiKit.TrackFill, out playPauseLabel);

            // 押せる面がいちばん明るい = ここを押せばよい、が色で分かる。
            playPauseLabel.color = new Color(0.05f, 0.10f, 0.15f, 1f);
            playPauseLabel.text = "▶  再生";

            Button next = UdonWorldUiKit.PushButton(
                section, "Next", side + Gap + main + Gap, 0f, side, height, "▶▶",
                wide ? 34 : 30, UdonWorldUiKit.ButtonFace, out unused);

            Button more = UdonWorldUiKit.PushButton(
                section, "More", width - MoreWidth, 0f, MoreWidth, height, "…",
                wide ? 30 : 26, UdonWorldUiKit.Section, out unused);

            view.PlayPauseLabel = playPauseLabel;

            UdonWorldUiKit.Wire(previous, view, "Previous", "前の曲へ");
            UdonWorldUiKit.Wire(playPause, view, "TogglePlayPause", "再生 / 一時停止");
            UdonWorldUiKit.Wire(next, view, "Next", "次の曲へ");

            BuildMoreSheet(section, view, width, height, wide, more);
            return view;
        }

        /// <summary>
        /// 「…」で開く引き出し。<b>めったに使わないものを、目に入らない所へ</b>。
        /// 開いていないときは畳んであるので、初めて見た人の選択肢は 3 つに減ります。
        /// </summary>
        private static void BuildMoreSheet(
            RectTransform section, UdonTransportView view,
            float width, float height, bool wide, Button more)
        {
            const float SheetHeight = 76f;

            RectTransform sheet = UdonWorldUiKit.Place(
                section, "MoreSheet", 0f, height + 10f, width, SheetHeight);

            UdonWorldUiKit.Plate(sheet, "Back", 0f, 0f, width, SheetHeight,
                                 UdonWorldUiKit.Section).raycastTarget = false;

            float half = (width - 24f) / 2f;

            Text unused;
            Button stop = UdonWorldUiKit.PushButton(
                sheet, "Stop", 8f, 8f, half, SheetHeight - 16f, "■ 停止",
                wide ? 21 : 18, UdonWorldUiKit.ButtonFace, out unused);

            Button clear = UdonWorldUiKit.PushButton(
                sheet, "ClearUpcoming", 16f + half, 8f, half, SheetHeight - 16f, "予定を空に",
                wide ? 21 : 18, UdonWorldUiKit.ButtonFace, out unused);

            UdonWorldUiKit.Wire(stop, view, "Stop", "停止");
            UdonWorldUiKit.Wire(clear, view, "ClearUpcoming", "再生予定を空にする");

            // 引き出しは畳んで置く。開け閉ては UdonMediaPanel の Toggle を借りる。
            sheet.gameObject.SetActive(false);
            UdonWorldUiKit.Wire(more, view, "ToggleMore", "そのほかの操作");

            view.MoreSheet = sheet.gameObject;
        }

        private static void BuildVolume(
            RectTransform body, UdonTransportView view,
            float x, float y, float width, float height)
        {
            if (view == null) return;

            const float ButtonWidth = 88f;

            RectTransform section = UdonWorldUiKit.Place(body, "Volume", x, y, width, height);

            Text unused;
            Button down = UdonWorldUiKit.PushButton(
                section, "VolumeDown", 0f, 0f, ButtonWidth, height, "−", 26,
                UdonWorldUiKit.Section, out unused);

            Button up = UdonWorldUiKit.PushButton(
                section, "VolumeUp", width - ButtonWidth, 0f, ButtonWidth, height, "＋", 26,
                UdonWorldUiKit.Section, out unused);

            float barX = ButtonWidth + 16f;
            float barWidth = width - ButtonWidth * 2f - 32f;

            UdonWorldUiKit.Plate(
                section, "Track", barX, height * 0.5f - 5f, barWidth, 10f,
                UdonWorldUiKit.TrackBack).raycastTarget = false;

            Text volumeLabel = UdonWorldUiKit.Label(
                section, "VolumeText", barX, 0f, barWidth, height, 17,
                TextAnchor.MiddleCenter, UdonWorldUiKit.TextSecondary);

            view.VolumeText = volumeLabel;

            UdonWorldUiKit.Wire(down, view, "VolumeDown", "音量を下げる");
            UdonWorldUiKit.Wire(up, view, "VolumeUp", "音量を上げる");
        }

        // ───────── タブと一覧 ─────────

        private static UdonMediaTabs BuildTabbedLists(
            RectTransform body, UdonMediaPanel panel,
            float x, float y, float width, float height)
        {
            const float TabHeight = 76f;
            const float TabGap = 9f;
            const int TabCount = 3;

            RectTransform section = UdonWorldUiKit.Place(body, "Browser", x, y, width, height);

            var tabs = Add<UdonMediaTabs>(section.gameObject);
            if (tabs == null) return null;

            float tabWidth = Mathf.Floor((width - TabGap * (TabCount - 1)) / TabCount);

            var lists = new UdonMediaListView[TabCount];
            var pages = new GameObject[TabCount];
            var marks = new GameObject[TabCount];
            var labels = new Text[TabCount];

            int[] sources =
            {
                UdonMediaListView.SourceLibrary,
                UdonMediaListView.SourceRelated,
                UdonMediaListView.SourceQueue,
            };

            string[] events = { "SelectTab0", "SelectTab1", "SelectTab2" };

            for (int i = 0; i < TabCount; i++)
            {
                float tabX = i * (tabWidth + TabGap);

                Text label;
                Button tab = UdonWorldUiKit.PushButton(
                    section, "Tab" + i, tabX, 0f, tabWidth, TabHeight, "", 24,
                    UdonWorldUiKit.Section, out label);

                // 選ばれている側は下線で示す。面の色だけだと 2 m 先で差が消える。
                Image mark = UdonWorldUiKit.Plate(
                    tab.transform, "Selected", 0f, TabHeight - 6f, tabWidth, 6f,
                    UdonWorldUiKit.TrackFill);
                mark.raycastTarget = false;

                marks[i] = mark.gameObject;
                labels[i] = label;

                UdonWorldUiKit.Wire(tab, tabs, events[i], "ここを見る");

                float pageTop = TabHeight + 16f;
                RectTransform page = UdonWorldUiKit.Place(
                    section, "Page" + i, 0f, pageTop, width, height - pageTop);

                pages[i] = page.gameObject;
                lists[i] = BuildList(page, sources[i], width, height - pageTop);

                if (NeedsCompile) return tabs;
            }

            tabs.Lists = lists;
            tabs.Pages = pages;
            tabs.SelectedMarks = marks;
            tabs.Labels = labels;
            tabs.SelectedColor = UdonWorldUiKit.TextPrimary;
            tabs.NormalColor = UdonWorldUiKit.TextSecondary;

            panel.Tabs = tabs;
            panel.Lists = lists;

            return tabs;
        }

        private static UdonMediaListView BuildList(
            RectTransform page, int source, float width, float height)
        {
            const float RowHeight = 112f;
            const float RowGap = 10f;
            const float FooterHeight = 60f;
            const int RowCount = 5;

            var view = Add<UdonMediaListView>(page.gameObject);
            if (view == null) return null;

            view.Source = source;
            view.HeaderLabel = "";
            view.ScrollStep = 0;
            view.FollowNowPlaying = source == UdonMediaListView.SourceQueue;

            // ── 何も無いときの案内。種類ごとに文が変わる。
            Text empty = UdonWorldUiKit.Label(
                page, "Empty", 0f, RowHeight * 0.5f, width, 44f, 22,
                TextAnchor.MiddleCenter, UdonWorldUiKit.TextSecondary);

            view.EmptyMessage = empty.gameObject;
            view.EmptyText = empty;

            // ── 行
            var rows = new UdonMediaListRow[RowCount];
            for (int i = 0; i < RowCount; i++)
            {
                float rowY = i * (RowHeight + RowGap);

                UdonMediaListRow row = BuildRow(page, "Row" + i, rowY, width, RowHeight, source, i);
                if (row == null) return view;

                row.Row = i;
                row.List = view;
                rows[i] = row;
            }
            view.Rows = rows;

            // ── 下の帯:何件目を見ているか + スクロール
            float footerY = height - FooterHeight;

            view.RangeText = UdonWorldUiKit.Label(
                page, "Range", 0f, footerY, 300f, FooterHeight, 19,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextSecondary);

            const float ScrollWidth = 80f;
            const float ScrollGap = 12f;

            float downX = width - ScrollWidth;
            float upX = downX - ScrollWidth - ScrollGap;
            float homeX = upX - ScrollWidth - ScrollGap;

            Text unused;
            Button home = UdonWorldUiKit.PushButton(
                page, "ScrollHome", homeX, footerY, ScrollWidth, FooterHeight, "▲▲", 20,
                UdonWorldUiKit.Section, out unused);

            Button up = UdonWorldUiKit.PushButton(
                page, "ScrollUp", upX, footerY, ScrollWidth, FooterHeight, "▲", 24,
                UdonWorldUiKit.Section, out unused);

            Button down = UdonWorldUiKit.PushButton(
                page, "ScrollDown", downX, footerY, ScrollWidth, FooterHeight, "▼", 24,
                UdonWorldUiKit.Section, out unused);

            view.ScrollHomeButton = home.gameObject;
            view.ScrollUpButton = up.gameObject;
            view.ScrollDownButton = down.gameObject;

            UdonWorldUiKit.Wire(home, view, "ScrollHome", "再生中 / 先頭へ");
            UdonWorldUiKit.Wire(up, view, "ScrollUp", "上へ(続けて押すと速い)");
            UdonWorldUiKit.Wire(down, view, "ScrollDown", "下へ(続けて押すと速い)");

            return view;
        }

        /// <summary>
        /// 1 行。<b>絵 + 曲名 + チャンネル · ジャンル + 長さ + 「予定へ」</b>。
        ///
        /// <b>文字は 2 段だけ</b>にしてあります。3 段にすると 1 行あたりが小さくなり、
        /// 2 m 先で読めなくなります。
        /// </summary>
        private static UdonMediaListRow BuildRow(
            RectTransform parent, string name, float y, float width, float height,
            int source, int index)
        {
            const float BarWidth = 6f;
            const float ArtWidth = 160f;
            const float ArtHeight = 90f;
            const float SecondaryWidth = 88f;

            bool queue = source == UdonMediaListView.SourceQueue;

            RectTransform rowRect = UdonWorldUiKit.Place(parent, name, 0f, y, width, height);

            var row = Add<UdonMediaListRow>(rowRect.gameObject);
            if (row == null) return null;

            // 中身は「行そのもの」ではなく子に置く。
            // 空行で非アクティブにする対象が UdonBehaviour 本体だと、
            // 二度と書き戻せなくなるため。
            RectTransform content = UdonWorldUiKit.Place(rowRect, "Content", 0f, 0f, width, height);

            // 1 行おきに濃さを変える。目が横に滑らないようにするためで、色に意味は無い。
            Color face = index % 2 == 0 ? UdonWorldUiKit.RowFace : UdonWorldUiKit.RowFaceAlt;

            float hitX = BarWidth + 14f;
            float hitWidth = width - hitX - SecondaryWidth - 14f;

            Button hit = UdonWorldUiKit.HitArea(content, "Hit", hitX, 0f, hitWidth, height, face);

            // ── 絵
            float artY = (height - ArtHeight) * 0.5f;
            Image artwork = UdonWorldUiKit.Plate(
                hit.transform, "Artwork", 12f, artY, ArtWidth, ArtHeight,
                new Color(0.18f, 0.20f, 0.26f, 1f));
            artwork.raycastTarget = false;
            artwork.preserveAspect = true;

            Text artworkFallback = UdonWorldUiKit.Label(
                artwork.transform, "Fallback", 0f, 0f, ArtWidth, ArtHeight, 22,
                TextAnchor.MiddleCenter, UdonWorldUiKit.TextSecondary);

            row.Artwork = artwork;
            row.ArtworkFallbackText = artworkFallback;

            // ── 動く 3 本の棒。絵の左下に重ねる(Spotify と同じ置き方)。
            row.EqualizerBars = BuildEqualizer(artwork.transform, 14f, ArtHeight - 34f);

            // ── 文字
            float textX = 12f + ArtWidth + 22f;
            float durationWidth = 96f;
            float textWidth = hitWidth - textX - durationWidth - 16f;

            row.TitleText = UdonWorldUiKit.Label(
                hit.transform, "Title", textX, height * 0.14f, textWidth, height * 0.34f,
                26, TextAnchor.LowerLeft, UdonWorldUiKit.TextPrimary);

            row.SubText = UdonWorldUiKit.Label(
                hit.transform, "Sub", textX, height * 0.52f, textWidth, height * 0.30f,
                19, TextAnchor.UpperLeft, UdonWorldUiKit.TextSecondary);

            row.DurationText = UdonWorldUiKit.Label(
                hit.transform, "Duration", hitWidth - durationWidth - 12f, 0f,
                durationWidth, height, 19, TextAnchor.MiddleRight, UdonWorldUiKit.TextSecondary);

            row.IndexText = UdonWorldUiKit.Label(
                hit.transform, "Index", 12f, artY - 4f, 34f, 30f, 18,
                TextAnchor.MiddleCenter, UdonWorldUiKit.TrackFill);

            // ── 2 つめのボタン。記号だけでは何をするか分からないので言葉を書く。
            Text secondaryLabel;
            Button secondaryButton = UdonWorldUiKit.PushButton(
                content, "Secondary", width - SecondaryWidth, (height - SecondaryWidth) * 0.5f,
                SecondaryWidth, SecondaryWidth, queue ? "外す" : "予定へ", 18,
                UdonWorldUiKit.ButtonFace, out secondaryLabel);

            row.SecondaryLabel = secondaryLabel;

            // ── 印は中身より後に置く(半透明でかぶせる)。
            Image highlight = UdonWorldUiKit.Plate(
                rowRect, "Highlight", hitX, 0f, hitWidth, height, UdonWorldUiKit.RowHighlight);
            highlight.raycastTarget = false;
            highlight.gameObject.SetActive(false);

            Image pressed = UdonWorldUiKit.Plate(
                rowRect, "Pressed", hitX, 0f, hitWidth, height, UdonWorldUiKit.RowPressed);
            pressed.raycastTarget = false;
            pressed.gameObject.SetActive(false);

            Image nowPlayingBar = UdonWorldUiKit.Plate(
                rowRect, "NowPlayingBar", 0f, 0f, BarWidth, height, UdonWorldUiKit.TrackFill);
            nowPlayingBar.raycastTarget = false;
            nowPlayingBar.gameObject.SetActive(false);

            row.Content = content.gameObject;
            row.Highlight = highlight.gameObject;
            row.PressedMarker = pressed.gameObject;
            row.NowPlayingBar = nowPlayingBar.gameObject;
            row.SecondaryButton = secondaryButton.gameObject;
            row.TitleColor = UdonWorldUiKit.TextPrimary;
            row.NowPlayingTitleColor = UdonWorldUiKit.TextNowPlaying;

            UdonWorldUiKit.Wire(hit, row, "Click", queue ? "この曲へ移動" : "再生");
            UdonWorldUiKit.Wire(secondaryButton, row, "ClickSecondary",
                                queue ? "再生予定から外す" : "再生予定に追加");

            return row;
        }

        /// <summary>
        /// 動く 3 本の棒。<b>色が見えにくい人にも「これが鳴っている」と伝わる</b>
        /// 3 つめの手がかりです(1 つめが色、2 つめが左端の縦棒)。
        /// </summary>
        private static RectTransform[] BuildEqualizer(Transform parent, float x, float y)
        {
            const int Bars = 3;
            const float BarWidth = 7f;
            const float BarGap = 5f;
            const float BarHeight = 22f;

            RectTransform holder = UdonWorldUiKit.Place(
                parent, "Equalizer", x, y, Bars * (BarWidth + BarGap), BarHeight);

            var bars = new RectTransform[Bars];

            for (int i = 0; i < Bars; i++)
            {
                Image bar = UdonWorldUiKit.Plate(
                    holder, "Bar" + i, i * (BarWidth + BarGap), 0f, BarWidth, BarHeight,
                    UdonWorldUiKit.TrackFill);
                bar.raycastTarget = false;

                // 下端に貼り付ける。高さを変えると下から伸びるように見える。
                RectTransform rect = bar.rectTransform;
                rect.pivot = new Vector2(0f, 0f);
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 1f);

                bars[i] = rect;
                bar.gameObject.SetActive(false);
            }

            return bars;
        }

        // ───────── 共通 ─────────

        private static void Finish(
            UdonMediaPanel panel, RectTransform body, Text panelTitle, Text status, string name,
            UdonNowPlayingView nowPlaying, UdonTransportView transport, UdonMediaTabs tabs)
        {
            panel.Body = body.gameObject;
            panel.TitleLabel = panelTitle;
            panel.PanelName = name;
            panel.StatusText = status;
            panel.NowPlaying = nowPlaying;
            panel.Transport = transport;

            if (panelTitle != null) panelTitle.text = name;
            if (status != null) status.text = "曲を押すと再生します。";

            // パネルの中の表示にも、状態の 1 行の宛先を教えておく
            // (実行時にも UdonMediaPanel.EnsureBound が同じことをするが、
            //  Inspector を見たときに繋がって見えるほうが分かりやすい)。
            if (transport != null) transport.Panel = panel;

            if (panel.Lists == null) panel.Lists = new UdonMediaListView[0];

            for (int i = 0; i < panel.Lists.Length; i++)
            {
                if (panel.Lists[i] != null) panel.Lists[i].Panel = panel;
            }

            // 絵が無い曲の色は一覧が決める。大きい絵もそれを借りて、
            // 行と再生中で色が食い違わないようにする。
            if (nowPlaying != null && panel.Lists.Length > 0) nowPlaying.PaletteSource = panel.Lists[0];
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
