using NUnit.Framework;
using SmartMediaPlatform.World.UdonModel;

namespace SmartMediaPlatform.World.UdonModel.Tests
{
    /// <summary>
    /// <see cref="PlaylistShelfModel"/> —— 名前付きで取っておくプレイリスト。
    ///
    /// <b>いちばん困るのは「取っておいたはずのものが消える」</b>ことです。
    /// 読み書きを往復しても中身が変わらないこと、
    /// 1 本が壊れていても残りが読めることを、ここで確かめます。
    /// </summary>
    public class PlaylistShelfModelTests
    {
        static string[] Ids(params string[] ids)
        {
            return ids;
        }

        static PlaylistShelfModel WithOne(string name, params string[] ids)
        {
            var shelf = new PlaylistShelfModel();
            shelf.Save(name, ids, ids.Length);
            return shelf;
        }

        // ───────── 保存 ─────────

        [Test]
        public void Save_AddsNamedPlaylist()
        {
            var shelf = new PlaylistShelfModel();

            int at = shelf.Save("作業用", Ids("a", "b", "c"), 3);

            Assert.AreEqual(0, at);
            Assert.AreEqual(1, shelf.Count);
            Assert.AreEqual("作業用", shelf.NameAt(0));
            Assert.AreEqual(3, shelf.SongCountAt(0));

            string[] ids = shelf.IdsAt(0);
            Assert.AreEqual(3, ids.Length);
            Assert.AreEqual("a", ids[0]);
            Assert.AreEqual("b", ids[1]);
            Assert.AreEqual("c", ids[2]);
        }

        [Test]
        public void Save_SameName_OverwritesInPlace()
        {
            var shelf = new PlaylistShelfModel();
            shelf.Save("A", Ids("1"), 1);
            shelf.Save("B", Ids("2"), 1);
            shelf.Save("C", Ids("3"), 1);

            int at = shelf.Save("B", Ids("x", "y"), 2);

            Assert.AreEqual(1, at);
            Assert.AreEqual(3, shelf.Count);
            Assert.AreEqual("B", shelf.NameAt(1));
            Assert.AreEqual(2, shelf.SongCountAt(1));
            Assert.AreEqual("x", shelf.IdsAt(1)[0]);

            // 前後は動かない
            Assert.AreEqual("A", shelf.NameAt(0));
            Assert.AreEqual("C", shelf.NameAt(2));
        }

        [Test]
        public void Save_NameIsTrimmedBeforeMatching()
        {
            var shelf = WithOne("夜", "1");

            int at = shelf.Save("  夜  ", Ids("2"), 1);

            Assert.AreEqual(0, at);
            Assert.AreEqual(1, shelf.Count);
            Assert.AreEqual("2", shelf.IdsAt(0)[0]);
        }

        [Test]
        public void Save_EmptyName_GetsSmallestUnusedNumber()
        {
            var shelf = new PlaylistShelfModel();

            shelf.Save("", Ids("1"), 1);
            shelf.Save("   ", Ids("2"), 1);
            shelf.Save(null, Ids("3"), 1);

            Assert.AreEqual("プレイリスト 1", shelf.NameAt(0));
            Assert.AreEqual("プレイリスト 2", shelf.NameAt(1));
            Assert.AreEqual("プレイリスト 3", shelf.NameAt(2));

            // 2 を消したら、次は 2 が埋まる
            shelf.Delete(1);
            shelf.Save("", Ids("4"), 1);

            Assert.AreEqual("プレイリスト 2", shelf.NameAt(2));
        }

        [Test]
        public void Save_RemovesDuplicateSongs_KeepsFirstOrder()
        {
            var shelf = new PlaylistShelfModel();

            shelf.Save("A", Ids("b", "a", "b", "c", "a"), 5);

            string[] ids = shelf.IdsAt(0);
            Assert.AreEqual(3, ids.Length);
            Assert.AreEqual("b", ids[0]);
            Assert.AreEqual("a", ids[1]);
            Assert.AreEqual("c", ids[2]);
            Assert.AreEqual(3, shelf.LastSavedSongs);
        }

        [Test]
        public void Save_SkipsEmptyAndNullIds()
        {
            var shelf = new PlaylistShelfModel();

            shelf.Save("A", Ids("", null, "a", "\n\t"), 4);

            Assert.AreEqual(1, shelf.SongCountAt(0));
            Assert.AreEqual("a", shelf.IdsAt(0)[0]);
        }

        [Test]
        public void Save_NoSongs_IsRefused()
        {
            var shelf = new PlaylistShelfModel();

            Assert.AreEqual(PlaylistShelfModel.ResultNothingToSave, shelf.Save("A", Ids(), 0));
            Assert.AreEqual(PlaylistShelfModel.ResultNothingToSave, shelf.Save("A", null, 3));
            Assert.AreEqual(PlaylistShelfModel.ResultNothingToSave, shelf.Save("A", Ids("", ""), 2));
            Assert.AreEqual(0, shelf.Count);
        }

        [Test]
        public void Save_CountLargerThanArray_DoesNotOverrun()
        {
            var shelf = new PlaylistShelfModel();

            int at = shelf.Save("A", Ids("a", "b"), 99);

            Assert.AreEqual(0, at);
            Assert.AreEqual(2, shelf.SongCountAt(0));
        }

        [Test]
        public void Save_OnlyReadsIdCount()
        {
            var shelf = new PlaylistShelfModel();

            shelf.Save("A", Ids("a", "b", "c"), 2);

            Assert.AreEqual(2, shelf.SongCountAt(0));
        }

        [Test]
        public void Save_CapsAtMaxSongs_AndReportsIt()
        {
            var ids = new string[PlaylistShelfModel.MaxSongs + 10];
            for (int i = 0; i < ids.Length; i++) ids[i] = "id" + i;

            var shelf = new PlaylistShelfModel();
            shelf.Save("A", ids, ids.Length);

            Assert.AreEqual(PlaylistShelfModel.MaxSongs, shelf.SongCountAt(0));
            Assert.IsTrue(shelf.LastTruncated);
            Assert.AreEqual("id0", shelf.IdsAt(0)[0]);
        }

        [Test]
        public void Save_ExactlyMaxSongs_IsNotTruncated()
        {
            var ids = new string[PlaylistShelfModel.MaxSongs];
            for (int i = 0; i < ids.Length; i++) ids[i] = "id" + i;

            var shelf = new PlaylistShelfModel();
            shelf.Save("A", ids, ids.Length);

            Assert.AreEqual(PlaylistShelfModel.MaxSongs, shelf.SongCountAt(0));
            Assert.IsFalse(shelf.LastTruncated);
        }

        [Test]
        public void Save_WhenFull_RefusesNewButAllowsOverwrite()
        {
            var shelf = new PlaylistShelfModel();
            for (int i = 0; i < PlaylistShelfModel.MaxPlaylists; i++)
            {
                Assert.AreEqual(i, shelf.Save("P" + i, Ids("x"), 1));
            }

            Assert.IsTrue(shelf.IsFull);
            Assert.AreEqual(PlaylistShelfModel.ResultFull, shelf.Save("new", Ids("y"), 1));
            Assert.AreEqual(PlaylistShelfModel.ResultFull, shelf.Save("", Ids("y"), 1));

            // 上書きなら入る
            Assert.AreEqual(3, shelf.Save("P3", Ids("y"), 1));
            Assert.AreEqual(PlaylistShelfModel.MaxPlaylists, shelf.Count);
        }

        [Test]
        public void Save_TooLarge_IsRefusedAndLeavesShelfUnchanged()
        {
            var shelf = new PlaylistShelfModel();

            // ID 1 つ 600 文字 × 64 曲 = 3.8 万文字
            string longId = new string('x', 600);
            var ids = new string[PlaylistShelfModel.MaxSongs];
            for (int i = 0; i < ids.Length; i++) ids[i] = longId + i;

            shelf.Save("keep", Ids("a"), 1);
            int revision = shelf.Revision;

            Assert.AreEqual(PlaylistShelfModel.ResultTooLarge, shelf.Save("big", ids, ids.Length));
            Assert.AreEqual(1, shelf.Count);
            Assert.AreEqual(revision, shelf.Revision);

            // 上書きで超えたら、元の中身に戻る
            Assert.AreEqual(PlaylistShelfModel.ResultTooLarge, shelf.Save("keep", ids, ids.Length));
            Assert.AreEqual(1, shelf.SongCountAt(0));
            Assert.AreEqual("a", shelf.IdsAt(0)[0]);
        }

        [Test]
        public void Save_ChangesRevision()
        {
            var shelf = new PlaylistShelfModel();
            int before = shelf.Revision;

            shelf.Save("A", Ids("a"), 1);

            Assert.Greater(shelf.Revision, before);
        }

        // ───────── 名前 ─────────

        [Test]
        public void SanitizeName_StripsControlCharactersAndTrims()
        {
            var shelf = new PlaylistShelfModel();

            Assert.AreEqual("ab c", shelf.SanitizeName(" a\tb c\n\r "));
            Assert.AreEqual("", shelf.SanitizeName("\n\t"));
            Assert.AreEqual("", shelf.SanitizeName(null));
        }

        [Test]
        public void SanitizeName_CapsLength()
        {
            var shelf = new PlaylistShelfModel();
            string longName = new string('あ', 40);

            Assert.AreEqual(PlaylistShelfModel.MaxNameLength, shelf.SanitizeName(longName).Length);
        }

        [Test]
        public void SanitizeName_DoesNotCutEmojiInHalf()
        {
            var shelf = new PlaylistShelfModel();

            // 23 文字 + 絵文字(2 つで 1 文字)+ 続き。24 文字目で切ると絵文字の上半分だけが残る。
            string name = new string('a', 23) + "\U0001F3B5" + "zzz";
            string cut = shelf.SanitizeName(name);

            Assert.AreEqual(23, cut.Length);
            Assert.AreEqual(new string('a', 23), cut);
        }

        [Test]
        public void TabAndNewlineInName_CannotBreakTheFormat()
        {
            var shelf = new PlaylistShelfModel();
            shelf.Save("a\tb\nc", Ids("x\ty", "z"), 2);

            var copy = new PlaylistShelfModel();
            Assert.IsTrue(copy.Decode(shelf.Encode()));

            Assert.AreEqual(1, copy.Count);
            Assert.AreEqual("abc", copy.NameAt(0));
            Assert.AreEqual(2, copy.SongCountAt(0));
            Assert.AreEqual("xy", copy.IdsAt(0)[0]);
        }

        [Test]
        public void SaveTargetName_PrefersTyped_ThenRemembered()
        {
            var shelf = WithOne("夜", "a");

            Assert.AreEqual("朝", shelf.SaveTargetName(" 朝 ", "夜"));
            Assert.AreEqual("夜", shelf.SaveTargetName("", "夜"));
            Assert.AreEqual("夜", shelf.SaveTargetName(null, " 夜 "));

            // 覚えていた名前が消されていたら、新しく作る
            Assert.AreEqual("", shelf.SaveTargetName("", "消えた"));
            Assert.AreEqual("", shelf.SaveTargetName("", ""));
            Assert.AreEqual("", shelf.SaveTargetName(null, null));
        }

        [Test]
        public void SaveLabel_TellsWhetherItOverwrites()
        {
            var shelf = WithOne("夜", "a");

            Assert.AreEqual("「夜」に上書き保存", shelf.SaveLabel("夜"));
            Assert.AreEqual("「朝」として保存", shelf.SaveLabel("朝"));
            Assert.AreEqual("新しいプレイリストとして保存", shelf.SaveLabel(""));
            Assert.AreEqual("新しいプレイリストとして保存", shelf.SaveLabel(null));
        }

        [Test]
        public void FindByName_UnknownOrEmpty_IsMinusOne()
        {
            var shelf = WithOne("A", "a");

            Assert.AreEqual(0, shelf.FindByName("A"));
            Assert.AreEqual(-1, shelf.FindByName("a"));
            Assert.AreEqual(-1, shelf.FindByName(""));
            Assert.AreEqual(-1, shelf.FindByName(null));
        }

        // ───────── 消す ─────────

        [Test]
        public void Delete_ShiftsTheRest()
        {
            var shelf = new PlaylistShelfModel();
            shelf.Save("A", Ids("1"), 1);
            shelf.Save("B", Ids("2", "3"), 2);
            shelf.Save("C", Ids("4"), 1);

            Assert.IsTrue(shelf.Delete(0));

            Assert.AreEqual(2, shelf.Count);
            Assert.AreEqual("B", shelf.NameAt(0));
            Assert.AreEqual(2, shelf.SongCountAt(0));
            Assert.AreEqual("C", shelf.NameAt(1));
            Assert.AreEqual("", shelf.NameAt(2));
        }

        [Test]
        public void Delete_OutOfRange_IsRefused()
        {
            var shelf = WithOne("A", "1");

            Assert.IsFalse(shelf.Delete(-1));
            Assert.IsFalse(shelf.Delete(1));
            Assert.AreEqual(1, shelf.Count);
        }

        [Test]
        public void OutOfRangeReads_AreSafe()
        {
            var shelf = new PlaylistShelfModel();

            Assert.AreEqual("", shelf.NameAt(0));
            Assert.AreEqual(0, shelf.SongCountAt(-1));
            Assert.AreEqual(0, shelf.IdsAt(5).Length);
        }

        // ───────── 保存する文字 ─────────

        [Test]
        public void EncodeDecode_RoundTrip()
        {
            var shelf = new PlaylistShelfModel();
            shelf.Save("作業用 🎵", Ids("yt:abc", "yt:def"), 2);
            shelf.Save("夜", Ids("id with space"), 1);
            shelf.Save("", Ids("z"), 1);

            var copy = new PlaylistShelfModel();
            Assert.IsTrue(copy.Decode(shelf.Encode()));

            Assert.AreEqual(3, copy.Count);
            Assert.AreEqual(0, copy.SkippedOnLoad);

            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(shelf.NameAt(i), copy.NameAt(i));
                Assert.AreEqual(shelf.SongCountAt(i), copy.SongCountAt(i));

                string[] a = shelf.IdsAt(i);
                string[] b = copy.IdsAt(i);
                Assert.AreEqual(a.Length, b.Length);
                for (int k = 0; k < a.Length; k++) Assert.AreEqual(a[k], b[k]);
            }

            // もう一度書き出しても同じ文字になる
            Assert.AreEqual(shelf.Encode(), copy.Encode());
        }

        [Test]
        public void Encode_EmptyShelf_IsJustTheHeader()
        {
            var shelf = new PlaylistShelfModel();

            Assert.AreEqual(PlaylistShelfModel.Header, shelf.Encode());

            var copy = WithOne("A", "a");
            Assert.IsTrue(copy.Decode(shelf.Encode()));
            Assert.AreEqual(0, copy.Count);
        }

        [Test]
        public void Decode_NullOrEmpty_IsEmptyShelf()
        {
            var shelf = WithOne("A", "a");

            Assert.IsTrue(shelf.Decode(null));
            Assert.AreEqual(0, shelf.Count);

            shelf = WithOne("A", "a");
            Assert.IsTrue(shelf.Decode(""));
            Assert.AreEqual(0, shelf.Count);
        }

        [Test]
        public void Decode_UnknownHeader_IsRefused()
        {
            var shelf = WithOne("A", "a");

            Assert.IsFalse(shelf.Decode("SMPPL9\nA\ta"));
            Assert.AreEqual(0, shelf.Count);

            Assert.IsFalse(shelf.Decode("garbage"));
            Assert.AreEqual(0, shelf.Count);
        }

        [Test]
        public void Decode_BrokenLinesAreSkipped_TheRestSurvive()
        {
            string data = "SMPPL1\n"
                          + "Good\ta\tb\n"
                          + "\n"                     // 空行
                          + "NoSongs\n"              // 曲が無い
                          + "\tlonely\n"             // 名前が無い
                          + "Good\tc\n"              // 同じ名前が先にある
                          + "Also\t\t\td\t\n";       // 空の欄はとばす

            var shelf = new PlaylistShelfModel();
            Assert.IsTrue(shelf.Decode(data));

            Assert.AreEqual(2, shelf.Count);
            Assert.AreEqual("Good", shelf.NameAt(0));
            Assert.AreEqual(2, shelf.SongCountAt(0));
            Assert.AreEqual("Also", shelf.NameAt(1));
            Assert.AreEqual(1, shelf.SongCountAt(1));
            Assert.AreEqual("d", shelf.IdsAt(1)[0]);
            Assert.AreEqual(3, shelf.SkippedOnLoad);
        }

        [Test]
        public void Decode_WindowsLineEndings_AreRead()
        {
            var shelf = new PlaylistShelfModel();

            Assert.IsTrue(shelf.Decode("SMPPL1\r\nA\ta\r\nB\tb"));

            Assert.AreEqual(2, shelf.Count);
            Assert.AreEqual("A", shelf.NameAt(0));
            Assert.AreEqual("a", shelf.IdsAt(0)[0]);
            Assert.AreEqual("B", shelf.NameAt(1));
        }

        [Test]
        public void Decode_TooManyPlaylists_KeepsTheFirstOnes()
        {
            string data = PlaylistShelfModel.Header;
            for (int i = 0; i < PlaylistShelfModel.MaxPlaylists + 5; i++) data += "\nP" + i + "\tx";

            var shelf = new PlaylistShelfModel();
            Assert.IsTrue(shelf.Decode(data));

            Assert.AreEqual(PlaylistShelfModel.MaxPlaylists, shelf.Count);
            Assert.AreEqual("P0", shelf.NameAt(0));
            Assert.AreEqual(5, shelf.SkippedOnLoad);
        }

        [Test]
        public void Decode_TooManySongs_AndDuplicates_AreCapped()
        {
            string data = PlaylistShelfModel.Header + "\nA";
            for (int i = 0; i < PlaylistShelfModel.MaxSongs + 5; i++) data += "\tid" + i + "\tid" + i;

            var shelf = new PlaylistShelfModel();
            Assert.IsTrue(shelf.Decode(data));

            Assert.AreEqual(PlaylistShelfModel.MaxSongs, shelf.SongCountAt(0));
            Assert.AreEqual("id63", shelf.IdsAt(0)[PlaylistShelfModel.MaxSongs - 1]);
        }

        [Test]
        public void Decode_ReplacesPreviousContents()
        {
            var shelf = new PlaylistShelfModel();
            shelf.Save("Old", Ids("a"), 1);

            Assert.IsTrue(shelf.Decode("SMPPL1\nNew\tb"));

            Assert.AreEqual(1, shelf.Count);
            Assert.AreEqual("New", shelf.NameAt(0));
            Assert.AreEqual(-1, shelf.FindByName("Old"));
        }

        [Test]
        public void FullShelf_OfLongestNames_StaysUnderTheLimit()
        {
            // ふつうの ID(YouTube の 11 文字 + 前置き)で棚を満杯にしても、保存できる量に収まる。
            var shelf = new PlaylistShelfModel();
            var ids = new string[PlaylistShelfModel.MaxSongs];
            for (int i = 0; i < ids.Length; i++) ids[i] = "youtube:ABCDEFGHI" + (i < 10 ? "0" : "") + i;

            for (int p = 0; p < PlaylistShelfModel.MaxPlaylists; p++)
            {
                string name = new string('あ', PlaylistShelfModel.MaxNameLength - 2) + (p < 10 ? "0" : "") + p;
                Assert.AreEqual(p, shelf.Save(name, ids, ids.Length));
            }

            Assert.LessOrEqual(shelf.Encode().Length, PlaylistShelfModel.MaxEncodedLength);
        }
    }
}
