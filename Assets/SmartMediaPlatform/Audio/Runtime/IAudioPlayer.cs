using UnityEngine;

namespace SmartMediaPlatform.Audio
{
    /// <summary>
    /// 音を鳴らす手段の抽象。実体は <see cref="UnityAudioSourcePlayer"/>(AudioSource)。
    ///
    /// <see cref="AudioBackend"/> がこの インターフェース越しにしか AudioSource を触らないため、
    /// EditMode テストでは偽の実装に差し替えて、実際に音を鳴らさずに
    /// 状態遷移や Ended 検出のロジックを決定的に検証できる。
    /// </summary>
    public interface IAudioPlayer
    {
        /// <summary>再生中か。曲が最後まで終わると false になる(これが Ended 検出の根拠)。</summary>
        bool IsPlaying { get; }

        /// <summary>現在割り当てられているクリップ。</summary>
        AudioClip Clip { get; }

        /// <summary>
        /// 再生位置(秒)。シークはここへ代入して行う。
        /// AudioSource.time に対応する。
        /// </summary>
        float Time { get; set; }

        /// <summary>クリップを頭から再生する。</summary>
        void Play(AudioClip clip);

        /// <summary>一時停止する(再生位置は保持)。</summary>
        void Pause();

        /// <summary>一時停止から再開する。</summary>
        void UnPause();

        /// <summary>停止して再生位置を先頭に戻す。</summary>
        void Stop();
    }
}
