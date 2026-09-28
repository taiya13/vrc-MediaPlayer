using System;
using SmartMediaPlatform.Audio;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Adapter.Audio
{
    /// <summary>
    /// <see cref="AudioBackend"/>(AudioSource で実際に音を鳴らす)を包むアダプタ。
    ///
    /// 内側の <see cref="AudioBackend"/> は <see cref="ISeekableBackend"/> を実装しているので、
    /// 再生位置はそのまま通す。<see cref="DummyVideoBackendAdapter"/> がアダプタ側で
    /// 再生位置を肩代わりしているのと対照的だが、<b>上位から見た API はまったく同じ</b>。
    /// これが「Backend の違いを吸収する」ということ。
    ///
    /// このアダプタが足している音声固有の面倒:
    ///  - 音源(AudioClip)が用意できていない ID を、読み込み前にエラーとして弾く
    ///  - 音源の対応表を持ち、必要なら手続き生成で埋める
    /// </summary>
    public sealed class AudioBackendAdapter : BackendAdapterBase
    {
        private readonly AudioClipLibrary _library;

        /// <param name="name">アダプタ名(ログに出る)。</param>
        /// <param name="backend">包む音声バックエンド。</param>
        /// <param name="library">MediaItem と AudioClip の対応表。</param>
        /// <param name="logger">ログ出力先。</param>
        public AudioBackendAdapter(
            string name,
            AudioBackend backend,
            AudioClipLibrary library,
            IBackendLogger logger = null)
            : base(name, backend, logger, MediaType.Music)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
        }

        /// <summary>包んでいる音声バックエンド。</summary>
        public AudioBackend AudioBackend => (AudioBackend)Inner;

        /// <summary>音源の対応表。</summary>
        public AudioClipLibrary Library => _library;

        /// <summary>
        /// 種別が Music で、かつ<b>音源が用意できている</b>ときだけ扱える。
        /// 用意できていなければ他のアダプタに任せる(あるいは扱えないと分かる)。
        /// </summary>
        protected override bool CanPlayCore(MediaItem item)
        {
            return _library.Contains(item) && Inner.CanPlay(item);
        }

        public override bool Load(MediaItem item)
        {
            // 音源が無い場合は、内側に渡す前に理由の分かるエラーにする
            if (item != null && item.Type == MediaType.Music && !_library.Contains(item))
            {
                ReportError($"Load 失敗: {item.Id} の AudioClip が登録されていません");
                return false;
            }

            return base.Load(item);
        }

        /// <summary>
        /// カタログの Music に対して、まだ音源が無いものを手続き生成で埋める。
        /// 本物の音源が登録済みの ID は上書きしない。
        /// </summary>
        /// <returns>生成した件数。</returns>
        public int PrepareClipsFor(IMediaCatalog catalog, float durationSeconds = 1.5f)
        {
            if (catalog == null) return 0;

            int created = ProceduralClipFactory.FillLibrary(
                _library, catalog, durationSeconds, MediaType.Music);

            if (created > 0) Log($"音源を {created} 件用意しました (合計 {_library.Count} 件)");
            return created;
        }
    }
}
