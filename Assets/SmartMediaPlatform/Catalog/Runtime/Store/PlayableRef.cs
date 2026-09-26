using System;

namespace SmartMediaPlatform.Catalog.Store
{
    /// <summary>
    /// <b>「これは再生に渡せるものだ」</b>という印だけを持つ参照。
    ///
    /// <b>URL は入っていません。</b>運ぶのは <see cref="MediaId"/> と <see cref="Type"/> だけです。
    /// URL への変換は、いまも今後も <c>VideoBackend</c>(ベイク済み <c>VRCUrl</c> 表)の中でだけ行われます。
    ///
    /// <b>なぜ string の MediaId をそのまま渡さないのか</b><br/>
    /// 「カタログに載っていることが確認済みの ID」と「ただの文字列」を
    /// <b>型で区別する</b>ためです。<see cref="ICatalogStore"/> を通ったものだけが
    /// <see cref="IsValid"/> == true になるので、
    /// 存在しない ID が再生系まで流れていく事故を入口で止められます。
    ///
    /// <code>
    /// PlayableRef playable = store.GetPlayableRef(meta.MediaId);
    /// if (playable.IsValid) bridge.Play(playable);
    /// </code>
    ///
    /// 構造体にしてあるのは、UI が 1 行ごとに持っても割り当てが増えないようにするためです。
    /// イミュータブル。純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public struct PlayableRef : IEquatable<PlayableRef>
    {
        /// <summary>どこも指していない参照。</summary>
        public static readonly PlayableRef None = default(PlayableRef);

        private readonly string _mediaId;
        private readonly MediaType _type;

        /// <summary>
        /// <b>これを直接呼べるのは <see cref="ICatalogStore"/> の実装だけにしてください。</b>
        /// カタログに存在しない ID から作ると、<see cref="IsValid"/> の意味が壊れます。
        /// </summary>
        public PlayableRef(string mediaId, MediaType type)
        {
            _mediaId = string.IsNullOrWhiteSpace(mediaId) ? null : mediaId;
            _type = _mediaId != null ? type : MediaType.Unknown;
        }

        /// <summary>カタログ内で一意な ID。<see cref="IsValid"/> が false なら null。</summary>
        public string MediaId => _mediaId;

        /// <summary>種別。再生できるバックエンドを選ぶのに使われます。</summary>
        public MediaType Type => _type;

        /// <summary>カタログに載っているものを指しているか。</summary>
        public bool IsValid => _mediaId != null;

        public bool Equals(PlayableRef other)
        {
            return string.Equals(_mediaId, other._mediaId, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj) => obj is PlayableRef other && Equals(other);

        public override int GetHashCode()
        {
            return _mediaId != null ? _mediaId.ToLowerInvariant().GetHashCode() : 0;
        }

        public static bool operator ==(PlayableRef a, PlayableRef b) => a.Equals(b);

        public static bool operator !=(PlayableRef a, PlayableRef b) => !a.Equals(b);

        public override string ToString()
        {
            return IsValid ? $"PlayableRef({_mediaId} [{_type}])" : "PlayableRef(none)";
        }
    }
}
