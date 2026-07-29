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
    /// <b>Phase4-3 の「見える再生フロー」。</b>
    /// Game ビューに <b>Library / 関連動画 / Queue / いま再生中</b> を並べて、
    /// クリックだけで一連の流れを触れます。
    ///
    /// <b>VRChat SDK は不要</b>で、Play を押せばそのまま操作できます。
    ///
    /// <b>この画面には判断ロジックがありません。</b>
    /// 一覧も Queue も再生中の表示も <see cref="PlaybackFlow"/>(純粋 C#)が持っていて、
    /// ここは<b>描いて、押されたことを伝えるだけ</b>です。
    /// 毎フレームやっているのは <c>_flow.Tick()</c> の 1 行だけで、
    /// これが Queue の表示・再生中の表示・関連の起点をまとめて追従させます。
    /// </summary>
    public sealed class PlaybackFlowScreenDemo : MonoBehaviour, IMediaListObserver
    {
        [Header("表示")]
        [Tooltip("最初に表示する種別(すべて / Music / Video)")]
        [SerializeField] private LibraryTypeTab _initialTab = LibraryTypeTab.Video;

        [Tooltip("画面が小さいときに文字を詰める")]
        [SerializeField] private int _fontSize = 12;

        [Header("関連動画")]
        [Tooltip("関連動画を何件まで出すか(0 で制限なし)")]
        [SerializeField] private int _relatedCount = 5;

        private PlaybackFlow _flow;
        private PlayerSession _session;
        private DummyBackend _backend;
        private ListBackendLogger _logger;

        private LibraryTypeTab _tab;
        private Vector2 _libraryScroll;
        private Vector2 _queueScroll;
        private string _status = "左の一覧から選んで「▶ 再生」を押してください。";

        private GUIStyle _richLabel;

        /// <summary>絞り込みのタブ。</summary>
        public enum LibraryTypeTab
        {
            All = 0,
            Music = 1,
            Video = 2,
        }

        /// <summary>組み立て済みの再生フロー(テスト・外部からの操作用)。</summary>
        public PlaybackFlow Flow => _flow;

        private void Awake()
        {
            _logger = new ListBackendLogger();

            IMediaCatalog catalog = new MediaCatalog(new MixedCatalogSource(), new System.Random(1));

            // ── 再生側は Phase1〜3 の組み立てをそのまま使う(変更なし)
            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, _logger);
            _backend = new DummyBackend("DummyBackend", _logger);
            backendManager.RegisterBackend(_backend);

            var mediaPlayer = new MediaPlayer(backendManager, _logger);
            var engine = new RecommendationEngine(catalog, RecommendationRule.CreateDefault());
            _session = new PlayerSession(
                "flow-screen", mediaPlayer, catalog, engine, new System.Random(1), _logger);

            // ── Phase4-3: ここ 1 行で Library → Queue → Player が繋がる
            _flow = PlaybackFlow.Create(catalog, _session, _relatedCount, _logger);
            _flow.Library.AddObserver(this);

            _tab = _initialTab;
            ApplyTab();
        }

        private void Update()
        {
            // ★ Phase4-3 の要:毎フレームこれだけ。
            //   Queue の表示・再生中の表示・関連の起点がまとめて追従します。
            _flow?.Tick();
        }

        // ───────── 描画 ─────────

        private void OnGUI()
        {
            if (_flow == null) return;

            GUI.skin.label.fontSize = _fontSize;
            GUI.skin.button.fontSize = _fontSize;

            GUILayout.BeginArea(new Rect(10f, 10f, Screen.width - 20f, Screen.height - 20f));

            DrawNowPlaying();
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            DrawLibrary();
            DrawMiddle();
            DrawQueue();
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.Label(_status);

            GUILayout.EndArea();
        }

        /// <summary>いちばん上:いま再生しているもの。</summary>
        private void DrawNowPlaying()
        {
            var now = _flow.NowPlaying;

            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label($"<b>▶ {now.FormatTitle()}</b>   [{now.State}]   {now.FormatTime()}",
                            RichLabel());

            // 進捗バー(見た目だけ。シークは対応バックエンドのみ)
            var rect = GUILayoutUtility.GetRect(1f, 6f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none);
            if (now.Duration > 0f)
            {
                var filled = new Rect(rect.x, rect.y, rect.width * now.Progress, rect.height);
                GUI.Box(filled, GUIContent.none);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("再生 / 一時停止", GUILayout.Width(130f))) _session.TogglePlayPause();
            if (GUILayout.Button("次へ", GUILayout.Width(70f))) _session.Next();
            if (GUILayout.Button("前へ", GUILayout.Width(70f))) _session.Previous();
            if (GUILayout.Button("停止", GUILayout.Width(70f))) _session.Stop();

            GUILayout.Space(16f);
            if (GUILayout.Button("曲を終わらせる(Ended)", GUILayout.Width(190f)))
            {
                // DummyBackend は実際には鳴らないので、終了を手で起こして
                // 「Ended → 次へ」の配線を確かめられるようにする
                bool ok = _backend.SimulateEnded();
                _status = ok ? "Ended を発火しました。" : "再生中ではないので Ended は出せません。";
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        /// <summary>左:カタログ全体。</summary>
        private void DrawLibrary()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(Screen.width * 0.40f));

            GUILayout.Label("<b>Library</b>  " + MediaLibraryFormatter.FormatSummary(_flow.Library),
                            RichLabel());

            GUILayout.BeginHorizontal();
            DrawTab(LibraryTypeTab.All, "すべて");
            DrawTab(LibraryTypeTab.Music, "Music");
            DrawTab(LibraryTypeTab.Video, "Video");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            _libraryScroll = GUILayout.BeginScrollView(_libraryScroll);
            for (int i = 0; i < _flow.Library.Count; i++)
            {
                var item = _flow.Library.GetAt(i);
                bool selected = i == _flow.Library.SelectedIndex;

                if (GUILayout.Toggle(selected, RowLabel(item, i), GUI.skin.button) != selected)
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
                    : "再生できませんでした。";
            }
            if (GUILayout.Button("次に再生"))
            {
                _status = _flow.LibraryPlayback.PlayNextSelected()
                    ? $"{_flow.LibraryPlayback.LastHandedOffId} を次に入れました。"
                    : "次に入れられませんでした。";
            }
            if (GUILayout.Button("＋ Queue"))
            {
                _status = _flow.LibraryPlayback.EnqueueSelected()
                    ? $"{_flow.LibraryPlayback.LastHandedOffId} を Queue に足しました。"
                    : "Queue に足せませんでした。";
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            GUILayout.EndVertical();
        }

        /// <summary>まん中:関連動画。</summary>
        private void DrawMiddle()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(Screen.width * 0.27f));

            GUILayout.Label("<b>関連動画</b>  " + MediaLibraryFormatter.FormatSummary(_flow.Related),
                            RichLabel());

            if (_flow.Related.Count == 0)
            {
                GUILayout.Label(_flow.Related.SourceMediaId == null
                    ? "再生するか、左で選ぶと出ます。"
                    : "関連が登録されていません。");
            }

            for (int i = 0; i < _flow.Related.Count; i++)
            {
                var item = _flow.Related.GetAt(i);
                if (GUILayout.Button($"▶ {item.Title}\n   {item.Artist}"))
                {
                    _status = _flow.RelatedPlayback.PlayAt(i)
                        ? $"関連から {item.MediaId} を再生します。"
                        : "再生できませんでした。";
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label($"出どころ:\n{_flow.RelatedProvider}");
            GUILayout.EndVertical();
        }

        /// <summary>右:Queue。</summary>
        private void DrawQueue()
        {
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label($"<b>Queue</b>  {_flow.Queue.Count} 件", RichLabel());

            _queueScroll = GUILayout.BeginScrollView(_queueScroll);

            if (_flow.Queue.Count == 0) GUILayout.Label("(空)");

            for (int i = 0; i < _flow.Queue.Count; i++)
            {
                var item = _flow.Queue.GetAt(i);
                bool isNow = i == 0;

                GUILayout.BeginHorizontal();

                bool selected = i == _flow.Queue.SelectedIndex;
                string label = (isNow ? "♪ " : $"{i}. ") + item.Title;

                if (GUILayout.Toggle(selected, label, GUI.skin.button) != selected)
                {
                    _flow.Queue.Select(i);
                }

                // 先頭(いま鳴っているもの)は動かさない・消さない
                GUI.enabled = !isNow;
                if (GUILayout.Button("▲", GUILayout.Width(28f))) _flow.Queue.MoveUp(i);
                if (GUILayout.Button("▼", GUILayout.Width(28f))) _flow.Queue.MoveDown(i);
                if (GUILayout.Button("×", GUILayout.Width(28f))) _flow.Queue.RemoveAt(i);
                GUI.enabled = true;

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            GUI.enabled = _flow.Queue.HasSelection && _flow.Queue.SelectedIndex > 0;
            if (GUILayout.Button("この曲へ飛ぶ"))
            {
                _status = _flow.QueuePlayback.PlaySelected()
                    ? $"{_flow.QueuePlayback.LastHandedOffId} へ飛びました。"
                    : "飛べませんでした。";
            }
            GUI.enabled = true;

            if (GUILayout.Button("この曲より後を消す"))
            {
                int removed = _flow.Queue.ClearUpcoming();
                _status = $"{removed} 件を Queue から外しました。";
            }

            GUILayout.EndVertical();
        }

        // ───────── 補助 ─────────

        private void DrawTab(LibraryTypeTab tab, string label)
        {
            bool active = _tab == tab;
            if (GUILayout.Toggle(active, label, GUI.skin.button, GUILayout.Width(70f)) != active)
            {
                _tab = tab;
                ApplyTab();
            }
        }

        private void ApplyTab()
        {
            switch (_tab)
            {
                case LibraryTypeTab.Music: _flow.Library.ShowOnly(MediaType.Music); break;
                case LibraryTypeTab.Video: _flow.Library.ShowOnly(MediaType.Video); break;
                default: _flow.Library.ShowAll(); break;
            }
        }

        private string RowLabel(DisplayMeta item, int index)
        {
            return $"{index + 1,3}. {item.Title}\n"
                   + $"      {item.Artist}  |  {item.Type}  |  "
                   + MediaLibraryFormatter.FormatDuration(item.DurationSeconds);
        }

        private GUIStyle RichLabel()
        {
            if (_richLabel == null) _richLabel = new GUIStyle(GUI.skin.label) { richText = true };
            return _richLabel;
        }

        // ───────── 一覧からの通知 ─────────

        public void OnListChanged(IMediaListView view)
        {
            if (ReferenceEquals(view, _flow.Library)) _libraryScroll = Vector2.zero;
        }

        public void OnSelectionChanged(IMediaListView view, DisplayMeta selected)
        {
            if (!ReferenceEquals(view, _flow.Library)) return;

            _status = selected != null
                ? $"選択: {selected.Title} / {selected.Artist}"
                : "選択を外しました。";

            // 何も再生していないときは、選んだものの関連を見せる
            _flow.FollowSource();
        }
    }
}
