using System;
using System.Collections.Generic;
using SmartMediaPlatform.Catalog.Store;

namespace SmartMediaPlatform.Library
{
    /// <summary>
    /// <see cref="IMediaListView"/> の共通部分(並びの保持・選択・通知)。
    ///
    /// <see cref="MediaLibrary"/> と <see cref="RelatedMediaView"/> の違いは
    /// <b>「何を並べるか」だけ</b>なので、その 1 点(<see cref="BuildEntries"/>)を
    /// 派生クラスに任せ、残りはここにまとめてあります。
    ///
    /// <b>選択は位置ではなく ID で覚え直します。</b>
    /// 並べ替え・絞り込み・起点の変更で並びが変わっても、
    /// <b>選んでいたものが残っていれば選ばれたまま</b>です。消えたときだけ外れます。
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public abstract class MediaListViewBase : IMediaListView
    {
        private readonly List<DisplayMeta> _entries = new List<DisplayMeta>();
        private readonly IReadOnlyList<DisplayMeta> _readOnlyEntries;
        private readonly List<IMediaListObserver> _observers = new List<IMediaListObserver>();

        private int _selectedIndex = -1;

        /// <param name="store">表示情報を引く唯一の窓口。</param>
        protected MediaListViewBase(ICatalogStore store)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            _readOnlyEntries = _entries.AsReadOnly();
        }

        /// <summary>カタログの窓口。派生クラスはここからしかデータを取りません。</summary>
        protected ICatalogStore Store { get; }

        // ───────── 閲覧 ─────────

        public IReadOnlyList<DisplayMeta> Entries => _readOnlyEntries;

        public int Count => _entries.Count;

        public DisplayMeta GetAt(int index)
        {
            return index >= 0 && index < _entries.Count ? _entries[index] : null;
        }

        public int IndexOf(string mediaId)
        {
            if (string.IsNullOrWhiteSpace(mediaId)) return -1;

            for (int i = 0; i < _entries.Count; i++)
            {
                if (Same(_entries[i].MediaId, mediaId)) return i;
            }
            return -1;
        }

        // ───────── 選択 ─────────

        public int SelectedIndex => _selectedIndex;

        public DisplayMeta SelectedItem => GetAt(_selectedIndex);

        public string SelectedMediaId
        {
            get
            {
                var item = SelectedItem;
                return item != null ? item.MediaId : null;
            }
        }

        public PlayableRef SelectedRef
        {
            get
            {
                string id = SelectedMediaId;
                return id != null ? Store.GetPlayableRef(id) : PlayableRef.None;
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

        public void AddObserver(IMediaListObserver observer)
        {
            if (observer == null || _observers.Contains(observer)) return;
            _observers.Add(observer);
        }

        public void RemoveObserver(IMediaListObserver observer)
        {
            if (observer == null) return;
            _observers.Remove(observer);
        }

        // ───────── 派生クラスが埋めるところ ─────────

        /// <summary>
        /// <b>何を並べるか。</b>ここだけが <see cref="MediaLibrary"/> と
        /// <see cref="RelatedMediaView"/> の違いです。
        /// </summary>
        /// <param name="into">ここへ順番に足してください(呼ばれた時点で空)。</param>
        protected abstract void BuildEntries(List<DisplayMeta> into);

        /// <summary>
        /// 並びを組み直す。選択は ID で覚えておいて選び直します。
        /// </summary>
        /// <param name="notify">観測者へ知らせるか(組み立て中は false)。</param>
        protected void Rebuild(bool notify)
        {
            string previousId = SelectedMediaId;

            _entries.Clear();
            BuildEntries(_entries);

            // 位置ではなく ID で選び直す(並びが変わっても同じものが選ばれたままになる)
            int restored = previousId != null ? IndexOf(previousId) : -1;
            bool selectionChanged = restored != _selectedIndex;
            _selectedIndex = restored;

            if (!notify) return;

            NotifyListChanged();
            if (selectionChanged) NotifySelectionChanged();
        }

        // ───────── 内部 ─────────

        private void NotifyListChanged()
        {
            for (int i = 0; i < _observers.Count; i++) _observers[i].OnListChanged(this);
        }

        private void NotifySelectionChanged()
        {
            var selected = SelectedItem;
            for (int i = 0; i < _observers.Count; i++)
            {
                _observers[i].OnSelectionChanged(this, selected);
            }
        }

        /// <summary>ID の比較(大文字小文字を無視)。</summary>
        protected static bool Same(string a, string b)
        {
            return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
