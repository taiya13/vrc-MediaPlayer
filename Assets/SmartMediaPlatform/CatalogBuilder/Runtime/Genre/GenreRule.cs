using System.Collections.Generic;

namespace SmartMediaPlatform.CatalogBuilder.Genres
{
    /// <summary>
    /// <b>ジャンルを言い当てる手がかり 1 つ。</b>Phase6-6。
    ///
    /// <b>言葉と重みの組</b>です。「vocaloid」のように<b>それだけで決まる</b>言葉もあれば、
    /// 「mix」のように<b>他と合わさって初めて効く</b>言葉もあるので、1 つずつ強さを持たせます。
    /// </summary>
    public sealed class GenreKeyword
    {
        /// <summary>探す言葉。<b>小文字でそろえて持ちます</b>(比べるときも小文字にする)。</summary>
        public readonly string Text;

        /// <summary>この言葉 1 つぶんの強さ。1(かすり) 〜 5(ほぼ確定)。</summary>
        public readonly int Weight;

        /// <summary>
        /// <b>前後が英数字なら当てないか。</b>
        ///
        /// <b>これが無いと誤爆します。</b>
        /// <list type="bullet">
        /// <item><c>cover</c> が <c>dis<b>cover</b>y</c> に当たる</item>
        /// <item><c>live</c> が <c>de<b>live</b>ry</c> や <c>o<b>live</b></c> に当たる</item>
        /// <item><c>rock</c> が <c><b>rock</b>et</c> に当たる</item>
        /// <item><c>epic</c> が <c><b>epic</b>enter</c> に当たる</item>
        /// </list>
        ///
        /// <b>日本語には効かせません。</b>「ロック」の前後に区切りは無いのが普通で、
        /// 区切りを求めると<b>ほとんど当たらなくなります</b>。
        /// 英数字だけの言葉なら自動で true、そうでなければ false になります。
        /// </summary>
        public readonly bool WholeWord;

        public GenreKeyword(string text, int weight)
            : this(text, weight, IsAsciiWord(text))
        {
        }

        public GenreKeyword(string text, int weight, bool wholeWord)
        {
            Text = text == null ? "" : text.Trim().ToLowerInvariant();
            Weight = weight < 1 ? 1 : weight;
            WholeWord = wholeWord;
        }

        /// <summary>英数字と空白・記号だけでできているか(= 区切りを見るべきか)。</summary>
        public static bool IsAsciiWord(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            bool hasLetter = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c > 127) return false;   // 日本語などが混ざっていたら区切りを見ない

                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    hasLetter = true;
                }
            }
            return hasLetter;
        }

        public override string ToString()
        {
            return Text + "(" + Weight + ")";
        }
    }

    /// <summary>
    /// <b>ジャンル 1 つぶんの決まり。</b>Phase6-6。
    ///
    /// <b>ジャンルごとに 1 つ</b>あり、そのジャンルらしい言葉を並べて持ちます。
    /// キーワードを足したいときは<b>この 1 か所に足すだけ</b>で、
    /// 判定の仕組み(<see cref="GenreClassifier"/>)には手を触れません。
    /// </summary>
    public sealed class GenreRule
    {
        private readonly List<GenreKeyword> _keywords = new List<GenreKeyword>();

        /// <summary>付けるジャンル名。そのまま <c>CatalogDraftItem.Genre</c> に入ります。</summary>
        public readonly string Genre;

        public GenreRule(string genre)
        {
            Genre = genre == null ? "" : genre.Trim();
        }

        public IReadOnlyList<GenreKeyword> Keywords { get { return _keywords; } }

        public int KeywordCount { get { return _keywords.Count; } }

        /// <summary>同じ強さの言葉をまとめて足す。辞書を読みやすくするための入口。</summary>
        public GenreRule Add(int weight, params string[] words)
        {
            if (words == null) return this;

            for (int i = 0; i < words.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(words[i])) continue;

                var keyword = new GenreKeyword(words[i], weight);

                // 同じ言葉を 2 度入れると点が二重に入る。強いほうを残す。
                int already = IndexOf(keyword.Text);
                if (already >= 0)
                {
                    if (_keywords[already].Weight >= keyword.Weight) continue;
                    _keywords[already] = keyword;
                    continue;
                }

                _keywords.Add(keyword);
            }
            return this;
        }

        /// <summary>区切りを見るかどうかを自分で決めて足す(短い英単語の逃げ道)。</summary>
        public GenreRule AddRaw(string word, int weight, bool wholeWord)
        {
            if (string.IsNullOrWhiteSpace(word)) return this;

            var keyword = new GenreKeyword(word, weight, wholeWord);

            int already = IndexOf(keyword.Text);
            if (already >= 0) _keywords[already] = keyword;
            else _keywords.Add(keyword);

            return this;
        }

        public bool Remove(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return false;

            int at = IndexOf(word.Trim().ToLowerInvariant());
            if (at < 0) return false;

            _keywords.RemoveAt(at);
            return true;
        }

        private int IndexOf(string text)
        {
            for (int i = 0; i < _keywords.Count; i++)
            {
                if (_keywords[i].Text == text) return i;
            }
            return -1;
        }

        public override string ToString()
        {
            return Genre + " (" + _keywords.Count + " 語)";
        }
    }
}
