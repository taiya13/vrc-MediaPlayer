using System.Collections;
using System.Text;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Queue;
using UnityEngine;

namespace SmartMediaPlatform.Audio.Demo
{
    /// <summary>
    /// AudioBackend で「実際に音を鳴らしながら」動作を確認するデモ。
    ///
    /// 空の GameObject にアタッチして Play すると、Queue の曲を順に再生する。
    /// AudioSource と <see cref="AudioBackendHost"/> は自動で用意される。
    ///
    /// 音源ファイルが無くても確認できるよう、既定では ID ごとに短い音を自動生成する。
    /// 本物の音源を使う場合は <see cref="AudioBackendHost"/> の Clips に割り当てる。
    /// </summary>
    [RequireComponent(typeof(AudioBackendHost))]
    public sealed class AudioBackendConsoleDemo : MonoBehaviour
    {
        [Header("再生する曲(Catalog の ID)")]
        [SerializeField]
        private string[] _queueIds = { "music-001", "music-002", "music-003" };

        [Header("シナリオ")]
        [Tooltip("Play を押したときに自動実行する")]
        [SerializeField] private bool _runOnStart = true;

        [Tooltip("曲が終わるのを待って次へ進む回数")]
        [SerializeField] private int _tracksToPlay = 2;

        [Tooltip("1 曲の再生を待つ上限(秒)")]
        [SerializeField] private float _trackTimeout = 5f;

        private AudioBackendHost _host;
        private BackendManager _manager;
        private ListBackendLogger _logger;
        private IQueue _queue;
        private int _flushed;

        private void Start()
        {
            if (_runOnStart) StartCoroutine(RunScenario());
        }

        /// <summary>Play を押さずに、状態遷移だけ Console で確認する。</summary>
        [ContextMenu("Run Control Scenario (no waiting)")]
        public void RunControlScenario()
        {
            Build();

            var sb = new StringBuilder();
            sb.AppendLine("=== AudioBackend Control Scenario ===");

            Command(sb, $"Load {_queue.Peek().MediaId}");
            _manager.LoadCurrent();
            Flush(sb);

            Command(sb, "Play");
            _manager.Play();
            Flush(sb);
            sb.AppendLine($"  AudioSource.isPlaying = {_host.Source.isPlaying}");

            Command(sb, "Pause");
            _manager.Pause();
            Flush(sb);

            Command(sb, "Resume");
            _manager.Resume();
            Flush(sb);

            Command(sb, "Stop");
            _manager.Stop();
            Flush(sb);
            sb.Append($"  AudioSource.isPlaying = {_host.Source.isPlaying}");

            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// 実際に音を鳴らし、曲が終わる(Ended)のを待って次の曲へ進む。
        /// </summary>
        public IEnumerator RunScenario()
        {
            Build();

            var sb = new StringBuilder();
            sb.AppendLine("=== AudioBackend Playback Scenario (実際に音が鳴ります) ===");
            sb.AppendLine($"  Queue: {_queue.Count} 曲 / clips: {_host.Library.Count} 件");

            _manager.LoadCurrent();
            _manager.Play();
            Flush(sb);

            for (int track = 0; track < _tracksToPlay; track++)
            {
                float waited = 0f;

                // Ended になるまで待つ(AudioBackendHost が毎フレーム Tick している)
                while (_manager.GetState() == BackendState.Playing && waited < _trackTimeout)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                Flush(sb);
                sb.AppendLine($"  -> State: {_manager.GetState()} ({waited:0.00}s 経過)");

                if (_manager.GetState() != BackendState.Ended)
                {
                    sb.AppendLine("  タイムアウトしました。AudioSource が再生できているか確認してください。");
                    break;
                }

                // Ended を受けて次の曲へ。BackendManager は次を読み込むところまで行うので、
                // 続けて鳴らすために Play() を呼ぶ。
                if (_queue.Count <= 1)
                {
                    sb.AppendLine("  キューが尽きました。");
                    break;
                }

                sb.AppendLine("  次の曲へ:");
                _manager.Skip();
                _manager.Play();
                Flush(sb);
            }

            _manager.Stop();
            Flush(sb);
            sb.Append("=== 完了 ===");

            Debug.Log(sb.ToString());
        }

        private void Build()
        {
            _logger = new ListBackendLogger();
            _flushed = 0;

            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());

            _host = GetComponent<AudioBackendHost>();
            _host.EnsureBuilt(_logger);
            _host.PrepareClipsFor(catalog);

            _queue = new MediaQueue();
            foreach (var id in _queueIds)
            {
                var item = catalog.FindById(id);
                if (item == null)
                {
                    Debug.LogWarning($"[AudioBackendConsoleDemo] 不明な ID: {id}");
                    continue;
                }
                _queue.Enqueue(item);
            }

            _manager = new BackendManager(_queue, _logger);
            _manager.RegisterBackend(_host.Backend);
        }

        private static void Command(StringBuilder sb, string label)
        {
            sb.AppendLine();
            sb.AppendLine(label);
        }

        private void Flush(StringBuilder sb)
        {
            for (int i = _flushed; i < _logger.Lines.Count; i++)
            {
                sb.Append("  ").AppendLine(_logger.Lines[i]);
            }
            _flushed = _logger.Lines.Count;
        }
    }
}
