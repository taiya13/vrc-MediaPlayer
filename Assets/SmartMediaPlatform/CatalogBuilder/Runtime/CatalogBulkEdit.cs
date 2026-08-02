using System.Collections.Generic;

namespace SmartMediaPlatform.CatalogBuilder
{
    /// <summary>
    /// <b>まとめて直す。</b>Phase6-5。
    ///
    /// <b>なぜ要るのか</b><br/>
    /// 1 チャンネルぶん 200 件にジャンルを入れるのに、
    /// 1 件ずつ開いて打つのは現実的ではありません。
    /// 「チャンネルで絞って全部にジャンルを入れる」が 2 手で終わるようにします。
    ///
    /// <b>直す先は「元の位置」で受け取ります。</b>
    /// <see cref="CatalogItemFilter"/> と <see cref="CatalogSortOrder"/> が返すのが
    /// 元の位置なので、<b>絞り込んだ結果をそのまま渡せます</b>。
    ///
    /// <b>数えて返します。</b>何件変わったかを出さないと、
    /// 押したのに何も起きていないのか、もともと同じだったのかが分かりません。
    ///
    /// <b>Unity にも取り込み元にも依存しません。</b>
    /// </summary>
    public static class CatalogBulkEdit
    {
        /// <summary>ジャンルをまとめて入れる。</summary>
        /// <returns>実際に変わった件数。</returns>
        public static int SetGenre(
            IReadOnlyList<CatalogDraftItem> items, int[] positions, string genre)
        {
            string value = genre == null ? "" : genre.Trim();

            int changed = 0;
            for (int i = 0; i < Length(positions); i++)
            {
                CatalogDraftItem item = At(items, positions[i]);
                if (item == null || item.Genre == value) continue;

                item.Genre = value;
                changed++;
            }
            return changed;
        }

        /// <summary>チャンネル(アーティスト)をまとめて入れる。</summary>
        public static int SetArtist(
            IReadOnlyList<CatalogDraftItem> items, int[] positions, string artist)
        {
            string value = artist == null ? "" : artist.Trim();

            int changed = 0;
            for (int i = 0; i < Length(positions); i++)
            {
                CatalogDraftItem item = At(items, positions[i]);
                if (item == null || item.Artist == value) continue;

                item.Artist = value;
                changed++;
            }
            return changed;
        }

        /// <summary>
        /// タグを足す。<b>すでに付いているものには足しません</b>
        /// (同じタグが 2 つ並ぶと、関連の点数が二重に入ってしまう)。
        /// </summary>
        public static int AddTag(
            IReadOnlyList<CatalogDraftItem> items, int[] positions, string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return 0;

            string value = tag.Trim();

            int changed = 0;
            for (int i = 0; i < Length(positions); i++)
            {
                CatalogDraftItem item = At(items, positions[i]);
                if (item == null) continue;
                if (HasTag(item, value)) continue;

                var grown = new List<string>();
                if (item.Tags != null) grown.AddRange(item.Tags);
                grown.Add(value);

                item.Tags = grown.ToArray();
                changed++;
            }
            return changed;
        }

        /// <summary>タグを外す。付いていないものは飛ばします。</summary>
        public static int RemoveTag(
            IReadOnlyList<CatalogDraftItem> items, int[] positions, string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return 0;

            string value = tag.Trim();

            int changed = 0;
            for (int i = 0; i < Length(positions); i++)
            {
                CatalogDraftItem item = At(items, positions[i]);
                if (item == null || item.Tags == null) continue;
                if (!HasTag(item, value)) continue;

                var kept = new List<string>();
                for (int t = 0; t < item.Tags.Length; t++)
                {
                    if (Same(item.Tags[t], value)) continue;
                    kept.Add(item.Tags[t]);
                }

                item.Tags = kept.ToArray();
                changed++;
            }
            return changed;
        }

        /// <summary>種別をまとめて変える。</summary>
        public static int SetType(
            IReadOnlyList<CatalogDraftItem> items, int[] positions, Catalog.MediaType type)
        {
            int changed = 0;
            for (int i = 0; i < Length(positions); i++)
            {
                CatalogDraftItem item = At(items, positions[i]);
                if (item == null || item.Type == type) continue;

                item.Type = type;
                changed++;
            }
            return changed;
        }

        /// <summary>
        /// 選んだものだけ関連を作り直す。
        ///
        /// <b>近さは一覧の全部から探します</b>が、<b>書き込むのは選んだものだけ</b>です。
        /// 選んだ中だけで探すと、20 件選んだときに
        /// 「その 20 件の中の関連」しか出てこなくなります。
        /// </summary>
        public static int RegenerateRelated(
            IReadOnlyList<CatalogDraftItem> items, int[] positions, RelatedIdGenerator generator)
        {
            if (items == null || generator == null) return 0;

            // 先に全部を計算してから書く。1 件ずつ書きながら進めると、
            // あとの行が「さっき書いた関連」を材料にして順番で結果が変わる。
            int count = Length(positions);
            var computed = new string[count][];

            for (int i = 0; i < count; i++)
            {
                CatalogDraftItem item = At(items, positions[i]);
                if (item == null || string.IsNullOrWhiteSpace(item.Id)) continue;

                computed[i] = generator.Suggest(items, positions[i]);
            }

            int changed = 0;
            for (int i = 0; i < count; i++)
            {
                if (computed[i] == null) continue;

                CatalogDraftItem item = At(items, positions[i]);
                if (item == null || Same(item.RelatedIds, computed[i])) continue;

                item.RelatedIds = computed[i];
                changed++;
            }
            return changed;
        }

        // ───────── 内部 ─────────

        private static int Length(int[] positions)
        {
            return positions == null ? 0 : positions.Length;
        }

        private static CatalogDraftItem At(IReadOnlyList<CatalogDraftItem> items, int index)
        {
            if (items == null || index < 0 || index >= items.Count) return null;
            return items[index];
        }

        private static bool HasTag(CatalogDraftItem item, string tag)
        {
            if (item.Tags == null) return false;

            for (int i = 0; i < item.Tags.Length; i++)
            {
                if (Same(item.Tags[i], tag)) return true;
            }
            return false;
        }

        private static bool Same(string left, string right)
        {
            if (left == null || right == null) return false;

            return string.Equals(
                left.Trim(), right.Trim(), System.StringComparison.OrdinalIgnoreCase);
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
