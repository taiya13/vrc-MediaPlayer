using System.Text;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
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
    /// Phase4-1 の Media Library を Console だけで確認するデモ。
    ///
    /// <b>VRChat SDK が無くても走ります。</b>
    /// 空の GameObject にアタッチして Play すれば動きます。
    ///
    /// 確認する順番:
    /// <list type="number">
    /// <item>カタログの中身が一覧として見える</item>
    /// <item>Music / Video で絞り込める</item>
    /// <item>タイトル・アーティスト・ジャンル・タグが見える</item>
    /// <item>1 件を選べる(位置でも ID でも、前後移動でも)</item>
    /// <item>選んだものが <see cref="PlayerSession"/> へ渡って再生が始まる</item>
    /// <item>Library は再生エンジンを知らない(参照関係で確認)</item>
    /// </list>
    /// </summary>
    public sealed class MediaLibraryConsoleDemo : MonoBehaviour
    {
        [Header("実行")]
        [SerializeField] private bool _runOnStart = true;

        [Header("表示")]
        [Tooltip("一覧を何件まで Console に出すか(0 で全件)")]
        [SerializeField] private int _maxListedEntries = 8;

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

            _out.AppendLine("=== Phase4-1 Media Library Demo ===");

            var rig = new Rig(_logger);

            ShowWholeCatalog(rig);
            ShowTypeFilter(rig);
            ShowDetails(rig);
            ShowSelection(rig);
            ShowHandOff(rig);
            ShowBoundaries(rig);

            Debug.Log(_out.ToString());
        }

        // ───────── 1. 一覧 ─────────

        private void ShowWholeCatalog(Rig rig)
        {
            Section("1. カタログの中身を一覧で見る");

            rig.Library.ShowAll();
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

        // ───────── 3. 表示項目 ─────────

        private void ShowDetails(Rig rig)
        {
            Section("3. タイトル・アーティスト・ジャンル・タグを表示する");

            rig.Library.ShowOnly(MediaType.Video);
            rig.Library.Select(0);

            foreach (string line in MediaLibraryFormatter.FormatSelection(rig.Library)
                         .Split('\n'))
            {
                Line(line.TrimEnd('\r'));
            }

            Line("");
            Line("※ URL は表示しません。URL を知るのは VideoBackend だけ、という約束を");
            Line("   表示側でも守っています(MediaLibraryFormatter に URL の出力口はありません)。");
            Flush();
        }

        // ───────── 4. 選択 ─────────

        private void ShowSelection(Rig rig)
        {
            Section("4. メディアを選ぶ");

            rig.Library.ShowOnly(MediaType.Video);
            rig.Library.ClearSelection();
            Line($"最初は未選択 -> HasSelection={rig.Library.HasSelection}"
                 + $" SelectedMediaId={rig.Library.SelectedMediaId ?? "null"}");

            rig.Library.Select(2);
            Line($"Select(2)        -> {Describe(rig.Library.SelectedItem)}");

            rig.Library.SelectNext();
            Line($"SelectNext()     -> {Describe(rig.Library.SelectedItem)}");

            rig.Library.SelectPrevious();
            Line($"SelectPrevious() -> {Describe(rig.Library.SelectedItem)}");

            rig.Library.SelectById("video-001");
            Line($"SelectById(...)  -> {Describe(rig.Library.SelectedItem)}");

            Line($"通知を受けた回数 -> 一覧 {rig.Watcher.LibraryChanged} 回"
                 + $" / 選択 {rig.Watcher.SelectionChanged} 回");
            Flush();
        }

        // ───────── 5. PlayerSession へ渡す ─────────

        private void ShowHandOff(Rig rig)
        {
            Section("5. 選んだものを PlayerSession へ渡す");

            rig.Library.ShowOnly(MediaType.Video);
            rig.Library.SelectById("video-003");

            Line($"選択 = {rig.Library.SelectedMediaId}");
            bool played = rig.Bridge.PlaySelected();
            Line($"PlaySelected() -> {played}");
            Line($"  PlayerSession.CurrentMediaId = {rig.Session.CurrentMediaId}"
                 + $" [{rig.Session.PlaybackState}]");

            Line("");
            Line("再生を止めずに Queue の末尾へ足すこともできる:");
            rig.Library.SelectById("video-007");
            bool queued = rig.Bridge.EnqueueSelected();
            Line($"EnqueueSelected() -> {queued}  Queue={rig.Session.Queue.Count} 件");
            Line($"  Queue の中身 = {rig.DescribeQueue()}");

            Line("");
            Line(rig.Bridge.Describe());
            Flush();
        }

        // ───────── 6. 責務の境界 ─────────

        private void ShowBoundaries(Rig rig)
        {
            Section("6. Media Library は閲覧と選択だけ(参照関係で確認)");

            var libraryAssembly = typeof(MediaLibrary).Assembly;
            var sessionAssembly = typeof(PlayerSession).Assembly;

            Line($"MediaLibrary のアセンブリ  = {libraryAssembly.GetName().Name}");
            Line($"PlayerSession のアセンブリ = {sessionAssembly.GetName().Name}");

            bool referencesSession = false;
            foreach (var reference in libraryAssembly.GetReferencedAssemblies())
            {
                if (reference.Name == sessionAssembly.GetName().Name) referencesSession = true;
            }

            Line($"Library は Session を参照しているか -> {referencesSession}"
                 + "(false なのが正しい)");
            Line("  → MediaLibrary からは PlayerSession も Queue も Backend も見えません。");
            Line($"  → つないでいるのは {typeof(LibraryPlaybackBridge).Name} だけで、");
            Line($"     境界を越えるのは MediaId(string)= \"{rig.Bridge.LastHandedOffId}\" だけです。");

            Line("");
            Line("再生エンジンには何も足していません:");
            Line("  PlayerSession / BackendAdapter / Queue / Recommendation / VideoBackend は差分ゼロ");
            Flush();
        }

        // ───────── 出力 ─────────

        private void ListEntries(IMediaLibrary library)
        {
            int limit = _maxListedEntries > 0
                ? System.Math.Min(_maxListedEntries, library.Count)
                : library.Count;

            for (int i = 0; i < limit; i++)
            {
                Line(MediaLibraryFormatter.FormatEntry(
                    library.GetAt(i), i + 1, i == library.SelectedIndex));
            }

            if (limit < library.Count) Line($"  … ほか {library.Count - limit} 件");
        }

        private static string Describe(MediaItem item)
        {
            return item != null ? $"[{item.Id}] {item.Title} / {item.Artist}" : "(なし)";
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

        /// <summary>Catalog → Library → PlayerSession を 1 式組み立てたもの。</summary>
        private sealed class Rig
        {
            public readonly IMediaCatalog Catalog;
            public readonly MediaLibrary Library;
            public readonly PlayerSession Session;
            public readonly LibraryPlaybackBridge Bridge;
            public readonly Watcher Watcher = new Watcher();

            public Rig(IBackendLogger logger)
            {
                // 音楽と動画が混ざったカタログ(Music / Video の絞り込みを見せるため)
                Catalog = new MediaCatalog(new MixedCatalogSource(), new System.Random(1));

                // ── ここまでが Library に必要なすべて。Catalog しか要らない。
                Library = new MediaLibrary(Catalog);
                Library.AddObserver(Watcher);

                // ── 再生側は Phase1〜3 の組み立てをそのまま使う(変更なし)
                var queue = new MediaQueue();
                var backendManager = new BackendManager(queue, logger);
                backendManager.RegisterBackend(new DummyBackend("DummyBackend", logger));

                var mediaPlayer = new MediaPlayer(backendManager, logger);
                var engine = new RecommendationEngine(Catalog, RecommendationRule.CreateDefault());
                Session = new PlayerSession(
                    "library", mediaPlayer, Catalog, engine, new System.Random(1), logger);

                // ── 両者をつなぐのはこの 1 クラスだけ
                Bridge = new LibraryPlaybackBridge(Library, Session, logger);
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

        /// <summary>通知が届いているかを数えるだけの観測者。</summary>
        private sealed class Watcher : IMediaLibraryObserver
        {
            public int LibraryChanged { get; private set; }
            public int SelectionChanged { get; private set; }

            public void OnLibraryChanged(IMediaLibrary library) => LibraryChanged++;

            public void OnSelectionChanged(IMediaLibrary library, MediaItem selected)
                => SelectionChanged++;
        }
    }
}
