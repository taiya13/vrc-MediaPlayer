using System.Collections.Generic;

namespace SmartMediaPlatform.CatalogBuilder
{
    /// <summary>
    /// <b>関連(<see cref="CatalogDraftItem.RelatedIds"/>)を自動で埋める。</b>Phase6-4。
    ///
    /// <b>なぜ自動が要るのか</b><br/>
    /// 関連はワールドの「おすすめ」欄にそのまま出ます。
    /// 200 件のカタログで手で書くのは現実的ではなく、
    /// <b>空のままだと「おすすめ」が常に空</b>になります。
    ///
    /// <b>手で書いたものを壊しません。</b>既定(<see cref="Overwrite"/> = false)では
    /// <b>すでに関連が入っている行は触りません</b>。
    /// 全部作り直したいときだけ <see cref="Overwrite"/> を立ててください。
    /// あとから窓で 1 件ずつ足したり消したりできる形は、Phase6-1 のままです。
    ///
    /// <b>近さの決め方</b><br/>
    /// <list type="bullet">
    /// <item>同じチャンネル …… <see cref="ChannelScore"/> 点</item>
    /// <item>同じジャンル …… <see cref="GenreScore"/> 点</item>
    /// <item>同じタグ 1 つにつき …… <see cref="TagScore"/> 点</item>
    /// </list>
    /// 0 点のものは<b>関連にしません</b>。何の共通点も無いものを並べても
    /// 「おすすめ」になっていないためです。同じ点なら<b>カタログの順</b>で選びます
    /// (同じ入力からは必ず同じ結果が出ます)。
    ///
    /// <b>推薦エンジン(Phase1-3)とは別物です。</b>
    /// あちらは再生中に「次に何をかけるか」を決めます。
    /// ここは<b>編集時に下ごしらえをするだけ</b>で、ワールドには 1 行も入りません。
    /// </summary>
    public sealed class RelatedIdGenerator
    {
        /// <summary>1 件あたりに入れる関連の上限。</summary>
        public int MaxPerItem = 6;

        public bool UseChannel = true;
        public bool UseGenre = true;
        public bool UseTags = true;

        public int ChannelScore = 3;
        public int GenreScore = 2;
        public int TagScore = 1;

        /// <summary>
        /// すでに関連が入っている行も作り直すか。
        /// <b>既定は false</b>(手で書いたものを守る)。
        /// </summary>
        public bool Overwrite;

        /// <summary>
        /// 全部の行の関連を埋める。
        /// </summary>
        /// <returns>実際に書き換えた件数。</returns>
        public int Generate(IReadOnlyList<CatalogDraftItem> items)
        {
            if (items == null) return 0;

            // 先に全部を計算してから書き込む。
            // 1 件ずつ書きながら進めると、あとの行が「さっき書いた関連」を
            // 材料にしてしまい、順番で結果が変わってしまう。
            var computed = new string[items.Count][];

            for (int i = 0; i < items.Count; i++)
            {
                CatalogDraftItem item = items[i];
                if (item == null) continue;
                if (!Overwrite && HasRelated(item)) continue;
                if (string.IsNullOrWhiteSpace(item.Id)) continue;

                computed[i] = Suggest(items, i);
            }

            int changed = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (computed[i] == null) continue;
                if (Same(items[i].RelatedIds, computed[i])) continue;

                items[i].RelatedIds = computed[i];
                changed++;
            }
            return changed;
        }

        /// <summary>
        /// <paramref name="index"/> の行に入れる関連 ID。近い順。
        /// <b>書き込みはしません</b>(窓のプレビュー用にも使えるように)。
        /// </summary>
        public string[] Suggest(IReadOnlyList<CatalogDraftItem> items, int index)
        {
            if (items == null || index < 0 || index >= items.Count) return new string[0];

            CatalogDraftItem self = items[index];
            if (self == null) return new string[0];

            int limit = MaxPerItem < 0 ? 0 : MaxPerItem;
            if (limit == 0) return new string[0];

            var ids = new List<string>();
            var scores = new List<int>();

            for (int i = 0; i < items.Count; i++)
            {
                if (i == index) continue;

                CatalogDraftItem other = items[i];
                if (other == null) continue;
                if (string.IsNullOrWhiteSpace(other.Id)) continue;

                int score = Score(self, other);
                if (score <= 0) continue;

                // 同じ ID が 2 度出てきたら、点の高いほうを残す。
                string id = other.Id.Trim();
                int already = ids.IndexOf(id);
                if (already >= 0)
                {
                    if (score > scores[already]) scores[already] = score;
                    continue;
                }

                ids.Add(id);
                scores.Add(score);
            }

            return TakeBest(ids, scores, limit);
        }

        /// <summary>2 件がどれだけ近いか。0 なら関連にしない。</summary>
        public int Score(CatalogDraftItem left, CatalogDraftItem right)
        {
            if (left == null || right == null) return 0;

            int score = 0;

            if (UseChannel && Same(left.Artist, right.Artist)) score += ChannelScore;
            if (UseGenre && Same(left.Genre, right.Genre)) score += GenreScore;
            if (UseTags) score += SharedTagCount(left, right) * TagScore;

            return score < 0 ? 0 : score;
        }

        // ───────── 内部 ─────────

        /// <summary>
        /// 点の高い順に <paramref name="limit"/> 件。
        /// 同じ点なら<b>先に出てきたほう</b>(= カタログの順)を選びます。
        /// </summary>
        private static string[] TakeBest(List<string> ids, List<int> scores, int limit)
        {
            int take = ids.Count < limit ? ids.Count : limit;
            var result = new List<string>();
            var used = new List<bool>();

            for (int i = 0; i < ids.Count; i++) used.Add(false);

            for (int picked = 0; picked < take; picked++)
            {
                int best = -1;

                for (int i = 0; i < ids.Count; i++)
                {
                    if (used[i]) continue;
                    if (best < 0 || scores[i] > scores[best]) best = i;
                }

                if (best < 0) break;

                used[best] = true;
                result.Add(ids[best]);
            }

            return result.ToArray();
        }

        private static bool HasRelated(CatalogDraftItem item)
        {
            if (item.RelatedIds == null) return false;

            for (int i = 0; i < item.RelatedIds.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(item.RelatedIds[i])) return true;
            }
            return false;
        }

        private static int SharedTagCount(CatalogDraftItem left, CatalogDraftItem right)
        {
            if (left.Tags == null || right.Tags == null) return 0;

            int shared = 0;
            for (int i = 0; i < left.Tags.Length; i++)
            {
                string tag = left.Tags[i];
                if (string.IsNullOrWhiteSpace(tag)) continue;

                // 左に同じタグが 2 回あっても 1 回だけ数える。
                bool duplicate = false;
                for (int back = 0; back < i; back++)
                {
                    if (Same(left.Tags[back], tag)) { duplicate = true; break; }
                }
                if (duplicate) continue;

                for (int j = 0; j < right.Tags.Length; j++)
                {
                    if (!Same(right.Tags[j], tag)) continue;
                    shared++;
                    break;
                }
            }
            return shared;
        }

        private static bool Same(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;

            return string.Equals(left.Trim(), right.Trim(), System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool Same(string[] left, string[] right)
        {
            if (left == null) left = new string[0];
            if (right == null) right = new string[0];

            if (left.Length != right.Length) return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i]) return false;
            }
            return true;
        }
    }
}
