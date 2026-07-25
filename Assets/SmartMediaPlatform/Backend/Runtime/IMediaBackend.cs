using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Backend
{
    /// <summary>
    /// Queue と Player の間に位置する共通 Backend API。
    ///
    /// Music / Video / Live / Podcast のどれであっても、上位はこの同一 API だけで扱う。
    /// メディア種別ごとの違いは <see cref="CanPlay"/> で表明し、
    /// 実際の再生手段(AudioSource / VideoPlayer / VRCVideoPlayer)は
    /// この インターフェースを実装する Phase2 以降の具象クラスが持つ。
    /// この層は再生手段を一切知らない。
    ///
    /// 状態遷移は <see cref="BackendState"/> を参照。
    /// 各操作は「実行できたか」を bool で返し、不正な遷移では状態を変えずに false を返す
    /// (例外を投げないので、上位は状態を気にせず安全に呼べる)。
    /// </summary>
    public interface IMediaBackend
    {
        /// <summary>バックエンド名(ログと診断用)。</summary>
        string Name { get; }

        /// <summary>
        /// このバックエンドが対象メディアを扱えるか。
        /// 種別ごとにバックエンドを分ける(Music / Video / Live …)ための選択条件であり、
        /// 拡張性の中心となる API。
        /// </summary>
        bool CanPlay(MediaItem item);

        /// <summary>
        /// メディアを読み込む。成功すると <see cref="BackendState.Ready"/> になる。
        /// 扱えないメディアや null の場合は false(状態は変えない)。
        /// </summary>
        bool Load(MediaItem item);

        /// <summary>
        /// 再生を開始する。Ready / Stopped / Ended から呼べる。
        /// 一時停止からの再開は <see cref="Resume"/> を使う。
        /// </summary>
        bool Play();

        /// <summary>再生中のみ一時停止できる。</summary>
        bool Pause();

        /// <summary>一時停止中のみ再開できる。</summary>
        bool Resume();

        /// <summary>停止して先頭に戻す。読み込みは保持する。</summary>
        bool Stop();

        /// <summary>
        /// 現在のメディアを中断する(利用者の意思によるスキップ)。
        /// Backend 自身は次に何を再生するかを知らないため、
        /// 「次へ進む」判断は <see cref="BackendManager"/> など上位が行う。
        /// </summary>
        bool Skip();

        /// <summary>現在読み込んでいるメディア。無ければ null。</summary>
        MediaItem GetCurrent();

        /// <summary>現在の状態。</summary>
        BackendState GetState();

        /// <summary>通知の受け取りを開始する。</summary>
        void AddObserver(IBackendObserver observer);

        /// <summary>通知の受け取りをやめる。</summary>
        void RemoveObserver(IBackendObserver observer);
    }
}
