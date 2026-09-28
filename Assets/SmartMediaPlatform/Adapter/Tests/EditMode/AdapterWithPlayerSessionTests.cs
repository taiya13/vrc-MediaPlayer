using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Adapter.Audio;
using SmartMediaPlatform.Audio;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;
using UnityEngine;

namespace SmartMediaPlatform.Adapter.Tests
{
    /// <summary>
    /// <b>このフェーズの中心的な主張の検証。</b>
    ///
    /// 「VideoBackend を足しても PlayerSession を変更しなくてよい」ことを、
    /// 実際に PlayerSession を使って確かめる。
    /// PlayerSession のコードには一切手を入れていない。
    /// </summary>
    public sealed class AdapterWithPlayerSessionTests
    {
        private IMediaCatalog _catalog;
        private ListBackendLogger _logger;
        private readonly List<AudioClip> _createdClips = new List<AudioClip>();

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _logger = new ListBackendLogger();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var clip in _createdClips)
            {
                if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
            }
            _createdClips.Clear();
        }

        private MediaItem M(string id) => _catalog.FindById(id);

        /// <summary>音源を用意した AudioBackendAdapter を作る。</summary>
        private AudioBackendAdapter NewAudioAdapter()
        {
            var library = new AudioClipLibrary();
            foreach (var item in _catalog.FilterByType(MediaType.Music))
            {
                var clip = ProceduralClipFactory.Create(item, 0.2f);
                _createdClips.Add(clip);
                library.Register(item, clip);
            }

            var backend = new AudioBackend("AudioBackend", new FakePlayer(), library, _logger);
            return new AudioBackendAdapter("AudioAdapter", backend, library, _logger);
        }

        private DummyVideoBackendAdapter NewVideoAdapter()
        {
            return new DummyVideoBackendAdapter("VideoAdapter", _logger);
        }

        /// <summary>PlayerSession 一式を組み立てる(登録するのはアダプタだけ)。</summary>
        private PlayerSession NewSession(params IBackendAdapter[] adapters)
        {
            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, _logger);
            var player = new MediaPlayer(backendManager, _logger);
            var engine = new RecommendationEngine(
                _catalog, RecommendationRule.CreateDefault(), new System.Random(1));

            var session = new PlayerSession("s", player, _catalog, engine, new System.Random(1), _logger);

            // アダプタ登録簿からまとめて渡す。PlayerSession は IMediaBackend しか知らない。
            var adapterManager = new BackendAdapterManager(_logger);
            foreach (var adapter in adapters) adapterManager.Register(adapter);
            adapterManager.AttachAll(session.RegisterBackend);

            return session;
        }

        // ───────── Audio だけ ─────────

        [Test]
        public void Session_PlaysThroughAudioAdapter()
        {
            var session = NewSession(NewAudioAdapter());
            session.SetTracks(new[] { "music-001", "music-002" });

            Assert.IsTrue(session.Play());
            Assert.AreEqual("music-001", session.CurrentMediaId);
            Assert.AreEqual(PlaybackState.Playing, session.PlaybackState);
        }

        // ───────── Video だけ ─────────

        [Test]
        public void Session_PlaysThroughVideoAdapter()
        {
            var session = NewSession(NewVideoAdapter());
            session.SetTracks(new[] { "video-001" });
            session.AutoQueueEnabled = false;

            Assert.IsTrue(session.Play());
            Assert.AreEqual("video-001", session.CurrentMediaId);
            Assert.AreEqual(PlaybackState.Playing, session.PlaybackState);
        }

        // ───────── 両方 = このフェーズの主張 ─────────

        [Test]
        public void AddingVideoAdapter_RequiresNoSessionChanges()
        {
            // Audio と Video の両方を登録する。PlayerSession には手を入れていない。
            var audio = NewAudioAdapter();
            var video = NewVideoAdapter();
            var session = NewSession(audio, video);

            session.AutoQueueEnabled = false;
            session.SetTracks(new[] { "music-001", "video-001", "music-002" });

            session.Play();
            Assert.AreEqual("music-001", session.CurrentMediaId);
            Assert.IsTrue(session.IsPlaying);

            // 種別が変わっても、上位は同じ Next() を呼ぶだけ
            session.Next();
            Assert.AreEqual("video-001", session.CurrentMediaId);
            Assert.IsTrue(session.IsPlaying, "Video でも再生は続く");

            session.Next();
            Assert.AreEqual("music-002", session.CurrentMediaId);
            Assert.IsTrue(session.IsPlaying, "Audio に戻っても続く");
        }

        [Test]
        public void Session_DoesNotSeeAnyDifferenceBetweenAudioAndVideo()
        {
            // まったく同じ操作列を Audio 用と Video 用のセッションに流し、
            // 見える状態遷移が一致することを確かめる。
            var audioStates = RunSequence(NewSession(NewAudioAdapter()), "music-001", "music-002");
            var videoStates = RunSequence(NewSession(NewVideoAdapter()), "video-001", "video-001");

            CollectionAssert.AreEqual(audioStates, videoStates,
                "PlayerSession から見て Audio と Video に違いは無い");
        }

        private PlaybackState[] RunSequence(PlayerSession session, string first, string second)
        {
            session.AutoQueueEnabled = false;
            session.SetTracks(second == first ? new[] { first } : new[] { first, second });

            var states = new List<PlaybackState>();
            session.Play(); states.Add(session.PlaybackState);
            session.Pause(); states.Add(session.PlaybackState);
            session.Resume(); states.Add(session.PlaybackState);
            session.Stop(); states.Add(session.PlaybackState);
            return states.ToArray();
        }

        // ───────── Ended 連携 ─────────

        [Test]
        public void EndedFromVideoAdapter_AdvancesTheSession()
        {
            var video = NewVideoAdapter();
            var audio = NewAudioAdapter();
            var session = NewSession(video, audio);

            session.AutoQueueEnabled = false;
            session.SetTracks(new[] { "video-001", "music-001" });
            session.Play();
            Assert.AreEqual("video-001", session.CurrentMediaId);

            // 動画の再生が終わった
            video.InnerDummy.SimulateEnded();

            Assert.AreEqual("music-001", session.CurrentMediaId,
                "Video の Ended でもセッションは次へ進む");
            Assert.IsTrue(session.IsPlaying);
        }

        [Test]
        public void RepeatOne_WorksWithVideoAdapter()
        {
            var video = NewVideoAdapter();
            var session = NewSession(video);
            session.SetTracks(new[] { "video-001" });
            session.RepeatMode = RepeatMode.One;
            session.Play();

            video.InnerDummy.SimulateEnded();

            Assert.AreEqual("video-001", session.CurrentMediaId, "同じ動画のまま");
            Assert.IsTrue(session.IsPlaying);
        }

        // ───────── シーク能力の違いを吸収 ─────────

        [Test]
        public void Session_CanSeekThroughBothAdapters()
        {
            var audioSession = NewSession(NewAudioAdapter());
            audioSession.SetTracks(new[] { "music-001" });
            audioSession.Play();

            var videoSession = NewSession(NewVideoAdapter());
            videoSession.AutoQueueEnabled = false;
            videoSession.SetTracks(new[] { "video-001" });
            videoSession.Play();

            Assert.IsTrue(audioSession.CanSeek(), "Audio はシークできる");
            Assert.IsTrue(videoSession.CanSeek(), "Video もアダプタが肩代わりしてシークできる");

            Assert.IsTrue(audioSession.Seek(0.5f));
            Assert.IsTrue(videoSession.Seek(0.5f));
            Assert.AreEqual(0.5f, videoSession.GetProgress(), 0.01f);
        }

        // ───────── アダプタ選択 ─────────

        [Test]
        public void BackendManager_PicksTheRightAdapterPerMediaType()
        {
            var audio = NewAudioAdapter();
            var video = NewVideoAdapter();
            var adapters = new BackendAdapterManager(_logger);
            adapters.Register(audio);
            adapters.Register(video);

            Assert.AreSame(audio, adapters.SelectFor(M("music-001")));
            Assert.AreSame(video, adapters.SelectFor(M("video-001")));
            Assert.IsNull(adapters.SelectFor(M("podcast-001")),
                "Podcast 用のアダプタは登録していない");
        }

        // ───────── エラー通知が上位まで届く ─────────

        [Test]
        public void AdapterError_ReachesTheSessionObservers()
        {
            var video = NewVideoAdapter();
            var session = NewSession(video);
            session.AutoQueueEnabled = false;
            session.SetTracks(new[] { "video-001" });
            session.Play();

            var observer = new RecordingObserver();
            video.AddObserver(observer);

            video.ReportError("動画URLの読み込みに失敗しました");

            Assert.IsTrue(video.HasError);
            Assert.AreEqual(BackendEventType.Error, observer.Events.Last().Type);
            Assert.AreEqual("VideoAdapter", observer.Events.Last().BackendName);
        }

        [Test]
        public void AudioAdapter_ReportsMissingClipAsAnError()
        {
            // 音源を用意しないアダプタで読み込むと、理由の分かるエラーになる
            var library = new AudioClipLibrary();
            var backend = new AudioBackend("AudioBackend", new FakePlayer(), library, _logger);
            var adapter = new AudioBackendAdapter("AudioAdapter", backend, library, _logger);

            Assert.IsFalse(adapter.Load(M("music-001")));
            Assert.IsTrue(adapter.HasError);
            StringAssert.Contains("AudioClip", adapter.LastError);
        }

        /// <summary>音を鳴らさない <see cref="IAudioPlayer"/>(テスト用)。</summary>
        private sealed class FakePlayer : IAudioPlayer
        {
            public bool IsPlaying { get; private set; }
            public AudioClip Clip { get; private set; }
            public float Time { get; set; }

            public void Play(AudioClip clip) { Clip = clip; Time = 0f; IsPlaying = true; }
            public void Pause() { IsPlaying = false; }
            public void UnPause() { IsPlaying = true; }
            public void Stop() { IsPlaying = false; Time = 0f; }
        }
    }
}
