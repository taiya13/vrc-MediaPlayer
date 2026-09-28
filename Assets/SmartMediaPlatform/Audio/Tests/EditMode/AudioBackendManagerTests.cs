using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using UnityEngine;

namespace SmartMediaPlatform.Audio.Tests
{
    /// <summary>
    /// Phase1 の設計思想(Backend 差し替え)が保たれているかの検証。
    ///
    /// 中心となる主張は 2 つ:
    ///  1. BackendManager を一切変更せずに AudioBackend を使える
    ///  2. DummyBackend と AudioBackend は同じ手順で入れ替えられる
    /// </summary>
    public sealed class AudioBackendManagerTests
    {
        private IMediaCatalog _catalog;
        private ListBackendLogger _logger;
        private FakeAudioPlayer _player;
        private AudioClipLibrary _library;
        private readonly List<AudioClip> _createdClips = new List<AudioClip>();

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _logger = new ListBackendLogger();
            _player = new FakeAudioPlayer();
            _library = new AudioClipLibrary();

            foreach (var item in _catalog.FilterByType(MediaType.Music))
            {
                var clip = ProceduralClipFactory.Create(item, 0.2f);
                _createdClips.Add(clip);
                _library.Register(item, clip);
            }
        }

        [TearDown]
        public void TearDown()
        {
            _library.Clear();
            foreach (var clip in _createdClips)
            {
                if (clip != null) Object.DestroyImmediate(clip);
            }
            _createdClips.Clear();
        }

        private AudioBackend NewAudioBackend() =>
            new AudioBackend("AudioBackend", _player, _library, _logger);

        private MediaQueue QueueWith(params string[] ids)
        {
            var queue = new MediaQueue();
            foreach (var id in ids) queue.Enqueue(_catalog.FindById(id));
            return queue;
        }

        // --- BackendManager を変更せずに使える ---

        [Test]
        public void BackendManager_DrivesAudioBackend_Unchanged()
        {
            var queue = QueueWith("music-001", "music-002");
            var manager = new BackendManager(queue, _logger);
            manager.RegisterBackend(NewAudioBackend());

            Assert.IsTrue(manager.LoadCurrent());
            Assert.AreEqual("music-001", manager.GetCurrent().Id);
            Assert.AreEqual(BackendState.Ready, manager.GetState());

            Assert.IsTrue(manager.Play());
            Assert.AreEqual(BackendState.Playing, manager.GetState());
            Assert.IsTrue(_player.IsPlaying, "AudioSource まで再生指示が届く");

            Assert.IsTrue(manager.Pause());
            Assert.AreEqual(BackendState.Paused, manager.GetState());

            Assert.IsTrue(manager.Resume());
            Assert.AreEqual(BackendState.Playing, manager.GetState());

            Assert.IsTrue(manager.Stop());
            Assert.AreEqual(BackendState.Stopped, manager.GetState());
            Assert.IsFalse(_player.IsPlaying);
        }

        [Test]
        public void QueueToBackendToAudioSource_PathIsConnected()
        {
            // Catalog → Recommendation → Queue → Backend → AudioSource
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
                new System.Random(1));

            var queue = new MediaQueue();
            var queueManager = new QueueManager(queue, engine, _catalog)
            {
                MinimumCount = 2,
                TargetCount = 4,
            };
            queue.Enqueue(_catalog.FindById("music-001"));
            queueManager.EnsureFilled();

            var manager = new BackendManager(queue, _logger);
            manager.RegisterBackend(NewAudioBackend());

            Assert.IsTrue(manager.LoadCurrent());
            Assert.IsTrue(manager.Play());

            Assert.AreSame(_library.Get(queue.Peek().MediaId), _player.Clip,
                "Queue の先頭に対応する AudioClip が AudioSource に渡っている");
        }

        [Test]
        public void Skip_ThroughManager_AdvancesQueueAndPlaysNextClip()
        {
            var queue = QueueWith("music-001", "music-002");
            var manager = new BackendManager(queue, _logger);
            manager.RegisterBackend(NewAudioBackend());

            manager.LoadCurrent();
            manager.Play();
            manager.Skip();

            Assert.AreEqual("music-002", manager.GetCurrent().Id);
            Assert.AreEqual("music-002", queue.Peek().MediaId);
            Assert.AreEqual(BackendState.Playing, manager.GetState());
            Assert.AreSame(_library.Get("music-002"), _player.Clip);
        }

        // --- Ended → 自動送り ---

        [Test]
        public void EndedFromAudioBackend_TriggersAutoAdvance()
        {
            var queue = QueueWith("music-001", "music-002");
            var backend = NewAudioBackend();
            var manager = new BackendManager(queue, _logger) { AutoAdvanceOnEnded = true };
            manager.RegisterBackend(backend);

            manager.LoadCurrent();
            manager.Play();

            // 曲が最後まで再生された
            _player.SimulateFinished();
            backend.Tick();

            Assert.AreEqual("music-002", manager.GetCurrent().Id,
                "Ended 通知を受けて BackendManager が次の曲へ進む");
            Assert.AreEqual("music-002", queue.Peek().MediaId, "Queue も進む");
        }

        [Test]
        public void AutoAdvance_LoadsNextTrackButDoesNotStartItAutomatically()
        {
            // BackendManager.Skip() は「再生中だったか」を現在の状態から判断するが、
            // Ended ハンドラから呼ばれる時点で状態は Ended になっている。
            // そのため自動送りは「次を読み込む」までで、再生開始は呼び出し側の責務。
            // 連続再生は Play() を続けて呼べば成立する(Phase2-2 で扱う)。
            var queue = QueueWith("music-001", "music-002");
            var backend = NewAudioBackend();
            var manager = new BackendManager(queue, _logger) { AutoAdvanceOnEnded = true };
            manager.RegisterBackend(backend);

            manager.LoadCurrent();
            manager.Play();
            _player.SimulateFinished();
            backend.Tick();

            Assert.AreEqual(BackendState.Ready, manager.GetState(),
                "次の曲は読み込まれるが、自動では再生を始めない");

            Assert.IsTrue(manager.Play(), "Play() を呼べば連続再生になる");
            Assert.AreEqual(BackendState.Playing, manager.GetState());
            Assert.AreSame(_library.Get("music-002"), _player.Clip,
                "次の曲の AudioClip が AudioSource に渡る");
        }

        [Test]
        public void EndedFromAudioBackend_DoesNotAdvanceWhenDisabled()
        {
            var queue = QueueWith("music-001", "music-002");
            var backend = NewAudioBackend();
            var manager = new BackendManager(queue, _logger);
            manager.RegisterBackend(backend);

            manager.LoadCurrent();
            manager.Play();
            _player.SimulateFinished();
            backend.Tick();

            Assert.AreEqual("music-001", manager.GetCurrent().Id);
        }

        // --- DummyBackend との差し替え ---

        [Test]
        public void DummyAndAudioBackend_AreInterchangeable()
        {
            // 同じ手順を DummyBackend と AudioBackend の両方で実行し、
            // 外から見える状態遷移が一致することを確認する。
            var dummyStates = RunStandardSequence(new DummyBackend("Dummy", _logger, MediaType.Music));
            var audioStates = RunStandardSequence(NewAudioBackend());

            CollectionAssert.AreEqual(dummyStates, audioStates,
                "DummyBackend と AudioBackend は同じ契約で振る舞う");
        }

        private List<BackendState> RunStandardSequence(IMediaBackend backend)
        {
            var queue = QueueWith("music-001", "music-002");
            var manager = new BackendManager(queue, _logger);
            manager.RegisterBackend(backend);

            var states = new List<BackendState>();
            manager.LoadCurrent(); states.Add(manager.GetState());
            manager.Play(); states.Add(manager.GetState());
            manager.Pause(); states.Add(manager.GetState());
            manager.Resume(); states.Add(manager.GetState());
            manager.Skip(); states.Add(manager.GetState());
            manager.Stop(); states.Add(manager.GetState());
            return states;
        }

        [Test]
        public void BothBackendsRegistered_AudioBackendWinsForMusic()
        {
            // 音源があるものは AudioBackend が、それ以外は DummyBackend が担当する。
            var queue = QueueWith("music-001", "video-001");
            var manager = new BackendManager(queue, _logger);

            var audioBackend = NewAudioBackend();
            var fallback = new DummyBackend("Fallback", _logger);
            manager.RegisterBackend(audioBackend);   // 先に問い合わせられる
            manager.RegisterBackend(fallback);

            Assert.AreSame(audioBackend, manager.SelectBackendFor(_catalog.FindById("music-001")),
                "Music は AudioBackend が担当する");
            Assert.AreSame(fallback, manager.SelectBackendFor(_catalog.FindById("video-001")),
                "Video は AudioBackend が扱えないので次の候補に回る");
        }

        [Test]
        public void SwitchingBackends_StopsTheAudioSource()
        {
            var queue = QueueWith("music-001", "video-001");
            var manager = new BackendManager(queue, _logger);
            manager.RegisterBackend(NewAudioBackend());
            manager.RegisterBackend(new DummyBackend("Fallback", _logger));

            manager.LoadCurrent();
            manager.Play();
            Assert.IsTrue(_player.IsPlaying);

            manager.Skip();   // video-001 → DummyBackend へ切り替わる

            Assert.IsFalse(_player.IsPlaying, "切り替え時に AudioSource は止まる");
            Assert.AreEqual("video-001", manager.GetCurrent().Id);
        }
    }
}
