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
    public sealed class YouTubeCatalogImporter
        : ICatalogImporter, IIncrementalCatalogImporter, ICatalogImporterSetup
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
            return Run(input, null);
        }

        // ───────── 使う前の設定(Phase7)─────────

        public bool IsConfigured { get { return IsAvailable; } }

        public string SetupTitle { get { return "YouTube の API キー"; } }

        public string[] SetupSteps
        {
            get
            {
                return new[]
                {
                    "ブラウザで Google Cloud Console を開く (console.cloud.google.com)",
                    "プロジェクトを 1 つ作る (名前は何でも構いません)",
                    "「API とサービス」→「ライブラリ」で YouTube Data API v3 を有効にする",
                    "「API とサービス」→「認証情報」→「認証情報を作成」→「API キー」",
                    "出てきたキーをコピーして、下の欄に貼る",
                };
            }
        }

        public string SecretLabel { get { return "API キー"; } }

        public string SecretValue
        {
#if UNITY_EDITOR
            get
            {
                YouTubeApiSettings settings = YouTubeApiSettings.LoadIfPresent();
                return settings != null ? settings.ApiKey : "";
            }
            set
            {
                YouTubeApiSettings settings = YouTubeApiSettings.LoadOrCreate();
                if (settings == null) return;

                settings.ApiKey = value != null ? value.Trim() : "";

                UnityEditor.EditorUtility.SetDirty(settings);
                UnityEditor.AssetDatabase.SaveAssets();
            }
#else
            get { return ""; }
            set { }
#endif
        }

        public bool HasStorage
        {
#if UNITY_EDITOR
            get { return YouTubeApiSettings.LoadIfPresent() != null; }
#else
            get { return false; }
#endif
        }

        public string StorageLocation
        {
#if UNITY_EDITOR
            get { return HasStorage ? YouTubeApiSettings.AssetPath : ""; }
#else
            get { return ""; }
#endif
        }

        public bool CreateStorage()
        {
#if UNITY_EDITOR
            return YouTubeApiSettings.LoadOrCreate() != null;
#else
            return false;
#endif
        }

        public void RevealStorage()
        {
#if UNITY_EDITOR
            YouTubeApiSettings settings = YouTubeApiSettings.LoadIfPresent();
            if (settings == null) return;

            UnityEditor.Selection.activeObject = settings;
            UnityEditor.EditorGUIUtility.PingObject(settings);
#endif
        }

        // ───────── 新着だけ(Phase6-5)─────────

        /// <summary>
        /// 1 回で見に行く件数。<b>チャンネルは新しい順に返ってきます</b>から、
        /// この数だけ見れば新着はまず入っています。
        /// </summary>
        public const int NewItemsPageSize = 50;

        /// <summary>
        /// <b>新着だけ取る。</b>
        ///
        /// <b>全件は取りません。</b>チャンネルもプレイリストも新しい順に返るので、
        /// <see cref="NewItemsPageSize"/> 件だけ見て、知っているものを落とします。
        /// 400 件のチャンネルなら<b>取得が 8 回から 1 回</b>になります。
        ///
        /// <b>上限いっぱいが全部新しかったときは</b>
        /// <see cref="CatalogImportResult.MayHaveMore"/> を立てます。
        /// 取りこぼしたかもしれないことを黙っておくと、
        /// <b>「入れたはずの曲が無い」</b>になるためです。
        /// </summary>
        public CatalogImportResult ImportNew(string input, CatalogImportBoundary boundary)
        {
            return Run(input, boundary != null ? boundary : new CatalogImportBoundary());
        }

        // ───────── 内部 ─────────

        /// <summary>
        /// 取り込みの本体。<paramref name="boundary"/> が null なら全部、
        /// あれば知らないものだけ。<b>取得と変換の道は 1 本だけ</b>にしてあります。
        /// </summary>
        private CatalogImportResult Run(string input, CatalogImportBoundary boundary)
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

            int limit = MaxCount;
            if (boundary != null)
            {
                limit = boundary.MaxNewItems > 0 ? boundary.MaxNewItems : NewItemsPageSize;
            }

            YouTubeFetchResult fetched = Fetch(client, LastTarget, limit);

            if (fetched == null) return CatalogImportResult.Failure("取得の係が結果を返しませんでした。");
            if (!fetched.Ok) return CatalogImportResult.Failure(fetched.Message);

            LastVideos = fetched.Videos;

            var items = new List<CatalogDraftItem>();
            int skipped = 0;

            for (int i = 0; i < fetched.Videos.Count; i++)
            {
                YouTubeVideoInfo video = fetched.Videos[i];
                if (video == null) continue;

                // 再生できないものは Builder へ渡さない。
                // LastVideos には「使えません」として残る。
                if (video.IsUnavailable) continue;

                if (boundary != null && boundary.IsKnown(video.VideoId))
                {
                    skipped++;
                    continue;
                }

                items.Add(video.ToDraftItem());
            }

            if (boundary == null)
            {
                return CatalogImportResult.Success(items, fetched.Message)
                                          .From(DescribeSource(), KindOf(LastTarget));
            }

            // 上限まで見て 1 件も知っているものが無かった = まだ先にあるかもしれない。
            bool mayHaveMore = skipped == 0 && fetched.Count >= limit;

            CatalogImportResult result = CatalogImportResult
                .Success(items, DescribeNew(items.Count, skipped, mayHaveMore))
                .From(DescribeSource(), KindOf(LastTarget));

            result.MayHaveMore = mayHaveMore;
            return result;
        }

        private static string DescribeNew(int fresh, int skipped, bool mayHaveMore)
        {
            string message = fresh == 0
                ? "新着はありませんでした(すでに " + skipped + " 件は取り込み済み)。"
                : "新着 " + fresh + " 件が見つかりました(" + skipped + " 件は取り込み済み)。";

            if (mayHaveMore)
            {
                message += "\n上限まで全部が新しかったので、まだ先にあるかもしれません。"
                           + "追加したあと、もう一度「新着だけ取る」を押してください。";
            }
            return message;
        }

        /// <summary>取り込み元の人が読む名前。取れたものから拾います。</summary>
        private string DescribeSource()
        {
            if (LastTarget.Kind == YouTubeUrlParser.TargetPlaylist)
            {
                return "再生リスト " + LastTarget.Id;
            }

            // チャンネル名は動画に付いてくる。ID だけより読みやすい。
            for (int i = 0; i < LastVideos.Count; i++)
            {
                if (LastVideos[i] == null) continue;
                if (string.IsNullOrEmpty(LastVideos[i].ChannelTitle)) continue;

                return LastVideos[i].ChannelTitle;
            }

            return LastTarget.Describe();
        }

        private static string KindOf(YouTubeUrlParser.Target target)
        {
            if (target.Kind == YouTubeUrlParser.TargetPlaylist) return CatalogSubscription.KindPlaylist;
            if (target.Kind == YouTubeUrlParser.TargetVideo) return CatalogSubscription.KindSingle;

            return CatalogSubscription.KindChannel;
        }

        private YouTubeFetchResult Fetch(
            IYouTubeClient client, YouTubeUrlParser.Target target, int maxCount)
        {
            if (target.Kind == YouTubeUrlParser.TargetPlaylist)
                return client.FetchPlaylist(target.Id, maxCount);

            if (target.Kind == YouTubeUrlParser.TargetChannelId)
                return client.FetchChannel(target.Id, false, maxCount);

            if (target.Kind == YouTubeUrlParser.TargetChannelName)
                return client.FetchChannel(target.Id, true, maxCount);

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
