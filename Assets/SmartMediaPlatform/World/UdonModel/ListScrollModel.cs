namespace SmartMediaPlatform.World.UdonModel
{
    /// <summary>
    /// <b>一覧のスクロール。Phase5-5 の「正典」。</b>
    /// Phase5-3 の <c>ListPageModel</c>(ページ送り)を置き換えたものです。
    ///
    /// <b>なぜページ送りをやめたのか</b><br/>
    /// ページ送りは<b>最終ページが半端に空きます</b>。
    /// 24 件を 6 行で見ると 4 ページ目は 6 件そろいますが、
    /// 22 件なら 4 ページ目は 4 件で、下 2 行が空白のまま残ります。
    /// また「1 件だけ先を見たい」ができず、6 件単位でしか動けません。
    ///
    /// ここでは<b>「先頭に見えている位置」(<see cref="Offset"/>)だけ</b>を持ちます。
    /// <list type="bullet">
    /// <item>最後まで送っても<b>行が埋まったまま</b>(<see cref="MaxOffset"/> で止まる)</item>
    /// <item>1 行ずつでも、まとめてでも動かせる(<see cref="Step"/>)</item>
    /// <item>「7〜12 / 24 件」と<b>いま何番目を見ているかが出せる</b></item>
    /// </list>
    ///
    /// <b>何を表示するかは知りません。</b>Library か 関連 か Queue かも、
    /// カタログの中身も、URL も、ここには出てきません。
    /// 扱うのは<b>件数と行数という 2 つの数だけ</b>です。
    ///
    /// 写した先は <c>UdonMediaListView</c> です。
    /// <b>数え方を変えるときは必ず両方を直してください。</b>
    /// </summary>
    public sealed class ListScrollModel
    {
        private int _rowCount;
        private int _totalCount;
        private int _offset;
        private int _step;

        /// <param name="rowCount">一度に並べる行数(= 用意した行の数)。</param>
        public ListScrollModel(int rowCount)
        {
            _rowCount = rowCount < 0 ? 0 : rowCount;
        }

        /// <summary>一度に並べる行数。</summary>
        public int RowCount
        {
            get { return _rowCount; }
        }

        /// <summary>いま一覧に何件あるか。</summary>
        public int TotalCount
        {
            get { return _totalCount; }
        }

        /// <summary>先頭に見えている位置(0 から)。</summary>
        public int Offset
        {
            get { return _offset; }
        }

        /// <summary>
        /// ▲▼ 1 回ぶんの移動量。<b>0 なら「1 画面 − 1 行」</b>。
        ///
        /// 1 行ずつだと 24 件で 18 回押すことになります。
        /// かといって 1 画面ずつだと<b>見ていた行が全部入れ替わって</b>
        /// どこまで見たのか分からなくなります。
        /// <b>1 行だけ残す</b>と、その 1 行が目印になって続きから読めます
        /// (Phase6-4 で「1 画面ぶん」から変えました)。
        /// </summary>
        public int Step
        {
            get { return _step; }
            set { _step = value < 0 ? 0 : value; }
        }

        /// <summary>実際に動く行数。</summary>
        public int EffectiveStep
        {
            get
            {
                if (_step > 0) return _step;
                if (_rowCount <= 1) return 1;

                return _rowCount - 1;
            }
        }

        // ───────── 続けて押すと速くなる(Phase6-4)─────────

        /// <summary>
        /// <b>続けて押すほど 1 回で動く行数が増える。</b>
        ///
        /// <b>なぜ要るのか</b><br/>
        /// VR では長押しもホイールも使えず、押せるのはボタンだけです。
        /// 200 件を 1 画面ずつ送ると 30 回以上押すことになります。
        /// 連打すると加速するようにすれば、<b>同じボタンのまま</b>
        /// 「少しだけ」も「一気に」も出せます。
        /// </summary>
        public bool Acceleration = true;

        /// <summary>この秒数以内に続けて押されたら「連打」とみなす。</summary>
        public float AccelerationWindow = 0.45f;

        /// <summary>連打 1 回ごとに何倍にするか。</summary>
        public float AccelerationFactor = 1.8f;

        /// <summary>加速しても 1 回でこれ以上は動かない。</summary>
        public int MaxStep = 64;

        private float _lastScrollAt = -999f;
        private int _runLength;
        private int _lastDirection;

        /// <summary>いま何連打目か(0 = 単発)。</summary>
        public int RunLength { get { return _runLength; } }

        /// <summary>
        /// <paramref name="now"/> に <paramref name="direction"/> 方向へ押されたときの移動量。
        ///
        /// <b>向きを変えたら加速はリセットします。</b>
        /// 行き過ぎて戻すときに、戻しすぎるのを避けるためです。
        /// </summary>
        public int StepAt(float now, int direction)
        {
            int step = EffectiveStep;
            if (!Acceleration) return step;

            bool continued = direction == _lastDirection
                             && now - _lastScrollAt <= AccelerationWindow;

            _runLength = continued ? _runLength + 1 : 0;
            _lastScrollAt = now;
            _lastDirection = direction;

            float grown = step;
            for (int i = 0; i < _runLength; i++)
            {
                grown *= AccelerationFactor;
                if (grown >= MaxStep) break;
            }

            int result = (int)grown;
            if (result < step) result = step;
            if (result > MaxStep) result = MaxStep;

            return result;
        }

        /// <summary>加速を初め(単発)に戻す。端へ飛んだあとなどに呼びます。</summary>
        public void ResetAcceleration()
        {
            _runLength = 0;
            _lastDirection = 0;
            _lastScrollAt = -999f;
        }

        /// <summary>上へ 1 回ぶん(連打なら加速する)。</summary>
        public bool ScrollUpAt(float now)
        {
            return ScrollBy(-StepAt(now, -1));
        }

        /// <summary>下へ 1 回ぶん(連打なら加速する)。</summary>
        public bool ScrollDownAt(float now)
        {
            return ScrollBy(StepAt(now, 1));
        }

        /// <summary>
        /// 先頭位置の上限。<b>これ以上は送れません</b>。
        /// 最後まで送っても行が埋まったままなのはこの値のおかげです。
        /// </summary>
        public int MaxOffset
        {
            get
            {
                int max = _totalCount - _rowCount;
                return max > 0 ? max : 0;
            }
        }

        /// <summary>
        /// 件数を知らせる。行き過ぎていた位置は<b>その場で戻します</b>
        /// (Queue から曲を消したときに空白へ取り残されないため)。
        /// </summary>
        public void SetTotalCount(int totalCount)
        {
            _totalCount = totalCount < 0 ? 0 : totalCount;
            Clamp();
        }

        // ───────── 動かす ─────────

        public bool CanScrollUp
        {
            get { return _offset > 0; }
        }

        public bool CanScrollDown
        {
            get { return _offset < MaxOffset; }
        }

        /// <summary><paramref name="lines"/> 行ぶん動かす。動けば true。</summary>
        public bool ScrollBy(int lines)
        {
            int before = _offset;

            _offset += lines;
            Clamp();

            return _offset != before;
        }

        /// <summary>上へ 1 回ぶん。</summary>
        public bool ScrollUp()
        {
            return ScrollBy(-EffectiveStep);
        }

        /// <summary>下へ 1 回ぶん。</summary>
        public bool ScrollDown()
        {
            return ScrollBy(EffectiveStep);
        }

        public bool ScrollToTop()
        {
            return ScrollBy(-_totalCount);
        }

        public bool ScrollToBottom()
        {
            return ScrollBy(_totalCount);
        }

        /// <summary>
        /// <paramref name="position"/> が見えるところまで動かす。
        /// <b>すでに見えているなら動かしません</b>(勝手に画面が飛ぶのを避ける)。
        /// </summary>
        public bool Reveal(int position)
        {
            if (_rowCount <= 0) return false;
            if (position < 0 || position >= _totalCount) return false;

            if (position < _offset) return ScrollBy(position - _offset);
            if (position >= _offset + _rowCount) return ScrollBy(position - (_offset + _rowCount - 1));

            return false;
        }

        // ───────── 何が見えているか ─────────

        /// <summary>
        /// <paramref name="row"/> 行目が指している一覧の位置。
        /// 行がはみ出している(= 空行)なら -1。
        /// </summary>
        public int PositionOf(int row)
        {
            if (row < 0 || row >= _rowCount) return -1;

            int position = _offset + row;
            if (position >= _totalCount) return -1;

            return position;
        }

        /// <summary>いま実際に中身が入る行数。</summary>
        public int FilledRowCount
        {
            get
            {
                int rest = _totalCount - _offset;
                if (rest <= 0) return 0;
                return rest < _rowCount ? rest : _rowCount;
            }
        }

        /// <summary>見えている先頭の番号(1 から数える。0 件なら 0)。</summary>
        public int FirstShownNumber
        {
            get { return _totalCount <= 0 ? 0 : _offset + 1; }
        }

        /// <summary>見えている末尾の番号(1 から数える。0 件なら 0)。</summary>
        public int LastShownNumber
        {
            get
            {
                if (_totalCount <= 0) return 0;

                int last = _offset + FilledRowCount;
                return last > _totalCount ? _totalCount : last;
            }
        }

        /// <summary>
        /// スクロールつまみの位置(0〜1)。動かせないときは 0。
        /// 「いまどのあたりを見ているか」を細い棒で出すために使います。
        /// </summary>
        public float ScrollFraction
        {
            get
            {
                int max = MaxOffset;
                if (max <= 0) return 0f;
                return (float)_offset / max;
            }
        }

        /// <summary>見えている割合(0〜1)。つまみの長さに使います。</summary>
        public float VisibleFraction
        {
            get
            {
                if (_totalCount <= 0) return 1f;
                if (_rowCount >= _totalCount) return 1f;
                return (float)_rowCount / _totalCount;
            }
        }

        private void Clamp()
        {
            if (_offset < 0) _offset = 0;

            int max = MaxOffset;
            if (_offset > max) _offset = max;
        }
    }
}
