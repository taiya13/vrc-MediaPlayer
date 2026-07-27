using System;
using System.Collections.Generic;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;

namespace SmartMediaPlatform.AutoPlay
{
    /// <summary>
    /// <b>Phase3-3 の中心。おすすめ再生ループを 1 本につなぐ接続層。</b>
    ///
    /// <code>
    /// RecommendationEngine → Queue → PlayerSession → BackendAdapter → VRChatVideoBackend
    /// </code>
    ///
    /// <b>この クラス が足しているのは 1 つだけです:「積む直前に、再生できるか確かめる」</b>
    ///
    /// おすすめは Catalog 全体から候補を返すので、Video バックエンドしか登録していない
    /// 構成では Music が混ざります。<c>PlayerSession.EnsureQueueFilled()</c> の
    /// おすすめ補充には種別の判定が無いため、そのまま積むと
    /// <c>BackendManager.LoadCurrent()</c> が失敗して再生が止まります。
    /// そこで <see cref="IPlaybackFilter"/> を通してから <c>PlayerSession.Enqueue</c> します。
    ///
    /// <b>既存の責務は 1 つも動かしていません</b>
    /// <list type="bullet">
    /// <item><b>RecommendationEngine</b> … 順位付けはそのまま。<b>MediaId しか受け取りません</b></item>
    /// <item><b>Queue</b> … 並びを持つだけ。積むものを選り好みしません</item>
    /// <item><b>PlayerSession</b> … 再生と Ended の受け取りはそのまま。1 行も変更していません</item>
    /// <item><b>BackendAdapter / VideoBackend</b> … 変更していません。URL(VRCUrl)を知るのは
    /// 引き続き VideoBackend だけです</item>
    /// </list>
    ///
    /// <b>PlayerSession 側のおすすめ補充は切ります</b>(<c>AutoQueueEnabled = false</c>)。
    /// 補充する場所を 2 つに増やすと、フィルタを通る経路と通らない経路が混ざるためです。
    /// 「補充は 1 箇所」を保つのがこの設計の要点です。
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public sealed class RecommendationPlaybackService : IBackendObserver
    {
        private readonly PlayerSession _session;
        private readonly IMediaCatalog _catalog;
        private readonly RecommendationEngine _engine;
        private readonly IPlaybackFilter _filter;

        /// <summary>直近に積んだ ID。同じ動画がすぐに繰り返されないように覚えておく。</summary>
        private readonly List<string> _recent = new List<string>();

        private readonly IReadOnlyList<string> _readOnlyRecent;

        private IBackendLogger _logger;

        /// <param name="session">再生を任せるセッション(変更しません)。</param>
        /// <param name="catalog">事前生成カタログ。</param>
        /// <param name="engine">おすすめエンジン(アルゴリズムは変更しません)。</param>
        /// <param name="filter">
        /// 積む直前の再生可否判定。未指定なら <see cref="MediaTypePlaybackFilter"/>(Video / Live)。
        /// </param>
        /// <param name="logger">ログ出力先。</param>
        /// <param name="takeOverAutoQueue">
        /// true(既定)なら <c>PlayerSession.AutoQueueEnabled</c> を false にして、
        /// おすすめ補充をこの クラス に一本化する。
        /// </param>
        public RecommendationPlaybackService(
            PlayerSession session,
            IMediaCatalog catalog,
            RecommendationEngine engine,
            IPlaybackFilter filter = null,
            IBackendLogger logger = null,
            bool takeOverAutoQueue = true)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _filter = filter ?? new MediaTypePlaybackFilter();
            _logger = logger ?? NullBackendLogger.Instance;
            _readOnlyRecent = _recent.AsReadOnly();

            TakesOverAutoQueue = takeOverAutoQueue;
            if (takeOverAutoQueue)
            {
                // 補充の入口を 1 つに保つ。
                // (PlayerSession 側の補充にはフィルタが無いので、両方動くと混ざる)
                _session.AutoQueueEnabled = false;
            }
        }

        // ───────── 設定 ─────────

        /// <summary>この数を下回ったら補充する。</summary>
        public int MinimumQueueCount { get; set; } = 2;

        /// <summary>補充後に目指す Queue の長さ。</summary>
        public int TargetQueueCount { get; set; } = 5;

        /// <summary>直近この件数に積んだ動画は、もう一度積まない。</summary>
        public int RecentMemory { get; set; } = 5;

        /// <summary>
        /// 積める候補が尽きたとき、直近の除外を緩めてでも積み続けるか。
        /// false にすると、カタログを一巡した時点で再生が止まります。
        /// </summary>
        public bool AllowRepeatWhenExhausted { get; set; } = true;

        /// <summary>
        /// おすすめが 1 件も返せなかったとき、カタログから直接補うか。
        /// おすすめが働いているかを確かめたい場合は false にしてください。
        /// </summary>
        public bool AllowCatalogFallback { get; set; } = true;

        /// <summary><c>PlayerSession</c> のおすすめ補充を引き取っているか。</summary>
        public bool TakesOverAutoQueue { get; }

        // ───────── 状態(診断用) ─────────

        /// <summary>最初に再生した動画の ID。おすすめの起点。</summary>
        public string SeedId { get; private set; }

        /// <summary>補充を行った回数。</summary>
        public int RefillCount { get; private set; }

        /// <summary>おすすめによって Queue に積んだ件数の累計。</summary>
        public int EnqueuedByRecommendation { get; private set; }

        /// <summary>おすすめが尽きてカタログから補った件数の累計。</summary>
        public int EnqueuedByCatalogFallback { get; private set; }

        /// <summary>再生できない種別だとして積まなかった候補の累計。</summary>
        public int FilteredOutCount { get; private set; }

        /// <summary>Ended を受け取った回数。</summary>
        public int EndedCount { get; private set; }

        /// <summary>直近に積んだ ID(新しいものが末尾)。</summary>
        public IReadOnlyList<string> Recent => _readOnlyRecent;

        /// <summary>使っている再生可否判定。</summary>
        public IPlaybackFilter Filter => _filter;

        public void SetLogger(IBackendLogger logger)
        {
            _logger = logger ?? NullBackendLogger.Instance;
        }

        // ───────── 組み立て ─────────

        /// <summary>
        /// バックエンドを登録する。
        ///
        /// <b>この クラス を先に、<c>PlayerSession</c> を後に購読させます。</b>
        /// Ended の通知は登録順に流れるので、
        /// <c>PlayerSession.Next()</c> が動くより前にここで次の動画を積んでおけます。
        /// (Queue は常に先まで積んであるので順序に頼らなくても動きますが、
        ///  取りこぼしを無くすため順序も保証しておきます)
        /// </summary>
        public void RegisterBackend(IMediaBackend backend)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));

            backend.AddObserver(this);
            _session.RegisterBackend(backend);
        }

        // ───────── 再生ループ ─────────

        /// <summary>
        /// おすすめ再生を開始する。
        ///
        /// <list type="number">
        /// <item>起点の動画を決める(指定が無ければカタログの最初の再生可能な動画)</item>
        /// <item>Queue を作り直し、起点 + おすすめで <see cref="TargetQueueCount"/> まで積む</item>
        /// <item><c>PlayerSession.Play()</c> で再生を始める</item>
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

            _recent.Clear();
            _session.ClearQueue();

            int added = 0;
            if (EnqueueId(SeedId)) added++;

            added += Fill(TargetQueueCount - _session.Queue.Count);

            Log($"おすすめ再生を開始します(起点: {SeedId}, Queue: {_session.Queue.Count} 本)");
            _session.Play();
            return added;
        }

        /// <summary>
        /// Queue が <see cref="MinimumQueueCount"/> を下回っていれば、おすすめで補充する。
        /// 曲が変わったタイミング、あるいは毎フレーム呼んで構いません。
        /// </summary>
        /// <returns>積んだ件数。</returns>
        public int EnsureQueueFilled()
        {
            if (_session.Queue.Count >= MinimumQueueCount) return 0;

            int wanted = TargetQueueCount - _session.Queue.Count;
            return wanted > 0 ? Fill(wanted) : 0;
        }

        /// <summary>
        /// おすすめから<b>次に再生する MediaId を 1 件だけ</b>返す(Queue には積まない)。
        ///
        /// <b>戻り値は MediaId(string)だけ</b>です。
        /// <c>MediaItem</c> も URL も返さないので、この層から先へ
        /// 再生用の URL が漏れることはありません
        /// (URL を知るのは引き続き VideoBackend だけです)。
        /// </summary>
        /// <param name="seedId">起点。null なら現在の再生位置から自動で決める。</param>
        /// <returns>積める MediaId。見つからなければ null。</returns>
        public string PickNextMediaId(string seedId = null)
        {
            string seed = string.IsNullOrWhiteSpace(seedId) ? ResolveRefillSeed() : seedId;
            if (seed == null) return null;

            // 1 回目: 直近に積んだものを避ける
            string picked = PickFromRecommendations(seed, avoidRecent: true);
            if (picked != null) return picked;

            // 2 回目: 尽きたので直近の除外を緩める
            if (AllowRepeatWhenExhausted)
            {
                picked = PickFromRecommendations(seed, avoidRecent: false);
                if (picked != null) return picked;
            }

            return null;
        }

        // ───────── Backend からの通知 ─────────

        /// <summary>
        /// 動画が終わったら、<b><c>PlayerSession</c> が次へ進む前に</b>次の候補を積んでおく。
        ///
        /// 「次に何を再生するか」を決めるのは引き続き <c>PlayerSession</c> です。
        /// この クラス は「選べる状態にしておく」だけで、再生の指示は出しません。
        /// </summary>
        public void OnBackendEvent(BackendEvent backendEvent)
        {
            if (backendEvent.Type != BackendEventType.Ended) return;

            EndedCount++;
            int added = EnsureQueueFilled();
            if (added > 0)
            {
                Log($"Ended を受けて {added} 本を補充しました (Queue: {_session.Queue.Count})");
            }
        }

        // ───────── 内部 ─────────

        /// <summary>指定 ID・カタログの順で、再生できる起点を決める。</summary>
        private string ResolveSeed(string requested)
        {
            if (!string.IsNullOrWhiteSpace(requested))
            {
                var item = _catalog.FindById(requested);
                if (item != null && _filter.CanPlay(item)) return item.Id;

                Log($"起点 {requested} は再生できないので、カタログから選び直します");
            }

            var all = _catalog.GetAll();
            for (int i = 0; i < all.Count; i++)
            {
                if (_filter.CanPlay(all[i])) return all[i].Id;
            }
            return null;
        }

        /// <summary>
        /// 補充の起点を決める。継続性を優先し
        /// 「再生中 → Queue の末尾 → 最初の起点」の順に選ぶ。
        /// </summary>
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

        /// <summary>おすすめ(足りなければカタログ)で <paramref name="wanted"/> 件まで積む。</summary>
        private int Fill(int wanted)
        {
            if (wanted <= 0) return 0;

            string seed = ResolveRefillSeed();
            if (seed == null) return 0;

            RefillCount++;

            int added = FillFromRecommendations(seed, wanted, avoidRecent: true);

            if (added < wanted && AllowRepeatWhenExhausted)
            {
                added += FillFromRecommendations(seed, wanted - added, avoidRecent: false);
            }

            if (added < wanted && AllowCatalogFallback)
            {
                added += FillFromCatalog(wanted - added);
            }

            return added;
        }

        private int FillFromRecommendations(string seedId, int wanted, bool avoidRecent)
        {
            if (wanted <= 0) return 0;

            // RecommendationEngine の順位付けはそのまま使う(アルゴリズムは変更しない)
            var ranked = _engine.GetNextRecommendations(seedId, _catalog.Count);
            if (ranked.Length == 0) return 0;

            int added = 0;
            for (int i = 0; i < ranked.Length && added < wanted; i++)
            {
                var item = ranked[i].Item;

                // ★ ここが Phase3-3 で足した唯一の判断:
                //    「再生できないものは積まない」
                if (!_filter.CanPlay(item))
                {
                    FilteredOutCount++;
                    continue;
                }

                if (!IsAcceptable(item.Id, avoidRecent)) continue;

                if (EnqueueId(item.Id))
                {
                    EnqueuedByRecommendation++;
                    added++;
                }
            }
            return added;
        }

        /// <summary>おすすめが尽きたときの最後の手段(カタログの並び順)。</summary>
        private int FillFromCatalog(int wanted)
        {
            if (wanted <= 0) return 0;

            var all = _catalog.GetAll();
            int added = 0;

            for (int pass = 0; pass < 2 && added < wanted; pass++)
            {
                bool avoidRecent = pass == 0;
                for (int i = 0; i < all.Count && added < wanted; i++)
                {
                    var item = all[i];
                    if (!_filter.CanPlay(item)) continue;
                    if (!IsAcceptable(item.Id, avoidRecent)) continue;

                    if (EnqueueId(item.Id))
                    {
                        EnqueuedByCatalogFallback++;
                        added++;
                    }
                }

                if (!AllowRepeatWhenExhausted) break;
            }

            return added;
        }

        /// <summary>いま積んでよい ID か(再生中・Queue 内・直近を避ける)。</summary>
        private bool IsAcceptable(string mediaId, bool avoidRecent)
        {
            if (string.IsNullOrWhiteSpace(mediaId)) return false;
            if (Same(mediaId, _session.CurrentMediaId)) return false;
            if (_session.Queue.Contains(mediaId)) return false;
            if (avoidRecent && IsRecent(mediaId)) return false;
            return true;
        }

        /// <summary>おすすめから 1 件だけ選ぶ(<see cref="PickNextMediaId"/> 用)。</summary>
        private string PickFromRecommendations(string seedId, bool avoidRecent)
        {
            var ranked = _engine.GetNextRecommendations(seedId, _catalog.Count);
            for (int i = 0; i < ranked.Length; i++)
            {
                var item = ranked[i].Item;
                if (!_filter.CanPlay(item)) continue;
                if (!IsAcceptable(item.Id, avoidRecent)) continue;

                return item.Id;   // ← MediaId だけを返す
            }
            return null;
        }

        /// <summary>PlayerSession の口から Queue に積む(Queue を直接触らない)。</summary>
        private bool EnqueueId(string mediaId)
        {
            if (!_session.Enqueue(mediaId)) return false;

            RememberRecent(mediaId);
            return true;
        }

        private void RememberRecent(string mediaId)
        {
            _recent.Add(mediaId);
            while (RecentMemory > 0 && _recent.Count > RecentMemory) _recent.RemoveAt(0);
        }

        private bool IsRecent(string mediaId)
        {
            for (int i = 0; i < _recent.Count; i++)
            {
                if (Same(_recent[i], mediaId)) return true;
            }
            return false;
        }

        private static bool Same(string a, string b)
        {
            return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private void Log(string message)
        {
            _logger.Log($"[RecommendationPlayback] {message}");
        }

        /// <summary>Console 用のまとめ。</summary>
        public string Describe()
        {
            return $"seed={SeedId ?? "(none)"} queue={_session.Queue.Count} "
                   + $"ended={EndedCount} refills={RefillCount} "
                   + $"recommended={EnqueuedByRecommendation} "
                   + $"fallback={EnqueuedByCatalogFallback} "
                   + $"filtered={FilteredOutCount} "
                   + $"filter={_filter}";
        }

        public override string ToString() => Describe();
    }
}
