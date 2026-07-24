using UdonSharp;
using UnityEngine;

namespace SmartMediaPlatform.Recommendation.Udon
{
    /// <summary>
    /// UdonRecommendationEngine の動作を VRChat ワールド内 Console で確認するデモ。
    /// Play すると seed を起点におすすめ順位とスコアを出力する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonRecommendationConsoleDemo : UdonSharpBehaviour
    {
        [Tooltip("参照するおすすめエンジン(Inspector で割り当て)")]
        public UdonRecommendationEngine Engine;

        [Tooltip("おすすめの起点にする ID")]
        public string SeedId = "music-001";

        [Tooltip("上位何件を表示するか")]
        public int Count = 5;

        void Start()
        {
            if (Engine == null)
            {
                Debug.LogError("[UdonRecommendationConsoleDemo] Engine が未設定です。");
                return;
            }

            Debug.Log("=== Udon Recommendation Demo (seed: " + SeedId + ") ===");

            int nextN = Engine.GetNextRecommendations(SeedId, Count);
            Dump("GetNextRecommendations(\"" + SeedId + "\", " + Count + ")", nextN);

            int relN = Engine.GetRelatedRecommendations(SeedId, Count);
            Dump("GetRelatedRecommendations(\"" + SeedId + "\", " + Count + ")", relN);
        }

        // エンジンのバッファは呼び出しごとに上書きされるため、Dump は直後に読む。
        private void Dump(string label, int n)
        {
            Debug.Log(label + " -> " + n + " 件");
            for (int i = 0; i < n; i++)
            {
                int idx = Engine.GetResultIndex(i);
                float score = Engine.GetResultScore(i);
                Debug.Log("  #" + (i + 1) + "  " + Engine.GetResultId(i)
                          + "  score=" + score.ToString("0.00")
                          + "  " + Engine.Catalog.GetTitle(idx) + " / " + Engine.Catalog.GetArtist(idx));
            }
        }
    }
}
