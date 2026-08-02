#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Assets;
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.CatalogBuilder.EditorTools
{
    /// <summary>
    /// <b>Catalog.asset を、ワールドで再生できる形(VRCUrl)へ焼く。</b>Phase6-3。
    ///
    /// <b>なぜ焼き直しが要るのか</b><br/>
    /// <c>VRCUrl</c> は<b>実行時に作れません</b>(Phase1-2 からの前提)。
    /// アセットに文字列で持っている URL を、編集時に <c>UdonMediaCatalog</c> の
    /// <c>VRCUrl</c> 配列へ写しておく必要があります。この写す作業が「焼く」です。
    ///
    /// <b>VRChat SDK に直接依存しません。</b>
    /// <c>UdonMediaCatalog</c> も <c>UdonCatalogBaker</c> も<b>名前で探します</b>
    /// (Phase3 の <c>UdonSharpSceneUtility</c> と同じ方針)。
    /// おかげで SDK が入っていないプロジェクトでも Catalog Builder は開けます。
    /// </summary>
    public static class CatalogUrlTableBridge
    {
        private const string CatalogTypeName = "SmartMediaPlatform.Catalog.Udon.UdonMediaCatalog";
        private const string BakerTypeName = "SmartMediaPlatform.Catalog.UdonEditor.UdonCatalogBaker";

        /// <summary>SDK と Udon 側のクラスがそろっているか。</summary>
        public static bool IsAvailable
        {
            get { return FindType(CatalogTypeName) != null && FindType(BakerTypeName) != null; }
        }

        public static string UnavailableReason
        {
            get
            {
                if (FindType(CatalogTypeName) == null)
                    return "UdonMediaCatalog が見つかりません。VRChat SDK(Worlds)を入れてください。";
                if (FindType(BakerTypeName) == null)
                    return "UdonCatalogBaker が見つかりません。";
                return "";
            }
        }

        /// <summary>焼いた結果。</summary>
        public sealed class BakeReport
        {
            public bool Ok;
            public int Targets;
            public int Items;
            public string Message = "";
        }

        /// <summary>
        /// シーンにある <c>UdonMediaCatalog</c> を全部探して、
        /// <paramref name="asset"/> の中身を焼き込む。
        ///
        /// <b>シーンにあるものを対象にします。</b>Prefab のアセットではなく、
        /// いま置いてある実物を書き換えないと、ワールドに反映されないためです。
        /// </summary>
        public static BakeReport BakeIntoScene(MediaCatalogAsset asset)
        {
            var report = new BakeReport();

            if (asset == null)
            {
                report.Message = "カタログが選ばれていません。";
                return report;
            }

            if (!IsAvailable)
            {
                report.Message = UnavailableReason;
                return report;
            }

            Type catalogType = FindType(CatalogTypeName);
            MethodInfo bake = FindBakeMethod();

            if (bake == null)
            {
                report.Message = "UdonCatalogBaker.Bake が見つかりません。";
                return report;
            }

            IReadOnlyList<MediaItem> items = asset.LoadItems();
            if (items == null || items.Count == 0)
            {
                report.Message = "カタログが空です。先に保存してください。";
                return report;
            }

            UnityEngine.Object[] found = UnityEngine.Object.FindObjectsOfType(catalogType);
            if (found == null || found.Length == 0)
            {
                report.Message =
                    "シーンに UdonMediaCatalog がありません。\n"
                    + "SmartMediaPlayer.prefab を Hierarchy へ置いてから実行してください。";
                return report;
            }

            int baked = 0;
            for (int i = 0; i < found.Length; i++)
            {
                var component = found[i] as Component;
                if (component == null) continue;

                try
                {
                    bake.Invoke(null, new object[] { component, items });
                    EditorUtility.SetDirty(component);
                    baked++;
                }
                catch (Exception e)
                {
                    Exception inner = e.InnerException != null ? e.InnerException : e;
                    report.Message = "焼き込みに失敗しました: " + inner.Message;
                    return report;
                }
            }

            AssetDatabase.SaveAssets();

            report.Ok = baked > 0;
            report.Targets = baked;
            report.Items = items.Count;
            report.Message = baked + " 個の Catalog へ " + items.Count + " 件を焼きました。\n"
                             + "シーンを保存してから Build & Test してください。";
            return report;
        }

        // ───────── 焼き忘れの検出(Phase6-5)─────────

        /// <summary>ずれの調べ結果。</summary>
        public sealed class SyncReport
        {
            /// <summary>調べられたか(SDK / カタログ / シーンがそろっているか)。</summary>
            public bool Checked;

            /// <summary>ずれていないか。</summary>
            public bool InSync;

            public int Targets;
            public int AssetCount;
            public int SceneCount;

            /// <summary>アセットにあってシーンに無いもの(焼き忘れ)。</summary>
            public string[] MissingInScene = new string[0];

            /// <summary>シーンにあってアセットに無いもの(消したのに焼いていない)。</summary>
            public string[] StaleInScene = new string[0];

            /// <summary>並びだけが違うか。</summary>
            public bool OrderDiffers;

            public string Message = "";
        }

        /// <summary>
        /// <b>Catalog.asset とシーンの VRCUrl がそろっているかを調べる。</b>
        ///
        /// <b>なぜ要るのか</b><br/>
        /// 保存しただけではワールドに反映されません。焼き込みを忘れると
        /// <b>「直したはずなのに現地では古いまま」</b>になり、
        /// しかも<b>何も警告が出ません</b>。Phase6-3 からの積み残しでした。
        ///
        /// <b>ID の並びで見ます。</b>件数だけだと
        /// 「1 件消して 1 件足した」を見逃します。
        /// </summary>
        public static SyncReport CompareWithScene(MediaCatalogAsset asset)
        {
            var report = new SyncReport();

            if (asset == null || !IsAvailable) return report;

            Type catalogType = FindType(CatalogTypeName);
            UnityEngine.Object[] found = UnityEngine.Object.FindObjectsOfType(catalogType);

            if (found == null || found.Length == 0) return report;

            IReadOnlyList<MediaItem> items = asset.LoadItems();
            var assetIds = new List<string>();
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] != null) assetIds.Add(items[i].Id);
                }
            }

            report.Checked = true;
            report.Targets = found.Length;
            report.AssetCount = assetIds.Count;

            var missing = new List<string>();
            var stale = new List<string>();
            bool orderDiffers = false;
            int sceneCount = 0;

            for (int i = 0; i < found.Length; i++)
            {
                string[] sceneIds = ReadIds(found[i]);
                if (sceneIds == null) continue;

                if (sceneIds.Length > sceneCount) sceneCount = sceneIds.Length;

                for (int a = 0; a < assetIds.Count; a++)
                {
                    if (Contains(sceneIds, assetIds[a])) continue;
                    if (!missing.Contains(assetIds[a])) missing.Add(assetIds[a]);
                }

                for (int s = 0; s < sceneIds.Length; s++)
                {
                    if (assetIds.Contains(sceneIds[s])) continue;
                    if (!stale.Contains(sceneIds[s])) stale.Add(sceneIds[s]);
                }

                if (SameOrder(assetIds, sceneIds)) continue;
                orderDiffers = true;
            }

            report.SceneCount = sceneCount;
            report.MissingInScene = missing.ToArray();
            report.StaleInScene = stale.ToArray();

            // 並びの違いは、中身が同じときだけ「並びだけ」と言える。
            report.OrderDiffers = orderDiffers && missing.Count == 0 && stale.Count == 0;
            report.InSync = missing.Count == 0 && stale.Count == 0 && !orderDiffers;

            report.Message = Describe(report);
            return report;
        }

        private static string Describe(SyncReport report)
        {
            if (report.InSync) return "シーンの VRCUrl はカタログと一致しています。";

            var sb = new System.Text.StringBuilder();
            sb.Append("シーンの VRCUrl がカタログと違います。「③ VRCUrl へ焼く」を押してください。\n");

            if (report.MissingInScene.Length > 0)
            {
                sb.Append("  ワールドにまだ無い: ").Append(report.MissingInScene.Length).Append(" 件");
                sb.Append(" (").Append(Preview(report.MissingInScene)).Append(")\n");
            }

            if (report.StaleInScene.Length > 0)
            {
                sb.Append("  消したのに残っている: ").Append(report.StaleInScene.Length).Append(" 件");
                sb.Append(" (").Append(Preview(report.StaleInScene)).Append(")\n");
            }

            if (report.OrderDiffers) sb.Append("  中身は同じですが並びが違います。\n");

            return sb.ToString().TrimEnd();
        }

        private static string Preview(string[] ids)
        {
            int take = ids.Length < 3 ? ids.Length : 3;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < take; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(ids[i]);
            }
            if (ids.Length > take) sb.Append(" ほか");

            return sb.ToString();
        }

        /// <summary>焼き込まれている ID の並びを読み返す。読めなければ null。</summary>
        private static string[] ReadIds(UnityEngine.Object catalog)
        {
            if (catalog == null) return null;

            FieldInfo field = catalog.GetType().GetField(
                "Ids", BindingFlags.Public | BindingFlags.Instance);

            if (field == null) return null;

            return field.GetValue(catalog) as string[];
        }

        private static bool Contains(string[] values, string wanted)
        {
            if (values == null || wanted == null) return false;

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == wanted) return true;
            }
            return false;
        }

        private static bool SameOrder(List<string> assetIds, string[] sceneIds)
        {
            if (sceneIds == null || assetIds.Count != sceneIds.Length) return false;

            for (int i = 0; i < sceneIds.Length; i++)
            {
                if (assetIds[i] != sceneIds[i]) return false;
            }
            return true;
        }

        /// <summary>シーンにある <c>UdonMediaCatalog</c> の数。ボタンの出し分けに使う。</summary>
        public static int CountInScene()
        {
            Type catalogType = FindType(CatalogTypeName);
            if (catalogType == null) return 0;

            UnityEngine.Object[] found = UnityEngine.Object.FindObjectsOfType(catalogType);
            return found != null ? found.Length : 0;
        }

        // ───────── 内部 ─────────

        private static MethodInfo FindBakeMethod()
        {
            Type baker = FindType(BakerTypeName);
            if (baker == null) return null;

            foreach (var method in baker.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != "Bake") continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 2) continue;

                return method;
            }
            return null;
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = assembly.GetType(fullName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
#endif
