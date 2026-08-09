namespace SmartMediaPlatform.World.UdonModel
{
    /// <summary>
    /// <b>再生位置の時計。Phase5-4 の「正典」。</b>
    ///
    /// <b>なぜこれがあるのか</b><br/>
    /// <see cref="PlaybackModel"/> / <see cref="ListPageModel"/> と同じ理由です。
    /// 実機で動くのは <c>UdonSyncCoordinator</c> ですが、
    /// <c>UdonSharpBehaviour</c> は EditMode から触れません。そこで
    /// <b>Udon で書ける形のまま純粋 C# で書いて先に検証し、1 対 1 で写す</b>手順を取ります。
    /// <b>数え方を変えるときは必ず両方を直してください。</b>
    ///
    /// <b>何を解くのか</b><br/>
    /// 「いま何秒目か」を<b>全員が同じ答えにする</b>ことです。
    /// 各自の <c>Time.time</c> は入室時刻がばらばらなので使えません。
    /// VRChat が配る<b>サーバー時刻</b>だけを基準にします。
    ///
    /// <code>
    /// 位置(いま) = 基準位置 + (再生中なら いまのサーバー時刻 − 基準サーバー時刻)
    /// </code>
    ///
    /// この形にすると、同期する値が
    /// <list type="bullet">
    /// <item>基準位置(ms)</item>
    /// <item>基準サーバー時刻(ms)</item>
    /// <item>再生中かどうか</item>
    /// </list>
    /// の <b>3 つだけ</b>で済みます。毎フレーム再生位置を流す必要はありません。
    /// 一時停止も「その時点の位置を基準に据えて止める」だけで表せます。
    ///
    /// <b>サーバー時刻は int で、いつか一周します。</b>
    /// (<c>Networking.GetServerTimeInMilliseconds()</c> は <c>int</c>。約 24.8 日で一周)
    /// 引き算だけで扱えば一周をまたいでも正しい差になるので、
    /// <b>大小比較はせず、必ず差だけを見ます</b>。
    /// </summary>
    public sealed class PlaybackClockModel
    {
        private int _baseServerTime;
        private int _basePositionMs;
        private bool _playing;

        /// <summary>基準にしたサーバー時刻(ms)。</summary>
        public int BaseServerTime
        {
            get { return _baseServerTime; }
        }

        /// <summary>基準にした再生位置(ms)。</summary>
        public int BasePositionMs
        {
            get { return _basePositionMs; }
        }

        public bool IsPlaying
        {
            get { return _playing; }
        }

        /// <summary>
        /// <paramref name="serverTimeMs"/> の時点で <paramref name="positionMs"/> だった、
        /// として再生を始める。
        /// </summary>
        public void PlayFrom(int serverTimeMs, int positionMs)
        {
            _baseServerTime = serverTimeMs;
            _basePositionMs = positionMs < 0 ? 0 : positionMs;
            _playing = true;
        }

        /// <summary><paramref name="positionMs"/> で止める。</summary>
        public void PauseAt(int serverTimeMs, int positionMs)
        {
            _baseServerTime = serverTimeMs;
            _basePositionMs = positionMs < 0 ? 0 : positionMs;
            _playing = false;
        }

        /// <summary>頭から流し直す。</summary>
        public void Restart(int serverTimeMs)
        {
            PlayFrom(serverTimeMs, 0);
        }

        /// <summary>受け取った同期値をそのまま当てる(途中参加もこれ 1 つで足りる)。</summary>
        public void Apply(int baseServerTime, int basePositionMs, bool playing)
        {
            _baseServerTime = baseServerTime;
            _basePositionMs = basePositionMs < 0 ? 0 : basePositionMs;
            _playing = playing;
        }

        /// <summary>
        /// <paramref name="serverTimeMs"/> の時点で何 ms 目か。
        ///
        /// <b>止まっているときは基準位置そのもの</b>です
        /// (時間が経っても進まない、が一時停止の意味なので)。
        /// </summary>
        public int PositionMsAt(int serverTimeMs)
        {
            if (!_playing) return _basePositionMs;

            // 引き算だけで扱う。int が一周していても差は正しく出る。
            int elapsed = serverTimeMs - _baseServerTime;
            int position = _basePositionMs + elapsed;

            return position < 0 ? 0 : position;
        }

        /// <summary>秒で欲しいとき。</summary>
        public float PositionSecondsAt(int serverTimeMs)
        {
            return PositionMsAt(serverTimeMs) * 0.001f;
        }

        /// <summary>
        /// 手元の再生位置がずれすぎているか。
        ///
        /// <b>ずれていても、止まっているときは直しません。</b>
        /// 一時停止中に何度も seek すると、見ている人には
        /// 画面がちらつくだけで得がないためです。
        /// </summary>
        public bool NeedsCorrection(int serverTimeMs, int actualMs, int toleranceMs)
        {
            if (!_playing) return false;
            if (toleranceMs < 0) toleranceMs = 0;

            int drift = actualMs - PositionMsAt(serverTimeMs);
            if (drift < 0) drift = -drift;

            return drift > toleranceMs;
        }
    }
}
