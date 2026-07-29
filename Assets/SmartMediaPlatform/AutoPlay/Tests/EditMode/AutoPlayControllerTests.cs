using System;
using NUnit.Framework;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;
using SmartMediaPlatform.Video;
using SmartMediaPlatform.Video.Data;

namespace SmartMediaPlatform.AutoPlay.Tests
{
    /// <summary>
    /// Phase3-4: 再生制御の完成形(Ended / Error / Timeout のどれでも止まらないこと)の検証。
    ///
    /// VRChat SDK は使いません。実機の動画プレイヤーの位置に
    /// <see cref="SimulatedVRCVideoPlayer"/> を置き、
    /// 進行は <see cref="VideoEventBridge"/> 経由のイベントだけで行います。
    /// </summary>
    public sealed class AutoPlayControllerTests
    {
        private Rig _rig;

        [SetUp]
        public void SetUp()
        {
            _rig = new Rig(new VideoCatalogSource());
        }

        // ───────── 前提 ─────────

        [Test]
        public void Constructor_RejectsNulls()
        {
            var playback = _rig.Controller.Playback;
            var failures = _rig.Controller.Failures;

            Assert.Throws<ArgumentNullException>(
                () => new AutoPlayController(null, playback, failures));
            Assert.Throws<ArgumentNullException>(
                () => new AutoPlayController(_rig.Session, null, failures));
            Assert.Throws<ArgumentNullException>(
                () => new AutoPlayController(_rig.Session, playback, null));
        }

        [Test]
        public void Controller_ObservesBackendEvents()
        {
            Assert.IsInstanceOf<IBackendObserver>(_rig.Controller);
        }

        [Test]
        public void Create_ComposesThePlayableFilterWithTheFailureTracker()
        {
            var filter = _rig.Controller.Playback.Filter;

            Assert.IsInstanceOf<CompositePlaybackFilter>(filter,
                "「再生できる種別か」と「さっき失敗していないか」は別の関心事なので合成する");
        }

        [Test]
        public void InitialState_IsIdle()
        {
            var rig = new Rig(new VideoCatalogSource());
            Assert.AreEqual(AutoPlayState.Idle, rig.Controller.State);
        }

        // ───────── 通常の再生 ─────────

        [Test]
        public void Start_BeginsPlaying()
        {
            Assert.IsTrue(_rig.Controller.Start("video-001"));

            Assert.AreEqual(AutoPlayState.Playing, _rig.Controller.State);
            Assert.AreEqual("video-001", _rig.Controller.CurrentMediaId);
            Assert.AreEqual(1, _rig.Controller.PlayedCount);
        }

        [Test]
        public void Start_FailsWhenNothingIsPlayable()
        {
            var rig = new Rig(new VideoCatalogSource(), nothingPlayable: true);

            Assert.IsFalse(rig.Controller.Start());
            Assert.AreEqual(AutoPlayState.Failed, rig.Controller.State);
        }

        [Test]
        public void EndedEvent_AdvancesWithoutAnyOtherTrigger()
        {
            _rig.Controller.Start("video-001");
            string first = _rig.Controller.CurrentMediaId;

            _rig.FinishCurrentVideo();

            Assert.AreNotEqual(first, _rig.Controller.CurrentMediaId);
            Assert.AreEqual(AutoPlayState.Playing, _rig.Controller.State);
            Assert.AreEqual(1, _rig.Controller.EndedCount);
        }

        [Test]
        public void PauseAndResume_AreReflectedInTheState()
        {
            _rig.Controller.Start("video-001");

            Assert.IsTrue(_rig.Controller.Pause());
            Assert.AreEqual(AutoPlayState.Paused, _rig.Controller.State);

            Assert.IsTrue(_rig.Controller.Resume());
            Assert.AreEqual(AutoPlayState.Playing, _rig.Controller.State);
        }

        [Test]
        public void Stop_LeavesTheLoop()
        {
            _rig.Controller.Start("video-001");
            _rig.Controller.Stop();

            Assert.AreEqual(AutoPlayState.Stopped, _rig.Controller.State);

            // 止めたあとは Tick が何もしない
            _rig.Controller.Tick(1f);
            Assert.AreEqual(AutoPlayState.Stopped, _rig.Controller.State);
        }

        // ───────── Error からの復帰 ─────────

        [Test]
        public void ErrorEvent_AdvancesToTheNextVideo()
        {
            _rig.Controller.Start("video-001");
            string failing = _rig.Controller.CurrentMediaId;

            _rig.FailCurrentVideo(VideoErrorKind.InvalidUrl);

            Assert.AreNotEqual(failing, _rig.Controller.CurrentMediaId, "次の候補へ進む");
            Assert.AreEqual(AutoPlayState.Playing, _rig.Controller.State, "再生が続いている");
            Assert.AreEqual(1, _rig.Controller.FailureCount);
            Assert.AreEqual(1, _rig.Controller.RecoveredCount);
        }

        [Test]
        public void ErrorEvent_RemembersTheFailedVideo()
        {
            _rig.Controller.Start("video-001");
            _rig.FailCurrentVideo(VideoErrorKind.AccessDenied);

            Assert.IsTrue(_rig.Controller.Failures.IsBlocked("video-001"));
            Assert.AreEqual(1, _rig.Controller.Failures.FailureCountOf("video-001"));
        }

        [Test]
        public void FailedVideo_IsNotQueuedAgainWhileBlocked()
        {
            _rig.Controller.Start("video-001");
            _rig.FailCurrentVideo(VideoErrorKind.InvalidUrl);

            _rig.Session.ClearQueue();
            _rig.Session.EnsureQueueFilled();

            foreach (var entry in _rig.Session.Queue.GetAll())
            {
                Assert.AreNotEqual("video-001", entry.MediaId, "失敗直後の動画は積み直さない");
            }
        }

        [Test]
        public void BlockedVideo_ComesBackAfterTheCooldown()
        {
            _rig.Controller.Failures.CooldownSeconds = 10f;
            _rig.Controller.Start("video-001");
            _rig.FailCurrentVideo(VideoErrorKind.InvalidUrl);

            Assert.IsTrue(_rig.Controller.Failures.IsBlocked("video-001"));

            _rig.Controller.Tick(11f);

            Assert.IsFalse(_rig.Controller.Failures.IsBlocked("video-001"),
                "一定時間が過ぎたら再挑戦できる");
        }

        [Test]
        public void SuccessfulPlayback_ForgetsAPreviousFailure()
        {
            _rig.Controller.Failures.CooldownSeconds = 1f;
            _rig.Controller.Start("video-001");
            _rig.FailCurrentVideo(VideoErrorKind.PlayerError);
            _rig.Controller.Tick(2f);

            // video-001 まで一周させる代わりに、直接もう一度再生させる
            _rig.Controller.Failures.MarkSucceeded("video-001");

            Assert.AreEqual(0, _rig.Controller.Failures.FailureCountOf("video-001"));
        }

        [Test]
        public void ConsecutiveErrors_KeepAdvancingUntilSomethingPlays()
        {
            _rig.Controller.Start("video-001");

            // 3 本続けて失敗しても、4 本目で再生が続いている
            for (int i = 0; i < 3; i++) _rig.FailCurrentVideo(VideoErrorKind.InvalidUrl);

            Assert.AreEqual(AutoPlayState.Playing, _rig.Controller.State);
            Assert.AreEqual(3, _rig.Controller.FailureCount);
            Assert.AreEqual(0, _rig.Controller.ConsecutiveFailures,
                "1 本でも再生できたら連続失敗の数はリセットされる");
        }

        [Test]
        public void TooManyConsecutiveFailures_StopTheLoop()
        {
            _rig.Controller.MaxConsecutiveFailures = 3;
            _rig.Controller.Start("video-001");

            // 再生が始まらないまま失敗し続ける状況を作る
            _rig.PlayerFailsEverything = true;
            for (int i = 0; i < 6; i++) _rig.FailCurrentVideo(VideoErrorKind.InvalidUrl);

            Assert.AreEqual(AutoPlayState.Failed, _rig.Controller.State,
                "候補を無限に試し続けて固まらない");
        }

        [Test]
        public void ErrorRecovery_DoesNotRecurseIntoAStackOverflow()
        {
            _rig.Controller.MaxConsecutiveFailures = 0;   // 無制限
            _rig.Controller.Start("video-001");
            _rig.PlayerFailsEverything = true;

            // 立て直すたびに即座に失敗する状況(再帰していれば落ちる)
            Assert.DoesNotThrow(() => _rig.FailCurrentVideo(VideoErrorKind.InvalidUrl));
        }

        // ───────── Timeout からの復帰 ─────────

        [Test]
        public void Timeout_AdvancesToTheNextVideo()
        {
            _rig.Controller.LoadWatchdogSeconds = 5f;
            _rig.PlayerNeverBecomesReady = true;
            _rig.Controller.Start("video-001");

            Assert.AreEqual(BackendState.Loading, _rig.Session.BackendState);

            _rig.PlayerNeverBecomesReady = false;
            _rig.Controller.Tick(6f);

            Assert.AreEqual(1, _rig.Controller.TimeoutCount);
            Assert.AreEqual(AutoPlayState.Playing, _rig.Controller.State,
                "読み込みが終わらなくても次へ進む");
        }

        [Test]
        public void Timeout_RemembersTheStuckVideo()
        {
            _rig.Controller.LoadWatchdogSeconds = 5f;
            _rig.PlayerNeverBecomesReady = true;
            _rig.Controller.Start("video-001");

            _rig.PlayerNeverBecomesReady = false;
            _rig.Controller.Tick(6f);

            Assert.IsTrue(_rig.Controller.Failures.IsBlocked("video-001"));
        }

        [Test]
        public void Watchdog_CanBeTurnedOff()
        {
            _rig.Controller.LoadWatchdogSeconds = 0f;
            _rig.PlayerNeverBecomesReady = true;
            _rig.Controller.Start("video-001");

            _rig.Controller.Tick(120f);

            Assert.AreEqual(0, _rig.Controller.TimeoutCount);
        }

        [Test]
        public void Watchdog_DoesNotFireWhilePlaying()
        {
            _rig.Controller.LoadWatchdogSeconds = 1f;
            _rig.Controller.Start("video-001");

            for (int i = 0; i < 20; i++) _rig.Controller.Tick(1f);

            Assert.AreEqual(0, _rig.Controller.TimeoutCount);
            Assert.AreEqual(AutoPlayState.Playing, _rig.Controller.State);
        }

        // ───────── Queue が枯渇しない ─────────

        [Test]
        public void Tick_KeepsTheQueueTopped()
        {
            _rig.Controller.Start("video-001");
            _rig.Session.ClearQueue();

            _rig.Controller.Tick(0.016f);

            Assert.GreaterOrEqual(_rig.Session.Queue.Count, 2);
        }

        [Test]
        public void Queue_NeverRunsDryDuringALongLoop()
        {
            _rig.Controller.Start("video-001");

            for (int i = 0; i < 40; i++)
            {
                _rig.FinishCurrentVideo();
                Assert.GreaterOrEqual(_rig.Session.Queue.Count, 1,
                    $"{i + 1} 本目で Queue が空になった");
            }
        }

        // ───────── 長時間耐久 ─────────

        [Test]
        public void Endurance_PlaysMoreThanOneHundredVideos()
        {
            _rig.Controller.Start("video-001");

            for (int i = 0; i < 120; i++)
            {
                _rig.FinishCurrentVideo();

                Assert.AreEqual(AutoPlayState.Playing, _rig.Controller.State,
                    $"{i + 1} 本目で止まった: {_rig.Controller.Describe()}");
                Assert.IsNotNull(_rig.Controller.CurrentMediaId);
            }

            Assert.AreEqual(120, _rig.Controller.EndedCount);
            Assert.GreaterOrEqual(_rig.Controller.PlayedCount, 120);
        }

        [Test]
        public void Endurance_SurvivesFailuresMixedIntoTheLoop()
        {
            _rig.Controller.Failures.CooldownSeconds = 5f;
            _rig.Controller.Start("video-001");

            for (int i = 0; i < 120; i++)
            {
                _rig.Controller.Tick(10f);   // クールダウンを進める

                // 3 本に 1 本は失敗させる
                if (i % 3 == 2) _rig.FailCurrentVideo(VideoErrorKind.InvalidUrl);
                else _rig.FinishCurrentVideo();

                Assert.AreEqual(AutoPlayState.Playing, _rig.Controller.State,
                    $"{i + 1} 本目で止まった: {_rig.Controller.Describe()}");
            }

            Assert.Greater(_rig.Controller.FailureCount, 30);
            Assert.Greater(_rig.Controller.RecoveredCount, 30);
        }

        [Test]
        public void Endurance_KeepsMemoryBounded()
        {
            _rig.Controller.Failures.MaxTracked = 16;
            _rig.Controller.Failures.CooldownSeconds = 5f;
            _rig.Controller.Start("video-001");

            for (int i = 0; i < 200; i++)
            {
                _rig.Controller.Tick(10f);
                if (i % 2 == 0) _rig.FailCurrentVideo(VideoErrorKind.InvalidUrl);
                else _rig.FinishCurrentVideo();
            }

            Assert.LessOrEqual(_rig.Controller.Failures.TrackedCount, 16,
                "失敗の記憶が増え続けない");
            Assert.LessOrEqual(_rig.Session.Queue.Count, _rig.Session.TargetQueueCount + 2,
                "Queue が肥大化しない");
            Assert.LessOrEqual(_rig.Session.History.Count, _rig.Session.MaxHistory,
                "履歴が増え続けない");
            Assert.LessOrEqual(_rig.Controller.Playback.Recent.Count,
                _rig.Controller.Playback.RecentMemory,
                "直近の記憶が増え続けない");
        }

        [Test]
        public void Endurance_RecommendationKeepsWorking()
        {
            _rig.Controller.Start("video-001");

            for (int i = 0; i < 100; i++) _rig.FinishCurrentVideo();

            Assert.Greater(_rig.Controller.Playback.EnqueuedByRecommendation, 50,
                "おすすめが継続して候補を出している");
        }

        [Test]
        public void Endurance_TicksDoNotBreakAnything()
        {
            _rig.Controller.Start("video-001");

            for (int i = 0; i < 100; i++)
            {
                _rig.Controller.Tick(0.5f);
                _rig.FinishCurrentVideo();
            }

            Assert.AreEqual(AutoPlayState.Playing, _rig.Controller.State);
        }

        // ───────── 手動スキップ ─────────

        [Test]
        public void SkipToNext_MovesOnWithoutMarkingAFailure()
        {
            _rig.Controller.Start("video-001");
            string first = _rig.Controller.CurrentMediaId;

            Assert.IsTrue(_rig.Controller.SkipToNext("利用者の操作"));

            Assert.AreNotEqual(first, _rig.Controller.CurrentMediaId);
            Assert.AreEqual(0, _rig.Controller.Failures.TotalFailures);
        }

        [Test]
        public void SkipToNext_CanMarkAFailure()
        {
            _rig.Controller.Start("video-001");

            _rig.Controller.SkipToNext("見たくない", markAsFailure: true);

            Assert.IsTrue(_rig.Controller.Failures.IsBlocked("video-001"));
        }

        // ───────── 診断 ─────────

        [Test]
        public void Describe_ReportsTheLoopState()
        {
            _rig.Controller.Start("video-001");
            _rig.FinishCurrentVideo();

            string text = _rig.Controller.Describe();

            StringAssert.Contains("ended=1", text);
            StringAssert.Contains("played=", text);
        }

        // ───────── 上位の責務を壊していない ─────────

        [Test]
        public void PlayerSession_StillDecidesWhatPlaysNext()
        {
            _rig.Controller.Start("video-001");
            _rig.FinishCurrentVideo();

            // 遷移そのものは PlayerSession が行っている(履歴が積まれるのがその証拠)
            Assert.GreaterOrEqual(_rig.Session.History.Count, 1);
            Assert.AreEqual(PlaybackState.Playing, _rig.Session.PlaybackState);
        }

        [Test]
        public void BackendAdapter_IsUsedUnchanged()
        {
            _rig.Controller.Start("video-001");

            Assert.IsInstanceOf<IMediaBackend>(_rig.Adapter);
            Assert.AreEqual(BackendState.Playing, _rig.Adapter.GetState());
        }

        // ───────── 組み立て ─────────

        private sealed class Rig
        {
            public readonly IMediaCatalog Catalog;
            public readonly PlayerSession Session;
            public readonly AutoPlayController Controller;
            public readonly VRChatVideoBackend Backend;
            public readonly VideoEventBridge Bridge;
            public readonly VideoBackendAdapter Adapter;

            private readonly SimulatedVRCVideoPlayer _player;

            /// <summary>true にすると、どの動画も読み込みが終わらなくなる。</summary>
            public bool PlayerNeverBecomesReady
            {
                get => !_player.AutoCompleteLoading;
                set => _player.AutoCompleteLoading = !value;
            }

            /// <summary>true にすると、どの動画も読み込みに失敗する(URL が軒並み切れた状態)。</summary>
            public bool PlayerFailsEverything
            {
                get => _player.FailAllLoads;
                set => _player.FailAllLoads = value;
            }

            public Rig(IMediaCatalogSource source, bool nothingPlayable = false)
            {
                var logger = new ListBackendLogger();
                Catalog = new MediaCatalog(source, new System.Random(1));

                var urls = nothingPlayable
                    ? new CatalogVideoUrlTable(new string[0])
                    : new CatalogVideoUrlTable(Catalog);

                _player = new SimulatedVRCVideoPlayer(urls) { AutoCompleteLoading = true };
                Backend = new VRChatVideoBackend("VRChatVideoBackend", _player, urls, logger)
                {
                    LoadTimeoutSeconds = 0f,   // 見張りは Controller 側で試す
                };
                Bridge = new VideoEventBridge(Backend, logger);
                Adapter = new VideoBackendAdapter("VideoAdapter", Backend, logger);

                var queue = new MediaQueue();
                var backendManager = new BackendManager(queue, logger);
                var mediaPlayer = new MediaPlayer(backendManager, logger);
                var engine = new RecommendationEngine(
                    Catalog, RecommendationRule.CreateDefault(), new System.Random(1));
                Session = new PlayerSession(
                    "test", mediaPlayer, Catalog, engine, new System.Random(1), logger);

                Controller = AutoPlayController.Create(
                    Session, Catalog, engine, new BackendPlaybackFilter(backendManager), logger);
                Controller.RegisterBackend(Adapter);
            }

            /// <summary>いま再生中の動画を最後まで再生させ、実機の OnVideoEnd を流す。</summary>
            public void FinishCurrentVideo()
            {
                EnsureReady();
                _player.FinishPlayback();
                Bridge.OnVideoEnd();
            }

            /// <summary>いま再生中の動画を失敗させ、実機の OnVideoError を流す。</summary>
            public void FailCurrentVideo(VideoErrorKind kind)
            {
                Bridge.OnVideoError(kind);
            }

            private void EnsureReady()
            {
                if (PlayerNeverBecomesReady) return;

                if (Backend.GetState() == VideoPlayerState.Loading)
                {
                    _player.CompleteLoading();
                    Bridge.OnVideoReady();
                }
            }
        }
    }
}
