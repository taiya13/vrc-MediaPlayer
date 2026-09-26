namespace SmartMediaPlatform.Video
{
    /// <summary>
    /// Udon 側の中継(<c>UdonVRCVideoEventRelay</c>)が積む <b>int のイベントコード</b>と、
    /// C# 側の <see cref="VideoEventKind"/> / <see cref="VideoErrorKind"/> を相互変換する。
    ///
    /// <b>なぜ int なのか</b><br/>
    /// UdonSharp は インターフェース も enum 配列の受け渡しも苦手なので、
    /// Udon から C# へ運べるのは実質 <c>int[]</c> だけです。
    /// そこで「イベント 1 件 = int 1 個」に符号化して運び、
    /// <b>意味の解釈はこの純粋 C# の 1 箇所に集約</b>しています。
    /// おかげで符号化の正しさを EditMode テストで確認できます。
    ///
    /// <b>符号</b>
    /// <list type="bullet">
    /// <item>1〜6 … <see cref="VideoEventKind.Ready"/> 〜 <see cref="VideoEventKind.Loop"/> と同じ値</item>
    /// <item>100 + VideoError の値 … エラー</item>
    /// </list>
    /// </summary>
    public static class VideoEventCodec
    {
        /// <summary>エラーコードの base。Udon 側の <c>CodeErrorBase</c> と一致させること。</summary>
        public const int ErrorBase = 100;

        /// <summary>
        /// Udon 側のイベントコードを解釈する。
        /// </summary>
        /// <param name="code">中継が積んだ int。</param>
        /// <param name="kind">イベント種別。</param>
        /// <param name="error">
        /// <paramref name="kind"/> が <see cref="VideoEventKind.Error"/> のときの理由。
        /// それ以外では <see cref="VideoErrorKind.None"/>。
        /// </param>
        /// <returns>解釈できたら true。未知のコードなら false。</returns>
        public static bool TryDecode(int code, out VideoEventKind kind, out VideoErrorKind error)
        {
            kind = VideoEventKind.None;
            error = VideoErrorKind.None;

            if (code >= ErrorBase)
            {
                kind = VideoEventKind.Error;
                error = TranslateSdkError(code - ErrorBase);
                return true;
            }

            switch (code)
            {
                case (int)VideoEventKind.Ready: kind = VideoEventKind.Ready; return true;
                case (int)VideoEventKind.Start: kind = VideoEventKind.Start; return true;
                case (int)VideoEventKind.Play: kind = VideoEventKind.Play; return true;
                case (int)VideoEventKind.Pause: kind = VideoEventKind.Pause; return true;
                case (int)VideoEventKind.End: kind = VideoEventKind.End; return true;
                case (int)VideoEventKind.Loop: kind = VideoEventKind.Loop; return true;
                default: return false;
            }
        }

        /// <summary>エラー以外のイベントを符号化する。</summary>
        public static int Encode(VideoEventKind kind)
        {
            return kind == VideoEventKind.Error ? ErrorBase : (int)kind;
        }

        /// <summary>エラーを符号化する。</summary>
        public static int EncodeError(VideoErrorKind error)
        {
            return ErrorBase + TranslateToSdkError(error);
        }

        /// <summary>
        /// SDK の <c>VRC.SDK3.Components.Video.VideoError</c> の int 値を
        /// <see cref="VideoErrorKind"/> へ写す。
        ///
        /// SDK の enum は Unknown=0 / InvalidURL=1 / AccessDenied=2 / PlayerError=3 / RateLimited=4。
        /// この対応は <c>VRChatVideoBackendHost.Translate</c> と同じ内容を、
        /// SDK の型に触れずに int で行うものです。
        /// </summary>
        public static VideoErrorKind TranslateSdkError(int sdkErrorValue)
        {
            switch (sdkErrorValue)
            {
                case 1: return VideoErrorKind.InvalidUrl;
                case 2: return VideoErrorKind.AccessDenied;
                case 3: return VideoErrorKind.PlayerError;
                case 4: return VideoErrorKind.RateLimited;
                default: return VideoErrorKind.Unknown;
            }
        }

        /// <summary><see cref="TranslateSdkError"/> の逆。</summary>
        public static int TranslateToSdkError(VideoErrorKind error)
        {
            switch (error)
            {
                case VideoErrorKind.InvalidUrl: return 1;
                case VideoErrorKind.AccessDenied: return 2;
                case VideoErrorKind.PlayerError: return 3;
                case VideoErrorKind.RateLimited: return 4;
                default: return 0;
            }
        }

        /// <summary>
        /// 符号化されたイベントを <see cref="IVideoEventSink"/> へ流す。
        ///
        /// <c>UdonVideoEventPump</c>(SDK 側)はこれを呼ぶだけです。
        /// <b>どのイベントをどう扱うかの判断は一切 SDK 側に置きません。</b>
        /// </summary>
        /// <returns>Sink が受理したら true。未知のコードや棄却なら false。</returns>
        public static bool Deliver(IVideoEventSink sink, int code)
        {
            if (sink == null) return false;
            if (!TryDecode(code, out var kind, out var error)) return false;

            switch (kind)
            {
                case VideoEventKind.Ready: return sink.OnVideoReady();
                case VideoEventKind.Start: return sink.OnVideoStart();
                case VideoEventKind.Play: return sink.OnVideoPlay();
                case VideoEventKind.Pause: return sink.OnVideoPause();
                case VideoEventKind.End: return sink.OnVideoEnd();
                case VideoEventKind.Loop: return sink.OnVideoLoop();
                case VideoEventKind.Error: return sink.OnVideoError(error);
                default: return false;
            }
        }
    }
}
