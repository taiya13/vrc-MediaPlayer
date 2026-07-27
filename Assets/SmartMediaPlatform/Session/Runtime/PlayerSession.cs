using System;
using System.Collections.Generic;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;

namespace SmartMediaPlatform.Session
{
    /// <summary>
    /// プレイヤー全体の状態を一元管理する中核。Catalog / Recommendation / Queue /
    /// Backend / Playback をまとめる<b>オーケストレーター</b>。
    ///
    /// 利用者はこのクラスだけを操作すれば再生できる
    /// (<see cref="MediaPlayer"/> や <see cref="BackendManager"/> を直接触る必要はない)。
    ///
    /// Backend の詳細は知らない:
    ///  - 具体的なバックエンド(AudioBackend / VideoBackend)の型を参照しない
    ///  - AudioSource などの再生手段にも触れない
    ///  - やり取りするのは <see cref="IMediaBackend"/> と <see cref="BackendEvent"/> という
    ///    抽象だけなので、Music でも Video でもそのまま動く
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public sealed class PlayerSession : IBackendObserver
    {
        private readonly MediaPlayer _player;
        private readonly IMediaCatalog _catalog;
        private readonly IRecommendationEngine _engine;
        private readonly Random _random;

        /// <summary>このセッションで流す曲。Queue の元になる。</summary>
        private readonly List<string> _tracks = new List<string>();

        /// <summary>すでに Queue へ出した曲。一巡したかの判定に使う。</summary>
        private readonly List<string> _dispatched = new List<string>();

        /// <summary>再生した曲の記録(新しいものが末尾)。</summary>
        private readonly List<string> _history = new List<string>();

        private readonly IReadOnlyList<string> _readOnlyTracks;
        private readonly IReadOnlyList<string> _readOnlyHistory;

        /// <summary>
        /// 実際に Queue へ積む処理。Phase3-4 で <see cref="IQueueRefiller"/> へ切り出しました。
        ///
        /// <b>PlayerSession の役割は増えていません。</b>
        /// もともとここが持っていた「おすすめで積む」手順を、
        /// プロジェクト全体で共有する実装へ委譲しただけです。
        /// 外から差し替えられるので、
        /// <b>利用側が <see cref="AutoQueueEnabled"/> を書き換える必要がなくなりました</b>
        /// (「補充の仕方」を渡せば済む)。
        /// 「直近に積んだ曲を覚えておく」記憶もこの中にあります。
        /// </summary>
        private IQueueRefiller _refiller;

        /// <summary>
        /// <see cref="_refiller"/> をこのセッションが自分で作ったか。
        ///
        /// 自作なら <see cref="RecentMemory"/> はセッションの設定なので毎回押し込みます
        /// (Phase2-3(B) の挙動をそのまま保つため)。
        /// 外から渡された補充は<b>渡した側の持ち物</b>なので、設定を上書きしません。
        /// </summary>
        private bool _ownsRefiller;

        private bool _exhausted;
        private IBackendLogger _logger;

        public PlayerSession(
            string id,
            MediaPlayer player,
            IMediaCatalog catalog,
            IRecommendationEngine engine,
            Random random = null,
            IBackendLogger logger = null,
            IQueueRefiller refiller = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("PlayerSession requires a non-empty id.", nameof(id));

            Id = id;
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _random = random ?? new Random();
            _logger = logger ?? NullBackendLogger.Instance;

            // 既定は Phase2-3(B) と同じ振る舞いの補充(ふるい無し・緩和無し・カタログ補完無し)。
            _ownsRefiller = refiller == null;
            _refiller = refiller ?? CreateDefaultRefiller();

            _readOnlyTracks = _tracks.AsReadOnly();
            _readOnlyHistory = _history.AsReadOnly();

            // 曲が終わったあと何を流すかはこのセッションが決めるので、
            // MediaPlayer 側の自動送りは切って二重に反応しないようにする。
            _player.AutoAdvanceOnEnded = false;

            foreach (var backend in _player.Manager.Backends) backend.AddObserver(this);
        }

        /// <summary>セッションを識別する ID。</summary>
        public string Id { get; }

        /// <summary>表示名。</summary>
        public string Name { get; set; }

        // ───────── 状態 ─────────

        /// <summary>いま再生中(または読み込み中)の MediaId。無ければ null。</summary>
        public string CurrentMediaId
        {
            get
            {
                var current = _player.GetCurrent();
                return current != null ? current.Id : null;
            }
        }

        /// <summary>いま再生中のメディア。無ければ null。</summary>
        public MediaItem CurrentItem => _player.GetCurrent();

        /// <summary>現在の Queue(読み取り用)。</summary>
        public IQueue Queue => _player.Queue;

        /// <summary>再生した曲の記録(新しいものが末尾)。</summary>
        public IReadOnlyList<string> History => _readOnlyHistory;

        /// <summary>このセッションで流す曲の一覧。</summary>
        public IReadOnlyList<string> Tracks => _readOnlyTracks;

        /// <summary>繰り返しの種類。</summary>
        public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;

        /// <summary>シャッフル再生するか。</summary>
        public bool ShuffleEnabled { get; set; }

        /// <summary>おすすめによる自動補充を使うか。</summary>
        public bool AutoQueueEnabled { get; set; } = true;

        /// <summary>
        /// Queue の補充の仕方。Phase3-4 で差し替え可能にしました。
        ///
        /// <b>これを渡すのが、利用側が補充に手を入れる正しいやり方です。</b>
        /// Phase3-3 では利用側が <see cref="AutoQueueEnabled"/> を false にして
        /// 自前で積んでいましたが、それは「セッションの設定を外から書き換える」形で、
        /// セッションの意思(自動補充する/しない)と補充の手段が混ざっていました。
        /// 手段だけを差し替えられるようにしたので、
        /// <see cref="AutoQueueEnabled"/> は本来の意味(補充するかどうか)のまま使えます。
        ///
        /// null を入れると既定の補充に戻ります。
        ///
        /// <b>渡した補充の設定はこのクラスが書き換えません。</b>
        /// <see cref="RecentMemory"/> を押し込むのは、自分で作った既定の補充のときだけです
        /// (渡した側が意図した設定を、セッションの既定値で潰さないため)。
        /// </summary>
        public IQueueRefiller QueueRefiller
        {
            get => _refiller;
            set
            {
                _ownsRefiller = value == null;
                _refiller = value ?? CreateDefaultRefiller();
            }
        }

        /// <summary>Phase2-3(B) と同じ振る舞いの補充を作る。</summary>
        private RecommendationQueueRefiller CreateDefaultRefiller()
        {
            return new RecommendationQueueRefiller(_catalog, _engine)
            {
                RecentMemory = RecentMemory,
                AllowRepeatWhenExhausted = false,
                AllowCatalogFallback = false,
            };
        }

        /// <summary>Backend が報告している状態(低レベル)。</summary>
        public BackendState BackendState => _player.GetState();

        /// <summary>セッションから見た再生状態。</summary>
        public PlaybackState PlaybackState
        {
            get
            {
                switch (_player.GetState())
                {
                    case Backend.BackendState.Playing: return PlaybackState.Playing;
                    case Backend.BackendState.Paused: return PlaybackState.Paused;
                    case Backend.BackendState.Stopped: return PlaybackState.Stopped;
                    case Backend.BackendState.Ready: return PlaybackState.Ready;
                    case Backend.BackendState.Loading: return PlaybackState.Ready;
                    case Backend.BackendState.Ended:
                        return _exhausted ? PlaybackState.Exhausted : PlaybackState.Stopped;
                    default:
                        return PlaybackState.Idle;
                }
            }
        }

        public bool IsPlaying => _player.IsPlaying();

        /// <summary>この数を下回ったら Queue を補充する。</summary>
        public int MinimumQueueCount { get; set; } = 2;

        /// <summary>補充後に目指す Queue の長さ。</summary>
        public int TargetQueueCount { get; set; } = 5;

        /// <summary>同じ曲を続けて推薦しないために覚えておく件数。</summary>
        public int RecentMemory { get; set; } = 5;

        /// <summary>履歴として保持する上限。</summary>
        public int MaxHistory { get; set; } = 50;

        public void SetLogger(IBackendLogger logger)
        {
            _logger = logger ?? NullBackendLogger.Instance;
            _player.SetLogger(_logger);
        }

        // ───────── バックエンドの登録 ─────────

        /// <summary>
        /// バックエンドを登録する。<see cref="MediaPlayer"/> への登録と、
        /// Ended を受け取るための購読をまとめて行う。
        /// </summary>
        public void RegisterBackend(IMediaBackend backend)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));

            _player.RegisterBackend(backend);
            backend.AddObserver(this);
        }

        // ───────── 曲の設定 / Queue 管理 ─────────

        /// <summary>
        /// このセッションで流す曲を設定し、Queue を作り直す。
        /// シャッフルが有効ならランダムな順に並べる。
        /// </summary>
        /// <returns>Queue に積んだ件数。</returns>
        public int SetTracks(IEnumerable<string> mediaIds)
        {
            _tracks.Clear();
            if (mediaIds != null)
            {
                foreach (var id in mediaIds)
                {
                    if (!string.IsNullOrWhiteSpace(id) && !ContainsIgnoreCase(_tracks, id))
                        _tracks.Add(id);
                }
            }

            return RebuildQueue();
        }

        /// <summary>いまの曲一覧から Queue を作り直す。</summary>
        public int RebuildQueue()
        {
            _dispatched.Clear();
            _refiller.ForgetRecent();
            _exhausted = false;
            Queue.Clear();

            int added = EnqueueFromTracks();
            Log($"Queue を作り直しました ({added} 曲, Repeat: {RepeatMode}, "
                + $"Shuffle: {ShuffleEnabled}, AutoQueue: {AutoQueueEnabled})");
            return added;
        }

        /// <summary>曲を 1 つ末尾に足す(Queue にも積む)。</summary>
        public bool Enqueue(string mediaId)
        {
            var item = _catalog.FindById(mediaId);
            if (item == null) return false;

            if (!ContainsIgnoreCase(_tracks, mediaId)) _tracks.Add(mediaId);

            Queue.Enqueue(item, QueueItemSource.Manual);
            _dispatched.Add(mediaId);
            _exhausted = false;
            return true;
        }

        /// <summary>Queue を空にする(曲一覧は残す)。</summary>
        public void ClearQueue()
        {
            Queue.Clear();
        }

        /// <summary>
        /// 再生モードに応じて Queue を保ち、足りなければ補充する。
        /// 曲が変わったタイミング、あるいは毎フレーム呼んでよい。
        /// </summary>
        /// <returns>Queue に積んだ件数。</returns>
        public int EnsureQueueFilled()
        {
            if (Queue.Count >= MinimumQueueCount) return 0;

            int wanted = TargetQueueCount - Queue.Count;
            if (wanted <= 0) return 0;

            // 1. まだ流していない曲があれば、それを使う
            int added = EnqueueFromTracks(wanted);

            // 2. Repeat All なら、もう一巡ぶん積み直す
            if (added < wanted && RepeatMode == RepeatMode.All && _tracks.Count > 0)
            {
                _dispatched.Clear();
                added += EnqueueFromTracks(wanted - added, allowDuplicates: true);
                if (added > 0) Log($"Repeat All: もう一巡ぶん積みました");
            }

            // 3. それでも足りなければ、おすすめで補う
            if (added < wanted && AutoQueueEnabled)
            {
                added += RefillFromRecommendation(wanted - added);
            }

            if (added > 0) _exhausted = false;
            return added;
        }

        // ───────── 再生制御(MediaPlayer へ委譲)─────────

        /// <summary>再生する。何も読み込んでいなければ Queue の先頭から始める。</summary>
        public bool Play()
        {
            EnsureQueueFilled();
            return _player.Play();
        }

        public bool Pause() => _player.Pause();

        public bool Resume() => _player.Resume();

        public bool Stop() => _player.Stop();

        public bool TogglePlayPause() => _player.TogglePlayPause();

        /// <summary>
        /// 次の曲へ進む。進む前に Queue を補充するので、
        /// 曲が尽きていてもおすすめで再生を続けられる。
        /// </summary>
        public bool Next()
        {
            var leaving = CurrentMediaId;

            EnsureQueueFilled();

            if (!_player.SkipNext())
            {
                _exhausted = true;
                Log($"Next: 次の曲がありません (現在: {leaving ?? "(none)"})");
                return false;
            }

            PushHistory(leaving);
            EnsureQueueFilled();

            Log($"Next: {leaving ?? "(none)"} -> {CurrentMediaId ?? "(none)"} ({PlaybackState})");
            return true;
        }

        /// <summary>前の曲へ戻る。履歴が無ければ false。</summary>
        public bool Previous()
        {
            if (!_player.SkipPrevious())
            {
                Log("Previous: 履歴がありません");
                return false;
            }

            PopHistory();
            _exhausted = false;

            Log($"Previous: -> {CurrentMediaId ?? "(none)"} ({PlaybackState})");
            return true;
        }

        public bool Seek(float normalizedPosition) => _player.Seek(normalizedPosition);

        public bool CanSeek() => _player.CanSeek();

        public float GetCurrentTime() => _player.GetCurrentTime();

        public float GetDuration() => _player.GetDuration();

        public float GetProgress() => _player.GetProgress();

        /// <summary>Console 表示用の 1 行サマリ。</summary>
        public string Describe()
        {
            return $"[{PlaybackState}] {CurrentMediaId ?? "(none)"}"
                   + $" | Queue {Queue.Count} | 履歴 {_history.Count}"
                   + $" | Repeat: {RepeatMode}, Shuffle: {ShuffleEnabled}, AutoQueue: {AutoQueueEnabled}";
        }

        public void ClearHistory()
        {
            _history.Clear();
        }

        // ───────── Backend からの通知 ─────────

        /// <summary>
        /// 曲が最後まで再生されたら、繰り返し設定に応じて次を決める。
        /// これが Queue / Recommendation / Playback をつなぐ中心。
        /// </summary>
        public void OnBackendEvent(BackendEvent backendEvent)
        {
            if (backendEvent.Type != BackendEventType.Ended) return;

            string endedId = backendEvent.Item != null ? backendEvent.Item.Id : CurrentMediaId;

            if (RepeatMode == RepeatMode.One)
            {
                // 同じ曲をもう一度。Queue は動かさない。
                Log($"Ended: {endedId} -> Repeat One なのでもう一度再生します");
                _player.Play();
                return;
            }

            Log($"Ended: {endedId} -> 次の曲へ");
            Next();
        }

        // ───────── 内部 ─────────

        /// <summary>
        /// 曲一覧から Queue に積む。シャッフルが有効なら順をランダムにする。
        /// </summary>
        /// <param name="limit">最大何曲積むか(0 以下なら制限なし)。</param>
        /// <param name="allowDuplicates">すでに出した曲も積むか(Repeat All 用)。</param>
        private int EnqueueFromTracks(int limit = 0, bool allowDuplicates = false)
        {
            if (_tracks.Count == 0) return 0;

            var ids = new List<string>(_tracks);
            if (ShuffleEnabled) ShuffleInPlace(ids);

            int added = 0;
            foreach (var id in ids)
            {
                if (limit > 0 && added >= limit) break;

                if (!allowDuplicates)
                {
                    if (Queue.Contains(id)) continue;

                    // 一度流し終えた曲は積み直さない。積み直すのは Repeat All の役割。
                    if (ContainsIgnoreCase(_dispatched, id)) continue;
                }

                var item = _catalog.FindById(id);
                if (item == null) continue;   // カタログに無い ID は読み飛ばす

                Queue.Enqueue(item, QueueItemSource.Manual);
                _dispatched.Add(id);
                added++;
            }

            return added;
        }

        /// <summary>
        /// おすすめで Queue を補う。
        ///  - いま再生中の曲を種にする
        ///  - Queue にすでにある曲は積まない
        ///  - 直近に推薦した曲・再生中の曲は積まない(同じ曲が続かない)
        /// </summary>
        private int RefillFromRecommendation(int wanted)
        {
            if (wanted <= 0) return 0;

            string seedId = ResolveSeedId();
            if (string.IsNullOrEmpty(seedId)) return 0;

            // 「直近何件を避けるか」はセッションの設定なので、毎回渡しておく。
            // ただし押し込むのは自分で作った補充だけ(渡された補充は渡した側の持ち物)。
            if (_ownsRefiller && _refiller is RecommendationQueueRefiller tuned)
            {
                tuned.RecentMemory = RecentMemory;
            }

            // 積み方そのものは共有実装に任せる(種を決めるのがこのクラスの仕事)
            int added = _refiller.Refill(Queue, seedId, wanted, CurrentMediaId);

            if (added > 0)
            {
                Log($"AutoQueue: {seedId} を種に {added} 曲を補充しました (Queue: {Queue.Count})");
            }
            return added;
        }

        /// <summary>
        /// 補充の種を決める。
        /// 「再生中の曲 → Queue の末尾 → 直近の履歴 → 曲一覧の先頭 → カタログからランダム」の順。
        /// </summary>
        private string ResolveSeedId()
        {
            string current = CurrentMediaId;
            if (!string.IsNullOrEmpty(current)) return current;

            var tail = Queue.PeekAt(Queue.Count - 1);
            if (tail != null) return tail.MediaId;

            if (_history.Count > 0) return _history[_history.Count - 1];

            for (int i = 0; i < _tracks.Count; i++)
            {
                if (_catalog.FindById(_tracks[i]) != null) return _tracks[i];
            }

            var random = _catalog.GetRandom();
            return random != null ? random.Id : null;
        }

        private void PushHistory(string mediaId)
        {
            if (string.IsNullOrEmpty(mediaId)) return;

            _history.Add(mediaId);
            while (_history.Count > MaxHistory && _history.Count > 0) _history.RemoveAt(0);
        }

        private void PopHistory()
        {
            if (_history.Count > 0) _history.RemoveAt(_history.Count - 1);
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

        private static bool ContainsIgnoreCase(List<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void Log(string message)
        {
            _logger.Log($"[Session:{Id}] {message}");
        }
    }
}
