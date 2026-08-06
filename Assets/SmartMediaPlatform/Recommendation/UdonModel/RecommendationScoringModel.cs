namespace SmartMediaPlatform.Recommendation.UdonModel
{
    /// <summary>
    /// <b>「なぜこれを勧めるか」を決める、Udon へ写す前の正典。</b>Phase7-4。
    ///
    /// <b>なぜ書き直したのか</b><br/>
    /// Phase7-3 までのおすすめは、<c>Catalog Builder</c> が事前計算した
    /// 「関連(RelatedIds)」だけを対象にスコア付けしていました。
    /// 関連が無い曲は<b>おすすめが 0 件</b>になり、利用者からは
    /// 「関連動画という印象で、おすすめらしくない」と見えていました。
    ///
    /// ここでは<b>カタログ全体から選び</b>、優先順位を<b>階層</b>にします —
    /// <list type="number">
    /// <item>同じアーティスト(チャンネル)</item>
    /// <item>同じジャンル</item>
    /// <item>共有タグの数</item>
    /// <item>それ以外(いまは軽いランダム性。<see cref="ReasonPopular"/> 参照)</item>
    /// </list>
    /// 「重み付き合計」ではなく<b>階層</b>にしたのは、
    /// 例えば「タグが 5 個一致した無関係な曲」が
    /// 「アーティストが一致した曲」より上に来てしまうと、
    /// 利用者から見て理由がぶれて信用されなくなるためです。
    /// 上の階層は、下の階層をどれだけ積んでも絶対に超えない大きさにしてあります。
    ///
    /// <b>将来「お気に入り・履歴・再生回数・評価」を足す場所</b><br/>
    /// いまは「人気順」に使える実データ(再生回数など)を持っていないので、
    /// 4 番目の階層は<b>ランダム性で代役</b>しています。
    /// 将来そうしたデータが増えたら、<see cref="ReasonOf"/> の判定順と
    /// <see cref="ScoreOf"/> の一番下の項を差し替えるだけで済むよう、
    /// <b>階層はこの 2 か所だけに集約してあります</b>。
    /// お気に入り・評価は「新しい階層」として、Artist の上か Genre と Tag の間に
    /// 挿し込む形になります(階層の大きさを 1 段増やすだけ)。
    ///
    /// <b>Udon で書ける形</b>です。interface・List・例外を使わず、
    /// すべて配列渡し・戻り値は数値だけにしてあります。
    /// 対応する Udon 版は <c>UdonRecommendationEngine</c> です。
    /// <b>ここを変えたら、必ずあちらも直してください。</b>
    /// </summary>
    public static class RecommendationScoringModel
    {
        // ───────── 理由 ─────────

        /// <summary>理由なし(実際には出ない。ReasonOf は必ずどれかを返す)。</summary>
        public const int ReasonNone = 0;

        /// <summary>同じアーティスト(チャンネル)。</summary>
        public const int ReasonSameArtist = 1;

        /// <summary>同じジャンル。</summary>
        public const int ReasonSameGenre = 2;

        /// <summary>タグが 1 つ以上重なっている。</summary>
        public const int ReasonSharedTag = 3;

        /// <summary>
        /// どれにも当てはまらない。「人気順」の代わりに、いまはここへ落ちます。
        /// 将来、再生回数などの実データが入ったら、この階層をそちらへ差し替えます。
        /// </summary>
        public const int ReasonPopular = 4;

        // ───────── 階層の大きさ ─────────
        //
        // 上の階層は、下の階層をどれだけ積んでも絶対に超えません。
        //   TierArtist > TierGenre + MaxTagBonus + MaxRandomNoise
        //   TierGenre  >            MaxTagBonus + MaxRandomNoise

        public const float TierArtist = 1000000f;
        public const float TierGenre = 10000f;

        /// <summary>タグ 1 件一致ごとの加点。</summary>
        public const float TierTagUnit = 100f;

        /// <summary>タグ加点の上限(何件一致しても Genre の階層を超えない量に抑える)。</summary>
        public const float MaxTagBonus = 5000f;

        /// <summary>毎回ほぼ同じ並びにならないための、ごく小さいランダム性(0〜この値)。</summary>
        public const float MaxRandomNoise = 50f;

        /// <summary>
        /// <b>再生予定にある曲を下げる量。</b>除外はしません — 消えると
        /// 「押したのに反応しない」ように見えるためです。どの階層にいても、
        /// 「予定に無い、いちばん弱い候補」より確実に下へ回るだけの量を引きます。
        /// </summary>
        public const float QueuePenalty = 2000000f;

        // ───────── 1 件ぶんのスコア ─────────

        /// <summary>
        /// <paramref name="randomNoise01"/> は 0〜1 の乱数(呼び出し側が用意する)。
        /// Udon 側は <c>UnityEngine.Random.Range(0f, 1f)</c>、
        /// テストでは <c>System.Random.NextDouble()</c> を渡します。
        /// </summary>
        public static float ScoreOf(
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

            if (inQueue) score -= QueuePenalty;

            return score;
        }

        /// <summary>いちばん強い理由を 1 つだけ返す。3 つ並べると、どれが効いたのか分からなくなるため。</summary>
        public static int ReasonOf(bool sameArtist, bool sameGenre, int sharedTagCount)
        {
            if (sameArtist) return ReasonSameArtist;
            if (sameGenre) return ReasonSameGenre;
            if (sharedTagCount > 0) return ReasonSharedTag;
            return ReasonPopular;
        }

        // ───────── 一致判定(文字列比較を 1 か所に集約)─────────

        /// <summary>大文字小文字を無視して比べる。どちらかが空なら不一致(「不明どうし」を一致にしないため)。</summary>
        public static bool SameText(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(a.Trim(), b.Trim(), System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>候補側のタグのうち、種のタグと重なっている数。</summary>
        public static int SharedTagCount(string[] seedTagsLower, string[] candidateTagsLower)
        {
            if (seedTagsLower == null || candidateTagsLower == null) return 0;

            int shared = 0;
            for (int c = 0; c < candidateTagsLower.Length; c++)
            {
                string tag = candidateTagsLower[c];
                if (string.IsNullOrEmpty(tag)) continue;

                for (int s = 0; s < seedTagsLower.Length; s++)
                {
                    if (seedTagsLower[s] == tag) { shared++; break; }
                }
            }
            return shared;
        }

        // ───────── 並べ替え ─────────

        /// <summary>
        /// <paramref name="candidateIndices"/> / <paramref name="scores"/> を
        /// スコア降順(同点は catalog index 昇順、決定的)に並べ替え、
        /// 上位 <paramref name="take"/> 件を <paramref name="resultIndices"/> へ書く。
        ///
        /// <b>2 つの配列を破壊的に並べ替えます</b>(Udon で新しい配列を都度作らないための節約)。
        /// 呼び出し側で使い捨てる作業配列を渡してください。
        /// </summary>
        /// <returns>実際に書いた件数(候補が足りなければ <paramref name="take"/> 未満)。</returns>
        public static int SelectTopInto(
            int[] candidateIndices, float[] scores, int take, int[] resultIndices)
        {
            int m = candidateIndices.Length;
            int actualTake = take < m ? take : m;
            if (actualTake < 0) actualTake = 0;

            for (int k = 0; k < actualTake; k++)
            {
                int best = k;
                for (int j = k + 1; j < m; j++)
                {
                    if (scores[j] > scores[best]
                        || (scores[j] == scores[best] && candidateIndices[j] < candidateIndices[best]))
                    {
                        best = j;
                    }
                }

                if (best != k)
                {
                    float ts = scores[k]; scores[k] = scores[best]; scores[best] = ts;
                    int ti = candidateIndices[k];
                    candidateIndices[k] = candidateIndices[best];
                    candidateIndices[best] = ti;
                }
            }

            for (int i = 0; i < actualTake; i++) resultIndices[i] = candidateIndices[i];
            return actualTake;
        }

        // ───────── 自動再生でどれを選ぶか(Phase7-6)─────────

        /// <summary>
        /// <b>いま見ている候補を採用するか。</b>
        ///
        /// おすすめの並びのうち<b>使える候補だけを、等確率で 1 つ選ぶ</b>ための判定です。
        /// 使える候補が何個あるかは<b>最後まで見ないと分かりません</b>。
        /// かといって全部を配列に貯めると、Udon では毎回配列を作ることになります。
        /// そこで「<paramref name="seen"/> 個目を確率 1/seen で採用する」を繰り返します。
        /// これだけで、最後まで見終わったときにどの候補も等確率になります
        /// (リザーバー抽出)。
        ///
        /// <paramref name="roll"/> は <c>0</c> 以上 <paramref name="seen"/> 未満の乱数です。
        /// Udon 側は <c>UnityEngine.Random.Range(0, seen)</c>、
        /// テストでは決めた値を渡します。
        /// </summary>
        /// <param name="seen">これが何個目の「使える候補」か(1 から数える)。</param>
        public static bool TakeAsRandomPick(int seen, int roll)
        {
            if (seen <= 1) return true;
            return roll == 0;
        }
    }
}
