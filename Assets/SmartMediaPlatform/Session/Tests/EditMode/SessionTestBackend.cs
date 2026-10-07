using System.Collections.Generic;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Session.Tests
{
    /// <summary>
    /// 検証用のバックエンド。実際には再生せず、
    /// <see cref="SimulateEnded"/> で「曲が最後まで再生された」状態を作れる。
    ///
    /// UnityEngine に依存しないので、PlayerSession の連携を
    /// EditMode で決定的に検証できる。
    /// </summary>
    public sealed class SessionTestBackend : IMediaBackend, ISeekableBackend
    {
        private readonly List<IBackendObserver> _observers = new List<IBackendObserver>();
        private readonly MediaType[] _supportedTypes;

        private MediaItem _current;
        private BackendState _state = BackendState.Idle;
        private float _time;

        public string Name { get; }

        public float Duration { get; set; } = 100f;

        /// <summary>Play が呼ばれた回数(Repeat One の検証に使う)。</summary>
        public int PlayCallCount { get; private set; }

        public SessionTestBackend(string name = "SessionTestBackend", params MediaType[] supportedTypes)
        {
            Name = name;
            _supportedTypes = supportedTypes != null && supportedTypes.Length > 0
                ? supportedTypes
                : new[] { MediaType.Music };
        }

        public bool CanPlay(MediaItem item)
        {
            if (item == null) return false;
            foreach (var type in _supportedTypes)
            {
                if (type == item.Type) return true;
            }
            return false;
        }

        public MediaItem GetCurrent() => _current;
        public BackendState GetState() => _state;

        public bool Load(MediaItem item)
        {
            if (item == null || !CanPlay(item)) return Reject(item);

            var previous = _state;
            _current = item;
            _time = 0f;
            _state = BackendState.Ready;
            Emit(BackendEventType.Loaded, item, previous);
            return true;
        }

        public bool Play()
        {
            if (_current == null || _state == BackendState.Playing || _state == BackendState.Paused)
                return Reject(_current);

            var previous = _state;
            _time = 0f;
            _state = BackendState.Playing;
            PlayCallCount++;
            Emit(BackendEventType.Started, _current, previous);
            return true;
        }

        public bool Pause()
        {
            if (_state != BackendState.Playing) return Reject(_current);

            var previous = _state;
            _state = BackendState.Paused;
            Emit(BackendEventType.Paused, _current, previous);
            return true;
        }

        public bool Resume()
        {
            if (_state != BackendState.Paused) return Reject(_current);

            var previous = _state;
            _state = BackendState.Playing;
            Emit(BackendEventType.Resumed, _current, previous);
            return true;
        }

        public bool Stop()
        {
            if (_current == null || _state == BackendState.Stopped) return Reject(_current);

            var previous = _state;
            _time = 0f;
            _state = BackendState.Stopped;
            Emit(BackendEventType.Stopped, _current, previous);
            return true;
        }

        public bool Skip()
        {
            if (_current == null) return Reject(null);

            var previous = _state;
            _time = 0f;
            _state = BackendState.Stopped;
            Emit(BackendEventType.Skipped, _current, previous);
            return true;
        }

        /// <summary>曲が最後まで再生された状態にする。</summary>
        public bool SimulateEnded()
        {
            if (_state != BackendState.Playing && _state != BackendState.Paused) return false;

            var previous = _state;
            _time = Duration;
            _state = BackendState.Ended;
            Emit(BackendEventType.Ended, _current, previous);
            return true;
        }

        public void Advance(float seconds)
        {
            if (_state == BackendState.Playing) _time += seconds;
        }

        // --- ISeekableBackend ---

        public bool CanSeek => _current != null && Duration > 0f;

        public float GetCurrentTime() => _time;

        public float GetDuration() => _current != null ? Duration : 0f;

        public bool Seek(float normalizedPosition)
        {
            if (!CanSeek) return false;

            float clamped = normalizedPosition < 0f ? 0f
                : normalizedPosition > 1f ? 1f : normalizedPosition;
            _time = clamped * Duration;
            return true;
        }

        // --- 通知 ---

        public void AddObserver(IBackendObserver observer)
        {
            if (observer != null && !_observers.Contains(observer)) _observers.Add(observer);
        }

        public void RemoveObserver(IBackendObserver observer)
        {
            if (observer != null) _observers.Remove(observer);
        }

        private void Emit(BackendEventType type, MediaItem item, BackendState previous)
        {
            var backendEvent = new BackendEvent(type, item, previous, _state, Name);
            foreach (var observer in _observers.ToArray()) observer.OnBackendEvent(backendEvent);
        }

        private bool Reject(MediaItem item)
        {
            Emit(BackendEventType.Error, item, _state);
            return false;
        }
    }
}
