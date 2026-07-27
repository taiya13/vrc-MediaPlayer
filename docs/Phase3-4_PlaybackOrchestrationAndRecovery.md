# Phase3-4: Playback Orchestration & Recovery 設計・実装ドキュメント

**目的は「おすすめ動画プレイヤーの再生制御を完成させること」です。**
Phase3-3 で `Ended → 次へ` は動きましたが、**失敗すると止まりました。**
実在しない URL・アクセス拒否・読み込みが返らない — 動画は普通に失敗します。
Phase3-4 は新機能を足すのではなく、**止まらない再生ループへ整理**します。

```
RecommendationEngine → Queue → PlayerSession → BackendAdapter → VRChatVideoBackend
                                     ↑
                            AutoPlayController(今回追加)
```

| 要件 | 実装 |
|---|---|
| 動画終了で自動的に次へ | 従来どおり `PlayerSession` が進める。`AutoPlayController` は数えるだけ |
| 再生失敗で自動的に次へ | `AutoPlayController.OnBackendEvent(Error)` → `Recover()` |
| タイムアウトで自動的に次へ | `AutoPlayController.Tick()` の見張り(`LoadWatchdogSeconds`) |
| 再生できない動画があってもループが止まらない | `PlaybackFailureTracker` を `IPlaybackFilter` として合成 |
| 長時間再生し続けられる | 記憶に上限(失敗記憶 / 直近記憶 / 履歴)。120 本・200 本の耐久テスト |

### 変更していないもの(制約どおり)

| 層 | 状態 |
|---|---|
| `BackendAdapter`(`VideoBackendAdapter` / `MediaBackendAdapter`) | **差分ゼロ** |
| `VideoBackend`(`VRChatVideoBackend` / `VideoEventBridge`) | **差分ゼロ**(テスト用の `SimulatedVRCVideoPlayer` に `FailAllLoads` を追加したのみ) |
| `RecommendationEngine` のアルゴリズム | **差分ゼロ**(`: IRecommendationEngine` を付けただけ) |
| `MediaQueue` / `IQueue`(並びの管理) | **差分ゼロ** |
| `PlayerSession` の役割 | 増えていない(むしろ補充手順が外に出て**減った**) |
| VRCUrl を知る層 | `VideoBackend` だけ。この層は最後まで `MediaId(string)` しか扱わない |

外部API・StringDownloader・YouTube検索・VRCUrlInputField・ネットワーク同期・
マルチプレイヤー同期・UI・お気に入り・Playlist編集・AI Recommendation
のいずれにも触れていません。

---

## 1. 依頼された 7 つの整理

### 1-1. Queue 補充ロジックの一本化

Phase3-3 の時点で「おすすめで Queue を埋める」処理が **4 箇所**にありました。

| 場所 | 特徴 |
|---|---|
| `QueueManager.FillFrom()` | 直近の除外なし・緩和なし・カタログ補完なし |
| `PlayerSession.FillFromRecommendations()` | 直近 N 件を除外・現在再生中を除外 |
| `AutoQueueService.FillFromRecommendations()` | Playlist 優先の 2 段構え |
| `RecommendationPlaybackService.Fill()` | ふるいあり・緩和あり・カタログ補完あり |

これを **`IQueueRefiller` / `RecommendationQueueRefiller`(Queue 層)の 1 実装**に集約しました。
違いは**設定 3 つ**で表現します。

```csharp
new RecommendationQueueRefiller(catalog, engine, filter)
{
    RecentMemory              = 5,      // 直近何件を避けるか(0 で無効)
    AllowRepeatWhenExhausted  = true,   // 尽きたら除外を緩めてでも積むか
    AllowCatalogFallback      = true,   // おすすめが 0 件ならカタログから補うか
};
```

| 呼び出し側 | 設定 | 結果 |
|---|---|---|
| `QueueManager` | `0 / false / false` | Phase1-4 と同一の並び(既存テストが証明) |
| `PlayerSession`(既定) | `RecentMemory / false / false` | Phase2-3(B) と同一 |
| おすすめ再生 | `5 / true / true` | 止まらないループ |

**責務は分けたままです。**
「どこから種(seed)を取るか」「いつ補充するか」は各呼び出し側に残り、
移したのは「**どう積むか**」だけです。
`QueueManager` は `count + _queue.Count` という候補数の取り方まで元のままなので、
`Fill_AddsRecommendationsInRankOrder` が期待する
`{ music-002, music-007, video-001 }` はそのまま通ります。

> **一本化しなかったもの:`AutoQueueService`(Playlists)。**
> 理由は §7 に書きました。

### 1-2. RecommendationEngine の抽象化

```csharp
public interface IRecommendationEngine
{
    RecommendationResult[] GetNextRecommendations(string seedId, int count);
    RecommendationResult[] GetRelatedRecommendations(string seedId, int count);
}
```

`RecommendationEngine` は `: IRecommendationEngine` を付けただけで、
**スコアリングは 1 行も変えていません。**
利用側(`QueueManager` / `PlayerSession` / `PlayerSessionManager` / `AutoQueueService` /
`RecommendationQueueRefiller`)を具象からインターフェースへ緩めたので、
AI Recommendation・外部API・履歴ベース・人気順 を差し込むときに
**上位を書き換えずに済みます**。

`Func<...>` ではなく `interface` にしたのは Phase1-2 から通している方針
(将来 UdonSharp へ移すときの書き換えを避ける)です。

### 1-3. `PlayerSession.AutoQueueEnabled` の整理

Phase3-3 の `RecommendationPlaybackService` は、**セッションの設定を外から書き換えて**いました。

```csharp
// Phase3-3(問題)
_session.AutoQueueEnabled = false;   // 自動補充を止めて…
_session.Enqueue(id);                // …自前で積む
```

これは「**セッションの意思(補充する/しない)**」と「**補充の手段**」が混ざっています。
利用者が `AutoQueueEnabled` を見ても、もう本当の意味を表していません。

Phase3-4 では**手段だけを渡します**。

```csharp
// Phase3-4
_session.QueueRefiller = _refiller;   // 積み方を渡す。AutoQueueEnabled は触らない
```

`PlayerSession` は今までどおり自分で補充します(`AutoQueueEnabled` は本来の意味のまま)。
そのうえで `_recentlyRecommended` と `RememberRecommendation()` が `PlayerSession` から
**消えた**ので、**クラスは大きくなるどころか小さくなりました**。

> **渡した補充の設定は、セッションが書き換えません。**
> `PlayerSession.RecentMemory` を補充へ押し込むのは、**自分で作った既定の補充のとき**だけです
> (`_ownsRefiller`)。渡された補充は渡した側の持ち物なので、
> `service.RecentMemory = 1000` のような設定がセッションの既定値 5 で潰されることはありません。
> 何も渡さない従来の使い方では、Phase2-3(B) と挙動が完全に同じです。

### 1-4. CatalogSource の整理

Catalog Builder(提案書 §7)を後から足せるよう、**組み合わせの道具**を用意しました
(`Catalog/Runtime/Data/CatalogSources.cs`)。

| クラス | 役割 |
|---|---|
| `CompositeCatalogSource` | 複数の供給元を束ねる。**ID が重なったら先勝ち**(手書きが生成物に勝つ) |
| `FilteredCatalogSource` | 種別で絞る(「動画だけのカタログ」を複製せずに作る) |
| `InMemoryCatalogSource` | `MediaItem` の列をそのまま返す。**Builder の生成結果の受け皿** |

役割分担も整理しました。

| 供給元 | 用途 |
|---|---|
| `DummyCatalogSource`(Phase1-1) | 音楽中心の総合サンプル。Catalog / Queue / Audio の検証用 |
| `VideoCatalogSource`(Phase3-3) | 動画だけのサンプル。おすすめ再生ループの検証用 |
| `MixedCatalogSource`(Phase3-3) | 上記 2 つの合成。ふるいの検証用 |

`MixedCatalogSource` は自前の合成をやめて `CompositeCatalogSource` に委譲しました。
Catalog Builder は次のように差し込むだけで済みます。

```csharp
var source = new CompositeCatalogSource(
    new VideoCatalogSource(),          // 手書き(優先)
    new InMemoryCatalogSource(built)); // Catalog Builder の生成結果
```

### 1-5. Error Recovery

`PlaybackFailureTracker` が失敗した動画を覚え、**しばらく積み直しません**。

| 設定 | 既定 | 意味 |
|---|---|---|
| `CooldownSeconds` | 120 | 1 回失敗したら積み直さない秒数 |
| `BackoffMultiplier` | 3 | 失敗を重ねるほど待ち時間を伸ばす倍率(120 → 360 → 1080…) |
| `MaxCooldownSeconds` | 3600 | 待ち時間の上限 |
| `MaxFailuresBeforePermanentBlock` | 5 | これを超えたら二度と積まない |
| `MaxTracked` | 256 | 記憶の件数上限(超えたら古い失敗から捨てる) |

**これ自体が `IPlaybackFilter` です。**
「再生できる種別か」(`BackendPlaybackFilter`)とは別の関心事なので、
1 つのクラスに混ぜず `CompositePlaybackFilter` で並べます。

```csharp
var filter = new CompositePlaybackFilter(
    new BackendPlaybackFilter(backendManager),   // 再生できる種別か
    failures);                                   // さっき失敗していないか
```

新しい条件(お気に入りのみ・年齢制限など)は**実装を 1 つ足して並べるだけ**です。

時計は `Tick(deltaSeconds)` で外から進めます。純粋C#(`Time.time` を使わない)なので、
EditMode テストで時間を自由に進められます。

**再生できたら記憶を消します**(`MarkSucceeded`)。
一時的な回線不調なら、次の周回で自力で戻ります。

### 1-6. Playback State の整理

再生ループから見た状態を `AutoPlayState` に分けました。

```
Idle → Starting → Loading → Ready → Playing ⇄ Paused
                     ↑                  │
                     └── Recovering ←───┘   (Error / Timeout / Skip)
                              │
                    Stopped   Failed         (立て直しが続けて失敗)
```

**既存の状態を置き換えたのではなく、層ごとに分けたままです。**

| 列挙型 | 層 | 表しているもの |
|---|---|---|
| `VideoPlayerState` | VideoBackend | 実機の動画プレイヤーの状態 |
| `BackendState` | Backend | バックエンド共通の状態 |
| `PlaybackState` | Session | 利用者から見た再生状態 |
| `AutoPlayState` | AutoPlay | **おすすめ再生ループ**から見た状態 |

`RefreshState()` が `BackendState` を `AutoPlayState` へ写します。
`Stopped` / `Failed` / `Idle` は「ループを降りた状態」なので、写しても上書きしません。

### 1-7. 長時間耐久性

| 心配ごと | 対策 | テスト |
|---|---|---|
| 100 本以上流せるか | — | `Endurance_PlaysMoreThanOneHundredVideos`(120 本) |
| Queue が枯渇しないか | `Tick()` が毎回 `EnsureQueueFilled()` | `Queue_NeverRunsDryDuringALongLoop`(40 本) |
| おすすめが働き続けるか | 3 段構えの補充 | `Endurance_RecommendationKeepsWorking`(100 本) |
| メモリが増え続けないか | 失敗記憶 `MaxTracked` / 直近記憶 `RecentMemory` / 履歴 `MaxHistory` | `Endurance_KeepsMemoryBounded`(200 本) |
| 失敗が混ざっても続くか | 立て直し + クールダウン | `Endurance_SurvivesFailuresMixedIntoTheLoop`(120 本・3 本に 1 本失敗) |

---

## 2. `AutoPlayController` の中身

### 2-1. Ended も Error も同じ出口に合流する

```csharp
public void OnBackendEvent(BackendEvent backendEvent)
{
    switch (backendEvent.Type)
    {
        case BackendEventType.Started:
        case BackendEventType.Resumed:
            OnPlaybackStarted(backendEvent);   // 数える + 失敗の記憶を消す
            break;

        case BackendEventType.Ended:
            EndedCount++;
            // 次へ進めるのは PlayerSession の仕事。ここでは数えるだけ。
            break;

        case BackendEventType.Error:
            OnPlaybackFailed(backendEvent);    // 記録して立て直す
            break;
    }

    RefreshState();
}
```

**`Ended` では何もしません。** 進めるのは今までどおり `PlayerSession` です。
`AutoPlayController` が引き受けるのは「**失敗の記録**」と「**次へ進める合図**」だけで、
「次に何を再生するか」の判断は 1 つも持っていません。

### 2-2. `Error` のあとは明示的に `Play()` する

`Ended` のあとは `MediaPlayer.SkipNext()` が自動で再生を始めます
(直前が `Playing` か `Ended` だから)。
**`Error` のあとは始まりません**(状態がそのどちらでもない)。
そこだけ立て直し側で補います。

```csharp
if (!_session.IsPlaying) _session.Play();
```

### 2-3. 再入で落ちない

次の候補も即座に失敗すると、`Recover()` の途中でまた `Error` が返ってきます
(実機でも URL が連続で切れていれば起きます)。
再帰でスタックを積まないよう、**ループで処理**します。

```csharp
if (_inRecovery) { _pendingRecovery = true; return false; }   // 周回に合流させる

_inRecovery = true;
do
{
    _pendingRecovery = false;
    if (State == AutoPlayState.Failed) break;
    ok = RecoverOnce(reason);
}
while (_pendingRecovery
       && State != AutoPlayState.Failed
       && ++guard < MaxRecoveryIterations
       && (MaxConsecutiveFailures <= 0 || ConsecutiveFailures <= MaxConsecutiveFailures));
```

`ErrorRecovery_DoesNotRecurseIntoAStackOverflow` が
「立て直すたびに即失敗」を `MaxConsecutiveFailures = 0`(無制限)で流して確かめます。

### 2-4. 全部塞がったときの最後の手段

クールダウンで候補が**全部**塞がると、そこでループが死にます。
実装中にテストで踏んだので、**記憶を捨ててやり直す**逃げ道を入れました。

```csharp
if (!TryAdvance())
{
    if (_failures.BlockedCount > 0)
    {
        Log("すべての候補が塞がっているため、失敗の記憶を消してやり直します");
        _failures.Clear();
        if (!TryAdvance()) return false;
    }
    else return false;
}
```

「全部ダメだから止まる」より「もう一度試す」ほうが、
一時的な回線不調から自力で戻れます。

### 2-5. タイムアウトの見張り

`VRChatVideoBackend.LoadTimeoutSeconds` は **Backend 自身**の見張りで、
そちらが働けば `Error` として届きます。
`AutoPlayController.LoadWatchdogSeconds`(既定 30 秒)は
**Backend が沈黙した場合の最後の砦**なので、少し長めにしてあります。

---

## 3. ディレクトリ構成(追加・変更分)

```
Assets/SmartMediaPlatform/
├── Catalog/Runtime/Data/
│   └── CatalogSources.cs                     ★新規 組み合わせの道具 3 つ
├── Recommendation/Runtime/
│   ├── IRecommendationEngine.cs              ★新規 差し替えの契約
│   └── RecommendationEngine.cs               　変更 : IRecommendationEngine を付けただけ
├── Queue/Runtime/
│   ├── IPlaybackFilter.cs                    ★移動 AutoPlay → Queue(+ CompositePlaybackFilter)
│   ├── IQueueRefiller.cs                     ★新規 補充の契約
│   ├── RecommendationQueueRefiller.cs        ★新規 補充の唯一の実装
│   └── QueueManager.cs                       　変更 補充を委譲(挙動は同一)
├── Session/Runtime/
│   ├── PlayerSession.cs                      　変更 QueueRefiller を注入可能に(役割は減った)
│   └── PlayerSessionManager.cs               　変更 IRecommendationEngine を受ける
├── Playlists/Runtime/
│   └── AutoQueueService.cs                   　変更 IRecommendationEngine を受ける
├── Video/Runtime/
│   ├── SimulatedVRCVideoPlayer.cs            　変更 FailAllLoads を追加(テスト用)
│   └── Data/VideoCatalogSource.cs            　変更 MixedCatalogSource が Composite に委譲
└── AutoPlay/
    ├── Runtime/
    │   ├── AutoPlayController.cs             ★新規 Phase3-4 の中心
    │   ├── AutoPlayState.cs                  ★新規 ループから見た状態
    │   ├── PlaybackFailureTracker.cs         ★新規 失敗の記憶(= IPlaybackFilter)
    │   ├── BackendPlaybackFilter.cs          ★新規(旧 IPlaybackFilter.cs から分離)
    │   └── RecommendationPlaybackService.cs  　変更 補充を Queue 層へ移し、接続だけに
    ├── Demo/
    │   └── AutoPlayRecoveryConsoleDemo.cs    ★新規 SDK 不要の 6 シナリオ
    ├── VRChat/Runtime/
    │   └── VRChatAutoPlayRecoverySceneDemo.cs ★新規 SDK で実際に再生
    ├── Editor/
    │   └── Phase3AutoPlayRecoverySceneBuilder.cs ★新規 シーン生成メニュー
    ├── Scenes/
    │   └── Phase3AutoPlayRecoveryDemoScene.unity ★新規
    └── Tests/EditMode/
        ├── AutoPlayControllerTests.cs        ★新規 33 ケース
        └── PlaybackFailureTrackerTests.cs    ★新規 22 ケース
```

---

## 4. テスト方法

### 4-1. EditMode テスト(VRChat SDK 不要・いちばん確実)

**Window > General > Test Runner > EditMode > Run All**

| 見るところ | 期待 |
|---|---|
| `SmartMediaPlatform.AutoPlay.Tests` | 94 ケース すべて緑 |
| `SmartMediaPlatform.Queue.Tests` | 72 ケース すべて緑(既存 44 + 新規 28) |
| 全体 | **690 ケース** すべて緑 |

要件ごとに対応するテストは次のとおりです。

| 要件 | テスト |
|---|---|
| Ended だけで永久に再生が続く | `EndedEvent_AdvancesWithoutAnyOtherTrigger` / `Endurance_PlaysMoreThanOneHundredVideos` |
| Error で止まらない | `ErrorEvent_AdvancesToTheNextVideo` / `ConsecutiveErrors_KeepAdvancingUntilSomethingPlays` |
| Timeout で止まらない | `Timeout_AdvancesToTheNextVideo` / `Timeout_RemembersTheStuckVideo` |
| Queue が空でも補充される | `Tick_KeepsTheQueueTopped` / `Queue_NeverRunsDryDuringALongLoop` |
| 100 本以上の連続再生 | `Endurance_PlaysMoreThanOneHundredVideos`(120)/ `Endurance_KeepsMemoryBounded`(200) |
| 補充が一本化されている | `RecommendationQueueRefillerTests`(28 ケース) |
| クールダウン / バックオフ | `PlaybackFailureTrackerTests`(22 ケース) |
| 既存の挙動が壊れていない | `QueueManagerTests.Fill_AddsRecommendationsInRankOrder` ほか既存 596 ケース |

### 4-2. ConsoleDemo(VRChat SDK 不要)

**Tools > Smart Media Platform > Open Phase3-4 Auto Play Recovery Demo Scene** → **Play**

シーンが無ければ
**Tools > Smart Media Platform > Create Phase3-4 Auto Play Recovery Demo Scene (再生成)**。

Console に 6 つのシナリオが出ます。

```
=== Phase3-4 Playback Orchestration & Recovery Demo ===

── 1. 動画終了(Ended)で次のおすすめへ
  Start("video-001") -> [Playing] video-001
    1. Ended(video-001) -> video-002 [Playing] Queue=4
    2. Ended(video-002) -> video-003 [Playing] Queue=4
    3. Ended(video-003) -> video-004 [Playing] Queue=4

── 2. 再生失敗(Error)でも止まらず次候補へ
  InvalidUrl   (video-001) -> video-002 [Playing]
  AccessDenied (video-002) -> video-003 [Playing]
  PlayerError  (video-003) -> video-004 [Playing]
  失敗 3 回 / 立て直し 3 回 / 連続失敗 0

── 3. タイムアウトでも止まらず次候補へ
  読み込みが終わらない状態 -> [Loading] BackendState=Loading
  見張りが 5 秒で打ち切り -> [Playing] video-002
  タイムアウト 1 回
  止まった動画は記録された -> Blocked=True

── 4. 失敗した動画を一定時間再試行しない
  video-001 が失敗 -> Blocked=True(待ち時間 10 秒)
  Queue を積み直しても入らない -> 入らない
  11 秒経過 -> Blocked=False(再挑戦できる)
  2 回目の失敗後 11 秒 -> Blocked=True(失敗を重ねるほど待ち時間が伸びる)

── 5. 長時間耐久(120 本連続・4 本に 1 本は失敗)
  120 本を最後まで流し切りました(一度も止まっていません)
  Queue の最小値 = 3(0 にならなければ枯渇していない)
  失敗の記憶 = N 件(上限 256 — 増え続けない)

── 6. Phase3-4 で整理したところ
  PlayerSession.QueueRefiller = RecommendationQueueRefiller
  RecommendationPlaybackService.Refiller と同じ実体か -> True
  PlayerSession.AutoQueueEnabled = True(外から書き換えていない)
  RecommendationEngine is IRecommendationEngine -> True
```

> 数値は乱数種と件数に依存します。**見るのは「止まっていないこと」**です。

Inspector で `_enduranceCount`(既定 120)と `_failEvery`(既定 4)を変えられます。

### 4-3. DemoScene(VRChat SDK で実際に再生)

**Tools > Smart Media Platform > Create Phase3-4 VRChat SDK Auto Play Recovery Scene (実際に再生)**
→ 生成されたシーンで **Play**

このシーンは **URL をわざと一部だけ焼き込みます**(3 本に 1 本は焼きません)。
焼かれていない動画に当たると `VRChatVideoBackend.Load` が `InvalidUrl` で失敗するので、
**実機で URL が切れたときとまったく同じ経路**で立て直しが動きます。
実行時に `VRCUrl` は作りません(作れません)。

`VRChatAutoPlayRecoverySceneDemo` は `Update()` で
`_controller.Tick(Time.deltaTime)` を呼ぶだけです。
最初の `Start()` 以降、**再生の指示を 1 つも出しません**。

Console に 10 秒ごとに状態が出ます。

```
[Phase3-4] [Playing] now=video-004 queue=4 played=6 ended=4 failed=2(timeout 0) recovered=2 consecutive=0 blocked=2
  PlaybackFailureTracker(記憶 2 / 停止中 2 / 失敗累計 2)
  直近の立て直し: URL が不正です
```

> **※ 実機で映像は出ません。** `VideoCatalogSource` の URL は架空のアドレスです。
> 実際に映像を出すには実在する動画 URL に差し替えてから焼き直してください。

### 4-4. 手っ取り早く「止まらない」ことだけ見たい場合

1. `Phase3AutoPlayRecoveryDemoScene` を開いて **Play**
2. Console の **§5(長時間耐久)** だけ見る
3. 「**120 本を最後まで流し切りました**」と出ていれば要件を満たしています

---

## 5. 設計上の判断

### 5-1. なぜ `IPlaybackFilter` を Queue 層へ移したか

補充の実装(`RecommendationQueueRefiller`)がこの判定を使うためです。
AutoPlay に置いたままだと `Queue → AutoPlay` の参照が要り、依存の向きが逆になります。

**`BackendPlaybackFilter` だけは AutoPlay に残しました。**
`BackendManager` に問い合わせる実装なので、Queue へ持っていくと
`Queue → Backend` の参照が生まれてしまいます。

```
Catalog ← Recommendation ← Queue ← Backend ← Player ← Session ← AutoPlay
                             ↑                                      │
                       IPlaybackFilter                    BackendPlaybackFilter
```

### 5-2. なぜ `AutoPlayController` を `PlayerSession` に入れなかったか

制約が「`PlayerSession` を巨大化させない」だったこともありますが、
**関心事が違う**のが本質です。

| クラス | 答える問い |
|---|---|
| `PlayerSession` | 「次に何を再生するか」(Repeat / Shuffle / 履歴 / Queue) |
| `AutoPlayController` | 「**止まってしまったときにどうするか**」(失敗の記録 / 立て直し / 見張り) |

後者は「おすすめ再生ループ」という**特定の使い方**でしか要りません。
`PlayerSession` は「利用者が手で操作するプレイヤー」としても使うので、
失敗しても勝手に次へ進んでほしくない場面があります。
分けておけば、`AutoPlayController` を**付けなければ従来どおり**です。

### 5-3. なぜ `AutoPlayState` を新設したか(既存の状態を使わなかったか)

`PlaybackState` は「利用者から見た再生状態」で、
`Recovering`(立て直し中)や `Starting` は**利用者に見せる状態ではありません**。
`PlaybackState` に足すと Session の語彙が「ループの都合」で汚れます。
層ごとに別の列挙型を持つのは Phase1-5 から一貫した方針です。

---

## 6. 確認できていないこと

- **コンパイルとテストの実行**:この作業環境に Unity / .NET SDK / VRChat SDK が無いため、
  **一度も実行検証できていません。**
  括弧対応・メンバー名重複の静的チェック(全 153 ファイル)と、
  状態遷移・カウンタの机上確認のみです。
- **実機での映像再生**:`VideoCatalogSource` の URL は架空のため、
  SDK シーンでは読み込みに失敗します(そのぶん `Error` 経路は確実に動きます)。
- **`AutoQueueService` の 29 ケース**:この環境で実行できないため、
  一本化を見送りました(§7)。
- **VRChat の `RateLimited`**:短時間の連続 `LoadURL` が実機でどう弾かれるかは
  未検証です。`PlaybackFailureTracker` のクールダウンが結果的に間隔を空けますが、
  **狙って作った対策ではありません**。

---

## 7. 実装中に見つけた、まだ手を付けていないこと

### 7-1. `AutoQueueService`(Playlists)の補充は一本化していない

**理由。** `AutoQueueService` の補充は 2 段構えで、
「まず Playlist の中から積む → 足りなければおすすめで補う」という形です。
これを共有の `RecommendationQueueRefiller` へ載せると、
**Queue 層に Playlist の概念が漏れます**(制約「Queue は並びを管理するだけ」に反する)。

**現状の安全性。** `AutoQueueService` は
`Playlists` asmdef の中だけで使われており、外部からの参照はありません
(`grep` で確認済み)。今回の変更は `RecommendationEngine` → `IRecommendationEngine` の
1 箇所だけで、補充処理には触れていません。29 ケースのテストもそのままです。

**提案する解き方。** `IPlaybackFilter` をもう 1 つ足すだけで済みます。

```csharp
// Playlists 層に置く。Queue 層は Playlist を知らないままで済む
public sealed class PlaylistMembershipFilter : IPlaybackFilter
{
    public bool CanPlay(MediaItem item) => _playlist.Contains(item.Id);
}

// 1 段目 = メンバーシップふるいあり、2 段目 = なし
```

**この作業環境ではテストを実行できないため、独断で進めませんでした。**
Phase3-5 以降で、テストを流せる状態でやるのが安全だと考えます。

### 7-2. Phase3-3 のテストを 3 つ書き換えた

要件 1-3(`AutoQueueEnabled` の整理)が、まさにその構造をやめろという指示だったため、
**それを確かめていたテストは成り立たなくなりました**。削除ではなく書き換えています。

| 旧テスト | 新テスト | 確かめる内容 |
|---|---|---|
| `Service_TakesOverTheSessionAutoQueue` | `Service_DoesNotTouchTheSessionSettings` | `AutoQueueEnabled` が **true のまま**であること |
| `Service_CanLeaveTheSessionAutoQueueAlone` | `Session_UsesTheInjectedRefillerForItsOwnRefill` | セッション自身の補充が、渡した refiller 経由で動きふるいも効くこと |
| `Service_ObservesBackendEvents` | `Service_InstallsItsRefillerOnTheSession` | `session.QueueRefiller` と `service.Refiller` が**同じ実体**であること |

3 つ目は、`Ended` を受け取る役目が `RecommendationPlaybackService` から
`AutoPlayController` へ移ったためです(接続層は補充だけを持つ形になりました)。
`Ended` を数える確認は `AutoPlayControllerTests` 側に移してあります。

このほか、参照先が変わったアサーションが 2 つあります
(`Service.EndedCount` → `Controller.EndedCount`、
`Describe()` の `ended=` → `refills=`)。**削除したテストはありません。**

### 7-3. `DummyVideoBackendAdapter` と `VideoBackendAdapter` の重複

Phase2-4 の引き継ぎに書かれたまま、まだ残っています。
Phase3-4 の制約が「BackendAdapter の責務を変更しない」だったため触っていません。
`DummyVideoBackendAdapter` を使っているのは Adapter のテストとデモだけなので、
削除は難しくないはずです。

### 7-4. `PlaybackFailureTracker` の線形探索

`Find()` が `List` の線形探索です。`MaxTracked = 256` なので実用上は問題ありませんが、
上限を大きく取る構成では `Dictionary` に替えるべきです
(Udon へ移す場合は並列配列になるので、そのときに合わせて見直すのが自然)。

---

## 8. Phase3-5 以降への引き継ぎ

| 項目 | 内容 |
|---|---|
| 読み込みレート制限 | VRChat の `RateLimited` に合わせた最小間隔は未実装。`AutoPlayController` に「前回の Load からの経過」を足すのが素直 |
| Catalog Builder | `InMemoryCatalogSource` + `CompositeCatalogSource` が受け皿として用意済み。JSON 読み込みは Builder 側に置く |
| AI Recommendation | `IRecommendationEngine` を実装して差し替えるだけ。上位の変更は不要 |
| UI | `AutoPlayController.Describe()` / `Failures.GetBlockedIds()` がそのまま表示材料になる |
| 同期(Phase5) | `AutoPlayController` は `PlayerSession` しか触らないので、`IQueue` の実装を共有キューに差し替えれば載る |
| Playlist との統合 | §7-1 の `PlaylistMembershipFilter` |
