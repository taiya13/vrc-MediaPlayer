using System;
using System.Collections.Generic;
using SmartMediaPlatform.Catalog.Store;

namespace SmartMediaPlatform.Library
{
    /// <summary>
    /// <b>Phase4-2 の中心。「いま見ているものに関連するもの」の一覧。</b>
    ///
    /// <code>
    /// 起点の MediaId
    ///   → IRelatedMediaProvider   … 関連する ID の並びを返す(ここがサーバー差し替え点)
    ///   → ICatalogStore           … ID を DisplayMeta に変換する(唯一の窓口)
    ///   → RelatedMediaView        … 並べて、選べるようにする
    ///   → UI                      … DisplayMeta を描く / PlayableRef を再生系へ渡す
    /// </code>
    ///
    /// <b>この クラス は「関連の決め方」を知りません。</b>
    /// <see cref="IRelatedMediaProvider"/> が返した ID の順番をそのまま並べるだけです。
    /// だから関連の出どころが
    /// <list type="bullet">
    /// <item>カタログの <c>RelatedIds</c>(いま)</item>
    /// <item>Catalog Builder が事前計算したもの(将来)</item>
    /// <item>サーバーが返した ID(将来)</item>
    /// </list>
    /// のどれに変わっても、<b>このクラスも UI も 1 行も変わりません。</b>
    ///
    /// <b>カタログの内部構造も知りません。</b>
    /// 触るのは <see cref="ICatalogStore"/> だけで、
    /// <c>MediaItem</c> も <c>IMediaCatalog</c> も URL も出てきません。
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public sealed class RelatedMediaView : MediaListViewBase
    {
        private readonly IRelatedMediaProvider _related;

        private string _sourceId;
        private int _maxCount;

        /// <param name="store">表示情報を引く唯一の窓口。</param>
        /// <param name="related">関連 ID を答える人。</param>
        /// <param name="maxCount">並べる最大件数。0 以下で制限なし。</param>
        public RelatedMediaView(
            ICatalogStore store,
            IRelatedMediaProvider related,
            int maxCount = 0)
            : base(store)
        {
            _related = related ?? throw new ArgumentNullException(nameof(related));
            _maxCount = maxCount;

            Rebuild(notify: false);
        }

        // ───────── 起点 ─────────

        /// <summary>いま何に対する関連を出しているか。未設定なら null。</summary>
        public string SourceMediaId => _sourceId;

        /// <summary>起点そのものの表示情報。未設定・カタログに無ければ null。</summary>
        public DisplayMeta SourceMeta =>
            _sourceId != null ? Store.GetDisplayMeta(_sourceId) : null;

        /// <summary>並べる最大件数。0 以下で制限なし。</summary>
        public int MaxCount
        {
            get => _maxCount;
            set
            {
                if (_maxCount == value) return;
                _maxCount = value;
                Rebuild(notify: true);
            }
        }

        /// <summary>関連 ID の出どころ(診断用)。</summary>
        public IRelatedMediaProvider Provider => _related;

        /// <summary>
        /// <b>起点を変える。</b>再生中の曲が変わったらこれを呼びます。
        ///
        /// <code>
        /// // 例: 再生が切り替わったら関連一覧を追従させる
        /// view.SetSource(session.CurrentMediaId);
        /// </code>
        /// </summary>
        /// <param name="mediaId">起点にする MediaId。null で一覧を空にする。</param>
        /// <returns>並んだ件数。</returns>
        public int SetSource(string mediaId)
        {
            string normalized = string.IsNullOrWhiteSpace(mediaId) ? null : mediaId;
            if (Same(_sourceId, normalized) && _sourceId != null) return Count;

            _sourceId = normalized;
            Rebuild(notify: true);
            return Count;
        }

        /// <summary>
        /// 起点を変えずに並べ直す。
        /// <b>サーバーから新しい関連 ID が届いたとき</b>に呼びます
        /// (<see cref="StaticRelatedMediaProvider.Set"/> のあとなど)。
        /// </summary>
        public void Refresh()
        {
            Rebuild(notify: true);
        }

        // ───────── 何を並べるか ─────────

        protected override void BuildEntries(List<DisplayMeta> into)
        {
            if (_sourceId == null) return;

            // 1. 関連する ID を聞く(この層は「なぜ関連なのか」を知らない)
            var ids = _related.GetRelatedIds(_sourceId, _maxCount);
            if (ids == null || ids.Count == 0) return;

            // 2. ID を表示情報に変える(唯一の窓口を通す)
            //    カタログに無い ID は Store が黙って落とすので、
            //    サーバーが古い ID を返しても残りはそのまま並びます。
            var metas = Store.GetDisplayMetas(ids);

            for (int i = 0; i < metas.Count; i++)
            {
                // 起点そのものが混ざっていたら外す(「関連」に自分は要らない)
                if (Same(metas[i].MediaId, _sourceId)) continue;

                into.Add(metas[i]);
                if (_maxCount > 0 && into.Count >= _maxCount) break;
            }
        }

        public override string ToString()
        {
            return $"RelatedMediaView(起点 {_sourceId ?? "なし"} / {Count} 件 / {_related})";
        }
    }
}
