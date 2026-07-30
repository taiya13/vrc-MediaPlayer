using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace SmartMediaPlatform.World.Udon.UI
{
    /// <summary>
    /// <b>操作 UI 1 枚ぶん。</b>壁パネル / タブレット / 手持ちリモコンの<b>差し替え単位</b>です。Phase5-3。
    ///
    /// <b>判断ロジックを 1 つも持っていません。</b>やることは 3 つだけです:
    /// <list type="number">
    /// <item><see cref="Core"/> から部品の参照をもらって、中の表示へ配る(<see cref="EnsureBound"/>)</item>
    /// <item><see cref="UdonMediaController"/> に自分を登録して、操作のたびに書き直してもらう</item>
    /// <item>開いている間だけ、決めた間隔で書き直す</item>
    /// </list>
    ///
    /// <b>差し替え方</b><br/>
    /// 壁パネルもリモコンも<b>このクラスのままで、中身の Prefab が違うだけ</b>です。
    /// <list type="bullet">
    /// <item>壁パネル …… <see cref="NowPlaying"/> + <see cref="Transport"/> + Library / 関連 / Queue</item>
    /// <item>リモコン …… <see cref="NowPlaying"/> + <see cref="Transport"/> + Queue だけ</item>
    /// <item>タブレット …… 同じものを小さい Canvas に並べて、<see cref="Body"/> で開閉する</item>
    /// </list>
    /// <b>新しい操作 UI を作るのにコードは 1 行も要りません。</b>
    ///
    /// <b>置き方</b><br/>
    /// ワールドのどこに置いても構いません。埋めるのは <see cref="Core"/> <b>1 つだけ</b>で、
    /// 残りはそこから引いてきます。同じ Core に<b>何枚でも</b>ぶら下げられます。
    ///
    /// <b>Phase5-2 の <c>UdonMediaPlayerUI</c> との違い</b><br/>
    /// あちらは <c>Text[]</c> を 3 種類抱えた 1 枚岩で、
    /// <c>UdonMediaController</c> が<b>1 枚だけ</b>を名指しで持っていました
    /// (= パネルは世界に 1 枚しか置けない)。
    /// ここでは<b>パネルが自分から名乗り出る</b>ので、枚数の上限が Prefab の都合から消えました。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonMediaPanel : UdonSharpBehaviour
    {
        [Header("★ ここだけ埋めれば動く")]
        [Tooltip("シーンに置いた SmartMediaPlayer を挿す。ほかの欄はここから引いてくる")]
        public UdonSmartMediaPlayer Core;

        [Header("見た目")]
        [Tooltip("見出しに出す文字")]
        public string PanelName = "";

        [Tooltip("ワールドに入った時点で開いておく")]
        public bool OpenOnStart = true;

        [Tooltip("何秒ごとに書き直すか。0 なら操作されたときだけ")]
        [Range(0f, 2f)]
        public float RefreshInterval = 0.5f;

        [Header("差し替えたいとき(空なら Core から)")]
        [Tooltip("操作を伝える窓口")]
        public UdonMediaController Controller;

        [Tooltip("表示するもとの状態")]
        public UdonPlayerSession Session;

        [Tooltip("表示用データの窓口")]
        public UdonCatalogStore Store;

        [Tooltip("音量の行き先")]
        public UdonMediaScreen Screen;

        [Header("中身(Prefab が配線済み)")]
        [Tooltip("いま鳴っているもの")]
        public UdonNowPlayingView NowPlaying;

        [Tooltip("再生操作のボタン群")]
        public UdonTransportView Transport;

        [Tooltip("Library / 関連 / Queue。並べたぶんだけ書き直す")]
        public UdonMediaListView[] Lists;

        [Tooltip("直近の操作の結果")]
        public Text StatusText;

        [Tooltip("パネルの名前を出す先(空なら触らない)")]
        public Text TitleLabel;

        [Tooltip("開け閉てする入れ物。必ず「子」を指すこと(このパネル自身は不可)。空なら開きっぱなし")]
        public GameObject Body;

        private bool _bound;
        private float _nextRefresh;
        private string _status = "";

        void Start()
        {
            EnsureBound();

            if (Body != null) Body.SetActive(OpenOnStart);

            Refresh();
        }

        void Update()
        {
            if (RefreshInterval <= 0f) return;
            if (!IsOpen) return;
            if (Time.time < _nextRefresh) return;

            _nextRefresh = Time.time + RefreshInterval;
            Refresh();
        }

        /// <summary>
        /// 参照を配って、書き直しの知らせを受け取れるようにする。
        ///
        /// <b>Start の順序に依存しません。</b>引いてくるのは Inspector で刺さっている
        /// 参照だけで、相手の <c>Start</c> の結果は見ないからです。
        /// </summary>
        public void EnsureBound()
        {
            if (_bound) return;
            _bound = true;

            if (Core != null)
            {
                if (Controller == null) Controller = Core.Controller;
                if (Session == null) Session = Core.Session;
                if (Store == null) Store = Core.Store;
                if (Screen == null) Screen = Core.Screen;
            }

            if (NowPlaying != null)
            {
                if (NowPlaying.Session == null) NowPlaying.Session = Session;
                if (NowPlaying.Store == null) NowPlaying.Store = Store;
                if (NowPlaying.Backend == null && Session != null) NowPlaying.Backend = Session.Backend;
                if (NowPlaying.Sync == null && Controller != null) NowPlaying.Sync = Controller.Sync;
            }

            if (Transport != null)
            {
                if (Transport.Controller == null) Transport.Controller = Controller;
                if (Transport.Session == null) Transport.Session = Session;
                if (Transport.Screen == null) Transport.Screen = Screen;
                if (Transport.Panel == null) Transport.Panel = this;
            }

            if (Lists != null)
            {
                for (int i = 0; i < Lists.Length; i++)
                {
                    UdonMediaListView list = Lists[i];
                    if (list == null) continue;

                    if (list.Controller == null) list.Controller = Controller;
                    if (list.Session == null) list.Session = Session;
                    if (list.Store == null) list.Store = Store;
                    if (list.Panel == null) list.Panel = this;

                    BindRows(list);
                }
            }

            if (TitleLabel != null && PanelName.Length > 0) TitleLabel.text = PanelName;

            // 操作のたびに Refresh を送ってもらう。
            if (Controller != null) Controller.AddListener(this);
        }

        /// <summary>開いているか。<see cref="Body"/> が空なら常に開いている扱い。</summary>
        public bool IsOpen
        {
            get { return Body == null || Body.activeSelf; }
        }

        /// <summary>中身を全部書き直す。<see cref="UdonMediaController"/> からも呼ばれる。</summary>
        public void Refresh()
        {
            EnsureBound();

            if (NowPlaying != null) NowPlaying.Refresh();
            if (Transport != null) Transport.Refresh();

            if (Lists != null)
            {
                for (int i = 0; i < Lists.Length; i++)
                {
                    if (Lists[i] != null) Lists[i].Refresh();
                }
            }

            if (StatusText != null && StatusText.text != _status) StatusText.text = _status;
        }

        /// <summary>状態の 1 行を差し替える。中の表示から呼ばれる。</summary>
        public void SetStatus(string message)
        {
            _status = message == null ? "" : message;
            if (StatusText != null) StatusText.text = _status;
        }

        // ───────── 開閉(ボタンからそのまま呼べる)─────────

        public void Open()
        {
            EnsureBound();
            if (Body != null) Body.SetActive(true);

            Refresh();
        }

        public void Close()
        {
            if (Body != null) Body.SetActive(false);
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        // ───────── 内部 ─────────

        /// <summary>一覧の行に「自分の一覧」を教える(Prefab の挿し忘れを減らすため)。</summary>
        private void BindRows(UdonMediaListView list)
        {
            if (list.Rows == null) return;

            for (int row = 0; row < list.Rows.Length; row++)
            {
                UdonMediaListRow target = list.Rows[row];
                if (target == null) continue;

                if (target.List == null) target.List = list;
            }
        }
    }
}
