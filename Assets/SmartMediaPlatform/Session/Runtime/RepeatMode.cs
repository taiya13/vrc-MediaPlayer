namespace SmartMediaPlatform.Session
{
    /// <summary>
    /// 繰り返しの種類。シャッフルは独立した切り替え
    /// (<see cref="PlayerSession.ShuffleEnabled"/>)なので、ここには含めない。
    /// </summary>
    public enum RepeatMode
    {
        /// <summary>繰り返さない。曲を流し切ったら止まる(自動補充が有効なら続く)。</summary>
        Off = 0,

        /// <summary>いまの曲を繰り返す。</summary>
        One = 1,

        /// <summary>セッションの曲全体を繰り返す。</summary>
        All = 2,
    }
}
