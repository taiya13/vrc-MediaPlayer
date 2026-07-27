using System;
using System.Collections.Generic;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Queue
{
    /// <summary>
    /// <b>「いま Queue に積んでよいメディアか」の判定。</b>
    ///
    /// おすすめは Catalog 全体から候補を返すので、そのまま積むと
    /// 「再生できない種別」や「さっき失敗したばかりの動画」が混ざります。
    /// それを<b>積む直前に</b>ふるい落とすのがこの契約です。
    ///
    /// <b>この判定を Recommendation にも Queue 本体にも持ち込まないのが要点です。</b>
    /// <list type="bullet">
    /// <item><see cref="Recommendation.IRecommendationEngine"/> は「何が似ているか」だけを知る</item>
    /// <item><see cref="IQueue"/> は「並び」だけを知る</item>
    /// <item>「積んでよいか」は文脈の都合なので、<see cref="IQueueRefiller"/> が積む直前に見る</item>
    /// </list>
    ///
    /// C# の <see cref="Func{T, TResult}"/> ではなく インターフェース にしてあるのは、
    /// Phase1-2 から通している方針(将来 UdonSharp へ移すときに書き換えずに済ませるため)です。
    ///
    /// <b>Phase3-4 で <c>SmartMediaPlatform.AutoPlay</c> からここへ移しました。</b>
    /// 補充の実装(<see cref="RecommendationQueueRefiller"/>)がこの判定を使うため、
    /// Queue と同じ層に置くのが自然だからです。Backend に問い合わせる実装
    /// (<c>BackendPlaybackFilter</c>)だけは Backend を参照できる AutoPlay 側に残しています
    /// (Queue → Backend の参照は依存の向きが逆になるため)。
    /// </summary>
    public interface IPlaybackFilter
    {
        /// <summary>そのメディアを積んでよいか。</summary>
        bool CanPlay(MediaItem item);
    }

    /// <summary>
    /// 種別で判定する <see cref="IPlaybackFilter"/>。既定は Video / Live。
    ///
    /// 「動画だけのループにしたい」と<b>明示的に決める</b>ときに使います。
    /// 登録済みバックエンドに合わせて自動で決めたい場合は
    /// <c>BackendPlaybackFilter</c>(AutoPlay)のほうが適しています。
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

    /// <summary>何でも受け入れるフィルタ(既定の挙動に戻したいとき用)。</summary>
    public sealed class AnyPlaybackFilter : IPlaybackFilter
    {
        public static readonly AnyPlaybackFilter Instance = new AnyPlaybackFilter();

        private AnyPlaybackFilter() { }

        public bool CanPlay(MediaItem item) => item != null;

        public override string ToString() => "AnyPlaybackFilter";
    }

    /// <summary>
    /// 複数のふるいを <b>すべて通ったものだけ</b>を受け入れる合成フィルタ。
    ///
    /// Phase3-4 の要点:「再生できる種別か」(<c>BackendPlaybackFilter</c>)と
    /// 「さっき失敗していないか」(<c>PlaybackFailureTracker</c>)は別々の関心事なので、
    /// <b>1 つのクラスに混ぜずに合成</b>します。
    /// 新しい条件(お気に入りのみ・年齢制限など)が要るときも、実装を 1 つ足して並べるだけです。
    /// </summary>
    public sealed class CompositePlaybackFilter : IPlaybackFilter
    {
        private readonly IPlaybackFilter[] _filters;

        public CompositePlaybackFilter(params IPlaybackFilter[] filters)
        {
            var usable = new List<IPlaybackFilter>();
            if (filters != null)
            {
                foreach (var filter in filters)
                {
                    if (filter != null) usable.Add(filter);
                }
            }
            _filters = usable.ToArray();
        }

        /// <summary>合成しているふるい。</summary>
        public IReadOnlyList<IPlaybackFilter> Filters => _filters;

        public bool CanPlay(MediaItem item)
        {
            if (item == null) return false;

            for (int i = 0; i < _filters.Length; i++)
            {
                if (!_filters[i].CanPlay(item)) return false;
            }
            return true;
        }

        public override string ToString()
        {
            return _filters.Length == 0
                ? "CompositePlaybackFilter(なし)"
                : "CompositePlaybackFilter(" + string.Join(" + ", Array.ConvertAll(
                    _filters, f => f.ToString())) + ")";
        }
    }
}
