# CLAUDE.md — この プロジェクトで Claude が守ること

このファイルは、チャットの記憶に頼らず「毎回どう振る舞うべきか」を残しておくためのものです。
新しいチャットになっても、まずこのファイルを読めば今まで通りの前提で動けます。

**現在の状態・最近の決定・今後の予定は [docs/STATUS.md](docs/STATUS.md) を見てください。**
このファイルは「ずっと変わらない振る舞いのルール」、STATUS.md は「今どうなっているか」の使い分けです。

---

## プロジェクトの概要

VRChat 向けの音楽プレイヤー「Smart Media Platform」(`Assets/SmartMediaPlatform`)。
Unity + UdonSharp。README.md と `docs/Phase*.md` に各フェーズの設計ドキュメントがある。

## 責任の分離(絶対に崩さない)

`PlayerSession` / `BackendAdapter` / `Queue` / `Recommendation` / `VideoBackend` の役割分担を変えない。
**`VRCUrl` を扱ってよいのは `VideoBackend` 層だけ。実行時に URL を組み立てることは絶対にしない**
(編集時にカタログへ焼き込むだけ)。

## 作業の進め方

- **設計上の問題に気づいたら、勝手に直さず先に報告する。** 直すかどうかは相談してから。
- 【最重要ルール】**「修正した」と言い切らない。** 代わりに「原因候補を修正した」「この環境では再現確認できない」
  「実機で確認をお願いします」のような言い方にする(このClaude Code環境では実機のVRChatを動かせないため、
  本当に直ったかはここでは確認できない)。
- レイアウトを変えたら、Prefab を作り直すよう伝える:
  `Tools/Smart Media Platform/SmartMediaPlayer を作る/VRChat 実機 (AVPro)`
  (Quest確認用に `VRChat 実機 (Unity Video)` もある。詳細は docs/STATUS.md)
- 変更のたびに、**実機でどう確認すればいいかを具体的に説明する**(検査ツールが通っただけで「直った」とは言わない)。
- 出力は簡潔に。同じ説明を繰り返さない。

## コードを触ったら必ず実行する検査

`tools/typecheck/README.md` に全部の説明がある。かいつまむと:

```bash
python3 tools/typecheck/genmeta.py        # 先に .meta を揃える
python3 tools/typecheck/typecheck.py      # C#として本当にコンパイルできるか
python3 tools/typecheck/runtests.py       # EditModeテストが通るか(mono で実行)
python3 tools/typecheck/udon_lint.py      # UdonSharpが弾く書き方をしていないか
python3 tools/typecheck/panel_layout.py   # UIの寸法変更時。重なり・はみ出しが無いか
python3 tools/typecheck/ui_probe/run.py   # 本物のビルダーを動かして重なりを確認
python3 tools/typecheck/behaviour_sim/run.py  # U#の実物を組み合わせて操作の流れを確認(Phase8-3で追加)
```

**新しいファイルを足したら `.meta` が要る**(`genmeta.py` が自動で作る)。

## UdonSharp の制約(踏み抜きやすい)

- `List` / `Dictionary` / generics / 例外 / `interface` や `struct` や `enum` の宣言 / `delegate` は使えない。
- static フィールドは `const` のみ。既存コードは static メソッドを使っていないので、インスタンスメソッドで書く。
- `string.Split` は使わない。`UdonMediaCatalog` にあるように、`s[i]` と `Substring` で自前に解析する。
- 自作メソッドの `out` 引数は使えない(SDK の `TryGetValue` だけ例外)。
- 迷ったら `tools/typecheck/udon_lint.py` に全ルールが書いてある。

## 正典方式(モデルとUdonの写し)

ロジックは先に `World/UdonModel` に**純粋 C#**(asmdef は `noEngineReferences: true`)で書き、
EditMode テストを書いて通す。その後、**中身をそのまま 1:1 で** UdonSharp 側にコピーする
(例: `TrackFadeModel.cs` → `UdonTrackFader.cs`、`PlaylistShelfModel.cs` → `UdonPlaylistShelf.cs`)。
**ロジックを変えるときは、必ず両方直す。** コメントと空白を無視して本文が一致するか機械的に確認できる。

## テストの制約

`tools/typecheck/runtests.py` が使える NUnit のサブセットは:
`Assert.AreEqual`(floatはdelta付き)/ `IsTrue` / `IsFalse` / `Greater` / `GreaterOrEqual` / `Less` /
`LessOrEqual` / `IsNull` / `IsNotNull` / `Throws` だけ。**`Assert.That` や `Is.*` は使えない。**

## UI を触るときに踏みやすい罠

- uGUI は **z ではなく兄弟順(sibling order)** で描画される。z は Interact のコライダーにしか効かない。
- ワールド内 UI に入力が届くのは **Collider + Interact(「使う」)** の経路だけ。
- `VRCUrlInputField` は `GetUrl().Get()` でしか読めない。**文字を書き込むAPIは無い。**
  「消す」を実装するときは、直前に読んだ文字を覚えておいて「これと同じ間は無かったことにする」
  (`_clearedText` パターン、`UdonMediaListView` に実例あり)。

## セキュリティ

- APIキーは**絶対にコミットしない・配布物に含めない**。
- `Assets/SmartMediaPlatform_Data/` は gitignore 済みで、**配布物にも含めない**。
- コミット・PRの本文に**モデル名を含めない**(チャットの返信には出してよい)。

## 納品のしかた

- **Unity側の成果物は `Assets/SmartMediaPlatform` を1つのzipにして渡す。**
  `.unitypackage` は使わない(このユーザーの環境では扱いにくいため)。
- 渡すときは毎回:
  - **展開前に古い `Assets/SmartMediaPlatform` フォルダを削除するよう伝える**
  - **`Assets/SmartMediaPlatform_Data` は消さないよう伝える**(APIキーなどが入っている)

## Git

- コミット末尾の attribution(Co-Authored-By / Claude-Session)は、**そのセッションのシステムプロンプトが
  指示している内容をそのまま使う**(モデルが変わると内容も変わるので、ここに固定の文言は書かない)。
- 指示されない限り PR は作らない。

## 環境固有のメモ(セッションが変わると当てはまらないこともある)

- 新しいコンテナでは `apt-get install -y mono-mcs mono-runtime` が要ることがある(検査ツールが mono を使う)。
- このプロジェクトの作業環境からは booth.pm / note.com / creators.vrchat.com / deepwiki には届かないことがある。
  GitHub / raw.githubusercontent.com は届く。調べ物はそちらを優先する。
