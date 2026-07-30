#!/usr/bin/env python3
"""
足りない .meta を、決まった GUID で作る。

これが無いと何が起きるか
------------------------
Unity は「どのファイルか」を .meta の GUID だけで見ています。
.meta を同梱せずに zip を渡すと、展開のたびに Unity が**新しい GUID を振り直す**ので、

  - UdonSharp の Program Asset が元の .cs を見失う
  - Console に「Source C# script ... is null」が出て、U# のコンパイルが全部止まる
  - = ワールドが何も動かなくなる

という壊れ方をします(Phase5-2 で実際に起きました)。

GUID の決め方
-------------
    md5("SmartMediaPlatform:" + Assets からの相対パス)

パスから決まるので、**誰がいつ実行しても同じ GUID**になります。
既にある .meta は触りません(いま Unity が覚えている GUID を壊さないため)。

使い方
------
    python3 tools/typecheck/genmeta.py            # 足りないぶんを作る
    python3 tools/typecheck/genmeta.py --check    # 作らずに、足りないものを一覧するだけ
"""
import hashlib
import os
import sys

PROJECT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ASSETS = os.path.join(PROJECT, "Assets")

SALT = "SmartMediaPlatform:"

# Unity が .meta を作らないもの
SKIP_NAMES = {".DS_Store", "Thumbs.db"}
SKIP_SUFFIXES = (".meta",)


def guid_for(relative_path):
    """パスから決まる GUID。区切りは常に / で数える(Windows でも同じ値にするため)。"""
    normalized = relative_path.replace(os.sep, "/")
    return hashlib.md5((SALT + normalized).encode("utf-8")).hexdigest()


FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

SCRIPT_META = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

ASMDEF_META = """fileFormatVersion: 2
guid: {guid}
AssemblyDefinitionImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

TEXT_META = """fileFormatVersion: 2
guid: {guid}
TextScriptImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

DEFAULT_META = """fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def template_for(path, is_dir):
    if is_dir:
        return FOLDER_META
    if path.endswith(".cs"):
        return SCRIPT_META
    if path.endswith(".asmdef") or path.endswith(".asmref"):
        return ASMDEF_META
    if path.endswith((".md", ".txt", ".json", ".xml")):
        return TEXT_META
    return DEFAULT_META


def targets():
    """.meta が要るもの(フォルダとファイルの両方)を、Assets 以下から集める。"""
    found = []
    for root, dirs, files in os.walk(ASSETS):
        dirs.sort()
        for d in dirs:
            found.append((os.path.join(root, d), True))
        for f in sorted(files):
            if f in SKIP_NAMES or f.endswith(SKIP_SUFFIXES):
                continue
            found.append((os.path.join(root, f), False))
    return found


def main():
    check_only = "--check" in sys.argv

    if not os.path.isdir(ASSETS):
        print("Assets が見つかりません: " + ASSETS)
        return 1

    missing = []
    for path, is_dir in targets():
        if os.path.exists(path + ".meta"):
            continue
        missing.append((path, is_dir))

    if not missing:
        print("足りない .meta はありません。")
        return 0

    for path, is_dir in missing:
        relative = os.path.relpath(path, PROJECT)
        guid = guid_for(relative)

        if check_only:
            print("  MISSING  " + relative + "  (guid: " + guid + ")")
            continue

        with open(path + ".meta", "w", encoding="utf-8", newline="\n") as handle:
            handle.write(template_for(path, is_dir).format(guid=guid))
        print("  created  " + relative + ".meta")

    print()
    if check_only:
        print("%d 件の .meta がありません。genmeta.py を引数なしで実行してください。" % len(missing))
        return 1

    print("%d 件の .meta を作成しました。" % len(missing))
    return 0


if __name__ == "__main__":
    sys.exit(main())
