namespace SmartMediaPlatform.CatalogBuilder.YouTube
{
    /// <summary>
    /// <b>YouTube のカテゴリ番号 → ジャンル名。</b>Phase6-4。
    ///
    /// <b>なぜ表を持つのか</b><br/>
    /// カテゴリ名を取るには <c>videoCategories</c> をもう 1 回叩く必要がありますが、
    /// <b>番号は世界共通で固定</b>です。表にしておけば通信が 1 回減り、
    /// API キーの割り当ても減りません。
    ///
    /// <b>ジャンルは絞り込みと関連の材料になります。</b>
    /// 空のままだと <see cref="RelatedIdGenerator"/> の「同じジャンル」が
    /// 1 件も効かないので、取り込んだ時点で埋めます。
    ///
    /// 知らない番号なら<b>空文字</b>を返します(勝手な名前を付けない)。
    /// </summary>
    public static class YouTubeCategoryNames
    {
        public static string Of(string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId)) return "";

            switch (categoryId.Trim())
            {
                case "1": return "映画・アニメ";
                case "2": return "自動車";
                case "10": return "音楽";
                case "15": return "ペット・動物";
                case "17": return "スポーツ";
                case "19": return "旅行・イベント";
                case "20": return "ゲーム";
                case "22": return "ブログ・人物";
                case "23": return "コメディ";
                case "24": return "エンタメ";
                case "25": return "ニュース・政治";
                case "26": return "ハウツー・スタイル";
                case "27": return "教育";
                case "28": return "科学技術";
                case "29": return "非営利・社会活動";
                default: return "";
            }
        }
    }
}
