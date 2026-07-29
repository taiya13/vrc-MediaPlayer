using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Video.VRChat;
using UnityEngine;

namespace SmartMediaPlatform.World.VRChat
{
    /// <summary>
    /// <b>VRChat の動画プレイヤーで再生するバックエンド。</b>
    /// <see cref="IMediaBackendProvider"/> の実機版。
    ///
    /// <b>Prefab の差し替えはこのコンポーネント 1 つだけ</b>です。
    /// <code>
    /// SmartMediaPlayer/Player/
    ///   ├── DummyMediaBackendProvider   ← ログのみ(SDK 不要)
    ///   └── VRChatMediaBackendProvider  ← 実際に動画が出る(SDK 必須)  ★どちらか一方
    /// </code>
    /// <see cref="SmartMediaPlayerRoot"/> は子から
    /// <see cref="IMediaBackendProvider"/> を 1 つ探すだけなので、
    /// <b>置き換えれば他は何も変わりません</b>。
    ///
    /// <b>やっていること</b>は <c>VRChatVideoBackendHost</c> に
    /// 組み立てさせて、その <c>VideoBackendAdapter</c> を返すだけです
    /// (Phase3-1 からの経路をそのまま使います)。
    ///
    /// <b>AVPro / Unity 版の切り替え</b>は <c>VRChatVideoBackendHost</c> の
    /// <c>Preferred Player</c>(Inspector)で行います。既定は AVPro です。
    /// </summary>
    [RequireComponent(typeof(VRChatVideoBackendHost))]
    [AddComponentMenu("Smart Media Platform/VRChat Media Backend Provider")]
    public sealed class VRChatMediaBackendProvider : MonoBehaviour, IMediaBackendProvider
    {
        [Header("URL の焼き込み")]
        [Tooltip("組み立て時に、焼き込み済み URL が 0 件なら警告を出す")]
        [SerializeField] private bool _warnWhenNoBakedUrls = true;

        private VRChatVideoBackendHost _host;

        /// <summary>包んでいる Host。</summary>
        public VRChatVideoBackendHost Host
        {
            get
            {
                if (_host == null) _host = GetComponent<VRChatVideoBackendHost>();
                return _host;
            }
        }

        public IMediaBackend CreateBackend(IMediaScreen screen, IBackendLogger logger)
        {
            var host = Host;
            if (host == null)
            {
                Debug.LogError(
                    $"[{name}] VRChatVideoBackendHost が同じ GameObject にありません。", this);
                return null;
            }

            // 組み立ては Host に任せる(Phase3-1 からの経路をそのまま使う)
            var adapter = host.EnsureBuilt(logger);
            if (adapter == null)
            {
                Debug.LogError(
                    $"[{name}] 動画バックエンドを組み立てられませんでした。"
                    + "VRCAVProVideoPlayer / VRCUnityVideoPlayer が同じ GameObject にあるか確認してください。",
                    this);
                return null;
            }

            if (_warnWhenNoBakedUrls && host.Urls.Count == 0)
            {
                Debug.LogWarning(
                    $"[{name}] 焼き込み済み URL が 0 件です。このままでは再生できません。\n"
                    + "  Hierarchy でこの GameObject を選び、\n"
                    + "  Tools > Smart Media Platform > Bake Catalog Urls Into Selected Video Host\n"
                    + "  を実行してください(VRCUrl は実行時に作れないため、編集時の焼き込みが必要です)。",
                    this);
            }

            return adapter;
        }

        /// <summary>
        /// カタログの URL を <c>VRCUrl</c> へ焼き込む。<b>編集時専用</b>。
        /// <see cref="ICatalogProvider"/> が返すカタログと揃えたいときに使います。
        /// </summary>
        /// <returns>焼き込んだ件数。</returns>
        public int BakeFrom(IMediaCatalog catalog)
        {
            return Host != null ? Host.BakeUrlsFrom(catalog) : 0;
        }

        public string Describe()
        {
            var host = Host;
            if (host == null) return "VRChat 動画(Host なし)";

            return $"VRChat 動画({host.PlayerKind} / 焼き込み URL {host.Urls.Count} 件)";
        }

        public override string ToString() => $"VRChatMediaBackendProvider({Describe()})";
    }
}
