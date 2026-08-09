using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace SmartMediaPlatform.World.Udon.UI
{
    /// <summary>
    /// <b>一覧の 1 行。</b>Phase5-3。
    ///
    /// <b>判断ロジックを 1 つも持っていません。</b>
    /// <see cref="UdonMediaListView"/> から渡された文字を <see cref="Text"/> へ書くだけ、
    /// 押されたら「自分は何行目か」を添えて一覧へ返すだけです。
    ///
    /// <b>Phase5-2 の <c>UdonMediaRowButton</c> との違い</b><br/>
    /// あちらは「何をするボタンか」を <c>Action</c> 番号で持ち、
    /// 押されると <c>UdonMediaController</c> を直接叩いていました。
    /// つまり<b>行が操作の意味を知っていた</b>ので、
    /// 一覧の種類が増えるたびに番号を足す必要がありました。
    /// こちらは<b>行番号を返すだけ</b>で、意味づけは一覧側の仕事です。
    /// おかげで Library / 関連 / Queue のどれでも<b>同じ行が使い回せます</b>。
    ///
    /// <b>押し方は uGUI の <see cref="Button"/></b> です
    /// (<c>onClick</c> → この behaviour の <c>Click</c> / <c>ClickSecondary</c>)。
    /// Collider を付ければ VRChat の「使う」でも押せます。
    ///
    /// <b>注意:</b> <see cref="Content"/> にこの行自身の GameObject を割り当てないでください。
    /// 非アクティブになった UdonBehaviour はイベントを受け取れないため、
    /// 一度空行になると二度と書き戻せなくなります(必ず<b>子</b>を指すこと)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonMediaListRow : UdonSharpBehaviour
    {
        [Tooltip("この行が属する一覧。パネルが自動で入れる")]
        public UdonMediaListView List;

        [Tooltip("上から何行目か(0 から)")]
        public int Row;

        [Header("文字(空でも動く)")]
        [Tooltip("行番号。鳴っている行だけ、ここが動く 3 本の棒に入れ替わる")]
        public Text IndexText;

        [Tooltip("見出し")]
        public Text TitleText;

        [Tooltip("2 行目(アーティストなど)")]
        public Text SubText;

        [Tooltip("長さ")]
        public Text DurationText;

        [Header("出し分け(空でも動く)")]
        [Tooltip("中身がある行だけ出す入れ物。必ず「子」を指すこと(この行自身は不可)")]
        public GameObject Content;

        [Tooltip("いま鳴っている行の印(行全体にかかる薄い色)")]
        public GameObject Highlight;

        [Tooltip("いま鳴っている行の印(左端の縦棒)。離れて見てもすぐ分かる")]
        public GameObject NowPlayingBar;

        [Tooltip("押した直後だけ出る印。「使う」で押したときの手応えになる")]
        public GameObject PressedMarker;

        [Tooltip("2 つめのボタン(予定へ追加 / 予定から外す)。使えない行では隠す")]
        public GameObject SecondaryButton;

        [Tooltip("2 つめのボタンに出す文字。何をするボタンかを常に見せる")]
        public Text SecondaryLabel;

        [Header("お気に入り(Phase7-8)")]
        [Tooltip("♥ のボタン。空でも動く")]
        public GameObject FavoriteButton;

        [Tooltip("♥ の文字。入っているかどうかを色で示す")]
        public Text FavoriteLabel;

        [Tooltip("お気に入りに入っているときの ♥ の色")]
        public Color FavoriteOnColor = new Color(0f, 0.478f, 1f, 1f);

        [Tooltip("入っていないときの ♥ の色")]
        public Color FavoriteOffColor = new Color(0.741f, 0.749f, 0.776f, 1f);

        [Header("チャンネルの見出しとして使うとき(Phase7-2)")]
        [Tooltip("見出しのときだけ出す帯。空なら見出しにできない")]
        public GameObject HeaderBand;

        [Tooltip("見出しの文字(チャンネル名)")]
        public Text HeaderText;

        [Tooltip("見出しの右の件数")]
        public Text HeaderCountText;

        [Tooltip("たたんでいるかを示す印(▼ / ▶)")]
        public Text HeaderArrowText;

        [Header("鳴っている印(Phase7)")]
        [Tooltip("動く 3 本の棒。いま鳴っている行だけ動かす。空でも動く")]
        public RectTransform[] EqualizerBars;

        [Tooltip("棒が動く速さ")]
        [Range(0.5f, 6f)]
        public float EqualizerSpeed = 2.4f;

        [Tooltip("棒のいちばん低いとき(0〜1)")]
        [Range(0f, 1f)]
        public float EqualizerFloor = 0.25f;

        [Header("文字の色")]
        [Tooltip("いま鳴っている行の見出しはこの色にする")]
        public Color NowPlayingTitleColor = new Color(0f, 0.478f, 1f, 1f);

        [Tooltip("ふだんの見出しの色")]
        public Color TitleColor = new Color(0.114f, 0.114f, 0.122f, 1f);

        /// <summary>いま空行か(診断用)。</summary>
        public bool IsEmpty { get { return _empty; } }

        private bool _empty = true;

        /// <summary>VRChat の「使う」。Collider があるときはこちらでも押せる。</summary>
        public override void Interact()
        {
            Click();
        }

        /// <summary>行そのものを押した。<c>Button.onClick</c> から呼ぶ。</summary>
        public void Click()
        {
            if (List != null) List.OnRowPrimary(Row);
        }

        /// <summary>2 つめのボタンを押した。<c>Button.onClick</c> から呼ぶ。</summary>
        /// <summary>♥ が押された。一覧に伝えるだけ。</summary>
        public void ClickFavorite()
        {
            if (List == null) return;
            List.OnRowFavorite(Row);
        }

        /// <summary>
        /// ♥ の見た目を合わせる。
        /// <b>形ではなく色で示します</b> —— ♥ と ♡ を出し分けると、
        /// 組み込みフォントに ♡ が無い環境で<b>豆腐(□)</b>になるためです。
        /// </summary>
        public void ShowFavorite(bool on, bool visible)
        {
            SetActive(FavoriteButton, visible);

            if (FavoriteLabel == null) return;

            Color wanted = on ? FavoriteOnColor : FavoriteOffColor;
            if (FavoriteLabel.color != wanted) FavoriteLabel.color = wanted;
        }

        public void ClickSecondary()
        {
            if (List != null) List.OnRowSecondary(Row);
        }

        /// <summary>
        /// <b>チャンネルの見出しにする。</b>Phase7-2。
        ///
        /// <b>行を作り分けていません。</b>同じ行を、曲としても見出しとしても使います。
        /// 見出し専用の行を別に持つと、
        /// <b>「見出しが何個要るか」を先に知らないと行を用意できません</b>。
        /// 検索でチャンネルが減れば見出しも減るので、それは決められません。
        /// </summary>
        public void ShowHeader(string channel, int count, bool expanded, bool selected)
        {
            _empty = false;
            _nowPlaying = false;
            _header = true;

            SetActive(Content, false);

            // ── 選ばれている見出しに印を出す(Phase8-2)。
            //    レールでは<b>これが唯一「いまどれを見ているか」を示すもの</b>です。
            //    曲の一覧側には手掛かりが無いので、ここを落とすと迷子になります。
            SetActive(Highlight, selected);
            SetActive(NowPlayingBar, selected);
            SetActive(PressedMarker, false);
            SetActive(SecondaryButton, false);
            SetActive(FavoriteButton, false);
            ShowEqualizer(false);

            SetActive(HeaderBand, true);
            SetText(HeaderText, channel);
            SetText(HeaderCountText, count + " 曲");

            // ▼ は開いている、▶ はたたんでいる。世の中の折りたたみと同じ向き。
            SetText(HeaderArrowText, expanded ? "▼" : "▶");
        }

        /// <summary>いま見出しの行か。押されたときの行き先を変えるのに使う。</summary>
        public bool IsHeader { get { return _header; } }

        /// <summary>空行にする。</summary>
        public void ShowEmpty()
        {
            _empty = true;
            _nowPlaying = false;
            _header = false;

            SetActive(HeaderBand, false);
            SetActive(Content, false);
            SetActive(Highlight, false);
            SetActive(NowPlayingBar, false);
            SetActive(PressedMarker, false);
            SetActive(SecondaryButton, false);
            SetActive(FavoriteButton, false);

            SetText(IndexText, "");
            SetText(TitleText, "");
            SetText(SubText, "");
            SetText(DurationText, "");

            ShowEqualizer(false);
        }

        /// <summary>
        /// 中身を書く。呼ぶのは <see cref="UdonMediaListView"/> だけ。
        ///
        /// <b>絵は受け取りません。</b>Phase8-2。
        /// 行の左端は <see cref="IndexText"/> の<b>番号</b>で、
        /// 鳴っている行だけ、そこが<b>動く 3 本の棒</b>に入れ替わります
        /// (<paramref name="highlight"/> が true のとき)。
        ///
        /// 番号と棒は<b>同じ場所に置いて、片方だけを出します</b>。
        /// 別々の場所に置くと、鳴っている行だけ文字の開始位置がずれて、
        /// 一覧の左端が<b>がたつきます</b>。
        /// </summary>
        public void ShowItem(
            string indexLabel, string title, string sub, string duration,
            bool highlight, bool secondary, string secondaryLabel)
        {
            _empty = false;
            _nowPlaying = highlight;
            _header = false;

            SetActive(HeaderBand, false);
            SetActive(Content, true);
            SetActive(Highlight, highlight);
            SetActive(NowPlayingBar, highlight);
            SetActive(SecondaryButton, secondary);

            // 鳴っている行では番号を消す。棒と同じ場所に出しているので、
            // 両方出すと重なって読めなくなる。
            SetText(IndexText, highlight ? "" : indexLabel);
            SetText(TitleText, title);
            SetText(SubText, sub);
            SetText(DurationText, duration);
            SetText(SecondaryLabel, secondaryLabel);

            // 鳴っている行だけ見出しを明るくする。
            // 色の差は、離れて見たときに縦棒より先に目に入る。
            if (TitleText != null)
            {
                Color wanted = highlight ? NowPlayingTitleColor : TitleColor;
                if (TitleText.color != wanted) TitleText.color = wanted;
            }

            // 色と縦棒だけだと、色が見えにくい人には差が伝わらない。
            // 動きは、色に頼らずに「これが鳴っている」と分かる 3 つめの手がかり。
            ShowEqualizer(highlight);
        }

        /// <summary>
        /// 鳴っている行の棒を動かす。
        ///
        /// <b>鳴っている 1 行だけが Update を使います。</b>
        /// 全部の行で毎フレーム動かすと、5 行 × 3 本 = 15 個の
        /// RectTransform を触ることになって重くなります。
        /// </summary>
        void Update()
        {
            if (!_nowPlaying || EqualizerBars == null) return;

            for (int i = 0; i < EqualizerBars.Length; i++)
            {
                RectTransform bar = EqualizerBars[i];
                if (bar == null) continue;

                // 棒ごとに波をずらす。そろって動くと機械的に見える。
                float phase = Time.time * EqualizerSpeed + i * 1.7f;
                float wave = (Mathf.Sin(phase) + 1f) * 0.5f;

                float height = EqualizerFloor + (1f - EqualizerFloor) * wave;

                bar.anchorMin = new Vector2(bar.anchorMin.x, 0f);
                bar.anchorMax = new Vector2(bar.anchorMax.x, height);
                bar.offsetMin = new Vector2(bar.offsetMin.x, 0f);
                bar.offsetMax = new Vector2(bar.offsetMax.x, 0f);
            }
        }

        /// <summary>
        /// 押した直後の印を出す / 消す。
        ///
        /// <b>uGUI の色変化(ColorTint)は「使う」で押したときには出ません。</b>
        /// VRChat のレーザーで押したのか、押せていないのかが分からないと
        /// 何度も押してしまうので、どちらの押し方でも手応えが返るようにしています。
        /// </summary>
        public void SetPressed(bool pressed)
        {
            SetActive(PressedMarker, pressed && !_empty);
        }

        // ───────── 内部 ─────────

        private bool _nowPlaying;
        private bool _header;

        private void ShowEqualizer(bool visible)
        {
            if (EqualizerBars == null) return;

            for (int i = 0; i < EqualizerBars.Length; i++)
            {
                if (EqualizerBars[i] == null) continue;
                SetActive(EqualizerBars[i].gameObject, visible);
            }
        }

        // 同じ値なら触らない。uGUI は text / SetActive のたびに再レイアウトが走るため。
        private void SetText(Text target, string value)
        {
            if (target == null) return;
            if (target.text == value) return;
            target.text = value;
        }

        private void SetActive(GameObject target, bool value)
        {
            if (target == null) return;
            if (target.activeSelf == value) return;
            target.SetActive(value);
        }
    }
}
