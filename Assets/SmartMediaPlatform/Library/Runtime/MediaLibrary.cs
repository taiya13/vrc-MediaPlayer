using System;
using System.Collections.Generic;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Store;

namespace SmartMediaPlatform.Library
{
    /// <summary>
    /// <b>カタログ全体を閲覧し、1 件を選ぶだけのクラス。</b>
    ///
    /// <code>
    /// var store   = new CatalogStore(catalog);        // 唯一の窓口
    /// var library = new MediaLibrary(store);
    /// library.ShowOnly(MediaType.Video);              // 動画だけ見る
    /// library.Select(0);                              // 先頭を選ぶ
    /// PlayableRef playable = library.SelectedRef;     // ← 再生系へ渡すのはこれ
    /// </code>
    ///
    /// <b>Phase4-2 での変更:データの取り方を <see cref="ICatalogStore"/> に一本化しました。</b>
    /// Phase4-1 では <c>IMediaCatalog</c> から <c>MediaItem</c> を受け取って
    /// そのまま UI へ渡していたので、<b>UI から <c>MediaItem.Url</c> が見えていました</b>。
    /// いまは <see cref="DisplayMeta"/>(URL 無し)しか出てこないので、
    /// 「URL を知るのは VideoBackend だけ」が<b>型のレベルで</b>守られます。
    ///
    /// <b>再生の手段は持っていません。</b>
    /// このクラスから <c>PlayerSession</c> も <c>Queue</c> も <c>Backend</c> も見えません
    /// (asmdef の参照が <c>SmartMediaPlatform.Catalog</c> だけ)。
    /// 再生へつなぐのは <c>LibraryPlaybackBridge</c>(別 asmdef)の仕事です。
    ///
    /// <b>一覧は毎回作り直しません。</b>
    /// 絞り込み・並び順・<see cref="Refresh"/> のときだけ組み直して覚えておきます。
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public sealed class MediaLibrary : MediaListViewBase, IMediaLibrary
    {
        /// <summary>絞り込みなしを表す種別の並び(Catalog の enum 全部)。</summary>
        private static readonly MediaType[] AllTypes =
        {
            MediaType.Unknown, MediaType.Music, MediaType.Video,
            MediaType.Podcast, MediaType.Live,
        };

        private MediaType[] _visibleTypes;
        private IReadOnlyList<MediaType> _readOnlyVisibleTypes;
        private LibrarySortOrder _sortOrder = LibrarySortOrder.CatalogOrder;

        /// <param name="store">データを引く唯一の窓口。</param>
        /// <param name="visibleTypes">
        /// 最初に表示する種別。省略するとすべて表示します。
        /// 「動画だけの一覧」にしたいときは <c>MediaType.Video</c> を渡してください。
        /// </param>
        public MediaLibrary(ICatalogStore store, params MediaType[] visibleTypes)
            : base(store)
        {
            SetVisibleTypes(visibleTypes);
            Rebuild(notify: false);
        }

        // ───────── 絞り込み・並び順 ─────────

        public IReadOnlyList<MediaType> VisibleTypes => _readOnlyVisibleTypes;

        public LibrarySortOrder SortOrder
        {
            get => _sortOrder;
            set
            {
                if (_sortOrder == value) return;
                _sortOrder = value;
                Rebuild(notify: true);
            }
        }

        public void ShowAll()
        {
            SetVisibleTypes(null);
            Rebuild(notify: true);
        }

        /// <summary>
        /// 指定した種別だけを表示する。
        /// <code>
        /// library.ShowOnly(MediaType.Video);                  // 動画だけ
        /// library.ShowOnly(MediaType.Music, MediaType.Video); // 音楽と動画
        /// </code>
        /// 何も渡さないと <see cref="ShowAll"/> と同じになります。
        /// </summary>
        public void ShowOnly(params MediaType[] types)
        {
            SetVisibleTypes(types);
            Rebuild(notify: true);
        }

        public bool IsVisible(MediaType type)
        {
            for (int i = 0; i < _visibleTypes.Length; i++)
            {
                if (_visibleTypes[i] == type) return true;
            }
            return false;
        }

        /// <summary>
        /// カタログを読み直して一覧を作り直す。
        /// <b>選択は可能なかぎり保ちます</b>(同じ ID がまだ一覧にあれば選び直す)。
        /// </summary>
        public void Refresh()
        {
            Rebuild(notify: true);
        }

        // ───────── 何を並べるか ─────────

        protected override void BuildEntries(List<DisplayMeta> into)
        {
            // ID の並びを窓口から受け取り、表示情報に変える。
            // カタログの内部構造(MediaItem)はここにも出てきません。
            var ids = Store.GetAllIds();
            for (int i = 0; i < ids.Count; i++)
            {
                var meta = Store.GetDisplayMeta(ids[i]);
                if (meta != null && IsVisible(meta.Type)) into.Add(meta);
            }

            Sort(into);
        }

        // ───────── 内部 ─────────

        private void SetVisibleTypes(MediaType[] types)
        {
            _visibleTypes = types != null && types.Length > 0
                ? (MediaType[])types.Clone()
                : (MediaType[])AllTypes.Clone();

            _readOnlyVisibleTypes = Array.AsReadOnly(_visibleTypes);
        }

        private void Sort(List<DisplayMeta> entries)
        {
            switch (_sortOrder)
            {
                case LibrarySortOrder.Title:
                    entries.Sort((a, b) => CompareText(a.Title, b.Title));
                    break;

                case LibrarySortOrder.Artist:
                    entries.Sort((a, b) =>
                    {
                        int byArtist = CompareText(a.Artist, b.Artist);
                        return byArtist != 0 ? byArtist : CompareText(a.Title, b.Title);
                    });
                    break;

                case LibrarySortOrder.Genre:
                    entries.Sort((a, b) =>
                    {
                        int byGenre = CompareText(a.Genre, b.Genre);
                        return byGenre != 0 ? byGenre : CompareText(a.Title, b.Title);
                    });
                    break;

                case LibrarySortOrder.Duration:
                    entries.Sort((a, b) =>
                    {
                        int byDuration = a.DurationSeconds.CompareTo(b.DurationSeconds);
                        return byDuration != 0 ? byDuration : CompareText(a.Title, b.Title);
                    });
                    break;

                default:
                    break;   // CatalogOrder — 窓口が返した並びをそのまま使う
            }
        }

        private static int CompareText(string a, string b)
        {
            return string.Compare(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString()
        {
            return $"MediaLibrary({Count} 件 / 並び {SortOrder} / 選択 {SelectedMediaId ?? "なし"})";
        }
    }
}
