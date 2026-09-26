import json, sys, itertools
PREFIX = {"wall": "/Holder/Wall/Canvas/Body", "remote": "/Holder/Remote/Canvas/Body"}

def load(kind):
    d = json.load(open(f"{kind}.json"))
    body = [n for n in d if n["path"] == PREFIX[kind]][0]
    W, H = body["x1"]-body["x0"], body["y1"]-body["y0"]
    out = []
    for n in d:
        if not n["path"].startswith(PREFIX[kind]): continue
        p = n["path"][len(PREFIX[kind]):] or "/"
        x0 = n["x0"]-body["x0"]; x1 = n["x1"]-body["x0"]
        top = body["y1"]-n["y1"]; bot = body["y1"]-n["y0"]
        m = dict(n); m.update(p=p, L=x0, T=top, R=x1, B=bot)
        out.append(m)
    return out, W, H

def state(nodes, tab=None, sheet=False):
    """実行時の見え方を真似る: 選んだタブのページだけ出す / レールの行は見出しとして出す"""
    act = {}
    for n in nodes:
        p = n["p"]; a = n["active"]
        # 親が非表示なら子も非表示(act は親から順に埋まる)
        parent = p.rsplit("/", 1)[0] if p.count("/") > 1 else "/"
        own = a if parent not in act else (act[parent] and selfactive(n, p, tab, sheet))
        if parent in act and not act[parent]: own = False
        if parent not in act: own = selfactive(n, p, tab, sheet)
        act[p] = own
    return [n for n in nodes if act[n["p"]]]

SELF = {}
def selfactive(n, p, tab, sheet):
    name = p.rsplit("/", 1)[-1]
    if tab is not None and name.startswith("Page") and p.count("/") == 2 and p.startswith("/Browser/"):
        return name == f"Page{tab}"
    if name == "MoreSheet": return sheet
    # レールの行: 実行時は Content を消して HeaderBand を出す
    if "/ArtistRail/Row" in p:
        if name == "Content" and p.count("/") == 5: return False
        if name == "HeaderBand": return True
    # 検索の × は文字が入っているときだけ。重なり確認のため出しておく
    if name == "SearchClear": return True
    return is_self_active(n)

def is_self_active(n):
    return n.get("_self", True)

def mark_self(nodes):
    # json の active は「親まで含めて」。自分だけの状態を復元する
    byp = {n["p"]: n for n in nodes}
    for n in nodes:
        parent = n["p"].rsplit("/", 1)[0] if n["p"].count("/") > 1 else "/"
        pa = byp[parent]["active"] if parent in byp else True
        n["_self"] = n["active"] if pa else True  # 親が非表示なら自分の値は不明 → 出す側に倒す

def area(n): return max(0, n["R"]-n["L"]) * max(0, n["B"]-n["T"])
def inter(a, b):
    w = min(a["R"], b["R"]) - max(a["L"], b["L"]); h = min(a["B"], b["B"]) - max(a["T"], b["T"])
    return (w, h) if w > 0.5 and h > 0.5 else None
def related(a, b): return a["p"].startswith(b["p"] + "/") or b["p"].startswith(a["p"] + "/")
def clickable(n): return "selectable" in n or n.get("collider") or n.get("input")
def is_text(n): return "text" in n and area(n) > 0

def report(kind, tab=None, sheet=False, label=""):
    nodes, W, H = load(kind); mark_self(nodes)
    vis = state(nodes, tab, sheet)
    probs = []
    texts = [n for n in vis if is_text(n)]
    for a, b in itertools.combinations(texts, 2):
        if related(a, b): continue
        o = inter(a, b)
        if o: probs.append(("文字×文字", a["p"], b["p"], o))
    clicks = [n for n in vis if clickable(n) and area(n) > 0]
    for a, b in itertools.combinations(clicks, 2):
        if related(a, b): continue
        o = inter(a, b)
        if o: probs.append(("押す所×押す所", a["p"], b["p"], o))
    for n in vis:
        if area(n) == 0: continue
        if "Shadow" in n["p"].rsplit("/",1)[-1]: continue
        if "/MoreSheet" in n["p"]: continue
        if n["L"] < -0.5 or n["T"] < -0.5 or n["R"] > W+0.5 or n["B"] > H+0.5:
            probs.append(("はみ出し", n["p"], f'({n["L"]:.0f},{n["T"]:.0f})-({n["R"]:.0f},{n["B"]:.0f})', None))
    for n in clicks:
        short = min(n["R"]-n["L"], n["B"]-n["T"])
        if short < 35 and "Strip" not in n["p"]:
            probs.append(("小さい", n["p"], f"{short:.0f}px", None))
    print(f"\n==== {kind} {label}  (見えている部品 {len(vis)})")
    for kind_, a, b, o in probs:
        extra = f"  重なり {o[0]:.0f}×{o[1]:.0f}px" if o else ""
        print(f"  [{kind_}] {a}  ↔  {b}{extra}")
    if not probs: print("  問題なし")
    return probs

if __name__ == "__main__":
    for t in range(6): report("wall", tab=t, label=f"タブ{t}")
    report("wall", tab=0, sheet=True, label="タブ0 + 「…」シート")
    report("remote", label="リモコン")
