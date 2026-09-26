using UdonSharp;
using UnityEngine;
using SmartMediaPlatform.Recommendation.Udon;

namespace SmartMediaPlatform.World.Udon
{
    /// <summary>
    /// <b>再生の判断をする中核。</b><c>PlayerSession</c> + <c>MediaQueue</c> +
    /// <c>LibraryPlaybackBridge</c> の Udon 版。
    ///
    /// <b>中身は <see cref="SmartMediaPlatform.World.UdonModel.PlaybackModel"/> の写しです。</b>
    /// あちらは純粋 C# で EditMode テスト済み、こちらはそれを UdonSharp の書き方へ
    /// 1 対 1 で移したものです(Phase1-2 の <c>ParallelMediaCatalog</c> →
    /// <c>UdonMediaCatalog</c>、Phase1-3 の <c>RecommendationEngine</c> →
    /// <c>UdonRecommendationEngine</c> と同じ手順)。
    /// <b>アルゴリズムを変えるときは必ず両方を直してください。</b>
    ///
    /// <b>責務は今までと同じ</b>です:
    /// <list type="bullet">
    /// <item>次に何を再生するかを決める</item>
    /// <item>再生予定(Queue)を保つ</item>
    /// <item>再生予定が空のとき、<see cref="UdonRecommendationEngine"/> に候補を出させる</item>
    /// </list>
    /// <b>実際の再生も、URL も、画面のことも知りません。</b>
    /// 再生してほしいものは <see cref="UdonVideoBackend"/> に頼むだけです。
    ///
    /// <b>Phase7-3:「再生中」と「再生予定」を分けました。</b>
    /// Phase7-2 まで「Queue の先頭 = 再生中」だったため、
    /// <b>選ぶだけで再生予定が増える / 再生中を消せない / 補充が勝手に埋める</b>が
    /// 構造的に起きていました。理由と新しい決まりごとは
    /// <see cref="SmartMediaPlatform.World.UdonModel.PlaybackModel"/> に書いてあります。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonPlayerSession : UdonSharpBehaviour
    {
        [Tooltip("表示用データの窓口(URL は見えない)")]
        public UdonCatalogStore Store;

        [Tooltip("次の候補を出すエンジン")]
        public UdonRecommendationEngine Recommendation;

        [Tooltip("実際に鳴らす相手")]
        public UdonVideoBackend Backend;

        [Tooltip("曲と曲をなめらかに繋ぐ担当(Phase7-5)。"
                 + "空なら今までどおり Backend へ直接読ませます")]
        public UdonCrossfadeCoordinator Crossfade;

        [Tooltip("その人の好み(お気に入り・履歴・再生回数)。Phase7-8。空でも動く")]
        public UdonUserProfile Profile;

        [Tooltip("「…」の中身(繰り返し・おやすみ・URL)。Phase7-9。空でも動く")]
        public UdonPlayerOptions Options;

        [Tooltip("再生予定を一周し続ける(Phase7-9)。"
                 + "取り出したものを後ろへ戻すので、予定が減りません")]
        public bool RepeatQueue;

        [Header("再生予定が空になったときの動き(Phase7-3 / 既定は Phase7-5 で変更)")]
        [Tooltip("曲が終わって再生予定が空だったときにどうするか。"
                 + "0 = 止まる / 1 = いまの 1 曲を繰り返す / 2 = おすすめで流し続ける(既定)。"
                 + "再生予定に何か入っているときは、必ずその先頭へ進みます")]
        [Range(0, 2)]
        public int EndBehaviour = EndBehaviourRecommend;

        [Tooltip("「次へ」を押したのに再生予定が空だったとき、おすすめから選んでよい。"
                 + "選んでも再生予定には積みません(そのまま次の曲になるだけ)")]
        public bool AutoQueueEnabled = true;

        [Header("そのほか")]
        [Tooltip("履歴として保持する上限")]
        public int MaxHistory = 32;

        [Tooltip("続けてこの回数だけ再生に失敗したら自動送りをやめる。0 で無制限。"
                 + "これが無いと「失敗 → 次へ → 失敗」が永遠に回る")]
        public int MaxConsecutiveErrors = 4;

        [Header("同期(Phase5-4)")]
        [Tooltip("動画が終わった / 失敗したときに自分で次へ進む。"
                 + "同期中は持ち主(Owner)だけ true にする")]
        public bool AutoAdvance = true;

        /// <summary>Queue に積める上限(Udon は固定長配列なので上限が要る)。</summary>
        public const int QueueCapacity = 64;

        /// <summary>止まる。<b>既定</b>。</summary>
        public const int EndBehaviourStop = 0;

        /// <summary>いまの 1 曲を繰り返す。</summary>
        public const int EndBehaviourRepeatOne = 1;

        /// <summary>おすすめで流し続ける。</summary>
        public const int EndBehaviourRecommend = 2;

        // ───────── 状態 ─────────

        /// <summary>これから流すものだけ。再生中は入らない(Phase7-3)。</summary>
        private int[] _queue;
        private int _queueCount;

        private int[] _history;
        private int _historyCount;

        /// <summary>いま鳴っている(鳴らそうとしている)もの。無ければ -1。</summary>
        private int _currentIndex = -1;

        private int _selectedIndex = -1;
        private bool _isPlaying;
        private bool _exhausted;
        private int _requestedIndex = -1;
        private int _loadCount;

        // 続けて失敗した回数。実際に鳴り始めたら 0 に戻す。
        private int _consecutiveErrors;

        private bool _initialized;

        void Start()
        {
            EnsureInitialized();
        }

        /// <summary>遅延初期化。Start の順序に関係なく安全に呼べるようにする。</summary>
        public void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            _queue = new int[QueueCapacity];
            _history = new int[QueueCapacity];
        }

        // ───────── 読み取り ─────────

        public int QueueCount
        {
            get { EnsureInitialized(); return _queueCount; }
        }

        public int GetQueueAt(int position)
        {
            EnsureInitialized();
            if (position < 0 || position >= _queueCount) return -1;
            return _queue[position];
        }

        /// <summary>
        /// <b>いま鳴っているもの。</b>無ければ -1。
        /// Phase7-3 から Queue とは別の場所を指します。
        /// </summary>
        public int CurrentIndex
        {
            get { return _currentIndex; }
        }

        public bool IsPlaying { get { return _isPlaying; } }

        public bool IsExhausted { get { return _exhausted; } }

        public int SelectedIndex { get { return _selectedIndex; } }

        public int HistoryCount { get { EnsureInitialized(); return _historyCount; } }

        public int GetHistoryAt(int position)
        {
            EnsureInitialized();
            if (position < 0 || position >= _historyCount) return -1;
            return _history[position];
        }

        public int RequestedIndex { get { return _requestedIndex; } }

        public int LoadCount { get { return _loadCount; } }

        /// <summary>
        /// <b>いま実際に音を出している動画プレイヤー。</b>Phase7-5。
        ///
        /// クロスフェードを使う構成では、A と B が入れ替わりながら鳴るので、
        /// <see cref="Backend"/>(= A 固定)を直接触ると<b>裏側を操作してしまいます</b>。
        /// 止める・鳴らす・時間を読む —— どれもここを通してください。
        /// </summary>
        public UdonVideoBackend ActiveBackend()
        {
            if (Crossfade != null)
            {
                UdonVideoBackend front = Crossfade.Front;
                if (front != null) return front;
            }
            return Backend;
        }

        /// <summary>いま鳴っているものの見出し。何も無ければ空文字。</summary>
        public string CurrentTitle
        {
            get
            {
                int index = CurrentIndex;
                if (index < 0 || Store == null) return "";
                return Store.GetTitle(index);
            }
        }

        // ───────── 一覧の選択 ─────────

        public bool Select(int catalogIndex)
        {
            if (!IsInCatalog(catalogIndex)) return false;

            _selectedIndex = catalogIndex;
            return true;
        }

        /// <summary>一覧の <paramref name="position"/> 番目を選ぶ。</summary>
        public bool SelectAt(int position)
        {
            if (Store == null) return false;
            return Select(Store.GetIndexAt(position));
        }

        public void ClearSelection()
        {
            _selectedIndex = -1;
        }

        // ───────── 再生 ─────────

        public bool Play()
        {
            EnsureInitialized();

            // 人が押したら、失敗の数え直し。
            // 「もう駄目」と諦めたあとでも、もう一度試せるようにするため。
            _consecutiveErrors = 0;

            if (_currentIndex < 0)
            {
                // まだ何も鳴っていないなら、再生予定の先頭を取り出して始める。
                if (_queueCount == 0) return false;
                if (!TakeFromQueue(0)) return false;
            }
            else if (_requestedIndex != _currentIndex)
            {
                Load(_currentIndex);
            }
            else
            {
                UdonVideoBackend active = ActiveBackend();
                if (active != null) active.Play();
            }

            _isPlaying = true;
            _exhausted = false;
            return true;
        }

        public bool Stop()
        {
            EnsureInitialized();
            if (_currentIndex < 0) return false;

            _isPlaying = false;

            // 混ぜている最中に止められたら、混ぜるのもやめる。
            if (Crossfade != null) Crossfade.CancelFade();

            UdonVideoBackend active = ActiveBackend();
            if (active != null) active.Stop();
            return true;
        }

        public bool TogglePlayPause()
        {
            EnsureInitialized();
            if (_currentIndex < 0 && _queueCount == 0) return false;

            if (_isPlaying)
            {
                _isPlaying = false;
                UdonVideoBackend active = ActiveBackend();
                if (active != null) active.Pause();
                return true;
            }
            return Play();
        }

        /// <summary>
        /// <b>次へ進む(人が押したとき)。</b>
        /// 再生予定に何かあれば必ずその先頭へ。空のときだけ、
        /// <see cref="AutoQueueEnabled"/> ならおすすめから 1 件選びます
        /// (選んでも<b>再生予定には積みません</b>)。
        /// </summary>
        public bool Next()
        {
            EnsureInitialized();

            // 最後まで聴かずに送ったなら、それは「飛ばした」ということ(Phase7-8)。
            if (Profile != null && _currentIndex >= 0) Profile.NoteSkipped(_currentIndex);

            if (_queueCount > 0) return TakeFromQueue(0);

            int pick = AutoQueueEnabled ? PickRecommendation() : -1;
            if (pick < 0)
            {
                _exhausted = true;
                return false;
            }

            return MoveTo(pick);
        }

        /// <summary>前へ戻る。履歴が無ければ false。</summary>
        public bool Previous()
        {
            EnsureInitialized();
            if (_historyCount == 0) return false;

            int previous = _history[_historyCount - 1];
            _historyCount--;

            // いま鳴っていたものは、戻ったあとの「次」になる。
            if (_currentIndex >= 0) InsertAt(0, _currentIndex);

            _currentIndex = previous;
            Load(previous);

            _isPlaying = true;
            _exhausted = false;
            return true;
        }

        // ───────── 一覧 / 関連から再生 ─────────

        /// <summary>
        /// <paramref name="catalogIndex"/> を今すぐ再生する。
        ///
        /// <b>再生予定には触りません</b>(Phase7-3)。
        /// 一覧から 1 曲選んだだけで再生予定が増えるのは、
        /// 人から見れば「勝手に追加された」ようにしか見えないためです。
        /// 選んだものが再生予定に入っていた場合だけ、そこからは外します
        /// (いま流し始めたものが「これから流すもの」に残っていたら二重表示になる)。
        /// </summary>
        public bool PlayAt(int catalogIndex)
        {
            EnsureInitialized();
            if (!IsInCatalog(catalogIndex)) return false;

            if (_currentIndex == catalogIndex)
            {
                // すでにこれが鳴っている。止まっていたら鳴らし直すだけ。
                if (_isPlaying) return true;
                return Play();
            }

            _consecutiveErrors = 0;
            return MoveTo(catalogIndex);
        }

        /// <summary>一覧の <paramref name="position"/> 番目を再生する。</summary>
        public bool PlayVisibleAt(int position)
        {
            if (Store == null) return false;
            return PlayAt(Store.GetIndexAt(position));
        }

        /// <summary>選んでいるものを再生する。</summary>
        public bool PlaySelected()
        {
            return PlayAt(_selectedIndex);
        }

        public bool Enqueue(int catalogIndex)
        {
            EnsureInitialized();
            if (!IsInCatalog(catalogIndex)) return false;
            if (IndexInQueue(catalogIndex) >= 0) return false;
            if (_queueCount >= QueueCapacity) return false;

            _queue[_queueCount] = catalogIndex;
            _queueCount++;
            _exhausted = false;

            // おすすめを裏で仕込んでいたら、予定のほうを優先し直してもらう(Phase7-5)。
            if (Crossfade != null) Crossfade.NotifyQueueChanged();
            return true;
        }

        /// <summary>選んでいるものを Queue の末尾へ積む。</summary>
        public bool EnqueueSelected()
        {
            return Enqueue(_selectedIndex);
        }

        /// <summary>再生予定の先頭に割り込ませる(再生はしない)。</summary>
        public bool PlayNext(int catalogIndex)
        {
            EnsureInitialized();
            if (!IsInCatalog(catalogIndex)) return false;

            int position = IndexInQueue(catalogIndex);
            if (position >= 0)
            {
                if (position != 0) Move(position, 0);
                return true;
            }

            if (_queueCount >= QueueCapacity) return false;

            InsertAt(0, catalogIndex);
            _exhausted = false;

            if (Crossfade != null) Crossfade.NotifyQueueChanged();
            return true;
        }

        /// <summary>選んでいるものを次に割り込ませる。</summary>
        public bool PlayNextSelected()
        {
            return PlayNext(_selectedIndex);
        }

        // ───────── Queue の操作 ─────────

        /// <summary>
        /// 再生予定の <paramref name="position"/> 番目へ飛ぶ。飛び越したものは外れます。
        /// Phase7-3 から<b>先頭(0 番目)も指定できます</b>
        /// (再生中は再生予定に入っていないため)。
        /// </summary>
        public bool JumpTo(int position)
        {
            EnsureInitialized();
            if (position < 0 || position >= _queueCount) return false;

            return TakeFromQueue(position);
        }

        /// <summary>
        /// 再生予定から外す。Phase7-3 から<b>どの位置でも外せます</b>
        /// (以前は先頭 = 再生中だったので外せず、「消しても消えない」が起きていました)。
        /// </summary>
        public bool RemoveFromQueue(int position)
        {
            EnsureInitialized();
            if (position < 0 || position >= _queueCount) return false;

            RemoveAt(position);
            return true;
        }

        /// <summary>
        /// <b>再生予定を空にする。</b>いま鳴っているものはそのまま流れ続けます。
        /// </summary>
        public int ClearUpcoming()
        {
            EnsureInitialized();

            int removed = _queueCount;
            _queueCount = 0;
            return removed;
        }

        /// <summary>再生予定を空にして、再生も止める。</summary>
        public void ClearQueue()
        {
            EnsureInitialized();
            _queueCount = 0;
            _currentIndex = -1;
            _requestedIndex = -1;
            _isPlaying = false;
        }

        public int IndexInQueue(int catalogIndex)
        {
            EnsureInitialized();
            for (int i = 0; i < _queueCount; i++)
            {
                if (_queue[i] == catalogIndex) return i;
            }
            return -1;
        }

        // ───────── 動画プレイヤーからの知らせ ─────────

        /// <summary>続けて失敗した回数(診断用)。</summary>
        public int ConsecutiveErrors { get { return _consecutiveErrors; } }

        /// <summary>実際に鳴り始めた。ここで失敗の数を戻す。</summary>
        /// <summary>
        /// <b>カタログに無い URL が鳴り始めた。</b>Phase7-9。
        /// 「いま鳴っているもの」を空にします —— カタログの何番でもないので、
        /// 一覧が別の曲を鳴っていることにしてしまわないためです。
        /// </summary>
        public void NotifyExternalPlayback()
        {
            EnsureInitialized();

            if (_currentIndex >= 0) PushHistory(_currentIndex);

            _currentIndex = -1;
            _requestedIndex = -1;
            _isPlaying = true;
            _exhausted = false;
        }

        public void NotifyStarted()
        {
            _consecutiveErrors = 0;

            // ── 実際に鳴り始めたときだけ履歴に積む(Phase7-8)。
            //
            //    <b>「読み込みを頼んだ」ではなく「鳴った」で数えます。</b>
            //    読み込みに失敗した曲や、鳴る前に飛ばした曲が履歴に残ると、
            //    おすすめが「聴いていない曲」を避け始めます。
            if (Profile != null && _currentIndex >= 0) Profile.NotePlayed(_currentIndex);
        }

        /// <summary>
        /// 最後まで再生された。<see cref="UdonVideoBackend"/> から呼ばれる。
        ///
        /// <list type="number">
        /// <item>再生予定に何かあれば<b>必ず</b>その先頭へ進み、再生予定から外す</item>
        /// <item>空なら <see cref="EndBehaviour"/> に従う(既定は停止)</item>
        /// </list>
        ///
        /// <b><see cref="AutoAdvance"/> が false のときは何もしません。</b>
        /// 動画の終了イベントは<b>全員の手元で別々に起きる</b>ので、
        /// 同期中に全員が次へ進むと、人によって違うものが鳴り始めます。
        /// 進むのは持ち主(Owner)だけにして、残りは同期で追いつきます。
        ///
        /// <see cref="SmartMediaPlatform.World.UdonModel.PlaybackModel.NotifyEnded"/> の写しです。
        /// </summary>
        public void NotifyEnded()
        {
            if (!AutoAdvance) return;
            EnsureInitialized();

            // ── 混ぜている最中に飛んできた「終わりました」は、
            //    <b>古いほうの曲</b>のものです(Phase7-5)。
            //    そのまま次へ進めると、裏でもう鳴っている曲を飛ばして
            //    <b>1 曲抜けます</b>。ここは Coordinator に締めてもらいます。
            if (Crossfade != null && Crossfade.IsBusy)
            {
                if (Crossfade.FinishNow()) return;

                // 裏がまだ鳴っていなかった。今までどおりの道で進む。
            }

            // ── おやすみタイマーが「この曲で」なら、ここで止まる(Phase7-9)。
            if (Options != null && Options.ShouldStopAfterTrack())
            {
                _isPlaying = false;

                UdonVideoBackend stopping = ActiveBackend();
                if (stopping != null) stopping.Stop();
                return;
            }

            // ── 「あとで流す」に積まれた URL があれば、そちらが先(Phase7-9)。
            //    人が明示的に積んだものなので、おすすめより優先します。
            if (Options != null && Options.TryPlayNextExternal()) return;

            if (_queueCount > 0)
            {
                TakeFromQueue(0);
                return;
            }

            if (EndBehaviour == EndBehaviourRepeatOne && _currentIndex >= 0)
            {
                Load(_currentIndex);
                _isPlaying = true;
                return;
            }

            if (EndBehaviour == EndBehaviourRecommend)
            {
                int pick = PickRecommendation();
                if (pick >= 0)
                {
                    MoveTo(pick);
                    return;
                }
            }

            // 既定 = 停止。
            _isPlaying = false;
            _exhausted = true;

            UdonVideoBackend active = ActiveBackend();
            if (active != null) active.Stop();
        }

        /// <summary>
        /// 再生に失敗した。壊れているものを飛ばして次へ送る。
        ///
        /// <b><see cref="MaxConsecutiveErrors"/> 回続けて失敗したら、そこで諦めます。</b>
        /// 諦めないと、失敗するたびに次を読み込み、その読み込みがまた失敗し …… と
        /// 無限に回ります。VRChat は読み込み回数を制限していて、制限に掛かった
        /// 読み込みは<b>即座に失敗して返る</b>ので、この輪はフレーム単位で回り、
        /// <b>クライアントごと固まります</b>(Phase5-5 で実際に起きました)。
        ///
        /// <b>失敗のときだけは、おすすめを使いません。</b>
        /// 壊れた URL を飛ばすのが目的で、勝手に別の曲を流し始めるためではないからです。
        ///
        /// <see cref="SmartMediaPlatform.World.UdonModel.PlaybackModel.NotifyError"/> の写しです。
        /// </summary>
        public void NotifyError()
        {
            if (!AutoAdvance) return;
            EnsureInitialized();

            _consecutiveErrors++;

            if (MaxConsecutiveErrors > 0 && _consecutiveErrors > MaxConsecutiveErrors)
            {
                _isPlaying = false;
                _exhausted = true;

                Debug.LogWarning("[UdonPlayerSession] 続けて " + _consecutiveErrors
                                 + " 回失敗したので自動送りをやめます。"
                                 + "URL が正しいか確認してください。", gameObject);
                return;
            }

            if (_queueCount > 0)
            {
                TakeFromQueue(0);
                return;
            }

            _isPlaying = false;
            _exhausted = true;
        }

        // ───────── 同期(Phase5-4)─────────

        /// <summary>
        /// <b>受け取った状態をそのまま当てる。</b>
        /// <see cref="SmartMediaPlatform.World.UdonModel.PlaybackModel.ApplySyncedState"/> の写しです。
        ///
        /// <b>ここは何も判断しません。</b>次に何を再生するかを決めるのは
        /// 今までどおり<b>持ち主(Owner)側のこのクラス</b>で、
        /// 受け取る側はその結果を映すだけです。補充も再生もしません
        /// (実際に動画を読ませるのは <see cref="UdonSyncCoordinator"/> の仕事)。
        ///
        /// <b>中身を検査しません。</b>持ち主がすでに検査したものなので、
        /// ここで弾くと<b>人によって Queue が違う</b>という一番困る形になります。
        /// </summary>
        public void ApplySyncedState(int[] queue, bool isPlaying, int loadedIndex)
        {
            EnsureInitialized();

            int count = queue == null ? 0 : queue.Length;
            if (count > QueueCapacity) count = QueueCapacity;

            for (int i = 0; i < count; i++) _queue[i] = queue[i];
            _queueCount = count;

            // 持ち主が読み込ませているものが、そのまま「再生中」になる(Phase7-3)。
            _currentIndex = loadedIndex;

            _isPlaying = isPlaying;
            if (isPlaying) _exhausted = false;

            _requestedIndex = loadedIndex;
        }

        /// <summary>いまの Queue を写し取る。持ち主が配るために使う。</summary>
        public int[] SnapshotQueue()
        {
            EnsureInitialized();

            int[] snapshot = new int[_queueCount];
            for (int i = 0; i < _queueCount; i++) snapshot[i] = _queue[i];
            return snapshot;
        }

        // ───────── 内部 ─────────

        /// <summary>
        /// 再生予定の <paramref name="position"/> 番目を取り出して再生する。
        /// <b>そこまでの曲は飛ばしたものとして外します</b>(履歴には積む)。
        /// <see cref="SmartMediaPlatform.World.UdonModel.PlaybackModel"/> の同名メソッドの写しです。
        /// </summary>
        private bool TakeFromQueue(int position)
        {
            if (position < 0 || position >= _queueCount) return false;

            int next = _queue[position];

            // 履歴は聴いた順に積む。先に終わったのは「いま鳴っていたもの」で、
            // そのあとに「飛び越した曲」が続く。
            if (_currentIndex >= 0 && _currentIndex != next) PushHistory(_currentIndex);
            for (int i = 0; i < position; i++) PushHistory(_queue[i]);

            // 取り出した位置までを再生予定から外す(飛び越したぶんも一緒に消える)。
            for (int i = position + 1; i < _queueCount; i++) _queue[i - position - 1] = _queue[i];
            _queueCount -= position + 1;

            // ── 再生予定を一周し続けるなら、取り出したものを後ろへ戻す(Phase7-9)。
            //    <b>飛び越したぶんは戻しません。</b>飛ばしたのは人の意思なので、
            //    戻すと「消したのにまた出てくる」になります。
            if (RepeatQueue && _queueCount < QueueCapacity)
            {
                _queue[_queueCount] = next;
                _queueCount++;
            }

            _currentIndex = next;
            Load(next);

            _isPlaying = true;
            _exhausted = false;
            return true;
        }

        /// <summary>いま鳴っているものを履歴へ送り、<paramref name="next"/> へ移る。</summary>
        private bool MoveTo(int next)
        {
            if (!IsInCatalog(next)) return false;

            if (_currentIndex >= 0 && _currentIndex != next) PushHistory(_currentIndex);

            // これから流すものが再生予定にも残っていると二重に見えるので外す。
            int duplicate = IndexInQueue(next);
            if (duplicate >= 0) RemoveAt(duplicate);

            _currentIndex = next;
            Load(next);

            _isPlaying = true;
            _exhausted = false;
            return true;
        }

        /// <summary>
        /// <b>おすすめを 1 件だけ選ぶ。</b>いま鳴っているものと、
        /// すでに再生予定にあるものは選びません。無ければ -1。
        ///
        /// <b>ここで選んだものを再生予定へ積むことはしません</b>(Phase7-3)。
        /// 積んでいたのが「勝手に追加され続ける」の原因でした。
        /// 種は「再生中 → 直近の履歴 → 一覧の先頭」の順です。
        /// </summary>
        private int PickRecommendation()
        {
            if (Recommendation == null || Store == null) return -1;

            int seed = ResolveSeedIndex();
            if (seed < 0) return -1;

            string seedId = Store.GetId(seed);
            if (seedId == null || seedId.Length == 0) return -1;

            // 使えない候補(再生中・再生予定・さっき聴いたもの)があるので多めに出させる。
            // ── 発見の仕組みを通す(Phase7-8)。
            //
            //    以前はここだけ Phase7-3 の重み付き合計を使っていたので、
            //    <b>おすすめタブと自動再生で別のものが出ていました</b>。
            //    再生予定を渡すのは、engine 側で下げてもらうためです。
            int count = Recommendation.GetRecommendations(
                seedId, _queueCount + RecentSkipDepth + 8, _queue, _queueCount);
            if (count <= 0) return -1;

            // ── 1 周目:さっき聴いたばかりのものを避けて、<b>ランダムに</b>選ぶ。
            //
            //    <b>先頭から順に選んではいけません。</b>
            //    「A に似ている B」は、たいてい「B に似ている A」でもあるので、
            //    いつも 1 位を取ると A → B → A → B と往復します。
            //    直近に聴いたものを避けたうえで、残りから等確率で引きます。
            int pick = PickRandomUsable(count, true);
            if (pick >= 0) return pick;

            // ── 2 周目:それでも見つからないなら、さっき聴いたものも許す。
            //
            //    曲が少ないカタログでは、避けているだけで候補が尽きます。
            //    <b>止まるより、少し前に聴いた曲でも流れ続けるほうがまし</b>です。
            pick = PickRandomUsable(count, false);
            if (pick >= 0) return pick;

            // ── 3 周目:おすすめが 1 件も使えないときは、カタログから引く。
            //
            //    <b>ここが無いと、同じ曲を鳴らし直すか止まるかしかありません。</b>
            //    「再生予定が空になったら、おすすめから次を流す」という約束は、
            //    おすすめが尽きた日も守られていなければ意味がないので、
            //    最後は<b>カタログの中の別の曲</b>まで下がって探します。
            return PickAnyOtherFromCatalog();
        }

        /// <summary>
        /// <b>使える候補から 1 つを等確率で引く。</b>Phase7-6。
        ///
        /// 使える候補が何個あるかは最後まで見ないと分からないので、
        /// 配列に貯めずに<b>リザーバー抽出</b>で選びます
        /// (<see cref="SmartMediaPlatform.Recommendation.UdonModel.RecommendationScoringModel.TakeAsRandomPick"/>
        /// の写しです)。
        /// </summary>
        private int PickRandomUsable(int count, bool avoidRecent)
        {
            int seen = 0;
            int chosen = -1;

            for (int i = 0; i < count; i++)
            {
                int candidate = Recommendation.GetResultIndex(i);
                if (!IsUsableRecommendation(candidate)) continue;
                if (avoidRecent && WasPlayedRecently(candidate)) continue;

                seen++;
                if (seen <= 1 || Random.Range(0, seen) == 0) chosen = candidate;
            }

            return chosen;
        }

        /// <summary>
        /// カタログの中から、いま鳴っているもの・再生予定のものを除いて 1 つ引く。
        /// <b>おすすめが尽きたときの最後の砦</b>です。
        /// </summary>
        private int PickAnyOtherFromCatalog()
        {
            if (Store == null || Store.Catalog == null) return -1;

            int total = Store.Catalog.Count;
            int seen = 0;
            int chosen = -1;

            for (int i = 0; i < total; i++)
            {
                if (i == _currentIndex) continue;
                if (IndexInQueue(i) >= 0) continue;

                seen++;
                if (seen <= 1 || Random.Range(0, seen) == 0) chosen = i;
            }

            return chosen;
        }

        /// <summary>おすすめとして使えるか(カタログにあり、いま鳴っておらず、予定にも無い)。</summary>
        private bool IsUsableRecommendation(int candidate)
        {
            if (!IsInCatalog(candidate)) return false;
            if (candidate == _currentIndex) return false;
            if (IndexInQueue(candidate) >= 0) return false;
            return true;
        }

        /// <summary>
        /// <b>直近この数だけ聴いたものは、おすすめで選ばない。</b>Phase7-5。
        /// 大きくしすぎると、曲が少ないカタログで候補が尽きます
        /// (尽きたときは 2 周目で拾うので、止まりはしません)。
        /// </summary>
        [Tooltip("おすすめで自動再生するとき、直近この曲数は選び直さない")]
        public int RecentSkipDepth = 8;

        private bool WasPlayedRecently(int catalogIndex)
        {
            if (RecentSkipDepth <= 0) return false;

            int from = _historyCount - RecentSkipDepth;
            if (from < 0) from = 0;

            for (int i = from; i < _historyCount; i++)
            {
                if (_history[i] == catalogIndex) return true;
            }
            return false;
        }

        // ───────── クロスフェード用の覗き見(Phase7-5)─────────

        /// <summary>
        /// <b>次に流れるものを、状態を変えずに教える。</b>
        ///
        /// クロスフェードは曲が終わる<b>前</b>に次を読み始める必要があります。
        /// ところが「次へ進む」(<see cref="Next"/>)を先に呼ぶと、
        /// まだ前の曲が鳴っているのに履歴も再生予定も動いてしまいます。
        /// <b>覗くだけ</b>と<b>実際に進む</b>を分けているのはそのためです。
        ///
        /// 実際に進むときは <see cref="CommitAdvanceTo"/> を呼びます。
        /// </summary>
        /// <returns>次の catalog index。無ければ -1。</returns>
        public int PeekNextIndex()
        {
            EnsureInitialized();

            // 再生予定が最優先。ここは Next() と同じ順番でなければならない。
            if (_queueCount > 0) return _queue[0];

            if (!AutoQueueEnabled) return -1;
            if (EndBehaviour != EndBehaviourRecommend) return -1;

            return PickRecommendation();
        }

        /// <summary>
        /// <b>覗いておいた次の曲へ、実際に移る。</b>
        ///
        /// <b>動画は読み込ませません。</b>クロスフェードでは、
        /// もう裏で読み終わって鳴っているものへ移るからです
        /// (ここで読み込ませると、鳴っているものを読み直してしまいます)。
        /// </summary>
        /// <returns>移れたら true。</returns>
        public bool CommitAdvanceTo(int catalogIndex)
        {
            EnsureInitialized();
            if (!IsInCatalog(catalogIndex)) return false;

            if (_currentIndex >= 0 && _currentIndex != catalogIndex)
            {
                PushHistory(_currentIndex);
            }

            // 再生予定に入っていたぶんは、そこから外す。
            int inQueue = IndexInQueue(catalogIndex);
            if (inQueue >= 0) RemoveAt(inQueue);

            _currentIndex = catalogIndex;

            // 読み込みは済んでいるので、頼まない。数だけ合わせる。
            _requestedIndex = catalogIndex;
            _loadCount++;

            _isPlaying = true;
            _exhausted = false;
            return true;
        }

        private int ResolveSeedIndex()
        {
            if (_currentIndex >= 0) return _currentIndex;
            if (_historyCount > 0) return _history[_historyCount - 1];
            if (Store != null && Store.Count > 0) return Store.GetIndexAt(0);
            return -1;
        }

        private void Load(int catalogIndex)
        {
            _requestedIndex = catalogIndex;
            _loadCount++;

            // ── クロスフェードを使う構成なら、そちらに任せる(Phase7-5)。
            //
            //    ここを通るのは<b>「いますぐ切り替える」道</b>だけです
            //    (人が曲を選んだ・次へを押した・失敗して飛ばした)。
            //    混ぜながら移るときは Coordinator が裏で読み込んでから
            //    CommitAdvanceTo を呼ぶので、ここは通りません。
            if (Crossfade != null)
            {
                Crossfade.PlayImmediate(catalogIndex);
                return;
            }

            if (Backend != null) Backend.LoadAndPlay(catalogIndex);
        }

        private bool IsInCatalog(int catalogIndex)
        {
            if (catalogIndex < 0) return false;
            if (Store == null || Store.Catalog == null) return false;
            return catalogIndex < Store.Catalog.Count;
        }

        private void PushHistory(int catalogIndex)
        {
            if (catalogIndex < 0) return;

            if (_historyCount >= MaxHistory || _historyCount >= QueueCapacity)
            {
                for (int i = 1; i < _historyCount; i++) _history[i - 1] = _history[i];
                _historyCount--;
            }

            _history[_historyCount] = catalogIndex;
            _historyCount++;
        }

        private void RemoveAt(int position)
        {
            for (int i = position + 1; i < _queueCount; i++) _queue[i - 1] = _queue[i];
            _queueCount--;
        }

        private bool InsertAt(int position, int catalogIndex)
        {
            if (_queueCount >= QueueCapacity) return false;

            for (int i = _queueCount; i > position; i--) _queue[i] = _queue[i - 1];
            _queue[position] = catalogIndex;
            _queueCount++;
            return true;
        }

        private void Move(int from, int to)
        {
            if (from == to || from < 0 || from >= _queueCount) return;
            if (to < 0 || to >= _queueCount) return;

            int value = _queue[from];
            if (from < to)
            {
                for (int i = from; i < to; i++) _queue[i] = _queue[i + 1];
            }
            else
            {
                for (int i = from; i > to; i--) _queue[i] = _queue[i - 1];
            }
            _queue[to] = value;
        }
    }
}
