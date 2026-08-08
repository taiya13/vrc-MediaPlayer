#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.World.EditorTools
{
    /// <summary>
    /// <b>配る前に、危ない状態になっていないか調べる。</b>Phase8。
    ///
    /// ───────────────────────────────────────────────
    /// <b>なぜ要るのか</b>
    ///
    /// このシステムは<b>ツールとして配る</b>ものです。
    /// 配るのは「プレイヤー + Catalog Builder」であって、
    /// <b>あなたが焼いた曲データではありません</b>。
    ///
    /// ところが、開発中のプロジェクトには
    /// <list type="bullet">
    /// <item>1000 曲入りのカタログ</item>
    /// <item>焼き込んだサムネイル</item>
    /// <item>API キー</item>
    /// </list>
    /// が入っています。<b>これを混ぜたまま配ると、
    /// 第三者のコンテンツと自分の鍵を一緒に配ることになります</b>。
    ///
    /// ───────────────────────────────────────────────
    /// <b>「開発中は好きにしてよい」</b>
    ///
    /// ここは<b>止めません</b>。開発では 1000 曲で試すのが正しいからです。
    /// <b>配る直前に 1 回押す</b>ためのものです。
    /// </summary>
    public static class ReleaseSafetyCheck
    {
        private const string MenuPath =
            "Tools/Smart Media Platform/配る前に調べる";

        /// <summary>作った物・あなたのデータの置き場。<b>配布物に入れてはいけません。</b></summary>
        public const string DataFolder = "Assets/SmartMediaPlatform_Data";

        /// <summary>1 件ぶんの結果。</summary>
        public sealed class Finding
        {
            /// <summary>直さないと配れないか。</summary>
            public bool Blocking;

            /// <summary>何が起きているか。</summary>
            public string What = "";

            /// <summary>どうすればよいか。</summary>
            public string Fix = "";
        }

        [MenuItem(MenuPath, false, -180)]
        public static void RunMenuItem()
        {
            List<Finding> findings = Run();

            var text = new StringBuilder();
            int blocking = 0;

            for (int i = 0; i < findings.Count; i++)
            {
                Finding f = findings[i];
                if (f.Blocking) blocking++;

                text.AppendLine((f.Blocking ? "■ " : "・ ") + f.What);
                text.AppendLine("   → " + f.Fix);
                text.AppendLine();
            }

            if (findings.Count == 0)
            {
                text.AppendLine("配って問題になりそうな所は見つかりませんでした。");
                text.AppendLine();
                text.AppendLine("※ このチェックは「うっかり」を防ぐためのものです。");
                text.AppendLine("   ワールドを公開するかどうかの判断は別に考えてください。");
            }
            else
            {
                text.Insert(0, blocking > 0
                    ? "■ が付いたものは、直さずに配ると問題になります。\n\n"
                    : "気になる所がありました。\n\n");
            }

            Debug.Log("[ReleaseSafetyCheck]\n" + text);

            EditorUtility.DisplayDialog(
                blocking > 0 ? "配る前に直してください" : "配る前の確認",
                text.ToString(), "OK");
        }

        /// <summary>調べる。見つかったものだけ返します(何も無ければ空)。</summary>
        public static List<Finding> Run()
        {
            var findings = new List<Finding>();

            CheckApiKey(findings);
            CheckDataFolder(findings);
            CheckBakedCatalog(findings);
            CheckBakedThumbnails(findings);

            return findings;
        }

        // ───────── API キー ─────────

        private static void CheckApiKey(List<Finding> findings)
        {
            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject YouTubeApiSettings");

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null) continue;

                FieldInfo field = asset.GetType().GetField(
                    "ApiKey", BindingFlags.Public | BindingFlags.Instance);

                if (field == null) continue;

                string key = field.GetValue(asset) as string;
                if (string.IsNullOrEmpty(key)) continue;

                // 置き場所で深刻さが変わる。SmartMediaPlatform の中にあると
                // 「フォルダごと配る」手順でそのまま漏れる。
                bool insideShipped = path.StartsWith("Assets/SmartMediaPlatform/");

                findings.Add(new Finding
                {
                    Blocking = insideShipped,
                    What = "API キーが入っています: " + path,
                    Fix = insideShipped
                        ? "このファイルは配布物の中にあります。"
                          + DataFolder + " へ移すか、キーを空にしてください。"
                        : "配布物には含めないでください(" + DataFolder + " は配らない)。",
                });
            }
        }

        // ───────── 配布物に混ぜてはいけないフォルダ ─────────

        private static void CheckDataFolder(List<Finding> findings)
        {
            if (!AssetDatabase.IsValidFolder(DataFolder)) return;

            findings.Add(new Finding
            {
                Blocking = false,
                What = DataFolder + " があります(Prefab・サムネイル・API キーの置き場)。",
                // ここは Debug.Log とダイアログの両方に出ます。
                // ダイアログはリッチテキストを解釈しないので、装飾タグは書きません。
                Fix = "ここは配布物に含めないでください。"
                      + "zip を作るときは Assets/SmartMediaPlatform だけを入れます。",
            });
        }

        // ───────── シーンに焼かれたカタログ ─────────

        private static void CheckBakedCatalog(List<Finding> findings)
        {
            int songs;
            int thumbnails;

            if (!InspectSceneCatalog(out songs, out thumbnails)) return;

            if (songs > 0)
            {
                findings.Add(new Finding
                {
                    Blocking = false,
                    What = "シーンのカタログに " + songs + " 曲が焼き込まれています。",
                    Fix = "開発中はこのままで構いません。"
                          + "配布する Prefab / シーンには曲を入れないでください"
                          + "(Catalog Builder で空のアセットを焼けば消えます)。",
                });
            }
        }

        // ───────── 焼き込まれたサムネイル ─────────

        private static void CheckBakedThumbnails(List<Finding> findings)
        {
            int songs;
            int thumbnails;

            if (!InspectSceneCatalog(out songs, out thumbnails)) return;

            if (thumbnails > 0)
            {
                findings.Add(new Finding
                {
                    Blocking = true,
                    What = "シーンのカタログにサムネイルが " + thumbnails + " 枚焼き込まれています。",
                    Fix = "YouTube の画像をワールドと一緒に配ることになります。"
                          + "Catalog Builder の「絵の大きさ」を「焼かない」にして、"
                          + "もう一度焼き込んでください。",
                });
            }
        }

        /// <summary>
        /// シーンの <c>UdonMediaCatalog</c> を見て、曲数とサムネイル枚数を数える。
        /// <b>型を名前で探します</b> —— このクラスは U# の有無に関係なく動きます。
        /// </summary>
        private static bool InspectSceneCatalog(out int songs, out int thumbnails)
        {
            songs = 0;
            thumbnails = 0;

            System.Type type = FindTypeByName("UdonMediaCatalog");
            if (type == null) return false;

            Object[] found = Object.FindObjectsOfType(type);
            if (found == null || found.Length == 0) return false;

            FieldInfo ids = type.GetField("Ids", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo thumbs = type.GetField(
                "Thumbnails", BindingFlags.Public | BindingFlags.Instance);

            for (int i = 0; i < found.Length; i++)
            {
                if (ids != null)
                {
                    var array = ids.GetValue(found[i]) as System.Array;
                    if (array != null && array.Length > songs) songs = array.Length;
                }

                if (thumbs != null)
                {
                    var array = thumbs.GetValue(found[i]) as System.Array;
                    if (array == null) continue;

                    // null が並んでいるだけのこともあるので、中身を数える。
                    int filled = 0;
                    for (int k = 0; k < array.Length; k++)
                    {
                        if (array.GetValue(k) != null) filled++;
                    }

                    if (filled > thumbnails) thumbnails = filled;
                }
            }

            return true;
        }

        private static System.Type FindTypeByName(string simpleName)
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type[] types;
                try { types = assembly.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }

                if (types == null) continue;

                foreach (var type in types)
                {
                    if (type != null && type.Name == simpleName) return type;
                }
            }
            return null;
        }
    }
}
#endif
