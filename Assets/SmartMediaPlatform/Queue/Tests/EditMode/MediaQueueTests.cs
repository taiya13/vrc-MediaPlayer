using System;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;

namespace SmartMediaPlatform.Queue.Tests
{
    /// <summary>Queue 本体(<see cref="MediaQueue"/>)の API 検証。</summary>
    public sealed class MediaQueueTests
    {
        private IMediaCatalog _catalog;
        private MediaQueue _queue;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _queue = new MediaQueue();
        }

        private MediaItem M(string id) => _catalog.FindById(id);

        private void Fill(params string[] ids)
        {
            foreach (var id in ids) _queue.Enqueue(M(id));
        }

        private string[] Ids() => _queue.GetAll().Select(x => x.MediaId).ToArray();

        // --- 空の状態 ---

        [Test]
        public void EmptyQueue_BehavesSafely()
        {
            Assert.AreEqual(0, _queue.Count);
            Assert.IsTrue(_queue.IsEmpty);
            Assert.IsNull(_queue.Peek());
            Assert.IsNull(_queue.PeekNext());
            Assert.IsNull(_queue.Dequeue());
            Assert.IsNull(_queue.Skip());
            Assert.IsEmpty(_queue.GetAll());
        }

        // --- Enqueue / EnqueueNext ---

        [Test]
        public void Enqueue_AppendsToTail()
        {
            Fill("music-001", "music-004", "music-010");
            CollectionAssert.AreEqual(new[] { "music-001", "music-004", "music-010" }, Ids());
            Assert.AreEqual(3, _queue.Count);
            Assert.IsFalse(_queue.IsEmpty);
        }

        [Test]
        public void EnqueueNext_InsertsAfterNow()
        {
            Fill("music-001", "music-002", "music-003");
            _queue.EnqueueNext(M("music-010"));
            CollectionAssert.AreEqual(
                new[] { "music-001", "music-010", "music-002", "music-003" }, Ids());
        }

        [Test]
        public void EnqueueNext_OnEmpty_BecomesHead()
        {
            _queue.EnqueueNext(M("music-001"));
            CollectionAssert.AreEqual(new[] { "music-001" }, Ids());
        }

        [Test]
        public void Enqueue_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _queue.Enqueue(null));
            Assert.Throws<ArgumentNullException>(() => _queue.EnqueueNext(null));
        }

        [Test]
        public void Enqueue_AssignsUniqueHandles()
        {
            Fill("music-001", "music-001", "music-002");
            var handles = _queue.GetAll().Select(x => x.Handle).ToArray();
            Assert.AreEqual(3, handles.Distinct().Count());
            Assert.IsTrue(handles.All(h => h > 0));
        }

        [Test]
        public void Enqueue_RecordsSource()
        {
            _queue.Enqueue(M("music-001"));
            _queue.Enqueue(M("music-002"), QueueItemSource.Recommendation);
            Assert.AreEqual(QueueItemSource.Manual, _queue.PeekAt(0).Source);
            Assert.AreEqual(QueueItemSource.Recommendation, _queue.PeekAt(1).Source);
        }

        // --- Peek / Dequeue / Skip ---

        [Test]
        public void Peek_ReturnsHeadWithoutRemoving()
        {
            Fill("music-001", "music-004");
            Assert.AreEqual("music-001", _queue.Peek().MediaId);
            Assert.AreEqual("music-004", _queue.PeekNext().MediaId);
            Assert.AreEqual(2, _queue.Count);
        }

        [Test]
        public void PeekAt_OutOfRange_ReturnsNull()
        {
            Fill("music-001");
            Assert.IsNotNull(_queue.PeekAt(0));
            Assert.IsNull(_queue.PeekAt(1));
            Assert.IsNull(_queue.PeekAt(-1));
        }

        [Test]
        public void Dequeue_RemovesAndReturnsHead()
        {
            Fill("music-001", "music-004");
            var head = _queue.Dequeue();
            Assert.AreEqual("music-001", head.MediaId);
            CollectionAssert.AreEqual(new[] { "music-004" }, Ids());
        }

        [Test]
        public void Skip_RemovesHeadAndReturnsNewHead()
        {
            Fill("music-001", "music-004", "music-010");
            var newNow = _queue.Skip();
            Assert.AreEqual("music-004", newNow.MediaId);
            CollectionAssert.AreEqual(new[] { "music-004", "music-010" }, Ids());
        }

        [Test]
        public void Skip_OnLastItem_ReturnsNull()
        {
            Fill("music-001");
            Assert.IsNull(_queue.Skip());
            Assert.IsTrue(_queue.IsEmpty);
        }

        // --- Contains / IndexOf ---

        [Test]
        public void Contains_IsCaseInsensitive()
        {
            Fill("music-001");
            Assert.IsTrue(_queue.Contains("music-001"));
            Assert.IsTrue(_queue.Contains("MUSIC-001"));
            Assert.IsFalse(_queue.Contains("music-999"));
            Assert.IsFalse(_queue.Contains(null));
        }

        [Test]
        public void IndexOf_ReturnsPositionOrMinusOne()
        {
            Fill("music-001", "music-004", "music-010");
            Assert.AreEqual(2, _queue.IndexOf("music-010"));
            Assert.AreEqual(-1, _queue.IndexOf("no-such"));
        }

        [Test]
        public void ContainsHandle_Works()
        {
            var entry = _queue.Enqueue(M("music-001"));
            Assert.IsTrue(_queue.ContainsHandle(entry.Handle));
            Assert.IsFalse(_queue.ContainsHandle(entry.Handle + 999));
        }

        // --- Remove ---

        [Test]
        public void Remove_RemovesFirstOccurrenceOnly()
        {
            Fill("music-001", "music-002", "music-001");
            Assert.IsTrue(_queue.Remove("music-001"));
            CollectionAssert.AreEqual(new[] { "music-002", "music-001" }, Ids());
        }

        [Test]
        public void Remove_UnknownId_ReturnsFalse()
        {
            Fill("music-001");
            Assert.IsFalse(_queue.Remove("no-such"));
            Assert.AreEqual(1, _queue.Count);
        }

        [Test]
        public void RemoveHandle_RemovesExactEntry()
        {
            Fill("music-001");
            var second = _queue.Enqueue(M("music-001"));
            Assert.IsTrue(_queue.RemoveHandle(second.Handle));
            Assert.AreEqual(1, _queue.Count);
            Assert.IsFalse(_queue.ContainsHandle(second.Handle));
        }

        [Test]
        public void RemoveAt_ChecksBounds()
        {
            Fill("music-001", "music-004");
            Assert.IsTrue(_queue.RemoveAt(0));
            CollectionAssert.AreEqual(new[] { "music-004" }, Ids());
            Assert.IsFalse(_queue.RemoveAt(5));
            Assert.IsFalse(_queue.RemoveAt(-1));
        }

        // --- Move ---

        [Test]
        public void Move_ReordersItems()
        {
            Fill("music-001", "music-002", "music-003", "music-004");

            Assert.IsTrue(_queue.Move(0, 2));
            CollectionAssert.AreEqual(
                new[] { "music-002", "music-003", "music-001", "music-004" }, Ids());

            Assert.IsTrue(_queue.Move(3, 0));
            CollectionAssert.AreEqual(
                new[] { "music-004", "music-002", "music-003", "music-001" }, Ids());
        }

        [Test]
        public void Move_InvalidArguments_ReturnFalse()
        {
            Fill("music-001", "music-002");
            Assert.IsFalse(_queue.Move(0, 0));
            Assert.IsFalse(_queue.Move(-1, 1));
            Assert.IsFalse(_queue.Move(0, 99));
        }

        // --- Clear / Version / GetAll ---

        [Test]
        public void Clear_EmptiesQueue()
        {
            Fill("music-001", "music-002");
            _queue.Clear();
            Assert.AreEqual(0, _queue.Count);
            Assert.IsTrue(_queue.IsEmpty);
        }

        [Test]
        public void Version_IncrementsOnMutationOnly()
        {
            int v0 = _queue.Version;

            _queue.Enqueue(M("music-001"));
            int v1 = _queue.Version;
            Assert.Greater(v1, v0);

            _queue.Peek();
            _queue.Contains("music-001");
            Assert.AreEqual(v1, _queue.Version, "読み取りでは版番号は変わらない");

            _queue.Dequeue();
            Assert.Greater(_queue.Version, v1);

            int v2 = _queue.Version;
            _queue.Clear();
            Assert.AreEqual(v2, _queue.Version, "空に対する Clear は版番号を変えない");
        }

        [Test]
        public void GetAll_IsLiveReadOnlyView()
        {
            var view = _queue.GetAll();
            Assert.AreEqual(0, view.Count);

            _queue.Enqueue(M("music-001"));
            Assert.AreEqual(1, view.Count, "GetAll は現在の状態を反映する読み取り専用ビュー");
        }

        // --- QueueItem ---

        [Test]
        public void QueueItem_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new QueueItem(1, null, QueueItemSource.Manual));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new QueueItem(0, M("music-001"), QueueItemSource.Manual));
        }
    }
}
