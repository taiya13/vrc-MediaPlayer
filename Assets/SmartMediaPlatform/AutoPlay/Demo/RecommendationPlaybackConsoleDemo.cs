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
    /// Phase3-3 のおすすめ再生ループを Console だけで確認するデモ。
    ///
    /// <b>VRChat SDK が無くても走ります。</b>
    /// 実機の動画プレイヤーの位置に <see cref="SimulatedVRCVideoPlayer"/> を置き、
    /// 再生の進行は <see cref="VideoEventBridge"/> 経由の
    /// <c>OnVideoReady</c> / <c>OnVideoEnd</c> だけで進めます
    /// (<b>Ended イベントだけで次の動画へ遷移できる</b>ことを示すため、
    ///  最初の <c>Start()</c> 以降はセッションを直接操作しません)。
    ///
    /// 空の GameObject にアタッチして Play すれば走ります。
    /// </summary>
    public sealed class RecommendationPlaybackConsoleDemo : MonoBehaviour
    {
        [Header("おすすめ再生")]
        [Tooltip("起点にする動画の Catalog ID。空ならカタログの先頭")]
        [SerializeField] private string _seedId = "video-001";

        [Tooltip("何本まで自動で流すか")]
        [SerializeField] private int _playCount = 8;

        [Header("実行")]
        [SerializeField] private bool _runOnStart = true;

        private StringBuilder _out;
        private ListBackendLogger _logger;
        private int _flushed;

        private void Start()
        {
            if (_runOnStart) Run();
        }

        [ContextMenu("Run Recommendation Playback Scenarios")]
        public void Run()
        {
            _out = new StringBuilder();
            _logger = new ListBackendLogger();
            _flushed = 0;

            _out.AppendLine("=== Phase3-3 Recommendation Playback Integration Demo ===");

            RunLoopScenario();
            RunEmptyQueueScenario();
            RunMixedCatalogScenario();
            RunPickNextScenario();

            Debug.Log(_out.ToString());
        }

        // ───────── 1. おすすめ再生ループ ─────────

        private void RunLoopScenario()
        {
            Section("1. Catalog の動画だけでおすすめ再生ループが回る");

            var rig = new Rig(new VideoCatalogSource(), _logger);

            Line($"カタログ: 動画 {rig.Catalog.FilterByType(MediaType.Video).Count} 本");
            Line($"フィルタ: {rig.Service.Filter}");

            int added = rig.Service.Start(_seedId);
            Line($"Start(\"{_seedId}\") -> Queue に {added} 本 / 再生開始");
            Line($"  再生中 = {rig.Session.CurrentMediaId} ({rig.Session.PlaybackState})");
            Line($"  Queue  = {rig.DescribeQueue()}");

            // ここから先は Ended イベントだけで進める
            Line("");
            Line("以降は OnVideoEnd(実機イベント)だけで進めます:");
            for (int i = 1; i <= _playCount; i++)
            {
                string before = rig.Session.CurrentMediaId;
                rig.FinishCurrentVideo();

                Line($"  {i,2}. Ended({before}) -> 次は {rig.Session.CurrentMediaId ?? "(none)"}"
                     + $" [{rig.Session.PlaybackState}] Queue={rig.Session.Queue.Count}"
                     + $" 担当={rig.ActiveBackendName}");

                if (rig.Session.PlaybackState == PlaybackState.Exhausted) break;
            }

            Line("");
            Line($"サービスの集計: {rig.Service.Describe()}");
            Line($"再生した順: {string.Join(" -> ", rig.Played)}");
            Flush();
        }

        // ───────── 2. Queue が空でも補充される ─────────

        private void RunEmptyQueueScenario()
        {
            Section("2. Queue が空になってもおすすめから補充される");

            var rig = new Rig(new VideoCatalogSource(), _logger);
            rig.Service.Start(_seedId);
            Line($"開始直後 -> Queue={rig.Session.Queue.Count}");

            rig.Session.ClearQueue();
            Line($"Queue を強制的に空にする -> Queue={rig.Session.Queue.Count}");

            int refilled = rig.Service.EnsureQueueFilled();
            Line($"EnsureQueueFilled() -> {refilled} 本を補充 / Queue={rig.Session.Queue.Count}");
            Line($"  中身: {rig.DescribeQueue()}");
            Line("おすすめが尽きても、直近の除外を緩めて積み続けます(AllowRepeatWhenExhausted)。");

            Flush();
        }

        // ───────── 3. 再生できない種別は積まれない ─────────

        private void RunMixedCatalogScenario()
        {
            Section("3. Music が混ざったカタログでも、動画だけが積まれる");

            var rig = new Rig(new MixedCatalogSource(), _logger);

            Line($"カタログ: 動画 {rig.Catalog.FilterByType(MediaType.Video).Count} 本"
                 + $" / 音楽 {rig.Catalog.FilterByType(MediaType.Music).Count} 曲"
                 + $" / Podcast {rig.Catalog.FilterByType(MediaType.Podcast).Count} 本");
            Line($"登録したバックエンド: VideoAdapter のみ");
            Line($"フィルタ: {rig.Service.Filter}(登録済みバックエンドに直接聞く)");

            rig.Service.Start(_seedId);
            for (int i = 0; i < 5; i++) rig.FinishCurrentVideo();

            Line($"5 本再生したあとの Queue: {rig.DescribeQueue()}");
            Line($"積まれなかった候補(再生できない種別)= {rig.Service.FilteredOutCount} 件");

            bool onlyVideo = true;
            foreach (var entry in rig.Session.Queue.GetAll())
            {
                if (entry.Item.Type != MediaType.Video && entry.Item.Type != MediaType.Live)
                {
                    onlyVideo = false;
                }
            }
            Line($"Queue に動画以外が混ざっていないか -> {(onlyVideo ? "混ざっていない" : "混ざっている")}");
            Line($"再生した順: {string.Join(" -> ", rig.Played)}");
            Line("Recommendation のアルゴリズムは変更していません。積む直前に弾いているだけです。");

            Flush();
        }

        // ───────── 4. MediaId だけを受け渡す ─────────

        private void RunPickNextScenario()
        {
            Section("4. Recommendation からは MediaId だけを受け取る");

            var rig = new Rig(new VideoCatalogSource(), _logger);
            rig.Service.Start(_seedId);

            string next = rig.Service.PickNextMediaId(_seedId);
            Line($"PickNextMediaId(\"{_seedId}\") -> \"{next}\"(string の MediaId)");
            Line("  URL も MediaItem も返しません。URL を知るのは VideoBackend だけです。");
            Line($"  実際に VideoBackend が読み込んでいる URL = {rig.Backend.GetCurrentUrl()}");
            Line("  この URL は Catalog に事前登録されたもので、実行時には生成していません。");

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

        /// <summary>
        /// Catalog → Recommendation → Queue → PlayerSession → Adapter → VideoBackend を
        /// 1 式組み立てたもの。<b>組み立て方は Phase2-4(B) から変わっていません。</b>
        /// </summary>
        private sealed class Rig
        {
            public readonly IMediaCatalog Catalog;
            public readonly PlayerSession Session;
            public readonly RecommendationPlaybackService Service;
            public readonly VRChatVideoBackend Backend;
            public readonly VideoEventBridge Bridge;
            public readonly System.Collections.Generic.List<string> Played =
                new System.Collections.Generic.List<string>();

            private readonly SimulatedVRCVideoPlayer _player;
            private readonly BackendManager _backendManager;

            public Rig(IMediaCatalogSource source, IBackendLogger logger)
            {
                Catalog = new MediaCatalog(source, new System.Random(1));

                // Backend(Phase3-1)
                var urls = new CatalogVideoUrlTable(Catalog);
                _player = new SimulatedVRCVideoPlayer(urls) { AutoCompleteLoading = true };
                Backend = new VRChatVideoBackend("VRChatVideoBackend", _player, urls, logger);
                Bridge = new VideoEventBridge(Backend, logger);

                // Adapter(Phase2-4B・変更なし)
                var adapter = new VideoBackendAdapter("VideoAdapter", Backend, logger);

                // Queue / Player / Session(変更なし)
                var queue = new MediaQueue();
                _backendManager = new BackendManager(queue, logger);
                var mediaPlayer = new MediaPlayer(_backendManager, logger);
                var engine = new RecommendationEngine(
                    Catalog, RecommendationRule.CreateDefault(), new System.Random(1));
                Session = new PlayerSession(
                    "autoplay", mediaPlayer, Catalog, engine, new System.Random(1), logger);

                // Phase3-3 の接続層。登録もここへ任せる(Ended の受け取り順を保証するため)
                Service = new RecommendationPlaybackService(
                    Session, Catalog, engine,
                    new BackendPlaybackFilter(_backendManager), logger);
                Service.RegisterBackend(adapter);
            }

            public string ActiveBackendName =>
                _backendManager.ActiveBackend != null ? _backendManager.ActiveBackend.Name : "(none)";

            /// <summary>いま再生中の動画を最後まで再生させ、実機の OnVideoEnd を流す。</summary>
            public void FinishCurrentVideo()
            {
                if (Session.CurrentMediaId != null
                    && (Played.Count == 0 || Played[Played.Count - 1] != Session.CurrentMediaId))
                {
                    Played.Add(Session.CurrentMediaId);
                }

                // 読み込みが終わっていなければ、実機の OnVideoReady を先に流す
                if (Backend.GetState() == VideoPlayerState.Loading)
                {
                    _player.CompleteLoading();
                    Bridge.OnVideoReady();
                }

                _player.FinishPlayback();
                Bridge.OnVideoEnd();   // ← これだけで次の動画へ進む
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
