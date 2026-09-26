namespace SmartMediaPlatform.Recommendation
{
    /// <summary>
    /// <b>おすすめを返すだけ</b>の契約。
    ///
    /// Phase3-4 で追加。<see cref="RecommendationEngine"/>(ルールベース)は
    /// これを実装しているだけで、<b>アルゴリズムは 1 行も変えていません。</b>
    ///
    /// この インターフェース があることで、将来こういった実装へ差し替えられます:
    /// <list type="bullet">
    /// <item>AI Recommendation(Phase6)</item>
    /// <item>外部 API Recommendation</item>
    /// <item>履歴ベース Recommendation</item>
    /// <item>人気順 Recommendation</item>
    /// </list>
    ///
    /// <b>責務は「順位付けした候補を返す」ことだけ</b>です。
    /// 「再生できるか」「Queue に積むか」は一切知りません
    /// (それは <c>IPlaybackFilter</c> と <c>IQueueRefiller</c> の仕事)。
    /// 依存も <see cref="Catalog.IMediaCatalog"/> だけに保ってください
    /// (asmdef の参照が <c>SmartMediaPlatform.Catalog</c> だけである意味が消えます)。
    ///
    /// C# の <c>Func</c> ではなく インターフェース にしてあるのは、Phase1-2 から通している方針
    /// (将来 UdonSharp へ移すときに書き換えずに済ませるため)です。
    /// </summary>
    public interface IRecommendationEngine
    {
        /// <summary>
        /// seed を起点に、カタログ全体から次のおすすめを上位 count 件返す。
        /// seed 自身は除外。seed が見つからない場合は空配列(null は返さない)。
        /// </summary>
        RecommendationResult[] GetNextRecommendations(string seedId, int count);

        /// <summary>
        /// seed の <c>RelatedIds</c>(Catalog Builder が事前計算した関連)だけを対象に、
        /// 順位付けして上位 count 件返す。「この作品だから次はこれ」用途。
        /// </summary>
        RecommendationResult[] GetRelatedRecommendations(string seedId, int count);
    }
}
