using System;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Library.Playback;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;
using SmartMediaPlatform.Video.Data;

namespace SmartMediaPlatform.Library.Tests
{
    /// <summary>
    /// Phase4-1: 選んだものが <see cref="PlayerSession"/> へ渡ることの検証。
    ///
    /// <b>ここで守りたいのは 2 つ</b>です。
    /// <list type="number">
    /// <item>境界を越えるのは <b>MediaId(string)だけ</b>(URL も MediaItem も渡さない)</item>
    /// <item>再生エンジンに何も足していない(<see cref="PlayerSession"/> の既存 API だけを呼ぶ)</item>
    /// </list>
    /// </summary>
    public sealed class LibraryPlaybackBridgeTests
    {
        private IMediaCatalog _catalog;
        private MediaLibrary _library;
        private PlayerSession _session;
        private LibraryPlaybackBridge _bridge;

        [SetUp]
        public void SetUp()
        {
            var logger = new ListBackendLogger();
            _catalog = new MediaCatalog(new MixedCatalogSource(), new System.Random(1));

            _library = new MediaLibrary(_catalog);

            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, logger);
            backendManager.RegisterBackend(new DummyBackend("DummyBackend", logger));

            var mediaPlayer = new MediaPlayer(backendManager, logger);
            var engine = new RecommendationEngine(_catalog, RecommendationRule.CreateDefault());
            _session = new PlayerSession(
                "test", mediaPlayer, _catalog, engine, new System.Random(1), logger);

            _bridge = new LibraryPlaybackBridge(_library, _session, logger);
        }

        // ───────── 前提 ─────────

        [Test]
        public void Constructor_RejectsNulls()
        {
            Assert.Throws<ArgumentNullException>(
                () => new LibraryPlaybackBridge(null, _session));
            Assert.Throws<ArgumentNullException>(
                () => new LibraryPlaybackBridge(_library, null));
        }

        [Test]
        public void Bridge_ExposesBothSides()
        {
            Assert.AreSame(_library, _bridge.Library);
            Assert.AreSame(_session, _bridge.Session);
        }

        // ───────── 選んだものを再生する ─────────

        [Test]
        public void PlaySelected_StartsTheSelectedMedia()
        {
            _library.ShowOnly(MediaType.Video);
            _library.SelectById("video-003");

            Assert.IsTrue(_bridge.PlaySelected());

            Assert.AreEqual("video-003", _session.CurrentMediaId);
            Assert.AreEqual(PlaybackState.Playing, _session.PlaybackState);
            Assert.AreEqual(1, _bridge.PlayCount);
            Assert.AreEqual("video-003", _bridge.LastHandedOffId);
        }

        [Test]
        public void PlaySelected_DoesNothingWithoutASelection()
        {
            _library.ClearSelection();

            Assert.IsFalse(_bridge.PlaySelected());
            Assert.AreEqual(0, _bridge.PlayCount);
            Assert.IsNull(_session.CurrentMediaId);
        }

        [Test]
        public void PlayAt_SelectsAndPlaysInOneStep()
        {
            _library.ShowOnly(MediaType.Video);

            Assert.IsTrue(_bridge.PlayAt(1));

            Assert.AreEqual(1, _library.SelectedIndex, "一覧のクリックが選択にもなる");
            Assert.AreEqual(_library.GetAt(1).Id, _session.CurrentMediaId);
        }

        [Test]
        public void PlayAt_RejectsAnIndexOutsideTheList()
        {
            Assert.IsFalse(_bridge.PlayAt(-1));
            Assert.IsFalse(_bridge.PlayAt(_library.Count));
            Assert.AreEqual(0, _bridge.PlayCount);
        }

        [Test]
        public void Play_AcceptsAMediaIdDirectly()
        {
            Assert.IsTrue(_bridge.Play("music-002"));

            Assert.AreEqual("music-002", _session.CurrentMediaId);
        }

        [Test]
        public void Play_RejectsEmptyIds()
        {
            Assert.IsFalse(_bridge.Play(null));
            Assert.IsFalse(_bridge.Play(""));
            Assert.IsFalse(_bridge.Play("   "));
        }

        [Test]
        public void PlaySelected_SwitchesWhileSomethingElseIsAlreadyPlaying()
        {
            _library.ShowOnly(MediaType.Video);

            _bridge.PlayAt(0);
            string first = _session.CurrentMediaId;
            Assert.IsTrue(_session.IsPlaying, "この検証は 1 本目が鳴っている前提");

            _bridge.PlayAt(1);

            // MediaPlayer.Play() は「何も読み込んでいないときだけ」Queue の先頭を読むので、
            // 再生中に呼び直しても切り替わらない。Bridge が Next() で移す必要がある。
            Assert.AreEqual(_library.GetAt(1).Id, _session.CurrentMediaId,
                "選び直したものへ実際に切り替わる");
            Assert.AreNotEqual(first, _session.CurrentMediaId);
            Assert.IsTrue(_session.IsPlaying, "切り替えたあとも鳴っている");
        }

        [Test]
        public void Play_SwitchesAcrossSeveralPicksInARow()
        {
            _library.ShowOnly(MediaType.Video);

            for (int i = 0; i < 5; i++)
            {
                Assert.IsTrue(_bridge.PlayAt(i), $"{i + 1} 回目の選び直しで失敗した");
                Assert.AreEqual(_library.GetAt(i).Id, _session.CurrentMediaId,
                    $"{i + 1} 回目で狙ったものに切り替わっていない");
            }

            Assert.AreEqual(5, _bridge.PlayCount);
        }

        [Test]
        public void Play_SwitchesFromAPausedState()
        {
            _library.ShowOnly(MediaType.Video);
            _bridge.PlayAt(0);
            _session.Pause();

            _bridge.PlayAt(2);

            Assert.AreEqual(_library.GetAt(2).Id, _session.CurrentMediaId);
            Assert.IsTrue(_session.IsPlaying, "一時停止から選び直しても鳴り始める");
        }

        [Test]
        public void Play_OnTheAlreadyPlayingMediaKeepsItGoing()
        {
            _library.ShowOnly(MediaType.Video);
            _bridge.PlayAt(0);
            string playing = _session.CurrentMediaId;

            Assert.IsTrue(_bridge.PlaySelected(), "同じものを選び直しても失敗しない");

            Assert.AreEqual(playing, _session.CurrentMediaId);
            Assert.IsTrue(_session.IsPlaying);
        }

        [Test]
        public void Play_AddsToTheQueueOnlyWhenItIsNotThereAlready()
        {
            _library.ShowOnly(MediaType.Video);
            _bridge.PlayAt(0);

            // おすすめで既に Queue に入っているものを選び直しても、二重に積まない
            string alreadyQueued = _session.Queue.GetAll()[1].MediaId;
            int before = _session.Queue.Count;

            _bridge.Play(alreadyQueued);

            Assert.AreEqual(alreadyQueued, _session.CurrentMediaId);
            Assert.LessOrEqual(_session.Queue.Count, before,
                "Queue に居るものを選び直しても増えない");
        }

        // ───────── Queue へ足す ─────────

        [Test]
        public void EnqueueSelected_AddsToTheQueueWithoutStopping()
        {
            _library.ShowOnly(MediaType.Video);
            _bridge.PlayAt(0);
            string playing = _session.CurrentMediaId;

            _library.SelectById("video-007");
            Assert.IsTrue(_bridge.EnqueueSelected());

            Assert.AreEqual(playing, _session.CurrentMediaId, "いまの再生は止まらない");
            Assert.IsTrue(_session.Queue.Contains("video-007"));
            Assert.AreEqual(1, _bridge.EnqueueCount);
        }

        [Test]
        public void EnqueueSelected_DoesNothingWithoutASelection()
        {
            _library.ClearSelection();

            Assert.IsFalse(_bridge.EnqueueSelected());
            Assert.AreEqual(0, _bridge.EnqueueCount);
        }

        [Test]
        public void Enqueue_RejectsSomethingNotInTheCatalog()
        {
            Assert.IsFalse(_bridge.Enqueue("does-not-exist"));
            Assert.AreEqual(0, _bridge.EnqueueCount);
        }

        [Test]
        public void Enqueue_RejectsEmptyIds()
        {
            Assert.IsFalse(_bridge.Enqueue(null));
            Assert.IsFalse(_bridge.Enqueue(""));
        }

        // ───────── 境界 ─────────

        [Test]
        public void OnlyAMediaIdCrossesTheBoundary()
        {
            _library.ShowOnly(MediaType.Video);
            _library.Select(0);

            _bridge.PlaySelected();

            Assert.IsInstanceOf<string>(_bridge.LastHandedOffId);
            Assert.AreEqual(_library.SelectedItem.Id, _bridge.LastHandedOffId);

            // URL を受け取る口も、返す口も無い
            var type = typeof(LibraryPlaybackBridge);
            Assert.IsNull(type.GetMethod("PlayUrl"));
            Assert.IsNull(type.GetProperty("Url"));

            foreach (var method in type.GetMethods())
            {
                foreach (var parameter in method.GetParameters())
                {
                    Assert.AreNotEqual("url", parameter.Name.ToLowerInvariant(),
                        $"{method.Name} が URL を受け取っている");
                }
            }
        }

        [Test]
        public void TheQueueOnlyEverSeesCatalogItems()
        {
            _library.ShowOnly(MediaType.Video);
            _bridge.PlayAt(0);

            foreach (var entry in _session.Queue.GetAll())
            {
                Assert.IsNotNull(_catalog.FindById(entry.MediaId),
                    "Queue に入るのはカタログにあるものだけ(Library が作った項目は入らない)");
            }
        }

        [Test]
        public void BridgeAssembly_IsTheOnlyPlaceThatSeesBothSides()
        {
            var bridgeRefs = typeof(LibraryPlaybackBridge).Assembly
                .GetReferencedAssemblies().Select(x => x.Name).ToArray();

            CollectionAssert.Contains(bridgeRefs, "SmartMediaPlatform.Library");
            CollectionAssert.Contains(bridgeRefs, "SmartMediaPlatform.Session");

            var libraryRefs = typeof(MediaLibrary).Assembly
                .GetReferencedAssemblies().Select(x => x.Name).ToArray();

            CollectionAssert.DoesNotContain(libraryRefs, "SmartMediaPlatform.Session",
                "Library 単体では再生側が見えない");
        }

        // ───────── 再生エンジンを変えていない ─────────

        [Test]
        public void Session_KeepsItsOwnAutoQueueBehaviour()
        {
            _library.ShowOnly(MediaType.Video);
            _bridge.PlayAt(0);

            Assert.IsTrue(_session.AutoQueueEnabled,
                "Library は PlayerSession の設定を書き換えない");
            Assert.GreaterOrEqual(_session.Queue.Count, 1,
                "続きはセッションがいつもどおり補充する");
        }

        [Test]
        public void Session_StillAdvancesOnItsOwn()
        {
            _library.ShowOnly(MediaType.Video);
            _bridge.PlayAt(0);
            string first = _session.CurrentMediaId;

            Assert.IsTrue(_session.Next(), "Next は今までどおり PlayerSession の仕事");
            Assert.AreNotEqual(first, _session.CurrentMediaId);
        }

        [Test]
        public void Bridge_IsNotABackendObserver()
        {
            Assert.IsNotInstanceOf<IBackendObserver>(_bridge,
                "受け渡すだけ — 再生の進行には関与しない");
        }

        // ───────── 診断 ─────────

        [Test]
        public void Describe_ReportsBothSides()
        {
            _library.ShowOnly(MediaType.Video);
            _bridge.PlayAt(0);

            string text = _bridge.Describe();

            StringAssert.Contains("選択=", text);
            StringAssert.Contains("再生指示=1", text);
        }
    }
}
