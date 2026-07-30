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
    /// <b>すべて引数なしの public メソッド</b>を基本にしてあります。
    /// これは Udon の都合ではなく、そうしておくと
    /// <list type="bullet">
    /// <item>uGUI の <c>Button.onClick</c> にそのまま繋がる</item>
    /// <item><c>SendCustomEvent("Next")</c> で名前だけで呼べる</item>
    /// </list>
    /// ので、<b>UI を差し替えても操作の意味が変わらない</b>ためです。
    ///
    /// <b>Phase5-3 で変わったところ</b><br/>
    /// Phase5-2 はここが <c>UdonMediaPlayerUI</c> を<b>1 枚だけ名指しで</b>持っていました。
    /// つまり<b>操作 UI はワールドに 1 枚しか置けません</b>でした。
    /// いまは「書き直しの知らせを受け取りたい人」の一覧(<see cref="AddListener"/>)になっています。
    /// <list type="bullet">
    /// <item>壁パネルと手持ちリモコンを<b>同時に</b>置ける</item>
    /// <item>受け取る側の型を知らないので、<b>UI 以外も登録できる</b>
    ///       (Phase5-4 の同期はここに乗ります)</item>
    /// </list>
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonMediaController : UdonSharpBehaviour
    {
        /// <summary>知らせを受け取る側に送るイベント名。</summary>
        public const string RefreshEvent = "Refresh";

        /// <summary>登録できる上限(Udon は固定長配列なので上限が要る)。</summary>
        public const int ListenerCapacity = 16;

        [Tooltip("操作を伝える相手")]
        public UdonPlayerSession Session;

        /// <summary>直近の操作が通ったか(診断用)。</summary>
        public bool LastResult;

        // 「状態が変わったら知らせてほしい」人たち。
        // UdonMediaPanel を名指しで持たないのは、UI 以外も登録できるようにするため
        // (Phase5-4 の同期、ログ、2 枚目の画面 …… すべて同じ口で足せる)。
        private UdonSharpBehaviour[] _listeners;
        private int _listenerCount;

        /// <summary>組み立て済みか。</summary>
        public bool IsReady
        {
            get { return Session != null; }
        }

        /// <summary>いま登録されている数(診断用)。</summary>
        public int ListenerCount { get { return _listenerCount; } }

        // ───────── 知らせを受け取る側の登録 ─────────

        /// <summary>
        /// 状態が変わったら <c>Refresh</c> を送ってほしい相手を登録する。
        /// 呼ぶのは相手の <c>Start</c> からで、<b>こちらは相手の型を知りません</b>。
        /// </summary>
        public bool AddListener(UdonSharpBehaviour listener)
        {
            EnsureInitialized();

            if (listener == null) return false;
            if (_listenerCount >= ListenerCapacity) return false;

            for (int i = 0; i < _listenerCount; i++)
            {
                if (_listeners[i] == listener) return false;
            }

            _listeners[_listenerCount] = listener;
            _listenerCount++;
            return true;
        }

        public bool RemoveListener(UdonSharpBehaviour listener)
        {
            EnsureInitialized();
            if (listener == null) return false;

            for (int i = 0; i < _listenerCount; i++)
            {
                if (_listeners[i] != listener) continue;

                for (int j = i + 1; j < _listenerCount; j++) _listeners[j - 1] = _listeners[j];
                _listenerCount--;
                return true;
            }
            return false;
        }

        /// <summary>登録した全員に「書き直して」と伝える。</summary>
        public void NotifyChanged()
        {
            EnsureInitialized();

            for (int i = 0; i < _listenerCount; i++)
            {
                if (_listeners[i] == null) continue;
                _listeners[i].SendCustomEvent(RefreshEvent);
            }
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

        // ───────── 一覧から(選択を経由する形)─────────

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

        // ───────── 何を、を指す操作 ─────────
        // ボタンからは引数を渡せないので、UI 側(UdonMediaListView)がここを呼びます。
        //
        // catalog index を受ける口を用意してあるのは、
        // 「画面に出ていたもの」と「実際に再生するもの」を必ず一致させるためです。
        // Phase5-2 は関連の位置番号だけを渡し、対応表は画面が持っていました
        // (= 窓口が特定の画面 1 枚に依存していた)。

        /// <summary>catalog index を今すぐ再生する。</summary>
        public bool PlayCatalogIndex(int catalogIndex)
        {
            bool ok = Session != null && Session.PlayAt(catalogIndex);
            Done(ok);
            return ok;
        }

        /// <summary>catalog index を Queue の末尾へ積む。</summary>
        public bool EnqueueCatalogIndex(int catalogIndex)
        {
            bool ok = Session != null && Session.Enqueue(catalogIndex);
            Done(ok);
            return ok;
        }

        /// <summary>catalog index を「次に再生」にする(再生はしない)。</summary>
        public bool PlayNextCatalogIndex(int catalogIndex)
        {
            bool ok = Session != null && Session.PlayNext(catalogIndex);
            Done(ok);
            return ok;
        }

        /// <summary>catalog index を選ぶ。</summary>
        public bool SelectCatalogIndex(int catalogIndex)
        {
            bool ok = Session != null && Session.Select(catalogIndex);
            Done(ok);
            return ok;
        }

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

        private void EnsureInitialized()
        {
            if (_listeners != null) return;
            _listeners = new UdonSharpBehaviour[ListenerCapacity];
        }

        private void Done(bool result)
        {
            LastResult = result;
            NotifyChanged();
        }
    }
}
