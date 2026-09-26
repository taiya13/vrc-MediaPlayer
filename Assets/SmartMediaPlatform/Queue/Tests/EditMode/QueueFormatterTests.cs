using System;
using NUnit.Framework;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;

namespace SmartMediaPlatform.Queue.Tests
{
    /// <summary>
    /// Console 表示の整形を検証する。
    /// 課題で指定された出力フォーマットを崩さないための回帰テスト。
    /// </summary>
    public sealed class QueueFormatterTests
    {
        private IMediaCatalog _catalog;
        private MediaQueue _queue;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _queue = new MediaQueue();
        }

        private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines);

        private void Fill(params string[] ids)
        {
            foreach (var id in ids) _queue.Enqueue(_catalog.FindById(id));
        }

        [Test]
        public void FormatQueue_NumbersItemsFromOne()
        {
            Fill("music-001", "music-004", "music-010");

            Assert.AreEqual(
                Lines("Queue", "1. music-001", "2. music-004", "3. music-010"),
                QueueFormatter.FormatQueue(_queue));
        }

        [Test]
        public void FormatQueue_EmptyQueue_ShowsPlaceholder()
        {
            Assert.AreEqual(Lines("Queue", "(empty)"), QueueFormatter.FormatQueue(_queue));
        }

        [Test]
        public void FormatNowNext_ShowsHeadAndSecond()
        {
            Fill("music-004", "music-010");

            Assert.AreEqual(
                Lines("Now", "music-004", "Next", "music-010"),
                QueueFormatter.FormatNowNext(_queue));
        }

        [Test]
        public void FormatNowNext_WithoutNext_ShowsNone()
        {
            Fill("music-004");

            Assert.AreEqual(
                Lines("Now", "music-004", "Next", "(none)"),
                QueueFormatter.FormatNowNext(_queue));
        }

        [Test]
        public void ScenarioFromSpec_ProducesExpectedTransitions()
        {
            // 課題の Console 例をそのまま再現する:
            //   Queue(3件) → Skip → Now/Next → Enqueue → Queue
            Fill("music-001", "music-004", "music-010");
            Assert.AreEqual(
                Lines("Queue", "1. music-001", "2. music-004", "3. music-010"),
                QueueFormatter.FormatQueue(_queue));

            _queue.Skip();
            Assert.AreEqual(
                Lines("Now", "music-004", "Next", "music-010"),
                QueueFormatter.FormatNowNext(_queue));

            _queue.Enqueue(_catalog.FindById("music-003"));
            Assert.AreEqual(
                Lines("Queue", "1. music-004", "2. music-010", "3. music-003"),
                QueueFormatter.FormatQueue(_queue));
        }

        [Test]
        public void FormatQueueVerbose_IncludesTitleArtistAndSource()
        {
            _queue.Enqueue(_catalog.FindById("music-001"), QueueItemSource.Recommendation);

            string text = QueueFormatter.FormatQueueVerbose(_queue);

            StringAssert.Contains("music-001", text);
            StringAssert.Contains("Neon Skyline", text);
            StringAssert.Contains("Aurora Drive", text);
            StringAssert.Contains("Recommendation", text);
        }
    }
}
