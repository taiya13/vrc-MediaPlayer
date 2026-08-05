#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.CatalogBuilder.YouTube
{
    /// <summary>
    /// <b>YouTube Data API v3 の設定。</b>Phase6-2。
    ///
    /// <b>API キーはここに入れます。</b>置き場所は
    /// <c>Assets/SmartMediaPlatform_Data/YouTubeApiSettings.asset</c>。
    ///
    /// <b>なぜ SmartMediaPlatform の外に置くのか</b>(Phase7-3)<br/>
    /// このシステムは更新のたびに <c>Assets/SmartMediaPlatform</c> を丸ごと
    /// 入れ替えてもらう配布方式です。以前はキーをその中に置いていたため、
    /// <b>更新のたびに設定ごと消えていました</b>。
    /// あなたのデータは <c>SmartMediaPlatform_Data</c> 側に置きます。
    /// 古い場所にあった設定は、最初に読むときに自動で引っ越します。
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
            "Assets/SmartMediaPlatform_Data/YouTubeApiSettings.asset";

        /// <summary>Phase7-2 までの置き場所。見つけたら <see cref="AssetPath"/> へ移す。</summary>
        private const string LegacyAssetPath =
            "Assets/SmartMediaPlatform/CatalogBuilder/Importers/YouTube/YouTubeApiSettings.asset";

        [Tooltip("Google Cloud で作った YouTube Data API v3 の API キー")]
        public string ApiKey = "";

        [Tooltip("1 回の取得で取る上限。API の割り当てを使い切らないための歯止め")]
        [Range(1, 500)]
        public int MaxItems = 100;

        [Tooltip("通信の待ち時間(秒)")]
        [Range(5, 120)]
        public int TimeoutSeconds = 20;

        [Header("取り込むもの")]
        [Tooltip("ショート動画を取り込まない。大画面では左右が黒いまますぐ終わるため、"
                 + "チャンネルごと取り込むと再生がほぼ成立しません")]
        public bool ExcludeShorts = true;

        [Tooltip("これ以下の長さはショートとみなす(秒)。"
                 + "#shorts の印が付いているものは、長さに関係なく外します")]
        [Range(15, 180)]
        public int MaxShortSeconds = 60;

        [Tooltip("見出しから、頭のチャンネル名と末尾の飾り([Official Music Video] など)を落とす。"
                 + "1 つのチャンネルを丸ごと取り込むと、全部の見出しの頭に同じ名前が並ぶため")]
        public bool CleanTitles = true;

        public bool HasKey
        {
            get { return !string.IsNullOrWhiteSpace(ApiKey); }
        }

        /// <summary>読み込む。無ければ作る。</summary>
        public static YouTubeApiSettings LoadOrCreate()
        {
            var found = LoadIfPresent();
            if (found != null) return found;

            var created = CreateInstance<YouTubeApiSettings>();

            EnsureFolder();
            AssetDatabase.CreateAsset(created, AssetPath);
            AssetDatabase.SaveAssets();
            return created;
        }

        /// <summary>読み込むだけ(無ければ null)。作らずに存在を確かめたいとき。</summary>
        public static YouTubeApiSettings LoadIfPresent()
        {
            var found = AssetDatabase.LoadAssetAtPath<YouTubeApiSettings>(AssetPath);
            if (found != null) return found;

            return MigrateLegacy();
        }

        /// <summary>
        /// 古い場所(SmartMediaPlatform の中)にある設定を、消えない側へ移す。
        /// 移せなかったときは古いものをそのまま使う(キーを失わないことを最優先)。
        /// </summary>
        private static YouTubeApiSettings MigrateLegacy()
        {
            var legacy = AssetDatabase.LoadAssetAtPath<YouTubeApiSettings>(LegacyAssetPath);
            if (legacy == null) return null;

            EnsureFolder();
            string error = AssetDatabase.MoveAsset(LegacyAssetPath, AssetPath);
            if (string.IsNullOrEmpty(error))
            {
                Debug.Log(
                    "[YouTubeApiSettings] 設定を移しました(更新で消えない場所へ): " + AssetPath);
                return AssetDatabase.LoadAssetAtPath<YouTubeApiSettings>(AssetPath);
            }

            return legacy;
        }

        private static void EnsureFolder()
        {
            string folder = System.IO.Path.GetDirectoryName(AssetPath);
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;

            System.IO.Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
        }
    }
}
#endif
