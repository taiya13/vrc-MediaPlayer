using System;
using System.Collections.Generic;
using SmartMediaPlatform.Catalog.Store;
using SmartMediaPlatform.Queue;

namespace SmartMediaPlatform.Library.Playback
{
    /// <summary>
    /// <b>Queue の中身を「見て」「並べ替えて」「消す」ための窓。</b>Phase4-3 で追加。
    ///
    /// <code>
    /// IQueue(並びを持つ) → QueueView → UI
    ///     MediaId              DisplayMeta(URL 無し)
    /// </code>
    ///
    /// <b>なぜ必要だったか</b><br/>
    /// <c>IQueue.GetAll()</c> が返す <c>QueueItem</c> は <c>MediaItem</c> を抱えていて、
    /// そこには <c>Url</c> が入っています。そのまま UI へ渡すと
    /// <b>Phase4-2 で塞いだ「UI が URL を触れる穴」が Queue 経由で開いてしまいます</b>。
    /// この クラス が <c>MediaId</c> だけを取り出して
    /// <see cref="ICatalogStore"/> から <see cref="DisplayMeta"/> を引き直すので、
    /// URL は Queue の外へ出ません。
    ///
    /// <b>Queue の責務は増やしていません。</b>
    /// 並べ替えも削除も <c>IQueue</c> が Phase1-4 から持っている API
    /// (<c>Move</c> / <c>RemoveAt</c> / <c>Clear</c>)を呼ぶだけで、
    /// このクラスは<b>「見せ方」と「操作の窓口」</b>しか持ちません。
    ///
    /// <b>カタログ全体の一覧・関連動画の一覧と同じ形</b>(<see cref="IMediaListView"/>)なので、
    /// UI の部品も <c>MediaLibraryFormatter</c> も <c>LibraryPlaybackBridge</c> も
    /// そのまま使い回せます。
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public sealed class QueueView : MediaListViewBase
    {
        private readonly IQueue _queue;

        /// <summary>最後に見た Queue の版。変わったときだけ組み直す。</summary>
        private int _seenVersion = -1;

        /// <param name="store">表示情報を引く唯一の窓口。</param>
        /// <param name="queue">見せる Queue。</param>
        public QueueView(ICatalogStore store, IQueue queue)
            : base(store)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            Rebuild(notify: false);
            _seenVersion = _queue.Version;
        }

        /// <summary>見せている Queue(診断用)。</summary>
        public IQueue Queue => _queue;

        /// <summary>Queue が空か。</summary>
        public bool IsEmpty => _queue.IsEmpty;

        /// <summary>
        /// <b>いま再生しているもの(Queue の先頭)。</b>
        ///
        /// Queue は Phase1-4 から「<b>先頭 = Now</b>」という約束で動いています。
        /// </summary>
        public DisplayMeta NowPlaying => GetAt(0);

        /// <summary>
        /// <b>毎フレーム呼んでよい同期。</b>
        /// Queue が変わっていたら並びを組み直して観測者へ知らせます。
        ///
        /// <c>IQueue.Version</c>(Phase1-4 からある更新カウンタ)を見るだけなので、
        /// 変わっていなければ何もしません。
        /// </summary>
        /// <returns>組み直したら true。</returns>
        public bool Sync()
        {
            if (_queue.Version == _seenVersion) return false;

            _seenVersion = _queue.Version;
            Rebuild(notify: true);
            return true;
        }

        /// <summary>強制的に組み直す。</summary>
        public void Refresh()
        {
            _seenVersion = _queue.Version;
            Rebuild(notify: true);
        }

        // ───────── 並べ替え・削除(Queue の既存 API へ委譲) ─────────

        /// <summary>
        /// index 番目を 1 つ前へ動かす。
        /// <b>先頭(いま再生中)は動かしません</b> — 動かすと再生と Queue がずれるためです。
        /// </summary>
        public bool MoveUp(int index)
        {
            // index 1 を 0 へ動かすと「いま鳴っているもの」を追い越してしまう
            if (index <= 1 || index >= Count) return false;
            return ApplyAndSync(() => _queue.Move(index, index - 1));
        }

        /// <summary>index 番目を 1 つ後ろへ動かす。先頭は動かしません。</summary>
        public bool MoveDown(int index)
        {
            if (index < 1 || index >= Count - 1) return false;
            return ApplyAndSync(() => _queue.Move(index, index + 1));
        }

        /// <summary>
        /// index 番目を Queue から外す。
        /// <b>先頭(いま再生中)は外せません</b> — 止めたいなら <c>PlayerSession.Stop()</c> です。
        /// </summary>
        public bool RemoveAt(int index)
        {
            if (index < 1 || index >= Count) return false;
            return ApplyAndSync(() => _queue.RemoveAt(index));
        }

        /// <summary>選択中のものを Queue から外す。</summary>
        public bool RemoveSelected()
        {
            return HasSelection && RemoveAt(SelectedIndex);
        }

        /// <summary>
        /// 先頭(いま再生中)以外をすべて外す。
        /// 「この曲のあとは止めたい」ときに使います。
        /// </summary>
        public int ClearUpcoming()
        {
            // ★ 見ているのは Queue そのものの件数です。
            //   ここで表示側の Count(= 直近に組み直した並びの件数)を使うと、
            //   1 件外しても表示は古いままなので次の RemoveAt が範囲外になり、
            //   1 件だけ外して止まってしまいます。
            int removed = 0;
            while (_queue.Count > 1 && _queue.RemoveAt(_queue.Count - 1)) removed++;

            if (removed > 0) Refresh();
            return removed;
        }

        /// <summary>Queue を空にする。</summary>
        public void Clear()
        {
            _queue.Clear();
            Refresh();
        }

        // ───────── 何を並べるか ─────────

        protected override void BuildEntries(List<DisplayMeta> into)
        {
            var entries = _queue.GetAll();
            for (int i = 0; i < entries.Count; i++)
            {
                // ★ ここが要点:QueueItem からは MediaId しか取らない。
                //   表示情報は窓口から引き直すので、MediaItem(= Url)は外へ出ない。
                var meta = Store.GetDisplayMeta(entries[i].MediaId);
                if (meta != null) into.Add(meta);
            }
        }

        // ───────── 内部 ─────────

        private bool ApplyAndSync(Func<bool> operation)
        {
            if (!operation()) return false;

            Refresh();
            return true;
        }

        public override string ToString()
        {
            return $"QueueView({Count} 件 / 先頭 {(NowPlaying != null ? NowPlaying.MediaId : "なし")})";
        }
    }
}
