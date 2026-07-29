using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Store;
using SmartMediaPlatform.Library.Playback;
using SmartMediaPlatform.Session;
using UnityEngine;

namespace SmartMediaPlatform.World
{
    /// <summary>
    /// <b>組み立て済みの再生一式。</b>Prefab の部品はこれだけを受け取ります。
    ///
    /// <see cref="SmartMediaPlayerRoot"/> が 1 回だけ作り、
    /// Screen / Controller / UI へ <see cref="IMediaPlayerPart.Bind"/> で配ります。
    ///
    /// <b>部品はこの入れ物より奥を知りません。</b>
    /// カタログの作り方も、どのバックエンドが繋がっているかも見えないので、
    /// Catalog Builder やバックエンドが増えても<b>部品側は無変更</b>です。
    /// </summary>
    public sealed class MediaPlayerContext
    {
        public MediaPlayerContext(
            ICatalogStore store,
            PlaybackFlow flow,
            PlayerSession session,
            IBackendLogger logger)
        {
            Store = store;
            Flow = flow;
            Session = session;
            Logger = logger;
        }

        /// <summary>カタログを引く唯一の窓口(Phase4-2)。</summary>
        public ICatalogStore Store { get; }

        /// <summary>Library / 関連 / Queue / いま再生中(Phase4-3)。</summary>
        public PlaybackFlow Flow { get; }

        /// <summary>再生を任せているセッション。</summary>
        public PlayerSession Session { get; }

        /// <summary>ログ出力先。</summary>
        public IBackendLogger Logger { get; }
    }

    /// <summary>
    /// <b>Prefab の部品が実装する共通の口。</b>
    ///
    /// <see cref="SmartMediaPlayerRoot"/> が子から探して
    /// <see cref="Bind"/> を呼びます。部品はそこで受け取った
    /// <see cref="MediaPlayerContext"/> だけを使ってください。
    /// </summary>
    public interface IMediaPlayerPart
    {
        /// <summary>組み立てが終わったときに 1 回だけ呼ばれます。</summary>
        void Bind(MediaPlayerContext context);
    }

    /// <summary>
    /// <b>映像と音の出力先。</b>差し替え可能。
    ///
    /// 「どこに映すか」だけを答えます。動画プレイヤーの種類(AVPro / Unity 版)は
    /// <see cref="IMediaBackendProvider"/> の担当で、こちらは知りません。
    /// </summary>
    public interface IMediaScreen : IMediaPlayerPart
    {
        /// <summary>映像を映す Renderer。</summary>
        Renderer Surface { get; }

        /// <summary>音を出す AudioSource。</summary>
        AudioSource Speaker { get; }
    }

    /// <summary>
    /// <b>再生操作の窓口。</b>差し替え可能。
    ///
    /// UI はここだけを呼びます(<see cref="PlayerSession"/> を直接触りません)。
    /// おかげで UI を差し替えても操作の意味が変わらず、
    /// 操作を差し替えても UI が壊れません。
    ///
    /// <b>ここに再生の判断は入れないでください。</b>
    /// 次に何を再生するかは今までどおり <see cref="PlayerSession"/> の仕事で、
    /// この インターフェース は<b>呼び出しを中継するだけ</b>です。
    /// </summary>
    public interface IMediaController : IMediaPlayerPart
    {
        /// <summary>一覧で選んでいるものを再生する。</summary>
        bool PlaySelected();

        /// <summary>一覧の index 番目を選んで再生する。</summary>
        bool PlayAt(int index);

        /// <summary>関連一覧の index 番目を再生する。</summary>
        bool PlayRelatedAt(int index);

        /// <summary>Queue の index 番目へ飛ぶ。</summary>
        bool JumpInQueueTo(int index);

        /// <summary>選んでいるものを Queue の末尾へ足す。</summary>
        bool EnqueueSelected();

        /// <summary>選んでいるものを「次に再生」にする。</summary>
        bool PlayNextSelected();

        /// <summary>Queue の index 番目を外す。</summary>
        bool RemoveFromQueue(int index);

        bool TogglePlayPause();

        bool Next();

        bool Previous();

        bool Stop();
    }

    /// <summary>
    /// <b>画面。</b>差し替え可能。
    ///
    /// 既定は <see cref="MediaPlayerUI"/>(IMGUI の β 版)です。
    /// uGUI やワールド内 Canvas に差し替えるときは、
    /// この インターフェース を実装したコンポーネントを UI の子に置いてください。
    /// </summary>
    public interface IMediaPlayerUI : IMediaPlayerPart
    {
        /// <summary>表示する / しない。</summary>
        bool Visible { get; set; }
    }

    /// <summary>
    /// <b>どのバックエンドで再生するかを決める人。</b>差し替え可能。
    ///
    /// <b>ここがバックエンド追加時の唯一の差し込み口です。</b>
    /// <list type="bullet">
    /// <item><see cref="DummyMediaBackendProvider"/> … ログだけ(SDK 不要)</item>
    /// <item><c>VRChatMediaBackendProvider</c> … VRChat の動画プレイヤー(SDK 必須)</item>
    /// <item>将来 … 音楽・配信・別の動画プレイヤー</item>
    /// </list>
    /// 増やすときは実装を 1 つ足して Player の下へ置くだけで、
    /// <b>Prefab の他の部分も上位のコードも変わりません</b>。
    /// </summary>
    public interface IMediaBackendProvider
    {
        /// <summary>
        /// 再生に使うバックエンドを作る。
        /// 作れなければ null(<see cref="SmartMediaPlayerRoot"/> が代役を立てます)。
        /// </summary>
        /// <param name="screen">映像と音の出力先。要らなければ無視して構いません。</param>
        /// <param name="logger">ログ出力先。</param>
        IMediaBackend CreateBackend(IMediaScreen screen, IBackendLogger logger);

        /// <summary>Console 表示用の説明。</summary>
        string Describe();
    }

    /// <summary>
    /// <b>どのカタログを見せるかを決める人。</b>差し替え可能。
    ///
    /// <b>ここが Catalog Builder の差し込み口です。</b>
    /// いまは手書きの <c>IMediaCatalogSource</c>、
    /// 将来は Catalog Builder が生成した <c>Catalog.asset</c> を返すだけで、
    /// <b>Prefab も上位のコードも変わりません</b>。
    /// </summary>
    public interface ICatalogProvider
    {
        /// <summary>見せるカタログを作る。作れなければ null。</summary>
        IMediaCatalog CreateCatalog();

        /// <summary>Console 表示用の説明。</summary>
        string Describe();
    }
}
