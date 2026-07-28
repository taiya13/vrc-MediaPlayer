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

## Phase2-3(A): Playlist & Auto Queue(実装済み)

Playlist・Queue・Recommendation を統合し「聴き続けられる」再生体験を実現。`Playlist` は **MediaId のみ**を保持し(Music/Video の区別を持たないので将来 VideoBackend でもそのまま使える)、作成/削除/保存/読込/複製/並び替え/曲の追加・削除・移動に対応。`AutoQueueService` が Playlist から Queue を生成し、5 つの再生モード(Normal / RepeatOne / RepeatAll / Shuffle / Shuffle+AutoQueue)に応じて維持、尽きそうなら Recommendation で自動補充する。**Backend・Catalog・MediaPlayer は変更していない**(MediaPlayer は Playlist の存在を知らない)。

- コード: `Assets/SmartMediaPlatform/Playlists/`(Runtime / Demo / Editor / Scenes / Tests)
- DemoScene: `Playlists/Scenes/Phase2PlaylistDemoScene.unity` を開いて **Play**
- 設計ドキュメント(クラス図 / API一覧 / 再生モード / Recommendation連携 / Console出力例 / テスト方法 / 引き継ぎ): [docs/Phase2-3_PlaylistAutoQueue.md](docs/Phase2-3_PlaylistAutoQueue.md)

## Phase2-3(B): Player Session(実装済み)

プレイヤーの再生状態を一元管理する中核 `PlayerSession`。Catalog / Recommendation / Queue / Backend / Playback をまとめる**オーケストレーター**で、**利用者は PlayerSession だけを触れば再生できる**。現在の MediaId・Queue・履歴・RepeatMode・Shuffle・AutoQueue・Backend状態・Playback状態を保持し、Ended を受けて次の曲を決める(Repeat One は同じ曲、それ以外は次へ)。Queue が尽きても Recommendation で補充して再生が続く。**Backend の具体型を一切参照しない**ので将来 VideoBackend でもそのまま動く。`PlayerSessionManager` で複数セッション(部屋ごと等)にも対応。

- コード: `Assets/SmartMediaPlatform/Session/`(Runtime / Demo / Editor / Scenes / Tests)
- DemoScene: `Session/Scenes/Phase2SessionDemoScene.unity` を開いて **Play**
- 設計ドキュメント(クラス図 / 状態一覧 / API / Ended フロー / Console出力例 / テスト方法 / 引き継ぎ): [docs/Phase2-3_PlayerSession.md](docs/Phase2-3_PlayerSession.md)

> Phase2-3 は Playlist(A)と PlayerSession(B)の 2 本立てです。両者の機能の重なりと使い分けは [PlayerSession ドキュメント §8](docs/Phase2-3_PlayerSession.md) を参照してください。

## Phase2-4(A): Backend Adapter(実装済み)

MusicBackend と VideoBackend を同じ窓口で扱う Adapter 層。`IBackendAdapter` を **`IMediaBackend` の拡張**にしたことで、**PlayerSession・MediaPlayer・BackendManager を 1 行も変更せず**にアダプタを差し込める。`AudioBackendAdapter`(内側の再生位置をそのまま通す)と `DummyVideoBackendAdapter`(内側が持たない再生位置をアダプタが肩代わり)が、**同じ API で違う中身を吸収**する。VideoBackend を足しても上位が変わらないことをテストで証明済み。VRChat SDK / VideoPlayer は未使用(動画はログのみ)。

- コード: `Assets/SmartMediaPlatform/Adapter/`(Runtime / Audio / Demo / Editor / Scenes / Tests)
- DemoScene: `Adapter/Scenes/Phase2AdapterDemoScene.unity` を開いて **Play**
- 設計ドキュメント(設計判断 / クラス図 / API一覧 / 違いの吸収 / Console出力例 / テスト方法 / 引き継ぎ): [docs/Phase2-4_BackendAdapter.md](docs/Phase2-4_BackendAdapter.md)

## Phase2-4(B): Video Backend Adapter(実装済み)

**VRChat の VideoPlayer に依存するコードを Backend 層だけに閉じ込める**ための Adapter。`IVideoBackend` を「実機の動画プレイヤーの形」(URL・秒・独自の状態 `VideoPlayerState`・非同期読み込み)にしたうえで、`VideoBackendAdapter` が **5 つの違い**(対象 / 状態 / シーク / 通知 / 読み込み)を翻訳する。`DummyVideoBackend` は**ログのみで動画は再生しない**。`Video/Runtime` の asmdef は `noEngineReferences: true` なので、SDK 依存の混入をビルドレベルで防いでいる。**Backend の種類を判断するのは `BackendManager` だけ**で、PlayerSession / Queue / Recommendation に `MediaType` の分岐は 1 つもない。

- コード: `Assets/SmartMediaPlatform/Video/`(Runtime / Demo / Editor / Scenes / Tests)
- DemoScene: `Video/Scenes/Phase2VideoBackendDemoScene.unity` を開いて **Play**
- 設計ドキュメント(設計判断 / クラス図 / API一覧 / 5つの違いの吸収 / Console出力例 / テスト方法 / Phase3引き継ぎ): [docs/Phase2-4_VideoBackendAdapter.md](docs/Phase2-4_VideoBackendAdapter.md)

> Phase2-4 は Backend Adapter(A)と Video Backend Adapter(B)の 2 本立てです。(A)の `DummyVideoBackendAdapter` と(B)の `VideoBackendAdapter` は役割が重なっており、Phase3 では後者への一本化を推奨します([詳細](docs/Phase2-4_VideoBackendAdapter.md#9-設計レビュー自己評価))。

## Phase3-1: VRChat Video Backend(実装済み)

Phase2-4(B) の `DummyVideoBackend`(ログのみ)を、**VRChat の VideoPlayer で実際に動画を再生する** `VRChatVideoBackend` へ置き換え。`IVideoBackend` を実装しているだけなので、**`VideoBackendAdapter` を 1 文字も変更せず**差し替えられる(`PlayerSession` / `Queue` / `Recommendation` も差分ゼロ)。`VRCUnityVideoPlayer` と `VRCAVProVideoPlayer` は共通基底 `BaseVRCVideoPlayer` を包む 1 つのブリッジで両対応。**動画 URL は Catalog から編集時に焼き込んだ `VRCUrl` のみ**を使い、実行時に URL を生成する処理はどこにも無い(ベイクされていない URL は `CanPlay` の時点で拒否)。読み込み完了・再生終了・エラーは、実機コールバック(プッシュ)と `Tick()`(ポーリング)の両経路で拾い、通知は必ず 1 回。SDK 依存は 3 ファイル + 専用 asmdef(`defineConstraints: VRC_SDK_VRCSDK3`)に閉じ込めてあるので、**SDK が無い環境でも既存テストはそのまま通る**。

- コード: `Assets/SmartMediaPlatform/Video/Runtime`(純粋C#)、`Video/VRChat`(SDK 依存)、`Demo` / `Editor` / `Scenes` / `Tests`
- ConsoleDemo(SDK 不要): `Video/Scenes/Phase3VRChatVideoDemoScene.unity` を開いて **Play**
- DemoScene(SDK で実際に再生): メニュー **Tools > Smart Media Platform > Create Phase3 VRChat SDK Video Scene (実際に再生)** で生成して **Play**
- 設計ドキュメント(差分の全体像 / 2段構成の理由 / 実行時URL生成をしない仕組み / 非同期読み込みの吸収 / API一覧 / テスト方法 / 設計レビュー / 引き継ぎ): [docs/Phase3-1_VRChatVideoBackend.md](docs/Phase3-1_VRChatVideoBackend.md)

## Phase3-2: VRChat Video Event Bridge(実装済み)

Phase3-1 の `Tick()` によるポーリング(状態を見て「終わったはず」と推測する)に加えて、**VRChat の VideoPlayer が実際に発火したイベントを Backend へ届ける経路**を正式に用意。中心は `VideoEventBridge`(純粋C#)で、**重複排除**(同じイベントが何度届いても上位への通知は 1 回)、**`Tick()` との調停**(イベントが届いたらポーリングによる終了推測を自動で降ろす。タイムアウト監視は保険として残す)、**証跡**(受理/棄却の集計と履歴。「イベントで動いた」ことを Console で証明できる)の 3 つを担当する。VRChat はイベントを同じ GameObject の UdonBehaviour にしか送らず UdonSharp は interface を扱えないため、`UdonVRCVideoEventRelay`(UdonSharp)がイベントを int のリングバッファへ記録し、`UdonVideoEventPump` が累計カウンタの差分だけを読み出して運ぶ。**判断ロジックは SDK 側に 1 つも無く**、符号の解釈(`VideoEventCodec`)も重複排除も純粋C#側にあるので EditMode で検証できる。`PlayerSession` / `Queue` / `Recommendation` / `BackendAdapter` は 1 行も変更していない。

- コード: `Assets/SmartMediaPlatform/Video/Runtime`(ブリッジ本体)、`Video/Udon`(Udon 中継)、`Video/VRChat`(運搬役)、`Demo` / `Editor` / `Scenes` / `Tests`
- ConsoleDemo(SDK 不要): **Tools > Smart Media Platform > Open Phase3-2 Video Event Demo Scene** → **Play**
- DemoScene(SDK で実機イベント): **Tools > Smart Media Platform > Create Phase3-2 VRChat SDK Video Event Scene (実機イベント)** で生成して **Play**
- 設計ドキュメント(なぜブリッジが要るか / 重複排除 / Tick 調停 / Udon からの運び方 / テスト方法 / 設計レビュー / 引き継ぎ): [docs/Phase3-2_VRChatVideoEventBridge.md](docs/Phase3-2_VRChatVideoEventBridge.md)

## Phase3-3: Recommendation Playback Integration(実装済み)

Phase3-2 までの部品を **1 本の再生フロー**につないだ接続層。`RecommendationEngine → Queue → PlayerSession → BackendAdapter → VRChatVideoBackend` が **Ended イベントだけで回り続ける**ようになった。中心は `RecommendationPlaybackService`(純粋C#)。

Phase3-3 が足した判断は **1 つだけ**:「**積む直前に、再生できるか確かめる**」。おすすめは Catalog 全体から候補を返すため、VideoBackend しか登録していない構成では Music が混ざり、`BackendManager.LoadCurrent()` が失敗して再生が止まっていた。この判定を `IPlaybackFilter` に切り出し、`BackendPlaybackFilter` は **`BackendManager.CanPlay()` に直接聞く**ので、`MediaType` の分岐をどこにも書かずに済む(構成が挙動を決める)。Recommendation からは **MediaId(string)だけ**を受け取り、Queue へは `PlayerSession.Enqueue()` 経由で積むので、**URL(VRCUrl)を知るのは引き続き VideoBackend だけ**。おすすめが尽きても直近の除外を緩めて回り続ける。動画 10 本の `VideoCatalogSource` を `IMediaCatalogSource` の実装として追加(Catalog の設計は無変更)。**Catalog / Recommendation / Queue / PlayerSession / BackendAdapter / VRChatVideoBackend は 1 行も変更していない**(差分は新規追加のみ)。

- コード: `Assets/SmartMediaPlatform/AutoPlay/`(Runtime / VRChat / Demo / Editor / Scenes / Tests)、`Video/Runtime/Data/VideoCatalogSource.cs`
- ConsoleDemo(SDK 不要): **Tools > Smart Media Platform > Open Phase3-3 Recommendation Playback Demo Scene** → **Play**
- DemoScene(SDK で実際に再生): **Tools > Smart Media Platform > Create Phase3-3 VRChat SDK Auto Play Scene (実際に再生)** で生成して **Play**
- 設計ドキュメント(見つけた穴 / 責務の置きどころ / フィルタ 3 実装 / Ended の受け取り順 / テスト方法 / 引き継ぎ): [docs/Phase3-3_RecommendationPlaybackIntegration.md](docs/Phase3-3_RecommendationPlaybackIntegration.md)

## Phase3-4: Playback Orchestration & Recovery(実装済み)

Phase3-3 の再生ループは **失敗すると止まりました**(実在しない URL・アクセス拒否・読み込みが返らない)。Phase3-4 は新機能を足さず、**Ended / Error / Timeout のどれが来ても止まらない再生制御**へ整理したものです。中心は `AutoPlayController`(純粋C#)。`Ended` では何もせず(進めるのは今までどおり `PlayerSession`)、**失敗の記録と「次へ進める合図」だけ**を引き受けます。

あわせて、依頼された 7 点を整理しました。**補充ロジックの一本化**:4 箇所に散っていた「おすすめで Queue を埋める」処理を `IQueueRefiller` / `RecommendationQueueRefiller`(Queue 層)の 1 実装に集約し、違いは設定 3 つ(`RecentMemory` / `AllowRepeatWhenExhausted` / `AllowCatalogFallback`)で表現。`QueueManager` は候補数の取り方まで元のままなので **Phase1-4 の期待順位がそのまま通ります**。**おすすめの抽象化**:`IRecommendationEngine` を追加(アルゴリズムは 1 行も変更せず、`: IRecommendationEngine` を付けただけ)。**`AutoQueueEnabled` の整理**:利用側がセッションの設定を書き換えるのをやめ、**補充の手段だけを渡す**形(`session.QueueRefiller = refiller`)にしたので、`PlayerSession` からは `_recentlyRecommended` が消えて**むしろ小さくなりました**。**Error Recovery**:`PlaybackFailureTracker` が失敗した動画をクールダウン + バックオフ付きで覚え、**それ自体が `IPlaybackFilter`** なので「再生できる種別か」とは `CompositePlaybackFilter` で並べるだけ。**状態の整理**:`AutoPlayState`(Idle / Starting / Loading / Ready / Playing / Paused / Recovering / Stopped / Failed)を層ごとの列挙型として追加。**カタログの整理**:`CompositeCatalogSource` / `FilteredCatalogSource` / `InMemoryCatalogSource` を用意し、Catalog Builder を差し込むだけにしました。**耐久**:120 本連続・失敗混在 120 本・200 本のメモリ上限テストで、記憶(失敗 / 直近 / 履歴)が増え続けないことを確認。

**`BackendAdapter` / `VideoBackend` / `MediaQueue` / `RecommendationEngine` のアルゴリズムは差分ゼロ**、VRCUrl を知るのは引き続き `VideoBackend` だけです。

- コード: `Assets/SmartMediaPlatform/AutoPlay/`、`Queue/Runtime`(補充の共有実装)、`Recommendation/Runtime/IRecommendationEngine.cs`、`Catalog/Runtime/Data/CatalogSources.cs`
- ConsoleDemo(SDK 不要): **Tools > Smart Media Platform > Open Phase3-4 Auto Play Recovery Demo Scene** → **Play**
- DemoScene(SDK で実際に再生): **Tools > Smart Media Platform > Create Phase3-4 VRChat SDK Auto Play Recovery Scene (実際に再生)** で生成して **Play**(URL をわざと一部だけ焼き込むので、実機と同じ `InvalidUrl` 経路で立て直しが動きます)
- 設計ドキュメント(7 つの整理 / 立て直しの仕組み / 状態の分け方 / テスト方法 / 設計レビュー / 引き継ぎ): [docs/Phase3-4_PlaybackOrchestrationAndRecovery.md](docs/Phase3-4_PlaybackOrchestrationAndRecovery.md)

## Phase4-1: Media Library(実装済み)

**Catalog の中身をユーザーが閲覧し、選んで再生できるようにする層。** 中心は `MediaLibrary`(純粋C#)で、役割は**閲覧と選択の 2 つだけ**です。一覧の表示、Music / Video の絞り込み(`ShowOnly`)、並べ替え(登録順 / タイトル / アーティスト / ジャンル / 長さ)、1 件の選択(位置・ID・前後移動)を持ちます。**選択は位置ではなく ID で覚え直す**ので、並び順や絞り込みを変えても選んでいたものは選ばれたまま、見えなくなったときだけ外れます。

**「閲覧と選択だけ」を参照関係で守っています。** `SmartMediaPlatform.Library` の asmdef は **`Catalog` しか参照していない**(`noEngineReferences: true`)ので、`MediaLibrary` からは `PlayerSession` も `Queue` も `Backend` も**そもそも見えません**。両側が見えるのは `LibraryPlaybackBridge`(別 asmdef)**1 クラスだけ**で、**境界を越えるのは MediaId(string)だけ**です — `MediaItem` も URL も渡さず、`MediaLibraryFormatter` にも URL の出力口がありません(Phase3 からの「URL を知るのは VideoBackend だけ」を表示側でも維持)。受け渡しは `PlayerSession` の既存 API(`SetTracks` / `Enqueue` / `Play`)だけを呼ぶので、**`PlayerSession` / `BackendAdapter` / `Queue` / `Recommendation` / `VideoBackend` は差分ゼロ**です。

UI は **Game ビューに一覧を描いてクリックで選べる画面**(`MediaLibraryScreenDemo`)を用意しました。判断ロジックは 1 つも持たず「描いて、押されたことを伝えるだけ」なので、**uGUI や Udon の UI に差し替えても下は 1 行も変わりません**(ワールド内 UI は Phase4-2 以降)。あわせて設計方針どおり、実機再生の標準を `VRCAVProVideoPlayer` に切り替えました。`VRChatVideoBackendHost` に `VideoPlayerPreference`(AVPro / Unity / Explicit)を足し、**Inspector のドロップダウン 1 つで両対応**します。抽象化は Phase3-1 のまま(`BaseVRCVideoPlayer` を 1 クラスで包む)で、上位はどちらが繋がっているか知りません。

- コード: `Assets/SmartMediaPlatform/Library/`(Runtime / Playback / Demo / Editor / Scenes / Tests)、`Video/VRChat/Runtime/VideoPlayerPreference.cs`
- DemoScene(SDK 不要・クリックで操作): **Tools > Smart Media Platform > Open Phase4-1 Media Library Demo Scene** → **Play**
- ConsoleDemo: 同じシーンで **Play**(1 つの GameObject に両方載っています)
- 設計ドキュメント(責務の守り方 / クラス一覧 / UI の方針 / バックエンド切り替え / テスト方法 / 引き継ぎ): [docs/Phase4-1_MediaLibrary.md](docs/Phase4-1_MediaLibrary.md)

## Phase4-2: 関連動画 UI と Catalog 接続(実装済み)

**関連動画 UI を Catalog システムへつなぎ、将来の Catalog Builder / サーバー連携に備えた層。** 中心は 3 つの新しい型です。**`DisplayMeta`**(表示用・**URL を持たない**)、**`PlayableRef`**(再生用・**MediaId と Type だけ**を運ぶ struct)、そして **`ICatalogStore` / `CatalogStore`**(唯一のデータ取得窓口)。`ICatalogStore` には **`MediaItem` を返すメンバーが 1 つもない**ので、利用側はカタログの内部構造を知りません。Phase4-1 では `MediaItem` を UI へ渡していて `MediaItem.Url` が見えていましたが、いまは型のレベルで塞がっています。

**関連動画は `RelatedMediaView`** が担当します。`起点の MediaId → IRelatedMediaProvider(関連 ID の並び)→ CatalogStore(DisplayMeta へ変換)→ UI → PlayableRef → PlayerSession` という流れで、**`RelatedMediaView` は「なぜ関連なのか」を知りません**。だから関連の出どころがカタログの `RelatedIds`(いま)でも、Catalog Builder の事前計算でも、**サーバーが返した ID**でも、この層から上は 1 行も変わりません。カタログに無い ID がサーバーから返ってきても `CatalogStore` が黙って落とすので、残りはそのまま並びます。

**将来の差し込み口は 2 つだけ**です。Catalog Builder は **`MediaCatalogAsset`**(`ScriptableObject`, `IMediaCatalogSource` 実装)へ書き込むだけ、サーバー連携は **`IRelatedMediaProvider`**(用意済みの `StaticRelatedMediaProvider` に `Set(id, ids)` するだけ)。どちらも利用側の変更は最小です。あわせて「並べて 1 つ選ぶ」共通契約 `IMediaListView` を切り出したので、**カタログ全体の一覧と関連動画の一覧が同じ UI・同じ Formatter・同じ `LibraryPlaybackBridge` で扱えます**。再生エンジン(`PlayerSession` / `BackendAdapter` / `Queue` / `Recommendation` / `VideoBackend`)と `MediaItem` / `IMediaCatalog` は**差分ゼロ**です。

- コード: `Assets/SmartMediaPlatform/Catalog/Runtime/Store/`(DisplayMeta / PlayableRef / CatalogStore / IRelatedMediaProvider)、`Catalog/Assets/`(MediaCatalogAsset)、`Library/Runtime/`(IMediaListView / RelatedMediaView)
- DemoScene: **Tools > Smart Media Platform > Open Phase4-1 Media Library Demo Scene** → **Play**(右下に関連動画パネル。▶ を押すと再生が切り替わり、関連一覧も追従します)
- 設計ドキュメント(追加クラス一覧 / データの流れ / Catalog Builder・サーバーとの接続点 / 確認項目): [docs/Phase4-2_RelatedMediaAndCatalogStore.md](docs/Phase4-2_RelatedMediaAndCatalogStore.md)

## テスト

EditMode テスト計 **829 ケース**(Catalog 95 / Recommendation 13 / Queue 72 / Backend 44 / Integration 9 / Audio 54 / Player 35 / Playlists 70 / Session 58 / Adapter 35 / Video 143 / AutoPlay 94 / Library 107)。
Unity の **Window > General > Test Runner > EditMode > Run All** で実行(VRChat SDK 不要)。
