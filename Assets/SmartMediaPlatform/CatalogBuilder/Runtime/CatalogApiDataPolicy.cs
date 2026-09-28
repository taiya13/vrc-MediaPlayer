using System;

namespace SmartMediaPlatform.CatalogBuilder
{
    /// <summary>
    /// <b>API から取ったデータをいつまで持ってよいか。</b>Phase8。
    ///
    /// ───────────────────────────────────────────────
    /// <b>なぜ要るのか</b>
    ///
    /// YouTube API の規約は、API から取ったデータ(タイトル・チャンネル名・タグなど)を
    /// <b>原則 30 日を超えて保存しないこと</b>を求めています。
    /// 動画 ID や URL といった<b>識別子は例外</b>で、期限なく持てます。
    ///
    /// このシステムでは、データを<b>3 つの層</b>に分けました。
    ///
    /// <list type="number">
    /// <item><b>下書き(API 由来)</b> …… タイトル・チャンネル名・タグの<b>原本</b>。
    ///       Sidecar の JSON に、<b>取得日時つき</b>で置きます。
    ///       <b>ワールドには入りません。</b>ここがこのクラスの管轄です</item>
    ///
    /// <item><b>確定(作者のもの)</b> …… 画面に出す曲名・アーティスト・ジャンル・タグ。
    ///       Catalog に入り、ワールドと一緒に配布されます。
    ///       <b>作者が入力・編集した結果</b>であり、期限はありません</item>
    ///
    /// <item><b>持たない</b> …… サムネイル・再生数・説明文。取っても捨てます</item>
    /// </list>
    ///
    /// ───────────────────────────────────────────────
    /// <b>「確定を押せば API Data ではなくなる」とは考えていません</b>
    ///
    /// 確定したかどうかに関係なく、<b>①の原本は API 由来のまま</b>です。
    /// だから①には常に取得日時が付き、30 日で期限切れになり、
    /// <b>再取得するか削除するか</b>を選ぶことになります。
    ///
    /// ②は「作者が見て、選んで、必要なら書き換えたもの」として別に持ちます。
    /// <b>①を消しても②は残り、②を消しても①は残ります。</b>
    /// 2 つを別々に扱えることが、この設計のすべてです。
    /// </summary>
    public static class CatalogApiDataPolicy
    {
        /// <summary>API から取ったデータを持ってよい日数。</summary>
        public const int MaxAgeDays = 30;

        /// <summary>
        /// 期限が近いと知らせ始める日数。
        /// 切れてから気付くと、<b>取り込み直す時間がありません</b>。
        /// </summary>
        public const int WarnAgeDays = 23;

        /// <summary>取得日時を書くときの形。並べ替えられるよう、この形に固定します。</summary>
        public const string TimeFormat = "yyyy-MM-ddTHH:mm:ssZ";

        /// <summary>いまを <see cref="TimeFormat"/> の文字列にする。</summary>
        public static string NowStamp()
        {
            return DateTime.UtcNow.ToString(TimeFormat);
        }

        /// <summary>
        /// 取得日時の文字列を読む。読めなければ <c>false</c>。
        ///
        /// <b>読めないものは「取得日時が分からない」</b>として扱い、
        /// 期限切れと同じ扱いにします。分からないものを持ち続けるより、
        /// 取り直すほうが安全だからです。
        /// </summary>
        public static bool TryParseStamp(string stamp, out DateTime utc)
        {
            utc = default(DateTime);
            if (string.IsNullOrEmpty(stamp)) return false;

            return DateTime.TryParseExact(
                stamp, TimeFormat,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal
                    | System.Globalization.DateTimeStyles.AssumeUniversal,
                out utc);
        }

        /// <summary>取ってから何日たったか。分からなければ -1。</summary>
        public static int AgeInDays(string stamp, DateTime nowUtc)
        {
            DateTime taken;
            if (!TryParseStamp(stamp, out taken)) return -1;

            double days = (nowUtc - taken).TotalDays;
            if (days < 0d) return 0;          // 時計がずれている。0 日扱い。

            return (int)days;
        }

        /// <summary>
        /// <b>もう持っていてはいけないか。</b>
        /// 取得日時が分からないものも、期限切れとして扱います。
        /// </summary>
        public static bool IsExpired(string stamp, DateTime nowUtc)
        {
            int age = AgeInDays(stamp, nowUtc);
            if (age < 0) return true;

            return age >= MaxAgeDays;
        }

        /// <summary>期限が近いか(まだ切れてはいない)。</summary>
        public static bool IsExpiringSoon(string stamp, DateTime nowUtc)
        {
            int age = AgeInDays(stamp, nowUtc);
            if (age < 0) return false;        // 分からないものは「切れている」側

            return age >= WarnAgeDays && age < MaxAgeDays;
        }

        /// <summary>あと何日持てるか。切れていれば 0。</summary>
        public static int DaysLeft(string stamp, DateTime nowUtc)
        {
            int age = AgeInDays(stamp, nowUtc);
            if (age < 0) return 0;

            int left = MaxAgeDays - age;
            return left > 0 ? left : 0;
        }

        /// <summary>
        /// 画面に出す 1 行。
        /// <b>数字だけでなく、次に何をすればよいか</b>まで書きます。
        /// </summary>
        public static string Describe(string stamp, DateTime nowUtc)
        {
            int age = AgeInDays(stamp, nowUtc);

            if (age < 0) return "取得日時が分かりません。取り込み直すか、消してください。";
            if (age >= MaxAgeDays)
            {
                return age + " 日前に取得しました。期限切れです —— "
                       + "取り込み直すか、消してください。";
            }

            int left = MaxAgeDays - age;
            if (age >= WarnAgeDays) return age + " 日前に取得しました。あと " + left + " 日です。";

            return age + " 日前に取得しました。";
        }
    }
}
