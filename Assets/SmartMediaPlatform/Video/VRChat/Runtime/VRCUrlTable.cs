using System;
using System.Collections.Generic;
using SmartMediaPlatform.Catalog;
using UnityEngine;
using VRC.SDKBase;

namespace SmartMediaPlatform.Video.VRChat
{
    /// <summary>
    /// <b>ベイク済み <see cref="VRCUrl"/> の表。</b>
    ///
    /// 提案書の結論どおり <b>VRCUrl は実行時に生成できません</b>。
    /// そこで Catalog の URL 文字列から <see cref="VRCUrl"/> を作るのは<b>編集時だけ</b>にし、
    /// 実行時は「文字列で引く」だけにします。Phase1-2 の <c>UdonCatalogBaker</c> と同じ方式です。
    ///
    /// <see cref="IVideoUrlTable"/> を実装しているので、
    /// <see cref="VRChatVideoBackend"/> はこの表を「再生してよい URL の一覧」としても使えます。
    /// </summary>
    [Serializable]
    public sealed class VRCUrlTable : IVideoUrlTable
    {
        [Tooltip("Catalog から焼き込んだ URL 文字列(_urls と同じ並び)")]
        [SerializeField] private string[] _keys = new string[0];

        [Tooltip("編集時に生成した VRCUrl(実行時には作れない)")]
        [SerializeField] private VRCUrl[] _urls = new VRCUrl[0];

        private Dictionary<string, int> _lookup;

        public int Count => _keys != null ? _keys.Length : 0;

        /// <summary>焼き込まれた URL 文字列(表示・診断用)。</summary>
        public IReadOnlyList<string> Keys => _keys ?? (_keys = new string[0]);

        public bool Contains(string url)
        {
            return IndexOf(url) >= 0;
        }

        /// <summary>URL 文字列に対応するベイク済み <see cref="VRCUrl"/>。無ければ null。</summary>
        public VRCUrl Resolve(string url)
        {
            int index = IndexOf(url);
            return index >= 0 && _urls != null && index < _urls.Length ? _urls[index] : null;
        }

        public int IndexOf(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return -1;

            EnsureLookup();
            return _lookup.TryGetValue(url, out int index) ? index : -1;
        }

        private void EnsureLookup()
        {
            if (_lookup != null) return;

            _lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (_keys == null) return;

            for (int i = 0; i < _keys.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(_keys[i])) continue;
                if (!_lookup.ContainsKey(_keys[i])) _lookup[_keys[i]] = i;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// カタログの指定種別(既定は Video / Live)の URL を焼き込む。<b>編集時専用。</b>
        ///
        /// 実行時に呼べないようにしてあるのが要点です — ここが
        /// 「実行時に VRCUrl を生成しない」という前提をコードで守っている場所です。
        /// </summary>
        /// <returns>焼き込んだ件数。</returns>
        public int Bake(IMediaCatalog catalog, params MediaType[] types)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            var source = new CatalogVideoUrlTable(catalog, types);
            return Bake(source.Urls);
        }

        /// <summary>URL 文字列の一覧を焼き込む。<b>編集時専用。</b></summary>
        public int Bake(IReadOnlyList<string> urls)
        {
            if (urls == null) throw new ArgumentNullException(nameof(urls));

            _keys = new string[urls.Count];
            _urls = new VRCUrl[urls.Count];

            for (int i = 0; i < urls.Count; i++)
            {
                _keys[i] = urls[i];
                _urls[i] = string.IsNullOrEmpty(urls[i]) ? VRCUrl.Empty : new VRCUrl(urls[i]);
            }

            _lookup = null;
            return _keys.Length;
        }
#endif

        public override string ToString()
        {
            return $"VRCUrlTable ({Count} baked urls)";
        }
    }
}
