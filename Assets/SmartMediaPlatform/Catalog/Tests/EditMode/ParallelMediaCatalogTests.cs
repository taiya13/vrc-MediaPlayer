using System;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Catalog.Parallel;

namespace SmartMediaPlatform.Catalog.Tests
{
    /// <summary>
    /// UdonMediaCatalog が移植元とする ParallelMediaCatalog を検証する。
    /// 大半のケースは「Phase1-1 の MediaCatalog と結果が(順序込みで)一致すること」を
    /// 直接突き合わせて担保する。これにより並列配列への変換が意味論を保つことを保証する。
    /// </summary>
    public sealed class ParallelMediaCatalogTests
    {
        private IMediaCatalog _reference;
        private ParallelMediaCatalog _parallel;

        [SetUp]
        public void SetUp()
        {
            var items = new DummyCatalogSource().LoadItems();
            _reference = new MediaCatalog(items, new Random(1));
            _parallel = new ParallelMediaCatalog(CatalogFlattener.Flatten(items), new Random(1));
        }

        private string[] Ids(int[] indices)
        {
            return indices.Select(i => _parallel.GetId(i)).ToArray();
        }

        private static string[] RefIds(System.Collections.Generic.IReadOnlyList<MediaItem> items)
        {
            return items.Select(i => i.Id).ToArray();
        }

        [Test]
        public void Count_MatchesReference()
        {
            Assert.AreEqual(_reference.Count, _parallel.Count);
        }

        [Test]
        public void Flatten_PreservesNonMusicTypes()
        {
            Assert.IsNotEmpty(_parallel.FilterByType(MediaTypeCode.Video));
            Assert.IsNotEmpty(_parallel.FilterByType(MediaTypeCode.Podcast));
        }

        [Test]
        public void FindIndexById_ResolvesEveryItem()
        {
            foreach (var item in _reference.GetAll())
            {
                int idx = _parallel.FindIndexById(item.Id);
                Assert.GreaterOrEqual(idx, 0);
                Assert.AreEqual(item.Id, _parallel.GetId(idx));
            }
        }

        [Test]
        public void FindIndexById_IsCaseInsensitive()
        {
            Assert.AreEqual(_parallel.FindIndexById("MUSIC-001"), _parallel.FindIndexById("music-001"));
        }

        [Test]
        public void FindIndexById_UnknownOrNull_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, _parallel.FindIndexById("no-such-id"));
            Assert.AreEqual(-1, _parallel.FindIndexById(null));
            Assert.AreEqual(-1, _parallel.FindIndexById(""));
        }

        [Test]
        public void GetAllIndices_MatchesReferenceOrder()
        {
            CollectionAssert.AreEqual(RefIds(_reference.GetAll()), Ids(_parallel.GetAllIndices()));
        }

        [TestCase("neon")]
        [TestCase("NEON")]
        [TestCase("half")]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("zzz")]
        public void SearchByTitle_MatchesReference(string query)
        {
            CollectionAssert.AreEqual(
                RefIds(_reference.SearchByTitle(query)),
                Ids(_parallel.SearchByTitle(query)));
        }

        [TestCase("aurora")]
        [TestCase("KOHAKU")]
        [TestCase("orbit")]
        [TestCase("")]
        public void SearchByArtist_MatchesReference(string query)
        {
            CollectionAssert.AreEqual(
                RefIds(_reference.SearchByArtist(query)),
                Ids(_parallel.SearchByArtist(query)));
        }

        [TestCase("night")]
        [TestCase("NIGHT")]
        [TestCase("chill")]
        [TestCase("nig")]
        [TestCase("")]
        [TestCase("dance")]
        public void SearchByTag_MatchesReference(string query)
        {
            CollectionAssert.AreEqual(
                RefIds(_reference.SearchByTag(query)),
                Ids(_parallel.SearchByTag(query)));
        }

        [TestCase("jazz")]
        [TestCase("Jazz")]
        [TestCase("Ambient")]
        [TestCase("electronic")]
        [TestCase("")]
        public void SearchByGenre_MatchesReference(string query)
        {
            CollectionAssert.AreEqual(
                RefIds(_reference.SearchByGenre(query)),
                Ids(_parallel.SearchByGenre(query)));
        }

        [TestCase(MediaTypeCode.Music)]
        [TestCase(MediaTypeCode.Video)]
        [TestCase(MediaTypeCode.Podcast)]
        [TestCase(MediaTypeCode.Live)]
        public void FilterByType_MatchesReference(int type)
        {
            CollectionAssert.AreEqual(
                RefIds(_reference.FilterByType((MediaType)type)),
                Ids(_parallel.FilterByType(type)));
        }

        [Test]
        public void GetRelated_MatchesReferenceForEveryItem()
        {
            foreach (var item in _reference.GetAll())
            {
                CollectionAssert.AreEqual(
                    RefIds(_reference.GetRelated(item.Id)),
                    Ids(_parallel.GetRelatedIndices(item.Id)),
                    $"related mismatch for {item.Id}");
            }
        }

        [Test]
        public void GetRelated_UnknownId_ReturnsEmpty()
        {
            Assert.IsEmpty(_parallel.GetRelatedIndices("no-such-id"));
            Assert.IsEmpty(_parallel.GetRelatedIndices(null));
        }

        [Test]
        public void GetRelated_SkipsSelfAndMissing()
        {
            var custom = new[]
            {
                new MediaItem("a", "A", "X", MediaType.Music, relatedIds: new[] { "a", "missing", "b" }),
                new MediaItem("b", "B", "X", MediaType.Music),
            };
            var p = new ParallelMediaCatalog(CatalogFlattener.Flatten(custom));
            CollectionAssert.AreEqual(new[] { "b" }, p.GetRelatedIndices("a").Select(i => p.GetId(i)).ToArray());
        }

        [Test]
        public void FieldAccessors_ReturnFlattenedValues()
        {
            int v = _parallel.FindIndexById("video-001");
            Assert.AreEqual("Neon Skyline (Official Video)", _parallel.GetTitle(v));
            Assert.AreEqual("Aurora Drive", _parallel.GetArtist(v));
            Assert.AreEqual(MediaTypeCode.Video, _parallel.GetMediaType(v));
            CollectionAssert.AreEqual(new[] { "mv", "night", "retro" }, _parallel.GetTags(v));

            int p = _parallel.FindIndexById("podcast-001");
            Assert.AreEqual(1820, _parallel.GetDurationSeconds(p));
            Assert.AreEqual("https://example.com/media/worlds-waveforms-12", _parallel.GetUrl(p));
        }

        [Test]
        public void GetRandomIndex_IsWithinRange()
        {
            int idx = _parallel.GetRandomIndex();
            Assert.GreaterOrEqual(idx, 0);
            Assert.Less(idx, _parallel.Count);
        }

        [Test]
        public void GetRandomIndices_AreDistinctAndClamped()
        {
            var five = _parallel.GetRandomIndices(5);
            Assert.AreEqual(5, five.Length);
            Assert.AreEqual(5, five.Distinct().Count());

            var all = _parallel.GetRandomIndices(999);
            Assert.AreEqual(_parallel.Count, all.Length);
            Assert.AreEqual(_parallel.Count, all.Distinct().Count());

            Assert.IsEmpty(_parallel.GetRandomIndices(0));
            Assert.IsEmpty(_parallel.GetRandomIndices(-1));
        }

        [Test]
        public void GetRandomIndices_WithSameSeed_IsDeterministic()
        {
            var data = CatalogFlattener.Flatten(new DummyCatalogSource().LoadItems());
            var a = new ParallelMediaCatalog(data, new Random(7)).GetRandomIndices(4);
            var b = new ParallelMediaCatalog(data, new Random(7)).GetRandomIndices(4);
            CollectionAssert.AreEqual(a, b);
        }
    }
}
