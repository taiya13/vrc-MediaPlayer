#!/usr/bin/env python3
"""
UdonSharp で書けない構文を、Unity を起動せずに見つける。

typecheck.py は「C# として正しいか」しか見ません。UdonSharp はその C# の
さらに狭い部分集合しか受け付けないので、mcs を通っても U# のコンパイルで
落ちるコードは書けてしまいます。ここではその差分を見ます。

対象は UdonSharpBehaviour を継承しているファイルだけです。
"""
import os
import re
import sys

PROJECT = sys.argv[1] if len(sys.argv) > 1 else "/home/user/vrc-MediaPlayer"
ASSETS = os.path.join(PROJECT, "Assets")


def strip_comments_and_strings(text):
    """コメントと文字列リテラルを空白に潰す。行数と桁は保つ。"""
    out = []
    i, n = 0, len(text)
    state = None  # None | 'line' | 'block' | 'str' | 'char' | 'verbatim'
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if state is None:
            if c == "/" and nxt == "/":
                state, i = "line", i + 2
                out.append("  ")
                continue
            if c == "/" and nxt == "*":
                state, i = "block", i + 2
                out.append("  ")
                continue
            if c == "@" and nxt == '"':
                state, i = "verbatim", i + 2
                out.append('@"')
                continue
            if c == '"':
                state, i = "str", i + 1
                out.append('"')
                continue
            if c == "'":
                state, i = "char", i + 1
                out.append("'")
                continue
            out.append(c)
            i += 1
            continue

        if state == "line":
            if c == "\n":
                state = None
                out.append(c)
            else:
                out.append(" ")
            i += 1
            continue

        if state == "block":
            if c == "*" and nxt == "/":
                state, i = None, i + 2
                out.append("  ")
                continue
            out.append(c if c == "\n" else " ")
            i += 1
            continue

        if state == "verbatim":
            if c == '"' and nxt == '"':
                out.append("  ")
                i += 2
                continue
            if c == '"':
                state, i = None, i + 1
                out.append('"')
                continue
            out.append(c if c == "\n" else " ")
            i += 1
            continue

        # 'str' / 'char'
        if c == "\\":
            out.append("  ")
            i += 2
            continue
        if (state == "str" and c == '"') or (state == "char" and c == "'"):
            state, i = None, i + 1
            out.append(c)
            continue
        out.append(c if c == "\n" else " ")
        i += 1

    return "".join(out)


# (正規表現, 説明) — コメント・文字列を潰したコードにだけ当てる
RULES = [
    (r"\bList\s*<", "List<> は使えない(固定長配列 + 件数にする)"),
    (r"\bDictionary\s*<", "Dictionary<> は使えない(DataDictionary か線形探索)"),
    (r"\bHashSet\s*<", "HashSet<> は使えない"),
    (r"\bIEnumerable\b", "IEnumerable は使えない"),
    (r"\bIReadOnlyList\b", "IReadOnlyList は使えない"),
    (r"using\s+System\.Linq\s*;", "LINQ は使えない"),
    (r"\?\?=", "??= は使えない"),
    (r"\?\?[^=]", "?? は使えない(null を明示的に比較する)"),
    (r"\?\.", "?. は使えない(null を明示的に比較する)"),
    (r"\bthrow\b", "例外は投げられない(bool を返す)"),
    (r"\btry\s*\{", "try/catch は使えない"),
    (r"\bcatch\s*[\({]", "try/catch は使えない"),
    (r"\byield\b", "yield は使えない(コルーチンは SendCustomEventDelayed～ で代用)"),
    (r"\bevent\s+", "event は使えない"),
    (r"\bdelegate\b", "delegate は使えない"),
    (r"\bAction\s*<", "Action<> は使えない"),
    (r"\bFunc\s*<", "Func<> は使えない"),
    (r"\binterface\s+\w", "interface は宣言できない"),
    (r"\bparams\s+\w", "params は使えない"),
    (r"\bout\s+\w+\s+\w+\s*[,)]", "自作メソッドの out 引数は使えない", "sdk_out_ok"),
    (r"\bref\s+\w+\s+\w+\s*[,)]", "ref 引数は使えない"),
    (r"\[\s*,\s*\]", "多次元配列は使えない"),
    (r"\]\s*\[\s*\]", "ジャグ配列は使えない"),
    (r"\babstract\b", "abstract は使えない"),
    (r"\$\"", "文字列補間($\"…\")は避ける(+ で連結する)"),
    (r"\bstruct\s+\w", "struct は宣言できない"),
    (r"\benum\s+\w", "enum は宣言できない(const int を使う)"),
]

# UdonSharpBehaviour が持つ「使ってよい」out 呼び出し(SDK 側の API)
SDK_OUT_ALLOWED = re.compile(r"TryGetValue\s*\(")

GENERIC_METHOD = re.compile(r"\b(?:public|private|protected|internal)\s+[\w\[\]<>.]+\s+\w+\s*<\s*\w")
STATIC_FIELD = re.compile(
    r"^\s*(?:public|private|protected|internal)\s+static\s+(?!readonly\s+)(?!const\s+)"
    r"[\w\[\]<>.]+\s+\w+\s*(=|;)")
DEFAULT_ARG = re.compile(
    r"\b(?:public|private|protected|internal)\s+[\w\[\]<>.]+\s+\w+\s*\([^)]*\w\s*=\s*[^)=]")


DERIVES = re.compile(r"\bclass\s+\w+\s*:\s*[^{;]*\bUdonSharpBehaviour\b")


def is_udon_behaviour(path):
    """UdonSharpBehaviour を継承しているファイルか(コメントの言及は数えない)。"""
    raw = open(path, encoding="utf-8").read()
    if "UdonSharpBehaviour" not in raw:
        return False
    return DERIVES.search(strip_comments_and_strings(raw)) is not None


def check(path):
    raw = open(path, encoding="utf-8").read()
    code = strip_comments_and_strings(raw)
    lines = code.splitlines()
    raw_lines = raw.splitlines()
    problems = []

    for rule in RULES:
        pattern, message = rule[0], rule[1]
        exempt = rule[2] if len(rule) > 2 else None
        for m in re.finditer(pattern, code):
            line_no = code[:m.start()].count("\n") + 1
            line = lines[line_no - 1] if line_no <= len(lines) else ""
            if exempt == "sdk_out_ok" and SDK_OUT_ALLOWED.search(line):
                continue
            problems.append((line_no, message, raw_lines[line_no - 1].strip()))

    for i, line in enumerate(lines, start=1):
        if GENERIC_METHOD.search(line):
            problems.append((i, "ジェネリックメソッドは使えない", raw_lines[i - 1].strip()))
        if STATIC_FIELD.search(line):
            problems.append((i, "static フィールドは使えない(const だけ可)",
                             raw_lines[i - 1].strip()))
        if DEFAULT_ARG.search(line):
            problems.append((i, "引数の既定値は使えない", raw_lines[i - 1].strip()))

    problems.sort()
    return problems


def main():
    targets = []
    for root, dirs, files in os.walk(ASSETS):
        for f in files:
            if f.endswith(".cs"):
                targets.append(os.path.join(root, f))

    total = 0
    checked = 0
    for path in sorted(targets):
        if not is_udon_behaviour(path):
            continue
        problems = check(path)
        checked += 1
        rel = os.path.relpath(path, PROJECT)
        if not problems:
            print("  ok   " + rel)
            continue
        print("  FAIL " + rel)
        for line_no, message, text in problems:
            print("       %s:%d  %s" % (rel, line_no, message))
            print("         > " + text)
            total += 1

    print()
    print("UdonSharpBehaviour: %d ファイルを確認" % checked)
    if total:
        print("%d 件、Udon で書けない書き方があります。" % total)
        return 1
    print("Udon で書けない書き方はありません。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
