using System.Text;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Library.Playback;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;
using UnityEngine;

namespace SmartMediaPlatform.World
{
    /// <summary>
    /// <b>SmartMediaPlayer.prefab の根っこ。組み立てだけを担当します。</b>
    ///
    /// <code>
    /// SmartMediaPlayer            ← このコンポーネント
    /// ├── Screen                  IMediaScreen      (映像と音の出力先)
    /// ├── Player                  IMediaBackendProvider (どのバックエンドで鳴らすか)
    /// ├── Controller              IMediaController  (再生操作の窓口)
    /// └── UI                      IMediaPlayerUI    (画面)
    /// </code>
    ///
    /// <b>ロジックは持っていません。</b>やることは 3 つだけです。
    /// <list type="number">
    /// <item>子から部品(インターフェース)を探す</item>
    /// <item>既存の <c>PlaybackFlow</c> / <c>PlayerSession</c> / <c>Catalog</c> を組み立てる</item>
    /// <item>結果を <see cref="MediaPlayerContext"/> にして部品へ配る</item>
    /// </list>
    /// 「次に何を再生するか」も「何を積むか」も、今までどおり
    /// <c>PlayerSession</c> と <c>IQueueRefiller</c> の仕事です。
    ///
    /// <b>部品はインターフェースで探します。</b>
    /// だから Screen も Controller も UI もバックエンドもカタログも、
    /// <b>子オブジェクトを差し替えるだけ</b>で交換できます
    /// (このクラスも Prefab の他の部分も変わりません)。
    ///
    /// <b>見つからない部品は代役を立てます。</b>
    /// Prefab をドラッグしただけで動くのはこのためです
    /// (バックエンドが無ければログだけの <see cref="DummyMediaBackendProvider"/>、
    ///  カタログが無ければ同梱のサンプル)。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Smart Media Platform/Smart Media Player Root")]
    public sealed class SmartMediaPlayerRoot : MonoBehaviour
    {
        [Header("起動")]
        [Tooltip("Start() で自動的に組み立てる")]
        [SerializeField] private bool _buildOnStart = true;

        [Tooltip("組み立て後、最初の 1 本を自動で再生する")]
        [SerializeField] private bool _playOnStart = true;

        [Header("表示するもの")]
        [Tooltip("一覧に出す種別。Unknown なら全種別")]
        [SerializeField] private MediaType _mediaType = MediaType.Video;

        [Tooltip("関連動画を何件まで出すか(0 で制限なし)")]
        [SerializeField] private int _relatedCount = 5;

        [Header("補充")]
        [Tooltip("Queue がこの数を下回ったら補充する")]
        [SerializeField] private int _minimumQueueCount = 2;

        [Tooltip("補充後に目指す Queue の長さ")]
        [SerializeField] private int _targetQueueCount = 5;

        [Header("記録")]
        [Tooltip("組み立て内容と再生の記録を Console に出す")]
        [SerializeField] private bool _logToConsole = true;

        private MediaPlayerContext _context;
        private ListBackendLogger _logger;
        private IMediaPlayerPart[] _parts;

        /// <summary>組み立て済みの一式。まだなら null。</summary>
        public MediaPlayerContext Context => _context;

        /// <summary>組み立て済みの再生フロー。まだなら null。</summary>
        public PlaybackFlow Flow => _context != null ? _context.Flow : null;

        /// <summary>再生操作の窓口(子から見つけたもの)。</summary>
        public IMediaController Controller { get; private set; }

        /// <summary>映像と音の出力先(子から見つけたもの)。</summary>
        public IMediaScreen Screen { get; private set; }

        /// <summary>画面(子から見つけたもの)。</summary>
        public IMediaPlayerUI UI { get; private set; }

        /// <summary>組み立てが終わっているか。</summary>
        public bool IsBuilt => _context != null;

        private void Start()
        {
            if (_buildOnStart) Build();
        }

        private void Update()
        {
            // Queue と「いま再生中」の表示を追従させる(Phase4-3)。
            // 変化が無ければ何もしないので毎フレーム呼んで構いません。
            _context?.Flow.Tick();
        }

        /// <summary>
        /// 一式を組み立てる。すでに組み立て済みなら何もしません。
        /// </summary>
        /// <returns>組み立てられたら true。</returns>
        [ContextMenu("Build")]
        public bool Build()
        {
            if (_context != null) return true;

            _logger = new ListBackendLogger();

            // ── 1. カタログ(Catalog Builder の差し込み口)
            var catalogProvider = GetComponentInChildren<ICatalogProvider>(true);
            var catalog = catalogProvider != null ? catalogProvider.CreateCatalog() : null;
            if (catalog == null) catalog = DefaultCatalogProvider.CreateDefaultCatalog();

            // ── 2. 出力先
            Screen = GetComponentInChildren<IMediaScreen>(true);

            // ── 3. バックエンド(バックエンド追加の差し込み口)
            var backendProvider = GetComponentInChildren<IMediaBackendProvider>(true);
            var backend = backendProvider != null
                ? backendProvider.CreateBackend(Screen, _logger)
                : null;

            if (backend == null)
            {
                // 何も無くてもドラッグしただけで動くように、ログだけの代役を立てる
                backend = DummyMediaBackendProvider.CreateDummyBackend(_logger);
            }

            // ── 4. 既存の再生系をそのまま組み立てる(Phase1〜4 から変更なし)
            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, _logger);
            backendManager.RegisterBackend(backend);

            var mediaPlayer = new MediaPlayer(backendManager, _logger);
            var engine = new RecommendationEngine(catalog, RecommendationRule.CreateDefault());
            var session = new PlayerSession(
                name, mediaPlayer, catalog, engine, new System.Random(), _logger)
            {
                MinimumQueueCount = _minimumQueueCount,
                TargetQueueCount = _targetQueueCount,
            };

            var flow = PlaybackFlow.Create(catalog, session, _relatedCount, _logger);
            if (_mediaType != MediaType.Unknown) flow.Library.ShowOnly(_mediaType);

            _context = new MediaPlayerContext(flow.Store, flow, session, _logger);

            // ── 5. 部品へ配る
            BindParts();

            if (_logToConsole)
            {
                Debug.Log(BuildReport(catalogProvider, backendProvider, backend), this);
            }

            if (_playOnStart) PlayFirst();
            return true;
        }

        /// <summary>
        /// 最初の 1 本を再生する。
        /// <b>再生の指示はこれだけ</b>で、あとは <c>PlayerSession</c> が進めます。
        /// </summary>
        public bool PlayFirst()
        {
            if (_context == null || _context.Flow.Library.Count == 0) return false;
            return _context.Flow.LibraryPlayback.PlayAt(0);
        }

        /// <summary>
        /// 部品を探し直して配り直す。
        /// <b>実行中に Screen や UI を差し替えたとき</b>に呼びます。
        /// </summary>
        [ContextMenu("Rebind Parts")]
        public void BindParts()
        {
            if (_context == null) return;

            Screen = GetComponentInChildren<IMediaScreen>(true);
            Controller = GetComponentInChildren<IMediaController>(true);
            UI = GetComponentInChildren<IMediaPlayerUI>(true);

            _parts = GetComponentsInChildren<IMediaPlayerPart>(true);
            for (int i = 0; i < _parts.Length; i++) _parts[i].Bind(_context);
        }

        /// <summary>Console 用のまとめ。</summary>
        public string BuildReport(
            ICatalogProvider catalogProvider = null,
            IMediaBackendProvider backendProvider = null,
            IMediaBackend backend = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {name} を組み立てました ===");
            sb.AppendLine($"  カタログ    : "
                          + (catalogProvider != null ? catalogProvider.Describe() : "既定(同梱サンプル)"));
            sb.AppendLine($"  バックエンド: "
                          + (backendProvider != null ? backendProvider.Describe() : "既定(ログのみ)")
                          + (backend != null ? $" -> {backend.Name}" : ""));
            sb.AppendLine($"  出力先      : {(Screen != null ? Screen.ToString() : "(なし)")}");
            sb.AppendLine($"  操作        : {(Controller != null ? Controller.ToString() : "(なし)")}");
            sb.AppendLine($"  画面        : {(UI != null ? UI.ToString() : "(なし)")}");

            if (_context != null)
            {
                sb.AppendLine($"  一覧        : {_context.Flow.Library.Count} 件"
                              + (_mediaType != MediaType.Unknown ? $"({_mediaType} のみ)" : ""));
            }

            return sb.ToString();
        }

        /// <summary>ログの行(診断用)。</summary>
        public System.Collections.Generic.IReadOnlyList<string> LogLines =>
            _logger != null ? _logger.Lines : new string[0];
    }
}
