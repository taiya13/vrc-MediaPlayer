using SmartMediaPlatform.Catalog;
using UnityEngine;

namespace SmartMediaPlatform.Audio
{
    /// <summary>
    /// 音声ファイルを用意しなくても「実際に音が鳴る」ようにするための、
    /// 手続き的な AudioClip 生成。
    ///
    /// カタログの ID から決まった高さの短い音を作る。
    /// 本番の音源が用意できるまでの繋ぎであり、
    /// <see cref="AudioClipLibrary"/> に本物の AudioClip を登録すればそちらが優先される。
    /// </summary>
    public static class ProceduralClipFactory
    {
        private const int SampleRate = 44100;

        /// <summary>ID から決まる音階(ドレミ…)。同じ ID なら常に同じ音になる。</summary>
        private static readonly float[] Scale =
        {
            261.63f, // C4
            293.66f, // D4
            329.63f, // E4
            349.23f, // F4
            392.00f, // G4
            440.00f, // A4
            493.88f, // B4
            523.25f, // C5
        };

        /// <summary>
        /// メディア 1 件ぶんのクリップを作る。
        /// </summary>
        /// <param name="item">対象メディア。ID から音の高さを決める。</param>
        /// <param name="durationSeconds">
        /// 長さ(秒)。統合デモで曲の終わりを待てるよう、既定は短くしてある。
        /// </param>
        public static AudioClip Create(MediaItem item, float durationSeconds = 1.5f)
        {
            if (item == null) return null;
            return Create(item.Id, durationSeconds);
        }

        public static AudioClip Create(string mediaId, float durationSeconds = 1.5f)
        {
            if (string.IsNullOrEmpty(mediaId)) return null;
            if (durationSeconds <= 0f) durationSeconds = 0.1f;

            float frequency = FrequencyFor(mediaId);
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * durationSeconds));

            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / SampleRate;

                // 端をなめらかに絞って、ぶつ切りのノイズが出ないようにする。
                float progress = (float)i / sampleCount;
                float envelope = Mathf.Min(1f, progress * 20f) * Mathf.Min(1f, (1f - progress) * 20f);

                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.25f * envelope;
            }

            var clip = AudioClip.Create($"tone-{mediaId}", sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>ID から決定的に音の高さを決める。</summary>
        public static float FrequencyFor(string mediaId)
        {
            if (string.IsNullOrEmpty(mediaId)) return Scale[0];

            // 文字コードの合計を使う(実行ごとに変わらない単純な方法)。
            int sum = 0;
            for (int i = 0; i < mediaId.Length; i++) sum += mediaId[i];

            return Scale[sum % Scale.Length];
        }

        /// <summary>
        /// カタログ全件ぶんのクリップを作って対応表を埋める。
        /// すでに登録済みの ID は上書きしない(本物の音源を優先するため)。
        /// </summary>
        /// <returns>新しく作った件数。</returns>
        public static int FillLibrary(
            AudioClipLibrary library, IMediaCatalog catalog,
            float durationSeconds = 1.5f, MediaType type = MediaType.Music)
        {
            if (library == null || catalog == null) return 0;

            int created = 0;
            foreach (var item in catalog.FilterByType(type))
            {
                if (library.Contains(item)) continue;
                if (library.Register(item, Create(item, durationSeconds))) created++;
            }
            return created;
        }
    }
}
