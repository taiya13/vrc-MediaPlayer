using System;
using System.Collections.Generic;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Library
{
    /// <summary>
    /// <b>カタログを閲覧し、1 件を選ぶだけのクラス。</b>Phase4-1 の中心。
    ///
    /// <code>
    /// var library = new MediaLibrary(catalog);
    /// library.ShowOnly(MediaType.Video);   // 動画だけ見る
    /// library.Select(0);                   // 先頭を選ぶ
    /// string id = library.SelectedMediaId; // ← 上位へ渡すのはこれだけ
    /// </code>
    ///
    /// <b>再生の手段を持っていません。</b>
    /// このクラスから <c>PlayerSession</c> も <c>Queue</c> も <c>Backend</c> も見えません
    /// (asmdef の参照が <c>SmartMediaPlatform.Catalog</c> だけ)。
    /// 再生へつなぐのは <c>LibraryPlaybackBridge</c>(別 asmdef)の仕事です。
    /// <b>この分け方が「Media Library は閲覧と選択だけ」という制約の実体</b>です。
    ///
    /// <b>一覧は毎回作り直しません。</b>
    /// 絞り込み・並び順・カタログの読み直しのときだけ組み直して覚えておきます
    /// (UI が毎フレーム <see cref="Entries"/> を読んでも重くならないように)。
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public sealed class MediaLibrary : IMediaLibrary
    {
        /// <summary>絞り込みなしを表す種別の並び(Catalog の enum 全部)。</summary>
        private static readonly MediaType[] AllTypes =
        {
            MediaType.Unknown, MediaType.Music, MediaType.Video,
            MediaType.Podcast, MediaType.Live,
        };

        private readonly IMediaCatalog _catalog;
        private readonly List<MediaItem> _entries = new List<MediaItem>();
        private readonly IReadOnlyList<MediaItem> _readOnlyEntries;
        private readonly List<IMediaLibraryObserver> _observers = new List<IMediaLibraryObserver>();

        private MediaType[] _visibleTypes;
        private IReadOnlyList<MediaType> _readOnlyVisibleTypes;
        private LibrarySortOrder _sortOrder = LibrarySortOrder.CatalogOrder;

        private int _selectedIndex = -1;

        /// <param name="catalog">見せるカタログ。</param>
        /// <param name="visibleTypes">
        /// 最初に表示する種別。省略するとすべて表示します。
        /// 「動画だけの一覧」にしたいときは <c>MediaType.Video</c> を渡してください。
        /// </param>
        public MediaLibrary(IMediaCatalog catalog, params MediaType[] visibleTypes)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _readOnlyEntries = _entries.AsReadOnly();

            SetVisibleTypes(visibleTypes);
            Rebuild(notify: false);
        }

        // ───────── 閲覧 ─────────

        public IReadOnlyList<MediaItem> Entries => _readOnlyEntries;

        public int Count => _entries.Count;

        public IReadOnlyList<MediaType> VisibleTypes => _readOnlyVisibleTypes;

        public LibrarySortOrder SortOrder
        {
            get => _sortOrder;
            set
            {
                if (_sortOrder == value) return;
                _sortOrder = value;
                Rebuild(notify: true);
            }
        }

        public MediaItem GetAt(int index)
        {
            return index >= 0 && index < _entries.Count ? _entries[index] : null;
        }

        public int IndexOf(string mediaId)
        {
            if (string.IsNullOrWhiteSpace(mediaId)) return -1;

            for (int i = 0; i < _entries.Count; i++)
            {
                if (Same(_entries[i].Id, mediaId)) return i;
            }
            return -1;
        }

        /// <summary>すべての種別を表示する。</summary>
        public void ShowAll()
        {
            SetVisibleTypes(null);
            Rebuild(notify: true);
        }

        /// <summary>
        /// 指定した種別だけを表示する。
        /// <code>
        /// library.ShowOnly(MediaType.Video);                  // 動画だけ
        /// library.ShowOnly(MediaType.Music, MediaType.Video); // 音楽と動画
        /// </code>
        /// 何も渡さないと <see cref="ShowAll"/> と同じになります。
        /// </summary>
        public void ShowOnly(params MediaType[] types)
        {
            SetVisibleTypes(types);
            Rebuild(notify: true);
        }

        /// <summary>その種別を表示しているか。</summary>
        public bool IsVisible(MediaType type)
        {
            for (int i = 0; i < _visibleTypes.Length; i++)
            {
                if (_visibleTypes[i] == type) return true;
            }
            return false;
        }

        /// <summary>
        /// カタログを読み直して一覧を作り直す。
        /// <b>選択は可能なかぎり保ちます</b>(同じ ID がまだ一覧にあれば選び直す)。
        /// </summary>
        public void Refresh()
        {
            Rebuild(notify: true);
        }

        // ───────── 選択 ─────────

        public int SelectedIndex => _selectedIndex;

        public MediaItem SelectedItem => GetAt(_selectedIndex);

        public string SelectedMediaId
        {
            get
            {
                var item = SelectedItem;
                return item != null ? item.Id : null;
            }
        }

        public bool HasSelection => _selectedIndex >= 0 && _selectedIndex < _entries.Count;

        public bool Select(int index)
        {
            if (index < 0 || index >= _entries.Count) return false;
            if (index == _selectedIndex) return true;

            _selectedIndex = index;
            NotifySelectionChanged();
            return true;
        }

        public bool SelectById(string mediaId)
        {
            int index = IndexOf(mediaId);
            return index >= 0 && Select(index);
        }

        public bool SelectNext(bool wrap = true)
        {
            if (_entries.Count == 0) return false;
            if (_selectedIndex < 0) return Select(0);

            int next = _selectedIndex + 1;
            if (next >= _entries.Count)
            {
                if (!wrap) return false;
                next = 0;
            }
            return Select(next);
        }

        public bool SelectPrevious(bool wrap = true)
        {
            if (_entries.Count == 0) return false;
            if (_selectedIndex < 0) return Select(_entries.Count - 1);

            int previous = _selectedIndex - 1;
            if (previous < 0)
            {
                if (!wrap) return false;
                previous = _entries.Count - 1;
            }
            return Select(previous);
        }

        public void ClearSelection()
        {
            if (_selectedIndex < 0) return;

            _selectedIndex = -1;
            NotifySelectionChanged();
        }

        // ───────── 通知 ─────────

        public void AddObserver(IMediaLibraryObserver observer)
        {
            if (observer == null || _observers.Contains(observer)) return;
            _observers.Add(observer);
        }

        public void RemoveObserver(IMediaLibraryObserver observer)
        {
            if (observer == null) return;
            _observers.Remove(observer);
        }

        // ───────── 内部 ─────────

        private void SetVisibleTypes(MediaType[] types)
        {
            _visibleTypes = types != null && types.Length > 0
                ? (MediaType[])types.Clone()
                : (MediaType[])AllTypes.Clone();

            _readOnlyVisibleTypes = Array.AsReadOnly(_visibleTypes);
        }

        /// <summary>一覧を組み直す。選択は ID で覚えておいて選び直す。</summary>
        private void Rebuild(bool notify)
        {
            string previousId = SelectedMediaId;

            _entries.Clear();

            var all = _catalog.GetAll();
            for (int i = 0; i < all.Count; i++)
            {
                var item = all[i];
                if (item != null && IsVisible(item.Type)) _entries.Add(item);
            }

            Sort();

            // 位置ではなく ID で選び直す(並び順が変わっても同じものが選ばれたままになる)
            int restored = previousId != null ? IndexOf(previousId) : -1;
            bool selectionChanged = restored != _selectedIndex;
            _selectedIndex = restored;

            if (!notify) return;

            NotifyLibraryChanged();
            if (selectionChanged) NotifySelectionChanged();
        }

        private void Sort()
        {
            switch (_sortOrder)
            {
                case LibrarySortOrder.Title:
                    _entries.Sort((a, b) => CompareText(a.Title, b.Title));
                    break;

                case LibrarySortOrder.Artist:
                    _entries.Sort((a, b) =>
                    {
                        int byArtist = CompareText(a.Artist, b.Artist);
                        return byArtist != 0 ? byArtist : CompareText(a.Title, b.Title);
                    });
                    break;

                case LibrarySortOrder.Genre:
                    _entries.Sort((a, b) =>
                    {
                        int byGenre = CompareText(a.Genre, b.Genre);
                        return byGenre != 0 ? byGenre : CompareText(a.Title, b.Title);
                    });
                    break;

                case LibrarySortOrder.Duration:
                    _entries.Sort((a, b) =>
                    {
                        int byDuration = a.DurationSeconds.CompareTo(b.DurationSeconds);
                        return byDuration != 0 ? byDuration : CompareText(a.Title, b.Title);
                    });
                    break;

                default:
                    break;   // CatalogOrder — カタログの並びをそのまま使う
            }
        }

        private static int CompareText(string a, string b)
        {
            return string.Compare(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Same(string a, string b)
        {
            return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private void NotifyLibraryChanged()
        {
            for (int i = 0; i < _observers.Count; i++) _observers[i].OnLibraryChanged(this);
        }

        private void NotifySelectionChanged()
        {
            var selected = SelectedItem;
            for (int i = 0; i < _observers.Count; i++)
            {
                _observers[i].OnSelectionChanged(this, selected);
            }
        }

        public override string ToString()
        {
            return $"MediaLibrary({Count} 件 / 並び {SortOrder} / 選択 {SelectedMediaId ?? "なし"})";
        }
    }
}
