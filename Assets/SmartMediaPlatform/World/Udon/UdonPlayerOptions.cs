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

        [Tooltip("URL の状態を出す先。空でも動く")]
        public Text UrlStatus;

        [Tooltip("URL の状態を、パネルの状態の行にも出す(壁パネルでは URL 欄が帯の下にあり、専用の行を持たないため)")]
        public SmartMediaPlatform.World.Udon.UI.UdonMediaPanel Panel;

        [Header("表示")]
        public Text RepeatLabel;
        public Text SleepLabel;

        [Header("誰が操作できるか(2026-09-28)")]
        [Tooltip("同期の担当。空なら切り替えのボタンは何もしない。UdonMediaPanel が自動で入れる")]
        public UdonSyncCoordinator Sync;

        [Tooltip("「操作できる人:誰でも」などを出す所。空でも動く")]
        public Text AccessLabel;

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
        private string[] _externalText;
        private int _externalCount;

        // いま流している URL(同期で全員へ配るため)。カタログの曲を流しているときは使わない。
        private VRCUrl _current;

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
            _externalText = new string[ExternalCapacity];
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

            if (!BeginControl()) return;

            if (StartExternal(url)) SetStatus("URL の動画を再生します");
            else SetStatus("再生できませんでした");

            EndControl();
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

            // ── 同じ URL を続けて積まない。
            //    URL 欄は Udon から空にできないので、押し直すと同じものが何度も入ります。
            string text = url.Get();
            for (int i = 0; i < _externalCount; i++)
            {
                if (_externalText[i] == text)
                {
                    SetStatus("この URL はもう再生予定に入っています");
                    return;
                }
            }

            if (_externalCount >= ExternalCapacity)
            {
                SetStatus("URL はこれ以上積めません(" + ExternalCapacity + " 件まで)");
                return;
            }

            if (!BeginControl()) return;

            _external[_externalCount] = url;
            _externalText[_externalCount] = text;
            _externalCount++;

            SetStatus("URL を再生予定に入れました(URL " + _externalCount + " 件)");

            EndControl();
        }

        // ───────── 同期(2026-09-28)─────────
        //
        // URL は番号を持たないので、<b>URL そのもの</b>を同期で配ります。
        // ここは VRCUrlInputField で人が打ち込んだものを<b>受け渡すだけ</b>で、
        // 文字列から URL を作ることはしません(VideoBackend 以外で VRCUrl を持つ唯一の例外)。

        /// <summary>
        /// 操作の前に、同期の持ち主になる。操作できる人でなければ理由を出して false。
        /// 同期していなければ何もせず true。
        /// </summary>
        private bool BeginControl()
        {
            if (Sync == null || !Sync.Enabled) return true;
            if (Sync.TakeControl()) return true;

            SetStatus(Sync.DenyReason());
            return false;
        }

        /// <summary>操作のあと、いまの状態を全員へ配る。</summary>
        private void EndControl()
        {
            if (Sync != null && Sync.Enabled) Sync.Capture();
        }

        /// <summary>いま流している URL。URL を流していなければ null。</summary>
        public VRCUrl CurrentUrl
        {
            get { return Session != null && Session.IsExternal ? _current : null; }
        }

        /// <summary>あとで流す URL を写し取る(持ち主が配るため)。</summary>
        public VRCUrl[] SnapshotExternal()
        {
            EnsureInitialized();

            VRCUrl[] copy = new VRCUrl[_externalCount];
            for (int i = 0; i < _externalCount; i++) copy[i] = _external[i];
            return copy;
        }

        /// <summary>
        /// <b>同期で届いた「あとで流す URL」をそのまま当てる。</b>持ち主以外の手元で呼ばれます。
        /// 持ち主が代わったとき、新しい持ち主がそのまま続きを流せるようにするためです。
        /// </summary>
        public void ApplySyncedExternal(VRCUrl[] urls)
        {
            EnsureInitialized();

            int count = urls == null ? 0 : urls.Length;
            if (count > ExternalCapacity) count = ExternalCapacity;

            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                VRCUrl url = urls[i];
                if (url == null) continue;

                string text = url.Get();
                if (text == null || text.Length == 0) continue;

                _external[kept] = url;
                _externalText[kept] = text;
                kept++;
            }

            for (int i = kept; i < _externalCount; i++)
            {
                _external[i] = null;
                _externalText[i] = null;
            }
            _externalCount = kept;
        }

        /// <summary>
        /// <b>同期で「持ち主がこの URL を流し始めた」と届いた。</b>手元でも同じ URL を読み込みます。
        /// <paramref name="serial"/> は持ち主の番号で、次に同じ番号が届いても読み直しません。
        /// </summary>
        public bool PlayFromSync(VRCUrl url, int serial)
        {
            if (url == null || Session == null) return false;

            bool ok = StartExternal(url);
            Session.ApplySyncedExternal(url.Get(), serial);
            return ok;
        }

        /// <summary>
        /// <b>あとで流す URL があれば 1 つ取り出して鳴らす。</b>
        /// 曲が終わったとき・「次へ」を押したときに <see cref="UdonPlayerSession"/> から聞かれます。
        /// </summary>
        /// <returns>鳴らしたら true。</returns>
        public bool TryPlayNextExternal()
        {
            EnsureInitialized();
            if (_externalCount <= 0) return false;

            return PlayExternalAt(0);
        }

        /// <summary>
        /// <b>再生予定の <paramref name="position"/> 番目の URL を、いま流す。</b>
        /// 一覧から外してから鳴らします(鳴っているものは再生予定に残さない、Phase7-3 と同じ)。
        /// </summary>
        public bool PlayExternalAt(int position)
        {
            EnsureInitialized();
            if (position < 0 || position >= _externalCount) return false;

            VRCUrl url = _external[position];
            RemoveExternalAt(position);

            if (url == null) return false;
            return StartExternal(url);
        }

        /// <summary>再生予定から URL を 1 つ外す。</summary>
        public bool RemoveExternalAt(int position)
        {
            EnsureInitialized();
            if (position < 0 || position >= _externalCount) return false;

            for (int i = position; i < _externalCount - 1; i++)
            {
                _external[i] = _external[i + 1];
                _externalText[i] = _externalText[i + 1];
            }

            _externalCount--;
            _external[_externalCount] = null;
            _externalText[_externalCount] = null;
            return true;
        }

        /// <summary>あとで流す URL を全部外す。外した数を返す。</summary>
        public int ClearExternal()
        {
            EnsureInitialized();

            int removed = _externalCount;
            for (int i = 0; i < _externalCount; i++)
            {
                _external[i] = null;
                _externalText[i] = null;
            }
            _externalCount = 0;
            return removed;
        }

        /// <summary>あとで流す URL の数。</summary>
        public int ExternalCount
        {
            get { EnsureInitialized(); return _externalCount; }
        }

        /// <summary>再生予定の一覧に出す文字(打ち込まれた URL そのもの)。</summary>
        public string ExternalTextAt(int position)
        {
            EnsureInitialized();
            if (position < 0 || position >= _externalCount) return "";

            string text = _externalText[position];
            return text == null ? "" : text;
        }

        /// <summary>URL を鳴らす。「いま鳴っているもの」を URL に切り替えてから、動画プレイヤーへ渡す。</summary>
        private bool StartExternal(VRCUrl url)
        {
            // カタログの曲ではないので、上位の「いま鳴っているもの」を URL に切り替えます。
            // 重ねている最中なら、ここで裏の曲がやめになります。
            // だから動画プレイヤーは<b>そのあとで</b>選びます(先に選ぶと裏を掴むことがある)。
            if (Session != null) Session.NotifyExternalPlayback(url.Get());
            _current = url;

            UdonVideoBackend backend = ResolveBackend();
            if (backend == null) return false;

            return backend.PlayExternal(url);
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

        // ───────── 誰が操作できるか ─────────

        /// <summary>
        /// <b>誰でも → マスターだけ → いまの操作者だけ</b> と送る。マスターだけが押せます。
        /// 判断と配るのは <see cref="UdonSyncCoordinator.CycleAccessPolicy"/> の仕事で、ここは頼むだけです。
        /// </summary>
        public void CycleAccess()
        {
            if (Sync == null || !Sync.Enabled)
            {
                SetStatus("同期していないので、切り替えるものがありません");
                return;
            }

            if (!Sync.IsLocalMaster())
            {
                SetStatus("操作できる人を変えられるのは、マスターだけです");
                return;
            }

            if (Sync.CycleAccessPolicy()) SetStatus(Sync.AccessLabel() + " にしました");
            RefreshLabels();
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
        /// <b>この曲で止める予定か</b>(覗くだけで、予定は消さない)。Phase8-5。
        /// 重ねるクロスフェードが、次の曲を裏で鳴らしてよいかを決めるのに使います。
        /// </summary>
        public bool WillStopAfterTrack()
        {
            return SleepMinutes == SleepAfterTrack;
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

            // 誰が操作できるかは、マスターが切り替えると同期で変わるので、書き直しのたびに合わせる。
            if (AccessLabel != null)
            {
                string access = Sync != null ? Sync.AccessLabel() : "操作できる人:同期なし";
                if (AccessLabel.text != access) AccessLabel.text = access;
            }
        }

        private void SetStatus(string text)
        {
            if (UrlStatus != null && UrlStatus.text != text) UrlStatus.text = text;
            if (Panel != null) Panel.SetStatus(text);
        }

        private UdonVideoBackend ResolveBackend()
        {
            // 動画プレイヤーが 2 つあるときは入れ替わるので、いま鳴っているほうを先に聞く(Phase8-5)。
            if (Session != null)
            {
                UdonVideoBackend active = Session.ActiveBackend();
                if (active != null) return active;
            }
            if (Backend != null) return Backend;
            return null;
        }
    }
}
