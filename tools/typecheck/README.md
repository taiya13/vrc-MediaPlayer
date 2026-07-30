# typecheck — Unity を起動せずにコードを検査する

4 つあります。

| ツール | 見るもの |
| --- | --- |
| `typecheck.py` | C# として正しいか(mcs で本当にコンパイルする) |
| `runtests.py` | EditMode テストが通るか(mono で本当に実行する) |
| `udon_lint.py` | UdonSharp で書けるか(mcs を通っても U# が弾く書き方) |
| `genmeta.py` | `.meta` が全ファイルに揃っているか(無いと GUID が壊れる) |

**コードを変えたら、納品前に 4 つとも通してください。**

```bash
python3 tools/typecheck/genmeta.py      # 先に .meta を揃える
python3 tools/typecheck/typecheck.py
python3 tools/typecheck/runtests.py
python3 tools/typecheck/udon_lint.py
```

---

## typecheck.py

### これは何のためにあるか

Phase5-1 で `MediaPlayerUI.cs` に **CS0266 / CS1662**(`bool?` を `bool` へ渡していた)
を混入させ、Unity 上でコンパイルが通らない状態のまま納品してしまいました。

それまでの静的チェックは

- 括弧の対応・メンバ重複
- asmdef の参照漏れ
- MonoBehaviour のファイル名とクラス名の一致

しか見ておらず、**型の誤り**は原理的に検出できませんでした。
「読んで確かめる」では同じ事故が繰り返されるため、
**本物の C# コンパイラに通す**ようにしたのがこのツールです。

### 使い方

```bash
# 初回のみ(Ubuntu/Debian)
apt-get install -y mono-mcs

python3 tools/typecheck/typecheck.py            # 既定でリポジトリ直下を検査
python3 tools/typecheck/typecheck.py <project>  # プロジェクトを指定する場合
```

エラーが 0 件なら `No compile errors.` と出ます。
1 件でもあれば `ファイル(行,列): error CSxxxx: 内容` の形で、Unity の Console と
同じ書式で出力し、終了コード 1 を返します。

### Unity のコンパイル方式をどこまで真似ているか

| Unity の挙動 | このツール |
| --- | --- |
| `.asmdef` ごとに 1 アセンブリ | 同じ |
| 参照は `references` に書いたものだけ | 同じ(**CS0012 の参照漏れが出る**) |
| `defineConstraints` を満たさないアセンブリは飛ばす | 同じ |
| `Editor/` 直下で asmdef が無いものは `Assembly-CSharp-Editor` | 同じ |
| `Assembly-CSharp-Editor` は `Assembly-CSharp` を参照する | 同じ |
| `#if UNITY_EDITOR` / `#if VRC_SDK_VRCSDK3` | `typecheck.py` の `DEFINES` で指定 |
| UnityEngine / UnityEditor / NUnit / VRChat SDK | `stubs/` の**形だけの代役** |

`DEFINES` には `UNITY_INCLUDE_TESTS` も入れてあるので、**テストアセンブリも検査されます**
(テストコードの型の誤りは Test Runner を回さないと分からなかった部分です)。

### stubs/ について

`stubs/` は Unity と VRChat SDK の **API の形だけ**を写した代役です。中身は空で、
実行はできません。型検査に必要なシグネチャしか持っていません。

そのため:

- **偽陽性**: 実在する API を stub に書き忘れると `CS0117` / `CS0246` が出ます。
  → その API を `stubs/` に足してください(実装は空のままで構いません)。
- **偽陰性**: 引数の型を実物と違えて書くと見逃します。
  → SDK 由来の型を新しく使うときは、stub の宣言を実物と突き合わせてください。

つまりこのツールは **「Unity で必ず通る」ことの保証ではなく、
「明らかな型の誤りを混ぜたまま出さない」ための関門**です。
最終確認は今までどおり Unity 上のコンパイルと Test Runner で行ってください。

### C# のバージョン

mcs 6.8 は C# 7.3 までです。Unity 2022 は C# 9 なので、
`??=` / switch 式 / `record` / target-typed `new()` などを書くとこのツールだけが
落ちます。現状このプロジェクトは C# 7.3 の範囲に収まっています。

---

## runtests.py

`typecheck.py` は「コンパイルが通るか」までしか見ません。
こちらは **EditMode テストを本当に実行**します。

```bash
python3 tools/typecheck/runtests.py
REPEAT=20 python3 tools/typecheck/runtests.py   # 乱数依存を疑うとき
```

`support/NUnitAsserting.cs` が NUnit の代役ですが、こちらは**本物の判定をします**
(`Assert.AreEqual` が合わなければ例外を投げる)。だからテストが落ちればここでも落ちます。

### 何が走らないか

`AudioSource` を鳴らす、`GameObject` を作るといった
**UnityEngine の実体が要るテスト**は動きません(stub には中身が無いため)。
`runtests.py` の `NEEDS_REAL_UNITY` に列挙して除外しており、実行後に名前が出ます。
そこは Unity の Test Runner に任せてください。

### 実績

Phase5-2 の時点で **841 ケースが通り**、Unity の Test Runner で落ちていた
`PlaybackFlowTests.ClearUpcomingLeavesOnlyWhatIsPlaying` と
`VideoBackendFlowTests.PlaybackCanContinueAfterAnError` を
**同じ失敗メッセージで再現**できました。
`git worktree` で前のコミットに当てて「いつから壊れていたか」を切り分けるのにも使えます。

---

## udon_lint.py

`typecheck.py` は「C# として正しいか」しか見ません。UdonSharp はその C# の
**さらに狭い部分集合**しか受け付けないので、mcs を通っても U# のコンパイルで
落ちるコードは書けてしまいます。その差分を見るのがこちらです。

```bash
python3 tools/typecheck/udon_lint.py
```

対象は **`UdonSharpBehaviour` を継承しているファイルだけ**です
(コメントで名前に触れているだけのファイルは対象外)。

見ているもの:

`List` / `Dictionary` / `HashSet` / `IEnumerable` / LINQ / `?.` / `??` / `??=` /
`throw` / `try` / `catch` / `yield` / `event` / `delegate` / `Action` / `Func` /
`interface` 宣言 / `struct` 宣言 / `enum` 宣言 / `params` / 自作メソッドの `out`・`ref` /
多次元配列 / ジャグ配列 / `abstract` / 文字列補間 / ジェネリックメソッド /
`static` フィールド(`const` 以外)/ 引数の既定値

### 限界

これは**構文の目視の代わり**であって、UdonSharp のコンパイラではありません。

- **偽陰性**: U# が弾く API(使えない BCL メソッドなど)は見ていません。
- **偽陽性**: `TryGetValue` のような SDK 側の `out` は除外していますが、
  他にも正当な例外があれば除外リストに足してください。

最終確認は Unity 上で U# がコンパイルするところまで行ってください。

---

## genmeta.py

### これは何のためにあるか

Phase5-2 で、新しく足した `.cs` に **`.meta` を同梱し忘れて納品**しました。

Unity は「どのファイルか」を `.meta` の GUID **だけ**で見ています。
`.meta` の無いファイルを zip で渡すと、展開のたびに Unity が新しい GUID を振り直すので、

- UdonSharp の Program Asset が元の `.cs` を見失う
- Console に `Source C# script ... is null` が出る
- **U# のコンパイルが全部止まる = ワールドが何も動かなくなる**

という壊れ方をします。しかもこれは「コンパイルは通っているのに動かない」ので、
`typecheck.py` でも `runtests.py` でも検出できません。

### 使い方

```bash
python3 tools/typecheck/genmeta.py            # 足りないぶんを作る
python3 tools/typecheck/genmeta.py --check    # 作らずに一覧するだけ(足りなければ終了コード 1)
```

### GUID の決め方

```
md5("SmartMediaPlatform:" + Assets からの相対パス)
```

パスから決まるので、**誰がいつ実行しても同じ GUID**になります
(ランダムだと、作り直すたびに参照が切れてしまう)。

**既にある `.meta` は触りません。** いま Unity が覚えている GUID を
書き換えると、それこそ参照が全部切れるためです。

### 限界

- Phase4 以前に手で書いた `.meta` は別の GUID を持っています(35 件)。
  そのままで正しいので、揃える必要はありません。
- ファイルを**消した**ときに残る `.meta` は消しません。手で消してください。
