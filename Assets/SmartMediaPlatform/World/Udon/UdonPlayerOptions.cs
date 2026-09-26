using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace SmartMediaPlatform.World.Udon
{
    /// <summary>
    /// <b>「…」の中身。</b>Phase7-9。
    ///
    /// たまにしか使わないが、あると助かる 3 つを持ちます。
    /// <list type="bullet">
    /// <item>好きな YouTube の URL を流す</item>
    /// <item>繰り返し(切 / 1 曲 / 再生予定)</item>
    /// <item>おやすみタイマー</item>
    /// </list>
    ///
    /// <b>なぜ常に出さないのか</b><br/>
    /// この 3 つは<b>1 回設定したら、しばらく触りません</b>。
    /// 常に出しておくと、毎回押すもの(再生・次へ)と同じ重さで並び、
    /// <b>初めて見た人の選択肢が倍</b>になります。
    /// だから畳んでおいて、必要なときだけ下から出します。
    ///
    /// <b>URL について</b><br/>
    /// Udon では<b>文字列から URL を作れません</b>。
    /// 唯一の道が <c>VRCUrlInputField</c>(人が打ち込んだものを URL として受け取る)で、
    /// ここもそれを使っています。<b>実行時に URL を組み立ててはいません</b>。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonPlayerOptions : UdonSharpBehaviour
    {
        [Header("つなぎ先")]
        public UdonPlayerSession Session;

        [Tooltip("URL を鳴らす相手。空なら Session から借りる")]
        public UdonVideoBackend Backend;

        [Tooltip("開け閉てする板")]
        public GameObject Sheet;

        [Header("URL")]
        [Tooltip("人が URL を打ち込む欄。ここ以外から URL は作れない")]
        public VRCUrlInputField UrlField;

        [Tooltip("URL の状態を出す先")]
        public Text UrlStatus;

        [Header("表示")]
        public Text RepeatLabel;
        public Text SleepLabel;

        // ───────── 繰り返し ─────────

        /// <summary>繰り返さない。</summary>
        public const int RepeatOff = 0;

        /// <summary>いまの 1 曲を繰り返す。</summary>
        public const int RepeatOne = 1;

        /// <summary>再生予定を一周し続ける。</summary>
        public const int RepeatQueue = 2;

        [Tooltip("0 = 切 / 1 = 1 曲 / 2 = 再生予定")]
        [Range(0, 2)]
        public int RepeatMode = RepeatOff;

        // ───────── おやすみタイマー ─────────

        /// <summary>タイマーの選択肢(分)。0 は切、-1 は「この曲が終わったら」。</summary>
        public const int SleepOff = 0;

        /// <summary>この曲が終わったら止める。</summary>
        public const int SleepAfterTrack = -1;

        [Tooltip("0 = 切 / 15・30・60・90 = 分 / -1 = この曲が終わったら")]
        public int SleepMinutes = SleepOff;

        // 止める時刻(Time.time)。0 以下なら動いていない。
        private float _sleepDeadline;

        // ───────── 外から足された URL ─────────

        /// <summary>あとで流す URL を積める数。</summary>
        public const int ExternalCapacity = 8;

        private VRCUrl[] _external;
        private int _externalCount;

        private bool _initialized;

        void Start()
        {
            EnsureInitialized();
            RefreshLabels();
        }

        public void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            _external = new VRCUrl[ExternalCapacity];
        }

        // ───────── 開け閉て ─────────

        public void Toggle()
        {
            if (Sheet == null) return;

            Sheet.SetActive(!Sheet.activeSelf);
            if (Sheet.activeSelf) RefreshLabels();
        }

        public void Close()
        {
            if (Sheet != null) Sheet.SetActive(false);
        }

        /// <summary>URL 欄を選んで、VRChat のキーボードを出す。</summary>
        public void OpenUrlKeyboard()
        {
            if (UrlField == null) return;

            UrlField.Select();
            UrlField.ActivateInputField();
        }

        // ───────── URL ─────────

        /// <summary>打ち込まれた URL をすぐ流す。</summary>
        public void PlayUrl()
        {
            EnsureInitialized();

            VRCUrl url = ReadUrl();
            if (url == null)
            {
                SetStatus("URL を入れてください");
                return;
            }

            UdonVideoBackend backend = ResolveBackend();
            if (backend == null)
            {
                SetStatus("鳴らす相手がいません");
                return;
            }

            // カタログの曲ではないので、上位の「いま鳴っているもの」は空にします。
            // ここを合わせておかないと、一覧が別の曲を鳴っていることにしてしまいます。
            if (Session != null) Session.NotifyExternalPlayback();

            if (backend.PlayExternal(url)) SetStatus("再生します");
            else SetStatus("再生できませんでした");
        }

        /// <summary>打ち込まれた URL をあとで流す。</summary>
        public void EnqueueUrl()
        {
            EnsureInitialized();

            VRCUrl url = ReadUrl();
            if (url == null)
            {
                SetStatus("URL を入れてください");
                return;
            }

            if (_externalCount >= ExternalCapacity)
            {
                SetStatus("これ以上は積めません");
                return;
            }

            _external[_externalCount] = url;
            _externalCount++;

            SetStatus("あとで流します(" + _externalCount + " 件)");
        }

        /// <summary>
        /// <b>あとで流す URL があれば 1 つ取り出して鳴らす。</b>
        /// 曲が終わったときに <see cref="UdonPlayerSession"/> から聞かれます。
        /// </summary>
        /// <returns>鳴らしたら true。</returns>
        public bool TryPlayNextExternal()
        {
            EnsureInitialized();
            if (_externalCount <= 0) return false;

            VRCUrl url = _external[0];
            for (int i = 0; i < _externalCount - 1; i++) _external[i] = _external[i + 1];
            _externalCount--;

            UdonVideoBackend backend = ResolveBackend();
            if (backend == null || url == null) return false;

            if (Session != null) Session.NotifyExternalPlayback();
            return backend.PlayExternal(url);
        }

        /// <summary>あとで流す URL の数。</summary>
        public int ExternalCount
        {
            get { EnsureInitialized(); return _externalCount; }
        }

        private VRCUrl ReadUrl()
        {
            if (UrlField == null) return null;

            VRCUrl url = UrlField.GetUrl();
            if (url == null) return null;

            string text = url.Get();
            if (text == null || text.Length < 8) return null;

            return url;
        }

        // ───────── 繰り返し ─────────

        /// <summary>切 → 1 曲 → 再生予定 → 切 …… と送る。</summary>
        public void CycleRepeat()
        {
            RepeatMode++;
            if (RepeatMode > RepeatQueue) RepeatMode = RepeatOff;

            Apply();
            RefreshLabels();
        }

        public string RepeatLabelText()
        {
            if (RepeatMode == RepeatOne) return "繰り返し:1 曲";
            if (RepeatMode == RepeatQueue) return "繰り返し:再生予定";
            return "繰り返し:切";
        }

        /// <summary>いまの設定を Session へ映す。</summary>
        public void Apply()
        {
            if (Session == null) return;

            // 1 曲だけは Session が元から持っている動きなので、そこへ渡します。
            Session.EndBehaviour = RepeatMode == RepeatOne
                ? UdonPlayerSession.EndBehaviourRepeatOne
                : UdonPlayerSession.EndBehaviourRecommend;

            // 再生予定の一周は、取り出したものを後ろへ戻すことで作ります。
            Session.RepeatQueue = RepeatMode == RepeatQueue;
        }

        // ───────── おやすみタイマー ─────────

        /// <summary>切 → 15 → 30 → 60 → 90 → この曲の終わり → 切 …… と送る。</summary>
        public void CycleSleep()
        {
            if (SleepMinutes == SleepOff) SleepMinutes = 15;
            else if (SleepMinutes == 15) SleepMinutes = 30;
            else if (SleepMinutes == 30) SleepMinutes = 60;
            else if (SleepMinutes == 60) SleepMinutes = 90;
            else if (SleepMinutes == 90) SleepMinutes = SleepAfterTrack;
            else SleepMinutes = SleepOff;

            _sleepDeadline = SleepMinutes > 0 ? Time.time + SleepMinutes * 60f : 0f;
            RefreshLabels();
        }

        public string SleepLabelText()
        {
            if (SleepMinutes == SleepAfterTrack) return "おやすみ:この曲で";
            if (SleepMinutes <= 0) return "おやすみ:切";

            int remain = Mathf.CeilToInt((_sleepDeadline - Time.time) / 60f);
            if (remain < 0) remain = 0;

            return "おやすみ:あと " + remain + " 分";
        }

        /// <summary>
        /// <b>曲が終わった。止める頃合いか。</b>
        /// <see cref="UdonPlayerSession"/> から聞かれます。
        /// </summary>
        /// <returns>止めるなら true(呼び出し側は次へ進まない)。</returns>
        public bool ShouldStopAfterTrack()
        {
            if (SleepMinutes != SleepAfterTrack) return false;

            SleepMinutes = SleepOff;
            RefreshLabels();
            return true;
        }

        void Update()
        {
            if (SleepMinutes <= 0) return;
            if (_sleepDeadline <= 0f) return;
            if (Time.time < _sleepDeadline) return;

            // 時間が来た。止めて、タイマー自体も切る。
            _sleepDeadline = 0f;
            SleepMinutes = SleepOff;

            if (Session != null) Session.Stop();
            RefreshLabels();
        }

        // ───────── 表示 ─────────

        /// <summary>見出しを書き直す。<see cref="UdonMediaPanel"/> からも呼ばれます。</summary>
        public void RefreshLabels()
        {
            if (RepeatLabel != null)
            {
                string repeat = RepeatLabelText();
                if (RepeatLabel.text != repeat) RepeatLabel.text = repeat;
            }

            if (SleepLabel != null)
            {
                string sleep = SleepLabelText();
                if (SleepLabel.text != sleep) SleepLabel.text = sleep;
            }
        }

        private void SetStatus(string text)
        {
            if (UrlStatus == null) return;
            if (UrlStatus.text != text) UrlStatus.text = text;
        }

        private UdonVideoBackend ResolveBackend()
        {
            if (Backend != null) return Backend;
            if (Session != null) return Session.ActiveBackend();
            return null;
        }
    }
}
