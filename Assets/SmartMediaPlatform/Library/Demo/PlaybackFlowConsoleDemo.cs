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
    /// Phase4-3: <b>Library → Queue → Player の一連の流れ</b>を Console で確認するデモ。
    ///
    /// <b>VRChat SDK が無くても走ります。</b>
    /// 空の GameObject にアタッチして Play すれば動きます。
    ///
    /// 確認する順番:
    /// <list type="number">
    /// <item>一式を組み立てる(<see cref="PlaybackFlow"/> 1 行)</item>
    /// <item>Library で選ぶ → 再生が始まる</item>
    /// <item>Queue が DisplayMeta で見える(URL は出ない)</item>
    /// <item>Queue へ足す / 次に再生する / 並べ替える / 消す</item>
    /// <item>Queue の途中へ飛ぶ</item>
    /// <item>曲が終わったら次へ進む(Ended)</item>
    /// <item>関連動画が再生に追従する</item>
    /// <item>責務の境界(MediaItem も URL も出てこない)</item>
    /// </list>
    /// </summary>
    public sealed class PlaybackFlowConsoleDemo : MonoBehaviour
    {
        [Header("実行")]
        [SerializeField] private bool _runOnStart = true;

        [Header("設定")]
        [Tooltip("関連動画を何件まで出すか(0 で制限なし)")]
        [SerializeField] private int _relatedCount = 5;

        private StringBuilder _out;
        private ListBackendLogger _logger;
        private int _flushed;

        private void Start()
        {
            if (_runOnStart) Run();
        }

        [ContextMenu("Run Playback Flow Scenarios")]
        public void Run()
        {
            _out = new StringBuilder();
            _logger = new ListBackendLogger();
            _flushed = 0;

            _out.AppendLine("=== Phase4-3 Playback Flow Demo (Library → Queue → Player) ===");

            var rig = new Rig(_logger, _relatedCount);

            ShowAssembly(rig);
            ShowPlayFromLibrary(rig);
            ShowQueueContents(rig);
            ShowQueueOperations(rig);
            ShowJumpInQueue(rig);
            ShowEndedAdvance(rig);
            ShowRelatedFollows(rig);
            ShowBoundaries(rig);

            Debug.Log(_out.ToString());
        }

        // ───────── 1. 組み立て ─────────

        private void ShowAssembly(Rig rig)
        {
            Section("1. 一式を組み立てる");

            Line("PlaybackFlow.Create(catalog, session) の 1 行で:");
            Line($"  Store       = {rig.Flow.Store}");
            Line($"  Library     = {rig.Flow.Library.Count} 件");
            Line($"  Related     = {rig.Flow.RelatedProvider}");
            Line($"  Queue       = {rig.Flow.Queue}");
            Line($"  NowPlaying  = {rig.Flow.NowPlaying.Describe()}");
            Flush();
        }

        // ───────── 2. Library → Player ─────────

        private void ShowPlayFromLibrary(Rig rig)
        {
            Section("2. Library で選ぶ → 再生が始まる");

            rig.Flow.Library.ShowOnly(MediaType.Video);
            rig.Flow.Library.Select(0);

            var meta = rig.Flow.Library.SelectedItem;
            Line($"選択 = {meta.Title} / {meta.Artist}");
            Line($"  渡すもの = {rig.Flow.Library.SelectedRef}");

            bool played = rig.Flow.LibraryPlayback.PlaySelected();
            rig.Flow.Tick();

            Line($"PlaySelected() -> {played}");
            Line($"  {rig.Flow.NowPlaying.Describe()}");
            Flush();
        }

        // ───────── 3. Queue が見える ─────────

        private void ShowQueueContents(Rig rig)
        {
            Section("3. Queue の中身が DisplayMeta で見える");

            rig.Flow.Tick();
            ListView(rig.Flow.Queue, "Queue");

            Line("");
            Line("Queue から取り出せる型:");
            var head = rig.Flow.Queue.NowPlaying;
            Line($"  QueueView.GetAt(0) = {head.GetType().Name}(URL の口 = "
                 + $"{(head.GetType().GetProperty("Url") == null ? "無し" : "ある")})");
            Line("  ※ IQueue.GetAll() が返す QueueItem は MediaItem(= Url)を抱えていますが、");
            Line("     QueueView は MediaId だけ取り出して CatalogStore から引き直しています。");
            Flush();
        }

        // ───────── 4. Queue 操作 ─────────

        private void ShowQueueOperations(Rig rig)
        {
            Section("4. Queue へ足す / 次に再生 / 並べ替え / 消す");

            rig.Flow.Library.ShowOnly(MediaType.Video);

            rig.Flow.Library.SelectById("video-008");
            rig.Flow.LibraryPlayback.EnqueueSelected();
            rig.Flow.Tick();
            Line($"末尾に足す(video-008) -> {DescribeIds(rig.Flow.Queue)}");

            rig.Flow.Library.SelectById("video-010");
            rig.Flow.LibraryPlayback.PlayNextSelected();
            rig.Flow.Tick();
            Line($"次に再生(video-010)   -> {DescribeIds(rig.Flow.Queue)}");
            Line("  → index 1(先頭の次)に入りました。いまの再生は止まっていません。");

            if (rig.Flow.Queue.Count > 3)
            {
                rig.Flow.Queue.MoveDown(1);
                Line($"1 番目を下へ         -> {DescribeIds(rig.Flow.Queue)}");

                rig.Flow.Queue.MoveUp(2);
                Line($"2 番目を上へ         -> {DescribeIds(rig.Flow.Queue)}");

                rig.Flow.Queue.RemoveAt(2);
                Line($"2 番目を消す         -> {DescribeIds(rig.Flow.Queue)}");
            }

            Line("");
            Line("先頭(いま鳴っているもの)は守られます:");
            Line($"  MoveUp(0)    -> {rig.Flow.Queue.MoveUp(0)}");
            Line($"  RemoveAt(0)  -> {rig.Flow.Queue.RemoveAt(0)}");
            Line("  → 再生と Queue がずれないよう、先頭は動かせません。");
            Flush();
        }

        // ───────── 5. Queue の途中へ飛ぶ ─────────

        private void ShowJumpInQueue(Rig rig)
        {
            Section("5. Queue の途中へ飛ぶ");

            rig.Flow.Tick();
            if (rig.Flow.Queue.Count < 3)
            {
                Line("Queue が短いため省略します。");
                Flush();
                return;
            }

            string before = rig.Flow.NowPlaying.MediaId;
            var target = rig.Flow.Queue.GetAt(2);

            Line($"いま = {before} / 飛び先 = {target.MediaId} ({target.Title})");

            rig.Flow.QueuePlayback.PlayAt(2);
            rig.Flow.Tick();

            Line($"QueuePlayback.PlayAt(2) -> {rig.Flow.NowPlaying.MediaId}");
            Line($"  {rig.Flow.NowPlaying.Describe()}");
            Line($"  Queue = {DescribeIds(rig.Flow.Queue)}");
            Flush();
        }

        // ───────── 6. Ended で次へ ─────────

        private void ShowEndedAdvance(Rig rig)
        {
            Section("6. 曲が終わったら次へ進む(Ended)");

            for (int i = 1; i <= 3; i++)
            {
                string before = rig.Flow.NowPlaying.MediaId;
                rig.FinishCurrent();
                rig.Flow.Tick();

                Line($"  {i}. Ended({before}) -> {rig.Flow.NowPlaying.MediaId}"
                     + $" [{rig.Flow.NowPlaying.State}] Queue={rig.Flow.Queue.Count}");
            }

            Line("");
            Line("→ 進めているのは PlayerSession です(Phase2-3(B) から変更なし)。");
            Line("   QueueView / NowPlayingView は結果を見せているだけです。");
            Flush();
        }

        // ───────── 7. 関連の追従 ─────────

        private void ShowRelatedFollows(Rig rig)
        {
            Section("7. 関連動画が再生に追従する");

            rig.Flow.Tick();
            Line($"再生中 = {rig.Flow.NowPlaying.MediaId}");
            Line($"関連の起点 = {rig.Flow.Related.SourceMediaId}");
            Line($"関連 = {DescribeIds(rig.Flow.Related)}");

            if (rig.Flow.Related.Count > 0)
            {
                var pick = rig.Flow.Related.GetAt(0);
                Line("");
                Line($"関連から再生する: {pick.MediaId} ({pick.Title})");

                rig.Flow.RelatedPlayback.PlayAt(0);
                rig.Flow.Tick();

                Line($"  再生中 = {rig.Flow.NowPlaying.MediaId}");
                Line($"  関連の起点も移った -> {rig.Flow.Related.SourceMediaId}");
                Line($"  新しい関連 = {DescribeIds(rig.Flow.Related)}");
                Line("  → 関連 → 関連 とたどれます。");
            }
            Flush();
        }

        // ───────── 8. 責務の境界 ─────────

        private void ShowBoundaries(Rig rig)
        {
            Section("8. 責務の境界");

            Line("UI へ出る型に URL の口が無い:");
            Line($"  DisplayMeta.Url  = {(typeof(DisplayMeta).GetProperty("Url") == null ? "無し" : "ある")}");
            Line($"  PlayableRef.Url  = {(typeof(PlayableRef).GetProperty("Url") == null ? "無し" : "ある")}");

            bool queueLeaks = typeof(QueueView).GetMethod("GetQueueItem") != null;
            Line($"  QueueView が QueueItem を返す = {(queueLeaks ? "ある" : "無し")}");

            Line("");
            Line("PlayerSession は MediaId(string)を中心に扱っている:");
            Line($"  PlayerSession.CurrentMediaId = \"{rig.Session.CurrentMediaId}\"(string)");
            Line($"  表示情報は Catalog から引き直す -> {rig.Flow.NowPlaying.FormatTitle()}");

            Line("");
            Line("Phase4-2 の抽象化はそのまま:");
            Line($"  関連 ID の出どころ = {rig.Flow.RelatedProvider}");
            Line("  → サーバー応答へ差し替えても、Queue も Player も UI も無変更です。");

            Line("");
            Line("再生エンジンは Phase1〜3 のまま:");
            Line("  PlayerSession / MediaPlayer / BackendManager / BackendAdapter /");
            Line("  MediaQueue / Recommendation / VideoBackend は差分ゼロ");
            Flush();
        }

        // ───────── 出力 ─────────

        private void ListView(IMediaListView view, string header)
        {
            Line($"{header}: {view.Count} 件");
            for (int i = 0; i < view.Count; i++)
            {
                Line(MediaLibraryFormatter.FormatEntry(view.GetAt(i), i + 1, i == view.SelectedIndex));
            }
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

        /// <summary>Catalog → PlaybackFlow → PlayerSession を 1 式。</summary>
        private sealed class Rig
        {
            public readonly PlaybackFlow Flow;
            public readonly PlayerSession Session;

            private readonly DummyBackend _backend;

            public Rig(IBackendLogger logger, int relatedCount)
            {
                IMediaCatalog catalog = new MediaCatalog(new VideoCatalogSource(), new System.Random(1));

                // ── 再生側は Phase1〜3 の組み立てをそのまま使う(変更なし)
                var queue = new MediaQueue();
                var backendManager = new BackendManager(queue, logger);
                _backend = new DummyBackend("DummyBackend", logger);
                backendManager.RegisterBackend(_backend);

                var mediaPlayer = new MediaPlayer(backendManager, logger);
                var engine = new RecommendationEngine(catalog, RecommendationRule.CreateDefault());
                Session = new PlayerSession(
                    "flow", mediaPlayer, catalog, engine, new System.Random(1), logger);

                // ── Phase4-3: ここ 1 行で Library → Queue → Player が繋がる
                Flow = PlaybackFlow.Create(catalog, Session, relatedCount, logger);
            }

            /// <summary>いま鳴っているものを終わらせる(実機の Ended 相当)。</summary>
            public void FinishCurrent()
            {
                _backend.SimulateEnded();
            }
        }
    }
}
