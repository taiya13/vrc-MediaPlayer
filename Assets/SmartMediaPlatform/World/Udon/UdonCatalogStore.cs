using UdonSharp;
using UnityEngine;
using SmartMediaPlatform.Catalog.Udon;

namespace SmartMediaPlatform.World.Udon
{
    /// <summary>
    /// <b>表示用データの唯一の窓口。</b>Phase4-2 の <c>CatalogStore</c> の Udon 版。
    ///
    /// <b>ここが URL を出さないことに意味があります。</b>
    /// Phase4-2 で決めたとおり、画面に渡るデータ(<c>DisplayMeta</c> 相当)には
    /// URL の口がありません。URL を知ってよいのは
    /// <see cref="UdonVideoBackend"/> だけで、そこだけが
    /// <see cref="UdonMediaCatalog"/> を直接持ちます。
    /// この Store には <c>GetUrl</c> がないので、
    /// <b>UI が URL を触れないことがコードの形で保証されます</b>。
    ///
    /// UdonSharp はカスタムクラスを返せないので、
    /// <c>DisplayMeta</c> のかわりに <b>catalog index を渡して項目ごとに引く</b>形にしています
    /// (Phase1-2 の <see cref="UdonMediaCatalog"/> と同じやり方)。
    ///
    /// <b>Catalog Builder との互換</b>:このクラスはカタログの中身を一切持ちません。
    /// 焼き込み先は今までどおり <see cref="UdonMediaCatalog"/> なので、
    /// Catalog Builder が生成し直しても<b>ここは変わりません</b>。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonCatalogStore : UdonSharpBehaviour
    {
        [UnityEngine.Tooltip("参照するカタログ(Inspector で割り当て)")]
        public UdonMediaCatalog Catalog;

        [UnityEngine.Tooltip("この種別だけを一覧に出す。0(Unknown)なら絞り込まない")]
        public int OnlyMediaType = UdonMediaCatalog.TypeVideo;

        // 絞り込んだ結果(catalog index の並び)。EnsureInitialized で作る。
        private int[] _visible;
        private bool _initialized;

        void Start()
        {
            EnsureInitialized();
        }

        /// <summary>
        /// 遅延初期化。Start の順序に関係なく安全に呼べるよう、各 API の冒頭で必ず通す
        /// (<see cref="UdonMediaCatalog"/> と同じ作法)。
        /// </summary>
        public void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            if (Catalog == null)
            {
                _visible = new int[0];
                return;
            }

            Catalog.EnsureInitialized();

            if (OnlyMediaType == UdonMediaCatalog.TypeUnknown)
            {
                _visible = Catalog.GetAllIndices();
            }
            else
            {
                _visible = Catalog.FilterByType(OnlyMediaType);
            }
        }

        /// <summary>一覧に出す件数。</summary>
        public int Count
        {
            get
            {
                EnsureInitialized();
                return _visible.Length;
            }
        }

        /// <summary>一覧の <paramref name="position"/> 番目の catalog index。無ければ -1。</summary>
        public int GetIndexAt(int position)
        {
            EnsureInitialized();
            if (position < 0 || position >= _visible.Length) return -1;
            return _visible[position];
        }

        /// <summary>catalog index が一覧の何番目か。無ければ -1。</summary>
        public int GetPositionOf(int catalogIndex)
        {
            EnsureInitialized();
            for (int i = 0; i < _visible.Length; i++)
            {
                if (_visible[i] == catalogIndex) return i;
            }
            return -1;
        }

        // ───────── 表示用の項目(URL は無い)─────────

        public string GetId(int catalogIndex)
        {
            if (!IsValid(catalogIndex)) return "";
            return Catalog.GetId(catalogIndex);
        }

        public string GetTitle(int catalogIndex)
        {
            if (!IsValid(catalogIndex)) return "";
            return Catalog.GetTitle(catalogIndex);
        }

        public string GetArtist(int catalogIndex)
        {
            if (!IsValid(catalogIndex)) return "";
            return Catalog.GetArtist(catalogIndex);
        }

        public string GetGenre(int catalogIndex)
        {
            if (!IsValid(catalogIndex)) return "";
            return Catalog.GetGenre(catalogIndex);
        }

        public int GetMediaType(int catalogIndex)
        {
            if (!IsValid(catalogIndex)) return UdonMediaCatalog.TypeUnknown;
            return Catalog.GetMediaType(catalogIndex);
        }

        public int GetDurationSeconds(int catalogIndex)
        {
            if (!IsValid(catalogIndex)) return 0;
            return Catalog.GetDurationSeconds(catalogIndex);
        }

        /// <summary>「3:45」の形。<c>MediaLibraryFormatter.FormatDuration</c> と同じ見た目。</summary>
        public string FormatDuration(int catalogIndex)
        {
            int seconds = GetDurationSeconds(catalogIndex);
            if (seconds <= 0) return "--:--";

            int minutes = seconds / 60;
            int rest = seconds % 60;
            string tail = rest < 10 ? "0" + rest : "" + rest;
            return minutes + ":" + tail;
        }

        /// <summary>一覧の 1 行分の文字列。</summary>
        public string FormatRow(int catalogIndex)
        {
            if (!IsValid(catalogIndex)) return "";
            return GetTitle(catalogIndex) + "  /  " + GetArtist(catalogIndex)
                   + "  (" + FormatDuration(catalogIndex) + ")";
        }

        /// <summary>
        /// <b>検索に引っかかるか。</b>Phase7-2。
        /// <paramref name="lowerQuery"/> は<b>小文字にそろえて渡してください</b>
        /// (行ごとに小文字化すると、100 曲で 100 回無駄が出ます)。
        ///
        /// 見るのは<b>見出し・チャンネル・ジャンル・タグ</b>の 4 つです。
        /// ID は見ません — 人が覚えているものではないためです。
        /// </summary>
        public bool Matches(int catalogIndex, string lowerQuery)
        {
            if (lowerQuery == null || lowerQuery.Length == 0) return true;
            if (!IsValid(catalogIndex)) return false;

            if (ContainsLower(GetTitle(catalogIndex), lowerQuery)) return true;
            if (ContainsLower(GetArtist(catalogIndex), lowerQuery)) return true;
            if (ContainsLower(GetGenre(catalogIndex), lowerQuery)) return true;

            int tags = Catalog.GetTagCount(catalogIndex);
            for (int i = 0; i < tags; i++)
            {
                if (ContainsLower(Catalog.GetTag(catalogIndex, i), lowerQuery)) return true;
            }

            return false;
        }

        private bool ContainsLower(string haystack, string lowerNeedle)
        {
            if (haystack == null || haystack.Length == 0) return false;
            return haystack.ToLower().Contains(lowerNeedle);
        }

        // ───────── 関連 ─────────

        /// <summary>
        /// 関連の catalog index を返す。
        /// 中身は <see cref="UdonMediaCatalog"/> に焼き込まれた関連 ID なので、
        /// <b>サーバーから受け取る ID に差し替えても、ここは変わりません</b>
        /// (Phase4-2 で決めた形をそのまま保っています)。
        /// </summary>
        /// <summary>
        /// その曲の絵。無ければ <c>null</c>。Phase7。
        /// <b>URL は返しません。</b>Phase4-2 の約束どおり、
        /// この窓口から出るのは<b>見せてよいものだけ</b>です。
        /// </summary>
        public Sprite GetThumbnail(int catalogIndex)
        {
            if (Catalog == null) return null;
            return Catalog.GetThumbnail(catalogIndex);
        }

        /// <summary>絵が焼き込まれているか。枠を出すかどうかの判断に使う。</summary>
        public bool HasThumbnails()
        {
            return Catalog != null && Catalog.HasThumbnails;
        }

        public int[] GetRelatedIndices(int catalogIndex)
        {
            if (!IsValid(catalogIndex)) return new int[0];
            return Catalog.GetRelatedIndices(Catalog.GetId(catalogIndex));
        }

        private bool IsValid(int catalogIndex)
        {
            EnsureInitialized();
            return Catalog != null && catalogIndex >= 0 && catalogIndex < Catalog.Count;
        }
    }
}
