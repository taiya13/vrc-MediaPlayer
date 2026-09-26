# Phase5-3 — ワールドで使う操作 UI(uGUI 版)

**目的**: デバッグ表示だったものを、**VRChat ワールドで実際に使える操作 UI** に置き換えること。
見た目を豪華にすることではなく、**操作性と拡張性の土台**を作ることを狙いにしています。

---

## 0. Phase5-2 の UI で何が足りなかったか

Phase5-2 の `UdonMediaPlayerUI` は「動く」ところまでは行っていましたが、
**ワールドに置いて使うものにはなっていません**でした。理由は 4 つあります。

| 問題 | 何が困るか |
| --- | --- |
| **1 枚岩** | Now Playing / Library / 関連 / Queue の表示を 1 クラスが全部抱えていた。行数を変えるにもここを触る |
| **世界に 1 枚だけ** | `UdonMediaController` が UI を **1 つだけ名指し**で持っていた。壁パネルとリモコンを同時に置けない |
| **押す手段が Collider** | 操作ボタンが 3D の板(Interact)で、Canvas の表示と別の場所にあった。見ているものと押すものが一致しない |
| **窓口が画面に依存** | `Controller.PlayRelated(位置)` が **UI 側の対応表**を読みに行っていた。UI を差し替えると窓口が壊れる |

加えて C# 側の `MediaPlayerUI` は `OnGUI`(IMGUI)で、
**アップロードしたワールドでは動きません**でした。

---

## 1. 何を作ったか

### 新しいクラス(`World/Udon/UI/` — 5 つ・すべて `UdonSharpBehaviour`)

| クラス | 責務 | ロジック |
| --- | --- | --- |
| `UdonMediaPanel` | **操作 UI 1 枚ぶん。差し替えの単位** | 持たない(参照を配って書き直しを配るだけ) |
| `UdonNowPlayingView` | いま鳴っているものの表示 | 持たない(読んで書き写すだけ) |
| `UdonTransportView` | 再生操作のボタン群 | 持たない(窓口へ中継するだけ) |
| `UdonMediaListView` | 一覧 1 つぶん(Library / 関連 / Queue **兼用**) | 持たない(ページの数え方だけ) |
| `UdonMediaListRow` | 一覧の 1 行 | 持たない(何行目かを返すだけ) |

### 消したクラス

| クラス | 理由 |
| --- | --- |
| `UdonMediaPlayerUI` | 1 枚岩のデバッグ表示。`UdonMediaPanel` + 4 つの表示に分割 |
| `UdonMediaRowButton` | 行が「操作の意味」を持っていた。`UdonMediaListRow` は行番号を返すだけ |
| `MediaPlayerUI`(C#) | **IMGUI**。ワールドで動かない。ClientSim は UdonSharp を動かせるので Udon 側で足りる |

`IMediaPlayerUI` インターフェースは**残しています**(`SmartMediaPlayerRoot` の UI 枠を塞がないため)。
既定の実装が無くなっただけです。

### 変えたクラス(制御層)

| クラス | 変更 |
| --- | --- |
| `UdonMediaController` | UI 1 枚の名指し → **登録制の通知先リスト**。`PlayCatalogIndex` などの口を追加 |
| `UdonSmartMediaPlayer` | `Ui` の欄を削除(パネルは自分で名乗り出るので根っこは知らなくてよい) |
| `UdonMediaControlButton` | 送り先を `UdonMediaController` 固定 → 任意の `UdonSharpBehaviour` |

### 正典(`World/UdonModel/`)

| クラス | 役割 |
| --- | --- |
| `ListPageModel` | `UdonMediaListView` のページの数え方を純粋 C# で書いたもの。**EditMode 16 ケース** |

**`UdonPlayerSession` / `PlaybackModel` / `UdonCatalogStore` / `UdonVideoBackend` /
`UdonRecommendationEngine` / `UdonMediaCatalog` は 1 行も変えていません。**

---

## 2. 責務の線はどこにあるか

```
           ┌──────────── パネル(何枚でも) ────────────┐
           │  UdonMediaPanel                         │
           │   ├ UdonNowPlayingView   読むだけ        │
           │   ├ UdonTransportView    押すだけ        │
           │   └ UdonMediaListView ×N 読む + 押す     │
           └───────┬──────────────────┬──────────────┘
          読む     │                  │  押す
                   ▼                  ▼
        UdonCatalogStore      UdonMediaController
        UdonPlayerSession              │ 中継だけ
        (URL は出ない)                  ▼
                              UdonPlayerSession   ← 再生の判断はここだけ
                                       │
                                       ▼
                              UdonVideoBackend    ← URL を知る唯一の場所
```

守っているルール(Phase4-2 から変わっていません):

- **UI は判断しない。** 次に何を再生するかは `UdonPlayerSession` だけが決める
- **UI は URL を見られない。** 引くのは `UdonCatalogStore` 越しだけで、`GetUrl` が存在しない
- **操作は必ず窓口を通る。** パネルが `UdonPlayerSession` を直接叩くことはない

### 窓口が画面に依存しなくなった話

Phase5-2 の `Controller.PlayRelated(位置)` は、**UI が持っている対応表**を読んでいました。
UI を差し替えると窓口が動かなくなる、という向きの依存です。

Phase5-3 では、一覧が **描いた時点の「行 → catalog index」を自分で覚えて**おき、
窓口には `PlayCatalogIndex(catalog index)` で渡します。

- 窓口は画面の型を知らない
- **画面に出ていたものと、実際に再生されるものが必ず一致する**
  (押した瞬間に引き直すと、その間に Queue が動いていたときズレる)

---

## 3. パネルはなぜ何枚でも置けるのか

`UdonMediaController` が持つのは「書き直しの知らせを受け取りたい人」の一覧です。

```csharp
// UdonMediaController
public bool AddListener(UdonSharpBehaviour listener)   // 相手の型を知らない
public void NotifyChanged()                            // 全員に SendCustomEvent("Refresh")
```

パネルは自分の `Start` で名乗り出ます。根っこ(`UdonSmartMediaPlayer`)は
**パネルが何枚あるかも、どこにあるかも知りません**。

その結果:

- 壁パネルと手持ちリモコンを**同時に**置ける(操作すると両方が更新される)
- パネルを**この Prefab の外**、ワールドの好きな場所に置ける
- 受け取る側の型を縛っていないので、**UI 以外も登録できる**

最後の点が Phase5-4(同期)の差し込み口になります。

---

## 4. Prefab 構成

`Tools > Smart Media Platform > セットアップ` の「Prefab を作る」で **3 つ**できます。

| Prefab | 中身 | いつ使うか |
| --- | --- | --- |
| `SmartMediaPlayer.prefab` | 中身 + 壁パネル 1 枚 | **まずこれを 1 つ置く** |
| `MediaWallPanel.prefab` | 壁パネルだけ | 2 枚目以降を別の場所に足す |
| `MediaRemotePanel.prefab` | 手持ちリモコン(最小構成) | 差し替えの例。Library も関連も持たない |

パネルを足す手順は **2 つだけ**です。

1. Prefab を Hierarchy へドラッグ
2. Inspector の `Core` に、シーンの `SmartMediaPlayer` を挿す

残りの欄(`Controller` / `Session` / `Store` / `Screen`)は空のままで構いません。
実行時に `Core` から引いてきます。

### Hierarchy

```
SmartMediaPlayer                UdonSmartMediaPlayer   ← 配線だけ
├── Screen                      UdonMediaScreen        ← 映像と音の出力先(自由に動かせる)
│   └── Surface                 Renderer + AudioSource
├── Player                      動画プレイヤー + UdonVideoBackend  ← URL を知る唯一の場所
├── Catalog                     UdonMediaCatalog + UdonCatalogStore + UdonRecommendationEngine
├── Session                     UdonPlayerSession      ← 再生の判断
├── Controller                  UdonMediaController    ← 操作の窓口(パネルはここに名乗り出る)
└── WallPanel                   UdonMediaPanel         ← ここから下がまるごと差し替え可能
    └── Canvas                  Canvas(World Space) + GraphicRaycaster
        └── Body                            ← 開閉する対象
            ├── Backplate       Image
            ├── PanelTitle      Text
            ├── NowPlaying      UdonNowPlayingView
            │   ├── Title / Artist / Time / QueueCount / State   Text
            │   └── Progress → Fill                              Image(Filled)
            ├── Transport       UdonTransportView
            │   ├── Previous / PlayPause / Next / Stop           Button
            │   ├── VolumeDown / Volume / VolumeUp               Button + Text
            │   └── ClearUpcoming                                Button
            ├── Library         UdonMediaListView(Source = 0)
            │   ├── Header / Page                                Text
            │   ├── PreviousPage / NextPage                      Button
            │   ├── Empty                                        Text
            │   └── Row0..Row5  UdonMediaListRow
            │       ├── Content
            │       │   ├── Hit        Button
            │       │   │   └── Index / Title / Sub / Duration   Text
            │       │   └── Secondary  Button(＋ Queue へ追加)
            │       └── Highlight      Image(いま鳴っている印・半透明)
            ├── Related         UdonMediaListView(Source = 1)× 3 行
            ├── Queue           UdonMediaListView(Source = 2)× 4 行
            └── Status          Text
```

`MediaRemotePanel` は `Library` と `Related` が無いだけで、**同じ形**です。

### 行の作りについて 1 点

`Content` は **必ず行の子**にしてあります。行そのもの(`UdonMediaListRow` が付いている
GameObject)を非アクティブにすると、その `UdonBehaviour` はイベントを受け取れなくなり、
**一度空行になったら二度と書き戻せなくなる**ためです。

`Highlight` は `Content` の**後ろ**に置いています。先に置くと行の不透明な面に隠れます。

---

## 5. Inspector で設定する項目

### 必ず触るもの

| どこ | 項目 | 何を入れるか |
| --- | --- | --- |
| `SmartMediaPlayer/Catalog` の `UdonMediaCatalog` | `Urls` | **実在する URL**。同梱サンプルは架空のアドレス |
| パネルを足したとき | `UdonMediaPanel.Core` | シーンの `SmartMediaPlayer` |

### 触ると挙動が変わるもの

| コンポーネント | 項目 | 既定 | 意味 |
| --- | --- | --- | --- |
| `UdonSmartMediaPlayer` | `PlayOnStart` | true | 入室したら自動で 1 本目を鳴らす |
| | `StartDelay` | 1.0 | 鳴らし始めるまでの待ち(動画プレイヤーの準備待ち) |
| | `LogWiring` | true | 配線の結果を Console に出す |
| `UdonPlayerSession` | `MinimumQueueCount` | 2 | この数を下回ったら補充 |
| | `TargetQueueCount` | 5 | 補充後に目指す長さ |
| | `AutoQueueEnabled` | true | おすすめによる自動補充 |
| `UdonMediaScreen` | `Volume` | 0.6 | 既定の音量 |
| `UdonCatalogStore` | `OnlyMediaType` | 1(Video) | 一覧に出す種別。0 なら絞り込まない |
| `UdonMediaPanel` | `RefreshInterval` | 0.5 | 何秒ごとに書き直すか。0 なら操作時だけ |
| | `OpenOnStart` | true | 入室時に開いておく |
| | `PanelName` | — | 見出しに出す文字 |
| `UdonMediaListView` | `Source` | — | **0 = Library / 1 = 関連 / 2 = Queue** |
| | `HeaderLabel` | — | 見出しに出す文字 |
| `UdonTransportView` | `VolumeStep` | 0.1 | ＋ / − 1 回ぶんの変化量 |

### 空のままでよいもの

`UdonMediaPanel` の `Controller` / `Session` / `Store` / `Screen`、
各表示の `Session` / `Store` / `Controller` / `Panel`、
`UdonMediaListRow.List` — **すべて実行時に自動で埋まります**。

---

## 6. VRChat で確認すべきテスト項目

`Build & Test`(またはアップロード)後に、上から順に見てください。

### A. 起動

1. Console に `[UdonSmartMediaPlayer] カタログ N 件 / 一覧 N 件 / … / パネル N 枚` が出るか
   - **パネル N 枚が 0** なら、パネルの `Core` が挿さっていません
2. 壁パネルの文字が読めるか(遠すぎ / 小さすぎないか)
3. 1 秒後に 1 本目が自動で鳴り始めるか

### B. 表示

4. Now Playing に**いま鳴っているもの**の見出し・アーティストが出るか
5. 時間表示が**進む**か(`0:12 / 3:45` の形)
6. 進捗バーが伸びるか
7. Library に一覧が出るか。件数が見出しの横に出るか
8. Queue の先頭に `♪` が付き、それが Now Playing と**同じもの**か
9. いま鳴っている行に**青い帯**が付くか
10. 関連が出るか(出ない場合、そのメディアに関連 ID が焼かれているか確認)

### C. 操作(ここが本題)

11. **再生 / 一時停止** — 押すと映像が止まる。ボタンの文字が `▶ 再生` ↔ `‖ 一時停止` で入れ替わる
12. **次へ / 前へ** — 別のものに切り替わる。Queue の並びが 1 つずれる
13. **停止**
14. **Library の行を押す** — その曲が再生される。Now Playing と Queue の先頭が変わる
15. **Library の ＋ を押す** — Queue の末尾に増える(再生は変わらない)
16. **関連の行を押す** — その曲が再生される
17. **Queue の 2 行目を押す** — そこへ飛ぶ
18. **Queue の × を押す** — その行が消える。**先頭には × が出ていない**こと
19. **Queue を空に** — 先頭以外が消える
20. **音量 ＋ / −** — 実際に音量が変わり、`音量 70%` の表示が追従する
21. 操作するたびに**下の Status 行**にメッセージが出るか

### D. ページ送り

22. Library の `›` を押すと 7 件目以降が出るか。`2 / 3` の表示が変わるか
23. 最終ページで `›` が**消える**か。先頭ページで `‹` が**消える**か
24. Queue から曲を消して件数が減ったとき、**空のページに取り残されない**か

### E. パネルが複数あるとき(`MediaRemotePanel` を足して確認)

25. リモコン側で「次へ」を押すと、**壁パネルの表示も更新される**か
26. 逆方向も同じか
27. リモコンには Library / 関連が無いが、それでも再生操作が全部効くか

### F. 落ちないこと

28. URL が壊れているものを再生しても、自動で次へ送られるか(`NotifyError`)
29. 最後まで再生されたら次へ進むか(`NotifyEnded`)
30. **2 人以上で入室**して、片方が操作しても Udon エラーが出ないか
    - ※ このフェーズでは**同期していないので、見え方が人によって違うのが正しい**

### よくある詰まり

| 症状 | 見るところ |
| --- | --- |
| **ボタンが 1 つも押せない** | `セットアップ > 操作 UI の配線を確認する` → 要修復なら「繋ぎ直す」（下記） |
| ボタンが一部だけ押せない | Canvas に `GraphicRaycaster` があるか / パネルの裏側から見ていないか |
| 何も映らない | `UdonMediaCatalog` の `Urls` が架空のアドレスのままではないか |
| `Source C# script … is null` | `セットアップ > 壊れた U# プログラムを修復する` |
| パネルが真っ白 | `Core` が挿さっているか(Console の「パネル N 枚」で分かる) |

### ボタンが 1 つも押せないとき（Phase5-3 で実際に起きた）

初版の Prefab ビルダーには**2 つのバグ**がありました。どちらも
「Prefab は出来ているのに中身が空」という同じ形をしています。

**1) `CopyProxyToUdon` を呼んでいなかった**

`UdonSharpBehaviour` のコンポーネントは **Inspector 用の見せかけ(proxy)** で、
実際に動くのは裏の `UdonBehaviour` が持つシリアライズ済みデータのほうです。
Inspector で値を変えたときは UdonSharp のエディタが写してくれますが、
**エディタスクリプトから代入したぶんは `UdonSharpEditorUtility.CopyProxyToUdon` を
自分で呼ばないと届きません。**

Phase5-2 までは参照が少なく自動更新で拾えていましたが、
Phase5-3 で参照が一気に増え(行 × 3 一覧、配列、行↔一覧の相互参照)、
Odin シリアライザが `ArgumentNullException: unityObject` で落ちて
そこから先の書き戻しが全部止まりました。

**2) 配線を読み戻して確かめていなかった**

`UnityEventTools.AddStringPersistentListener` を呼んだだけで
「すべて繋がりました」と報告していたので、
Inspector の On Click が `No Function` のままでも気づけませんでした。

**直したこと**

- `SavePrefab` の直前に `UdonWorldUiKit.SyncProxies()` で全部書き戻す
- 裏の `UdonBehaviour` が無い proxy は**先に弾く**(Odin を落とさない)
- `Bind` は登録後に `GetPersistentTarget` / `GetPersistentMethodName` を
  **読み戻してから**成功と数える
- Console の報告を `40 件 OK / 0 件 NG` の実数に

**3) uGUI だけでは実機で押せなかった**

上の 2 つを直して配線が 39 個すべて正しく入った状態でも、
**VRChat では 1 つも押せませんでした。** ワールド内 uGUI のレイキャストを
VRChat が拾わない置き方になっていたためです。

Phase5-2 の `UdonMediaControlButton` のコメントに
「VRChat でいちばん確実に押せるのは Collider + Interact」と書いてあったのに、
Phase5-3 で uGUI 一本にしたのが誤りでした。

**いまはボタンごとに 2 つの経路を持たせています。**

| 経路 | 仕組み | いつ効くか |
| --- | --- | --- |
| uGUI | `Button.onClick` → `SendCustomEvent` | レイキャストが通る置き方のとき |
| **「使う」** | `BoxCollider` + `UdonMediaControlButton.Interact()` | **実機ではこちらが本命** |

「使う」のときはボタンの意味がそのまま出ます(`再生 / 一時停止`、`この曲へ移動`、
`Queue から外す` …)。届く距離は 5 m にしてあります(`UdonBehaviour` の既定は 2 m)。

**二重発火**は、両方の経路が着地する `UdonTransportView` /
`UdonMediaListView` の 1 か所で抑えます(同じ操作が 0.25 秒以内に 2 回来たら
2 回目を捨てる。`DoubleFireGuard` で変更可)。

**すでに置いてしまった Prefab を直す**

作り直すと位置調整がやり直しになるので、シーンに置いたまま繋ぎ直せます。

| メニュー | すること |
| --- | --- |
| `セットアップ > 操作 UI の配線を確認する` | 何が繋がっていないかを Console に出す |
| `セットアップ > 操作 UI の配線を繋ぎ直す` | シーンのパネルを名前を頼りに張り直し、Udon へ書き戻す |

繋ぎ先は **GameObject の名前**で決まります(`PlayPause` → `TogglePlayPause`、
`Hit` → `Click`、`Secondary` → `ClickSecondary` …)。
名前を変えていると対象から外れ、診断にその旨が出ます。

実行後は**シーンを保存**してから Build & Test してください。

---

## 7. Phase5-4(同期)で足される部分

**いまは同期していません。** 各プレイヤーが自分の `UdonPlayerSession` を持っていて、
それぞれ別のものを再生します(上の項目 30 のとおり)。

Phase5-4 で入るのは次の 3 つです。

### 1) 同期の持ち主(新規)

`UdonPlayerSession` を **`BehaviourSyncMode.Manual`** にして、
`[UDONSYNCED]` を付けるのは以下だけになる見込みです。

| 同期するもの | 理由 |
| --- | --- |
| Queue(catalog index の配列 + 件数) | 全員が同じ並びを見る必要がある |
| 再生中かどうか | 一時停止を揃える |
| 再生開始時刻(`Networking.GetServerTimeInMilliseconds`) | 途中入室者を同じ位置に合わせる |

`_selectedIndex`(誰が何を選んでいるか)は**同期しません**。手元の都合なので。

### 2) 操作の所有権

いまは押した人がその場で `UdonPlayerSession` を動かしています。
同期後は「**所有権を取ってから動かし、`RequestSerialization` する**」に変わります。
変わるのは **`UdonMediaController` の中だけ**です。

```csharp
// Phase5-4 でこうなる(イメージ)
public void Next()
{
    if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
    Done(Session != null && Session.Next());
    RequestSerialization();
}
```

### 3) 受信したら書き直す

`OnDeserialization()` で `NotifyChanged()` を呼ぶだけです。
**登録制の通知はもう入っている**ので、パネルが何枚あっても勝手に更新されます。

### UI 側の変更見込み

| 部分 | 変わるか |
| --- | --- |
| `UdonMediaPanel` / 4 つの表示 / `UdonMediaListRow` | **変わらない** |
| Prefab のレイアウト | **変わらない** |
| `UdonMediaController` | 所有権の取得と `RequestSerialization` が入る |
| `UdonPlayerSession` | `[UDONSYNCED]` と `OnDeserialization` が入る |
| 追加されうる表示 | 「誰が操作したか」「同期中」の 1 行程度。`Text` を 1 つ足すだけ |

**UI がロジックを持っていないので、同期は UI の下だけで完結します。**
これが Phase5-3 で責務を分けた一番の理由です。

### Catalog Builder が入るとき

こちらも UI は変わりません。焼き込み先は今までどおり `UdonMediaCatalog` で、
UI が見るのは `UdonCatalogStore` 越しだけだからです。
件数が増えればページ送りがそのぶん増える、というだけです。

---

## 8. 検査

```bash
python3 tools/typecheck/genmeta.py      # .meta が揃っているか
python3 tools/typecheck/typecheck.py    # No compile errors.
python3 tools/typecheck/runtests.py     # 857 passed, 0 failed
python3 tools/typecheck/udon_lint.py    # 17 ファイル / 問題なし
```

Phase5-2 の 841 ケースに `ListPageModelTests` の 16 ケースが加わって **857** です。
