using UnityEngine;

namespace SmartMediaPlatform.Audio.Tests
{
    /// <summary>
    /// 実際に音を鳴らさない <see cref="IAudioPlayer"/>。
    ///
    /// EditMode テストでは再生が時間で進まないため、
    /// 「曲が終わった」状態を <see cref="SimulateFinished"/> で明示的に作り、
    /// Ended 検出のロジックを決定的に検証する。
    /// </summary>
    public sealed class FakeAudioPlayer : IAudioPlayer
    {
        public bool IsPlaying { get; private set; }
        public AudioClip Clip { get; private set; }
        public float Time { get; set; }

        public int PlayCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public int PauseCallCount { get; private set; }
        public int UnPauseCallCount { get; private set; }

        public void Play(AudioClip clip)
        {
            Clip = clip;
            Time = 0f;
            IsPlaying = true;
            PlayCallCount++;
        }

        public void Pause()
        {
            IsPlaying = false;
            PauseCallCount++;
        }

        public void UnPause()
        {
            IsPlaying = true;
            UnPauseCallCount++;
        }

        public void Stop()
        {
            IsPlaying = false;
            Time = 0f;
            StopCallCount++;
        }

        /// <summary>曲が最後まで再生し終わった状態にする(AudioSource の自然停止に相当)。</summary>
        public void SimulateFinished()
        {
            IsPlaying = false;
        }
    }
}
