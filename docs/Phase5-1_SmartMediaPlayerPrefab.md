# Phase5-1: SmartMediaPlayer Prefab(β)

**目的は「MediaPlayer をワールドに簡単に設置できる Prefab にすること」です。**
Phase4 までで再生フローは完成しているので、今回は**それを 1 つの Prefab にまとめ、
ドラッグするだけで動く**ようにします。

| 要件 | 実装 |
|---|---|
| `SmartMediaPlayer.prefab` を作る | `SmartMediaPlayer_NoSDK.prefab`(同梱)+ SDK 版はメニューで生成(§6-1) |
| ドラッグするだけで動く | 部品が無ければ代役を立てる。Inspector 設定は**必須ゼロ** |
| Screen / Player / Controller / UI を 1 つに | 5 つの子オブジェクトに分けて 1 Prefab |
| 参照は自動接続 | 子から**インターフェースで探す**。手動割り当て不要 |
| ロジックを Prefab に持たせない | 根っこは**組み立てだけ**。再生は既存の `PlaybackFlow` / `PlayerSession` |
| Screen / Controller を分離・交換できる | 5 つの契約(§2)。**子を差し替えるだけ** |
| Catalog Builder / バックエンド追加時も最小変更 | `ICatalogProvider` / `IMediaBackendProvider` の 2 点だけ |

---

## ⚠️ 最初に:β の範囲について

**このプレハブは Unity エディタ / ClientSim で動きます。
アップロードした VRChat ワールドでは、制御部分は動きません。**

理由は、`SmartMediaPlayerRoot` / `PlaybackFlow` / `PlayerSession` などが
**素の C# MonoBehaviour** だからです。VRChat のワールドで実行されるのは
Udon(UdonSharp)だけで、独自の MonoBehaviour は動きません。

いま Udon で動くのは、Phase1-2 / 3-2 で用意した次の 3 つだけです。

| クラス | 役割 |
|---|---|
| `UdonMediaCatalog` | カタログの並列配列版 |
| `UdonRecommendationEngine` | おすすめの Udon 版 |
| `UdonVRCVideoEventRelay` | 動画イベントの中継 |

**制御層の Udon 移植が Phase5-2 の最重要項目**です(§6-2)。
今回は「構造を固める β」と位置づけ、動作確認はエディタ / ClientSim で行います。

---

## 1. Prefab 構成

```
SmartMediaPlayer                    SmartMediaPlayerRoot   ← 組み立てだけ
├── Screen                          MediaScreen            ← 差し替え可能
│   └── Surface                     MeshRenderer + AudioSource(Quad)
├── Player                          IMediaBackendProvider  ← 差し替え可能
│                                     ・DummyMediaBackendProvider(ログのみ)
│                                     ・VRChatMediaBackendProvider(実機・SDK 版)
├── Controller                      MediaController        ← 差し替え可能
├── UI                              MediaPlayerUI          ← 差し替え可能
└── Catalog                         DefaultCatalogProvider ← 差し替え可能
```

**5 つの子はすべて独立して差し替えられます。**
根っこは子から**インターフェースで探す**だけなので、
子オブジェクトごと入れ替えても `SmartMediaPlayerRoot` も他の子も変わりません。

### 追加したクラス

| クラス | 場所(asmdef) | 役割 |
|---|---|---|
| **`SmartMediaPlayerRoot`** | `World`(SDK 不要) | 組み立てと配布だけ。**再生の判断は持たない** |
| `MediaPlayerContext` | `World` | 組み立て結果の入れ物。部品はこれだけを受け取る |
| `IMediaPlayerPart` | `World` | 部品の共通の口(`Bind`) |
| **`IMediaScreen`** / `MediaScreen` | `World` | 映像と音の出力先 |
| **`IMediaController`** / `MediaController` | `World` | 再生操作の窓口(`PlaybackFlow` への中継) |
| **`IMediaPlayerUI`** / `MediaPlayerUI` | `World` | 画面(IMGUI の β 版) |
| **`IMediaBackendProvider`** / `DummyMediaBackendProvider` | `World` | **バックエンド追加の差し込み口** |
| **`ICatalogProvider`** / `DefaultCatalogProvider` | `World` | **Catalog Builder の差し込み口** |
| `VRChatMediaBackendProvider` | `World.VRChat`(SDK 必須) | 実機の動画バックエンド |
| `SmartMediaPlayerPrefabBuilder` | `World/Editor` | SDK 版 Prefab の生成 |

**変更したクラスはありません。** Phase1〜4 はすべて差分ゼロです。

---

## 2. 差し替えの仕組み

`SmartMediaPlayerRoot.Build()` がやっているのはこれだけです。

```csharp
var catalogProvider = GetComponentInChildren<ICatalogProvider>(true);
var catalog = catalogProvider?.CreateCatalog() ?? DefaultCatalogProvider.CreateDefaultCatalog();

Screen = GetComponentInChildren<IMediaScreen>(true);

var backendProvider = GetComponentInChildren<IMediaBackendProvider>(true);
var backend = backendProvider?.CreateBackend(Screen, _logger)
              ?? DummyMediaBackendProvider.CreateDummyBackend(_logger);

// …既存の PlayerSession / PlaybackFlow をそのまま組み立てる…

foreach (var part in GetComponentsInChildren<IMediaPlayerPart>(true)) part.Bind(_context);
```

**要点は 2 つ**です。

1. **探すのは型ではなくインターフェース。** だから実装を差し替えても根っこは無変更。
2. **見つからなければ代役を立てる。** だからドラッグしただけで動く。

### 部品は `MediaPlayerContext` より奥を知らない

```csharp
public sealed class MediaPlayerContext
{
    public ICatalogStore Store { get; }    // Phase4-2 の唯一の窓口
    public PlaybackFlow Flow { get; }      // Phase4-3
    public PlayerSession Session { get; }
    public IBackendLogger Logger { get; }
}
```

カタログの作り方も、どのバックエンドが繋がっているかも見えません。
**URL の口もありません**(Phase4-2 からの約束をそのまま維持)。

---

## 3. Hierarchy 構成(置いたあと)

```
YourWorld
└── SmartMediaPlayer               ← ここをドラッグして置く
    ├── Screen
    │   └── Surface                ← 見える位置へ動かす(唯一の必須作業)
    ├── Player
    ├── Controller
    ├── UI
    └── Catalog
```

Play すると Console に組み立て結果が出ます。

```
=== SmartMediaPlayer を組み立てました ===
  カタログ    : 同梱サンプル(動画のみ)
  バックエンド: ログのみ(音も映像も出ません) -> DummyBackend
  出力先      : MediaScreen(映像: Surface / 音: Surface)
  操作        : MediaController(PlaybackFlow への中継)
  画面        : MediaPlayerUI(IMGUI β 版)
  一覧        : 10 件(Video のみ)
```

---

## 4. Inspector で設定する項目

### 必須:ありません

**すべて既定値のまま動きます。** 唯一やることは
`Screen/Surface` を見える位置へ動かすことだけです(Transform の操作)。

### 任意

| オブジェクト | 項目 | 既定 | 意味 |
|---|---|---|---|
| `SmartMediaPlayer` | Build On Start | ✓ | Start で自動的に組み立てる |
| | Play On Start | ✓ | 組み立て後、最初の 1 本を再生 |
| | Media Type | Video | 一覧に出す種別(Unknown で全種別) |
| | Related Count | 5 | 関連動画の件数 |
| | Minimum / Target Queue Count | 2 / 5 | Queue の補充設定 |
| | Log To Console | ✓ | 組み立て内容を Console に出す |
| `Screen` | Surface / Speaker | 自動 | 未設定なら子から探す |
| `Player` | Backend Name | DummyBackend | ログに出る名前 |
| `UI` | Visible / Area / Font Size | ✓ / 左半分 / 12 | 画面の見た目 |
| `Catalog` | Asset | なし | **Catalog Builder が生成した `Catalog.asset`** |
| | Include Music | — | 同梱サンプルに音楽も混ぜる |

### SDK 版で追加される項目

| オブジェクト | 項目 | 既定 | 意味 |
|---|---|---|---|
| `Player` | Preferred Player | **AVPro** | AVPro / Unity / Explicit |
| | Load Timeout Seconds | 20 | 読み込みの見張り |
| | Urls(焼き込み済み) | 10 件 | **実行時に VRCUrl は作れないので編集時に焼く** |

---

## 5. ワールドへ設置する手順

### 5-1. SDK 不要版(まず動かす)

1. `Assets/SmartMediaPlatform/World/Prefabs/SmartMediaPlayer_NoSDK.prefab` を
   **Hierarchy へドラッグ**
2. `Screen/Surface` を見える位置へ動かす
3. **Play**

画面左に Library / 関連 / Queue / いま再生中 が出て、
選ぶ → 再生 → 次へ が触れます(**音も映像も出ません**。ログのみ)。

### 5-2. SDK 版(実際に動画を出す)

1. **Tools > Smart Media Platform > Create SmartMediaPlayer Prefab (実際に再生)**
   - AVPro はエディタで映像を出さないので、絵を見たいときは
     **(Unity 版・エディタで確認)** のメニュー
2. 生成された `SmartMediaPlayer.prefab` を **Hierarchy へドラッグ**
3. `Screen/Surface` を見える位置へ動かす
4. **URL を実在するものに差し替える**
   - `VideoCatalogSource` の `url:` を編集(同梱データは架空のアドレス)
   - `Player` を選んで
     **Tools > Smart Media Platform > Bake Catalog Urls Into Selected Video Host**
5. シーンに `VRCSceneDescriptor` が無ければ追加(ClientSim の起動に必要)
6. **Play**(ClientSim)、または **VRChat SDK > Build & Test**

> ⚠️ アップロードしたワールドでは制御部分が動きません(冒頭の注意)。

---

## 6. Phase5-2 で分離・拡張する予定

### 6-1. Prefab をリポジトリに同梱する(SDK 版)

いま SDK 版はメニューで生成しています。理由は
**SDK 固有のコンポーネント(AVPro / UdonBehaviour / UdonSharp プログラム)を
含む Prefab はバージョン差で壊れる**ためです
(Phase3-3 で「program asset が null」を実際に踏んでいます)。

SDK のバージョンを固定できる段階になったら、同梱に切り替えられます。

### 6-2. 制御層の Udon 移植 ★最重要

冒頭のとおり、**アップロードしたワールドでは制御部分が動きません**。
Phase1-2 で作った並列配列 + `int` index の方式をそのまま広げる形で、
`PlayerSession` / `Queue` / `PlaybackFlow` 相当を UdonSharp へ移す必要があります。

移植しやすいよう、これまで
**interface を使い、`event` を避け、例外ではなく bool を返す**方針を通してきました
(Phase1-2 からの一貫した理由がこれです)。

### 6-3. UI を uGUI / ワールド内 Canvas へ

いまの `MediaPlayerUI` は IMGUI(`OnGUI`)で、**VR では見えません**。
`IMediaPlayerUI` を実装した Canvas 版に差し替えるのが 5-2 の作業です。
**下は 1 行も変わりません**。

### 6-4. Screen / Controller / UI を個別 Prefab へ

いまは 1 つの Prefab の中の子オブジェクトです。
`Screen.prefab` / `Controller.prefab` / `UI.prefab` に分けて
**Nested Prefab** にすれば、ワールドごとに UI だけ差し替える運用ができます。
契約(`IMediaScreen` など)は既にあるので、**分けるだけ**です。

### 6-5. 複数プレイヤーの同期

`PlayerSession` は 1 人ぶんの状態しか持ちません。
共有 Queue / DJ モードは Phase5-3 以降の想定です。

---

## 7. 確認できていないこと

- **コンパイルとテストの実行**:この作業環境に Unity / VRChat SDK が無いため、
  **一度も実行検証できていません。**
  括弧対応・メンバー名重複・asmdef 参照・
  **MonoBehaviour のファイル名一致**の静的チェック(全 197 ファイル)と机上確認のみです。
- **手書き Prefab の読み込み**:`SmartMediaPlayer_NoSDK.prefab` は
  YAML を手で書いています。Unity が読めるかは開いてみないと分かりません。
  もし壊れていたら
  **Tools > … > Create SmartMediaPlayer Prefab (SDK 不要・ログのみ)** で作り直せます。
- **AVPro の実配線**:Phase4-4 と同じく、`VRCAVProVideoScreen` /
  `VRCAVProVideoSpeaker` のフィールド名は実物で未確認です
  (名前で探して、できなければ Console に報告する形)。
- **VR での見た目**:IMGUI は VR で見えません(§6-3)。
