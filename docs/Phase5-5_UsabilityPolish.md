# Phase5-5 — 仕上げ(操作性・見やすさ・扱いやすさ)

**目的**: 実際に VRChat ワールドへ置いて**快適に使える**状態にすること。
新しい大きな機能は足さず、**触り心地**を上げるフェーズです。

---

## 1. 改善した UX

### A. 縦長 1 列 → 横長 2 列(いちばん効いた変更)

Phase5-3 の壁パネルは **900 × 1390 px ≒ 1.17 m × 1.81 m の縦長**でした。
これを目の高さに合わせて置くと、**下半分(Queue と Status)が膝の高さ**に来ます。
VR で膝を狙うのは、しゃがむか腕を下げるかしかなく、実際に使いにくい形でした。

いまは **1400 × 980 px ≒ 1.82 m × 1.27 m の横長 2 列**です。

```
┌───────────────────────────────────────────────┐
│ Smart Media Player               操作中: ○○  │
├──────────────────┬────────────────────────────┤
│ いま鳴っているもの │ Library      1〜6 / 24 件 ▲▼│
│ ◀◀  ▶ 再生  ▶▶  │ ──────────────────────────  │
│ ■ − 60% ＋ Queueを空に│ (6 行)                 │
│ Queue        ▲ ▼ │ 関連          1〜3 / 3 件  │
│ (4 行)            │ (3 行)                     │
│ 状態の 1 行        │                            │
└──────────────────┴────────────────────────────┘
```

中心を **1.35 m** に置くと、上端 1.99 m / 下端 0.72 m。
**全部が胸〜目の高さ**に収まります。

左の列は「見る」と「押す」を上から順に:
**いま鳴っているもの → 再生操作 → Queue**。いちばん使うものがいちばん上です。

### B. VR で押しやすい大きさ

| 部品 | Phase5-3 | Phase5-5 | 実寸 |
| --- | --- | --- | --- |
| 一覧の行 | 56 px | **72 px** | 9.4 cm |
| 再生 / 一時停止 | 68 px 高 | **88 px 高** | 11.4 cm |
| 停止・音量 | 52 px 高 | **76 px 高** | 9.9 cm |
| スクロール ▲▼ | 38 px 高 | **56 px 高** | 7.3 cm |
| 行の ＋ / × | 73 × 56 | **94 × 72** | 12 × 9.4 cm |

**組み立て時に実寸を測って警告を出すようにしました。**
`UdonWorldUiKit.ComfortableTouchMeters`(4.5 cm)を下回るボタンがあると、
Console に「○○ は 44 mm しかありません」と出て、件数が組み立て報告に載ります。

> レーザーで狙うだけなら 2 cm 角でも当たります。それでも 4.5 cm を基準にしたのは、
> **腕を伸ばした姿勢や、歩きながら**だと当たらないからです。

### C. ページ送り → スクロール

ページ送りには 2 つ不満がありました。

- **最終ページが半端に空く。** 22 件を 6 行で見ると、4 ページ目は 4 件で下 2 行が空白
- **1 件だけ先が見られない。** 6 件単位でしか動けない

いまは「先頭に見えている位置」だけを持つスクロールです。

- 最後まで送っても**行が埋まったまま**
- **半画面ずつ**動くので、見ていた行が半分残って位置を見失わない(Library の既定)
- **「7〜12 / 24 件」** と、いま何番目を見ているかが出る
- 右端に**細いつまみ**。長さが「見えている割合」、位置が「どこまで送ったか」
- 端では ▲ / ▼ が消える(押せないボタンを押させない)

正典は `World/UdonModel/ListScrollModel.cs`(**EditMode 22 ケース**)。
Phase5-3 の `ListPageModel` は役目を終えたので削除しました。

### D. 選択・状態が見て分かる

| 状態 | 見え方 |
| --- | --- |
| **いま鳴っている行** | 左端に**縦棒**(青)+ 行全体に薄い色 + 見出しが白く明るくなる |
| **押した直後** | 行が **0.6 秒だけ白く光る** |
| 行が空 | 中身ごと消える(押せない) |
| Queue の先頭 | `×` が出ない(いま鳴っているものは外せない) |
| 1 行おき | 背景の濃さをわずかに変える(目が横に滑らないように) |

**押した直後の光りが地味に重要**です。
uGUI の色変化(ColorTint)は **VRChat の「使う」で押したときには出ません**。
押せたのか押せていないのか分からないと何度も押してしまうので、
**どちらの押し方でも手応えが返る**ようにしました。

### E. Now Playing が読みやすい

| 追加・変更 | 中身 |
| --- | --- |
| 見出し | 36 → **38 pt**、アーティストも 22 → 24 pt |
| **残り時間** | 「残り 2:14」。「次はいつ?」に一番よく答える表示 |
| **読み込み中** | 再生中のはずなのに動いていないときに出す。無音の数秒を「壊れた?」と思わせない |
| 長さ | 動画から取れなければカタログの値を使う(`--:--` になりにくい) |
| 進捗バー | 12 → **16 px** に |

### F. Queue が迷子にならない

Queue の下のほうを眺めている最中に曲が変わると、
先頭(= いま鳴っているもの)が画面外へ出てしまいます。

**曲が変わったときだけ**先頭へ戻すようにしました(`FollowNowPlaying`)。
毎回追いかけると、眺めている最中に画面が飛んで操作できなくなるので、
**変わった瞬間だけ**です。

---

## 2. Prefab 変更点

### 変わったもの

| Prefab | 変更 |
| --- | --- |
| `SmartMediaPlayer.prefab` | 壁パネルが **2 列**に。既定位置を `(-2.8, 1.35, 0)` へ |
| `MediaWallPanel.prefab` | 同上 |
| `MediaRemotePanel.prefab` | **760 × 770 px ≒ 0.65 m 角**に拡大(ボタンが 4.5 cm を割っていた) |

### 一覧の中身(名前が変わったもの)

```
Library                     UdonMediaListView
├── Header        Text
├── Range         Text       ← 【新規】「7〜12 / 24 件」
├── ScrollUp      Button     ← 旧 PreviousPage
├── ScrollDown    Button     ← 旧 NextPage
├── Rule          Image      ← 【新規】区切り線
├── ScrollTrack   Image      ← 【新規】つまみの下地
│   └── Handle    Image      ← 【新規】つまみ
├── Empty         Text
└── Row0..RowN    UdonMediaListRow
    ├── Content
    │   ├── Hit        Button(1 行おきに濃さが違う)
    │   └── Secondary  Button
    ├── Highlight      Image  ← 鳴っている行の薄い色
    ├── Pressed        Image  ← 【新規】押した直後だけ光る
    └── NowPlayingBar  Image  ← 【新規】左端の縦棒
```

**`ScrollUp` / `ScrollDown` は旧名でも動きます。**
`UdonMediaListView` に `NextPage` / `PreviousPage` を残してあり、
`操作 UI の配線を繋ぎ直す` も旧名の GameObject を受け付けます。
**すでにワールドへ置いた Prefab は、作り直さずに繋ぎ直しだけで使えます。**

### Now Playing

```
NowPlaying                  UdonNowPlayingView
├── Title / Artist   Text
├── Progress → Fill  Image
├── Time             Text
├── Remaining        Text    ← 【新規】「残り 2:14」
└── State            Text
```

`QueueCount` は 3 分割の枠から外れましたが、フィールドは残してあるので
自分のレイアウトで使えます。

---

## 3. Inspector 変更点

**「まず触る欄」を上に、「Prefab が配線済みの欄」を下に**並べ直しました。

### `UdonSmartMediaPlayer`

```
★ よく触る設定     PlayOnStart / StartDelay(スライダー 0〜10 秒)
データ             Catalog / Store / Recommendation
部品(差し替え可能) Session / Backend / Screen / Controller / Sync
困ったとき          LogWiring
```

### `UdonMediaPanel`

```
★ ここだけ埋めれば動く   Core
見た目                   PanelName / OpenOnStart / RefreshInterval(0〜2 秒)
差し替えたいとき          Controller / Session / Store / Screen
中身(Prefab が配線済み)  NowPlaying / Transport / Lists / StatusText / TitleLabel / Body
```

### `UdonSyncCoordinator`

```
★ よく触る設定   Enabled / AccessPolicy(0〜2 のスライダー)
つなぎ先          Session / Backend / Controller
合わせ方          DriftTolerance / CheckInterval
困ったとき        LogSync
```

### 増えた項目

| コンポーネント | 項目 | 既定 | 意味 |
| --- | --- | --- | --- |
| `UdonMediaListView` | `Offset` | 0 | 先頭に見えている位置 |
| | `ScrollStep` | 0 | ▲▼ 1 回で動く行数。**0 なら 1 画面**。Library は半画面 |
| | `FollowNowPlaying` | false | 曲が変わったら鳴っている行まで戻す(Queue は true) |
| | `TouchFeedbackSeconds` | 0.6 | 押した行が光る時間 |
| `UdonMediaListRow` | `TitleColor` / `NowPlayingTitleColor` | — | ふだんの色 / 鳴っている行の色 |
| `UdonNowPlayingView` | `LoadingLabel` | 読み込み中… | 再生中なのに動いていないときの文言 |

### 消えた項目

| 項目 | 代わり |
| --- | --- |
| `UdonMediaListView.Page` | `Offset` |
| `UdonMediaListView.PageText` | `RangeText` |
| `UdonMediaListView.PreviousPageButton` / `NextPageButton` | `ScrollUpButton` / `ScrollDownButton` |

---

## 4. VRChat で確認すべき項目

### A. 置いた直後

1. パネルの**上端が目の高さ、下端が腰の高さ**に収まっているか
2. **膝を狙わないと押せないボタンが無い**か
3. 離れて(2〜3 m)立ったとき、Library の見出しが読めるか
4. Console の組み立て報告に「VR で押しやすさ: すべて 45 mm 以上」と出たか

### B. スクロール

5. Library の ▼ を押すと**半分だけ**送られるか(見ていた行が半分残る)
6. 「1〜6 / 24 件」→「4〜9 / 24 件」と表示が変わるか
7. **いちばん下まで送っても行が空かない**か
8. 端まで行くと ▲ か ▼ が**消える**か
9. 右端のつまみが動くか。件数が少ないときは**つまみが満杯**になるか

### C. 状態の見え方

10. いま鳴っている行の**左端に青い縦棒**が出るか
11. その行の見出しが**ほかより明るい**か
12. 行を押した瞬間、**その行が一瞬白く光る**か
13. **「使う」で押したときも光る**か(← ここが Phase5-3 で欠けていた)
14. 1 行おきに背景の濃さが違うか

### D. Now Playing

15. 「残り 2:14」が出て、**減っていく**か
16. 曲を切り替えた直後に「読み込み中…」が出て、始まったら「▶ 再生中」に変わるか
17. 進捗バーが伸びるか

### E. Queue の追従

18. Queue を下までスクロールした状態で曲が変わると、**先頭へ戻る**か
19. スクロールしただけでは**勝手に戻らない**か

### F. リモコン

20. `MediaRemotePanel` が **0.65 m 角**で、手に持てる感じか
21. リモコンのボタンも全部押せるか(小さすぎないか)

### G. こわれていないこと(Phase5-3 / 5-4 の再確認)

22. 再生 / 一時停止 / 次へ / 前へ / 停止が効くか
23. 2 人で入って**同じ動画・同じ位置**か
24. 途中参加した人が追いつくか

### H. すでに置いてある Prefab を作り直さない場合

25. `セットアップ > 操作 UI の配線を繋ぎ直す` で、旧 `NextPage` / `PreviousPage` が
    `ScrollDown` / `ScrollUp` として繋ぎ直されるか
26. ただし**見た目は Phase5-3 のまま**です。2 列レイアウトが欲しければ作り直しが要ります

---

## 5. 正式版までに残る課題

### UI / UX

| 課題 | いまの状態 | やりたいこと |
| --- | --- | --- |
| **文字が Legacy Font** | `LegacyRuntime.ttf` の動的フォント | TextMeshPro にすると遠くでもぼやけない。ただし Udon での扱いが増える |
| **検索・絞り込みが無い** | Library は並び順のまま | ジャンル / アーティストで絞る行を 1 本足す(`UdonMediaListView` の `Source` を増やすだけ) |
| **サムネイルが無い** | 文字だけ | カタログに画像を焼き込む仕組みが要る(Catalog Builder 待ち) |
| **シークできない** | 進捗バーは表示だけ | バーを押した位置へ飛ぶ。同期側は `_basePositionMs` を書き換えるだけで足りる |
| **音量が手元だけ** | 各自の `UdonMediaScreen` | 「全体音量」と「自分の音量」を分ける |
| **パネルの開閉ボタンが Prefab に無い** | `UdonMediaPanel.Toggle` はある | リモコンに開閉ボタンを付けた Prefab を用意する |
| **長い見出しが切れる** | 1 行で切り捨て | 流れる表示(マーキー)か 2 行折り返し |

### 同期(Phase5-4 からの持ち越し)

| 課題 | やりたいこと |
| --- | --- |
| 位置合わせが `SetTime` で一瞬飛ぶ | 小さいずれは**再生速度 0.95〜1.05 倍**で滑らかに寄せる |
| 全員が同じ瞬間に再生し始めない | 「この時刻に再生開始」を予告して**待ち合わせる** |
| 音ズレ | AVPro の音声遅延を別に補正 |

### 仕組み

| 課題 | 中身 |
| --- | --- |
| **uGUI が実機で効くか未確認** | Phase5-3 で uGUI のレイキャストが通らず、Collider +「使う」を併用する形になった。どちらで押されているかは実機でしか分からない |
| **Prefab の作り直しが必要な変更が多い** | レイアウトを変えるたびに作り直しになる。位置だけ引き継ぐ仕組みがあると楽 |
| **カタログのバージョン照合が無い** | 全員が同じカタログを持っている前提。サーバー連携時は `_catalogVersion` の同期が要る |
| **EditMode で UI を検証できない** | `UdonSharpBehaviour` は触れないので、数え方を正典へ写す方式に頼っている |

---

## 6. 検査

```bash
python3 tools/typecheck/genmeta.py      # .meta が揃っているか
python3 tools/typecheck/typecheck.py    # No compile errors.
python3 tools/typecheck/runtests.py     # 887 passed, 0 failed
python3 tools/typecheck/udon_lint.py    # 18 ファイル / 問題なし
```

Phase5-4 の 881 ケースから、`ListPageModelTests`(16)を落として
`ListScrollModelTests`(22)を足し、**887** です。
