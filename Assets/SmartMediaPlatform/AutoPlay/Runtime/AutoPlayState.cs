namespace SmartMediaPlatform.AutoPlay
{
    /// <summary>
    /// <b>おすすめ再生ループから見た状態。</b>
    ///
    /// <see cref="Session.PlaybackState"/>(セッションから見た状態)とも
    /// <see cref="Backend.BackendState"/>(Backend が報告する状態)とも<b>別の型</b>です。
    /// Phase2-4(B) から通している「層ごとに違う語彙を持つ」方針をそのまま守っています。
    ///
    /// この enum だけが持っている概念は <see cref="Recovering"/> です。
    /// 「失敗したので次の候補へ移っている最中」は、
    /// セッションから見れば単なる <c>Stopped</c> や <c>Exhausted</c> にしか見えず、
    /// <b>「壊れて止まった」のか「立て直している」のかを区別できません。</b>
    /// その区別を持つのが <see cref="AutoPlayController"/> の仕事です。
    /// </summary>
    public enum AutoPlayState
    {
        /// <summary>まだ何も始めていない。</summary>
        Idle = 0,

        /// <summary>起点を決めて Queue を組み立てている。</summary>
        Starting = 1,

        /// <summary>動画を読み込み中(実機では URL の解決やバッファリング)。</summary>
        Loading = 2,

        /// <summary>読み込みは終わったが、まだ再生していない。</summary>
        Ready = 3,

        /// <summary>再生中。</summary>
        Playing = 4,

        /// <summary>一時停止中。</summary>
        Paused = 5,

        /// <summary>
        /// <b>失敗から立て直している最中。</b>
        /// 失敗した動画を記録し、次の候補へ移そうとしている状態。
        /// ここを通っても再生は止まりません。
        /// </summary>
        Recovering = 6,

        /// <summary>利用者の意思で止めた。</summary>
        Stopped = 7,

        /// <summary>
        /// 立て直しに連続して失敗し、諦めた状態。
        /// <see cref="AutoPlayController.MaxConsecutiveFailures"/> を超えたときだけここへ来ます
        /// (失敗が続いても無限に候補を試し続けないための歯止め)。
        /// </summary>
        Failed = 8,
    }
}
