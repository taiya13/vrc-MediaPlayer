#!/usr/bin/env python3
"""壁パネルとリモコンを<b>本物のビルダーで組み立てて</b>、重なりを調べる。

panel_layout.py との違い
────────────────────────
panel_layout.py は寸法を読み取って<b>計算し直す</b>ので、
ビルダーの書き方が変わると見落とします(実際、レールの名前の枠が
30px しか無い不具合は計算の外にありました)。

こちらは UdonMediaPanelBuilder.BuildWallPanel / BuildRemotePanel を
mono で<b>そのまま実行</b>し、できあがった全部品の座標を調べます。

見るもの
  1. タブごと・「…」シートを開いたときに、文字どうし・押す所どうしが重なっていないか
     (わざと重ねているもの —— バーと「使う」用の区画、入力欄と当たり判定、
      空のときの案内と行 —— は除外)
  2. 板からはみ出していないか
  3. 実行中に入る文字(タブの件数、一時停止、時刻、操作の結果など)の長いほうが枠に入るか
  4. 「…」シートがいちばん最後に描かれるか(uGUI は z ではなく階層の順で塗るため)

使い方
  python3 tools/typecheck/ui_probe/run.py
  (画像で見たいときは、実行後に out-ui-probe で render.py を走らせて
   できた HTML を Chromium で撮る)
"""
import os, subprocess, sys, json, glob, shutil

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
STUBS = os.path.join(ROOT, "tools", "typecheck", "stubs")
OUT = os.path.join(ROOT, "tools", "typecheck", "out-ui-probe")

DEFINES = ["UNITY_EDITOR", "UNITY_2022_3", "UNITY_STANDALONE", "VRC_SDK_VRCSDK3",
           "UDONSHARP", "UNITY_2022_2_OR_NEWER"]


def main():
    os.makedirs(OUT, exist_ok=True)
    srcs = [p for p in glob.glob(os.path.join(ROOT, "Assets", "**", "*.cs"), recursive=True)
            if "/Tests/" not in p and not p.endswith("UdonSharpSceneUtility.cs")]
    with open(os.path.join(OUT, "srcs.txt"), "w") as f:
        f.write("\n".join(srcs))
    cmd = (["mcs", "-langversion:latest",
            "-nowarn:0108,0114,0169,0649,0067,0618,0219,0414,0162,0168",
            "-out:" + os.path.join(OUT, "probe.exe")]
           + ["-d:" + d for d in DEFINES]
           + [os.path.join(HERE, "UnityEngine.live.cs"),
              os.path.join(STUBS, "UnityEditor.cs"), os.path.join(STUBS, "VRC.cs"),
              os.path.join(STUBS, "NUnit.cs"),
              os.path.join(HERE, "SceneUtilityShim.cs"), os.path.join(HERE, "Probe.cs"),
              "@" + os.path.join(OUT, "srcs.txt")])
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        print(r.stdout + r.stderr); return 2
    # ビルダーは角丸の画像を「Assets/SmartMediaPlatform_Data/UI」へ書き出します(本物の動き)。
    # 作業フォルダの中で走らせて、プロジェクトには書き込ませません。
    r = subprocess.run(["mono", os.path.join(OUT, "probe.exe"), OUT],
                       capture_output=True, text=True, cwd=OUT)
    if r.returncode != 0:
        print(r.stdout + r.stderr); return 2

    os.chdir(OUT)
    sys.path.insert(0, HERE)
    import analyze2, runtime_text

    problems = 0
    for t in range(5):
        problems += report(analyze2.run, "wall", t, False, f"タブ{t}")
    problems += report(analyze2.run, "remote", None, False, "リモコン")

    for line in runtime_text.check():
        print(line); problems += 1

    # シートがいちばん最後に描かれるか
    for kind in ("wall", "remote"):
        d = json.load(open(f"{kind}.json"))
        body = [n for n in d if n["path"].endswith("/Canvas/Body")][0]
        top = [n for n in d if n["path"].startswith(body["path"] + "/")
               and n["path"].count("/") == body["path"].count("/") + 1]
        last = top[-1]["path"].rsplit("/", 1)[-1]
        if last != "MoreSheet":
            print(f"  ✗ {kind}: 「…」シートより後に {last} が描かれます")
            problems += 1

    print("\n" + ("問題なし" if problems == 0 else f"{problems} 件の問題があります"))
    return 1 if problems else 0


def report(fn, kind, tab, sheet, label):
    import io, contextlib
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        fn(kind, tab=tab, sheet=sheet, label=label)
    lines = [l for l in buf.getvalue().splitlines()
             if l.strip().startswith("[") and "押す所が小さい" not in l]
    for l in lines: print(f"{kind} {label}: {l.strip()}")
    return len(lines)


if __name__ == "__main__":
    sys.exit(main())
