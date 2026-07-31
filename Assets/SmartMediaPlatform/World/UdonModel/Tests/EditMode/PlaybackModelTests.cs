using NUnit.Framework;

namespace SmartMediaPlatform.World.UdonModel.Tests
{
    /// <summary>
    /// Phase5-2: Udon へ写す前のモデルを検証する。
    ///
    /// <b>ここが通っていれば、実機の <c>UdonPlayerSession</c> も同じように動きます</b>
    /// (UdonSharp はこの環境でコンパイルできないため、
    ///  Phase1-2 / Phase1-3 と同じく「純粋 C# 側を検証して 1 対 1 で写す」方式です)。
    ///
    /// 検証したいのは Phase3〜4 で決めた約束が Udon 形でも守られていること:
    /// <list type="bullet">
    /// <item>Queue の先頭 = いま鳴っているもの</item>
    /// <item>一覧から選んだものへ「割り込んで」切り替わる</item>
    /// <item>先頭は動かせない・消せない</item>
    /// <item>終わったら / 失敗したら次へ送る</item>
    /// </list>
    /// </summary>
    public sealed class PlaybackModelTests
    {
        private const int CatalogCount = 10;

        private static PlaybackModel New()
        {
            return new PlaybackModel(CatalogCount);
        }

        private static int[] Candidates(params int[] values)
        {
            return values;
        }

        // ───────── Queue の不変条件 ─────────

        [Test]
        public void TheHeadOfTheQueueIsWhatIsPlaying()
        {
            var model = New();
            model.Enqueue(3);
            model.Enqueue(7);

            model.Play();

            Assert.AreEqual(3, model.CurrentIndex);
            Assert.AreEqual(3, model.GetQueueAt(0), "先頭 = 再生中");
            Assert.AreEqual(3, model.RequestedIndex, "先頭を読み込ませている");
            Assert.IsTrue(model.IsPlaying);
        }

        [Test]
        public void NextDropsTheHeadAndLoadsTheNewHead()
        {
            var model = New();
            model.Enqueue(3);
            model.Enqueue(7);
            model.Play();

            Assert.IsTrue(model.Next(null));

            Assert.AreEqual(7, model.CurrentIndex);
            Assert.AreEqual(7, model.RequestedIndex);
            Assert.AreEqual(1, model.QueueCount, "流し終えたものは Queue から消える");
        }

        [Test]
        public void NextFailsWhenThereIsNothingAfterTheHead()
        {
            var model = New();
            model.Enqueue(3);
            model.Play();

            Assert.IsFalse(model.Next(null));
            Assert.IsTrue(model.IsExhausted);
            Assert.AreEqual(3, model.CurrentIndex, "鳴っているものは残る");
        }

        [Test]
        public void PlayWithAnEmptyQueueDoesNothing()
        {
            var model = New();

            Assert.IsFalse(model.Play());
            Assert.AreEqual(-1, model.CurrentIndex);
            Assert.AreEqual(0, model.LoadCount, "読み込みを頼んでいない");
        }

        [Test]
        public void TheSameThingIsNotLoadedTwice()
        {
            var model = New();
            model.Enqueue(3);

            model.Play();
            model.Stop();
            model.Play();

            Assert.AreEqual(1, model.LoadCount, "止めて再開しても読み込み直さない");
        }

        // ───────── 一覧から再生 ─────────

        [Test]
        public void PlayAtSwitchesToTheChosenOneWhileSomethingIsPlaying()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);
            model.Play();

            Assert.IsTrue(model.PlayAt(8, null));

            Assert.AreEqual(8, model.CurrentIndex, "選んだものへ切り替わる");
            Assert.AreEqual(8, model.RequestedIndex);
            Assert.IsTrue(model.IsPlaying);
        }

        [Test]
        public void PlayAtKeepsTheRestOfTheQueue()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);
            model.Play();

            model.PlayAt(8, null);

            Assert.AreEqual(8, model.GetQueueAt(0));
            Assert.AreEqual(2, model.GetQueueAt(1), "後ろに積んであったものは残る");
        }

        [Test]
        public void PlayAtOnAnEmptyQueueJustStarts()
        {
            var model = New();

            Assert.IsTrue(model.PlayAt(5, null));

            Assert.AreEqual(5, model.CurrentIndex);
            Assert.AreEqual(1, model.QueueCount);
            Assert.IsTrue(model.IsPlaying);
        }

        [Test]
        public void PlayAtOnWhatIsAlreadyPlayingDoesNotReload()
        {
            var model = New();
            model.Enqueue(4);
            model.Play();
            int before = model.LoadCount;

            Assert.IsTrue(model.PlayAt(4, null));

            Assert.AreEqual(before, model.LoadCount, "同じものを頼み直さない");
            Assert.IsTrue(model.IsPlaying);
        }

        [Test]
        public void PlayAtResumesWhenTheSameOneIsPaused()
        {
            var model = New();
            model.Enqueue(4);
            model.Play();
            model.TogglePlayPause();

            Assert.IsTrue(model.PlayAt(4, null));
            Assert.IsTrue(model.IsPlaying, "止まっていたら鳴らし直す");
        }

        [Test]
        public void PlayAtRefusesSomethingOutsideTheCatalog()
        {
            var model = New();

            Assert.IsFalse(model.PlayAt(-1, null));
            Assert.IsFalse(model.PlayAt(CatalogCount, null));
            Assert.AreEqual(0, model.QueueCount);
        }

        [Test]
        public void PlayAtDoesNotDuplicateSomethingAlreadyQueued()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);
            model.Enqueue(3);
            model.Play();

            model.PlayAt(3, null);

            Assert.AreEqual(3, model.CurrentIndex);
            Assert.AreEqual(-1, IndexOfDuplicate(model, 3), "同じものが二度入っていない");
        }

        private static int IndexOfDuplicate(PlaybackModel model, int value)
        {
            bool seen = false;
            for (int i = 0; i < model.QueueCount; i++)
            {
                if (model.GetQueueAt(i) != value) continue;
                if (seen) return i;
                seen = true;
            }
            return -1;
        }

        // ───────── 次に再生 / 追加 ─────────

        [Test]
        public void PlayNextPutsItRightAfterWhatIsPlaying()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);
            model.Enqueue(3);
            model.Play();

            Assert.IsTrue(model.PlayNext(3));

            Assert.AreEqual(1, model.GetQueueAt(0), "鳴っているものは動かない");
            Assert.AreEqual(3, model.GetQueueAt(1));
            Assert.AreEqual(1, model.LoadCount, "割り込ませただけでは切り替わらない");
        }

        [Test]
        public void EnqueueAppendsToTheEnd()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);

            Assert.AreEqual(2, model.QueueCount);
            Assert.AreEqual(2, model.GetQueueAt(1));
        }

        [Test]
        public void EnqueueRefusesADuplicate()
        {
            var model = New();
            model.Enqueue(1);

            Assert.IsFalse(model.Enqueue(1));
            Assert.AreEqual(1, model.QueueCount);
        }

        // ───────── Queue の操作 ─────────

        [Test]
        public void JumpToMovesToThatEntry()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);
            model.Enqueue(3);
            model.Play();

            Assert.IsTrue(model.JumpTo(2, null));

            Assert.AreEqual(3, model.CurrentIndex);
            Assert.AreEqual(2, model.GetQueueAt(1), "飛び越したものは後ろに残る");
        }

        [Test]
        public void TheHeadCannotBeJumpedToOrRemoved()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);
            model.Play();

            Assert.IsFalse(model.JumpTo(0, null), "鳴っているものへは飛べない");
            Assert.IsFalse(model.RemoveFromQueue(0), "鳴っているものは消せない");
            Assert.AreEqual(1, model.CurrentIndex);
        }

        [Test]
        public void RemoveFromQueueTakesOutTheChosenEntry()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);
            model.Enqueue(3);
            model.Play();

            Assert.IsTrue(model.RemoveFromQueue(1));

            Assert.AreEqual(2, model.QueueCount);
            Assert.AreEqual(3, model.GetQueueAt(1));
        }

        [Test]
        public void ClearUpcomingKeepsOnlyWhatIsPlaying()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);
            model.Enqueue(3);
            model.Play();

            Assert.AreEqual(2, model.ClearUpcoming());

            Assert.AreEqual(1, model.QueueCount);
            Assert.AreEqual(1, model.CurrentIndex);
            Assert.IsTrue(model.IsPlaying, "鳴っているものは止まらない");
        }

        // ───────── 補充 ─────────

        [Test]
        public void TheQueueIsRefilledWhenItRunsLow()
        {
            var model = New();
            model.Enqueue(1);

            int added = model.EnsureQueueFilled(Candidates(4, 5, 6, 7, 8, 9));

            Assert.AreEqual(4, added, "目標 5 まで積む");
            Assert.AreEqual(5, model.QueueCount);
        }

        [Test]
        public void TheQueueIsNotRefilledWhenItIsLongEnough()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);

            Assert.AreEqual(0, model.EnsureQueueFilled(Candidates(4, 5, 6)),
                            "下限を割っていなければ足さない");
        }

        [Test]
        public void RefillingSkipsWhatIsAlreadyQueued()
        {
            var model = New();
            model.Enqueue(1);

            model.EnsureQueueFilled(Candidates(1, 1, 4));

            Assert.AreEqual(2, model.QueueCount);
            Assert.AreEqual(4, model.GetQueueAt(1));
        }

        [Test]
        public void NextRefillsSoPlaybackKeepsGoing()
        {
            var model = New();
            model.Enqueue(1);
            model.Play();

            Assert.IsTrue(model.Next(Candidates(6, 7, 8)),
                          "曲が尽きていても、おすすめがあれば進める");
            Assert.AreEqual(6, model.CurrentIndex);
            Assert.IsFalse(model.IsExhausted);
        }

        // ───────── 動画プレイヤーからの知らせ ─────────

        [Test]
        public void EndedAdvancesToTheNextOne()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);
            model.Play();

            Assert.IsTrue(model.OnEnded(null));
            Assert.AreEqual(2, model.CurrentIndex);
        }

        [Test]
        public void EndedWithNothingLeftStopsInsteadOfLooping()
        {
            var model = New();
            model.Enqueue(1);
            model.Play();

            Assert.IsFalse(model.OnEnded(null));
            Assert.IsFalse(model.IsPlaying);
            Assert.IsTrue(model.IsExhausted);
        }

        [Test]
        public void AFailureSkipsTheBrokenOne()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);
            model.Play();

            Assert.IsTrue(model.OnError(null));

            Assert.AreEqual(2, model.CurrentIndex, "壊れているものを飛ばして次へ");
            Assert.AreEqual(-1, model.IndexInQueue(1), "壊れているものは Queue に残さない");
        }

        [Test]
        public void RepeatedFailuresDoNotWedgeThePlayer()
        {
            var model = New();
            for (int i = 0; i < 5; i++) model.Enqueue(i);
            model.Play();

            for (int i = 0; i < 10; i++) model.OnError(null);

            Assert.AreEqual(1, model.QueueCount, "最後の 1 つで止まる");
            Assert.IsFalse(model.IsPlaying);
        }

        // ───────── 履歴 ─────────

        [Test]
        public void PreviousGoesBackToWhatPlayedBefore()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);
            model.Play();
            model.Next(null);

            Assert.IsTrue(model.Previous());

            Assert.AreEqual(1, model.CurrentIndex);
            Assert.AreEqual(2, model.GetQueueAt(1), "戻る前のものは次に残る");
        }

        [Test]
        public void PreviousFailsWithNoHistory()
        {
            var model = New();
            model.Enqueue(1);
            model.Play();

            Assert.IsFalse(model.Previous());
            Assert.AreEqual(1, model.CurrentIndex);
        }

        [Test]
        public void HistoryDoesNotGrowWithoutLimit()
        {
            var model = New();
            model.MaxHistory = 3;
            for (int i = 0; i < CatalogCount; i++) model.Enqueue(i);
            model.Play();

            for (int i = 0; i < 6; i++) model.Next(null);

            Assert.AreEqual(3, model.HistoryCount);
            Assert.AreEqual(5, model.GetHistoryAt(2), "新しいものが末尾");
        }

        // ───────── 選択 ─────────

        [Test]
        public void SelectRemembersTheChoiceWithoutPlaying()
        {
            var model = New();

            Assert.IsTrue(model.Select(6));

            Assert.AreEqual(6, model.SelectedIndex);
            Assert.AreEqual(0, model.LoadCount, "選んだだけでは再生しない");
        }

        [Test]
        public void SelectRefusesSomethingOutsideTheCatalog()
        {
            var model = New();

            Assert.IsFalse(model.Select(CatalogCount));
            Assert.AreEqual(-1, model.SelectedIndex);
        }

        // ───────── 止める / 一時停止 ─────────

        [Test]
        public void StopKeepsWhatIsLoaded()
        {
            var model = New();
            model.Enqueue(1);
            model.Play();

            Assert.IsTrue(model.Stop());

            Assert.IsFalse(model.IsPlaying);
            Assert.AreEqual(1, model.CurrentIndex, "読み込んだものは残る");
        }

        [Test]
        public void TogglePlayPauseGoesBothWays()
        {
            var model = New();
            model.Enqueue(1);
            model.Play();

            model.TogglePlayPause();
            Assert.IsFalse(model.IsPlaying);

            model.TogglePlayPause();
            Assert.IsTrue(model.IsPlaying);
        }

        [Test]
        public void NothingWorksOnAnEmptyQueue()
        {
            var model = New();

            Assert.IsFalse(model.Stop());
            Assert.IsFalse(model.TogglePlayPause());
            Assert.IsFalse(model.Next(null));
            Assert.IsFalse(model.Previous());
        }

        // ───────── 上限 ─────────

        [Test]
        public void TheQueueStopsGrowingAtItsCapacity()
        {
            var model = new PlaybackModel(PlaybackModel.QueueCapacity + 10);

            int added = 0;
            for (int i = 0; i < PlaybackModel.QueueCapacity + 10; i++)
            {
                if (model.Enqueue(i)) added++;
            }

            Assert.AreEqual(PlaybackModel.QueueCapacity, added, "配列からあふれない");
            Assert.AreEqual(PlaybackModel.QueueCapacity, model.QueueCount);
        }

        // ───────── Udon で書ける形か ─────────

        [Test]
        public void TheModelUsesOnlyThingsUdonCanCompile()
        {
            var type = typeof(PlaybackModel);

            foreach (var method in type.GetMethods())
            {
                if (method.DeclaringType != type) continue;

                Assert.IsFalse(method.IsGenericMethod,
                               $"{method.Name}: Udon はジェネリックを扱えない");

                foreach (var parameter in method.GetParameters())
                {
                    AssertUdonFriendly(parameter.ParameterType, $"{method.Name} の引数");
                }
                AssertUdonFriendly(method.ReturnType, $"{method.Name} の戻り値");
            }
        }


        // ───────── 同期(Phase5-4)─────────

        [Test]
        public void ApplyingASyncedStateReplacesTheWholeQueue()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);
            model.Play();

            model.ApplySyncedState(new[] { 7, 8, 9 }, true, 7);

            Assert.AreEqual(3, model.QueueCount);
            Assert.AreEqual(7, model.CurrentIndex, "先頭 = いま鳴っているもの、は同期でも同じ");
            Assert.AreEqual(8, model.GetQueueAt(1));
            Assert.AreEqual(9, model.GetQueueAt(2));
            Assert.IsTrue(model.IsPlaying);
        }

        [Test]
        public void ApplyingASyncedStateDoesNotAskTheBackendToLoadAnything()
        {
            var model = New();
            int before = model.LoadCount;

            model.ApplySyncedState(new[] { 4, 5 }, true, 4);

            Assert.AreEqual(before, model.LoadCount,
                            "受け取る側は映すだけ。動画を読ませるのは呼び出し側の仕事");
            Assert.AreEqual(4, model.RequestedIndex, "持ち主が読み込ませているものを覚える");
        }

        [Test]
        public void ApplyingASyncedStateDoesNotRefillTheQueue()
        {
            var model = New();

            // 補充が働けば 1 件では終わらないはず
            model.ApplySyncedState(new[] { 6 }, true, 6);

            Assert.AreEqual(1, model.QueueCount,
                            "受け取る側が勝手に補充すると、人によって Queue が変わってしまう");
        }

        [Test]
        public void AnEmptySyncedStateEmptiesTheQueue()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);

            model.ApplySyncedState(new int[0], false, -1);

            Assert.AreEqual(0, model.QueueCount);
            Assert.AreEqual(-1, model.CurrentIndex);
            Assert.IsFalse(model.IsPlaying);
        }

        [Test]
        public void ANullSyncedQueueIsTreatedAsEmpty()
        {
            var model = New();
            model.Enqueue(1);

            model.ApplySyncedState(null, false, -1);

            Assert.AreEqual(0, model.QueueCount);
        }

        [Test]
        public void ASyncedStateLongerThanTheQueueIsTruncated()
        {
            var model = new PlaybackModel(PlaybackModel.QueueCapacity + 10);

            var tooMany = new int[PlaybackModel.QueueCapacity + 5];
            for (int i = 0; i < tooMany.Length; i++) tooMany[i] = i;

            model.ApplySyncedState(tooMany, true, 0);

            Assert.AreEqual(PlaybackModel.QueueCapacity, model.QueueCount,
                            "固定長を超えるぶんは捨てる(落ちるより切り詰める)");
        }

        [Test]
        public void ApplyingAPausedStateStopsWithoutClearingTheQueue()
        {
            var model = New();

            model.ApplySyncedState(new[] { 2, 3 }, false, 2);

            Assert.IsFalse(model.IsPlaying);
            Assert.AreEqual(2, model.QueueCount, "止まっていても Queue は見える");
            Assert.AreEqual(2, model.CurrentIndex);
        }

        [Test]
        public void TheSnapshotIsExactlyWhatIsInTheQueue()
        {
            var model = New();
            model.Enqueue(5);
            model.Enqueue(1);
            model.Enqueue(9);

            var snapshot = model.SnapshotQueue();

            Assert.AreEqual(3, snapshot.Length, "使っていない後ろの余りは配らない");
            CollectionAssert.AreEqual(new[] { 5, 1, 9 }, snapshot);
        }

        [Test]
        public void ASnapshotSurvivesARoundTrip()
        {
            var owner = New();
            owner.Enqueue(2);
            owner.Enqueue(6);
            owner.Play();
            owner.Next(Candidates());

            var joiner = New();
            joiner.ApplySyncedState(owner.SnapshotQueue(), owner.IsPlaying, owner.RequestedIndex);

            Assert.AreEqual(owner.QueueCount, joiner.QueueCount);
            Assert.AreEqual(owner.CurrentIndex, joiner.CurrentIndex);
            Assert.AreEqual(owner.IsPlaying, joiner.IsPlaying);
        }


        // ───────── 失敗が続いても止まる(Phase5-5 の事故)─────────

        [Test]
        public void RepeatedFailuresEventuallyGiveUp()
        {
            // 実機で起きたのはこれ。失敗するたびに次を読み込み、その読み込みも失敗し …… と
            // 補充が効いているかぎり永遠に回って、クライアントごと固まった。
            var model = New();
            model.MaxConsecutiveErrors = 4;
            model.Enqueue(0);
            model.Play();

            int guard = 0;
            while (model.NotifyError(Candidates(1, 2, 3, 4, 5, 6, 7, 8, 9)))
            {
                guard++;
                Assert.Less(guard, 50, "止まらない = 実機が固まる");
            }

            Assert.AreEqual(5, model.ConsecutiveErrors, "上限 + 1 回目で諦める");
            Assert.IsFalse(model.IsPlaying);
            Assert.IsTrue(model.IsExhausted, "「次がありません」と出る状態になる");
        }

        [Test]
        public void GivingUpDoesNotLoadAnythingMore()
        {
            var model = New();
            model.MaxConsecutiveErrors = 2;
            model.Enqueue(0);
            model.Play();

            model.NotifyError(Candidates(1, 2, 3, 4, 5));
            model.NotifyError(Candidates(1, 2, 3, 4, 5));

            int loadsBefore = model.LoadCount;
            model.NotifyError(Candidates(1, 2, 3, 4, 5));

            Assert.AreEqual(loadsBefore, model.LoadCount,
                            "諦めたあとは読み込みを頼まない(ここが無限だった)");
        }

        [Test]
        public void PlayingSuccessfullyResetsTheFailureCount()
        {
            var model = New();
            model.MaxConsecutiveErrors = 4;
            model.Enqueue(0);
            model.Play();

            model.NotifyError(Candidates(1, 2, 3));
            model.NotifyError(Candidates(1, 2, 3));
            Assert.AreEqual(2, model.ConsecutiveErrors);

            model.NotifyStarted();

            Assert.AreEqual(0, model.ConsecutiveErrors, "1 本鳴れば数え直し");
        }

        [Test]
        public void PressingPlayGivesAnotherChanceAfterGivingUp()
        {
            var model = New();
            model.MaxConsecutiveErrors = 1;
            model.Enqueue(0);
            model.Play();

            model.NotifyError(Candidates(1, 2, 3));
            model.NotifyError(Candidates(1, 2, 3));
            Assert.IsFalse(model.IsPlaying, "いったん諦めている");

            Assert.IsTrue(model.Play(), "人が押したらもう一度試せる");
            Assert.AreEqual(0, model.ConsecutiveErrors);
        }

        [Test]
        public void TheCapCanBeTurnedOff()
        {
            var model = New();
            model.MaxConsecutiveErrors = 0;
            model.Enqueue(0);
            model.Play();

            for (int i = 0; i < 20; i++) model.NotifyError(Candidates(1, 2, 3, 4, 5));

            Assert.AreEqual(20, model.ConsecutiveErrors, "0 なら数えるだけで諦めない");
        }

        [Test]
        public void RunningOutOfMediaStopsPlaybackWithoutAnError()
        {
            var model = New();
            model.Enqueue(0);
            model.Play();

            // 候補が無ければ補充できないので、そこで終わる
            Assert.IsFalse(model.NotifyError(Candidates()));
            Assert.IsFalse(model.IsPlaying);
        }

        [Test]
        public void EndingNormallyMovesToTheNextOne()
        {
            var model = New();
            model.Enqueue(3);
            model.Enqueue(7);
            model.Play();

            model.NotifyEnded(Candidates());

            Assert.AreEqual(7, model.CurrentIndex);
            Assert.IsTrue(model.IsPlaying);
        }

        private static void AssertUdonFriendly(System.Type type, string what)
        {
            if (type == typeof(void)) return;

            var element = type.IsArray ? type.GetElementType() : type;

            Assert.IsTrue(element.IsPrimitive || element == typeof(string),
                          $"{what}: {element.Name} は Udon へ写せない"
                          + "(int / bool / float / string と、その配列だけにする)");
            Assert.IsFalse(type.IsGenericType, $"{what}: ジェネリックは Udon で使えない");
        }
    }
}
