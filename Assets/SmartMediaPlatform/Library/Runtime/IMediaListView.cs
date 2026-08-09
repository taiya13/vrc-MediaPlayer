using System.Collections.Generic;
using SmartMediaPlatform.Catalog.Store;

namespace SmartMediaPlatform.Library
{
    /// <summary>
    /// <b>「並んでいるものを見て、1 つ選ぶ」だけ</b>の共通の形。
    ///
    /// Phase4-2 で <see cref="IMediaLibrary"/>(カタログ全体)から切り出しました。
    /// <b>関連動画の一覧も、カタログ全体の一覧も、UI から見れば同じもの</b>だからです。
    ///
    /// <list type="bullet">
    /// <item><see cref="MediaLibrary"/> … カタログ全体(絞り込み・並べ替えができる)</item>
    /// <item><see cref="RelatedMediaView"/> … ある 1 件に関連するもの</item>
    /// </list>
    ///
    /// おかげで UI の部品も <c>MediaLibraryFormatter</c> も
    /// <b>両方に対して同じコードで動きます</b>。
    ///
    /// <b>出てくるのは <see cref="DisplayMeta"/> と <see cref="PlayableRef"/> だけ</b>です。
    /// <c>MediaItem</c> も URL もこの契約からは出てきません。
    /// </summary>
    public interface IMediaListView
    {
        /// <summary>いま並んでいるもの。</summary>
        IReadOnlyList<DisplayMeta> Entries { get; }

        /// <summary>並んでいる件数。</summary>
        int Count { get; }

        /// <summary>index 番目。範囲外なら null。</summary>
        DisplayMeta GetAt(int index);

        /// <summary>その ID が何番目にあるか。無ければ -1。</summary>
        int IndexOf(string mediaId);

        // ───────── 選択 ─────────

        /// <summary>選択中の位置。未選択は -1。</summary>
        int SelectedIndex { get; }

        /// <summary>選択中の表示情報。未選択は null。</summary>
        DisplayMeta SelectedItem { get; }

        /// <summary>選択中の MediaId。未選択は null。</summary>
        string SelectedMediaId { get; }

        /// <summary>
        /// <b>選択中のものを再生系へ渡すための参照。</b>未選択なら <see cref="PlayableRef.None"/>。
        /// URL は入っていません。
        /// </summary>
        PlayableRef SelectedRef { get; }

        /// <summary>何か選ばれているか。</summary>
        bool HasSelection { get; }

        /// <summary>index 番目を選ぶ。範囲外なら何もしない。</summary>
        bool Select(int index);

        /// <summary>ID で選ぶ。並びに無ければ何もしない。</summary>
        bool SelectById(string mediaId);

        /// <summary>1 つ後ろへ。未選択なら先頭。</summary>
        bool SelectNext(bool wrap = true);

        /// <summary>1 つ前へ。未選択なら末尾。</summary>
        bool SelectPrevious(bool wrap = true);

        /// <summary>選択を外す。</summary>
        void ClearSelection();

        // ───────── 通知 ─────────

        void AddObserver(IMediaListObserver observer);

        void RemoveObserver(IMediaListObserver observer);
    }

    /// <summary>
    /// <b>並びと選択の変化を受け取る側。</b>
    ///
    /// UI(Console / IMGUI / uGUI / VRChat の Canvas)はこれを実装して登録します。
    /// <b>一覧側は UI を一切知りません</b> — 知らせるだけです。
    ///
    /// C# の <c>event</c> ではなく インターフェース にしてあるのは、
    /// Phase1-5(<c>IBackendObserver</c>)から通している方針です
    /// (将来 UdonSharp へ移すときに書き換えずに済ませるため)。
    /// </summary>
    public interface IMediaListObserver
    {
        /// <summary>
        /// 並びが変わった(絞り込み・並び順・起点の変更・読み直し)。
        /// 選択も外れている場合があるので、UI は並びごと描き直してください。
        /// </summary>
        void OnListChanged(IMediaListView view);

        /// <summary>選択が変わった。外れたときは <paramref name="selected"/> が null。</summary>
        void OnSelectionChanged(IMediaListView view, DisplayMeta selected);
    }
}
