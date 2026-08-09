using System.Collections.Generic;

namespace SmartMediaPlatform.CatalogBuilder
{
    /// <summary>
    /// <b>「どこまで取ればよいか」の伝え方。</b>Phase6-5。
    ///
    /// 取り込み元は新しい順に返してくるのが普通なので、
    /// <b>知っている ID に当たった時点で止められます</b>。
    /// 400 件のチャンネルでも、新着 3 件なら 1 ページで済みます。
    /// </summary>
    public sealed class CatalogImportBoundary
    {
        /// <summary>前回いちばん新しかったもの。これに当たったら止めてよい。</summary>
        public string StopAtId = "";

        /// <summary>
        /// すでにカタログに入っている ID。
        /// <see cref="StopAtId"/> の動画が消されていた場合の保険です。
        /// </summary>
        public string[] KnownIds = new string[0];

        /// <summary>これ以上は取らない。0 なら取り込み元の上限に任せる。</summary>
        public int MaxNewItems;

        /// <summary>知っているものか。</summary>
        public bool IsKnown(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;

            string wanted = id.Trim();
            if (!string.IsNullOrWhiteSpace(StopAtId) && StopAtId.Trim() == wanted) return true;

            if (KnownIds == null) return false;

            for (int i = 0; i < KnownIds.Length; i++)
            {
                if (KnownIds[i] != null && KnownIds[i].Trim() == wanted) return true;
            }
            return false;
        }

        /// <summary>いま持っているカタログから作る。</summary>
        public static CatalogImportBoundary From(
            CatalogSubscription subscription, IReadOnlyList<CatalogDraftItem> known)
        {
            var boundary = new CatalogImportBoundary();

            if (subscription != null) boundary.StopAtId = subscription.LastItemId;

            if (known != null)
            {
                var ids = new List<string>();
                for (int i = 0; i < known.Count; i++)
                {
                    if (known[i] == null || string.IsNullOrWhiteSpace(known[i].Id)) continue;
                    ids.Add(known[i].Id.Trim());
                }
                boundary.KnownIds = ids.ToArray();
            }

            return boundary;
        }
    }

    /// <summary>
    /// <b>新着だけ取れる取り込み元。</b>Phase6-5。<b>付けても付けなくても構いません。</b>
    ///
    /// <b>なぜ <see cref="ICatalogImporter"/> を増やさなかったのか</b><br/>
    /// 増やすと、JSON も CSV も<b>使わないメソッドを書かされます</b>。
    /// 別の口に分けておけば、
    /// <list type="bullet">
    /// <item>差分が取れる取り込み元(YouTube)…… これも実装する</item>
    /// <item>差分の意味が無い取り込み元(CSV)…… <see cref="ICatalogImporter"/> だけでよい</item>
    /// </list>
    /// となり、<b>どちらも Builder からは同じように見えます</b>。
    /// Builder は「実装していたら使う、していなければ全部取って既存を飛ばす」だけです。
    /// </summary>
    public interface IIncrementalCatalogImporter
    {
        /// <summary>
        /// 新着だけ取る。
        /// <paramref name="boundary"/> が知っている ID に当たったら、そこで止めてよい。
        /// </summary>
        CatalogImportResult ImportNew(string input, CatalogImportBoundary boundary);
    }
}
