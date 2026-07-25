# vrc-MediaPlayer

VRChat 向け Smart Media Platform。

## Phase1-1: Catalog System(実装済み)

メディア情報の管理・検索基盤。再生・UI・ネットワークには依存しない独立レイヤー(純粋 C#)。

- コード: `Assets/SmartMediaPlatform/Catalog/Runtime`, `Demo`, `Tests`
- 設計ドキュメント(アーキテクチャ図 / クラス図 / API 一覧 / 設計レビュー / Unity でのテスト方法): [docs/Phase1-1_CatalogSystem.md](docs/Phase1-1_CatalogSystem.md)

## Phase1-2: Udon Compatibility Layer(実装済み)

Phase1-1 の設計思想を維持したまま、UdonSharp で使える Adapter 層に変換。`interface` / `Dictionary` / カスタムクラスを使わず、並列配列 + `int` index 返し + VRChat の `DataDictionary` で Catalog API を再現。

- コード: `Assets/SmartMediaPlatform/Catalog/Runtime/Parallel`(純粋C#・テスト対象)、`Assets/SmartMediaPlatform/Catalog/Udon`(UdonSharp)
- 設計ドキュメント(アーキテクチャ図 / クラス図 / API対応表 / 設計レビュー / Unity・実機でのテスト方法 / 引き継ぎ): [docs/Phase1-2_UdonCompatibilityLayer.md](docs/Phase1-2_UdonCompatibilityLayer.md)

## Phase1-3: Recommendation Engine(実装済み)

Catalog System の上に載る、ルールベース(AI 非使用)のおすすめエンジン。同アーティスト / 同ジャンル / タグ一致 / RelatedIds / ランダム補正の重み付き合計で順位を決定。Catalog API にのみ依存(asmdef 参照レベルで強制)。純粋C#版 + Udon版。

- コード: `Assets/SmartMediaPlatform/Recommendation/Runtime`(純粋C#・テスト対象)、`Assets/SmartMediaPlatform/Recommendation/Udon`(UdonSharp)
- 設計ドキュメント(アーキテクチャ図 / クラス図 / スコアリング仕様 / 期待順位 / 設計レビュー / テスト方法 / 引き継ぎ): [docs/Phase1-3_RecommendationEngine.md](docs/Phase1-3_RecommendationEngine.md)

## Phase1-4: Queue System(実装済み)

Recommendation Engine が決定した曲を管理する純粋な Queue。Player・UI・ネットワーク・VRChat SDK に依存せず(asmdef で強制)、将来の Music/Video Player・Shared Queue・DJ Mode・Auto Play・Vote System すべてから `IQueue` 経由で利用できる。キューが尽きそうなときに Recommendation Engine から自動補充する `QueueManager` を同梱(自動再生は未実装)。

- コード: `Assets/SmartMediaPlatform/Queue/Runtime`(純粋C#)、`Demo`、`Tests`
- 設計ドキュメント(クラス図 / ディレクトリ構成 / Queue API一覧 / Console出力例 / 設計レビュー / 将来改善点 / 引き継ぎ / テスト方法): [docs/Phase1-4_QueueSystem.md](docs/Phase1-4_QueueSystem.md)

## Phase1-5: Backend Interface(実装済み)

Queue と Player の間に位置する共通 Backend Interface。Music / Video / Live / Podcast を同一 API で扱い、`CanPlay()` によって種別ごとのバックエンドが選ばれる。`DummyBackend` は**実際の再生を行わずログのみ**出力する。Catalog・Recommendation・Queue にのみ依存(asmdef で強制)。

- コード: `Assets/SmartMediaPlatform/Backend/Runtime`(純粋C#)、`Demo`、`Tests`
- 設計ドキュメント(クラス図 / 状態遷移図 / API一覧 / Console出力例 / 設計レビュー / Phase2引き継ぎ / テスト方法): [docs/Phase1-5_BackendInterface.md](docs/Phase1-5_BackendInterface.md)

## Phase1 最終統合テスト(DemoScene)

Catalog → Recommendation → Queue → Backend を 1 つの Unity シーンで連携させ、Console だけで一連の動作を確認できる統合テスト。実際の再生は行わない(DummyBackend)。**28 の成功条件**を各ステップのログとともに判定・出力する。

- シーン: `Assets/SmartMediaPlatform/Integration/Scenes/Phase1DemoScene.unity` を開いて **Play**
  - メニュー **Tools > Smart Media Platform > Run Phase1 Integration Test (Console)** なら Play 不要
- コード: `Assets/SmartMediaPlatform/Integration/`
- 設計ドキュメント(構成 / 実行方法 / Console出力例 / 成功条件一覧 / Phase2引き継ぎ): [docs/Phase1-Final_IntegrationTest.md](docs/Phase1-Final_IntegrationTest.md)

## Phase2-1: Audio Backend(実装済み)

Unity の `AudioSource` を使って**実際に音楽を再生する** Backend。Phase1-5 の `IMediaBackend` を実装しているだけなので、**`BackendManager` を一切変更せず** DummyBackend と差し替えられる。曲の終わりを検出して `Ended` を通知する。音源ファイルが無くても動作確認できるよう、ID から短い音を自動生成する仕組みを同梱。

- コード: `Assets/SmartMediaPlatform/Audio/`(Runtime / Demo / Tests)
- 設計ドキュメント(クラス図 / 再生経路 / 設計の要点 / 使い方 / テスト方法 / Phase2-2引き継ぎ): [docs/Phase2-1_AudioBackend.md](docs/Phase2-1_AudioBackend.md)

## Phase2-2: Playback Control(実装済み)

再生制御の統一 API(`MediaPlayer`)。Play / Pause / Resume / Stop / SkipNext / SkipPrevious / TogglePlayPause / Seek / 再生位置取得 を提供し、曲が終わったら Queue の次へ自動で進んで再生を続ける。**純粋C#(UnityEngine 非依存)**なので、AudioBackend でも将来の VideoBackend でも同じコードが動く。シーク対応可否は `ISeekableBackend` の有無で判定するため、**`IMediaBackend` は今後変更不要**。`BackendManager`・`Queue` は変更していない。

- コード: `Assets/SmartMediaPlatform/Player/`(Runtime / Demo / Editor / Scenes / Tests)
- DemoScene: `Player/Scenes/Phase2PlaybackDemoScene.unity` を開いて **Play**
- 設計ドキュメント(クラス図 / API一覧 / 設計の要点 / Console出力例 / テスト方法 / 引き継ぎ): [docs/Phase2-2_PlaybackControl.md](docs/Phase2-2_PlaybackControl.md)

## Phase2-3: Playlist & Auto Queue(実装済み)

Playlist・Queue・Recommendation を統合し「聴き続けられる」再生体験を実現。`Playlist` は **MediaId のみ**を保持し(Music/Video の区別を持たないので将来 VideoBackend でもそのまま使える)、作成/削除/保存/読込/複製/並び替え/曲の追加・削除・移動に対応。`AutoQueueService` が Playlist から Queue を生成し、5 つの再生モード(Normal / RepeatOne / RepeatAll / Shuffle / Shuffle+AutoQueue)に応じて維持、尽きそうなら Recommendation で自動補充する。**Backend・Catalog・MediaPlayer は変更していない**(MediaPlayer は Playlist の存在を知らない)。

- コード: `Assets/SmartMediaPlatform/Playlists/`(Runtime / Demo / Editor / Scenes / Tests)
- DemoScene: `Playlists/Scenes/Phase2PlaylistDemoScene.unity` を開いて **Play**
- 設計ドキュメント(クラス図 / API一覧 / 再生モード / Recommendation連携 / Console出力例 / テスト方法 / 引き継ぎ): [docs/Phase2-3_PlaylistAutoQueue.md](docs/Phase2-3_PlaylistAutoQueue.md)

## テスト

EditMode テスト計 **332 ケース**(Catalog 63 / Recommendation 13 / Queue 44 / Backend 44 / Integration 9 / Audio 54 / Player 35 / Playlists 70)。
Unity の **Window > General > Test Runner > EditMode > Run All** で実行(VRChat SDK 不要)。
