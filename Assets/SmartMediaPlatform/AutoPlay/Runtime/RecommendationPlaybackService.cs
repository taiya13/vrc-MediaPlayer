using System;
using System.Collections.Generic;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;

namespace SmartMediaPlatform.AutoPlay
{
    /// <summary>
    /// <b>おすすめ再生ループを 1 本につなぐ接続層。</b>
    ///
    /// <code>
    /// RecommendationEngine → Queue → PlayerSession → BackendAdapter → VRChatVideoBackend
    /// </code>
    ///
    /// <b>Phase3-4 での整理</b>
    /// <list type="bullet">
    /// <item>
    /// 積む処理そのものは <see cref="RecommendationQueueRefiller"/>(Queue 層)へ移しました。
    /// プロジェクト全体で補充の実装は 1 つだけです。
    /// </item>
    /// <item>
    /// その実装を <c>PlayerSession.QueueRefiller</c> に<b>渡します</b>。
    /// Phase3-3 のように <c>AutoQueueEnabled</c> を外から false にすることはもうありません。
    /// セッションは今までどおり自分で補充し、<b>その手段だけが差し替わります</b>。
    /// </item>
    /// <item>
    /// おかげでこの クラス に残るのは「起点を決める」「ふるいを組む」「診断を見せる」だけです。
    /// </item>
    /// </list>
    ///
    /// <b>守っている責務</b>
    /// <list type="bullet">
    /// <item>Recommendation … 順位付けだけ。<b>MediaId しか受け取りません</b></item>
    /// <item>Queue … 並びだけ。積むものを選り好みしません</item>
    /// <item>PlayerSession … 再生と Ended の判断。役割は増えていません</item>
    /// <item>BackendAdapter / VideoBackend … 変更なし。URL を知るのは VideoBackend だけ</item>
    /// </list>
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public sealed class RecommendationPlaybackService
    {
        private readonly PlayerSession _session;
        private readonly IMediaCatalog _catalog;
        private readonly RecommendationQueueRefiller _refiller;

        private IBackendLogger _logger;

        /// <param name="session">再生を任せるセッション。</param>
        /// <param name="catalog">事前生成カタログ。</param>
        /// <param name="engine">おすすめエンジン(アルゴリズムは変更しません)。</param>
        /// <param name="filter">
        /// 積む直前のふるい。未指定なら <see cref="MediaTypePlaybackFilter"/>(Video / Live)。
        /// </param>
        /// <param name="logger">ログ出力先。</param>
        public RecommendationPlaybackService(
            PlayerSession session,
            IMediaCatalog catalog,
            IRecommendationEngine engine,
            IPlaybackFilter filter = null,
            IBackendLogger logger = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            _logger = logger ?? NullBackendLogger.Instance;

            Filter = filter ?? new MediaTypePlaybackFilter();

            _refiller = new RecommendationQueueRefiller(catalog, engine, Filter)
            {
                RecentMemory = 5,
                AllowRepeatWhenExhausted = true,
                AllowCatalogFallback = true,
            };

            // ★ Phase3-4: セッションの設定を書き換えるのではなく、補充の手段を渡す。
            _session.QueueRefiller = _refiller;
        }

        // ───────── 設定 ─────────

        /// <summary>積む直前のふるい。</summary>
        public IPlaybackFilter Filter { get; }

        /// <summary>実際に積む処理(セッションにも渡してある同じ実体)。</summary>
        public RecommendationQueueRefiller Refiller => _refiller;

        /// <summary>直近この件数に積んだ動画は積み直さない。</summary>
        public int RecentMemory
        {
            get => _refiller.RecentMemory;
            set => _refiller.RecentMemory = value;
        }

        /// <summary>候補が尽きたとき、直近の除外を緩めてでも積み続けるか。</summary>
        public bool AllowRepeatWhenExhausted
        {
            get => _refiller.AllowRepeatWhenExhausted;
            set => _refiller.AllowRepeatWhenExhausted = value;
        }

        /// <summary>おすすめが 1 件も返せないとき、カタログから補うか。</summary>
        public bool AllowCatalogFallback
        {
            get => _refiller.AllowCatalogFallback;
            set => _refiller.AllowCatalogFallback = value;
        }

        /// <summary>この数を下回ったら補充する(セッションの設定に委譲)。</summary>
        public int MinimumQueueCount
        {
            get => _session.MinimumQueueCount;
            set => _session.MinimumQueueCount = value;
        }

        /// <summary>補充後に目指す Queue の長さ(セッションの設定に委譲)。</summary>
        public int TargetQueueCount
        {
            get => _session.TargetQueueCount;
            set => _session.TargetQueueCount = value;
        }

        // ───────── 状態(診断用) ─────────

        /// <summary>最初に再生した動画の ID。おすすめの起点。</summary>
        public string SeedId { get; private set; }

        /// <summary>補充を試みた回数。</summary>
        public int RefillCount => _refiller.RefillCount;

        /// <summary>おすすめによって積んだ件数の累計。</summary>
        public int EnqueuedByRecommendation => _refiller.EnqueuedByRecommendation;

        /// <summary>おすすめが尽きてカタログから補った件数の累計。</summary>
        public int EnqueuedByCatalogFallback => _refiller.EnqueuedByCatalogFallback;

        /// <summary>ふるいに弾かれた候補の累計。</summary>
        public int FilteredOutCount => _refiller.FilteredOutCount;

        /// <summary>直近に積んだ ID(新しいものが末尾)。</summary>
        public IReadOnlyList<string> Recent => _refiller.Recent;

        public void SetLogger(IBackendLogger logger)
        {
            _logger = logger ?? NullBackendLogger.Instance;
        }

        // ───────── 組み立て ─────────

        /// <summary>バックエンドをセッションに登録する。</summary>
        public void RegisterBackend(IMediaBackend backend)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            _session.RegisterBackend(backend);
        }

        // ───────── 再生ループ ─────────

        /// <summary>
        /// おすすめ再生を開始する。
        ///
        /// <list type="number">
        /// <item>起点の動画を決める(指定が無ければカタログの最初の再生可能な動画)</item>
        /// <item>起点だけを曲一覧に置き、Queue を作り直す</item>
        /// <item><c>PlayerSession.Play()</c> で再生を始める
        /// — 残りは<b>セッション自身が</b>おすすめで埋める</item>
        /// </list>
        /// </summary>
        /// <param name="seedMediaId">起点にする Catalog の ID。null なら自動で選ぶ。</param>
        /// <returns>Queue に積んだ件数。起点を選べなければ 0。</returns>
        public int Start(string seedMediaId = null)
        {
            SeedId = ResolveSeed(seedMediaId);
            if (SeedId == null)
            {
                Log("再生を開始できません: 再生できる動画がカタログにありません");
                return 0;
            }

            _refiller.ForgetRecent();
            _session.SetTracks(new[] { SeedId });

            // セッションが自分で補充する(手段はこの クラス が渡した refiller)
            int added = _session.EnsureQueueFilled();

            Log($"おすすめ再生を開始します(起点: {SeedId}, Queue: {_session.Queue.Count} 本)");
            _session.Play();
            return _session.Queue.Count > 0 ? added + 1 : added;
        }

        /// <summary>
        /// Queue が不足していれば補充する。毎フレーム呼んで構いません。
        /// 実体は <c>PlayerSession.EnsureQueueFilled()</c>(= 渡した refiller)です。
        /// </summary>
        public int EnsureQueueFilled()
        {
            return _session.EnsureQueueFilled();
        }

        /// <summary>
        /// おすすめから<b>次に再生する MediaId を 1 件だけ</b>返す(Queue には積まない)。
        ///
        /// <b>戻り値は MediaId(string)だけ</b>です。
        /// <c>MediaItem</c> も URL も返さないので、この層から先へ再生用の URL が漏れません。
        /// </summary>
        public string PickNextMediaId(string seedId = null)
        {
            string seed = string.IsNullOrWhiteSpace(seedId) ? ResolveRefillSeed() : seedId;
            if (seed == null) return null;

            return _refiller.PickNext(_session.Queue, seed, _session.CurrentMediaId);
        }

        // ───────── 内部 ─────────

        /// <summary>指定 ID・カタログの順で、再生できる起点を決める。</summary>
        private string ResolveSeed(string requested)
        {
            if (!string.IsNullOrWhiteSpace(requested))
            {
                var item = _catalog.FindById(requested);
                if (item != null && Filter.CanPlay(item)) return item.Id;

                Log($"起点 {requested} は再生できないので、カタログから選び直します");
            }

            var all = _catalog.GetAll();
            for (int i = 0; i < all.Count; i++)
            {
                if (Filter.CanPlay(all[i])) return all[i].Id;
            }
            return null;
        }

        /// <summary>「再生中 → Queue の末尾 → 最初の起点」の順で種を決める。</summary>
        private string ResolveRefillSeed()
        {
            string current = _session.CurrentMediaId;
            if (!string.IsNullOrWhiteSpace(current)) return current;

            var queue = _session.Queue;
            if (queue.Count > 0)
            {
                var tail = queue.PeekAt(queue.Count - 1);
                if (tail != null && tail.Item != null) return tail.Item.Id;
            }

            return SeedId;
        }

        private void Log(string message)
        {
            _logger.Log($"[RecommendationPlayback] {message}");
        }

        /// <summary>Console 用のまとめ。</summary>
        public string Describe()
        {
            return $"seed={SeedId ?? "(none)"} queue={_session.Queue.Count} "
                   + $"refills={RefillCount} recommended={EnqueuedByRecommendation} "
                   + $"fallback={EnqueuedByCatalogFallback} filtered={FilteredOutCount} "
                   + $"filter={Filter}";
        }

        public override string ToString() => Describe();
    }
}
