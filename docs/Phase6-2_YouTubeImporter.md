# Phase6-2 — YouTube Importer

**目的**: Catalog Builder へ YouTube 取り込みを足すこと。
**Catalog へは書きません。**取れたかどうかを確かめるところまでです。

---

## 1. Importer 構成

```
CatalogBuilder/Importers/YouTube/
├── YouTubeUrlParser.cs        貼られた文字列が何を指すか見分ける  ← 通信しない
├── YouTubeDurationParser.cs   PT4M13S → 253 秒                    ← 通信しない
├── YouTubeVideoInfo.cs        取れた 1 本(YouTube の言葉のまま)
├── IYouTubeClient.cs          取得の口 + YouTubeFetchResult
├── YouTubeDataApiClient.cs    Data API v3 の実装(通信はここだけ)
├── YouTubeApiSettings.cs      API キーの置き場(ScriptableObject)
├── YouTubeCatalogImporter.cs  ICatalogImporter の実装
├── Editor/
│   └── YouTubeImportPreviewWindow.cs   見るだけの窓
└── Tests/EditMode/
    └── YouTubeImporterTests.cs         23 ケース(通信しない)
```

### 取得を分けた理由

`YouTubeCatalogImporter` は **通信しません**。やるのは
「入力を見分ける」「取れたものを並べ直す」の 2 つだけです。

```
YouTubeCatalogImporter ──► IYouTubeClient ──► YouTubeDataApiClient ──► HTTP
      (見分ける・並べ直す)      (口)              (通信はここだけ)
```

- Data API が変わっても、直すのは実装 1 つ
- キー無しの取得方法に替えても Importer は無変更
- **偽物を差せばテストできる**(実際 23 ケースすべて通信していません)

### 触っていないもの

`CatalogBuilderWindow` / `CatalogDraft` / `CatalogDraftIO` / MediaPlayer 本体は
**1 行も変更していません**。プレビューは別の窓として足しました。

---

## 2. 取得フロー

```
貼られた文字列
   │
   ▼ YouTubeUrlParser.Parse         ← 通信しない。ここで弾けば無駄打ちしない
   ├ 再生リスト PL…  ──┐
   ├ チャンネル UC…   ─┤
   ├ チャンネル @名前 ─┤
   └ 動画 xxxxxxxxxxx ─┘
                        ▼
              IYouTubeClient
                        │
   チャンネルの場合  ── channels?part=contentDetails
                        │   → uploads 再生リスト ID を得る
                        ▼
              playlistItems?part=contentDetails   (50 件ずつ・ページ送り)
                        │   → 動画 ID の並び
                        ▼
              videos?part=snippet,contentDetails  (50 件ずつ)
                        │   → 見出し・チャンネル名・公開日・長さ・サムネイル
                        ▼
              YouTubeVideoInfo[]  →  プレビュー表示
                                  →  CatalogDraftItem[](Phase6-3 で使う)
```

**チャンネルに専用の口がない**ので、いったん「アップロード用の再生リスト」の ID を
引いてから、再生リストとして読みます。

**長さは `videos` で別に引きます。** `playlistItems` の応答には入っていません。

### 取れなかったものの扱い

非公開・削除済み・地域制限のものは **黙って消さず、印を付けて残します**。
「入れたはずの曲が無い」より「これは使えません」と出るほうが分かるためです。
Builder へ渡す側からは外れます。

### 失敗の返し方

**例外は投げません。**すべて `CatalogImportResult.Failure` で返します
(Phase6-1 で決めた約束)。HTTP のコードは直せる言葉に置き換えます。

| コード | 出す文言 |
| --- | --- |
| 403 | キーが無効か割り当て切れ。API が有効か、キーの制限が厳しすぎないか |
| 400 | 指定が正しくありません。URL / ID を確認 |
| 404 | 見つかりません。非公開か削除済みの可能性 |
| 0 | 通信できませんでした(ネットワーク・プロキシ) |

---

## 3. プレビュー UI

`Tools > Smart Media Platform > YouTube Import (プレビュー)`

```
┌──────────────────────────────────────────┐
│ API キー                                  │
│  [••••••••••••]  1 回の上限 [====100===] │
│  ⚠ 公開リポジトリへ入れないでください      │
├──────────────────────────────────────────┤
│ 取り込み元                                │
│  [https://youtube.com/@someone         ] │
│  判別: チャンネル @someone                │
│  [ 取得する(Catalog へは書きません) ]    │
├──────────────────────────────────────────┤
│ 取得結果 12 件 — チャンネル @someone      │
│ ┌────────────────────────────────────┐  │
│ │ 1. 曲のタイトル                      │  │
│ │   チャンネル  ○○ Channel            │  │
│ │   公開日      2024-05-01            │  │
│ │   再生時間    4:13 (253 秒)          │  │
│ │   動画 ID     dQw4w9WgXcQ           │  │
│ │   https://www.youtube.com/watch?v=… │  │
│ │   サムネイル  https://i.ytimg.com/… │  │
│ └────────────────────────────────────┘  │
│ 2. ⚠ (取得できません)  非公開・削除済み  │
└──────────────────────────────────────────┘
```

**Catalog Builder 本体とは別の窓**です。Phase6-1 の窓を 1 行も触らずに
取り込みを試せるようにするためで、
**取り込み元を足しても Builder 本体が壊れない**ことの確認も兼ねています。

貼った瞬間に「判別: 再生リスト PL…」と出るので、
**取得ボタンを押す前に間違いに気づけます**。

---

## 4. 取得できる項目

| 項目 | どこから | 備考 |
| --- | --- | --- |
| タイトル | `snippet.title` | |
| 動画 URL | 組み立て | `https://www.youtube.com/watch?v=<id>` |
| 動画 ID | `id` | そのまま `CatalogDraftItem.Id` になる(一意なので重複しない) |
| チャンネル名 | `snippet.channelTitle` | `Artist` に入る |
| チャンネル ID | `snippet.channelId` | |
| 公開日 | `snippet.publishedAt` | 表示は `2024-05-01` に短縮 |
| 再生時間 | `contentDetails.duration` | ISO 8601 → 秒。**生放送は 0**(長さ不明) |
| サムネイル URL | `snippet.thumbnails` | maxres → standard → high → medium → default の順で一番大きいもの |
| 説明 | `snippet.description` | 取るだけ。まだ使っていません |

---

## 5. API キーの設定方法

### 1) キーを作る

1. [Google Cloud Console](https://console.cloud.google.com/) でプロジェクトを作る
2. 「API とサービス」→ **YouTube Data API v3** を有効にする
3. 「認証情報」→「認証情報を作成」→ **API キー**

### 2) Unity へ入れる

`Tools > Smart Media Platform > YouTube Import (プレビュー)` を開き、
**「設定アセットを作る」** を押してからキーを貼ります。

保存先: `Assets/SmartMediaPlatform/CatalogBuilder/Importers/YouTube/YouTubeApiSettings.asset`

### 3) 気をつけること

> **このアセットを公開リポジトリへ入れないでください。**
> キーが漏れると他人に使われて割り当てを食い潰されます。
> `.gitignore` に追加するか、キーに制限を掛けてください。

- **ワールドには含まれません。** asmdef が `includePlatforms: Editor` なので、
  ビルドしたワールドにキーもコードも入りません
- **割り当ては 1 日 10,000 単位**が既定です。
  `playlistItems` と `videos` が 1 単位ずつなので、
  100 件取ると 4 単位ほど。ふつうに使うぶんには足りますが、
  `1 回の上限` で歯止めをかけてあります

---

## 6. Phase6-3 で追加する部分

| 追加 | 触る場所 |
| --- | --- |
| **Builder への反映** | プレビューの結果を `CatalogDraft.Merge` へ渡す。混ぜ方の 3 つはもうある |
| Catalog Builder 窓への統合 | `CatalogImporterRegistry` がすでに拾うので、窓側は無変更で出る |
| **VRCUrlTable 生成** | `CatalogDraftIO` に焼き込みを 1 本 |
| **サムネイルの取り込み** | URL は取れている。落として Texture にするのは Phase6-3 |
| 関連 ID の自動付け | 同じチャンネル・近い公開日で結ぶ |
| JSON / CSV 取り込み | `ICatalogImporter` をもう 1 つ実装するだけ |

**取得側は変わらない見込み**です。`IYouTubeClient` と
`YouTubeVideoInfo` の形は Phase6-3 でもそのまま使えます。

---

## 7. 検査

```bash
python3 tools/typecheck/genmeta.py
python3 tools/typecheck/typecheck.py    # No compile errors.
python3 tools/typecheck/runtests.py     # 937 passed, 0 failed
python3 tools/typecheck/udon_lint.py    # 18 ファイル / 問題なし
```

Phase6-1 の 914 に `YouTubeImporterTests` の 23 ケースを足して **937** です。
**23 ケースすべて通信しません**(`IYouTubeClient` に偽物を差しています)。
