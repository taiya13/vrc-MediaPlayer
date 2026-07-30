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
    /// <b>操作 UI(パネル)を組み立てるエディタツール。</b>Phase5-3 → Phase5-5。
    ///
    /// <b>ここが「Controller は差し替え可能」の実体です。</b>
    /// 壁パネルも手持ちリモコンも<b>同じ <see cref="UdonMediaPanel"/> 1 クラス</b>で、
    /// 違うのは「どの表示を、どの大きさで並べたか」だけです。
    /// タブレットや別レイアウトを足したくなったら、
    /// <b>ここにメソッドを 1 つ増やすだけ</b>です。Udon 側のコードは変わりません。
    ///
    /// <b>Phase5-5 で変えたところ</b>
    /// <list type="bullet">
    /// <item><b>縦長 1 列 → 横長 2 列。</b>1 列だと 1.8 m の縦長になり、
    ///       下半分(Queue と Status)が膝の高さに来て狙いにくかった。
    ///       2 列にすると全部が胸〜目の高さに収まる</item>
    /// <item><b>行を 56 → 72 px(約 9.4 cm)に。</b>ボタンはすべて
    ///       <see cref="UdonWorldUiKit.ComfortableTouchMeters"/> 以上。下回ると組み立て時に警告が出る</item>
    /// <item><b>ページ送り → スクロール。</b>「7〜12 / 24 件」の表示とつまみ付き</item>
    /// <item><b>鳴っている行に左端の縦棒。</b>離れて見ても分かる</item>
    /// <item><b>押した行が一瞬光る。</b>「使う」で押したときも手応えが返る</item>
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

        // ───────── 壁パネル(2 列)─────────

        /// <summary>
        /// <b>壁に貼る全部入り。</b>1400 × 980 px ≒ 1.82 m × 1.27 m。
        ///
        /// <code>
        /// ┌───────────────────────────────────────────────┐
        /// │ Smart Media Player              操作中: ○○   │
        /// ├──────────────────┬────────────────────────────┤
        /// │ いま鳴っているもの │ Library     1〜6 / 24 件 ▲▼│
        /// │ ▶ 前へ 再生 次へ  │ ─────────────────────────  │
        /// │ ■ 音量 − 60% ＋   │ (6 行)                     │
        /// │ Queue      ▲▼    │ 関連        1〜3 / 3 件    │
        /// │ (4 行)            │ (3 行)                     │
        /// │ 状態の 1 行        │                            │
        /// └──────────────────┴────────────────────────────┘
        /// </code>
        /// </summary>
        public static UdonMediaPanel BuildWallPanel(GameObject parent, string objectName)
        {
            const float W = 1400f;
            const float H = 980f;
            const float Pad = 28f;
            const float ColGap = 28f;

            float colWidth = (W - Pad * 2f - ColGap) * 0.5f;   // 658
            float leftX = Pad;
            float rightX = Pad + colWidth + ColGap;

            GameObject root = NewChild(parent, objectName);
            RectTransform canvas = UdonWorldUiKit.WorldCanvas(root, "Canvas", W, H, 0.0013f);
            RectTransform body = UdonWorldUiKit.Place(canvas, "Body", 0f, 0f, W, H);

            UdonWorldUiKit.Plate(body, "Backplate", 0f, 0f, W, H, UdonWorldUiKit.Backplate)
                .raycastTarget = false;

            // ── 見出し(2 列にまたがる)
            Text panelTitle = UdonWorldUiKit.Label(
                body, "PanelTitle", Pad, 24f, 700f, 52f, 34,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextPrimary);

            Text syncOwner = UdonWorldUiKit.Label(
                body, "SyncOwner", 760f, 24f, W - 760f - Pad, 52f, 22,
                TextAnchor.MiddleRight, UdonWorldUiKit.TextSecondary);

            UdonWorldUiKit.Divider(body, "HeaderRule", Pad, 86f, W - Pad * 2f, UdonWorldUiKit.Section);

            var panel = Add<UdonMediaPanel>(root);
            if (panel == null) return null;

            // ── 左の列:いま鳴っているもの → 操作 → Queue
            //    いちばん見るものといちばん押すものを、いちばん上(= 目の高さ)へ。
            float leftY = 104f;

            var nowPlaying = BuildNowPlaying(body, leftX, leftY, colWidth, 38, 24, 21, 16);
            if (NeedsCompile) return panel;
            if (nowPlaying != null) nowPlaying.SyncText = syncOwner;
            leftY += 176f;

            var transport = BuildTransport(body, leftX, leftY, colWidth, 88f, 76f, 25, 21, true);
            if (NeedsCompile) return panel;
            leftY += 196f;

            var queueSpec = QueueSpec(colWidth, 4);
            float queueHeight;
            var queue = BuildList(body, leftX, leftY, queueSpec, out queueHeight);
            if (NeedsCompile) return panel;
            leftY += queueHeight + 20f;

            Text status = UdonWorldUiKit.Label(
                body, "Status", leftX, leftY, colWidth, 44f, 21,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextSecondary);

            // ── 右の列:Library → 関連
            float rightY = 104f;

            var librarySpec = LibrarySpec(colWidth, 6);
            float libraryHeight;
            var library = BuildList(body, rightX, rightY, librarySpec, out libraryHeight);
            if (NeedsCompile) return panel;
            rightY += libraryHeight + 20f;

            var relatedSpec = RelatedSpec(colWidth, 3);
            float relatedHeight;
            var related = BuildList(body, rightX, rightY, relatedSpec, out relatedHeight);
            if (NeedsCompile) return panel;

            Finish(panel, body, panelTitle, status, "Smart Media Player",
                   nowPlaying, transport, new[] { library, related, queue });

            return panel;
        }

        // ───────── 手持ちリモコン ─────────

        /// <summary>
        /// <b>最小構成。</b>760 × 770 px ≒ 0.65 m × 0.65 m。
        ///
        /// <b>Library も関連も持ちません。</b>それでも動くことが
        /// 「UI がロジックを持っていない」ことの一番わかりやすい証明になります。
        /// </summary>
        public static UdonMediaPanel BuildRemotePanel(GameObject parent, string objectName)
        {
            const float W = 760f;
            const float H = 770f;
            const float Pad = 24f;
            const float CW = W - Pad * 2f;   // 712

            GameObject root = NewChild(parent, objectName);
            RectTransform canvas = UdonWorldUiKit.WorldCanvas(root, "Canvas", W, H, 0.00085f);
            RectTransform body = UdonWorldUiKit.Place(canvas, "Body", 0f, 0f, W, H);

            UdonWorldUiKit.Plate(body, "Backplate", 0f, 0f, W, H, UdonWorldUiKit.Backplate)
                .raycastTarget = false;

            Text panelTitle = UdonWorldUiKit.Label(
                body, "PanelTitle", Pad, 20f, 320f, 48f, 26,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextPrimary);

            Text syncOwner = UdonWorldUiKit.Label(
                body, "SyncOwner", Pad + 330f, 20f, CW - 330f, 48f, 18,
                TextAnchor.MiddleRight, UdonWorldUiKit.TextSecondary);

            var panel = Add<UdonMediaPanel>(root);
            if (panel == null) return null;

            float y = 78f;

            var nowPlaying = BuildNowPlaying(body, Pad, y, CW, 30, 19, 18, 14);
            if (NeedsCompile) return panel;
            if (nowPlaying != null) nowPlaying.SyncText = syncOwner;
            y += 154f;

            var transport = BuildTransport(body, Pad, y, CW, 80f, 68f, 21, 18, true);
            if (NeedsCompile) return panel;
            y += 178f;

            var queueSpec = QueueSpec(CW, 3);
            queueSpec.RowHeight = 68f;
            queueSpec.HeaderHeight = 56f;   // ▲▼ が 4.5 cm を割らない大きさ
            queueSpec.HeaderSize = 21;
            queueSpec.TitleSize = 21;
            queueSpec.SubSize = 15;

            float queueHeight;
            var queue = BuildList(body, Pad, y, queueSpec, out queueHeight);
            if (NeedsCompile) return panel;
            y += queueHeight + 16f;

            Text status = UdonWorldUiKit.Label(
                body, "Status", Pad, y, CW, 38f, 17,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextSecondary);

            Finish(panel, body, panelTitle, status, "リモコン",
                   nowPlaying, transport, new[] { queue });

            return panel;
        }

        // ───────── いま鳴っているもの ─────────

        private static UdonNowPlayingView BuildNowPlaying(
            RectTransform body, float x, float y, float width,
            int titleSize, int artistSize, int metaSize, float barHeight)
        {
            float titleHeight = Mathf.Round(titleSize * 1.45f);
            float artistHeight = Mathf.Round(artistSize * 1.45f);
            float metaHeight = Mathf.Round(metaSize * 1.6f);
            float total = titleHeight + 4f + artistHeight + 10f + barHeight + 8f + metaHeight;

            RectTransform section = UdonWorldUiKit.Place(body, "NowPlaying", x, y, width, total);

            var view = Add<UdonNowPlayingView>(section.gameObject);
            if (view == null) return null;

            float cursor = 0f;

            view.TitleText = UdonWorldUiKit.Label(
                section, "Title", 0f, cursor, width, titleHeight, titleSize,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextPrimary);
            cursor += titleHeight + 4f;

            view.ArtistText = UdonWorldUiKit.Label(
                section, "Artist", 0f, cursor, width, artistHeight, artistSize,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextSecondary);
            cursor += artistHeight + 10f;

            view.ProgressFill = UdonWorldUiKit.ProgressBar(
                section, "Progress", 0f, cursor, width, barHeight);
            cursor += barHeight + 8f;

            // 経過 / 残り / 状態 を 3 つに割る。
            // 残り時間を出すのは「あと何分で次に行くか」が一番聞かれるため。
            float third = (width - 24f) / 3f;

            view.TimeText = UdonWorldUiKit.Label(
                section, "Time", 0f, cursor, third, metaHeight, metaSize,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextSecondary);

            view.RemainingText = UdonWorldUiKit.Label(
                section, "Remaining", third + 12f, cursor, third, metaHeight, metaSize,
                TextAnchor.MiddleCenter, UdonWorldUiKit.TextSecondary);

            view.StateText = UdonWorldUiKit.Label(
                section, "State", (third + 12f) * 2f, cursor, third, metaHeight, metaSize,
                TextAnchor.MiddleRight, UdonWorldUiKit.TextSecondary);

            view.TitleText.text = "(何も再生していません)";
            return view;
        }

        // ───────── 操作ボタン ─────────

        private static UdonTransportView BuildTransport(
            RectTransform body, float x, float y, float width,
            float mainHeight, float subHeight, int mainSize, int subSize, bool showClear)
        {
            const float Gap = 16f;
            const float RowGap = 16f;

            RectTransform section = UdonWorldUiKit.Place(
                body, "Transport", x, y, width, mainHeight + RowGap + subHeight);

            var view = Add<UdonTransportView>(section.gameObject);
            if (view == null) return null;

            Text unused;
            Text playPauseLabel;

            // ── 1 段目:前へ / 再生・一時停止 / 次へ
            //    いちばん押す「再生 / 一時停止」を 2 倍幅にして、まず狙えるようにする。
            float unit = Mathf.Floor((width - Gap * 2f) / 4f);

            Button previous = UdonWorldUiKit.PushButton(
                section, "Previous", 0f, 0f, unit, mainHeight, "◀◀", mainSize + 4,
                UdonWorldUiKit.ButtonFace, out unused);

            Button playPause = UdonWorldUiKit.PushButton(
                section, "PlayPause", unit + Gap, 0f, unit * 2f, mainHeight, "▶  再生", mainSize,
                UdonWorldUiKit.ButtonAccent, out playPauseLabel);

            float nextX = unit * 3f + Gap * 2f;
            Button next = UdonWorldUiKit.PushButton(
                section, "Next", nextX, 0f, width - nextX, mainHeight, "▶▶", mainSize + 4,
                UdonWorldUiKit.ButtonFace, out unused);

            // ── 2 段目:停止 / 音量 / Queue を空に
            float subY = mainHeight + RowGap;
            float stop = Mathf.Round(width * 0.19f);
            float volumeButton = Mathf.Round(subHeight * 1.35f);
            float volumeText = Mathf.Round(width * 0.20f);

            Button stopButton = UdonWorldUiKit.PushButton(
                section, "Stop", 0f, subY, stop, subHeight, "■", subSize + 4,
                UdonWorldUiKit.ButtonFace, out unused);

            float volumeDownX = stop + RowGap;
            Button volumeDown = UdonWorldUiKit.PushButton(
                section, "VolumeDown", volumeDownX, subY, volumeButton, subHeight, "−", subSize + 6,
                UdonWorldUiKit.ButtonFace, out unused);

            float volumeTextX = volumeDownX + volumeButton + 8f;
            Text volumeLabel = UdonWorldUiKit.Label(
                section, "Volume", volumeTextX, subY, volumeText, subHeight, subSize,
                TextAnchor.MiddleCenter, UdonWorldUiKit.TextSecondary);

            float volumeUpX = volumeTextX + volumeText + 8f;
            Button volumeUp = UdonWorldUiKit.PushButton(
                section, "VolumeUp", volumeUpX, subY, volumeButton, subHeight, "＋", subSize + 6,
                UdonWorldUiKit.ButtonFace, out unused);

            Button clear = null;
            if (showClear)
            {
                float clearX = volumeUpX + volumeButton + RowGap;
                clear = UdonWorldUiKit.PushButton(
                    section, "ClearUpcoming", clearX, subY, width - clearX, subHeight,
                    "Queue を空に", subSize, UdonWorldUiKit.ButtonFace, out unused);
            }

            view.PlayPauseLabel = playPauseLabel;
            view.VolumeText = volumeLabel;

            UdonWorldUiKit.Wire(previous, view, "Previous", "前へ");
            UdonWorldUiKit.Wire(playPause, view, "TogglePlayPause", "再生 / 一時停止");
            UdonWorldUiKit.Wire(next, view, "Next", "次へ");
            UdonWorldUiKit.Wire(stopButton, view, "Stop", "停止");
            UdonWorldUiKit.Wire(volumeDown, view, "VolumeDown", "音量を下げる");
            UdonWorldUiKit.Wire(volumeUp, view, "VolumeUp", "音量を上げる");
            if (clear != null) UdonWorldUiKit.Wire(clear, view, "ClearUpcoming", "Queue を空にする");

            return view;
        }

        // ───────── 一覧 ─────────

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
            public float Width = 658f;
            public float RowHeight = 72f;
            public float RowGap = 8f;
            public float HeaderHeight = 56f;
            public int HeaderSize = 26;
            public int TitleSize = 25;
            public int SubSize = 17;

            /// <summary>▲▼ 1 回で動く行数。0 なら 1 画面ぶん。</summary>
            public int ScrollStep;

            /// <summary>曲が変わったら鳴っている行まで戻すか。</summary>
            public bool FollowNowPlaying;
        }

        private static ListSpec LibrarySpec(float width, int rows)
        {
            var spec = new ListSpec();
            spec.Name = "Library";
            spec.Source = UdonMediaListView.SourceLibrary;
            spec.Header = "Library";
            spec.RowCount = rows;
            spec.Width = width;

            // 半画面ずつ送ると、見ていた行が半分残るので位置を見失いにくい。
            spec.ScrollStep = rows / 2;
            return spec;
        }

        private static ListSpec RelatedSpec(float width, int rows)
        {
            var spec = new ListSpec();
            spec.Name = "Related";
            spec.Source = UdonMediaListView.SourceRelated;
            spec.Header = "関連";
            spec.RowCount = rows;
            spec.Width = width;
            return spec;
        }

        private static ListSpec QueueSpec(float width, int rows)
        {
            var spec = new ListSpec();
            spec.Name = "Queue";
            spec.Source = UdonMediaListView.SourceQueue;
            spec.Header = "Queue";
            spec.SecondaryCaption = "×";
            spec.PrimaryCaption = "この曲へ移動";
            spec.SecondaryHint = "Queue から外す";
            spec.RowCount = rows;
            spec.Width = width;

            // 曲が変わったら先頭へ戻す。Queue の先頭 = いま鳴っているものなので、
            // 下を眺めたまま次の曲になっても、勝手に迷子にならない。
            spec.FollowNowPlaying = true;
            return spec;
        }

        private static UdonMediaListView BuildList(
            RectTransform body, float x, float y, ListSpec spec, out float consumedHeight)
        {
            const float TrackWidth = 8f;
            const float TrackGap = 6f;

            float rowsY = spec.HeaderHeight + 8f;
            float rowsHeight = spec.RowCount * (spec.RowHeight + spec.RowGap) - spec.RowGap;
            consumedHeight = rowsY + rowsHeight;

            float rowWidth = spec.Width - TrackWidth - TrackGap;

            RectTransform section = UdonWorldUiKit.Place(
                body, spec.Name, x, y, spec.Width, consumedHeight);

            var view = Add<UdonMediaListView>(section.gameObject);
            if (view == null) return null;

            view.Source = spec.Source;
            view.HeaderLabel = spec.Header;
            view.ScrollStep = spec.ScrollStep;
            view.FollowNowPlaying = spec.FollowNowPlaying;

            // ── 見出しの行:名前 / 何番目を見ているか / ▲ ▼
            view.HeaderText = UdonWorldUiKit.Label(
                section, "Header", 0f, 0f, spec.Width * 0.36f, spec.HeaderHeight, spec.HeaderSize,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextPrimary);
            view.HeaderText.text = spec.Header;

            float scrollButton = Mathf.Round(spec.HeaderHeight * 1.7f);
            float downX = spec.Width - scrollButton;
            float upX = downX - scrollButton - 8f;
            float rangeX = spec.Width * 0.36f + 8f;

            view.RangeText = UdonWorldUiKit.Label(
                section, "Range", rangeX, 0f, upX - rangeX - 8f, spec.HeaderHeight, spec.SubSize + 2,
                TextAnchor.MiddleRight, UdonWorldUiKit.TextSecondary);

            Text unused;
            Button up = UdonWorldUiKit.PushButton(
                section, "ScrollUp", upX, 0f, scrollButton, spec.HeaderHeight, "▲",
                spec.HeaderSize, UdonWorldUiKit.ButtonFace, out unused);

            Button down = UdonWorldUiKit.PushButton(
                section, "ScrollDown", downX, 0f, scrollButton, spec.HeaderHeight, "▼",
                spec.HeaderSize, UdonWorldUiKit.ButtonFace, out unused);

            view.ScrollUpButton = up.gameObject;
            view.ScrollDownButton = down.gameObject;

            UdonWorldUiKit.Wire(up, view, "ScrollUp", "上へ");
            UdonWorldUiKit.Wire(down, view, "ScrollDown", "下へ");

            UdonWorldUiKit.Divider(
                section, "Rule", 0f, spec.HeaderHeight - 2f, spec.Width, UdonWorldUiKit.Section);

            // ── いまどのあたりを見ているか(細い棒)
            var track = UdonWorldUiKit.Plate(
                section, "ScrollTrack", spec.Width - TrackWidth, rowsY, TrackWidth, rowsHeight,
                UdonWorldUiKit.TrackBack);
            track.raycastTarget = false;

            var handle = UdonWorldUiKit.Plate(
                track.transform, "Handle", 0f, 0f, TrackWidth, rowsHeight,
                UdonWorldUiKit.TrackFill);
            handle.raycastTarget = false;
            view.ScrollHandle = handle.rectTransform;

            // ── 空のときだけ出す案内
            Text empty = UdonWorldUiKit.Label(
                section, "Empty", 0f, rowsY, rowWidth, spec.RowHeight, spec.SubSize + 3,
                TextAnchor.MiddleLeft, UdonWorldUiKit.TextSecondary);
            empty.text = "(まだありません)";
            view.EmptyMessage = empty.gameObject;

            // ── 行
            var rows = new UdonMediaListRow[spec.RowCount];
            for (int i = 0; i < spec.RowCount; i++)
            {
                float rowY = rowsY + i * (spec.RowHeight + spec.RowGap);

                UdonMediaListRow row = BuildRow(section, "Row" + i, rowY, rowWidth, spec, i);
                if (row == null) return view;

                row.Row = i;
                row.List = view;
                rows[i] = row;
            }
            view.Rows = rows;

            return view;
        }

        private static UdonMediaListRow BuildRow(
            RectTransform parent, string name, float y, float width, ListSpec spec, int index)
        {
            float height = spec.RowHeight;

            RectTransform rowRect = UdonWorldUiKit.Place(parent, name, 0f, y, width, height);

            var row = Add<UdonMediaListRow>(rowRect.gameObject);
            if (row == null) return null;

            float bar = 8f;
            float secondary = Mathf.Round(height * 1.3f);
            float hitX = bar + 6f;
            float hitWidth = width - hitX - secondary - 12f;
            float indexWidth = Mathf.Round(height * 0.7f);
            float durationWidth = Mathf.Round(width * 0.16f);
            float titleX = indexWidth + 18f;
            float durationX = hitWidth - durationWidth - 10f;
            float titleWidth = durationX - titleX - 12f;

            // 中身は「行そのもの」ではなく子に置く。
            // 空行で非アクティブにする対象が UdonBehaviour 本体だと、
            // 二度と書き戻せなくなるため(UdonMediaListRow の注意書きを参照)。
            RectTransform content = UdonWorldUiKit.Place(rowRect, "Content", 0f, 0f, width, height);

            // 1 行おきに少しだけ濃さを変える。
            // 目が横に滑らないようにするためで、色そのものは意味を持たない。
            Color face = index % 2 == 0
                ? UdonWorldUiKit.RowFace
                : UdonWorldUiKit.RowFaceAlt;

            Button hit = UdonWorldUiKit.HitArea(content, "Hit", hitX, 0f, hitWidth, height, face);

            row.IndexText = UdonWorldUiKit.Label(
                hit.transform, "Index", 6f, 0f, indexWidth, height, spec.SubSize + 3,
                TextAnchor.MiddleCenter, UdonWorldUiKit.TextSecondary);

            row.TitleText = UdonWorldUiKit.Label(
                hit.transform, "Title", titleX, height * 0.10f, titleWidth, height * 0.46f,
                spec.TitleSize, TextAnchor.LowerLeft, UdonWorldUiKit.TextPrimary);

            row.SubText = UdonWorldUiKit.Label(
                hit.transform, "Sub", titleX, height * 0.56f, titleWidth, height * 0.34f,
                spec.SubSize, TextAnchor.UpperLeft, UdonWorldUiKit.TextSecondary);

            row.DurationText = UdonWorldUiKit.Label(
                hit.transform, "Duration", durationX, 0f, durationWidth, height,
                spec.SubSize + 3, TextAnchor.MiddleRight, UdonWorldUiKit.TextSecondary);

            Text secondaryLabel;
            Button secondaryButton = UdonWorldUiKit.PushButton(
                content, "Secondary", hitX + hitWidth + 12f, 0f, secondary, height,
                spec.SecondaryCaption, spec.TitleSize + 2,
                UdonWorldUiKit.ButtonFace, out secondaryLabel);

            // ── 印は中身より後に置く(半透明でかぶせる)。
            //    先に置くと、行の不透明な面に隠れて見えない。
            Image highlight = UdonWorldUiKit.Plate(
                rowRect, "Highlight", hitX, 0f, width - hitX, height, UdonWorldUiKit.RowHighlight);
            highlight.raycastTarget = false;
            highlight.gameObject.SetActive(false);

            Image pressed = UdonWorldUiKit.Plate(
                rowRect, "Pressed", hitX, 0f, width - hitX, height, UdonWorldUiKit.RowPressed);
            pressed.raycastTarget = false;
            pressed.gameObject.SetActive(false);

            // 左端の縦棒。離れて見ると、色の帯よりこちらが先に目に入る。
            Image nowPlayingBar = UdonWorldUiKit.Plate(
                rowRect, "NowPlayingBar", 0f, 0f, bar, height, UdonWorldUiKit.TrackFill);
            nowPlayingBar.raycastTarget = false;
            nowPlayingBar.gameObject.SetActive(false);

            row.Content = content.gameObject;
            row.Highlight = highlight.gameObject;
            row.PressedMarker = pressed.gameObject;
            row.NowPlayingBar = nowPlayingBar.gameObject;
            row.SecondaryButton = secondaryButton.gameObject;
            row.TitleColor = UdonWorldUiKit.TextPrimary;
            row.NowPlayingTitleColor = UdonWorldUiKit.TextNowPlaying;

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
