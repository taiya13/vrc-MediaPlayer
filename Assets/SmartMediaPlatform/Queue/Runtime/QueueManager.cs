using System;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Recommendation;

namespace SmartMediaPlatform.Queue
{
    /// <summary>
    /// Queue と Recommendation Engine を仲介し、「キューが尽きそうなら自動で補充する」役割だけを担う。
    ///
    /// 重要:これは自動“再生”ではない。曲を積むところまでしか行わず、再生は一切行わない
    /// (Player は Phase1-5 以降)。
    ///
    /// 依存は Catalog と Recommendation のみ。Queue 本体(<see cref="MediaQueue"/>)を
    /// 汚さずに補充ポリシーを分離することで、将来
    /// 「DJ Mode では補充しない」「Shared Queue ではホストだけが補充する」といった
    /// 差し替えがこのクラスの置き換えだけで済む。
    ///
    /// <b>Phase3-4:</b> 実際に積む処理は <see cref="RecommendationQueueRefiller"/> へ移しました
    /// (補充の実装をプロジェクト全体で 1 つに保つため)。
    /// このクラスに残っているのは<b>「種(seed)の決め方」と「いつ補充するか」</b>だけです。
    /// 公開 API と挙動は Phase1-4 から変わっていません。
    /// </summary>
    public sealed class QueueManager
    {
        private readonly IQueue _queue;
        private readonly IMediaCatalog _catalog;
        private readonly RecommendationQueueRefiller _refiller;

        /// <summary>この数を下回ったら補充する(既定 2)。</summary>
        public int MinimumCount { get; set; } = 2;

        /// <summary>補充後に目指す要素数(既定 5)。</summary>
        public int TargetCount { get; set; } = 5;

        /// <summary>
        /// 直近にキューから取り除かれた曲の ID。
        /// キューが空になっても「直前の曲」を種にして補充を続けられるようにする。
        /// </summary>
        public string LastRemovedId { get; private set; }

        public QueueManager(IQueue queue, IRecommendationEngine engine, IMediaCatalog catalog)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

            // Phase1-4 の挙動をそのまま再現する設定:
            // ふるい無し・直近の除外無し・緩和無し・カタログ補完無し。
            _refiller = new RecommendationQueueRefiller(catalog, engine)
            {
                RecentMemory = 0,
                AllowRepeatWhenExhausted = false,
                AllowCatalogFallback = false,
                Source = QueueItemSource.Recommendation,
            };
        }

        public IQueue Queue => _queue;

        /// <summary>実際に積む処理(プロジェクト全体で共有している実装)。</summary>
        public IQueueRefiller Refiller => _refiller;

        /// <summary>
        /// キューが <see cref="MinimumCount"/> を下回っていれば、
        /// Recommendation Engine のおすすめで <see cref="TargetCount"/> まで補充する。
        /// 戻り値は実際に追加した件数(補充不要なら 0)。
        /// </summary>
        public int EnsureFilled()
        {
            if (_queue.Count >= MinimumCount) return 0;
            return Refill(TargetCount - _queue.Count);
        }

        /// <summary>
        /// 種を明示して count 件ぶん補充する(初期投入にも使える)。
        /// すでにキューにある曲は積まない。戻り値は実際に追加した件数。
        /// </summary>
        public int Fill(string seedId, int count)
        {
            return AddRecommendations(seedId, count);
        }

        /// <summary>
        /// 先頭を取り出し、その後に必要なら補充する。
        /// 取り出した曲を返す(空なら null)。再生は行わない。
        /// </summary>
        public QueueItem DequeueAndRefill()
        {
            var removed = _queue.Dequeue();
            if (removed != null) LastRemovedId = removed.MediaId;

            EnsureFilled();
            return removed;
        }

        /// <summary>
        /// 先頭を捨てて次へ進み、その後に必要なら補充する。
        /// 戻り値は新しい先頭(= 次の Now)。再生は行わない。
        /// </summary>
        public QueueItem SkipAndRefill()
        {
            var skipped = _queue.Peek();
            _queue.Skip();
            if (skipped != null) LastRemovedId = skipped.MediaId;

            EnsureFilled();
            return _queue.Peek();
        }

        private int Refill(int count)
        {
            return AddRecommendations(ResolveSeedId(), count);
        }

        /// <summary>
        /// 補充の種を決める。継続性を優先し、
        /// 「キューの末尾 → 直近に取り除いた曲 → カタログからランダム」の順に選ぶ。
        /// </summary>
        private string ResolveSeedId()
        {
            var tail = _queue.PeekAt(_queue.Count - 1);
            if (tail != null) return tail.MediaId;

            if (!string.IsNullOrEmpty(LastRemovedId)) return LastRemovedId;

            var random = _catalog.GetRandom();
            return random != null ? random.Id : null;
        }

        private int AddRecommendations(string seedId, int count)
        {
            if (count <= 0 || string.IsNullOrEmpty(seedId)) return 0;

            // 既にキューにある曲を避けるため、必要数より多めに候補を取る
            // (Phase1-4 と同じ取り方を保つ)。
            return _refiller.Refill(
                _queue, seedId, count,
                excludeMediaId: null,
                candidateCount: count + _queue.Count);
        }
    }
}
