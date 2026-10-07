using System;
using System.Collections.Generic;
using SmartMediaPlatform.Catalog;
using UnityEngine;

namespace SmartMediaPlatform.Audio
{
    /// <summary>
    /// Inspector で「カタログの ID」と「AudioClip」を対応づけるための 1 行分。
    /// </summary>
    [Serializable]
    public struct AudioClipEntry
    {
        [Tooltip("Catalog の MediaItem.Id(例: music-001)")]
        public string MediaId;

        public AudioClip Clip;

        public AudioClipEntry(string mediaId, AudioClip clip)
        {
            MediaId = mediaId;
            Clip = clip;
        }
    }

    /// <summary>
    /// <see cref="MediaItem"/> と <see cref="AudioClip"/> の対応表。
    ///
    /// Catalog は「どんなメディアがあるか」しか知らず、実体(AudioClip)は持たない。
    /// その橋渡しをここが担うことで、Catalog を汚さずに再生資源を紐づけられる。
    /// Phase2 以降で Video を足す場合も、同じ形の対応表を別に用意すればよい。
    /// </summary>
    public sealed class AudioClipLibrary
    {
        private readonly Dictionary<string, AudioClip> _clips =
            new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

        public int Count => _clips.Count;

        public AudioClipLibrary() { }

        public AudioClipLibrary(IEnumerable<AudioClipEntry> entries)
        {
            if (entries == null) return;
            foreach (var entry in entries) Register(entry.MediaId, entry.Clip);
        }

        /// <summary>
        /// 対応を登録する。同じ ID を再登録すると上書きする。
        /// ID が空、またはクリップが null の場合は登録しない(false を返す)。
        /// </summary>
        public bool Register(string mediaId, AudioClip clip)
        {
            if (string.IsNullOrEmpty(mediaId) || clip == null) return false;

            _clips[mediaId] = clip;
            return true;
        }

        public bool Register(MediaItem item, AudioClip clip)
        {
            return item != null && Register(item.Id, clip);
        }

        public bool Contains(string mediaId)
        {
            return !string.IsNullOrEmpty(mediaId) && _clips.ContainsKey(mediaId);
        }

        public bool Contains(MediaItem item)
        {
            return item != null && Contains(item.Id);
        }

        /// <summary>対応するクリップ。無ければ null。</summary>
        public AudioClip Get(string mediaId)
        {
            if (string.IsNullOrEmpty(mediaId)) return null;
            return _clips.TryGetValue(mediaId, out var clip) ? clip : null;
        }

        public AudioClip Get(MediaItem item)
        {
            return item != null ? Get(item.Id) : null;
        }

        public bool Remove(string mediaId)
        {
            return !string.IsNullOrEmpty(mediaId) && _clips.Remove(mediaId);
        }

        public void Clear()
        {
            _clips.Clear();
        }
    }
}
