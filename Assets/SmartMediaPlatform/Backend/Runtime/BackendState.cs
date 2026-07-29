namespace SmartMediaPlatform.Backend
{
    /// <summary>
    /// Backend の再生状態。実際に音や映像を出さない Phase1-5 でも、
    /// 状態遷移そのものは実機バックエンド(Phase2 以降)と同じものを使う。
    ///
    /// 遷移の要点:
    ///   Idle --Load--> Ready --Play--> Playing --Pause--> Paused --Resume--> Playing
    ///   Playing/Paused --Stop--> Stopped --Play--> Playing
    ///   Playing --(再生終了)--> Ended
    ///   Load 失敗 --> Error
    /// </summary>
    public enum BackendState
    {
        /// <summary>何も読み込んでいない。</summary>
        Idle = 0,

        /// <summary>読み込み中(実機の非同期ロード用。DummyBackend では経由しない)。</summary>
        Loading = 1,

        /// <summary>読み込み済みで、まだ再生していない。</summary>
        Ready = 2,

        /// <summary>再生中。</summary>
        Playing = 3,

        /// <summary>一時停止中。</summary>
        Paused = 4,

        /// <summary>停止済み(先頭に戻った状態)。読み込みは保持している。</summary>
        Stopped = 5,

        /// <summary>最後まで再生し終えた。上位はこれを合図に次へ進める。</summary>
        Ended = 6,

        /// <summary>読み込みや再生に失敗した。</summary>
        Error = 7,
    }
}
