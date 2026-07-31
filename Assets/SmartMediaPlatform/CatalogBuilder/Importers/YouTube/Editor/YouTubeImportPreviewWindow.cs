#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.CatalogBuilder.YouTube.EditorTools
{
    /// <summary>
    /// <b>YouTube から取ったものを見るだけの窓。</b>Phase6-2。
    /// <c>Tools > Smart Media Platform > YouTube Import (プレビュー)</c>。
    ///
    /// <b>Catalog へは書きません。</b>取れたかどうか、何が取れたかを確かめるだけです
    /// (Builder への反映は Phase6-3)。
    ///
    /// <b>Catalog Builder 本体とは別の窓</b>にしてあります。
    /// Phase6-1 で作った <c>CatalogBuilderWindow</c> を 1 行も触らずに
    /// 取り込みを試せるようにするためで、
    /// <b>取り込み元を足しても Builder 本体が壊れない</b>ことの確認も兼ねています。
    /// </summary>
    public sealed class YouTubeImportPreviewWindow : EditorWindow
    {
        private const string MenuPath = "Tools/Smart Media Platform/YouTube Import (プレビュー)";

        private readonly YouTubeCatalogImporter _importer = new YouTubeCatalogImporter();

        private string _input = "";
        private string _message = "";
        private bool _lastOk;

        private Vector2 _scroll;
        private YouTubeApiSettings _settings;

        [MenuItem(MenuPath, false, -80)]
        public static void Open()
        {
            var window = GetWindow<YouTubeImportPreviewWindow>("YouTube Import");
            window.minSize = new Vector2(680f, 460f);
            window.Show();
        }

        private void OnEnable()
        {
            _settings = YouTubeApiSettings.LoadIfPresent();
        }

        private void OnGUI()
        {
            DrawSettings();
            EditorGUILayout.Space();

            DrawInput();
            EditorGUILayout.Space();

            DrawMessage();
            DrawList();
        }

        // ───────── API キー ─────────

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("API キー", EditorStyles.boldLabel);

            if (_settings == null)
            {
                EditorGUILayout.HelpBox(
                    "設定アセットがまだありません。\n"
                    + "Google Cloud で YouTube Data API v3 を有効にし、API キーを作ってください。",
                    MessageType.Info);

                if (GUILayout.Button("設定アセットを作る"))
                {
                    _settings = YouTubeApiSettings.LoadOrCreate();
                    Selection.activeObject = _settings;
                }
                return;
            }

            EditorGUI.BeginChangeCheck();
            _settings.ApiKey = EditorGUILayout.PasswordField("API キー", _settings.ApiKey);
            _settings.MaxItems = EditorGUILayout.IntSlider("1 回の上限", _settings.MaxItems, 1, 500);

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_settings);
            }

            if (!_settings.HasKey)
            {
                EditorGUILayout.HelpBox("API キーが空です。", MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "このアセットは公開リポジトリへ入れないでください。\n"
                + YouTubeApiSettings.AssetPath,
                MessageType.None);

            if (GUILayout.Button("設定アセットを選ぶ")) Selection.activeObject = _settings;
        }

        // ───────── 入力 ─────────

        private void DrawInput()
        {
            EditorGUILayout.LabelField("取り込み元", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_importer.InputHint, EditorStyles.wordWrappedMiniLabel);

            _input = EditorGUILayout.TextField(_input);

            YouTubeUrlParser.Target target = YouTubeUrlParser.Parse(_input);
            EditorGUILayout.LabelField("判別", target.Describe());

            using (new EditorGUI.DisabledScope(!_importer.IsAvailable || !target.IsValid))
            {
                if (GUILayout.Button("取得する(Catalog へは書きません)", GUILayout.Height(26f)))
                {
                    Run();
                }
            }

            if (!_importer.IsAvailable)
            {
                EditorGUILayout.HelpBox(_importer.UnavailableReason, MessageType.Warning);
            }
        }

        private void Run()
        {
            EditorUtility.DisplayProgressBar("YouTube", "取得しています…", 0.5f);

            CatalogImportResult result;
            try
            {
                result = _importer.Import(_input);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            _lastOk = result != null && result.Ok;
            _message = result != null ? result.Message : "結果が返りませんでした。";

            Repaint();
        }

        // ───────── 結果 ─────────

        private void DrawMessage()
        {
            if (_message.Length == 0) return;

            EditorGUILayout.HelpBox(_message, _lastOk ? MessageType.Info : MessageType.Error);
        }

        private void DrawList()
        {
            IReadOnlyList<YouTubeVideoInfo> videos = _importer.LastVideos;
            if (videos == null || videos.Count == 0) return;

            EditorGUILayout.LabelField(
                "取得結果 " + videos.Count + " 件 — " + _importer.LastTarget.Describe(),
                EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            for (int i = 0; i < videos.Count; i++)
            {
                DrawVideo(i + 1, videos[i]);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.HelpBox(
                "Phase6-2 はここまでです。Catalog Builder への反映は Phase6-3 で行います。",
                MessageType.None);
        }

        private static void DrawVideo(int number, YouTubeVideoInfo video)
        {
            if (video == null) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            string head = number + ". " + video.Title;
            if (video.IsUnavailable) head = number + ". ⚠ " + video.Title;

            EditorGUILayout.LabelField(head, EditorStyles.boldLabel);

            if (video.IsUnavailable)
            {
                EditorGUILayout.LabelField("使えません", video.UnavailableReason);
            }

            EditorGUILayout.LabelField("チャンネル", video.ChannelTitle);
            EditorGUILayout.LabelField("公開日", video.FormatPublishedDate());
            EditorGUILayout.LabelField("再生時間", video.FormatDuration()
                                                  + "(" + video.DurationSeconds + " 秒)");
            EditorGUILayout.LabelField("動画 ID", video.VideoId);

            EditorGUILayout.SelectableLabel(video.WatchUrl, GUILayout.Height(18f));

            if (video.ThumbnailUrl.Length > 0)
            {
                EditorGUILayout.LabelField("サムネイル", EditorStyles.miniBoldLabel);
                EditorGUILayout.SelectableLabel(video.ThumbnailUrl, GUILayout.Height(18f));
            }

            EditorGUILayout.EndVertical();
        }
    }
}
#endif
