# Phase3-2: VRChat Video Event Bridge 設計・実装ドキュメント

**目的はおすすめ動画を再生することではありません。**
Phase3-1 では `VRChatVideoBackend.Tick()` の**ポーリング**で読み込み完了と再生終了を
「推測」していました。Phase3-2 では **VRChat の VideoPlayer が実際に発火したイベント**を
Backend へ届ける経路を正式に用意し、ポーリングを保険に格下げします。

| 要件 | 実装 |
|---|---|
| `OnVideoReady` | `VideoEventBridge.OnVideoReady()` → `VRChatVideoBackend.NotifyVideoReady()` |
| `OnVideoStart` | `VideoEventBridge.OnVideoStart()` |
| `OnVideoEnd` | `VideoEventBridge.OnVideoEnd()` |
| `OnVideoError` | `VideoEventBridge.OnVideoError(VideoErrorKind)` |
| `OnVideoLoop` | `VideoEventBridge.OnVideoLoop()`(記録するが状態は変えない) |
| `VRChatVideoBackendHost` | イベントの受け口をブリッジ経由に変更 |
| **イベントブリッジ** | **`VideoEventBridge`(純粋 C#)— Phase3-2 の中心** |
| Backend への通知 | ブリッジ → `NotifyVideoXxx` → `IVideoBackendObserver` → `VideoBackendAdapter` |

### 変更していないもの(要件どおり)

`PlayerSession` / `Queue` / `Recommendation` / `BackendAdapter`(`VideoBackendAdapter`)は
**1 行も変更していません**(`git diff` で確認済み)。
Recommendation・Playlist・検索・StringDownloader・VRCUrlInputField・ネットワーク・
同期・UI・自動URL生成のいずれにも触れていません。

---

## 1. なぜ「ブリッジ」が要るのか

VRChat の動画プレイヤーは、イベントを**同じ GameObject の UdonBehaviour** へ送ります。
一方 UdonSharp は **インターフェースを扱えず、任意の C# クラスのメソッドも呼べません**
(Udon から呼べるのは許可された Unity API と、他の UdonSharpBehaviour だけ)。

つまり実機のイベントは、そのままでは `VRChatVideoBackend`(純粋 C#)へ届きません。
ここを繋ぐのが Phase3-2 です。

```mermaid
flowchart LR
    VP["VRCUnityVideoPlayer /<br>VRCAVProVideoPlayer"]

    subgraph SDK["SDK 依存（運ぶだけ・判断しない）"]
        RELAY["<b>UdonVRCVideoEventRelay</b><br>UdonSharpBehaviour<br>OnVideoReady/Start/End/Error…<br>を int で記録"]
        PUMP["<b>UdonVideoEventPump</b><br>差分を読み出す<br><i>SDK の型に依存しない</i>"]
        HOST["VRChatVideoBackendHost<br>OnVideoXxx()"]
    end

    subgraph PURE["純粋C#（判断はすべてここ）"]
        CODEC["VideoEventCodec<br>int → 種別/理由"]
        BRIDGE["<b>VideoEventBridge</b><br>重複排除・Tick 調停・証跡"]
        BE["VRChatVideoBackend<br>NotifyVideoXxx()"]
    end

    AD["VideoBackendAdapter<br><b>（変更なし）</b>"]
    PS["PlayerSession<br><b>（変更なし）</b>"]

    VP -->|SendCustomEvent| RELAY
    RELAY -->|int[] リングバッファ| PUMP
    PUMP --> CODEC --> BRIDGE
    HOST --> BRIDGE
    BRIDGE --> BE
    BE -->|IVideoBackendObserver| AD -->|BackendEvent| PS

    BRIDGE -.->|保険| TICK["Tick（ポーリング）"]
    TICK -.-> BE

    style BRIDGE stroke-width:3px
    style TICK stroke-dasharray: 5 5
```

**要点**:SDK 側の 3 つは「運ぶだけ」で、**何をどう扱うかの判断は 1 つも持っていません。**
判断はすべて純粋 C# の `VideoEventCodec` と `VideoEventBridge` にあり、EditMode テストで検証できます。

---

## 2. ディレクトリ構成(追加・変更分)

```
Assets/SmartMediaPlatform/Video/
├── Runtime/                                    ← 純粋C#(noEngineReferences: true)
│   ├── IVideoEventSink.cs                      ★ VideoEventKind + イベントの受け口
│   ├── VideoEventBridge.cs                     ★ 本体(重複排除・Tick 調停・証跡)
│   ├── VideoEventCodec.cs                      ★ Udon の int 符号 ⇄ 種別/理由
│   └── VRChatVideoBackend.cs                   ◎ Notify* が bool を返すように + Error 重複排除
├── Udon/                                       ★ 新設(asmdef 無し = Assembly-CSharp)
│   └── UdonVRCVideoEventRelay.cs               ★ 実機イベントを受け取る唯一の場所
├── VRChat/Runtime/
│   ├── UdonVideoEventPump.cs                   ★ 中継 → ブリッジ の運搬役
│   ├── VRChatVideoEventSceneDemo.cs            ★ DemoScene の進行役
│   └── VRChatVideoBackendHost.cs               ◎ 受け口をブリッジ経由へ
├── Demo/
│   └── VRChatVideoEventBridgeConsoleDemo.cs    ★ ConsoleDemo(SDK 不要・6 シナリオ)
├── Scenes/
│   └── Phase3VRChatVideoEventDemoScene.unity   ★ ConsoleDemo 用シーン
├── Editor/
│   └── Phase3VRChatVideoEventSceneBuilder.cs   ★ シーン生成(SDK シーンも)
└── Tests/EditMode/
    └── VideoEventBridgeTests.cs                ★ 38 ケース

★ = 新規 / ◎ = 変更
```

`Video/Udon/` は既存の `Catalog/Udon` `Recommendation/Udon` と同じく **asmdef を持ちません**
(UdonSharp は既定アセンブリで動かす必要があるため)。Phase1-2 から一貫した配置です。

---

## 3. `VideoEventBridge` が引き受ける 3 つの仕事

### 3.1 重複排除 — 「二重発火しない」

判定そのものは `VRChatVideoBackend` の状態遷移が持っています。
Phase3-2 では `NotifyVideoXxx` が **「受理したか」を bool で返す**ようにし、
ブリッジがそれを集計・記録します。

```csharp
private bool Dispatch(VideoEventKind kind, Func<bool> notify)
{
    var before = _backend.GetState();
    EventsObserved = true;              // イベントが届いた事実はここで確定

    bool accepted = notify();           // ← 状態遷移が重複を弾く
    var after = _backend.GetState();

    if (accepted) { _accepted[(int)kind]++; ... }
    else          { _rejected[(int)kind]++; ... }   // ← 二重発火を防いだ回数

    Append(new VideoEventRecord { Kind = kind, Accepted = accepted, Before = before, After = after, ... });
    return accepted;
}
```

| イベント | 2 回目が棄却される理由 |
|---|---|
| `OnVideoReady` | 状態が `Loading` でなくなっているため |
| `OnVideoStart` / `OnVideoPlay` | すでに `Playing` のため |
| `OnVideoPause` | すでに `Playing` ではないため |
| `OnVideoEnd` | すでに `Finished` のため |
| `OnVideoError` | **Phase3-2 で追加**(下記) |
| `OnVideoLoop` | 状態を変えないので常に「受理しない」 |

**`OnVideoError` の重複排除は Phase3-2 で新たに入れた唯一の判定です。**
Phase3-1 の `NotifyVideoError` は無条件に `Fail()` を呼んでいたため、
VRChat が同じ失敗を複数回送ると `BackendEventType.Error` が重複して上位へ届いていました。

```csharp
public bool NotifyVideoError(VideoErrorKind kind, string message = null)
{
    if (_state == VideoPlayerState.Failed && LastErrorKind == kind)
    {
        Log($"エラー通知を無視: {kind} は既に報告済みです");
        return false;
    }
    Fail(_url, kind, message ?? DescribeError(kind));
    return true;
}
```

**違う理由の失敗は握りつぶしません**し、`Load` し直せば同じ理由でも改めて報告します。
検証: `RepeatedIdenticalError_NotifiesOnlyOnce` / `DifferentError_IsStillReported` /
`ErrorAfterReload_IsReportedAgain`。

### 3.2 `Tick()` との調停 — 「競合しない」

```csharp
public void Tick(float deltaSeconds = 0f)
{
    if (SuppressPollingWhenEventsArrive && EventsObserved && _backend.DetectEndByPolling)
    {
        _backend.DetectEndByPolling = false;   // ← ポーリングによる「終了の推測」を降ろす
        Log("イベントが届いているので、ポーリングによる再生終了の推測を止めます");
    }
    _backend.Tick(deltaSeconds);
}
```

- **イベントが 1 つでも届いたら**、ポーリングによる**再生終了の推測だけ**を止めます。
- **読み込みタイムアウトの監視は残します。** イベントもエラーも来ないまま
  読み込みが終わらない場合の最後の砦だからです(検証: `Tick_KeepsWatchingForTimeoutEvenWhenEventsArrive`)。
- イベントが来ない環境では Phase3-1 とまったく同じ動作のままです
  (検証: `PollingStillWorksWhenNoEventArrives`)。

**どちらが先に気づいても通知は 1 回だけ**です。順序を入れ替えたテストも用意しています
(`EventAndTick_TogetherStillNotifyEndOnce` / `TickFirstThenEvent_StillNotifiesEndOnce`)。

### 3.3 証跡 — 「イベントで動いたこと」を証明する

ポーリングでも同じ結果になるため、**「イベントで動いた」と言い切るには証跡が要ります。**

| メンバー | 意味 |
|---|---|
| `EventsObserved` | 実機イベントが 1 度でも届いたか |
| `AcceptedCount(kind)` / `RejectedCount(kind)` | 種類ごとの受理/棄却回数 |
| `TotalAccepted` / `TotalRejected` | 合計 |
| `LastAccepted` / `LastReceived` | 直近のイベント |
| `Log` | 直近 32 件の `VideoEventRecord`(種別・受理可否・前後の状態・棄却理由) |
| `Describe()` | Console 用 1 行サマリ |

`Log` は `Load` のたびに消えます(`VRChatVideoBackend.LoadGeneration` の変化を見る)。
前の動画のイベントが次の動画の記録に混ざらないようにするためです。

---

## 4. Udon からどうやって運ぶか

UdonSharp は インターフェース を扱えないので、**中継は「記録するだけ」**にしました。

### 4.1 `UdonVRCVideoEventRelay`(UdonSharp)

VRCUnityVideoPlayer と**同じ GameObject** に置くだけです。設定項目はありません。

```csharp
public override void OnVideoReady() { Push(CodeReady); }   // 1
public override void OnVideoStart() { Push(CodeStart); }   // 2
public override void OnVideoEnd()   { Push(CodeEnd); }     // 5
public override void OnVideoError(VideoError videoError)
{
    LastErrorCode = (int)videoError;
    Push(CodeErrorBase + LastErrorCode);                   // 100 + n
}

private void Push(int code)
{
    EventCodes[WriteCount % EventCodes.Length] = code;      // リングバッファ
    WriteCount++;                                           // 累計(取りこぼし検知に使う)
}
```

### 4.2 `UdonVideoEventPump`(C#)

`WriteCount` と `EventCodes` を読み、**前回からの差分だけ**を順番に流します。

読み出しは **VRChat SDK の型に依存しません**。中継は `MonoBehaviour` として受け取り、
2 つの経路をリフレクションで試します。

| 経路 | 対象 | 方法 |
|---|---|---|
| 1 | `UdonBehaviour`(Udon プログラム) | `GetProgramVariable(string)` をメソッド名と引数で探して呼ぶ |
| 2 | `UdonSharpBehaviour` のプロキシ | `public` フィールドを直接読む |

`VRC.Udon.UdonBehaviour` を型で参照すると、この asmdef から Udon アセンブリを
参照できる構成でないとコンパイルが通りません。SDK の配布形態(DLL / asmdef /
Auto Reference の有無)はバージョンで変わるため、**型依存を持たないほうが壊れにくい**という判断です。
SDK のバージョン差をリフレクションで吸収するのは、Phase1-2 の
`UdonCatalogBaker.SyncUdonSharpProxy` と同じ考え方です。

```csharp
while (_consumed < writeCount)
{
    int code = codes[_consumed % codes.Length];
    VideoEventCodec.Deliver(sink, code);    // ← 解釈は純粋C#側
    _consumed++;
}
```

**これはポーリングではありません。** `IsPlaying` を見て「終わったはず」と推測するのではなく、
**VRChat が実際に発火したイベントを、起きた順にそのまま運んでいます。**
累計カウンタの差分を消費するので取りこぼしも二重取得も起きません
(1 フレームに 32 件を超えた場合のみ `DroppedCount` に計上)。

### 4.3 `VideoEventCodec`(純粋 C#)

int ↔ 種別/理由 の対応はここ 1 箇所です。EditMode テストで往復を確認しています。

| 符号 | 意味 |
|---|---|
| 1〜6 | `VideoEventKind.Ready` 〜 `Loop`(enum の値と一致) |
| 100 + n | エラー。n は SDK の `VideoError` の値(0=Unknown, 1=InvalidURL, 2=AccessDenied, 3=PlayerError, 4=RateLimited) |

---

## 5. テスト方法

### A. EditMode 自動テスト(SDK 不要)

`Window > General > Test Runner` → **EditMode** → `SmartMediaPlatform.Video.Tests` を **Run All**。

**38 ケース**が追加され、プロジェクト全体で **568 ケース**になります。

| 分類 | 主なテスト |
|---|---|
| イベントが届く | `OnVideoReady_ReachesTheBackend` / `OnVideoStart_…` / `OnVideoEnd_…` / `OnVideoError_…` / `OnVideoPause_…` / `OnVideoPlay_ResumesFromPause` |
| 二重発火しない | `RepeatedReady_NotifiesOnlyOnce` / `RepeatedStart_…` / `RepeatedEnd_…` / `RepeatedIdenticalError_…` |
| 順序が乱れても壊れない | `OutOfOrderEvents_AreRejectedWithoutBreakingState` |
| Tick と競合しない | `Tick_StopsGuessingTheEndOnceEventsArrive` / `EventAndTick_TogetherStillNotifyEndOnce` / `TickFirstThenEvent_StillNotifiesEndOnce` / `Tick_KeepsWatchingForTimeoutEvenWhenEventsArrive` / `PollingStillWorksWhenNoEventArrives` |
| Udon 経路 | `Codec_RoundTripsEveryEventKind` / `Codec_RoundTripsEveryErrorKind` / `Codec_TranslatesTheSdkErrorNumbers` / `Codec_DeliversAnUdonEventStreamInOrder` |
| 上位は無変更 | `PlayerSession_AdvancesFromAnEventWithoutAnyChanges` / `Adapter_TranslatesEventDrivenEndUnchanged` / `Adapter_TranslatesEventDrivenErrorsUnchanged` |
| 証跡 | `Log_RecordsAcceptedAndRejectedEvents` / `Log_IsClearedWhenANewVideoIsLoaded` / `Log_IsCapped` |

### B. ConsoleDemo(SDK 不要)

メニュー **Tools > Smart Media Platform > Open Phase3-2 Video Event Demo Scene** → **Play**。

| # | 内容 |
|---|---|
| 1 | 4 つのイベントが Backend へ届く |
| 2 | 同じイベントを 3 回ずつ送っても、上位への通知は 1 回だけ |
| 3 | Tick とイベントが競合しない(イベントが来る/来ない両方) |
| 4 | Error の重複(同じ失敗は 1 回、違う失敗は通す) |
| 5 | Udon から運ばれてくる int 符号の経路 |
| 6 | PlayerSession / BackendAdapter は無変更のまま自動送りする |

### C. DemoScene(VRChat SDK で実機イベント)

メニュー **Tools > Smart Media Platform > Create Phase3-2 VRChat SDK Video Event Scene (実機イベント)**。

生成されるシーンの `VRCVideoPlayer` GameObject:

- `VRCUnityVideoPlayer` … 本物の動画プレイヤー
- **`UdonVRCVideoEventRelay`** … イベントを受け取る Udon 中継
- `UdonVideoEventPump` … 中継 → ブリッジ の運搬役
- `VRChatVideoBackendHost` … Backend + ブリッジの組み立て
- `VRChatVideoEventSceneDemo` … 進行役(Console 出力)

Play すると、`OnVideoReady` / `OnVideoStart` / `OnVideoEnd` / `OnVideoError` の
**受理回数と棄却回数**、`EventsObserved`、`DetectEndByPolling` が Console に出ます。
中継が繋がっていない場合はその旨をはっきり出力するので、
**ポーリングで動いているのを「イベントで動いた」と誤認することはありません。**

### 成功条件の対応表

| 成功条件 | 確認方法 |
|---|---|
| VideoPlayer のイベントが Backend へ届く | Console B-1 / C-1 / `OnVideoReady_ReachesTheBackend` ほか |
| OnVideoReady / Start / End / Error が動作する | 同上(4 種すべてに個別テストあり) |
| 通知が重複しない | Console B-2・B-4 / `Repeated*_NotifiesOnlyOnce` |
| Tick と競合しない | Console B-3 / `EventAndTick_TogetherStillNotifyEndOnce` |
| DummyBackend を差し替えただけで動作する | Phase3-1 の `BothBackends_BehaveIdenticallyThroughTheSameScript` は無改変で通ります |
| PlayerSession・Queue・Recommendation・BackendAdapter を変更しない | `git diff` に該当ディレクトリ・`VideoBackendAdapter.cs` の差分なし |
| 既存テストを壊さない | EditMode 568 ケース全実行 |
| ConsoleDemo と DemoScene の両方 | B と C |

---

## 6. Phase3-1 からの変更点(Video 配下のみ)

| ファイル | 変更 | 互換性 |
|---|---|---|
| `VRChatVideoBackend.NotifyVideoXxx` | `void` → `bool` | **ソース互換**(既存の呼び出しはすべて文としての呼び出し) |
| `VRChatVideoBackend.NotifyVideoError` | 同じ理由の連続は無視 | 二重発火の修正。違う理由・再読み込み後は従来どおり |
| `VRChatVideoBackend.LoadGeneration` | 追加(読み取り専用) | 追加のみ |
| `VRChatVideoBackendHost` | 受け口をブリッジ経由に。`Update()` が `Bridge.Tick()` を呼ぶ | 公開 API の形は不変 |

Phase3-1 の既存テスト(56 ケース)は**書き換えずにそのまま通る**設計にしています。

---

## 7. 設計レビュー(自己評価)

### 良かった点

- **判断ロジックを SDK 側に 1 つも置かなかった。** `UdonVideoEventPump` は「読んで渡す」だけ、
  `UdonVRCVideoEventRelay` は「記録する」だけ。おかげで重複排除も符号化も EditMode で検証できる。
- **「イベントで動いた」ことを証明できるようにした。** ポーリングでも結果は同じになるので、
  `EventsObserved` と受理/棄却の集計が無ければ検証にならない。
- **ポーリングを消さずに格下げした。** 中継の配線を忘れても再生は壊れない。
- **累計カウンタ方式の中継**にしたので、取りこぼしも二重取得も起きない。

### 気になっている点(正直な申告)

1. **Udon → C# の受け渡しがリフレクション頼みです。**
   UdonSharp が インターフェース を扱えない以上、この受け渡しは
   `GetProgramVariable` かフィールド直読みしかありません。
   SDK の型に依存しない形にして壊れにくくはしましたが、
   **この環境では実行検証できていません**(§8)。
   どちらの経路も使えない場合は `UdonVideoEventPump.ConnectionDescription` が
   「未接続」と出て、ポーリング(保険)にフォールバックします。

2. **1 フレームに 32 件を超えるイベントが来ると取りこぼします。**
   実際には起こらない想定ですが、`UdonVideoEventPump.DroppedCount` で検知できるようにしました。
   起きるようならリングバッファ長を増やしてください。

3. **中継とプレイヤーが同じ GameObject にある必要があります。**
   VRChat の仕様なので避けられませんが、配線ミスに気づきにくいので、
   DemoScene では「未接続」を明示するようにしました。

4. **`OnVideoLoop` は記録するだけです。** `VRChatVideoBackend` は `Loop = false` を
   強制しているので通常は届きません。届いた場合は設定ミスの手がかりになります。

---

## 8. 確認できていないこと

- **コンパイルとテストの実行**:この作業環境に Unity / .NET SDK / VRChat SDK が無いため、
  **一度も実行検証できていません。** 括弧対応と状態遷移の机上確認のみです。
- **`GetProgramVariable` / フィールド直読みの実挙動**、および UdonSharp の
  `OnVideoReady` などのオーバーライドが期待どおり呼ばれるか。
- **`UdonSharpEditorUtility.CreateBehaviourForProxy` の有無**(リフレクションで呼び、
  見つからなければスキップするようにしてあります)。

---

## 9. Phase3-3 以降への引き継ぎ

| 項目 | 内容 |
|---|---|
| 読み込みレート制限 | VRChat は短時間の連続 `LoadURL` を `RateLimited` で弾く。連続再生では再試行層が要る(未実装) |
| 中継の共有 | 動画プレイヤーが複数ある場合、中継も 1 つずつ要る。まとめ方は未検討 |
| 同期 | 複数クライアント間の再生位置同期は Phase5。今回は単独クライアント内のイベント配線のみ |
| アダプタの一本化 | `DummyVideoBackendAdapter` の廃止(Phase2-4A、要承認) |
| `RecommendationEngine` の interface 化 | 投票・AI 推薦の前提(要承認) |
