#if UNITY_EDITOR && VRC_SDK_VRCSDK3
using System;
using SmartMediaPlatform.Video.EditorTools;
using SmartMediaPlatform.World.Udon;
using SmartMediaPlatform.World.Udon.UI;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;

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
                typeof(UdonListScroller),
                typeof(UdonRecommendationCards),
                typeof(UdonValueStrip),
                typeof(UdonPlayerOptions),
            };
        }

        /// <summary>コンパイル待ちのため組み立てを中断すべきか。</summary>
        public static bool NeedsCompile { get; private set; }

        public static void ResetNeedsCompile()
        {
            NeedsCompile = false;
        }

        // ───────── 寸法(1 px = 0.0013 m)─────────

        private const float Pad = UdonMediaTheme.Space4;          // 32

        // ───────── 壁パネルの骨格(Phase8-2)─────────
        //
        // <b>左右 2 カラムをやめました。</b>512px の左カラムは
        // <b>ジャケットの正方形に合わせた形</b>で、絵を捨てた時点で
        // 意味を失いました。文字は横に伸びるものなので、
        // 「いま鳴っているもの」は<b>全幅の帯</b>になります。
        //
        // 縦の積み方は 32(ふち) → 帯 → 検索 → タブ → 本体 → 32(ふち)。

        /// <summary>いま鳴っているものの帯の高さ。下辺がそのままシークバー。</summary>
        private const float BandHeight = 156f;

        /// <summary>帯の右側、操作(前 / 再生 / 次 / …)と音量に渡す幅。</summary>
        private const float BandControlWidth = 430f;

        /// <summary>探す欄とタブの高さ。どちらも 4.5 cm 以上。</summary>
        private const float SearchHeight = 60f;
        private const float TabHeight = 60f;

        /// <summary>アーティストのレールの幅(Phase8-2)。</summary>
        private const float RailWidth = 260f;

        /// <summary>レールと一覧のあいだ。</summary>
        private const float RailGap = 20f;

        // ───────── 「使う」で動かすバーの細かさ(Phase7-6)─────────
        //
        // 区画を増やすほど細かく狙えますが、1 区画が狭くなって当てにくくなります。
        // レーザーで無理なく当てられる幅(3 cm 前後)から逆算した数です。

        /// <summary>再生位置バーの区画数。5 分の曲で 20 秒きざみ。</summary>
        private const int SeekSegments = 16;

        /// <summary>音量バーの区画数。0 / 8 / 17 …… / 100 %。</summary>
        private const int VolumeSegments = 13;

        /// <summary>一覧スクロールの区画数。</summary>
        private const int ScrollSegments = 12;

        // ───────── 壁パネル ─────────

        /// <summary>
        /// <b>壁に貼る全部入り。</b>1400 × 860 px ≒ 1.82 m × 1.12 m。Phase8-2 で組み直し。
        ///
        /// <code>
        /// ┌─────────────────────────────────────────────────────┐
        /// │ いま流れている                    ◀◀  ▶  ▶▶  …    │
        /// │ 夜に駆ける                        音量 − ━━━━ ＋   │  帯 156
        /// │ YOASOBI                    2:14                4:23 │
        /// │ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ │← シークバー
        /// ├─────────────────────────────────────────────────────┤
        /// │ ○ 曲名・アーティスト・ジャンルで探す            × │  検索 60
        /// ├─────────────────────────────────────────────────────┤
        /// │   曲     おすすめ    お気に入り    履歴    再生予定  │  タブ 60
        /// ├──────────┬──────────────────────────────────────────┤
        /// │ アーティスト│ ♪ 夜に駆ける              4:23  ♥  ＋ │
        /// │ すべて 1043│    YOASOBI ・ J-POP                    │
        /// │ Ado      38│ 02 アイドル               3:34  ♥  ＋ │  本体 472
        /// │ YOASOBI  31│    YOASOBI ・ アニメ                   │
        /// │ …          │  … 6 行 …                              │
        /// │ ▲    ▼    │ 1 – 6 / 31            ▲▲   ▲    ▼    │
        /// └──────────┴──────────────────────────────────────────┘
        /// </code>
        ///
        /// <b>絵はどこにもありません。</b>いちばん大きい図形は
        /// 帯の下辺を走る<b>全幅のシークバー</b>で、行の左端は<b>番号</b>です。
        /// </summary>
        public static UdonMediaPanel BuildWallPanel(GameObject parent, string objectName)
        {
            const float W = 1400f;
            const float H = 860f;

            float inner = W - Pad * 2f;

            GameObject root = NewChild(parent, objectName);
            RectTransform canvas = UdonWorldUiKit.WorldCanvas(root, "Canvas", W, H, 0.0013f);
            RectTransform body = UdonWorldUiKit.Place(canvas, "Body", 0f, 0f, W, H);

            // ── パネルそのものが 1 枚の硝子(Frost / Phase7-7)。
            //    半透明なので<b>後ろのワールドが透けます</b>。
            UdonWorldUiKit.GlassCard(
                body, "Backplate", 0f, 0f, W, H,
                UdonMediaTheme.Base, UdonMediaTheme.RadiusXLarge).raycastTarget = false;

            var panel = Add<UdonMediaPanel>(root);
            if (panel == null) return null;

            // ── 上:いま鳴っているものの帯。
            //    <b>持ち上がった硝子 1 枚</b>にして、下の一覧と層を分けます。
            RectTransform band = UdonWorldUiKit.Place(
                body, "NowPlayingBand", Pad, Pad, inner, BandHeight);

            UdonWorldUiKit.GlassCard(
                band, "Back", 0f, 0f, inner, BandHeight,
                UdonMediaTheme.SurfaceRaised, UdonMediaTheme.RadiusLarge).raycastTarget = false;

            float textWidth = inner - BandControlWidth - UdonMediaTheme.Space4;

            float bandHeightUsed;
            var nowPlaying = BuildNowPlaying(
                band, UdonMediaTheme.Space3, 0f, textWidth,
                inner - UdonMediaTheme.Space3 * 2f, true, out bandHeightUsed);
            if (NeedsCompile) return panel;

            // ── 帯の右:操作と音量。
            //    <b>いちばん押すもの</b>なので、いちばん上に置きます。
            float controlX = inner - BandControlWidth - UdonMediaTheme.Space3;

            var transport = BuildTransport(
                band, controlX, 12f, BandControlWidth, 72f, true);
            if (NeedsCompile) return panel;

            BuildVolume(band, transport, controlX, 88f, BandControlWidth, 40f);

            // ── 中:探す → 選ぶ → 一覧。
            //    探すのは<b>タブを選ぶより前</b>の行動なので、この順に積みます。
            float browserTop = Pad + BandHeight + UdonMediaTheme.Space2;

            var tabs = BuildTabbedLists(
                body, panel, Pad, browserTop, inner, H - Pad - browserTop);
            if (NeedsCompile) return panel;

            // ── 状態と「誰が操作しているか」は帯の中に小さく。
            //    別の行を作ると、そのぶん一覧が 1 行減ります。
            Text status = UdonWorldUiKit.Label(
                band, "Status", UdonMediaTheme.Space3, BandHeight - 34f,
                textWidth, 24f, UdonMediaTheme.TextCaption,
                TextAnchor.MiddleLeft, UdonMediaTheme.TextMuted);

            Text syncOwner = UdonWorldUiKit.Label(
                band, "SyncOwner", controlX, BandHeight - 34f,
                BandControlWidth, 24f, UdonMediaTheme.TextCaption,
                TextAnchor.MiddleRight, UdonMediaTheme.TextMuted);

            if (nowPlaying != null) nowPlaying.SyncText = syncOwner;

            Finish(panel, body, null, status, "Smart Media Player",
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

            // ── パネルそのものが 1 枚の硝子(Frost / Phase7-7)。
            //    半透明なので<b>後ろのワールドが透けます</b>。
            UdonWorldUiKit.GlassCard(
                body, "Backplate", 0f, 0f, W, H,
                UdonMediaTheme.Base, UdonMediaTheme.RadiusXLarge).raycastTarget = false;

            var panel = Add<UdonMediaPanel>(root);
            if (panel == null) return null;

            float y = RemotePad + UdonMediaTheme.Space2;

            float nowPlayingHeight;
            var nowPlaying = BuildNowPlaying(
                body, RemotePad, y, CW, CW, false, out nowPlayingHeight);
            if (NeedsCompile) return panel;
            y += nowPlayingHeight + UdonMediaTheme.Space3;

            var transport = BuildTransport(body, RemotePad, y, CW, 96f, false);
            if (NeedsCompile) return panel;
            y += 96f + UdonMediaTheme.Space2;

            Text status = UdonWorldUiKit.Label(
                body, "Status", RemotePad, y, CW, 34f,
                UdonMediaTheme.ForRemote(UdonMediaTheme.TextCaption),
                TextAnchor.MiddleLeft, UdonMediaTheme.TextMuted);

            Text syncOwner = UdonWorldUiKit.Label(
                body, "SyncOwner", RemotePad, UdonMediaTheme.Space1, CW, 20f, 13,
                TextAnchor.MiddleRight, UdonMediaTheme.TextMuted);

            if (nowPlaying != null) nowPlaying.SyncText = syncOwner;

            Finish(panel, body, null, status, "リモコン", nowPlaying, transport, null);
            return panel;
        }

        // ───────── いま鳴っているもの ─────────

        /// <summary>
        /// <b>絵を持ちません。</b>Phase8-2。
        ///
        /// この区画の仕事は「何が鳴っているか」に一目で答えることです。
        /// 絵を捨てたので、その役は<b>曲名の大きさ</b>が引き受けます。
        /// 代わりの四角は<b>置きません</b> —— 色の四角に頭文字を置く形は、
        /// どれだけ丁寧に作っても<b>「本当は絵が入る枠」</b>として読まれるからです。
        ///
        /// <paramref name="wide"/>(壁パネル)では<b>横帯</b>に組みます。
        /// 文字は横に伸びるものなので、正方形の名残を捨てると自然にこの形になります。
        /// <paramref name="barWidth"/> はシークバーと時刻の行に渡す幅で、
        /// <paramref name="textWidth"/>(曲名の幅)より広く取れます ——
        /// 右に操作ボタンが乗るのは<b>文字の段だけ</b>だからです。
        /// </summary>
        private static UdonNowPlayingView BuildNowPlaying(
            RectTransform parent, float x, float y,
            float textWidth, float barWidth, bool wide, out float consumedHeight)
        {
            // 文字の 4 段はテーマのまま。曲名だけ、帯では 1 段上げます。
            int titleSize = wide ? 40 : UdonMediaTheme.ForRemote(UdonMediaTheme.TextTitle);
            int artistSize = wide ? 22 : UdonMediaTheme.ForRemote(UdonMediaTheme.TextBody);
            int metaSize = wide
                ? UdonMediaTheme.TextCaption
                : UdonMediaTheme.ForRemote(UdonMediaTheme.TextCaption);

            float kickerHeight = wide ? 18f : 0f;
            float titleHeight = wide ? 48f : Mathf.Round(titleSize * 1.34f);
            float artistHeight = wide ? 26f : Mathf.Round(artistSize * 1.34f);
            float metaHeight = wide ? 20f : Mathf.Round(metaSize * 1.6f);

            // つかめるバーは、見た目より当たり判定を広く取る。
            float seekTouch = wide ? 28f : 34f;
            float seekBar = wide ? 8f : 7f;

            float titleTop = wide ? 12f + kickerHeight + 2f : 0f;
            float artistTop = titleTop + titleHeight + 2f;
            float metaTop = artistTop + artistHeight + (wide ? 0f : UdonMediaTheme.Space2);
            float barTop = metaTop + metaHeight + (wide ? 0f : UdonMediaTheme.Space1);

            float total = barTop + seekTouch;
            consumedHeight = total;

            RectTransform section = UdonWorldUiKit.Place(
                parent, "NowPlaying", x, y, barWidth, total);

            var view = Add<UdonNowPlayingView>(section.gameObject);
            if (view == null) return null;

            // ── 「いま流れている」。<b>字間を空けた小さな見出し</b>にします。
            //    大きくすると曲名と competing して、どちらが曲名か分かりません。
            if (wide)
            {
                UdonWorldUiKit.Label(
                    section, "Kicker", 0f, 12f, textWidth, kickerHeight,
                    UdonMediaTheme.TextCaption, TextAnchor.LowerLeft,
                    UdonMediaTheme.TextMuted).text = "い ま 流 れ て い る";
            }

            // ── 曲名。<b>この画面でいちばん大きい文字</b>。
            //    入り切るまで小さくします。途中で切れた名前は、
            //    小さい名前より役に立ちません。
            view.TitleText = UdonWorldUiKit.FittedLabel(
                section, "Title", 0f, titleTop, textWidth, titleHeight, titleSize,
                wide ? 20 : 14, TextAnchor.LowerLeft, UdonMediaTheme.TextPrimary);

            // ── アーティストとジャンル。
            //    ジャンルの札は<b>アーティストのすぐ右</b>に置きます。
            //    行を増やすと、そのぶん一覧が 1 行減ります。
            float chipWidth = wide ? 120f : 104f;
            float chipHeight = wide ? 24f : 22f;
            float artistWidth = textWidth - chipWidth - UdonMediaTheme.Space2;

            view.ArtistText = UdonWorldUiKit.FittedLabel(
                section, "Artist", 0f, artistTop, artistWidth, artistHeight, artistSize,
                wide ? 14 : 12, TextAnchor.UpperLeft, UdonMediaTheme.TextSecondary);

            Image chip = UdonWorldUiKit.RoundedPlate(
                section, "GenreChip", artistWidth + UdonMediaTheme.Space2,
                artistTop + (artistHeight - chipHeight) * 0.5f, chipWidth, chipHeight,
                new Color(0f, 0f, 0f, 0.06f), UdonMediaTheme.RadiusSmall);
            chip.raycastTarget = false;

            view.GenreChip = chip.gameObject;
            view.GenreText = UdonWorldUiKit.Label(
                chip.transform, "Genre", 0f, 0f, chipWidth, chipHeight,
                wide ? 15 : 13, TextAnchor.MiddleCenter, UdonMediaTheme.TextSecondary);

            // ── 時刻の行。<b>バーの幅いっぱい</b>に取ります。
            //    左に経過、右に残り。真ん中は「読み込み中」など、
            //    伝えることがあるときだけ出ます。
            view.TimeText = UdonWorldUiKit.Label(
                section, "Time", 0f, metaTop, 150f, metaHeight, metaSize,
                TextAnchor.MiddleLeft, UdonMediaTheme.TextSecondary);

            view.StateText = UdonWorldUiKit.Label(
                section, "State", 156f, metaTop, 240f, metaHeight, metaSize,
                TextAnchor.MiddleLeft, UdonMediaTheme.TextMuted);

            view.RemainingText = UdonWorldUiKit.Label(
                section, "Remaining", barWidth - 150f, metaTop, 150f, metaHeight, metaSize,
                TextAnchor.MiddleRight, UdonMediaTheme.TextSecondary);

            // ── シークバー。<b>この画面でいちばん大きい図形</b>です。
            //    飾りではなく、進み具合そのもの。常に動いているので、
            //    絵を持たない画面でもここだけは「生きて」見えます。
            Image seekFill;
            Slider seek = UdonWorldUiKit.DragBar(
                section, "Seek", 0f, barTop, barWidth, seekTouch, seekBar, out seekFill);

            view.SeekSlider = seek;
            view.ProgressFill = seekFill;

            UdonWorldUiKit.WireSlider(seek, view, "OnSeekChanged", "再生位置を動かす");

            // ── 「使う」でも動かせるようにする(Phase7-6)。
            //    実機では uGUI のポインターが届いていないので、
            //    バーの上に細長い当たり判定を並べて、指した所へ飛ばします。
            UdonWorldUiKit.ValueStrip(
                section, "SeekStrip", 0f, barTop, barWidth, seekTouch,
                SeekSegments, seek, null, null, false, view, "OnSeekChanged", "");

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
            const float MoreWidth = 56f;
            float Gap = UdonMediaTheme.Space2;

            RectTransform section = UdonWorldUiKit.Place(body, "Transport", x, y, width, height);

            var view = Add<UdonTransportView>(section.gameObject);
            if (view == null) return null;

            // 残りを 前 : 再生 : 次 = 1 : 1.83 : 1 で割る。
            float rest = width - MoreWidth - Gap * 3f;
            float side = Mathf.Floor(rest / 3.83f);
            float main = rest - side * 2f;

            int radius = Mathf.RoundToInt(height * 0.5f);   // 端は丸く(押せる感じを出す)

            Text unused;
            Text playPauseLabel;

            Button previous = UdonWorldUiKit.RoundedButton(
                section, "Previous", 0f, 0f, side, height, "◀◀",
                wide ? 30 : 26, UdonMediaTheme.SurfaceRaised, radius, out unused);

            // ── 唯一の強調色をここに置く。
            //    画面の中で「色が付いている押せるもの」がこれ 1 つだけなので、
            //    初めて見た人でも、どこを押せば始まるかが色だけで分かる。
            Button playPause = UdonWorldUiKit.RoundedButton(
                section, "PlayPause", side + Gap, 0f, main, height, "▶  再生",
                wide ? UdonMediaTheme.TextTitle : UdonMediaTheme.TextBody,
                UdonMediaTheme.Accent, radius, out playPauseLabel);

            Button next = UdonWorldUiKit.RoundedButton(
                section, "Next", side + Gap + main + Gap, 0f, side, height, "▶▶",
                wide ? 30 : 26, UdonMediaTheme.SurfaceRaised, radius, out unused);

            // ── 「…」だけは<b>角丸の長方形</b>にする(Phase7-10)。
            //    他のボタンと同じ「高さの半分」を角丸にすると、
            //    幅が高さより狭いので<b>卵型</b>になります。
            //    幅の半分より小さい角丸にすれば、ちゃんと長方形に見えます。
            Button more = UdonWorldUiKit.RoundedButton(
                section, "More", width - MoreWidth, 0f, MoreWidth, height, "…",
                wide ? 28 : 24, UdonMediaTheme.Surface,
                UdonMediaTheme.RadiusLarge, out unused);

            view.PlayPauseLabel = playPauseLabel;

            UdonWorldUiKit.Wire(previous, view, "Previous", "前の曲へ");
            UdonWorldUiKit.Wire(playPause, view, "TogglePlayPause", "再生 / 一時停止");
            UdonWorldUiKit.Wire(next, view, "Next", "次の曲へ");

            BuildMoreSheet(section, view, width, height, wide, more);
            return view;
        }

        /// <summary>
        /// <b>「…」で下から出るシート。</b>Phase7-9。
        ///
        /// <b>設定画面にはしません。</b>別ウィンドウを開くと、
        /// <list type="bullet">
        /// <item>いま鳴っているものが見えなくなる</item>
        /// <item>「戻る」を探さないと帰れない</item>
        /// </list>
        /// の 2 つが同時に起きます。ワールドではどちらも致命的です。
        /// <b>操作の真上に重ねて、押した所のすぐそばで閉じられる</b>形にします。
        ///
        /// 中身はどれも<b>1 回決めたらしばらく触らないもの</b>です。
        /// だから常には出しません。
        /// </summary>
        private static void BuildMoreSheet(
            RectTransform section, UdonTransportView view,
            float width, float height, bool wide, Button more)
        {
            const float RowHeight = 64f;
            const float Gap = 10f;

            float pad = UdonMediaTheme.Space2;
            float inner = width - pad * 2f;

            // 上から:URL 欄 → 再生 / 予定へ → 繰り返し → おやすみ → 停止 / 予定を空に → 閉じる
            float sheetHeight = pad * 2f + RowHeight * 6f + Gap * 5f + 28f;

            RectTransform sheet = UdonWorldUiKit.Place(
                section, "MoreSheet", 0f, height + UdonMediaTheme.Space1, width, sheetHeight);

            // ── しっかり手前へ出す(Phase7-10)。
            //    <b>1 枚ぶんでは足りませんでした。</b>後ろの一覧と重なって
            //    文字が透けて見えるので、はっきり浮かせます
            //    (30px ≒ 4 cm。近すぎず、板から浮きすぎない距離)。
            sheet.localPosition = new Vector3(
                sheet.localPosition.x, sheet.localPosition.y, -30f);

            UdonWorldUiKit.GlassCard(
                sheet, "Back", 0f, 0f, width, sheetHeight,
                UdonMediaTheme.SurfaceRaised, UdonMediaTheme.RadiusLarge).raycastTarget = false;

            // つまんで下ろす取っ手。<b>形だけ</b>ですが、
            // 「これは下から出てきた板だ」と伝えるのはこの 1 本です。
            UdonWorldUiKit.RoundedPlate(
                sheet, "Grabber", width * 0.5f - 34f, UdonMediaTheme.Space1, 68f, 5f,
                UdonMediaTheme.Outline, 3).raycastTarget = false;

            var options = Add<UdonPlayerOptions>(sheet.gameObject);

            float y = pad + 20f;
            Text unused;

            // ── URL
            UdonWorldUiKit.Label(
                sheet, "UrlCaption", pad, y - 4f, inner, 24f,
                UdonMediaTheme.TextCaption, TextAnchor.LowerLeft, UdonMediaTheme.TextMuted)
                .text = "YouTube の URL を貼り付けて再生";

            y += 26f;

            RectTransform fieldRect = UdonWorldUiKit.Place(
                sheet, "UrlField", pad, y, inner, RowHeight);

            Image fieldBack = fieldRect.gameObject.AddComponent<Image>();
            fieldBack.color = UdonMediaTheme.Surface;
            UdonWorldUiKit.ApplyRadius(fieldBack, UdonMediaTheme.RadiusMedium);

            var urlField = fieldRect.gameObject.AddComponent<VRCUrlInputField>();

            Text typed = UdonWorldUiKit.Label(
                fieldRect, "Text", UdonMediaTheme.Space2, 0f,
                inner - UdonMediaTheme.Space4, RowHeight,
                UdonMediaTheme.TextCaption, TextAnchor.MiddleLeft, UdonMediaTheme.TextPrimary);
            typed.raycastTarget = false;

            Text placeholder = UdonWorldUiKit.Label(
                fieldRect, "Placeholder", UdonMediaTheme.Space2, 0f,
                inner - UdonMediaTheme.Space4, RowHeight,
                UdonMediaTheme.TextCaption, TextAnchor.MiddleLeft, UdonMediaTheme.TextMuted);
            placeholder.text = "https://www.youtube.com/watch?v=…";
            placeholder.raycastTarget = false;

            urlField.textComponent = typed;
            urlField.placeholder = placeholder;
            urlField.targetGraphic = fieldBack;

            // 枠のどこを「使う」でも VRChat のキーボードが開くようにする。
            if (options != null)
            {
                UdonWorldUiKit.InteractArea(
                    sheet, "UrlHit", pad, y, inner, RowHeight,
                    options, "OpenUrlKeyboard", "URL を打つ");
            }

            y += RowHeight + Gap;

            // ── 再生 / 予定へ
            float half = (inner - Gap) * 0.5f;

            Button playUrl = UdonWorldUiKit.RoundedButton(
                sheet, "UrlPlay", pad, y, half, RowHeight, "▶ 再生",
                UdonMediaTheme.TextBody, UdonMediaTheme.Accent,
                UdonMediaTheme.RadiusMedium, out unused);

            Button queueUrl = UdonWorldUiKit.RoundedButton(
                sheet, "UrlQueue", pad + half + Gap, y, half, RowHeight, "＋ 予定へ",
                UdonMediaTheme.TextBody, UdonMediaTheme.Surface,
                UdonMediaTheme.RadiusMedium, out unused);

            y += RowHeight + Gap;

            Text urlStatus = UdonWorldUiKit.Label(
                sheet, "UrlStatus", pad, y - Gap, inner, 22f,
                UdonMediaTheme.TextCaption, TextAnchor.UpperLeft, UdonMediaTheme.TextMuted);

            // ── 繰り返し
            Text repeatLabel;
            Button repeat = UdonWorldUiKit.RoundedButton(
                sheet, "Repeat", pad, y, inner, RowHeight, "繰り返し:切",
                UdonMediaTheme.TextBody, UdonMediaTheme.Surface,
                UdonMediaTheme.RadiusMedium, out repeatLabel);

            y += RowHeight + Gap;

            // ── おやすみタイマー
            Text sleepLabel;
            Button sleep = UdonWorldUiKit.RoundedButton(
                sheet, "Sleep", pad, y, inner, RowHeight, "おやすみ:切",
                UdonMediaTheme.TextBody, UdonMediaTheme.Surface,
                UdonMediaTheme.RadiusMedium, out sleepLabel);

            y += RowHeight + Gap;

            // ── 停止 / 予定を空に
            Button stop = UdonWorldUiKit.RoundedButton(
                sheet, "Stop", pad, y, half, RowHeight, "■ 停止",
                UdonMediaTheme.TextBody, UdonMediaTheme.Surface,
                UdonMediaTheme.RadiusMedium, out unused);

            Button clear = UdonWorldUiKit.RoundedButton(
                sheet, "ClearUpcoming", pad + half + Gap, y, half, RowHeight, "予定を空に",
                UdonMediaTheme.TextBody, UdonMediaTheme.Surface,
                UdonMediaTheme.RadiusMedium, out unused);

            y += RowHeight + Gap;

            // ── 閉じる
            Button close = UdonWorldUiKit.RoundedButton(
                sheet, "Close", pad, y, inner, RowHeight, "閉じる",
                UdonMediaTheme.TextBody, UdonMediaTheme.Base,
                UdonMediaTheme.RadiusMedium, out unused);

            UdonWorldUiKit.Wire(stop, view, "Stop", "停止");
            UdonWorldUiKit.Wire(clear, view, "ClearUpcoming", "再生予定を空にする");

            if (options != null)
            {
                options.Sheet = sheet.gameObject;
                options.UrlField = urlField;
                options.UrlStatus = urlStatus;
                options.RepeatLabel = repeatLabel;
                options.SleepLabel = sleepLabel;

                UdonWorldUiKit.Wire(playUrl, options, "PlayUrl", "この URL を再生");
                UdonWorldUiKit.Wire(queueUrl, options, "EnqueueUrl", "この URL をあとで");
                UdonWorldUiKit.Wire(repeat, options, "CycleRepeat", "繰り返しを変える");
                UdonWorldUiKit.Wire(sleep, options, "CycleSleep", "おやすみタイマー");
                UdonWorldUiKit.Wire(close, options, "Close", "閉じる");
                UdonWorldUiKit.Wire(more, options, "Toggle", "そのほかの操作");

                view.Options = options;
            }
            else
            {
                UdonWorldUiKit.Wire(more, view, "ToggleMore", "そのほかの操作");
            }

            // 畳んで置く。
            sheet.gameObject.SetActive(false);
            view.MoreSheet = sheet.gameObject;
        }

        private static void BuildVolume(
            RectTransform body, UdonTransportView view,
            float x, float y, float width, float height)
        {
            if (view == null) return;

            const float ButtonWidth = 84f;

            RectTransform section = UdonWorldUiKit.Place(body, "Volume", x, y, width, height);

            int radius = Mathf.RoundToInt(height * 0.5f);

            Text unused;
            Button down = UdonWorldUiKit.RoundedButton(
                section, "VolumeDown", 0f, 0f, ButtonWidth, height, "−",
                26, UdonMediaTheme.Surface, radius, out unused);

            Button up = UdonWorldUiKit.RoundedButton(
                section, "VolumeUp", width - ButtonWidth, 0f, ButtonWidth, height, "＋",
                26, UdonMediaTheme.Surface, radius, out unused);

            float barX = ButtonWidth + UdonMediaTheme.Space2;
            float barWidth = width - ButtonWidth * 2f - UdonMediaTheme.Space2 * 2f;

            // 音量も、見た目より当たり判定を広く取ったバーにする。
            // 「＋ / −」を連打しなくても、つかんで一気に動かせる。
            Image volumeFill;
            Slider volume = UdonWorldUiKit.DragBar(
                section, "VolumeBar", barX, 0f, barWidth, height, 8f, out volumeFill);

            view.VolumeSlider = volume;
            UdonWorldUiKit.WireSlider(volume, view, "OnVolumeSliderChanged", "音量");

            // 「使う」でも動かせるようにする(Phase7-6)。
            UdonWorldUiKit.ValueStrip(
                section, "VolumeStrip", barX, 0f, barWidth, height,
                VolumeSegments, volume, null, null, false,
                view, "OnVolumeSliderChanged", "");

            Text volumeLabel = UdonWorldUiKit.Label(
                section, "VolumeText", barX, height, barWidth, 24f,
                UdonMediaTheme.TextCaption, TextAnchor.UpperCenter, UdonMediaTheme.TextMuted);

            view.VolumeText = volumeLabel;

            UdonWorldUiKit.Wire(down, view, "VolumeDown", "音量を下げる");
            UdonWorldUiKit.Wire(up, view, "VolumeUp", "音量を上げる");
        }

        // ───────── タブと一覧 ─────────

        /// <summary>
        /// <b>タブは 5 本。</b>曲 / おすすめ / お気に入り / 履歴 / 再生予定。Phase8-2。
        ///
        /// <b>「アーティスト」タブを畳みました。</b>
        /// アーティストは<b>曲タブの中の常設レール</b>になり、
        /// レールで「すべて」を選んだ状態が、以前の「曲」タブと同じ中身になります。
        /// 同じ結果を出す入口が 2 つあると、押す前にどちらか考えることになるので、
        /// <b>1 つにまとめました</b>。タブが 1 本減ったぶん、
        /// 残りのタブは 1 本ずつ広くなって押しやすくなります。
        /// </summary>
        private static UdonMediaTabs BuildTabbedLists(
            RectTransform body, UdonMediaPanel panel,
            float x, float y, float width, float height)
        {
            const float TabGap = 9f;
            const int TabCount = 5;

            RectTransform section = UdonWorldUiKit.Place(body, "Browser", x, y, width, height);

            var tabs = Add<UdonMediaTabs>(section.gameObject);
            if (tabs == null) return null;

            // ── 探す欄は<b>いちばん上</b>(Phase7-10)。
            //    探すのは<b>タブを選ぶより前</b>の行動なので、その順に並べます。
            float searchHeight = SearchHeight + UdonMediaTheme.Space2;

            float tabWidth = Mathf.Floor((width - TabGap * (TabCount - 1)) / TabCount);

            var lists = new UdonMediaListView[TabCount];
            var pages = new GameObject[TabCount];
            var marks = new GameObject[TabCount];
            var tabRects = new RectTransform[TabCount];
            var labels = new Text[TabCount];

            // ── 並びは「曲 → おすすめ → お気に入り → 履歴 → 再生予定」。
            //    <b>おすすめを 2 番目</b>に置いたのは、
            //    「曲を見る → そのまま次を選ぶ」が<b>隣どうし</b>になるからです。
            int[] sources =
            {
                UdonMediaListView.SourceLibrary,
                UdonMediaListView.SourceRelated,
                UdonMediaListView.SourceFavorite,
                UdonMediaListView.SourceHistory,
                UdonMediaListView.SourceQueue,
            };

            string[] events =
            {
                "SelectTab0", "SelectTab1", "SelectTab2", "SelectTab3", "SelectTab4",
            };

            float pageTop = searchHeight + TabHeight + UdonMediaTheme.Space2;
            float pageHeight = height - pageTop;

            UdonMediaListView rail = null;

            for (int i = 0; i < TabCount; i++)
            {
                float tabX = i * (tabWidth + TabGap);

                // ── タブは<b>面を持ちません</b>(Frost / Phase7-7)。
                //    塗った箱を並べると、そこがいちばん強い模様になり、
                //    <b>中身より枠のほうが目に入ります</b>。
                //    選ばれていることは、下を滑る 1 本の線だけで示します。
                Button tab = UdonWorldUiKit.HitArea(
                    section, "Tab" + i, tabX, searchHeight, tabWidth, TabHeight,
                    new Color(1f, 1f, 1f, 0.001f));

                Text label = UdonWorldUiKit.Label(
                    tab.transform, "Label", 0f, 0f, tabWidth, TabHeight,
                    UdonMediaTheme.TextBody, TextAnchor.MiddleCenter,
                    UdonMediaTheme.TextMuted);

                tabRects[i] = tab.GetComponent<RectTransform>();
                labels[i] = label;

                UdonWorldUiKit.Wire(tab, tabs, events[i], "ここを見る");

                RectTransform page = UdonWorldUiKit.Place(
                    section, "Page" + i, 0f, pageTop, width, pageHeight);

                pages[i] = page.gameObject;

                if (sources[i] == UdonMediaListView.SourceRelated)
                {
                    // おすすめだけカードにする。
                    // 一覧は「探している人」、カードは「探していない人」のための形。
                    BuildRecommendationCards(page, panel, width, pageHeight);
                }
                else if (sources[i] == UdonMediaListView.SourceLibrary)
                {
                    // ── ここだけ<b>レール + 一覧</b>の 2 枚組(Phase8-2)。
                    rail = BuildArtistRail(page, pageHeight);
                    if (NeedsCompile) return tabs;

                    float listX = RailWidth + RailGap;
                    RectTransform songArea = UdonWorldUiKit.Place(
                        page, "Songs", listX, 0f, width - listX, pageHeight);

                    lists[i] = BuildList(
                        songArea, sources[i], width - listX, pageHeight);
                }
                else
                {
                    // ── <b>お気に入り・履歴・再生予定にレールは付けません</b>。
                    //    どれも曲数が絞られていて、そこからさらにアーティストで
                    //    絞りたくなる場面がほとんどないためです。
                    //    全幅を一覧に渡したほうが、1 行ぶん多く見えます。
                    lists[i] = BuildList(page, sources[i], width, pageHeight);
                }

                if (NeedsCompile) return tabs;
            }

            // ── 選ばれているタブの下を滑る 1 本の線。
            //    ぱっと点け消しすると「どこからどこへ移ったか」が残りません。
            Image indicator = UdonWorldUiKit.RoundedPlate(
                section, "Indicator", 0f, searchHeight + TabHeight - 5f, tabWidth * 0.5f, 5f,
                UdonMediaTheme.Accent, 3);
            indicator.raycastTarget = false;

            tabs.Indicator = indicator.GetComponent<RectTransform>();
            tabs.TabRects = tabRects;
            tabs.SlideSeconds = UdonMediaTheme.MotionBase;

            tabs.Lists = lists;
            tabs.Pages = pages;
            tabs.SelectedMarks = marks;
            tabs.Labels = labels;

            // おすすめはカードなので一覧を持たない。名前だけ決め打ちで渡す。
            tabs.FixedLabels = new[] { "", "おすすめ", "", "", "" };

            UdonMediaListView songList = lists[0];

            // ── レールと一覧をつなぐ。
            //    <b>Tabs は渡しません</b> —— 2 つは同じ画面にいるので、
            //    選んでもタブを切り替える必要がありません。
            if (rail != null)
            {
                rail.SongList = songList;
                rail.Tabs = null;
                rail.ShowAllEntry = true;
            }

            // ── 曲の一覧は<b>まとめません</b>(Phase8-2)。
            //    チャンネルごとの見出しはレールと同じ情報なので、
            //    両方出すと画面に 2 回書くことになります。
            //    レールが索引で、一覧はその中身、という役割分担にします。
            if (songList != null) songList.GroupByChannel = false;

            tabs.Selected = 0;

            tabs.SelectedColor = UdonMediaTheme.TextPrimary;

            // ── 選んでいないタブも<b>同じ濃さ</b>にする(Phase7-10)。
            //    薄くして「主役」を作ったつもりが、<b>ただ読めないタブ</b>に
            //    なっていました。主従は<b>並び順</b>(左が先)と下線で伝えます。
            tabs.NormalColor = UdonMediaTheme.TextSecondary;
            tabs.SecondaryColor = UdonMediaTheme.TextSecondary;
            tabs.Primary = null;

            // ── 探す欄をいちばん上に置き、曲の一覧へ繋ぐ。
            //    <b>絞り込むのは「曲」だけ</b>なので、繋ぎ先はそこ 1 つです。
            if (songList != null) BuildSearchBar(section, songList, width);

            panel.Tabs = tabs;

            // ── レールも窓口をつないでもらう必要があるので、ここに混ぜます。
            //    <see cref="UdonMediaPanel.Lists"/> はタブと対応していません
            //    (順に舐めて Store などを挿すだけ)。
            panel.Lists = new[]
            {
                lists[0], lists[1], lists[2], lists[3], lists[4], rail,
            };

            return tabs;
        }

        /// <summary>
        /// <b>アーティストのレール。</b>Phase8-2。曲タブの左に常設します。
        ///
        /// <b>なぜタブをやめてレールにしたのか</b><br/>
        /// タブだった頃は「アーティストを選ぶ → 曲タブへ移る」の 2 手で、
        /// <b>いま誰を見ているのかが画面から消えていました</b>。
        /// 「別のアーティストを選んだら前のと混ざる」という報告も、
        /// もとをたどるとここです。常に見えていれば、選択は 1 手で済み、
        /// <b>いま誰を見ているかが常に画面にあります</b>。
        ///
        /// <b>20〜30 人</b>を想定しています。7 人ぶんが同時に見えて、
        /// ▲▼ で送れる —— これ以上詰めると 1 行が 4.5 cm を切って、
        /// VR の手ぶれで隣を押すようになります。
        /// </summary>
        private static UdonMediaListView BuildArtistRail(RectTransform page, float height)
        {
            const float HeaderHeight = 30f;
            const float RowHeight = 50f;
            const float RowGap = 6f;
            const int RowCount = 7;
            const float FooterHeight = 48f;

            // つまみのぶんだけ内側へ寄せる。
            const float BarLane = 22f + UdonMediaTheme.Space1;
            float width = RailWidth - BarLane;

            RectTransform rail = UdonWorldUiKit.Place(
                page, "ArtistRail", 0f, 0f, RailWidth, height);

            UdonWorldUiKit.GlassCard(
                rail, "Back", 0f, 0f, RailWidth, height,
                UdonMediaTheme.Surface, UdonMediaTheme.RadiusLarge).raycastTarget = false;

            var view = Add<UdonMediaListView>(rail.gameObject);
            if (view == null) return null;

            view.Source = UdonMediaListView.SourceArtist;
            view.HeaderLabel = "";
            view.ScrollStep = 0;
            view.GroupByChannel = false;

            UdonWorldUiKit.Label(
                rail, "RailHeader", UdonMediaTheme.Space2, 6f,
                width - UdonMediaTheme.Space2, HeaderHeight,
                UdonMediaTheme.TextCaption, TextAnchor.MiddleLeft,
                UdonMediaTheme.TextMuted).text = "ア ー テ ィ ス ト";

            float listTop = HeaderHeight + UdonMediaTheme.Space1;

            Text empty = UdonWorldUiKit.Label(
                rail, "Empty", 0f, listTop + RowHeight, width, 40f,
                UdonMediaTheme.TextCaption, TextAnchor.MiddleCenter, UdonMediaTheme.TextMuted);

            view.EmptyMessage = empty.gameObject;
            view.EmptyText = empty;

            var rows = new UdonMediaListRow[RowCount];
            for (int i = 0; i < RowCount; i++)
            {
                float rowY = listTop + i * (RowHeight + RowGap);

                UdonMediaListRow row = BuildRow(
                    rail, "Row" + i, rowY, width, RowHeight,
                    UdonMediaListView.SourceArtist, i);
                if (row == null) return view;

                row.Row = i;
                row.List = view;
                rows[i] = row;
            }
            view.Rows = rows;

            float listHeight = RowCount * (RowHeight + RowGap) - RowGap;
            BuildScroller(rail, view, RailWidth, listTop, listHeight,
                          RowHeight + RowGap, RowCount);
            if (NeedsCompile) return view;

            // ── ▲▼ だけの小さな足もと。
            //    「▲▲(先頭へ)」は<b>置きません</b> —— レールは 30 行も無いので、
            //    2 回押せば戻れます。ボタンを増やすほど 1 つが小さくなります。
            float footerY = height - FooterHeight - UdonMediaTheme.Space1;
            float half = Mathf.Floor((width - UdonMediaTheme.Space1) * 0.5f);
            int radius = Mathf.RoundToInt(FooterHeight * 0.5f);

            Text unused;
            Button up = UdonWorldUiKit.RoundedButton(
                rail, "ScrollUp", 0f, footerY, half, FooterHeight, "▲",
                22, UdonMediaTheme.SurfaceRaised, radius, out unused);

            Button down = UdonWorldUiKit.RoundedButton(
                rail, "ScrollDown", half + UdonMediaTheme.Space1, footerY,
                half, FooterHeight, "▼",
                22, UdonMediaTheme.SurfaceRaised, radius, out unused);

            view.ScrollUpButton = up.gameObject;
            view.ScrollDownButton = down.gameObject;

            UdonWorldUiKit.Wire(up, view, "ScrollUp", "上へ");
            UdonWorldUiKit.Wire(down, view, "ScrollDown", "下へ");

            return view;
        }

        /// <summary>
        /// <b>おすすめのカード。</b>3 列 × 2 段。Phase8-2 で絵を外しました。
        ///
        /// <b>カードの主役は曲名</b>です。知らない曲を勧めるので、
        /// 読ませるものが少ないほど速く選べます。
        ///
        /// <b>強調色はカード 1 枚につき「理由」の 1 行だけ</b>に使います。
        /// 理由はカードごとに違う文が出るので、<b>6 枚が同じ顔になりません</b>。
        /// ジャンル色でやろうとしていた「1 枚ずつ違って見える」を、
        /// 意味のある文字で作ります。
        /// </summary>
        private static void BuildRecommendationCards(
            RectTransform page, UdonMediaPanel panel, float width, float height)
        {
            const int Columns = 3;
            const int Rows = 2;
            const int Count = Columns * Rows;

            var view = Add<UdonRecommendationCards>(page.gameObject);
            if (view == null) return;

            float gap = UdonMediaTheme.Space2;
            float cardW = Mathf.Floor((width - gap * (Columns - 1)) / Columns);
            float cardH = Mathf.Floor((height - gap * (Rows - 1)) / Rows);

            float pad = UdonMediaTheme.Space3;
            float textW = cardW - pad * 2f;

            var cards = new GameObject[Count];
            var titles = new Text[Count];
            var artists = new Text[Count];
            var reasons = new Text[Count];
            var pressed = new GameObject[Count];

            string[] events = { "Click0", "Click1", "Click2", "Click3", "Click4", "Click5" };
            string[] queueEvents = { "Queue0", "Queue1", "Queue2", "Queue3", "Queue4", "Queue5" };

            for (int i = 0; i < Count; i++)
            {
                float cx = (i % Columns) * (cardW + gap);
                float cy = (i / Columns) * (cardH + gap);

                // カード全体が 1 つの大きなボタン。
                // 「どこを押せばよいか」を考えさせないための形です。
                UdonWorldUiKit.RoundedPlate(
                    page, "CardShadow" + i, cx, cy + UdonWorldUiKit.GlassShadowDrop,
                    cardW, cardH, UdonMediaTheme.Shadow,
                    UdonMediaTheme.RadiusLarge).raycastTarget = false;

                Button card = UdonWorldUiKit.HitArea(
                    page, "Card" + i, cx, cy, cardW, cardH, UdonMediaTheme.SurfaceRaised);
                UdonWorldUiKit.ApplyRadius(
                    card.targetGraphic as Image, UdonMediaTheme.RadiusLarge);
                UdonWorldUiKit.AddGlassEdge(
                    card.targetGraphic as Image, cardW, UdonMediaTheme.RadiusLarge);

                cards[i] = card.gameObject;

                // ── 上から順に 曲名 → アーティスト。理由だけが下に離れて座ります。
                //    <b>読む順そのもの</b>を縦の位置で作ります。
                titles[i] = UdonWorldUiKit.FittedLabel(
                    card.transform, "Title", pad, pad, textW, 76f,
                    UdonMediaTheme.TextTitle, 16, TextAnchor.UpperLeft,
                    UdonMediaTheme.TextPrimary);

                artists[i] = UdonWorldUiKit.FittedLabel(
                    card.transform, "Artist", pad, pad + 80f, textW, 28f,
                    UdonMediaTheme.TextBody, 13, TextAnchor.UpperLeft,
                    UdonMediaTheme.TextSecondary);

                // ── 理由。<b>このカードで唯一色が付くもの</b>。
                //    「押す決め手」ではなく「押したあとの納得」なので下に置きますが、
                //    色があるぶん、6 枚を見わたしたときの差はここで生まれます。
                reasons[i] = UdonWorldUiKit.Label(
                    card.transform, "Reason", pad, cardH - 34f - pad, textW - 64f, 26f,
                    UdonMediaTheme.TextCaption, TextAnchor.MiddleLeft,
                    UdonMediaTheme.Accent);

                // ── 「＋」= 再生予定へ。一覧と同じ形・同じ位置に置く。
                //    カードを押すとすぐ流れてしまうので、
                //    「いまは流さず覚えておく」道が要ります。
                const float PlusSize = 52f;

                Text plusLabel;
                Button plus = UdonWorldUiKit.RoundedButton(
                    card.transform, "Queue",
                    cardW - PlusSize - pad, cardH - PlusSize - pad,
                    PlusSize, PlusSize, "＋", 26,
                    UdonMediaTheme.Base, Mathf.RoundToInt(PlusSize * 0.5f), out plusLabel);

                UdonWorldUiKit.Wire(plus, view, queueEvents[i], "再生予定に追加");

                Image press = UdonWorldUiKit.RoundedPlate(
                    card.transform, "Pressed", 0f, 0f, cardW, cardH,
                    UdonMediaTheme.SurfacePressed, UdonMediaTheme.RadiusLarge);
                press.raycastTarget = false;
                press.gameObject.SetActive(false);
                pressed[i] = press.gameObject;

                UdonWorldUiKit.Wire(card, view, events[i], "再生");
            }

            Text empty = UdonWorldUiKit.Label(
                page, "Empty", 0f, height * 0.4f, width, 48f, UdonMediaTheme.TextBody,
                TextAnchor.MiddleCenter, UdonMediaTheme.TextMuted);

            view.Cards = cards;
            view.Titles = titles;
            view.Artists = artists;
            view.Reasons = reasons;
            view.PressedMarkers = pressed;
            view.EmptyMessage = empty.gameObject;
            view.EmptyText = empty;

            panel.Cards = view;
        }

        /// <summary>
        /// <b>曲の一覧。</b>Phase8-2 で 1 行 64 px に戻しました。
        ///
        /// 絵を捨てたので、行に要るのは<b>文字 2 段ぶんの高さ</b>だけです。
        /// 112 px は 16:9 の絵を入れるための高さで、いまは意味がありません。
        /// 詰めたぶん、<b>同じ面積で 5 行 → 6 行</b>が見えるようになります。
        /// 64 px(≒ 8.3 cm)は VR で狙う面積としては据え置きなので、
        /// <b>押しやすさは変わりません</b>。
        /// </summary>
        private static UdonMediaListView BuildList(
            RectTransform page, int source, float pageWidth, float height)
        {
            const float RowHeight = 64f;
            const float RowGap = 6f;
            const float FooterHeight = 48f;

            // つまみのぶんだけ内側へ寄せる。重ねると「＋」が押せなくなる。
            const float ScrollBarLane = 22f + UdonMediaTheme.Space1;
            float width = pageWidth - ScrollBarLane;

            var view = Add<UdonMediaListView>(page.gameObject);
            if (view == null) return null;

            view.Source = source;
            view.HeaderLabel = "";
            view.ScrollStep = 0;
            view.FollowNowPlaying = source == UdonMediaListView.SourceQueue;

            // ── チャンネルごとのまとめは<b>使いません</b>(Phase8-2)。
            //    その役はレールが引き受けました。呼び出し側で上書きされますが、
            //    ここでも既定を切っておきます。
            view.GroupByChannel = false;

            // ── お気に入りの並べ替え(Phase7-8)
            float listTop = 0f;
            if (source == UdonMediaListView.SourceFavorite)
            {
                const float SortHeight = 48f;

                Text sortLabel;
                Button sort = UdonWorldUiKit.RoundedButton(
                    page, "Sort", 0f, 0f, width, SortHeight, "追加が新しい順",
                    UdonMediaTheme.TextCaption, UdonMediaTheme.Surface,
                    UdonMediaTheme.RadiusMedium, out sortLabel);

                view.SortLabel = sortLabel;
                UdonWorldUiKit.Wire(sort, view, "CycleSort", "並べ替え");

                listTop = SortHeight + UdonMediaTheme.Space1;
            }

            // ── 上に貼り付くチャンネルの帯は<b>置きません</b>(Phase8-2)。
            //    まとめをやめたので見出しそのものが出ず、
            //    「いま誰を見ているか」はレールの選択が示します。

            // 何行入るかは、残りの高さから決めます。
            //    先に行数を決めると、パネルの寸法を変えたときに
            //    <b>足もとのボタンが枠からはみ出します</b>。
            float listRoom = height - listTop - FooterHeight - UdonMediaTheme.Space1;
            int rowCount = Mathf.FloorToInt((listRoom + RowGap) / (RowHeight + RowGap));
            if (rowCount < 1) rowCount = 1;

            // ── 何も無いときの案内。種類ごとに文が変わる。
            Text empty = UdonWorldUiKit.Label(
                page, "Empty", 0f, listTop + RowHeight * 0.5f, width, 44f,
                UdonMediaTheme.TextBody, TextAnchor.MiddleCenter, UdonMediaTheme.TextMuted);

            view.EmptyMessage = empty.gameObject;
            view.EmptyText = empty;

            // ── 行
            var rows = new UdonMediaListRow[rowCount];
            for (int i = 0; i < rowCount; i++)
            {
                float rowY = listTop + i * (RowHeight + RowGap);

                UdonMediaListRow row = BuildRow(page, "Row" + i, rowY, width, RowHeight, source, i);
                if (row == null) return view;

                row.Row = i;
                row.List = view;
                rows[i] = row;
            }
            view.Rows = rows;

            // ── つまみ + ホイール(Phase7-3)
            float listHeight = rowCount * (RowHeight + RowGap) - RowGap;
            BuildScroller(page, view, pageWidth, listTop, listHeight,
                          RowHeight + RowGap, rowCount);
            if (NeedsCompile) return view;

            // ── 下の帯:何件目を見ているか + スクロール
            float footerY = height - FooterHeight;

            view.RangeText = UdonWorldUiKit.Label(
                page, "Range", 0f, footerY, 300f, FooterHeight, UdonMediaTheme.TextCaption,
                TextAnchor.MiddleLeft, UdonMediaTheme.TextMuted);

            const float ScrollWidth = 76f;
            float ScrollGap = UdonMediaTheme.Space1;

            float downX = width - ScrollWidth;
            float upX = downX - ScrollWidth - ScrollGap;
            float homeX = upX - ScrollWidth - ScrollGap;

            int radius = Mathf.RoundToInt(FooterHeight * 0.5f);

            Text unused;
            Button home = UdonWorldUiKit.RoundedButton(
                page, "ScrollHome", homeX, footerY, ScrollWidth, FooterHeight, "▲▲",
                20, UdonMediaTheme.Surface, radius, out unused);

            Button up = UdonWorldUiKit.RoundedButton(
                page, "ScrollUp", upX, footerY, ScrollWidth, FooterHeight, "▲",
                24, UdonMediaTheme.Surface, radius, out unused);

            Button down = UdonWorldUiKit.RoundedButton(
                page, "ScrollDown", downX, footerY, ScrollWidth, FooterHeight, "▼",
                24, UdonMediaTheme.Surface, radius, out unused);

            view.ScrollHomeButton = home.gameObject;
            view.ScrollUpButton = up.gameObject;
            view.ScrollDownButton = down.gameObject;

            UdonWorldUiKit.Wire(home, view, "ScrollHome", "再生中 / 先頭へ");
            UdonWorldUiKit.Wire(up, view, "ScrollUp", "上へ(続けて押すと速い)");
            UdonWorldUiKit.Wire(down, view, "ScrollDown", "下へ(続けて押すと速い)");

            return view;
        }

        /// <summary>
        /// <b>ホイールとつまみ。</b>Phase7-3。
        ///
        /// <b>行はここに入りません。</b><see cref="ScrollRect"/> の中身は
        /// <b>高さだけを持つ透明な板</b>で、行は今までどおり 4〜5 個を使い回します。
        /// <see cref="ScrollRect"/> には<b>「どれだけ動かしたか」を測る役だけ</b>を
        /// 任せていて、そこから先は <see cref="UdonListScroller"/> が
        /// 一覧の <c>Offset</c> へ写します。
        ///
        /// こうすると、曲が何千曲あっても<b>作る GameObject は増えません</b>。
        /// それでいて、ホイールもドラッグも<b>uGUI が最初から持っている動き</b>が
        /// そのまま効きます。
        /// </summary>
        private static void BuildScroller(
            RectTransform page, UdonMediaListView view,
            float width, float listTop, float listHeight, float rowPitch, int visibleRows)
        {
            const float BarWidth = 22f;

            if (listHeight <= 0f) return;

            // ── ホイールは「行の上で回したとき」に効かないと意味がない。
            //
            //    uGUI のホイールは<b>指しているものから親へ順に登っていって</b>、
            //    最初に受け取れる相手に届きます。だから ScrollRect は
            //    <b>行より上の階層(= このページそのもの)</b>に付けます。
            //    別の入れ物を作って重ねると、行はその外側にいるので
            //    <b>行の上で回しても何も起きません</b>。
            //
            //    ScrollRect が動かすのは Content(高さだけを持つ透明な板)だけです。
            //    行はその外にいるので、勝手に動かされることはありません。
            RectTransform content = UdonWorldUiKit.Place(
                page, "ScrollContent", 0f, listTop, width, listHeight);
            content.SetAsFirstSibling();

            var scroll = page.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = page;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;         // VR で慣性が付くと狙った所で止まらない
            scroll.scrollSensitivity = rowPitch * 0.5f;

            // ── つまみ。右端に。
            //    VR のレーザーでつまめる幅が要るので、見た目より太めにします。
            RectTransform barRect = UdonWorldUiKit.Place(
                page, "ScrollBar", width - BarWidth, listTop, BarWidth, listHeight);

            Image barBack = UdonWorldUiKit.RoundedPlate(
                barRect, "Track", 0f, 0f, BarWidth, listHeight,
                new Color(0f, 0f, 0f, 0.06f), Mathf.RoundToInt(BarWidth * 0.5f));
            barBack.raycastTarget = true;

            var bar = barRect.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;

            RectTransform sliding = UdonWorldUiKit.Place(
                barRect, "SlidingArea", 0f, 0f, BarWidth, listHeight);
            sliding.anchorMin = Vector2.zero;
            sliding.anchorMax = Vector2.one;
            sliding.offsetMin = Vector2.zero;
            sliding.offsetMax = Vector2.zero;

            Image handle = UdonWorldUiKit.RoundedPlate(
                sliding, "Handle", 0f, 0f, BarWidth, 120f,
                new Color(0f, 0f, 0f, 0.26f), Mathf.RoundToInt(BarWidth * 0.5f));
            handle.raycastTarget = true;

            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;

            bar.handleRect = handleRect;
            bar.targetGraphic = handle;
            bar.value = 1f;

            scroll.verticalScrollbar = bar;

            // ── 橋渡し役
            var scroller = Add<UdonListScroller>(page.gameObject);
            if (scroller == null) return;

            scroller.List = view;
            scroller.Scroll = scroll;
            scroller.Bar = bar;
            scroller.Content = content;
            scroller.RowPitch = rowPitch;
            scroller.VisibleRows = visibleRows;

            view.Scroller = scroller;

            UdonWorldUiKit.Bind(scroll, scroller, "OnScrolled");
            UdonWorldUiKit.Bind(bar, scroller, "OnScrolled");

            // ── 「使う」でもスクロールできるようにする(Phase7-6)。
            //    つまみを掴めない代わりに、行きたい高さを指して使います。
            //    Scrollbar は下が 0・上が 1 なので、上から数えた区画とは逆向きです。
            UdonWorldUiKit.ValueStrip(
                page, "ScrollStrip", width - BarWidth, listTop, BarWidth, listHeight,
                ScrollSegments, null, null, scroller, false, null, "", "");
        }

        /// <summary>
        /// <b>検索バー。</b>Phase7-2。押すと VRChat のキーボードが出ます。
        ///
        /// <b>打つそばから絞り込みます。</b>「検索」ボタンを押させると、
        /// <b>押し忘れて「効いていない」と思われます</b>。
        /// </summary>
        /// <returns>使った高さ。</returns>
        private static float BuildSearchBar(
            RectTransform page, UdonMediaListView view, float width)
        {
            const float Height = 64f;
            const float ClearWidth = 56f;

            RectTransform bar = UdonWorldUiKit.Place(page, "Search", 0f, 0f, width, Height);

            // 入力欄は「へこんで見える」ほうが、打つ場所だと分かりやすい。
            Image back = UdonWorldUiKit.GlassPlate(
                bar, "Back", 0f, 0f, width, Height,
                UdonMediaTheme.SurfaceRaised, Mathf.RoundToInt(Height * 0.5f));
            back.raycastTarget = false;

            // ── 虫めがねの絵文字は使いません。
            //    ワールドで使える組み込みフォントに無いことがあり、
            //    そのときは□(豆腐)になります。<b>読めない記号は
            //    「壊れている」という印象にしかなりません</b>。
            //    形で伝わるように、○ に柄を付けた図形で描きます。
            RectTransform icon = UdonWorldUiKit.Place(
                bar, "Icon", UdonMediaTheme.Space2, (Height - 24f) * 0.5f, 24f, 24f);

            // 輪郭だけにしたいので、内側を背景色で塗りつぶす。
            UdonWorldUiKit.RoundedPlate(
                icon, "LensRing", 0f, 0f, 17f, 17f, UdonMediaTheme.TextMuted, 8)
                .raycastTarget = false;
            UdonWorldUiKit.RoundedPlate(
                icon, "LensHole", 3f, 3f, 11f, 11f, UdonMediaTheme.Surface, 5)
                .raycastTarget = false;
            UdonWorldUiKit.RoundedPlate(
                icon, "LensGrip", 14f, 14f, 9f, 3f, UdonMediaTheme.TextMuted, 1)
                .raycastTarget = false;

            float fieldX = UdonMediaTheme.Space2 + 36f + UdonMediaTheme.Space1;
            float fieldWidth = width - fieldX - ClearWidth - UdonMediaTheme.Space1;

            RectTransform fieldRect = UdonWorldUiKit.Place(
                bar, "Field", fieldX, 0f, fieldWidth, Height);

            // InputField は自分の当たり判定が要る(押すとキーボードが出る)。
            Image fieldBack = fieldRect.gameObject.AddComponent<Image>();
            fieldBack.color = new Color(0f, 0f, 0f, 0.001f);

            // ── <b>VRCUrlInputField を使います</b>(Phase7-10)。
            //
            //    ふつうの InputField は、VR では<b>キーボードが出ません</b>。
            //    出るのは VRChat 側が用意している <c>VRCUrlInputField</c> の
            //    入力ダイアログだけで、URL 欄で開いたのがそれです。
            //    <b>打ち込まれた文字はそのまま読めます</b>(URL かどうかは
            //    再生するときにしか問われません)。探す言葉の入れ物として使います。
            var field = fieldRect.gameObject.AddComponent<VRCUrlInputField>();

            Text typed = UdonWorldUiKit.Label(
                fieldRect, "Text", 0f, 0f, fieldWidth, Height, UdonMediaTheme.TextBody,
                TextAnchor.MiddleLeft, UdonMediaTheme.TextPrimary);
            typed.raycastTarget = false;

            Text placeholder = UdonWorldUiKit.Label(
                fieldRect, "Placeholder", 0f, 0f, fieldWidth, Height, UdonMediaTheme.TextBody,
                TextAnchor.MiddleLeft, UdonMediaTheme.TextMuted);
            placeholder.text = "曲名・チャンネル・ジャンルで探す";
            placeholder.raycastTarget = false;

            field.textComponent = typed;
            field.placeholder = placeholder;
            field.targetGraphic = fieldBack;

            Text unused;
            Button clear = UdonWorldUiKit.RoundedButton(
                bar, "SearchClear", width - ClearWidth - UdonMediaTheme.Space1,
                (Height - 44f) * 0.5f, 44f, 44f, "×", 24,
                UdonMediaTheme.SurfaceRaised, 22, out unused);

            view.SearchField = field;
            view.SearchClearButton = clear.gameObject;

            // ── <b>onValueChanged は繋ぎません</b>(Phase7-10)。
            //
            //    <c>VRCUrlInputField</c> は <c>InputField</c> の仲間ではないので、
            //    <c>Bind</c> の相手として渡せません。渡そうとしたせいで
            //    <b>ビルドが止まりました</b>。
            //    そもそも実機では uGUI のイベントが飛んでこないため、
            //    打った文字は <c>PollSearchField</c> が書き直しのたびに読んでいます。
            //    <b>繋がなくても検索は効きます</b>。
            UdonWorldUiKit.Wire(clear, view, "ClearSearch", "検索をやめる");

            clear.gameObject.SetActive(false);

            // ── 枠のどこを「使う」でも VRChat のキーボードが出るようにする(Phase7-6)。
            //
            //    InputField は uGUI のポインターで選ばれないと開きません。
            //    そのポインターが実機で届いていないので、
            //    <b>選ぶところだけ「使う」で肩代わり</b>します。
            //    虫めがねから「×」の手前まで、枠ぜんぶが押せます。
            UdonWorldUiKit.InteractArea(
                bar, "SearchHit", 0f, 0f, width - ClearWidth - UdonMediaTheme.Space1, Height,
                view, "OpenSearchKeyboard", "文字を打つ");

            return Height;
        }

        /// <summary>
        /// <b>1 行。</b>番号 + 曲名 + アーティスト · ジャンル + 長さ + ♥ + 「＋」。Phase8-2。
        ///
        /// <b>左端は正方形ではなく番号</b>です。数字は<b>情報であって
        /// 絵の代用品ではない</b>ので、「絵が抜けている」ようには見えません。
        /// 44 px の数字が縦の基準線を作り、目が上から下へ滑ります。
        ///
        /// 鳴っている行では、番号が<b>そのままの位置で</b>動く 3 本の棒に変わります。
        /// 別の場所に置くと、鳴っている行だけ文字の開始位置がずれて、
        /// 一覧の左端が<b>がたつきます</b>。
        ///
        /// <b>文字は 2 段だけ</b>にしてあります。3 段にすると 1 行あたりが小さくなり、
        /// 2 m 先で読めなくなります。
        /// </summary>
        private static UdonMediaListRow BuildRow(
            RectTransform parent, string name, float y, float width, float height,
            int source, int index)
        {
            const float BarWidth = 4f;
            const float SecondaryWidth = 64f;

            // 番号と棒を置く欄。ここが一覧の「左の基準線」になります。
            const float IndexWidth = 44f;

            // 3 本の棒の幅。BuildEqualizer と同じ数え方(3 × (7 + 5))。
            const float EqualizerWidth = 36f;

            bool queue = source == UdonMediaListView.SourceQueue;

            // ── アーティストのレールは<b>見出ししか出しません</b>。
            //    ♥ と「＋」を作っても一度も見えないうえ、
            //    幅 230 px の中に置くと中身の幅が 60 px を切ります。
            bool namesOnly = source == UdonMediaListView.SourceArtist;

            RectTransform rowRect = UdonWorldUiKit.Place(parent, name, 0f, y, width, height);

            var row = Add<UdonMediaListRow>(rowRect.gameObject);
            if (row == null) return null;

            // 中身は「行そのもの」ではなく子に置く。
            // 空行で非アクティブにする対象が UdonBehaviour 本体だと、
            // 二度と書き戻せなくなるため。
            RectTransform content = UdonWorldUiKit.Place(rowRect, "Content", 0f, 0f, width, height);

            float hitX = BarWidth + UdonMediaTheme.Space1;

            // ♥ と「＋」の 2 つぶん、行の中身を詰める。
            float hitWidth = namesOnly
                ? width - hitX
                : width - hitX - SecondaryWidth * 2f - UdonMediaTheme.Space1 * 2f;

            // ── 1 行 = 1 枚の硝子カード(Frost / Phase7-7)。
            //    影(下)と縁の光(上)を対で置いて、はじめて浮いて見えます。
            UdonWorldUiKit.RoundedPlate(
                content, "Shadow", hitX, UdonWorldUiKit.GlassShadowDrop, hitWidth, height,
                UdonMediaTheme.Shadow, UdonMediaTheme.RadiusMedium).raycastTarget = false;

            // 面は半透明の白。ここを完全に透明にすると、
            // 触れたときの明るさ変化(uGUI の ColorTint)が効かなくなります。
            Button hit = UdonWorldUiKit.HitArea(
                content, "Hit", hitX, 0f, hitWidth, height, UdonMediaTheme.Surface);
            UdonWorldUiKit.ApplyRadius(hit.targetGraphic as Image, UdonMediaTheme.RadiusMedium);
            UdonWorldUiKit.AddGlassEdge(
                hit.targetGraphic as Image, hitWidth, UdonMediaTheme.RadiusMedium);

            // ── 番号。行の高さいっぱいに縦中央で置きます。
            //    <b>いちばん小さい段</b>を使います —— 4 桁(1043)まで
            //    44 px に収まり、かつ 2 m から読める下限だからです。
            row.IndexText = UdonWorldUiKit.Label(
                hit.transform, "Index", UdonMediaTheme.Space2, 0f, IndexWidth, height,
                UdonMediaTheme.TextCaption, TextAnchor.MiddleRight, UdonMediaTheme.TextMuted);

            // ── 動く 3 本の棒。<b>番号とぴったり同じ場所</b>に重ねて置きます。
            //    番号は右揃えなので、棒も右端をそろえます。
            row.EqualizerBars = BuildEqualizer(
                hit.transform,
                UdonMediaTheme.Space2 + IndexWidth - EqualizerWidth,
                (height - 22f) * 0.5f);

            // ── 文字
            float textX = UdonMediaTheme.Space2 + IndexWidth + UdonMediaTheme.Space3;
            float durationWidth = 86f;
            float textWidth = hitWidth - textX - durationWidth - UdonMediaTheme.Space2;

            // 曲名は長さがまちまちなので、入り切るまで小さくする。
            // 途中で切れた名前は、小さい名前より役に立たない。
            row.TitleText = UdonWorldUiKit.FittedLabel(
                hit.transform, "Title", textX, height * 0.14f, textWidth, height * 0.40f,
                UdonMediaTheme.TextTitle, 15, TextAnchor.LowerLeft, UdonMediaTheme.TextPrimary);

            row.SubText = UdonWorldUiKit.FittedLabel(
                hit.transform, "Sub", textX, height * 0.56f, textWidth, height * 0.30f,
                UdonMediaTheme.TextCaption, 12, TextAnchor.UpperLeft,
                UdonMediaTheme.TextSecondary);

            row.DurationText = UdonWorldUiKit.Label(
                hit.transform, "Duration", hitWidth - durationWidth - UdonMediaTheme.Space2, 0f,
                durationWidth, height, UdonMediaTheme.TextCaption,
                TextAnchor.MiddleRight, UdonMediaTheme.TextMuted);

            if (!namesOnly)
            {
                // ── 2 つめのボタン。記号だけにして、意味は「使う」の案内に任せる。
                Text secondaryLabel;
                Button secondaryButton = UdonWorldUiKit.RoundedButton(
                    content, "Secondary", width - SecondaryWidth,
                    (height - SecondaryWidth) * 0.5f,
                    SecondaryWidth, SecondaryWidth, queue ? "×" : "＋", 30,
                    UdonMediaTheme.SurfaceRaised,
                    Mathf.RoundToInt(SecondaryWidth * 0.5f), out secondaryLabel);

                row.SecondaryLabel = secondaryLabel;
                row.SecondaryButton = secondaryButton.gameObject;

                UdonWorldUiKit.Wire(secondaryButton, row, "ClickSecondary",
                                    queue ? "再生予定から外す" : "再生予定に追加");

                // ── ♥(Phase7-8)。「好き」と「いま聴く」は別なので、別のボタンにする。
                //    形(♥ / ♡)ではなく<b>色</b>で入っているかを示します —— 組み込み
                //    フォントに ♡ が無い環境で豆腐(□)になるのを避けるためです。
                Text favoriteLabel;
                Button favoriteButton = UdonWorldUiKit.RoundedButton(
                    content, "Favorite",
                    width - SecondaryWidth * 2f - UdonMediaTheme.Space1,
                    (height - SecondaryWidth) * 0.5f,
                    SecondaryWidth, SecondaryWidth, "♥", 28,
                    UdonMediaTheme.Surface,
                    Mathf.RoundToInt(SecondaryWidth * 0.5f), out favoriteLabel);

                row.FavoriteButton = favoriteButton.gameObject;
                row.FavoriteLabel = favoriteLabel;
                row.FavoriteOnColor = UdonMediaTheme.Accent;
                row.FavoriteOffColor = UdonMediaTheme.TextMuted;

                UdonWorldUiKit.Wire(favoriteButton, row, "ClickFavorite", "お気に入り");
            }

            // ── 印は中身より後に置く(半透明でかぶせる)。
            Image highlight = UdonWorldUiKit.RoundedPlate(
                rowRect, "Highlight", hitX, 0f, hitWidth, height,
                UdonMediaTheme.AccentWash, UdonMediaTheme.RadiusMedium);
            highlight.raycastTarget = false;
            highlight.gameObject.SetActive(false);

            Image pressed = UdonWorldUiKit.RoundedPlate(
                rowRect, "Pressed", hitX, 0f, hitWidth, height,
                UdonMediaTheme.SurfacePressed, UdonMediaTheme.RadiusMedium);
            pressed.raycastTarget = false;
            pressed.gameObject.SetActive(false);

            Image nowPlayingBar = UdonWorldUiKit.RoundedPlate(
                rowRect, "NowPlayingBar", 0f, UdonMediaTheme.Space1, BarWidth,
                height - UdonMediaTheme.Space1 * 2f, UdonMediaTheme.Accent, 2);
            nowPlayingBar.raycastTarget = false;
            nowPlayingBar.gameObject.SetActive(false);

            // ── 見出しとして使うときの帯。
            //    同じ行を曲としても見出しとしても使うので、重ねて置いて出し分けます。
            //    <b>アーティストのレールは見出しだけで出来ています</b> ——
            //    ここを通さないと 1 行も見えません。
            if (source == UdonMediaListView.SourceLibrary || namesOnly)
            {
                BuildHeaderBand(rowRect, row, hit, width, height);
            }

            row.Content = content.gameObject;
            row.Highlight = highlight.gameObject;
            row.PressedMarker = pressed.gameObject;
            row.NowPlayingBar = nowPlayingBar.gameObject;
            row.TitleColor = UdonWorldUiKit.TextPrimary;
            row.NowPlayingTitleColor = UdonWorldUiKit.TextNowPlaying;

            UdonWorldUiKit.Wire(hit, row, "Click", queue ? "この曲へ移動" : "再生");

            return row;
        }

        /// <summary>
        /// <b>チャンネルの見出しの帯。</b>Phase7-2。
        ///
        /// 曲の行に<b>重ねて</b>置いて、出し分けます。見出し専用の行を別に持つと
        /// <b>「見出しが何個要るか」を先に決めないと行を用意できません</b>が、
        /// 検索でチャンネルが減れば見出しも減るので、それは決められません。
        /// </summary>
        private static void BuildHeaderBand(
            RectTransform rowRect, UdonMediaListRow row, Button hit, float width, float height)
        {
            // ── 見出しの帯。
            //    曲の行(112px)の中では低くして「親」に見せますが、
            //    アーティストの一覧(72px)では<b>行いっぱい</b>まで使います。
            //    そこには曲の行が無いので、低くする理由がありません。
            float BandHeight = height > 90f ? 64f : height;

            float top = (height - BandHeight) * 0.5f;

            RectTransform band = UdonWorldUiKit.Place(
                rowRect, "HeaderBand", 0f, top, width, BandHeight);

            UdonWorldUiKit.RoundedPlate(
                band, "Back", 0f, 0f, width, BandHeight,
                UdonMediaTheme.Surface, UdonMediaTheme.RadiusMedium).raycastTarget = false;

            Text arrow = UdonWorldUiKit.Label(
                band, "Arrow", UdonMediaTheme.Space2, 0f, 32f, BandHeight, 18,
                TextAnchor.MiddleCenter, UdonMediaTheme.TextMuted);

            Text channel = UdonWorldUiKit.FittedLabel(
                band, "Channel", UdonMediaTheme.Space2 + 40f, 0f, width - 200f, BandHeight,
                UdonMediaTheme.TextBody, 13, TextAnchor.MiddleLeft, UdonMediaTheme.TextPrimary);

            Text count = UdonWorldUiKit.Label(
                band, "Count", width - 130f, 0f, 112f - UdonMediaTheme.Space2, BandHeight,
                UdonMediaTheme.TextCaption, TextAnchor.MiddleRight, UdonMediaTheme.TextMuted);

            row.HeaderBand = band.gameObject;
            row.HeaderArrowText = arrow;
            row.HeaderText = channel;
            row.HeaderCountText = count;

            band.gameObject.SetActive(false);

            // 見出しも「行を押す」で開け閉てする。当たり判定は曲と同じものを使う。
            // 帯のほうが幅が広いので、帯側にも当たり判定を足しておく。
            Button headerHit = UdonWorldUiKit.HitArea(
                band, "HeaderHit", 0f, 0f, width, BandHeight, new Color(0f, 0f, 0f, 0.001f));

            UdonWorldUiKit.Wire(headerHit, row, "Click", "開く / たたむ");
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
