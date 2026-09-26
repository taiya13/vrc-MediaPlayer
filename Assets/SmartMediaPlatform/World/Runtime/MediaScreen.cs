using UnityEngine;

namespace SmartMediaPlatform.World
{
    /// <summary>
    /// <b>映像と音の出力先。</b>既定の <see cref="IMediaScreen"/> 実装。
    ///
    /// 持っているのは「どこに映すか」「どこから鳴らすか」だけです。
    /// 動画プレイヤーの種類(AVPro / Unity 版)は
    /// <see cref="IMediaBackendProvider"/> の担当で、こちらは知りません。
    ///
    /// <b>差し替えるとき</b>は <see cref="IMediaScreen"/> を実装した
    /// 別のコンポーネントを Screen の下に置いてください
    /// (ワールド内の別の板に映す、複数枚に映す、など)。
    ///
    /// 未設定なら自分と子から探すので、<b>Inspector を触らなくても動きます</b>。
    /// </summary>
    [AddComponentMenu("Smart Media Platform/Media Screen")]
    public sealed class MediaScreen : MonoBehaviour, IMediaScreen
    {
        [Header("出力先")]
        [Tooltip("映像を映す Renderer。未設定なら自分と子から探す")]
        [SerializeField] private Renderer _surface;

        [Tooltip("音を出す AudioSource。未設定なら自分と子から探す")]
        [SerializeField] private AudioSource _speaker;

        public Renderer Surface
        {
            get
            {
                if (_surface == null) _surface = GetComponentInChildren<Renderer>(true);
                return _surface;
            }
        }

        public AudioSource Speaker
        {
            get
            {
                if (_speaker == null) _speaker = GetComponentInChildren<AudioSource>(true);
                return _speaker;
            }
        }

        public void Bind(MediaPlayerContext context)
        {
            // 出力先は再生の中身を知りません。受け取るだけで何もしません。
        }

        public override string ToString()
        {
            return $"MediaScreen(映像: {(Surface != null ? Surface.name : "なし")}"
                   + $" / 音: {(Speaker != null ? Speaker.name : "なし")})";
        }
    }
}
