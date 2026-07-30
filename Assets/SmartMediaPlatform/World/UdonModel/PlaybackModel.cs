namespace SmartMediaPlatform.World.UdonModel
{
    /// <summary>
    /// <b>Udon 形の再生モデル。Phase5-2 の「正典」。</b>
    ///
    /// <b>なぜこれがあるのか</b><br/>
    /// アップロードした VRChat ワールドでは <c>UdonSharpBehaviour</c> しか動きません。
    /// ところが UdonSharp は interface・ジェネリック・<c>List</c>・<c>Dictionary</c>・例外を
    /// 使えないので、これまでの <c>PlayerSession</c> / <c>MediaQueue</c> /
    /// <c>LibraryPlaybackBridge</c> をそのまま持ち込めません。
    ///
    /// そこで Phase1-2(<c>ParallelMediaCatalog</c> → <c>UdonMediaCatalog</c>)、
    /// Phase1-3(<c>RecommendationEngine</c> → <c>UdonRecommendationEngine</c>)と同じ手を使います。
    /// <b>Udon で書ける形のまま、まず純粋 C# で書いて EditMode で検証し、
    /// それを 1 対 1 で UdonSharp へ写す</b>という手順です。
    /// このクラスがその「まず書くほう」で、写した先が <c>UdonPlayerSession</c> です。
    ///
    /// <b>Udon で書ける形</b>とは:
    /// <list type="bullet">
    /// <item>interface を使わない</item>
    /// <item><c>List</c> / <c>Dictionary</c> を使わない(固定長配列 + 件数)</item>
    /// <item>例外を投げない(<c>bool</c> を返す)</item>
    /// <item>メディアは <b>catalog index(int)</b> で扱う(カスタムクラスを持ち回らない)</item>
    /// </list>
    ///
    /// <b>責務は今までと同じ</b>です。ここが決めるのは
    /// 「次に何を再生するか」「Queue をどう保つか」だけで、
    /// <b>実際の再生も、カタログの中身も、URL も知りません</b>。
    /// 再生してほしいものは <see cref="RequestedIndex"/> に出すだけで、
    /// それを動画プレイヤーへ渡すのは呼び出し側(実機では <c>UdonVideoBackend</c>)の仕事です。
    ///
    /// <b>Queue の不変条件</b>は Phase3 から変わりません —
    /// <b>先頭がいま鳴っているもの</b>です。
    /// </summary>
    public sealed class PlaybackModel
    {
        // ───────── 設定 ─────────

        /// <summary>この数を下回ったら Queue を補充する。</summary>
        public int MinimumQueueCount = 2;

        /// <summary>補充後に目指す Queue の長さ。</summary>
        public int TargetQueueCount = 5;

        /// <summary>履歴として保持する上限。</summary>
        public int MaxHistory = 32;

        /// <summary>Queue に積める上限(Udon では固定長配列なので上限が要る)。</summary>
        public const int QueueCapacity = 64;

        // ───────── 状態 ─────────

        private readonly int _catalogCount;

        private readonly int[] _queue = new int[QueueCapacity];
        private int _queueCount;

        private readonly int[] _history = new int[QueueCapacity];
        private int _historyCount;

        private int _selectedIndex = -1;
        private bool _isPlaying;
        private bool _exhausted;

        private int _requestedIndex = -1;
        private int _loadCount;

        public PlaybackModel(int catalogCount)
        {
            _catalogCount = catalogCount < 0 ? 0 : catalogCount;
        }

        /// <summary>カタログの件数。</summary>
        public int CatalogCount { get { return _catalogCount; } }

        /// <summary>Queue の長さ。</summary>
        public int QueueCount { get { return _queueCount; } }

        /// <summary>Queue の <paramref name="position"/> 番目の catalog index。無ければ -1。</summary>
        public int GetQueueAt(int position)
        {
            if (position < 0 || position >= _queueCount) return -1;
            return _queue[position];
        }

        /// <summary>いま読み込んでいるもの(= Queue の先頭)。無ければ -1。</summary>
        public int CurrentIndex { get { return _queueCount > 0 ? _queue[0] : -1; } }

        /// <summary>鳴っているか。</summary>
        public bool IsPlaying { get { return _isPlaying; } }

        /// <summary>次に流すものが尽きたか。</summary>
        public bool IsExhausted { get { return _exhausted; } }

        /// <summary>一覧で選んでいるもの。無ければ -1。</summary>
        public int SelectedIndex { get { return _selectedIndex; } }

        /// <summary>履歴の件数。</summary>
        public int HistoryCount { get { return _historyCount; } }

        /// <summary>履歴の <paramref name="position"/> 番目(0 が古い)。</summary>
        public int GetHistoryAt(int position)
        {
            if (position < 0 || position >= _historyCount) return -1;
            return _history[position];
        }

        /// <summary>
        /// <b>いま「読み込め」と頼んでいるもの。</b>
        /// 実機ではこれを見て動画プレイヤーへ URL を渡します。
        /// </summary>
        public int RequestedIndex { get { return _requestedIndex; } }

        /// <summary>読み込みを頼んだ回数。同じものを頼み直したかの判定に使う。</summary>
        public int LoadCount { get { return _loadCount; } }

        // ───────── 一覧の選択 ─────────

        /// <summary>一覧の <paramref name="catalogIndex"/> 番目を選ぶ(再生はしない)。</summary>
        public bool Select(int catalogIndex)
        {
            if (catalogIndex < 0 || catalogIndex >= _catalogCount) return false;

            _selectedIndex = catalogIndex;
            return true;
        }

        /// <summary>選択を外す。</summary>
        public void ClearSelection()
        {
            _selectedIndex = -1;
        }

        // ───────── 再生 ─────────

        /// <summary>
        /// 再生する。何も読み込んでいなければ Queue の先頭から始める。
        /// <c>PlayerSession.Play()</c> と同じ意味。
        /// </summary>
        public bool Play()
        {
            if (_queueCount == 0) return false;

            if (_requestedIndex != _queue[0]) Load(_queue[0]);
            _isPlaying = true;
            _exhausted = false;
            return true;
        }

        /// <summary>止める。読み込んでいるものはそのまま。</summary>
        public bool Stop()
        {
            if (_queueCount == 0) return false;

            _isPlaying = false;
            return true;
        }

        /// <summary>鳴っていれば止め、止まっていれば鳴らす。</summary>
        public bool TogglePlayPause()
        {
            if (_queueCount == 0) return false;

            if (_isPlaying)
            {
                _isPlaying = false;
                return true;
            }
            return Play();
        }

        /// <summary>
        /// 次へ進む。<c>PlayerSession.Next()</c> と同じで、
        /// <b>進む前に Queue を補充する</b>ので、曲が尽きていてもおすすめで続けられる。
        /// </summary>
        /// <param name="candidates">補充に使ってよい catalog index の候補。無ければ null。</param>
        public bool Next(int[] candidates)
        {
            EnsureQueueFilled(candidates);

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

            EnsureQueueFilled(candidates);
            return true;
        }

        /// <summary>前へ戻る。履歴が無ければ false。</summary>
        public bool Previous()
        {
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
        /// <c>LibraryPlaybackBridge.Play(mediaId)</c> の手順をそのまま写したもの:
        /// <list type="number">
        /// <item>いま鳴っているものと同じなら、鳴っていることだけ確かめる</item>
        /// <item>Queue に無ければ積む</item>
        /// <item>「先頭の次」へ動かしてから <see cref="Next"/>
        ///       (先頭 = 再生中 という不変条件を壊さないため)</item>
        /// </list>
        /// 何も読み込んでいなければ先頭へ動かして <see cref="Play"/> する。
        /// </summary>
        public bool PlayAt(int catalogIndex, int[] candidates)
        {
            if (catalogIndex < 0 || catalogIndex >= _catalogCount) return false;

            if (_queueCount > 0 && _queue[0] == catalogIndex)
            {
                return _isPlaying || Play();
            }

            bool somethingIsLoaded = _queueCount > 0;

            if (IndexInQueue(catalogIndex) < 0 && !Enqueue(catalogIndex)) return false;

            int wanted = somethingIsLoaded ? 1 : 0;
            int position = IndexInQueue(catalogIndex);
            if (position > wanted) Move(position, wanted);

            if (!somethingIsLoaded) return Play();

            bool started = Next(candidates);
            if (!_isPlaying) started = Play();
            return started;
        }

        /// <summary>Queue の末尾に積む。すでにあれば何もしない。</summary>
        public bool Enqueue(int catalogIndex)
        {
            if (catalogIndex < 0 || catalogIndex >= _catalogCount) return false;
            if (IndexInQueue(catalogIndex) >= 0) return false;
            if (_queueCount >= QueueCapacity) return false;

            _queue[_queueCount] = catalogIndex;
            _queueCount++;
            _exhausted = false;
            return true;
        }

        /// <summary>いま鳴っているものの次に割り込ませる(再生はしない)。</summary>
        public bool PlayNext(int catalogIndex)
        {
            if (catalogIndex < 0 || catalogIndex >= _catalogCount) return false;
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

        // ───────── Queue の操作 ─────────

        /// <summary>
        /// Queue の <paramref name="position"/> 番目へ飛ぶ。
        /// 先頭(= いま鳴っているもの)は指定できない。
        /// </summary>
        public bool JumpTo(int position, int[] candidates)
        {
            if (position <= 0 || position >= _queueCount) return false;

            if (position != 1) Move(position, 1);
            return Next(candidates);
        }

        /// <summary>
        /// Queue から外す。先頭(= いま鳴っているもの)は外せない
        /// (外すと「先頭 = 再生中」が壊れるため。Phase4-3 の <c>QueueView</c> と同じ)。
        /// </summary>
        public bool RemoveFromQueue(int position)
        {
            if (position <= 0 || position >= _queueCount) return false;

            RemoveAt(position);
            return true;
        }

        /// <summary>いま鳴っているもの以外を Queue から外す。</summary>
        public int ClearUpcoming()
        {
            if (_queueCount <= 1) return 0;

            int removed = _queueCount - 1;
            _queueCount = 1;
            return removed;
        }

        /// <summary>Queue を空にする。</summary>
        public void ClearQueue()
        {
            _queueCount = 0;
            _requestedIndex = -1;
            _isPlaying = false;
        }

        /// <summary>
        /// <b>受け取った状態をそのまま当てる。</b>Phase5-4(同期)の入口。
        ///
        /// <b>ここは何も判断しません。</b>次に何を再生するかを決めるのは
        /// 今までどおり<b>持ち主(Owner)側のこのクラス</b>で、
        /// 受け取る側はその結果を映すだけです。
        /// だから補充もしませんし、再生も始めません
        /// (実際に動画を読ませるのは呼び出し側 = <c>UdonSyncCoordinator</c> の仕事)。
        ///
        /// <b>中身を検査しません。</b>持ち主がすでに検査したものなので、
        /// ここで弾くと<b>人によって Queue が違う</b>という一番困る形になります。
        /// 入り切らないぶんだけ捨てます。
        /// </summary>
        /// <param name="queue">catalog index の並び(先頭 = いま鳴っているもの)。</param>
        /// <param name="isPlaying">持ち主が再生中かどうか。</param>
        /// <param name="loadedIndex">持ち主が読み込ませている catalog index。</param>
        public void ApplySyncedState(int[] queue, bool isPlaying, int loadedIndex)
        {
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
            var snapshot = new int[_queueCount];
            for (int i = 0; i < _queueCount; i++) snapshot[i] = _queue[i];
            return snapshot;
        }

        /// <summary>Queue に入っている位置。無ければ -1。</summary>
        public int IndexInQueue(int catalogIndex)
        {
            for (int i = 0; i < _queueCount; i++)
            {
                if (_queue[i] == catalogIndex) return i;
            }
            return -1;
        }

        /// <summary>
        /// 足りなければ候補から補充する。
        /// <c>PlayerSession.EnsureQueueFilled()</c> と同じ判断
        /// (下限を割ったときだけ、目標まで積む)。
        /// </summary>
        /// <returns>積んだ件数。</returns>
        public int EnsureQueueFilled(int[] candidates)
        {
            if (candidates == null) return 0;
            if (_queueCount >= MinimumQueueCount) return 0;

            int wanted = TargetQueueCount - _queueCount;
            if (wanted <= 0) return 0;

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

        /// <summary>
        /// 最後まで再生された。<c>PlayerSession.OnBackendEvent(Ended)</c> と同じで次へ送る。
        /// </summary>
        public bool OnEnded(int[] candidates)
        {
            bool advanced = Next(candidates);
            if (!advanced) _isPlaying = false;
            return advanced;
        }

        /// <summary>
        /// 再生に失敗した。壊れているものを飛ばして次へ送る
        /// (Phase3-4 の復帰と同じ考え方)。
        /// </summary>
        public bool OnError(int[] candidates)
        {
            return OnEnded(candidates);
        }

        // ───────── 内部 ─────────

        private void Load(int catalogIndex)
        {
            _requestedIndex = catalogIndex;
            _loadCount++;
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
