using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace SmartMediaPlatform.World.Udon
{
    /// <summary>
    /// <b>みんなで同じものを、同じところで見るための層。</b>Phase5-4。
    ///
    /// <b>再生の判断は 1 つも持っていません。</b>やることは 3 つだけです:
    /// <list type="number">
    /// <item>持ち主(Owner)の <see cref="UdonPlayerSession"/> の状態を<b>写し取って配る</b></item>
    /// <item>受け取った状態を<b>そのまま当てる</b></item>
    /// <item>再生位置がずれていたら<b>合わせ直す</b></item>
    /// </list>
    /// 次に何を再生するかは今までどおり <see cref="UdonPlayerSession"/> が決めます。
    /// URL も見ません(見るのは <see cref="UdonVideoBackend"/> だけ)。
    ///
    /// <b>なぜ PlayerSession に [UdonSynced] を付けなかったのか</b><br/>
    /// 付ければ手数は減りますが、<b>再生の判断をする場所と、
    /// ネットワークの都合を扱う場所が混ざります</b>。
    /// 分けておくと
    /// <list type="bullet">
    /// <item><see cref="UdonPlayerSession"/> は今までどおり EditMode で検証できる
    ///       (<c>PlaybackModel</c> が正典のまま)</item>
    /// <item>同期のやり方を変えても、判断のコードは 1 行も動かない</item>
    /// <item>この層を外せば Phase5-3 と同じ「1 人用」に戻る</item>
    /// </list>
    /// が保てます。実際、<see cref="Enabled"/> を false にすると完全にソロ動作へ戻ります。
    ///
    /// <b>同期するのは 5 つだけです。</b>
    /// <list type="bullet">
    /// <item><see cref="_queue"/> …… catalog index の並び。<b>先頭がいま鳴っているもの</b></item>
    /// <item><see cref="_playing"/> …… 再生中かどうか</item>
    /// <item><see cref="_baseServerTime"/> / <see cref="_basePositionMs"/> …… 位置の基準</item>
    /// <item><see cref="_revision"/> …… 変わったことの目印</item>
    /// </list>
    /// <b>再生位置そのものは流しません。</b>「いつ、どこだったか」を 1 回配れば、
    /// あとは各自がサーバー時刻から計算できます
    /// (計算の正典は <see cref="SmartMediaPlatform.World.UdonModel.PlaybackClockModel"/>、
    ///  EditMode 検証済み)。
    /// 同じ理由で「いま鳴っているもの」も別に持ちません — Queue の先頭がそれだからです。
    ///
    /// <b>Catalog Builder / サーバー連携が来ても、ここは変わりません。</b>
    /// 配っているのは catalog index だけで、カタログの作り方も URL も知らないためです。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class UdonSyncCoordinator : UdonSharpBehaviour
    {
        /// <summary>誰でも操作できる(押した人が持ち主になる)。</summary>
        public const int AccessEveryone = 0;

        /// <summary>インスタンスマスターだけが操作できる。</summary>
        public const int AccessMasterOnly = 1;

        /// <summary>いまの持ち主だけが操作できる(手放すまで独占)。</summary>
        public const int AccessOwnerOnly = 2;

        [Header("つなぎ先")]
        [Tooltip("状態を読み書きする相手")]
        public UdonPlayerSession Session;

        [Tooltip("実際に鳴らす相手(位置合わせに使う)")]
        public UdonVideoBackend Backend;

        [Tooltip("画面へ「書き直して」と伝える窓口")]
        public UdonMediaController Controller;

        [Header("同期")]
        [Tooltip("false にすると完全に 1 人用に戻る(Phase5-3 と同じ動き)")]
        public bool Enabled = true;

        [Tooltip("0=誰でも / 1=マスターだけ / 2=いまの持ち主だけ")]
        public int AccessPolicy = AccessEveryone;

        [Tooltip("この秒数より大きくずれていたら合わせ直す")]
        [Range(0.2f, 5f)]
        public float DriftTolerance = 1.0f;

        [Tooltip("ずれを見に行く間隔(秒)")]
        [Range(0.25f, 10f)]
        public float CheckInterval = 1.0f;

        [Tooltip("同期の様子を Console に出す")]
        public bool LogSync;

        // ───────── 同期する値(これで全部)─────────

        // catalog index の並び。先頭 = いま鳴っているもの。
        // 「いま何を鳴らしているか」を別に持たないのは、先頭がそれだからです。
        [UdonSynced] private int[] _queue = new int[0];

        [UdonSynced] private bool _playing;

        // この「サーバー時刻」のとき、再生位置は「基準位置」だった。
        // 2 つあれば、いつ入ってきた人でも位置を計算できます。
        [UdonSynced] private int _baseServerTime;
        [UdonSynced] private int _basePositionMs;

        // 変わったことの目印。中身が同じでも「配り直した」と分かるようにする。
        [UdonSynced] private int _revision;

        // ───────── 手元だけの状態 ─────────

        private int _appliedRevision = -1;
        private int _appliedMedia = -1;
        private int _capturedMedia = -1;
        private float _nextCheck;
        private bool _initialized;

        void Start()
        {
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            if (_queue == null) _queue = new int[0];

            // 動画の終了で次へ進んでよいのは持ち主だけ。
            // (終了イベントは全員の手元で別々に起きるため)
            ApplyAdvancePermission();
        }

        // ───────── 誰が操作できるか ─────────

        /// <summary>いまこのプレイヤーが持ち主か。</summary>
        public bool IsOwner()
        {
            if (!Enabled) return true;
            return Networking.IsOwner(Networking.LocalPlayer, gameObject);
        }

        /// <summary>いま操作してよいか(押す前の判定)。</summary>
        public bool CanOperate()
        {
            if (!Enabled) return true;

            if (AccessPolicy == AccessMasterOnly)
            {
                VRCPlayerApi local = Networking.LocalPlayer;
                return local != null && local.isMaster;
            }

            if (AccessPolicy == AccessOwnerOnly) return IsOwner();

            return true;
        }

        /// <summary>操作できないときに画面へ出す理由。</summary>
        public string DenyReason()
        {
            if (AccessPolicy == AccessMasterOnly) return "いまはマスターだけが操作できます";
            if (AccessPolicy == AccessOwnerOnly) return "いまは別の人が操作しています";
            return "いまは操作できません";
        }

        /// <summary>
        /// 操作の前に持ち主を取る。取れなければ false(操作させない)。
        ///
        /// <b>VRChat では所有権はその場で移ります</b>ので、
        /// 呼んだ直後から <see cref="IsOwner"/> は true になります。
        /// </summary>
        public bool TakeControl()
        {
            EnsureInitialized();
            if (!Enabled) return true;
            if (!CanOperate()) return false;

            if (!IsOwner())
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
                ApplyAdvancePermission();
            }
            return true;
        }

        /// <summary>持ち主が変わった。全員の手元で呼ばれる。</summary>
        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            ApplyAdvancePermission();

            if (LogSync)
            {
                string name = player != null ? player.displayName : "(不明)";
                Debug.Log("[UdonSyncCoordinator] 操作者が " + name + " になりました。", gameObject);
            }

            if (Controller != null) Controller.NotifyChanged();
        }

        /// <summary>
        /// 所有権を渡してよいか。<see cref="AccessOwnerOnly"/> のときだけ断ります。
        /// </summary>
        public override bool OnOwnershipRequest(VRCPlayerApi requester, VRCPlayerApi newOwner)
        {
            if (!Enabled) return true;
            return AccessPolicy != AccessOwnerOnly;
        }

        // ───────── 配る(持ち主だけ)─────────

        /// <summary>
        /// いまの状態を写し取って全員へ配る。操作のたびに窓口から呼ばれます。
        ///
        /// <b>再生位置は「いつ、どこだったか」の形で配ります。</b>
        /// 毎フレーム位置を流すのに比べて、通信は<b>操作したときの 1 回だけ</b>で済みます。
        /// </summary>
        public void Capture()
        {
            EnsureInitialized();
            if (!Enabled || Session == null) return;
            if (!IsOwner()) return;

            _queue = Session.SnapshotQueue();
            _playing = Session.IsPlaying;

            int current = Session.CurrentIndex;

            // 鳴らすものが変わったなら頭から。変わっていないなら、いまの位置を基準にする
            // (一時停止・再開はこれで表せる)。
            if (current != _capturedMedia)
            {
                _basePositionMs = 0;
                _capturedMedia = current;
            }
            else
            {
                float seconds = Backend != null ? Backend.GetTime() : 0f;
                _basePositionMs = seconds > 0f ? (int)(seconds * 1000f) : 0;
            }

            _baseServerTime = Networking.GetServerTimeInMilliseconds();
            _revision++;

            _appliedRevision = _revision;
            _appliedMedia = current;

            RequestSerialization();

            if (LogSync) Debug.Log("[UdonSyncCoordinator] 配信 " + Describe(), gameObject);
        }

        // ───────── 受け取る(持ち主以外)─────────

        /// <summary>
        /// 同期が届いた。<b>途中参加もここを通ります</b>
        /// (VRChat は入室したプレイヤーへ現在の値を 1 回送ってくれる)。
        /// </summary>
        public override void OnDeserialization()
        {
            Apply();
        }

        /// <summary>受け取った状態を当てる。</summary>
        public void Apply()
        {
            EnsureInitialized();
            if (!Enabled || Session == null) return;

            // 1. Queue と再生状態をそのまま写す(判断はしない)
            int current = _queue != null && _queue.Length > 0 ? _queue[0] : -1;
            Session.ApplySyncedState(_queue, _playing, current);

            // 2. 鳴らすものが変わったなら読み直す
            if (current != _appliedMedia)
            {
                _appliedMedia = current;
                _capturedMedia = current;

                if (Backend != null)
                {
                    if (current >= 0) Backend.LoadAndPlay(current);
                    else Backend.Stop();
                }
            }
            else if (Backend != null && current >= 0)
            {
                // 3. 同じものなら、再生 / 一時停止だけ合わせる
                if (_playing) Backend.Play();
                else Backend.Pause();
            }

            _appliedRevision = _revision;

            // 4. 位置合わせは次の見回りに任せる。
            //    読み込みが終わるまで seek は効かないので、ここで急いでも無駄になる。
            _nextCheck = 0f;

            if (Controller != null) Controller.NotifyChanged();

            if (LogSync) Debug.Log("[UdonSyncCoordinator] 受信 " + Describe(), gameObject);
        }

        // ───────── 位置を合わせ続ける ─────────

        void Update()
        {
            if (!Enabled) return;
            if (CheckInterval <= 0f) return;
            if (Time.time < _nextCheck) return;

            _nextCheck = Time.time + CheckInterval;

            WatchOwnerChanges();
            CorrectDrift();
        }

        /// <summary>
        /// 持ち主の状態が勝手に変わっていたら配り直す。
        ///
        /// 動画が終わって次へ進んだ、失敗して飛ばした —— こうした変化は
        /// ボタンを通らないので、<see cref="Capture"/> が呼ばれません。
        /// <b>見回って拾う</b>ことで、どんな経路の変化でも同期に乗ります。
        /// </summary>
        private void WatchOwnerChanges()
        {
            if (Session == null || !IsOwner()) return;

            if (Session.CurrentIndex != _appliedMedia
                || Session.IsPlaying != _playing
                || QueueChanged())
            {
                Capture();
            }
        }

        private bool QueueChanged()
        {
            int count = Session.QueueCount;
            if (_queue == null) return count != 0;
            if (_queue.Length != count) return true;

            for (int i = 0; i < count; i++)
            {
                if (_queue[i] != Session.GetQueueAt(i)) return true;
            }
            return false;
        }

        /// <summary>
        /// ずれていたら合わせ直す。
        /// <see cref="SmartMediaPlatform.World.UdonModel.PlaybackClockModel.NeedsCorrection"/> の写しです。
        /// </summary>
        private void CorrectDrift()
        {
            if (!_playing) return;
            if (Backend == null || Session == null) return;
            if (Session.CurrentIndex < 0) return;

            // 読み込み中は GetTime が意味を持たないので触らない。
            // 次の見回りでまた見に来るので、読み込みが終われば自然に揃う。
            if (!Backend.IsPlaying) return;

            int expected = ExpectedPositionMs();
            int actual = (int)(Backend.GetTime() * 1000f);

            int drift = actual - expected;
            if (drift < 0) drift = -drift;

            int tolerance = (int)(DriftTolerance * 1000f);
            if (drift <= tolerance) return;

            if (!Backend.SetTime(expected * 0.001f)) return;

            if (LogSync)
            {
                Debug.Log("[UdonSyncCoordinator] " + (drift / 1000f)
                          + " 秒ずれていたので合わせました。", gameObject);
            }
        }

        /// <summary>
        /// いま何 ms 目のはずか。
        ///
        /// <b>サーバー時刻は int で、約 24.8 日で一周します。</b>
        /// 引き算だけで扱えば一周をまたいでも正しい差になるので、
        /// 大小比較はしません(<c>PlaybackClockModel</c> と同じ)。
        /// </summary>
        public int ExpectedPositionMs()
        {
            if (!_playing) return _basePositionMs;

            int elapsed = Networking.GetServerTimeInMilliseconds() - _baseServerTime;
            int position = _basePositionMs + elapsed;

            return position < 0 ? 0 : position;
        }

        /// <summary>いま何秒目のはずか。</summary>
        public float ExpectedPositionSeconds()
        {
            return ExpectedPositionMs() * 0.001f;
        }

        // ───────── ボタンからそのまま呼べる ─────────

        /// <summary>いまの同期状態へ強制的に合わせ直す(「ずれた」ときの手動リセット)。</summary>
        public void Resync()
        {
            EnsureInitialized();
            if (!Enabled) return;

            if (IsOwner())
            {
                Capture();
                return;
            }

            _appliedMedia = -1;   // 読み直させる
            Apply();
        }

        // ───────── 内部 ─────────

        /// <summary>
        /// 動画の終了で次へ進んでよいのは持ち主だけ。
        ///
        /// <c>OnVideoEnd</c> は<b>全員の手元で別々に起きます</b>。
        /// 全員が <c>Next()</c> すると、人によって違うものが鳴り始めるので、
        /// 進むのは持ち主だけにして、残りは同期で追いつきます。
        /// </summary>
        private void ApplyAdvancePermission()
        {
            if (Session == null) return;
            Session.AutoAdvance = !Enabled || IsOwner();
        }

        /// <summary>Console 表示用の 1 行。</summary>
        public string Describe()
        {
            int count = _queue == null ? 0 : _queue.Length;
            int current = count > 0 ? _queue[0] : -1;

            return "rev " + _revision + " / 曲 " + current + " / Queue " + count + " 件 / "
                   + (_playing ? "再生中" : "停止") + " / "
                   + (ExpectedPositionMs() / 1000) + " 秒目 / "
                   + (IsOwner() ? "自分が操作者" : "他の人が操作者");
        }

        /// <summary>いま操作している人の名前。画面に出す用。</summary>
        public string OwnerName()
        {
            if (!Enabled) return "";

            VRCPlayerApi owner = Networking.GetOwner(gameObject);
            return owner != null ? owner.displayName : "";
        }
    }
}
