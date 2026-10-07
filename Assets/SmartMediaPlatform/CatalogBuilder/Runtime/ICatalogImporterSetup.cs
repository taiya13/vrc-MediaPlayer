namespace SmartMediaPlatform.CatalogBuilder
{
    /// <summary>
    /// <b>使う前にひと手間いる取り込み元の口。</b>Phase7。<b>付けても付けなくても構いません。</b>
    ///
    /// <b>なぜ要るのか</b><br/>
    /// Phase6-5 まで、YouTube の API キーが未設定だと窓に
    /// <b>「窓の『設定を作る』を押してください」</b>とだけ出ていました。
    /// ところが<b>その窓もボタンも、もうありません</b>(Phase6-5 で
    /// プレビュー窓を Builder へ統合したときに一緒に消えました)。
    /// <b>存在しないボタンを押せと言われる</b>という、いちばん困る形の案内でした。
    ///
    /// <b>ここには GUI を持ち込みません。</b>持たせるのは
    /// 「何が要るか」「どこに入れるか」という<b>言葉と値だけ</b>です。
    /// 窓がそれを見て入力欄とボタンを描きます。
    /// おかげで
    /// <list type="bullet">
    /// <item>取り込み元は Unity の GUI を知らないまま</item>
    /// <item>窓は YouTube を知らないまま</item>
    /// </list>
    /// どちらの責務も動かさずに、まともな案内が出せます。
    ///
    /// <b>実装しない取り込み元(CSV など)はそのままで構いません。</b>
    /// 窓は <c>is</c> で見て、あるときだけ設定欄を出します。
    /// </summary>
    public interface ICatalogImporterSetup
    {
        /// <summary>もう使える状態か。false なら窓が設定欄を出します。</summary>
        bool IsConfigured { get; }

        /// <summary>設定欄の見出し(「YouTube の API キー」など)。</summary>
        string SetupTitle { get; }

        /// <summary>
        /// <b>ここに書いたとおりにやれば使える</b>という手順。1 行 1 手順。
        /// 「どこで作るか」「何を選ぶか」まで書いてください。
        /// </summary>
        string[] SetupSteps { get; }

        /// <summary>入れてもらう値の名前(「API キー」など)。</summary>
        string SecretLabel { get; }

        /// <summary>
        /// 入れてもらう値。窓は<b>伏せ字の欄</b>で受け取ります。
        /// 空なら未設定。書き込むと保存まで済ませてください。
        /// </summary>
        string SecretValue { get; set; }

        /// <summary>入れ物(設定アセットなど)がもう用意されているか。</summary>
        bool HasStorage { get; }

        /// <summary>入れ物を作る。作れたら true。</summary>
        bool CreateStorage();

        /// <summary>入れ物の場所。窓が「ここにあります」と出します。無ければ空文字。</summary>
        string StorageLocation { get; }

        /// <summary>設定を開いて見せる(Project 欄で選ぶなど)。できなければ何もしないでよい。</summary>
        void RevealStorage();
    }
}
