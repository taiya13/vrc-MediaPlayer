using System;
using System.Collections.Generic;

namespace SmartMediaPlatform.CatalogBuilder.YouTube
{
    /// <summary>
    /// <b>取り込むときにタグを自動で足す。</b>Phase7-9。
    ///
    /// <b>なぜ要るのか</b><br/>
    /// おすすめの精度は<b>タグの質でほぼ決まります</b>。
    /// ところが YouTube のタグは投稿者が自由に付けるもので、
    /// <list type="bullet">
    /// <item>付いていないことがある</item>
    /// <item>付いていても「YOASOBI」「ヨアソビ」のように<b>その曲固有</b>で、
    ///       別の曲と重ならない</item>
    /// </list>
    /// つまり<b>「似ている」を測る物差しとしては弱い</b>のです。
    /// ここでは<b>曲どうしで重なる言葉</b>を足します
    /// (「アニメ」「2020年代」「インスト」など)。
    /// 重なる言葉が増えるほど、おすすめは
    /// 「同じチャンネル」以外の手掛かりを持てるようになります。
    ///
    /// <b>足すだけで、消しません。</b>もとのタグはそのまま残します。
    ///
    /// <b>増やし方</b><br/>
    /// <see cref="Rules"/> に 1 行足すだけです。
    /// 「この言葉がどこかにあれば、このタグを付ける」以上のことはしません。
    /// 判定を賢くしたくなったら、その種類だけ専用のメソッドに切り出してください
    /// (年代と長さがすでにそうなっています)。
    /// </summary>
    public static class YouTubeAutoTagger
    {
        /// <summary>
        /// <b>言葉 → タグ</b>の対応表。
        ///
        /// 左の言葉が<b>見出し・説明・もとのタグのどこかに</b>あれば、
        /// 右のタグを付けます。大文字小文字は無視します。
        ///
        /// <b>順番に意味はありません。</b>当たったものは全部付きます。
        /// </summary>
        public static readonly string[][] Rules =
        {
            // ── 種類
            new[] { "アニメ", "アニメ" },
            new[] { "anime", "アニメ" },
            new[] { "主題歌", "アニメ" },
            new[] { "op主題歌", "アニメ" },
            new[] { "オープニング", "アニメ" },
            new[] { "エンディング", "アニメ" },
            new[] { "劇場版", "アニメ" },
            new[] { "tvアニメ", "アニメ" },

            new[] { "ゲーム", "ゲーム" },
            new[] { "game", "ゲーム" },
            new[] { "ost", "ゲーム" },
            new[] { "原神", "ゲーム" },
            new[] { "ボカロ", "ボカロ" },
            new[] { "vocaloid", "ボカロ" },
            new[] { "初音ミク", "ボカロ" },
            new[] { "utau", "ボカロ" },

            // ── 演奏の形
            new[] { "instrumental", "インスト" },
            new[] { "inst.", "インスト" },
            new[] { "インスト", "インスト" },
            new[] { "off vocal", "インスト" },
            new[] { "オフボーカル", "インスト" },
            new[] { "karaoke", "インスト" },
            new[] { "カラオケ", "インスト" },
            new[] { "bgm", "インスト" },
            new[] { "piano", "インスト" },
            new[] { "ピアノ", "インスト" },

            new[] { "band", "バンド" },
            new[] { "バンド", "バンド" },
            new[] { "ギター", "バンド" },
            new[] { "ドラム", "バンド" },
            new[] { "rock", "バンド" },

            new[] { "acoustic", "アコースティック" },
            new[] { "アコースティック", "アコースティック" },
            new[] { "cover", "カバー" },
            new[] { "カバー", "カバー" },
            new[] { "歌ってみた", "カバー" },
            new[] { "remix", "リミックス" },
            new[] { "リミックス", "リミックス" },
            new[] { "live", "ライブ" },
            new[] { "ライブ", "ライブ" },
            new[] { "ライヴ", "ライブ" },

            // ── ボーカル
            //    <b>ここはあまり当たりません。</b>投稿者が書いていることが少なく、
            //    音そのものからは判定できないためです。当たったときだけ足します。
            new[] { "女性ボーカル", "女性ボーカル" },
            new[] { "female vocal", "女性ボーカル" },
            new[] { "女性vo", "女性ボーカル" },
            new[] { "男性ボーカル", "男性ボーカル" },
            new[] { "male vocal", "男性ボーカル" },
            new[] { "男性vo", "男性ボーカル" },

            // ── 雰囲気
            new[] { "バラード", "バラード" },
            new[] { "ballad", "バラード" },
            new[] { "切ない", "切ない" },
            new[] { "泣ける", "切ない" },
            new[] { "chill", "チル" },
            new[] { "チル", "チル" },
            new[] { "lofi", "チル" },
            new[] { "lo-fi", "チル" },
            new[] { "relax", "チル" },
            new[] { "作業用", "作業用" },
            new[] { "応援", "元気" },
            new[] { "元気", "元気" },
            new[] { "夏", "夏" },
            new[] { "冬", "冬" },
            new[] { "夜", "夜" },
            new[] { "恋", "ラブソング" },
            new[] { "love song", "ラブソング" },
            new[] { "ラブソング", "ラブソング" },
        };

        /// <summary>
        /// この曲に足すタグを作る。<b>もとのタグは含めません</b>
        /// (呼び出し側が足し合わせます)。
        /// </summary>
        public static string[] Generate(
            string title, string description, string[] sourceTags,
            int durationSeconds, string publishedAt)
        {
            var found = new List<string>();

            string haystack = Haystack(title, description, sourceTags);

            for (int i = 0; i < Rules.Length; i++)
            {
                string needle = Rules[i][0];
                string tag = Rules[i][1];

                if (found.Contains(tag)) continue;
                if (haystack.IndexOf(needle, StringComparison.Ordinal) < 0) continue;

                found.Add(tag);
            }

            string decade = DecadeOf(publishedAt);
            if (decade.Length > 0 && !found.Contains(decade)) found.Add(decade);

            string length = LengthOf(durationSeconds);
            if (length.Length > 0 && !found.Contains(length)) found.Add(length);

            return found.ToArray();
        }

        /// <summary>
        /// <b>年代。</b>「2023-04-12T…」→「2020年代」。
        ///
        /// 年そのものではなく<b>年代</b>にするのは、
        /// 年で分けると<b>どの 2 曲も重ならなくなる</b>からです。
        /// タグは「重なってはじめて意味がある」ので、粗いほうが役に立ちます。
        /// </summary>
        public static string DecadeOf(string publishedAt)
        {
            if (string.IsNullOrEmpty(publishedAt) || publishedAt.Length < 4) return "";

            int year;
            if (!int.TryParse(publishedAt.Substring(0, 4), out year)) return "";
            if (year < 1950 || year > 2999) return "";

            return (year / 10 * 10) + "年代";
        }

        /// <summary>長さの目安。短すぎる / 長すぎるものだけ印を付けます。</summary>
        public static string LengthOf(int durationSeconds)
        {
            if (durationSeconds <= 0) return "";
            if (durationSeconds < 120) return "短め";
            if (durationSeconds >= 480) return "長め";
            return "";
        }

        /// <summary>探す対象を 1 本の小文字の文字列にまとめる。</summary>
        private static string Haystack(string title, string description, string[] sourceTags)
        {
            var text = new System.Text.StringBuilder();

            if (!string.IsNullOrEmpty(title)) text.Append(title).Append('\n');

            // 説明は長いので頭だけ見ます。
            // 末尾は歌詞やリンクの羅列で、当たっても意味がありません。
            if (!string.IsNullOrEmpty(description))
            {
                int take = description.Length < 400 ? description.Length : 400;
                text.Append(description, 0, take).Append('\n');
            }

            if (sourceTags != null)
            {
                for (int i = 0; i < sourceTags.Length; i++)
                {
                    if (string.IsNullOrEmpty(sourceTags[i])) continue;
                    text.Append(sourceTags[i]).Append('\n');
                }
            }

            return text.ToString().ToLowerInvariant();
        }

        /// <summary>もとのタグと自動タグを、重複なく足し合わせる。</summary>
        public static string[] Merge(string[] sourceTags, string[] generated, int limit)
        {
            var result = new List<string>();

            if (sourceTags != null)
            {
                for (int i = 0; i < sourceTags.Length && result.Count < limit; i++)
                {
                    string tag = sourceTags[i];
                    if (string.IsNullOrWhiteSpace(tag)) continue;

                    tag = tag.Trim();
                    if (Contains(result, tag)) continue;

                    result.Add(tag);
                }
            }

            if (generated != null)
            {
                for (int i = 0; i < generated.Length && result.Count < limit; i++)
                {
                    string tag = generated[i];
                    if (string.IsNullOrWhiteSpace(tag)) continue;
                    if (Contains(result, tag)) continue;

                    result.Add(tag);
                }
            }

            return result.ToArray();
        }

        private static bool Contains(List<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
