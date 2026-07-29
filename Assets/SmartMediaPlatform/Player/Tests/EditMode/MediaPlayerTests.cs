using System;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Queue;

namespace SmartMediaPlatform.Player.Tests
{
    /// <summary>再生制御の統一 API の検証。</summary>
    public sealed class MediaPlayerTests
    {
        private IMediaCatalog _catalog;
        private ListBackendLogger _logger;
        private MediaQueue _queue;
        private BackendManager _manager;
        private SeekableTestBackend _backend;
        private MediaPlayer _player;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _logger = new ListBackendLogger();
            _queue = new MediaQueue();
            _manager = new BackendManager(_queue, _logger);
            _backend = new SeekableTestBackend();

            _player = new MediaPlayer(_manager, _logger);
            _player.RegisterBackend(_backend);
        }

        private MediaItem M(string id) => _catalog.FindById(id);

        private void Fill(params string[] ids)
        {
            foreach (var id in ids) _queue.Enqueue(M(id));
        }

        // ───────── Play / Pause / Resume / Stop ─────────

        [Test]
        public void Play_LoadsQueueHeadAutomatically()
        {
            Fill("music-001", "music-002");

            Assert.IsTrue(_player.Play());
            Assert.AreEqual("music-001", _player.GetCurrent().Id);
            Assert.IsTrue(_player.IsPlaying());
            Assert.AreEqual(BackendState.Playing, _player.GetState());
        }

        [Test]
        public void Play_WithEmptyQueue_Fails()
        {
            Assert.IsFalse(_player.Play());
            Assert.IsNull(_player.GetCurrent());
        }

        [Test]
        public void PauseResume_RoundTrips()
        {
            Fill("music-001");
            _player.Play();

            Assert.IsTrue(_player.Pause());
            Assert.AreEqual(BackendState.Paused, _player.GetState());
            Assert.IsFalse(_player.IsPlaying());

            Assert.IsTrue(_player.Resume());
            Assert.AreEqual(BackendState.Playing, _player.GetState());
            Assert.IsTrue(_player.IsPlaying());
        }

        [Test]
        public void Play_WhilePaused_ActsAsResume()
        {
            Fill("music-001");
            _player.Play();
            _player.Pause();

            Assert.IsTrue(_player.Play(), "利用者から見れば Play は再開として働く");
            Assert.AreEqual(BackendState.Playing, _player.GetState());
        }

        [Test]
        public void Stop_ReturnsToStoppedAndCanPlayAgain()
        {
            Fill("music-001");
            _player.Play();

            Assert.IsTrue(_player.Stop());
            Assert.AreEqual(BackendState.Stopped, _player.GetState());
            Assert.IsFalse(_player.IsPlaying());
            Assert.AreEqual("music-001", _player.GetCurrent().Id, "停止しても読み込みは保持される");

            Assert.IsTrue(_player.Play(), "停止後もそのまま再生を再開できる");
            Assert.AreEqual(BackendState.Playing, _player.GetState());
        }

        [Test]
        public void TogglePlayPause_CyclesThroughStates()
        {
            Fill("music-001");

            Assert.IsTrue(_player.TogglePlayPause());               // 何も再生していない -> Play
            Assert.AreEqual(BackendState.Playing, _player.GetState());

            Assert.IsTrue(_player.TogglePlayPause());               // Playing -> Pause
            Assert.AreEqual(BackendState.Paused, _player.GetState());

            Assert.IsTrue(_player.TogglePlayPause());               // Paused -> Resume
            Assert.AreEqual(BackendState.Playing, _player.GetState());
        }

        // ───────── SkipNext / SkipPrevious ─────────

        [Test]
        public void SkipNext_AdvancesQueueAndKeepsPlaying()
        {
            Fill("music-001", "music-002", "music-003");
            _player.Play();

            Assert.IsTrue(_player.SkipNext());
            Assert.AreEqual("music-002", _player.GetCurrent().Id);
            Assert.AreEqual("music-002", _queue.Peek().MediaId, "Queue と連動する");
            Assert.IsTrue(_player.IsPlaying(), "再生中のスキップは再生を継続する");
        }

        [Test]
        public void SkipNext_WhileStopped_DoesNotStartPlaying()
        {
            Fill("music-001", "music-002");
            _player.Play();
            _player.Stop();

            _player.SkipNext();

            Assert.AreEqual("music-002", _player.GetCurrent().Id);
            Assert.IsFalse(_player.IsPlaying(), "止めていたなら勝手に鳴り出さない");
        }

        [Test]
        public void SkipNext_OnLastTrack_ReturnsFalse()
        {
            Fill("music-001");
            _player.Play();

            Assert.IsFalse(_player.SkipNext());
        }

        [Test]
        public void SkipPrevious_ReturnsToPreviousTrack()
        {
            Fill("music-001", "music-002", "music-003");
            _player.Play();
            _player.SkipNext();                       // -> music-002

            Assert.IsTrue(_player.SkipPrevious());

            Assert.AreEqual("music-001", _player.GetCurrent().Id);
            Assert.AreEqual("music-001", _queue.Peek().MediaId, "Queue の先頭も戻る");
            Assert.IsTrue(_player.IsPlaying());
        }

        [Test]
        public void SkipPrevious_RestoresQueueOrder()
        {
            Fill("music-001", "music-002", "music-003");
            _player.Play();
            _player.SkipNext();
            _player.SkipPrevious();

            CollectionAssert.AreEqual(
                new[] { "music-001", "music-002", "music-003" },
                _queue.GetAll().Select(x => x.MediaId).ToArray());
        }

        [Test]
        public void SkipPrevious_WithoutHistory_ReturnsFalse()
        {
            Fill("music-001", "music-002");
            _player.Play();

            Assert.IsFalse(_player.SkipPrevious());
            Assert.AreEqual(0, _player.HistoryCount);
        }

        [Test]
        public void SkipNext_BuildsHistory()
        {
            Fill("music-001", "music-002", "music-003");
            _player.Play();

            _player.SkipNext();
            Assert.AreEqual(1, _player.HistoryCount);

            _player.SkipNext();
            Assert.AreEqual(2, _player.HistoryCount);

            _player.SkipPrevious();
            Assert.AreEqual(1, _player.HistoryCount);
        }

        [Test]
        public void SkipPrevious_CanWalkBackSeveralTracks()
        {
            Fill("music-001", "music-002", "music-003");
            _player.Play();
            _player.SkipNext();     // -> 002
            _player.SkipNext();     // -> 003

            _player.SkipPrevious(); // -> 002
            Assert.AreEqual("music-002", _player.GetCurrent().Id);

            _player.SkipPrevious(); // -> 001
            Assert.AreEqual("music-001", _player.GetCurrent().Id);

            Assert.IsFalse(_player.SkipPrevious(), "履歴を使い切ったら false");
        }

        [Test]
        public void History_IsCappedByMaxHistory()
        {
            _player.MaxHistory = 2;
            Fill("music-001", "music-002", "music-003", "music-004", "music-005");
            _player.Play();

            _player.SkipNext();
            _player.SkipNext();
            _player.SkipNext();

            Assert.AreEqual(2, _player.HistoryCount);
        }

        // ───────── Ended → 自動で次へ ─────────

        [Test]
        public void Ended_AutomaticallyAdvancesAndKeepsPlaying()
        {
            Fill("music-001", "music-002");
            _player.Play();

            _backend.SimulateEnded();

            Assert.AreEqual("music-002", _player.GetCurrent().Id);
            Assert.IsTrue(_player.IsPlaying(), "自動送り後も再生が続く(Phase2-1 からの改善点)");
        }

        [Test]
        public void Ended_PlaysThroughTheWholeQueue()
        {
            Fill("music-001", "music-002", "music-003");
            _player.Play();

            _backend.SimulateEnded();
            Assert.AreEqual("music-002", _player.GetCurrent().Id);

            _backend.SimulateEnded();
            Assert.AreEqual("music-003", _player.GetCurrent().Id);
            Assert.IsTrue(_player.IsPlaying());
        }

        [Test]
        public void Ended_OnLastTrack_StopsGracefully()
        {
            Fill("music-001");
            _player.Play();

            _backend.SimulateEnded();

            Assert.AreEqual(BackendState.Ended, _player.GetState(),
                "最後の曲が終わったら Ended のまま(再生完了が分かる)");
            Assert.IsFalse(_player.IsPlaying());
        }

        [Test]
        public void SkipNext_WithNoNextTrack_IsNonDestructive()
        {
            // 次が無いときは Queue も再生状態も壊さない。
            Fill("music-001");
            _player.Play();

            Assert.IsFalse(_player.SkipNext());
            Assert.AreEqual(1, _queue.Count, "Queue は消費されない");
            Assert.AreEqual("music-001", _player.GetCurrent().Id);
            Assert.IsTrue(_player.IsPlaying(), "再生も止まらない");
        }

        [Test]
        public void Ended_DoesNotAdvanceWhenAutoAdvanceDisabled()
        {
            _player.AutoAdvanceOnEnded = false;
            Fill("music-001", "music-002");
            _player.Play();

            _backend.SimulateEnded();

            Assert.AreEqual("music-001", _player.GetCurrent().Id);
        }

        [Test]
        public void MediaPlayer_TakesOverAutoAdvanceFromBackendManager()
        {
            // 二重に反応しないよう、BackendManager 側の自動送りは切られる。
            Assert.IsFalse(_manager.AutoAdvanceOnEnded);
        }

        // ───────── Seek / 再生位置 ─────────

        [Test]
        public void CanSeek_IsTrueForSeekableBackend()
        {
            Fill("music-001");
            _player.Play();

            Assert.IsTrue(_player.CanSeek());
        }

        [Test]
        public void Seek_MovesPlaybackPosition()
        {
            Fill("music-001");
            _backend.Duration = 200f;
            _player.Play();

            Assert.IsTrue(_player.Seek(0.5f));
            Assert.AreEqual(100f, _player.GetCurrentTime(), 0.001f);
            Assert.AreEqual(0.5f, _player.GetProgress(), 0.001f);
        }

        [Test]
        public void Seek_ClampsOutOfRangeValues()
        {
            Fill("music-001");
            _backend.Duration = 100f;
            _player.Play();

            _player.Seek(-5f);
            Assert.AreEqual(0f, _player.GetCurrentTime(), 0.001f);

            _player.Seek(9f);
            Assert.AreEqual(100f, _player.GetCurrentTime(), 0.001f);
        }

        [Test]
        public void GetProgress_TracksPlaybackPosition()
        {
            Fill("music-001");
            _backend.Duration = 10f;
            _player.Play();

            Assert.AreEqual(0f, _player.GetProgress(), 0.001f);

            _backend.Advance(5f);
            Assert.AreEqual(0.5f, _player.GetProgress(), 0.001f);
            Assert.AreEqual(5f, _player.GetCurrentTime(), 0.001f);
        }

        [Test]
        public void GetDuration_ComesFromBackend()
        {
            Fill("music-001");
            _backend.Duration = 42f;
            _player.Play();

            Assert.AreEqual(42f, _player.GetDuration(), 0.001f);
        }

        // ───────── シーク非対応バックエンド ─────────

        [Test]
        public void CanSeek_IsFalseForNonSeekableBackend()
        {
            var queue = new MediaQueue();
            queue.Enqueue(M("music-001"));
            var manager = new BackendManager(queue, _logger);
            var player = new MediaPlayer(manager, _logger);
            player.RegisterBackend(new DummyBackend("Dummy", _logger, MediaType.Music));

            player.Play();

            Assert.IsFalse(player.CanSeek(), "DummyBackend は再生位置を持たない");
            Assert.IsFalse(player.Seek(0.5f), "シークは拒否される");
            Assert.AreEqual(0f, player.GetCurrentTime(), 0.001f);
        }

        [Test]
        public void GetDuration_FallsBackToCatalogMetadata()
        {
            // シークできないバックエンドでも、Catalog の長さは使える。
            var queue = new MediaQueue();
            queue.Enqueue(M("music-001"));
            var manager = new BackendManager(queue, _logger);
            var player = new MediaPlayer(manager, _logger);
            player.RegisterBackend(new DummyBackend("Dummy", _logger, MediaType.Music));

            player.Play();

            Assert.AreEqual(M("music-001").DurationSeconds, player.GetDuration(), 0.001f);
        }

        [Test]
        public void ControlsWithoutBackend_AreSafe()
        {
            var manager = new BackendManager(new MediaQueue(), _logger);
            var player = new MediaPlayer(manager, _logger);

            Assert.IsFalse(player.Play());
            Assert.IsFalse(player.Pause());
            Assert.IsFalse(player.Resume());
            Assert.IsFalse(player.Stop());
            Assert.IsFalse(player.SkipNext());
            Assert.IsFalse(player.SkipPrevious());
            Assert.IsFalse(player.Seek(0.5f));
            Assert.AreEqual(BackendState.Idle, player.GetState());
            Assert.IsFalse(player.IsPlaying());
            Assert.AreEqual(0f, player.GetProgress(), 0.001f);
        }

        // ───────── Backend の種類に依存しない ─────────

        [Test]
        public void SameControlSequence_WorksOnDifferentBackendTypes()
        {
            // まったく同じ操作列を、種類の違うバックエンドに対して流す。
            // MediaPlayer は Backend の種類を知らないので結果は一致する。
            var withSeekable = RunSequence(new SeekableTestBackend("Seekable"));
            var withDummy = RunSequence(new DummyBackend("Dummy", null, MediaType.Music));

            CollectionAssert.AreEqual(withSeekable, withDummy);
        }

        private BackendState[] RunSequence(IMediaBackend backend)
        {
            var queue = new MediaQueue();
            foreach (var id in new[] { "music-001", "music-002" }) queue.Enqueue(M(id));

            var manager = new BackendManager(queue, null);
            var player = new MediaPlayer(manager, null);
            player.RegisterBackend(backend);

            var states = new System.Collections.Generic.List<BackendState>();
            player.Play(); states.Add(player.GetState());
            player.Pause(); states.Add(player.GetState());
            player.Resume(); states.Add(player.GetState());
            player.SkipNext(); states.Add(player.GetState());
            player.SkipPrevious(); states.Add(player.GetState());
            player.Stop(); states.Add(player.GetState());
            return states.ToArray();
        }

        [Test]
        public void BackendSwitching_IsHandledByBackendManager()
        {
            var queue = new MediaQueue();
            queue.Enqueue(M("music-001"));
            queue.Enqueue(M("video-001"));

            var manager = new BackendManager(queue, _logger);
            var player = new MediaPlayer(manager, _logger);
            var music = new SeekableTestBackend("MusicBackend", MediaType.Music);
            var video = new SeekableTestBackend("VideoBackend", MediaType.Video);
            player.RegisterBackend(music);
            player.RegisterBackend(video);

            player.Play();
            Assert.AreSame(music, manager.ActiveBackend);

            player.SkipNext();

            Assert.AreSame(video, manager.ActiveBackend, "種別が変われば担当バックエンドも変わる");
            Assert.AreEqual("video-001", player.GetCurrent().Id);
            Assert.IsTrue(player.IsPlaying(), "上位コードは何も変えていない");
        }

        // ───────── ログ ─────────

        [Test]
        public void Operations_AreLoggedWithStateTransitions()
        {
            Fill("music-001", "music-002");
            _player.Play();
            _player.Pause();
            _player.SkipNext();

            string text = string.Join("\n", _logger.Lines);
            StringAssert.Contains("[MediaPlayer] Play", text);
            StringAssert.Contains("Ready -> Playing", text);
            StringAssert.Contains("[MediaPlayer] Pause", text);
            StringAssert.Contains("[MediaPlayer] SkipNext", text);
        }

        [Test]
        public void Describe_SummarizesCurrentState()
        {
            Fill("music-001");
            _backend.Duration = 100f;
            _player.Play();
            _player.Seek(0.25f);

            string text = _player.Describe();
            StringAssert.Contains("Playing", text);
            StringAssert.Contains("music-001", text);
        }

        // ───────── ガード ─────────

        [Test]
        public void Constructor_RejectsNullManager()
        {
            Assert.Throws<ArgumentNullException>(() => new MediaPlayer(null));
        }

        [Test]
        public void RegisterBackend_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => _player.RegisterBackend(null));
        }
    }
}
