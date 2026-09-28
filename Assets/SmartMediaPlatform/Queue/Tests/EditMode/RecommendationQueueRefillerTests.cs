using System;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Recommendation;

namespace SmartMediaPlatform.Queue.Tests
{
    /// <summary>
    /// Phase3-4: 補充処理の一本化(<see cref="RecommendationQueueRefiller"/>)の検証。
    ///
    /// ここで確かめたいのは<b>「補充の実装が 1 つになっても、
    /// これまでの呼び出し側それぞれの振る舞いを再現できる」</b>ことです。
    /// 設定 3 つ(<c>RecentMemory</c> / <c>AllowRepeatWhenExhausted</c> /
    /// <c>AllowCatalogFallback</c>)の組み合わせで、
    /// QueueManager 相当・PlayerSession 相当・おすすめ再生相当を作り分けます。
    ///
    /// 決定的にするためランダム補正は 0 にします。
    /// </summary>
    public sealed class RecommendationQueueRefillerTests
    {
        private IMediaCatalog _catalog;
        private RecommendationEngine _engine;
        private MediaQueue _queue;

        private static readonly RecommendationRule[] Deterministic =
        {
            RecommendationRule.Related(10.0),
            RecommendationRule.SameArtist(5.0),
            RecommendationRule.SameGenre(3.0),
            RecommendationRule.TagMatch(1.0),
            RecommendationRule.Random(0.0),
        };

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _engine = new RecommendationEngine(_catalog, Deterministic, new System.Random(99));
            _queue = new MediaQueue();
        }

        private string[] Ids() => _queue.GetAll().Select(x => x.MediaId).ToArray();

        private RecommendationQueueRefiller Refiller(
            IPlaybackFilter filter = null,
            int recentMemory = 0,
            bool allowRepeat = false,
            bool allowFallback = false)
        {
            return new RecommendationQueueRefiller(_catalog, _engine, filter)
            {
                RecentMemory = recentMemory,
                AllowRepeatWhenExhausted = allowRepeat,
                AllowCatalogFallback = allowFallback,
            };
        }

        // ───────── 前提 ─────────

        [Test]
        public void Constructor_RejectsNulls()
        {
            Assert.Throws<ArgumentNullException>(
                () => new RecommendationQueueRefiller(null, _engine));
            Assert.Throws<ArgumentNullException>(
                () => new RecommendationQueueRefiller(_catalog, null));
        }

        [Test]
        public void Refiller_IsTheQueueRefillerContract()
        {
            Assert.IsInstanceOf<IQueueRefiller>(Refiller());
        }

        [Test]
        public void Refill_AcceptsANullQueueWithoutThrowing()
        {
            Assert.AreEqual(0, Refiller().Refill(null, "music-001", 3));
        }

        [Test]
        public void Refill_DoesNothingWhenNothingIsWanted()
        {
            Assert.AreEqual(0, Refiller().Refill(_queue, "music-001", 0));
            Assert.AreEqual(0, _queue.Count);
        }

        [Test]
        public void Refill_DoesNothingWithoutASeed()
        {
            Assert.AreEqual(0, Refiller().Refill(_queue, null, 3));
            Assert.AreEqual(0, _queue.Count);
        }

        // ───────── QueueManager 相当の設定 ─────────

        [Test]
        public void Refill_KeepsTheRankOrderOfTheEngine()
        {
            int added = Refiller().Refill(
                _queue, "music-001", 3, excludeMediaId: null, candidateCount: 3 + _queue.Count);

            Assert.AreEqual(3, added);
            CollectionAssert.AreEqual(new[] { "music-002", "music-007", "video-001" }, Ids(),
                "おすすめの順位付けには手を入れていない");
        }

        [Test]
        public void Refill_MarksItemsAsRecommendation()
        {
            Refiller().Refill(_queue, "music-001", 3);

            Assert.IsTrue(_queue.GetAll().All(x => x.Source == QueueItemSource.Recommendation));
        }

        [Test]
        public void Refill_SkipsWhatIsAlreadyQueued()
        {
            var refiller = Refiller();
            refiller.Refill(_queue, "music-001", 2);
            string[] first = Ids();

            refiller.Refill(_queue, "music-001", 2);

            Assert.AreEqual(4, _queue.Count);
            CollectionAssert.IsSubsetOf(first, Ids());
            Assert.AreEqual(_queue.Count, Ids().Distinct().Count(), "同じ曲は二重に積まない");
        }

        [Test]
        public void Refill_SkipsTheExcludedId()
        {
            Refiller().Refill(_queue, "music-001", 3, excludeMediaId: "music-002");

            CollectionAssert.DoesNotContain(Ids(), "music-002");
        }

        [Test]
        public void Refill_ReturnsZeroForAnUnknownSeed()
        {
            Assert.AreEqual(0, Refiller().Refill(_queue, "does-not-exist", 3));
        }

        // ───────── PlayerSession 相当の設定 ─────────

        [Test]
        public void RecentMemory_KeepsTheSameSongFromComingBackImmediately()
        {
            var refiller = Refiller(recentMemory: 5);
            refiller.Refill(_queue, "music-001", 3);
            string[] first = Ids();

            _queue.Clear();
            refiller.Refill(_queue, "music-001", 3);

            foreach (string id in first)
            {
                CollectionAssert.DoesNotContain(Ids(), id, "直近に積んだ曲は避ける");
            }
        }

        [Test]
        public void ForgetRecent_LetsThePreviousPicksComeBack()
        {
            var refiller = Refiller(recentMemory: 5);
            refiller.Refill(_queue, "music-001", 3);
            string[] first = Ids();

            _queue.Clear();
            refiller.ForgetRecent();
            refiller.Refill(_queue, "music-001", 3);

            CollectionAssert.AreEqual(first, Ids(), "記憶を消せば同じ並びに戻る");
        }

        [Test]
        public void RecentMemory_OnlyRemembersTheConfiguredCount()
        {
            var refiller = Refiller(recentMemory: 2);
            refiller.Refill(_queue, "music-001", 5);

            Assert.AreEqual(2, refiller.Recent.Count);
        }

        [Test]
        public void WithoutRepeat_TheRefillStopsOnceEverythingHasBeenSeen()
        {
            var refiller = Refiller(recentMemory: 100);
            _queue.Clear();

            int total = 0;
            for (int i = 0; i < 10; i++)
            {
                total += refiller.Refill(_queue, "music-001", 3);
                _queue.Clear();
            }

            Assert.Less(total, _catalog.Count * 10, "緩和しない設定では一巡した時点で止まる");
        }

        // ───────── おすすめ再生相当の設定 ─────────

        [Test]
        public void AllowRepeat_KeepsFillingAfterEverythingHasBeenSeen()
        {
            var refiller = Refiller(recentMemory: 100, allowRepeat: true);

            for (int i = 0; i < 20; i++)
            {
                _queue.Clear();
                int added = refiller.Refill(_queue, "music-001", 3);
                Assert.Greater(added, 0, $"{i + 1} 回目の補充で積めなくなった");
            }
        }

        [Test]
        public void CatalogFallback_FillsWhenTheEngineReturnsNothing()
        {
            // 存在しない seed → おすすめは 0 件。カタログ補完だけが働く。
            var refiller = Refiller(allowFallback: true);

            int added = refiller.Refill(_queue, "does-not-exist", 3);

            Assert.AreEqual(3, added);
            Assert.AreEqual(0, refiller.EnqueuedByRecommendation);
            Assert.AreEqual(3, refiller.EnqueuedByCatalogFallback);
        }

        [Test]
        public void CatalogFallback_IsOffByDefault()
        {
            var refiller = Refiller();

            Assert.AreEqual(0, refiller.Refill(_queue, "does-not-exist", 3));
            Assert.AreEqual(0, _queue.Count);
        }

        // ───────── ふるい ─────────

        [Test]
        public void Filter_KeepsUnplayableItemsOut()
        {
            var refiller = Refiller(new MediaTypePlaybackFilter(MediaType.Video));

            refiller.Refill(_queue, "music-001", 5, excludeMediaId: null, candidateCount: 50);

            Assert.Greater(_queue.Count, 0);
            foreach (var entry in _queue.GetAll())
            {
                Assert.AreEqual(MediaType.Video, entry.Item.Type);
            }
            Assert.Greater(refiller.FilteredOutCount, 0, "弾いた数を数えている");
        }

        [Test]
        public void Filter_CanBeSwappedAfterConstruction()
        {
            var refiller = Refiller();
            refiller.Filter = new MediaTypePlaybackFilter(MediaType.Video);

            refiller.Refill(_queue, "music-001", 5, excludeMediaId: null, candidateCount: 50);

            foreach (var entry in _queue.GetAll())
            {
                Assert.AreEqual(MediaType.Video, entry.Item.Type);
            }
        }

        [Test]
        public void CompositeFilter_NeedsEveryPartToAgree()
        {
            var refiller = Refiller(new CompositePlaybackFilter(
                new MediaTypePlaybackFilter(MediaType.Video), new RejectEverything()));

            Assert.AreEqual(0, refiller.Refill(_queue, "music-001", 5));
        }

        [Test]
        public void CompositeFilter_PassesWhenEveryPartAgrees()
        {
            var refiller = Refiller(new CompositePlaybackFilter(
                AnyPlaybackFilter.Instance, new MediaTypePlaybackFilter(MediaType.Video)));

            refiller.Refill(_queue, "music-001", 5, excludeMediaId: null, candidateCount: 50);

            Assert.Greater(_queue.Count, 0);
        }

        /// <summary>何も通さないふるい(合成の検証用)。</summary>
        private sealed class RejectEverything : IPlaybackFilter
        {
            public bool CanPlay(MediaItem item) => false;
        }

        // ───────── PickNext(積まずに 1 件だけ) ─────────

        [Test]
        public void PickNext_ReturnsAMediaIdWithoutQueueingIt()
        {
            string picked = Refiller().PickNext(_queue, "music-001");

            Assert.AreEqual("music-002", picked);
            Assert.AreEqual(0, _queue.Count, "PickNext は Queue に触らない");
        }

        [Test]
        public void PickNext_SkipsWhatIsAlreadyQueued()
        {
            var refiller = Refiller();
            refiller.Refill(_queue, "music-001", 1);

            string picked = refiller.PickNext(_queue, "music-001");

            CollectionAssert.DoesNotContain(Ids(), picked);
        }

        [Test]
        public void PickNext_ReturnsNullWhenNothingIsLeft()
        {
            var refiller = Refiller(recentMemory: 100);
            refiller.Refill(_queue, "music-001", _catalog.Count);

            Assert.IsNull(refiller.PickNext(_queue, "music-001"));
        }

        // ───────── 診断 ─────────

        [Test]
        public void Diagnostics_CountRefillsAndSources()
        {
            var refiller = Refiller(allowFallback: true);

            refiller.Refill(_queue, "music-001", 2);
            refiller.Refill(_queue, "music-001", 2);

            Assert.AreEqual(2, refiller.RefillCount);
            Assert.AreEqual(4, refiller.EnqueuedByRecommendation);
        }

        [Test]
        public void ResetDiagnostics_LeavesTheQueueAlone()
        {
            var refiller = Refiller();
            refiller.Refill(_queue, "music-001", 3);

            refiller.ResetDiagnostics();

            Assert.AreEqual(0, refiller.RefillCount);
            Assert.AreEqual(0, refiller.EnqueuedByRecommendation);
            Assert.AreEqual(3, _queue.Count);
        }

        [Test]
        public void ToString_DescribesTheSettings()
        {
            string text = Refiller(allowRepeat: true, allowFallback: true).ToString();

            StringAssert.Contains("RecommendationQueueRefiller", text);
            StringAssert.Contains("repeat: True", text);
        }

        // ───────── 責務の境界 ─────────

        [Test]
        public void Refiller_NeverExposesAUrl()
        {
            // 積むのは MediaItem、選ぶのは MediaId。URL を返す口はどこにも無い。
            var picked = Refiller().PickNext(_queue, "music-001");

            Assert.IsInstanceOf<string>(picked);
            Assert.IsNull(
                typeof(RecommendationQueueRefiller).GetMethod("GetUrl"),
                "URL を扱うのは VideoBackend だけ");
        }
    }
}
