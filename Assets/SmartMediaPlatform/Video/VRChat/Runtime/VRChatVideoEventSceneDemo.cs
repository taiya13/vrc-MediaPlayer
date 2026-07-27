using System.Collections;
using System.Text;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;
using UnityEngine;
using VRC.SDK3.Components.Video;

namespace SmartMediaPlatform.Video.VRChat
{
    /// <summary>
    /// <b>Phase3-2 の DemoScene の進行役。</b>
    ///
    /// 実機の VideoPlayer が発火したイベントが
    /// <c>UdonVRCVideoEventRelay</c> → <c>UdonVideoEventPump</c> →
    /// <see cref="VideoEventBridge"/> → <see cref="VRChatVideoBackend"/> と流れ、
    /// <b>PlayerSession が何も知らないまま自動送りされる</b>ことを Console で確認します。
    ///
    /// イベントが実際に届いているかは <see cref="VideoEventBridge.EventsObserved"/> と
    /// 受理/棄却の集計で判定します。届いていなければ、その旨をはっきり出力します
    /// (ポーリングで動いていることを「イベントで動いた」と誤認しないため)。
    /// </summary>
    [RequireComponent(typeof(VRChatVideoBackendHost))]
    public sealed class VRChatVideoEventSceneDemo : MonoBehaviour
    {
        [Header("再生する動画(Catalog の ID)")]
        [SerializeField] private string[] _videoIds = { "video-001" };

        [Header("実行")]
        [SerializeField] private bool _runOnStart = true;

        [Tooltip("読み込み完了(OnVideoReady)を待つ最大秒数")]
        [SerializeField] private float _readyWaitSeconds = 20f;

        [Tooltip("再生終了(OnVideoEnd)を待つ最大秒数")]
        [SerializeField] private float _endWaitSeconds = 30f;

        [Tooltip("終端付近までシークして Ended を早く確認する(0 で無効)")]
        [SerializeField] private float _seekNearEndTailSeconds = 3f;

        private VRChatVideoBackendHost _host;
        private UdonVideoEventPump _pump;
        private ListBackendLogger _logger;
        private StringBuilder _out;
        private int _flushed;

        private void Start()
        {
            if (_runOnStart) StartCoroutine(Run());
        }

        [ContextMenu("Run VRChat Video Event Scenarios")]
        public void RunFromMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[VRChatVideoEventSceneDemo] Play 中に実行してください。");
                return;
            }
            StartCoroutine(Run());
        }

        public IEnumerator Run()
        {
            _logger = new ListBackendLogger();
            _out = new StringBuilder();
            _flushed = 0;
            _out.AppendLine("=== Phase3-2 VRChat Video Event Bridge Demo (DemoScene) ===");

            _host = GetComponent<VRChatVideoBackendHost>();
            _pump = GetComponent<UdonVideoEventPump>();

            var adapter = _host.EnsureBuilt(_logger);
            if (adapter == null)
            {
                Debug.LogError("[VRChatVideoEventSceneDemo] Backend を組み立てられませんでした。");
                yield break;
            }

            var backend = _host.Backend;
            var bridge = _host.Bridge;

            // ── 0. 配線
            Section("0. イベントの配線");
            Line($"動画プレイヤー : {(_host.VideoPlayer != null ? _host.VideoPlayer.GetType().Name : "(none)")}");
            Line($"Udon 中継の接続 : {(_pump != null && _pump.IsConnected ? "OK" : "未接続")}");
            if (_pump == null || !_pump.IsConnected)
            {
                Line("  ※ UdonVRCVideoEventRelay が見つかりません。");
                Line("     この場合はポーリング(保険)で動作します。イベント経路の確認にはなりません。");
            }
            Line($"ベイク済み URL : {_host.Urls.Count} 件");

            // ── 1. 再生して OnVideoReady / OnVideoStart を待つ
            Section("1. OnVideoReady / OnVideoStart");
            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());
            var session = BuildSession(catalog, adapter);
            session.AutoQueueEnabled = false;
            session.SetTracks(_videoIds);

            Line($"session.Play() -> {session.Play()} / State={backend.GetState()}");

            yield return WaitUntil(() => backend.GetState() == VideoPlayerState.Playing, _readyWaitSeconds);

            Line($"OnVideoReady 受理 = {bridge.AcceptedCount(VideoEventKind.Ready)} 回"
                 + $" / 棄却 = {bridge.RejectedCount(VideoEventKind.Ready)} 回");
            Line($"OnVideoStart 受理 = {bridge.AcceptedCount(VideoEventKind.Start)} 回"
                 + $" / 棄却 = {bridge.RejectedCount(VideoEventKind.Start)} 回");
            Line($"State={backend.GetState()} / 長さ={backend.GetDuration():0.0}s"
                 + $" / 上位から見た状態={adapter.GetState()}");
            Line($"イベントは届いているか -> {bridge.EventsObserved}");
            Line($"ポーリングによる終了推測 -> {backend.DetectEndByPolling}"
                 + "(イベントが届いていれば False に降ります)");
            Flush();

            // ── 2. OnVideoEnd
            Section("2. OnVideoEnd(上位が自動で次へ進む)");
            float duration = backend.GetDuration();
            if (_seekNearEndTailSeconds > 0f && duration > _seekNearEndTailSeconds)
            {
                backend.Seek(duration - _seekNearEndTailSeconds);
                Line($"終端付近へシーク -> {backend.GetTime():0.0}s / {duration:0.0}s");
            }

            yield return WaitUntil(() => backend.GetState() == VideoPlayerState.Finished, _endWaitSeconds);

            Line($"OnVideoEnd 受理 = {bridge.AcceptedCount(VideoEventKind.End)} 回"
                 + $" / 棄却 = {bridge.RejectedCount(VideoEventKind.End)} 回");
            Line($"State={backend.GetState()} / 上位から見た状態={adapter.GetState()}");
            Line($"PlayerSession -> {session.PlaybackState} / Current={session.CurrentMediaId ?? "(none)"}");
            Flush();

            // ── 3. OnVideoError と重複
            Section("3. OnVideoError(重複しても 1 回だけ通知)");
            var item = catalog.FindById(_videoIds.Length > 0 ? _videoIds[0] : "video-001");
            backend.Load(item.Url);

            int before = bridge.AcceptedCount(VideoEventKind.Error);
            _host.OnVideoError(VideoError.AccessDenied);
            _host.OnVideoError(VideoError.AccessDenied);   // 実機が二度送ってきた場合
            _host.OnVideoError(VideoError.RateLimited);    // 違う失敗

            Line($"OnVideoError x3(うち 2 件は同じ失敗)");
            Line($"  受理 = {bridge.AcceptedCount(VideoEventKind.Error) - before} 回"
                 + $" / 棄却 = {bridge.RejectedCount(VideoEventKind.Error)} 回");
            Line($"  上位から見た状態 = {adapter.GetState()} / HasError={adapter.HasError}");
            Line($"  {adapter.LastError}");
            Flush();

            // ── 4. まとめ
            Section("4. まとめ");
            Line(bridge.Describe());
            Line("直近のイベント履歴:");
            foreach (var record in bridge.Log) Line("  " + record);

            Line(bridge.EventsObserved
                ? "VideoPlayer のイベントが Backend へ届き、重複は棄却されました。"
                : "イベントが届いていません(ポーリングで動作)。Udon 中継の配線を確認してください。");
            Line("BackendAdapter・PlayerSession・Queue・Recommendation は 1 行も変更していません。");

            Debug.Log(_out.ToString());
        }

        // ───────── 補助 ─────────

        private PlayerSession BuildSession(IMediaCatalog catalog, IMediaBackend adapter)
        {
            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, _logger);
            var player = new MediaPlayer(backendManager, _logger);
            var engine = new RecommendationEngine(catalog, RecommendationRule.CreateDefault());
            var session = new PlayerSession(
                "vrchat-video-events", player, catalog, engine, new System.Random(1), _logger);

            session.RegisterBackend(adapter);
            return session;
        }

        private IEnumerator WaitUntil(System.Func<bool> condition, float timeoutSeconds)
        {
            float waited = 0f;
            while (!condition() && waited < timeoutSeconds)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (!condition()) Line($"  (待機打ち切り: {timeoutSeconds:0.#} 秒以内に条件を満たしませんでした)");
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
