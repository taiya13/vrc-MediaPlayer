using System;
using System.Collections.Generic;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;

namespace SmartMediaPlatform.Playlists
{
    /// <summary>
    /// Playlist と Queue をつなぎ、再生モードに応じて Queue を維持する。
    ///
    /// 責務は 3 つ:
    ///  1. プレイリストから Queue を生成する(<see cref="PlayPlaylist"/>)
    ///  2. 再生モードに応じて Queue を保つ(Repeat One / Repeat All / Shuffle)
    ///  3. 尽きそうなら Recommendation Engine で自動補充する(Auto Queue)
    ///
    /// 意図的に依存していないもの:
    ///  - Backend / MediaPlayer(再生手段を知らないので Music でも Video でも使える)
    ///  - UI
    /// 「今どの曲か」は呼び出し側から MediaId で渡してもらう。
    ///
    /// 呼び出し側は再生が進むたびに(あるいは毎フレーム)<see cref="Tick"/> を呼べばよい。
    /// Queue の変更は <see cref="IQueue.Version"/> で検知するので、無駄な処理は走らない。
    /// </summary>
    public sealed class AutoQueueService
    {
        private readonly IQueue _queue;
        private readonly IMediaCatalog _catalog;
        private readonly RecommendationEngine _engine;
        private readonly Random _random;

        /// <summary>直近におすすめとして積んだ曲。同じ曲を続けて推薦しないために覚えておく。</summary>
        private readonly List<string> _recentlyRecommended = new List<string>();

        /// <summary>
        /// 今の再生でプレイリストから Queue に出した曲。
        /// 一度再生し終えた曲を Normal / Shuffle で二度積まないために覚えておく
        /// (再度積むのは Repeat All の役割)。
        /// </summary>
        private readonly List<string> _dispatchedFromPlaylist = new List<string>();

        private int _lastHandledVersion = -1;

        public AutoQueueService(
            IQueue queue,
            IMediaCatalog catalog,
            RecommendationEngine engine,
            Random random = null)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _random = random ?? new Random();
        }

        /// <summary>ログ出力先。Backend に依存しないよう、単純な受け口にしてある。</summary>
        public Action<string> Logger { get; set; }

        /// <summary>再生モード。</summary>
        public PlaybackMode Mode { get; set; } = PlaybackMode.Normal;

        /// <summary>おすすめによる自動補充を使うか。</summary>
        public bool AutoQueueEnabled { get; set; } = true;

        /// <summary>この数を下回ったら補充する。</summary>
        public int MinimumQueueCount { get; set; } = 2;

        /// <summary>補充後に目指す Queue の長さ。</summary>
        public int TargetQueueCount { get; set; } = 5;

        /// <summary>同じ曲を続けて推薦しないために覚えておく件数。</summary>
        public int RecentMemory { get; set; } = 5;

        /// <summary>再生中のプレイリスト。<see cref="PlayPlaylist"/> で設定される。</summary>
        public Playlist CurrentPlaylist { get; private set; }

        /// <summary>いま自動補充が働く状態か(モードによる強制も加味する)。</summary>
        public bool IsAutoQueueActive =>
            Mode == PlaybackMode.ShuffleAutoQueue || AutoQueueEnabled;

        /// <summary>いまシャッフル再生か。</summary>
        public bool IsShuffle =>
            Mode == PlaybackMode.Shuffle || Mode == PlaybackMode.ShuffleAutoQueue;

        // ───────── Playlist → Queue ─────────

        /// <summary>
        /// プレイリストを再生するために Queue を作り直す。
        /// シャッフル系のモードならランダムな順に並べる。
        /// </summary>
        /// <returns>Queue に積んだ件数。</returns>
        public int PlayPlaylist(Playlist playlist)
        {
            CurrentPlaylist = playlist;
            _recentlyRecommended.Clear();
            _dispatchedFromPlaylist.Clear();
            _queue.Clear();

            if (playlist == null || playlist.IsEmpty)
            {
                Log("PlayPlaylist: プレイリストが空です");
                _lastHandledVersion = _queue.Version;
                return 0;
            }

            int added = EnqueueFromPlaylist();
            Log($"PlayPlaylist: {playlist.Name} -> Queue に {added} 曲を生成しました"
                + $" (Mode: {Mode}, AutoQueue: {IsAutoQueueActive})");

            _lastHandledVersion = _queue.Version;
            return added;
        }

        /// <summary>プレイリストの再生を終える(Queue はそのまま)。</summary>
        public void ClearPlaylist()
        {
            CurrentPlaylist = null;
            _recentlyRecommended.Clear();
            _dispatchedFromPlaylist.Clear();
        }

        // ───────── 維持 / 補充 ─────────

        /// <summary>
        /// Queue に変化があったときだけ維持処理を行う。毎フレーム呼んでよい。
        /// </summary>
        /// <param name="currentMediaId">いま再生中の曲。分からなければ null。</param>
        /// <returns>Queue に積んだ件数。</returns>
        public int Tick(string currentMediaId = null)
        {
            if (_queue.Version == _lastHandledVersion) return 0;

            int added = EnsureFilled(currentMediaId);
            _lastHandledVersion = _queue.Version;
            return added;
        }

        /// <summary>
        /// 再生モードに応じて Queue を保つ。必要なら補充する。
        /// </summary>
        /// <param name="currentMediaId">いま再生中の曲。分からなければ null。</param>
        /// <returns>Queue に積んだ件数。</returns>
        public int EnsureFilled(string currentMediaId = null)
        {
            switch (Mode)
            {
                case PlaybackMode.RepeatOne:
                    return MaintainRepeatOne(currentMediaId);

                case PlaybackMode.RepeatAll:
                    return MaintainRepeatAll();

                default:
                    return MaintainForward(currentMediaId);
            }
        }

        /// <summary>
        /// 同じ曲を繰り返す。次にも同じ曲が来るよう Queue を保つ。
        /// </summary>
        private int MaintainRepeatOne(string currentMediaId)
        {
            // 先頭が「いま鳴っている曲」。その後ろにもう 1 つ同じ曲を置いておけば、
            // 曲が終わって次へ進んだときにまた同じ曲が始まる。
            if (_queue.Count >= 2) return 0;

            string targetId = currentMediaId;
            if (string.IsNullOrEmpty(targetId))
            {
                var head = _queue.Peek();
                targetId = head != null ? head.MediaId : null;
            }
            if (string.IsNullOrEmpty(targetId)) return 0;

            var item = _catalog.FindById(targetId);
            if (item == null) return 0;

            _queue.Enqueue(item, QueueItemSource.Manual);
            Log($"RepeatOne: {targetId} をもう一度 Queue に積みました");
            return 1;
        }

        /// <summary>
        /// プレイリスト全体を繰り返す。尽きそうならプレイリストをもう一巡ぶん積む。
        /// </summary>
        private int MaintainRepeatAll()
        {
            if (_queue.Count > 1) return 0;
            if (CurrentPlaylist == null || CurrentPlaylist.IsEmpty) return 0;

            // 新しい一巡が始まるので、前の巡で出し終えた記録を消す。
            _dispatchedFromPlaylist.Clear();

            // 先頭(再生中)以外を積み直すと 1 曲だけのプレイリストで詰まるため、
            // 重複を許して丸ごと 1 巡ぶん追加する。
            int added = EnqueueFromPlaylist(allowDuplicates: true);
            Log($"RepeatAll: プレイリストをもう一巡ぶん積みました ({added} 曲)");
            return added;
        }

        /// <summary>
        /// Normal / Shuffle。尽きそうならプレイリストの残り、それでも足りなければおすすめで補う。
        /// </summary>
        private int MaintainForward(string currentMediaId)
        {
            if (_queue.Count >= MinimumQueueCount) return 0;

            int wanted = TargetQueueCount - _queue.Count;
            if (wanted <= 0) return 0;

            // まずプレイリストにまだ出していない曲があれば、それを使う。
            int added = EnqueueFromPlaylist(limit: wanted);

            // それでも足りなければ、おすすめで補充する(Auto Queue)。
            if (added < wanted && IsAutoQueueActive)
            {
                added += RefillFromRecommendation(currentMediaId, wanted - added);
            }

            return added;
        }

        // ───────── Recommendation 連携 ─────────

        /// <summary>
        /// おすすめで Queue を補う。
        ///
        /// 守っている決まり:
        ///  - いま再生中の曲を種にする(関連曲が来る)
        ///  - Queue にすでにある曲は積まない
        ///  - 直近に推薦した曲は積まない(同じ曲が続かない)
        ///  - プレイリスト内の曲を優先し、尽きたら Catalog 全体から選ぶ
        /// </summary>
        private int RefillFromRecommendation(string currentMediaId, int wanted)
        {
            if (wanted <= 0) return 0;

            string seedId = ResolveSeedId(currentMediaId);
            if (string.IsNullOrEmpty(seedId))
            {
                Log("AutoQueue: 種になる曲が見つかりませんでした");
                return 0;
            }

            // 既存 API をそのまま利用する。カタログ全件ぶん順位付けしてもらい、
            // 絞り込みはこちらで行う。
            var ranked = _engine.GetNextRecommendations(seedId, _catalog.Count);
            if (ranked.Length == 0) return 0;

            // 1) プレイリスト内を優先
            int added = EnqueueRanked(ranked, wanted, currentMediaId, playlistOnly: true);

            // 2) プレイリストが尽きたら Catalog 全体から
            if (added < wanted)
            {
                added += EnqueueRanked(ranked, wanted - added, currentMediaId, playlistOnly: false);
            }

            if (added > 0)
            {
                Log($"AutoQueue: {seedId} を種に {added} 曲を補充しました (Queue: {_queue.Count})");
            }
            return added;
        }

        /// <summary>
        /// 補充の種を決める。
        /// 「再生中の曲 → Queue の末尾 → プレイリストの先頭 → カタログからランダム」の順。
        /// </summary>
        private string ResolveSeedId(string currentMediaId)
        {
            if (!string.IsNullOrEmpty(currentMediaId) && _catalog.FindById(currentMediaId) != null)
                return currentMediaId;

            var tail = _queue.PeekAt(_queue.Count - 1);
            if (tail != null) return tail.MediaId;

            if (CurrentPlaylist != null && !CurrentPlaylist.IsEmpty)
            {
                for (int i = 0; i < CurrentPlaylist.Count; i++)
                {
                    var id = CurrentPlaylist.GetAt(i);
                    if (_catalog.FindById(id) != null) return id;
                }
            }

            var random = _catalog.GetRandom();
            return random != null ? random.Id : null;
        }

        private int EnqueueRanked(
            RecommendationResult[] ranked, int wanted, string currentMediaId, bool playlistOnly)
        {
            int added = 0;

            for (int i = 0; i < ranked.Length && added < wanted; i++)
            {
                var item = ranked[i].Item;
                string id = item.Id;

                if (playlistOnly && (CurrentPlaylist == null || !CurrentPlaylist.Contains(id)))
                    continue;

                // 同じ曲を連続で推薦しない
                if (string.Equals(id, currentMediaId, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsRecentlyRecommended(id)) continue;

                // Queue にある曲は推薦しない
                if (_queue.Contains(id)) continue;

                _queue.Enqueue(item, QueueItemSource.Recommendation);
                RememberRecommendation(id);
                added++;
            }

            return added;
        }

        private bool IsRecentlyRecommended(string mediaId)
        {
            for (int i = 0; i < _recentlyRecommended.Count; i++)
            {
                if (string.Equals(_recentlyRecommended[i], mediaId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void RememberRecommendation(string mediaId)
        {
            _recentlyRecommended.Add(mediaId);
            while (_recentlyRecommended.Count > RecentMemory && _recentlyRecommended.Count > 0)
            {
                _recentlyRecommended.RemoveAt(0);
            }
        }

        // ───────── Playlist からの積み込み ─────────

        /// <summary>
        /// プレイリストの曲を Queue に積む。シャッフル系のモードなら順をランダムにする。
        /// </summary>
        /// <param name="limit">最大何曲積むか(0 以下なら制限なし)。</param>
        /// <param name="allowDuplicates">
        /// すでに Queue にある曲・出し終えた曲も積むか(Repeat All の一巡追加で使う)。
        /// </param>
        private int EnqueueFromPlaylist(int limit = 0, bool allowDuplicates = false)
        {
            if (CurrentPlaylist == null || CurrentPlaylist.IsEmpty) return 0;

            var ids = new List<string>(CurrentPlaylist.MediaIds);
            if (IsShuffle) ShuffleInPlace(ids);

            int added = 0;
            foreach (var id in ids)
            {
                if (limit > 0 && added >= limit) break;

                if (!allowDuplicates)
                {
                    if (_queue.Contains(id)) continue;

                    // 一度再生し終えた曲を積み直さない。
                    // 積み直すのは Repeat All の役割で、Normal / Shuffle は一巡したら終わる。
                    if (ContainsIgnoreCase(_dispatchedFromPlaylist, id)) continue;
                }

                var item = _catalog.FindById(id);
                if (item == null)
                {
                    // カタログに無い ID は黙って読み飛ばす(データ不整合に強くする)
                    continue;
                }

                _queue.Enqueue(item, QueueItemSource.Manual);
                _dispatchedFromPlaylist.Add(id);
                added++;
            }

            return added;
        }

        private void ShuffleInPlace(List<string> ids)
        {
            for (int i = ids.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                var tmp = ids[i];
                ids[i] = ids[j];
                ids[j] = tmp;
            }
        }

        private static bool ContainsIgnoreCase(IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void Log(string message)
        {
            Logger?.Invoke($"[AutoQueue] {message}");
        }
    }
}
