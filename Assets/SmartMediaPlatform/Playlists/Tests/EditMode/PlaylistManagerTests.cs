using System.Linq;
using NUnit.Framework;

namespace SmartMediaPlatform.Playlists.Tests
{
    /// <summary>PlaylistManager の作成・削除・複製・保存・読み込みの検証。</summary>
    public sealed class PlaylistManagerTests
    {
        private PlaylistManager _manager;

        [SetUp]
        public void SetUp()
        {
            _manager = new PlaylistManager();
        }

        // ───────── 作成 / 削除 ─────────

        [Test]
        public void Create_AddsPlaylistWithGeneratedId()
        {
            var playlist = _manager.Create("朝の一曲");

            Assert.IsNotNull(playlist);
            Assert.AreEqual("朝の一曲", playlist.Name);
            Assert.IsNotEmpty(playlist.Id);
            Assert.AreEqual(1, _manager.Count);
        }

        [Test]
        public void Create_GeneratesUniqueIds()
        {
            var a = _manager.Create("A");
            var b = _manager.Create("B");

            Assert.AreNotEqual(a.Id, b.Id);
        }

        [Test]
        public void Create_WithExplicitId()
        {
            var playlist = _manager.Create("A", "my-list");

            Assert.AreEqual("my-list", playlist.Id);
            Assert.AreSame(playlist, _manager.Get("my-list"));
        }

        [Test]
        public void Create_WithDuplicateId_Fails()
        {
            _manager.Create("A", "same");

            Assert.IsNull(_manager.Create("B", "same"));
            Assert.AreEqual(1, _manager.Count);
        }

        [Test]
        public void Delete_RemovesPlaylist()
        {
            var playlist = _manager.Create("A");

            Assert.IsTrue(_manager.Delete(playlist.Id));
            Assert.AreEqual(0, _manager.Count);
            Assert.IsFalse(_manager.Delete(playlist.Id), "もう無い");
            Assert.IsNull(_manager.Get(playlist.Id));
        }

        [Test]
        public void Delete_ByInstance()
        {
            var playlist = _manager.Create("A");

            Assert.IsTrue(_manager.Delete(playlist));
            Assert.AreEqual(0, _manager.Count);
        }

        // ───────── 取得 ─────────

        [Test]
        public void Get_IsCaseInsensitive()
        {
            _manager.Create("A", "My-List");

            Assert.IsNotNull(_manager.Get("my-list"));
            Assert.IsTrue(_manager.Contains("MY-LIST"));
            Assert.IsNull(_manager.Get("no-such"));
            Assert.IsNull(_manager.Get(null));
        }

        [Test]
        public void GetByName_FindsPlaylist()
        {
            _manager.Create("夜のBGM");

            Assert.IsNotNull(_manager.GetByName("夜のBGM"));
            Assert.IsNull(_manager.GetByName("無い名前"));
        }

        [Test]
        public void Playlists_IsLiveReadOnlyView()
        {
            var view = _manager.Playlists;
            Assert.AreEqual(0, view.Count);

            _manager.Create("A");
            Assert.AreEqual(1, view.Count);
        }

        // ───────── 複製 ─────────

        [Test]
        public void Duplicate_CreatesIndependentCopy()
        {
            var source = _manager.Create("元", "src");
            source.AddRange(new[] { "music-001", "music-002" });

            var copy = _manager.Duplicate("src", "複製");

            Assert.IsNotNull(copy);
            Assert.AreEqual("複製", copy.Name);
            Assert.AreNotEqual(source.Id, copy.Id);
            CollectionAssert.AreEqual(source.MediaIds.ToArray(), copy.MediaIds.ToArray());
            Assert.AreEqual(2, _manager.Count);

            copy.Add("music-003");
            Assert.AreEqual(2, source.Count, "複製を編集しても元は変わらない");
        }

        [Test]
        public void Duplicate_UnknownId_ReturnsNull()
        {
            Assert.IsNull(_manager.Duplicate("no-such"));
        }

        // ───────── 保存 / 読み込み ─────────

        [Test]
        public void SaveAndLoad_RoundTrips()
        {
            var a = _manager.Create("朝", "morning");
            a.AddRange(new[] { "music-001", "music-002" });
            var b = _manager.Create("夜", "night");
            b.AddRange(new[] { "music-003" });

            var store = new InMemoryPlaylistStore();
            Assert.IsTrue(_manager.Save(store));

            var restored = new PlaylistManager();
            Assert.IsTrue(restored.Load(store));

            Assert.AreEqual(2, restored.Count);

            var morning = restored.Get("morning");
            Assert.IsNotNull(morning);
            Assert.AreEqual("朝", morning.Name);
            CollectionAssert.AreEqual(new[] { "music-001", "music-002" }, morning.MediaIds.ToArray());

            var night = restored.Get("night");
            Assert.IsNotNull(night);
            CollectionAssert.AreEqual(new[] { "music-003" }, night.MediaIds.ToArray());
        }

        [Test]
        public void Load_ReplacesCurrentContent()
        {
            _manager.Create("残ってはいけない", "old");

            var other = new PlaylistManager();
            other.Create("新しい", "new-one");
            var store = new InMemoryPlaylistStore();
            other.Save(store);

            _manager.Load(store);

            Assert.AreEqual(1, _manager.Count);
            Assert.IsNull(_manager.Get("old"));
            Assert.IsNotNull(_manager.Get("new-one"));
        }

        [Test]
        public void Load_WithoutContent_ReturnsFalse()
        {
            Assert.IsFalse(_manager.Load(new InMemoryPlaylistStore()));
            Assert.IsFalse(_manager.Load(null));
        }

        [Test]
        public void Save_EmptyManager_RoundTripsToEmpty()
        {
            var store = new InMemoryPlaylistStore();
            _manager.Save(store);

            var restored = new PlaylistManager();
            restored.Load(store);

            Assert.AreEqual(0, restored.Count);
        }

        [Test]
        public void Load_AfterLoad_KeepsGeneratedIdsUnique()
        {
            var created = _manager.Create("A");     // playlist-001
            var store = new InMemoryPlaylistStore();
            _manager.Save(store);

            var restored = new PlaylistManager();
            restored.Load(store);
            var extra = restored.Create("B");

            Assert.AreNotEqual(created.Id, extra.Id,
                "読み込み後に採番しても既存 ID とぶつからない");
        }

        [Test]
        public void Serializer_IgnoresBrokenLines()
        {
            var playlists = PlaylistSerializer.Deserialize(
                "SMP-PLAYLIST\t1\nこれは壊れた行\nP\tok\t名前\nT\tmusic-001\nT\n");

            Assert.AreEqual(1, playlists.Count);
            Assert.AreEqual("ok", playlists[0].Id);
            CollectionAssert.AreEqual(new[] { "music-001" }, playlists[0].MediaIds.ToArray());
        }

        [Test]
        public void Serializer_SanitizesSeparators()
        {
            var manager = new PlaylistManager();
            manager.Create("タブ\t入り\n名前", "id-1");

            var store = new InMemoryPlaylistStore();
            manager.Save(store);

            var restored = new PlaylistManager();
            restored.Load(store);

            Assert.AreEqual(1, restored.Count, "区切り文字が混ざっても壊れない");
        }
    }
}
