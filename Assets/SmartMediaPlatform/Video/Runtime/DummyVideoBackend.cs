using System;
using System.Collections.Generic;
using SmartMediaPlatform.Backend;

namespace SmartMediaPlatform.Video
{
    /// <summary>
    /// 動画を再生しない <see cref="IVideoBackend"/>。<b>ログを出すだけ</b>です。
    ///
    /// VRChat SDK / VRCUnityVideoPlayer / VRCAVProVideoPlayer / VRCUrl は使いません。
    /// 目的は「動画バックエンドを差し替えても上位が変わらない」ことを示すことなので、
    /// 状態機械と通知の流れだけを本物と同じ形で持っています。
    ///
    /// 実機の動画プレイヤーは読み込みが非同期なので、
    /// <see cref="Load"/> 直後は <see cref="VideoPlayerState.Loading"/> になり、
    /// <see cref="CompleteLoading"/> を呼ぶと読み込み完了(Ready)になります。
    /// 既定では <see cref="AutoCompleteLoading"/> が true なので、
    /// 同期的に Ready まで進みます(テストとデモを書きやすくするため)。
    /// </summary>
    public sealed class DummyVideoBackend : IVideoBackend
    {
        private readonly List<IVideoBackendObserver> _observers = new List<IVideoBackendObserver>();
        private readonly IBackendLogger _logger;

        private string _url;
        private VideoPlayerState _state = VideoPlayerState.None;
        private float _time;

        public DummyVideoBackend(string name = "DummyVideoBackend", IBackendLogger logger = null)
        {
            Name = string.IsNullOrEmpty(name) ? "DummyVideoBackend" : name;
            _logger = logger ?? NullBackendLogger.Instance;
        }

        public string Name { get; }

        /// <summary>読み込みを同期的に完了させるか。false にすると非同期の挙動を再現できる。</summary>
        public bool AutoCompleteLoading { get; set; } = true;

        /// <summary>動画の長さ(秒)。実機では読み込み後に判明する。</summary>
        public float Duration { get; set; } = 120f;

        /// <summary>シークできるか。false にすると生配信の振る舞いになる。</summary>
        public bool CanSeek { get; set; } = true;

        /// <summary>再生開始の回数(繰り返し再生の検証に使う)。</summary>
        public int PlayCallCount { get; private set; }

        // ───────── 問い合わせ ─────────

        public VideoPlayerState GetState() => _state;

        public string GetCurrentUrl() => _url;

        public float GetTime() => _time;

        public float GetDuration() => _url != null ? Duration : 0f;

        /// <summary>
        /// 空でない URL なら再生できることにする。
        /// 実機では拡張子やホストを見て判断する部分。
        /// </summary>
        public bool CanPlay(string url)
        {
            return !string.IsNullOrWhiteSpace(url);
        }

        // ───────── 操作 ─────────

        public bool Load(string url)
        {
            if (!CanPlay(url))
            {
                Fail(url, "URL が空です");
                return false;
            }

            _url = url;
            _time = 0f;
            _state = VideoPlayerState.Loading;
            Log($"Load {url} — 動画は再生しません / no actual video");

            if (AutoCompleteLoading) CompleteLoading();
            return true;
        }

        /// <summary>読み込みが終わったことにする(実機の OnVideoReady に相当)。</summary>
        public bool CompleteLoading()
        {
            if (_state != VideoPlayerState.Loading) return false;

            _state = VideoPlayerState.Ready;
            Log($"読み込み完了: {_url}");
            Notify(o => o.OnVideoReady(_url));
            return true;
        }

        public bool Play()
        {
            if (_url == null)
            {
                Log("Play 無視: 何も読み込んでいません");
                return false;
            }
            if (_state == VideoPlayerState.Loading)
            {
                Log("Play 無視: まだ読み込み中です");
                return false;
            }
            if (_state == VideoPlayerState.Playing)
            {
                Log("Play 無視: すでに再生中です");
                return false;
            }
            if (_state == VideoPlayerState.Paused)
            {
                Log("Play 無視: 一時停止中です(Resume を使ってください)");
                return false;
            }

            _time = 0f;
            _state = VideoPlayerState.Playing;
            PlayCallCount++;
            Log($"Play {_url} — 動画は再生しません / no actual video");
            Notify(o => o.OnVideoStart(_url));
            return true;
        }

        public bool Pause()
        {
            if (_state != VideoPlayerState.Playing)
            {
                Log("Pause 無視: 再生中ではありません");
                return false;
            }

            _state = VideoPlayerState.Paused;
            Log($"Pause {_url}");
            Notify(o => o.OnVideoPause(_url));
            return true;
        }

        public bool Resume()
        {
            if (_state != VideoPlayerState.Paused)
            {
                Log("Resume 無視: 一時停止中ではありません");
                return false;
            }

            _state = VideoPlayerState.Playing;
            Log($"Resume {_url}");
            Notify(o => o.OnVideoStart(_url));
            return true;
        }

        public bool Stop()
        {
            if (_url == null)
            {
                Log("Stop 無視: 何も読み込んでいません");
                return false;
            }
            if (_state == VideoPlayerState.Stopped)
            {
                Log("Stop 無視: すでに停止しています");
                return false;
            }

            _state = VideoPlayerState.Stopped;
            _time = 0f;
            Log($"Stop {_url}");
            Notify(o => o.OnVideoStop(_url));
            return true;
        }

        public bool Seek(float seconds)
        {
            if (_url == null || !CanSeek)
            {
                Log("Seek 無視: シークできません");
                return false;
            }

            float max = GetDuration();
            _time = seconds < 0f ? 0f : (max > 0f && seconds > max ? max : seconds);
            Log($"Seek {_url} -> {_time:0.00}s / {max:0.00}s");
            return true;
        }

        // ───────── 検証用のフック ─────────

        /// <summary>再生位置を進める(実機では自動で進む)。</summary>
        public void Advance(float seconds)
        {
            if (_state == VideoPlayerState.Playing) _time += seconds;
        }

        /// <summary>最後まで再生し終えたことにする(実機の OnVideoEnd に相当)。</summary>
        public bool SimulateFinished()
        {
            if (_state != VideoPlayerState.Playing && _state != VideoPlayerState.Paused) return false;

            _state = VideoPlayerState.Finished;
            _time = GetDuration();
            Log($"再生終了: {_url}");
            Notify(o => o.OnVideoEnd(_url));
            return true;
        }

        /// <summary>失敗したことにする(実機の OnVideoError に相当)。</summary>
        public void SimulateError(string message)
        {
            Fail(_url, message);
        }

        private void Fail(string url, string message)
        {
            _state = VideoPlayerState.Failed;
            Log($"エラー: {message} ({url ?? "(no url)"})");
            Notify(o => o.OnVideoError(url, message));
        }

        // ───────── 通知 ─────────

        public void AddObserver(IVideoBackendObserver observer)
        {
            if (observer == null || _observers.Contains(observer)) return;
            _observers.Add(observer);
        }

        public void RemoveObserver(IVideoBackendObserver observer)
        {
            if (observer == null) return;
            _observers.Remove(observer);
        }

        private void Notify(Action<IVideoBackendObserver> action)
        {
            var snapshot = _observers.ToArray();
            for (int i = 0; i < snapshot.Length; i++) action(snapshot[i]);
        }

        private void Log(string message)
        {
            _logger.Log($"[{Name}] {message}");
        }
    }
}
