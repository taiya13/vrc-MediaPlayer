using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace SmartMediaPlatform.World.Udon.UI
{
    /// <summary>
    /// <b>一覧のスクロール。</b>Phase7-3。
    ///
    /// <b>やり方</b><br/>
    /// 見えている行は 4〜5 個しか作りません(仮想リスト)。
    /// 100 曲あっても行は 5 個のままで、<b>中身だけを差し替えます</b>。
    /// これは重さのために譲れないので、
    /// <b>uGUI の <see cref="ScrollRect"/> に「動かす操作」だけを担当させます</b>。
    ///
    /// <list type="bullet">
    /// <item><b>PC のホイール</b> …… <see cref="ScrollRect"/> が最初から拾います</item>
    /// <item><b>VR のレーザーでつまみをドラッグ</b> …… <see cref="Scrollbar"/> が拾います</item>
    /// <item><b>▲▼ ボタン</b> …… 今までどおり残します
    ///       (VR で細かくつまむのが苦手な人のため)</item>
    /// </list>
    ///
    /// <b>中身は空っぽです。</b><see cref="ScrollRect"/> の中身(Content)は
    /// <b>高さだけを持つ透明な板</b>で、行はそこに入っていません。
    /// 「どこまで動かしたか」を <see cref="ScrollRect.verticalNormalizedPosition"/> から読み、
    /// それを <see cref="UdonMediaListView.Offset"/> へ写すだけです。
    /// <b>行が何個あっても、動かす仕組みの重さは変わりません。</b>
    ///
    /// <b>将来 ScrollRect へ丸ごと置き換えたくなったら</b>、
    /// このクラスを外して Content に行を並べるだけで済みます。
    /// 一覧側(<see cref="UdonMediaListView"/>)には手を入れません。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonListScroller : UdonSharpBehaviour
    {
        [Tooltip("動かす相手の一覧")]
        public UdonMediaListView List;

        [Tooltip("ホイールとドラッグを拾う ScrollRect")]
        public ScrollRect Scroll;

        [Tooltip("つまみ。ScrollRect の Vertical Scrollbar に挿してあること")]
        public Scrollbar Bar;

        [Tooltip("高さだけを持つ透明な板(ScrollRect の Content)")]
        public RectTransform Content;

        [Tooltip("1 行ぶんの高さ(px)。Content の高さを決めるのに使う")]
        public float RowPitch = 120f;

        [Tooltip("見えている行の数")]
        public int VisibleRows = 4;

        // 自分で書き込んだぶんを「人が動かした」と取り違えないための札。
        private bool _writing;

        // 直前に一覧へ渡した位置。同じ値を何度も渡さないために覚えておく。
        private int _appliedOffset = -1;

        // 直前に組み直したときの件数。変わったときだけ Content を作り直す。
        private int _knownTotal = -1;

        void Start()
        {
            Rebuild();
        }

        /// <summary>
        /// <b>つまみやホイールで動かされた。</b>
        /// <c>ScrollRect.onValueChanged</c> と <c>Scrollbar.onValueChanged</c> の
        /// どちらから呼ばれても構いません。
        /// </summary>
        public void OnScrolled()
        {
            if (_writing) return;
            if (List == null || Scroll == null) return;

            int max = MaxOffset();
            if (max <= 0) return;

            // ScrollRect の縦位置は「1 = いちばん上」。一覧の Offset は「0 = いちばん上」。
            float top = 1f - Mathf.Clamp01(Scroll.verticalNormalizedPosition);
            int offset = Mathf.RoundToInt(top * max);

            if (offset == _appliedOffset) return;
            _appliedOffset = offset;

            List.ScrollTo(offset);
        }

        /// <summary>
        /// <b>「使う」で動かされた。</b>Phase7-6。
        ///
        /// <see cref="OnScrolled"/> は <see cref="ScrollRect"/> の位置を読みますが、
        /// <b>実機ではその ScrollRect まで操作が届いていません</b>。
        /// こちらは <see cref="UdonValueStrip"/> から
        /// <b>「上から何割の所」を直接受け取ります</b>。
        /// 途中に uGUI を挟まないので、押せさえすれば必ず動きます。
        /// </summary>
        /// <param name="top01">0 = いちばん上、1 = いちばん下。</param>
        public void ScrollToFraction(float top01)
        {
            if (List == null) return;

            Rebuild();

            int max = MaxOffset();
            if (max <= 0) return;

            int offset = Mathf.RoundToInt(Mathf.Clamp01(top01) * max);
            if (offset == _appliedOffset) return;
            _appliedOffset = offset;

            List.ScrollTo(offset);

            // つまみも合わせておく(見た目だけ。ここから操作は戻ってこない)。
            if (Scroll != null)
            {
                _writing = true;
                Scroll.verticalNormalizedPosition = 1f - (float)offset / max;
                _writing = false;
            }
        }

        /// <summary>
        /// <b>一覧の側が動いた(▲▼ ボタン・検索・再生中へ追従)。</b>
        /// つまみをそちらへ合わせます。<see cref="UdonMediaListView"/> から呼ばれます。
        /// </summary>
        public void Follow()
        {
            if (List == null || Scroll == null) return;

            Rebuild();

            int max = MaxOffset();
            if (max <= 0) return;

            int offset = List.Offset;
            if (offset == _appliedOffset) return;
            _appliedOffset = offset;

            _writing = true;
            Scroll.verticalNormalizedPosition = 1f - (float)offset / max;
            _writing = false;
        }

        /// <summary>
        /// 件数が変わったら、動かせる長さを取り直す。
        ///
        /// <b>Content の高さが「全部の行の高さ」になっている</b>ので、
        /// つまみの長さが<b>ひとりでに「見えている割合」になります</b>。
        /// 100 曲あればつまみは短く、6 曲なら長い —— 自分で計算しなくても、
        /// uGUI がそうしてくれます。
        /// </summary>
        public void Rebuild()
        {
            if (List == null || Content == null) return;

            int total = List.TotalCount();
            if (total == _knownTotal) return;
            _knownTotal = total;

            // 動かせる長さが決まるのはここ。
            // <b>一覧が書き直されるたびに呼ばれます</b> —— 曲が焼き込まれるのは
            // Start より後なので、最初の 1 回だけでは「0 件」のまま固まります
            // (Phase7-3 の最初の版がそれで、まったくスクロールできませんでした)。

            float height = total * RowPitch;
            float minimum = VisibleRows * RowPitch;
            if (height < minimum) height = minimum;

            Content.sizeDelta = new Vector2(Content.sizeDelta.x, height);

            // 動かせないときは、つまみを出しっぱなしにしない。
            if (Bar != null) Bar.gameObject.SetActive(total > VisibleRows);
        }

        /// <summary>いちばん下まで動かしたときの Offset。</summary>
        private int MaxOffset()
        {
            if (List == null) return 0;

            int max = List.TotalCount() - VisibleRows;
            return max > 0 ? max : 0;
        }
    }
}
