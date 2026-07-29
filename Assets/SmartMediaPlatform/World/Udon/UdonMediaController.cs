using UdonSharp;
using UnityEngine;

namespace SmartMediaPlatform.World.Udon
{
    /// <summary>
    /// <b>操作の窓口。</b>Phase5-1 の <c>MediaController</c> の Udon 版。
    ///
    /// <b>再生の判断は 1 つも持っていません。</b>
    /// <see cref="UdonPlayerSession"/> への<b>呼び出しの中継だけ</b>です。
    /// 次に何を再生するかは今までどおりセッションが決めます。
    ///
    /// <b>すべて引数なしの public メソッド</b>にしてあります。
    /// これは Udon の都合ではなく、そうしておくと
    /// <list type="bullet">
    /// <item>uGUI の <c>Button.onClick</c> にそのまま繋がる</item>
    /// <item><c>SendCustomEvent("Next")</c> で名前だけで呼べる</item>
    /// </list>
    /// ので、<b>UI を差し替えても操作の意味が変わらない</b>ためです。
    /// 「どれを」は <see cref="UdonPlayerSession.Select(int)"/> で先に選ぶ形にしています
    /// (index 付きの操作は <see cref="UdonMediaRowButton"/> が担当)。
    ///
    /// <b>差し替え方</b>:この窓口ごと別の <c>UdonSharpBehaviour</c> に替えても、
    /// 同じイベント名に応えるなら UI もボタンもそのまま動きます。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonMediaController : UdonSharpBehaviour
    {
        [Tooltip("操作を伝える相手")]
        public UdonPlayerSession Session;

        [Tooltip("操作のたびに書き直す画面(任意)")]
        public UdonMediaPlayerUI Ui;

        /// <summary>直近の操作が通ったか(診断用)。</summary>
        public bool LastResult;

        /// <summary>組み立て済みか。</summary>
        public bool IsReady
        {
            get { return Session != null; }
        }

        // ───────── 再生制御 ─────────

        public void Play()
        {
            Done(Session != null && Session.Play());
        }

        public void TogglePlayPause()
        {
            Done(Session != null && Session.TogglePlayPause());
        }

        public void Next()
        {
            Done(Session != null && Session.Next());
        }

        public void Previous()
        {
            Done(Session != null && Session.Previous());
        }

        public void Stop()
        {
            Done(Session != null && Session.Stop());
        }

        // ───────── 一覧から ─────────

        public void PlaySelected()
        {
            Done(Session != null && Session.PlaySelected());
        }

        public void EnqueueSelected()
        {
            Done(Session != null && Session.EnqueueSelected());
        }

        public void PlayNextSelected()
        {
            Done(Session != null && Session.PlayNextSelected());
        }

        public void ClearUpcoming()
        {
            Done(Session != null && Session.ClearUpcoming() > 0);
        }

        // ───────── index 付きの操作 ─────────
        // ボタンからは引数を渡せないので、UdonMediaRowButton がここを呼びます。

        /// <summary>一覧の <paramref name="position"/> 番目を選ぶ。</summary>
        public bool SelectVisible(int position)
        {
            bool ok = Session != null && Session.SelectAt(position);
            Done(ok);
            return ok;
        }

        /// <summary>一覧の <paramref name="position"/> 番目を再生する。</summary>
        public bool PlayVisible(int position)
        {
            bool ok = Session != null && Session.PlayVisibleAt(position);
            Done(ok);
            return ok;
        }

        /// <summary>関連の <paramref name="position"/> 番目を再生する。</summary>
        public bool PlayRelated(int position)
        {
            bool ok = false;
            if (Ui != null)
            {
                int catalogIndex = Ui.GetRelatedIndexAt(position);
                ok = Session != null && Session.PlayAt(catalogIndex);
            }
            Done(ok);
            return ok;
        }

        /// <summary>Queue の <paramref name="position"/> 番目へ飛ぶ。</summary>
        public bool JumpInQueue(int position)
        {
            bool ok = Session != null && Session.JumpTo(position);
            Done(ok);
            return ok;
        }

        /// <summary>Queue の <paramref name="position"/> 番目を外す。</summary>
        public bool RemoveFromQueue(int position)
        {
            bool ok = Session != null && Session.RemoveFromQueue(position);
            Done(ok);
            return ok;
        }

        // ───────── 内部 ─────────

        private void Done(bool result)
        {
            LastResult = result;
            if (Ui != null) Ui.Refresh();
        }
    }
}
