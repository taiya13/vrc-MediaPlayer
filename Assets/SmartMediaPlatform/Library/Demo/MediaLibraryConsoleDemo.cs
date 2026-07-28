using System.Text;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Store;
using SmartMediaPlatform.Library.Playback;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using SmartMediaPlatform.Session;
using SmartMediaPlatform.Video.Data;
using UnityEngine;

namespace SmartMediaPlatform.Library.Demo
{
    /// <summary>
    /// Phase4-1 / 4-2 の Media Library を Console だけで確認するデモ。
    ///
    /// <b>VRChat SDK が無くても走ります。</b>
    /// 空の GameObject にアタッチして Play すれば動きます。
    ///
    /// 確認する順番:
    /// <list type="number">
    /// <item>カタログの中身が一覧として見える(取得口は <c>CatalogStore</c> だけ)</item>
    /// <item>Music / Video で絞り込める</item>
    /// <item><c>DisplayMeta</c> に表示項目が揃い、<b>URL は入っていない</b></item>
    /// <item>1 件を選べる / <c>PlayableRef</c> が取れる</item>
    /// <item><b>関連動画が出る(Phase4-2)</b>。ID → Catalog → DisplayMeta の流れ</item>
    /// <item><b>関連 ID をサーバー由来のものへ差し替えても、UI 側は無変更</b></item>
    /// <item>選んだものが <see cref="PlayerSession"/> へ渡って再生が始まる</item>
    /// <item>責務の境界(参照関係で確認)</item>
    /// </list>
    /// </summary>
    public sealed class MediaLibraryConsoleDemo : MonoBehaviour
    {
        [Header("実行")]
        [SerializeField] private bool _runOnStart = true;

        [Header("表示")]
        [Tooltip("一覧を何件まで Console に出すか(0 で全件)")]
        [SerializeField] private int _maxListedEntries = 8;

        [Header("関連動画(Phase4-2)")]
        [Tooltip("関連動画を出す起点")]
        [SerializeField] private string _relatedSeedId = "video-001";

        [Tooltip("関連動画を何件まで出すか(0 で制限なし)")]
        [SerializeField] private int _relatedCount = 5;

        private StringBuilder _out;
        private ListBackendLogger _logger;
        private int _flushed;

        private void Start()
        {
            if (_runOnStart) Run();
        }

        [ContextMenu("Run Media Library Scenarios")]
        public void Run()
        {
            _out = new StringBuilder();
            _logger = new ListBackendLogger();
            _flushed = 0;

            _out.AppendLine("=== Phase4-1 / 4-2 Media Library Demo ===");

            var rig = new Rig(_logger, _relatedCount);

            ShowWholeCatalog(rig);
            ShowTypeFilter(rig);
            ShowDetails(rig);
            ShowSelection(rig);
            ShowRelated(rig);
            ShowServerSwap(rig);
            ShowHandOff(rig);
            ShowBoundaries(rig);

            Debug.Log(_out.ToString());
        }

        // ───────── 1. 一覧 ─────────

        private void ShowWholeCatalog(Rig rig)
        {
            Section("1. カタログの中身を一覧で見る(取得口は CatalogStore だけ)");

            rig.Library.ShowAll();
            Line($"CatalogStore = {rig.Store}");
            Line(MediaLibraryFormatter.FormatSummary(rig.Library));
            ListEntries(rig.Library);

            Line("");
            Line("並び順を変えても中身は同じ(選択も保たれる):");
            rig.Library.SortOrder = LibrarySortOrder.Artist;
            Line("  " + MediaLibraryFormatter.FormatSummary(rig.Library));
            Line("  先頭 = " + Describe(rig.Library.GetAt(0)));

            rig.Library.SortOrder = LibrarySortOrder.CatalogOrder;
            Flush();
        }

        // ───────── 2. Music / Video ─────────

        private void ShowTypeFilter(Rig rig)
        {
            Section("2. Music / Video で絞り込む");

            rig.Library.ShowOnly(MediaType.Music);
            Line($"Music だけ  -> {rig.Library.Count} 件  例: {Describe(rig.Library.GetAt(0))}");

            rig.Library.ShowOnly(MediaType.Video);
            Line($"Video だけ  -> {rig.Library.Count} 件  例: {Describe(rig.Library.GetAt(0))}");

            rig.Library.ShowOnly(MediaType.Music, MediaType.Video);
            Line($"両方        -> {rig.Library.Count} 件");

            rig.Library.ShowAll();
            Line($"すべて      -> {rig.Library.Count} 件");
            Flush();
        }

        // ───────── 3. DisplayMeta ─────────

        private void ShowDetails(Rig rig)
        {
            Section("3. DisplayMeta に表示項目が揃っている(URL は入っていない)");

            rig.Library.ShowOnly(MediaType.Video);
            rig.Library.Select(0);

            foreach (string line in MediaLibraryFormatter.FormatSelection(rig.Library).Split('\n'))
            {
                Line(line.TrimEnd('\r'));
            }

            Line("");
            var meta = rig.Library.SelectedItem;
            Line($"DisplayMeta の型   = {meta.GetType().Name}");
            Line($"URL を返すメンバー = "
                 + $"{(meta.GetType().GetProperty("Url") == null ? "無し" : "ある(設計違反)")}");
            Line("→ UI へ渡る型に URL の口が無いので、表示側から URL は触れません。");
            Flush();
        }

        // ───────── 4. 選択と PlayableRef ─────────

        private void ShowSelection(Rig rig)
        {
            Section("4. メディアを選ぶ / PlayableRef を取り出す");

            rig.Library.ShowOnly(MediaType.Video);
            rig.Library.ClearSelection();
            Line($"最初は未選択 -> HasSelection={rig.Library.HasSelection}"
                 + $" SelectedRef={rig.Library.SelectedRef}");

            rig.Library.Select(2);
            Line($"Select(2)        -> {Describe(rig.Library.SelectedItem)}");

            rig.Library.SelectNext();
            Line($"SelectNext()     -> {Describe(rig.Library.SelectedItem)}");

            rig.Library.SelectById("video-001");
            Line($"SelectById(...)  -> {Describe(rig.Library.SelectedItem)}");

            var playable = rig.Library.SelectedRef;
            Line("");
            Line($"再生系へ渡すのはこれ -> {playable}");
            Line($"  IsValid = {playable.IsValid} / MediaId = {playable.MediaId} / Type = {playable.Type}");
            Line("  ※ URL は入っていません。URL 化は VideoBackend が焼き込み表から行います。");

            var missing = rig.Store.GetPlayableRef("does-not-exist");
            Line($"カタログに無い ID -> {missing}(IsValid={missing.IsValid})");
            Line("  → 存在しない ID は入口で止まるので、再生系まで流れません。");
            Flush();
        }

        // ───────── 5. 関連動画(Phase4-2) ─────────

        private void ShowRelated(Rig rig)
        {
            Section($"5. 関連動画を出す(起点: {_relatedSeedId})");

            int count = rig.Related.SetSource(_relatedSeedId);

            Line($"起点     = {Describe(rig.Related.SourceMeta)}");
            Line($"出どころ = {rig.Related.Provider}");
            Line($"件数     = {count}");

            for (int i = 0; i < rig.Related.Count; i++)
            {
                Line(MediaLibraryFormatter.FormatEntry(rig.Related.GetAt(i), i + 1));
            }

            Line("");
            Line("データの流れ:");
            Line("  ID(起点) → IRelatedMediaProvider → 関連 ID の並び");
            Line("            → CatalogStore        → DisplayMeta(表示用・URL 無し)");
            Line("            → RelatedMediaView    → UI");
            Line("            → PlayableRef         → PlayerSession(再生)");
            Flush();
        }

        // ───────── 6. サーバー差し替え ─────────

        private void ShowServerSwap(Rig rig)
        {
            Section("6. 関連 ID をサーバー由来のものへ差し替える(UI 側は無変更)");

            Line($"差し替え前 = {DescribeIds(rig.Related)}");

            // サーバーから「この動画の関連はこれ」と返ってきた想定。
            // 取得処理はこの層の外。ここでは結果の ID を入れるだけ。
            var fromServer = new[] { "video-009", "video-006", "does-not-exist", "video-010" };
            rig.RelatedProvider.Set(_relatedSeedId, fromServer);
            rig.Related.Refresh();

            Line($"サーバー応答 = [{string.Join(", ", fromServer)}]");
            Line($"差し替え後   = {DescribeIds(rig.Related)}");
            Line($"出どころ     = {rig.Related.Provider}");
            Line("");
            Line("・カタログに無い ID(does-not-exist)は CatalogStore が黙って落としました。");
            Line("・RelatedMediaView も UI も 1 行も変えていません。");
            Line("・差し替えたのは IRelatedMediaProvider の中身だけです。");

            // 以降のシナリオのためカタログ由来へ戻す
            rig.RelatedProvider.Remove(_relatedSeedId);
            rig.Related.Refresh();
            Flush();
        }

        // ───────── 7. PlayerSession へ渡す ─────────

        private void ShowHandOff(Rig rig)
        {
            Section("7. 関連動画から選んで PlayerSession へ渡す");

            rig.Related.SetSource(_relatedSeedId);
            if (rig.Related.Count == 0)
            {
                Line("関連が 0 件のため、この確認は省略します。");
                Flush();
                return;
            }

            rig.Related.Select(0);
            Line($"関連から選択 = {Describe(rig.Related.SelectedItem)}");
            Line($"  PlayableRef = {rig.Related.SelectedRef}");

            bool played = rig.RelatedBridge.PlaySelected();
            Line($"PlaySelected() -> {played}");
            Line($"  PlayerSession.CurrentMediaId = {rig.Session.CurrentMediaId}"
                 + $" [{rig.Session.PlaybackState}]");

            Line("");
            Line("再生を止めずに Queue の末尾へ足すこともできる:");
            if (rig.Related.Count > 1)
            {
                rig.Related.Select(1);
                bool queued = rig.RelatedBridge.EnqueueSelected();
                Line($"EnqueueSelected() -> {queued}  Queue={rig.Session.Queue.Count} 件");
                Line($"  Queue の中身 = {rig.DescribeQueue()}");
            }

            Line("");
            Line(rig.RelatedBridge.Describe());
            Flush();
        }

        // ───────── 8. 責務の境界 ─────────

        private void ShowBoundaries(Rig rig)
        {
            Section("8. 責務の境界(参照関係で確認)");

            var libraryAssembly = typeof(MediaLibrary).Assembly;
            var sessionAssembly = typeof(PlayerSession).Assembly;

            bool referencesSession = false;
            foreach (var reference in libraryAssembly.GetReferencedAssemblies())
            {
                if (reference.Name == sessionAssembly.GetName().Name) referencesSession = true;
            }

            Line($"Library のアセンブリ = {libraryAssembly.GetName().Name}");
            Line($"Library は Session を参照しているか -> {referencesSession}(false が正しい)");
            Line($"つないでいるのは {typeof(LibraryPlaybackBridge).Name} だけ。");
            Line($"境界を越えた値 = \"{rig.RelatedBridge.LastHandedOffId}\"(MediaId の string)");

            Line("");
            Line("カタログの内部構造を知っているのは CatalogStore だけ:");
            Line("  ICatalogStore が返す型 = DisplayMeta / PlayableRef / string");

            bool leaksMediaItem = false;
            foreach (var method in typeof(ICatalogStore).GetMethods())
            {
                if (method.ReturnType == typeof(MediaItem)) leaksMediaItem = true;
            }
            Line($"  MediaItem を返すメンバー = {(leaksMediaItem ? "ある(設計違反)" : "無し")}");

            Line("");
            Line("再生エンジンには何も足していません:");
            Line("  PlayerSession / BackendAdapter / Queue / Recommendation / VideoBackend は差分ゼロ");
            Flush();
        }

        // ───────── 出力 ─────────

        private void ListEntries(IMediaListView view)
        {
            int limit = _maxListedEntries > 0
                ? System.Math.Min(_maxListedEntries, view.Count)
                : view.Count;

            for (int i = 0; i < limit; i++)
            {
                Line(MediaLibraryFormatter.FormatEntry(view.GetAt(i), i + 1, i == view.SelectedIndex));
            }

            if (limit < view.Count) Line($"  … ほか {view.Count - limit} 件");
        }

        private static string Describe(DisplayMeta meta)
        {
            return meta != null ? $"[{meta.MediaId}] {meta.Title} / {meta.Artist}" : "(なし)";
        }

        private static string DescribeIds(IMediaListView view)
        {
            if (view.Count == 0) return "(空)";

            var sb = new StringBuilder();
            for (int i = 0; i < view.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(view.GetAt(i).MediaId);
            }
            return sb.ToString();
        }

        private void Section(string title)
        {
            _out.AppendLine();
            _out.AppendLine($"── {title}");
        }

        private void Line(string text)
        {
            _out.AppendLine(text.Length == 0 ? "" : "  " + text);
        }

        private void Flush()
        {
            for (int i = _flushed; i < _logger.Lines.Count; i++)
            {
                _out.Append("    ").AppendLine(_logger.Lines[i]);
            }
            _flushed = _logger.Lines.Count;
        }

        /// <summary>Catalog → CatalogStore → Library / Related → PlayerSession を 1 式。</summary>
        private sealed class Rig
        {
            public readonly CatalogStore Store;
            public readonly MediaLibrary Library;
            public readonly RelatedMediaView Related;
            public readonly StaticRelatedMediaProvider RelatedProvider;
            public readonly PlayerSession Session;
            public readonly LibraryPlaybackBridge Bridge;
            public readonly LibraryPlaybackBridge RelatedBridge;

            public Rig(IBackendLogger logger, int relatedCount)
            {
                // 音楽と動画が混ざったカタログ(Music / Video の絞り込みを見せるため)
                IMediaCatalog catalog = new MediaCatalog(new MixedCatalogSource(), new System.Random(1));

                // ── ここが唯一のデータ取得口。以降どこも IMediaCatalog を触らない。
                Store = new CatalogStore(catalog);

                Library = new MediaLibrary(Store);

                // ── 関連 ID の出どころ。いまはカタログの RelatedIds。
                //    サーバー連携になったら RelatedProvider.Set(...) を呼ぶだけ。
                RelatedProvider = new StaticRelatedMediaProvider(
                    new CatalogRelatedMediaProvider(catalog));
                Related = new RelatedMediaView(Store, RelatedProvider, relatedCount);

                // ── 再生側は Phase1〜3 の組み立てをそのまま使う(変更なし)
                var queue = new MediaQueue();
                var backendManager = new BackendManager(queue, logger);
                backendManager.RegisterBackend(new DummyBackend("DummyBackend", logger));

                var mediaPlayer = new MediaPlayer(backendManager, logger);
                var engine = new RecommendationEngine(catalog, RecommendationRule.CreateDefault());
                Session = new PlayerSession(
                    "library", mediaPlayer, catalog, engine, new System.Random(1), logger);

                // ── 一覧と再生をつなぐのはこのクラスだけ(どちらの一覧でも同じ)
                Bridge = new LibraryPlaybackBridge(Library, Session, logger);
                RelatedBridge = new LibraryPlaybackBridge(Related, Session, logger);
            }

            public string DescribeQueue()
            {
                var entries = Session.Queue.GetAll();
                if (entries.Count == 0) return "(空)";

                var sb = new StringBuilder();
                for (int i = 0; i < entries.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(entries[i].MediaId);
                }
                return sb.ToString();
            }
        }
    }
}
