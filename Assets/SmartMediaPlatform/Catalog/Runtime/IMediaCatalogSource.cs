using System.Collections.Generic;

namespace SmartMediaPlatform.Catalog
{
    /// <summary>
    /// カタログの供給元を抽象化する。
    /// Phase1 はダミーデータ、Phase2 以降は Catalog Builder が生成した
    /// ScriptableObject / JSON などがこの口から入る(MediaCatalog 側は変更不要)。
    /// </summary>
    public interface IMediaCatalogSource
    {
        IReadOnlyList<MediaItem> LoadItems();
    }
}
