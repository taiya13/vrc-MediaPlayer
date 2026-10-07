using System.Collections.Generic;

namespace SmartMediaPlatform.CatalogBuilder
{
    /// <summary>
    /// <b>一覧の並べ替え。</b>Phase6-5。
    ///
    /// <b>データは並べ替えません。</b>返すのは<b>元の位置の番号</b>だけで、
    /// <see cref="CatalogDraft"/> の中身は動きません。理由は 2 つです。
    /// <list type="number">
    /// <item><b>手動順に戻せる。</b>一度並べ替えて保存すると、
    ///       自分で決めた並びは二度と戻りません</item>
    /// <item><b>再生側の並びが勝手に変わらない。</b>カタログの並びは
    ///       ワールドの「すべての曲」の並びそのものです</item>
    /// </list>
    /// <see cref="CatalogItemFilter"/> と同じ約束(元の位置を返す)なので、
    /// 絞り込みと並べ替えは<b>そのまま重ねられます</b>。
    ///
    /// <b>取り込み元を問いません。</b>見るのは <see cref="CatalogDraftItem"/> だけです。
    /// </summary>
    public static class CatalogSortOrder
    {
        /// <summary>手で決めた並び(= カタログそのままの順)。</summary>
        public const int Manual = 0;

        /// <summary>公開日。</summary>
        public const int PublishedAt = 1;

        /// <summary>見出し。</summary>
        public const int Title = 2;

        /// <summary>チャンネル名。同じチャンネルの中は見出し順。</summary>
        public const int Channel = 3;

        /// <summary>再生時間。</summary>
        public const int Duration = 4;

        public static readonly string[] Labels =
        {
            "手動順", "公開日", "見出し", "チャンネル", "再生時間",
        };

        /// <summary>
        /// <paramref name="positions"/> を並べ替えて返す。
        /// <paramref name="positions"/> は絞り込みの結果(元の位置)をそのまま渡せます。
        ///
        /// <b>並びは必ず 1 つに決まります。</b>同じ値のものは<b>元の位置の順</b>にするので、
        /// 同じ入力からは何度やっても同じ並びが出ます。
        /// </summary>
        public static int[] Apply(
            IReadOnlyList<CatalogDraftItem> items, int[] positions, int order, bool descending)
        {
            if (items == null || positions == null) return new int[0];

            var result = new List<int>(positions);
            if (order == Manual)
            {
                // 手動順は「元の並び」。逆順だけは受け付ける。
                if (descending) result.Reverse();
                return result.ToArray();
            }

            // 挿入ソート。数百件が相手で、しかも「同じ値なら元の順」を
            // そのまま保てる(安定ソート)ので、これで足りる。
            for (int i = 1; i < result.Count; i++)
            {
                int moving = result[i];
                int at = i - 1;

                while (at >= 0 && ShouldComeAfter(items, result[at], moving, order, descending))
                {
                    result[at + 1] = result[at];
                    at--;
                }
                result[at + 1] = moving;
            }

            return result.ToArray();
        }

        /// <summary><paramref name="left"/> が <paramref name="right"/> より後ろに来るべきか。</summary>
        private static bool ShouldComeAfter(
            IReadOnlyList<CatalogDraftItem> items, int left, int right, int order, bool descending)
        {
            int compared = Compare(Get(items, left), Get(items, right), order);
            if (compared == 0) return false;   // 同じなら動かさない(元の順を保つ)

            return descending ? compared < 0 : compared > 0;
        }

        private static CatalogDraftItem Get(IReadOnlyList<CatalogDraftItem> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            return items[index];
        }

        private static int Compare(CatalogDraftItem left, CatalogDraftItem right, int order)
        {
            if (left == null || right == null) return 0;

            if (order == Duration) return left.DurationSeconds.CompareTo(right.DurationSeconds);

            if (order == PublishedAt)
            {
                // ISO8601 は文字のまま比べても日付順になる。
                // 空(取れなかったもの)は必ず後ろへ回す。
                bool leftBlank = string.IsNullOrWhiteSpace(left.PublishedAt);
                bool rightBlank = string.IsNullOrWhiteSpace(right.PublishedAt);

                if (leftBlank && rightBlank) return 0;
                if (leftBlank) return 1;
                if (rightBlank) return -1;

                return string.CompareOrdinal(left.PublishedAt, right.PublishedAt);
            }

            if (order == Channel)
            {
                int channel = CompareText(left.Artist, right.Artist);
                if (channel != 0) return channel;

                // 同じチャンネルの中は見出し順。これが無いと
                // まとまってはいるが中がばらばら、という一番読みにくい形になる。
                return CompareText(left.Title, right.Title);
            }

            return CompareText(left.Title, right.Title);
        }

        private static int CompareText(string left, string right)
        {
            if (left == null) left = "";
            if (right == null) right = "";

            return string.Compare(
                left.Trim(), right.Trim(), System.StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
