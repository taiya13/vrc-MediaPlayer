#!/usr/bin/env python3
"""壁パネルの配置を調べる。

なぜ要るのか
────────────
UI の置き場所は「数を足した結果」で決まります。コンパイラも
EditMode テストも、<b>部品どうしが重なっていること</b>は教えてくれません。
実機で組み上げて初めて「文字の上にバーが乗っている」と分かります。

Phase8-2 で帯を組み直したとき、この検査を書いた直後に
6 か所の重なりが見つかりました。目で追える量ではありません。

何を見るのか
────────────
1. 帯(いま流れている)の中で、部品どうしが重なっていないか
2. 帯からはみ出していないか
3. 縦の積み上げ(帯 → 検索 → タブ → 本体)が板に収まるか
4. 一覧とレールが、足もとのボタンと重ならずに何行入るか
5. 触れるものが 4.5 cm(≒ 35 px)を割っていないか

寸法は UdonMediaPanelBuilder.cs から読み取ります。
<b>ここに数を書き写さない</b>ため —— 写すと必ずずれます。
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
BUILDER = ROOT / "Assets/SmartMediaPlatform/World/Editor/UdonMediaPanelBuilder.cs"

# 1 px ≒ 1.3 mm。触れるところの下限は 4.5 cm。
MIN_TOUCH_PX = 35

Space1, Space2, Space3, Space4 = 8, 16, 24, 32


def read_const(source: str, name: str) -> float:
    """`private const float X = 123f;` から数を取り出す。"""
    hit = re.search(
        r"const\s+(?:float|int)\s+" + re.escape(name) + r"\s*=\s*([0-9.]+)f?\s*;",
        source,
    )
    if not hit:
        sys.exit(f"{name} を UdonMediaPanelBuilder.cs から読み取れませんでした")
    return float(hit.group(1))


def overlaps(a, b) -> bool:
    return a[1] < b[3] and b[1] < a[3] and a[2] < b[4] and b[2] < a[4]


def main() -> int:
    source = BUILDER.read_text(encoding="utf-8")

    # Pad だけは UdonMediaTheme.Space4 を参照しているので、数を直接は書いていない。
    if "const float Pad = UdonMediaTheme.Space4" not in source:
        sys.exit("Pad の決め方が変わりました。この検査を直してください。")
    Pad = float(Space4)

    BandHeight = read_const(source, "BandHeight")
    ControlW = read_const(source, "BandControlWidth")
    SearchHeight = read_const(source, "SearchHeight")
    TabHeight = read_const(source, "TabHeight")
    RailWidth = read_const(source, "RailWidth")
    RailGap = read_const(source, "RailGap")

    W, H = 1400.0, 860.0
    inner = W - Pad * 2

    problems = []

    # ───────── 帯 ─────────
    controlX = inner - ControlW - Space3
    textWidth = controlX - Space3 - Space4
    barWidth = inner - Space3 * 2
    sx = Space3

    kickerH = read_const(source, "BandKickerHeight")
    titleH = read_const(source, "BandTitleHeight")
    artistH = read_const(source, "BandArtistHeight")
    metaH = read_const(source, "BandMetaHeight")
    seekTouch = read_const(source, "BandSeekTouch")
    titleTop = read_const(source, "BandTitleTop")

    artistTop = titleTop + titleH + 2
    metaTop = artistTop + artistH + 2
    barTop = metaTop + metaH + 2
    total = barTop + seekTouch

    if total != BandHeight:
        problems.append(
            f"帯の中身({total})と BandHeight({BandHeight:.0f})が合いません")

    chipW, chipH = 120, 24
    artistW = textWidth - chipW - Space2

    boxes = []

    def add(name, x, y, w, h, touch=False):
        boxes.append((name, x, y, x + w, y + h, touch))

    add("Kicker", sx, 4, 400, kickerH)
    add("SyncOwner", sx + 400, 4, textWidth - 400, 18)
    add("Title", sx, titleTop, textWidth, titleH)
    add("Artist", sx, artistTop, artistW, artistH)
    add("GenreChip", sx + artistW + Space2,
        artistTop + (artistH - chipH) / 2, chipW, chipH)
    add("Time", sx, metaTop, 150, metaH)
    add("State", sx + 156, metaTop, 240, metaH)
    add("Status", sx + 410, metaTop, 250, metaH)
    add("Remaining", sx + textWidth - 150, metaTop, 150, metaH)
    add("Seek", sx, barTop, barWidth, seekTouch, touch=True)
    add("Transport", controlX, 6, ControlW, 52, touch=True)
    add("VolumeLabel", controlX + 84 + Space2, 60, ControlW - 84 * 2 - Space2 * 2, 22)
    add("VolumeBar", controlX, 84, ControlW, 36, touch=True)

    for i in range(len(boxes)):
        for j in range(i + 1, len(boxes)):
            if overlaps(boxes[i], boxes[j]):
                problems.append(f"帯の中で重なっています: {boxes[i][0]} × {boxes[j][0]}")

    for name, x0, y0, x1, y1, _ in boxes:
        if x0 < 0 or y0 < 0 or x1 > inner or y1 > BandHeight:
            problems.append(
                f"帯からはみ出しています: {name} = ({x0:.0f},{y0:.0f})-({x1:.0f},{y1:.0f})")

    for name, _, y0, _, y1, touch in boxes:
        if touch and (y1 - y0) < MIN_TOUCH_PX:
            problems.append(
                f"触れるところが小さすぎます: {name} = {y1 - y0:.0f} px "
                f"(下限 {MIN_TOUCH_PX} px ≒ 4.5 cm)")

    # ───────── 縦の積み上げ ─────────
    browser_top = Pad + BandHeight + Space2
    browser_h = H - Pad - browser_top
    page_top = (SearchHeight + Space2) + TabHeight + Space2
    page_h = browser_h - page_top

    if page_h <= 0:
        problems.append("帯・検索・タブで板を使い切っていて、一覧の場所がありません")

    # ───────── 一覧 ─────────
    RowH, RowGap, FooterH = 64.0, 6.0, 48.0

    def rows_for(top):
        room = page_h - top - FooterH - Space1
        return int((room + RowGap) // (RowH + RowGap)), room

    song_rows, _ = rows_for(0)
    fav_rows, _ = rows_for(48 + Space1)   # 並べ替えボタンのぶん

    if song_rows < 4:
        problems.append(f"曲の一覧が {song_rows} 行しか入りません")

    # ───────── レール ─────────
    RailHeaderH, RailRowH, RailRowGap, RailRows, RailFooterH = 30.0, 48.0, 6.0, 7, 48.0
    rail_top = RailHeaderH + Space1
    rail_bottom = rail_top + RailRows * (RailRowH + RailRowGap) - RailRowGap
    rail_footer_y = page_h - RailFooterH - Space1

    if rail_bottom > rail_footer_y:
        problems.append(
            f"レールの行({rail_bottom:.0f})が足もとのボタン({rail_footer_y:.0f})に"
            f"かぶっています")
    if rail_footer_y + RailFooterH > page_h:
        problems.append("レールの足もとのボタンが枠からはみ出しています")
    if RailRowH < MIN_TOUCH_PX:
        problems.append(f"レールの行が小さすぎます: {RailRowH:.0f} px")

    # ───────── おすすめのカード ─────────
    card_w = (inner - Space2 * 2) / 3
    card_h = (page_h - Space2) / 2

    # ───────── 出力 ─────────
    print("壁パネル 1400 × 860")
    print(f"  帯          {BandHeight:.0f}  (文字 {textWidth:.0f} / 操作 {ControlW:.0f})")
    print(f"  検索        {SearchHeight:.0f}")
    print(f"  タブ        {TabHeight:.0f}")
    print(f"  本体        {page_h:.0f}")
    print(f"  一覧        {song_rows} 行 (お気に入り {fav_rows} 行)")
    print(f"  レール      {RailWidth:.0f} 幅 / {RailRows} 行 (間 {RailGap:.0f})")
    print(f"  おすすめ    {card_w:.0f} × {card_h:.0f} を 3 × 2")
    print()

    if problems:
        for p in problems:
            print("  ✗ " + p)
        print(f"\n{len(problems)} 件の置き場所の問題があります。")
        return 1

    print("重なり・はみ出し・小さすぎる当たり判定はありません。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
