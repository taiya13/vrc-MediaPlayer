using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Data;

namespace SmartMediaPlatform.Catalog.Udon
{
    /// <summary>
    /// Catalog Layer(Phase1-1)の Udon 互換 Adapter。
    ///
    /// 設計思想は Phase1-1 を維持する:
    ///  - カタログは読み取り専用・事前生成。他レイヤーを一切知らない。
    ///  - Recommendation Engine / Queue / Player は「このコンポーネントの検索 API」だけを使う。
    ///
    /// UdonSharp の制約への対応:
    ///  - interface 不可          → 具象 UdonSharpBehaviour として提供。
    ///  - ジェネリック Dictionary 不可 → ID 逆引きは VRChat の DataDictionary で代替。
    ///  - カスタムクラス配列不可     → MediaItem を返さず、並列配列 + index 返しにする。
    ///  - ジャグ配列を避ける        → タグ / 関連は CSR(平坦化配列 + オフセット)で保持。
    ///
    /// 検索アルゴリズムは純粋 C# の ParallelMediaCatalog(EditMode テスト済み)を移植したもの。
    /// データは UdonCatalogBaker が Phase1-1 の DummyCatalogSource から焼き込む。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonMediaCatalog : UdonSharpBehaviour
    {
        // MediaType の int 値。Phase1-1 の MediaType / MediaTypeCode と一致させること。
        public const int TypeUnknown = 0;
        public const int TypeMusic = 1;
        public const int TypeVideo = 2;
        public const int TypePodcast = 3;
        public const int TypeLive = 4;

        // === ベイク済みデータ(UdonCatalogBaker または Inspector が設定)===
        // すべて index 整列された並列配列。
        public string[] Ids;
        public string[] Titles;
        public string[] Artists;
        public string[] Genres;
        public int[] Types;            // MediaType の int 値
        public VRCUrl[] Urls;          // 実行時生成不可のため編集時に焼き込む
        public int[] Durations;

        // タグ(CSR): item i のタグ = TagValues[TagOffsets[i] .. TagOffsets[i + 1])
        public string[] TagValues;
        public int[] TagOffsets;       // 長さ = Count + 1

        // 関連 ID(CSR): item i の関連 = RelatedIds[RelatedOffsets[i] .. RelatedOffsets[i + 1])
        public string[] RelatedIds;
        public int[] RelatedOffsets;   // 長さ = Count + 1

        // === 実行時に構築する派生データ(シリアライズしない)===
        private bool _initialized;
        private string[] _idsLower;
        private string[] _titlesLower;
        private string[] _artistsLower;
        private string[] _genresLower;
        private string[] _tagValuesLower;
        private DataDictionary _idToIndex;   // 小文字 id -> index(Dictionary の代替)

        void Start()
        {
            EnsureInitialized();
        }

        /// <summary>
        /// 遅延初期化。他の UdonSharpBehaviour が Start 順序に関係なく
        /// 安全に呼べるよう、各 API の冒頭で必ず通す。
        /// </summary>
        public void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            _idsLower = LowerAll(Ids);
            _titlesLower = LowerAll(Titles);
            _artistsLower = LowerAll(Artists);
            _genresLower = LowerAll(Genres);
            _tagValuesLower = LowerAll(TagValues);

            _idToIndex = new DataDictionary();
            if (_idsLower != null)
            {
                for (int i = 0; i < _idsLower.Length; i++)
                {
                    // id は一意。上書きせず最初の登録を優先する。
                    if (!_idToIndex.ContainsKey(new DataToken(_idsLower[i])))
                    {
                        _idToIndex.SetValue(new DataToken(_idsLower[i]), new DataToken(i));
                    }
                }
            }
        }

        public int Count
        {
            get { return Ids != null ? Ids.Length : 0; }
        }

        // === ID 検索(DataDictionary 経由 / 線形フォールバック)===

        public int FindIndexById(string id)
        {
            if (id == null || id.Length == 0) return -1;
            EnsureInitialized();

            string q = id.ToLower();
            if (_idToIndex != null)
            {
                DataToken key = new DataToken(q);
                if (_idToIndex.TryGetValue(key, out DataToken value))
                {
                    return value.Int;
                }
                return -1;
            }

            // 万一 dict が無い場合の線形フォールバック
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

        // === 全件 ===

        public int[] GetAllIndices()
        {
            int n = Count;
            int[] result = new int[n];
            for (int i = 0; i < n; i++) result[i] = i;
            return result;
        }

        // === タイトル / アーティスト(部分一致・大小無視)===

        public int[] SearchByTitle(string query)
        {
            EnsureInitialized();
            return PartialSearch(_titlesLower, query);
        }

        public int[] SearchByArtist(string query)
        {
            EnsureInitialized();
            return PartialSearch(_artistsLower, query);
        }

        // === ジャンル(完全一致・大小無視)===

        public int[] SearchByGenre(string genre)
        {
            EnsureInitialized();
            return ExactSearch(_genresLower, genre);
        }

        // === タグ(完全一致・大小無視・CSR を走査)===

        public int[] SearchByTag(string tag)
        {
            if (IsBlank(tag)) return new int[0];
            EnsureInitialized();

            string q = tag.ToLower();
            int n = Count;
            int[] temp = new int[n];
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                int start = TagOffsets[i];
                int end = TagOffsets[i + 1];
                for (int t = start; t < end; t++)
                {
                    if (_tagValuesLower[t] == q)
                    {
                        temp[count] = i;
                        count++;
                        break;
                    }
                }
            }
            return Trim(temp, count);
        }

        // === 種別フィルタ ===

        public int[] FilterByType(int mediaType)
        {
            int n = Count;
            int[] temp = new int[n];
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                if (Types[i] == mediaType)
                {
                    temp[count] = i;
                    count++;
                }
            }
            return Trim(temp, count);
        }

        // === ランダム ===

        public int GetRandomIndex()
        {
            int n = Count;
            if (n == 0) return -1;
            return Random.Range(0, n);
        }

        public int[] GetRandomIndices(int count)
        {
            int n = Count;
            if (count <= 0 || n == 0) return new int[0];

            int[] pool = new int[n];
            for (int i = 0; i < n; i++) pool[i] = i;

            int take = count < n ? count : n;
            for (int i = 0; i < take; i++)
            {
                int j = i + Random.Range(0, n - i);
                int tmp = pool[i];
                pool[i] = pool[j];
                pool[j] = tmp;
            }

            int[] result = new int[take];
            for (int i = 0; i < take; i++) result[i] = pool[i];
            return result;
        }

        // === 関連 ===

        public int[] GetRelatedIndices(string id)
        {
            int origin = FindIndexById(id);
            if (origin < 0) return new int[0];

            int start = RelatedOffsets[origin];
            int end = RelatedOffsets[origin + 1];
            if (end <= start) return new int[0];

            int[] temp = new int[end - start];
            int count = 0;
            for (int r = start; r < end; r++)
            {
                int idx = FindIndexById(RelatedIds[r]);
                // 未知 ID・自己参照はスキップ(Phase1-1 と同一挙動)
                if (idx >= 0 && idx != origin)
                {
                    temp[count] = idx;
                    count++;
                }
            }
            return Trim(temp, count);
        }

        // === フィールドアクセサ(index 指定)===

        public string GetId(int index) { return Ids[index]; }
        public string GetTitle(int index) { return Titles[index]; }
        public string GetArtist(int index) { return Artists[index]; }
        public string GetGenre(int index) { return Genres[index]; }
        public int GetMediaType(int index) { return Types[index]; }
        public int GetDurationSeconds(int index) { return Durations[index]; }

        public VRCUrl GetUrl(int index)
        {
            if (Urls == null || index < 0 || index >= Urls.Length) return VRCUrl.Empty;
            return Urls[index];
        }

        public int GetTagCount(int index)
        {
            return TagOffsets[index + 1] - TagOffsets[index];
        }

        public string GetTag(int index, int tagIndex)
        {
            return TagValues[TagOffsets[index] + tagIndex];
        }

        // === 内部ヘルパ ===

        private int[] PartialSearch(string[] lowerField, string query)
        {
            if (IsBlank(query)) return new int[0];
            string q = query.ToLower();

            int[] temp = new int[lowerField.Length];
            int count = 0;
            for (int i = 0; i < lowerField.Length; i++)
            {
                if (lowerField[i].IndexOf(q) >= 0)
                {
                    temp[count] = i;
                    count++;
                }
            }
            return Trim(temp, count);
        }

        private int[] ExactSearch(string[] lowerField, string value)
        {
            if (IsBlank(value)) return new int[0];
            string q = value.ToLower();

            int[] temp = new int[lowerField.Length];
            int count = 0;
            for (int i = 0; i < lowerField.Length; i++)
            {
                if (lowerField[i] == q)
                {
                    temp[count] = i;
                    count++;
                }
            }
            return Trim(temp, count);
        }

        private int[] Trim(int[] temp, int count)
        {
            if (count == 0) return new int[0];
            if (count == temp.Length) return temp;
            int[] result = new int[count];
            for (int i = 0; i < count; i++) result[i] = temp[i];
            return result;
        }

        private string[] LowerAll(string[] src)
        {
            if (src == null) return new string[0];
            string[] result = new string[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                string s = src[i];
                result[i] = s == null ? "" : s.ToLower();
            }
            return result;
        }

        // UdonSharp では string.IsNullOrWhiteSpace / char.IsWhiteSpace が使えないことがあるため自前実装。
        private bool IsBlank(string s)
        {
            if (s == null || s.Length == 0) return true;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return false;
            }
            return true;
        }
    }
}
