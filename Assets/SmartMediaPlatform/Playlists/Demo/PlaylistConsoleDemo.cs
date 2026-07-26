using System.Collections;
using System.Linq;
using System.Text;
using SmartMediaPlatform.Audio;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;
using UnityEngine;

namespace SmartMediaPlatform.Playlists.Demo
{
    /// <summary>
    /// Phase2-3 の Playlist / Auto Queue を Console だけで確認するデモ。
    ///
    /// 前半はプレイリストの編集(作成・追加・削除・移動・並び替え・複製・保存/読込)、
    /// 後半は再生モードごとの Queue の作られ方と自動補充を確認する。
    /// 最後に実際に音を鳴らして再生まで通す。
    ///
    /// 空の GameObject にアタッチして Play すると走る
    /// (<see cref="AudioBackendHost"/> と AudioSource は自動で用意される)。
    /// </summary>
    public sealed class PlaylistConsoleDemo : MonoBehaviour
    {
        [Header("プレイリストに入れる曲(Catalog の ID)")]
        [SerializeField]
        private string[] _playlistIds = { "music-001", "music-002", "music-003" };

        [Header("実行")]
        [SerializeField] private bool _runOnStart = true;

        [Tooltip("実際に音を鳴らすところまで通すか")]
        [SerializeField] private bool _playAudioAtTheEnd = true;

        [Tooltip("再生を聴かせる時間(秒)")]
        [SerializeField] private float _listenSeconds = 4f;

        private IMediaCatalog _catalog;
        private StringBuilder _out;

        private void Start()
        {
            if (_runOnStart) StartCoroutine(RunAll());
        }

        [ContextMenu("Run Playlist Scenarios (no audio)")]
        public void RunWithoutAudio()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _out = new StringBuilder();
            _out.AppendLine("=== Phase2-3 Playlist & Auto Queue Demo ===");

            RunEditingScenario();
            RunPersistenceScenario();
            RunQueueGenerationScenario();
            RunModeScenarios();
            RunAutoQueueScenario();

            Debug.Log(_out.ToString());
        }

        public IEnumerator RunAll()
        {
            RunWithoutAudio();

            if (_playAudioAtTheEnd) yield return RunPlaybackScenario();
        }

        // ───────── 1. 編集 ─────────

        private void RunEditingScenario()
        {
            Section("1. Playlist の編集");

            var manager = new PlaylistManager();

            var playlist = manager.Create("お気に入り");
            Line($"作成: {playlist}");

            playlist.AddRange(_playlistIds);
            Line($"曲追加: {Join(playlist)}");

            playlist.Add("music-010");
            Line($"曲追加(1曲): {Join(playlist)}");

            playlist.Remove("music-002");
            Line($"曲削除(music-002): {Join(playlist)}");

            playlist.Move(0, 2);
            Line($"曲移動(0 -> 2): {Join(playlist)}");

            playlist.Reverse();
            Line($"並び替え(逆順): {Join(playlist)}");

            playlist.Shuffle(new System.Random(3));
            Line($"並び替え(シャッフル): {Join(playlist)}");

            var copy = manager.Duplicate(playlist.Id, "お気に入り(コピー)");
            Line($"複製: {copy} -> {Join(copy)}");

            manager.Delete(copy.Id);
            Line($"削除: 残り {manager.Count} 件");
        }

        // ───────── 2. 保存 / 読み込み ─────────

        private void RunPersistenceScenario()
        {
            Section("2. Playlist の保存と読み込み");

            var manager = new PlaylistManager();
            var morning = manager.Create("朝", "morning");
            morning.AddRange(new[] { "music-001", "music-009" });
            var night = manager.Create("夜", "night");
            night.AddRange(new[] { "music-003", "music-006" });

            var store = new InMemoryPlaylistStore();
            manager.Save(store);
            Line($"保存: {manager.Count} 件");

            var restored = new PlaylistManager();
            restored.Load(store);
            Line($"読み込み: {restored.Count} 件");
            foreach (var playlist in restored.Playlists)
            {
                Line($"  {playlist} -> {Join(playlist)}");
            }
        }

        // ───────── 3. Playlist -> Queue ─────────

        private void RunQueueGenerationScenario()
        {
            Section("3. Playlist から Queue を生成");

            var (service, queue, playlist) = BuildService(PlaybackMode.Normal);
            service.PlayPlaylist(playlist);

            Line($"Playlist: {Join(playlist)}");
            Line($"Queue   : {JoinQueue(queue)}");
        }

        // ───────── 4. 再生モード ─────────

        private void RunModeScenarios()
        {
            Section("4. 再生モード");

            // Shuffle
            var (shuffleService, shuffleQueue, shufflePlaylist) = BuildService(PlaybackMode.Shuffle);
            shuffleService.PlayPlaylist(shufflePlaylist);
            Line($"Shuffle    : {JoinQueue(shuffleQueue)}");

            // Repeat One
            var (oneService, oneQueue, onePlaylist) = BuildService(PlaybackMode.RepeatOne);
            oneService.PlayPlaylist(onePlaylist);
            while (oneQueue.Count > 1) oneQueue.Skip();          // 1 曲だけ残す
            oneService.EnsureFilled(oneQueue.Peek().MediaId);
            Line($"RepeatOne  : {JoinQueue(oneQueue)} (次も同じ曲)");

            // Repeat All
            var (allService, allQueue, allPlaylist) = BuildService(PlaybackMode.RepeatAll);
            allService.PlayPlaylist(allPlaylist);
            while (allQueue.Count > 1) allQueue.Skip();          // 最後の曲まで進める
            allService.EnsureFilled(allQueue.Peek().MediaId);
            Line($"RepeatAll  : {JoinQueue(allQueue)} (もう一巡ぶん積まれる)");
        }

        // ───────── 5. Auto Queue ─────────

        private void RunAutoQueueScenario()
        {
            Section("5. Auto Queue(おすすめによる自動補充)");

            // ON
            var (onService, onQueue, onPlaylist) = BuildService(PlaybackMode.Normal);
            onService.AutoQueueEnabled = true;
            onService.PlayPlaylist(onPlaylist);
            while (onQueue.Count > 1) onQueue.Skip();
            int added = onService.EnsureFilled(onQueue.Peek().MediaId);
            Line($"AutoQueue ON : {added} 曲を補充 -> {JoinQueue(onQueue)}");
            Line($"  補充分の内訳: "
                 + string.Join(", ", onQueue.GetAll()
                     .Where(x => x.Source == QueueItemSource.Recommendation)
                     .Select(x => x.MediaId)));

            // OFF
            var (offService, offQueue, offPlaylist) = BuildService(PlaybackMode.Normal);
            offService.AutoQueueEnabled = false;
            offService.PlayPlaylist(offPlaylist);
            while (offQueue.Count > 1) offQueue.Skip();
            int notAdded = offService.EnsureFilled(offQueue.Peek().MediaId);
            Line($"AutoQueue OFF: {notAdded} 曲を補充 -> {JoinQueue(offQueue)} (増えない)");

            // Shuffle + Auto Queue
            var (mixService, mixQueue, mixPlaylist) = BuildService(PlaybackMode.ShuffleAutoQueue);
            mixService.AutoQueueEnabled = false;   // モードが優先される
            mixService.PlayPlaylist(mixPlaylist);
            while (mixQueue.Count > 1) mixQueue.Skip();
            int mixAdded = mixService.EnsureFilled(mixQueue.Peek().MediaId);
            Line($"Shuffle+Auto : {mixAdded} 曲を補充 (IsAutoQueueActive = {mixService.IsAutoQueueActive})");
        }

        // ───────── 6. 実際に再生する ─────────

        private IEnumerator RunPlaybackScenario()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 6. Playlist を実際に再生する(音が鳴ります) ===");

            var logger = new ListBackendLogger();
            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());

            var host = GetComponent<AudioBackendHost>();
            if (host == null) host = gameObject.AddComponent<AudioBackendHost>();
            host.EnsureBuilt(logger);
            host.PrepareClipsFor(catalog);

            var queue = new MediaQueue();
            var engine = new RecommendationEngine(catalog, RecommendationRule.CreateDefault());
            var service = new AutoQueueService(queue, catalog, engine)
            {
                Mode = PlaybackMode.Normal,
                AutoQueueEnabled = true,
                Logger = logger.Log,
            };

            var manager = new BackendManager(queue, logger);
            var player = new MediaPlayer(manager, logger);
            player.RegisterBackend(host.Backend);
            player.RegisterBackend(new DummyBackend("DummyBackend", logger));

            var playlist = new Playlist("play", "再生用");
            playlist.AddRange(_playlistIds);

            service.PlayPlaylist(playlist);
            sb.AppendLine($"  Queue: {JoinQueue(queue)}");

            player.Play();

            float waited = 0f;
            while (waited < _listenSeconds)
            {
                waited += Time.deltaTime;

                // 再生が進んで Queue が減ったら、モードに応じて補充する
                var current = player.GetCurrent();
                service.Tick(current != null ? current.Id : null);

                yield return null;
            }

            player.Stop();

            foreach (var line in logger.Lines) sb.AppendLine("  " + line);
            sb.Append($"  最終: {player.Describe()} / Queue {queue.Count} 曲");

            Debug.Log(sb.ToString());
        }

        // ───────── 組み立て / 出力 ─────────

        private (AutoQueueService, MediaQueue, Playlist) BuildService(PlaybackMode mode)
        {
            var queue = new MediaQueue();
            var engine = new RecommendationEngine(
                _catalog,
                new[]
                {
                    RecommendationRule.Related(10.0),
                    RecommendationRule.SameArtist(5.0),
                    RecommendationRule.SameGenre(3.0),
                    RecommendationRule.TagMatch(1.0),
                    RecommendationRule.Random(0.0),
                },
                new System.Random(1));

            var service = new AutoQueueService(queue, _catalog, engine, new System.Random(1))
            {
                Mode = mode,
            };

            var playlist = new Playlist("demo", "デモ用");
            playlist.AddRange(_playlistIds);

            return (service, queue, playlist);
        }

        private static string Join(Playlist playlist)
        {
            return playlist.Count == 0 ? "(空)" : string.Join(", ", playlist.MediaIds);
        }

        private static string JoinQueue(IQueue queue)
        {
            return queue.Count == 0
                ? "(空)"
                : string.Join(", ", queue.GetAll().Select(x => x.MediaId));
        }

        private void Section(string title)
        {
            _out.AppendLine();
            _out.AppendLine($"── {title}");
        }

        private void Line(string text)
        {
            _out.AppendLine("  " + text);
        }
    }
}
