using System;

namespace SmartMediaPlatform.CatalogBuilder.YouTube
{
    /// <summary>
    /// <b>YouTube から取れた 1 本ぶん。</b>Phase6-2。
    ///
    /// <b>ここは YouTube の言葉のまま持ちます。</b>
    /// <c>CatalogDraftItem</c> へ移すのは <see cref="ToDraftItem"/> の 1 か所だけにしてあり、
    /// <b>API の形が変わっても直すのはそこだけ</b>で済みます。
    /// </summary>
    public sealed class YouTubeVideoInfo
    {
        public string VideoId = "";
        public string Title = "";
        public string ChannelTitle = "";
        public string ChannelId = "";

        /// <summary>公開日。取れなければ空文字(そのまま表示する)。</summary>
        public string PublishedAt = "";

        /// <summary>再生時間(秒)。取れなければ 0。</summary>
        public int DurationSeconds;

        /// <summary>いちばん大きいサムネイルの URL。</summary>
        public string ThumbnailUrl = "";

        public string Description = "";

        /// <summary>
        /// 非公開・削除済みなど、再生できないもの。
        /// <b>取得結果から黙って消さずに、印を付けて残します</b> —
        /// 「入れたはずの曲が無い」より「これは使えません」と出るほうが分かるためです。
        /// </summary>
        public bool IsUnavailable;

        public string UnavailableReason = "";

        /// <summary>再生に使う URL。</summary>
        public string WatchUrl
        {
            get
            {
                return VideoId.Length == 0 ? "" : "https://www.youtube.com/watch?v=" + VideoId;
            }
        }

        /// <summary>「3:45」の形。</summary>
        public string FormatDuration()
        {
            if (DurationSeconds <= 0) return "--:--";

            int hours = DurationSeconds / 3600;
            int minutes = DurationSeconds % 3600 / 60;
            int seconds = DurationSeconds % 60;

            string tail = seconds < 10 ? "0" + seconds : "" + seconds;
            if (hours <= 0) return minutes + ":" + tail;

            string mid = minutes < 10 ? "0" + minutes : "" + minutes;
            return hours + ":" + mid + ":" + tail;
        }

        /// <summary>公開日を「2024-05-01」の形で。取れなければ空文字。</summary>
        public string FormatPublishedDate()
        {
            if (PublishedAt.Length < 10) return PublishedAt;
            return PublishedAt.Substring(0, 10);
        }

        /// <summary>
        /// Builder の形へ移す。<b>Phase6-2 では Catalog へ書きません</b>が、
        /// プレビューの表示と Phase6-3 の反映が同じ変換を使うようにしておきます。
        /// </summary>
        public CatalogDraftItem ToDraftItem()
        {
            var item = new CatalogDraftItem();

            // ID は動画 ID をそのまま。もとが一意なので、重複はまず起きない。
            item.Id = VideoId;
            item.Title = Title;
            item.Artist = ChannelTitle;
            item.Url = WatchUrl;
            item.DurationSeconds = DurationSeconds;
            item.ThumbnailPath = ThumbnailUrl;
            item.Source = "YouTube";

            return item;
        }
    }
}
