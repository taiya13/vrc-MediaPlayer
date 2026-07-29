using System.Text;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Library.Playback;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;
using SmartMediaPlatform.Video;
using SmartMediaPlatform.Video.Data;
using UnityEngine;

namespace SmartMediaPlatform.Library.Demo
{
    /// <summary>
    /// Phase4-4: <b>Library → Queue → Player → VideoBackend</b> を
    /// <b>VRChat SDK 無しで</b>通しで確認するデモ。
    ///
    /// 実機の動画プレイヤーの位置に <see cref="SimulatedVRCVideoPlayer"/> を置くので、
    /// <b>SDK が入っていない環境でも配線をそのまま確かめられます</b>
    /// (SDK 版との違いは <c>IVRCVideoPlayer</c> の実装だけです)。
    ///
    /// <code>
    /// SDK 無し(このデモ)  : SimulatedVRCVideoPlayer
    /// SDK あり(実機)      : VRCVideoPlayerBridge → VRCAVProVideoPlayer / VRCUnityVideoPlayer
    /// </code>
    ///
    /// 確認する順番:
    /// <list type="number">
    /// <item>DummyBackend ではなく VideoBackend を載せる(差し替えは 1 行)</item>
    /// <item>Library で選ぶ → URL の解決 → 実際の Load / Play まで届く</item>
    /// <item>読み込み完了(OnVideoReady)を待って再生が始まる</item>
    /// <item>再生終了(OnVideoEnd)で次へ進む</item>
    /// <item>再生失敗(OnVideoError)でも Queue と表示が壊れない</item>
    /// <item>焼き込まれていない URL は入口で断られる</item>
    /// <item>AVPro と Unity 版の違い(生配信の可否)</item>
    /// </list>
    /// </summary>
    public sealed class VideoBackendFlowConsoleDemo : MonoBehaviour
    {
        [Header("実行")]
        [SerializeField] private bool _runOnStart = true;

        private StringBuilder _out;
        private ListBackendLogger _logger;
        private int _flushed;

        private void Start()
        {
            if (_runOnStart) Run();
        }

        [ContextMenu("Run Video Backend Flow Scenarios")]
        public void Run()
        {
            _out = new StringBuilder();
            _logger = new ListBackendLogger();
            _flushed = 0;

            _out.AppendLine("=== Phase4-4 Video Backend Flow Demo (SDK 不要) ===");

            var rig = new Rig(_logger);

            ShowAssembly(rig);
            ShowPlayReachesTheVideoPlayer(rig);
            ShowReadyThenPlay(rig);
            ShowEndedAdvance(rig);
            ShowErrorRecovery(rig);
            ShowUnbakedUrl(rig);
            ShowPlayerKinds(rig);

            Debug.Log(_out.ToString());
        }

        // ───────── 1. 差し替え ─────────

        private void ShowAssembly(Rig rig)
        {
            Section("1. DummyBackend ではなく VideoBackend を載せる");

            Line("Phase4-3(ログだけ):");
            Line("  backendManager.RegisterBackend(new DummyBackend(...));");
            Line("");
            Line("Phase4-4(実際に動画プレイヤーを叩く):");
            Line("  backendManager.RegisterBackend(new VideoBackendAdapter(...));");
            Line("");
            Line($"いま載っているもの = {rig.Adapter.Name}");
            Line($"  中身            = {rig.Backend}");
            Line($"  動画プレイヤー   = {rig.Player.GetType().Name}(SDK 版では VRCVideoPlayerBridge)");
            Line($"  焼き込み URL     = {rig.Urls.Count} 件");
            Line("");
            Line("→ PlaybackFlow / PlayerSession / Queue / MediaLibrary は");
            Line("  この違いを 1 行も知りません。");
            Flush();
        }

        // ───────── 2. 実際に Load まで届く ─────────

        private void ShowPlayReachesTheVideoPlayer(Rig rig)
        {
            Section("2. Library で選ぶ → 動画プレイヤーの Load まで届く");

            rig.Flow.Library.ShowOnly(MediaType.Video);
            rig.Flow.Library.Select(0);

            var meta = rig.Flow.Library.SelectedItem;
            Line($"選択 = {meta.Title}({meta.MediaId})");
            Line($"  UI が渡すもの = {rig.Flow.Library.SelectedRef}(URL は入っていない)");

            rig.Flow.LibraryPlayback.PlaySelected();
            rig.Flow.Tick();

            Line("");
            Line($"VideoBackend が解決した URL = {rig.Backend.GetCurrentUrl()}");
            Line($"動画プレイヤーが受け取った URL = {rig.Player.CurrentUrl}");
            Line("→ URL が現れるのは VideoBackend から先だけです。");
            Flush();
        }

        // ───────── 3. Ready → Play ─────────

        private void ShowReadyThenPlay(Rig rig)
        {
            Section("3. 読み込み完了(OnVideoReady)を待って再生が始まる");

            rig.Reset();
            rig.Player.AutoCompleteLoading = false;   // 実機と同じく非同期にする

            rig.Flow.Library.ShowOnly(MediaType.Video);
            rig.Flow.LibraryPlayback.PlayAt(0);
            rig.Flow.Tick();

            Line($"Play 直後       -> Backend={rig.Backend.GetState()}"
                 + $" / Session=[{rig.Flow.NowPlaying.State}]");
            Line("  → まだ読み込み中。再生は予約されています。");

            rig.Player.CompleteLoading();
            rig.Bridge.OnVideoReady();
            rig.Flow.Tick();

            Line($"OnVideoReady 後 -> Backend={rig.Backend.GetState()}"
                 + $" / Session=[{rig.Flow.NowPlaying.State}]");
            Line($"  再生中 = {rig.Flow.NowPlaying.FormatTitle()}");

            rig.Player.AutoCompleteLoading = true;
            Flush();
        }

        // ───────── 4. Ended で次へ ─────────

        private void ShowEndedAdvance(Rig rig)
        {
            Section("4. 再生終了(OnVideoEnd)で次へ進む");

            for (int i = 1; i <= 3; i++)
            {
                string before = rig.Flow.NowPlaying.MediaId;
                rig.FinishCurrent();
                rig.Flow.Tick();

                Line($"  {i}. OnVideoEnd({before}) -> {rig.Flow.NowPlaying.MediaId}"
                     + $" [{rig.Flow.NowPlaying.State}] Queue={rig.Flow.Queue.Count}");
                Line($"     動画プレイヤーの URL = {rig.Player.CurrentUrl}");
            }

            Line("");
            Line("→ 実機では OnVideoEnd が VideoEventBridge 経由で届きます。");
            Line("  そこから先(次の曲を決める)は PlayerSession の仕事で、変わっていません。");
            Flush();
        }

        // ───────── 5. Error でも壊れない ─────────

        private void ShowErrorRecovery(Rig rig)
        {
            Section("5. 再生失敗(OnVideoError)でも Queue と表示が壊れない");

            string failing = rig.Flow.NowPlaying.MediaId;
            int queueBefore = rig.Flow.Queue.Count;

            rig.Bridge.OnVideoError(VideoErrorKind.InvalidUrl);
            rig.Flow.Tick();

            Line($"OnVideoError({failing})");
            Line($"  Backend      = {rig.Backend.GetState()} / {rig.Backend.LastErrorKind}");
            Line($"  Session      = [{rig.Flow.NowPlaying.State}]");
            Line($"  Queue        = {queueBefore} 件 -> {rig.Flow.Queue.Count} 件");
            Line("");
            Line("→ この層は「失敗した」ことを見せるだけです。");
            Line("  失敗から自動で立て直すのは AutoPlayController(Phase3-4)の役目で、");
            Line("  必要なら同じ PlayerSession に載せられます。");

            // 次の曲へ手動で進めて後片付け
            rig.Flow.Session.Next();
            rig.Flow.Tick();
            Line($"  Next() -> {rig.Flow.NowPlaying.MediaId} [{rig.Flow.NowPlaying.State}]");
            Flush();
        }

        // ───────── 6. 焼き込まれていない URL ─────────

        private void ShowUnbakedUrl(Rig rig)
        {
            Section("6. 焼き込まれていない URL は入口で断られる");

            Line($"CanPlay(焼き込み済み)   = {rig.Backend.CanPlay(rig.Urls.Urls[0])}");
            Line($"CanPlay(焼き込み無し)   = {rig.Backend.CanPlay("https://example.com/not-baked")}");
            Line("");
            Line("→ VRCUrl は実行時に作れないため、ベイクされていない URL は");
            Line("  再生を試みる前に断られます(Phase3-1 からの約束)。");
            Flush();
        }

        // ───────── 7. AVPro / Unity ─────────

        private void ShowPlayerKinds(Rig rig)
        {
            Section("7. AVPro と Unity 版の違い(切り替えても上位は同じ)");

            var urls = new CatalogVideoUrlTable(new[] { "rtsp://example.com/live" });

            var unity = new VRChatVideoBackend(
                "Unity", new SimulatedVRCVideoPlayer(urls, VRCVideoPlayerKind.Unity), urls);
            var avpro = new VRChatVideoBackend(
                "AVPro", new SimulatedVRCVideoPlayer(urls, VRCVideoPlayerKind.AVPro), urls);

            Line($"生配信 rtsp:// を扱えるか");
            Line($"  VRCUnityVideoPlayer -> {unity.CanPlay("rtsp://example.com/live")}");
            Line($"  VRCAVProVideoPlayer -> {avpro.CanPlay("rtsp://example.com/live")}");
            Line("");
            Line("→ 違いを見るのは VideoBackend の CanPlay だけです。");
            Line("  Phase4-4 で実機の標準を AVPro にしました。");
            Line("  切り替えは VRChatVideoBackendHost の Preferred Player(Inspector)です。");
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

        /// <summary>Catalog → PlaybackFlow → PlayerSession → VideoBackend を 1 式。</summary>
        private sealed class Rig
        {
            public readonly PlaybackFlow Flow;
            public readonly VRChatVideoBackend Backend;
            public readonly VideoBackendAdapter Adapter;
            public readonly VideoEventBridge Bridge;
            public readonly SimulatedVRCVideoPlayer Player;
            public readonly CatalogVideoUrlTable Urls;

            private readonly IBackendLogger _logger;
            private readonly IMediaCatalog _catalog;

            public Rig(IBackendLogger logger)
            {
                _logger = logger;
                _catalog = new MediaCatalog(new VideoCatalogSource(), new System.Random(1));

                // 実機では VRCUrlTable(編集時に焼き込む)。ここでは同じ形の代役。
                Urls = new CatalogVideoUrlTable(_catalog);

                Player = new SimulatedVRCVideoPlayer(Urls) { AutoCompleteLoading = true };
                Backend = new VRChatVideoBackend("VRChatVideoBackend", Player, Urls, logger);
                Bridge = new VideoEventBridge(Backend, logger);
                Adapter = new VideoBackendAdapter("VideoAdapter", Backend, logger);

                var queue = new MediaQueue();
                var backendManager = new BackendManager(queue, logger);
                backendManager.RegisterBackend(Adapter);   // ← DummyBackend との唯一の違い

                var mediaPlayer = new MediaPlayer(backendManager, logger);
                var engine = new RecommendationEngine(_catalog, RecommendationRule.CreateDefault());
                var session = new PlayerSession(
                    "video-flow", mediaPlayer, _catalog, engine, new System.Random(1), logger);

                Flow = PlaybackFlow.Create(_catalog, session, 5, logger);
            }

            /// <summary>いま鳴っているものを終端まで進めて、実機の OnVideoEnd を流す。</summary>
            public void FinishCurrent()
            {
                if (Backend.GetState() == VideoPlayerState.Loading && Player.AutoCompleteLoading)
                {
                    Player.CompleteLoading();
                    Bridge.OnVideoReady();
                }

                Player.FinishPlayback();
                Bridge.OnVideoEnd();
            }

            /// <summary>再生を止めて最初からやり直せるようにする。</summary>
            public void Reset()
            {
                Flow.Session.Stop();
                Flow.Session.ClearQueue();
            }
        }
    }
}
