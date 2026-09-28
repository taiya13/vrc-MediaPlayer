using System;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;

namespace SmartMediaPlatform.AutoPlay
{
    /// <summary>
    /// <b>Phase3-4 の中心。おすすめ再生を「止まらないループ」にする司令塔。</b>
    ///
    /// Phase3-3 までで <c>Ended → 次へ</c> は動いていましたが、
    /// <b>失敗すると止まりました。</b>実在しない URL・アクセス拒否・読み込みタイムアウト —
    /// 動画は普通に失敗します。それでも流れ続けるようにするのがこの クラス です。
    ///
    /// <b>引き受けている 3 つの仕事</b>
    /// <list type="number">
    /// <item>
    /// <b>失敗からの復帰</b> — <c>Error</c> を受けたら、失敗した動画を記録して次の候補へ進む。
    /// <c>Ended</c> と同じように再生が続きます。
    /// </item>
    /// <item>
    /// <b>タイムアウトの見張り</b> — 読み込みが終わらないまま何の通知も来ない場合、
    /// <see cref="Tick"/> が見つけて次へ進めます(Backend が沈黙しても止まらない)。
    /// </item>
    /// <item>
    /// <b>暴走の歯止め</b> — 立て直しが連続して失敗したら
    /// <see cref="AutoPlayState.Failed"/> で降ります
    /// (候補を無限に試し続けて固まらないため)。1 本でも再生できれば数えなおします。
    /// </item>
    /// </list>
    ///
    /// <b>他の層の責務は増やしていません</b>
    /// <list type="bullet">
    /// <item>「次に何を再生するか」を決めるのは引き続き <see cref="PlayerSession"/></item>
    /// <item>「何を積むか」は <see cref="RecommendationQueueRefiller"/>(Queue 層)</item>
    /// <item>「積んでよいか」は <see cref="IPlaybackFilter"/></item>
    /// <item>この クラス は<b>失敗の記録と、次へ進める合図</b>だけを持ちます</item>
    /// </list>
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public sealed class AutoPlayController : IBackendObserver
    {
        private readonly PlayerSession _session;
        private readonly RecommendationPlaybackService _playback;
        private readonly PlaybackFailureTracker _failures;

        private IBackendLogger _logger;

        /// <summary>
        /// 1 回の立て直しで試す上限。
        /// URL が軒並み切れているときに、その場で延々と回り続けないための歯止め。
        /// </summary>
        private const int MaxRecoveryIterations = 32;

        private float _loadingSeconds;
        private bool _inRecovery;
        private bool _pendingRecovery;

        /// <param name="session">再生を任せるセッション。</param>
        /// <param name="playback">おすすめ補充の接続層。</param>
        /// <param name="failures">失敗した動画の記憶(ふるいにも入れておくこと)。</param>
        /// <param name="logger">ログ出力先。</param>
        public AutoPlayController(
            PlayerSession session,
            RecommendationPlaybackService playback,
            PlaybackFailureTracker failures,
            IBackendLogger logger = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _playback = playback ?? throw new ArgumentNullException(nameof(playback));
            _failures = failures ?? throw new ArgumentNullException(nameof(failures));
            _logger = logger ?? NullBackendLogger.Instance;
        }

        /// <summary>
        /// 一式をまとめて組み立てる。
        ///
        /// <b>ふるいの合成がここでの要点</b>です。
        /// 「再生できる種別か」と「さっき失敗していないか」は別の関心事なので、
        /// <see cref="CompositePlaybackFilter"/> で並べます。
        /// </summary>
        /// <param name="session">再生を任せるセッション。</param>
        /// <param name="catalog">事前生成カタログ。</param>
        /// <param name="engine">おすすめエンジン。</param>
        /// <param name="playableFilter">
        /// 再生できるかの判定。未指定なら <see cref="MediaTypePlaybackFilter"/>(Video / Live)。
        /// <c>BackendPlaybackFilter</c> を渡すと、登録済みバックエンドに合わせて自動で決まります。
        /// </param>
        /// <param name="logger">ログ出力先。</param>
        public static AutoPlayController Create(
            PlayerSession session,
            IMediaCatalog catalog,
            IRecommendationEngine engine,
            IPlaybackFilter playableFilter = null,
            IBackendLogger logger = null)
        {
            var failures = new PlaybackFailureTracker();
            var filter = new CompositePlaybackFilter(
                playableFilter ?? new MediaTypePlaybackFilter(),
                failures);

            var playback = new RecommendationPlaybackService(
                session, catalog, engine, filter, logger);

            return new AutoPlayController(session, playback, failures, logger);
        }

        // ───────── 設定 ─────────

        /// <summary>
        /// 立て直しが連続してこの回数を超えたら諦める。0 以下で無制限。
        /// 1 本でも再生できれば 0 に戻ります。
        /// </summary>
        public int MaxConsecutiveFailures { get; set; } = 8;

        /// <summary>
        /// 読み込みがこの秒数を超えても何も起きなければ、次の候補へ進む。0 以下で無効。
        ///
        /// <c>VRChatVideoBackend.LoadTimeoutSeconds</c> は Backend 自身の見張りで、
        /// そちらが働けば <c>Error</c> として届きます。こちらは
        /// <b>Backend が沈黙した場合の最後の砦</b>なので、少し長めにしてあります。
        /// </summary>
        public float LoadWatchdogSeconds { get; set; } = 30f;

        /// <summary>失敗した動画をどれだけ積み直さないか(<see cref="PlaybackFailureTracker"/>)。</summary>
        public PlaybackFailureTracker Failures => _failures;

        /// <summary>おすすめ補充の接続層。</summary>
        public RecommendationPlaybackService Playback => _playback;

        /// <summary>操作しているセッション。</summary>
        public PlayerSession Session => _session;

        // ───────── 状態 ─────────

        /// <summary>おすすめ再生ループから見た状態。</summary>
        public AutoPlayState State { get; private set; } = AutoPlayState.Idle;

        /// <summary>再生が始まった回数(= 実際に流せた本数)。</summary>
        public int PlayedCount { get; private set; }

        /// <summary>最後まで再生し終えた回数。</summary>
        public int EndedCount { get; private set; }

        /// <summary>失敗した回数(累計)。</summary>
        public int FailureCount { get; private set; }

        /// <summary>タイムアウトで打ち切った回数。</summary>
        public int TimeoutCount { get; private set; }

        /// <summary>失敗から立て直した回数。</summary>
        public int RecoveredCount { get; private set; }

        /// <summary>再生できないまま続いている失敗の回数。</summary>
        public int ConsecutiveFailures { get; private set; }

        /// <summary>直近に立て直したときの理由。</summary>
        public string LastRecoveryReason { get; private set; }

        /// <summary>いま再生している MediaId。</summary>
        public string CurrentMediaId => _session.CurrentMediaId;

        public void SetLogger(IBackendLogger logger)
        {
            _logger = logger ?? NullBackendLogger.Instance;
        }

        // ───────── 組み立て ─────────

        /// <summary>
        /// バックエンドを登録する。
        /// <b>この クラス を先に購読させます</b>(失敗を記録してから上位が動くように)。
        /// </summary>
        public void RegisterBackend(IMediaBackend backend)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));

            backend.AddObserver(this);
            _playback.RegisterBackend(backend);
        }

        // ───────── 操作 ─────────

        /// <summary>おすすめ再生を開始する。</summary>
        /// <returns>再生を始められたら true。</returns>
        public bool Start(string seedMediaId = null)
        {
            State = AutoPlayState.Starting;
            ConsecutiveFailures = 0;
            _loadingSeconds = 0f;

            if (_playback.Start(seedMediaId) <= 0)
            {
                State = AutoPlayState.Failed;
                Log("開始できません: 再生できる動画がありません");
                return false;
            }

            RefreshState();
            return true;
        }

        /// <summary>再生を止める(ループも止まる)。</summary>
        public void Stop()
        {
            _session.Stop();
            State = AutoPlayState.Stopped;
        }

        /// <summary>一時停止。</summary>
        public bool Pause()
        {
            bool ok = _session.Pause();
            RefreshState();
            return ok;
        }

        /// <summary>再開。</summary>
        public bool Resume()
        {
            bool ok = _session.Resume();
            RefreshState();
            return ok;
        }

        /// <summary>
        /// 毎フレーム呼ぶ。
        ///
        /// やることは 3 つだけです:
        /// 失敗の記憶の時計を進める / 読み込みを見張る / Queue を切らさない。
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            _failures.Tick(deltaSeconds);

            if (State == AutoPlayState.Stopped || State == AutoPlayState.Failed
                || State == AutoPlayState.Idle)
            {
                return;
            }

            WatchLoading(deltaSeconds);

            // Queue を先まで積んでおく(Ended のときに次が無いと再生が止まるため)
            _session.EnsureQueueFilled();

            RefreshState();
        }

        /// <summary>
        /// いまの動画を打ち切って次の候補へ進む。
        /// 失敗として記録したい場合は <paramref name="markAsFailure"/> を true に。
        /// </summary>
        public bool SkipToNext(string reason = null, bool markAsFailure = false)
        {
            if (markAsFailure)
            {
                string id = _session.CurrentMediaId;
                if (id != null) _failures.MarkFailed(id, reason);
            }

            return Recover(reason ?? "スキップ");
        }

        // ───────── Backend からの通知 ─────────

        /// <summary>
        /// <b>Ended も Error も、ここから先は同じ「次へ進む」に合流します。</b>
        /// 違いは「失敗として記録するかどうか」だけです。
        /// </summary>
        public void OnBackendEvent(BackendEvent backendEvent)
        {
            switch (backendEvent.Type)
            {
                case BackendEventType.Started:
                case BackendEventType.Resumed:
                    OnPlaybackStarted(backendEvent);
                    break;

                case BackendEventType.Ended:
                    EndedCount++;
                    _loadingSeconds = 0f;
                    // 次へ進めるのは PlayerSession の仕事。ここでは数えるだけ。
                    break;

                case BackendEventType.Error:
                    OnPlaybackFailed(backendEvent);
                    break;
            }

            RefreshState();
        }

        private void OnPlaybackStarted(BackendEvent backendEvent)
        {
            PlayedCount++;
            ConsecutiveFailures = 0;
            _loadingSeconds = 0f;

            // 再生できたので失敗の記憶を消す(次からは普通に候補に戻る)
            string id = backendEvent.Item != null ? backendEvent.Item.Id : _session.CurrentMediaId;
            if (id != null) _failures.MarkSucceeded(id);
        }

        private void OnPlaybackFailed(BackendEvent backendEvent)
        {
            string failedId = backendEvent.Item != null
                ? backendEvent.Item.Id
                : _session.CurrentMediaId;

            string reason = string.IsNullOrEmpty(backendEvent.Message)
                ? "再生に失敗しました"
                : backendEvent.Message;

            FailureCount++;
            ConsecutiveFailures++;
            LastRecoveryReason = reason;

            if (failedId != null)
            {
                _failures.MarkFailed(failedId, reason);
                Log($"失敗を記録しました: {failedId} ({reason})");
            }

            if (MaxConsecutiveFailures > 0 && ConsecutiveFailures > MaxConsecutiveFailures)
            {
                State = AutoPlayState.Failed;
                Log($"立て直しを {ConsecutiveFailures} 回続けて失敗したので停止します");
                return;
            }

            Recover(reason);
        }

        // ───────── 立て直し ─────────

        /// <summary>
        /// 次の候補へ進む。
        ///
        /// <b>再入に注意</b>:次の動画も即座に失敗すると、その場で <c>Error</c> が
        /// 返ってくることがあります(実機でも URL が連続で切れていれば起きます)。
        /// 再帰でスタックを積まないよう、<b>ループで処理</b>します。
        /// </summary>
        private bool Recover(string reason)
        {
            if (_inRecovery)
            {
                // すでに立て直し中。いまの周回が終わったらもう一度やる。
                _pendingRecovery = true;
                return false;
            }

            _inRecovery = true;
            try
            {
                bool ok = false;
                int guard = 0;

                do
                {
                    _pendingRecovery = false;
                    if (State == AutoPlayState.Failed) break;

                    ok = RecoverOnce(reason);
                }
                while (_pendingRecovery
                       && State != AutoPlayState.Failed
                       && ++guard < MaxRecoveryIterations
                       && (MaxConsecutiveFailures <= 0 || ConsecutiveFailures <= MaxConsecutiveFailures));

                return ok;
            }
            finally
            {
                _inRecovery = false;
                RefreshState();
            }
        }

        private bool RecoverOnce(string reason)
        {
            State = AutoPlayState.Recovering;
            RecoveredCount++;
            _loadingSeconds = 0f;
            LastRecoveryReason = reason;

            // 失敗したものはふるいで弾かれるので、積み直せば別の候補が入る
            if (!TryAdvance())
            {
                // 最後の手段:候補が全部「失敗済み」で塞がっているなら、記憶を捨ててやり直す。
                // 「全部ダメだから止まる」より「もう一度試す」ほうが、
                // 一時的な回線不調から自力で戻れる。
                if (_failures.BlockedCount > 0)
                {
                    Log("すべての候補が塞がっているため、失敗の記憶を消してやり直します");
                    _failures.Clear();
                    if (!TryAdvance())
                    {
                        Log($"立て直せませんでした({reason}): 積める候補がありません");
                        return false;
                    }
                }
                else
                {
                    Log($"立て直せませんでした({reason}): 積める候補がありません");
                    return false;
                }
            }

            // Ended と違い、Error のあとは MediaPlayer が自動では再生を始めない
            // (直前が Playing でも Ended でもないため)。ここで明示的に始める。
            if (!_session.IsPlaying) _session.Play();

            Log($"立て直しました({reason}) -> {_session.CurrentMediaId ?? "(none)"}");
            return _session.IsPlaying;
        }

        /// <summary>Queue を積み直してから次へ進む。進めたら true。</summary>
        private bool TryAdvance()
        {
            _session.EnsureQueueFilled();
            if (_session.Next()) return true;

            _session.EnsureQueueFilled();
            return _session.Next();
        }

        private void WatchLoading(float deltaSeconds)
        {
            bool loading = _session.BackendState == BackendState.Loading;
            if (!loading)
            {
                _loadingSeconds = 0f;
                return;
            }

            _loadingSeconds += deltaSeconds > 0f ? deltaSeconds : 0f;
            if (LoadWatchdogSeconds <= 0f || _loadingSeconds < LoadWatchdogSeconds) return;

            _loadingSeconds = 0f;
            TimeoutCount++;
            FailureCount++;
            ConsecutiveFailures++;

            string id = _session.CurrentMediaId;
            const string reason = "読み込みが終わりませんでした(タイムアウト)";
            if (id != null) _failures.MarkFailed(id, reason);

            LastRecoveryReason = reason;
            Log($"タイムアウト: {id ?? "(none)"} を打ち切ります");

            if (MaxConsecutiveFailures > 0 && ConsecutiveFailures > MaxConsecutiveFailures)
            {
                State = AutoPlayState.Failed;
                return;
            }

            Recover(reason);
        }

        // ───────── 状態の反映 ─────────

        private void RefreshState()
        {
            if (State == AutoPlayState.Failed
                || State == AutoPlayState.Stopped
                || State == AutoPlayState.Idle)
            {
                return;
            }

            switch (_session.BackendState)
            {
                case BackendState.Loading: State = AutoPlayState.Loading; break;
                case BackendState.Ready: State = AutoPlayState.Ready; break;
                case BackendState.Playing: State = AutoPlayState.Playing; break;
                case BackendState.Paused: State = AutoPlayState.Paused; break;
                default: break;   // Stopped / Ended / Error は遷移の途中なので触らない
            }
        }

        private void Log(string message)
        {
            _logger.Log($"[AutoPlay] {message}");
        }

        /// <summary>Console 用のまとめ。</summary>
        public string Describe()
        {
            return $"[{State}] now={CurrentMediaId ?? "(none)"} queue={_session.Queue.Count} "
                   + $"played={PlayedCount} ended={EndedCount} "
                   + $"failed={FailureCount}(timeout {TimeoutCount}) "
                   + $"recovered={RecoveredCount} consecutive={ConsecutiveFailures} "
                   + $"blocked={_failures.BlockedCount}";
        }

        public override string ToString() => Describe();
    }
}
