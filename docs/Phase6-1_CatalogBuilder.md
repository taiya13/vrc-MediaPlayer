# Phase6-1 — Catalog Builder の土台

**目的**: カタログを作る道具の土台を完成させること。
**YouTube 連携は行いません**(Phase6-2 以降)。

---

## 1. Builder の構成

```
Assets/SmartMediaPlatform/CatalogBuilder/
├── Runtime/                        Unity にも VRChat にも依存しない判断
│   ├── CatalogDraft.cs             編集中のカタログ 1 冊(判断はここに全部)
│   ├── CatalogDraftItem.cs         編集中の 1 件(作りかけを許す)
│   └── ICatalogImporter.cs         取り込み元の口 + CatalogImportResult
├── Editor/
│   ├── CatalogBuilderWindow.cs     EditorWindow(表示と入力だけ)
│   ├── CatalogDraftIO.cs           Catalog.asset の読み書き・新規作成
│   └── CatalogImporterRegistry.cs  取り込み元を全アセンブリから探す
└── Tests/EditMode/
    └── CatalogDraftTests.cs        判断の検証(20 ケース)
```

**MediaPlayer 本体には 1 行も触っていません。**
接点は `MediaCatalogAsset` 1 つだけです。

### なぜ Runtime / Editor を分けたか

`EditorWindow` は EditMode テストから触れません。
Phase5 の `PlaybackModel` / `ListScrollModel` と同じで、
**判断を Unity 非依存の側へ寄せて検証する**ためです。

窓が持つのは「いま何を編集しているか」「どこを開いているか」だけで、
**足す・消す・混ぜる・焼く**はすべて `CatalogDraft` の仕事です。

---

## 2. EditorWindow の構成

`Tools > Smart Media Platform > Catalog Builder`

```
┌───────────────────────────────────────────────────┐
│ [Catalog.asset ▼] [新規] [読み直す] [保存]  未保存 │  ツールバー
├──────────────────┬────────────────────────────────┤
│ 12 件 / 完成 10 件 │ ID          media-001         │
│ 1. ⚠ (名前なし)   │ 見出し      …                  │
│ 2. サンプル A     │ アーティスト …                 │
│ 3. サンプル B     │ ジャンル / 種別 / URL / 長さ    │
│ …                │ タグ[＋] 関連 ID[＋]           │
│ [＋追加][複製]    │ 出どころ  手入力                │
│ [削除][▲][▼]     │ サムネイル(未使用)             │
├──────────────────┴────────────────────────────────┤
│ 取り込み  [取り込み元 ▼] 入力欄 [混ぜ方 ▼] [取り込む] │
├───────────────────────────────────────────────────┤
│ ID 重複の警告 / 保存すると何件書かれるかの案内        │
└───────────────────────────────────────────────────┘
```

| 機能 | 中身 |
| --- | --- |
| 読み込み | 欄へ `Catalog.asset` をドラッグ、または一覧から選ぶ |
| 新規作成 | 保存先を選んで空のアセットを作る |
| 保存 | **完成している件だけ**書く(ID・見出し・URL がそろっているもの) |
| 追加 / 複製 / 削除 / 並べ替え | 複製は ID を自動でずらす |
| 未保存の保護 | 切り替え・読み直しの前に確認ダイアログ |
| ID 重複 | 常に検出して赤で警告。重複があると焼かせない |

---

## 3. Importer アーキテクチャ

### 口(Phase6-1 で決めたのはここまで)

```csharp
public interface ICatalogImporter
{
    string DisplayName    { get; }   // タブに出す名前
    string InputHint      { get; }   // 入力欄の説明
    bool   IsAvailable    { get; }   // API キー未設定などで使えないなら false
    string UnavailableReason { get; }
    bool   CanImport(string input);          // 押す前の判定(軽く)
    CatalogImportResult Import(string input); // 例外を投げない
}
```

### 決めた約束

| 約束 | 理由 |
| --- | --- |
| **例外を投げない** | 窓は取り込み元ごとの事情を知らない。失敗も `CatalogImportResult` に入れて返す |
| **ID を一意にしなくてよい** | 重なりは `CatalogDraft` がずらす |
| **混ぜ方を決めない** | 足す / 上書き / 入れ替えは使う人が窓で選ぶ |
| **入力は文字列 1 本** | URL・ファイルパス・貼り付けたテキストはすべてこれに収まる |

### 登録が要らない

`CatalogImporterRegistry` が **読み込み済みの全アセンブリから実装を探します**。
Phase6-2 で `YouTubeCatalogImporter` を 1 つ書けば、次のコンパイルでタブに出ます。
**窓のコードは 1 行も変わりません。**

属性やリストで登録する形にしなかったのは、**登録し忘れが必ず起きる**からです。
Phase5-3 の「Prefab はできているのに中身が空」と同じ種類の事故で、
動かないのに理由が見えないのがいちばん困ります。

### 混ぜ方は 3 つだけ

| 値 | 動き |
| --- | --- |
| `MergeAppend` | そのまま足す。ID が重なればずらす |
| `MergeUpdate` | 同じ ID があれば上書き、無ければ足す |
| `MergeReplace` | いまの中身を捨てて入れ替え |

取り込み元が何本増えても、**混ぜ方はここ 1 か所**で済みます。

---

## 4. Catalog との接続方法

```
CatalogBuilderWindow
      │ 表示・入力だけ
      ▼
  CatalogDraft ──ToMediaItems()──► MediaItem[]
      │                                │
      │ CatalogDraftIO.Load            │ CatalogDraftIO.Save
      ▼                                ▼
              MediaCatalogAsset (ScriptableObject)
                        │
                        │ UdonCatalogBaker(既存・Phase1-2)
                        ▼
                 UdonMediaCatalog (VRCUrl 焼き込み済み)
```

- 書き込み口は `MediaCatalogAsset.SetEntries`。
  Phase4 から「Catalog Builder の書き込み口」として空けてあったものです
- **作りかけは再生側へ流れません。**
  `CatalogDraftItem.ToMediaItem()` が不完全なら `null` を返し、黙って落ちます
- **VRCUrl への焼き込みはまだ窓からは行いません。**
  今までどおり Catalog の Inspector から行います

### 依存の向き

```
CatalogBuilder ──► Catalog(MediaItem / MediaType)
CatalogBuilder ──► Catalog.Assets(MediaCatalogAsset)
CatalogBuilder ──✗─ World / World.VRChat        ← 参照しない
```

最後の行はテストで固定してあります(`TheDraftNeverTouchesTheRuntime`)。

---

## 5. Phase6-2 で追加する部分

| 追加 | 触る場所 |
| --- | --- |
| **YouTube 取り込み** | `ICatalogImporter` の実装を 1 つ。窓は無変更 |
| JSON / CSV 取り込み | 同上 |
| API キーの設定 | 実装側が `ScriptableObject` で持つ。`IsAvailable` で未設定を知らせる |
| 長さの自動取得 | 取り込み時に `DurationSeconds` を埋める |
| **サムネイル** | `CatalogDraftItem.ThumbnailPath` に枠だけ空けてある |
| **VRCUrlTable 生成** | `CatalogDraftIO` に焼き込みを 1 本足す(Phase6-3 想定) |
| 関連 ID の自動付け | `CatalogDraft` に「タグが近いものを結ぶ」を足す |

**土台側は変わらない見込み**です。取り込み元が増えても
窓・`CatalogDraft`・`CatalogDraftIO` は今の形のままです。

---

## 6. 検査

```bash
python3 tools/typecheck/genmeta.py
python3 tools/typecheck/typecheck.py    # No compile errors.
python3 tools/typecheck/runtests.py     # 914 passed, 0 failed
python3 tools/typecheck/udon_lint.py    # 18 ファイル / 問題なし
```

Phase5-5 の 894 に `CatalogDraftTests` の 20 ケースを足して **914** です。
