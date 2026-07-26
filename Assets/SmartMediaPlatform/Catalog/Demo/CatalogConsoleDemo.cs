using System.Collections.Generic;
using System.Text;
using SmartMediaPlatform.Catalog.Data;
using UnityEngine;

namespace SmartMediaPlatform.Catalog.Demo
{
    /// <summary>
    /// Catalog API の動作を Unity の Console で確認するためのデモ。
    /// 空の GameObject にアタッチして Play するか、Inspector の右上メニュー
    /// (⋮)から "Run All Queries" を実行する(Play 不要)。
    /// Catalog 層はこのクラスを知らない — 依存は Demo → Catalog の一方向のみ。
    /// </summary>
    public sealed class CatalogConsoleDemo : MonoBehaviour
    {
        [Header("検索クエリ(Inspector から自由に変更可)")]
        [SerializeField] private string _idQuery = "music-003";
        [SerializeField] private string _titleQuery = "neon";
        [SerializeField] private string _artistQuery = "kohaku";
        [SerializeField] private string _tagQuery = "night";
        [SerializeField] private string _genreQuery = "Jazz";
        [SerializeField] private string _relatedQuery = "music-001";
        [SerializeField] private int _randomCount = 3;

        private void Start()
        {
            RunAllQueries();
        }

        [ContextMenu("Run All Queries")]
        public void RunAllQueries()
        {
            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());

            Debug.Log($"=== Smart Media Platform / Catalog Demo (items: {catalog.Count}) ===");

            LogResults($"GetAll()", catalog.GetAll());
            LogSingle($"FindById(\"{_idQuery}\")", catalog.FindById(_idQuery));
            LogResults($"SearchByTitle(\"{_titleQuery}\")", catalog.SearchByTitle(_titleQuery));
            LogResults($"SearchByArtist(\"{_artistQuery}\")", catalog.SearchByArtist(_artistQuery));
            LogResults($"SearchByTag(\"{_tagQuery}\")", catalog.SearchByTag(_tagQuery));
            LogResults($"SearchByGenre(\"{_genreQuery}\")", catalog.SearchByGenre(_genreQuery));
            LogResults($"FilterByType(Video)", catalog.FilterByType(MediaType.Video));
            LogSingle($"GetRandom()", catalog.GetRandom());
            LogResults($"GetRandom({_randomCount})", catalog.GetRandom(_randomCount));
            LogResults($"GetRelated(\"{_relatedQuery}\")", catalog.GetRelated(_relatedQuery));
        }

        private static void LogSingle(string label, MediaItem item)
        {
            Debug.Log($"{label} -> {(item != null ? item.ToString() : "(not found)")}");
        }

        private static void LogResults(string label, IReadOnlyList<MediaItem> items)
        {
            var sb = new StringBuilder();
            sb.Append(label).Append(" -> ").Append(items.Count).Append(" 件");
            for (int i = 0; i < items.Count; i++)
            {
                sb.AppendLine();
                sb.Append("    ").Append(items[i]);
            }
            Debug.Log(sb.ToString());
        }
    }
}
