using UdonSharp;
using UnityEngine;
using SmartMediaPlatform.Catalog.Udon;

namespace SmartMediaPlatform.Recommendation.Udon
{
    /// <summary>
    /// ルールベースおすすめエンジンの UdonSharp 版(AI 非使用)。
    /// 純粋 C# の <see cref="RecommendationEngine"/> を移植したもので、
    /// スコア式・既定重み・並び順(スコア降順→index 昇順)は完全に一致させている。
    ///
    /// 依存は <see cref="UdonMediaCatalog"/>(Catalog API)のみ。
    /// Player / Queue / UI / ネットワークを一切参照しない。
    ///
    /// Udon はカスタムクラス配列を返せないため、結果は内部バッファ(並列配列)に格納し、
    /// GetNextRecommendations / GetRelatedRecommendations は件数を返す。
    /// 呼び出し側は ResultCount / GetResultIndex(i) / GetResultScore(i) で読み取る。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonRecommendationEngine : UdonSharpBehaviour
    {
        [Tooltip("参照するカタログ(Inspector で割り当て)")]
        public UdonMediaCatalog Catalog;

        [Header("スコアリング重み(RecommendationRule.CreateDefault と一致)")]
        public float WeightRelated = 10.0f;
        public float WeightSameArtist = 5.0f;
        public float WeightSameGenre = 3.0f;
        public float WeightTagMatch = 1.0f;
        public float WeightRandom = 0.5f;

        // === 結果バッファ(並列配列)===
        private int[] _resultIndices;
        private float[] _resultScores;
        private int _resultCount;

        public int ResultCount { get { return _resultCount; } }
        public int GetResultIndex(int i) { return _resultIndices[i]; }
        public float GetResultScore(int i) { return _resultScores[i]; }
        public string GetResultId(int i) { return Catalog.GetId(_resultIndices[i]); }

        /// <summary>
        /// seed を起点に、カタログ全体から次のおすすめ上位 count 件をバッファに格納し、件数を返す。
        /// </summary>
        public int GetNextRecommendations(string seedId, int count)
        {
            int seed = Catalog != null ? Catalog.FindIndexById(seedId) : -1;
            if (seed < 0 || count <= 0)
            {
                _resultIndices = new int[0];
                _resultScores = new float[0];
                _resultCount = 0;
                return 0;
            }

            int n = Catalog.Count;
            int[] candidates = new int[n - 1];
            int c = 0;
            for (int i = 0; i < n; i++)
            {
                if (i == seed) continue;
                candidates[c] = i;
                c++;
            }
            return RankInto(seed, candidates, count);
        }

        /// <summary>
        /// seed の関連(RelatedIds)だけを対象にスコア付けし、上位 count 件をバッファに格納して件数を返す。
        /// </summary>
        public int GetRelatedRecommendations(string seedId, int count)
        {
            int seed = Catalog != null ? Catalog.FindIndexById(seedId) : -1;
            if (seed < 0 || count <= 0)
            {
                _resultIndices = new int[0];
                _resultScores = new float[0];
                _resultCount = 0;
                return 0;
            }

            int[] candidates = Catalog.GetRelatedIndices(seedId); // 解決済み・自己/欠損除外・宣言順
            return RankInto(seed, candidates, count);
        }

        private int RankInto(int seed, int[] candidates, int count)
        {
            int n = Catalog.Count;

            // seed の関連集合を bool 配列で持つ(Related ルール用)
            bool[] isRelated = new bool[n];
            int[] rel = Catalog.GetRelatedIndices(Catalog.GetId(seed));
            for (int i = 0; i < rel.Length; i++) isRelated[rel[i]] = true;

            // seed のアーティスト / ジャンル / タグ(小文字化)
            string seedArtist = Catalog.GetArtist(seed).ToLower();
            string seedGenre = Catalog.GetGenre(seed).ToLower();
            int seedTagCount = Catalog.GetTagCount(seed);
            string[] seedTags = new string[seedTagCount];
            for (int i = 0; i < seedTagCount; i++) seedTags[i] = Catalog.GetTag(seed, i).ToLower();

            int m = candidates.Length;
            float[] scores = new float[m];
            for (int i = 0; i < m; i++)
            {
                scores[i] = Score(candidates[i], isRelated, seedArtist, seedGenre, seedTags);
            }

            // 上位 take 件だけ部分選択ソート(スコア降順 → index 昇順)
            int take = count < m ? count : m;
            for (int k = 0; k < take; k++)
            {
                int best = k;
                for (int j = k + 1; j < m; j++)
                {
                    if (scores[j] > scores[best]
                        || (scores[j] == scores[best] && candidates[j] < candidates[best]))
                    {
                        best = j;
                    }
                }
                if (best != k)
                {
                    float ts = scores[k]; scores[k] = scores[best]; scores[best] = ts;
                    int ti = candidates[k]; candidates[k] = candidates[best]; candidates[best] = ti;
                }
            }

            _resultIndices = new int[take];
            _resultScores = new float[take];
            for (int i = 0; i < take; i++)
            {
                _resultIndices[i] = candidates[i];
                _resultScores[i] = scores[i];
            }
            _resultCount = take;
            return take;
        }

        private float Score(int cand, bool[] isRelated, string seedArtist, string seedGenre, string[] seedTags)
        {
            float score = 0f;

            if (WeightRelated > 0f && isRelated[cand]) score += WeightRelated;

            if (WeightSameArtist > 0f && seedArtist.Length > 0
                && Catalog.GetArtist(cand).ToLower() == seedArtist)
            {
                score += WeightSameArtist;
            }

            if (WeightSameGenre > 0f && seedGenre.Length > 0
                && Catalog.GetGenre(cand).ToLower() == seedGenre)
            {
                score += WeightSameGenre;
            }

            if (WeightTagMatch > 0f)
            {
                int shared = 0;
                int candTagCount = Catalog.GetTagCount(cand);
                for (int t = 0; t < candTagCount; t++)
                {
                    string ct = Catalog.GetTag(cand, t).ToLower();
                    for (int s = 0; s < seedTags.Length; s++)
                    {
                        if (seedTags[s] == ct) { shared++; break; }
                    }
                }
                score += WeightTagMatch * shared;
            }

            if (WeightRandom > 0f) score += Random.Range(0f, WeightRandom);

            return score;
        }
    }
}
