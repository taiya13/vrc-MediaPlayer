using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Recommendation
{
    /// <summary>
    /// 1 件のおすすめ結果。合計スコアと、Console で内訳を確認するための
    /// ルール別スコアを保持する。イミュータブル。
    /// </summary>
    public sealed class RecommendationResult
    {
        public MediaItem Item { get; }
        public double Score { get; }

        public double RelatedScore { get; }
        public double ArtistScore { get; }
        public double GenreScore { get; }
        public double TagScore { get; }
        public double RandomScore { get; }

        /// <summary>共有タグ数(内訳表示用)。</summary>
        public int SharedTagCount { get; }

        public RecommendationResult(
            MediaItem item,
            double relatedScore,
            double artistScore,
            double genreScore,
            double tagScore,
            double randomScore,
            int sharedTagCount)
        {
            Item = item;
            RelatedScore = relatedScore;
            ArtistScore = artistScore;
            GenreScore = genreScore;
            TagScore = tagScore;
            RandomScore = randomScore;
            SharedTagCount = sharedTagCount;
            Score = relatedScore + artistScore + genreScore + tagScore + randomScore;
        }

        /// <summary>Console 用の 1 行サマリ。</summary>
        public override string ToString()
        {
            return $"{Item.Id,-12} score={Score,6:0.00}  "
                   + $"(related={RelatedScore:0.#}, artist={ArtistScore:0.#}, "
                   + $"genre={GenreScore:0.#}, tag={TagScore:0.#}×{SharedTagCount}, rand={RandomScore:0.00})  "
                   + $"{Item.Title} / {Item.Artist}";
        }
    }
}
