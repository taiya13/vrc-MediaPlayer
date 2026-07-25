using System;
using System.Collections.Generic;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using UnityEngine;

namespace SmartMediaPlatform.Audio
{
    /// <summary>
    /// AudioSource を使って実際に音を鳴らす Backend。
    ///
    /// Phase1-5 の <see cref="IMediaBackend"/> を実装しているだけなので、
    /// <see cref="BackendManager"/> は一切変更せずにそのまま使える
    /// (DummyBackend と登録を差し替えるだけで切り替わる)。
    ///
    /// 状態遷移・bool 返却・不正操作では状態を変えない、という Phase1-5 の契約を守る。
    /// 曲が最後まで再生されたら <see cref="BackendEventType.Ended"/> を通知する。
    ///
    /// 音を出す部分は <see cref="IAudioPlayer"/> に委ねているため、
    /// テストでは偽の実装に差し替えて決定的に検証できる。
    /// </summary>
    public sealed class AudioBackend : IMediaBackend
    {
        private readonly List<IBackendObserver> _observers = new List<IBackendObserver>();
        private readonly IAudioPlayer _player;
        private readonly AudioClipLibrary _library;
        private IBackendLogger _logger;
        private readonly MediaType[] _supportedTypes;

        private MediaItem _current;
        private AudioClip _currentClip;
        private BackendState _state = BackendState.Idle;

        /// <summary>
        /// クリップが登録されていないメディアも「扱える」と答えるか。
        ///
        /// 既定は false。クリップが無ければ音を出せないので正直に false を返し、
        /// <see cref="BackendManager"/> に別のバックエンドを探させる。
        /// </summary>
        public bool AllowMissingClip { get; set; }

        public string Name { get; }

        /// <summary>
        /// ログ出力先を後から差し替える。
        ///
        /// <see cref="AudioBackendHost.Awake"/> はコンポーネントの初期化順序上、
        /// ロガーが用意される前にバックエンドを組み立てざるを得ない。
        /// そのため一旦 <see cref="NullBackendLogger"/> で構築し、
        /// 実際のロガーが用意できた時点でこれを呼んで差し替える。
        /// </summary>
        public void SetLogger(IBackendLogger logger)
        {
            _logger = logger ?? NullBackendLogger.Instance;
        }

        /// <param name="name">バックエンド名(ログに出る)。</param>
        /// <param name="player">音を鳴らす手段。</param>
        /// <param name="library">MediaItem と AudioClip の対応表。</param>
        /// <param name="logger">ログ出力先。null なら出力しない。</param>
        /// <param name="supportedTypes">
        /// 扱えるメディア種別。未指定なら <see cref="MediaType.Music"/> のみ。
        /// </param>
        public AudioBackend(
            string name,
            IAudioPlayer player,
            AudioClipLibrary library,
            IBackendLogger logger = null,
            params MediaType[] supportedTypes)
        {
            Name = string.IsNullOrEmpty(name) ? "AudioBackend" : name;
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _logger = logger ?? NullBackendLogger.Instance;
            _supportedTypes = supportedTypes != null && supportedTypes.Length > 0
                ? (MediaType[])supportedTypes.Clone()
                : new[] { MediaType.Music };
        }

        // --- 問い合わせ ---

        public bool CanPlay(MediaItem item)
        {
            if (item == null) return false;

            bool typeSupported = false;
            for (int i = 0; i < _supportedTypes.Length; i++)
            {
                if (_supportedTypes[i] == item.Type) { typeSupported = true; break; }
            }
            if (!typeSupported) return false;

            // 音源が無ければ鳴らせない。BackendManager に別の候補を探させる。
            return AllowMissingClip || _library.Contains(item);
        }

        public MediaItem GetCurrent() => _current;

        public BackendState GetState() => _state;

        /// <summary>現在再生している AudioClip。無ければ null。</summary>
        public AudioClip GetCurrentClip() => _currentClip;

        // --- 操作 ---

        public bool Load(MediaItem item)
        {
            if (item == null)
                return Reject(null, "Load failed: item is null");

            if (!CanPlay(item))
                return Reject(item, $"Load failed: {Name} cannot play {item.Id} ({item.Type})");

            var clip = _library.Get(item);
            if (clip == null && !AllowMissingClip)
                return Reject(item, $"Load failed: no AudioClip registered for {item.Id}");

            // 別の曲を鳴らしている途中なら止めてから差し替える。
            if (_state == BackendState.Playing || _state == BackendState.Paused)
            {
                _player.Stop();
            }

            var previous = _state;
            _current = item;
            _currentClip = clip;
            _state = BackendState.Ready;

            Log($"Load {item.Id}  ({item.Title} / {item.Artist})"
                + (clip != null ? $"  clip: {clip.name}" : "  clip: (none)"));
            Emit(BackendEventType.Loaded, item, previous);
            return true;
        }

        public bool Play()
        {
            if (_current == null)
                return Reject(null, "Play failed: nothing loaded");

            if (_state == BackendState.Playing)
                return Reject(_current, "Play ignored: already playing");

            if (_state == BackendState.Paused)
                return Reject(_current, "Play ignored: paused (use Resume)");

            if (_currentClip == null)
                return Reject(_current, $"Play failed: no AudioClip for {_current.Id}");

            var previous = _state;
            _state = BackendState.Playing;

            _player.Play(_currentClip);

            Log($"Play {_current.Id}  (AudioSource 再生開始 / clip: {_currentClip.name})");
            Emit(BackendEventType.Started, _current, previous);
            return true;
        }

        public bool Pause()
        {
            if (_state != BackendState.Playing)
                return Reject(_current, "Pause ignored: not playing");

            var previous = _state;
            _state = BackendState.Paused;

            _player.Pause();

            Log($"Pause {_current.Id}");
            Emit(BackendEventType.Paused, _current, previous);
            return true;
        }

        public bool Resume()
        {
            if (_state != BackendState.Paused)
                return Reject(_current, "Resume ignored: not paused");

            var previous = _state;
            _state = BackendState.Playing;

            _player.UnPause();

            Log($"Resume {_current.Id}");
            Emit(BackendEventType.Resumed, _current, previous);
            return true;
        }

        public bool Stop()
        {
            if (_current == null)
                return Reject(null, "Stop ignored: nothing loaded");

            if (_state == BackendState.Stopped)
                return Reject(_current, "Stop ignored: already stopped");

            var previous = _state;
            _state = BackendState.Stopped;

            _player.Stop();

            Log($"Stop {_current.Id}");
            Emit(BackendEventType.Stopped, _current, previous);
            return true;
        }

        public bool Skip()
        {
            if (_current == null)
                return Reject(null, "Skip ignored: nothing loaded");

            var previous = _state;
            var skipped = _current;
            _state = BackendState.Stopped;

            _player.Stop();

            Log($"Skip {skipped.Id}");
            Emit(BackendEventType.Skipped, skipped, previous);
            return true;
        }

        /// <summary>
        /// 再生の進行を監視し、曲が最後まで終わっていたら
        /// <see cref="BackendEventType.Ended"/> を通知する。
        ///
        /// <see cref="AudioBackendHost"/> が毎フレーム呼ぶ。
        /// 自分で止めた場合(Stop / Skip / Pause)は状態が Playing でなくなるため、
        /// 自然終了とは区別される。
        /// </summary>
        /// <returns>このフレームで自然終了を検出したら true。</returns>
        public bool Tick()
        {
            if (_state != BackendState.Playing) return false;
            if (_player.IsPlaying) return false;

            var previous = _state;
            _state = BackendState.Ended;

            Log($"Ended {_current.Id}  (曲が最後まで再生されました)");
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

        private bool Reject(MediaItem item, string message)
        {
            Log(message);
            Notify(new BackendEvent(BackendEventType.Error, item, _state, _state, Name, message));
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
