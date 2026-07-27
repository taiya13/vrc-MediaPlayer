using System.Collections.Generic;
using System.Text;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;
using UnityEngine;

namespace SmartMediaPlatform.Video.Demo
{
    /// <summary>
    /// Phase3-2 の <see cref="VideoEventBridge"/> を Console だけで確認するデモ。
    ///
    /// <b>VRChat SDK が無くても走ります。</b>
    /// 実機の代わりに <see cref="SimulatedVRCVideoPlayer"/> を置き、
    /// VRChat が送ってくるイベント(<c>OnVideoReady</c> / <c>OnVideoStart</c> /
    /// <c>OnVideoEnd</c> / <c>OnVideoError</c> / <c>OnVideoLoop</c>)を
    /// <see cref="IVideoEventSink"/> へ手で流し込みます。
    ///
    /// 実機では同じ場所に <c>UdonVRCVideoEventRelay</c> →
    /// <c>UdonVideoEventPump</c> が入るだけで、<b>ブリッジから下は完全に同じ</b>です。
    ///
    /// 空の GameObject にアタッチして Play すれば走ります。
    /// </summary>
    public sealed class VRChatVideoEventBridgeConsoleDemo : MonoBehaviour
    {
        [Header("再生する動画(Catalog の ID)")]
        [SerializeField] private string[] _videoIds = { "video-001" };

        [Header("実行")]
        [SerializeField] private bool _runOnStart = true;

        private IMediaCatalog _catalog;
        private CatalogVideoUrlTable _urls;
        private ListBackendLogger _logger;
        private StringBuilder _out;
        private int _flushed;

        private void Start()
        {
            if (_runOnStart) Run();
        }

        [ContextMenu("Run VRChat Video Event Bridge Scenarios")]
        public void Run()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _urls = new CatalogVideoUrlTable(_catalog);
            _logger = new ListBackendLogger();
            _out = new StringBuilder();
            _flushed = 0;

            _out.AppendLine("=== Phase3-2 VRChat Video Event Bridge Demo ===");

            RunEventPathScenario();
            RunNoDoubleFireScenario();
            RunTickCoexistenceScenario();
            RunErrorScenario();
            RunUdonCodecScenario();
            RunSessionScenario();

            Debug.Log(_out.ToString());
        }

        // ───────── 1. 4 つのイベントが Backend へ届く ─────────

        private void RunEventPathScenario()
        {
            Section("1. VideoPlayer のイベントが Backend へ届く");

            var backend = NewBackend(out var player);
            var bridge = new VideoEventBridge(backend, _logger);
            var observer = new CountingObserver();
            backend.AddObserver(observer);

            backend.Load(Url());
            Line($"Load 直後 -> State={backend.GetState()}(実機と同じく読み込み中)");

            // 実機の OnVideoReady。プレイヤー側も Ready になったことにする。
            player.CompleteLoading();
            Line($"OnVideoReady  -> 受理={bridge.OnVideoReady()} / State={backend.GetState()}"
                 + $" / 観測者への Ready 通知={observer.ReadyCount} 回");

            Line($"OnVideoStart  -> 受理={bridge.OnVideoStart()} / State={backend.GetState()}"
                 + $" / Start 通知={observer.StartCount} 回");

            Line($"OnVideoEnd    -> 受理={bridge.OnVideoEnd()} / State={backend.GetState()}"
                 + $" / End 通知={observer.EndCount} 回");

            backend.Load(Url());
            player.CompleteLoading();
            bridge.OnVideoReady();
            Line($"OnVideoError  -> 受理={bridge.OnVideoError(VideoErrorKind.AccessDenied)}"
                 + $" / State={backend.GetState()} / Error 通知={observer.ErrorCount} 回");

            Line($"ブリッジの集計: {bridge.Describe()}");
            Flush();
        }

        // ───────── 2. 二重発火しない ─────────

        private void RunNoDoubleFireScenario()
        {
            Section("2. 同じイベントが二度届いても、上位への通知は 1 回だけ");

            var backend = NewBackend(out var player);
            var bridge = new VideoEventBridge(backend, _logger);
            var observer = new CountingObserver();
            backend.AddObserver(observer);

            backend.Load(Url());
            player.CompleteLoading();

            Line("VRChat が同じイベントを 3 回ずつ送ってきた場合:");
            for (int i = 1; i <= 3; i++) Line($"  OnVideoReady #{i} -> 受理={bridge.OnVideoReady()}");
            for (int i = 1; i <= 3; i++) Line($"  OnVideoStart #{i} -> 受理={bridge.OnVideoStart()}");
            for (int i = 1; i <= 3; i++) Line($"  OnVideoEnd   #{i} -> 受理={bridge.OnVideoEnd()}");

            Line($"観測者が受け取った回数 -> Ready={observer.ReadyCount}"
                 + $" / Start={observer.StartCount} / End={observer.EndCount}");
            Line($"棄却した回数 -> Ready={bridge.RejectedCount(VideoEventKind.Ready)}"
                 + $" / Start={bridge.RejectedCount(VideoEventKind.Start)}"
                 + $" / End={bridge.RejectedCount(VideoEventKind.End)}");
            Line("いずれも 1 回だけ通知され、残りは棄却されています。");

            Flush();
        }

        // ───────── 3. Tick と競合しない ─────────

        private void RunTickCoexistenceScenario()
        {
            Section("3. Tick(ポーリング)とイベントが競合しない");

            Line("(a) イベントが届く構成 — ポーリングは自動で降りる");
            var backend = NewBackend(out var player);
            var bridge = new VideoEventBridge(backend, _logger);
            var observer = new CountingObserver();
            backend.AddObserver(observer);

            backend.Load(Url());
            player.CompleteLoading();
            bridge.OnVideoReady();
            bridge.OnVideoStart();
            Line($"  イベントを受け取った -> EventsObserved={bridge.EventsObserved}");

            bridge.Tick(0.016f);
            Line($"  ポーリングによる終了検出 -> DetectEndByPolling={backend.DetectEndByPolling}(降りた)");

            // 動画が終端まで進み、イベントとポーリングの両方が終了を検出しうる状況を作る
            player.FinishPlayback();
            bridge.OnVideoEnd();
            bridge.Tick(0.016f);
            bridge.Tick(0.016f);
            Line($"  End イベント + Tick 2 回 -> 観測者への End 通知={observer.EndCount} 回");

            Line("(b) イベントが来ない構成 — ポーリングが保険として働く");
            var backend2 = NewBackend(out var player2);
            var bridge2 = new VideoEventBridge(backend2, _logger);
            var observer2 = new CountingObserver();
            backend2.AddObserver(observer2);

            backend2.Load(Url());
            player2.CompleteLoading();
            bridge2.Tick(0.016f);          // イベント無しで Ready を拾う
            backend2.Play();
            player2.Advance(player2.DurationSeconds + 1f);
            bridge2.Tick(0.016f);          // イベント無しで End を拾う

            Line($"  EventsObserved={bridge2.EventsObserved}"
                 + $" / DetectEndByPolling={backend2.DetectEndByPolling}(残っている)");
            Line($"  観測者への End 通知={observer2.EndCount} 回 / State={backend2.GetState()}");
            Line("  Phase3-1 の動作はそのまま残っています(イベントが来る環境でだけ降ります)。");

            Flush();
        }

        // ───────── 4. Error ─────────

        private void RunErrorScenario()
        {
            Section("4. Error イベント(重複も含めて)");

            var backend = NewBackend(out var player);
            var bridge = new VideoEventBridge(backend, _logger);
            var observer = new CountingObserver();
            backend.AddObserver(observer);

            backend.Load(Url());
            player.CompleteLoading();
            bridge.OnVideoReady();

            foreach (var kind in new[]
                     {
                         VideoErrorKind.AccessDenied,
                         VideoErrorKind.AccessDenied,   // 同じ失敗の二度目
                         VideoErrorKind.RateLimited,    // 違う失敗
                     })
            {
                bool accepted = bridge.OnVideoError(kind);
                Line($"OnVideoError({kind}) -> 受理={accepted} / Error 通知={observer.ErrorCount} 回");
            }

            Line($"棄却した Error = {bridge.RejectedCount(VideoEventKind.Error)} 回"
                 + "(同じ失敗の二度目だけを落としています)");
            Line($"直近のエラー -> {observer.LastError}");

            Flush();
        }

        // ───────── 5. Udon 経由の符号化 ─────────

        private void RunUdonCodecScenario()
        {
            Section("5. Udon から運ばれてくる経路(int 符号 → Sink)");

            var backend = NewBackend(out var player);
            var bridge = new VideoEventBridge(backend, _logger);
            var observer = new CountingObserver();
            backend.AddObserver(observer);

            backend.Load(Url());
            player.CompleteLoading();

            // UdonVRCVideoEventRelay がリングバッファに積むのと同じ int の並び
            var codes = new List<int>
            {
                VideoEventCodec.Encode(VideoEventKind.Ready),
                VideoEventCodec.Encode(VideoEventKind.Start),
                VideoEventCodec.Encode(VideoEventKind.End),
                VideoEventCodec.EncodeError(VideoErrorKind.PlayerError),
            };

            Line($"Udon が積んだコード列: [{string.Join(", ", codes)}]");
            foreach (var code in codes)
            {
                VideoEventCodec.TryDecode(code, out var kind, out var error);
                bool accepted = VideoEventCodec.Deliver(bridge, code);
                Line($"  {code,4} -> {kind}{(kind == VideoEventKind.Error ? $"({error})" : "")}"
                     + $" / 受理={accepted}");
            }

            Line($"観測者 -> Ready={observer.ReadyCount} Start={observer.StartCount}"
                 + $" End={observer.EndCount} Error={observer.ErrorCount}");
            Line("実機では UdonVideoEventPump がこの int 列を運ぶだけです。");

            Flush();
        }

        // ───────── 6. 上位は無変更 ─────────

        private void RunSessionScenario()
        {
            Section("6. PlayerSession / BackendAdapter は無変更のまま自動送りする");

            var backend = NewBackend(out var player);
            player.AutoCompleteLoading = true;
            var bridge = new VideoEventBridge(backend, _logger);

            // ← Phase2-4(B) から変わっていない行
            var adapter = new VideoBackendAdapter("VideoAdapter", backend, _logger);

            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, _logger);
            var mediaPlayer = new MediaPlayer(backendManager, _logger);
            var engine = new RecommendationEngine(
                _catalog, RecommendationRule.CreateDefault(), new System.Random(1));
            var session = new PlayerSession(
                "video-events", mediaPlayer, _catalog, engine, new System.Random(1), _logger);

            session.RegisterBackend(adapter);
            session.AutoQueueEnabled = false;
            session.SetTracks(_videoIds);

            Line($"session.Play() -> {session.Play()} / PlayerSession={session.PlaybackState}");

            // 実機の OnVideoEnd が届いた、という 1 行だけで上位が次へ進む
            bool accepted = bridge.OnVideoEnd();
            Line($"OnVideoEnd(実機イベント) -> 受理={accepted}");
            Line($"  PlayerSession={session.PlaybackState}"
                 + $" / Current={session.CurrentMediaId ?? "(none)"}");
            Line($"  BackendAdapter から見た状態={adapter.GetState()}");
            Line("PlayerSession も BackendAdapter も、イベントブリッジの存在を知りません。");

            Flush();
        }

        // ───────── 組み立て / 出力 ─────────

        private string Url()
        {
            string id = _videoIds != null && _videoIds.Length > 0 ? _videoIds[0] : "video-001";
            var item = _catalog.FindById(id) ?? _catalog.FilterByType(MediaType.Video)[0];
            return item.Url;
        }

        private VRChatVideoBackend NewBackend(out SimulatedVRCVideoPlayer player)
        {
            player = new SimulatedVRCVideoPlayer(_urls);
            return new VRChatVideoBackend("VRChatVideoBackend", player, _urls, _logger);
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

        /// <summary>通知を数えるだけの観測者。</summary>
        private sealed class CountingObserver : IVideoBackendObserver
        {
            public int ReadyCount { get; private set; }
            public int StartCount { get; private set; }
            public int EndCount { get; private set; }
            public int ErrorCount { get; private set; }
            public string LastError { get; private set; }

            public void OnVideoReady(string url) => ReadyCount++;
            public void OnVideoStart(string url) => StartCount++;
            public void OnVideoPause(string url) { }
            public void OnVideoStop(string url) { }
            public void OnVideoEnd(string url) => EndCount++;

            public void OnVideoError(string url, string message)
            {
                ErrorCount++;
                LastError = message;
            }
        }
    }
}
