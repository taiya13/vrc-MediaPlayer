using System;
using System.Collections.Generic;

namespace SmartMediaPlatform.CatalogBuilder
{
    /// <summary>
    /// <b>編集のためだけの覚え書き 1 件ぶん。</b>Phase6-5。
    ///
    /// <see cref="Catalog.MediaItem"/> は<b>再生に要るものしか持ちません</b>。
    /// サムネイル・公開日・出どころは<b>再生に要らないので入っていません</b>。
    /// ここに書き出しておいて、読み直したときに戻します。
    /// </summary>
    [Serializable]
    public sealed class CatalogItemMeta
    {
        public string Id = "";
        public string Source = "";
        public string ThumbnailPath = "";
        public string PublishedAt = "";
    }

    /// <summary>
    /// <b>Catalog.asset の横に置く覚え書き。</b>Phase6-5。
    ///
    /// <b>なぜアセットに入れないのか</b><br/>
    /// <c>Catalog.asset</c> は<b>そのままワールドに乗ります</b>。
    /// サムネイルの URL や「いつ取り込んだか」を入れると、
    /// <b>再生に要らないものをアップロードすることになります</b>。
    /// 編集のための情報はここへ分けて、JSON で横に置きます。
    ///
    /// <b>これで直った不具合(Phase6-4 まで)</b><br/>
    /// 保存して読み直すと<b>サムネイルと出どころが消えていました</b>。
    /// <c>MediaItem</c> を通ると落ちるためです。ここに残すので消えません。
    ///
    /// <b>Unity に依存しません。</b>読み書きは <c>CatalogSidecarIO</c>(Editor)の仕事です。
    /// </summary>
    [Serializable]
    public sealed class CatalogSidecar
    {
        /// <summary>形が変わったときに読み分けるための番号。</summary>
        public int Version = 1;

        public CatalogSubscription[] Subscriptions = new CatalogSubscription[0];

        public CatalogItemMeta[] Items = new CatalogItemMeta[0];

        /// <summary>いまの編集内容から作る。</summary>
        public static CatalogSidecar CaptureFrom(
            IReadOnlyList<CatalogDraftItem> items, CatalogSubscriptionBook book)
        {
            var sidecar = new CatalogSidecar();

            var metas = new List<CatalogItemMeta>();
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    CatalogDraftItem item = items[i];
                    if (item == null) continue;
                    if (string.IsNullOrWhiteSpace(item.Id)) continue;

                    // 何も持っていない行は書かない。手入力だけのカタログなら
                    // 覚え書きそのものが空になり、余計なファイルが増えない。
                    if (!item.HasEditorMetadata) continue;

                    var meta = new CatalogItemMeta();
                    meta.Id = item.Id.Trim();
                    meta.Source = item.Source;
                    meta.ThumbnailPath = item.ThumbnailPath;
                    meta.PublishedAt = item.PublishedAt;

                    metas.Add(meta);
                }
            }
            sidecar.Items = metas.ToArray();

            if (book != null)
            {
                var subs = new List<CatalogSubscription>();
                for (int i = 0; i < book.Count; i++) subs.Add(book.GetAt(i));
                sidecar.Subscriptions = subs.ToArray();
            }

            return sidecar;
        }

        /// <summary>
        /// 読み込んだ覚え書きを編集内容へ戻す。
        /// <b>ID で突き合わせます</b>ので、並べ替えても順番が変わっても正しく戻ります。
        /// </summary>
        /// <returns>戻せた件数。</returns>
        public int ApplyTo(IReadOnlyList<CatalogDraftItem> items, CatalogSubscriptionBook book)
        {
            if (book != null)
            {
                book.Clear();
                if (Subscriptions != null)
                {
                    for (int i = 0; i < Subscriptions.Length; i++) book.Add(Subscriptions[i]);
                }
            }

            if (items == null || Items == null) return 0;

            int restored = 0;
            for (int i = 0; i < items.Count; i++)
            {
                CatalogDraftItem item = items[i];
                if (item == null || string.IsNullOrWhiteSpace(item.Id)) continue;

                CatalogItemMeta meta = FindMeta(item.Id);
                if (meta == null) continue;

                if (!string.IsNullOrWhiteSpace(meta.Source)) item.Source = meta.Source;
                if (!string.IsNullOrWhiteSpace(meta.ThumbnailPath)) item.ThumbnailPath = meta.ThumbnailPath;
                if (!string.IsNullOrWhiteSpace(meta.PublishedAt)) item.PublishedAt = meta.PublishedAt;

                restored++;
            }
            return restored;
        }

        private CatalogItemMeta FindMeta(string id)
        {
            string wanted = id.Trim();

            for (int i = 0; i < Items.Length; i++)
            {
                if (Items[i] != null && Items[i].Id == wanted) return Items[i];
            }
            return null;
        }
    }
}
