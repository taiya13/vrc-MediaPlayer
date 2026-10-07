# Phase4-4: 再生フローを実際の VRChat VideoPlayer へ接続する

**目的は「現在の Playback フローを実際の VRChat VideoPlayer へ接続すること」です。**
Phase4-3 までのデモは `DummyBackend`(ログのみ)でした。
今回それを**実機の動画バックエンドへ差し替え**、
あわせて**実機の標準を AVPro** にします。

```
MediaLibrary → Queue → PlayerSession → VideoBackendAdapter
    → VRChatVideoBackend → VRCVideoPlayerBridge → VRCAVProVideoPlayer
```

| 要件 | 実装 |
|---|---|
| 現在のアーキテクチャを維持 | 差し替えは **`RegisterBackend` の引数 1 行**だけ |
| DummyBackend を実機へ置き換えられる構造 | `VRChatPlaybackFlowSceneDemo` が実物。上位は無変更 |
| AVPro との切り替えが容易 | `VideoPlayerPreference`(Inspector)+ `VRChatVideoPlayerFactory`(生成側) |
| Library → … → VRChat VideoPlayer を実際に動かす | `Phase4VRChatPlaybackScene`(SDK 必須・メニューで生成) |
| PoC として Unity 上で確認できる | **SDK 無しでも通しを確認できる**シーンとテストを同梱 |
| 再生バックエンドの既定を AVPro に | シーン生成が **AVPro を作る**ように変更(4 つのビルダー全部) |

### 変更していないもの

`PlayerSession` / `MediaPlayer` / `BackendManager` / `BackendAdapter` /
`MediaQueue` / `IQueue` / `RecommendationEngine` / `VRChatVideoBackend` /
`VRCVideoPlayerBridge` / `PlaybackFlow` / `MediaLibrary` / `QueueView` /
`NowPlayingView` — **すべて差分ゼロ**です。

---

## 1. 追加・変更したクラス

### 追加

| クラス | 場所(asmdef) | 役割 |
|---|---|---|
| **`VRChatVideoPlayerFactory`** | `Video/Editor`(SDK 必須) | シーンに動画プレイヤーを置く共通の組み立て役。**AVPro / Unity 版の配線の違いを吸収** |
| **`VRChatPlaybackFlowSceneDemo`** | `Library.VRChat`(新 asmdef・SDK 必須) | **本命**。`PlaybackFlow` を実機バックエンドの上で動かす画面 |
| `VideoBackendFlowConsoleDemo` | `Library.Demo` | **SDK 無しで**通しを確認する Console デモ(7 シナリオ) |
| `Phase4VRChatPlaybackSceneBuilder` | `Library/Editor`(SDK 必須) | 実機シーンを作るメニュー(AVPro 版 / Unity 版) |
| `Phase4VideoBackendFlowDemoScene.unity` | `Library/Scenes` | 上記 Console デモのシーン |
| `SmartMediaPlatform.Library.VRChat.asmdef` | — | SDK 依存を閉じ込める(`defineConstraints: VRC_SDK_VRCSDK3`) |

### 変更

| クラス | 変更点 |
|---|---|
| `Phase3VRChatVideoSceneBuilder` | 動画プレイヤーの生成を Factory 経由に。**AVPro が既定** |
| `Phase3VRChatVideoEventSceneBuilder` | 同上 |
| `Phase3RecommendationPlaybackSceneBuilder` | 同上 |
| `Phase3AutoPlayRecoverySceneBuilder` | 同上 |
| `Phase4MediaLibrarySceneBuilder` | Phase4-4 の Console シーンのメニューを追加 |

**削除したクラスはありません。**

---

## 2. 実際の再生フロー

### 2-1. 差し替えは 1 行

```csharp
// Phase4-3(ログだけ)
backendManager.RegisterBackend(new DummyBackend("DummyBackend", logger));

// Phase4-4(実際に動画が出る)
backendManager.RegisterBackend(host.EnsureBuilt(logger));
```

`host.EnsureBuilt()` が返すのは `VideoBackendAdapter`(= `IMediaBackend`)なので、
**`DummyBackend` と同じ口に入ります**。
`PlaybackFlow` も `PlayerSession` も `Queue` も `MediaLibrary` も、この違いを知りません。

> **検証済み:** `ThePlaybackFlowDoesNotKnowWhichBackendIsAttached` が、
> `PlaybackFlow` に `VRChatVideoBackend` を露出するメンバーが無いことを確かめています。

### 2-2. ID が URL になるまで

```
① MediaLibrary.Select(i)            DisplayMeta(URL 無し)
② LibraryPlaybackBridge             PlayableRef(MediaId だけ)
③ PlayerSession → Queue             MediaId
④ BackendManager.LoadCurrent()      MediaItem を選び、扱えるバックエンドへ
⑤ VideoBackendAdapter.Load()        MediaItem.Url(string)を取り出す
⑥ VRChatVideoBackend.Load(url)      焼き込み表にあるか確認(無ければ InvalidUrl で拒否)
⑦ VRCVideoPlayerBridge.LoadURL()    VRCUrlTable から VRCUrl を引く
⑧ VRCAVProVideoPlayer.LoadURL(VRCUrl)   ← ここで初めて実機の API
```

**⑦ が「実行時に VRCUrl を作らない」約束の実装点**です。
`VRCUrl` は編集時に `VRCUrlTable.Bake()` で焼き込んだものしか使いません。

### 2-3. イベントが戻ってくる経路(Phase3-2 のまま)

```
VRCAVProVideoPlayer が OnVideoReady / OnVideoEnd / OnVideoError を発火
  → 同じ GameObject の UdonVRCVideoEventRelay(UdonSharp)が int で記録
  → UdonVideoEventPump が差分を読み出す
  → VideoEventBridge(重複排除 + Tick 調停)
  → VRChatVideoBackend → VideoBackendAdapter → MediaPlayer → PlayerSession
  → 次の曲へ
  → PlaybackFlow.Tick() で Queue と「いま再生中」の表示が追従
```

画面側がやっているのは **`_flow.Tick()` の 1 行だけ**です。

---

## 3. VRChat VideoPlayer との接続ポイント

### 3-1. 接続点は 3 つだけ

| # | 場所 | 何をする |
|---|---|---|
| 1 | `VRChatVideoBackendHost`(シーン) | 動画プレイヤーを包んで `VideoBackendAdapter` を作る |
| 2 | `BackendManager.RegisterBackend(adapter)` | それを再生系に登録する(`DummyBackend` と同じ口) |
| 3 | `VRCUrlTable.Bake(catalog)`(編集時) | カタログの URL を `VRCUrl` へ焼き込む |

3 が抜けていると `CanPlay` が false になり、**再生を試みる前に断られます**。
`VRChatPlaybackFlowSceneDemo` は起動時に焼き込み件数を Console へ出すので、
0 件ならそこで気づけます。

### 3-2. AVPro と Unity 版の切り替え

**実行時**は `VRChatVideoBackendHost` の Inspector 1 箇所です。

| `Preferred Player` | 動き |
|---|---|
| `AVPro`(**既定**) | 同じ GameObject の AVPro を使う。無ければ Unity 版へ降りる |
| `Unity` | `VRCUnityVideoPlayer` を使う |
| `Explicit` | Inspector で指したものだけを使う |

**シーンを作るとき**は `VRChatVideoPlayerFactory` が担当します。
AVPro と Unity 版では**配線の形が違う**ので、そこを吸収するのがこのクラスの主な仕事です。

| | 映像の出力先 | 音の出力先 |
|---|---|---|
| **Unity 版** | プレイヤーの `TargetMaterialRenderer` | プレイヤーの `TargetAudioSources` |
| **AVPro** | Renderer 側に `VRCAVProVideoScreen` を付けてプレイヤーを指す | AudioSource 側に `VRCAVProVideoSpeaker` |

SDK の型は**名前で探します**(`VRCVideoPlayerBridge.DetectKind` と同じ方針)。
名前空間やクラス名が SDK 更新で変わっても**コンパイルは壊れず**、
配線できなかったものは Console に書き出すので Inspector で手当てできます。

> ⚠️ **AVPro は Unity エディタ(ClientSim)では映像を出しません。**
> エディタで絵を確認したいときは、
> **Unity 版のシーンを作るメニュー**を使うか、
> Host の `Preferred Player` を `Unity` にしてください。

### 3-3. まだ手を付けていない接続点

| 項目 | 状況 |
|---|---|
| `RenderTexture` / Material | Quad は作りますが、**マテリアルは既定のまま**です。実際に映像を映すには RenderTexture を作って割り当ててください |
| 実在する動画 URL | `VideoCatalogSource` は架空のアドレスです(§5) |
| ネットワーク同期 | 未実装(Phase5 の想定) |

---

## 4. Unity で確認すべきテスト項目

### 4-1. SDK 無しで通しを確認する(まずこちら)

**Tools > Smart Media Platform > Open Phase4-4 Video Backend Flow Demo Scene (SDK 不要)** → **Play**

Console に 7 シナリオが出ます。実機の動画プレイヤーの位置に
`SimulatedVRCVideoPlayer` が入るだけで、**経路は実機とまったく同じ**です。

| 見るところ | 期待 |
|---|---|
| §1 | `いま載っているもの = VideoAdapter`(DummyBackend ではない) |
| §2 | `動画プレイヤーが受け取った URL = https://example.com/media/...`(Load まで届いている) |
| §3 | `Play 直後 -> Loading` → `OnVideoReady 後 -> Playing`(非同期読み込みを待っている) |
| §4 | `OnVideoEnd(video-001) -> video-002` が 3 回続く。URL も切り替わる |
| §5 | `OnVideoError` のあとも Queue の件数が変わらない |
| §6 | `CanPlay(焼き込み無し) = False` |
| §7 | `VRCUnityVideoPlayer -> False` / `VRCAVProVideoPlayer -> True`(生配信) |

### 4-2. EditMode テスト

**Window > General > Test Runner > EditMode > Run All**

| 見るところ | 期待 |
|---|---|
| `SmartMediaPlatform.Library.Tests` | **155 ケース**(Phase4-3 の 139 + Phase4-4 の 16) |
| 全体 | **877 ケース** すべて緑 |

要件との対応。

| 要件 | テスト |
|---|---|
| DummyBackend と同じ口で載る | `TheVideoBackendIsWhatTheSessionTalksTo` |
| 上位が実機を知らない | `ThePlaybackFlowDoesNotKnowWhichBackendIsAttached` |
| Library → … → 動画プレイヤー | `PlayingFromTheLibraryReachesTheVideoPlayer` |
| URL が上位に出ない | `TheUrlNeverAppearsAboveTheVideoBackend` |
| 非同期読み込みを待つ | `PlaybackWaitsForTheLoadToComplete` |
| Ended で次の URL を読む | `VideoEndAdvancesToTheNextAndLoadsItsUrl` / `SeveralVideosPlayInARow` |
| 失敗で壊れない | `AVideoErrorIsVisibleWithoutBreakingTheViews` / `PlaybackCanContinueAfterAnError` |
| 焼き込み済みのみ再生 | `OnlyBakedUrlsCanPlay` / `APartiallyBakedCatalogStillPlaysWhatItCan` |
| AVPro / Unity 切り替え | `OnlyTheVideoBackendCaresWhichPlayerIsAttached` / `SwappingThePlayerKindDoesNotChangeTheFlow` |
| 上位が変わっていない | `TheSessionBehavesTheSameAsWithTheDummyBackend` / `RelatedStillFollowsPlaybackWithTheRealBackend` |

### 4-3. 実機(VRChat SDK)で確認する

**Tools > Smart Media Platform > Create Phase4-4 VRChat Playback Scene (実際に再生)**

生成されたシーンで **Play**(ClientSim)、または **VRChat SDK > Build & Test**。

| # | 確認 | 期待 |
|---|---|---|
| 1 | Console の起動メッセージ | `種類 : AVPro`、`ベイク済み URL : 10 件` |
| 2 | 画面左の Library | カタログの動画が並ぶ |
| 3 | **▶ 再生** | 「いま再生中」が変わり、Queue に積まれる |
| 4 | **終端へ飛ぶ** | しばらくして `OnVideoEnd` が届き、次の曲へ進む |
| 5 | Queue の **飛ぶ** / **×** | 途中へ飛べる / 消せる |
| 6 | Console の `Udon 中継` | `UdonVRCVideoEventRelay` に繋がっている旨が出る |

> **エディタで映像を見たい場合**は
> **Create Phase4-4 VRChat Playback Scene (Unity 版・エディタで確認)** を使ってください。
> AVPro は ClientSim では描画されません。

### 4-4. 手っ取り早く

1. `Phase4VideoBackendFlowDemoScene` を **Play**
2. Console の **§4** に `OnVideoEnd(video-001) -> video-002` が出ていれば、
   **Library → Queue → Player → VideoBackend が通っています**
3. 実機は §4-3

---

## 5. 確認できていないこと

- **コンパイルとテストの実行**:この作業環境に Unity / .NET SDK / VRChat SDK が無いため、
  **一度も実行検証できていません。**
  括弧対応・メンバー名重複の静的チェック(全 187 ファイル)と机上確認のみです。
- **AVPro の実際の配線**:`VRCAVProVideoScreen` / `VRCAVProVideoSpeaker` の
  **クラス名とフィールド名を実物で確認できていません**。
  名前で探して見つからなければ Console に「Inspector で設定してください」と出すようにしてあるので、
  <b>壊れはしませんが、手作業が残る可能性があります</b>。
  実行して Console の `映像の出力先 :` の行を確認してください。
- **実機での映像**:`VideoCatalogSource` の URL は架空のアドレスなので、
  **そのままでは映像は出ません**(`OnVideoError` の経路は動きます)。
  実在する URL に差し替えてから焼き直してください。
- **RenderTexture / Material**:Quad は作りますがマテリアルは既定のままです。

---

## 6. 報告:設計上の判断

### 6-1. SDK 依存を新しい asmdef に閉じ込めました

`SmartMediaPlatform.Library.VRChat`(`defineConstraints: VRC_SDK_VRCSDK3`)を追加しました。
`Video.VRChat` / `AutoPlay.VRChat` と同じ形です。
おかげで **SDK が無い環境でも既存のすべてがそのままコンパイル・テストできます**。

### 6-2. 「SDK 無しで通しを確認できる」ことを重視しました

実機バックエンドの検証を SDK 必須にすると、
**この作業環境でも、SDK を入れていない人の環境でも検証できなくなります**。
`SimulatedVRCVideoPlayer`(Phase3-1 から在るテスト用の実装)を使えば、
`IVRCVideoPlayer` から上は実機とまったく同じ経路を通せるので、
`VideoBackendFlowTests` / `VideoBackendFlowConsoleDemo` はそちらで書きました。

**実機でしか確かめられないのは `IVRCVideoPlayer` の実装(= SDK の API 呼び出し)だけ**に絞ってあります。

### 6-3. AVPro の配線は「名前で探して、できなければ報告」にしました

`VRCAVProVideoScreen` / `VRCAVProVideoSpeaker` の正確なクラス名・フィールド名を
この環境で確認できませんでした。直接参照するとコンパイルが壊れるリスクがあるため、
`VRCVideoPlayerBridge.DetectKind` と同じ**名前による探索**にしています。

見つからなかった場合は Console に何ができなかったかを出すので、
Inspector で手当てできます(黙って失敗しません)。

### 6-4. Phase3-4 / 4-1 / 4-3 からの持ち越し(未着手)

- `AutoQueueService`(Playlists)の補充一本化
- `DummyVideoBackendAdapter` と `VideoBackendAdapter` の重複
- `PlayerSession.PlayNow(mediaId)` / `PlayNext(mediaId)` を本体へ置く案
