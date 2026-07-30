using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace SmartMediaPlatform.World.Udon.UI
{
    /// <summary>
    /// <b>再生操作のボタン群。</b>Phase5-3。
    ///
    /// <b>再生の判断は 1 つも持っていません。</b>
    /// <see cref="UdonMediaController"/> への<b>中継だけ</b>です
    /// (次に何を再生するかは今までどおり <see cref="UdonPlayerSession"/> が決めます)。
    ///
    /// <b>なぜ Controller を直接 onClick に挿さないのか</b><br/>
    /// 挿しても動きます。それでもここを 1 枚かませているのは
    /// <list type="bullet">
    /// <item>再生 / 一時停止の<b>見出しを状態に合わせて書き換える</b>場所が要る</item>
    /// <item>音量は <see cref="UdonMediaScreen"/> 側にあり、<b>行き先が 2 つに割れる</b></item>
    /// </list>
    /// ためです。ボタンの挿し先をこの 1 つにまとめておくと、
    /// <b>パネルを作り替えても挿し直す先が変わりません</b>。
    ///
    /// メソッドはすべて<b>引数なしの public</b> です。
    /// uGUI の <c>Button.onClick</c> にも <c>SendCustomEvent</c> にもそのまま挿せます。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonTransportView : UdonSharpBehaviour
    {
        [Header("つなぎ先(UdonMediaPanel が自動で入れる)")]
        [Tooltip("操作を伝える窓口")]
        public UdonMediaController Controller;

        [Tooltip("状態を読む相手(見出しの出し分けに使う)")]
        public UdonPlayerSession Session;

        [Tooltip("音量の行き先")]
        public UdonMediaScreen Screen;

        [Tooltip("状態の 1 行を出す先")]
        public UdonMediaPanel Panel;

        [Header("見出し(空でも動く)")]
        [Tooltip("再生 / 一時停止ボタンの文字")]
        public Text PlayPauseLabel;

        [Tooltip("止まっているときに出す文字")]
        public string PlayLabel = "▶  再生";

        [Tooltip("鳴っているときに出す文字")]
        public string PauseLabel = "‖  一時停止";

        [Tooltip("音量の表示")]
        public Text VolumeText;

        [Header("音量つまみ(空でも動く)")]
        [Tooltip("onValueChanged から OnVolumeSliderChanged を呼ぶこと")]
        public Slider VolumeSlider;

        [Tooltip("＋ / − 1 回ぶんの変化量")]
        [Range(0.02f, 0.5f)]
        public float VolumeStep = 0.1f;

        [Header("二重発火よけ")]
        [Tooltip("同じ操作をこの秒数以内に 2 回受けたら 2 回目を捨てる。0 で無効")]
        public float DoubleFireGuard = 0.25f;

        // つまみを Refresh で書き戻すときに onValueChanged が跳ね返るのを防ぐ
        private bool _writingSlider;

        // 直近に受けた操作(二重発火よけ)
        private int _lastAction = -1;
        private float _lastActionAt = -999f;

        // ───────── 再生制御(Button.onClick / Interact のどちらからも)─────────

        public void TogglePlayPause()
        {
            if (Controller == null || !Accept(0)) return;

            Controller.TogglePlayPause();
            Report(Controller.LastResult, "再生 / 一時停止");
        }

        public void Play()
        {
            if (Controller == null || !Accept(1)) return;

            Controller.Play();
            Report(Controller.LastResult, "再生");
        }

        public void Next()
        {
            if (Controller == null || !Accept(2)) return;

            Controller.Next();
            Report(Controller.LastResult, "次へ");
        }

        public void Previous()
        {
            if (Controller == null || !Accept(3)) return;

            Controller.Previous();
            Report(Controller.LastResult, "前へ");
        }

        public void Stop()
        {
            if (Controller == null || !Accept(4)) return;

            Controller.Stop();
            Report(Controller.LastResult, "停止");
        }

        public void ClearUpcoming()
        {
            if (Controller == null || !Accept(5)) return;

            Controller.ClearUpcoming();
            Report(Controller.LastResult, "Queue の掃除");
        }

        // ───────── 音量 ─────────

        public void VolumeUp()
        {
            if (Screen == null || !Accept(6)) return;

            Screen.SetVolume(Screen.Volume + VolumeStep);
            AfterVolumeChanged();
        }

        public void VolumeDown()
        {
            if (Screen == null || !Accept(7)) return;

            Screen.SetVolume(Screen.Volume - VolumeStep);
            AfterVolumeChanged();
        }

        /// <summary><c>Slider.onValueChanged</c> から呼ぶ(引数は取らず、つまみを読む)。</summary>
        public void OnVolumeSliderChanged()
        {
            if (_writingSlider) return;
            if (Screen == null || VolumeSlider == null) return;

            Screen.SetVolume(VolumeSlider.value);
            AfterVolumeChanged();
        }

        // ───────── 表示 ─────────

        /// <summary>画面を書き直す。<see cref="UdonMediaPanel"/> から呼ばれる。</summary>
        public void Refresh()
        {
            if (PlayPauseLabel != null)
            {
                bool playing = Session != null && Session.IsPlaying;
                string label = playing ? PauseLabel : PlayLabel;
                if (PlayPauseLabel.text != label) PlayPauseLabel.text = label;
            }

            RefreshVolume();
        }

        // ───────── 内部 ─────────

        /// <summary>
        /// 同じ操作が続けて 2 回来たら、2 回目を捨てる。
        ///
        /// ボタンは uGUI(<c>Button.onClick</c>)と VRChat の「使う」(<c>Interact</c>)の
        /// <b>両方から押せる</b>ようにしてあります。実機では
        /// ワールド内 uGUI のレイキャストが通らないことがあるためです。
        /// 両方が同時に反応した場合に「次へ」が 2 曲進んでしまうので、
        /// <b>同じ操作が着地する 1 か所</b>であるここで抑えます。
        /// </summary>
        private bool Accept(int action)
        {
            if (DoubleFireGuard <= 0f) return true;

            if (action == _lastAction && Time.time - _lastActionAt < DoubleFireGuard) return false;

            _lastAction = action;
            _lastActionAt = Time.time;
            return true;
        }

        private void AfterVolumeChanged()
        {
            RefreshVolume();
            if (Screen != null) Report(true, "音量 " + Percent(Screen.Volume));
        }

        private void RefreshVolume()
        {
            if (Screen == null) return;

            if (VolumeText != null)
            {
                string label = "音量 " + Percent(Screen.Volume);
                if (VolumeText.text != label) VolumeText.text = label;
            }

            if (VolumeSlider != null && VolumeSlider.value != Screen.Volume)
            {
                // 書き戻しで onValueChanged が走っても、二重に音量を触らないようにする
                _writingSlider = true;
                VolumeSlider.value = Screen.Volume;
                _writingSlider = false;
            }
        }

        private string Percent(float value)
        {
            return "" + Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";
        }

        private void Report(bool ok, string what)
        {
            if (Panel == null) return;
            Panel.SetStatus(ok ? what + " しました。" : what + " できませんでした。");
        }
    }
}
