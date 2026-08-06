using UdonSharp;
using UnityEngine;
using SmartMediaPlatform.Catalog.Udon;

namespace SmartMediaPlatform.Recommendation.Udon
{
    /// <summary>
    /// ルールベースおすすめエンジンの UdonSharp 版(AI 非使用)。
    ///
    /// 依存は <see cref="UdonMediaCatalog"/>(Catalog API)のみ。
    /// <b>Player / Queue / UI / ネットワークのクラスは一切参照しません。</b>
    /// 再生予定を考慮する <see cref="GetRecommendations"/> も、Queue のクラスではなく
    /// <b>catalog index の配列</b>を引数で受け取るだけなので、この境界は保たれています。
    ///
    /// Udon はカスタムクラス配列を返せないため、結果は内部バッファ(並列配列)に格納し、
    /// 各メソッドは件数を返します。
    /// 呼び出し側は ResultCount / GetResultIndex(i) / GetResultScore(i) / GetResultReason(i) で読み取ります。
    ///
    /// ───────────────────────────────────────────────
    /// <b>Phase7-4:「おすすめ」を階層順にしました。</b>
    ///
    /// Phase7-3 までの <see cref="GetRelatedRecommendations"/> は、
    /// <c>Catalog Builder</c> が事前計算した「関連(RelatedIds)」だけを対象にしていました。
    /// 関連が無い曲では<b>おすすめが 0 件</b>になり、
    /// 利用者からは「関連動画という印象で、おすすめらしくない」と見えていました。
    ///
    /// <see cref="GetRecommendations"/> は<b>カタログ全体</b>から選び、
    /// 優先順位を「重みの合計」ではなく<b>階層</b>にしています —
    /// 同じアーティスト(チャンネル) → 同じジャンル → タグ一致 → それ以外(いまは
    /// ランダム性で代役。将来、再生回数などの「人気」データが増えたら、
    /// 対応する正典 <see cref="SmartMediaPlatform.Recommendation.UdonModel.RecommendationScoringModel"/>
    /// の一番下の階層だけを差し替えれば済みます)。
    ///
    /// <b>ここは正典の写しです。</b>スコアの式・階層の大きさ・並び順の決め方は
    /// <see cref="SmartMediaPlatform.Recommendation.UdonModel.RecommendationScoringModel"/>
    /// と 1 対 1 で一致させてあります。<b>計算のしかたを変えるときは必ず両方を直してください</b>
    /// (UdonSharp は他アセンブリの静的メソッドを安定して呼べないため、
    ///  ロジックはコピーしてあります。テストは正典側で行います)。
    ///
    /// <see cref="GetNextRecommendations"/> / <see cref="GetRelatedRecommendations"/> は
    /// Phase7-3 までの重み付き合計のまま<b>残してあります</b>
    /// (<see cref="UdonPlayerSession"/> の Queue 自動補充が使っており、
    ///  そちらの挙動を変えないため)。
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

        // ───────── おすすめの理由(Phase7-3)─────────

        /// <summary>理由なし(ただの候補)。</summary>
        public const int ReasonNone = 0;

        /// <summary>同じチャンネル / アーティスト。</summary>
        public const int ReasonSameArtist = 1;

        /// <summary>同じジャンル。</summary>
        public const int ReasonSameGenre = 2;

        /// <summary>似たタグが付いている。</summary>
        public const int ReasonSharedTag = 3;

        /// <summary>カタログ側で関連付けられている(Phase7-3 までの経路でのみ使う)。</summary>
        public const int ReasonRelated = 4;

        /// <summary>
        /// どれにも当てはまらない。「人気順」の代わりに、いまはここへ落ちます(Phase7-4)。
        /// <see cref="GetRecommendations"/> が返す理由はこれか、上の 3 つのどれかです。
        /// </summary>
        public const int ReasonPopular = 5;

        private int[] _resultReasons;

        public int ResultCount { get { return _resultCount; } }
        public int GetResultIndex(int i) { return _resultIndices[i]; }
        public float GetResultScore(int i) { return _resultScores[i]; }

        /// <summary>なぜこれを勧めたか(<c>Reason…</c> のどれか)。</summary>
        public int GetResultReason(int i)
        {
            if (_resultReasons == null || i < 0 || i >= _resultReasons.Length) return ReasonNone;
            return _resultReasons[i];
        }

        /// <summary>理由を人が読む言葉にする。</summary>
        public string DescribeReason(int reason)
        {
            if (reason == ReasonSameArtist) return "同じチャンネル";
            if (reason == ReasonSameGenre) return "同じジャンル";
            if (reason == ReasonSharedTag) return "似たタグ";
            if (reason == ReasonRelated) return "この曲と一緒によく聴かれます";
            if (reason == ReasonPopular) return "人気曲";
            return "おすすめ";
        }
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

        // ───────── 階層順のおすすめ(Phase7-4)─────────

        /// <summary>
        /// <b>カタログ全体から、階層順に選ぶ。</b>
        /// 優先順位は「同じアーティスト → 同じジャンル → タグ一致 → それ以外」。
        /// <b>いま鳴っている曲(seed)は候補に入りません</b>(カタログ全体から
        /// seed を除いて候補を作るため)。
        /// </summary>
        /// <param name="seedId">起点にする曲の ID(いま鳴っている曲)。</param>
        /// <param name="count">欲しい件数。</param>
        /// <param name="queueIndices">
        /// 再生予定にある catalog index の並び。<b>除外はせず、優先度だけ下げます</b>
        /// (再生予定にあるからおすすめに出ない、では「消えた」ように見えるため)。
        /// 無ければ null で構いません。
        /// </param>
        /// <param name="queueCount"><paramref name="queueIndices"/> のうち使う件数。</param>
        public int GetRecommendations(string seedId, int count, int[] queueIndices, int queueCount)
        {
            int seed = Catalog != null ? Catalog.FindIndexById(seedId) : -1;
            if (seed < 0 || count <= 0)
            {
                _resultIndices = new int[0];
                _resultScores = new float[0];
                _resultReasons = new int[0];
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

            return RankByTierInto(seed, candidates, count, queueIndices, queueCount);
        }

        /// <summary>
        /// 階層スコアリングの本体。
        /// <see cref="SmartMediaPlatform.Recommendation.UdonModel.RecommendationScoringModel"/> の写しです。
        /// <b>計算のしかたを変えるときは必ず両方を直してください。</b>
        /// </summary>
        private int RankByTierInto(
            int seed, int[] candidates, int count, int[] queueIndices, int queueCount)
        {
            string seedArtist = Catalog.GetArtist(seed).ToLower();
            string seedGenre = Catalog.GetGenre(seed).ToLower();
            int seedTagCount = Catalog.GetTagCount(seed);
            string[] seedTags = new string[seedTagCount];
            for (int i = 0; i < seedTagCount; i++) seedTags[i] = Catalog.GetTag(seed, i).ToLower();

            int m = candidates.Length;
            float[] scores = new float[m];
            int[] reasons = new int[m];

            for (int i = 0; i < m; i++)
            {
                int cand = candidates[i];

                bool sameArtist = seedArtist.Length > 0
                                  && Catalog.GetArtist(cand).ToLower() == seedArtist;
                bool sameGenre = seedGenre.Length > 0
                                 && Catalog.GetGenre(cand).ToLower() == seedGenre;

                int candTagCount = Catalog.GetTagCount(cand);
                int shared = 0;
                for (int t = 0; t < candTagCount; t++)
                {
                    string ct = Catalog.GetTag(cand, t).ToLower();
                    for (int s = 0; s < seedTags.Length; s++)
                    {
                        if (seedTags[s] == ct) { shared++; break; }
                    }
                }

                bool inQueue = IsInQueue(cand, queueIndices, queueCount);

                scores[i] = TierScoreOf(sameArtist, sameGenre, shared, inQueue, Random.Range(0f, 1f));
                reasons[i] = TierReasonOf(sameArtist, sameGenre, shared);
            }

            // 部分選択ソート(スコア降順 → index 昇順、正典 SelectTopInto と同じ)
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
                    int tr = reasons[k]; reasons[k] = reasons[best]; reasons[best] = tr;
                }
            }

            _resultIndices = new int[take];
            _resultScores = new float[take];
            _resultReasons = new int[take];
            for (int i = 0; i < take; i++)
            {
                _resultIndices[i] = candidates[i];
                _resultScores[i] = scores[i];
                _resultReasons[i] = reasons[i];
            }
            _resultCount = take;
            return take;
        }

        private static bool IsInQueue(int catalogIndex, int[] queueIndices, int queueCount)
        {
            if (queueIndices == null) return false;

            int limit = queueCount < queueIndices.Length ? queueCount : queueIndices.Length;
            for (int i = 0; i < limit; i++)
            {
                if (queueIndices[i] == catalogIndex) return true;
            }
            return false;
        }

        // ── 正典 RecommendationScoringModel.ScoreOf / ReasonOf の写し(定数含む)。
        //    UdonSharp は他アセンブリの静的メソッドを安定して呼べないため、
        //    ロジックをコピーしてあります。テストは正典側で行います。

        private const float TierArtist = 1000000f;
        private const float TierGenre = 10000f;
        private const float TierTagUnit = 100f;
        private const float MaxTagBonus = 5000f;
        private const float MaxRandomNoise = 50f;
        private const float TierQueuePenalty = 2000000f;

        private static float TierScoreOf(
            bool sameArtist, bool sameGenre, int sharedTagCount, bool inQueue, float randomNoise01)
        {
            float score = 0f;

            if (sameArtist) score += TierArtist;
            if (sameGenre) score += TierGenre;

            float tagBonus = sharedTagCount * TierTagUnit;
            if (tagBonus > MaxTagBonus) tagBonus = MaxTagBonus;
            score += tagBonus;

            float noise = randomNoise01;
            if (noise < 0f) noise = 0f;
            if (noise > 1f) noise = 1f;
            score += noise * MaxRandomNoise;

            if (inQueue) score -= TierQueuePenalty;

            return score;
        }

        private static int TierReasonOf(bool sameArtist, bool sameGenre, int sharedTagCount)
        {
            if (sameArtist) return ReasonSameArtist;
            if (sameGenre) return ReasonSameGenre;
            if (sharedTagCount > 0) return ReasonSharedTag;
            return ReasonPopular;
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
            _resultReasons = new int[take];
            for (int i = 0; i < take; i++)
            {
                _resultIndices[i] = candidates[i];
                _resultScores[i] = scores[i];
                _resultReasons[i] = ReasonFor(
                    candidates[i], isRelated, seedArtist, seedGenre, seedTags);
            }
            _resultCount = take;
            return take;
        }

        /// <summary>
        /// <b>なぜこれを勧めるのか。</b>Phase7-3。
        ///
        /// <b>理由が言えないおすすめは押されません。</b>
        /// 「あなたへのおすすめ」とだけ書かれていても、
        /// <b>なぜそれなのかが分からないと信用されない</b>ためです。
        /// 出すのは<b>いちばん強い理由 1 つだけ</b>にします。
        /// 3 つ並べると、どれが効いたのか分からなくなります。
        /// </summary>
        private int ReasonFor(
            int cand, bool[] isRelated, string seedArtist, string seedGenre, string[] seedTags)
        {
            if (WeightRelated > 0f && isRelated[cand]) return ReasonRelated;

            if (WeightSameArtist > 0f && seedArtist.Length > 0
                && Catalog.GetArtist(cand).ToLower() == seedArtist)
            {
                return ReasonSameArtist;
            }

            if (WeightSameGenre > 0f && seedGenre.Length > 0
                && Catalog.GetGenre(cand).ToLower() == seedGenre)
            {
                return ReasonSameGenre;
            }

            if (WeightTagMatch > 0f)
            {
                int candTagCount = Catalog.GetTagCount(cand);
                for (int t = 0; t < candTagCount; t++)
                {
                    string ct = Catalog.GetTag(cand, t).ToLower();
                    for (int s = 0; s < seedTags.Length; s++)
                    {
                        if (seedTags[s] == ct) return ReasonSharedTag;
                    }
                }
            }

            return ReasonNone;
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
