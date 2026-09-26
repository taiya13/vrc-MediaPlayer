using NUnit.Framework;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Queue;

namespace SmartMediaPlatform.AutoPlay.Tests
{
    /// <summary>
    /// Phase3-4: Error Recovery の記憶(<see cref="PlaybackFailureTracker"/>)の検証。
    ///
    /// 確かめたいのは 4 点です。
    /// <list type="number">
    /// <item>失敗した動画をしばらく積み直さない(<c>CooldownSeconds</c>)</item>
    /// <item>失敗を重ねるほど待ち時間が伸びる(<c>BackoffMultiplier</c>)</item>
    /// <item>再生できたら記憶を消す(一時的な不調から自力で戻る)</item>
    /// <item>長時間動かしても記憶が増え続けない(<c>MaxTracked</c>)</item>
    /// </list>
    ///
    /// 時計は <see cref="PlaybackFailureTracker.Tick"/> で進めるので、
    /// 実時間を待たずに決定的に検証できます。
    /// </summary>
    public sealed class PlaybackFailureTrackerTests
    {
        private PlaybackFailureTracker _tracker;

        [SetUp]
        public void SetUp()
        {
            _tracker = new PlaybackFailureTracker
            {
                CooldownSeconds = 10f,
                BackoffMultiplier = 3f,
                MaxCooldownSeconds = 1000f,
                MaxFailuresBeforePermanentBlock = 5,
                MaxTracked = 256,
            };
        }

        private static MediaItem Item(string id, MediaType type = MediaType.Video)
        {
            return new MediaItem(id, id, "artist", type, "genre", new[] { "tag" },
                "https://example.com/" + id, 100);
        }

        // ───────── 契約 ─────────

        [Test]
        public void Tracker_IsAPlaybackFilter()
        {
            Assert.IsInstanceOf<IPlaybackFilter>(_tracker);
        }

        [Test]
        public void NothingIsBlockedAtTheStart()
        {
            Assert.IsFalse(_tracker.IsBlocked("video-001"));
            Assert.IsTrue(_tracker.CanPlay(Item("video-001")));
            Assert.AreEqual(0, _tracker.TrackedCount);
            Assert.AreEqual(0, _tracker.BlockedCount);
        }

        [Test]
        public void CanPlay_RejectsNull()
        {
            Assert.IsFalse(_tracker.CanPlay(null));
        }

        [Test]
        public void MarkFailed_IgnoresEmptyIds()
        {
            _tracker.MarkFailed(null);
            _tracker.MarkFailed("");
            _tracker.MarkFailed("   ");

            Assert.AreEqual(0, _tracker.TrackedCount);
            Assert.AreEqual(0, _tracker.TotalFailures);
        }

        // ───────── クールダウン ─────────

        [Test]
        public void MarkFailed_BlocksTheVideo()
        {
            _tracker.MarkFailed("video-001", "URL が不正です");

            Assert.IsTrue(_tracker.IsBlocked("video-001"));
            Assert.IsFalse(_tracker.CanPlay(Item("video-001")), "ふるいとしても弾く");
            Assert.AreEqual(1, _tracker.TrackedCount);
            Assert.AreEqual(1, _tracker.BlockedCount);
            Assert.AreEqual("video-001", _tracker.LastFailedId);
            Assert.AreEqual("URL が不正です", _tracker.LastReason);
        }

        [Test]
        public void MarkFailed_LeavesOtherVideosAlone()
        {
            _tracker.MarkFailed("video-001");

            Assert.IsFalse(_tracker.IsBlocked("video-002"));
            Assert.IsTrue(_tracker.CanPlay(Item("video-002")));
        }

        [Test]
        public void BlockExpiresAfterTheCooldown()
        {
            _tracker.MarkFailed("video-001");

            _tracker.Tick(9f);
            Assert.IsTrue(_tracker.IsBlocked("video-001"), "まだ待ち時間の中");

            _tracker.Tick(2f);
            Assert.IsFalse(_tracker.IsBlocked("video-001"), "10 秒過ぎたら再挑戦できる");
        }

        [Test]
        public void Tick_IgnoresNegativeTime()
        {
            _tracker.MarkFailed("video-001");
            _tracker.Tick(-100f);

            Assert.IsTrue(_tracker.IsBlocked("video-001"), "時間は巻き戻らない");
        }

        [Test]
        public void RepeatedFailures_LengthenTheWait()
        {
            _tracker.MarkFailed("video-001");
            _tracker.Tick(11f);
            Assert.IsFalse(_tracker.IsBlocked("video-001"));

            // 2 回目 → 10 × 3 = 30 秒
            _tracker.MarkFailed("video-001");
            _tracker.Tick(11f);
            Assert.IsTrue(_tracker.IsBlocked("video-001"), "2 回目は待ち時間が伸びる");

            _tracker.Tick(20f);
            Assert.IsFalse(_tracker.IsBlocked("video-001"));
        }

        [Test]
        public void Backoff_CanBeTurnedOff()
        {
            _tracker.BackoffMultiplier = 1f;

            _tracker.MarkFailed("video-001");
            _tracker.Tick(11f);
            _tracker.MarkFailed("video-001");
            _tracker.Tick(11f);

            Assert.IsFalse(_tracker.IsBlocked("video-001"), "倍率 1 なら待ち時間は伸びない");
        }

        [Test]
        public void Backoff_StopsAtTheCeiling()
        {
            _tracker.MaxCooldownSeconds = 50f;

            // 5 回失敗。倍率のままなら 10 × 3^4 = 810 秒だが、上限が効いて 50 秒になる。
            for (int i = 0; i < 5; i++) _tracker.MarkFailed("video-001");

            Assert.IsTrue(_tracker.IsBlocked("video-001"));

            _tracker.Tick(51f);

            Assert.IsFalse(_tracker.IsBlocked("video-001"), "待ち時間は上限を超えない");
        }

        // ───────── 恒久ブロック ─────────

        [Test]
        public void TooManyFailures_BlockTheVideoForGood()
        {
            for (int i = 0; i < 6; i++)
            {
                _tracker.MarkFailed("video-001");
                _tracker.Tick(100000f);
            }

            Assert.IsTrue(_tracker.IsBlocked("video-001"),
                "何度も失敗する動画は待っても戻さない");
            Assert.AreEqual(6, _tracker.FailureCountOf("video-001"));
        }

        [Test]
        public void PermanentBlock_CanBeTurnedOff()
        {
            _tracker.MaxFailuresBeforePermanentBlock = 0;

            for (int i = 0; i < 10; i++)
            {
                _tracker.MarkFailed("video-001");
                _tracker.Tick(100000f);
            }

            Assert.IsFalse(_tracker.IsBlocked("video-001"));
        }

        [Test]
        public void Clear_LetsEverythingBackIn()
        {
            for (int i = 0; i < 6; i++) _tracker.MarkFailed("video-001");
            _tracker.MarkFailed("video-002");

            _tracker.Clear();

            Assert.AreEqual(0, _tracker.TrackedCount);
            Assert.IsFalse(_tracker.IsBlocked("video-001"));
            Assert.IsFalse(_tracker.IsBlocked("video-002"));
        }

        [Test]
        public void Forget_ClearsOneVideoOnly()
        {
            _tracker.MarkFailed("video-001");
            _tracker.MarkFailed("video-002");

            Assert.IsTrue(_tracker.Forget("video-001"));
            Assert.IsFalse(_tracker.Forget("video-001"), "2 度目は何もしない");

            Assert.IsFalse(_tracker.IsBlocked("video-001"));
            Assert.IsTrue(_tracker.IsBlocked("video-002"));
        }

        // ───────── 成功で忘れる ─────────

        [Test]
        public void MarkSucceeded_ForgetsThePastFailures()
        {
            _tracker.MarkFailed("video-001");
            _tracker.MarkFailed("video-001");

            _tracker.MarkSucceeded("video-001");

            Assert.IsFalse(_tracker.IsBlocked("video-001"));
            Assert.AreEqual(0, _tracker.FailureCountOf("video-001"),
                "1 本でも再生できれば、次からは普通の候補に戻る");
        }

        [Test]
        public void MarkSucceeded_IsSafeForUnknownIds()
        {
            Assert.DoesNotThrow(() => _tracker.MarkSucceeded("never-seen"));
            Assert.DoesNotThrow(() => _tracker.MarkSucceeded(null));
        }

        // ───────── 大文字小文字 ─────────

        [Test]
        public void IdsAreComparedCaseInsensitively()
        {
            _tracker.MarkFailed("Video-001");

            Assert.IsTrue(_tracker.IsBlocked("video-001"));
            Assert.IsFalse(_tracker.CanPlay(Item("VIDEO-001")));
        }

        // ───────── 記憶の上限 ─────────

        [Test]
        public void Memory_StaysBoundedOverALongRun()
        {
            _tracker.MaxTracked = 16;

            for (int i = 0; i < 500; i++)
            {
                _tracker.MarkFailed($"video-{i:000}");
                _tracker.Tick(0.1f);
            }

            Assert.LessOrEqual(_tracker.TrackedCount, 16, "記憶が増え続けない");
            Assert.AreEqual(500, _tracker.TotalFailures, "累計はちゃんと数えている");
        }

        [Test]
        public void Memory_DropsTheOldestFailureFirst()
        {
            _tracker.MaxTracked = 2;

            _tracker.MarkFailed("video-001");
            _tracker.Tick(1f);
            _tracker.MarkFailed("video-002");
            _tracker.Tick(1f);
            _tracker.MarkFailed("video-003");

            Assert.IsFalse(_tracker.IsBlocked("video-001"), "いちばん古い失敗から捨てる");
            Assert.IsTrue(_tracker.IsBlocked("video-002"));
            Assert.IsTrue(_tracker.IsBlocked("video-003"));
        }

        // ───────── 診断 ─────────

        [Test]
        public void GetBlockedIds_ListsWhatIsCurrentlyBlocked()
        {
            _tracker.MarkFailed("video-001");
            _tracker.MarkFailed("video-002");
            _tracker.Tick(11f);
            _tracker.MarkFailed("video-003");

            var blocked = _tracker.GetBlockedIds();

            CollectionAssert.Contains(blocked, "video-003");
            CollectionAssert.DoesNotContain(blocked, "video-001", "待ち時間が過ぎたものは外れる");
        }

        [Test]
        public void ToString_ReportsTheCounts()
        {
            _tracker.MarkFailed("video-001");

            StringAssert.Contains("PlaybackFailureTracker", _tracker.ToString());
        }
    }
}
