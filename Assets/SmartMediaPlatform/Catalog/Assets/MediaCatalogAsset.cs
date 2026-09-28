using System;
using System.Collections.Generic;
using UnityEngine;

namespace SmartMediaPlatform.Catalog.Assets
{
    /// <summary>
    /// <b>Catalog Builder が生成する <c>Catalog.asset</c> の受け皿。</b>
    ///
    /// <c>IMediaCatalogSource</c>(Phase1-1 からの拡張点)を実装した
    /// <c>ScriptableObject</c> です。<b>これを足しただけで、
    /// カタログの作り方が「手書きのクラス」から「アセット」へ変わります。</b>
    /// 利用側(<c>CatalogStore</c> より上)のコードは 1 行も変わりません。
    ///
    /// <code>
    /// // 手書き(いま)
    /// var store = new CatalogStore(new MediaCatalog(new VideoCatalogSource()));
    ///
    /// // アセット(将来 — Catalog Builder が生成)
    /// var store = new CatalogStore(new MediaCatalog(catalogAsset));
    /// </code>
    ///
    /// <b>Catalog Builder はここへ書き込むだけで済みます。</b>
    /// <see cref="SetEntries"/> が編集時専用の書き込み口です
    /// (実行時に書き換えられると、焼き込み済み <c>VRCUrl</c> と食い違うため塞いであります)。
    ///
    /// <b>URL について</b><br/>
    /// ここに入るのは URL <b>文字列</b>です。<c>VRCUrl</c> への焼き込みは
    /// 今までどおり <c>VRCUrlTable.Bake()</c>(編集時)が行います。
    /// この アセット は URL を「メタデータとして持っているだけ」で、
    /// UI へは <c>CatalogStore</c> が <c>DisplayMeta</c> に変換するときに落とされます。
    /// </summary>
    [CreateAssetMenu(
        fileName = "Catalog",
        menuName = "Smart Media Platform/Media Catalog",
        order = 0)]
    public sealed class MediaCatalogAsset : ScriptableObject, IMediaCatalogSource
    {
        /// <summary>
        /// アセットに保存する 1 件ぶん。
        /// <c>MediaItem</c> はイミュータブルで Unity にシリアライズできないため、
        /// <b>保存用の器としてこちらを持ちます</b>(読み込み時に <c>MediaItem</c> へ変換)。
        /// </summary>
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("カタログ内で一意な ID(例: video-001)")]
            public string Id;

            public string Title;

            [Tooltip("アーティスト / チャンネル / 配信者名")]
            public string Artist;

            public MediaType Type = MediaType.Video;

            public string Genre;

            public string[] Tags;

            [Tooltip("再生 URL(文字列)。VRCUrl への焼き込みは編集時に別途行う")]
            public string Url;

            [Tooltip("秒。不明なら 0")]
            public int DurationSeconds;

            [Tooltip("関連メディアの ID。Catalog Builder が事前計算して埋める想定")]
            public string[] RelatedIds;
        }

        [Header("カタログ")]
        [Tooltip("Catalog Builder が生成する。手で編集しても構わない")]
        [SerializeField] private Entry[] _entries = new Entry[0];

        [Header("生成情報(任意)")]
        [Tooltip("どこから生成したか(Catalog Builder が記録する用)")]
        [SerializeField] private string _sourceDescription = "";

        [Tooltip("いつ生成したか(Catalog Builder が記録する用)")]
        [SerializeField] private string _generatedAt = "";

        /// <summary>登録件数。</summary>
        public int Count => _entries != null ? _entries.Length : 0;

        /// <summary>どこから生成したか。</summary>
        public string SourceDescription => _sourceDescription;

        /// <summary>いつ生成したか。</summary>
        public string GeneratedAt => _generatedAt;

        /// <summary>
        /// <c>MediaCatalog</c> へ渡すための読み出し。
        /// <b>ID が空の行と、ID が重なった行は落とします</b>(生成物が壊れていても止まらないように)。
        /// </summary>
        public IReadOnlyList<MediaItem> LoadItems()
        {
            var items = new List<MediaItem>();
            if (_entries == null) return items;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _entries.Length; i++)
            {
                var entry = _entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Id)) continue;
                if (!seen.Add(entry.Id)) continue;

                // MediaItem はタイトル必須。生成物が欠けていても落ちないよう ID で代用する。
                string title = string.IsNullOrWhiteSpace(entry.Title) ? entry.Id : entry.Title;

                items.Add(new MediaItem(
                    id: entry.Id,
                    title: title,
                    artist: entry.Artist,
                    type: entry.Type,
                    genre: entry.Genre,
                    tags: entry.Tags,
                    url: entry.Url,
                    durationSeconds: entry.DurationSeconds,
                    relatedIds: entry.RelatedIds));
            }

            return items;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 中身を差し替える。<b>編集時専用</b>(Catalog Builder の書き込み口)。
        /// </summary>
        public void SetEntries(IReadOnlyList<Entry> entries, string sourceDescription = null)
        {
            _entries = entries != null ? new List<Entry>(entries).ToArray() : new Entry[0];
            if (sourceDescription != null) _sourceDescription = sourceDescription;
            _generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// 既存の <c>IMediaCatalogSource</c> の中身をこのアセットへ写す。
        /// <b>Catalog Builder ができるまでの繋ぎ</b>として、
        /// 手書きカタログをアセット化するのに使えます。
        /// </summary>
        /// <returns>写した件数。</returns>
        public int ImportFrom(IMediaCatalogSource source)
        {
            if (source == null) return 0;

            var items = source.LoadItems();
            if (items == null) return 0;

            var entries = new List<Entry>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null) continue;

                entries.Add(new Entry
                {
                    Id = item.Id,
                    Title = item.Title,
                    Artist = item.Artist,
                    Type = item.Type,
                    Genre = item.Genre,
                    Tags = ToArray(item.Tags),
                    Url = item.Url,
                    DurationSeconds = item.DurationSeconds,
                    RelatedIds = ToArray(item.RelatedIds),
                });
            }

            SetEntries(entries, source.GetType().Name);
            return entries.Count;
        }

        private static string[] ToArray(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0) return new string[0];

            var array = new string[values.Count];
            for (int i = 0; i < values.Count; i++) array[i] = values[i];
            return array;
        }
#endif

        public override string ToString()
        {
            return $"MediaCatalogAsset({Count} 件"
                   + (string.IsNullOrEmpty(_sourceDescription) ? "" : $" / {_sourceDescription}")
                   + ")";
        }
    }
}
