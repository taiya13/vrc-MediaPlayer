using System;
using System.Collections.Generic;
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
    /// Phase3-2: VRChat の動画イベントを Backend へ届ける
    /// <see cref="VideoEventBridge"/> の検証。
    ///
    /// VRChat SDK は使いません。実機の <c>OnVideoReady</c> …に相当する呼び出しを
    /// <see cref="IVideoEventSink"/> へ直接流し込むので、
    /// 重複排除と <see cref="VRChatVideoBackend.Tick"/> との調停を決定的に確かめられます。
    /// </summary>
    public sealed class VideoEventBridgeTests
    {
        private IMediaCatalog _catalog;
        private CatalogVideoUrlTable _urls;
        private ListBackendLogger _logger;
        private SimulatedVRCVideoPlayer _player;
        private VRChatVideoBackend _backend;
        private VideoEventBridge _bridge;
        private VideoObserver _observer;

        private string Url => _catalog.FindById("video-001").Url;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _urls = new CatalogVideoUrlTable(_catalog);
            _logger = new ListBackendLogger();
            _player = new SimulatedVRCVideoPlayer(_urls);
            _backend = new VRChatVideoBackend("VRChatVideoBackend", _player, _urls, _logger);
            _bridge = new VideoEventBridge(_backend, _logger);
            _observer = new VideoObserver();
            _backend.AddObserver(_observer);
        }

        // ───────── 前提 ─────────

        [Test]
        public void Bridge_IsAVideoEventSink()
        {
            Assert.IsInstanceOf<IVideoEventSink>(_bridge,
                "イベントの運び手はこの契約だけを知っていればよい");
        }

        [Test]
        public void Constructor_RejectsNullBackend()
        {
            Assert.Throws<ArgumentNullException>(() => new VideoEventBridge(null, _logger));
        }

        [Test]
        public void NoEventsYet_MeansPollingStaysOn()
        {
            Assert.IsFalse(_bridge.EventsObserved);
            Assert.IsTrue(_backend.DetectEndByPolling, "Phase3-1 の保険はそのまま");
            Assert.AreEqual(0, _bridge.TotalAccepted);
            Assert.AreEqual(0, _bridge.TotalRejected);
        }

        // ───────── 4 つのイベントが届く ─────────

        [Test]
        public void OnVideoReady_ReachesTheBackend()
        {
            _backend.Load(Url);
            _player.CompleteLoading();

            Assert.IsTrue(_bridge.OnVideoReady());

            Assert.AreEqual(VideoPlayerState.Ready, _backend.GetState());
            Assert.AreEqual(1, _observer.ReadyCount, "観測者へ 1 回だけ届く");
            Assert.AreEqual(1, _bridge.AcceptedCount(VideoEventKind.Ready));
        }

        [Test]
        public void OnVideoStart_ReachesTheBackend()
        {
            Ready();

            Assert.IsTrue(_bridge.OnVideoStart());

            Assert.AreEqual(VideoPlayerState.Playing, _backend.GetState());
            Assert.AreEqual(1, _observer.StartCount);
            Assert.AreEqual(1, _bridge.AcceptedCount(VideoEventKind.Start));
        }

        [Test]
        public void OnVideoEnd_ReachesTheBackend()
        {
            Ready();
            _bridge.OnVideoStart();

            Assert.IsTrue(_bridge.OnVideoEnd());

            Assert.AreEqual(VideoPlayerState.Finished, _backend.GetState());
            Assert.AreEqual(1, _observer.EndCount);
            Assert.AreEqual(1, _bridge.AcceptedCount(VideoEventKind.End));
        }

        [Test]
        public void OnVideoError_ReachesTheBackend()
        {
            Ready();

            Assert.IsTrue(_bridge.OnVideoError(VideoErrorKind.AccessDenied));

            Assert.AreEqual(VideoPlayerState.Failed, _backend.GetState());
            Assert.AreEqual(VideoErrorKind.AccessDenied, _backend.LastErrorKind);
            Assert.AreEqual(1, _observer.ErrorCount);
            StringAssert.Contains("AccessDenied", _observer.LastError);
        }

        [Test]
        public void OnVideoPause_ReachesTheBackend()
        {
            Ready();
            _bridge.OnVideoStart();

            Assert.IsTrue(_bridge.OnVideoPause());

            Assert.AreEqual(VideoPlayerState.Paused, _backend.GetState());
            Assert.AreEqual(1, _observer.PauseCount);
        }

        [Test]
        public void OnVideoPlay_ResumesFromPause()
        {
            Ready();
            _bridge.OnVideoStart();
            _bridge.OnVideoPause();

            Assert.IsTrue(_bridge.OnVideoPlay());

            Assert.AreEqual(VideoPlayerState.Playing, _backend.GetState());
        }

        [Test]
        public void OnVideoLoop_IsRecordedButChangesNothing()
        {
            Ready();
            _bridge.OnVideoStart();

            Assert.IsFalse(_bridge.OnVideoLoop(), "ループは状態を変えないので受理しない");

            Assert.AreEqual(VideoPlayerState.Playing, _backend.GetState());
            Assert.AreEqual(1, _bridge.RejectedCount(VideoEventKind.Loop));
            Assert.IsTrue(_bridge.EventsObserved, "それでもイベントは届いている");
        }

        [Test]
        public void AnyEvent_MarksTheSetupAsEventDriven()
        {
            Ready();

            Assert.IsTrue(_bridge.EventsObserved);
            Assert.AreEqual(VideoEventKind.Ready, _bridge.LastAccepted);
            Assert.AreEqual(VideoEventKind.Ready, _bridge.LastReceived);
        }

        // ───────── 二重発火しない ─────────

        [Test]
        public void RepeatedReady_NotifiesOnlyOnce()
        {
            _backend.Load(Url);
            _player.CompleteLoading();

            Assert.IsTrue(_bridge.OnVideoReady());
            Assert.IsFalse(_bridge.OnVideoReady());
            Assert.IsFalse(_bridge.OnVideoReady());

            Assert.AreEqual(1, _observer.ReadyCount);
            Assert.AreEqual(2, _bridge.RejectedCount(VideoEventKind.Ready));
        }

        [Test]
        public void RepeatedStart_NotifiesOnlyOnce()
        {
            Ready();

            Assert.IsTrue(_bridge.OnVideoStart());
            Assert.IsFalse(_bridge.OnVideoStart());
            Assert.IsFalse(_bridge.OnVideoPlay(), "OnVideoPlay も同じ扱い");

            Assert.AreEqual(1, _observer.StartCount);
        }

        [Test]
        public void RepeatedEnd_NotifiesOnlyOnce()
        {
            Ready();
            _bridge.OnVideoStart();

            Assert.IsTrue(_bridge.OnVideoEnd());
            Assert.IsFalse(_bridge.OnVideoEnd());
            Assert.IsFalse(_bridge.OnVideoEnd());

            Assert.AreEqual(1, _observer.EndCount);
            Assert.AreEqual(2, _bridge.RejectedCount(VideoEventKind.End));
        }

        [Test]
        public void RepeatedIdenticalError_NotifiesOnlyOnce()
        {
            Ready();

            Assert.IsTrue(_bridge.OnVideoError(VideoErrorKind.AccessDenied));
            Assert.IsFalse(_bridge.OnVideoError(VideoErrorKind.AccessDenied),
                "VRChat は同じ失敗を複数回送ることがある");

            Assert.AreEqual(1, _observer.ErrorCount);
            Assert.AreEqual(1, _bridge.RejectedCount(VideoEventKind.Error));
        }

        [Test]
        public void DifferentError_IsStillReported()
        {
            Ready();
            _bridge.OnVideoError(VideoErrorKind.AccessDenied);

            Assert.IsTrue(_bridge.OnVideoError(VideoErrorKind.RateLimited),
                "違う失敗は握りつぶさない");

            Assert.AreEqual(2, _observer.ErrorCount);
        }

        [Test]
        public void ErrorAfterReload_IsReportedAgain()
        {
            Ready();
            _bridge.OnVideoError(VideoErrorKind.PlayerError);

            // 読み込み直したら、同じ失敗でも改めて報告する
            _backend.Load(Url);
            _player.CompleteLoading();
            _bridge.OnVideoReady();

            Assert.IsTrue(_bridge.OnVideoError(VideoErrorKind.PlayerError));
            Assert.AreEqual(2, _observer.ErrorCount);
        }

        [Test]
        public void OutOfOrderEvents_AreRejectedWithoutBreakingState()
        {
            // 読み込みもしていないのにイベントだけ飛んでくる
            Assert.IsFalse(_bridge.OnVideoStart());
            Assert.IsFalse(_bridge.OnVideoEnd());
            Assert.IsFalse(_bridge.OnVideoPause());

            Assert.AreEqual(VideoPlayerState.None, _backend.GetState());
            Assert.AreEqual(0, _observer.StartCount + _observer.EndCount + _observer.PauseCount);
            Assert.AreEqual(3, _bridge.TotalRejected);
        }

        // ───────── Tick と競合しない ─────────

        [Test]
        public void Tick_StopsGuessingTheEndOnceEventsArrive()
        {
            Ready();

            Assert.IsTrue(_backend.DetectEndByPolling, "イベント到着前は保険が効いている");

            _bridge.Tick(0.016f);

            Assert.IsFalse(_backend.DetectEndByPolling,
                "イベントが届く構成では、ポーリングによる終了の推測を止める");
        }

        [Test]
        public void EventAndTick_TogetherStillNotifyEndOnce()
        {
            Ready();
            _bridge.OnVideoStart();

            // 実機と同じく、終端まで進んでプレイヤーが自分で止まる
            _player.FinishPlayback();

            // イベントとポーリングが同じフレームで競合する状況
            _bridge.OnVideoEnd();
            _bridge.Tick(0.016f);
            _bridge.Tick(0.016f);

            Assert.AreEqual(1, _observer.EndCount, "Ended は 1 回だけ");
            Assert.AreEqual(VideoPlayerState.Finished, _backend.GetState());
        }

        [Test]
        public void TickFirstThenEvent_StillNotifiesEndOnce()
        {
            // 逆順(ポーリングが先に気づき、あとからイベントが届く)でも 1 回
            _bridge.SuppressPollingWhenEventsArrive = false;
            Ready();
            _bridge.OnVideoStart();
            _player.FinishPlayback();

            _bridge.Tick(0.016f);
            _bridge.OnVideoEnd();

            Assert.AreEqual(1, _observer.EndCount);
        }

        [Test]
        public void Tick_KeepsWatchingForTimeoutEvenWhenEventsArrive()
        {
            _backend.LoadTimeoutSeconds = 3f;
            _backend.Load(Url);
            _player.CompleteLoading();
            _bridge.OnVideoReady();
            _bridge.OnVideoStart();

            // 次の動画は読み込みが終わらず、イベントも来ない
            _player.AutoCompleteLoading = false;
            _backend.Load(Url);

            for (int i = 0; i < 3; i++) _bridge.Tick(1f);

            Assert.AreEqual(VideoPlayerState.Failed, _backend.GetState(),
                "タイムアウトの監視はイベントが来ていても残す");
            Assert.AreEqual(VideoErrorKind.Timeout, _backend.LastErrorKind);
        }

        [Test]
        public void PollingStillWorksWhenNoEventArrives()
        {
            // Phase3-1 の動作を壊していないこと
            _backend.Load(Url);
            _player.CompleteLoading();
            _bridge.Tick(0.016f);

            Assert.AreEqual(VideoPlayerState.Ready, _backend.GetState());
            Assert.IsFalse(_bridge.EventsObserved);

            _backend.Play();
            _player.Advance(_player.DurationSeconds + 1f);
            _bridge.Tick(0.016f);

            Assert.AreEqual(VideoPlayerState.Finished, _backend.GetState());
            Assert.AreEqual(1, _observer.EndCount);
            Assert.IsTrue(_backend.DetectEndByPolling, "イベントが無いので保険は降りない");
        }

        [Test]
        public void SuppressPolling_CanBeTurnedOff()
        {
            _bridge.SuppressPollingWhenEventsArrive = false;
            Ready();

            _bridge.Tick(0.016f);

            Assert.IsTrue(_backend.DetectEndByPolling, "Phase3-1 と同じ「両方動く」状態に戻せる");
        }

        // ───────── 証跡 ─────────

        [Test]
        public void Log_RecordsAcceptedAndRejectedEvents()
        {
            Ready();
            _bridge.OnVideoReady();   // 重複

            Assert.AreEqual(2, _bridge.Log.Count);
            Assert.IsTrue(_bridge.Log[0].Accepted);
            Assert.IsFalse(_bridge.Log[1].Accepted);
            Assert.IsNotNull(_bridge.Log[1].RejectReason);
            Assert.AreEqual(VideoPlayerState.Loading, _bridge.Log[0].Before);
            Assert.AreEqual(VideoPlayerState.Ready, _bridge.Log[0].After);
        }

        [Test]
        public void Log_IsClearedWhenANewVideoIsLoaded()
        {
            Ready();
            Assert.Greater(_bridge.Log.Count, 0);

            _backend.Load(Url);
            _player.CompleteLoading();
            _bridge.OnVideoReady();

            Assert.AreEqual(1, _bridge.Log.Count, "前の動画の記録は持ち越さない");
        }

        [Test]
        public void Log_IsCapped()
        {
            _bridge.MaxLogEntries = 3;
            Ready();
            for (int i = 0; i < 10; i++) _bridge.OnVideoReady();

            Assert.AreEqual(3, _bridge.Log.Count);
        }

        [Test]
        public void ResetDiagnostics_ClearsCountsButNotTheBackend()
        {
            Ready();
            _bridge.OnVideoStart();

            _bridge.ResetDiagnostics();

            Assert.AreEqual(0, _bridge.TotalAccepted);
            Assert.AreEqual(0, _bridge.TotalRejected);
            Assert.AreEqual(0, _bridge.Log.Count);
            Assert.AreEqual(VideoPlayerState.Playing, _backend.GetState(),
                "バックエンドの状態には触れない");
        }

        // ───────── Udon からの符号化経路 ─────────

        [Test]
        public void Codec_RoundTripsEveryEventKind()
        {
            foreach (VideoEventKind kind in Enum.GetValues(typeof(VideoEventKind)))
            {
                if (kind == VideoEventKind.None || kind == VideoEventKind.Error) continue;

                int code = VideoEventCodec.Encode(kind);
                Assert.IsTrue(VideoEventCodec.TryDecode(code, out var decoded, out var error));
                Assert.AreEqual(kind, decoded);
                Assert.AreEqual(VideoErrorKind.None, error);
            }
        }

        [Test]
        public void Codec_RoundTripsEveryErrorKind()
        {
            var kinds = new[]
            {
                VideoErrorKind.Unknown,
                VideoErrorKind.InvalidUrl,
                VideoErrorKind.AccessDenied,
                VideoErrorKind.PlayerError,
                VideoErrorKind.RateLimited,
            };

            foreach (var kind in kinds)
            {
                int code = VideoEventCodec.EncodeError(kind);
                Assert.IsTrue(VideoEventCodec.TryDecode(code, out var decoded, out var error));
                Assert.AreEqual(VideoEventKind.Error, decoded);
                Assert.AreEqual(kind, error);
            }
        }

        [Test]
        public void Codec_TranslatesTheSdkErrorNumbers()
        {
            // VRC.SDK3.Components.Video.VideoError の値
            Assert.AreEqual(VideoErrorKind.Unknown, VideoEventCodec.TranslateSdkError(0));
            Assert.AreEqual(VideoErrorKind.InvalidUrl, VideoEventCodec.TranslateSdkError(1));
            Assert.AreEqual(VideoErrorKind.AccessDenied, VideoEventCodec.TranslateSdkError(2));
            Assert.AreEqual(VideoErrorKind.PlayerError, VideoEventCodec.TranslateSdkError(3));
            Assert.AreEqual(VideoErrorKind.RateLimited, VideoEventCodec.TranslateSdkError(4));
            Assert.AreEqual(VideoErrorKind.Unknown, VideoEventCodec.TranslateSdkError(99));
        }

        [Test]
        public void Codec_RejectsUnknownCodes()
        {
            Assert.IsFalse(VideoEventCodec.TryDecode(0, out _, out _));
            Assert.IsFalse(VideoEventCodec.TryDecode(42, out _, out _));
            Assert.IsFalse(VideoEventCodec.Deliver(_bridge, 42));
        }

        [Test]
        public void Codec_DeliversAnUdonEventStreamInOrder()
        {
            _backend.Load(Url);
            _player.CompleteLoading();

            // UdonVRCVideoEventRelay がリングバッファに積むのと同じ並び
            var stream = new[]
            {
                VideoEventCodec.Encode(VideoEventKind.Ready),
                VideoEventCodec.Encode(VideoEventKind.Start),
                VideoEventCodec.Encode(VideoEventKind.End),
            };

            foreach (var code in stream) VideoEventCodec.Deliver(_bridge, code);

            Assert.AreEqual(1, _observer.ReadyCount);
            Assert.AreEqual(1, _observer.StartCount);
            Assert.AreEqual(1, _observer.EndCount);
            Assert.AreEqual(VideoPlayerState.Finished, _backend.GetState());
        }

        [Test]
        public void Codec_DeliversErrorsWithTheirReason()
        {
            Ready();

            VideoEventCodec.Deliver(_bridge, VideoEventCodec.EncodeError(VideoErrorKind.RateLimited));

            Assert.AreEqual(VideoErrorKind.RateLimited, _backend.LastErrorKind);
            Assert.AreEqual(1, _observer.ErrorCount);
        }

        [Test]
        public void Codec_IgnoresANullSink()
        {
            Assert.IsFalse(VideoEventCodec.Deliver(null, VideoEventCodec.Encode(VideoEventKind.Ready)));
        }

        // ───────── 上位は無変更 ─────────

        [Test]
        public void PlayerSession_AdvancesFromAnEventWithoutAnyChanges()
        {
            _player.AutoCompleteLoading = true;

            // ← Phase2-4(B) から変わっていない行
            var adapter = new VideoBackendAdapter("VideoAdapter", _backend, _logger);
            var session = NewSession(adapter);
            session.AutoQueueEnabled = false;
            session.SetTracks(new[] { "video-001" });
            session.Play();

            Assert.AreEqual(PlaybackState.Playing, session.PlaybackState);

            // 実機の OnVideoEnd が届いた、という 1 行だけ
            Assert.IsTrue(_bridge.OnVideoEnd());

            Assert.AreEqual(PlaybackState.Exhausted, session.PlaybackState,
                "イベントが上位の自動送りにつながる");
        }

        [Test]
        public void Adapter_TranslatesEventDrivenErrorsUnchanged()
        {
            _player.AutoCompleteLoading = true;
            var adapter = new VideoBackendAdapter("VideoAdapter", _backend, _logger);
            var recorder = new RecordingObserver();
            adapter.AddObserver(recorder);

            adapter.Load(_catalog.FindById("video-001"));
            _bridge.OnVideoError(VideoErrorKind.AccessDenied);
            _bridge.OnVideoError(VideoErrorKind.AccessDenied);   // 重複

            int errorEvents = 0;
            foreach (var e in recorder.Events)
            {
                if (e.Type == BackendEventType.Error) errorEvents++;
            }

            Assert.AreEqual(1, errorEvents, "BackendAdapter へも Error は 1 回だけ");
            Assert.IsTrue(adapter.HasError);
            Assert.AreEqual(BackendState.Error, adapter.GetState());
        }

        [Test]
        public void Adapter_TranslatesEventDrivenEndUnchanged()
        {
            _player.AutoCompleteLoading = true;
            var adapter = new VideoBackendAdapter("VideoAdapter", _backend, _logger);
            var recorder = new RecordingObserver();
            adapter.AddObserver(recorder);

            adapter.Load(_catalog.FindById("video-001"));
            adapter.Play();
            _bridge.OnVideoEnd();
            _bridge.OnVideoEnd();   // 重複

            int endedEvents = 0;
            foreach (var e in recorder.Events)
            {
                if (e.Type == BackendEventType.Ended) endedEvents++;
            }

            Assert.AreEqual(1, endedEvents, "BackendAdapter へも Ended は 1 回だけ");
        }

        // ───────── 補助 ─────────

        /// <summary>読み込みが終わって再生できる状態まで、イベント経由で進める。</summary>
        private void Ready()
        {
            _backend.Load(Url);
            _player.CompleteLoading();
            _bridge.OnVideoReady();
        }

        private PlayerSession NewSession(IBackendAdapter adapter)
        {
            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, _logger);
            var player = new MediaPlayer(backendManager, _logger);
            var engine = new RecommendationEngine(
                _catalog, RecommendationRule.CreateDefault(), new System.Random(1));

            var session = new PlayerSession(
                "s", player, _catalog, engine, new System.Random(1), _logger);
            session.RegisterBackend(adapter);
            return session;
        }

        private sealed class VideoObserver : IVideoBackendObserver
        {
            public int ReadyCount { get; private set; }
            public int StartCount { get; private set; }
            public int PauseCount { get; private set; }
            public int EndCount { get; private set; }
            public int ErrorCount { get; private set; }
            public string LastError { get; private set; }

            public void OnVideoReady(string url) => ReadyCount++;
            public void OnVideoStart(string url) => StartCount++;
            public void OnVideoPause(string url) => PauseCount++;
            public void OnVideoStop(string url) { }
            public void OnVideoEnd(string url) => EndCount++;

            public void OnVideoError(string url, string message)
            {
                ErrorCount++;
                LastError = message;
            }
        }

        private sealed class RecordingObserver : IBackendObserver
        {
            public List<BackendEvent> Events { get; } = new List<BackendEvent>();

            public void OnBackendEvent(BackendEvent backendEvent) => Events.Add(backendEvent);
        }
    }
}
