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
            if (Session == null) return;
            if (Session.CurrentIndex < 0) return;

            UdonVideoBackend backend = ResolveBackend();
            if (backend == null) return;

            float elapsed = backend.GetTime();
            float length = backend.GetDuration();

            SetFill(backend.GetProgress());
            SetText(TimeText, FormatSeconds(elapsed) + " / " + FormatLength(length, Session.CurrentIndex));
            SetText(RemainingText, FormatRemaining(elapsed, length));
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

            // 再生中のはずなのにまだ動いていないなら「読み込み中」。
            // 何も出ない時間に「壊れた?」と思わせないための 1 行。
            bool loading = Session.IsPlaying && !backend.IsPlaying;
            SetText(StateText, loading
                ? LoadingLabel
                : (Session.IsPlaying ? PlayingLabel : PausedLabel));

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
                    : new Color(0.18f, 0.20f, 0.26f, 1f);

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
            if (Backend != null) return Backend;
            if (Session == null) return null;
            return Session.Backend;
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
