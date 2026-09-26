using System.Collections.Generic;

namespace SmartMediaPlatform.CatalogBuilder
{
    /// <summary>
    /// <b>取り込み元 1 つぶんの口。</b>Phase6-1 では<b>形だけ</b>で、実装は Phase6-2 以降です。
    ///
    /// <b>なぜ先に決めておくのか</b><br/>
    /// YouTube / JSON / CSV / スプレッドシート …… と足すたびに窓の作りを直すことになるのを
    /// 避けるためです。窓が知るのは<b>この口だけ</b>で、
    /// 取り込み元が何本増えても<b>窓のコードは 1 行も変わりません</b>。
    ///
    /// <b>実装するときの約束</b>
    /// <list type="bullet">
    /// <item><b>例外を投げない。</b>失敗は <see cref="CatalogImportResult"/> に入れて返す
    ///       (窓は取り込み元ごとの事情を知らないので、拾って表示するしかない)</item>
    /// <item><b>できたものだけ返す。</b>作りかけがあっても
    ///       <see cref="CatalogDraft"/> 側が落とすので、無理に埋めなくてよい</item>
    /// <item><b>混ぜ方は決めない。</b>足すか上書きかは使う人が窓で選ぶ</item>
    /// <item><b>ID を自分で一意にしなくてよい。</b>重なりは <c>CatalogDraft</c> がずらす</item>
    /// </list>
    ///
    /// <b>入力の形は実装ごとに違います。</b>URL 1 本、ファイルパス、貼り付けたテキスト ……
    /// どれも「1 本の文字列」に収まるので、<see cref="Import"/> は文字列 1 つで受けます。
    /// 収まらないもの(認証など)は実装側が設定を持ってください。
    /// </summary>
    public interface ICatalogImporter
    {
        /// <summary>窓のボタンに出す名前(「YouTube 再生リスト」「CSV ファイル」など)。</summary>
        string DisplayName { get; }

        /// <summary>入力欄の上に出す説明。何を貼ればよいかを 1 行で。</summary>
        string InputHint { get; }

        /// <summary>
        /// いま使えるか。
        /// API キー未設定・ネットワーク不可などで使えないときは false を返し、
        /// 理由を <see cref="UnavailableReason"/> に入れてください。
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>使えない理由。使えるときは空文字。</summary>
        string UnavailableReason { get; }

        /// <summary>
        /// <paramref name="input"/> が扱える形か(ボタンを押す前の判定)。
        /// 重い処理はここでしないでください。
        /// </summary>
        bool CanImport(string input);

        /// <summary>
        /// 取り込む。<b>例外は投げず</b>、失敗も結果に入れて返します。
        /// </summary>
        CatalogImportResult Import(string input);
    }

    /// <summary>
    /// <b>取り込みの結果。</b>成功も失敗も同じ形で返します。
    ///
    /// 「例外を投げない」ことにしたので、
    /// <b>窓は取り込み元ごとの事情を知らずに結果を表示できます</b>。
    /// </summary>
    public sealed class CatalogImportResult
    {
        private CatalogImportResult(bool ok, string message, IReadOnlyList<CatalogDraftItem> items)
        {
            Ok = ok;
            Message = message ?? "";
            Items = items ?? new CatalogDraftItem[0];
        }

        public bool Ok { get; private set; }

        /// <summary>窓に出す 1 行。成功なら「12 件を読み込みました」など。</summary>
        public string Message { get; private set; }

        public IReadOnlyList<CatalogDraftItem> Items { get; private set; }

        public int Count { get { return Items.Count; } }

        /// <summary>
        /// 取り込み元の人が読む名前(「○○チャンネル」「お気に入り」など)。Phase6-5。
        /// <b>空でも構いません。</b>窓は貼られた文字列で代わりにします。
        /// </summary>
        public string SourceName = "";

        /// <summary>
        /// <c>CatalogSubscription.KindChannel</c> など。Phase6-5。
        /// <b>空でも構いません。</b>
        /// </summary>
        public string SourceKind = "";

        /// <summary>
        /// <b>取り切れなかった見込みがあるか。</b>Phase6-5。
        /// 新着だけ取ったときに「上限まで全部が新しかった」場合に立てます。
        /// 窓が「もう一度押してください」と案内します。
        /// </summary>
        public bool MayHaveMore;

        public static CatalogImportResult Success(IReadOnlyList<CatalogDraftItem> items, string message)
        {
            return new CatalogImportResult(true, message, items);
        }

        /// <summary>取り込み元の名前と種類を添えて返す。</summary>
        public CatalogImportResult From(string sourceName, string sourceKind)
        {
            SourceName = sourceName ?? "";
            SourceKind = sourceKind ?? "";
            return this;
        }

        public static CatalogImportResult Failure(string message)
        {
            return new CatalogImportResult(false, message, null);
        }
    }
}
