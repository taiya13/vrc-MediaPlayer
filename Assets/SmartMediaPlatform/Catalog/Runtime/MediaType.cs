namespace SmartMediaPlatform.Catalog
{
    /// <summary>
    /// メディアの種別。
    /// Music 専用ではなく Media Platform 全体の基盤とするため、
    /// 将来の Video / Podcast / Live をこの enum の追加だけで受け入れられるようにする。
    /// 値はカタログの永続化(Phase2 以降)で使うため明示的に固定する。
    /// </summary>
    public enum MediaType
    {
        Unknown = 0,
        Music = 1,
        Video = 2,
        Podcast = 3,
        Live = 4,
    }
}
