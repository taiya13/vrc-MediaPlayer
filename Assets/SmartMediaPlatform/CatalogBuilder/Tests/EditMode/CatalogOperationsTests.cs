using NUnit.Framework;
using SmartMediaPlatform.CatalogBuilder;

namespace SmartMediaPlatform.CatalogBuilder.Tests
{
    /// <summary>
    /// Phase6-5: 長く使うための道具の検証。
    ///
    /// <list type="bullet">
    /// <item><see cref="CatalogSortOrder"/> …… 並べ替え(データは動かさない)</item>
    /// <item><see cref="CatalogBulkEdit"/> …… まとめて直す</item>
    /// <item><see cref="CatalogSubscription"/> …… どこまで取ったかの覚え書き</item>
    /// <item><see cref="CatalogImportBoundary"/> …… 新着だけ取るための境目</item>
    /// <item><see cref="CatalogSidecar"/> …… 再生に要らない情報の持ち回り</item>
    /// </list>
    /// どれも Unity にも取り込み元にも依存しないので、ここで全部確かめられます。
    /// </summary>
    public sealed class CatalogOperationsTests
    {
        private static CatalogDraftItem Item(
            string id, string title, string channel, int duration, string published)
        {
            var item = new CatalogDraftItem();
            item.Id = id;
            item.Title = title;
            item.Artist = channel;
            item.Url = "https://example.com/" + id;
            item.DurationSeconds = duration;
            item.PublishedAt = published;
            return item;
        }

        private static CatalogDraft Draft(params CatalogDraftItem[] items)
        {
            var draft = new CatalogDraft();
            for (int i = 0; i < items.Length; i++) draft.Add(items[i]);
            return draft;
        }

        private static int[] All(CatalogDraft draft)
        {
            var result = new int[draft.Count];
            for (int i = 0; i < result.Length; i++) result[i] = i;
            return result;
        }

        // ───────── 並べ替え ─────────

        [Test]
        public void ManualOrderIsTheCatalogOrder()
        {
            var draft = Draft(
                Item("c", "うた", "B", 100, "2024-03-01"),
                Item("a", "あさ", "A", 300, "2024-01-01"));

            int[] sorted = CatalogSortOrder.Apply(
                draft.Items, All(draft), CatalogSortOrder.Manual, false);

            Assert.AreEqual(0, sorted[0], "手動順は並べ替えない");
            Assert.AreEqual(1, sorted[1]);
        }

        [Test]
        public void SortingDoesNotMoveTheData()
        {
            var draft = Draft(
                Item("c", "うた", "B", 100, "2024-03-01"),
                Item("a", "あさ", "A", 300, "2024-01-01"));

            CatalogSortOrder.Apply(draft.Items, All(draft), CatalogSortOrder.Title, false);

            Assert.AreEqual("c", draft.GetAt(0).Id,
                            "カタログの並び = ワールドの並び。勝手に変えない");
        }

        [Test]
        public void SortingByDurationAndTitle()
        {
            var draft = Draft(
                Item("a", "ちゅう", "X", 200, ""),
                Item("b", "あ", "X", 100, ""),
                Item("c", "い", "X", 300, ""));

            int[] byDuration = CatalogSortOrder.Apply(
                draft.Items, All(draft), CatalogSortOrder.Duration, false);

            Assert.AreEqual(1, byDuration[0], "短い順");
            Assert.AreEqual(2, byDuration[2]);

            int[] byTitle = CatalogSortOrder.Apply(
                draft.Items, All(draft), CatalogSortOrder.Title, false);

            Assert.AreEqual("あ", draft.GetAt(byTitle[0]).Title);
        }

        [Test]
        public void DescendingFlipsTheOrder()
        {
            var draft = Draft(
                Item("a", "あ", "X", 100, ""),
                Item("b", "い", "X", 300, ""));

            int[] sorted = CatalogSortOrder.Apply(
                draft.Items, All(draft), CatalogSortOrder.Duration, true);

            Assert.AreEqual(1, sorted[0], "長い順");
        }

        [Test]
        public void ItemsWithoutAPublishDateGoLast()
        {
            var draft = Draft(
                Item("a", "あ", "X", 0, ""),
                Item("b", "い", "X", 0, "2024-01-01"),
                Item("c", "う", "X", 0, "2023-01-01"));

            int[] sorted = CatalogSortOrder.Apply(
                draft.Items, All(draft), CatalogSortOrder.PublishedAt, false);

            Assert.AreEqual("c", draft.GetAt(sorted[0]).Id, "古い順");
            Assert.AreEqual("b", draft.GetAt(sorted[1]).Id);
            Assert.AreEqual("a", draft.GetAt(sorted[2]).Id, "日付が無いものは後ろへ");
        }

        [Test]
        public void ChannelSortKeepsTitlesInOrderWithinAChannel()
        {
            var draft = Draft(
                Item("a", "ゆう", "みどり", 0, ""),
                Item("b", "あさ", "あお", 0, ""),
                Item("c", "あさ", "みどり", 0, ""));

            int[] sorted = CatalogSortOrder.Apply(
                draft.Items, All(draft), CatalogSortOrder.Channel, false);

            Assert.AreEqual("b", draft.GetAt(sorted[0]).Id, "あお が先");
            Assert.AreEqual("c", draft.GetAt(sorted[1]).Id, "みどりの中は見出し順");
            Assert.AreEqual("a", draft.GetAt(sorted[2]).Id);
        }

        [Test]
        public void EqualValuesKeepTheirOriginalOrder()
        {
            var draft = Draft(
                Item("a", "同じ", "X", 100, ""),
                Item("b", "同じ", "X", 100, ""),
                Item("c", "同じ", "X", 100, ""));

            int[] sorted = CatalogSortOrder.Apply(
                draft.Items, All(draft), CatalogSortOrder.Title, false);

            Assert.AreEqual(0, sorted[0], "同じ値なら元の順(何度やっても同じ並び)");
            Assert.AreEqual(1, sorted[1]);
            Assert.AreEqual(2, sorted[2]);
        }

        [Test]
        public void SortingWorksOnFilteredPositions()
        {
            var draft = Draft(
                Item("a", "あ", "X", 300, ""),
                Item("b", "い", "Y", 100, ""),
                Item("c", "う", "Y", 200, ""));

            var filter = new CatalogItemFilter();
            filter.Channel = "Y";

            int[] sorted = CatalogSortOrder.Apply(
                draft.Items, filter.Apply(draft.Items), CatalogSortOrder.Duration, false);

            Assert.AreEqual(2, sorted.Length, "絞り込みと重ねられる");
            Assert.AreEqual(1, sorted[0]);
            Assert.AreEqual(2, sorted[1]);
        }

        [Test]
        public void SortingIsSafeOnNothing()
        {
            Assert.AreEqual(0, CatalogSortOrder.Apply(null, null, CatalogSortOrder.Title, false).Length);
        }

        // ───────── まとめて直す ─────────

        [Test]
        public void GenreAndArtistCanBeSetForMany()
        {
            var draft = Draft(
                Item("a", "あ", "X", 0, ""),
                Item("b", "い", "X", 0, ""),
                Item("c", "う", "X", 0, ""));

            int changed = CatalogBulkEdit.SetGenre(draft.Items, new[] { 0, 2 }, "音楽");

            Assert.AreEqual(2, changed);
            Assert.AreEqual("音楽", draft.GetAt(0).Genre);
            Assert.AreEqual("", draft.GetAt(1).Genre, "選んでいないものは触らない");

            CatalogBulkEdit.SetArtist(draft.Items, new[] { 1 }, "みどり");
            Assert.AreEqual("みどり", draft.GetAt(1).Artist);
        }

        [Test]
        public void AlreadyCorrectRowsAreNotCounted()
        {
            var draft = Draft(Item("a", "あ", "X", 0, ""));
            draft.GetAt(0).Genre = "音楽";

            Assert.AreEqual(0, CatalogBulkEdit.SetGenre(draft.Items, new[] { 0 }, "音楽"),
                            "もともと同じなら数えない(押しても何も起きていないと分かる)");
        }

        [Test]
        public void TagsAreAddedWithoutDuplicating()
        {
            var draft = Draft(Item("a", "あ", "X", 0, ""), Item("b", "い", "X", 0, ""));
            draft.GetAt(0).Tags = new[] { "朝" };

            int changed = CatalogBulkEdit.AddTag(draft.Items, new[] { 0, 1 }, "朝");

            Assert.AreEqual(1, changed, "すでに付いているものには足さない");
            Assert.AreEqual(1, draft.GetAt(0).Tags.Length);
            Assert.AreEqual(1, draft.GetAt(1).Tags.Length);
        }

        [Test]
        public void TagsCanBeRemovedInBulk()
        {
            var draft = Draft(Item("a", "あ", "X", 0, ""), Item("b", "い", "X", 0, ""));
            draft.GetAt(0).Tags = new[] { "朝", "静か" };
            draft.GetAt(1).Tags = new[] { "夜" };

            int changed = CatalogBulkEdit.RemoveTag(draft.Items, new[] { 0, 1 }, "朝");

            Assert.AreEqual(1, changed);
            Assert.AreEqual(1, draft.GetAt(0).Tags.Length);
            Assert.AreEqual("静か", draft.GetAt(0).Tags[0]);
            Assert.AreEqual(1, draft.GetAt(1).Tags.Length, "付いていないものは触らない");
        }

        [Test]
        public void BulkRelatedLooksAtEverythingButWritesOnlyTheChosen()
        {
            var draft = Draft(
                Item("a", "あ", "みどり", 0, ""),
                Item("b", "い", "みどり", 0, ""),
                Item("c", "う", "みどり", 0, ""));

            int changed = CatalogBulkEdit.RegenerateRelated(
                draft.Items, new[] { 0 }, new RelatedIdGenerator());

            Assert.AreEqual(1, changed);
            Assert.AreEqual(2, draft.GetAt(0).RelatedIds.Length,
                            "選んだのは 1 件でも、近さは全部から探す");
            Assert.AreEqual(0, draft.GetAt(1).RelatedIds.Length, "書き込むのは選んだものだけ");
        }

        [Test]
        public void BulkEditIsSafeOnNothing()
        {
            Assert.AreEqual(0, CatalogBulkEdit.SetGenre(null, null, "音楽"));
            Assert.AreEqual(0, CatalogBulkEdit.AddTag(null, new[] { 0 }, "朝"));
            Assert.AreEqual(0, CatalogBulkEdit.AddTag(new CatalogDraft().Items, new[] { 99 }, "朝"));
            Assert.AreEqual(0, CatalogBulkEdit.RegenerateRelated(null, null, null));
        }

        // ───────── 見張り(差分取得)─────────

        [Test]
        public void RegisteringTheSameSourceTwiceDoesNotDuplicate()
        {
            var book = new CatalogSubscriptionBook();

            book.Register("YouTube", "https://youtube.com/@a", "Aさん", CatalogSubscription.KindChannel);
            book.Register("YouTube", " https://youtube.com/@a ", "A さん(改)", CatalogSubscription.KindChannel);

            Assert.AreEqual(1, book.Count, "前後の空白が違うだけなら同じもの");
            Assert.AreEqual("A さん(改)", book.GetAt(0).DisplayName, "名前は新しいほうへ");
        }

        [Test]
        public void FetchingIsRemembered()
        {
            var book = new CatalogSubscriptionBook();
            CatalogSubscription sub = book.Register(
                "YouTube", "@a", "Aさん", CatalogSubscription.KindChannel);

            Assert.IsFalse(sub.HasFetched);
            Assert.AreEqual("未取得", sub.FormatLastFetched());

            sub.MarkFetched("video9", 3, "2026-08-02T05:00:00.0000000Z");

            Assert.IsTrue(sub.HasFetched);
            Assert.AreEqual("video9", sub.LastItemId);
            Assert.AreEqual(3, sub.ImportedCount);
            Assert.AreEqual(3, sub.LastNewCount);
        }

        [Test]
        public void AnEmptyFetchKeepsTheOldMarker()
        {
            var sub = new CatalogSubscription();
            sub.MarkFetched("video9", 3, "2026-08-01T00:00:00.0000000Z");

            sub.MarkFetched("", 0, "2026-08-02T00:00:00.0000000Z");

            Assert.AreEqual("video9", sub.LastItemId,
                            "目印を消すと、次に全件取り直すことになる");
            Assert.AreEqual(3, sub.ImportedCount);
            Assert.AreEqual(0, sub.LastNewCount);
        }

        [Test]
        public void PlaylistsAndChannelsShareTheSameShape()
        {
            var book = new CatalogSubscriptionBook();

            book.Register("YouTube", "@a", "Aさん", CatalogSubscription.KindChannel);
            book.Register("YouTube", "PLxxxx", "お気に入り", CatalogSubscription.KindPlaylist);

            Assert.AreEqual(2, book.Count);
            Assert.AreEqual("チャンネル", book.GetAt(0).KindLabel);
            Assert.AreEqual("プレイリスト", book.GetAt(1).KindLabel);
        }

        [Test]
        public void DisabledSourcesAreLeftOutOfTheActiveList()
        {
            var book = new CatalogSubscriptionBook();
            book.Register("YouTube", "@a", "Aさん", CatalogSubscription.KindChannel);
            book.Register("YouTube", "@b", "Bさん", CatalogSubscription.KindChannel).Enabled = false;

            Assert.AreEqual(2, book.Count, "切っても消えない");
            Assert.AreEqual(1, book.Active().Length);
        }

        [Test]
        public void TheBoundaryKnowsWhatIsAlreadyThere()
        {
            var draft = Draft(Item("a", "あ", "X", 0, ""), Item("b", "い", "X", 0, ""));

            var sub = new CatalogSubscription();
            sub.LastItemId = "z";

            CatalogImportBoundary boundary = CatalogImportBoundary.From(sub, draft.Items);

            Assert.IsTrue(boundary.IsKnown("a"), "カタログにあるものは知っている");
            Assert.IsTrue(boundary.IsKnown(" b "), "前後の空白は気にしない");
            Assert.IsTrue(boundary.IsKnown("z"), "前回の目印も知っている");
            Assert.IsFalse(boundary.IsKnown("new"));
            Assert.IsFalse(boundary.IsKnown(""));
        }

        [Test]
        public void TheBoundaryWorksWithoutASubscription()
        {
            var draft = Draft(Item("a", "あ", "X", 0, ""));

            // 1 回目(まだ見張っていない)でも、いまのカタログが目印になる。
            CatalogImportBoundary boundary = CatalogImportBoundary.From(null, draft.Items);

            Assert.IsTrue(boundary.IsKnown("a"));
            Assert.IsFalse(boundary.IsKnown("b"));
        }

        // ───────── 覚え書き(再生に要らない情報)─────────

        [Test]
        public void ThumbnailsAndDatesSurviveASaveAndReload()
        {
            var original = Draft(Item("a", "あ", "みどり", 100, "2024-05-01"));
            original.GetAt(0).ThumbnailPath = "https://img.example/1.jpg";
            original.GetAt(0).Source = "YouTube";

            CatalogSidecar sidecar = CatalogSidecar.CaptureFrom(original.Items, null);

            // アセットを通ると、再生に要らないものは落ちる(MediaItem に無いため)。
            var reloaded = new CatalogDraft();
            reloaded.LoadFrom(original.ToMediaItems());

            Assert.AreEqual("", reloaded.GetAt(0).ThumbnailPath, "アセットだけでは消えてしまう");

            sidecar.ApplyTo(reloaded.Items, null);

            Assert.AreEqual("https://img.example/1.jpg", reloaded.GetAt(0).ThumbnailPath);
            Assert.AreEqual("2024-05-01", reloaded.GetAt(0).PublishedAt);
            Assert.AreEqual("YouTube", reloaded.GetAt(0).Source);
        }

        [Test]
        public void TheSidecarMatchesByIdNotByOrder()
        {
            var original = Draft(
                Item("a", "あ", "X", 0, "2024-01-01"),
                Item("b", "い", "X", 0, "2024-02-01"));

            CatalogSidecar sidecar = CatalogSidecar.CaptureFrom(original.Items, null);

            // 並べ替えたあとでも正しく戻ること。
            var shuffled = Draft(Item("b", "い", "X", 0, ""), Item("a", "あ", "X", 0, ""));
            sidecar.ApplyTo(shuffled.Items, null);

            Assert.AreEqual("2024-02-01", shuffled.GetAt(0).PublishedAt);
            Assert.AreEqual("2024-01-01", shuffled.GetAt(1).PublishedAt);
        }

        [Test]
        public void RowsWithNothingExtraAreNotWrittenDown()
        {
            var draft = Draft(Item("a", "あ", "X", 0, ""));

            CatalogSidecar sidecar = CatalogSidecar.CaptureFrom(draft.Items, null);

            Assert.AreEqual(0, sidecar.Items.Length,
                            "手入力だけのカタログに、空のファイルを増やさない");
        }

        [Test]
        public void SubscriptionsRideAlongInTheSidecar()
        {
            var book = new CatalogSubscriptionBook();
            book.Register("YouTube", "@a", "Aさん", CatalogSubscription.KindChannel);

            CatalogSidecar sidecar = CatalogSidecar.CaptureFrom(new CatalogDraft().Items, book);

            var restored = new CatalogSubscriptionBook();
            sidecar.ApplyTo(null, restored);

            Assert.AreEqual(1, restored.Count);
            Assert.AreEqual("Aさん", restored.GetAt(0).DisplayName);
        }

        [Test]
        public void TheSidecarIsSafeOnNothing()
        {
            var sidecar = new CatalogSidecar();

            Assert.AreEqual(0, sidecar.ApplyTo(null, null));
            Assert.AreEqual(0, CatalogSidecar.CaptureFrom(null, null).Items.Length);
        }
    }
}
