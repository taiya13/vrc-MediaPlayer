using System;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;

namespace SmartMediaPlatform.Playlists.Tests
{
    /// <summary>Playlist → Queue の生成、再生モード、おすすめ自動補充の検証。</summary>
    public sealed class AutoQueueServiceTests
    {
        private IMediaCatalog _catalog;
        private MediaQueue _queue;
        private RecommendationEngine _engine;
        private AutoQueueService _service;
        private Playlist _playlist;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new Random(1));

            // ランダム補正を 0 にして順位を決定的にする
            _engine = new RecommendationEngine(
                _catalog,
                new[]
                {
                    RecommendationRule.Related(10.0),
                    RecommendationRule.SameArtist(5.0),
                    RecommendationRule.SameGenre(3.0),
                    RecommendationRule.TagMatch(1.0),
                    RecommendationRule.Random(0.0),
                },
                new Random(1));

            _queue = new MediaQueue();
            _service = new AutoQueueService(_queue, _catalog, _engine, new Random(1));

            _playlist = new Playlist("pl-1", "テスト");
            _playlist.AddRange(new[] { "music-001", "music-002", "music-003" });
        }

        private string[] QueueIds() => _queue.GetAll().Select(x => x.MediaId).ToArray();

        // ───────── Playlist → Queue ─────────

        [Test]
        public void PlayPlaylist_GeneratesQueueInPlaylistOrder()
        {
            int added = _service.PlayPlaylist(_playlist);

            Assert.AreEqual(3, added);
            CollectionAssert.AreEqual(
                new[] { "music-001", "music-002", "music-003" }, QueueIds());
        }

        [Test]
        public void PlayPlaylist_ReplacesPreviousQueue()
        {
            _queue.Enqueue(_catalog.FindById("music-010"));
            _service.PlayPlaylist(_playlist);

            Assert.IsFalse(_queue.Contains("music-010"), "前の Queue は作り直される");
            Assert.AreEqual(3, _queue.Count);
        }

        [Test]
        public void PlayPlaylist_SkipsIdsMissingFromCatalog()
        {
            _playlist.Add("no-such-id");

            int added = _service.PlayPlaylist(_playlist);

            Assert.AreEqual(3, added, "カタログに無い ID は読み飛ばす");
            Assert.IsFalse(_queue.Contains("no-such-id"));
        }

        [Test]
        public void PlayPlaylist_WithEmptyPlaylist_ClearsQueue()
        {
            _service.PlayPlaylist(_playlist);

            int added = _service.PlayPlaylist(new Playlist("empty"));

            Assert.AreEqual(0, added);
            Assert.IsTrue(_queue.IsEmpty);
        }

        [Test]
        public void PlayPlaylist_WithNull_ClearsQueue()
        {
            _service.PlayPlaylist(_playlist);

            Assert.AreEqual(0, _service.PlayPlaylist(null));
            Assert.IsTrue(_queue.IsEmpty);
            Assert.IsNull(_service.CurrentPlaylist);
        }

        // ───────── Shuffle ─────────

        [Test]
        public void Shuffle_QueueContainsEveryTrack()
        {
            _service.Mode = PlaybackMode.Shuffle;

            _service.PlayPlaylist(_playlist);

            Assert.AreEqual(3, _queue.Count);
            CollectionAssert.AreEquivalent(
                new[] { "music-001", "music-002", "music-003" }, QueueIds());
        }

        [Test]
        public void Shuffle_ChangesOrderForLargerPlaylists()
        {
            var big = new Playlist("big");
            for (int i = 1; i <= 10; i++) big.Add($"music-{i:000}");

            _service.Mode = PlaybackMode.Shuffle;
            _service.PlayPlaylist(big);

            CollectionAssert.AreEquivalent(big.MediaIds.ToArray(), QueueIds());
            CollectionAssert.AreNotEqual(big.MediaIds.ToArray(), QueueIds(),
                "10 曲もあればシャッフルで順が変わる");
        }

        [Test]
        public void IsShuffle_ReflectsMode()
        {
            _service.Mode = PlaybackMode.Normal;
            Assert.IsFalse(_service.IsShuffle);

            _service.Mode = PlaybackMode.Shuffle;
            Assert.IsTrue(_service.IsShuffle);

            _service.Mode = PlaybackMode.ShuffleAutoQueue;
            Assert.IsTrue(_service.IsShuffle);
        }

        // ───────── Repeat One ─────────

        [Test]
        public void RepeatOne_KeepsTheSameTrackQueuedNext()
        {
            _service.PlayPlaylist(_playlist);
            _service.Mode = PlaybackMode.RepeatOne;

            // Queue を 1 曲だけにして「いま music-001 を再生中」の状態を作る
            _queue.Clear();
            _queue.Enqueue(_catalog.FindById("music-001"));

            _service.EnsureFilled("music-001");

            CollectionAssert.AreEqual(new[] { "music-001", "music-001" }, QueueIds(),
                "次にも同じ曲が来る");
        }

        [Test]
        public void RepeatOne_SurvivesRepeatedAdvancing()
        {
            _service.Mode = PlaybackMode.RepeatOne;
            _queue.Enqueue(_catalog.FindById("music-002"));

            // 「曲が終わって次へ進む」を 3 回繰り返しても同じ曲であり続ける
            for (int i = 0; i < 3; i++)
            {
                _service.EnsureFilled("music-002");
                _queue.Skip();
                Assert.AreEqual("music-002", _queue.Peek().MediaId);
            }
        }

        [Test]
        public void RepeatOne_DoesNothingWhenAlreadyQueued()
        {
            _service.Mode = PlaybackMode.RepeatOne;
            _queue.Enqueue(_catalog.FindById("music-001"));
            _queue.Enqueue(_catalog.FindById("music-001"));

            Assert.AreEqual(0, _service.EnsureFilled("music-001"));
            Assert.AreEqual(2, _queue.Count);
        }

        [Test]
        public void RepeatOne_UsesQueueHeadWhenCurrentIsUnknown()
        {
            _service.Mode = PlaybackMode.RepeatOne;
            _queue.Enqueue(_catalog.FindById("music-003"));

            _service.EnsureFilled(null);

            CollectionAssert.AreEqual(new[] { "music-003", "music-003" }, QueueIds());
        }

        // ───────── Repeat All ─────────

        [Test]
        public void RepeatAll_RefillsWholePlaylistWhenRunningOut()
        {
            _service.PlayPlaylist(_playlist);
            _service.Mode = PlaybackMode.RepeatAll;

            // 最後の 1 曲まで進んだ状態にする
            _queue.Skip();
            _queue.Skip();
            Assert.AreEqual(1, _queue.Count);

            int added = _service.EnsureFilled("music-003");

            Assert.AreEqual(3, added);
            CollectionAssert.AreEqual(
                new[] { "music-003", "music-001", "music-002", "music-003" }, QueueIds(),
                "現在の曲の後ろにプレイリストが 1 巡ぶん積まれる");
        }

        [Test]
        public void RepeatAll_LoopsForeverWithSingleTrackPlaylist()
        {
            var single = new Playlist("single");
            single.Add("music-001");

            _service.Mode = PlaybackMode.RepeatAll;
            _service.PlayPlaylist(single);

            // 1 曲だけでも詰まらずに繰り返せる
            for (int i = 0; i < 3; i++)
            {
                _service.EnsureFilled("music-001");
                Assert.GreaterOrEqual(_queue.Count, 2);
                _queue.Skip();
                Assert.AreEqual("music-001", _queue.Peek().MediaId);
            }
        }

        [Test]
        public void RepeatAll_DoesNothingWhileQueueHasRoom()
        {
            _service.PlayPlaylist(_playlist);
            _service.Mode = PlaybackMode.RepeatAll;

            Assert.AreEqual(0, _service.EnsureFilled("music-001"));
            Assert.AreEqual(3, _queue.Count);
        }

        // ───────── Auto Queue(おすすめ補充) ─────────

        [Test]
        public void AutoQueue_RefillsWhenQueueRunsLow()
        {
            _service.PlayPlaylist(_playlist);

            // プレイリストを使い切った状態にする
            _queue.Skip();
            _queue.Skip();
            Assert.AreEqual(1, _queue.Count);

            int added = _service.EnsureFilled("music-003");

            Assert.Greater(added, 0, "おすすめで補充される");
            Assert.AreEqual(_service.TargetQueueCount, _queue.Count);
        }

        [Test]
        public void AutoQueue_CanBeTurnedOff()
        {
            _service.PlayPlaylist(_playlist);
            _service.AutoQueueEnabled = false;

            _queue.Skip();
            _queue.Skip();

            Assert.AreEqual(0, _service.EnsureFilled("music-003"),
                "Auto Queue を切ると補充しない");
            Assert.AreEqual(1, _queue.Count);
        }

        [Test]
        public void ShuffleAutoQueue_ForcesAutoQueueOn()
        {
            _service.Mode = PlaybackMode.ShuffleAutoQueue;
            _service.AutoQueueEnabled = false;   // 明示的に切っても

            Assert.IsTrue(_service.IsAutoQueueActive, "モードが優先される");

            _service.PlayPlaylist(_playlist);
            _queue.Skip();
            _queue.Skip();

            Assert.Greater(_service.EnsureFilled("music-003"), 0);
        }

        [Test]
        public void AutoQueue_DoesNotRecommendTracksAlreadyInQueue()
        {
            _service.PlayPlaylist(_playlist);
            _queue.Skip();
            _queue.Skip();

            _service.EnsureFilled("music-003");

            var ids = QueueIds();
            CollectionAssert.AllItemsAreUnique(ids, "Queue にある曲は積まれない");
        }

        [Test]
        public void AutoQueue_DoesNotRecommendTheCurrentTrack()
        {
            _service.PlayPlaylist(_playlist);
            _queue.Skip();
            _queue.Skip();

            _service.EnsureFilled("music-003");

            Assert.AreEqual(1, QueueIds().Count(id => id == "music-003"),
                "いま鳴っている曲は続けて推薦しない");
        }

        [Test]
        public void AutoQueue_DoesNotRepeatRecentRecommendationsImmediately()
        {
            _service.PlayPlaylist(_playlist);
            _queue.Skip();
            _queue.Skip();

            _service.EnsureFilled("music-003");
            var firstBatch = QueueIds().Skip(1).ToArray();

            // 補充分をすべて消費してから、もう一度補充する
            while (_queue.Count > 1) _queue.Skip();
            _service.EnsureFilled(_queue.Peek().MediaId);

            var secondBatch = QueueIds().Skip(1).ToArray();
            CollectionAssert.IsNotSubsetOf(secondBatch, firstBatch,
                "直前に推薦した曲がそのまま繰り返されない");
        }

        [Test]
        public void AutoQueue_PrefersPlaylistTracks()
        {
            // プレイリストに関連の薄い曲を入れておき、それでも優先されることを見る
            var playlist = new Playlist("pl-2");
            playlist.AddRange(new[] { "music-001", "music-009", "music-010" });
            _service.PlayPlaylist(playlist);

            // music-001 だけ残して他を消費 -> 補充時にプレイリストの曲が優先される
            _queue.Skip();
            _queue.Skip();

            _service.EnsureFilled("music-010");

            var refilled = QueueIds().Skip(1).ToArray();
            Assert.IsTrue(refilled.Any(id => playlist.Contains(id)),
                "プレイリスト内の曲が優先して積まれる");
        }

        [Test]
        public void AutoQueue_FallsBackToCatalogWhenPlaylistIsExhausted()
        {
            var playlist = new Playlist("tiny");
            playlist.Add("music-001");
            _service.PlayPlaylist(playlist);

            int added = _service.EnsureFilled("music-001");

            Assert.Greater(added, 0);
            var refilled = QueueIds().Skip(1).ToArray();
            Assert.IsTrue(refilled.All(id => !playlist.Contains(id)),
                "プレイリストが尽きたら Catalog 全体から選ばれる");
        }

        [Test]
        public void AutoQueue_WithoutPlaylist_StillWorksFromCatalog()
        {
            _queue.Enqueue(_catalog.FindById("music-001"));

            int added = _service.EnsureFilled("music-001");

            Assert.Greater(added, 0, "プレイリスト無しでも Catalog から補充できる");
        }

        [Test]
        public void AutoQueue_OnEmptyQueue_PicksASeed()
        {
            Assert.IsTrue(_queue.IsEmpty);

            int added = _service.EnsureFilled(null);

            Assert.Greater(added, 0, "空でもカタログから種を選んで補充する");
        }

        [Test]
        public void AutoQueue_DoesNothingWhileQueueHasRoom()
        {
            _service.PlayPlaylist(_playlist);

            Assert.AreEqual(0, _service.EnsureFilled("music-001"));
        }

        // ───────── Tick(変化検知) ─────────

        [Test]
        public void Tick_OnlyWorksWhenQueueChanged()
        {
            _service.PlayPlaylist(_playlist);

            Assert.AreEqual(0, _service.Tick("music-001"), "変化が無ければ何もしない");

            _queue.Skip();
            _queue.Skip();
            int added = _service.Tick("music-003");

            Assert.Greater(added, 0, "Queue が変わったので維持処理が走る");
            Assert.AreEqual(0, _service.Tick("music-003"), "同じ状態では二重に走らない");
        }

        // ───────── ログ / ガード ─────────

        [Test]
        public void Logger_RecordsQueueGeneration()
        {
            var lines = new System.Collections.Generic.List<string>();
            _service.Logger = lines.Add;

            _service.PlayPlaylist(_playlist);

            Assert.IsTrue(lines.Any(l => l.Contains("PlayPlaylist")));
            Assert.IsTrue(lines.Any(l => l.Contains("Queue に 3 曲")));
        }

        [Test]
        public void Constructor_RejectsNullDependencies()
        {
            Assert.Throws<ArgumentNullException>(
                () => new AutoQueueService(null, _catalog, _engine));
            Assert.Throws<ArgumentNullException>(
                () => new AutoQueueService(_queue, null, _engine));
            Assert.Throws<ArgumentNullException>(
                () => new AutoQueueService(_queue, _catalog, null));
        }
    }
}
