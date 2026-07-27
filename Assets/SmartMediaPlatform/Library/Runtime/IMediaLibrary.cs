using System.Collections.Generic;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Library
{
    /// <summary>
    /// <b>カタログを「見て」「選ぶ」だけの契約。</b>
    ///
    /// Phase4-1 の役割はこの 2 つだけです。
    /// <list type="number">
    /// <item><b>閲覧</b> … カタログの中身を一覧として見せる(種別で絞れる・並べ替えられる)</item>
    /// <item><b>選択</b> … その中から 1 件を選び、<b>MediaId</b> を上位へ渡せる状態にする</item>
    /// </list>
    ///
    /// <b>持っていないもの(意図的に)</b>
    /// <list type="bullet">
    /// <item>再生する手段 — 再生は <c>PlayerSession</c> の仕事。
    /// 受け渡しは <c>LibraryPlaybackBridge</c>(別 asmdef)が行う</item>
    /// <item>URL — <b>この層は URL を一切扱いません。</b>
    /// 渡すのは <see cref="SelectedMediaId"/>(string)だけ。
    /// URL を知るのは引き続き <c>VideoBackend</c> だけです</item>
    /// <item>検索・Playlist・お気に入り・履歴・おすすめ — Phase4-1 の対象外</item>
    /// </list>
    ///
    /// <b>依存はカタログだけ</b>です(asmdef の参照が
    /// <c>SmartMediaPlatform.Catalog</c> 1 つだけなのがその証拠)。
    /// おかげで Console・uGUI・VRChat の Canvas のどれから使っても同じコードで済みます。
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public interface IMediaLibrary
    {
        // ───────── 閲覧 ─────────

        /// <summary>いま表示している一覧(絞り込み・並べ替え済み)。</summary>
        IReadOnlyList<MediaItem> Entries { get; }

        /// <summary>表示件数。</summary>
        int Count { get; }

        /// <summary>いま表示している種別。</summary>
        IReadOnlyList<MediaType> VisibleTypes { get; }

        /// <summary>並び順。</summary>
        LibrarySortOrder SortOrder { get; set; }

        /// <summary>index 番目のメディア。範囲外なら null。</summary>
        MediaItem GetAt(int index);

        /// <summary>その ID が一覧の何番目にあるか。無ければ -1。</summary>
        int IndexOf(string mediaId);

        // ───────── 選択 ─────────

        /// <summary>選択中の位置。未選択は -1。</summary>
        int SelectedIndex { get; }

        /// <summary>選択中のメディア。未選択は null。</summary>
        MediaItem SelectedItem { get; }

        /// <summary>
        /// 選択中の MediaId。未選択は null。
        /// <b>上位へ渡してよいのはこれだけ</b>です。
        /// </summary>
        string SelectedMediaId { get; }

        /// <summary>何か選ばれているか。</summary>
        bool HasSelection { get; }

        /// <summary>index 番目を選ぶ。範囲外なら何もしない。</summary>
        /// <returns>選べたら true。</returns>
        bool Select(int index);

        /// <summary>ID で選ぶ。一覧に無ければ何もしない。</summary>
        /// <returns>選べたら true。</returns>
        bool SelectById(string mediaId);

        /// <summary>選択を 1 つ後ろへ。未選択なら先頭を選ぶ。</summary>
        bool SelectNext(bool wrap = true);

        /// <summary>選択を 1 つ前へ。未選択なら末尾を選ぶ。</summary>
        bool SelectPrevious(bool wrap = true);

        /// <summary>選択を外す。</summary>
        void ClearSelection();

        // ───────── 通知 ─────────

        void AddObserver(IMediaLibraryObserver observer);

        void RemoveObserver(IMediaLibraryObserver observer);
    }
}
