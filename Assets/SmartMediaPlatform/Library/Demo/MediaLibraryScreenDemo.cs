using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Store;
using SmartMediaPlatform.Library.Playback;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;
using SmartMediaPlatform.Video.Data;
using UnityEngine;

namespace SmartMediaPlatform.Library.Demo
{
    /// <summary>
    /// <b>Phase4-1 の「見える UI」。</b>Game ビューに一覧を描き、クリックで選べます。
    ///
    /// <b>VRChat SDK は不要</b>で、Play を押せばそのまま触れます。
    ///
    /// <b>なぜ IMGUI(OnGUI)なのか</b><br/>
    /// この画面は「<see cref="MediaLibrary"/> が UI から使える形になっているか」を
    /// <b>手で触って確かめるための確認用</b>です。
    /// uGUI の Canvas や VRChat のワールド内 UI は、Prefab とレイアウトの作り込みが本体になり、
    /// Phase4-1 の目的(閲覧と選択の仕組み)から焦点がずれます。
    /// IMGUI ならアセットを 1 つも作らずに動くので、確認だけを最短で済ませられます。
    ///
    /// <b>この画面には判断ロジックがありません。</b>
    /// 一覧・絞り込み・並び順・選択はすべて <see cref="MediaLibrary"/>(純粋 C#)にあり、
    /// 再生への受け渡しは <see cref="LibraryPlaybackBridge"/> が行います。
    /// ここは<b>描いて、押されたことを伝えるだけ</b>です
    /// — だから uGUI や Udon の UI に差し替えても、下は 1 行も変わりません。
    /// </summary>
    public sealed class MediaLibraryScreenDemo : MonoBehaviour, IMediaListObserver
    {
        [Header("表示")]
        [Tooltip("最初に表示する種別(すべて / Music / Video)")]
        [SerializeField] private LibraryTypeTab _initialTab = LibraryTypeTab.All;

        [SerializeField] private LibrarySortOrder _initialSort = LibrarySortOrder.CatalogOrder;

        [Tooltip("画面が小さいときに文字を詰める")]
        [SerializeField] private int _fontSize = 12;

        [Header("関連動画(Phase4-2)")]
        [Tooltip("関連動画を何件まで出すか(0 で制限なし)")]
        [SerializeField] private int _relatedCount = 5;

        private CatalogStore _store;
        private MediaLibrary _library;
        private RelatedMediaView _related;
        private StaticRelatedMediaProvider _relatedProvider;
        private PlayerSession _session;
        private LibraryPlaybackBridge _bridge;
        private LibraryPlaybackBridge _relatedBridge;
        private ListBackendLogger _logger;

        private LibraryTypeTab _tab;
        private Vector2 _scroll;
        private Vector2 _relatedScroll;
        private string _status = "一覧から選んでください。";

        /// <summary>絞り込みのタブ。</summary>
        public enum LibraryTypeTab
        {
            All = 0,
            Music = 1,
            Video = 2,
        }

        /// <summary>データを引く唯一の窓口。</summary>
        public CatalogStore Store => _store;

        /// <summary>組み立て済みの一覧(テスト・外部からの操作用)。</summary>
        public MediaLibrary Library => _library;

        /// <summary>関連動画の一覧(Phase4-2)。</summary>
        public RelatedMediaView Related => _related;

        /// <summary>関連 ID の差し替え口(サーバー連携の受け皿)。</summary>
        public StaticRelatedMediaProvider RelatedProvider => _relatedProvider;

        /// <summary>受け渡し役。</summary>
        public LibraryPlaybackBridge Bridge => _bridge;

        private void Awake()
        {
            _logger = new ListBackendLogger();

            // ── データの取得口は CatalogStore ただ 1 つ
            IMediaCatalog catalog = new MediaCatalog(new MixedCatalogSource(), new System.Random(1));
            _store = new CatalogStore(catalog);

            _library = new MediaLibrary(_store) { SortOrder = _initialSort };
            _library.AddObserver(this);

            // ── 関連動画(Phase4-2)
            //    いまはカタログの RelatedIds を使う。サーバー連携になったら
            //    _relatedProvider.Set(id, idsFromServer) を呼ぶだけで差し替わる。
            _relatedProvider = new StaticRelatedMediaProvider(
                new CatalogRelatedMediaProvider(catalog));
            _related = new RelatedMediaView(_store, _relatedProvider, _relatedCount);
            _related.AddObserver(this);

            // ── 再生側は Phase1〜3 の組み立てをそのまま使う(変更なし)
            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, _logger);
            backendManager.RegisterBackend(new DummyBackend("DummyBackend", _logger));

            var mediaPlayer = new MediaPlayer(backendManager, _logger);
            var engine = new RecommendationEngine(catalog, RecommendationRule.CreateDefault());
            _session = new PlayerSession(
                "library-screen", mediaPlayer, catalog, engine, new System.Random(1), _logger);

            _bridge = new LibraryPlaybackBridge(_library, _session, _logger);
            _relatedBridge = new LibraryPlaybackBridge(_related, _session, _logger);

            _tab = _initialTab;
            ApplyTab();
        }

        // ───────── 描画 ─────────

        private void OnGUI()
        {
            if (_library == null) return;

            GUI.skin.label.fontSize = _fontSize;
            GUI.skin.button.fontSize = _fontSize;

            GUILayout.BeginArea(new Rect(10f, 10f, Screen.width - 20f, Screen.height - 20f));

            DrawHeader();
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            DrawList();
            GUILayout.BeginVertical();
            DrawDetails();
            DrawRelated();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            DrawPlayerState();

            GUILayout.EndArea();
        }

        private void DrawHeader()
        {
            GUILayout.Label("<b>Phase4-2  Media Library + 関連動画</b>  " +
                            MediaLibraryFormatter.FormatSummary(_library),
                            RichLabel());

            GUILayout.BeginHorizontal();

            GUILayout.Label("種別:", GUILayout.Width(40f));
            DrawTab(LibraryTypeTab.All, "すべて");
            DrawTab(LibraryTypeTab.Music, "Music");
            DrawTab(LibraryTypeTab.Video, "Video");

            GUILayout.Space(20f);
            GUILayout.Label("並び:", GUILayout.Width(40f));
            DrawSort(LibrarySortOrder.CatalogOrder, "登録順");
            DrawSort(LibrarySortOrder.Title, "タイトル");
            DrawSort(LibrarySortOrder.Artist, "アーティスト");
            DrawSort(LibrarySortOrder.Genre, "ジャンル");
            DrawSort(LibrarySortOrder.Duration, "長さ");

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawTab(LibraryTypeTab tab, string label)
        {
            bool active = _tab == tab;
            if (GUILayout.Toggle(active, label, GUI.skin.button, GUILayout.Width(70f)) == active)
            {
                return;
            }

            _tab = tab;
            ApplyTab();
        }

        private void DrawSort(LibrarySortOrder order, string label)
        {
            bool active = _library.SortOrder == order;
            if (GUILayout.Toggle(active, label, GUI.skin.button, GUILayout.Width(90f)) != active)
            {
                _library.SortOrder = order;
            }
        }

        private void DrawList()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(Screen.width * 0.55f));

            _scroll = GUILayout.BeginScrollView(_scroll);

            if (_library.Count == 0)
            {
                GUILayout.Label("表示できるメディアがありません。");
            }

            for (int i = 0; i < _library.Count; i++)
            {
                var item = _library.GetAt(i);
                bool selected = i == _library.SelectedIndex;

                // クリック = 選択。ダブルクリック相当は「再生」ボタンに任せる。
                if (GUILayout.Toggle(selected, RowLabel(item, i), GUI.skin.button) != selected)
                {
                    _library.Select(i);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private string RowLabel(DisplayMeta item, int index)
        {
            string tags = MediaLibraryFormatter.FormatTags(item);
            return $"{index + 1,3}. {item.Title}\n"
                   + $"      {item.Artist}  |  {item.Type}  |  {item.Genre}  "
                   + $"{(tags.Length > 0 ? "|  " + tags : "")}  "
                   + $"|  {MediaLibraryFormatter.FormatDuration(item.DurationSeconds)}";
        }

        private void DrawDetails()
        {
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label("<b>選択中</b>", RichLabel());
            GUILayout.Label(MediaLibraryFormatter.FormatSelection(_library));

            GUILayout.Space(8f);

            GUI.enabled = _library.HasSelection;

            if (GUILayout.Button("▶ この曲を再生する"))
            {
                bool ok = _bridge.PlaySelected();
                _status = ok
                    ? $"{_bridge.LastHandedOffId} を PlayerSession へ渡しました。"
                    : "再生を開始できませんでした。";
            }

            if (GUILayout.Button("＋ Queue の末尾に足す"))
            {
                bool ok = _bridge.EnqueueSelected();
                _status = ok
                    ? $"{_bridge.LastHandedOffId} を Queue に足しました({_session.Queue.Count} 件)。"
                    : "Queue に足せませんでした。";
            }

            GUI.enabled = true;

            GUILayout.Space(4f);
            if (GUILayout.Button("選択を外す")) _library.ClearSelection();

            GUILayout.FlexibleSpace();
            GUILayout.Label("Library → LibraryPlaybackBridge → PlayerSession\n"
                            + "境界を越えるのは MediaId(string)だけです。");

            GUILayout.EndVertical();
        }

        /// <summary>
        /// <b>Phase4-2 の関連動画パネル。</b>
        ///
        /// この描画コードは <see cref="RelatedMediaView"/> しか見ていません。
        /// 関連 ID の出どころ(カタログ / Catalog Builder / サーバー)が変わっても、
        /// <b>ここは 1 行も変わりません。</b>
        /// </summary>
        private void DrawRelated()
        {
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label("<b>関連動画</b>  " + MediaLibraryFormatter.FormatSummary(_related),
                            RichLabel());

            if (_related.SourceMediaId == null)
            {
                GUILayout.Label("左の一覧から選ぶと、関連するものが出ます。");
                GUILayout.EndVertical();
                return;
            }

            if (_related.Count == 0)
            {
                GUILayout.Label("関連するメディアが登録されていません。");
                GUILayout.EndVertical();
                return;
            }

            _relatedScroll = GUILayout.BeginScrollView(_relatedScroll, GUILayout.MinHeight(120f));

            for (int i = 0; i < _related.Count; i++)
            {
                var item = _related.GetAt(i);
                string tags = MediaLibraryFormatter.FormatTags(item);

                if (GUILayout.Button(
                        $"▶ {item.Title}\n"
                        + $"   {item.Artist}  |  {item.Genre}  "
                        + $"{(tags.Length > 0 ? "|  " + tags : "")}  "
                        + $"|  {MediaLibraryFormatter.FormatDuration(item.DurationSeconds)}"))
                {
                    // 関連側の一覧から、そのまま再生へ渡す
                    bool ok = _relatedBridge.PlayAt(i);
                    _status = ok
                        ? $"関連動画 {item.MediaId} を再生します。"
                        : "関連動画を再生できませんでした。";
                }
            }

            GUILayout.EndScrollView();

            GUILayout.Label($"関連 ID の出どころ: {_related.Provider}");
            GUILayout.EndVertical();
        }

        private void DrawPlayerState()
        {
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label($"PlayerSession: 再生中 = {_session.CurrentMediaId ?? "(なし)"}"
                            + $"  [{_session.PlaybackState}]  Queue = {_session.Queue.Count} 件");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("再生 / 一時停止", GUILayout.Width(130f))) _session.TogglePlayPause();
            if (GUILayout.Button("次へ", GUILayout.Width(80f))) _session.Next();
            if (GUILayout.Button("前へ", GUILayout.Width(80f))) _session.Previous();
            if (GUILayout.Button("停止", GUILayout.Width(80f))) _session.Stop();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Label(_status);
            GUILayout.EndVertical();
        }

        private GUIStyle _richLabel;

        /// <summary>毎フレーム作り直さないよう覚えておく(OnGUI は 1 フレームに何度も呼ばれる)。</summary>
        private GUIStyle RichLabel()
        {
            if (_richLabel == null) _richLabel = new GUIStyle(GUI.skin.label) { richText = true };
            return _richLabel;
        }

        // ───────── Library からの通知 ─────────

        public void OnListChanged(IMediaListView view)
        {
            if (ReferenceEquals(view, _library)) _scroll = Vector2.zero;
            else _relatedScroll = Vector2.zero;
        }

        public void OnSelectionChanged(IMediaListView view, DisplayMeta selected)
        {
            if (!ReferenceEquals(view, _library)) return;   // 関連側の選択は状態行に出さない

            _status = selected != null
                ? $"選択: {selected.Title} / {selected.Artist}"
                : "選択を外しました。";

            FollowSource();
        }

        // ───────── 内部 ─────────

        private void Update()
        {
            // 再生が切り替わったら関連動画も追従させる
            // (関連の一覧から再生したときも、その先の関連へ進める)
            if (_session != null && _session.CurrentMediaId != _lastPlayingId)
            {
                _lastPlayingId = _session.CurrentMediaId;
                FollowSource();
            }
        }

        private string _lastPlayingId;

        /// <summary>
        /// <b>関連動画の起点を決める。</b>
        /// 再生中ならそれに、していなければ一覧で選んでいるものに追従します。
        ///
        /// <b>ここが「関連 UI と再生の接続点」です。</b>
        /// 渡しているのは MediaId(string)だけで、
        /// 起点が変わったあと何を並べるかは <see cref="RelatedMediaView"/> と
        /// <c>IRelatedMediaProvider</c> が決めます。
        /// </summary>
        private void FollowSource()
        {
            string source = _session != null && _session.CurrentMediaId != null
                ? _session.CurrentMediaId
                : _library.SelectedMediaId;

            _related.SetSource(source);
        }

        private void ApplyTab()
        {
            switch (_tab)
            {
                case LibraryTypeTab.Music: _library.ShowOnly(MediaType.Music); break;
                case LibraryTypeTab.Video: _library.ShowOnly(MediaType.Video); break;
                default: _library.ShowAll(); break;
            }
        }
    }
}
