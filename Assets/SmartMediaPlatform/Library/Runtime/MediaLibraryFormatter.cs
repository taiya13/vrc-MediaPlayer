using System.Text;
using SmartMediaPlatform.Catalog.Store;

namespace SmartMediaPlatform.Library
{
    /// <summary>
    /// 一覧を表示用の文字列に整形する。
    /// <c>QueueFormatter</c>(Phase1-4)と同じ立ち位置で、
    /// <b>文字列を組み立てるだけ</b>で UI には一切依存しません(純粋 C#)。
    ///
    /// <b>URL は絶対に出しません。</b>
    /// 表示するのは タイトル / アーティスト / ジャンル / タグ / 長さ / 種別 だけです
    /// (URL を知るのは <c>VideoBackend</c> だけ、という約束を表示側でも守るため)。
    /// </summary>
    public static class MediaLibraryFormatter
    {
        /// <summary>
        /// 1 行ぶんの表示。選択中の行には <c>&gt;</c> が付きます。
        /// <code>
        /// &gt; 1. Neon Skyline (Official Video)  / Aurora Drive  [Video] Synthwave  #mv #night #retro  4:22
        /// </code>
        /// </summary>
        public static string FormatEntry(DisplayMeta item, int number, bool selected = false)
        {
            if (item == null) return "";

            var sb = new StringBuilder();
            sb.Append(selected ? "> " : "  ");
            sb.Append(number).Append(". ");
            sb.Append(item.Title);
            sb.Append("  / ").Append(string.IsNullOrEmpty(item.Artist) ? "(不明)" : item.Artist);
            sb.Append("  [").Append(item.Type).Append(']');

            if (!string.IsNullOrEmpty(item.Genre)) sb.Append(' ').Append(item.Genre);

            string tags = FormatTags(item);
            if (tags.Length > 0) sb.Append("  ").Append(tags);

            if (item.DurationSeconds > 0) sb.Append("  ").Append(FormatDuration(item.DurationSeconds));

            return sb.ToString();
        }

        /// <summary>タグを <c>#mv #night</c> の形に。タグが無ければ空文字。</summary>
        public static string FormatTags(DisplayMeta item)
        {
            if (item == null || item.Tags.Count == 0) return "";

            var sb = new StringBuilder();
            for (int i = 0; i < item.Tags.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append('#').Append(item.Tags[i]);
            }
            return sb.ToString();
        }

        /// <summary>秒を <c>4:22</c> の形に。</summary>
        public static string FormatDuration(int seconds)
        {
            if (seconds <= 0) return "-:--";

            int minutes = seconds / 60;
            int rest = seconds % 60;
            return $"{minutes}:{rest:00}";
        }

        /// <summary>一覧全体。ヘッダに件数と絞り込みの状態が出ます。</summary>
        public static string FormatLibrary(IMediaListView library, string header = "Media Library")
        {
            if (library == null) return header + "\n(なし)";

            var sb = new StringBuilder();
            sb.Append(header).Append("  ").Append(FormatSummary(library));

            if (library.Count == 0)
            {
                sb.AppendLine();
                sb.Append("  (表示できるメディアがありません)");
                return sb.ToString();
            }

            for (int i = 0; i < library.Count; i++)
            {
                sb.AppendLine();
                sb.Append(FormatEntry(library.GetAt(i), i + 1, i == library.SelectedIndex));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 「10 件 / 種別 Video / 並び CatalogOrder」のような 1 行。
        ///
        /// 絞り込みと並び順は <see cref="IMediaLibrary"/>(カタログ全体)だけが持つので、
        /// 関連動画の一覧(<see cref="RelatedMediaView"/>)では
        /// 件数と起点だけを出します。
        /// </summary>
        public static string FormatSummary(IMediaListView view)
        {
            if (view == null) return "";

            var sb = new StringBuilder();
            sb.Append(view.Count).Append(" 件");

            if (view is IMediaLibrary library)
            {
                sb.Append(" / 種別 ");
                var types = library.VisibleTypes;
                if (types.Count >= 5) sb.Append("すべて");
                else
                {
                    for (int i = 0; i < types.Count; i++)
                    {
                        if (i > 0) sb.Append('+');
                        sb.Append(types[i]);
                    }
                }

                sb.Append(" / 並び ").Append(library.SortOrder);
            }
            else if (view is RelatedMediaView related)
            {
                sb.Append(" / 起点 ").Append(related.SourceMediaId ?? "なし");
            }

            return sb.ToString();
        }

        /// <summary>選択中の 1 件の詳細(UI の「詳細パネル」に相当)。</summary>
        public static string FormatSelection(IMediaListView library)
        {
            if (library == null || !library.HasSelection) return "選択なし";

            var item = library.SelectedItem;
            var sb = new StringBuilder();
            sb.Append("選択中: ").Append(item.Title).AppendLine();
            sb.Append("  アーティスト : ").Append(
                string.IsNullOrEmpty(item.Artist) ? "(不明)" : item.Artist).AppendLine();
            sb.Append("  ジャンル     : ").Append(
                string.IsNullOrEmpty(item.Genre) ? "(なし)" : item.Genre).AppendLine();

            string tags = FormatTags(item);
            sb.Append("  タグ         : ").Append(tags.Length > 0 ? tags : "(なし)").AppendLine();

            sb.Append("  種別         : ").Append(item.Type).AppendLine();
            sb.Append("  長さ         : ").Append(FormatDuration(item.DurationSeconds)).AppendLine();
            sb.Append("  MediaId      : ").Append(item.MediaId);
            return sb.ToString();
        }
    }
}
