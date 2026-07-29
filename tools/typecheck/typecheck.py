#!/usr/bin/env python3
"""
Type-check the whole Unity project with mcs, without Unity.

Mirrors Unity's compilation model:
  * every .asmdef becomes one assembly, compiled with exactly the references it declares
  * files under an Editor/ folder with no asmdef go into Assembly-CSharp-Editor
  * everything else with no asmdef goes into Assembly-CSharp
  * defineConstraints / #if UNITY_EDITOR / #if VRC_SDK_VRCSDK3 are honoured
  * stub UnityEngine / UnityEditor / NUnit / VRChat SDK assemblies stand in for the real ones

This catches real compile errors (CS0266, CS1662, CS0012, CS0117, ...) that the
brace/asmdef text checks cannot see.
"""
import json
import os
import re
import subprocess
import sys
from collections import OrderedDict

HERE = os.path.dirname(os.path.abspath(__file__))
STUBS = os.path.join(HERE, "stubs")
OUT = os.path.join(HERE, "out")
PROJECT = sys.argv[1] if len(sys.argv) > 1 else "/home/user/vrc-MediaPlayer"
ASSETS = os.path.join(PROJECT, "Assets")

# Defines Unity would set. VRC_SDK_VRCSDK3 is on because the user's project has the SDK.
DEFINES = ["UNITY_EDITOR", "UNITY_2022_3", "UNITY_STANDALONE", "VRC_SDK_VRCSDK3",
           "UDONSHARP", "UNITY_INCLUDE_TESTS", "UNITY_2022_2_OR_NEWER"]

STUB_ASSEMBLIES = [
    ("UnityStubs", ["UnityEngine.cs", "UnityEditor.cs", "NUnit.cs", "VRC.cs"]),
]


def sh(cmd):
    return subprocess.run(cmd, shell=True, capture_output=True, text=True)


def build_stubs():
    os.makedirs(OUT, exist_ok=True)
    for name, files in STUB_ASSEMBLIES:
        srcs = " ".join('"%s"' % os.path.join(STUBS, f) for f in files)
        r = sh('mcs -target:library -langversion:latest -nowarn:0108,0114,0169,0649,0067 '
               '-out:"%s/%s.dll" %s' % (OUT, name, srcs))
        if r.returncode != 0:
            print("!! stub assembly '%s' failed to build:" % name)
            print(r.stdout + r.stderr)
            sys.exit(2)


def find_asmdefs():
    found = []
    for root, dirs, files in os.walk(ASSETS):
        for f in files:
            if f.endswith(".asmdef"):
                found.append(os.path.join(root, f))
    return sorted(found)


def load_asmdefs():
    asms = OrderedDict()
    for path in find_asmdefs():
        with open(path, encoding="utf-8") as fh:
            data = json.load(fh)
        name = data["name"]
        asms[name] = {
            "name": name,
            "dir": os.path.dirname(path),
            "refs": [r for r in data.get("references", [])],
            "constraints": data.get("defineConstraints", []),
            "noEngine": data.get("noEngineReferences", False),
            "files": [],
        }
    return asms


def owner_of(path, asms):
    """The asmdef whose folder is the deepest ancestor of path (Unity's rule)."""
    best = None
    for a in asms.values():
        d = a["dir"] + os.sep
        if path.startswith(d) and (best is None or len(a["dir"]) > len(best["dir"])):
            best = a
    return best


def assign_files(asms):
    predef = {"Assembly-CSharp": [], "Assembly-CSharp-Editor": []}
    for root, dirs, files in os.walk(ASSETS):
        for f in files:
            if not f.endswith(".cs"):
                continue
            path = os.path.join(root, f)
            a = owner_of(path, asms)
            if a is not None:
                a["files"].append(path)
            elif os.sep + "Editor" + os.sep in path + os.sep or os.path.basename(root) == "Editor":
                predef["Assembly-CSharp-Editor"].append(path)
            else:
                predef["Assembly-CSharp"].append(path)
    return predef


def constraints_met(a):
    """Unity skips an assembly whose defineConstraints aren't satisfied."""
    for c in a["constraints"]:
        neg = c.startswith("!")
        sym = c[1:] if neg else c
        have = sym in DEFINES
        if neg and have:
            return False
        if not neg and not have:
            return False
    return True


def topo(asms):
    order, seen, stack = [], set(), set()

    def visit(name):
        if name in seen:
            return
        if name in stack:
            print("!! circular asmdef reference at %s" % name)
            sys.exit(2)
        stack.add(name)
        for r in asms[name]["refs"]:
            if r in asms:
                visit(r)
        stack.discard(name)
        seen.add(name)
        order.append(name)

    for n in asms:
        visit(n)
    return order


def compile_assembly(name, files, refs, no_engine):
    if not files:
        return None
    srcs = " ".join('"%s"' % f for f in files)
    rs = ['-r:"%s/UnityStubs.dll"' % OUT] if not no_engine else []
    for r in refs:
        rs.append('-r:"%s/%s.dll"' % (OUT, r))
    defs = " ".join("-define:" + d for d in DEFINES)
    cmd = ('mcs -target:library -langversion:latest -nostdlib+ '
           '-r:/usr/lib/mono/4.5/mscorlib.dll -r:/usr/lib/mono/4.5/System.dll '
           '-r:/usr/lib/mono/4.5/System.Core.dll '
           '-nowarn:0108,0114,0169,0649,0067,0414,0162,0219,1701,0429 '
           '%s %s %s -out:"%s/%s.dll" %s' % (defs, " ".join(rs), "", OUT, name, srcs))
    return sh(cmd)


ERROR_RE = re.compile(r"^(.*?)\((\d+),(\d+)\): error (CS\d+): (.*)$")


def report(name, result, failures):
    if result is None:
        return
    text = (result.stdout or "") + (result.stderr or "")
    errs = []
    for line in text.splitlines():
        m = ERROR_RE.match(line.strip())
        if m:
            errs.append(m)
        elif "error CS" in line:
            errs.append(None)
    if result.returncode == 0 and not errs:
        print("  ok   %s" % name)
        return
    print("  FAIL %s" % name)
    seen = set()
    for line in text.splitlines():
        s = line.strip()
        if "error CS" not in s:
            continue
        s = s.replace(PROJECT + "/", "")
        if s in seen:
            continue
        seen.add(s)
        print("       " + s)
        failures.append(s)


def main():
    print("== building stub assemblies")
    build_stubs()

    asms = load_asmdefs()
    predef = assign_files(asms)
    order = topo(asms)

    print("== compiling %d asmdef assemblies + predefined" % len(asms))
    failures = []
    built = set()
    for name in order:
        a = asms[name]
        if not constraints_met(a):
            print("  skip %s (defineConstraints not met)" % name)
            continue
        refs = [r for r in a["refs"] if r in built]
        missing = [r for r in a["refs"] if r in asms and r not in built]
        if missing:
            print("  skip %s (depends on skipped/failed: %s)" % (name, ", ".join(missing)))
            continue
        res = compile_assembly(name, a["files"], refs, a["noEngine"])
        if res is None:
            print("  ---  %s (no .cs files)" % name)
            built.add(name)
            continue
        before = len(failures)
        report(name, res, failures)
        if len(failures) == before:
            built.add(name)

    # Predefined assemblies reference every autoReferenced asmdef;
    # Assembly-CSharp-Editor additionally references Assembly-CSharp.
    all_refs = [n for n in order if n in built]
    for pname in ("Assembly-CSharp", "Assembly-CSharp-Editor"):
        files = predef[pname]
        if not files:
            continue
        refs = list(all_refs)
        if pname == "Assembly-CSharp-Editor" and os.path.exists(
                os.path.join(OUT, "Assembly-CSharp.dll")):
            refs.append("Assembly-CSharp")
        res = compile_assembly(pname, files, refs, False)
        report(pname, res, failures)

    print()
    if failures:
        print("%d compile error(s)." % len(failures))
        return 1
    print("No compile errors.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
