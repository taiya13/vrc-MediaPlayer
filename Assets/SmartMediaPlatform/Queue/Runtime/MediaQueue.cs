using System;
using System.Collections.Generic;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Queue
{
    /// <summary>
    /// <see cref="IQueue"/> の純粋 C# 実装(UnityEngine / VRChat SDK 非依存)。
    ///
    /// 再生も同期も行わない「並び順の管理」だけに責務を絞る。
    /// Player / UI / ネットワークを一切知らないため、
    /// 将来 Music Player・Video Player・Auto Play・DJ Mode・Vote・Shared Queue の
    /// いずれからも同じ API で利用できる。
    /// </summary>
    public sealed class MediaQueue : IQueue
    {
        private readonly List<QueueItem> _items = new List<QueueItem>();
        private readonly IReadOnlyList<QueueItem> _readOnlyItems;

        private int _nextHandle = 1;
        private int _version;

        public MediaQueue()
        {
            _readOnlyItems = _items.AsReadOnly();
        }

        public int Count => _items.Count;

        public bool IsEmpty => _items.Count == 0;

        public int Version => _version;

        // --- 追加 ---

        public QueueItem Enqueue(MediaItem item, QueueItemSource source = QueueItemSource.Manual)
        {
            var entry = CreateItem(item, source);
            _items.Add(entry);
            _version++;
            return entry;
        }

        public QueueItem EnqueueNext(MediaItem item, QueueItemSource source = QueueItemSource.Manual)
        {
            var entry = CreateItem(item, source);
            // Now(index 0)の直後に割り込む。空なら先頭になる。
            int insertAt = _items.Count == 0 ? 0 : 1;
            _items.Insert(insertAt, entry);
            _version++;
            return entry;
        }

        private QueueItem CreateItem(MediaItem item, QueueItemSource source)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            return new QueueItem(_nextHandle++, item, source);
        }

        // --- 取り出し / 参照 ---

        public QueueItem Dequeue()
        {
            if (_items.Count == 0) return null;

            var head = _items[0];
            _items.RemoveAt(0);
            _version++;
            return head;
        }

        public QueueItem Peek()
        {
            return _items.Count > 0 ? _items[0] : null;
        }

        public QueueItem PeekAt(int index)
        {
            if (index < 0 || index >= _items.Count) return null;
            return _items[index];
        }

        public QueueItem PeekNext()
        {
            return PeekAt(1);
        }

        public QueueItem Skip()
        {
            // 先頭を捨てて、新しい先頭(次の Now)を返す。
            if (_items.Count == 0) return null;

            _items.RemoveAt(0);
            _version++;
            return Peek();
        }

        public void Clear()
        {
            if (_items.Count == 0) return;

            _items.Clear();
            _version++;
        }

        // --- 検索 ---

        public bool Contains(string mediaId)
        {
            return IndexOf(mediaId) >= 0;
        }

        public bool ContainsHandle(int handle)
        {
            return IndexOfHandle(handle) >= 0;
        }

        public int IndexOf(string mediaId)
        {
            if (string.IsNullOrEmpty(mediaId)) return -1;

            for (int i = 0; i < _items.Count; i++)
            {
                if (string.Equals(_items[i].MediaId, mediaId, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private int IndexOfHandle(int handle)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Handle == handle) return i;
            }
            return -1;
        }

        // --- 削除 / 並べ替え ---

        public bool Remove(string mediaId)
        {
            return RemoveAt(IndexOf(mediaId));
        }

        public bool RemoveHandle(int handle)
        {
            return RemoveAt(IndexOfHandle(handle));
        }

        public bool RemoveAt(int index)
        {
            if (index < 0 || index >= _items.Count) return false;

            _items.RemoveAt(index);
            _version++;
            return true;
        }

        public bool Move(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= _items.Count) return false;
            if (toIndex < 0 || toIndex >= _items.Count) return false;
            if (fromIndex == toIndex) return false;

            var entry = _items[fromIndex];
            _items.RemoveAt(fromIndex);
            _items.Insert(toIndex, entry);
            _version++;
            return true;
        }

        // --- 列挙 ---

        public IReadOnlyList<QueueItem> GetAll()
        {
            return _readOnlyItems;
        }
    }
}
