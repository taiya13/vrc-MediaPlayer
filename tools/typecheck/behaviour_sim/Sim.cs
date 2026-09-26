// U# の実物を組み合わせて、プレイリストの流れを通す(Phase8-3)。
// 入口は run.py。VRChat の部品は run.py が中身を持たせたスタブです。
using System;
using System.Reflection;
using SmartMediaPlatform.Catalog.Udon;
using SmartMediaPlatform.World.Udon;
using SmartMediaPlatform.World.Udon.UI;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

public static class Sim
{
    static int _failures;
    static int _checks;

    static void Check(bool ok, string what)
    {
        _checks++;
        if (ok) return;
        _failures++;
        Console.WriteLine("  ✗ " + what);
    }

    static void Equal<T>(T expected, T actual, string what)
    {
        Check(Equals(expected, actual), what + "  (期待: " + expected + " / 実際: " + actual + ")");
    }

    static void Contains(string text, string part, string what)
    {
        Check(text != null && text.Contains(part), what + "  (「" + part + "」が無い: 「" + text + "」)");
    }

    // ───────── 組み立て ─────────

    class World
    {
        public UdonMediaCatalog Catalog;
        public UdonCatalogStore Store;
        public UdonPlayerSession Session;
        public UdonMediaController Controller;
        public UdonPlaylistShelf Shelf;
        public UdonMediaListView View;
        public UdonMediaPanel Panel;
        public VRCUrlInputField NameField;
        public Text SaveLabel;
        public UdonMediaListRow[] Rows;

        public string Status { get { return Panel.StatusText.text; } }
        public string Key { get { string v; PlayerData.SimStore.TryGetValue(UdonPlaylistShelf.StorageKey, out v); return v; } }
    }

    static T Make<T>(string name) where T : Component
    {
        return new GameObject(name).AddComponent<T>();
    }

    static void CallPrivate(object target, string method)
    {
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)
              .Invoke(target, null);
    }

    static World Build(int songs, bool restore)
    {
        var w = new World();

        w.Catalog = Make<UdonMediaCatalog>("Catalog");
        w.Catalog.Ids = new string[songs];
        w.Catalog.Titles = new string[songs];
        w.Catalog.Artists = new string[songs];
        w.Catalog.Genres = new string[songs];
        w.Catalog.Types = new int[songs];
        w.Catalog.Urls = new VRCUrl[songs];
        w.Catalog.Durations = new int[songs];
        w.Catalog.TagValues = new string[0];
        w.Catalog.TagOffsets = new int[songs + 1];
        w.Catalog.RelatedIds = new string[0];
        w.Catalog.RelatedOffsets = new int[songs + 1];

        for (int i = 0; i < songs; i++)
        {
            w.Catalog.Ids[i] = "id" + i;
            w.Catalog.Titles[i] = "曲" + i;
            w.Catalog.Artists[i] = "歌手" + (i % 3);
            w.Catalog.Genres[i] = "J-POP";
            w.Catalog.Types[i] = UdonMediaCatalog.TypeVideo;
            w.Catalog.Durations[i] = 200 + i;
        }

        w.Store = Make<UdonCatalogStore>("Store");
        w.Store.Catalog = w.Catalog;

        w.Session = Make<UdonPlayerSession>("Session");
        w.Session.Store = w.Store;

        w.Controller = Make<UdonMediaController>("Controller");
        w.Controller.Session = w.Session;

        w.Shelf = Make<UdonPlaylistShelf>("Playlists");
        w.Shelf.Catalog = w.Catalog;
        w.Shelf.Session = w.Session;
        w.Shelf.LogPersistence = false;
        CallPrivate(w.Shelf, "Start");

        w.Panel = Make<UdonMediaPanel>("Panel");
        w.Panel.StatusText = Make<Text>("Status");

        w.View = Make<UdonMediaListView>("Page3");
        w.View.Source = UdonMediaListView.SourcePlaylist;
        w.View.Shelf = w.Shelf;
        w.View.Controller = w.Controller;
        w.View.Session = w.Session;
        w.View.Store = w.Store;
        w.View.Panel = w.Panel;

        w.NameField = Make<VRCUrlInputField>("NameField");
        w.View.NameField = w.NameField;
        w.SaveLabel = Make<Text>("SaveLabel");
        w.View.SaveLabel = w.SaveLabel;

        w.Rows = new UdonMediaListRow[5];
        for (int i = 0; i < w.Rows.Length; i++)
        {
            var row = Make<UdonMediaListRow>("Row" + i);
            row.Row = i;
            row.List = w.View;
            row.TitleText = Make<Text>("Title");
            row.SubText = Make<Text>("Sub");
            row.DurationText = Make<Text>("Duration");
            row.FavoriteLabel = Make<Text>("FavoriteLabel");
            row.FavoriteButton = new GameObject("Favorite");
            row.Content = new GameObject("Content");
            w.Rows[i] = row;
        }
        w.View.Rows = w.Rows;

        if (restore) w.Shelf.OnPlayerRestored(Networking.LocalPlayer);

        w.View.Refresh();
        return w;
    }

    static void Tick(float seconds)
    {
        Time.SimNow += seconds;
    }

    static void Queue(World w, int current, params int[] queued)
    {
        w.Session.ClearUpcoming();
        if (current >= 0) w.Session.PlayAt(current);
        foreach (int q in queued) w.Session.Enqueue(q);
    }

    static string QueueText(World w)
    {
        string s = "";
        for (int i = 0; i < w.Session.QueueCount; i++) s += (i > 0 ? "," : "") + w.Session.GetQueueAt(i);
        return s;
    }

    static void Fresh()
    {
        PlayerData.SimStore.Clear();
        PlayerData.SimWrites = 0;
        Networking.SimLocal = new VRCPlayerApi { isLocal = true, displayName = "me" };
        Time.SimNow = 100f;
    }

    // ───────── 流れ ─────────

    static void Scenario(string name, Action body)
    {
        Console.WriteLine("■ " + name);
        Fresh();
        try { body(); }
        catch (Exception e)
        {
            _failures++;
            Console.WriteLine("  ✗ 例外: " + e);
        }
    }

    public static int Main()
    {
        Scenario("保存データが届く前は、保存も削除もしない(残っていたものを空で上書きしない)", () =>
        {
            PlayerData.SimStore[UdonPlaylistShelf.StorageKey] = "SMPPL1\n前から\tid1";
            var w = Build(10, false);
            Queue(w, 2, 5);

            w.View.SavePlaylist();
            Contains(w.Status, "読み込み中", "届く前の保存は断る");
            Equal(0, PlayerData.SimWrites, "何も書いていない");
            Equal("読み込み中…", w.SaveLabel.text, "ボタンも待ちを示す");

            w.Shelf.OnPlayerRestored(new VRCPlayerApi { isLocal = false });
            Check(!w.Shelf.IsReady, "他人の分が届いても、まだ待つ");

            w.Shelf.OnPlayerRestored(Networking.LocalPlayer);
            Check(w.Shelf.IsReady, "自分の分が届いたら使える");
            Equal(1, w.Shelf.Count, "残っていたものを読んだ");
            Equal("前から", w.Shelf.NameAt(0), "名前も読めた");
        });

        Scenario("保存 → 入り直し → 同じ中身が戻る", () =>
        {
            var w = Build(10, true);

            w.View.SavePlaylist();
            Contains(w.Status, "保存する曲がありません", "何も無ければ保存しない");
            Equal(0, w.Shelf.Count, "空のまま");

            Queue(w, 2, 5, 7);
            w.NameField.SimText = "  作業用 ";
            w.View.Refresh();
            Equal("「作業用」として保存", w.SaveLabel.text, "押す前に新しく作ると分かる");

            Tick(1f);
            w.View.SavePlaylist();
            Contains(w.Status, "「作業用」を保存しました(3 曲)", "保存した");
            Equal(1, w.Shelf.Count, "1 本");
            Equal("id2,id5,id7", string.Join(",", w.Shelf.IdsAt(0)), "鳴っている曲 + 再生予定の順");
            Contains(w.Key, "作業用\tid2\tid5\tid7", "PlayerData に書いた");

            w.View.Refresh();
            Equal("「作業用」に上書き保存", w.SaveLabel.text, "保存後は上書きになると分かる");
            Equal("作業用", w.Rows[0].TitleText.text, "行に名前");
            Contains(w.Rows[0].SubText.text, "3 曲 · 曲2 / 曲5 / 曲7", "行に中身");
            Equal("10:14", w.Rows[0].DurationText.text, "長さの合計(202+205+207 = 614 秒)");
            Equal("×", w.Rows[0].FavoriteLabel.text, "消すボタン");

            // 入り直す
            string saved = w.Key;
            var again = Build(10, true);
            Equal(1, again.Shelf.Count, "入り直しても残っている");
            Equal("id2,id5,id7", string.Join(",", again.Shelf.IdsAt(0)), "中身も同じ");
            Equal(saved, again.Key, "読んだだけでは書き換えない");
        });

        Scenario("名前欄が空なら、最後に使ったものへ上書き / それも無ければ自動の名前", () =>
        {
            var w = Build(10, true);
            Queue(w, 1);

            w.View.SavePlaylist();
            Equal("プレイリスト 1", w.Shelf.NameAt(0), "自動の名前");

            Queue(w, 1, 2);
            Tick(1f);
            w.View.SavePlaylist();
            Equal(1, w.Shelf.Count, "2 回押しても増えない(同じものへ上書き)");
            Contains(w.Status, "上書き保存しました(2 曲)", "上書きと伝える");

            w.NameField.SimText = "夜";
            Tick(1f);
            w.View.SavePlaylist();
            Equal(2, w.Shelf.Count, "打った名前なら新しく作る");

            // 同じ文字が残っている間は、使い終わった名前として扱う
            Queue(w, 3);
            Tick(1f);
            w.View.SavePlaylist();
            Equal(2, w.Shelf.Count, "残った文字では増えない");
            Equal("id3", w.Shelf.IdsAt(1)[0], "「夜」へ上書き");
        });

        Scenario("行を押す = 再生予定を置き換えて 1 曲目から / ＋ = 後ろに足す", () =>
        {
            var w = Build(10, true);
            Queue(w, 2, 5, 7);
            w.NameField.SimText = "A";
            w.View.SavePlaylist();

            Queue(w, 0, 1, 3);
            w.View.Refresh();

            Tick(1f);
            w.View.OnRowPrimary(0);
            Equal(2, w.Session.CurrentIndex, "1 曲目が鳴る");
            Equal("5,7", QueueText(w), "残りが再生予定に(前の予定は消える)");
            Contains(w.Status, "「A」を再生します(3 曲)", "伝える");

            Queue(w, 0, 1);
            Tick(1f);
            w.View.OnRowSecondary(0);
            Equal(0, w.Session.CurrentIndex, "＋ では鳴っている曲は変わらない");
            Equal("1,2,5,7", QueueText(w), "後ろに足す");
            Contains(w.Status, "3 曲を再生予定に追加しました", "伝える");

            Tick(1f);
            w.View.OnRowSecondary(0);
            Contains(w.Status, "再生予定がいっぱいか、もう全部入っています", "全部入っていれば断る");
            Equal("1,2,5,7", QueueText(w), "増えない");
        });

        Scenario("1 曲も鳴らせないときは、再生予定を消さない", () =>
        {
            var w = Build(10, true);
            Queue(w, 0, 1, 3);

            bool ok = w.Controller.LoadList(new[] { 99, -1 }, true);
            Check(!ok, "鳴らせないなら false");
            Equal("1,3", QueueText(w), "再生予定はそのまま");
            Equal(0, w.Session.CurrentIndex, "鳴っている曲もそのまま");

            Check(!w.Controller.LoadList(new int[0], true), "空なら何もしない");
            Check(!w.Controller.LoadList(null, false), "null でも落ちない");
        });

        Scenario("カタログから消えた曲は飛ばし、数を伝える", () =>
        {
            PlayerData.SimStore[UdonPlaylistShelf.StorageKey] = "SMPPL1\n古い\tid1\tgone\tID3\tid1";
            var w = Build(10, true);

            Contains(w.Rows[0].SubText.text, "(1 曲は見つかりません)", "行に出す");

            Tick(1f);
            w.View.OnRowPrimary(0);
            Equal(1, w.Session.CurrentIndex, "残りで再生");
            Equal("3", QueueText(w), "大文字の ID もカタログで見つかる");
            Contains(w.Status, "1 曲はカタログに見つかりません", "伝える");

            PlayerData.SimStore[UdonPlaylistShelf.StorageKey] = "SMPPL1\n全滅\tx\ty";
            var none = Build(10, true);
            Tick(1f);
            none.View.OnRowPrimary(0);
            Contains(none.Status, "どれもカタログに見つかりません", "全部無ければ再生しない");
            Equal(-1, none.Session.CurrentIndex, "何も鳴らない");
        });

        Scenario("消すのは 2 回押したときだけ", () =>
        {
            var w = Build(10, true);
            Queue(w, 1);
            w.NameField.SimText = "A";
            w.View.SavePlaylist();
            w.NameField.SimText = "B";
            Tick(1f);
            w.View.SavePlaylist();

            Tick(1f);
            w.View.OnRowFavorite(0);
            Equal(2, w.Shelf.Count, "1 回目では消えない");
            Equal("消す", w.Rows[0].FavoriteLabel.text, "確かめ中の表示");
            Equal("×", w.Rows[1].FavoriteLabel.text, "ほかの行はそのまま");
            Contains(w.Status, "もう一度", "伝える");

            Tick(0.1f);
            w.View.OnRowFavorite(0);          // 二重発火(「使う」と uGUI の両方)
            Equal(2, w.Shelf.Count, "すぐの 2 回目(二重発火)では消えない");

            Tick(0.2f);
            w.View.OnRowFavorite(0);          // 0.3 秒。二重発火よけは抜けるが、最短の間より早い
            Equal(2, w.Shelf.Count, "早すぎる 2 回目では消えない");

            Tick(1f);
            w.View.OnRowFavorite(0);
            Equal(1, w.Shelf.Count, "確かめてから押せば消える");
            Equal("B", w.Shelf.NameAt(0), "消したのは押した行");
            Check(!w.Key.Contains("A\t"), "PlayerData からも消えた");
            Contains(w.Status, "「A」を消しました", "伝える");

            // 時間切れ
            Tick(1f);
            w.View.OnRowFavorite(0);
            Tick(DefaultConfirm() + 1f);
            w.View.Refresh();
            Equal("×", w.Rows[0].FavoriteLabel.text, "時間が経てば元に戻る");
            w.View.OnRowFavorite(0);
            Equal(1, w.Shelf.Count, "時間切れのあとの 1 回では消えない");
        });

        Scenario("描いたあとに並びが変わっても、押した名前のものを扱う", () =>
        {
            var w = Build(10, true);
            Queue(w, 1);
            w.NameField.SimText = "A"; w.View.SavePlaylist();
            Queue(w, 2);
            w.NameField.SimText = "B"; Tick(1f); w.View.SavePlaylist();
            w.View.Refresh();
            Equal("B", w.Rows[1].TitleText.text, "2 行目は B");

            // 別のパネルなどから A が消された(この一覧はまだ書き直していない)
            w.Shelf.Remove(0);

            Queue(w, 5);
            Tick(1f);
            w.View.OnRowPrimary(1);
            Equal(2, w.Session.CurrentIndex, "押した行の B が流れる(ずれない)");

            Tick(1f);
            w.View.OnRowPrimary(1);           // B は 1 行目へ詰まったので、2 行目は空
            Equal(2, w.Session.CurrentIndex, "空の行は何もしない");
        });

        Scenario("読めない形のデータは、上書きする前に逃がす", () =>
        {
            PlayerData.SimStore[UdonPlaylistShelf.StorageKey] = "garbage";
            var w = Build(10, true);

            Equal(0, w.Shelf.Count, "読めなければ空");
            Check(w.Shelf.IsReady, "使える状態にはなる");
            string backup;
            PlayerData.SimStore.TryGetValue(UdonPlaylistShelf.UnreadableKey, out backup);
            Equal("garbage", backup, "別の場所に控えた");

            Queue(w, 1);
            w.View.SavePlaylist();
            Equal(1, w.Shelf.Count, "そのあと保存できる");
            Check(w.Key.StartsWith("SMPPL1\n"), "新しい形で書いた");
        });

        Scenario("本数の上限", () =>
        {
            var w = Build(10, true);
            Queue(w, 1);

            for (int i = 0; i < UdonPlaylistShelf.MaxPlaylists; i++)
            {
                w.NameField.SimText = "P" + i;
                Tick(1f);
                w.View.SavePlaylist();
            }
            Equal(UdonPlaylistShelf.MaxPlaylists, w.Shelf.Count, "上限まで入る");

            w.NameField.SimText = "もう 1 本";
            Tick(1f);
            w.View.SavePlaylist();
            Contains(w.Status, "20 本まで", "上限を伝える");
            Equal(UdonPlaylistShelf.MaxPlaylists, w.Shelf.Count, "増えない");

            w.NameField.SimText = "P3";
            Tick(1f);
            w.View.SavePlaylist();
            Contains(w.Status, "上書き保存しました", "上書きならできる");
        });

        Scenario("64 曲を超える再生予定は、入るぶんだけ保存して伝える", () =>
        {
            var w = Build(80, true);
            w.Session.PlayAt(0);
            for (int i = 1; i < 80; i++) w.Session.Enqueue(i);    // 再生予定は 64 まで

            w.View.SavePlaylist();
            Equal(UdonPlaylistShelf.MaxSongs, w.Shelf.SongCountAt(0), "64 曲まで");
            Contains(w.Status, "入るのは 64 曲まで", "切り捨てたと伝える");

            Queue(w, 70);
            Tick(1f);
            w.View.OnRowPrimary(0);
            Equal(0, w.Session.CurrentIndex, "1 曲目");
            Equal(63, w.Session.QueueCount, "残り 63 曲が再生予定に");
        });

        Scenario("エディタで ClientSim を使わない(LocalPlayer が無い)ときは、覚えるだけで動く", () =>
        {
            Networking.SimLocal = null;
            var w = Build(10, false);
            Check(w.Shelf.IsReady, "合図を待たずに使える");

            Queue(w, 1);
            w.View.SavePlaylist();
            Equal(1, w.Shelf.Count, "保存できる");
            Equal(0, PlayerData.SimWrites, "PlayerData には書かない");
        });

        Scenario("タブの見出し", () =>
        {
            var w = Build(10, true);
            Equal("プレイリスト", w.View.TabLabel(), "タブ名");
            Equal(0, w.View.TotalCount(), "0 本");
            Contains(w.View.EmptyMessageFor(), "保存", "空のときの案内");
        });

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "問題なし(" + _checks + " 項目)"
            : _failures + " 件の問題があります(" + _checks + " 項目中)");
        return _failures == 0 ? 0 : 1;
    }

    static float DefaultConfirm()
    {
        return new GameObject("tmp").AddComponent<UdonMediaListView>().DeleteConfirmSeconds;
    }
}
