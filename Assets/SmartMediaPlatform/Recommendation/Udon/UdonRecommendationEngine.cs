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

        [Tooltip("その人の好み(お気に入り・履歴・再生回数)。空でも動く")]
        public SmartMediaPlatform.World.Udon.UdonUserProfile Profile;

        [Header("発見(Phase7-8)")]
        [Tooltip("同じアーティストから続けて取ってよい曲数。"
                 + "ここが 1 か 2 でないと「同じ人しか出ない」に戻る")]
        [Range(1, 8)]
        public int MaxPerArtist = 2;

        [Tooltip("直近この曲数までを「さっき聴いた」として下げる")]
        [Range(0, 32)]
        public int RecentDepth = 10;

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

        /// <summary>お気に入りに入れているアーティストの曲(Phase7-8)。</summary>
        public const int ReasonFavoriteArtist = 6;

        /// <summary>しばらく聴いていない曲(Phase7-8)。</summary>
        public const int ReasonFresh = 7;

        /// <summary>お気に入りに多いジャンルの曲(Phase7-8)。</summary>
        public const int ReasonFavoriteGenre = 8;

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
            if (reason == ReasonSameArtist) return "このアーティスト";
            if (reason == ReasonFavoriteArtist) return "お気に入りに近い";
            if (reason == ReasonSameGenre) return "同じジャンル";
            if (reason == ReasonSharedTag) return "似た雰囲気";
            if (reason == ReasonFavoriteGenre) return "好きなジャンル";
            if (reason == ReasonFresh) return "まだ聴いていない";
            if (reason == ReasonRelated) return "この曲と一緒によく聴かれます";
            if (reason == ReasonPopular) return "よく聴かれています";
            return "おすすめ";
        }

        /// <summary>「発見」の見出しに出す言葉(理由ごとのまとまりの名前)。</summary>
        public string DescribeGroup(int reason)
        {
            if (reason == ReasonSameArtist) return "このアーティストが好きなら";
            if (reason == ReasonFavoriteArtist) return "お気に入りに近い";
            if (reason == ReasonSameGenre) return "同じジャンル";
            if (reason == ReasonSharedTag) return "似た雰囲気";
            if (reason == ReasonFavoriteGenre) return "好きなジャンルから";
            if (reason == ReasonFresh) return "まだ聴いていないもの";
            if (reason == ReasonPopular) return "最近よく聴かれています";
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

            return RankByDiscoveryInto(seed, candidates, count, queueIndices, queueCount);
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


        // ═════════ 発見(Phase7-8)═════════
        //
        // 正典 DiscoveryScoringModel の写しです。
        // <b>計算のしかたを変えるときは必ず両方を直してください。</b>
        // 点数の意味と、なぜこの比率なのかは正典側に書いてあります。

        private const float WSameArtist = 120f;
        private const float WSameGenre = 90f;
        private const float WTagUnit = 35f;
        private const float WMaxTagBonus = 105f;
        private const float WFavoriteArtist = 70f;
        private const float WFavoriteGenre = 45f;
        private const float WFavorite = 60f;
        private const float WMaxPopularity = 40f;
        private const float WMaxRecentPenalty = 220f;
        private const float WQueuePenalty = 1000f;
        private const float WCurrentPenalty = 100000f;
        private const float WMaxRandomNoise = 25f;

        /// <summary>理由ごとのまとまりの最大数。</summary>
        public const int MaxGroups = 6;

        /// <summary>1 つのまとまりに入れる最大数。</summary>
        public const int MaxPerGroup = 8;

        private int[] _groupReasons;
        private int[] _groupSizes;
        private int[] _groupItems;
        private int _groupTotal;

        /// <summary>いくつのまとまりができたか(Phase7-8)。</summary>
        public int GroupCount { get { return _groupTotal; } }

        /// <summary>そのまとまりの理由(<c>Reason…</c> のどれか)。</summary>
        public int GetGroupReason(int group)
        {
            if (_groupReasons == null || group < 0 || group >= _groupTotal) return ReasonNone;
            return _groupReasons[group];
        }

        /// <summary>そのまとまりの見出し。</summary>
        public string GetGroupTitle(int group)
        {
            return DescribeGroup(GetGroupReason(group));
        }

        /// <summary>そのまとまりに何曲入っているか。</summary>
        public int GetGroupItemCount(int group)
        {
            if (_groupSizes == null || group < 0 || group >= _groupTotal) return 0;
            return _groupSizes[group];
        }

        /// <summary>そのまとまりの <paramref name="position"/> 番目の曲。</summary>
        public int GetGroupItem(int group, int position)
        {
            if (_groupItems == null) return -1;
            if (group < 0 || group >= _groupTotal) return -1;
            if (position < 0 || position >= _groupSizes[group]) return -1;

            return _groupItems[group * MaxPerGroup + position];
        }

        /// <summary>
        /// <b>点数を付けて、理由ごとに仕分けて、多様さを保って並べる。</b>Phase7-8。
        ///
        /// やることは 3 つです。
        /// <list type="number">
        /// <item>候補ぜんぶに点数と理由を付ける</item>
        /// <item>理由ごとのまとまり(「このアーティストが好きなら」など)を作る</item>
        /// <item>1 本の並びを作る。<b>ただし 1 組から取りすぎない</b></item>
        /// </list>
        ///
        /// <b>3 番目が「同じ人しか出ない」の答え</b>です。
        /// 点数だけで並べると、同アーティストが 10 曲あれば上位 10 件が
        /// 全部その人になります。点数は正しいのに一覧としては失敗なので、
        /// 選ぶ側で <see cref="MaxPerArtist"/> の枠を掛けます。
        /// </summary>
        private int RankByDiscoveryInto(
            int seed, int[] candidates, int count, int[] queueIndices, int queueCount)
        {
            string seedArtist = Lower(Catalog.GetArtist(seed));
            string seedGenre = Lower(Catalog.GetGenre(seed));

            int seedTagCount = Catalog.GetTagCount(seed);
            string[] seedTags = new string[seedTagCount];
            for (int i = 0; i < seedTagCount; i++) seedTags[i] = Lower(Catalog.GetTag(seed, i));

            int maxPlays = Profile != null ? Profile.MaxPlayCount() : 0;

            // 取り込み時に焼き込んだ再生数(千回単位)。Phase7-9。
            int maxViewsK = Catalog.MaxViewCountK();

            int m = candidates.Length;
            float[] scores = new float[m];
            int[] reasons = new int[m];
            int[] artistKeys = new int[m];

            for (int i = 0; i < m; i++)
            {
                int cand = candidates[i];

                string candArtist = Lower(Catalog.GetArtist(cand));
                string candGenre = Lower(Catalog.GetGenre(cand));

                bool sameArtist = seedArtist.Length > 0 && candArtist == seedArtist;
                bool sameGenre = seedGenre.Length > 0 && candGenre == seedGenre;

                int shared = SharedTagCount(seedTags, cand);

                bool favoriteArtist = false;
                bool favoriteGenre = false;
                bool isFavorite = false;
                int recentRank = -1;
                float popularity = 0f;

                if (Profile != null)
                {
                    // 種と同じアーティストなら、お気に入り由来の加点は付けません。
                    // 同じ手掛かりを二重に数えると、そのアーティストだけが
                    // どんどん強くなって、また「同じ人しか出ない」に戻ります。
                    favoriteArtist = !sameArtist && Profile.IsFavoriteArtist(candArtist);
                    favoriteGenre = !sameGenre && Profile.IsFavoriteGenre(candGenre);

                    isFavorite = Profile.IsFavorite(cand);
                    recentRank = Profile.RecentRankOf(cand);

                    if (maxPlays > 0)
                    {
                        popularity = (float)Profile.PlayCountOf(cand) / (float)maxPlays;
                    }
                }

                // ── 世の中の人気(取り込み時の再生数)を主にする。
                //    このワールドでの再生回数は、まだ数が少ないうちは
                //    <b>たまたま最初に押された曲</b>を指しているだけなので、
                //    足しはしても主役にはしません。
                if (maxViewsK > 0)
                {
                    float world = PopularityOf(Catalog.GetViewCountK(cand), maxViewsK);
                    popularity = world * 0.7f + popularity * 0.3f;
                }

                bool inQueue = IsInQueue(cand, queueIndices, queueCount);

                scores[i] = DiscoveryScoreOf(
                    sameArtist, sameGenre, shared,
                    favoriteArtist, favoriteGenre, isFavorite,
                    popularity, recentRank, inQueue, false, Random.Range(0f, 1f));

                reasons[i] = DiscoveryReasonOf(
                    sameArtist, sameGenre, shared,
                    favoriteArtist, favoriteGenre, recentRank < 0);

                artistKeys[i] = KeyOf(candArtist);
            }

            BuildGroups(candidates, scores, reasons);

            int take = count < m ? count : m;
            if (take < 0) take = 0;

            int[] result = new int[take];
            int[] work = new int[m];
            float[] workScores = new float[m];
            int[] workKeys = new int[m];
            int[] workReasons = new int[m];

            for (int i = 0; i < m; i++)
            {
                work[i] = candidates[i];
                workScores[i] = scores[i];
                workKeys[i] = artistKeys[i];
                workReasons[i] = reasons[i];
            }

            int written = SelectDiverse(work, workScores, workKeys, workReasons, take, result);

            _resultIndices = new int[written];
            _resultScores = new float[written];
            _resultReasons = new int[written];

            for (int i = 0; i < written; i++)
            {
                _resultIndices[i] = work[i];
                _resultScores[i] = workScores[i];
                _resultReasons[i] = workReasons[i];
            }

            _resultCount = written;
            return written;
        }

        /// <summary>
        /// <b>理由ごとのまとまりを作る。</b>
        ///
        /// UI はいま 1 本の並びしか出しませんが、
        /// <b>「発見」画面をあとから足せるように</b>ここで作っておきます。
        /// まとまりの中でも、同じアーティストばかりにならないよう枠を掛けます。
        /// </summary>
        private void BuildGroups(int[] candidates, float[] scores, int[] reasons)
        {
            if (_groupReasons == null)
            {
                _groupReasons = new int[MaxGroups];
                _groupSizes = new int[MaxGroups];
                _groupItems = new int[MaxGroups * MaxPerGroup];
            }

            int[] wanted = new int[MaxGroups];
            wanted[0] = ReasonSameArtist;
            wanted[1] = ReasonFavoriteArtist;
            wanted[2] = ReasonSameGenre;
            wanted[3] = ReasonSharedTag;
            wanted[4] = ReasonFresh;
            wanted[5] = ReasonPopular;

            _groupTotal = 0;

            for (int g = 0; g < MaxGroups; g++)
            {
                int reason = wanted[g];

                // この理由の候補だけ集める。
                int n = 0;
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (reasons[i] == reason) n++;
                }
                if (n == 0) continue;

                int[] pick = new int[n];
                float[] pickScores = new float[n];
                int[] pickKeys = new int[n];
                int[] pickReasons = new int[n];

                int c = 0;
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (reasons[i] != reason) continue;

                    pick[c] = candidates[i];
                    pickScores[c] = scores[i];
                    pickKeys[c] = KeyOf(Lower(Catalog.GetArtist(candidates[i])));
                    pickReasons[c] = reason;
                    c++;
                }

                int take = n < MaxPerGroup ? n : MaxPerGroup;
                int[] into = new int[take];
                int written = SelectDiverse(pick, pickScores, pickKeys, pickReasons, take, into);

                if (written <= 0) continue;

                _groupReasons[_groupTotal] = reason;
                _groupSizes[_groupTotal] = written;
                for (int i = 0; i < written; i++)
                {
                    _groupItems[_groupTotal * MaxPerGroup + i] = into[i];
                }
                _groupTotal++;
            }
        }

        /// <summary>
        /// 点数順に選ぶ。ただし 1 組(アーティスト)から
        /// <see cref="MaxPerArtist"/> 曲までしか取りません。
        /// 正典 <c>DiscoveryScoringModel.SelectDiverseInto</c> の写しです。
        /// </summary>
        private int SelectDiverse(
            int[] indices, float[] scores, int[] keys, int[] reasons, int take, int[] result)
        {
            int m = indices.Length;
            int actualTake = take < m ? take : m;
            if (actualTake < 0) actualTake = 0;

            int[] takenKeys = new int[actualTake > 0 ? actualTake : 1];
            int[] takenCounts = new int[actualTake > 0 ? actualTake : 1];
            int takenKinds = 0;

            int written = 0;

            for (int k = 0; k < actualTake; k++)
            {
                int best = FindBest(
                    indices, scores, keys, k, m, takenKeys, takenCounts, takenKinds, true);

                if (best < 0)
                {
                    best = FindBest(
                        indices, scores, keys, k, m, takenKeys, takenCounts, takenKinds, false);
                }
                if (best < 0) break;

                if (best != k)
                {
                    int ti = indices[k]; indices[k] = indices[best]; indices[best] = ti;
                    float ts = scores[k]; scores[k] = scores[best]; scores[best] = ts;
                    int tk = keys[k]; keys[k] = keys[best]; keys[best] = tk;
                    int tr = reasons[k]; reasons[k] = reasons[best]; reasons[best] = tr;
                }

                int key = keys[k];
                int slot = -1;
                for (int i = 0; i < takenKinds; i++)
                {
                    if (takenKeys[i] == key) { slot = i; break; }
                }

                if (slot < 0)
                {
                    takenKeys[takenKinds] = key;
                    takenCounts[takenKinds] = 1;
                    takenKinds++;
                }
                else
                {
                    takenCounts[slot] = takenCounts[slot] + 1;
                }

                result[written] = indices[k];
                written++;
            }

            return written;
        }

        private int FindBest(
            int[] indices, float[] scores, int[] keys, int from, int to,
            int[] takenKeys, int[] takenCounts, int takenKinds, bool respectQuota)
        {
            int best = -1;

            for (int j = from; j < to; j++)
            {
                if (respectQuota && MaxPerArtist > 0)
                {
                    int used = 0;
                    for (int i = 0; i < takenKinds; i++)
                    {
                        if (takenKeys[i] == keys[j]) { used = takenCounts[i]; break; }
                    }
                    if (used >= MaxPerArtist) continue;
                }

                if (best < 0
                    || scores[j] > scores[best]
                    || (scores[j] == scores[best] && indices[j] < indices[best]))
                {
                    best = j;
                }
            }

            return best;
        }

        private float DiscoveryScoreOf(
            bool sameArtist, bool sameGenre, int sharedTagCount,
            bool favoriteArtist, bool favoriteGenre, bool isFavorite,
            float popularity01, int recentRank, bool inQueue, bool isCurrent, float randomNoise01)
        {
            float score = 0f;

            if (sameArtist) score += WSameArtist;
            if (sameGenre) score += WSameGenre;

            if (sharedTagCount > 0)
            {
                float tag = sharedTagCount * WTagUnit;
                score += tag > WMaxTagBonus ? WMaxTagBonus : tag;
            }

            if (favoriteArtist) score += WFavoriteArtist;
            if (favoriteGenre) score += WFavoriteGenre;
            if (isFavorite) score += WFavorite;

            score += Clamp01(popularity01) * WMaxPopularity;
            score -= RecentPenalty(recentRank);

            if (inQueue) score -= WQueuePenalty;
            if (isCurrent) score -= WCurrentPenalty;

            score += Clamp01(randomNoise01) * WMaxRandomNoise;
            return score;
        }

        private float RecentPenalty(int recentRank)
        {
            if (recentRank < 0) return 0f;
            if (RecentDepth <= 0) return 0f;
            if (recentRank >= RecentDepth) return 0f;

            float remain = (float)(RecentDepth - recentRank) / (float)RecentDepth;
            return WMaxRecentPenalty * remain;
        }

        private int DiscoveryReasonOf(
            bool sameArtist, bool sameGenre, int sharedTagCount,
            bool favoriteArtist, bool favoriteGenre, bool neverPlayed)
        {
            if (sameArtist) return ReasonSameArtist;
            if (favoriteArtist) return ReasonFavoriteArtist;
            if (sameGenre) return ReasonSameGenre;
            if (sharedTagCount > 0) return ReasonSharedTag;
            if (favoriteGenre) return ReasonFavoriteGenre;
            if (neverPlayed) return ReasonFresh;
            return ReasonPopular;
        }

        private int SharedTagCount(string[] seedTagsLower, int candidate)
        {
            int candTagCount = Catalog.GetTagCount(candidate);
            int shared = 0;

            for (int t = 0; t < candTagCount; t++)
            {
                string ct = Lower(Catalog.GetTag(candidate, t));
                if (ct.Length == 0) continue;

                for (int s = 0; s < seedTagsLower.Length; s++)
                {
                    if (seedTagsLower[s] == ct) { shared++; break; }
                }
            }

            return shared;
        }

        private int KeyOf(string text)
        {
            if (text == null || text.Length == 0) return 0;

            int hash = 17;
            for (int i = 0; i < text.Length; i++) hash = hash * 31 + text[i];
            return hash;
        }

        private string Lower(string text)
        {
            return text == null ? "" : text.ToLower();
        }

        /// <summary>
        /// 再生数を 0〜1 に均す。<b>対数で潰します</b> ——
        /// そのまま割ると、飛び抜けた 1 本のせいで他が全部 0 になるためです。
        /// 正典 DiscoveryScoringModel.PopularityOf の写しです。
        /// </summary>
        private float PopularityOf(float views, float maxViews)
        {
            if (views <= 0f || maxViews <= 0f) return 0f;

            // Log10 ではなく Log を使うのは、Udon で確実に呼べる側だからです。
            float top = Mathf.Log(1f + maxViews);
            if (top <= 0f) return 0f;

            return Clamp01(Mathf.Log(1f + views) / top);
        }

        private float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
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
