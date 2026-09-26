using System.Collections.Generic;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Adapter
{
    /// <summary>
    /// Backend の違いを吸収する共通の窓口。
    ///
    /// <b>なぜ <see cref="IMediaBackend"/> を継承するのか</b>
    /// アダプタが「バックエンドとしても振る舞える」ようにするため。
    /// これにより <see cref="BackendManager"/> / <c>MediaPlayer</c> / <c>PlayerSession</c> は
    /// <b>1 行も変更せずに</b>アダプタを受け取れる。
    /// 将来 VideoBackend を足すときも、上位のコードは一切触らなくてよい。
    ///
    /// <b>アダプタが足しているもの</b>
    ///  - <see cref="ISeekableBackend"/> を必ず実装する。
    ///    包んでいるバックエンドがシークに対応していなければ
    ///    <see cref="ISeekableBackend.CanSeek"/> が false を返すか、
    ///    アダプタ側で再生位置を持って肩代わりする。
    ///    <b>どのアダプタも同じ質問に答えられる</b>のが要点。
    ///  - <see cref="SkipNext"/> / <see cref="SkipPrevious"/>
    ///  - エラーの明示的な通知(<see cref="LastError"/> / <see cref="ReportError"/>)
    ///  - 扱えるメディア種別の公開(<see cref="SupportedTypes"/>)
    ///
    /// 上位は Audio と Video の違いを知りません。
    /// </summary>
    public interface IBackendAdapter : IMediaBackend, ISeekableBackend
    {
        /// <summary>このアダプタが扱えるメディア種別。</summary>
        IReadOnlyList<MediaType> SupportedTypes { get; }

        /// <summary>
        /// いま読み込んでいるメディア。<see cref="IMediaBackend.GetCurrent"/> と同じものを、
        /// アダプタの API 名として公開する。
        /// </summary>
        MediaItem GetCurrentMedia();

        /// <summary>再生の進み具合(0.0〜1.0)。分からなければ 0。</summary>
        float GetProgress();

        /// <summary>
        /// 次へ送る。アダプタは<b>現在のメディアを打ち切る</b>ところまでを行う。
        /// 次に何を再生するかは上位(Queue を持つ層)が決める。
        /// </summary>
        bool SkipNext();

        /// <summary>
        /// 前へ戻す。再生位置が <see cref="RewindThresholdSeconds"/> より進んでいれば
        /// <b>頭出し</b>(よくある音楽プレイヤーの挙動)、そうでなければ現在のメディアを打ち切る。
        /// 打ち切った場合、前の曲を選ぶのは上位の仕事。
        /// </summary>
        bool SkipPrevious();

        /// <summary>頭出しに切り替わる境目(秒)。</summary>
        float RewindThresholdSeconds { get; set; }

        /// <summary>直近のエラー内容。無ければ null。</summary>
        string LastError { get; }

        /// <summary>エラーを抱えているか。</summary>
        bool HasError { get; }

        /// <summary>
        /// エラーを通知する。バックエンド側の失敗(動画の読み込み失敗など)を
        /// 上位へ伝えるために使う。<see cref="BackendEventType.Error"/> が飛ぶ。
        /// </summary>
        void ReportError(string message);

        /// <summary>抱えているエラーを消す。</summary>
        void ClearError();
    }
}
