using System;
using System.Collections.Generic;

namespace SmartMediaPlatform.Catalog.Store
{
    /// <summary>
    /// <b>画面に出すための情報だけ</b>を持つ、表示専用のデータ。
    ///
    /// <b>URL を持ちません。</b>これが <see cref="PlayableRef"/> との分離の要点です。
    /// <c>MediaItem</c> は表示用の項目(タイトル・アーティスト…)と
    /// 再生用の項目(<c>Url</c>)を 1 つのクラスに同居させているので、
    /// そのまま UI へ渡すと <b>UI が URL を触れてしまいます</b>。
    /// Phase3 から通している「URL を知るのは VideoBackend だけ」を
    /// <b>型のレベルで</b>守るために、表示用だけを取り出したのがこの クラス です。
    ///
    /// <code>
    /// // UI が受け取るのはこれ(URL は入っていない)
    /// DisplayMeta meta = store.GetDisplayMeta("video-001");
    /// label.text = $"{meta.Title} / {meta.Artist}";
    ///
    /// // 再生側へ渡すのはこれ(MediaId だけを運ぶ)
    /// PlayableRef playable = store.GetPlayableRef("video-001");
    /// </code>
    ///
    /// <b>作れるのは <see cref="ICatalogStore"/> だけ</b>にしてあります
    /// (<c>MediaItem</c> から作る口を公開すると、結局どこでも作れてしまうため)。
    ///
    /// イミュータブル。純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public sealed class DisplayMeta
    {
        private static readonly string[] Empty = new string[0];

        /// <summary>カタログ内で一意な ID。<see cref="PlayableRef"/> との対応づけに使います。</summary>
        public string MediaId { get; }

        public string Title { get; }

        /// <summary>アーティスト / チャンネル / 配信者名。</summary>
        public string Artist { get; }

        public string Genre { get; }

        /// <summary>検索・分類用のタグ。null にはなりません。</summary>
        public IReadOnlyList<string> Tags { get; }

        /// <summary>秒。不明なら 0。</summary>
        public int DurationSeconds { get; }

        /// <summary>種別。UI が「動画/音楽」の見た目を分けるのに使います。</summary>
        public MediaType Type { get; }

        /// <param name="mediaId">カタログ内で一意な ID。</param>
        public DisplayMeta(
            string mediaId,
            string title,
            string artist = "",
            string genre = "",
            IReadOnlyList<string> tags = null,
            int durationSeconds = 0,
            MediaType type = MediaType.Unknown)
        {
            if (string.IsNullOrWhiteSpace(mediaId))
                throw new ArgumentException("DisplayMeta requires a non-empty media id.", nameof(mediaId));

            MediaId = mediaId;
            Title = title ?? "";
            Artist = artist ?? "";
            Genre = genre ?? "";
            Tags = tags ?? Empty;
            DurationSeconds = durationSeconds < 0 ? 0 : durationSeconds;
            Type = type;
        }

        /// <summary>表示できる中身があるか(タイトルが空でないか)。</summary>
        public bool HasTitle => Title.Length > 0;

        public override string ToString()
        {
            return $"DisplayMeta({MediaId}: {Title} / {Artist} [{Type}])";
        }
    }
}
