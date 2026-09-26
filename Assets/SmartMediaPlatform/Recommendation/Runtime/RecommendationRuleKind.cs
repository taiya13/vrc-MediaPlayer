namespace SmartMediaPlatform.Recommendation
{
    /// <summary>
    /// スコアリング対象(ルールの種類)。
    /// このフェーズは AI を使わず、これらのルールの重み付き合計で順位を決める。
    /// 将来ルールを増やす場合もここに追加する(engine 側の switch に対応を足すだけ)。
    /// </summary>
    public enum RecommendationRuleKind
    {
        /// <summary>同じアーティスト。</summary>
        SameArtist,

        /// <summary>同じジャンル。</summary>
        SameGenre,

        /// <summary>タグ一致(共有タグ数に比例)。</summary>
        TagMatch,

        /// <summary>Catalog Builder が事前計算した RelatedIds に含まれる(最も強い信号)。</summary>
        Related,

        /// <summary>ランダム補正(僅かなゆらぎで単調さを避ける / 同点を崩す)。</summary>
        Random,
    }
}
