using UdonSharp;
using UnityEngine;

namespace SmartMediaPlatform.World.Udon
{
    /// <summary>
    /// <b>曲と曲をなめらかに繋ぐ担当。</b>Phase7-5。
    ///
    /// ───────────────────────────────────────────────
    /// <b>なぜこのクラスが要るのか</b>
    ///
    /// クロスフェードには<b>動画プレイヤーが 2 つ</b>要ります
    /// (前の曲を鳴らしながら、次の曲を鳴らし始めるため)。
    /// ところが「次に何を流すか」を決めるのは <see cref="UdonPlayerSession"/> で、
    /// 「どこに映すか」は <see cref="UdonMediaScreen"/> です。
    /// <b>混ぜ方だけをここへ集めます。</b>
    /// おかげで Session も Queue も、クロスフェードのことを 1 行も知りません。
    ///
    /// ───────────────────────────────────────────────
    /// <b>画面は 1 枚のままです</b>
    ///
    /// AVPro の <c>VRCAVProVideoScreen</c> は「どの Renderer の、どのテクスチャ欄へ
    /// 書くか」を指定できます。そこで
    /// <list type="bullet">
    /// <item>プレイヤー A → 画面の <c>_MainTex</c></item>
    /// <item>プレイヤー B → 画面の <c>_SecondTex</c></item>
    /// </list>
    /// と<b>同じ 1 枚の Renderer</b> に別々の欄で書かせ、
    /// <c>SmartMediaPlatform/Crossfade</c> シェーダーの <c>_Blend</c> で混ぜます。
    /// 板も描画も 1 枚のままなので、見た目も負荷もほとんど変わりません。
    ///
    /// ───────────────────────────────────────────────
    /// <b>手で選んだときは混ぜません</b>
    ///
    /// 曲を選ぶのは「いますぐこれが聴きたい」という操作です。
    /// そこで 10 秒かけて混ぜると<b>反応が鈍い</b>としか感じられません。
    /// 混ぜるのは<b>ひとりでに次へ移るとき</b>(再生予定・おすすめ)だけです。
    ///
    /// ───────────────────────────────────────────────
    /// <b>使えないときは黙って今までどおりに戻ります</b>
    ///
    /// プレイヤーが 1 つしか無い / シェーダーが見つからない /
    /// 曲の長さが分からない(生配信)/ 曲が短すぎる —— どれでも
    /// <see cref="Enabled"/> は落ちず、<b>その曲だけ</b>今までの
    /// 「終わってから次へ」に任せます。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonCrossfadeCoordinator : UdonSharpBehaviour
    {
        [Tooltip("再生の判断をする相手")]
        public UdonPlayerSession Session;

        [Tooltip("映像と音の出力先(人が決めた音量をここから借りる)")]
        public UdonMediaScreen Screen;

        [Header("2 系統の動画プレイヤー")]
        [Tooltip("A 系統。画面の _MainTex に書く")]
        public UdonVideoBackend BackendA;

        [Tooltip("B 系統。画面の _SecondTex に書く。"
                 + "空ならクロスフェードは使わず、今までどおり A だけで動きます")]
        public UdonVideoBackend BackendB;

        [Header("設定")]
        [Tooltip("混ぜる長さ(秒)。0 にすると今までどおりの即切り替えになります")]
        [Range(0f, 20f)]
        public float FadeSeconds = 10f;

        [Tooltip("この長さより短い曲では混ぜない(秒)。"
                 + "短い曲に長いフェードを掛けると、鳴っている時間の大半が混ざった状態になります")]
        public float MinimumTrackSeconds = 30f;

        [Tooltip("混ざり具合を書き込むシェーダーの欄")]
        public string BlendProperty = "_Blend";

        [Tooltip("様子を Console に出す")]
        public bool LogFade;

        // ───────── 状態 ─────────
        //
        // CrossfadeModel(正典・EditMode テスト済み)の写しです。
        // 数え方を変えるときは必ず両方を直してください。

        private const int StateIdle = 0;
        private const int StatePreparing = 1;
        private const int StateFading = 2;

        private int _state = StateIdle;
        private float _elapsed;

        /// <summary>いま表に出ている系統。false なら A、true なら B。</summary>
        private bool _bIsFront;

        /// <summary>裏で読み込ませた次の曲。無ければ -1。</summary>
        private int _pendingIndex = -1;

        private Material _screenMaterial;
        private bool _materialChecked;

        private bool _initialized;

        void Start()
        {
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            // 始まりは A が表。B は黙らせておく。
            ApplyFadeVolumes(1f, 0f);
            ApplyBlend(0f);
        }

        // ───────── 表と裏 ─────────

        /// <summary>いま鳴っている(表の)系統。</summary>
        public UdonVideoBackend Front
        {
            get { return _bIsFront ? BackendB : BackendA; }
        }

        /// <summary>次を仕込む(裏の)系統。</summary>
        public UdonVideoBackend Back
        {
            get { return _bIsFront ? BackendA : BackendB; }
        }

        /// <summary>クロスフェードが使える構成か。</summary>
        public bool CanCrossfade
        {
            get
            {
                if (FadeSeconds <= 0f) return false;
                if (BackendA == null || BackendB == null) return false;
                return ResolveMaterial() != null;
            }
        }

        /// <summary>混ざっている最中か(診断・表示用)。</summary>
        public bool IsFading { get { return _state == StateFading; } }

        /// <summary>
        /// <b>次の曲へ移る作業の途中か</b>(裏で読ませた〜混ざり終わるまで)。
        ///
        /// <see cref="UdonPlayerSession.NotifyEnded"/> がこれを見ます。
        /// 混ぜている最中は<b>古いほうの「終わりました」が飛んでくる</b>ので、
        /// そのまま次へ進ませると<b>1 曲飛ばしてしまいます</b>。
        /// </summary>
        public bool IsBusy { get { return _state != StateIdle; } }

        /// <summary>
        /// <b>いますぐ混ぜ終わったことにする。</b>
        ///
        /// 混ざり切る前に古い曲が終わってしまったときに呼びます
        /// (フェード時間より曲の残りが短かった・読み込みに手間取ったなど)。
        /// 途中で切れるより、<b>そこで入れ替えてしまうほうが自然</b>です。
        /// </summary>
        /// <returns>入れ替えたら true。まだ裏が鳴っていないなら false。</returns>
        public bool FinishNow()
        {
            if (_state == StateIdle) return false;

            UdonVideoBackend back = Back;

            // 裏がまだ鳴っていないなら、混ぜようがない。やめて呼び出し側に任せる。
            if (back == null || _pendingIndex < 0 || !back.IsPlaying)
            {
                CancelFade();
                return false;
            }

            Swap();
            return true;
        }

        // ───────── 上位からの指示 ─────────

        /// <summary>
        /// <b>いますぐこれを流す(手で選んだとき)。</b>混ぜません。
        /// 表の系統に読ませ、裏は黙らせます。
        /// </summary>
        public bool PlayImmediate(int catalogIndex)
        {
            EnsureInitialized();

            CancelFade();

            UdonVideoBackend front = Front;
            if (front == null) return false;

            // 裏で何か鳴っていたら止める(混ぜている最中に選び直された場合)。
            UdonVideoBackend back = Back;
            if (back != null) back.Stop();

            ApplyFadeVolumes(1f, 0f);
            ApplyBlend(0f);

            return front.LoadAndPlay(catalogIndex);
        }

        /// <summary>
        /// <b>再生予定が変わった。</b>Phase7-5。
        ///
        /// おすすめを裏で読み始めたあとに人が曲を予定へ入れたら、
        /// <b>予定のほうを優先し直します</b>。
        /// 入れたのに次に流れないのでは、入れた意味がありません。
        ///
        /// <b>もう混ざり始めていたら、そのまま最後まで混ぜます。</b>
        /// 音が半分まで入れ替わったところで別の曲に差し替えると、
        /// <b>ぶつ切りに聞こえて、かえって壊れたように感じます</b>。
        /// 入れた曲はその次に流れます。
        /// </summary>
        public void NotifyQueueChanged()
        {
            if (_state != StatePreparing) return;
            if (Session == null) return;

            int wanted = Session.PeekNextIndex();
            if (wanted == _pendingIndex) return;   // 変わっていない

            // 裏で読ませたものが「次」でなくなった。読み直しからやり直す。
            UdonVideoBackend back = Back;
            if (back != null) back.Stop();

            CancelFade();

            if (LogFade)
            {
                Debug.Log("[UdonCrossfadeCoordinator] 再生予定が変わったので仕込み直します。",
                          gameObject);
            }
        }

        /// <summary>
        /// 混ぜている途中でやめる(手で操作された・止められた)。
        /// 表の系統だけが鳴っている状態に戻します。
        /// </summary>
        public void CancelFade()
        {
            if (_state == StateIdle && _pendingIndex < 0) return;

            _state = StateIdle;
            _elapsed = 0f;
            _pendingIndex = -1;

            ApplyFadeVolumes(1f, 0f);
            ApplyBlend(0f);

            if (LogFade) Debug.Log("[UdonCrossfadeCoordinator] 混ぜるのをやめました。", gameObject);
        }

        // ───────── 毎フレーム ─────────

        void Update()
        {
            if (!_initialized) return;
            if (Session == null) return;

            if (_state == StateFading)
            {
                AdvanceFade();
                return;
            }

            if (_state == StatePreparing)
            {
                WaitForBackToStart();
                return;
            }

            WatchForEndOfTrack();
        }

        /// <summary>
        /// 終わりが近づいたら、次の曲を裏で読み始める。
        /// <c>CrossfadeModel.ShouldPrepare</c> の写しです。
        /// </summary>
        private void WatchForEndOfTrack()
        {
            if (!CanCrossfade) return;
            if (!Session.IsPlaying) return;

            UdonVideoBackend front = Front;
            if (front == null || !front.IsPlaying) return;

            float duration = front.GetDuration();

            // 長さが分からない(生配信・読み込み中)なら掛けない。
            // ここで掛けると、まだ半分残っているのに次へ移ってしまう。
            if (duration <= 0f) return;
            if (duration < MinimumTrackSeconds) return;

            float remaining = duration - front.GetTime();
            if (remaining <= 0f) return;
            if (remaining > FadeSeconds) return;

            BeginPrepare();
        }

        /// <summary>次に流すものを覗いて、裏へ読ませる。</summary>
        private void BeginPrepare()
        {
            int next = Session.PeekNextIndex();
            if (next < 0) return;      // 次が無い。今までどおり終わりの合図に任せる。

            UdonVideoBackend back = Back;
            if (back == null) return;

            if (!back.LoadAndPlay(next)) return;

            _pendingIndex = next;
            _state = StatePreparing;
            _elapsed = 0f;

            // 裏はまだ黙らせておく。鳴り出してから混ぜ始める。
            ApplyFadeVolumes(1f, 0f);

            if (LogFade)
            {
                Debug.Log("[UdonCrossfadeCoordinator] 次の曲を裏で読み始めました: index "
                          + next, gameObject);
            }
        }

        /// <summary>
        /// 裏が鳴り出すのを待つ。
        /// <b>読み込みに掛かる時間はまちまち</b>なので、決め打ちで待たずに
        /// 「鳴り出した」ことを見てから混ぜ始めます。
        /// </summary>
        private void WaitForBackToStart()
        {
            UdonVideoBackend back = Back;

            if (back == null || _pendingIndex < 0)
            {
                CancelFade();
                return;
            }

            // 裏がまだ鳴っていないなら待つ。
            if (!back.IsPlaying) return;

            _state = StateFading;
            _elapsed = 0f;

            if (LogFade) Debug.Log("[UdonCrossfadeCoordinator] 混ぜ始めます。", gameObject);
        }

        /// <summary>混ざり具合を進める。混ざり終わったら役割を入れ替える。</summary>
        private void AdvanceFade()
        {
            _elapsed += Time.deltaTime;

            float progress = FadeSeconds > 0f ? _elapsed / FadeSeconds : 1f;
            if (progress < 0f) progress = 0f;
            if (progress > 1f) progress = 1f;

            // 音は等パワー(真ん中で痩せないように)、絵はそのまま。
            // CrossfadeModel と同じ式。
            float outgoing = Mathf.Cos(progress * Mathf.PI * 0.5f);
            float incoming = Mathf.Cos((1f - progress) * Mathf.PI * 0.5f);

            ApplyFadeVolumes(outgoing, incoming);
            ApplyBlend(progress);

            if (_elapsed < FadeSeconds) return;

            Swap();
        }

        /// <summary>混ざり終わった。裏を表にして、古いほうを止める。</summary>
        private void Swap()
        {
            UdonVideoBackend oldFront = Front;
            int moved = _pendingIndex;

            _bIsFront = !_bIsFront;
            _state = StateIdle;
            _elapsed = 0f;
            _pendingIndex = -1;

            // 表が入れ替わったので、混ざり具合も裏返す。
            ApplyFadeVolumes(1f, 0f);
            ApplyBlend(_bIsFront ? 1f : 0f);

            if (oldFront != null) oldFront.Stop();

            // ここで初めて Session の状態を進める。
            // 「覗いただけ」を「実際に移った」に変えるのはこの 1 行です。
            if (moved >= 0) Session.CommitAdvanceTo(moved);

            if (LogFade)
            {
                Debug.Log("[UdonCrossfadeCoordinator] 入れ替えました: index " + moved, gameObject);
            }
        }

        // ───────── 出力 ─────────

        /// <summary>表と裏の音量を当てる(混ざり具合から来る倍率)。</summary>
        private void ApplyFadeVolumes(float frontFade, float backFade)
        {
            UdonVideoBackend front = Front;
            UdonVideoBackend back = Back;

            if (front != null) front.SetFadeVolume(frontFade);
            if (back != null) back.SetFadeVolume(backFade);
        }

        /// <summary>
        /// 絵の混ざり具合をシェーダーへ書く。
        ///
        /// <b>A が表のときは 0 → 1、B が表のときは 1 → 0</b> と向きが逆になります。
        /// シェーダーの <c>_Blend</c> は「_SecondTex(= B)がどれだけ出ているか」だからです。
        /// </summary>
        private void ApplyBlend(float progress)
        {
            Material material = ResolveMaterial();
            if (material == null) return;

            // 表が A なら、進むほど B が出てくる(0 → 1)。
            // 表が B なら、進むほど A が出てくる(1 → 0)。
            float blend = _bIsFront ? 1f - progress : progress;

            material.SetFloat(BlendProperty, Mathf.Clamp01(blend));
        }

        /// <summary>
        /// 画面のマテリアル。<b>1 回だけ探して覚えます</b>
        /// (毎フレーム <c>GetComponent</c> を叩かないため)。
        /// </summary>
        private Material ResolveMaterial()
        {
            if (_materialChecked) return _screenMaterial;
            _materialChecked = true;

            if (Screen == null || Screen.Surface == null) return null;

            // sharedMaterial だと、同じ材質を使う他の板まで一緒に変わります。
            // ワールドに複数枚置かれることを考えて、こちらは触りません。
            _screenMaterial = Screen.Surface.material;
            return _screenMaterial;
        }

        /// <summary>Console 表示用の 1 行(診断)。</summary>
        public string Describe()
        {
            if (BackendA == null) return "A 系統がありません";
            if (BackendB == null) return "B 系統がありません(クロスフェードは使いません)";

            if (ResolveMaterial() == null) return "画面の材質が見つかりません";

            return "A / B の 2 系統 / " + FadeSeconds + " 秒で混ぜます"
                   + (IsFading ? "(いま混ざっています)" : "");
        }
    }
}
