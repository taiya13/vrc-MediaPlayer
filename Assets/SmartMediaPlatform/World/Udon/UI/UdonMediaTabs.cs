using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace SmartMediaPlatform.World.Udon.UI
{
    /// <summary>
    /// <b>一覧の切り替え。</b>Phase7。
    ///
    /// <b>なぜ切り替えにしたのか</b><br/>
    /// Phase6 までは「すべての曲」「おすすめ」「再生予定」を<b>同時に</b>出していました。
    /// 13 行が同じ大きさで並び、<b>どこを見ればよいか分からない</b>状態でした。
    /// 1 本だけにすると、1 行あたりに使える面積が 2 倍以上になり、
    /// 絵と 2 行の文字が入ります。
    ///
    /// <b>判断を持っていません。</b>やることは 2 つだけです。
    /// <list type="number">
    /// <item>押されたタブの一覧だけを表示する</item>
    /// <item>タブの見出しに<b>いま何件あるか</b>を書く</item>
    /// </list>
    /// 何件あるかは <see cref="UdonMediaListView.TotalCount"/> に聞くだけで、
    /// 中身は知りません。
    ///
    /// <b>件数を出すのは、切り替えないと分からないのを防ぐため</b>です。
    /// 「再生予定 3」と見えていれば、押さなくても入っていることが分かります。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonMediaTabs : UdonSharpBehaviour
    {
        [Header("切り替える一覧(並びがタブの並び)")]
        public UdonMediaListView[] Lists;

        [Tooltip("一覧を包む入れ物。必ず一覧の「親」を指すこと")]
        public GameObject[] Pages;

        [Header("タブのボタン")]
        [Tooltip("押されている側の背景。選ばれているタブだけ出す")]
        public GameObject[] SelectedMarks;

        [Tooltip("タブの文字。件数を添えて書き換える")]
        public Text[] Labels;

        [Tooltip("選ばれているタブの文字色")]
        public Color SelectedColor = new Color(1f, 1f, 1f, 1f);

        [Tooltip("選ばれていないタブの文字色")]
        public Color NormalColor = new Color(0.66f, 0.70f, 0.78f, 1f);

        [Header("状態")]
        [Tooltip("最初に開くタブ")]
        public int Selected;

        private bool _initialized;

        void Start()
        {
            EnsureInitialized();
        }

        /// <summary>遅延初期化。Start の順序に関係なく安全に呼べる。</summary>
        public void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            Apply();
        }

        public int TabCount
        {
            get { return Lists == null ? 0 : Lists.Length; }
        }

        // ───────── ボタンからそのまま呼べる ─────────

        public void SelectTab0()
        {
            Select(0);
        }

        public void SelectTab1()
        {
            Select(1);
        }

        public void SelectTab2()
        {
            Select(2);
        }

        public void Select(int index)
        {
            if (index < 0 || index >= TabCount) return;
            if (index == Selected && _initialized) return;

            Selected = index;
            Apply();
        }

        /// <summary>
        /// 見出しの件数を書き直す。<see cref="UdonMediaPanel"/> から呼ばれます。
        /// <b>開いていないタブの件数も書きます</b> — それが件数を出す理由なので。
        /// </summary>
        public void Refresh()
        {
            EnsureInitialized();
            RefreshLabels();
        }

        // ───────── 内部 ─────────

        private void Apply()
        {
            for (int i = 0; i < TabCount; i++)
            {
                bool active = i == Selected;

                if (Pages != null && i < Pages.Length) SetActive(Pages[i], active);
                if (SelectedMarks != null && i < SelectedMarks.Length)
                {
                    SetActive(SelectedMarks[i], active);
                }

                if (Labels == null || i >= Labels.Length || Labels[i] == null) continue;

                Color wanted = active ? SelectedColor : NormalColor;
                if (Labels[i].color != wanted) Labels[i].color = wanted;
            }

            RefreshLabels();

            // 開いた一覧はすぐ書き直す。切り替えた瞬間に古い中身が見えるのを避ける。
            if (Lists != null && Selected >= 0 && Selected < Lists.Length
                && Lists[Selected] != null)
            {
                Lists[Selected].Refresh();
            }
        }

        private void RefreshLabels()
        {
            if (Labels == null || Lists == null) return;

            for (int i = 0; i < TabCount && i < Labels.Length; i++)
            {
                if (Labels[i] == null || Lists[i] == null) continue;

                string label = Lists[i].EffectiveHeader();

                // 0 件のときは数を出さない。「おすすめ 0」は読んでいて気持ちが良くない。
                int count = Lists[i].TotalCount();
                if (count > 0) label += "  " + count;

                if (Labels[i].text != label) Labels[i].text = label;
            }
        }

        /// <summary>いま開いている一覧。パネルが書き直すときに使う。</summary>
        public UdonMediaListView Current()
        {
            if (Lists == null || Selected < 0 || Selected >= Lists.Length) return null;
            return Lists[Selected];
        }

        private void SetActive(GameObject target, bool value)
        {
            if (target == null) return;
            if (target.activeSelf == value) return;
            target.SetActive(value);
        }
    }
}
