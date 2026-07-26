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

namespace SmartMediaPlatform.Session.Demo
{
    /// <summary>
    /// PlayerSession の動作を Console だけで確認するデモ。
    ///
    /// 前半は状態管理・Queue・履歴・各モードを、実際の再生を待たずに確認する
    /// (テスト用バックエンドで曲の終わりを擬似的に起こす)。
    /// 後半は AudioBackend で実際に音を鳴らし、Ended から次の曲へ移るところまで通す。
    ///
    /// 空の GameObject にアタッチして Play すれば走る。
    /// </summary>
    public sealed class PlayerSessionConsoleDemo : MonoBehaviour
    {
        [Header("セッションで流す曲(Catalog の ID)")]
        [SerializeField]
        private string[] _trackIds = { "music-001", "music-002", "music-003" };

        [Header("実行")]
        [SerializeField] private bool _runOnStart = true;

        [Tooltip("最後に実際に音を鳴らすところまで通すか")]
        [SerializeField] private bool _playAudioAtTheEnd = true;

        [Tooltip("再生を聴かせる時間(秒)")]
        [SerializeField] private float _listenSeconds = 5f;

        private IMediaCatalog _catalog;
        private StringBuilder _out;

        private void Start()
        {
            if (_runOnStart) StartCoroutine(RunAll());
        }

        [ContextMenu("Run Session Scenarios (no audio)")]
        public void RunWithoutAudio()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _out = new StringBuilder();
            _out.AppendLine("=== Phase2-3 PlayerSession Demo ===");

            RunStateScenario();
            RunQueueAndHistoryScenario();
            RunRepeatOneScenario();
            RunRepeatAllScenario();
            RunShuffleScenario();
            RunAutoQueueScenario();
            RunSessionManagerScenario();

            Debug.Log(_out.ToString());
        }

        public IEnumerator RunAll()
        {
            RunWithoutAudio();

            if (_playAudioAtTheEnd) yield return RunRealPlaybackScenario();
        }

        // ───────── 1. 状態遷移 ─────────

        private void RunStateScenario()
        {
            Section("1. 状態管理と状態遷移");

            var session = NewSession();
            session.SetTracks(_trackIds);
            Line($"SetTracks -> {Describe(session)}");

            session.Play();
            Line($"Play      -> {Describe(session)}");

            session.Pause();
            Line($"Pause     -> {Describe(session)}");

            session.Resume();
            Line($"Resume    -> {Describe(session)}");

            session.Stop();
            Line($"Stop      -> {Describe(session)}");

            session.Play();
            Line($"Play      -> {Describe(session)}");
            Line($"  BackendState = {session.BackendState} / PlaybackState = {session.PlaybackState}");
        }

        // ───────── 2. Queue と履歴 ─────────

        private void RunQueueAndHistoryScenario()
        {
            Section("2. Queue 管理と履歴");

            var session = NewSession();
            session.AutoQueueEnabled = false;
            session.SetTracks(_trackIds);
            session.Play();
            Line($"開始   : {Describe(session)}  Queue=[{JoinQueue(session)}]");

            session.Next();
            Line($"Next   : {Describe(session)}  履歴=[{Join(session.History)}]");

            session.Next();
            Line($"Next   : {Describe(session)}  履歴=[{Join(session.History)}]");

            session.Previous();
            Line($"Previous: {Describe(session)}  履歴=[{Join(session.History)}]");
        }

        // ───────── 3. Repeat One ─────────

        private void RunRepeatOneScenario()
        {
            Section("3. Repeat One");

            var (session, backend) = BuildSession();
            session.SetTracks(_trackIds);
            session.RepeatMode = RepeatMode.One;
            session.Play();
            Line($"開始 : {Describe(session)}");

            for (int i = 1; i <= 2; i++)
            {
                backend.SimulateEnded();
                Line($"{i} 曲目の再生終了 -> {Describe(session)} (同じ曲のまま)");
            }
        }

        // ───────── 4. Repeat All ─────────

        private void RunRepeatAllScenario()
        {
            Section("4. Repeat All");

            var (session, backend) = BuildSession();
            session.AutoQueueEnabled = false;
            session.SetTracks(_trackIds);
            session.RepeatMode = RepeatMode.All;
            session.Play();
            Line($"開始 : {Describe(session)}");

            for (int i = 0; i < 4; i++)
            {
                backend.SimulateEnded();
                Line($"再生終了 -> {Describe(session)}");
            }
            Line("  最後まで行くと先頭に戻る");
        }

        // ───────── 5. Shuffle ─────────

        private void RunShuffleScenario()
        {
            Section("5. Shuffle");

            var normal = NewSession();
            normal.SetTracks(_trackIds);
            Line($"Shuffle OFF: [{JoinQueue(normal)}]");

            var shuffled = NewSession();
            shuffled.ShuffleEnabled = true;
            shuffled.SetTracks(_trackIds);
            Line($"Shuffle ON : [{JoinQueue(shuffled)}]");
        }

        // ───────── 6. Auto Queue / Recommendation ─────────

        private void RunAutoQueueScenario()
        {
            Section("6. Auto Queue(おすすめ連携)");

            // ON: 曲が 1 つでも再生が続く
            var (on, onBackend) = BuildSession();
            on.SetTracks(new[] { "music-001" });
            on.Play();
            Line($"AutoQueue ON : 1 曲だけ設定 -> Queue=[{JoinQueue(on)}]");
            Line($"  補充分: [{JoinRecommended(on)}]");

            onBackend.SimulateEnded();
            Line($"  再生終了 -> {Describe(on)} (おすすめで再生が続く)");

            // OFF: 尽きたら止まる
            var (off, offBackend) = BuildSession();
            off.AutoQueueEnabled = false;
            off.SetTracks(new[] { "music-001" });
            off.Play();
            offBackend.SimulateEnded();
            Line($"AutoQueue OFF: 再生終了 -> {Describe(off)} (Exhausted で止まる)");
        }

        // ───────── 7. セッション管理 ─────────

        private void RunSessionManagerScenario()
        {
            Section("7. PlayerSessionManager(複数セッション)");

            var manager = new PlayerSessionManager();
            var engine = NewEngine();

            var (playerA, backendA) = NewPlayer();
            var a = manager.Create(playerA, _catalog, engine, "room-1", "部屋1", new System.Random(1));
            a.RegisterBackend(backendA);
            a.SetTracks(new[] { "music-001", "music-002" });

            var (playerB, backendB) = NewPlayer();
            var b = manager.Create(playerB, _catalog, engine, "room-2", "部屋2", new System.Random(2));
            b.RegisterBackend(backendB);
            b.SetTracks(new[] { "music-009" });
            b.RepeatMode = RepeatMode.One;

            Line($"作成: {manager.Count} セッション / 有効 = {manager.Active.Id}");

            a.Play();
            Line($"  room-1 再生開始 -> {Describe(a)}");

            manager.SetActive("room-2");
            Line($"有効を room-2 へ切り替え -> 有効 = {manager.Active.Id}");
            Line($"  room-1: {Describe(a)} (切り替え時に停止する)");
            Line($"  room-2: {Describe(b)} (設定は独立: Repeat={b.RepeatMode})");
        }

        // ───────── 8. 実際に音を鳴らす ─────────

        private IEnumerator RunRealPlaybackScenario()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 8. AudioBackend で実際に再生する(音が鳴ります) ===");

            var logger = new ListBackendLogger();
            IMediaCatalog catalog = new MediaCatalog(new DummyCatalogSource());

            var host = GetComponent<AudioBackendHost>();
            if (host == null) host = gameObject.AddComponent<AudioBackendHost>();
            host.EnsureBuilt(logger);
            host.PrepareClipsFor(catalog);

            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, logger);
            var player = new MediaPlayer(backendManager, logger);
            var engine = new RecommendationEngine(catalog, RecommendationRule.CreateDefault());

            var session = new PlayerSession(
                "audio", player, catalog, engine, new System.Random(1), logger);
            session.RegisterBackend(host.Backend);
            session.RegisterBackend(new DummyBackend("DummyBackend", logger));

            session.SetTracks(_trackIds);
            session.Play();
            sb.AppendLine($"  開始: {Describe(session)}");

            float waited = 0f;
            string lastId = session.CurrentMediaId;

            while (waited < _listenSeconds)
            {
                waited += Time.deltaTime;

                if (session.CurrentMediaId != lastId)
                {
                    sb.AppendLine($"  {waited:0.0}s: 曲が変わりました -> {Describe(session)}");
                    lastId = session.CurrentMediaId;
                }

                yield return null;
            }

            session.Stop();

            foreach (var line in logger.Lines) sb.AppendLine("    " + line);
            sb.Append($"  最終: {Describe(session)}");

            Debug.Log(sb.ToString());
        }

        // ───────── 組み立て / 出力 ─────────

        /// <summary>バックエンドを直接触らない場面用。</summary>
        private PlayerSession NewSession()
        {
            return BuildSession().Item1;
        }

        private (PlayerSession, DummyBackend) BuildSession()
        {
            var (player, backend) = NewPlayer();
            var session = new PlayerSession(
                "demo", player, _catalog, NewEngine(), new System.Random(1));
            session.RegisterBackend(backend);
            return (session, backend);
        }

        private (MediaPlayer, DummyBackend) NewPlayer()
        {
            var queue = new MediaQueue();
            var backendManager = new BackendManager(queue, null);
            var player = new MediaPlayer(backendManager, null);
            // DummyBackend は音を鳴らさず SimulateEnded() で曲の終わりを起こせるので、
            // 再生を待たずに Ended 連携を確認できる。
            return (player, new DummyBackend("DemoBackend", null, MediaType.Music));
        }

        private RecommendationEngine NewEngine()
        {
            return new RecommendationEngine(
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
        }

        private static string Describe(PlayerSession session)
        {
            return $"[{session.PlaybackState}] {session.CurrentMediaId ?? "(none)"}"
                   + $" Queue={session.Queue.Count} 履歴={session.History.Count}";
        }

        private static string JoinQueue(PlayerSession session)
        {
            return session.Queue.Count == 0
                ? "空"
                : string.Join(", ", session.Queue.GetAll().Select(x => x.MediaId));
        }

        private static string JoinRecommended(PlayerSession session)
        {
            var ids = session.Queue.GetAll()
                .Where(x => x.Source == QueueItemSource.Recommendation)
                .Select(x => x.MediaId)
                .ToArray();
            return ids.Length == 0 ? "なし" : string.Join(", ", ids);
        }

        private static string Join(System.Collections.Generic.IReadOnlyList<string> ids)
        {
            return ids.Count == 0 ? "なし" : string.Join(", ", ids);
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
