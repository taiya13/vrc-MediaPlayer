using NUnit.Framework;
using SmartMediaPlatform.World.UdonModel;

namespace SmartMediaPlatform.World.UdonModel.Tests
{
    /// <summary>
    /// Phase5-5: 一覧のスクロールの検証。
    ///
    /// 実機で動くのは <c>UdonMediaListView</c> ですが、
    /// <c>UdonSharpBehaviour</c> は EditMode から触れないので、
    /// <b>同じ数え方を写した <see cref="ListScrollModel"/> をここで確かめます</b>
    /// (Phase5-2 の <c>PlaybackModel</c>、Phase5-4 の <c>PlaybackClockModel</c> と同じ手順)。
    /// </summary>
    public sealed class ListScrollModelTests
    {
        private static ListScrollModel Model(int rowCount, int totalCount)
        {
            var model = new ListScrollModel(rowCount);
            model.SetTotalCount(totalCount);
            return model;
        }

        // ───────── 送っても行が空かない(ページ送りをやめた理由)─────────

        [Test]
        public void ScrollingToTheEndStillFillsEveryRow()
        {
            // 22 件を 6 行で見る。ページ送りなら 4 ページ目は 4 件で下 2 行が空いた。
            var model = Model(6, 22);
            model.ScrollToBottom();

            Assert.AreEqual(16, model.Offset, "最後の 6 件がちょうど収まる位置で止まる");
            Assert.AreEqual(6, model.FilledRowCount, "空行が出ない");
            Assert.AreEqual(21, model.PositionOf(5), "最後の 1 件が最下行に出る");
        }

        [Test]
        public void AShortListNeverScrolls()
        {
            var model = Model(6, 4);

            Assert.AreEqual(0, model.MaxOffset);
            Assert.IsFalse(model.CanScrollUp);
            Assert.IsFalse(model.CanScrollDown);
            Assert.IsFalse(model.ScrollDown());
            Assert.AreEqual(4, model.FilledRowCount);
        }

        [Test]
        public void AnEmptyListIsQuiet()
        {
            var model = Model(6, 0);

            Assert.AreEqual(0, model.FilledRowCount);
            Assert.AreEqual(-1, model.PositionOf(0));
            Assert.AreEqual(0, model.FirstShownNumber);
            Assert.AreEqual(0, model.LastShownNumber);
            Assert.IsFalse(model.CanScrollDown);
        }

        // ───────── 動かす ─────────

        [Test]
        public void TheStepDefaultsToOneScreen()
        {
            var model = Model(6, 30);

            Assert.AreEqual(6, model.EffectiveStep, "Step が 0 なら 1 画面ぶん");

            model.ScrollDown();
            Assert.AreEqual(6, model.Offset);
        }

        [Test]
        public void TheStepCanBeMadeFiner()
        {
            var model = Model(6, 30);
            model.Step = 3;

            model.ScrollDown();
            Assert.AreEqual(3, model.Offset, "半画面ずつ動くと、見ていた行が半分残る");

            model.ScrollDown();
            Assert.AreEqual(6, model.Offset);

            model.ScrollUp();
            Assert.AreEqual(3, model.Offset);
        }

        [Test]
        public void ScrollingStopsAtBothEnds()
        {
            var model = Model(6, 10);

            Assert.IsFalse(model.ScrollUp(), "先頭より前には行かない");

            Assert.IsTrue(model.ScrollDown());
            Assert.AreEqual(4, model.Offset, "10 − 6 = 4 で止まる");

            Assert.IsFalse(model.ScrollDown(), "最後より先には行かない");
        }

        [Test]
        public void ScrollByReportsWhetherItActuallyMoved()
        {
            var model = Model(6, 20);

            Assert.IsTrue(model.ScrollBy(5));
            Assert.IsFalse(model.ScrollBy(0));
            Assert.IsTrue(model.ScrollBy(-5));
            Assert.IsFalse(model.ScrollBy(-1), "もう先頭なので動かない");
        }

        // ───────── 何番目を見ているか ─────────

        [Test]
        public void TheShownRangeIsCountedFromOne()
        {
            var model = Model(6, 24);

            Assert.AreEqual(1, model.FirstShownNumber);
            Assert.AreEqual(6, model.LastShownNumber);

            model.ScrollDown();

            Assert.AreEqual(7, model.FirstShownNumber, "「7〜12 / 24 件」と出せる");
            Assert.AreEqual(12, model.LastShownNumber);
        }

        [Test]
        public void TheShownRangeStopsAtTheTotal()
        {
            var model = Model(6, 4);

            Assert.AreEqual(1, model.FirstShownNumber);
            Assert.AreEqual(4, model.LastShownNumber, "無い件数まで数えない");
        }

        [Test]
        public void RowsMapStraightThroughFromTheOffset()
        {
            var model = Model(6, 20);
            model.ScrollBy(4);

            Assert.AreEqual(4, model.PositionOf(0));
            Assert.AreEqual(9, model.PositionOf(5));
            Assert.AreEqual(-1, model.PositionOf(6), "並べた行の外");
            Assert.AreEqual(-1, model.PositionOf(-1));
        }

        // ───────── 目的の行を見せる ─────────

        [Test]
        public void RevealScrollsDownJustEnough()
        {
            var model = Model(6, 30);

            Assert.IsTrue(model.Reveal(13));
            Assert.AreEqual(8, model.Offset, "13 が最下行に来るところまでしか動かない");
            Assert.AreEqual(13, model.PositionOf(5));
        }

        [Test]
        public void RevealScrollsUpJustEnough()
        {
            var model = Model(6, 30);
            model.ScrollBy(20);

            Assert.IsTrue(model.Reveal(3));
            Assert.AreEqual(3, model.Offset, "3 が最上行に来る");
        }

        [Test]
        public void RevealLeavesTheViewAloneIfItIsAlreadyVisible()
        {
            var model = Model(6, 30);
            model.ScrollBy(10);

            Assert.IsFalse(model.Reveal(12), "見えているものを追いかけて画面を飛ばさない");
            Assert.AreEqual(10, model.Offset);
        }

        [Test]
        public void RevealIgnoresSomethingOutsideTheList()
        {
            var model = Model(6, 20);
            model.ScrollBy(6);

            Assert.IsFalse(model.Reveal(99));
            Assert.AreEqual(6, model.Offset);
        }

        // ───────── 件数が変わったとき ─────────

        [Test]
        public void ShrinkingTheListPullsTheViewBack()
        {
            // Queue の後ろを見ているときに、まとめて消されるとこうなる
            var model = Model(6, 30);
            model.ScrollBy(20);

            model.SetTotalCount(8);

            Assert.AreEqual(2, model.Offset, "空白へ取り残されない");
            Assert.AreEqual(6, model.FilledRowCount);
        }

        [Test]
        public void EmptyingTheListGoesBackToTheTop()
        {
            var model = Model(6, 30);
            model.ScrollBy(20);

            model.SetTotalCount(0);

            Assert.AreEqual(0, model.Offset);
            Assert.AreEqual(0, model.FilledRowCount);
        }

        [Test]
        public void GrowingTheListKeepsTheViewWhereItWas()
        {
            var model = Model(6, 10);
            model.ScrollBy(4);

            model.SetTotalCount(40);

            Assert.AreEqual(4, model.Offset, "見ていた場所から勝手に動かされない");
        }

        [Test]
        public void NegativeCountsAreTreatedAsEmpty()
        {
            var model = Model(6, -5);

            Assert.AreEqual(0, model.TotalCount);
            Assert.AreEqual(0, model.MaxOffset);
        }

        // ───────── つまみの位置 ─────────

        [Test]
        public void TheScrollBarShowsWhereYouAre()
        {
            var model = Model(6, 30);

            Assert.AreEqual(0f, model.ScrollFraction, 0.001f);

            model.ScrollToBottom();
            Assert.AreEqual(1f, model.ScrollFraction, 0.001f);

            model.ScrollBy(-12);
            Assert.AreEqual(0.5f, model.ScrollFraction, 0.001f, "24 のうち 12 で真ん中");
        }

        [Test]
        public void TheScrollBarFillsWhenEverythingFits()
        {
            var model = Model(6, 4);

            Assert.AreEqual(1f, model.VisibleFraction, 0.001f, "全部見えているなら満杯");
            Assert.AreEqual(0f, model.ScrollFraction, 0.001f);
        }

        [Test]
        public void TheScrollBarShrinksWithLongLists()
        {
            var model = Model(6, 30);

            Assert.AreEqual(0.2f, model.VisibleFraction, 0.001f, "30 件のうち 6 行ぶん");
        }

        // ───────── 何を知らないか ─────────

        [Test]
        public void TheModelNeverKnowsWhatIsInTheList()
        {
            var type = typeof(ListScrollModel);

            Assert.IsNull(type.GetProperty("Url"));
            Assert.IsNull(type.GetProperty("Title"));
            Assert.IsNull(type.GetMethod("GetTitle"),
                          "件数と行数だけ — 何を表示するかは知らない");
        }
    }
}
