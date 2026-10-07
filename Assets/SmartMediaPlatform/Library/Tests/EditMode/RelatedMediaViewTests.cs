using System;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Store;
using SmartMediaPlatform.Video.Data;

namespace SmartMediaPlatform.Library.Tests
{
    /// <summary>
    /// Phase4-2: 関連動画の一覧(<see cref="RelatedMediaView"/>)の検証。
    ///
    /// 確かめたいのは 3 点です。
    /// <list type="number">
    /// <item>ID → Catalog → DisplayMeta の流れで一覧ができる</item>
    /// <item><b>関連 ID の出どころを差し替えても、この層から上は無変更</b></item>
    /// <item>カタログの内部構造も URL も出てこない</item>
    /// </list>
    /// </summary>
    public sealed class RelatedMediaViewTests
    {
        private IMediaCatalog _catalog;
        private CatalogStore _store;
        private StaticRelatedMediaProvider _provider;
        private RelatedMediaView _view;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new VideoCatalogSource(), new System.Random(1));
            _store = new CatalogStore(_catalog);
            _provider = new StaticRelatedMediaProvider(new CatalogRelatedMediaProvider(_catalog));
            _view = new RelatedMediaView(_store, _provider);
        }

        private string[] Ids() => _view.Entries.Select(x => x.MediaId).ToArray();

        // ───────── 前提 ─────────

        [Test]
        public void Constructor_RejectsNulls()
        {
            Assert.Throws<ArgumentNullException>(() => new RelatedMediaView(null, _provider));
            Assert.Throws<ArgumentNullException>(() => new RelatedMediaView(_store, null));
        }

        [Test]
        public void View_IsTheSharedListContract()
        {
            Assert.IsInstanceOf<IMediaListView>(_view,
                "カタログ全体の一覧と同じ形なので、UI を使い回せる");
        }

        [Test]
        public void WithoutASource_TheListIsEmpty()
        {
            Assert.AreEqual(0, _view.Count);
            Assert.IsNull(_view.SourceMediaId);
            Assert.IsNull(_view.SourceMeta);
            Assert.IsFalse(_view.HasSelection);
        }

        // ───────── ID → Catalog → DisplayMeta ─────────

        [Test]
        public void SetSource_BuildsTheListFromTheCatalogRelatedIds()
        {
            int count = _view.SetSource("video-001");

            Assert.Greater(count, 0);
            Assert.AreEqual(count, _view.Count);
            Assert.AreEqual("video-001", _view.SourceMediaId);

            // VideoCatalogSource の video-001 は video-002 / video-004 を関連に持つ
            CollectionAssert.Contains(Ids(), "video-002");
            CollectionAssert.Contains(Ids(), "video-004");
        }

        [Test]
        public void SetSource_ExposesTheSourceMeta()
        {
            _view.SetSource("video-001");

            Assert.IsNotNull(_view.SourceMeta);
            Assert.AreEqual("video-001", _view.SourceMeta.MediaId);
            Assert.IsTrue(_view.SourceMeta.HasTitle);
        }

        [Test]
        public void EntriesAreDisplayMeta()
        {
            _view.SetSource("video-001");

            foreach (var entry in _view.Entries)
            {
                Assert.IsInstanceOf<DisplayMeta>(entry);
                Assert.IsTrue(entry.HasTitle, "UI が出す情報は揃っている");
            }
        }

        [Test]
        public void TheSourceItselfIsNeverListed()
        {
            _provider.Set("video-001", new[] { "video-001", "video-002" });

            _view.SetSource("video-001");

            CollectionAssert.DoesNotContain(Ids(), "video-001", "「関連」に自分は要らない");
        }

        [Test]
        public void SetSource_WithNullEmptiesTheList()
        {
            _view.SetSource("video-001");
            Assert.Greater(_view.Count, 0);

            _view.SetSource(null);

            Assert.AreEqual(0, _view.Count);
            Assert.IsNull(_view.SourceMediaId);
        }

        [Test]
        public void SetSource_WithAnUnknownIdGivesAnEmptyList()
        {
            Assert.AreEqual(0, _view.SetSource("does-not-exist"));
            Assert.AreEqual(0, _view.Count);
        }

        [Test]
        public void MaxCount_LimitsTheList()
        {
            _provider.Set("video-001", new[] { "video-002", "video-003", "video-004", "video-005" });

            var limited = new RelatedMediaView(_store, _provider, 2);
            limited.SetSource("video-001");

            Assert.AreEqual(2, limited.Count);
        }

        [Test]
        public void MaxCount_CanBeChangedAfterwards()
        {
            _provider.Set("video-001", new[] { "video-002", "video-003", "video-004" });
            _view.SetSource("video-001");
            Assert.AreEqual(3, _view.Count);

            _view.MaxCount = 1;

            Assert.AreEqual(1, _view.Count);
        }

        // ───────── 出どころの差し替え(サーバー連携) ─────────

        [Test]
        public void ServerSuppliedIds_ReplaceTheCatalogOnes()
        {
            _view.SetSource("video-001");
            string[] fromCatalog = Ids();

            // サーバーが返した ID を入れる(取得処理はこの層の外)
            _provider.Set("video-001", new[] { "video-009", "video-006" });
            _view.Refresh();

            CollectionAssert.AreEqual(new[] { "video-009", "video-006" }, Ids(),
                "サーバーが返した順番そのままで並ぶ");
            CollectionAssert.AreNotEqual(fromCatalog, Ids());
        }

        [Test]
        public void ServerSuppliedIds_KeepWorkingWhenSomeAreUnknown()
        {
            _provider.Set("video-001", new[] { "video-009", "removed-from-catalog", "video-006" });

            _view.SetSource("video-001");

            CollectionAssert.AreEqual(new[] { "video-009", "video-006" }, Ids(),
                "知らない ID は落ちるが、残りはそのまま並ぶ");
        }

        [Test]
        public void FallingBackToTheCatalogWhenTheServerHasNotAnswered()
        {
            // video-002 についてはサーバー応答がまだ無い
            _provider.Set("video-001", new[] { "video-009" });

            _view.SetSource("video-002");

            Assert.Greater(_view.Count, 0, "応答が無い間もカタログの関連で埋まる");
        }

        [Test]
        public void RemovingAServerAnswerGoesBackToTheCatalog()
        {
            _view.SetSource("video-001");
            string[] fromCatalog = Ids();

            _provider.Set("video-001", new[] { "video-009" });
            _view.Refresh();
            CollectionAssert.AreNotEqual(fromCatalog, Ids());

            _provider.Remove("video-001");
            _view.Refresh();

            CollectionAssert.AreEqual(fromCatalog, Ids());
        }

        [Test]
        public void SwappingTheWholeProviderNeedsNoChangeHere()
        {
            // 「カタログ由来」から「完全に外部由来」へ、実装ごと差し替える
            var external = new StaticRelatedMediaProvider();
            external.Set("video-001", new[] { "video-007", "video-008" });

            var view = new RelatedMediaView(_store, external);
            view.SetSource("video-001");

            CollectionAssert.AreEqual(
                new[] { "video-007", "video-008" }, view.Entries.Select(x => x.MediaId).ToArray());
        }

        [Test]
        public void Provider_IsVisibleForDiagnostics()
        {
            Assert.AreSame(_provider, _view.Provider);
            StringAssert.Contains("StaticRelatedMediaProvider", _view.Provider.ToString());
        }

        // ───────── 選択 ─────────

        [Test]
        public void Select_GivesAPlayableRef()
        {
            _view.SetSource("video-001");
            _view.Select(0);

            var playable = _view.SelectedRef;

            Assert.IsTrue(playable.IsValid);
            Assert.AreEqual(_view.SelectedMediaId, playable.MediaId);
            Assert.AreEqual(MediaType.Video, playable.Type);
        }

        [Test]
        public void SelectedRef_IsNoneWithoutASelection()
        {
            _view.SetSource("video-001");

            Assert.IsFalse(_view.SelectedRef.IsValid);
        }

        [Test]
        public void Selection_IsKeptWhenItSurvivesASourceChange()
        {
            _provider.Set("video-001", new[] { "video-005", "video-006" });
            _provider.Set("video-002", new[] { "video-006", "video-007" });

            _view.SetSource("video-001");
            _view.SelectById("video-006");

            _view.SetSource("video-002");

            Assert.AreEqual("video-006", _view.SelectedMediaId,
                "起点が変わっても、まだ並んでいるものは選ばれたまま");
        }

        [Test]
        public void Selection_IsDroppedWhenItDisappears()
        {
            _provider.Set("video-001", new[] { "video-005" });
            _provider.Set("video-002", new[] { "video-007" });

            _view.SetSource("video-001");
            _view.SelectById("video-005");

            _view.SetSource("video-002");

            Assert.IsFalse(_view.HasSelection);
        }

        // ───────── 通知 ─────────

        [Test]
        public void Observer_HearsAboutSourceChanges()
        {
            var watcher = new Watcher();
            _view.AddObserver(watcher);

            _view.SetSource("video-001");

            Assert.AreEqual(1, watcher.ListChanged);
        }

        [Test]
        public void Observer_HearsAboutSelectionChanges()
        {
            _view.SetSource("video-001");
            var watcher = new Watcher();
            _view.AddObserver(watcher);

            _view.Select(0);

            Assert.AreEqual(1, watcher.SelectionChanged);
            Assert.AreSame(_view.GetAt(0), watcher.LastSelected);
        }

        [Test]
        public void Observer_IsNotToldWhenTheSourceDoesNotChange()
        {
            _view.SetSource("video-001");
            var watcher = new Watcher();
            _view.AddObserver(watcher);

            _view.SetSource("video-001");

            Assert.AreEqual(0, watcher.ListChanged);
        }

        // ───────── 責務の境界 ─────────

        [Test]
        public void TheView_NeverExposesAUrl()
        {
            var type = typeof(RelatedMediaView);

            Assert.IsNull(type.GetProperty("Url"));
            Assert.IsNull(type.GetMethod("GetUrl"));
            Assert.IsNull(typeof(DisplayMeta).GetProperty("Url"));
        }

        [Test]
        public void TheProviderContract_ReturnsIdsOnly()
        {
            var method = typeof(IRelatedMediaProvider).GetMethod("GetRelatedIds");

            Assert.IsNotNull(method);
            Assert.AreEqual(typeof(System.Collections.Generic.IReadOnlyList<string>),
                method.ReturnType,
                "関連の出どころが返すのは ID(string)の並びだけ");
        }

        [Test]
        public void CatalogProvider_ReturnsIdsInTheCatalogsOrder()
        {
            var provider = new CatalogRelatedMediaProvider(_catalog);

            var ids = provider.GetRelatedIds("video-001");
            var expected = _catalog.GetRelated("video-001").Select(x => x.Id).ToArray();

            CollectionAssert.AreEqual(expected, ids.ToArray());
        }

        [Test]
        public void CatalogProvider_RespectsMaxCount()
        {
            var provider = new CatalogRelatedMediaProvider(_catalog);

            Assert.AreEqual(1, provider.GetRelatedIds("video-001", 1).Count);
        }

        [Test]
        public void CatalogProvider_HandlesUnknownIds()
        {
            var provider = new CatalogRelatedMediaProvider(_catalog);

            Assert.AreEqual(0, provider.GetRelatedIds("nope").Count);
            Assert.AreEqual(0, provider.GetRelatedIds(null).Count);
        }

        [Test]
        public void StaticProvider_CopiesWhatItIsGiven()
        {
            var ids = new[] { "video-002", "video-003" };
            _provider.Set("video-001", ids);

            ids[0] = "changed-after-the-fact";

            CollectionAssert.AreEqual(
                new[] { "video-002", "video-003" }, _provider.GetRelatedIds("video-001").ToArray(),
                "渡した配列をあとから書き換えても影響しない");
        }

        [Test]
        public void StaticProvider_CanBeClearedAndInspected()
        {
            _provider.Set("video-001", new[] { "video-002" });

            Assert.IsTrue(_provider.Has("video-001"));
            Assert.AreEqual(1, _provider.Count);

            _provider.Clear();

            Assert.IsFalse(_provider.Has("video-001"));
            Assert.AreEqual(0, _provider.Count);
        }

        /// <summary>通知を数えるだけの観測者。</summary>
        private sealed class Watcher : IMediaListObserver
        {
            public int ListChanged { get; private set; }
            public int SelectionChanged { get; private set; }
            public DisplayMeta LastSelected { get; private set; }

            public void OnListChanged(IMediaListView view) => ListChanged++;

            public void OnSelectionChanged(IMediaListView view, DisplayMeta selected)
            {
                SelectionChanged++;
                LastSelected = selected;
            }
        }
    }
}
