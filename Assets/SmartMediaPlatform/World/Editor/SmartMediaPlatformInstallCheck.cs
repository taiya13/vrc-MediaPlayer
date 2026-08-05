#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.World.EditorTools
{
    /// <summary>
    /// <b>導入チェック。</b>Phase7-3。
    /// このシステムが<b>プロジェクトに 1 個だけ、正しい場所に</b>入っているかを調べます。
    ///
    /// <b>なぜ要るのか</b><br/>
    /// 更新は「<c>Assets/SmartMediaPlatform</c> を削除して入れ替え」ですが、
    /// zip の展開場所を 1 段間違えると <c>Assets/Assets/…</c> や
    /// <c>Assets/SmartMediaPlatform/Assets/…</c> という<b>入れ子のコピー</b>ができます。
    /// コピーが 2 つあると同じ名前のアセンブリ(asmdef)が衝突して
    /// <b>コンパイルが止まり、画面・音・ボタンが全部動かなくなります</b>。
    /// しかもエラーの文面からは原因が分かりにくい。
    ///
    /// <b>壊れた状態でも動きます。</b>コンパイルが失敗している間、Unity は
    /// 最後に成功したアセンブリを使い続けるので、このメニューは押せます。
    /// </summary>
    public static class SmartMediaPlatformInstallCheck
    {
        private const string MenuPath =
            "Tools/Smart Media Platform/導入チェック(二重コピーを調べる)";

        /// <summary>このシステム 1 コピーにつき必ず 1 つある目印のファイル。</summary>
        private const string MarkerFileName = "UdonSmartMediaPlayer.cs";

        /// <summary>目印から見た、システムの入っているフォルダまでの相対位置。</summary>
        private const string MarkerSuffix = "/World/Udon/" + MarkerFileName;

        /// <summary>正しい置き場所。</summary>
        private const string ExpectedRoot = "Assets/SmartMediaPlatform";

        [MenuItem(MenuPath, false, -185)]
        public static void RunMenuItem()
        {
            var sb = new StringBuilder();
            bool ok = Run(sb);

            Debug.Log("[SmartMediaPlatformInstallCheck] " + sb);

            EditorUtility.DisplayDialog(
                "Smart Media Platform — 導入チェック",
                (ok ? "問題ありません。\n\n" : "直すところがあります。\n\n") + sb,
                "OK");
        }

        /// <summary>調べて結果を書き込む。true = 問題なし。</summary>
        public static bool Run(StringBuilder sb)
        {
            bool ok = true;

            // ── 1. システムのコピーがいくつ入っているか
            List<string> roots = FindSystemRoots();

            if (roots.Count == 0)
            {
                // 目印が見つからないのにこのコードが動いている = 名前を変えている等。
                sb.AppendLine("？ " + MarkerFileName + " が見つかりません。");
                sb.AppendLine("   フォルダ名を変えている場合はこのチェックは使えません。");
                ok = false;
            }
            else if (roots.Count == 1)
            {
                sb.AppendLine("✓ システムは 1 個だけ入っています: " + roots[0]);

                if (roots[0] != ExpectedRoot)
                {
                    ok = false;
                    sb.AppendLine("✗ ただし置き場所が標準と違います。");
                    sb.AppendLine("   標準: " + ExpectedRoot);
                    sb.AppendLine("   → このままだと次の更新手順(標準の場所を削除して入れ替え)が");
                    sb.AppendLine("     効かず、コピーが 2 つになります。");
                    sb.AppendLine("     Project ウィンドウで " + FolderOf(roots[0]) + " を");
                    sb.AppendLine("     Assets 直下へドラッグして移動してください。");
                }
            }
            else
            {
                ok = false;
                sb.AppendLine("✗ システムが " + roots.Count + " 個入っています。これが不具合の原因です。");
                foreach (string root in roots)
                {
                    sb.AppendLine("   - " + root
                        + (root == ExpectedRoot ? "  ← これを残す" : "  ← これを削除"));
                }
                sb.AppendLine("   同じ名前のアセンブリが衝突してコンパイルが止まるため、");
                sb.AppendLine("   画面・音・ボタンが全部動かなくなります。");
                sb.AppendLine("   → 残す 1 つ以外を、Project ウィンドウで右クリック → Delete。");
            }

            // ── 2. 展開ミスでできる入れ子フォルダ
            ReportIfFolderExists(sb, "Assets/Assets",
                "zip を Assets の中で展開すると出来ます。", ref ok);
            ReportIfFolderExists(sb, ExpectedRoot + "/Assets",
                "zip を SmartMediaPlatform の中で展開すると出来ます。", ref ok);
            ReportIfFolderExists(sb, ExpectedRoot + "/SmartMediaPlatform",
                "フォルダを二重に入れると出来ます。", ref ok);

            if (ok && roots.Count == 1)
            {
                sb.AppendLine("✓ 入れ子のコピーもありません。導入は正常です。");
            }

            return ok;
        }

        // ───────── 中身 ─────────

        /// <summary>
        /// 目印ファイルの場所から、システムが入っているフォルダを全部挙げる。
        /// アセットの検索なので、<b>コンパイルが止まっていても動きます</b>。
        /// </summary>
        private static List<string> FindSystemRoots()
        {
            var roots = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("UdonSmartMediaPlayer"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("/" + MarkerFileName)) continue;

                int cut = path.LastIndexOf(MarkerSuffix, System.StringComparison.Ordinal);
                string root = cut > 0 ? path.Substring(0, cut) : path;

                if (!roots.Contains(root)) roots.Add(root);
            }

            roots.Sort();
            return roots;
        }

        private static void ReportIfFolderExists(
            StringBuilder sb, string folder, string howItHappens, ref bool ok)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return;

            ok = false;
            sb.AppendLine("✗ " + folder + " があります(" + howItHappens + ")");
            sb.AppendLine("   → Project ウィンドウで削除してください。");
        }

        private static string FolderOf(string root)
        {
            int slash = root.LastIndexOf('/');
            return slash >= 0 ? root.Substring(slash + 1) : root;
        }
    }
}
#endif
