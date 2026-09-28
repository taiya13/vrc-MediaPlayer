using System.Collections.Generic;

namespace SmartMediaPlatform.Catalog.Store
{
    /// <summary>
    /// <b>カタログから何かを取り出す、唯一の窓口。</b>
    ///
    /// Phase4-2 の中心となる契約です。UI も再生系も
    /// <b>ここ以外からカタログの中身を取りません</b>。
    ///
    /// <b>この インターフェース が <c>MediaItem</c> を返さないのが要点です。</b>
    /// 返すのは <see cref="DisplayMeta"/>(表示用)と <see cref="PlayableRef"/>(再生用)
    /// と ID の並びだけ。<c>MediaItem</c> も <c>IMediaCatalog</c> も外へ出ないので、
    /// <b>利用側はカタログの内部構造を知りません</b>。
    ///
    /// おかげで、カタログの作り方が変わっても利用側は無傷です。
    /// <list type="bullet">
    /// <item>手書きの <c>IMediaCatalogSource</c>(いま)</item>
    /// <item>Catalog Builder が生成した <c>Catalog.asset</c>(将来)</item>
    /// <item>サーバーから受け取ったデータ(将来)</item>
    /// </list>
    /// どれになっても、変わるのは <see cref="CatalogStore"/> に何を渡すかだけです。
    ///
    /// 純粋 C#(UnityEngine 非依存)。
    /// </summary>
    public interface ICatalogStore
    {
        /// <summary>登録されている件数。</summary>
        int Count { get; }

        /// <summary>その ID がカタログにあるか。</summary>
        bool Contains(string mediaId);

        /// <summary>
        /// 表示用の情報を取り出す。無ければ null。
        /// <b>URL は含まれません。</b>
        /// </summary>
        DisplayMeta GetDisplayMeta(string mediaId);

        /// <summary>例外を投げずに取り出す。</summary>
        bool TryGetDisplayMeta(string mediaId, out DisplayMeta meta);

        /// <summary>
        /// ID の並びをまとめて表示用に変換する。
        /// <b>カタログに無い ID は黙って落とします</b>
        /// (サーバーが古い ID を返しても UI が壊れないように)。
        /// </summary>
        IReadOnlyList<DisplayMeta> GetDisplayMetas(IReadOnlyList<string> mediaIds);

        /// <summary>
        /// 再生系へ渡す参照を取り出す。無ければ <see cref="PlayableRef.None"/>。
        /// </summary>
        PlayableRef GetPlayableRef(string mediaId);

        /// <summary>登録されているすべての ID(カタログの登録順)。</summary>
        IReadOnlyList<string> GetAllIds();

        /// <summary>その種別の ID だけ(カタログの登録順)。</summary>
        IReadOnlyList<string> GetIdsByType(MediaType type);
    }
}
