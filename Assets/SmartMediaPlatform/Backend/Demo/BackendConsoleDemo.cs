using System.Text;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Queue;
using UnityEngine;

namespace SmartMediaPlatform.Backend.Demo
{
    /// <summary>
    /// Backend Interface の動作を Unity の Console だけで確認するデモ。VRChat SDK 不要。
    ///
    /// 実際の再生は行わない(DummyBackend はログを出すだけ)。
    /// 空の GameObject にアタッチして Play するか、
    /// Inspector の ⋮ メニュー &gt; 各シナリオで実行。
    /// </summary>
    public sealed class BackendConsoleDemo : MonoBehaviour
    {
        [Header("キューに積む曲(先頭が最初に読み込まれる)")]
        [SerializeField]
        private string[] _queueIds = { "music-001", "music-004", "music-010" };

        [Header("種別ごとにバックエンドを分けるか(拡張性の確認)")]
        [SerializeField] private bool _useSeparateBackends = true;

        private ListBackendLogger _logger;
        private StringBuilder _output;
        private int _flushed;

        private void Start()
        {
            RunBackendScenario();
        }

        /// <summary>
        /// 課題指定のシナリオ:
        /// Load → Play → Current → Pause → Resume → Skip → Current → Stop
        /// </summary>
        [ContextMenu("Run Backend Scenario")]
        public void RunBackendScenario()
        {
            var manager = BuildManager(out IQueue queue);

            _output = new StringBuilder();
            _output.AppendLine("=== Backend Scenario (no actual playback) ===");

            var head = queue.Peek();
            Command($"Load {(head != null ? head.MediaId : "(empty)")}");
            manager.LoadCurrent();
            Flush();

            Command("Play");
            manager.Play();
            Flush();

            ShowCurrent(manager);

            Command("Pause");
            manager.Pause();
            Flush();

            Command("Resume");
            manager.Resume();
            Flush();

            Command("Skip");
            manager.Skip();
            Flush();

            ShowCurrent(manager);

            Command("Stop");
            manager.Stop();
            Flush();

            _output.Append("State: ").Append(manager.GetState());

            Debug.Log(_output.ToString());
        }

        /// <summary>
        /// 拡張性の確認:Music と Video で別のバックエンドが選ばれることを見る。
        /// </summary>
        [ContextMenu("Run Backend Selection Scenario")]
        public void RunBackendSelectionScenario()
        {
            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());
            var logger = new ListBackendLogger();

            IQueue queue = new MediaQueue();
            queue.Enqueue(catalog.FindById("music-001"));
            queue.Enqueue(catalog.FindById("video-001"));
            queue.Enqueue(catalog.FindById("podcast-001"));

            var manager = new BackendManager(queue, logger);
            manager.RegisterBackend(new DummyBackend("MusicBackend", logger, MediaType.Music));
            manager.RegisterBackend(new DummyBackend("VideoBackend", logger, MediaType.Video, MediaType.Live));

            var sb = new StringBuilder();
            sb.AppendLine("=== Backend Selection (CanPlay) ===");

            foreach (var entry in queue.GetAll())
            {
                var backend = manager.SelectBackendFor(entry.Item);
                sb.Append(entry.MediaId).Append("  (").Append(entry.Item.Type).Append(")  -> ")
                  .AppendLine(backend != null ? backend.Name : "(扱えるバックエンドなし)");
            }

            sb.AppendLine();
            sb.AppendLine("Load + Play + Skip:");
            manager.LoadCurrent();
            manager.Play();
            manager.Skip();

            foreach (var line in logger.Lines) sb.AppendLine("  " + line);
            sb.Append("Active backend: ")
              .Append(manager.ActiveBackend != null ? manager.ActiveBackend.Name : "(none)");

            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// 自然終了(Ended)を受けて自動で次へ進む配線の確認。
        /// 実機バックエンドでは再生完了時に同じ通知が飛ぶ。
        /// </summary>
        [ContextMenu("Run Auto Advance Scenario")]
        public void RunAutoAdvanceScenario()
        {
            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());
            var logger = new ListBackendLogger();

            IQueue queue = new MediaQueue();
            foreach (var id in _queueIds)
            {
                var item = catalog.FindById(id);
                if (item != null) queue.Enqueue(item);
            }

            var backend = new DummyBackend("DummyBackend", logger);
            var manager = new BackendManager(queue, logger) { AutoAdvanceOnEnded = true };
            manager.RegisterBackend(backend);

            var sb = new StringBuilder();
            sb.AppendLine("=== Auto Advance on Ended ===");

            manager.LoadCurrent();
            manager.Play();
            sb.Append("Current: ").AppendLine(Describe(manager.GetCurrent()));

            backend.SimulateEnded();   // 実機なら「再生が終わった」タイミング
            sb.Append("After Ended -> Current: ").AppendLine(Describe(manager.GetCurrent()));
            sb.Append("State: ").AppendLine(manager.GetState().ToString());

            sb.AppendLine();
            foreach (var line in logger.Lines) sb.AppendLine("  " + line);

            Debug.Log(sb.ToString().TrimEnd());
        }

        // --- 組み立て / 出力ヘルパ ---

        private BackendManager BuildManager(out IQueue queue)
        {
            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());
            _logger = new ListBackendLogger();
            _flushed = 0;

            queue = new MediaQueue();
            foreach (var id in _queueIds)
            {
                var item = catalog.FindById(id);
                if (item == null)
                {
                    Debug.LogWarning($"[BackendConsoleDemo] 不明な ID: {id}");
                    continue;
                }
                queue.Enqueue(item);
            }

            var manager = new BackendManager(queue, _logger);
            if (_useSeparateBackends)
            {
                manager.RegisterBackend(new DummyBackend("MusicBackend", _logger, MediaType.Music));
                manager.RegisterBackend(new DummyBackend("VideoBackend", _logger, MediaType.Video, MediaType.Live));
                manager.RegisterBackend(new DummyBackend("PodcastBackend", _logger, MediaType.Podcast));
            }
            else
            {
                manager.RegisterBackend(new DummyBackend("DummyBackend", _logger));
            }
            return manager;
        }

        private void Command(string label)
        {
            _output.AppendLine();
            _output.AppendLine(label);
        }

        /// <summary>直前のコマンドで新しく増えたログ行だけを出力する。</summary>
        private void Flush()
        {
            for (int i = _flushed; i < _logger.Lines.Count; i++)
            {
                _output.Append("  ").AppendLine(_logger.Lines[i]);
            }
            _flushed = _logger.Lines.Count;
        }

        private void ShowCurrent(BackendManager manager)
        {
            _output.AppendLine();
            _output.AppendLine("Current");
            _output.AppendLine(Describe(manager.GetCurrent()));
        }

        private static string Describe(MediaItem item)
        {
            return item != null ? item.Id : "(none)";
        }
    }
}
