using System;
using System.Collections.Generic;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Backend
{
    /// <summary>
    /// 再生を行わないテスト用バックエンド。
    ///
    /// 音も映像も出さず、状態遷移とログ出力だけを行う。
    /// AudioSource / VideoPlayer / VRCVideoPlayer は一切使わない(Phase1-5 の禁止事項)。
    ///
    /// それでも状態機械と通知の流れは実機バックエンドと完全に同じなので、
    /// Phase2 で実機実装に差し替えても上位コード(BackendManager / Player)は無変更で動く。
    /// </summary>
    public sealed class DummyBackend : IMediaBackend
    {
        private readonly List<IBackendObserver> _observers = new List<IBackendObserver>();
        private readonly IBackendLogger _logger;
        private readonly MediaType[] _supportedTypes;

        private MediaItem _current;
        private BackendState _state = BackendState.Idle;

        public string Name { get; }

        /// <param name="name">バックエンド名(ログに出る)。</param>
        /// <param name="logger">ログ出力先。null なら出力しない。</param>
        /// <param name="supportedTypes">
        /// 扱えるメディア種別。null または空なら全種別を扱う。
        /// Music 用 / Video 用にバックエンドを分ける検証に使う。
        /// </param>
        public DummyBackend(
            string name = "DummyBackend",
            IBackendLogger logger = null,
            params MediaType[] supportedTypes)
        {
            Name = string.IsNullOrEmpty(name) ? "DummyBackend" : name;
            _logger = logger ?? NullBackendLogger.Instance;
            _supportedTypes = supportedTypes != null && supportedTypes.Length > 0
                ? (MediaType[])supportedTypes.Clone()
                : null;
        }

        // --- 問い合わせ ---

        public bool CanPlay(MediaItem item)
        {
            if (item == null) return false;
            if (_supportedTypes == null) return true;

            for (int i = 0; i < _supportedTypes.Length; i++)
            {
                if (_supportedTypes[i] == item.Type) return true;
            }
            return false;
        }

        public MediaItem GetCurrent()
        {
            return _current;
        }

        public BackendState GetState()
        {
            return _state;
        }

        // --- 操作 ---

        public bool Load(MediaItem item)
        {
            if (item == null)
                return Reject(BackendEventType.Error, null, "Load failed: item is null");

            if (!CanPlay(item))
                return Reject(BackendEventType.Error, item,
                    $"Load failed: {Name} cannot play {item.Type}");

            var previous = _state;
            _current = item;
            _state = BackendState.Ready;

            // 実機ならここでメディアを開く。DummyBackend はログのみ。
            Log($"Load {item.Id}  ({item.Title} / {item.Artist}, {item.Type})");
            Emit(BackendEventType.Loaded, item, previous);
            return true;
        }

        public bool Play()
        {
            if (_current == null)
                return Reject(BackendEventType.Error, null, "Play failed: nothing loaded");

            if (_state == BackendState.Playing)
                return Reject(BackendEventType.Error, _current, "Play ignored: already playing");

            if (_state == BackendState.Paused)
                return Reject(BackendEventType.Error, _current, "Play ignored: paused (use Resume)");

            var previous = _state;
            _state = BackendState.Playing;

            // ここが「実際には音を鳴らさず、ログだけ出力する」箇所。
            Log($"Play {_current.Id}  (再生はしません / no actual playback)");
            Emit(BackendEventType.Started, _current, previous);
            return true;
        }

        public bool Pause()
        {
            if (_state != BackendState.Playing)
                return Reject(BackendEventType.Error, _current, "Pause ignored: not playing");

            var previous = _state;
            _state = BackendState.Paused;

            Log($"Pause {_current.Id}");
            Emit(BackendEventType.Paused, _current, previous);
            return true;
        }

        public bool Resume()
        {
            if (_state != BackendState.Paused)
                return Reject(BackendEventType.Error, _current, "Resume ignored: not paused");

            var previous = _state;
            _state = BackendState.Playing;

            Log($"Resume {_current.Id}");
            Emit(BackendEventType.Resumed, _current, previous);
            return true;
        }

        public bool Stop()
        {
            if (_current == null)
                return Reject(BackendEventType.Error, null, "Stop ignored: nothing loaded");

            if (_state == BackendState.Stopped)
                return Reject(BackendEventType.Error, _current, "Stop ignored: already stopped");

            var previous = _state;
            _state = BackendState.Stopped;

            Log($"Stop {_current.Id}");
            Emit(BackendEventType.Stopped, _current, previous);
            return true;
        }

        public bool Skip()
        {
            if (_current == null)
                return Reject(BackendEventType.Error, null, "Skip ignored: nothing loaded");

            var previous = _state;
            var skipped = _current;
            _state = BackendState.Stopped;

            Log($"Skip {skipped.Id}");
            Emit(BackendEventType.Skipped, skipped, previous);
            return true;
        }

        /// <summary>
        /// 「最後まで再生し終えた」ことを擬似的に起こす。
        ///
        /// DummyBackend は実際には再生しないため自然終了が発生しない。
        /// 実機バックエンドでは再生完了時にこれと同じ通知(<see cref="BackendEventType.Ended"/>)が
        /// 自動で飛ぶので、上位の自動送り配線を今のうちに検証できる。
        /// <see cref="IMediaBackend"/> には含めない(実機では外から呼ぶものではないため)。
        /// </summary>
        public bool SimulateEnded()
        {
            if (_current == null)
                return Reject(BackendEventType.Error, null, "Ended ignored: nothing loaded");

            if (_state != BackendState.Playing && _state != BackendState.Paused)
                return Reject(BackendEventType.Error, _current, "Ended ignored: not playing");

            var previous = _state;
            _state = BackendState.Ended;

            Log($"Ended {_current.Id}");
            Emit(BackendEventType.Ended, _current, previous);
            return true;
        }

        // --- 通知 ---

        public void AddObserver(IBackendObserver observer)
        {
            if (observer == null || _observers.Contains(observer)) return;
            _observers.Add(observer);
        }

        public void RemoveObserver(IBackendObserver observer)
        {
            if (observer == null) return;
            _observers.Remove(observer);
        }

        private void Emit(BackendEventType type, MediaItem item, BackendState previous)
        {
            Notify(new BackendEvent(type, item, previous, _state, Name));
        }

        /// <summary>
        /// 不正な操作を「状態を変えずに」拒否する。
        /// 例外を投げないので、上位は状態を気にせず呼べる。
        /// </summary>
        private bool Reject(BackendEventType type, MediaItem item, string message)
        {
            Log(message);
            Notify(new BackendEvent(type, item, _state, _state, Name, message));
            return false;
        }

        private void Notify(BackendEvent backendEvent)
        {
            // 通知中に観測者が増減しても壊れないよう、コピーを回す。
            var snapshot = _observers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].OnBackendEvent(backendEvent);
            }
        }

        private void Log(string message)
        {
            _logger.Log($"[{Name}] {message}");
        }
    }
}
