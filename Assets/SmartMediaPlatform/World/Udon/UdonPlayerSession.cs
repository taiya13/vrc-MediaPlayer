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
    /// <item>Queue を保つ(先頭 = いま鳴っているもの)</item>
    /// <item>足りなければ <see cref="UdonRecommendationEngine"/> に候補を出させる</item>
    /// </list>
    /// <b>実際の再生も、URL も、画面のことも知りません。</b>
    /// 再生してほしいものは <see cref="UdonVideoBackend"/> に頼むだけです。
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

        [Header("Queue の保ち方(PlayerSession と同じ既定値)")]
        [Tooltip("この数を下回ったら補充する")]
        public int MinimumQueueCount = 2;

        [Tooltip("補充後に目指す Queue の長さ")]
        public int TargetQueueCount = 5;

        [Tooltip("履歴として保持する上限")]
        public int MaxHistory = 32;

        [Tooltip("おすすめによる自動補充を使う")]
        public bool AutoQueueEnabled = true;

        [Tooltip("続けてこの回数だけ再生に失敗したら自動送りをやめる。0 で無制限。"
                 + "これが無いと「失敗 → 次へ → 失敗」が永遠に回る")]
        public int MaxConsecutiveErrors = 4;

        [Header("同期(Phase5-4)")]
        [Tooltip("動画が終わった / 失敗したときに自分で次へ進む。"
                 + "同期中は持ち主(Owner)だけ true にする")]
        public bool AutoAdvance = true;

        /// <summary>Queue に積める上限(Udon は固定長配列なので上限が要る)。</summary>
        public const int QueueCapacity = 64;

        // ───────── 状態 ─────────

        private int[] _queue;
        private int _queueCount;

        private int[] _history;
        private int _historyCount;

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

        /// <summary>いま読み込んでいるもの(= Queue の先頭)。無ければ -1。</summary>
        public int CurrentIndex
        {
            get { EnsureInitialized(); return _queueCount > 0 ? _queue[0] : -1; }
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
            if (_queueCount == 0) return false;

            // 人が押したら、失敗の数え直し。
            // 「もう駄目」と諦めたあとでも、もう一度試せるようにするため。
            _consecutiveErrors = 0;

            if (_requestedIndex != _queue[0]) Load(_queue[0]);
            else if (Backend != null) Backend.Play();

            _isPlaying = true;
            _exhausted = false;
            return true;
        }

        public bool Stop()
        {
            EnsureInitialized();
            if (_queueCount == 0) return false;

            _isPlaying = false;
            if (Backend != null) Backend.Stop();
            return true;
        }

        public bool TogglePlayPause()
        {
            EnsureInitialized();
            if (_queueCount == 0) return false;

            if (_isPlaying)
            {
                _isPlaying = false;
                if (Backend != null) Backend.Pause();
                return true;
            }
            return Play();
        }

        /// <summary>次へ進む。進む前に Queue を補充するので、尽きていても続けられる。</summary>
        public bool Next()
        {
            EnsureInitialized();
            EnsureQueueFilled();

            if (_queueCount <= 1)
            {
                _exhausted = true;
                return false;
            }

            PushHistory(_queue[0]);
            RemoveAt(0);

            Load(_queue[0]);
            _isPlaying = true;
            _exhausted = false;

            EnsureQueueFilled();
            return true;
        }

        /// <summary>前へ戻る。履歴が無ければ false。</summary>
        public bool Previous()
        {
            EnsureInitialized();
            if (_historyCount == 0) return false;

            int previous = _history[_historyCount - 1];
            _historyCount--;

            if (!InsertAt(0, previous)) return false;

            Load(_queue[0]);
            _isPlaying = true;
            _exhausted = false;
            return true;
        }

        // ───────── 一覧 / 関連から再生 ─────────

        /// <summary>
        /// <paramref name="catalogIndex"/> を今すぐ再生する。
        /// <c>LibraryPlaybackBridge.Play(mediaId)</c> の手順そのまま:
        /// Queue に積んでから「先頭の次」へ動かして <see cref="Next"/> する
        /// (先頭 = 再生中 という不変条件を壊さないため)。
        /// </summary>
        public bool PlayAt(int catalogIndex)
        {
            EnsureInitialized();
            if (!IsInCatalog(catalogIndex)) return false;

            if (_queueCount > 0 && _queue[0] == catalogIndex)
            {
                if (_isPlaying) return true;
                return Play();
            }

            bool somethingIsLoaded = _queueCount > 0;

            if (IndexInQueue(catalogIndex) < 0 && !Enqueue(catalogIndex)) return false;

            int wanted = somethingIsLoaded ? 1 : 0;
            int position = IndexInQueue(catalogIndex);
            if (position > wanted) Move(position, wanted);

            if (!somethingIsLoaded) return Play();

            bool started = Next();
            if (!_isPlaying) started = Play();
            return started;
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
            return true;
        }

        /// <summary>選んでいるものを Queue の末尾へ積む。</summary>
        public bool EnqueueSelected()
        {
            return Enqueue(_selectedIndex);
        }

        /// <summary>いま鳴っているものの次に割り込ませる(再生はしない)。</summary>
        public bool PlayNext(int catalogIndex)
        {
            EnsureInitialized();
            if (!IsInCatalog(catalogIndex)) return false;
            if (_queueCount > 0 && _queue[0] == catalogIndex) return false;

            int wanted = _queueCount > 0 ? 1 : 0;

            int position = IndexInQueue(catalogIndex);
            if (position < 0)
            {
                if (!Enqueue(catalogIndex)) return false;
                position = IndexInQueue(catalogIndex);
            }

            if (position != wanted) Move(position, wanted);
            return true;
        }

        /// <summary>選んでいるものを次に割り込ませる。</summary>
        public bool PlayNextSelected()
        {
            return PlayNext(_selectedIndex);
        }

        // ───────── Queue の操作 ─────────

        /// <summary>Queue の <paramref name="position"/> 番目へ飛ぶ。先頭は指定できない。</summary>
        public bool JumpTo(int position)
        {
            EnsureInitialized();
            if (position <= 0 || position >= _queueCount) return false;

            if (position != 1) Move(position, 1);
            return Next();
        }

        /// <summary>Queue から外す。先頭(= いま鳴っているもの)は外せない。</summary>
        public bool RemoveFromQueue(int position)
        {
            EnsureInitialized();
            if (position <= 0 || position >= _queueCount) return false;

            RemoveAt(position);
            return true;
        }

        /// <summary>いま鳴っているもの以外を Queue から外す。</summary>
        public int ClearUpcoming()
        {
            EnsureInitialized();
            if (_queueCount <= 1) return 0;

            int removed = _queueCount - 1;
            _queueCount = 1;
            return removed;
        }

        public void ClearQueue()
        {
            EnsureInitialized();
            _queueCount = 0;
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

        /// <summary>
        /// 足りなければおすすめで補充する。
        /// 判断は <c>PlayerSession.EnsureQueueFilled()</c> と同じ
        /// (下限を割ったときだけ、目標まで積む)。
        /// </summary>
        public int EnsureQueueFilled()
        {
            EnsureInitialized();
            if (!AutoQueueEnabled) return 0;
            if (_queueCount >= MinimumQueueCount) return 0;

            int wanted = TargetQueueCount - _queueCount;
            if (wanted <= 0) return 0;

            int[] candidates = BuildCandidates(wanted);
            if (candidates == null) return 0;

            int added = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (added >= wanted) break;
                if (Enqueue(candidates[i])) added++;
            }

            if (added > 0) _exhausted = false;
            return added;
        }

        // ───────── 動画プレイヤーからの知らせ ─────────

        /// <summary>続けて失敗した回数(診断用)。</summary>
        public int ConsecutiveErrors { get { return _consecutiveErrors; } }

        /// <summary>実際に鳴り始めた。ここで失敗の数を戻す。</summary>
        public void NotifyStarted()
        {
            _consecutiveErrors = 0;
        }

        /// <summary>
        /// 最後まで再生された。<see cref="UdonVideoBackend"/> から呼ばれる。
        ///
        /// <b><see cref="AutoAdvance"/> が false のときは何もしません。</b>
        /// 動画の終了イベントは<b>全員の手元で別々に起きる</b>ので、
        /// 同期中に全員が次へ進むと、人によって違うものが鳴り始めます。
        /// 進むのは持ち主(Owner)だけにして、残りは同期で追いつきます。
        /// </summary>
        public void NotifyEnded()
        {
            if (!AutoAdvance) return;
            if (!Next()) _isPlaying = false;
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
        /// <see cref="SmartMediaPlatform.World.UdonModel.PlaybackModel.NotifyError"/> の写しです。
        /// </summary>
        public void NotifyError()
        {
            if (!AutoAdvance) return;

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

            if (!Next()) _isPlaying = false;
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
        /// 補充の候補を作る。種は「再生中 → Queue の末尾 → 直近の履歴 → 一覧の先頭」の順
        /// (<c>PlayerSession.ResolveSeedId()</c> と同じ)。
        /// </summary>
        private int[] BuildCandidates(int wanted)
        {
            if (Recommendation == null || Store == null) return null;

            int seed = ResolveSeedIndex();
            if (seed < 0) return null;

            string seedId = Store.GetId(seed);
            if (seedId == null || seedId.Length == 0) return null;

            // 積めない候補(すでに Queue にあるもの)があるので多めに出させる
            int count = Recommendation.GetNextRecommendations(seedId, wanted + _queueCount + 4);
            if (count <= 0) return null;

            int[] result = new int[count];
            for (int i = 0; i < count; i++) result[i] = Recommendation.GetResultIndex(i);
            return result;
        }

        private int ResolveSeedIndex()
        {
            if (_queueCount > 0) return _queue[0];
            if (_historyCount > 0) return _history[_historyCount - 1];
            if (Store != null && Store.Count > 0) return Store.GetIndexAt(0);
            return -1;
        }

        private void Load(int catalogIndex)
        {
            _requestedIndex = catalogIndex;
            _loadCount++;

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
