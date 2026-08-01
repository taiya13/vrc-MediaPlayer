#if UNITY_EDITOR
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
    /// だけで、<b>足す・消す・混ぜる・焼く</b>はすべて
    /// <see cref="CatalogDraft"/> と <see cref="CatalogDraftIO"/> の仕事です。
    /// おかげで判断の部分は EditMode で検証できます
    /// (Phase5 の「UI はロジックを持たない」と同じ考え方)。
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
        private int _mergeMode = CatalogDraft.MergeAppend;
        private string _importMessage = "";

        // Phase6-3: 取り込んだものを選んでから入れる
        private readonly CatalogImportSelection _selection = new CatalogImportSelection();
        private Vector2 _selectionScroll;
        private bool _lastImportOk;

        [MenuItem(MenuPath, false, -90)]
        public static void Open()
        {
            var window = GetWindow<CatalogBuilderWindow>("Catalog Builder");
            window.minSize = new Vector2(720f, 520f);
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

            EditorGUILayout.BeginHorizontal();
            DrawList();
            DrawInspector();
            EditorGUILayout.EndHorizontal();

            DrawImportBar();
            DrawSelection();
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

        // ───────── 左:一覧 ─────────

        private void DrawList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(300f));

            EditorGUILayout.LabelField(
                _draft.Count + " 件 / 完成 " + _draft.CompleteCount + " 件",
                EditorStyles.boldLabel);

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            for (int i = 0; i < _draft.Count; i++)
            {
                CatalogDraftItem item = _draft.GetAt(i);

                EditorGUILayout.BeginHorizontal();

                bool wasSelected = i == _selected;
                string label = (i + 1) + ". " + (item.IsComplete ? "" : "⚠ ") + item;

                if (GUILayout.Toggle(wasSelected, label, EditorStyles.miniButton) != wasSelected)
                {
                    _selected = i;
                    GUI.FocusControl(null);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

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

                if (GUILayout.Button("▲", GUILayout.Width(28f)))
                {
                    if (_draft.Move(_selected, -1)) { _selected--; MarkDirty(); }
                }

                if (GUILayout.Button("▼", GUILayout.Width(28f)))
                {
                    if (_draft.Move(_selected, 1)) { _selected++; MarkDirty(); }
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
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
            item.Artist = EditorGUILayout.TextField("アーティスト", item.Artist);
            item.Genre = EditorGUILayout.TextField("ジャンル", item.Genre);
            item.Type = (MediaType)EditorGUILayout.EnumPopup("種別", item.Type);
            item.Url = EditorGUILayout.TextField("URL", item.Url);
            item.DurationSeconds = EditorGUILayout.IntField("長さ(秒)", item.DurationSeconds);

            EditorGUILayout.Space();
            item.Tags = DrawStringList("タグ", item.Tags);
            item.RelatedIds = DrawStringList("関連 ID", item.RelatedIds);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("出どころ", item.Source);

            // Phase6-3 以降で使う枠。いまは持つだけ。
            item.ThumbnailPath = EditorGUILayout.TextField("サムネイル(未使用)", item.ThumbnailPath);

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
                    "取り込み元がまだありません(Phase6-2 以降で追加)。\n"
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

            using (new EditorGUI.DisabledScope(
                       !importer.IsAvailable || !importer.CanImport(_importerInput)))
            {
                if (GUILayout.Button("① 取得する", GUILayout.Height(24f)))
                {
                    RunImport(importer);
                }
            }

            if (!importer.IsAvailable)
            {
                EditorGUILayout.HelpBox(importer.UnavailableReason, MessageType.Warning);
            }

            if (_importMessage.Length > 0)
            {
                EditorGUILayout.HelpBox(_importMessage, MessageType.None);
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
            // そのまま足すと同じ曲が並んでしまう。
            int existing = _selection.MarkExisting(_draft);

            _lastImportOk = true;
            _importMessage = result.Message;
            if (existing > 0) _importMessage += "(うち " + existing + " 件はすでにあります)";

            Repaint();
        }

        // ───────── 取り込んだものを選ぶ(Phase6-3)─────────

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
            EditorGUILayout.EndHorizontal();

            _selectionScroll = EditorGUILayout.BeginScrollView(
                _selectionScroll, GUILayout.Height(180f));

            for (int i = 0; i < _selection.Count; i++)
            {
                DrawSelectionRow(i);
            }

            EditorGUILayout.EndScrollView();

            _mergeMode = EditorGUILayout.Popup(
                "混ぜ方", _mergeMode,
                new[] { "そのまま足す", "同じ ID は上書き", "いまの中身を捨てて入れ替え" });

            using (new EditorGUI.DisabledScope(_selection.SelectedCount == 0))
            {
                if (GUILayout.Button(
                        "② 選んだ " + _selection.SelectedCount + " 件を追加する",
                        GUILayout.Height(26f)))
                {
                    ApplySelection();
                }
            }
        }

        private void DrawSelectionRow(int index)
        {
            CatalogDraftItem item = _selection.GetAt(index);
            if (item == null) return;

            EditorGUILayout.BeginHorizontal();

            bool wanted = EditorGUILayout.Toggle(_selection.IsSelected(index), GUILayout.Width(18f));
            _selection.SetSelected(index, wanted);

            string label = item.Title;
            if (_selection.IsExisting(index)) label = "【あり】" + label;
            if (!item.IsComplete) label = "⚠ " + label;

            EditorGUILayout.LabelField(label);

            EditorGUILayout.LabelField(item.Artist, GUILayout.Width(140f));
            EditorGUILayout.LabelField(FormatDuration(item.DurationSeconds), GUILayout.Width(56f));

            EditorGUILayout.EndHorizontal();
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
            int changed = _draft.Merge(chosen, _mergeMode);

            _importMessage = changed + " 件を追加しました。保存するとカタログに入ります。";
            _selection.MarkExisting(_draft);

            if (_selected < 0 && _draft.Count > 0) _selected = 0;
            MarkDirty();
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

            if (_asset == null) return;

            CatalogDraftIO.Load(_asset, _draft);
            if (_draft.Count > 0) _selected = 0;
        }

        private void SaveToAsset()
        {
            int written = CatalogDraftIO.Save(_asset, _draft);
            if (written < 0) return;

            _dirty = false;
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
