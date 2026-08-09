using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Catalog.Assets;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Catalog.Store;
using UnityEngine;

namespace SmartMediaPlatform.Catalog.Tests
{
    /// <summary>
    /// Phase4-2: <see cref="MediaCatalogAsset"/>(Catalog Builder の受け皿)の検証。
    ///
    /// いちばん大事なのは<b>「カタログの作り方が変わっても、上のコードが変わらない」</b>ことです。
    /// アセットから作った <see cref="CatalogStore"/> が、
    /// 手書きのソースから作ったものと同じように振る舞うかを確かめます。
    /// </summary>
    public sealed class MediaCatalogAssetTests
    {
        private MediaCatalogAsset _asset;

        [SetUp]
        public void SetUp()
        {
            _asset = ScriptableObject.CreateInstance<MediaCatalogAsset>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_asset != null) Object.DestroyImmediate(_asset);
        }

        // ───────── 契約 ─────────

        [Test]
        public void Asset_IsACatalogSource()
        {
            Assert.IsInstanceOf<IMediaCatalogSource>(_asset,
                "Phase1-1 の拡張点をそのまま実装しているだけ");
        }

        [Test]
        public void EmptyAsset_LoadsNothing()
        {
            Assert.AreEqual(0, _asset.Count);
            Assert.AreEqual(0, _asset.LoadItems().Count);
        }

        // ───────── 読み書き ─────────

        [Test]
        public void SetEntries_BecomesLoadableItems()
        {
            _asset.SetEntries(new[]
            {
                new MediaCatalogAsset.Entry
                {
                    Id = "video-900",
                    Title = "Generated Clip",
                    Artist = "Builder",
                    Type = MediaType.Video,
                    Genre = "Test",
                    Tags = new[] { "generated" },
                    Url = "https://example.com/generated",
                    DurationSeconds = 120,
                    RelatedIds = new[] { "video-901" },
                },
            }, "unit test");

            var items = _asset.LoadItems();

            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("video-900", items[0].Id);
            Assert.AreEqual("Generated Clip", items[0].Title);
            Assert.AreEqual(MediaType.Video, items[0].Type);
            Assert.AreEqual(120, items[0].DurationSeconds);
            CollectionAssert.AreEqual(new[] { "video-901" }, items[0].RelatedIds);
            Assert.AreEqual("unit test", _asset.SourceDescription);
            Assert.IsNotEmpty(_asset.GeneratedAt);
        }

        [Test]
        public void BrokenRows_AreDroppedInsteadOfThrowing()
        {
            _asset.SetEntries(new[]
            {
                new MediaCatalogAsset.Entry { Id = "", Title = "ID が無い" },
                new MediaCatalogAsset.Entry { Id = "video-900", Title = "ちゃんとしている" },
                new MediaCatalogAsset.Entry { Id = "video-900", Title = "ID が重複" },
                null,
            });

            var items = _asset.LoadItems();

            Assert.AreEqual(1, items.Count, "生成物が壊れていても読み込みは止まらない");
            Assert.AreEqual("video-900", items[0].Id);
        }

        [Test]
        public void MissingTitle_FallsBackToTheId()
        {
            _asset.SetEntries(new[]
            {
                new MediaCatalogAsset.Entry { Id = "video-900", Title = "" },
            });

            Assert.AreEqual("video-900", _asset.LoadItems()[0].Title,
                "MediaItem はタイトル必須なので ID で代用する");
        }

        // ───────── 差し替えても上が変わらない ─────────

        [Test]
        public void ImportFrom_CopiesAnExistingSource()
        {
            var source = new DummyCatalogSource();

            int copied = _asset.ImportFrom(source);

            Assert.AreEqual(source.LoadItems().Count, copied);
            Assert.AreEqual(copied, _asset.Count);
            StringAssert.Contains("DummyCatalogSource", _asset.SourceDescription);
        }

        [Test]
        public void AnAssetBackedStore_BehavesLikeACodeBackedOne()
        {
            var source = new DummyCatalogSource();
            _asset.ImportFrom(source);

            var fromCode = new CatalogStore(new MediaCatalog(source));
            var fromAsset = new CatalogStore(new MediaCatalog(_asset));

            // ★ ここが Phase4-2 の要点:
            //   カタログの作り方が変わっても、窓口から見える形は同じ。
            Assert.AreEqual(fromCode.Count, fromAsset.Count);
            CollectionAssert.AreEqual(fromCode.GetAllIds().ToArray(), fromAsset.GetAllIds().ToArray());

            foreach (string id in fromCode.GetAllIds())
            {
                var a = fromCode.GetDisplayMeta(id);
                var b = fromAsset.GetDisplayMeta(id);

                Assert.AreEqual(a.Title, b.Title);
                Assert.AreEqual(a.Artist, b.Artist);
                Assert.AreEqual(a.Genre, b.Genre);
                Assert.AreEqual(a.Type, b.Type);
                Assert.AreEqual(a.DurationSeconds, b.DurationSeconds);
                CollectionAssert.AreEqual(a.Tags, b.Tags);
            }
        }

        [Test]
        public void AnAssetBackedStore_StillHidesTheUrl()
        {
            _asset.ImportFrom(new DummyCatalogSource());
            var store = new CatalogStore(new MediaCatalog(_asset));

            var meta = store.GetDisplayMeta(store.GetAllIds()[0]);

            Assert.IsNotNull(meta);
            Assert.IsNull(meta.GetType().GetProperty("Url"),
                "アセット経由でも UI へ URL は出ない");
        }

        [Test]
        public void RelatedIdsSurviveTheRoundTrip()
        {
            _asset.ImportFrom(new DummyCatalogSource());
            var catalog = new MediaCatalog(_asset);
            var provider = new CatalogRelatedMediaProvider(catalog);

            // DummyCatalogSource の music-001 は関連を持っている
            Assert.Greater(provider.GetRelatedIds("music-001").Count, 0,
                "Catalog Builder が埋める RelatedIds がアセットを通っても残る");
        }
    }
}
