using System.Collections.Generic;

namespace SmartMediaPlatform.Playlists
{
    /// <summary>
    /// プレイリストの保存先を抽象化する。
    ///
    /// 保存形式(文字列化)は <see cref="PlaylistSerializer"/> が担当し、
    /// この インターフェース は「その文字列をどこに置くか」だけを受け持つ。
    /// テストではメモリ、Unity ではファイルや PlayerPrefs、
    /// 将来 VRChat では別の手段、と差し替えられる。
    /// </summary>
    public interface IPlaylistStore
    {
        void Save(string content);

        /// <summary>保存された内容。無ければ null。</summary>
        string Load();
    }

    /// <summary>メモリ上に持つだけの保存先。テストと動作確認用。</summary>
    public sealed class InMemoryPlaylistStore : IPlaylistStore
    {
        private string _content;

        public bool HasContent => _content != null;

        public void Save(string content)
        {
            _content = content;
        }

        public string Load()
        {
            return _content;
        }

        public void Clear()
        {
            _content = null;
        }
    }

    /// <summary>
    /// プレイリストを文字列に変換する。
    ///
    /// 外部ライブラリに頼らず、タブ区切りの行形式にしている:
    /// <code>
    /// SMP-PLAYLIST\t1
    /// P\t{playlistId}\t{name}
    /// T\t{mediaId}
    /// T\t{mediaId}
    /// P\t{playlistId2}\t{name2}
    /// </code>
    /// </summary>
    public static class PlaylistSerializer
    {
        private const string Header = "SMP-PLAYLIST\t1";

        public static string Serialize(IEnumerable<Playlist> playlists)
        {
            var lines = new List<string> { Header };

            if (playlists != null)
            {
                foreach (var playlist in playlists)
                {
                    if (playlist == null) continue;

                    lines.Add($"P\t{Sanitize(playlist.Id)}\t{Sanitize(playlist.Name)}");
                    foreach (var mediaId in playlist.MediaIds)
                    {
                        lines.Add($"T\t{Sanitize(mediaId)}");
                    }
                }
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// 文字列からプレイリストを復元する。
        /// 形式が違う行は読み飛ばす(壊れた保存データで例外を出さない)。
        /// </summary>
        public static List<Playlist> Deserialize(string content)
        {
            var result = new List<Playlist>();
            if (string.IsNullOrEmpty(content)) return result;

            var lines = content.Replace("\r\n", "\n").Split('\n');
            Playlist current = null;

            foreach (var line in lines)
            {
                if (line.Length == 0 || line.StartsWith("SMP-PLAYLIST")) continue;

                var parts = line.Split('\t');
                if (parts.Length < 2) continue;

                if (parts[0] == "P")
                {
                    string id = parts[1];
                    if (string.IsNullOrWhiteSpace(id)) { current = null; continue; }

                    string name = parts.Length >= 3 ? parts[2] : id;
                    current = new Playlist(id, name);
                    result.Add(current);
                }
                else if (parts[0] == "T" && current != null)
                {
                    current.Add(parts[1]);
                }
            }

            return result;
        }

        /// <summary>区切り文字が混ざっても壊れないようにする。</summary>
        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
