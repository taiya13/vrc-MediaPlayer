using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Library
{
    /// <summary>
    /// <b>一覧と選択の変化を受け取る側。</b>
    ///
    /// UI(Console / uGUI / VRChat の Canvas)はこれを実装して
    /// <see cref="MediaLibrary.AddObserver"/> で登録します。
    /// <b>Library 側は UI を一切知りません</b> — 知らせるだけです。
    ///
    /// C# の <c>event</c> ではなく インターフェース にしてあるのは、
    /// Phase1-5(<c>IBackendObserver</c>)から通している方針です
    /// (将来 UdonSharp へ移すときに書き換えずに済ませるため)。
    /// </summary>
    public interface IMediaLibraryObserver
    {
        /// <summary>
        /// 表示する一覧が変わった(絞り込み・並び順・カタログの読み直し)。
        /// 選択も外れている場合があるので、UI は一覧ごと描き直してください。
        /// </summary>
        void OnLibraryChanged(IMediaLibrary library);

        /// <summary>
        /// 選択が変わった。
        /// </summary>
        /// <param name="library">通知元。</param>
        /// <param name="selected">選ばれたメディア。選択が外れたときは null。</param>
        void OnSelectionChanged(IMediaLibrary library, MediaItem selected);
    }
}
