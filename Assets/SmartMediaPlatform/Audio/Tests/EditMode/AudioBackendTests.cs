using System;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using UnityEngine;

namespace SmartMediaPlatform.Audio.Tests
{
    /// <summary>
    /// AudioBackend の状態遷移・Ended 検出・AudioSource 操作の検証。
    /// 実際に音は鳴らさず、<see cref="FakeAudioPlayer"/> で決定的に確認する。
    /// </summary>
    public sealed class AudioBackendTests
    {
        private IMediaCatalog _catalog;
        private ListBackendLogger _logger;
        private FakeAudioPlayer _player;
        private AudioClipLibrary _library;
        private AudioBackend _backend;

        // 生成した AudioClip は Unity のオブジェクトなので、テストごとに破棄して漏らさない。
        private readonly System.Collections.Generic.List<AudioClip> _createdClips =
            new System.Collections.Generic.List<AudioClip>();

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _logger = new ListBackendLogger();
            _player = new FakeAudioPlayer();
            _library = new AudioClipLibrary();

            // Music 全件に手続き生成のクリップを登録する
            foreach (var item in _catalog.FilterByType(MediaType.Music))
            {
                var clip = ProceduralClipFactory.Create(item, 0.2f);
                _createdClips.Add(clip);
                _library.Register(item, clip);
            }

            _backend = new AudioBackend("AudioBackend", _player, _library, _logger);
        }

        [TearDown]
        public void TearDown()
        {
            _library.Clear();
            foreach (var clip in _createdClips)
            {
                if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
            }
            _createdClips.Clear();
        }

        private MediaItem M(string id) => _catalog.FindById(id);

        // --- 契約(Phase1-5 と同じ)---

        [Test]
        public void InitialState_IsIdle()
        {
            Assert.AreEqual(BackendState.Idle, _backend.GetState());
            Assert.IsNull(_backend.GetCurrent());
        }

        [Test]
        public void OperationsBeforeLoad_AreRejectedWithoutStateChange()
        {
            Assert.IsFalse(_backend.Play());
            Assert.IsFalse(_backend.Pause());
            Assert.IsFalse(_backend.Resume());
            Assert.IsFalse(_backend.Stop());
            Assert.IsFalse(_backend.Skip());
            Assert.AreEqual(BackendState.Idle, _backend.GetState());
            Assert.AreEqual(0, _player.PlayCallCount, "音は鳴らさない");
        }

        [Test]
        public void Load_MovesToReadyAndResolvesClip()
        {
            Assert.IsTrue(_backend.Load(M("music-001")));
            Assert.AreEqual(BackendState.Ready, _backend.GetState());
            Assert.AreEqual("music-001", _backend.GetCurrent().Id);
            Assert.IsNotNull(_backend.GetCurrentClip(), "MediaItem に対応する AudioClip が解決される");
            Assert.AreEqual(0, _player.PlayCallCount, "Load では再生しない");
        }

        [Test]
        public void Load_Null_IsRejected()
        {
            Assert.IsFalse(_backend.Load(null));
            Assert.AreEqual(BackendState.Idle, _backend.GetState());
        }

        // --- Play / Stop(最小実装)---

        [Test]
        public void Play_StartsAudioSourceWithTheMatchingClip()
        {
            _backend.Load(M("music-001"));

            Assert.IsTrue(_backend.Play());
            Assert.AreEqual(BackendState.Playing, _backend.GetState());
            Assert.AreEqual(1, _player.PlayCallCount, "AudioSource の再生が呼ばれる");
            Assert.IsTrue(_player.IsPlaying);
            Assert.AreSame(_library.Get("music-001"), _player.Clip,
                "MediaItem に紐づいた AudioClip が再生される");
        }

        [Test]
        public void Stop_StopsAudioSource()
        {
            _backend.Load(M("music-001"));
            _backend.Play();

            Assert.IsTrue(_backend.Stop());
            Assert.AreEqual(BackendState.Stopped, _backend.GetState());
            Assert.AreEqual(1, _player.StopCallCount);
            Assert.IsFalse(_player.IsPlaying);
        }

        [Test]
        public void Play_FromStopped_RestartsAudio()
        {
            _backend.Load(M("music-001"));
            _backend.Play();
            _backend.Stop();

            Assert.IsTrue(_backend.Play());
            Assert.AreEqual(2, _player.PlayCallCount);
            Assert.AreEqual(BackendState.Playing, _backend.GetState());
        }

        [Test]
        public void Play_WhenAlreadyPlaying_IsRejected()
        {
            _backend.Load(M("music-001"));
            _backend.Play();

            Assert.IsFalse(_backend.Play());
            Assert.AreEqual(1, _player.PlayCallCount, "二重に再生を始めない");
        }

        [Test]
        public void PauseAndResume_DriveTheAudioSource()
        {
            _backend.Load(M("music-001"));
            _backend.Play();

            Assert.IsTrue(_backend.Pause());
            Assert.AreEqual(BackendState.Paused, _backend.GetState());
            Assert.AreEqual(1, _player.PauseCallCount);
            Assert.IsFalse(_player.IsPlaying);

            Assert.IsTrue(_backend.Resume());
            Assert.AreEqual(BackendState.Playing, _backend.GetState());
            Assert.AreEqual(1, _player.UnPauseCallCount);
            Assert.IsTrue(_player.IsPlaying);
        }

        [Test]
        public void Play_WhilePaused_IsRejectedInFavorOfResume()
        {
            _backend.Load(M("music-001"));
            _backend.Play();
            _backend.Pause();

            Assert.IsFalse(_backend.Play());
            Assert.AreEqual(BackendState.Paused, _backend.GetState());
        }

        [Test]
        public void Skip_StopsAudioSource()
        {
            _backend.Load(M("music-001"));
            _backend.Play();

            Assert.IsTrue(_backend.Skip());
            Assert.AreEqual(BackendState.Stopped, _backend.GetState());
            Assert.AreEqual(1, _player.StopCallCount);
        }

        [Test]
        public void Load_WhilePlaying_StopsPreviousTrack()
        {
            _backend.Load(M("music-001"));
            _backend.Play();

            _backend.Load(M("music-002"));

            Assert.AreEqual(1, _player.StopCallCount, "前の曲を止めてから差し替える");
            Assert.AreEqual("music-002", _backend.GetCurrent().Id);
            Assert.AreEqual(BackendState.Ready, _backend.GetState());
        }

        // --- Ended 検出(このフェーズの中心)---

        [Test]
        public void Tick_WhenClipFinished_RaisesEnded()
        {
            var observer = new RecordingObserver();
            _backend.AddObserver(observer);
            _backend.Load(M("music-001"));
            _backend.Play();

            _player.SimulateFinished();   // AudioSource が自然に停止した状態
            bool ended = _backend.Tick();

            Assert.IsTrue(ended);
            Assert.AreEqual(BackendState.Ended, _backend.GetState());
            Assert.AreEqual(BackendEventType.Ended, observer.Events.Last().Type);
            Assert.AreEqual("music-001", observer.Events.Last().Item.Id);
        }

        [Test]
        public void Tick_WhileStillPlaying_DoesNothing()
        {
            _backend.Load(M("music-001"));
            _backend.Play();

            Assert.IsFalse(_backend.Tick());
            Assert.AreEqual(BackendState.Playing, _backend.GetState());
        }

        [Test]
        public void Tick_AfterManualStop_DoesNotRaiseEnded()
        {
            var observer = new RecordingObserver();
            _backend.AddObserver(observer);
            _backend.Load(M("music-001"));
            _backend.Play();
            _backend.Stop();

            Assert.IsFalse(_backend.Tick(), "自分で止めた場合は自然終了ではない");
            Assert.AreEqual(BackendState.Stopped, _backend.GetState());
            Assert.IsFalse(observer.Types().Contains(BackendEventType.Ended));
        }

        [Test]
        public void Tick_WhilePaused_DoesNotRaiseEnded()
        {
            _backend.Load(M("music-001"));
            _backend.Play();
            _backend.Pause();

            Assert.IsFalse(_backend.Tick(),
                "一時停止では AudioSource が止まるが、自然終了ではない");
            Assert.AreEqual(BackendState.Paused, _backend.GetState());
        }

        [Test]
        public void Tick_RaisesEndedOnlyOnce()
        {
            var observer = new RecordingObserver();
            _backend.AddObserver(observer);
            _backend.Load(M("music-001"));
            _backend.Play();
            _player.SimulateFinished();

            _backend.Tick();
            _backend.Tick();
            _backend.Tick();

            Assert.AreEqual(1, observer.Types().Count(t => t == BackendEventType.Ended));
        }

        [Test]
        public void Play_AfterEnded_StartsAgain()
        {
            _backend.Load(M("music-001"));
            _backend.Play();
            _player.SimulateFinished();
            _backend.Tick();

            Assert.IsTrue(_backend.Play());
            Assert.AreEqual(BackendState.Playing, _backend.GetState());
            Assert.AreEqual(2, _player.PlayCallCount);
        }

        // --- CanPlay(バックエンド選択)---

        [Test]
        public void CanPlay_RequiresMusicTypeAndRegisteredClip()
        {
            Assert.IsTrue(_backend.CanPlay(M("music-001")));
            Assert.IsFalse(_backend.CanPlay(M("video-001")), "種別が違えば扱えない");
            Assert.IsFalse(_backend.CanPlay(M("podcast-001")), "種別が違えば扱えない");
            Assert.IsFalse(_backend.CanPlay(null));
        }

        [Test]
        public void CanPlay_IsFalseWhenClipIsMissing()
        {
            var emptyLibrary = new AudioClipLibrary();
            var backend = new AudioBackend("Empty", new FakeAudioPlayer(), emptyLibrary, _logger);

            Assert.IsFalse(backend.CanPlay(M("music-001")),
                "音源が無ければ正直に扱えないと答える(別のバックエンドに任せられる)");
        }

        [Test]
        public void AllowMissingClip_MakesCanPlayIgnoreTheLibrary()
        {
            var emptyLibrary = new AudioClipLibrary();
            var backend = new AudioBackend("Empty", new FakeAudioPlayer(), emptyLibrary, _logger)
            {
                AllowMissingClip = true,
            };

            Assert.IsTrue(backend.CanPlay(M("music-001")));
            Assert.IsTrue(backend.Load(M("music-001")));
            Assert.IsFalse(backend.Play(), "音源が無ければ再生はできない");
        }

        // --- 通知 ---

        [Test]
        public void Observer_ReceivesTheSameSequenceAsDummyBackend()
        {
            var observer = new RecordingObserver();
            _backend.AddObserver(observer);

            _backend.Load(M("music-001"));
            _backend.Play();
            _backend.Pause();
            _backend.Resume();
            _backend.Stop();
            _backend.Skip();

            CollectionAssert.AreEqual(
                new[]
                {
                    BackendEventType.Loaded, BackendEventType.Started, BackendEventType.Paused,
                    BackendEventType.Resumed, BackendEventType.Stopped, BackendEventType.Skipped,
                },
                observer.Types());
        }

        [Test]
        public void RemoveObserver_StopsNotifications()
        {
            var observer = new RecordingObserver();
            _backend.AddObserver(observer);
            _backend.Load(M("music-001"));
            _backend.RemoveObserver(observer);

            int before = observer.Events.Count;
            _backend.Play();

            Assert.AreEqual(before, observer.Events.Count);
        }

        [Test]
        public void Logger_RecordsPlayback()
        {
            _backend.Load(M("music-001"));
            _backend.Play();

            Assert.IsTrue(_logger.Lines.Any(l => l.Contains("Play music-001")));
            Assert.IsTrue(_logger.Lines.Any(l => l.Contains("AudioSource")));
        }

        [Test]
        public void SetLogger_RewiresOutputAfterConstruction()
        {
            // AudioBackendHost.Awake() はロガーが用意される前にバックエンドを組み立てるため、
            // 後から SetLogger でログ出力先を差し替えられる必要がある
            // (でないと Load/Play のログが握りつぶされる)。
            var backend = new AudioBackend("Late", _player, _library); // ロガー未指定 = Null

            backend.Load(M("music-001")); // まだ捨てられる
            Assert.IsEmpty(_logger.Lines);

            backend.SetLogger(_logger);
            backend.Play();

            Assert.IsTrue(_logger.Lines.Any(l => l.Contains("Play music-001")),
                "SetLogger 以降のログは新しい出力先に届く");
        }

        // --- ガード ---

        [Test]
        public void Constructor_RejectsNullDependencies()
        {
            Assert.Throws<ArgumentNullException>(
                () => new AudioBackend("x", null, _library));
            Assert.Throws<ArgumentNullException>(
                () => new AudioBackend("x", new FakeAudioPlayer(), null));
        }
    }
}
