namespace SmartMediaPlatform.World.UdonModel
{
    /// <summary>
    /// <b>一覧のページ送りモデル。Phase5-3 の「正典」。</b>
    ///
    /// <b>なぜこれがあるのか</b><br/>
    /// <see cref="PlaybackModel"/> と同じ理由です。実機で動く
    /// <c>UdonMediaListView</c> は <c>UdonSharpBehaviour</c> なので
    /// EditMode テストから触れません。そこで<b>Udon で書ける形のまま純粋 C# で書いて
    /// 先に検証し、それを 1 対 1 で写す</b>手順を取ります。
    /// このクラスがその「まず書くほう」で、写した先が <c>UdonMediaListView</c> です。
    /// <b>ページの数え方を変えるときは必ず両方を直してください。</b>
    ///
    /// <b>ここが決めるのは 3 つだけ</b>です:
    /// <list type="bullet">
    /// <item><b>何ページあるか</b>(0 件でも 1 ページ。空の見た目を出す先が要るため)</item>
    /// <item><b>行 → 一覧の位置</b>(押された行が指しているもの)</item>
    /// <item><b>行き過ぎたページの戻し方</b>(件数が減ったときに空ページで固まらないように)</item>
    /// </list>
    ///
    /// <b>何を表示するかは知りません。</b>Library か 関連 か Queue かも、
    /// カタログの中身も、URL も、ここには出てきません。
    /// 扱うのは<b>件数と行数という 2 つの数だけ</b>です。
    /// </summary>
    public sealed class ListPageModel
    {
        private int _rowCount;
        private int _totalCount;
        private int _page;

        /// <param name="rowCount">1 ページに並べる行数(= 用意した行の数)。</param>
        public ListPageModel(int rowCount)
        {
            _rowCount = rowCount < 0 ? 0 : rowCount;
        }

        /// <summary>1 ページの行数。</summary>
        public int RowCount
        {
            get { return _rowCount; }
        }

        /// <summary>いま一覧に何件あるか。</summary>
        public int TotalCount
        {
            get { return _totalCount; }
        }

        /// <summary>
        /// 件数を知らせる。行き過ぎていたページは<b>その場で戻します</b>
        /// (Queue から曲を消したときに空ページへ取り残されないため)。
        /// </summary>
        public void SetTotalCount(int totalCount)
        {
            _totalCount = totalCount < 0 ? 0 : totalCount;
            Clamp();
        }

        /// <summary>いま何ページ目か(0 から)。</summary>
        public int Page
        {
            get { return _page; }
        }

        /// <summary>
        /// 全部で何ページあるか。
        /// <b>0 件でも 1 ページ</b>です — 「まだありません」を出す場所が要るからで、
        /// ページ番号の表示が「0 / 0」になるのも避けられます。
        /// </summary>
        public int PageCount
        {
            get
            {
                if (_rowCount <= 0) return 1;
                if (_totalCount <= 0) return 1;

                // 端数のぶんをもう 1 ページ数える
                return (_totalCount + _rowCount - 1) / _rowCount;
            }
        }

        /// <summary>このページの先頭が指している一覧の位置。</summary>
        public int FirstPosition
        {
            get { return _page * _rowCount; }
        }

        /// <summary>
        /// <paramref name="row"/> 行目が指している一覧の位置。
        /// 行がはみ出している(= 空行)なら -1。
        /// </summary>
        public int PositionOf(int row)
        {
            if (row < 0 || row >= _rowCount) return -1;

            int position = _page * _rowCount + row;
            if (position >= _totalCount) return -1;

            return position;
        }

        /// <summary>このページで実際に中身が入る行数。</summary>
        public int FilledRowCount
        {
            get
            {
                int rest = _totalCount - _page * _rowCount;
                if (rest <= 0) return 0;
                return rest < _rowCount ? rest : _rowCount;
            }
        }

        public bool HasNextPage
        {
            get { return _page + 1 < PageCount; }
        }

        public bool HasPreviousPage
        {
            get { return _page > 0; }
        }

        /// <summary>次のページへ。行けなければ false。</summary>
        public bool NextPage()
        {
            if (!HasNextPage) return false;

            _page++;
            return true;
        }

        /// <summary>前のページへ。行けなければ false。</summary>
        public bool PreviousPage()
        {
            if (!HasPreviousPage) return false;

            _page--;
            return true;
        }

        public void FirstPage()
        {
            _page = 0;
        }

        /// <summary>
        /// <paramref name="position"/> が見えるページへ移る。
        /// 一覧の外を指していれば何もせず false。
        /// </summary>
        public bool RevealPosition(int position)
        {
            if (_rowCount <= 0) return false;
            if (position < 0 || position >= _totalCount) return false;

            _page = position / _rowCount;
            return true;
        }

        private void Clamp()
        {
            if (_page < 0) _page = 0;

            int last = PageCount - 1;
            if (_page > last) _page = last;
        }
    }
}
