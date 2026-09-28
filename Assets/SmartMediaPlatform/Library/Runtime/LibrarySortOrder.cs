namespace SmartMediaPlatform.Library
{
    /// <summary>
    /// 一覧の並び順。
    ///
    /// <b>「検索」ではありません。</b>Phase4-1 では検索を実装しないので、
    /// ここにあるのは<b>すでに手元にある一覧をどう並べるか</b>だけです。
    /// 絞り込み条件を増やす場所でもありません(種別の絞り込みは
    /// <see cref="MediaLibrary.ShowOnly"/>)。
    /// </summary>
    public enum LibrarySortOrder
    {
        /// <summary>カタログの登録順(既定)。Catalog Builder が意図した並びをそのまま見せる。</summary>
        CatalogOrder = 0,

        /// <summary>タイトル昇順。</summary>
        Title = 1,

        /// <summary>アーティスト昇順(同じアーティスト内はタイトル昇順)。</summary>
        Artist = 2,

        /// <summary>ジャンル昇順(同じジャンル内はタイトル昇順)。</summary>
        Genre = 3,

        /// <summary>再生時間の短い順。</summary>
        Duration = 4,
    }
}
