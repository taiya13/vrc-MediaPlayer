#if UNITY_EDITOR
using System;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Assets;
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.CatalogBuilder.EditorTools
{
    /// <summary>
    /// <b>Catalog Builder。</b>Phase6-1。<c>Tools > Smart Media Platform > Catalog Builder</c>。
    ///
    /// <b>この窓は判断を持ちません。</b>持っているのは
    /// <list type="bullet">
    /// <item>いま何を編集しているか(<see cref="_asset"/> / <see cref="_draft"/>)</item>
    /// <item>どこを開いているか(スクロール位置・折りたたみ)</item>
    /// </list>
    /// だけで、<b>足す・消す・混ぜる・絞る・まとめる・関連を作る</b>はすべて
    /// <see cref="CatalogDraft"/> / <see cref="CatalogItemFilter"/> /
    /// <see cref="CatalogItemGrouping"/> / <see cref="RelatedIdGenerator"/> /
    /// <see cref="CatalogDraftIO"/> の仕事です。
    /// おかげで判断の部分は EditMode で検証できます
    /// (Phase5 の「UI はロジックを持たない」と同じ考え方)。
    ///
    /// <b>取り込み元を足しても、この窓は変わりません。</b>
    /// 絞り込みもまとめ方も関連も、見ているのは <see cref="CatalogDraftItem"/> だけで、
    /// YouTube 固有の言葉はどこにも出てきません。
    /// JSON / CSV の取り込みを足しても<b>直すのは <see cref="ICatalogImporter"/> の実装 1 つだけ</b>です。
    ///
    /// <b>MediaPlayer 本体には触りません。</b>書き込み先は
    /// <see cref="MediaCatalogAsset"/> 1 つだけです。
    /// </summary>
    public sealed class CatalogBuilderWindow : EditorWindow
    {
        private const string MenuPath = "Tools/Smart Media Platform/Catalog Builder";

        private MediaCatalogAsset _asset;
        private CatalogDraft _draft = new CatalogDraft();

        private Vector2 _listScroll;
        private int _selected = -1;
        private bool _dirty;

        // 取り込み
        private int _importerIndex;
        private string _importerInput = "";
        private int _mergeMode = CatalogDraft.MergeSkip;
        private string _importMessage = "";

        // Phase6-3: 取り込んだものを選んでから入れる
        private readonly CatalogImportSelection _selection = new CatalogImportSelection();
        private Vector2 _selectionScroll;
        private bool _lastImportOk;

        // Phase6-4: 絞り込み / まとめ / 絵 / 関連
        private readonly CatalogItemFilter _filter = new CatalogItemFilter();
        private readonly CatalogItemGrouping _draftGrouping = new CatalogItemGrouping();
        private readonly RelatedIdGenerator _related = new RelatedIdGenerator();

        private bool _showThumbnails = true;
        private bool _groupDraft = true;
        private bool _groupSelection = true;
        private bool _autoRelated = true;
        private bool _showRelatedOptions;
        private int[] _visible = new int[0];

        /// <summary>混ぜ方のプルダウン。並びと中身をここ 1 か所で結び付ける。</summary>
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

        [MenuItem(MenuPath, false, -90)]
        public static void Open()
        {
            var window = GetWindow<CatalogBuilderWindow>("Catalog Builder");
            window.minSize = new Vector2(820f, 560f);
            window.Show();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_asset == null)
            {
                DrawNoAsset();
                return;
            }

            DrawFilterBar();

            EditorGUILayout.BeginHorizontal();
            DrawList();
            DrawInspector();
            EditorGUILayout.EndHorizontal();

            DrawImportBar();
            DrawSelection();
            DrawRelatedBar();
            DrawBakeBar();
            DrawFooter();
        }

        // ───────── 上のバー ─────────

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            var picked = (MediaCatalogAsset)EditorGUILayout.ObjectField(
                _asset, typeof(MediaCatalogAsset), false, GUILayout.Width(240f));

            if (picked != _asset)
            {
                if (!ConfirmDiscard()) return;
                _asset = picked;
                ReloadFromAsset();
            }

            if (GUILayout.Button("新規", EditorStyles.toolbarButton, GUILayout.Width(56f)))
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
                if (GUILayout.Button("読み直す", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                {
                    if (ConfirmDiscard()) ReloadFromAsset();
                }

                if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(56f)))
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

        // ───────── 絞り込み(Phase6-4)─────────

        /// <summary>
        /// 見出し・チャンネル・ジャンル・タグで絞る。
        /// <b>絞っても編集先はずれません</b> — 一覧が持っているのは元の位置だからです。
        /// </summary>
        private void DrawFilterBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("絞り込み", EditorStyles.miniBoldLabel, GUILayout.Width(52f));

            _filter.Query = EditorGUILayout.TextField(
                _filter.Query, EditorStyles.toolbarTextField, GUILayout.Width(180f));

            _filter.Channel = DrawPickList(
                "チャンネル", _filter.Channel, CatalogItemFilter.CollectChannels(_draft.Items), 130f);

            _filter.Genre = DrawPickList(
                "ジャンル", _filter.Genre, CatalogItemFilter.CollectGenres(_draft.Items), 110f);

            _filter.Tag = DrawPickList(
                "タグ", _filter.Tag, CatalogItemFilter.CollectTags(_draft.Items), 110f);

            _filter.OnlyIncomplete = GUILayout.Toggle(
                _filter.OnlyIncomplete, "作りかけだけ", EditorStyles.toolbarButton,
                GUILayout.Width(88f));

            using (new EditorGUI.DisabledScope(_filter.IsEmpty))
            {
                if (GUILayout.Button("解除", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                {
                    _filter.Clear();
                    GUI.FocusControl(null);
                }
            }

            GUILayout.FlexibleSpace();

            _showThumbnails = GUILayout.Toggle(
                _showThumbnails, "絵を出す", EditorStyles.toolbarButton, GUILayout.Width(66f));

            _groupDraft = GUILayout.Toggle(
                _groupDraft, "まとめる", EditorStyles.toolbarButton, GUILayout.Width(66f));

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 「(すべて)」＋出てきた値のプルダウン。
        /// 手で打たせないのは、打ち間違いだと 1 件も出ずに理由が分からないためです。
        /// </summary>
        private static string DrawPickList(string label, string current, string[] values, float width)
        {
            var names = new string[values.Length + 1];
            names[0] = label + ": すべて";

            int selected = 0;
            for (int i = 0; i < values.Length; i++)
            {
                names[i + 1] = values[i];
                if (string.Equals(values[i], current, System.StringComparison.OrdinalIgnoreCase))
                {
                    selected = i + 1;
                }
            }

            int picked = EditorGUILayout.Popup(
                selected, names, EditorStyles.toolbarPopup, GUILayout.Width(width));

            return picked <= 0 ? "" : names[picked];
        }

        // ───────── 左:一覧 ─────────

        private void DrawList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(360f));

            _visible = _filter.Apply(_draft.Items);

            string heading = _draft.Count + " 件 / 完成 " + _draft.CompleteCount + " 件";
            if (!_filter.IsEmpty) heading = _visible.Length + " 件を表示中 (" + heading + ")";

            EditorGUILayout.LabelField(heading, EditorStyles.boldLabel);

            if (!_filter.IsEmpty)
            {
                EditorGUILayout.LabelField(_filter.Describe(), EditorStyles.miniLabel);
            }

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            if (_visible.Length == 0)
            {
                EditorGUILayout.LabelField(
                    _draft.Count == 0 ? "まだ 1 件もありません。" : "絞り込みに合うものがありません。",
                    EditorStyles.miniLabel);
            }
            else if (_groupDraft)
            {
                DrawGroupedList();
            }
            else
            {
                for (int i = 0; i < _visible.Length; i++) DrawListRow(_visible[i]);
            }

            EditorGUILayout.EndScrollView();

            DrawListButtons();
            EditorGUILayout.EndVertical();
        }

        /// <summary>まとまりごとに並べる。絞り込みで 0 件になったまとまりは出しません。</summary>
        private void DrawGroupedList()
        {
            _draftGrouping.Build(_draft.Items);

            for (int group = 0; group < _draftGrouping.GroupCount; group++)
            {
                int[] members = _draftGrouping.GetIndices(group);

                int shown = 0;
                for (int i = 0; i < members.Length; i++)
                {
                    if (System.Array.IndexOf(_visible, members[i]) >= 0) shown++;
                }
                if (shown == 0) continue;

                bool expanded = EditorGUILayout.Foldout(
                    _draftGrouping.IsExpanded(group),
                    _draftGrouping.GetName(group) + "  (" + shown + " 件)",
                    true);

                _draftGrouping.SetExpanded(group, expanded);
                if (!expanded) continue;

                EditorGUI.indentLevel++;
                for (int i = 0; i < members.Length; i++)
                {
                    if (System.Array.IndexOf(_visible, members[i]) < 0) continue;
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

            DrawThumbnail(item, 46f);

            bool wasSelected = index == _selected;
            string label = (index + 1) + ". " + (item.IsComplete ? "" : "⚠ ") + item;

            if (GUILayout.Toggle(wasSelected, label, EditorStyles.miniButton) != wasSelected)
            {
                _selected = index;
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 絵を 1 枚。まだ取れていなければ<b>同じ大きさの枠だけ</b>出します
        /// (取れた瞬間に行の高さが変わって、押す場所がずれるのを避けるため)。
        /// </summary>
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

                // 並べ替えは絞り込み中でも「元の並び」に対して効く。
                // 見えている隣ではなく本当の隣と入れ替わるので、絞り込み中は止める。
                using (new EditorGUI.DisabledScope(!_filter.IsEmpty))
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
        }

        // ───────── 右:1 件の編集 ─────────

        private void DrawInspector()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            CatalogDraftItem item = _draft.GetAt(_selected);
            if (item == null)
            {
                EditorGUILayout.LabelField("左の一覧から選んでください。");
                EditorGUILayout.EndVertical();
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

            EditorGUILayout.Space();
            item.Tags = DrawStringList("タグ", item.Tags);

            DrawRelatedEditor(item);

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

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 関連 ID。<b>手で足す / 消すはそのまま残してあります</b>。
        /// 自動で作るのは「この 1 件だけ作り直す」ボタンからで、
        /// <b>押さないかぎり書き換わりません</b>。
        /// </summary>
        private void DrawRelatedEditor(CatalogDraftItem item)
        {
            item.RelatedIds = DrawStringList("関連 ID(おすすめに出る)", item.RelatedIds);

            if (!GUILayout.Button("この 1 件の関連を作り直す", EditorStyles.miniButton)) return;

            var single = new RelatedIdGenerator();
            CopyRelatedSettings(single);

            item.RelatedIds = single.Suggest(_draft.Items, _selected);
            MarkDirty();
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

        // ───────── 取り込み ─────────

        private void DrawImportBar()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("取り込み", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "① 取得 → ② 選んで追加 → 保存 → ③ VRCUrl へ焼く",
                EditorStyles.miniLabel);

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

            _importerIndex = EditorGUILayout.Popup("取り込み元", _importerIndex, names);
            _importerIndex = Mathf.Clamp(_importerIndex, 0, importers.Length - 1);

            ICatalogImporter importer = importers[_importerIndex];

            EditorGUILayout.LabelField(importer.InputHint, EditorStyles.wordWrappedMiniLabel);
            _importerInput = EditorGUILayout.TextField(_importerInput);

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(
                       !importer.IsAvailable || !importer.CanImport(_importerInput)))
            {
                if (GUILayout.Button("① 取得する", GUILayout.Height(24f)))
                {
                    RunImport(importer);
                }
            }

            // 取り込み直す前に前の結果を消せるようにする。
            // 残ったまま次を取ると、どちらの結果を見ているのか分からなくなる。
            using (new EditorGUI.DisabledScope(_selection.IsEmpty))
            {
                if (GUILayout.Button("取得結果をクリア", GUILayout.Height(24f), GUILayout.Width(140f)))
                {
                    ClearImportResult();
                }
            }

            EditorGUILayout.EndHorizontal();

            if (!importer.IsAvailable)
            {
                EditorGUILayout.HelpBox(importer.UnavailableReason, MessageType.Warning);
            }

            if (_importMessage.Length > 0)
            {
                EditorGUILayout.HelpBox(_importMessage, _lastImportOk
                                                            ? MessageType.Info
                                                            : MessageType.None);
            }
        }

        /// <summary>
        /// 取得する。<b>この時点では Catalog にも編集中の一覧にも入れません。</b>
        /// 下の一覧で選んでから「② 追加する」で入ります。
        /// </summary>
        private void RunImport(ICatalogImporter importer)
        {
            _selection.Clear();

            CatalogImportResult result;
            EditorUtility.DisplayProgressBar("Catalog Builder", "取得しています…", 0.5f);
            try
            {
                result = importer.Import(_importerInput);
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

            // すでに入っているものへ印を付ける。
            // 同じチャンネルを 2 回取り込むのはよくあることで、
            // どれが新しいのかが分からないと選びようがない。
            int existing = _selection.MarkExisting(_draft);

            _lastImportOk = true;
            _importMessage = result.Message;
            if (existing > 0)
            {
                _importMessage += "(うち " + existing + " 件はすでにあります。"
                                  + "「すでにあるものは飛ばす」なら二重になりません)";
            }

            Repaint();
        }

        private void ClearImportResult()
        {
            _selection.Clear();
            _importMessage = "";
            _lastImportOk = false;

            // 絵は取り込み結果と一緒に捨てる。編集中の一覧のぶんは次に描くとき取り直す。
            CatalogThumbnailCache.Clear();

            GUI.FocusControl(null);
            Repaint();
        }

        // ───────── 取り込んだものを選ぶ(Phase6-3 / まとめは Phase6-4)─────────

        private void DrawSelection()
        {
            if (_selection.IsEmpty) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "取得結果 " + _selection.Count + " 件 / 選択 " + _selection.SelectedCount + " 件",
                EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全選択")) _selection.SelectAll();
            if (GUILayout.Button("全解除")) _selection.SelectNone();
            if (GUILayout.Button("反転")) _selection.InvertSelection();

            using (new EditorGUI.DisabledScope(_selection.ExistingCount == 0))
            {
                if (GUILayout.Button("まだ無いものだけ")) _selection.SelectOnlyNew();
            }

            _groupSelection = GUILayout.Toggle(
                _groupSelection, "まとめる", EditorStyles.miniButton, GUILayout.Width(72f));

            EditorGUILayout.EndHorizontal();

            if (_groupSelection && _selection.Grouping.IsMeaningful)
            {
                EditorGUILayout.BeginHorizontal();

                int mode = EditorGUILayout.Popup(
                    "まとめ方", _selection.Grouping.Mode, GroupLabels);

                if (mode != _selection.Grouping.Mode) _selection.Regroup(mode);

                if (GUILayout.Button("全部開く", GUILayout.Width(80f)))
                {
                    _selection.Grouping.ExpandAll();
                }
                if (GUILayout.Button("全部たたむ", GUILayout.Width(88f)))
                {
                    _selection.Grouping.CollapseAll();
                }

                EditorGUILayout.EndHorizontal();
            }

            _selectionScroll = EditorGUILayout.BeginScrollView(
                _selectionScroll, GUILayout.Height(220f));

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

                // まとまりごと入り / 切りできるようにする。
                // 3 チャンネルぶん取り込んで 1 つだけ要る、が一番よくある。
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

            EditorGUILayout.LabelField(item.Artist, GUILayout.Width(130f));
            EditorGUILayout.LabelField(FormatDuration(item.DurationSeconds), GUILayout.Width(56f));

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
                int linked = GenerateRelated();
                if (linked > 0) _importMessage += " 関連を " + linked + " 件ぶん作りました。";
            }

            _importMessage += " 保存するとカタログに入ります。";

            _selection.MarkExisting(_draft);
            _lastImportOk = true;

            if (_selected < 0 && _draft.Count > 0) _selected = 0;
            MarkDirty();
        }

        // ───────── 関連(Phase6-4)─────────

        private void DrawRelatedBar()
        {
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("関連(ワールドの「おすすめ」)", EditorStyles.boldLabel);

            _showRelatedOptions = GUILayout.Toggle(
                _showRelatedOptions, "細かい設定", EditorStyles.miniButton, GUILayout.Width(80f));

            EditorGUILayout.EndHorizontal();

            if (_showRelatedOptions) DrawRelatedOptions();

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(_draft.Count == 0))
            {
                if (GUILayout.Button("空いているものだけ作る"))
                {
                    _related.Overwrite = false;
                    RunRelated();
                }

                if (GUILayout.Button("全部作り直す"))
                {
                    if (EditorUtility.DisplayDialog(
                            "Catalog Builder",
                            "手で書いた関連も消えて、全部作り直します。よいですか?",
                            "作り直す", "やめる"))
                    {
                        _related.Overwrite = true;
                        RunRelated();
                        _related.Overwrite = false;
                    }
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "同じチャンネル / 同じジャンル / 同じタグ が多いものほど先に並びます。"
                + "手で足したものは「空いているものだけ」では触りません。",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawRelatedOptions()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            _related.MaxPerItem = EditorGUILayout.IntSlider("1 件に入れる数", _related.MaxPerItem, 1, 16);

            _related.UseChannel = EditorGUILayout.Toggle("同じチャンネルを見る", _related.UseChannel);
            _related.UseGenre = EditorGUILayout.Toggle("同じジャンルを見る", _related.UseGenre);
            _related.UseTags = EditorGUILayout.Toggle("同じタグを見る", _related.UseTags);

            EditorGUILayout.LabelField("重み", EditorStyles.miniBoldLabel);
            _related.ChannelScore = EditorGUILayout.IntSlider("チャンネル", _related.ChannelScore, 0, 10);
            _related.GenreScore = EditorGUILayout.IntSlider("ジャンル", _related.GenreScore, 0, 10);
            _related.TagScore = EditorGUILayout.IntSlider("タグ 1 つ", _related.TagScore, 0, 10);

            EditorGUILayout.EndVertical();
        }

        private void RunRelated()
        {
            int changed = GenerateRelated();

            _importMessage = changed > 0
                ? changed + " 件の関連を作りました。保存するとカタログに入ります。"
                : "書き換えるものがありませんでした。";

            _lastImportOk = changed > 0;
            if (changed > 0) MarkDirty();
        }

        private int GenerateRelated()
        {
            var generator = new RelatedIdGenerator();
            CopyRelatedSettings(generator);
            generator.Overwrite = _related.Overwrite;

            return generator.Generate(_draft.Items);
        }

        /// <summary>窓で決めた重みを、その場で使う 1 個へ写す。</summary>
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

        // ───────── VRCUrl へ焼く(Phase6-3)─────────

        private void DrawBakeBar()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ワールドへ反映", EditorStyles.boldLabel);

            if (!CatalogUrlTableBridge.IsAvailable)
            {
                EditorGUILayout.HelpBox(CatalogUrlTableBridge.UnavailableReason, MessageType.None);
                return;
            }

            int targets = CatalogUrlTableBridge.CountInScene();

            using (new EditorGUI.DisabledScope(_dirty || targets == 0))
            {
                if (GUILayout.Button(
                        "③ VRCUrl へ焼く(シーンの Catalog " + targets + " 個)",
                        GUILayout.Height(26f)))
                {
                    Bake();
                }
            }

            if (_dirty)
            {
                EditorGUILayout.HelpBox("先に保存してください(未保存のぶんは焼かれません)。",
                                        MessageType.Warning);
            }
            else if (targets == 0)
            {
                EditorGUILayout.HelpBox(
                    "シーンに SmartMediaPlayer がありません。Hierarchy へ置いてください。",
                    MessageType.Info);
            }
        }

        private void Bake()
        {
            CatalogUrlTableBridge.BakeReport report = CatalogUrlTableBridge.BakeIntoScene(_asset);

            _importMessage = report.Message;
            _lastImportOk = report.Ok;

            if (report.Ok) Debug.Log("[CatalogBuilder] " + report.Message);
            else Debug.LogWarning("[CatalogBuilder] " + report.Message);

            EditorUtility.DisplayDialog("Catalog Builder", report.Message, "OK");
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

            EditorGUILayout.HelpBox(
                "保存すると、完成している " + _draft.CompleteCount + " 件だけが書かれます"
                + "(ID・見出し・URL がそろっているもの)。\n"
                + "そのあと「③ VRCUrl へ焼く」を押すと、シーンの SmartMediaPlayer に反映されます。",
                MessageType.None);
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
            CatalogThumbnailCache.Clear();

            if (_asset == null) return;

            CatalogDraftIO.Load(_asset, _draft);
            if (_draft.Count > 0) _selected = 0;
        }

        private void OnDisable()
        {
            // 窓を閉じたら絵を捨てる。開いている間だけ覚えていればよい。
            CatalogThumbnailCache.Clear();
        }

        private void SaveToAsset()
        {
            int written = CatalogDraftIO.Save(_asset, _draft);
            if (written < 0) return;

            _dirty = false;
            _lastImportOk = true;
            _importMessage = written + " 件を保存しました。";
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
