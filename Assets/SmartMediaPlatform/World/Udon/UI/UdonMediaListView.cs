using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace SmartMediaPlatform.World.Udon.UI
{
    /// <summary>
    /// <b>一覧 1 つぶん。</b>Library / 関連 / Queue を<b>この 1 クラスで兼ねます</b>。Phase5-3。
    ///
    /// <b>判断ロジックを 1 つも持っていません。</b>やることは 2 つだけです:
    /// <list type="number">
    /// <item><see cref="UdonCatalogStore"/> / <see cref="UdonPlayerSession"/> の内容を行へ書き写す</item>
    /// <item>行が押されたら <see cref="UdonMediaController"/> へ中継する</item>
    /// </list>
    /// 次に何を再生するかは今までどおり <see cref="UdonPlayerSession"/> が決めます。
    ///
    /// <b>なぜ 3 種類を 1 クラスにしたのか</b><br/>
    /// Phase5-2 は <c>UdonMediaPlayerUI</c> が Library / 関連 / Queue の
    /// <b>3 つぶんの表示を丸ごと抱えて</b>いました。行数を変えるにも、
    /// 一覧を 1 つ増やすにも、あの 1 クラスを触ることになります。
    /// ここでは<b>「どこから引くか」を <see cref="Source"/> の番号 1 つ</b>にしたので、
    /// <list type="bullet">
    /// <item>壁パネルは 3 つ置く / リモコンは Queue だけ 1 つ置く …… <b>Prefab の違いだけ</b></item>
    /// <item>履歴やプレイリストを足す …… <b>番号を 1 つ増やすだけ</b></item>
    /// </list>
    /// で済みます。
    ///
    /// <b>URL は見えません。</b>引くのは <see cref="UdonCatalogStore"/> 越しだけなので、
    /// Phase4-2 で決めた「URL を知るのは <c>UdonVideoBackend</c> だけ」は保たれています。
    ///
    /// <b>ページの数え方は
    /// <see cref="SmartMediaPlatform.World.UdonModel.ListPageModel"/> の写しです。</b>
    /// あちらは純粋 C# で EditMode テスト済み、こちらはそれを UdonSharp の書き方へ
    /// 1 対 1 で移したものです(Phase5-2 の <c>PlaybackModel</c> →
    /// <see cref="UdonPlayerSession"/> と同じ手順)。
    /// <b>数え方を変えるときは必ず両方を直してください。</b>
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonMediaListView : UdonSharpBehaviour
    {
        /// <summary>カタログの一覧(絞り込み済み)。</summary>
        public const int SourceLibrary = 0;

        /// <summary>いま鳴っているものの関連。</summary>
        public const int SourceRelated = 1;

        /// <summary>再生待ち(先頭 = いま鳴っているもの)。</summary>
        public const int SourceQueue = 2;

        [Header("何の一覧か")]
        [Tooltip("0=Library / 1=関連 / 2=Queue")]
        public int Source = SourceLibrary;

        [Tooltip("見出しに出す文字")]
        public string HeaderLabel = "Library";

        [Header("つなぎ先(UdonMediaPanel が自動で入れる)")]
        [Tooltip("状態の 1 行を出す先")]
        public UdonMediaPanel Panel;

        [Tooltip("操作を伝える窓口")]
        public UdonMediaController Controller;

        [Tooltip("表示するもとの状態")]
        public UdonPlayerSession Session;

        [Tooltip("表示用データの窓口(URL は見えない)")]
        public UdonCatalogStore Store;

        [Header("行(この数がそのまま 1 ページの行数)")]
        public UdonMediaListRow[] Rows;

        [Header("見出し / ページ送り(空でも動く)")]
        public Text HeaderText;

        [Tooltip("「2 / 5」の形で出す")]
        public Text PageText;

        [Tooltip("先頭ページでは隠す")]
        public GameObject PreviousPageButton;

        [Tooltip("最終ページでは隠す")]
        public GameObject NextPageButton;

        [Tooltip("1 件も無いときだけ出す")]
        public GameObject EmptyMessage;

        [Header("表示")]
        [Tooltip("いま何ページ目か(0 から)")]
        public int Page;

        // 行 → catalog index。Refresh のたびに焼き直す。
        // 「画面に出ているもの」と「押したときに再生するもの」を必ず一致させるため、
        // 押された時点で引き直すのではなく、描いた時点の対応を持っておく。
        private int[] _shown;
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

            _shown = new int[RowCount()];
        }

        /// <summary>1 ページの行数。</summary>
        public int RowCount()
        {
            return Rows == null ? 0 : Rows.Length;
        }

        /// <summary>この一覧の総件数。</summary>
        public int TotalCount()
        {
            if (Source == SourceQueue) return Session != null ? Session.QueueCount : 0;
            if (Source == SourceRelated) return ResolveRelated().Length;
            return Store != null ? Store.Count : 0;
        }

        /// <summary>画面を書き直す。<see cref="UdonMediaPanel"/> から呼ばれる。</summary>
        public void Refresh()
        {
            EnsureInitialized();

            int rows = RowCount();
            if (rows == 0) return;

            int[] related = new int[0];
            int total;

            if (Source == SourceRelated)
            {
                related = ResolveRelated();
                total = related.Length;
            }
            else if (Source == SourceQueue)
            {
                total = Session != null ? Session.QueueCount : 0;
            }
            else
            {
                total = Store != null ? Store.Count : 0;
            }

            ClampPage(total, rows);

            int first = Page * rows;

            for (int row = 0; row < rows; row++)
            {
                UdonMediaListRow target = Rows[row];
                if (target == null) continue;

                int position = first + row;
                int catalogIndex = -1;

                if (Source == SourceRelated)
                {
                    if (position >= 0 && position < related.Length) catalogIndex = related[position];
                }
                else if (Source == SourceQueue)
                {
                    if (Session != null) catalogIndex = Session.GetQueueAt(position);
                }
                else
                {
                    if (Store != null) catalogIndex = Store.GetIndexAt(position);
                }

                _shown[row] = catalogIndex;

                if (catalogIndex < 0)
                {
                    target.ShowEmpty();
                    continue;
                }

                target.ShowItem(
                    IndexLabel(position),
                    Store != null ? Store.GetTitle(catalogIndex) : "",
                    Store != null ? Store.GetArtist(catalogIndex) : "",
                    Store != null ? Store.FormatDuration(catalogIndex) : "",
                    IsNowPlaying(catalogIndex),
                    HasSecondary(position));
            }

            RefreshChrome(total, rows);
        }

        // ───────── 行から呼ばれる ─────────

        /// <summary>行そのものが押された。Library / 関連は再生、Queue はそこへ移動。</summary>
        public void OnRowPrimary(int row)
        {
            EnsureInitialized();
            if (Controller == null) return;

            int rows = RowCount();
            if (row < 0 || row >= rows) return;

            // 見出しは「押す前」に控える。
            // 窓口を呼ぶと Refresh が返ってきて _shown が書き換わるので、
            // 後から引くと「1 つずれた曲名」を報告してしまう。
            string title = TitleAt(row);

            if (Source == SourceQueue)
            {
                int position = Page * rows + row;
                Report(Controller.JumpInQueue(position), "移動", title);
                return;
            }

            int catalogIndex = _shown[row];
            if (catalogIndex < 0) return;

            Report(Controller.PlayCatalogIndex(catalogIndex), "再生", title);
        }

        /// <summary>2 つめのボタンが押された。Library / 関連は Queue へ追加、Queue は削除。</summary>
        public void OnRowSecondary(int row)
        {
            EnsureInitialized();
            if (Controller == null) return;

            int rows = RowCount();
            if (row < 0 || row >= rows) return;

            string title = TitleAt(row);

            if (Source == SourceQueue)
            {
                int position = Page * rows + row;
                Report(Controller.RemoveFromQueue(position), "Queue から削除", title);
                return;
            }

            int catalogIndex = _shown[row];
            if (catalogIndex < 0) return;

            Report(Controller.EnqueueCatalogIndex(catalogIndex), "Queue に追加", title);
        }

        // ───────── ページ送り(ボタンからそのまま呼べる)─────────

        public void NextPage()
        {
            int rows = RowCount();
            if (rows == 0) return;
            if ((Page + 1) * rows >= TotalCount()) return;

            Page++;
            Refresh();
        }

        public void PreviousPage()
        {
            if (Page <= 0) return;

            Page--;
            Refresh();
        }

        public void FirstPage()
        {
            Page = 0;
            Refresh();
        }

        // ───────── 内部 ─────────

        private int[] ResolveRelated()
        {
            if (Session == null || Store == null) return new int[0];

            int current = Session.CurrentIndex;
            if (current < 0) return new int[0];

            return Store.GetRelatedIndices(current);
        }

        private void ClampPage(int total, int rows)
        {
            if (Page < 0) Page = 0;
            if (rows <= 0) return;

            // 端数があるぶんもう 1 ページ。0 件でも 1 ページとして扱う。
            int pages = total <= 0 ? 1 : (total + rows - 1) / rows;
            if (Page >= pages) Page = pages - 1;
        }

        private void RefreshChrome(int total, int rows)
        {
            if (HeaderText != null)
            {
                string label = HeaderLabel + "  " + total;
                if (HeaderText.text != label) HeaderText.text = label;
            }

            int pages = total <= 0 ? 1 : (total + rows - 1) / rows;

            if (PageText != null)
            {
                string label = (Page + 1) + " / " + pages;
                if (PageText.text != label) PageText.text = label;
            }

            SetActive(PreviousPageButton, Page > 0);
            SetActive(NextPageButton, Page + 1 < pages);
            SetActive(EmptyMessage, total <= 0);
        }

        /// <summary>行の頭に出す印。Queue だけ「♪ / 1. / 2. …」の並びにする。</summary>
        private string IndexLabel(int position)
        {
            if (Source == SourceQueue) return position == 0 ? "♪" : "" + position;
            if (Source == SourceRelated) return "▷";
            return "" + (position + 1);
        }

        private bool IsNowPlaying(int catalogIndex)
        {
            if (Session == null) return false;
            return catalogIndex >= 0 && catalogIndex == Session.CurrentIndex;
        }

        /// <summary>Queue の先頭(= いま鳴っているもの)は外せないので 2 つめのボタンを隠す。</summary>
        private bool HasSecondary(int position)
        {
            if (Source == SourceQueue) return position > 0;
            return true;
        }

        private string TitleAt(int row)
        {
            if (_shown == null || row < 0 || row >= _shown.Length) return "";
            if (Store == null) return "";

            return Store.GetTitle(_shown[row]);
        }

        private void Report(bool ok, string what, string title)
        {
            if (Panel == null) return;

            string subject = title == null || title.Length == 0 ? what : title + " を " + what;
            Panel.SetStatus(ok ? subject + "しました。" : subject + "できませんでした。");
        }

        private void SetActive(GameObject target, bool value)
        {
            if (target == null) return;
            if (target.activeSelf == value) return;
            target.SetActive(value);
        }
    }
}
