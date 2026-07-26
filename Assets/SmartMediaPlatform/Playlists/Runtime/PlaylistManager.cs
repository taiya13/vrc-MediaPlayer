using System;
using System.Collections.Generic;

namespace SmartMediaPlatform.Playlists
{
    /// <summary>
    /// プレイリストの管理役。作成・削除・複製・保存・読み込みを受け持つ。
    ///
    /// 曲の追加/削除/移動/並び替えは <see cref="Playlist"/> 自身の責務なので、
    /// ここでは「プレイリストという単位」の出し入れだけを扱う。
    ///
    /// Backend・UI・再生状態に依存しない純粋 C#。
    /// </summary>
    public sealed class PlaylistManager
    {
        private readonly List<Playlist> _playlists = new List<Playlist>();
        private readonly IReadOnlyList<Playlist> _readOnlyPlaylists;

        private int _nextId = 1;

        public PlaylistManager()
        {
            _readOnlyPlaylists = _playlists.AsReadOnly();
        }

        public int Count => _playlists.Count;

        /// <summary>登録順の全プレイリスト(読み取り専用ビュー)。</summary>
        public IReadOnlyList<Playlist> Playlists => _readOnlyPlaylists;

        // ───────── 作成 / 削除 ─────────

        /// <summary>
        /// 新しいプレイリストを作る。ID を省略すると自動で採番する。
        /// 同じ ID がすでにあれば作らずに null を返す。
        /// </summary>
        public Playlist Create(string name, string id = null)
        {
            string playlistId = string.IsNullOrWhiteSpace(id) ? GenerateId() : id;
            if (Get(playlistId) != null) return null;

            var playlist = new Playlist(playlistId, name);
            _playlists.Add(playlist);
            return playlist;
        }

        public bool Delete(string playlistId)
        {
            var playlist = Get(playlistId);
            return playlist != null && _playlists.Remove(playlist);
        }

        public bool Delete(Playlist playlist)
        {
            return playlist != null && _playlists.Remove(playlist);
        }

        public void Clear()
        {
            _playlists.Clear();
        }

        // ───────── 取得 ─────────

        public Playlist Get(string playlistId)
        {
            if (string.IsNullOrEmpty(playlistId)) return null;

            for (int i = 0; i < _playlists.Count; i++)
            {
                if (string.Equals(_playlists[i].Id, playlistId, StringComparison.OrdinalIgnoreCase))
                    return _playlists[i];
            }
            return null;
        }

        public Playlist GetByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            for (int i = 0; i < _playlists.Count; i++)
            {
                if (string.Equals(_playlists[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return _playlists[i];
            }
            return null;
        }

        public bool Contains(string playlistId)
        {
            return Get(playlistId) != null;
        }

        // ───────── 複製 ─────────

        /// <summary>
        /// 中身をそのまま持つ別のプレイリストを作って登録する。
        /// 元が見つからなければ null。
        /// </summary>
        public Playlist Duplicate(string playlistId, string newName = null, string newId = null)
        {
            var source = Get(playlistId);
            if (source == null) return null;

            string id = string.IsNullOrWhiteSpace(newId) ? GenerateId() : newId;
            if (Get(id) != null) return null;

            var copy = source.Duplicate(id, newName);
            _playlists.Add(copy);
            return copy;
        }

        // ───────── 保存 / 読み込み ─────────

        /// <summary>すべてのプレイリストを保存先へ書き出す。</summary>
        public bool Save(IPlaylistStore store)
        {
            if (store == null) return false;

            store.Save(PlaylistSerializer.Serialize(_playlists));
            return true;
        }

        /// <summary>
        /// 保存先から読み込んで、現在の内容を置き換える。
        /// 保存内容が無ければ何もせず false。
        /// </summary>
        public bool Load(IPlaylistStore store)
        {
            if (store == null) return false;

            string content = store.Load();
            if (string.IsNullOrEmpty(content)) return false;

            var loaded = PlaylistSerializer.Deserialize(content);

            _playlists.Clear();
            _playlists.AddRange(loaded);

            // 自動採番が既存の ID とぶつからないようにする
            _nextId = 1;
            foreach (var playlist in _playlists)
            {
                if (playlist.Id.StartsWith("playlist-", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(playlist.Id.Substring("playlist-".Length), out int number)
                    && number >= _nextId)
                {
                    _nextId = number + 1;
                }
            }

            return true;
        }

        private string GenerateId()
        {
            string id;
            do
            {
                id = $"playlist-{_nextId:000}";
                _nextId++;
            }
            while (Get(id) != null);

            return id;
        }
    }
}
