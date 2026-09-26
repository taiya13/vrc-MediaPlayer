using System;
using System.Collections.Generic;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Recommendation;

namespace SmartMediaPlatform.Queue
{
    /// <summary>
    /// <b>おすすめで Queue を補充する、唯一の実装。</b>
    ///
    /// Phase3-4 の整理の中心です。Phase3-3 まで 4 箇所に散らばっていた補充処理を
    /// ここへ集約しました。呼び出し側に残るのは「種(seed)の決め方」だけです。
    ///
    /// <b>積む手順(3 段構え)</b>
    /// <list type="number">
    /// <item>おすすめ順に見て、積めるものを積む(直近に積んだものは避ける)</item>
    /// <item>足りなければ、直近の除外を緩めてもう一度
    /// (<see cref="AllowRepeatWhenExhausted"/>)</item>
    /// <item>それでも足りなければ、カタログの並び順から補う
    /// (<see cref="AllowCatalogFallback"/> — おすすめが 1 件も返せない異常時の保険)</item>
    /// </list>
    ///
    /// <b>積まない条件</b>
    /// <list type="bullet">
    /// <item><see cref="Filter"/> が false を返す(再生できない種別・失敗直後 など)</item>
    /// <item>すでに Queue に入っている</item>
    /// <item><c>excludeMediaId</c>(通常は再生中のもの)と同じ</item>
    /// <item>直近 <see cref="RecentMemory"/> 件に積んだばかり(1 段目のみ)</item>
    /// </list>
    ///
    /// <b>おすすめの順位付けには一切触れません。</b>
    /// <see cref="IRecommendationEngine"/> が返した並びのまま、上から見ていくだけです。
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public sealed class RecommendationQueueRefiller : IQueueRefiller
    {
        private readonly IMediaCatalog _catalog;
        private readonly IRecommendationEngine _engine;

        /// <summary>直近に積んだ ID(新しいものが末尾)。</summary>
        private readonly List<string> _recent = new List<string>();

        private readonly IReadOnlyList<string> _readOnlyRecent;

        /// <param name="catalog">事前生成カタログ。</param>
        /// <param name="engine">おすすめエンジン(順位付けは変更しません)。</param>
        /// <param name="filter">積む直前のふるい。null なら何でも積む。</param>
        public RecommendationQueueRefiller(
            IMediaCatalog catalog,
            IRecommendationEngine engine,
            IPlaybackFilter filter = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            Filter = filter;
            _readOnlyRecent = _recent.AsReadOnly();
        }

        // ───────── 設定 ─────────

        /// <summary>積む直前のふるい。null なら素通し。</summary>
        public IPlaybackFilter Filter { get; set; }

        /// <summary>直近この件数に積んだものは、1 段目では積み直さない。0 で無効。</summary>
        public int RecentMemory { get; set; } = 5;

        /// <summary>
        /// 候補が尽きたとき、直近の除外を緩めてでも積み続けるか。
        /// false にすると、一巡した時点で補充が止まります。
        /// </summary>
        public bool AllowRepeatWhenExhausted { get; set; }

        /// <summary>
        /// おすすめが 1 件も返せなかったとき、カタログの並び順から補うか。
        /// おすすめが働いているかを確かめたいときは false にしてください。
        /// </summary>
        public bool AllowCatalogFallback { get; set; }

        /// <summary>Queue に積むときの由来。</summary>
        public QueueItemSource Source { get; set; } = QueueItemSource.Recommendation;

        // ───────── 診断 ─────────

        /// <summary>直近に積んだ ID(新しいものが末尾)。</summary>
        public IReadOnlyList<string> Recent => _readOnlyRecent;

        /// <summary>おすすめによって積んだ件数の累計。</summary>
        public int EnqueuedByRecommendation { get; private set; }

        /// <summary>おすすめが尽きてカタログから補った件数の累計。</summary>
        public int EnqueuedByCatalogFallback { get; private set; }

        /// <summary><see cref="Filter"/> に弾かれた候補の累計。</summary>
        public int FilteredOutCount { get; private set; }

        /// <summary>補充を試みた回数。</summary>
        public int RefillCount { get; private set; }

        // ───────── 補充 ─────────

        public int Refill(
            IQueue queue,
            string seedMediaId,
            int wanted,
            string excludeMediaId = null,
            int candidateCount = 0)
        {
            if (queue == null || wanted <= 0) return 0;

            RefillCount++;

            int added = FromRecommendations(
                queue, seedMediaId, wanted, excludeMediaId, candidateCount, avoidRecent: true);

            if (added < wanted && AllowRepeatWhenExhausted)
            {
                added += FromRecommendations(
                    queue, seedMediaId, wanted - added, excludeMediaId, candidateCount,
                    avoidRecent: false);
            }

            if (added < wanted && AllowCatalogFallback)
            {
                added += FromCatalog(queue, wanted - added, excludeMediaId);
            }

            return added;
        }

        public string PickNext(IQueue queue, string seedMediaId, string excludeMediaId = null)
        {
            string picked = PickFromRecommendations(queue, seedMediaId, excludeMediaId, avoidRecent: true);
            if (picked != null) return picked;

            if (AllowRepeatWhenExhausted)
            {
                picked = PickFromRecommendations(queue, seedMediaId, excludeMediaId, avoidRecent: false);
                if (picked != null) return picked;
            }

            return null;
        }

        public void ForgetRecent()
        {
            _recent.Clear();
        }

        /// <summary>診断カウンタを 0 に戻す(積んだ内容には触れない)。</summary>
        public void ResetDiagnostics()
        {
            EnqueuedByRecommendation = 0;
            EnqueuedByCatalogFallback = 0;
            FilteredOutCount = 0;
            RefillCount = 0;
        }

        // ───────── 内部 ─────────

        private int FromRecommendations(
            IQueue queue, string seedMediaId, int wanted,
            string excludeMediaId, int candidateCount, bool avoidRecent)
        {
            if (wanted <= 0 || string.IsNullOrWhiteSpace(seedMediaId)) return 0;

            int take = candidateCount > 0 ? candidateCount : _catalog.Count;

            // おすすめの順位付けはそのまま使う(アルゴリズムは変更しない)
            var ranked = _engine.GetNextRecommendations(seedMediaId, take);
            if (ranked == null || ranked.Length == 0) return 0;

            int added = 0;
            for (int i = 0; i < ranked.Length && added < wanted; i++)
            {
                var item = ranked[i].Item;
                if (item == null) continue;

                if (Filter != null && !Filter.CanPlay(item))
                {
                    FilteredOutCount++;
                    continue;
                }

                if (!IsAcceptable(queue, item.Id, excludeMediaId, avoidRecent)) continue;

                Enqueue(queue, item);
                EnqueuedByRecommendation++;
                added++;
            }

            return added;
        }

        /// <summary>おすすめが尽きたときの最後の手段(カタログの並び順)。</summary>
        private int FromCatalog(IQueue queue, int wanted, string excludeMediaId)
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
                    if (Filter != null && !Filter.CanPlay(item)) continue;
                    if (!IsAcceptable(queue, item.Id, excludeMediaId, avoidRecent)) continue;

                    Enqueue(queue, item);
                    EnqueuedByCatalogFallback++;
                    added++;
                }

                if (!AllowRepeatWhenExhausted) break;
            }

            return added;
        }

        private string PickFromRecommendations(
            IQueue queue, string seedMediaId, string excludeMediaId, bool avoidRecent)
        {
            if (string.IsNullOrWhiteSpace(seedMediaId)) return null;

            var ranked = _engine.GetNextRecommendations(seedMediaId, _catalog.Count);
            if (ranked == null) return null;

            for (int i = 0; i < ranked.Length; i++)
            {
                var item = ranked[i].Item;
                if (item == null) continue;
                if (Filter != null && !Filter.CanPlay(item)) continue;
                if (!IsAcceptable(queue, item.Id, excludeMediaId, avoidRecent)) continue;

                return item.Id;   // ← MediaId だけを返す
            }

            return null;
        }

        private bool IsAcceptable(IQueue queue, string mediaId, string excludeMediaId, bool avoidRecent)
        {
            if (string.IsNullOrWhiteSpace(mediaId)) return false;
            if (Same(mediaId, excludeMediaId)) return false;
            if (queue.Contains(mediaId)) return false;
            if (avoidRecent && IsRecent(mediaId)) return false;
            return true;
        }

        private void Enqueue(IQueue queue, MediaItem item)
        {
            queue.Enqueue(item, Source);
            RememberRecent(item.Id);
        }

        private void RememberRecent(string mediaId)
        {
            _recent.Add(mediaId);
            while (RecentMemory > 0 && _recent.Count > RecentMemory) _recent.RemoveAt(0);
        }

        private bool IsRecent(string mediaId)
        {
            if (RecentMemory <= 0) return false;

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

        public override string ToString()
        {
            return $"RecommendationQueueRefiller(filter: {Filter?.ToString() ?? "なし"}, "
                   + $"recent: {RecentMemory}, repeat: {AllowRepeatWhenExhausted}, "
                   + $"fallback: {AllowCatalogFallback})";
        }
    }
}
