using System;
using System.Reflection;
using UnityEngine;

namespace SmartMediaPlatform.Video.VRChat
{
    /// <summary>
    /// <b>Udon 側の中継が受け取ったイベントを C# 側へ運ぶ運搬役。</b>
    ///
    /// VRChat の動画プレイヤーはイベントを<b>同じ GameObject の Udon</b> へ送ります。
    /// C# の MonoBehaviour は直接受け取れず、UdonSharp は インターフェース を扱えないため、
    /// <c>UdonVRCVideoEventRelay</c>(UdonSharp)がイベントを int のリングバッファへ記録し、
    /// このクラスがそれを読み出して
    /// <see cref="IVideoEventSink"/>(= <see cref="VideoEventBridge"/>)へ流します。
    ///
    /// <b>VRChat SDK の型に依存していません。</b>
    /// 中継は <see cref="MonoBehaviour"/> として受け取り、値の読み出しはリフレクションで行います。
    /// 理由は 2 つ:
    /// <list type="number">
    /// <item>
    /// <c>VRC.Udon.UdonBehaviour</c> を型で参照すると、この asmdef から
    /// Udon アセンブリを参照できる構成でないとコンパイルが通らない。
    /// SDK の配布形態(DLL / asmdef / Auto Reference の有無)はバージョンで変わるため、
    /// <b>型依存を持たないほうが壊れにくい</b>。
    /// </item>
    /// <item>
    /// 中継の実体は環境によって <c>UdonBehaviour</c>(Udon プログラム)だったり
    /// <c>UdonSharpBehaviour</c> のプロキシだったりする。
    /// <b>どちらでも読めるように</b>、2 つの経路を試す。
    /// </item>
    /// </list>
    /// リフレクションで SDK のバージョン差を吸収するのは、Phase1-2 の
    /// <c>UdonCatalogBaker.SyncUdonSharpProxy</c> と同じ考え方です。
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

        [Tooltip("UdonVRCVideoEventRelay(UdonBehaviour でもプロキシでも可)。未設定なら同じ GameObject から探す")]
        [SerializeField] private MonoBehaviour _relay;

        [Header("診断")]
        [Tooltip("運んだイベントをすべて Console に出す")]
        [SerializeField] private bool _verbose;

        // 読み出し経路(どちらか一方が使われる)
        private MethodInfo _getProgramVariable;   // UdonBehaviour.GetProgramVariable(string)
        private FieldInfo _writeCountField;       // UdonSharpBehaviour のプロキシの public フィールド
        private FieldInfo _eventCodesField;

        private bool _resolved;
        private int _consumed;
        private bool _warned;

        /// <summary>これまでに運んだイベントの件数。</summary>
        public int DeliveredCount { get; private set; }

        /// <summary>取りこぼした件数(1 フレームにバッファ長を超えた場合のみ発生)。</summary>
        public int DroppedCount { get; private set; }

        /// <summary>中継を見つけて読み出せる状態か。</summary>
        public bool IsConnected =>
            _relay != null && (_getProgramVariable != null || _writeCountField != null);

        /// <summary>どの経路で読んでいるか(診断用)。</summary>
        public string ConnectionDescription
        {
            get
            {
                if (_relay == null) return "未接続(中継が見つかりません)";
                if (_getProgramVariable != null) return $"{_relay.GetType().Name} / GetProgramVariable";
                if (_writeCountField != null) return $"{_relay.GetType().Name} / フィールド直読み";
                return $"{_relay.GetType().Name}(読み出し方法が見つかりません)";
            }
        }

        private void Awake()
        {
            if (_host == null) _host = GetComponent<VRChatVideoBackendHost>();
            Resolve();
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
            if (sink == null) return 0;

            if (!_resolved) Resolve();
            if (!IsConnected)
            {
                WarnOnce();
                return 0;
            }

            if (!TryReadWriteCount(out int writeCount)) return 0;

            // シーンの再読み込みなどでカウンタが巻き戻った場合は追従する
            if (writeCount < _consumed) _consumed = 0;
            if (writeCount == _consumed) return 0;

            int[] codes = ReadEventCodes();
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

        // ───────── 中継の解決(SDK の型に依存しない) ─────────

        /// <summary>
        /// 中継と、その読み出し方法を決める。
        /// 期待する変数(<see cref="WriteCountVariable"/>)を持つものだけを選ぶので、
        /// 他の Udon が同居していても誤爆しません。
        /// </summary>
        private void Resolve()
        {
            _resolved = true;

            if (_relay != null && TryBind(_relay)) return;

            var candidates = GetComponents<MonoBehaviour>();
            for (int i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (candidate == null || ReferenceEquals(candidate, this)) continue;
                if (ReferenceEquals(candidate, _host)) continue;

                if (TryBind(candidate))
                {
                    _relay = candidate;
                    return;
                }
            }

            _getProgramVariable = null;
            _writeCountField = null;
            _eventCodesField = null;
        }

        /// <summary>
        /// その コンポーネント から <c>WriteCount</c> / <c>EventCodes</c> を読めるか調べ、
        /// 読めるなら読み出し方法を覚える。
        /// </summary>
        private bool TryBind(MonoBehaviour candidate)
        {
            if (candidate == null) return false;

            var type = candidate.GetType();

            // 経路 1: UdonBehaviour.GetProgramVariable(string)
            //   型で参照せず、メソッド名と引数で探す。
            var method = type.GetMethod(
                "GetProgramVariable",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(string) },
                null);

            if (method != null && method.ReturnType == typeof(object))
            {
                try
                {
                    object raw = method.Invoke(candidate, new object[] { WriteCountVariable });
                    if (raw is int)
                    {
                        _getProgramVariable = method;
                        _writeCountField = null;
                        _eventCodesField = null;
                        return true;
                    }
                }
                catch (Exception)
                {
                    // その コンポーネント は中継ではない、というだけ。
                }
            }

            // 経路 2: UdonSharpBehaviour のプロキシ(public フィールドを直接読む)
            var countField = type.GetField(
                WriteCountVariable, BindingFlags.Public | BindingFlags.Instance);
            var codesField = type.GetField(
                EventCodesVariable, BindingFlags.Public | BindingFlags.Instance);

            if (countField != null && countField.FieldType == typeof(int)
                && codesField != null && codesField.FieldType == typeof(int[]))
            {
                _getProgramVariable = null;
                _writeCountField = countField;
                _eventCodesField = codesField;
                return true;
            }

            return false;
        }

        private bool TryReadWriteCount(out int value)
        {
            value = 0;
            try
            {
                object raw = _getProgramVariable != null
                    ? _getProgramVariable.Invoke(_relay, new object[] { WriteCountVariable })
                    : _writeCountField.GetValue(_relay);

                if (raw is int number)
                {
                    value = number;
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{name}] 中継の {WriteCountVariable} を読めませんでした: {e.Message}");
                _resolved = false;   // 次のフレームで探し直す
            }
            return false;
        }

        private int[] ReadEventCodes()
        {
            try
            {
                object raw = _getProgramVariable != null
                    ? _getProgramVariable.Invoke(_relay, new object[] { EventCodesVariable })
                    : _eventCodesField.GetValue(_relay);

                return raw as int[];
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{name}] 中継の {EventCodesVariable} を読めませんでした: {e.Message}");
                _resolved = false;
                return null;
            }
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
