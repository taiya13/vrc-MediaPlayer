using System.Collections.Generic;

namespace SmartMediaPlatform.CatalogBuilder.Genres
{
    /// <summary>
    /// <b>判定に使う材料。</b>Phase6-6。
    ///
    /// <b>取り込み元を問いません。</b>YouTube の言葉は 1 つも出てきません。
    /// <see cref="SourceCategory"/> も<b>ただの文字列</b>で、
    /// 「YouTube のカテゴリ番号」ではありません
    /// (番号を言葉に直すのは取り込み元の仕事)。
    ///
    /// おかげで JSON でも CSV でも、材料さえ詰めれば<b>同じ判定が効きます</b>。
    /// </summary>
    public sealed class GenreSignals
    {
        public string Title = "";
        public string Description = "";
        public string ChannelName = "";
        public string[] Tags = new string[0];

        /// <summary>
        /// 取り込み元が言っているカテゴリ(「音楽」「ゲーム」など)。
        ///
        /// <b>これだけではジャンルを決めません。</b>YouTube の「音楽」は
        /// <b>ほぼ全部の曲に付いている</b>ので、そのまま使うと
        /// カタログ全体が「音楽」1 色になります(Phase6-4 までがそうでした)。
        /// ここは<b>すでに点が入っているジャンルを少し押すだけ</b>に使います。
        /// </summary>
        public string SourceCategory = "";

        /// <summary>編集中の 1 件から材料を集める。</summary>
        public static GenreSignals FromDraftItem(CatalogDraftItem item)
        {
            var signals = new GenreSignals();
            if (item == null) return signals;

            signals.Title = item.Title;
            signals.ChannelName = item.Artist;
            signals.Tags = item.Tags;

            return signals;
        }

        /// <summary>集めた材料を 1 行で(調べるとき用)。</summary>
        public override string ToString()
        {
            return Title + " / " + ChannelName + " / タグ "
                   + (Tags == null ? 0 : Tags.Length) + " 個";
        }
    }

    /// <summary>ジャンル 1 つぶんの点。</summary>
    public sealed class GenreScore
    {
        public string Genre = "";
        public int Score;

        /// <summary>何が効いたか。「なぜこのジャンルになったか」を出すために持ちます。</summary>
        public string[] Reasons = new string[0];

        public override string ToString()
        {
            return Genre + " " + Score + " 点";
        }
    }

    /// <summary>外から来た手がかり。</summary>
    public sealed class GenreHint
    {
        public string Genre = "";
        public int Score;
        public string Reason = "";

        public GenreHint()
        {
        }

        public GenreHint(string genre, int score, string reason)
        {
            Genre = genre;
            Score = score;
            Reason = reason;
        }
    }

    /// <summary>
    /// <b>外から手がかりを足す口。</b>Phase6-6。<b>いまは実装がありません。</b>
    ///
    /// <b>なぜ先に空けておくのか</b><br/>
    /// MusicBrainz のような外部の音楽データベースを引ければ、
    /// 「このアーティストは何のジャンルか」が<b>辞書より正確に</b>分かります。
    /// ただし<b>通信が要る</b>ので、辞書と同じ場所には置けません
    /// (辞書は Unity にも通信にも依存しない純粋 C# のままにしておきたい)。
    ///
    /// ここを実装したものを <see cref="GenreClassifier.AddHintProvider"/> で足せば、
    /// <b>判定の仕組みそのものは 1 行も変えずに</b>賢くできます。
    /// 点の入れ方も辞書と同じ(足し合わせて一番高いものを採る)なので、
    /// <b>外部が落ちていても辞書だけで動きます</b>。
    /// </summary>
    public interface IGenreHintProvider
    {
        /// <summary>何が言っているか(「MusicBrainz」など)。理由に出します。</summary>
        string Name { get; }

        /// <summary>いま使えるか。通信できないときは false を返してください。</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// 手がかりを返す。<b>例外を投げないでください</b>
        /// (Phase6-1 で <c>ICatalogImporter</c> に決めた約束と同じ)。
        /// 分からなければ空の配列を返します。
        /// </summary>
        GenreHint[] Suggest(GenreSignals signals);
    }
}
