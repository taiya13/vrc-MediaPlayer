using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;
using SmartMediaPlatform.Catalog.Udon;

namespace SmartMediaPlatform.World.Udon
{
    /// <summary>
    /// <b>名前を付けて取っておくプレイリストの棚。</b>Phase8-3。
    ///
    /// ───────────────────────────────────────────────
    /// <b>どこに残るのか</b>
    ///
    /// VRChat の <b>PlayerData</b>(Persistence)に、その人の分だけ書きます。
    /// <list type="bullet">
    /// <item>ワールドを出ても、次に来たときに残っている</item>
    /// <item><b>他の人には見えません</b>(同期しません。棚は人それぞれ)</item>
    /// <item>書けるのは<b>自分の分だけ</b>で、1 人 1 ワールド 100 KB まで</item>
    /// </list>
    /// VRChat が保存データを配り終えた合図(<see cref="OnPlayerRestored"/>)より前は
    /// <b>読むことも書くこともできません</b>。その間は保存を断ります
    /// (先に書くと、残っていたものを空で上書きしてしまうため)。
    ///
    /// ───────────────────────────────────────────────
    /// <b>判断は持っていません</b>
    ///
    /// ここは「何を取っておいたか」を覚えて出し入れするだけです。
    /// 取り出した曲を<b>再生予定へ積むのは <see cref="UdonMediaController"/></b>、
    /// 次に何を流すかを決めるのは今までどおり <see cref="UdonPlayerSession"/> です。
    /// URL には触りません(曲は ID で覚え、番号に直すのはカタログです)。
    ///
    /// ───────────────────────────────────────────────
    /// <b>「覚える」部分は <c>SmartMediaPlatform.World.UdonModel.PlaylistShelfModel</c> の写しです。</b>
    /// あちらは純粋 C# で EditMode テスト済みです。<b>変えるときは両方を直してください。</b>
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonPlaylistShelf : UdonSharpBehaviour
    {
        /// <summary>PlayerData に書くときの名前。形を変えるときは末尾の数字を上げる。</summary>
        public const string StorageKey = "SmartMediaPlatform.Playlists.v1";

        /// <summary>読めなかったデータを、上書きする前に逃がしておく先。</summary>
        public const string UnreadableKey = "SmartMediaPlatform.Playlists.v1.unreadable";

        [Tooltip("曲の ID と番号を行き来するカタログ")]
        public UdonMediaCatalog Catalog;

        [Tooltip("保存するときに、いま鳴っている曲と再生予定を読む先")]
        public UdonPlayerSession Session;

        [Tooltip("VRChat の PlayerData に残す。切るとワールドを出たら消える")]
        public bool UsePersistence = true;

        [Tooltip("読み書きの結果を Console に出す")]
        public bool LogPersistence = true;

        // PlayerData が読めるようになったか(自分の OnPlayerRestored が来たか)。
        private bool _restored;

        // エディタで ClientSim を使わずに動かしている(LocalPlayer が無い)。
        // このときは保存先が無いので、覚えるだけで動かす。
        private bool _memoryOnly;

        // 一覧の 2 行目と長さに出す文字の控え。棚が変わったときだけ作り直す。
        // 毎回 ID を引き直すと、書き直し(0.5 秒ごと)のたびに 64 曲 × 行数ぶん探すことになる。
        private string[] _summaries;
        private string[] _durations;
        private int _cacheFor = -1;

        // 直近の解決(ID → 番号)で見つからなかった曲の数。
        private int _lastMissing;

        void Start()
        {
            EnsureInitialized();

            // エディタで ClientSim 無しに再生したときは、合図が来ない。
            // 保存先も無いので、覚えるだけで使えるようにしておく。
            if (Networking.LocalPlayer == null) _memoryOnly = true;
        }

        // ───────── 保存先(VRChat の PlayerData)─────────

        /// <summary>
        /// <b>保存データが届いた。</b>VRChat がプレイヤーごとに 1 回呼びます。
        /// 自分の分が届いたときだけ、棚を読み直します。
        /// </summary>
        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal) return;

            _restored = true;
            if (!UsePersistence) return;

            string data = "";
            if (PlayerData.HasKey(player, StorageKey))
            {
                data = PlayerData.GetString(player, StorageKey);
                if (data == null) data = "";
            }

            if (!Decode(data))
            {
                // ── 読めない形だった。<b>次の保存で上書きされる前に、別の場所へ逃がす。</b>
                //    消すことはできない(PlayerData の鍵は消せない)ので、1 か所だけ使います。
                PlayerData.SetString(UnreadableKey, data);
                Debug.LogWarning(
                    "[UdonPlaylistShelf] 保存してあったプレイリストが読めませんでした。"
                    + "上書きされないよう " + UnreadableKey + " に控えました。", gameObject);
                return;
            }

            if (LogPersistence)
            {
                Debug.Log("[UdonPlaylistShelf] " + Describe()
                          + (SkippedOnLoad > 0 ? "(壊れていた " + SkippedOnLoad + " 本は読み飛ばし)" : ""),
                          gameObject);
            }
        }

        /// <summary>
        /// <b>保存や削除ができる状態か。</b>
        /// 保存データが届く前に書くと、残っていたものを空で上書きしてしまうので、それまでは断ります。
        /// </summary>
        public bool IsReady
        {
            get { return _restored || _memoryOnly || !UsePersistence; }
        }

        /// <summary>まだ使えないときに出す文。</summary>
        public string NotReadyMessage()
        {
            return "保存してあるプレイリストを読み込み中です。少し待ってからもう一度押してください。";
        }

        private void Persist()
        {
            if (!UsePersistence || !_restored) return;
            if (Networking.LocalPlayer == null) return;

            PlayerData.SetString(StorageKey, Encode());
        }

        // ───────── 保存する / 消す(画面から呼ぶ)─────────

        /// <summary>
        /// <b>いま鳴っている曲 + 再生予定を、名前を付けて取っておく。</b>
        /// 同じ名前があれば上書きします。名前が空なら「プレイリスト N」。
        /// </summary>
        /// <returns>保存した位置。保存しなかったら負の数(Result〜)。</returns>
        public int SaveCurrent(string name)
        {
            if (!IsReady) return ResultNotReady;
            if (Session == null || Catalog == null) return ResultNothingToSave;

            int queued = Session.QueueCount;
            string[] ids = new string[queued + 1];
            int count = 0;

            int current = Session.CurrentIndex;
            if (current >= 0 && current < Catalog.Count)
            {
                ids[count] = Catalog.GetId(current);
                count++;
            }

            for (int i = 0; i < queued; i++)
            {
                int index = Session.GetQueueAt(i);
                if (index < 0 || index >= Catalog.Count) continue;

                ids[count] = Catalog.GetId(index);
                count++;
            }

            int result = Save(name, ids, count);
            if (result >= 0) Persist();
            return result;
        }

        /// <summary>消す。保存先からも消えます。</summary>
        public bool Remove(int index)
        {
            if (!IsReady) return false;
            if (!Delete(index)) return false;

            Persist();
            return true;
        }

        /// <summary>保存しなかった:保存データがまだ届いていない。</summary>
        public const int ResultNotReady = -4;

        // ───────── 取り出す ─────────

        /// <summary>
        /// <b><paramref name="index"/> 本目を、カタログの番号の並びにする。</b>
        /// カタログから消えた曲は飛ばし、その数を <see cref="LastMissing"/> に残します。
        /// 同じ曲が 2 つの ID で入っていても 1 回にします。
        /// </summary>
        public int[] Resolve(int index)
        {
            _lastMissing = 0;

            string[] ids = IdsAt(index);
            int[] found = new int[ids.Length];
            int count = 0;

            for (int i = 0; i < ids.Length; i++)
            {
                int catalogIndex = Catalog != null ? Catalog.FindIndexById(ids[i]) : -1;
                if (catalogIndex < 0)
                {
                    _lastMissing++;
                    continue;
                }

                bool seen = false;
                for (int k = 0; k < count; k++)
                {
                    if (found[k] == catalogIndex) { seen = true; break; }
                }
                if (seen) continue;

                found[count] = catalogIndex;
                count++;
            }

            if (count == found.Length) return found;

            int[] trimmed = new int[count];
            for (int i = 0; i < count; i++) trimmed[i] = found[i];
            return trimmed;
        }

        /// <summary>直近の <see cref="Resolve"/> で見つからなかった曲の数。</summary>
        public int LastMissing
        {
            get { return _lastMissing; }
        }

        /// <summary>
        /// 一覧の 2 行目。「12 曲 · 夜に駆ける / アイドル / …」。
        /// <b>棚が変わったときだけ作り直します</b>(毎回 ID を引き直すと重いため)。
        /// </summary>
        public string SummaryAt(int index)
        {
            if (index < 0 || index >= Count) return "";

            EnsureCache();
            if (_summaries[index] == null) _summaries[index] = BuildSummary(index);
            return _summaries[index];
        }

        /// <summary>全部の長さ(「1:02:03」「48:12」)。長さが分からない曲は数えない。</summary>
        public string TotalDurationAt(int index)
        {
            if (index < 0 || index >= Count) return "";

            EnsureCache();
            if (_durations[index] == null) _durations[index] = BuildDuration(index);
            return _durations[index];
        }

        private void EnsureCache()
        {
            if (_summaries != null && _cacheFor == Revision) return;

            _summaries = new string[MaxPlaylists];
            _durations = new string[MaxPlaylists];
            _cacheFor = Revision;
        }

        private string BuildDuration(int index)
        {
            if (Catalog == null) return "";

            int[] songs = Resolve(index);
            int seconds = 0;
            for (int i = 0; i < songs.Length; i++)
            {
                int length = Catalog.GetDurationSeconds(songs[i]);
                if (length > 0) seconds += length;
            }

            if (seconds <= 0) return "";

            int hours = seconds / 3600;
            int minutes = (seconds / 60) % 60;
            int rest = seconds % 60;

            string mm = minutes < 10 ? "0" + minutes : "" + minutes;
            string ss = rest < 10 ? "0" + rest : "" + rest;

            if (hours > 0) return hours + ":" + mm + ":" + ss;
            return minutes + ":" + ss;
        }

        private string BuildSummary(int index)
        {
            int[] songs = Resolve(index);
            int missing = _lastMissing;

            string text = songs.Length + " 曲";
            if (missing > 0) text += "(" + missing + " 曲は見つかりません)";

            if (Catalog == null) return text;

            // 頭の 3 曲だけ並べる。全部並べても読み切れない。
            int shown = songs.Length < 3 ? songs.Length : 3;
            for (int i = 0; i < shown; i++)
            {
                text += (i == 0 ? " · " : " / ") + Catalog.GetTitle(songs[i]);
            }
            if (songs.Length > shown) text += " / …";

            return text;
        }

        /// <summary>Console 表示用の 1 行。</summary>
        public string Describe()
        {
            string where = _memoryOnly || !UsePersistence
                ? "(保存なし・覚えるだけ)"
                : (_restored ? "" : "(保存データ待ち)");

            return "プレイリスト " + Count + " 本" + where;
        }

        // ───────── PlaylistShelfModel の写し(ここから下)─────────

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
