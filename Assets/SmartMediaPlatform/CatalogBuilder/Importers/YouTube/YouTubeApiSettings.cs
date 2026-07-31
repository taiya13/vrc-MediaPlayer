#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.CatalogBuilder.YouTube
{
    /// <summary>
    /// <b>YouTube Data API v3 の設定。</b>Phase6-2。
    ///
    /// <b>API キーはここに入れます。</b>置き場所は
    /// <c>Assets/SmartMediaPlatform/CatalogBuilder/Importers/YouTube/YouTubeApiSettings.asset</c>。
    ///
    /// <b>このアセットを公開リポジトリへ入れないでください。</b>
    /// キーが漏れると他人に使われて割り当てを食い潰されます。
    /// <c>.gitignore</c> へ追加するか、キーに HTTP リファラ制限を掛けてください。
    ///
    /// <b>ワールドには含まれません。</b>この asmdef は <c>includePlatforms: Editor</c> なので、
    /// ビルドしたワールドにはこのクラスもキーも入りません。
    /// </summary>
    public sealed class YouTubeApiSettings : ScriptableObject
    {
        public const string AssetPath =
            "Assets/SmartMediaPlatform/CatalogBuilder/Importers/YouTube/YouTubeApiSettings.asset";

        [Tooltip("Google Cloud で作った YouTube Data API v3 の API キー")]
        public string ApiKey = "";

        [Tooltip("1 回の取得で取る上限。API の割り当てを使い切らないための歯止め")]
        [Range(1, 500)]
        public int MaxItems = 100;

        [Tooltip("通信の待ち時間(秒)")]
        [Range(5, 120)]
        public int TimeoutSeconds = 20;

        public bool HasKey
        {
            get { return !string.IsNullOrWhiteSpace(ApiKey); }
        }

        /// <summary>読み込む。無ければ作る。</summary>
        public static YouTubeApiSettings LoadOrCreate()
        {
            var found = AssetDatabase.LoadAssetAtPath<YouTubeApiSettings>(AssetPath);
            if (found != null) return found;

            var created = CreateInstance<YouTubeApiSettings>();

            string folder = System.IO.Path.GetDirectoryName(AssetPath);
            if (!string.IsNullOrEmpty(folder)) System.IO.Directory.CreateDirectory(folder);

            AssetDatabase.CreateAsset(created, AssetPath);
            AssetDatabase.SaveAssets();
            return created;
        }

        /// <summary>読み込むだけ(無ければ null)。作らずに存在を確かめたいとき。</summary>
        public static YouTubeApiSettings LoadIfPresent()
        {
            return AssetDatabase.LoadAssetAtPath<YouTubeApiSettings>(AssetPath);
        }
    }
}
#endif
