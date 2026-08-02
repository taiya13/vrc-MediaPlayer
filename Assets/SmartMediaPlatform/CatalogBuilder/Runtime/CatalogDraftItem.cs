using System;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.CatalogBuilder
{
    /// <summary>
    /// <b>編集中の 1 件。</b>Phase6-1。
    ///
    /// <see cref="MediaItem"/> との違いは<b>「作りかけを許す」</b>ことだけです。
    /// <c>MediaItem</c> は ID も見出しも必須で、空だと例外を投げます。
    /// 編集中はどちらも空の行がありえるので、そのまま持てる入れ物を分けました。
    ///
    /// <b>完成した時点で <see cref="ToMediaItem"/> で <c>MediaItem</c> へ移します。</b>
    /// この一方通行のおかげで、<b>不完全なものが再生側へ流れ込みません</b>。
    /// </summary>
    public sealed class CatalogDraftItem
    {
        public string Id = "";
        public string Title = "";
        public string Artist = "";
        public string Genre = "";
        public MediaType Type = MediaType.Video;
        public string Url = "";
        public int DurationSeconds;

        public string[] Tags = new string[0];
        public string[] RelatedIds = new string[0];

        /// <summary>
        /// どこから来たか(「手入力」「YouTube」「CSV」など)。
        /// 取り込み元を混ぜたときに、何を消せばよいか分かるように持ちます。
        /// </summary>
        public string Source = "手入力";

        /// <summary>サムネイルの場所(URL)。Phase6-4 から一覧に絵を出すのに使っています。</summary>
        public string ThumbnailPath = "";

        /// <summary>
        /// 公開日(ISO8601 / 「2024-05-01」でも可)。並べ替えに使います。Phase6-5。
        /// 取れなければ空文字。
        /// </summary>
        public string PublishedAt = "";

        /// <summary>
        /// <b>再生側に無い情報。</b><see cref="MediaItem"/> は
        /// ID・見出し・URL など<b>再生に要るものしか持ちません</b>。
        /// 出どころ・サムネイル・公開日は編集のためだけのものなので、
        /// <b>アセットの横に置く JSON</b> に書き残します(<c>CatalogSidecarIO</c>)。
        ///
        /// <b>ワールドの容量を増やさないため</b>にこう分けています。
        /// Phase6-4 まではここが抜けていて、保存して読み直すと絵が消えていました。
        /// </summary>
        public bool HasEditorMetadata
        {
            get
            {
                return !string.IsNullOrWhiteSpace(ThumbnailPath)
                       || !string.IsNullOrWhiteSpace(PublishedAt)
                       || (!string.IsNullOrWhiteSpace(Source) && Source != "手入力");
            }
        }

        public CatalogDraftItem()
        {
        }

        public CatalogDraftItem(MediaItem item)
        {
            if (item == null) return;

            Id = item.Id;
            Title = item.Title;
            Artist = item.Artist;
            Genre = item.Genre;
            Type = item.Type;
            Url = item.Url;
            DurationSeconds = item.DurationSeconds;
            Tags = ToArray(item.Tags);
            RelatedIds = ToArray(item.RelatedIds);
        }

        /// <summary>再生側へ渡せる形になっているか。</summary>
        public bool IsComplete
        {
            get
            {
                return !string.IsNullOrWhiteSpace(Id)
                       && !string.IsNullOrWhiteSpace(Title)
                       && !string.IsNullOrWhiteSpace(Url);
            }
        }

        /// <summary>足りないものを 1 行で。問題なければ空文字。</summary>
        public string Describe()
        {
            if (string.IsNullOrWhiteSpace(Id)) return "ID が空です";
            if (string.IsNullOrWhiteSpace(Title)) return "見出しが空です";
            if (string.IsNullOrWhiteSpace(Url)) return "URL が空です";
            return "";
        }

        /// <summary>
        /// 再生側の形へ移す。<b>不完全なら null</b>(例外は投げない)。
        /// </summary>
        public MediaItem ToMediaItem()
        {
            if (!IsComplete) return null;

            return new MediaItem(
                Id.Trim(),
                Title.Trim(),
                Artist ?? "",
                Type,
                Genre ?? "",
                Clean(Tags),
                Url.Trim(),
                DurationSeconds < 0 ? 0 : DurationSeconds,
                Clean(RelatedIds));
        }

        public CatalogDraftItem Clone()
        {
            var copy = new CatalogDraftItem();
            copy.Id = Id;
            copy.Title = Title;
            copy.Artist = Artist;
            copy.Genre = Genre;
            copy.Type = Type;
            copy.Url = Url;
            copy.DurationSeconds = DurationSeconds;
            copy.Tags = Clean(Tags);
            copy.RelatedIds = Clean(RelatedIds);
            copy.Source = Source;
            copy.ThumbnailPath = ThumbnailPath;
            copy.PublishedAt = PublishedAt;
            return copy;
        }

        private static string[] ToArray(System.Collections.Generic.IReadOnlyList<string> values)
        {
            if (values == null) return new string[0];

            var result = new string[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = values[i];
            return result;
        }

        /// <summary>空要素を落とす。取り込み元が末尾に空を付けてくることがあるため。</summary>
        private static string[] Clean(string[] values)
        {
            if (values == null) return new string[0];

            int kept = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i])) kept++;
            }

            var result = new string[kept];
            int at = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(values[i])) continue;
                result[at] = values[i].Trim();
                at++;
            }
            return result;
        }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Title) ? "(名前なし)" : Title;
        }
    }
}
