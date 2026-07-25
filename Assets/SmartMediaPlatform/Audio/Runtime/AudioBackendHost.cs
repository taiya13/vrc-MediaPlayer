using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using UnityEngine;

namespace SmartMediaPlatform.Audio
{
    /// <summary>
    /// <see cref="AudioBackend"/> をシーン上で動かすための入れ物。
    ///
    /// 役割は 3 つだけ:
    ///  1. AudioSource を用意して <see cref="AudioBackend"/> を組み立てる
    ///  2. Inspector で設定した AudioClip を対応表に登録する
    ///  3. 毎フレーム <see cref="AudioBackend.Tick"/> を呼び、曲の終わりを検出させる
    ///
    /// Backend 自体のロジックはこのクラスに入れていない(テストできなくなるため)。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class AudioBackendHost : MonoBehaviour
    {
        [Header("バックエンド設定")]
        [SerializeField] private string _backendName = "AudioBackend";

        [Tooltip("Catalog の ID と AudioClip の対応。ここに本物の音源を割り当てる")]
        [SerializeField] private AudioClipEntry[] _clips = new AudioClipEntry[0];

        [Header("音源が未設定のときの補助")]
        [Tooltip("音源ファイルが無くても動作を確認できるよう、ID ごとに短い音を自動生成する")]
        [SerializeField] private bool _generateProceduralClips = true;

        [Tooltip("自動生成する音の長さ(秒)")]
        [SerializeField] private float _proceduralClipSeconds = 1.5f;

        private AudioSource _audioSource;

        /// <summary>組み立て済みのバックエンド。BackendManager にそのまま登録できる。</summary>
        public AudioBackend Backend { get; private set; }

        public AudioClipLibrary Library { get; private set; }

        public AudioSource Source => _audioSource;

        private void Awake()
        {
            EnsureBuilt();
        }

        private void Update()
        {
            // 曲が最後まで再生されたら Ended が通知される。
            Backend?.Tick();
        }

        /// <summary>
        /// バックエンドを組み立てる(まだなら)。
        /// Awake 前に他のコンポーネントから参照されても動くよう、明示的に呼べるようにしてある。
        ///
        /// Awake はロガーが用意される前に呼ばれるため、既に組み立て済みでも
        /// logger が指定された場合はそれを差し替える(ログが握りつぶされないようにするため)。
        /// </summary>
        public AudioBackend EnsureBuilt(IBackendLogger logger = null)
        {
            if (Backend != null)
            {
                if (logger != null) Backend.SetLogger(logger);
                return Backend;
            }

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();

            Library = new AudioClipLibrary(_clips);
            Backend = new AudioBackend(
                _backendName,
                new UnityAudioSourcePlayer(_audioSource),
                Library,
                logger,
                MediaType.Music);

            return Backend;
        }

        /// <summary>
        /// カタログの Music を対応表に揃える。
        /// Inspector で音源が割り当てられていない ID には、必要なら短い音を自動生成する。
        /// </summary>
        /// <returns>自動生成した件数。</returns>
        public int PrepareClipsFor(IMediaCatalog catalog)
        {
            EnsureBuilt();
            if (!_generateProceduralClips || catalog == null) return 0;

            return ProceduralClipFactory.FillLibrary(
                Library, catalog, _proceduralClipSeconds, MediaType.Music);
        }
    }
}
