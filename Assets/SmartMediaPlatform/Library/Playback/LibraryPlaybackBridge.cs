using System;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Session;

namespace SmartMediaPlatform.Library.Playback
{
    /// <summary>
    /// <b>一覧で選んだものを <see cref="PlayerSession"/> へ渡すだけの接続層。</b>
    ///
    /// <code>
    /// MediaLibrary(閲覧・選択) → LibraryPlaybackBridge → PlayerSession(再生)
    /// </code>
    ///
    /// <b>なぜ別のクラス・別 asmdef なのか</b><br/>
    /// 「Media Library は閲覧と選択だけ」という制約を、
    /// <b>コメントではなく参照関係で守る</b>ためです。
    /// <c>SmartMediaPlatform.Library</c> の asmdef は <c>Catalog</c> しか参照していないので、
    /// <see cref="MediaLibrary"/> からは <c>PlayerSession</c> も <c>Queue</c> も
    /// <c>Backend</c> も<b>そもそも見えません</b>。
    /// 見える場所をこの 1 クラスに閉じ込めてあります。
    ///
    /// <b>境界を越えるのは MediaId(string)だけ</b>です。
    /// <c>MediaItem</c> も URL も渡しません。
    /// Phase3 から通している「URL を知るのは VideoBackend だけ」がそのまま保たれます。
    ///
    /// <b>再生エンジンには何も足していません。</b>
    /// 呼んでいるのは <c>PlayerSession</c> の既存 API
    /// (<c>SetTracks</c> / <c>Enqueue</c> / <c>Play</c>)だけで、
    /// <c>PlayerSession</c> / <c>BackendAdapter</c> / <c>Queue</c> /
    /// <c>Recommendation</c> / <c>VideoBackend</c> は 1 行も変更していません。
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public sealed class LibraryPlaybackBridge
    {
        private readonly IMediaLibrary _library;
        private readonly PlayerSession _session;

        private IBackendLogger _logger;

        /// <param name="library">閲覧・選択している一覧。</param>
        /// <param name="session">再生を任せるセッション。</param>
        /// <param name="logger">ログ出力先。</param>
        public LibraryPlaybackBridge(
            IMediaLibrary library,
            PlayerSession session,
            IBackendLogger logger = null)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _logger = logger ?? NullBackendLogger.Instance;
        }

        // ───────── 設定 ─────────

        /// <summary>
        /// <see cref="PlaySelected"/> のときに、いまの曲一覧を選んだ 1 件で置き換えるか。
        ///
        /// <list type="bullet">
        /// <item><b>true(既定)</b> … 選んだものから再生を始める。
        /// 続きはセッションの自動補充(おすすめ)に任せる</item>
        /// <item><b>false</b> … いまの曲一覧を残したまま末尾に足して、そこへ飛ぶ</item>
        /// </list>
        /// </summary>
        public bool ReplaceTracksOnPlay { get; set; } = true;

        /// <summary>これまでに再生を指示した回数(診断用)。</summary>
        public int PlayCount { get; private set; }

        /// <summary>これまでに Queue へ足した回数(診断用)。</summary>
        public int EnqueueCount { get; private set; }

        /// <summary>直近に渡した MediaId(診断用)。</summary>
        public string LastHandedOffId { get; private set; }

        /// <summary>つないでいる一覧。</summary>
        public IMediaLibrary Library => _library;

        /// <summary>つないでいるセッション。</summary>
        public PlayerSession Session => _session;

        public void SetLogger(IBackendLogger logger)
        {
            _logger = logger ?? NullBackendLogger.Instance;
        }

        // ───────── 受け渡し ─────────

        /// <summary>
        /// いま選んでいるものを再生する。
        /// </summary>
        /// <returns>再生を始められたら true。未選択なら false。</returns>
        public bool PlaySelected()
        {
            string id = _library.SelectedMediaId;
            if (id == null)
            {
                Log("再生できません: 何も選ばれていません");
                return false;
            }
            return Play(id);
        }

        /// <summary>
        /// いま選んでいるものを Queue の末尾に足す(いまの再生は止めない)。
        /// </summary>
        /// <returns>足せたら true。</returns>
        public bool EnqueueSelected()
        {
            string id = _library.SelectedMediaId;
            if (id == null)
            {
                Log("Queue に足せません: 何も選ばれていません");
                return false;
            }
            return Enqueue(id);
        }

        /// <summary>
        /// index 番目を選んでそのまま再生する(一覧を直接クリックしたときの動き)。
        /// </summary>
        public bool PlayAt(int index)
        {
            return _library.Select(index) && PlaySelected();
        }

        /// <summary>
        /// MediaId を指定して再生する。
        /// <b>渡すのは string だけ</b> — ここが Library と再生エンジンの境界です。
        /// </summary>
        /// <returns>再生を始められたら true。</returns>
        public bool Play(string mediaId)
        {
            if (string.IsNullOrWhiteSpace(mediaId)) return false;

            LastHandedOffId = mediaId;

            if (ReplaceTracksOnPlay)
            {
                // 選んだ 1 件から始めて、続きはセッションの自動補充に任せる
                _session.SetTracks(new[] { mediaId });
            }
            else
            {
                // いまの一覧を残したまま末尾に足して、そこまで進める。
                //
                // ★ 進める回数に上限が要ります。
                //   PlayerSession.Next() は毎回 EnsureQueueFilled() を呼ぶので
                //   Queue の件数は減りません(「空になったら終わり」では止まらない)。
                //   足したものは末尾にあり、補充はその後ろに積まれるので、
                //   「足した時点の Queue の長さ」だけ進めば必ず届きます。
                if (!_session.Enqueue(mediaId)) return false;

                int steps = _session.Queue.Count;
                while (steps-- > 0 && !Same(_session.CurrentMediaId, mediaId))
                {
                    if (!_session.Next()) break;
                }
            }

            PlayCount++;
            bool started = _session.Play();
            Log($"{mediaId} を再生します -> {(started ? "開始" : "開始できませんでした")}");
            return started;
        }

        /// <summary>
        /// MediaId を指定して Queue の末尾に足す。
        /// </summary>
        /// <returns>足せたら true(カタログに無い ID なら false)。</returns>
        public bool Enqueue(string mediaId)
        {
            if (string.IsNullOrWhiteSpace(mediaId)) return false;

            bool added = _session.Enqueue(mediaId);
            if (!added)
            {
                Log($"Queue に足せません: {mediaId} はカタログにありません");
                return false;
            }

            EnqueueCount++;
            LastHandedOffId = mediaId;
            Log($"{mediaId} を Queue に足しました(いまは {_session.Queue.Count} 件)");
            return true;
        }

        // ───────── 内部 ─────────

        private static bool Same(string a, string b)
        {
            return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private void Log(string message)
        {
            _logger.Log($"[MediaLibrary] {message}");
        }

        /// <summary>Console 用のまとめ。</summary>
        public string Describe()
        {
            return $"選択={_library.SelectedMediaId ?? "なし"} "
                   + $"再生指示={PlayCount} 追加={EnqueueCount} "
                   + $"再生中={_session.CurrentMediaId ?? "なし"} [{_session.PlaybackState}]";
        }

        public override string ToString() => Describe();
    }
}
