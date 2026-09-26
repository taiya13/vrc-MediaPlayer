using System;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Queue;

namespace SmartMediaPlatform.AutoPlay
{
    /// <summary>
    /// <b>登録済みのバックエンドに直接聞く</b> <see cref="IPlaybackFilter"/>。
    ///
    /// <c>BackendManager.CanPlay(item)</c> は「そのメディアを扱えるバックエンドが居るか」を
    /// 答えます(Phase1-5 から変更なし)。これを使うと
    /// <b>どこにも <c>MediaType</c> の分岐を書かずに</b>再生可能なものだけを積めます。
    ///
    /// VideoBackend だけ登録すれば動画だけのループになり、
    /// AudioBackend も登録すれば音楽も混ざる — <b>設定ではなく構成で決まります。</b>
    /// 「Backend の種類を判断するのは BackendManager だけ」という
    /// Phase1-5 からの約束をそのまま利用しています。
    ///
    /// <b>Phase3-4:</b> 種別で判定する <c>MediaTypePlaybackFilter</c> などは
    /// <c>SmartMediaPlatform.Queue</c> へ移しました。
    /// このクラスだけが <c>Backend</c> を参照するため、ここに残っています
    /// (Queue → Backend の参照は依存の向きが逆になるので置けません)。
    /// </summary>
    public sealed class BackendPlaybackFilter : IPlaybackFilter
    {
        private readonly BackendManager _manager;

        public BackendPlaybackFilter(BackendManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        public bool CanPlay(MediaItem item)
        {
            return item != null && _manager.CanPlay(item);
        }

        public override string ToString()
        {
            return $"BackendPlaybackFilter({_manager.Backends.Count} backends)";
        }
    }
}
