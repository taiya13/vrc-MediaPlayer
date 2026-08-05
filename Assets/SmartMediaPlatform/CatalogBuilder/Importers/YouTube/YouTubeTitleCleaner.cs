using System.Text;

namespace SmartMediaPlatform.CatalogBuilder.YouTube
{
    /// <summary>
    /// <b>YouTube の見出しから、曲名だけを取り出す。</b>Phase7-3。
    ///
    /// <b>なぜ要るのか</b><br/>
    /// YouTube の見出しは<b>チャンネル名を頭に付けるのが慣例</b>です。
    /// <c>ONE OK ROCK - C.U.R.I.O.S.I.T.Y. [Official Video]</c> のように。
    /// ところが 1 つのチャンネルを丸ごと取り込むと、
    /// <b>全 88 曲の頭に同じ名前が並びます</b>。
    /// 一覧はチャンネルでまとめてあるので、その名前はもう見出しに出ています。
    /// 二重に出ているぶんは<b>曲名が読める幅を食っているだけ</b>です。
    ///
    /// 利用者が選ぶときに見たいのは<b>曲名</b>なので、
    /// 取り込む時点で頭のチャンネル名と、末尾の飾りを落とします。
    ///
    /// <b>迷ったら削らない</b>のが決まりです。
    /// 削りすぎて<b>空になる・意味が変わる</b>ほうが、
    /// 少し長い名前が残るより困ります。だから
    /// <list type="bullet">
    /// <item>削った結果が空になるなら、元のまま返す</item>
    /// <item>チャンネル名と<b>完全に一致する</b>頭だけ落とす(部分一致では落とさない)</item>
    /// <item>末尾の飾りは<b>決まった言い回しだけ</b>を落とす</item>
    /// </list>
    /// </summary>
    public static class YouTubeTitleCleaner
    {
        /// <summary>頭のチャンネル名と、末尾の飾りを落とした見出し。</summary>
        public static string Clean(string title, string channel)
        {
            if (string.IsNullOrEmpty(title)) return title;

            string work = title.Trim();

            work = DropLeadingChannel(work, channel);
            work = DropDecorations(work);
            work = work.Trim(' ', '　', '-', '–', '—', '|', '/', '・');

            // 削った結果、何も残らなかったなら元のまま。
            return work.Length == 0 ? title.Trim() : work;
        }

        // ───────── 頭のチャンネル名 ─────────

        /// <summary>
        /// <c>チャンネル名 - 曲名</c> の形なら、頭を落とす。
        /// 区切りは <c>-</c> <c>–</c> <c>—</c> <c>|</c> <c>:</c> <c>：</c> <c>/</c> のどれか。
        /// </summary>
        public static string DropLeadingChannel(string title, string channel)
        {
            if (string.IsNullOrEmpty(channel)) return title;

            string trimmedChannel = channel.Trim();
            if (trimmedChannel.Length == 0) return title;

            if (!StartsWithIgnoreCase(title, trimmedChannel)) return title;

            string rest = title.Substring(trimmedChannel.Length).TrimStart();
            if (rest.Length == 0) return title;

            if (!IsSeparator(rest[0])) return title;

            return rest.Substring(1).TrimStart();
        }

        private static bool IsSeparator(char c)
        {
            return c == '-' || c == '–' || c == '—' || c == '|'
                   || c == ':' || c == '：' || c == '/' || c == '・';
        }

        private static bool StartsWithIgnoreCase(string text, string head)
        {
            if (text.Length < head.Length) return false;

            for (int i = 0; i < head.Length; i++)
            {
                if (char.ToLowerInvariant(text[i]) != char.ToLowerInvariant(head[i])) return false;
            }
            return true;
        }

        // ───────── 末尾の飾り ─────────

        /// <summary>
        /// <c>[Official Music Video]</c> <c>(MV)</c> <c>【MV】</c> のような飾りを落とす。
        ///
        /// <b>中の言葉を見てから落とします。</b>括弧を無条件に消すと、
        /// <c>(Acoustic Version)</c> や <c>(feat. …)</c> —— <b>別の曲</b>だと分かる
        /// 大事な情報まで消えてしまいます。
        /// </summary>
        public static string DropDecorations(string title)
        {
            string work = title;

            // 後ろから順に、括弧のかたまりを 1 つずつ見る。
            for (int guard = 0; guard < 8; guard++)
            {
                string trimmed = work.TrimEnd();
                if (trimmed.Length == 0) break;

                char close = trimmed[trimmed.Length - 1];
                char open = OpeningOf(close);
                if (open == '\0') break;

                int start = trimmed.LastIndexOf(open);
                if (start < 0) break;

                string inside = trimmed.Substring(start + 1, trimmed.Length - start - 2);
                if (!IsDecoration(inside)) break;

                work = trimmed.Substring(0, start).TrimEnd();
            }

            return work;
        }

        private static char OpeningOf(char close)
        {
            if (close == ']') return '[';
            if (close == ')') return '(';
            if (close == '）') return '（';
            if (close == '】') return '【';
            if (close == '」') return '「';
            return '\0';
        }

        /// <summary>中身が「飾り」か。曲を見分ける言葉が入っていたら飾りではない。</summary>
        private static bool IsDecoration(string inside)
        {
            if (string.IsNullOrEmpty(inside)) return true;

            string lower = inside.Trim().ToLowerInvariant();
            if (lower.Length == 0) return true;

            // 曲を見分けるための言葉。これが入っていたら残す。
            for (int i = 0; i < Keepers.Length; i++)
            {
                if (lower.Contains(Keepers[i])) return false;
            }

            for (int i = 0; i < Decorations.Length; i++)
            {
                if (lower.Contains(Decorations[i])) return true;
            }
            return false;
        }

        /// <summary>これが入っていたら消さない(別の曲だと分かる情報)。</summary>
        private static readonly string[] Keepers =
        {
            "feat", "ft.", "remix", "acoustic", "cover", "version", "ver.",
            "inst", "live at", "アコースティック", "カバー", "リミックス",
        };

        /// <summary>これだけが入っているなら飾り。</summary>
        private static readonly string[] Decorations =
        {
            "official", "music video", "mv", "m/v", "pv", "audio", "lyric",
            "hd", "4k", "full", "video", "teaser", "trailer",
            "公式", "ミュージックビデオ", "歌詞", "字幕", "本編",
        };

        // ───────── 見出しからアーティストを拾う ─────────

        /// <summary>
        /// <c>アーティスト - 曲名</c> の形から、アーティストのほうを拾う。
        /// 形になっていなければ空文字。
        ///
        /// <b>チャンネル名より、こちらのほうが正しいことがあります。</b>
        /// 音楽レーベルのチャンネルには<b>いろいろな歌手</b>が入っているためです。
        /// </summary>
        public static string ArtistFromTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";

            for (int i = 0; i < title.Length; i++)
            {
                if (!IsSeparator(title[i])) continue;

                // 「A-B」のように前後に空白が無いものは、区切りではなく綴りの一部。
                if (i == 0 || i + 1 >= title.Length) return "";
                if (title[i - 1] != ' ' && title[i - 1] != '　') return "";

                string head = title.Substring(0, i).Trim();
                return head.Length > 0 && head.Length <= 40 ? head : "";
            }
            return "";
        }

        /// <summary>Console へ出すときの 1 行(何がどう変わったか)。</summary>
        public static string Describe(string before, string after)
        {
            if (before == after) return "";

            var sb = new StringBuilder();
            sb.Append(before).Append("  →  ").Append(after);
            return sb.ToString();
        }
    }
}
