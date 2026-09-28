using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Backend;

namespace SmartMediaPlatform.Video.Tests
{
    /// <summary>DummyVideoBackend(動画プレイヤー側)の状態遷移と通知の検証。</summary>
    public sealed class DummyVideoBackendTests
    {
        private const string Url = "https://example.com/media/sample";

        private ListBackendLogger _logger;
        private DummyVideoBackend _backend;
        private VideoObserver _observer;

        [SetUp]
        public void SetUp()
        {
            _logger = new ListBackendLogger();
            _backend = new DummyVideoBackend("DummyVideoBackend", _logger);
            _observer = new VideoObserver();
            _backend.AddObserver(_observer);
        }

        // ───────── 初期状態 ─────────

        [Test]
        public void InitialState_IsNone()
        {
            Assert.AreEqual(VideoPlayerState.None, _backend.GetState());
            Assert.IsNull(_backend.GetCurrentUrl());
            Assert.AreEqual(0f, _backend.GetDuration(), 0.001f);
        }

        [Test]
        public void OperationsBeforeLoad_AreRejected()
        {
            Assert.IsFalse(_backend.Play());
            Assert.IsFalse(_backend.Pause());
            Assert.IsFalse(_backend.Resume());
            Assert.IsFalse(_backend.Stop());
            Assert.IsFalse(_backend.Seek(10f));
            Assert.AreEqual(VideoPlayerState.None, _backend.GetState());
        }

        // ───────── CanPlay ─────────

        [Test]
        public void CanPlay_RequiresANonEmptyUrl()
        {
            Assert.IsTrue(_backend.CanPlay(Url));
            Assert.IsFalse(_backend.CanPlay(""));
            Assert.IsFalse(_backend.CanPlay(null));
            Assert.IsFalse(_backend.CanPlay("   "));
        }

        // ───────── Load(非同期の再現) ─────────

        [Test]
        public void Load_ReachesReadyWhenAutoCompleting()
        {
            Assert.IsTrue(_backend.Load(Url));

            Assert.AreEqual(VideoPlayerState.Ready, _backend.GetState());
            Assert.AreEqual(Url, _backend.GetCurrentUrl());
            Assert.AreEqual(1, _observer.ReadyCount);
        }

        [Test]
        public void Load_StaysLoadingWhenNotAutoCompleting()
        {
            _backend.AutoCompleteLoading = false;

            _backend.Load(Url);

            Assert.AreEqual(VideoPlayerState.Loading, _backend.GetState(),
                "実機の非同期読み込みを再現できる");
            Assert.IsFalse(_backend.Play(), "読み込み中は再生できない");

            Assert.IsTrue(_backend.CompleteLoading());
            Assert.AreEqual(VideoPlayerState.Ready, _backend.GetState());
            Assert.IsTrue(_backend.Play());
        }

        [Test]
        public void Load_EmptyUrl_Fails()
        {
            Assert.IsFalse(_backend.Load(""));

            Assert.AreEqual(VideoPlayerState.Failed, _backend.GetState());
            Assert.AreEqual(1, _observer.ErrorCount);
        }

        // ───────── 再生制御 ─────────

        [Test]
        public void PlaybackLifecycle_FollowsTheExpectedStates()
        {
            _backend.Load(Url);

            Assert.IsTrue(_backend.Play());
            Assert.AreEqual(VideoPlayerState.Playing, _backend.GetState());

            Assert.IsTrue(_backend.Pause());
            Assert.AreEqual(VideoPlayerState.Paused, _backend.GetState());

            Assert.IsTrue(_backend.Resume());
            Assert.AreEqual(VideoPlayerState.Playing, _backend.GetState());

            Assert.IsTrue(_backend.Stop());
            Assert.AreEqual(VideoPlayerState.Stopped, _backend.GetState());
        }

        [Test]
        public void Play_WhilePaused_IsRejectedInFavorOfResume()
        {
            _backend.Load(Url);
            _backend.Play();
            _backend.Pause();

            Assert.IsFalse(_backend.Play());
            Assert.AreEqual(VideoPlayerState.Paused, _backend.GetState());
        }

        [Test]
        public void Play_Twice_IsRejected()
        {
            _backend.Load(Url);
            _backend.Play();

            Assert.IsFalse(_backend.Play());
            Assert.AreEqual(1, _backend.PlayCallCount);
        }

        [Test]
        public void Play_AfterStop_StartsAgain()
        {
            _backend.Load(Url);
            _backend.Play();
            _backend.Stop();

            Assert.IsTrue(_backend.Play());
            Assert.AreEqual(2, _backend.PlayCallCount);
        }

        // ───────── Seek(秒指定) ─────────

        [Test]
        public void Seek_MovesToTheGivenSecond()
        {
            _backend.Duration = 200f;
            _backend.Load(Url);
            _backend.Play();

            Assert.IsTrue(_backend.Seek(50f));
            Assert.AreEqual(50f, _backend.GetTime(), 0.001f);
        }

        [Test]
        public void Seek_ClampsToTheDuration()
        {
            _backend.Duration = 100f;
            _backend.Load(Url);

            _backend.Seek(-10f);
            Assert.AreEqual(0f, _backend.GetTime(), 0.001f);

            _backend.Seek(999f);
            Assert.AreEqual(100f, _backend.GetTime(), 0.001f);
        }

        [Test]
        public void Seek_IsRejectedForLiveStreams()
        {
            _backend.CanSeek = false;
            _backend.Load(Url);

            Assert.IsFalse(_backend.Seek(10f), "生配信の振る舞いを再現できる");
        }

        [Test]
        public void Advance_MovesTimeWhilePlaying()
        {
            _backend.Load(Url);
            _backend.Play();

            _backend.Advance(5f);
            Assert.AreEqual(5f, _backend.GetTime(), 0.001f);

            _backend.Pause();
            _backend.Advance(5f);
            Assert.AreEqual(5f, _backend.GetTime(), 0.001f, "停止中は進まない");
        }

        // ───────── 通知 ─────────

        [Test]
        public void Observer_ReceivesTheVideoPlayerCallbacks()
        {
            _backend.Load(Url);
            _backend.Play();
            _backend.Pause();
            _backend.Resume();
            _backend.Stop();

            CollectionAssert.AreEqual(
                new[] { "Ready", "Start", "Pause", "Start", "Stop" }, _observer.Calls);
        }

        [Test]
        public void SimulateFinished_RaisesTheEndCallback()
        {
            _backend.Load(Url);
            _backend.Play();

            Assert.IsTrue(_backend.SimulateFinished());
            Assert.AreEqual(VideoPlayerState.Finished, _backend.GetState());
            Assert.AreEqual(1, _observer.EndCount);
        }

        [Test]
        public void SimulateFinished_WhenNotPlaying_IsIgnored()
        {
            _backend.Load(Url);

            Assert.IsFalse(_backend.SimulateFinished());
            Assert.AreEqual(0, _observer.EndCount);
        }

        [Test]
        public void SimulateError_RaisesTheErrorCallback()
        {
            _backend.Load(Url);

            _backend.SimulateError("読み込みに失敗しました");

            Assert.AreEqual(VideoPlayerState.Failed, _backend.GetState());
            Assert.AreEqual(1, _observer.ErrorCount);
            StringAssert.Contains("読み込みに失敗", _observer.LastError);
        }

        [Test]
        public void RemoveObserver_StopsNotifications()
        {
            _backend.RemoveObserver(_observer);

            _backend.Load(Url);

            Assert.AreEqual(0, _observer.Calls.Count);
        }

        // ───────── ログ ─────────

        [Test]
        public void Logger_RecordsThatNoVideoIsPlayed()
        {
            _backend.Load(Url);
            _backend.Play();

            Assert.IsTrue(_logger.Lines.Any(l => l.Contains("no actual video")),
                "動画を再生しないことがログに残る");
        }

        /// <summary>動画プレイヤーの通知を記録する検証用の観測者。</summary>
        private sealed class VideoObserver : IVideoBackendObserver
        {
            public List<string> Calls { get; } = new List<string>();
            public int ReadyCount { get; private set; }
            public int EndCount { get; private set; }
            public int ErrorCount { get; private set; }
            public string LastError { get; private set; }

            public void OnVideoReady(string url) { Calls.Add("Ready"); ReadyCount++; }
            public void OnVideoStart(string url) { Calls.Add("Start"); }
            public void OnVideoPause(string url) { Calls.Add("Pause"); }
            public void OnVideoStop(string url) { Calls.Add("Stop"); }
            public void OnVideoEnd(string url) { Calls.Add("End"); EndCount++; }

            public void OnVideoError(string url, string message)
            {
                Calls.Add("Error");
                ErrorCount++;
                LastError = message;
            }
        }
    }
}
