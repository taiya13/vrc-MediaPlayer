using UdonSharp;
using UnityEngine;

namespace SmartMediaPlatform.World.Udon
{
    /// <summary>
    /// <b>曲を重ねてつなぐ担当(重ねるクロスフェード)。</b>Phase8-5。
    ///
    /// ───────────────────────────────────────────────
    /// <b>やること</b>
    ///
    /// 前の曲が小さくなっていく間に、次の曲が<b>もう鳴っていて</b>、同時に大きくなる。
    /// 動画プレイヤーを 2 つ(表と裏)使います。
    /// <list type="number">
    /// <item>終わりの 20 秒ほど前に、次の曲を<b>裏で読み込むだけ</b>(まだ鳴らさない)</item>
    /// <item>表が下がり始める所で、裏を鳴らし始める</item>
    /// <item>表が終わったら、裏を表にする(<b>読み直さない</b>)</item>
    /// </list>
    ///
    /// <b>音量の上げ下げはここでは決めません。</b>表と裏にそれぞれ付いた <see cref="UdonTrackFader"/> が、
    /// 表は残り時間で下げ、裏は音が出始めてから上げます(Phase8-3 / 8-4 で実機確認済みの仕組み)。
    /// ここが決めるのは<b>いつ読むか・いつ鳴らすか・いつやめるか・どちらを表にするか</b>だけです。
    /// <b>次に何を流すかも決めません</b>(<see cref="UdonPlayerSession.PeekNextIndex"/> に聞くだけ)。
    ///
    /// ───────────────────────────────────────────────
    /// <b>Phase7-5 の作りとの違い(前回うまく動かなかった所)</b>
    /// <list type="bullet">
    /// <item><b>音の出口を別々の GameObject に置く</b>(同じ所に 2 つ付けると、2 つめが使われなかった)</item>
    /// <item><b>早めに読み込んでおき、間に合わなければ重ねない方式に落ちる</b>(打ち切られて無音にならない)</item>
    /// <item><b>次の曲を持ち主が決めて同期する</b>(人によって違う曲が裏で鳴らない)</item>
    /// <item><b>裏の失敗は裏だけで片付ける</b>(表の曲まで止まらない)</item>
    /// <item><b>映像は混ぜず、2 枚の画面を切り替えるだけ</b>(専用シェーダーで画面が真っ白になった)</item>
    /// </list>
    ///
    /// ───────────────────────────────────────────────
    /// <b>判断の部分は <c>SmartMediaPlatform.World.UdonModel.CrossfadeModel</c> の写しです。</b>
    /// あちらは純粋 C# で EditMode テスト済みです。<b>変えるときは両方を直してください。</b>
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonCrossfadeCoordinator : UdonSharpBehaviour
    {
        [Header("★ よく触る設定")]
        [Tooltip("曲を重ねてつなぐ。切ると、重ねない方式(下げきってから次を上げる)になる")]
        public bool Enabled = true;

        [Tooltip("重ねる長さ(秒)。前の曲が下がる長さと、次の曲が上がる長さを、どちらもこれにそろえる。"
                 + "長くするほど、前の曲の終わり際(サビの余韻など)が早くから小さくなる")]
        [Range(1f, 15f)]
        public float FadeSeconds = 8f;

        [Tooltip("音量の変わり方。1 = 等パワー(重なり中も大きさが一定。ただし最後に一気に消え、最初に一気に上がって聞こえる)"
                 + " / 2 = なめらか(じわっと下がり、じわっと上がる。重なりの真ん中が少し静かになる)"
                 + " / 0 = まっすぐ(その中間)。再生中に変えても効く")]
        [Range(0, 2)]
        public int Curve = UdonMediaScreen.CurveSmooth;

        [Tooltip("Quest など Android でも重ねる。動画プレイヤーを 2 つ同時に動かすので重く、"
                 + "動くかどうか実機で確かめてから入れること。切っていれば Android では重ねない方式になる")]
        public bool AllowOnAndroid;

        [Header("つなぎ先(Prefab が配線済み)")]
        [Tooltip("次に流す曲を聞く相手")]
        public UdonPlayerSession Session;

        [Tooltip("映像と音の出口(どちらの画面を見せるかを切り替える)")]
        public UdonMediaScreen Screen;

        [Tooltip("1 つめの動画プレイヤー(最初はこちらが表)")]
        public UdonVideoBackend BackendA;

        [Tooltip("2 つめの動画プレイヤー。空なら重ねず、今までどおり 1 つで動く")]
        public UdonVideoBackend BackendB;

        [Tooltip("1 つめの音量の上げ下げ(Channel 0)")]
        public UdonTrackFader FaderA;

        [Tooltip("2 つめの音量の上げ下げ(Channel 1)")]
        public UdonTrackFader FaderB;

        [Header("細かい設定")]
        [Tooltip("本当の終わりより、これだけ手前で前の曲を 0 にする(秒)。音量担当の SilentBeforeEnd と同じ値にする")]
        public float SilentBeforeEnd = 0.8f;

        [Tooltip("重ね始める所より、これだけ前から次の曲を裏で読み込む(秒)")]
        [Range(5f, 60f)]
        public float PrepareLeadSeconds = 20f;

        [Tooltip("裏を鳴らし始めるのを、前の曲が下がり始める所よりこれだけ早める(秒)")]
        [Range(0f, 2f)]
        public float StartLeadSeconds = 0.3f;

        [Tooltip("この長さより短い曲では重ねない(秒)")]
        public float MinimumTrackSeconds = 45f;

        [Tooltip("これより長い「長さ」は信じない(秒)")]
        public float MaximumTrackSeconds = 86400f;

        [Tooltip("バーをこれ以上戻されたら、重ねるのをやめる(秒)")]
        public float SeekBackMargin = 3f;

        [Tooltip("見回りの間隔(秒)")]
        [Range(0.05f, 0.5f)]
        public float CheckInterval = 0.1f;

        [Header("困ったとき")]
        [Tooltip("読み込み始めた・重ね始めた・入れ替えた・やめた、を Console に出す")]
        public bool LogFade = true;

        // ───────── CrossfadeModel の写し ─────────

        private const int StateIdle = 0;
        private const int StatePreloading = 1;
        private const int StateOverlapping = 2;

        private const int ActionNone = 0;
        private const int ActionPreload = 1;
        private const int ActionStartBack = 2;
        private const int ActionCancel = 3;

        private int _state = StateIdle;
        private int _pending = -1;
        private bool _hasFailure;
        private int _failedForLoad;

        // ───────── ここだけの状態 ─────────

        // いま表にいるのが B か。false なら A。
        private bool _bIsFront;

        private float _nextCheck;
        private bool _initialized;

        void Start()
        {
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            // 動画プレイヤーに、裏にいる間の知らせをこちらへ送るよう教える。
            // 読み込みの間隔も、2 つ合わせて空けさせる。
            if (BackendA != null)
            {
                BackendA.Coordinator = this;
                BackendA.LoadPartner = BackendB;
            }
            if (BackendB != null)
            {
                BackendB.Coordinator = this;
                BackendB.LoadPartner = BackendA;
            }

            if (IsActive())
            {
                // 下がる長さと上がる長さを、重ねる長さにそろえる。
                ApplyFadeSettings(FaderA);
                ApplyFadeSettings(FaderB);

                ApplyLiveSettings();
            }

            ShowBackend(Front);

            if (LogFade) Log(Describe());
        }

        private void ApplyFadeSettings(UdonTrackFader fader)
        {
            if (fader == null) return;

            fader.FadeOutSeconds = FadeSeconds;
            fader.FadeInSeconds = FadeSeconds;
            fader.SilentBeforeEnd = SilentBeforeEnd;
        }

        /// <summary>
        /// <b>Inspector の設定を、音量担当と画面へ渡し直す。</b>
        /// 再生中に秒数や曲線を変えても、その場で効くように、見回りのたびに呼びます
        /// (変わっていなければ何もしない)。
        /// </summary>
        private void ApplyLiveSettings()
        {
            if (FaderA != null && FaderA.FadeOutSeconds != FadeSeconds) ApplyFadeSettings(FaderA);
            if (FaderB != null && FaderB.FadeOutSeconds != FadeSeconds) ApplyFadeSettings(FaderB);

            if (Screen != null && Screen.FadeCurve != Curve)
            {
                Screen.FadeCurve = Curve;
                Screen.ApplyVolume();
            }
        }

        // ───────── 表と裏 ─────────

        /// <summary>いま鳴っている(表の)動画プレイヤー。</summary>
        public UdonVideoBackend Front
        {
            get { return _bIsFront && BackendB != null ? BackendB : BackendA; }
        }

        /// <summary>次の曲を用意する(裏の)動画プレイヤー。1 つしか無ければ null。</summary>
        public UdonVideoBackend Back
        {
            get
            {
                if (BackendA == null || BackendB == null) return null;
                return _bIsFront ? BackendA : BackendB;
            }
        }

        /// <summary>この動画プレイヤーが、いま裏にいるか。裏の知らせは Session へ送らない。</summary>
        public bool IsBackRole(UdonVideoBackend backend)
        {
            if (backend == null) return false;
            UdonVideoBackend back = Back;
            return back != null && backend == back;
        }

        /// <summary>
        /// <b>重ねてよい構成か。</b>設定・動画プレイヤーが 2 つあるか・機種。
        /// </summary>
        public bool IsActive()
        {
            if (!Enabled || FadeSeconds <= 0f) return false;
            if (BackendA == null || BackendB == null || Screen == null) return false;
            if (FaderA == null || FaderB == null) return false;

#if UNITY_ANDROID
            if (!AllowOnAndroid) return false;
#endif
            return true;
        }

        /// <summary>裏を使っている最中か(読み込み中〜重なっている間)。</summary>
        public bool IsBusy
        {
            get { return _state != StateIdle; }
        }

        /// <summary>重なっている最中か(診断・表示用)。</summary>
        public bool IsFading
        {
            get { return _state == StateOverlapping; }
        }

        // ───────── 見回り ─────────

        void Update()
        {
            if (!_initialized) return;
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + CheckInterval;

            if (IsActive()) ApplyLiveSettings();

            Step();
            UpdateVisibleScreen();
        }

        private void Step()
        {
            if (Session == null) return;

            UdonVideoBackend front = Front;
            UdonVideoBackend back = Back;
            if (front == null || back == null) return;

            int current = Session.CurrentIndex;
            bool frontOnCurrent = current >= 0 && front.LoadedIndex == current && !front.IsLoading;

            // 次の曲を聞くのは、要るときだけ(聞くとおすすめを選ぶことがあるため)。
            int next = -1;
            if (_state != StateIdle || NearPreload(front)) next = Session.PeekNextIndex();

            bool backReady = _pending >= 0 && back.LoadedIndex == _pending && back.IsReadyToPlay;

            int action = Decide(
                IsActive(), Session.CrossfadeBlocked(), Session.IsPlaying,
                frontOnCurrent, front.IsPlaying,
                front.GetTime(), front.GetDuration(), front.LoadCount,
                next, backReady);

            if (action == ActionPreload)
            {
                BeginPreload(front, back, next);
                return;
            }

            if (action == ActionStartBack)
            {
                back.Play();
                MarkOverlapping();

                if (LogFade) Log("前の曲が下がり始めるので、次の曲を鳴らし始めます(index " + _pending + ")");
                return;
            }

            if (action == ActionCancel)
            {
                StopBack("やめました(一時停止・選び直し・バーを戻した・次の曲が変わった、のどれか)");
            }
        }

        /// <summary>
        /// 読み込み始める所に近いか。<b>おすすめを選ぶのは 1 曲に 1 回だけ</b>ですが、
        /// 曲の頭で選ぶ必要もないので、近づいてから聞きます。
        /// </summary>
        private bool NearPreload(UdonVideoBackend front)
        {
            float duration = front.GetDuration();
            if (duration <= 1f) return false;

            float remaining = duration - front.GetTime();
            return remaining <= FadeSeconds + SilentBeforeEnd + PrepareLeadSeconds + 1f;
        }

        private void BeginPreload(UdonVideoBackend front, UdonVideoBackend back, int next)
        {
            // 裏の音量担当に「次は 0 から上げる」と先に伝える(読み込みより前に)。
            UdonTrackFader backFader = FaderOf(back);
            if (backFader != null) backFader.PrepareIncoming();

            if (!back.Preload(next))
            {
                MarkFailed(front.LoadCount);
                if (LogFade) Log("次の曲を裏で読み込めませんでした。この曲は重ねずにつなぎます(index " + next + ")");
                return;
            }

            MarkPreloading(next);
            if (LogFade) Log("次の曲を裏で読み込み始めました(index " + next + ")");
        }

        /// <summary>
        /// 重なっている間は、<b>音の大きいほうの画面</b>を見せる。
        /// ちょうど真ん中(両方の音量が同じになる所)で映像が切り替わります。
        /// </summary>
        private void UpdateVisibleScreen()
        {
            UdonVideoBackend front = Front;
            UdonVideoBackend back = Back;

            if (_state != StateOverlapping || back == null)
            {
                ShowBackend(front);
                return;
            }

            UdonTrackFader frontFader = FaderOf(front);
            UdonTrackFader backFader = FaderOf(back);
            if (frontFader == null || backFader == null) return;

            ShowBackend(backFader.Level > frontFader.Level ? back : front);
        }

        // ───────── 動画プレイヤーからの知らせ ─────────

        /// <summary>
        /// <b>表の曲が終わった。</b>裏に次の曲があれば、それを表にして true。
        /// false なら、今までどおり Session に次へ進んでもらう。
        /// </summary>
        public bool NotifyFrontEnded(UdonVideoBackend backend)
        {
            if (backend == null || backend != Front) return false;
            if (_state == StateIdle) return false;

            bool blocked = Session != null && Session.CrossfadeBlocked();
            int pending = TakeSwap(blocked);

            if (pending < 0)
            {
                StopBack("止める予定があるので、次の曲は鳴らしません");
                return false;
            }

            // ── Session を進めるのは、次へ進んでよい人(持ち主・1 人用)だけ。
            //
            //    持ち主でない人が手元で進めると、同期の位置合わせが
            //    <b>前の曲の秒数を新しい曲に当てて</b>、曲の途中へ飛ばしてしまいます。
            //    持ち主でない人は、音と映像だけ入れ替えて、曲の切り替わりは同期で受け取ります
            //    (1 秒ほどで届きます。届いたときは読み直しません → FollowRemote)。
            bool commit = Session != null && Session.AutoAdvance;

            Swap(pending, commit);
            return true;
        }

        /// <summary>
        /// <b>裏で困ったことが起きた</b>(読み込みの失敗・一度も鳴らずに終わった・裏のまま終わった)。
        /// 表はそのまま鳴らし続け、この曲では重ねるのをやめます。
        /// </summary>
        public void NotifyBackTrouble(UdonVideoBackend backend)
        {
            if (backend == null || backend != Back) return;

            // 裏を使っていないときの知らせは、止めたあとに遅れて届いたもの。何もしない
            // (ここで止め直すと、止める → 知らせ → 止める … と回るおそれがある)。
            if (_state == StateIdle) return;

            UdonVideoBackend front = Front;
            int frontLoad = front != null ? front.LoadCount : 0;

            backend.Stop();
            MarkFailed(frontLoad);
            ShowBackend(front);

            if (LogFade) Log("裏の読み込みに失敗しました。いまの曲はそのまま流し、重ねずにつなぎます");
        }

        /// <summary>
        /// 互換のため残してある(Phase7-5 では Session がここを呼んでいた)。
        /// いまは表の終わりを動画プレイヤーが直接知らせるので、ここはほぼ通りません。
        /// </summary>
        public bool FinishNow()
        {
            return NotifyFrontEnded(Front);
        }

        // ───────── Session からの指示 ─────────

        /// <summary>
        /// <b>いますぐこれを流す</b>(手で選んだ・次へ・前へ・失敗して飛ばした)。重ねません。
        /// それが<b>裏にもう読ませてある曲なら、読み直さずに裏を表にします</b>
        /// (重なっている最中に「次へ」を押したとき)。
        /// </summary>
        public bool PlayImmediate(int catalogIndex)
        {
            EnsureInitialized();

            if (_state != StateIdle)
            {
                int promoted = PromoteFor(catalogIndex);
                if (promoted >= 0)
                {
                    // Session はもうこの曲へ移っているので、進め直さない。
                    Swap(promoted, false);
                    return true;
                }

                StopBack("別の曲が選ばれたので、裏の曲はやめました");
            }

            UdonVideoBackend front = Front;
            if (front == null) return false;

            ShowBackend(front);
            return front.LoadAndPlay(catalogIndex);
        }

        /// <summary>
        /// <b>同期で、持ち主の曲が変わったと届いた。</b>持ち主以外の手元で呼ばれます。
        /// 自分の手元でもう切り替わっていれば何もせず、裏に読ませてあれば表にし、
        /// どちらでもなければ読み込みます。
        /// </summary>
        public void FollowRemote(int catalogIndex)
        {
            EnsureInitialized();

            UdonVideoBackend front = Front;
            if (front == null) return;

            if (front.LoadedIndex == catalogIndex && catalogIndex >= 0)
            {
                // すでに手元で入れ替わっている(自分の手元でも曲が終わって、裏を表にした)。
                if (!front.IsPlaying && !front.IsLoading) front.Play();
                return;
            }

            if (_state != StateIdle)
            {
                int promoted = PromoteFor(catalogIndex);
                if (promoted >= 0)
                {
                    // 同期で受け取った状態はもう Session に入っているので、進め直さない。
                    Swap(promoted, false);
                    return;
                }

                StopBack("持ち主が別の曲にしたので、裏の曲はやめました");
            }

            ShowBackend(front);
            front.LoadAndPlay(catalogIndex);
        }

        /// <summary>
        /// 重ねている途中でやめる(止められた・一時停止された)。表だけが鳴っている状態に戻します。
        /// </summary>
        public void CancelFade()
        {
            if (_state == StateIdle) return;
            StopBack("止められたので、裏の曲もやめました");
        }

        /// <summary>
        /// 互換のため残してある。再生予定の変化は、見回りのたびに次の曲を聞き直して拾います
        /// (読み込み中なら読み直し、重なっている最中ならそのまま)。
        /// </summary>
        public void NotifyQueueChanged()
        {
            _nextCheck = 0f;
        }

        // ───────── 入れ替え ─────────

        /// <summary>
        /// <b>裏を表にする。</b>裏はもう読み込んである(または読み込み中)なので、読み直しません。
        /// </summary>
        /// <param name="pending">裏に読ませてある曲。</param>
        /// <param name="commit">Session を次の曲へ進めるか(自分で選んだときは、もう進んでいる)。</param>
        private void Swap(int pending, bool commit)
        {
            UdonVideoBackend oldFront = Front;

            _bIsFront = !_bIsFront;

            UdonVideoBackend newFront = Front;

            // まだ鳴らしていなければ鳴らす(読み込み中なら、読み終わったら鳴る)。
            if (newFront != null && !newFront.IsPlaying) newFront.Play();

            if (oldFront != null && oldFront != newFront) oldFront.Stop();

            ShowBackend(newFront);

            if (Session != null)
            {
                if (commit) Session.CommitAdvanceTo(pending);

                // 裏にいる間は「鳴り始めた」を Session へ送っていないので、ここで送る
                // (履歴・再生回数に数えるため)。まだ鳴っていなければ、鳴り始めたときに届く。
                // Session がまだこの曲へ進んでいない(持ち主でない人)なら送らない —
                // 前の曲を二度数えてしまうため。
                if (newFront != null && newFront.HasStarted && Session.CurrentIndex == pending)
                {
                    Session.NotifyStarted();
                }
            }

            if (LogFade) Log("次の曲へ入れ替えました(index " + pending + ")");
        }

        private void StopBack(string reason)
        {
            UdonVideoBackend back = Back;
            bool wasBusy = _state != StateIdle;

            Reset();

            if (back != null && (wasBusy || back.IsPlaying || back.IsLoading)) back.Stop();

            ShowBackend(Front);

            if (wasBusy && LogFade) Log(reason);
        }

        // ───────── 出力 ─────────

        private void ShowBackend(UdonVideoBackend backend)
        {
            if (Screen == null) return;
            Screen.ShowChannel(backend != null && backend == BackendB);
        }

        private UdonTrackFader FaderOf(UdonVideoBackend backend)
        {
            if (backend == null) return null;
            if (backend == BackendB) return FaderB;
            return FaderA;
        }

        private void Log(string message)
        {
            Debug.Log("[UdonCrossfadeCoordinator] " + message, gameObject);
        }

        // ───────── 判断(CrossfadeModel の写し)─────────

        private int Decide(
            bool enabled, bool blocked, bool sessionPlaying,
            bool frontOnCurrent, bool frontPlaying,
            float frontTime, float frontDuration, int frontLoad,
            int nextIndex, bool backReady)
        {
            // 表の曲が変わったら、前の曲での失敗は忘れる。
            if (_hasFailure && frontLoad != _failedForLoad) _hasFailure = false;

            bool lengthKnown = LengthUsable(frontDuration);
            float remaining = frontDuration - frontTime;

            if (_state == StateIdle)
            {
                if (!enabled || blocked) return ActionNone;
                if (!sessionPlaying || !frontOnCurrent || !frontPlaying) return ActionNone;
                if (nextIndex < 0 || _hasFailure || !lengthKnown) return ActionNone;

                if (remaining > PreloadRemaining) return ActionNone;

                // 終わりぎりぎりでは読まない。終わりの合図とぶつかるので、今までの道に任せる。
                if (remaining <= SilentBeforeEnd + 1f) return ActionNone;

                return ActionPreload;
            }

            // ── ここから下は、裏を使っている最中。
            //    やめる理由が 1 つでもあれば、やめる。
            if (!enabled || blocked || !frontOnCurrent || !lengthKnown) return ActionCancel;

            if (_state == StatePreloading)
            {
                // 次の曲が変わった(再生予定に入れた・並べ替えた)。読み直すために一度やめる。
                if (nextIndex != _pending) return ActionCancel;

                // バーを大きく戻された。まだ当分鳴らさないので、裏を空ける。
                if (remaining > PreloadRemaining + SeekBackMargin) return ActionCancel;

                // 一時停止中は、読み込んだまま待つ(再開したら続きから)。
                if (!sessionPlaying || !frontPlaying) return ActionNone;

                if (remaining <= FadeStartRemaining + StartLeadSeconds && backReady)
                {
                    return ActionStartBack;
                }

                // 読み込みが間に合っていないなら待つ。表が終わったら、裏を表にする(TakeSwap)。
                return ActionNone;
            }

            // ── 重なっている最中。
            //    <b>次の曲が変わっても、やめません。</b>もう聞こえている曲を途中で切ると、
            //    壊れたように聞こえます。新しく入れた曲は、そのあとに流れます。

            // 一時停止・停止されたら、裏も止める(片方だけ鳴り続けないように)。
            if (!sessionPlaying) return ActionCancel;

            // バーを戻された。重ねる所より前なので、裏を止める。
            if (remaining > FadeStartRemaining + StartLeadSeconds + SeekBackMargin) return ActionCancel;

            return ActionNone;
        }

        /// <summary>前の曲が下がり始める、残り時間(秒)。</summary>
        public float FadeStartRemaining
        {
            get { return FadeSeconds + SilentBeforeEnd; }
        }

        /// <summary>次の曲を裏で読み込み始める、残り時間(秒)。</summary>
        public float PreloadRemaining
        {
            get { return FadeStartRemaining + PrepareLeadSeconds; }
        }

        private void MarkPreloading(int catalogIndex)
        {
            if (_state != StateIdle) return;
            if (catalogIndex < 0) return;

            _state = StatePreloading;
            _pending = catalogIndex;
        }

        private void MarkOverlapping()
        {
            if (_state != StatePreloading) return;
            _state = StateOverlapping;
        }

        private void Reset()
        {
            _state = StateIdle;
            _pending = -1;
        }

        private void MarkFailed(int frontLoad)
        {
            Reset();
            _hasFailure = true;
            _failedForLoad = frontLoad;
        }

        private int TakeSwap(bool blocked)
        {
            if (_state == StateIdle || _pending < 0) return -1;

            int pending = _pending;
            Reset();

            if (blocked) return -1;
            return pending;
        }

        private int PromoteFor(int requested)
        {
            if (_state == StateIdle) return -1;

            int pending = _pending;
            Reset();

            if (requested < 0 || requested != pending) return -1;
            return pending;
        }

        private bool LengthUsable(float duration)
        {
            if (duration <= 1f) return false;
            if (duration > MaximumTrackSeconds) return false;
            if (duration < MinimumTrackSeconds) return false;
            return true;
        }

        /// <summary>Console 表示用の 1 行(診断)。</summary>
        public string Describe()
        {
            if (BackendA == null) return "動画プレイヤー A がありません";
            if (BackendB == null) return "動画プレイヤー B がありません(重ねません)";
            if (FaderA == null || FaderB == null) return "音量の担当が 2 つそろっていません(重ねません)";
            if (!IsActive()) return "重ねない設定です(Android では AllowOnAndroid が切れていると重ねません)";

            return FadeSeconds + " 秒重ねてつなぎます"
                   + (_state == StateOverlapping ? "(いま重なっています)"
                      : _state == StatePreloading ? "(次の曲を裏で読み込み中)" : "");
        }
    }
}
