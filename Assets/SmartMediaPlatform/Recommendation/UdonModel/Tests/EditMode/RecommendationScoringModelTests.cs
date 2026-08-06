using NUnit.Framework;
using SmartMediaPlatform.Recommendation.UdonModel;

namespace SmartMediaPlatform.Recommendation.UdonModel.Tests
{
    /// <summary>
    /// Phase7-4: おすすめの階層スコアリングを検証する。
    ///
    /// 確かめたいのは、ユーザーが指定した優先順位が
    /// <b>「重みの合計」ではなく「階層」として絶対に守られる</b>ことです —
    /// どれだけタグが一致しても、アーティスト一致には勝てません。
    /// </summary>
    public sealed class RecommendationScoringModelTests
    {
        // ───────── 階層が守られること ─────────

        [Test]
        public void SameArtistAlwaysOutscoresEverythingBelowIt()
        {
            // アーティストだけ一致、他は何も無い。
            float artistOnly = RecommendationScoringModel.ScoreOf(
                sameArtist: true, sameGenre: false, sharedTagCount: 0,
                inQueue: false, randomNoise01: 0f);

            // ジャンルも一致、タグも 100 個一致、ノイズも最大。それでもアーティストに勝てない。
            float everythingElseMaxed = RecommendationScoringModel.ScoreOf(
                sameArtist: false, sameGenre: true, sharedTagCount: 100,
                inQueue: false, randomNoise01: 1f);

            Assert.Greater(artistOnly, everythingElseMaxed,
                           "同じアーティストというだけで、他の何を積んでも上回るはず");
        }

        [Test]
        public void SameGenreAlwaysOutscoresTagsAndNoise()
        {
            float genreOnly = RecommendationScoringModel.ScoreOf(
                sameArtist: false, sameGenre: true, sharedTagCount: 0,
                inQueue: false, randomNoise01: 0f);

            float tagsAndNoiseMaxed = RecommendationScoringModel.ScoreOf(
                sameArtist: false, sameGenre: false, sharedTagCount: 100,
                inQueue: false, randomNoise01: 1f);

            Assert.Greater(genreOnly, tagsAndNoiseMaxed,
                           "同じジャンルというだけで、タグとノイズをどれだけ積んでも上回るはず");
        }

        [Test]
        public void MoreSharedTagsScoreHigherThanFewer()
        {
            float few = RecommendationScoringModel.ScoreOf(false, false, 1, false, 0f);
            float many = RecommendationScoringModel.ScoreOf(false, false, 5, false, 0f);

            Assert.Greater(many, few, "タグの一致は多いほど上");
        }

        [Test]
        public void TagBonusNeverOutgrowsIntoTheGenreTier()
        {
            // タグが極端に多くても、ジャンル一致より上には行かない。
            float manyTags = RecommendationScoringModel.ScoreOf(
                sameArtist: false, sameGenre: false, sharedTagCount: 9999,
                inQueue: false, randomNoise01: 1f);

            float genreOnly = RecommendationScoringModel.ScoreOf(
                sameArtist: false, sameGenre: true, sharedTagCount: 0,
                inQueue: false, randomNoise01: 0f);

            Assert.Less(manyTags, genreOnly);
        }

        // ───────── 再生予定は下がるが、消えはしない ─────────

        [Test]
        public void BeingInTheUpcomingListLowersScoreButDoesNotZeroIt()
        {
            float notQueued = RecommendationScoringModel.ScoreOf(true, true, 3, false, 0.5f);
            float queued = RecommendationScoringModel.ScoreOf(true, true, 3, true, 0.5f);

            Assert.Less(queued, notQueued, "再生予定にあるものは優先度が下がる");
        }

        [Test]
        public void AQueuedTopTierCandidateStillLosesToAnUnqueuedWeakerOne()
        {
            // 再生予定にあるアーティスト一致でも、
            // 予定に無い「何も一致しない」候補より下がる。
            float queuedArtistMatch = RecommendationScoringModel.ScoreOf(
                sameArtist: true, sameGenre: true, sharedTagCount: 50,
                inQueue: true, randomNoise01: 1f);

            float unqueuedNothing = RecommendationScoringModel.ScoreOf(
                sameArtist: false, sameGenre: false, sharedTagCount: 0,
                inQueue: false, randomNoise01: 0f);

            Assert.Less(queuedArtistMatch, unqueuedNothing,
                       "「優先度を下げる」は、最下位グループより下まで沈めてよい");
        }

        // ───────── 理由 ─────────

        [Test]
        public void TheReasonMatchesTheStrongestSignal()
        {
            Assert.AreEqual(RecommendationScoringModel.ReasonSameArtist,
                            RecommendationScoringModel.ReasonOf(true, true, 5));

            Assert.AreEqual(RecommendationScoringModel.ReasonSameGenre,
                            RecommendationScoringModel.ReasonOf(false, true, 5));

            Assert.AreEqual(RecommendationScoringModel.ReasonSharedTag,
                            RecommendationScoringModel.ReasonOf(false, false, 1));

            Assert.AreEqual(RecommendationScoringModel.ReasonPopular,
                            RecommendationScoringModel.ReasonOf(false, false, 0));
        }

        // ───────── 一致判定 ─────────

        [Test]
        public void TextMatchingIgnoresCaseAndTreatsEmptyAsNoMatch()
        {
            Assert.IsTrue(RecommendationScoringModel.SameText("ONE OK ROCK", "one ok rock"));
            Assert.IsFalse(RecommendationScoringModel.SameText("", ""));
            Assert.IsFalse(RecommendationScoringModel.SameText("Ado", ""));
            Assert.IsFalse(RecommendationScoringModel.SameText(null, "Ado"));
        }

        [Test]
        public void SharedTagCountingCountsOncePerCandidateTag()
        {
            // SharedTagCount は「呼び出し側がすでに小文字化した」ものを受け取る前提
            // (大文字小文字を無視したいのは Udon 側の GetTag().ToLower() の仕事)。
            string[] seed = { "rock", "live", "2024" };
            string[] candidate = { "rock", "acoustic", "live" };

            Assert.AreEqual(2, RecommendationScoringModel.SharedTagCount(seed, candidate));
        }

        [Test]
        public void SharedTagCountingHandlesEmptyArrays()
        {
            Assert.AreEqual(0, RecommendationScoringModel.SharedTagCount(null, null));
            Assert.AreEqual(0, RecommendationScoringModel.SharedTagCount(new string[0], new[] { "rock" }));
        }

        // ───────── 並べ替え ─────────

        [Test]
        public void SelectTopIntoOrdersByScoreDescending()
        {
            int[] indices = { 5, 2, 8, 1 };
            float[] scores = { 10f, 30f, 20f, 5f };
            var result = new int[4];

            int written = RecommendationScoringModel.SelectTopInto(indices, scores, 4, result);

            Assert.AreEqual(4, written);
            CollectionAssert.AreEqual(new[] { 2, 8, 5, 1 }, result);
        }

        [Test]
        public void SelectTopIntoBreaksTiesByCatalogIndexAscending()
        {
            int[] indices = { 9, 3, 7 };
            float[] scores = { 10f, 10f, 10f };
            var result = new int[3];

            RecommendationScoringModel.SelectTopInto(indices, scores, 3, result);

            CollectionAssert.AreEqual(new[] { 3, 7, 9 }, result, "同点は index の若い順(決定的)");
        }

        [Test]
        public void SelectTopIntoStopsAtTheRequestedCount()
        {
            int[] indices = { 1, 2, 3, 4, 5 };
            float[] scores = { 5f, 4f, 3f, 2f, 1f };
            var result = new int[5];

            int written = RecommendationScoringModel.SelectTopInto(indices, scores, 2, result);

            Assert.AreEqual(2, written);
            Assert.AreEqual(1, result[0]);
            Assert.AreEqual(2, result[1]);
        }

        [Test]
        public void SelectTopIntoHandlesFewerCandidatesThanRequested()
        {
            int[] indices = { 4, 1 };
            float[] scores = { 1f, 2f };
            var result = new int[5];

            int written = RecommendationScoringModel.SelectTopInto(indices, scores, 5, result);

            Assert.AreEqual(2, written);
        }

        // ───────── 現実的な一場面を通しで ─────────

        [Test]
        public void ARealisticSceneRanksArtistFirstThenGenreThenTagsThenTheRest()
        {
            // 0: 同アーティスト
            // 1: 同ジャンルのみ
            // 2: タグ 2 個一致のみ
            // 3: 何も一致しないが、再生予定にも入っていない
            // 4: 同アーティストだが再生予定に入っている(下がるが消えない)
            int[] indices = { 0, 1, 2, 3, 4 };
            var scores = new float[5];

            scores[0] = RecommendationScoringModel.ScoreOf(true, false, 0, false, 0f);
            scores[1] = RecommendationScoringModel.ScoreOf(false, true, 0, false, 0f);
            scores[2] = RecommendationScoringModel.ScoreOf(false, false, 2, false, 0f);
            scores[3] = RecommendationScoringModel.ScoreOf(false, false, 0, false, 0f);
            scores[4] = RecommendationScoringModel.ScoreOf(true, false, 0, true, 0f);

            var result = new int[5];
            RecommendationScoringModel.SelectTopInto(indices, scores, 5, result);

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, result,
                "アーティスト → ジャンル → タグ → その他 → 再生予定(下がるが最後まで残る)");
        }
    }
}
