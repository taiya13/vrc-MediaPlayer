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

        [Header("出し分け(空でも動く)")]
        [Tooltip("中身がある行だけ出す入れ物。必ず「子」を指すこと(この行自身は不可)")]
        public GameObject Content;

        [Tooltip("いま鳴っている行の印")]
        public GameObject Highlight;

        [Tooltip("2 つめのボタン(＋ Queue / × 削除)。使えない行では隠す")]
        public GameObject SecondaryButton;

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

        /// <summary>空行にする。</summary>
        public void ShowEmpty()
        {
            _empty = true;

            SetActive(Content, false);
            SetActive(Highlight, false);
            SetActive(SecondaryButton, false);

            SetText(IndexText, "");
            SetText(TitleText, "");
            SetText(SubText, "");
            SetText(DurationText, "");
        }

        /// <summary>中身を書く。呼ぶのは <see cref="UdonMediaListView"/> だけ。</summary>
        public void ShowItem(
            string indexLabel, string title, string sub, string duration,
            bool highlight, bool secondary)
        {
            _empty = false;

            SetActive(Content, true);
            SetActive(Highlight, highlight);
            SetActive(SecondaryButton, secondary);

            SetText(IndexText, indexLabel);
            SetText(TitleText, title);
            SetText(SubText, sub);
            SetText(DurationText, duration);
        }

        // ───────── 内部 ─────────

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
