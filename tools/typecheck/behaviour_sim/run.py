#!/usr/bin/env python3
"""U# のクラスを<b>そのまま mono で動かして</b>、操作の流れを確かめる。

runtests.py との違い
────────────────────
runtests.py が見るのは純粋 C# の「写しの元」(UdonModel)だけです。
こちらは Udon の実物(UdonPlaylistShelf / UdonPlayerSession /
UdonMediaController / UdonMediaListView …)を組み合わせて、
「保存 → 入り直し → 再生 → 消す」のような<b>画面から見た流れ</b>を通します。

VRChat の部品(PlayerData・DataDictionary・VRCUrl・入力欄)は、
tools/typecheck/stubs/VRC.cs を<b>その場で書き換えて</b>中身を持たせます。
書き換える場所が見つからなければ止まるので、スタブが変わっても気付けます。

使い方
  python3 tools/typecheck/behaviour_sim/run.py
"""
import glob
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
STUBS = os.path.join(ROOT, "tools", "typecheck", "stubs")
PROBE = os.path.join(ROOT, "tools", "typecheck", "ui_probe")
OUT = os.path.join(ROOT, "tools", "typecheck", "out-behaviour-sim")

DEFINES = ["UNITY_EDITOR", "UNITY_2022_3", "UNITY_STANDALONE", "VRC_SDK_VRCSDK3",
           "UDONSHARP", "UNITY_2022_2_OR_NEWER"]


def replace_once(text, old, new, where):
    if text.count(old) != 1:
        raise SystemExit(f"{where}: 書き換える場所が見つかりません(スタブが変わった?)\n  {old[:80]}")
    return text.replace(old, new)


def live_vrc():
    text = open(os.path.join(STUBS, "VRC.cs"), encoding="utf-8").read()
    w = "stubs/VRC.cs"

    text = replace_once(text,
        "        public VRCUrl(string url) { }\n",
        "        private string _url;\n        public VRCUrl(string url) { _url = url; }\n", w)
    text = replace_once(text,
        "        public string Get() { return null; }\n",
        "        public string Get() { return _url; }\n", w)
    text = replace_once(text,
        "        public static VRCPlayerApi LocalPlayer { get { return null; } }\n",
        "        public static VRCPlayerApi SimLocal;\n"
        "        public static VRCPlayerApi LocalPlayer { get { return SimLocal; } }\n", w)
    text = replace_once(text,
        "        public VRC.SDKBase.VRCUrl GetUrl() { return null; }\n",
        "        public string SimText;\n"
        "        public VRC.SDKBase.VRCUrl GetUrl() { return SimText == null ? null : new VRC.SDKBase.VRCUrl(SimText); }\n", w)

    # PlayerData に中身を持たせる
    text = replace_once(text,
        "        public static bool HasKey(VRC.SDKBase.VRCPlayerApi player, string key) { return false; }\n"
        "        public static string GetString(VRC.SDKBase.VRCPlayerApi player, string key) { return null; }\n"
        "        public static void SetString(string key, string value) { }\n",
        "        public static System.Collections.Generic.Dictionary<string, string> SimStore =\n"
        "            new System.Collections.Generic.Dictionary<string, string>();\n"
        "        public static int SimWrites;\n"
        "        public static bool HasKey(VRC.SDKBase.VRCPlayerApi player, string key) { return SimStore.ContainsKey(key); }\n"
        "        public static string GetString(VRC.SDKBase.VRCPlayerApi player, string key)\n"
        "        { string v; return SimStore.TryGetValue(key, out v) ? v : null; }\n"
        "        public static void SetString(string key, string value) { SimWrites++; SimStore[key] = value; }\n", w)

    # DataToken / DataDictionary に中身を持たせる(カタログの ID → 番号で使う)
    start = text.index("    public struct DataToken\n")
    end = text.index("    public class DataList\n")
    text = text[:start] + """    public struct DataToken
    {
        private object _v;
        public DataToken(string value) { _v = value; }
        public DataToken(int value) { _v = value; }
        public DataToken(float value) { _v = value; }
        public DataToken(double value) { _v = value; }
        public DataToken(bool value) { _v = value; }
        public DataToken(object value) { _v = value; }
        public TokenType TokenType { get { return _v is string ? TokenType.String : (_v is int ? TokenType.Int : TokenType.Null); } }
        public string String { get { return _v as string; } }
        public int Int { get { return _v is int ? (int)_v : 0; } }
        public float Float { get { return 0f; } }
        public double Double { get { return 0d; } }
        public bool Boolean { get { return false; } }
        public object Reference { get { return _v; } }
        public DataList DataList { get { return null; } }
        public DataDictionary DataDictionary { get { return null; } }
        public bool IsNull { get { return _v == null; } }
        public string SimKey { get { return _v == null ? "\\0null" : _v.GetType().Name + ":" + _v; } }
        public static implicit operator DataToken(string value) { return new DataToken(value); }
        public static implicit operator DataToken(int value) { return new DataToken(value); }
        public static implicit operator DataToken(bool value) { return new DataToken(value); }
        public static implicit operator DataToken(float value) { return new DataToken(value); }
    }

""" + text[end:]

    start = text.index("    public class DataDictionary\n")
    end = text.index("\n    }\n", text.index("public DataList GetValues()", start)) + len("\n    }\n")
    text = text[:start] + """    public class DataDictionary
    {
        private System.Collections.Generic.Dictionary<string, DataToken> _d =
            new System.Collections.Generic.Dictionary<string, DataToken>();
        public DataDictionary() { }
        public int Count { get { return _d.Count; } }
        public DataToken this[DataToken key] { get { return _d[key.SimKey]; } set { _d[key.SimKey] = value; } }
        public void Add(DataToken key, DataToken value) { _d.Add(key.SimKey, value); }
        public void Clear() { _d.Clear(); }
        public bool ContainsKey(DataToken key) { return _d.ContainsKey(key.SimKey); }
        public bool Remove(DataToken key) { return _d.Remove(key.SimKey); }
        public bool TryGetValue(DataToken key, out DataToken value) { return _d.TryGetValue(key.SimKey, out value); }
        public bool TryGetValue(DataToken key, TokenType type, out DataToken value) { return _d.TryGetValue(key.SimKey, out value); }
        public bool SetValue(DataToken key, DataToken value) { _d[key.SimKey] = value; return true; }
        public DataList GetKeys() { return null; }
        public DataList GetValues() { return null; }
    }
""" + text[end:]
    return text


def live_unity():
    text = open(os.path.join(PROBE, "UnityEngine.live.cs"), encoding="utf-8").read()
    return replace_once(text,
        "        public static float time { get { return 0f; } }\n",
        "        public static float SimNow;\n        public static float time { get { return SimNow; } }\n",
        "ui_probe/UnityEngine.live.cs")


def main():
    os.makedirs(OUT, exist_ok=True)

    vrc = os.path.join(OUT, "VRC.live.cs")
    unity = os.path.join(OUT, "UnityEngine.live.cs")
    open(vrc, "w", encoding="utf-8").write(live_vrc())
    open(unity, "w", encoding="utf-8").write(live_unity())

    srcs = [p for p in glob.glob(os.path.join(ROOT, "Assets", "**", "*.cs"), recursive=True)
            if "/Tests/" not in p and not p.endswith("UdonSharpSceneUtility.cs")]
    with open(os.path.join(OUT, "srcs.txt"), "w") as f:
        f.write("\n".join(srcs))

    exe = os.path.join(OUT, "sim.exe")
    cmd = (["mcs", "-langversion:latest",
            "-nowarn:0108,0114,0169,0649,0067,0618,0219,0414,0162,0168",
            "-out:" + exe]
           + ["-d:" + d for d in DEFINES]
           + [unity, os.path.join(STUBS, "UnityEditor.cs"), vrc,
              os.path.join(STUBS, "NUnit.cs"),
              os.path.join(PROBE, "SceneUtilityShim.cs"),
              os.path.join(HERE, "Sim.cs"),
              "@" + os.path.join(OUT, "srcs.txt")])
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        print(r.stdout + r.stderr)
        return 2

    r = subprocess.run(["mono", exe], capture_output=True, text=True, cwd=OUT)
    print(r.stdout + r.stderr, end="")
    return r.returncode


if __name__ == "__main__":
    sys.exit(main())
