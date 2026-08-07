#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace SmartMediaPlatform.CatalogBuilder.YouTube
{
    /// <summary>
    /// <b>YouTube Data API v3 から取ってくる。</b>Phase6-2。
    ///
    /// <b>取得の仕方はここだけに閉じ込めてあります。</b>
    /// API が変わっても、キー無しの方法に替えても、
    /// 直すのはこのクラスだけで <see cref="YouTubeCatalogImporter"/> は無変更です。
    ///
    /// <b>同期(待つ)処理です。</b><c>ICatalogImporter.Import</c> が同期なので、
    /// エディタを止めて待ちます。エディタツールでは普通のやり方ですが、
    /// 件数が多いと数秒止まるので <see cref="YouTubeApiSettings.MaxItems"/> で歯止めをかけています。
    ///
    /// <b>例外を投げません。</b>失敗は <see cref="YouTubeFetchResult"/> に入れて返します。
    /// </summary>
    public sealed class YouTubeDataApiClient
        : IYouTubeClient, IPagedYouTubeClient, ISearchingYouTubeClient
    {
        private const string Api = "https://www.googleapis.com/youtube/v3/";

        private readonly YouTubeApiSettings _injectedSettings;

        public YouTubeDataApiClient() : this(null)
        {
        }

        /// <summary>設定を差し替える(テストで偽物を入れるため)。渡さなければ毎回ディスクから読み直す。</summary>
        public YouTubeDataApiClient(YouTubeApiSettings settings)
        {
            _injectedSettings = settings;
        }

        /// <summary>
        /// <b>毎回読み直します。</b>キャッシュすると、窓を開いたままアセットを
        /// 後から作ったり選び直したりしたときに気づけないままになるためです。
        /// </summary>
        private YouTubeApiSettings _settings
        {
            get { return _injectedSettings != null ? _injectedSettings : YouTubeApiSettings.LoadIfPresent(); }
        }

        public bool IsAvailable
        {
            get { return _settings != null && _settings.HasKey; }
        }

        public string UnavailableReason
        {
            get
            {
                // Phase7: 「窓の『設定を作る』」と書いていたが、その窓は Phase6-5 で
                // 消えている。いま実際に押せる場所だけを書く。
                if (_settings == null || !_settings.HasKey)
                {
                    return "YouTube の API キーがまだ入っていません。\n"
                           + "下の「YouTube の API キー」欄に貼ると使えるようになります。";
                }
                return "";
            }
        }

        private int MaxItems { get { return _settings != null ? _settings.MaxItems : 50; } }

        private int Timeout { get { return _settings != null ? _settings.TimeoutSeconds : 20; } }

        private string Key { get { return _settings != null ? _settings.ApiKey : ""; } }

        // ───────── 入口 ─────────

        public YouTubeFetchResult FetchPlaylist(string playlistId, int maxCount)
        {
            if (!IsAvailable) return YouTubeFetchResult.Failure(UnavailableReason);
            if (string.IsNullOrWhiteSpace(playlistId))
                return YouTubeFetchResult.Failure("再生リスト ID が空です。");

            return FetchPlaylistItems(playlistId, Clamp(maxCount));
        }

        public YouTubeFetchResult FetchChannel(string channel, bool byName, int maxCount)
        {
            if (!IsAvailable) return YouTubeFetchResult.Failure(UnavailableReason);

            // チャンネルの投稿は「アップロード用の再生リスト」として取れる。
            // 専用の口が無いので、まず ID を引いてから再生リストとして読む。
            string uploads;
            string error;
            if (!ResolveUploadsPlaylist(channel, byName, out uploads, out error))
            {
                return YouTubeFetchResult.Failure(error);
            }

            return FetchPlaylistItems(uploads, Clamp(maxCount));
        }

        public YouTubeFetchResult FetchVideo(string videoId)
        {
            if (!IsAvailable) return YouTubeFetchResult.Failure(UnavailableReason);
            if (string.IsNullOrWhiteSpace(videoId))
                return YouTubeFetchResult.Failure("動画 ID が空です。");

            var ids = new List<string>();
            ids.Add(videoId);

            var map = new Dictionary<string, YouTubeVideoInfo>();
            string error;
            if (!FillVideoDetails(ids, map, out error)) return YouTubeFetchResult.Failure(error);

            if (!map.ContainsKey(videoId))
            {
                return YouTubeFetchResult.Failure("動画が見つかりません(非公開か削除済み): " + videoId);
            }

            var list = new List<YouTubeVideoInfo>();
            list.Add(map[videoId]);
            return YouTubeFetchResult.Success(list, "1 件を取得しました。");
        }

        // ───────── 1 ページずつ取る(Phase7-3)─────────

        /// <summary>API が 1 回で返せる上限。</summary>
        public int PageSize { get { return 50; } }

        public YouTubeFetchResult FetchPlaylistPage(string playlistId, int want, string pageToken)
        {
            if (!IsAvailable) return YouTubeFetchResult.Failure(UnavailableReason);
            if (string.IsNullOrWhiteSpace(playlistId))
                return YouTubeFetchResult.Failure("再生リスト ID が空です。");

            return FetchPlaylistItems(playlistId, Clamp(want), pageToken);
        }

        public YouTubeFetchResult FetchChannelPage(
            string channel, bool byName, int want, string pageToken)
        {
            if (!IsAvailable) return YouTubeFetchResult.Failure(UnavailableReason);

            string uploads;
            string error;
            if (!ResolveUploadsPlaylist(channel, byName, out uploads, out error))
            {
                return YouTubeFetchResult.Failure(error);
            }

            return FetchPlaylistItems(uploads, Clamp(want), pageToken);
        }

        /// <summary>
        /// チャンネルの「アップロード用の再生リスト」を引く。
        /// <b>1 ページずつ取るときは毎回ここを通るので、覚えておきます</b> —
        /// 同じチャンネルを何度も引くと、そのぶん API の割り当てを食うためです。
        /// </summary>
        private bool ResolveUploadsPlaylist(
            string channel, bool byName, out string uploads, out string error)
        {
            uploads = "";
            error = "";

            if (string.IsNullOrWhiteSpace(channel))
            {
                error = "チャンネルの指定が空です。";
                return false;
            }

            string cacheKey = (byName ? "@" : "") + channel;
            if (_uploadsCache.ContainsKey(cacheKey))
            {
                uploads = _uploadsCache[cacheKey];
                return true;
            }

            string query = byName
                ? "channels?part=contentDetails&forHandle=@" + Escape(channel)
                : "channels?part=contentDetails&id=" + Escape(channel);

            string json;
            if (!Get(query, out json, out error)) return false;

            var channels = Parse<ChannelListResponse>(json);
            if (channels == null || channels.items == null || channels.items.Length == 0)
            {
                error = "チャンネルが見つかりません: " + channel + "\n"
                        + "@名前 が変わっている場合は channel/UC… の URL を試してください。";
                return false;
            }

            var details = channels.items[0].contentDetails;
            if (details == null || details.relatedPlaylists == null
                || string.IsNullOrEmpty(details.relatedPlaylists.uploads))
            {
                error = "このチャンネルの投稿一覧が取れませんでした。";
                return false;
            }

            uploads = details.relatedPlaylists.uploads;
            _uploadsCache[cacheKey] = uploads;
            return true;
        }

        private readonly Dictionary<string, string> _uploadsCache = new Dictionary<string, string>();

        // ───────── 並べ替えて探す(Phase7-4)─────────

        /// <summary>
        /// <c>search.list</c> で、チャンネルの動画を指定の順に並べて 1 ページ取る。
        ///
        /// <b>ここで取るのは動画 ID だけ</b>です。見出し・長さ・タグは
        /// <c>videos.list</c> で引く<b>いままでの道</b>に合流させます
        /// (<c>search.list</c> の snippet には長さが入っていないため、
        ///  どのみち引き直しが要ります)。
        ///
        /// <b>API の割り当てを 100 単位使います</b>(<c>playlistItems</c> は 1 単位)。
        /// 呼ぶ回数が増えないよう、1 回で取れるだけ取ります。
        /// </summary>
        public YouTubeFetchResult SearchChannelPage(
            string channel, bool byName, string order, int want, string pageToken)
        {
            if (!IsAvailable) return YouTubeFetchResult.Failure(UnavailableReason);

            string channelId;
            string error;
            if (!ResolveChannelId(channel, byName, out channelId, out error))
            {
                return YouTubeFetchResult.Failure(error);
            }

            int take = Math.Min(50, Clamp(want));
            if (take <= 0) take = 50;

            string query = "search?part=id&type=video&channelId=" + Escape(channelId)
                           + "&order=" + Escape(string.IsNullOrEmpty(order) ? "viewCount" : order)
                           + "&maxResults=" + take;

            if (!string.IsNullOrEmpty(pageToken)) query += "&pageToken=" + Escape(pageToken);

            string json;
            if (!Get(query, out json, out error)) return YouTubeFetchResult.Failure(error);

            var page = Parse<SearchListResponse>(json);
            if (page == null || page.items == null || page.items.Length == 0)
            {
                return YouTubeFetchResult
                    .Success(new List<YouTubeVideoInfo>(), "これ以上ありません。")
                    .WithNextPage("");
            }

            var order_ = new List<string>();
            for (int i = 0; i < page.items.Length; i++)
            {
                SearchItem item = page.items[i];
                if (item == null || item.id == null) continue;
                if (string.IsNullOrEmpty(item.id.videoId)) continue;
                if (order_.Contains(item.id.videoId)) continue;

                order_.Add(item.id.videoId);
            }

            string next = page.nextPageToken != null ? page.nextPageToken : "";

            if (order_.Count == 0)
            {
                return YouTubeFetchResult
                    .Success(new List<YouTubeVideoInfo>(), "この並びでは取れませんでした。")
                    .WithNextPage(next);
            }

            return BuildFromIds(order_, next);
        }

        /// <summary>
        /// チャンネル ID を引く。<c>@名前</c> でも <c>UC…</c> でも受けます。
        /// <b>覚えておきます</b> —— ページを繰り返すたびに引くと、そのぶん割り当てを食うためです。
        /// </summary>
        private bool ResolveChannelId(
            string channel, bool byName, out string channelId, out string error)
        {
            channelId = "";
            error = "";

            if (string.IsNullOrWhiteSpace(channel))
            {
                error = "チャンネルの指定が空です。";
                return false;
            }

            if (!byName)
            {
                channelId = channel;
                return true;
            }

            string cacheKey = "@" + channel;
            if (_channelIdCache.ContainsKey(cacheKey))
            {
                channelId = _channelIdCache[cacheKey];
                return true;
            }

            string json;
            if (!Get("channels?part=id&forHandle=@" + Escape(channel), out json, out error))
            {
                return false;
            }

            var channels = Parse<ChannelIdListResponse>(json);
            if (channels == null || channels.items == null || channels.items.Length == 0
                || string.IsNullOrEmpty(channels.items[0].id))
            {
                error = "チャンネルが見つかりません: @" + channel + "\n"
                        + "@名前 が変わっている場合は channel/UC… の URL を試してください。";
                return false;
            }

            channelId = channels.items[0].id;
            _channelIdCache[cacheKey] = channelId;
            return true;
        }

        private readonly Dictionary<string, string> _channelIdCache = new Dictionary<string, string>();

        /// <summary>
        /// 動画 ID の並びから、いままでと同じ形の結果を作る。
        /// <b>再生リストから読んだときと、この先はまったく同じ道</b>です。
        /// </summary>
        private YouTubeFetchResult BuildFromIds(List<string> ids, string nextPageToken)
        {
            var map = new Dictionary<string, YouTubeVideoInfo>();
            string error;
            if (!FillVideoDetails(ids, map, out error)) return YouTubeFetchResult.Failure(error);

            var videos = new List<YouTubeVideoInfo>();
            int unavailable = 0;

            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];

                if (map.ContainsKey(id))
                {
                    videos.Add(map[id]);
                    continue;
                }

                var missing = new YouTubeVideoInfo();
                missing.VideoId = id;
                missing.Title = "(取得できません)";
                missing.IsUnavailable = true;
                missing.UnavailableReason = "非公開・削除済み・地域制限のいずれか";
                videos.Add(missing);
                unavailable++;
            }

            string message = videos.Count + " 件を取得しました。";
            if (unavailable > 0) message += "(うち " + unavailable + " 件は再生できません)";

            return YouTubeFetchResult.Success(videos, message).WithNextPage(nextPageToken);
        }

        // ───────── 再生リストを読む ─────────

        private YouTubeFetchResult FetchPlaylistItems(string playlistId, int maxCount)
        {
            return FetchPlaylistItems(playlistId, maxCount, "");
        }

        /// <summary>
        /// 再生リストを読む。<paramref name="startToken"/> が空でなければ<b>その続きから</b>。
        /// 続きの位置は結果に入れて返します(<see cref="YouTubeFetchResult.NextPageToken"/>)。
        /// </summary>
        private YouTubeFetchResult FetchPlaylistItems(
            string playlistId, int maxCount, string startToken)
        {
            var order = new List<string>();
            var pending = new List<string>();
            string pageToken = startToken != null ? startToken : "";

            while (order.Count < maxCount)
            {
                int want = Math.Min(50, maxCount - order.Count);

                string query = "playlistItems?part=contentDetails&maxResults=" + want
                               + "&playlistId=" + Escape(playlistId);
                if (pageToken.Length > 0) query += "&pageToken=" + Escape(pageToken);

                string json;
                string error;
                if (!Get(query, out json, out error)) return YouTubeFetchResult.Failure(error);

                var page = Parse<PlaylistItemListResponse>(json);
                if (page == null || page.items == null || page.items.Length == 0)
                {
                    pageToken = "";
                    break;
                }

                for (int i = 0; i < page.items.Length; i++)
                {
                    var details = page.items[i].contentDetails;
                    if (details == null || string.IsNullOrEmpty(details.videoId)) continue;

                    if (order.Contains(details.videoId)) continue;
                    order.Add(details.videoId);
                    pending.Add(details.videoId);
                }

                pageToken = page.nextPageToken != null ? page.nextPageToken : "";
                if (pageToken.Length == 0) break;
            }

            if (order.Count == 0)
            {
                // 続きを読んでいる途中なら「空のページ」は失敗ではない。
                // 最後まで来ただけなので、0 件の成功として返す。
                if (!string.IsNullOrEmpty(startToken))
                {
                    return YouTubeFetchResult
                        .Success(new List<YouTubeVideoInfo>(), "これ以上ありません。")
                        .WithNextPage("");
                }

                return YouTubeFetchResult.Failure(
                    "動画が 1 件も取れませんでした。再生リストが空か、非公開の可能性があります。");
            }

            // 長さ・見出し・サムネイルは videos で別に引く。
            // playlistItems の snippet にも見出しはあるが、長さが入っていない。
            var map = new Dictionary<string, YouTubeVideoInfo>();
            string detailError;
            if (!FillVideoDetails(pending, map, out detailError))
            {
                return YouTubeFetchResult.Failure(detailError);
            }

            var videos = new List<YouTubeVideoInfo>();
            int unavailable = 0;

            for (int i = 0; i < order.Count; i++)
            {
                string id = order[i];

                if (map.ContainsKey(id))
                {
                    videos.Add(map[id]);
                    continue;
                }

                // 取れなかったものは黙って消さず、印を付けて残す。
                // 「入れたはずの曲が無い」より「これは使えません」のほうが分かる。
                var missing = new YouTubeVideoInfo();
                missing.VideoId = id;
                missing.Title = "(取得できません)";
                missing.IsUnavailable = true;
                missing.UnavailableReason = "非公開・削除済み・地域制限のいずれか";
                videos.Add(missing);
                unavailable++;
            }

            string message = videos.Count + " 件を取得しました。";
            if (unavailable > 0) message += "(うち " + unavailable + " 件は再生できません)";

            return YouTubeFetchResult.Success(videos, message).WithNextPage(pageToken);
        }

        /// <summary>動画の詳細を 50 件ずつ引いて <paramref name="map"/> へ入れる。</summary>
        private bool FillVideoDetails(
            List<string> ids, Dictionary<string, YouTubeVideoInfo> map, out string error)
        {
            error = "";

            for (int start = 0; start < ids.Count; start += 50)
            {
                int count = Math.Min(50, ids.Count - start);
                string joined = string.Join(",", ids.GetRange(start, count).ToArray());

                // statistics を足すのは人気度(viewCount)のためです(Phase7-9)。
                // 1 回の呼び出しで一緒に取れるので、API の消費は増えません。
                string query = "videos?part=snippet,contentDetails,statistics&id=" + Escape(joined);

                string json;
                if (!Get(query, out json, out error)) return false;

                var page = Parse<VideoListResponse>(json);
                if (page == null || page.items == null) continue;

                for (int i = 0; i < page.items.Length; i++)
                {
                    YouTubeVideoInfo info = ToInfo(page.items[i]);
                    if (info == null || info.VideoId.Length == 0) continue;

                    map[info.VideoId] = info;
                }
            }

            return true;
        }

        private static YouTubeVideoInfo ToInfo(VideoItem item)
        {
            if (item == null) return null;

            var info = new YouTubeVideoInfo();
            info.VideoId = item.id != null ? item.id : "";

            if (item.snippet != null)
            {
                info.Title = item.snippet.title != null ? item.snippet.title : "";
                info.ChannelTitle = item.snippet.channelTitle != null ? item.snippet.channelTitle : "";
                info.ChannelId = item.snippet.channelId != null ? item.snippet.channelId : "";
                info.PublishedAt = item.snippet.publishedAt != null ? item.snippet.publishedAt : "";
                info.Description = item.snippet.description != null ? item.snippet.description : "";
                info.ThumbnailUrl = PickThumbnail(item.snippet.thumbnails);

                // Phase6-4: 関連と絞り込みの材料。どちらも無いことがあるので既定を保つ。
                if (item.snippet.tags != null) info.Tags = item.snippet.tags;
                info.CategoryId = item.snippet.categoryId != null ? item.snippet.categoryId : "";

                info.LiveBroadcastContent = item.snippet.liveBroadcastContent != null
                    ? item.snippet.liveBroadcastContent
                    : "";
            }

            if (item.contentDetails != null)
            {
                info.DurationSeconds = YouTubeDurationParser.ToSeconds(item.contentDetails.duration);
            }

            if (item.statistics != null && !string.IsNullOrEmpty(item.statistics.viewCount))
            {
                long views;
                if (long.TryParse(item.statistics.viewCount, out views)) info.ViewCount = views;
            }

            return info;
        }

        /// <summary>いちばん大きいサムネイルを選ぶ。</summary>
        private static string PickThumbnail(Thumbnails thumbnails)
        {
            if (thumbnails == null) return "";

            if (thumbnails.maxres != null && !string.IsNullOrEmpty(thumbnails.maxres.url))
                return thumbnails.maxres.url;
            if (thumbnails.standard != null && !string.IsNullOrEmpty(thumbnails.standard.url))
                return thumbnails.standard.url;
            if (thumbnails.high != null && !string.IsNullOrEmpty(thumbnails.high.url))
                return thumbnails.high.url;
            if (thumbnails.medium != null && !string.IsNullOrEmpty(thumbnails.medium.url))
                return thumbnails.medium.url;
            if (thumbnails.@default != null && !string.IsNullOrEmpty(thumbnails.@default.url))
                return thumbnails.@default.url;

            return "";
        }

        // ───────── 通信 ─────────

        /// <summary>GET して本文を返す。失敗したら理由を <paramref name="error"/> へ。</summary>
        private bool Get(string query, out string json, out string error)
        {
            json = "";
            error = "";

            string url = Api + query + "&key=" + Escape(Key);

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = Timeout;

                UnityWebRequestAsyncOperation operation = request.SendWebRequest();

                // エディタを止めて待つ。Import が同期の口なので、ここで待つしかない。
                while (!operation.isDone)
                {
                    System.Threading.Thread.Sleep(10);
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    error = Explain(request);
                    return false;
                }

                json = request.downloadHandler.text;
                return true;
            }
        }

        /// <summary>
        /// 失敗を、直せる形の言葉にする。
        /// 「403」とだけ出しても何をすればよいか分からないため。
        /// </summary>
        private static string Explain(UnityWebRequest request)
        {
            long code = request.responseCode;

            if (code == 403)
            {
                return "403: API キーが無効か、割り当てを使い切っています。\n"
                       + "Google Cloud で YouTube Data API v3 が有効か、"
                       + "キーの制限(HTTP リファラなど)が厳しすぎないか確認してください。";
            }
            if (code == 400)
            {
                return "400: 指定が正しくありません。URL / ID を確認してください。";
            }
            if (code == 404)
            {
                return "404: 見つかりません。非公開か、削除済みの可能性があります。";
            }
            if (code == 0)
            {
                return "通信できませんでした: " + request.error + "\n"
                       + "ネットワークとプロキシの設定を確認してください。";
            }

            return code + ": " + request.error;
        }

        private static string Escape(string value)
        {
            return UnityWebRequest.EscapeURL(value != null ? value : "");
        }

        private int Clamp(int wanted)
        {
            int limit = MaxItems;
            if (wanted <= 0) return limit;
            return wanted < limit ? wanted : limit;
        }

        private static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[YouTubeDataApiClient] 応答を読めませんでした: " + e.Message);
                return null;
            }
        }

        // ───────── 応答の形(JsonUtility 用)─────────

        [Serializable]
        private sealed class ChannelListResponse
        {
            public ChannelItem[] items;
        }

        [Serializable]
        private sealed class ChannelItem
        {
            public string id;
            public ChannelContentDetails contentDetails;
        }

        [Serializable]
        private sealed class ChannelContentDetails
        {
            public RelatedPlaylists relatedPlaylists;
        }

        [Serializable]
        private sealed class RelatedPlaylists
        {
            public string uploads;
        }

        [Serializable]
        private sealed class PlaylistItemListResponse
        {
            public string nextPageToken;
            public PlaylistItem[] items;
        }

        [Serializable]
        private sealed class PlaylistItem
        {
            public PlaylistItemContentDetails contentDetails;
        }

        [Serializable]
        private sealed class PlaylistItemContentDetails
        {
            public string videoId;
        }

        [Serializable]
        private sealed class SearchListResponse
        {
            public string nextPageToken;
            public SearchItem[] items;
        }

        [Serializable]
        private sealed class SearchItem
        {
            public SearchItemId id;
        }

        [Serializable]
        private sealed class SearchItemId
        {
            public string videoId;
        }

        [Serializable]
        private sealed class ChannelIdListResponse
        {
            public ChannelIdItem[] items;
        }

        [Serializable]
        private sealed class ChannelIdItem
        {
            public string id;
        }

        [Serializable]
        private sealed class VideoListResponse
        {
            public VideoItem[] items;
        }

        [Serializable]
        private sealed class VideoItem
        {
            public string id;
            public VideoSnippet snippet;
            public VideoContentDetails contentDetails;
            public VideoStatistics statistics;
        }

        [Serializable]
        private sealed class VideoStatistics
        {
            // JSON では文字列で来る(64bit の数を JSON の数値で送らないため)。
            public string viewCount;
        }

        [Serializable]
        private sealed class VideoSnippet
        {
            public string title;
            public string description;
            public string channelId;
            public string channelTitle;
            public string publishedAt;
            public Thumbnails thumbnails;

            // Phase6-4: 関連(RelatedIds)の材料。tags は付いていない動画も多い。
            public string[] tags;
            public string categoryId;

            // Phase7-4: "none" / "live" / "upcoming"。
            // 生配信と配信予定(プレミア公開を含む)を見分ける唯一の手掛かり。
            public string liveBroadcastContent;
        }

        [Serializable]
        private sealed class VideoContentDetails
        {
            public string duration;
        }

        [Serializable]
        private sealed class Thumbnails
        {
            public Thumbnail @default;
            public Thumbnail medium;
            public Thumbnail high;
            public Thumbnail standard;
            public Thumbnail maxres;
        }

        [Serializable]
        private sealed class Thumbnail
        {
            public string url;
        }
    }
}
#endif
