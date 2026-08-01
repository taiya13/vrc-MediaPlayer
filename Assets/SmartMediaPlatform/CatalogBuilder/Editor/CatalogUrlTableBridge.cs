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
