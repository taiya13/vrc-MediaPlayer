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

            LoadedIndex = catalogIndex;
            LoadCount++;
            IsLoading = true;
            _lastReportedErrorCode = -1;

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
        }

        public override void OnVideoEnd()
        {
            IsLoading = false;
            _wantsPlay = false;

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
