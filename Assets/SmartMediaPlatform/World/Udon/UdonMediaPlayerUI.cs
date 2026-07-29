using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace SmartMediaPlatform.World.Udon
{
    /// <summary>
    /// <b>画面。</b>Phase5-1 の <c>MediaPlayerUI</c>(IMGUI)の Udon 版。
    ///
    /// <b>判断ロジックを 1 つも持っていません。</b>
    /// <see cref="UdonPlayerSession"/> と <see cref="UdonCatalogStore"/> が持っている内容を
    /// <see cref="Text"/> へ書き写すだけ、押されたら
    /// <see cref="UdonMediaController"/> に伝えるだけです。
    /// だから<b>この画面ごと差し替えても、下は 1 行も変わりません</b>。
    ///
    /// <b>なぜ OnGUI をやめたのか</b><br/>
    /// <c>OnGUI</c> はアップロードしたワールドでは動きません
    /// (そもそも独自 MonoBehaviour が動かない)。
    /// ワールドの中で見えるのは uGUI(Canvas)なので、
    /// <b>Text へ文字を書くだけ</b>の形に置き換えました。
    ///
    /// <b>Text が 1 つも割り当てられていなくても動きます。</b>
    /// 画面が無いだけで、再生そのものは <see cref="UdonPlayerSession"/> が続けます。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonMediaPlayerUI : UdonSharpBehaviour
    {
        [Tooltip("表示するもとの状態")]
        public UdonPlayerSession Session;

        [Tooltip("表示用データの窓口(URL は見えない)")]
        public UdonCatalogStore Store;

        [Header("書き込み先(空でも動く)")]
        [Tooltip("いま鳴っているもの")]
        public Text NowPlayingText;

        [Tooltip("再生位置 / 長さ")]
        public Text TimeText;

        [Tooltip("一覧。行数は Text の数で決まる")]
        public Text[] LibraryRows;

        [Tooltip("Queue。行数は Text の数で決まる")]
        public Text[] QueueRows;

        [Tooltip("関連。行数は Text の数で決まる")]
        public Text[] RelatedRows;

        [Tooltip("直近の操作の結果")]
        public Text StatusText;

        [Header("表示")]
        [Tooltip("一覧をこの行数ぶんずらして表示する(ページ送り)")]
        public int LibraryScroll;

        [Tooltip("何秒ごとに書き直すか。0 なら操作されたときだけ")]
        public float RefreshInterval = 0.5f;

        // 関連の catalog index(いま表示しているもの)
        private int[] _relatedIndices;
        private float _nextRefresh;
        private string _status = "";

        void Start()
        {
            _relatedIndices = new int[0];
            Refresh();
        }

        void Update()
        {
            if (RefreshInterval <= 0f) return;
            if (Time.time < _nextRefresh) return;

            _nextRefresh = Time.time + RefreshInterval;
            Refresh();
        }

        /// <summary>画面を書き直す。操作のたびに <see cref="UdonMediaController"/> が呼ぶ。</summary>
        public void Refresh()
        {
            if (Session == null || Store == null) return;

            RefreshNowPlaying();
            RefreshLibrary();
            RefreshRelated();
            RefreshQueue();

            if (StatusText != null) StatusText.text = _status;
        }

        /// <summary>状態の 1 行を差し替える。</summary>
        public void SetStatus(string message)
        {
            _status = message == null ? "" : message;
            if (StatusText != null) StatusText.text = _status;
        }

        /// <summary>関連の <paramref name="position"/> 番目の catalog index。無ければ -1。</summary>
        public int GetRelatedIndexAt(int position)
        {
            if (_relatedIndices == null) return -1;
            if (position < 0 || position >= _relatedIndices.Length) return -1;
            return _relatedIndices[position];
        }

        // ───────── ページ送り(ボタンからそのまま呼べる)─────────

        public void ScrollLibraryDown()
        {
            int rows = LibraryRows != null ? LibraryRows.Length : 0;
            if (rows <= 0) return;

            if (LibraryScroll + rows < Store.Count) LibraryScroll += rows;
            Refresh();
        }

        public void ScrollLibraryUp()
        {
            int rows = LibraryRows != null ? LibraryRows.Length : 0;
            if (rows <= 0) return;

            LibraryScroll -= rows;
            if (LibraryScroll < 0) LibraryScroll = 0;
            Refresh();
        }

        /// <summary>一覧の <paramref name="row"/> 行目が指している一覧位置。</summary>
        public int GetLibraryPositionForRow(int row)
        {
            return LibraryScroll + row;
        }

        // ───────── 内部 ─────────

        private void RefreshNowPlaying()
        {
            int current = Session.CurrentIndex;

            if (NowPlayingText != null)
            {
                if (current < 0)
                {
                    NowPlayingText.text = "(何も再生していません)";
                }
                else
                {
                    string mark = Session.IsPlaying ? "▶ " : "‖ ";
                    NowPlayingText.text = mark + Store.GetTitle(current)
                                          + "\n" + Store.GetArtist(current);
                }
            }

            if (TimeText != null)
            {
                if (current < 0) TimeText.text = "--:-- / --:--";
                else TimeText.text = FormatSeconds(CurrentSeconds()) + " / "
                                     + Store.FormatDuration(current);
            }
        }

        private void RefreshLibrary()
        {
            if (LibraryRows == null) return;

            int selected = Session.SelectedIndex;

            for (int row = 0; row < LibraryRows.Length; row++)
            {
                Text target = LibraryRows[row];
                if (target == null) continue;

                int position = LibraryScroll + row;
                int catalogIndex = Store.GetIndexAt(position);

                if (catalogIndex < 0)
                {
                    target.text = "";
                    continue;
                }

                string mark = catalogIndex == selected ? "› " : "  ";
                target.text = mark + (position + 1) + ". " + Store.FormatRow(catalogIndex);
            }
        }

        private void RefreshRelated()
        {
            int current = Session.CurrentIndex;
            _relatedIndices = current < 0 ? new int[0] : Store.GetRelatedIndices(current);

            if (RelatedRows == null) return;

            for (int row = 0; row < RelatedRows.Length; row++)
            {
                Text target = RelatedRows[row];
                if (target == null) continue;

                if (row >= _relatedIndices.Length)
                {
                    target.text = "";
                    continue;
                }
                target.text = "▷ " + Store.FormatRow(_relatedIndices[row]);
            }
        }

        private void RefreshQueue()
        {
            if (QueueRows == null) return;

            for (int row = 0; row < QueueRows.Length; row++)
            {
                Text target = QueueRows[row];
                if (target == null) continue;

                int catalogIndex = Session.GetQueueAt(row);
                if (catalogIndex < 0)
                {
                    target.text = "";
                    continue;
                }

                // 先頭はいま鳴っているもの。並びは Phase4-3 の QueueView と同じ見た目。
                string mark = row == 0 ? "♪ " : row + ". ";
                target.text = mark + Store.GetTitle(catalogIndex);
            }
        }

        private float CurrentSeconds()
        {
            if (Session.Backend == null) return 0f;
            return Session.Backend.GetTime();
        }

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
