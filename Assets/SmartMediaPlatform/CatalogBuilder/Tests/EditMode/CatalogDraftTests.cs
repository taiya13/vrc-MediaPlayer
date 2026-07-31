using NUnit.Framework;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.CatalogBuilder;

namespace SmartMediaPlatform.CatalogBuilder.Tests
{
    /// <summary>
    /// Phase6-1: Catalog Builder の判断部分の検証。
    ///
    /// 窓(<c>EditorWindow</c>)は表示と入力だけなので触りません。
    /// <b>足す・消す・混ぜる・焼く</b>の判断は
    /// <see cref="CatalogDraft"/> に寄せてあるので、ここで確かめます。
    /// </summary>
    public sealed class CatalogDraftTests
    {
        private static CatalogDraftItem Item(string id, string title, string url)
        {
            var item = new CatalogDraftItem();
            item.Id = id;
            item.Title = title;
            item.Url = url;
            return item;
        }

        private static CatalogDraft Draft(params CatalogDraftItem[] items)
        {
            var draft = new CatalogDraft();
            for (int i = 0; i < items.Length; i++) draft.Add(items[i]);
            return draft;
        }

        // ───────── 作りかけを許す ─────────

        [Test]
        public void AnEmptyRowCanExistWhileEditing()
        {
            var draft = new CatalogDraft();
            int at = draft.Add();

            Assert.AreEqual(0, at);
            Assert.AreEqual(1, draft.Count, "空の行を置ける(MediaItem は空だと例外)");
            Assert.IsFalse(draft.GetAt(0).IsComplete);
        }

        [Test]
        public void OnlyCompleteRowsReachThePlayer()
        {
            var draft = Draft(
                Item("a", "あ", "http://example.com/a"),
                Item("", "名前だけ", "http://example.com/b"),
                Item("c", "う", ""));

            MediaItem[] items = draft.ToMediaItems();

            Assert.AreEqual(1, items.Length, "作りかけは再生側へ流さない");
            Assert.AreEqual("a", items[0].Id);
        }

        [Test]
        public void AnIncompleteRowSaysWhatIsMissing()
        {
            Assert.AreEqual("ID が空です", Item("", "あ", "u").Describe());
            Assert.AreEqual("見出しが空です", Item("a", "", "u").Describe());
            Assert.AreEqual("URL が空です", Item("a", "あ", "").Describe());
            Assert.AreEqual("", Item("a", "あ", "u").Describe());
        }

        // ───────── ID が重ならない ─────────

        [Test]
        public void DuplicateIdsAreReported()
        {
            var draft = Draft(
                Item("same", "1", "u"),
                Item("other", "2", "u"),
                Item("same", "3", "u"));

            CollectionAssert.AreEqual(new[] { "same" }, draft.FindDuplicateIds());
            Assert.IsFalse(draft.CanBuild, "重なったままでは焼かせない");
        }

        [Test]
        public void AUniqueIdIsFoundByAddingASuffix()
        {
            var draft = Draft(Item("song", "1", "u"), Item("song-2", "2", "u"));

            Assert.AreEqual("song-3", draft.MakeUniqueId("song"));
            Assert.AreEqual("free", draft.MakeUniqueId("free"), "空いていればそのまま");
        }

        [Test]
        public void DuplicatingARowGivesItANewId()
        {
            var draft = Draft(Item("song", "1", "u"));

            int at = draft.Duplicate(0);

            Assert.AreEqual(1, at, "すぐ下に入る");
            Assert.AreEqual("song-2", draft.GetAt(1).Id);
            Assert.AreEqual(0, draft.FindDuplicateIds().Length);
        }

        // ───────── 並べ替え ─────────

        [Test]
        public void RowsCanBeMovedUpAndDown()
        {
            var draft = Draft(Item("a", "1", "u"), Item("b", "2", "u"));

            Assert.IsTrue(draft.Move(1, -1));
            Assert.AreEqual("b", draft.GetAt(0).Id);

            Assert.IsFalse(draft.Move(0, -1), "端より外へは動かない");
            Assert.IsFalse(draft.Move(1, 1));
        }

        // ───────── 取り込みの混ぜ方 ─────────

        [Test]
        public void AppendKeepsBothAndAvoidsIdClashes()
        {
            var draft = Draft(Item("a", "元", "u"));
            var incoming = new[] { Item("a", "新", "u2") };

            int changed = draft.Merge(incoming, CatalogDraft.MergeAppend);

            Assert.AreEqual(1, changed);
            Assert.AreEqual(2, draft.Count);
            Assert.AreEqual("a-2", draft.GetAt(1).Id, "同じ ID が並ぶと再生側が別のものを指す");
            Assert.AreEqual(0, draft.FindDuplicateIds().Length);
        }

        [Test]
        public void UpdateOverwritesTheMatchingId()
        {
            var draft = Draft(Item("a", "元", "u"), Item("b", "残る", "u"));
            var incoming = new[] { Item("a", "新", "u2") };

            int changed = draft.Merge(incoming, CatalogDraft.MergeUpdate);

            Assert.AreEqual(1, changed);
            Assert.AreEqual(2, draft.Count, "件数は増えない");
            Assert.AreEqual("新", draft.GetAt(0).Title);
            Assert.AreEqual("残る", draft.GetAt(1).Title);
        }

        [Test]
        public void UpdateAddsWhenTheIdIsNew()
        {
            var draft = Draft(Item("a", "元", "u"));

            draft.Merge(new[] { Item("z", "新顔", "u") }, CatalogDraft.MergeUpdate);

            Assert.AreEqual(2, draft.Count);
            Assert.AreEqual("z", draft.GetAt(1).Id);
        }

        [Test]
        public void ReplaceThrowsAwayWhatWasThere()
        {
            var draft = Draft(Item("a", "元", "u"), Item("b", "元2", "u"));

            draft.Merge(new[] { Item("z", "新", "u") }, CatalogDraft.MergeReplace);

            Assert.AreEqual(1, draft.Count);
            Assert.AreEqual("z", draft.GetAt(0).Id);
        }

        [Test]
        public void MergingNothingIsSafe()
        {
            var draft = Draft(Item("a", "元", "u"));

            Assert.AreEqual(0, draft.Merge(null, CatalogDraft.MergeAppend));
            Assert.AreEqual(1, draft.Count);
        }

        [Test]
        public void MergedItemsAreCopiedNotShared()
        {
            var draft = new CatalogDraft();
            var source = Item("a", "元", "u");

            draft.Merge(new[] { source }, CatalogDraft.MergeAppend);
            source.Title = "あとから書き換えた";

            Assert.AreEqual("元", draft.GetAt(0).Title, "取り込み元を握り続けない");
        }

        // ───────── 既存カタログを読む ─────────

        [Test]
        public void LoadingAnExistingCatalogReplacesEverything()
        {
            var draft = Draft(Item("old", "捨てる", "u"));

            draft.LoadFrom(new[]
            {
                new MediaItem("m1", "読んだ 1", "作者", MediaType.Video, "pop",
                              new[] { "tag" }, "http://example.com/1", 120, new[] { "m2" }),
                new MediaItem("m2", "読んだ 2", "作者", MediaType.Video),
            });

            Assert.AreEqual(2, draft.Count);
            Assert.AreEqual("m1", draft.GetAt(0).Id);
            Assert.AreEqual(120, draft.GetAt(0).DurationSeconds);
            CollectionAssert.AreEqual(new[] { "m2" }, draft.GetAt(0).RelatedIds);
        }

        [Test]
        public void ARoundTripKeepsTheContent()
        {
            var draft = new CatalogDraft();
            draft.LoadFrom(new[]
            {
                new MediaItem("m1", "見出し", "作者", MediaType.Video, "pop",
                              new[] { "a", "b" }, "http://example.com/1", 90, new[] { "m2" }),
            });

            MediaItem[] back = draft.ToMediaItems();

            Assert.AreEqual(1, back.Length);
            Assert.AreEqual("m1", back[0].Id);
            Assert.AreEqual("見出し", back[0].Title);
            Assert.AreEqual(90, back[0].DurationSeconds);
            CollectionAssert.AreEqual(new[] { "a", "b" }, back[0].Tags);
        }

        [Test]
        public void EmptyTagsAreDroppedOnTheWayOut()
        {
            var item = Item("a", "あ", "u");
            item.Tags = new[] { "keep", "", "  ", "also" };

            MediaItem media = item.ToMediaItem();

            CollectionAssert.AreEqual(new[] { "keep", "also" }, media.Tags,
                                      "取り込み元が付ける空欄をそのまま焼かない");
        }

        [Test]
        public void WhitespaceAroundIdsAndUrlsIsTrimmed()
        {
            MediaItem media = Item("  a  ", "  あ  ", "  http://x  ").ToMediaItem();

            Assert.AreEqual("a", media.Id);
            Assert.AreEqual("あ", media.Title);
            Assert.AreEqual("http://x", media.Url);
        }

        // ───────── 焼いてよいか ─────────

        [Test]
        public void AnEmptyDraftCannotBeBuilt()
        {
            Assert.IsFalse(new CatalogDraft().CanBuild);
        }

        [Test]
        public void ADraftWithOneCompleteRowCanBeBuilt()
        {
            var draft = Draft(Item("a", "あ", "u"), Item("", "作りかけ", ""));

            Assert.IsTrue(draft.CanBuild, "作りかけが混ざっていても、完成が 1 件あれば焼ける");
            Assert.AreEqual(1, draft.CompleteCount);
        }

        // ───────── Builder は再生側を知らない ─────────

        [Test]
        public void TheDraftNeverTouchesTheRuntime()
        {
            var assembly = typeof(CatalogDraft).Assembly;
            var referenced = assembly.GetReferencedAssemblies();

            foreach (var name in referenced)
            {
                Assert.AreNotEqual("SmartMediaPlatform.World", name.Name,
                                   "Builder は MediaPlayer 本体に依存しない");
                Assert.AreNotEqual("SmartMediaPlatform.World.VRChat", name.Name);
            }
        }
    }
}
