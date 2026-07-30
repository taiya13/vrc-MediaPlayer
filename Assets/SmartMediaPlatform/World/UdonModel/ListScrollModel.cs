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
        /// ▲▼ 1 回ぶんの移動量。<b>0 なら 1 画面ぶん</b>。
        ///
        /// 1 行ずつだと 24 件で 18 回押すことになり、
        /// 1 画面ずつだと見ていた行が全部入れ替わって位置を見失います。
        /// 既定は「半画面」くらいが押しやすい、という判断です。
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
                return _rowCount > 0 ? _rowCount : 1;
            }
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
