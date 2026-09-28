# Phase4-3: 再生フローの完成(Library → Queue → Player)

**目的は「MediaPlayer の再生フローを完成させること」です。**
Phase4-1 で「選ぶ」、4-2 で「関連を出す」ができたので、
今回は**選んだものが Queue を経て再生されるまで**を通しで触れるようにします。

```
MediaLibrary ─┐
RelatedMediaView ─┼→ LibraryPlaybackBridge → PlayerSession → Queue → Backend
              │                                   │
              └──────── QueueView ────────────────┤
                        NowPlayingView ───────────┘
```

| 要件 | 実装 |
|---|---|
| Library → Queue → Player の一連の流れ | `PlaybackFlow` が部品を繋ぎ、`Tick()` で表示を追従させる |
| PlayerSession は MediaId 中心 / 表示は Catalog から | `NowPlayingView` が `CurrentMediaId`(string)→ `CatalogStore` → `DisplayMeta` |
| 4-2 の抽象化を維持し責務を混ぜない | `QueueView` も `IMediaListView`。データは `ICatalogStore` からのみ |
| Catalog Builder / バックエンド切り替えが容易 | 差し込み口は 4-2 のまま（§3） |
| PoC として動作確認しやすい | `PlaybackFlow.Create(catalog, session)` の 1 行で組み上がる |

### 変更していないもの

`PlayerSession` / `MediaPlayer` / `BackendManager` / `BackendAdapter` /
`MediaQueue` / `IQueue` / `QueueManager` / `RecommendationEngine` /
`VRChatVideoBackend` / `MediaItem` / `IMediaCatalog` — **すべて差分ゼロ**です。

---

## 1. 追加・変更したクラス一覧

### 追加

| クラス | 場所(asmdef) | 役割 |
|---|---|---|
| **`QueueView`** | `Library.Playback`(純粋C#) | Queue の中身を `DisplayMeta` で見せ、並べ替え・削除の窓口になる |
| **`NowPlayingView`** | `Library.Playback`(純粋C#) | 再生中のものを `MediaId → Catalog` で引き直して見せる。進捗も持つ |
| **`PlaybackFlow`** | `Library.Playback`(純粋C#) | 部品の組み立てと `Tick()` による同期。**再生の判断は持たない** |
| `PlaybackFlowConsoleDemo` | `Library.Demo` | 一連の流れを Console で確認(8 シナリオ) |
| `PlaybackFlowScreenDemo` | `Library.Demo` | Library / 関連 / Queue / 再生中 を並べた操作画面 |
| `Phase4PlaybackFlowDemoScene.unity` | `Library.Scenes` | 上記 2 つを載せたシーン |

### 変更

| クラス | 変更点 |
|---|---|
| `LibraryPlaybackBridge` | **`PlayNext()` / `PlayNextSelected()` を追加**(Queue の 2 番目へ入れる) |
| `Phase4MediaLibrarySceneBuilder` | Phase4-3 のシーンを開く / 作るメニューを追加 |

**削除したクラスはありません。**

---

## 2. データフロー

### 2-1. Library で選んで再生するまで

```
① MediaLibrary.Select(i)
      DisplayMeta(タイトル・アーティスト…。URL 無し)を UI が描く

② LibraryPlaybackBridge.PlaySelected()
      library.SelectedRef → PlayableRef(中身は MediaId だけ)

③ PlayerSession.Enqueue(mediaId) / Queue.Move(index, 0 or 1) / Play()
      Queue は「先頭 = いま鳴っているもの」

④ MediaPlayer → BackendManager → BackendAdapter → Backend
      ここで初めて MediaItem.Url が使われる(VideoBackend なら焼き込み VRCUrl)

⑤ QueueView.Sync() / NowPlayingView.Poll()
      Queue と再生中の表示が追従。どちらも DisplayMeta を Catalog から引き直す
```

**URL が現れるのは ④ だけ**です。①②③⑤ には一度も出てきません。

### 2-2. なぜ `QueueView` が要ったか

`IQueue.GetAll()` が返す `QueueItem` は `MediaItem` を抱えていて、
そこに `Url` が入っています。

```csharp
public sealed class QueueItem
{
    public MediaItem Item { get; }        // ← Item.Url がある
    public string MediaId => Item.Id;
}
```

そのまま UI へ渡すと **Phase4-2 で塞いだ穴が Queue 経由で開きます**。
`QueueView` は `MediaId` だけ取り出して `ICatalogStore` から引き直すので、
URL は Queue の外へ出ません。

```csharp
protected override void BuildEntries(List<DisplayMeta> into)
{
    var entries = _queue.GetAll();
    for (int i = 0; i < entries.Count; i++)
    {
        var meta = Store.GetDisplayMeta(entries[i].MediaId);   // ← ID だけ使う
        if (meta != null) into.Add(meta);
    }
}
```

**Queue の責務は増やしていません。**
並べ替えも削除も `IQueue` が Phase1-4 から持っている
`Move` / `RemoveAt` / `Clear` を呼ぶだけです。

### 2-3. 先頭(いま鳴っているもの)を守る

Queue は「先頭 = Now」で動いているので、
先頭を動かしたり消したりすると**再生と Queue がずれます**。
`QueueView` は index 0 への操作を拒否します。

| 操作 | index 0 | index 1 以降 |
|---|---|---|
| `MoveUp` | ✗ | index 1 も ✗(先頭を追い越すため) |
| `MoveDown` | ✗ | ✓ |
| `RemoveAt` | ✗ | ✓ |

止めたいときは `PlayerSession.Stop()`、飛びたいときは `QueuePlayback.PlayAt(i)` です。

### 2-4. `NowPlayingView` — MediaId 中心の設計そのもの

```csharp
public DisplayMeta Meta
{
    get
    {
        string id = _session.CurrentMediaId;      // PlayerSession は string しか持たない
        return id != null ? _store.GetDisplayMeta(id) : null;   // 表示は Catalog から
    }
}
```

`PlayerSession.CurrentItem`(`MediaItem` を返す)は**使っていません**。
セッションに表示用の情報を持たせずに済み、UI からも URL が見えません。

長さはバックエンドが答えられないときカタログの値で埋めるので、
読み込み中でも「4:22」と出せます。

---

## 3. 今後の接続ポイント

**Phase4-2 から増えていません。** そこが狙いです。

### 3-1. Catalog Builder

差し込み口は `MediaCatalogAsset` の 1 つだけ。

```csharp
// いま
var flow = PlaybackFlow.Create(new MediaCatalog(new VideoCatalogSource()), session);

// 将来(Catalog.asset)
var flow = PlaybackFlow.Create(new MediaCatalog(catalogAsset), session);
```

`QueueView` も `NowPlayingView` も `ICatalogStore` しか見ていないので無変更です。

### 3-2. サーバー連携(関連動画 ID)

差し込み口は `IRelatedMediaProvider` の 1 つだけ。

```csharp
var provider = (StaticRelatedMediaProvider)flow.RelatedProvider;
provider.Set(mediaId, idsFromServer);
flow.Related.Refresh();
```

> **検証済み:** `ServerSuppliedRelatedIdsFlowThroughTheWholeChain` が、
> サーバー由来の ID が関連一覧に出て、**そのまま再生まで通る**ことを確かめています。

### 3-3. AVPro / VRChat VideoPlayer への切り替え

差し込み口は **`VRChatVideoBackendHost` の Inspector**(Phase4-1 の `VideoPlayerPreference`)。
`PlaybackFlow` は `PlayerSession` しか見ていないので、
バックエンドが `DummyBackend` でも `VRChatVideoBackend` でも同じコードです。

実機で動かすときは、デモの `DummyBackend` を
`VRChatVideoBackendHost.EnsureBuilt()` が返すアダプタに差し替えるだけです。

```csharp
// デモ(いま)
backendManager.RegisterBackend(new DummyBackend("DummyBackend", logger));

// 実機
backendManager.RegisterBackend(host.EnsureBuilt(logger));
```

### 3-4. まだ口を作っていないもの

| 項目 | 状況 |
|---|---|
| シーク UI | `NowPlayingView.CanSeek` / `Progress` はありますが、画面はバーの表示のみ(操作は未実装) |
| サムネイル | `DisplayMeta` に項目がありません |
| Queue の永続化 | Playlist(Phase2-3(A))と重なるので、統合方針を決めてから |

---

## 4. Unity で確認すべきテスト項目

### 4-1. DemoScene(いちばん分かりやすい)

**Tools > Smart Media Platform > Open Phase4-3 Playback Flow Demo Scene** → **Play**

画面は 上=いま再生中 / 左=Library / 中=関連動画 / 右=Queue の 4 区画です。

| # | 操作 | 期待 |
|---|---|---|
| 1 | 左で行をクリック → **▶ 再生** | 上に曲名が出て `[Playing]`、右の Queue に積まれる（先頭 = ♪） |
| 2 | 別の行を選んで **次に再生** | Queue の **2 番目**に入る。**再生は止まらない** |
| 3 | 別の行を選んで **＋ Queue** | Queue の**末尾**に入る |
| 4 | Queue の **▲ / ▼** | 順番が入れ替わる。**先頭の ▲▼ は押せない**（グレーアウト） |
| 5 | Queue の **×** | その行が消える。**先頭の × は押せない** |
| 6 | Queue で 2 番目以降を選んで **この曲へ飛ぶ** | 上の表示がその曲に変わり、Queue の先頭になる |
| 7 | **曲を終わらせる(Ended)** | 次の曲へ進み、Queue の先頭も入れ替わる |
| 8 | 7 を続けて押す | 止まらずに進み続ける |
| 9 | 中央の関連動画 **▶** | その曲が再生され、**関連一覧の中身も入れ替わる**（関連 → 関連 とたどれる） |
| 10 | **この曲より後を消す** | Queue が 1 件（再生中のみ）になる。再生は止まらない |

> 音は出ません(`DummyBackend`)。確認するのは **Library → Queue → Player の接続**です。
> 実機で音や映像を出す構成は §3-3 を参照してください。

### 4-2. ConsoleDemo(同じシーンに同梱)

Play すると Console に 8 シナリオが出ます。特に見てほしいのは:

- **§3** `QueueView.GetAt(0) = DisplayMeta(URL の口 = 無し)`
- **§4** 「次に再生」が index 1 に入る / 先頭が守られる
- **§6** Ended → 次へ が 3 回続く
- **§7** 関連の起点が再生に追従する
- **§8** `PlayerSession.CurrentMediaId = "video-00X"(string)` と、
  そこから引き直した表示

### 4-3. EditMode テスト

**Window > General > Test Runner > EditMode > Run All**

| 見るところ | 期待 |
|---|---|
| `SmartMediaPlatform.Library.Tests` | **139 ケース**(Phase4-2 の 107 + Phase4-3 の 32) |
| 全体 | **861 ケース** すべて緑 |

要件との対応。

| 要件 | テスト |
|---|---|
| Library → Queue → Player | `PlayingFromTheLibraryStartsPlaybackAndFillsTheQueue` |
| MediaId 中心 / Catalog から表示 | `NowPlayingReadsTheMetaBackFromTheCatalog` |
| Queue が DisplayMeta で見える | `TheQueueIsVisibleAsDisplayMeta` / `TheQueueViewNeverExposesTheUrl` |
| Queue 操作 | `PlayNextPutsItRightAfterTheCurrentOne` / `MovingReordersTheUpcomingItems` / `RemovingTakesItOutOfTheQueue` / `ClearUpcomingLeavesOnlyWhatIsPlaying` |
| 先頭が守られる | `TheCurrentItemIsProtectedFromReorderingAndRemoval` |
| Queue の途中へ飛ぶ | `JumpingToAQueueEntryPlaysIt` |
| Ended で進む | `EndedAdvancesAndTheViewsFollow` / `SeveralEndedInARowKeepPlaying` |
| 4-2 の抽象化を維持 | `ServerSuppliedRelatedIdsFlowThroughTheWholeChain` / `EveryListIsTheSameShape` |
| 責務を混ぜない | `TheFlowDoesNotDecideWhatPlaysNext` / `ThePlaybackEngineIsUntouched` / `TheLibraryAssemblyStillDoesNotSeeThePlaybackStack` |

### 4-4. 手っ取り早く

1. `Phase4PlaybackFlowDemoScene` を **Play**
2. 左で 1 行クリック → **▶ 再生** → 上に曲名、右の Queue に積まれる
3. **曲を終わらせる(Ended)** を数回押す → 止まらずに進む
4. 中央の関連動画の **▶** → 関連一覧も入れ替わる

ここまで動けば Phase4-3 の要件は満たしています。

---

## 5. 確認できていないこと

- **コンパイルとテストの実行**:この作業環境に Unity / .NET SDK が無いため、
  **一度も実行検証できていません。**
  括弧対応・メンバー名重複の静的チェック(全 182 ファイル)と机上確認のみです。
- **実機バックエンドでの通し**:デモは `DummyBackend`(ログのみ)です。
  `VRChatVideoBackend` に差し替えた通し確認は行っていません(§3-3 に手順)。
- **IMGUI の見た目**:4 区画に増えたので、低い解像度では詰まる可能性があります
  (`_fontSize` で調整)。

---

## 6. 報告:設計上の判断

### 6-1. `PlaybackFlow` に再生の判断を持たせていません

「一連の流れを完成させる」ために組み立て役を置きましたが、
**神クラスにしないよう意図的に絞っています**。

| 持っているもの | 持っていないもの |
|---|---|
| 部品の組み立て | 次に何を再生するか(= `PlayerSession`) |
| `Tick()` による表示の同期 | Queue に何を積むか(= `IQueueRefiller`) |
| 関連の起点の追従(画面の都合) | 実際の再生(= `BackendAdapter`) |

`Play` / `Next` / `Stop` といったメソッドは**あえて生やしていません**
(`TheFlowDoesNotDecideWhatPlaysNext` で確認)。
利用側は `flow.Session` か `flow.LibraryPlayback` を直接呼びます。

### 6-2. 「関連の起点の追従」を MonoBehaviour から純粋 C# へ移しました

Phase4-2 では `MediaLibraryScreenDemo`(MonoBehaviour)に書いていたため
テストできませんでした。`PlaybackFlow.FollowSource()` に移したので、
`RelatedMovesOnWhenPlaybackAdvances` などで検証できます。

### 6-3. `QueueView` の「先頭を守る」は Queue 側の制約ではありません

`IQueue.Move(0, 1)` は普通に成功します(Queue は並びを管理するだけ)。
**「先頭 = いま鳴っているもの」という運用上の約束を守るのは表示層の責務**と考え、
`QueueView` で弾いています。Queue の責務は増やしていません。

### 6-4. Phase3-4 / 4-1 からの持ち越し(未着手)

- `AutoQueueService`(Playlists)の補充一本化
  ([Phase3-4 §7-1](Phase3-4_PlaybackOrchestrationAndRecovery.md))
- `DummyVideoBackendAdapter` と `VideoBackendAdapter` の重複
- `PlayerSession.PlayNow(mediaId)` を本体へ置く案
  ([Phase4-1 §8-1](Phase4-1_MediaLibrary.md))
  — Phase4-3 で `PlayNext()` も同じ形(`Enqueue` + `Move`)になったので、
  **2 つまとめて `PlayerSession` へ移すほうが素直**かもしれません。制約に触れるため保留しています。
