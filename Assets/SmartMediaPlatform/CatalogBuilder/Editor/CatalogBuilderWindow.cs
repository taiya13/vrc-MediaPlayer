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

            _mergeMode = EditorGUILayout.Popup(
                "混ぜ方", _mergeMode,
                new[] { "そのまま足す", "同じ ID は上書き", "いまの中身を捨てて入れ替え" });

            using (new EditorGUI.DisabledScope(
                       !importer.IsAvailable || !importer.CanImport(_importerInput)))
            {
                if (GUILayout.Button("取り込む"))
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

        private void RunImport(ICatalogImporter importer)
        {
            CatalogImportResult result = importer.Import(_importerInput);

            if (result == null)
            {
                _importMessage = "取り込み元が結果を返しませんでした。";
                return;
            }

            if (!result.Ok)
            {
                _importMessage = "失敗: " + result.Message;
                return;
            }

            int changed = _draft.Merge(result.Items, _mergeMode);
            _importMessage = result.Message + "(" + changed + " 件を反映)";
            MarkDirty();
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
                + "ワールドで再生するには、そのあと Catalog の Inspector で VRCUrl に焼いてください。",
                MessageType.None);
        }

        // ───────── 状態 ─────────

        private void ReloadFromAsset()
        {
            _draft = new CatalogDraft();
            _selected = -1;
            _dirty = false;
            _importMessage = "";

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
