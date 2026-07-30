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
    /// <b>スクロールの数え方は
    /// <see cref="SmartMediaPlatform.World.UdonModel.ListScrollModel"/> の写しです。</b>
    /// あちらは純粋 C# で EditMode テスト済み、こちらはそれを UdonSharp の書き方へ
    /// 1 対 1 で移したものです(Phase5-2 の <c>PlaybackModel</c> →
    /// <see cref="UdonPlayerSession"/> と同じ手順)。
    /// <b>数え方を変えるときは必ず両方を直してください。</b>
    ///
    /// <b>Phase5-5 でページ送りをやめました。</b>
    /// ページ送りは最終ページが半端に空き、1 件だけ先を見ることもできません。
    /// いまは<b>「先頭に見えている位置」<see cref="Offset"/> だけ</b>を持ち、
    /// 最後まで送っても行が埋まったままになります。
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

        [Header("行(この数がそのまま一度に見える行数)")]
        public UdonMediaListRow[] Rows;

        [Header("見出し / スクロール(空でも動く)")]
        public Text HeaderText;

        [Tooltip("「7〜12 / 24 件」の形で出す")]
        public Text RangeText;

        [Tooltip("先頭では隠す")]
        public GameObject ScrollUpButton;

        [Tooltip("末尾では隠す")]
        public GameObject ScrollDownButton;

        [Tooltip("いまどのあたりを見ているかを示すつまみ(RectTransform を動かす)")]
        public RectTransform ScrollHandle;

        [Tooltip("1 件も無いときだけ出す")]
        public GameObject EmptyMessage;

        [Header("スクロール")]
        [Tooltip("先頭に見えている位置(0 から)")]
        public int Offset;

        [Tooltip("▲▼ 1 回で動く行数。0 なら 1 画面ぶん")]
        public int ScrollStep;

        [Tooltip("曲が変わったら、鳴っている行が見えるところまで自動で戻す")]
        public bool FollowNowPlaying;

        [Header("押した感じ")]
        [Tooltip("押した行に印を出す秒数。0 で無効")]
        [Range(0f, 2f)]
        public float TouchFeedbackSeconds = 0.6f;

        [Tooltip("同じ行をこの秒数以内に 2 回押されたら 2 回目を捨てる。0 で無効")]
        public float DoubleFireGuard = 0.25f;

        // 行 → catalog index。Refresh のたびに焼き直す。
        // 「画面に出ているもの」と「押したときに再生するもの」を必ず一致させるため、
        // 押された時点で引き直すのではなく、描いた時点の対応を持っておく。
        private int[] _shown;
        private bool _initialized;

        // 直近に受けた押下(二重発火よけ + 押した感じの表示)
        private int _lastRow = -1;
        private int _lastKind = -1;
        private float _lastAt = -999f;

        // 押した行の印。位置で覚えるのは、スクロールしても
        // 「押したのはこの曲」がずれないようにするため。
        private int _touchedPosition = -1;
        private float _touchedAt = -999f;

        // 追いかけ済みの曲(FollowNowPlaying 用)
        private int _followedIndex = -2;

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

            ClampOffset(total, rows);

            // 曲が変わったときだけ追いかける。毎回追いかけると、
            // 眺めている最中に画面が飛んで操作できなくなる。
            if (FollowNowPlaying && Session != null && Session.CurrentIndex != _followedIndex)
            {
                _followedIndex = Session.CurrentIndex;
                RevealNowPlaying();
                ClampOffset(total, rows);
            }

            int first = Offset;
            bool pressedStillOn = TouchFeedbackSeconds > 0f
                                  && Time.time - _touchedAt < TouchFeedbackSeconds;

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

                // 押した行に短く印を出す。
                // uGUI の色変化は「使う」で押したときには出ないので、
                // どちらの押し方でも手応えが返るようにここで出す。
                target.SetPressed(pressedStillOn && position == _touchedPosition);
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
            if (!Accept(0, row)) return;

            // 見出しは「押す前」に控える。
            // 窓口を呼ぶと Refresh が返ってきて _shown が書き換わるので、
            // 後から引くと「1 つずれた曲名」を報告してしまう。
            string title = TitleAt(row);

            MarkTouched(Offset + row);

            if (Source == SourceQueue)
            {
                Report(Controller.JumpInQueue(Offset + row), "移動", title);
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
            if (!Accept(1, row)) return;

            string title = TitleAt(row);

            MarkTouched(Offset + row);

            if (Source == SourceQueue)
            {
                Report(Controller.RemoveFromQueue(Offset + row), "Queue から削除", title);
                return;
            }

            int catalogIndex = _shown[row];
            if (catalogIndex < 0) return;

            Report(Controller.EnqueueCatalogIndex(catalogIndex), "Queue に追加", title);
        }

        // ───────── スクロール(ボタンからそのまま呼べる)─────────

        /// <summary>▲ 1 回ぶん戻す。</summary>
        public void ScrollUp()
        {
            ScrollBy(-EffectiveStep());
        }

        /// <summary>▼ 1 回ぶん進める。</summary>
        public void ScrollDown()
        {
            ScrollBy(EffectiveStep());
        }

        public void ScrollToTop()
        {
            ScrollBy(-TotalCount());
        }

        public void ScrollToBottom()
        {
            ScrollBy(TotalCount());
        }

        /// <summary>
        /// <paramref name="lines"/> 行ぶん動かす。
        /// <see cref="SmartMediaPlatform.World.UdonModel.ListScrollModel.ScrollBy"/> の写しです。
        /// </summary>
        public void ScrollBy(int lines)
        {
            int before = Offset;

            Offset += lines;
            ClampOffset(TotalCount(), RowCount());

            if (Offset != before) Refresh();
        }

        /// <summary>いま鳴っているものが見えるところまで動かす。</summary>
        public void RevealNowPlaying()
        {
            if (Session == null) return;

            int current = Session.CurrentIndex;
            if (current < 0) return;

            int position = -1;

            if (Source == SourceQueue) position = Session.IndexInQueue(current);
            else if (Source == SourceLibrary && Store != null) position = Store.GetPositionOf(current);

            if (position < 0) return;

            int rows = RowCount();
            if (rows <= 0) return;

            // すでに見えているなら動かさない(勝手に画面が飛ぶのを避ける)
            if (position >= Offset && position < Offset + rows) return;

            if (position < Offset) ScrollBy(position - Offset);
            else ScrollBy(position - (Offset + rows - 1));
        }

        // ── Phase5-3 の名前でも呼べるようにしておく(既存 Prefab のボタン向け)
        public void NextPage()
        {
            ScrollDown();
        }

        public void PreviousPage()
        {
            ScrollUp();
        }

        public void FirstPage()
        {
            ScrollToTop();
        }

        /// <summary>▲▼ 1 回で動く行数。0 なら 1 画面ぶん。</summary>
        public int EffectiveStep()
        {
            if (ScrollStep > 0) return ScrollStep;

            int rows = RowCount();
            return rows > 0 ? rows : 1;
        }

        /// <summary>これ以上は送れない位置。</summary>
        public int MaxOffset()
        {
            int max = TotalCount() - RowCount();
            return max > 0 ? max : 0;
        }

        // ───────── 内部 ─────────

        /// <summary>
        /// 同じ行の同じ押し方が続けて 2 回来たら、2 回目を捨てる。
        ///
        /// 行は uGUI(<c>Button.onClick</c>)と VRChat の「使う」(<c>Interact</c>)の
        /// <b>両方から押せる</b>ようにしてあります(実機ではワールド内 uGUI の
        /// レイキャストが通らないことがあるため)。両方が同時に反応すると
        /// 「Queue から外す」が 2 行消してしまうので、ここで抑えます。
        /// </summary>
        private bool Accept(int kind, int row)
        {
            if (DoubleFireGuard <= 0f) return true;

            if (kind == _lastKind && row == _lastRow
                && Time.time - _lastAt < DoubleFireGuard) return false;

            _lastKind = kind;
            _lastRow = row;
            _lastAt = Time.time;
            return true;
        }

        /// <summary>押した行を覚える(短く印を出すため)。</summary>
        private void MarkTouched(int position)
        {
            _touchedPosition = position;
            _touchedAt = Time.time;
        }

        private int[] ResolveRelated()
        {
            if (Session == null || Store == null) return new int[0];

            int current = Session.CurrentIndex;
            if (current < 0) return new int[0];

            return Store.GetRelatedIndices(current);
        }

        /// <summary>
        /// 行き過ぎた位置を戻す。
        ///
        /// <b>上限は「総件数 − 行数」</b>です。ページ送りと違い、
        /// 最後まで送っても行が空かないのはこれが理由です。
        /// </summary>
        private void ClampOffset(int total, int rows)
        {
            if (Offset < 0) Offset = 0;

            int max = total - rows;
            if (max < 0) max = 0;

            if (Offset > max) Offset = max;
        }

        private void RefreshChrome(int total, int rows)
        {
            if (HeaderText != null && HeaderText.text != HeaderLabel)
            {
                HeaderText.text = HeaderLabel;
            }

            int filled = total - Offset;
            if (filled < 0) filled = 0;
            if (filled > rows) filled = rows;

            if (RangeText != null)
            {
                // 「7〜12 / 24 件」。総数だけでなく、いま何番目を見ているかまで出す。
                string label = total <= 0
                    ? "0 件"
                    : (Offset + 1) + "〜" + (Offset + filled) + " / " + total + " 件";

                if (RangeText.text != label) RangeText.text = label;
            }

            int max = total - rows;
            if (max < 0) max = 0;

            SetActive(ScrollUpButton, Offset > 0);
            SetActive(ScrollDownButton, Offset < max);
            SetActive(EmptyMessage, total <= 0);

            RefreshScrollHandle(total, rows, max);
        }

        /// <summary>
        /// つまみを動かす。長さが「見えている割合」、位置が「どこまで送ったか」。
        /// 一覧が短くて全部見えているときは、つまみが track いっぱいになります。
        /// </summary>
        private void RefreshScrollHandle(int total, int rows, int max)
        {
            if (ScrollHandle == null) return;

            float visible = total <= 0 || rows >= total ? 1f : (float)rows / total;
            float progress = max <= 0 ? 0f : (float)Offset / max;

            float top = 1f - (1f - visible) * progress;
            float bottom = top - visible;

            if (bottom < 0f) bottom = 0f;
            if (top > 1f) top = 1f;

            ScrollHandle.anchorMin = new Vector2(0f, bottom);
            ScrollHandle.anchorMax = new Vector2(1f, top);
            ScrollHandle.offsetMin = new Vector2(0f, 0f);
            ScrollHandle.offsetMax = new Vector2(0f, 0f);
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

            // 権限が無くて弾かれたのか、やってみて駄目だったのかは書き分ける。
            if (!ok && Controller != null && Controller.LastDenied)
            {
                Panel.SetStatus(Controller.Sync != null
                    ? Controller.Sync.DenyReason() + "。"
                    : "いまは操作できません。");
                return;
            }

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
