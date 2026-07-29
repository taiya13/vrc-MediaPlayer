namespace SmartMediaPlatform.Video
{
    /// <summary>
    /// VRChat の動画プレイヤーの種類。
    ///
    /// <see cref="VRChatVideoBackend"/> は「どちらの実装が繋がっているか」を
    /// <b>再生可否の判断にだけ</b>使います(AVPro は生配信を扱えるが Unity 版は扱えない)。
    /// 操作そのものは <see cref="IVRCVideoPlayer"/> 越しなので、両者を区別しません。
    /// </summary>
    public enum VRCVideoPlayerKind
    {
        /// <summary><c>VRCUnityVideoPlayer</c>。ファイル系の動画向け。生配信は不可。</summary>
        Unity = 0,

        /// <summary><c>VRCAVProVideoPlayer</c>。生配信(rtsp / rtmp / HLS)も扱える。</summary>
        AVPro = 1,
    }

    /// <summary>
    /// VRChat の <c>BaseVRCVideoPlayer</c>(<c>VRCUnityVideoPlayer</c> /
    /// <c>VRCAVProVideoPlayer</c> の共通基底)を抽象化したもの。
    ///
    /// <b>この インターフェース が「SDK を閉じ込める壁」です。</b>
    /// メンバー名は SDK の API にそのまま合わせてあり、実体は
    /// <c>SmartMediaPlatform.Video.VRChat</c> アセンブリの <c>VRCVideoPlayerBridge</c> が
    /// <c>BaseVRCVideoPlayer</c> を包んで提供します。
    ///
    /// SDK と違う点は <see cref="LoadURL"/> だけです:
    /// SDK は <c>VRCUrl</c> を受け取りますが、<b>VRCUrl は実行時に生成できない</b>ため、
    /// ここでは URL <b>文字列</b>を渡し、ブリッジが<b>事前にベイクした VRCUrl 表</b>から引きます。
    /// 表に無い URL は <c>false</c> が返り、実行時生成は一切行いません。
    ///
    /// Audio 層の <c>IAudioPlayer</c> と同じ役割で、EditMode テストでは
    /// 偽の実装に差し替えて SDK 無しで状態遷移を検証できます。
    /// </summary>
    public interface IVRCVideoPlayer
    {
        /// <summary>繋がっている VRChat 動画プレイヤーの種類。</summary>
        VRCVideoPlayerKind Kind { get; }

        /// <summary>再生中か(SDK の <c>IsPlaying</c>)。</summary>
        bool IsPlaying { get; }

        /// <summary>読み込みが終わって再生できる状態か(SDK の <c>IsReady</c>)。</summary>
        bool IsReady { get; }

        /// <summary>
        /// ループ再生するか(SDK の <c>Loop</c>)。
        /// true のままだと動画が終わらず <c>OnVideoEnd</c> が来ないため、
        /// <see cref="VRChatVideoBackend"/> は必ず false にします。
        /// </summary>
        bool Loop { get; set; }

        /// <summary>
        /// ベイク済みの VRCUrl を引いて読み込みを開始する(SDK の <c>LoadURL(VRCUrl)</c>)。
        /// </summary>
        /// <returns>
        /// 表に URL があり読み込みを開始できたら true。
        /// <b>登録されていない URL なら false</b>(実行時に VRCUrl は作りません)。
        /// </returns>
        bool LoadURL(string url);

        /// <summary>再生を開始・再開する(SDK の <c>Play</c>)。</summary>
        void Play();

        /// <summary>一時停止する(SDK の <c>Pause</c>)。</summary>
        void Pause();

        /// <summary>停止する(SDK の <c>Stop</c>)。</summary>
        void Stop();

        /// <summary>再生位置を秒で設定する(SDK の <c>SetTime</c>)。</summary>
        void SetTime(float seconds);

        /// <summary>現在の再生位置(秒)(SDK の <c>GetTime</c>)。</summary>
        float GetTime();

        /// <summary>動画の長さ(秒)。読み込み前や生配信では 0 や無限大になる(SDK の <c>GetDuration</c>)。</summary>
        float GetDuration();
    }
}
