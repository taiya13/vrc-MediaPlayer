using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Store;
using SmartMediaPlatform.Video.Data;

namespace SmartMediaPlatform.Library.Tests
{
    /// <summary>
    /// Phase4-1: 閲覧と選択(<see cref="MediaLibrary"/>)の検証。
    ///
    /// 音楽と動画が混ざった <see cref="MixedCatalogSource"/> を使うので、
    /// 「Music / Video を表示できる」ことをそのまま確かめられます。
    /// </summary>
    public sealed class MediaLibraryTests
    {
        private IMediaCatalog _catalog;
        private CatalogStore _store;
        private MediaLibrary _library;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new MixedCatalogSource(), new System.Random(1));
            _store = new CatalogStore(_catalog);
            _library = new MediaLibrary(_store);
        }

        private string[] Ids() => _library.Entries.Select(x => x.MediaId).ToArray();

        // ───────── 前提 ─────────

        [Test]
        public void Constructor_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new MediaLibrary((ICatalogStore)null));
        }

        [Test]
        public void Library_IsTheBrowseContract()
        {
            Assert.IsInstanceOf<IMediaLibrary>(_library);
        }

        [Test]
        public void NewLibrary_ShowsEverythingInCatalogOrder()
        {
            Assert.AreEqual(_store.Count, _library.Count);
            CollectionAssert.AreEqual(
                _store.GetAllIds().ToArray(), Ids(),
                "既定はカタログの登録順そのまま");
            Assert.AreEqual(LibrarySortOrder.CatalogOrder, _library.SortOrder);
        }

        [Test]
        public void NewLibrary_HasNothingSelected()
        {
            Assert.IsFalse(_library.HasSelection);
            Assert.AreEqual(-1, _library.SelectedIndex);
            Assert.IsNull(_library.SelectedItem);
            Assert.IsNull(_library.SelectedMediaId);
        }

        [Test]
        public void Constructor_CanStartFilteredToOneType()
        {
            var videos = new MediaLibrary(_store, MediaType.Video);

            Assert.Greater(videos.Count, 0);
            foreach (var item in videos.Entries) Assert.AreEqual(MediaType.Video, item.Type);
        }

        // ───────── 閲覧: Music / Video ─────────

        [Test]
        public void ShowOnly_Video_KeepsOnlyVideos()
        {
            _library.ShowOnly(MediaType.Video);

            Assert.Greater(_library.Count, 0);
            foreach (var item in _library.Entries) Assert.AreEqual(MediaType.Video, item.Type);
        }

        [Test]
        public void ShowOnly_Music_KeepsOnlyMusic()
        {
            _library.ShowOnly(MediaType.Music);

            Assert.Greater(_library.Count, 0);
            foreach (var item in _library.Entries) Assert.AreEqual(MediaType.Music, item.Type);
        }

        [Test]
        public void ShowOnly_AcceptsSeveralTypes()
        {
            _library.ShowOnly(MediaType.Music, MediaType.Video);

            foreach (var item in _library.Entries)
            {
                Assert.IsTrue(item.Type == MediaType.Music || item.Type == MediaType.Video);
            }
            Assert.IsTrue(_library.IsVisible(MediaType.Music));
            Assert.IsTrue(_library.IsVisible(MediaType.Video));
            Assert.IsFalse(_library.IsVisible(MediaType.Podcast));
        }

        [Test]
        public void MusicPlusVideo_EqualsTheirSeparateCounts()
        {
            _library.ShowOnly(MediaType.Music);
            int music = _library.Count;

            _library.ShowOnly(MediaType.Video);
            int video = _library.Count;

            _library.ShowOnly(MediaType.Music, MediaType.Video);

            Assert.AreEqual(music + video, _library.Count);
        }

        [Test]
        public void ShowAll_BringsEverythingBack()
        {
            _library.ShowOnly(MediaType.Video);
            _library.ShowAll();

            Assert.AreEqual(_store.Count, _library.Count);
        }

        [Test]
        public void ShowOnly_WithNoArgumentsMeansShowAll()
        {
            _library.ShowOnly(MediaType.Video);
            _library.ShowOnly();

            Assert.AreEqual(_store.Count, _library.Count);
        }

        [Test]
        public void ShowOnly_CanEndUpEmpty()
        {
            _library.ShowOnly(MediaType.Live);   // このカタログに生配信は 1 件も無い

            Assert.AreEqual(0, _library.Count);
            Assert.IsFalse(_library.HasSelection);
        }

        // ───────── 閲覧: 並び順 ─────────

        [Test]
        public void SortByTitle_OrdersAlphabetically()
        {
            _library.SortOrder = LibrarySortOrder.Title;

            var titles = _library.Entries.Select(x => x.Title).ToArray();
            CollectionAssert.AreEqual(
                titles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(), titles);
        }

        [Test]
        public void SortByArtist_GroupsTheSameArtistTogether()
        {
            _library.SortOrder = LibrarySortOrder.Artist;

            var artists = _library.Entries.Select(x => x.Artist).ToArray();
            CollectionAssert.AreEqual(
                artists.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(), artists);
        }

        [Test]
        public void SortByGenre_OrdersByGenre()
        {
            _library.SortOrder = LibrarySortOrder.Genre;

            var genres = _library.Entries.Select(x => x.Genre).ToArray();
            CollectionAssert.AreEqual(
                genres.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(), genres);
        }

        [Test]
        public void SortByDuration_OrdersShortestFirst()
        {
            _library.SortOrder = LibrarySortOrder.Duration;

            var durations = _library.Entries.Select(x => x.DurationSeconds).ToArray();
            CollectionAssert.AreEqual(durations.OrderBy(x => x).ToArray(), durations);
        }

        [Test]
        public void ChangingSort_DoesNotChangeWhatIsShown()
        {
            _library.ShowOnly(MediaType.Video);
            int before = _library.Count;

            _library.SortOrder = LibrarySortOrder.Title;

            Assert.AreEqual(before, _library.Count);
            foreach (var item in _library.Entries) Assert.AreEqual(MediaType.Video, item.Type);
        }

        [Test]
        public void SortingIsStableAcrossRepeatedApplications()
        {
            _library.SortOrder = LibrarySortOrder.Artist;
            string[] first = Ids();

            _library.SortOrder = LibrarySortOrder.CatalogOrder;
            _library.SortOrder = LibrarySortOrder.Artist;

            CollectionAssert.AreEqual(first, Ids());
        }

        // ───────── 選択 ─────────

        [Test]
        public void Select_PicksByIndex()
        {
            Assert.IsTrue(_library.Select(2));

            Assert.AreEqual(2, _library.SelectedIndex);
            Assert.AreSame(_library.GetAt(2), _library.SelectedItem);
            Assert.AreEqual(_library.GetAt(2).MediaId, _library.SelectedMediaId);
            Assert.IsTrue(_library.HasSelection);
        }

        [Test]
        public void Select_RejectsOutOfRange()
        {
            Assert.IsFalse(_library.Select(-1));
            Assert.IsFalse(_library.Select(_library.Count));
            Assert.IsFalse(_library.HasSelection);
        }

        [Test]
        public void SelectById_PicksByMediaId()
        {
            _library.ShowOnly(MediaType.Video);

            Assert.IsTrue(_library.SelectById("video-003"));
            Assert.AreEqual("video-003", _library.SelectedMediaId);
        }

        [Test]
        public void SelectById_IsCaseInsensitive()
        {
            _library.ShowOnly(MediaType.Video);

            Assert.IsTrue(_library.SelectById("VIDEO-003"));
            Assert.AreEqual("video-003", _library.SelectedMediaId);
        }

        [Test]
        public void SelectById_FailsForSomethingNotShown()
        {
            _library.ShowOnly(MediaType.Video);

            Assert.IsFalse(_library.SelectById("music-001"),
                "絞り込みで隠れているものは選べない");
            Assert.IsFalse(_library.HasSelection);
        }

        [Test]
        public void SelectNext_MovesForwardAndWraps()
        {
            _library.Select(_library.Count - 1);

            Assert.IsTrue(_library.SelectNext());
            Assert.AreEqual(0, _library.SelectedIndex, "末尾の次は先頭へ戻る");
        }

        [Test]
        public void SelectNext_CanStopAtTheEnd()
        {
            _library.Select(_library.Count - 1);

            Assert.IsFalse(_library.SelectNext(wrap: false));
            Assert.AreEqual(_library.Count - 1, _library.SelectedIndex);
        }

        [Test]
        public void SelectNext_FromNothingPicksTheFirst()
        {
            Assert.IsTrue(_library.SelectNext());
            Assert.AreEqual(0, _library.SelectedIndex);
        }

        [Test]
        public void SelectPrevious_MovesBackAndWraps()
        {
            _library.Select(0);

            Assert.IsTrue(_library.SelectPrevious());
            Assert.AreEqual(_library.Count - 1, _library.SelectedIndex);
        }

        [Test]
        public void SelectPrevious_FromNothingPicksTheLast()
        {
            Assert.IsTrue(_library.SelectPrevious());
            Assert.AreEqual(_library.Count - 1, _library.SelectedIndex);
        }

        [Test]
        public void ClearSelection_LeavesNothingSelected()
        {
            _library.Select(1);
            _library.ClearSelection();

            Assert.IsFalse(_library.HasSelection);
            Assert.IsNull(_library.SelectedMediaId);
        }

        [Test]
        public void Selection_SurvivesASortChange()
        {
            _library.ShowOnly(MediaType.Video);
            _library.SelectById("video-005");

            _library.SortOrder = LibrarySortOrder.Title;

            Assert.AreEqual("video-005", _library.SelectedMediaId,
                "並び順が変わっても、選んでいたものは選ばれたまま");
        }

        [Test]
        public void Selection_IsDroppedWhenItIsFilteredOut()
        {
            _library.ShowAll();
            _library.SelectById("music-001");

            _library.ShowOnly(MediaType.Video);

            Assert.IsFalse(_library.HasSelection, "見えなくなったものは選択から外れる");
        }

        [Test]
        public void Selection_SurvivesAFilterThatStillContainsIt()
        {
            _library.ShowAll();
            _library.SelectById("video-002");

            _library.ShowOnly(MediaType.Video);

            Assert.AreEqual("video-002", _library.SelectedMediaId);
        }

        [Test]
        public void IndexOf_FindsWhatIsShown()
        {
            _library.ShowOnly(MediaType.Video);

            int index = _library.IndexOf("video-004");

            Assert.GreaterOrEqual(index, 0);
            Assert.AreEqual("video-004", _library.GetAt(index).MediaId);
            Assert.AreEqual(-1, _library.IndexOf("music-001"));
            Assert.AreEqual(-1, _library.IndexOf(null));
        }

        [Test]
        public void GetAt_ReturnsNullOutOfRange()
        {
            Assert.IsNull(_library.GetAt(-1));
            Assert.IsNull(_library.GetAt(_library.Count));
        }

        // ───────── 通知 ─────────

        [Test]
        public void Observer_HearsAboutFilterChanges()
        {
            var watcher = new Watcher();
            _library.AddObserver(watcher);

            _library.ShowOnly(MediaType.Video);

            Assert.AreEqual(1, watcher.LibraryChanged);
        }

        [Test]
        public void Observer_HearsAboutSelectionChanges()
        {
            var watcher = new Watcher();
            _library.AddObserver(watcher);

            _library.Select(1);

            Assert.AreEqual(1, watcher.SelectionChanged);
            Assert.AreSame(_library.GetAt(1), watcher.LastSelected);
        }

        [Test]
        public void Observer_IsToldWhenTheSelectionIsCleared()
        {
            var watcher = new Watcher();
            _library.Select(1);
            _library.AddObserver(watcher);

            _library.ClearSelection();

            Assert.AreEqual(1, watcher.SelectionChanged);
            Assert.IsNull(watcher.LastSelected);
        }

        [Test]
        public void Observer_IsNotToldWhenNothingChanges()
        {
            var watcher = new Watcher();
            _library.Select(1);
            _library.AddObserver(watcher);

            _library.Select(1);
            _library.SortOrder = LibrarySortOrder.CatalogOrder;

            Assert.AreEqual(0, watcher.SelectionChanged);
            Assert.AreEqual(0, watcher.LibraryChanged);
        }

        [Test]
        public void Observer_CanBeRemoved()
        {
            var watcher = new Watcher();
            _library.AddObserver(watcher);
            _library.RemoveObserver(watcher);

            _library.ShowOnly(MediaType.Video);

            Assert.AreEqual(0, watcher.LibraryChanged);
        }

        [Test]
        public void Observer_IsNotRegisteredTwice()
        {
            var watcher = new Watcher();
            _library.AddObserver(watcher);
            _library.AddObserver(watcher);

            _library.ShowOnly(MediaType.Video);

            Assert.AreEqual(1, watcher.LibraryChanged);
        }

        [Test]
        public void AddObserver_IgnoresNull()
        {
            Assert.DoesNotThrow(() => _library.AddObserver(null));
            Assert.DoesNotThrow(() => _library.RemoveObserver(null));
        }

        // ───────── 責務の境界 ─────────

        [Test]
        public void Library_NeverExposesAUrl()
        {
            // 上位へ渡してよいのは MediaId(string)だけ。
            // URL を取り出す口はこの型のどこにも無い。
            var type = typeof(MediaLibrary);

            Assert.IsNull(type.GetMethod("GetUrl"));
            Assert.IsNull(type.GetProperty("SelectedUrl"));
            Assert.IsNull(type.GetProperty("Url"));

            // Phase4-2: UI へ渡る型そのものに URL の口が無い
            Assert.IsNull(typeof(DisplayMeta).GetProperty("Url"),
                "DisplayMeta は表示用だけ — URL を持ってはいけない");
            Assert.IsInstanceOf<string>(_library.Select(0) ? _library.SelectedMediaId : "");
        }

        [Test]
        public void LibraryAssembly_DoesNotReferenceThePlaybackStack()
        {
            var referenced = typeof(MediaLibrary).Assembly
                .GetReferencedAssemblies().Select(x => x.Name).ToArray();

            CollectionAssert.DoesNotContain(referenced, "SmartMediaPlatform.Session",
                "Media Library は閲覧と選択だけ — 再生側は見えてはいけない");
            CollectionAssert.DoesNotContain(referenced, "SmartMediaPlatform.Queue");
            CollectionAssert.DoesNotContain(referenced, "SmartMediaPlatform.Backend");
            CollectionAssert.DoesNotContain(referenced, "SmartMediaPlatform.Player");
            CollectionAssert.DoesNotContain(referenced, "SmartMediaPlatform.Recommendation");
            CollectionAssert.Contains(referenced, "SmartMediaPlatform.Catalog",
                "見せる相手はカタログだけ");
        }

        [Test]
        public void LibraryAssembly_DoesNotReferenceUnityEngine()
        {
            var referenced = typeof(MediaLibrary).Assembly
                .GetReferencedAssemblies().Select(x => x.Name).ToArray();

            CollectionAssert.DoesNotContain(referenced, "UnityEngine",
                "純粋 C#(UI にも Unity にも依存しない)");
            CollectionAssert.DoesNotContain(referenced, "UnityEngine.CoreModule");
        }

        // ───────── 整形 ─────────

        [Test]
        public void Formatter_ShowsTitleArtistGenreAndTags()
        {
            _library.ShowOnly(MediaType.Video);
            var item = _library.GetAt(0);

            string line = MediaLibraryFormatter.FormatEntry(item, 1);

            StringAssert.Contains(item.Title, line);
            StringAssert.Contains(item.Artist, line);
            StringAssert.Contains(item.Genre, line);
            StringAssert.Contains("#" + item.Tags[0], line);
        }

        [Test]
        public void Formatter_NeverPrintsTheUrl()
        {
            _library.ShowOnly(MediaType.Video);
            _library.Select(0);
            string url = _catalog.FindById(_library.SelectedMediaId).Url;

            Assert.IsNotEmpty(url, "この検証はカタログに URL がある前提");
            StringAssert.DoesNotContain(url, MediaLibraryFormatter.FormatLibrary(_library));
            StringAssert.DoesNotContain(url, MediaLibraryFormatter.FormatSelection(_library));
            StringAssert.DoesNotContain(
                url, MediaLibraryFormatter.FormatEntry(_library.SelectedItem, 1));
        }

        [Test]
        public void Formatter_MarksTheSelectedRow()
        {
            _library.Select(1);

            string text = MediaLibraryFormatter.FormatLibrary(_library);
            string[] lines = text.Split('\n');

            Assert.IsTrue(lines[2].StartsWith(">"), "選択中の行に印が付く");
        }

        [Test]
        public void Formatter_HandlesAnEmptyLibrary()
        {
            _library.ShowOnly(MediaType.Live);

            StringAssert.Contains("表示できるメディアがありません",
                MediaLibraryFormatter.FormatLibrary(_library));
            Assert.AreEqual("選択なし", MediaLibraryFormatter.FormatSelection(_library));
        }

        [Test]
        public void Formatter_FormatsDuration()
        {
            Assert.AreEqual("4:22", MediaLibraryFormatter.FormatDuration(262));
            Assert.AreEqual("0:07", MediaLibraryFormatter.FormatDuration(7));
            Assert.AreEqual("-:--", MediaLibraryFormatter.FormatDuration(0));
        }

        [Test]
        public void Formatter_SummaryReportsCountFilterAndSort()
        {
            _library.ShowOnly(MediaType.Video);
            _library.SortOrder = LibrarySortOrder.Title;

            string summary = MediaLibraryFormatter.FormatSummary(_library);

            StringAssert.Contains($"{_library.Count} 件", summary);
            StringAssert.Contains("Video", summary);
            StringAssert.Contains("Title", summary);
        }

        // ───────── 読み直し ─────────

        [Test]
        public void Refresh_RereadsTheCatalogAndKeepsTheSelection()
        {
            _library.ShowOnly(MediaType.Video);
            _library.SelectById("video-002");

            _library.Refresh();

            Assert.AreEqual("video-002", _library.SelectedMediaId);
            Assert.Greater(_library.Count, 0);
        }

        [Test]
        public void Entries_AreReadOnlyToCallers()
        {
            Assert.IsNotInstanceOf<List<DisplayMeta>>(_library.Entries,
                "UI が一覧を書き換えられないようにする");
        }

        /// <summary>通知を数えるだけの観測者。</summary>
        private sealed class Watcher : IMediaListObserver
        {
            public int LibraryChanged { get; private set; }
            public int SelectionChanged { get; private set; }
            public DisplayMeta LastSelected { get; private set; }

            public void OnListChanged(IMediaListView view) => LibraryChanged++;

            public void OnSelectionChanged(IMediaListView view, DisplayMeta selected)
            {
                SelectionChanged++;
                LastSelected = selected;
            }
        }
    }
}
