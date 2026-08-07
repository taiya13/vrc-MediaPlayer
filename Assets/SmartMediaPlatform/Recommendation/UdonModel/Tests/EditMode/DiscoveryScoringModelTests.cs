using NUnit.Framework;
using SmartMediaPlatform.Recommendation.UdonModel;

namespace SmartMediaPlatform.Recommendation.UdonModel.Tests
{
    /// <summary>
    /// <see cref="DiscoveryScoringModel"/> の点数と並び。
    ///
    /// いちばん大事なのは<b>「同じアーティストばかりにならないこと」</b>です。
    /// Phase7-4 の階層方式では、同アーティストが 1 曲でもあれば
    /// 他は永久に浮上できませんでした。ここではそれが起きないことを確かめます。
    /// </summary>
    public class DiscoveryScoringModelTests
    {
        const int RecentDepth = 10;

        static float Score(
            bool sameArtist = false, bool sameGenre = false, int tags = 0,
            bool favArtist = false, bool favGenre = false, bool isFav = false,
            float popularity = 0f, int recentRank = -1,
            bool inQueue = false, bool isCurrent = false, float noise = 0f)
        {
            return DiscoveryScoringModel.ScoreOf(
                sameArtist, sameGenre, tags, favArtist, favGenre, isFav,
                popularity, recentRank, RecentDepth, inQueue, isCurrent, noise);
        }

        // ───────── 点数 ─────────

        [Test]
        public void 手掛かりが何も無ければ零点()
        {
            Assert.AreEqual(0f, Score(), 0.001f);
        }

        [Test]
        public void 同じアーティストがいちばん強い単独の手掛かり()
        {
            Assert.Greater(Score(sameArtist: true), Score(sameGenre: true));
            Assert.Greater(Score(sameArtist: true), Score(tags: 3));
            Assert.Greater(Score(sameArtist: true), Score(favArtist: true));
        }

        [Test]
        public void 手掛かりが重なれば同じアーティストを追い越せる()
        {
            // ここが Phase7-8 の肝。階層方式では絶対に起きなかったこと。
            float sameArtistOnly = Score(sameArtist: true);
            float otherArtistButClose = Score(sameGenre: true, tags: 2, favArtist: true);

            Assert.Greater(otherArtistButClose, sameArtistOnly,
                "同ジャンル + タグ 2 つ + お気に入りのアーティスト なら追い越す");
        }

        [Test]
        public void タグの加点には上限がある()
        {
            float three = Score(tags: 3);
            float ten = Score(tags: 10);

            Assert.AreEqual(three, ten, 0.001f, "何個重なっても 3 個分で頭打ち");
        }

        [Test]
        public void 再生予定の曲は大きく下がるが消えはしない()
        {
            float queued = Score(sameArtist: true, inQueue: true);

            Assert.Less(queued, Score(), "手掛かりが何も無い曲より下");
            Assert.Greater(queued, Score(isCurrent: true), "再生中よりは上");
        }

        [Test]
        public void 再生中の曲は実質的に選ばれない()
        {
            float current = Score(
                sameArtist: true, sameGenre: true, tags: 3, favArtist: true, isFav: true,
                popularity: 1f, isCurrent: true);

            Assert.Less(current, -1000f, "どれだけ手掛かりが揃っても下に沈む");
        }

        // ───────── さっき聴いた曲 ─────────

        [Test]
        public void 直前に聴いた曲がいちばん強く下がる()
        {
            float justNow = DiscoveryScoringModel.RecentPenalty(0, RecentDepth);
            float aWhileAgo = DiscoveryScoringModel.RecentPenalty(5, RecentDepth);

            Assert.Greater(justNow, aWhileAgo);
            Assert.AreEqual(DiscoveryScoringModel.MaxRecentPenalty, justNow, 0.001f);
        }

        [Test]
        public void 履歴に無い曲は下がらない()
        {
            Assert.AreEqual(0f, DiscoveryScoringModel.RecentPenalty(-1, RecentDepth), 0.001f);
        }

        [Test]
        public void 深さを越えて古い曲は下がらない()
        {
            Assert.AreEqual(0f, DiscoveryScoringModel.RecentPenalty(RecentDepth, RecentDepth), 0.001f);
            Assert.AreEqual(0f, DiscoveryScoringModel.RecentPenalty(99, RecentDepth), 0.001f);
        }

        // ───────── 理由 ─────────

        [Test]
        public void 理由はいちばん強いものを一つだけ返す()
        {
            Assert.AreEqual(
                DiscoveryScoringModel.ReasonSameArtist,
                DiscoveryScoringModel.ReasonOf(true, true, 3, true, true, true));

            Assert.AreEqual(
                DiscoveryScoringModel.ReasonFavoriteArtist,
                DiscoveryScoringModel.ReasonOf(false, true, 3, true, true, true));

            Assert.AreEqual(
                DiscoveryScoringModel.ReasonSameGenre,
                DiscoveryScoringModel.ReasonOf(false, true, 3, false, true, true));

            Assert.AreEqual(
                DiscoveryScoringModel.ReasonSharedTag,
                DiscoveryScoringModel.ReasonOf(false, false, 2, false, true, true));

            Assert.AreEqual(
                DiscoveryScoringModel.ReasonFavoriteGenre,
                DiscoveryScoringModel.ReasonOf(false, false, 0, false, true, true));

            Assert.AreEqual(
                DiscoveryScoringModel.ReasonFresh,
                DiscoveryScoringModel.ReasonOf(false, false, 0, false, false, true));

            Assert.AreEqual(
                DiscoveryScoringModel.ReasonPopular,
                DiscoveryScoringModel.ReasonOf(false, false, 0, false, false, false));
        }

        // ───────── 並び(多様さ)─────────

        [Test]
        public void 同じアーティストは枠の数までしか並ばない()
        {
            // 0〜4 は同じアーティスト(点数が高い)、5〜6 は別のアーティスト。
            var indices = new[] { 0, 1, 2, 3, 4, 5, 6 };
            var scores = new[] { 100f, 99f, 98f, 97f, 96f, 50f, 49f };
            var keys = new[] { 7, 7, 7, 7, 7, 8, 9 };

            var result = new int[4];
            int written = DiscoveryScoringModel.SelectDiverseInto(
                indices, scores, keys, 2, 4, result);

            Assert.AreEqual(4, written);
            CollectionAssert.AreEqual(new[] { 0, 1, 5, 6 }, result,
                "同じ組からは 2 曲まで。3 曲目より、点数が低くても別の組が先");
        }

        [Test]
        public void 枠を守ると足りなくなるときは枠を外す()
        {
            // 全部同じアーティストしかいない。枠を守ると 2 件しか返せない。
            var indices = new[] { 0, 1, 2, 3 };
            var scores = new[] { 100f, 99f, 98f, 97f };
            var keys = new[] { 7, 7, 7, 7 };

            var result = new int[4];
            int written = DiscoveryScoringModel.SelectDiverseInto(
                indices, scores, keys, 2, 4, result);

            Assert.AreEqual(4, written, "候補があるのに件数が足りない、にはしない");
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, result);
        }

        [Test]
        public void 候補より多く求められても候補の数しか返さない()
        {
            var indices = new[] { 0, 1 };
            var scores = new[] { 10f, 5f };
            var keys = new[] { 1, 2 };

            var result = new int[8];
            int written = DiscoveryScoringModel.SelectDiverseInto(
                indices, scores, keys, 2, 8, result);

            Assert.AreEqual(2, written);
        }

        [Test]
        public void 同点なら番号の小さいほうが先で結果が毎回同じになる()
        {
            var indices = new[] { 5, 2, 9 };
            var scores = new[] { 10f, 10f, 10f };
            var keys = new[] { 1, 2, 3 };

            var result = new int[3];
            DiscoveryScoringModel.SelectDiverseInto(indices, scores, keys, 2, 3, result);

            CollectionAssert.AreEqual(new[] { 2, 5, 9 }, result);
        }

        // ───────── 組を表す数 ─────────

        [Test]
        public void 同じ名前は必ず同じ数になる()
        {
            Assert.AreEqual(
                DiscoveryScoringModel.KeyOf("YOASOBI"),
                DiscoveryScoringModel.KeyOf("YOASOBI"));

            Assert.AreNotEqual(
                DiscoveryScoringModel.KeyOf("YOASOBI"),
                DiscoveryScoringModel.KeyOf("Ado"));
        }

        [Test]
        public void 名前が無いものは全部同じ組として扱う()
        {
            Assert.AreEqual(0, DiscoveryScoringModel.KeyOf(""));
            Assert.AreEqual(0, DiscoveryScoringModel.KeyOf(null));
        }

        // ───────── 人気度(Phase7-9)─────────

        [Test]
        public void 再生数が無ければ人気度は零()
        {
            Assert.AreEqual(0f, DiscoveryScoringModel.PopularityOf(0f, 1000000f), 0.0001f);
            Assert.AreEqual(0f, DiscoveryScoringModel.PopularityOf(1000f, 0f), 0.0001f);
        }

        [Test]
        public void いちばん多い曲の人気度は一()
        {
            Assert.AreEqual(1f, DiscoveryScoringModel.PopularityOf(1000000f, 1000000f), 0.0001f);
        }

        [Test]
        public void 対数なので少ない再生数でも埋もれない()
        {
            // 1 億回が最大のとき、10 万回の曲。
            // そのまま割ると 0.001 で、人気度は実質ゼロになる。
            float linear = 100000f / 100000000f;
            float scaled = DiscoveryScoringModel.PopularityOf(100000f, 100000000f);

            Assert.Less(linear, 0.01f, "そのまま割ると埋もれる");
            Assert.Greater(scaled, 0.55f, "対数なら半分より上に来る");
        }

        [Test]
        public void 十倍ごとに同じだけ上がる()
        {
            float a = DiscoveryScoringModel.PopularityOf(1000f, 100000000f);
            float b = DiscoveryScoringModel.PopularityOf(10000f, 100000000f);
            float c = DiscoveryScoringModel.PopularityOf(100000f, 100000000f);

            Assert.AreEqual(b - a, c - b, 0.01f);
        }

        // ───────── 通しで見る ─────────

        [Test]
        public void 同アーティスト五曲でも上位には別のアーティストが混ざる()
        {
            // 「YOASOBI を聴いている」状況を作る。
            //   0〜4: YOASOBI(同アーティスト)
            //   5   : Ado    (同ジャンル + タグ 2)
            //   6   : ヨルシカ(同ジャンル + タグ 1 + お気に入りのアーティスト)
            //   7   : 無関係
            var indices = new[] { 0, 1, 2, 3, 4, 5, 6, 7 };
            var keys = new[] { 1, 1, 1, 1, 1, 2, 3, 4 };
            var scores = new float[8];

            for (int i = 0; i < 5; i++) scores[i] = Score(sameArtist: true);
            scores[5] = Score(sameGenre: true, tags: 2);
            scores[6] = Score(sameGenre: true, tags: 1, favArtist: true);
            scores[7] = Score(popularity: 0.5f);

            var result = new int[5];
            int written = DiscoveryScoringModel.SelectDiverseInto(
                indices, scores, keys, 2, 5, result);

            Assert.AreEqual(5, written);

            int sameArtistCount = 0;
            for (int i = 0; i < written; i++)
            {
                if (result[i] <= 4) sameArtistCount++;
            }

            Assert.AreEqual(2, sameArtistCount, "YOASOBI は 2 曲まで");
            CollectionAssert.Contains(result, 6, "ヨルシカが並ぶ");
            CollectionAssert.Contains(result, 5, "Ado が並ぶ");
        }
    }
}
