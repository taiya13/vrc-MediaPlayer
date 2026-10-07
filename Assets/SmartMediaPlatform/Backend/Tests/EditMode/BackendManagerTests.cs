using System;
using NUnit.Framework;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Queue;

namespace SmartMediaPlatform.Backend.Tests
{
    /// <summary>Queue と Backend の連携、およびバックエンド選択の検証。</summary>
    public sealed class BackendManagerTests
    {
        private IMediaCatalog _catalog;
        private ListBackendLogger _logger;
        private MediaQueue _queue;
        private BackendManager _manager;
        private DummyBackend _musicBackend;
        private DummyBackend _videoBackend;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _logger = new ListBackendLogger();
            _queue = new MediaQueue();

            _manager = new BackendManager(_queue, _logger);
            _musicBackend = new DummyBackend("MusicBackend", _logger, MediaType.Music);
            _videoBackend = new DummyBackend("VideoBackend", _logger, MediaType.Video, MediaType.Live);
            _manager.RegisterBackend(_musicBackend);
            _manager.RegisterBackend(_videoBackend);
        }

        private MediaItem M(string id) => _catalog.FindById(id);

        private void FillQueue(params string[] ids)
        {
            foreach (var id in ids) _queue.Enqueue(M(id));
        }

        // --- バックエンド選択 ---

        [Test]
        public void SelectBackendFor_PicksByMediaType()
        {
            Assert.AreSame(_musicBackend, _manager.SelectBackendFor(M("music-001")));
            Assert.AreSame(_videoBackend, _manager.SelectBackendFor(M("video-001")));
        }

        [Test]
        public void SelectBackendFor_UnsupportedType_ReturnsNull()
        {
            Assert.IsNull(_manager.SelectBackendFor(M("podcast-001")),
                "Podcast 用バックエンドは登録していない");
            Assert.IsNull(_manager.SelectBackendFor(null));
        }

        [Test]
        public void CanPlay_ReflectsRegisteredBackends()
        {
            Assert.IsTrue(_manager.CanPlay(M("music-001")));
            Assert.IsTrue(_manager.CanPlay(M("video-001")));
            Assert.IsFalse(_manager.CanPlay(M("podcast-001")));
        }

        [Test]
        public void RegisterBackend_IsIdempotent()
        {
            _manager.RegisterBackend(_musicBackend);
            Assert.AreEqual(2, _manager.Backends.Count);
        }

        [Test]
        public void UnregisterBackend_RemovesIt()
        {
            Assert.IsTrue(_manager.UnregisterBackend(_videoBackend));
            Assert.AreEqual(1, _manager.Backends.Count);
            Assert.IsFalse(_manager.UnregisterBackend(_videoBackend));
            Assert.IsNull(_manager.SelectBackendFor(M("video-001")));
        }

        // --- Queue 連携 ---

        [Test]
        public void LoadCurrent_LoadsQueueHead()
        {
            FillQueue("music-001", "music-004");

            Assert.IsTrue(_manager.LoadCurrent());
            Assert.AreEqual("music-001", _manager.GetCurrent().Id);
            Assert.AreSame(_musicBackend, _manager.ActiveBackend);
            Assert.AreEqual(BackendState.Ready, _manager.GetState());
        }

        [Test]
        public void LoadCurrent_EmptyQueue_ReturnsFalse()
        {
            Assert.IsFalse(_manager.LoadCurrent());
            Assert.IsNull(_manager.GetCurrent());
        }

        [Test]
        public void LoadItem_WithoutSuitableBackend_ReturnsFalse()
        {
            Assert.IsFalse(_manager.LoadItem(M("podcast-001")));
            Assert.IsNull(_manager.ActiveBackend);
        }

        [Test]
        public void PlaybackControls_DelegateToActiveBackend()
        {
            FillQueue("music-001");
            _manager.LoadCurrent();

            Assert.IsTrue(_manager.Play());
            Assert.AreEqual(BackendState.Playing, _manager.GetState());

            Assert.IsTrue(_manager.Pause());
            Assert.AreEqual(BackendState.Paused, _manager.GetState());

            Assert.IsTrue(_manager.Resume());
            Assert.AreEqual(BackendState.Playing, _manager.GetState());

            Assert.IsTrue(_manager.Stop());
            Assert.AreEqual(BackendState.Stopped, _manager.GetState());
        }

        [Test]
        public void ControlsWithoutBackend_AreSafe()
        {
            var bare = new BackendManager(new MediaQueue());

            Assert.IsFalse(bare.Play());
            Assert.IsFalse(bare.Pause());
            Assert.IsFalse(bare.Resume());
            Assert.IsFalse(bare.Stop());
            Assert.AreEqual(BackendState.Idle, bare.GetState());
            Assert.IsNull(bare.GetCurrent());
        }

        // --- Skip ---

        [Test]
        public void Skip_AdvancesQueueAndLoadsNext()
        {
            FillQueue("music-001", "music-004", "music-010");
            _manager.LoadCurrent();
            _manager.Play();

            Assert.IsTrue(_manager.Skip());
            Assert.AreEqual("music-004", _manager.GetCurrent().Id);
            Assert.AreEqual("music-004", _queue.Peek().MediaId, "Queue も進む");
        }

        [Test]
        public void Skip_WhilePlaying_KeepsPlaying()
        {
            FillQueue("music-001", "music-004");
            _manager.LoadCurrent();
            _manager.Play();

            _manager.Skip();

            Assert.AreEqual(BackendState.Playing, _manager.GetState(),
                "再生中にスキップしたら次も再生される");
        }

        [Test]
        public void Skip_WhileStopped_StaysReady()
        {
            FillQueue("music-001", "music-004");
            _manager.LoadCurrent();

            _manager.Skip();

            Assert.AreEqual(BackendState.Ready, _manager.GetState(),
                "再生していなければ勝手に再生を始めない");
        }

        [Test]
        public void Skip_OnLastItem_ReturnsFalse()
        {
            FillQueue("music-001");
            _manager.LoadCurrent();

            Assert.IsFalse(_manager.Skip());
            Assert.IsTrue(_queue.IsEmpty);
        }

        [Test]
        public void Skip_SwitchesBackendWhenTypeChanges()
        {
            FillQueue("music-001", "video-001");
            _manager.LoadCurrent();
            _manager.Play();

            _manager.Skip();

            Assert.AreSame(_videoBackend, _manager.ActiveBackend);
            Assert.AreEqual("video-001", _manager.GetCurrent().Id);
            Assert.AreEqual(BackendState.Stopped, _musicBackend.GetState(),
                "切り替え時に前のバックエンドは停止される");
        }

        // --- 自動送り ---

        [Test]
        public void AutoAdvance_IsDisabledByDefault()
        {
            FillQueue("music-001", "music-004");
            _manager.LoadCurrent();
            _manager.Play();

            _musicBackend.SimulateEnded();

            Assert.AreEqual("music-001", _manager.GetCurrent().Id,
                "既定では自然終了しても次へ進まない");
        }

        [Test]
        public void AutoAdvance_WhenEnabled_MovesToNextOnEnded()
        {
            FillQueue("music-001", "music-004");
            _manager.AutoAdvanceOnEnded = true;
            _manager.LoadCurrent();
            _manager.Play();

            _musicBackend.SimulateEnded();

            Assert.AreEqual("music-004", _manager.GetCurrent().Id);
        }

        // --- ガード ---

        [Test]
        public void Constructor_RejectsNullQueue()
        {
            Assert.Throws<ArgumentNullException>(() => new BackendManager(null));
        }

        [Test]
        public void RegisterBackend_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => _manager.RegisterBackend(null));
        }
    }
}
