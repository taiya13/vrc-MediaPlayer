using System;
using System.Collections.Generic;

namespace SmartMediaPlatform.Catalog.Data
{
    /// <summary>
    /// <b>複数のカタログ供給元を 1 つに束ねる。</b>
    ///
    /// Phase3-4 で追加。カタログの「作り方」を足すときに、
    /// <b>既存のソースを書き換えずに並べるだけ</b>で済むようにするためのものです。
    ///
    /// <code>
    /// // 例: 手書きの動画カタログ + Catalog Builder が生成したカタログ
    /// var source = new CompositeCatalogSource(
    ///     new VideoCatalogSource(),
    ///     new GeneratedCatalogSource());     // ← Phase4 で足す想定
    /// var catalog = new MediaCatalog(source);
    /// </code>
    ///
    /// <b>ID が重なったときは先に並べたソースが勝ちます</b>(先勝ち)。
    /// 「手書きで上書きしたい」「生成物より手元の修正を優先したい」に素直に対応できます。
    /// </summary>
    public sealed class CompositeCatalogSource : IMediaCatalogSource
    {
        private readonly IMediaCatalogSource[] _sources;

        public CompositeCatalogSource(params IMediaCatalogSource[] sources)
        {
            var usable = new List<IMediaCatalogSource>();
            if (sources != null)
            {
                foreach (var source in sources)
                {
                    if (source != null) usable.Add(source);
                }
            }
            _sources = usable.ToArray();
        }

        /// <summary>束ねている供給元。</summary>
        public IReadOnlyList<IMediaCatalogSource> Sources => _sources;

        public IReadOnlyList<MediaItem> LoadItems()
        {
            var result = new List<MediaItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _sources.Length; i++)
            {
                var items = _sources[i].LoadItems();
                if (items == null) continue;

                for (int j = 0; j < items.Count; j++)
                {
                    var item = items[j];
                    if (item == null) continue;
                    if (seen.Add(item.Id)) result.Add(item);   // 先勝ち
                }
            }

            return result;
        }

        public override string ToString()
        {
            return $"CompositeCatalogSource({_sources.Length} sources)";
        }
    }

    /// <summary>
    /// <b>種別で絞り込んだカタログ供給元。</b>
    ///
    /// 「動画だけのカタログが欲しい」といった場面で、
    /// <b>元のソースを複製せずに</b>絞り込めます。
    ///
    /// <code>
    /// // 混在カタログから動画だけを取り出す
    /// var videosOnly = new FilteredCatalogSource(
    ///     new CompositeCatalogSource(...), MediaType.Video, MediaType.Live);
    /// </code>
    /// </summary>
    public sealed class FilteredCatalogSource : IMediaCatalogSource
    {
        private readonly IMediaCatalogSource _inner;
        private readonly MediaType[] _types;

        public FilteredCatalogSource(IMediaCatalogSource inner, params MediaType[] types)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _types = types != null && types.Length > 0
                ? (MediaType[])types.Clone()
                : new[] { MediaType.Video, MediaType.Live };
        }

        /// <summary>残す種別。</summary>
        public MediaType[] Types => (MediaType[])_types.Clone();

        public IReadOnlyList<MediaItem> LoadItems()
        {
            var result = new List<MediaItem>();
            var items = _inner.LoadItems();
            if (items == null) return result;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item != null && Accepts(item.Type)) result.Add(item);
            }

            return result;
        }

        private bool Accepts(MediaType type)
        {
            for (int i = 0; i < _types.Length; i++)
            {
                if (_types[i] == type) return true;
            }
            return false;
        }

        public override string ToString()
        {
            return $"FilteredCatalogSource({string.Join("/", Array.ConvertAll(_types, t => t.ToString()))})";
        }
    }

    /// <summary>
    /// メモリ上のリストをそのまま返す供給元。
    ///
    /// テスト・実験用のほか、<b>Catalog Builder(提案書 §7)が生成した結果を
    /// 差し込む受け皿</b>としても使えます
    /// (JSON を読む処理は Builder 側に置き、ここには <c>MediaItem</c> の列だけを渡す)。
    /// </summary>
    public sealed class InMemoryCatalogSource : IMediaCatalogSource
    {
        private readonly MediaItem[] _items;

        public InMemoryCatalogSource(params MediaItem[] items)
        {
            _items = items != null ? (MediaItem[])items.Clone() : new MediaItem[0];
        }

        public InMemoryCatalogSource(IEnumerable<MediaItem> items)
        {
            var list = new List<MediaItem>();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item != null) list.Add(item);
                }
            }
            _items = list.ToArray();
        }

        public IReadOnlyList<MediaItem> LoadItems() => _items;

        public override string ToString() => $"InMemoryCatalogSource({_items.Length} items)";
    }
}
