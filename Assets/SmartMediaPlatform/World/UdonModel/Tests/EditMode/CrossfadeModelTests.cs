using NUnit.Framework;
using SmartMediaPlatform.World.UdonModel;

namespace SmartMediaPlatform.World.UdonModel.Tests
{
    /// <summary>
    /// <see cref="CrossfadeModel"/> —— 重ねるクロスフェードの段取り(Phase8-5)。
    ///
    /// <b>いちばん困るのは「今の曲が止まる」「次の曲が鳴らない」「同じ曲を読み直す」</b>の 3 つです。
    /// どの失敗の道でも、それが起きないことを確かめます。
    /// </summary>
    public class CrossfadeModelTests
    {
        const float Length = 200f;
        const int Next = 7;

        // 既定: 重ねる 6 秒 + 手前 0.8 秒 = 残り 6.8 秒で重ね始め、そこから 20 秒前(残り 26.8 秒)に読む。

        static CrossfadeModel New()
        {
            return new CrossfadeModel
            {
                FadeSeconds = 6f,
                SilentBeforeEnd = 0.8f,
                PrepareLeadSeconds = 20f,
                StartLeadSeconds = 0.3f,
                MinimumTrackSeconds = 45f,
                MaximumTrackSeconds = 86400f,
                SeekBackMargin = 3f,
            };
        }

        /// <summary>ふつうに再生中の表で判断させる。</summary>
        static int At(CrossfadeModel model, float remaining, int next = Next, bool backReady = false,
                      bool playing = true, bool enabled = true, bool blocked = false, int load = 1,
                      float length = Length)
        {
            return model.Decide(enabled, blocked, playing, true, playing,
                                length - remaining, length, load, next, backReady);
        }

        // ───────── いつ読むか ─────────

        [Test]
        public void 読み込み始める所より前では何もしない()
        {
            var model = New();

            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 100f));
            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 26.9f));
        }

        [Test]
        public void 残り26_8秒で裏に読み込ませる()
        {
            var model = New();

            Assert.AreEqual(26.8f, model.PreloadRemaining, 1e-4f);
            Assert.AreEqual(CrossfadeModel.ActionPreload, At(model, 26.7f));
            Assert.AreEqual(CrossfadeModel.ActionPreload, At(model, 10f), "遅れても読む");
        }

        [Test]
        public void 終わりぎりぎりでは読まない()
        {
            var model = New();

            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 1.7f));
            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 0.2f));
        }

        [Test]
        public void 次の曲が無ければ読まない()
        {
            var model = New();
            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 20f, next: -1));
        }

        [Test]
        public void 使えない構成や止める予定があれば読まない()
        {
            var model = New();

            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 20f, enabled: false));
            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 20f, blocked: true), "おやすみタイマー");
            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 20f, playing: false), "一時停止中");
        }

        [Test]
        public void 表が今の曲を鳴らしていなければ読まない()
        {
            var model = New();

            // 読み込み中・外部 URL を鳴らしている
            Assert.AreEqual(CrossfadeModel.ActionNone,
                model.Decide(true, false, true, false, true, Length - 20f, Length, 1, Next, false));
        }

        [Test]
        public void 長さが分からない曲や短い曲では重ねない()
        {
            var model = New();

            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 20f, length: 0f), "生配信");
            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 20f, length: 40f), "短い曲");
            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 20f, length: 1e9f), "長さがおかしい");
            Assert.AreEqual(CrossfadeModel.ActionPreload, At(model, 20f, length: 45f), "ちょうど下限");
        }

        // ───────── いつ鳴らすか ─────────

        [Test]
        public void 読み終わっていても重ねる所までは鳴らさない()
        {
            var model = New();
            model.MarkPreloading(Next);

            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 20f, backReady: true));
            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 7.2f, backReady: true));
        }

        [Test]
        public void 表が下がり始める少し前に裏を鳴らす()
        {
            var model = New();
            model.MarkPreloading(Next);

            // 6.8 + 0.3 = 7.1
            Assert.AreEqual(CrossfadeModel.ActionStartBack, At(model, 7.0f, backReady: true));
            Assert.AreEqual(CrossfadeModel.ActionStartBack, At(model, 3f, backReady: true), "遅れて読み終わっても鳴らす");
        }

        [Test]
        public void 読み終わっていなければ待つ()
        {
            var model = New();
            model.MarkPreloading(Next);

            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 5f, backReady: false));
            Assert.AreEqual(CrossfadeModel.StatePreloading, model.State);
        }

        [Test]
        public void 一時停止中は読み込んだまま待つ()
        {
            var model = New();
            model.MarkPreloading(Next);

            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 5f, backReady: true, playing: false));
            Assert.AreEqual(CrossfadeModel.StatePreloading, model.State);
        }

        // ───────── やめる ─────────

        [Test]
        public void 読み込み中に次の曲が変わったらやめる()
        {
            var model = New();
            model.MarkPreloading(Next);

            Assert.AreEqual(CrossfadeModel.ActionCancel, At(model, 20f, next: 9));
            Assert.AreEqual(CrossfadeModel.ActionCancel, At(model, 20f, next: -1));
        }

        [Test]
        public void 重なっている最中は次の曲が変わってもやめない()
        {
            var model = New();
            model.MarkPreloading(Next);
            model.MarkOverlapping();

            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 4f, next: 9));
            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 4f, next: -1));
        }

        [Test]
        public void バーを大きく戻されたらやめる()
        {
            var model = New();
            model.MarkPreloading(Next);

            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 29.7f), "少しだけなら続ける");
            Assert.AreEqual(CrossfadeModel.ActionCancel, At(model, 29.9f));

            model.Reset();
            model.MarkPreloading(Next);
            model.MarkOverlapping();

            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 10f));
            Assert.AreEqual(CrossfadeModel.ActionCancel, At(model, 10.5f));
        }

        [Test]
        public void 重なっている最中に一時停止されたら裏を止める()
        {
            var model = New();
            model.MarkPreloading(Next);
            model.MarkOverlapping();

            Assert.AreEqual(CrossfadeModel.ActionCancel, At(model, 4f, playing: false));
        }

        [Test]
        public void 止める予定になったらやめる()
        {
            var model = New();
            model.MarkPreloading(Next);
            Assert.AreEqual(CrossfadeModel.ActionCancel, At(model, 20f, blocked: true));

            model.Reset();
            model.MarkPreloading(Next);
            model.MarkOverlapping();
            Assert.AreEqual(CrossfadeModel.ActionCancel, At(model, 4f, blocked: true));
        }

        [Test]
        public void 表が別の物を鳴らし始めたらやめる()
        {
            var model = New();
            model.MarkPreloading(Next);

            Assert.AreEqual(CrossfadeModel.ActionCancel,
                model.Decide(true, false, true, false, true, Length - 20f, Length, 1, Next, false));
        }

        [Test]
        public void 表の長さが分からなくなったらやめる()
        {
            var model = New();
            model.MarkPreloading(Next);

            Assert.AreEqual(CrossfadeModel.ActionCancel, At(model, 20f, length: 0f));
        }

        [Test]
        public void 設定で切られたらやめる()
        {
            var model = New();
            model.MarkPreloading(Next);

            Assert.AreEqual(CrossfadeModel.ActionCancel, At(model, 20f, enabled: false));
        }

        // ───────── 失敗 ─────────

        [Test]
        public void 裏が失敗した曲ではもう読まない()
        {
            var model = New();
            model.MarkPreloading(Next);
            model.MarkFailed(1);

            Assert.IsFalse(model.IsBusy);
            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 20f, load: 1));
            Assert.AreEqual(CrossfadeModel.ActionNone, At(model, 10f, load: 1));
        }

        [Test]
        public void 失敗は次の曲まで持ち越さない()
        {
            var model = New();
            model.MarkPreloading(Next);
            model.MarkFailed(1);

            Assert.AreEqual(CrossfadeModel.ActionPreload, At(model, 20f, load: 2));
        }

        // ───────── 表が終わったとき ─────────

        [Test]
        public void 表が終わったら裏を表にする()
        {
            var model = New();
            model.MarkPreloading(Next);
            model.MarkOverlapping();

            Assert.AreEqual(Next, model.TakeSwap(false));
            Assert.IsFalse(model.IsBusy);
            Assert.AreEqual(-1, model.PendingIndex);
        }

        [Test]
        public void 読み込みが間に合わなくても表が終わったら裏を表にする()
        {
            // 裏はまだ読み込み中。読み直さずに、そのまま表にする(鳴り始めた所から上がる)。
            var model = New();
            model.MarkPreloading(Next);

            Assert.AreEqual(Next, model.TakeSwap(false));
        }

        [Test]
        public void 止める予定なら表にしない()
        {
            var model = New();
            model.MarkPreloading(Next);
            model.MarkOverlapping();

            Assert.AreEqual(-1, model.TakeSwap(true));
            Assert.IsFalse(model.IsBusy, "やめた状態に戻る");
        }

        [Test]
        public void 裏を使っていなければ何もしない()
        {
            var model = New();
            Assert.AreEqual(-1, model.TakeSwap(false));
        }

        // ───────── いますぐこの曲を ─────────

        [Test]
        public void 裏に読ませてある曲を選ばれたら読み直さない()
        {
            var model = New();
            model.MarkPreloading(Next);
            model.MarkOverlapping();

            Assert.AreEqual(Next, model.PromoteFor(Next));
            Assert.IsFalse(model.IsBusy);
        }

        [Test]
        public void 別の曲を選ばれたら裏はやめる()
        {
            var model = New();
            model.MarkPreloading(Next);

            Assert.AreEqual(-1, model.PromoteFor(9));
            Assert.IsFalse(model.IsBusy);
            Assert.AreEqual(-1, model.PromoteFor(-1));
        }

        [Test]
        public void 裏を使っていないときの選曲は何もしない()
        {
            var model = New();
            Assert.AreEqual(-1, model.PromoteFor(Next));
        }

        // ───────── 状態の出入り ─────────

        [Test]
        public void 状態は決まった順にしか進まない()
        {
            var model = New();

            model.MarkOverlapping();
            Assert.AreEqual(CrossfadeModel.StateIdle, model.State, "読んでいないのに重ならない");

            model.MarkPreloading(-1);
            Assert.AreEqual(CrossfadeModel.StateIdle, model.State, "曲が無いのに読まない");

            model.MarkPreloading(Next);
            model.MarkPreloading(9);
            Assert.AreEqual(Next, model.PendingIndex, "読み込み中に上書きしない");

            model.MarkOverlapping();
            Assert.AreEqual(CrossfadeModel.StateOverlapping, model.State);
        }

        [Test]
        public void 一曲まるごと流すと読む鳴らす入れ替えるが1回ずつ起きる()
        {
            var model = New();
            int preloads = 0, starts = 0, cancels = 0;
            bool backReady = false;

            for (float remaining = Length; remaining > 0.3f; remaining -= 0.1f)
            {
                int action = At(model, remaining, backReady: backReady);

                if (action == CrossfadeModel.ActionPreload) { preloads++; model.MarkPreloading(Next); }
                if (action == CrossfadeModel.ActionStartBack) { starts++; model.MarkOverlapping(); }
                if (action == CrossfadeModel.ActionCancel) { cancels++; model.Reset(); }

                // 読み込みに 5 秒かかる
                if (model.State == CrossfadeModel.StatePreloading && remaining < 21.8f) backReady = true;
            }

            Assert.AreEqual(1, preloads);
            Assert.AreEqual(1, starts);
            Assert.AreEqual(0, cancels);
            Assert.AreEqual(Next, model.TakeSwap(false));
        }
    }
}
