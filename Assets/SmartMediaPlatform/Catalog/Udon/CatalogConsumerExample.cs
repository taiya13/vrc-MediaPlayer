using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace SmartMediaPlatform.Catalog.Udon
{
    /// <summary>
    /// Phase1-2 の受け入れ確認用サンプル。将来の Recommendation Engine が
    /// UdonMediaCatalog を「検索 API だけ」でどう使うかを示す(＝完成品の推薦エンジンではない)。
    ///
    /// 依存方向は Consumer → Catalog の一方向のみ。Catalog はこのクラスを知らない。
    /// Play すると Console に一連の検索結果と「次の候補」を出力する(Console 動作確認)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CatalogConsumerExample : UdonSharpBehaviour
    {
        [Tooltip("参照するカタログ(Inspector で割り当て)")]
        public UdonMediaCatalog Catalog;

        [Tooltip("推薦の起点にする ID")]
        public string SeedId = "music-001";

        [Tooltip("次の候補として何件返すか")]
        public int RecommendationCount = 3;

        void Start()
        {
            if (Catalog == null)
            {
                Debug.LogError("[CatalogConsumerExample] Catalog が未設定です。");
                return;
            }

            Debug.Log("=== Smart Media Platform / Udon Catalog Demo (items: " + Catalog.Count + ") ===");

            DumpAll();
            DumpSearch();
            DumpRecommendation();
        }

        private void DumpAll()
        {
            int[] all = Catalog.GetAllIndices();
            Debug.Log("GetAllIndices() -> " + all.Length + " 件");
            for (int i = 0; i < all.Length; i++)
            {
                Debug.Log("    " + Describe(all[i]));
            }
        }

        private void DumpSearch()
        {
            LogIndices("SearchByTitle(\"neon\")", Catalog.SearchByTitle("neon"));
            LogIndices("SearchByArtist(\"kohaku\")", Catalog.SearchByArtist("kohaku"));
            LogIndices("SearchByTag(\"night\")", Catalog.SearchByTag("night"));
            LogIndices("SearchByGenre(\"Jazz\")", Catalog.SearchByGenre("Jazz"));
            LogIndices("FilterByType(Video)", Catalog.FilterByType(UdonMediaCatalog.TypeVideo));

            int idx = Catalog.FindIndexById(SeedId);
            Debug.Log("FindIndexById(\"" + SeedId + "\") -> " + (idx >= 0 ? Describe(idx) : "(not found)"));

            int rnd = Catalog.GetRandomIndex();
            Debug.Log("GetRandomIndex() -> " + (rnd >= 0 ? Describe(rnd) : "(empty)"));
            LogIndices("GetRandomIndices(3)", Catalog.GetRandomIndices(3));
        }

        /// <summary>
        /// 「次に何を再生するか」を Catalog API だけで組み立てる最小推薦ロジック。
        /// 関連アイテム → 同ジャンル → ランダム、の順に候補を集める。
        /// </summary>
        private void DumpRecommendation()
        {
            int seed = Catalog.FindIndexById(SeedId);
            if (seed < 0)
            {
                Debug.LogWarning("[CatalogConsumerExample] Seed ID が見つかりません: " + SeedId);
                return;
            }

            int want = RecommendationCount;
            int[] picks = new int[want];
            int picked = 0;

            picked = AddCandidates(picks, picked, Catalog.GetRelatedIndices(SeedId), seed);
            if (picked < want)
                picked = AddCandidates(picks, picked, Catalog.SearchByGenre(Catalog.GetGenre(seed)), seed);
            if (picked < want)
                picked = AddCandidates(picks, picked, Catalog.GetRandomIndices(Catalog.Count), seed);

            Debug.Log("Recommendation from \"" + Catalog.GetTitle(seed) + "\" -> " + picked + " 件");
            for (int i = 0; i < picked; i++)
            {
                Debug.Log("    next: " + Describe(picks[i]));
            }
        }

        // 候補を重複なしで詰める(seed 自身は除外)
        private int AddCandidates(int[] picks, int picked, int[] source, int seed)
        {
            for (int i = 0; i < source.Length && picked < picks.Length; i++)
            {
                int idx = source[i];
                if (idx == seed) continue;

                bool dup = false;
                for (int k = 0; k < picked; k++)
                {
                    if (picks[k] == idx) { dup = true; break; }
                }
                if (dup) continue;

                picks[picked] = idx;
                picked++;
            }
            return picked;
        }

        private void LogIndices(string label, int[] indices)
        {
            Debug.Log(label + " -> " + indices.Length + " 件");
            for (int i = 0; i < indices.Length; i++)
            {
                Debug.Log("    " + Describe(indices[i]));
            }
        }

        private string Describe(int index)
        {
            VRCUrl url = Catalog.GetUrl(index);
            string urlText = url != null ? url.Get() : "";
            return "[" + Catalog.GetId(index) + "] "
                   + Catalog.GetTitle(index) + " / " + Catalog.GetArtist(index)
                   + " (type=" + Catalog.GetMediaType(index) + ", " + Catalog.GetGenre(index)
                   + ", url=" + urlText + ")";
        }
    }
}
