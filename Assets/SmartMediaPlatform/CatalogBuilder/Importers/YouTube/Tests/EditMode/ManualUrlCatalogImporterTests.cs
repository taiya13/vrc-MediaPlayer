using NUnit.Framework;
using SmartMediaPlatform.CatalogBuilder;
using SmartMediaPlatform.CatalogBuilder.YouTube;

namespace SmartMediaPlatform.CatalogBuilder.YouTube.Tests
{
    /// <summary>
    /// <see cref="ManualUrlCatalogImporter"/> —— API を呼ばない取り込み。
    ///
    /// <b>いちばん大事なのは「API 由来のデータを 1 つも作らないこと」</b>です。
    /// ここが崩れると、この経路を分けた意味がなくなります。
    /// </summary>
    public class ManualUrlCatalogImporterTests
    {
        static ManualUrlCatalogImporter New()
        {
            return new ManualUrlCatalogImporter();
        }

        // ───────── 読み取り ─────────

        [Test]
        public void 動画のURLから動画IDを取り出せる()
        {
            CatalogImportResult result = New().Import(
                "https://www.youtube.com/watch?v=ZRtdQ81jPUQ");

            Assert.IsTrue(result.Ok, result.Message);
            Assert.AreEqual(1, result.Items.Count);
            Assert.AreEqual("ZRtdQ81jPUQ", result.Items[0].Id);
        }

        [Test]
        public void 一行に一つずつ何本でも読める()
        {
            CatalogImportResult result = New().Import(
                "https://www.youtube.com/watch?v=aaaaaaaaaaa\n"
                + "https://youtu.be/bbbbbbbbbbb\n"
                + "https://www.youtube.com/watch?v=ccccccccccc");

            Assert.IsTrue(result.Ok, result.Message);
            Assert.AreEqual(3, result.Items.Count);
        }

        [Test]
        public void 同じ動画を二度貼っても一件になる()
        {
            CatalogImportResult result = New().Import(
                "https://www.youtube.com/watch?v=aaaaaaaaaaa\n"
                + "https://youtu.be/aaaaaaaaaaa");

            Assert.IsTrue(result.Ok, result.Message);
            Assert.AreEqual(1, result.Items.Count);
        }

        [Test]
        public void 空行と余分な空白は飛ばす()
        {
            CatalogImportResult result = New().Import(
                "\n  https://www.youtube.com/watch?v=aaaaaaaaaaa  \n\n");

            Assert.IsTrue(result.Ok, result.Message);
            Assert.AreEqual(1, result.Items.Count);
        }

        // ───────── 受け付けないもの ─────────

        [Test]
        public void 再生リストやチャンネルは受け付けない()
        {
            // 展開するには API が要るので、ここで受けると
            // 「API 不使用」の約束が守れなくなる。
            CatalogImportResult result = New().Import(
                "https://www.youtube.com/playlist?list=PLxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");

            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void 動画のURLが一つも無ければ断る()
        {
            Assert.IsFalse(New().Import("").Ok);
            Assert.IsFalse(New().Import("こんにちは").Ok);
            Assert.IsFalse(New().Import("https://example.com/movie").Ok);
        }

        [Test]
        public void 読めない行が混ざっていても読めた行は取り込む()
        {
            CatalogImportResult result = New().Import(
                "https://www.youtube.com/watch?v=aaaaaaaaaaa\n"
                + "これはURLではない");

            Assert.IsTrue(result.Ok, result.Message);
            Assert.AreEqual(1, result.Items.Count);
            StringAssert.Contains("読めませんでした", result.Message);
        }

        // ───────── ここが本題 ─────────

        [Test]
        public void API由来のデータを一つも作らない()
        {
            CatalogImportResult result = New().Import(
                "https://www.youtube.com/watch?v=ZRtdQ81jPUQ");

            CatalogDraftItem item = result.Items[0];

            Assert.IsFalse(item.HasApiData, "API 由来の印が付いてはいけない");
            Assert.AreEqual("", item.ApiFetchedAtUtc);
            Assert.AreEqual("", item.ApiTitle);
            Assert.AreEqual("", item.ApiChannel);
            Assert.AreEqual(0, item.ApiTags.Length);
        }

        [Test]
        public void 曲名もアーティストも空のまま渡す()
        {
            // 埋めてしまうと「どこから来た文字か」が分からなくなる。
            CatalogImportResult result = New().Import(
                "https://www.youtube.com/watch?v=ZRtdQ81jPUQ");

            CatalogDraftItem item = result.Items[0];

            Assert.AreEqual("", item.Title);
            Assert.AreEqual("", item.Artist);
            Assert.AreEqual("", item.Genre);
        }

        [Test]
        public void 再生できるURLを組み立てる()
        {
            CatalogImportResult result = New().Import("https://youtu.be/ZRtdQ81jPUQ");

            Assert.AreEqual(
                "https://www.youtube.com/watch?v=ZRtdQ81jPUQ", result.Items[0].Url);
        }

        [Test]
        public void 手入力の印が付く()
        {
            CatalogImportResult result = New().Import(
                "https://www.youtube.com/watch?v=ZRtdQ81jPUQ");

            Assert.AreEqual(
                ManualUrlCatalogImporter.SourceName, result.Items[0].Source);
        }

        // ───────── いつでも使える ─────────

        [Test]
        public void APIキーが無くても使える()
        {
            Assert.IsTrue(New().IsAvailable);
            Assert.AreEqual("", New().UnavailableReason);
        }

        [Test]
        public void 押す前に読めるかどうかを判定できる()
        {
            Assert.IsTrue(New().CanImport("https://www.youtube.com/watch?v=aaaaaaaaaaa"));
            Assert.IsFalse(New().CanImport("こんにちは"));
            Assert.IsFalse(New().CanImport(""));
        }
    }
}
