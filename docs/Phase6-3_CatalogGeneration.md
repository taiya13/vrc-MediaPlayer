# Phase6-3 — 取り込んだ動画を Catalog へ反映する

Phase6-2 で「YouTube から取ってくる」ところまで出来ました。
Phase6-3 は、その結果を **Catalog.asset に保存し、ワールドで実際に鳴らせる形（VRCUrl）まで焼く**
一連の流れを Catalog Builder の窓ひとつで完結させます。

MediaPlayer 本体（`World/Udon` 以下）は **1 行も変えていません**。

---

## 1. 取り込みから Catalog 生成までの流れ

```
 ①取得                ②選ぶ                     ③保存            ④焼く
┌──────────┐   ┌──────────────────┐   ┌───────────┐   ┌─────────────┐
│ Importer │ → │ CatalogImport    │ → │ Catalog   │ → │ UdonMedia   │
│ (YouTube)│   │ Selection        │   │ Draft     │   │ Catalog     │
└──────────┘   └──────────────────┘   └───────────┘   └─────────────┘
   通信する        まだ何も書かない       Merge して保存    VRCUrl 配列
                                        Catalog.asset      （実行時に
                                                            作れないので
                                                            ここで用意）
```

| 段 | 担当クラス | やること | やらないこと |
|---|---|---|---|
| ① | `YouTubeCatalogImporter` → `YouTubeDataApiClient` | API を叩いて `CatalogDraftItem[]` を返す | Catalog に触らない |
| ② | `CatalogImportSelection` | どれを入れるか。既存 ID に印を付ける | 通信しない・Unity に依存しない |
| ③ | `CatalogDraft.Merge` → `CatalogDraftIO.Save` | `MediaCatalogAsset.SetEntries` で保存 | VRCUrl を作らない |
| ④ | `CatalogUrlTableBridge` → `UdonCatalogBaker` | シーンの `UdonMediaCatalog` へ `VRCUrl` を焼く | アセットを書き換えない |

責務は Phase6-1 のまま動かしていません。
Importer は **取ってくるだけ**、`CatalogDraft` は **編集中の入れ物**、
窓は **見せて押されたことを伝えるだけ** です。

### `VRCUrl` の扱い

Phase1-2 からの決まりどおり、**`VRCUrl` を知っているのは VideoBackend 層と
その入り口である `UdonMediaCatalog` だけ**です。
`CatalogDraftItem` も `MediaItem` も URL を **ただの文字列**で持ちます。
文字列 → `VRCUrl` に変わるのは ④ の焼き込み（編集時）ただ 1 か所で、
**実行時に `VRCUrl` を作る箇所はありません**。

---

## 2. Builder UI の変更点

`Window > Smart Media Platform > Catalog Builder`

### 変わったところ

1. **取り込みバーが 3 ステップ表示になった**
   上に `① 取得 → ② 選んで追加 → 保存 → ③ VRCUrl へ焼く` と出ます。
   ボタンにも番号が付いているので、次に押すものが分かります。

2. **「取得」が即 Catalog へ入れなくなった**（← 一番大きい変更）
   Phase6-2 までは押した瞬間に全部入っていました。
   いまは **取得結果の一覧が下に出るだけ**で、Draft には入りません。

3. **取得結果の選択欄が増えた**

   ```
   取得結果 42 件 / 選択 12 件
   [全選択] [全解除] [反転] [まだ無いものだけ]
   ┌────────────────────────────────────────┐
   │ ☑ 【あり】曲名 A            3:45        │
   │ ☑ 曲名 B                    4:12        │
   │ ☐ 曲名 C                    2:58        │
   └────────────────────────────────────────┘
   [ ② 選んだ 12 件を追加する ]     混ぜ方 [追加 ▼]
   ```

   - **`【あり】`** は、いま編集しているカタログに**同じ ID が既にある**もの。
     同じチャンネルを 2 回取り込んでも、どれが新顔か一目で分かります。
   - **`まだ無いものだけ`** はその逆を選ぶボタン。2 回目の取り込みで押します。
   - 最初は**全部チェックが入った状態**で出ます（貼って押すだけを最短にするため）。

4. **「ワールドへ反映」欄が増えた**

   ```
   ワールドへ反映
   [ ③ VRCUrl へ焼く(シーンの Catalog 1 個) ]
   ```

   - **未保存だと押せません。** 保存していない分は焼いても意味がないためです
     （その旨を黄色で出します）。
   - シーンに `SmartMediaPlayer` が無ければ、置いてくださいと出ます。
   - VRChat SDK が入っていないプロジェクトでは、この欄は静かに消えます
     （窓自体は開けます）。

### 変わっていないところ

ツールバー（アセット選択 / 新規 / 読み直す / 保存）、左の一覧、右の 1 件編集は
Phase6-1 のままです。手で 1 件ずつ足す使い方もそのまま出来ます。

---

## 3. Catalog の保存方法

保存の入り口は **`CatalogDraftIO.Save` ただ 1 つ**です。

```
CatalogDraft.ToMediaItems()      ← 不完全な行（ID・見出し・URL のどれか欠け）は落ちる
        ↓
MediaCatalogAsset.SetEntries()   ← アセットへ書き込む唯一の口
        ↓
EditorUtility.SetDirty + AssetDatabase.SaveAssets
```

- **作りかけを許します。** URL がまだ無い行があっても保存できます。
  その行はアセットに入らないだけで、消えはしません。
- **ID が重なっていると保存できません。** `FindDuplicateIds()` が
  ツールバーに赤字で出し、保存ボタンを止めます。

### 混ぜ方（Merge）3 種

| 混ぜ方 | 同じ ID があったら | 使うとき |
|---|---|---|
| 追加（`MergeAppend`） | **飛ばす** | ふつう。既存を守る |
| 更新（`MergeUpdate`） | **上書きする** | タイトルが変わった等 |
| 置換（`MergeReplace`） | 全部消してから入れる | 作り直す |

既定は「追加」です。`Merge` は Phase6-1 で作った既存機構をそのまま使っています。

---

## 4. VRCUrlTable の生成方法

`CatalogUrlTableBridge.BakeIntoScene(asset)` が行います。

```
Catalog.asset (文字列 URL)
        ↓ LoadItems()
   MediaItem[]
        ↓ UdonCatalogBaker.Bake(catalog, items)
UdonMediaCatalog の VRCUrl[] （シーン上の実物）
        ↓ EditorUtility.SetDirty / AssetDatabase.SaveAssets
```

**設計上の要点**

- **シーンにある実物を書き換えます。** Prefab のアセット側ではありません。
  ワールドに乗るのは Hierarchy に置かれた実物なので、そちらを直さないと反映されません。
  → **焼いたあと、シーンの保存を忘れないでください。**
- **シーンの `UdonMediaCatalog` を全部探して、全部に焼きます。**
  カタログを 2 個置いていても取りこぼしません。
- **VRChat SDK に直接依存しません。**
  `UdonMediaCatalog` も `UdonCatalogBaker` も**型名の文字列で探します**
  （Phase3 の `UdonSharpSceneUtility` と同じ方針）。
  SDK が無いプロジェクトでも Catalog Builder はコンパイルでき、開けます。

---

## 5. MediaPlayer へ反映するまでの手順

1. `Window > Smart Media Platform > Catalog Builder` を開く
2. ツールバーで **カタログを選ぶ**（無ければ `新規`）
3. 取り込み元に **YouTube** を選び、URL を貼って **① 取得する**
4. 出てきた一覧から欲しいものにチェック → **② 選んだ N 件を追加する**
5. ツールバーの **保存**
6. Hierarchy に **`SmartMediaPlayer.prefab` が置いてある**ことを確認
7. **③ VRCUrl へ焼く** を押す
8. **シーンを保存**（`Ctrl+S`）
9. VRChat SDK の **Build & Test**

これで、ワールド内のパネルの Library に並びます。

> **2 回目以降**：3 まで同じ → **まだ無いものだけ** を押す → ② → 保存 → ③ → シーン保存。

---

## 6. Phase6-4 以降に回したもの

今回、依頼どおり**入れていません**。

| 機能 | 中身 | なぜ後回しか |
|---|---|---|
| **サムネイル画像のダウンロード** | URL だけでなく画像を落として `Texture` にする | 落とす場所・容量・ワールド容量制限の設計が要る。`CatalogDraftItem.ThumbnailPath` は器だけ用意済み |
| **関連動画の自動生成** | `MediaItem.RelatedIds` をチャンネル・タグから自動で埋める | どれを「関連」とするかは推薦の設計判断。Recommendation 層と一緒に決めたい |
| **差分更新** | 前回取り込みとの差だけを出す（消えた動画の検出含む） | 「消えた動画をどうするか」を決める必要がある。いまは `【あり】` の印までで止めてある |

その他、あると良さそうなもの：
- 取り込み結果の並べ替え・絞り込み（長さ、公開日）
- 複数チャンネルのまとめ取り込み
- 焼いた内容と Catalog.asset のズレ検出

---

## 7. 検査

```bash
python3 tools/typecheck/genmeta.py     # 足りない .meta を作る
python3 tools/typecheck/typecheck.py   # コンパイル
python3 tools/typecheck/runtests.py    # EditMode テスト
python3 tools/typecheck/udon_lint.py   # Udon で書けない書き方の検出
```

Phase6-3 時点：**947 / 947 通過**（Phase6-2 の 937 から +10）。

追加した 10 ケース（`CatalogDraftTests.cs`）:

| ケース | 確かめること |
|---|---|
| `EverythingIsSelectedWhenItArrives` | 取り込み直後は全選択 |
| `RowsCanBeTickedIndividually` | 1 件ずつ入り切りできる |
| `BulkSelectionWorks` | 全選択 / 全解除 / 反転 |
| `OnlySelectedRowsAreHandedOver` | 選んだものだけ渡る。順番は保つ |
| `ItemsAlreadyInTheDraftAreMarked` | 既存 ID に印が付く |
| `SelectOnlyNewSkipsWhatIsAlreadyThere` | まだ無いものだけ選ばれる |
| `SelectedItemsGoThroughTheExistingMerge` | 選択 → `Merge` が既存機構を通る |
| `ImportingTwiceWithSelectOnlyNewAddsNothingTheSecondTime` | 2 回目は増えない |
| `ClearingTheSelectionEmptiesIt` | `Clear` で空になる |
| `ANullImportIsSafe` | 空・null・範囲外で落ちない |
