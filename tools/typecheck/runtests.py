#!/usr/bin/env python3
"""
EditMode テストを Unity を起動せずに実際に走らせる。

typecheck.py が「コンパイルが通るか」しか見ないのに対して、こちらは
**テストを本当に実行**します。NUnit の代役に本物の判定を書いてあるので、
Assert が落ちればここでも落ちます。

UnityEngine に実体が要るテスト(GameObject を作る等)は動きません。
そういうアセンブリは自動的に外して、走ったものだけ報告します。
"""
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import typecheck as tc  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out-tests")
SUPPORT = os.path.join(HERE, "support")

REPEAT = int(os.environ.get("REPEAT", "1"))

# UnityEngine の実体(AudioSource / GameObject / ScriptableObject)が要るテスト。
# 代役の stub には中身が無いので、ここでは走らせず Unity の Test Runner に任せる。
NEEDS_REAL_UNITY = [
    "AudioBackendTests",
    "AudioBackendManagerTests",
    "AudioClipLibraryTests",
    "AdapterWithPlayerSessionTests",
    "MediaCatalogAssetTests",
    "SmartMediaPlayerRootTests",
]


def sh(cmd):
    return subprocess.run(cmd, shell=True, capture_output=True, text=True)


def build_support():
    os.makedirs(OUT, exist_ok=True)

    # 1. 本物の判定をする NUnit の代役
    r = sh('mcs -target:library -langversion:latest -nowarn:0108,0114,0169,0649,0067 '
           '-out:"%s/nunit.framework.dll" "%s/NUnitAsserting.cs"' % (OUT, SUPPORT))
    if r.returncode != 0:
        print(r.stdout + r.stderr)
        sys.exit(2)

    # 2. Unity / VRChat の形だけの代役(NUnit は含めない)
    srcs = " ".join('"%s"' % os.path.join(tc.STUBS, f)
                    for f in ["UnityEngine.cs", "UnityEditor.cs", "VRC.cs"])
    r = sh('mcs -target:library -langversion:latest -nowarn:0108,0114,0169,0649,0067 '
           '-r:"%s/nunit.framework.dll" -out:"%s/UnityStubs.dll" %s '
           '"%s/UnityTestTools.cs"' % (OUT, OUT, srcs, SUPPORT))
    if r.returncode != 0:
        print(r.stdout + r.stderr)
        sys.exit(2)


def compile_assembly(name, files, refs, no_engine):
    if not files:
        return None
    srcs = " ".join('"%s"' % f for f in files)
    rs = ['-r:"%s/UnityStubs.dll"' % OUT, '-r:"%s/nunit.framework.dll"' % OUT]
    if no_engine:
        rs = []
    for r in refs:
        rs.append('-r:"%s/%s.dll"' % (OUT, r))
    defs = " ".join("-define:" + d for d in tc.DEFINES)
    return sh('mcs -target:library -langversion:latest '
              '-nowarn:0108,0114,0169,0649,0067,0414,0162,0219,1701,0429 '
              '%s %s -out:"%s/%s.dll" %s' % (defs, " ".join(rs), OUT, name, srcs))


def main():
    print("== building support assemblies")
    build_support()

    asms = tc.load_asmdefs()
    tc.assign_files(asms)
    order = tc.topo(asms)

    built = set()
    test_assemblies = []
    skipped = []

    print("== compiling")
    for name in order:
        a = asms[name]
        if not tc.constraints_met(a):
            continue
        if any(r in asms and r not in built for r in a["refs"]):
            skipped.append(name)
            continue
        refs = [r for r in a["refs"] if r in built]
        res = compile_assembly(name, a["files"], refs, a["noEngine"])
        if res is None:
            built.add(name)
            continue
        if res.returncode != 0:
            skipped.append(name)
            text = (res.stdout or "") + (res.stderr or "")
            first = [l for l in text.splitlines() if "error CS" in l][:1]
            print("  skip %s  %s" % (name, first[0].strip() if first else ""))
            continue
        built.add(name)
        if name.endswith(".Tests"):
            test_assemblies.append(name)

    if not test_assemblies:
        print("no test assemblies built")
        return 1

    print("== building runner for %d test assemblies" % len(test_assemblies))
    rs = ['-r:"%s/UnityStubs.dll"' % OUT, '-r:"%s/nunit.framework.dll"' % OUT]
    for n in built:
        rs.append('-r:"%s/%s.dll"' % (OUT, n))
    names = " ".join('-define:X' for _ in [])
    r = sh('mcs -target:exe -langversion:latest %s -out:"%s/Runner.exe" "%s/Runner.cs" %s'
           % (" ".join(rs), OUT, SUPPORT, names))
    if r.returncode != 0:
        print(r.stdout + r.stderr)
        return 2

    arg = ",".join(test_assemblies)
    os.environ["SKIP_FIXTURES"] = ",".join(NEEDS_REAL_UNITY)
    failed_total = 0
    for i in range(REPEAT):
        run = sh('cd "%s" && SKIP_FIXTURES="%s" mono Runner.exe %s'
                 % (OUT, ",".join(NEEDS_REAL_UNITY), arg))
        out = run.stdout + run.stderr
        if REPEAT > 1:
            last = [l for l in out.splitlines() if "passed," in l]
            print("  run %-3d %s" % (i + 1, last[-1] if last else "?"))
            if run.returncode != 0:
                for l in out.splitlines():
                    if l.startswith("FAIL") or l.startswith("     "):
                        print("        " + l)
        else:
            print(out)
        if run.returncode != 0:
            failed_total += 1

    print("(Unity の実体が要るため未実行: %s)" % ", ".join(NEEDS_REAL_UNITY))
    if skipped:
        print("(コンパイルできず未実行: %s)" % ", ".join(sorted(skipped)))
    if REPEAT > 1:
        print()
        print("%d/%d 回で失敗" % (failed_total, REPEAT))
    return 1 if failed_total else 0


if __name__ == "__main__":
    sys.exit(main())
