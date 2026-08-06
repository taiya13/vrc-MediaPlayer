using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace SmartMediaPlatform.World.Udon.UI
{
    /// <summary>
    /// <b>バーを「使う」(Interact)で動かせるようにする。</b>Phase7-6。
    ///
    /// <b>なぜ要るのか</b><br/>
    /// このワールドでは、<b>uGUI のポインター操作が実機で一切届いていません</b>。
    /// ボタンだけ動いていたのは、ボタンには別途 Collider +「使う」を
    /// 付けてあったからです。つまり
    /// <list type="bullet">
    /// <item>押す(Button) …… 「使う」で動く</item>
    /// <item>つまむ(Slider / Scrollbar) …… 沈黙</item>
    /// <item>打つ(InputField) …… 沈黙</item>
    /// </list>
    /// という壊れ方でした。EventSystem・BoxCollider・VRC_UiShape を
    /// 順に足しても直らなかったので、<b>動くと分かっている道(「使う」)の側へ
    /// バーを寄せます</b>。
    ///
    /// <b>やり方</b><br/>
    /// バーの上に、細長い当たり判定を <see cref="SegmentCount"/> 個並べます。
    /// どれか 1 つを「使う」と、その位置の値をバーへ書き込みます。
    /// つまみを<b>掴んで滑らせる</b>のではなく、<b>行きたい所を指して使う</b>操作です。
    /// VR のレーザーでは、こちらのほうがむしろ狙いやすくなります。
    ///
    /// <b>uGUI 側は残してあります。</b>値を書き込むと
    /// <c>onValueChanged</c> も走るので、将来 uGUI が実機で通るようになれば
    /// つまみを掴む操作もそのまま増えるだけです。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonValueStrip : UdonSharpBehaviour
    {
        [Header("動かす相手(どちらか片方)")]
        [Tooltip("音量・再生位置のバー")]
        public Slider Bar;

        [Tooltip("一覧のスクロールバー")]
        public Scrollbar ScrollBar;

        [Header("設定")]
        [Tooltip("いくつに区切るか。多いほど細かく狙えるが、1 区画が狭くなる")]
        public int SegmentCount = 16;

        [Tooltip("上が 0 のスクロールバー用。値を裏返す")]
        public bool Invert;

        [Header("知らせる相手(任意)")]
        [Tooltip("値を書き込んだあとに呼ぶ相手。"
                 + "uGUI の onValueChanged が実機で走らなくても届くようにするための保険")]
        public UdonSharpBehaviour Target;

        [Tooltip("Target で呼ぶイベント名")]
        public string EventName = "";

        /// <summary>
        /// <b>押された区画の番号。</b>
        /// <see cref="UdonMediaControlButton.IndexVariable"/> から書き込まれます。
        /// </summary>
        public int PickedIndex;

        /// <summary>
        /// <see cref="PickedIndex"/> の位置へバーを動かす。
        /// <see cref="UdonMediaControlButton"/> から <c>SendCustomEvent</c> で呼ばれます。
        /// </summary>
        public void Pick()
        {
            int count = SegmentCount;
            if (count < 2) count = 2;

            int index = PickedIndex;
            if (index < 0) index = 0;
            if (index > count - 1) index = count - 1;

            // 端まで届かせたいので、区画の<b>中央</b>ではなく<b>境目</b>で数えます。
            // 中央だと、いちばん左でも 0 にならず、いちばん右でも 1 になりません
            // (音量 0% と 100% が出せなくなります)。
            float t = (float)index / (float)(count - 1);
            if (Invert) t = 1f - t;

            Apply(t);

            if (Target != null && EventName != null && EventName.Length > 0)
            {
                Target.SendCustomEvent(EventName);
            }
        }

        private void Apply(float t)
        {
            if (Bar != null)
            {
                Bar.value = Bar.minValue + (Bar.maxValue - Bar.minValue) * t;
                return;
            }

            if (ScrollBar != null) ScrollBar.value = t;
        }

        /// <summary>Console 表示用の 1 行。</summary>
        public string Describe()
        {
            string what = Bar != null ? "バー" : (ScrollBar != null ? "スクロール" : "行き先なし");
            return what + " を " + SegmentCount + " 区画で操作";
        }
    }
}
