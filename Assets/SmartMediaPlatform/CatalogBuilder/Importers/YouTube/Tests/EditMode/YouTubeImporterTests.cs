using System.Collections.Generic;
using NUnit.Framework;
using SmartMediaPlatform.CatalogBuilder;
using SmartMediaPlatform.CatalogBuilder.YouTube;

namespace SmartMediaPlatform.CatalogBuilder.YouTube.Tests
{
    /// <summary>
    /// Phase6-2: YouTube 取り込みの検証。
    ///
    /// <b>通信はしません。</b><see cref="IYouTubeClient"/> に偽物を差して確かめます
    /// (取得を別クラスに分けた効果がここで出ます)。
    /// </summary>
    public sealed class YouTubeImporterTests
    {
        // ───────── URL の見分け ─────────

        [Test]
        public void PlaylistUrlsAreRecognised()
        {
            var t = YouTubeUrlParser.Parse("https://www.youtube.com/playlist?list=PLabc123");

            Assert.AreEqual(YouTubeUrlParser.TargetPlaylist, t.Kind);
            Assert.AreEqual("PLabc123", t.Id);
        }

        [Test]
        public void APlaylistWinsOverTheVideoInTheSameUrl()
        {
            // 「再生リストの途中を開いた URL」。欲しいのは一覧のほう。
            var t = YouTubeUrlParser.Parse("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PLxyz");

            Assert.AreEqual(YouTubeUrlParser.TargetPlaylist, t.Kind);
            Assert.AreEqual("PLxyz", t.Id);
        }

        [Test]
        public void VideoUrlsAreRecognised()
        {
            var watch = YouTubeUrlParser.Parse("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
            Assert.AreEqual(YouTubeUrlParser.TargetVideo, watch.Kind);
            Assert.AreEqual("dQw4w9WgXcQ", watch.Id);

            var shortUrl = YouTubeUrlParser.Parse("https://youtu.be/dQw4w9WgXcQ");
            Assert.AreEqual(YouTubeUrlParser.TargetVideo, shortUrl.Kind);
            Assert.AreEqual("dQw4w9WgXcQ", shortUrl.Id);

            var shorts = YouTubeUrlParser.Parse("https://www.youtube.com/shorts/abcdefghijk");
            Assert.AreEqual(YouTubeUrlParser.TargetVideo, shorts.Kind);
            Assert.AreEqual("abcdefghijk", shorts.Id);
        }

        [Test]
        public void ChannelUrlsAreRecognised()
        {
            var byId = YouTubeUrlParser.Parse("https://www.youtube.com/channel/UC1234567890abcdefghij");
            Assert.AreEqual(YouTubeUrlParser.TargetChannelId, byId.Kind);
            Assert.AreEqual("UC1234567890abcdefghij", byId.Id);

            var byHandle = YouTubeUrlParser.Parse("https://www.youtube.com/@someone");
            Assert.AreEqual(YouTubeUrlParser.TargetChannelName, byHandle.Kind);
            Assert.AreEqual("someone", byHandle.Id);

            var legacy = YouTubeUrlParser.Parse("https://www.youtube.com/user/OldName");
            Assert.AreEqual(YouTubeUrlParser.TargetChannelName, legacy.Kind);
            Assert.AreEqual("OldName", legacy.Id);
        }

        [Test]
        public void BareIdsWork()
        {
            Assert.AreEqual(YouTubeUrlParser.TargetPlaylist,
                            YouTubeUrlParser.Parse("PLabc").Kind);
            Assert.AreEqual(YouTubeUrlParser.TargetChannelId,
                            YouTubeUrlParser.Parse("UC1234567890abcdefghij").Kind);
            Assert.AreEqual(YouTubeUrlParser.TargetChannelName,
                            YouTubeUrlParser.Parse("@someone").Kind);
            Assert.AreEqual(YouTubeUrlParser.TargetVideo,
                            YouTubeUrlParser.Parse("dQw4w9WgXcQ").Kind);
        }

        [Test]
        public void NonsenseIsRejectedInsteadOfGuessing()
        {
            Assert.IsFalse(YouTubeUrlParser.Parse("").IsValid);
            Assert.IsFalse(YouTubeUrlParser.Parse("   ").IsValid);
            Assert.IsFalse(YouTubeUrlParser.Parse(null).IsValid);
            Assert.IsFalse(YouTubeUrlParser.Parse("https://example.com/watch").IsValid);
        }

        [Test]
        public void TrailingSlashesAndSchemesDoNotMatter()
        {
            Assert.AreEqual("someone", YouTubeUrlParser.Parse("youtube.com/@someone/").Id);
            Assert.AreEqual("someone", YouTubeUrlParser.Parse("http://youtube.com/@someone").Id);
        }

        // ───────── 再生時間 ─────────

        [Test]
        public void DurationsAreParsed()
        {
            Assert.AreEqual(253, YouTubeDurationParser.ToSeconds("PT4M13S"));
            Assert.AreEqual(3600, YouTubeDurationParser.ToSeconds("PT1H"));
            Assert.AreEqual(3723, YouTubeDurationParser.ToSeconds("PT1H2M3S"));
            Assert.AreEqual(45, YouTubeDurationParser.ToSeconds("PT45S"));
        }

        [Test]
        public void LongStreamsSpanningDaysAreParsed()
        {
            Assert.AreEqual(93784, YouTubeDurationParser.ToSeconds("P1DT2H3M4S"));
        }

        [Test]
        public void AnUnknownDurationBecomesZero()
        {
            // 生放送は長さを返さない。0 は「不明」として扱う。
            Assert.AreEqual(0, YouTubeDurationParser.ToSeconds("P0D"));
            Assert.AreEqual(0, YouTubeDurationParser.ToSeconds(""));
            Assert.AreEqual(0, YouTubeDurationParser.ToSeconds(null));
            Assert.AreEqual(0, YouTubeDurationParser.ToSeconds("4M13S"), "P で始まらない");
            Assert.AreEqual(0, YouTubeDurationParser.ToSeconds("PTM"), "数字が無い");
        }

        // ───────── 取れたものの形 ─────────

        [Test]
        public void AVideoBecomesADraftItem()
        {
            var video = new YouTubeVideoInfo();
            video.VideoId = "abc12345678";
            video.Title = "テスト曲";
            video.ChannelTitle = "テストチャンネル";
            video.DurationSeconds = 253;
            video.ThumbnailUrl = "https://img.example/1.jpg";

            CatalogDraftItem item = video.ToDraftItem();

            Assert.AreEqual("abc12345678", item.Id, "動画 ID がそのまま ID になる(一意なので重複しない)");
            Assert.AreEqual("テスト曲", item.Title);
            Assert.AreEqual("テストチャンネル", item.Artist);
            Assert.AreEqual("https://www.youtube.com/watch?v=abc12345678", item.Url);
            Assert.AreEqual(253, item.DurationSeconds);
            Assert.AreEqual("YouTube", item.Source);
            Assert.AreEqual("https://img.example/1.jpg", item.ThumbnailPath);
            Assert.IsTrue(item.IsComplete);
        }

        [Test]
        public void DurationsAreFormattedForReading()
        {
            var video = new YouTubeVideoInfo();

            video.DurationSeconds = 253;
            Assert.AreEqual("4:13", video.FormatDuration());

            video.DurationSeconds = 3723;
            Assert.AreEqual("1:02:03", video.FormatDuration());

            video.DurationSeconds = 0;
            Assert.AreEqual("--:--", video.FormatDuration(), "生放送は長さが無い");
        }

        [Test]
        public void ThePublishedDateIsShortened()
        {
            var video = new YouTubeVideoInfo();
            video.PublishedAt = "2024-05-01T12:34:56Z";

            Assert.AreEqual("2024-05-01", video.FormatPublishedDate());
        }

        // ───────── Importer(偽物の取得係で)─────────

        [Test]
        public void ImportingAPlaylistReturnsItems()
        {
            var client = new FakeClient();
            client.Videos.Add(Video("aaa", "1 曲目"));
            client.Videos.Add(Video("bbb", "2 曲目"));

            var importer = new YouTubeCatalogImporter(client);
            CatalogImportResult result = importer.Import("https://youtube.com/playlist?list=PLx");

            Assert.IsTrue(result.Ok);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("playlist:PLx", client.LastCall);
        }

        [Test]
        public void ImportingAChannelByHandleAsksForTheHandle()
        {
            var client = new FakeClient();
            client.Videos.Add(Video("aaa", "1 曲目"));

            var importer = new YouTubeCatalogImporter(client);
            importer.Import("https://youtube.com/@someone");

            Assert.AreEqual("channel-name:someone", client.LastCall);
        }

        [Test]
        public void ImportingAChannelByIdAsksForTheId()
        {
            var client = new FakeClient();
            client.Videos.Add(Video("aaa", "1 曲目"));

            var importer = new YouTubeCatalogImporter(client);
            importer.Import("https://youtube.com/channel/UC1234567890abcdefghij");

            Assert.AreEqual("channel-id:UC1234567890abcdefghij", client.LastCall);
        }

        [Test]
        public void UnavailableVideosAreKeptForThePreviewButNotHandedOver()
        {
            var client = new FakeClient();
            client.Videos.Add(Video("aaa", "使える"));

            var broken = Video("bbb", "(取得できません)");
            broken.IsUnavailable = true;
            broken.UnavailableReason = "非公開";
            client.Videos.Add(broken);

            var importer = new YouTubeCatalogImporter(client);
            CatalogImportResult result = importer.Import("PLx");

            Assert.AreEqual(1, result.Count, "使えないものは Builder へ渡さない");
            Assert.AreEqual(2, importer.LastVideos.Count, "プレビューには残る(消すと気づけない)");
        }

        [Test]
        public void AFailureComesBackAsAResultNotAnException()
        {
            var client = new FakeClient();
            client.FailWith = "割り当てを使い切りました";

            var importer = new YouTubeCatalogImporter(client);
            CatalogImportResult result = importer.Import("PLx");

            Assert.IsFalse(result.Ok);
            Assert.AreEqual("割り当てを使い切りました", result.Message);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void AnUnreadableUrlFailsBeforeTouchingTheNetwork()
        {
            var client = new FakeClient();
            var importer = new YouTubeCatalogImporter(client);

            CatalogImportResult result = importer.Import("https://example.com/nope");

            Assert.IsFalse(result.Ok);
            Assert.AreEqual("", client.LastCall, "読めない入力で通信しない");
        }

        [Test]
        public void AnUnavailableClientIsReportedBeforeFetching()
        {
            var client = new FakeClient();
            client.Available = false;
            client.Reason = "API キーが空です";

            var importer = new YouTubeCatalogImporter(client);

            Assert.IsFalse(importer.IsAvailable);
            Assert.AreEqual("API キーが空です", importer.UnavailableReason);

            CatalogImportResult result = importer.Import("PLx");
            Assert.IsFalse(result.Ok);
            Assert.AreEqual("", client.LastCall);
        }

        [Test]
        public void CanImportMatchesWhatTheParserAccepts()
        {
            var importer = new YouTubeCatalogImporter(new FakeClient());

            Assert.IsTrue(importer.CanImport("https://youtube.com/playlist?list=PLx"));
            Assert.IsFalse(importer.CanImport("https://example.com"));
            Assert.IsFalse(importer.CanImport(""));
        }

        [Test]
        public void ImportedItemsGoStraightIntoADraft()
        {
            // Phase6-3 の下ごしらえ。Builder 側の混ぜ方がそのまま使えることの確認。
            var client = new FakeClient();
            client.Videos.Add(Video("aaa", "1 曲目"));
            client.Videos.Add(Video("bbb", "2 曲目"));

            var importer = new YouTubeCatalogImporter(client);
            CatalogImportResult result = importer.Import("PLx");

            var draft = new CatalogDraft();
            int changed = draft.Merge(result.Items, CatalogDraft.MergeAppend);

            Assert.AreEqual(2, changed);
            Assert.AreEqual(2, draft.CompleteCount);
            Assert.AreEqual(0, draft.FindDuplicateIds().Length);
        }

        // ───────── 取得の係は差し替えられる ─────────

        [Test]
        public void TheImporterNeverTalksToTheNetworkItself()
        {
            var type = typeof(YouTubeCatalogImporter);

            Assert.IsNull(type.GetMethod("Get"));
            Assert.IsNull(type.GetMethod("Fetch",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance),
                "通信は IYouTubeClient の仕事(差し替えられるようにするため)");
        }

        // ───────── 道具 ─────────

        private static YouTubeVideoInfo Video(string id, string title)
        {
            var video = new YouTubeVideoInfo();
            video.VideoId = id;
            video.Title = title;
            video.ChannelTitle = "チャンネル";
            video.PublishedAt = "2024-05-01T00:00:00Z";
            video.DurationSeconds = 200;
            video.ThumbnailUrl = "https://img.example/" + id + ".jpg";
            return video;
        }

        /// <summary>通信しない取得係。何を頼まれたかだけ覚える。</summary>
        private sealed class FakeClient : IYouTubeClient
        {
            public readonly List<YouTubeVideoInfo> Videos = new List<YouTubeVideoInfo>();

            public string LastCall = "";
            public string FailWith = "";
            public bool Available = true;
            public string Reason = "";

            public bool IsAvailable { get { return Available; } }

            public string UnavailableReason { get { return Reason; } }

            public YouTubeFetchResult FetchPlaylist(string playlistId, int maxCount)
            {
                LastCall = "playlist:" + playlistId;
                return Answer();
            }

            public YouTubeFetchResult FetchChannel(string channel, bool byName, int maxCount)
            {
                LastCall = (byName ? "channel-name:" : "channel-id:") + channel;
                return Answer();
            }

            public YouTubeFetchResult FetchVideo(string videoId)
            {
                LastCall = "video:" + videoId;
                return Answer();
            }

            private YouTubeFetchResult Answer()
            {
                if (FailWith.Length > 0) return YouTubeFetchResult.Failure(FailWith);
                return YouTubeFetchResult.Success(Videos, Videos.Count + " 件を取得しました。");
            }
        }
    }
}
