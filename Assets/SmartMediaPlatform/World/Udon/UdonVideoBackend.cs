using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components.Video;
using VRC.SDK3.Video.Components.Base;
using SmartMediaPlatform.Catalog.Udon;

namespace SmartMediaPlatform.World.Udon
{
    /// <summary>
    /// <b>実際に動画を鳴らす唯一の場所。</b>
    /// Phase2〜3 の <c>VideoBackend</c> / <c>VRChatVideoBackend</c> /
    /// <c>VideoBackendAdapter</c> をまとめた Udon 版です。
    ///
    /// <b>URL を知ってよいのはここだけ</b>という Phase2 からの線をそのまま守ります。
    /// <see cref="UdonCatalogStore"/> には <c>GetUrl</c> がなく、
    /// <see cref="UdonMediaCatalog"/> を直接持つのはこのクラスだけです。
    /// <b>実行時に <see cref="VRCUrl"/> は作りません</b> — 編集時に焼き込まれたものを引くだけです
    /// (Phase1-2 からの前提)。
    ///
    /// <b>AVPro / Unity 版の切り替え</b>は
    /// <see cref="Player"/> にどちらを差すかだけです。
    /// 両方の共通基底 <see cref="BaseVRCVideoPlayer"/> しか触らないので、
    /// <b>差し替えてもこのクラスは変わりません</b>(Phase3-1 と同じ考え方)。
    ///
    /// <b>置き場所が重要</b>:VRChat の動画イベントは
    /// <b>動画プレイヤーと同じ GameObject の UdonBehaviour</b> にしか届きません。
    /// このコンポーネントは必ず <c>VRCAVProVideoPlayer</c> /
    /// <c>VRCUnityVideoPlayer</c> と同じ GameObject に置いてください
    /// (Prefab ではその形で作られます)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonVideoBackend : UdonSharpBehaviour
    {
        [Tooltip("同じ GameObject の VRCAVProVideoPlayer / VRCUnityVideoPlayer")]
        public BaseVRCVideoPlayer Player;

        [Tooltip("焼き込み済み VRCUrl を持つカタログ。URL を見てよいのはこのクラスだけ")]
        public UdonMediaCatalog Catalog;

        [Tooltip("映像と音の出力先")]
        public UdonMediaScreen Screen;

        [Tooltip("再生が終わった / 失敗したことを伝える相手")]
        public UdonPlayerSession Session;

        [Tooltip("読み込みが終わったら自動で再生を始める")]
        public bool AutoPlayWhenReady = true;

        [Header("2 系統(Phase8-5 / 重ねるクロスフェード)")]
        [Tooltip("重ねるクロスフェードの担当。入っていれば、裏にいる間の知らせ(終わり・失敗・鳴り始め)は"
                 + "Session ではなくこちらへ送る。裏の失敗で、表の曲まで止まらないようにするため")]
        public UdonCrossfadeCoordinator Coordinator;

        [Tooltip("もう 1 つの動画プレイヤー。読み込みの間隔は、2 つ合わせて空ける"
                 + "(VRChat の読み込み回数の制限は、プレイヤーごとではないため)")]
        public UdonVideoBackend LoadPartner;

        [Tooltip("同じ失敗を何度も送らない(直前と同じ種類のエラーは 1 回だけ通す)")]
        public bool CollapseRepeatedErrors = true;

        [Tooltip("動画の読み込みを最短でも何秒あけるか。"
                 + "VRChat 側の制限より短く呼ぶと即座に失敗が返り、"
                 + "「失敗 → 次へ → 失敗」がフレーム単位で回ってしまう")]
        [Range(0f, 15f)]
        public float MinimumLoadInterval = 5f;

        // ───────── 状態(診断用に外から読める)─────────

        /// <summary>いま読み込んでいる catalog index。無ければ -1。</summary>
        public int LoadedIndex = -1;

        /// <summary>読み込みを頼まれた回数。</summary>
        public int LoadCount;

        /// <summary>直近のエラーコード(<c>VideoError</c> の値)。無ければ -1。</summary>
        public int LastErrorCode = -1;

        /// <summary>読み込み待ちか。</summary>
        public bool IsLoading;

        private bool _wantsPlay;
        private int _lastReportedErrorCode = -1;

        // 読み込みの間隔をあけるための状態
        private int _pendingIndex = -1;
        private float _lastLoadAt = -999f;

        // 待たせている URL(カタログに無いもの)。null ならカタログの _pendingIndex を読む。
        private VRCUrl _pendingExternal;
        private bool _loadScheduled;

        // 読み込んでから一度でも鳴ったか(鳴らずに終わったら失敗とみなす)
        private bool _started;

        // 読み込みが終わって、鳴らせる状態か(Phase8-5。裏で読ませたものを鳴らしてよいかの判断)
        private bool _ready;

        // ───────── 上位からの指示 ─────────

        /// <summary>
        /// <paramref name="catalogIndex"/> を読み込む。
        /// 焼き込まれた <see cref="VRCUrl"/> が無ければ断る
        /// (実行時に URL を作らないため)。
        /// </summary>
        public bool Load(int catalogIndex)
        {
            if (Player == null || Catalog == null) return false;
            if (catalogIndex < 0 || catalogIndex >= Catalog.Count) return false;

            VRCUrl url = Catalog.GetUrl(catalogIndex);
            if (url == null)
            {
                Debug.LogWarning("[UdonVideoBackend] 焼き込み済み URL がありません: index " + catalogIndex);
                return false;
            }

            _pendingIndex = catalogIndex;
            _pendingExternal = null;

            // ── 前の読み込みから間があいていなければ、あとで読む。
            //    VRChat は読み込み回数を制限していて、制限に掛かった読み込みは
            //    「即座に失敗して返る」。それを上位が「失敗 → 次へ」と受けると、
            //    次の読み込みもまた制限に掛かり、フレーム単位で回り続けて固まる。
            //    捨てずに「遅らせる」ので、押した操作は必ず効く。
            return LoadOrWait();
        }

        /// <summary>待つ必要があれば読み込みを遅らせ、無ければいま読む。</summary>
        private bool LoadOrWait()
        {
            float wait = LoadWait();
            if (wait > 0f)
            {
                IsLoading = true;
                _ready = false;

                if (!_loadScheduled)
                {
                    _loadScheduled = true;
                    SendCustomEventDelayedSeconds("LoadPending", wait);
                }
                return true;
            }

            return LoadNow();
        }

        /// <summary>
        /// 待たせていた読み込みを実行する。
        /// <c>SendCustomEventDelayedSeconds</c> から呼ばれるので引数は取れない。
        /// </summary>
        public void LoadPending()
        {
            _loadScheduled = false;

            // 待っている間に、もう 1 つのプレイヤーが読み込んでいたら、もう少し待つ。
            float wait = LoadWait();
            if (wait > 0f)
            {
                _loadScheduled = true;
                SendCustomEventDelayedSeconds("LoadPending", wait);
                return;
            }

            LoadNow();
        }

        /// <summary>
        /// あと何秒待てば読み込んでよいか。<b>2 つのプレイヤーのうち、新しいほうの読み込みから数えます</b>
        /// (Phase8-5)。0 以下なら今すぐ読んでよい。
        /// </summary>
        private float LoadWait()
        {
            float last = _lastLoadAt;
            if (LoadPartner != null && LoadPartner.LastLoadAt > last) last = LoadPartner.LastLoadAt;

            return MinimumLoadInterval - (Time.time - last);
        }

        /// <summary>最後に読み込みを始めた時刻(<c>Time.time</c>)。</summary>
        public float LastLoadAt
        {
            get { return _lastLoadAt; }
        }

        /// <summary>いま読み込む。待ち時間の判断はしない。</summary>
        private bool LoadNow()
        {
            if (Player == null) return false;

            VRCUrl url;
            if (_pendingExternal != null)
            {
                // カタログに無い URL。カタログの何番でもないので -1。
                url = _pendingExternal;
                _pendingExternal = null;
                LoadedIndex = -1;
            }
            else
            {
                if (Catalog == null) return false;
                if (_pendingIndex < 0 || _pendingIndex >= Catalog.Count) return false;

                url = Catalog.GetUrl(_pendingIndex);
                if (url == null) return false;

                LoadedIndex = _pendingIndex;
            }

            LoadCount++;
            IsLoading = true;
            _started = false;
            _ready = false;
            _lastReportedErrorCode = -1;
            _lastLoadAt = Time.time;

            // 新しい曲なので、終わりの見張りをやり直す。
            _endReported = false;
            _lastWatchedTime = 0f;

            // ── 読み込む前に黙らせる(Phase7-6)。
            //
            //    次の URL を読み終わるまでの数秒、AVPro は
            //    <b>前の曲を鳴らし続けます</b>。曲を選び直したのに
            //    前の曲が流れているのは分かりにくいので、先に止めます。
            Player.Pause();

            Player.LoadURL(url);
            return true;
        }

        /// <summary>
        /// <b>読み込むだけで、鳴らさない。</b>Phase8-5。
        /// 重ねるクロスフェードで、次の曲を裏に用意しておくために使います
        /// (鳴らすのは、前の曲が下がり始めるときに <see cref="Play"/> で)。
        /// </summary>
        public bool Preload(int catalogIndex)
        {
            _wantsPlay = false;
            return Load(catalogIndex);
        }

        /// <summary>読み込みが終わって、<see cref="Play"/> ですぐ鳴らせる状態か(Phase8-5)。</summary>
        public bool IsReadyToPlay
        {
            get { return _ready && !IsLoading; }
        }

        /// <summary>読み込んでから再生する。上位が使うのは基本これ。</summary>
        public bool LoadAndPlay(int catalogIndex)
        {
            _wantsPlay = true;
            if (Load(catalogIndex)) return true;

            _wantsPlay = false;
            return false;
        }

        public bool Play()
        {
            if (Player == null) return false;

            // 読み込み中なら、終わってから鳴らす(Phase3-1 と同じ)
            if (IsLoading)
            {
                _wantsPlay = true;
                return true;
            }

            Player.Play();
            return true;
        }

        public bool Pause()
        {
            if (Player == null) return false;

            _wantsPlay = false;
            Player.Pause();
            return true;
        }

        /// <summary>
        /// <b>カタログに無い URL を鳴らす。</b>Phase7-9。
        ///
        /// <b>URL を知ってよいのはこの層だけ</b>という決まりはそのままです。
        /// 上位から渡ってくるのは <c>VRCUrl</c>(すでに URL になっているもの)で、
        /// <b>文字列から URL を作ってはいません</b> ——
        /// Udon では実行時に文字列から <c>VRCUrl</c> を作れないので、
        /// もとをたどると必ず <c>VRCUrlInputField</c>(人が打ち込んだもの)です。
        ///
        /// <see cref="LoadedIndex"/> は -1 になります。カタログの何番でもないので、
        /// 上位が「いまカタログの何を鳴らしているか」と取り違えないためです。
        /// </summary>
        public bool PlayExternal(VRCUrl url)
        {
            if (Player == null || url == null) return false;

            _pendingIndex = -1;
            _pendingExternal = url;
            LoadedIndex = -1;
            _wantsPlay = true;

            // ── カタログの曲と同じく、読み込みの間隔を空けます。
            //    曲を選んだ直後に URL を流すと、VRChat の読み込み回数の制限に掛かって
            //    即座に失敗が返り、次の曲へ飛ばされていました。
            //    待っている間は、前の曲を黙らせておきます(読み込み中に前の曲が鳴り続けないように)。
            if (LoadWait() > 0f) Player.Pause();

            return LoadOrWait();
        }

        public bool Stop()
        {
            if (Player == null) return false;

            _wantsPlay = false;
            IsLoading = false;
            _started = false;
            _ready = false;
            Player.Stop();
            return true;
        }

        public bool IsPlaying
        {
            get { return Player != null && Player.IsPlaying; }
        }

        public float GetTime()
        {
            return Player != null ? Player.GetTime() : 0f;
        }

        public float GetDuration()
        {
            return Player != null ? Player.GetDuration() : 0f;
        }

        /// <summary>
        /// 再生位置を動かす。Phase5-4(同期)で追加。
        ///
        /// <b>「どこへ動かすか」は決めません。</b>言われた位置へ動かすだけです
        /// (同期の基準を持っているのは <see cref="UdonSyncCoordinator"/>)。
        /// まだ読み込みが終わっていないときは動かせないので false を返します。
        /// </summary>
        public bool SetTime(float seconds)
        {
            if (Player == null) return false;
            if (IsLoading) return false;

            float target = seconds < 0f ? 0f : seconds;

            // 終端を越えて指すと、プレイヤーによっては止まってしまう。
            float duration = GetDuration();
            if (duration > 0f && target > duration) return false;

            Player.SetTime(target);
            return true;
        }

        /// <summary>0〜1 の進み具合。長さが分からなければ 0。</summary>
        public float GetProgress()
        {
            float duration = GetDuration();
            if (duration <= 0f) return 0f;
            return Mathf.Clamp01(GetTime() / duration);
        }

        // ───────── 終わりを自分で見つける(Phase7-6)─────────

        [Header("終わりの見張り(Phase7-6)")]
        [Tooltip("VRChat の「終わりました」(OnVideoEnd)が届かない・"
                 + "プレイヤーが勝手に頭へ戻る環境があるので、再生位置を見て自分でも終わりを見つける。"
                 + "0 にすると切れる")]
        public float EndWatchInterval = 0.4f;

        [Tooltip("残りがこの秒数を切ったら「終わった」とみなす")]
        public float EndThresholdSeconds = 0.4f;

        private float _nextEndCheck;
        private float _lastWatchedTime;
        private bool _endReported;

        void Update()
        {
            if (EndWatchInterval <= 0f) return;
            if (Time.time < _nextEndCheck) return;

            _nextEndCheck = Time.time + EndWatchInterval;
            WatchForEnd();
        }

        /// <summary>
        /// <b>再生位置を見て、終わりを自分で見つける。</b>Phase7-6。
        ///
        /// <b>なぜ要るのか</b><br/>
        /// 上位(<see cref="UdonPlayerSession"/>)が次へ進むきっかけは
        /// <see cref="OnVideoEnd"/> ひとつだけでした。ところが
        /// <list type="bullet">
        /// <item>AVPro では「終わりました」が届かないことがある</item>
        /// <item>プレイヤー側の繰り返しが切れていないと、届かないまま頭へ戻る</item>
        /// </list>
        /// のどちらでも、上位は<b>終わったことを永久に知りません</b>。
        /// これが「曲が終わるとまた最初から始まる」の形です。
        ///
        /// <b>知らせが来なくても進めるようにします。</b>
        /// <list type="number">
        /// <item>終わり際まで来た</item>
        /// <item>終わり際にいたのに頭へ戻った(= 勝手に繰り返した)</item>
        /// </list>
        /// のどちらかで、こちらから <see cref="UdonPlayerSession.NotifyEnded"/> を呼びます。
        /// 二重に呼ばないよう、1 曲につき 1 回だけです。
        /// </summary>
        private void WatchForEnd()
        {
            if (Player == null || Session == null) return;
            if (IsLoading || !_started) return;

            float duration = GetDuration();

            // 長さが分からない(生配信など)ときは、終わりも分からない。
            if (duration <= 1f) return;

            float time = GetTime();
            float previous = _lastWatchedTime;
            _lastWatchedTime = time;

            if (_endReported) return;

            // (1) 終わりまで来た。
            if (time >= duration - EndThresholdSeconds)
            {
                ReportEnd("終わりまで来ました");
                return;
            }

            // (2) 終わり際にいたのに頭へ戻った = プレイヤーが勝手に繰り返した。
            //     人が手でバーを戻したときと区別するため、
            //     <b>直前が終わり際だったときだけ</b>そう見なします。
            if (previous > duration - 2f && time < previous - 2f)
            {
                ReportEnd("頭へ戻ったので一周したとみなします");
            }
        }

        private void ReportEnd(string why)
        {
            _endReported = true;
            _started = false;
            _wantsPlay = false;

            Debug.Log("[UdonVideoBackend] " + why + "(index " + LoadedIndex + ")", gameObject);

            // ── <b>先に黙らせてから次へ渡します。</b>Phase7-6。
            //
            //    終わりは<b>本当の終わりより少し手前</b>で見つけます
            //    (見に行く間隔のぶん、行き過ぎてからでは遅いため)。
            //    そのまま次へ渡すと、次の URL を読んでいる間ずっと
            //    <b>前の曲の残り 0.5 秒ほどが鳴り続けます</b>。
            //    曲が変わる瞬間に前の曲が一瞬鳴るのはこれです。
            if (Player != null) Player.Pause();

            DeliverEnded();
        }

        // ───────── 知らせの行き先(Phase8-5)─────────
        //
        // 動画プレイヤーが 2 つあるとき、<b>裏</b>で起きたことを Session へ送ると、
        // いま鳴っている(表の)曲が飛ばされます(Phase7-5 の「裏の失敗で表まで止まる」)。
        // 裏の知らせは、クロスフェードの担当が受け取ります。

        private bool IsBack()
        {
            return Coordinator != null && Coordinator.IsBackRole(this);
        }

        private void DeliverEnded()
        {
            if (Coordinator != null)
            {
                if (Coordinator.IsBackRole(this))
                {
                    Coordinator.NotifyBackTrouble(this);
                    return;
                }

                // 表が終わった。裏に次の曲があれば、それを表にして終わり(読み直さない)。
                if (Coordinator.NotifyFrontEnded(this)) return;
            }

            if (Session != null) Session.NotifyEnded();
        }

        private void DeliverError()
        {
            if (IsBack())
            {
                Coordinator.NotifyBackTrouble(this);
                return;
            }

            if (Session != null) Session.NotifyError();
        }

        // ───────── VRChat からの知らせ ─────────
        // 同じ GameObject の動画プレイヤーが直接ここへ送ってくる。

        public override void OnVideoReady()
        {
            // ── 次の読み込みを待たせている間に、<b>前の読み込み</b>が終わった知らせ。
            //    ここで鳴らすと、「鳴らしてほしい」の印を前の曲が使ってしまい、
            //    あとから読み込んだ曲(曲を選んだ直後に流した URL など)が
            //    <b>読み込まれたまま鳴らなくなります</b>。次の読み込みを待ちます。
            if (_loadScheduled) return;

            IsLoading = false;
            _ready = true;

            if (Screen != null) Screen.ApplyVolume();

            if (AutoPlayWhenReady && _wantsPlay)
            {
                _wantsPlay = false;
                if (Player != null) Player.Play();
            }
        }

        public override void OnVideoStart()
        {
            IsLoading = false;
            _started = true;

            // 裏で鳴り始めたものは、まだ「いまの曲」ではない。表になったときに知らせてもらう。
            if (IsBack()) return;

            // 実際に鳴ったので、上位の「続けて失敗した回数」を戻してもらう。
            if (Session != null) Session.NotifyStarted();
        }

        public override void OnVideoEnd()
        {
            IsLoading = false;
            _wantsPlay = false;

            // 一度も鳴らずに終わったなら、それは「正常に終わった」ではなく失敗。
            // 正常扱いにすると、上位が何事もなく次へ送り続けて止まらなくなる。
            if (!_started)
            {
                Debug.LogWarning("[UdonVideoBackend] 一度も再生されずに終わりました: index "
                                 + LoadedIndex, gameObject);

                _ready = false;
                DeliverError();
                return;
            }

            _started = false;

            // 見張り(WatchForEnd)がすでに知らせていたら、二重に進めない。
            if (_endReported) return;
            _endReported = true;

            DeliverEnded();
        }

        public override void OnVideoError(VideoError videoError)
        {
            IsLoading = false;
            _wantsPlay = false;

            int code = (int)videoError;
            LastErrorCode = code;

            // 同じ失敗を続けて送ってきたときに二重で次へ送らない(Phase3-2 と同じ)
            if (CollapseRepeatedErrors && code == _lastReportedErrorCode) return;
            _lastReportedErrorCode = code;

            Debug.LogWarning("[UdonVideoBackend] 再生に失敗しました (VideoError " + code
                             + ") index " + LoadedIndex);

            _ready = false;
            DeliverError();
        }

        /// <summary>いま読み込んでいるものが鳴り始めたか(クロスフェードの開始判定)。</summary>
        public bool HasStarted
        {
            get { return _started; }
        }

        /// <summary>配線が済んでいるか(診断用)。</summary>
        public bool IsReady
        {
            get { return Player != null && Catalog != null; }
        }

        /// <summary>Console 表示用の 1 行。</summary>
        public string Describe()
        {
            if (Player == null) return "動画プレイヤー未設定";

            string kind = Player.GetType().Name;
            int urls = Catalog != null ? Catalog.Count : 0;
            return kind + " / カタログ " + urls + " 件";
        }
    }
}
