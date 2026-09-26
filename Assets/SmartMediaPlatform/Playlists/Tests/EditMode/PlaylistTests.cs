using System;
using System.Linq;
using NUnit.Framework;

namespace SmartMediaPlatform.Playlists.Tests
{
    /// <summary>Playlist の編集操作(追加・削除・移動・並び替え・複製)の検証。</summary>
    public sealed class PlaylistTests
    {
        private Playlist _playlist;

        [SetUp]
        public void SetUp()
        {
            _playlist = new Playlist("pl-1", "お気に入り");
        }

        private void Fill(params string[] ids)
        {
            foreach (var id in ids) _playlist.Add(id);
        }

        private string[] Ids() => _playlist.MediaIds.ToArray();

        // ───────── 生成 ─────────

        [Test]
        public void NewPlaylist_IsEmpty()
        {
            Assert.AreEqual(0, _playlist.Count);
            Assert.IsTrue(_playlist.IsEmpty);
            Assert.AreEqual("pl-1", _playlist.Id);
            Assert.AreEqual("お気に入り", _playlist.Name);
        }

        [Test]
        public void Constructor_RequiresId()
        {
            Assert.Throws<ArgumentException>(() => new Playlist(""));
            Assert.Throws<ArgumentException>(() => new Playlist(null));
        }

        [Test]
        public void Name_DefaultsToId()
        {
            Assert.AreEqual("pl-2", new Playlist("pl-2").Name);
        }

        // ───────── 曲追加 ─────────

        [Test]
        public void Add_AppendsInOrder()
        {
            Fill("music-001", "music-002", "music-003");

            CollectionAssert.AreEqual(
                new[] { "music-001", "music-002", "music-003" }, Ids());
            Assert.AreEqual(3, _playlist.Count);
        }

        [Test]
        public void Add_RejectsDuplicatesAndEmpty()
        {
            Assert.IsTrue(_playlist.Add("music-001"));
            Assert.IsFalse(_playlist.Add("music-001"), "同じ曲は 2 度入らない");
            Assert.IsFalse(_playlist.Add(""));
            Assert.IsFalse(_playlist.Add(null));
            Assert.AreEqual(1, _playlist.Count);
        }

        [Test]
        public void AddRange_AddsEverythingNew()
        {
            _playlist.Add("music-001");

            int added = _playlist.AddRange(new[] { "music-001", "music-002", "music-003" });

            Assert.AreEqual(2, added, "すでにある曲は数えない");
            Assert.AreEqual(3, _playlist.Count);
        }

        [Test]
        public void Insert_PlacesAtIndex()
        {
            Fill("music-001", "music-003");

            Assert.IsTrue(_playlist.Insert(1, "music-002"));
            CollectionAssert.AreEqual(
                new[] { "music-001", "music-002", "music-003" }, Ids());
        }

        [Test]
        public void Insert_RejectsOutOfRange()
        {
            Fill("music-001");

            Assert.IsFalse(_playlist.Insert(-1, "music-002"));
            Assert.IsFalse(_playlist.Insert(5, "music-002"));
            Assert.IsTrue(_playlist.Insert(1, "music-002"), "末尾への挿入は有効");
        }

        // ───────── 曲削除 ─────────

        [Test]
        public void Remove_ById()
        {
            Fill("music-001", "music-002", "music-003");

            Assert.IsTrue(_playlist.Remove("music-002"));
            CollectionAssert.AreEqual(new[] { "music-001", "music-003" }, Ids());
            Assert.IsFalse(_playlist.Remove("music-002"), "もう無い");
        }

        [Test]
        public void RemoveAt_ChecksBounds()
        {
            Fill("music-001", "music-002");

            Assert.IsTrue(_playlist.RemoveAt(0));
            Assert.IsFalse(_playlist.RemoveAt(5));
            Assert.IsFalse(_playlist.RemoveAt(-1));
            CollectionAssert.AreEqual(new[] { "music-002" }, Ids());
        }

        [Test]
        public void Clear_EmptiesPlaylist()
        {
            Fill("music-001", "music-002");
            _playlist.Clear();

            Assert.IsTrue(_playlist.IsEmpty);
        }

        // ───────── 曲移動 ─────────

        [Test]
        public void Move_ReordersTracks()
        {
            Fill("music-001", "music-002", "music-003", "music-004");

            Assert.IsTrue(_playlist.Move(0, 2));
            CollectionAssert.AreEqual(
                new[] { "music-002", "music-003", "music-001", "music-004" }, Ids());

            Assert.IsTrue(_playlist.Move(3, 0));
            CollectionAssert.AreEqual(
                new[] { "music-004", "music-002", "music-003", "music-001" }, Ids());
        }

        [Test]
        public void Move_RejectsInvalidArguments()
        {
            Fill("music-001", "music-002");

            Assert.IsFalse(_playlist.Move(0, 0));
            Assert.IsFalse(_playlist.Move(-1, 1));
            Assert.IsFalse(_playlist.Move(0, 9));
        }

        // ───────── 並び替え ─────────

        [Test]
        public void Reorder_AppliesGivenOrder()
        {
            Fill("music-001", "music-002", "music-003");

            Assert.IsTrue(_playlist.Reorder(new[] { "music-003", "music-001", "music-002" }));
            CollectionAssert.AreEqual(
                new[] { "music-003", "music-001", "music-002" }, Ids());
        }

        [Test]
        public void Reorder_KeepsTracksMissingFromTheGivenOrder()
        {
            Fill("music-001", "music-002", "music-003");

            _playlist.Reorder(new[] { "music-003" });

            CollectionAssert.AreEqual(
                new[] { "music-003", "music-001", "music-002" }, Ids(),
                "指定に無い曲は元の順のまま残る(曲を失わない)");
        }

        [Test]
        public void Reorder_IgnoresUnknownIds()
        {
            Fill("music-001", "music-002");

            _playlist.Reorder(new[] { "no-such-id", "music-002", "music-001" });

            CollectionAssert.AreEqual(new[] { "music-002", "music-001" }, Ids());
        }

        [Test]
        public void Reverse_FlipsOrder()
        {
            Fill("music-001", "music-002", "music-003");
            _playlist.Reverse();

            CollectionAssert.AreEqual(
                new[] { "music-003", "music-002", "music-001" }, Ids());
        }

        [Test]
        public void Shuffle_KeepsEveryTrack()
        {
            Fill("music-001", "music-002", "music-003", "music-004", "music-005");

            _playlist.Shuffle(new Random(1));

            Assert.AreEqual(5, _playlist.Count);
            CollectionAssert.AreEquivalent(
                new[] { "music-001", "music-002", "music-003", "music-004", "music-005" }, Ids());
        }

        [Test]
        public void Shuffle_WithSameSeed_IsDeterministic()
        {
            var a = new Playlist("a");
            var b = new Playlist("b");
            foreach (var id in new[] { "1", "2", "3", "4", "5", "6" }) { a.Add(id); b.Add(id); }

            a.Shuffle(new Random(7));
            b.Shuffle(new Random(7));

            CollectionAssert.AreEqual(a.MediaIds.ToArray(), b.MediaIds.ToArray());
        }

        // ───────── 検索 / 複製 ─────────

        [Test]
        public void Contains_And_IndexOf_AreCaseInsensitive()
        {
            Fill("music-001", "music-002");

            Assert.IsTrue(_playlist.Contains("MUSIC-001"));
            Assert.AreEqual(1, _playlist.IndexOf("Music-002"));
            Assert.AreEqual(-1, _playlist.IndexOf("no-such"));
            Assert.IsFalse(_playlist.Contains(null));
        }

        [Test]
        public void GetAt_ReturnsNullOutOfRange()
        {
            Fill("music-001");

            Assert.AreEqual("music-001", _playlist.GetAt(0));
            Assert.IsNull(_playlist.GetAt(1));
            Assert.IsNull(_playlist.GetAt(-1));
        }

        [Test]
        public void Duplicate_CopiesTracksButNotIdentity()
        {
            Fill("music-001", "music-002");

            var copy = _playlist.Duplicate("pl-copy", "コピー");

            Assert.AreEqual("pl-copy", copy.Id);
            Assert.AreEqual("コピー", copy.Name);
            CollectionAssert.AreEqual(Ids(), copy.MediaIds.ToArray());

            copy.Add("music-003");
            Assert.AreEqual(2, _playlist.Count, "複製を編集しても元は変わらない");
        }

        [Test]
        public void MediaIds_IsLiveReadOnlyView()
        {
            var view = _playlist.MediaIds;
            Assert.AreEqual(0, view.Count);

            _playlist.Add("music-001");
            Assert.AreEqual(1, view.Count);
        }
    }
}
