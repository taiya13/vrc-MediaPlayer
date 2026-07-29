using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Assets;
using SmartMediaPlatform.Video.Data;
using UnityEngine;

namespace SmartMediaPlatform.World
{
    /// <summary>
    /// <b>どのカタログを見せるかを決めるコンポーネント。</b>既定の <see cref="ICatalogProvider"/>。
    ///
    /// <b>Catalog Builder の差し込み口です。</b>
    /// <list type="bullet">
    /// <item><see cref="_asset"/> を割り当てれば <c>Catalog.asset</c> を使う(将来 Catalog Builder が生成)</item>
    /// <item>未設定なら同梱のサンプル(<c>VideoCatalogSource</c> / <c>MixedCatalogSource</c>)</item>
    /// </list>
    /// どちらでも <c>CatalogStore</c> から先は同じなので、
    /// <b>差し替えても Prefab の他の部分は変わりません</b>。
    /// </summary>
    [AddComponentMenu("Smart Media Platform/Default Catalog Provider")]
    public sealed class DefaultCatalogProvider : MonoBehaviour, ICatalogProvider
    {
        [Header("カタログ")]
        [Tooltip("Catalog Builder が生成した Catalog.asset。未設定なら同梱サンプル")]
        [SerializeField] private MediaCatalogAsset _asset;

        [Tooltip("同梱サンプルを使うとき、音楽も混ぜるか")]
        [SerializeField] private bool _includeMusic;

        public IMediaCatalog CreateCatalog()
        {
            if (_asset != null && _asset.Count > 0) return new MediaCatalog(_asset);

            return _includeMusic
                ? new MediaCatalog(new MixedCatalogSource())
                : CreateDefaultCatalog();
        }

        /// <summary>何も設定が無いときのカタログ(動画 10 本の同梱サンプル)。</summary>
        public static IMediaCatalog CreateDefaultCatalog()
        {
            return new MediaCatalog(new VideoCatalogSource());
        }

        public string Describe()
        {
            if (_asset != null && _asset.Count > 0) return $"Catalog.asset({_asset.Count} 件)";
            return _includeMusic ? "同梱サンプル(動画 + 音楽)" : "同梱サンプル(動画のみ)";
        }

        public override string ToString() => $"DefaultCatalogProvider({Describe()})";
    }
}
