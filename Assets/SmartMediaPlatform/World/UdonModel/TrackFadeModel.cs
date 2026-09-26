namespace SmartMediaPlatform.World.UdonModel
{
    /// <summary>
    /// <b>曲の終わりで音を下げ、次の曲を 0 から上げる規則。</b>Phase8-3。
    /// <c>UdonTrackFader</c> が 1:1 で写しています。<b>変えるときは両方を直してください。</b>
    ///
    /// ───────────────────────────────────────────────
    /// <b>重ねません</b>
    ///
    /// Phase7-5 のクロスフェードは、2 曲を<b>同時に</b>鳴らして混ぜていました。
    /// 動画プレイヤーが 2 つ要り、音の出口も 2 つになり、
    /// そこから崩れました。ここでは同時に鳴る瞬間を作りません。
    /// <list type="number">
    /// <item>曲の残りが少なくなったら、<b>終わりに向かって音量を 0 まで下げる</b></item>
    /// <item>次の曲は<b>音量 0 から始めて上げる</b></item>
    /// </list>
    /// 動画プレイヤーは 1 つのまま、変えるのは音量の倍率だけです。
    ///
    /// ───────────────────────────────────────────────
    /// <b>下げるほうは「残り時間」だけで決めます</b>
    ///
    /// 経過時間を足し込んで数えると、途中で一時停止やシークがあったときに
    /// <b>数えた値と実際の位置がずれます</b>。残り時間から毎回計算し直せば、
    /// バーを少し戻しただけで音量も自然に戻ります。
    ///
    /// ───────────────────────────────────────────────
    /// <b>上げるほうは「曲が変わる直前に下がっていたか」で決めます</b>
    ///
    /// 手で別の曲を選んだときは、フェードインしません。
    /// 押した瞬間に全開で鳴るほうが「効いた」と分かるからです(Phase7-5 と同じ考え)。
    /// 見分け方は、<b>曲が切り替わった瞬間の音量</b>です。
    /// 終わりに向かって下がっていた = ひとりでに終わった、なので次はフェードイン。
    /// 1 のままだった = 途中で手で変えた、なので次は頭から全開。
    ///
    /// 上げる速さは<b>実際の時間</b>で数えます。曲の再生位置で数えると、
    /// 生配信のように位置が進まないものでは<b>ずっと無音のまま</b>になります。
    /// </summary>
    public sealed class TrackFadeModel
    {
        /// <summary>終わりに向かって下げる長さ(秒)。0 なら下げない。</summary>
        public float FadeOutSeconds = 3f;

        /// <summary>次の曲を 0 から上げる長さ(秒)。0 なら上げない(すぐ全開)。</summary>
        public float FadeInSeconds = 2f;

        /// <summary>
        /// <b>本当の終わりより、これだけ手前で 0 になる</b>(秒)。
        ///
        /// 曲の終わりは再生位置を見張って見つけています(<see cref="EndWatchModel"/>)。
        /// 見に行く間隔(0.4 秒)と、終わりとみなす幅(0.4 秒)のぶんだけ、
        /// 気付くのが<b>本当の終わりより早い</b>ことがあります。
        /// その時点でもう 0 になっていないと、少し音が残ったまま止まります。
        /// </summary>
        public float SilentBeforeEnd = 0.8f;

        /// <summary>この長さより短い曲では下げない(秒)。短い曲の大半が小さく聞こえるのを避ける。</summary>
        public float MinimumTrackSeconds = 20f;

        /// <summary>
        /// これより長い「長さ」は信じない(秒)。
        /// 生配信では、極端に大きな値が返ることがあります。
        /// </summary>
        public float MaximumTrackSeconds = 86400f;

        private bool _seen;
        private int _lastLoadCount;
        private bool _fadeInArmed;
        private bool _fadeInRunning;
        private float _fadeInStartedAt;
        private float _level = 1f;

        /// <summary>いまの倍率(0〜1)。最後に <see cref="Tick"/> が返した値。</summary>
        public float Level { get { return _level; } }

        /// <summary>次の曲をフェードインで始めるつもりか(診断用)。</summary>
        public bool FadeInArmed { get { return _fadeInArmed; } }

        // ───────── 計算だけ(状態を持たない)─────────

        /// <summary>
        /// <b>終わりに向かって下げる倍率。</b>残り時間だけで決めます。
        /// 長さが分からないもの・短すぎる曲・下げない設定では、いつも 1。
        /// </summary>
        public static float FadeOutLevel(
            float time, float duration, float fadeOutSeconds,
            float silentBeforeEnd, float minimumTrackSeconds, float maximumTrackSeconds)
        {
            if (fadeOutSeconds <= 0f) return 1f;

            // 長さが分からない(生配信・読み込み中)なら、終わりも分からない。
            if (duration <= 1f) return 1f;
            if (duration > maximumTrackSeconds) return 1f;
            if (duration < minimumTrackSeconds) return 1f;

            float untilSilent = (duration - time) - silentBeforeEnd;

            if (untilSilent >= fadeOutSeconds) return 1f;

            // 1 ミリ秒未満は 0 とみなす。float の引き算は、ちょうど 0 になるはずの所で
            // 0.000001 のような端数を残すことがあり、「0 になったか」の判断がぶれます。
            if (untilSilent <= 0.001f) return 0f;

            return untilSilent / fadeOutSeconds;
        }

        /// <summary><b>0 から上げる倍率。</b>上げ始めてからの実時間で決めます。</summary>
        public static float FadeInLevel(float elapsedSeconds, float fadeInSeconds)
        {
            if (fadeInSeconds <= 0f) return 1f;
            if (elapsedSeconds <= 0f) return 0f;
            if (elapsedSeconds >= fadeInSeconds) return 1f;

            return elapsedSeconds / fadeInSeconds;
        }

        // ───────── 状態を進める ─────────

        /// <summary>
        /// <b>いまの倍率を出す。</b>毎フレーム(または見回りのたびに)呼びます。
        /// </summary>
        /// <param name="loadCount">
        /// 動画プレイヤーが<b>読み込みを始めた回数</b>。変わったら「曲が変わった」とみなす。
        /// </param>
        /// <param name="started">いま読み込んだものが、実際に鳴り始めたか。</param>
        /// <param name="time">再生位置(秒)。</param>
        /// <param name="duration">長さ(秒)。分からなければ 0。</param>
        /// <param name="now">いまの時刻(秒。<c>Time.time</c>)。</param>
        public float Tick(int loadCount, bool started, float time, float duration, float now)
        {
            // ── 曲が変わった(新しい読み込みが始まった)。
            if (!_seen || loadCount != _lastLoadCount)
            {
                // 最初の 1 回は「変わった」ではない。ワールドに入った時点のもの。
                bool changed = _seen;

                _seen = true;
                _lastLoadCount = loadCount;
                _fadeInRunning = false;

                // 切り替わった瞬間に下がっていた = 終わりに向かって下げていた。
                // 手で途中から変えたなら 1 のまま。
                _fadeInArmed = changed && _level < 0.999f && FadeInSeconds > 0f;
            }

            // ── まだ鳴り始めていない(読み込み中・終わったあとで次を待っている)。
            //
            //    <b>ここで 1 に戻してはいけません。</b>終わりを見つけてから次の読み込みが
            //    始まるまでに間があり、その間に 1 へ戻すと、次の曲が変わった瞬間に
            //    「下がっていなかった = 手で変えた」と取り違え、フェードインしなくなります。
            //    動画は止まっているので、倍率を据え置いても音は出ません。
            if (!started)
            {
                if (_fadeInArmed) _level = 0f;
                return _level;
            }

            float level = FadeOutLevel(
                time, duration, FadeOutSeconds, SilentBeforeEnd,
                MinimumTrackSeconds, MaximumTrackSeconds);

            if (_fadeInArmed)
            {
                if (!_fadeInRunning)
                {
                    _fadeInRunning = true;
                    _fadeInStartedAt = now;
                }

                float fadeIn = FadeInLevel(now - _fadeInStartedAt, FadeInSeconds);

                // 上がりきったら、もう掛けない。
                // 残しておくと、あとで頭へシークしたときにまた下がってしまう。
                if (fadeIn >= 1f)
                {
                    _fadeInArmed = false;
                    _fadeInRunning = false;
                }

                if (fadeIn < level) level = fadeIn;
            }

            _level = Clamp01(level);
            return _level;
        }

        /// <summary>
        /// <b>何も掛けていない状態に戻す。</b>止めた・無効にしたとき。
        /// 次に曲が変わっても、フェードインはしません。
        /// </summary>
        public void Reset()
        {
            _fadeInArmed = false;
            _fadeInRunning = false;
            _level = 1f;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
