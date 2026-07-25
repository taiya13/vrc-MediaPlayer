using System.Linq;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using SmartMediaPlatform.Queue;
using SmartMediaPlatform.Recommendation;

namespace SmartMediaPlatform.Integration
{
    /// <summary>
    /// Phase1 の最終統合シナリオ。
    /// Catalog → Recommendation → Queue → Backend を実際につないで一連の動作を確認し、
    /// 各手順のログと成功条件の判定を <see cref="IntegrationReport"/> に書き出す。
    ///
    /// 純粋 C#(UnityEngine 非依存)なので、Unity のシーンからも
    /// EditMode テストからも同じ内容を実行できる。
    /// 実際の再生は行わない(DummyBackend はログのみ)。
    /// </summary>
    public sealed class Phase1IntegrationScenario
    {
        /// <summary>おすすめとキュー投入の起点にする曲。</summary>
        public string SeedId { get; set; } = "music-001";

        /// <summary>自動補充後に目指すキューの長さ。</summary>
        public int QueueTargetCount { get; set; } = 5;

        /// <summary>
        /// 乱数の種。固定しておくことで、シーンで実行しても
        /// EditMode テストで実行しても同じ結果になる。
        /// </summary>
        public int RandomSeed { get; set; } = 1;

        /// <summary>ID 検索の検証に使う基準メディア(<see cref="SeedId"/> の設定に左右されない)。</summary>
        private const string ReferenceId = "music-001";
        private const string ReferenceTitle = "Neon Skyline";

        public IntegrationReport Run()
        {
            var report = new IntegrationReport();

            var catalog = RunCatalogStep(report);

            // 起点の曲が無ければ以降は成立しない。例外を投げずに失敗として報告して終える
            // (Inspector で ID を打ち間違えてもデモが落ちないようにするため)。
            if (catalog.FindById(SeedId) == null)
            {
                report.Info($"起点の曲 \"{SeedId}\" がカタログに無いため、以降の手順を中止します。");
                report.Summarize();
                return report;
            }

            var engine = RunRecommendationStep(report, catalog);
            var queue = RunQueueStep(report, catalog, engine);
            var manager = RunBackendStep(report, queue);
            RunEndToEndStep(report, catalog, queue, manager);

            report.Summarize();
            return report;
        }

        // ───── STEP 1: Catalog ─────

        private IMediaCatalog RunCatalogStep(IntegrationReport report)
        {
            report.Step("STEP 1 / Catalog — メディア情報の管理・検索");

            IMediaCatalog catalog = new MediaCatalog(
                new DummyCatalogSource(), new System.Random(RandomSeed));
            report.Info($"MediaCatalog を構築しました (items: {catalog.Count})");

            // ID 検索の検証は基準メディアで行う(SeedId を変えても壊れないようにするため)。
            var reference = catalog.FindById(ReferenceId);
            report.Info($"FindById(\"{ReferenceId}\") -> {(reference != null ? reference.ToString() : "(not found)")}");

            var seed = catalog.FindById(SeedId);
            report.Info($"起点の曲 FindById(\"{SeedId}\") -> {(seed != null ? seed.ToString() : "(not found)")}");

            var byTag = catalog.SearchByTag("night");
            report.Info($"SearchByTag(\"night\") -> {byTag.Count} 件: "
                        + string.Join(", ", byTag.Select(i => i.Id)));

            var videos = catalog.FilterByType(MediaType.Video);
            var podcasts = catalog.FilterByType(MediaType.Podcast);
            report.Info($"FilterByType: Video {videos.Count} 件 / Podcast {podcasts.Count} 件");

            report.Check(catalog.Count >= 10, "カタログに 10 件以上のメディアがある");
            report.Check(reference != null && reference.Title == ReferenceTitle,
                "ID 検索が正しいメディアを返す");
            report.Check(seed != null, $"起点の曲 \"{SeedId}\" がカタログに存在する");
            report.Check(byTag.Count > 0, "タグ検索が結果を返す");
            report.Check(videos.Count > 0 && podcasts.Count > 0,
                "Music 以外(Video / Podcast)も扱える構造になっている");

            return catalog;
        }

        // ───── STEP 2: Recommendation ─────

        private RecommendationEngine RunRecommendationStep(
            IntegrationReport report, IMediaCatalog catalog)
        {
            report.Step("STEP 2 / Recommendation — おすすめ順位の決定");

            // ランダム補正を 0 にして、順位を決定的に検証できるようにする。
            var rules = new[]
            {
                RecommendationRule.Related(10.0),
                RecommendationRule.SameArtist(5.0),
                RecommendationRule.SameGenre(3.0),
                RecommendationRule.TagMatch(1.0),
                RecommendationRule.Random(0.0),
            };
            var engine = new RecommendationEngine(catalog, rules, new System.Random(RandomSeed));
            report.Info("RecommendationEngine を構築しました (Related10 / Artist5 / Genre3 / Tag1 / Random0)");

            var results = engine.GetNextRecommendations(SeedId, 3);
            report.Info($"GetNextRecommendations(\"{SeedId}\", 3):");
            for (int i = 0; i < results.Length; i++)
            {
                report.Info($"  #{i + 1}  {results[i]}");
            }

            bool descending = true;
            for (int i = 1; i < results.Length; i++)
            {
                if (results[i].Score > results[i - 1].Score) descending = false;
            }

            report.Check(results.Length > 0, "Catalog を使っておすすめを取得できる");
            report.Check(descending, "結果がスコアの降順に並んでいる");
            report.Check(results.All(r => r.Item.Id != SeedId), "起点の曲自体は候補に含まれない");
            report.Check(results.Length > 0 && results[0].Score > 0,
                "最上位の候補は起点と何らかの関連を持つ(スコア > 0)");

            // 既定の起点でのみ、順位そのものを固定値で検証する。
            // (起点を変えた場合に無関係な条件が落ちないようにするため)
            if (SeedId == ReferenceId)
            {
                report.Check(results.Length > 0 && results[0].Item.Id == "music-002",
                    "スコア最上位が期待どおり(music-002: 関連+同アーティスト+同ジャンル+タグ一致)");
            }

            return engine;
        }

        // ───── STEP 3: Queue ─────

        private IQueue RunQueueStep(
            IntegrationReport report, IMediaCatalog catalog, RecommendationEngine engine)
        {
            report.Step("STEP 3 / Queue — 再生順の管理と自動補充");

            var queue = new MediaQueue();
            var manager = new QueueManager(queue, engine, catalog)
            {
                MinimumCount = 2,
                TargetCount = QueueTargetCount,
            };

            queue.Enqueue(catalog.FindById(SeedId));
            report.Info($"起点の曲を手動で Enqueue しました: {SeedId} (Count: {queue.Count})");

            int added = manager.EnsureFilled();
            report.Info($"EnsureFilled() -> {added} 件を自動補充 (Count: {queue.Count})");
            report.Info("キューの内容:");
            report.Info(QueueFormatter.FormatQueueVerbose(queue).Replace("\n", "\n  "));

            var all = queue.GetAll();
            int distinct = all.Select(x => x.MediaId).Distinct().Count();
            bool refilledAreRecommendation = all
                .Where(x => x.MediaId != SeedId)
                .All(x => x.Source == QueueItemSource.Recommendation);

            report.Check(added > 0, "Recommendation Engine と連携して自動補充できる");
            report.Check(queue.Count == QueueTargetCount,
                $"キューが目標件数({QueueTargetCount})まで補充されている");
            report.Check(distinct == all.Count, "補充されたキューに重複がない");
            report.Check(refilledAreRecommendation,
                "自動補充された曲は Recommendation 由来として記録されている");
            report.Check(queue.Peek() != null && queue.Peek().MediaId == SeedId,
                "先頭(Now)は手動で積んだ起点の曲のまま");

            return queue;
        }

        // ───── STEP 4: Backend ─────

        private BackendManager RunBackendStep(IntegrationReport report, IQueue queue)
        {
            report.Step("STEP 4 / Backend — 再生制御(実際には再生しない)");

            // Backend のログもレポートに流し込む
            var backendManager = new BackendManager(queue, report);
            var musicBackend = new DummyBackend("MusicBackend", report, MediaType.Music);
            var videoBackend = new DummyBackend("VideoBackend", report, MediaType.Video, MediaType.Live);
            backendManager.RegisterBackend(musicBackend);
            backendManager.RegisterBackend(videoBackend);
            report.Info("MusicBackend / VideoBackend を登録しました(いずれも再生はしません)");

            var head = queue.Peek();
            var selected = backendManager.SelectBackendFor(head.Item);
            report.Info($"SelectBackendFor({head.MediaId}) -> {(selected != null ? selected.Name : "(none)")}");

            // 種別に対応したバックエンドが選ばれること。起点の種別が変わっても
            // 正しく判定できるよう、期待値は種別から導く。
            IMediaBackend expected =
                head.Item.Type == MediaType.Music ? musicBackend :
                head.Item.Type == MediaType.Video || head.Item.Type == MediaType.Live ? videoBackend :
                null;
            report.Check(selected != null && ReferenceEquals(selected, expected),
                $"CanPlay により、メディア種別({head.Item.Type})に対応したバックエンドが選ばれる");

            report.Info("Load:");
            bool loaded = backendManager.LoadCurrent();
            report.Check(loaded && backendManager.GetState() == BackendState.Ready,
                "Queue の先頭を Backend に読み込める (State: Ready)");

            report.Info("Play:");
            backendManager.Play();
            report.Check(backendManager.GetState() == BackendState.Playing,
                "Play で再生状態になる (State: Playing)");

            report.Info("Pause:");
            backendManager.Pause();
            report.Check(backendManager.GetState() == BackendState.Paused,
                "Pause で一時停止する (State: Paused)");

            report.Info("Resume:");
            backendManager.Resume();
            report.Check(backendManager.GetState() == BackendState.Playing,
                "Resume で再生に戻る (State: Playing)");

            string beforeSkip = backendManager.GetCurrent().Id;
            report.Info("Skip:");
            backendManager.Skip();
            string afterSkip = backendManager.GetCurrent() != null
                ? backendManager.GetCurrent().Id : "(none)";
            report.Info($"Current: {beforeSkip} -> {afterSkip}");

            report.Check(afterSkip != beforeSkip, "Skip で次のメディアへ進む");
            report.Check(queue.Peek() != null && queue.Peek().MediaId == afterSkip,
                "Skip 後の Backend の曲と Queue の先頭が一致している");
            report.Check(backendManager.GetState() == BackendState.Playing,
                "再生中のスキップでは、次の曲も再生状態になる");

            report.Info("Stop:");
            backendManager.Stop();
            report.Check(backendManager.GetState() == BackendState.Stopped,
                "Stop で停止する (State: Stopped)");

            return backendManager;
        }

        // ───── STEP 5: 全体の一貫性 ─────

        private void RunEndToEndStep(
            IntegrationReport report, IMediaCatalog catalog, IQueue queue, BackendManager backendManager)
        {
            report.Step("STEP 5 / End-to-End — レイヤー間の一貫性");

            var current = backendManager.GetCurrent();
            report.Info($"Backend が保持している曲: {(current != null ? current.ToString() : "(none)")}");

            bool fromCatalog = current != null && catalog.FindById(current.Id) != null;
            bool sameInstance = current != null && ReferenceEquals(current, catalog.FindById(current.Id));
            bool inQueue = current != null && queue.Contains(current.Id);

            report.Check(fromCatalog, "Backend の曲は Catalog に存在する ID である");
            report.Check(sameInstance,
                "Catalog → Recommendation → Queue → Backend が同一のメディア実体を共有している");
            report.Check(inQueue, "Backend の曲は Queue 経由で渡ってきている");
            report.Check(queue.Count > 0, "Queue には後続の曲が残っている(継続再生の準備ができている)");
        }
    }
}
