using System;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Recommendation;

namespace SmartMediaPlatform.Queue.Tests
{
    /// <summary>
    /// Recommendation Engine 連携(自動補充)の検証。
    /// 決定的にするためランダム補正は 0 にする。
    /// </summary>
    public sealed class QueueManagerTests
    {
        private IMediaCatalog _catalog;
        private RecommendationEngine _engine;
        private MediaQueue _queue;
        private QueueManager _manager;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));

            var deterministicRules = new[]
            {
                RecommendationRule.Related(10.0),
                RecommendationRule.SameArtist(5.0),
                RecommendationRule.SameGenre(3.0),
                RecommendationRule.TagMatch(1.0),
                RecommendationRule.Random(0.0),
            };
            _engine = new RecommendationEngine(_catalog, deterministicRules, new System.Random(99));

            _queue = new MediaQueue();
            _manager = new QueueManager(_queue, _engine, _catalog)
            {
                MinimumCount = 2,
                TargetCount = 5,
            };
        }

        private string[] Ids() => _queue.GetAll().Select(x => x.MediaId).ToArray();

        [Test]
        public void Fill_AddsRecommendationsInRankOrder()
        {
            int added = _manager.Fill("music-001", 3);

            Assert.AreEqual(3, added);
            CollectionAssert.AreEqual(new[] { "music-002", "music-007", "video-001" }, Ids());
        }

        [Test]
        public void Fill_MarksItemsAsRecommendation()
        {
            _manager.Fill("music-001", 3);
            Assert.IsTrue(_queue.GetAll().All(x => x.Source == QueueItemSource.Recommendation));
        }

        [Test]
        public void EnsureFilled_DoesNothingWhenAboveMinimum()
        {
            _manager.Fill("music-001", 3);
            int before = _queue.Count;

            Assert.AreEqual(0, _manager.EnsureFilled());
            Assert.AreEqual(before, _queue.Count);
        }

        [Test]
        public void EnsureFilled_FillsUpToTargetWhenRunningLow()
        {
            _queue.Enqueue(_catalog.FindById("music-001"));

            int added = _manager.EnsureFilled();

            Assert.AreEqual(4, added);
            Assert.AreEqual(_manager.TargetCount, _queue.Count);
        }

        [Test]
        public void EnsureFilled_DoesNotAddDuplicates()
        {
            _queue.Enqueue(_catalog.FindById("music-001"));
            _manager.EnsureFilled();

            Assert.AreEqual(_queue.Count, Ids().Distinct().Count());
        }

        [Test]
        public void EnsureFilled_OnEmptyQueue_UsesRandomSeed()
        {
            Assert.IsTrue(_queue.IsEmpty);

            int added = _manager.EnsureFilled();

            Assert.Greater(added, 0);
            Assert.AreEqual(_manager.TargetCount, _queue.Count);
        }

        [Test]
        public void EnsureFilled_WithExhaustedCatalog_AddsNothing()
        {
            foreach (var item in _catalog.GetAll()) _queue.Enqueue(item);
            _manager.MinimumCount = 99;
            _manager.TargetCount = 99;

            Assert.AreEqual(0, _manager.EnsureFilled(), "積める曲が無ければ何も追加しない(無限ループしない)");
        }

        [Test]
        public void SkipAndRefill_AdvancesAndRefills()
        {
            _manager.Fill("music-001", 2);
            string firstId = _queue.Peek().MediaId;

            var newNow = _manager.SkipAndRefill();

            Assert.AreEqual(firstId, _manager.LastRemovedId);
            Assert.IsNotNull(newNow);
            Assert.AreEqual(_queue.Peek().MediaId, newNow.MediaId);
            Assert.GreaterOrEqual(_queue.Count, _manager.MinimumCount);
        }

        [Test]
        public void DequeueAndRefill_ReturnsRemovedAndRefills()
        {
            _manager.Fill("music-001", 1);

            var removed = _manager.DequeueAndRefill();

            Assert.IsNotNull(removed);
            Assert.AreEqual(removed.MediaId, _manager.LastRemovedId);
            Assert.Greater(_queue.Count, 0, "空になっても直前の曲を種に補充できる");
        }

        [Test]
        public void DequeueAndRefill_OnEmptyQueue_ReturnsNull()
        {
            var removed = _manager.DequeueAndRefill();
            Assert.IsNull(removed);
        }

        [Test]
        public void Fill_UnknownSeed_AddsNothing()
        {
            Assert.AreEqual(0, _manager.Fill("no-such-id", 3));
            Assert.IsTrue(_queue.IsEmpty);
        }

        [Test]
        public void Fill_NonPositiveCount_AddsNothing()
        {
            Assert.AreEqual(0, _manager.Fill("music-001", 0));
            Assert.AreEqual(0, _manager.Fill("music-001", -1));
        }

        [Test]
        public void Constructor_RejectsNullDependencies()
        {
            Assert.Throws<ArgumentNullException>(() => new QueueManager(null, _engine, _catalog));
            Assert.Throws<ArgumentNullException>(() => new QueueManager(_queue, null, _catalog));
            Assert.Throws<ArgumentNullException>(() => new QueueManager(_queue, _engine, null));
        }
    }
}
