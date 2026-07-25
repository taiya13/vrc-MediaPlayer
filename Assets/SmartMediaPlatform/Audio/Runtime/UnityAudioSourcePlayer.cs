using System;
using UnityEngine;

namespace SmartMediaPlatform.Audio
{
    /// <summary>
    /// Unity の <see cref="AudioSource"/> を <see cref="IAudioPlayer"/> として包む。
    /// ここが「実際に音が出る」唯一の場所。
    /// </summary>
    public sealed class UnityAudioSourcePlayer : IAudioPlayer
    {
        private readonly AudioSource _source;

        public UnityAudioSourcePlayer(AudioSource source)
        {
            _source = source != null
                ? source
                : throw new ArgumentNullException(nameof(source));

            // ループしていると曲が終わらず Ended が発火しないため、必ず切る。
            _source.loop = false;
            _source.playOnAwake = false;
        }

        public bool IsPlaying => _source.isPlaying;

        public AudioClip Clip => _source.clip;

        public void Play(AudioClip clip)
        {
            _source.clip = clip;
            _source.time = 0f;
            _source.Play();
        }

        public void Pause()
        {
            _source.Pause();
        }

        public void UnPause()
        {
            _source.UnPause();
        }

        public void Stop()
        {
            _source.Stop();
            _source.time = 0f;
        }
    }
}
