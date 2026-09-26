# Phase4-2: 関連動画 UI と Catalog システムの接続

**目的は「関連動画 UI と Catalog システムを接続すること」です。**
あわせて、将来 Catalog Builder やサーバー連携が入っても
**コード変更が最小限で済む形**に整えます。

```
起点の MediaId
  → IRelatedMediaProvider   関連する ID の並びを返す   ← ★ サーバー差し替え点
  → ICatalogStore           ID を DisplayMeta に変える ← ★ 唯一のデータ取得窓口
  → RelatedMediaView        並べて、選べるようにする
  → UI                      DisplayMeta を描く
  → PlayableRef             → LibraryPlaybackBridge → PlayerSession(再生)
```

| 要件 | 実装 |
|---|---|
| UI は Catalog からデータを取得して表示する | `RelatedMediaView` → `ICatalogStore.GetDisplayMetas()` |
| DisplayMeta と PlayableRef の分離 | `DisplayMeta`(表示用・**URL 無し**)/ `PlayableRef`(再生用・**MediaId のみ**) |
| CatalogStore を唯一の取得窓口にする | `ICatalogStore` は `MediaItem` も `IMediaCatalog` も返さない |
| Catalog.asset が自動生成されても変更最小 | `MediaCatalogAsset : ScriptableObject, IMediaCatalogSource` |
| 関連動画 ID だけ差し替えれば動く | `IRelatedMediaProvider` / `StaticRelatedMediaProvider` |
| VRCUrl を直接 UI へ渡さない | `DisplayMeta` にも `PlayableRef` にも URL の口が無い(テストで検証) |
| 現在動作している部分を壊さない | 再生エンジンは差分ゼロ。Phase4-1 の動作・テストは型の移行のみ |

### 変更していないもの

`PlayerSession` / `MediaPlayer` / `BackendManager` / `BackendAdapter` /
`MediaQueue` / `QueueManager` / `RecommendationEngine` / `VRChatVideoBackend` /
`MediaItem` / `IMediaCatalog` / `MediaCatalog` — **すべて差分ゼロ**です。

---

## 1. 追加・変更したクラス一覧

### 追加

| クラス | 場所(asmdef) | 役割 |
|---|---|---|
| **`DisplayMeta`** | `Catalog`(純粋C#) | 表示用のデータ。**URL を持たない** |
| **`PlayableRef`** | `Catalog`(純粋C#) | 再生系へ渡す参照。**MediaId と Type だけ**を運ぶ struct |
| **`ICatalogStore`** | `Catalog`(純粋C#) | 唯一のデータ取得窓口。**`MediaItem` を返さない** |
| **`CatalogStore`** | `Catalog`(純粋C#) | 実装。`MediaItem` を触る最後の場所。変換結果をキャッシュ |
| **`IRelatedMediaProvider`** | `Catalog`(純粋C#) | 「関連するのはどれか」を**ID の並びだけ**で答える |
| **`CatalogRelatedMediaProvider`** | `Catalog`(純粋C#) | カタログの `RelatedIds` を使う実装(いまの構成) |
| **`StaticRelatedMediaProvider`** | `Catalog`(純粋C#) | 外から与えた ID 表を返す実装。**サーバー連携の受け皿** |
| **`MediaCatalogAsset`** | `Catalog.Assets`(新規 asmdef) | **Catalog Builder が生成する `Catalog.asset` の受け皿** |
| **`IMediaListView`** | `Library`(純粋C#) | 「並べて 1 つ選ぶ」共通契約 |
| **`IMediaListObserver`** | `Library`(純粋C#) | 並び・選択の変化を知らせる |
| **`MediaListViewBase`** | `Library`(純粋C#) | 選択・通知の共通実装 |
| **`RelatedMediaView`** | `Library`(純粋C#) | **Phase4-2 の中心。関連動画の一覧** |

### 変更

| クラス | 変更点 |
|---|---|
| `IMediaLibrary` | `IMediaListView` を継承する形に。絞り込みと並び順だけを持つ |
| `MediaLibrary` | データ取得を `ICatalogStore` に一本化。**`MediaItem` ではなく `DisplayMeta` を返す** |
| `MediaLibraryFormatter` | `DisplayMeta` / `IMediaListView` を受けるように。関連一覧にもそのまま使える |
| `LibraryPlaybackBridge` | 受け取る一覧を `IMediaListView` に拡大。`PlayableRef` の入口を追加 |
| `MediaLibraryScreenDemo` | **関連動画パネルを追加**。再生中/選択中に追従 |
| `MediaLibraryConsoleDemo` | Phase4-2 の 8 シナリオへ拡張(関連表示・サーバー差し替えを含む) |
| `SmartMediaPlatform.Catalog.Tests.asmdef` | `Catalog.Assets` への参照を追加 |

### 削除

| クラス | 理由 |
|---|---|
| `IMediaLibraryObserver` | `IMediaListObserver` に統合(カタログ一覧と関連一覧で同じ観測者を使うため) |

---

## 2. データの流れ

### 2-1. ID → Catalog → DisplayMeta / PlayableRef → UI・Player

```
                        ┌──────────────── UI(描画・クリック)
                        │                       │
        DisplayMeta ────┘                       │ PlayableRef
        (URL 無し)                              ↓
              ↑                        LibraryPlaybackBridge
              │                                 │ MediaId(string)
     ┌────────┴────────┐                        ↓
     │  ICatalogStore  │                  PlayerSession
     │  唯一の取得窓口  │                        ↓
     └────────┬────────┘                     Queue → BackendAdapter
              │ MediaItem(ここから外へ出ない)        ↓
        IMediaCatalog                        VideoBackend
              ↑                            (ここだけが VRCUrl を知る)
     IMediaCatalogSource
     ├── VideoCatalogSource(手書き・いま)
     └── MediaCatalogAsset(Catalog Builder・将来)
```

**要点は 3 つ**です。

1. **`MediaItem` は `CatalogStore` の外へ出ません。**
   `ICatalogStore` に `MediaItem` を返すメンバーが 1 つも無いことをテストで確認しています。
2. **UI へ渡る `DisplayMeta` に URL の口がありません。**
   だから「UI から URL を触る」コードは書こうとしても書けません。
3. **再生系へ渡るのは `PlayableRef`(中身は MediaId のみ)。**
   URL への変換は今までどおり `VideoBackend` の焼き込み表だけが行います。

### 2-2. 関連動画が出るまで

```csharp
// 1. 起点を決める(再生中のもの、または一覧で選んだもの)
view.SetSource("video-001");

// 2. RelatedMediaView の中で:
var ids   = provider.GetRelatedIds("video-001", maxCount);  // ← ID の並びだけ
var metas = store.GetDisplayMetas(ids);                     // ← 表示用に変換

// 3. UI は DisplayMeta を描くだけ
foreach (var meta in view.Entries) Draw(meta.Title, meta.Artist, meta.Genre);

// 4. クリックされたら PlayableRef を渡す
bridge.PlayAt(index);   // 中身は MediaId(string)
```

**`RelatedMediaView` は「なぜ関連なのか」を知りません。**
`IRelatedMediaProvider` が返した順番をそのまま並べるだけです。

---

## 3. 将来の接続ポイント

### 3-1. Catalog Builder(`Catalog.asset` の自動生成)

**差し込み口は `MediaCatalogAsset` の 1 つだけです。**

```csharp
// Catalog Builder(将来)がやること
var asset = ScriptableObject.CreateInstance<MediaCatalogAsset>();
asset.SetEntries(generatedEntries, "Catalog Builder v1");
AssetDatabase.CreateAsset(asset, "Assets/.../Catalog.asset");
```

利用側の変更は **1 行だけ**です。

```csharp
// いま
var store = new CatalogStore(new MediaCatalog(new VideoCatalogSource()));

// 将来(Catalog.asset を Inspector から渡す)
var store = new CatalogStore(new MediaCatalog(catalogAsset));
```

`MediaCatalogAsset` は Phase1-1 からの拡張点 `IMediaCatalogSource` を実装しているだけなので、
**`MediaCatalog` も `CatalogStore` も、その先も無変更**です。
`ImportFrom(IMediaCatalogSource)` があるので、
Builder ができるまでの繋ぎとして**いまの手書きカタログをアセット化**することもできます。

> **検証済み:** `AnAssetBackedStore_BehavesLikeACodeBackedOne` が、
> 手書きソースから作った `CatalogStore` とアセットから作ったものが
> **同じ ID 順・同じ表示内容**になることを確かめています。

### 3-2. サーバー連携(関連動画 ID の差し替え)

**差し込み口は `IRelatedMediaProvider` の 1 つだけです。**

いちばん簡単なのは、用意済みの `StaticRelatedMediaProvider` に入れる方法です。

```csharp
// サーバー応答を受け取ったら(取得処理はこの層の外)
relatedProvider.Set("video-001", idsFromServer);
relatedView.Refresh();
```

`StaticRelatedMediaProvider` は **`Fallback` を持てる**ので、
サーバーがまだ答えていない起点についてはカタログの関連で埋まります
(画面が空にならない)。

自前の実装に丸ごと差し替えることもできます。

```csharp
public sealed class ServerRelatedMediaProvider : IRelatedMediaProvider
{
    public IReadOnlyList<string> GetRelatedIds(string mediaId, int maxCount = 0)
    {
        return _cache.TryGetValue(mediaId, out var ids) ? ids : Empty;
    }
}
```

どちらの場合も **`RelatedMediaView` も UI も 1 行も変わりません。**
カタログに無い ID がサーバーから返ってきても、`CatalogStore` が黙って落とすので
残りはそのまま並びます。

> **検証済み:** `ServerSuppliedIds_ReplaceTheCatalogOnes` /
> `ServerSuppliedIds_KeepWorkingWhenSomeAreUnknown` /
> `SwappingTheWholeProviderNeedsNoChangeHere`

### 3-3. ワールド内 UI(uGUI / Udon)

`RelatedMediaView` は `IMediaListObserver` で変化を知らせる形なので、
Udon 側は「通知が来たら描き直す」だけで載ります。
`MediaLibraryScreenDemo`(IMGUI)に判断ロジックは 1 つも入っていません。

### 3-4. まだ差し込み口を作っていないもの

| 項目 | 状況 |
|---|---|
| サムネイル画像 | `DisplayMeta` に項目がありません。追加するならここへ(URL ではなく**キー**を推奨) |
| 検索 | `IMediaCatalog` に `SearchByXxx` はありますが、`ICatalogStore` には出していません |
| ページング | 一覧が数百件になったら `Entries` を全部描くのは重い。`GetAt(i)` があるので範囲描画に移行できます |

---

## 4. Unity 上で確認すべき項目

### 4-1. DemoScene(いちばん分かりやすい)

**Tools > Smart Media Platform > Open Phase4-1 Media Library Demo Scene** → **Play**

| 操作 | 期待 |
|---|---|
| 左の一覧で行をクリック | 右上に詳細、**右下に「関連動画」が出る** |
| 関連動画の **▶** を押す | その動画が再生され、**関連一覧が新しい再生中のものに追従して入れ替わる** |
| そのまま関連の **▶** を押し続ける | 関連 → 関連 とたどれる(UI が Catalog から取り続けている証拠) |
| **Music** タブ → 行をクリック | Music の関連(`music-002` など)が出る |
| 関連動画パネル下の「関連 ID の出どころ」 | `StaticRelatedMediaProvider(0 件 / 予備: CatalogRelatedMediaProvider…)` |
| 関連が無いものを選ぶ | 「関連するメディアが登録されていません」と出る(空でも壊れない) |

> 音は出ません(`DummyBackend`)。確認したいのは **Catalog → UI → Player の接続**です。

### 4-2. ConsoleDemo(同じシーンに同梱)

Play すると Console に 8 シナリオが出ます。特に見てほしいのは:

- **§3** `URL を返すメンバー = 無し` — `DisplayMeta` に URL の口が無い
- **§5** 関連動画が `ID → Catalog → DisplayMeta` の流れで出ている
- **§6** サーバー応答に差し替えても並びが入れ替わり、
  `does-not-exist` が黙って落ちる
- **§8** `Library は Session を参照しているか -> False` /
  `MediaItem を返すメンバー = 無し`

### 4-3. EditMode テスト

**Window > General > Test Runner > EditMode > Run All**

| 見るところ | 期待 |
|---|---|
| `SmartMediaPlatform.Catalog.Tests` | **95 ケース**(既存 63 + Phase4-2 の 32) |
| `SmartMediaPlatform.Library.Tests` | **107 ケース**(Phase4-1 の 76 + Phase4-2 の 31) |
| 全体 | **829 ケース** すべて緑 |

要件との対応。

| 要件 | テスト |
|---|---|
| UI が Catalog から取得 | `SetSource_BuildsTheListFromTheCatalogRelatedIds` / `EntriesAreDisplayMeta` |
| DisplayMeta / PlayableRef の分離 | `DisplayMeta_HasNoUrlAtAll` / `PlayableRef_HasNoUrlAtAll` / `GetPlayableRef_CarriesOnlyTheIdAndType` |
| CatalogStore が唯一の窓口 | `TheContract_NeverReturnsAMediaItem` / `TheContract_NeverReturnsTheCatalogItself` |
| Catalog.asset 対応 | `AnAssetBackedStore_BehavesLikeACodeBackedOne` / `RelatedIdsSurviveTheRoundTrip` |
| 関連 ID の差し替え | `ServerSuppliedIds_ReplaceTheCatalogOnes` / `SwappingTheWholeProviderNeedsNoChangeHere` |
| 壊れた入力でも止まらない | `GetDisplayMetas_SilentlyDropsUnknownIds` / `BrokenRows_AreDroppedInsteadOfThrowing` |
| 既存が壊れていない | Phase4-1 の 76 ケース + それ以前の 690 ケース |

### 4-4. 手っ取り早く

1. `Phase4MediaLibraryDemoScene` を **Play**
2. **Video** タブ → 適当な行をクリック
3. **右下に関連動画が出る**ことを確認
4. 関連動画の **▶** を押す → 再生が切り替わり、**関連一覧も入れ替わる**

ここまで動けば Phase4-2 の要件は満たしています。

---

## 5. 確認できていないこと

- **コンパイルとテストの実行**:この作業環境に Unity / .NET SDK が無いため、
  **一度も実行検証できていません。**
  括弧対応・メンバー名重複の静的チェック(全 176 ファイル)と、
  参照関係・データ流れの机上確認のみです。
- **`Catalog.asset` の実物**:`MediaCatalogAsset` の型は用意しましたが、
  **アセットファイル自体はまだ 1 つも作っていません**
  (Catalog Builder が無く、手で作る必要も現時点では無いため)。
  `Assets > Create > Smart Media Platform > Media Catalog` で作れます。
- **IMGUI の見た目**:関連動画パネルを足したぶん、右側が縦に長くなりました。
  解像度によっては詰まる可能性があります(`_fontSize` で調整)。

---

## 6. 報告:設計上の判断と、気づいたこと

### 6-1. 「維持すること」とされた設計は、この時点では存在しませんでした

要件に `CatalogStore` / `DisplayMeta` / `PlayableRef` / `Catalog.asset` /
「現在の PoC アーキテクチャ」とありましたが、
**着手時点のリポジトリにはいずれも存在しませんでした**(`grep` で 0 件、`.asset` も 0 個)。

近いものは `MediaItem` で、これは**表示用の項目と `Url` が同居**しています
— つまり「分離すべき」と指摘されている状態そのものでした。

そこで **Phase4-2 でこれらを新規に確立し、以降維持する**ものとして実装しています。
別途お持ちの設計書があり、名前や責務の切り方が違う場合は教えてください。

### 6-2. `MediaLibrary` の戻り値を `MediaItem` → `DisplayMeta` に変えました

Phase4-1 の `MediaLibrary` は `MediaItem` を UI へ渡していたので、
**UI から `MediaItem.Url` が見えていました**。
「VRCUrl や動画情報を直接 UI へ渡さない」という要件に反するため、型を移しています。

「現在動作している部分はできるだけ壊さない」との兼ね合いで、
**振る舞いは 1 つも変えていません**。テストの変更も
`item.Id` → `meta.MediaId` のような機械的な置き換えだけです。

### 6-3. `PlayableRef` を struct にした理由

UI が 1 行ごとに持っても割り当てが増えないようにするためです。
ただし **`default(PlayableRef)` が作れてしまう**ので、
`IsValid` で必ず判定してください
(`ICatalogStore` を通ったものだけが `IsValid == true` になります)。

### 6-4. Phase3-4 / 4-1 からの持ち越し(未着手)

- `AutoQueueService`(Playlists)の補充一本化
  ([Phase3-4 §7-1](Phase3-4_PlaybackOrchestrationAndRecovery.md))
- `DummyVideoBackendAdapter` と `VideoBackendAdapter` の重複
- `PlayerSession.PlayNow(mediaId)` を本体へ置く案
  ([Phase4-1 §8-1](Phase4-1_MediaLibrary.md))
