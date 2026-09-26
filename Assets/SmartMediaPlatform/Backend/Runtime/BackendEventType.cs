namespace SmartMediaPlatform.Backend
{
    /// <summary>
    /// Backend が上位へ通知する出来事の種類。
    /// 上位(将来の Player / Auto Play / DJ Mode)はこれを見て次の行動を決める。
    /// </summary>
    public enum BackendEventType
    {
        /// <summary>メディアを読み込んだ。</summary>
        Loaded = 0,

        /// <summary>再生を開始した。</summary>
        Started = 1,

        /// <summary>一時停止した。</summary>
        Paused = 2,

        /// <summary>一時停止から復帰した。</summary>
        Resumed = 3,

        /// <summary>停止した。</summary>
        Stopped = 4,

        /// <summary>スキップされた(利用者の意思による中断)。</summary>
        Skipped = 5,

        /// <summary>最後まで再生し終えた(自然終了)。自動送りの起点になる。</summary>
        Ended = 6,

        /// <summary>エラーが起きた。</summary>
        Error = 7,
    }
}
