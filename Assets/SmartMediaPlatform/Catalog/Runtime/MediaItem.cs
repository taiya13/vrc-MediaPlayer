using System;
using System.Collections.Generic;

namespace SmartMediaPlatform.Catalog
{
    /// <summary>
    /// カタログに登録される 1 メディアのメタデータ。イミュータブル。
    /// 再生・URL 解決・お気に入り等の状態は一切持たない(それらは上位レイヤーの責務)。
    /// VRChat の制約で VRCUrl は実行時生成できないため、URL はここでは文字列の
    /// メタデータとしてのみ保持し、VRCUrl 化は Phase2(Catalog Database)で行う。
    /// </summary>
    public sealed class MediaItem
    {
        private static readonly string[] Empty = new string[0];

        /// <summary>カタログ内で一意な ID(例: "music-001")。</summary>
        public string Id { get; }

        public string Title { get; }

        /// <summary>アーティスト / チャンネル / 配信者名。種別によらず「作者」を表す。</summary>
        public string Artist { get; }

        public string Genre { get; }

        public MediaType Type { get; }

        /// <summary>検索用タグ。null は渡せず、常に空以上の配列。</summary>
        public IReadOnlyList<string> Tags { get; }

        /// <summary>再生 URL(文字列)。Phase1 ではメタデータとしてのみ保持する。</summary>
        public string Url { get; }

        public int DurationSeconds { get; }

        /// <summary>
        /// 関連アイテムの ID リスト。Catalog Builder が事前計算して埋める想定。
        /// Recommendation Engine はまずこれを種にして候補を広げられる。
        /// </summary>
        public IReadOnlyList<string> RelatedIds { get; }

        public MediaItem(
            string id,
            string title,
            string artist,
            MediaType type,
            string genre = "",
            string[] tags = null,
            string url = "",
            int durationSeconds = 0,
            string[] relatedIds = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("MediaItem requires a non-empty id.", nameof(id));
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException($"MediaItem '{id}' requires a non-empty title.", nameof(title));

            Id = id;
            Title = title;
            Artist = artist ?? "";
            Genre = genre ?? "";
            Type = type;
            Tags = tags != null ? (string[])tags.Clone() : Empty;
            Url = url ?? "";
            DurationSeconds = durationSeconds;
            RelatedIds = relatedIds != null ? (string[])relatedIds.Clone() : Empty;
        }

        public override string ToString()
        {
            return $"[{Id}] {Title} / {Artist} ({Type}, {Genre})";
        }
    }
}
