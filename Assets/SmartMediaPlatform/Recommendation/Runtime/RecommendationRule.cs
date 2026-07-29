namespace SmartMediaPlatform.Recommendation
{
    /// <summary>
    /// 1 つのスコアリングルール(種類・重み・有効フラグ)。
    /// ルールを「データ」として表現することで、engine のコードを変えずに
    /// 重みの調整や有効/無効の切り替えができる(開放閉鎖の原則)。
    /// </summary>
    public sealed class RecommendationRule
    {
        public RecommendationRuleKind Kind { get; }
        public double Weight { get; }
        public bool Enabled { get; }

        public RecommendationRule(RecommendationRuleKind kind, double weight, bool enabled = true)
        {
            Kind = kind;
            Weight = weight;
            Enabled = enabled;
        }

        public static RecommendationRule SameArtist(double weight) =>
            new RecommendationRule(RecommendationRuleKind.SameArtist, weight);

        public static RecommendationRule SameGenre(double weight) =>
            new RecommendationRule(RecommendationRuleKind.SameGenre, weight);

        public static RecommendationRule TagMatch(double weight) =>
            new RecommendationRule(RecommendationRuleKind.TagMatch, weight);

        public static RecommendationRule Related(double weight) =>
            new RecommendationRule(RecommendationRuleKind.Related, weight);

        public static RecommendationRule Random(double weight) =>
            new RecommendationRule(RecommendationRuleKind.Random, weight);

        /// <summary>
        /// 既定のルールセット。RelatedIds を最も重く、次いでアーティスト・ジャンル・タグ、
        /// ランダムは同点崩し程度の小さい重みにする。
        /// Udon 版(UdonRecommendationEngine)の既定重みと必ず一致させること。
        /// </summary>
        public static RecommendationRule[] CreateDefault()
        {
            return new[]
            {
                Related(10.0),
                SameArtist(5.0),
                SameGenre(3.0),
                TagMatch(1.0),
                Random(0.5),
            };
        }
    }
}
