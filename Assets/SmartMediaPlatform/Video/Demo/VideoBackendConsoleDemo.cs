using System.Collections;
using System.Linq;
using System.Text;
using SmartMediaPlatform.Adapter;
using SmartMediaPlatform.Adapter.Audio;
using SmartMediaPlatform.Audio;
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
    /// Video Backend Adapter の動作を Console だけで確認するデモ。
    ///
    /// 動画は再生しません(<see cref="DummyVideoBackend"/> はログのみ)。
    /// 見どころは、動画プレイヤーの語彙(URL・秒・独自の状態)が
    /// プラットフォーム共通の契約へ翻訳され、
    /// <b>PlayerSession / Queue / Recommendation を変更せずに動く</b>ことです。
    ///
    /// 空の GameObject にアタッチして Play すれば走ります。
    /// </summary>
    public sealed class VideoBackendConsoleDemo : MonoBehaviour
    {
        [Header("再生する動画・曲(Catalog の ID)")]
        [SerializeField]
        private string[] _trackIds = { "video-001", "music-001", "music-002" };

        [Header("実行")]
        [SerializeField] private bool _runOnStart = true;

        [Tooltip("最後に音声だけ実際に鳴らすところまで通すか")]
        [SerializeField] private bool _playAudioAtTheEnd = true;

        [Tooltip("再生を聴かせる時間(秒)")]
        [SerializeField] private float _listenSeconds = 4f;

        private IMediaCatalog _catalog;
        private ListBackendLogger _logger;
        private StringBuilder _out;
        private int _flushed;

        private void Start()
        {
            if (_runOnStart) StartCoroutine(RunAll());
        }

        [ContextMenu("Run Video Backend Scenarios (no audio)")]
        public void RunWithoutAudio()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _logger = new ListBackendLogger();
            _out = new StringBuilder();
            _out.AppendLine("=== Phase2-4 Video Backend Adapter Demo ===");

            RunDummyVideoBackendScenario();
            RunTranslationScenario();
            RunAsyncLoadingScenario();
            RunErrorScenario();
            RunBackendManagerScenario();
            RunSessionScenario();

            Debug.Log(_out.ToString());
        }

        public IEnumerator RunAll()
        {
            RunWithoutAudio();

            if (_playAudioAtTheEnd) yield return RunMixedPlaybackScenario();
        }

        // ───────── 1. DummyVideoBackend 単体 ─────────

        private void RunDummyVideoBackendScenario()
        {
            Section("1. DummyVideoBackend(動画プレイヤー側の API)");

            var video = new DummyVideoBackend("DummyVideoBackend", _logger);
            var url = _catalog.FindById("video-001").Url;

            Line($"CanPlay(\"{url}\") -> {video.CanPlay(url)}");
            Line($"CanPlay(\"\")     -> {video.CanPlay("")}");
            Line($"Load    -> {video.Load(url)} / State={video.GetState()}");
            Line($"Play    -> {video.Play()} / State={video.GetState()}");
            Line($"Pause   -> {video.Pause()} / State={video.GetState()}");
            Line($"Resume  -> {video.Resume()} / State={video.GetState()}");
            Line($"Seek 30s-> {video.Seek(30f)} / Time={video.GetTime():0.0}s / {video.GetDuration():0.0}s");
            Line($"Stop    -> {video.Stop()} / State={video.GetState()}");

            video.Play();
            video.SimulateFinished();
            Line($"再生終了 -> State={video.GetState()}");

            Flush();
        }

        // ───────── 2. 翻訳(動画プレイヤー → プラットフォーム) ─────────

        private void RunTranslationScenario()
        {
            Section("2. VideoBackendAdapter による翻訳");

            var video = new DummyVideoBackend("DummyVideoBackend", _logger) { Duration = 200f };
            var adapter = new VideoBackendAdapter("VideoAdapter", video, _logger);
            var item = _catalog.FindById("video-001");

            Line("対象の翻訳: MediaItem -> URL");
            adapter.Load(item);
            Line($"  Load({item.Id}) -> 動画プレイヤーの URL = {video.GetCurrentUrl()}");

            Line("状態の翻訳: VideoPlayerState -> BackendState");
            adapter.Play();
            Line($"  動画プレイヤー: {video.GetState()}  ->  上位から見た状態: {adapter.GetState()}");
            video.SimulateFinished();
            Line($"  動画プレイヤー: {video.GetState()}  ->  上位から見た状態: {adapter.GetState()}");

            Line("シークの翻訳: 割合(0〜1) -> 秒");
            adapter.Load(item);
            adapter.Play();
            adapter.Seek(0.25f);
            Line($"  Seek(0.25) -> 動画プレイヤーへは {video.GetTime():0.0} 秒 "
                 + $"(長さ {adapter.GetDuration():0.0} 秒 / 進捗 {adapter.GetProgress() * 100f:0}%)");

            Flush();
        }

        // ───────── 3. 非同期読み込み ─────────

        private void RunAsyncLoadingScenario()
        {
            Section("3. 非同期の読み込み(実機の動画プレイヤーを再現)");

            var video = new DummyVideoBackend("AsyncVideoBackend", _logger)
            {
                AutoCompleteLoading = false,
            };
            var adapter = new VideoBackendAdapter("AsyncVideoAdapter", video, _logger);

            adapter.Load(_catalog.FindById("video-001"));
            Line($"Load 直後 -> 上位から見た状態 = {adapter.GetState()} (まだ読み込み中)");
            Line($"  この間 Play は拒否される -> {adapter.Play()}");

            video.CompleteLoading();
            Line($"読み込み完了 -> 上位から見た状態 = {adapter.GetState()}");
            Line($"  Play できるようになる -> {adapter.Play()} / State={adapter.GetState()}");

            Flush();
        }

        // ───────── 4. エラー ─────────

        private void RunErrorScenario()
        {
            Section("4. エラーの翻訳");

            var video = new DummyVideoBackend("DummyVideoBackend", _logger);
            var adapter = new VideoBackendAdapter("VideoAdapter", video, _logger);

            adapter.Load(_catalog.FindById("music-001"));
            Line($"音楽を動画アダプタに渡す -> HasError={adapter.HasError}");
            Line($"  {adapter.LastError}");

            adapter.ClearError();
            adapter.Load(_catalog.FindById("video-001"));
            video.SimulateError("URL の読み込みに失敗しました(ダミー)");
            Line($"動画プレイヤー側の失敗 -> HasError={adapter.HasError}");
            Line($"  {adapter.LastError}");
            Line($"  上位から見た状態 = {adapter.GetState()}");

            Flush();
        }

        // ───────── 5. BackendManager だけが種類を判断する ─────────

        private void RunBackendManagerScenario()
        {
            Section("5. BackendManager だけが Backend の種類を判断する");

            var adapters = BuildAdapters(out _, out _);
            _out.AppendLine(adapters.Describe());

            var queue = new MediaQueue();
            var manager = new BackendManager(queue, _logger);
            adapters.AttachAll(manager.RegisterBackend);

            foreach (var id in new[] { "music-001", "video-001", "podcast-001" })
            {
                var item = _catalog.FindById(id);
                var backend = manager.SelectBackendFor(item);
                Line($"{id} ({item.Type}) -> "
                     + (backend != null ? backend.Name : "(扱えるバックエンドなし)"));
            }

            Flush();
        }

        // ───────── 6. PlayerSession は変更不要 ─────────

        private void RunSessionScenario()
        {
            Section("6. PlayerSession / Queue / Recommendation は変更不要");

            var adapters = BuildAdapters(out _, out _);

            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, _logger);
            var player = new MediaPlayer(backendManager, _logger);
            var engine = new RecommendationEngine(
                _catalog, RecommendationRule.CreateDefault(), new System.Random(1));
            var session = new PlayerSession(
                "video-demo", player, _catalog, engine, new System.Random(1), _logger);

            adapters.AttachAll(session.RegisterBackend);

            session.AutoQueueEnabled = false;
            session.SetTracks(_trackIds);
            Line($"SetTracks: [{string.Join(", ", _trackIds)}]");

            session.Play();
            Line($"Play -> {Describe(session)}  担当={ActiveName(backendManager)}");

            // 種別が変わっても、上位は同じ Next() を呼ぶだけ
            for (int i = 0; i < _trackIds.Length - 1; i++)
            {
                session.Next();
                Line($"Next -> {Describe(session)}  担当={ActiveName(backendManager)}");
            }

            Line("Queue も Recommendation も動画のことを知らないまま動いている。");
            Flush();
        }

        // ───────── 7. 音声だけ実際に鳴らす ─────────

        private IEnumerator RunMixedPlaybackScenario()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 7. Audio と Video を混ぜて再生(音声のみ音が鳴ります) ===");

            var logger = new ListBackendLogger();
            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());

            var host = GetComponent<AudioBackendHost>();
            if (host == null) host = gameObject.AddComponent<AudioBackendHost>();
            host.EnsureBuilt(logger);
            host.PrepareClipsFor(catalog);

            var audioAdapter = new AudioBackendAdapter(
                "AudioAdapter", host.Backend, host.Library, logger);
            var videoBackend = new DummyVideoBackend("DummyVideoBackend", logger);
            var videoAdapter = new VideoBackendAdapter("VideoAdapter", videoBackend, logger);

            var adapters = new BackendAdapterManager(logger);
            adapters.Register(audioAdapter);
            adapters.Register(videoAdapter);

            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, logger);
            var player = new MediaPlayer(backendManager, logger);
            var engine = new RecommendationEngine(catalog, RecommendationRule.CreateDefault());
            var session = new PlayerSession(
                "mixed", player, catalog, engine, new System.Random(1), logger);

            adapters.AttachAll(session.RegisterBackend);

            session.AutoQueueEnabled = false;
            session.SetTracks(_trackIds);
            session.Play();
            sb.AppendLine($"  開始: {Describe(session)}  担当={ActiveName(backendManager)}");

            float waited = 0f;
            string lastId = session.CurrentMediaId;

            while (waited < _listenSeconds)
            {
                waited += Time.deltaTime;

                // ダミー動画は自分で進まないので進めてやる。
                // 一定時間が過ぎたら「再生が終わった」ことにする。
                videoBackend.Advance(Time.deltaTime);
                if (videoBackend.GetState() == VideoPlayerState.Playing
                    && videoBackend.GetTime() > 1.5f)
                {
                    videoBackend.SimulateFinished();
                }

                if (session.CurrentMediaId != lastId)
                {
                    sb.AppendLine($"  {waited:0.0}s: {Describe(session)}  担当={ActiveName(backendManager)}");
                    lastId = session.CurrentMediaId;
                }

                yield return null;
            }

            session.Stop();

            foreach (var line in logger.Lines) sb.AppendLine("    " + line);
            sb.Append($"  最終: {Describe(session)}");

            Debug.Log(sb.ToString());
        }

        // ───────── 組み立て / 出力 ─────────

        private BackendAdapterManager BuildAdapters(
            out AudioBackendAdapter audio, out VideoBackendAdapter video)
        {
            var library = new AudioClipLibrary();
            foreach (var item in _catalog.FilterByType(MediaType.Music))
            {
                library.Register(item, ProceduralClipFactory.Create(item, 0.2f));
            }

            audio = new AudioBackendAdapter(
                "AudioAdapter",
                new AudioBackend("AudioBackend", new SilentAudioPlayer(), library, _logger),
                library, _logger);

            video = new VideoBackendAdapter(
                "VideoAdapter", new DummyVideoBackend("DummyVideoBackend", _logger), _logger);

            var adapters = new BackendAdapterManager(_logger);
            adapters.Register(audio);
            adapters.Register(video);
            return adapters;
        }

        private static string Describe(PlayerSession session)
        {
            return $"[{session.PlaybackState}] {session.CurrentMediaId ?? "(none)"}"
                   + $" Queue={session.Queue.Count}";
        }

        private static string ActiveName(BackendManager manager)
        {
            return manager.ActiveBackend != null ? manager.ActiveBackend.Name : "(none)";
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

        /// <summary>音を鳴らさない <see cref="IAudioPlayer"/>(前半のシナリオ用)。</summary>
        private sealed class SilentAudioPlayer : IAudioPlayer
        {
            public bool IsPlaying { get; private set; }
            public AudioClip Clip { get; private set; }
            public float Time { get; set; }

            public void Play(AudioClip clip) { Clip = clip; Time = 0f; IsPlaying = true; }
            public void Pause() { IsPlaying = false; }
            public void UnPause() { IsPlaying = true; }
            public void Stop() { IsPlaying = false; Time = 0f; }
        }
    }
}
