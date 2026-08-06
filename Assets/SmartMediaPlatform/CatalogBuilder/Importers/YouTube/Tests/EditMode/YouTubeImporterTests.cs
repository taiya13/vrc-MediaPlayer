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

        // ───────── 関連と絞り込みの材料(Phase6-4)─────────

        [Test]
        public void TheGenreIsDecidedHereNotByTheCategory()
        {
            // Phase6-6: カテゴリ「音楽」はほぼ全部の曲に付くので、
            // そのまま入れると 200 本すべてが「音楽」になる。
            var video = new YouTubeVideoInfo();
            video.VideoId = "abc12345678";
            video.Title = "初音ミク - メルト";
            video.CategoryId = "10";

            Assert.AreEqual(Genres.GenreDictionary.Vocaloid, video.ToDraftItem().Genre,
                            "見出しから決める");
        }

        [Test]
        public void AnUnrecognisedVideoFallsBackToOther()
        {
            var video = new YouTubeVideoInfo();
            video.VideoId = "abc12345678";
            video.Title = "テスト曲";
            video.CategoryId = "9999";

            Assert.AreEqual(Genres.GenreClassifier.Unknown, video.ToDraftItem().Genre,
                            "「音楽」ではなく「その他」");
        }

        [Test]
        public void TheCategoryIsPassedAsAWordNotANumber()
        {
            var video = new YouTubeVideoInfo();
            video.CategoryId = "20";

            Assert.AreEqual("ゲーム", video.ToGenreSignals().SourceCategory,
                            "判定側に YouTube の番号の意味を持ち込まない");
        }

        [Test]
        public void TagsAreCarriedOverButCapped()
        {
            var video = new YouTubeVideoInfo();
            video.VideoId = "abc12345678";
            video.Title = "テスト曲";

            var many = new string[30];
            for (int i = 0; i < many.Length; i++) many[i] = "タグ" + i;
            video.Tags = many;

            CatalogDraftItem item = video.ToDraftItem();

            Assert.AreEqual(YouTubeVideoInfo.MaxTags, item.Tags.Length,
                            "30 個そのまま入れると編集画面がタグで埋まる");
            Assert.AreEqual("タグ0", item.Tags[0]);
        }

        [Test]
        public void VideosWithoutTagsStillConvert()
        {
            var video = new YouTubeVideoInfo();
            video.VideoId = "abc12345678";
            video.Title = "テスト曲";

            CatalogDraftItem item = video.ToDraftItem();

            Assert.AreEqual(0, item.Tags.Length, "タグが付いていない動画も多い");
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

        // ───────── 新着だけ取る(Phase6-5)─────────

        [Test]
        public void OnlyUnknownVideosComeBack()
        {
            var client = new FakeClient();
            client.Videos.Add(Video("new1", "新しい 1"));
            client.Videos.Add(Video("old1", "もうある 1"));
            client.Videos.Add(Video("old2", "もうある 2"));

            var importer = new YouTubeCatalogImporter(client);

            var boundary = new CatalogImportBoundary();
            boundary.KnownIds = new[] { "old1", "old2" };

            CatalogImportResult result = importer.ImportNew(
                "https://www.youtube.com/@channel", boundary);

            Assert.IsTrue(result.Ok);
            Assert.AreEqual(1, result.Count, "知らないものだけ返る");
            Assert.AreEqual("new1", result.Items[0].Id);
            Assert.IsFalse(result.MayHaveMore, "知っているものに当たったので、そこまで");
        }

        [Test]
        public void NothingNewIsSaidPlainly()
        {
            var client = new FakeClient();
            client.Videos.Add(Video("old1", "もうある"));

            var importer = new YouTubeCatalogImporter(client);

            var boundary = new CatalogImportBoundary();
            boundary.StopAtId = "old1";

            CatalogImportResult result = importer.ImportNew("@channel", boundary);

            Assert.IsTrue(result.Ok, "新着 0 件は失敗ではない");
            Assert.AreEqual(0, result.Count);
            StringAssert.Contains("新着はありません", result.Message);
        }

        [Test]
        public void AFullPageOfNewItemsWarnsThatMoreMayExist()
        {
            var client = new FakeClient();
            for (int i = 0; i < 5; i++) client.Videos.Add(Video("v" + i, "曲" + i));

            var importer = new YouTubeCatalogImporter(client);

            var boundary = new CatalogImportBoundary();
            boundary.MaxNewItems = 5;

            CatalogImportResult result = importer.ImportNew("@channel", boundary);

            Assert.AreEqual(5, result.Count);
            Assert.IsTrue(result.MayHaveMore,
                          "取りこぼしを黙っていると「入れたはずの曲が無い」になる");
            StringAssert.Contains("まだ先にある", result.Message);
        }

        [Test]
        public void TheSourceNameAndKindComeBackForTheSubscription()
        {
            var client = new FakeClient();
            client.Videos.Add(Video("v1", "曲"));

            var importer = new YouTubeCatalogImporter(client);

            CatalogImportResult channel = importer.ImportNew(
                "https://www.youtube.com/@channel", new CatalogImportBoundary());

            Assert.AreEqual(CatalogSubscription.KindChannel, channel.SourceKind);
            Assert.AreEqual("チャンネル", channel.SourceName, "動画に付いてくるチャンネル名を使う");

            CatalogImportResult playlist = importer.ImportNew(
                "https://www.youtube.com/playlist?list=PLabc", new CatalogImportBoundary());

            Assert.AreEqual(CatalogSubscription.KindPlaylist, playlist.SourceKind);
            StringAssert.Contains("PLabc", playlist.SourceName);
        }

        [Test]
        public void PlaylistsGoThroughTheSamePath()
        {
            var client = new FakeClient();
            client.Videos.Add(Video("v1", "曲"));

            var importer = new YouTubeCatalogImporter(client);
            importer.ImportNew("https://www.youtube.com/playlist?list=PLabc",
                               new CatalogImportBoundary());

            Assert.AreEqual("playlist:PLabc", client.LastCall,
                            "チャンネルもプレイリストも同じ入口");
        }

        [Test]
        public void ImportingEverythingStillIgnoresTheBoundary()
        {
            var client = new FakeClient();
            client.Videos.Add(Video("old1", "もうある"));
            client.Videos.Add(Video("new1", "新しい"));

            var importer = new YouTubeCatalogImporter(client);

            Assert.AreEqual(2, importer.Import("@channel").Count,
                            "「全部取得」は今までどおり全部返す");
        }

        [Test]
        public void TheImporterOffersTheIncrementalDoor()
        {
            Assert.IsTrue(new YouTubeCatalogImporter() is IIncrementalCatalogImporter,
                          "Builder はこの口があるかどうかでボタンを出し分ける");
        }

        // ───────── ショート動画を外す(Phase7-3)─────────

        [Test]
        public void ShortVideosAreLeftOutWhenAWholeChannelIsImported()
        {
            var client = new FakeClient();
            client.Videos.Add(Video("full", "ふつうの曲"));
            client.Videos.Add(Short("s1", "きりぬき", 45));

            var importer = new YouTubeCatalogImporter(client);
            CatalogImportResult result = importer.Import("@channel");

            Assert.AreEqual(1, result.Count, "ショートは入らない");
            Assert.AreEqual("full", result.Items[0].Id);
            Assert.AreEqual(1, importer.LastShortsExcluded);
        }

        [Test]
        public void LeavingOutShortsIsSaidOutLoud()
        {
            var client = new FakeClient();
            client.Videos.Add(Video("full", "ふつうの曲"));
            client.Videos.Add(Short("s1", "きりぬき", 30));

            var importer = new YouTubeCatalogImporter(client);

            Assert.IsTrue(importer.Import("@channel").Message.Contains("ショート"),
                          "黙って消さない。何件外したか必ず書く");
        }

        [Test]
        public void AShortPastedOnItsOwnIsStillImported()
        {
            var client = new FakeClient();
            client.Videos.Add(Short("s1", "きりぬき", 30));

            var importer = new YouTubeCatalogImporter(client);

            Assert.AreEqual(1, importer.Import("https://www.youtube.com/shorts/s1").Count,
                            "人が名指しで貼ったものを黙って消さない");
        }

        [Test]
        public void ShortsCanBeTurnedBackOn()
        {
            var client = new FakeClient();
            client.Videos.Add(Video("full", "ふつうの曲"));
            client.Videos.Add(Short("s1", "きりぬき", 30));

            var importer = new YouTubeCatalogImporter(client);
            importer.ExcludeShorts = false;

            Assert.AreEqual(2, importer.Import("@channel").Count);
        }

        [Test]
        public void AShortIsSpottedByItsLength()
        {
            Assert.IsTrue(YouTubeShortsFilter.IsShort(Short("a", "きりぬき", 59)));
            Assert.IsTrue(YouTubeShortsFilter.IsShort(Short("b", "きりぬき", 60)));
            Assert.IsFalse(YouTubeShortsFilter.IsShort(Short("c", "ふつう", 61)));
        }

        [Test]
        public void AVideoOfUnknownLengthIsNeverTreatedAsAShort()
        {
            // 長さが取れないことは実際にある。分からないものを消すと
            // 「入れたはずの曲が無い」という一番困る壊れ方になる。
            var unknown = Video("x", "長さ不明");
            unknown.DurationSeconds = 0;

            Assert.IsFalse(YouTubeShortsFilter.IsShort(unknown));
        }

        [Test]
        public void ALongVideoMarkedAsShortsIsStillAShort()
        {
            // ショートの上限は 3 分まで延びた。長さだけでは足りない。
            var marked = Video("y", "ダンス #Shorts");
            marked.DurationSeconds = 170;

            Assert.IsTrue(YouTubeShortsFilter.IsShort(marked), "見出しの印で拾う");

            var tagged = Video("z", "ダンス");
            tagged.DurationSeconds = 170;
            tagged.Tags = new[] { "dance", "shorts" };

            Assert.IsTrue(YouTubeShortsFilter.IsShort(tagged), "タグの印でも拾う");
        }

        [Test]
        public void TheReasonForLeavingSomethingOutCanBeRead()
        {
            Assert.IsTrue(YouTubeShortsFilter.ReasonFor(Short("a", "きりぬき", 30)).Length > 0);
            Assert.AreEqual("", YouTubeShortsFilter.ReasonFor(Video("b", "ふつうの曲")),
                            "ショートでなければ理由は無い");
        }

        // ───────── 目標の件数までそろえる(Phase7-3)─────────

        [Test]
        public void PagesAreFetchedUntilEnoughNormalVideosAreFound()
        {
            // 1 ページ 10 件。うち 8 件がショート、2 件が通常動画。
            // 通常動画を 10 件そろえるには 5 ページ見る必要がある。
            var client = new FakeClient();
            client.Page = 10;

            for (int i = 0; i < 100; i++)
            {
                client.Videos.Add(i % 5 < 4
                    ? Short("s" + i, "きりぬき", 30)
                    : Video("v" + i, "ふつうの曲"));
            }

            var importer = new YouTubeCatalogImporter(client);
            importer.MaxCount = 10;

            CatalogImportResult result = importer.Import("@channel");

            Assert.AreEqual(10, result.Count, "「10 件見る」ではなく「通常動画を 10 件そろえる」");
            Assert.AreEqual(5, client.PageRequests, "足りないぶんだけ次のページを見る");
        }

        [Test]
        public void FetchingStopsAsSoonAsThereAreEnough()
        {
            var client = new FakeClient();
            client.Page = 10;
            for (int i = 0; i < 100; i++) client.Videos.Add(Video("v" + i, "ふつうの曲"));

            var importer = new YouTubeCatalogImporter(client);
            importer.MaxCount = 10;

            Assert.AreEqual(10, importer.Import("@channel").Count);
            Assert.AreEqual(1, client.PageRequests, "そろっているのに次を見に行かない");
        }

        [Test]
        public void FetchingStopsAtTheEndEvenIfNotEnough()
        {
            // 通常動画が 3 件しか無いのに 50 件頼まれた。最後まで見て終わる。
            var client = new FakeClient();
            client.Page = 10;
            for (int i = 0; i < 25; i++) client.Videos.Add(Short("s" + i, "きりぬき", 30));
            for (int i = 0; i < 3; i++) client.Videos.Add(Video("v" + i, "ふつうの曲"));

            var importer = new YouTubeCatalogImporter(client);
            importer.MaxCount = 50;

            Assert.AreEqual(3, importer.Import("@channel").Count);
            Assert.AreEqual(3, client.PageRequests, "28 件しか無いので 3 ページで終わり");
        }

        [Test]
        public void FetchingNeverRunsAwayWhenEverythingIsAShort()
        {
            // ショートしか無いチャンネル。歯止めが無いと API を叩き続ける。
            var client = new FakeClient();
            client.Page = 10;
            for (int i = 0; i < 5000; i++) client.Videos.Add(Short("s" + i, "きりぬき", 30));

            var importer = new YouTubeCatalogImporter(client);
            importer.MaxCount = 100;

            Assert.AreEqual(0, importer.Import("@channel").Count);
            Assert.AreEqual(YouTubeCatalogImporter.MaxPages, client.PageRequests,
                            "決めたページ数で必ず止まる");
        }

        [Test]
        public void TheTallyOfWhatWasSeenAndDroppedIsReported()
        {
            var client = new FakeClient();
            client.Page = 10;
            for (int i = 0; i < 6; i++) client.Videos.Add(Short("s" + i, "きりぬき", 30));
            for (int i = 0; i < 4; i++) client.Videos.Add(Video("v" + i, "ふつうの曲"));

            var importer = new YouTubeCatalogImporter(client);
            importer.MaxCount = 50;

            string message = importer.Import("@channel").Message;

            Assert.AreEqual(10, importer.LastFetchedTotal, "見た総数");
            Assert.AreEqual(6, importer.LastShortsExcluded, "外したショート");
            Assert.AreEqual(4, importer.LastImported, "実際に入った件数");

            Assert.IsTrue(message.Contains("10"), "見た総数が出る: " + message);
            Assert.IsTrue(message.Contains("6"), "外したショートが出る: " + message);
            Assert.IsTrue(message.Contains("4"), "入った件数が出る: " + message);
        }

        [Test]
        public void MoreThanOnePageWorthCanBeImported()
        {
            // 「1 ページ分で終わり」になっていないことの確認。
            var client = new FakeClient();
            client.Page = 50;
            for (int i = 0; i < 200; i++) client.Videos.Add(Video("v" + i, "ふつうの曲"));

            var importer = new YouTubeCatalogImporter(client);
            importer.MaxCount = 120;

            Assert.AreEqual(120, importer.Import("@channel").Count);
        }

        // ───────── 見出しを整える(Phase7-3)─────────

        [Test]
        public void TheChannelNameIsTakenOffTheFrontOfTheTitle()
        {
            Assert.AreEqual(
                "C.U.R.I.O.S.I.T.Y.",
                YouTubeTitleCleaner.Clean("ONE OK ROCK - C.U.R.I.O.S.I.T.Y.", "ONE OK ROCK"));

            Assert.AreEqual(
                "Tiny Pieces",
                YouTubeTitleCleaner.Clean("ONE OK ROCK：Tiny Pieces", "ONE OK ROCK"));
        }

        [Test]
        public void ADecorationAtTheEndIsTakenOff()
        {
            Assert.AreEqual(
                "All Mine",
                YouTubeTitleCleaner.Clean(
                    "ONE OK ROCK - All Mine [Official Music Video]", "ONE OK ROCK"));

            Assert.AreEqual("Wasted Nights", YouTubeTitleCleaner.Clean("Wasted Nights(MV)", ""));
        }

        [Test]
        public void SomethingThatTellsSongsApartIsKept()
        {
            // これを消すと、原曲と区別が付かなくなる。
            Assert.AreEqual(
                "Stand Out Fit In (Acoustic Version)",
                YouTubeTitleCleaner.Clean(
                    "ONE OK ROCK - Stand Out Fit In (Acoustic Version)", "ONE OK ROCK"));

            Assert.AreEqual(
                "Renegades (feat. Someone)",
                YouTubeTitleCleaner.Clean("Renegades (feat. Someone) [Official Video]", ""));
        }

        [Test]
        public void ATitleIsNeverEmptiedOut()
        {
            // 見出しがチャンネル名そのものだった場合。消したら何も残らない。
            Assert.AreEqual("ONE OK ROCK", YouTubeTitleCleaner.Clean("ONE OK ROCK", "ONE OK ROCK"));
            Assert.AreEqual("[Official]", YouTubeTitleCleaner.Clean("[Official]", ""));
        }

        [Test]
        public void ANameThatOnlyLooksLikeTheChannelIsLeftAlone()
        {
            // 頭が似ているだけで、区切りが無いものは触らない。
            Assert.AreEqual(
                "ONE OK ROCK Documentary",
                YouTubeTitleCleaner.Clean("ONE OK ROCK Documentary", "ONE OK ROCK"));
        }

        [Test]
        public void ImportedItemsCarryTheCleanedTitle()
        {
            var client = new FakeClient();

            var video = Video("a", "ONE OK ROCK - Wasted Nights [Official Music Video]");
            video.ChannelTitle = "ONE OK ROCK";
            client.Videos.Add(video);

            var importer = new YouTubeCatalogImporter(client);
            CatalogImportResult result = importer.Import("@channel");

            Assert.AreEqual("Wasted Nights", result.Items[0].Title);
            Assert.AreEqual("ONE OK ROCK", result.Items[0].Artist);
        }

        [Test]
        public void EverythingFromOneChannelGetsTheSameArtist()
        {
            // 一覧はこの値でチャンネルごとにまとめる。1 件でも違うと
            // 「1 チャンネルなのにグループが山ほどできる」になる。
            var client = new FakeClient();

            string[] titles =
            {
                "ONE OK ROCK - Wasted Nights [Official Music Video]",
                "Live at Nagisaen 2019",
                "Renegades - Japanese Version",
                "【MV】Stand Out Fit In",
            };

            for (int i = 0; i < titles.Length; i++)
            {
                var v = Video("v" + i, titles[i]);
                v.ChannelTitle = "ONE OK ROCK";
                client.Videos.Add(v);
            }

            var importer = new YouTubeCatalogImporter(client);
            CatalogImportResult result = importer.Import("@channel");

            Assert.AreEqual(titles.Length, result.Count);
            for (int i = 0; i < result.Count; i++)
            {
                Assert.AreEqual("ONE OK ROCK", result.Items[i].Artist,
                                "見出しから推測せず、投稿チャンネル名をそのまま使う");
            }
        }

        // ───────── 道具 ─────────

        private static YouTubeVideoInfo Short(string id, string title, int seconds)
        {
            var video = Video(id, title);
            video.DurationSeconds = seconds;
            return video;
        }

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

        /// <summary>
        /// 通信しない取得係。何を頼まれたかだけ覚える。
        ///
        /// <b>1 ページずつも返せます</b>(Phase7-3)。<see cref="PageSize"/> ごとに区切り、
        /// 続きの位置は「次の開始番号」を文字列にしたものです。
        /// </summary>
        private sealed class FakeClient : IYouTubeClient, IPagedYouTubeClient
        {
            public readonly List<YouTubeVideoInfo> Videos = new List<YouTubeVideoInfo>();

            public string LastCall = "";
            public string FailWith = "";
            public bool Available = true;
            public string Reason = "";

            /// <summary>1 ページの件数。</summary>
            public int Page = 50;

            /// <summary>何ページ頼まれたか(取りすぎていないかを見る)。</summary>
            public int PageRequests;

            public bool IsAvailable { get { return Available; } }

            public string UnavailableReason { get { return Reason; } }

            public int PageSize { get { return Page; } }

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

            public YouTubeFetchResult FetchPlaylistPage(
                string playlistId, int want, string pageToken)
            {
                LastCall = "playlist:" + playlistId;
                return AnswerPage(pageToken);
            }

            public YouTubeFetchResult FetchChannelPage(
                string channel, bool byName, int want, string pageToken)
            {
                LastCall = (byName ? "channel-name:" : "channel-id:") + channel;
                return AnswerPage(pageToken);
            }

            private YouTubeFetchResult Answer()
            {
                if (FailWith.Length > 0) return YouTubeFetchResult.Failure(FailWith);
                return YouTubeFetchResult.Success(Videos, Videos.Count + " 件を取得しました。");
            }

            private YouTubeFetchResult AnswerPage(string pageToken)
            {
                if (FailWith.Length > 0) return YouTubeFetchResult.Failure(FailWith);

                PageRequests++;

                int start = 0;
                if (!string.IsNullOrEmpty(pageToken)) int.TryParse(pageToken, out start);

                var slice = new List<YouTubeVideoInfo>();
                for (int i = start; i < Videos.Count && slice.Count < Page; i++)
                {
                    slice.Add(Videos[i]);
                }

                int next = start + slice.Count;
                string token = next < Videos.Count ? next.ToString() : "";

                return YouTubeFetchResult
                    .Success(slice, slice.Count + " 件を取得しました。")
                    .WithNextPage(token);
            }
        }
    }
}
