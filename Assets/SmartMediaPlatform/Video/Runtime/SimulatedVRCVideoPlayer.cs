namespace SmartMediaPlatform.Video
{
    /// <summary>
    /// VRChat SDK を使わずに <see cref="IVRCVideoPlayer"/> の振る舞いを再現する実装。
    /// <b>動画は出ません</b> — <see cref="DummyVideoBackend"/> と同じ立ち位置の「代役」です。
    ///
    /// 実機の動画プレイヤーと同じ 4 つの性質を持ちます:
    /// <list type="number">
    /// <item><b>読み込みが非同期</b>(<see cref="LoadURL"/> の直後は <see cref="IsReady"/> が false)</item>
    /// <item><b>時間が勝手に進む</b>(<see cref="Advance"/> で進める)</item>
    /// <item><b>終端で自然に止まる</b>(<see cref="IsPlaying"/> が false になる)</item>
    /// <item><b>失敗しうる</b>(<see cref="FailNextLoad"/>)</item>
    /// </list>
    ///
    /// これがあるおかげで、<see cref="VRChatVideoBackend"/> の状態機械を
    /// <b>SDK 無しの EditMode テストと ConsoleDemo で決定的に検証</b>できます。
    /// Audio 層のテストが <c>IAudioPlayer</c> を差し替えるのと同じやり方です。
    /// </summary>
    public sealed class SimulatedVRCVideoPlayer : IVRCVideoPlayer
    {
        private readonly IVideoUrlTable _urls;

        private string _url;
        private float _time;
        private bool _ready;
        private bool _playing;

        /// <param name="urls">ベイク済み URL 表(実機の VRCUrl 表に相当)。</param>
        /// <param name="kind">再現するプレイヤーの種類。</param>
        public SimulatedVRCVideoPlayer(
            IVideoUrlTable urls, VRCVideoPlayerKind kind = VRCVideoPlayerKind.Unity)
        {
            _urls = urls;
            Kind = kind;
        }

        public VRCVideoPlayerKind Kind { get; }

        public bool IsPlaying => _playing;

        public bool IsReady => _ready;

        public bool Loop { get; set; }

        /// <summary>読み込みを同期的に完了させるか。false なら <see cref="CompleteLoading"/> を待つ。</summary>
        public bool AutoCompleteLoading { get; set; }

        /// <summary>読み込み後に報告する長さ(秒)。0 にすると生配信のように長さ不明になる。</summary>
        public float DurationSeconds { get; set; } = 120f;

        /// <summary>次の <see cref="LoadURL"/> を失敗させる(ベイク表にあっても false を返す)。</summary>
        public bool FailNextLoad { get; set; }

        /// <summary>いま読み込んでいる URL。</summary>
        public string CurrentUrl => _url;

        public bool LoadURL(string url)
        {
            if (FailNextLoad)
            {
                FailNextLoad = false;
                return false;
            }
            if (_urls != null && !_urls.Contains(url)) return false;

            _url = url;
            _time = 0f;
            _ready = false;
            _playing = false;

            if (AutoCompleteLoading) CompleteLoading();
            return true;
        }

        /// <summary>読み込みが終わったことにする(実機の OnVideoReady 相当の状態変化)。</summary>
        public void CompleteLoading()
        {
            if (_url != null) _ready = true;
        }

        public void Play()
        {
            if (_url == null || !_ready) return;
            _playing = true;
        }

        public void Pause()
        {
            _playing = false;
        }

        public void Stop()
        {
            _playing = false;
            _time = 0f;
        }

        public void SetTime(float seconds)
        {
            float max = GetDuration();
            _time = seconds < 0f ? 0f : (max > 0f && seconds > max ? max : seconds);
        }

        public float GetTime() => _time;

        public float GetDuration() => _url != null ? DurationSeconds : 0f;

        /// <summary>
        /// 再生位置を進める(実機では勝手に進む)。
        /// 終端まで来たら実機と同じく<b>自分で止まる</b>ので、
        /// <see cref="VRChatVideoBackend.Tick"/> が再生終了を検出できる。
        /// </summary>
        public void Advance(float seconds)
        {
            if (!_playing) return;

            _time += seconds;

            float max = GetDuration();
            if (max > 0f && _time >= max)
            {
                _time = max;
                _playing = false;
            }
        }

        /// <summary>終端まで一気に進めて再生を終わらせる。</summary>
        public void FinishPlayback()
        {
            float max = GetDuration();
            if (max > 0f) _time = max;
            _playing = false;
        }
    }
}
