using System;
using System.Collections.Generic;

namespace SmartMediaPlatform.Catalog.Store
{
    /// <summary>
    /// <b>「これに関連するのはどれか」を ID の並びで答えるだけ</b>の契約。
    ///
    /// <b>返すのは MediaId(string)の並びだけです。</b>
    /// <see cref="DisplayMeta"/> も <c>MediaItem</c> も URL も返しません。
    /// 表示用への変換は <see cref="ICatalogStore"/> が行います。
    ///
    /// <b>これが「将来サーバーに差し替える」ための境目です。</b>
    /// サーバーから返ってくるのは普通「関連動画の ID の配列」なので、
    /// <b>この インターフェース の実装を 1 つ足すだけ</b>で乗り換えられます。
    /// UI も再生系も、ID がどこから来たのかを知りません。
    ///
    /// <code>
    /// // いま:カタログが持つ RelatedIds を使う
    /// IRelatedMediaProvider related = new CatalogRelatedMediaProvider(catalog);
    ///
    /// // 将来:サーバーが返した ID をそのまま入れる
    /// var related = new StaticRelatedMediaProvider();
    /// related.Set("video-001", idsFromServer);
    ///
    /// // どちらでも、この先は同じコード
    /// var view = new RelatedMediaView(store, related);
    /// </code>
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public interface IRelatedMediaProvider
    {
        /// <summary>
        /// <paramref name="mediaId"/> に関連するものの ID を返す。
        /// </summary>
        /// <param name="mediaId">起点。</param>
        /// <param name="maxCount">最大件数。0 以下なら制限なし。</param>
        /// <returns>関連する ID の並び。無ければ空(null は返さない)。</returns>
        IReadOnlyList<string> GetRelatedIds(string mediaId, int maxCount = 0);
    }

    /// <summary>
    /// カタログが持つ <c>RelatedIds</c> をそのまま使う実装(いまの構成)。
    ///
    /// <c>RelatedIds</c> は「Catalog Builder が事前計算して埋める想定」の項目
    /// (Phase1-1 からの設計)なので、<b>Catalog Builder が動き出したら
    /// このクラスは書き換えずにそのまま精度が上がります。</b>
    /// </summary>
    public sealed class CatalogRelatedMediaProvider : IRelatedMediaProvider
    {
        private static readonly string[] Empty = new string[0];

        private readonly IMediaCatalog _catalog;

        public CatalogRelatedMediaProvider(IMediaCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public IReadOnlyList<string> GetRelatedIds(string mediaId, int maxCount = 0)
        {
            if (string.IsNullOrWhiteSpace(mediaId)) return Empty;

            // IMediaCatalog.GetRelated は解決済み・自己除外・欠損除外を済ませてくれる
            var items = _catalog.GetRelated(mediaId);
            if (items == null || items.Count == 0) return Empty;

            int take = maxCount > 0 && maxCount < items.Count ? maxCount : items.Count;
            var ids = new string[take];
            for (int i = 0; i < take; i++) ids[i] = items[i].Id;
            return ids;
        }

        public override string ToString() => "CatalogRelatedMediaProvider(Catalog の RelatedIds)";
    }

    /// <summary>
    /// <b>外から与えられた ID 表をそのまま返す実装。</b>
    ///
    /// <b>サーバー連携の受け皿</b>です。
    /// 「関連動画の ID を取ってくる処理」だけを別に書いて、
    /// 結果をここへ <see cref="Set"/> すれば、UI から下は何も変わりません。
    ///
    /// <code>
    /// // 例: サーバーからの応答を流し込む(取得処理はこの層の外)
    /// provider.Set("video-001", new[] { "video-004", "video-009" });
    /// </code>
    ///
    /// 入っていない ID を聞かれたときは、<see cref="Fallback"/> があればそちらへ回します
    /// (サーバーがまだ答えていない間もカタログの関連で表示を埋められます)。
    /// </summary>
    public sealed class StaticRelatedMediaProvider : IRelatedMediaProvider
    {
        private static readonly string[] Empty = new string[0];

        private readonly Dictionary<string, string[]> _table =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        /// <param name="fallback">表に無いときに回す先。null なら空を返す。</param>
        public StaticRelatedMediaProvider(IRelatedMediaProvider fallback = null)
        {
            Fallback = fallback;
        }

        /// <summary>表に無い ID を聞かれたときの回し先。</summary>
        public IRelatedMediaProvider Fallback { get; set; }

        /// <summary>覚えている起点の数。</summary>
        public int Count => _table.Count;

        /// <summary>ある起点に対する関連 ID を設定する(既にあれば置き換え)。</summary>
        public void Set(string mediaId, IReadOnlyList<string> relatedIds)
        {
            if (string.IsNullOrWhiteSpace(mediaId)) return;

            if (relatedIds == null || relatedIds.Count == 0)
            {
                _table[mediaId] = Empty;
                return;
            }

            var copy = new string[relatedIds.Count];
            for (int i = 0; i < relatedIds.Count; i++) copy[i] = relatedIds[i];
            _table[mediaId] = copy;
        }

        /// <summary>ある起点の設定を消す(次からは <see cref="Fallback"/> に回ります)。</summary>
        public bool Remove(string mediaId)
        {
            return !string.IsNullOrWhiteSpace(mediaId) && _table.Remove(mediaId);
        }

        /// <summary>すべて消す。</summary>
        public void Clear() => _table.Clear();

        /// <summary>その起点が表にあるか。</summary>
        public bool Has(string mediaId)
        {
            return !string.IsNullOrWhiteSpace(mediaId) && _table.ContainsKey(mediaId);
        }

        public IReadOnlyList<string> GetRelatedIds(string mediaId, int maxCount = 0)
        {
            if (string.IsNullOrWhiteSpace(mediaId)) return Empty;

            if (!_table.TryGetValue(mediaId, out var ids))
            {
                return Fallback != null ? Fallback.GetRelatedIds(mediaId, maxCount) : Empty;
            }

            if (maxCount <= 0 || maxCount >= ids.Length) return ids;

            var trimmed = new string[maxCount];
            Array.Copy(ids, trimmed, maxCount);
            return trimmed;
        }

        public override string ToString()
        {
            return $"StaticRelatedMediaProvider({Count} 件"
                   + (Fallback != null ? $" / 予備: {Fallback}" : "") + ")";
        }
    }
}
