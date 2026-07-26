using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using UnityEngine;

namespace SmartMediaPlatform.Recommendation.Demo
{
    /// <summary>
    /// Recommendation Engine(純粋 C# 版)を Unity の Console で確認するデモ。
    /// VRChat SDK 不要。空の GameObject にアタッチして Play するか、
    /// Inspector の ⋮ メニュー &gt; "Run Recommendations" で実行(Play 不要)。
    /// </summary>
    public sealed class RecommendationConsoleDemo : MonoBehaviour
    {
        [SerializeField] private string _seedId = "music-001";
        [SerializeField] private int _count = 5;

        [Header("スコアリング重み")]
        [SerializeField] private double _weightRelated = 10.0;
        [SerializeField] private double _weightSameArtist = 5.0;
        [SerializeField] private double _weightSameGenre = 3.0;
        [SerializeField] private double _weightTagMatch = 1.0;
        [SerializeField] private double _weightRandom = 0.5;

        private void Start()
        {
            RunRecommendations();
        }

        [ContextMenu("Run Recommendations")]
        public void RunRecommendations()
        {
            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());
            var rules = new[]
            {
                RecommendationRule.Related(_weightRelated),
                RecommendationRule.SameArtist(_weightSameArtist),
                RecommendationRule.SameGenre(_weightSameGenre),
                RecommendationRule.TagMatch(_weightTagMatch),
                RecommendationRule.Random(_weightRandom),
            };
            // System.Random を明示(UnityEngine.Random との衝突を避ける)。
            var engine = new RecommendationEngine(catalog, rules, new System.Random());

            var seed = catalog.FindById(_seedId);
            Debug.Log($"=== Recommendation Demo (seed: {(seed != null ? seed.ToString() : _seedId)}) ===");

            Log($"GetNextRecommendations(\"{_seedId}\", {_count})", engine.GetNextRecommendations(_seedId, _count));
            Log($"GetRelatedRecommendations(\"{_seedId}\", {_count})", engine.GetRelatedRecommendations(_seedId, _count));
        }

        private static void Log(string label, RecommendationResult[] results)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(label).Append(" -> ").Append(results.Length).Append(" 件");
            for (int i = 0; i < results.Length; i++)
            {
                sb.AppendLine();
                sb.Append($"  #{i + 1}  ").Append(results[i]);
            }
            Debug.Log(sb.ToString());
        }
    }
}
