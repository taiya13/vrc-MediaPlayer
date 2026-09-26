using System;
using System.Collections.Generic;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Queue;

namespace SmartMediaPlatform.AutoPlay
{
    /// <summary>
    /// <b>失敗した動画を、しばらく積み直さないための記憶。</b>
    ///
    /// 再生に失敗した動画(URL 切れ・アクセス拒否・タイムアウトなど)を
    /// そのまま候補に残すと、おすすめが同じものを何度も返して
    /// <b>失敗 → 次へ → また同じもの → 失敗</b>の無限ループになります。
    /// それを止めるのがこの クラス です。
    ///
    /// <b><see cref="IPlaybackFilter"/> として振る舞います。</b>
    /// 「再生できる種別か」(<c>BackendPlaybackFilter</c>)とは別の関心事なので、
    /// 1 つのクラスに混ぜず <c>CompositePlaybackFilter</c> で並べて使います。
    ///
    /// <b>時計は外から渡します。</b>
    /// 純粋 C#(UnityEngine 非依存)なので <c>Time.time</c> は使えません。
    /// <see cref="Tick"/> で経過秒を積み上げる方式にしてあるので、
    /// EditMode テストでは時間を自由に進められます。
    ///
    /// <b>件数に上限があります。</b>
    /// 長時間再生でも記憶が際限なく増えないよう、
    /// <see cref="MaxTracked"/> を超えたら古いものから捨てます。
    /// </summary>
    public sealed class PlaybackFailureTracker : IPlaybackFilter
    {
        private sealed class Entry
        {
            public string MediaId;
            public int FailureCount;
            public float BlockedUntil;
            public string LastReason;
            public float LastFailedAt;
        }

        private readonly List<Entry> _entries = new List<Entry>();

        private float _clock;

        // ───────── 設定 ─────────

        /// <summary>1 回失敗したら、この秒数は積み直さない。</summary>
        public float CooldownSeconds { get; set; } = 120f;

        /// <summary>
        /// 失敗を重ねるほど待ち時間を延ばす倍率。
        /// 2 回目は <see cref="CooldownSeconds"/> × この値、3 回目はさらに ×、…。
        /// 1 で固定。
        /// </summary>
        public float BackoffMultiplier { get; set; } = 3f;

        /// <summary>待ち時間の上限(秒)。</summary>
        public float MaxCooldownSeconds { get; set; } = 3600f;

        /// <summary>
        /// この回数を超えて失敗した動画は、二度と積まない
        /// (<see cref="Clear"/> または <see cref="Forget"/> するまで)。0 以下で無効。
        /// </summary>
        public int MaxFailuresBeforePermanentBlock { get; set; } = 5;

        /// <summary>記憶しておく件数の上限。超えたら古いものから捨てる。</summary>
        public int MaxTracked { get; set; } = 256;

        // ───────── 状態 ─────────

        /// <summary>いま覚えている件数。</summary>
        public int TrackedCount => _entries.Count;

        /// <summary>いま積めない状態の件数。</summary>
        public int BlockedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (IsBlocked(_entries[i])) count++;
                }
                return count;
            }
        }

        /// <summary>失敗を記録した回数の累計。</summary>
        public int TotalFailures { get; private set; }

        /// <summary>直近に失敗した MediaId。</summary>
        public string LastFailedId { get; private set; }

        /// <summary>直近の失敗理由。</summary>
        public string LastReason { get; private set; }

        // ───────── 操作 ─────────

        /// <summary>時間を進める(ホストの <c>Update</c> から毎フレーム)。</summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds > 0f) _clock += deltaSeconds;
        }

        /// <summary>失敗を記録し、しばらく積まないようにする。</summary>
        public void MarkFailed(string mediaId, string reason = null)
        {
            if (string.IsNullOrWhiteSpace(mediaId)) return;

            TotalFailures++;
            LastFailedId = mediaId;
            LastReason = reason;

            var entry = Find(mediaId);
            if (entry == null)
            {
                // 先に時刻を入れてから間引く。順序が逆だと、いま足したばかりの記憶が
                // 「いちばん古い(時刻 0)」と見なされて自分自身を捨ててしまう。
                entry = new Entry { MediaId = mediaId, LastFailedAt = _clock };
                _entries.Add(entry);
                Trim();
            }

            entry.FailureCount++;
            entry.LastReason = reason;
            entry.LastFailedAt = _clock;
            entry.BlockedUntil = _clock + CooldownFor(entry.FailureCount);
        }

        /// <summary>再生できたので、失敗の記憶を消す。</summary>
        public void MarkSucceeded(string mediaId)
        {
            if (string.IsNullOrWhiteSpace(mediaId)) return;

            var entry = Find(mediaId);
            if (entry != null) _entries.Remove(entry);
        }

        /// <summary>いま積めない状態か。</summary>
        public bool IsBlocked(string mediaId)
        {
            var entry = Find(mediaId);
            return entry != null && IsBlocked(entry);
        }

        /// <summary>その動画の失敗回数。</summary>
        public int FailureCountOf(string mediaId)
        {
            var entry = Find(mediaId);
            return entry != null ? entry.FailureCount : 0;
        }

        /// <summary>その動画の記憶を消す(手動で再挑戦させたいとき)。</summary>
        public bool Forget(string mediaId)
        {
            var entry = Find(mediaId);
            return entry != null && _entries.Remove(entry);
        }

        /// <summary>すべての記憶を消す。</summary>
        public void Clear()
        {
            _entries.Clear();
        }

        /// <summary>いま積めない ID の一覧(診断用)。</summary>
        public List<string> GetBlockedIds()
        {
            var result = new List<string>();
            for (int i = 0; i < _entries.Count; i++)
            {
                if (IsBlocked(_entries[i])) result.Add(_entries[i].MediaId);
            }
            return result;
        }

        // ───────── IPlaybackFilter ─────────

        public bool CanPlay(MediaItem item)
        {
            return item != null && !IsBlocked(item.Id);
        }

        // ───────── 内部 ─────────

        private bool IsBlocked(Entry entry)
        {
            if (MaxFailuresBeforePermanentBlock > 0
                && entry.FailureCount > MaxFailuresBeforePermanentBlock)
            {
                return true;
            }

            return _clock < entry.BlockedUntil;
        }

        private float CooldownFor(int failureCount)
        {
            float seconds = CooldownSeconds;

            for (int i = 1; i < failureCount && BackoffMultiplier > 1f; i++)
            {
                seconds *= BackoffMultiplier;
                if (seconds >= MaxCooldownSeconds) return MaxCooldownSeconds;
            }

            return seconds < MaxCooldownSeconds ? seconds : MaxCooldownSeconds;
        }

        private Entry Find(string mediaId)
        {
            if (string.IsNullOrWhiteSpace(mediaId)) return null;

            for (int i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].MediaId, mediaId, StringComparison.OrdinalIgnoreCase))
                {
                    return _entries[i];
                }
            }
            return null;
        }

        /// <summary>上限を超えたら、いちばん古い失敗から捨てる(記憶が増え続けないように)。</summary>
        private void Trim()
        {
            while (MaxTracked > 0 && _entries.Count > MaxTracked)
            {
                int oldest = 0;
                for (int i = 1; i < _entries.Count; i++)
                {
                    if (_entries[i].LastFailedAt < _entries[oldest].LastFailedAt) oldest = i;
                }
                _entries.RemoveAt(oldest);
            }
        }

        public override string ToString()
        {
            return $"PlaybackFailureTracker(記憶 {TrackedCount} / 停止中 {BlockedCount} "
                   + $"/ 失敗累計 {TotalFailures})";
        }
    }
}
