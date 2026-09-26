using System;
using System.Collections.Generic;

namespace SmartMediaPlatform.Playlists
{
    /// <summary>
    /// 曲の並び。<b>MediaItem ではなく MediaId(文字列)だけ</b>を持つ。
    ///
    /// ID だけを持つ理由:
    ///  - Catalog の実体を抱え込まないので、保存・読み込みが単純な文字列で済む
    ///  - Catalog が差し替わっても(Phase2 の本番カタログなど)プレイリストは壊れない
    ///  - Music / Video の区別を持たないため、将来 VideoBackend でもそのまま使える
    ///
    /// Backend・UI・再生状態には一切依存しない純粋なデータ構造。
    /// </summary>
    public sealed class Playlist
    {
        private readonly List<string> _mediaIds = new List<string>();
        private readonly IReadOnlyList<string> _readOnlyIds;

        /// <summary>プレイリストを一意に識別する ID。</summary>
        public string Id { get; }

        /// <summary>表示名。変更できる。</summary>
        public string Name { get; set; }

        public Playlist(string id, string name = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Playlist requires a non-empty id.", nameof(id));

            Id = id;
            Name = string.IsNullOrEmpty(name) ? id : name;
            _readOnlyIds = _mediaIds.AsReadOnly();
        }

        public int Count => _mediaIds.Count;

        public bool IsEmpty => _mediaIds.Count == 0;

        /// <summary>先頭から順に全件(読み取り専用ビュー)。</summary>
        public IReadOnlyList<string> MediaIds => _readOnlyIds;

        // ───────── 曲の追加 ─────────

        /// <summary>
        /// 末尾に追加する。すでに含まれている ID は追加しない(重複を持たない)。
        /// </summary>
        public bool Add(string mediaId)
        {
            if (string.IsNullOrWhiteSpace(mediaId) || Contains(mediaId)) return false;

            _mediaIds.Add(mediaId);
            return true;
        }

        /// <summary>まとめて追加する。追加できた件数を返す。</summary>
        public int AddRange(IEnumerable<string> mediaIds)
        {
            if (mediaIds == null) return 0;

            int added = 0;
            foreach (var id in mediaIds)
            {
                if (Add(id)) added++;
            }
            return added;
        }

        /// <summary>位置を指定して挿入する。</summary>
        public bool Insert(int index, string mediaId)
        {
            if (string.IsNullOrWhiteSpace(mediaId) || Contains(mediaId)) return false;
            if (index < 0 || index > _mediaIds.Count) return false;

            _mediaIds.Insert(index, mediaId);
            return true;
        }

        // ───────── 曲の削除 ─────────

        public bool Remove(string mediaId)
        {
            return RemoveAt(IndexOf(mediaId));
        }

        public bool RemoveAt(int index)
        {
            if (index < 0 || index >= _mediaIds.Count) return false;

            _mediaIds.RemoveAt(index);
            return true;
        }

        public void Clear()
        {
            _mediaIds.Clear();
        }

        // ───────── 曲の移動 / 並び替え ─────────

        /// <summary>fromIndex の曲を toIndex へ移動する。</summary>
        public bool Move(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= _mediaIds.Count) return false;
            if (toIndex < 0 || toIndex >= _mediaIds.Count) return false;
            if (fromIndex == toIndex) return false;

            var id = _mediaIds[fromIndex];
            _mediaIds.RemoveAt(fromIndex);
            _mediaIds.Insert(toIndex, id);
            return true;
        }

        /// <summary>
        /// 指定した順に並び替える。
        /// 与えられた順に含まれない曲は、元の順のまま末尾に残す(曲を失わない)。
        /// </summary>
        public bool Reorder(IEnumerable<string> orderedIds)
        {
            if (orderedIds == null) return false;

            var result = new List<string>(_mediaIds.Count);
            foreach (var id in orderedIds)
            {
                if (Contains(id) && !ContainsIn(result, id)) result.Add(id);
            }
            foreach (var id in _mediaIds)
            {
                if (!ContainsIn(result, id)) result.Add(id);
            }

            _mediaIds.Clear();
            _mediaIds.AddRange(result);
            return true;
        }

        /// <summary>並びを逆にする。</summary>
        public void Reverse()
        {
            _mediaIds.Reverse();
        }

        /// <summary>
        /// ランダムに並び替える(Fisher–Yates)。
        /// 乱数を渡せるのでテストで結果を固定できる。
        /// </summary>
        public void Shuffle(Random random = null)
        {
            var rng = random ?? new Random();
            for (int i = _mediaIds.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var tmp = _mediaIds[i];
                _mediaIds[i] = _mediaIds[j];
                _mediaIds[j] = tmp;
            }
        }

        // ───────── 検索 / 複製 ─────────

        public bool Contains(string mediaId)
        {
            return IndexOf(mediaId) >= 0;
        }

        public int IndexOf(string mediaId)
        {
            if (string.IsNullOrEmpty(mediaId)) return -1;

            for (int i = 0; i < _mediaIds.Count; i++)
            {
                if (string.Equals(_mediaIds[i], mediaId, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        /// <summary>index 番目の ID。範囲外なら null。</summary>
        public string GetAt(int index)
        {
            if (index < 0 || index >= _mediaIds.Count) return null;
            return _mediaIds[index];
        }

        /// <summary>中身をそのまま持つ別のプレイリストを作る。</summary>
        public Playlist Duplicate(string newId, string newName = null)
        {
            var copy = new Playlist(newId, newName ?? Name + " (copy)");
            copy._mediaIds.AddRange(_mediaIds);
            return copy;
        }

        public override string ToString()
        {
            return $"{Name} [{Id}] ({Count} 曲)";
        }

        private static bool ContainsIn(List<string> list, string mediaId)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], mediaId, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
