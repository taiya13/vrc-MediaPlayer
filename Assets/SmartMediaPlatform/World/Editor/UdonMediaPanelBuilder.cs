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
                typeof(UdonListScroller),
                typeof(UdonRecommendationCards),
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
        private const float ColumnGap = UdonMediaTheme.Space4;    // 32
        private const float LeftWidth = 568f;
        private const float RightWidth = 736f;

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

            // ── パネルそのものが 1 枚の硝子(Frost / Phase7-7)。
            //    半透明なので<b>後ろのワールドが透けます</b>。
            UdonWorldUiKit.GlassCard(
                body, "Backplate", 0f, 0f, W, H,
                UdonMediaTheme.Base, UdonMediaTheme.RadiusXLarge).raycastTarget = false;

            var panel = Add<UdonMediaPanel>(root);
            if (panel == null) return null;

            // ── 左:いま鳴っているもの
            //    上から順に「絵 → 曲名 → バー → 操作 → 音量」。
            //    視線は上から下へ流れるので、いちばん知りたいもの(何が鳴っているか)を
            //    いちばん上に置き、いちばん押すもの(再生)をその次に置く。
            float leftY = Pad + UdonMediaTheme.Space2;

            float nowPlayingHeight;
            var nowPlaying = BuildNowPlaying(
                body, leftX, leftY, LeftWidth, true, out nowPlayingHeight);
            if (NeedsCompile) return panel;
            leftY += nowPlayingHeight + UdonMediaTheme.Space2;

            var transport = BuildTransport(body, leftX, leftY, LeftWidth, 96f, true);
            if (NeedsCompile) return panel;
            leftY += 96f + UdonMediaTheme.Space2;

            BuildVolume(body, transport, leftX, leftY, LeftWidth, 56f);
            leftY += 56f + UdonMediaTheme.Space2;

            Text status = UdonWorldUiKit.Label(
                body, "Status", leftX, leftY, LeftWidth, 32f, UdonMediaTheme.TextCaption,
                TextAnchor.MiddleLeft, UdonMediaTheme.TextMuted);

            // ── 右:これから選ぶもの
            var tabs = BuildTabbedLists(body, panel, rightX, Pad, RightWidth, H - Pad * 2f);
            if (NeedsCompile) return panel;

            // 見出しは絵の上に小さく。パネルの名前より、鳴っている曲のほうが大事。
            Text panelTitle = UdonWorldUiKit.Label(
                body, "PanelTitle", leftX, Pad * 0.5f, 400f, 26f, UdonMediaTheme.TextCaption,
                TextAnchor.MiddleLeft, UdonMediaTheme.TextMuted);

            Text syncOwner = UdonWorldUiKit.Label(
                body, "SyncOwner", rightX, Pad * 0.5f, RightWidth, 26f, UdonMediaTheme.TextCaption,
                TextAnchor.MiddleRight, UdonMediaTheme.TextMuted);

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

            // ── パネルそのものが 1 枚の硝子(Frost / Phase7-7)。
            //    半透明なので<b>後ろのワールドが透けます</b>。
            UdonWorldUiKit.GlassCard(
                body, "Backplate", 0f, 0f, W, H,
                UdonMediaTheme.Base, UdonMediaTheme.RadiusXLarge).raycastTarget = false;

            var panel = Add<UdonMediaPanel>(root);
            if (panel == null) return null;

            float y = RemotePad + UdonMediaTheme.Space2;

            float nowPlayingHeight;
            var nowPlaying = BuildNowPlaying(body, RemotePad, y, CW, false, out nowPlayingHeight);
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
        /// <paramref name="wide"/> が true なら絵を上に大きく、false なら左に小さく。
        /// リモコンは手に持つので、絵を大きくすると視界を塞ぎます。
        /// </summary>
        private static UdonNowPlayingView BuildNowPlaying(
            RectTransform body, float x, float y, float width, bool wide, out float consumedHeight)
        {
            // ── 絵を主役にする。
            //    「何が鳴っているか」に一目で答えるのがこの区画の仕事なので、
            //    ボタンより絵のほうが大きい。壁パネルでは幅いっぱい(16:9)。
            float artWidth = wide ? width : 220f;
            float artHeight = Mathf.Round(artWidth * 9f / 16f);

            int titleSize = wide ? UdonMediaTheme.TextDisplay : UdonMediaTheme.ForRemote(UdonMediaTheme.TextTitle);
            int artistSize = wide ? UdonMediaTheme.TextTitle : UdonMediaTheme.ForRemote(UdonMediaTheme.TextBody);
            int metaSize = wide ? UdonMediaTheme.TextCaption : UdonMediaTheme.ForRemote(UdonMediaTheme.TextCaption);

            float titleHeight = Mathf.Round(titleSize * 1.32f);
            float artistHeight = Mathf.Round(artistSize * 1.32f);
            float chipHeight = wide ? 26f : 22f;
            float metaHeight = Mathf.Round(metaSize * 1.6f);

            // つかめるバーは、見た目より当たり判定を広く取る。
            float seekTouch = wide ? 40f : 34f;
            float seekBar = wide ? 8f : 7f;

            float textTop = wide ? artHeight + UdonMediaTheme.Space2 : 0f;
            float textLeft = wide ? 0f : artWidth + UdonMediaTheme.Space2;
            float textWidth = wide ? width : width - textLeft;

            float textHeight = titleHeight + artistHeight + UdonMediaTheme.Space1 + chipHeight;
            float barTop = (wide ? textTop + textHeight : artHeight) + UdonMediaTheme.Space2;

            float total = barTop + seekTouch + metaHeight;

            consumedHeight = total;

            RectTransform section = UdonWorldUiKit.Place(body, "NowPlaying", x, y, width, total);

            var view = Add<UdonNowPlayingView>(section.gameObject);
            if (view == null) return null;

            // ── 絵。<b>この区画でいちばん面積を取るもの</b>。
            //    影を敷いて、硝子の上に絵が 1 枚置かれているように見せます。
            UdonWorldUiKit.RoundedPlate(
                section, "ArtworkShadow", 0f, UdonWorldUiKit.GlassShadowDrop * 1.5f,
                artWidth, artHeight, UdonMediaTheme.Shadow,
                UdonMediaTheme.RadiusLarge).raycastTarget = false;

            Image artwork = UdonWorldUiKit.RoundedPlate(
                section, "Artwork", 0f, 0f, artWidth, artHeight,
                UdonMediaTheme.SurfaceHover, UdonMediaTheme.RadiusLarge);
            artwork.raycastTarget = false;
            artwork.preserveAspect = true;

            // 曲が変わったときに、ここを少しだけ弾ませる(Frost / Phase7-7)。
            view.ArtworkRect = artwork.GetComponent<RectTransform>();
            view.ArtworkPopSeconds = UdonMediaTheme.MotionSlow;
            view.ArtworkPopScale = UdonMediaTheme.SelectScale;

            Text artworkFallback = UdonWorldUiKit.Label(
                artwork.transform, "Fallback", 0f, 0f, artWidth, artHeight,
                wide ? 30 : 20, TextAnchor.MiddleCenter, UdonMediaTheme.TextMuted);

            view.Artwork = artwork;
            view.ArtworkFallbackText = artworkFallback;

            // ── 曲名 / チャンネル / ジャンル
            //    大きさではなく明るさで主従を付ける。曲名だけが明るい。
            float cursor = textTop;

            view.TitleText = UdonWorldUiKit.FittedLabel(
                section, "Title", textLeft, cursor, textWidth, titleHeight, titleSize,
                wide ? 18 : 14, TextAnchor.LowerLeft, UdonMediaTheme.TextPrimary);
            cursor += titleHeight;

            view.ArtistText = UdonWorldUiKit.FittedLabel(
                section, "Artist", textLeft, cursor, textWidth, artistHeight, artistSize,
                wide ? 14 : 12, TextAnchor.UpperLeft, UdonMediaTheme.TextSecondary);
            cursor += artistHeight + UdonMediaTheme.Space1;

            Image chip = UdonWorldUiKit.RoundedPlate(
                section, "GenreChip", textLeft, cursor, 104f, chipHeight,
                new Color(0f, 0f, 0f, 0.06f), UdonMediaTheme.RadiusSmall);
            chip.raycastTarget = false;

            view.GenreChip = chip.gameObject;
            view.GenreText = UdonWorldUiKit.Label(
                chip.transform, "Genre", 0f, 0f, 104f, chipHeight,
                wide ? 15 : 13,
                TextAnchor.MiddleCenter, UdonMediaTheme.TextSecondary);

            // ── つかんで動かせる再生バー(Phase7-3)
            Image seekFill;
            Slider seek = UdonWorldUiKit.DragBar(
                section, "Seek", 0f, barTop, width, seekTouch, seekBar, out seekFill);

            view.SeekSlider = seek;
            view.ProgressFill = seekFill;

            UdonWorldUiKit.WireSlider(seek, view, "OnSeekChanged", "再生位置を動かす");

            // ── 「使う」でも動かせるようにする(Phase7-6)。
            //    実機では uGUI のポインターが届いていないので、
            //    バーの上に細長い当たり判定を並べて、指した所へ飛ばします。
            UdonWorldUiKit.ValueStrip(
                section, "SeekStrip", 0f, barTop, width, seekTouch,
                SeekSegments, seek, null, null, false, view, "OnSeekChanged", "");

            // ── 時間(左に経過 / 右に残り)
            //    真ん中に状態を置くと 3 つが競合するので、
            //    状態は「読み込み中」など、伝えることがあるときだけ出す。
            float metaTop = barTop + seekTouch;
            float third = (width - UdonMediaTheme.Space2) / 3f;

            view.TimeText = UdonWorldUiKit.Label(
                section, "Time", 0f, metaTop, third, metaHeight, metaSize,
                TextAnchor.MiddleLeft, UdonMediaTheme.TextSecondary);

            view.StateText = UdonWorldUiKit.Label(
                section, "State", third, metaTop, third + UdonMediaTheme.Space2, metaHeight, metaSize,
                TextAnchor.MiddleCenter, UdonMediaTheme.TextMuted);

            view.RemainingText = UdonWorldUiKit.Label(
                section, "Remaining", width - third, metaTop, third, metaHeight, metaSize,
                TextAnchor.MiddleRight, UdonMediaTheme.TextSecondary);

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

            Button more = UdonWorldUiKit.RoundedButton(
                section, "More", width - MoreWidth, 0f, MoreWidth, height, "…",
                wide ? 28 : 24, UdonMediaTheme.Surface, radius, out unused);

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
            const float SheetHeight = 80f;
            float pad = UdonMediaTheme.Space1;

            RectTransform sheet = UdonWorldUiKit.Place(
                section, "MoreSheet", 0f, height + UdonMediaTheme.Space1, width, SheetHeight);

            Image back = UdonWorldUiKit.RoundedPlate(
                sheet, "Back", 0f, 0f, width, SheetHeight,
                UdonMediaTheme.Surface, UdonMediaTheme.RadiusMedium);
            back.raycastTarget = false;

            float inner = SheetHeight - pad * 2f;
            float half = (width - pad * 3f) / 2f;

            Text unused;
            Button stop = UdonWorldUiKit.RoundedButton(
                sheet, "Stop", pad, pad, half, inner, "■ 停止",
                wide ? UdonMediaTheme.TextBody : 17,
                UdonMediaTheme.SurfaceRaised, UdonMediaTheme.RadiusSmall, out unused);

            Button clear = UdonWorldUiKit.RoundedButton(
                sheet, "ClearUpcoming", pad * 2f + half, pad, half, inner, "予定を空に",
                wide ? UdonMediaTheme.TextBody : 17,
                UdonMediaTheme.SurfaceRaised, UdonMediaTheme.RadiusSmall, out unused);

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

        private static UdonMediaTabs BuildTabbedLists(
            RectTransform body, UdonMediaPanel panel,
            float x, float y, float width, float height)
        {
            const float TabHeight = 76f;
            const float TabGap = 9f;
            const int TabCount = 6;

            RectTransform section = UdonWorldUiKit.Place(body, "Browser", x, y, width, height);

            var tabs = Add<UdonMediaTabs>(section.gameObject);
            if (tabs == null) return null;

            float tabWidth = Mathf.Floor((width - TabGap * (TabCount - 1)) / TabCount);

            var lists = new UdonMediaListView[TabCount];
            var pages = new GameObject[TabCount];
            var marks = new GameObject[TabCount];
            var tabRects = new RectTransform[TabCount];
            var labels = new Text[TabCount];

            // ── 並びは「アーティスト → 曲 → おすすめ → 再生予定」(Phase7-6)。
            //    <b>最初に開くのはアーティスト</b>です。曲を全部並べても
            //    どれを選べばよいか決められないので、まず誰を聴くかから入ります。
            int[] sources =
            {
                UdonMediaListView.SourceArtist,
                UdonMediaListView.SourceLibrary,
                UdonMediaListView.SourceFavorite,
                UdonMediaListView.SourceHistory,
                UdonMediaListView.SourceRelated,
                UdonMediaListView.SourceQueue,
            };

            string[] events =
            {
                "SelectTab0", "SelectTab1", "SelectTab2",
                "SelectTab3", "SelectTab4", "SelectTab5",
            };

            for (int i = 0; i < TabCount; i++)
            {
                float tabX = i * (tabWidth + TabGap);

                // ── タブは<b>面を持ちません</b>(Frost / Phase7-7)。
                //    塗った箱を 4 つ並べると、そこがいちばん強い模様になり、
                //    <b>中身より枠のほうが目に入ります</b>。
                //    選ばれていることは、下を滑る 1 本の線だけで示します。
                Button tab = UdonWorldUiKit.HitArea(
                    section, "Tab" + i, tabX, 0f, tabWidth, TabHeight,
                    new Color(1f, 1f, 1f, 0.001f));

                // タブは 4 本並ぶので、いちばん小さい段(2 m の下限)を使う。
                Text label = UdonWorldUiKit.Label(
                    tab.transform, "Label", 0f, 0f, tabWidth, TabHeight,
                    UdonMediaTheme.TextCaption, TextAnchor.MiddleCenter,
                    UdonMediaTheme.TextMuted);

                tabRects[i] = tab.GetComponent<RectTransform>();
                labels[i] = label;

                UdonWorldUiKit.Wire(tab, tabs, events[i], "ここを見る");

                float pageTop = TabHeight + UdonMediaTheme.Space3;
                RectTransform page = UdonWorldUiKit.Place(
                    section, "Page" + i, 0f, pageTop, width, height - pageTop);

                pages[i] = page.gameObject;

                // おすすめだけカードにする。
                // 一覧は「探している人」、カードは「探していない人」のための形。
                if (sources[i] == UdonMediaListView.SourceRelated)
                {
                    BuildRecommendationCards(page, panel, width, height - pageTop);
                }
                else
                {
                    lists[i] = BuildList(page, sources[i], width, height - pageTop);
                }

                if (NeedsCompile) return tabs;
            }

            // ── 選ばれているタブの下を滑る 1 本の線。
            //    ぱっと点け消しすると「どこからどこへ移ったか」が残りません。
            //    線が滑れば、移動そのものが目に入ります。
            Image indicator = UdonWorldUiKit.RoundedPlate(
                section, "Indicator", 0f, TabHeight - 5f, tabWidth * 0.5f, 5f,
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
            tabs.FixedLabels = new[] { "", "", "", "", "おすすめ", "" };

            // ── アーティストを選んだら曲のタブへ移る(Phase7-6)。
            UdonMediaListView artistList = lists[0];
            UdonMediaListView songList = lists[1];   // 並びは sources と同じ

            if (artistList != null)
            {
                artistList.SongList = songList;
                artistList.Tabs = tabs;
                artistList.SongTabIndex = 1;
            }

            // 曲のタブは、選ばれたアーティストのぶんだけ出す。
            // まとめ直す必要はないので、チャンネルごとのまとめは切っておく。
            if (songList != null) songList.GroupByChannel = false;

            tabs.Selected = 0;

            tabs.SelectedColor = UdonMediaTheme.TextPrimary;
            tabs.NormalColor = UdonMediaTheme.TextMuted;

            panel.Tabs = tabs;
            panel.Lists = lists;

            return tabs;
        }

        /// <summary>
        /// <b>おすすめのカード。</b>Phase7-3。2 列 × 2 段。
        ///
        /// <b>絵を大きく、文字を少なく。</b>知らない曲を勧めるので、
        /// 名前を読ませても伝わりません。<b>絵と理由の一言</b>で決めてもらいます。
        /// </summary>
        private static void BuildRecommendationCards(
            RectTransform page, UdonMediaPanel panel, float width, float height)
        {
            // 3 × 2 = 6 枚。4 枚だと「選んだ」感じがせず、
            // 押さなかったときに次の手が無くなる。
            const int Columns = 3;
            const int Rows = 2;
            const int Count = Columns * Rows;

            var view = Add<UdonRecommendationCards>(page.gameObject);
            if (view == null) return;

            float gap = UdonMediaTheme.Space2;
            float cardW = Mathf.Floor((width - gap * (Columns - 1)) / Columns);

            // 絵は 16:9。カードの高さは<b>中身から決めます</b> ——
            // 余っている高さで割ると、絵の下に意味のない空白が生まれ、
            // 「作りかけ」に見えます。
            float artH = Mathf.Round(cardW * 9f / 16f);
            float cardH = artH + UdonMediaTheme.Space1 + 30f + 26f
                          + UdonMediaTheme.Space1 + 32f + UdonMediaTheme.Space2;

            float available = Mathf.Floor((height - gap * (Rows - 1) - 60f) / Rows);
            if (cardH > available) cardH = available;

            var cards = new GameObject[Count];
            var artworks = new Image[Count];
            var fallbacks = new Text[Count];
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
                    page, "Card" + i, cx, cy, cardW, cardH, UdonMediaTheme.Surface);
                UdonWorldUiKit.ApplyRadius(
                    card.targetGraphic as Image, UdonMediaTheme.RadiusLarge);
                UdonWorldUiKit.AddGlassEdge(
                    card.targetGraphic as Image, cardW, UdonMediaTheme.RadiusLarge);

                cards[i] = card.gameObject;

                Image art = UdonWorldUiKit.RoundedPlate(
                    card.transform, "Artwork", 0f, 0f, cardW, artH,
                    UdonMediaTheme.SurfaceHover, UdonMediaTheme.RadiusLarge);
                art.raycastTarget = false;
                art.preserveAspect = true;
                artworks[i] = art;

                fallbacks[i] = UdonWorldUiKit.Label(
                    art.transform, "Fallback", 0f, 0f, cardW, artH, 34,
                    TextAnchor.MiddleCenter, UdonMediaTheme.TextMuted);

                float textY = artH + UdonMediaTheme.Space1;
                float textW = cardW - UdonMediaTheme.Space2 * 2f;

                titles[i] = UdonWorldUiKit.FittedLabel(
                    card.transform, "Title", UdonMediaTheme.Space2, textY, textW, 30f,
                    UdonMediaTheme.TextBody, 12, TextAnchor.UpperLeft, UdonMediaTheme.TextPrimary);

                artists[i] = UdonWorldUiKit.FittedLabel(
                    card.transform, "Artist", UdonMediaTheme.Space2, textY + 30f, textW, 24f,
                    UdonMediaTheme.TextCaption, 11, TextAnchor.UpperLeft,
                    UdonMediaTheme.TextSecondary);

                // ── 理由。強調色の札にして、いちばん下に置く。
                //    「なぜ勧めるか」が言えないおすすめは押されません。
                Image chip = UdonWorldUiKit.RoundedPlate(
                    card.transform, "ReasonChip", UdonMediaTheme.Space2,
                    cardH - 36f - UdonMediaTheme.Space1, textW, 32f,
                    UdonMediaTheme.AccentWash, UdonMediaTheme.RadiusSmall);
                chip.raycastTarget = false;

                reasons[i] = UdonWorldUiKit.Label(
                    chip.transform, "Reason", UdonMediaTheme.Space1, 0f,
                    textW - UdonMediaTheme.Space2, 32f, 15,
                    TextAnchor.MiddleLeft, UdonMediaTheme.Accent);

                // ── 「＋」= 再生予定へ。一覧と同じ形・同じ位置に置く。
                //    カードを押すとすぐ流れてしまうので、
                //    「いまは流さず覚えておく」道が要ります。
                const float PlusSize = 52f;

                Text plusLabel;
                Button plus = UdonWorldUiKit.RoundedButton(
                    card.transform, "Queue",
                    cardW - PlusSize - UdonMediaTheme.Space1,
                    artH - PlusSize - UdonMediaTheme.Space1,
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
            view.Artworks = artworks;
            view.ArtworkFallbacks = fallbacks;
            view.Titles = titles;
            view.Artists = artists;
            view.Reasons = reasons;
            view.PressedMarkers = pressed;
            view.EmptyMessage = empty.gameObject;
            view.EmptyText = empty;

            panel.Cards = view;
        }

        private static UdonMediaListView BuildList(
            RectTransform page, int source, float pageWidth, float height)
        {
            const float RowHeight = 112f;
            const float RowGap = 10f;
            const float FooterHeight = 60f;
            const int RowCount = 5;

            // つまみのぶんだけ内側へ寄せる。重ねると「＋」が押せなくなる。
            const float ScrollBarLane = 22f + UdonMediaTheme.Space1;
            float width = pageWidth - ScrollBarLane;

            var view = Add<UdonMediaListView>(page.gameObject);
            if (view == null) return null;

            view.Source = source;
            view.HeaderLabel = "";
            view.ScrollStep = 0;
            view.FollowNowPlaying = source == UdonMediaListView.SourceQueue;
            view.GroupByChannel = source == UdonMediaListView.SourceLibrary;

            // ── お気に入りの並べ替え(Phase7-8)
            float listTop = 0f;
            if (source == UdonMediaListView.SourceFavorite)
            {
                const float SortHeight = 52f;

                Text sortLabel;
                Button sort = UdonWorldUiKit.RoundedButton(
                    page, "Sort", 0f, 0f, width, SortHeight, "追加が新しい順",
                    UdonMediaTheme.TextCaption, UdonMediaTheme.Surface,
                    UdonMediaTheme.RadiusMedium, out sortLabel);

                view.SortLabel = sortLabel;
                UdonWorldUiKit.Wire(sort, view, "CycleSort", "並べ替え");

                listTop = SortHeight + UdonMediaTheme.Space2;
            }

            // ── 検索バー(「すべての曲」だけ)
            if (source == UdonMediaListView.SourceLibrary)
            {
                listTop = BuildSearchBar(page, view, width) + UdonMediaTheme.Space2;

                // ── いま見ているチャンネルを上に貼り付けておく。
                //    見出しは一覧の中にあるので 1 行スクロールで消えてしまい、
                //    「別のアーティストへ移りたいだけなのに、いちばん上まで戻る」
                //    ことになっていました。
                const float StickyHeight = 44f;

                Image sticky = UdonWorldUiKit.GlassPlate(
                    page, "StickyChannel", 0f, listTop, width, StickyHeight,
                    UdonMediaTheme.SurfaceRaised, UdonMediaTheme.RadiusMedium);
                sticky.raycastTarget = false;

                Text stickyText = UdonWorldUiKit.Label(
                    sticky.transform, "Text", UdonMediaTheme.Space2, 0f,
                    width - UdonMediaTheme.Space4, StickyHeight,
                    UdonMediaTheme.TextCaption, TextAnchor.MiddleLeft,
                    UdonMediaTheme.TextSecondary);

                view.StickyChannelBand = sticky.gameObject;
                view.StickyChannelText = stickyText;

                listTop += StickyHeight + UdonMediaTheme.Space1;
            }

            // ── 何も無いときの案内。種類ごとに文が変わる。
            Text empty = UdonWorldUiKit.Label(
                page, "Empty", 0f, listTop + RowHeight * 0.5f, width, 44f,
                UdonMediaTheme.TextBody, TextAnchor.MiddleCenter, UdonMediaTheme.TextMuted);

            view.EmptyMessage = empty.gameObject;
            view.EmptyText = empty;

            // ── 行
            int rowCount = source == UdonMediaListView.SourceLibrary ? RowCount - 1 : RowCount;

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
            float listHeight = footerYOf(height) - listTop - UdonMediaTheme.Space1;
            BuildScroller(page, view, pageWidth, listTop, listHeight,
                          RowHeight + RowGap, rowCount);
            if (NeedsCompile) return view;

            // ── 下の帯:何件目を見ているか + スクロール
            float footerY = footerYOf(height);

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

        private static float footerYOf(float height)
        {
            return height - 60f;
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

            var field = fieldRect.gameObject.AddComponent<InputField>();

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

            UdonWorldUiKit.Bind(field, view, "OnSearchChanged");
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
        /// 1 行。<b>絵 + 曲名 + チャンネル · ジャンル + 長さ + 「予定へ」</b>。
        ///
        /// <b>文字は 2 段だけ</b>にしてあります。3 段にすると 1 行あたりが小さくなり、
        /// 2 m 先で読めなくなります。
        /// </summary>
        private static UdonMediaListRow BuildRow(
            RectTransform parent, string name, float y, float width, float height,
            int source, int index)
        {
            const float BarWidth = 4f;
            const float SecondaryWidth = 76f;

            // 絵を大きくする。一覧で最初に目に入るのは文字ではなく絵なので、
            // ここが小さいと「どれがどれか」を文字で読ませることになります。
            // サムネイルは<b>行の高さいっぱい</b>まで使う。
            // Frost では絵が主役なので、余白を削ってでも絵を大きく取ります。
            float artHeight = height - UdonMediaTheme.Space1 * 1.5f;
            float artWidth = Mathf.Round(artHeight * 16f / 9f);

            bool queue = source == UdonMediaListView.SourceQueue;

            RectTransform rowRect = UdonWorldUiKit.Place(parent, name, 0f, y, width, height);

            var row = Add<UdonMediaListRow>(rowRect.gameObject);
            if (row == null) return null;

            // 中身は「行そのもの」ではなく子に置く。
            // 空行で非アクティブにする対象が UdonBehaviour 本体だと、
            // 二度と書き戻せなくなるため。
            RectTransform content = UdonWorldUiKit.Place(rowRect, "Content", 0f, 0f, width, height);

            // ── 行の地は「無い」に近い色にする。
            //    Phase7-2 までは 1 行おきに濃さを変えていましたが、
            //    縞模様は中身より先に目に入ります。区切りは余白に任せて、
            //    <b>触れたときだけ</b>薄く浮かせるほうが速く読めます。
            float hitX = BarWidth + UdonMediaTheme.Space1;
            // ♥ と「＋」の 2 つぶん、行の中身を詰める。
            float hitWidth = width - hitX - SecondaryWidth * 2f - UdonMediaTheme.Space1 * 2f;

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

            // ── 絵
            float artY = (height - artHeight) * 0.5f;
            Image artwork = UdonWorldUiKit.RoundedPlate(
                hit.transform, "Artwork", UdonMediaTheme.Space1, artY, artWidth, artHeight,
                UdonMediaTheme.SurfaceHover, UdonMediaTheme.RadiusMedium);
            artwork.raycastTarget = false;
            artwork.preserveAspect = true;

            Text artworkFallback = UdonWorldUiKit.Label(
                artwork.transform, "Fallback", 0f, 0f, artWidth, artHeight, 22,
                TextAnchor.MiddleCenter, UdonMediaTheme.TextMuted);

            row.Artwork = artwork;
            row.ArtworkFallbackText = artworkFallback;

            // ── 動く 3 本の棒。絵の左下に重ねる(音が出ている行だけ動く)。
            row.EqualizerBars = BuildEqualizer(artwork.transform, 12f, artHeight - 30f);

            // ── 文字
            float textX = UdonMediaTheme.Space1 + artWidth + UdonMediaTheme.Space2;
            float durationWidth = 86f;
            float textWidth = hitWidth - textX - durationWidth - UdonMediaTheme.Space2;

            // 曲名は長さがまちまちなので、入り切るまで小さくする。
            // 途中で切れた名前は、小さい名前より役に立たない。
            row.TitleText = UdonWorldUiKit.FittedLabel(
                hit.transform, "Title", textX, height * 0.14f, textWidth, height * 0.38f,
                UdonMediaTheme.TextBody, 13, TextAnchor.LowerLeft, UdonMediaTheme.TextPrimary);

            row.SubText = UdonWorldUiKit.FittedLabel(
                hit.transform, "Sub", textX, height * 0.54f, textWidth, height * 0.28f,
                14, 11, TextAnchor.UpperLeft, UdonMediaTheme.TextSecondary);

            row.DurationText = UdonWorldUiKit.Label(
                hit.transform, "Duration", hitWidth - durationWidth - UdonMediaTheme.Space1, 0f,
                durationWidth, height, UdonMediaTheme.TextCaption,
                TextAnchor.MiddleRight, UdonMediaTheme.TextMuted);

            row.IndexText = UdonWorldUiKit.Label(
                hit.transform, "Index", UdonMediaTheme.Space1, artY - 4f, 32f, 28f,
                UdonMediaTheme.TextCaption, TextAnchor.MiddleCenter, UdonMediaTheme.Accent);

            // ── 2 つめのボタン。記号だけにして、意味は「使う」の案内に任せる。
            Text secondaryLabel;
            Button secondaryButton = UdonWorldUiKit.RoundedButton(
                content, "Secondary", width - SecondaryWidth, (height - SecondaryWidth) * 0.5f,
                SecondaryWidth, SecondaryWidth, queue ? "×" : "＋", 32,
                UdonMediaTheme.SurfaceRaised,
                Mathf.RoundToInt(SecondaryWidth * 0.5f), out secondaryLabel);

            row.SecondaryLabel = secondaryLabel;

            // ── ♥(Phase7-8)。「好き」と「いま聴く」は別なので、別のボタンにする。
            //    形(♥ / ♡)ではなく<b>色</b>で入っているかを示します —— 組み込み
            //    フォントに ♡ が無い環境で豆腐(□)になるのを避けるためです。
            Text favoriteLabel;
            Button favoriteButton = UdonWorldUiKit.RoundedButton(
                content, "Favorite",
                width - SecondaryWidth * 2f - UdonMediaTheme.Space1,
                (height - SecondaryWidth) * 0.5f,
                SecondaryWidth, SecondaryWidth, "♥", 30,
                UdonMediaTheme.Surface,
                Mathf.RoundToInt(SecondaryWidth * 0.5f), out favoriteLabel);

            row.FavoriteButton = favoriteButton.gameObject;
            row.FavoriteLabel = favoriteLabel;
            row.FavoriteOnColor = UdonMediaTheme.Accent;
            row.FavoriteOffColor = UdonMediaTheme.TextMuted;

            UdonWorldUiKit.Wire(favoriteButton, row, "ClickFavorite", "お気に入り");

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
                rowRect, "NowPlayingBar", 0f, UdonMediaTheme.Space2, BarWidth,
                height - UdonMediaTheme.Space2 * 2f, UdonMediaTheme.Accent, 2);
            nowPlayingBar.raycastTarget = false;
            nowPlayingBar.gameObject.SetActive(false);

            // ── チャンネルの見出しとして使うときの帯。
            //    同じ行を曲としても見出しとしても使うので、重ねて置いて出し分ける。
            //    アーティスト一覧は<b>見出しだけで出来ています</b>。
            //    ここを通さないと 1 行も見えません。
            if (source == UdonMediaListView.SourceLibrary
                || source == UdonMediaListView.SourceArtist)
            {
                BuildHeaderBand(rowRect, row, hit, width, height);
            }

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
        /// <b>チャンネルの見出しの帯。</b>Phase7-2。
        ///
        /// 曲の行に<b>重ねて</b>置いて、出し分けます。見出し専用の行を別に持つと
        /// <b>「見出しが何個要るか」を先に決めないと行を用意できません</b>が、
        /// 検索でチャンネルが減れば見出しも減るので、それは決められません。
        /// </summary>
        private static void BuildHeaderBand(
            RectTransform rowRect, UdonMediaListRow row, Button hit, float width, float height)
        {
            // 見出しは曲より低くする。曲と同じ高さだと、どちらが親か分からない。
            const float BandHeight = 64f;

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
