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

        public float Time
        {
            get => _source.time;
            set
            {
                // AudioSource.time はクリップ長を超えると例外になるため、手前で丸める。
                var clip = _source.clip;
                float max = clip != null ? clip.length : 0f;
                float clamped = value < 0f ? 0f : value;
                if (max > 0f && clamped >= max) clamped = Mathf.Max(0f, max - 0.01f);
                _source.time = clamped;
            }
        }

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
