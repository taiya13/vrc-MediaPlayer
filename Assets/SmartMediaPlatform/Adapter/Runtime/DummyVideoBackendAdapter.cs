using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Adapter
{
    /// <summary>
    /// 動画用のダミーアダプタ。<b>実際には映像を出さず、ログだけ</b>を出す。
    ///
    /// 目的は「VideoBackend を足しても上位が変わらない」ことを示すことなので、
    /// VRChat SDK も VideoPlayer も使わない。
    ///
    /// <b>アダプタが違いを吸収している例</b>
    /// 内側に使っている <see cref="DummyBackend"/> は再生位置を持たない
    /// (<see cref="ISeekableBackend"/> を実装していない)。
    /// しかし実際の動画プレイヤーはシークできるのが普通なので、
    /// このアダプタが<b>再生位置を肩代わり</b>して <c>CanSeek = true</c> を返す。
    ///
    /// これにより上位から見ると Audio と Video は同じ顔をしている:
    ///  - AudioBackendAdapter … 内側(AudioSource)の再生位置をそのまま通す
    ///  - DummyVideoBackendAdapter … アダプタが再生位置を作って通す
    ///
    /// 将来 <c>VRCUnityVideoPlayer</c> / <c>VRCAVProVideoPlayer</c> を使う実装に
    /// 差し替えるときは、このクラスを本物のバックエンドを包むものに置き換えるだけでよい。
    /// </summary>
    public sealed class DummyVideoBackendAdapter : BackendAdapterBase
    {
        private float _time;

        /// <summary>
        /// 動画プレイヤーの代わりにシークできるふりをするか。
        /// false にすると、シークできないバックエンド(生配信など)の振る舞いを再現できる。
        /// </summary>
        public bool SimulateSeekSupport { get; set; } = true;

        public DummyVideoBackendAdapter(
            string name = "DummyVideoBackendAdapter",
            IBackendLogger logger = null,
            params MediaType[] supportedTypes)
            : base(
                name,
                // 内側は Phase1-5 の DummyBackend を流用する(映像は出さずログのみ)。
                new DummyBackend(
                    (string.IsNullOrEmpty(name) ? "DummyVideoBackend" : name) + ".inner",
                    logger,
                    supportedTypes != null && supportedTypes.Length > 0
                        ? supportedTypes
                        : new[] { MediaType.Video, MediaType.Live }),
                logger,
                supportedTypes != null && supportedTypes.Length > 0
                    ? supportedTypes
                    : new[] { MediaType.Video, MediaType.Live })
        {
        }

        /// <summary>内側のダミーバックエンド(自然終了を起こすなど、検証用)。</summary>
        public DummyBackend InnerDummy => (DummyBackend)Inner;

        // ───────── 再生位置を肩代わりする ─────────

        /// <summary>
        /// 内側はシークできないが、動画プレイヤーとして振る舞うために
        /// アダプタが再生位置を持つ。ここが「違いの吸収」。
        /// </summary>
        public override bool CanSeek => SimulateSeekSupport && GetCurrent() != null;

        public override float GetCurrentTime() => _time;

        public override float GetDuration()
        {
            // 動画の長さは Catalog のメタデータから取る
            var current = GetCurrent();
            return current != null ? current.DurationSeconds : 0f;
        }

        public override bool Seek(float normalizedPosition)
        {
            if (!CanSeek)
            {
                Log("Seek 無視: シークに対応していません");
                return false;
            }

            float clamped = normalizedPosition < 0f ? 0f
                : normalizedPosition > 1f ? 1f : normalizedPosition;
            _time = clamped * GetDuration();

            Log($"Seek {GetCurrent().Id} -> {_time:0.00}s / {GetDuration():0.00}s (映像は出しません)");
            return true;
        }

        /// <summary>
        /// 再生位置を進める。実機の動画プレイヤーでは自動で進むが、
        /// ダミーなので外から進めてもらう。
        /// </summary>
        public void Advance(float seconds)
        {
            if (GetState() == BackendState.Playing) _time += seconds;
        }

        // ───────── 状態が変わったら再生位置を戻す ─────────

        protected override void OnLoaded(MediaItem item)
        {
            _time = 0f;
            Log($"Load {item.Id} ({item.Title}) — 映像は出しません / no actual video");
        }

        public override bool Play()
        {
            bool ok = base.Play();
            if (ok) Log($"Play {GetCurrent().Id} — 映像は出しません / no actual video");
            return ok;
        }

        public override bool Stop()
        {
            bool ok = base.Stop();
            if (ok) _time = 0f;
            return ok;
        }

        public override bool Skip()
        {
            bool ok = base.Skip();
            if (ok) _time = 0f;
            return ok;
        }
    }
}
