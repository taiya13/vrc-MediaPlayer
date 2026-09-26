using System;
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
using SmartMediaPlatform.Video.Data;

namespace SmartMediaPlatform.Library.Tests
{
    /// <summary>
    /// Phase4-3: <b>Library → Queue → Player</b> の一連の流れの検証。
    ///
    /// 確かめたいのは 4 点です。
    /// <list type="number">
    /// <item>Library で選ぶと再生が始まり、Queue に反映される</item>
    /// <item>Queue が <see cref="DisplayMeta"/> で見え、**URL が出てこない**</item>
    /// <item>Queue の操作(次に再生 / 並べ替え / 削除 / 途中へ飛ぶ)が効く</item>
    /// <item>再生中の表示が <c>MediaId → Catalog</c> で引き直されている</item>
    /// </list>
    /// </summary>
    public sealed class PlaybackFlowTests
    {
        private IMediaCatalog _catalog;
        private PlayerSession _session;
        private DummyBackend _backend;
        private PlaybackFlow _flow;

        [SetUp]
        public void SetUp()
        {
            var logger = new ListBackendLogger();
            _catalog = new MediaCatalog(new VideoCatalogSource(), new System.Random(1));

            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, logger);
            _backend = new DummyBackend("DummyBackend", logger);
            backendManager.RegisterBackend(_backend);

            var mediaPlayer = new MediaPlayer(backendManager, logger);
            var engine = new RecommendationEngine(_catalog, RecommendationRule.CreateDefault());
            _session = new PlayerSession(
                "test", mediaPlayer, _catalog, engine, new System.Random(1), logger);

            _flow = PlaybackFlow.Create(_catalog, _session, 5, logger);
        }

        private string[] QueueIds() => _flow.Queue.Entries.Select(x => x.MediaId).ToArray();

        // ───────── 組み立て ─────────

        [Test]
        public void Constructor_RejectsNulls()
        {
            var store = new CatalogStore(_catalog);

            Assert.Throws<ArgumentNullException>(() => new PlaybackFlow(null, _session));
            Assert.Throws<ArgumentNullException>(() => new PlaybackFlow(store, null));
            Assert.Throws<ArgumentNullException>(() => PlaybackFlow.Create(null, _session));
        }

        [Test]
        public void Create_WiresEveryPiece()
        {
            Assert.IsNotNull(_flow.Store);
            Assert.IsNotNull(_flow.Library);
            Assert.IsNotNull(_flow.Related);
            Assert.IsNotNull(_flow.Queue);
            Assert.IsNotNull(_flow.NowPlaying);
            Assert.IsNotNull(_flow.LibraryPlayback);
            Assert.IsNotNull(_flow.RelatedPlayback);
            Assert.IsNotNull(_flow.QueuePlayback);
            Assert.AreSame(_session, _flow.Session);
        }

        [Test]
        public void Create_UsesTheCatalogRelatedIdsButLeavesRoomForAServer()
        {
            Assert.IsInstanceOf<StaticRelatedMediaProvider>(_flow.RelatedProvider,
                "サーバー応答を Set() で差し込める形で組み立てる");
        }

        [Test]
        public void EveryListIsTheSameShape()
        {
            Assert.IsInstanceOf<IMediaListView>(_flow.Library);
            Assert.IsInstanceOf<IMediaListView>(_flow.Related);
            Assert.IsInstanceOf<IMediaListView>(_flow.Queue,
                "Queue も同じ形なので UI と Bridge を使い回せる");
        }

        // ───────── Library → Player ─────────

        [Test]
        public void PlayingFromTheLibraryStartsPlaybackAndFillsTheQueue()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.Library.Select(0);
            string picked = _flow.Library.SelectedMediaId;

            Assert.IsTrue(_flow.LibraryPlayback.PlaySelected());
            _flow.Tick();

            Assert.AreEqual(picked, _session.CurrentMediaId);
            Assert.AreEqual(picked, _flow.NowPlaying.MediaId);
            Assert.AreEqual(picked, _flow.Queue.NowPlaying.MediaId, "Queue の先頭 = いま鳴っているもの");
            Assert.Greater(_flow.Queue.Count, 1, "続きはセッションが補充している");
        }

        [Test]
        public void NowPlayingReadsTheMetaBackFromTheCatalog()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            var meta = _flow.NowPlaying.Meta;
            var fromCatalog = _catalog.FindById(_session.CurrentMediaId);

            Assert.IsNotNull(meta);
            Assert.AreEqual(fromCatalog.Title, meta.Title);
            Assert.AreEqual(fromCatalog.Artist, meta.Artist);
            Assert.AreEqual(_session.CurrentMediaId, meta.MediaId,
                "PlayerSession は MediaId しか持たず、表示は Catalog から引き直す");
        }

        [Test]
        public void NowPlayingIsEmptyBeforeAnythingPlays()
        {
            Assert.IsFalse(_flow.NowPlaying.HasMedia);
            Assert.IsNull(_flow.NowPlaying.Meta);
            Assert.IsFalse(_flow.NowPlaying.Playable.IsValid);
            StringAssert.Contains("再生していません", _flow.NowPlaying.FormatTitle());
        }

        [Test]
        public void NowPlayingFallsBackToTheCatalogDuration()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);

            // DummyBackend は長さを answer しないので、カタログの値で埋まる
            var meta = _flow.NowPlaying.Meta;
            Assert.Greater(meta.DurationSeconds, 0, "この検証はカタログに長さがある前提");
            Assert.AreEqual(meta.DurationSeconds, _flow.NowPlaying.Duration, 0.01f);
        }

        [Test]
        public void NowPlayingPollReportsChangesOnce()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);

            Assert.IsTrue(_flow.NowPlaying.Poll(), "変わったので 1 回目は true");
            Assert.IsFalse(_flow.NowPlaying.Poll(), "変わっていなければ false");
        }

        // ───────── Queue が見える ─────────

        [Test]
        public void TheQueueIsVisibleAsDisplayMeta()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            Assert.Greater(_flow.Queue.Count, 0);
            foreach (var entry in _flow.Queue.Entries)
            {
                Assert.IsInstanceOf<DisplayMeta>(entry);
                Assert.IsTrue(entry.HasTitle);
            }
        }

        [Test]
        public void TheQueueViewNeverExposesTheUrl()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            // IQueue.GetAll() は MediaItem(= Url)を抱えた QueueItem を返すが、
            // QueueView からはそれが出てこない。
            Assert.IsNull(typeof(QueueView).GetMethod("GetQueueItem"));
            Assert.IsNull(typeof(DisplayMeta).GetProperty("Url"));

            foreach (var method in typeof(QueueView).GetMethods())
            {
                Assert.AreNotEqual(typeof(QueueItem), method.ReturnType,
                    $"{method.Name} が QueueItem を返している");
            }
        }

        [Test]
        public void TheQueueViewMirrorsTheQueueOrder()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            CollectionAssert.AreEqual(
                _session.Queue.GetAll().Select(x => x.MediaId).ToArray(), QueueIds());
        }

        [Test]
        public void SyncOnlyRebuildsWhenTheQueueChanged()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);

            _flow.Queue.Sync();
            Assert.IsFalse(_flow.Queue.Sync(), "変わっていなければ何もしない");

            _session.Enqueue("video-009");
            Assert.IsTrue(_flow.Queue.Sync(), "変わったら組み直す");
        }

        // ───────── Queue 操作 ─────────

        [Test]
        public void PlayNextPutsItRightAfterTheCurrentOne()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            _flow.Library.SelectById("video-010");
            Assert.IsTrue(_flow.LibraryPlayback.PlayNextSelected());
            _flow.Tick();

            Assert.AreEqual("video-010", _flow.Queue.GetAt(1).MediaId, "index 1 が「次」");
            Assert.AreNotEqual("video-010", _session.CurrentMediaId, "いまの再生は止まらない");
        }

        [Test]
        public void PlayNextRefusesTheAlreadyPlayingMedia()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);

            Assert.IsFalse(_flow.LibraryPlayback.PlayNext(_session.CurrentMediaId));
        }

        [Test]
        public void MovingReordersTheUpcomingItems()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            Assert.Greater(_flow.Queue.Count, 3, "この検証は Queue に余裕がある前提");
            string second = _flow.Queue.GetAt(1).MediaId;

            Assert.IsTrue(_flow.Queue.MoveDown(1));
            Assert.AreEqual(second, _flow.Queue.GetAt(2).MediaId);

            Assert.IsTrue(_flow.Queue.MoveUp(2));
            Assert.AreEqual(second, _flow.Queue.GetAt(1).MediaId);
        }

        [Test]
        public void TheCurrentItemIsProtectedFromReorderingAndRemoval()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            string playing = _flow.Queue.NowPlaying.MediaId;

            Assert.IsFalse(_flow.Queue.MoveUp(0), "先頭は上げられない");
            Assert.IsFalse(_flow.Queue.MoveUp(1), "先頭を追い越せない");
            Assert.IsFalse(_flow.Queue.MoveDown(0), "先頭は下げられない");
            Assert.IsFalse(_flow.Queue.RemoveAt(0), "先頭は消せない");

            Assert.AreEqual(playing, _flow.Queue.NowPlaying.MediaId);
            Assert.AreEqual(playing, _session.CurrentMediaId, "再生と Queue がずれない");
        }

        [Test]
        public void RemovingTakesItOutOfTheQueue()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            string removed = _flow.Queue.GetAt(1).MediaId;
            int before = _flow.Queue.Count;

            Assert.IsTrue(_flow.Queue.RemoveAt(1));

            Assert.AreEqual(before - 1, _flow.Queue.Count);
            CollectionAssert.DoesNotContain(QueueIds(), removed);
        }

        [Test]
        public void RemoveSelectedUsesTheSelection()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            _flow.Queue.Select(1);
            string removed = _flow.Queue.SelectedMediaId;

            Assert.IsTrue(_flow.Queue.RemoveSelected());
            CollectionAssert.DoesNotContain(QueueIds(), removed);
        }

        [Test]
        public void ClearUpcomingLeavesOnlyWhatIsPlaying()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            string playing = _flow.Queue.NowPlaying.MediaId;
            int removed = _flow.Queue.ClearUpcoming();

            Assert.Greater(removed, 0);
            Assert.AreEqual(1, _flow.Queue.Count);
            Assert.AreEqual(playing, _flow.Queue.NowPlaying.MediaId);
            Assert.AreEqual(playing, _session.CurrentMediaId, "再生は止まらない");
        }

        // ───────── Queue の途中へ飛ぶ ─────────

        [Test]
        public void JumpingToAQueueEntryPlaysIt()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            Assert.Greater(_flow.Queue.Count, 2);
            string target = _flow.Queue.GetAt(2).MediaId;

            Assert.IsTrue(_flow.QueuePlayback.PlayAt(2));
            _flow.Tick();

            Assert.AreEqual(target, _session.CurrentMediaId);
            Assert.AreEqual(target, _flow.Queue.NowPlaying.MediaId);
            Assert.IsTrue(_session.IsPlaying);
        }

        // ───────── Ended で進む ─────────

        [Test]
        public void EndedAdvancesAndTheViewsFollow()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            string first = _session.CurrentMediaId;

            _backend.SimulateEnded();
            _flow.Tick();

            Assert.AreNotEqual(first, _session.CurrentMediaId, "PlayerSession が次へ進める");
            Assert.AreEqual(_session.CurrentMediaId, _flow.NowPlaying.MediaId, "表示も追従する");
            Assert.AreEqual(_session.CurrentMediaId, _flow.Queue.NowPlaying.MediaId);
        }

        [Test]
        public void SeveralEndedInARowKeepPlaying()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);

            for (int i = 0; i < 5; i++)
            {
                _flow.Tick();
                Assert.IsTrue(_backend.SimulateEnded(), $"{i + 1} 本目で Ended を出せなかった");
                _flow.Tick();

                Assert.IsNotNull(_flow.NowPlaying.MediaId, $"{i + 1} 本目のあとに止まった");
            }
        }

        // ───────── 関連の追従 ─────────

        [Test]
        public void RelatedFollowsWhatIsPlaying()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            Assert.AreEqual(_session.CurrentMediaId, _flow.Related.SourceMediaId);
        }

        [Test]
        public void RelatedMovesOnWhenPlaybackAdvances()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();
            string firstSource = _flow.Related.SourceMediaId;

            _backend.SimulateEnded();
            _flow.Tick();

            Assert.AreNotEqual(firstSource, _flow.Related.SourceMediaId);
            Assert.AreEqual(_session.CurrentMediaId, _flow.Related.SourceMediaId);
        }

        [Test]
        public void RelatedFollowingCanBeTurnedOff()
        {
            _flow.RelatedFollowsPlayback = false;
            _flow.RelatedFollowsSelection = false;
            _flow.Related.SetSource("video-005");

            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            Assert.AreEqual("video-005", _flow.Related.SourceMediaId, "起点は自分で決めたまま");
        }

        [Test]
        public void PlayingFromTheRelatedListMovesTheSourceAlong()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            Assert.Greater(_flow.Related.Count, 0, "この検証は関連がある前提");
            string target = _flow.Related.GetAt(0).MediaId;

            Assert.IsTrue(_flow.RelatedPlayback.PlayAt(0));
            _flow.Tick();

            Assert.AreEqual(target, _session.CurrentMediaId);
            Assert.AreEqual(target, _flow.Related.SourceMediaId, "関連 → 関連 とたどれる");
        }

        [Test]
        public void ServerSuppliedRelatedIdsFlowThroughTheWholeChain()
        {
            var provider = (StaticRelatedMediaProvider)_flow.RelatedProvider;

            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            // サーバー応答を差し込む
            provider.Set(_session.CurrentMediaId, new[] { "video-009", "video-006" });
            _flow.Related.Refresh();

            CollectionAssert.AreEqual(
                new[] { "video-009", "video-006" },
                _flow.Related.Entries.Select(x => x.MediaId).ToArray());

            // そのまま再生まで通る
            Assert.IsTrue(_flow.RelatedPlayback.PlayAt(0));
            Assert.AreEqual("video-009", _session.CurrentMediaId);
        }

        // ───────── Tick ─────────

        [Test]
        public void TickIsCheapWhenNothingChanged()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            int before = _flow.SyncCount;

            for (int i = 0; i < 10; i++) Assert.IsFalse(_flow.Tick());

            Assert.AreEqual(before, _flow.SyncCount, "変化が無ければ何も起きない");
        }

        // ───────── 責務の境界 ─────────

        [Test]
        public void TheFlowDoesNotDecideWhatPlaysNext()
        {
            // 「次に何を再生するか」を決めるメンバーは持たない
            var type = typeof(PlaybackFlow);

            Assert.IsNull(type.GetMethod("Next"));
            Assert.IsNull(type.GetMethod("Play"));
            Assert.IsNull(type.GetMethod("Stop"));
            Assert.IsNull(type.GetMethod("SkipNext"));
        }

        [Test]
        public void ThePlaybackEngineIsUntouched()
        {
            _flow.Library.ShowOnly(MediaType.Video);
            _flow.LibraryPlayback.PlayAt(0);
            _flow.Tick();

            Assert.IsTrue(_session.AutoQueueEnabled, "セッションの設定を書き換えていない");
            Assert.IsTrue(_session.Next(), "Next は今までどおり PlayerSession の仕事");
        }

        [Test]
        public void TheLibraryAssemblyStillDoesNotSeeThePlaybackStack()
        {
            var libraryRefs = typeof(MediaLibrary).Assembly
                .GetReferencedAssemblies().Select(x => x.Name).ToArray();

            CollectionAssert.DoesNotContain(libraryRefs, "SmartMediaPlatform.Session");
            CollectionAssert.DoesNotContain(libraryRefs, "SmartMediaPlatform.Queue");

            // Queue / Session が見えるのは Library.Playback だけ
            var playbackRefs = typeof(PlaybackFlow).Assembly
                .GetReferencedAssemblies().Select(x => x.Name).ToArray();

            CollectionAssert.Contains(playbackRefs, "SmartMediaPlatform.Session");
            CollectionAssert.Contains(playbackRefs, "SmartMediaPlatform.Queue");
        }
    }
}
