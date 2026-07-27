# Phase4-1: Media Library 設計・実装ドキュメント

**目的は「Catalog の内容をユーザーが閲覧できるようにすること」です。**
Phase1〜3 で再生側は完成しているので、今回は**見る・選ぶ**だけを足します。

```
Catalog → MediaLibrary(閲覧・選択) → LibraryPlaybackBridge → PlayerSession → …(既存)
```

| 要件 | 実装 |
|---|---|
| Catalog 内のメディア一覧を表示できる | `MediaLibrary.Entries` / `MediaLibraryFormatter.FormatLibrary()` |
| Music / Video を表示できる | `MediaLibrary.ShowOnly(MediaType.Music)` / `ShowOnly(MediaType.Video)` |
| タイトル・アーティスト・ジャンル・タグを表示できる | `MediaLibraryFormatter.FormatEntry()` / `FormatSelection()` |
| MediaItem を選択できる | `Select(index)` / `SelectById(id)` / `SelectNext()` / `SelectPrevious()` |
| 選択した MediaItem を PlayerSession へ渡せる | `LibraryPlaybackBridge.PlaySelected()` / `EnqueueSelected()` |

### 変更していないもの(制約どおり)

| 層 | 状態 |
|---|---|
| `PlayerSession` | **差分ゼロ** |
| `BackendAdapter`(`VideoBackendAdapter` / `MediaBackendAdapter`) | **差分ゼロ** |
| `RecommendationEngine` | **差分ゼロ** |
| `MediaQueue` / `QueueManager` | **差分ゼロ** |
| `VRChatVideoBackend` / `VideoEventBridge` | **差分ゼロ** |

`VRChatVideoBackendHost`(シーンの配線役)にだけ、
再生バックエンドを Inspector から選べる仕組みを足しました(§4)。
これは「組み立てる」役目の中の話で、**Backend の責務は増えていません**。

検索・Playlist・お気に入り・履歴・Recommendation UI・外部API・同期
のいずれにも触れていません。

---

## 1. 「閲覧と選択だけ」をどう守ったか

制約は「Media Library は閲覧と選択だけを担当してください」でした。
これを**コメントではなく参照関係で守っています。**

```
SmartMediaPlatform.Library          references: Catalog のみ  (noEngineReferences: true)
SmartMediaPlatform.Library.Playback references: Library + Session + Queue + Backend + Player
```

`MediaLibrary` からは **`PlayerSession` も `Queue` も `Backend` もそもそも見えません**。
書こうと思っても書けない、という状態です。
両方が見える場所は `LibraryPlaybackBridge` **1 クラスだけ**に閉じてあります。

テストでも直接確かめています。

```csharp
[Test]
public void LibraryAssembly_DoesNotReferenceThePlaybackStack()
{
    var referenced = typeof(MediaLibrary).Assembly
        .GetReferencedAssemblies().Select(x => x.Name).ToArray();

    CollectionAssert.DoesNotContain(referenced, "SmartMediaPlatform.Session");
    CollectionAssert.DoesNotContain(referenced, "SmartMediaPlatform.Queue");
    CollectionAssert.DoesNotContain(referenced, "SmartMediaPlatform.Backend");
    CollectionAssert.Contains(referenced, "SmartMediaPlatform.Catalog");
}
```

### 境界を越えるのは MediaId(string)だけ

Phase3 から通している「**URL を知るのは VideoBackend だけ**」をそのまま守ります。

- `LibraryPlaybackBridge` が `PlayerSession` へ渡すのは `SelectedMediaId`(string)だけ
- `MediaItem` も URL も渡しません
- `MediaLibraryFormatter` に **URL を出力する口がありません**(表示側でも守る)

```csharp
[Test]
public void Formatter_NeverPrintsTheUrl()
{
    string url = _library.SelectedItem.Url;
    StringAssert.DoesNotContain(url, MediaLibraryFormatter.FormatLibrary(_library));
    StringAssert.DoesNotContain(url, MediaLibraryFormatter.FormatSelection(_library));
}
```

---

## 2. クラスの中身

### `MediaLibrary`(純粋 C#)

| 分類 | メンバー |
|---|---|
| 閲覧 | `Entries` / `Count` / `GetAt(i)` / `IndexOf(id)` / `VisibleTypes` / `SortOrder` |
| 絞り込み | `ShowAll()` / `ShowOnly(params MediaType[])` / `IsVisible(type)` |
| 選択 | `SelectedIndex` / `SelectedItem` / **`SelectedMediaId`** / `HasSelection` |
| 選択操作 | `Select(i)` / `SelectById(id)` / `SelectNext()` / `SelectPrevious()` / `ClearSelection()` |
| 読み直し | `Refresh()` |
| 通知 | `AddObserver` / `RemoveObserver`(`IMediaLibraryObserver`) |

**一覧は毎回作り直しません。**
絞り込み・並び順・`Refresh()` のときだけ組み直して覚えておきます
(UI が毎フレーム `Entries` を読んでも重くならないように)。

**選択は位置ではなく ID で覚え直します。**
並び順を変えても、絞り込みを変えても、**選んでいたものが一覧に残っていれば選ばれたまま**です。
消えたときだけ選択が外れます。

```csharp
_library.SelectById("video-005");
_library.SortOrder = LibrarySortOrder.Title;
Assert.AreEqual("video-005", _library.SelectedMediaId);   // 保たれる

_library.ShowAll();
_library.SelectById("music-001");
_library.ShowOnly(MediaType.Video);
Assert.IsFalse(_library.HasSelection);                    // 見えなくなったので外れる
```

### `IMediaLibraryObserver`

C# の `event` ではなく インターフェース です。
Phase1-5 の `IBackendObserver` から通している方針で、
**将来 UdonSharp へ移すときに書き換えずに済ませる**ためです。

```csharp
void OnLibraryChanged(IMediaLibrary library);                          // 一覧が変わった
void OnSelectionChanged(IMediaLibrary library, MediaItem selected);    // 選択が変わった
```

**何も変わらなければ通知しません**(同じものを選び直す・同じ並び順を入れ直す、は無通知)。

### `LibraryPlaybackBridge`

| メソッド | 動き |
|---|---|
| `PlaySelected()` | 選択中のものを再生する |
| `PlayAt(index)` | その位置を選んでそのまま再生(一覧をクリックしたときの動き) |
| `Play(mediaId)` | ID を指定して再生 |
| `EnqueueSelected()` / `Enqueue(mediaId)` | いまの再生を止めずに Queue の末尾へ足す |

`ReplaceTracksOnPlay`(既定 true)で 2 つの動きを選べます。

- **true** … `PlayerSession.SetTracks(new[]{ id })` — 選んだ 1 件から始めて、続きはセッションの自動補充(おすすめ)に任せる
- **false** … `PlayerSession.Enqueue(id)` してそこまで `Next()` で進む — いまの曲一覧を残す

呼んでいるのは `PlayerSession` の**既存 API だけ**です。
`AutoQueueEnabled` などの設定も書き換えません
(Phase3-4 で「利用側がセッションの設定を書き換えない」形に整理した方針をそのまま守ります)。

---

## 3. UI について

Phase4-1 の成果物は **`MediaLibrary`(純粋 C#)** です。
UI はその確認手段で、**2 つ用意しました**。

| デモ | 見えるもの | SDK |
|---|---|---|
| `MediaLibraryConsoleDemo` | Console に 6 シナリオ | 不要 |
| `MediaLibraryScreenDemo` | Game ビューに一覧。**クリックで選択・ボタンで再生** | 不要 |

### なぜ画面側が IMGUI(OnGUI)なのか

uGUI の Canvas や VRChat のワールド内 UI(Udon)は、
**Prefab とレイアウトの作り込みが本体**になり、
Phase4-1 の目的(閲覧と選択の仕組み)から焦点がずれます。
IMGUI なら**アセットを 1 つも作らずに動く**ので、
「`MediaLibrary` が UI から使える形になっているか」を最短で確かめられます。

**画面側に判断ロジックはありません。**
一覧・絞り込み・並び順・選択はすべて `MediaLibrary` にあり、
再生への受け渡しは `LibraryPlaybackBridge` が行います。
`MediaLibraryScreenDemo` は**描いて、押されたことを伝えるだけ**なので、
**uGUI や Udon の UI に差し替えても下は 1 行も変わりません。**

> ワールド内 UI(Canvas + Udon)は Phase4-2 以降の想定です。
> `MediaLibrary` は `IMediaLibraryObserver` で変化を知らせる形になっているので、
> Udon 側は「通知が来たら描き直す」だけで載ります。

---

## 4. 再生バックエンドの切り替え(設計方針)

> 今後の実機再生は `VRCAVProVideoPlayer` を標準に。
> ただし両方へ切り替え可能な構造は維持する。

**抽象化はもともと壊れていません。**
`VRCVideoPlayerBridge` は `VRCUnityVideoPlayer` と `VRCAVProVideoPlayer` の共通基底
`BaseVRCVideoPlayer` を包むので、**1 クラスで両対応**です
(Phase3-1 からこの形)。違いを見るのは「生配信を扱えるか」の判定だけです。

Phase4-1 で足したのは**選び方**です。

```csharp
// VRChatVideoBackendHost(Inspector)
[SerializeField] private VideoPlayerPreference _preferredPlayer = VideoPlayerPreference.AVPro;
```

| 値 | 動き |
|---|---|
| `AVPro`(既定) | 同じ GameObject の AVPro を使う。無ければ Unity 版へ降りる |
| `Unity` | `VRCUnityVideoPlayer` を使う |
| `Explicit` | Inspector の `Video Player` に割り当てたものだけを使う(自動で探さない) |

コードからも切り替えられます。

```csharp
host.SetPreferredPlayer(VideoPlayerPreference.Unity);
var adapter = host.Rebuild();   // 組み立て直す(登録し直しは呼び出し側で)
```

**上位は何も知りません。** `VideoBackendAdapter` から上
(`PlayerSession` / `Queue` / `Recommendation`)は
どちらが繋がっているかを見る場所がありません。

> **エディタでの注意:** `VRCAVProVideoPlayer` は Unity エディタ(ClientSim)では映像を出しません。
> エディタで絵を確認したいときだけ `Unity` にしてください。実機は `AVPro` のままで構いません。

---

## 5. ディレクトリ構成(追加分)

```
Assets/SmartMediaPlatform/Library/
├── Runtime/                                  asmdef: Catalog のみ参照 / noEngineReferences
│   ├── IMediaLibrary.cs                      閲覧と選択の契約
│   ├── MediaLibrary.cs                       ★中心。閲覧と選択だけ
│   ├── IMediaLibraryObserver.cs              変化の通知(interface。event ではない)
│   ├── LibrarySortOrder.cs                   並び順
│   └── MediaLibraryFormatter.cs              表示用の整形(URL は出さない)
├── Playback/                                 asmdef: Library + Session …
│   └── LibraryPlaybackBridge.cs              ★両側が見える唯一の場所
├── Demo/
│   ├── MediaLibraryConsoleDemo.cs            Console に 6 シナリオ
│   └── MediaLibraryScreenDemo.cs             Game ビューの一覧(クリックで選択)
├── Editor/
│   └── Phase4MediaLibrarySceneBuilder.cs     シーン生成メニュー
├── Scenes/
│   └── Phase4MediaLibraryDemoScene.unity     カメラ + 上記 2 デモ
└── Tests/EditMode/
    ├── MediaLibraryTests.cs                  52 ケース
    └── LibraryPlaybackBridgeTests.cs         21 ケース

Assets/SmartMediaPlatform/Video/VRChat/Runtime/
├── VideoPlayerPreference.cs                  ★新規 Inspector で選ぶ enum
└── VRChatVideoBackendHost.cs                 　変更 好みに合わせてプレイヤーを選ぶ
```

---

## 6. テスト方法

### 6-1. DemoScene(いちばん分かりやすい)

**Tools > Smart Media Platform > Open Phase4-1 Media Library Demo Scene** → **Play**

Game ビューに一覧が出ます。

| 操作 | 期待 |
|---|---|
| 上の **すべて / Music / Video** を押す | 一覧が切り替わる(件数も変わる) |
| 上の **登録順 / タイトル / アーティスト / ジャンル / 長さ** を押す | 並びが変わり、**選択は保たれる** |
| 一覧の行をクリック | その行が選択され、右に詳細(タイトル・アーティスト・ジャンル・タグ・長さ)が出る |
| **▶ この曲を再生する** | 下の PlayerSession の「再生中」が選んだものに変わる |
| **＋ Queue の末尾に足す** | 再生は変わらず Queue の件数が増える |
| 下の **次へ / 前へ / 停止** | PlayerSession が今までどおり動く |

> 音は出ません。この画面は `DummyBackend`(ログだけ)に繋いであります。
> Library が PlayerSession へ正しく渡せているかを見るためのシーンです。

シーンが開けない場合は
**Tools > Smart Media Platform > Create Phase4-1 Media Library Demo Scene (再生成)**。

### 6-2. ConsoleDemo

同じシーンで **Play** すると Console にも出ます(1 つの GameObject に両方載っています)。

```
=== Phase4-1 Media Library Demo ===

── 1. カタログの中身を一覧で見る
  21 件 / 種別 すべて / 並び CatalogOrder
    1. Neon Skyline (Official Video)  / Aurora Drive  [Video] Synthwave  #mv #night #retro  4:22
    2. Midnight Circuit (Official Video)  / Aurora Drive  [Video] Synthwave  #mv #night #electronic  3:58
  …

── 2. Music / Video で絞り込む
  Music だけ  -> 10 件  例: [music-001] …
  Video だけ  -> 10 件  例: [video-001] Neon Skyline (Official Video) / Aurora Drive
  両方        -> 20 件
  すべて      -> 21 件

── 3. タイトル・アーティスト・ジャンル・タグを表示する
  選択中: Neon Skyline (Official Video)
    アーティスト : Aurora Drive
    ジャンル     : Synthwave
    タグ         : #mv #night #retro
    種別         : Video
    長さ         : 4:22
    MediaId      : video-001

── 5. 選んだものを PlayerSession へ渡す
  選択 = video-003
  PlaySelected() -> True
    PlayerSession.CurrentMediaId = video-003 [Playing]

── 6. Media Library は閲覧と選択だけ(参照関係で確認)
  Library は Session を参照しているか -> False(false なのが正しい)
```

### 6-3. EditMode テスト(いちばん確実)

**Window > General > Test Runner > EditMode > Run All**

| 見るところ | 期待 |
|---|---|
| `SmartMediaPlatform.Library.Tests` | **73 ケース** すべて緑 |
| 全体 | **763 ケース** すべて緑(既存 690 は変更なし) |

要件との対応。

| 要件 | テスト |
|---|---|
| 一覧を表示できる | `NewLibrary_ShowsEverythingInCatalogOrder` / `Formatter_ShowsTitleArtistGenreAndTags` |
| Music / Video を表示できる | `ShowOnly_Music_KeepsOnlyMusic` / `ShowOnly_Video_KeepsOnlyVideos` / `MusicPlusVideo_EqualsTheirSeparateCounts` |
| タイトル・アーティスト・ジャンル・タグ | `Formatter_ShowsTitleArtistGenreAndTags` |
| 選択できる | `Select_PicksByIndex` / `SelectById_PicksByMediaId` / `SelectNext_MovesForwardAndWraps` ほか |
| 選択が保たれる / 外れる | `Selection_SurvivesASortChange` / `Selection_IsDroppedWhenItIsFilteredOut` |
| PlayerSession へ渡せる | `PlaySelected_StartsTheSelectedMedia` / `EnqueueSelected_AddsToTheQueueWithoutStopping` |
| 閲覧と選択だけ(責務) | `LibraryAssembly_DoesNotReferenceThePlaybackStack` / `Library_NeverExposesAUrl` |
| 再生エンジンを変えていない | `Session_KeepsItsOwnAutoQueueBehaviour` / `Session_StillAdvancesOnItsOwn` / `Bridge_IsNotABackendObserver` |

### 6-4. 手っ取り早く

1. `Phase4MediaLibraryDemoScene` を **Play**
2. **Video** タブを押す → 動画 10 件だけになる
3. 適当な行をクリック → 右にタイトル・アーティスト・ジャンル・タグが出る
4. **▶ この曲を再生する** → 下の「再生中」が変わる

ここまで動けば Phase4-1 の要件は満たしています。

---

## 7. 確認できていないこと

- **コンパイルとテストの実行**:この作業環境に Unity / .NET SDK / VRChat SDK が無いため、
  **一度も実行検証できていません。**
  括弧対応・メンバー名重複の静的チェック(全 165 ファイル)と、
  参照関係・状態遷移の机上確認のみです。
- **AVPro の実機動作**:`VideoPlayerPreference` の切り替えは
  「同じ GameObject から型名で選ぶ」ところまでを実装・確認しましたが、
  **AVPro で実際に映像が出るかは実機でしか確かめられません**
  (エディタでは AVPro は描画されません)。
- **IMGUI の見た目**:解像度・DPI によって行が詰まる可能性があります。
  `_fontSize` で調整してください。

---

## 8. 実装中に見つけた、報告しておきたいこと

### 8-1. `Next()` は Queue を補充するので「空になるまで進める」は書けない

`LibraryPlaybackBridge` の `ReplaceTracksOnPlay = false` で、
足したものまで `Next()` で進める処理を書いたとき、最初は

```csharp
while (_session.Queue.Count > 1 && !Same(_session.CurrentMediaId, mediaId)) _session.Next();
```

としていました。**これは止まらない可能性があります。**
`PlayerSession.Next()` は中で `EnsureQueueFilled()` を 2 回呼ぶので、
**Queue の件数は減らない**からです。
回数に上限を付けて直しました(足した時点の Queue の長さぶんだけ進めれば必ず届く)。

同じ形の書き方が他にもないか確認しましたが、見つかりませんでした。

### 8-2. `MediaType` を増やしたときに 2 箇所直す必要がある

`MediaLibrary.AllTypes`(絞り込みなしを表す並び)と
`MediaLibraryFormatter.FormatSummary` の「すべて」判定(`types.Count >= 5`)が
`MediaType` の要素数に依存しています。
`Podcast` や `Live` に加えて新しい種別を足すときは、この 2 箇所も直してください。

`Enum.GetValues` を使えば自動になりますが、
UdonSharp が `Enum.GetValues` を扱えないため、
Phase1-2 から通している「Udon へ移せる書き方」を優先して**あえて配列で持っています**。

### 8-3. Phase3-4 からの持ち越し

- `AutoQueueService`(Playlists)の補充一本化は未着手のままです
  ([Phase3-4 §7-1](Phase3-4_PlaybackOrchestrationAndRecovery.md))
- `DummyVideoBackendAdapter` と `VideoBackendAdapter` の重複も残っています

---

## 9. Phase4-2 以降への引き継ぎ

| 項目 | 内容 |
|---|---|
| ワールド内 UI | `MediaLibrary` は `IMediaLibraryObserver` で変化を知らせる形。Udon 側は「通知が来たら描き直す」だけで載る |
| 検索 | `IMediaCatalog` に `SearchByTitle` / `SearchByArtist` / `SearchByTag` / `SearchByGenre` が既にある。`MediaLibrary` に「絞り込みの種」を足す形で入る |
| Playlist 連携 | `LibraryPlaybackBridge` と同じ形で `LibraryPlaylistBridge` を足すのが素直(Library 側は変更不要) |
| お気に入り | 「積んでよいか」ではなく「見せるか」なので、`MediaLibrary` 側の絞り込みとして入る |
| ページング | 一覧が数百件になったら `Entries` をそのまま描くのは重い。`GetAt(i)` があるので範囲指定の描画に移行できる |
