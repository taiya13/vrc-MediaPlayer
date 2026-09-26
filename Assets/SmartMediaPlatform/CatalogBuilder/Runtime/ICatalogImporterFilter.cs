namespace SmartMediaPlatform.CatalogBuilder
{
    /// <summary>
    /// <b>「これは取り込まない」を持つ取り込み元の口。</b>Phase7-3。
    /// <b>付けても付けなくても構いません。</b>
    ///
    /// <b>なぜ要るのか</b><br/>
    /// YouTube のチャンネルをまるごと取り込むと、<b>ショート動画が大量に混ざります</b>。
    /// ショートは 1 分前後・縦長なので、ワールドの大画面では
    /// 左右が黒いまますぐ次へ飛び、再生がほぼ成立しません。
    /// しかも<b>あとから 1 本ずつ消すのは現実的ではない</b>ので、
    /// 取り込みの<b>入り口</b>で外す必要があります。
    ///
    /// <b>窓は「何を外すか」を知りません。</b>
    /// ここが渡すのは<b>言葉と入切だけ</b>で、
    /// 「ショートとは何か」「何秒以下か」は取り込み元の側にあります。
    /// おかげで
    /// <list type="bullet">
    /// <item>Builder の窓は YouTube を知らないまま</item>
    /// <item>取り込み元は Unity の GUI を知らないまま</item>
    /// </list>
    /// 押す前に見える設定を出せます。
    /// <see cref="ICatalogImporterSetup"/> と同じ考え方です。
    ///
    /// <b>覚えておくのは実装側の仕事</b>です。窓は入切を渡すだけなので、
    /// 次に開いたときも同じ状態にしたいなら、設定アセットなどへ自分で書いてください。
    /// </summary>
    public interface ICatalogImporterFilter
    {
        /// <summary>チェック欄に出す言葉(「ショート動画は取り込まない」など)。</summary>
        string FilterLabel { get; }

        /// <summary>
        /// チェックが入っているときに、その下に出す 1 行。
        /// <b>何が外れるのかを具体的に</b>書いてください(「60 秒以下と #shorts」など)。
        /// </summary>
        string FilterHint { get; }

        /// <summary>外すかどうか。窓がそのまま読み書きします。</summary>
        bool FilterEnabled { get; set; }
    }
}
