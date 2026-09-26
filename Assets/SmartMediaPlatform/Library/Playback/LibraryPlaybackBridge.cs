using System;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog.Store;
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
    /// <b>境界を越えるのは <see cref="PlayableRef"/>(中身は MediaId だけ)</b>です。
    /// <c>MediaItem</c> も <c>DisplayMeta</c> も URL も渡しません。
    /// Phase3 から通している「URL を知るのは VideoBackend だけ」がそのまま保たれます。
    ///
    /// <b>Phase4-2:</b> 受け取る一覧を <see cref="IMediaListView"/> に広げました。
    /// カタログ全体(<see cref="MediaLibrary"/>)でも
    /// 関連動画(<see cref="RelatedMediaView"/>)でも、<b>同じこのクラスで再生へつなげます</b>。
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
        private readonly IMediaListView _library;
        private readonly PlayerSession _session;

        private IBackendLogger _logger;

        /// <param name="library">閲覧・選択している一覧(カタログ全体でも関連動画でも可)。</param>
        /// <param name="session">再生を任せるセッション。</param>
        /// <param name="logger">ログ出力先。</param>
        public LibraryPlaybackBridge(
            IMediaListView library,
            PlayerSession session,
            IBackendLogger logger = null)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _logger = logger ?? NullBackendLogger.Instance;
        }

        // ───────── 設定 ─────────

        /// <summary>これまでに再生を指示した回数(診断用)。</summary>
        public int PlayCount { get; private set; }

        /// <summary>これまでに Queue へ足した回数(診断用)。</summary>
        public int EnqueueCount { get; private set; }

        /// <summary>直近に渡した MediaId(診断用)。</summary>
        public string LastHandedOffId { get; private set; }

        /// <summary>つないでいる一覧。</summary>
        public IMediaListView Library => _library;

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
            var playable = _library.SelectedRef;
            if (!playable.IsValid)
            {
                Log("再生できません: 何も選ばれていません");
                return false;
            }
            return Play(playable);
        }

        /// <summary>
        /// <b><see cref="PlayableRef"/> を渡して再生する(Phase4-2 の入口)。</b>
        ///
        /// <see cref="PlayableRef"/> は <c>ICatalogStore</c> を通ったものだけが
        /// <c>IsValid</c> になるので、<b>カタログに無い ID が再生系まで流れません</b>。
        /// 中で運ばれるのは <c>MediaId</c> だけで、URL は入っていません。
        /// </summary>
        public bool Play(PlayableRef playable)
        {
            if (!playable.IsValid)
            {
                Log("再生できません: 指定が空です");
                return false;
            }
            return Play(playable.MediaId);
        }

        /// <summary><see cref="PlayableRef"/> を Queue の末尾に足す。</summary>
        public bool Enqueue(PlayableRef playable)
        {
            return playable.IsValid && Enqueue(playable.MediaId);
        }

        /// <summary>
        /// いま選んでいるものを Queue の末尾に足す(いまの再生は止めない)。
        /// </summary>
        /// <returns>足せたら true。</returns>
        public bool EnqueueSelected()
        {
            var playable = _library.SelectedRef;
            if (!playable.IsValid)
            {
                Log("Queue に足せません: 何も選ばれていません");
                return false;
            }
            return Enqueue(playable.MediaId);
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
        ///
        /// <b>再生エンジンの 2 つの仕様に合わせて組み立てています</b>
        /// (どちらも Phase1〜3 のまま。こちらが合わせる側です)。
        /// <list type="number">
        /// <item>
        /// <c>MediaPlayer.Play()</c> は<b>何も読み込んでいないときだけ</b> Queue の先頭を読みます。
        /// すでに何か鳴っている状態で呼んでも、鳴っているものが続くだけで切り替わりません。
        /// </item>
        /// <item>
        /// <c>Queue</c> は<b>先頭 = いま鳴っているもの</b>という約束で、
        /// <c>Next()</c> は<b>先頭を捨てて次を読み</b>ます。
        /// </item>
        /// </list>
        /// つまり切り替えたいものは<b>先頭の次(index 1)</b>に置いてから <c>Next()</c> する、
        /// というのが既存 API だけで成立する唯一の道です。
        /// 何も鳴っていないときは素直に<b>先頭(index 0)</b>へ置いて <c>Play()</c> します。
        /// </summary>
        /// <returns>再生を始められたら true。</returns>
        public bool Play(string mediaId)
        {
            if (string.IsNullOrWhiteSpace(mediaId)) return false;

            // すでにそれが鳴っているなら、わざわざ入れ直さない
            if (Same(_session.CurrentMediaId, mediaId))
            {
                LastHandedOffId = mediaId;
                return _session.IsPlaying || _session.Play();
            }

            var queue = _session.Queue;
            bool somethingIsLoaded = _session.CurrentMediaId != null;

            // Queue に無ければ足す。カタログに無い ID はここで弾かれる。
            if (!queue.Contains(mediaId) && !_session.Enqueue(mediaId))
            {
                Log($"再生できません: {mediaId} はカタログにありません");
                return false;
            }

            LastHandedOffId = mediaId;

            // 鳴っているなら「次」(index 1)、鳴っていないなら「先頭」(index 0)へ寄せる
            int wanted = somethingIsLoaded ? 1 : 0;
            int index = queue.IndexOf(mediaId);
            if (index > wanted) queue.Move(index, wanted);

            PlayCount++;

            bool started = somethingIsLoaded ? _session.Next() : _session.Play();

            // Next() は「直前が再生中/再生終了」のときだけ続けて鳴らす。
            // 一時停止や停止から切り替えた場合はここで始める。
            if (!_session.IsPlaying) started = _session.Play();

            Log($"{mediaId} を再生します -> {(started ? "開始" : "開始できませんでした")}");
            return started;
        }

        /// <summary>
        /// <b>いま選んでいるものを「次に再生」する</b>(Phase4-3)。
        /// いまの再生は止めず、Queue の <b>2 番目</b>(先頭の次)へ入れます。
        /// </summary>
        public bool PlayNextSelected()
        {
            var playable = _library.SelectedRef;
            if (!playable.IsValid)
            {
                Log("「次に再生」できません: 何も選ばれていません");
                return false;
            }
            return PlayNext(playable.MediaId);
        }

        /// <summary>
        /// <b>MediaId を「次に再生」する</b>(Phase4-3)。
        ///
        /// Queue は「先頭 = いま鳴っているもの」という約束なので、
        /// <b>index 1 が「次」</b>です。末尾に足してからそこへ動かします
        /// (<c>IQueue.Move</c> は Phase1-4 からある並べ替え用の API)。
        /// </summary>
        /// <returns>入れられたら true。</returns>
        public bool PlayNext(string mediaId)
        {
            if (string.IsNullOrWhiteSpace(mediaId)) return false;

            // すでにそれが鳴っているなら、次に入れる意味がない
            if (Same(_session.CurrentMediaId, mediaId)) return false;

            var queue = _session.Queue;
            if (!queue.Contains(mediaId) && !_session.Enqueue(mediaId))
            {
                Log($"「次に再生」できません: {mediaId} はカタログにありません");
                return false;
            }

            // 何も鳴っていなければ先頭が「次」。鳴っていれば先頭の次。
            int wanted = _session.CurrentMediaId != null ? 1 : 0;
            int index = queue.IndexOf(mediaId);
            if (index > wanted) queue.Move(index, wanted);

            EnqueueCount++;
            LastHandedOffId = mediaId;
            Log($"{mediaId} を次に再生します(Queue: {queue.Count} 件)");
            return true;
        }

        /// <summary><see cref="PlayableRef"/> を「次に再生」する。</summary>
        public bool PlayNext(PlayableRef playable)
        {
            return playable.IsValid && PlayNext(playable.MediaId);
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
