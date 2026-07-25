using UnityEngine;

namespace SmartMediaPlatform.Integration.Demo
{
    /// <summary>
    /// Phase1 の最終統合テストをシーン上で実行し、結果を Console に出力する。
    ///
    /// Catalog → Recommendation → Queue → Backend を実際につないで一連の動作を確認する。
    /// 実際の音楽再生や VideoPlayer は使用しない(DummyBackend がログを出すだけ)。
    ///
    /// 使い方:
    ///   1. DemoScene を開いて Play を押す(推奨)
    ///   2. または空の GameObject にアタッチして Play
    ///   3. Play せずに実行したい場合は Inspector の ⋮ メニュー &gt; "Run Phase1 Integration Test"
    /// </summary>
    public sealed class Phase1IntegrationDemo : MonoBehaviour
    {
        [Header("シナリオ設定")]
        [Tooltip("おすすめとキュー投入の起点にする曲")]
        [SerializeField] private string _seedId = "music-001";

        [Tooltip("自動補充後に目指すキューの長さ")]
        [SerializeField] private int _queueTargetCount = 5;

        [Tooltip("乱数の種。固定しておくと毎回同じ結果になる")]
        [SerializeField] private int _randomSeed = 1;

        [Header("実行タイミング")]
        [Tooltip("Play を押したときに自動実行する")]
        [SerializeField] private bool _runOnStart = true;

        private void Start()
        {
            if (_runOnStart) RunIntegrationTest();
        }

        /// <summary>統合テストを実行し、手順のログと成功条件の判定を Console に出力する。</summary>
        [ContextMenu("Run Phase1 Integration Test")]
        public void RunIntegrationTest()
        {
            var scenario = new Phase1IntegrationScenario
            {
                SeedId = _seedId,
                QueueTargetCount = _queueTargetCount,
                RandomSeed = _randomSeed,
            };

            IntegrationReport report = scenario.Run();

            // 手順と判定をまとめて 1 件のログに出す(時系列で追いやすくするため)。
            string header = "===== Phase1 Integration Test / Smart Media Platform =====\n"
                            + "Catalog -> Recommendation -> Queue -> Backend\n"
                            + "(実際の再生は行いません / no actual playback)\n";
            Debug.Log(header + report);

            // 総合結果は 1 行で別途出す。失敗時は LogError にして Console で目立たせる。
            int total = report.PassedCount + report.FailedCount;
            if (report.AllPassed)
            {
                Debug.Log($"[Phase1 Integration] SUCCESS — {report.PassedCount}/{total} conditions passed");
            }
            else
            {
                Debug.LogError(
                    $"[Phase1 Integration] FAILED — {report.PassedCount}/{total} conditions passed\n"
                    + "失敗した条件:\n  - " + string.Join("\n  - ", report.Failures));
            }
        }
    }
}
