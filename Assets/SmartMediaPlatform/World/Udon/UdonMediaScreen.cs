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
    ///
    /// ───────────────────────────────────────────────
    /// <b>2 系統(Phase8-5 / 重ねるクロスフェード)</b>
    ///
    /// 動画プレイヤーが 2 つあるときは、画面も音の出口も 2 つずつ持ちます
    /// (<see cref="SurfaceB"/> / <see cref="SpeakerB"/>)。
    /// <list type="bullet">
    /// <item><b>画面</b>は 2 枚を同じ場所に重ね、<b>どちらか 1 枚だけを表示</b>します
    ///       (<see cref="ShowChannel"/>)。混ぜる専用シェーダーは使いません —
    ///       Phase7-5 で画面が真っ白になった原因がそれでした。</item>
    /// <item><b>音</b>はそれぞれ別の場所から出し、倍率を別々に掛けます。</item>
    /// </list>
    /// B が空なら、今までどおりの 1 系統として動きます。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonMediaScreen : UdonSharpBehaviour
    {
        [Tooltip("映像を映す面(Prefab では Screen/Surface の Renderer)")]
        public Renderer Surface;

        [Tooltip("音を鳴らす場所(Prefab では Screen/Speaker の AudioSource)")]
        public AudioSource Speaker;

        [Header("2 系統目(Phase8-5。空なら 1 系統)")]
        [Tooltip("2 つめの動画プレイヤーが映す面。Surface と同じ場所に重ねて置く")]
        public Renderer SurfaceB;

        [Tooltip("2 つめの動画プレイヤーの音の出口。Speaker とは別の GameObject に置く")]
        public AudioSource SpeakerB;

        [Tooltip("既定の音量(0〜1)")]
        [Range(0f, 1f)]
        public float Volume = 0.6f;

        [Tooltip("倍率を「等パワー」の曲線で掛ける(Phase8-5)。"
                 + "2 曲を重ねたとき、真ん中で音が痩せないようにする。クロスフェードの担当が入れる")]
        public bool EqualPowerCurve;

        // 曲の切り替わりで一時的に掛ける倍率(0〜1)。Phase8-3。
        // <see cref="Volume"/>(人が決めた音量)とは別に持ち、スピーカーへは掛け算で出す。
        private float _fadeLevel = 1f;
        private float _fadeLevelB = 1f;

        // いま表示している面。false = Surface、true = SurfaceB。
        private bool _showingB;

        void Start()
        {
            ApplyVolume();
            ApplyChannel();
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
            float master = Mathf.Clamp01(Volume);

            if (Speaker != null) Speaker.volume = master * Shape(_fadeLevel);
            if (SpeakerB != null) SpeakerB.volume = master * Shape(_fadeLevelB);
        }

        /// <summary>
        /// 倍率を音量に直す。
        /// <b>等パワー</b>(<c>sin(x·π/2)</c>)なら、下がるほうと上がるほうの 2 乗の和が 1 になり、
        /// 重なっている間も大きさが変わらずに聞こえます。
        /// </summary>
        private float Shape(float level)
        {
            float clamped = Mathf.Clamp01(level);
            if (!EqualPowerCurve) return clamped;
            return Mathf.Sin(clamped * Mathf.PI * 0.5f);
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

        /// <summary>2 系統目の倍率を変える(Phase8-5)。</summary>
        public void SetFadeLevelB(float level)
        {
            float clamped = Mathf.Clamp01(level);
            if (clamped == _fadeLevelB) return;

            _fadeLevelB = clamped;
            ApplyVolume();
        }

        /// <summary>いま掛けている倍率(診断用)。</summary>
        public float FadeLevel
        {
            get { return _fadeLevel; }
        }

        /// <summary>2 系統目に掛けている倍率(診断用)。</summary>
        public float FadeLevelB
        {
            get { return _fadeLevelB; }
        }

        /// <summary>
        /// <b>どちらの面を表示するか。</b>Phase8-5。true なら <see cref="SurfaceB"/>。
        ///
        /// <b>Renderer を消すだけで、GameObject は消しません。</b>
        /// 古い Prefab では音の出口が面と同じ GameObject に付いていて、
        /// GameObject ごと消すと音まで止まるためです。
        /// </summary>
        public void ShowChannel(bool showB)
        {
            if (SurfaceB == null) showB = false;
            if (showB == _showingB) return;

            _showingB = showB;
            ApplyChannel();
        }

        /// <summary>いま B の面を表示しているか(診断用)。</summary>
        public bool ShowingB
        {
            get { return _showingB; }
        }

        private void ApplyChannel()
        {
            if (Surface != null && Surface.enabled == _showingB) Surface.enabled = !_showingB;
            if (SurfaceB != null && SurfaceB.enabled != _showingB) SurfaceB.enabled = _showingB;
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

        /// <summary>2 系統ぶんの出口がそろっているか(診断用)。</summary>
        public bool HasSecondChannel
        {
            get { return SurfaceB != null && SpeakerB != null; }
        }
    }
}
