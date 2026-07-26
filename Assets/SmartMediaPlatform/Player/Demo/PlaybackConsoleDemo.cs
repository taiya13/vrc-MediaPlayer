using System.Collections;
using System.Text;
using SmartMediaPlatform.Audio;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Queue;
using UnityEngine;

namespace SmartMediaPlatform.Player.Demo
{
    /// <summary>
    /// Phase2-2 の再生制御を Console だけで確認するデモ。
    ///
    /// 実際に音を鳴らしながら、
    /// Play / Pause / Resume / Stop / Next / Previous / Seek / Ended→自動送り /
    /// Queue 連携 / Backend 切替 をひととおり実行する。
    ///
    /// 空の GameObject にアタッチして Play すると走る
    /// (<see cref="AudioBackendHost"/> と AudioSource は自動で付く)。
    /// </summary>
    [RequireComponent(typeof(AudioBackendHost))]
    public sealed class PlaybackConsoleDemo : MonoBehaviour
    {
        [Header("再生する曲(Catalog の ID)")]
        [SerializeField]
        private string[] _queueIds = { "music-001", "music-002", "music-003" };

        [Header("Backend 切替の確認に使う(音源が無いので Dummy が担当する)")]
        [SerializeField] private string _otherTypeId = "video-001";

        [Header("実行")]
        [SerializeField] private bool _runOnStart = true;

        [Tooltip("Ended を待つ上限(秒)")]
        [SerializeField] private float _endedTimeout = 6f;

        private MediaPlayer _player;
        private ListBackendLogger _logger;
        private IQueue _queue;
        private AudioBackendHost _host;
        private int _flushed;
        private StringBuilder _out;

        private void Start()
        {
            if (_runOnStart) StartCoroutine(RunAll());
        }

        [ContextMenu("Run Control Scenario (no waiting)")]
        public void RunControlScenarioFromMenu()
        {
            Build();
            _out = new StringBuilder();
            _out.AppendLine("=== Playback Control Scenario ===");
            RunControlScenario();
            Debug.Log(_out.ToString());
        }

        public IEnumerator RunAll()
        {
            Build();
            _out = new StringBuilder();
            _out.AppendLine("=== Phase2-2 Playback Control Demo (実際に音が鳴ります) ===");

            RunControlScenario();

            yield return RunEndedScenario();

            RunBackendSwitchScenario();

            _out.Append("=== 完了 ===");
            Debug.Log(_out.ToString());
        }

        /// <summary>Play / Pause / Resume / Next / Previous / Seek / Stop。</summary>
        private void RunControlScenario()
        {
            Step("Play");
            _player.Play();
            Report();

            Step("Pause");
            _player.Pause();
            Report();

            Step("Resume");
            _player.Resume();
            Report();

            Step("TogglePlayPause (再生中なので一時停止)");
            _player.TogglePlayPause();
            Report();

            Step("TogglePlayPause (一時停止中なので再開)");
            _player.TogglePlayPause();
            Report();

            Step("SkipNext");
            _player.SkipNext();
            Report();

            Step("SkipNext");
            _player.SkipNext();
            Report();

            Step("SkipPrevious");
            _player.SkipPrevious();
            Report();

            Step($"Seek 0.5 (CanSeek = {_player.CanSeek()})");
            _player.Seek(0.5f);
            Report();

            Step("Stop");
            _player.Stop();
            Report();

            Step("Play (停止後にもう一度)");
            _player.Play();
            Report();
        }

        /// <summary>曲の終わりを待ち、自動で次の曲へ移ることを確認する。</summary>
        private IEnumerator RunEndedScenario()
        {
            Step("Ended → 自動で次の曲へ(曲が終わるまで待ちます)");

            string before = _player.GetCurrent() != null ? _player.GetCurrent().Id : "(none)";
            float waited = 0f;

            // 自動送りが働くと別の曲になる。最後の曲なら Ended のまま止まる。
            while (waited < _endedTimeout)
            {
                waited += Time.deltaTime;

                var current = _player.GetCurrent();
                string now = current != null ? current.Id : "(none)";
                if (now != before) break;
                if (_player.GetState() == BackendState.Ended) break;

                yield return null;
            }

            Flush();
            _out.AppendLine($"  {waited:0.00} 秒待機 -> {_player.Describe()}");
        }

        /// <summary>種別の違うメディアで、担当バックエンドが自動的に切り替わることを確認する。</summary>
        private void RunBackendSwitchScenario()
        {
            Step($"Backend 切替: {_otherTypeId} をキューの次に入れて SkipNext");

            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());
            var other = catalog.FindById(_otherTypeId);
            if (other == null)
            {
                _out.AppendLine($"  不明な ID: {_otherTypeId}");
                return;
            }

            _queue.EnqueueNext(other);
            _player.SkipNext();
            Flush();

            var active = _player.Manager.ActiveBackend;
            _out.AppendLine($"  Active backend -> {(active != null ? active.Name : "(none)")}");
            _out.AppendLine($"  CanSeek -> {_player.CanSeek()} (Dummy はシークに対応しない)");
            _out.AppendLine($"  {_player.Describe()}");
        }

        // --- 組み立て / 出力 ---

        private void Build()
        {
            _logger = new ListBackendLogger();
            _flushed = 0;

            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());

            // シーンに置かれていなくても動くよう、必要なら自分で用意する
            // (AudioBackendHost 側の RequireComponent により AudioSource も付く)。
            _host = GetComponent<AudioBackendHost>();
            if (_host == null) _host = gameObject.AddComponent<AudioBackendHost>();

            _host.EnsureBuilt(_logger);
            _host.PrepareClipsFor(catalog);

            _queue = new MediaQueue();
            foreach (var id in _queueIds)
            {
                var item = catalog.FindById(id);
                if (item == null)
                {
                    Debug.LogWarning($"[PlaybackConsoleDemo] 不明な ID: {id}");
                    continue;
                }
                _queue.Enqueue(item);
            }

            var manager = new BackendManager(_queue, _logger);
            _player = new MediaPlayer(manager, _logger);

            // Music は AudioBackend、それ以外は DummyBackend が担当する。
            _player.RegisterBackend(_host.Backend);
            _player.RegisterBackend(new DummyBackend("DummyBackend", _logger));
        }

        private void Step(string label)
        {
            _out.AppendLine();
            _out.AppendLine($"── {label}");
        }

        private void Report()
        {
            Flush();
            _out.AppendLine($"  => {_player.Describe()}");
        }

        private void Flush()
        {
            for (int i = _flushed; i < _logger.Lines.Count; i++)
            {
                _out.Append("  ").AppendLine(_logger.Lines[i]);
            }
            _flushed = _logger.Lines.Count;
        }
    }
}
