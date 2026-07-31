namespace SmartMediaPlatform.CatalogBuilder.YouTube
{
    /// <summary>
    /// <b>YouTube の再生時間(ISO 8601)を秒へ。</b>Phase6-2。
    ///
    /// API は <c>PT4M13S</c> のような形で返します。
    /// <c>P1DT2H3M4S</c>(日をまたぐ配信)、<c>PT1H</c>(分秒なし)、
    /// <c>P0D</c>(配信中で長さ未定)なども来るので、まとめてここで扱います。
    ///
    /// <b>ネットワークに触りません。</b>文字列だけなので EditMode で検証できます。
    /// </summary>
    public static class YouTubeDurationParser
    {
        /// <summary>
        /// 秒に直す。読めなければ 0(例外は投げない)。
        ///
        /// <b>0 は「長さ不明」として扱ってください。</b>
        /// 生放送は長さを返さないので、失敗と区別できません。
        /// </summary>
        public static int ToSeconds(string iso8601)
        {
            if (string.IsNullOrWhiteSpace(iso8601)) return 0;

            string text = iso8601.Trim().ToUpperInvariant();
            if (!text.StartsWith("P")) return 0;

            int total = 0;
            int number = 0;
            bool hasNumber = false;
            bool inTime = false;

            for (int i = 1; i < text.Length; i++)
            {
                char c = text[i];

                if (c == 'T')
                {
                    inTime = true;
                    number = 0;
                    hasNumber = false;
                    continue;
                }

                if (c >= '0' && c <= '9')
                {
                    number = number * 10 + (c - '0');
                    hasNumber = true;
                    continue;
                }

                if (!hasNumber) return 0;   // 単位だけが来るのは壊れた文字列

                // T の前は日付、後ろは時刻。M が「月」と「分」で重なるのでここで分ける。
                if (c == 'D') total += number * 86400;
                else if (c == 'W') total += number * 604800;
                else if (c == 'H' && inTime) total += number * 3600;
                else if (c == 'M' && inTime) total += number * 60;
                else if (c == 'S' && inTime) total += number;
                else if (c == 'Y' || c == 'M') return 0;   // 年月は長さとして扱わない
                else return 0;

                number = 0;
                hasNumber = false;
            }

            return total;
        }
    }
}
