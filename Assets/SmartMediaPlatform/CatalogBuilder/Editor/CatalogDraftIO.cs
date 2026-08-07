#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Assets;
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.CatalogBuilder.EditorTools
{
    /// <summary>
    /// <b>編集中のカタログとアセットの行き来。</b>Phase6-1。
    ///
    /// <b>ここが Catalog Builder と再生側の唯一の接点です。</b>
    /// 書き込み先は <see cref="MediaCatalogAsset"/> 1 つだけで、
    /// <c>MediaPlayer</c> 本体には手を触れません。
    /// <see cref="MediaCatalogAsset.SetEntries"/> は Phase4 から
    /// 「Catalog Builder の書き込み口」として空けてあったものです。
    ///
    /// <b>VRCUrlTable の生成はまだ行いません。</b>
    /// アセットに URL を文字列で残すところまでが Phase6-1 で、
    /// <c>VRCUrl</c> へ焼くのは今までどおり <c>UdonCatalogBaker</c> の仕事です
    /// (窓から呼べるようにするのは Phase6-3 以降)。
    /// </summary>
    public static class CatalogDraftIO
    {
        // あなたが作るカタログは「更新で消えない側」に置く(Phase7-3 で移動)。
        // SmartMediaPlatform の中に保存すると、更新(フォルダ入れ替え)のたびに消えます。
        private const string DefaultFolder = "Assets/SmartMediaPlatform_Data/Catalogs";
        private const string DefaultName = "MediaCatalog.asset";

        // ───────── 読む ─────────

        /// <summary>
        /// アセットの中身を編集用へ写す。
        ///
        /// <b>横の覚え書きも一緒に読みます</b>(Phase6-5)。
        /// <c>MediaItem</c> にはサムネイル・公開日・出どころが無いので、
        /// これが無いと保存して読み直すたびに消えていました。
        /// </summary>
        public static bool Load(MediaCatalogAsset asset, CatalogDraft draft)
        {
            return Load(asset, draft, null);
        }

        /// <summary><paramref name="book"/> を渡すと、見張っている取り込み元も戻します。</summary>
        public static bool Load(
            MediaCatalogAsset asset, CatalogDraft draft, CatalogSubscriptionBook book)
        {
            if (asset == null || draft == null) return false;

            IReadOnlyList<MediaItem> items = asset.LoadItems();
            draft.LoadFrom(items);
            draft.SourceDescription = asset.SourceDescription;

            CatalogSidecar sidecar = CatalogSidecarIO.Load(asset);
            sidecar.ApplyTo(draft.Items, book);

            return true;
        }

        // ───────── 書く ─────────

        /// <summary>
        /// 編集中の中身をアセットへ書き戻す。
        /// <b>完成しているものだけ</b>が書かれます(作りかけは落ちる)。
        /// </summary>
        /// <returns>書いた件数。書けなければ -1。</returns>
        public static int Save(MediaCatalogAsset asset, CatalogDraft draft)
        {
            return Save(asset, draft, null);
        }

        /// <summary><paramref name="book"/> を渡すと、見張っている取り込み元も書き残します。</summary>
        public static int Save(
            MediaCatalogAsset asset, CatalogDraft draft, CatalogSubscriptionBook book)
        {
            if (asset == null || draft == null) return -1;

            MediaItem[] items = draft.ToMediaItems();
            var entries = new List<MediaCatalogAsset.Entry>();

            for (int i = 0; i < items.Length; i++)
            {
                entries.Add(ToEntry(items[i]));
            }

            asset.SetEntries(entries, draft.SourceDescription);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            // 再生に要らないものはアセットに入れず、横の JSON へ(Phase6-5)。
            // ワールドの容量を増やさずに、編集の続きができる。
            CatalogSidecarIO.Save(asset, CatalogSidecar.CaptureFrom(draft.Items, book));

            return entries.Count;
        }

        private static MediaCatalogAsset.Entry ToEntry(MediaItem item)
        {
            var entry = new MediaCatalogAsset.Entry();
            entry.Id = item.Id;
            entry.Title = item.Title;
            entry.Artist = item.Artist;
            entry.Genre = item.Genre;
            entry.Type = item.Type;
            entry.Url = item.Url;
            entry.DurationSeconds = item.DurationSeconds;
            entry.Tags = ToArray(item.Tags);
            entry.RelatedIds = ToArray(item.RelatedIds);
            entry.ViewCount = item.ViewCount;
            return entry;
        }

        private static string[] ToArray(IReadOnlyList<string> values)
        {
            if (values == null) return new string[0];

            var result = new string[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = values[i];
            return result;
        }

        // ───────── 作る ─────────

        /// <summary>
        /// 新しいアセットを作る。保存先はダイアログで選ばせます。
        /// 取り消されたら null。
        /// </summary>
        public static MediaCatalogAsset CreateNew()
        {
            Directory.CreateDirectory(DefaultFolder);

            string path = EditorUtility.SaveFilePanelInProject(
                "カタログを新しく作る", DefaultName, "asset",
                "どこに保存しますか", DefaultFolder);

            if (string.IsNullOrEmpty(path)) return null;

            var asset = ScriptableObject.CreateInstance<MediaCatalogAsset>();
            asset.SetEntries(new List<MediaCatalogAsset.Entry>(), "Catalog Builder");

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return asset;
        }

        /// <summary>プロジェクトにあるカタログを全部探す。</summary>
        public static MediaCatalogAsset[] FindAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(MediaCatalogAsset));
            var found = new List<MediaCatalogAsset>();

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<MediaCatalogAsset>(path);
                if (asset != null) found.Add(asset);
            }

            return found.ToArray();
        }
    }
}
#endif
