using System.Collections;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Integration;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using UnityEngine;

namespace SmartMediaPlatform.Audio.Demo
{
    /// <summary>
    /// Phase2-1 の統合デモ。
    /// Catalog → Recommendation → Queue → AudioBackend → AudioSource の経路を
    /// 実際に音を鳴らしながら確認し、成功条件を Console に出力する。
    ///
    /// Phase1 の統合テスト(DummyBackend)と同じ <see cref="IntegrationReport"/> を使うので、
    /// 出力の見た目と判定の作法は揃っている。
    /// </summary>
    [RequireComponent(typeof(AudioBackendHost))]
    public sealed class Phase2AudioIntegrationDemo : MonoBehaviour
    {
        [Header("シナリオ設定")]
        [SerializeField] private string _seedId = "music-001";
        [SerializeField] private int _queueTargetCount = 4;
        [SerializeField] private bool _runOnStart = true;

        [Tooltip("曲の終わり(Ended)を待つ上限(秒)")]
        [SerializeField] private float _endedTimeout = 5f;

        private void Start()
        {
            if (_runOnStart) StartCoroutine(RunIntegration());
        }

        [ContextMenu("Run Phase2 Audio Integration")]
        public void RunFromMenu()
        {
            StartCoroutine(RunIntegration());
        }

        public IEnumerator RunIntegration()
        {
            var report = new IntegrationReport();

            // ── STEP 1-3: Catalog → Recommendation → Queue(Phase1 と同じ経路)
            report.Step("STEP 1-3 / Catalog → Recommendation → Queue");

            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            var engine = new RecommendationEngine(
                catalog,
                new[]
                {
                    RecommendationRule.Related(10.0),
                    RecommendationRule.SameArtist(5.0),
                    RecommendationRule.SameGenre(3.0),
                    RecommendationRule.TagMatch(1.0),
                    RecommendationRule.Random(0.0),
                },
                new System.Random(1));

            var queue = new MediaQueue();
            var queueManager = new QueueManager(queue, engine, catalog)
            {
                MinimumCount = 2,
                TargetCount = _queueTargetCount,
            };

            var seed = catalog.FindById(_seedId);
            if (seed == null)
            {
                report.Check(false, $"起点の曲 \"{_seedId}\" がカタログに存在する");
                report.Summarize();
                Debug.LogError(Header() + report);
                yield break;
            }

            queue.Enqueue(seed);
            int added = queueManager.EnsureFilled();
            report.Info($"Queue を構築しました (起点: {_seedId} / 自動補充: {added} 件 / 合計: {queue.Count})");
            report.Info(QueueFormatter.FormatQueue(queue).Replace("\n", "\n  "));

            report.Check(queue.Count == _queueTargetCount, "Recommendation から Queue が構築される");

            // ── STEP 4: AudioBackend を登録(BackendManager は Phase1 のまま)
            report.Step("STEP 4 / AudioBackend — BackendManager は変更していない");

            var host = GetComponent<AudioBackendHost>();
            host.EnsureBuilt(report);
            int generated = host.PrepareClipsFor(catalog);
            report.Info($"AudioClip を用意しました (自動生成: {generated} 件 / 合計: {host.Library.Count} 件)");

            var manager = new BackendManager(queue, report);
            manager.RegisterBackend(host.Backend);

            var head = queue.Peek();
            report.Check(manager.SelectBackendFor(head.Item) == (IMediaBackend)host.Backend,
                "BackendManager が AudioBackend を選ぶ(コード変更なしで差し替わる)");
            report.Check(host.Library.Contains(head.Item),
                "Queue の先頭に対応する AudioClip が登録されている");

            // ── STEP 5: 実際に再生する
            report.Step("STEP 5 / 実際の再生 — Queue → Backend → AudioSource");

            report.Check(manager.LoadCurrent(), "Queue の先頭を AudioBackend に読み込める");
            report.Check(manager.Play(), "Play が成功する");
            report.Check(manager.GetState() == BackendState.Playing, "State が Playing になる");

            // 1 フレーム待って AudioSource が実際に鳴り始めたことを確認する
            yield return null;
            report.Info($"AudioSource.clip = {(host.Source.clip != null ? host.Source.clip.name : "(none)")}");
            report.Check(host.Source.clip != null, "AudioSource に AudioClip が渡っている");
            report.Check(ReferenceEquals(host.Source.clip, host.Library.Get(head.Item)),
                "AudioSource のクリップは Queue の曲に紐づいたもの");
            report.Check(host.Source.isPlaying, "AudioSource が実際に再生している(音が鳴っている)");

            // ── STEP 6: 曲の終わりを待って Ended を確認
            report.Step("STEP 6 / Ended — 曲の終わりが通知される");

            string playingId = manager.GetCurrent().Id;
            float waited = 0f;
            while (manager.GetState() == BackendState.Playing && waited < _endedTimeout)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            report.Info($"{waited:0.00} 秒待機しました (State: {manager.GetState()})");
            report.Check(manager.GetState() == BackendState.Ended,
                "曲が最後まで再生されると Ended になる");
            report.Check(!host.Source.isPlaying, "AudioSource は停止している");

            // ── STEP 7: 次の曲へ進んで再生を続けられる
            report.Step("STEP 7 / 継続再生 — 次の曲へ");

            if (queue.Count > 1)
            {
                manager.Skip();
                report.Check(manager.GetCurrent().Id != playingId, "Skip で次の曲へ進む");
                report.Check(manager.Play(), "次の曲を再生できる");

                yield return null;
                report.Check(host.Source.isPlaying, "次の曲も実際に鳴っている");

                manager.Stop();
                report.Check(manager.GetState() == BackendState.Stopped, "Stop で停止する");
                report.Check(!host.Source.isPlaying, "AudioSource も停止する");
            }
            else
            {
                report.Info("キューが 1 曲しかないため継続再生の確認は省略しました。");
            }

            report.Summarize();

            int total = report.PassedCount + report.FailedCount;
            if (report.AllPassed)
            {
                Debug.Log(Header() + report);
                Debug.Log($"[Phase2 Audio Integration] SUCCESS — {report.PassedCount}/{total} conditions passed");
            }
            else
            {
                Debug.LogError(Header() + report
                    + $"\n[Phase2 Audio Integration] FAILED — {report.PassedCount}/{total} conditions passed");
            }
        }

        private static string Header()
        {
            return "===== Phase2-1 Audio Integration / Smart Media Platform =====\n"
                   + "Catalog -> Recommendation -> Queue -> AudioBackend -> AudioSource\n"
                   + "(実際に音が鳴ります / real playback)\n";
        }
    }
}
