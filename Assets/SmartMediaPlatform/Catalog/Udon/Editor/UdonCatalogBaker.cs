#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Catalog.Parallel;
using SmartMediaPlatform.Catalog.Udon;

namespace SmartMediaPlatform.Catalog.UdonEditor
{
    /// <summary>
    /// Phase1-1 のカタログ(オブジェクトモデル)を、Phase1-2 の UdonMediaCatalog
    /// (並列配列)へ「焼き込む」エディタツール。
    ///
    /// これが提案書の Catalog Builder に相当する編集時変換の最小版:
    ///   MediaItem[]  --CatalogFlattener-->  並列配列  --このツール-->  UdonMediaCatalog
    ///
    /// VRCUrl は実行時生成不可のため、URL 文字列からの VRCUrl 生成は「編集時のここ」で行う。
    /// 検索ロジックには一切触れない — データを詰めるだけ。
    /// </summary>
    public static class UdonCatalogBaker
    {
        // Phase6-5: 「ダミーを焼く」メニューは外した。
        // 焼き込みは Catalog Builder の「③ VRCUrl へ焼く」1 か所に集約されたので、
        // 同じことをする入口が 2 つあると、どちらを押したか分からなくなる。
        // Bake(target, items) は CatalogUrlTableBridge が名前で呼ぶため残す。

        /// <summary>
        /// 任意のアイテム列を UdonMediaCatalog へ焼き込む。
        /// Phase2 で本番ソース(JSON 等)へ差し替える場合もこの入口を使えばよい。
        /// </summary>
        public static void Bake(UdonMediaCatalog target, System.Collections.Generic.IReadOnlyList<MediaItem> items)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (items == null) throw new ArgumentNullException(nameof(items));

            ParallelCatalogData d = CatalogFlattener.Flatten(items);

            Undo.RecordObject(target, "Bake Catalog");

            target.Ids = d.Ids;
            target.Titles = d.Titles;
            target.Artists = d.Artists;
            target.Genres = d.Genres;
            target.Types = d.Types;
            target.Durations = d.Durations;
            target.TagValues = d.TagValues;
            target.TagOffsets = d.TagOffsets;
            target.RelatedIds = d.RelatedIds;
            target.RelatedOffsets = d.RelatedOffsets;

            // URL 文字列 -> VRCUrl(編集時のみ可能)
            var urls = new VRCUrl[d.Urls.Length];
            for (int i = 0; i < urls.Length; i++)
            {
                urls[i] = string.IsNullOrEmpty(d.Urls[i]) ? VRCUrl.Empty : new VRCUrl(d.Urls[i]);
            }
            target.Urls = urls;

            EditorUtility.SetDirty(target);
            SyncUdonSharpProxy(target);

            if (!Application.isPlaying)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
            }
        }

        /// <summary>
        /// UdonSharp はプロキシ(MonoBehaviour)の値を Udon プログラムへ同期する必要がある。
        /// バージョン差で API 名が変わっても壊れないよう、UdonSharpEditorUtility.CopyProxyToUdon を
        /// リフレクションで呼ぶ(見つからなければスキップ。多くのバージョンでは保存/ビルド時に自動同期される)。
        /// </summary>
        private static void SyncUdonSharpProxy(UdonMediaCatalog target)
        {
            try
            {
                Type util = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    util = asm.GetType("UdonSharpEditor.UdonSharpEditorUtility");
                    if (util != null) break;
                }
                if (util == null) return;

                MethodInfo copy = util.GetMethod(
                    "CopyProxyToUdon",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(UdonSharp.UdonSharpBehaviour) },
                    null);
                copy?.Invoke(null, new object[] { target });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UdonCatalogBaker] UdonSharp プロキシ同期をスキップしました: " + e.Message);
            }
        }
    }
}
#endif
