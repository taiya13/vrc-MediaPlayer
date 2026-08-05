namespace SmartMediaPlatform.CatalogBuilder.YouTube
{
    /// <summary>
    /// <b>ショート動画かどうかを見分ける。</b>Phase7-3。
    ///
    /// <b>なぜ取り込みの時点で外すのか</b><br/>
    /// ショートは 1 分前後で終わるうえ縦長です。ワールドの大画面に出すと
    /// <b>左右が真っ黒のまま、すぐ次へ飛ぶ</b>ので、
    /// チャンネルをまるごと取り込むと再生がほぼ成立しません。
    /// あとから 1 本ずつ消すのは現実的でないので、<b>入り口で外します</b>。
    ///
    /// <b>API は「これはショートです」と教えてくれません。</b>
    /// YouTube Data API v3 にショートを表す項目は無く、
    /// 公式に案内されている見分け方もありません。
    /// だから<b>状況証拠を複数あわせて</b>判断します。
    ///
    /// <b>仕様変更に強くするための決まりごと</b>:
    /// <list type="number">
    /// <item><b>手がかりは 1 つずつ別のメソッドにする。</b>
    ///       YouTube 側が変わったら、そのメソッドだけ直します</item>
    /// <item><b>長さだけでは決めない。</b>ショートの上限は 60 秒でしたが
    ///       2024 年に 3 分へ延びました。<b>数字は必ず変わる</b>ものとして、
    ///       <see cref="MaxShortSeconds"/> は外から変えられるようにしてあります</item>
    /// <item><b>長さが分からないもの(0 秒)は外さない。</b>
    ///       取れなかっただけかもしれないのに消すと、
    ///       <b>ふつうの動画が黙って消える</b>という一番困る壊れ方をします</item>
    /// <item><b>なぜ外したかを必ず言葉で残す</b>(<see cref="ReasonFor"/>)。
    ///       「入れたはずの曲が無い」を起こさないためです</item>
    /// </list>
    /// </summary>
    public static class YouTubeShortsFilter
    {
        /// <summary>
        /// これ以下の長さならショートとみなす(秒)。
        ///
        /// <b>60 にしてあります。</b>ショートの上限は 3 分まで延びましたが、
        /// そこまで上げると<b>ふつうの短い曲</b>(イントロ・SE・1 分半の曲)まで
        /// 巻き込みます。3 分の長いショートは
        /// <see cref="HasShortsMark"/>(#shorts の印)のほうで拾います。
        /// </summary>
        public static int MaxShortSeconds = 60;

        /// <summary>ショートか。</summary>
        public static bool IsShort(YouTubeVideoInfo video)
        {
            return ReasonFor(video).Length > 0;
        }

        /// <summary>
        /// ショートだと判断した理由。ショートでなければ空文字。
        /// <b>人が読んで納得できる言葉</b>にします(取り込み結果に出すため)。
        /// </summary>
        public static string ReasonFor(YouTubeVideoInfo video)
        {
            if (video == null) return "";

            // ① 印がある。長さに関係なく決まり。
            //    3 分まで延びた長いショートはここで拾います。
            if (HasShortsMark(video)) return "#shorts の印";

            // ② 短い。長さが取れているときだけ見ます。
            if (IsShortEnough(video.DurationSeconds))
            {
                return video.DurationSeconds + " 秒(" + MaxShortSeconds + " 秒以下)";
            }

            return "";
        }

        // ───────── 手がかり ─────────

        /// <summary>
        /// <b>#shorts の印があるか。</b>投稿者が自分で付けるので、いちばん確かな手がかりです。
        /// 見出し・説明・タグのどこにあっても拾います。
        /// </summary>
        public static bool HasShortsMark(YouTubeVideoInfo video)
        {
            if (video == null) return false;

            if (ContainsMark(video.Title)) return true;
            if (ContainsMark(video.Description)) return true;

            if (video.Tags != null)
            {
                for (int i = 0; i < video.Tags.Length; i++)
                {
                    if (ContainsMark(video.Tags[i])) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// <b>ショートとみなす長さか。</b>
        /// 0 以下(= 長さが取れなかった)は<b>false</b> —
        /// 分からないものを消さないためです。
        /// </summary>
        public static bool IsShortEnough(int durationSeconds)
        {
            if (durationSeconds <= 0) return false;
            return durationSeconds <= MaxShortSeconds;
        }

        /// <summary>
        /// 貼られた URL 自体がショートを指しているか。
        /// <c>youtube.com/shorts/xxxx</c> を 1 本だけ取り込むときに使います。
        ///
        /// <b>この場合は外しません。</b>人が名指しで貼ったものを黙って消すのは
        /// 「勝手に消えた」でしかないためです。呼び出し側が
        /// 「これはショートです」と伝えるためだけに使います。
        /// </summary>
        public static bool LooksLikeShortsUrl(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            return input.ToLowerInvariant().Contains("/shorts/");
        }

        // ───────── 小道具 ─────────

        private static bool ContainsMark(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            string lower = text.ToLowerInvariant();

            // "#shorts" / "#short" / "#ショート" のどれか。
            // タグでは # が落ちていることがあるので、タグ 1 個まるごとが
            // "shorts" のときも拾います。
            if (lower.Contains("#shorts")) return true;
            if (lower.Contains("#short")) return true;
            if (lower.Contains("#ショート")) return true;

            return lower.Trim() == "shorts" || lower.Trim() == "ショート";
        }
    }
}
