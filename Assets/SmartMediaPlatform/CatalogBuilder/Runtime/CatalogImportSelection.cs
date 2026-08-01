using System.Collections.Generic;

namespace SmartMediaPlatform.CatalogBuilder
{
    /// <summary>
    /// <b>取り込んだものの「どれを入れるか」。</b>Phase6-3。
    ///
    /// <b>取り込み元を問いません。</b>扱うのは <see cref="CatalogDraftItem"/> の並びだけなので、
    /// YouTube でも JSON でも CSV でも<b>同じ選び方</b>になります。
    ///
    /// <b>Unity に依存しません。</b>窓はこれを表示して押された結果を伝えるだけで、
    /// 判断(全選択・重複の見分け・選んだものを渡す)はここに寄せてあります。
    ///
    /// <b>既存と重なるものに印を付けます。</b>
    /// 同じチャンネルを 2 回取り込むのはよくあることで、
    /// <b>そのまま足すと同じ曲が並びます</b>。どれが新しいかを見せて選ばせます。
    /// </summary>
    public sealed class CatalogImportSelection
    {
        private readonly List<CatalogDraftItem> _items = new List<CatalogDraftItem>();
        private readonly List<bool> _selected = new List<bool>();
        private readonly List<bool> _existing = new List<bool>();

        public int Count { get { return _items.Count; } }

        public bool IsEmpty { get { return _items.Count == 0; } }

        public CatalogDraftItem GetAt(int index)
        {
            if (index < 0 || index >= _items.Count) return null;
            return _items[index];
        }

        /// <summary>
        /// 取り込み結果を受け取る。<b>最初は全部選んだ状態</b>にします
        /// (貼って押すだけ、をいちばん短くするため)。
        /// </summary>
        public void SetItems(IReadOnlyList<CatalogDraftItem> items)
        {
            _items.Clear();
            _selected.Clear();
            _existing.Clear();

            if (items == null) return;

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null) continue;

                _items.Add(items[i]);
                _selected.Add(true);
                _existing.Add(false);
            }
        }

        public void Clear()
        {
            _items.Clear();
            _selected.Clear();
            _existing.Clear();
        }

        // ───────── 選ぶ ─────────

        public bool IsSelected(int index)
        {
            if (index < 0 || index >= _selected.Count) return false;
            return _selected[index];
        }

        public void SetSelected(int index, bool value)
        {
            if (index < 0 || index >= _selected.Count) return;
            _selected[index] = value;
        }

        public void Toggle(int index)
        {
            SetSelected(index, !IsSelected(index));
        }

        public void SelectAll()
        {
            for (int i = 0; i < _selected.Count; i++) _selected[i] = true;
        }

        public void SelectNone()
        {
            for (int i = 0; i < _selected.Count; i++) _selected[i] = false;
        }

        public void InvertSelection()
        {
            for (int i = 0; i < _selected.Count; i++) _selected[i] = !_selected[i];
        }

        public int SelectedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _selected.Count; i++)
                {
                    if (_selected[i]) count++;
                }
                return count;
            }
        }

        /// <summary>選ばれているものだけ。順番は取り込んだときのまま。</summary>
        public CatalogDraftItem[] SelectedItems()
        {
            var result = new List<CatalogDraftItem>();

            for (int i = 0; i < _items.Count; i++)
            {
                if (_selected[i]) result.Add(_items[i]);
            }
            return result.ToArray();
        }

        // ───────── 既存と重なるもの ─────────

        public bool IsExisting(int index)
        {
            if (index < 0 || index >= _existing.Count) return false;
            return _existing[index];
        }

        /// <summary>
        /// <paramref name="draft"/> にすでにある ID へ印を付ける。
        /// </summary>
        /// <returns>重なっていた件数。</returns>
        public int MarkExisting(CatalogDraft draft)
        {
            int found = 0;

            for (int i = 0; i < _items.Count; i++)
            {
                bool exists = draft != null && draft.IndexOfId(_items[i].Id) >= 0;
                _existing[i] = exists;
                if (exists) found++;
            }
            return found;
        }

        /// <summary>まだ無いものだけを選ぶ。2 回目の取り込みで押すボタン。</summary>
        public void SelectOnlyNew()
        {
            for (int i = 0; i < _selected.Count; i++) _selected[i] = !_existing[i];
        }

        public int ExistingCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _existing.Count; i++)
                {
                    if (_existing[i]) count++;
                }
                return count;
            }
        }
    }
}
