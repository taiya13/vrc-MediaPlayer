namespace SmartMediaPlatform.Video
{
    /// <summary>
    /// VRChat の動画イベントの種類。
    ///
    /// VRChat が UdonBehaviour へ送るイベント名にそのまま対応しています。
    /// <see cref="VideoErrorKind"/> と同じく、SDK の型を
    /// <c>Video/Runtime</c>(<c>noEngineReferences: true</c>)へ持ち込まないための写しです。
    /// </summary>
    public enum VideoEventKind
    {
        None = 0,

        /// <summary>実機の <c>OnVideoReady</c>。読み込みが終わった。</summary>
        Ready = 1,

        /// <summary>実機の <c>OnVideoStart</c>。再生が始まった。</summary>
        Start = 2,

        /// <summary>実機の <c>OnVideoPlay</c>。一時停止から再開した。</summary>
        Play = 3,

        /// <summary>実機の <c>OnVideoPause</c>。一時停止した。</summary>
        Pause = 4,

        /// <summary>実機の <c>OnVideoEnd</c>。最後まで再生した。</summary>
        End = 5,

        /// <summary>実機の <c>OnVideoLoop</c>。ループした。</summary>
        Loop = 6,

        /// <summary>実機の <c>OnVideoError</c>。失敗した。</summary>
        Error = 7,
    }

    /// <summary>
    /// <b>動画イベントの受け口。</b>
    ///
    /// メソッド名は VRChat の動画イベント名にそのまま合わせてあります。
    /// 実機からイベントを運んでくる側(Udon 中継・ホスト・テスト)は、
    /// 何を使っていてもこの インターフェース に流し込むだけで済みます。
    ///
    /// 実装は <see cref="VideoEventBridge"/> 1 つだけで、
    /// そこに重複排除と <see cref="VRChatVideoBackend.Tick"/> との調停が集約されています。
    /// <b>イベントの経路が増えても、判断ロジックは 1 箇所のまま</b>というのが狙いです。
    /// </summary>
    public interface IVideoEventSink
    {
        /// <returns>受理したら true。すでに反映済みなどで無視したら false。</returns>
        bool OnVideoReady();

        bool OnVideoStart();

        bool OnVideoPlay();

        bool OnVideoPause();

        bool OnVideoEnd();

        bool OnVideoLoop();

        bool OnVideoError(VideoErrorKind kind, string message = null);
    }
}
