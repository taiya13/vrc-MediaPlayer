#!/usr/bin/env python3
"""Assets/SmartMediaPlatform を 1 つの .unitypackage に固める。

なぜ zip をやめたか (Phase7-3)
------------------------------
zip は「どこで展開するか」をユーザーに任せます。1 段間違えると
  Assets/Assets/SmartMediaPlatform/...        (Assets の中で展開した)
  Assets/SmartMediaPlatform/Assets/...        (フォルダの中で展開した)
という入れ子のコピーができ、同名 asmdef の衝突でコンパイルが止まり、
「画面も音もボタンも全部動かない」になります。実際に起きました。

.unitypackage は中に「GUID → 置き場所」を持っていて、Unity が必ず
正しいパスへ取り込みます。展開場所という概念が無いので、この事故が
構造的に起きません。ダブルクリック → Import だけです。

形式
----
.unitypackage = tar.gz。GUID ごとにディレクトリが 1 つ:
  <guid>/pathname   ... "Assets/..." の置き場所 (テキスト)
  <guid>/asset      ... ファイルの中身 (フォルダには無い)
  <guid>/asset.meta ... .meta の中身
GUID は既存の .meta から読む(genmeta.py が決定的に採番済みなので、
納品のたびに同じ GUID = 参照が壊れない)。

使い方
------
  python3 tools/package/make_unitypackage.py 出力先.unitypackage
"""

import io
import os
import re
import sys
import tarfile

ROOT = "Assets/SmartMediaPlatform"
GUID_RE = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.MULTILINE)


def fail(message: str) -> None:
    print("NG: " + message)
    sys.exit(1)


def read_guid(meta_path: str) -> str:
    with open(meta_path, "r", encoding="utf-8") as handle:
        match = GUID_RE.search(handle.read())
    if not match:
        fail(f"{meta_path} に guid がありません")
    return match.group(1)


def collect():
    """(パス, meta パス, フォルダか) を全部集める。root フォルダ自身も含む。"""
    if not os.path.isdir(ROOT):
        fail(f"{ROOT} がありません。リポジトリ直下で実行してください")

    entries = [(ROOT, ROOT + ".meta", True)]

    for base, dirs, files in os.walk(ROOT):
        dirs.sort()
        for name in sorted(dirs):
            path = os.path.join(base, name)
            entries.append((path, path + ".meta", True))
        for name in sorted(files):
            if name.endswith(".meta"):
                continue
            path = os.path.join(base, name)
            entries.append((path, path + ".meta", False))

    missing = [meta for _, meta, _ in entries if not os.path.isfile(meta)]
    if missing:
        for meta in missing[:10]:
            print("  missing meta: " + meta)
        fail(f".meta が {len(missing)} 件足りません。genmeta.py を実行してください")

    return entries


def add_bytes(tar: tarfile.TarFile, name: str, data: bytes) -> None:
    info = tarfile.TarInfo(name)
    info.size = len(data)
    info.mtime = 0          # 毎回同じ中身なら毎回同じファイルにする(差分確認のため)
    info.mode = 0o644
    tar.addfile(info, io.BytesIO(data))


def main() -> None:
    if len(sys.argv) != 2:
        fail("使い方: make_unitypackage.py 出力先.unitypackage")

    out_path = sys.argv[1]
    entries = collect()

    seen_guids = {}
    files = 0
    folders = 0

    os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)

    with tarfile.open(out_path, "w:gz", format=tarfile.GNU_FORMAT) as tar:
        for path, meta_path, is_folder in entries:
            guid = read_guid(meta_path)

            if guid in seen_guids:
                fail(f"GUID が重複: {path} と {seen_guids[guid]}")
            seen_guids[guid] = path

            unity_path = path.replace(os.sep, "/")
            add_bytes(tar, guid + "/pathname", (unity_path + "\n").encode("utf-8"))

            with open(meta_path, "rb") as handle:
                add_bytes(tar, guid + "/asset.meta", handle.read())

            if is_folder:
                folders += 1
            else:
                with open(path, "rb") as handle:
                    add_bytes(tar, guid + "/asset", handle.read())
                files += 1

    size = os.path.getsize(out_path)
    print(f"OK: {out_path}")
    print(f"  ファイル {files} 件 / フォルダ {folders} 件 / {size / 1024:.0f} KB")


if __name__ == "__main__":
    main()
