using UdonSharp;
using UnityEngine;

namespace SmartMediaPlatform.World.Udon
{
    /// <summary>
    /// <b>映像と音の出力先。</b>Phase5-1 の <c>MediaScreen</c> の Udon 版。
    ///
    /// <b>再生の判断は 1 つも持っていません。</b>持っているのは
    /// 「どこに映すか(<see cref="Surface"/>)」と「どこから鳴らすか(<see cref="Speaker"/>)」だけです。
    /// だから<b>この画面ごと差し替えても、下は 1 行も変わりません</b>。
    ///
    /// <b>実際の映像の配線は SDK 側が行います。</b>
    /// AVPro は <c>VRCAVProVideoScreen</c> / <c>VRCAVProVideoSpeaker</c> を
    /// 出力先の GameObject に置いて動画プレイヤーを指す方式なので、
    /// スクリプトから毎フレーム何かする必要はありません。
    /// このクラスは<b>「どれが出力先か」を 1 か所にまとめて持つ</b>役です
    /// (音量を触りたいときの窓口でもあります)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonMediaScreen : UdonSharpBehaviour
    {
        [Tooltip("映像を映す面(Prefab では Screen/Surface の Renderer)")]
        public Renderer Surface;

        [Tooltip("音を鳴らす場所(Prefab では Screen/Surface の AudioSource)")]
        public AudioSource Speaker;

        [Tooltip("既定の音量(0〜1)")]
        [Range(0f, 1f)]
        public float Volume = 0.6f;

        // 曲の切り替わりで一時的に掛ける倍率(0〜1)。Phase8-3。
        // <see cref="Volume"/>(人が決めた音量)とは別に持ち、スピーカーへは掛け算で出す。
        private float _fadeLevel = 1f;

        void Start()
        {
            ApplyVolume();
        }

        /// <summary>
        /// いまの音量をスピーカーへ反映する。
        ///
        /// <b>スピーカーの音量を書くのはここだけです</b>(Phase8-3)。
        /// 書く所が複数あると、どれかが倍率を無視して上書きします。
        /// 以前のクロスフェードでは、読み込み完了の合図で呼ばれるたびに
        /// 人が決めた音量へ戻され、<b>フェードインせずいきなり全開で鳴る</b>原因になっていました。
        /// </summary>
        public void ApplyVolume()
        {
            if (Speaker == null) return;
            Speaker.volume = Mathf.Clamp01(Volume) * _fadeLevel;
        }

        /// <summary>
        /// <b>曲の切り替わりで掛ける倍率を変える。</b><c>UdonTrackFader</c> から呼ばれます。
        /// 人が決めた <see cref="Volume"/> は変えないので、音量バーの表示は動きません。
        /// </summary>
        public void SetFadeLevel(float level)
        {
            float clamped = Mathf.Clamp01(level);
            if (clamped == _fadeLevel) return;

            _fadeLevel = clamped;
            ApplyVolume();
        }

        /// <summary>いま掛けている倍率(診断用)。</summary>
        public float FadeLevel
        {
            get { return _fadeLevel; }
        }

        /// <summary>音量を変える(0〜1)。</summary>
        public void SetVolume(float value)
        {
            Volume = Mathf.Clamp01(value);
            ApplyVolume();
        }

        /// <summary>少し上げる。ボタンからそのまま呼べる。</summary>
        public void VolumeUp()
        {
            SetVolume(Volume + 0.1f);
        }

        /// <summary>少し下げる。ボタンからそのまま呼べる。</summary>
        public void VolumeDown()
        {
            SetVolume(Volume - 0.1f);
        }

        /// <summary>配線が済んでいるか(診断用)。</summary>
        public bool IsReady
        {
            get { return Surface != null && Speaker != null; }
        }
    }
}
