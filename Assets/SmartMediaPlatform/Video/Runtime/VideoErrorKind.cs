namespace SmartMediaPlatform.Video
{
    /// <summary>
    /// 動画の読み込み・再生に失敗した理由。
    ///
    /// VRChat SDK の <c>VRC.SDK3.Components.Video.VideoError</c> を、
    /// <b>SDK に依存しない形で写したもの</b>です。
    /// <see cref="VideoPlayerState"/> と同じ考え方で、SDK の型を
    /// <c>Video/Runtime</c>(<c>noEngineReferences: true</c>)へ持ち込まずに済ませます。
    /// 変換は SDK 側のブリッジ(<c>SmartMediaPlatform.Video.VRChat</c>)が行います。
    /// </summary>
    public enum VideoErrorKind
    {
        /// <summary>エラーなし。</summary>
        None = 0,

        /// <summary>原因不明(SDK の <c>VideoError.Unknown</c>)。</summary>
        Unknown = 1,

        /// <summary>URL が不正、またはカタログに登録されていない。</summary>
        InvalidUrl = 2,

        /// <summary>アクセスを拒否された(ユーザー設定で untrusted URL が無効など)。</summary>
        AccessDenied = 3,

        /// <summary>プレイヤー側の失敗(コーデック非対応・再生不能など)。</summary>
        PlayerError = 4,

        /// <summary>読み込み要求が短時間に集中して制限された。</summary>
        RateLimited = 5,

        /// <summary>読み込みが所定時間内に終わらなかった(SDK には無い、こちら側の判定)。</summary>
        Timeout = 6,
    }
}
