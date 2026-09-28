namespace SmartMediaPlatform.CatalogBuilder
{
    /// <summary>
    /// <b>取り込み方を選べる取り込み元の口。</b>Phase7-4。
    /// <b>付けても付けなくても構いません。</b>
    ///
    /// <b>なぜ要るのか</b><br/>
    /// YouTube は「新しい順」と「再生数の多い順」で取れます。
    /// どちらを使うかは<b>押す前に決めるもの</b>なので、取得ボタンの near に出したい。
    /// けれど<b>窓は YouTube を知りません</b>(知ってしまうと、
    /// 取り込み元を足すたびに窓を直すことになります)。
    ///
    /// そこで、窓が受け取るのは<b>名前と番号だけ</b>にします。
    /// 「新着順とは何か」「何件取るか」を知っているのは取り込み元の側です。
    /// <see cref="ICatalogImporterSetup"/> / <see cref="ICatalogImporterFilter"/> と
    /// 同じ考え方です。
    ///
    /// <b>覚えておくのは実装側の仕事</b>です。窓は選ばれた番号を渡すだけなので、
    /// 次に開いたときも同じ状態にしたいなら、設定アセットなどへ自分で書いてください。
    /// </summary>
    public interface ICatalogImporterModes
    {
        /// <summary>
        /// 選べる取り込み方の名前(「新着順」「人気順」)。
        /// <b>2 つ未満なら窓は何も出しません</b> —— 選べないものを見せない。
        /// </summary>
        string[] ModeNames { get; }

        /// <summary>いま選ばれている取り込み方(<see cref="ModeNames"/> の添字)。</summary>
        int Mode { get; set; }

        /// <summary>
        /// いまの取り込み方の説明。空なら出しません。
        /// <b>使えない組み合わせがあるならここに書いてください</b>
        /// (「再生リストでは使えません」など)。
        /// </summary>
        string ModeHint { get; }

        /// <summary>
        /// 件数の選択肢(「20 件」「50 件」…)。
        /// <b>いまの取り込み方で選べないなら null</b>。窓は何も出しません。
        /// </summary>
        string[] AmountLabels { get; }

        /// <summary>いま選ばれている件数(<see cref="AmountLabels"/> の添字)。</summary>
        int AmountIndex { get; set; }
    }
}
