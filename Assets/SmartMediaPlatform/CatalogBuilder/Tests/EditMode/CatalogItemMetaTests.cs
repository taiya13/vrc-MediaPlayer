using NUnit.Framework;
using SmartMediaPlatform.CatalogBuilder;

namespace SmartMediaPlatform.CatalogBuilder.Tests
{
    /// <summary>
    /// <see cref="CatalogItemMeta.ForgetApiData"/> ——「API 由来のものだけ消す」。
    ///
    /// <b>ここが守れないと 3 層に分けた意味がなくなります。</b>
    /// 消しすぎれば作者の入力が飛び、消し足りなければ
    /// 期限切れのデータが残り続けます。
    /// </summary>
    public class CatalogItemMetaTests
    {
        static CatalogItemMeta Filled()
        {
            return new CatalogItemMeta
            {
                Id = "ZRtdQ81jPUQ",
                Source = "YouTube Data API",
                ThumbnailPath = "https://i.ytimg.com/vi/ZRtdQ81jPUQ/hqdefault.jpg",
                PublishedAt = "2019-11-16",
                ApiTitle = "夜に駆ける",
                ApiChannel = "YOASOBI",
                ApiTags = new[] { "J-POP", "ボカロ" },
                ApiFetchedAtUtc = "2026-08-01T00:00:00Z",
            };
        }

        // ───────── 消すもの ─────────

        [Test]
        public void API由来のものは全部消える()
        {
            CatalogItemMeta meta = Filled();
            meta.ForgetApiData();

            Assert.AreEqual("", meta.ApiTitle);
            Assert.AreEqual("", meta.ApiChannel);
            Assert.AreEqual(0, meta.ApiTags.Length);
            Assert.AreEqual("", meta.ApiFetchedAtUtc);
        }

        [Test]
        public void 絵のURLもAPI由来なので消える()
        {
            // Api… の名前が付いていないので見落としやすいが、
            // snippet.thumbnails から取った文字列なので出どころは同じ。
            CatalogItemMeta meta = Filled();
            meta.ForgetApiData();

            Assert.AreEqual("", meta.ThumbnailPath);
        }

        // ───────── 残すもの ─────────

        [Test]
        public void 識別子は残る()
        {
            // 動画 ID は規約上も期限なく持てる。ここを消すと
            // 「取り込み直す」ことすらできなくなる。
            CatalogItemMeta meta = Filled();
            meta.ForgetApiData();

            Assert.AreEqual("ZRtdQ81jPUQ", meta.Id);
        }

        [Test]
        public void どこから入れたかの記録は残る()
        {
            CatalogItemMeta meta = Filled();
            meta.ForgetApiData();

            Assert.AreEqual("YouTube Data API", meta.Source);
        }

        // ───────── 持っているかの判定 ─────────

        [Test]
        public void 消したあとは持っていない扱いになる()
        {
            CatalogItemMeta meta = Filled();
            Assert.IsTrue(meta.HasApiData);

            meta.ForgetApiData();
            Assert.IsFalse(meta.HasApiData);
        }

        [Test]
        public void 何度消しても壊れない()
        {
            CatalogItemMeta meta = Filled();

            meta.ForgetApiData();
            meta.ForgetApiData();

            Assert.IsFalse(meta.HasApiData);
            Assert.IsNotNull(meta.ApiTags, "null にすると次に触ったところで落ちる");
        }
    }
}
