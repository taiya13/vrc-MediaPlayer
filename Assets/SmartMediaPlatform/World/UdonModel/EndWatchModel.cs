namespace SmartMediaPlatform.World.UdonModel
{
    /// <summary>
    /// <b>「曲が終わった」を再生位置から見つける規則。</b>Phase7-6。
    ///
    /// <b>なぜ要るのか</b><br/>
    /// 上位が次へ進むきっかけは、VRChat からの「終わりました」(<c>OnVideoEnd</c>)
    /// ひとつだけでした。ところが実機では
    /// <list type="bullet">
    /// <item>AVPro だと、その知らせが届かないことがある</item>
    /// <item>プレイヤー側の繰り返しが切れていないと、届かないまま頭へ戻る</item>
    /// </list>
    /// のどちらでも、上位は<b>終わったことを永久に知りません</b>。
    /// 「曲が終わるとまた最初から始まる」はこの形です。
    ///
    /// ここは<b>時計を見て判断する部分だけ</b>を切り出したものです。
    /// <c>UdonVideoBackend.WatchForEnd</c> が 1:1 で写しています。
    /// </summary>
    public static class EndWatchModel
    {
        /// <summary>
        /// <b>長さが分かっているか。</b>生配信は長さが取れないので、
        /// 終わりも判断できません(0 が返ります)。
        /// </summary>
        public static bool HasKnownLength(float duration)
        {
            return duration > 1f;
        }

        /// <summary>
        /// <b>終わり際まで来たか。</b>
        /// ぴったり <paramref name="duration"/> になるとは限らない
        /// (見に行く間隔のぶんだけ飛ぶ)ので、手前で決めます。
        /// </summary>
        public static bool ReachedEnd(float time, float duration, float threshold)
        {
            if (!HasKnownLength(duration)) return false;
            return time >= duration - threshold;
        }

        /// <summary>
        /// <b>頭へ戻ったか(= プレイヤーが勝手に繰り返した)。</b>
        ///
        /// <b>人が手でバーを戻したときと区別しなければなりません。</b>
        /// 区別の手がかりは「戻る<b>前</b>にどこにいたか」です。
        /// 繰り返しは<b>終わり際からしか起きません</b>。
        /// 曲の途中から頭へ戻ったのなら、それは人が動かしたのです。
        /// </summary>
        /// <param name="previousTime">ひとつ前に見たときの位置。</param>
        /// <param name="time">いまの位置。</param>
        public static bool WrappedToStart(float previousTime, float time, float duration)
        {
            if (!HasKnownLength(duration)) return false;

            // 直前が終わり際にいなかったなら、繰り返しではない。
            if (previousTime <= duration - EndZoneSeconds) return false;

            // 大きく戻っていなければ、ただの揺れ。
            return time < previousTime - BackJumpSeconds;
        }

        /// <summary>ここから先を「終わり際」とみなす幅(秒)。</summary>
        public const float EndZoneSeconds = 2f;

        /// <summary>これだけ戻ったら「頭へ戻った」とみなす(秒)。</summary>
        public const float BackJumpSeconds = 2f;
    }
}
