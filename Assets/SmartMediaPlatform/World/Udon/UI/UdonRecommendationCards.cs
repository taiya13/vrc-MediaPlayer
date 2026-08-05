using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using SmartMediaPlatform.Recommendation.Udon;

namespace SmartMediaPlatform.World.Udon.UI
{
    /// <summary>
    /// <b>おすすめをカードで見せる。</b>Phase7-3。
    ///
    /// <b>なぜ一覧をやめたのか</b><br/>
    /// 一覧の行は<b>「探しているものが決まっている人」</b>のための形です。
    /// 上から順に読んで、目当ての名前を見つける。
    /// ところがおすすめは逆で、<b>探していない人に見つけてもらう</b>ためのものです。
    /// 名前を読ませても、知らない曲なら何も伝わりません。
    ///
    /// カードにすると<b>絵が主役</b>になります。名前を読む前に、
    /// 絵と「同じチャンネル」の一言で、押すかどうかを決められます。
    /// Apple Music も Spotify も、探す画面は行・見つける画面はカード、で揃っています。
    ///
    /// <b>理由を必ず添えます。</b>「あなたへのおすすめ」とだけ書かれたものは
    /// <b>なぜそれなのかが分からないので押されません</b>。
    /// 出すのは<b>いちばん強い理由 1 つだけ</b>です。
    ///
    /// <b>作るカードの数は決め打ち</b>です(ふつう 4 枚)。
    /// 一覧と同じで、GameObject を増やさずに中身だけ差し替えます。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UdonRecommendationCards : UdonSharpBehaviour
    {
        [Tooltip("表示用データの窓口")]
        public UdonCatalogStore Store;

        [Tooltip("いま何が鳴っているかを知るため")]
        public UdonPlayerSession Session;

        [Tooltip("押されたことを伝える窓口")]
        public UdonMediaController Controller;

        [Tooltip("理由を出させるエンジン。空なら理由は「おすすめ」になる")]
        public UdonRecommendationEngine Recommendation;

        [Header("カード(順番どおりに挿すこと)")]
        public GameObject[] Cards;
        public Image[] Artworks;
        public Text[] ArtworkFallbacks;
        public Text[] Titles;
        public Text[] Artists;
        public Text[] Reasons;
        public GameObject[] PressedMarkers;

        [Header("何も無いとき")]
        public GameObject EmptyMessage;
        public Text EmptyText;

        [Tooltip("絵が無いときの色を借りる一覧(ジャンルの色)")]
        public UdonMediaListView PaletteSource;

        [Tooltip("押したしるしを出しておく時間(秒)")]
        public float TouchFeedbackSeconds = 0.25f;

        // いま何番目のカードに何を出しているか。押されたときに引くために持つ。
        private int[] _shown;

        private int _touchedCard = -1;
        private float _touchedAt = -999f;

        // 前回どの曲を種にして作ったか。変わったときだけ作り直す。
        private int _seed = -2;

        private bool _initialized;

        void Start()
        {
            EnsureInitialized();
            Refresh();
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            int count = Cards != null ? Cards.Length : 0;
            _shown = new int[count];
            for (int i = 0; i < count; i++) _shown[i] = -1;
        }

        /// <summary>画面を書き直す。<see cref="UdonMediaPanel"/> から呼ばれます。</summary>
        public void Refresh()
        {
            EnsureInitialized();

            int count = Cards != null ? Cards.Length : 0;
            if (count == 0) return;

            int current = Session != null ? Session.CurrentIndex : -1;

            // 種が変わったときだけ引き直す。毎回引くと、
            // 眺めている最中に中身が入れ替わって選べません。
            if (current != _seed)
            {
                _seed = current;
                Rebuild(current, count);
            }

            bool pressedStillOn = TouchFeedbackSeconds > 0f
                                  && Time.time - _touchedAt < TouchFeedbackSeconds;

            for (int i = 0; i < count; i++)
            {
                if (PressedMarkers != null && i < PressedMarkers.Length
                    && PressedMarkers[i] != null)
                {
                    PressedMarkers[i].SetActive(pressedStillOn && i == _touchedCard);
                }
            }
        }

        /// <summary>おすすめを引き直して、カードへ書き込む。</summary>
        private void Rebuild(int current, int count)
        {
            int found = 0;

            if (current >= 0 && Store != null && Recommendation != null)
            {
                string seedId = Store.GetId(current);
                if (seedId != null && seedId.Length > 0)
                {
                    // さっき聴いたものを外すぶん、多めに出させる。
                    found = Recommendation.GetRelatedRecommendations(seedId, count + SkipDepth);
                }
            }

            int filled = 0;

            for (int i = 0; i < found && filled < count; i++)
            {
                int catalogIndex = Recommendation.GetResultIndex(i);

                // ── さっき聴いたばかりのものは出さない。
                //
                //    おすすめから 1 曲選ぶと、次はその曲が種になります。
                //    ところが「A に似ている B」は、たいてい「B に似ている A」でもあるので、
                //    <b>A → B → A → B と往復し続けます</b>。
                //    直近に聴いたものを外すだけで、この輪は切れます。
                if (WasPlayedRecently(catalogIndex)) continue;

                _shown[filled] = catalogIndex;
                ShowCard(filled, catalogIndex, Recommendation.GetResultReason(i));
                filled++;
            }

            for (int i = filled; i < count; i++)
            {
                _shown[i] = -1;
                if (Cards[i] != null) Cards[i].SetActive(false);
            }

            bool empty = filled == 0;

            if (EmptyMessage != null) EmptyMessage.SetActive(empty);
            if (EmptyText != null)
            {
                // 鳴っていないだけなのか、鳴っているが似た曲が無いのかは別のこと。
                EmptyText.text = current < 0
                    ? "曲を再生すると、似た曲がここに出ます"
                    : "関連する曲はありません";
            }
        }

        /// <summary>
        /// <b>直近この数だけ聴いたものは、おすすめに出さない。</b>
        /// 大きくしすぎると、曲数の少ないカタログでおすすめが空になります。
        /// </summary>
        public int SkipDepth = 6;

        /// <summary>さっき聴いたばかりか(履歴の新しいほうから数えて調べる)。</summary>
        private bool WasPlayedRecently(int catalogIndex)
        {
            if (Session == null || SkipDepth <= 0) return false;

            int count = Session.HistoryCount;
            int from = count - SkipDepth;
            if (from < 0) from = 0;

            for (int i = from; i < count; i++)
            {
                if (Session.GetHistoryAt(i) == catalogIndex) return true;
            }
            return false;
        }

        private void ShowCard(int card, int catalogIndex, int reason)
        {
            if (Cards[card] != null) Cards[card].SetActive(true);

            if (Titles != null && card < Titles.Length && Titles[card] != null)
            {
                Titles[card].text = Store != null ? Store.GetTitle(catalogIndex) : "";
            }

            if (Artists != null && card < Artists.Length && Artists[card] != null)
            {
                Artists[card].text = Store != null ? Store.GetArtist(catalogIndex) : "";
            }

            if (Reasons != null && card < Reasons.Length && Reasons[card] != null)
            {
                Reasons[card].text = Recommendation != null
                    ? Recommendation.DescribeReason(reason)
                    : "おすすめ";
            }

            ShowArtwork(card, catalogIndex);
        }

        /// <summary>
        /// 絵を出す。焼き込んでいなければ、ジャンルの色で塗って頭文字を出します。
        /// <b>空白のままにしない</b>のは、抜けているのか読み込み中なのかが
        /// 分からないと不安になるためです。
        /// </summary>
        private void ShowArtwork(int card, int catalogIndex)
        {
            if (Artworks == null || card >= Artworks.Length || Artworks[card] == null) return;

            Sprite art = Store != null ? Store.GetThumbnail(catalogIndex) : null;
            Image image = Artworks[card];

            if (art != null)
            {
                image.sprite = art;
                image.color = Color.white;

                if (ArtworkFallbacks != null && card < ArtworkFallbacks.Length
                    && ArtworkFallbacks[card] != null)
                {
                    ArtworkFallbacks[card].text = "";
                }
                return;
            }

            image.sprite = null;
            image.color = PaletteSource != null
                ? PaletteSource.GenreColor(Store != null ? Store.GetGenre(catalogIndex) : "")
                : new Color(0.16f, 0.17f, 0.21f, 1f);

            if (ArtworkFallbacks != null && card < ArtworkFallbacks.Length
                && ArtworkFallbacks[card] != null)
            {
                string title = Store != null ? Store.GetTitle(catalogIndex) : "";
                ArtworkFallbacks[card].text = title.Length > 0 ? title.Substring(0, 1) : "♪";
            }
        }

        // ───────── 押された ─────────
        //
        // カードごとに別の名前を用意します。uGUI から呼べるのは
        // 「引数の無いイベント」だけなので、番号を引数で渡せないためです。

        public void Click0() { Click(0); }
        public void Click1() { Click(1); }
        public void Click2() { Click(2); }
        public void Click3() { Click(3); }
        public void Click4() { Click(4); }
        public void Click5() { Click(5); }

        private void Click(int card)
        {
            EnsureInitialized();

            if (_shown == null || card < 0 || card >= _shown.Length) return;

            int catalogIndex = _shown[card];
            if (catalogIndex < 0) return;

            _touchedCard = card;
            _touchedAt = Time.time;

            if (Controller != null) Controller.PlayCatalogIndex(catalogIndex);
        }

        // ───────── 「＋」で再生予定へ ─────────
        //
        // カードは押すとすぐ流れます。<b>いま流したくないけれど覚えておきたい</b>
        // ときのために、一覧と同じ「＋」を付けます。
        // 一覧とカードで操作が違うと、どちらかを覚え直すことになります。

        public void Queue0() { Queue(0); }
        public void Queue1() { Queue(1); }
        public void Queue2() { Queue(2); }
        public void Queue3() { Queue(3); }
        public void Queue4() { Queue(4); }
        public void Queue5() { Queue(5); }

        private void Queue(int card)
        {
            EnsureInitialized();

            if (_shown == null || card < 0 || card >= _shown.Length) return;

            int catalogIndex = _shown[card];
            if (catalogIndex < 0) return;

            _touchedCard = card;
            _touchedAt = Time.time;

            if (Controller != null) Controller.EnqueueCatalogIndex(catalogIndex);
        }
    }
}
