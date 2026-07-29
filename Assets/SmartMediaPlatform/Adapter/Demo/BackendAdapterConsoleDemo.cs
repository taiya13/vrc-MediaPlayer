using System.Collections;
using System.Linq;
using System.Text;
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

namespace SmartMediaPlatform.Adapter.Demo
{
    /// <summary>
    /// Backend Adapter 層の動作を Console だけで確認するデモ。
    ///
    /// 見どころは <b>PlayerSession を 1 行も変えずに Video を足せる</b>こと。
    /// 実際の映像は出さず(DummyVideoBackendAdapter はログのみ)、
    /// 音声だけ AudioBackend で実際に鳴らす。
    ///
    /// 空の GameObject にアタッチして Play すれば走る。
    /// </summary>
    public sealed class BackendAdapterConsoleDemo : MonoBehaviour
    {
        [Header("再生する曲・動画(Catalog の ID)")]
        [SerializeField]
        private string[] _trackIds = { "music-001", "video-001", "music-002" };

        [Header("実行")]
        [SerializeField] private bool _runOnStart = true;

        [Tooltip("最後に実際に音を鳴らすところまで通すか")]
        [SerializeField] private bool _playAudioAtTheEnd = true;

        [Tooltip("再生を聴かせる時間(秒)")]
        [SerializeField] private float _listenSeconds = 4f;

        private IMediaCatalog _catalog;
        private ListBackendLogger _logger;
        private StringBuilder _out;

        private void Start()
        {
            if (_runOnStart) StartCoroutine(RunAll());
        }

        [ContextMenu("Run Adapter Scenarios (no audio)")]
        public void RunWithoutAudio()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _out = new StringBuilder();
            _out.AppendLine("=== Phase2-4 Backend Adapter Demo ===");

            RunAdapterRegistryScenario();
            RunCapabilityScenario();
            RunAudioAdapterScenario();
            RunVideoAdapterScenario();
            RunErrorScenario();
            RunSessionProofScenario();

            Debug.Log(_out.ToString());
        }

        public IEnumerator RunAll()
        {
            RunWithoutAudio();

            if (_playAudioAtTheEnd) yield return RunRealPlaybackScenario();
        }

        // ───────── 1. アダプタの登録と選択 ─────────

        private void RunAdapterRegistryScenario()
        {
            Section("1. アダプタの登録と選択");

            var adapters = BuildAdapters(out _, out _);
            _out.AppendLine(adapters.Describe());

            foreach (var id in new[] { "music-001", "video-001", "podcast-001" })
            {
                var item = _catalog.FindById(id);
                var adapter = adapters.SelectFor(item);
                Line($"{id} ({item.Type}) -> "
                     + (adapter != null ? adapter.Name : "(扱えるアダプタなし)"));
            }
        }

        // ───────── 2. 能力の違いを吸収する ─────────

        private void RunCapabilityScenario()
        {
            Section("2. 能力の違いを吸収する(CanPlay / CanSeek)");

            BuildAdapters(out var audio, out var video);

            audio.Load(_catalog.FindById("music-001"));
            video.Load(_catalog.FindById("video-001"));

            Line($"{audio.Name}: CanPlay(music)={audio.CanPlay(_catalog.FindById("music-001"))}, "
                 + $"CanPlay(video)={audio.CanPlay(_catalog.FindById("video-001"))}, "
                 + $"CanSeek={audio.CanSeek}");
            Line($"  内側の AudioBackend は再生位置を持つので、そのまま通す");

            Line($"{video.Name}: CanPlay(video)={video.CanPlay(_catalog.FindById("video-001"))}, "
                 + $"CanPlay(music)={video.CanPlay(_catalog.FindById("music-001"))}, "
                 + $"CanSeek={video.CanSeek}");
            Line($"  内側は再生位置を持たないが、アダプタが肩代わりして同じ顔をする");

            var live = new DummyVideoBackendAdapter("LiveAdapter", _logger)
            {
                SimulateSeekSupport = false,
            };
            live.Load(_catalog.FindById("video-001"));
            Line($"{live.Name}: CanSeek={live.CanSeek} (生配信のようにシークできない場合も表現できる)");
        }

        // ───────── 3. AudioBackendAdapter ─────────

        private void RunAudioAdapterScenario()
        {
            Section("3. AudioBackendAdapter の状態遷移");

            BuildAdapters(out var audio, out _);
            RunStandardSequence(audio, "music-001");
        }

        // ───────── 4. DummyVideoBackendAdapter ─────────

        private void RunVideoAdapterScenario()
        {
            Section("4. DummyVideoBackendAdapter の状態遷移(映像は出しません)");

            BuildAdapters(out _, out var video);
            RunStandardSequence(video, "video-001");

            Line("Ended を起こす:");
            video.Play();
            video.InnerDummy.SimulateEnded();
            Line($"  -> State={video.GetState()} (上位へ Ended が中継される)");
        }

        /// <summary>どちらのアダプタにも同じ操作列を流す。</summary>
        private void RunStandardSequence(IBackendAdapter adapter, string mediaId)
        {
            var item = _catalog.FindById(mediaId);

            Line($"Load    -> {adapter.Load(item)} / State={adapter.GetState()}");
            Line($"Play    -> {adapter.Play()} / State={adapter.GetState()}");
            Line($"Pause   -> {adapter.Pause()} / State={adapter.GetState()}");
            Line($"Resume  -> {adapter.Resume()} / State={adapter.GetState()}");

            if (adapter.CanSeek)
            {
                adapter.Seek(0.5f);
                Line($"Seek 0.5-> {adapter.GetCurrentTime():0.0}s / {adapter.GetDuration():0.0}s "
                     + $"({adapter.GetProgress() * 100f:0}%)");
            }
            else
            {
                Line($"Seek 0.5-> {adapter.Seek(0.5f)} (シーク非対応)");
            }

            Line($"SkipNext-> {adapter.SkipNext()} / State={adapter.GetState()}");
            Line($"Stop    -> {adapter.Stop()} / State={adapter.GetState()}");
            Line($"GetCurrentMedia = {(adapter.GetCurrentMedia() != null ? adapter.GetCurrentMedia().Id : "(none)")}");
        }

        // ───────── 5. Error 通知 ─────────

        private void RunErrorScenario()
        {
            Section("5. Error 通知");

            BuildAdapters(out var audio, out var video);

            // 扱えない種別を読み込ませる
            audio.Load(_catalog.FindById("video-001"));
            Line($"AudioAdapter に動画を渡す -> HasError={audio.HasError}");
            Line($"  {audio.LastError}");

            // 音源が無い場合
            var emptyLibrary = new AudioClipLibrary();
            var bare = new AudioBackendAdapter(
                "AudioAdapter(音源なし)",
                new AudioBackend("inner", new SilentAudioPlayer(), emptyLibrary, _logger),
                emptyLibrary, _logger);
            bare.Load(_catalog.FindById("music-001"));
            Line($"音源未登録で読み込む -> HasError={bare.HasError}");
            Line($"  {bare.LastError}");

            // 動画側のエラーを外から通知(実機なら URL 読み込み失敗など)
            video.ReportError("動画URLの読み込みに失敗しました(ダミー)");
            Line($"VideoAdapter のエラー -> {video.LastError}");
            video.ClearError();
            Line($"  ClearError 後 -> HasError={video.HasError}");
        }

        // ───────── 6. PlayerSession が変更不要であることの証明 ─────────

        private void RunSessionProofScenario()
        {
            Section("6. PlayerSession は変更不要(Audio と Video を混ぜて再生)");

            var adapters = BuildAdapters(out _, out var video);

            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, _logger);
            var player = new MediaPlayer(backendManager, _logger);
            var engine = new RecommendationEngine(
                _catalog, RecommendationRule.CreateDefault(), new System.Random(1));
            var session = new PlayerSession(
                "proof", player, _catalog, engine, new System.Random(1), _logger);

            // ここが唯一の接続点。PlayerSession のコードには手を入れていない。
            adapters.AttachAll(session.RegisterBackend);

            session.AutoQueueEnabled = false;
            session.SetTracks(_trackIds);
            Line($"SetTracks: [{string.Join(", ", _trackIds)}]");

            session.Play();
            Line($"Play -> {Describe(session)}");

            for (int i = 0; i < _trackIds.Length - 1; i++)
            {
                session.Next();
                Line($"Next -> {Describe(session)}"
                     + $"  担当={(backendManager.ActiveBackend != null ? backendManager.ActiveBackend.Name : "(none)")}");
            }

            Line("種別が変わっても上位は Next() を呼ぶだけ。担当アダプタは自動で切り替わる。");
        }

        // ───────── 7. 実際に音を鳴らす ─────────

        private IEnumerator RunRealPlaybackScenario()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 7. 実際に再生する(音声のみ音が鳴ります) ===");

            var logger = new ListBackendLogger();
            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());

            var host = GetComponent<AudioBackendHost>();
            if (host == null) host = gameObject.AddComponent<AudioBackendHost>();
            host.EnsureBuilt(logger);
            host.PrepareClipsFor(catalog);

            var audioAdapter = new AudioBackendAdapter(
                "AudioAdapter", host.Backend, host.Library, logger);
            var videoAdapter = new DummyVideoBackendAdapter("VideoAdapter", logger);

            var adapters = new BackendAdapterManager(logger);
            adapters.Register(audioAdapter);
            adapters.Register(videoAdapter);

            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, logger);
            var player = new MediaPlayer(backendManager, logger);
            var engine = new RecommendationEngine(catalog, RecommendationRule.CreateDefault());
            var session = new PlayerSession(
                "audio", player, catalog, engine, new System.Random(1), logger);

            adapters.AttachAll(session.RegisterBackend);

            session.AutoQueueEnabled = false;
            session.SetTracks(_trackIds);
            session.Play();
            sb.AppendLine($"  開始: {Describe(session)}");

            float waited = 0f;
            string lastId = session.CurrentMediaId;

            while (waited < _listenSeconds)
            {
                waited += Time.deltaTime;

                // ダミー動画は自分で進まないので進めてやる
                videoAdapter.Advance(Time.deltaTime);

                if (session.CurrentMediaId != lastId)
                {
                    sb.AppendLine($"  {waited:0.0}s: {Describe(session)}");
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

        /// <summary>音声・動画のアダプタを作って登録簿にまとめる。</summary>
        private BackendAdapterManager BuildAdapters(
            out AudioBackendAdapter audio, out DummyVideoBackendAdapter video)
        {
            _logger = _logger ?? new ListBackendLogger();

            var library = new AudioClipLibrary();
            foreach (var item in _catalog.FilterByType(MediaType.Music))
            {
                library.Register(item, ProceduralClipFactory.Create(item, 0.2f));
            }

            audio = new AudioBackendAdapter(
                "AudioAdapter",
                new AudioBackend("AudioBackend", new SilentAudioPlayer(), library, _logger),
                library, _logger);

            video = new DummyVideoBackendAdapter("VideoAdapter", _logger);

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

        private void Section(string title)
        {
            _out.AppendLine();
            _out.AppendLine($"── {title}");
        }

        private void Line(string text)
        {
            _out.AppendLine("  " + text);
        }

        /// <summary>
        /// 音を鳴らさない <see cref="IAudioPlayer"/>。
        /// 前半のシナリオでは状態遷移だけを見たいので、こちらを使う。
        /// </summary>
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
