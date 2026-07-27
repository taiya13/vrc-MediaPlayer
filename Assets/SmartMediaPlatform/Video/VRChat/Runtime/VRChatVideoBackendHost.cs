using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using UnityEngine;
using VRC.SDK3.Components.Video;
using VRC.SDK3.Video.Components.Base;

namespace SmartMediaPlatform.Video.VRChat
{
    /// <summary>
    /// <see cref="VRChatVideoBackend"/> をシーン上で動かすための入れ物。
    /// Audio 層の <c>AudioBackendHost</c> と同じ役割・同じ形にしてあります。
    ///
    /// 役割は 4 つだけです:
    /// <list type="number">
    /// <item>シーンの VRCUnityVideoPlayer / VRCAVProVideoPlayer を包んで Backend を組み立てる</item>
    /// <item>組み立てた Backend を <see cref="VideoBackendAdapter"/> に載せて上位へ渡す</item>
    /// <item>毎フレーム <see cref="VRChatVideoBackend.Tick"/> を呼び、読み込み完了と再生終了を拾う</item>
    /// <item>実機の動画コールバックを Backend へ中継する</item>
    /// </list>
    ///
    /// <b>ロジックはこのクラスに入れていません</b>(テストできなくなるため)。
    /// 状態機械はすべて <see cref="VRChatVideoBackend"/>(純粋 C#)側にあります。
    ///
    /// <b>コールバックについて</b><br/>
    /// 実機(Udon)では動画プレイヤーは<b>同じ GameObject の UdonBehaviour</b> へ
    /// <c>OnVideoReady</c> / <c>OnVideoEnd</c> / <c>OnVideoError</c> …を送ります。
    /// UdonSharp は インターフェース を扱えないため、その中継は Phase3-2(Udon 版)の課題です。
    /// それまでの間、および Unity エディタでの確認では、
    /// <see cref="Update"/> のポーリング(<see cref="VRChatVideoBackend.Tick"/>)が
    /// 読み込み完了・再生終了・タイムアウトを検出します。
    /// コールバックを繋げられる場合は、下の <c>OnVideoXxx</c> をそのまま呼んでください
    /// (プッシュとポーリングのどちらから来ても通知は 1 回だけです)。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VRChatVideoBackendHost : MonoBehaviour
    {
        [Header("VRChat 動画プレイヤー")]
        [Tooltip("VRCUnityVideoPlayer または VRCAVProVideoPlayer。未設定なら同じ GameObject から探す")]
        [SerializeField] private BaseVRCVideoPlayer _videoPlayer;

        [Header("バックエンド設定")]
        [SerializeField] private string _backendName = "VRChatVideoBackend";

        [SerializeField] private string _adapterName = "VideoAdapter";

        [Tooltip("読み込みがこの秒数を超えたら Error にする(0 で無効)")]
        [SerializeField] private float _loadTimeoutSeconds = 20f;

        [Header("ベイク済み URL(実行時に VRCUrl は作れない)")]
        [Tooltip("Tools > Smart Media Platform > Bake Catalog Urls Into Selected Video Host で焼き込む")]
        [SerializeField] private VRCUrlTable _urls = new VRCUrlTable();

        /// <summary>組み立て済みの動画バックエンド。</summary>
        public VRChatVideoBackend Backend { get; private set; }

        /// <summary>
        /// 上位へ渡すアダプタ。<c>BackendManager.RegisterBackend</c> /
        /// <c>PlayerSession.RegisterBackend</c> にそのまま登録できます。
        /// <b>DummyVideoBackend を包んでいたときと同じ型・同じ使い方</b>です。
        /// </summary>
        public VideoBackendAdapter Adapter { get; private set; }

        /// <summary>ベイク済み URL 表。</summary>
        public VRCUrlTable Urls => _urls;

        public BaseVRCVideoPlayer VideoPlayer => _videoPlayer;

        private void Awake()
        {
            EnsureBuilt();
        }

        private void Update()
        {
            // 読み込み完了・タイムアウト・再生終了を拾う。
            Backend?.Tick(Time.deltaTime);
        }

        /// <summary>
        /// バックエンドとアダプタを組み立てる(まだなら)。
        /// <c>AudioBackendHost.EnsureBuilt</c> と同じく、Awake より前に呼ばれても動くよう
        /// 明示的に呼べるようにし、後からロガーを差し替えられるようにしてある。
        /// </summary>
        public VideoBackendAdapter EnsureBuilt(IBackendLogger logger = null)
        {
            if (Adapter != null)
            {
                if (logger != null) Adapter.SetLogger(logger);
                return Adapter;
            }

            if (_videoPlayer == null) _videoPlayer = GetComponent<BaseVRCVideoPlayer>();
            if (_videoPlayer == null)
            {
                Debug.LogError(
                    $"[{name}] VRCUnityVideoPlayer / VRCAVProVideoPlayer が設定されていません。"
                    + "Inspector の Video Player に割り当てるか、同じ GameObject に追加してください。");
                return null;
            }

            var bridge = new VRCVideoPlayerBridge(_videoPlayer, _urls);

            Backend = new VRChatVideoBackend(_backendName, bridge, _urls, logger)
            {
                LoadTimeoutSeconds = _loadTimeoutSeconds,
            };

            // ここが Phase3-1 の要点:
            // アダプタの引数が DummyVideoBackend から VRChatVideoBackend に変わるだけで、
            // VideoBackendAdapter も、その上のすべても一切変更していない。
            Adapter = new VideoBackendAdapter(_adapterName, Backend, logger);
            return Adapter;
        }

        /// <summary>
        /// カタログの動画 URL を焼き込む。<b>編集時専用</b>(実行時は何もしない)。
        /// </summary>
        /// <returns>焼き込んだ件数。</returns>
        public int BakeUrlsFrom(IMediaCatalog catalog, params MediaType[] types)
        {
#if UNITY_EDITOR
            if (catalog == null) return 0;
            int count = _urls.Bake(catalog, types);
            Debug.Log($"[{name}] Catalog から {count} 件の URL を VRCUrl へ焼き込みました。");
            return count;
#else
            Debug.LogWarning($"[{name}] VRCUrl は実行時に生成できません。編集時に焼き込んでください。");
            return 0;
#endif
        }

        // ───────── 実機の動画コールバックの受け口 ─────────
        // メソッド名は VRChat の動画イベントに合わせてあります。
        // Udon 側から中継する場合はそのままこれらを呼んでください。

        public void OnVideoReady() => Backend?.NotifyVideoReady();

        public void OnVideoStart() => Backend?.NotifyVideoStart();

        public void OnVideoPlay() => Backend?.NotifyVideoPlay();

        public void OnVideoPause() => Backend?.NotifyVideoPause();

        public void OnVideoEnd() => Backend?.NotifyVideoEnd();

        public void OnVideoLoop() => Backend?.NotifyVideoLoop();

        /// <summary>実機の <c>OnVideoError(VideoError)</c>。SDK の enum をこちらの enum へ翻訳する。</summary>
        public void OnVideoError(VideoError videoError)
        {
            Backend?.NotifyVideoError(Translate(videoError));
        }

        /// <summary>
        /// SDK の <see cref="VideoError"/> を <see cref="VideoErrorKind"/> へ写す。
        /// <b>SDK の型が出てくるのはこのブリッジ層だけ</b>で、
        /// Backend も Adapter も上位も SDK を知りません。
        /// </summary>
        public static VideoErrorKind Translate(VideoError videoError)
        {
            switch (videoError)
            {
                case VideoError.InvalidURL: return VideoErrorKind.InvalidUrl;
                case VideoError.AccessDenied: return VideoErrorKind.AccessDenied;
                case VideoError.PlayerError: return VideoErrorKind.PlayerError;
                case VideoError.RateLimited: return VideoErrorKind.RateLimited;
                default: return VideoErrorKind.Unknown;
            }
        }
    }
}
