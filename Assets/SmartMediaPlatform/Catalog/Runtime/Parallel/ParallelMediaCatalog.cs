using System;

namespace SmartMediaPlatform.Catalog.Parallel
{
    /// <summary>
    /// 並列配列だけで Catalog API を提供する、純粋 C#(UnityEngine 非依存)の実装。
    ///
    /// 役割:
    ///  - UdonMediaCatalog(UdonSharp / 実機)が写し取るための「検索アルゴリズムの正典」。
    ///    UdonSharp はこの環境でコンパイルできないため、アルゴリズムの正しさはこのクラスへの
    ///    EditMode テスト(ParallelMediaCatalogTests)で担保し、Udon 側は同じ手順を移植する。
    ///  - interface も Dictionary もカスタムクラスの配列も使わない(すべて index 返し + 並列配列)。
    ///
    /// 検索の意味論は Phase1-1 の <see cref="IMediaCatalog"/> と一致:
    ///  - タイトル / アーティスト: 部分一致・大文字小文字無視、空クエリは 0 件
    ///  - タグ / ジャンル: 完全一致・大文字小文字無視
    ///  - 関連: 宣言順に解決し、自己参照・未知 ID はスキップ
    ///  - 戻り値は常にカタログ内 index の配列(該当なしは長さ 0)
    /// </summary>
    public sealed class ParallelMediaCatalog
    {
        private static readonly int[] EmptyIndices = new int[0];

        private readonly ParallelCatalogData _d;
        private readonly Random _random;

        // 検索を高速化するため、比較対象を小文字化して事前計算しておく
        private readonly string[] _idsLower;
        private readonly string[] _titlesLower;
        private readonly string[] _artistsLower;
        private readonly string[] _genresLower;
        private readonly string[] _tagValuesLower;

        public ParallelMediaCatalog(ParallelCatalogData data, Random random = null)
        {
            _d = data ?? throw new ArgumentNullException(nameof(data));
            _random = random ?? new Random();

            _idsLower = LowerAll(_d.Ids);
            _titlesLower = LowerAll(_d.Titles);
            _artistsLower = LowerAll(_d.Artists);
            _genresLower = LowerAll(_d.Genres);
            _tagValuesLower = LowerAll(_d.TagValues);
        }

        public int Count => _d.Count;

        // --- ID 検索 ---

        public int FindIndexById(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            string q = id.ToLowerInvariant();
            for (int i = 0; i < _idsLower.Length; i++)
            {
                if (_idsLower[i] == q) return i;
            }
            return -1;
        }

        public bool ContainsId(string id)
        {
            return FindIndexById(id) >= 0;
        }

        // --- 全件 ---

        public int[] GetAllIndices()
        {
            var result = new int[_d.Count];
            for (int i = 0; i < result.Length; i++) result[i] = i;
            return result;
        }

        // --- タイトル / アーティスト(部分一致) ---

        public int[] SearchByTitle(string query)
        {
            return PartialSearch(_titlesLower, query);
        }

        public int[] SearchByArtist(string query)
        {
            return PartialSearch(_artistsLower, query);
        }

        // --- ジャンル(完全一致) ---

        public int[] SearchByGenre(string genre)
        {
            return ExactSearch(_genresLower, genre);
        }

        // --- タグ(完全一致・CSR を走査) ---

        public int[] SearchByTag(string tag)
        {
            if (IsNullOrWhiteSpace(tag)) return EmptyIndices;
            string q = tag.ToLowerInvariant();

            var temp = new int[_d.Count];
            int count = 0;
            for (int i = 0; i < _d.Count; i++)
            {
                int start = _d.TagOffsets[i];
                int end = _d.TagOffsets[i + 1];
                for (int t = start; t < end; t++)
                {
                    if (_tagValuesLower[t] == q)
                    {
                        temp[count++] = i;
                        break;
                    }
                }
            }
            return Trim(temp, count);
        }

        // --- 種別フィルタ ---

        public int[] FilterByType(int mediaType)
        {
            var temp = new int[_d.Count];
            int count = 0;
            for (int i = 0; i < _d.Count; i++)
            {
                if (_d.Types[i] == mediaType) temp[count++] = i;
            }
            return Trim(temp, count);
        }

        // --- ランダム ---

        public int GetRandomIndex()
        {
            if (_d.Count == 0) return -1;
            return _random.Next(_d.Count);
        }

        public int[] GetRandomIndices(int count)
        {
            if (count <= 0 || _d.Count == 0) return EmptyIndices;

            int n = _d.Count;
            var pool = new int[n];
            for (int i = 0; i < n; i++) pool[i] = i;

            int take = count < n ? count : n;
            for (int i = 0; i < take; i++)
            {
                int j = i + _random.Next(n - i);
                int tmp = pool[i];
                pool[i] = pool[j];
                pool[j] = tmp;
            }

            var result = new int[take];
            for (int i = 0; i < take; i++) result[i] = pool[i];
            return result;
        }

        // --- 関連 ---

        public int[] GetRelatedIndices(string id)
        {
            int origin = FindIndexById(id);
            if (origin < 0) return EmptyIndices;

            int start = _d.RelatedOffsets[origin];
            int end = _d.RelatedOffsets[origin + 1];
            if (end <= start) return EmptyIndices;

            var temp = new int[end - start];
            int count = 0;
            for (int r = start; r < end; r++)
            {
                string relatedId = _d.RelatedIds[r];
                int idx = FindIndexById(relatedId);
                // 未知 ID と自己参照はスキップ(Phase1-1 と同一挙動)
                if (idx >= 0 && idx != origin) temp[count++] = idx;
            }
            return Trim(temp, count);
        }

        // --- フィールドアクセサ(index 指定) ---

        public string GetId(int index) => _d.Ids[index];
        public string GetTitle(int index) => _d.Titles[index];
        public string GetArtist(int index) => _d.Artists[index];
        public string GetGenre(int index) => _d.Genres[index];
        public int GetMediaType(int index) => _d.Types[index];
        public string GetUrl(int index) => _d.Urls[index];
        public int GetDurationSeconds(int index) => _d.Durations[index];

        public string[] GetTags(int index)
        {
            int start = _d.TagOffsets[index];
            int end = _d.TagOffsets[index + 1];
            var result = new string[end - start];
            for (int i = 0; i < result.Length; i++) result[i] = _d.TagValues[start + i];
            return result;
        }

        // --- 内部ヘルパ ---

        private int[] PartialSearch(string[] lowerField, string query)
        {
            if (IsNullOrWhiteSpace(query)) return EmptyIndices;
            string q = query.ToLowerInvariant();

            var temp = new int[lowerField.Length];
            int count = 0;
            for (int i = 0; i < lowerField.Length; i++)
            {
                if (lowerField[i].IndexOf(q, StringComparison.Ordinal) >= 0) temp[count++] = i;
            }
            return Trim(temp, count);
        }

        private int[] ExactSearch(string[] lowerField, string value)
        {
            if (IsNullOrWhiteSpace(value)) return EmptyIndices;
            string q = value.ToLowerInvariant();

            var temp = new int[lowerField.Length];
            int count = 0;
            for (int i = 0; i < lowerField.Length; i++)
            {
                if (lowerField[i] == q) temp[count++] = i;
            }
            return Trim(temp, count);
        }

        private static int[] Trim(int[] temp, int count)
        {
            if (count == 0) return EmptyIndices;
            if (count == temp.Length) return temp;
            var result = new int[count];
            for (int i = 0; i < count; i++) result[i] = temp[i];
            return result;
        }

        private static string[] LowerAll(string[] src)
        {
            var result = new string[src.Length];
            for (int i = 0; i < src.Length; i++)
                result[i] = (src[i] ?? "").ToLowerInvariant();
            return result;
        }

        // UdonSharp には string.IsNullOrWhiteSpace が無い場合があるため、
        // Udon 側と同じ判定を明示実装しておく(挙動を揃える)。
        private static bool IsNullOrWhiteSpace(string s)
        {
            if (s == null) return true;
            for (int i = 0; i < s.Length; i++)
            {
                if (!char.IsWhiteSpace(s[i])) return false;
            }
            return true;
        }
    }
}
