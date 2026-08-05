using System.Collections.Generic;

namespace SmartMediaPlatform.CatalogBuilder.YouTube
{
    /// <summary>
    /// <b>YouTube から取ってくる係の口。</b>Phase6-2。
    ///
    /// <b>なぜ取得を別クラスに分けたのか</b><br/>
    /// <see cref="YouTubeCatalogImporter"/> は「入力を見分けて、取れたものを並べ直す」だけにし、
    /// <b>通信のやり方はここへ閉じ込めます</b>。おかげで
    /// <list type="bullet">
    /// <item>Data API が v4 になっても、直すのは実装 1 つ</item>
    /// <item>API キー無しの取得方法に替えても、Importer は無変更</item>
    /// <item><b>偽物を差せばテストできる</b>(実際そうしています)</item>
    /// </list>
    ///
    /// <b>例外を投げないでください。</b>失敗は <see cref="YouTubeFetchResult"/> に入れて返します
    /// (Phase6-1 で <c>ICatalogImporter</c> に決めた約束と同じ)。
    /// </summary>
    public interface IYouTubeClient
    {
        /// <summary>いま使えるか(API キーが入っているかなど)。</summary>
        bool IsAvailable { get; }

        /// <summary>使えない理由。使えるときは空文字。</summary>
        string UnavailableReason { get; }

        /// <summary>再生リストの中身を取る。</summary>
        YouTubeFetchResult FetchPlaylist(string playlistId, int maxCount);

        /// <summary>
        /// チャンネルの投稿を取る。
        /// <paramref name="byName"/> が true なら <c>@名前</c>、false なら <c>UC…</c> の ID。
        /// </summary>
        YouTubeFetchResult FetchChannel(string channel, bool byName, int maxCount);

        /// <summary>動画 1 本を取る。</summary>
        YouTubeFetchResult FetchVideo(string videoId);
    }

    /// <summary>
    /// <b>続きから取れる取得係の口。</b>Phase7-3。<b>付けても付けなくても構いません。</b>
    ///
    /// <b>なぜ要るのか</b><br/>
    /// ショートを外すようにしたら、<b>取れる件数が激減しました</b>。
    /// 「100 件取る」が「100 件<b>見て</b>、そのうち通常動画だけ残す」だったためです。
    /// ショートばかりのチャンネルでは 100 件見て 10 件しか残りません。
    ///
    /// 欲しいのは<b>「通常動画を 100 件そろえる」</b>ほうです。
    /// そのためには「足りなければ次のページも見る」必要があり、
    /// <b>続きの位置(pageToken)</b>を外へ出す口が要ります。
    ///
    /// <b>どこまでが誰の仕事か</b>は変えません。
    /// <list type="bullet">
    /// <item>ここ(取得係)…… ページを 1 枚取ってきて、次の位置を返す</item>
    /// <item>取り込み側 …… 何を外すか決めて、足りなければもう 1 枚頼む</item>
    /// </list>
    /// 取得係は<b>ショートという言葉を知りません</b>。
    /// </summary>
    public interface IPagedYouTubeClient
    {
        /// <summary>1 ページぶんの目安(API の上限)。</summary>
        int PageSize { get; }

        /// <summary>
        /// 再生リストを 1 ページぶん取る。
        /// <paramref name="pageToken"/> が空なら先頭から。
        /// </summary>
        YouTubeFetchResult FetchPlaylistPage(string playlistId, int want, string pageToken);

        /// <summary>チャンネルの投稿を 1 ページぶん取る。</summary>
        YouTubeFetchResult FetchChannelPage(
            string channel, bool byName, int want, string pageToken);
    }

    /// <summary>取得の結果。成功も失敗も同じ形。</summary>
    public sealed class YouTubeFetchResult
    {
        private YouTubeFetchResult(bool ok, string message, IReadOnlyList<YouTubeVideoInfo> videos)
        {
            Ok = ok;
            Message = message ?? "";
            Videos = videos ?? new YouTubeVideoInfo[0];
            NextPageToken = "";
        }

        public bool Ok { get; private set; }

        public string Message { get; private set; }

        public IReadOnlyList<YouTubeVideoInfo> Videos { get; private set; }

        public int Count { get { return Videos.Count; } }

        /// <summary>
        /// 続きの位置。空なら<b>ここで終わり</b>。
        /// <see cref="IPagedYouTubeClient"/> で 1 ページずつ取るときだけ入ります。
        /// </summary>
        public string NextPageToken { get; private set; }

        /// <summary>まだ先があるか。</summary>
        public bool HasMore { get { return NextPageToken.Length > 0; } }

        /// <summary>続きの位置を付けて返す(取得係が使う)。</summary>
        public YouTubeFetchResult WithNextPage(string pageToken)
        {
            NextPageToken = pageToken != null ? pageToken : "";
            return this;
        }

        public static YouTubeFetchResult Success(IReadOnlyList<YouTubeVideoInfo> videos, string message)
        {
            return new YouTubeFetchResult(true, message, videos);
        }

        public static YouTubeFetchResult Failure(string message)
        {
            return new YouTubeFetchResult(false, message, null);
        }
    }
}
