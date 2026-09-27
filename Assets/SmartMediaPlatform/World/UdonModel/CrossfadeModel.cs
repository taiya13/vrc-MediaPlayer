namespace SmartMediaPlatform.World.UdonModel
{
    /// <summary>
    /// <b>重ねるクロスフェードの段取り。Udon へ写す前の正典。</b>Phase8-5。
    /// <c>UdonCrossfadeCoordinator</c> が 1:1 で写しています。<b>変えるときは両方を直してください。</b>
    ///
    /// ───────────────────────────────────────────────
    /// <b>やりたいこと</b>
    ///
    /// 前の曲が小さくなっていく間に、次の曲が<b>もう鳴っていて</b>、同時に大きくなる。
    /// そのために動画プレイヤーを 2 つ(表と裏)使います。
    /// <list type="number">
    /// <item>終わりの 20 秒ほど前に、次の曲を<b>裏で読み込むだけ</b>(まだ鳴らさない)</item>
    /// <item>表が下がり始める所で、裏を鳴らし始める</item>
    /// <item>表が終わったら、裏を表にする(読み直さない)</item>
    /// </list>
    /// 音量の上げ下げそのものは、各プレイヤーに付いた <see cref="TrackFadeModel"/> の仕事です
    /// (表は残り時間で下がり、裏は音が出始めてから上がる)。
    /// <b>ここが決めるのは「いつ読むか・いつ鳴らすか・いつやめるか」だけ</b>です。
    ///
    /// ───────────────────────────────────────────────
    /// <b>間に合わなくても壊れない</b>
    ///
    /// 裏の読み込みが遅れて、表の終わりまでに鳴らせなかったら、
    /// 表が終わった所で裏を表にします。裏はまだ読み込み中なので、鳴り始めた所から上がります。
    /// つまり<b>重ねない方式(Phase8-3)と同じ聞こえ方に落ちるだけ</b>で、無音で止まったり、
    /// 同じ曲を読み直したりはしません。
    /// 裏の読み込みが<b>失敗</b>したら、この曲では重ねるのをやめ、表はそのまま最後まで鳴らします
    /// (前回の作りでは、裏の失敗で表まで止まっていました)。
    /// </summary>
    public sealed class CrossfadeModel
    {
        // ───────── 設定 ─────────

        /// <summary>重ねる長さ(秒)。表が下がる長さと、裏が上がる長さを、どちらもこれにそろえる。</summary>
        public float FadeSeconds = 6f;

        /// <summary>
        /// 本当の終わりより、これだけ手前で表を 0 にする(秒)。
        /// <see cref="TrackFadeModel.SilentBeforeEnd"/> と同じ値にすること(下がり始める所がずれるため)。
        /// </summary>
        public float SilentBeforeEnd = 0.8f;

        /// <summary>
        /// 裏で読み込み始めるのを、重ね始める所よりこれだけ前にする(秒)。
        /// YouTube の読み込みは数秒〜十数秒かかることがあるので、余裕を持たせる。
        /// </summary>
        public float PrepareLeadSeconds = 20f;

        /// <summary>
        /// 裏を鳴らし始めるのを、表が下がり始める所よりこれだけ早める(秒)。
        /// 「鳴らして」から実際に音が出るまでの間を見込む。
        /// </summary>
        public float StartLeadSeconds = 0.3f;

        /// <summary>この長さより短い曲では重ねない(秒)。短い曲では、裏の読み込みの時間が取れない。</summary>
        public float MinimumTrackSeconds = 45f;

        /// <summary>これより長い「長さ」は信じない(秒)。生配信では極端な値が返ることがある。</summary>
        public float MaximumTrackSeconds = 86400f;

        /// <summary>
        /// 人がバーを戻したと見なす余裕(秒)。
        /// 読み込み・重ねる予定の所より、これ以上前へ戻されたらやめる。
        /// </summary>
        public float SeekBackMargin = 3f;

        // ───────── 状態 ─────────

        /// <summary>何もしていない。表だけが鳴っている。</summary>
        public const int StateIdle = 0;

        /// <summary>裏で次の曲を読み込ませた(まだ鳴らしていない)。</summary>
        public const int StatePreloading = 1;

        /// <summary>裏も鳴っている(重なっている)。</summary>
        public const int StateOverlapping = 2;

        // ───────── 答え ─────────

        /// <summary>何もしない。</summary>
        public const int ActionNone = 0;

        /// <summary>次の曲を裏で読み込む(鳴らさない)。成功したら <see cref="MarkPreloading"/>。</summary>
        public const int ActionPreload = 1;

        /// <summary>裏を鳴らし始める。そのあと <see cref="MarkOverlapping"/>。</summary>
        public const int ActionStartBack = 2;

        /// <summary>やめる。裏を止めて <see cref="Reset"/>。</summary>
        public const int ActionCancel = 3;

        private int _state = StateIdle;
        private int _pending = -1;

        // 裏の読み込みに失敗した曲では、もう重ねない(同じ失敗を繰り返さないため)。
        // 「どの曲のときに失敗したか」は、表の読み込み回数で覚える。
        private bool _hasFailure;
        private int _failedForLoad;

        public int State { get { return _state; } }

        /// <summary>裏に読ませた曲(catalog index)。無ければ -1。</summary>
        public int PendingIndex { get { return _pending; } }

        /// <summary>裏を使っている最中か(読み込み中〜重なっている間)。</summary>
        public bool IsBusy { get { return _state != StateIdle; } }

        /// <summary>表が下がり始める、残り時間(秒)。</summary>
        public float FadeStartRemaining { get { return FadeSeconds + SilentBeforeEnd; } }

        /// <summary>裏で読み込み始める、残り時間(秒)。</summary>
        public float PreloadRemaining { get { return FadeStartRemaining + PrepareLeadSeconds; } }

        // ───────── 毎回の判断 ─────────

        /// <summary>
        /// <b>いま何をするか。</b>見回りのたびに呼びます。状態は変えません
        /// (実際にやれたかどうかは呼び出し側にしか分からないので、Mark〜 で知らせてもらう)。
        /// </summary>
        /// <param name="enabled">重ねてよい構成か(設定・機種)。</param>
        /// <param name="blocked">いまは重ねてはいけないか(おやすみタイマーの「この曲で止める」・あとで流す URL がある)。</param>
        /// <param name="sessionPlaying">人が「再生中」にしているか(一時停止・停止なら false)。</param>
        /// <param name="frontOnCurrent">表が、いま選ばれている曲を鳴らしているか(読み込み中・外部 URL なら false)。</param>
        /// <param name="frontPlaying">表の動画プレイヤーが実際に進んでいるか。</param>
        /// <param name="frontTime">表の再生位置(秒)。</param>
        /// <param name="frontDuration">表の長さ(秒)。分からなければ 0。</param>
        /// <param name="frontLoad">表の読み込み回数(曲が変わったかを見分ける)。</param>
        /// <param name="nextIndex">次に流す曲。無ければ -1。</param>
        /// <param name="backReady">裏が、読ませた曲を読み終えて、鳴らせる状態か。</param>
        public int Decide(
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

        // ───────── やったことを知らせてもらう ─────────

        /// <summary>裏で <paramref name="catalogIndex"/> を読み込ませた。</summary>
        public void MarkPreloading(int catalogIndex)
        {
            if (_state != StateIdle) return;
            if (catalogIndex < 0) return;

            _state = StatePreloading;
            _pending = catalogIndex;
        }

        /// <summary>裏を鳴らし始めた。</summary>
        public void MarkOverlapping()
        {
            if (_state != StatePreloading) return;
            _state = StateOverlapping;
        }

        /// <summary>やめた。</summary>
        public void Reset()
        {
            _state = StateIdle;
            _pending = -1;
        }

        /// <summary>
        /// <b>裏の読み込みに失敗した。</b>やめたうえで、表がこの曲の間は、もう重ねない。
        /// </summary>
        public void MarkFailed(int frontLoad)
        {
            Reset();
            _hasFailure = true;
            _failedForLoad = frontLoad;
        }

        /// <summary>
        /// <b>表が終わった。</b>裏を表にするなら、その曲を返す(状態は空に戻る)。
        /// 裏を使っていない・いまは重ねてはいけない、なら -1(呼び出し側は裏を止めて、今までの道に任せる)。
        /// </summary>
        public int TakeSwap(bool blocked)
        {
            if (_state == StateIdle || _pending < 0) return -1;

            int pending = _pending;
            Reset();

            if (blocked) return -1;
            return pending;
        }

        /// <summary>
        /// <b>「いますぐこの曲を」と言われた</b>(手で選んだ・同期で持ち主が変えた)。
        /// それが裏に読ませてある曲なら、その曲を返す(裏をそのまま表にすればよい。読み直さない)。
        /// 違う曲なら -1(呼び出し側は裏を止める)。どちらでも状態は空に戻る。
        /// </summary>
        public int PromoteFor(int requested)
        {
            if (_state == StateIdle) return -1;

            int pending = _pending;
            Reset();

            if (requested < 0 || requested != pending) return -1;
            return pending;
        }

        // ───────── 内部 ─────────

        private bool LengthUsable(float duration)
        {
            if (duration <= 1f) return false;
            if (duration > MaximumTrackSeconds) return false;
            if (duration < MinimumTrackSeconds) return false;
            return true;
        }
    }
}
