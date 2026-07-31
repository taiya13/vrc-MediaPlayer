using System.Collections.Generic;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.CatalogBuilder
{
    /// <summary>
    /// <b>編集中のカタログ 1 冊。</b>Phase6-1。
    ///
    /// <b>Unity にも VRChat にも依存しません。</b>
    /// <c>EditorWindow</c> は「これを表示して押された操作を伝える」だけの入れ物にし、
    /// <b>判断はすべてここへ寄せて EditMode で検証します</b>
    /// (Phase5 の <c>PlaybackModel</c> / <c>ListScrollModel</c> と同じ考え方)。
    ///
    /// <b>ここが持つ判断は 3 つだけ</b>です:
    /// <list type="bullet">
    /// <item>ID が重なっていないか(重なると再生側で別のものを指す)</item>
    /// <item>完成しているものだけを再生側へ渡す</item>
    /// <item>取り込んだものを、既存とどう混ぜるか</item>
    /// </list>
    ///
    /// URL の形が正しいかは<b>見ません</b>。何が正しい URL かは
    /// 取り込み元(YouTube / CSV / 手入力)によって違うので、
    /// <see cref="ICatalogImporter"/> 側の仕事です。
    /// </summary>
    public sealed class CatalogDraft
    {
        private readonly List<CatalogDraftItem> _items = new List<CatalogDraftItem>();

        /// <summary>このカタログの出どころ(アセットに書き残す説明)。</summary>
        public string SourceDescription = "Catalog Builder";

        public int Count { get { return _items.Count; } }

        public CatalogDraftItem GetAt(int index)
        {
            if (index < 0 || index >= _items.Count) return null;
            return _items[index];
        }

        public IReadOnlyList<CatalogDraftItem> Items { get { return _items; } }

        // ───────── 足す・消す・並べ替える ─────────

        /// <summary>空の 1 件を足す。返すのは足した位置。</summary>
        public int Add()
        {
            return Add(new CatalogDraftItem());
        }

        public int Add(CatalogDraftItem item)
        {
            if (item == null) return -1;

            _items.Add(item);
            return _items.Count - 1;
        }

        public bool RemoveAt(int index)
        {
            if (index < 0 || index >= _items.Count) return false;

            _items.RemoveAt(index);
            return true;
        }

        public void Clear()
        {
            _items.Clear();
        }

        /// <summary><paramref name="index"/> の 1 つ上 / 下と入れ替える。</summary>
        public bool Move(int index, int delta)
        {
            int target = index + delta;
            if (index < 0 || index >= _items.Count) return false;
            if (target < 0 || target >= _items.Count) return false;

            var moved = _items[index];
            _items[index] = _items[target];
            _items[target] = moved;
            return true;
        }

        /// <summary>複製して真下に置く。似た項目を続けて作るときに使う。</summary>
        public int Duplicate(int index)
        {
            var source = GetAt(index);
            if (source == null) return -1;

            var copy = source.Clone();
            copy.Id = MakeUniqueId(copy.Id);

            _items.Insert(index + 1, copy);
            return index + 1;
        }

        // ───────── ID ─────────

        public int IndexOfId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return -1;

            string wanted = id.Trim();
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Id != null && _items[i].Id.Trim() == wanted) return i;
            }
            return -1;
        }

        /// <summary>
        /// <paramref name="wanted"/> がすでにあれば「-2」「-3」…と後ろを足して空きを探す。
        /// 空なら <c>media-1</c> から順に付ける。
        /// </summary>
        public string MakeUniqueId(string wanted)
        {
            string baseId = string.IsNullOrWhiteSpace(wanted) ? "media" : wanted.Trim();

            if (IndexOfId(baseId) < 0) return baseId;

            for (int suffix = 2; suffix < 10000; suffix++)
            {
                string candidate = baseId + "-" + suffix;
                if (IndexOfId(candidate) < 0) return candidate;
            }
            return baseId;
        }

        /// <summary>
        /// 重なっている ID の一覧。
        /// <b>重なったまま焼くと、再生側が別のものを指します。</b>
        /// </summary>
        public string[] FindDuplicateIds()
        {
            var seen = new List<string>();
            var duplicates = new List<string>();

            for (int i = 0; i < _items.Count; i++)
            {
                string id = _items[i].Id;
                if (string.IsNullOrWhiteSpace(id)) continue;

                id = id.Trim();
                if (seen.Contains(id))
                {
                    if (!duplicates.Contains(id)) duplicates.Add(id);
                    continue;
                }
                seen.Add(id);
            }

            return duplicates.ToArray();
        }

        // ───────── 状態 ─────────

        /// <summary>再生側へ渡せる件数。</summary>
        public int CompleteCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i].IsComplete) count++;
                }
                return count;
            }
        }

        /// <summary>そのまま焼いてよいか(重複が無く、1 件以上そろっている)。</summary>
        public bool CanBuild
        {
            get { return CompleteCount > 0 && FindDuplicateIds().Length == 0; }
        }

        /// <summary>
        /// <b>完成しているものだけ</b>を再生側の形で返す。
        /// 作りかけは黙って落とします(不完全なものを再生側へ流さないため)。
        /// </summary>
        public MediaItem[] ToMediaItems()
        {
            var result = new List<MediaItem>();

            for (int i = 0; i < _items.Count; i++)
            {
                MediaItem item = _items[i].ToMediaItem();
                if (item != null) result.Add(item);
            }
            return result.ToArray();
        }

        // ───────── 取り込み ─────────

        /// <summary>そのまま足す。</summary>
        public const int MergeAppend = 0;

        /// <summary>同じ ID があれば上書き、無ければ足す。</summary>
        public const int MergeUpdate = 1;

        /// <summary>いまの中身を捨てて入れ替える。</summary>
        public const int MergeReplace = 2;

        /// <summary>
        /// 取り込んだものを混ぜる。
        ///
        /// <b>混ぜ方を 3 つに絞ってあります。</b>
        /// 取り込み元がどれだけ増えても、<b>混ぜ方はここ 1 か所</b>で済みます。
        /// </summary>
        /// <returns>足した / 上書きした件数。</returns>
        public int Merge(IReadOnlyList<CatalogDraftItem> incoming, int mode)
        {
            if (incoming == null) return 0;

            if (mode == MergeReplace) _items.Clear();

            int changed = 0;

            for (int i = 0; i < incoming.Count; i++)
            {
                CatalogDraftItem item = incoming[i];
                if (item == null) continue;

                if (mode == MergeUpdate)
                {
                    int existing = IndexOfId(item.Id);
                    if (existing >= 0)
                    {
                        _items[existing] = item.Clone();
                        changed++;
                        continue;
                    }
                }

                var copy = item.Clone();

                // 同じ ID が並ぶと再生側が別のものを指すので、必ずずらす。
                if (mode != MergeUpdate) copy.Id = MakeUniqueId(copy.Id);

                _items.Add(copy);
                changed++;
            }

            return changed;
        }

        /// <summary>既存のカタログを読み込んで編集対象にする。</summary>
        public void LoadFrom(IReadOnlyList<MediaItem> items)
        {
            _items.Clear();
            if (items == null) return;

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null) continue;
                _items.Add(new CatalogDraftItem(items[i]));
            }
        }
    }
}
