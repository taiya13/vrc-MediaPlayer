using System;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog.Store;
using SmartMediaPlatform.Session;

namespace SmartMediaPlatform.Library.Playback
{
    /// <summary>
    /// <b>「いま何を再生しているか」を UI 向けにまとめた窓。</b>Phase4-3 で追加。
    ///
    /// <code>
    /// PlayerSession.CurrentMediaId(string)
    ///   → ICatalogStore.GetDisplayMeta()
    ///   → DisplayMeta(タイトル・アーティスト・ジャンル・タグ。URL 無し)
    /// </code>
    ///
    /// <b>これが「PlayerSession は MediaId を中心に扱い、
    /// 再生情報は Catalog から取る」設計そのものです。</b>
    /// <c>PlayerSession</c> は文字列の ID しか持ちません。
    /// 画面に出す文字はすべてカタログから引き直すので、
    /// <b>セッションに表示用の情報を持たせずに済みます</b>。
    ///
    /// <b><c>MediaItem</c> を外へ出しません。</b>
    /// <c>PlayerSession.CurrentItem</c> は <c>MediaItem</c>(= <c>Url</c> 付き)を返しますが、
    /// この クラス はそれを使わず <see cref="ICatalogStore"/> から引き直します。
    /// おかげで UI から URL は見えません。
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public sealed class NowPlayingView
    {
        private readonly ICatalogStore _store;
        private readonly PlayerSession _session;

        private string _lastMediaId;

        /// <param name="store">表示情報を引く唯一の窓口。</param>
        /// <param name="session">見張るセッション。</param>
        public NowPlayingView(ICatalogStore store, PlayerSession session)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        // ───────── いま鳴っているもの ─────────

        /// <summary>再生中の MediaId。何も無ければ null。</summary>
        public string MediaId => _session.CurrentMediaId;

        /// <summary>
        /// 再生中の表示情報。何も無ければ null。
        /// <b>URL は入っていません。</b>
        /// </summary>
        public DisplayMeta Meta
        {
            get
            {
                string id = _session.CurrentMediaId;
                return id != null ? _store.GetDisplayMeta(id) : null;
            }
        }

        /// <summary>再生中のものを指す参照(再生系へ渡せる形)。</summary>
        public PlayableRef Playable
        {
            get
            {
                string id = _session.CurrentMediaId;
                return id != null ? _store.GetPlayableRef(id) : PlayableRef.None;
            }
        }

        /// <summary>何か鳴っている(あるいは読み込んでいる)か。</summary>
        public bool HasMedia => _session.CurrentMediaId != null;

        // ───────── 状態 ─────────

        /// <summary>利用者から見た再生状態。</summary>
        public PlaybackState State => _session.PlaybackState;

        /// <summary>バックエンドが報告している状態(低レベル)。</summary>
        public BackendState BackendState => _session.BackendState;

        /// <summary>再生中か。</summary>
        public bool IsPlaying => _session.IsPlaying;

        /// <summary>いまの再生位置(秒)。</summary>
        public float CurrentTime => _session.GetCurrentTime();

        /// <summary>
        /// 長さ(秒)。
        /// バックエンドが答えられないときは、カタログの値で埋めます
        /// (読み込み中でも UI に「4:22」と出せるように)。
        /// </summary>
        public float Duration
        {
            get
            {
                float fromBackend = _session.GetDuration();
                if (fromBackend > 0f) return fromBackend;

                var meta = Meta;
                return meta != null ? meta.DurationSeconds : 0f;
            }
        }

        /// <summary>0〜1 の進捗。長さが分からなければ 0。</summary>
        public float Progress
        {
            get
            {
                float fromBackend = _session.GetProgress();
                if (fromBackend > 0f) return fromBackend;

                float duration = Duration;
                return duration > 0f ? Clamp01(CurrentTime / duration) : 0f;
            }
        }

        /// <summary>シークできるか(バックエンドによる)。</summary>
        public bool CanSeek => _session.CanSeek();

        // ───────── 変化の検出 ─────────

        /// <summary>
        /// 前回の <see cref="Poll"/> から再生中のものが変わったか。
        ///
        /// UI が毎フレーム呼んで「変わったときだけ描き直す」ために使います
        /// (<c>IMediaListView</c> のような観測者を持たせるほどの情報量ではないため、
        ///  こちらは素直な問い合わせ方式にしてあります)。
        /// </summary>
        /// <returns>変わっていたら true。</returns>
        public bool Poll()
        {
            string current = _session.CurrentMediaId;
            if (current == _lastMediaId) return false;

            _lastMediaId = current;
            return true;
        }

        // ───────── 表示用の整形 ─────────

        /// <summary>「1:23 / 4:22」の形。</summary>
        public string FormatTime()
        {
            return MediaLibraryFormatter.FormatDuration((int)CurrentTime)
                   + " / " + MediaLibraryFormatter.FormatDuration((int)Duration);
        }

        /// <summary>「Neon Skyline / Aurora Drive」の形。何も無ければ「(再生していません)」。</summary>
        public string FormatTitle()
        {
            var meta = Meta;
            if (meta == null) return "(再生していません)";

            return string.IsNullOrEmpty(meta.Artist)
                ? meta.Title
                : $"{meta.Title} / {meta.Artist}";
        }

        /// <summary>Console 用のまとめ。</summary>
        public string Describe()
        {
            var meta = Meta;
            if (meta == null) return $"[{State}] (再生していません)";

            return $"[{State}] {meta.Title} / {meta.Artist} "
                   + $"[{meta.Type}] {FormatTime()} ({Progress:P0})";
        }

        public override string ToString() => Describe();

        private static float Clamp01(float value)
        {
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }
    }
}
