using System.Collections.Generic;
using System.Text;
using SmartMediaPlatform.Backend;

namespace SmartMediaPlatform.Integration
{
    /// <summary>
    /// 統合テストの実行記録。手順のログと成功条件の判定結果を 1 本のテキストにまとめる。
    ///
    /// <see cref="IBackendLogger"/> も実装しているので、Backend が出すログを
    /// そのままこのレポートに流し込める(手順とログが時系列で 1 つに並ぶ)。
    /// </summary>
    public sealed class IntegrationReport : IBackendLogger
    {
        private readonly List<string> _lines = new List<string>();

        public int PassedCount { get; private set; }
        public int FailedCount { get; private set; }

        public bool AllPassed => FailedCount == 0;

        public IReadOnlyList<string> Lines => _lines;

        /// <summary>判定に失敗した条件の一覧(失敗時の要約用)。</summary>
        public List<string> Failures { get; } = new List<string>();

        // --- 記録 ---

        /// <summary>見出し(手順の区切り)。</summary>
        public void Step(string title)
        {
            if (_lines.Count > 0) _lines.Add("");
            _lines.Add($"───── {title} ─────");
        }

        /// <summary>操作や中間結果の記録。</summary>
        public void Info(string message)
        {
            _lines.Add("  " + message);
        }

        /// <summary>Backend からのログ(<see cref="IBackendLogger"/> 実装)。</summary>
        public void Log(string message)
        {
            _lines.Add("    " + message);
        }

        /// <summary>
        /// 成功条件を 1 つ判定して記録する。
        /// これが統合テストの合否そのものになる。
        /// </summary>
        public bool Check(bool condition, string description)
        {
            if (condition)
            {
                PassedCount++;
                _lines.Add($"  [PASS] {description}");
            }
            else
            {
                FailedCount++;
                Failures.Add(description);
                _lines.Add($"  [FAIL] {description}");
            }
            return condition;
        }

        /// <summary>末尾に総合結果を付ける。</summary>
        public void Summarize()
        {
            int total = PassedCount + FailedCount;

            _lines.Add("");
            _lines.Add("═════ RESULT ═════");
            _lines.Add($"  {PassedCount} / {total} conditions passed");

            if (AllPassed)
            {
                _lines.Add("  ✅ Phase1 統合テスト: 成功");
                _lines.Add("     Catalog → Recommendation → Queue → Backend の連携をすべて確認しました。");
            }
            else
            {
                _lines.Add("  ❌ Phase1 統合テスト: 失敗");
                foreach (var failure in Failures)
                {
                    _lines.Add("     - " + failure);
                }
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _lines.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(_lines[i]);
            }
            return sb.ToString();
        }
    }
}
