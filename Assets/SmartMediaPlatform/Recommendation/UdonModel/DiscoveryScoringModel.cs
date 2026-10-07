namespace SmartMediaPlatform.Recommendation.UdonModel
{
    /// <summary>
    /// <b>「次に聴きたい曲」を決める点数のつけ方。</b>Phase7-8。
    ///
    /// ───────────────────────────────────────────────
    /// <b>なぜ作り直したのか</b>
    ///
    /// Phase7-4 までは<b>階層</b>で並べていました
    /// (<see cref="RecommendationScoringModel"/>)。
    /// 同アーティストは 1000000 点、同ジャンルは 10000 点 —— という具合です。
    /// この作りは「絶対に同アーティストが先」を保証できる代わりに、
    /// <b>同アーティストが 1 曲でもあれば、他は永久に浮上できません</b>。
    /// 「YOASOBI の次に YOASOBI しか出ない」の原因はここでした。
    ///
    /// ここでは<b>足し算</b>にします。同アーティストは強い手掛かりですが、
    /// 「同ジャンル + タグ 3 つ一致 + お気に入りのアーティスト」が集まれば
    /// <b>追い越せる</b>ようにします。そうして初めて
    /// YOASOBI → Ado / ヨルシカ / 米津玄師 が並びます。
    ///
    /// ───────────────────────────────────────────────
    /// <b>点数だけでは足りない</b>
    ///
    /// 足し算にしても、同アーティストが 10 曲あれば上位 10 件が
    /// 全部そのアーティストになります。点数は正しいのに、
    /// <b>一覧としては失敗</b>です。だから選ぶときに
    /// <b>「1 組から取ってよいのは N 曲まで」</b>という枠を掛けます
    /// (<see cref="SelectDiverseInto"/>)。
    /// <b>点数(良さ)と並び(多様さ)は別の仕事</b>で、分けて書いてあります。
    ///
    /// ───────────────────────────────────────────────
    /// <b>将来ここに足せるもの</b>
    ///
    /// 引数はすべて<b>「すでに数えた結果」</b>で受け取ります
    /// (再生回数そのものではなく、0〜1 に均した人気度、など)。
    /// 数え方を変えても、この式は変わりません。
    /// 再生回数・スキップ回数・評価を足すときも、
    /// <b>引数を 1 つ増やして重みを 1 つ足すだけ</b>で済みます。
    /// </summary>
    public static class DiscoveryScoringModel
    {
        // ───────── おすすめの理由 ─────────

        public const int ReasonNone = 0;

        /// <summary>いま聴いている曲と同じアーティスト。</summary>
        public const int ReasonSameArtist = 1;

        /// <summary>同じジャンル。</summary>
        public const int ReasonSameGenre = 2;

        /// <summary>タグが重なっている(似た雰囲気)。</summary>
        public const int ReasonSharedTag = 3;

        /// <summary>お気に入りに入れているアーティスト。</summary>
        public const int ReasonFavoriteArtist = 4;

        /// <summary>よく聴かれている。</summary>
        public const int ReasonPopular = 5;

        /// <summary>しばらく聴いていない。</summary>
        public const int ReasonFresh = 6;

        /// <summary>お気に入りによくあるジャンル。</summary>
        public const int ReasonFavoriteGenre = 7;

        // ───────── 重み ─────────
        //
        // <b>この数字の並びがそのまま「何を大事にするか」の宣言</b>です。
        // 同アーティストがいちばん強いが、他の手掛かりが 2〜3 個重なれば
        // 追い越せる —— そういう比率にしてあります。

        /// <summary>同じアーティスト。いちばん強い 1 つの手掛かり。</summary>
        public const float WeightSameArtist = 120f;

        /// <summary>同じジャンル。</summary>
        public const float WeightSameGenre = 90f;

        /// <summary>タグ 1 つ一致ごと。</summary>
        public const float WeightTagUnit = 35f;

        /// <summary>タグ加点の上限(3 つ分)。いくら重なっても青天井にはしない。</summary>
        public const float MaxTagBonus = 105f;

        /// <summary>お気に入りに入っているアーティストの曲。</summary>
        public const float WeightFavoriteArtist = 70f;

        /// <summary>お気に入りに多いジャンルの曲。</summary>
        public const float WeightFavoriteGenre = 45f;

        /// <summary>その曲自体がお気に入り。</summary>
        public const float WeightFavorite = 60f;

        /// <summary>人気度(0〜1)に掛ける上限。</summary>
        public const float MaxPopularity = 40f;

        /// <summary>
        /// <b>さっき聴いた曲を下げる量の上限。</b>
        /// 直前に聴いた曲がいちばん強く下がり、古いほど戻ってきます。
        /// <b>消さずに下げる</b>のは、曲が少ないカタログで
        /// 候補が尽きるのを避けるためです。
        /// </summary>
        public const float MaxRecentPenalty = 220f;

        /// <summary>再生予定に入っている曲を下げる量。</summary>
        public const float QueuePenalty = 1000f;

        /// <summary>いま鳴っている曲を下げる量。<b>実質的に除外</b>。</summary>
        public const float CurrentPenalty = 100000f;

        /// <summary>毎回同じ並びにならないための揺らぎ(0〜この値)。</summary>
        public const float MaxRandomNoise = 25f;

        /// <summary>
        /// <b>1 曲の点数。</b>
        ///
        /// <paramref name="popularity01"/> は 0〜1 に均した人気度、
        /// <paramref name="recentRank"/> は「何曲前に聴いたか」
        /// (0 = 直前、-1 = 履歴に無い)、
        /// <paramref name="randomNoise01"/> は 0〜1 の乱数です
        /// (Udon 側は <c>Random.Range(0f, 1f)</c>、テストでは決めた値)。
        /// </summary>
        public static float ScoreOf(
            bool sameArtist, bool sameGenre, int sharedTagCount,
            bool favoriteArtist, bool favoriteGenre, bool isFavorite,
            float popularity01, int recentRank, int recentDepth,
            bool inQueue, bool isCurrent, float randomNoise01)
        {
            float score = 0f;

            if (sameArtist) score += WeightSameArtist;
            if (sameGenre) score += WeightSameGenre;

            if (sharedTagCount > 0)
            {
                float tag = sharedTagCount * WeightTagUnit;
                score += tag > MaxTagBonus ? MaxTagBonus : tag;
            }

            if (favoriteArtist) score += WeightFavoriteArtist;
            if (favoriteGenre) score += WeightFavoriteGenre;
            if (isFavorite) score += WeightFavorite;

            score += Clamp01(popularity01) * MaxPopularity;
            score -= RecentPenalty(recentRank, recentDepth);

            if (inQueue) score -= QueuePenalty;
            if (isCurrent) score -= CurrentPenalty;

            score += Clamp01(randomNoise01) * MaxRandomNoise;
            return score;
        }

        /// <summary>
        /// <b>さっき聴いた曲を下げる量。</b>
        /// 直前(<paramref name="recentRank"/> = 0)がいちばん重く、
        /// <paramref name="recentDepth"/> 曲前まで来ると 0 に戻ります。
        /// </summary>
        public static float RecentPenalty(int recentRank, int recentDepth)
        {
            if (recentRank < 0) return 0f;
            if (recentDepth <= 0) return 0f;
            if (recentRank >= recentDepth) return 0f;

            float remain = (float)(recentDepth - recentRank) / (float)recentDepth;
            return MaxRecentPenalty * remain;
        }

        /// <summary>
        /// <b>いちばん強い理由を 1 つだけ返す。</b>
        /// 3 つ並べると、どれが効いたのか分からなくなるためです。
        /// </summary>
        public static int ReasonOf(
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

        /// <summary>
        /// <b>点数順に選ぶ。ただし 1 組から取りすぎない。</b>
        ///
        /// <paramref name="groupKeys"/> は「同じ組かどうか」を表す数
        /// (アーティスト名のハッシュなど)。同じ数のものは同じ組です。
        ///
        /// <b>枠が埋まって選べる候補が無くなったら、枠を外して続けます。</b>
        /// 候補があるのに件数が足りない、という結果にしないためです
        /// (曲が 1 組しかないカタログでは、枠を守ると 2 件しか返せません)。
        ///
        /// <paramref name="candidateIndices"/> / <paramref name="scores"/> /
        /// <paramref name="groupKeys"/> は<b>破壊的に並べ替えます</b>
        /// (Udon で配列を都度作らないための節約)。
        /// </summary>
        /// <returns>実際に書いた件数。</returns>
        public static int SelectDiverseInto(
            int[] candidateIndices, float[] scores, int[] groupKeys,
            int maxPerGroup, int take, int[] result)
        {
            int m = candidateIndices.Length;
            int actualTake = take < m ? take : m;
            if (actualTake < 0) actualTake = 0;

            // すでに選んだ組と、その組から何曲取ったか。
            var takenKeys = new int[actualTake];
            var takenCounts = new int[actualTake];
            int takenKinds = 0;

            int written = 0;

            for (int k = 0; k < actualTake; k++)
            {
                int best = FindBest(
                    candidateIndices, scores, groupKeys, k, m,
                    takenKeys, takenCounts, takenKinds, maxPerGroup, true);

                // 枠のせいで選べなかったら、枠を外してもう一度探す。
                if (best < 0)
                {
                    best = FindBest(
                        candidateIndices, scores, groupKeys, k, m,
                        takenKeys, takenCounts, takenKinds, maxPerGroup, false);
                }

                if (best < 0) break;

                Swap(candidateIndices, scores, groupKeys, k, best);

                int key = groupKeys[k];
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
                    takenCounts[slot]++;
                }

                result[written] = candidateIndices[k];
                written++;
            }

            return written;
        }

        private static int FindBest(
            int[] candidateIndices, float[] scores, int[] groupKeys, int from, int to,
            int[] takenKeys, int[] takenCounts, int takenKinds, int maxPerGroup, bool respectQuota)
        {
            int best = -1;

            for (int j = from; j < to; j++)
            {
                if (respectQuota && maxPerGroup > 0)
                {
                    int used = 0;
                    for (int i = 0; i < takenKinds; i++)
                    {
                        if (takenKeys[i] == groupKeys[j]) { used = takenCounts[i]; break; }
                    }
                    if (used >= maxPerGroup) continue;
                }

                if (best < 0
                    || scores[j] > scores[best]
                    || (scores[j] == scores[best] && candidateIndices[j] < candidateIndices[best]))
                {
                    best = j;
                }
            }

            return best;
        }

        private static void Swap(int[] indices, float[] scores, int[] keys, int a, int b)
        {
            if (a == b) return;

            int ti = indices[a]; indices[a] = indices[b]; indices[b] = ti;
            float ts = scores[a]; scores[a] = scores[b]; scores[b] = ts;
            int tk = keys[a]; keys[a] = keys[b]; keys[b] = tk;
        }

        /// <summary>
        /// <b>並び順から人気度を作る。</b>Phase8。
        ///
        /// <b>再生数そのものは持ちません。</b>持つと
        /// <list type="bullet">
        /// <item>YouTube の数字をワールドへ恒久保存することになる</item>
        /// <item>古い数字を表示し続けることになる</item>
        /// </list>
        /// の 2 つが避けられないためです。
        ///
        /// 代わりに<b>「人気順で取り込んだ結果の並び」</b>を使います。
        /// 前にあるものほど人気なので、位置だけで順序は復元できます。
        /// <b>数字を 1 つも保存せずに、人気度の順序だけが残る</b>のが狙いです。
        ///
        /// <paramref name="rank"/> は 0 が先頭(いちばん人気)。
        /// 先頭を 1.0、最後を 0.0 として、間はまっすぐ下がります。
        /// 対数で潰す必要はありません —— 順位はすでに均された値だからです。
        /// </summary>
        public static float PopularityFromRank(int rank, int count)
        {
            if (count <= 1) return 0f;
            if (rank < 0) return 0f;
            if (rank >= count) return 0f;

            return 1f - (float)rank / (float)(count - 1);
        }

        /// <summary>文字列を数に潰す。<b>同じ文字列なら必ず同じ数</b>になればよい。</summary>
        public static int KeyOf(string text)
        {
            if (text == null || text.Length == 0) return 0;

            int hash = 17;
            for (int i = 0; i < text.Length; i++) hash = hash * 31 + text[i];
            return hash;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
