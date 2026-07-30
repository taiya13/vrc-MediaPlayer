using NUnit.Framework;
using SmartMediaPlatform.World.UdonModel;

namespace SmartMediaPlatform.World.UdonModel.Tests
{
    /// <summary>
    /// Phase5-3: 一覧のページ送りの検証。
    ///
    /// 実機で動くのは <c>UdonMediaListView</c> ですが、
    /// <c>UdonSharpBehaviour</c> は EditMode から触れないので、
    /// <b>同じ数え方を写した <see cref="ListPageModel"/> をここで確かめます</b>
    /// (Phase5-2 の <c>PlaybackModel</c> と同じ手順)。
    /// </summary>
    public sealed class ListPageModelTests
    {
        private static ListPageModel Model(int rowCount, int totalCount)
        {
            var model = new ListPageModel(rowCount);
            model.SetTotalCount(totalCount);
            return model;
        }

        // ───────── ページ数 ─────────

        [Test]
        public void ExactlyFullPagesDoNotGetAnExtraOne()
        {
            Assert.AreEqual(2, Model(6, 12).PageCount, "12 件を 6 行ずつ = ちょうど 2 ページ");
        }

        [Test]
        public void TheRemainderGetsItsOwnPage()
        {
            Assert.AreEqual(3, Model(6, 13).PageCount, "13 件目のために 3 ページ目が要る");
        }

        [Test]
        public void AnEmptyListStillHasOnePage()
        {
            var model = Model(6, 0);

            Assert.AreEqual(1, model.PageCount, "「まだありません」を出す場所が要る");
            Assert.AreEqual(0, model.Page);
            Assert.IsFalse(model.HasNextPage);
            Assert.IsFalse(model.HasPreviousPage);
        }

        [Test]
        public void NoRowsMeansOnePage()
        {
            // 行を 1 つも用意していないパネル(= 表示しない一覧)でも壊れない
            Assert.AreEqual(1, Model(0, 20).PageCount);
        }

        // ───────── 行 → 位置 ─────────

        [Test]
        public void TheFirstPageMapsRowsStraightThrough()
        {
            var model = Model(6, 20);

            Assert.AreEqual(0, model.PositionOf(0));
            Assert.AreEqual(5, model.PositionOf(5));
        }

        [Test]
        public void TheSecondPageIsOffsetByOnePageOfRows()
        {
            var model = Model(6, 20);
            model.NextPage();

            Assert.AreEqual(6, model.FirstPosition);
            Assert.AreEqual(6, model.PositionOf(0));
            Assert.AreEqual(11, model.PositionOf(5));
        }

        [Test]
        public void RowsPastTheEndAreEmpty()
        {
            var model = Model(6, 8);
            model.NextPage();

            // 2 ページ目には 2 件しか無い
            Assert.AreEqual(6, model.PositionOf(0));
            Assert.AreEqual(7, model.PositionOf(1));
            Assert.AreEqual(-1, model.PositionOf(2), "空行は -1(押しても何も起きない)");
            Assert.AreEqual(2, model.FilledRowCount);
        }

        [Test]
        public void RowsOutsideTheViewAreEmpty()
        {
            var model = Model(6, 20);

            Assert.AreEqual(-1, model.PositionOf(-1));
            Assert.AreEqual(-1, model.PositionOf(6));
        }

        // ───────── 送り ─────────

        [Test]
        public void PagingStopsAtBothEnds()
        {
            var model = Model(6, 8);

            Assert.IsFalse(model.PreviousPage(), "先頭より前には行かない");
            Assert.IsTrue(model.NextPage());
            Assert.IsFalse(model.NextPage(), "最後より先には行かない");
            Assert.AreEqual(1, model.Page);
        }

        [Test]
        public void RevealJumpsToThePageHoldingTheItem()
        {
            var model = Model(6, 20);

            Assert.IsTrue(model.RevealPosition(13));
            Assert.AreEqual(2, model.Page, "13 番目は 3 ページ目(0 から数えて 2)");
            Assert.AreEqual(13, model.PositionOf(1));
        }

        [Test]
        public void RevealIgnoresSomethingOutsideTheList()
        {
            var model = Model(6, 20);
            model.NextPage();

            Assert.IsFalse(model.RevealPosition(99));
            Assert.AreEqual(1, model.Page, "ページは動かない");
        }

        // ───────── 件数が変わったとき ─────────

        [Test]
        public void ShrinkingTheListPullsThePageBack()
        {
            // Queue の後ろのほうを見ているときに、まとめて消されるとこうなる
            var model = Model(6, 20);
            model.NextPage();
            model.NextPage();
            Assert.AreEqual(2, model.Page);

            model.SetTotalCount(4);

            Assert.AreEqual(0, model.Page, "空のページに取り残されない");
            Assert.AreEqual(0, model.PositionOf(0));
            Assert.AreEqual(4, model.FilledRowCount);
        }

        [Test]
        public void EmptyingTheListPullsThePageBackToTheFirst()
        {
            var model = Model(6, 20);
            model.NextPage();

            model.SetTotalCount(0);

            Assert.AreEqual(0, model.Page);
            Assert.AreEqual(0, model.FilledRowCount);
            Assert.AreEqual(-1, model.PositionOf(0));
        }

        [Test]
        public void GrowingTheListKeepsThePageWhereItWas()
        {
            var model = Model(6, 8);
            model.NextPage();

            model.SetTotalCount(30);

            Assert.AreEqual(1, model.Page, "見ていた場所から動かされない");
            Assert.AreEqual(6, model.PositionOf(0));
        }

        [Test]
        public void NegativeCountsAreTreatedAsEmpty()
        {
            var model = Model(6, -5);

            Assert.AreEqual(0, model.TotalCount);
            Assert.AreEqual(1, model.PageCount);
        }

        // ───────── 一覧の中身は知らない ─────────

        [Test]
        public void TheModelNeverExposesAUrlOrATitle()
        {
            var type = typeof(ListPageModel);

            Assert.IsNull(type.GetProperty("Url"));
            Assert.IsNull(type.GetProperty("Title"));
            Assert.IsNull(type.GetMethod("GetUrl"));
            Assert.IsNull(type.GetMethod("GetTitle"),
                "ページの数え方だけを持つ — 何を表示するかは知らない");
        }
    }
}
