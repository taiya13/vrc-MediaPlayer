using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components.Video;

namespace SmartMediaPlatform.Video.Udon
{
    /// <summary>
    /// <b>VRChat の動画イベントを実際に受け取る唯一のコンポーネント。</b>
    ///
    /// VRChat の動画プレイヤーは、イベントを<b>同じ GameObject の UdonBehaviour</b> へ
    /// 送ります。C# の MonoBehaviour では受け取れないため、この中継が要ります。
    /// Phase1-2 の <c>UdonMediaCatalog</c> と同じ「純粋C#版 + Udon版」二本立ての考え方です。
    ///
    /// <b>なぜ「記録するだけ」なのか</b><br/>
    /// UdonSharp は インターフェース を扱えず、任意の C# クラスのメソッドも呼べません
    /// (Udon から呼べるのは許可された Unity API と、他の UdonSharpBehaviour だけ)。
    /// そのため、この中継は <b>イベントを起きた順にリングバッファへ書き込むだけ</b>にし、
    /// C# 側の <c>UdonVideoEventPump</c> が <c>UdonBehaviour.GetProgramVariable</c> で
    /// それを読み出して <see cref="VideoEventBridge"/> へ流します。
    ///
    /// <b>これは「ポーリング」ではありません。</b>
    /// 推測(<c>IsPlaying</c> を見て終わったことにする)ではなく、
    /// VRChat が実際に発火したイベントをそのまま順序どおり運んでいます。
    ///
    /// <b>使い方</b>:VRCUnityVideoPlayer / VRCAVProVideoPlayer と<b>同じ GameObject</b>に
    /// このコンポーネントを付けるだけです。設定項目はありません。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Smart Media Platform/Udon VRC Video Event Relay")]
    public class UdonVRCVideoEventRelay : UdonSharpBehaviour
    {
        // イベント種別のコード。VideoEventKind と一致させること。
        public const int CodeReady = 1;
        public const int CodeStart = 2;
        public const int CodePlay = 3;
        public const int CodePause = 4;
        public const int CodeEnd = 5;
        public const int CodeLoop = 6;

        /// <summary>エラーはこの値に VideoError の値を足したコードで積む。</summary>
        public const int CodeErrorBase = 100;

        /// <summary>リングバッファの長さ。1 フレームにこれ以上のイベントは来ない想定。</summary>
        public const int Capacity = 32;

        // === C# 側(UdonVideoEventPump)が GetProgramVariable で読む変数 ===
        // 名前を変えると読み出し側が壊れるので注意。

        /// <summary>イベントコードのリングバッファ。</summary>
        public int[] EventCodes = new int[Capacity];

        /// <summary>
        /// これまでに書き込んだイベントの累計。
        /// 読み出し側はこの値の差分だけを消費するので、
        /// <b>取りこぼしも二重取得も起きません</b>(1 フレームに Capacity 件を超えなければ)。
        /// </summary>
        public int WriteCount;

        /// <summary>直近のエラーコード(診断用)。</summary>
        public int LastErrorCode = -1;

        // ───────── VRChat の動画イベント ─────────

        public override void OnVideoReady()
        {
            Push(CodeReady);
        }

        public override void OnVideoStart()
        {
            Push(CodeStart);
        }

        public override void OnVideoPlay()
        {
            Push(CodePlay);
        }

        public override void OnVideoPause()
        {
            Push(CodePause);
        }

        public override void OnVideoEnd()
        {
            Push(CodeEnd);
        }

        public override void OnVideoLoop()
        {
            Push(CodeLoop);
        }

        public override void OnVideoError(VideoError videoError)
        {
            LastErrorCode = (int)videoError;
            Push(CodeErrorBase + LastErrorCode);
        }

        // ───────── 内部 ─────────

        /// <summary>イベントを起きた順にリングバッファへ積む。</summary>
        private void Push(int code)
        {
            if (EventCodes == null || EventCodes.Length == 0)
            {
                EventCodes = new int[Capacity];
            }

            EventCodes[WriteCount % EventCodes.Length] = code;
            WriteCount++;
        }
    }
}
