using System;
using NUnit.Framework;
using SmartMediaPlatform.CatalogBuilder;

namespace SmartMediaPlatform.CatalogBuilder.Tests
{
    /// <summary>
    /// <see cref="CatalogApiDataPolicy"/> の 30 日ルール。
    ///
    /// <b>いちばん大事なのは「分からないものは切れている扱い」</b>です。
    /// 取得日時が読めないデータを持ち続けるより、取り直すほうが安全なので。
    /// </summary>
    public class CatalogApiDataPolicyTests
    {
        static readonly DateTime Now = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);

        static string DaysAgo(int days)
        {
            return Now.AddDays(-days).ToString(CatalogApiDataPolicy.TimeFormat);
        }

        // ───────── 読み書き ─────────

        [Test]
        public void 書いた形をそのまま読み戻せる()
        {
            string stamp = CatalogApiDataPolicy.NowStamp();

            DateTime parsed;
            Assert.IsTrue(CatalogApiDataPolicy.TryParseStamp(stamp, out parsed));
            Assert.AreEqual(DateTimeKind.Utc, parsed.Kind, "必ず UTC で読む");
        }

        [Test]
        public void 読めない文字列は失敗として返す()
        {
            DateTime parsed;

            Assert.IsFalse(CatalogApiDataPolicy.TryParseStamp("", out parsed));
            Assert.IsFalse(CatalogApiDataPolicy.TryParseStamp(null, out parsed));
            Assert.IsFalse(CatalogApiDataPolicy.TryParseStamp("きのう", out parsed));
            Assert.IsFalse(CatalogApiDataPolicy.TryParseStamp("2026-08-08", out parsed));
        }

        // ───────── 何日たったか ─────────

        [Test]
        public void 経過日数を数えられる()
        {
            Assert.AreEqual(0, CatalogApiDataPolicy.AgeInDays(DaysAgo(0), Now));
            Assert.AreEqual(10, CatalogApiDataPolicy.AgeInDays(DaysAgo(10), Now));
            Assert.AreEqual(45, CatalogApiDataPolicy.AgeInDays(DaysAgo(45), Now));
        }

        [Test]
        public void 分からないものは負の数で返す()
        {
            Assert.AreEqual(-1, CatalogApiDataPolicy.AgeInDays("", Now));
            Assert.AreEqual(-1, CatalogApiDataPolicy.AgeInDays("こわれた", Now));
        }

        [Test]
        public void 時計が先に進んでいても零日として扱う()
        {
            // 別の PC で取り込んだデータを持ち込むと起きうる。
            // 負の日数を返すと「分からない」と区別が付かなくなる。
            string future = Now.AddDays(3).ToString(CatalogApiDataPolicy.TimeFormat);

            Assert.AreEqual(0, CatalogApiDataPolicy.AgeInDays(future, Now));
        }

        // ───────── 期限 ─────────

        [Test]
        public void 三十日で期限切れになる()
        {
            Assert.IsFalse(CatalogApiDataPolicy.IsExpired(DaysAgo(29), Now));
            Assert.IsTrue(CatalogApiDataPolicy.IsExpired(DaysAgo(30), Now));
            Assert.IsTrue(CatalogApiDataPolicy.IsExpired(DaysAgo(31), Now));
        }

        [Test]
        public void 取得日時が分からないものは期限切れとして扱う()
        {
            // ここがこのクラスでいちばん大事な判断。
            // 分からないものを持ち続けるより、取り直すほうが安全。
            Assert.IsTrue(CatalogApiDataPolicy.IsExpired("", Now));
            Assert.IsTrue(CatalogApiDataPolicy.IsExpired(null, Now));
            Assert.IsTrue(CatalogApiDataPolicy.IsExpired("こわれた", Now));
        }

        [Test]
        public void 切れる手前で知らせ始める()
        {
            Assert.IsFalse(CatalogApiDataPolicy.IsExpiringSoon(DaysAgo(22), Now));
            Assert.IsTrue(CatalogApiDataPolicy.IsExpiringSoon(DaysAgo(23), Now));
            Assert.IsTrue(CatalogApiDataPolicy.IsExpiringSoon(DaysAgo(29), Now));
        }

        [Test]
        public void 切れたあとはもうすぐ扱いにしない()
        {
            // 切れているものは「もうすぐ切れる」ではなく「切れている」。
            // 2 つが同時に真になると、画面の出し分けができない。
            Assert.IsFalse(CatalogApiDataPolicy.IsExpiringSoon(DaysAgo(30), Now));
            Assert.IsTrue(CatalogApiDataPolicy.IsExpired(DaysAgo(30), Now));
        }

        [Test]
        public void 残り日数を数えられる()
        {
            Assert.AreEqual(30, CatalogApiDataPolicy.DaysLeft(DaysAgo(0), Now));
            Assert.AreEqual(5, CatalogApiDataPolicy.DaysLeft(DaysAgo(25), Now));
            Assert.AreEqual(0, CatalogApiDataPolicy.DaysLeft(DaysAgo(30), Now));
            Assert.AreEqual(0, CatalogApiDataPolicy.DaysLeft(DaysAgo(99), Now));
            Assert.AreEqual(0, CatalogApiDataPolicy.DaysLeft("", Now));
        }

        // ───────── 画面に出す文 ─────────

        [Test]
        public void 期限切れなら次に何をすればよいかまで書く()
        {
            string text = CatalogApiDataPolicy.Describe(DaysAgo(40), Now);

            StringAssert.Contains("期限切れ", text);
            StringAssert.Contains("取り込み直す", text);
        }

        [Test]
        public void 分からないものにも次の手を書く()
        {
            string text = CatalogApiDataPolicy.Describe("", Now);

            StringAssert.Contains("分かりません", text);
            StringAssert.Contains("取り込み直す", text);
        }

        [Test]
        public void まだ余裕があるときは残り日数を出さない()
        {
            // 毎回「あと 28 日」と出ると、見なくなる。
            string text = CatalogApiDataPolicy.Describe(DaysAgo(2), Now);

            Assert.IsFalse(text.Contains("あと"), text);
        }
    }
}
