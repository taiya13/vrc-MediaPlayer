using NUnit.Framework;

namespace SmartMediaPlatform.World.UdonModel.Tests
{
    /// <summary>
    /// Phase7-5: クロスフェードの数え方を検証する。
    ///
    /// 確かめたいのは 3 つです。
    /// <list type="bullet">
    /// <item><b>始めてはいけないときに始めない</b>(長さ不明・短い曲・行き過ぎ)</item>
    /// <item><b>混ざっている最中に音が痩せない</b>(等パワー)</item>
    /// <item>絵と音で混ぜ方が違う</item>
    /// </list>
    /// </summary>
    public sealed class CrossfadeModelTests
    {
        private static CrossfadeModel New()
        {
            var model = new CrossfadeModel();
            model.FadeSeconds = 10f;
            model.MinimumTrackSeconds = 30f;
            return model;
        }

        // ───────── いつ始めるか ─────────

        [Test]
        public void PreparingStartsExactlyOneFadeLengthBeforeTheEnd()
        {
            var model = New();

            // 200 秒の曲。残り 10.1 秒ではまだ始めない。
            Assert.IsFalse(model.ShouldPrepare(189.9f, 200f));

            // 残りちょうど 10 秒で始める。
            Assert.IsTrue(model.ShouldPrepare(190f, 200f));
        }

        [Test]
        public void NothingHappensWhenTheLengthIsUnknown()
        {
            var model = New();

            // 生配信や読み込み中は長さが 0 で返る。
            // ここで始めると、まだ半分残っているのに次の曲へ移ってしまう。
            Assert.IsFalse(model.ShouldPrepare(0f, 0f));
            Assert.IsFalse(model.ShouldPrepare(100f, 0f));
            Assert.IsFalse(model.ShouldPrepare(100f, -1f));
        }

        [Test]
        public void ShortTracksAreNeverCrossfaded()
        {
            var model = New();

            // 25 秒の曲に 10 秒のフェードを掛けると、
            // 鳴っている時間の 4 割が「混ざっている最中」になる。
            Assert.IsFalse(model.ShouldPrepare(20f, 25f));

            // 30 秒あれば掛ける。
            Assert.IsTrue(model.ShouldPrepare(20f, 30f));
        }

        [Test]
        public void NothingHappensAfterTheTrackHasAlreadyEnded()
        {
            var model = New();

            // 行き過ぎているときは、終わりの合図(OnVideoEnd)に任せる。
            Assert.IsFalse(model.ShouldPrepare(200f, 200f));
            Assert.IsFalse(model.ShouldPrepare(205f, 200f));
        }

        [Test]
        public void PreparingOnlyHappensOnce()
        {
            var model = New();

            Assert.IsTrue(model.ShouldPrepare(190f, 200f));
            model.MarkPrepared();

            // もう読ませたので、次のフレームでまた読ませようとしない。
            Assert.IsFalse(model.ShouldPrepare(191f, 200f));
        }

        [Test]
        public void TurningTheFadeOffStopsItFromEverStarting()
        {
            var model = New();
            model.FadeSeconds = 0f;

            Assert.IsFalse(model.ShouldPrepare(190f, 200f));
        }

        // ───────── 進み方 ─────────

        [Test]
        public void TheFadeFinishesAfterExactlyTheFadeLength()
        {
            var model = New();
            model.BeginFade();

            Assert.IsFalse(model.Advance(5f), "半分ではまだ終わらない");
            Assert.IsFalse(model.Advance(4.9f));
            Assert.IsTrue(model.Advance(0.2f), "10 秒を越えたら終わり");
        }

        [Test]
        public void AdvancingDoesNothingWhileNotFading()
        {
            var model = New();

            Assert.IsFalse(model.Advance(100f), "混ぜていないのに終わったことにしない");
            Assert.AreEqual(0f, model.Elapsed);
        }

        [Test]
        public void ProgressGoesFromZeroToOne()
        {
            var model = New();
            model.BeginFade();

            Assert.AreEqual(0f, model.Progress, 0.001f);

            model.Advance(5f);
            Assert.AreEqual(0.5f, model.Progress, 0.001f);

            model.Advance(5f);
            Assert.AreEqual(1f, model.Progress, 0.001f);
        }

        [Test]
        public void ProgressNeverGoesPastOne()
        {
            var model = New();
            model.BeginFade();
            model.Advance(999f);

            Assert.AreEqual(1f, model.Progress, 0.001f);
        }

        [Test]
        public void ResettingPutsItBackToIdle()
        {
            var model = New();
            model.BeginFade();
            model.Advance(5f);

            model.Reset();

            Assert.IsTrue(model.IsIdle);
            Assert.AreEqual(0f, model.Progress);
            Assert.AreEqual(0f, model.Elapsed);
        }

        // ───────── 音の混ざり方(等パワー)─────────

        [Test]
        public void TheOutgoingTrackFadesFromFullToSilent()
        {
            Assert.AreEqual(1f, CrossfadeModel.OutgoingVolumeAt(0f), 0.01f);
            Assert.AreEqual(0f, CrossfadeModel.OutgoingVolumeAt(1f), 0.01f);
        }

        [Test]
        public void TheIncomingTrackFadesFromSilentToFull()
        {
            Assert.AreEqual(0f, CrossfadeModel.IncomingVolumeAt(0f), 0.01f);
            Assert.AreEqual(1f, CrossfadeModel.IncomingVolumeAt(1f), 0.01f);
        }

        [Test]
        public void TheTotalLoudnessStaysTheSameThroughoutTheFade()
        {
            // ここが「等パワー」の肝。
            // out² + in² が常に 1 なら、混ざっている最中も大きさが変わらない。
            // 素朴に 1-t と t で混ぜると、真ん中で 0.5 まで落ちて音が痩せる。
            for (int step = 0; step <= 10; step++)
            {
                float t = step / 10f;

                float outgoing = CrossfadeModel.OutgoingVolumeAt(t);
                float incoming = CrossfadeModel.IncomingVolumeAt(t);

                float power = outgoing * outgoing + incoming * incoming;

                Assert.AreEqual(1f, power, 0.02f,
                                "混ざっている最中に音の大きさが変わってはいけない(t = " + t + ")");
            }
        }

        [Test]
        public void TheMidPointIsLouderThanANaiveLinearFadeWouldBe()
        {
            // 真ん中では両方 0.707 付近。単純な 0.5 ずつより大きい。
            float half = CrossfadeModel.OutgoingVolumeAt(0.5f);

            Assert.Greater(half, 0.6f);
            Assert.Less(half, 0.8f);
        }

        [Test]
        public void VolumesAreClampedOutsideTheNormalRange()
        {
            Assert.AreEqual(1f, CrossfadeModel.OutgoingVolumeAt(-1f), 0.01f);
            Assert.AreEqual(0f, CrossfadeModel.OutgoingVolumeAt(2f), 0.01f);

            Assert.AreEqual(0f, CrossfadeModel.IncomingVolumeAt(-1f), 0.01f);
            Assert.AreEqual(1f, CrossfadeModel.IncomingVolumeAt(2f), 0.01f);
        }

        // ───────── 絵の混ざり方(そのまま)─────────

        [Test]
        public void TheVideoBlendIsPlainAndLinear()
        {
            var model = New();
            model.BeginFade();
            model.Advance(5f);

            // 絵は 2 枚を重ねるだけなので、真ん中はちょうど半分。
            // ここに音と同じ曲線を使うと、切り替わりが急に見える。
            Assert.AreEqual(0.5f, model.VideoBlend, 0.001f);
        }

        [Test]
        public void TheVideoBlendIsZeroWhileNotFading()
        {
            var model = New();

            Assert.AreEqual(0f, model.VideoBlend);
        }

        // ───────── ひとつながりの流れ ─────────

        [Test]
        public void AWholeFadeRunsThroughItsStatesInOrder()
        {
            var model = New();
            Assert.IsTrue(model.IsIdle);

            // 残り 10 秒。次の曲を裏で読ませる。
            Assert.IsTrue(model.ShouldPrepare(190f, 200f));
            model.MarkPrepared();
            Assert.IsTrue(model.IsPreparing);

            // 裏の曲が鳴り出した。混ぜ始める。
            model.BeginFade();
            Assert.IsTrue(model.IsFading);

            // 混ざっている最中。
            model.Advance(5f);
            Assert.AreEqual(0.5f, model.Progress, 0.001f);

            // 混ざり終わった。
            Assert.IsTrue(model.Advance(5f));

            model.Reset();
            Assert.IsTrue(model.IsIdle);
        }
    }
}
