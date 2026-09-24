import json, re, html, sys
from analyze import load, mark_self, state

SONGS = [("夜に駆ける","YOASOBI · J-POP","4:23"),("アイドル","YOASOBI · アニメ","3:34"),
         ("怪物","YOASOBI · アニメ","3:35"),("群青","YOASOBI · J-POP","4:44"),
         ("あの夢をなぞって","YOASOBI · J-POP","4:12"),("たぶん","YOASOBI · J-POP","4:03")]
ARTISTS = [("すべて",1043),("Ado",38),("YOASOBI",31),("Official髭男dism",27),("米津玄師",24),("優里",19),("Aimer",17)]
CARDS = [("アイドル","YOASOBI","よく聴くアーティスト"),("Subtitle","Official髭男dism","お気に入りと同じジャンル"),
         ("残響散歌","Aimer","まだ聴いたことがない"),("Lemon","米津玄師","この中でよく再生されている"),
         ("怪物","YOASOBI","さっき聴いた曲と似ている"),("ドライフラワー","優里","しばらく聴いていない")]
FIXED = {
 r"/NowPlaying/Title$":"夜に駆ける", r"/NowPlaying/Artist$":"YOASOBI", r"/GenreChip/Genre$":"J-POP",
 r"/NowPlaying/Time$":"1:48 / 4:23", r"/NowPlaying/State$":"▶ 再生中", r"/NowPlaying/Remaining$":"残り 2:35",
 r"/NowPlayingBand/Status$":"アイドル を 再生予定に追加しました。",
 r"/Tab0/Label$":"曲  31", r"/Tab1/Label$":"おすすめ", r"/Tab2/Label$":"お気に入り  12",
 r"/Tab3/Label$":"履歴  48", r"/Tab4/Label$":"再生予定  3",
 r"/PlayPause/Label$":"‖  一時停止", r"/Volume/VolumeText$":"音量 80%", r"/Range$":"1〜6 / 31 件",
}

def fill(n):
    p = n["p"]
    for pat, s in FIXED.items():
        if re.search(pat, p): return s
    m = re.search(r"/(?:Songs|Page[234])/Row(\d)/Content/Hit/(Index|Title|Sub|Duration)$", p)
    if m:
        i = int(m.group(1)); t, s, d = SONGS[i % len(SONGS)]
        k = m.group(2)
        if k == "Index": return "" if i == 0 and "/Songs/" in p else str(i + 1)
        return {"Title": t, "Sub": s, "Duration": d}[k]
    m = re.search(r"/ArtistRail/Row(\d)/HeaderBand/(Channel|Count)$", p)
    if m:
        a, c = ARTISTS[int(m.group(1))]
        return a if m.group(2) == "Channel" else f"{c} 曲"
    m = re.search(r"/Card(\d)/(Title|Artist|Reason)$", p)
    if m:
        t, a, r = CARDS[int(m.group(1))]
        return {"Title": t, "Artist": a, "Reason": r}[m.group(2)]
    return n.get("text", "")

def force_active(p):
    # 実行中の見た目:再生中の行と、選ばれているアーティスト
    return (re.search(r"/Songs/Row0/(Highlight|NowPlayingBar)$", p)
            or re.search(r"/Songs/Row0/Content/Hit/Equalizer(/Bar\d)?$", p)
            or re.search(r"/ArtistRail/Row0/(Highlight|NowPlayingBar)$", p))

def rgba(n, pre=""):
    r, g, b, a = (n.get(pre+"r", 0), n.get(pre+"g", 0), n.get(pre+"b", 0), n.get(pre+"a", 0))
    return f"rgba({int(r*255)},{int(g*255)},{int(b*255)},{a})"

def render(kind, tab, sheet, out):
    nodes, W, H = load(kind); mark_self(nodes)
    vis = {n["p"] for n in state(nodes, tab, sheet)}
    extra = {n["p"] for n in nodes if force_active(n["p"])}
    parts = []
    for n in nodes:
        p = n["p"]
        if p not in vis and p not in extra: continue
        if p in extra and tab != 0: continue
        w, h = n["R"]-n["L"], n["B"]-n["T"]
        if w <= 0 or h <= 0: continue
        style = f"left:{n['L']:.1f}px;top:{n['T']:.1f}px;width:{w:.1f}px;height:{h:.1f}px;"
        name = p.rsplit("/",1)[-1]
        if n.get("img") and n.get("a", 0) > 0.002:
            radius = min(14, h/2) if h < 30 else (22 if w > 300 else 14)
            if "Shadow" in name: radius = 22
            width = w
            if name in ("Fill",) or "Fill" in name:
                pass
            if re.search(r"/Seek/.*Fill", p) or name == "SeekFill": width = w * 0.41
            parts.append(f'<div style="{style}width:{width:.1f}px;background:{rgba(n)};border-radius:{radius}px"></div>')
        t = fill(n) if "fontSize" in n else ""
        if "/Equalizer/Bar" in p:
            parts.append(f'<div style="{style}background:#2F6BFF;border-radius:2px;transform-origin:bottom;transform:scaleY({[0.6,1,0.45][int(name[-1])]})"></div>')
        if t:
            al = n["align"]
            jh = "flex-start" if "Left" in al else ("flex-end" if "Right" in al else "center")
            jv = "flex-start" if al.startswith("Upper") else ("flex-end" if al.startswith("Lower") else "center")
            color = rgba(n, "t")
            if re.search(r"/Songs/Row0/Content/Hit/Title$", p): color = "#2F6BFF"
            fs = n["fontSize"]
            if name in ("Title","Artist","Channel","Sub","Status"):
                # FittedLabel:入り切らなければ縮む
                from analyze2 import text_width
                while fs > 13 and text_width(t, fs) > w: fs -= 1
            parts.append(f'<div style="{style}display:flex;justify-content:{jh};align-items:{jv};font-size:{fs}px;color:{color};line-height:1.1;white-space:nowrap;overflow:hidden">{html.escape(t)}</div>')
    doc = f'''<html><head><meta charset="utf-8"><style>
    body{{margin:0;background:radial-gradient(60% 80% at 20% 10%,#3b3350,transparent 60%),radial-gradient(70% 70% at 90% 90%,#1d3a48,transparent 60%),#1c222c;width:{W+80}px;height:{H+80}px}}
    #p{{position:absolute;left:40px;top:40px;width:{W}px;height:{H}px}}
    #p div{{position:absolute;box-sizing:border-box;font-family:"Noto Sans CJK JP","Noto Sans JP",sans-serif}}
    </style></head><body><div id="p">{"".join(parts)}</div></body></html>'''
    open(out, "w", encoding="utf-8").write(doc)

if __name__ == "__main__":
    for t in range(5): render("wall", t, False, f"wall_tab{t}.html")
    render("wall", 0, True, "wall_sheet.html")
    render("remote", None, False, "remote.html")
    print("ok")
