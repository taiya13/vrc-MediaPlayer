using System;
using System.Collections.Generic;
using SmartMediaPlatform.Backend;

namespace SmartMediaPlatform.Video
{
    /// <summary>1 件のイベント受理/棄却の記録(Console と診断用)。</summary>
    public struct VideoEventRecord
    {
        public VideoEventKind Kind;

        /// <summary>受理して Backend の状態が変わったか。</summary>
        public bool Accepted;

        /// <summary>イベント到着前の Backend の状態。</summary>
        public VideoPlayerState Before;

        /// <summary>イベント処理後の Backend の状態。</summary>
        public VideoPlayerState After;

        /// <summary>棄却した理由(受理した場合は null)。</summary>
        public string RejectReason;

        public override string ToString()
        {
            return Accepted
                ? $"{Kind,-6} 受理  {Before} -> {After}"
                : $"{Kind,-6} 棄却  {Before}(理由: {RejectReason})";
        }
    }

    /// <summary>
    /// <b>Phase3-2 の中心。VRChat の動画イベントを Backend へ届ける唯一の窓口。</b>
    ///
    /// Phase3-1 では <see cref="VRChatVideoBackend.Tick"/> のポーリングで
    /// 読み込み完了と再生終了を推測していました。Phase3-2 では
    /// <b>実機が実際に発火したイベントをそのまま運ぶ</b>経路を正式に用意し、
    /// ポーリングは保険に格下げします。
    ///
    /// <b>このクラスが引き受ける 3 つの仕事</b>
    /// <list type="number">
    /// <item>
    /// <b>重複排除</b> — 同じ出来事が二度届いても、上位へは 1 回しか流さない。
    /// 判定そのものは <see cref="VRChatVideoBackend"/> の状態遷移が持っているので、
    /// ここでは「受理されたか」を受け取って集計・記録します。
    /// </item>
    /// <item>
    /// <b><see cref="VRChatVideoBackend.Tick"/> との調停</b> —
    /// イベントが 1 つでも届いたら「この構成ではイベントが来る」と判断し、
    /// ポーリングによる<b>再生終了の推測を止めます</b>
    /// (<see cref="VRChatVideoBackend.DetectEndByPolling"/> を false にする)。
    /// 読み込みタイムアウトの監視だけは、イベントすら来ない場合の保険として残します。
    /// </item>
    /// <item>
    /// <b>証跡</b> — 何を受理し何を棄却したかを数え、直近の履歴を残します。
    /// 「二重発火しない」ことは、この集計を見れば Console で確認できます。
    /// </item>
    /// </list>
    ///
    /// <b>依存していないもの</b>:UnityEngine・VRChat SDK・PlayerSession・Queue。
    /// 純粋 C# なので EditMode テストで完全に検証できます。
    /// SDK 側(<c>UdonVideoEventPump</c> / <c>VRChatVideoBackendHost</c>)は
    /// <see cref="IVideoEventSink"/> にイベントを流し込むだけです。
    /// </summary>
    public sealed class VideoEventBridge : IVideoEventSink
    {
        private const int KindCount = 8;

        private readonly VRChatVideoBackend _backend;
        private readonly IBackendLogger _logger;

        private readonly int[] _accepted = new int[KindCount];
        private readonly int[] _rejected = new int[KindCount];
        private readonly List<VideoEventRecord> _log = new List<VideoEventRecord>();

        private int _generation = -1;

        public VideoEventBridge(VRChatVideoBackend backend, IBackendLogger logger = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _logger = logger ?? NullBackendLogger.Instance;
        }

        /// <summary>イベントを届ける先のバックエンド。</summary>
        public VRChatVideoBackend Backend => _backend;

        /// <summary>
        /// イベントが届いたら、ポーリングによる再生終了の推測を止めるか。
        /// false にすると Phase3-1 と同じ「両方動く」状態になります(通知は変わらず 1 回)。
        /// </summary>
        public bool SuppressPollingWhenEventsArrive { get; set; } = true;

        /// <summary>履歴に残す件数の上限。</summary>
        public int MaxLogEntries { get; set; } = 32;

        /// <summary>
        /// 実機のイベントが 1 度でも届いたか。
        /// true になった時点で、この構成は「イベント経由で動いている」と言えます。
        /// </summary>
        public bool EventsObserved { get; private set; }

        /// <summary>直近に受理したイベント。</summary>
        public VideoEventKind LastAccepted { get; private set; } = VideoEventKind.None;

        /// <summary>直近に届いたイベント(棄却したものを含む)。</summary>
        public VideoEventKind LastReceived { get; private set; } = VideoEventKind.None;

        /// <summary>受理/棄却の履歴(古い順)。</summary>
        public IReadOnlyList<VideoEventRecord> Log => _log;

        public int TotalAccepted { get; private set; }

        public int TotalRejected { get; private set; }

        /// <summary>その種類のイベントを受理した回数。</summary>
        public int AcceptedCount(VideoEventKind kind) => _accepted[(int)kind];

        /// <summary>その種類のイベントを棄却した回数(= 二重発火を防いだ回数)。</summary>
        public int RejectedCount(VideoEventKind kind) => _rejected[(int)kind];

        // ───────── イベントの受け口(VRChat のイベント名と同じ) ─────────

        public bool OnVideoReady() => Dispatch(VideoEventKind.Ready, () => _backend.NotifyVideoReady());

        public bool OnVideoStart() => Dispatch(VideoEventKind.Start, () => _backend.NotifyVideoStart());

        public bool OnVideoPlay() => Dispatch(VideoEventKind.Play, () => _backend.NotifyVideoPlay());

        public bool OnVideoPause() => Dispatch(VideoEventKind.Pause, () => _backend.NotifyVideoPause());

        public bool OnVideoEnd() => Dispatch(VideoEventKind.End, () => _backend.NotifyVideoEnd());

        public bool OnVideoLoop() => Dispatch(VideoEventKind.Loop, () => _backend.NotifyVideoLoop());

        public bool OnVideoError(VideoErrorKind kind, string message = null)
        {
            return Dispatch(VideoEventKind.Error, () => _backend.NotifyVideoError(kind, message));
        }

        // ───────── ポーリング(保険) ─────────

        /// <summary>
        /// 毎フレーム呼ぶ。ホストの <c>Update()</c> はこれだけを呼べばよい。
        ///
        /// イベントが届いている構成では、<see cref="VRChatVideoBackend.Tick"/> の
        /// <b>再生終了の推測だけを止めます</b>。
        /// 読み込みタイムアウトの監視は「イベントもエラーも来ない」場合の
        /// 最後の砦なので、常に残します。
        /// </summary>
        public void Tick(float deltaSeconds = 0f)
        {
            SyncGeneration();

            if (SuppressPollingWhenEventsArrive && EventsObserved && _backend.DetectEndByPolling)
            {
                _backend.DetectEndByPolling = false;
                Log("イベントが届いているので、ポーリングによる再生終了の推測を止めます");
            }

            _backend.Tick(deltaSeconds);
        }

        /// <summary>集計と履歴を消す(バックエンドの状態には触れない)。</summary>
        public void ResetDiagnostics()
        {
            Array.Clear(_accepted, 0, _accepted.Length);
            Array.Clear(_rejected, 0, _rejected.Length);
            _log.Clear();
            TotalAccepted = 0;
            TotalRejected = 0;
            LastAccepted = VideoEventKind.None;
            LastReceived = VideoEventKind.None;
        }

        /// <summary>Console 用のまとめ。</summary>
        public string Describe()
        {
            return $"events={(EventsObserved ? "live" : "none")} "
                   + $"accepted={TotalAccepted} rejected={TotalRejected} "
                   + $"(Ready {AcceptedCount(VideoEventKind.Ready)}/{RejectedCount(VideoEventKind.Ready)}, "
                   + $"Start {AcceptedCount(VideoEventKind.Start)}/{RejectedCount(VideoEventKind.Start)}, "
                   + $"End {AcceptedCount(VideoEventKind.End)}/{RejectedCount(VideoEventKind.End)}, "
                   + $"Error {AcceptedCount(VideoEventKind.Error)}/{RejectedCount(VideoEventKind.Error)}) "
                   + $"[受理/棄却]";
        }

        // ───────── 内部 ─────────

        private bool Dispatch(VideoEventKind kind, Func<bool> notify)
        {
            SyncGeneration();

            var before = _backend.GetState();
            LastReceived = kind;

            // 実機のイベントが届いた、という事実自体はここで確定する。
            // (棄却されたイベントも「イベントは来ている」証拠になる)
            EventsObserved = true;

            bool accepted = notify();
            var after = _backend.GetState();

            var record = new VideoEventRecord
            {
                Kind = kind,
                Accepted = accepted,
                Before = before,
                After = after,
                RejectReason = accepted ? null : DescribeRejection(kind, before),
            };

            if (accepted)
            {
                _accepted[(int)kind]++;
                TotalAccepted++;
                LastAccepted = kind;
            }
            else
            {
                _rejected[(int)kind]++;
                TotalRejected++;
                Log($"{kind} を無視しました({record.RejectReason})");
            }

            Append(record);
            return accepted;
        }

        /// <summary>
        /// 読み込みが切り替わったら、1 回の読み込みごとの記録をやり直す。
        /// 前の動画のイベントの集計が次の動画に混ざらないようにするため。
        /// </summary>
        private void SyncGeneration()
        {
            if (_backend.LoadGeneration == _generation) return;

            _generation = _backend.LoadGeneration;
            _log.Clear();
        }

        private static string DescribeRejection(VideoEventKind kind, VideoPlayerState before)
        {
            switch (kind)
            {
                case VideoEventKind.Ready:
                    return before == VideoPlayerState.Loading
                        ? "読み込み完了の反映に失敗"
                        : $"読み込み中ではありません({before})";
                case VideoEventKind.Start:
                case VideoEventKind.Play:
                    return before == VideoPlayerState.Playing
                        ? "すでに再生中です"
                        : $"再生を開始できる状態ではありません({before})";
                case VideoEventKind.Pause:
                    return $"再生中ではありません({before})";
                case VideoEventKind.End:
                    return before == VideoPlayerState.Finished
                        ? "すでに再生を終えています"
                        : $"再生中でも一時停止中でもありません({before})";
                case VideoEventKind.Loop:
                    return "ループは状態を変えません";
                case VideoEventKind.Error:
                    return "同じ失敗を報告済みです";
                default:
                    return "不明なイベントです";
            }
        }

        private void Append(VideoEventRecord record)
        {
            _log.Add(record);
            while (MaxLogEntries > 0 && _log.Count > MaxLogEntries) _log.RemoveAt(0);
        }

        private void Log(string message)
        {
            _logger.Log($"[VideoEventBridge] {message}");
        }

        public override string ToString() => Describe();
    }
}
