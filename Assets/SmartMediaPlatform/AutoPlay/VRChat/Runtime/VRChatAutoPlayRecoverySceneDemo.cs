using System.Text;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;
using SmartMediaPlatform.Video;
using SmartMediaPlatform.Video.Data;
using SmartMediaPlatform.Video.VRChat;
using UnityEngine;

namespace SmartMediaPlatform.AutoPlay.VRChat
{
    /// <summary>
    /// <b>Phase3-4 の DemoScene の進行役(VRChat SDK で実際に動画を再生する版)。</b>
    ///
    /// Phase3-3 の <see cref="VRChatAutoPlaySceneDemo"/> は
    /// <b>Ended だけ</b>で次へ進むことを見せるものでした。
    /// こちらは <see cref="AutoPlayController"/> を挟んで
    /// <b>Ended / Error / Timeout のどれが来ても止まらない</b>ことを見せます。
    ///
    /// <b>失敗をわざと起こす仕掛け</b>
    /// シーン作成時に<b>一部の動画だけ</b> URL を焼き込んであります。
    /// 焼かれていない動画に当たると <c>VRChatVideoBackend.Load</c> が
    /// <c>InvalidUrl</c> で失敗します — 実機で URL が切れたときと同じ経路です。
    /// <b>実行時に VRCUrl は作りません</b>(作れません)。
    ///
    /// 進行のきっかけは実機のイベントと <see cref="Tick"/> だけです。
    /// このクラスは最初の <c>Start()</c> 以降、再生の指示を出しません。
    /// </summary>
    [RequireComponent(typeof(VRChatVideoBackendHost))]
    public sealed class VRChatAutoPlayRecoverySceneDemo : MonoBehaviour
    {
        [Header("おすすめ再生")]
        [Tooltip("起点にする動画の Catalog ID。空ならカタログの先頭")]
        [SerializeField] private string _seedId = "video-001";

        [Header("実行")]
        [SerializeField] private bool _runOnStart = true;

        [Tooltip("1 本ごとに、終端付近までシークして早送りする(0 で無効)")]
        [SerializeField] private float _seekNearEndTailSeconds = 3f;

        [Header("見張り")]
        [Tooltip("読み込みがこの秒数で終わらなければ次の候補へ進む")]
        [SerializeField] private float _loadWatchdogSeconds = 15f;

        [Tooltip("失敗した動画をこの秒数は積み直さない")]
        [SerializeField] private float _failureCooldownSeconds = 60f;

        [Header("表示")]
        [Tooltip("この秒数ごとに Console へ経過を出す(0 で出さない)")]
        [SerializeField] private float _reportIntervalSeconds = 10f;

        private VRChatVideoBackendHost _host;
        private UdonVideoEventPump _pump;
        private AutoPlayController _controller;
        private PlayerSession _session;
        private VRChatVideoBackend _backend;
        private ListBackendLogger _logger;

        private bool _running;
        private string _seeking;
        private float _reportTimer;
        private int _flushed;

        /// <summary>組み立て済みの再生制御。</summary>
        public AutoPlayController Controller => _controller;

        /// <summary>おすすめ補充の接続層。</summary>
        public RecommendationPlaybackService Playback => _controller != null ? _controller.Playback : null;

        private void Start()
        {
            if (_runOnStart) Run();
        }

        [ContextMenu("Run Auto Play Recovery")]
        public void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[VRChatAutoPlayRecoverySceneDemo] Play 中に実行してください。");
                return;
            }

            _logger = new ListBackendLogger();
            _flushed = 0;

            _host = GetComponent<VRChatVideoBackendHost>();
            _pump = GetComponent<UdonVideoEventPump>();

            var adapter = _host.EnsureBuilt(_logger);
            if (adapter == null)
            {
                Debug.LogError("[VRChatAutoPlayRecoverySceneDemo] Backend を組み立てられませんでした。");
                return;
            }

            _backend = _host.Backend;

            // ── 組み立て(Phase3-3 と同じ並び。挟まるのは AutoPlayController だけ)
            IMediaCatalog catalog = new MediaCatalog(new VideoCatalogSource());
            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, _logger);
            var mediaPlayer = new MediaPlayer(backendManager, _logger);
            var engine = new RecommendationEngine(catalog, RecommendationRule.CreateDefault());
            _session = new PlayerSession(
                "vrchat-autoplay-recovery", mediaPlayer, catalog, engine, new System.Random(1), _logger);

            _controller = AutoPlayController.Create(
                _session, catalog, engine, new BackendPlaybackFilter(backendManager), _logger);
            _controller.LoadWatchdogSeconds = _loadWatchdogSeconds;
            _controller.Failures.CooldownSeconds = _failureCooldownSeconds;
            _controller.RegisterBackend(adapter);

            var sb = new StringBuilder();
            sb.AppendLine("=== Phase3-4 Playback Orchestration & Recovery (VRChat SDK DemoScene) ===");
            sb.AppendLine();
            sb.AppendLine("── 0. 構成");
            sb.AppendLine($"  動画プレイヤー : {(_host.VideoPlayer != null ? _host.VideoPlayer.GetType().Name : "(none)")}");
            sb.AppendLine($"  Udon 中継の接続 : {(_pump != null ? _pump.ConnectionDescription : "(Pump なし)")}");
            sb.AppendLine($"  カタログ       : 動画 {catalog.FilterByType(MediaType.Video).Count} 本");
            sb.AppendLine($"  ベイク済み URL : {_host.Urls.Count} 件"
                          + $"(焼かれていない動画に当たると InvalidUrl で失敗します)");
            sb.AppendLine($"  ふるい         : {_controller.Playback.Filter}");
            sb.AppendLine($"  読み込みの見張り: {_controller.LoadWatchdogSeconds:0.#} 秒");
            sb.AppendLine($"  失敗の待ち時間  : {_controller.Failures.CooldownSeconds:0.#} 秒");
            sb.AppendLine($"  補充の実装     : {(_session.QueueRefiller != null ? _session.QueueRefiller.GetType().Name : "(none)")}");

            sb.AppendLine();
            sb.AppendLine("── 1. おすすめ再生を開始する");
            bool started = _controller.Start(_seedId);
            sb.AppendLine($"  Start(\"{_seedId}\") -> {(started ? "開始しました" : "開始できませんでした")}");
            sb.AppendLine($"  {_controller.Describe()}");
            Flush(sb);

            if (!started)
            {
                sb.AppendLine();
                sb.AppendLine("  URL のベイクを確認してください。");
                Debug.Log(sb.ToString());
                return;
            }

            sb.AppendLine();
            sb.AppendLine("── 2. ここから先は Ended / Error / Timeout だけで進みます");
            sb.AppendLine("  (このクラスは再生の指示を出しません。Update で Tick するだけです)");
            Debug.Log(sb.ToString());

            _running = true;
            _reportTimer = 0f;
        }

        /// <summary>おすすめ再生を止める。</summary>
        [ContextMenu("Stop")]
        public void StopAutoPlay()
        {
            if (_controller == null) return;
            _controller.Stop();
            _running = false;
            Debug.Log("[VRChatAutoPlayRecoverySceneDemo] 停止しました: " + _controller.Describe());
        }

        private void Update()
        {
            if (!_running || _controller == null) return;

            // ★ Phase3-4 の要:毎フレームこれだけ。
            //    失敗の時計を進め、読み込みを見張り、Queue を切らさない。
            _controller.Tick(Time.deltaTime);

            SeekNearEnd();
            Report(Time.deltaTime);
        }

        /// <summary>
        /// デモを短く済ませるため、1 本につき 1 回だけ終端付近へ跳ぶ。
        /// 実機の <c>OnVideoEnd</c> を早く出すためだけの細工で、再生制御には関与しません。
        /// </summary>
        private void SeekNearEnd()
        {
            if (_seekNearEndTailSeconds <= 0f || _backend == null) return;
            if (_backend.GetState() != VideoPlayerState.Playing) return;

            string current = _session.CurrentMediaId;
            if (current == null || current == _seeking) return;

            float duration = _backend.GetDuration();
            if (duration <= _seekNearEndTailSeconds) return;

            _seeking = current;
            _backend.Seek(duration - _seekNearEndTailSeconds);
        }

        private void Report(float deltaSeconds)
        {
            if (_reportIntervalSeconds <= 0f) return;

            _reportTimer += deltaSeconds;
            if (_reportTimer < _reportIntervalSeconds) return;
            _reportTimer = 0f;

            var sb = new StringBuilder();
            sb.AppendLine("[Phase3-4] " + _controller.Describe());
            sb.AppendLine("  " + _controller.Failures);
            if (_controller.LastRecoveryReason != null)
            {
                sb.AppendLine($"  直近の立て直し: {_controller.LastRecoveryReason}");
            }
            Flush(sb);

            Debug.Log(sb.ToString());
        }

        private void Flush(StringBuilder sb)
        {
            for (int i = _flushed; i < _logger.Lines.Count; i++)
            {
                sb.Append("    ").AppendLine(_logger.Lines[i]);
            }
            _flushed = _logger.Lines.Count;
        }
    }
}
