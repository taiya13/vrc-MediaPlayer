using System;
using System.Collections.Generic;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Recommendation
{
    /// <summary>
    /// ルールベースのおすすめエンジン(AI 非使用)。
    ///
    /// 依存は <see cref="IMediaCatalog"/>(Catalog API)のみ。
    /// Player / Queue / UI / ネットワークを一切参照しない
    /// (アセンブリ参照レベルでも SmartMediaPlatform.Catalog だけに制限している)。
    ///
    /// スコアリング:各候補について、有効なルールの寄与(重み × 素の信号)を合計する。
    ///  - Related     : 候補が seed の RelatedIds に含まれる → 重み
    ///  - SameArtist  : アーティスト一致 → 重み
    ///  - SameGenre   : ジャンル一致 → 重み
    ///  - TagMatch    : 共有タグ数 × 重み
    ///  - Random      : [0, 重み) の乱数(同点崩し)
    /// 並び順は「スコア降順 → カタログ登録順(index 昇順)」で決定的。
    /// </summary>
    public sealed class RecommendationEngine : IRecommendationEngine
    {
        private static readonly RecommendationResult[] EmptyResults = new RecommendationResult[0];

        private readonly IMediaCatalog _catalog;
        // 決定的なテストのため System.Random を使用(UnityEngine.Random と混同しないよう明示)。
        private readonly System.Random _random;

        // ルールを種類ごとの重みに解決(無効・未指定は 0)
        private readonly double _wArtist;
        private readonly double _wGenre;
        private readonly double _wTag;
        private readonly double _wRelated;
        private readonly double _wRandom;

        public RecommendationEngine(IMediaCatalog catalog, RecommendationRule[] rules = null, System.Random random = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _random = random ?? new System.Random();

            if (rules == null) rules = RecommendationRule.CreateDefault();
            foreach (var rule in rules)
            {
                if (rule == null || !rule.Enabled) continue;
                switch (rule.Kind)
                {
                    case RecommendationRuleKind.SameArtist: _wArtist = rule.Weight; break;
                    case RecommendationRuleKind.SameGenre: _wGenre = rule.Weight; break;
                    case RecommendationRuleKind.TagMatch: _wTag = rule.Weight; break;
                    case RecommendationRuleKind.Related: _wRelated = rule.Weight; break;
                    case RecommendationRuleKind.Random: _wRandom = rule.Weight; break;
                }
            }
        }

        /// <summary>
        /// seed を起点に、カタログ全体から次のおすすめを上位 count 件返す。
        /// seed 自身は除外。seed が見つからない場合は空。
        /// </summary>
        public RecommendationResult[] GetNextRecommendations(string seedId, int count)
        {
            var seed = _catalog.FindById(seedId);
            if (seed == null || count <= 0) return EmptyResults;

            var candidates = _catalog.GetAll();
            return Rank(seed, candidates, count, excludeSeed: true);
        }

        /// <summary>
        /// seed の RelatedIds(Catalog Builder が事前計算した関連)だけを対象に、
        /// ルールでスコア付けして上位 count 件返す。「この作品だから次はこれ」用途。
        /// </summary>
        public RecommendationResult[] GetRelatedRecommendations(string seedId, int count)
        {
            var seed = _catalog.FindById(seedId);
            if (seed == null || count <= 0) return EmptyResults;

            var related = _catalog.GetRelated(seedId); // 解決済み・自己/欠損は除外済み・宣言順
            if (related.Count == 0) return EmptyResults;

            return Rank(seed, related, count, excludeSeed: true);
        }

        private RecommendationResult[] Rank(
            MediaItem seed, IReadOnlyList<MediaItem> candidates, int count, bool excludeSeed)
        {
            // seed の関連 ID とタグを検索しやすい形に(大文字小文字無視)
            var seedRelated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rid in seed.RelatedIds) seedRelated.Add(rid);

            var seedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in seed.Tags) seedTags.Add(tag);

            var scored = new List<Scored>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                var cand = candidates[i];
                if (excludeSeed && string.Equals(cand.Id, seed.Id, StringComparison.OrdinalIgnoreCase))
                    continue;

                scored.Add(new Scored(i, Score(seed, cand, seedRelated, seedTags)));
            }

            // スコア降順 → 元の走査順(index 昇順)で決定的に整列
            scored.Sort((a, b) =>
            {
                int byScore = b.Result.Score.CompareTo(a.Result.Score);
                return byScore != 0 ? byScore : a.Order.CompareTo(b.Order);
            });

            int take = Math.Min(count, scored.Count);
            var results = new RecommendationResult[take];
            for (int i = 0; i < take; i++) results[i] = scored[i].Result;
            return results;
        }

        private RecommendationResult Score(
            MediaItem seed, MediaItem cand, HashSet<string> seedRelated, HashSet<string> seedTags)
        {
            double relatedScore = (_wRelated > 0 && seedRelated.Contains(cand.Id)) ? _wRelated : 0.0;

            double artistScore = (_wArtist > 0
                && !string.IsNullOrEmpty(seed.Artist)
                && string.Equals(cand.Artist, seed.Artist, StringComparison.OrdinalIgnoreCase))
                ? _wArtist : 0.0;

            double genreScore = (_wGenre > 0
                && !string.IsNullOrEmpty(seed.Genre)
                && string.Equals(cand.Genre, seed.Genre, StringComparison.OrdinalIgnoreCase))
                ? _wGenre : 0.0;

            int sharedTags = 0;
            if (_wTag > 0)
            {
                foreach (var tag in cand.Tags)
                {
                    if (seedTags.Contains(tag)) sharedTags++;
                }
            }
            double tagScore = _wTag * sharedTags;

            double randomScore = _wRandom > 0 ? _wRandom * _random.NextDouble() : 0.0;

            return new RecommendationResult(
                cand, relatedScore, artistScore, genreScore, tagScore, randomScore, sharedTags);
        }

        // 元の走査順を保持して決定的な同点崩しに使う内部ペア
        private struct Scored
        {
            public readonly int Order;
            public readonly RecommendationResult Result;

            public Scored(int order, RecommendationResult result)
            {
                Order = order;
                Result = result;
            }
        }
    }
}
