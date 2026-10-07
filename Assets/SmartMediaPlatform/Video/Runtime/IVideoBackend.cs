namespace SmartMediaPlatform.Video
{
    /// <summary>
    /// 動画プレイヤーからの通知。
    ///
    /// VRChat の動画プレイヤーが持つコールバック
    /// (OnVideoReady / OnVideoStart / OnVideoEnd / OnVideoError …)に対応する形にしてあります。
    /// <see cref="VideoBackendAdapter"/> がこれを受け取り、
    /// プラットフォーム共通の <see cref="Backend.BackendEvent"/> へ翻訳します。
    /// </summary>
    public interface IVideoBackendObserver
    {
        void OnVideoReady(string url);
        void OnVideoStart(string url);
        void OnVideoPause(string url);
        void OnVideoStop(string url);
        void OnVideoEnd(string url);
        void OnVideoError(string url, string message);
    }

    /// <summary>
    /// 動画バックエンドの契約。<b>動画プレイヤーの語彙で書かれています。</b>
    ///
    /// メソッド名は MusicBackend と揃えてありますが、扱う型が違います:
    ///  - 対象は <c>MediaItem</c> ではなく <b>URL 文字列</b>
    ///  - 状態は <c>BackendState</c> ではなく <see cref="VideoPlayerState"/>
    ///  - <see cref="Seek"/> は 0〜1 の割合ではなく <b>秒</b>
    ///  - 通知は <see cref="Backend.IBackendObserver"/> ではなく <see cref="IVideoBackendObserver"/>
    ///
    /// この差を吸収するのが <see cref="VideoBackendAdapter"/> です。
    /// おかげで <b>VRChat の VideoPlayer に依存するコードはこの契約の実装側だけ</b>に閉じ込められ、
    /// MediaPlayer / PlayerSession / Queue / Recommendation / Catalog は動画のことを知りません。
    ///
    /// 将来は <c>VRCUnityVideoPlayer</c> / <c>VRCAVProVideoPlayer</c> を包む実装を
    /// この インターフェース に対して用意するだけで差し替わります
    /// (本フェーズでは実装しません)。
    /// </summary>
    public interface IVideoBackend
    {
        /// <summary>バックエンド名(ログと診断用)。</summary>
        string Name { get; }

        /// <summary>その URL を再生できるか。</summary>
        bool CanPlay(string url);

        /// <summary>URL を読み込む。実機では非同期なので、完了は通知で受け取る。</summary>
        bool Load(string url);

        bool Play();

        bool Pause();

        bool Resume();

        bool Stop();

        /// <summary>再生位置を<b>秒</b>で指定して移動する。</summary>
        bool Seek(float seconds);

        VideoPlayerState GetState();

        /// <summary>いま読み込んでいる URL。無ければ null。</summary>
        string GetCurrentUrl();

        /// <summary>現在の再生位置(秒)。</summary>
        float GetTime();

        /// <summary>動画全体の長さ(秒)。分からなければ 0。</summary>
        float GetDuration();

        /// <summary>シークできるか(生配信では false になる)。</summary>
        bool CanSeek { get; }

        void AddObserver(IVideoBackendObserver observer);

        void RemoveObserver(IVideoBackendObserver observer);
    }
}
