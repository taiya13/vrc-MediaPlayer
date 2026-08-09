using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Adapter
{
    /// <summary>
    /// 任意の <see cref="IMediaBackend"/> をそのまま包む汎用アダプタ。
    ///
    /// 特別な事情が無いバックエンドはこれで足りる。
    /// シークは内側の能力に従う(対応していなければ <see cref="BackendAdapterBase.CanSeek"/> が false)。
    /// </summary>
    public sealed class MediaBackendAdapter : BackendAdapterBase
    {
        public MediaBackendAdapter(
            string name,
            IMediaBackend inner,
            IBackendLogger logger = null,
            params MediaType[] supportedTypes)
            : base(name, inner, logger, supportedTypes)
        {
        }
    }
}
