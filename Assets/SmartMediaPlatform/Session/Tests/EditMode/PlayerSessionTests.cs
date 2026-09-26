using System;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;

namespace SmartMediaPlatform.Session.Tests
{
    /// <summary>PlayerSession の状態管理・モード制御・Ended 連携の検証。</summary>
    public sealed class PlayerSessionTests
    {
        private IMediaCatalog _catalog;
        private ListBackendLogger _logger;
        private MediaQueue _queue;
        private MediaPlayer _player;
        private SessionTestBackend _backend;
        private PlayerSession _session;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new Random(1));
            _logger = new ListBackendLogger();

            var engine = new RecommendationEngine(
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
            var manager = new BackendManager(_queue, _logger);
            _player = new MediaPlayer(manager, _logger);
            _backend = new SessionTestBackend();

            _session = new PlayerSession("s1", _player, _catalog, engine, new Random(1), _logger);
            _session.RegisterBackend(_backend);
        }

        private string[] QueueIds() => _queue.GetAll().Select(x => x.MediaId).ToArray();

        private int SetThreeTracks() =>
            _session.SetTracks(new[] { "music-001", "music-002", "music-003" });

        // ───────── 状態管理 ─────────

        [Test]
        public void NewSession_StartsIdle()
        {
            Assert.AreEqual(PlaybackState.Idle, _session.PlaybackState);
            Assert.AreEqual(BackendState.Idle, _session.BackendState);
            Assert.IsNull(_session.CurrentMediaId);
            Assert.IsEmpty(_session.History);
            Assert.IsFalse(_session.IsPlaying);
        }

        [Test]
        public void SetTracks_BuildsQueue()
        {
            int added = SetThreeTracks();

            Assert.AreEqual(3, added);
            CollectionAssert.AreEqual(
                new[] { "music-001", "music-002", "music-003" }, QueueIds());
            CollectionAssert.AreEqual(
                new[] { "music-001", "music-002", "music-003" }, _session.Tracks.ToArray());
        }

        [Test]
        public void SetTracks_SkipsUnknownAndDuplicateIds()
        {
            int added = _session.SetTracks(
                new[] { "music-001", "music-001", "no-such-id", "music-002" });

            Assert.AreEqual(2, added);
            CollectionAssert.AreEqual(new[] { "music-001", "music-002" }, QueueIds());
        }

        [Test]
        public void Play_StartsFromQueueHead()
        {
            SetThreeTracks();

            Assert.IsTrue(_session.Play());
            Assert.AreEqual("music-001", _session.CurrentMediaId);
            Assert.AreEqual(PlaybackState.Playing, _session.PlaybackState);
            Assert.IsTrue(_session.IsPlaying);
        }

        [Test]
        public void PlaybackState_TracksTheBackend()
        {
            SetThreeTracks();
            _session.Play();
            Assert.AreEqual(PlaybackState.Playing, _session.PlaybackState);

            _session.Pause();
            Assert.AreEqual(PlaybackState.Paused, _session.PlaybackState);

            _session.Resume();
            Assert.AreEqual(PlaybackState.Playing, _session.PlaybackState);

            _session.Stop();
            Assert.AreEqual(PlaybackState.Stopped, _session.PlaybackState);
        }

        [Test]
        public void TogglePlayPause_Works()
        {
            SetThreeTracks();

            _session.TogglePlayPause();
            Assert.AreEqual(PlaybackState.Playing, _session.PlaybackState);

            _session.TogglePlayPause();
            Assert.AreEqual(PlaybackState.Paused, _session.PlaybackState);

            _session.TogglePlayPause();
            Assert.AreEqual(PlaybackState.Playing, _session.PlaybackState);
        }

        [Test]
        public void Enqueue_AddsToQueueAndTracks()
        {
            SetThreeTracks();

            Assert.IsTrue(_session.Enqueue("music-010"));
            Assert.IsTrue(_queue.Contains("music-010"));
            CollectionAssert.Contains(_session.Tracks.ToArray(), "music-010");

            Assert.IsFalse(_session.Enqueue("no-such-id"));
        }

        [Test]
        public void ClearQueue_KeepsTracks()
        {
            SetThreeTracks();
            _session.ClearQueue();

            Assert.AreEqual(0, _queue.Count);
            Assert.AreEqual(3, _session.Tracks.Count);
        }

        // ───────── Queue 管理 / 履歴 ─────────

        [Test]
        public void Next_AdvancesAndRecordsHistory()
        {
            SetThreeTracks();
            _session.Play();

            Assert.IsTrue(_session.Next());

            Assert.AreEqual("music-002", _session.CurrentMediaId);
            CollectionAssert.AreEqual(new[] { "music-001" }, _session.History.ToArray());
            Assert.IsTrue(_session.IsPlaying, "再生中に進めば再生は続く");
        }

        [Test]
        public void Previous_GoesBackAndTrimsHistory()
        {
            SetThreeTracks();
            _session.Play();
            _session.Next();
            Assert.AreEqual(1, _session.History.Count);

            Assert.IsTrue(_session.Previous());

            Assert.AreEqual("music-001", _session.CurrentMediaId);
            Assert.AreEqual(0, _session.History.Count);
        }

        [Test]
        public void Previous_WithoutHistory_Fails()
        {
            SetThreeTracks();
            _session.Play();

            Assert.IsFalse(_session.Previous());
        }

        [Test]
        public void History_IsCapped()
        {
            _session.MaxHistory = 2;
            _session.SetTracks(new[] { "music-001", "music-002", "music-003", "music-004" });
            _session.Play();

            _session.Next();
            _session.Next();
            _session.Next();

            Assert.AreEqual(2, _session.History.Count);
        }

        [Test]
        public void ClearHistory_EmptiesIt()
        {
            SetThreeTracks();
            _session.Play();
            _session.Next();

            _session.ClearHistory();
            Assert.IsEmpty(_session.History);
        }

        // ───────── Ended 連携 ─────────

        [Test]
        public void Ended_AdvancesToNextTrackAndKeepsPlaying()
        {
            SetThreeTracks();
            _session.Play();

            _backend.SimulateEnded();

            Assert.AreEqual("music-002", _session.CurrentMediaId);
            Assert.IsTrue(_session.IsPlaying);
            CollectionAssert.AreEqual(new[] { "music-001" }, _session.History.ToArray());
        }

        [Test]
        public void Ended_PlaysThroughEveryTrack()
        {
            _session.AutoQueueEnabled = false;
            SetThreeTracks();
            _session.Play();

            _backend.SimulateEnded();
            Assert.AreEqual("music-002", _session.CurrentMediaId);

            _backend.SimulateEnded();
            Assert.AreEqual("music-003", _session.CurrentMediaId);
        }

        [Test]
        public void Ended_MediaPlayerAutoAdvanceIsHandedOverToSession()
        {
            Assert.IsFalse(_player.AutoAdvanceOnEnded,
                "二重に反応しないよう MediaPlayer 側の自動送りは切られる");
        }

        [Test]
        public void Ended_WithNothingLeft_BecomesExhausted()
        {
            _session.AutoQueueEnabled = false;
            _session.SetTracks(new[] { "music-001" });
            _session.Play();

            _backend.SimulateEnded();

            Assert.AreEqual(PlaybackState.Exhausted, _session.PlaybackState);
            Assert.IsFalse(_session.IsPlaying);
        }

        // ───────── Repeat One ─────────

        [Test]
        public void RepeatOne_ReplaysTheSameTrack()
        {
            SetThreeTracks();
            _session.RepeatMode = RepeatMode.One;
            _session.Play();
            int playsBefore = _backend.PlayCallCount;

            _backend.SimulateEnded();

            Assert.AreEqual("music-001", _session.CurrentMediaId, "同じ曲のまま");
            Assert.AreEqual(playsBefore + 1, _backend.PlayCallCount, "もう一度再生される");
            Assert.IsTrue(_session.IsPlaying);
        }

        [Test]
        public void RepeatOne_DoesNotConsumeTheQueueOrHistory()
        {
            SetThreeTracks();
            _session.RepeatMode = RepeatMode.One;
            _session.Play();

            _backend.SimulateEnded();
            _backend.SimulateEnded();

            Assert.AreEqual(3, _queue.Count, "Queue は減らない");
            Assert.IsEmpty(_session.History, "履歴も増えない");
        }

        [Test]
        public void RepeatOne_CanBeTurnedOffMidPlayback()
        {
            SetThreeTracks();
            _session.RepeatMode = RepeatMode.One;
            _session.Play();
            _backend.SimulateEnded();
            Assert.AreEqual("music-001", _session.CurrentMediaId);

            _session.RepeatMode = RepeatMode.Off;
            _backend.SimulateEnded();

            Assert.AreEqual("music-002", _session.CurrentMediaId, "解除すれば次へ進む");
        }

        // ───────── Repeat All ─────────

        [Test]
        public void RepeatAll_LoopsBackToTheBeginning()
        {
            _session.AutoQueueEnabled = false;
            SetThreeTracks();
            _session.RepeatMode = RepeatMode.All;
            _session.Play();

            _backend.SimulateEnded();   // -> 002
            _backend.SimulateEnded();   // -> 003
            Assert.AreEqual("music-003", _session.CurrentMediaId);

            _backend.SimulateEnded();   // 一巡したので先頭へ戻る

            Assert.AreEqual("music-001", _session.CurrentMediaId);
            Assert.IsTrue(_session.IsPlaying);
        }

        [Test]
        public void RepeatAll_WorksWithASingleTrack()
        {
            _session.AutoQueueEnabled = false;
            _session.SetTracks(new[] { "music-001" });
            _session.RepeatMode = RepeatMode.All;
            _session.Play();

            for (int i = 0; i < 3; i++)
            {
                _backend.SimulateEnded();
                Assert.AreEqual("music-001", _session.CurrentMediaId);
                Assert.IsTrue(_session.IsPlaying, "1 曲だけでも詰まらない");
            }
        }

        [Test]
        public void RepeatAll_NeverBecomesExhausted()
        {
            _session.AutoQueueEnabled = false;
            SetThreeTracks();
            _session.RepeatMode = RepeatMode.All;
            _session.Play();

            for (int i = 0; i < 6; i++) _backend.SimulateEnded();

            Assert.AreNotEqual(PlaybackState.Exhausted, _session.PlaybackState);
        }

        // ───────── Shuffle ─────────

        [Test]
        public void Shuffle_QueueKeepsEveryTrack()
        {
            _session.ShuffleEnabled = true;
            SetThreeTracks();

            CollectionAssert.AreEquivalent(
                new[] { "music-001", "music-002", "music-003" }, QueueIds());
        }

        [Test]
        public void Shuffle_ChangesOrderForLargerTrackLists()
        {
            var ids = Enumerable.Range(1, 10).Select(i => $"music-{i:000}").ToArray();
            _session.ShuffleEnabled = true;

            _session.SetTracks(ids);

            CollectionAssert.AreEquivalent(ids, QueueIds());
            CollectionAssert.AreNotEqual(ids, QueueIds(), "10 曲あれば順が変わる");
        }

        [Test]
        public void Shuffle_CanBeCombinedWithRepeatAll()
        {
            _session.AutoQueueEnabled = false;
            _session.ShuffleEnabled = true;
            SetThreeTracks();
            _session.RepeatMode = RepeatMode.All;
            _session.Play();

            for (int i = 0; i < 5; i++) _backend.SimulateEnded();

            Assert.IsTrue(_session.IsPlaying, "シャッフル + 全曲繰り返しでも止まらない");
        }

        // ───────── Auto Queue / Recommendation ─────────

        [Test]
        public void AutoQueue_RefillsWhenTracksRunOut()
        {
            _session.SetTracks(new[] { "music-001" });
            Assert.AreEqual(1, _queue.Count, "最初は設定した 1 曲だけ");

            int added = _session.EnsureQueueFilled();

            Assert.Greater(added, 0);
            Assert.AreEqual(_session.TargetQueueCount, _queue.Count);
        }

        [Test]
        public void Play_TopsUpTheQueueBeforeStarting()
        {
            // 再生を始める前に補充されるので、1 曲しか無くても途切れない。
            _session.SetTracks(new[] { "music-001" });

            _session.Play();

            Assert.AreEqual(_session.TargetQueueCount, _queue.Count);
            Assert.AreEqual("music-001", _session.CurrentMediaId, "先頭の曲から始まる");
        }

        [Test]
        public void AutoQueue_KeepsPlaybackGoingAfterTheLastTrack()
        {
            _session.SetTracks(new[] { "music-001" });
            _session.Play();

            _backend.SimulateEnded();

            Assert.IsTrue(_session.IsPlaying, "Queue が尽きてもおすすめで再生が続く");
            Assert.AreNotEqual("music-001", _session.CurrentMediaId);
        }

        [Test]
        public void AutoQueue_CanBeTurnedOff()
        {
            _session.AutoQueueEnabled = false;
            _session.SetTracks(new[] { "music-001" });
            _session.Play();

            Assert.AreEqual(0, _session.EnsureQueueFilled());
            Assert.AreEqual(1, _queue.Count);
        }

        [Test]
        public void AutoQueue_DoesNotRecommendTheCurrentTrack()
        {
            _session.SetTracks(new[] { "music-001" });
            _session.Play();
            _session.EnsureQueueFilled();

            Assert.AreEqual(1, QueueIds().Count(id => id == "music-001"),
                "いま鳴っている曲は続けて推薦しない");
        }

        [Test]
        public void AutoQueue_DoesNotDuplicateQueuedTracks()
        {
            _session.SetTracks(new[] { "music-001" });
            _session.Play();
            _session.EnsureQueueFilled();

            CollectionAssert.AllItemsAreUnique(QueueIds());
        }

        [Test]
        public void AutoQueue_WorksWithoutAnyTracks()
        {
            Assert.IsTrue(_queue.IsEmpty);

            int added = _session.EnsureQueueFilled();

            Assert.Greater(added, 0, "曲を設定していなくてもカタログから補充できる");
        }

        [Test]
        public void AutoQueue_UsesCurrentTrackAsSeed()
        {
            // music-001 の関連は music-002 / music-007。種として使われていれば
            // それらが優先的に積まれる。
            _session.SetTracks(new[] { "music-001" });
            _session.Play();
            _session.EnsureQueueFilled();

            var refilled = QueueIds().Skip(1).ToArray();
            Assert.IsTrue(refilled.Contains("music-002") || refilled.Contains("music-007"),
                "再生中の曲の関連曲が積まれる");
        }

        // ───────── シーク(Backend の能力に依存)─────────

        [Test]
        public void Seek_WorksWhenTheBackendSupportsIt()
        {
            SetThreeTracks();
            _backend.Duration = 200f;
            _session.Play();

            Assert.IsTrue(_session.CanSeek());
            Assert.IsTrue(_session.Seek(0.5f));
            Assert.AreEqual(100f, _session.GetCurrentTime(), 0.001f);
            Assert.AreEqual(0.5f, _session.GetProgress(), 0.001f);
        }

        [Test]
        public void Seek_IsRejectedForNonSeekableBackends()
        {
            var queue = new MediaQueue();
            var manager = new BackendManager(queue, _logger);
            var player = new MediaPlayer(manager, _logger);
            var engine = new RecommendationEngine(_catalog, RecommendationRule.CreateDefault());
            var session = new PlayerSession("s2", player, _catalog, engine, new Random(1), _logger);
            session.RegisterBackend(new DummyBackend("Dummy", _logger, MediaType.Music));

            session.SetTracks(new[] { "music-001" });
            session.Play();

            Assert.IsFalse(session.CanSeek());
            Assert.IsFalse(session.Seek(0.5f));
        }

        // ───────── Backend の種類に依存しない ─────────

        [Test]
        public void Session_DoesNotDependOnBackendType()
        {
            // 種別が違うバックエンドでも、同じ操作で同じ状態遷移になる。
            var music = RunSequence(new SessionTestBackend("Music", MediaType.Music));
            var dummy = RunSequence(new DummyBackend("Dummy", null, MediaType.Music));

            CollectionAssert.AreEqual(music, dummy);
        }

        private PlaybackState[] RunSequence(IMediaBackend backend)
        {
            var queue = new MediaQueue();
            var manager = new BackendManager(queue, null);
            var player = new MediaPlayer(manager, null);
            var engine = new RecommendationEngine(_catalog, RecommendationRule.CreateDefault());
            var session = new PlayerSession("tmp", player, _catalog, engine, new Random(1));
            session.RegisterBackend(backend);
            session.AutoQueueEnabled = false;

            session.SetTracks(new[] { "music-001", "music-002" });

            var states = new System.Collections.Generic.List<PlaybackState>();
            session.Play(); states.Add(session.PlaybackState);
            session.Pause(); states.Add(session.PlaybackState);
            session.Resume(); states.Add(session.PlaybackState);
            session.Next(); states.Add(session.PlaybackState);
            session.Previous(); states.Add(session.PlaybackState);
            session.Stop(); states.Add(session.PlaybackState);
            return states.ToArray();
        }

        // ───────── ログ / ガード ─────────

        [Test]
        public void Operations_AreLogged()
        {
            SetThreeTracks();
            _session.Play();
            _session.Next();

            string text = string.Join("\n", _logger.Lines);
            StringAssert.Contains("[Session:s1]", text);
            StringAssert.Contains("Next:", text);
        }

        [Test]
        public void Describe_SummarizesState()
        {
            SetThreeTracks();
            _session.Play();

            string text = _session.Describe();
            StringAssert.Contains("Playing", text);
            StringAssert.Contains("music-001", text);
            StringAssert.Contains("Repeat", text);
        }

        [Test]
        public void Constructor_RejectsInvalidArguments()
        {
            var engine = new RecommendationEngine(_catalog, RecommendationRule.CreateDefault());

            Assert.Throws<ArgumentException>(
                () => new PlayerSession("", _player, _catalog, engine));
            Assert.Throws<ArgumentNullException>(
                () => new PlayerSession("x", null, _catalog, engine));
            Assert.Throws<ArgumentNullException>(
                () => new PlayerSession("x", _player, null, engine));
            Assert.Throws<ArgumentNullException>(
                () => new PlayerSession("x", _player, _catalog, null));
        }

        [Test]
        public void RegisterBackend_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => _session.RegisterBackend(null));
        }
    }
}
