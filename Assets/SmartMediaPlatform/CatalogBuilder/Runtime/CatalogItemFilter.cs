using System.Collections.Generic;

namespace SmartMediaPlatform.CatalogBuilder
{
    /// <summary>
    /// <b>一覧の絞り込み。</b>Phase6-4。
    ///
    /// <b>取り込み元を問いません。</b>見るのは <see cref="CatalogDraftItem"/> の
    /// 見出し・チャンネル・ジャンル・タグだけなので、
    /// YouTube でも JSON でも CSV でも<b>同じ絞り込みが効きます</b>。
    /// 取り込み元を足しても、このクラスは 1 行も変わりません。
    ///
    /// <b>Unity に依存しません。</b>窓は入力欄を出して結果を並べるだけで、
    /// 「何が引っかかるか」の判断はここに寄せてあります(EditMode で検証できます)。
    ///
    /// <b>絞り込みは元の並びを壊しません。</b><see cref="Apply"/> が返すのは
    /// <b>元の位置の番号</b>で、中身を並べ替えたコピーではありません。
    /// おかげで絞り込み中に編集しても、<b>編集先がずれません</b>
    /// (Phase6-3 で「押した行と再生する行を必ず一致させる」ためにやったのと同じ考え方です)。
    /// </summary>
    public sealed class CatalogItemFilter
    {
        /// <summary>
        /// あいまい検索。<b>空白で区切ると「全部を含む」</b>の意味になります。
        /// 見出し / チャンネル / ジャンル / タグ / ID を横断して見ます。
        /// </summary>
        public string Query = "";

        /// <summary>チャンネル(= アーティスト)の完全一致。空なら効きません。</summary>
        public string Channel = "";

        /// <summary>ジャンルの完全一致。空なら効きません。</summary>
        public string Genre = "";

        /// <summary>タグの完全一致。空なら効きません。</summary>
        public string Tag = "";

        /// <summary>作りかけ(ID・見出し・URL のどれかが欠けている)だけを出す。</summary>
        public bool OnlyIncomplete;

        /// <summary>1 つも条件が入っていないか。</summary>
        public bool IsEmpty
        {
            get
            {
                return Blank(Query) && Blank(Channel) && Blank(Genre) && Blank(Tag)
                       && !OnlyIncomplete;
            }
        }

        public void Clear()
        {
            Query = "";
            Channel = "";
            Genre = "";
            Tag = "";
            OnlyIncomplete = false;
        }

        /// <summary>いま効いている条件を 1 行で。空なら空文字。</summary>
        public string Describe()
        {
            var parts = new List<string>();

            if (!Blank(Query)) parts.Add("「" + Query.Trim() + "」");
            if (!Blank(Channel)) parts.Add("チャンネル: " + Channel);
            if (!Blank(Genre)) parts.Add("ジャンル: " + Genre);
            if (!Blank(Tag)) parts.Add("タグ: " + Tag);
            if (OnlyIncomplete) parts.Add("作りかけだけ");

            return string.Join(" / ", parts.ToArray());
        }

        // ───────── 判定 ─────────

        public bool Matches(CatalogDraftItem item)
        {
            if (item == null) return false;

            if (OnlyIncomplete && item.IsComplete) return false;

            if (!Blank(Channel) && !Same(item.Artist, Channel)) return false;
            if (!Blank(Genre) && !Same(item.Genre, Genre)) return false;
            if (!Blank(Tag) && !HasTag(item, Tag)) return false;

            return MatchesQuery(item);
        }

        /// <summary>
        /// 引っかかったものの<b>元の位置</b>を順に返す。
        /// 条件が空なら全部の位置をそのまま返します。
        /// </summary>
        public int[] Apply(IReadOnlyList<CatalogDraftItem> items)
        {
            if (items == null) return new int[0];

            var result = new List<int>();
            for (int i = 0; i < items.Count; i++)
            {
                if (Matches(items[i])) result.Add(i);
            }
            return result.ToArray();
        }

        // ───────── 選択肢を集める(窓のプルダウン用)─────────

        /// <summary>出てくるチャンネル名を、最初に出た順で。</summary>
        public static string[] CollectChannels(IReadOnlyList<CatalogDraftItem> items)
        {
            return Collect(items, 0);
        }

        public static string[] CollectGenres(IReadOnlyList<CatalogDraftItem> items)
        {
            return Collect(items, 1);
        }

        public static string[] CollectTags(IReadOnlyList<CatalogDraftItem> items)
        {
            if (items == null) return new string[0];

            var found = new List<string>();
            for (int i = 0; i < items.Count; i++)
            {
                CatalogDraftItem item = items[i];
                if (item == null || item.Tags == null) continue;

                for (int t = 0; t < item.Tags.Length; t++)
                {
                    string tag = item.Tags[t];
                    if (Blank(tag)) continue;

                    tag = tag.Trim();
                    if (!found.Contains(tag)) found.Add(tag);
                }
            }
            return found.ToArray();
        }

        // ───────── 内部 ─────────

        private static string[] Collect(IReadOnlyList<CatalogDraftItem> items, int field)
        {
            if (items == null) return new string[0];

            var found = new List<string>();
            for (int i = 0; i < items.Count; i++)
            {
                CatalogDraftItem item = items[i];
                if (item == null) continue;

                string value = field == 0 ? item.Artist : item.Genre;
                if (Blank(value)) continue;

                value = value.Trim();
                if (!found.Contains(value)) found.Add(value);
            }
            return found.ToArray();
        }

        /// <summary>
        /// あいまい検索。<b>空白区切りは AND</b> です
        /// (「ボカロ 2023」で、ボカロも 2023 も含むものだけ)。
        /// </summary>
        private bool MatchesQuery(CatalogDraftItem item)
        {
            if (Blank(Query)) return true;

            string haystack = Haystack(item);
            string[] words = Query.Trim().Split(' ');

            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i].Trim();
                if (word.Length == 0) continue;

                if (haystack.IndexOf(word.ToLowerInvariant(), System.StringComparison.Ordinal) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>探す対象をひとつなぎにする。小文字にそろえてから比べます。</summary>
        private static string Haystack(CatalogDraftItem item)
        {
            var sb = new System.Text.StringBuilder();

            sb.Append(item.Title).Append('\n');
            sb.Append(item.Artist).Append('\n');
            sb.Append(item.Genre).Append('\n');
            sb.Append(item.Id).Append('\n');

            if (item.Tags != null)
            {
                for (int i = 0; i < item.Tags.Length; i++)
                {
                    sb.Append(item.Tags[i]).Append('\n');
                }
            }

            return sb.ToString().ToLowerInvariant();
        }

        private static bool HasTag(CatalogDraftItem item, string wanted)
        {
            if (item.Tags == null) return false;

            for (int i = 0; i < item.Tags.Length; i++)
            {
                if (Same(item.Tags[i], wanted)) return true;
            }
            return false;
        }

        private static bool Same(string left, string right)
        {
            if (left == null) left = "";
            if (right == null) right = "";

            return string.Equals(left.Trim(), right.Trim(), System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool Blank(string value)
        {
            return string.IsNullOrWhiteSpace(value);
        }
    }
}
