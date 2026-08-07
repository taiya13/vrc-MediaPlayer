using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace SmartMediaPlatform.World.Udon.UI
{
    /// <summary>
    /// <b>「いま鳴っているもの」の表示。</b>Phase5-3。
    ///
    /// <b>判断ロジックを 1 つも持っていません。</b>
    /// <see cref="UdonPlayerSession"/> と <see cref="UdonCatalogStore"/> の内容を
    /// <see cref="Text"/> と進捗バーへ書き写すだけです。
    /// <b>操作の口も持ちません</b>(押すのは <see cref="UdonTransportView"/> の仕事)。
    ///
    /// 割り当ては<b>すべて任意</b>です。見出しだけのリモコンなら
    /// <see cref="TitleText"/> だけ挿しておけば動きます。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonNowPlayingView : UdonSharpBehaviour
    {
        [Header("つなぎ先(UdonMediaPanel が自動で入れる)")]
        [Tooltip("表示するもとの状態")]
        public UdonPlayerSession Session;

        [Tooltip("表示用データの窓口(URL は見えない)")]
        public UdonCatalogStore Store;

        [Tooltip("再生位置を聞く相手。空なら Session のものを使う")]
        public UdonVideoBackend Backend;

        [Tooltip("同期の担当(Phase5-4)。空なら操作者の欄を出さない")]
        public UdonSyncCoordinator Sync;

        [Header("文字(空でも動く)")]
        public Text TitleText;

        [Tooltip("アーティスト / ジャンル")]
        public Text ArtistText;

        [Tooltip("0:12 / 3:45")]
        public Text TimeText;

        [Tooltip("再生中 / 一時停止 / 停止")]
        public Text StateText;

        [Tooltip("Queue の残り件数")]
        public Text QueueCountText;

        [Tooltip("あと何分で終わるか。「次はいつ?」に一番よく答える表示")]
        public Text RemainingText;

        [Tooltip("いま誰が操作しているか(同期しているときだけ出る)")]
        public Text SyncText;

        [Tooltip("ジャンル。丸い札に出す")]
        public Text GenreText;

        [Tooltip("ジャンルの札そのもの。ジャンルが無い曲では隠す")]
        public GameObject GenreChip;

        [Header("絵(Phase7)")]
        [Tooltip("いま鳴っている曲の絵。焼き込んでいなければジャンルの色で塗る")]
        public Image Artwork;

        [Tooltip("絵が無いときに枠へ出す文字")]
        public Text ArtworkFallbackText;

        [Tooltip("絵が無いときの塗り色を決める一覧(ジャンルの色を借りる)")]
        public UdonMediaListView PaletteSource;

        [Header("進捗(空でも動く)")]
        [Tooltip("Image Type を Filled にしておくこと")]
        public Image ProgressFill;

        [Tooltip("動かせる再生バー。onValueChanged から OnSeekChanged を呼ぶこと。"
                 + "空のままでも再生には影響しません")]
        public Slider SeekSlider;

        [Tooltip("つまみを離してから実際に動かすまでの間(秒)。"
                 + "つかんでいる最中に毎フレーム seek すると、読み込みが追いつかず固まる")]
        [Range(0.05f, 1f)]
        public float SeekApplyDelay = 0.2f;

        [Header("文言")]
        public string NothingLabel = "(何も再生していません)";
        public string PlayingLabel = "▶ 再生中";
        public string PausedLabel = "‖ 一時停止";
        public string StoppedLabel = "■ 停止";
        public string ExhaustedLabel = "次がありません";
        public string LoadingLabel = "読み込み中…";

        /// <summary>
        /// <b>バーと時間だけを毎フレーム動かす。</b>Phase7-2。
        ///
        /// <b>なぜ Refresh と分けたのか</b><br/>
        /// <see cref="UdonMediaPanel"/> は<b>0.5 秒に 1 回</b>しか書き直しません
        /// (曲名や一覧を毎フレーム書き直すと重いため)。
        /// バーもその周期で動いていたので、<b>カクカク跳ねるか、まったく動かない</b>
        /// ように見えていました。
        ///
        /// ここで動かすのは<b>3 つの値だけ</b>です。
        /// <list type="bullet">
        /// <item>進捗バーの伸び</item>
        /// <item>経過時間</item>
        /// <item>残り時間</item>
        /// </list>
        /// 曲名・チャンネル・絵・一覧は今までどおり 0.5 秒周期のままなので、
        /// <b>重さはほとんど変わりません</b>。
        /// </summary>
        void Update()
        {
            TickArtworkPop();

            if (Session == null) return;
            if (Session.CurrentIndex < 0) return;

            UdonVideoBackend backend = ResolveBackend();
            if (backend == null) return;

            float length = backend.GetDuration();

            // ── つまみをつかんでいる間は、こちらから書き戻さない。
            //    書き戻すと、動かした先から毎フレーム引き戻されて操作できません。
            if (_seekPending)
            {
                float wanted = _seekValue * length;

                SetFill(_seekValue);
                SetText(TimeText,
                        FormatSeconds(wanted) + " / " + FormatLength(length, Session.CurrentIndex));
                SetText(RemainingText, FormatRemaining(wanted, length));

                // 手が止まったら実際に動かす。
                // つかんでいる最中に毎フレーム seek すると、読み込みが追いつきません。
                if (Time.time - _lastSeekInput >= SeekApplyDelay) ApplySeek(backend, length);
                return;
            }

            float elapsed = backend.GetTime();

            SetFill(backend.GetProgress());
            SetSeekSlider(backend.GetProgress());
            SetText(TimeText, FormatSeconds(elapsed) + " / " + FormatLength(length, Session.CurrentIndex));
            SetText(RemainingText, FormatRemaining(elapsed, length));
        }

        // ───────── 絵が入れ替わったときに弾ませる(Frost / Phase7-7)─────────

        [Header("絵の動き(Frost / Phase7-7)")]
        [Tooltip("弾ませる絵。空なら何も動かない")]
        public RectTransform ArtworkRect;

        [Tooltip("元の大きさに戻るまでの時間(秒)")]
        public float ArtworkPopSeconds = 0.30f;

        [Tooltip("入れ替わった瞬間の大きさ。1.04 なら 4% だけ大きく始まる")]
        public float ArtworkPopScale = 1.04f;

        // いまの倍率。1 に向かって毎フレーム戻る。
        private float _artworkScale = 1f;
        private int _poppedFor = -2;

        /// <summary>
        /// <b>曲が変わったら、絵をほんの少しだけ弾ませる。</b>
        ///
        /// <b>なぜ動かすのか</b><br/>
        /// ワールドでは<b>視線が板の外にあることがふつう</b>です。
        /// 絵が音もなく差し替わっても、次に見たときには終わっていて、
        /// 「いつ変わったのか」が分かりません。<b>動きだけが、見ていない間にも
        /// 気付かせられる合図</b>です。
        ///
        /// 4% しか変えないのは、大きく動かすと<b>安っぽく</b>なるからです。
        /// 気付くが、邪魔にはならない —— その境目がこのあたりです。
        /// </summary>
        private void TickArtworkPop()
        {
            if (ArtworkRect == null) return;

            int current = Session != null ? Session.CurrentIndex : -1;

            if (current != _poppedFor)
            {
                _poppedFor = current;
                _artworkScale = ArtworkPopScale;
            }

            if (_artworkScale <= 1.0005f && _artworkScale >= 0.9995f) return;

            // 1 へ向かって指数的に戻る。時間で割るので、
            // フレームレートが変わっても掛かる時間は同じ。
            float k = ArtworkPopSeconds <= 0f
                ? 1f
                : Mathf.Clamp01(Time.deltaTime / ArtworkPopSeconds);

            _artworkScale = Mathf.Lerp(_artworkScale, 1f, k);
            ArtworkRect.localScale = new Vector3(_artworkScale, _artworkScale, 1f);
        }

        // ───────── 動かせる再生バー(Phase7-3)─────────

        // つまみを動かしている最中か。動かしている間は書き戻さない。
        private bool _seekPending;
        private float _seekValue;
        private float _lastSeekInput;

        // 自分で書き込んだぶんを「人が動かした」と取り違えないための札。
        private bool _writingSeek;

        /// <summary>
        /// <b><c>Slider.onValueChanged</c> から呼ぶ。</b>引数は取らず、つまみを読みます。
        ///
        /// <b>ここでは動かしません。</b>覚えておいて、手が止まってから動かします
        /// (つかんでいる最中に毎フレーム seek すると、読み込みが追いつかず固まるため)。
        /// </summary>
        public void OnSeekChanged()
        {
            if (_writingSeek) return;
            if (SeekSlider == null || Session == null) return;
            if (Session.CurrentIndex < 0) return;

            _seekValue = Mathf.Clamp01(SeekSlider.value);
            _seekPending = true;
            _lastSeekInput = Time.time;
        }

        /// <summary>覚えていた位置へ実際に動かす。</summary>
        private void ApplySeek(UdonVideoBackend backend, float length)
        {
            _seekPending = false;

            if (backend == null || length <= 0f) return;

            // 同期しているときは、まず持ち主になる。
            // 持ち主でないまま動かすと、次の見回りで元の位置へ戻されます。
            UdonSyncCoordinator sync = ResolveSync();
            if (sync != null && sync.Enabled)
            {
                if (!sync.IsOwner() && !sync.TakeControl()) return;
            }

            float seconds = _seekValue * length;
            if (!backend.SetTime(seconds)) return;

            // 同期の基準もここへ置き直す(置き直さないと引き戻される)。
            if (sync != null && sync.Enabled) sync.NotifySeeked(seconds);
        }

        private void SetSeekSlider(float value)
        {
            if (SeekSlider == null) return;

            float clamped = Mathf.Clamp01(value);
            if (Mathf.Abs(SeekSlider.value - clamped) < 0.0005f) return;

            _writingSeek = true;
            SeekSlider.value = clamped;
            _writingSeek = false;
        }

        /// <summary>画面を書き直す。<see cref="UdonMediaPanel"/> から呼ばれる。</summary>
        public void Refresh()
        {
            RefreshSyncOwner();

            if (Session == null)
            {
                ShowNothing("");
                return;
            }

            int current = Session.CurrentIndex;

            if (current < 0)
            {
                ShowNothing(Session.IsExhausted ? ExhaustedLabel : StoppedLabel);
                return;
            }

            SetText(TitleText, Store != null ? Store.GetTitle(current) : "");
            SetText(ArtistText, Store != null ? Store.GetArtist(current) : "");

            ShowGenre(Store != null ? Store.GetGenre(current) : "");
            ShowArtwork(current);

            // Phase7-3 から QueueCount は「これから流すもの」だけの数。
            // 鳴っているものは入っていないので、引き算は要らない。
            SetText(QueueCountText, "次 " + Session.QueueCount + " 件");

            UdonVideoBackend backend = ResolveBackend();

            if (backend == null)
            {
                SetText(StateText, Session.IsPlaying ? PlayingLabel : PausedLabel);
                SetText(TimeText, "--:-- / " + Duration(current));
                SetText(RemainingText, "");
                SetFill(0f);
                return;
            }

            // ── 状態は「伝えることがあるときだけ」出す。
            //
            //    ふつうに鳴っているとき「▶ 再生中」と書いても、
            //    <b>バーが動いていることと、ボタンの見た目で既に分かっています</b>。
            //    3 つ並んだ文字のうち 1 つが常に無意味だと、
            //    残りの 2 つ(経過・残り)も読まれなくなります。
            //    読み込み中と一時停止のときだけ出します。
            bool loading = Session.IsPlaying && !backend.IsPlaying;
            SetText(StateText, loading
                ? LoadingLabel
                : (Session.IsPlaying ? "" : PausedLabel));

            float elapsed = backend.GetTime();
            float length = backend.GetDuration();

            SetText(TimeText, FormatSeconds(elapsed) + " / " + FormatLength(length, current));
            SetText(RemainingText, FormatRemaining(elapsed, length));
            SetFill(backend.GetProgress());
        }

        // ───────── 内部 ─────────

        private void ShowNothing(string state)
        {
            SetText(TitleText, NothingLabel);
            SetText(ArtistText, "");
            SetText(TimeText, "--:-- / --:--");
            SetText(RemainingText, "");
            SetText(StateText, state);
            SetText(QueueCountText, "");
            SetFill(0f);

            ShowGenre("");
            ShowArtwork(-1);
        }

        /// <summary>ジャンルの札。無い曲では札ごと隠す(空の丸が残らないように)。</summary>
        private void ShowGenre(string genre)
        {
            bool has = genre != null && genre.Length > 0;

            SetActive(GenreChip, has);
            SetText(GenreText, has ? genre : "");
        }

        /// <summary>
        /// 絵を入れる。<b>枠は必ず残します</b> —
        /// 曲が変わるたびに大きさが変わると、目が落ち着きません。
        /// </summary>
        private void ShowArtwork(int catalogIndex)
        {
            if (Artwork == null) return;

            Sprite sprite = catalogIndex >= 0 && Store != null
                ? Store.GetThumbnail(catalogIndex)
                : null;

            if (Artwork.sprite != sprite) Artwork.sprite = sprite;

            Color wanted = Color.white;
            string fallback = "";

            if (sprite == null)
            {
                string genre = catalogIndex >= 0 && Store != null
                    ? Store.GetGenre(catalogIndex)
                    : "";

                // 一覧と同じ色の決め方を借りる。行と大きい絵で色が違うと、
                // 同じ曲だと分からなくなる。
                wanted = PaletteSource != null
                    ? PaletteSource.GenreColor(genre)
                    : new Color(0.902f, 0.902f, 0.918f, 1f);

                fallback = genre != null && genre.Length > 0 ? genre : "♪";
            }

            if (Artwork.color != wanted) Artwork.color = wanted;
            SetText(ArtworkFallbackText, fallback);
        }

        private void SetActive(GameObject target, bool value)
        {
            if (target == null) return;
            if (target.activeSelf == value) return;
            target.SetActive(value);
        }

        /// <summary>長さ。動画から取れなければカタログの値を使う。</summary>
        private string FormatLength(float seconds, int catalogIndex)
        {
            if (seconds > 0f) return FormatSeconds(seconds);
            return Duration(catalogIndex);
        }

        /// <summary>あと何分か。分からなければ空。</summary>
        private string FormatRemaining(float elapsed, float length)
        {
            if (length <= 0f) return "";

            float rest = length - elapsed;
            if (rest < 0f) rest = 0f;

            return "残り " + FormatSeconds(rest);
        }

        /// <summary>
        /// いま誰が操作しているかを出す。
        ///
        /// <b>「誰が操作できるか」を目に見えるようにする</b>ためだけの 1 行です。
        /// 同期していないときは何も出しません。
        /// </summary>
        private void RefreshSyncOwner()
        {
            if (SyncText == null) return;

            if (Sync == null || !Sync.Enabled)
            {
                SetText(SyncText, "");
                return;
            }

            string owner = Sync.OwnerName();
            string label = owner.Length == 0
                ? ""
                : (Sync.IsOwner() ? "操作中: あなた" : "操作中: " + owner);

            SetText(SyncText, label);
        }

        private UdonVideoBackend ResolveBackend()
        {
            // クロスフェード中は A と B が入れ替わるので、
            // 「いま鳴っているほう」を Session に教えてもらう(Phase7-5)。
            // ここを固定にすると、混ざったあとバーが止まって見えます。
            if (Session != null)
            {
                UdonVideoBackend active = Session.ActiveBackend();
                if (active != null) return active;
            }

            if (Backend != null) return Backend;
            return null;
        }

        private UdonSyncCoordinator ResolveSync()
        {
            return Sync;
        }

        private string Duration(int catalogIndex)
        {
            if (Store == null) return "--:--";
            return Store.FormatDuration(catalogIndex);
        }

        private void SetFill(float value)
        {
            if (ProgressFill == null) return;

            float clamped = Mathf.Clamp01(value);
            if (ProgressFill.fillAmount == clamped) return;

            ProgressFill.fillAmount = clamped;
        }

        private void SetText(Text target, string value)
        {
            if (target == null) return;
            if (target.text == value) return;
            target.text = value;
        }

        /// <summary>「3:45」の形。<see cref="UdonCatalogStore.FormatDuration"/> と同じ見た目。</summary>
        private string FormatSeconds(float seconds)
        {
            if (seconds < 0f) return "--:--";

            int total = (int)seconds;
            int minutes = total / 60;
            int rest = total % 60;
            string tail = rest < 10 ? "0" + rest : "" + rest;
            return minutes + ":" + tail;
        }
    }
}
