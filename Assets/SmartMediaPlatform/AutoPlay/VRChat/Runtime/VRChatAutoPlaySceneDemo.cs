using System.Collections;
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
    /// <b>Phase3-3 の DemoScene の進行役(VRChat SDK で実際に動画を再生する版)。</b>
    ///
    /// <c>VRChatVideoBackendHost</c> が組み立てた本物のバックエンドに
    /// <see cref="RecommendationPlaybackService"/> を接続し、
    /// <b>おすすめだけで動画が次々に切り替わる</b>ことを Console で確認します。
    ///
    /// 進行のきっかけは実機の <c>OnVideoEnd</c> だけです
    /// (Phase3-2 の <c>UdonVRCVideoEventRelay</c> → <c>UdonVideoEventPump</c> →
    ///  <c>VideoEventBridge</c> の経路で届きます)。
    /// このクラスは再生の指示を一切出しません — 最初の <c>Start()</c> だけです。
    /// </summary>
    [RequireComponent(typeof(VRChatVideoBackendHost))]
    public sealed class VRChatAutoPlaySceneDemo : MonoBehaviour
    {
        [Header("おすすめ再生")]
        [Tooltip("起点にする動画の Catalog ID。空ならカタログの先頭")]
        [SerializeField] private string _seedId = "video-001";

        [Tooltip("何本まで自動で流すか")]
        [SerializeField] private int _playCount = 5;

        [Header("実行")]
        [SerializeField] private bool _runOnStart = true;

        [Tooltip("1 本ごとに、終端付近までシークして早送りする(0 で無効)")]
        [SerializeField] private float _seekNearEndTailSeconds = 3f;

        [Tooltip("1 本あたりの待ち時間の上限(秒)")]
        [SerializeField] private float _perVideoTimeoutSeconds = 60f;

        private VRChatVideoBackendHost _host;
        private UdonVideoEventPump _pump;
        private RecommendationPlaybackService _service;
        private PlayerSession _session;
        private ListBackendLogger _logger;
        private StringBuilder _out;
        private int _flushed;

        /// <summary>組み立て済みのおすすめ再生サービス。</summary>
        public RecommendationPlaybackService Service => _service;

        private void Start()
        {
            if (_runOnStart) StartCoroutine(Run());
        }

        [ContextMenu("Run Recommendation Auto Play")]
        public void RunFromMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[VRChatAutoPlaySceneDemo] Play 中に実行してください。");
                return;
            }
            StartCoroutine(Run());
        }

        public IEnumerator Run()
        {
            _logger = new ListBackendLogger();
            _out = new StringBuilder();
            _flushed = 0;
            _out.AppendLine("=== Phase3-3 Recommendation Auto Play (VRChat SDK DemoScene) ===");

            _host = GetComponent<VRChatVideoBackendHost>();
            _pump = GetComponent<UdonVideoEventPump>();

            var adapter = _host.EnsureBuilt(_logger);
            if (adapter == null)
            {
                Debug.LogError("[VRChatAutoPlaySceneDemo] Backend を組み立てられませんでした。");
                yield break;
            }

            var backend = _host.Backend;
            var bridge = _host.Bridge;

            // ── 0. 組み立て
            Section("0. 構成");

            // Catalog は動画だけのソースを使う(Phase3-3 で追加)
            IMediaCatalog catalog = new MediaCatalog(new VideoCatalogSource());
            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, _logger);
            var mediaPlayer = new MediaPlayer(backendManager, _logger);
            var engine = new RecommendationEngine(catalog, RecommendationRule.CreateDefault());
            _session = new PlayerSession(
                "vrchat-autoplay", mediaPlayer, catalog, engine, new System.Random(1), _logger);

            // Phase3-3 の接続層。登録もここへ任せる(Ended の受け取り順を保証するため)
            _service = new RecommendationPlaybackService(
                _session, catalog, engine,
                new BackendPlaybackFilter(backendManager), _logger);
            _service.RegisterBackend(adapter);

            Line($"動画プレイヤー : {(_host.VideoPlayer != null ? _host.VideoPlayer.GetType().Name : "(none)")}");
            Line($"Udon 中継の接続 : {(_pump != null ? _pump.ConnectionDescription : "(Pump なし)")}");
            Line($"カタログ       : 動画 {catalog.FilterByType(MediaType.Video).Count} 本");
            Line($"ベイク済み URL : {_host.Urls.Count} 件");
            Line($"フィルタ       : {_service.Filter}");
            if (_host.Urls.Count == 0)
            {
                Line("  ※ URL が焼き込まれていません。");
                Line("     Tools > Smart Media Platform > Bake Catalog Urls Into Selected Video Host");
            }

            // ── 1. 開始
            Section("1. おすすめ再生を開始する");
            int added = _service.Start(_seedId);
            Line($"Start(\"{_seedId}\") -> Queue に {added} 本");
            Line($"  再生中 = {_session.CurrentMediaId ?? "(none)"} [{_session.PlaybackState}]");
            Line($"  Queue  = {DescribeQueue()}");
            Flush();

            if (added == 0)
            {
                Line("再生を開始できませんでした。URL のベイクを確認してください。");
                Debug.Log(_out.ToString());
                yield break;
            }

            // ── 2. Ended だけで次へ進む
            Section("2. Ended イベントだけで次のおすすめ動画へ進む");
            for (int i = 1; i <= _playCount; i++)
            {
                string playing = _session.CurrentMediaId;

                // 再生が始まるのを待つ
                yield return WaitUntil(
                    () => backend.GetState() == VideoPlayerState.Playing
                          || backend.GetState() == VideoPlayerState.Failed,
                    _perVideoTimeoutSeconds);

                if (backend.GetState() == VideoPlayerState.Failed)
                {
                    Line($"  {i,2}. {playing} の再生に失敗しました: {_host.Bridge.Describe()}");
                    break;
                }

                // デモを短く済ませるため終端付近へ跳ぶ(実機の OnVideoEnd を早く出す)
                float duration = backend.GetDuration();
                if (_seekNearEndTailSeconds > 0f && duration > _seekNearEndTailSeconds)
                {
                    backend.Seek(duration - _seekNearEndTailSeconds);
                }

                // 動画が終わって次へ切り替わるのを待つ
                yield return WaitUntil(
                    () => _session.CurrentMediaId != playing
                          || _session.PlaybackState == PlaybackState.Exhausted,
                    _perVideoTimeoutSeconds);

                Line($"  {i,2}. {playing} 終了 -> 次は {_session.CurrentMediaId ?? "(none)"}"
                     + $" [{_session.PlaybackState}] Queue={_session.Queue.Count}");

                if (_session.PlaybackState == PlaybackState.Exhausted) break;
            }
            Flush();

            // ── 3. まとめ
            Section("3. まとめ");
            Line(_service.Describe());
            Line($"Ended イベント受理 = {bridge.AcceptedCount(VideoEventKind.End)} 回"
                 + $" / 棄却 = {bridge.RejectedCount(VideoEventKind.End)} 回");
            Line($"イベントは届いているか -> {bridge.EventsObserved}");
            Line($"履歴: {string.Join(" -> ", _session.History)}");
            Line("Recommendation → Queue → PlayerSession → BackendAdapter → VRChatVideoBackend");
            Line("BackendAdapter・PlayerSession・Queue・Recommendation は 1 行も変更していません。");

            Debug.Log(_out.ToString());
        }

        // ───────── 補助 ─────────

        private string DescribeQueue()
        {
            var entries = _session.Queue.GetAll();
            if (entries.Count == 0) return "(空)";

            var sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(entries[i].MediaId);
            }
            return sb.ToString();
        }

        private IEnumerator WaitUntil(System.Func<bool> condition, float timeoutSeconds)
        {
            float waited = 0f;
            while (!condition() && waited < timeoutSeconds)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (!condition()) Line($"  (待機打ち切り: {timeoutSeconds:0.#} 秒)");
        }

        private void Section(string title)
        {
            _out.AppendLine();
            _out.AppendLine($"── {title}");
        }

        private void Line(string text)
        {
            _out.AppendLine("  " + text);
        }

        private void Flush()
        {
            for (int i = _flushed; i < _logger.Lines.Count; i++)
            {
                _out.Append("    ").AppendLine(_logger.Lines[i]);
            }
            _flushed = _logger.Lines.Count;
        }
    }
}
