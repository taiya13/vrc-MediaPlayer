namespace SmartMediaPlatform.Backend
{
    /// <summary>
    /// 再生位置を扱えるバックエンドが「追加で」実装する能力。
    ///
    /// なぜ <see cref="IMediaBackend"/> に足さないのか:
    ///  - DummyBackend(再生しない)や将来の Live 配信バックエンドは、
    ///    本質的に再生位置を持たない / シークできない。
    ///    コアの インターフェース に押し込むと、実装できないメソッドを
    ///    嘘の値や例外で埋めることになり置換可能性(Liskov)が壊れる。
    ///  - 能力を別 インターフェース に分けておけば、
    ///    新しい能力(再生速度・音量・字幕…)が必要になっても
    ///    <see cref="IMediaBackend"/> 自体は二度と変更しなくてよい。
    ///
    /// 上位層は <c>backend as ISeekableBackend</c> で問い合わせ、
    /// 対応していなければシーク系の機能を無効化する
    /// (<c>MediaPlayer.CanSeek()</c> がその判定を代行する)。
    /// </summary>
    public interface ISeekableBackend
    {
        /// <summary>
        /// 今この瞬間シークできるか。
        /// 生配信のように「再生位置は分かるがシークはできない」場合は false を返す。
        /// </summary>
        bool CanSeek { get; }

        /// <summary>現在の再生位置(秒)。分からなければ 0。</summary>
        float GetCurrentTime();

        /// <summary>メディア全体の長さ(秒)。分からなければ 0。</summary>
        float GetDuration();

        /// <summary>
        /// 再生位置を移動する。
        /// </summary>
        /// <param name="normalizedPosition">0.0(先頭)〜1.0(末尾)。範囲外は丸める。</param>
        /// <returns>移動できたら true。</returns>
        bool Seek(float normalizedPosition);
    }
}
