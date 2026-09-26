# 実行中に入る文字の「長いほう」を入れて、枠に収まるか見る
from analyze import load, mark_self
from analyze2 import text_width
import re

SAMPLES = [
    (r"/Browser/Tab0/Label$", "曲  1043"),
    (r"/Browser/Tab1/Label$", "おすすめ"),
    (r"/Browser/Tab2/Label$", "お気に入り  128"),
    (r"/Browser/Tab3/Label$", "プレイリスト  20"),
    (r"/Browser/Tab4/Label$", "履歴  100"),
    (r"/Browser/Tab5/Label$", "再生予定  64"),
    # プレイリスト(Phase8-3)。名前は 24 文字まで。保存ボタンは縮めて収める(14px まで)。
    (r"/SaveBar/Save/Label$", "「" + "あ" * 24 + "」に上書き保存"),
    (r"/Page3/Row0/Content/Hit/Duration$", "10:23:45"),
    (r"/Page3/Row0/Content/Favorite/Label$", "消す"),
    (r"/Transport/PlayPause/Label$", "‖  一時停止"),
    (r"/Volume/VolumeText$", "音量 100%"),
    (r"/NowPlaying/Time$", "12:34 / 58:07"),
    (r"/NowPlaying/State$", "‖ 一時停止"),
    (r"/NowPlaying/Remaining$", "残り 45:33"),
    (r"/NowPlayingBand/Status$", "夜に駆ける を 再生予定に追加しました。"),
    (r"/NowPlayingBand/SyncOwner$", "操作中: たいや"),
    (r"/NowPlaying/GenreChip/Genre$", "アニメ"),
    (r"/Range$", "1〜6 / 1043 件"),
    (r"/ArtistRail/Row0/HeaderBand/Channel$", "Official髭男dism"),
    (r"/ArtistRail/Row1/HeaderBand/Channel$", "YOASOBI"),
    (r"/ArtistRail/Row0/HeaderBand/Count$", "1043 曲"),
    (r"/Songs/Row0/Content/Hit/Index$", "1043"),
    (r"/Songs/Row0/Content/Hit/Duration$", "12:34"),
    (r"/Sort/Label$", "追加が新しい順"),
]
FITTED = ("Title", "Artist", "Channel", "Sub")   # FittedLabel は小さくして収める

def check():
    """入り切らないもの(縮めても入らないもの)だけを返す。"""
    problems = []
    for kind in ("wall", "remote"):
        nodes, W, H = load(kind)
        for n in nodes:
            for pat, s in SAMPLES:
                if not (re.search(pat, n["p"]) and "fontSize" in n): continue
                have = n["R"] - n["L"]
                name = n["p"].rsplit("/", 1)[-1]
                size = 13 if name in FITTED else n["fontSize"]
                if re.search(r"/SaveBar/Save/Label$", n["p"]): size = 14      # 縮めて 14px まで
                if re.search(r"/Page3/Row\d+/Content/Favorite/Label$", n["p"]): size = 16
                if text_width(s, size) > have:
                    problems.append(f"[文字が枠に入らない] {kind} {n['p']} 「{s}」 "
                                    f"必要≈{text_width(s, size):.0f}px / 枠 {have:.0f}px")
    return problems


if __name__ == "__main__":
    for line in check(): print(line)
