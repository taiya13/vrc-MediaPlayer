using System.Collections.Generic;

namespace SmartMediaPlatform.CatalogBuilder
{
    /// <summary>
    /// <b>一覧のまとまり。</b>Phase6-4。
    ///
    /// チャンネルを丸ごと取り込むと 200 件を超えることがあり、
    /// <b>平らに並べると何が入っているのか分からなくなります</b>。
    /// ここでまとめ直して、たたんだり開いたりできるようにします。
    ///
    /// <b>YouTube 専用ではありません。</b>まとめる鍵は
    /// <see cref="CatalogDraftItem.Artist"/> / <see cref="CatalogDraftItem.Genre"/> /
    /// <see cref="CatalogDraftItem.Source"/> のどれかで、
    /// <b>どの取り込み元でも同じように効きます</b>。
    /// JSON/CSV を足しても、このクラスは 1 行も変わりません。
    ///
    /// <b>並べ替えません。</b>持っているのは<b>元の位置の番号</b>だけで、
    /// まとまりの順も「最初に出てきた順」です。
    /// 取り込んだ順が保たれるので、<b>あとから元の一覧と見比べられます</b>。
    /// </summary>
    public sealed class CatalogItemGrouping
    {
        /// <summary>チャンネル(= アーティスト)ごと。YouTube の既定。</summary>
        public const int ByChannel = 0;

        /// <summary>ジャンルごと。</summary>
        public const int ByGenre = 1;

        /// <summary>取り込み元ごと(「YouTube」「手入力」など)。</summary>
        public const int BySource = 2;

        /// <summary>まとめない(全部を 1 つのまとまりに入れる)。</summary>
        public const int Flat = 3;

        /// <summary>鍵が空のものを入れる先の名前。</summary>
        public const string UnknownName = "(未設定)";

        private readonly List<string> _names = new List<string>();
        private readonly List<List<int>> _members = new List<List<int>>();
        private readonly List<bool> _expanded = new List<bool>();

        private int _mode = ByChannel;

        /// <summary>まとめ方。変えたら <see cref="Build"/> をやり直してください。</summary>
        public int Mode
        {
            get { return _mode; }
            set { _mode = value; }
        }

        public int GroupCount { get { return _names.Count; } }

        /// <summary>まとめる意味があるか(2 つ以上に分かれたか)。</summary>
        public bool IsMeaningful { get { return _names.Count > 1; } }

        /// <summary>
        /// まとめ直す。<b>開いている / たたんでいるは名前で引き継ぎます</b> —
        /// 取り込み直すたびに全部開くと、たたんだ意味が無くなるためです。
        /// </summary>
        public void Build(IReadOnlyList<CatalogDraftItem> items)
        {
            var wasCollapsed = new List<string>();
            for (int i = 0; i < _names.Count; i++)
            {
                if (!_expanded[i]) wasCollapsed.Add(_names[i]);
            }

            _names.Clear();
            _members.Clear();
            _expanded.Clear();

            if (items == null) return;

            for (int i = 0; i < items.Count; i++)
            {
                CatalogDraftItem item = items[i];
                if (item == null) continue;

                string key = KeyOf(item);

                int group = _names.IndexOf(key);
                if (group < 0)
                {
                    _names.Add(key);
                    _members.Add(new List<int>());
                    _expanded.Add(!wasCollapsed.Contains(key));
                    group = _names.Count - 1;
                }

                _members[group].Add(i);
            }
        }

        public void Clear()
        {
            _names.Clear();
            _members.Clear();
            _expanded.Clear();
        }

        // ───────── 中身を見る ─────────

        public string GetName(int group)
        {
            if (group < 0 || group >= _names.Count) return "";
            return _names[group];
        }

        /// <summary>そのまとまりに入っている<b>元の位置</b>。</summary>
        public int[] GetIndices(int group)
        {
            if (group < 0 || group >= _members.Count) return new int[0];
            return _members[group].ToArray();
        }

        public int GetCount(int group)
        {
            if (group < 0 || group >= _members.Count) return 0;
            return _members[group].Count;
        }

        /// <summary><paramref name="itemIndex"/> がどのまとまりに入っているか。無ければ -1。</summary>
        public int GroupOf(int itemIndex)
        {
            for (int g = 0; g < _members.Count; g++)
            {
                if (_members[g].Contains(itemIndex)) return g;
            }
            return -1;
        }

        // ───────── 開閉 ─────────

        public bool IsExpanded(int group)
        {
            if (group < 0 || group >= _expanded.Count) return false;
            return _expanded[group];
        }

        public void SetExpanded(int group, bool value)
        {
            if (group < 0 || group >= _expanded.Count) return;
            _expanded[group] = value;
        }

        public void ToggleExpanded(int group)
        {
            SetExpanded(group, !IsExpanded(group));
        }

        public void ExpandAll()
        {
            for (int i = 0; i < _expanded.Count; i++) _expanded[i] = true;
        }

        public void CollapseAll()
        {
            for (int i = 0; i < _expanded.Count; i++) _expanded[i] = false;
        }

        // ───────── 内部 ─────────

        private string KeyOf(CatalogDraftItem item)
        {
            if (_mode == Flat) return "すべて";

            string value;
            if (_mode == ByGenre) value = item.Genre;
            else if (_mode == BySource) value = item.Source;
            else value = item.Artist;

            if (string.IsNullOrWhiteSpace(value)) return UnknownName;
            return value.Trim();
        }
    }
}
