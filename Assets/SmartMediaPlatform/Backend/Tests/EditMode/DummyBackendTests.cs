using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;

namespace SmartMediaPlatform.Backend.Tests
{
    /// <summary>DummyBackend の状態遷移・拒否条件・通知の検証。</summary>
    public sealed class DummyBackendTests
    {
        private IMediaCatalog _catalog;
        private ListBackendLogger _logger;
        private DummyBackend _backend;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _logger = new ListBackendLogger();
            _backend = new DummyBackend("TestBackend", _logger);
        }

        private MediaItem M(string id) => _catalog.FindById(id);

        // --- 初期状態と未ロード時の安全性 ---

        [Test]
        public void InitialState_IsIdle()
        {
            Assert.AreEqual(BackendState.Idle, _backend.GetState());
            Assert.IsNull(_backend.GetCurrent());
        }

        [Test]
        public void OperationsBeforeLoad_AreRejectedWithoutStateChange()
        {
            Assert.IsFalse(_backend.Play());
            Assert.IsFalse(_backend.Pause());
            Assert.IsFalse(_backend.Resume());
            Assert.IsFalse(_backend.Stop());
            Assert.IsFalse(_backend.Skip());
            Assert.AreEqual(BackendState.Idle, _backend.GetState());
        }

        // --- Load ---

        [Test]
        public void Load_MovesToReady()
        {
            Assert.IsTrue(_backend.Load(M("music-001")));
            Assert.AreEqual(BackendState.Ready, _backend.GetState());
            Assert.AreEqual("music-001", _backend.GetCurrent().Id);
        }

        [Test]
        public void Load_Null_IsRejected()
        {
            Assert.IsFalse(_backend.Load(null));
            Assert.AreEqual(BackendState.Idle, _backend.GetState());
        }

        [Test]
        public void Load_UnsupportedType_IsRejected()
        {
            var musicOnly = new DummyBackend("MusicOnly", _logger, MediaType.Music);

            Assert.IsFalse(musicOnly.Load(M("video-001")));
            Assert.AreEqual(BackendState.Idle, musicOnly.GetState());
            Assert.IsNull(musicOnly.GetCurrent());
        }

        // --- Play / Pause / Resume / Stop ---

        [Test]
        public void Play_FromReady_MovesToPlaying()
        {
            _backend.Load(M("music-001"));

            Assert.IsTrue(_backend.Play());
            Assert.AreEqual(BackendState.Playing, _backend.GetState());
        }

        [Test]
        public void Play_LogsWithoutActualPlayback()
        {
            _backend.Load(M("music-001"));
            _backend.Play();

            Assert.IsTrue(_logger.Lines.Any(l => l.Contains("Play music-001")),
                "Play はログを出力する");
            Assert.IsTrue(_logger.Lines.Any(l => l.Contains("no actual playback")),
                "実際には再生しないことがログに残る");
        }

        [Test]
        public void Play_WhenAlreadyPlaying_IsRejected()
        {
            _backend.Load(M("music-001"));
            _backend.Play();

            Assert.IsFalse(_backend.Play());
            Assert.AreEqual(BackendState.Playing, _backend.GetState());
        }

        [Test]
        public void Play_WhenPaused_IsRejectedInFavorOfResume()
        {
            _backend.Load(M("music-001"));
            _backend.Play();
            _backend.Pause();

            Assert.IsFalse(_backend.Play(), "一時停止からの再開は Resume を使う");
            Assert.AreEqual(BackendState.Paused, _backend.GetState());
        }

        [Test]
        public void Pause_OnlyWorksWhilePlaying()
        {
            _backend.Load(M("music-001"));
            Assert.IsFalse(_backend.Pause(), "Ready からは一時停止できない");

            _backend.Play();
            Assert.IsTrue(_backend.Pause());
            Assert.AreEqual(BackendState.Paused, _backend.GetState());

            Assert.IsFalse(_backend.Pause(), "二重の一時停止は拒否");
        }

        [Test]
        public void Resume_OnlyWorksWhilePaused()
        {
            _backend.Load(M("music-001"));
            _backend.Play();
            Assert.IsFalse(_backend.Resume(), "再生中は Resume できない");

            _backend.Pause();
            Assert.IsTrue(_backend.Resume());
            Assert.AreEqual(BackendState.Playing, _backend.GetState());
        }

        [Test]
        public void Stop_MovesToStoppedAndKeepsCurrent()
        {
            _backend.Load(M("music-001"));
            _backend.Play();

            Assert.IsTrue(_backend.Stop());
            Assert.AreEqual(BackendState.Stopped, _backend.GetState());
            Assert.AreEqual("music-001", _backend.GetCurrent().Id, "停止しても読み込みは保持する");
            Assert.IsFalse(_backend.Stop(), "二重の停止は拒否");
        }

        [Test]
        public void Play_FromStopped_RestartsPlayback()
        {
            _backend.Load(M("music-001"));
            _backend.Play();
            _backend.Stop();

            Assert.IsTrue(_backend.Play());
            Assert.AreEqual(BackendState.Playing, _backend.GetState());
        }

        // --- Skip / Ended ---

        [Test]
        public void Skip_StopsCurrentAndKeepsReference()
        {
            _backend.Load(M("music-001"));
            _backend.Play();

            Assert.IsTrue(_backend.Skip());
            Assert.AreEqual(BackendState.Stopped, _backend.GetState());
            Assert.IsNotNull(_backend.GetCurrent(),
                "次に何を再生するかは Backend の責務ではない");
        }

        [Test]
        public void SimulateEnded_MovesToEnded()
        {
            _backend.Load(M("music-001"));
            _backend.Play();

            Assert.IsTrue(_backend.SimulateEnded());
            Assert.AreEqual(BackendState.Ended, _backend.GetState());
        }

        [Test]
        public void SimulateEnded_WhenNotPlaying_IsRejected()
        {
            _backend.Load(M("music-001"));
            Assert.IsFalse(_backend.SimulateEnded());
            Assert.AreEqual(BackendState.Ready, _backend.GetState());
        }

        [Test]
        public void Play_FromEnded_RestartsPlayback()
        {
            _backend.Load(M("music-001"));
            _backend.Play();
            _backend.SimulateEnded();

            Assert.IsTrue(_backend.Play());
            Assert.AreEqual(BackendState.Playing, _backend.GetState());
        }

        // --- CanPlay ---

        [Test]
        public void CanPlay_DefaultBackend_AcceptsEveryType()
        {
            var any = new DummyBackend();
            Assert.IsTrue(any.CanPlay(M("music-001")));
            Assert.IsTrue(any.CanPlay(M("video-001")));
            Assert.IsTrue(any.CanPlay(M("podcast-001")));
        }

        [Test]
        public void CanPlay_RestrictedBackend_AcceptsOnlyItsTypes()
        {
            var music = new DummyBackend("Music", null, MediaType.Music);
            var video = new DummyBackend("Video", null, MediaType.Video, MediaType.Live);

            Assert.IsTrue(music.CanPlay(M("music-001")));
            Assert.IsFalse(music.CanPlay(M("video-001")));

            Assert.IsTrue(video.CanPlay(M("video-001")));
            Assert.IsFalse(video.CanPlay(M("music-001")));
        }

        [Test]
        public void CanPlay_Null_IsFalse()
        {
            Assert.IsFalse(_backend.CanPlay(null));
        }

        // --- 通知 ---

        [Test]
        public void Observer_ReceivesEventsInOrder()
        {
            var observer = new RecordingObserver();
            _backend.AddObserver(observer);

            _backend.Load(M("music-001"));
            _backend.Play();
            _backend.Pause();
            _backend.Resume();
            _backend.Stop();
            _backend.Skip();

            CollectionAssert.AreEqual(
                new[]
                {
                    BackendEventType.Loaded, BackendEventType.Started, BackendEventType.Paused,
                    BackendEventType.Resumed, BackendEventType.Stopped, BackendEventType.Skipped,
                },
                observer.Types());
        }

        [Test]
        public void Event_CarriesStateTransition()
        {
            var observer = new RecordingObserver();
            _backend.AddObserver(observer);

            _backend.Load(M("music-001"));
            _backend.Play();

            var started = observer.Events.Last();
            Assert.AreEqual(BackendEventType.Started, started.Type);
            Assert.AreEqual(BackendState.Ready, started.PreviousState);
            Assert.AreEqual(BackendState.Playing, started.NewState);
            Assert.AreEqual("music-001", started.Item.Id);
            Assert.AreEqual("TestBackend", started.BackendName);
        }

        [Test]
        public void RemoveObserver_StopsNotifications()
        {
            var observer = new RecordingObserver();
            _backend.AddObserver(observer);
            _backend.Load(M("music-001"));

            _backend.RemoveObserver(observer);
            int before = observer.Events.Count;
            _backend.Play();

            Assert.AreEqual(before, observer.Events.Count);
        }

        [Test]
        public void AddObserver_IsIdempotent()
        {
            var observer = new RecordingObserver();
            _backend.AddObserver(observer);
            _backend.AddObserver(observer);

            _backend.Load(M("music-001"));

            Assert.AreEqual(1, observer.Events.Count, "同じ観測者を二重登録しても通知は 1 回");
        }

        [Test]
        public void RejectedOperation_EmitsErrorEventWithoutStateChange()
        {
            var observer = new RecordingObserver();
            _backend.AddObserver(observer);

            _backend.Play(); // 未ロード

            Assert.AreEqual(BackendEventType.Error, observer.Events.Last().Type);
            Assert.AreEqual(BackendState.Idle, observer.Events.Last().PreviousState);
            Assert.AreEqual(BackendState.Idle, observer.Events.Last().NewState);
        }
    }
}
