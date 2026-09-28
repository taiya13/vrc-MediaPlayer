using System;
using System.Collections.Generic;

namespace SmartMediaPlatform.Catalog.Parallel
{
    /// <summary>
    /// Phase1-1 のオブジェクトモデル(<see cref="MediaItem"/>)を、UdonSharp 互換の
    /// 並列配列(<see cref="ParallelCatalogData"/>)へ変換する。
    ///
    /// これが Phase1-1 → Phase1-2 の「変換の唯一の口」。
    /// 実機用ベイカー(UdonCatalogBaker)とテストの両方がここを通るので、
    /// 平坦化ロジックは 1 箇所に集約される(検索ロジックは Udon 制約上どうしても二重化するが、
    /// 変換ロジックは共有できる)。
    /// </summary>
    public static class CatalogFlattener
    {
        public static ParallelCatalogData Flatten(IReadOnlyList<MediaItem> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            int n = items.Count;
            var d = new ParallelCatalogData
            {
                Ids = new string[n],
                Titles = new string[n],
                Artists = new string[n],
                Genres = new string[n],
                Types = new int[n],
                Urls = new string[n],
                Durations = new int[n],
                TagOffsets = new int[n + 1],
                RelatedOffsets = new int[n + 1],
            };

            // 1st pass: 可変長フィールドの合計長を求める
            int totalTags = 0;
            int totalRelated = 0;
            for (int i = 0; i < n; i++)
            {
                var item = items[i];
                if (item == null)
                    throw new ArgumentException("Catalog items must not contain null.", nameof(items));
                totalTags += item.Tags.Count;
                totalRelated += item.RelatedIds.Count;
            }

            d.TagValues = new string[totalTags];
            d.RelatedIds = new string[totalRelated];

            // 2nd pass: 平坦化して詰める
            int tagPos = 0;
            int relPos = 0;
            for (int i = 0; i < n; i++)
            {
                var item = items[i];

                d.Ids[i] = item.Id;
                d.Titles[i] = item.Title;
                d.Artists[i] = item.Artist;
                d.Genres[i] = item.Genre;
                d.Types[i] = (int)item.Type;
                d.Urls[i] = item.Url;
                d.Durations[i] = item.DurationSeconds;

                d.TagOffsets[i] = tagPos;
                for (int t = 0; t < item.Tags.Count; t++)
                    d.TagValues[tagPos++] = item.Tags[t];

                d.RelatedOffsets[i] = relPos;
                for (int r = 0; r < item.RelatedIds.Count; r++)
                    d.RelatedIds[relPos++] = item.RelatedIds[r];
            }

            d.TagOffsets[n] = tagPos;
            d.RelatedOffsets[n] = relPos;

            return d;
        }
    }
}
