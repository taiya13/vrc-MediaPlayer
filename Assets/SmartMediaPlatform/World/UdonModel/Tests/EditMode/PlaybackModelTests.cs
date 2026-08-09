using NUnit.Framework;

namespace SmartMediaPlatform.World.UdonModel.Tests
{
    /// <summary>
    /// Phase5-2: Udon へ写す前のモデルを検証する。Phase7-3 で全面的に書き直し。
    ///
    /// <b>ここが通っていれば、実機の <c>UdonPlayerSession</c> も同じように動きます</b>
    /// (UdonSharp はこの環境でコンパイルできないため、
    ///  Phase1-2 / Phase1-3 と同じく「純粋 C# 側を検証して 1 対 1 で写す」方式です)。
    ///
    /// <b>Phase7-3 で確かめたいこと</b> — 実機で出た 3 つの不具合の再発を止める:
    /// <list type="number">
    /// <item><b>選んだだけで再生予定が増えない</b>(「勝手に追加され続ける」)</item>
    /// <item><b>再生予定から消したものは戻ってこない</b>(「消しても消えない」)</item>
    /// <item><b>曲が終わったら再生予定の先頭へ進む</b>(「同じ動画がリピートし続ける」)</item>
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

        /// <summary>再生予定の中身を一気に見る(テストを読みやすくするため)。</summary>
        private static int[] Upcoming(PlaybackModel model)
        {
            return model.SnapshotQueue();
        }

        // ─────────────────────────────────────────────
        // 1. 再生中と再生予定が分かれていること(Phase7-3 の中心)
        // ─────────────────────────────────────────────

        [Test]
        public void WhatIsPlayingIsNotPartOfTheUpcomingList()
        {
            var model = New();
            model.Enqueue(3);
            model.Enqueue(7);

            model.Play();

            Assert.AreEqual(3, model.CurrentIndex, "先頭を取り出して鳴らす");
            Assert.AreEqual(3, model.RequestedIndex);
            Assert.IsTrue(model.IsPlaying);

            Assert.AreEqual(1, model.QueueCount, "鳴っているものは再生予定から外れる");
            Assert.AreEqual(7, model.GetQueueAt(0));
        }

        [Test]
        public void ChoosingSomethingFromTheLibraryDoesNotTouchTheUpcomingList()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);

            model.PlayAt(8, null);

            Assert.AreEqual(8, model.CurrentIndex);
            Assert.AreEqual(2, model.QueueCount, "一覧から選んでも再生予定は増えも減りもしない");
            Assert.AreEqual(1, model.GetQueueAt(0));
            Assert.AreEqual(2, model.GetQueueAt(1));
        }

        [Test]
        public void ChoosingTheSameThingOverAndOverNeverGrowsTheUpcomingList()
        {
            // 実機で出た「同じ動画ばかりが追加され続ける」の再現テスト。
            var model = New();

            for (int i = 0; i < 20; i++) model.PlayAt(4, Candidates(5, 6, 7));

            Assert.AreEqual(4, model.CurrentIndex);
            Assert.AreEqual(0, model.QueueCount, "何回選んでも再生予定は空のまま");
        }

        [Test]
        public void PlayingSomethingThatWasQueuedTakesItOutOfTheUpcomingList()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);
            model.Enqueue(3);

            model.PlayAt(2, null);

            Assert.AreEqual(2, model.CurrentIndex);
            CollectionAssert.AreEqual(new[] { 1, 3 }, Upcoming(model),
                                      "鳴らし始めたものは再生予定に残さない(二重に見えるため)");
        }

        // ─────────────────────────────────────────────
        // 2. 再生予定は消せる(「消しても消えない」の再発防止)
        // ─────────────────────────────────────────────

        [Test]
        public void TheFirstEntryOfTheUpcomingListCanBeRemoved()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);

            Assert.IsTrue(model.RemoveFromQueue(0), "Phase7-3 から先頭も外せる");
            CollectionAssert.AreEqual(new[] { 2 }, Upcoming(model));
        }

        [Test]
        public void ClearingTheUpcomingListLeavesTheMusicPlaying()
        {
            var model = New();
            model.PlayAt(5, null);
            model.Enqueue(1);
            model.Enqueue(2);
            model.Enqueue(3);

            Assert.AreEqual(3, model.ClearUpcoming());

            Assert.AreEqual(0, model.QueueCount, "再生予定は空になる");
            Assert.AreEqual(5, model.CurrentIndex, "鳴っているものは止まらない");
            Assert.IsTrue(model.IsPlaying);
        }

        [Test]
        public void AClearedUpcomingListStaysClearedWhileMusicKeepsPlaying()
        {
            // 「再生予定を消しても勝手に追加され続ける」の再現テスト。
            var model = New();
            model.PlayAt(5, Candidates(6, 7, 8));
            model.ClearUpcoming();

            // 選ぶ・鳴らす・止める・戻す —— どれをしても再生予定は空のまま。
            model.PlayAt(6, Candidates(7, 8, 9));
            model.Select(2);
            model.Stop();
            model.Play();

            Assert.AreEqual(0, model.QueueCount, "誰も入れていないものが並ぶことはない");
        }

        // ─────────────────────────────────────────────
        // 3. 曲が終わったら次へ進む(「リピートし続ける」の再発防止)
        // ─────────────────────────────────────────────

        [Test]
        public void EndingMovesToTheFirstEntryOfTheUpcomingListAndTakesItOut()
        {
            var model = New();
            model.PlayAt(5, null);
            model.Enqueue(1);
            model.Enqueue(2);

            Assert.IsTrue(model.NotifyEnded(null));

            Assert.AreEqual(1, model.CurrentIndex, "再生予定の先頭へ移る");
            Assert.AreEqual(1, model.RequestedIndex, "実際に読み込ませている");
            CollectionAssert.AreEqual(new[] { 2 }, Upcoming(model), "進んだぶんは再生予定から消える");
            Assert.IsTrue(model.IsPlaying);
        }

        [Test]
        public void EndingWalksThroughTheWholeUpcomingListInOrder()
        {
            var model = New();
            model.PlayAt(5, null);
            model.Enqueue(1);
            model.Enqueue(2);
            model.Enqueue(3);

            model.NotifyEnded(null);
            Assert.AreEqual(1, model.CurrentIndex);

            model.NotifyEnded(null);
            Assert.AreEqual(2, model.CurrentIndex);

            model.NotifyEnded(null);
            Assert.AreEqual(3, model.CurrentIndex);
            Assert.AreEqual(0, model.QueueCount);
        }

        [Test]
        public void EndingWithAnEmptyUpcomingListStopsInsteadOfRepeating()
        {
            var model = New();
            model.PlayAt(5, null);

            Assert.IsFalse(model.NotifyEnded(Candidates(6, 7)),
                           "既定は停止。おすすめで勝手に続けない");

            Assert.IsFalse(model.IsPlaying);
            Assert.IsTrue(model.IsExhausted);
            Assert.AreEqual(5, model.CurrentIndex, "何が鳴っていたかは残す(表示のため)");
        }

        [Test]
        public void EndingNeverReloadsTheSameThingByDefault()
        {
            // 「同じ動画がリピートし続ける」の再現テスト。
            var model = New();
            model.PlayAt(5, null);
            int loadsBefore = model.LoadCount;

            model.NotifyEnded(null);
            model.NotifyEnded(null);
            model.NotifyEnded(null);

            Assert.AreEqual(loadsBefore, model.LoadCount,
                            "止まったあとは読み込み直さない(= 繰り返さない)");
        }

        [Test]
        public void RepeatOneReloadsTheSameThingOnlyWhenItIsAskedFor()
        {
            var model = New();
            model.EndBehaviour = PlaybackModel.EndBehaviourRepeatOne;
            model.PlayAt(5, null);
            int loadsBefore = model.LoadCount;

            Assert.IsTrue(model.NotifyEnded(null));

            Assert.AreEqual(5, model.CurrentIndex);
            Assert.AreEqual(loadsBefore + 1, model.LoadCount, "もう一度読み込ませる");
            Assert.IsTrue(model.IsPlaying);
        }

        [Test]
        public void TheUpcomingListWinsOverRepeatOne()
        {
            var model = New();
            model.EndBehaviour = PlaybackModel.EndBehaviourRepeatOne;
            model.PlayAt(5, null);
            model.Enqueue(9);

            model.NotifyEnded(null);

            Assert.AreEqual(9, model.CurrentIndex,
                            "人が入れたものがあるなら、繰り返しより優先する");
        }

        [Test]
        public void RecommendKeepsGoingWithoutFillingTheUpcomingList()
        {
            var model = New();
            model.EndBehaviour = PlaybackModel.EndBehaviourRecommend;
            model.PlayAt(5, null);

            Assert.IsTrue(model.NotifyEnded(Candidates(6, 7, 8)));

            Assert.AreEqual(6, model.CurrentIndex, "おすすめの 1 件目へ進む");
            Assert.AreEqual(0, model.QueueCount, "おすすめは再生予定に積まない");
        }

        [Test]
        public void RecommendNeverPicksWhatIsAlreadyPlaying()
        {
            var model = New();
            model.EndBehaviour = PlaybackModel.EndBehaviourRecommend;
            model.PlayAt(5, null);

            Assert.IsTrue(model.NotifyEnded(Candidates(5, 5, 8)));

            Assert.AreEqual(8, model.CurrentIndex,
                            "鳴っていたものは選ばない(それでは繰り返しになる)");
        }

        [Test]
        public void TheUpcomingListWinsOverRecommendations()
        {
            var model = New();
            model.EndBehaviour = PlaybackModel.EndBehaviourRecommend;
            model.PlayAt(5, null);
            model.Enqueue(7);

            Assert.IsTrue(model.NotifyEnded(Candidates(8, 9)));

            Assert.AreEqual(7, model.CurrentIndex,
                            "人が入れたものが、おすすめより先");
        }

        // ─────────────────────────────────────────────
        // 4. 再生予定の動き 5 通り(空 / 1 曲 / 複数 / 途中削除 / 途中追加)
        // ─────────────────────────────────────────────

        [Test]
        public void AnEmptyUpcomingListDoesNothingSurprising()
        {
            var model = New();

            Assert.IsFalse(model.Play(), "何も入っていなければ鳴らせない");
            Assert.IsFalse(model.Next(null));
            Assert.IsFalse(model.RemoveFromQueue(0));
            Assert.IsFalse(model.JumpTo(0, null));
            Assert.AreEqual(0, model.ClearUpcoming());

            Assert.AreEqual(-1, model.CurrentIndex);
            Assert.IsFalse(model.IsPlaying);
        }

        [Test]
        public void AUpcomingListOfOnePlaysThenStops()
        {
            var model = New();
            model.Enqueue(4);

            Assert.IsTrue(model.Play());
            Assert.AreEqual(4, model.CurrentIndex);
            Assert.AreEqual(0, model.QueueCount, "取り出したので空になる");

            Assert.IsFalse(model.NotifyEnded(null), "次が無いので止まる");
            Assert.IsFalse(model.IsPlaying);
        }

        [Test]
        public void AUpcomingListOfManyIsPlayedFromTopToBottom()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);
            model.Enqueue(3);

            model.Play();
            Assert.AreEqual(1, model.CurrentIndex);

            model.NotifyEnded(null);
            Assert.AreEqual(2, model.CurrentIndex);

            model.NotifyEnded(null);
            Assert.AreEqual(3, model.CurrentIndex);

            Assert.IsFalse(model.NotifyEnded(null));
            Assert.AreEqual(0, model.QueueCount);
        }

        [Test]
        public void RemovingFromTheMiddleSkipsThatOneAndKeepsTheRest()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);
            model.Enqueue(3);
            model.Play();                       // 1 が鳴る。残りは [2, 3]

            Assert.IsTrue(model.RemoveFromQueue(0));   // 2 を消す
            CollectionAssert.AreEqual(new[] { 3 }, Upcoming(model));

            model.NotifyEnded(null);
            Assert.AreEqual(3, model.CurrentIndex, "消したものは飛ばされる");
        }

        [Test]
        public void AddingInTheMiddleIsPlayedInThatOrder()
        {
            var model = New();
            model.PlayAt(1, null);
            model.Enqueue(2);
            model.Enqueue(3);

            model.PlayNext(9);                  // 割り込ませる
            CollectionAssert.AreEqual(new[] { 9, 2, 3 }, Upcoming(model));

            model.NotifyEnded(null);
            Assert.AreEqual(9, model.CurrentIndex, "割り込ませたものが先に鳴る");

            model.NotifyEnded(null);
            Assert.AreEqual(2, model.CurrentIndex, "そのあとは元の並びどおり");
        }

        // ─────────────────────────────────────────────
        // 5. 再生予定そのものの決まりごと
        // ─────────────────────────────────────────────

        [Test]
        public void EnqueueAppendsToTheEnd()
        {
            var model = New();
            model.Enqueue(4);
            model.Enqueue(8);

            CollectionAssert.AreEqual(new[] { 4, 8 }, Upcoming(model));
        }

        [Test]
        public void EnqueueRefusesADuplicate()
        {
            var model = New();
            Assert.IsTrue(model.Enqueue(4));
            Assert.IsFalse(model.Enqueue(4), "同じものを二重に並べない");

            Assert.AreEqual(1, model.QueueCount);
        }

        [Test]
        public void EnqueueRefusesSomethingOutsideTheCatalog()
        {
            var model = New();

            Assert.IsFalse(model.Enqueue(-1));
            Assert.IsFalse(model.Enqueue(CatalogCount));
            Assert.AreEqual(0, model.QueueCount);
        }

        [Test]
        public void PlayNextMovesSomethingAlreadyQueuedToTheFront()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);
            model.Enqueue(3);

            Assert.IsTrue(model.PlayNext(3));
            CollectionAssert.AreEqual(new[] { 3, 1, 2 }, Upcoming(model),
                                      "二重に並べず、前へ動かすだけ");
        }

        [Test]
        public void JumpingSkipsEverythingBeforeIt()
        {
            var model = New();
            model.PlayAt(5, null);
            model.Enqueue(1);
            model.Enqueue(2);
            model.Enqueue(3);

            Assert.IsTrue(model.JumpTo(2, null));

            Assert.AreEqual(3, model.CurrentIndex);
            Assert.AreEqual(0, model.QueueCount, "飛び越したものは再生予定から外れる");
        }

        [Test]
        public void JumpingToTheFirstEntryIsAllowed()
        {
            var model = New();
            model.PlayAt(5, null);
            model.Enqueue(1);
            model.Enqueue(2);

            Assert.IsTrue(model.JumpTo(0, null), "Phase7-3 から先頭も指定できる");
            Assert.AreEqual(1, model.CurrentIndex);
            CollectionAssert.AreEqual(new[] { 2 }, Upcoming(model));
        }

        [Test]
        public void TheUpcomingListStopsGrowingAtItsCapacity()
        {
            var model = new PlaybackModel(PlaybackModel.QueueCapacity + 10);

            for (int i = 0; i < PlaybackModel.QueueCapacity + 5; i++) model.Enqueue(i);

            Assert.AreEqual(PlaybackModel.QueueCapacity, model.QueueCount);
        }

        // ─────────────────────────────────────────────
        // 6. 再生そのもの
        // ─────────────────────────────────────────────

        [Test]
        public void PlayingWithNothingLoadedAndNothingQueuedDoesNothing()
        {
            var model = New();

            Assert.IsFalse(model.Play());
            Assert.IsFalse(model.IsPlaying);
            Assert.AreEqual(-1, model.RequestedIndex);
        }

        [Test]
        public void TheSameThingIsNotLoadedTwice()
        {
            var model = New();
            model.PlayAt(3, null);

            int loads = model.LoadCount;
            model.Play();

            Assert.AreEqual(loads, model.LoadCount, "すでに読み込んでいるものは読み直さない");
        }

        [Test]
        public void PlayAtOnWhatIsAlreadyPlayingDoesNotReload()
        {
            var model = New();
            model.PlayAt(3, null);
            int loads = model.LoadCount;

            Assert.IsTrue(model.PlayAt(3, null));
            Assert.AreEqual(loads, model.LoadCount);
        }

        [Test]
        public void PlayAtResumesWhenTheSameOneIsPaused()
        {
            var model = New();
            model.PlayAt(3, null);
            model.Stop();

            Assert.IsTrue(model.PlayAt(3, null));
            Assert.IsTrue(model.IsPlaying);
        }

        [Test]
        public void PlayAtRefusesSomethingOutsideTheCatalog()
        {
            var model = New();

            Assert.IsFalse(model.PlayAt(-1, null));
            Assert.IsFalse(model.PlayAt(CatalogCount, null));
            Assert.AreEqual(-1, model.CurrentIndex);
        }

        [Test]
        public void StopKeepsWhatIsLoaded()
        {
            var model = New();
            model.PlayAt(3, null);

            Assert.IsTrue(model.Stop());
            Assert.IsFalse(model.IsPlaying);
            Assert.AreEqual(3, model.CurrentIndex, "止めても「何が鳴っていたか」は残る");
        }

        [Test]
        public void TogglePlayPauseGoesBothWays()
        {
            var model = New();
            model.PlayAt(3, null);

            Assert.IsTrue(model.TogglePlayPause());
            Assert.IsFalse(model.IsPlaying);

            Assert.IsTrue(model.TogglePlayPause());
            Assert.IsTrue(model.IsPlaying);
        }

        [Test]
        public void NextUsesTheUpcomingListBeforeAnyRecommendation()
        {
            var model = New();
            model.PlayAt(1, null);
            model.Enqueue(2);

            Assert.IsTrue(model.Next(Candidates(8, 9)));
            Assert.AreEqual(2, model.CurrentIndex, "人が入れたものが先");
        }

        [Test]
        public void NextFallsBackToRecommendationsWhenAPersonAsksForIt()
        {
            var model = New();
            model.PlayAt(1, null);

            Assert.IsTrue(model.Next(Candidates(8, 9)),
                          "人が「次へ」を押したのだから勝手ではない");

            Assert.AreEqual(8, model.CurrentIndex);
            Assert.AreEqual(0, model.QueueCount, "それでも再生予定には積まない");
        }

        [Test]
        public void NextFailsWhenThereIsNothingLeftAtAll()
        {
            var model = New();
            model.PlayAt(1, null);

            Assert.IsFalse(model.Next(null));
            Assert.IsTrue(model.IsExhausted);
        }

        // ─────────────────────────────────────────────
        // 7. 履歴
        // ─────────────────────────────────────────────

        [Test]
        public void PreviousGoesBackToWhatPlayedBefore()
        {
            var model = New();
            model.PlayAt(1, null);
            model.PlayAt(2, null);

            Assert.IsTrue(model.Previous());

            Assert.AreEqual(1, model.CurrentIndex);
            Assert.AreEqual(1, model.RequestedIndex);
            CollectionAssert.AreEqual(new[] { 2 }, Upcoming(model),
                                      "鳴っていたものは「次」に回る");
        }

        [Test]
        public void PreviousFailsWithNoHistory()
        {
            var model = New();
            model.PlayAt(1, null);

            Assert.IsFalse(model.Previous());
            Assert.AreEqual(1, model.CurrentIndex);
        }

        [Test]
        public void SkippedEntriesGoIntoTheHistoryInTheOrderTheyWereHeard()
        {
            var model = New();
            model.PlayAt(5, null);
            model.Enqueue(1);
            model.Enqueue(2);
            model.Enqueue(3);

            model.JumpTo(2, null);      // 5 が終わり、1 と 2 を飛ばして 3 へ

            Assert.AreEqual(3, model.HistoryCount);
            Assert.AreEqual(5, model.GetHistoryAt(0));
            Assert.AreEqual(1, model.GetHistoryAt(1));
            Assert.AreEqual(2, model.GetHistoryAt(2));
        }

        [Test]
        public void HistoryDoesNotGrowWithoutLimit()
        {
            var model = New();
            model.MaxHistory = 3;

            for (int i = 0; i < 8; i++) model.PlayAt(i % CatalogCount, null);

            Assert.AreEqual(3, model.HistoryCount);
        }

        // ─────────────────────────────────────────────
        // 8. 一覧の選択
        // ─────────────────────────────────────────────

        [Test]
        public void SelectRemembersTheChoiceWithoutPlaying()
        {
            var model = New();

            Assert.IsTrue(model.Select(4));
            Assert.AreEqual(4, model.SelectedIndex);
            Assert.IsFalse(model.IsPlaying);
            Assert.AreEqual(-1, model.CurrentIndex);
        }

        [Test]
        public void SelectRefusesSomethingOutsideTheCatalog()
        {
            var model = New();

            Assert.IsFalse(model.Select(-1));
            Assert.IsFalse(model.Select(CatalogCount));
            Assert.AreEqual(-1, model.SelectedIndex);
        }

        // ─────────────────────────────────────────────
        // 9. 失敗したとき
        // ─────────────────────────────────────────────

        [Test]
        public void AFailureSkipsTheBrokenOne()
        {
            var model = New();
            model.PlayAt(5, null);
            model.Enqueue(1);

            Assert.IsTrue(model.NotifyError(null));
            Assert.AreEqual(1, model.CurrentIndex);
        }

        [Test]
        public void AFailureWithNothingQueuedStopsWithoutPickingSomethingElse()
        {
            var model = New();
            model.PlayAt(5, null);

            Assert.IsFalse(model.NotifyError(Candidates(6, 7)),
                           "壊れた URL を飛ばすためのもので、勝手に別の曲を流すためではない");
            Assert.IsFalse(model.IsPlaying);
        }

        [Test]
        public void RepeatedFailuresEventuallyGiveUp()
        {
            var model = New();
            model.MaxConsecutiveErrors = 2;
            model.PlayAt(0, null);
            for (int i = 1; i < CatalogCount; i++) model.Enqueue(i);

            Assert.IsTrue(model.NotifyError(null));
            Assert.IsTrue(model.NotifyError(null));
            Assert.IsFalse(model.NotifyError(null), "3 回目で諦める");

            Assert.IsFalse(model.IsPlaying);
            Assert.IsTrue(model.IsExhausted);
        }

        [Test]
        public void GivingUpDoesNotLoadAnythingMore()
        {
            var model = New();
            model.MaxConsecutiveErrors = 1;
            model.PlayAt(0, null);
            for (int i = 1; i < CatalogCount; i++) model.Enqueue(i);

            model.NotifyError(null);
            model.NotifyError(null);
            int loads = model.LoadCount;

            model.NotifyError(null);
            model.NotifyError(null);

            Assert.AreEqual(loads, model.LoadCount, "諦めたあとは読み込まない");
        }

        [Test]
        public void PlayingSuccessfullyResetsTheFailureCount()
        {
            var model = New();
            model.MaxConsecutiveErrors = 2;
            model.PlayAt(0, null);
            model.Enqueue(1);
            model.Enqueue(2);
            model.Enqueue(3);

            model.NotifyError(null);
            model.NotifyStarted();

            Assert.AreEqual(0, model.ConsecutiveErrors);
            Assert.IsTrue(model.NotifyError(null), "数え直したので、まだ諦めない");
        }

        [Test]
        public void PressingPlayGivesAnotherChanceAfterGivingUp()
        {
            var model = New();
            model.MaxConsecutiveErrors = 1;
            model.PlayAt(0, null);
            model.Enqueue(1);

            model.NotifyError(null);
            model.NotifyError(null);
            Assert.IsFalse(model.IsPlaying);

            Assert.IsTrue(model.Play(), "人が押したらもう一度試せる");
            Assert.AreEqual(0, model.ConsecutiveErrors);
        }

        [Test]
        public void TheCapCanBeTurnedOff()
        {
            var model = New();
            model.MaxConsecutiveErrors = 0;
            model.PlayAt(0, null);
            for (int i = 1; i < CatalogCount; i++) model.Enqueue(i);

            for (int i = 1; i < CatalogCount; i++)
            {
                Assert.IsTrue(model.NotifyError(null), "0 なら諦めない");
            }
        }

        // ─────────────────────────────────────────────
        // 10. 同期(Phase5-4)
        // ─────────────────────────────────────────────

        [Test]
        public void ApplyingASyncedStateReplacesTheWholeUpcomingList()
        {
            var model = New();
            model.Enqueue(1);
            model.Enqueue(2);
            model.Play();

            model.ApplySyncedState(new[] { 7, 8 }, true, 6);

            Assert.AreEqual(6, model.CurrentIndex, "持ち主が鳴らしているものになる");
            CollectionAssert.AreEqual(new[] { 7, 8 }, Upcoming(model));
            Assert.IsTrue(model.IsPlaying);
        }

        [Test]
        public void ApplyingASyncedStateDoesNotAskTheBackendToLoadAnything()
        {
            var model = New();
            model.PlayAt(1, null);
            int loads = model.LoadCount;

            model.ApplySyncedState(new[] { 7 }, true, 6);

            Assert.AreEqual(loads, model.LoadCount,
                            "読み込ませるのは UdonSyncCoordinator の仕事");
        }

        [Test]
        public void ANullSyncedQueueIsTreatedAsEmpty()
        {
            var model = New();
            model.Enqueue(1);

            model.ApplySyncedState(null, false, -1);

            Assert.AreEqual(0, model.QueueCount);
            Assert.AreEqual(-1, model.CurrentIndex);
        }

        [Test]
        public void ASyncedStateLongerThanTheQueueIsTruncated()
        {
            var model = new PlaybackModel(PlaybackModel.QueueCapacity + 10);

            var oversized = new int[PlaybackModel.QueueCapacity + 5];
            for (int i = 0; i < oversized.Length; i++) oversized[i] = i;

            model.ApplySyncedState(oversized, true, 0);

            Assert.AreEqual(PlaybackModel.QueueCapacity, model.QueueCount);
        }

        [Test]
        public void ApplyingAPausedStateStopsWithoutClearingTheUpcomingList()
        {
            var model = New();
            model.ApplySyncedState(new[] { 7, 8 }, false, 6);

            Assert.IsFalse(model.IsPlaying);
            Assert.AreEqual(2, model.QueueCount);
            Assert.AreEqual(6, model.CurrentIndex);
        }

        [Test]
        public void ASnapshotSurvivesARoundTrip()
        {
            var source = New();
            source.PlayAt(3, null);
            source.Enqueue(1);
            source.Enqueue(2);

            var mirror = New();
            mirror.ApplySyncedState(source.SnapshotQueue(), source.IsPlaying, source.CurrentIndex);

            Assert.AreEqual(source.CurrentIndex, mirror.CurrentIndex);
            CollectionAssert.AreEqual(Upcoming(source), Upcoming(mirror));
            Assert.AreEqual(source.IsPlaying, mirror.IsPlaying);
        }

        // ─────────────────────────────────────────────
        // 11. Udon で書ける形か
        // ─────────────────────────────────────────────

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
