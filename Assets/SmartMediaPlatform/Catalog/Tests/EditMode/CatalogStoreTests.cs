using System;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Catalog.Store;

namespace SmartMediaPlatform.Catalog.Tests
{
    /// <summary>
    /// Phase4-2: <see cref="CatalogStore"/> が「唯一のデータ取得窓口」として
    /// 正しく振る舞うことの検証。
    ///
    /// いちばん大事なのは<b>「外へ出るものに URL が混ざらない」</b>ことです。
    /// </summary>
    public sealed class CatalogStoreTests
    {
        private IMediaCatalog _catalog;
        private CatalogStore _store;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _store = new CatalogStore(_catalog);
        }

        // ───────── 前提 ─────────

        [Test]
        public void Constructor_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new CatalogStore(null));
        }

        [Test]
        public void Store_IsTheContract()
        {
            Assert.IsInstanceOf<ICatalogStore>(_store);
        }

        [Test]
        public void Count_MatchesTheCatalog()
        {
            Assert.AreEqual(_catalog.Count, _store.Count);
        }

        // ───────── DisplayMeta ─────────

        [Test]
        public void GetDisplayMeta_CopiesTheDisplayFields()
        {
            var item = _catalog.GetAll()[0];
            var meta = _store.GetDisplayMeta(item.Id);

            Assert.IsNotNull(meta);
            Assert.AreEqual(item.Id, meta.MediaId);
            Assert.AreEqual(item.Title, meta.Title);
            Assert.AreEqual(item.Artist, meta.Artist);
            Assert.AreEqual(item.Genre, meta.Genre);
            Assert.AreEqual(item.Type, meta.Type);
            Assert.AreEqual(item.DurationSeconds, meta.DurationSeconds);
            CollectionAssert.AreEqual(item.Tags, meta.Tags);
        }

        [Test]
        public void DisplayMeta_HasNoUrlAtAll()
        {
            var type = typeof(DisplayMeta);

            Assert.IsNull(type.GetProperty("Url"), "表示用に URL の口があってはいけない");
            Assert.IsNull(type.GetField("Url"));

            foreach (var property in type.GetProperties())
            {
                StringAssert.DoesNotContain("url", property.Name.ToLowerInvariant(),
                    $"{property.Name} が URL を運んでいる");
            }
        }

        [Test]
        public void GetDisplayMeta_ReturnsNullForUnknownIds()
        {
            Assert.IsNull(_store.GetDisplayMeta("does-not-exist"));
            Assert.IsNull(_store.GetDisplayMeta(null));
            Assert.IsNull(_store.GetDisplayMeta("  "));
        }

        [Test]
        public void GetDisplayMeta_ReturnsTheSameInstanceEveryTime()
        {
            var first = _store.GetDisplayMeta("music-001");
            var second = _store.GetDisplayMeta("music-001");

            Assert.AreSame(first, second, "UI が毎フレーム呼んでも割り当てが増えない");
        }

        [Test]
        public void GetDisplayMeta_IsCaseInsensitive()
        {
            Assert.IsNotNull(_store.GetDisplayMeta("MUSIC-001"));
        }

        [Test]
        public void TryGetDisplayMeta_ReportsWhetherItExists()
        {
            Assert.IsTrue(_store.TryGetDisplayMeta("music-001", out var found));
            Assert.IsNotNull(found);

            Assert.IsFalse(_store.TryGetDisplayMeta("nope", out var missing));
            Assert.IsNull(missing);
        }

        // ───────── まとめて変換 ─────────

        [Test]
        public void GetDisplayMetas_KeepsTheGivenOrder()
        {
            var ids = new[] { "music-003", "music-001", "music-002" };

            var metas = _store.GetDisplayMetas(ids);

            CollectionAssert.AreEqual(ids, metas.Select(x => x.MediaId).ToArray());
        }

        [Test]
        public void GetDisplayMetas_SilentlyDropsUnknownIds()
        {
            var ids = new[] { "music-001", "server-sent-something-old", "music-002" };

            var metas = _store.GetDisplayMetas(ids);

            Assert.AreEqual(2, metas.Count,
                "サーバーが古い ID を返しても、残りはそのまま並べられる");
            CollectionAssert.AreEqual(
                new[] { "music-001", "music-002" }, metas.Select(x => x.MediaId).ToArray());
        }

        [Test]
        public void GetDisplayMetas_HandlesNullAndEmpty()
        {
            Assert.AreEqual(0, _store.GetDisplayMetas(null).Count);
            Assert.AreEqual(0, _store.GetDisplayMetas(new string[0]).Count);
        }

        // ───────── PlayableRef ─────────

        [Test]
        public void GetPlayableRef_CarriesOnlyTheIdAndType()
        {
            var item = _catalog.GetAll()[0];
            var playable = _store.GetPlayableRef(item.Id);

            Assert.IsTrue(playable.IsValid);
            Assert.AreEqual(item.Id, playable.MediaId);
            Assert.AreEqual(item.Type, playable.Type);
        }

        [Test]
        public void PlayableRef_HasNoUrlAtAll()
        {
            var type = typeof(PlayableRef);

            Assert.IsNull(type.GetProperty("Url"));
            foreach (var property in type.GetProperties())
            {
                StringAssert.DoesNotContain("url", property.Name.ToLowerInvariant(),
                    $"{property.Name} が URL を運んでいる");
            }
        }

        [Test]
        public void GetPlayableRef_IsNoneForUnknownIds()
        {
            Assert.IsFalse(_store.GetPlayableRef("does-not-exist").IsValid);
            Assert.IsFalse(_store.GetPlayableRef(null).IsValid);
            Assert.AreEqual(PlayableRef.None, _store.GetPlayableRef("nope"));
        }

        [Test]
        public void PlayableRef_ComparesById()
        {
            var a = _store.GetPlayableRef("music-001");
            var b = _store.GetPlayableRef("MUSIC-001");

            Assert.AreEqual(a, b);
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.AreNotEqual(a, _store.GetPlayableRef("music-002"));
        }

        [Test]
        public void PlayableRef_NoneIsTheDefault()
        {
            PlayableRef none = default(PlayableRef);

            Assert.IsFalse(none.IsValid);
            Assert.IsNull(none.MediaId);
            Assert.AreEqual(PlayableRef.None, none);
        }

        // ───────── ID の並び ─────────

        [Test]
        public void GetAllIds_MatchesTheCatalogOrder()
        {
            CollectionAssert.AreEqual(
                _catalog.GetAll().Select(x => x.Id).ToArray(), _store.GetAllIds().ToArray());
        }

        [Test]
        public void GetIdsByType_FiltersByType()
        {
            var videoIds = _store.GetIdsByType(MediaType.Video);

            Assert.Greater(videoIds.Count, 0);
            foreach (string id in videoIds)
            {
                Assert.AreEqual(MediaType.Video, _store.GetDisplayMeta(id).Type);
            }
        }

        [Test]
        public void Contains_AnswersWithoutBuildingAnything()
        {
            Assert.IsTrue(_store.Contains("music-001"));
            Assert.IsFalse(_store.Contains("does-not-exist"));
            Assert.IsFalse(_store.Contains(null));
        }

        // ───────── 内部構造を漏らさない ─────────

        [Test]
        public void TheContract_NeverReturnsAMediaItem()
        {
            foreach (var method in typeof(ICatalogStore).GetMethods())
            {
                Assert.AreNotEqual(typeof(MediaItem), method.ReturnType,
                    $"{method.Name} が MediaItem を返している(内部構造が漏れる)");
            }

            foreach (var property in typeof(ICatalogStore).GetProperties())
            {
                Assert.AreNotEqual(typeof(MediaItem), property.PropertyType,
                    $"{property.Name} が MediaItem を返している");
            }
        }

        [Test]
        public void TheContract_NeverReturnsTheCatalogItself()
        {
            foreach (var property in typeof(ICatalogStore).GetProperties())
            {
                Assert.AreNotEqual(typeof(IMediaCatalog), property.PropertyType,
                    "カタログそのものを外へ出さない");
            }
        }

        // ───────── 読み直し ─────────

        [Test]
        public void Invalidate_DropsTheCachedConversions()
        {
            var before = _store.GetDisplayMeta("music-001");

            _store.Invalidate();

            var after = _store.GetDisplayMeta("music-001");
            Assert.AreNotSame(before, after);
            Assert.AreEqual(before.Title, after.Title);
        }
    }
}
