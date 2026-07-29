using SmartMediaPlatform.Backend;
using UnityEngine;

namespace SmartMediaPlatform.World
{
    /// <summary>
    /// <b>ログだけのバックエンド。</b>既定の <see cref="IMediaBackendProvider"/>。
    ///
    /// <b>音も映像も出ません。</b>
    /// VRChat SDK が無い環境でも Prefab をドラッグしただけで
    /// 一連の流れ(選ぶ → Queue → 再生 → 次へ)を触れるようにするための代役です。
    ///
    /// 実機で鳴らすときは、この代わりに
    /// <c>VRChatMediaBackendProvider</c>(SDK 必須)を Player の下へ置いてください。
    /// <b>差し替えるのはそれだけ</b>で、Prefab の他の部分も上位のコードも変わりません。
    /// </summary>
    [AddComponentMenu("Smart Media Platform/Dummy Media Backend Provider")]
    public sealed class DummyMediaBackendProvider : MonoBehaviour, IMediaBackendProvider
    {
        [Tooltip("バックエンドの名前(ログに出ます)")]
        [SerializeField] private string _backendName = "DummyBackend";

        public IMediaBackend CreateBackend(IMediaScreen screen, IBackendLogger logger)
        {
            return new DummyBackend(_backendName, logger);
        }

        /// <summary>何も設定が無いときのバックエンド。</summary>
        public static IMediaBackend CreateDummyBackend(IBackendLogger logger)
        {
            return new DummyBackend("DummyBackend", logger);
        }

        public string Describe() => "ログのみ(音も映像も出ません)";

        public override string ToString() => $"DummyMediaBackendProvider({_backendName})";
    }
}
