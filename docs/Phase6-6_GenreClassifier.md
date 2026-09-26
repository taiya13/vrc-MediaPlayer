# Phase6-6 — Smart Genre Classifier

YouTube のカテゴリは **「音楽」しか返しません**。
曲を 200 本入れると **200 本すべてが「音楽」** になり、絞り込みにも「おすすめ」にも使えません。
Phase6-6 は、タイトル・説明・タグ・チャンネル名から**こちらでジャンルを決めます**。

---

## 1. 判定アルゴリズム

### 流れ

```
GenreSignals                     GenreDictionary
 ├ Title          ×3  ─┐          ├ GenreRule "Vocaloid"
 ├ Tags           ×3  ─┤            └ GenreKeyword "初音ミク"(5)
 ├ ChannelName    ×2  ─┼─→ 点取り ─→ GenreScore[] ─→ 一番高いもの
 ├ Description    ×1  ─┘                  ↑
 └ SourceCategory ─────── 後押しだけ ──────┘
```

1. 4 つの欄をそれぞれ**小文字にそろえる**
2. 辞書の言葉が入っていたら **`欄の重み × 言葉の重み`** を足す
3. 取り込み元のカテゴリで、**すでに点が入っているジャンルだけ**を少し押す
4. 外の手がかり（将来の MusicBrainz）があれば足す
5. いちばん高いジャンルを返す。`MinimumScore`（6 点）に届かなければ **「その他」**

### 欄の重み

| 欄 | 重み | なぜ |
|---|---|---|
| 見出し | **3** | いちばん素直に書いてある |
| タグ | **3** | 付ける人が意図して選んでいる |
| チャンネル名 | **2** | 曲ごとには変わらない |
| 説明 | **1** | 長くて関係ない言葉も多い |

`MinimumScore = 6` の意味：
説明に 1 語かすっただけ（1×2 = 2 点）では決めない。見出しに 1 語（3×2 = 6 点）なら決める。

### 言葉の重み

| 重み | 目安 | 例 |
|---|---|---|
| 5 | これが出たらほぼ確定 | `vocaloid` `dnb` `two steps from hell` |
| 4 | かなり強い | `cover` `live` `piano` `ado` |
| 3 | 強いが単独では危うい | `chill` `punk` `swing` |
| 2 | 添えるだけ | `edit` `score` `fusion` `band` |

### 決めたことが 3 つあります

**① 同じ言葉は 1 回しか数えません。**
回数で数えると、説明欄に同じ単語を並べただけの動画が上位に来ます。
**「あるか無いか」だけ**を見るので、結果が読みやすく、テストもしやすくなります。

**② 短い英単語は「前後が英数字でないこと」を見ます。**
これが無いと誤爆します。

| 誤爆 | 何に当たるか |
|---|---|
| `cover` | dis**cover**y |
| `live` | de**live**ry / o**live** |
| `rock` | **rock**et |
| `epic` | **epic**enter |
| `trance` | en**trance** |

**日本語には効かせません。** 「ロック」の前後に区切りは無いのが普通で、
区切りを求めるとほとんど当たらなくなります。
英数字だけの言葉なら自動で有効、そうでなければ無効です（`GenreKeyword.IsAsciiWord`）。

**③ 同点なら辞書の並び順が勝ちます。**
辞書は**細かいジャンルを先、大きいジャンルを後**に置いてあります。

```
Vocaloid → Anime → Game Music → Soundtrack → Epic → Orchestra → Classical
→ Piano → Jazz → Lo-fi → Hardcore → Drum & Bass → Dubstep → Trance
→ House → EDM → Metal → Rock → K-POP → J-POP
→ Remix → Cover → Live          （修飾ジャンルは最後）
```

「EDM House Mix」が **House** に、「Rock Metal Mix」が **Metal** になるのはこのためです。

### カテゴリは「補助」だけ

**カテゴリだけではジャンルになりません。**
`ApplyCategoryBias` は **すでに点が入っているジャンルにしか**足しません。

| カテゴリ | 押すもの | 点 |
|---|---|---|
| ゲーム | Game Music | +3 |
| 映画・アニメ | Anime / Soundtrack | +3 / +2 |
| **音楽** | **なし** | — |

「音楽」に後押しを付けていないのは、**ほぼ全部の曲に付くから**です。
「ゲーム」カテゴリの動画が全部 Game Music になることもありません。

数字（`categoryId`）は判定側に入りません。
YouTube 側が `YouTubeCategoryNames` で**言葉に直してから**渡します。

---

## 2. ジャンル辞書の構成

```
GenreDictionary
 ├ Rules: GenreRule[]              ← ジャンル 1 つに 1 個
 │    ├ Genre  "Vocaloid"
 │    └ Keywords: GenreKeyword[]
 │         ├ Text     "初音ミク"
 │         ├ Weight   5
 │         └ WholeWord false（日本語なので区切りを見ない）
 └ Biases: CategoryBias[]          ← カテゴリの後押し
```

### あとから足す

**判定の仕組みには手を触れません。**

```csharp
// キーワードを足す
dictionary.Rule(GenreDictionary.Orchestra).Add(5, "すこやか楽団", "○○フィル");

// ジャンルごと足す
dictionary.Rule("Reggae").Add(5, "reggae", "レゲエ", "ska");

// 短い英単語で区切りを自分で決める
dictionary.Rule(GenreDictionary.Live).AddRaw("live", 4, true);

// 消す
dictionary.Rule(GenreDictionary.Live).Remove("live");
```

同じ言葉を 2 度入れても**点は二重に入りません**（強いほうが残ります）。

### 外部 DB への拡張

```csharp
public interface IGenreHintProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    GenreHint[] Suggest(GenreSignals signals);   // (ジャンル, 点, 理由)
}

classifier.AddHintProvider(new MusicBrainzHints());
```

- 点の入れ方は辞書と同じ（足し合わせて一番高いものを採る）
- **辞書に無いジャンルも足せます**（外部のほうが詳しいことがあるため）
- **`IsAvailable` が false なら黙って飛ばします** — 外部が落ちていても辞書だけで動きます
- 辞書側は通信も Unity も知らないまま

---

## 3. 追加されたキーワード数

**23 ジャンル / 528 語**

| ジャンル | 語数の目安 | 中身 |
|---|---|---|
| Vocaloid | 約 40 | ボカロ / 初音ミク / SynthV / CeVIO / ボカロP |
| Anime | 約 24 | アニソン / NCOP / 主題歌 / 声優 |
| Game Music | 約 38 | ゲーム音楽 / chiptune / 原神 / Zelda / 東方 |
| Soundtrack | 約 22 | OST / 劇伴 / 澤野弘之 / 久石譲 / Hans Zimmer |
| Epic | 約 16 | Two Steps From Hell / trailer music / cinematic |
| Orchestra | 約 15 | オーケストラ / 管弦楽 / philharmonic / 吹奏楽 |
| Classical | 約 42 | 作曲家 22 名 + 交響曲 / 協奏曲 / ノクターン |
| Piano | 約 13 | ピアノソロ / 連弾 / pianist |
| Jazz | 約 22 | ジャズ / bebop / bossa nova / ragtime |
| Lo-fi | 約 20 | lofi / chillhop / 作業用BGM / 睡眠用 |
| Hardcore | 約 18 | gabber / speedcore / J-core / Camellia |
| Drum & Bass | 約 12 | dnb / neurofunk / breakcore |
| Dubstep | 約 11 | riddim / brostep / Skrillex |
| Trance | 約 11 | psytrance / uplifting / Armin van Buuren |
| House | 約 14 | deep / tech / tropical / Daft Punk |
| EDM | 約 26 | Alan Walker / Avicii / future bass |
| Metal | 約 24 | death / black / djent / BABYMETAL |
| Rock | 約 32 | visual kei / ONE OK ROCK / Linkin Park |
| K-POP | 約 25 | BTS / BLACKPINK / NewJeans |
| J-POP | 約 42 | YOASOBI / Ado / 米津玄師 / 藤井風 |
| Remix | 約 14 | bootleg / mashup / VIP mix |
| Cover | 約 15 | 歌ってみた / 弾いてみた / 耳コピ |
| Live | 約 17 | ライブ映像 / 武道館 / 一発撮り |

語数は**テストで固定しています**（`Assert.AreEqual(528, dictionary.KeywordCount)`）。
足したらこの数も直すことになるので、**数え漏れが起きません**。

---

## 4. 判定例（すべてテスト済み）

| 入力 | 結果 |
|---|---|
| YOASOBI - アイドル | **J-POP** |
| Ado - 唱 | **J-POP** |
| Alan Walker - Faded | **EDM** |
| Camellia - Exit This Earth's Atomosphere | **Hardcore** |
| Hatsune Miku - Senbonzakura | **Vocaloid** |
| 澤野弘之 - Before My Body Is Dry | **Soundtrack** |
| Two Steps From Hell - Victory | **Epic** |
| Official Live | **Live** |
| Piano Cover | **Piano** |
| Daft Punk - One More Time | **House**（`punk` に引っぱられない） |
| Discovery Channel Theme | Cover に**ならない** |
| aaaa bbbb cccc | **その他** |

---

## 5. Importer 側

`YouTubeVideoInfo.ToDraftItem()` は**材料を詰めて呼ぶだけ**です。

```csharp
item.Genre = GenreClassifier.Shared.Classify(ToGenreSignals());
```

```csharp
public GenreSignals ToGenreSignals()
{
    signals.Title          = Title;
    signals.ChannelName    = ChannelTitle;
    signals.Description    = Description;
    signals.Tags           = Tags;
    signals.SourceCategory = YouTubeCategoryNames.Of(CategoryId);   // 番号 → 言葉
    return signals;
}
```

判定の中身は 1 行もありません。**JSON / CSV Importer も同じ 2 行**で済みます。

---

## 6. すでにあるカタログへの適用

「まとめて直す」タブに **「ジャンルを自動で決める」** を追加しました。

```
ジャンルを自動で決める
[空いているものだけ] [全部付け直す]
例: メルト → Vocaloid 15 点 — 見出し:初音ミク
```

- **「空いているものだけ」** …… すでに入っているものは触りません
- **「全部付け直す」** …… 確認を挟んで上書き（Phase6-4 までの「音楽」を直すのはこちら）
- **判定できなかったものに「その他」を書き込みません。**
  もとのジャンルが消えてしまうためです
- **1 件目の判定理由**を出します。なぜそうなったかが見えないと直しようがありません

---

## 7. 窓が小さいと下が切れる問題

Phase6-5 までは真ん中の 2 枚が縮まず、**下のボタンが画面の外へ出ていました**。

| | |
|---|---|
| 上のバー・絞り込み | **いつも上に固定** |
| 真ん中の 2 枚 | **残った高さいっぱい**に伸び縮み |
| ③ VRCUrl へ焼く | **いつも下に固定** |
| それでも足りないくらい小さいとき | **外側がまとめてスクロール** |

- `minSize` を **900×600 → 420×280**
- 左の一覧の幅を **固定 400px → 窓幅の 42%**（200〜460px）
- セットアップの窓も `minSize` を 420×480 → **360×240** に

---

## 8. 変更したクラス

### 新規（すべて Unity 非依存 = EditMode で検証可能）

| クラス | 役割 |
|---|---|
| `GenreKeyword` / `GenreRule` | 言葉と重み / ジャンル 1 つぶんの決まり |
| `GenreDictionary` | 辞書本体（23 ジャンル・528 語）＋ カテゴリの後押し |
| `GenreSignals` / `GenreScore` | 判定の材料 / 点 |
| `IGenreHintProvider` / `GenreHint` | 外部 DB を足す口（実装はまだ無し） |
| `GenreClassifier` | 点取りと順位付け |

### 変更

| クラス | 何を |
|---|---|
| `YouTubeVideoInfo` | `ToGenreSignals()` を追加。ジャンルは分類器に任せる |
| `CatalogBulkEdit` | `ClassifyGenres` を追加 |
| `CatalogBuilderWindow` | 自動判定ボタン / 縮めても収まるレイアウト |
| `SmartMediaPlatformSetupWindow` | `minSize` を小さく |

**責務は 1 つも動かしていません。MediaPlayer 本体は 1 行も変えていません。**

---

## 9. テスト方法

### PC 上（Unity 不要）

```bash
python3 tools/typecheck/genmeta.py
python3 tools/typecheck/typecheck.py    # No compile errors.
python3 tools/typecheck/runtests.py     # 1065 passed, 0 failed
python3 tools/typecheck/udon_lint.py    # Udon で書けない書き方はありません。
```

Phase6-5 の 1029 から **+36**（判定例 3・「音楽」を返さない 3・カテゴリ 2・区切り 3・
欄の重み 5・同点 3・全ジャンル 1・辞書を育てる 5・外部手がかり 4・理由 2・
まとめて判定 3・辞書の大きさ 1・YouTube 側 1）。

**`GenreClassifierTests` は辞書の回帰試験です。**
キーワードを足したあとにここを通せば、**足したせいで別の曲が別ジャンルへ動いていないか**が
すぐ分かります。

### Unity 上

**A. 新しく取り込む**

| # | やること | 期待 |
|---|---|---|
| 1 | ボカロ曲の多いチャンネルを取り込む | 「② 追加」後、ジャンルが **Vocaloid** |
| 2 | ゲーム音楽チャンネル | **Game Music** |
| 3 | 一覧の「ジャンル」で絞る | **「音楽」が 1 つも無い** |
| 4 | 判定できない曲 | **「その他」**（空でも「音楽」でもない） |

**B. すでにあるカタログを直す**（Phase6-4 で入れたぶん）

| # | やること | 期待 |
|---|---|---|
| 5 | 「表示中を全部チェック」 | ☑ の数が出る |
| 6 | 「まとめて直す」→「全部付け直す」 | 確認が出る → 「N 件に反映しました」 |
| 7 | ジャンルのプルダウン | **「音楽」が消え、細かいジャンルが並ぶ** |
| 8 | 手でジャンルを書いた 1 件を作る →「空いているものだけ」 | **書いたものが残る** |
| 9 | 判定理由の行 | 「Vocaloid 15 点 — 見出し:初音ミク」のように出る |

**C. 窓の大きさ**

| # | やること | 期待 |
|---|---|---|
| 10 | Catalog Builder を**画面の 1/4 くらいまで**縮める | **③ のボタンが見えたまま** |
| 11 | さらに縦を潰す | 全体がスクロールして**下まで届く** |
| 12 | 横を細くする | 左の一覧が細くなり、右のタブが潰れない |

**D. 誤爆の確認**（手で 1 件足して見出しを打つだけ）

| 見出し | 期待 |
|---|---|
| `Discovery Channel Theme` | Cover に**ならない** |
| `Rocket Launch` | Rock に**ならない** |
| `Entrance Hall` | Trance に**ならない** |
| `EDM House Mix` | **House** |

---

## 10. 判定精度を上げるために今後できること

**辞書を触るだけでできるもの**

1. **キーワードを足す** — いちばん効きます。誤判定を見つけたら
   `GenreDictionary.CreateDefault()` に 1 行足して `runtests.py` を通すだけです
2. **アーティスト名を増やす** — いま約 120 名。ここが増えるほど当たります
3. **除外語** — いまは「足す」しかありません。
   「この語があったらこのジャンルではない」（例：`karaoke` があれば Cover でない）を
   負の重みで持てるようにすると、修飾ジャンルの誤爆が減ります

**仕組みを足すもの**

4. **チャンネル単位の学習** — 同じチャンネルの曲は同じジャンルになりやすい。
   「このチャンネルは 8 割 Vocaloid」を覚えて後押しにすれば、
   タイトルに手がかりが無い曲も当たります。**カタログの中身だけで作れます**
5. **2 位をタグに入れる** — `Rank()` はすでに全部返しています。
   「Piano Cover」を Piano にしてタグへ `Cover` を入れれば、両方で絞れます
6. **複合ジャンル** — 修飾（Remix / Cover / Live）を**ジャンルではなくタグ**として
   別枠にすると、「YOASOBI のライブ」が J-POP + Live になります。
   いまは片方しか選べません
7. **MusicBrainz 連携** — 口（`IGenreHintProvider`）は空けてあります。
   アーティスト名で引いて公式のジャンルを取れば、辞書に無い曲も当たります。
   API 制限があるので、**取り込み時に 1 回だけ引いて覚える**設計が要ります
8. **説明欄の絞り込み** — いまは説明を丸ごと見ています。
   概要欄には他の曲の宣伝や定型文が入りがちなので、
   **先頭 200 文字だけ**にすると誤爆が減るはずです
9. **判定結果の記録** — 「自動で付けたのか、手で直したのか」を覚えておけば、
   辞書を育てたときに**自動判定ぶんだけ付け直す**ことができます
   （いまは全部か、空いているものだけかの 2 択です）
