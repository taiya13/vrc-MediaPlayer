using UdonSharp;
using UnityEngine;

namespace SmartMediaPlatform.World.Udon
{
    /// <summary>
    /// <b>曲の終わりで音を下げ、次の曲を 0 から上げる担当。</b>Phase8-3。
    ///
    /// ───────────────────────────────────────────────
    /// <b>重ねません</b>
    ///
    /// Phase7-5 のクロスフェードは 2 曲を<b>同時に</b>鳴らしていました。
    /// 動画プレイヤーが 2 つ要り、音の出口が 2 つになり、そこから崩れました。
    /// ここは動画プレイヤー 1 つのまま、<b>音量の倍率だけ</b>を動かします。
    /// <list type="number">
    /// <item>曲の残りが少なくなったら、終わりに向かって 0 まで下げる</item>
    /// <item>次の曲は 0 から上げる(手で選んだ曲はすぐ全開)</item>
    /// </list>
    ///
    /// ───────────────────────────────────────────────
    /// <b>判断は 1 つも持っていません</b>
    ///
    /// 次に何を流すかは今までどおり <see cref="UdonPlayerSession"/> が決め、
    /// 曲の終わりは <see cref="UdonVideoBackend"/> が見つけます。
    /// ここは<b>動画プレイヤーの様子を見て、画面の音量倍率を書くだけ</b>です。
    /// 外しても(このコンポーネントを消しても)再生はそのまま動きます。
    ///
    /// <b>同期しません。</b>各自の再生位置は同期で揃っているので、
    /// それぞれが自分の位置から計算すれば、同じ所で下がります。
    ///
    /// ───────────────────────────────────────────────
    /// <b>数え方は <c>SmartMediaPlatform.World.UdonModel.TrackFadeModel</c> の写しです。</b>
    /// あちらは純粋 C# で EditMode テスト済みです。<b>変えるときは両方を直してください。</b>
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonTrackFader : UdonSharpBehaviour
    {
        [Tooltip("様子を見る動画プレイヤー")]
        public UdonVideoBackend Backend;

        [Tooltip("音量を出す先。人が決めた音量に、ここで決めた倍率を掛ける")]
        public UdonMediaScreen Screen;

        [Header("長さ")]
        [Tooltip("終わりに向かって下げる長さ(秒)。0 にすると下げない")]
        [Range(0f, 10f)]
        public float FadeOutSeconds = 3f;

        [Tooltip("次の曲を 0 から上げる長さ(秒)。0 にするとすぐ全開")]
        [Range(0f, 10f)]
        public float FadeInSeconds = 2f;

        [Tooltip("本当の終わりより、これだけ手前で 0 にする(秒)。"
                 + "終わりに気付くのが少し早いことがあるので、その前に静かにしておく")]
        public float SilentBeforeEnd = 0.8f;

        [Tooltip("この長さより短い曲では下げない(秒)")]
        public float MinimumTrackSeconds = 20f;

        [Tooltip("これより長い「長さ」は信じない(秒)。生配信では極端な値が返ることがある")]
        public float MaximumTrackSeconds = 86400f;

        [Tooltip("止めておく。2 系統のクロスフェードと一緒に置かれたときに、根っこが立てる")]
        public bool Suspended;

        [Header("見回り")]
        [Tooltip("音量が 1 で変わりそうもないときに見に行く間隔(秒)。"
                 + "下げている・上げている最中は毎フレーム見ます")]
        [Range(0.05f, 1f)]
        public float IdleCheckInterval = 0.1f;

        // ───────── TrackFadeModel の写し ─────────

        private bool _seen;
        private int _lastLoadCount;
        private bool _fadeInArmed;
        private bool _fadeInRunning;
        private float _fadeInStartedAt;
        private float _level = 1f;

        private float _nextIdleCheck;

        /// <summary>いまの倍率(診断用)。</summary>
        public float Level
        {
            get { return _level; }
        }

        void Start()
        {
            // 前回のワールドの状態を持ち越していることは無いが、念のため全開から始める。
            if (Screen != null) Screen.SetFadeLevel(1f);
        }

        void Update()
        {
            if (Suspended) return;
            if (Backend == null || Screen == null) return;

            // 動いていないとき(全開のまま・フェードインの予定も無い)は、見る回数を減らす。
            // 下げ始める所は残り 3 秒あたりなので、0.1 秒ごとでも取りこぼしません。
            bool busy = _level < 1f || _fadeInArmed || _fadeInRunning;
            if (!busy)
            {
                if (Time.time < _nextIdleCheck) return;
                _nextIdleCheck = Time.time + IdleCheckInterval;
            }

            // 鳴り始めたか。VRChat の「鳴り始めました」が届かない環境もあるので、
            // 実際に再生中で読み込み中でもなければ、鳴っているとみなします。
            bool started = Backend.HasStarted || (Backend.IsPlaying && !Backend.IsLoading);

            float level = Tick(
                Backend.LoadCount, started,
                Backend.GetTime(), Backend.GetDuration(), Time.time);

            Screen.SetFadeLevel(level);
        }

        /// <summary>
        /// <b>何も掛けていない状態に戻す。</b>止めたとき・外したときに呼んでも安全です。
        /// </summary>
        public void ResetFade()
        {
            _fadeInArmed = false;
            _fadeInRunning = false;
            _level = 1f;

            if (Screen != null) Screen.SetFadeLevel(1f);
        }

        // ───────── 計算(TrackFadeModel の写し)─────────

        private float Tick(int loadCount, bool started, float time, float duration, float now)
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

            // ── まだ鳴り始めていない。ここで 1 に戻すと「手で変えた」と取り違える。
            if (!started)
            {
                if (_fadeInArmed) _level = 0f;
                return _level;
            }

            float level = FadeOutLevel(time, duration);

            if (_fadeInArmed)
            {
                if (!_fadeInRunning)
                {
                    _fadeInRunning = true;
                    _fadeInStartedAt = now;
                }

                float fadeIn = FadeInLevel(now - _fadeInStartedAt);

                // 上がりきったら、もう掛けない(あとで頭へシークしても下がらない)。
                if (fadeIn >= 1f)
                {
                    _fadeInArmed = false;
                    _fadeInRunning = false;
                }

                if (fadeIn < level) level = fadeIn;
            }

            _level = Mathf.Clamp01(level);
            return _level;
        }

        private float FadeOutLevel(float time, float duration)
        {
            if (FadeOutSeconds <= 0f) return 1f;

            // 長さが分からない(生配信・読み込み中)なら、終わりも分からない。
            if (duration <= 1f) return 1f;
            if (duration > MaximumTrackSeconds) return 1f;
            if (duration < MinimumTrackSeconds) return 1f;

            float untilSilent = (duration - time) - SilentBeforeEnd;

            if (untilSilent >= FadeOutSeconds) return 1f;

            // 1 ミリ秒未満は 0 とみなす(float の端数で判断がぶれないように)。
            if (untilSilent <= 0.001f) return 0f;

            return untilSilent / FadeOutSeconds;
        }

        private float FadeInLevel(float elapsedSeconds)
        {
            if (FadeInSeconds <= 0f) return 1f;
            if (elapsedSeconds <= 0f) return 0f;
            if (elapsedSeconds >= FadeInSeconds) return 1f;

            return elapsedSeconds / FadeInSeconds;
        }

        /// <summary>Console 表示用の 1 行(診断)。</summary>
        public string Describe()
        {
            if (Backend == null) return "動画プレイヤーが未設定です";
            if (Screen == null) return "音の出力先が未設定です";

            return "終わり " + FadeOutSeconds + " 秒で下げる / 次の曲 " + FadeInSeconds
                   + " 秒で上げる(いま " + Mathf.RoundToInt(_level * 100f) + "%)";
        }
    }
}
