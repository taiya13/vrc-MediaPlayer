using System.Text;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;
using SmartMediaPlatform.Video;
using SmartMediaPlatform.Video.Data;
using UnityEngine;

namespace SmartMediaPlatform.AutoPlay.Demo
{
    /// <summary>
    /// Phase3-4 の再生制御(Ended / Error / Timeout のどれでも止まらない)を
    /// Console だけで確認するデモ。
    ///
    /// <b>VRChat SDK が無くても走ります。</b>
    /// 実機の動画プレイヤーの位置に <see cref="SimulatedVRCVideoPlayer"/> を置き、
    /// 進行は <see cref="VideoEventBridge"/> 経由のイベントだけで行います
    /// (最初の <c>Start()</c> 以降、再生の指示は一切出しません)。
    ///
    /// 空の GameObject にアタッチして Play すれば走ります。
    /// </summary>
    public sealed class AutoPlayRecoveryConsoleDemo : MonoBehaviour
    {
        [Header("おすすめ再生")]
        [SerializeField] private string _seedId = "video-001";

        [Header("耐久確認")]
        [Tooltip("連続再生する本数")]
        [SerializeField] private int _enduranceCount = 120;

        [Tooltip("何本に 1 本を失敗させるか(0 で失敗させない)")]
        [SerializeField] private int _failEvery = 4;

        [Header("実行")]
        [SerializeField] private bool _runOnStart = true;

        private StringBuilder _out;
        private ListBackendLogger _logger;
        private int _flushed;

        private void Start()
        {
            if (_runOnStart) Run();
        }

        [ContextMenu("Run Auto Play Recovery Scenarios")]
        public void Run()
        {
            _out = new StringBuilder();
            _logger = new ListBackendLogger();
            _flushed = 0;

            _out.AppendLine("=== Phase3-4 Playback Orchestration & Recovery Demo ===");

            RunEndedScenario();
            RunErrorScenario();
            RunTimeoutScenario();
            RunCooldownScenario();
            RunEnduranceScenario();
            RunRefactorScenario();

            Debug.Log(_out.ToString());
        }

        // ───────── 1. Ended ─────────

        private void RunEndedScenario()
        {
            Section("1. 動画終了(Ended)で次のおすすめへ");

            var rig = new Rig(_logger);
            rig.Controller.Start(_seedId);
            Line($"Start(\"{_seedId}\") -> [{rig.Controller.State}] {rig.Controller.CurrentMediaId}");

            for (int i = 1; i <= 3; i++)
            {
                string before = rig.Controller.CurrentMediaId;
                rig.FinishCurrentVideo();
                Line($"  {i}. Ended({before}) -> {rig.Controller.CurrentMediaId}"
                     + $" [{rig.Controller.State}] Queue={rig.Session.Queue.Count}");
            }

            Flush();
        }

        // ───────── 2. Error ─────────

        private void RunErrorScenario()
        {
            Section("2. 再生失敗(Error)でも止まらず次候補へ");

            var rig = new Rig(_logger);
            rig.Controller.Start(_seedId);

            foreach (var kind in new[]
                     {
                         VideoErrorKind.InvalidUrl,      // URL 切れ
                         VideoErrorKind.AccessDenied,
                         VideoErrorKind.PlayerError,
                     })
            {
                string before = rig.Controller.CurrentMediaId;
                rig.FailCurrentVideo(kind);
                Line($"  {kind,-12} ({before}) -> {rig.Controller.CurrentMediaId}"
                     + $" [{rig.Controller.State}]");
            }

            Line($"失敗 {rig.Controller.FailureCount} 回 / 立て直し {rig.Controller.RecoveredCount} 回"
                 + $" / 連続失敗 {rig.Controller.ConsecutiveFailures}");
            Line("1 本でも再生できれば連続失敗の数は 0 に戻ります。");
            Flush();
        }

        // ───────── 3. Timeout ─────────

        private void RunTimeoutScenario()
        {
            Section("3. タイムアウトでも止まらず次候補へ");

            var rig = new Rig(_logger);
            rig.Controller.LoadWatchdogSeconds = 5f;
            rig.PlayerNeverBecomesReady = true;

            rig.Controller.Start(_seedId);
            Line($"読み込みが終わらない状態 -> [{rig.Controller.State}]"
                 + $" BackendState={rig.Session.BackendState}");

            rig.PlayerNeverBecomesReady = false;
            rig.Controller.Tick(6f);

            Line($"見張りが 5 秒で打ち切り -> [{rig.Controller.State}] {rig.Controller.CurrentMediaId}");
            Line($"タイムアウト {rig.Controller.TimeoutCount} 回");
            Line($"止まった動画は記録された -> Blocked={rig.Controller.Failures.IsBlocked(_seedId)}");
            Flush();
        }

        // ───────── 4. 再試行の抑制 ─────────

        private void RunCooldownScenario()
        {
            Section("4. 失敗した動画を一定時間再試行しない");

            var rig = new Rig(_logger);
            rig.Controller.Failures.CooldownSeconds = 10f;
            rig.Controller.Failures.BackoffMultiplier = 3f;
            rig.Controller.Start(_seedId);

            rig.FailCurrentVideo(VideoErrorKind.InvalidUrl);
            Line($"{_seedId} が失敗 -> Blocked={rig.Controller.Failures.IsBlocked(_seedId)}"
                 + $"(待ち時間 10 秒)");

            rig.Session.ClearQueue();
            rig.Session.EnsureQueueFilled();
            bool requeued = rig.Session.Queue.Contains(_seedId);
            Line($"Queue を積み直しても入らない -> {(requeued ? "入ってしまった" : "入らない")}");
            Line($"  積み直した中身: {rig.DescribeQueue()}");

            rig.Controller.Tick(11f);
            Line($"11 秒経過 -> Blocked={rig.Controller.Failures.IsBlocked(_seedId)}(再挑戦できる)");

            // 2 回目の失敗は待ち時間が伸びる(10 → 30 秒)
            rig.Controller.Failures.MarkFailed(_seedId, "2 回目");
            rig.Controller.Tick(11f);
            Line($"2 回目の失敗後 11 秒 -> Blocked={rig.Controller.Failures.IsBlocked(_seedId)}"
                 + "(失敗を重ねるほど待ち時間が伸びる)");

            Line(rig.Controller.Failures.ToString());
            Flush();
        }

        // ───────── 5. 長時間耐久 ─────────

        private void RunEnduranceScenario()
        {
            Section($"5. 長時間耐久({_enduranceCount} 本連続・"
                    + (_failEvery > 0 ? $"{_failEvery} 本に 1 本は失敗" : "失敗なし") + ")");

            var rig = new Rig(_logger);
            rig.Controller.Failures.CooldownSeconds = 5f;
            rig.Controller.Start(_seedId);

            int stoppedAt = -1;
            int minQueue = int.MaxValue;

            for (int i = 0; i < _enduranceCount; i++)
            {
                rig.Controller.Tick(10f);

                if (_failEvery > 0 && i % _failEvery == _failEvery - 1)
                {
                    rig.FailCurrentVideo(VideoErrorKind.InvalidUrl);
                }
                else
                {
                    rig.FinishCurrentVideo();
                }

                if (rig.Session.Queue.Count < minQueue) minQueue = rig.Session.Queue.Count;

                if (rig.Controller.State != AutoPlayState.Playing)
                {
                    stoppedAt = i + 1;
                    break;
                }
            }

            Line(stoppedAt < 0
                ? $"{_enduranceCount} 本を最後まで流し切りました(一度も止まっていません)"
                : $"{stoppedAt} 本目で止まりました: {rig.Controller.Describe()}");
            Line($"Queue の最小値 = {minQueue}(0 にならなければ枯渇していない)");
            Line($"おすすめが出した候補 = {rig.Controller.Playback.EnqueuedByRecommendation} 件"
                 + $" / カタログ補完 = {rig.Controller.Playback.EnqueuedByCatalogFallback} 件");
            Line($"失敗の記憶 = {rig.Controller.Failures.TrackedCount} 件"
                 + $"(上限 {rig.Controller.Failures.MaxTracked} — 増え続けない)");
            Line($"履歴 = {rig.Session.History.Count} 件(上限 {rig.Session.MaxHistory})");
            Line(rig.Controller.Describe());
            Flush();
        }

        // ───────── 6. 整理の結果 ─────────

        private void RunRefactorScenario()
        {
            Section("6. Phase3-4 で整理したところ");

            var rig = new Rig(_logger);
            rig.Controller.Start(_seedId);

            Line("補充の実装は 1 つだけ:");
            Line($"  PlayerSession.QueueRefiller = {rig.Session.QueueRefiller.GetType().Name}");
            Line($"  RecommendationPlaybackService.Refiller と同じ実体か"
                 + $" -> {ReferenceEquals(rig.Session.QueueRefiller, rig.Controller.Playback.Refiller)}");
            Line($"  PlayerSession.AutoQueueEnabled = {rig.Session.AutoQueueEnabled}"
                 + "(外から書き換えていない)");

            Line("");
            Line("おすすめは差し替え可能:");
            Line($"  RecommendationEngine is IRecommendationEngine -> "
                 + $"{rig.Engine is IRecommendationEngine}");

            Line("");
            Line("ふるいは関心事ごとに分けて合成:");
            Line($"  {rig.Controller.Playback.Filter}");

            Line("");
            Line("カタログ供給元は組み合わせられる:");
            var mixed = new MediaCatalog(new MixedCatalogSource());
            Line($"  MixedCatalogSource = {mixed.Count} 件"
                 + $"(動画 {mixed.FilterByType(MediaType.Video).Count}"
                 + $" / 音楽 {mixed.FilterByType(MediaType.Music).Count})");

            Flush();
        }

        // ───────── 出力 ─────────

        private void Section(string title)
        {
            _out.AppendLine();
            _out.AppendLine($"── {title}");
        }

        private void Line(string text)
        {
            _out.AppendLine(text.Length == 0 ? "" : "  " + text);
        }

        private void Flush()
        {
            for (int i = _flushed; i < _logger.Lines.Count; i++)
            {
                _out.Append("    ").AppendLine(_logger.Lines[i]);
            }
            _flushed = _logger.Lines.Count;
        }

        /// <summary>Catalog → … → VideoBackend を 1 式組み立てたもの。</summary>
        private sealed class Rig
        {
            public readonly IMediaCatalog Catalog;
            public readonly RecommendationEngine Engine;
            public readonly PlayerSession Session;
            public readonly AutoPlayController Controller;
            public readonly VRChatVideoBackend Backend;
            public readonly VideoEventBridge Bridge;

            private readonly SimulatedVRCVideoPlayer _player;

            public Rig(IBackendLogger logger)
            {
                Catalog = new MediaCatalog(new VideoCatalogSource(), new System.Random(1));

                var urls = new CatalogVideoUrlTable(Catalog);
                _player = new SimulatedVRCVideoPlayer(urls) { AutoCompleteLoading = true };
                Backend = new VRChatVideoBackend("VRChatVideoBackend", _player, urls, logger)
                {
                    LoadTimeoutSeconds = 0f,
                };
                Bridge = new VideoEventBridge(Backend, logger);
                var adapter = new VideoBackendAdapter("VideoAdapter", Backend, logger);

                var queue = new MediaQueue();
                var backendManager = new BackendManager(queue, logger);
                var mediaPlayer = new MediaPlayer(backendManager, logger);
                Engine = new RecommendationEngine(
                    Catalog, RecommendationRule.CreateDefault(), new System.Random(1));
                Session = new PlayerSession(
                    "autoplay", mediaPlayer, Catalog, Engine, new System.Random(1), logger);

                Controller = AutoPlayController.Create(
                    Session, Catalog, Engine, new BackendPlaybackFilter(backendManager), logger);
                Controller.RegisterBackend(adapter);
            }

            public bool PlayerNeverBecomesReady
            {
                get => !_player.AutoCompleteLoading;
                set => _player.AutoCompleteLoading = !value;
            }

            public void FinishCurrentVideo()
            {
                if (Backend.GetState() == VideoPlayerState.Loading && _player.AutoCompleteLoading)
                {
                    _player.CompleteLoading();
                    Bridge.OnVideoReady();
                }

                _player.FinishPlayback();
                Bridge.OnVideoEnd();
            }

            public void FailCurrentVideo(VideoErrorKind kind)
            {
                Bridge.OnVideoError(kind);
            }

            public string DescribeQueue()
            {
                var entries = Session.Queue.GetAll();
                if (entries.Count == 0) return "(空)";

                var sb = new StringBuilder();
                for (int i = 0; i < entries.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(entries[i].MediaId);
                }
                return sb.ToString();
            }
        }
    }
}
