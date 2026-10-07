using System;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Store;
using SmartMediaPlatform.Session;

namespace SmartMediaPlatform.Library.Playback
{
    /// <summary>
    /// <b>Library → Queue → Player を 1 本につないだ組み立て役。</b>Phase4-3 の入口。
    ///
    /// <code>
    /// MediaLibrary ─┐
    /// RelatedMediaView ─┼→ LibraryPlaybackBridge → PlayerSession → Queue → Backend
    ///               │                                  │
    ///               └──────── QueueView ───────────────┤
    ///                         NowPlayingView ──────────┘
    /// </code>
    ///
    /// <b>このクラス自身は再生の判断を持ちません。</b>
    /// やることは 3 つだけです。
    /// <list type="number">
    /// <item><b>組み立て</b> — 部品を作って繋ぐ(PoC で毎回書かずに済むように)</item>
    /// <item><b>同期</b> — <see cref="Tick"/> で Queue の表示と再生中の表示を追従させる</item>
    /// <item><b>関連の起点の追従</b> — 再生が変わったら関連一覧の起点も変える</item>
    /// </list>
    ///
    /// 3 番目だけが「判断」ですが、これは<b>画面の都合</b>であって再生制御ではありません。
    /// Phase4-2 まで <c>MonoBehaviour</c> に書いていたものを、
    /// 純粋 C# に移して<b>テストできるように</b>しました。
    ///
    /// <b>再生の中身には一切触れていません。</b>
    /// 次に何を再生するかは <see cref="PlayerSession"/>、
    /// 積むものは <c>IQueueRefiller</c>、
    /// 実際の再生は <c>BackendAdapter</c> — Phase1〜3 のままです。
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public sealed class PlaybackFlow
    {
        private readonly PlayerSession _session;

        /// <summary>
        /// 一式を組み立てる。
        /// </summary>
        /// <param name="store">データを引く唯一の窓口。</param>
        /// <param name="session">再生を任せるセッション(組み立て済み)。</param>
        /// <param name="related">
        /// 関連 ID の出どころ。null なら関連一覧は空のままになります
        /// (<c>CatalogRelatedMediaProvider</c> か <c>StaticRelatedMediaProvider</c> を渡してください)。
        /// </param>
        /// <param name="relatedCount">関連を何件まで出すか。0 以下で制限なし。</param>
        /// <param name="logger">ログ出力先。</param>
        public PlaybackFlow(
            ICatalogStore store,
            PlayerSession session,
            IRelatedMediaProvider related = null,
            int relatedCount = 5,
            IBackendLogger logger = null)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            _session = session ?? throw new ArgumentNullException(nameof(session));

            Library = new MediaLibrary(Store);
            Queue = new QueueView(Store, session.Queue);
            NowPlaying = new NowPlayingView(Store, session);

            RelatedProvider = related ?? new StaticRelatedMediaProvider();
            Related = new RelatedMediaView(Store, RelatedProvider, relatedCount);

            // 一覧と再生をつなぐのは 1 クラスだけ。どの一覧からでも同じ形で渡せる。
            LibraryPlayback = new LibraryPlaybackBridge(Library, session, logger);
            RelatedPlayback = new LibraryPlaybackBridge(Related, session, logger);
            QueuePlayback = new LibraryPlaybackBridge(Queue, session, logger);
        }

        /// <summary>
        /// カタログから丸ごと組み立てる近道(PoC 用)。
        /// 関連はカタログの <c>RelatedIds</c> を使い、
        /// あとからサーバー応答へ差し替えられるよう <c>StaticRelatedMediaProvider</c> で包みます。
        /// </summary>
        public static PlaybackFlow Create(
            IMediaCatalog catalog,
            PlayerSession session,
            int relatedCount = 5,
            IBackendLogger logger = null)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            var store = new CatalogStore(catalog);
            var related = new StaticRelatedMediaProvider(new CatalogRelatedMediaProvider(catalog));

            return new PlaybackFlow(store, session, related, relatedCount, logger);
        }

        // ───────── 部品 ─────────

        /// <summary>データを引く唯一の窓口。</summary>
        public ICatalogStore Store { get; }

        /// <summary>カタログ全体の一覧。</summary>
        public MediaLibrary Library { get; }

        /// <summary>関連動画の一覧。</summary>
        public RelatedMediaView Related { get; }

        /// <summary>関連 ID の出どころ(サーバー連携の差し替え口)。</summary>
        public IRelatedMediaProvider RelatedProvider { get; }

        /// <summary>Queue の中身。</summary>
        public QueueView Queue { get; }

        /// <summary>いま再生しているもの。</summary>
        public NowPlayingView NowPlaying { get; }

        /// <summary>再生を任せているセッション。</summary>
        public PlayerSession Session => _session;

        /// <summary>カタログ全体の一覧から再生へ渡す役。</summary>
        public LibraryPlaybackBridge LibraryPlayback { get; }

        /// <summary>関連動画の一覧から再生へ渡す役。</summary>
        public LibraryPlaybackBridge RelatedPlayback { get; }

        /// <summary>Queue の一覧から再生へ渡す役(「この曲へ飛ぶ」)。</summary>
        public LibraryPlaybackBridge QueuePlayback { get; }

        // ───────── 設定 ─────────

        /// <summary>
        /// 関連一覧の起点を、再生中のものに追従させるか。
        ///
        /// <list type="bullet">
        /// <item><b>true(既定)</b> … 再生が変わると関連も入れ替わる(関連 → 関連 とたどれる)</item>
        /// <item><b>false</b> … 起点は <c>Related.SetSource()</c> で自分で決める</item>
        /// </list>
        /// </summary>
        public bool RelatedFollowsPlayback { get; set; } = true;

        /// <summary>
        /// 何も再生していないとき、一覧で選んでいるものを関連の起点にするか。
        /// 「まだ再生していないが、選んだものの関連を見たい」場面のためです。
        /// </summary>
        public bool RelatedFollowsSelection { get; set; } = true;

        // ───────── 同期 ─────────

        /// <summary>直近の <see cref="Tick"/> で何か変わったか(診断用)。</summary>
        public int SyncCount { get; private set; }

        /// <summary>
        /// <b>毎フレーム呼んでよい同期。</b>
        /// Queue の表示・再生中の表示・関連の起点を、いまの状態に合わせます。
        ///
        /// 変化が無ければ何もしないので、毎フレーム呼んでも負荷になりません。
        /// </summary>
        /// <returns>何か変わったら true。</returns>
        public bool Tick()
        {
            bool changed = Queue.Sync();

            if (NowPlaying.Poll())
            {
                changed = true;
                if (RelatedFollowsPlayback) FollowSource();
            }

            if (changed) SyncCount++;
            return changed;
        }

        /// <summary>
        /// 関連一覧の起点を決め直す。
        /// 一覧の選択を変えたあとなど、<see cref="Tick"/> を待たずに反映したいときに呼びます。
        /// </summary>
        public void FollowSource()
        {
            string source = null;

            if (RelatedFollowsPlayback) source = _session.CurrentMediaId;
            if (source == null && RelatedFollowsSelection) source = Library.SelectedMediaId;

            Related.SetSource(source);
        }

        // ───────── Console 用のまとめ ─────────

        public string Describe()
        {
            return $"Library {Library.Count} 件 / 関連 {Related.Count} 件 "
                   + $"/ Queue {Queue.Count} 件 / {NowPlaying.Describe()}";
        }

        public override string ToString() => Describe();
    }
}
