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

        [Tooltip("いま誰が操作しているか(同期しているときだけ出る)")]
        public Text SyncText;

        [Header("進捗(空でも動く)")]
        [Tooltip("Image Type を Filled にしておくこと")]
        public Image ProgressFill;

        [Header("文言")]
        public string NothingLabel = "(何も再生していません)";
        public string PlayingLabel = "▶ 再生中";
        public string PausedLabel = "‖ 一時停止";
        public string StoppedLabel = "■ 停止";
        public string ExhaustedLabel = "次がありません";

        /// <summary>画面を書き直す。<see cref="UdonMediaPanel"/> から呼ばれる。</summary>
        public void Refresh()
        {
            RefreshSyncOwner();

            if (Session == null)
            {
                SetText(TitleText, NothingLabel);
                SetText(ArtistText, "");
                SetText(TimeText, "--:-- / --:--");
                SetText(StateText, "");
                SetText(QueueCountText, "");
                SetFill(0f);
                return;
            }

            int current = Session.CurrentIndex;

            if (current < 0)
            {
                SetText(TitleText, NothingLabel);
                SetText(ArtistText, "");
                SetText(TimeText, "--:-- / --:--");
                SetText(StateText, Session.IsExhausted ? ExhaustedLabel : StoppedLabel);
                SetText(QueueCountText, "");
                SetFill(0f);
                return;
            }

            SetText(TitleText, Store != null ? Store.GetTitle(current) : "");
            SetText(ArtistText, Store != null ? Store.GetArtist(current) : "");
            SetText(StateText, Session.IsPlaying ? PlayingLabel : PausedLabel);

            // 「いま鳴っているもの」を含めた件数なので、待ちは 1 引いた数。
            int upcoming = Session.QueueCount - 1;
            if (upcoming < 0) upcoming = 0;
            SetText(QueueCountText, "次 " + upcoming + " 件");

            UdonVideoBackend backend = ResolveBackend();

            if (backend == null)
            {
                SetText(TimeText, "--:-- / " + Duration(current));
                SetFill(0f);
                return;
            }

            SetText(TimeText, FormatSeconds(backend.GetTime()) + " / " + Duration(current));
            SetFill(backend.GetProgress());
        }

        // ───────── 内部 ─────────

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
