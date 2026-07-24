namespace SmartMediaPlatform.Catalog.Parallel
{
    /// <summary>
    /// MediaType を「int のワイヤー値」として扱うための定数。
    /// UdonSharp では別アセンブリの enum 参照が不安定なため、
    /// Udon 層とやり取りする境界では enum ではなく int を使う。
    /// 値は Phase1-1 の <see cref="MediaType"/> と完全一致させること。
    /// </summary>
    public static class MediaTypeCode
    {
        public const int Unknown = 0;
        public const int Music = 1;
        public const int Video = 2;
        public const int Podcast = 3;
        public const int Live = 4;
    }
}
