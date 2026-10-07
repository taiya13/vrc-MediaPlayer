using System;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;

namespace SmartMediaPlatform.Adapter.Tests
{
    /// <summary>
    /// アダプタ共通の振る舞い、および
    /// 「Backend の違いを吸収する」ことの検証。
    /// </summary>
    public sealed class BackendAdapterTests
    {
        private IMediaCatalog _catalog;
        private ListBackendLogger _logger;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new Random(1));
            _logger = new ListBackendLogger();
        }

        private MediaItem M(string id) => _catalog.FindById(id);

        private MediaBackendAdapter NewMusicAdapter(string name = "MusicAdapter")
        {
            return new MediaBackendAdapter(
                name, new DummyBackend(name + ".inner", _logger, MediaType.Music),
                _logger, MediaType.Music);
        }

        // ───────── アダプタはバックエンドでもある ─────────

        [Test]
        public void Adapter_IsAlsoAMediaBackend()
        {
            var adapter = NewMusicAdapter();

            Assert.IsInstanceOf<IMediaBackend>(adapter,
                "上位(BackendManager / MediaPlayer / PlayerSession)は変更なしで受け取れる");
            Assert.IsInstanceOf<ISeekableBackend>(adapter,
                "どのアダプタも再生位置の問い合わせに答えられる");
        }

        [Test]
        public void Adapter_ExposesSupportedTypes()
        {
            var adapter = NewMusicAdapter();

            CollectionAssert.AreEqual(new[] { MediaType.Music }, adapter.SupportedTypes.ToArray());
        }

        // ───────── CanPlay ─────────

        [Test]
        public void CanPlay_ChecksTypeFirst()
        {
            var adapter = NewMusicAdapter();

            Assert.IsTrue(adapter.CanPlay(M("music-001")));
            Assert.IsFalse(adapter.CanPlay(M("video-001")), "扱えない種別は false");
            Assert.IsFalse(adapter.CanPlay(null));
        }

        // ───────── 状態遷移(内側へ委譲) ─────────

        [Test]
        public void StateTransitions_MatchTheWrappedBackend()
        {
            var adapter = NewMusicAdapter();

            Assert.AreEqual(BackendState.Idle, adapter.GetState());

            Assert.IsTrue(adapter.Load(M("music-001")));
            Assert.AreEqual(BackendState.Ready, adapter.GetState());
            Assert.AreEqual("music-001", adapter.GetCurrentMedia().Id);

            Assert.IsTrue(adapter.Play());
            Assert.AreEqual(BackendState.Playing, adapter.GetState());

            Assert.IsTrue(adapter.Pause());
            Assert.AreEqual(BackendState.Paused, adapter.GetState());

            Assert.IsTrue(adapter.Resume());
            Assert.AreEqual(BackendState.Playing, adapter.GetState());

            Assert.IsTrue(adapter.Stop());
            Assert.AreEqual(BackendState.Stopped, adapter.GetState());
        }

        [Test]
        public void GetCurrentMedia_MatchesGetCurrent()
        {
            var adapter = NewMusicAdapter();
            adapter.Load(M("music-001"));

            Assert.AreSame(adapter.GetCurrent(), adapter.GetCurrentMedia());
        }

        // ───────── SkipNext / SkipPrevious ─────────

        [Test]
        public void SkipNext_StopsTheCurrentMedia()
        {
            var adapter = NewMusicAdapter();
            adapter.Load(M("music-001"));
            adapter.Play();

            Assert.IsTrue(adapter.SkipNext());
            Assert.AreEqual(BackendState.Stopped, adapter.GetState());
            Assert.IsNotNull(adapter.GetCurrentMedia(),
                "次に何を再生するかはアダプタの仕事ではない");
        }

        [Test]
        public void SkipNext_WithoutMedia_IsRejected()
        {
            Assert.IsFalse(NewMusicAdapter().SkipNext());
        }

        [Test]
        public void SkipPrevious_RewindsWhenPlaybackHasProgressed()
        {
            // シークできるアダプタで、十分に再生が進んでいれば頭出しする
            var adapter = new DummyVideoBackendAdapter("Video", _logger);
            adapter.RewindThresholdSeconds = 3f;
            adapter.Load(M("video-001"));
            adapter.Play();
            adapter.Advance(10f);

            Assert.IsTrue(adapter.SkipPrevious());
            Assert.AreEqual(0f, adapter.GetCurrentTime(), 0.001f);
            Assert.AreEqual(BackendState.Playing, adapter.GetState(), "頭出しなので再生は続く");
        }

        [Test]
        public void SkipPrevious_StopsWhenPlaybackJustStarted()
        {
            var adapter = new DummyVideoBackendAdapter("Video", _logger);
            adapter.RewindThresholdSeconds = 3f;
            adapter.Load(M("video-001"));
            adapter.Play();
            adapter.Advance(1f);

            Assert.IsTrue(adapter.SkipPrevious());
            Assert.AreEqual(BackendState.Stopped, adapter.GetState(),
                "始まったばかりなら打ち切って、前の曲は上位に選ばせる");
        }

        [Test]
        public void SkipPrevious_WithoutMedia_IsRejected()
        {
            Assert.IsFalse(NewMusicAdapter().SkipPrevious());
        }

        // ───────── シーク能力の吸収 ─────────

        [Test]
        public void NonSeekableBackend_ReportsCanSeekFalse()
        {
            var adapter = NewMusicAdapter();   // 内側は DummyBackend(シーク非対応)
            adapter.Load(M("music-001"));

            Assert.IsFalse(adapter.CanSeek);
            Assert.IsFalse(adapter.Seek(0.5f));
            Assert.AreEqual(0f, adapter.GetCurrentTime(), 0.001f);
        }

        [Test]
        public void GetDuration_FallsBackToCatalogMetadata()
        {
            var adapter = NewMusicAdapter();
            adapter.Load(M("music-001"));

            Assert.AreEqual(M("music-001").DurationSeconds, adapter.GetDuration(), 0.001f,
                "内側が長さを答えられなくても Catalog で補える");
        }

        [Test]
        public void VideoAdapter_SuppliesSeekEvenThoughInnerBackendCannot()
        {
            var adapter = new DummyVideoBackendAdapter("Video", _logger);

            Assert.IsNotInstanceOf<ISeekableBackend>(adapter.InnerDummy,
                "内側のバックエンドは再生位置を持たない");

            adapter.Load(M("video-001"));
            adapter.Play();

            Assert.IsTrue(adapter.CanSeek, "アダプタが肩代わりしてシークできる");
            Assert.IsTrue(adapter.Seek(0.5f));
            Assert.AreEqual(M("video-001").DurationSeconds * 0.5f,
                adapter.GetCurrentTime(), 0.01f);
            Assert.AreEqual(0.5f, adapter.GetProgress(), 0.01f);
        }

        [Test]
        public void VideoAdapter_CanPretendToBeNonSeekable()
        {
            var adapter = new DummyVideoBackendAdapter("Live", _logger)
            {
                SimulateSeekSupport = false,
            };
            adapter.Load(M("video-001"));

            Assert.IsFalse(adapter.CanSeek, "生配信のように、シークできない場合も表現できる");
            Assert.IsFalse(adapter.Seek(0.5f));
        }

        [Test]
        public void Seek_ClampsOutOfRangeValues()
        {
            var adapter = new DummyVideoBackendAdapter("Video", _logger);
            adapter.Load(M("video-001"));
            adapter.Play();

            adapter.Seek(-1f);
            Assert.AreEqual(0f, adapter.GetCurrentTime(), 0.001f);

            adapter.Seek(2f);
            Assert.AreEqual(adapter.GetDuration(), adapter.GetCurrentTime(), 0.01f);
        }

        [Test]
        public void Stop_ResetsThePosition()
        {
            var adapter = new DummyVideoBackendAdapter("Video", _logger);
            adapter.Load(M("video-001"));
            adapter.Play();
            adapter.Seek(0.5f);

            adapter.Stop();

            Assert.AreEqual(0f, adapter.GetCurrentTime(), 0.001f);
        }

        // ───────── Ended 通知の中継 ─────────

        [Test]
        public void EndedFromInnerBackend_IsRelayedWithTheAdapterName()
        {
            var adapter = new DummyVideoBackendAdapter("VideoAdapter", _logger);
            var observer = new RecordingObserver();
            adapter.AddObserver(observer);

            adapter.Load(M("video-001"));
            adapter.Play();
            adapter.InnerDummy.SimulateEnded();

            var ended = observer.Events.Last(e => e.Type == BackendEventType.Ended);
            Assert.AreEqual("VideoAdapter", ended.BackendName,
                "上位からはアダプタが 1 つのバックエンドに見える");
            Assert.AreEqual("video-001", ended.Item.Id);
        }

        [Test]
        public void LifecycleEvents_AreRelayedInOrder()
        {
            var adapter = NewMusicAdapter();
            var observer = new RecordingObserver();
            adapter.AddObserver(observer);

            adapter.Load(M("music-001"));
            adapter.Play();
            adapter.Pause();
            adapter.Resume();
            adapter.Stop();

            var types = observer.Types().Where(t => t != BackendEventType.Error).ToArray();
            CollectionAssert.AreEqual(
                new[]
                {
                    BackendEventType.Loaded, BackendEventType.Started,
                    BackendEventType.Paused, BackendEventType.Resumed, BackendEventType.Stopped,
                },
                types);
        }

        [Test]
        public void RemoveObserver_StopsNotifications()
        {
            var adapter = NewMusicAdapter();
            var observer = new RecordingObserver();
            adapter.AddObserver(observer);
            adapter.Load(M("music-001"));

            adapter.RemoveObserver(observer);
            int before = observer.Events.Count;
            adapter.Play();

            Assert.AreEqual(before, observer.Events.Count);
        }

        // ───────── Error 通知 ─────────

        [Test]
        public void ReportError_RaisesAnErrorEventAndRemembersIt()
        {
            var adapter = NewMusicAdapter();
            var observer = new RecordingObserver();
            adapter.AddObserver(observer);

            adapter.ReportError("動画の読み込みに失敗しました");

            Assert.IsTrue(adapter.HasError);
            Assert.AreEqual("動画の読み込みに失敗しました", adapter.LastError);

            var error = observer.Events.Last();
            Assert.AreEqual(BackendEventType.Error, error.Type);
            StringAssert.Contains("読み込みに失敗", error.Message);
        }

        [Test]
        public void ClearError_ResetsTheState()
        {
            var adapter = NewMusicAdapter();
            adapter.ReportError("失敗");

            adapter.ClearError();

            Assert.IsFalse(adapter.HasError);
            Assert.IsNull(adapter.LastError);
        }

        [Test]
        public void Load_UnsupportedMedia_ReportsAnError()
        {
            var adapter = NewMusicAdapter();

            Assert.IsFalse(adapter.Load(M("video-001")));
            Assert.IsTrue(adapter.HasError);
            StringAssert.Contains("扱えません", adapter.LastError);
        }

        [Test]
        public void Load_Null_ReportsAnError()
        {
            var adapter = NewMusicAdapter();

            Assert.IsFalse(adapter.Load(null));
            Assert.IsTrue(adapter.HasError);
        }

        [Test]
        public void Load_Success_ClearsThePreviousError()
        {
            var adapter = NewMusicAdapter();
            adapter.ReportError("前のエラー");

            adapter.Load(M("music-001"));

            Assert.IsFalse(adapter.HasError);
        }

        // ───────── ガード ─────────

        [Test]
        public void Constructor_RejectsNullBackend()
        {
            Assert.Throws<ArgumentNullException>(
                () => new MediaBackendAdapter("x", null, _logger, MediaType.Music));
        }
    }
}
