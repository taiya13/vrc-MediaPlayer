#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SmartMediaPlatform.CatalogBuilder.EditorTools
{
    /// <summary>
    /// <b>取り込み元の名簿。</b>Phase6-1。
    ///
    /// <see cref="ICatalogImporter"/> を実装したクラスを<b>読み込み済みの全アセンブリから探します</b>。
    /// つまり<b>登録作業が要りません</b> —
    /// Phase6-2 で <c>YouTubeCatalogImporter</c> を 1 つ書けば、
    /// 次のコンパイルで窓のタブに出ます。<b>窓のコードは 1 行も変わりません</b>。
    ///
    /// <b>なぜ属性やリストで登録しないのか</b><br/>
    /// 登録し忘れが必ず起きるからです。Phase5-3 で
    /// 「Prefab はできているのに中身が空」を踏んだのと同じ種類の事故で、
    /// <b>動かないのに理由が見えない</b>のがいちばん困ります。
    /// 探す側を自動にしておけば、実装した時点で必ず出ます。
    /// </summary>
    public static class CatalogImporterRegistry
    {
        private static ICatalogImporter[] _cache;

        /// <summary>見つかった取り込み元。1 度だけ探して覚えます。</summary>
        public static ICatalogImporter[] All()
        {
            if (_cache != null) return _cache;

            var found = new List<ICatalogImporter>();
            Type target = typeof(ICatalogImporter);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }

                if (types == null) continue;

                foreach (var type in types)
                {
                    if (type == null) continue;
                    if (type.IsAbstract || type.IsInterface) continue;
                    if (!target.IsAssignableFrom(type)) continue;

                    // 引数なしで作れるものだけ。設定が要るなら実装側が既定値を持つこと。
                    if (type.GetConstructor(Type.EmptyTypes) == null) continue;

                    try
                    {
                        found.Add((ICatalogImporter)Activator.CreateInstance(type));
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[CatalogImporterRegistry] " + type.Name
                                         + " を作れませんでした: " + e.Message);
                    }
                }
            }

            found.Sort(CompareByName);
            _cache = found.ToArray();
            return _cache;
        }

        /// <summary>探し直す(実装を足した直後など)。</summary>
        public static void Refresh()
        {
            _cache = null;
        }

        private static int CompareByName(ICatalogImporter a, ICatalogImporter b)
        {
            string left = a != null && a.DisplayName != null ? a.DisplayName : "";
            string right = b != null && b.DisplayName != null ? b.DisplayName : "";
            return string.CompareOrdinal(left, right);
        }
    }
}
#endif
