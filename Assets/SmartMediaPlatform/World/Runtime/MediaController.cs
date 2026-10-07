using UnityEngine;

namespace SmartMediaPlatform.World
{
    /// <summary>
    /// <b>再生操作の窓口。</b>既定の <see cref="IMediaController"/> 実装。
    ///
    /// <b>再生の判断は 1 つも持っていません。</b>
    /// やっているのは <c>PlaybackFlow</c> と <c>PlayerSession</c> への
    /// <b>呼び出しの中継だけ</b>です。
    /// 次に何を再生するかは今までどおり <c>PlayerSession</c> が決めます。
    ///
    /// <b>なぜ中継が要るのか</b><br/>
    /// UI が <c>PlayerSession</c> を直接触ると、UI を差し替えるたびに
    /// 再生系の使い方を書き直すことになります。
    /// 窓口を挟んでおけば、<b>UI を差し替えても操作の意味が変わらず</b>、
    /// <b>操作を差し替えても UI が壊れません</b>。
    ///
    /// ボタンから直接呼べるよう、すべて引数なし / int 1 個にしてあります
    /// (uGUI の <c>Button.onClick</c> にそのまま繋げられます)。
    /// </summary>
    [AddComponentMenu("Smart Media Platform/Media Controller")]
    public sealed class MediaController : MonoBehaviour, IMediaController
    {
        private MediaPlayerContext _context;

        /// <summary>組み立て済みか。</summary>
        public bool IsReady => _context != null;

        public void Bind(MediaPlayerContext context)
        {
            _context = context;
        }

        // ───────── 一覧から ─────────

        public bool PlaySelected()
        {
            return _context != null && _context.Flow.LibraryPlayback.PlaySelected();
        }

        public bool PlayAt(int index)
        {
            return _context != null && _context.Flow.LibraryPlayback.PlayAt(index);
        }

        public bool EnqueueSelected()
        {
            return _context != null && _context.Flow.LibraryPlayback.EnqueueSelected();
        }

        public bool PlayNextSelected()
        {
            return _context != null && _context.Flow.LibraryPlayback.PlayNextSelected();
        }

        /// <summary>一覧の index 番目を選ぶ(再生はしない)。</summary>
        public bool Select(int index)
        {
            return _context != null && _context.Flow.Library.Select(index);
        }

        // ───────── 関連から ─────────

        public bool PlayRelatedAt(int index)
        {
            return _context != null && _context.Flow.RelatedPlayback.PlayAt(index);
        }

        // ───────── Queue から ─────────

        public bool JumpInQueueTo(int index)
        {
            return _context != null && _context.Flow.QueuePlayback.PlayAt(index);
        }

        public bool RemoveFromQueue(int index)
        {
            return _context != null && _context.Flow.Queue.RemoveAt(index);
        }

        /// <summary>いま鳴っているもの以外を Queue から外す。</summary>
        public int ClearUpcoming()
        {
            return _context != null ? _context.Flow.Queue.ClearUpcoming() : 0;
        }

        // ───────── 再生制御 ─────────

        public bool TogglePlayPause()
        {
            return _context != null && _context.Session.TogglePlayPause();
        }

        public bool Next()
        {
            return _context != null && _context.Session.Next();
        }

        public bool Previous()
        {
            return _context != null && _context.Session.Previous();
        }

        public bool Stop()
        {
            return _context != null && _context.Session.Stop();
        }

        public override string ToString() => "MediaController(PlaybackFlow への中継)";
    }
}
