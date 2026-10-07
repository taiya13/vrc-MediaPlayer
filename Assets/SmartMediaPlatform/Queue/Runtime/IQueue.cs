using System.Collections.Generic;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Queue
{
    /// <summary>
    /// Queue API。上位レイヤー(将来の Music/Video Player, Auto Play, DJ Mode,
    /// Vote System, Shared Queue)はこのインターフェースだけに依存する。
    ///
    /// 位置の約束:
    ///  - index 0 が「Now(現在の先頭)」、index 1 が「Next」。
    ///  - <see cref="Dequeue"/> / <see cref="Skip"/> は先頭を取り除く。
    ///
    /// Queue 自身は再生を行わない。再生・同期・投票は上位が Queue API を通して実現する。
    /// この抽象があることで、将来 Shared Queue(同期実装)を別クラスとして差し込んでも
    /// 上位コードを変更せずに済む。
    /// </summary>
    public interface IQueue
    {
        /// <summary>現在の要素数。</summary>
        int Count { get; }

        /// <summary>空なら true。</summary>
        bool IsEmpty { get; }

        /// <summary>
        /// 変更のたびに増える版番号。ポーリングによる差分検知(将来の同期・表示更新)に使う。
        /// UI には依存しない純粋なカウンタ。
        /// </summary>
        int Version { get; }

        /// <summary>末尾に追加する。</summary>
        QueueItem Enqueue(MediaItem item, QueueItemSource source = QueueItemSource.Manual);

        /// <summary>
        /// 「次に再生」として、Now(index 0)の直後に割り込ませる。
        /// 空の場合は先頭に入る。
        /// </summary>
        QueueItem EnqueueNext(MediaItem item, QueueItemSource source = QueueItemSource.Manual);

        /// <summary>先頭を取り出して取り除く。空なら null。</summary>
        QueueItem Dequeue();

        /// <summary>先頭を取り除かずに見る(= Now)。空なら null。</summary>
        QueueItem Peek();

        /// <summary>index 番目を取り除かずに見る。範囲外なら null。</summary>
        QueueItem PeekAt(int index);

        /// <summary>Next(index 1)を取り除かずに見る。なければ null。</summary>
        QueueItem PeekNext();

        /// <summary>
        /// 現在の先頭を捨てて次へ進む。戻り値は「新しい先頭(= 次の Now)」。
        /// 空になった場合は null。
        /// </summary>
        QueueItem Skip();

        /// <summary>全消去。</summary>
        void Clear();

        /// <summary>その Catalog ID がキューに含まれるか。</summary>
        bool Contains(string mediaId);

        /// <summary>そのハンドルのエントリが含まれるか。</summary>
        bool ContainsHandle(int handle);

        /// <summary>
        /// その Catalog ID の最初の 1 件を取り除く。取り除けたら true。
        /// 同じ曲が複数ある場合は先頭側が対象。
        /// </summary>
        bool Remove(string mediaId);

        /// <summary>ハンドル指定で 1 件取り除く。取り除けたら true。</summary>
        bool RemoveHandle(int handle);

        /// <summary>index 指定で 1 件取り除く。取り除けたら true。</summary>
        bool RemoveAt(int index);

        /// <summary>
        /// fromIndex の要素を toIndex へ移動する。移動できたら true。
        /// 範囲外や同一位置は false。
        /// </summary>
        bool Move(int fromIndex, int toIndex);

        /// <summary>先頭から順に全件(読み取り専用)。</summary>
        IReadOnlyList<QueueItem> GetAll();

        /// <summary>その Catalog ID の位置。無ければ -1。</summary>
        int IndexOf(string mediaId);
    }
}
