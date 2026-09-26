using System;
using System.Collections.Generic;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Video
{
    /// <summary>
    /// <b>事前に登録された動画 URL の一覧</b>。
    ///
    /// 提案書のとおり VRChat では実行時に <c>VRCUrl</c> を生成できません。
    /// そのため再生できる URL は「編集時に Catalog からベイクしたもの」に限られます。
    /// <see cref="VRChatVideoBackend"/> はこの表に無い URL を <see cref="VRChatVideoBackend.CanPlay"/>
    /// の時点で断り、<b>実行時に URL を組み立てようとしません。</b>
    ///
    /// 実装は 2 つあります:
    ///  - <see cref="CatalogVideoUrlTable"/> … 純粋 C#(テスト・ConsoleDemo 用)
    ///  - <c>VRCUrlTable</c>(SDK 側)… ベイク済み <c>VRCUrl</c> 配列そのもの
    /// </summary>
    public interface IVideoUrlTable
    {
        /// <summary>登録されている URL の数。</summary>
        int Count { get; }

        /// <summary>その URL が事前登録されているか。</summary>
        bool Contains(string url);
    }

    /// <summary>
    /// Catalog に登録済みの URL をそのまま表にした <see cref="IVideoUrlTable"/>。
    ///
    /// SDK 側の <c>VRCUrlTable</c> は同じ URL 集合から <c>VRCUrl</c> を焼き込みます。
    /// つまり<b>両者の中身は必ず一致し</b>、EditMode テストで確認したことが実機でも成立します。
    /// </summary>
    public sealed class CatalogVideoUrlTable : IVideoUrlTable
    {
        private static readonly MediaType[] DefaultTypes = { MediaType.Video, MediaType.Live };

        private readonly List<string> _urls = new List<string>();
        private readonly HashSet<string> _lookup =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>カタログの指定種別(既定は Video / Live)の URL を取り込む。</summary>
        public CatalogVideoUrlTable(IMediaCatalog catalog, params MediaType[] types)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            var targets = types != null && types.Length > 0 ? types : DefaultTypes;
            for (int i = 0; i < targets.Length; i++)
            {
                var items = catalog.FilterByType(targets[i]);
                for (int j = 0; j < items.Count; j++) Add(items[j].Url);
            }
        }

        /// <summary>URL を直接与える(テストや部分的なベイク用)。</summary>
        public CatalogVideoUrlTable(IEnumerable<string> urls)
        {
            if (urls == null) throw new ArgumentNullException(nameof(urls));
            foreach (var url in urls) Add(url);
        }

        public int Count => _urls.Count;

        /// <summary>登録順の URL 一覧(SDK 側のベイクはこの順で <c>VRCUrl</c> を作る)。</summary>
        public IReadOnlyList<string> Urls => _urls;

        public bool Contains(string url)
        {
            return !string.IsNullOrWhiteSpace(url) && _lookup.Contains(url);
        }

        private void Add(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || _lookup.Contains(url)) return;
            _urls.Add(url);
            _lookup.Add(url);
        }
    }
}
