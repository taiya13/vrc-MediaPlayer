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
        private bool _loadScheduled;

        // 読み込んでから一度でも鳴ったか(鳴らずに終わったら失敗とみなす)
        private bool _started;

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

            // ── 前の読み込みから間があいていなければ、あとで読む。
            //    VRChat は読み込み回数を制限していて、制限に掛かった読み込みは
            //    「即座に失敗して返る」。それを上位が「失敗 → 次へ」と受けると、
            //    次の読み込みもまた制限に掛かり、フレーム単位で回り続けて固まる。
            //    捨てずに「遅らせる」ので、押した操作は必ず効く。
            float wait = MinimumLoadInterval - (Time.time - _lastLoadAt);
            if (wait > 0f)
            {
                IsLoading = true;

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
            LoadNow();
        }

        /// <summary>いま読み込む。待ち時間の判断はしない。</summary>
        private bool LoadNow()
        {
            if (Player == null || Catalog == null) return false;
            if (_pendingIndex < 0 || _pendingIndex >= Catalog.Count) return false;

            VRCUrl url = Catalog.GetUrl(_pendingIndex);
            if (url == null) return false;

            LoadedIndex = _pendingIndex;
            LoadCount++;
            IsLoading = true;
            _started = false;
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

        public bool Stop()
        {
            if (Player == null) return false;

            _wantsPlay = false;
            IsLoading = false;
            _started = false;
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

            Session.NotifyEnded();
        }

        // ───────── VRChat からの知らせ ─────────
        // 同じ GameObject の動画プレイヤーが直接ここへ送ってくる。

        public override void OnVideoReady()
        {
            IsLoading = false;

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

                if (Session != null) Session.NotifyError();
                return;
            }

            _started = false;

            // 見張り(WatchForEnd)がすでに知らせていたら、二重に進めない。
            if (_endReported) return;
            _endReported = true;

            if (Session != null) Session.NotifyEnded();
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

            if (Session != null) Session.NotifyError();
        }

        // ───────── クロスフェード用(Phase7-5)─────────

        [Header("クロスフェード(Phase7-5)")]
        [Tooltip("この動画プレイヤーの音を鳴らす AudioSource。"
                 + "クロスフェードでは A / B の音量を別々に動かすので、"
                 + "Screen の Speaker ではなくこちらを直接触ります")]
        public AudioSource Speaker;

        [Tooltip("この動画プレイヤーが書き込む絵の欄(_MainTex か _SecondTex)。"
                 + "1 枚の画面に 2 系統を混ぜるために分けてあります")]
        public string TextureProperty = "_MainTex";

        /// <summary>
        /// <b>この系統の音量を直接決める。</b>Phase7-5。
        ///
        /// <see cref="UdonMediaScreen.SetVolume"/> は<b>人が決めた音量</b>で、
        /// こちらは<b>混ぜている最中の一時的な倍率</b>です。
        /// 掛け算にしてあるので、フェード中に人が音量を変えても、
        /// 両方が正しく効きます。
        /// </summary>
        /// <param name="fade">0〜1。混ざり具合から来る倍率。</param>
        public void SetFadeVolume(float fade)
        {
            if (Speaker == null) return;

            float master = Screen != null ? Screen.Volume : 1f;
            Speaker.volume = Mathf.Clamp01(master) * Mathf.Clamp01(fade);
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
