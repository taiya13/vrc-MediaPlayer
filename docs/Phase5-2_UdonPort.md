# Phase5-2 — 制御層の UdonSharp 移植

**目的**: `SmartMediaPlayer.prefab` を VRChat ワールドへ配置し、
**ドラッグして Build & Test するだけで動く**状態にすること。

---

## 0. Phase5-1 で何が足りなかったか

Phase5-1 の Prefab はエディタ / ClientSim では動きましたが、
**アップロードしたワールドでは制御が動きません**でした。

VRChat のワールドで実行されるのは **Udon だけ**です。
独自の `MonoBehaviour` はアップロード時に取り除かれ、`Start` も `Update` も呼ばれません。
Phase5-1 の `SmartMediaPlayerRoot` / `MediaController` / `MediaPlayerUI` は
すべて `MonoBehaviour` だったので、ワールドでは Prefab が置かれるだけの状態でした。

Phase5-2 はここを埋めます。

---

## 1. Udon 化したクラス一覧

`Assets/SmartMediaPlatform/World/Udon/`(9 クラス・すべて `UdonSharpBehaviour`)

| クラス | Phase5-1 までの対応物 | 責務 |
| --- | --- | --- |
| `UdonSmartMediaPlayer` | `SmartMediaPlayerRoot` | 部品の配線と最初の 1 本。**判断は持たない** |
| `UdonPlayerSession` | `PlayerSession` + `MediaQueue` + `LibraryPlaybackBridge` | 次に何を再生するか / Queue を保つ |
| `UdonVideoBackend` | `VideoBackend` + `VRChatVideoBackend` + `VideoBackendAdapter` | 実際に鳴らす。**URL を知る唯一の場所** |
| `UdonCatalogStore` | `CatalogStore` | 表示用データの唯一の窓口。**URL を出さない** |
| `UdonMediaScreen` | `MediaScreen` | 映像と音の出力先 |
| `UdonMediaController` | `MediaController` | 操作の窓口(引数なしイベント) |
| `UdonMediaPlayerUI` | `MediaPlayerUI` | 画面(`Text` へ書くだけ) |
| `UdonMediaControlButton` | (新規) | 「使う」で押せる操作ボタン |
| `UdonMediaRowButton` | (新規) | 「何番目か」を持つ行ボタン |

既存の Udon クラスはそのまま使います(**変更ゼロ**)。

| クラス | 追加時期 |
| --- | --- |
| `UdonMediaCatalog` | Phase1-2 |
| `UdonRecommendationEngine` | Phase1-3 |
| `UdonVRCVideoEventRelay` | Phase3-2(C# 経路用。実機経路では `UdonVideoBackend` が直接受ける) |

あわせて、Udon へ写す前の**正典**を 1 つ追加しています。

| クラス | 場所 | 役割 |
| --- | --- | --- |
| `PlaybackModel` | `World/UdonModel/` | `UdonPlayerSession` のアルゴリズムを純粋 C# で書いたもの。EditMode 37 ケースで検証 |

---

## 2. 変更したアーキテクチャ

### 2-1. 変えていないこと(こちらが本題)

**責務の線は 1 本も動かしていません。**

```
Catalog ──> CatalogStore ──> UI            表示用データ(URL 無し)
   │
   └──────> VideoBackend                   URL を知るのはここだけ
                 ▲
Recommendation ──┼── PlayerSession ──> Queue の保ち方・次の 1 本
                 │        ▲
              Controller ─┘             操作の窓口
```

- **URL を知ってよいのは `UdonVideoBackend` だけ**。
  `UdonCatalogStore` には `GetUrl` がありません。UI は URL に触れません。
- **再生の判断は `UdonPlayerSession` だけ**。
  `UdonSmartMediaPlayer` に `Next` / `Play` / `Stop` は生やしていません。
- **Queue の不変条件は Phase3 のまま**。先頭がいま鳴っているもので、
  先頭は動かせず消せません。
- **`VRCUrl` は実行時に作らない**。編集時に `UdonMediaCatalog` へ焼き込んだものを引くだけです。

### 2-2. 変えたこと

| | Phase5-1(C#) | Phase5-2(Udon) | なぜ |
| --- | --- | --- | --- |
| 部品の見つけ方 | `GetComponentInChildren<IMediaScreen>()` | Inspector の参照 | Udon に interface が無い |
| メディアの受け渡し | `MediaItem` / `DisplayMeta` | **catalog index(int)** | Udon はカスタムクラスを持ち回れない |
| Queue | `List<QueueEntry>` | 固定長 `int[]` + 件数 | Udon に `List` が無い |
| 失敗の伝え方 | `bool` 戻り値 | `bool` 戻り値(同じ) | Phase1-2 から Udon を見据えて例外を使っていない |
| イベント | 明示的な Observer インターフェース | `UdonVideoBackend` が直接 `Session` を呼ぶ | Udon に interface が無い。Phase1-2 から `event` を避けていたので影響は局所 |
| 画面 | `OnGUI`(IMGUI) | uGUI の `Text` へ書く | `OnGUI` はワールドで動かない |
| 操作の受け取り | `Button.onClick` 前提 | **Collider + `Interact()`** | ワールドでいちばん確実に押せる形 |

### 2-3. 「Udon へ写す前に純粋 C# で検証する」方式

`UdonPlayerSession` の中身は `PlaybackModel`(`World/UdonModel/`)の写しです。

```
PlaybackModel        純粋 C# / EditMode 37 ケース   ← ここでアルゴリズムを確かめる
      │  1 対 1 で書き写す
      ▼
UdonPlayerSession    UdonSharpBehaviour / 実機      ← 動くのはこちら
```

Phase1-2(`ParallelMediaCatalog` → `UdonMediaCatalog`)、
Phase1-3(`RecommendationEngine` → `UdonRecommendationEngine`)と同じ手順です。
UdonSharp はこの環境でコンパイルできず、EditMode テストからも触れないため、
**検証できる形で書いてから写す**ようにしています。

> **アルゴリズムを変えるときは必ず両方を直してください。**
> `PlaybackModel` だけ直しても実機は変わりません。

### 2-4. C# 版は残してあります

Phase5-1 までの `MonoBehaviour` 版(`World/Runtime/`)は**そのまま**です。

| | 使いどころ |
| --- | --- |
| `SmartMediaPlayer_NoSDK.prefab`(C#) | SDK 無しで仕組みを確かめる。Console だけ |
| `SmartMediaPlayer.prefab`(Udon) | **ワールドに置く。実際に動く** |

C# 版のテスト(EditMode 895+ ケース)がそのまま設計の根拠として残ります。

---

## 3. Prefab の変更点

### 3-1. 作り方

```
Tools > Smart Media Platform >
    Create SmartMediaPlayer Prefab (VRChat 実機・Udon)              ← AVPro(既定)
    Create SmartMediaPlayer Prefab (VRChat 実機・Udon / Unity 版)   ← エディタで絵を見たいとき
```

`Assets/SmartMediaPlatform/World/Prefabs/SmartMediaPlayer.prefab` ができます。

Phase5-1 と同じ理由で **Prefab はリポジトリに同梱しません**。
UdonSharp のプログラム(`.asset`)と `UdonBehaviour` は SDK のバージョンで中身が変わるため、
固定の Prefab を持つと「SDK を更新したら program asset が null」になります
(Phase3-3 で実際に踏んだ問題)。

### 3-2. 構造

```
SmartMediaPlayer              UdonSmartMediaPlayer     配線だけ
├── Screen                    UdonMediaScreen          ← 差し替え可能
│   └── Surface               Quad + AudioSource
├── Player                    VRCAVProVideoPlayer      ← AVPro / Unity 版を差し替え可能
│                             UdonVideoBackend         ★ プレイヤーと同じ GameObject
├── Catalog                   UdonMediaCatalog         ← Catalog Builder の焼き込み先
│                             UdonCatalogStore
│                             UdonRecommendationEngine
├── Session                   UdonPlayerSession
├── Controller                UdonMediaController      ← 差し替え可能
├── UI                        UdonMediaPlayerUI        ← 差し替え可能
│   ├── Canvas                World Space + Text 20 行
│   └── RowButtons            UdonMediaRowButton × 8(一覧の行)
└── Buttons                   UdonMediaControlButton × 4(再生 / 次へ / 前へ / 停止)
```

**★ が Phase5-2 でいちばん重要な配置です。**
VRChat の動画イベント(`OnVideoEnd` / `OnVideoError`)は
**動画プレイヤーと同じ GameObject の UdonBehaviour** にしか届きません。
`UdonVideoBackend` が `Player` に同居しているのはそのためです。

### 3-3. Phase5-1 の Prefab との違い

| | Phase5-1 | Phase5-2 |
| --- | --- | --- |
| 制御 | `MonoBehaviour` | `UdonSharpBehaviour` |
| ワールドで動くか | **動かない** | 動く |
| カタログ | 実行時に `MediaCatalog` を作る | **編集時に `UdonMediaCatalog` へ焼き込む** |
| 画面 | `OnGUI`(画面の左半分) | ワールド内 Canvas(World Space) |
| 操作 | 画面のボタン | Collider を「使う」 |
| Inspector の必須設定 | なし | なし(Builder が全部つなぐ) |

---

## 4. 交換性

| 替えたいもの | やること | 影響 |
| --- | --- | --- |
| **Screen** | `UdonSmartMediaPlayer.Screen` に別の `UdonMediaScreen` を差す | なし |
| **AVPro / Unity 版** | `UdonVideoBackend.Player` に別のプレイヤーを差す | なし(共通基底しか触っていない) |
| **Controller** | `Controller` 欄を差し替える。同じイベント名(`Next` など)に応えればよい | ボタンはそのまま |
| **UI** | `Ui` 欄を差し替える。`Refresh()` に応えればよい | 制御側は変わらない |
| **Catalog** | `UdonMediaCatalog` に焼き込み直す(Catalog Builder) | **コード変更ゼロ** |
| **関連の出どころ** | 焼き込む `RelatedIds` を差し替える | **コード変更ゼロ**(Phase4-2 のまま) |

`UdonMediaControlButton` は `Controller` の**イベント名**しか知りません。
`UdonMediaPlayerUI` は `Session` と `Store` からしか読みません。
どちらも具体的な処理を知らないので、窓口ごと差し替えても壊れません。

---

## 5. VRChat で確認すべきテスト項目

### 5-1. 準備

1. `Tools > Smart Media Platform > Create SmartMediaPlayer Prefab (VRChat 実機・Udon)`
2. Console に「Prefab を作成しました」と出るのを確認
   (「Recompile が必要」と出たら、コンパイル完了後にもう一度メニューを実行)
3. `SmartMediaPlayer.prefab` を Hierarchy へドラッグ
4. `Screen/Surface` を見える位置へ動かす
5. **URL を実在するものに差し替える**
   `Catalog` の `UdonMediaCatalog` を選び、Inspector の `Urls` を編集
   (同梱カタログの URL は架空のアドレスです。`VRCUrl` は実行時に作れないため、
   ここで入れたものだけが再生できます)
6. `VRChat SDK > Build & Test`

### 5-2. 確認する項目

| # | 見るところ | 期待 |
| --- | --- | --- |
| 1 | ワールドに入って 1〜2 秒 | 1 本目が自動で流れ始める |
| 2 | Screen/Surface | 映像が出ている |
| 3 | 音 | Surface に近づくと大きくなる(3D) |
| 4 | Canvas の一番上 | `▶ タイトル` と経過時間が更新される |
| 5 | Canvas の `Library` | 一覧が 8 行出ている |
| 6 | Canvas の `Queue` | 先頭に `♪`(いま鳴っているもの)、その下に次以降 |
| 7 | Canvas の `関連` | いま鳴っているものの関連が出ている |
| 8 | `Buttons/Next` を「使う」 | 次へ進む。Queue の先頭が入れ替わる |
| 9 | `Buttons/Previous` を「使う」 | 前へ戻る |
| 10 | `Buttons/TogglePlayPause` を「使う」 | 止まる / 再開する。表示が `▶` ↔ `‖` |
| 11 | `Buttons/Stop` を「使う」 | 止まる |
| 12 | `UI/RowButtons/PlayRow2` を「使う」 | 一覧の 3 番目へ**割り込んで**切り替わる。Queue の残りは消えない |
| 13 | 最後まで再生する | 自動で次へ進む(`OnVideoEnd`) |
| 14 | 壊れた URL を 1 本混ぜる | そこで止まらず次へ飛ぶ(`OnVideoError`) |
| 15 | Queue が 2 本を切る | おすすめで自動補充されて 5 本に戻る |
| 16 | 2 人で入る | それぞれのクライアントで再生される(**同期は未実装**。§6 参照) |

### 5-3. エディタ側の確認

```
Window > General > Test Runner > EditMode > Run All
```

- `SmartMediaPlatform.World.UdonModel.Tests` … **37 ケース**(Phase5-2 で追加)
- 既存のテストは全部そのまま通ります(Phase1〜5-1 のクラスは差分ゼロ)

```
python3 tools/typecheck/typecheck.py    # 全アセンブリの型検査(Unity 不要)
python3 tools/typecheck/udon_lint.py    # Udon で書けない書き方の検出
```

---

## 6. 今後残る課題

### 6-1. 同期していない(いちばん大きい)

いまの実装は **`BehaviourSyncMode.None`**、つまり**各自のクライアントで別々に再生**します。
「みんなで同じものを同じ位置で観る」にはネットワーク同期が要ります。

必要になるもの:

- `UdonPlayerSession` を `Manual` 同期にして、Queue と再生位置を `[UdonSynced]` で配る
- オーナー(Master)だけが `Next` / `PlayAt` を実行し、他は `SendCustomNetworkEvent` で頼む
- 後から入ってきた人へ再生位置を合わせる(`Networking.GetServerTimeInSeconds()` 基準)
- 誰が操作してよいかの権限(全員 / Master のみ)

これは Phase5-3 相当の分量です。同期を入れると
「誰の判断で次へ進むか」が変わるので、`UdonPlayerSession` の設計に踏み込みます。

### 6-2. UI がまだ素朴

- `Text` を並べただけで、スクロールバーもサムネイルもありません
- 一覧のページ送り(`ScrollLibraryUp` / `ScrollLibraryDown`)はメソッドだけあって、
  Prefab にボタンを置いていません
- 選択(`Select`)と再生(`PlayVisible`)を別々に押せる形にしていません
  (いまは行を押すと即再生)
- ワールド内 uGUI の `Button` ではなく Collider + `Interact` で押しています。
  確実ですが、押した感(ハイライト)がありません

### 6-3. 検証できていないところ

- **UdonSharp のコンパイル自体は未検証**です。この環境に VRChat SDK が無いため、
  `tools/typecheck/udon_lint.py` で「Udon で書けない書き方」を機械的に排除してあるだけです。
  U# のバージョン差で弾かれる書き方が残っている可能性はゼロではありません。
- **Prefab の生成結果も未検証**です(SDK が無いと動かせないメニューのため)。
  Phase3-3 と同じ理由で「生成する」方式にしているので、
  実行して Console を見ていただくのがいちばん早い確認になります。
- AVPro のスクリーン/スピーカー配線(`VRCAVProVideoScreen` など)は
  `VRChatVideoPlayerFactory` が名前で探して繋いでいます。SDK 側の名前が変わると
  Console に「配線できませんでした」と出ます(壊れはしません)。

### 6-4. C# 版と Udon 版の二重管理

`PlaybackModel` ↔ `UdonPlayerSession` は手で写しています。
片方だけ直すとずれます。いまは

- ドキュメント(このファイルと両クラスのコメント)
- `PlaybackModel` 側のテスト

で守っていますが、**ずれを機械的に検出する仕組みはありません**。
クラスが増えるなら、C# から U# を生成する方向を考える時期です。

### 6-5. カタログの焼き込みが手作業寄り

Prefab を作るときに同梱サンプル(動画 10 本)を焼き込みます。
差し替えは `UdonMediaCatalog` の Inspector を直接編集する形で、
Catalog Builder(`Catalog.asset` → `UdonMediaCatalog`)からの
ワンボタン焼き込みは `Tools > Smart Media Platform > Bake Dummy Catalog Into Selected`
(ダミー専用)しかありません。`Catalog.asset` を受け取る版が要ります。
