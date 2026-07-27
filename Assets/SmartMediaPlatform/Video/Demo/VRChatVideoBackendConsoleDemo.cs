using System;
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
    /// Phase3-1 の <see cref="VRChatVideoBackend"/> を Console だけで確認するデモ。
    ///
    /// <b>VRChat SDK が無くても走ります。</b>
    /// 動画プレイヤーの位置に <see cref="SimulatedVRCVideoPlayer"/> を差し込み、
    /// 実機と同じ「非同期読み込み・時間経過・自然終了・失敗」を再現します。
    /// 実機では <c>VRChatVideoBackendHost</c> が同じ場所に
    /// <c>VRCUnityVideoPlayer</c> / <c>VRCAVProVideoPlayer</c> を差し込むだけで、
    /// <b>この下の Backend / Adapter / PlayerSession は一切変わりません。</b>
    ///
    /// 空の GameObject にアタッチして Play すれば走ります。
    /// </summary>
    public sealed class VRChatVideoBackendConsoleDemo : MonoBehaviour
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

        [ContextMenu("Run VRChat Video Backend Scenarios")]
        public void Run()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _urls = new CatalogVideoUrlTable(_catalog);   // Catalog の Video/Live URL を事前登録
            _logger = new ListBackendLogger();
            _out = new StringBuilder();
            _flushed = 0;

            _out.AppendLine("=== Phase3-1 VRChat Video Backend Demo ===");

            RunUrlTableScenario();
            RunPlaybackScenario();
            RunEndedScenario();
            RunErrorScenario();
            RunSwapScenario();
            RunSessionScenario();
            RunLiveStreamScenario();

            Debug.Log(_out.ToString());
        }

        // ───────── 1. 事前登録された URL しか再生しない ─────────

        private void RunUrlTableScenario()
        {
            Section("1. Catalog に事前登録された URL だけを再生する");

            var item = Item();
            var backend = NewBackend(out _);

            Line($"ベイク済み URL: {_urls.Count} 件(Catalog の Video / Live から)");
            foreach (var url in _urls.Urls) Line($"  - {url}");

            Line($"CanPlay(\"{item.Url}\") -> {backend.CanPlay(item.Url)}");
            Line($"CanPlay(\"https://example.com/generated-at-runtime\") -> "
                 + $"{backend.CanPlay("https://example.com/generated-at-runtime")}");
            Line("  未登録の URL は再生しません。実行時に VRCUrl は生成できないためです。");
            Line($"CanPlay(\"\") -> {backend.CanPlay("")}");

            Flush();
        }

        // ───────── 2. Load / Play / Pause / Resume / Stop ─────────

        private void RunPlaybackScenario()
        {
            Section("2. Load / Play / Pause / Resume / Seek / Stop");

            var backend = NewBackend(out var player);
            var url = Item().Url;

            Line($"Load    -> {backend.Load(url)} / State={backend.GetState()}(実機と同じく非同期)");
            Line($"  読み込み中の Play は予約になる -> {backend.Play()} / Pending={backend.IsPlayPending}");

            player.CompleteLoading();
            backend.Tick(0.016f);
            Line($"読み込み完了 -> State={backend.GetState()}(予約分が自動で再生された)");

            Advance(player, backend, 10f);
            Line($"再生中   -> Time={backend.GetTime():0.0}s / {backend.GetDuration():0.0}s");

            Line($"Pause   -> {backend.Pause()} / State={backend.GetState()}");
            Advance(player, backend, 5f);
            Line($"  一時停止中は進まない -> Time={backend.GetTime():0.0}s");

            Line($"Resume  -> {backend.Resume()} / State={backend.GetState()}");
            Advance(player, backend, 5f);
            Line($"  再開後 -> Time={backend.GetTime():0.0}s");

            Line($"Seek 60s-> {backend.Seek(60f)} / Time={backend.GetTime():0.0}s");
            Line($"Stop    -> {backend.Stop()} / State={backend.GetState()} / Time={backend.GetTime():0.0}s");

            Flush();
        }

        // ───────── 3. Ended ─────────

        private void RunEndedScenario()
        {
            Section("3. Ended イベント");

            var backend = NewBackend(out var player);
            var observer = new CountingObserver();
            backend.AddObserver(observer);

            LoadAndPlay(backend, player, Item().Url);

            Line("(a) 実機の OnVideoEnd が届く場合");
            backend.NotifyVideoEnd();
            Line($"  Ended {observer.EndCount} 回 / State={backend.GetState()}");

            Line("(b) 通知が来ず、プレイヤーが終端で自然に止まった場合(Tick が拾う)");
            var backend2 = NewBackend(out var player2);
            var observer2 = new CountingObserver();
            backend2.AddObserver(observer2);
            LoadAndPlay(backend2, player2, Item().Url);

            Advance(player2, backend2, player2.DurationSeconds + 1f);
            Line($"  Ended {observer2.EndCount} 回 / State={backend2.GetState()}");
            Line("  どちらの経路でも Ended は 1 回だけです。");

            Flush();
        }

        // ───────── 4. Error ─────────

        private void RunErrorScenario()
        {
            Section("4. Error イベント");

            var backend = NewBackend(out var player);
            var observer = new CountingObserver();
            backend.AddObserver(observer);

            Line($"(a) 未ベイクの URL -> Load={backend.Load("https://example.com/not-baked")}");
            Line($"    {observer.LastError} / Kind={backend.LastErrorKind} / State={backend.GetState()}");

            Line("(b) 実機の OnVideoError(AccessDenied) 相当");
            LoadAndPlay(backend, player, Item().Url);
            backend.NotifyVideoError(VideoErrorKind.AccessDenied);
            Line($"    {observer.LastError} / State={backend.GetState()}");

            Line("(c) 読み込みが終わらない(タイムアウト)");
            var slow = NewBackend(out _);
            slow.LoadTimeoutSeconds = 5f;
            var slowObserver = new CountingObserver();
            slow.AddObserver(slowObserver);

            slow.Load(Item().Url);
            for (int i = 0; i < 6; i++) slow.Tick(1f);
            Line($"    {slowObserver.LastError} / Kind={slow.LastErrorKind} / State={slow.GetState()}");
            Line($"    Error 通知は合計 {observer.ErrorCount + slowObserver.ErrorCount} 回");

            Flush();
        }

        // ───────── 5. DummyVideoBackend との差し替え ─────────

        private void RunSwapScenario()
        {
            Section("5. DummyVideoBackend を差し替えるだけで動く");

            var dummy = new DummyVideoBackend("DummyVideoBackend", _logger);

            var player = new SimulatedVRCVideoPlayer(_urls) { AutoCompleteLoading = true };
            var vrchat = new VRChatVideoBackend("VRChatVideoBackend", player, _urls, _logger);

            Line("同じ手順を 2 つの Backend に流します(呼び出し側のコードは 1 つ)。");
            Line($"  {"操作",-10}{"DummyVideoBackend",-22}VRChatVideoBackend");

            foreach (var step in Steps())
            {
                string a = step.Value(dummy);
                string b = step.Value(vrchat);
                string mark = a == b ? " " : "≠";
                Line($"{mark} {step.Key,-10}{a,-22}{b}");
            }

            Line("上位から見た振る舞いが一致するので、new する行を 1 つ変えるだけで置き換わります。");
            Flush();
        }

        /// <summary>2 つの Backend に同じ手順を流すための操作表。</summary>
        private List<KeyValuePair<string, Func<IVideoBackend, string>>> Steps()
        {
            string url = Item().Url;

            return new List<KeyValuePair<string, Func<IVideoBackend, string>>>
            {
                Step("CanPlay", b => b.CanPlay(url).ToString()),
                Step("Load", b => $"{b.Load(url)} / {b.GetState()}"),
                Step("Play", b => $"{b.Play()} / {b.GetState()}"),
                Step("Pause", b => $"{b.Pause()} / {b.GetState()}"),
                Step("Resume", b => $"{b.Resume()} / {b.GetState()}"),
                Step("Stop", b => $"{b.Stop()} / {b.GetState()}"),
                Step("Play(再)", b => $"{b.Play()} / {b.GetState()}"),
            };
        }

        private static KeyValuePair<string, Func<IVideoBackend, string>> Step(
            string name, Func<IVideoBackend, string> value)
        {
            return new KeyValuePair<string, Func<IVideoBackend, string>>(name, value);
        }

        // ───────── 6. 上位は無変更 ─────────

        private void RunSessionScenario()
        {
            Section("6. BackendAdapter / PlayerSession / Queue / Recommendation は無変更");

            var backend = NewBackend(out var player);
            player.AutoCompleteLoading = true;   // 読み込みが即終わる状況(実機の 2 回目以降に近い)

            // ← Phase2-4(B) と同じ行。中身が Dummy から VRChat に変わっただけ。
            var adapter = new VideoBackendAdapter("VideoAdapter", backend, _logger);

            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, _logger);
            var mediaPlayer = new MediaPlayer(backendManager, _logger);
            var engine = new RecommendationEngine(
                _catalog, RecommendationRule.CreateDefault(), new System.Random(1));
            var session = new PlayerSession(
                "vrchat-video", mediaPlayer, _catalog, engine, new System.Random(1), _logger);

            session.RegisterBackend(adapter);
            session.AutoQueueEnabled = false;
            session.SetTracks(_videoIds);

            Line($"SetTracks: [{string.Join(", ", _videoIds)}]");
            Line($"session.Play() -> {session.Play()}");
            Line($"  PlayerSession={session.PlaybackState} / BackendState={session.BackendState}"
                 + $" / 担当={(backendManager.ActiveBackend != null ? backendManager.ActiveBackend.Name : "(none)")}");
            Line($"  動画プレイヤー側 = {backend.GetState()}");

            Line($"session.Pause()  -> {session.Pause()} / {session.BackendState}");
            Line($"session.Resume() -> {session.Resume()} / {session.BackendState}");

            // 動画が終わると PlayerSession が次を決める(Queue が空なので Exhausted)
            Advance(player, backend, player.DurationSeconds + 1f);
            Line($"動画が終わる -> PlayerSession={session.PlaybackState}"
                 + $" / Current={session.CurrentMediaId ?? "(none)"}");
            Line("PlayerSession も Queue も Recommendation も、動画のことを知らないまま動いています。");

            Flush();
        }

        // ───────── 7. 生配信 ─────────

        private void RunLiveStreamScenario()
        {
            Section("7. 生配信(VRCUnityVideoPlayer と VRCAVProVideoPlayer の違い)");

            const string live = "rtsp://example.com/live/stage-a";
            var table = new CatalogVideoUrlTable(new[] { live });

            var unity = new VRChatVideoBackend(
                "UnityPlayerBackend",
                new SimulatedVRCVideoPlayer(table, VRCVideoPlayerKind.Unity),
                table, _logger);

            // 生配信は長さが分からない(実機も 0 や無限大を返す)
            var avproPlayer = new SimulatedVRCVideoPlayer(table, VRCVideoPlayerKind.AVPro)
            {
                DurationSeconds = 0f,
                AutoCompleteLoading = true,
            };
            var avpro = new VRChatVideoBackend("AVProPlayerBackend", avproPlayer, table, _logger);

            Line($"VRCUnityVideoPlayer  -> CanPlay(rtsp) = {unity.CanPlay(live)}(生配信は扱えない)");
            Line($"VRCAVProVideoPlayer  -> CanPlay(rtsp) = {avpro.CanPlay(live)}");

            avpro.Load(live);
            Line($"AVPro で Load -> {avpro.GetState()} / 長さ={avpro.GetDuration():0.0}s(不明)"
                 + $" / CanSeek={avpro.CanSeek}");
            Line("  長さが分からない生配信ではシークを断ります(上位の Seek は握りつぶされます)。");

            Flush();
        }

        // ───────── 組み立て / 出力 ─────────

        private MediaItem Item()
        {
            string id = _videoIds != null && _videoIds.Length > 0 ? _videoIds[0] : "video-001";
            return _catalog.FindById(id) ?? _catalog.FilterByType(MediaType.Video)[0];
        }

        private VRChatVideoBackend NewBackend(out SimulatedVRCVideoPlayer player)
        {
            player = new SimulatedVRCVideoPlayer(_urls);
            return new VRChatVideoBackend("VRChatVideoBackend", player, _urls, _logger);
        }

        private static void LoadAndPlay(
            VRChatVideoBackend backend, SimulatedVRCVideoPlayer player, string url)
        {
            backend.Load(url);
            player.CompleteLoading();
            backend.Tick(0.016f);
            backend.Play();
        }

        /// <summary>実機で時間が進むのと同じことを 0.5 秒刻みで行う。</summary>
        private static void Advance(
            SimulatedVRCVideoPlayer player, VRChatVideoBackend backend, float seconds)
        {
            const float stepSeconds = 0.5f;
            for (float elapsed = 0f; elapsed < seconds; elapsed += stepSeconds)
            {
                player.Advance(stepSeconds);
                backend.Tick(stepSeconds);
            }
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

        /// <summary>直前のシナリオで増えたログ行を出力する。</summary>
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
            public int EndCount { get; private set; }
            public int ErrorCount { get; private set; }
            public string LastError { get; private set; }

            public void OnVideoReady(string url) { }
            public void OnVideoStart(string url) { }
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
