using System;
using VRC.SDK3.Video.Components.Base;
using VRC.SDKBase;

namespace SmartMediaPlatform.Video.VRChat
{
    /// <summary>
    /// VRChat の <see cref="BaseVRCVideoPlayer"/> を <see cref="IVRCVideoPlayer"/> として包む。
    /// <b>ここが「実際に動画が出る」唯一の場所</b>です(Audio 層の <c>UnityAudioSourcePlayer</c> に対応)。
    ///
    /// <see cref="BaseVRCVideoPlayer"/> は <c>VRCUnityVideoPlayer</c> と
    /// <c>VRCAVProVideoPlayer</c> の共通基底なので、<b>両方をこの 1 クラスで扱えます。</b>
    /// どちらが繋がっているかは <see cref="Kind"/> でのみ区別し(生配信の可否判定に使う)、
    /// 操作は完全に共通です。
    ///
    /// 唯一の翻訳は URL です:
    /// 上位は URL 文字列を渡し、ここが <see cref="VRCUrlTable"/> から
    /// <b>ベイク済みの</b> <see cref="VRCUrl"/> を引きます。実行時生成は行いません。
    /// </summary>
    public sealed class VRCVideoPlayerBridge : IVRCVideoPlayer
    {
        private readonly BaseVRCVideoPlayer _player;
        private readonly VRCUrlTable _urls;

        /// <param name="player">シーンに置いた VRCUnityVideoPlayer / VRCAVProVideoPlayer。</param>
        /// <param name="urls">編集時に焼き込んだ VRCUrl 表。</param>
        /// <param name="kind">
        /// プレイヤーの種類。null なら型名から自動判定する
        /// (SDK のバージョン差で AVPro の名前空間が変わっても壊れないようにするため)。
        /// </param>
        public VRCVideoPlayerBridge(
            BaseVRCVideoPlayer player,
            VRCUrlTable urls,
            VRCVideoPlayerKind? kind = null)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _urls = urls ?? throw new ArgumentNullException(nameof(urls));
            Kind = kind ?? DetectKind(player);
        }

        public VRCVideoPlayerKind Kind { get; }

        public bool IsPlaying => _player.IsPlaying;

        public bool IsReady => _player.IsReady;

        public bool Loop
        {
            get => _player.Loop;
            set => _player.Loop = value;
        }

        public bool LoadURL(string url)
        {
            VRCUrl baked = _urls.Resolve(url);
            if (baked == null) return false;   // 表に無ければ諦める(実行時に作らない)

            _player.LoadURL(baked);
            return true;
        }

        public void Play() => _player.Play();

        public void Pause() => _player.Pause();

        public void Stop() => _player.Stop();

        public void SetTime(float seconds) => _player.SetTime(seconds);

        public float GetTime() => _player.GetTime();

        public float GetDuration() => _player.GetDuration();

        /// <summary>
        /// 型名から AVPro かどうかを判定する。
        /// 具体型を直接参照しないので、SDK の名前空間が変わってもコンパイルが壊れない。
        /// </summary>
        private static VRCVideoPlayerKind DetectKind(BaseVRCVideoPlayer player)
        {
            string typeName = player.GetType().Name;
            return typeName.IndexOf("AVPro", StringComparison.OrdinalIgnoreCase) >= 0
                ? VRCVideoPlayerKind.AVPro
                : VRCVideoPlayerKind.Unity;
        }
    }
}
