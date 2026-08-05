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
        : ICatalogImporter, IIncrementalCatalogImporter, ICatalogImporterSetup,
          ICatalogImporterFilter
    {
        private readonly IYouTubeClient _client;

        /// <summary>1 回で取る上限。0 なら設定側の上限に任せる。</summary>
        public int MaxCount;

        /// <summary>
        /// <b>ショート動画を取り込まない。</b>Phase7-3。既定は入(true)。
        ///
        /// 見分け方は <see cref="YouTubeShortsFilter"/> に任せます。
        /// <b>名指しで 1 本だけ貼られたときは外しません</b> —
        /// 人が自分で選んだものを黙って消すのは「勝手に消えた」でしかないためです。
        /// </summary>
        public bool ExcludeShorts = true;

        /// <summary>直近の取り込みで<b>見た</b>動画の総数。</summary>
        public int LastFetchedTotal { get; private set; }

        /// <summary>直近の取り込みでショートとして外した件数。</summary>
        public int LastShortsExcluded { get; private set; }

        /// <summary>直近の取り込みで実際に取り込んだ通常動画の件数。</summary>
        public int LastImported { get; private set; }

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

        // ───────── 取り込まないもの(Phase7-3)─────────

        public string FilterLabel { get { return "ショート動画は取り込まない"; } }

        public string FilterHint
        {
            get
            {
                return YouTubeShortsFilter.MaxShortSeconds
                       + " 秒以下と、#shorts の印が付いたものを外します。"
                       + "外した件数は取得のあとに出ます"
                       + "(ショートの URL を 1 本だけ貼ったときは外しません)。";
            }
        }

        /// <summary>
        /// ショートを外すか。<b>設定アセットがあればそちらに書きます</b>
        /// (次に開いたときも覚えているように)。
        /// </summary>
        public bool FilterEnabled
        {
            get { return ResolveExcludeShorts(); }
            set
            {
                ExcludeShorts = value;

#if UNITY_EDITOR
                YouTubeApiSettings settings = YouTubeApiSettings.LoadIfPresent();
                if (settings == null || settings.ExcludeShorts == value) return;

                settings.ExcludeShorts = value;
                UnityEditor.EditorUtility.SetDirty(settings);
                UnityEditor.AssetDatabase.SaveAssets();
#endif
            }
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

            // 0 は「上限を決めていない」。ページを繰り返す仕組みでは
            // 「そろったら止まる」の目標が要るので、既定の上限を当てる。
            if (limit <= 0) limit = DefaultMaxItems;

            // 名指しで 1 本だけ貼られたときは、ショートでも外さない。
            // 人が自分で選んだものを黙って消すと「勝手に消えた」になる。
            bool dropShorts = ResolveExcludeShorts()
                              && LastTarget.Kind != YouTubeUrlParser.TargetVideo;

            var harvest = Harvest(client, limit, boundary, dropShorts);
            if (harvest.Error.Length > 0) return CatalogImportResult.Failure(harvest.Error);

            LastVideos = harvest.Seen;
            LastFetchedTotal = harvest.Seen.Count;
            LastShortsExcluded = harvest.Shorts;
            LastImported = harvest.Items.Count;

            string tally = DescribeTally(harvest, dropShorts);

            if (boundary == null)
            {
                return CatalogImportResult.Success(harvest.Items, tally)
                                          .From(DescribeSource(), KindOf(LastTarget));
            }

            // まだ先がある、または上限ぴったりで打ち切った = 続きが残っているかもしれない。
            // 上限で切ったときは「これで全部か」を確かめていないので、
            // 黙って終わると「入れたはずの曲が無い」になる。
            bool mayHaveMore = harvest.HasMore || harvest.Items.Count >= limit;

            CatalogImportResult result = CatalogImportResult
                .Success(harvest.Items,
                         DescribeNew(harvest.Items.Count, harvest.Known, mayHaveMore)
                         + "\n" + tally)
                .From(DescribeSource(), KindOf(LastTarget));

            result.MayHaveMore = mayHaveMore;
            return result;
        }

        // ───────── 目標の件数までそろえる(Phase7-3)─────────

        /// <summary>
        /// <b>1 回の取り込みで集めたもの。</b>
        /// 数を 3 つとも持つのは、<b>取りこぼしを人が確かめられるようにする</b>ためです。
        /// 「100 件のはずが 12 件しか入らない」が起きたとき、
        /// 見た総数・外したショート・入った件数が並んでいれば理由がすぐ分かります。
        /// </summary>
        private sealed class HarvestResult
        {
            public readonly List<CatalogDraftItem> Items = new List<CatalogDraftItem>();
            public readonly List<YouTubeVideoInfo> Seen = new List<YouTubeVideoInfo>();

            /// <summary>ショートとして外した数。</summary>
            public int Shorts;

            /// <summary>再生できない(非公開・削除済み)として外した数。</summary>
            public int Unavailable;

            /// <summary>すでに取り込み済みとして飛ばした数。</summary>
            public int Known;

            /// <summary>まだ先のページが残っているか。</summary>
            public bool HasMore;

            /// <summary>見に行ったページ数。</summary>
            public int Pages;

            public string Error = "";
        }

        /// <summary>
        /// <b>「通常動画を <paramref name="limit"/> 件そろえる」まで、ページを繰り返し取る。</b>
        ///
        /// <b>なぜ要るのか</b><br/>
        /// ショートを外すようにしたら取れる件数が激減しました。
        /// これまでは<b>「100 件見る」</b>だったので、
        /// ショートばかりのチャンネルでは 100 件見て 10 件しか残りませんでした。
        /// 欲しいのは<b>「通常動画を 100 件そろえる」</b>ほうです。
        ///
        /// <b>止まる条件は 3 つ</b>です。どれか 1 つでも当たれば終わります。
        /// <list type="number">
        /// <item>目標の件数に届いた</item>
        /// <item>続きが無い(最後まで見た)</item>
        /// <item><see cref="MaxPages"/> 枚見た …… <b>歯止め</b>。
        ///       ショートしか無いチャンネルで延々と API を叩き続けないため</item>
        /// </list>
        ///
        /// <b>続きから取れない取得係のときは 1 回で終わります</b>
        /// (<see cref="IPagedYouTubeClient"/> を実装していない場合)。
        /// </summary>
        private HarvestResult Harvest(
            IYouTubeClient client, int limit, CatalogImportBoundary boundary, bool dropShorts)
        {
            var harvest = new HarvestResult();

            // 動画 1 本にページは無いので、続きを取る仕組みは使わない。
            var paged = LastTarget.Kind == YouTubeUrlParser.TargetVideo
                ? null
                : client as IPagedYouTubeClient;

            int pageSize = paged != null && paged.PageSize > 0 ? paged.PageSize : limit;

            string pageToken = "";

            for (int page = 0; page < MaxPages; page++)
            {
                YouTubeFetchResult fetched = paged != null
                    ? FetchPage(paged, LastTarget, pageSize, pageToken)
                    : Fetch(client, LastTarget, limit);

                if (fetched == null)
                {
                    harvest.Error = "取得の係が結果を返しませんでした。";
                    return harvest;
                }

                if (!fetched.Ok)
                {
                    // 1 枚目で失敗したら失敗。2 枚目以降なら、
                    // そこまでに集めたものを活かす(全部捨てるほうが困る)。
                    if (page == 0) harvest.Error = fetched.Message;
                    return harvest;
                }

                harvest.Pages = page + 1;
                Absorb(harvest, fetched, boundary, dropShorts, limit);

                pageToken = fetched.NextPageToken;
                harvest.HasMore = fetched.HasMore;

                if (harvest.Items.Count >= limit) break;
                if (paged == null || !fetched.HasMore) break;
            }

            return harvest;
        }

        /// <summary>1 ページぶんを仕分けて取り込む。</summary>
        private static void Absorb(
            HarvestResult harvest, YouTubeFetchResult fetched,
            CatalogImportBoundary boundary, bool dropShorts, int limit)
        {
            for (int i = 0; i < fetched.Videos.Count; i++)
            {
                YouTubeVideoInfo video = fetched.Videos[i];
                if (video == null) continue;

                harvest.Seen.Add(video);

                // 再生できないものは Builder へ渡さない。
                // Seen には「使えません」として残る。
                if (video.IsUnavailable)
                {
                    harvest.Unavailable++;
                    continue;
                }

                // ショートは取り込みの時点で外す(Phase7-3)。
                // 大画面では左右が黒いままですぐ次へ飛ぶため、
                // あとから 1 本ずつ消すのは現実的ではない。
                if (dropShorts && YouTubeShortsFilter.IsShort(video))
                {
                    harvest.Shorts++;
                    continue;
                }

                if (boundary != null && boundary.IsKnown(video.VideoId))
                {
                    harvest.Known++;
                    continue;
                }

                if (harvest.Items.Count >= limit) return;

                harvest.Items.Add(video.ToDraftItem());
            }
        }

        private static YouTubeFetchResult FetchPage(
            IPagedYouTubeClient client, YouTubeUrlParser.Target target,
            int want, string pageToken)
        {
            if (target.Kind == YouTubeUrlParser.TargetPlaylist)
            {
                return client.FetchPlaylistPage(target.Id, want, pageToken);
            }
            if (target.Kind == YouTubeUrlParser.TargetChannelId)
            {
                return client.FetchChannelPage(target.Id, false, want, pageToken);
            }
            if (target.Kind == YouTubeUrlParser.TargetChannelName)
            {
                return client.FetchChannelPage(target.Id, true, want, pageToken);
            }

            // 動画 1 本にページは無い。続きも無い。
            return YouTubeFetchResult.Failure("この指定は 1 ページずつ取れません。");
        }

        /// <summary>
        /// <b>見に行くページ数の上限。</b>
        /// ショートしか投稿していないチャンネルで、目標に届かないまま
        /// 延々と API を叩き続けないための歯止めです。
        /// 1 ページ 50 件なので、最大 1000 件まで見ます。
        /// </summary>
        public const int MaxPages = 20;

        /// <summary>
        /// <see cref="MaxCount"/> を決めていないときに目指す件数。
        /// 「そろったら止まる」ための目標なので、何かしら数が要ります。
        /// </summary>
        public const int DefaultMaxItems = 100;

        /// <summary>
        /// <b>何を見て、何を外して、何が入ったか。</b>
        /// 取りこぼしが起きていないかを人が確かめられるように、3 つとも出します。
        /// </summary>
        private static string DescribeTally(HarvestResult harvest, bool dropShorts)
        {
            var sb = new System.Text.StringBuilder();

            sb.Append("見た動画 ").Append(harvest.Seen.Count).Append(" 件");
            if (harvest.Pages > 1) sb.Append("(").Append(harvest.Pages).Append(" ページ)");

            if (dropShorts)
            {
                sb.Append(" / ショート除外 ").Append(harvest.Shorts).Append(" 件");
            }
            if (harvest.Unavailable > 0)
            {
                sb.Append(" / 再生不可 ").Append(harvest.Unavailable).Append(" 件");
            }
            if (harvest.Known > 0)
            {
                sb.Append(" / 取り込み済み ").Append(harvest.Known).Append(" 件");
            }

            sb.Append(" → 取り込み ").Append(harvest.Items.Count).Append(" 件");

            if (harvest.HasMore)
            {
                sb.Append("\nまだ先があります。もう一度押すと続きから取れます。");
            }
            else
            {
                sb.Append("\n最後まで見ました。");
            }

            return sb.ToString();
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

        /// <summary>
        /// ショートを外すか。設定アセットがあればそちらが優先。
        ///
        /// <b>毎回読み直します。</b>取っておくと、あとから設定を変えても
        /// 効かない(Phase7 で実際に踏んだ問題)ためです。
        /// テストでは設定アセットが無いので、このクラスの
        /// <see cref="ExcludeShorts"/> がそのまま使われます。
        /// </summary>
        private bool ResolveExcludeShorts()
        {
#if UNITY_EDITOR
            YouTubeApiSettings settings = YouTubeApiSettings.LoadIfPresent();
            if (settings != null)
            {
                YouTubeShortsFilter.MaxShortSeconds = settings.MaxShortSeconds;
                YouTubeVideoInfo.CleanTitles = settings.CleanTitles;
                return settings.ExcludeShorts;
            }
#endif
            return ExcludeShorts;
        }
    }
}
