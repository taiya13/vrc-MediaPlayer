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
    /// ───────────────────────────────────────────────
    /// <b>Phase7-3:「再生中」と「再生予定」を分けました。</b>
    ///
    /// Phase3 から Phase7-2 まで、Queue の不変条件は
    /// <b>「先頭がいま鳴っているもの」</b>でした。これが実機で起きた 3 つの不具合の
    /// <b>共通の原因</b>です。
    /// <list type="number">
    /// <item><b>選んだ曲が必ず「再生予定」に出る。</b>再生中が Queue の中にいるので、
    ///       一覧から 1 曲選ぶだけで再生予定に並びます。
    ///       「勝手に追加される」の正体はこれでした</item>
    /// <item><b>再生中を再生予定から消せない。</b>先頭を外すと不変条件が壊れるので、
    ///       <c>RemoveFromQueue</c> は先頭を拒んでいました。
    ///       消しても消えないものが残り続けます</item>
    /// <item><b>補充が再生予定を勝手に埋める。</b>Queue が短くなるたびに
    ///       おすすめを Queue へ積んでいました。しかも種はいつも再生中なので、
    ///       <b>毎回同じ顔ぶれ</b>が入ります。「同じ動画ばかり追加され続ける」の正体です</item>
    /// </list>
    ///
    /// <b>いまの決まりごと</b>:
    /// <list type="bullet">
    /// <item><see cref="CurrentIndex"/>(再生中)は Queue の<b>外</b>にいる</item>
    /// <item>Queue は<b>これから流すものだけ</b>。人が入れたものしか入らない</item>
    /// <item><b>おすすめは Queue に入れない。</b>次の曲として直接使うだけなので、
    ///       人が作った再生予定を汚しません</item>
    /// <item>曲が終わったら<b>必ず Queue の先頭</b>へ進み、その 1 件を Queue から外す</item>
    /// <item>Queue が空のときだけ <see cref="EndBehaviour"/> に従う
    ///       (既定は<b>停止</b>。リピートは既定にしない)</item>
    /// </list>
    /// </summary>
    public sealed class PlaybackModel
    {
        // ───────── Queue が空になったときの動き ─────────

        /// <summary>止まる。<b>既定</b>。</summary>
        public const int EndBehaviourStop = 0;

        /// <summary>いまの 1 曲を繰り返す。</summary>
        public const int EndBehaviourRepeatOne = 1;

        /// <summary>おすすめで流し続ける。</summary>
        public const int EndBehaviourRecommend = 2;

        // ───────── 設定 ─────────

        /// <summary>
        /// <b>Queue が空になったあとどうするか。</b>既定は <see cref="EndBehaviourStop"/>。
        ///
        /// <b>リピートを既定にしません。</b>「何もしていないのに同じ曲が延々流れる」は
        /// ワールドでは事故です。流し続けたい人だけが
        /// <see cref="EndBehaviourRecommend"/> を選びます。
        ///
        /// <b>Queue に何か入っているときは、この設定は関係ありません。</b>
        /// 必ず Queue の先頭へ進みます。
        /// </summary>
        public int EndBehaviour = EndBehaviourStop;

        /// <summary>履歴として保持する上限。</summary>
        public int MaxHistory = 32;

        /// <summary>Queue に積める上限(Udon では固定長配列なので上限が要る)。</summary>
        public const int QueueCapacity = 64;

        /// <summary>
        /// <b>続けてこの回数だけ失敗したら、自動送りをやめる。</b>0 で無制限。
        ///
        /// <b>これが無いと止まりません。</b>失敗するたびに次へ送るので、
        /// 「失敗 → 次へ → 失敗 → …」が<b>Queue が尽きるまで、
        /// 補充が効いていれば永遠に</b>続きます。
        /// VRChat の動画プレイヤーは読み込み回数を制限していて、
        /// 制限に掛かった読み込みは<b>即座に失敗して返る</b>ため、
        /// この輪はフレーム単位で回り、クライアントごと固まります
        /// (Phase5-5 で実際に起きました)。
        /// </summary>
        public int MaxConsecutiveErrors = 4;

        // ───────── 状態 ─────────

        private readonly int _catalogCount;

        /// <summary>
        /// <b>これから流すものだけ。</b>再生中はここに入りません(Phase7-3)。
        /// </summary>
        private readonly int[] _queue = new int[QueueCapacity];
        private int _queueCount;

        private readonly int[] _history = new int[QueueCapacity];
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

        public PlaybackModel(int catalogCount)
        {
            _catalogCount = catalogCount < 0 ? 0 : catalogCount;
        }

        /// <summary>カタログの件数。</summary>
        public int CatalogCount { get { return _catalogCount; } }

        /// <summary><b>再生予定</b>の件数(再生中は数に入らない)。</summary>
        public int QueueCount { get { return _queueCount; } }

        /// <summary>再生予定の <paramref name="position"/> 番目の catalog index。無ければ -1。</summary>
        public int GetQueueAt(int position)
        {
            if (position < 0 || position >= _queueCount) return -1;
            return _queue[position];
        }

        /// <summary>
        /// <b>いま鳴っているもの。</b>無ければ -1。
        /// Phase7-3 から Queue とは別の場所を指します。
        /// </summary>
        public int CurrentIndex { get { return _currentIndex; } }

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
            if (!IsInCatalog(catalogIndex)) return false;

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
        /// 再生する。何も読み込んでいなければ再生予定の先頭から始める。
        /// </summary>
        public bool Play()
        {
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

            _isPlaying = true;
            _exhausted = false;
            return true;
        }

        /// <summary>止める。読み込んでいるものはそのまま。</summary>
        public bool Stop()
        {
            if (_currentIndex < 0) return false;

            _isPlaying = false;
            return true;
        }

        /// <summary>鳴っていれば止め、止まっていれば鳴らす。</summary>
        public bool TogglePlayPause()
        {
            if (_currentIndex < 0 && _queueCount == 0) return false;

            if (_isPlaying)
            {
                _isPlaying = false;
                return true;
            }
            return Play();
        }

        /// <summary>
        /// <b>次へ進む(人が押したとき)。</b>
        ///
        /// 再生予定に何か入っていれば、必ずその先頭へ進み、その 1 件を再生予定から外します。
        /// 空のときは <paramref name="candidates"/>(おすすめ)を使って構いません。
        /// <b>人が「次へ」を押したのだから、勝手ではありません。</b>
        /// おすすめを使っても<b>再生予定には積みません</b>(そのまま次の曲になるだけ)。
        /// </summary>
        /// <param name="candidates">再生予定が空のときに使ってよい catalog index の候補。無ければ null。</param>
        public bool Next(int[] candidates)
        {
            if (_queueCount > 0) return TakeFromQueue(0);

            int pick = PickCandidate(candidates);
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
        /// ただし<b>選んだものが再生予定に入っていたら、そこからは外します</b> —
        /// いま流し始めたものが「これから流すもの」に残っていたら二重表示になります。
        /// </summary>
        public bool PlayAt(int catalogIndex, int[] candidates)
        {
            if (!IsInCatalog(catalogIndex)) return false;

            if (_currentIndex == catalogIndex)
            {
                // すでにこれが鳴っている。止まっていたら鳴らし直すだけ。
                return _isPlaying || Play();
            }

            _consecutiveErrors = 0;
            return MoveTo(catalogIndex);
        }

        /// <summary>再生予定の末尾に積む。すでにあれば何もしない。</summary>
        public bool Enqueue(int catalogIndex)
        {
            if (!IsInCatalog(catalogIndex)) return false;
            if (IndexInQueue(catalogIndex) >= 0) return false;
            if (_queueCount >= QueueCapacity) return false;

            _queue[_queueCount] = catalogIndex;
            _queueCount++;
            _exhausted = false;
            return true;
        }

        /// <summary>再生予定の先頭に割り込ませる(再生はしない)。</summary>
        public bool PlayNext(int catalogIndex)
        {
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
            return true;
        }

        // ───────── Queue の操作 ─────────

        /// <summary>
        /// 再生予定の <paramref name="position"/> 番目へ飛ぶ。
        /// 飛び越したものは<b>再生予定から外れます</b>(聴かずに飛ばしたので)。
        ///
        /// Phase7-3 から<b>先頭(0 番目)も指定できます</b>。
        /// 再生中は再生予定に入っていないので、先頭を除ける理由がなくなりました。
        /// </summary>
        public bool JumpTo(int position, int[] candidates)
        {
            if (position < 0 || position >= _queueCount) return false;

            return TakeFromQueue(position);
        }

        /// <summary>
        /// 再生予定から外す。
        ///
        /// Phase7-3 から<b>どの位置でも外せます</b>。
        /// 以前は先頭 = 再生中だったので外せませんでした
        /// (「消しても消えない」の原因)。
        /// </summary>
        public bool RemoveFromQueue(int position)
        {
            if (position < 0 || position >= _queueCount) return false;

            RemoveAt(position);
            return true;
        }

        /// <summary>
        /// <b>再生予定を空にする。</b>いま鳴っているものはそのまま流れ続けます。
        /// </summary>
        /// <returns>外した件数。</returns>
        public int ClearUpcoming()
        {
            int removed = _queueCount;
            _queueCount = 0;
            return removed;
        }

        /// <summary>再生予定を空にして、再生も止める。</summary>
        public void ClearQueue()
        {
            _queueCount = 0;
            _currentIndex = -1;
            _requestedIndex = -1;
            _isPlaying = false;
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
        /// <b>最後まで再生された。</b>
        ///
        /// <list type="number">
        /// <item>再生予定に何かあれば<b>必ず</b>その先頭へ進み、Queue から外す</item>
        /// <item>空なら <see cref="EndBehaviour"/> に従う(既定は停止)</item>
        /// </list>
        /// </summary>
        public bool NotifyEnded(int[] candidates)
        {
            if (_queueCount > 0) return TakeFromQueue(0);

            if (EndBehaviour == EndBehaviourRepeatOne && _currentIndex >= 0)
            {
                Load(_currentIndex);
                _isPlaying = true;
                return true;
            }

            if (EndBehaviour == EndBehaviourRecommend)
            {
                int pick = PickCandidate(candidates);
                if (pick >= 0) return MoveTo(pick);
            }

            // 既定 = 停止。
            _isPlaying = false;
            _exhausted = true;
            return false;
        }

        /// <summary>
        /// 再生に失敗した。壊れているものを飛ばして次へ送る。
        ///
        /// <b><see cref="MaxConsecutiveErrors"/> 回続けて失敗したら、そこで諦めます。</b>
        /// 諦めないと、失敗するたびに次を読み込み、その読み込みがまた失敗し …… と
        /// 無限に回ります。
        ///
        /// <b>失敗のときだけは、止まる設定でもおすすめを使いません。</b>
        /// 壊れた URL を飛ばすのが目的で、勝手に別の曲を流し始めるためではないからです。
        /// </summary>
        public bool NotifyError(int[] candidates)
        {
            _consecutiveErrors++;

            if (MaxConsecutiveErrors > 0 && _consecutiveErrors > MaxConsecutiveErrors)
            {
                _isPlaying = false;
                _exhausted = true;
                return false;
            }

            if (_queueCount > 0) return TakeFromQueue(0);

            _isPlaying = false;
            _exhausted = true;
            return false;
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
        /// <param name="queue">再生予定の並び(再生中は含まない)。</param>
        /// <param name="isPlaying">持ち主が再生中かどうか。</param>
        /// <param name="loadedIndex">持ち主が読み込ませている catalog index(= 再生中)。</param>
        public void ApplySyncedState(int[] queue, bool isPlaying, int loadedIndex)
        {
            int count = queue == null ? 0 : queue.Length;
            if (count > QueueCapacity) count = QueueCapacity;

            for (int i = 0; i < count; i++) _queue[i] = queue[i];
            _queueCount = count;

            _currentIndex = loadedIndex;
            _isPlaying = isPlaying;
            if (isPlaying) _exhausted = false;

            _requestedIndex = loadedIndex;
        }

        /// <summary>いまの再生予定を写し取る。持ち主が配るために使う。</summary>
        public int[] SnapshotQueue()
        {
            var snapshot = new int[_queueCount];
            for (int i = 0; i < _queueCount; i++) snapshot[i] = _queue[i];
            return snapshot;
        }

        /// <summary>再生予定に入っている位置。無ければ -1。</summary>
        public int IndexInQueue(int catalogIndex)
        {
            for (int i = 0; i < _queueCount; i++)
            {
                if (_queue[i] == catalogIndex) return i;
            }
            return -1;
        }

        // ───────── 内部 ─────────

        /// <summary>
        /// 再生予定の <paramref name="position"/> 番目を取り出して再生する。
        /// <b>そこまでの曲は飛ばしたものとして外します</b>(履歴には積む)。
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
        /// おすすめの中から、いま鳴っているものでも再生予定でもない 1 件を選ぶ。
        /// 無ければ -1。
        /// </summary>
        private int PickCandidate(int[] candidates)
        {
            if (candidates == null) return -1;

            for (int i = 0; i < candidates.Length; i++)
            {
                int candidate = candidates[i];
                if (!IsInCatalog(candidate)) continue;
                if (candidate == _currentIndex) continue;
                if (IndexInQueue(candidate) >= 0) continue;
                return candidate;
            }
            return -1;
        }

        private bool IsInCatalog(int catalogIndex)
        {
            return catalogIndex >= 0 && catalogIndex < _catalogCount;
        }

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
