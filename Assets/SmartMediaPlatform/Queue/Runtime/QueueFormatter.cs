using System.Text;

namespace SmartMediaPlatform.Queue
{
    /// <summary>
    /// キューの状態を Console 表示用の文字列に整形する。
    /// 文字列を組み立てるだけで UI には一切依存しない(純粋 C#)。
    /// Demo と EditMode テストの双方から使えるようにここへ置く。
    /// </summary>
    public static class QueueFormatter
    {
        /// <summary>
        /// 番号付きの一覧。
        /// <code>
        /// Queue
        /// 1. music-001
        /// 2. music-004
        /// </code>
        /// </summary>
        public static string FormatQueue(IQueue queue, string header = "Queue")
        {
            var sb = new StringBuilder();
            sb.Append(header);

            var items = queue.GetAll();
            if (items.Count == 0)
            {
                sb.AppendLine();
                sb.Append("(empty)");
                return sb.ToString();
            }

            for (int i = 0; i < items.Count; i++)
            {
                sb.AppendLine();
                sb.Append(i + 1).Append(". ").Append(items[i].MediaId);
            }
            return sb.ToString();
        }

        /// <summary>詳細付きの一覧(タイトル・アーティスト・積まれた経緯)。</summary>
        public static string FormatQueueVerbose(IQueue queue, string header = "Queue")
        {
            var sb = new StringBuilder();
            sb.Append(header);

            var items = queue.GetAll();
            if (items.Count == 0)
            {
                sb.AppendLine();
                sb.Append("(empty)");
                return sb.ToString();
            }

            for (int i = 0; i < items.Count; i++)
            {
                var entry = items[i];
                sb.AppendLine();
                sb.Append(i + 1).Append(". ").Append(entry.MediaId)
                  .Append("  ").Append(entry.Item.Title).Append(" / ").Append(entry.Item.Artist)
                  .Append("  [").Append(entry.Source).Append(']');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Now / Next の 2 行。
        /// <code>
        /// Now
        /// music-004
        /// Next
        /// music-010
        /// </code>
        /// </summary>
        public static string FormatNowNext(IQueue queue)
        {
            var now = queue.Peek();
            var next = queue.PeekNext();

            var sb = new StringBuilder();
            sb.Append("Now").AppendLine();
            sb.Append(now != null ? now.MediaId : "(none)").AppendLine();
            sb.Append("Next").AppendLine();
            sb.Append(next != null ? next.MediaId : "(none)");
            return sb.ToString();
        }
    }
}
