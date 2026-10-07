using NUnit.Framework;
using SmartMediaPlatform.CatalogBuilder;

namespace SmartMediaPlatform.CatalogBuilder.Tests
{
    /// <summary>
    /// Phase6-4: 使いやすさのために足したものの検証。
    ///
    /// 対象は<b>取り込み元を問わない 3 つ</b>です。
    /// <list type="bullet">
    /// <item><see cref="CatalogItemFilter"/> …… 見出し / チャンネル / ジャンル / タグで絞る</item>
    /// <item><see cref="CatalogItemGrouping"/> …… チャンネルなどでまとめる</item>
    /// <item><see cref="RelatedIdGenerator"/> …… 関連(おすすめ)を自動で作る</item>
    /// </list>
    /// どれも Unity にも YouTube にも依存しないので、ここで全部確かめられます。
    /// </summary>
    public sealed class CatalogBuilderPolishTests
    {
        private static CatalogDraftItem Item(
            string id, string title, string channel, string genre, params string[] tags)
        {
            var item = new CatalogDraftItem();
            item.Id = id;
            item.Title = title;
            item.Artist = channel;
            item.Genre = genre;
            item.Url = "https://example.com/" + id;
            item.Tags = tags != null ? tags : new string[0];
            return item;
        }

        private static CatalogDraft Draft(params CatalogDraftItem[] items)
        {
            var draft = new CatalogDraft();
            for (int i = 0; i < items.Length; i++) draft.Add(items[i]);
            return draft;
        }

        // ───────── 絞り込み ─────────

        [Test]
        public void AnEmptyFilterLetsEverythingThrough()
        {
            var draft = Draft(
                Item("a", "あさ", "チャンネル1", "音楽"),
                Item("b", "ひる", "チャンネル2", "ゲーム"));

            var filter = new CatalogItemFilter();

            Assert.IsTrue(filter.IsEmpty);
            Assert.AreEqual(2, filter.Apply(draft.Items).Length);
        }

        [Test]
        public void TheQueryLooksAtTitleChannelGenreAndTags()
        {
            var draft = Draft(
                Item("a", "あさのうた", "みどり", "音楽", "朝"),
                Item("b", "よるのうた", "あお", "音楽", "夜"),
                Item("c", "実況", "みどり", "ゲーム"));

            var filter = new CatalogItemFilter();

            filter.Query = "あさ";
            Assert.AreEqual(1, filter.Apply(draft.Items).Length, "見出しで引ける");

            filter.Query = "みどり";
            Assert.AreEqual(2, filter.Apply(draft.Items).Length, "チャンネルで引ける");

            filter.Query = "ゲーム";
            Assert.AreEqual(1, filter.Apply(draft.Items).Length, "ジャンルで引ける");

            filter.Query = "夜";
            Assert.AreEqual(1, filter.Apply(draft.Items).Length, "タグで引ける");
        }

        [Test]
        public void SpacesInTheQueryMeanAnd()
        {
            var draft = Draft(
                Item("a", "あさのうた", "みどり", "音楽"),
                Item("b", "よるのうた", "みどり", "音楽"));

            var filter = new CatalogItemFilter();
            filter.Query = "みどり あさ";

            int[] hits = filter.Apply(draft.Items);

            Assert.AreEqual(1, hits.Length, "両方を含むものだけ");
            Assert.AreEqual(0, hits[0]);
        }

        [Test]
        public void TheFilterKeepsTheOriginalPositions()
        {
            var draft = Draft(
                Item("a", "あ", "X", ""),
                Item("b", "い", "Y", ""),
                Item("c", "う", "Y", ""));

            var filter = new CatalogItemFilter();
            filter.Channel = "Y";

            int[] hits = filter.Apply(draft.Items);

            Assert.AreEqual(2, hits.Length);
            Assert.AreEqual(1, hits[0], "並べ替えたコピーではなく、元の位置が返る");
            Assert.AreEqual(2, hits[1]);
        }

        [Test]
        public void ChannelGenreAndTagAreExactMatches()
        {
            var draft = Draft(
                Item("a", "あ", "みどり", "音楽", "朝", "静か"),
                Item("b", "い", "みどりいろ", "音楽ゲーム", "朝焼け"));

            var filter = new CatalogItemFilter();

            filter.Channel = "みどり";
            Assert.AreEqual(1, filter.Apply(draft.Items).Length, "部分一致では引っかからない");

            filter.Clear();
            filter.Genre = "音楽";
            Assert.AreEqual(1, filter.Apply(draft.Items).Length);

            filter.Clear();
            filter.Tag = "朝";
            Assert.AreEqual(1, filter.Apply(draft.Items).Length);
        }

        [Test]
        public void FiltersStackTogether()
        {
            var draft = Draft(
                Item("a", "あさのうた", "みどり", "音楽"),
                Item("b", "あさの実況", "みどり", "ゲーム"),
                Item("c", "あさのうた", "あお", "音楽"));

            var filter = new CatalogItemFilter();
            filter.Query = "あさ";
            filter.Channel = "みどり";
            filter.Genre = "音楽";

            Assert.AreEqual(1, filter.Apply(draft.Items).Length, "全部の条件を満たすものだけ");
        }

        [Test]
        public void IncompleteRowsCanBeSingledOut()
        {
            var broken = Item("d", "URL なし", "みどり", "音楽");
            broken.Url = "";

            var draft = Draft(Item("a", "あ", "みどり", "音楽"), broken);

            var filter = new CatalogItemFilter();
            filter.OnlyIncomplete = true;

            int[] hits = filter.Apply(draft.Items);

            Assert.AreEqual(1, hits.Length, "作りかけだけを拾って直せる");
            Assert.AreEqual(1, hits[0]);
        }

        [Test]
        public void TheChoicesComeFromWhatIsActuallyThere()
        {
            var draft = Draft(
                Item("a", "あ", "みどり", "音楽", "朝"),
                Item("b", "い", "あお", "音楽", "夜"),
                Item("c", "う", "みどり", "", "朝"));

            string[] channels = CatalogItemFilter.CollectChannels(draft.Items);
            string[] genres = CatalogItemFilter.CollectGenres(draft.Items);
            string[] tags = CatalogItemFilter.CollectTags(draft.Items);

            Assert.AreEqual(2, channels.Length, "同じチャンネルは 1 度だけ");
            Assert.AreEqual("みどり", channels[0], "最初に出てきた順");
            Assert.AreEqual(1, genres.Length, "空のジャンルは選択肢に出さない");
            Assert.AreEqual(2, tags.Length);
        }

        [Test]
        public void TheFilterIsSafeOnNothing()
        {
            var filter = new CatalogItemFilter();
            filter.Query = "なにか";

            Assert.AreEqual(0, filter.Apply(null).Length);
            Assert.IsFalse(filter.Matches(null));
            Assert.AreEqual(0, CatalogItemFilter.CollectTags(null).Length);
        }

        // ───────── まとめる ─────────

        [Test]
        public void ItemsAreGroupedByChannel()
        {
            var draft = Draft(
                Item("a", "あ", "みどり", ""),
                Item("b", "い", "あお", ""),
                Item("c", "う", "みどり", ""));

            var grouping = new CatalogItemGrouping();
            grouping.Build(draft.Items);

            Assert.AreEqual(2, grouping.GroupCount);
            Assert.AreEqual("みどり", grouping.GetName(0), "最初に出てきた順に並ぶ");
            Assert.AreEqual(2, grouping.GetCount(0));

            int[] members = grouping.GetIndices(0);
            Assert.AreEqual(0, members[0], "持っているのは元の位置");
            Assert.AreEqual(2, members[1]);
        }

        [Test]
        public void GroupingCanSwitchToGenreOrSource()
        {
            var draft = Draft(
                Item("a", "あ", "みどり", "音楽"),
                Item("b", "い", "あお", "音楽"),
                Item("c", "う", "みどり", "ゲーム"));

            var grouping = new CatalogItemGrouping();

            grouping.Mode = CatalogItemGrouping.ByGenre;
            grouping.Build(draft.Items);
            Assert.AreEqual(2, grouping.GroupCount);

            grouping.Mode = CatalogItemGrouping.BySource;
            grouping.Build(draft.Items);
            Assert.AreEqual(1, grouping.GroupCount, "出どころは全部「手入力」");

            grouping.Mode = CatalogItemGrouping.Flat;
            grouping.Build(draft.Items);
            Assert.AreEqual(1, grouping.GroupCount);
            Assert.AreEqual(3, grouping.GetCount(0));
        }

        [Test]
        public void ItemsWithoutAChannelGetTheirOwnGroup()
        {
            var draft = Draft(Item("a", "あ", "", ""), Item("b", "い", "みどり", ""));

            var grouping = new CatalogItemGrouping();
            grouping.Build(draft.Items);

            Assert.AreEqual(2, grouping.GroupCount);
            Assert.AreEqual(CatalogItemGrouping.UnknownName, grouping.GetName(0));
        }

        [Test]
        public void CollapsedGroupsStayCollapsedAfterRebuilding()
        {
            var draft = Draft(Item("a", "あ", "みどり", ""), Item("b", "い", "あお", ""));

            var grouping = new CatalogItemGrouping();
            grouping.Build(draft.Items);
            grouping.SetExpanded(0, false);

            grouping.Build(draft.Items);

            Assert.IsFalse(grouping.IsExpanded(0), "たたんだものが勝手に開かない");
            Assert.IsTrue(grouping.IsExpanded(1));
        }

        [Test]
        public void ExpandAllAndCollapseAllWork()
        {
            var draft = Draft(Item("a", "あ", "みどり", ""), Item("b", "い", "あお", ""));

            var grouping = new CatalogItemGrouping();
            grouping.Build(draft.Items);

            grouping.CollapseAll();
            Assert.IsFalse(grouping.IsExpanded(0));
            Assert.IsFalse(grouping.IsExpanded(1));

            grouping.ExpandAll();
            Assert.IsTrue(grouping.IsExpanded(0));
            Assert.IsTrue(grouping.IsExpanded(1));
        }

        [Test]
        public void OneChannelIsNotWorthGrouping()
        {
            var draft = Draft(Item("a", "あ", "みどり", ""), Item("b", "い", "みどり", ""));

            var grouping = new CatalogItemGrouping();
            grouping.Build(draft.Items);

            Assert.IsFalse(grouping.IsMeaningful, "1 つにまとまるなら、まとめても意味がない");
        }

        [Test]
        public void GroupingIsSafeOnNothing()
        {
            var grouping = new CatalogItemGrouping();
            grouping.Build(null);

            Assert.AreEqual(0, grouping.GroupCount);
            Assert.AreEqual("", grouping.GetName(0));
            Assert.AreEqual(0, grouping.GetIndices(5).Length);
            Assert.AreEqual(-1, grouping.GroupOf(0));
        }

        // ───────── 選ぶ + まとめる ─────────

        [Test]
        public void AWholeChannelCanBeTickedAtOnce()
        {
            var items = new[]
            {
                Item("a", "あ", "みどり", ""),
                Item("b", "い", "あお", ""),
                Item("c", "う", "みどり", ""),
            };

            var selection = new CatalogImportSelection();
            selection.SetItems(items);

            Assert.AreEqual(2, selection.Grouping.GroupCount);
            Assert.IsTrue(selection.IsGroupFullySelected(0));

            selection.SetGroupSelected(0, false);

            Assert.AreEqual(0, selection.GroupSelectedCount(0));
            Assert.AreEqual(1, selection.SelectedCount, "別のチャンネルは触らない");
        }

        [Test]
        public void ExistingItemsAreCountedPerChannel()
        {
            var draft = Draft(Item("a", "あ", "みどり", ""));

            var selection = new CatalogImportSelection();
            selection.SetItems(new[]
            {
                Item("a", "あ", "みどり", ""),
                Item("c", "う", "みどり", ""),
                Item("b", "い", "あお", ""),
            });
            selection.MarkExisting(draft);

            Assert.AreEqual(1, selection.GroupExistingCount(0), "みどりは 1 件だけすでにある");
            Assert.AreEqual(0, selection.GroupExistingCount(1));
        }

        // ───────── 混ぜ方(重複しない)─────────

        [Test]
        public void SkipDoesNotAddTheSameIdTwice()
        {
            var draft = Draft(Item("a", "あ", "みどり", ""));

            int changed = draft.Merge(
                new[] { Item("a", "あ(新)", "みどり", ""), Item("b", "い", "みどり", "") },
                CatalogDraft.MergeSkip);

            Assert.AreEqual(1, changed, "増えたぶんだけ数える");
            Assert.AreEqual(2, draft.Count);
            Assert.AreEqual("あ", draft.GetAt(0).Title, "すでにあるものは触らない");
            Assert.AreEqual(0, draft.FindDuplicateIds().Length);
        }

        [Test]
        public void ImportingTheSameChannelTwiceAddsNothing()
        {
            var incoming = new[]
            {
                Item("a", "あ", "みどり", ""),
                Item("b", "い", "みどり", ""),
            };

            var draft = new CatalogDraft();
            draft.Merge(incoming, CatalogDraft.MergeSkip);

            int second = draft.Merge(incoming, CatalogDraft.MergeSkip);

            Assert.AreEqual(0, second, "2 回目は 1 件も増えない");
            Assert.AreEqual(2, draft.Count);
        }

        [Test]
        public void UpdateKeepsRelatedIdsThatWereAddedByHand()
        {
            var existing = Item("a", "あ", "みどり", "");
            existing.RelatedIds = new[] { "b", "c" };

            var draft = Draft(existing);

            // 取り込み元は関連を知らない。上書きで消えてしまうと
            // 「更新したらおすすめが空になった」になる。
            draft.Merge(new[] { Item("a", "あ(新)", "みどり", "") }, CatalogDraft.MergeUpdate);

            Assert.AreEqual("あ(新)", draft.GetAt(0).Title, "見出しは新しくなる");
            Assert.AreEqual(2, draft.GetAt(0).RelatedIds.Length, "手で足した関連は残る");
        }

        [Test]
        public void CountNewTellsHowManyWouldBeAdded()
        {
            var draft = Draft(Item("a", "あ", "みどり", ""));

            int fresh = draft.CountNew(new[]
            {
                Item("a", "あ", "みどり", ""),
                Item("b", "い", "みどり", ""),
                Item("b", "い(重複)", "みどり", ""),
            });

            Assert.AreEqual(1, fresh, "すでにあるもの・同じ ID の重複は数えない");
        }

        [Test]
        public void AppendStillExistsForDeliberateCopies()
        {
            var draft = Draft(Item("a", "あ", "みどり", ""));

            draft.Merge(new[] { Item("a", "あ", "みどり", "") }, CatalogDraft.MergeAppend);

            Assert.AreEqual(2, draft.Count);
            Assert.AreEqual("a-2", draft.GetAt(1).Id, "意図して複製したいときのために残してある");
        }

        // ───────── 関連の自動生成 ─────────

        [Test]
        public void TheSameChannelBecomesRelated()
        {
            var draft = Draft(
                Item("a", "あ", "みどり", ""),
                Item("b", "い", "みどり", ""),
                Item("c", "う", "あお", ""));

            int changed = new RelatedIdGenerator().Generate(draft.Items);

            Assert.AreEqual(2, changed, "「あお」は共通点が無いので空のまま");
            Assert.AreEqual(1, draft.GetAt(0).RelatedIds.Length);
            Assert.AreEqual("b", draft.GetAt(0).RelatedIds[0]);
            Assert.AreEqual(0, draft.GetAt(2).RelatedIds.Length);
        }

        [Test]
        public void NothingInCommonMeansNoRelated()
        {
            var draft = Draft(
                Item("a", "あ", "みどり", "音楽"),
                Item("b", "い", "あお", "ゲーム"));

            new RelatedIdGenerator().Generate(draft.Items);

            Assert.AreEqual(0, draft.GetAt(0).RelatedIds.Length,
                            "何の共通点も無いものを並べても「おすすめ」にならない");
        }

        [Test]
        public void CloserItemsComeFirst()
        {
            var generator = new RelatedIdGenerator();

            var draft = Draft(
                Item("me", "わたし", "みどり", "音楽", "朝"),
                Item("tagOnly", "タグだけ", "あお", "ゲーム", "朝"),
                Item("everything", "全部同じ", "みどり", "音楽", "朝"),
                Item("genreOnly", "ジャンルだけ", "あお", "音楽"));

            string[] related = generator.Suggest(draft.Items, 0);

            Assert.AreEqual("everything", related[0], "共通点が多いほど先に来る");
            Assert.AreEqual("genreOnly", related[1], "ジャンル(2 点) > タグ(1 点)");
            Assert.AreEqual("tagOnly", related[2]);
        }

        [Test]
        public void TheNumberOfRelatedIsCapped()
        {
            var draft = new CatalogDraft();
            for (int i = 0; i < 20; i++) draft.Add(Item("id" + i, "曲" + i, "みどり", "音楽"));

            var generator = new RelatedIdGenerator();
            generator.MaxPerItem = 4;
            generator.Generate(draft.Items);

            Assert.AreEqual(4, draft.GetAt(0).RelatedIds.Length);
        }

        [Test]
        public void AnItemIsNeverRelatedToItself()
        {
            var draft = Draft(Item("a", "あ", "みどり", "音楽"), Item("b", "い", "みどり", "音楽"));

            new RelatedIdGenerator().Generate(draft.Items);

            Assert.AreEqual(1, draft.GetAt(0).RelatedIds.Length);
            Assert.AreNotEqual("a", draft.GetAt(0).RelatedIds[0]);
        }

        [Test]
        public void HandWrittenRelatedIsLeftAlone()
        {
            var mine = Item("a", "あ", "みどり", "音楽");
            mine.RelatedIds = new[] { "手で入れた" };

            var draft = Draft(mine, Item("b", "い", "みどり", "音楽"));

            new RelatedIdGenerator().Generate(draft.Items);

            Assert.AreEqual(1, draft.GetAt(0).RelatedIds.Length);
            Assert.AreEqual("手で入れた", draft.GetAt(0).RelatedIds[0],
                            "既定では手で書いたものを壊さない");
        }

        [Test]
        public void OverwriteRebuildsEverything()
        {
            var mine = Item("a", "あ", "みどり", "音楽");
            mine.RelatedIds = new[] { "手で入れた" };

            var draft = Draft(mine, Item("b", "い", "みどり", "音楽"));

            var generator = new RelatedIdGenerator();
            generator.Overwrite = true;
            generator.Generate(draft.Items);

            Assert.AreEqual("b", draft.GetAt(0).RelatedIds[0], "頼んだときだけ作り直す");
        }

        [Test]
        public void EachSourceOfClosenessCanBeTurnedOff()
        {
            var draft = Draft(
                Item("a", "あ", "みどり", "音楽"),
                Item("b", "い", "みどり", "ゲーム"));

            var generator = new RelatedIdGenerator();
            generator.UseChannel = false;
            generator.Generate(draft.Items);

            Assert.AreEqual(0, draft.GetAt(0).RelatedIds.Length,
                            "チャンネルを見なければ、これらに共通点は無い");
        }

        [Test]
        public void TagsAreCountedOnePerSharedTag()
        {
            var generator = new RelatedIdGenerator();
            generator.UseChannel = false;
            generator.UseGenre = false;

            CatalogDraftItem left = Item("a", "あ", "みどり", "音楽", "朝", "静か", "朝");
            CatalogDraftItem right = Item("b", "い", "あお", "ゲーム", "朝", "静か");

            Assert.AreEqual(2, generator.Score(left, right),
                            "同じタグが 2 回書いてあっても 1 回ぶん");
        }

        [Test]
        public void TheResultDoesNotDependOnTheOrderItIsWritten()
        {
            var draft = new CatalogDraft();
            for (int i = 0; i < 6; i++) draft.Add(Item("id" + i, "曲" + i, "みどり", "音楽"));

            var first = new RelatedIdGenerator();
            first.MaxPerItem = 3;
            first.Generate(draft.Items);

            string[] before = draft.GetAt(3).RelatedIds;

            // もう一度上書きで作り直しても、同じ入力なら同じ結果になること。
            var again = new RelatedIdGenerator();
            again.MaxPerItem = 3;
            again.Overwrite = true;
            again.Generate(draft.Items);

            Assert.AreEqual(before.Length, draft.GetAt(3).RelatedIds.Length);
            for (int i = 0; i < before.Length; i++)
            {
                Assert.AreEqual(before[i], draft.GetAt(3).RelatedIds[i],
                                "書いた順で結果が変わらないこと");
            }
        }

        [Test]
        public void RowsWithoutAnIdAreSkipped()
        {
            var nameless = Item("", "ID なし", "みどり", "音楽");

            var draft = Draft(Item("a", "あ", "みどり", "音楽"), nameless);

            new RelatedIdGenerator().Generate(draft.Items);

            Assert.AreEqual(0, draft.GetAt(0).RelatedIds.Length, "ID の無いものは関連にできない");
            Assert.AreEqual(0, draft.GetAt(1).RelatedIds.Length);
        }

        [Test]
        public void TheGeneratorIsSafeOnNothing()
        {
            var generator = new RelatedIdGenerator();

            Assert.AreEqual(0, generator.Generate(null));
            Assert.AreEqual(0, generator.Suggest(null, 0).Length);
            Assert.AreEqual(0, generator.Score(null, null));
        }
    }
}
