namespace SmartMediaPlatform.Video
{
    /// <summary>
    /// 動画プレイヤー側の状態。
    ///
    /// <b>意図的に <see cref="Backend.BackendState"/> と別の型・別の名前にしています。</b>
    /// 実際の動画プレイヤー(VRChat のものを含む)は、このプラットフォームの
    /// 状態機械と同じ語彙を持っていません。両者をつなぐのが
    /// <see cref="VideoBackendAdapter"/> の仕事であり、
    /// 「Backend の違いを吸収する」とはこういうことです。
    /// </summary>
    public enum VideoPlayerState
    {
        /// <summary>何も読み込んでいない。</summary>
        None = 0,

        /// <summary>読み込み中(実機では URL の解決やバッファリング)。</summary>
        Loading = 1,

        /// <summary>読み込み完了。再生できる。</summary>
        Ready = 2,

        /// <summary>再生中。</summary>
        Playing = 3,

        /// <summary>一時停止中。</summary>
        Paused = 4,

        /// <summary>停止済み。</summary>
        Stopped = 5,

        /// <summary>最後まで再生し終えた。</summary>
        Finished = 6,

        /// <summary>失敗した(URL 不正、読み込み失敗など)。</summary>
        Failed = 7,
    }
}
