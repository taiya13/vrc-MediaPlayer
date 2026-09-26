import itertools, re, sys
from analyze import load, mark_self, state, area, inter, related, clickable, is_text

def intended(a, b):
    pa, pb = a["p"], b["p"]
    both = pa + " " + pb
    if "Strip/Cell" in both: return True                     # 「使う」用の区画はバーの上に重ねる設計
    if "/Search/Field" in both and "SearchHit" in both: return True
    if pa.endswith("/Text") and pb.endswith("/Placeholder") or pb.endswith("/Text") and pa.endswith("/Placeholder"): return True
    if "/Empty" in both: return True                         # 空の案内は行が空のときだけ
    if "UrlField" in both and "UrlHit" in both: return True
    if "/SaveBar/NameField" in both and "NameHit" in both: return True   # 名前欄の上に「使う」を重ねる設計
    return False

def text_width(s, size):
    w = 0
    for ch in s:
        if ord(ch) < 0x2E80 and ch not in "♥＋×▲▼◀▶❚…♪": w += 0.56 * size
        else: w += 1.0 * size
    return w

def fits(n, sample=None):
    s = sample if sample is not None else n.get("text", "")
    if not s: return None
    need = text_width(s, n["fontSize"]); have = n["R"] - n["L"]
    return need, have

def run(kind, tab=None, sheet=False, label=""):
    nodes, W, H = load(kind); mark_self(nodes)
    vis = state(nodes, tab, sheet)
    out = []
    texts = [n for n in vis if is_text(n)]
    for a, b in itertools.combinations(texts, 2):
        if related(a, b) or intended(a, b): continue
        o = inter(a, b)
        if o: out.append(f"[文字が重なる] {a['p']} ↔ {b['p']}  {o[0]:.0f}×{o[1]:.0f}px")
    clicks = [n for n in vis if clickable(n) and area(n) > 0]
    for a, b in itertools.combinations(clicks, 2):
        if related(a, b) or intended(a, b): continue
        o = inter(a, b)
        if o: out.append(f"[押す所が重なる] {a['p']} ↔ {b['p']}  {o[0]:.0f}×{o[1]:.0f}px")
    for n in vis:
        if area(n) == 0 or "Shadow" in n["p"].rsplit("/",1)[-1] or "/MoreSheet" in n["p"]: continue
        if n["L"] < -0.5 or n["T"] < -0.5 or n["R"] > W+0.5 or n["B"] > H+0.5:
            out.append(f"[板からはみ出す] {n['p']} ({n['L']:.0f},{n['T']:.0f})-({n['R']:.0f},{n['B']:.0f})")
    for n in texts:
        f = fits(n)
        if f and f[0] > f[1] + 1:
            out.append(f"[固定の文字が枠に入らない] {n['p']} 「{n['text']}」 必要≈{f[0]:.0f}px / 枠 {f[1]:.0f}px")
        if n["B"] - n["T"] + 0.5 < n["fontSize"] and n.get("text"):
            out.append(f"[文字より枠が低い] {n['p']} 「{n['text']}」 {n['fontSize']}px の文字 / 高さ {n['B']-n['T']:.0f}px")
    for n in clicks:
        s = min(n["R"]-n["L"], n["B"]-n["T"])
        if s < 35: out.append(f"[押す所が小さい] {n['p']} 短辺 {s:.0f}px (下限 35px)")
    print(f"\n==== {kind} {label}")
    for line in out: print("  " + line)
    if not out: print("  問題なし")
    return vis

if __name__ == "__main__":
    for t in range(6): run("wall", tab=t, label=f"タブ{t}")
    run("wall", tab=0, sheet=True, label="タブ0 + 「…」シート")
    run("remote", label="リモコン")
