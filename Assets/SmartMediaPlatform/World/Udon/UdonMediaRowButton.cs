using UdonSharp;
using UnityEngine;

namespace SmartMediaPlatform.World.Udon
{
    /// <summary>
    /// <b>「何番目か」を持ったボタン。</b>
    ///
    /// <b>なぜ要るのか</b><br/>
    /// Udon のイベント(<c>SendCustomEvent</c> / <c>Button.onClick</c>)には
    /// <b>引数を渡せません</b>。一覧の 3 行目を押した、という情報を運ぶには
    /// 「3 を持ったボタン」を用意するのが Udon での定石です。
    /// 行ごとに別のイベント名(<c>PlayRow0</c>, <c>PlayRow1</c>, …)を生やす方法もありますが、
    /// 行数を変えるたびにコードを書き直すことになるので採りません。
    ///
    /// <b>使い方</b>は 2 通りあり、どちらでも同じ結果になります。
    /// <list type="bullet">
    /// <item>uGUI の <c>Button.onClick</c> から <c>Click</c> を呼ぶ</item>
    /// <item>Collider を付けて VRChat の「使う(Interact)」で押す</item>
    /// </list>
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonMediaRowButton : UdonSharpBehaviour
    {
        // 何をするボタンか。
        public const int ActionPlayLibrary = 0;
        public const int ActionSelectLibrary = 1;
        public const int ActionPlayRelated = 2;
        public const int ActionJumpInQueue = 3;
        public const int ActionRemoveFromQueue = 4;

        [Tooltip("押されたら伝える窓口")]
        public UdonMediaController Controller;

        [Tooltip("画面(行番号をページ送りに合わせるために使う)")]
        public UdonMediaPlayerUI Ui;

        [Tooltip("0=一覧を再生 / 1=一覧を選ぶ / 2=関連を再生 / 3=Queue へ飛ぶ / 4=Queue から外す")]
        public int Action = ActionPlayLibrary;

        [Tooltip("何行目のボタンか(0 から)")]
        public int Row;

        /// <summary>VRChat の「使う」。Collider があるときはこちらで押せる。</summary>
        public override void Interact()
        {
            Click();
        }

        /// <summary>押されたときの処理。<c>Button.onClick</c> からも呼べる。</summary>
        public void Click()
        {
            if (Controller == null) return;

            if (Action == ActionPlayLibrary)
            {
                Controller.PlayVisible(LibraryPosition());
            }
            else if (Action == ActionSelectLibrary)
            {
                Controller.SelectVisible(LibraryPosition());
            }
            else if (Action == ActionPlayRelated)
            {
                Controller.PlayRelated(Row);
            }
            else if (Action == ActionJumpInQueue)
            {
                Controller.JumpInQueue(Row);
            }
            else if (Action == ActionRemoveFromQueue)
            {
                Controller.RemoveFromQueue(Row);
            }
        }

        /// <summary>ページ送りを踏まえた一覧の位置。</summary>
        private int LibraryPosition()
        {
            if (Ui == null) return Row;
            return Ui.GetLibraryPositionForRow(Row);
        }
    }
}
