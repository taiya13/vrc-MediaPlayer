using System;
using System.Collections.Generic;
using NUnit.Framework;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;
using SmartMediaPlatform.Video;
using SmartMediaPlatform.Video.Data;

namespace SmartMediaPlatform.AutoPlay.Tests
{
    /// <summary>
    /// Phase3-3: おすすめ再生ループ(Recommendation → Queue → PlayerSession →
    /// BackendAdapter → VRChatVideoBackend)の検証。
    ///
    /// VRChat SDK は使いません。実機の動画プレイヤーの位置に
    /// <see cref="SimulatedVRCVideoPlayer"/> を置き、進行は
    /// <see cref="VideoEventBridge"/> 経由の <c>OnVideoEnd</c> だけで行います。
    /// </summary>
    public sealed class RecommendationPlaybackServiceTests
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
            Assert.Throws<ArgumentNullException>(
                () => new RecommendationPlaybackService(null, _rig.Catalog, _rig.Engine));
            Assert.Throws<ArgumentNullException>(
                () => new RecommendationPlaybackService(_rig.Session, null, _rig.Engine));
            Assert.Throws<ArgumentNullException>(
                () => new RecommendationPlaybackService(_rig.Session, _rig.Catalog, null));
        }

        [Test]
        public void Service_ObservesBackendEvents()
        {
            Assert.IsInstanceOf<IBackendObserver>(_rig.Service,
                "Ended を受け取って先に補充するため");
        }

        [Test]
        public void Service_TakesOverTheSessionAutoQueue()
        {
            Assert.IsFalse(_rig.Session.AutoQueueEnabled,
                "補充の入口を 1 つに保つ(PlayerSession 側の無フィルタ補充を止める)");
            Assert.IsTrue(_rig.Service.TakesOverAutoQueue);
        }

        [Test]
        public void Service_CanLeaveTheSessionAutoQueueAlone()
        {
            var rig = new Rig(new VideoCatalogSource(), takeOverAutoQueue: false);

            Assert.IsTrue(rig.Session.AutoQueueEnabled);
            Assert.IsFalse(rig.Service.TakesOverAutoQueue);
        }

        [Test]
        public void CatalogSource_ProvidesEnoughVideosForALoop()
        {
            var videos = _rig.Catalog.FilterByType(MediaType.Video);

            Assert.GreaterOrEqual(videos.Count, 5, "おすすめループを回すには複数の動画が要る");
            foreach (var video in videos)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(video.Url), $"{video.Id} に URL が無い");
            }
        }

        // ───────── Start:Catalog から再生できる ─────────

        [Test]
        public void Start_PlaysASeedVideoFromTheCatalog()
        {
            int added = _rig.Service.Start("video-001");

            Assert.Greater(added, 0);
            Assert.AreEqual("video-001", _rig.Session.CurrentMediaId);
            Assert.AreEqual(PlaybackState.Playing, _rig.Session.PlaybackState);
            Assert.AreEqual(VideoPlayerState.Playing, _rig.Backend.GetState(),
                "実際に VideoBackend が再生している");
        }

        [Test]
        public void Start_UsesTheUrlBakedInTheCatalog()
        {
            _rig.Service.Start("video-001");

            var item = _rig.Catalog.FindById("video-001");
            Assert.AreEqual(item.Url, _rig.Backend.GetCurrentUrl(),
                "実行時に URL を作らず、Catalog に登録済みのものを使う");
        }

        [Test]
        public void Start_FillsTheQueueAhead()
        {
            _rig.Service.Start("video-001");

            Assert.GreaterOrEqual(_rig.Session.Queue.Count, 2,
                "Ended のときに次が無いと MediaPlayer.SkipNext が失敗する");
        }

        [Test]
        public void Start_PicksASeedWhenNoneIsGiven()
        {
            int added = _rig.Service.Start();

            Assert.Greater(added, 0);
            Assert.IsNotNull(_rig.Service.SeedId);
            Assert.AreEqual(MediaType.Video, _rig.Catalog.FindById(_rig.Service.SeedId).Type);
        }

        [Test]
        public void Start_FallsBackWhenTheSeedCannotBePlayed()
        {
            var rig = new Rig(new MixedCatalogSource());

            rig.Service.Start("music-001");   // 音楽は Video バックエンドでは再生できない

            Assert.AreNotEqual("music-001", rig.Service.SeedId);
            Assert.AreEqual(MediaType.Video, rig.Catalog.FindById(rig.Service.SeedId).Type);
            Assert.AreEqual(PlaybackState.Playing, rig.Session.PlaybackState);
        }

        [Test]
        public void Start_ReturnsZeroWhenNothingIsPlayable()
        {
            // 音楽しか無いカタログを、動画しか再生できない構成に渡す
            var rig = new Rig(new DummyCatalogSource(), musicOnlyCatalog: true);

            Assert.AreEqual(0, rig.Service.Start());
            Assert.IsNull(rig.Service.SeedId);
        }

        // ───────── Ended だけで次へ進む ─────────

        [Test]
        public void EndedEvent_AdvancesToTheNextRecommendedVideo()
        {
            _rig.Service.Start("video-001");
            string first = _rig.Session.CurrentMediaId;

            _rig.FinishCurrentVideo();   // OnVideoEnd だけ

            Assert.AreNotEqual(first, _rig.Session.CurrentMediaId, "次の動画へ進む");
            Assert.AreEqual(PlaybackState.Playing, _rig.Session.PlaybackState);
            Assert.AreEqual(VideoPlayerState.Playing, _rig.Backend.GetState());
        }

        [Test]
        public void EndedEvent_IsTheOnlyTriggerNeededForALongLoop()
        {
            _rig.Service.Start("video-001");

            var played = new List<string> { _rig.Session.CurrentMediaId };
            for (int i = 0; i < 12; i++)
            {
                _rig.FinishCurrentVideo();

                Assert.AreEqual(PlaybackState.Playing, _rig.Session.PlaybackState,
                    $"{i + 1} 本目で再生が止まった");
                played.Add(_rig.Session.CurrentMediaId);
            }

            Assert.AreEqual(13, played.Count);
            Assert.AreEqual(12, _rig.Service.EndedCount);
            CollectionAssert.AllItemsAreNotNull(played);
        }

        [Test]
        public void Loop_OnlyPlaysVideosThatExistInTheCatalog()
        {
            _rig.Service.Start("video-001");

            for (int i = 0; i < 10; i++)
            {
                var item = _rig.Catalog.FindById(_rig.Session.CurrentMediaId);
                Assert.IsNotNull(item, "カタログに無い ID が再生された");
                Assert.AreEqual(MediaType.Video, item.Type);
                _rig.FinishCurrentVideo();
            }
        }

        [Test]
        public void Loop_DoesNotRepeatTheSameVideoBackToBack()
        {
            _rig.Service.Start("video-001");

            string previous = _rig.Session.CurrentMediaId;
            for (int i = 0; i < 10; i++)
            {
                _rig.FinishCurrentVideo();
                Assert.AreNotEqual(previous, _rig.Session.CurrentMediaId,
                    "同じ動画が続けて再生された");
                previous = _rig.Session.CurrentMediaId;
            }
        }

        [Test]
        public void EndedEvent_RefillsBeforeTheSessionAdvances()
        {
            _rig.Service.Start("video-001");
            int before = _rig.Service.RefillCount;

            // Queue を最小まで削ってから終了させる
            while (_rig.Session.Queue.Count > 1) _rig.Session.Queue.RemoveAt(_rig.Session.Queue.Count - 1);

            _rig.FinishCurrentVideo();

            Assert.Greater(_rig.Service.RefillCount, before, "Ended を受けて補充した");
            Assert.AreEqual(PlaybackState.Playing, _rig.Session.PlaybackState,
                "補充が間に合ったので再生が続いている");
        }

        // ───────── Queue が空でも補充される ─────────

        [Test]
        public void EnsureQueueFilled_RefillsAnEmptyQueue()
        {
            _rig.Service.Start("video-001");
            _rig.Session.ClearQueue();
            Assert.AreEqual(0, _rig.Session.Queue.Count);

            int added = _rig.Service.EnsureQueueFilled();

            Assert.Greater(added, 0);
            Assert.GreaterOrEqual(_rig.Session.Queue.Count, 2);
        }

        [Test]
        public void EnsureQueueFilled_DoesNothingWhenTheQueueIsLongEnough()
        {
            _rig.Service.Start("video-001");

            Assert.AreEqual(0, _rig.Service.EnsureQueueFilled(),
                "足りているときは積まない");
        }

        [Test]
        public void EnsureQueueFilled_KeepsWorkingAfterTheCatalogIsExhausted()
        {
            _rig.Service.Start("video-001");

            // カタログの本数より多く回す
            for (int i = 0; i < 25; i++) _rig.FinishCurrentVideo();

            Assert.AreEqual(PlaybackState.Playing, _rig.Session.PlaybackState,
                "一巡しても止まらない(AllowRepeatWhenExhausted)");
        }

        [Test]
        public void AllowRepeatWhenExhausted_CanBeTurnedOff()
        {
            _rig.Service.AllowRepeatWhenExhausted = false;
            _rig.Service.AllowCatalogFallback = false;
            _rig.Service.RecentMemory = 1000;      // 積んだものをすべて覚えておく
            _rig.Service.TargetQueueCount = 100;   // 積めるものを一度に全部積む
            _rig.Service.Start("video-001");

            _rig.Session.ClearQueue();

            Assert.AreEqual(0, _rig.Service.EnsureQueueFilled(),
                "繰り返しを許さなければ、候補が尽きた時点で積めなくなる");
        }

        // ───────── 再生できない種別は積まれない ─────────

        [Test]
        public void MixedCatalog_OnlyQueuesPlayableItems()
        {
            var rig = new Rig(new MixedCatalogSource());
            rig.Service.Start("video-001");

            for (int i = 0; i < 6; i++) rig.FinishCurrentVideo();

            foreach (var entry in rig.Session.Queue.GetAll())
            {
                Assert.IsTrue(
                    entry.Item.Type == MediaType.Video || entry.Item.Type == MediaType.Live,
                    $"{entry.MediaId} ({entry.Item.Type}) が Queue に積まれている");
            }
        }

        [Test]
        public void MixedCatalog_CountsWhatItFilteredOut()
        {
            var rig = new Rig(new MixedCatalogSource());

            // 候補を最後まで走査させて、音楽を必ず 1 件は踏むようにする
            rig.Service.TargetQueueCount = 100;
            rig.Service.Start("video-001");

            Assert.Greater(rig.Service.FilteredOutCount, 0,
                "おすすめには音楽も含まれるので、弾いた記録が残るはず");
        }

        [Test]
        public void MixedCatalog_LoopKeepsPlayingVideosOnly()
        {
            var rig = new Rig(new MixedCatalogSource());
            rig.Service.Start("video-001");

            for (int i = 0; i < 10; i++)
            {
                var item = rig.Catalog.FindById(rig.Session.CurrentMediaId);
                Assert.AreEqual(MediaType.Video, item.Type,
                    $"{i} 本目に動画以外が再生された: {item.Id} ({item.Type})");
                rig.FinishCurrentVideo();
            }
        }

        // ───────── MediaId だけを受け渡す ─────────

        [Test]
        public void PickNextMediaId_ReturnsAPlayableCatalogId()
        {
            _rig.Service.Start("video-001");

            string next = _rig.Service.PickNextMediaId("video-001");

            Assert.IsNotNull(next);
            var item = _rig.Catalog.FindById(next);
            Assert.IsNotNull(item, "カタログに存在する ID を返す");
            Assert.AreEqual(MediaType.Video, item.Type);
        }

        [Test]
        public void PickNextMediaId_NeverReturnsTheCurrentVideo()
        {
            _rig.Service.Start("video-001");

            for (int i = 0; i < 8; i++)
            {
                string next = _rig.Service.PickNextMediaId();
                Assert.AreNotEqual(_rig.Session.CurrentMediaId, next);
                _rig.FinishCurrentVideo();
            }
        }

        [Test]
        public void PickNextMediaId_DoesNotTouchTheQueue()
        {
            _rig.Service.Start("video-001");
            int before = _rig.Session.Queue.Count;

            _rig.Service.PickNextMediaId();

            Assert.AreEqual(before, _rig.Session.Queue.Count, "選ぶだけで積まない");
        }

        [Test]
        public void PickNextMediaId_ReturnsNullWhenNothingIsAvailable()
        {
            var rig = new Rig(new DummyCatalogSource(), musicOnlyCatalog: true);

            Assert.IsNull(rig.Service.PickNextMediaId("music-001"));
        }

        // ───────── 上位は無変更 ─────────

        [Test]
        public void PlayerSession_IsDrivenThroughItsPublicApiOnly()
        {
            _rig.Service.Start("video-001");

            // セッションの状態はセッション自身が持っている
            Assert.Greater(_rig.Session.Tracks.Count, 0);
            Assert.AreEqual(BackendState.Playing, _rig.Session.BackendState);
            Assert.IsTrue(_rig.Session.IsPlaying);
        }

        [Test]
        public void PlayerSession_StillRecordsHistory()
        {
            _rig.Service.Start("video-001");
            _rig.FinishCurrentVideo();
            _rig.FinishCurrentVideo();

            Assert.GreaterOrEqual(_rig.Session.History.Count, 2,
                "履歴の管理は PlayerSession の責務のまま");
        }

        [Test]
        public void Adapter_TranslatesEverythingAsBefore()
        {
            _rig.Service.Start("video-001");

            Assert.IsInstanceOf<IMediaBackend>(_rig.Adapter);
            Assert.AreEqual(BackendState.Playing, _rig.Adapter.GetState());
            Assert.AreEqual("video-001", _rig.Adapter.GetCurrent().Id);
        }

        [Test]
        public void BackendManager_IsStillTheOnlyPlaceThatPicksABackend()
        {
            _rig.Service.Start("video-001");

            var video = _rig.Catalog.FindById("video-001");
            Assert.AreSame(_rig.Adapter, _rig.BackendManager.SelectBackendFor(video));
        }

        // ───────── フィルタ ─────────

        [Test]
        public void MediaTypePlaybackFilter_DefaultsToVideoAndLive()
        {
            var filter = new MediaTypePlaybackFilter();

            Assert.IsTrue(filter.CanPlay(_rig.Catalog.FindById("video-001")));
            CollectionAssert.AreEquivalent(
                new[] { MediaType.Video, MediaType.Live }, filter.Types);
        }

        [Test]
        public void MediaTypePlaybackFilter_RejectsOtherTypes()
        {
            var catalog = new MediaCatalog(new MixedCatalogSource());
            var filter = new MediaTypePlaybackFilter();

            Assert.IsFalse(filter.CanPlay(catalog.FindById("music-001")));
            Assert.IsFalse(filter.CanPlay(catalog.FindById("podcast-001")));
            Assert.IsFalse(filter.CanPlay(null));
        }

        [Test]
        public void MediaTypePlaybackFilter_AcceptsAnExplicitTypeList()
        {
            var catalog = new MediaCatalog(new MixedCatalogSource());
            var filter = new MediaTypePlaybackFilter(MediaType.Music);

            Assert.IsTrue(filter.CanPlay(catalog.FindById("music-001")));
            Assert.IsFalse(filter.CanPlay(catalog.FindById("video-001")));
        }

        [Test]
        public void BackendPlaybackFilter_AsksTheRegisteredBackends()
        {
            var rig = new Rig(new MixedCatalogSource());
            var filter = new BackendPlaybackFilter(rig.BackendManager);

            Assert.IsTrue(filter.CanPlay(rig.Catalog.FindById("video-001")),
                "Video バックエンドが登録されている");
            Assert.IsFalse(filter.CanPlay(rig.Catalog.FindById("music-001")),
                "音楽を扱えるバックエンドは登録していない");
            Assert.IsFalse(filter.CanPlay(null));
        }

        [Test]
        public void BackendPlaybackFilter_RejectsANullManager()
        {
            Assert.Throws<ArgumentNullException>(() => new BackendPlaybackFilter(null));
        }

        [Test]
        public void AnyPlaybackFilter_AcceptsEverythingButNull()
        {
            var catalog = new MediaCatalog(new MixedCatalogSource());

            Assert.IsTrue(AnyPlaybackFilter.Instance.CanPlay(catalog.FindById("music-001")));
            Assert.IsTrue(AnyPlaybackFilter.Instance.CanPlay(catalog.FindById("video-001")));
            Assert.IsFalse(AnyPlaybackFilter.Instance.CanPlay(null));
        }

        // ───────── 診断 ─────────

        [Test]
        public void Describe_ReportsTheLoopState()
        {
            _rig.Service.Start("video-001");
            _rig.FinishCurrentVideo();

            string text = _rig.Service.Describe();

            StringAssert.Contains("seed=video-001", text);
            StringAssert.Contains("ended=1", text);
        }

        [Test]
        public void Recent_RemembersWhatItQueued()
        {
            _rig.Service.RecentMemory = 3;
            _rig.Service.Start("video-001");

            Assert.LessOrEqual(_rig.Service.Recent.Count, 3);
            Assert.Greater(_rig.Service.Recent.Count, 0);
        }

        // ───────── 組み立て ─────────

        /// <summary>
        /// Catalog → Recommendation → Queue → PlayerSession → Adapter → VideoBackend の一式。
        /// 組み立て方は Phase2-4(B) から変えていない(渡すものが増えただけ)。
        /// </summary>
        private sealed class Rig
        {
            public readonly IMediaCatalog Catalog;
            public readonly RecommendationEngine Engine;
            public readonly PlayerSession Session;
            public readonly RecommendationPlaybackService Service;
            public readonly VRChatVideoBackend Backend;
            public readonly VideoEventBridge Bridge;
            public readonly VideoBackendAdapter Adapter;
            public readonly BackendManager BackendManager;

            private readonly SimulatedVRCVideoPlayer _player;

            public Rig(
                IMediaCatalogSource source,
                bool takeOverAutoQueue = true,
                bool musicOnlyCatalog = false)
            {
                var logger = new ListBackendLogger();
                Catalog = new MediaCatalog(source, new System.Random(1));

                // 動画 URL だけをベイクする(音楽しか無いカタログでは空になる)
                var urls = musicOnlyCatalog
                    ? new CatalogVideoUrlTable(new string[0])
                    : new CatalogVideoUrlTable(Catalog);

                _player = new SimulatedVRCVideoPlayer(urls) { AutoCompleteLoading = true };
                Backend = new VRChatVideoBackend("VRChatVideoBackend", _player, urls, logger);
                Bridge = new VideoEventBridge(Backend, logger);
                Adapter = new VideoBackendAdapter("VideoAdapter", Backend, logger);

                var queue = new MediaQueue();
                BackendManager = new BackendManager(queue, logger);
                var mediaPlayer = new MediaPlayer(BackendManager, logger);
                Engine = new RecommendationEngine(
                    Catalog, RecommendationRule.CreateDefault(), new System.Random(1));
                Session = new PlayerSession(
                    "test", mediaPlayer, Catalog, Engine, new System.Random(1), logger);

                Service = new RecommendationPlaybackService(
                    Session, Catalog, Engine,
                    new BackendPlaybackFilter(BackendManager), logger, takeOverAutoQueue);
                Service.RegisterBackend(Adapter);
            }

            /// <summary>いま再生中の動画を最後まで再生させ、実機の OnVideoEnd を流す。</summary>
            public void FinishCurrentVideo()
            {
                if (Backend.GetState() == VideoPlayerState.Loading)
                {
                    _player.CompleteLoading();
                    Bridge.OnVideoReady();
                }

                _player.FinishPlayback();
                Bridge.OnVideoEnd();
            }
        }
    }
}
