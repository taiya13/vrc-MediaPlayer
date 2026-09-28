using System.Collections.Generic;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Library
{
    /// <summary>
    /// <b>カタログ全体を「見て」「選ぶ」ための契約。</b>
    ///
    /// <see cref="IMediaListView"/>(並べて選ぶ共通の形)に、
    /// <b>カタログ全体ならではの操作</b>だけを足したものです。
    /// <list type="bullet">
    /// <item>種別での絞り込み(Music / Video …)</item>
    /// <item>並べ替え(登録順 / タイトル / アーティスト / ジャンル / 長さ)</item>
    /// </list>
    /// 関連動画の一覧(<see cref="RelatedMediaView"/>)にはこれらが要らないので、
    /// Phase4-2 で共通部分を <see cref="IMediaListView"/> へ切り出しました。
    ///
    /// <b>持っていないもの(意図的に)</b>
    /// <list type="bullet">
    /// <item>再生する手段 — 再生は <c>PlayerSession</c> の仕事。
    /// 受け渡しは <c>LibraryPlaybackBridge</c>(別 asmdef)が行う</item>
    /// <item>URL — この層は <c>DisplayMeta</c> と <c>PlayableRef</c> しか扱いません。
    /// URL を知るのは引き続き <c>VideoBackend</c> だけです</item>
    /// <item>カタログの内部構造 — データは <c>ICatalogStore</c> からしか取りません</item>
    /// <item>検索・Playlist・お気に入り・履歴・おすすめ — Phase4-1 / 4-2 の対象外</item>
    /// </list>
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public interface IMediaLibrary : IMediaListView
    {
        /// <summary>いま表示している種別。</summary>
        IReadOnlyList<MediaType> VisibleTypes { get; }

        /// <summary>並び順。</summary>
        LibrarySortOrder SortOrder { get; set; }

        /// <summary>すべての種別を表示する。</summary>
        void ShowAll();

        /// <summary>指定した種別だけを表示する。何も渡さなければ <see cref="ShowAll"/> と同じ。</summary>
        void ShowOnly(params MediaType[] types);

        /// <summary>その種別を表示しているか。</summary>
        bool IsVisible(MediaType type);

        /// <summary>カタログを読み直して一覧を作り直す(選択は可能なかぎり保つ)。</summary>
        void Refresh();
    }
}
