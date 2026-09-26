#if UNITY_EDITOR
using System.IO;
using SmartMediaPlatform.Catalog.Assets;
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.CatalogBuilder.EditorTools
{
    /// <summary>
    /// <b>覚え書き(<see cref="CatalogSidecar"/>)の読み書き。</b>Phase6-5。
    ///
    /// <b>置き場所は Catalog.asset の真横</b>です。
    /// <c>Catalog.asset</c> → <c>Catalog.builder.json</c>。
    /// <list type="bullet">
    /// <item>アセットを別の場所へ移しても、一緒に付いてくる(名前で引くため)</item>
    /// <item>テキストなので<b>git の差分が読めます</b></item>
    /// <item><b>ワールドには入りません。</b>どこからも参照されない JSON なので、
    ///       VRChat のビルドに含まれません</item>
    /// </list>
    ///
    /// <b>無くても動きます。</b>ファイルが無ければ空の覚え書きを返すだけで、
    /// カタログそのものは今までどおり読めます。
    /// </summary>
    public static class CatalogSidecarIO
    {
        public const string Extension = ".builder.json";

        /// <summary>覚え書きの置き場所。アセットが無ければ空文字。</summary>
        public static string PathFor(MediaCatalogAsset asset)
        {
            if (asset == null) return "";

            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath)) return "";

            string directory = Path.GetDirectoryName(assetPath);
            string name = Path.GetFileNameWithoutExtension(assetPath);

            return Path.Combine(directory, name + Extension).Replace('\\', '/');
        }

        public static bool Exists(MediaCatalogAsset asset)
        {
            string path = PathFor(asset);
            return path.Length > 0 && File.Exists(path);
        }

        /// <summary>読む。無ければ<b>空の覚え書き</b>(null は返しません)。</summary>
        public static CatalogSidecar Load(MediaCatalogAsset asset)
        {
            string path = PathFor(asset);
            if (path.Length == 0 || !File.Exists(path)) return new CatalogSidecar();

            try
            {
                string json = File.ReadAllText(path);
                CatalogSidecar loaded = JsonUtility.FromJson<CatalogSidecar>(json);

                return loaded != null ? loaded : new CatalogSidecar();
            }
            catch (System.Exception e)
            {
                // 壊れていても編集は続けられるようにする。
                // ここで例外を投げると、カタログそのものが開けなくなる。
                Debug.LogWarning("[CatalogSidecarIO] 覚え書きを読めませんでした: " + e.Message);
                return new CatalogSidecar();
            }
        }

        /// <summary>
        /// 書く。<b>中身が空なら書かずに消します</b> —
        /// 手入力だけのカタログに、空のファイルを増やさないためです。
        /// </summary>
        public static bool Save(MediaCatalogAsset asset, CatalogSidecar sidecar)
        {
            string path = PathFor(asset);
            if (path.Length == 0) return false;

            bool empty = sidecar == null
                         || ((sidecar.Items == null || sidecar.Items.Length == 0)
                             && (sidecar.Subscriptions == null || sidecar.Subscriptions.Length == 0));

            try
            {
                if (empty)
                {
                    if (File.Exists(path))
                    {
                        AssetDatabase.DeleteAsset(path);
                        if (File.Exists(path)) File.Delete(path);
                    }
                    return true;
                }

                File.WriteAllText(path, JsonUtility.ToJson(sidecar, true));
                AssetDatabase.ImportAsset(path);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[CatalogSidecarIO] 覚え書きを書けませんでした: " + e.Message);
                return false;
            }
        }
    }
}
#endif
