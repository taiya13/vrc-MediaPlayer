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

## Phase4-3: 再生フローの完成(Library → Queue → Player)(実装済み)

**選んだものが Queue を経て再生されるまでを通しで触れるようにした層。** 追加は 3 クラスだけです。**`QueueView`**(Queue の中身を `DisplayMeta` で見せ、並べ替え・削除の窓口になる)、**`NowPlayingView`**(再生中を `MediaId → Catalog` で引き直して見せる。進捗も持つ)、**`PlaybackFlow`**(部品の組み立てと `Tick()` による同期)。`PlaybackFlow.Create(catalog, session)` の **1 行**で Library / 関連 / Queue / 再生中 が繋がります。

**`QueueView` が必要だった理由**は、`IQueue.GetAll()` が返す `QueueItem` が `MediaItem`(= `Url`)を抱えているためです。そのまま UI へ渡すと **Phase4-2 で塞いだ穴が Queue 経由で開きます**。`QueueView` は `MediaId` だけ取り出して `ICatalogStore` から引き直すので、URL は Queue の外へ出ません。**Queue の責務は増やしておらず**、並べ替えも削除も `IQueue` が Phase1-4 から持つ `Move` / `RemoveAt` / `Clear` を呼ぶだけです。Queue は「先頭 = いま鳴っているもの」で動くため、**先頭への並べ替え・削除は表示層で弾いています**(再生と Queue がずれないように)。

**`NowPlayingView` は「PlayerSession は MediaId 中心、表示は Catalog から」の実装そのもの**です。`PlayerSession.CurrentItem`(`MediaItem` を返す)は使わず、`CurrentMediaId`(string)から `CatalogStore` を引き直します。あわせて `LibraryPlaybackBridge` に **`PlayNext()`**(Queue の 2 番目へ入れる)を追加しました。**`PlaybackFlow` は再生の判断を持ちません** — `Play` / `Next` / `Stop` はあえて生やさず、次に何を再生するかは今までどおり `PlayerSession` の仕事です。再生エンジンと `IQueue` / `MediaItem` / `IMediaCatalog` は**差分ゼロ**。Catalog Builder・サーバー連携・AVPro 切り替えの差し込み口も **Phase4-2 から増えていません**。

- コード: `Assets/SmartMediaPlatform/Library/Playback/`(QueueView / NowPlayingView / PlaybackFlow)、`Library/Demo/`(Console / Screen)
- DemoScene: **Tools > Smart Media Platform > Open Phase4-3 Playback Flow Demo Scene** → **Play**(上=再生中 / 左=Library / 中=関連 / 右=Queue の 4 区画を操作できます)
- 設計ドキュメント(追加クラス / データフロー / 接続ポイント / 確認項目): [docs/Phase4-3_PlaybackFlow.md](docs/Phase4-3_PlaybackFlow.md)

## Phase4-4: VRChat VideoPlayer への接続(実装済み)

**Phase4-3 までのデモは `DummyBackend`(ログのみ)でした。それを実機の動画バックエンドへ差し替え、あわせて実機の標準を AVPro にした層。** 差し替えは **1 行**です — `backendManager.RegisterBackend(new DummyBackend(...))` を `RegisterBackend(host.EnsureBuilt(logger))` に変えるだけ。`host.EnsureBuilt()` が返すのは `VideoBackendAdapter`(= `IMediaBackend`)なので **`DummyBackend` と同じ口に入り**、`PlaybackFlow` も `PlayerSession` も `Queue` も `MediaLibrary` もこの違いを知りません。

**AVPro を既定にしました。** シーンを作る 4 つのメニューがすべて **AVPro を生成**します。AVPro と Unity 版では配線の形が違う（AVPro は出力先の側に `VRCAVProVideoScreen` / `VRCAVProVideoSpeaker` を付けてプレイヤーを指す）ため、その違いを吸収する **`VRChatVideoPlayerFactory`** を追加しました。SDK の型は**名前で探す**ので（`VRCVideoPlayerBridge.DetectKind` と同じ方針）、SDK 更新で名前空間が変わってもコンパイルは壊れず、配線できなかったものは Console に報告されます。実行時の切り替えは従来どおり `VRChatVideoBackendHost` の `Preferred Player`(Inspector)1 箇所です。

**SDK 無しでも通しを確認できます。** 実機バックエンドの検証を SDK 必須にすると SDK 未導入の環境で検証できなくなるため、`SimulatedVRCVideoPlayer` を使った `VideoBackendFlowConsoleDemo` と 16 ケースのテストを用意しました。`IVRCVideoPlayer` から上は実機とまったく同じ経路を通るので、**実機でしか確かめられないのは SDK の API 呼び出しだけ**に絞ってあります。SDK 依存は新しい asmdef `SmartMediaPlatform.Library.VRChat`(`defineConstraints: VRC_SDK_VRCSDK3`)に閉じ込めました。再生エンジンと `PlaybackFlow` / `MediaLibrary` / `QueueView` / `NowPlayingView` は**差分ゼロ**です。

- コード: `Assets/SmartMediaPlatform/Library/VRChat/`(SDK 版の画面)、`Video/Editor/VRChatVideoPlayerFactory.cs`、`Library/Demo/VideoBackendFlowConsoleDemo.cs`
- DemoScene(SDK 不要): **Tools > Smart Media Platform > Open Phase4-4 Video Backend Flow Demo Scene (SDK 不要)** → **Play**
- DemoScene(SDK で実際に再生): **Tools > Smart Media Platform > Create Phase4-4 VRChat Playback Scene (実際に再生)** で生成して **Play**(エディタで絵を見たい場合は Unity 版のメニュー)
- 設計ドキュメント(追加クラス / 再生フロー / 接続ポイント / 確認項目): [docs/Phase4-4_VRChatVideoPlayerIntegration.md](docs/Phase4-4_VRChatVideoPlayerIntegration.md)

## Phase5-1: SmartMediaPlayer Prefab(β)

**再生フローを「ワールドに置ける 1 つの Prefab」にまとめた層。** `SmartMediaPlayer` は 5 つの子（**Screen / Player / Controller / UI / Catalog**）を持ち、根っこの `SmartMediaPlayerRoot` は**組み立てだけ**を担当します。再生の判断は今までどおり `PlayerSession` の仕事で、`Play` / `Next` / `Stop` は根っこに生やしていません。

**部品はインターフェースで探します**（`IMediaScreen` / `IMediaController` / `IMediaPlayerUI` / `IMediaBackendProvider` / `ICatalogProvider`）。だから**子オブジェクトを差し替えるだけ**で交換でき、根っこも他の子も変わりません。見つからない部品には代役を立てるので、**Inspector の必須設定はゼロ** — Prefab をドラッグして `Screen/Surface` を見える位置へ動かすだけで動きます。部品が受け取るのは `MediaPlayerContext`（`ICatalogStore` / `PlaybackFlow` / `PlayerSession`）だけで、カタログの作り方もどのバックエンドが繋がっているかも見えず、**URL の口もありません**。

**将来の差し込み口は 2 つだけ**です。Catalog Builder は `ICatalogProvider`（`Catalog.asset` を割り当てるだけ）、バックエンド追加は `IMediaBackendProvider`（`DummyMediaBackendProvider` を `VRChatMediaBackendProvider` に置き換えるだけ）。Phase1〜4 のクラスは**すべて差分ゼロ**です。

> ℹ️ **この Prefab の役割は Phase5-2 で変わりました。** アップロードした VRChat ワールドでは独自 MonoBehaviour が動かないため、この C# 版は **エディタ / ClientSim で仕組みを確かめる用**になりました。ワールドに置いて実際に動かすのは Phase5-2 の Udon 版 (`SmartMediaPlayer.prefab`) です。

- コード: `Assets/SmartMediaPlatform/World/`（Runtime / VRChat / Editor / Prefabs / Tests）
- Prefab(SDK 不要): `World/Prefabs/SmartMediaPlayer_NoSDK.prefab` を **Hierarchy へドラッグ** → **Play**
- Prefab(SDK・実際に再生): **Tools > Smart Media Platform > Create SmartMediaPlayer Prefab (実際に再生)** で生成
- 設計ドキュメント(Prefab 構成 / Hierarchy / Inspector 項目 / 設置手順 / 5-2 の予定): [docs/Phase5-1_SmartMediaPlayerPrefab.md](docs/Phase5-1_SmartMediaPlayerPrefab.md)

## Phase5-2: 制御層の UdonSharp 移植(実装済み)

**Phase5-1 の Prefab を「アップロードしたワールドでそのまま動く」ところまで持っていった層。** VRChat のワールドで実行されるのは Udon だけなので、`SmartMediaPlayerRoot` / `MediaController` / `MediaPlayerUI` といった MonoBehaviour の制御層を **9 つの `UdonSharpBehaviour` へ移植**しました。`SmartMediaPlayer.prefab` を Hierarchy へドラッグして **Build & Test するだけで動きます**。

**責務の線は 1 本も動かしていません。** URL を知ってよいのは `UdonVideoBackend` だけ(`UdonCatalogStore` に `GetUrl` はありません)、再生の判断は `UdonPlayerSession` だけ(根っこに `Play` / `Next` / `Stop` は生やしていません)、Queue の先頭 = いま鳴っているもの、`VRCUrl` は実行時に作らない — Phase1〜4 で決めた約束はそのままです。変わったのは**実現の仕方**だけで、interface → Inspector 参照、`MediaItem` → catalog index、`List` → 固定長配列、`OnGUI` → uGUI の `Text` になりました。

**Udon へ写す前に純粋 C# で検証しています。** `UdonPlayerSession` のアルゴリズムは `PlaybackModel`(`World/UdonModel/`・EditMode **37 ケース**)で先に確かめ、それを 1 対 1 で写したものです。Phase1-2(`ParallelMediaCatalog` → `UdonMediaCatalog`)、Phase1-3(`RecommendationEngine` → `UdonRecommendationEngine`)と同じ手順です。Phase1〜5-1 のクラスは**すべて差分ゼロ**で、C# 版 Prefab も残してあります。

> ⚠️ **同期はまだありません。** 現在は `BehaviourSyncMode.None` で、各自のクライアントで別々に再生されます。「みんなで同じものを同じ位置で観る」にはネットワーク同期が要ります(Phase5-3 相当)。

**入口は 1 つにまとめました。** メニューが増えて「どれを使えばワールドで動くのか」が分からなくなっていたので、**Tools > Smart Media Platform > セットアップ** に手順を上から順に並べました。**URL は `Catalog` の Inspector に入れます** — `UdonMediaCatalog` は Udon の制約で 11 本の並列配列としてデータを持つため素の Inspector では編集できないので、**1 行 = 1 本**の一覧を出す専用 Inspector を用意しました。書き戻しは `UdonCatalogBaker` に任せるので、タグや関連の CSR オフセットは自動で組み直されます。

**あわせて `.meta` を全スクリプトに同梱しました。** U# のプログラム(`.asset`)は元の `.cs` を GUID で指しており、`.meta` ごと入れ替えると行き先を失って `null` になります。U# は壊れたプログラムが 1 つでもあるとコンパイル全体を止めるため、これが「何も動かない」の原因になっていました。GUID をリポジトリ側で固定したので、zip を展開し直しても変わりません(既に壊れている場合は **セットアップ > 壊れた U# プログラムを修復する**)。

- コード: `Assets/SmartMediaPlatform/World/Udon/`(9 クラス)、`World/UdonModel/`(検証用の正典)、`World/Editor/`(Prefab Builder / セットアップ窓)、`Catalog/Udon/Editor/UdonMediaCatalogEditor.cs`(URL 入力)
- Prefab(実機): **Tools > Smart Media Platform > セットアップ** → 「SmartMediaPlayer.prefab を作る」 → **Hierarchy へドラッグ** → **VRChat SDK > Build & Test**
- 設計ドキュメント(Udon 化したクラス / 変更したアーキテクチャ / Prefab の変更点 / 実機の確認項目 / 残る課題): [docs/Phase5-2_UdonPort.md](docs/Phase5-2_UdonPort.md)

## テスト

EditMode テスト計 **932 ケース**(Catalog 95 / Recommendation 13 / Queue 72 / Backend 44 / Integration 9 / Audio 54 / Player 35 / Playlists 70 / Session 58 / Adapter 35 / Video 143 / AutoPlay 94 / Library 155 / World 18 / World.UdonModel 37)。
Unity の **Window > General > Test Runner > EditMode > Run All** で実行(VRChat SDK 不要)。

### Unity を起動しない静的チェック

```bash
apt-get install -y mono-mcs                # 初回のみ

python3 tools/typecheck/typecheck.py       # 全アセンブリを実際にコンパイルして型検査
python3 tools/typecheck/runtests.py        # EditMode テストを mono で実行(841 ケース)
python3 tools/typecheck/udon_lint.py       # UdonSharp で書けない書き方を検出
```

`typecheck.py` は Unity のコンパイル方式(asmdef ごとに 1 アセンブリ / 参照は宣言したものだけ / `defineConstraints` / `Assembly-CSharp-Editor`)をなぞって **mcs で本当にコンパイル**します。`runtests.py` はさらに一歩進めて、NUnit の代役に本物の判定を入れて **EditMode テストを実際に走らせます**(`AudioSource` など UnityEngine の実体が要る 6 つのテストクラスだけは Unity の Test Runner に任せます)。`udon_lint.py` は UdonSharp が受け付けない構文(`List` / `?.` / `throw` / 文字列補間など)を `UdonSharpBehaviour` を継承したファイルだけに対して見ます。詳細は [tools/typecheck/README.md](tools/typecheck/README.md)。
