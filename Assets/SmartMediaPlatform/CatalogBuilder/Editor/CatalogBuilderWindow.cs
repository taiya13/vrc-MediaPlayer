#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Assets;
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.CatalogBuilder.EditorTools
{
    /// <summary>
    /// <b>Catalog Builder。</b>Phase6-1 → Phase6-5。
    /// <c>Tools > Smart Media Platform > Catalog Builder</c>。
    ///
    /// <b>この窓は判断を持ちません。</b>持っているのは
    /// <list type="bullet">
    /// <item>いま何を編集しているか(<see cref="_asset"/> / <see cref="_draft"/>)</item>
    /// <item>どこを開いているか(タブ・スクロール位置・折りたたみ)</item>
    /// <item>何にチェックを付けたか(<see cref="_checkedIds"/>)</item>
    /// </list>
    /// だけで、<b>足す・消す・混ぜる・絞る・並べる・まとめる・関連を作る</b>は
    /// すべて <c>Runtime</c> 側の純粋 C# の仕事です。
    /// おかげで判断の部分は EditMode で検証できます
    /// (Phase5 の「UI はロジックを持たない」と同じ考え方)。
    ///
    /// <b>取り込み元を足しても、この窓は変わりません。</b>
    /// 絞り込みも並べ替えもまとめ方も関連も、見ているのは
    /// <see cref="CatalogDraftItem"/> だけで、YouTube 固有の言葉は 1 つも出てきません。
    /// 新着だけ取る仕組みも <see cref="IIncrementalCatalogImporter"/> 越しなので、
    /// <b>実装していない取り込み元(CSV など)もそのまま並びます</b>。
    ///
    /// <b>Phase6-5 で右側をタブにしました。</b>
    /// 縦に伸び続けて下のボタンが画面外へ出ていたためです。
    /// 左の一覧は常に見えたまま、右だけが切り替わります。
    ///
    /// <b>MediaPlayer 本体には触りません。</b>書き込み先は
    /// <see cref="MediaCatalogAsset"/> と、その横の覚え書き JSON だけです。
    /// </summary>
    public sealed partial class CatalogBuilderWindow : EditorWindow
    {
        private const string MenuPath = "Tools/Smart Media Platform/Catalog Builder";

        private const int TabItem = 0;
        private const int TabBulk = 1;
        private const int TabImport = 2;
        private const int TabSubscriptions = 3;

        private static readonly string[] TabLabels =
        {
            "選んだ 1 件", "まとめて直す", "取り込む", "チャンネル",
        };

        private MediaCatalogAsset _asset;
        private CatalogDraft _draft = new CatalogDraft();
        private readonly CatalogSubscriptionBook _book = new CatalogSubscriptionBook();

        private Vector2 _listScroll;
        private Vector2 _rightScroll;
        private Vector2 _outerScroll;

        /// <summary>真ん中の 2 枚をこれ以上は縮めない高さ。</summary>
        private const float MinimumPaneHeight = 180f;
        private int _selected = -1;
        private int _tab = TabItem;
        private bool _dirty;

        // 取り込み
        private int _importerIndex;
        private string _importerInput = "";
        private int _mergeMode = CatalogDraft.MergeSkip;
        private string _importMessage = "";
        private bool _lastImportOk;

        private readonly CatalogImportSelection _selection = new CatalogImportSelection();
        private Vector2 _selectionScroll;

        // 絞り込み / 並べ替え / まとめ / 絵 / 関連
        private readonly CatalogItemFilter _filter = new CatalogItemFilter();
        private readonly CatalogItemGrouping _draftGrouping = new CatalogItemGrouping();
        private readonly RelatedIdGenerator _related = new RelatedIdGenerator();

        private int _sortOrder = CatalogSortOrder.Manual;
        private bool _sortDescending;

        private bool _showThumbnails = true;
        /// <summary>
        /// <b>チャンネルごとにまとめて出す。</b>Phase7-9 で<b>既定を「まとめる」に</b>しました。
        ///
        /// 40 件・80 件と取り込むと、平らな一覧では
        /// <b>どこからどこまでが 1 つのチャンネルか</b>が読めません。
        /// まとめれば、チャンネル単位で畳めて、
        /// 「このチャンネル全部にジャンルを付ける」も 2 手で終わります。
        ///
        /// <b>これは Unity の中の見せ方だけです。</b>
        /// 書き出す中身も並びも変わらないので、<b>ワールド側は何も変わりません</b>。
        /// </summary>
        private bool _groupDraft = true;
        private bool _groupSelection = true;
        private bool _autoRelated = true;
        private bool _showRelatedOptions;
        private bool _showSetup;
        // Phase8:既定は「焼かない」。YouTube のサムネイルをワールドへ
        // 焼き込むと、著作物の再配布になるためです。
        private int _thumbnailSize = CatalogThumbnailBaker.SizeOff;

        // Phase8:この並びは何順か(0 = 不明 / 1 = 人気順 / 2 = 新着順)。
        // 再生数を保存する代わりに、並びの意味だけを焼き込みます。
        // <b>既定は「不明」</b>です。手で並べ替えたり混ぜたりすると
        // 並びの意味が失われるので、作者が明示したときだけ効かせます。
        private int _catalogOrderKind;

        private static readonly string[] OrderKindLabels =
        {
            "不明(おすすめで使わない)", "人気順に並んでいる", "新着順に並んでいる",
        };
        private int[] _visible = new int[0];

        // まとめて直す
        private readonly List<string> _checkedIds = new List<string>();
        private string _bulkGenre = "";
        private string _bulkArtist = "";
        private string _bulkTag = "";

        // 焼き忘れの検出。毎フレーム調べると重いので間を空ける。
        private CatalogUrlTableBridge.SyncReport _syncReport;
        private double _nextSyncCheck;

        private static readonly int[] MergeModes =
        {
            CatalogDraft.MergeSkip, CatalogDraft.MergeUpdate, CatalogDraft.MergeReplace,
        };

        private static readonly string[] MergeLabels =
        {
            "すでにあるものは飛ばす", "すでにあるものを新しくする", "いまの中身を捨てて入れ替え",
        };

        private static readonly string[] GroupLabels =
        {
            "チャンネルごと", "ジャンルごと", "取り込み元ごと", "まとめない",
        };

        [MenuItem(MenuPath, false, -200)]
        public static void Open()
        {
            var window = GetWindow<CatalogBuilderWindow>("Catalog Builder");

            // 小さくしても下まで届くよう、最小の縛りはゆるくしておく。
            // 足りないぶんは外側のスクロールが受け持つ。
            window.minSize = new Vector2(420f, 280f);
            window.Show();
        }

        /// <summary>
        /// <b>窓を小さくしても下まで届くようにしてあります。</b>Phase6-6。
        ///
        /// <b>作り</b>
        /// <list type="number">
        /// <item>上のバーと絞り込みは<b>いつも上に固定</b>(いちばんよく使うため)</item>
        /// <item>真ん中の 2 枚は<b>残った高さいっぱい</b>に伸び縮みする</item>
        /// <item>③ のボタンは<b>いつも下に固定</b>(押せないと詰むため)</item>
        /// <item>それでも足りないくらい小さくしたら、<b>外側がスクロール</b>する</item>
        /// </list>
        /// Phase6-5 までは真ん中が縮まず、下のボタンが画面の外へ出ていました。
        /// </summary>
        private void OnGUI()
        {
            DrawModeBar();

            // ── ふだんはこちら。「URL を貼って曲を足す」だけの道。
            //    表を並べた画面は、慣れた人のための道具として奥に置きます。
            if (!_expertMode)
            {
                DrawSimpleFlow();
                return;
            }

            DrawToolbar();

            if (_asset == null)
            {
                _outerScroll = EditorGUILayout.BeginScrollView(_outerScroll);
                DrawNoAsset();
                EditorGUILayout.EndScrollView();
                return;
            }

            // ── 下に置くもののぶんを先に取っておく。
            //    これをしないと、真ん中が伸びきってボタンが画面外へ出る。
            float footer = FooterHeight();
            float middle = position.height - HeaderHeight() - footer;

            bool tooSmall = middle < MinimumPaneHeight;
            if (tooSmall)
            {
                // ここまで小さいと固定では収まらない。全部を 1 本のスクロールに入れる。
                _outerScroll = EditorGUILayout.BeginScrollView(_outerScroll);

                DrawSyncWarning();
                DrawFilterBar();
                DrawPanes(MinimumPaneHeight);
                DrawFooter();

                EditorGUILayout.EndScrollView();
                return;
            }

            DrawSyncWarning();
            DrawFilterBar();
            DrawPanes(middle);

            GUILayout.FlexibleSpace();
            DrawFooter();
        }

        /// <summary>真ん中の 2 枚。高さは呼び手が決める。</summary>
        private void DrawPanes(float height)
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(height));
            DrawList(height);
            DrawRightPane(height);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>上のバー + 焼き忘れの警告 + 絞り込みのおよその高さ。</summary>
        private float HeaderHeight()
        {
            float height = 22f + 22f;   // ツールバー + 絞り込み

            if (_syncReport != null && _syncReport.Checked && !_syncReport.InSync) height += 56f;

            return height;
        }

        /// <summary>下に固定するもののおよその高さ。</summary>
        private float FooterHeight()
        {
            float height = 30f + 18f;   // ③ のボタン + 1 行の案内

            if (_draft.FindDuplicateIds().Length > 0) height += 46f;

            return height;
        }

        // ───────── 上のバー ─────────

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            var picked = (MediaCatalogAsset)EditorGUILayout.ObjectField(
                _asset, typeof(MediaCatalogAsset), false, GUILayout.Width(220f));

            if (picked != _asset)
            {
                if (!ConfirmDiscard()) return;
                _asset = picked;
                ReloadFromAsset();
            }

            if (GUILayout.Button("新規", EditorStyles.toolbarButton, GUILayout.Width(48f)))
            {
                if (ConfirmDiscard())
                {
                    var created = CatalogDraftIO.CreateNew();
                    if (created != null)
                    {
                        _asset = created;
                        ReloadFromAsset();
                    }
                }
            }

            using (new EditorGUI.DisabledScope(_asset == null))
            {
                if (GUILayout.Button("読み直す", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                {
                    if (ConfirmDiscard()) ReloadFromAsset();
                }

                if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(48f)))
                {
                    SaveToAsset();
                }
            }

            GUILayout.FlexibleSpace();

            if (CatalogThumbnailCache.Pending > 0)
            {
                GUILayout.Label("絵を読み込み中 " + CatalogThumbnailCache.Pending + " 枚",
                                EditorStyles.miniLabel);
            }

            if (_dirty) GUILayout.Label("未保存", EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawNoAsset()
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "編集するカタログを選んでください。\n"
                + "上の欄へ Catalog.asset をドラッグするか、「新規」で作れます。",
                MessageType.Info);

            var found = CatalogDraftIO.FindAll();
            if (found.Length == 0) return;

            EditorGUILayout.LabelField("プロジェクトにあるカタログ", EditorStyles.boldLabel);

            for (int i = 0; i < found.Length; i++)
            {
                if (!GUILayout.Button(AssetDatabase.GetAssetPath(found[i]))) continue;

                _asset = found[i];
                ReloadFromAsset();
            }
        }

        // ───────── 焼き忘れの警告(Phase6-5)─────────

        /// <summary>
        /// <b>保存しただけではワールドに反映されません。</b>
        /// 焼き忘れると「直したはずなのに現地では古いまま」になり、
        /// しかも何も言われないのが Phase6-3 からの積み残しでした。
        /// </summary>
        private void DrawSyncWarning()
        {
            RefreshSyncReport(false);

            if (_syncReport == null || !_syncReport.Checked || _syncReport.InSync) return;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            EditorGUILayout.LabelField("⚠ " + _syncReport.Message, EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUI.DisabledScope(_dirty))
            {
                if (GUILayout.Button("いま焼く", GUILayout.Width(80f), GUILayout.Height(32f)))
                {
                    Bake();
                }
            }

            EditorGUILayout.EndHorizontal();

            if (_dirty)
            {
                EditorGUILayout.LabelField(
                    "  先に保存してください(未保存のぶんは焼かれません)。",
                    EditorStyles.miniLabel);
            }
        }

        /// <summary>ずれを調べ直す。<paramref name="force"/> でなければ間隔を空ける。</summary>
        private void RefreshSyncReport(bool force)
        {
            if (!force && EditorApplication.timeSinceStartup < _nextSyncCheck) return;

            // シーン全部を探して反射で読み返すので、毎フレームやるには重い。
            _nextSyncCheck = EditorApplication.timeSinceStartup + 2.0;
            _syncReport = CatalogUrlTableBridge.CompareWithScene(_asset);
        }

        // ───────── 絞り込みと並べ替え ─────────

        private void DrawFilterBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("絞り込み", EditorStyles.miniBoldLabel, GUILayout.Width(52f));

            _filter.Query = EditorGUILayout.TextField(
                _filter.Query, EditorStyles.toolbarTextField, GUILayout.Width(150f));

            _filter.Channel = DrawPickList(
                "チャンネル", _filter.Channel, CatalogItemFilter.CollectChannels(_draft.Items), 120f);

            _filter.Genre = DrawPickList(
                "ジャンル", _filter.Genre, CatalogItemFilter.CollectGenres(_draft.Items), 100f);

            _filter.Tag = DrawPickList(
                "タグ", _filter.Tag, CatalogItemFilter.CollectTags(_draft.Items), 100f);

            _filter.OnlyIncomplete = GUILayout.Toggle(
                _filter.OnlyIncomplete, "作りかけ", EditorStyles.toolbarButton, GUILayout.Width(64f));

            using (new EditorGUI.DisabledScope(_filter.IsEmpty))
            {
                if (GUILayout.Button("解除", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                {
                    _filter.Clear();
                    GUI.FocusControl(null);
                }
            }

            GUILayout.FlexibleSpace();

            GUILayout.Label("並び", EditorStyles.miniBoldLabel, GUILayout.Width(28f));

            _sortOrder = EditorGUILayout.Popup(
                _sortOrder, CatalogSortOrder.Labels, EditorStyles.toolbarPopup, GUILayout.Width(88f));

            _sortDescending = GUILayout.Toggle(
                _sortDescending, _sortDescending ? "↓ 降順" : "↑ 昇順",
                EditorStyles.toolbarButton, GUILayout.Width(58f));

            _showThumbnails = GUILayout.Toggle(
                _showThumbnails, "絵", EditorStyles.toolbarButton, GUILayout.Width(30f));

            _groupDraft = GUILayout.Toggle(
                _groupDraft, "まとめる", EditorStyles.toolbarButton, GUILayout.Width(60f));

            EditorGUILayout.EndHorizontal();
        }

        private static string DrawPickList(string label, string current, string[] values, float width)
        {
            var names = new string[values.Length + 1];
            names[0] = label + ": すべて";

            int selected = 0;
            for (int i = 0; i < values.Length; i++)
            {
                names[i + 1] = values[i];
                if (string.Equals(values[i], current, StringComparison.OrdinalIgnoreCase))
                {
                    selected = i + 1;
                }
            }

            int picked = EditorGUILayout.Popup(
                selected, names, EditorStyles.toolbarPopup, GUILayout.Width(width));

            return picked <= 0 ? "" : names[picked];
        }

        // ───────── 左:一覧 ─────────

        private void DrawList(float height)
        {
            // 窓を細くしたときに、右のタブが潰れないようにする。
            float width = Mathf.Clamp(position.width * 0.42f, 200f, 460f);

            EditorGUILayout.BeginVertical(GUILayout.Width(width), GUILayout.Height(height));

            // 絞ってから並べる。どちらも「元の位置」を返すので重ねられる。
            _visible = CatalogSortOrder.Apply(
                _draft.Items, _filter.Apply(_draft.Items), _sortOrder, _sortDescending);

            DrawListHeading();

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            if (_visible.Length == 0)
            {
                EditorGUILayout.LabelField(
                    _draft.Count == 0 ? "まだ 1 件もありません。" : "絞り込みに合うものがありません。",
                    EditorStyles.miniLabel);
            }
            else if (_groupDraft) DrawGroupedList();
            else
            {
                for (int i = 0; i < _visible.Length; i++) DrawListRow(_visible[i]);
            }

            EditorGUILayout.EndScrollView();

            DrawListButtons();
            EditorGUILayout.EndVertical();
        }

        private void DrawListHeading()
        {
            string heading = _draft.Count + " 件 / 完成 " + _draft.CompleteCount + " 件";
            if (!_filter.IsEmpty) heading = _visible.Length + " 件を表示中 (" + heading + ")";

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(heading, EditorStyles.boldLabel);

            if (_checkedIds.Count > 0)
            {
                EditorGUILayout.LabelField(
                    "☑ " + _checkedIds.Count + " 件", EditorStyles.miniBoldLabel,
                    GUILayout.Width(64f));
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("表示中を全部チェック", EditorStyles.miniButtonLeft))
            {
                for (int i = 0; i < _visible.Length; i++) SetChecked(_visible[i], true);
            }

            using (new EditorGUI.DisabledScope(_checkedIds.Count == 0))
            {
                if (GUILayout.Button("チェックを外す", EditorStyles.miniButtonRight))
                {
                    _checkedIds.Clear();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawGroupedList()
        {
            _draftGrouping.Build(_draft.Items);

            for (int group = 0; group < _draftGrouping.GroupCount; group++)
            {
                int[] members = _draftGrouping.GetIndices(group);

                int shown = 0;
                for (int i = 0; i < members.Length; i++)
                {
                    if (Array.IndexOf(_visible, members[i]) >= 0) shown++;
                }
                if (shown == 0) continue;

                EditorGUILayout.BeginHorizontal();

                bool expanded = EditorGUILayout.Foldout(
                    _draftGrouping.IsExpanded(group),
                    _draftGrouping.GetName(group) + "  (" + shown + " 件)", true);

                _draftGrouping.SetExpanded(group, expanded);

                // まとまりごとチェックできると、「このチャンネル全部にジャンル」が 2 手で終わる。
                if (GUILayout.Button("全部☑", EditorStyles.miniButton, GUILayout.Width(48f)))
                {
                    for (int i = 0; i < members.Length; i++) SetChecked(members[i], true);
                }

                EditorGUILayout.EndHorizontal();

                if (!expanded) continue;

                EditorGUI.indentLevel++;
                for (int i = 0; i < members.Length; i++)
                {
                    if (Array.IndexOf(_visible, members[i]) < 0) continue;
                    DrawListRow(members[i]);
                }
                EditorGUI.indentLevel--;
            }
        }

        private void DrawListRow(int index)
        {
            CatalogDraftItem item = _draft.GetAt(index);
            if (item == null) return;

            EditorGUILayout.BeginHorizontal();

            bool wasChecked = IsChecked(item);
            if (EditorGUILayout.Toggle(wasChecked, GUILayout.Width(18f)) != wasChecked)
            {
                SetChecked(index, !wasChecked);
            }

            DrawThumbnail(item, 46f);

            bool wasSelected = index == _selected;
            string label = (index + 1) + ". " + (item.IsComplete ? "" : "⚠ ") + item;

            if (GUILayout.Toggle(wasSelected, label, EditorStyles.miniButton) != wasSelected)
            {
                _selected = index;
                _tab = TabItem;
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawThumbnail(CatalogDraftItem item, float width)
        {
            if (!_showThumbnails) return;

            float height = Mathf.Round(width * 9f / 16f);
            Rect rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));

            Texture2D texture = CatalogThumbnailCache.Get(item.ThumbnailPath);

            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleAndCrop);
            else EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.18f));
        }

        private void DrawListButtons()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("＋ 追加"))
            {
                _selected = _draft.Add();
                _tab = TabItem;
                MarkDirty();
            }

            using (new EditorGUI.DisabledScope(_selected < 0))
            {
                if (GUILayout.Button("複製"))
                {
                    _selected = _draft.Duplicate(_selected);
                    MarkDirty();
                }

                if (GUILayout.Button("削除"))
                {
                    if (_draft.RemoveAt(_selected))
                    {
                        _selected = Mathf.Min(_selected, _draft.Count - 1);
                        MarkDirty();
                    }
                }

                // 並べ替えは「元の並び」に対して効く。見えている隣と本当の隣が
                // 違うときに動かすと、思っていない場所へ飛ぶ。
                bool canMove = _filter.IsEmpty && _sortOrder == CatalogSortOrder.Manual
                               && !_sortDescending;

                using (new EditorGUI.DisabledScope(!canMove))
                {
                    if (GUILayout.Button("▲", GUILayout.Width(28f)))
                    {
                        if (_draft.Move(_selected, -1)) { _selected--; MarkDirty(); }
                    }

                    if (GUILayout.Button("▼", GUILayout.Width(28f)))
                    {
                        if (_draft.Move(_selected, 1)) { _selected++; MarkDirty(); }
                    }
                }
            }

            EditorGUILayout.EndHorizontal();

            if (_sortOrder != CatalogSortOrder.Manual)
            {
                EditorGUILayout.LabelField(
                    "並べ替えて表示中です。中身の順は変わっていません(手動順に戻せます)。",
                    EditorStyles.miniLabel);
            }
        }

        // ───────── チェックしたもの ─────────

        /// <summary>
        /// チェックは<b>位置ではなく ID</b>で覚えます。
        /// 並べ替えても絞り込んでも、選んだものが入れ替わらないようにするためです。
        /// </summary>
        private bool IsChecked(CatalogDraftItem item)
        {
            return item != null && !string.IsNullOrWhiteSpace(item.Id)
                   && _checkedIds.Contains(item.Id.Trim());
        }

        private void SetChecked(int index, bool value)
        {
            CatalogDraftItem item = _draft.GetAt(index);
            if (item == null || string.IsNullOrWhiteSpace(item.Id)) return;

            string id = item.Id.Trim();

            if (value)
            {
                if (!_checkedIds.Contains(id)) _checkedIds.Add(id);
            }
            else _checkedIds.Remove(id);
        }

        /// <summary>チェックしたものの「元の位置」。まとめて直すのに渡します。</summary>
        private int[] CheckedPositions()
        {
            var result = new List<int>();

            for (int i = 0; i < _draft.Count; i++)
            {
                if (IsChecked(_draft.GetAt(i))) result.Add(i);
            }
            return result.ToArray();
        }

        // ───────── 右:タブ ─────────

        private void DrawRightPane(float height)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(height));

            _tab = GUILayout.Toolbar(_tab, TabLabels);

            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

            if (_tab == TabBulk) DrawBulkTab();
            else if (_tab == TabImport) DrawImportTab();
            else if (_tab == TabSubscriptions) DrawSubscriptionTab();
            else DrawItemTab();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ───────── タブ:1 件の編集 ─────────

        private void DrawItemTab()
        {
            CatalogDraftItem item = _draft.GetAt(_selected);
            if (item == null)
            {
                EditorGUILayout.LabelField("左の一覧から選んでください。");
                return;
            }

            EditorGUI.BeginChangeCheck();

            item.Id = EditorGUILayout.TextField("ID", item.Id);
            item.Title = EditorGUILayout.TextField("見出し", item.Title);
            item.Artist = EditorGUILayout.TextField("チャンネル", item.Artist);
            item.Genre = EditorGUILayout.TextField("ジャンル", item.Genre);
            item.Type = (MediaType)EditorGUILayout.EnumPopup("種別", item.Type);
            item.Url = EditorGUILayout.TextField("URL", item.Url);
            item.DurationSeconds = EditorGUILayout.IntField("長さ(秒)", item.DurationSeconds);
            item.PublishedAt = EditorGUILayout.TextField("公開日", item.PublishedAt);

            EditorGUILayout.Space();
            item.Tags = DrawStringList("タグ", item.Tags);

            item.RelatedIds = DrawStringList("関連 ID(おすすめに出る)", item.RelatedIds);
            if (GUILayout.Button("この 1 件の関連を作り直す", EditorStyles.miniButton))
            {
                var single = new RelatedIdGenerator();
                CopyRelatedSettings(single);

                item.RelatedIds = single.Suggest(_draft.Items, _selected);
                MarkDirty();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("出どころ", item.Source);

            item.ThumbnailPath = EditorGUILayout.TextField("サムネイル URL", item.ThumbnailPath);
            DrawThumbnailPreview(item);

            if (EditorGUI.EndChangeCheck()) MarkDirty();

            string problem = item.Describe();
            if (problem.Length > 0)
            {
                EditorGUILayout.HelpBox(problem + "(このままだと保存時に落ちます)", MessageType.Warning);
            }

            if (GUILayout.Button("ID を空いているものにする"))
            {
                item.Id = _draft.MakeUniqueId(item.Id);
                MarkDirty();
            }
        }

        private void DrawThumbnailPreview(CatalogDraftItem item)
        {
            if (!CatalogThumbnailCache.IsSupported(item.ThumbnailPath)) return;

            Texture2D texture = CatalogThumbnailCache.Get(item.ThumbnailPath);
            if (texture == null)
            {
                EditorGUILayout.LabelField(" ", "読み込み中…", EditorStyles.miniLabel);
                return;
            }

            Rect rect = GUILayoutUtility.GetRect(200f, 113f, GUILayout.ExpandWidth(false));
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
        }

        private static string[] DrawStringList(string label, string[] values)
        {
            if (values == null) values = new string[0];

            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);

            int removeAt = -1;
            for (int i = 0; i < values.Length; i++)
            {
                EditorGUILayout.BeginHorizontal();
                values[i] = EditorGUILayout.TextField(values[i]);
                if (GUILayout.Button("×", GUILayout.Width(24f))) removeAt = i;
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("＋ " + label + " を足す", EditorStyles.miniButton))
            {
                var grown = new string[values.Length + 1];
                for (int i = 0; i < values.Length; i++) grown[i] = values[i];
                grown[values.Length] = "";
                return grown;
            }

            if (removeAt < 0) return values;

            var shrunk = new string[values.Length - 1];
            int at = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (i == removeAt) continue;
                shrunk[at] = values[i];
                at++;
            }
            return shrunk;
        }

        // ───────── タブ:まとめて直す(Phase6-5)─────────

        private void DrawBulkTab()
        {
            int[] positions = CheckedPositions();

            EditorGUILayout.LabelField(
                "チェックした " + positions.Length + " 件を直します", EditorStyles.boldLabel);

            if (positions.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "左の一覧でチェックを付けてください。\n"
                    + "「絞り込み」で目当てのものだけ出してから"
                    + "「表示中を全部チェック」が速いです。",
                    MessageType.Info);
                return;
            }

            DrawBulkClassify(positions);
            DrawBulkGenre(positions);
            DrawBulkArtist(positions);
            DrawBulkTags(positions);
            DrawBulkRelated(positions);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("チェックしたもの", EditorStyles.miniBoldLabel);

            int preview = positions.Length < 8 ? positions.Length : 8;
            for (int i = 0; i < preview; i++)
            {
                EditorGUILayout.LabelField("  " + _draft.GetAt(positions[i]), EditorStyles.miniLabel);
            }
            if (positions.Length > preview)
            {
                EditorGUILayout.LabelField(
                    "  ほか " + (positions.Length - preview) + " 件", EditorStyles.miniLabel);
            }
        }

        /// <summary>
        /// <b>ジャンルを言い当てて入れる。</b>Phase6-6。
        ///
        /// Phase6-4 まではカタログ全部が「音楽」になっていました。
        /// ここを 1 回押せば、見出し・タグ・チャンネル名から付け直せます。
        /// </summary>
        private void DrawBulkClassify(int[] positions)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ジャンルを自動で決める", EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("空いているものだけ"))
            {
                Report(CatalogBulkEdit.ClassifyGenres(
                           _draft.Items, positions, Genres.GenreClassifier.Shared, false),
                       "ジャンルの判定");
            }

            if (GUILayout.Button("全部付け直す"))
            {
                if (EditorUtility.DisplayDialog(
                        "Catalog Builder",
                        "手で直したジャンルも上書きします。よいですか?\n"
                        + "(判定できなかったものは、いまのジャンルを残します)",
                        "付け直す", "やめる"))
                {
                    Report(CatalogBulkEdit.ClassifyGenres(
                               _draft.Items, positions, Genres.GenreClassifier.Shared, true),
                           "ジャンルの判定");
                }
            }

            EditorGUILayout.EndHorizontal();

            DrawClassifyPreview(positions);
        }

        /// <summary>1 件目の判定理由を出す。「なぜこうなったか」が見えないと直しようがない。</summary>
        private void DrawClassifyPreview(int[] positions)
        {
            if (positions.Length == 0) return;

            CatalogDraftItem first = _draft.GetAt(positions[0]);
            if (first == null) return;

            EditorGUILayout.LabelField(
                "例: " + first + " → "
                + Genres.GenreClassifier.Shared.Explain(Genres.GenreSignals.FromDraftItem(first)),
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawBulkGenre(int[] positions)
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            _bulkGenre = EditorGUILayout.TextField("ジャンル", _bulkGenre);

            if (GUILayout.Button("入れる", GUILayout.Width(64f)))
            {
                Report(CatalogBulkEdit.SetGenre(_draft.Items, positions, _bulkGenre), "ジャンル");
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawBulkArtist(int[] positions)
        {
            EditorGUILayout.BeginHorizontal();

            _bulkArtist = EditorGUILayout.TextField("チャンネル", _bulkArtist);

            if (GUILayout.Button("入れる", GUILayout.Width(64f)))
            {
                Report(CatalogBulkEdit.SetArtist(_draft.Items, positions, _bulkArtist), "チャンネル");
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawBulkTags(int[] positions)
        {
            EditorGUILayout.BeginHorizontal();

            _bulkTag = EditorGUILayout.TextField("タグ", _bulkTag);

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_bulkTag)))
            {
                if (GUILayout.Button("足す", GUILayout.Width(48f)))
                {
                    Report(CatalogBulkEdit.AddTag(_draft.Items, positions, _bulkTag), "タグを足す");
                }

                if (GUILayout.Button("外す", GUILayout.Width(48f)))
                {
                    Report(CatalogBulkEdit.RemoveTag(_draft.Items, positions, _bulkTag), "タグを外す");
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawBulkRelated(int[] positions)
        {
            EditorGUILayout.Space();

            if (GUILayout.Button("チェックしたものの関連を作り直す", GUILayout.Height(24f)))
            {
                var generator = new RelatedIdGenerator();
                CopyRelatedSettings(generator);

                Report(CatalogBulkEdit.RegenerateRelated(_draft.Items, positions, generator),
                       "関連");
            }

            _showRelatedOptions = EditorGUILayout.Foldout(_showRelatedOptions, "関連の細かい設定", true);
            if (_showRelatedOptions) DrawRelatedOptions();

            EditorGUILayout.LabelField(
                "近さは一覧の全部から探し、書き込むのはチェックしたものだけです。",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawRelatedOptions()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            _related.MaxPerItem = EditorGUILayout.IntSlider("1 件に入れる数", _related.MaxPerItem, 1, 16);
            _related.UseChannel = EditorGUILayout.Toggle("同じチャンネルを見る", _related.UseChannel);
            _related.UseGenre = EditorGUILayout.Toggle("同じジャンルを見る", _related.UseGenre);
            _related.UseTags = EditorGUILayout.Toggle("同じタグを見る", _related.UseTags);

            _related.ChannelScore = EditorGUILayout.IntSlider("重み:チャンネル", _related.ChannelScore, 0, 10);
            _related.GenreScore = EditorGUILayout.IntSlider("重み:ジャンル", _related.GenreScore, 0, 10);
            _related.TagScore = EditorGUILayout.IntSlider("重み:タグ 1 つ", _related.TagScore, 0, 10);

            EditorGUILayout.EndVertical();
        }

        private void Report(int changed, string what)
        {
            _lastImportOk = changed > 0;
            _importMessage = changed > 0
                ? what + " を " + changed + " 件に反映しました。"
                : what + " は 1 件も変わりませんでした(もともと同じでした)。";

            if (changed > 0) MarkDirty();
        }

        private void CopyRelatedSettings(RelatedIdGenerator target)
        {
            target.MaxPerItem = _related.MaxPerItem;
            target.UseChannel = _related.UseChannel;
            target.UseGenre = _related.UseGenre;
            target.UseTags = _related.UseTags;
            target.ChannelScore = _related.ChannelScore;
            target.GenreScore = _related.GenreScore;
            target.TagScore = _related.TagScore;
        }

        // ───────── タブ:取り込む ─────────

        private void DrawImportTab()
        {
            EditorGUILayout.LabelField(
                "① 取得 → ② 選んで追加 → 保存 → ③ VRCUrl へ焼く", EditorStyles.miniLabel);

            ICatalogImporter[] importers = CatalogImporterRegistry.All();

            if (importers.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "取り込み元がまだありません。\n"
                    + "ICatalogImporter を実装したクラスを 1 つ書けば、"
                    + "次のコンパイルでここに出ます。",
                    MessageType.None);
                return;
            }

            var names = new string[importers.Length];
            for (int i = 0; i < importers.Length; i++) names[i] = importers[i].DisplayName;

            _importerIndex = Mathf.Clamp(
                EditorGUILayout.Popup("取り込み元", _importerIndex, names), 0, importers.Length - 1);

            ICatalogImporter importer = importers[_importerIndex];

            EditorGUILayout.LabelField(importer.InputHint, EditorStyles.wordWrappedMiniLabel);
            _importerInput = EditorGUILayout.TextField(_importerInput);

            bool canImport = importer.IsAvailable && importer.CanImport(_importerInput);

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(!canImport))
            {
                // 新着だけ取れる取り込み元なら、そちらを先に(速くて API も食わない)。
                if (importer is IIncrementalCatalogImporter
                    && GUILayout.Button("① 新着だけ取る", GUILayout.Height(24f)))
                {
                    RunImport(importer, true);
                }

                if (GUILayout.Button("① 全部取得する", GUILayout.Height(24f)))
                {
                    RunImport(importer, false);
                }
            }

            EditorGUILayout.EndHorizontal();

            DrawShortsToggle(importer);

            using (new EditorGUI.DisabledScope(_selection.IsEmpty))
            {
                if (GUILayout.Button("取得結果をクリア", EditorStyles.miniButton))
                {
                    ClearImportResult();
                }
            }

            DrawImporterSetup(importer);

            if (_importMessage.Length > 0)
            {
                EditorGUILayout.HelpBox(
                    _importMessage, _lastImportOk ? MessageType.Info : MessageType.None);
            }

            DrawSelection();
        }

        /// <summary>
        /// <b>使う前のひと手間を、その場で終わらせる。</b>Phase7。
        ///
        /// Phase6-5 まで、キーが無いと
        /// <b>「窓の『設定を作る』を押してください」</b>としか出ませんでした。
        /// ところが<b>その窓はもうありません</b>(Phase6-5 で統合したときに消えました)。
        /// ここで<b>手順と入力欄をその場に出す</b>ので、窓を探し回らずに済みます。
        ///
        /// <b>この窓は YouTube を知りません。</b>
        /// 見出しも手順も入力欄の名前も、取り込み元が
        /// <see cref="ICatalogImporterSetup"/> で名乗ったものをそのまま出しているだけです。
        /// </summary>
        private void DrawImporterSetup(ICatalogImporter importer)
        {
            var setup = importer as ICatalogImporterSetup;

            if (setup == null)
            {
                // 設定の口を持たない取り込み元。今までどおり理由だけ出す。
                if (!importer.IsAvailable)
                {
                    EditorGUILayout.HelpBox(importer.UnavailableReason, MessageType.Warning);
                }
                return;
            }

            bool ready = setup.IsConfigured;

            // 済んでいるなら畳んでおく。毎回出ていると邪魔なので。
            _showSetup = EditorGUILayout.Foldout(
                _showSetup || !ready,
                setup.SetupTitle + (ready ? "  (設定済み)" : "  ← まず、ここ"),
                true);

            if (!_showSetup && ready) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (!ready)
            {
                EditorGUILayout.LabelField(
                    "この取り込み元を使うには、下の欄に " + setup.SecretLabel + " が要ります。",
                    EditorStyles.wordWrappedLabel);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("取り方", EditorStyles.miniBoldLabel);

                string[] steps = setup.SetupSteps;
                for (int i = 0; i < steps.Length; i++)
                {
                    EditorGUILayout.LabelField(
                        "  " + (i + 1) + ". " + steps[i], EditorStyles.wordWrappedMiniLabel);
                }

                EditorGUILayout.Space();
            }

            EditorGUI.BeginChangeCheck();

            // 伏せ字にするのは、画面共有や配信に映っても漏れないようにするため。
            string typed = EditorGUILayout.PasswordField(setup.SecretLabel, setup.SecretValue);

            if (EditorGUI.EndChangeCheck())
            {
                setup.SecretValue = typed;
                Repaint();
            }

            EditorGUILayout.BeginHorizontal();

            if (setup.HasStorage)
            {
                EditorGUILayout.LabelField(
                    "保存先: " + setup.StorageLocation, EditorStyles.miniLabel);

                if (GUILayout.Button("開く", EditorStyles.miniButton, GUILayout.Width(48f)))
                {
                    setup.RevealStorage();
                }
            }
            else if (GUILayout.Button("設定ファイルを作る", EditorStyles.miniButton))
            {
                setup.CreateStorage();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                ready
                    ? "このファイルはワールドには入りません。公開リポジトリへは上げないでください。"
                    : "貼るとすぐ使えます。ファイルはワールドには入りません。",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// <b>ショート動画を取り込むかどうか。</b>Phase7-3。
        ///
        /// 取り込みの直前に出します。<b>あとから 1 本ずつ消すのは現実的でない</b>ので、
        /// 押す前に見えていることが大事です。
        /// 設定アセットに書くので、次に開いたときも覚えています。
        ///
        /// この欄はショートという考え方がある取り込み元
        /// (いまは YouTube だけ)にしか出しません。
        /// </summary>
        private void DrawShortsToggle(ICatalogImporter importer)
        {
            var filter = importer as ICatalogImporterFilter;
            if (filter == null) return;

            bool enabled = EditorGUILayout.ToggleLeft(filter.FilterLabel, filter.FilterEnabled);
            if (enabled != filter.FilterEnabled) filter.FilterEnabled = enabled;

            if (!enabled) return;

            string hint = filter.FilterHint;
            if (string.IsNullOrEmpty(hint)) return;

            EditorGUILayout.LabelField("　" + hint, EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>
        /// 取得する。<b>この時点では Catalog にも編集中の一覧にも入れません。</b>
        /// 下の一覧で選んでから「② 追加する」で入ります。
        /// </summary>
        private void RunImport(ICatalogImporter importer, bool onlyNew)
        {
            _selection.Clear();

            // 見張っていなくても「新着だけ」は使えます。
            // 前回の目印が無くても、いま持っているカタログの ID が目印になるためです。
            bool incremental = onlyNew && importer is IIncrementalCatalogImporter;
            CatalogSubscription subscription = _book.Find(importer.DisplayName, _importerInput);

            CatalogImportResult result;

            EditorUtility.DisplayProgressBar("Catalog Builder", "取得しています…", 0.5f);
            try
            {
                result = incremental
                    ? ((IIncrementalCatalogImporter)importer).ImportNew(
                        _importerInput,
                        CatalogImportBoundary.From(subscription, _draft.Items))
                    : importer.Import(_importerInput);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (result == null)
            {
                _lastImportOk = false;
                _importMessage = "取り込み元が結果を返しませんでした。";
                return;
            }

            if (!result.Ok)
            {
                _lastImportOk = false;
                _importMessage = "失敗: " + result.Message;
                return;
            }

            _selection.SetItems(result.Items);

            int existing = _selection.MarkExisting(_draft);

            _lastImportOk = true;
            _importMessage = result.Message;
            if (existing > 0)
            {
                _importMessage += "(うち " + existing + " 件はすでにあります)";
            }

            // 見張り先として覚えておく。次からは「新着だけ取る」が使える。
            RememberSubscription(importer, result);

            Repaint();
        }

        private void RememberSubscription(ICatalogImporter importer, CatalogImportResult result)
        {
            if (string.IsNullOrWhiteSpace(_importerInput)) return;

            // 単発の動画 1 本は見張っても意味がない。
            if (result.SourceKind == CatalogSubscription.KindSingle) return;

            string name = string.IsNullOrWhiteSpace(result.SourceName)
                ? _importerInput.Trim()
                : result.SourceName;

            _book.Register(importer.DisplayName, _importerInput, name, result.SourceKind);
            MarkDirty();
        }

        private void ClearImportResult()
        {
            _selection.Clear();
            _importMessage = "";
            _lastImportOk = false;

            GUI.FocusControl(null);
            Repaint();
        }

        // ───────── 取り込んだものを選ぶ ─────────

        private void DrawSelection()
        {
            if (_selection.IsEmpty) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "取得結果 " + _selection.Count + " 件 / 選択 " + _selection.SelectedCount + " 件",
                EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全選択", EditorStyles.miniButtonLeft)) _selection.SelectAll();
            if (GUILayout.Button("全解除", EditorStyles.miniButtonMid)) _selection.SelectNone();
            if (GUILayout.Button("反転", EditorStyles.miniButtonMid)) _selection.InvertSelection();

            using (new EditorGUI.DisabledScope(_selection.ExistingCount == 0))
            {
                if (GUILayout.Button("まだ無いものだけ", EditorStyles.miniButtonRight))
                {
                    _selection.SelectOnlyNew();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_selection.Grouping.IsMeaningful)
            {
                EditorGUILayout.BeginHorizontal();

                _groupSelection = GUILayout.Toggle(
                    _groupSelection, "まとめる", EditorStyles.miniButton, GUILayout.Width(60f));

                if (_groupSelection)
                {
                    int mode = EditorGUILayout.Popup(_selection.Grouping.Mode, GroupLabels);
                    if (mode != _selection.Grouping.Mode) _selection.Regroup(mode);

                    if (GUILayout.Button("開く", EditorStyles.miniButtonLeft, GUILayout.Width(40f)))
                    {
                        _selection.Grouping.ExpandAll();
                    }
                    if (GUILayout.Button("たたむ", EditorStyles.miniButtonRight, GUILayout.Width(48f)))
                    {
                        _selection.Grouping.CollapseAll();
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            _selectionScroll = EditorGUILayout.BeginScrollView(
                _selectionScroll, GUILayout.Height(200f));

            if (_groupSelection && _selection.Grouping.IsMeaningful) DrawSelectionGroups();
            else
            {
                for (int i = 0; i < _selection.Count; i++) DrawSelectionRow(i);
            }

            EditorGUILayout.EndScrollView();

            DrawSelectionApply();
        }

        private void DrawSelectionGroups()
        {
            CatalogItemGrouping grouping = _selection.Grouping;

            for (int group = 0; group < grouping.GroupCount; group++)
            {
                EditorGUILayout.BeginHorizontal();

                bool all = _selection.IsGroupFullySelected(group);
                if (EditorGUILayout.Toggle(all, GUILayout.Width(18f)) != all)
                {
                    _selection.SetGroupSelected(group, !all);
                }

                int existing = _selection.GroupExistingCount(group);
                string label = grouping.GetName(group)
                               + "  (" + _selection.GroupSelectedCount(group)
                               + " / " + grouping.GetCount(group) + " 件"
                               + (existing > 0 ? "・うち " + existing + " 件はあり" : "")
                               + ")";

                bool expanded = EditorGUILayout.Foldout(grouping.IsExpanded(group), label, true);
                grouping.SetExpanded(group, expanded);

                EditorGUILayout.EndHorizontal();

                if (!expanded) continue;

                EditorGUI.indentLevel++;
                int[] members = grouping.GetIndices(group);
                for (int i = 0; i < members.Length; i++) DrawSelectionRow(members[i]);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawSelectionRow(int index)
        {
            CatalogDraftItem item = _selection.GetAt(index);
            if (item == null) return;

            EditorGUILayout.BeginHorizontal();

            bool wanted = EditorGUILayout.Toggle(_selection.IsSelected(index), GUILayout.Width(18f));
            _selection.SetSelected(index, wanted);

            DrawThumbnail(item, 46f);

            string label = item.Title;
            if (_selection.IsExisting(index)) label = "【あり】" + label;
            if (!item.IsComplete) label = "⚠ " + label;

            EditorGUILayout.LabelField(label);
            EditorGUILayout.LabelField(FormatDuration(item.DurationSeconds), GUILayout.Width(52f));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSelectionApply()
        {
            int picked = Array.IndexOf(MergeModes, _mergeMode);
            if (picked < 0) picked = 0;

            picked = EditorGUILayout.Popup("混ぜ方", picked, MergeLabels);
            _mergeMode = MergeModes[picked];

            _autoRelated = EditorGUILayout.ToggleLeft(
                "追加したあと、関連(おすすめ)を自動で作る", _autoRelated);

            int selected = _selection.SelectedCount;
            int fresh = _draft.CountNew(_selection.SelectedItems());

            using (new EditorGUI.DisabledScope(selected == 0))
            {
                string caption = "② 選んだ " + selected + " 件を追加する";
                if (_mergeMode == CatalogDraft.MergeSkip && fresh != selected)
                {
                    caption += "(増えるのは " + fresh + " 件)";
                }

                if (GUILayout.Button(caption, GUILayout.Height(26f))) ApplySelection();
            }
        }

        private static string FormatDuration(int seconds)
        {
            if (seconds <= 0) return "--:--";

            int minutes = seconds / 60;
            int rest = seconds % 60;
            return minutes + ":" + (rest < 10 ? "0" + rest : "" + rest);
        }

        private void ApplySelection()
        {
            CatalogDraftItem[] chosen = _selection.SelectedItems();
            int before = _draft.Count;
            int changed = _draft.Merge(chosen, _mergeMode);

            _importMessage = changed + " 件を入れました(一覧は "
                             + before + " → " + _draft.Count + " 件)。";

            if (_autoRelated)
            {
                var generator = new RelatedIdGenerator();
                CopyRelatedSettings(generator);

                int linked = generator.Generate(_draft.Items);
                if (linked > 0) _importMessage += " 関連を " + linked + " 件ぶん作りました。";
            }

            // 見張り先に「どこまで取ったか」を書き残す。
            // これが無いと、次も全部取り直すことになる。
            MarkSubscriptionFetched(chosen, changed);

            _importMessage += " 保存するとカタログに入ります。";

            _selection.MarkExisting(_draft);
            _lastImportOk = true;

            if (_selected < 0 && _draft.Count > 0) _selected = 0;
            MarkDirty();
        }

        private void MarkSubscriptionFetched(CatalogDraftItem[] added, int changed)
        {
            ICatalogImporter[] importers = CatalogImporterRegistry.All();
            if (importers.Length == 0) return;

            ICatalogImporter importer = importers[Mathf.Clamp(_importerIndex, 0, importers.Length - 1)];
            CatalogSubscription subscription = _book.Find(importer.DisplayName, _importerInput);
            if (subscription == null) return;

            // 取り込み元は新しい順に返すので、先頭がいちばん新しい。
            string newest = added != null && added.Length > 0 ? added[0].Id : "";

            subscription.MarkFetched(newest, changed, DateTime.UtcNow.ToString("o"));
        }

        // ───────── タブ:見張っているチャンネル(Phase6-5)─────────

        private void DrawSubscriptionTab()
        {
            EditorGUILayout.LabelField("見張っている取り込み元", EditorStyles.boldLabel);

            if (_book.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "まだありません。\n"
                    + "「取り込む」タブでチャンネルや再生リストを 1 度取り込むと、"
                    + "自動でここに並びます。\n"
                    + "次からは「新着だけ取る」で、増えたぶんだけを取れます。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                "覚えているのは Catalog.asset の横の JSON です(ワールドには入りません)。",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();

            int removeAt = -1;
            for (int i = 0; i < _book.Count; i++)
            {
                if (DrawSubscriptionRow(i)) removeAt = i;
            }

            if (removeAt >= 0)
            {
                _book.RemoveAt(removeAt);
                MarkDirty();
            }
        }

        /// <summary>1 行ぶん。消してほしければ true。</summary>
        private bool DrawSubscriptionRow(int index)
        {
            CatalogSubscription subscription = _book.GetAt(index);
            if (subscription == null) return false;

            bool remove = false;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            bool enabled = EditorGUILayout.Toggle(subscription.Enabled, GUILayout.Width(18f));
            if (enabled != subscription.Enabled)
            {
                subscription.Enabled = enabled;
                MarkDirty();
            }

            EditorGUILayout.LabelField(subscription.DisplayName, EditorStyles.boldLabel);

            EditorGUILayout.LabelField(
                subscription.Source + " / " + subscription.KindLabel,
                EditorStyles.miniLabel, GUILayout.Width(140f));

            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24f))) remove = true;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "最後に取得: " + subscription.FormatLastFetched()
                + "  /  これまで " + subscription.ImportedCount + " 件"
                + (subscription.LastNewCount > 0
                       ? "  /  前回の新着 " + subscription.LastNewCount + " 件"
                       : ""),
                EditorStyles.miniLabel);

            EditorGUILayout.SelectableLabel(subscription.Input, EditorStyles.miniLabel,
                                            GUILayout.Height(16f));

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("新着だけ取る", EditorStyles.miniButtonLeft))
            {
                OpenImportFor(subscription, true);
            }

            if (GUILayout.Button("全部取り直す", EditorStyles.miniButtonRight))
            {
                OpenImportFor(subscription, false);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            return remove;
        }

        /// <summary>その取り込み元を「取り込む」タブへ載せて、そのまま取りに行く。</summary>
        private void OpenImportFor(CatalogSubscription subscription, bool onlyNew)
        {
            ICatalogImporter[] importers = CatalogImporterRegistry.All();

            for (int i = 0; i < importers.Length; i++)
            {
                if (importers[i].DisplayName != subscription.Source) continue;

                _importerIndex = i;
                _importerInput = subscription.Input;
                _tab = TabImport;

                RunImport(importers[i], onlyNew);
                return;
            }

            _lastImportOk = false;
            _importMessage = "「" + subscription.Source + "」が見つかりません"
                             + "(取り込み元のクラスが消えていませんか)。";
            _tab = TabImport;
        }

        // ───────── 下 ─────────

        private void DrawFooter()
        {
            string[] duplicates = _draft.FindDuplicateIds();
            if (duplicates.Length > 0)
            {
                EditorGUILayout.HelpBox(
                    "ID が重なっています: " + string.Join(", ", duplicates) + "\n"
                    + "重なったまま保存すると、再生側が別のものを指します。",
                    MessageType.Error);
            }

            DrawThumbnailSetting();
            DrawApiDataPanel();

            EditorGUILayout.BeginHorizontal();

            int targets = CatalogUrlTableBridge.IsAvailable
                ? CatalogUrlTableBridge.CountInScene()
                : 0;

            using (new EditorGUI.DisabledScope(_dirty || targets == 0))
            {
                if (GUILayout.Button(
                        "③ VRCUrl へ焼く(シーンの Catalog " + targets + " 個)",
                        GUILayout.Height(26f)))
                {
                    Bake();
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "保存すると、完成している " + _draft.CompleteCount + " 件だけが書かれます。"
                + (targets == 0 && CatalogUrlTableBridge.IsAvailable
                       ? "  シーンに SmartMediaPlayer を置くと ③ が押せます。"
                       : ""),
                EditorStyles.miniLabel);
        }

        /// <summary>
        /// <b>絵の大きさ。</b>Phase7。
        ///
        /// VR の見え方から逆算した目安を出します。2 m 先の壁パネルでは、
        /// 一覧の絵は <b>120 px 程度しか画面に映りません</b>ので、
        /// 128 px でもほぼ足ります。大きくして効くのは<b>再生中の大きな絵</b>だけです。
        /// </summary>
        private void DrawThumbnailSetting()
        {
            EditorGUILayout.BeginHorizontal();

            int picked = System.Array.IndexOf(CatalogThumbnailBaker.Sizes, _thumbnailSize);
            if (picked < 0) picked = 0;

            picked = EditorGUILayout.Popup("絵の大きさ", picked, CatalogThumbnailBaker.SizeLabels);
            _thumbnailSize = CatalogThumbnailBaker.Sizes[picked];

            if (_thumbnailSize <= 0)
            {
                EditorGUILayout.LabelField(
                    "ワールドに +0 MB", EditorStyles.miniLabel, GUILayout.Width(140f));

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField(
                    "一覧はジャンル色 + 頭文字で表示します。",
                    EditorStyles.miniLabel);

                DrawOrderKindSetting();
                return;
            }

            float megabytes = CatalogThumbnailBaker.EstimateMegabytes(
                _draft.CompleteCount, _thumbnailSize);

            EditorGUILayout.LabelField(
                "ワールドに +" + megabytes.ToString("0.0") + " MB",
                EditorStyles.miniLabel, GUILayout.Width(140f));

            EditorGUILayout.EndHorizontal();

            DrawOrderKindSetting();

            // ── 焼くことを選んだ人にだけ、はっきり伝える。
            EditorGUILayout.HelpBox(
                "YouTube のサムネイルをワールドに焼き込みます。\n"
                + "焼いた絵はワールドと一緒に配布されるため、"
                + "著作物の再配布になります。公開ワールドでは「焼かない」を"
                + "選ぶことを強く勧めます。",
                MessageType.Warning);
        }

        /// <summary>
        /// <b>この並びは何順か。</b>Phase8。
        ///
        /// 再生数を焼き込むのをやめた代わりに、<b>並びの意味だけ</b>を渡します。
        /// おすすめは「人気順」のときだけ、位置を人気度として扱います。
        /// <b>YouTube の数字はひとつも保存しません。</b>
        /// </summary>
        private void DrawOrderKindSetting()
        {
            _catalogOrderKind = EditorGUILayout.Popup(
                "並びの意味", _catalogOrderKind, OrderKindLabels);

            if (_catalogOrderKind == 1)
            {
                EditorGUILayout.LabelField(
                    "前にある曲ほど人気として、おすすめに使います。",
                    EditorStyles.miniLabel);
            }
            else if (_catalogOrderKind == 0)
            {
                EditorGUILayout.LabelField(
                    "人気順で取り込んだなら「人気順」を選ぶと、おすすめが賢くなります。",
                    EditorStyles.miniLabel);
            }
        }

        /// <summary>
        /// <b>API から取った原本の面倒を見る。</b>Phase8。
        ///
        /// YouTube API の規約は、API から取ったデータを<b>原則 30 日</b>までしか
        /// 持たせません。ここは<b>Sidecar の JSON に置いてある原本</b>の話で、
        /// <b>ワールドには入っていません</b>(ワールドに入るのは、作者が確認した側)。
        ///
        /// 期限が来たら、やることは 2 つのどちらかです。
        /// <list type="bullet">
        /// <item><b>取り込み直す</b> …… 原本を新しくする(曲のデータはそのまま)</item>
        /// <item><b>消す</b> …… 原本だけ捨てる。<b>曲は消えません</b></item>
        /// </list>
        /// </summary>
        private void DrawApiDataPanel()
        {
            if (_draft == null || _draft.Count == 0) return;

            System.DateTime now = System.DateTime.UtcNow;

            int withApi = 0;
            int expired = 0;
            int soon = 0;
            int oldest = -1;

            for (int i = 0; i < _draft.Count; i++)
            {
                CatalogDraftItem item = _draft.GetAt(i);
                if (item == null || !item.HasApiData) continue;

                withApi++;

                if (CatalogApiDataPolicy.IsExpired(item.ApiFetchedAtUtc, now)) expired++;
                else if (CatalogApiDataPolicy.IsExpiringSoon(item.ApiFetchedAtUtc, now)) soon++;

                int age = CatalogApiDataPolicy.AgeInDays(item.ApiFetchedAtUtc, now);
                if (age > oldest) oldest = age;
            }

            if (withApi == 0) return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                "API から取ったデータ", EditorStyles.miniBoldLabel);

            string summary = withApi + " 件が API 由来です";
            if (oldest >= 0) summary += "(いちばん古いもので " + oldest + " 日前)";

            if (expired > 0)
            {
                EditorGUILayout.HelpBox(
                    summary + "。\n"
                    + expired + " 件が 30 日を過ぎています。"
                    + "取り込み直すか、原本を消してください。\n"
                    + "※ 原本を消しても、曲・曲名・タグ(あなたが編集した側)は残ります。",
                    MessageType.Warning);
            }
            else if (soon > 0)
            {
                EditorGUILayout.HelpBox(
                    summary + "。\n" + soon + " 件がまもなく 30 日になります。",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField(summary + "。", EditorStyles.miniLabel);
            }

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(expired == 0))
            {
                if (GUILayout.Button("期限切れの原本を消す (" + expired + ")",
                        EditorStyles.miniButtonLeft))
                {
                    ForgetApiData(true);
                }
            }

            if (GUILayout.Button("原本を全部消す (" + withApi + ")", EditorStyles.miniButtonRight))
            {
                ForgetApiData(false);
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// API 由来の原本を捨てる。<b>曲そのものは消しません。</b>
        /// </summary>
        /// <param name="expiredOnly">期限切れのものだけ消すか。</param>
        private void ForgetApiData(bool expiredOnly)
        {
            System.DateTime now = System.DateTime.UtcNow;
            int forgotten = 0;

            for (int i = 0; i < _draft.Count; i++)
            {
                CatalogDraftItem item = _draft.GetAt(i);
                if (item == null || !item.HasApiData) continue;

                if (expiredOnly
                    && !CatalogApiDataPolicy.IsExpired(item.ApiFetchedAtUtc, now)) continue;

                item.ApiTitle = "";
                item.ApiChannel = "";
                item.ApiTags = new string[0];
                item.ApiFetchedAtUtc = "";
                forgotten++;
            }

            _importMessage = forgotten + " 件の API 由来データを消しました"
                             + "(曲そのものは残っています)。保存すると確定します。";
            _lastImportOk = true;

            Repaint();
        }

        private void Bake()
        {
            // 絵の URL を持っているのは編集中の中身だけ。焼く直前に渡す。
            CatalogUrlTableBridge.ThumbnailSize = _thumbnailSize;
            CatalogUrlTableBridge.ThumbnailSource = _draft.Items;
            CatalogUrlTableBridge.OrderKind = _catalogOrderKind;

            CatalogUrlTableBridge.BakeReport report = CatalogUrlTableBridge.BakeIntoScene(_asset);

            _importMessage = report.Message;
            _lastImportOk = report.Ok;

            if (report.Ok) Debug.Log("[CatalogBuilder] " + report.Message);
            else Debug.LogWarning("[CatalogBuilder] " + report.Message);

            RefreshSyncReport(true);
            EditorUtility.DisplayDialog("Catalog Builder", report.Message, "OK");
        }

        // ───────── 状態 ─────────

        private void ReloadFromAsset()
        {
            _draft = new CatalogDraft();
            _selected = -1;
            _dirty = false;
            _importMessage = "";
            _selection.Clear();
            _filter.Clear();
            _draftGrouping.Clear();
            _checkedIds.Clear();
            _syncReport = null;
            _nextSyncCheck = 0;

            if (_asset == null)
            {
                _book.Clear();
                return;
            }

            CatalogDraftIO.Load(_asset, _draft, _book);
            if (_draft.Count > 0) _selected = 0;
        }

        private void SaveToAsset()
        {
            int written = CatalogDraftIO.Save(_asset, _draft, _book);
            if (written < 0) return;

            _dirty = false;
            _lastImportOk = true;
            _importMessage = written + " 件を保存しました。";

            // 保存した直後こそ焼き忘れが起きやすい。すぐ調べ直す。
            RefreshSyncReport(true);
        }

        private void MarkDirty()
        {
            _dirty = true;
            Repaint();
        }

        private bool ConfirmDiscard()
        {
            if (!_dirty) return true;

            return EditorUtility.DisplayDialog(
                "Catalog Builder",
                "保存していない変更があります。捨ててよいですか?",
                "捨てる", "やめる");
        }
    }
}
#endif
