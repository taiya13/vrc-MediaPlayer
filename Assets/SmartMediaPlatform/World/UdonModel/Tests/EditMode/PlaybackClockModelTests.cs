using NUnit.Framework;
using SmartMediaPlatform.World.UdonModel;

namespace SmartMediaPlatform.World.UdonModel.Tests
{
    /// <summary>
    /// Phase5-4: 再生位置の時計の検証。
    ///
    /// 実機で動くのは <c>UdonSyncCoordinator</c> ですが、
    /// <c>UdonSharpBehaviour</c> は EditMode から触れないので、
    /// <b>同じ数え方を写した <see cref="PlaybackClockModel"/> をここで確かめます</b>
    /// (Phase5-2 の <c>PlaybackModel</c>、Phase5-3 の <c>ListPageModel</c> と同じ手順)。
    /// </summary>
    public sealed class PlaybackClockModelTests
    {
        private const int T0 = 1000000;

        // ───────── 再生中は進む ─────────

        [Test]
        public void PlaybackAdvancesWithServerTime()
        {
            var clock = new PlaybackClockModel();
            clock.PlayFrom(T0, 0);

            Assert.AreEqual(0, clock.PositionMsAt(T0));
            Assert.AreEqual(5000, clock.PositionMsAt(T0 + 5000));
            Assert.AreEqual(90000, clock.PositionMsAt(T0 + 90000));
        }

        [Test]
        public void PlaybackCanStartPartWayThrough()
        {
            // 途中から流し始めた場合(seek した直後など)
            var clock = new PlaybackClockModel();
            clock.PlayFrom(T0, 30000);

            Assert.AreEqual(30000, clock.PositionMsAt(T0));
            Assert.AreEqual(40000, clock.PositionMsAt(T0 + 10000));
        }

        // ───────── 止めたら進まない ─────────

        [Test]
        public void PauseHoldsThePosition()
        {
            var clock = new PlaybackClockModel();
            clock.PauseAt(T0, 42000);

            Assert.AreEqual(42000, clock.PositionMsAt(T0));
            Assert.AreEqual(42000, clock.PositionMsAt(T0 + 60000),
                            "1 分待っても一時停止中は動かない");
        }

        [Test]
        public void ResumingContinuesFromWhereItStopped()
        {
            var clock = new PlaybackClockModel();
            clock.PlayFrom(T0, 0);

            int pausedAt = clock.PositionMsAt(T0 + 20000);
            clock.PauseAt(T0 + 20000, pausedAt);

            // 30 秒放置してから再開
            clock.PlayFrom(T0 + 50000, pausedAt);

            Assert.AreEqual(20000, clock.PositionMsAt(T0 + 50000), "止めた場所から続く");
            Assert.AreEqual(25000, clock.PositionMsAt(T0 + 55000));
        }

        // ───────── 途中参加 ─────────

        [Test]
        public void ALateJoinerLandsAtTheSamePlaceAsEveryoneElse()
        {
            // 先にいる人:T0 に頭から再生を始めた
            var host = new PlaybackClockModel();
            host.PlayFrom(T0, 0);

            // 2 分後に入ってきた人が、同期された 3 つの値だけを受け取る
            var joiner = new PlaybackClockModel();
            joiner.Apply(host.BaseServerTime, host.BasePositionMs, host.IsPlaying);

            int now = T0 + 120000;
            Assert.AreEqual(host.PositionMsAt(now), joiner.PositionMsAt(now));
            Assert.AreEqual(120000, joiner.PositionMsAt(now));
        }

        [Test]
        public void ALateJoinerSeesAPausedPlayerAsPaused()
        {
            var host = new PlaybackClockModel();
            host.PauseAt(T0, 15000);

            var joiner = new PlaybackClockModel();
            joiner.Apply(host.BaseServerTime, host.BasePositionMs, host.IsPlaying);

            Assert.IsFalse(joiner.IsPlaying);
            Assert.AreEqual(15000, joiner.PositionMsAt(T0 + 300000));
        }

        // ───────── サーバー時刻の一周 ─────────

        [Test]
        public void TheClockSurvivesTheServerTimeWrappingAround()
        {
            // Networking.GetServerTimeInMilliseconds() は int。約 24.8 日で一周する。
            // 大小比較をすると「時間が巻き戻った」と誤解するので、差だけを見ている。
            int justBeforeWrap = int.MaxValue - 999;

            var clock = new PlaybackClockModel();
            clock.PlayFrom(justBeforeWrap, 10000);

            // 一周ちょうどまたいだところ(999 進んで、さらに 1 で折り返す = 1000 ms)
            Assert.AreEqual(11000, clock.PositionMsAt(int.MinValue));
        }

        [Test]
        public void TheClockKeepsAdvancingAfterTheWrap()
        {
            int justBeforeWrap = int.MaxValue - 999;

            var clock = new PlaybackClockModel();
            clock.PlayFrom(justBeforeWrap, 0);

            Assert.AreEqual(1000, clock.PositionMsAt(int.MinValue));
            Assert.AreEqual(6000, clock.PositionMsAt(int.MinValue + 5000));
        }

        // ───────── ずれの判定 ─────────

        [Test]
        public void SmallDriftIsLeftAlone()
        {
            var clock = new PlaybackClockModel();
            clock.PlayFrom(T0, 0);

            int now = T0 + 30000;

            Assert.IsFalse(clock.NeedsCorrection(now, 30400, 1000), "0.4 秒のずれは直さない");
            Assert.IsFalse(clock.NeedsCorrection(now, 29600, 1000));
        }

        [Test]
        public void LargeDriftIsCorrectedInBothDirections()
        {
            var clock = new PlaybackClockModel();
            clock.PlayFrom(T0, 0);

            int now = T0 + 30000;

            Assert.IsTrue(clock.NeedsCorrection(now, 33000, 1000), "進みすぎ");
            Assert.IsTrue(clock.NeedsCorrection(now, 27000, 1000), "遅れすぎ");
        }

        [Test]
        public void APausedPlayerIsNeverCorrected()
        {
            var clock = new PlaybackClockModel();
            clock.PauseAt(T0, 10000);

            Assert.IsFalse(clock.NeedsCorrection(T0, 999999, 1000),
                           "一時停止中に seek すると、ちらつくだけで得がない");
        }

        // ───────── 端の扱い ─────────

        [Test]
        public void ThePositionNeverGoesNegative()
        {
            var clock = new PlaybackClockModel();
            clock.PlayFrom(T0, 0);

            // 時刻がわずかに巻き戻って見えることがある(同期の到着順など)
            Assert.AreEqual(0, clock.PositionMsAt(T0 - 5000));
        }

        [Test]
        public void ANegativeStartPositionIsTreatedAsZero()
        {
            var clock = new PlaybackClockModel();
            clock.PlayFrom(T0, -500);

            Assert.AreEqual(0, clock.BasePositionMs);
        }

        [Test]
        public void RestartGoesBackToTheHead()
        {
            var clock = new PlaybackClockModel();
            clock.PlayFrom(T0, 60000);

            clock.Restart(T0 + 1000);

            Assert.AreEqual(0, clock.PositionMsAt(T0 + 1000));
            Assert.IsTrue(clock.IsPlaying);
        }

        // ───────── 何を知らないか ─────────

        [Test]
        public void TheClockNeverKnowsWhatIsPlaying()
        {
            var type = typeof(PlaybackClockModel);

            Assert.IsNull(type.GetProperty("Url"));
            Assert.IsNull(type.GetProperty("Title"));
            Assert.IsNull(type.GetProperty("CurrentIndex"),
                          "扱うのは時刻と位置だけ — 何が鳴っているかは知らない");
        }
    }
}
