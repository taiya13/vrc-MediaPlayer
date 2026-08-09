using System;
using System.Collections.Generic;

namespace SmartMediaPlatform.CatalogBuilder.Genres
{
    /// <summary>
    /// <b>ジャンルを言い当てる。</b>Phase6-6。
    ///
    /// <b>なぜ要るのか</b><br/>
    /// YouTube のカテゴリは「音楽」しか返しません。曲を 200 本入れると
    /// <b>200 本すべてが「音楽」</b>になり、絞り込みにも「おすすめ」にも使えません。
    /// タイトル・説明・タグ・チャンネル名から<b>こちらで決めます</b>。
    ///
    /// <b>やり方は点取りです。</b>
    /// <list type="number">
    /// <item>材料(タイトル / タグ / チャンネル / 説明)を小文字にそろえる</item>
    /// <item>辞書の言葉が入っていたら <b>その欄の重み × その言葉の重み</b> を足す</item>
    /// <item>取り込み元のカテゴリで<b>すでに点が入っているジャンルだけ</b>を少し押す</item>
    /// <item>いちばん高いジャンルを返す。<see cref="MinimumScore"/> に届かなければ「その他」</item>
    /// </list>
    ///
    /// <b>同じ言葉は 1 回しか数えません。</b>回数で数えると、
    /// 説明欄に同じ単語を並べただけの動画が上位に来てしまいます。
    /// <b>「あるか無いか」だけ</b>を見るので、結果が読みやすく、テストもしやすくなります。
    ///
    /// <b>Unity にも取り込み元にも依存しません。</b>
    /// Importer がするのは<b>材料を詰めて呼ぶだけ</b>です。
    ///
    /// <b>あとから賢くできます。</b><see cref="AddHintProvider"/> で
    /// MusicBrainz のような外部データベースを足せます。点の入れ方は辞書と同じなので、
    /// <b>外部が落ちていても辞書だけで動きます</b>。
    /// </summary>
    public sealed class GenreClassifier
    {
        /// <summary>決められなかったときの答え。<b>「音楽」は使いません。</b></summary>
        public const string Unknown = "その他";

        private readonly List<IGenreHintProvider> _hints = new List<IGenreHintProvider>();

        public GenreDictionary Dictionary;

        // ── どの欄を、どれだけ信じるか

        /// <summary>見出し。いちばん素直に書いてある。</summary>
        public int TitleWeight = 3;

        /// <summary>タグ。付ける人が意図して選んでいるので、見出しと同じくらい強い。</summary>
        public int TagWeight = 3;

        /// <summary>チャンネル名。曲ごとには変わらないので、見出しよりは弱く。</summary>
        public int ChannelWeight = 2;

        /// <summary>説明欄。長くて関係ない言葉も多いので、いちばん弱く。</summary>
        public int DescriptionWeight = 1;

        /// <summary>
        /// これに届かなければ「その他」。
        /// <b>説明欄に 1 語かすっただけで決めない</b>ための線引きです
        /// (説明 1 × 重み 2 = 2 点では届かず、見出し 3 × 重み 2 = 6 点なら届く)。
        /// </summary>
        public int MinimumScore = 6;

        /// <summary>そのまま使える 1 個。窓や Importer はこれを呼びます。</summary>
        public static readonly GenreClassifier Shared = new GenreClassifier();

        public GenreClassifier() : this(GenreDictionary.CreateDefault())
        {
        }

        public GenreClassifier(GenreDictionary dictionary)
        {
            Dictionary = dictionary != null ? dictionary : GenreDictionary.CreateDefault();
        }

        /// <summary>外の手がかりを足す(MusicBrainz など)。</summary>
        public void AddHintProvider(IGenreHintProvider provider)
        {
            if (provider == null) return;
            if (_hints.Contains(provider)) return;

            _hints.Add(provider);
        }

        public void ClearHintProviders()
        {
            _hints.Clear();
        }

        public int HintProviderCount { get { return _hints.Count; } }

        // ───────── 判定 ─────────

        /// <summary>いちばんそれらしいジャンル。分からなければ <see cref="Unknown"/>。</summary>
        public string Classify(GenreSignals signals)
        {
            GenreScore[] ranked = Rank(signals);

            if (ranked.Length == 0) return Unknown;
            if (ranked[0].Score < MinimumScore) return Unknown;

            return ranked[0].Genre;
        }

        /// <summary>
        /// 点の高い順に全部返す。
        /// <b>「なぜそうなったか」を見せるため</b>と、
        /// 2 位以下をタグに使えるようにするために開けてあります。
        /// </summary>
        public GenreScore[] Rank(GenreSignals signals)
        {
            if (signals == null) return new GenreScore[0];

            string title = Normalize(signals.Title);
            string channel = Normalize(signals.ChannelName);
            string description = Normalize(signals.Description);
            string tags = NormalizeTags(signals.Tags);

            var scores = new List<GenreScore>();

            for (int i = 0; i < Dictionary.RuleCount; i++)
            {
                GenreRule rule = Dictionary.Rules[i];

                var reasons = new List<string>();
                int score = 0;

                score += ScoreField(rule, title, TitleWeight, "見出し", reasons);
                score += ScoreField(rule, tags, TagWeight, "タグ", reasons);
                score += ScoreField(rule, channel, ChannelWeight, "チャンネル", reasons);
                score += ScoreField(rule, description, DescriptionWeight, "説明", reasons);

                if (score <= 0) continue;

                var entry = new GenreScore();
                entry.Genre = rule.Genre;
                entry.Score = score;
                entry.Reasons = reasons.ToArray();

                scores.Add(entry);
            }

            ApplyCategoryBias(signals, scores);
            ApplyHints(signals, scores);

            Sort(scores);
            return scores.ToArray();
        }

        /// <summary>1 位の理由を 1 行で。窓の説明に出します。</summary>
        public string Explain(GenreSignals signals)
        {
            GenreScore[] ranked = Rank(signals);

            if (ranked.Length == 0) return "手がかりがありませんでした。";

            if (ranked[0].Score < MinimumScore)
            {
                return "手がかりが弱すぎました(" + ranked[0].Genre + " " + ranked[0].Score
                       + " 点 / " + MinimumScore + " 点必要)。";
            }

            return ranked[0].Genre + " " + ranked[0].Score + " 点 — "
                   + string.Join(", ", ranked[0].Reasons);
        }

        // ───────── 内部 ─────────

        /// <summary>1 つの欄を見て点を出す。<b>同じ言葉は 1 回だけ</b>数えます。</summary>
        private static int ScoreField(
            GenreRule rule, string haystack, int fieldWeight, string label, List<string> reasons)
        {
            if (haystack.Length == 0 || fieldWeight <= 0) return 0;

            int score = 0;

            for (int i = 0; i < rule.Keywords.Count; i++)
            {
                GenreKeyword keyword = rule.Keywords[i];
                if (!Contains(haystack, keyword)) continue;

                score += fieldWeight * keyword.Weight;
                reasons.Add(label + ":" + keyword.Text);
            }

            return score;
        }

        /// <summary>
        /// 取り込み元のカテゴリで押す。
        /// <b>すでに点が入っているジャンルにしか足しません。</b>
        /// 「ゲーム」カテゴリの動画が全部 Game Music になるのを防ぐためです。
        /// </summary>
        private void ApplyCategoryBias(GenreSignals signals, List<GenreScore> scores)
        {
            if (string.IsNullOrWhiteSpace(signals.SourceCategory)) return;

            string category = signals.SourceCategory.Trim();

            for (int i = 0; i < Dictionary.Biases.Count; i++)
            {
                GenreDictionary.CategoryBias bias = Dictionary.Biases[i];
                if (bias.Category != category) continue;

                GenreScore found = FindScore(scores, bias.Genre);
                if (found == null) continue;

                found.Score += bias.Bonus;
                found.Reasons = Append(found.Reasons, "カテゴリ:" + category);
            }
        }

        /// <summary>
        /// 外の手がかりを混ぜる。
        /// <b>辞書に無かったジャンルも足せます</b>(外部 DB のほうが詳しいことがあるため)。
        /// </summary>
        private void ApplyHints(GenreSignals signals, List<GenreScore> scores)
        {
            for (int i = 0; i < _hints.Count; i++)
            {
                IGenreHintProvider provider = _hints[i];
                if (provider == null || !provider.IsAvailable) continue;

                GenreHint[] suggested = provider.Suggest(signals);
                if (suggested == null) continue;

                for (int h = 0; h < suggested.Length; h++)
                {
                    GenreHint hint = suggested[h];
                    if (hint == null || string.IsNullOrWhiteSpace(hint.Genre)) continue;
                    if (hint.Score <= 0) continue;

                    string reason = provider.Name + ":"
                                    + (string.IsNullOrEmpty(hint.Reason) ? hint.Genre : hint.Reason);

                    GenreScore found = FindScore(scores, hint.Genre);
                    if (found != null)
                    {
                        found.Score += hint.Score;
                        found.Reasons = Append(found.Reasons, reason);
                        continue;
                    }

                    var entry = new GenreScore();
                    entry.Genre = hint.Genre;
                    entry.Score = hint.Score;
                    entry.Reasons = new[] { reason };

                    scores.Add(entry);
                }
            }
        }

        /// <summary>
        /// 点の高い順。<b>同じ点なら辞書の並び順</b>にします
        /// (House を EDM より先に置いてあるので、両方当たれば House になる)。
        /// </summary>
        private void Sort(List<GenreScore> scores)
        {
            // 挿入ソート。数十件が相手で、同点なら元の順(= 辞書順)を保てる。
            for (int i = 1; i < scores.Count; i++)
            {
                GenreScore moving = scores[i];
                int at = i - 1;

                while (at >= 0 && scores[at].Score < moving.Score)
                {
                    scores[at + 1] = scores[at];
                    at--;
                }
                scores[at + 1] = moving;
            }
        }

        private static GenreScore FindScore(List<GenreScore> scores, string genre)
        {
            for (int i = 0; i < scores.Count; i++)
            {
                if (scores[i].Genre == genre) return scores[i];
            }
            return null;
        }

        private static string[] Append(string[] values, string added)
        {
            if (values == null) return new[] { added };

            var grown = new string[values.Length + 1];
            for (int i = 0; i < values.Length; i++) grown[i] = values[i];

            grown[values.Length] = added;
            return grown;
        }

        // ───────── 文字を合わせる ─────────

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.ToLowerInvariant();
        }

        /// <summary>タグをひとつなぎにする。区切りを挟んで、隣同士がくっつかないようにする。</summary>
        private static string NormalizeTags(string[] tags)
        {
            if (tags == null || tags.Length == 0) return "";

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < tags.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(tags[i])) continue;

                sb.Append(' ').Append(tags[i].ToLowerInvariant()).Append(' ');
            }
            return sb.ToString();
        }

        /// <summary>
        /// その言葉が入っているか。
        /// <see cref="GenreKeyword.WholeWord"/> なら<b>前後が英数字でないこと</b>も見ます。
        /// </summary>
        public static bool Contains(string haystack, GenreKeyword keyword)
        {
            if (keyword == null || keyword.Text.Length == 0) return false;
            if (string.IsNullOrEmpty(haystack)) return false;

            int at = 0;
            while (at <= haystack.Length - keyword.Text.Length)
            {
                int hit = haystack.IndexOf(keyword.Text, at, StringComparison.Ordinal);
                if (hit < 0) return false;

                if (!keyword.WholeWord || IsBounded(haystack, hit, keyword.Text.Length))
                {
                    return true;
                }

                at = hit + 1;
            }
            return false;
        }

        private static bool IsBounded(string text, int start, int length)
        {
            if (start > 0 && IsWordChar(text[start - 1])) return false;

            int end = start + length;
            if (end < text.Length && IsWordChar(text[end])) return false;

            return true;
        }

        /// <summary>
        /// 区切りにならない文字か。<b>ASCII の英数字だけ</b>を「続きの文字」とみなします。
        /// 日本語を続きとみなすと、「アニメrock」のような並びで当たらなくなります。
        /// </summary>
        private static bool IsWordChar(char c)
        {
            if (c > 127) return false;

            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        }
    }
}
