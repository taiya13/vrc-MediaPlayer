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

        [Header("音が出始めるのを待つ(Phase8-4)")]
        [Tooltip("再生位置がこれだけ進んだら「音が出始めた」とみなして上げ始める(秒)。"
                 + "再生中になってから実際に音が出るまでの間に上げきってしまわないように")]
        public float ProgressThreshold = 0.1f;

        [Tooltip("位置が進まなくても、これだけ待ったら上げ始める(秒)。生配信で無音のまま止まらないように")]
        [Range(0.5f, 10f)]
        public float MaxWaitForProgress = 3f;

        [Header("困ったとき")]
        [Tooltip("下げ始めた・上げ始めた・手で変えたのでフェードしない、などを Console に出す。"
                 + "実機で「効いていない」ように感じたときの手掛かりになる")]
        public bool LogTransitions = true;

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

        // 音が出始めるのを待っている間の控え(Phase8-4)
        private bool _progressWatching;
        private float _progressFrom;
        private float _progressWaitStartedAt;

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

            float time = Backend.GetTime();
            float duration = Backend.GetDuration();

            // ログのために、進める前の様子を控えておく(計算そのものには関わらない)。
            bool wasSeen = _seen;
            int wasLoad = _lastLoadCount;
            bool wasArmed = _fadeInArmed;
            bool wasRunning = _fadeInRunning;
            float wasLevel = _level;

            float level = Tick(Backend.LoadCount, started, time, duration, Time.time);

            Screen.SetFadeLevel(level);

            if (LogTransitions)
            {
                ReportTransition(wasSeen, wasLoad, wasArmed, wasRunning, wasLevel, time, duration);
            }
        }

        /// <summary>
        /// <b>状態が変わった瞬間だけ Console に 1 行出す。</b>Phase8-4。
        /// 実機では耳だけが頼りなので、「下げたのに聞き分けられない」のか
        /// 「そもそも下げていない」のかを、ここで見分けられるようにします。
        /// </summary>
        private void ReportTransition(
            bool wasSeen, int wasLoad, bool wasArmed, bool wasRunning, float wasLevel,
            float time, float duration)
        {
            bool trackChanged = wasSeen && _lastLoadCount != wasLoad;

            if (trackChanged && !_fadeInArmed)
            {
                Log(wasLevel >= 0.999f
                    ? "曲が変わりました。途中で手で変えたので、フェードせず全開で始めます"
                    : "曲が変わりましたが、フェードインは切ってあるので全開で始めます");
                return;
            }

            if (!wasArmed && _fadeInArmed)
            {
                Log("曲が自然に終わりました。次の曲は音が出始めるのを待ってから上げます");
                return;
            }

            if (!wasRunning && _fadeInRunning)
            {
                float waited = Time.time - _progressWaitStartedAt;
                Log(waited >= MaxWaitForProgress
                    ? "再生位置が進まないので、待たずに上げ始めます(" + FadeInSeconds + " 秒)"
                    : "音が出始めたので上げ始めます(" + FadeInSeconds + " 秒・待ち "
                      + Mathf.RoundToInt(waited * 10f) / 10f + " 秒)");
                return;
            }

            if (wasArmed && !_fadeInArmed && !trackChanged)
            {
                Log("上げきりました");
                return;
            }

            if (wasLevel >= 0.999f && _level < 0.999f && !_fadeInArmed)
            {
                Log("終わりに向かって下げ始めました(残り "
                    + Mathf.RoundToInt((duration - time) * 10f) / 10f + " 秒)");
            }
        }

        private void Log(string message)
        {
            Debug.Log("[UdonTrackFader] " + message, gameObject);
        }

        /// <summary>
        /// <b>何も掛けていない状態に戻す。</b>止めたとき・外したときに呼んでも安全です。
        /// </summary>
        public void ResetFade()
        {
            _fadeInArmed = false;
            _fadeInRunning = false;
            _progressWatching = false;
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
                _progressWatching = false;

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
                    // ── 音が出始めるまで待つ(Phase8-4)。
                    //    「再生中」になった瞬間ではなく、<b>再生位置が実際に進み始めた瞬間</b>から数えます。
                    if (!_progressWatching)
                    {
                        _progressWatching = true;
                        _progressFrom = time;
                        _progressWaitStartedAt = now;
                    }

                    // 前の曲の位置が残っていた(大きく戻った)なら、そこから数え直す。
                    if (time < _progressFrom - 1f) _progressFrom = time;

                    bool moved = time > _progressFrom + ProgressThreshold;
                    bool gaveUp = now - _progressWaitStartedAt >= MaxWaitForProgress;

                    if (!moved && !gaveUp)
                    {
                        _level = 0f;
                        return _level;
                    }

                    _fadeInRunning = true;

                    // 位置が進んだぶんだけ、音はもう出ていた。そのぶん遡って数え始める。
                    // 待ちきれずに始めたときは、位置が当てにならないので「いま」から。
                    _fadeInStartedAt = moved ? now - (time - _progressFrom) : now;
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
