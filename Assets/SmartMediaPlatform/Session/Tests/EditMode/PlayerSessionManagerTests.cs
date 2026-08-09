using System;
using NUnit.Framework;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;

namespace SmartMediaPlatform.Session.Tests
{
    /// <summary>PlayerSessionManager の作成・削除・切り替えの検証。</summary>
    public sealed class PlayerSessionManagerTests
    {
        private IMediaCatalog _catalog;
        private RecommendationEngine _engine;
        private PlayerSessionManager _manager;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new Random(1));
            _engine = new RecommendationEngine(_catalog, RecommendationRule.CreateDefault());
            _manager = new PlayerSessionManager();
        }

        /// <summary>セッション 1 つぶんの再生系一式を組み立てる。</summary>
        private (MediaPlayer, SessionTestBackend) NewPlayer()
        {
            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, null);
            var player = new MediaPlayer(backendManager, null);
            var backend = new SessionTestBackend();
            return (player, backend);
        }

        private PlayerSession CreateSession(string id = null)
        {
            var (player, backend) = NewPlayer();
            var session = _manager.Create(player, _catalog, _engine, id);
            session?.RegisterBackend(backend);
            return session;
        }

        // ───────── 作成 ─────────

        [Test]
        public void Create_AddsSessionAndMakesItActive()
        {
            var session = CreateSession();

            Assert.IsNotNull(session);
            Assert.AreEqual(1, _manager.Count);
            Assert.AreSame(session, _manager.Active, "最初のセッションが有効になる");
        }

        [Test]
        public void Create_GeneratesUniqueIds()
        {
            var a = CreateSession();
            var b = CreateSession();

            Assert.AreNotEqual(a.Id, b.Id);
            Assert.AreEqual(2, _manager.Count);
        }

        [Test]
        public void Create_WithExplicitId()
        {
            var session = CreateSession("room-1");

            Assert.AreEqual("room-1", session.Id);
            Assert.AreSame(session, _manager.Get("room-1"));
        }

        [Test]
        public void Create_WithDuplicateId_Fails()
        {
            CreateSession("same");

            Assert.IsNull(CreateSession("same"));
            Assert.AreEqual(1, _manager.Count);
        }

        [Test]
        public void Create_DoesNotStealActiveFromTheFirstSession()
        {
            var first = CreateSession();
            CreateSession();

            Assert.AreSame(first, _manager.Active);
        }

        // ───────── 取得 ─────────

        [Test]
        public void Get_IsCaseInsensitive()
        {
            CreateSession("Room-1");

            Assert.IsNotNull(_manager.Get("room-1"));
            Assert.IsTrue(_manager.Contains("ROOM-1"));
            Assert.IsNull(_manager.Get("no-such"));
            Assert.IsNull(_manager.Get(null));
        }

        [Test]
        public void Sessions_IsLiveReadOnlyView()
        {
            var view = _manager.Sessions;
            Assert.AreEqual(0, view.Count);

            CreateSession();
            Assert.AreEqual(1, view.Count);
        }

        // ───────── 削除 ─────────

        [Test]
        public void Delete_RemovesSession()
        {
            CreateSession("a");

            Assert.IsTrue(_manager.Delete("a"));
            Assert.AreEqual(0, _manager.Count);
            Assert.IsFalse(_manager.Delete("a"));
            Assert.IsNull(_manager.Active);
        }

        [Test]
        public void Delete_ActiveSession_PromotesAnother()
        {
            var first = CreateSession("a");
            var second = CreateSession("b");
            Assert.AreSame(first, _manager.Active);

            _manager.Delete("a");

            Assert.AreSame(second, _manager.Active, "残ったセッションが有効になる");
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            CreateSession();
            CreateSession();

            _manager.Clear();

            Assert.AreEqual(0, _manager.Count);
            Assert.IsNull(_manager.Active);
        }

        // ───────── 切り替え ─────────

        [Test]
        public void SetActive_SwitchesSession()
        {
            CreateSession("a");
            var second = CreateSession("b");

            Assert.IsTrue(_manager.SetActive("b"));
            Assert.AreSame(second, _manager.Active);
        }

        [Test]
        public void SetActive_StopsThePreviouslyPlayingSession()
        {
            var first = CreateSession("a");
            CreateSession("b");

            first.SetTracks(new[] { "music-001" });
            first.Play();
            Assert.IsTrue(first.IsPlaying);

            _manager.SetActive("b");

            Assert.IsFalse(first.IsPlaying, "切り替え前のセッションは停止し、音が重ならない");
        }

        [Test]
        public void SetActive_UnknownId_Fails()
        {
            CreateSession("a");

            Assert.IsFalse(_manager.SetActive("no-such"));
            Assert.AreEqual("a", _manager.Active.Id);
        }

        [Test]
        public void SetActive_SameSession_IsNoOp()
        {
            var session = CreateSession("a");
            session.SetTracks(new[] { "music-001" });
            session.Play();

            Assert.IsTrue(_manager.SetActive("a"));
            Assert.IsTrue(session.IsPlaying, "同じセッションなら止めない");
        }

        // ───────── 登録 ─────────

        [Test]
        public void Register_AddsExistingSession()
        {
            var (player, backend) = NewPlayer();
            var session = new PlayerSession("manual", player, _catalog, _engine);
            session.RegisterBackend(backend);

            Assert.IsTrue(_manager.Register(session));
            Assert.AreSame(session, _manager.Active);
            Assert.IsFalse(_manager.Register(session), "二重登録はしない");
        }

        [Test]
        public void Register_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => _manager.Register(null));
        }

        // ───────── セッションが独立していること ─────────

        [Test]
        public void Sessions_KeepIndependentState()
        {
            var a = CreateSession("a");
            var b = CreateSession("b");

            a.SetTracks(new[] { "music-001", "music-002" });
            b.SetTracks(new[] { "music-009" });
            a.RepeatMode = RepeatMode.All;

            Assert.AreEqual(RepeatMode.All, a.RepeatMode);
            Assert.AreEqual(RepeatMode.Off, b.RepeatMode, "設定は混ざらない");
            Assert.AreEqual(2, a.Tracks.Count);
            Assert.AreEqual(1, b.Tracks.Count);
        }
    }
}
