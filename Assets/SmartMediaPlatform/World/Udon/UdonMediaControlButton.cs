using UdonSharp;
using UnityEngine;

namespace SmartMediaPlatform.World.Udon
{
    /// <summary>
    /// <b>「使う」で押せるボタン。</b>Collider + Interact で押す<b>物理ボタン</b>用です。
    ///
    /// <b>uGUI が本線です。</b>Phase5-3 の操作 UI は World Space Canvas の
    /// <c>Button</c> で組んであります。こちらは
    /// <list type="bullet">
    /// <item>壁のスイッチや机の上のボタンなど、<b>板を直接押させたい</b>とき</item>
    /// <item>uGUI のレイキャストが通らない置き方をしてしまったときの保険</item>
    /// </list>
    /// のために残してあります。
    ///
    /// <b>Phase5-3 で変わったところ</b><br/>
    /// 送り先が <c>UdonMediaController</c> 固定ではなくなりました。
    /// <see cref="Target"/> は <c>UdonSharpBehaviour</c> なら何でもよいので、
    /// <list type="bullet">
    /// <item>窓口 …… <c>Target=UdonMediaController</c> / <c>EventName="Next"</c></item>
    /// <item>パネルの開閉 …… <c>Target=UdonMediaPanel</c> / <c>EventName="Toggle"</c></item>
    /// <item>ページ送り …… <c>Target=UdonMediaListView</c> / <c>EventName="NextPage"</c></item>
    /// </list>
    /// が<b>同じ 1 クラスで</b>書けます。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonMediaControlButton : UdonSharpBehaviour
    {
        [Tooltip("押されたら伝える相手(窓口 / パネル / 一覧 など)")]
        public UdonSharpBehaviour Target;

        [Tooltip("呼ぶイベント名。TogglePlayPause / Next / Previous / Stop / Toggle など")]
        public string EventName = "TogglePlayPause";

        [Tooltip("見出し(空なら触らない)")]
        public UnityEngine.UI.Text Label;

        [Tooltip("見出しに出す文字")]
        public string LabelText = "";

        [Header("番号を伝えたいとき(Phase7-6)")]
        [Tooltip("イベントを送る前に、相手のどの変数へ番号を書き込むか。空なら書き込まない。"
                 + "バーの何番目を押したか・キーボードの何文字目を押したか を伝えるのに使う")]
        public string IndexVariable = "";

        [Tooltip("IndexVariable へ書き込む番号")]
        public int Index;

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
            if (Target == null || EventName == null || EventName.Length == 0) return;

            // 番号を先に渡してから知らせる。順番が逆だと、相手は
            // 「前に押した番号」で動いてしまいます。
            if (IndexVariable != null && IndexVariable.Length > 0)
            {
                Target.SetProgramVariable(IndexVariable, Index);
            }

            Target.SendCustomEvent(EventName);
        }
    }
}
