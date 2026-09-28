using System;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Queue
{
    /// <summary>
    /// キューに入っている 1 エントリ。イミュータブル。
    ///
    /// 同じ MediaItem を複数回積めるため、<see cref="Handle"/> で個体を一意に識別する。
    /// これにより「2 回積んだうちの後ろだけ消す」「投票対象を一意に指す」といった操作が
    /// 将来(Vote / DJ Mode / Shared Queue)成立する。
    ///
    /// 再生状態(再生位置・再生済みフラグ等)は持たない。それは Player の責務。
    /// </summary>
    public sealed class QueueItem
    {
        /// <summary>キュー内でエントリを一意に識別するハンドル(1 始まり)。</summary>
        public int Handle { get; }

        public MediaItem Item { get; }

        public QueueItemSource Source { get; }

        /// <summary>Catalog 上の ID(<see cref="Item"/>.Id のショートカット)。</summary>
        public string MediaId => Item.Id;

        public QueueItem(int handle, MediaItem item, QueueItemSource source)
        {
            if (handle <= 0)
                throw new ArgumentOutOfRangeException(nameof(handle), "Handle must be positive.");

            Handle = handle;
            Item = item ?? throw new ArgumentNullException(nameof(item));
            Source = source;
        }

        public override string ToString()
        {
            return $"{MediaId} ({Item.Title} / {Item.Artist}, {Source})";
        }
    }
}
