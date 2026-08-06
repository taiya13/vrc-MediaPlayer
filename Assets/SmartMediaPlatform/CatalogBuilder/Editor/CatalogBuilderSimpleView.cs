#if UNITY_EDITOR
using System;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Assets;
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.CatalogBuilder.EditorTools
{
    /// <summary>
    /// <b>Catalog Builder の「ふだんの画面」。</b>Phase7-3。
    ///
    /// <b>なぜ分けたのか</b><br/>
    /// これまでの画面は<b>開発ツールの形</b>でした。一覧・編集・まとめて直す・
    /// 取り込み・絞り込み・並べ替えが<b>全部いちどに見えている</b>ので、
    /// 「YouTube の URL を貼って曲を足したいだけ」の人には、
    /// <b>どこから触ればよいのかが分かりません</b>。
    ///
    /// ここでやることを 1 つに絞ります —— <b>URL を貼って、選んで、足す。</b>
    /// それ以外(1 件ずつの編集・まとめて直す・並べ替え)は
    /// <b>「くわしく」に入れて畳みます</b>。機能は 1 つも減らしていません。
    ///
    /// <b>Apple Music / Spotify / YouTube Music から採った考え方</b>
    /// <list type="number">
    /// <item><b>いま何をする番かを、常に 1 か所で示す。</b>
    ///       ① 取得 → ② 選ぶ → ③ 足す → ④ ワールドへ。
    ///       済んだ番号は色を落とし、いまの番号だけを明るくします</item>
    /// <item><b>次に押すボタンは、いつも 1 つだけ大きい。</b>
    ///       選択肢が同じ大きさで並ぶと、どれが本筋か分かりません</item>
    /// <item><b>取れたものは表ではなくカードで見せる。</b>
    ///       曲を選ぶのは<b>絵</b>でする作業です。文字の行で出すと、
    ///       知らない曲は名前を読むしかありません</item>
    /// <item><b>設定は既定のまま使えるようにして、畳んでおく。</b>
    ///       開かなくても正しく動くものは、開かせない</item>
    /// </list>
    /// </summary>
    public sealed partial class CatalogBuilderWindow
    {
        // ───────── ふだんの画面の状態 ─────────

        /// <summary>表を並べた画面(今までの形)を出すか。</summary>
        private bool _expertMode;

        private Vector2 _simpleScroll;
        private bool _showImportOptions;

        // カード 1 枚の大きさ。絵は 16:9。
        private const float CardWidth = 190f;
        private const float CardGap = 10f;

        // ───────── 上のバー ─────────

        private void DrawModeBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label(
                _asset != null ? _asset.name : "カタログ未選択",
                EditorStyles.toolbarButton, GUILayout.Width(180f));

            if (_expertMode)
            {
                var hint = new GUIStyle(EditorStyles.miniLabel);
                hint.normal.textColor = MutedText();
                GUILayout.Label("表の画面(慣れた人向け)", hint);
            }

            GUILayout.FlexibleSpace();

            bool expert = GUILayout.Toggle(
                _expertMode, "くわしく", EditorStyles.toolbarButton, GUILayout.Width(72f));

            if (expert != _expertMode)
            {
                _expertMode = expert;
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();
        }

        // ───────── ふだんの画面 ─────────

        private void DrawSimpleFlow()
        {
            _simpleScroll = EditorGUILayout.BeginScrollView(_simpleScroll);

            EditorGUILayout.Space(6f);
            DrawSteps();
            EditorGUILayout.Space(10f);

            if (_asset == null)
            {
                DrawStepPickCatalog();
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawStepFetch();
            EditorGUILayout.Space(10f);

            if (!_selection.IsEmpty)
            {
                DrawStepChoose();
                EditorGUILayout.Space(10f);
            }

            DrawStepPublish();

            EditorGUILayout.Space(12f);
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// <b>いま何をする番か。</b>
        /// 4 つのうち 1 つだけを明るくします。「次はこれ」が読めれば、
        /// 説明を読まなくても進めます。
        /// </summary>
        private void DrawSteps()
        {
            int current = CurrentStep();

            string[] names = { "① 取得", "② 選ぶ", "③ 足す", "④ ワールドへ" };

            EditorGUILayout.BeginHorizontal();

            for (int i = 0; i < names.Length; i++)
            {
                var style = new GUIStyle(EditorStyles.miniLabel);
                style.alignment = TextAnchor.MiddleCenter;

                bool active = i == current;
                bool done = i < current;

                style.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
                style.normal.textColor = active
                    ? AccentText()
                    : (done ? DoneText() : MutedText());

                GUILayout.Label(names[i], style, GUILayout.Height(20f));

                if (i < names.Length - 1)
                {
                    var arrow = new GUIStyle(EditorStyles.miniLabel);
                    arrow.normal.textColor = MutedText();
                    GUILayout.Label("→", arrow, GUILayout.Width(18f));
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private int CurrentStep()
        {
            if (_asset == null) return 0;
            if (!_selection.IsEmpty) return 1;
            if (_draft.Count == 0) return 0;
            if (_syncReport != null && _syncReport.Checked && !_syncReport.InSync) return 3;
            return 3;
        }

        // ───────── ① カタログを決める ─────────

        private void DrawStepPickCatalog()
        {
            EditorGUILayout.HelpBox(
                "曲の入れ物(カタログ)を 1 つ決めます。\n"
                + "作った物は Assets/SmartMediaPlatform_Data/Catalogs に置かれ、\n"
                + "システムを更新しても消えません。",
                MessageType.Info);

            if (GUILayout.Button("新しく作る", GUILayout.Height(32f)))
            {
                var created = CatalogDraftIO.CreateNew();
                if (created != null)
                {
                    _asset = created;
                    ReloadFromAsset();
                }
            }

            EditorGUILayout.Space(4f);

            var picked = (MediaCatalogAsset)EditorGUILayout.ObjectField(
                "すでにある物を使う", _asset, typeof(MediaCatalogAsset), false);

            if (picked != _asset)
            {
                _asset = picked;
                ReloadFromAsset();
            }
        }

        // ───────── ① 取得 ─────────

        /// <summary>
        /// いま使う取り込み元。ふだんの画面では<b>選ばせません</b> ——
        /// 実際に選ぶものが 1 つしかないのに選択肢を出すと、
        /// 「何か決めないと先へ進めない」と思わせるためです。
        /// 2 つ以上あるときだけ、「くわしく」で選べます。
        /// </summary>
        private ICatalogImporter CurrentImporter()
        {
            ICatalogImporter[] importers = CatalogImporterRegistry.All();
            if (importers == null || importers.Length == 0) return null;

            int index = Mathf.Clamp(_importerIndex, 0, importers.Length - 1);
            return importers[index];
        }

        private void DrawStepFetch()
        {
            ICatalogImporter importer = CurrentImporter();
            if (importer == null)
            {
                EditorGUILayout.HelpBox("取り込み元が見つかりません。", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("YouTube の URL を貼る", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "チャンネル・再生リスト・動画 1 本、どれでも構いません。",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(2f);

            var fieldStyle = new GUIStyle(EditorStyles.textField);
            fieldStyle.fontSize = 13;
            _importerInput = EditorGUILayout.TextField(_importerInput, fieldStyle,
                                                       GUILayout.Height(26f));

            EditorGUILayout.Space(6f);
            DrawFetchOrder();
            EditorGUILayout.Space(6f);

            bool ready = importer.IsAvailable && importer.CanImport(_importerInput);

            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button("この URL から取得する", GUILayout.Height(36f)))
                {
                    RunImport(importer, false);
                }
            }

            if (!importer.IsAvailable)
            {
                EditorGUILayout.Space(4f);
                DrawImporterSetup(importer);
            }

            DrawImportOptions(importer);

            if (_importMessage.Length > 0)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(
                    _importMessage, _lastImportOk ? MessageType.Info : MessageType.Warning);
            }
        }

        /// <summary>
        /// <b>取り込み方(新着順 / 人気順)。</b>Phase7-4。
        ///
        /// <b>畳まずに出します。</b>ここは<b>押す前に決めておくもの</b>で、
        /// あとから変えると取り直しになるためです。
        /// 既定は新着順のままなので、触らなければ今までどおりです。
        /// </summary>
        private void DrawFetchOrder()
        {
            var modes = CurrentImporter() as ICatalogImporterModes;
            if (modes == null) return;

            string[] names = modes.ModeNames;
            if (names == null || names.Length < 2) return;   // 選べないものは見せない

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("取り込み方", GUILayout.Width(64f));

            int picked = GUILayout.Toolbar(
                Mathf.Clamp(modes.Mode, 0, names.Length - 1), names, GUILayout.Height(22f));

            EditorGUILayout.EndHorizontal();

            if (picked != modes.Mode) modes.Mode = picked;

            string[] amounts = modes.AmountLabels;
            if (amounts != null && amounts.Length > 0)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("件数", GUILayout.Width(64f));

                int index = GUILayout.Toolbar(
                    Mathf.Clamp(modes.AmountIndex, 0, amounts.Length - 1), amounts,
                    GUILayout.Height(20f));

                EditorGUILayout.EndHorizontal();

                if (index != modes.AmountIndex) modes.AmountIndex = index;
            }

            string hint = modes.ModeHint;
            if (string.IsNullOrEmpty(hint)) return;

            var style = new GUIStyle(EditorStyles.miniLabel);
            style.wordWrap = true;
            style.normal.textColor = MutedText();

            GUILayout.Label(hint, style);
        }

        /// <summary>
        /// <b>取り込みの細かい設定。畳んでおきます。</b>
        /// 既定のままで正しく動くものを開かせると、
        /// 「読まないと使えない」と思わせてしまいます。
        /// </summary>
        private void DrawImportOptions(ICatalogImporter importer)
        {
            EditorGUILayout.Space(2f);

            _showImportOptions = EditorGUILayout.Foldout(
                _showImportOptions, "取り込みの設定", true);

            if (!_showImportOptions) return;

            EditorGUI.indentLevel++;

            DrawShortsToggle(importer);

            if (importer is IIncrementalCatalogImporter)
            {
                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField(
                    "一度取り込んだチャンネルは、増えたぶんだけ取り直せます。",
                    EditorStyles.wordWrappedMiniLabel);

                using (new EditorGUI.DisabledScope(!importer.CanImport(_importerInput)))
                {
                    if (GUILayout.Button("新着だけ取る")) RunImport(importer, true);
                }
            }

            EditorGUI.indentLevel--;
        }

        // ───────── ② 選ぶ ─────────

        private void DrawStepChoose()
        {
            EditorGUILayout.LabelField(
                "入れるものを選ぶ  (" + _selection.SelectedCount
                + " / " + _selection.Count + " 件)", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("全部", EditorStyles.miniButtonLeft)) _selection.SelectAll();
            if (GUILayout.Button("解除", EditorStyles.miniButtonMid)) _selection.SelectNone();

            using (new EditorGUI.DisabledScope(_selection.ExistingCount == 0))
            {
                if (GUILayout.Button("まだ無いものだけ", EditorStyles.miniButtonMid))
                {
                    _selection.SelectOnlyNew();
                }
            }

            if (GUILayout.Button("取得結果を消す", EditorStyles.miniButtonRight))
            {
                ClearImportResult();
                return;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);
            DrawSelectionCards();
            EditorGUILayout.Space(6f);
            DrawSimpleApply();
        }

        /// <summary>
        /// <b>取れたものをカードで並べる。</b>
        ///
        /// 曲を選ぶのは<b>絵</b>でする作業です。表の行で出すと、
        /// 知らない曲は名前を読むしかありません。
        /// 選んでいるカードだけ枠を強調色にして、
        /// <b>チェックボックスを探さなくても分かる</b>ようにします。
        /// </summary>
        private void DrawSelectionCards()
        {
            float available = position.width - 32f;
            int columns = Mathf.Max(1, Mathf.FloorToInt(available / (CardWidth + CardGap)));

            _selectionScroll = EditorGUILayout.BeginScrollView(
                _selectionScroll, GUILayout.Height(CardHeight() * 2f + CardGap * 2f + 8f));

            for (int i = 0; i < _selection.Count; i += columns)
            {
                EditorGUILayout.BeginHorizontal();

                for (int c = 0; c < columns && i + c < _selection.Count; c++)
                {
                    DrawSelectionCard(i + c);
                    GUILayout.Space(CardGap);
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(CardGap);
            }

            EditorGUILayout.EndScrollView();
        }

        private static float CardHeight()
        {
            // 絵(16:9) + 曲名 2 行 + チャンネル + 時間
            return Mathf.Round(CardWidth * 9f / 16f) + 62f;
        }

        private void DrawSelectionCard(int index)
        {
            CatalogDraftItem item = _selection.GetAt(index);
            if (item == null) return;

            bool selected = _selection.IsSelected(index);
            bool existing = _selection.IsExisting(index);

            Rect card = GUILayoutUtility.GetRect(
                CardWidth, CardHeight(), GUILayout.Width(CardWidth), GUILayout.Height(CardHeight()));

            // ── 地と枠。選んでいるものだけ枠が付く。
            EditorGUI.DrawRect(card, selected ? SelectedCardFace() : CardFace());
            if (selected) DrawBorder(card, AccentText());

            float artHeight = Mathf.Round(CardWidth * 9f / 16f);
            var artRect = new Rect(card.x, card.y, card.width, artHeight);

            Texture2D art = CatalogThumbnailCache.Get(item.ThumbnailPath);
            if (art != null) GUI.DrawTexture(artRect, art, ScaleMode.ScaleAndCrop);
            else EditorGUI.DrawRect(artRect, EmptyArtFace());

            // ── すでにあるものは、ひと目で分かるように帯を出す。
            if (existing)
            {
                var badge = new Rect(artRect.x + 6f, artRect.y + 6f, 54f, 18f);
                EditorGUI.DrawRect(badge, new Color(0f, 0f, 0f, 0.72f));

                var badgeStyle = new GUIStyle(EditorStyles.miniLabel);
                badgeStyle.alignment = TextAnchor.MiddleCenter;
                badgeStyle.normal.textColor = MutedText();
                GUI.Label(badge, "追加済み", badgeStyle);
            }

            // ── 時間は絵の右下に重ねる(YouTube と同じ位置)。
            string duration = FormatDuration(item.DurationSeconds);
            var timeRect = new Rect(artRect.xMax - 46f, artRect.yMax - 20f, 40f, 16f);
            EditorGUI.DrawRect(timeRect, new Color(0f, 0f, 0f, 0.72f));

            var timeStyle = new GUIStyle(EditorStyles.miniLabel);
            timeStyle.alignment = TextAnchor.MiddleCenter;
            timeStyle.normal.textColor = Color.white;
            GUI.Label(timeRect, duration, timeStyle);

            // ── 曲名とチャンネル。
            var titleStyle = new GUIStyle(EditorStyles.label);
            titleStyle.wordWrap = true;
            titleStyle.fontSize = 11;

            var titleRect = new Rect(card.x + 6f, artRect.yMax + 3f, card.width - 12f, 32f);
            GUI.Label(titleRect, item.Title, titleStyle);

            var subStyle = new GUIStyle(EditorStyles.miniLabel);
            subStyle.normal.textColor = MutedText();

            var subRect = new Rect(card.x + 6f, titleRect.yMax, card.width - 12f, 16f);
            GUI.Label(subRect, item.Artist, subStyle);

            // ── カードのどこを押しても選び直せる。
            //    小さいチェックボックスを狙わせない。
            if (GUI.Button(card, GUIContent.none, EditorStyles.label))
            {
                _selection.SetSelected(index, !selected);
                GUI.FocusControl(null);
            }
        }

        private void DrawSimpleApply()
        {
            int selected = _selection.SelectedCount;
            int fresh = _draft.CountNew(_selection.SelectedItems());

            using (new EditorGUI.DisabledScope(selected == 0))
            {
                string caption = "選んだ " + selected + " 件をカタログに足す";
                if (fresh != selected) caption += "(増えるのは " + fresh + " 件)";

                if (GUILayout.Button(caption, GUILayout.Height(36f)))
                {
                    // ふだんの画面では、混ぜ方は「すでにある物は飛ばす」で固定。
                    // 上書きしたい人は「くわしく」で選べます。
                    _mergeMode = CatalogDraft.MergeSkip;
                    ApplySelection();
                }
            }
        }

        // ───────── ④ ワールドへ ─────────

        private void DrawStepPublish()
        {
            EditorGUILayout.LabelField(
                "カタログの中身  " + _draft.Count + " 曲", EditorStyles.boldLabel);

            if (_draft.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "まだ 1 曲も入っていません。上の欄に URL を貼って取得してください。",
                    MessageType.Info);
                return;
            }

            bool dirty = _syncReport != null && _syncReport.Checked && !_syncReport.InSync;

            if (dirty)
            {
                EditorGUILayout.HelpBox(
                    "カタログとワールドの中身が違います。\n"
                    + "下のボタンを押すまで、ワールドには反映されません。",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(2f);

            if (GUILayout.Button("ワールドへ反映する(VRCUrl へ焼く)", GUILayout.Height(36f)))
            {
                Bake();
            }

            EditorGUILayout.LabelField(
                "VRCUrl は実行時に作れないので、ここで焼いた URL だけが再生できます。",
                EditorStyles.wordWrappedMiniLabel);
        }

        // ───────── 色 ─────────
        //
        // エディタの見た目(明るい / 暗い)で見え方が変わるので、
        // 決め打ちの色ではなく、地の明るさから決めます。

        private static Color AccentText()
        {
            return IsDark()
                ? new Color(0.35f, 0.82f, 0.76f)
                : new Color(0.05f, 0.45f, 0.42f);
        }

        private static Color DoneText()
        {
            return IsDark()
                ? new Color(0.55f, 0.58f, 0.60f)
                : new Color(0.42f, 0.45f, 0.47f);
        }

        private static Color MutedText()
        {
            return IsDark()
                ? new Color(0.62f, 0.64f, 0.66f)
                : new Color(0.38f, 0.40f, 0.42f);
        }

        private static Color CardFace()
        {
            return IsDark()
                ? new Color(0.20f, 0.20f, 0.21f)
                : new Color(0.88f, 0.88f, 0.89f);
        }

        private static Color SelectedCardFace()
        {
            return IsDark()
                ? new Color(0.16f, 0.28f, 0.28f)
                : new Color(0.82f, 0.93f, 0.92f);
        }

        private static Color EmptyArtFace()
        {
            return IsDark()
                ? new Color(0.14f, 0.14f, 0.15f)
                : new Color(0.78f, 0.78f, 0.79f);
        }

        /// <summary>暗い見た目か。色は地の明るさから決めます。</summary>
        private static bool IsDark()
        {
            return EditorGUIUtility.isProSkin;
        }

        private static void DrawBorder(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), color);
        }
    }
}
#endif
