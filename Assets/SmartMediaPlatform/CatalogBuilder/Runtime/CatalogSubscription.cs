using System;
using System.Collections.Generic;

namespace SmartMediaPlatform.CatalogBuilder
{
    /// <summary>
    /// <b>見張っている取り込み元 1 つ。</b>Phase6-5。
    /// チャンネルでもプレイリストでも、<b>同じ形</b>で持ちます。
    ///
    /// <b>なぜ要るのか</b><br/>
    /// チャンネルを追いかけていると、月に何度も取り込み直すことになります。
    /// 毎回 400 件を全部取ると<b>数十秒かかり、API の割り当ても食いつぶします</b>。
    /// 「前回どこまで取ったか」を覚えておけば、<b>新着だけを 1 ページで</b>取れます。
    ///
    /// <b>取り込み元を問いません。</b>持っているのは
    /// 「何を」(<see cref="Input"/>)「いつ」(<see cref="LastFetchedUtc"/>)
    /// 「どこまで」(<see cref="LastItemId"/>)の 3 つだけで、
    /// YouTube 固有のものは 1 つもありません。
    /// JSON / CSV の取り込みを足しても、そのまま使えます。
    ///
    /// <b>ワールドには入りません。</b>これは編集のための覚え書きなので、
    /// <c>Catalog.asset</c> ではなく<b>横に置く JSON</b> に書きます
    /// (<c>CatalogSidecarIO</c>)。
    /// </summary>
    [Serializable]
    public sealed class CatalogSubscription
    {
        /// <summary>チャンネルまるごと。</summary>
        public const string KindChannel = "channel";

        /// <summary>プレイリスト。</summary>
        public const string KindPlaylist = "playlist";

        /// <summary>1 本だけ。</summary>
        public const string KindSingle = "single";

        /// <summary>取り込み元の名前(<c>ICatalogImporter.DisplayName</c>)。</summary>
        public string Source = "";

        /// <summary>貼られた文字列そのもの。もう一度取るときはこれを渡します。</summary>
        public string Input = "";

        /// <summary>人が見る名前。チャンネル名やプレイリスト名。</summary>
        public string DisplayName = "";

        /// <summary><see cref="KindChannel"/> / <see cref="KindPlaylist"/> / <see cref="KindSingle"/>。</summary>
        public string Kind = KindChannel;

        /// <summary>最後に取りに行った時刻(UTC・ISO8601)。空なら 1 度も取っていない。</summary>
        public string LastFetchedUtc = "";

        /// <summary>
        /// 最後の取り込みで<b>いちばん新しかったもの</b>の ID。
        /// 次に取るときは<b>これに当たった時点で止められます</b>。
        /// </summary>
        public string LastItemId = "";

        /// <summary>これまでにこの取り込み元から入れた件数(のべ)。</summary>
        public int ImportedCount;

        /// <summary>前回の取り込みで増えた件数。</summary>
        public int LastNewCount;

        /// <summary>この取り込み元を見張り続けるか。切っても消さずに残せます。</summary>
        public bool Enabled = true;

        /// <summary>取り込み元 + 入力で一意。同じものを 2 度登録しないために使います。</summary>
        public string Key
        {
            get { return (Source ?? "") + "\n" + Normalize(Input); }
        }

        /// <summary>1 度でも取ったか。</summary>
        public bool HasFetched
        {
            get { return !string.IsNullOrWhiteSpace(LastFetchedUtc); }
        }

        public string KindLabel
        {
            get
            {
                if (Kind == KindPlaylist) return "プレイリスト";
                if (Kind == KindSingle) return "単発";
                return "チャンネル";
            }
        }

        /// <summary>「2026-08-02 14:30」の形。1 度も取っていなければ「未取得」。</summary>
        public string FormatLastFetched()
        {
            if (!HasFetched) return "未取得";

            DateTime parsed;
            if (!DateTime.TryParse(
                    LastFetchedUtc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out parsed))
            {
                return LastFetchedUtc;
            }

            return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }

        /// <summary>いま取ったことにする。</summary>
        public void MarkFetched(string newestItemId, int newCount, string nowUtcIso)
        {
            LastFetchedUtc = nowUtcIso;
            LastNewCount = newCount;
            ImportedCount += newCount;

            // 1 件も無かったときは前の目印を残す。上書きすると
            // 「どこまで取ったか」を見失って、次に全件取り直すことになる。
            if (!string.IsNullOrWhiteSpace(newestItemId)) LastItemId = newestItemId;
        }

        public static string Normalize(string input)
        {
            return input == null ? "" : input.Trim();
        }
    }

    /// <summary>
    /// <b>見張っている取り込み元の一覧。</b>Phase6-5。
    ///
    /// <b>同じものを 2 度登録しません。</b>取り込み元 + 入力が同じなら、
    /// 既にあるものを返して更新します(同じチャンネルの行が並ぶのを防ぐため)。
    /// </summary>
    [Serializable]
    public sealed class CatalogSubscriptionBook
    {
        private readonly List<CatalogSubscription> _items = new List<CatalogSubscription>();

        public int Count { get { return _items.Count; } }

        public IReadOnlyList<CatalogSubscription> Items { get { return _items; } }

        public CatalogSubscription GetAt(int index)
        {
            if (index < 0 || index >= _items.Count) return null;
            return _items[index];
        }

        public CatalogSubscription Find(string source, string input)
        {
            string key = (source ?? "") + "\n" + CatalogSubscription.Normalize(input);

            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Key == key) return _items[i];
            }
            return null;
        }

        /// <summary>
        /// 登録する。<b>すでにあれば、そのものを返します</b>(2 度は増えません)。
        /// 名前と種類は新しいほうで上書きします。
        /// </summary>
        public CatalogSubscription Register(
            string source, string input, string displayName, string kind)
        {
            CatalogSubscription found = Find(source, input);

            if (found == null)
            {
                found = new CatalogSubscription();
                found.Source = source ?? "";
                found.Input = CatalogSubscription.Normalize(input);
                _items.Add(found);
            }

            if (!string.IsNullOrWhiteSpace(displayName)) found.DisplayName = displayName;
            if (!string.IsNullOrWhiteSpace(kind)) found.Kind = kind;

            return found;
        }

        public bool Remove(CatalogSubscription subscription)
        {
            return subscription != null && _items.Remove(subscription);
        }

        public bool RemoveAt(int index)
        {
            if (index < 0 || index >= _items.Count) return false;

            _items.RemoveAt(index);
            return true;
        }

        public void Clear()
        {
            _items.Clear();
        }

        public void Add(CatalogSubscription subscription)
        {
            if (subscription == null) return;
            if (Find(subscription.Source, subscription.Input) != null) return;

            _items.Add(subscription);
        }

        /// <summary>まだ見張っているものだけ。</summary>
        public CatalogSubscription[] Active()
        {
            var result = new List<CatalogSubscription>();

            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Enabled) result.Add(_items[i]);
            }
            return result.ToArray();
        }
    }
}
