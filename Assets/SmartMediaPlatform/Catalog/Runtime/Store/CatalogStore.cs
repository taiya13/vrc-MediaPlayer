using System;
using System.Collections.Generic;

namespace SmartMediaPlatform.Catalog.Store
{
    /// <summary>
    /// <see cref="ICatalogStore"/> の実装。<see cref="IMediaCatalog"/> を包んで、
    /// <b>外へ出るものを <see cref="DisplayMeta"/> と <see cref="PlayableRef"/> だけに絞ります。</b>
    ///
    /// <b>ここが「カタログの内部構造を知っている最後の場所」です。</b>
    /// <c>MediaItem</c> を触るのはこのクラスの中だけで、
    /// 外へ渡すときには必ず表示用と再生用に分けてから渡します。
    ///
    /// <b>変換結果は覚えておきます。</b>
    /// <see cref="DisplayMeta"/> は ID ごとに 1 個だけ作って使い回すので、
    /// UI が毎フレーム呼んでも割り当てが増えません
    /// (同じ ID からは必ず同じ実体が返るので、参照比較もできます)。
    ///
    /// <code>
    /// // いま(手書きのカタログ)
    /// var store = new CatalogStore(new MediaCatalog(new VideoCatalogSource()));
    ///
    /// // 将来(Catalog Builder が生成した Catalog.asset)
    /// var store = new CatalogStore(new MediaCatalog(catalogAsset));
    ///
    /// // ここから先は、どちらでも同じコードで動きます
    /// </code>
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public sealed class CatalogStore : ICatalogStore
    {
        private static readonly DisplayMeta[] EmptyMetas = new DisplayMeta[0];
        private static readonly string[] EmptyIds = new string[0];

        private readonly IMediaCatalog _catalog;

        /// <summary>ID → 表示用(1 度作ったら使い回す)。</summary>
        private readonly Dictionary<string, DisplayMeta> _metaCache =
            new Dictionary<string, DisplayMeta>(StringComparer.OrdinalIgnoreCase);

        private string[] _allIds;

        /// <param name="catalog">包むカタログ。</param>
        public CatalogStore(IMediaCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        // ───────── ICatalogStore ─────────

        public int Count => _catalog.Count;

        public bool Contains(string mediaId)
        {
            return !string.IsNullOrWhiteSpace(mediaId) && _catalog.FindById(mediaId) != null;
        }

        public DisplayMeta GetDisplayMeta(string mediaId)
        {
            if (string.IsNullOrWhiteSpace(mediaId)) return null;

            if (_metaCache.TryGetValue(mediaId, out var cached)) return cached;

            var item = _catalog.FindById(mediaId);
            if (item == null) return null;

            var meta = ToDisplayMeta(item);
            _metaCache[item.Id] = meta;
            return meta;
        }

        public bool TryGetDisplayMeta(string mediaId, out DisplayMeta meta)
        {
            meta = GetDisplayMeta(mediaId);
            return meta != null;
        }

        public IReadOnlyList<DisplayMeta> GetDisplayMetas(IReadOnlyList<string> mediaIds)
        {
            if (mediaIds == null || mediaIds.Count == 0) return EmptyMetas;

            var result = new List<DisplayMeta>(mediaIds.Count);
            for (int i = 0; i < mediaIds.Count; i++)
            {
                // カタログに無い ID は黙って落とす。
                // サーバーが古い ID を返しても、UI は残りをそのまま並べられる。
                var meta = GetDisplayMeta(mediaIds[i]);
                if (meta != null) result.Add(meta);
            }
            return result;
        }

        public PlayableRef GetPlayableRef(string mediaId)
        {
            if (string.IsNullOrWhiteSpace(mediaId)) return PlayableRef.None;

            var item = _catalog.FindById(mediaId);
            return item != null ? new PlayableRef(item.Id, item.Type) : PlayableRef.None;
        }

        public IReadOnlyList<string> GetAllIds()
        {
            if (_allIds != null) return _allIds;

            var all = _catalog.GetAll();
            var ids = new string[all.Count];
            for (int i = 0; i < all.Count; i++) ids[i] = all[i].Id;

            _allIds = ids;
            return _allIds;
        }

        public IReadOnlyList<string> GetIdsByType(MediaType type)
        {
            var items = _catalog.FilterByType(type);
            if (items == null || items.Count == 0) return EmptyIds;

            var ids = new string[items.Count];
            for (int i = 0; i < items.Count; i++) ids[i] = items[i].Id;
            return ids;
        }

        // ───────── 内部 ─────────

        /// <summary>
        /// <c>MediaItem</c> から表示用だけを取り出す。
        /// <b><c>Url</c> は意図的に写していません。</b>
        /// </summary>
        private static DisplayMeta ToDisplayMeta(MediaItem item)
        {
            return new DisplayMeta(
                mediaId: item.Id,
                title: item.Title,
                artist: item.Artist,
                genre: item.Genre,
                tags: item.Tags,
                durationSeconds: item.DurationSeconds,
                type: item.Type);
        }

        /// <summary>
        /// カタログを読み直したときに、覚えている変換結果を捨てる。
        /// (<c>Catalog.asset</c> を差し替えた場合など)
        /// </summary>
        public void Invalidate()
        {
            _metaCache.Clear();
            _allIds = null;
        }

        public override string ToString() => $"CatalogStore({Count} 件)";
    }
}
