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

namespace SmartMediaPlatform.Video.VRChat
{
    /// <summary>
    /// <b>VRChat SDK の動画プレイヤーで実際に再生する</b> DemoScene の進行役。
    ///
    /// Console だけで Phase3-1 の成功条件を確認できます:
    /// Load → Play → Pause → Resume → Stop → 再生 → Ended → Error。
    ///
    /// 上位の組み立ては <c>VideoBackendConsoleDemo</c>(Phase2-4B)と<b>同一</b>で、
    /// 違いは <c>new DummyVideoBackend(...)</c> が
    /// <see cref="VRChatVideoBackendHost"/> の組み立てた <see cref="VRChatVideoBackend"/> に
    /// 変わったところだけです。<b>BackendAdapter も PlayerSession も変更していません。</b>
    /// </summary>
    [RequireComponent(typeof(VRChatVideoBackendHost))]
    public sealed class VRChatVideoSceneDemo : MonoBehaviour
    {
        [Header("再生する動画(Catalog の ID)")]
        [SerializeField] private string[] _videoIds = { "video-001" };

        [Header("実行")]
        [SerializeField] private bool _runOnStart = true;

        [Tooltip("読み込み完了を待つ最大秒数")]
        [SerializeField] private float _loadWaitSeconds = 20f;

        [Tooltip("各操作のあいだに待つ秒数")]
        [SerializeField] private float _stepSeconds = 2f;

        [Tooltip("最後まで再生せず、この秒数でシークして Ended を確認する(0 で無効)")]
        [SerializeField] private float _seekNearEndTailSeconds = 3f;

        private VRChatVideoBackendHost _host;
        private ListBackendLogger _logger;
        private StringBuilder _out;
        private int _flushed;

        private void Start()
        {
            if (_runOnStart) StartCoroutine(Run());
        }

        [ContextMenu("Run VRChat Video Scenarios")]
        public void RunFromMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[VRChatVideoSceneDemo] Play 中に実行してください。");
                return;
            }
            StartCoroutine(Run());
        }

        public IEnumerator Run()
        {
            _logger = new ListBackendLogger();
            _out = new StringBuilder();
            _flushed = 0;
            _out.AppendLine("=== Phase3-1 VRChat Video Backend Demo (DemoScene) ===");

            _host = GetComponent<VRChatVideoBackendHost>();
            var adapter = _host.EnsureBuilt(_logger);
            if (adapter == null)
            {
                Debug.LogError("[VRChatVideoSceneDemo] Backend を組み立てられませんでした。");
                yield break;
            }

            var backend = _host.Backend;

            // ── 0. 構成
            Section("0. 構成");
            Line($"動画プレイヤー : {(_host.VideoPlayer != null ? _host.VideoPlayer.GetType().Name : "(none)")}");
            Line($"Backend        : {backend}");
            Line($"Adapter        : {adapter}");
            Line($"ベイク済み URL : {_host.Urls.Count} 件(実行時に VRCUrl は作りません)");

            // ── 1. CanPlay
            Section("1. CanPlay(Catalog に登録された URL だけ再生できる)");
            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());
            var item = catalog.FindById(_videoIds.Length > 0 ? _videoIds[0] : "video-001");
            Line($"CanPlay(\"{item.Url}\") -> {backend.CanPlay(item.Url)}");
            Line($"CanPlay(\"https://example.com/not-baked\") -> "
                 + $"{backend.CanPlay("https://example.com/not-baked")}(未ベイクなので再生しない)");
            Flush();

            // ── 2. PlayerSession から再生(上位は一切変更していない)
            Section("2. PlayerSession から再生(Catalog → Queue → Backend)");
            var session = BuildSession(catalog, adapter);
            session.AutoQueueEnabled = false;
            session.SetTracks(_videoIds);

            bool started = session.Play();
            Line($"session.Play() -> {started} / BackendState={session.BackendState}");

            yield return WaitUntilState(backend, VideoPlayerState.Playing, _loadWaitSeconds);
            Line($"読み込み完了 -> State={backend.GetState()} / "
                 + $"長さ={backend.GetDuration():0.0}s / 上位から見た状態={adapter.GetState()}");
            Flush();

            // ── 3. Play / Pause / Resume / Stop
            Section("3. Play / Pause / Resume / Stop");
            yield return new WaitForSeconds(_stepSeconds);
            Line($"再生中 -> Time={backend.GetTime():0.0}s / State={backend.GetState()}");

            Line($"Pause  -> {session.Pause()} / State={backend.GetState()} / 上位={session.BackendState}");
            yield return new WaitForSeconds(_stepSeconds);
            Line($"  一時停止中に時間が進まないこと -> Time={backend.GetTime():0.0}s");

            Line($"Resume -> {session.Resume()} / State={backend.GetState()} / 上位={session.BackendState}");
            yield return new WaitForSeconds(_stepSeconds);
            Line($"  再開後 -> Time={backend.GetTime():0.0}s");

            Line($"Stop   -> {session.Stop()} / State={backend.GetState()} / 上位={session.BackendState}");
            Flush();

            // ── 4. Ended
            Section("4. Ended イベント(動画の終わりで上位が次へ進む)");
            var endObserver = new EndObserver();
            backend.AddObserver(endObserver);

            session.Play();
            yield return WaitUntilState(backend, VideoPlayerState.Playing, _loadWaitSeconds);

            float duration = backend.GetDuration();
            if (_seekNearEndTailSeconds > 0f && duration > _seekNearEndTailSeconds)
            {
                // 最後まで待たずに終端付近へ跳ぶ(デモを短く済ませるため)。
                backend.Seek(duration - _seekNearEndTailSeconds);
                Line($"終端付近へシーク -> {backend.GetTime():0.0}s / {duration:0.0}s");
            }

            yield return WaitUntilState(backend, VideoPlayerState.Finished,
                _seekNearEndTailSeconds + _loadWaitSeconds);

            Line($"Ended 通知 -> {endObserver.EndCount} 回 / State={backend.GetState()}");
            Line($"上位から見た状態 -> {adapter.GetState()}(Finished は Ended に翻訳される)");
            Line($"PlayerSession -> {session.PlaybackState} / Current={session.CurrentMediaId ?? "(none)"}");
            Flush();

            // ── 5. Error
            Section("5. Error イベント");
            var errorObserver = new ErrorObserver();
            backend.AddObserver(errorObserver);

            Line($"未ベイクの URL を Load -> {backend.Load("https://example.com/not-baked")}");
            Line($"  Error 通知 -> {errorObserver.ErrorCount} 回 / {errorObserver.LastMessage}");
            Line($"  上位から見た状態 -> {adapter.GetState()} / HasError={adapter.HasError}");

            // 実機の OnVideoError と同じ経路(SDK の VideoError を翻訳して流す)
            backend.Load(item.Url);
            _host.OnVideoError(VRC.SDK3.Components.Video.VideoError.AccessDenied);
            Line($"実機の OnVideoError(AccessDenied) -> {errorObserver.LastMessage}");
            Line($"  上位から見た状態 -> {adapter.GetState()}");
            Flush();

            Section("結果");
            Line("Catalog の動画を Backend 経由で再生し、Play/Pause/Resume/Stop・Ended・Error を確認しました。");
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
                "vrchat-video-demo", player, catalog, engine, new System.Random(1), _logger);

            session.RegisterBackend(adapter);
            return session;
        }

        private IEnumerator WaitUntilState(
            VRChatVideoBackend backend, VideoPlayerState state, float timeoutSeconds)
        {
            float waited = 0f;
            while (backend.GetState() != state && waited < timeoutSeconds)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (backend.GetState() != state)
            {
                Line($"  (待機打ち切り: {state} になりませんでした。現在 {backend.GetState()})");
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

        private void Flush()
        {
            for (int i = _flushed; i < _logger.Lines.Count; i++)
            {
                _out.Append("    ").AppendLine(_logger.Lines[i]);
            }
            _flushed = _logger.Lines.Count;
        }

        private class NullVideoObserver : IVideoBackendObserver
        {
            public virtual void OnVideoReady(string url) { }
            public virtual void OnVideoStart(string url) { }
            public virtual void OnVideoPause(string url) { }
            public virtual void OnVideoStop(string url) { }
            public virtual void OnVideoEnd(string url) { }
            public virtual void OnVideoError(string url, string message) { }
        }

        private sealed class EndObserver : NullVideoObserver
        {
            public int EndCount { get; private set; }

            public override void OnVideoEnd(string url) => EndCount++;
        }

        private sealed class ErrorObserver : NullVideoObserver
        {
            public int ErrorCount { get; private set; }
            public string LastMessage { get; private set; }

            public override void OnVideoError(string url, string message)
            {
                ErrorCount++;
                LastMessage = message;
            }
        }
    }
}
