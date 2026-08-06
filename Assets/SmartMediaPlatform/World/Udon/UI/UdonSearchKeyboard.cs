using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace SmartMediaPlatform.World.Udon.UI
{
    /// <summary>
    /// <b>ワールドの中に置くキーボード。</b>Phase7-6。
    ///
    /// <b>なぜ要るのか</b><br/>
    /// 検索欄(<c>InputField</c>)は<b>uGUI のポインターで押されないと開きません</b>。
    /// このワールドでは uGUI のポインターが実機で届いていないため、
    /// 検索欄は押しても何も起きませんでした。
    /// <b>「使う」(Interact)は確実に動いている</b>ので、
    /// 文字をぜんぶ「使う」で押せるボタンに置き換えます。
    ///
    /// <b>検索の仕組みには手を入れていません。</b>
    /// ここがやるのは <see cref="Field"/> の文字を書き換えることだけで、
    /// 絞り込みは今までどおり <see cref="UdonMediaListView"/> が
    /// 検索欄の文字を読んで行います。<b>探し方・探す対象は変わりません</b>
    /// (曲名・アーティスト・チャンネル・ジャンル・タグ)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonSearchKeyboard : UdonSharpBehaviour
    {
        [Header("つなぎ先")]
        [Tooltip("書き込む検索欄")]
        public InputField Field;

        [Tooltip("打った文字を出す先(検索欄が読みにくいとき用。空でも動く)")]
        public Text Preview;

        [Tooltip("打つたびに絞り込み直してもらう相手。空でも動く(次の書き直しで反映される)")]
        public UdonMediaListView List;

        [Tooltip("開け閉てする板。空ならいつも出しっぱなし")]
        public GameObject Sheet;

        [Header("配列")]
        [Tooltip("キーに並べる文字。1 文字 = 1 キー。並び順がそのままボタンの順番")]
        public string Keys = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 ";

        [Tooltip("何も打っていないときに出す文字")]
        public string EmptyLabel = "(文字を押して検索)";

        /// <summary>
        /// <b>押されたキーの番号。</b>
        /// <see cref="UdonMediaControlButton.IndexVariable"/> から書き込まれます。
        /// </summary>
        public int PickedIndex;

        private string _text = "";
        private bool _initialized;

        void Start()
        {
            EnsureInitialized();
            RefreshPreview();
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            // 先に何か入っていれば、それを引き継ぐ。
            if (Field != null && Field.text != null) _text = Field.text;
        }

        /// <summary>1 文字足す。<see cref="UdonMediaControlButton"/> から呼ばれます。</summary>
        public void PressKey()
        {
            EnsureInitialized();

            if (Keys == null) return;
            if (PickedIndex < 0 || PickedIndex >= Keys.Length) return;

            _text = _text + Keys.Substring(PickedIndex, 1);
            Apply();
        }

        /// <summary>1 文字消す。</summary>
        public void Backspace()
        {
            EnsureInitialized();

            if (_text.Length == 0) return;

            _text = _text.Substring(0, _text.Length - 1);
            Apply();
        }

        /// <summary>ぜんぶ消す。</summary>
        public void ClearAll()
        {
            EnsureInitialized();

            if (_text.Length == 0) return;

            _text = "";
            Apply();
        }

        /// <summary>キーボードを開け閉てする。</summary>
        public void Toggle()
        {
            if (Sheet == null) return;

            Sheet.SetActive(!Sheet.activeSelf);
        }

        /// <summary>開く。</summary>
        public void Open()
        {
            if (Sheet != null) Sheet.SetActive(true);
        }

        /// <summary>閉じる。</summary>
        public void Close()
        {
            if (Sheet != null) Sheet.SetActive(false);
        }

        private void Apply()
        {
            if (Field != null) Field.text = _text;

            RefreshPreview();

            // 検索欄の中身は一覧が毎回読み直しますが、
            // その周期(0.5 秒)を待たずに反応させたいので直接知らせます。
            if (List != null) List.OnSearchChanged();
        }

        private void RefreshPreview()
        {
            if (Preview == null) return;

            string label = _text.Length > 0 ? _text : EmptyLabel;
            if (Preview.text != label) Preview.text = label;
        }
    }
}
