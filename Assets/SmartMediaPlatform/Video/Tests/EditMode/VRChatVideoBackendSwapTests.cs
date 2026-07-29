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
    /// Phase3-1 の成功条件そのもの:
    /// <b><see cref="DummyVideoBackend"/> を <see cref="VRChatVideoBackend"/> に
    /// 差し替えるだけで、上位が一切変わらずに動くこと</b>の検証。
    ///
    /// ここで使う <see cref="VideoBackendAdapter"/> / <see cref="BackendManager"/> /
    /// <see cref="MediaPlayer"/> / <see cref="PlayerSession"/> / <see cref="MediaQueue"/> /
    /// <see cref="RecommendationEngine"/> は Phase2 のまま、<b>1 行も変更していません</b>。
    /// </summary>
    public sealed class VRChatVideoBackendSwapTests
    {
        private IMediaCatalog _catalog;
        private CatalogVideoUrlTable _urls;
        private ListBackendLogger _logger;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _urls = new CatalogVideoUrlTable(_catalog);
            _logger = new ListBackendLogger();
        }

        private MediaItem Video => _catalog.FindById("video-001");

        // ───────── 差し替えの証明 ─────────

        [Test]
        public void BothBackends_BehaveIdenticallyThroughTheSameScript()
        {
            var dummy = new DummyVideoBackend("DummyVideoBackend", _logger);
            var vrchat = NewVRChatBackend(out _, autoCompleteLoading: true);

            CollectionAssert.AreEqual(RunScript(dummy), RunScript(vrchat),
                "同じ手順を流したときの状態遷移が一致する = 差し替えても上位に影響しない");
        }

        /// <summary>2 つの Backend に同じ操作を流し、状態の並びを返す。</summary>
        private List<string> RunScript(IVideoBackend backend)
        {
            string url = Video.Url;
            var states = new List<string>
            {
                $"CanPlay={backend.CanPlay(url)}",
                $"Load={backend.Load(url)}/{backend.GetState()}",
                $"Play={backend.Play()}/{backend.GetState()}",
                $"Pause={backend.Pause()}/{backend.GetState()}",
                $"Resume={backend.Resume()}/{backend.GetState()}",
                $"Stop={backend.Stop()}/{backend.GetState()}",
                $"Replay={backend.Play()}/{backend.GetState()}",
            };
            return states;
        }

        [Test]
        public void SwappingTheBackend_DoesNotChangeTheAdapterContract()
        {
            var dummyAdapter = new VideoBackendAdapter(
                "VideoAdapter", new DummyVideoBackend("dummy", _logger), _logger);
            var vrchatAdapter = new VideoBackendAdapter(
                "VideoAdapter", NewVRChatBackend(out _, autoCompleteLoading: true), _logger);

            // 同じ型・同じ契約・同じ扱える種別
            Assert.IsInstanceOf<IBackendAdapter>(vrchatAdapter);
            Assert.IsInstanceOf<IMediaBackend>(vrchatAdapter);
            Assert.IsInstanceOf<ISeekableBackend>(vrchatAdapter);
            CollectionAssert.AreEquivalent(
                dummyAdapter.SupportedTypes.ToArray(), vrchatAdapter.SupportedTypes.ToArray());

            Assert.IsTrue(dummyAdapter.CanPlay(Video));
            Assert.IsTrue(vrchatAdapter.CanPlay(Video));
        }

        // ───────── アダプタは無変更のまま翻訳する ─────────

        [Test]
        public void Adapter_TranslatesMediaItemToTheBakedUrl()
        {
            var backend = NewVRChatBackend(out _, autoCompleteLoading: true);
            var adapter = new VideoBackendAdapter("VideoAdapter", backend, _logger);

            Assert.IsTrue(adapter.Load(Video));

            Assert.AreEqual(Video.Url, backend.GetCurrentUrl());
            Assert.AreSame(Video, adapter.GetCurrent());
        }

        [Test]
        public void Adapter_TranslatesTheVideoStatesIntoBackendStates()
        {
            var backend = NewVRChatBackend(out var player, autoCompleteLoading: false);
            var adapter = new VideoBackendAdapter("VideoAdapter", backend, _logger);

            adapter.Load(Video);
            Assert.AreEqual(BackendState.Loading, adapter.GetState(), "非同期読み込みを偽装しない");

            player.CompleteLoading();
            backend.Tick(0.016f);
            Assert.AreEqual(BackendState.Ready, adapter.GetState());

            adapter.Play();
            Assert.AreEqual(BackendState.Playing, adapter.GetState());

            adapter.Pause();
            Assert.AreEqual(BackendState.Paused, adapter.GetState());

            adapter.Resume();
            backend.NotifyVideoEnd();
            Assert.AreEqual(BackendState.Ended, adapter.GetState(), "Finished -> Ended");

            backend.NotifyVideoError(VideoErrorKind.PlayerError);
            Assert.AreEqual(BackendState.Error, adapter.GetState(), "Failed -> Error");
        }

        [Test]
        public void Adapter_TranslatesNormalisedSeekIntoSeconds()
        {
            var backend = NewVRChatBackend(out var player, autoCompleteLoading: true);
            player.DurationSeconds = 200f;
            var adapter = new VideoBackendAdapter("VideoAdapter", backend, _logger);

            adapter.Load(Video);
            adapter.Play();

            Assert.IsTrue(adapter.Seek(0.25f));

            Assert.AreEqual(50f, backend.GetTime(), 0.001f, "割合 -> 秒");
            Assert.AreEqual(0.25f, adapter.GetProgress(), 0.001f);
        }

        [Test]
        public void Adapter_ReportsVideoErrorsAsBackendErrors()
        {
            var backend = NewVRChatBackend(out _, autoCompleteLoading: true);
            var adapter = new VideoBackendAdapter("VideoAdapter", backend, _logger);
            var observer = new RecordingObserver();
            adapter.AddObserver(observer);

            adapter.Load(Video);
            backend.NotifyVideoError(VideoErrorKind.AccessDenied);

            Assert.IsTrue(adapter.HasError);
            StringAssert.Contains("AccessDenied", adapter.LastError);
            CollectionAssert.Contains(observer.Types(), BackendEventType.Error);
        }

        [Test]
        public void Adapter_ReportsTheEndSoTheUpperLayerCanAdvance()
        {
            var backend = NewVRChatBackend(out var player, autoCompleteLoading: true);
            var adapter = new VideoBackendAdapter("VideoAdapter", backend, _logger);
            var observer = new RecordingObserver();
            adapter.AddObserver(observer);

            adapter.Load(Video);
            adapter.Play();
            player.Advance(player.DurationSeconds + 1f);
            backend.Tick(0.016f);

            CollectionAssert.Contains(observer.Types(), BackendEventType.Ended);
        }

        // ───────── BackendManager / PlayerSession は無変更 ─────────

        [Test]
        public void BackendManager_SelectsTheVideoAdapterUnchanged()
        {
            var adapter = new VideoBackendAdapter(
                "VideoAdapter", NewVRChatBackend(out _, autoCompleteLoading: true), _logger);

            var queue = new MediaQueue();
            var manager = new BackendManager(queue, _logger);
            manager.RegisterBackend(adapter);
            queue.Enqueue(Video);

            Assert.AreSame(adapter, manager.SelectBackendFor(Video),
                "Backend の種類を判断するのは BackendManager だけ");
            Assert.IsTrue(manager.LoadCurrent());
            Assert.IsTrue(manager.Play());
            Assert.AreEqual(BackendState.Playing, manager.GetState());
        }

        [Test]
        public void PlayerSession_PlaysACatalogVideoThroughTheRealBackend()
        {
            var backend = NewVRChatBackend(out var player, autoCompleteLoading: true);
            var session = NewSession(backend);
            session.SetTracks(new[] { "video-001" });

            Assert.IsTrue(session.Play(), "Catalog に登録された動画を再生できる");

            Assert.AreEqual("video-001", session.CurrentMediaId);
            Assert.AreEqual(PlaybackState.Playing, session.PlaybackState);
            Assert.AreEqual(VideoPlayerState.Playing, backend.GetState());
            Assert.IsTrue(player.IsPlaying, "実際に VRChat の動画プレイヤーが再生している");
        }

        [Test]
        public void PlayerSession_PlaysThroughAnAsynchronousLoadWithoutAnyChanges()
        {
            // 実機と同じ「読み込みに時間がかかる」状況。
            // MediaPlayer.Play() は読み込み直後に再生しようとするが、
            // Backend 側が予約して吸収するので上位は何も変えなくてよい。
            var backend = NewVRChatBackend(out var player, autoCompleteLoading: false);
            var session = NewSession(backend);
            session.SetTracks(new[] { "video-001" });

            Assert.IsTrue(session.Play());
            Assert.AreEqual(BackendState.Loading, session.BackendState);

            player.CompleteLoading();
            backend.Tick(0.016f);

            Assert.AreEqual(BackendState.Playing, session.BackendState);
            Assert.IsTrue(player.IsPlaying);
        }

        [Test]
        public void PlayerSession_ControlsPlaybackWithoutKnowingItIsAVideo()
        {
            var backend = NewVRChatBackend(out var player, autoCompleteLoading: true);
            var session = NewSession(backend);
            session.SetTracks(new[] { "video-001" });
            session.Play();

            Assert.IsTrue(session.Pause());
            Assert.AreEqual(BackendState.Paused, session.BackendState);
            Assert.IsFalse(player.IsPlaying);

            Assert.IsTrue(session.Resume());
            Assert.AreEqual(BackendState.Playing, session.BackendState);
            Assert.IsTrue(player.IsPlaying);

            Assert.IsTrue(session.Stop());
            Assert.AreEqual(BackendState.Stopped, session.BackendState);
            Assert.IsFalse(player.IsPlaying);
        }

        [Test]
        public void PlayerSession_AdvancesWhenTheVideoEnds()
        {
            var backend = NewVRChatBackend(out var player, autoCompleteLoading: true);
            var session = NewSession(backend);
            session.AutoQueueEnabled = false;
            session.SetTracks(new[] { "video-001" });
            session.Play();

            // 動画が最後まで再生された(実機の OnVideoEnd 相当)
            player.Advance(player.DurationSeconds + 1f);
            backend.Tick(0.016f);

            Assert.AreEqual(PlaybackState.Exhausted, session.PlaybackState,
                "Ended が上位の自動送りにつながる");
        }

        [Test]
        public void PlayerSession_QueueAndRecommendationAreUntouched()
        {
            var session = NewSession(NewVRChatBackend(out _, autoCompleteLoading: true));
            session.SetTracks(new[] { "video-001" });

            Assert.AreEqual(1, session.Queue.Count);
            Assert.Greater(session.EnsureQueueFilled(), 0,
                "Recommendation は動画を種にしても働く(変更していない)");
        }

        // ───────── 組み立て ─────────

        private VRChatVideoBackend NewVRChatBackend(
            out SimulatedVRCVideoPlayer player, bool autoCompleteLoading)
        {
            player = new SimulatedVRCVideoPlayer(_urls)
            {
                AutoCompleteLoading = autoCompleteLoading,
            };
            return new VRChatVideoBackend("VRChatVideoBackend", player, _urls, _logger);
        }

        private PlayerSession NewSession(IVideoBackend video)
        {
            // ← Phase2-4(B) と全く同じ組み立て。渡す IVideoBackend が変わっただけ。
            var adapter = new VideoBackendAdapter("VideoAdapter", video, _logger);

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

        /// <summary>テスト用に通知を記録する観測者。</summary>
        private sealed class RecordingObserver : IBackendObserver
        {
            public List<BackendEvent> Events { get; } = new List<BackendEvent>();

            public void OnBackendEvent(BackendEvent backendEvent) => Events.Add(backendEvent);

            public BackendEventType[] Types() => Events.Select(e => e.Type).ToArray();
        }
    }
}
