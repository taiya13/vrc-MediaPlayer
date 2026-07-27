using System;
using UnityEngine;
using VRC.Udon;

namespace SmartMediaPlatform.Video.VRChat
{
    /// <summary>
    /// <b>Udon 側の中継が受け取ったイベントを C# 側へ運ぶ運搬役。</b>
    ///
    /// VRChat の動画プレイヤーはイベントを<b>同じ GameObject の UdonBehaviour</b> へ送ります。
    /// C# の MonoBehaviour は直接受け取れず、UdonSharp は インターフェース を扱えないため、
    /// <c>UdonVRCVideoEventRelay</c>(UdonSharp)がイベントを int のリングバッファへ記録し、
    /// このクラスが <c>UdonBehaviour.GetProgramVariable</c> で読み出して
    /// <see cref="IVideoEventSink"/>(= <see cref="VideoEventBridge"/>)へ流します。
    ///
    /// <b>ポーリングとの違い</b><br/>
    /// 状態を見て推測する(<c>IsPlaying</c> が false だから終わったはず)のではなく、
    /// <b>VRChat が実際に発火したイベントを、起きた順にそのまま運びます。</b>
    /// 累計カウンタ(<c>WriteCount</c>)の差分だけを消費するので、
    /// 取りこぼしも二重取得も起きません。
    ///
    /// <b>判断ロジックはここに一切ありません。</b>
    /// 符号の解釈は <see cref="VideoEventCodec"/>(純粋 C#)、
    /// 重複排除と調停は <see cref="VideoEventBridge"/>(純粋 C#)にあります。
    /// このクラスは「読んで渡す」だけです。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UdonVideoEventPump : MonoBehaviour
    {
        /// <summary>Udon 側と一致させる変数名。</summary>
        private const string WriteCountVariable = "WriteCount";
        private const string EventCodesVariable = "EventCodes";

        [Header("接続先")]
        [Tooltip("イベントを届ける先。未設定なら同じ GameObject から探す")]
        [SerializeField] private VRChatVideoBackendHost _host;

        [Tooltip("UdonVRCVideoEventRelay の UdonBehaviour。未設定なら同じ GameObject から探す")]
        [SerializeField] private UdonBehaviour _relay;

        [Header("診断")]
        [Tooltip("運んだイベントをすべて Console に出す")]
        [SerializeField] private bool _verbose;

        private int _consumed;
        private bool _warned;

        /// <summary>これまでに運んだイベントの件数。</summary>
        public int DeliveredCount { get; private set; }

        /// <summary>取りこぼした件数(1 フレームに 32 件を超えた場合のみ発生)。</summary>
        public int DroppedCount { get; private set; }

        /// <summary>中継の UdonBehaviour を見つけられているか。</summary>
        public bool IsConnected => _relay != null;

        private void Awake()
        {
            if (_host == null) _host = GetComponent<VRChatVideoBackendHost>();
            if (_relay == null) _relay = FindRelay();
        }

        private void Update()
        {
            Drain();
        }

        /// <summary>
        /// 中継に溜まったイベントを、起きた順にすべて <see cref="VideoEventBridge"/> へ流す。
        /// </summary>
        /// <returns>今回流した件数。</returns>
        public int Drain()
        {
            var sink = _host != null ? _host.Bridge : null;
            if (sink == null || _relay == null)
            {
                WarnOnce();
                return 0;
            }

            if (!TryReadInt(_relay, WriteCountVariable, out int writeCount)) return 0;
            if (writeCount == _consumed) return 0;

            var codes = _relay.GetProgramVariable(EventCodesVariable) as int[];
            if (codes == null || codes.Length == 0) return 0;

            // 1 フレームにバッファ長を超えるイベントが来た場合は、
            // 古い方が上書きされている。取りこぼしとして数え、読める分だけ運ぶ。
            int available = writeCount - _consumed;
            if (available > codes.Length)
            {
                DroppedCount += available - codes.Length;
                _consumed = writeCount - codes.Length;
            }

            int delivered = 0;
            while (_consumed < writeCount)
            {
                int code = codes[_consumed % codes.Length];
                bool accepted = VideoEventCodec.Deliver(sink, code);

                _consumed++;
                delivered++;
                DeliveredCount++;

                if (_verbose)
                {
                    VideoEventCodec.TryDecode(code, out var kind, out var error);
                    Debug.Log($"[{name}] Udon イベントを中継: {kind}"
                              + (kind == VideoEventKind.Error ? $"({error})" : "")
                              + $" -> {(accepted ? "受理" : "棄却(重複)")}");
                }
            }

            return delivered;
        }

        /// <summary>
        /// 中継の UdonBehaviour を同じ GameObject から探す。
        /// 期待する変数を持つものを選ぶので、他の Udon が同居していても誤爆しない。
        /// </summary>
        private UdonBehaviour FindRelay()
        {
            var candidates = GetComponents<UdonBehaviour>();
            for (int i = 0; i < candidates.Length; i++)
            {
                if (TryReadInt(candidates[i], WriteCountVariable, out _)) return candidates[i];
            }
            return null;
        }

        /// <summary>
        /// Udon の変数を読む。存在しない変数名では実装によって例外が出るため、
        /// 探索にも使えるよう握りつぶして false を返す。
        /// </summary>
        private static bool TryReadInt(UdonBehaviour behaviour, string name, out int value)
        {
            value = 0;
            if (behaviour == null) return false;

            try
            {
                object raw = behaviour.GetProgramVariable(name);
                if (raw is int number)
                {
                    value = number;
                    return true;
                }
            }
            catch (Exception)
            {
                // その UdonBehaviour は中継ではない、というだけ。
            }
            return false;
        }

        private void WarnOnce()
        {
            if (_warned) return;
            _warned = true;

            Debug.LogWarning(
                $"[{name}] Udon の中継が見つかりません。"
                + "UdonVRCVideoEventRelay を動画プレイヤーと同じ GameObject に付けてください。\n"
                + "  それまでは VideoEventBridge のポーリング(保険)で動作します。");
        }
    }
}
