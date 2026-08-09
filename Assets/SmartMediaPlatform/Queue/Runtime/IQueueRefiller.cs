namespace SmartMediaPlatform.Queue
{
    /// <summary>
    /// <b>Queue を補充する方法。</b>
    ///
    /// Phase3-3 までは似た補充処理が 4 箇所(<see cref="QueueManager"/> /
    /// <c>AutoQueueService</c> / <c>PlayerSession</c> / <c>RecommendationPlaybackService</c>)に
    /// 散らばっていました。Phase3-4 でこの契約に集約し、
    /// <b>実装は <see cref="RecommendationQueueRefiller"/> ひとつ</b>になっています。
    ///
    /// <b>役割分担</b>
    /// <list type="bullet">
    /// <item><b>種(seed)を決めるのは呼び出し側</b> — 「いま何を基準にするか」は文脈だから
    /// (再生中の曲・Queue の末尾・直前に消えた曲…と、呼ぶ側ごとに正解が違う)</item>
    /// <item><b>積み方を決めるのはこの実装</b> — 候補の取得・重複排除・ふるい・
    /// 尽きたときの緩和は、どこから呼ばれても同じでよい</item>
    /// </list>
    ///
    /// この分け方のおかげで、呼び出し側には数行の「種の決め方」しか残りません。
    /// </summary>
    public interface IQueueRefiller
    {
        /// <summary>
        /// Queue を補充する。
        /// </summary>
        /// <param name="queue">積む先。</param>
        /// <param name="seedMediaId">おすすめの起点にする Catalog の ID。</param>
        /// <param name="wanted">積みたい件数。0 以下なら何もしない。</param>
        /// <param name="excludeMediaId">積んではいけない ID(通常は再生中のもの)。</param>
        /// <param name="candidateCount">
        /// おすすめから取り出す候補数。0 以下ならカタログ全件。
        /// 既存の呼び出し側の挙動を厳密に保つために用意している調整弁です。
        /// </param>
        /// <returns>実際に積んだ件数。</returns>
        int Refill(
            IQueue queue,
            string seedMediaId,
            int wanted,
            string excludeMediaId = null,
            int candidateCount = 0);

        /// <summary>
        /// 次に積める MediaId を 1 件だけ選ぶ(Queue には積まない)。
        /// <b>戻り値は MediaId(string)だけ</b>で、<c>MediaItem</c> も URL も返しません。
        /// </summary>
        /// <returns>積める MediaId。見つからなければ null。</returns>
        string PickNext(IQueue queue, string seedMediaId, string excludeMediaId = null);

        /// <summary>「直近に積んだ」記録を消す(Queue を作り直したときなど)。</summary>
        void ForgetRecent();
    }
}
