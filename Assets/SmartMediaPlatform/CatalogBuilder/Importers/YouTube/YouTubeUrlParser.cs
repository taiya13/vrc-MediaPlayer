namespace SmartMediaPlatform.CatalogBuilder.YouTube
{
    /// <summary>
    /// <b>貼られた文字列が何を指しているかを見分ける。</b>Phase6-2。
    ///
    /// <b>ネットワークに触りません。</b>文字列を見るだけなので EditMode で検証できます。
    /// YouTube の URL は形が多く、取り違えると<b>「取得できません」としか出せなくなる</b>ので、
    /// ここだけ切り出してテストで固めています。
    ///
    /// 受け付ける形:
    /// <list type="bullet">
    /// <item><c>youtube.com/playlist?list=PL…</c> / <c>watch?v=…&amp;list=PL…</c> → 再生リスト</item>
    /// <item><c>youtube.com/channel/UC…</c> → チャンネル(ID)</item>
    /// <item><c>youtube.com/@name</c> / <c>/c/Name</c> / <c>/user/Name</c> → チャンネル(名前)</item>
    /// <item><c>youtu.be/xxx</c> / <c>watch?v=xxx</c> → 動画 1 本</item>
    /// <item>ID をそのまま貼った場合(<c>PL…</c> / <c>UC…</c> / <c>@name</c>)</item>
    /// </list>
    ///
    /// <b>再生リストが動画より優先</b>です。<c>watch?v=…&amp;list=…</c> は
    /// 「再生リストの途中を開いた URL」なので、欲しいのは一覧のほうだからです。
    /// </summary>
    public static class YouTubeUrlParser
    {
        public const int TargetUnknown = 0;
        public const int TargetPlaylist = 1;
        public const int TargetChannelId = 2;
        public const int TargetChannelName = 3;
        public const int TargetVideo = 4;

        /// <summary>見分けた結果。</summary>
        public sealed class Target
        {
            public int Kind = TargetUnknown;

            /// <summary>再生リスト ID / チャンネル ID / チャンネル名 / 動画 ID。</summary>
            public string Id = "";

            public bool IsValid { get { return Kind != TargetUnknown && Id.Length > 0; } }

            /// <summary>画面に出す 1 行。</summary>
            public string Describe()
            {
                if (Kind == TargetPlaylist) return "再生リスト " + Id;
                if (Kind == TargetChannelId) return "チャンネル " + Id;
                if (Kind == TargetChannelName) return "チャンネル @" + Id;
                if (Kind == TargetVideo) return "動画 " + Id;
                return "(判別できません)";
            }
        }

        public static Target Parse(string input)
        {
            var result = new Target();
            if (string.IsNullOrWhiteSpace(input)) return result;

            string text = input.Trim();

            // ── ID だけ貼られた場合
            if (!text.Contains("/") && !text.Contains("?"))
            {
                if (text.StartsWith("@") && text.Length > 1)
                {
                    result.Kind = TargetChannelName;
                    result.Id = text.Substring(1);
                    return result;
                }
                if (text.StartsWith("PL") || text.StartsWith("UU") || text.StartsWith("OL")
                    || text.StartsWith("FL") || text.StartsWith("RD"))
                {
                    result.Kind = TargetPlaylist;
                    result.Id = text;
                    return result;
                }
                if (text.StartsWith("UC") && text.Length >= 20)
                {
                    result.Kind = TargetChannelId;
                    result.Id = text;
                    return result;
                }
                if (text.Length == 11)
                {
                    result.Kind = TargetVideo;
                    result.Id = text;
                    return result;
                }
                return result;
            }

            // ── URL
            string list = QueryValue(text, "list");
            if (list.Length > 0)
            {
                // 「再生リストの途中を開いた URL」でも、欲しいのは一覧のほう。
                result.Kind = TargetPlaylist;
                result.Id = list;
                return result;
            }

            string video = QueryValue(text, "v");
            if (video.Length > 0)
            {
                result.Kind = TargetVideo;
                result.Id = video;
                return result;
            }

            string path = PathOf(text);

            string channel = SegmentAfter(path, "channel");
            if (channel.Length > 0)
            {
                result.Kind = TargetChannelId;
                result.Id = channel;
                return result;
            }

            string user = SegmentAfter(path, "user");
            if (user.Length == 0) user = SegmentAfter(path, "c");
            if (user.Length > 0)
            {
                result.Kind = TargetChannelName;
                result.Id = user;
                return result;
            }

            // youtu.be/xxxx と youtube.com/@name と youtube.com/shorts/xxxx
            string shorts = SegmentAfter(path, "shorts");
            if (shorts.Length > 0)
            {
                result.Kind = TargetVideo;
                result.Id = shorts;
                return result;
            }

            string first = FirstSegment(path);
            if (first.StartsWith("@") && first.Length > 1)
            {
                result.Kind = TargetChannelName;
                result.Id = first.Substring(1);
                return result;
            }

            if (text.Contains("youtu.be/") && first.Length > 0)
            {
                result.Kind = TargetVideo;
                result.Id = first;
                return result;
            }

            return result;
        }

        // ───────── 文字列を切る ─────────

        /// <summary><c>?a=1&amp;b=2</c> から欲しい値を取り出す。無ければ空文字。</summary>
        public static string QueryValue(string url, string key)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key)) return "";

            int mark = url.IndexOf('?');
            if (mark < 0) return "";

            string query = url.Substring(mark + 1);
            string[] parts = query.Split('&');

            for (int i = 0; i < parts.Length; i++)
            {
                int equals = parts[i].IndexOf('=');
                if (equals <= 0) continue;

                if (parts[i].Substring(0, equals) != key) continue;
                return parts[i].Substring(equals + 1);
            }
            return "";
        }

        /// <summary>ホスト名と ? 以降を落として、パスだけにする。</summary>
        private static string PathOf(string url)
        {
            string text = url;

            int scheme = text.IndexOf("://");
            if (scheme >= 0) text = text.Substring(scheme + 3);

            int query = text.IndexOf('?');
            if (query >= 0) text = text.Substring(0, query);

            int slash = text.IndexOf('/');
            if (slash < 0) return "";

            return text.Substring(slash + 1).TrimEnd('/');
        }

        /// <summary><paramref name="name"/> の次の区切りを返す。</summary>
        private static string SegmentAfter(string path, string name)
        {
            if (string.IsNullOrEmpty(path)) return "";

            string[] parts = path.Split('/');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i] == name) return parts[i + 1];
            }
            return "";
        }

        private static string FirstSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";

            string[] parts = path.Split('/');
            return parts.Length > 0 ? parts[0] : "";
        }
    }
}
