using UdonSharp;
using UnityEngine;
using SmartMediaPlatform.Catalog.Udon;

namespace SmartMediaPlatform.World.Udon
{
    /// <summary>
    /// <b>その人の好みを覚えておく場所。</b>Phase7-8。
    ///
    /// <b>お気に入り・履歴・再生回数・スキップ回数</b>を持ちます。
    /// 「次に何を流すか」は決めません —— <b>数えるだけ</b>です。
    /// 決めるのは今までどおり <c>UdonRecommendationEngine</c> と
    /// <c>UdonPlayerSession</c> の仕事で、ここはその材料を出す係です。
    ///
    /// <b>なぜ 1 か所にまとめたのか</b><br/>
    /// 好みの記録は、置き場所を間違えると<b>あちこちに散ります</b>。
    /// お気に入りは UI が、履歴は Session が、再生回数は Backend が —— と
    /// 持ち始めると、おすすめを強くしたいときに<b>全部を触る</b>ことになります。
    /// 数える所を 1 つにしておけば、
    /// <b>おすすめ側は「ここに聞く」だけ</b>で済みます。
    ///
    /// <b>同期しません。</b>好みは人それぞれで、
    /// <b>他人の履歴が自分のおすすめに混ざるほうが困ります</b>。
    ///
    /// <b>ワールドを出ると消えます。</b>いまは覚えているだけで、保存はしません。
    /// 保存を足すときも、書き込む場所はこのクラスの中だけで済みます
    /// (<see cref="NotePlayed"/> / <see cref="ToggleFavorite"/> の 2 か所)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonUserProfile : UdonSharpBehaviour
    {
        [Tooltip("曲の情報を引くカタログ")]
        public UdonMediaCatalog Catalog;

        /// <summary>お気に入りに入れられる上限。</summary>
        public const int MaxFavorites = 128;

        /// <summary>覚えておく履歴の長さ。</summary>
        public const int MaxHistory = 64;

        /// <summary>お気に入りの並び:追加が新しい順。</summary>
        public const int SortNewest = 0;

        /// <summary>お気に入りの並び:追加が古い順(入れた順)。</summary>
        public const int SortOldest = 1;

        /// <summary>お気に入りの並び:アーティスト順。</summary>
        public const int SortArtist = 2;

        [Header("並び")]
        [Tooltip("0 = 追加が新しい順 / 1 = 追加が古い順 / 2 = アーティスト順")]
        [Range(0, 2)]
        public int FavoriteSort = SortNewest;

        // 追加が新しい順。[0] がいちばん新しい。
        private int[] _favorites;
        private int _favoriteCount;

        // アーティスト順に並べ替えた結果。要るときだけ組み直す。
        private int[] _sorted;
        private int _sortedFor = -1;

        // 直近が [0]。同じ曲を続けて聴いても 1 つしか積まない。
        private int[] _history;
        private int _historyCount;

        private int[] _playCount;
        private int[] _skipCount;

        private bool _initialized;

        /// <summary>中身が変わった回数。UI が「組み直すか」を決めるのに使う。</summary>
        public int Revision;

        void Start()
        {
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            _favorites = new int[MaxFavorites];
            _sorted = new int[MaxFavorites];
            _history = new int[MaxHistory];

            int n = Catalog != null ? Catalog.Count : 0;
            if (n < 1) n = 1;

            _playCount = new int[n];
            _skipCount = new int[n];
        }

        // ───────── お気に入り ─────────

        public int FavoriteCount
        {
            get { EnsureInitialized(); return _favoriteCount; }
        }

        public bool IsFavorite(int catalogIndex)
        {
            EnsureInitialized();
            return PositionInFavorites(catalogIndex) >= 0;
        }

        /// <summary>
        /// お気に入りを入れる / 外す。
        /// <b>入れたら前に置きます</b>(追加が新しい順が既定の並びなので)。
        /// </summary>
        /// <returns>入れたら true、外したら false。</returns>
        public bool ToggleFavorite(int catalogIndex)
        {
            EnsureInitialized();
            if (catalogIndex < 0) return false;

            int at = PositionInFavorites(catalogIndex);

            if (at >= 0)
            {
                for (int i = at; i < _favoriteCount - 1; i++) _favorites[i] = _favorites[i + 1];
                _favoriteCount--;
                Touch();
                return false;
            }

            if (_favoriteCount >= MaxFavorites)
            {
                // いっぱいなら、いちばん古いものを 1 つ落とす。
                _favoriteCount = MaxFavorites - 1;
            }

            for (int i = _favoriteCount; i > 0; i--) _favorites[i] = _favorites[i - 1];
            _favorites[0] = catalogIndex;
            _favoriteCount++;

            Touch();
            return true;
        }

        /// <summary>いまの並びで <paramref name="position"/> 番目。</summary>
        public int GetFavoriteAt(int position)
        {
            EnsureInitialized();
            if (position < 0 || position >= _favoriteCount) return -1;

            if (FavoriteSort == SortOldest)
            {
                return _favorites[_favoriteCount - 1 - position];
            }

            if (FavoriteSort == SortArtist)
            {
                EnsureSorted();
                return _sorted[position];
            }

            return _favorites[position];
        }

        /// <summary>並びを次のものへ送る。ボタンからそのまま呼べます。</summary>
        public void CycleFavoriteSort()
        {
            FavoriteSort++;
            if (FavoriteSort > SortArtist) FavoriteSort = SortNewest;

            _sortedFor = -1;
            Touch();
        }

        /// <summary>いまの並びの名前。</summary>
        public string FavoriteSortLabel()
        {
            if (FavoriteSort == SortOldest) return "追加が古い順";
            if (FavoriteSort == SortArtist) return "アーティスト順";
            return "追加が新しい順";
        }

        private int PositionInFavorites(int catalogIndex)
        {
            for (int i = 0; i < _favoriteCount; i++)
            {
                if (_favorites[i] == catalogIndex) return i;
            }
            return -1;
        }

        /// <summary>
        /// アーティスト順の並びを作る。
        /// <b>中身が変わったときだけ</b>組み直します(毎回並べ替えると重い)。
        /// </summary>
        private void EnsureSorted()
        {
            if (_sortedFor == Revision) return;
            _sortedFor = Revision;

            for (int i = 0; i < _favoriteCount; i++) _sorted[i] = _favorites[i];

            // 挿入ソート。お気に入りは多くても 128 件なので、これで十分です。
            for (int i = 1; i < _favoriteCount; i++)
            {
                int value = _sorted[i];
                int j = i - 1;

                while (j >= 0 && CompareArtist(_sorted[j], value) > 0)
                {
                    _sorted[j + 1] = _sorted[j];
                    j--;
                }
                _sorted[j + 1] = value;
            }
        }

        /// <summary>
        /// アーティスト名を比べる。
        /// <b>自前で 1 文字ずつ比べます</b> —— Udon で使える文字列比較は
        /// 版によって差があり、当てにすると壊れやすいためです。
        /// </summary>
        private int CompareArtist(int a, int b)
        {
            if (Catalog == null) return 0;

            string left = Catalog.GetArtist(a);
            string right = Catalog.GetArtist(b);

            if (left == null) left = "";
            if (right == null) right = "";

            int shortest = left.Length < right.Length ? left.Length : right.Length;

            for (int i = 0; i < shortest; i++)
            {
                int lc = left[i];
                int rc = right[i];
                if (lc != rc) return lc < rc ? -1 : 1;
            }

            if (left.Length == right.Length) return a < b ? -1 : 1;
            return left.Length < right.Length ? -1 : 1;
        }

        // ───────── 履歴 ─────────

        public int HistoryCount
        {
            get { EnsureInitialized(); return _historyCount; }
        }

        /// <summary>直近が 0 番目。</summary>
        public int GetHistoryAt(int position)
        {
            EnsureInitialized();
            if (position < 0 || position >= _historyCount) return -1;
            return _history[position];
        }

        /// <summary>
        /// 曲が鳴り始めた。<b>ここが履歴と再生回数の唯一の入口</b>です。
        /// </summary>
        public void NotePlayed(int catalogIndex)
        {
            EnsureInitialized();
            if (catalogIndex < 0) return;

            CountUp(_playCount, catalogIndex);

            // 同じ曲が続けて先頭に並ばないようにする。
            // 一時停止して再開しただけで履歴が埋まると、履歴として役に立ちません。
            if (_historyCount > 0 && _history[0] == catalogIndex)
            {
                Touch();
                return;
            }

            // すでにどこかにいるなら、そこから抜いてから前に置く。
            int at = -1;
            for (int i = 0; i < _historyCount; i++)
            {
                if (_history[i] == catalogIndex) { at = i; break; }
            }

            if (at >= 0)
            {
                for (int i = at; i < _historyCount - 1; i++) _history[i] = _history[i + 1];
                _historyCount--;
            }
            else if (_historyCount >= MaxHistory)
            {
                _historyCount = MaxHistory - 1;
            }

            for (int i = _historyCount; i > 0; i--) _history[i] = _history[i - 1];
            _history[0] = catalogIndex;
            _historyCount++;

            Touch();
        }

        /// <summary>最後まで聴かずに飛ばされた。いまは数えるだけです。</summary>
        public void NoteSkipped(int catalogIndex)
        {
            EnsureInitialized();
            CountUp(_skipCount, catalogIndex);
        }

        /// <summary>
        /// <b>何曲前に聴いたか。</b>0 なら直前、-1 なら履歴に無い。
        /// おすすめが「さっき聴いた曲」を下げるのに使います。
        /// </summary>
        public int RecentRankOf(int catalogIndex)
        {
            EnsureInitialized();

            for (int i = 0; i < _historyCount; i++)
            {
                if (_history[i] == catalogIndex) return i;
            }
            return -1;
        }

        public int PlayCountOf(int catalogIndex)
        {
            EnsureInitialized();
            if (_playCount == null || catalogIndex < 0 || catalogIndex >= _playCount.Length) return 0;
            return _playCount[catalogIndex];
        }

        public int SkipCountOf(int catalogIndex)
        {
            EnsureInitialized();
            if (_skipCount == null || catalogIndex < 0 || catalogIndex >= _skipCount.Length) return 0;
            return _skipCount[catalogIndex];
        }

        /// <summary>
        /// <b>いちばん多く聴かれた回数。</b>人気度を 0〜1 に均すのに使います。
        /// </summary>
        public int MaxPlayCount()
        {
            EnsureInitialized();
            if (_playCount == null) return 0;

            int max = 0;
            for (int i = 0; i < _playCount.Length; i++)
            {
                if (_playCount[i] > max) max = _playCount[i];
            }
            return max;
        }

        // ───────── おすすめが使う手掛かり ─────────

        /// <summary>
        /// <b>お気に入りに入っているアーティストか。</b>
        /// 「この人が好きなら」を作るための手掛かりです。
        /// <paramref name="artistLower"/> は<b>呼び出し側が小文字化して</b>渡します。
        /// </summary>
        public bool IsFavoriteArtist(string artistLower)
        {
            EnsureInitialized();
            if (Catalog == null || artistLower == null || artistLower.Length == 0) return false;

            for (int i = 0; i < _favoriteCount; i++)
            {
                string other = Catalog.GetArtist(_favorites[i]);
                if (other != null && other.ToLower() == artistLower) return true;
            }
            return false;
        }

        /// <summary>お気に入りに入っているジャンルか。</summary>
        public bool IsFavoriteGenre(string genreLower)
        {
            EnsureInitialized();
            if (Catalog == null || genreLower == null || genreLower.Length == 0) return false;

            for (int i = 0; i < _favoriteCount; i++)
            {
                string other = Catalog.GetGenre(_favorites[i]);
                if (other != null && other.ToLower() == genreLower) return true;
            }
            return false;
        }

        /// <summary>Console 表示用の 1 行。</summary>
        public string Describe()
        {
            EnsureInitialized();
            return "お気に入り " + _favoriteCount + " 曲 / 履歴 " + _historyCount + " 曲";
        }

        private void CountUp(int[] table, int index)
        {
            if (table == null || index < 0 || index >= table.Length) return;
            table[index] = table[index] + 1;
        }

        private void Touch()
        {
            Revision++;
        }
    }
}
