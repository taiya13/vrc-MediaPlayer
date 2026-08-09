using System;
using System.Collections.Generic;
using NUnit.Framework;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;

namespace SmartMediaPlatform.Video.Tests
{
    /// <summary>
    /// Phase3-1: VRChat の動画プレイヤーを使う <see cref="VRChatVideoBackend"/> の検証。
    ///
    /// VRChat SDK は使いません。<see cref="SimulatedVRCVideoPlayer"/> が
    /// 実機と同じ「非同期読み込み・時間経過・自然終了・失敗」を再現するので、
    /// 状態機械と通知を決定的に確かめられます
    /// (Audio 層が <c>IAudioPlayer</c> を差し替えるのと同じやり方)。
    /// </summary>
    public sealed class VRChatVideoBackendTests
    {
        private IMediaCatalog _catalog;
        private CatalogVideoUrlTable _urls;
        private ListBackendLogger _logger;
        private SimulatedVRCVideoPlayer _player;
        private VRChatVideoBackend _backend;
        private VideoObserver _observer;

        /// <summary>Catalog に登録済みの動画 URL。</summary>
        private string Url => _catalog.FindById("video-001").Url;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _urls = new CatalogVideoUrlTable(_catalog);
            _logger = new ListBackendLogger();
            _player = new SimulatedVRCVideoPlayer(_urls);
            _backend = new VRChatVideoBackend("VRChatVideoBackend", _player, _urls, _logger);
            _observer = new VideoObserver();
            _backend.AddObserver(_observer);
        }

        // ───────── 前提 ─────────

        [Test]
        public void Backend_IsAVideoBackend()
        {
            Assert.IsInstanceOf<IVideoBackend>(_backend,
                "VideoBackendAdapter がそのまま包める(= DummyVideoBackend と差し替えられる)");
        }

        [Test]
        public void Constructor_TurnsLoopOff()
        {
            _player.Loop = true;

            var backend = new VRChatVideoBackend("x", _player, _urls, _logger);

            Assert.IsFalse(_player.Loop,
                "ループしたままだと動画が終わらず Ended が発火しない");
            Assert.IsNotNull(backend);
        }

        [Test]
        public void Constructor_RejectsNulls()
        {
            Assert.Throws<ArgumentNullException>(
                () => new VRChatVideoBackend("x", null, _urls, _logger));
            Assert.Throws<ArgumentNullException>(
                () => new VRChatVideoBackend("x", _player, null, _logger));
        }

        [Test]
        public void InitialState_IsNone()
        {
            Assert.AreEqual(VideoPlayerState.None, _backend.GetState());
            Assert.IsNull(_backend.GetCurrentUrl());
            Assert.AreEqual(0f, _backend.GetDuration(), 0.001f);
            Assert.AreEqual(0f, _backend.GetTime(), 0.001f);
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

        // ───────── CanPlay: 事前登録された URL だけ ─────────

        [Test]
        public void CanPlay_AcceptsUrlsRegisteredInTheCatalog()
        {
            Assert.IsTrue(_backend.CanPlay(Url));
        }

        [Test]
        public void CanPlay_RejectsUrlsThatWereNotBaked()
        {
            Assert.IsFalse(_backend.CanPlay("https://example.com/generated-at-runtime"),
                "実行時に VRCUrl は作れないので、ベイクされていない URL は再生できない");
            Assert.IsFalse(_backend.CanPlay(""));
            Assert.IsFalse(_backend.CanPlay(null));
            Assert.IsFalse(_backend.CanPlay("   "));
        }

        [Test]
        public void UrlTable_ContainsEveryVideoInTheCatalog()
        {
            var videos = _catalog.FilterByType(MediaType.Video);

            Assert.Greater(videos.Count, 0);
            foreach (var video in videos)
            {
                Assert.IsTrue(_urls.Contains(video.Url), $"{video.Id} がベイクされていない");
            }
        }

        [Test]
        public void CanPlay_RejectsLiveStreamsOnTheUnityPlayer()
        {
            const string live = "rtsp://example.com/live/stage-a";
            var table = new CatalogVideoUrlTable(new[] { live });

            var unity = new VRChatVideoBackend(
                "unity", new SimulatedVRCVideoPlayer(table, VRCVideoPlayerKind.Unity), table);
            var avpro = new VRChatVideoBackend(
                "avpro", new SimulatedVRCVideoPlayer(table, VRCVideoPlayerKind.AVPro), table);

            Assert.IsFalse(unity.CanPlay(live), "VRCUnityVideoPlayer は生配信を扱えない");
            Assert.IsTrue(avpro.CanPlay(live), "VRCAVProVideoPlayer なら扱える");
        }

        [Test]
        public void IsLiveStreamUrl_DetectsStreamingSchemes()
        {
            Assert.IsTrue(VRChatVideoBackend.IsLiveStreamUrl("rtsp://example.com/a"));
            Assert.IsTrue(VRChatVideoBackend.IsLiveStreamUrl("RTMP://example.com/a"));
            Assert.IsTrue(VRChatVideoBackend.IsLiveStreamUrl("https://example.com/a.m3u8"));
            Assert.IsFalse(VRChatVideoBackend.IsLiveStreamUrl("https://example.com/a.mp4"));
            Assert.IsFalse(VRChatVideoBackend.IsLiveStreamUrl(null));
        }

        // ───────── Load(実機と同じ非同期) ─────────

        [Test]
        public void Load_StaysLoadingUntilThePlayerIsReady()
        {
            Assert.IsTrue(_backend.Load(Url));

            Assert.AreEqual(VideoPlayerState.Loading, _backend.GetState(),
                "実機の動画プレイヤーは Load 直後には再生できない");
            Assert.AreEqual(Url, _backend.GetCurrentUrl());
            Assert.AreEqual(0, _observer.ReadyCount);
        }

        [Test]
        public void Load_CompletesSynchronouslyWhenThePlayerIsAlreadyReady()
        {
            _player.AutoCompleteLoading = true;

            Assert.IsTrue(_backend.Load(Url));

            Assert.AreEqual(VideoPlayerState.Ready, _backend.GetState());
            Assert.AreEqual(1, _observer.ReadyCount);
        }

        [Test]
        public void Load_RejectsUrlsThatWereNotBaked_AndReportsAnError()
        {
            Assert.IsFalse(_backend.Load("https://example.com/not-baked"));

            Assert.AreEqual(VideoPlayerState.Failed, _backend.GetState());
            Assert.AreEqual(VideoErrorKind.InvalidUrl, _backend.LastErrorKind);
            Assert.AreEqual(1, _observer.ErrorCount);
            StringAssert.Contains("InvalidUrl", _observer.LastError);
        }

        [Test]
        public void Load_ReportsAnErrorWhenTheBakedUrlCannotBeResolved()
        {
            _player.FailNextLoad = true;

            Assert.IsFalse(_backend.Load(Url));

            Assert.AreEqual(VideoPlayerState.Failed, _backend.GetState());
            Assert.AreEqual(VideoErrorKind.InvalidUrl, _backend.LastErrorKind);
            Assert.IsNull(_backend.GetCurrentUrl(), "読み込めなかった URL は保持しない");
        }

        [Test]
        public void Load_StopsThePreviousVideoFirst()
        {
            Ready();
            _backend.Play();
            Assert.IsTrue(_player.IsPlaying);

            _backend.Load(Url);

            Assert.IsFalse(_player.IsPlaying, "前の動画の音が残らないよう必ず止める");
        }

        // ───────── 読み込み完了の 2 経路 ─────────

        [Test]
        public void NotifyVideoReady_MovesToReady()
        {
            _backend.Load(Url);

            _backend.NotifyVideoReady();   // 実機の OnVideoReady

            Assert.AreEqual(VideoPlayerState.Ready, _backend.GetState());
            Assert.AreEqual(1, _observer.ReadyCount);
        }

        [Test]
        public void Tick_PicksUpReadinessWhenNoCallbackArrives()
        {
            _backend.Load(Url);
            _player.CompleteLoading();   // プレイヤーは Ready になったが通知は来ない

            _backend.Tick(0.016f);

            Assert.AreEqual(VideoPlayerState.Ready, _backend.GetState());
            Assert.AreEqual(1, _observer.ReadyCount);
        }

        [Test]
        public void ReadyIsNotifiedOnlyOnce_EvenWithBothPaths()
        {
            _backend.Load(Url);
            _player.CompleteLoading();

            _backend.NotifyVideoReady();
            _backend.Tick(0.016f);
            _backend.NotifyVideoReady();

            Assert.AreEqual(1, _observer.ReadyCount, "プッシュとポーリングで二重に通知しない");
        }

        // ───────── Play / Pause / Resume / Stop ─────────

        [Test]
        public void Play_StartsThePlayer()
        {
            Ready();

            Assert.IsTrue(_backend.Play());

            Assert.AreEqual(VideoPlayerState.Playing, _backend.GetState());
            Assert.IsTrue(_player.IsPlaying, "実機の動画プレイヤーへ Play が伝わる");
            Assert.AreEqual(1, _backend.PlayCallCount);
            CollectionAssert.Contains(_observer.Calls, "Start");
        }

        [Test]
        public void Play_WhileLoading_IsQueuedAndRunsWhenReady()
        {
            _backend.Load(Url);

            Assert.IsTrue(_backend.Play(), "読み込み中の Play は「予約」として受け付ける");
            Assert.IsTrue(_backend.IsPlayPending);
            Assert.IsFalse(_player.IsPlaying);

            _player.CompleteLoading();
            _backend.Tick(0.016f);

            Assert.AreEqual(VideoPlayerState.Playing, _backend.GetState(),
                "MediaPlayer.Play() の「読み込んですぐ再生」を Backend 側で吸収する");
            Assert.IsTrue(_player.IsPlaying);
            Assert.IsFalse(_backend.IsPlayPending);
        }

        [Test]
        public void Play_WhileLoading_IsRejectedWhenAutoPlayIsOff()
        {
            _backend.AutoPlayWhenReady = false;
            _backend.Load(Url);

            Assert.IsFalse(_backend.Play(), "DummyVideoBackend と同じ振る舞いに戻せる");
            Assert.IsFalse(_backend.IsPlayPending);

            _player.CompleteLoading();
            _backend.Tick(0.016f);

            Assert.AreEqual(VideoPlayerState.Ready, _backend.GetState());
        }

        [Test]
        public void Play_IsIgnoredWhilePlayingOrPaused()
        {
            Ready();
            _backend.Play();

            Assert.IsFalse(_backend.Play(), "すでに再生中");

            _backend.Pause();
            Assert.IsFalse(_backend.Play(), "一時停止中は Resume を使う");
            Assert.AreEqual(1, _backend.PlayCallCount);
        }

        [Test]
        public void Pause_StopsTheClockAndKeepsThePosition()
        {
            Ready();
            _backend.Play();
            _player.Advance(30f);

            Assert.IsTrue(_backend.Pause());

            Assert.AreEqual(VideoPlayerState.Paused, _backend.GetState());
            Assert.IsFalse(_player.IsPlaying);
            Assert.AreEqual(30f, _backend.GetTime(), 0.001f);
            CollectionAssert.Contains(_observer.Calls, "Pause");
        }

        [Test]
        public void Resume_ContinuesFromTheSamePosition()
        {
            Ready();
            _backend.Play();
            _player.Advance(30f);
            _backend.Pause();

            Assert.IsTrue(_backend.Resume());

            Assert.AreEqual(VideoPlayerState.Playing, _backend.GetState());
            Assert.IsTrue(_player.IsPlaying);
            Assert.AreEqual(30f, _backend.GetTime(), 0.001f, "頭出しはしない");
        }

        [Test]
        public void Resume_IsIgnoredWhenNotPaused()
        {
            Ready();
            Assert.IsFalse(_backend.Resume());

            _backend.Play();
            Assert.IsFalse(_backend.Resume());
        }

        [Test]
        public void Stop_ResetsThePosition()
        {
            Ready();
            _backend.Play();
            _player.Advance(30f);

            Assert.IsTrue(_backend.Stop());

            Assert.AreEqual(VideoPlayerState.Stopped, _backend.GetState());
            Assert.IsFalse(_player.IsPlaying);
            Assert.AreEqual(0f, _backend.GetTime(), 0.001f);
            CollectionAssert.Contains(_observer.Calls, "Stop");
        }

        [Test]
        public void Stop_CancelsAPendingPlay()
        {
            _backend.Load(Url);
            _backend.Play();
            Assert.IsTrue(_backend.IsPlayPending);

            _backend.Stop();
            _player.CompleteLoading();
            _backend.Tick(0.016f);

            Assert.IsFalse(_player.IsPlaying, "Stop したのに後から再生が始まってはいけない");
        }

        [Test]
        public void Play_AfterStop_RestartsFromTheBeginning()
        {
            Ready();
            _backend.Play();
            _player.Advance(30f);
            _backend.Stop();

            Assert.IsTrue(_backend.Play());

            Assert.AreEqual(0f, _backend.GetTime(), 0.001f);
            Assert.AreEqual(2, _backend.PlayCallCount);
        }

        // ───────── Seek ─────────

        [Test]
        public void Seek_ClampsToTheDuration()
        {
            Ready();
            _backend.Play();

            Assert.IsTrue(_backend.Seek(45f));
            Assert.AreEqual(45f, _backend.GetTime(), 0.001f);

            _backend.Seek(-10f);
            Assert.AreEqual(0f, _backend.GetTime(), 0.001f);

            _backend.Seek(9999f);
            Assert.AreEqual(_backend.GetDuration(), _backend.GetTime(), 0.001f);
        }

        [Test]
        public void CanSeek_IsFalseForLiveStreamsWithoutADuration()
        {
            _player.DurationSeconds = 0f;   // 生配信は長さを答えられない
            Ready();

            Assert.IsFalse(_backend.CanSeek);
            Assert.IsFalse(_backend.Seek(10f));
            Assert.AreEqual(0f, _backend.GetDuration(), 0.001f);
        }

        [Test]
        public void GetDuration_NormalisesUnknownLengths()
        {
            _player.DurationSeconds = float.PositiveInfinity;
            Ready();

            Assert.AreEqual(0f, _backend.GetDuration(), 0.001f,
                "無限大は「長さ不明」として 0 に揃える(Adapter が Catalog で補える)");
        }

        // ───────── Ended ─────────

        [Test]
        public void NotifyVideoEnd_FinishesPlayback()
        {
            Ready();
            _backend.Play();

            _backend.NotifyVideoEnd();   // 実機の OnVideoEnd

            Assert.AreEqual(VideoPlayerState.Finished, _backend.GetState());
            Assert.AreEqual(1, _observer.EndCount);
        }

        [Test]
        public void Tick_DetectsTheEndWhenNoCallbackArrives()
        {
            Ready();
            _backend.Play();

            // プレイヤーが終端まで進んで自分で止まる(実機と同じ)
            _player.Advance(_player.DurationSeconds + 1f);
            _backend.Tick(0.016f);

            Assert.AreEqual(VideoPlayerState.Finished, _backend.GetState());
            Assert.AreEqual(1, _observer.EndCount);
        }

        [Test]
        public void EndIsNotifiedOnlyOnce()
        {
            Ready();
            _backend.Play();

            _backend.NotifyVideoEnd();
            _player.FinishPlayback();
            _backend.Tick(0.016f);
            _backend.NotifyVideoEnd();

            Assert.AreEqual(1, _observer.EndCount);
        }

        [Test]
        public void Tick_DoesNotReportTheEndWhilePlaybackStallsMidway()
        {
            Ready();
            _backend.Play();
            _player.Advance(10f);

            // バッファ切れなどで途中で止まった(まだ終わっていない)
            _player.Pause();
            _backend.Tick(0.016f);

            Assert.AreEqual(0, _observer.EndCount, "終端に達していないので Ended にしない");
            Assert.AreEqual(VideoPlayerState.Playing, _backend.GetState());
        }

        [Test]
        public void DetectEndByPolling_CanBeTurnedOff()
        {
            _backend.DetectEndByPolling = false;
            Ready();
            _backend.Play();

            _player.Advance(_player.DurationSeconds + 1f);
            _backend.Tick(0.016f);

            Assert.AreEqual(0, _observer.EndCount, "通知だけを信じる構成にもできる");
        }

        // ───────── Error ─────────

        [Test]
        public void NotifyVideoError_TranslatesEachReason()
        {
            Ready();

            _backend.NotifyVideoError(VideoErrorKind.AccessDenied);

            Assert.AreEqual(VideoPlayerState.Failed, _backend.GetState());
            Assert.AreEqual(VideoErrorKind.AccessDenied, _backend.LastErrorKind);
            Assert.AreEqual(1, _observer.ErrorCount);
            StringAssert.Contains("AccessDenied", _observer.LastError);
        }

        [Test]
        public void NotifyVideoError_KeepsACustomMessage()
        {
            Ready();

            _backend.NotifyVideoError(VideoErrorKind.RateLimited, "読み込みが多すぎます");

            StringAssert.Contains("読み込みが多すぎます", _observer.LastError);
        }

        [Test]
        public void Tick_ReportsATimeoutWhenLoadingNeverCompletes()
        {
            _backend.LoadTimeoutSeconds = 5f;
            _backend.Load(Url);

            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(VideoPlayerState.Loading, _backend.GetState());
                _backend.Tick(1f);
            }

            Assert.AreEqual(VideoPlayerState.Failed, _backend.GetState());
            Assert.AreEqual(VideoErrorKind.Timeout, _backend.LastErrorKind);
            Assert.AreEqual(1, _observer.ErrorCount);
        }

        [Test]
        public void LoadTimeout_CanBeDisabled()
        {
            _backend.LoadTimeoutSeconds = 0f;
            _backend.Load(Url);

            for (int i = 0; i < 100; i++) _backend.Tick(1f);

            Assert.AreEqual(VideoPlayerState.Loading, _backend.GetState());
            Assert.AreEqual(0, _observer.ErrorCount);
        }

        [Test]
        public void ErrorClearsAPendingPlay()
        {
            _backend.Load(Url);
            _backend.Play();

            _backend.NotifyVideoError(VideoErrorKind.PlayerError);

            Assert.IsFalse(_backend.IsPlayPending);

            _player.CompleteLoading();
            _backend.Tick(0.016f);
            Assert.IsFalse(_player.IsPlaying, "失敗した動画が後から鳴り出さない");
        }

        // ───────── 観測者 ─────────

        [Test]
        public void Observers_CanBeRemoved()
        {
            _backend.RemoveObserver(_observer);
            Ready();
            _backend.Play();
            _backend.NotifyVideoEnd();

            Assert.AreEqual(0, _observer.EndCount);
        }

        [Test]
        public void Observers_AreNotAddedTwice()
        {
            _backend.AddObserver(_observer);
            _backend.AddObserver(null);
            Ready();
            _backend.Play();
            _backend.NotifyVideoEnd();

            Assert.AreEqual(1, _observer.EndCount);
        }

        // ───────── 補助 ─────────

        /// <summary>読み込みが終わって再生できる状態まで進める。</summary>
        private void Ready()
        {
            _backend.Load(Url);
            _player.CompleteLoading();
            _backend.Tick(0.016f);
        }

        /// <summary>テスト用に通知を記録する観測者。</summary>
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
