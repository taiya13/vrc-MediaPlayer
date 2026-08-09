using NUnit.Framework;
using SmartMediaPlatform.World.UdonModel;

namespace SmartMediaPlatform.World.UdonModel.Tests
{
    /// <summary>
    /// <see cref="EndWatchModel"/> の規則。
    /// 「曲が終わったのにまた最初から始まる」を、知らせが来なくても拾えるか。
    /// </summary>
    public class EndWatchModelTests
    {
        const float Threshold = 0.4f;

        // ───────── 長さが分かるか ─────────

        [Test]
        public void 長さが取れないときは何も判断しない()
        {
            Assert.IsFalse(EndWatchModel.HasKnownLength(0f), "生配信は長さが 0");
            Assert.IsFalse(EndWatchModel.ReachedEnd(9999f, 0f, Threshold));
            Assert.IsFalse(EndWatchModel.WrappedToStart(9999f, 0f, 0f));
        }

        // ───────── 終わりまで来た ─────────

        [Test]
        public void 終わり際まで来たら終わりとみなす()
        {
            Assert.IsTrue(EndWatchModel.ReachedEnd(353.0f, 353.2f, Threshold),
                "5 分 53 秒の曲。見に行く間隔のぶん手前でも終わり");
            Assert.IsTrue(EndWatchModel.ReachedEnd(353.2f, 353.2f, Threshold));
        }

        [Test]
        public void 途中では終わりとみなさない()
        {
            Assert.IsFalse(EndWatchModel.ReachedEnd(100f, 353.2f, Threshold));
            Assert.IsFalse(EndWatchModel.ReachedEnd(352.0f, 353.2f, Threshold),
                "残り 1.2 秒はまだ終わりではない");
        }

        // ───────── 頭へ戻った ─────────

        [Test]
        public void 終わり際から頭へ戻ったら一周とみなす()
        {
            Assert.IsTrue(EndWatchModel.WrappedToStart(352.8f, 0.3f, 353.2f),
                "プレイヤーが勝手に繰り返した形");
        }

        [Test]
        public void 途中から戻したのは人の操作なので一周とみなさない()
        {
            Assert.IsFalse(EndWatchModel.WrappedToStart(100f, 0f, 353.2f),
                "1 分 40 秒から頭へ戻したのは、バーを動かした人がいるから");
        }

        [Test]
        public void 終わり際での小さな揺れは一周とみなさない()
        {
            Assert.IsFalse(EndWatchModel.WrappedToStart(352.8f, 352.0f, 353.2f),
                "0.8 秒の戻りは、位置の報告が揺れているだけ");
        }

        [Test]
        public void 前へ進んでいるうちは一周とみなさない()
        {
            Assert.IsFalse(EndWatchModel.WrappedToStart(352.0f, 352.5f, 353.2f));
        }

        // ───────── 実際の並びで通してみる ─────────

        [Test]
        public void 曲を頭から終わりまで見張ると一度だけ終わりを見つける()
        {
            const float Duration = 10f;
            float[] samples = { 0f, 2f, 4f, 6f, 8f, 9.8f };

            int found = 0;
            float previous = 0f;

            foreach (float time in samples)
            {
                if (EndWatchModel.ReachedEnd(time, Duration, Threshold)
                    || EndWatchModel.WrappedToStart(previous, time, Duration))
                {
                    found++;
                }
                previous = time;
            }

            Assert.AreEqual(1, found, "終わり際の 1 回だけ");
        }

        [Test]
        public void 繰り返す作りのプレイヤーでも終わりを見つける()
        {
            // 終わり際に来る前に頭へ飛ぶ、いじわるな並び。
            const float Duration = 10f;
            float[] samples = { 0f, 3f, 6f, 9.5f, 0.2f };

            int found = 0;
            float previous = 0f;

            foreach (float time in samples)
            {
                if (EndWatchModel.ReachedEnd(time, Duration, Threshold)
                    || EndWatchModel.WrappedToStart(previous, time, Duration))
                {
                    found++;
                }
                previous = time;
            }

            Assert.GreaterOrEqual(found, 1, "知らせが来なくても、必ずどこかで気付く");
        }
    }
}
