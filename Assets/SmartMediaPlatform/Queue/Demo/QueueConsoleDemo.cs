using System.Text;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Recommendation;
using UnityEngine;

namespace SmartMediaPlatform.Queue.Demo
{
    /// <summary>
    /// Queue System の動作を Unity の Console だけで確認するデモ。VRChat SDK 不要。
    /// 空の GameObject にアタッチして Play するか、
    /// Inspector の ⋮ メニュー &gt; "Run Queue Scenario" / "Run Auto Refill Scenario" で実行。
    ///
    /// 再生は一切行わない(Player は Phase1-5 以降)。
    /// </summary>
    public sealed class QueueConsoleDemo : MonoBehaviour
    {
        [Header("最初にキューへ積む曲")]
        [SerializeField]
        private string[] _initialIds = { "music-001", "music-004", "music-010" };

        [Header("シナリオ中に Enqueue する曲")]
        [SerializeField] private string _enqueueId = "music-003";

        [Header("自動補充の設定")]
        [SerializeField] private string _seedId = "music-001";
        [SerializeField] private int _minimumCount = 2;
        [SerializeField] private int _targetCount = 5;

        private void Start()
        {
            RunQueueScenario();
        }

        /// <summary>
        /// 課題指定のシナリオ:Queue 表示 → Skip → Now/Next 表示 → Enqueue → Queue 表示。
        /// </summary>
        [ContextMenu("Run Queue Scenario")]
        public void RunQueueScenario()
        {
            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());
            IQueue queue = new MediaQueue();

            var sb = new StringBuilder();
            sb.AppendLine("=== Queue Scenario ===");

            foreach (var id in _initialIds)
            {
                var item = catalog.FindById(id);
                if (item == null)
                {
                    Debug.LogWarning($"[QueueConsoleDemo] 不明な ID: {id}");
                    continue;
                }
                queue.Enqueue(item);
            }

            sb.AppendLine(QueueFormatter.FormatQueue(queue));
            sb.AppendLine();

            sb.AppendLine("Skip");
            queue.Skip();
            sb.AppendLine();

            sb.AppendLine(QueueFormatter.FormatNowNext(queue));
            sb.AppendLine();

            sb.AppendLine("Enqueue");
            sb.AppendLine(_enqueueId);
            var enqueued = catalog.FindById(_enqueueId);
            if (enqueued != null) queue.Enqueue(enqueued);
            sb.AppendLine();

            sb.Append(QueueFormatter.FormatQueue(queue));

            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Recommendation Engine 連携の確認:
        /// キューが尽きそうになったら自動で補充されることを Console に出す(再生はしない)。
        /// </summary>
        [ContextMenu("Run Auto Refill Scenario")]
        public void RunAutoRefillScenario()
        {
            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());
            var engine = new RecommendationEngine(catalog, RecommendationRule.CreateDefault(), new System.Random());

            var queue = new MediaQueue();
            var manager = new QueueManager(queue, engine, catalog)
            {
                MinimumCount = _minimumCount,
                TargetCount = _targetCount,
            };

            var sb = new StringBuilder();
            sb.AppendLine("=== Auto Refill Scenario ===");

            var seed = catalog.FindById(_seedId);
            if (seed != null) queue.Enqueue(seed);
            sb.AppendLine(QueueFormatter.FormatQueueVerbose(queue, "Queue (initial)"));
            sb.AppendLine();

            int added = manager.EnsureFilled();
            sb.AppendLine($"EnsureFilled() -> {added} 件追加 (minimum={_minimumCount}, target={_targetCount})");
            sb.AppendLine(QueueFormatter.FormatQueueVerbose(queue, "Queue (after refill)"));
            sb.AppendLine();

            sb.AppendLine("SkipAndRefill()");
            manager.SkipAndRefill();
            sb.AppendLine(QueueFormatter.FormatNowNext(queue));
            sb.AppendLine();
            sb.Append(QueueFormatter.FormatQueueVerbose(queue, "Queue (after skip)"));

            Debug.Log(sb.ToString());
        }
    }
}
