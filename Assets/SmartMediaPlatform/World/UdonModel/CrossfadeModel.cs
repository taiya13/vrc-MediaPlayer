namespace SmartMediaPlatform.World.UdonModel
{
    /// <summary>
    /// <b>クロスフェードの数え方。Udon へ写す前の正典。</b>Phase7-5。
    ///
    /// <b>やることは 3 つだけ</b>です。
    /// <list type="number">
    /// <item>いつ次の曲を裏で読み始めるかを決める(<see cref="ShouldPrepare"/>)</item>
    /// <item>いま何割まで混ざったかを数える(<see cref="Progress"/>)</item>
    /// <item>その割合から、両方の音量と絵の混ざり具合を出す</item>
    /// </list>
    /// <b>動画プレイヤーも Queue も知りません。</b>実際に読み込ませたり
    /// 役割を入れ替えたりするのは <c>UdonCrossfadeCoordinator</c> の仕事です。
    ///
    /// ───────────────────────────────────────────────
    /// <b>音と絵で混ぜ方を変えています。</b>
    ///
    /// <b>音は「等パワー」</b>(<see cref="OutgoingVolume"/> / <see cref="IncomingVolume"/>)。
    /// 単純に 1→0 と 0→1 で足すと、<b>真ん中で音が痩せます</b>。
    /// 別々の音を 0.5 ずつ足しても、感じる大きさは 0.707 ぶんにしかならないためです
    /// (無相関な音は「エネルギー」で足し算されるので、振幅の合計にはならない)。
    /// <c>cos</c> と <c>sin</c> を使うと <c>out² + in² = 1</c> が常に成り立ち、
    /// <b>混ざっている最中も大きさが変わりません</b>。
    ///
    /// <b>絵は「そのまま」</b>(<see cref="VideoBlend"/>)。
    /// 絵は 2 枚を <c>lerp</c> で重ねるだけなので、真ん中で薄くなることはありません。
    /// ここに <c>sin</c> を使うと、逆に<b>切り替わりが急に見えます</b>。
    ///
    /// ───────────────────────────────────────────────
    /// <b>いつ始めるか</b>
    ///
    /// 「残り &lt;= フェード時間」で始めます。10 秒のフェードなら、
    /// 残り 10 秒の時点で次の曲を裏で読み始め、そのまま混ぜていくと
    /// <b>ちょうど曲の終わりで入れ替わりが完了</b>します。
    ///
    /// <b>長さが分からない曲では始めません。</b>生配信や、まだ読み込み中で
    /// 長さが取れていないときに始めると、<b>まだ半分残っているのに
    /// 次の曲へ移ってしまいます</b>。分からないときは今までどおり
    /// 「終わってから次へ」に任せるのが安全です。
    ///
    /// <b>フェード時間より短い曲でも始めません。</b>15 秒の曲に 10 秒の
    /// フェードを掛けると、鳴っている時間の 3 分の 2 が混ざった状態になります。
    /// </summary>
    public sealed class CrossfadeModel
    {
        // ───────── 設定 ─────────

        /// <summary>混ぜる長さ(秒)。既定は 10 秒。</summary>
        public float FadeSeconds = 10f;

        /// <summary>
        /// <b>この長さより短い曲では混ぜない。</b>
        /// 既定はフェード時間の 3 倍(10 秒フェードなら 30 秒未満の曲)。
        /// 短い曲で混ぜると、鳴っている時間の大半が「混ざっている最中」になります。
        /// </summary>
        public float MinimumTrackSeconds = 30f;

        // ───────── 状態 ─────────

        /// <summary>混ざっていない。ふつうに 1 曲だけ鳴っている。</summary>
        public const int StateIdle = 0;

        /// <summary>次の曲を裏で読み込ませた。まだ混ぜてはいない。</summary>
        public const int StatePreparing = 1;

        /// <summary>混ざっている最中。</summary>
        public const int StateFading = 2;

        private int _state = StateIdle;
        private float _elapsed;

        public int State { get { return _state; } }

        /// <summary>混ざっている最中か。</summary>
        public bool IsFading { get { return _state == StateFading; } }

        /// <summary>次の曲を裏で読ませた(まだ混ぜていない)か。</summary>
        public bool IsPreparing { get { return _state == StatePreparing; } }

        /// <summary>何も混ざっていないか。</summary>
        public bool IsIdle { get { return _state == StateIdle; } }

        /// <summary>フェードを始めてから経った時間(秒)。</summary>
        public float Elapsed { get { return _elapsed; } }

        // ───────── いつ始めるか ─────────

        /// <summary>
        /// <b>いま次の曲を裏で読み始めるべきか。</b>
        ///
        /// <paramref name="durationSeconds"/> が 0 以下(長さが分からない)なら false。
        /// 曲が <see cref="MinimumTrackSeconds"/> より短くても false。
        /// </summary>
        /// <param name="elapsedSeconds">いまの曲の再生位置(秒)。</param>
        /// <param name="durationSeconds">いまの曲の長さ(秒)。0 以下なら「分からない」。</param>
        public bool ShouldPrepare(float elapsedSeconds, float durationSeconds)
        {
            if (_state != StateIdle) return false;
            if (FadeSeconds <= 0f) return false;

            // 長さが分からないものには掛けない。
            // 生配信や読み込み中は 0 が返るので、ここで必ず弾く。
            if (durationSeconds <= 0f) return false;

            // 短い曲には掛けない。
            if (durationSeconds < MinimumTrackSeconds) return false;

            float remaining = durationSeconds - elapsedSeconds;

            // 行き過ぎ(もう終わっている)ときは始めない。終わりの合図に任せる。
            if (remaining <= 0f) return false;

            return remaining <= FadeSeconds;
        }

        // ───────── 進める ─────────

        /// <summary>次の曲を裏で読ませたことにする。</summary>
        public void MarkPrepared()
        {
            if (_state != StateIdle) return;

            _state = StatePreparing;
            _elapsed = 0f;
        }

        /// <summary>混ぜ始める(裏の曲が鳴り出したら呼ぶ)。</summary>
        public void BeginFade()
        {
            if (_state == StateFading) return;

            _state = StateFading;
            _elapsed = 0f;
        }

        /// <summary>
        /// 時間を進める。<b>混ざり終わったら true</b>。
        /// 混ざっていないときは何もせず false。
        /// </summary>
        public bool Advance(float deltaSeconds)
        {
            if (_state != StateFading) return false;

            _elapsed += deltaSeconds;
            return _elapsed >= FadeSeconds;
        }

        /// <summary>元に戻す(混ぜ終わった / やめた)。</summary>
        public void Reset()
        {
            _state = StateIdle;
            _elapsed = 0f;
        }

        // ───────── 何割混ざったか ─────────

        /// <summary>0(始まり)〜1(終わり)。混ざっていないときは 0。</summary>
        public float Progress
        {
            get
            {
                if (_state != StateFading) return 0f;
                if (FadeSeconds <= 0f) return 1f;

                float t = _elapsed / FadeSeconds;
                if (t < 0f) return 0f;
                if (t > 1f) return 1f;
                return t;
            }
        }

        /// <summary>
        /// <b>いま鳴っている(消えていく)ほうの音量。</b>1 → 0。
        /// 等パワー(<c>cos</c>)なので、真ん中でも合計の大きさが変わりません。
        /// </summary>
        public float OutgoingVolume
        {
            get { return OutgoingVolumeAt(Progress); }
        }

        /// <summary><b>入ってくるほうの音量。</b>0 → 1。等パワー(<c>sin</c>)。</summary>
        public float IncomingVolume
        {
            get { return IncomingVolumeAt(Progress); }
        }

        /// <summary>
        /// <b>絵の混ざり具合。</b>0(いまの曲だけ)→ 1(次の曲だけ)。
        /// 絵は素直に <c>lerp</c> するので、そのままの割合を使います。
        /// </summary>
        public float VideoBlend
        {
            get { return Progress; }
        }

        // ───────── 割合 → 音量(テストしやすいよう静的にも用意)─────────

        /// <summary>消えていくほうの音量。<c>cos(t · π/2)</c>。</summary>
        public static float OutgoingVolumeAt(float progress01)
        {
            float t = Clamp01(progress01);
            return Cos01(t);
        }

        /// <summary>入ってくるほうの音量。<c>sin(t · π/2)</c>。</summary>
        public static float IncomingVolumeAt(float progress01)
        {
            float t = Clamp01(progress01);
            return Cos01(1f - t);
        }

        /// <summary>
        /// <c>cos(t · π/2)</c>。
        ///
        /// 正典側は <c>UnityEngine</c> を参照しない決まりなので <c>System.Math</c> を使います。
        /// Udon 側は同じ式を <c>Mathf.Cos</c> で書きます(値は同じ)。
        /// </summary>
        private static float Cos01(float t)
        {
            return (float)System.Math.Cos(t * System.Math.PI * 0.5);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
