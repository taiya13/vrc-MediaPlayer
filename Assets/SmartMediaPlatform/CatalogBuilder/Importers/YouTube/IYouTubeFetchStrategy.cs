namespace SmartMediaPlatform.CatalogBuilder.YouTube
{
    /// <summary>
    /// <b>「どの順で取ってくるか」の担当。</b>Phase7-4。
    ///
    /// <b>なぜ if で分けないのか</b><br/>
    /// 取り込みの流れは、順番が変わっても<b>まったく同じ</b>です。
    /// <list type="number">
    /// <item>動画 ID を並べて取る ← <b>ここだけが違う</b></item>
    /// <item><c>videos.list</c> で見出し・長さ・タグを引く</item>
    /// <item>重複を外す / ショートを外す / 生配信を外す</item>
    /// <item>ジャンルを判定して Builder の形へ移す</item>
    /// </list>
    /// 違うのは 1 番だけなので、そこだけを差し替えられる形にします。
    /// <c>if (人気順) … else …</c> を流れの途中に置くと、
    /// <b>3 番目・4 番目の道が 2 本に増えます</b>。
    /// 増えた道は片方しか試されないまま腐るので、分岐は入口の 1 か所に閉じ込めます。
    ///
    /// <b>ここは「1 ページ取る」だけです。</b>
    /// 何件そろえるか・足りなければ次を見るか、という判断は
    /// <see cref="YouTubeCatalogImporter"/> が持ちます。
    /// </summary>
    public interface IYouTubeFetchStrategy
    {
        /// <summary>選ぶときに出す名前(「新着順」「人気順」)。</summary>
        string DisplayName { get; }

        /// <summary>この指定に使えるか。</summary>
        bool CanHandle(YouTubeUrlParser.Target target);

        /// <summary>使えないときの理由。使えるなら空文字。</summary>
        string UnsupportedReason(YouTubeUrlParser.Target target);

        /// <summary>
        /// 1 ページぶん取る。<paramref name="pageToken"/> が空なら先頭から。
        /// 続きの位置は <see cref="YouTubeFetchResult.NextPageToken"/> に入れて返します。
        /// </summary>
        YouTubeFetchResult FetchPage(
            IYouTubeClient client, YouTubeUrlParser.Target target, int want, string pageToken);
    }

    /// <summary>取ってくる順。</summary>
    public static class YouTubeFetchOrder
    {
        /// <summary>新しい順。<b>いままでどおり</b>で、これが既定。</summary>
        public const int Latest = 0;

        /// <summary>再生数の多い順。</summary>
        public const int Popular = 1;

        public static readonly string[] Labels = { "新着順", "人気順" };

        /// <summary>その順を担当するもの。</summary>
        public static IYouTubeFetchStrategy Resolve(int order)
        {
            if (order == Popular) return Popular_;
            return Latest_;
        }

        private static readonly IYouTubeFetchStrategy Latest_ = new YouTubeLatestStrategy();
        private static readonly IYouTubeFetchStrategy Popular_ = new YouTubePopularStrategy();
    }

    /// <summary>
    /// <b>新しい順。</b>チャンネルの「アップロード用の再生リスト」を頭から読みます。
    ///
    /// <b>Phase7-3 までの唯一のやり方</b>で、中身は変えていません。
    /// 取りこぼしが無く、API の割り当ても軽い(1 ページ 1 単位)ので、これが既定です。
    /// </summary>
    public sealed class YouTubeLatestStrategy : IYouTubeFetchStrategy
    {
        public string DisplayName { get { return "新着順"; } }

        public bool CanHandle(YouTubeUrlParser.Target target)
        {
            return target.Kind == YouTubeUrlParser.TargetPlaylist
                   || target.Kind == YouTubeUrlParser.TargetChannelId
                   || target.Kind == YouTubeUrlParser.TargetChannelName;
        }

        public string UnsupportedReason(YouTubeUrlParser.Target target)
        {
            return CanHandle(target) ? "" : "この指定は 1 ページずつ取れません。";
        }

        public YouTubeFetchResult FetchPage(
            IYouTubeClient client, YouTubeUrlParser.Target target, int want, string pageToken)
        {
            var paged = client as IPagedYouTubeClient;
            if (paged == null)
            {
                return YouTubeFetchResult.Failure("この取得の係は続きから取れません。");
            }

            if (target.Kind == YouTubeUrlParser.TargetPlaylist)
            {
                return paged.FetchPlaylistPage(target.Id, want, pageToken);
            }
            if (target.Kind == YouTubeUrlParser.TargetChannelId)
            {
                return paged.FetchChannelPage(target.Id, false, want, pageToken);
            }
            if (target.Kind == YouTubeUrlParser.TargetChannelName)
            {
                return paged.FetchChannelPage(target.Id, true, want, pageToken);
            }

            return YouTubeFetchResult.Failure(UnsupportedReason(target));
        }
    }

    /// <summary>
    /// <b>人気順(再生数の多い順)。</b>Phase7-4。
    ///
    /// <c>search.list</c> に <c>order=viewCount</c> を付けて動画 ID を並べ、
    /// そこから先は<b>いままでとまったく同じ道</b>(<c>videos.list</c> で
    /// 見出し・長さ・タグを引き、ショートと生配信を外し、ジャンルを判定)を通ります。
    ///
    /// <b>公式 MV が上に来ます。</b>公式かどうかを判定する仕組みは要りません —
    /// 再生数で並べると、そのチャンネルでいちばん見られているもの、
    /// つまり MV や公式動画が自然に上へ来るためです。
    ///
    /// <b>再生リストには使えません。</b><c>search.list</c> が見るのはチャンネルで、
    /// 再生リストの中を再生数順に並べる口が API にありません。
    /// その場合は新着順のまま取ります(黙って別のものを取らないため、理由を返します)。
    ///
    /// <b>API の割り当てを多く使います。</b><c>search.list</c> は 1 回 100 単位で、
    /// <c>playlistItems</c>(1 単位)の 100 倍です。
    /// 件数を選べるようにしてあるのはこのためで、既定は 50 件にしてあります。
    /// </summary>
    public sealed class YouTubePopularStrategy : IYouTubeFetchStrategy
    {
        public string DisplayName { get { return "人気順"; } }

        public bool CanHandle(YouTubeUrlParser.Target target)
        {
            return target.Kind == YouTubeUrlParser.TargetChannelId
                   || target.Kind == YouTubeUrlParser.TargetChannelName;
        }

        public string UnsupportedReason(YouTubeUrlParser.Target target)
        {
            if (CanHandle(target)) return "";

            if (target.Kind == YouTubeUrlParser.TargetPlaylist)
            {
                return "再生リストは人気順で取れません(YouTube 側に並べ替える口がありません)。"
                       + "新着順で取ってください。";
            }
            return "この指定は人気順で取れません。";
        }

        public YouTubeFetchResult FetchPage(
            IYouTubeClient client, YouTubeUrlParser.Target target, int want, string pageToken)
        {
            var searching = client as ISearchingYouTubeClient;
            if (searching == null)
            {
                return YouTubeFetchResult.Failure("この取得の係は人気順に対応していません。");
            }

            if (!CanHandle(target)) return YouTubeFetchResult.Failure(UnsupportedReason(target));

            bool byName = target.Kind == YouTubeUrlParser.TargetChannelName;
            return searching.SearchChannelPage(target.Id, byName, OrderViewCount, want, pageToken);
        }

        /// <summary>YouTube Data API の <c>order</c> に渡す値。</summary>
        public const string OrderViewCount = "viewCount";
    }
}
