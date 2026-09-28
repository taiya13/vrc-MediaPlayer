using System;
using System.Collections.Generic;

namespace SmartMediaPlatform.Catalog
{
    /// <summary>
    /// IMediaCatalog のインメモリ実装。
    /// 構築時に全アイテムを受け取って以降は不変(事前生成カタログ方式)。
    /// 乱数は外から注入できるため、ランダム API もテストで決定的に検証できる。
    /// </summary>
    public sealed class MediaCatalog : IMediaCatalog
    {
        private readonly List<MediaItem> _items;
        private readonly IReadOnlyList<MediaItem> _readOnlyItems;
        private readonly Dictionary<string, MediaItem> _byId;
        private readonly Random _random;

        public MediaCatalog(IMediaCatalogSource source, Random random = null)
            : this(Load(source), random)
        {
        }

        public MediaCatalog(IEnumerable<MediaItem> items, Random random = null)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            _items = new List<MediaItem>();
            _byId = new Dictionary<string, MediaItem>(StringComparer.OrdinalIgnoreCase);
            _random = random ?? new Random();

            foreach (var item in items)
            {
                if (item == null)
                    throw new ArgumentException("Catalog items must not contain null.", nameof(items));
                if (_byId.ContainsKey(item.Id))
                    throw new ArgumentException($"Duplicate media id: '{item.Id}'.", nameof(items));

                _byId.Add(item.Id, item);
                _items.Add(item);
            }

            // GetAll の戻り値を List にキャストして書き換えられないよう、読み取り専用ビューを挟む。
            _readOnlyItems = _items.AsReadOnly();
        }

        private static IEnumerable<MediaItem> Load(IMediaCatalogSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return source.LoadItems() ?? throw new InvalidOperationException(
                $"{source.GetType().Name}.LoadItems() returned null.");
        }

        public int Count => _items.Count;

        public MediaItem FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _byId.TryGetValue(id, out var item) ? item : null;
        }

        public bool TryFindById(string id, out MediaItem item)
        {
            item = FindById(id);
            return item != null;
        }

        public IReadOnlyList<MediaItem> GetAll()
        {
            return _readOnlyItems;
        }

        public IReadOnlyList<MediaItem> SearchByTitle(string query)
        {
            return FindPartial(query, item => item.Title);
        }

        public IReadOnlyList<MediaItem> SearchByArtist(string query)
        {
            return FindPartial(query, item => item.Artist);
        }

        public IReadOnlyList<MediaItem> SearchByGenre(string genre)
        {
            if (string.IsNullOrWhiteSpace(genre)) return Array.Empty<MediaItem>();

            var results = new List<MediaItem>();
            foreach (var item in _items)
            {
                if (string.Equals(item.Genre, genre, StringComparison.OrdinalIgnoreCase))
                    results.Add(item);
            }
            return results;
        }

        public IReadOnlyList<MediaItem> SearchByTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return Array.Empty<MediaItem>();

            var results = new List<MediaItem>();
            foreach (var item in _items)
            {
                foreach (var t in item.Tags)
                {
                    if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(item);
                        break;
                    }
                }
            }
            return results;
        }

        public IReadOnlyList<MediaItem> FilterByType(MediaType type)
        {
            var results = new List<MediaItem>();
            foreach (var item in _items)
            {
                if (item.Type == type)
                    results.Add(item);
            }
            return results;
        }

        public MediaItem GetRandom()
        {
            if (_items.Count == 0) return null;
            return _items[_random.Next(_items.Count)];
        }

        public IReadOnlyList<MediaItem> GetRandom(int count)
        {
            if (count <= 0 || _items.Count == 0) return Array.Empty<MediaItem>();

            // 先頭 count 件だけの部分 Fisher–Yates。元のリストは変更しない。
            var pool = _items.ToArray();
            int take = Math.Min(count, pool.Length);
            for (int i = 0; i < take; i++)
            {
                int j = i + _random.Next(pool.Length - i);
                var tmp = pool[i];
                pool[i] = pool[j];
                pool[j] = tmp;
            }

            var results = new MediaItem[take];
            Array.Copy(pool, results, take);
            return results;
        }

        public IReadOnlyList<MediaItem> GetRelated(string id)
        {
            var origin = FindById(id);
            if (origin == null || origin.RelatedIds.Count == 0)
                return Array.Empty<MediaItem>();

            var results = new List<MediaItem>(origin.RelatedIds.Count);
            foreach (var relatedId in origin.RelatedIds)
            {
                // カタログに存在しない ID は黙って読み飛ばす(データ不整合に強くする)。
                // 自分自身への参照も除外する。
                if (relatedId != null
                    && !string.Equals(relatedId, origin.Id, StringComparison.OrdinalIgnoreCase)
                    && _byId.TryGetValue(relatedId, out var related))
                {
                    results.Add(related);
                }
            }
            return results;
        }

        private IReadOnlyList<MediaItem> FindPartial(string query, Func<MediaItem, string> selector)
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<MediaItem>();

            var results = new List<MediaItem>();
            foreach (var item in _items)
            {
                var value = selector(item);
                if (value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    results.Add(item);
            }
            return results;
        }
    }
}
