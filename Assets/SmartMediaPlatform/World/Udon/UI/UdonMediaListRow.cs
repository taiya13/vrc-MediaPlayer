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
        [Tooltip("行番号 / ♪ などの印")]
        public Text IndexText;

        [Tooltip("見出し")]
        public Text TitleText;

        [Tooltip("2 行目(アーティストなど)")]
        public Text SubText;

        [Tooltip("長さ")]
        public Text DurationText;

        [Header("絵(Phase7)")]
        [Tooltip("曲の絵。焼き込んでいなければジャンルの色で塗る")]
        public Image Artwork;

        [Tooltip("絵が無いときに出す文字(ジャンルの頭 1 文字など)")]
        public Text ArtworkFallbackText;

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
        public Color NowPlayingTitleColor = new Color(1f, 1f, 1f, 1f);

        [Tooltip("ふだんの見出しの色")]
        public Color TitleColor = new Color(0.86f, 0.88f, 0.92f, 1f);

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
        public void ShowHeader(string channel, int count, bool expanded)
        {
            _empty = false;
            _nowPlaying = false;
            _header = true;

            SetActive(Content, false);
            SetActive(Highlight, false);
            SetActive(NowPlayingBar, false);
            SetActive(PressedMarker, false);
            SetActive(SecondaryButton, false);
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

            SetText(IndexText, "");
            SetText(TitleText, "");
            SetText(SubText, "");
            SetText(DurationText, "");

            ShowEqualizer(false);
        }

        /// <summary>中身を書く。呼ぶのは <see cref="UdonMediaListView"/> だけ。</summary>
        public void ShowItem(
            string indexLabel, string title, string sub, string duration,
            bool highlight, bool secondary)
        {
            ShowItem(indexLabel, title, sub, duration, highlight, secondary, null, "", "");
        }

        /// <summary>
        /// 中身を書く(絵つき)。Phase7。
        ///
        /// <b>絵が無くても穴が開きません。</b><paramref name="artwork"/> が
        /// <c>null</c> なら、ジャンルの色で塗って頭 1 文字を出します。
        /// 焼き込んでいないカタログでも、並びが崩れないようにするためです。
        /// </summary>
        public void ShowItem(
            string indexLabel, string title, string sub, string duration,
            bool highlight, bool secondary,
            Sprite artwork, string fallbackText, string secondaryLabel)
        {
            _empty = false;
            _nowPlaying = highlight;
            _header = false;

            SetActive(HeaderBand, false);
            SetActive(Content, true);
            SetActive(Highlight, highlight);
            SetActive(NowPlayingBar, highlight);
            SetActive(SecondaryButton, secondary);

            SetText(IndexText, indexLabel);
            SetText(TitleText, title);
            SetText(SubText, sub);
            SetText(DurationText, duration);
            SetText(SecondaryLabel, secondaryLabel);

            ShowArtwork(artwork, fallbackText);

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

        /// <summary>
        /// 絵を入れる。無ければジャンルの色で塗って頭 1 文字を出す。
        /// <b>枠の大きさは変えません</b> — 絵の有無で行の高さが変わると、
        /// 押す場所がずれてしまうためです。
        /// </summary>
        private void ShowArtwork(Sprite artwork, string fallbackText)
        {
            if (Artwork == null) return;

            bool hasArtwork = artwork != null;

            if (Artwork.sprite != artwork) Artwork.sprite = artwork;

            // 絵があるときは白(素の色)、無いときは塗りつぶしの色を活かす。
            Color wanted = hasArtwork ? Color.white : _fallbackColor;
            if (Artwork.color != wanted) Artwork.color = wanted;

            if (ArtworkFallbackText == null) return;

            SetText(ArtworkFallbackText, hasArtwork ? "" : fallbackText);
        }

        /// <summary>絵が無いときの塗り色。一覧から渡します。</summary>
        public void SetFallbackColor(Color color)
        {
            _fallbackColor = color;
        }

        private Color _fallbackColor = new Color(0.18f, 0.20f, 0.26f, 1f);

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
