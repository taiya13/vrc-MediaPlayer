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

        /// <summary>アーティスト(チャンネル)の一覧。曲ではなく名前が並ぶ(Phase7-6)。</summary>
        public const int SourceArtist = 3;

        /// <summary>お気に入り(Phase7-8)。</summary>
        public const int SourceFavorite = 4;

        /// <summary>最近聴いたもの(Phase7-8)。</summary>
        public const int SourceHistory = 5;

        [Header("何の一覧か")]
        [Tooltip("0=Library / 1=関連 / 2=Queue / 3=アーティスト / 4=お気に入り / 5=履歴")]
        public int Source = SourceLibrary;

        [Header("アーティストで絞る(Phase7-6)")]
        [Tooltip("空でなければ、このアーティストの曲だけを出す。"
                 + "アーティスト一覧から選ばれると書き換わる")]
        public string ArtistFilter = "";

        [Tooltip("アーティスト一覧の側だけ入れる。ここで選んだ結果を映す曲の一覧")]
        public UdonMediaListView SongList;

        [Tooltip("アーティスト一覧の側だけ入れる。選んだあとに開くタブ")]
        public UdonMediaTabs Tabs;

        [Header("好み(Phase7-8)")]
        [Tooltip("お気に入り・履歴の持ち主。空だと ♥ と履歴タブが動かない")]
        public SmartMediaPlatform.World.Udon.UdonUserProfile Profile;

        [Tooltip("お気に入りの並びを出す先(お気に入りタブだけ)")]
        public Text SortLabel;

        [Tooltip("選んだあとに開くタブの番号")]
        public int SongTabIndex = 1;

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

        [Tooltip("ホイールとつまみの担当。空でも ▲▼ ボタンだけで動きます")]
        public UdonListScroller Scroller;

        [Header("いま見ているチャンネル(Phase7-3)")]
        [Tooltip("一覧の上に貼り付いて残るチャンネル名。空でも動く")]
        public Text StickyChannelText;

        [Tooltip("その帯そのもの。チャンネルまとめでないときは隠す")]
        public GameObject StickyChannelBand;

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
        public VRC.SDK3.Components.VRCUrlInputField SearchField;

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
            if (Source == SourceFavorite) return Profile != null ? Profile.FavoriteCount : 0;
            if (Source == SourceHistory) return Profile != null ? Profile.HistoryCount : 0;
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
            if (Store == null) return false;
            return Source == SourceLibrary || Source == SourceArtist;
        }

        /// <summary>検索欄が変わったとき。<c>InputField.onValueChanged</c> から呼ぶ。</summary>
        public void OnSearchChanged()
        {
            if (ApplySearchFieldText()) Refresh();
        }

        /// <summary>
        /// <see cref="Refresh"/> から毎回呼ぶポーリング版。
        /// <b>変わっていれば取り込むだけで、ここから <see cref="Refresh"/> は呼びません</b>
        /// (呼び出し元がすでに <see cref="Refresh"/> の中にいるため、
        ///  ここで呼び返すと同じ 1 回の書き直しが二重に走ります)。
        /// </summary>
        private void PollSearchField()
        {
            ApplySearchFieldText();
        }

        /// <summary>
        /// 検索欄の文字を読み、変わっていれば取り込む。
        /// <see cref="OnSearchChanged"/>(イベント経路)と
        /// <see cref="PollSearchField"/>(毎フレーム経路)の共通部分です。
        /// </summary>
        /// <returns>変わっていたら true。</returns>
        private bool ApplySearchFieldText()
        {
            string typed = ReadSearchField();

            // 「×」で消したときの文字は、打ち直されるまで無かったことにする。
            // (VRCUrlInputField の中身は Udon から書き換えられないので、
            //  見た目を消す代わりに<b>こちらが読まない</b>ことで消します)
            if (_clearedText != null && typed == _clearedText) typed = "";

            if (typed == _query) return false;

            _query = typed;

            // 検索し直したら先頭から見せる。前の位置に留まると
            // 「打ったのに何も出ない」ように見える。
            Offset = 0;
            _builtFor = -1;

            return true;
        }

        /// <summary>
        /// <b>VRChat のキーボードを出す。</b>Phase7-6。
        ///
        /// 検索欄(<c>InputField</c>)は、<b>uGUI のポインターで選ばれたとき</b>に
        /// VRChat がキーボードを出します。このワールドではそのポインターが
        /// 届いていないので、いつまでも開きませんでした。
        ///
        /// <b>選ぶところだけ、こちらから代わりにやります。</b>
        /// 枠のどこを「使う」でもここへ来て、キーボードが開きます。
        /// 打った文字は今までどおり <see cref="PollSearchField"/> が拾うので、
        /// 検索の仕組みには手を入れていません。
        ///
        /// Phase7-6 の前に置いていた<b>ワールド内キーボードは廃止しました</b>
        /// (一覧に重なって邪魔になるうえ、日本語が打てないため)。
        /// </summary>
        public void OpenSearchKeyboard()
        {
            if (SearchField == null) return;

            SearchField.Select();
            SearchField.ActivateInputField();
        }

        // 「×」で消したときの文字。これと同じ間は「検索していない」とみなす。
        private string _clearedText;

        /// <summary>
        /// <b>検索欄の文字を読む。</b>Phase7-10。
        ///
        /// <c>VRCUrlInputField</c> で Udon から触れるのは
        /// <c>GetUrl()</c> <b>だけ</b>です。<c>text</c> は
        /// 「Method is not exposed to Udon」で弾かれます。
        /// 打ち込まれた文字は URL として包まれて返ってくるので、
        /// そこから中身を取り出します。
        /// </summary>
        private string ReadSearchField()
        {
            if (SearchField == null) return "";

            VRC.SDKBase.VRCUrl url = SearchField.GetUrl();
            if (url == null) return "";

            string text = url.Get();
            return text == null ? "" : text;
        }

        /// <summary>検索をやめる。「×」から呼ぶ。</summary>
        public void ClearSearch()
        {
            // ── <b>欄の中身は書き換えません</b>(Phase7-10)。
            //
            //    Udon から触れるのは <c>GetUrl()</c> だけで、
            //    <c>text</c> も <c>SetUrl</c> も白名簿に無い版があります。
            //    そこで「いま入っている文字は無かったことにする」と覚えておき、
            //    <b>読む側で消します</b>。打ち直せばまた効きます。
            _clearedText = ReadSearchField();

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

            // 曲数が同じでも、選んでいるアーティストが変わったら中身は別物です。
            // ここを見ていないと、<b>前に選んだアーティストの曲が残ります</b>。
            string filter = ArtistFilter != null ? ArtistFilter : "";
            bool sameFilter = filter == _builtForArtist;

            if (_builtFor == catalogCount && sameFilter && _viewIndex != null) return;

            _builtFor = catalogCount;
            _builtForArtist = filter;
            BuildView(catalogCount);
        }

        // 直前に組んだときのアーティスト絞り込み。
        private string _builtForArtist = "";

        private void BuildView(int catalogCount)
        {
            // 見出しは多くても曲数と同じ数までしか出ない(1 曲 1 チャンネルの場合)。
            int capacity = catalogCount * 2 + 2;

            _viewIndex = new int[capacity];
            _viewName = new string[capacity];
            _viewSize = new int[capacity];
            _viewLength = 0;

            string query = _query != null ? _query.Trim().ToLower() : "";

            if (Source == SourceArtist)
            {
                FillArtists(catalogCount, query);
                RefreshSearchChrome(query);
                return;
            }

            // アーティストを 1 組に絞っているなら、まとめる必要はありません
            // (見出しが 1 本だけ出て、そのぶん行が減るだけになります)。
            bool filtered = ArtistFilter != null && ArtistFilter.Length > 0;

            if (!GroupByChannel || filtered)
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
                if (!PassesArtistFilter(catalogIndex)) continue;
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

        /// <summary>
        /// <b>アーティスト(投稿チャンネル)の名前だけを並べる。</b>Phase7-6。
        ///
        /// 曲は 1 曲も出しません。<b>見出しだけの一覧</b>です
        /// (行の描き方は、まとめたときの見出しと同じものを使い回します)。
        /// 並びはカタログの順 —— <b>最初に出てきた順</b>です。
        /// 名前順に並べ替えると、取り込んだ順(= 新しい順)が失われます。
        /// </summary>
        private void FillArtists(int catalogCount, string query)
        {
            for (int i = 0; i < catalogCount; i++)
            {
                int catalogIndex = Store.GetIndexAt(i);
                if (catalogIndex < 0) continue;

                string channel = ChannelOf(catalogIndex);

                // すでに出したアーティストなら飛ばす。
                bool already = false;
                for (int j = 0; j < _viewLength; j++)
                {
                    if (_viewName[j] == channel) { already = true; break; }
                }
                if (already) continue;

                // このアーティストで、検索に当たる曲が何曲あるか。
                int matched = 0;
                for (int j = 0; j < catalogCount; j++)
                {
                    int candidate = Store.GetIndexAt(j);
                    if (candidate < 0) continue;
                    if (ChannelOf(candidate) != channel) continue;
                    if (Store.Matches(candidate, query)) matched++;
                }

                if (matched == 0) continue;

                _viewIndex[_viewLength] = -1;      // -1 = 見出しの行
                _viewName[_viewLength] = channel;
                _viewSize[_viewLength] = matched;
                _viewLength++;
            }
        }

        /// <summary>選んでいるアーティストの曲か(選んでいなければ全部通す)。</summary>
        private bool PassesArtistFilter(int catalogIndex)
        {
            if (ArtistFilter == null || ArtistFilter.Length == 0) return true;
            return ChannelOf(catalogIndex) == ArtistFilter;
        }

        /// <summary>
        /// <b>アーティストが選ばれた。</b>Phase7-6。
        /// 曲の一覧をそのアーティストだけに絞って、そちらのタブへ移ります。
        /// </summary>
        public void PickArtist(string artist)
        {
            if (SongList == null) return;

            SongList.ArtistFilter = artist == null ? "" : artist;

            // 前に選んだアーティストの続きから見せない。
            SongList.Offset = 0;
            SongList.ClearSearch();

            if (Tabs != null) Tabs.Select(SongTabIndex);
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

            // ── 検索欄を毎回ポーリングする(Phase7-4)。
            //
            //    <c>InputField.onValueChanged</c> だけに頼らないのは、
            //    VRChat の実機キーボード(VR のレーザーで文字を打つ画面)から
            //    入力したとき、<b>1 文字ごとにこのイベントが飛んでこない
            //    ことがある</b>ためです。onValueChanged は uGUI が
            //    「コードから text を書き換えた」経路をたどったときにしか
            //    確実に発火せず、VRChat 側のキーボードの実装次第では
            //    素通りします。「検索バーが反応しない」の主因はこれでした。
            //
            //    ここは 0.5 秒に 1 回(<see cref="UdonMediaPanel.RefreshInterval"/>)
            //    しか回らないので、コストはほぼありません。
            //    onValueChanged のほうも残してあるので、効く環境では
            //    そのまま即座に反応します。
            if (Source == SourceLibrary) PollSearchField();

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
            else if (Source == SourceFavorite || Source == SourceHistory)
            {
                total = TotalCount();
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

            // つまみの長さは件数で決まる。曲が焼き込まれるのは Start より後なので、
            // 書き直しのたびに取り直さないと「0 件のまま動かない」になる。
            if (Scroller != null) Scroller.Rebuild();

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
                else if (Source == SourceFavorite)
                {
                    if (Profile != null) catalogIndex = Profile.GetFavoriteAt(position);
                }
                else if (Source == SourceHistory)
                {
                    if (Profile != null) catalogIndex = Profile.GetHistoryAt(position);
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

                            // アーティスト一覧では、たたむ操作がありません。
                            // 「開く」ことを示す ▶ のまま出します。
                            bool expanded = Source != SourceArtist
                                            && !IsCollapsed(_viewName[position]);

                            target.ShowHeader(
                                _viewName[position], _viewSize[position], expanded);
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
                target.SetFallbackInk(GenreInkColor(genre));

                target.ShowItem(
                    IndexLabel(position),
                    Store != null ? Store.GetTitle(catalogIndex) : "",
                    SubLabel(catalogIndex, genre),
                    Store != null ? Store.FormatDuration(catalogIndex) : "",
                    IsNowPlaying(catalogIndex),
                    HasSecondary(position),
                    Store != null ? Store.GetThumbnail(catalogIndex) : null,
                    FallbackInitial(
                        Store != null ? Store.GetTitle(catalogIndex) : "", genre),
                    SecondaryLabel());

                // ♥ は「好み」を持っているときだけ出す(Phase7-8)。
                target.ShowFavorite(
                    Profile != null && Profile.IsFavorite(catalogIndex),
                    Profile != null);

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

            // 見出しの行を押したとき。
            if (UsesView())
            {
                int position = Offset + row;
                if (position >= 0 && position < _viewLength && _viewIndex[position] < 0)
                {
                    // アーティスト一覧では、見出しがそのままアーティストです。
                    if (Source == SourceArtist)
                    {
                        PickArtist(_viewName[position]);
                        return;
                    }

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

        /// <summary>
        /// <b>♥ が押された。</b>Phase7-8。
        ///
        /// 入れる / 外すを切り替えるだけです。<b>再生はしません</b> ——
        /// 「好き」と「いま聴く」は別のことなので、同じ操作にまとめると
        /// <b>お気に入りに入れるたびに曲が変わります</b>。
        /// </summary>
        public void OnRowFavorite(int row)
        {
            EnsureInitialized();
            if (Profile == null) return;

            int rows = RowCount();
            if (row < 0 || row >= rows) return;
            if (!Accept(2, row)) return;
            if (_shown == null || row >= _shown.Length) return;

            int catalogIndex = _shown[row];
            if (catalogIndex < 0) return;

            string title = TitleAt(row);
            bool added = Profile.ToggleFavorite(catalogIndex);

            MarkTouched(Offset + row);

            // お気に入りタブでは、外した瞬間に並びが縮みます。
            _builtFor = -1;
            Refresh();

            Report(true, added ? "お気に入りに追加" : "お気に入りから削除", title);
        }

        /// <summary>お気に入りの並びを次のものへ送る。</summary>
        public void CycleSort()
        {
            if (Profile == null) return;

            Profile.CycleFavoriteSort();
            Offset = 0;
            _builtFor = -1;
            Refresh();
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
        /// <b><paramref name="offset"/> 行目を上端にする。</b>Phase7-3。
        /// つまみやホイールから呼ばれます(<see cref="UdonListScroller"/>)。
        ///
        /// <see cref="ScrollBy"/> と違って<b>つまみへ書き戻しません</b>。
        /// 書き戻すと、動かしている最中に引っぱり合いになります。
        /// </summary>
        public void ScrollTo(int offset)
        {
            int before = Offset;

            Offset = offset;
            ClampOffset(TotalCount(), RowCount());

            if (Offset == before) return;

            ResetAcceleration();
            Refresh();
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

            if (Offset == before) return;

            Refresh();

            // ▲▼ で動かしたぶんも、つまみの位置に反映させる。
            // 片方だけ動くと「壊れている」ように見えます。
            if (Scroller != null) Scroller.Follow();
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

        /// <summary>
        /// <b>いま見ているチャンネル名を、一覧の上に貼り付けておく。</b>Phase7-3。
        ///
        /// <b>なぜ要るのか</b><br/>
        /// チャンネルの見出しは一覧の中に並んでいるので、
        /// <b>1 行スクロールしただけで画面の外へ消えます</b>。
        /// そうなると「いま誰の曲を見ているのか」が分からなくなり、
        /// 別のチャンネルへ移りたいだけなのに<b>いちばん上まで戻る</b>ことになります。
        ///
        /// 上端に見えている曲のチャンネル名をここへ写すと、
        /// スクロールしても<b>必ず 1 つは見出しが見えている</b>状態になります。
        /// </summary>
        private void RefreshStickyChannel()
        {
            if (StickyChannelBand == null && StickyChannelText == null) return;

            bool grouped = Source == SourceLibrary && UsesView();
            SetActive(StickyChannelBand, grouped);

            if (!grouped || StickyChannelText == null) return;

            string channel = ChannelAtView(Offset);
            if (StickyChannelText.text != channel) StickyChannelText.text = channel;
        }

        /// <summary>
        /// 並びの <paramref name="position"/> 番目が属するチャンネル。
        /// その位置が見出しならその名前、曲なら<b>上へさかのぼって</b>探します。
        /// </summary>
        private string ChannelAtView(int position)
        {
            if (_viewIndex == null || _viewLength == 0) return "";

            if (position < 0) position = 0;
            if (position >= _viewLength) position = _viewLength - 1;

            for (int i = position; i >= 0; i--)
            {
                if (_viewIndex[i] < 0) return _viewName[i];
            }
            return "";
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
        /// <summary>
        /// <b>タブに出す短い名前。</b>Phase7-6。
        /// 見出し(<see cref="EffectiveHeader"/>)と分けているのは、
        /// アーティスト名がタブに入りきらないためです。
        /// </summary>
        public string TabLabel()
        {
            if (Source == SourceQueue) return "再生予定";
            if (Source == SourceRelated) return "おすすめ";
            if (Source == SourceArtist) return "アーティスト";
            if (Source == SourceFavorite) return "お気に入り";
            if (Source == SourceHistory) return "履歴";
            return "曲";
        }

        public string EffectiveHeader()
        {
            if (HeaderLabel != null && HeaderLabel.Length > 0) return HeaderLabel;

            if (Source == SourceQueue) return "再生予定";
            if (Source == SourceRelated) return "おすすめ";
            if (Source == SourceArtist) return "アーティスト";
            if (Source == SourceFavorite) return "お気に入り";
            if (Source == SourceHistory) return "さっき聴いた曲";

            // アーティストを選んでいるなら、その名前をそのまま見出しにする。
            // 「すべての曲」と出したまま 1 組しか並んでいないのは嘘になります。
            if (ArtistFilter != null && ArtistFilter.Length > 0) return ArtistFilter;

            return "曲";
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

            RefreshStickyChannel();

            // 並べ替えのボタンに、いまの並びを書く(お気に入りタブだけ)。
            if (SortLabel != null && Profile != null)
            {
                string sort = Profile.FavoriteSortLabel();
                if (SortLabel.text != sort) SortLabel.text = sort;
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
        /// <summary>
        /// 2 行目に出すもの。
        ///
        /// <b>チャンネルでまとめているときは、チャンネル名を出しません</b>(Phase7-4)。
        /// すぐ上の見出しと、一覧の上に貼り付いた帯に<b>すでに 2 回出ています</b>。
        /// 3 回目を行にも出すと、そのぶん<b>曲名の幅が減って途中で切れます</b> ——
        /// いちばん読みたいものが読めなくなるので、ここでは省きます。
        /// </summary>
        private string SubLabel(int catalogIndex, string genre)
        {
            if (Store == null) return "";

            bool grouped = Source == SourceLibrary && UsesView();
            if (grouped) return genre != null ? genre : "";

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

        /// <summary>
        /// <b>絵の代わりに出す 1 文字。</b>Phase8。
        ///
        /// <b>ジャンルではなく曲名の頭を取ります。</b>
        /// ジャンルの頭だと、同じジャンルの曲が<b>全部おなじ文字</b>になり、
        /// 並べたときに 1 枚も見分けられません。曲名の頭なら
        /// <b>1 曲ずつ違う顔</b>になり、色と合わせて「その曲の絵」になります。
        ///
        /// 「Official髭男dism - Pretender」のような見出しでも、
        /// 曲名だけが入っているので頭は曲名の 1 文字目です
        /// (アーティスト名は取り込みのときに落としてあります)。
        /// </summary>
        private string FallbackInitial(string title, string genre)
        {
            if (title != null)
            {
                // 記号や空白は「顔」にならないので飛ばす。
                for (int i = 0; i < title.Length; i++)
                {
                    char c = title[i];
                    if (c == ' ' || c == '　') continue;
                    if (c == '「' || c == '『' || c == '"' || c == '\'') continue;
                    if (c == '[' || c == '(' || c == '【') continue;

                    return title.Substring(i, 1);
                }
            }

            if (genre != null && genre.Length > 0) return genre.Substring(0, 1);
            return "♪";
        }

        [Header("絵の代わりの色(Phase8)")]
        [Tooltip("ジャンル名から 1 つ選ぶ。空なら組み込みの 10 色")]
        public Color[] GenrePalette;

        /// <summary>
        /// <b>絵の代わりに敷く色。</b>Phase8 で作り直しました。
        ///
        /// <b>なぜ色を戻したのか</b><br/>
        /// Phase7-6 では「色はいま鳴っているものにだけ使う」として、
        /// ここを灰色 1 色にしていました。<b>サムネイルがあったから</b>成立していた
        /// 判断です。焼き込みをやめた以上、灰色のままだと
        /// <b>一覧が同じ四角の羅列</b>になり、どれがどの曲か目で追えません。
        ///
        /// <b>強調色とは絶対にぶつからない色にしてあります。</b>
        /// どれも彩度を低く抑えた淡色で、シグナルブルー(#2F6BFF)のような
        /// 鮮やかさは持ちません。<b>「色が付いている = いま」</b>という
        /// 手掛かりは壊れません。
        ///
        /// <b>ジャンルが同じなら必ず同じ色</b>になるので、
        /// 一覧を眺めるだけで「このあたりは J-POP」と分かります。
        /// これは<b>サムネイルには無かった手掛かり</b>です。
        /// </summary>
        public Color GenreColor(string genre)
        {
            if (GenrePalette != null && GenrePalette.Length > 0)
            {
                if (genre == null || genre.Length == 0) return GenrePalette[0];
                return GenrePalette[HashOf(genre) % GenrePalette.Length];
            }

            if (genre == null || genre.Length == 0)
            {
                return new Color(0.898f, 0.906f, 0.925f, 1f);   // 灰(ジャンル不明)
            }

            int slot = HashOf(genre) % 10;

            // 明るい配色に乗る淡色。彩度は 0.12〜0.20 に抑えてある。
            if (slot == 0) return new Color(0.831f, 0.878f, 0.953f, 1f);   // 空
            if (slot == 1) return new Color(0.890f, 0.855f, 0.945f, 1f);   // 藤
            if (slot == 2) return new Color(0.827f, 0.918f, 0.898f, 1f);   // 若草
            if (slot == 3) return new Color(0.968f, 0.874f, 0.843f, 1f);   // 杏
            if (slot == 4) return new Color(0.949f, 0.925f, 0.831f, 1f);   // 麦
            if (slot == 5) return new Color(0.957f, 0.851f, 0.886f, 1f);   // 桜
            if (slot == 6) return new Color(0.843f, 0.906f, 0.941f, 1f);   // 水
            if (slot == 7) return new Color(0.886f, 0.910f, 0.839f, 1f);   // 苔
            if (slot == 8) return new Color(0.925f, 0.886f, 0.847f, 1f);   // 砂
            return new Color(0.874f, 0.867f, 0.925f, 1f);                  // 霞
        }

        /// <summary>
        /// <b>頭文字の色。</b>敷いた色より 1 段濃くして、同じ色味で揃えます。
        /// 灰色の文字を置くと<b>色と文字が別々のもの</b>に見えます。
        /// </summary>
        public Color GenreInkColor(string genre)
        {
            Color face = GenreColor(genre);

            // そのままだと薄すぎるので、黒へ 62% 寄せる。
            // 色味は保ったまま濃さだけ上がるので、面と文字が同じ「一枚の絵」に見える。
            return new Color(face.r * 0.38f, face.g * 0.38f, face.b * 0.38f, 1f);
        }

        private int HashOf(string text)
        {
            int hash = 0;
            for (int i = 0; i < text.Length; i++) hash = hash * 31 + text[i];
            return hash < 0 ? -hash : hash;
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
