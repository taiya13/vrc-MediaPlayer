using UdonSharp;
using UnityEngine;

namespace SmartMediaPlatform.World.Udon
{
    /// <summary>
    /// <b>「使う」で押せる操作ボタン。</b>
    ///
    /// VRChat で<b>いちばん確実に押せるのは Collider + Interact</b> です
    /// (ワールド内 uGUI はレイキャストの設定を間違えると押せません)。
    /// Prefab の操作ボタンはこの方式にしてあります。
    ///
    /// 呼ぶ先は <see cref="UdonMediaController"/> の<b>イベント名</b>だけなので、
    /// <b>窓口ごと差し替えても、同じ名前に応えるならボタンはそのまま動きます</b>。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonMediaControlButton : UdonSharpBehaviour
    {
        [Tooltip("押されたら伝える窓口")]
        public UdonMediaController Controller;

        [Tooltip("呼ぶイベント名。TogglePlayPause / Next / Previous / Stop / PlaySelected など")]
        public string EventName = "TogglePlayPause";

        [Tooltip("見出し(空なら触らない)")]
        public UnityEngine.UI.Text Label;

        [Tooltip("見出しに出す文字")]
        public string LabelText = "";

        void Start()
        {
            if (Label != null && LabelText.Length > 0) Label.text = LabelText;
        }

        /// <summary>VRChat の「使う」。</summary>
        public override void Interact()
        {
            Click();
        }

        /// <summary>押されたときの処理。<c>Button.onClick</c> からも呼べる。</summary>
        public void Click()
        {
            if (Controller == null || EventName == null || EventName.Length == 0) return;

            Controller.SendCustomEvent(EventName);
        }
    }
}
