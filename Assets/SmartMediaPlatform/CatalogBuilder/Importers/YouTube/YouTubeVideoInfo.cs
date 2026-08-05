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
        /// 動画に付いているタグ。Phase6-4 で足しました。
        /// <b>付いていない動画も多い</b>ので、空を前提に扱ってください。
        /// </summary>
        public string[] Tags = new string[0];

        /// <summary>カテゴリ番号。<see cref="YouTubeCategoryNames"/> でジャンル名に直します。</summary>
        public string CategoryId = "";

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

        /// <summary>
        /// 見出しを整えるか。Phase7-3。既定は入。
        /// 切ると、YouTube の見出しがそのまま入ります。
        /// </summary>
        public static bool CleanTitles = true;

        /// <summary>
        /// アーティスト名を決める。
        /// 見出しが「歌手 - 曲名」の形ならそちらを、無ければチャンネル名を使います。
        /// </summary>
        public string ResolveArtist()
        {
            if (!CleanTitles) return ChannelTitle;

            string fromTitle = YouTubeTitleCleaner.ArtistFromTitle(Title);
            return fromTitle.Length > 0 ? fromTitle : ChannelTitle;
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

            // ── 見出しから、頭のチャンネル名と末尾の飾りを落とす(Phase7-3)。
            //
            //    1 つのチャンネルを丸ごと取り込むと、全部の見出しの頭に
            //    <b>同じ名前が並びます</b>。一覧はチャンネルでまとめてあるので、
            //    その名前は見出しにもう出ていて、二重です。
            //    選ぶときに見たいのは曲名なので、そちらを前へ出します。
            //    落とす名前は<b>アーティストとして採ったほうの名前</b>です。
            //    チャンネル名で試すだけだと、レーベルのチャンネル
            //    (見出しは「歌手 - 曲名」、チャンネル名は別)で落とせません。
            string artist = ResolveArtist();

            item.Title = CleanTitles ? YouTubeTitleCleaner.Clean(Title, artist) : Title;

            // アーティストは、見出しに「歌手 - 曲名」の形があればそちらを採る。
            // 音楽レーベルのチャンネルには、いろいろな歌手が入っているためです。
            item.Artist = artist;
            item.Url = WatchUrl;
            item.DurationSeconds = DurationSeconds;
            item.ThumbnailPath = ThumbnailUrl;
            item.Source = "YouTube";

            // Phase6-4: 関連(RelatedIds)と絞り込みの材料。
            item.Tags = TrimTags(MaxTags);

            // Phase6-5: 並べ替えの材料。
            item.PublishedAt = PublishedAt;

            // Phase6-6: ジャンルはこちらで決める。
            // YouTube のカテゴリは「音楽」しか返さないので、そのまま入れると
            // 200 本すべてが「音楽」になり、絞り込みにも「おすすめ」にも使えない。
            // ここは材料を詰めて呼ぶだけで、判定は GenreClassifier の仕事。
            item.Genre = Genres.GenreClassifier.Shared.Classify(ToGenreSignals());

            return item;
        }

        /// <summary>
        /// ジャンル判定の材料を渡す形にする。
        /// <b>カテゴリは番号ではなく言葉で渡します</b> —
        /// 判定側に YouTube の事情(番号の意味)を持ち込まないためです。
        /// </summary>
        public Genres.GenreSignals ToGenreSignals()
        {
            var signals = new Genres.GenreSignals();

            signals.Title = Title;
            signals.ChannelName = ChannelTitle;
            signals.Description = Description;
            signals.Tags = Tags != null ? Tags : new string[0];
            signals.SourceCategory = YouTubeCategoryNames.Of(CategoryId);

            return signals;
        }

        /// <summary>
        /// 持ち込むタグの上限。
        /// YouTube は 30 個以上付いていることがあり、そのまま入れると
        /// <b>編集画面がタグで埋まって使えなくなります</b>。
        /// </summary>
        public const int MaxTags = 8;

        private string[] TrimTags(int limit)
        {
            if (Tags == null || Tags.Length == 0) return new string[0];

            int take = Tags.Length < limit ? Tags.Length : limit;

            var result = new System.Collections.Generic.List<string>();
            for (int i = 0; i < Tags.Length && result.Count < take; i++)
            {
                string tag = Tags[i];
                if (string.IsNullOrWhiteSpace(tag)) continue;

                result.Add(tag.Trim());
            }
            return result.ToArray();
        }
    }
}
