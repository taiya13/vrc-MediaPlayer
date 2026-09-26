using System;
using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;

namespace SmartMediaPlatform.Recommendation.Tests
{
    /// <summary>
    /// ルールベースおすすめエンジンの検証。
    /// ダミーデータで「期待通りの順位とスコア」になることを固定値で突き合わせる。
    /// ランダム補正は決定的な検証のため 0 にする(範囲チェックだけ別途行う)。
    /// </summary>
    public sealed class RecommendationEngineTests
    {
        private IMediaCatalog _catalog;
        private RecommendationEngine _engine;

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new Random(1));

            var deterministicRules = new[]
            {
                RecommendationRule.Related(10.0),
                RecommendationRule.SameArtist(5.0),
                RecommendationRule.SameGenre(3.0),
                RecommendationRule.TagMatch(1.0),
                RecommendationRule.Random(0.0), // 決定的にする
            };
            _engine = new RecommendationEngine(_catalog, deterministicRules, new Random(999));
        }

        private static string[] Ids(RecommendationResult[] r) => r.Select(x => x.Item.Id).ToArray();
        private static int[] IntScores(RecommendationResult[] r) => r.Select(x => (int)x.Score).ToArray();

        // --- GetNextRecommendations ---

        [Test]
        public void GetNext_ProducesExpectedRankingAndScores()
        {
            var r = _engine.GetNextRecommendations("music-001", 6);

            CollectionAssert.AreEqual(
                new[] { "music-002", "music-007", "video-001", "music-003", "music-006", "music-008" },
                Ids(r));
            CollectionAssert.AreEqual(new[] { 19, 10, 10, 1, 1, 1 }, IntScores(r));
        }

        [Test]
        public void GetNext_ScoreBreakdown_IsCorrect()
        {
            var top = _engine.GetNextRecommendations("music-001", 1)[0];
            Assert.AreEqual("music-002", top.Item.Id);
            Assert.AreEqual(10.0, top.RelatedScore);   // related
            Assert.AreEqual(5.0, top.ArtistScore);     // Aurora Drive
            Assert.AreEqual(3.0, top.GenreScore);      // Synthwave
            Assert.AreEqual(1.0, top.TagScore);        // night
            Assert.AreEqual(1, top.SharedTagCount);
            Assert.AreEqual(19.0, top.Score);
        }

        [Test]
        public void GetNext_TieIsBrokenByCatalogOrder()
        {
            // music-007 と video-001 は同点(10)。カタログ登録順で music-007 が先。
            var r = _engine.GetNextRecommendations("music-001", 3);
            Assert.AreEqual("music-007", r[1].Item.Id);
            Assert.AreEqual("video-001", r[2].Item.Id);
        }

        [Test]
        public void GetNext_ExcludesSeedItself()
        {
            var r = _engine.GetNextRecommendations("music-001", 999);
            Assert.IsFalse(r.Any(x => x.Item.Id == "music-001"));
            Assert.AreEqual(_catalog.Count - 1, r.Length);
        }

        [Test]
        public void GetNext_DifferentSeed_RanksByOverlap()
        {
            // video-001: Aurora Drive / Synthwave / [mv,night,retro] / related[music-001,music-002]
            var r = _engine.GetNextRecommendations("video-001", 2);
            Assert.AreEqual("music-001", r[0].Item.Id); // related10+artist5+genre3+tag2 = 20
            Assert.AreEqual(20.0, r[0].Score);
            Assert.AreEqual("music-002", r[1].Item.Id); // related10+artist5+genre3+tag1 = 19
            Assert.AreEqual(19.0, r[1].Score);
        }

        [Test]
        public void GetNext_UnknownSeedOrZeroCount_ReturnsEmpty()
        {
            Assert.IsEmpty(_engine.GetNextRecommendations("no-such-id", 5));
            Assert.IsEmpty(_engine.GetNextRecommendations(null, 5));
            Assert.IsEmpty(_engine.GetNextRecommendations("music-001", 0));
        }

        // --- GetRelatedRecommendations ---

        [Test]
        public void GetRelated_OnlyConsidersRelatedItems()
        {
            var r = _engine.GetRelatedRecommendations("music-001", 10);
            CollectionAssert.AreEqual(new[] { "music-002", "music-007" }, Ids(r));
            CollectionAssert.AreEqual(new[] { 19, 10 }, IntScores(r));
        }

        [Test]
        public void GetRelated_NoRelations_ReturnsEmpty()
        {
            Assert.IsEmpty(_engine.GetRelatedRecommendations("podcast-001", 5));
        }

        [Test]
        public void GetRelated_UnknownSeed_ReturnsEmpty()
        {
            Assert.IsEmpty(_engine.GetRelatedRecommendations("no-such-id", 5));
        }

        // --- 重みの効果 ---

        [Test]
        public void DisablingRelatedRule_ChangesRanking()
        {
            // Related を無効化すると、music-001 起点の 1 位は
            // artist+genre+tag が効く video-001(5+3+2=10)になる。
            var noRelated = new[]
            {
                RecommendationRule.SameArtist(5.0),
                RecommendationRule.SameGenre(3.0),
                RecommendationRule.TagMatch(1.0),
            };
            var engine = new RecommendationEngine(_catalog, noRelated, new Random(1));
            var r = engine.GetNextRecommendations("music-001", 1);
            Assert.AreEqual("video-001", r[0].Item.Id);
            Assert.AreEqual(10.0, r[0].Score);
        }

        // --- ランダム補正 ---

        [Test]
        public void RandomCorrection_StaysWithinWeightBound()
        {
            var engine = new RecommendationEngine(_catalog, RecommendationRule.CreateDefault(), new Random(42));
            var r = engine.GetNextRecommendations("music-001", 12);
            Assert.IsTrue(r.All(x => x.RandomScore >= 0.0 && x.RandomScore < 0.5));
        }

        [Test]
        public void RandomCorrection_DoesNotOverturnClearWinner()
        {
            // music-002(19) と 2 位(10)の差は乱数幅 0.5 より遥かに大きいので首位は不変。
            var engine = new RecommendationEngine(_catalog, RecommendationRule.CreateDefault(), new Random(7));
            Assert.AreEqual("music-002", engine.GetNextRecommendations("music-001", 3)[0].Item.Id);
        }

        [Test]
        public void Constructor_NullCatalog_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new RecommendationEngine(null));
        }
    }
}
