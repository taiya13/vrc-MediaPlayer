namespace SmartMediaPlatform.World.UdonModel
{
    /// <summary>
    /// <b>名前を付けて取っておくプレイリストの棚。</b>Phase8-3。
    /// <c>UdonPlaylistShelf</c> が 1:1 で写しています。<b>変えるときは両方を直してください。</b>
    ///
    /// ───────────────────────────────────────────────
    /// <b>曲は「カタログの番号」ではなく「曲の ID」で覚えます</b>
    ///
    /// 番号は、ワールドの作者がカタログを作り直すと<b>ずれます</b>。
    /// 番号で覚えると、次に来たときに<b>別の曲が並びます</b>。
    /// ID なら、曲が消されていても「見つからない」と分かるだけで、ほかの曲は正しいままです。
    ///
    /// ───────────────────────────────────────────────
    /// <b>保存する文字の形</b>
    /// <code>
    /// SMPPL1
    /// 名前 [TAB] ID [TAB] ID …
    /// 名前 [TAB] ID …
    /// </code>
    /// 1 行目が目印、2 行目から 1 行 1 本です。
    /// 名前にも ID にも<b>制御文字(改行・TAB を含む)を入れさせない</b>ので、区切りと混ざりません。
    /// 読むときは<b>壊れた行だけを捨てて</b>、残りは読みます。
    /// 1 本が壊れていたせいで全部が消える、という道は作りません。
    ///
    /// ───────────────────────────────────────────────
    /// <b>ここは覚えるだけです。</b>保存先(VRChat の PlayerData)に書くのも、
    /// 再生予定へ積むのも、<c>UdonPlaylistShelf</c> と <c>UdonMediaController</c> の仕事です。
    /// </summary>
    public sealed class PlaylistShelfModel
    {
        /// <summary>取っておける本数。</summary>
        public const int MaxPlaylists = 20;

        /// <summary>1 本に入る曲数。再生予定の上限(64)と同じ。</summary>
        public const int MaxSongs = 64;

        /// <summary>名前の長さ(文字)。</summary>
        public const int MaxNameLength = 24;

        /// <summary>
        /// 保存する文字の上限。VRChat の PlayerData は<b>1 人 1 ワールド 100 KB</b>までで、
        /// 日本語は 1 文字 3 バイトになります。余裕を見てここで止めます。
        /// </summary>
        public const int MaxEncodedLength = 32000;

        /// <summary>1 行目の目印。形を変えるときは数字を上げる。</summary>
        public const string Header = "SMPPL1";

        /// <summary>自動で付ける名前の頭。</summary>
        public const string AutoNamePrefix = "プレイリスト ";

        /// <summary>保存しなかった:入れる曲が 1 曲も無い。</summary>
        public const int ResultNothingToSave = -1;

        /// <summary>保存しなかった:棚がいっぱい。</summary>
        public const int ResultFull = -2;

        /// <summary>保存しなかった:保存できる量を超える。</summary>
        public const int ResultTooLarge = -3;

        private const int LineBreak = 10;   // '\n'
        private const int Tab = 9;          // '\t'

        private string[] _names;
        private string[] _ids;      // 1 本ぶんの ID を TAB でつないだもの
        private int[] _counts;
        private int _count;
        private bool _initialized;

        private int _revision;
        private int _lastSavedSongs;
        private bool _lastTruncated;
        private int _skippedOnLoad;

        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            _names = new string[MaxPlaylists];
            _ids = new string[MaxPlaylists];
            _counts = new int[MaxPlaylists];
        }

        // ───────── 読み取り ─────────

        /// <summary>いま何本あるか。</summary>
        public int Count
        {
            get { return _count; }
        }

        /// <summary>中身が変わった回数。画面が「書き直すか」を決めるのに使う。</summary>
        public int Revision
        {
            get { return _revision; }
        }

        /// <summary>直前に保存した曲数(重なりを除いたあと)。</summary>
        public int LastSavedSongs
        {
            get { return _lastSavedSongs; }
        }

        /// <summary>直前の保存で、上限を超えたぶんを切り捨てたか。</summary>
        public bool LastTruncated
        {
            get { return _lastTruncated; }
        }

        /// <summary>直前の読み込みで、壊れていて捨てた行の数。</summary>
        public int SkippedOnLoad
        {
            get { return _skippedOnLoad; }
        }

        public bool IsFull
        {
            get { return _count >= MaxPlaylists; }
        }

        public string NameAt(int index)
        {
            if (index < 0 || index >= _count) return "";
            return _names[index];
        }

        /// <summary>覚えている曲の数(カタログに無くなった曲も数える)。</summary>
        public int SongCountAt(int index)
        {
            if (index < 0 || index >= _count) return 0;
            return _counts[index];
        }

        /// <summary><paramref name="index"/> 本目の曲の ID を、並びどおりに返す。</summary>
        public string[] IdsAt(int index)
        {
            if (index < 0 || index >= _count) return new string[0];

            string joined = _ids[index];
            string[] result = new string[_counts[index]];

            int filled = 0;
            int start = 0;
            int length = joined.Length;

            for (int i = 0; i <= length; i++)
            {
                if (i < length)
                {
                    int c = joined[i];
                    if (c != Tab) continue;
                }

                if (i > start && filled < result.Length)
                {
                    result[filled] = joined.Substring(start, i - start);
                    filled++;
                }
                start = i + 1;
            }

            // 数え違いがあっても、空の欄は返さない。
            if (filled == result.Length) return result;

            string[] trimmed = new string[filled];
            for (int i = 0; i < filled; i++) trimmed[i] = result[i];
            return trimmed;
        }

        /// <summary>同じ名前の位置。無ければ -1。前後の空白は無視します。</summary>
        public int FindByName(string name)
        {
            string wanted = SanitizeName(name);
            if (wanted.Length == 0) return -1;

            for (int i = 0; i < _count; i++)
            {
                if (_names[i] == wanted) return i;
            }
            return -1;
        }

        // ───────── 名前 ─────────

        /// <summary>
        /// <b>名前を整える。</b>制御文字を抜き、前後の空白を落とし、長すぎれば切ります。
        /// 絵文字の途中(サロゲートペアの片方)では切りません。
        /// </summary>
        public string SanitizeName(string raw)
        {
            if (raw == null) return "";

            string name = StripControl(raw).Trim();
            if (name.Length <= MaxNameLength) return name;

            int cut = MaxNameLength;

            // 切る位置の直前が上位サロゲートなら、その前で切る(半端な文字を残さない)。
            int last = name[cut - 1];
            if (last >= 0xD800 && last <= 0xDBFF) cut--;

            return name.Substring(0, cut).Trim();
        }

        /// <summary>ID を整える。制御文字だけを抜きます(空白は ID の一部かもしれないので残す)。</summary>
        public string SanitizeId(string raw)
        {
            if (raw == null) return "";
            return StripControl(raw);
        }

        /// <summary>
        /// 名前が空のときに付ける名前。<b>使われていない最小の番号</b>を選びます
        /// (「プレイリスト 2」を消したら、次は 2 が埋まる)。
        /// </summary>
        public string NextAutoName()
        {
            for (int n = 1; n <= MaxPlaylists + 1; n++)
            {
                string candidate = AutoNamePrefix + n;
                if (FindByName(candidate) < 0) return candidate;
            }

            // ここには来ない(本数の上限 + 1 までに必ず空きがある)。
            return AutoNamePrefix + (MaxPlaylists + 1);
        }

        /// <summary>
        /// <b>保存したら、どの名前になるか。</b>
        /// 打った名前があればそれ。無ければ、最後に読み込んだ(保存した)ものがまだ棚にあればそれ。
        /// どちらも無ければ空文字(= 新しい名前を自動で付ける)。
        /// </summary>
        public string SaveTargetName(string typed, string remembered)
        {
            string name = SanitizeName(typed);
            if (name.Length > 0) return name;

            int at = FindByName(remembered);
            if (at >= 0) return _names[at];

            return "";
        }

        /// <summary>保存ボタンに出す文字。押す前に「上書きになるか」が分かるようにします。</summary>
        public string SaveLabel(string target)
        {
            if (target == null || target.Length == 0) return "新しいプレイリストとして保存";
            if (FindByName(target) >= 0) return "「" + target + "」に上書き保存";
            return "「" + target + "」として保存";
        }

        // ───────── 変える ─────────

        /// <summary>
        /// <b>保存する。</b>同じ名前があれば<b>その場で上書き</b>、無ければ末尾に足します。
        /// 同じ曲が 2 回入っていれば 1 回にし、<see cref="MaxSongs"/> を超えたぶんは捨てます。
        /// </summary>
        /// <returns>保存した位置。保存しなかったら負の数(Result〜)。</returns>
        public int Save(string rawName, string[] ids, int idCount)
        {
            EnsureInitialized();

            _lastSavedSongs = 0;
            _lastTruncated = false;

            string[] unique = new string[MaxSongs];
            int count = 0;

            if (ids != null)
            {
                if (idCount > ids.Length) idCount = ids.Length;

                for (int i = 0; i < idCount; i++)
                {
                    string id = SanitizeId(ids[i]);
                    if (id.Length == 0) continue;
                    if (Contains(unique, count, id)) continue;

                    if (count >= MaxSongs)
                    {
                        _lastTruncated = true;
                        break;
                    }

                    unique[count] = id;
                    count++;
                }
            }

            if (count == 0) return ResultNothingToSave;

            string name = SanitizeName(rawName);
            if (name.Length == 0) name = NextAutoName();

            int at = FindByName(name);
            if (at < 0 && _count >= MaxPlaylists) return ResultFull;

            // ── いったん入れてみて、量を確かめる。超えたら元に戻す。
            bool appended = at < 0;
            string oldName = "";
            string oldIds = "";
            int oldCount = 0;

            if (appended)
            {
                at = _count;
                _count++;
            }
            else
            {
                oldName = _names[at];
                oldIds = _ids[at];
                oldCount = _counts[at];
            }

            _names[at] = name;
            _ids[at] = Join(unique, count);
            _counts[at] = count;

            if (Encode().Length > MaxEncodedLength)
            {
                if (appended)
                {
                    _count--;
                    _names[at] = null;
                    _ids[at] = null;
                    _counts[at] = 0;
                }
                else
                {
                    _names[at] = oldName;
                    _ids[at] = oldIds;
                    _counts[at] = oldCount;
                }

                _lastTruncated = false;
                return ResultTooLarge;
            }

            _lastSavedSongs = count;
            _revision++;
            return at;
        }

        /// <summary>消す。後ろのものは 1 つずつ前に詰めます。</summary>
        public bool Delete(int index)
        {
            if (index < 0 || index >= _count) return false;

            for (int i = index; i < _count - 1; i++)
            {
                _names[i] = _names[i + 1];
                _ids[i] = _ids[i + 1];
                _counts[i] = _counts[i + 1];
            }

            _count--;
            _names[_count] = null;
            _ids[_count] = null;
            _counts[_count] = 0;

            _revision++;
            return true;
        }

        /// <summary>全部消す(読み込み直す前など)。</summary>
        public void Clear()
        {
            EnsureInitialized();

            for (int i = 0; i < MaxPlaylists; i++)
            {
                _names[i] = null;
                _ids[i] = null;
                _counts[i] = 0;
            }

            _count = 0;
            _revision++;
        }

        // ───────── 保存する文字 ─────────

        /// <summary>棚全体を、保存する 1 本の文字にする。</summary>
        public string Encode()
        {
            string text = Header;

            for (int i = 0; i < _count; i++)
            {
                text += "\n" + _names[i] + "\t" + _ids[i];
            }

            return text;
        }

        /// <summary>
        /// <b>保存してあった文字から棚を作り直す。</b>
        /// 空なら空の棚。目印が違えば読まずに false(空の棚のまま)。
        /// 壊れた行は捨てて数え(<see cref="SkippedOnLoad"/>)、残りは読みます。
        /// </summary>
        public bool Decode(string data)
        {
            Clear();
            _skippedOnLoad = 0;

            if (data == null || data.Length == 0) return true;

            int length = data.Length;

            // 1 行目 = 目印
            int headerEnd = 0;
            while (headerEnd < length)
            {
                int c = data[headerEnd];
                if (c == LineBreak) break;
                headerEnd++;
            }

            string header = data.Substring(0, headerEnd);

            // Windows の改行(CR LF)で書き戻されていても読めるように、行末の CR は落とす。
            if (header.Length > 0)
            {
                int lastChar = header[header.Length - 1];
                if (lastChar == 13) header = header.Substring(0, header.Length - 1);
            }

            if (header != Header) return false;

            int start = headerEnd + 1;
            for (int i = start; i <= length; i++)
            {
                if (i < length)
                {
                    int c = data[i];
                    if (c != LineBreak) continue;
                }

                if (i > start) ReadLine(data, start, i);
                start = i + 1;
            }

            return true;
        }

        private void ReadLine(string data, int from, int to)
        {
            string[] unique = new string[MaxSongs];
            int count = 0;
            string name = "";
            bool haveName = false;

            int start = from;
            for (int i = from; i <= to; i++)
            {
                if (i < to)
                {
                    int c = data[i];
                    if (c != Tab) continue;
                }

                string field = data.Substring(start, i - start);
                start = i + 1;

                if (!haveName)
                {
                    haveName = true;
                    name = SanitizeName(field);
                    continue;
                }

                string id = SanitizeId(field);
                if (id.Length == 0) continue;
                if (count >= MaxSongs) continue;
                if (Contains(unique, count, id)) continue;

                unique[count] = id;
                count++;
            }

            // 名前が無い・曲が無い・同じ名前が先にある・棚がいっぱい …… は捨てる。
            if (name.Length == 0 || count == 0 || FindByName(name) >= 0 || _count >= MaxPlaylists)
            {
                _skippedOnLoad++;
                return;
            }

            _names[_count] = name;
            _ids[_count] = Join(unique, count);
            _counts[_count] = count;
            _count++;
        }

        // ───────── 内部 ─────────

        private string StripControl(string text)
        {
            int length = text.Length;

            bool clean = true;
            for (int i = 0; i < length; i++)
            {
                int c = text[i];
                if (c < 32 || c == 127) { clean = false; break; }
            }
            if (clean) return text;

            string result = "";
            int runStart = 0;

            for (int i = 0; i < length; i++)
            {
                int c = text[i];
                if (c >= 32 && c != 127) continue;

                if (i > runStart) result += text.Substring(runStart, i - runStart);
                runStart = i + 1;
            }

            if (length > runStart) result += text.Substring(runStart, length - runStart);
            return result;
        }

        private bool Contains(string[] values, int count, string value)
        {
            for (int i = 0; i < count; i++)
            {
                if (values[i] == value) return true;
            }
            return false;
        }

        private string Join(string[] values, int count)
        {
            string text = "";
            for (int i = 0; i < count; i++)
            {
                if (i > 0) text += "\t";
                text += values[i];
            }
            return text;
        }
    }
}
