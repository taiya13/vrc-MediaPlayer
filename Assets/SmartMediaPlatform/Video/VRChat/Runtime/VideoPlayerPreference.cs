namespace SmartMediaPlatform.Video.VRChat
{
    /// <summary>
    /// <b>どちらの動画プレイヤーを使うか</b>の指定(Inspector 用)。
    ///
    /// Phase4-1 の設計方針で<b>実機再生は AVPro を標準</b>にしましたが、
    /// <see cref="VRCVideoPlayerKind"/> の抽象化はそのまま残してあります。
    /// 切り替えは<b>この 1 つのドロップダウン</b>で済み、
    /// <c>VideoBackendAdapter</c> より上("PlayerSession / Queue / Recommendation")は
    /// 何が繋がっているかを知りません。
    ///
    /// <b>編集時の注意:</b> <c>VRCAVProVideoPlayer</c> は Unity エディタ(ClientSim)では
    /// 映像を出しません。エディタで絵を確認したいときだけ <see cref="Unity"/> にしてください。
    /// </summary>
    public enum VideoPlayerPreference
    {
        /// <summary>
        /// <b>AVPro を優先する(既定・実機の標準)。</b>
        /// 生配信(rtsp / rtmp / HLS)も扱えます。
        /// 同じ GameObject に AVPro が無ければ Unity 版へ自動で降ります。
        /// </summary>
        AVPro = 0,

        /// <summary>
        /// <c>VRCUnityVideoPlayer</c> を優先する。
        /// エディタで映像を確認したいときはこちら。生配信は扱えません。
        /// </summary>
        Unity = 1,

        /// <summary>
        /// Inspector の <c>Video Player</c> に割り当てたものをそのまま使う(自動で探さない)。
        /// 別の GameObject に置いたプレイヤーを指したいときに。
        /// </summary>
        Explicit = 2,
    }
}
