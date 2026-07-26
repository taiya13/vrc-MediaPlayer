namespace SmartMediaPlatform.Playlists
{
    /// <summary>
    /// 再生モード。Queue をどう維持するかを決める。
    ///
    /// 自動補充の ON/OFF は <see cref="AutoQueueService.AutoQueueEnabled"/> でも切り替えられる。
    /// <see cref="ShuffleAutoQueue"/> は「シャッフル + 自動補充」をひとまとめにした指定で、
    /// このモードのときは AutoQueueEnabled の設定によらず自動補充が有効になる。
    /// </summary>
    public enum PlaybackMode
    {
        /// <summary>プレイリストの順に再生し、尽きたら止まる(自動補充が有効なら補充する)。</summary>
        Normal = 0,

        /// <summary>同じ曲を繰り返す。</summary>
        RepeatOne = 1,

        /// <summary>プレイリスト全体を繰り返す。</summary>
        RepeatAll = 2,

        /// <summary>プレイリストをシャッフルして再生する。</summary>
        Shuffle = 3,

        /// <summary>シャッフル再生 + おすすめによる自動補充。</summary>
        ShuffleAutoQueue = 4,
    }
}
