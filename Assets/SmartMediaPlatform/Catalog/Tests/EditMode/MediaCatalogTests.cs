using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Catalog.Data;

namespace SmartMediaPlatform.Catalog.Tests
{
    public sealed class MediaCatalogTests
    {
        private IMediaCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            // seed 固定でランダム系 API も決定的にテストする
            _catalog = new MediaCatalog(new DummyCatalogSource(), new Random(12345));
        }

        // --- ダミーデータ ---

        [Test]
        public void DummyData_Has10OrMoreItems()
        {
            Assert.GreaterOrEqual(_catalog.Count, 10);
        }

        [Test]
        public void DummyData_ContainsNonMusicTypes()
        {
            // 「Music 専用ではない」ことをデータレベルで担保
            Assert.IsNotEmpty(_catalog.FilterByType(MediaType.Video));
            Assert.IsNotEmpty(_catalog.FilterByType(MediaType.Podcast));
        }

        [Test]
        public void DummyData_AllRelatedIdsResolve()
        {
            foreach (var item in _catalog.GetAll())
            {
                foreach (var relatedId in item.RelatedIds)
                {
                    Assert.IsNotNull(_catalog.FindById(relatedId),
                        $"'{item.Id}' の関連 ID '{relatedId}' がカタログに存在しません");
                }
            }
        }

        // --- ID 検索 ---

        [Test]
        public void FindById_ReturnsItem()
        {
            var item = _catalog.FindById("music-003");
            Assert.IsNotNull(item);
            Assert.AreEqual("Paper Lanterns", item.Title);
        }

        [Test]
        public void FindById_UnknownOrEmpty_ReturnsNull()
        {
            Assert.IsNull(_catalog.FindById("no-such-id"));
            Assert.IsNull(_catalog.FindById(""));
            Assert.IsNull(_catalog.FindById(null));
        }

        [Test]
        public void TryFindById_Works()
        {
            Assert.IsTrue(_catalog.TryFindById("music-001", out var found));
            Assert.AreEqual("Neon Skyline", found.Title);
            Assert.IsFalse(_catalog.TryFindById("no-such-id", out var missing));
            Assert.IsNull(missing);
        }

        // --- タイトル / アーティスト検索(部分一致・大文字小文字無視) ---

        [Test]
        public void SearchByTitle_IsPartialAndCaseInsensitive()
        {
            var results = _catalog.SearchByTitle("neon");
            CollectionAssert.AreEquivalent(
                new[] { "music-001", "video-001" },
                results.Select(i => i.Id));
        }

        [Test]
        public void SearchByArtist_IsPartialAndCaseInsensitive()
        {
            var results = _catalog.SearchByArtist("KOHAKU");
            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results.All(i => i.Artist == "Kohaku"));
        }

        [Test]
        public void Search_EmptyQuery_ReturnsEmpty()
        {
            Assert.IsEmpty(_catalog.SearchByTitle(""));
            Assert.IsEmpty(_catalog.SearchByArtist(null));
            Assert.IsEmpty(_catalog.SearchByTag("  "));
            Assert.IsEmpty(_catalog.SearchByGenre(""));
        }

        // --- タグ / ジャンル検索(完全一致・大文字小文字無視) ---

        [Test]
        public void SearchByTag_MatchesExactTag()
        {
            var results = _catalog.SearchByTag("night");
            CollectionAssert.AreEquivalent(
                new[] { "music-001", "music-002", "music-003", "music-006", "video-001" },
                results.Select(i => i.Id));
        }

        [Test]
        public void SearchByTag_DoesNotMatchPartially()
        {
            Assert.IsEmpty(_catalog.SearchByTag("nig"));
        }

        [Test]
        public void SearchByGenre_IsCaseInsensitive()
        {
            var results = _catalog.SearchByGenre("jazz");
            CollectionAssert.AreEquivalent(
                new[] { "music-005", "music-006" },
                results.Select(i => i.Id));
        }

        // --- 種別フィルタ ---

        [Test]
        public void FilterByType_ReturnsOnlyThatType()
        {
            var music = _catalog.FilterByType(MediaType.Music);
            Assert.AreEqual(10, music.Count);
            Assert.IsTrue(music.All(i => i.Type == MediaType.Music));
        }

        // --- ランダム取得 ---

        [Test]
        public void GetRandom_ReturnsItemFromCatalog()
        {
            var item = _catalog.GetRandom();
            Assert.IsNotNull(item);
            Assert.AreSame(item, _catalog.FindById(item.Id));
        }

        [Test]
        public void GetRandomCount_ReturnsDistinctItems()
        {
            var results = _catalog.GetRandom(5);
            Assert.AreEqual(5, results.Count);
            Assert.AreEqual(5, results.Select(i => i.Id).Distinct().Count());
        }

        [Test]
        public void GetRandomCount_ClampsToCatalogSize()
        {
            var results = _catalog.GetRandom(999);
            Assert.AreEqual(_catalog.Count, results.Count);
            Assert.AreEqual(_catalog.Count, results.Select(i => i.Id).Distinct().Count());
        }

        [Test]
        public void GetRandomCount_ZeroOrNegative_ReturnsEmpty()
        {
            Assert.IsEmpty(_catalog.GetRandom(0));
            Assert.IsEmpty(_catalog.GetRandom(-1));
        }

        [Test]
        public void GetRandom_WithSameSeed_IsDeterministic()
        {
            var a = new MediaCatalog(new DummyCatalogSource(), new Random(7)).GetRandom(4);
            var b = new MediaCatalog(new DummyCatalogSource(), new Random(7)).GetRandom(4);
            CollectionAssert.AreEqual(
                a.Select(i => i.Id).ToArray(),
                b.Select(i => i.Id).ToArray());
        }

        // --- 関連アイテム ---

        [Test]
        public void GetRelated_ReturnsDeclaredOrder()
        {
            var results = _catalog.GetRelated("music-001");
            CollectionAssert.AreEqual(
                new[] { "music-002", "music-007" },
                results.Select(i => i.Id).ToArray());
        }

        [Test]
        public void GetRelated_UnknownId_ReturnsEmpty()
        {
            Assert.IsEmpty(_catalog.GetRelated("no-such-id"));
            Assert.IsEmpty(_catalog.GetRelated(null));
        }

        [Test]
        public void GetRelated_NoRelations_ReturnsEmpty()
        {
            Assert.IsEmpty(_catalog.GetRelated("podcast-001"));
        }

        [Test]
        public void GetRelated_SkipsMissingAndSelfReferences()
        {
            var items = new[]
            {
                new MediaItem("a", "A", "X", MediaType.Music,
                    relatedIds: new[] { "a", "missing", "b" }),
                new MediaItem("b", "B", "X", MediaType.Music),
            };
            var catalog = new MediaCatalog(items);

            var results = catalog.GetRelated("a");
            CollectionAssert.AreEqual(new[] { "b" }, results.Select(i => i.Id).ToArray());
        }

        // --- 全件取得 / 構築時バリデーション ---

        [Test]
        public void GetAll_PreservesRegistrationOrder()
        {
            var all = _catalog.GetAll();
            Assert.AreEqual(_catalog.Count, all.Count);
            Assert.AreEqual("music-001", all[0].Id);
        }

        [Test]
        public void Constructor_DuplicateId_Throws()
        {
            var items = new List<MediaItem>
            {
                new MediaItem("dup", "One", "X", MediaType.Music),
                new MediaItem("DUP", "Two", "Y", MediaType.Music),
            };
            Assert.Throws<ArgumentException>(() => new MediaCatalog(items));
        }

        [Test]
        public void MediaItem_RequiresIdAndTitle()
        {
            Assert.Throws<ArgumentException>(() => new MediaItem("", "T", "A", MediaType.Music));
            Assert.Throws<ArgumentException>(() => new MediaItem("id", " ", "A", MediaType.Music));
        }
    }
}
