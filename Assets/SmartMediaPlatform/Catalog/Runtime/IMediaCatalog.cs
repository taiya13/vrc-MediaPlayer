using System.Collections.Generic;

namespace SmartMediaPlatform.Catalog
{
    /// <summary>
    /// Catalog API。上位レイヤー(Recommendation Engine / Queue / Player Backend)は
    /// このインターフェースだけに依存する。カタログ側は上位レイヤーを一切知らない。
    /// すべて読み取り専用 — カタログは実行時に変更されない(事前生成カタログ方式)。
    /// </summary>
    public interface IMediaCatalog
    {
        /// <summary>登録アイテム数。</summary>
        int Count { get; }

        /// <summary>ID 完全一致で 1 件取得。見つからなければ null。</summary>
        MediaItem FindById(string id);

        bool TryFindById(string id, out MediaItem item);

        /// <summary>全件取得(登録順)。</summary>
        IReadOnlyList<MediaItem> GetAll();

        /// <summary>タイトル部分一致(大文字小文字を区別しない)。空クエリは 0 件。</summary>
        IReadOnlyList<MediaItem> SearchByTitle(string query);

        /// <summary>アーティスト部分一致(大文字小文字を区別しない)。空クエリは 0 件。</summary>
        IReadOnlyList<MediaItem> SearchByArtist(string query);

        /// <summary>タグ完全一致(大文字小文字を区別しない)。</summary>
        IReadOnlyList<MediaItem> SearchByTag(string tag);

        /// <summary>ジャンル完全一致(大文字小文字を区別しない)。</summary>
        IReadOnlyList<MediaItem> SearchByGenre(string genre);

        /// <summary>種別で絞り込み。将来 Video / Podcast / Live を扱うための入口。</summary>
        IReadOnlyList<MediaItem> FilterByType(MediaType type);

        /// <summary>ランダムに 1 件。カタログが空なら null。</summary>
        MediaItem GetRandom();

        /// <summary>ランダムに最大 count 件(重複なし)。</summary>
        IReadOnlyList<MediaItem> GetRandom(int count);

        /// <summary>
        /// 指定 ID の関連アイテムを宣言順で取得。
        /// 未知の ID・関連なしの場合は空リスト(null は返さない)。
        /// </summary>
        IReadOnlyList<MediaItem> GetRelated(string id);
    }
}
