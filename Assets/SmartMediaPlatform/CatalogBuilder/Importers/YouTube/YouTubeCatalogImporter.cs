using System.Collections.Generic;

namespace SmartMediaPlatform.CatalogBuilder.YouTube
{
    /// <summary>
    /// <b>YouTube 取り込み。</b>Phase6-2。Phase6-1 の <see cref="ICatalogImporter"/> の実装です。
    ///
    /// <b>やることは 2 つだけ</b>です:
    /// <list type="number">
    /// <item>貼られた文字列が何を指すか見分ける(<see cref="YouTubeUrlParser"/>)</item>
    /// <item>取ってきたものを Builder の形へ並べ直す</item>
    /// </list>
    /// <b>通信はしません。</b>取得は <see cref="IYouTubeClient"/> の仕事です。
    /// おかげで API が変わっても、キー無しの方法に替えても、ここは無変更です。
    ///
    /// <b>Phase6-2 では Catalog へ書きません。</b>
    /// <see cref="LastVideos"/> にそのまま残るので、
    /// プレビュー窓がそれを見ます(反映は Phase6-3)。
    /// </summary>
    public sealed class YouTubeCatalogImporter : ICatalogImporter
    {
        private readonly IYouTubeClient _client;

        /// <summary>1 回で取る上限。0 なら設定側の上限に任せる。</summary>
        public int MaxCount;

        /// <summary>
        /// 直近に取れた生の一覧。<b>プレビューはこれを見ます。</b>
        /// <c>CatalogImportResult</c> は <c>CatalogDraftItem</c> しか運べず、
        /// 公開日やチャンネル名が落ちてしまうため、別に残しています。
        /// </summary>
        public IReadOnlyList<YouTubeVideoInfo> LastVideos { get; private set; }

        /// <summary>直近に見分けた対象(プレビューの見出しに出す)。</summary>
        public YouTubeUrlParser.Target LastTarget { get; private set; }

        /// <summary>既定の作り。<c>CatalogImporterRegistry</c> はこちらを使う。</summary>
        public YouTubeCatalogImporter() : this(null)
        {
        }

        /// <summary>取得の係を差し替える(テストで偽物を入れるため)。</summary>
        public YouTubeCatalogImporter(IYouTubeClient client)
        {
            _client = client;
            LastVideos = new YouTubeVideoInfo[0];
            LastTarget = new YouTubeUrlParser.Target();
        }

        /// <summary>いま使っている取得の係。</summary>
        public IYouTubeClient Client { get { return ResolveClient(); } }

        // ───────── ICatalogImporter ─────────

        public string DisplayName { get { return "YouTube"; } }

        public string InputHint
        {
            get
            {
                return "チャンネル URL / 再生リスト URL / 動画 URL を貼ってください"
                       + "(@名前・PL… などの ID だけでも可)";
            }
        }

        public bool IsAvailable
        {
            get
            {
                IYouTubeClient client = ResolveClient();
                return client != null && client.IsAvailable;
            }
        }

        public string UnavailableReason
        {
            get
            {
                IYouTubeClient client = ResolveClient();
                if (client == null) return "取得の係がありません。";
                return client.UnavailableReason;
            }
        }

        public bool CanImport(string input)
        {
            return YouTubeUrlParser.Parse(input).IsValid;
        }

        public CatalogImportResult Import(string input)
        {
            LastVideos = new YouTubeVideoInfo[0];
            LastTarget = YouTubeUrlParser.Parse(input);

            if (!LastTarget.IsValid)
            {
                return CatalogImportResult.Failure(
                    "URL を読めませんでした。\n"
                    + "チャンネル(youtube.com/@名前 か /channel/UC…)、"
                    + "再生リスト(?list=PL…)、動画(watch?v=… か youtu.be/…)に対応しています。");
            }

            IYouTubeClient client = ResolveClient();
            if (client == null) return CatalogImportResult.Failure("取得の係がありません。");
            if (!client.IsAvailable) return CatalogImportResult.Failure(client.UnavailableReason);

            YouTubeFetchResult fetched = Fetch(client, LastTarget);

            if (fetched == null) return CatalogImportResult.Failure("取得の係が結果を返しませんでした。");
            if (!fetched.Ok) return CatalogImportResult.Failure(fetched.Message);

            LastVideos = fetched.Videos;

            var items = new List<CatalogDraftItem>();
            for (int i = 0; i < fetched.Videos.Count; i++)
            {
                YouTubeVideoInfo video = fetched.Videos[i];
                if (video == null) continue;

                // 再生できないものは Builder へ渡さない。
                // プレビューには LastVideos 経由で「使えません」として残る。
                if (video.IsUnavailable) continue;

                items.Add(video.ToDraftItem());
            }

            return CatalogImportResult.Success(items, fetched.Message);
        }

        // ───────── 内部 ─────────

        private YouTubeFetchResult Fetch(IYouTubeClient client, YouTubeUrlParser.Target target)
        {
            if (target.Kind == YouTubeUrlParser.TargetPlaylist)
                return client.FetchPlaylist(target.Id, MaxCount);

            if (target.Kind == YouTubeUrlParser.TargetChannelId)
                return client.FetchChannel(target.Id, false, MaxCount);

            if (target.Kind == YouTubeUrlParser.TargetChannelName)
                return client.FetchChannel(target.Id, true, MaxCount);

            if (target.Kind == YouTubeUrlParser.TargetVideo)
                return client.FetchVideo(target.Id);

            return YouTubeFetchResult.Failure("対応していない指定です。");
        }

        private IYouTubeClient _resolved;

        /// <summary>
        /// 取得の係を決める。差し替えられていなければ Data API を使います。
        ///
        /// <c>#if UNITY_EDITOR</c> で分けているのは、
        /// <see cref="YouTubeDataApiClient"/> がエディタ専用(通信と設定アセット)だからです。
        /// </summary>
        private IYouTubeClient ResolveClient()
        {
            if (_client != null) return _client;
            if (_resolved != null) return _resolved;

#if UNITY_EDITOR
            _resolved = new YouTubeDataApiClient();
#endif
            return _resolved;
        }
    }
}
