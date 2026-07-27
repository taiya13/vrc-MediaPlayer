using System;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.AutoPlay
{
    /// <summary>
    /// <b>「いま再生できるメディアか」の判定。</b>
    ///
    /// おすすめは Catalog 全体から候補を返すので、Video バックエンドしか
    /// 登録していない構成では Music が混ざります。そのまま Queue へ積むと
    /// <c>BackendManager.LoadCurrent()</c> が失敗して再生が止まります。
    ///
    /// <b>この判定を Recommendation にも Queue にも持ち込まないのが要点です。</b>
    /// <list type="bullet">
    /// <item>Recommendation は「何が似ているか」だけを知る(順位付けは変更しない)</item>
    /// <item>Queue は「並び」だけを知る(積むものを選り好みしない)</item>
    /// <item>「再生できるか」は Backend の都合なので、<b>積む直前に</b>ここで見る</item>
    /// </list>
    ///
    /// C# の <see cref="Func{T, TResult}"/> ではなく インターフェース にしてあるのは、
    /// Phase1-2 から通している方針(将来 UdonSharp へ移すときに書き換えずに済ませるため)です。
    /// </summary>
    public interface IPlaybackFilter
    {
        /// <summary>そのメディアを再生できるか。</summary>
        bool CanPlay(MediaItem item);
    }

    /// <summary>
    /// 種別で判定する <see cref="IPlaybackFilter"/>。既定は Video / Live。
    ///
    /// 「動画だけのおすすめループにしたい」と<b>明示的に決める</b>ときに使います。
    /// 登録済みバックエンドに合わせて自動で決めたい場合は
    /// <see cref="BackendPlaybackFilter"/> のほうが適しています。
    /// </summary>
    public sealed class MediaTypePlaybackFilter : IPlaybackFilter
    {
        private static readonly MediaType[] DefaultTypes = { MediaType.Video, MediaType.Live };

        private readonly MediaType[] _types;

        public MediaTypePlaybackFilter(params MediaType[] types)
        {
            _types = types != null && types.Length > 0
                ? (MediaType[])types.Clone()
                : DefaultTypes;
        }

        /// <summary>受け入れる種別。</summary>
        public MediaType[] Types => (MediaType[])_types.Clone();

        public bool CanPlay(MediaItem item)
        {
            if (item == null) return false;

            for (int i = 0; i < _types.Length; i++)
            {
                if (_types[i] == item.Type) return true;
            }
            return false;
        }

        public override string ToString()
        {
            return $"MediaTypePlaybackFilter({string.Join("/", Array.ConvertAll(_types, t => t.ToString()))})";
        }
    }

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

    /// <summary>何でも受け入れるフィルタ(既定の挙動に戻したいとき用)。</summary>
    public sealed class AnyPlaybackFilter : IPlaybackFilter
    {
        public static readonly AnyPlaybackFilter Instance = new AnyPlaybackFilter();

        private AnyPlaybackFilter() { }

        public bool CanPlay(MediaItem item) => item != null;

        public override string ToString() => "AnyPlaybackFilter";
    }
}
