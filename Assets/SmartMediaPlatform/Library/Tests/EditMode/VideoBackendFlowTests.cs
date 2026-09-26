using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Store;
using SmartMediaPlatform.Library.Playback;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;
using SmartMediaPlatform.Video;
using SmartMediaPlatform.Video.Data;

namespace SmartMediaPlatform.Library.Tests
{
    /// <summary>
    /// Phase4-4: <b>Library → Queue → Player → VideoBackend</b> が
    /// 実際につながっていることの検証。
    ///
    /// <b>VRChat SDK は使いません。</b>
    /// 実機の動画プレイヤーの位置に <see cref="SimulatedVRCVideoPlayer"/> を置きます。
    /// SDK 版との違いは <c>IVRCVideoPlayer</c> の実装だけなので、
    /// <b>ここで通れば実機でも同じ経路を通ります</b>
    /// (実機で追加になるのは「URL の焼き込み」と「Udon 経由のイベント」だけです)。
    /// </summary>
    public sealed class VideoBackendFlowTests
    {
        private IMediaCatalog _catalog;
        private CatalogVideoUrlTable _urls;
        private SimulatedVRCVideoPlayer _player;
        private VRChatVideoBackend _backend;
        private VideoEventBridge _bridge;
        private VideoBackendAdapter _adapter;
        private PlayerSession _session;
        private PlaybackFlow _flow;

        [SetUp]
        public void SetUp()
        {
            var logger = new ListBackendLogger();
            _catalog = new MediaCatalog(new VideoCatalogSource(), new System.Random(1));

            // 実機では VRCUrlTable(編集時に焼き込む)。ここでは同じ形の代役。
            _urls = new CatalogVideoUrlTable(_catalog);
            _player = new SimulatedVRCVideoPlayer(_urls) { AutoCompleteLoading = true };
            _backend = new VRChatVideoBackend("VRChatVideoBackend", _player, _urls, logger);
            _bridge = new VideoEventBridge(_backend, logger);
            _adapter = new VideoBackendAdapter("VideoAdapter", _backend, logger);

            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, logger);
            backendManager.RegisterBackend(_adapter);      // ← DummyBackend との唯一の違い

            var mediaPlayer = new MediaPlayer(backendManager, logger);
            var engine = new RecommendationEngine(_catalog, RecommendationRule.CreateDefault());
            _session = new PlayerSession(
                "test", mediaPlayer, _catalog, engine, new System.Random(1), logger);

            _flow = PlaybackFlow.Create(_catalog, _session, 5, logger);
            _flow.Library.ShowOnly(MediaType.Video);
        }

        /// <summary>いま鳴っているものを終わらせる(実機の OnVideoEnd 相当)。</summary>
        private void FinishCurrent()
        {
            if (_backend.GetState() == VideoPlayerState.Loading && _player.AutoCompleteLoading)
            {
                _player.CompleteLoading();
                _bridge.OnVideoReady();
            }

            _player.FinishPlayback();
            _bridge.OnVideoEnd();
        }

        // ───────── 差し替えが成立している ─────────

        [Test]
        public void TheVideoBackendIsWhatTheSessionTalksTo()
        {
            Assert.IsInstanceOf<IMediaBackend>(_adapter,
                "DummyBackend と同じ口で登録できる");
        }

        [Test]
        public void ThePlaybackFlowDoesNotKnowWhichBackendIsAttached()
        {
            var type = typeof(PlaybackFlow);

            Assert.IsNull(type.GetProperty("Backend"));
            Assert.IsNull(type.GetProperty("VideoBackend"));

            foreach (var property in type.GetProperties())
            {
                Assert.AreNotEqual(typeof(VRChatVideoBackend), property.PropertyType,
                    $"{property.Name} が VideoBackend を露出している");
            }
        }

        // ───────── Library → … → 動画プレイヤー ─────────

        [Test]
        public void PlayingFromTheLibraryReachesTheVideoPlayer()
        {
            _flow.Library.Select(0);
            string picked = _flow.Library.SelectedMediaId;

            Assert.IsTrue(_flow.LibraryPlayback.PlaySelected());
            _flow.Tick();

            var expected = _catalog.FindById(picked);

            Assert.AreEqual(expected.Url, _backend.GetCurrentUrl(),
                "VideoBackend が URL を解決している");
            Assert.AreEqual(expected.Url, _player.CurrentUrl,
                "動画プレイヤーまで届いている");
            Assert.AreEqual(picked, _flow.NowPlaying.MediaId);
        }

        [Test]
        public void TheUrlNeverAppearsAboveTheVideoBackend()
        {
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            string url = _backend.GetCurrentUrl();
            Assert.IsNotEmpty(url, "この検証は URL が解決されている前提");

            // UI へ出る型からは URL が取れない
            Assert.IsNull(typeof(DisplayMeta).GetProperty("Url"));
            Assert.IsNull(typeof(PlayableRef).GetProperty("Url"));

            // 表示文字列にも出ない
            StringAssert.DoesNotContain(url, MediaLibraryFormatter.FormatLibrary(_flow.Library));
            StringAssert.DoesNotContain(url, MediaLibraryFormatter.FormatLibrary(_flow.Queue));
            StringAssert.DoesNotContain(url, _flow.NowPlaying.Describe());
        }

        [Test]
        public void PlaybackWaitsForTheLoadToComplete()
        {
            _player.AutoCompleteLoading = false;   // 実機と同じ非同期

            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            Assert.AreEqual(VideoPlayerState.Loading, _backend.GetState());
            Assert.IsFalse(_player.IsPlaying, "読み込み中はまだ鳴らない");

            _player.CompleteLoading();
            _bridge.OnVideoReady();
            _flow.Tick();

            Assert.AreEqual(VideoPlayerState.Playing, _backend.GetState());
            Assert.IsTrue(_player.IsPlaying, "読み込み完了で自動的に再生が始まる");
            Assert.AreEqual(PlaybackState.Playing, _flow.NowPlaying.State);
        }

        // ───────── Ended で進む ─────────

        [Test]
        public void VideoEndAdvancesToTheNextAndLoadsItsUrl()
        {
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            string first = _flow.NowPlaying.MediaId;
            string firstUrl = _player.CurrentUrl;

            FinishCurrent();
            _flow.Tick();

            Assert.AreNotEqual(first, _flow.NowPlaying.MediaId);
            Assert.AreNotEqual(firstUrl, _player.CurrentUrl, "次の URL が読み込まれた");

            var expected = _catalog.FindById(_flow.NowPlaying.MediaId);
            Assert.AreEqual(expected.Url, _player.CurrentUrl);
        }

        [Test]
        public void SeveralVideosPlayInARow()
        {
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            for (int i = 0; i < 5; i++)
            {
                FinishCurrent();
                _flow.Tick();

                Assert.IsNotNull(_flow.NowPlaying.MediaId, $"{i + 1} 本目で止まった");
                Assert.AreEqual(_flow.NowPlaying.MediaId, _flow.Queue.NowPlaying.MediaId);
            }
        }

        [Test]
        public void TheQueueViewFollowsTheRealBackend()
        {
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            FinishCurrent();
            _flow.Tick();

            Assert.AreEqual(_session.CurrentMediaId, _flow.Queue.NowPlaying.MediaId);
            CollectionAssert.AreEqual(
                _session.Queue.GetAll().Select(x => x.MediaId).ToArray(),
                _flow.Queue.Entries.Select(x => x.MediaId).ToArray());
        }

        // ───────── 失敗 ─────────

        [Test]
        public void AVideoErrorIsVisibleWithoutBreakingTheViews()
        {
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            int queueBefore = _flow.Queue.Count;

            _bridge.OnVideoError(VideoErrorKind.InvalidUrl);
            _flow.Tick();

            Assert.AreEqual(VideoPlayerState.Failed, _backend.GetState());
            Assert.AreEqual(VideoErrorKind.InvalidUrl, _backend.LastErrorKind);
            Assert.AreEqual(queueBefore, _flow.Queue.Count, "Queue は壊れない");
            Assert.IsNotNull(_flow.NowPlaying.Meta, "表示も残っている");
        }

        [Test]
        public void PlaybackCanContinueAfterAnError()
        {
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            _bridge.OnVideoError(VideoErrorKind.PlayerError);
            _flow.Tick();

            Assert.IsTrue(_session.Next(), "次へは進める");
            _flow.Tick();

            Assert.AreEqual(VideoPlayerState.Playing, _backend.GetState());
        }

        // ───────── 焼き込み済み URL しか使わない ─────────

        [Test]
        public void OnlyBakedUrlsCanPlay()
        {
            Assert.IsTrue(_backend.CanPlay(_urls.Urls[0]));
            Assert.IsFalse(_backend.CanPlay("https://example.com/not-baked"),
                "実行時に VRCUrl は作れないので、焼き込み表にない URL は断る");
        }

        [Test]
        public void APartiallyBakedCatalogStillPlaysWhatItCan()
        {
            // 一部だけ焼き込んだ状態(実機で URL が切れているのと同じ)
            var videos = _catalog.FilterByType(MediaType.Video);
            var partial = new CatalogVideoUrlTable(new[] { videos[0].Url });

            var player = new SimulatedVRCVideoPlayer(partial) { AutoCompleteLoading = true };
            var backend = new VRChatVideoBackend("partial", player, partial);

            Assert.IsTrue(backend.CanPlay(videos[0].Url));
            Assert.IsFalse(backend.CanPlay(videos[1].Url));
        }

        // ───────── AVPro / Unity ─────────

        [Test]
        public void OnlyTheVideoBackendCaresWhichPlayerIsAttached()
        {
            var urls = new CatalogVideoUrlTable(new[] { "rtsp://example.com/live" });

            var unity = new VRChatVideoBackend(
                "unity", new SimulatedVRCVideoPlayer(urls, VRCVideoPlayerKind.Unity), urls);
            var avpro = new VRChatVideoBackend(
                "avpro", new SimulatedVRCVideoPlayer(urls, VRCVideoPlayerKind.AVPro), urls);

            Assert.IsFalse(unity.CanPlay("rtsp://example.com/live"),
                "Unity 版は生配信を扱えない");
            Assert.IsTrue(avpro.CanPlay("rtsp://example.com/live"),
                "AVPro なら扱える");
        }

        [Test]
        public void SwappingThePlayerKindDoesNotChangeTheFlow()
        {
            // 同じカタログ・同じ手順を AVPro でも通す
            var logger = new ListBackendLogger();
            var urls = new CatalogVideoUrlTable(_catalog);
            var player = new SimulatedVRCVideoPlayer(urls, VRCVideoPlayerKind.AVPro)
            {
                AutoCompleteLoading = true,
            };
            var backend = new VRChatVideoBackend("avpro", player, urls, logger);
            var adapter = new VideoBackendAdapter("avproAdapter", backend, logger);

            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, logger);
            backendManager.RegisterBackend(adapter);

            var mediaPlayer = new MediaPlayer(backendManager, logger);
            var engine = new RecommendationEngine(_catalog, RecommendationRule.CreateDefault());
            var session = new PlayerSession(
                "avpro", mediaPlayer, _catalog, engine, new System.Random(1), logger);

            var flow = PlaybackFlow.Create(_catalog, session, 5, logger);
            flow.Library.ShowOnly(MediaType.Video);

            Assert.IsTrue(flow.LibraryPlayback.PlayAt(0), "AVPro でも同じ手順で再生できる");
            flow.Tick();

            Assert.AreEqual(flow.Library.GetAt(0).MediaId, flow.NowPlaying.MediaId);
            Assert.AreEqual(VRCVideoPlayerKind.AVPro, player.Kind);
        }

        // ───────── 上位が変わっていない ─────────

        [Test]
        public void TheSessionBehavesTheSameAsWithTheDummyBackend()
        {
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            Assert.IsTrue(_session.AutoQueueEnabled, "セッションの設定は書き換えない");
            Assert.Greater(_session.Queue.Count, 1, "Queue はいつもどおり補充される");
            Assert.IsTrue(_session.Next(), "Next も今までどおり");
        }

        [Test]
        public void RelatedStillFollowsPlaybackWithTheRealBackend()
        {
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            Assert.AreEqual(_session.CurrentMediaId, _flow.Related.SourceMediaId);

            FinishCurrent();
            _flow.Tick();

            Assert.AreEqual(_session.CurrentMediaId, _flow.Related.SourceMediaId);
        }
    }
}
