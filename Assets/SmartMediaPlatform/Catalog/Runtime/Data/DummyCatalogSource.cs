using System.Collections.Generic;

namespace SmartMediaPlatform.Catalog.Data
{
    /// <summary>
    /// Phase1 用のダミーデータ供給元(架空のアーティスト・作品)。
    /// Music 10 件に加え、Video / Podcast を 1 件ずつ混ぜて
    /// カタログが Music 専用でないことを最初から保証する。
    /// Phase2 で Catalog Builder 生成のソースに差し替えたら、このクラスは Demo/Tests 専用になる。
    /// </summary>
    public sealed class DummyCatalogSource : IMediaCatalogSource
    {
        public IReadOnlyList<MediaItem> LoadItems()
        {
            return new[]
            {
                new MediaItem(
                    id: "music-001",
                    title: "Neon Skyline",
                    artist: "Aurora Drive",
                    type: MediaType.Music,
                    genre: "Synthwave",
                    tags: new[] { "night", "drive", "retro" },
                    url: "https://example.com/media/neon-skyline",
                    durationSeconds: 254,
                    relatedIds: new[] { "music-002", "music-007" }),

                new MediaItem(
                    id: "music-002",
                    title: "Midnight Circuit",
                    artist: "Aurora Drive",
                    type: MediaType.Music,
                    genre: "Synthwave",
                    tags: new[] { "night", "electronic" },
                    url: "https://example.com/media/midnight-circuit",
                    durationSeconds: 231,
                    relatedIds: new[] { "music-001" }),

                new MediaItem(
                    id: "music-003",
                    title: "Paper Lanterns",
                    artist: "Kohaku",
                    type: MediaType.Music,
                    genre: "Lo-fi",
                    tags: new[] { "chill", "study", "night" },
                    url: "https://example.com/media/paper-lanterns",
                    durationSeconds: 187,
                    relatedIds: new[] { "music-004", "music-009" }),

                new MediaItem(
                    id: "music-004",
                    title: "Rainy Platform",
                    artist: "Kohaku",
                    type: MediaType.Music,
                    genre: "Lo-fi",
                    tags: new[] { "chill", "rain" },
                    url: "https://example.com/media/rainy-platform",
                    durationSeconds: 203,
                    relatedIds: new[] { "music-003" }),

                new MediaItem(
                    id: "music-005",
                    title: "Gravity Waltz",
                    artist: "The Orbit Room",
                    type: MediaType.Music,
                    genre: "Jazz",
                    tags: new[] { "live", "instrumental" },
                    url: "https://example.com/media/gravity-waltz",
                    durationSeconds: 312,
                    relatedIds: new[] { "music-006" }),

                new MediaItem(
                    id: "music-006",
                    title: "Half Past Blue",
                    artist: "The Orbit Room",
                    type: MediaType.Music,
                    genre: "Jazz",
                    tags: new[] { "instrumental", "night" },
                    url: "https://example.com/media/half-past-blue",
                    durationSeconds: 276,
                    relatedIds: new[] { "music-005", "music-003" }),

                new MediaItem(
                    id: "music-007",
                    title: "Static Bloom",
                    artist: "Glasshouse Signal",
                    type: MediaType.Music,
                    genre: "Electronic",
                    tags: new[] { "electronic", "dance" },
                    url: "https://example.com/media/static-bloom",
                    durationSeconds: 198,
                    relatedIds: new[] { "music-001", "music-008" }),

                new MediaItem(
                    id: "music-008",
                    title: "Solar Arcade",
                    artist: "Glasshouse Signal",
                    type: MediaType.Music,
                    genre: "Electronic",
                    tags: new[] { "dance", "retro" },
                    url: "https://example.com/media/solar-arcade",
                    durationSeconds: 224,
                    relatedIds: new[] { "music-007" }),

                new MediaItem(
                    id: "music-009",
                    title: "Komorebi",
                    artist: "Yuzuriha",
                    type: MediaType.Music,
                    genre: "Ambient",
                    tags: new[] { "chill", "nature" },
                    url: "https://example.com/media/komorebi",
                    durationSeconds: 341,
                    relatedIds: new[] { "music-003" }),

                new MediaItem(
                    id: "music-010",
                    title: "First Light Anthem",
                    artist: "Yuzuriha",
                    type: MediaType.Music,
                    genre: "Ambient",
                    tags: new[] { "morning", "nature" },
                    url: "https://example.com/media/first-light-anthem",
                    durationSeconds: 289,
                    relatedIds: new[] { "music-009" }),

                new MediaItem(
                    id: "video-001",
                    title: "Neon Skyline (Official Video)",
                    artist: "Aurora Drive",
                    type: MediaType.Video,
                    genre: "Synthwave",
                    tags: new[] { "mv", "night", "retro" },
                    url: "https://example.com/media/neon-skyline-mv",
                    durationSeconds: 262,
                    relatedIds: new[] { "music-001", "music-002" }),

                new MediaItem(
                    id: "podcast-001",
                    title: "Worlds & Waveforms #12",
                    artist: "VR Sound Lab",
                    type: MediaType.Podcast,
                    genre: "Talk",
                    tags: new[] { "talk", "music", "vr" },
                    url: "https://example.com/media/worlds-waveforms-12",
                    durationSeconds: 1820,
                    relatedIds: new string[0]),
            };
        }
    }
}
