using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Store;
using SmartMediaPlatform.Library.Playback;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;
using SmartMediaPlatform.Video;
using SmartMediaPlatform.Video.Data;
using SmartMediaPlatform.Video.VRChat;
using UnityEngine;

namespace SmartMediaPlatform.Library.VRChat
{
    /// <summary>
    /// <b>Phase4-4 の本命。Library → Queue → Player → VideoBackend → VRChat VideoPlayer
    /// を実際につないで動かす画面。</b>
    ///
    /// Phase4-3 の <c>PlaybackFlowScreenDemo</c> との違いは<b>1 箇所だけ</b>です。
    /// <code>
    /// // Phase4-3(ログだけ)
    /// backendManager.RegisterBackend(new DummyBackend("DummyBackend", logger));
    ///
    /// // Phase4-4(実際に動画が出る)
    /// backendManager.RegisterBackend(host.EnsureBuilt(logger));
    /// </code>
    /// <b>これが「DummyBackend を実機へ置き換えられる構造」の実物</b>です。
    /// <see cref="PlaybackFlow"/> も <c>PlayerSession</c> も <c>Queue</c> も
    /// <c>MediaLibrary</c> も、この 1 行の違いを知りません。
    ///
    /// <b>AVPro / Unity 版の切り替え</b>は
    /// <c>VRChatVideoBackendHost</c> の Inspector(<c>Preferred Player</c>)で行います。
    /// この画面はどちらが繋がっているかを表示するだけで、扱いは同じです。
    ///
    /// <b>置き場所</b>:<c>VRChatVideoBackendHost</c> と同じ GameObject に付けてください
    /// (VRChat の動画イベントは同じ GameObject の UdonBehaviour にしか届かないため)。
    /// </summary>
    [RequireComponent(typeof(VRChatVideoBackendHost))]
    public sealed class VRChatPlaybackFlowSceneDemo : MonoBehaviour
    {
        [Header("表示")]
        [Tooltip("最初に表示する種別")]
        [SerializeField] private MediaType _initialType = MediaType.Video;

        [Tooltip("画面が小さいときに文字を詰める")]
        [SerializeField] private int _fontSize = 12;

        [Tooltip("UI を描くか(実機のワールドでは切っておく)")]
        [SerializeField] private bool _drawOverlay = true;

        [Header("関連動画")]
        [Tooltip("関連動画を何件まで出すか(0 で制限なし)")]
        [SerializeField] private int _relatedCount = 5;

        [Header("自動再生")]
        [Tooltip("開始時に先頭を再生する")]
        [SerializeField] private bool _playOnStart = true;

        private VRChatVideoBackendHost _host;
        private UdonVideoEventPump _pump;
        private PlaybackFlow _flow;
        private PlayerSession _session;
        private VRChatVideoBackend _backend;
        private ListBackendLogger _logger;

        private Vector2 _libraryScroll;
        private Vector2 _queueScroll;
        private string _status = "左の一覧から選んで「▶ 再生」を押してください。";
        private GUIStyle _richLabel;

        /// <summary>組み立て済みの再生フロー。</summary>
        public PlaybackFlow Flow => _flow;

        /// <summary>つないだ動画バックエンド。</summary>
        public VRChatVideoBackend Backend => _backend;

        private void Start()
        {
            _logger = new ListBackendLogger();

            _host = GetComponent<VRChatVideoBackendHost>();
            _pump = GetComponent<UdonVideoEventPump>();

            // ★ ここが Phase4-4 の要点:
            //   DummyBackend の代わりに、実機の動画バックエンドを載せたアダプタを登録する。
            var adapter = _host.EnsureBuilt(_logger);
            if (adapter == null)
            {
                Debug.LogError(
                    "[VRChatPlaybackFlowSceneDemo] Backend を組み立てられませんでした。"
                    + "VRCUnityVideoPlayer / VRCAVProVideoPlayer が同じ GameObject にあるか確認してください。");
                enabled = false;
                return;
            }

            _backend = _host.Backend;

            // ── ここから下は Phase4-3 とまったく同じ組み立て
            IMediaCatalog catalog = new MediaCatalog(new VideoCatalogSource(), new System.Random(1));

            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, _logger);
            backendManager.RegisterBackend(adapter);          // ← DummyBackend との唯一の違い

            var mediaPlayer = new MediaPlayer(backendManager, _logger);
            var engine = new RecommendationEngine(catalog, RecommendationRule.CreateDefault());
            _session = new PlayerSession(
                "vrchat-flow", mediaPlayer, catalog, engine, new System.Random(1), _logger);

            _flow = PlaybackFlow.Create(catalog, _session, _relatedCount, _logger);
            _flow.Library.ShowOnly(_initialType);

            Debug.Log(BuildStartupReport());

            if (_playOnStart && _flow.Library.Count > 0)
            {
                _flow.LibraryPlayback.PlayAt(0);
            }
        }

        private void Update()
        {
            // Phase4-3 と同じ 1 行。
            // 実機イベント(OnVideoReady / OnVideoEnd / OnVideoError)は
            // VRChatVideoBackendHost が VideoEventBridge へ流し込んでいます。
            _flow?.Tick();
        }

        /// <summary>組み立て結果のまとめ(Console 用)。</summary>
        public string BuildStartupReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Phase4-4 VRChat Playback Flow ===");
            sb.AppendLine("Library → Queue → Player → VideoBackend → VRChat VideoPlayer");
            sb.AppendLine();
            sb.AppendLine($"  動画プレイヤー : {(_host.VideoPlayer != null ? _host.VideoPlayer.GetType().Name : "(none)")}");
            sb.AppendLine($"  種類           : {_host.PlayerKind}"
                          + (_host.PlayerKind == VRCVideoPlayerKind.AVPro
                              ? "(実機の標準。エディタでは映像が出ません)"
                              : "(エディタでも映像が出ます)"));
            sb.AppendLine($"  Udon 中継      : {(_pump != null ? _pump.ConnectionDescription : "(Pump なし — ポーリングで代用)")}");
            sb.AppendLine($"  ベイク済み URL : {_host.Urls.Count} 件");
            sb.AppendLine($"  カタログ       : {_flow.Library.Count} 件");
            sb.AppendLine($"  Backend        : {_backend}");

            if (_host.Urls.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("  ※ URL が焼き込まれていません。再生できません。");
                sb.AppendLine("     Tools > Smart Media Platform > Bake Catalog Urls Into Selected Video Host");
            }

            return sb.ToString();
        }

        // ───────── 画面 ─────────

        private void OnGUI()
        {
            if (!_drawOverlay || _flow == null) return;

            GUI.skin.label.fontSize = _fontSize;
            GUI.skin.button.fontSize = _fontSize;

            GUILayout.BeginArea(new Rect(10f, 10f, Screen.width * 0.5f, Screen.height - 20f));

            DrawNowPlaying();
            GUILayout.Space(4f);
            DrawLibrary();
            GUILayout.Space(4f);
            DrawQueue();
            GUILayout.Space(4f);
            GUILayout.Label(_status);

            GUILayout.EndArea();
        }

        private void DrawNowPlaying()
        {
            var now = _flow.NowPlaying;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"<b>▶ {now.FormatTitle()}</b>  [{now.State}]  {now.FormatTime()}",
                            RichLabel());
            GUILayout.Label($"{_host.PlayerKind} / VideoBackend: {_backend.GetState()}"
                            + $" / 焼き込み URL {_host.Urls.Count} 件");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("再生/一時停止", GUILayout.Width(120f))) _session.TogglePlayPause();
            if (GUILayout.Button("次へ", GUILayout.Width(60f))) _session.Next();
            if (GUILayout.Button("前へ", GUILayout.Width(60f))) _session.Previous();
            if (GUILayout.Button("停止", GUILayout.Width(60f))) _session.Stop();

            GUILayout.Space(10f);
            if (GUILayout.Button("終端へ飛ぶ", GUILayout.Width(100f)))
            {
                // 実機の OnVideoEnd を早く出すための細工(確認用)
                float duration = _backend.GetDuration();
                if (duration > 3f) _backend.Seek(duration - 3f);
                _status = duration > 3f ? "終端付近へ飛びました。" : "長さが分からないため飛べません。";
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void DrawLibrary()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Height(Screen.height * 0.3f));
            GUILayout.Label($"<b>Library</b>  {_flow.Library.Count} 件", RichLabel());

            _libraryScroll = GUILayout.BeginScrollView(_libraryScroll);
            for (int i = 0; i < _flow.Library.Count; i++)
            {
                var item = _flow.Library.GetAt(i);
                bool selected = i == _flow.Library.SelectedIndex;

                if (GUILayout.Toggle(selected, $"{i + 1}. {item.Title} / {item.Artist}",
                                     GUI.skin.button) != selected)
                {
                    _flow.Library.Select(i);
                }
            }
            GUILayout.EndScrollView();

            GUI.enabled = _flow.Library.HasSelection;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("▶ 再生"))
            {
                _status = _flow.LibraryPlayback.PlaySelected()
                    ? $"{_flow.LibraryPlayback.LastHandedOffId} を再生します。"
                    : "再生できませんでした(URL が焼き込まれているか確認してください)。";
            }
            if (GUILayout.Button("次に再生")) _flow.LibraryPlayback.PlayNextSelected();
            if (GUILayout.Button("＋ Queue")) _flow.LibraryPlayback.EnqueueSelected();
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            GUILayout.EndVertical();
        }

        private void DrawQueue()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Height(Screen.height * 0.22f));
            GUILayout.Label($"<b>Queue</b>  {_flow.Queue.Count} 件", RichLabel());

            _queueScroll = GUILayout.BeginScrollView(_queueScroll);
            for (int i = 0; i < _flow.Queue.Count; i++)
            {
                var item = _flow.Queue.GetAt(i);
                GUILayout.BeginHorizontal();
                GUILayout.Label((i == 0 ? "♪ " : $"{i}. ") + item.Title);
                GUILayout.FlexibleSpace();

                GUI.enabled = i > 0;
                if (GUILayout.Button("飛ぶ", GUILayout.Width(50f))) _flow.QueuePlayback.PlayAt(i);
                if (GUILayout.Button("×", GUILayout.Width(28f))) _flow.Queue.RemoveAt(i);
                GUI.enabled = true;

                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.EndVertical();
        }

        private GUIStyle RichLabel()
        {
            if (_richLabel == null) _richLabel = new GUIStyle(GUI.skin.label) { richText = true };
            return _richLabel;
        }
    }
}
