using System;
using System.Collections.Generic;
using SmartMediaPlatform.Backend;

namespace SmartMediaPlatform.Video
{
    /// <summary>
    /// VRChat の動画プレイヤー(<c>VRCUnityVideoPlayer</c> / <c>VRCAVProVideoPlayer</c>)で
    /// <b>実際に動画を再生する</b> <see cref="IVideoBackend"/>。
    ///
    /// <see cref="DummyVideoBackend"/> と<b>同じ契約・同じ状態遷移</b>なので、
    /// <see cref="VideoBackendAdapter"/> を 1 行も変えずに差し替えられます。
    /// その上の MediaPlayer / PlayerSession / Queue / Recommendation / Catalog も無変更です。
    ///
    /// <b>SDK 型はここには出てきません。</b>
    /// 動画プレイヤーは <see cref="IVRCVideoPlayer"/> 越しにだけ触るので、
    /// このクラスは <c>Video/Runtime</c>(<c>noEngineReferences: true</c>)に置けて、
    /// EditMode テストでは偽のプレイヤーで決定的に検証できます。
    /// 実体は <c>SmartMediaPlatform.Video.VRChat</c> の <c>VRCVideoPlayerBridge</c> です。
    ///
    /// <b>通知の入口は 2 つあります。</b>
    /// <list type="number">
    /// <item>
    /// <b>プッシュ</b>: 実機の <c>OnVideoReady</c> / <c>OnVideoEnd</c> / <c>OnVideoError</c> …を
    /// <see cref="NotifyVideoReady"/> などで流し込む(正確・即時)。
    /// </item>
    /// <item>
    /// <b>ポーリング</b>: <see cref="Tick"/> が <see cref="IVRCVideoPlayer.IsReady"/> /
    /// <see cref="IVRCVideoPlayer.IsPlaying"/> を見て読み込み完了・再生終了を補足する。
    /// コールバックが繋がっていない場合や、プレイヤーが<b>自発的に止まった</b>場合
    /// (Phase2-4(B) の設計レビュー 2 番)の保険。
    /// </item>
    /// </list>
    /// どちらから来ても通知は 1 回だけです(状態で二重発火を防いでいます)。
    /// </summary>
    public sealed class VRChatVideoBackend : IVideoBackend
    {
        /// <summary>再生終了とみなす、終端までの許容誤差(秒)。</summary>
        private const float EndTolerance = 0.25f;

        private readonly List<IVideoBackendObserver> _observers = new List<IVideoBackendObserver>();
        private readonly IVRCVideoPlayer _player;
        private readonly IVideoUrlTable _urls;
        private readonly IBackendLogger _logger;

        private string _url;
        private VideoPlayerState _state = VideoPlayerState.None;
        private float _loadingSeconds;
        private bool _playOnReady;

        /// <param name="name">バックエンド名(ログに出る)。</param>
        /// <param name="player">VRChat の動画プレイヤー(<c>BaseVRCVideoPlayer</c> のラッパー)。</param>
        /// <param name="urls">事前ベイク済みの URL 表。実行時に URL は作らない。</param>
        /// <param name="logger">ログ出力先。</param>
        public VRChatVideoBackend(
            string name,
            IVRCVideoPlayer player,
            IVideoUrlTable urls,
            IBackendLogger logger = null)
        {
            Name = string.IsNullOrEmpty(name) ? "VRChatVideoBackend" : name;
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _urls = urls ?? throw new ArgumentNullException(nameof(urls));
            _logger = logger ?? NullBackendLogger.Instance;

            // ループしたままだと動画が終わらず OnVideoEnd が来ない。
            // Audio 層の UnityAudioSourcePlayer が loop を切るのと同じ理由。
            _player.Loop = false;
        }

        public string Name { get; }

        /// <summary>包んでいる VRChat 動画プレイヤー。</summary>
        public IVRCVideoPlayer Player => _player;

        /// <summary>事前ベイク済みの URL 表。</summary>
        public IVideoUrlTable UrlTable => _urls;

        /// <summary>直近のエラー理由。</summary>
        public VideoErrorKind LastErrorKind { get; private set; } = VideoErrorKind.None;

        /// <summary>
        /// 読み込みがこの秒数を超えたらタイムアウトにする。0 以下で無効。
        /// 実機では URL の解決に数秒かかることがあるため、既定は余裕を持たせている。
        /// </summary>
        public float LoadTimeoutSeconds { get; set; } = 20f;

        /// <summary>
        /// <see cref="Tick"/> による再生終了の自動検出を行うか。
        /// 実機の <c>OnVideoEnd</c> が確実に届く構成では false にしてもよい。
        /// </summary>
        public bool DetectEndByPolling { get; set; } = true;

        /// <summary>
        /// 読み込み中に <see cref="Play"/> が来たとき、<b>読み込み完了後に自動で再生を始めるか</b>。
        ///
        /// 実機の動画プレイヤーは <see cref="Load"/> してもすぐには再生できません。
        /// 一方 <c>MediaPlayer.Play()</c> は「読み込んで、すぐ再生する」という順で呼びます
        /// (Phase2-2、変更不可)。この 2 つを噛み合わせるのがこのフラグです。
        ///
        /// <b>この吸収を Backend 側でやることに意味があります。</b>
        /// 上位に「読み込みが終わったら再生し直す」責務を持ち込まずに済むので、
        /// MediaPlayer / PlayerSession / Queue / Recommendation を 1 行も変えずに
        /// 実機の非同期読み込みを扱えます。
        ///
        /// false にすると <see cref="DummyVideoBackend"/> と完全に同じ振る舞い
        /// (読み込み中の <see cref="Play"/> は拒否)になります。
        /// </summary>
        public bool AutoPlayWhenReady { get; set; } = true;

        /// <summary>読み込み完了後に自動再生する予約が入っているか。</summary>
        public bool IsPlayPending => _playOnReady;

        /// <summary>再生開始の回数(繰り返し再生の検証用。Dummy と揃えてある)。</summary>
        public int PlayCallCount { get; private set; }

        // ───────── 問い合わせ ─────────

        public VideoPlayerState GetState() => _state;

        public string GetCurrentUrl() => _url;

        public float GetTime() => _url != null ? _player.GetTime() : 0f;

        public float GetDuration()
        {
            if (_url == null) return 0f;

            float duration = _player.GetDuration();

            // 生配信は 0 や無限大を返す。長さ不明は 0 に揃える
            // (VideoBackendAdapter が Catalog のメタデータで補ってくれる)。
            if (duration <= 0f || float.IsInfinity(duration) || float.IsNaN(duration)) return 0f;
            return duration;
        }

        /// <summary>
        /// 生配信・長さ不明の動画ではシークできない。
        /// <c>VideoBackendAdapter.Seek(割合)</c> はこれを見て要求を握りつぶす。
        /// </summary>
        public bool CanSeek => _url != null && GetDuration() > 0f;

        /// <summary>
        /// その URL を再生できるか。<b>3 つの条件をすべて満たす必要があります。</b>
        /// <list type="number">
        /// <item>空でないこと</item>
        /// <item><b>事前ベイク済みの表に載っていること</b>(実行時 VRCUrl 生成をしないため)</item>
        /// <item>プレイヤーの種類が対応していること(生配信は AVPro のみ)</item>
        /// </list>
        /// </summary>
        public bool CanPlay(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!_urls.Contains(url)) return false;

            return _player.Kind == VRCVideoPlayerKind.AVPro || !IsLiveStreamUrl(url);
        }

        /// <summary>
        /// 生配信系の URL か。<c>VRCUnityVideoPlayer</c> はこれらを扱えないため、
        /// AVPro が繋がっていない場合は <see cref="CanPlay"/> で断る。
        /// </summary>
        public static bool IsLiveStreamUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            string lower = url.Trim().ToLowerInvariant();
            return lower.StartsWith("rtsp://")
                   || lower.StartsWith("rtmp://")
                   || lower.StartsWith("rtspt://")
                   || lower.EndsWith(".m3u8");
        }

        // ───────── 操作 ─────────

        public bool Load(string url)
        {
            if (!CanPlay(url))
            {
                Fail(url,
                    _urls.Contains(url) ? VideoErrorKind.PlayerError : VideoErrorKind.InvalidUrl,
                    _urls.Contains(url)
                        ? $"{_player.Kind} プレイヤーはこの URL を扱えません"
                        : "Catalog にベイクされていない URL です(実行時に VRCUrl は作れません)");
                return false;
            }

            // 前の再生を必ず止めてから読み込む(実機で前の動画の音が残るのを防ぐ)。
            _player.Stop();

            if (!_player.LoadURL(url))
            {
                _url = null;
                Fail(url, VideoErrorKind.InvalidUrl, "ベイク済み VRCUrl の取得に失敗しました");
                return false;
            }

            _url = url;
            _state = VideoPlayerState.Loading;
            _loadingSeconds = 0f;
            _playOnReady = false;
            LastErrorKind = VideoErrorKind.None;

            Log($"Load {url}(読み込み中 — 完了は OnVideoReady で通知される)");

            // 実機は非同期だが、既に読み込み済みのプレイヤーはこの時点で Ready を返す。
            if (_player.IsReady) CompleteLoading();
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
                if (!AutoPlayWhenReady)
                {
                    Log("Play 無視: まだ読み込み中です");
                    return false;
                }

                // 実機は読み込みが非同期。要求を覚えておいて Ready になったら始める。
                _playOnReady = true;
                Log("Play 予約: 読み込み中のため、完了後に自動で再生します");
                return true;
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

            // 一度止まった/終わった動画は頭から流し直す(Dummy が Play で 0 に戻すのと同じ)。
            if ((_state == VideoPlayerState.Stopped || _state == VideoPlayerState.Finished)
                && CanSeek)
            {
                _player.SetTime(0f);
            }

            StartPlayback("Play");
            return true;
        }

        private void StartPlayback(string reason)
        {
            _playOnReady = false;
            _player.Play();
            _state = VideoPlayerState.Playing;
            PlayCallCount++;
            Log($"{reason} {_url}");
            Notify(o => o.OnVideoStart(_url));
        }

        public bool Pause()
        {
            if (_state != VideoPlayerState.Playing)
            {
                Log("Pause 無視: 再生中ではありません");
                return false;
            }

            _player.Pause();
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

            // SDK には Resume が無く、Pause 後の Play が再開にあたる。
            _player.Play();
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

            _player.Stop();
            _state = VideoPlayerState.Stopped;
            _playOnReady = false;   // 読み込み完了後の自動再生も取り消す
            Log($"Stop {_url}");
            Notify(o => o.OnVideoStop(_url));
            return true;
        }

        public bool Seek(float seconds)
        {
            if (!CanSeek)
            {
                Log("Seek 無視: シークできません(生配信または長さ不明)");
                return false;
            }

            float max = GetDuration();
            float clamped = seconds < 0f ? 0f : (seconds > max ? max : seconds);

            _player.SetTime(clamped);
            Log($"Seek {_url} -> {clamped:0.00}s / {max:0.00}s");
            return true;
        }

        // ───────── 実機のコールバック(プッシュ) ─────────

        /// <summary>実機の <c>OnVideoReady</c>。読み込みが終わって再生できる。</summary>
        public void NotifyVideoReady()
        {
            CompleteLoading();
        }

        /// <summary>実機の <c>OnVideoStart</c>。プレイヤーが自発的に再生を始めた場合に備える。</summary>
        public void NotifyVideoStart()
        {
            if (_url == null || _state == VideoPlayerState.Playing) return;

            _state = VideoPlayerState.Playing;
            _playOnReady = false;   // すでに始まったので予約は不要
            Log($"再生開始の通知: {_url}");
            Notify(o => o.OnVideoStart(_url));
        }

        /// <summary>実機の <c>OnVideoPlay</c>(一時停止からの再開)。</summary>
        public void NotifyVideoPlay()
        {
            NotifyVideoStart();
        }

        /// <summary>実機の <c>OnVideoPause</c>。プレイヤー側の都合で止まった場合を拾う。</summary>
        public void NotifyVideoPause()
        {
            if (_state != VideoPlayerState.Playing) return;

            _state = VideoPlayerState.Paused;
            Log($"一時停止の通知: {_url}");
            Notify(o => o.OnVideoPause(_url));
        }

        /// <summary>
        /// 実機の <c>OnVideoEnd</c>。<b>上位の自動送りの起点</b>になる。
        /// <see cref="VideoBackendAdapter"/> がこれを <c>BackendEventType.Ended</c> へ翻訳する。
        /// </summary>
        public void NotifyVideoEnd()
        {
            if (_url == null) return;
            if (_state != VideoPlayerState.Playing && _state != VideoPlayerState.Paused) return;

            _state = VideoPlayerState.Finished;
            Log($"再生終了の通知: {_url}");
            Notify(o => o.OnVideoEnd(_url));
        }

        /// <summary>実機の <c>OnVideoLoop</c>。Loop は切っているので通常は来ない。</summary>
        public void NotifyVideoLoop()
        {
            Log($"ループの通知: {_url}(Loop は無効のはずです)");
        }

        /// <summary>実機の <c>OnVideoError(VideoError)</c>。</summary>
        public void NotifyVideoError(VideoErrorKind kind, string message = null)
        {
            Fail(_url, kind, message ?? DescribeError(kind));
        }

        // ───────── 毎フレームの補足(ポーリング) ─────────

        /// <summary>
        /// プレイヤーの状態を見て、コールバックが来なかった変化を補う。
        /// ホストの <c>Update()</c> から毎フレーム呼ぶ。
        ///
        /// 拾うのは 3 つだけです:
        ///  1. 読み込み完了(<c>IsReady</c> が立った)
        ///  2. 読み込みタイムアウト
        ///  3. 再生終了(終端まで進んだのに <c>IsPlaying</c> が false)
        /// </summary>
        /// <param name="deltaSeconds">前回からの経過秒(タイムアウト判定に使う)。</param>
        public void Tick(float deltaSeconds = 0f)
        {
            if (_state == VideoPlayerState.Loading)
            {
                if (_player.IsReady)
                {
                    CompleteLoading();
                    return;
                }

                _loadingSeconds += deltaSeconds > 0f ? deltaSeconds : 0f;
                if (LoadTimeoutSeconds > 0f && _loadingSeconds >= LoadTimeoutSeconds)
                {
                    Fail(_url, VideoErrorKind.Timeout,
                        $"読み込みが {LoadTimeoutSeconds:0.#} 秒以内に完了しませんでした");
                }
                return;
            }

            if (_state != VideoPlayerState.Playing || !DetectEndByPolling) return;
            if (_player.IsPlaying) return;

            // 再生開始直後の 1 フレームだけ IsPlaying が false になることがあるため、
            // 「終端まで進んでいる」ことを条件にして誤検出を避ける。
            float duration = GetDuration();
            if (duration > 0f && _player.GetTime() >= duration - EndTolerance)
            {
                NotifyVideoEnd();
            }
        }

        // ───────── 内部 ─────────

        private void CompleteLoading()
        {
            if (_state != VideoPlayerState.Loading) return;

            _state = VideoPlayerState.Ready;
            _loadingSeconds = 0f;
            Log($"読み込み完了: {_url}");
            Notify(o => o.OnVideoReady(_url));

            // 読み込み中に来ていた Play 要求をここで実行する。
            if (_playOnReady) StartPlayback("Play(予約分)");
        }

        private void Fail(string url, VideoErrorKind kind, string message)
        {
            LastErrorKind = kind;
            _state = VideoPlayerState.Failed;
            _playOnReady = false;

            string text = $"{kind}: {message}";
            Log($"エラー: {text} ({url ?? "(no url)"})");
            Notify(o => o.OnVideoError(url, text));
        }

        private static string DescribeError(VideoErrorKind kind)
        {
            switch (kind)
            {
                case VideoErrorKind.InvalidUrl: return "URL が不正です";
                case VideoErrorKind.AccessDenied: return "URL へのアクセスが拒否されました";
                case VideoErrorKind.PlayerError: return "動画プレイヤーが再生に失敗しました";
                case VideoErrorKind.RateLimited: return "読み込み要求が制限されました";
                case VideoErrorKind.Timeout: return "読み込みがタイムアウトしました";
                default: return "原因不明の失敗です";
            }
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

        public override string ToString()
        {
            return $"{Name} (player: {_player.Kind}, urls: {_urls.Count}, state: {_state})";
        }
    }
}
