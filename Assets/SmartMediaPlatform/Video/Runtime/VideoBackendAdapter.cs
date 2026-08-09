using System;
using System.Collections.Generic;
using SmartMediaPlatform.Adapter;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Video
{
    /// <summary>
    /// <see cref="IVideoBackend"/>(動画プレイヤーの語彙)を
    /// <see cref="IBackendAdapter"/>(プラットフォーム共通の契約)へ翻訳するアダプタ。
    ///
    /// <b>ここで吸収している 5 つの違い</b>
    /// <list type="number">
    /// <item>対象:<c>MediaItem</c> ↔ URL 文字列</item>
    /// <item>状態:<see cref="BackendState"/> ↔ <see cref="VideoPlayerState"/></item>
    /// <item>シーク:0〜1 の割合 ↔ 秒</item>
    /// <item>通知:<see cref="IBackendObserver"/> ↔ <see cref="IVideoBackendObserver"/></item>
    /// <item>読み込み:同期的な期待 ↔ 非同期(Loading を経由する)</item>
    /// </list>
    ///
    /// <see cref="IBackendAdapter"/> は <see cref="IMediaBackend"/> を継承しているので、
    /// このアダプタはそのまま <see cref="BackendManager"/> に登録できます。
    /// <b>MediaPlayer / PlayerSession / Queue / Recommendation / Catalog は変更不要</b>です。
    ///
    /// 将来 VRChat の動画プレイヤーを使うときは、<see cref="IVideoBackend"/> の実装を
    /// 差し替えるだけで、このアダプタも上位も手を入れません。
    /// </summary>
    public sealed class VideoBackendAdapter : IBackendAdapter, IVideoBackendObserver
    {
        private readonly List<IBackendObserver> _observers = new List<IBackendObserver>();
        private readonly IVideoBackend _video;
        private readonly MediaType[] _supportedTypes;
        private readonly IReadOnlyList<MediaType> _readOnlyTypes;

        private IBackendLogger _logger;
        private MediaItem _current;

        /// <param name="name">アダプタ名(ログに出る)。</param>
        /// <param name="video">包む動画バックエンド。</param>
        /// <param name="logger">ログ出力先。</param>
        /// <param name="supportedTypes">扱う種別。未指定なら Video と Live。</param>
        public VideoBackendAdapter(
            string name,
            IVideoBackend video,
            IBackendLogger logger = null,
            params MediaType[] supportedTypes)
        {
            Name = string.IsNullOrEmpty(name) ? "VideoBackendAdapter" : name;
            _video = video ?? throw new ArgumentNullException(nameof(video));
            _logger = logger ?? NullBackendLogger.Instance;

            _supportedTypes = supportedTypes != null && supportedTypes.Length > 0
                ? (MediaType[])supportedTypes.Clone()
                : new[] { MediaType.Video, MediaType.Live };
            _readOnlyTypes = Array.AsReadOnly(_supportedTypes);

            // 動画プレイヤーの通知を受け取って、プラットフォームの通知へ翻訳する
            _video.AddObserver(this);
        }

        public string Name { get; }

        /// <summary>包んでいる動画バックエンド。</summary>
        public IVideoBackend VideoBackend => _video;

        public IReadOnlyList<MediaType> SupportedTypes => _readOnlyTypes;

        public float RewindThresholdSeconds { get; set; } = 3f;

        public string LastError { get; private set; }

        public bool HasError => LastError != null;

        public void SetLogger(IBackendLogger logger)
        {
            _logger = logger ?? NullBackendLogger.Instance;
        }

        // ───────── 状態の翻訳 ─────────

        /// <summary>
        /// 動画プレイヤーの状態を、プラットフォーム共通の状態へ写す。
        /// <b>ここが「違いの吸収」の中心</b>。
        /// </summary>
        public BackendState GetState()
        {
            switch (_video.GetState())
            {
                case VideoPlayerState.Loading: return BackendState.Loading;
                case VideoPlayerState.Ready: return BackendState.Ready;
                case VideoPlayerState.Playing: return BackendState.Playing;
                case VideoPlayerState.Paused: return BackendState.Paused;
                case VideoPlayerState.Stopped: return BackendState.Stopped;
                case VideoPlayerState.Finished: return BackendState.Ended;
                case VideoPlayerState.Failed: return BackendState.Error;
                default: return BackendState.Idle;
            }
        }

        public MediaItem GetCurrent() => _current;

        public MediaItem GetCurrentMedia() => _current;

        // ───────── 対象の翻訳(MediaItem ↔ URL) ─────────

        public bool CanPlay(MediaItem item)
        {
            if (item == null) return false;

            bool typeSupported = false;
            for (int i = 0; i < _supportedTypes.Length; i++)
            {
                if (_supportedTypes[i] == item.Type) { typeSupported = true; break; }
            }
            if (!typeSupported) return false;

            return _video.CanPlay(item.Url);
        }

        public bool Load(MediaItem item)
        {
            ClearError();

            if (item == null)
            {
                ReportError("Load 失敗: メディアが null です");
                return false;
            }

            if (!CanPlay(item))
            {
                ReportError($"Load 失敗: {Name} は {item.Id} ({item.Type}) を扱えません");
                return false;
            }

            var previous = GetState();

            // MediaItem から URL を取り出して動画プレイヤーへ渡す。
            // 実機ではここでベイク済みの VRCUrl を引くことになるが、
            // その処理はこのアダプタの内側(IVideoBackend の実装)に閉じ込める。
            if (!_video.Load(item.Url))
            {
                ReportError($"Load 失敗: {item.Id} ({item.Url})");
                return false;
            }

            _current = item;
            Log($"Load {item.Id} ({item.Title}) -> {item.Url}");
            Emit(BackendEventType.Loaded, item, previous);
            return true;
        }

        // ───────── 操作の委譲 ─────────

        public bool Play()
        {
            if (_current == null)
            {
                Log("Play 無視: 何も読み込んでいません");
                return false;
            }

            var previous = GetState();
            if (!_video.Play()) return false;

            Emit(BackendEventType.Started, _current, previous);
            return true;
        }

        public bool Pause()
        {
            var previous = GetState();
            if (!_video.Pause()) return false;

            Emit(BackendEventType.Paused, _current, previous);
            return true;
        }

        public bool Resume()
        {
            var previous = GetState();
            if (!_video.Resume()) return false;

            Emit(BackendEventType.Resumed, _current, previous);
            return true;
        }

        public bool Stop()
        {
            var previous = GetState();
            if (!_video.Stop()) return false;

            Emit(BackendEventType.Stopped, _current, previous);
            return true;
        }

        public bool Skip()
        {
            if (_current == null)
            {
                Log("Skip 無視: 何も読み込んでいません");
                return false;
            }

            var previous = GetState();
            _video.Stop();

            Emit(BackendEventType.Skipped, _current, previous);
            return true;
        }

        public bool SkipNext()
        {
            if (_current == null)
            {
                Log("SkipNext 無視: 何も読み込んでいません");
                return false;
            }

            Log($"SkipNext: {_current.Id} を打ち切ります(次の選択は上位の仕事)");
            return Skip();
        }

        public bool SkipPrevious()
        {
            if (_current == null)
            {
                Log("SkipPrevious 無視: 何も読み込んでいません");
                return false;
            }

            // 十分に再生が進んでいれば頭出しする
            if (CanSeek && GetCurrentTime() > RewindThresholdSeconds)
            {
                Log($"SkipPrevious: {_current.Id} を頭出しします");
                return Seek(0f);
            }

            Log($"SkipPrevious: {_current.Id} を打ち切ります(前の選択は上位の仕事)");
            return Skip();
        }

        // ───────── シークの翻訳(割合 ↔ 秒) ─────────

        public bool CanSeek => _current != null && _video.CanSeek;

        public float GetCurrentTime() => _video.GetTime();

        public float GetDuration()
        {
            float duration = _video.GetDuration();
            if (duration > 0f) return duration;

            // 動画プレイヤーが長さを答えられないときは Catalog のメタデータで補う
            return _current != null ? _current.DurationSeconds : 0f;
        }

        public float GetProgress()
        {
            float duration = GetDuration();
            if (duration <= 0f) return 0f;

            float progress = GetCurrentTime() / duration;
            return progress < 0f ? 0f : progress > 1f ? 1f : progress;
        }

        /// <summary>
        /// 上位は 0〜1 の割合で指定するが、動画プレイヤーは秒で受け取る。
        /// ここで変換する。
        /// </summary>
        public bool Seek(float normalizedPosition)
        {
            if (!CanSeek)
            {
                Log("Seek 無視: シークに対応していません");
                return false;
            }

            float clamped = normalizedPosition < 0f ? 0f
                : normalizedPosition > 1f ? 1f : normalizedPosition;

            return _video.Seek(clamped * GetDuration());
        }

        // ───────── エラー ─────────

        public void ReportError(string message)
        {
            LastError = message ?? "unknown error";
            Log($"エラー: {LastError}");

            Notify(new BackendEvent(
                BackendEventType.Error, _current, GetState(), GetState(), Name, LastError));
        }

        public void ClearError()
        {
            LastError = null;
        }

        // ───────── 通知の翻訳(動画プレイヤー → プラットフォーム) ─────────

        public void OnVideoReady(string url)
        {
            Log($"読み込み完了の通知: {url}");
            // 読み込み完了は Loaded として既に伝えているので、状態の変化だけ知らせる
            Emit(BackendEventType.Loaded, _current, BackendState.Loading);
        }

        public void OnVideoStart(string url)
        {
            // Play() / Resume() から明示的に通知済みなので、ここでは二重に出さない。
        }

        public void OnVideoPause(string url)
        {
            // Pause() から通知済み。
        }

        public void OnVideoStop(string url)
        {
            // Stop() / Skip() から通知済み。
        }

        /// <summary>
        /// 動画が最後まで再生された。<b>これが上位の自動送りの起点</b>になる。
        /// </summary>
        public void OnVideoEnd(string url)
        {
            Log($"再生終了の通知: {url}");
            Emit(BackendEventType.Ended, _current, BackendState.Playing);
        }

        public void OnVideoError(string url, string message)
        {
            ReportError($"動画エラー: {message} ({url})");
        }

        // ───────── 観測者 ─────────

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
            Notify(new BackendEvent(type, item, previous, GetState(), Name));
        }

        private void Notify(BackendEvent backendEvent)
        {
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

        public override string ToString()
        {
            return $"{Name} (types: {string.Join("/", Array.ConvertAll(_supportedTypes, t => t.ToString()))}, "
                   + $"video: {_video.Name})";
        }
    }
}
