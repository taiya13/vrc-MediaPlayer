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

    /// <summary>取得の結果。成功も失敗も同じ形。</summary>
    public sealed class YouTubeFetchResult
    {
        private YouTubeFetchResult(bool ok, string message, IReadOnlyList<YouTubeVideoInfo> videos)
        {
            Ok = ok;
            Message = message ?? "";
            Videos = videos ?? new YouTubeVideoInfo[0];
        }

        public bool Ok { get; private set; }

        public string Message { get; private set; }

        public IReadOnlyList<YouTubeVideoInfo> Videos { get; private set; }

        public int Count { get { return Videos.Count; } }

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
