using System.Linq;
using NUnit.Framework;

namespace SmartMediaPlatform.Integration.Tests
{
    /// <summary>
    /// Phase1 最終統合テスト。
    /// DemoScene で実行されるのとまったく同じシナリオを EditMode でも回し、
    /// すべての成功条件を満たすことを CI でも検証できるようにする。
    /// </summary>
    public sealed class Phase1IntegrationTests
    {
        private IntegrationReport Run()
        {
            return new Phase1IntegrationScenario().Run();
        }

        [Test]
        public void AllIntegrationConditions_Pass()
        {
            var report = Run();

            Assert.IsTrue(report.AllPassed,
                "統合テストの失敗条件:\n  - " + string.Join("\n  - ", report.Failures)
                + "\n\n--- レポート全文 ---\n" + report);
        }

        [Test]
        public void Report_ContainsEveryStep()
        {
            string text = Run().ToString();

            StringAssert.Contains("STEP 1 / Catalog", text);
            StringAssert.Contains("STEP 2 / Recommendation", text);
            StringAssert.Contains("STEP 3 / Queue", text);
            StringAssert.Contains("STEP 4 / Backend", text);
            StringAssert.Contains("STEP 5 / End-to-End", text);
        }

        [Test]
        public void Report_ContainsResultSummary()
        {
            var report = Run();
            string text = report.ToString();

            StringAssert.Contains("RESULT", text);
            StringAssert.Contains($"{report.PassedCount} / {report.PassedCount + report.FailedCount} conditions passed", text);
        }

        [Test]
        public void EveryConditionIsReportedWithVerdict()
        {
            var report = Run();
            int verdicts = report.Lines.Count(l => l.Contains("[PASS]") || l.Contains("[FAIL]"));

            Assert.AreEqual(report.PassedCount + report.FailedCount, verdicts,
                "すべての成功条件が Console に判定付きで出力される");
            Assert.Greater(report.PassedCount, 0);
        }

        [Test]
        public void BackendLogs_AppearInReport_WithoutActualPlayback()
        {
            string text = Run().ToString();

            StringAssert.Contains("[MusicBackend] Load music-001", text);
            StringAssert.Contains("[MusicBackend] Play music-001", text);
            StringAssert.Contains("no actual playback", text);
        }

        [Test]
        public void Scenario_IsDeterministic()
        {
            // 種を固定しているので、何度実行しても同じレポートになる。
            Assert.AreEqual(Run().ToString(), Run().ToString());
        }

        [Test]
        public void Scenario_RespectsCustomSeedItem()
        {
            var scenario = new Phase1IntegrationScenario { SeedId = "music-003" };
            var report = scenario.Run();

            StringAssert.Contains("music-003", report.ToString());
        }

        [Test]
        public void Scenario_RespectsQueueTargetCount()
        {
            var scenario = new Phase1IntegrationScenario { QueueTargetCount = 4 };
            var report = scenario.Run();

            Assert.IsTrue(report.AllPassed,
                "キュー長を変えても全条件を満たす:\n  - " + string.Join("\n  - ", report.Failures));
            StringAssert.Contains("キューが目標件数(4)まで補充されている", report.ToString());
        }

        [Test]
        public void UnknownSeed_IsReportedAsFailureNotCrash()
        {
            // 壊れた設定でも例外を投げず、失敗として報告されることを確認する。
            var scenario = new Phase1IntegrationScenario { SeedId = "no-such-id" };

            IntegrationReport report = null;
            Assert.DoesNotThrow(() => report = scenario.Run());
            Assert.IsFalse(report.AllPassed);
            Assert.IsNotEmpty(report.Failures);
        }
    }
}
