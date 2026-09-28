using System.Collections.Generic;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.CatalogBuilder.YouTube
{
    /// <summary>
    /// <b>URL を貼るだけの取り込み。API を一切呼びません。</b>Phase8。
    ///
    /// ───────────────────────────────────────────────
    /// <b>なぜ API 版と分けたのか</b>
    ///
    /// YouTube API の 30 日ルールは、<b>API から取ったデータ</b>に掛かります。
    /// API を呼んでいないデータは、定義上そもそも「API Data」になりません。
    ///
    /// この経路は<b>URL の文字列を見るだけ</b>で、通信を一度もしません。
    /// 取り出すのは<b>動画 ID</b>だけで、これは規約上も期限なく持てる
    /// <b>識別子</b>です。曲名・アーティスト・ジャンルは<b>あなたが入力します</b>。
    ///
    /// つまり、この経路で作ったカタログには<b>API 由来のデータが 1 つも入りません</b>。
    /// <c>CatalogDraftItem.ApiFetchedAtUtc</c> も空のままなので、
    /// 期限管理の対象にもなりません。
    ///
    /// ───────────────────────────────────────────────
    /// <b>やらないこと</b>
    ///
    /// <b>YouTube のページを裏で読みに行くことはしません。</b>
    /// API キー無しでタイトルを取る方法はありますが、それは
    /// <b>YouTube 本体の利用規約</b>(自動的手段でのコンテンツ収集の禁止)に
    /// 触れます。API を避けたつもりで<b>より重い違反</b>になるので、
    /// この経路は<b>本当に何も取りに行きません</b>。
    ///
    /// 曲名を入れたあとは <see cref="YouTubeAutoTagger"/> が
    /// <b>あなたが入力した文字列だけ</b>を見てタグを付けます。
    /// こちらも通信しません。
    /// </summary>
    public sealed class ManualUrlCatalogImporter : ICatalogImporter
    {
        public string DisplayName { get { return "URL を貼る (API 不使用)"; } }

        public string InputHint
        {
            get
            {
                return "YouTube の URL を 1 行に 1 つ貼ってください。"
                       + "通信はしません(曲名はあとで入力します)。";
            }
        }

        /// <summary>API キーが要らないので、いつでも使えます。</summary>
        public bool IsAvailable { get { return true; } }

        public string UnavailableReason { get { return ""; } }

        public bool CanImport(string input)
        {
            return CountVideoIds(input) > 0;
        }

        public CatalogImportResult Import(string input)
        {
            var ids = new List<string>();
            var skipped = new List<string>();

            CollectVideoIds(input, ids, skipped);

            if (ids.Count == 0)
            {
                return CatalogImportResult.Failure(
                    "動画の URL が見つかりませんでした。\n"
                    + "https://www.youtube.com/watch?v=… の形で、"
                    + "1 行に 1 つ貼ってください。");
            }

            var items = new List<CatalogDraftItem>();

            for (int i = 0; i < ids.Count; i++)
            {
                var item = new CatalogDraftItem();

                item.Id = ids[i];
                item.Url = "https://www.youtube.com/watch?v=" + ids[i];
                item.Type = MediaType.Video;
                item.Source = SourceName;

                // ── ここから先は<b>空のまま</b>です。
                //    曲名もアーティストも、あなたが入力するものだからです。
                //    埋めてしまうと「どこから来た文字か」が分からなくなります。
                item.Title = "";
                item.Artist = "";
                item.Genre = "";

                items.Add(item);
            }

            string message = ids.Count + " 件の URL を読み取りました。"
                             + "曲名を入力してから「タグを自動で付ける」を押してください。";

            if (skipped.Count > 0)
            {
                message += "\n" + skipped.Count + " 行は動画の URL として読めませんでした。";
            }

            return CatalogImportResult.Success(items, message);
        }

        /// <summary>この経路で入れたものの印。API 経由と見分けるために使います。</summary>
        public const string SourceName = "手入力 URL";

        /// <summary>読み取れる動画 URL が何本あるか(ボタンを押す前の判定)。</summary>
        public static int CountVideoIds(string input)
        {
            var ids = new List<string>();
            CollectVideoIds(input, ids, null);
            return ids.Count;
        }

        /// <summary>
        /// 改行区切りの入力から動画 ID を集める。
        /// <b>同じ ID は 1 つにまとめます</b>(貼り間違いで二重に入るのを防ぐ)。
        /// </summary>
        private static void CollectVideoIds(
            string input, List<string> ids, List<string> skipped)
        {
            if (string.IsNullOrEmpty(input)) return;

            string[] lines = input.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;

                YouTubeUrlParser.Target target = YouTubeUrlParser.Parse(line);

                // ── <b>動画だけ</b>を受け付けます。
                //    再生リストやチャンネルを展開するには API が要るので、
                //    ここで受けると「API 不使用」の約束が守れません。
                if (target == null
                    || target.Kind != YouTubeUrlParser.TargetVideo
                    || target.Id.Length == 0)
                {
                    if (skipped != null) skipped.Add(line);
                    continue;
                }

                if (ids.Contains(target.Id)) continue;
                ids.Add(target.Id);
            }
        }
    }
}
