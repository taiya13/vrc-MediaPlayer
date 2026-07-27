using System.Collections.Generic;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Video.Data
{
    /// <summary>
    /// <b>動画だけのカタログ供給元</b>(架空の作品)。
    ///
    /// Phase1-1 の <c>DummyCatalogSource</c> は Music 10 件に対して Video が 1 件しかなく、
    /// 「おすすめで次の<b>動画</b>を選び続ける」ループを確かめられません。
    /// そこで Phase3-3 用に、動画だけを 10 件揃えたソースを用意しました。
    ///
    /// <b>Catalog の設計は一切変えていません。</b>
    /// Phase1-1 が用意した拡張点 <see cref="IMediaCatalogSource"/> を実装しているだけで、
    /// <c>MediaCatalog</c> にそのまま差し込めます。
    ///
    /// おすすめが効くよう、アーティスト・ジャンル・タグ・RelatedIds に
    /// 意図的な重なりを持たせてあります:
    /// <list type="bullet">
    /// <item>Aurora Drive(Synthwave)… video-001 / 002 / 003</item>
    /// <item>Glasshouse Signal(Electronic)… video-004 / 005</item>
    /// <item>Kohaku(Lo-fi)… video-006 / 007</item>
    /// <item>Yuzuriha(Ambient)… video-008 / 009</item>
    /// <item>The Orbit Room(Jazz)… video-010</item>
    /// </list>
    /// URL は <c>DummyCatalogSource</c> と同じ形式の架空アドレスです。
    /// 実機で再生する場合は、ここを実在する動画の URL に差し替えてから
    /// <c>VRCUrlTable</c> へ焼き直してください(実行時に VRCUrl は作れません)。
    /// </summary>
    public sealed class VideoCatalogSource : IMediaCatalogSource
    {
        public IReadOnlyList<MediaItem> LoadItems()
        {
            return new[]
            {
                // ── Aurora Drive / Synthwave ────────────────────────────
                new MediaItem(
                    id: "video-001",
                    title: "Neon Skyline (Official Video)",
                    artist: "Aurora Drive",
                    type: MediaType.Video,
                    genre: "Synthwave",
                    tags: new[] { "mv", "night", "retro" },
                    url: "https://example.com/media/neon-skyline-mv",
                    durationSeconds: 262,
                    relatedIds: new[] { "video-002", "video-004" }),

                new MediaItem(
                    id: "video-002",
                    title: "Midnight Circuit (Official Video)",
                    artist: "Aurora Drive",
                    type: MediaType.Video,
                    genre: "Synthwave",
                    tags: new[] { "mv", "night", "electronic" },
                    url: "https://example.com/media/midnight-circuit-mv",
                    durationSeconds: 238,
                    relatedIds: new[] { "video-001", "video-003" }),

                new MediaItem(
                    id: "video-003",
                    title: "Chrome Harbor (Live Session)",
                    artist: "Aurora Drive",
                    type: MediaType.Video,
                    genre: "Synthwave",
                    tags: new[] { "live", "night" },
                    url: "https://example.com/media/chrome-harbor-live",
                    durationSeconds: 401,
                    relatedIds: new[] { "video-002" }),

                // ── Glasshouse Signal / Electronic ──────────────────────
                new MediaItem(
                    id: "video-004",
                    title: "Static Bloom (Official Video)",
                    artist: "Glasshouse Signal",
                    type: MediaType.Video,
                    genre: "Electronic",
                    tags: new[] { "mv", "electronic", "dance" },
                    url: "https://example.com/media/static-bloom-mv",
                    durationSeconds: 204,
                    relatedIds: new[] { "video-005", "video-001" }),

                new MediaItem(
                    id: "video-005",
                    title: "Solar Arcade (Visualizer)",
                    artist: "Glasshouse Signal",
                    type: MediaType.Video,
                    genre: "Electronic",
                    tags: new[] { "visualizer", "dance", "retro" },
                    url: "https://example.com/media/solar-arcade-visualizer",
                    durationSeconds: 229,
                    relatedIds: new[] { "video-004" }),

                // ── Kohaku / Lo-fi ─────────────────────────────────────
                new MediaItem(
                    id: "video-006",
                    title: "Paper Lanterns (Animated)",
                    artist: "Kohaku",
                    type: MediaType.Video,
                    genre: "Lo-fi",
                    tags: new[] { "animation", "chill", "night" },
                    url: "https://example.com/media/paper-lanterns-animated",
                    durationSeconds: 191,
                    relatedIds: new[] { "video-007" }),

                new MediaItem(
                    id: "video-007",
                    title: "Rainy Platform (Loop Visual)",
                    artist: "Kohaku",
                    type: MediaType.Video,
                    genre: "Lo-fi",
                    tags: new[] { "visualizer", "chill", "rain" },
                    url: "https://example.com/media/rainy-platform-loop",
                    durationSeconds: 210,
                    relatedIds: new[] { "video-006", "video-008" }),

                // ── Yuzuriha / Ambient ─────────────────────────────────
                new MediaItem(
                    id: "video-008",
                    title: "Komorebi (Nature Film)",
                    artist: "Yuzuriha",
                    type: MediaType.Video,
                    genre: "Ambient",
                    tags: new[] { "nature", "chill" },
                    url: "https://example.com/media/komorebi-film",
                    durationSeconds: 348,
                    relatedIds: new[] { "video-009" }),

                new MediaItem(
                    id: "video-009",
                    title: "First Light Anthem (Official Video)",
                    artist: "Yuzuriha",
                    type: MediaType.Video,
                    genre: "Ambient",
                    tags: new[] { "mv", "morning", "nature" },
                    url: "https://example.com/media/first-light-anthem-mv",
                    durationSeconds: 295,
                    relatedIds: new[] { "video-008" }),

                // ── The Orbit Room / Jazz ──────────────────────────────
                new MediaItem(
                    id: "video-010",
                    title: "Gravity Waltz (Studio Take)",
                    artist: "The Orbit Room",
                    type: MediaType.Video,
                    genre: "Jazz",
                    tags: new[] { "live", "instrumental" },
                    url: "https://example.com/media/gravity-waltz-studio",
                    durationSeconds: 318,
                    relatedIds: new[] { "video-003" }),
            };
        }
    }

    /// <summary>
    /// 動画と音楽を混ぜたカタログ供給元。
    ///
    /// <b>「再生できない種別が混ざっても、おすすめ再生ループが崩れない」</b>ことを
    /// 確かめるために使います(Phase3-3 の <c>IPlaybackFilter</c> の検証)。
    /// </summary>
    public sealed class MixedCatalogSource : IMediaCatalogSource
    {
        public IReadOnlyList<MediaItem> LoadItems()
        {
            var items = new List<MediaItem>();
            items.AddRange(new VideoCatalogSource().LoadItems());
            items.AddRange(new Catalog.Data.DummyCatalogSource().LoadItems());

            // DummyCatalogSource の video-001 は VideoCatalogSource と ID が重なるので落とす。
            var result = new List<MediaItem>();
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                if (seen.Add(item.Id)) result.Add(item);
            }
            return result;
        }
    }
}
