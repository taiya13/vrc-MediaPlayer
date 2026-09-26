using NUnit.Framework;
using SmartMediaPlatform.World.UdonModel;

namespace SmartMediaPlatform.World.UdonModel.Tests
{
    /// <summary>
    /// <see cref="TrackFadeModel"/> —— 重ねない方式のフェード。
    ///
    /// <b>いちばん困るのは「音が 0 のまま戻らない」</b>ことです。
    /// 見た目には再生しているのに無音になり、壊れたとしか思えません。
    /// その道が無いことを、ここで繰り返し確かめます。
    /// </summary>
    public class TrackFadeModelTests
    {
        const float Length = 200f;

        static TrackFadeModel New()
        {
            return new TrackFadeModel
            {
                FadeOutSeconds = 3f,
                FadeInSeconds = 2f,
                SilentBeforeEnd = 0.8f,
                MinimumTrackSeconds = 20f,
                MaximumTrackSeconds = 86400f,
            };
        }

        /// <summary>ひとりでに終わって、次の曲が始まるまでを進める。戻り値は次の曲の頭の倍率。</summary>
        static float PlayToEndThenStartNext(TrackFadeModel model, int load, float now)
        {
            model.Tick(load, true, 0f, Length, now);
            model.Tick(load, true, Length - 0.5f, Length, now + 199f);   // 終わり際
            model.Tick(load, false, Length - 0.5f, Length, now + 199.1f); // 終わりを見つけて止めた
            model.Tick(load + 1, false, 0f, 0f, now + 200f);            // 次の読み込み
            return model.Tick(load + 1, true, 0f, 180f, now + 202f);     // 次が鳴り始めた
        }

        // ───────── 下げる(計算だけ)─────────

        [Test]
        public void 残りが多いうちは下げない()
        {
            Assert.AreEqual(1f, TrackFadeModel.FadeOutLevel(10f, Length, 3f, 0.8f, 20f, 86400f));
            Assert.AreEqual(1f, TrackFadeModel.FadeOutLevel(Length - 3.8f, Length, 3f, 0.8f, 20f, 86400f));
        }

        [Test]
        public void 終わりに向かってなめらかに下がる()
        {
            float early = TrackFadeModel.FadeOutLevel(Length - 3.0f, Length, 3f, 0.8f, 20f, 86400f);
            float mid = TrackFadeModel.FadeOutLevel(Length - 2.3f, Length, 3f, 0.8f, 20f, 86400f);
            float late = TrackFadeModel.FadeOutLevel(Length - 1.2f, Length, 3f, 0.8f, 20f, 86400f);

            Assert.Less(early, 1f);
            Assert.Less(mid, early);
            Assert.Less(late, mid);
            Assert.Greater(late, 0f);
        }

        [Test]
        public void 終わりを見つけるより前に0になっている()
        {
            // 見張りは「残り 0.4 秒」で終わりとみなし、見に行く間隔は 0.4 秒。
            // 残り 0.8 秒の時点でもう 0 でないと、少し鳴ったまま止まる。
            Assert.AreEqual(0f, TrackFadeModel.FadeOutLevel(Length - 0.8f, Length, 3f, 0.8f, 20f, 86400f));
            Assert.AreEqual(0f, TrackFadeModel.FadeOutLevel(Length - 0.1f, Length, 3f, 0.8f, 20f, 86400f));
            Assert.AreEqual(0f, TrackFadeModel.FadeOutLevel(Length + 5f, Length, 3f, 0.8f, 20f, 86400f));
        }

        [Test]
        public void 長さが分からないものは下げない()
        {
            // 生配信・読み込み中。ここで下げると、いつまでも小さいまま。
            Assert.AreEqual(1f, TrackFadeModel.FadeOutLevel(5000f, 0f, 3f, 0.8f, 20f, 86400f));
            Assert.AreEqual(1f, TrackFadeModel.FadeOutLevel(0.5f, 1f, 3f, 0.8f, 20f, 86400f));
            Assert.AreEqual(1f, TrackFadeModel.FadeOutLevel(10f, float.MaxValue, 3f, 0.8f, 20f, 86400f));
        }

        [Test]
        public void 短い曲は下げない()
        {
            Assert.AreEqual(1f, TrackFadeModel.FadeOutLevel(14f, 15f, 3f, 0.8f, 20f, 86400f));
        }

        [Test]
        public void 下げない設定なら常に1()
        {
            Assert.AreEqual(1f, TrackFadeModel.FadeOutLevel(Length - 0.1f, Length, 0f, 0.8f, 20f, 86400f));
        }

        [Test]
        public void 終わり際から戻せば音量も戻る()
        {
            // 残り時間だけで決めているので、シークで戻せば自然に 1 へ戻る。
            Assert.Less(TrackFadeModel.FadeOutLevel(Length - 1f, Length, 3f, 0.8f, 20f, 86400f), 1f);
            Assert.AreEqual(1f, TrackFadeModel.FadeOutLevel(Length - 30f, Length, 3f, 0.8f, 20f, 86400f));
        }

        // ───────── 上げる(計算だけ)─────────

        [Test]
        public void 上げるほうは0から1まで()
        {
            Assert.AreEqual(0f, TrackFadeModel.FadeInLevel(0f, 2f));
            Assert.AreEqual(0.5f, TrackFadeModel.FadeInLevel(1f, 2f), 1e-5f);
            Assert.AreEqual(1f, TrackFadeModel.FadeInLevel(2f, 2f));
            Assert.AreEqual(1f, TrackFadeModel.FadeInLevel(99f, 2f));
            Assert.AreEqual(1f, TrackFadeModel.FadeInLevel(0f, 0f), "上げない設定ならすぐ全開");
        }

        // ───────── 状態を持った流れ ─────────

        [Test]
        public void ワールドに入った最初の曲はフェードインしない()
        {
            TrackFadeModel model = New();

            float level = model.Tick(1, true, 0f, Length, 10f);

            Assert.AreEqual(1f, level);
            Assert.IsFalse(model.FadeInArmed);
        }

        [Test]
        public void ひとりでに終わったら次の曲は0から上がる()
        {
            TrackFadeModel model = New();

            float atStart = PlayToEndThenStartNext(model, 1, 0f);

            Assert.AreEqual(0f, atStart, "鳴り始めた瞬間は 0");
            Assert.AreEqual(0.5f, model.Tick(2, true, 1f, 180f, 203f), 1e-4f);
            Assert.AreEqual(1f, model.Tick(2, true, 2f, 180f, 204f));
        }

        [Test]
        public void 上がりきったあとで頭へ戻しても下がらない()
        {
            TrackFadeModel model = New();

            PlayToEndThenStartNext(model, 1, 0f);
            model.Tick(2, true, 3f, 180f, 205f);    // 上がりきった

            Assert.AreEqual(1f, model.Tick(2, true, 0f, 180f, 206f), "頭へシークしても全開のまま");
            Assert.IsFalse(model.FadeInArmed);
        }

        [Test]
        public void 手で途中から別の曲にしたら全開で始まる()
        {
            TrackFadeModel model = New();

            model.Tick(1, true, 60f, Length, 60f);            // 曲の途中
            model.Tick(2, false, 60f, Length, 61f);           // 手で別の曲を選んだ
            float level = model.Tick(2, true, 0f, 180f, 63f); // 鳴り始めた

            Assert.AreEqual(1f, level);
            Assert.IsFalse(model.FadeInArmed);
        }

        [Test]
        public void 終わりを見つけてから次の読み込みまでの間に1へ戻らない()
        {
            // ここで 1 に戻すと、次の曲が変わった瞬間に「手で変えた」と取り違える。
            TrackFadeModel model = New();

            model.Tick(1, true, Length - 0.5f, Length, 199f);
            float waiting = model.Tick(1, false, Length - 0.5f, Length, 199.5f);

            Assert.AreEqual(0f, waiting);

            model.Tick(2, false, 0f, 0f, 205f);   // 読み込み間隔で待たされても
            Assert.IsTrue(model.FadeInArmed);
        }

        [Test]
        public void 次の曲が読み込み中のあいだは無音のまま()
        {
            TrackFadeModel model = New();

            model.Tick(1, true, Length - 0.5f, Length, 199f);
            model.Tick(1, false, Length - 0.5f, Length, 199.5f);

            // 読み込み中は、前の曲の位置と長さが返ってくることがある。
            float loading = model.Tick(2, false, 150f, Length, 200f);

            Assert.AreEqual(0f, loading);
        }

        [Test]
        public void 読み込みに失敗して次へ飛んでもフェードインは残る()
        {
            TrackFadeModel model = New();

            model.Tick(1, true, Length - 0.5f, Length, 199f);
            model.Tick(1, false, Length - 0.5f, Length, 199.5f);
            model.Tick(2, false, 0f, 0f, 200f);       // 読み込んだが失敗
            model.Tick(3, false, 0f, 0f, 205f);       // 次を読み込んだ
            float level = model.Tick(3, true, 0f, 180f, 207f);

            Assert.AreEqual(0f, level);
            Assert.AreEqual(1f, model.Tick(3, true, 2f, 180f, 209f));
        }

        [Test]
        public void 生配信でも0のまま止まらない()
        {
            // 生配信は再生位置が進まないことがある。上げる速さを実時間で数えるので、必ず上がりきる。
            TrackFadeModel model = New();

            model.Tick(1, true, Length - 0.5f, Length, 199f);
            model.Tick(1, false, Length - 0.5f, Length, 199.5f);
            model.Tick(2, false, 0f, 0f, 200f);

            model.Tick(2, true, 0f, 0f, 201f);
            float later = model.Tick(2, true, 0f, 0f, 204f);

            Assert.AreEqual(1f, later);
        }

        [Test]
        public void 一時停止してから再開しても下げ具合がずれない()
        {
            TrackFadeModel model = New();

            model.Tick(1, true, 0f, Length, 0f);
            float before = model.Tick(1, true, Length - 2f, Length, 198f);

            // 一時停止中(位置は進まない)に実時間だけ進む
            float paused = model.Tick(1, true, Length - 2f, Length, 260f);

            Assert.AreEqual(before, paused, 1e-5f);
        }

        [Test]
        public void フェードインの途中で曲が終わりに来たら小さいほうに従う()
        {
            // 短い曲(ただし下げる対象の長さ)でも、上げている途中に下げ始めて音が跳ねない。
            TrackFadeModel model = New();
            model.MinimumTrackSeconds = 0f;

            model.Tick(1, true, 4f, 5f, 0f);
            model.Tick(1, false, 4.8f, 5f, 1f);
            model.Tick(2, false, 0f, 0f, 2f);
            model.Tick(2, true, 0f, 5f, 3f);

            float level = model.Tick(2, true, 3.5f, 5f, 3.5f);

            Assert.Less(level, 1f);
            Assert.GreaterOrEqual(level, 0f);
        }

        [Test]
        public void リセットすると全開に戻り次もフェードインしない()
        {
            TrackFadeModel model = New();

            model.Tick(1, true, Length - 0.5f, Length, 199f);
            model.Reset();

            Assert.AreEqual(1f, model.Level);
            model.Tick(2, false, 0f, 0f, 200f);
            Assert.IsFalse(model.FadeInArmed);
        }

        [Test]
        public void フェードインしない設定なら次の曲も全開()
        {
            TrackFadeModel model = New();
            model.FadeInSeconds = 0f;

            float level = PlayToEndThenStartNext(model, 1, 0f);

            Assert.AreEqual(1f, level);
        }

        [Test]
        public void 倍率はいつも0から1の範囲()
        {
            TrackFadeModel model = New();
            float now = 0f;

            for (int load = 1; load < 6; load++)
            {
                for (float t = -5f; t < Length + 5f; t += 7.3f)
                {
                    now += 0.7f;
                    float level = model.Tick(load, t > 0f, t, Length, now);
                    Assert.GreaterOrEqual(level, 0f);
                    Assert.LessOrEqual(level, 1f);
                }
            }
        }
    }
}
