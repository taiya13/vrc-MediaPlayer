namespace SmartMediaPlatform.Catalog.Parallel
{
    /// <summary>
    /// カタログを「並列配列だけ」で表現した、UdonSharp 互換の中間フォーマット。
    /// interface / Dictionary / ジャグ配列 / カスタムクラスの配列を一切使わない。
    ///
    /// 可変長フィールド(タグ・関連 ID)は CSR 形式で平坦化する:
    ///   item i のタグ = TagValues[TagOffsets[i] .. TagOffsets[i + 1])
    /// これによりジャグ配列(string[][])を避けつつ item ごとの可変長を表現できる。
    ///
    /// このクラス自体は純粋 C#(UnityEngine 非依存)。同じ配列レイアウトを
    /// <see cref="ParallelMediaCatalog"/>(テスト対象)と UdonMediaCatalog(実機)が共有する。
    /// </summary>
    public sealed class ParallelCatalogData
    {
        public string[] Ids;
        public string[] Titles;
        public string[] Artists;
        public string[] Genres;

        /// <summary>MediaType の int 値(<see cref="MediaTypeCode"/>)。</summary>
        public int[] Types;

        /// <summary>URL は文字列で保持。実機では編集時に VRCUrl[] へ焼き込む。</summary>
        public string[] Urls;

        public int[] Durations;

        // --- タグ(CSR) ---
        public string[] TagValues;
        public int[] TagOffsets;      // 長さ = Count + 1

        // --- 関連 ID(CSR) ---
        public string[] RelatedIds;
        public int[] RelatedOffsets;  // 長さ = Count + 1

        public int Count => Ids != null ? Ids.Length : 0;
    }
}
