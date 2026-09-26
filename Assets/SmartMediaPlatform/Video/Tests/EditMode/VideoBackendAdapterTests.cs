using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Adapter;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;

namespace SmartMediaPlatform.Video.Tests
{
    /// <summary>
    /// VideoBackendAdapter が動画プレイヤーの語彙をプラットフォームの契約へ
    /// 正しく翻訳しているか、および上位が変更不要であることの検証。
    /// </summary>
    public sealed class VideoBackendAdapterTests
    {
        private IMediaCatalog _catalog;
        private ListBackendLogger _logger;
        private DummyVideoBackend _video;
        private VideoBackendAdapter _adapter;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _logger = new ListBackendLogger();
            _video = new DummyVideoBackend("DummyVideoBackend", _logger);
            _adapter = new VideoBackendAdapter("VideoAdapter", _video, _logger);
        }

        private MediaItem M(string id) => _catalog.FindById(id);

        // ───────── アダプタはバックエンドとして振る舞う ─────────

        [Test]
        public void Adapter_IsAMediaBackendAndSeekable()
        {
            Assert.IsInstanceOf<IMediaBackend>(_adapter,
                "BackendManager にそのまま登録できる");
            Assert.IsInstanceOf<ISeekableBackend>(_adapter);
            Assert.IsInstanceOf<IBackendAdapter>(_adapter);
        }

        [Test]
        public void SupportedTypes_DefaultToVideoAndLive()
        {
            CollectionAssert.AreEquivalent(
                new[] { MediaType.Video, MediaType.Live }, _adapter.SupportedTypes.ToArray());
        }

        // ───────── 対象の翻訳(MediaItem → URL) ─────────

        [Test]
        public void CanPlay_ChecksTypeAndUrl()
        {
            Assert.IsTrue(_adapter.CanPlay(M("video-001")));
            Assert.IsFalse(_adapter.CanPlay(M("music-001")), "種別が違えば扱えない");
            Assert.IsFalse(_adapter.CanPlay(null));
        }

        [Test]
        public void CanPlay_IsFalseWhenTheMediaHasNoUrl()
        {
            var noUrl = new MediaItem("video-x", "URL なし", "作者", MediaType.Video);

            Assert.IsFalse(_adapter.CanPlay(noUrl), "URL が無ければ動画プレイヤーは扱えない");
        }

        [Test]
        public void Load_PassesTheUrlToTheVideoBackend()
        {
            var item = M("video-001");

            Assert.IsTrue(_adapter.Load(item));

            Assert.AreEqual(item.Url, _video.GetCurrentUrl(),
                "MediaItem から URL を取り出して動画プレイヤーへ渡す");
            Assert.AreEqual("video-001", _adapter.GetCurrentMedia().Id);
        }

        [Test]
        public void Load_UnsupportedMedia_ReportsAnError()
        {
            Assert.IsFalse(_adapter.Load(M("music-001")));

            Assert.IsTrue(_adapter.HasError);
            StringAssert.Contains("扱えません", _adapter.LastError);
        }

        [Test]
        public void Load_Null_ReportsAnError()
        {
            Assert.IsFalse(_adapter.Load(null));
            Assert.IsTrue(_adapter.HasError);
        }

        // ───────── 状態の翻訳 ─────────

        [Test]
        public void State_IsTranslatedFromTheVideoPlayer()
        {
            Assert.AreEqual(BackendState.Idle, _adapter.GetState());

            _adapter.Load(M("video-001"));
            Assert.AreEqual(BackendState.Ready, _adapter.GetState());

            _adapter.Play();
            Assert.AreEqual(BackendState.Playing, _adapter.GetState());

            _adapter.Pause();
            Assert.AreEqual(BackendState.Paused, _adapter.GetState());

            _adapter.Resume();
            Assert.AreEqual(BackendState.Playing, _adapter.GetState());

            _adapter.Stop();
            Assert.AreEqual(BackendState.Stopped, _adapter.GetState());
        }

        [Test]
        public void LoadingState_IsTranslated()
        {
            _video.AutoCompleteLoading = false;

            _adapter.Load(M("video-001"));

            Assert.AreEqual(BackendState.Loading, _adapter.GetState(),
                "非同期読み込み中は Loading になる");

            _video.CompleteLoading();
            Assert.AreEqual(BackendState.Ready, _adapter.GetState());
        }

        [Test]
        public void FinishedState_BecomesEnded()
        {
            _adapter.Load(M("video-001"));
            _adapter.Play();
            _video.SimulateFinished();

            Assert.AreEqual(BackendState.Ended, _adapter.GetState(),
                "動画プレイヤーの Finished は Ended に写る");
        }

        [Test]
        public void FailedState_BecomesError()
        {
            _adapter.Load(M("video-001"));
            _video.SimulateError("読み込み失敗");

            Assert.AreEqual(BackendState.Error, _adapter.GetState());
        }

        // ───────── シークの翻訳(割合 → 秒) ─────────

        [Test]
        public void Seek_ConvertsNormalizedPositionToSeconds()
        {
            _video.Duration = 200f;
            _adapter.Load(M("video-001"));
            _adapter.Play();

            Assert.IsTrue(_adapter.Seek(0.25f));

            Assert.AreEqual(50f, _video.GetTime(), 0.01f,
                "上位は割合、動画プレイヤーは秒。アダプタが変換する");
            Assert.AreEqual(50f, _adapter.GetCurrentTime(), 0.01f);
            Assert.AreEqual(0.25f, _adapter.GetProgress(), 0.01f);
        }

        [Test]
        public void Seek_ClampsOutOfRangeValues()
        {
            _video.Duration = 100f;
            _adapter.Load(M("video-001"));

            _adapter.Seek(-1f);
            Assert.AreEqual(0f, _adapter.GetCurrentTime(), 0.01f);

            _adapter.Seek(5f);
            Assert.AreEqual(100f, _adapter.GetCurrentTime(), 0.01f);
        }

        [Test]
        public void CanSeek_ReflectsTheVideoBackend()
        {
            Assert.IsFalse(_adapter.CanSeek, "読み込み前はシークできない");

            _adapter.Load(M("video-001"));
            Assert.IsTrue(_adapter.CanSeek);

            _video.CanSeek = false;
            Assert.IsFalse(_adapter.CanSeek, "生配信ならシークできない");
            Assert.IsFalse(_adapter.Seek(0.5f));
        }

        [Test]
        public void GetDuration_FallsBackToCatalogMetadata()
        {
            _video.Duration = 0f;   // 動画プレイヤーが長さを答えられない
            _adapter.Load(M("video-001"));

            Assert.AreEqual(M("video-001").DurationSeconds, _adapter.GetDuration(), 0.01f);
        }

        // ───────── SkipNext / SkipPrevious ─────────

        [Test]
        public void SkipNext_StopsTheCurrentVideo()
        {
            _adapter.Load(M("video-001"));
            _adapter.Play();

            Assert.IsTrue(_adapter.SkipNext());
            Assert.AreEqual(BackendState.Stopped, _adapter.GetState());
        }

        [Test]
        public void SkipPrevious_RewindsWhenPlaybackHasProgressed()
        {
            _video.Duration = 200f;
            _adapter.RewindThresholdSeconds = 3f;
            _adapter.Load(M("video-001"));
            _adapter.Play();
            _video.Advance(10f);

            Assert.IsTrue(_adapter.SkipPrevious());
            Assert.AreEqual(0f, _adapter.GetCurrentTime(), 0.01f);
            Assert.AreEqual(BackendState.Playing, _adapter.GetState(), "頭出しなので再生は続く");
        }

        [Test]
        public void SkipPrevious_StopsWhenPlaybackJustStarted()
        {
            _adapter.RewindThresholdSeconds = 3f;
            _adapter.Load(M("video-001"));
            _adapter.Play();
            _video.Advance(1f);

            Assert.IsTrue(_adapter.SkipPrevious());
            Assert.AreEqual(BackendState.Stopped, _adapter.GetState());
        }

        [Test]
        public void SkipOperations_WithoutMedia_AreRejected()
        {
            Assert.IsFalse(_adapter.SkipNext());
            Assert.IsFalse(_adapter.SkipPrevious());
        }

        // ───────── 通知の翻訳 ─────────

        [Test]
        public void LifecycleEvents_AreEmittedToBackendObservers()
        {
            var observer = new RecordingObserver();
            _adapter.AddObserver(observer);

            _adapter.Load(M("video-001"));
            _adapter.Play();
            _adapter.Pause();
            _adapter.Resume();
            _adapter.Stop();

            var types = observer.Types().Where(t => t != BackendEventType.Error).ToArray();
            CollectionAssert.IsSubsetOf(
                new[]
                {
                    BackendEventType.Started, BackendEventType.Paused,
                    BackendEventType.Resumed, BackendEventType.Stopped,
                },
                types);
            Assert.AreEqual("VideoAdapter", observer.Events.Last().BackendName);
        }

        [Test]
        public void VideoEnd_IsTranslatedToTheEndedEvent()
        {
            var observer = new RecordingObserver();
            _adapter.AddObserver(observer);

            _adapter.Load(M("video-001"));
            _adapter.Play();
            _video.SimulateFinished();

            var ended = observer.Events.Last(e => e.Type == BackendEventType.Ended);
            Assert.AreEqual("video-001", ended.Item.Id);
            Assert.AreEqual("VideoAdapter", ended.BackendName,
                "上位からはアダプタが 1 つのバックエンドに見える");
        }

        [Test]
        public void VideoError_IsTranslatedToTheErrorEvent()
        {
            var observer = new RecordingObserver();
            _adapter.AddObserver(observer);

            _adapter.Load(M("video-001"));
            _video.SimulateError("URL の読み込みに失敗しました");

            Assert.IsTrue(_adapter.HasError);
            StringAssert.Contains("URL の読み込みに失敗", _adapter.LastError);
            Assert.AreEqual(BackendEventType.Error, observer.Events.Last().Type);
        }

        [Test]
        public void ClearError_ResetsTheState()
        {
            _adapter.ReportError("失敗");
            _adapter.ClearError();

            Assert.IsFalse(_adapter.HasError);
            Assert.IsNull(_adapter.LastError);
        }

        [Test]
        public void RemoveObserver_StopsNotifications()
        {
            var observer = new RecordingObserver();
            _adapter.AddObserver(observer);
            _adapter.Load(M("video-001"));

            _adapter.RemoveObserver(observer);
            int before = observer.Events.Count;
            _adapter.Play();

            Assert.AreEqual(before, observer.Events.Count);
        }

        // ───────── 上位が変更不要であること ─────────

        [Test]
        public void Adapter_RegistersWithBackendManagerUnchanged()
        {
            var queue = new MediaQueue();
            var manager = new BackendManager(queue, _logger);

            manager.RegisterBackend(_adapter);
            queue.Enqueue(M("video-001"));

            Assert.AreSame(_adapter, manager.SelectBackendFor(M("video-001")),
                "BackendManager だけが種類を判断する");
            Assert.IsTrue(manager.LoadCurrent());
            Assert.IsTrue(manager.Play());
            Assert.AreEqual(BackendState.Playing, manager.GetState());
        }

        [Test]
        public void PlayerSession_PlaysVideoWithoutAnyChanges()
        {
            var session = NewSession(_adapter);
            session.AutoQueueEnabled = false;
            session.SetTracks(new[] { "video-001" });

            Assert.IsTrue(session.Play());
            Assert.AreEqual("video-001", session.CurrentMediaId);
            Assert.AreEqual(PlaybackState.Playing, session.PlaybackState);
        }

        [Test]
        public void PlayerSession_AdvancesOnVideoEnd()
        {
            var session = NewSession(_adapter);
            session.AutoQueueEnabled = false;
            session.SetTracks(new[] { "video-001" });
            session.Play();

            // 動画プレイヤーが「最後まで再生した」と言ってくる
            _video.SimulateFinished();

            Assert.AreEqual(PlaybackState.Exhausted, session.PlaybackState,
                "Video の終了もセッションが受け取れる");
        }

        [Test]
        public void PlayerSession_QueueAndRecommendationStillWork()
        {
            // Queue と Recommendation は動画でもそのまま働く(どちらも未変更)
            var session = NewSession(_adapter);
            session.SetTracks(new[] { "video-001" });

            Assert.AreEqual(1, session.Queue.Count, "Queue が動画を保持できる");

            // おすすめ補充は Music を返すので、Video アダプタだけでは再生できない。
            // それでも Queue 自体は正しく積まれる。
            int added = session.EnsureQueueFilled();
            Assert.Greater(added, 0, "Recommendation が動画を種に候補を返す");
            Assert.Greater(session.Queue.Count, 1);
        }

        private PlayerSession NewSession(params IBackendAdapter[] adapters)
        {
            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, _logger);
            var player = new MediaPlayer(backendManager, _logger);
            var engine = new RecommendationEngine(
                _catalog, RecommendationRule.CreateDefault(), new System.Random(1));

            var session = new PlayerSession(
                "s", player, _catalog, engine, new System.Random(1), _logger);

            foreach (var adapter in adapters) session.RegisterBackend(adapter);
            return session;
        }

        // ───────── ガード ─────────

        [Test]
        public void Constructor_RejectsNullVideoBackend()
        {
            Assert.Throws<ArgumentNullException>(
                () => new VideoBackendAdapter("x", null, _logger));
        }

        /// <summary>テスト用に通知を記録する観測者。</summary>
        private sealed class RecordingObserver : IBackendObserver
        {
            public List<BackendEvent> Events { get; } = new List<BackendEvent>();

            public void OnBackendEvent(BackendEvent backendEvent) => Events.Add(backendEvent);

            public BackendEventType[] Types() => Events.Select(e => e.Type).ToArray();
        }
    }
}
