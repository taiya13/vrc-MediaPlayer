using UdonSharp;
using UnityEngine;
using SmartMediaPlatform.Catalog.Udon;
using SmartMediaPlatform.Recommendation.Udon;

namespace SmartMediaPlatform.World.Udon
{
    /// <summary>
    /// <b>Prefab の根っこ。</b>Phase5-1 の <c>SmartMediaPlayerRoot</c> の Udon 版。
    ///
    /// <b>ロジックを 1 つも持っていません。</b>やることは 2 つだけです:
    /// <list type="number">
    /// <item>部品どうしの参照をつなぐ(<see cref="Wire"/>)</item>
    /// <item>設定どおりに最初の 1 本を鳴らす(<see cref="PlayFirst"/>)</item>
    /// </list>
    /// <c>Next</c> / <c>Play</c> / <c>Stop</c> は<b>生やしていません</b> —
    /// 再生の判断は今までどおり <see cref="UdonPlayerSession"/> の仕事だからです。
    ///
    /// <b>C# 版との関係</b><br/>
    /// <c>SmartMediaPlayerRoot</c> は子からインターフェースで部品を探して
    /// <c>PlaybackFlow</c> を組み立てていましたが、Udon には interface がありません。
    /// そこで<b>参照を Inspector で持つ</b>形にしました。
    /// 組み立て先が変わっただけで、<b>誰が何を知ってよいかの線は同じ</b>です:
    /// <list type="bullet">
    /// <item>URL を知るのは <see cref="UdonVideoBackend"/> だけ</item>
    /// <item>UI は <see cref="UdonCatalogStore"/> 越しにしかデータを見ない</item>
    /// <item>操作は <see cref="UdonMediaController"/> を通る</item>
    /// </list>
    ///
    /// <b>差し替え</b>:Screen / Controller / 動画プレイヤー / Catalog は
    /// すべてこの Inspector の欄を差し替えるだけで替わります。
    ///
    /// <b>操作 UI(パネル)の欄はここにありません。</b>Phase5-3 から
    /// パネルは自分で <see cref="UdonMediaController"/> に名乗り出るので、
    /// 根っこは<b>何枚あるか・どこにあるかを知りません</b>。
    /// おかげでパネルは
    /// <list type="bullet">
    /// <item>この Prefab の外(ワールドの好きな場所)に置ける</item>
    /// <item>何枚でも置ける</item>
    /// <item>後から足しても、ここは 1 行も変わらない</item>
    /// </list>
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonSmartMediaPlayer : UdonSharpBehaviour
    {
        [Header("よく触る設定")]
        [Tooltip("ワールドに入ったら自動で 1 本目を鳴らす")]
        public bool PlayOnStart = true;

        [Tooltip("鳴らし始めるまでの待ち時間(秒)。動画プレイヤーの準備を待つ")]
        [Range(0f, 10f)]
        public float StartDelay = 1.0f;

        [Header("データ(Prefab が配線済み)")]
        [Tooltip("焼き込み済みカタログ。URL はここに入れる。"
                 + "Catalog Builder が作り直してもこの欄は変わらない")]
        public UdonMediaCatalog Catalog;

        [Tooltip("表示用データの窓口")]
        public UdonCatalogStore Store;

        [Tooltip("次の候補を出すエンジン")]
        public UdonRecommendationEngine Recommendation;

        [Header("部品(差し替え可能・Prefab が配線済み)")]
        [Tooltip("再生の判断")]
        public UdonPlayerSession Session;

        [Tooltip("実際に鳴らす。動画プレイヤーと同じ GameObject に置くこと")]
        public UdonVideoBackend Backend;

        [Tooltip("映像と音の出力先")]
        public UdonMediaScreen Screen;

        [Tooltip("操作の窓口。操作 UI(パネル)はここを通して名乗り出る")]
        public UdonMediaController Controller;

        [Tooltip("同期の担当(Phase5-4)。空なら 1 人用として動く")]
        public UdonSyncCoordinator Sync;

        [Tooltip("曲と曲を繋ぐ担当(Phase7-5)。空なら今までどおり即切り替え")]
        public UdonCrossfadeCoordinator Crossfade;

        [Header("困ったとき")]
        [Tooltip("配線の結果を Console に出す。「パネル N 枚」が 0 なら Core の挿し忘れ")]
        public bool LogWiring = true;

        /// <summary>配線が済んでいるか。</summary>
        public bool IsWired;

        void Start()
        {
            Wire();

            if (PlayOnStart)
            {
                if (StartDelay > 0f) SendCustomEventDelayedSeconds("PlayFirst", StartDelay);
                else PlayFirst();
            }
        }

        /// <summary>
        /// 部品どうしの参照をつなぐ。
        /// Prefab は最初からつながっていますが、部品を差し替えたときのために
        /// <b>ここでもう一度つなぎ直します</b>(Inspector の付け忘れを減らすため)。
        /// </summary>
        public void Wire()
        {
            if (Store != null && Store.Catalog == null) Store.Catalog = Catalog;

            if (Recommendation != null && Recommendation.Catalog == null)
                Recommendation.Catalog = Catalog;

            if (Backend != null)
            {
                if (Backend.Catalog == null) Backend.Catalog = Catalog;
                if (Backend.Screen == null) Backend.Screen = Screen;
                if (Backend.Session == null) Backend.Session = Session;
            }

            if (Session != null)
            {
                if (Session.Store == null) Session.Store = Store;
                if (Session.Recommendation == null) Session.Recommendation = Recommendation;
                if (Session.Backend == null) Session.Backend = Backend;
                if (Session.Crossfade == null) Session.Crossfade = Crossfade;
            }

            if (Crossfade != null)
            {
                if (Crossfade.Session == null) Crossfade.Session = Session;
                if (Crossfade.Screen == null) Crossfade.Screen = Screen;
                if (Crossfade.BackendA == null) Crossfade.BackendA = Backend;
            }

            if (Controller != null)
            {
                if (Controller.Session == null) Controller.Session = Session;
                if (Controller.Sync == null) Controller.Sync = Sync;
            }

            if (Sync != null)
            {
                if (Sync.Session == null) Sync.Session = Session;
                if (Sync.Backend == null) Sync.Backend = Backend;
                if (Sync.Controller == null) Sync.Controller = Controller;
            }

            IsWired = Session != null && Backend != null && Store != null && Catalog != null;

            if (LogWiring) Debug.Log("[UdonSmartMediaPlayer] " + Describe(), gameObject);
        }

        /// <summary>
        /// 1 本目を鳴らす。何を鳴らすかは決めません — <b>一覧の先頭を再生するだけ</b>です。
        ///
        /// <b>再生予定(Queue)には何も積みません</b>(Phase7-3)。
        /// 以前はここで補充させていたので、ワールドに入った時点で
        /// 「誰も入れていない曲が再生予定に並んでいる」状態から始まっていました。
        /// </summary>
        public void PlayFirst()
        {
            if (Session == null) return;

            // 同期しているときに全員が 1 本目を鳴らすと、人によって違うものが始まる。
            // 決めるのは持ち主だけで、残りは同期で追いつく。
            if (Sync != null && Sync.Enabled && !Sync.IsOwner())
            {
                if (Controller != null) Controller.NotifyChanged();
                return;
            }

            Session.EnsureInitialized();

            // まだ何も鳴っていなければ、一覧の先頭から始める。
            if (Session.CurrentIndex < 0 && Store != null && Store.Count > 0)
            {
                Session.PlayAt(Store.GetIndexAt(0));
            }
            else
            {
                Session.Play();
            }

            // 置いてあるパネル全部に書き直してもらう(何枚あるかは窓口だけが知っている)。
            if (Controller != null) Controller.NotifyChanged();

            // パネルは自分の Start で名乗り出るので、枚数が確定するのはここ
            // (Wire() の時点ではまだ 0 枚のことがある)。
            if (LogWiring) Debug.Log("[UdonSmartMediaPlayer] " + Describe(), gameObject);
        }

        /// <summary>Console 表示用の 1 行。</summary>
        public string Describe()
        {
            int items = Catalog != null ? Catalog.Count : 0;
            int visible = Store != null ? Store.Count : 0;

            string backend = Backend != null ? Backend.Describe() : "バックエンド未設定";
            string screen = Screen != null && Screen.IsReady ? "画面あり" : "画面なし";

            // パネルは自分から名乗り出るので、枚数は窓口に聞く。
            string panels = Controller != null
                ? "パネル " + Controller.ListenerCount + " 枚"
                : "窓口未設定";

            string sync = Sync == null ? "同期なし"
                : (Sync.Enabled ? "同期あり" : "同期オフ");

            string fade = Crossfade == null ? "混ぜない" : Crossfade.Describe();

            return "カタログ " + items + " 件 / 一覧 " + visible + " 件 / "
                   + backend + " / " + screen + " / " + panels + " / " + sync
                   + " / " + fade;
        }
    }
}
