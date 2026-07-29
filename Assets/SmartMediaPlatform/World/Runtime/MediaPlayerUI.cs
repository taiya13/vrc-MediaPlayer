using SmartMediaPlatform.Catalog.Store;
using SmartMediaPlatform.Library;
using UnityEngine;

namespace SmartMediaPlatform.World
{
    /// <summary>
    /// <b>画面。</b>既定の <see cref="IMediaPlayerUI"/> 実装(β 版)。
    ///
    /// <b>判断ロジックを 1 つも持っていません。</b>
    /// 描くのは <c>PlaybackFlow</c> が持っている内容だけ、
    /// 押されたら <see cref="IMediaController"/> に伝えるだけです。
    /// だから<b>この画面ごと差し替えても、下は 1 行も変わりません</b>。
    ///
    /// <b>なぜ IMGUI(OnGUI)なのか</b><br/>
    /// β 版として「アセットを 1 つも作らずに、ドラッグしただけで触れる」ことを優先しました。
    /// uGUI の Canvas やワールド内 UI は Prefab とレイアウトの作り込みが本体になるため、
    /// <b>Phase5-2 で差し替える前提</b>です
    /// (差し替え先は <see cref="IMediaPlayerUI"/> を実装するだけで済みます)。
    /// </summary>
    [AddComponentMenu("Smart Media Platform/Media Player UI")]
    public sealed class MediaPlayerUI : MonoBehaviour, IMediaPlayerUI
    {
        [Header("表示")]
        [SerializeField] private bool _visible = true;

        [Tooltip("画面のどこに出すか(0〜1)")]
        [SerializeField] private Rect _area = new Rect(0.02f, 0.02f, 0.46f, 0.96f);

        [Tooltip("文字の大きさ")]
        [SerializeField] private int _fontSize = 12;

        private MediaPlayerContext _context;
        private IMediaController _controller;

        private Vector2 _libraryScroll;
        private Vector2 _queueScroll;
        private string _status = "一覧から選んで「▶ 再生」を押してください。";
        private GUIStyle _richLabel;

        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        public void Bind(MediaPlayerContext context)
        {
            _context = context;

            // 操作は窓口を通す(PlayerSession を直接触らない)
            _controller = GetComponentInParent<SmartMediaPlayerRoot>() != null
                ? GetComponentInParent<SmartMediaPlayerRoot>().Controller
                : null;
        }

        private void OnGUI()
        {
            if (!_visible || _context == null) return;

            GUI.skin.label.fontSize = _fontSize;
            GUI.skin.button.fontSize = _fontSize;

            var area = new Rect(
                _area.x * Screen.width, _area.y * Screen.height,
                _area.width * Screen.width, _area.height * Screen.height);

            GUILayout.BeginArea(area, GUI.skin.box);

            DrawNowPlaying();
            GUILayout.Space(4f);
            DrawLibrary();
            GUILayout.Space(4f);
            DrawRelated();
            GUILayout.Space(4f);
            DrawQueue();
            GUILayout.Space(4f);
            GUILayout.Label(_status);

            GUILayout.EndArea();
        }

        private void DrawNowPlaying()
        {
            var now = _context.Flow.NowPlaying;

            GUILayout.Label($"<b>▶ {now.FormatTitle()}</b>  [{now.State}]  {now.FormatTime()}",
                            RichLabel());

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("再生/一時停止")) Do(() => _controller?.TogglePlayPause());
            if (GUILayout.Button("次へ", GUILayout.Width(60f))) Do(() => _controller?.Next());
            if (GUILayout.Button("前へ", GUILayout.Width(60f))) Do(() => _controller?.Previous());
            if (GUILayout.Button("停止", GUILayout.Width(60f))) Do(() => _controller?.Stop());
            GUILayout.EndHorizontal();
        }

        private void DrawLibrary()
        {
            var library = _context.Flow.Library;

            GUILayout.Label($"<b>Library</b>  {library.Count} 件", RichLabel());

            _libraryScroll = GUILayout.BeginScrollView(_libraryScroll, GUILayout.MinHeight(120f));
            for (int i = 0; i < library.Count; i++)
            {
                var item = library.GetAt(i);
                bool selected = i == library.SelectedIndex;

                if (GUILayout.Toggle(selected, Row(item, i), GUI.skin.button) != selected)
                {
                    library.Select(i);
                    _status = $"選択: {item.Title}";
                }
            }
            GUILayout.EndScrollView();

            GUI.enabled = library.HasSelection;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("▶ 再生"))
            {
                _status = Report(_controller != null && _controller.PlaySelected(), "再生");
            }
            if (GUILayout.Button("次に再生"))
            {
                _status = Report(_controller != null && _controller.PlayNextSelected(), "次に再生");
            }
            if (GUILayout.Button("＋ Queue"))
            {
                _status = Report(_controller != null && _controller.EnqueueSelected(), "Queue に追加");
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        private void DrawRelated()
        {
            var related = _context.Flow.Related;
            if (related.Count == 0) return;

            GUILayout.Label($"<b>関連</b>  {related.Count} 件", RichLabel());

            for (int i = 0; i < related.Count; i++)
            {
                var item = related.GetAt(i);
                if (GUILayout.Button($"▶ {item.Title}"))
                {
                    _status = Report(
                        _controller != null && _controller.PlayRelatedAt(i), $"関連 {item.Title}");
                }
            }
        }

        private void DrawQueue()
        {
            var queue = _context.Flow.Queue;

            GUILayout.Label($"<b>Queue</b>  {queue.Count} 件", RichLabel());

            _queueScroll = GUILayout.BeginScrollView(_queueScroll, GUILayout.MinHeight(80f));
            for (int i = 0; i < queue.Count; i++)
            {
                var item = queue.GetAt(i);

                GUILayout.BeginHorizontal();
                GUILayout.Label((i == 0 ? "♪ " : $"{i}. ") + item.Title);
                GUILayout.FlexibleSpace();

                // 先頭(いま鳴っているもの)は動かさない・消さない
                GUI.enabled = i > 0;
                if (GUILayout.Button("飛ぶ", GUILayout.Width(50f)))
                {
                    _status = Report(
                        _controller != null && _controller.JumpInQueueTo(i), $"{item.Title} へ移動");
                }
                if (GUILayout.Button("×", GUILayout.Width(28f)))
                {
                    _status = Report(
                        _controller != null && _controller.RemoveFromQueue(i), "Queue から削除");
                }
                GUI.enabled = true;

                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        // ───────── 補助 ─────────

        private static string Row(DisplayMeta item, int index)
        {
            return $"{index + 1,3}. {item.Title}\n"
                   + $"      {item.Artist}  |  "
                   + MediaLibraryFormatter.FormatDuration(item.DurationSeconds);
        }

        private void Do(System.Func<bool> action)
        {
            if (action == null) return;
            action();
        }

        private static string Report(bool ok, string what)
        {
            return ok ? $"{what} しました。" : $"{what} できませんでした。";
        }

        private GUIStyle RichLabel()
        {
            if (_richLabel == null) _richLabel = new GUIStyle(GUI.skin.label) { richText = true };
            return _richLabel;
        }

        public override string ToString() => "MediaPlayerUI(IMGUI β 版)";
    }
}
