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

        /// <summary>再生予定(これから流すものだけ。再生中は入らない)。</summary>
        public const int SourceQueue = 2;

        [Header("何の一覧か")]
        [Tooltip("0=Library / 1=関連 / 2=Queue")]
        public int Source = SourceLibrary;

        [Tooltip("見出しに出す文字。空なら種類に合わせて「すべての曲 / おすすめ / 再生予定」")]
        public string HeaderLabel = "";

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

        [Tooltip("いま鳴っているもの(無ければ先頭)へ戻る。先頭にいるときは隠す")]
        public GameObject ScrollHomeButton;

        [Tooltip("いまどのあたりを見ているかを示すつまみ(RectTransform を動かす)")]
        public RectTransform ScrollHandle;

        [Tooltip("1 件も無いときだけ出す")]
        public GameObject EmptyMessage;

        [Tooltip("1 件も無いときの文字。空なら種類に合わせた文を出す")]
        public Text EmptyText;

        [Header("スクロール")]
        [Tooltip("先頭に見えている位置(0 から)")]
        public int Offset;

        [Tooltip("▲▼ 1 回で動く行数。0 なら「1 画面 − 1 行」(1 行だけ残して目印にする)")]
        public int ScrollStep;

        [Tooltip("曲が変わったら、鳴っている行が見えるところまで自動で戻す")]
        public bool FollowNowPlaying;

        [Header("続けて押すと速くなる(VR ではボタンしか押せないため)")]
        [Tooltip("連打すると 1 回で動く行数が増える")]
        public bool ScrollAcceleration = true;

        [Tooltip("この秒数以内に続けて押されたら「連打」とみなす")]
        [Range(0.1f, 1.5f)]
        public float AccelerationWindow = 0.45f;

        [Tooltip("連打 1 回ごとに何倍にするか")]
        [Range(1f, 4f)]
        public float AccelerationFactor = 1.8f;

        [Tooltip("加速しても 1 回でこれ以上は動かない")]
        public int MaxScrollStep = 64;

        [Header("検索とまとめ(Phase7-2。すべての曲のときだけ効く)")]
        [Tooltip("検索欄。空でも動く。押すと VRChat のキーボードが出る")]
        public InputField SearchField;

        [Tooltip("検索中だけ出す「×」。空でも動く")]
        public GameObject SearchClearButton;

        [Tooltip("何件当たったかを出す先。空でも動く")]
        public Text SearchResultText;

        [Tooltip("チャンネルごとにまとめて、折りたためるようにする")]
        public bool GroupByChannel = true;

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

        // 検索とまとめ(Phase7-2)。
        // _viewIndex が -1 なら、その位置はチャンネルの見出し。
        private int[] _viewIndex;
        private string[] _viewName;
        private int[] _viewSize;
        private int _viewLength;

        private string _query = "";
        private string[] _collapsed;
        private int _collapsedCount;
        private int _builtFor = -1;

        // 連打の加速(ListScrollModel の写し)
        private float _lastScrollAt = -999f;
        private int _runLength;
        private int _lastDirection;

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

        /// <summary>この一覧の総件数(見出しの行も 1 つと数える)。</summary>
        public int TotalCount()
        {
            if (Source == SourceQueue) return Session != null ? Session.QueueCount : 0;
            if (Source == SourceRelated) return ResolveRelated().Length;

            if (UsesView())
            {
                EnsureView();
                return _viewLength;
            }

            return Store != null ? Store.Count : 0;
        }

        // ───────── 検索とチャンネルまとめ(Phase7-2)─────────

        /// <summary>
        /// 「すべての曲」だけが、検索とまとめを通ります。
        /// <b>再生予定を検索しても意味がありません</b>し、
        /// おすすめは並びそのものに意味があるためです。
        /// </summary>
        public bool UsesView()
        {
            return Source == SourceLibrary && Store != null;
        }

        /// <summary>検索欄が変わったとき。<c>InputField.onValueChanged</c> から呼ぶ。</summary>
        public void OnSearchChanged()
        {
            string typed = SearchField != null ? SearchField.text : "";
            if (typed == null) typed = "";

            if (typed == _query) return;

            _query = typed;

            // 検索し直したら先頭から見せる。前の位置に留まると
            // 「打ったのに何も出ない」ように見える。
            Offset = 0;
            _builtFor = -1;

            Refresh();
        }

        /// <summary>検索をやめる。「×」から呼ぶ。</summary>
        public void ClearSearch()
        {
            if (SearchField != null) SearchField.text = "";

            _query = "";
            Offset = 0;
            _builtFor = -1;

            Refresh();
        }

        /// <summary>
        /// 一覧の中身を組み直す。
        ///
        /// <b>毎フレームは作りません。</b>作り直すのは
        /// <list type="bullet">
        /// <item>検索の文字が変わったとき</item>
        /// <item>チャンネルをたたんだ / 開いたとき</item>
        /// <item>カタログの件数が変わったとき</item>
        /// </list>
        /// の 3 つだけです。200 曲を毎フレーム並べ直すと確実に重くなります。
        /// </summary>
        private void EnsureView()
        {
            int catalogCount = Store != null ? Store.Count : 0;
            if (_builtFor == catalogCount && _viewIndex != null) return;

            _builtFor = catalogCount;
            BuildView(catalogCount);
        }

        private void BuildView(int catalogCount)
        {
            // 見出しは多くても曲数と同じ数までしか出ない(1 曲 1 チャンネルの場合)。
            int capacity = catalogCount * 2 + 2;

            _viewIndex = new int[capacity];
            _viewName = new string[capacity];
            _viewSize = new int[capacity];
            _viewLength = 0;

            string query = _query != null ? _query.Trim().ToLower() : "";

            if (!GroupByChannel)
            {
                FillFlat(catalogCount, query);
                RefreshSearchChrome(query);
                return;
            }

            FillGrouped(catalogCount, query);
            RefreshSearchChrome(query);
        }

        /// <summary>まとめずに、当たったものを順に並べる。</summary>
        private void FillFlat(int catalogCount, string query)
        {
            for (int i = 0; i < catalogCount; i++)
            {
                int catalogIndex = Store.GetIndexAt(i);
                if (catalogIndex < 0) continue;
                if (!Store.Matches(catalogIndex, query)) continue;

                _viewIndex[_viewLength] = catalogIndex;
                _viewLength++;
            }
        }

        /// <summary>
        /// チャンネルごとにまとめる。
        ///
        /// <b>並びはカタログのままです。</b>チャンネルは<b>最初に出てきた順</b>に並び、
        /// その中の曲もカタログの順のままです。名前順に並べ替えないのは、
        /// <b>取り込んだ順(=新しい順)を保ちたい</b>ためです。
        /// </summary>
        private void FillGrouped(int catalogCount, string query)
        {
            var done = new bool[catalogCount];

            for (int i = 0; i < catalogCount; i++)
            {
                if (done[i]) continue;

                int first = Store.GetIndexAt(i);
                if (first < 0) { done[i] = true; continue; }

                string channel = ChannelOf(first);

                // このチャンネルで当たったものを数えてから見出しを置く。
                // 「0 曲」の見出しが出てしまうのを防ぐため。
                int matched = 0;
                for (int j = i; j < catalogCount; j++)
                {
                    if (done[j]) continue;

                    int candidate = Store.GetIndexAt(j);
                    if (candidate < 0) continue;
                    if (ChannelOf(candidate) != channel) continue;

                    if (Store.Matches(candidate, query)) matched++;
                }

                if (matched == 0)
                {
                    // 当たらなかったチャンネルは、見出しごと出さない。
                    for (int j = i; j < catalogCount; j++)
                    {
                        int candidate = Store.GetIndexAt(j);
                        if (candidate >= 0 && ChannelOf(candidate) == channel) done[j] = true;
                    }
                    continue;
                }

                bool expanded = !IsCollapsed(channel);

                _viewIndex[_viewLength] = -1;
                _viewName[_viewLength] = channel;
                _viewSize[_viewLength] = matched;
                _viewLength++;

                for (int j = i; j < catalogCount; j++)
                {
                    if (done[j]) continue;

                    int candidate = Store.GetIndexAt(j);
                    if (candidate < 0) continue;
                    if (ChannelOf(candidate) != channel) continue;

                    done[j] = true;

                    if (!expanded) continue;
                    if (!Store.Matches(candidate, query)) continue;

                    _viewIndex[_viewLength] = candidate;
                    _viewLength++;
                }
            }
        }

        private string ChannelOf(int catalogIndex)
        {
            string channel = Store.GetArtist(catalogIndex);
            return channel == null || channel.Length == 0 ? "(不明)" : channel;
        }

        private void RefreshSearchChrome(string query)
        {
            bool searching = query.Length > 0;

            SetActive(SearchClearButton, searching);

            if (SearchResultText == null) return;

            string label = searching ? SongCount() + " 曲が見つかりました" : "";
            if (SearchResultText.text != label) SearchResultText.text = label;
        }

        /// <summary>見出しを除いた曲の数。</summary>
        public int SongCount()
        {
            int count = 0;
            for (int i = 0; i < _viewLength; i++)
            {
                if (_viewIndex[i] >= 0) count++;
            }
            return count;
        }

        // ───────── 折りたたみ ─────────

        private bool IsCollapsed(string channel)
        {
            if (_collapsed == null) return false;

            for (int i = 0; i < _collapsedCount; i++)
            {
                if (_collapsed[i] == channel) return true;
            }
            return false;
        }

        private void ToggleCollapsed(string channel)
        {
            if (_collapsed == null) _collapsed = new string[64];

            for (int i = 0; i < _collapsedCount; i++)
            {
                if (_collapsed[i] != channel) continue;

                // 詰め直す。並びに意味は無いので、最後のものを持ってくれば済む。
                _collapsed[i] = _collapsed[_collapsedCount - 1];
                _collapsedCount--;

                _builtFor = -1;
                return;
            }

            if (_collapsedCount >= _collapsed.Length) return;

            _collapsed[_collapsedCount] = channel;
            _collapsedCount++;

            _builtFor = -1;
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
            else if (UsesView())
            {
                EnsureView();
                total = _viewLength;
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
                else if (UsesView())
                {
                    if (position >= 0 && position < _viewLength)
                    {
                        catalogIndex = _viewIndex[position];

                        // -1 はチャンネルの見出し。曲ではないので別に描く。
                        if (catalogIndex < 0)
                        {
                            _shown[row] = -1;

                            target.ShowHeader(
                                _viewName[position], _viewSize[position],
                                !IsCollapsed(_viewName[position]));
                            continue;
                        }
                    }
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

                // 絵が無いカタログでも穴が開かないよう、ジャンルの色を先に渡す。
                string genre = Store != null ? Store.GetGenre(catalogIndex) : "";
                target.SetFallbackColor(GenreColor(genre));

                target.ShowItem(
                    IndexLabel(position),
                    Store != null ? Store.GetTitle(catalogIndex) : "",
                    SubLabel(catalogIndex, genre),
                    Store != null ? Store.FormatDuration(catalogIndex) : "",
                    IsNowPlaying(catalogIndex),
                    HasSecondary(position),
                    Store != null ? Store.GetThumbnail(catalogIndex) : null,
                    FallbackInitial(genre),
                    SecondaryLabel());

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

            // チャンネルの見出しを押したら、開け閉てするだけ。
            if (UsesView())
            {
                int position = Offset + row;
                if (position >= 0 && position < _viewLength && _viewIndex[position] < 0)
                {
                    ToggleCollapsed(_viewName[position]);
                    Refresh();
                    return;
                }
            }

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
            if (_shown != null && row < _shown.Length && _shown[row] < 0) return;

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

        /// <summary>▲ 1 回ぶん戻す。<b>連打すると加速します。</b></summary>
        public void ScrollUp()
        {
            ScrollBy(-StepNow(-1));
        }

        /// <summary>▼ 1 回ぶん進める。<b>連打すると加速します。</b></summary>
        public void ScrollDown()
        {
            ScrollBy(StepNow(1));
        }

        public void ScrollToTop()
        {
            ResetAcceleration();
            ScrollBy(-TotalCount());
        }

        public void ScrollToBottom()
        {
            ResetAcceleration();
            ScrollBy(TotalCount());
        }

        /// <summary>
        /// <b>▲▲ = 迷子からの復帰。</b>
        /// いま鳴っているものがこの一覧にあればそこへ、無ければ先頭へ。
        ///
        /// 再生予定には鳴っているものが入らないので、そちらでは先頭へ戻り、
        /// Library では「さっきかけた曲の場所」へ戻ります。
        /// <b>ボタンを 1 つ増やさずに、どちらの一覧でも正しいことをします。</b>
        /// </summary>
        public void ScrollHome()
        {
            ResetAcceleration();

            int position = NowPlayingPosition();
            if (position < 0)
            {
                ScrollToTop();
                return;
            }

            // 上端に置く。「見えるところまで」だと、
            // どこに出るかが押すたびに変わって落ち着かない。
            ScrollBy(position - Offset);
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
            int position = NowPlayingPosition();
            if (position < 0) return;

            int rows = RowCount();
            if (rows <= 0) return;

            // すでに見えているなら動かさない(勝手に画面が飛ぶのを避ける)
            if (position >= Offset && position < Offset + rows) return;

            if (position < Offset) ScrollBy(position - Offset);
            else ScrollBy(position - (Offset + rows - 1));
        }

        /// <summary>いま鳴っているものが、この一覧の何番目か。無ければ -1。</summary>
        public int NowPlayingPosition()
        {
            if (Session == null) return -1;

            int current = Session.CurrentIndex;
            if (current < 0) return -1;

            if (Source == SourceQueue) return Session.IndexInQueue(current);

            if (Source == SourceLibrary && UsesView())
            {
                EnsureView();

                for (int i = 0; i < _viewLength; i++)
                {
                    if (_viewIndex[i] == current) return i;
                }
                return -1;
            }

            if (Source == SourceLibrary && Store != null) return Store.GetPositionOf(current);

            return -1;
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

        /// <summary>
        /// ▲▼ 1 回で動く行数。0 なら<b>「1 画面 − 1 行」</b>。
        /// 1 行だけ残すと、その行が目印になって続きから読めます。
        /// <see cref="SmartMediaPlatform.World.UdonModel.ListScrollModel.EffectiveStep"/> の写しです。
        /// </summary>
        public int EffectiveStep()
        {
            if (ScrollStep > 0) return ScrollStep;

            int rows = RowCount();
            if (rows <= 1) return 1;

            return rows - 1;
        }

        /// <summary>
        /// いま押されたぶんの移動量。<b>連打すると増えます。</b>
        /// <see cref="SmartMediaPlatform.World.UdonModel.ListScrollModel.StepAt"/> の写しです。
        /// <b>数え方を変えるときは必ず両方を直してください。</b>
        /// </summary>
        public int StepNow(int direction)
        {
            int step = EffectiveStep();
            if (!ScrollAcceleration) return step;

            // 向きを変えたら初めに戻す。行き過ぎて戻すときに戻しすぎないため。
            bool continued = direction == _lastDirection
                             && Time.time - _lastScrollAt <= AccelerationWindow;

            _runLength = continued ? _runLength + 1 : 0;
            _lastScrollAt = Time.time;
            _lastDirection = direction;

            float grown = step;
            for (int i = 0; i < _runLength; i++)
            {
                grown *= AccelerationFactor;
                if (grown >= MaxScrollStep) break;
            }

            int result = (int)grown;
            if (result < step) result = step;
            if (result > MaxScrollStep) result = MaxScrollStep;

            return result;
        }

        /// <summary>加速を初め(単発)に戻す。</summary>
        public void ResetAcceleration()
        {
            _runLength = 0;
            _lastDirection = 0;
            _lastScrollAt = -999f;
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

        /// <summary>
        /// 見出しに出す文字。<see cref="HeaderLabel"/> が空なら種類から決めます。
        ///
        /// <b>Phase6-4 で英語をやめました。</b>
        /// <c>Library</c> / <c>Queue</c> は<b>ワールドに来た人には通じません</b>。
        /// 既定を空にしてここで決めるようにしたので、
        /// <b>すでに置いてある Prefab も、見出しの文字を消すだけで日本語になります</b>
        /// (作り直しも配線のやり直しも要りません)。
        /// </summary>
        public string EffectiveHeader()
        {
            if (HeaderLabel != null && HeaderLabel.Length > 0) return HeaderLabel;

            if (Source == SourceQueue) return "再生予定";
            if (Source == SourceRelated) return "おすすめ";

            return "すべての曲";
        }

        /// <summary>
        /// 1 件も無いときに出す文。<b>種類ごとに書き分けます</b>(Phase7)。
        ///
        /// 同じ「ありません」でも、<b>次に何をすればよいかが違います</b>。
        /// おすすめが空なのは何も悪くないので謝りません。
        /// Queue が空なのは「足せば入る」と伝えます。
        /// </summary>
        public string EmptyMessageFor()
        {
            if (Source == SourceRelated)
            {
                // 曲が鳴っていないときと、鳴っているが関連が無いときは別のこと。
                bool playing = Session != null && Session.CurrentIndex >= 0;

                return playing
                    ? "関連する曲はありません"
                    : "曲を再生すると、似た曲がここに出ます";
            }

            // Phase7-3 から、再生予定は「人が入れたものだけ」が並びます。
            // 何もしなければ空なのがふつうなので、入れ方を書いておきます。
            if (Source == SourceQueue) return "再生予定はありません(一覧の ＋ で追加)";

            // 検索で 0 件なのと、そもそも 1 曲も無いのは別のこと。
            // 前者は打ち直せばよく、後者は Catalog Builder で入れる話になる。
            if (_query != null && _query.Trim().Length > 0)
            {
                return "「" + _query.Trim() + "」に合う曲はありません";
            }

            return "曲がまだ入っていません";
        }

        private void RefreshChrome(int total, int rows)
        {
            string header = EffectiveHeader();
            if (HeaderText != null && HeaderText.text != header)
            {
                HeaderText.text = header;
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
            if (total <= 0 && EmptyText != null)
            {
                string empty = EmptyMessageFor();
                if (EmptyText.text != empty) EmptyText.text = empty;
            }

            // ▲▲ は「戻る先がある」ときだけ出す。
            // 先頭にいるのに出ていると、押しても何も起きなくて戸惑う。
            SetActive(ScrollHomeButton, Offset > 0 || NowPlayingPosition() > 0);

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

        /// <summary>行の頭に出す印。Queue だけ「♪ / 次 / 2 …」の並びにする。</summary>
        private string IndexLabel(int position)
        {
            if (Source != SourceQueue) return "";

            // 「次に何がかかるか」がいちばん知りたいこと。数字より言葉で書く。
            if (position == 0) return "♪";
            if (position == 1) return "次";

            return "" + position;
        }

        /// <summary>
        /// 2 行目。<b>チャンネル · ジャンル</b>の形にします。
        /// ジャンルまで出すのは、同じチャンネルの中から選ぶときの手がかりになるためです。
        /// </summary>
        private string SubLabel(int catalogIndex, string genre)
        {
            if (Store == null) return "";

            string artist = Store.GetArtist(catalogIndex);

            if (genre == null || genre.Length == 0) return artist;
            if (artist == null || artist.Length == 0) return genre;

            return artist + " · " + genre;
        }

        /// <summary>
        /// 2 つめのボタンに出す文字。
        ///
        /// <b>記号だけにしてあります</b>(Phase7-2)。Phase7-1 では「予定へ」と
        /// 書いていましたが、<b>説明的すぎて画面がうるさくなりました</b>。
        /// 何をするかは、押す前に出る「使う」の案内
        /// (<c>再生予定に追加</c>)が伝えます。
        /// </summary>
        private string SecondaryLabel()
        {
            return Source == SourceQueue ? "×" : "＋";
        }

        /// <summary>絵が無いときに枠へ出す 1 文字。ジャンルの頭を取る。</summary>
        private string FallbackInitial(string genre)
        {
            if (genre == null || genre.Length == 0) return "♪";
            return genre.Substring(0, 1);
        }

        [Header("絵が無いときの色(ジャンルごとに割り当てる)")]
        [Tooltip("ジャンル名から 1 つ選ぶ。空なら組み込みの 8 色")]
        public Color[] GenrePalette;

        /// <summary>
        /// ジャンルの色。絵が焼き込まれていないカタログでも、
        /// <b>同じジャンルは必ず同じ色</b>になるので目印として使えます。
        ///
        /// <b>ジャンルの表を持ちません。</b>文字から色を決めるので、
        /// Phase6-6 の辞書にジャンルを足しても<b>ここは直さなくて済みます</b>。
        ///
        /// 色は<b>くすんだ 8 色</b>に絞ってあります。鮮やかにすると、
        /// 絵が入っている行と入っていない行がちぐはぐに見えるためです。
        /// </summary>
        public Color GenreColor(string genre)
        {
            if (genre == null || genre.Length == 0) return new Color(0.18f, 0.20f, 0.26f, 1f);

            int hash = 0;
            for (int i = 0; i < genre.Length; i++) hash = hash * 31 + genre[i];

            if (hash < 0) hash = -hash;

            if (GenrePalette != null && GenrePalette.Length > 0)
            {
                return GenrePalette[hash % GenrePalette.Length];
            }

            // Inspector で色を入れていないときの組み込み。
            int slot = hash % 8;

            if (slot == 0) return new Color(0.26f, 0.35f, 0.50f, 1f);   // 藍
            if (slot == 1) return new Color(0.42f, 0.30f, 0.48f, 1f);   // 藤
            if (slot == 2) return new Color(0.22f, 0.42f, 0.42f, 1f);   // 青緑
            if (slot == 3) return new Color(0.48f, 0.32f, 0.28f, 1f);   // 煉瓦
            if (slot == 4) return new Color(0.30f, 0.40f, 0.28f, 1f);   // 苔
            if (slot == 5) return new Color(0.46f, 0.38f, 0.24f, 1f);   // 芥子
            if (slot == 6) return new Color(0.28f, 0.32f, 0.46f, 1f);   // 群青
            return new Color(0.44f, 0.28f, 0.36f, 1f);                  // 葡萄
        }

        private bool IsNowPlaying(int catalogIndex)
        {
            if (Session == null) return false;
            return catalogIndex >= 0 && catalogIndex == Session.CurrentIndex;
        }

        /// <summary>
        /// 2 つめのボタン(Library では「＋」、再生予定では「×」)を出すか。
        ///
        /// <b>Phase7-3 からは、どの行にも出します。</b>
        /// 以前は再生予定の先頭 = 再生中だったので「×」を隠していました。
        /// そのせいで<b>消せない行が必ず 1 つ残り</b>、
        /// 「消しても消えない」ように見えていました。
        /// いま再生中は再生予定に入らないので、隠す理由がありません。
        /// </summary>
        private bool HasSecondary(int position)
        {
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
