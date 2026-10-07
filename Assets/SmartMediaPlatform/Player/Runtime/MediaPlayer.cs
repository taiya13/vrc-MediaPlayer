using System;
using System.Collections.Generic;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Queue;

namespace SmartMediaPlatform.Player
{
    /// <summary>
    /// 再生制御の統一 API。「実際に使える音楽プレイヤー」の操作面を受け持つ。
    ///
    /// 特徴:
    ///  - <b>純粋 C#(UnityEngine 非依存)</b>。AudioBackend でも将来の VideoBackend でも
    ///    まったく同じコードが動く。Backend を追加してもこの層をコピーする必要はない。
    ///  - <see cref="BackendManager"/> を経由して操作するだけで、BackendManager 自体は変更していない。
    ///  - Backend の種類を知らない。シークのように対応可否が分かれる機能は
    ///    <see cref="ISeekableBackend"/> の有無で判断する。
    ///  - 曲が終わったら(Ended)Queue の次の曲へ自動で進み、再生を続ける。
    ///  - 曲戻し用に再生履歴を持つ(Queue は前方向にしか進まないため)。
    ///
    /// すべての操作はログを出すので、Console だけで状態遷移を追える。
    /// </summary>
    public sealed class MediaPlayer : IBackendObserver
    {
        private readonly BackendManager _manager;
        private readonly List<MediaItem> _history = new List<MediaItem>();
        private IBackendLogger _logger;

        /// <summary>
        /// 曲が最後まで再生されたら自動で次へ進むか。既定 true
        /// (Phase2-2 の目的が「実際に使えるプレイヤー」であるため)。
        /// </summary>
        public bool AutoAdvanceOnEnded { get; set; } = true;

        /// <summary>曲戻しで遡れる曲数。</summary>
        public int HistoryCount => _history.Count;

        /// <summary>再生履歴として保持する上限。</summary>
        public int MaxHistory { get; set; } = 50;

        public MediaPlayer(BackendManager manager, IBackendLogger logger = null)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _logger = logger ?? NullBackendLogger.Instance;

            // 自動送りはこの層が受け持つ。
            // BackendManager 側の自動送りは「次を読み込むだけで再生は始めない」ため、
            // 二重に反応しないよう切ってから、こちらで再生まで面倒を見る。
            _manager.AutoAdvanceOnEnded = false;

            foreach (var backend in _manager.Backends) backend.AddObserver(this);
        }

        public BackendManager Manager => _manager;

        public IQueue Queue => _manager.Queue;

        /// <summary>ログ出力先を差し替える。</summary>
        public void SetLogger(IBackendLogger logger)
        {
            _logger = logger ?? NullBackendLogger.Instance;
        }

        /// <summary>
        /// バックエンドを登録する。<see cref="BackendManager.RegisterBackend"/> に加えて
        /// 通知の購読も行うので、Ended の自動送りが確実に働く。
        /// </summary>
        public void RegisterBackend(IMediaBackend backend)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));

            _manager.RegisterBackend(backend);
            backend.AddObserver(this);
        }

        // ───────── 再生制御 ─────────

        /// <summary>
        /// 再生する。何も読み込んでいなければ Queue の先頭を読み込んでから始める。
        /// 一時停止中に呼ばれた場合は再開として扱う(利用者の期待に沿うため)。
        /// </summary>
        public bool Play()
        {
            if (GetState() == BackendState.Paused)
            {
                Log("Play -> 一時停止中のため Resume として扱います");
                return Resume();
            }

            if (_manager.GetCurrent() == null && !_manager.LoadCurrent())
            {
                Log("Play 失敗: 読み込めるメディアがありません(Queue が空か、対応する Backend がありません)");
                return false;
            }

            return Operation("Play", () => _manager.Play());
        }

        public bool Pause()
        {
            return Operation("Pause", () => _manager.Pause());
        }

        public bool Resume()
        {
            return Operation("Resume", () => _manager.Resume());
        }

        public bool Stop()
        {
            return Operation("Stop", () => _manager.Stop());
        }

        /// <summary>再生中なら一時停止、一時停止中なら再開、それ以外なら再生を開始する。</summary>
        public bool TogglePlayPause()
        {
            switch (GetState())
            {
                case BackendState.Playing:
                    Log("TogglePlayPause -> Pause");
                    return Pause();
                case BackendState.Paused:
                    Log("TogglePlayPause -> Resume");
                    return Resume();
                default:
                    Log("TogglePlayPause -> Play");
                    return Play();
            }
        }

        /// <summary>
        /// 次の曲へ進む。Queue を 1 つ進め、直前まで再生していたなら続けて再生する。
        /// 進んだ曲は履歴に積まれ、<see cref="SkipPrevious"/> で戻れる。
        /// </summary>
        public bool SkipNext()
        {
            // Ended / Error 直後は状態が Playing ではないが、
            // 利用者の体感は「再生中だった」なので続けて鳴らす。
            //
            // Error を含めるのが要点です。1 本 URL が切れているだけで
            // 次を読み込んだまま鳴らさない(Ready で止まる)と、
            // 実機では「壊れた動画に当たると再生が終わる」ことになります。
            // 壊れているものを飛ばして続ける、が Phase3-4 で決めた復帰の形です。
            var before = GetState();
            bool shouldKeepPlaying = IsPlaying()
                                     || before == BackendState.Ended
                                     || before == BackendState.Error;

            var leaving = _manager.GetCurrent();
            string from = leaving != null ? leaving.Id : "(none)";

            // 次が無いなら何もしない。
            // BackendManager.Skip() は「次が無い」と分かる前に現在の曲と Queue を消費するため、
            // 先に確認しておかないと、最後の曲が終わったときに再生完了(Ended)の状態が失われ、
            // Queue も空になってしまう。
            if (_manager.Queue.Count <= 1)
            {
                Log($"SkipNext: {from} -> (次の曲がありません。State は {GetState()} のまま)");
                return false;
            }

            if (!_manager.Skip())
            {
                Log($"SkipNext: {from} -> (キューが尽きました)");
                return false;
            }

            PushHistory(leaving);

            // BackendManager.Skip() は「再生中だった」ときだけ続けて再生する。
            // Ended から来た場合はここで明示的に再生を始める。
            if (shouldKeepPlaying && GetState() != BackendState.Playing) _manager.Play();

            var arrived = _manager.GetCurrent();
            Log($"SkipNext: {from} -> {(arrived != null ? arrived.Id : "(none)")} (State: {GetState()})");
            return true;
        }

        /// <summary>
        /// 前の曲へ戻る。履歴から 1 つ取り出して Queue の先頭に戻し、読み込み直す。
        /// 直前まで再生していたなら続けて再生する。履歴が無ければ false。
        /// </summary>
        public bool SkipPrevious()
        {
            if (_history.Count == 0)
            {
                Log("SkipPrevious: 履歴がありません");
                return false;
            }

            bool shouldKeepPlaying = IsPlaying() || GetState() == BackendState.Ended;

            var previous = _history[_history.Count - 1];
            _history.RemoveAt(_history.Count - 1);

            var leaving = _manager.GetCurrent();
            string from = leaving != null ? leaving.Id : "(none)";

            // Queue の先頭(現在の Now)より前に差し込む。
            // EnqueueNext は Now の直後に入るので、そのあと先頭へ動かす。
            var queue = _manager.Queue;
            queue.EnqueueNext(previous);
            if (queue.Count > 1) queue.Move(1, 0);

            if (!_manager.LoadCurrent())
            {
                Log($"SkipPrevious 失敗: {previous.Id} を読み込めませんでした");
                return false;
            }

            if (shouldKeepPlaying) _manager.Play();

            Log($"SkipPrevious: {from} -> {previous.Id} (State: {GetState()}, 履歴残り: {_history.Count})");
            return true;
        }

        /// <summary>
        /// 再生位置を移動する。対応していないバックエンド(DummyBackend や生配信)では false。
        /// </summary>
        /// <param name="normalizedPosition">0.0(先頭)〜1.0(末尾)。範囲外は丸める。</param>
        public bool Seek(float normalizedPosition)
        {
            var seekable = GetSeekable();
            if (seekable == null || !seekable.CanSeek)
            {
                Log($"Seek 無視: 現在のバックエンドはシークに対応していません");
                return false;
            }

            float clamped = Clamp01(normalizedPosition);
            bool ok = seekable.Seek(clamped);

            Log($"Seek {clamped:0.00} -> {(ok ? "成功" : "失敗")} "
                + $"({GetCurrentTime():0.00}s / {GetDuration():0.00}s)");
            return ok;
        }

        // ───────── 問い合わせ ─────────

        /// <summary>現在の再生位置(秒)。分からなければ 0。</summary>
        public float GetCurrentTime()
        {
            var seekable = GetSeekable();
            return seekable != null ? seekable.GetCurrentTime() : 0f;
        }

        /// <summary>
        /// メディアの長さ(秒)。
        /// バックエンドが答えられない場合は Catalog のメタデータで補う。
        /// </summary>
        public float GetDuration()
        {
            var seekable = GetSeekable();
            if (seekable != null)
            {
                float duration = seekable.GetDuration();
                if (duration > 0f) return duration;
            }

            var current = _manager.GetCurrent();
            return current != null ? current.DurationSeconds : 0f;
        }

        /// <summary>再生の進み具合(0.0〜1.0)。長さが分からなければ 0。</summary>
        public float GetProgress()
        {
            float duration = GetDuration();
            if (duration <= 0f) return 0f;
            return Clamp01(GetCurrentTime() / duration);
        }

        public BackendState GetState() => _manager.GetState();

        public bool IsPlaying() => GetState() == BackendState.Playing;

        /// <summary>今シークできるか。</summary>
        public bool CanSeek()
        {
            var seekable = GetSeekable();
            return seekable != null && seekable.CanSeek;
        }

        /// <summary>現在のメディア。無ければ null。</summary>
        public MediaItem GetCurrent() => _manager.GetCurrent();

        /// <summary>Console 表示用の 1 行サマリ。</summary>
        public string Describe()
        {
            var current = GetCurrent();
            string title = current != null ? $"{current.Id} ({current.Title})" : "(none)";
            string time = CanSeek() || GetDuration() > 0f
                ? $" {GetCurrentTime():0.0}s/{GetDuration():0.0}s {GetProgress() * 100f:0}%"
                : "";
            return $"[{GetState()}] {title}{time}";
        }

        public void ClearHistory()
        {
            _history.Clear();
        }

        // ───────── Backend からの通知 ─────────

        public void OnBackendEvent(BackendEvent backendEvent)
        {
            if (backendEvent.Type != BackendEventType.Ended) return;
            if (!AutoAdvanceOnEnded) return;

            Log($"Ended を検知: {(backendEvent.Item != null ? backendEvent.Item.Id : "(none)")} -> 次の曲へ");
            SkipNext();
        }

        // ───────── 内部 ─────────

        private ISeekableBackend GetSeekable()
        {
            return _manager.ActiveBackend as ISeekableBackend;
        }

        private void PushHistory(MediaItem item)
        {
            if (item == null) return;

            _history.Add(item);
            while (_history.Count > MaxHistory && _history.Count > 0) _history.RemoveAt(0);
        }

        /// <summary>操作の前後の状態をログに残しつつ実行する。</summary>
        private bool Operation(string label, Func<bool> action)
        {
            var before = GetState();
            bool ok = action();
            var after = GetState();

            var current = GetCurrent();
            string id = current != null ? current.Id : "(none)";
            Log($"{label} {id}: {before} -> {after} {(ok ? "" : "(拒否されました)")}".TrimEnd());
            return ok;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private void Log(string message)
        {
            _logger.Log($"[MediaPlayer] {message}");
        }
    }
}
