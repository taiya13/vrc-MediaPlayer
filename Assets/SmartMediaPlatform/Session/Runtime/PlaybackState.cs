namespace SmartMediaPlatform.Session
{
    /// <summary>
    /// セッションから見た再生状態。
    ///
    /// <see cref="Backend.BackendState"/>(Backend が報告する低レベルの状態)とは別物で、
    /// こちらは「利用者に見せる状態」を表す。両者の違いが出るのは主に次の 2 点:
    ///  - Backend の <c>Ready</c> / <c>Loading</c> はまとめて <see cref="Ready"/> として扱う
    ///  - Backend が <c>Ended</c> でも、次の曲があるなら再生は続くので
    ///    「もう流すものが無い」ときだけ <see cref="Exhausted"/> になる
    /// </summary>
    public enum PlaybackState
    {
        /// <summary>何も読み込んでいない。</summary>
        Idle = 0,

        /// <summary>読み込み済みで、まだ再生していない。</summary>
        Ready = 1,

        /// <summary>再生中。</summary>
        Playing = 2,

        /// <summary>一時停止中。</summary>
        Paused = 3,

        /// <summary>停止中。</summary>
        Stopped = 4,

        /// <summary>流す曲が尽きた(最後まで再生し終えて、次が無い)。</summary>
        Exhausted = 5,
    }
}
