using NUnit.Framework;
using SmartMediaPlatform.CatalogBuilder;
using SmartMediaPlatform.CatalogBuilder.Genres;

namespace SmartMediaPlatform.CatalogBuilder.Tests
{
    /// <summary>
    /// Phase6-6: ジャンル判定の検証。
    ///
    /// <b>ここが辞書の回帰試験になります。</b>キーワードを足したあとにここを通せば、
    /// <b>足したせいで別の曲が別ジャンルへ動いていないか</b>が分かります。
    /// </summary>
    public sealed class GenreClassifierTests
    {
        private static GenreSignals Of(string title)
        {
            var signals = new GenreSignals();
            signals.Title = title;
            return signals;
        }

        private static GenreSignals Of(string title, string channel)
        {
            GenreSignals signals = Of(title);
            signals.ChannelName = channel;
            return signals;
        }

        private static string Classify(string title)
        {
            return GenreClassifier.Shared.Classify(Of(title));
        }

        private static string Classify(string title, string channel)
        {
            return GenreClassifier.Shared.Classify(Of(title, channel));
        }

        // ───────── 依頼にあった判定例 ─────────

        [Test]
        public void TheExamplesFromTheRequestAreClassified()
        {
            Assert.AreEqual(GenreDictionary.JPop, Classify("YOASOBI - アイドル"));
            Assert.AreEqual(GenreDictionary.JPop, Classify("Ado - 唱"));
            Assert.AreEqual(GenreDictionary.Edm, Classify("Alan Walker - Faded"));
            Assert.AreEqual(GenreDictionary.Vocaloid, Classify("Hatsune Miku - Senbonzakura"));
            Assert.AreEqual(GenreDictionary.Soundtrack, Classify("澤野弘之 - Before My Body Is Dry"));
            Assert.AreEqual(GenreDictionary.Epic, Classify("Two Steps From Hell - Victory"));
            Assert.AreEqual(GenreDictionary.Live, Classify("Official Live"));
        }

        [Test]
        public void CamelliaIsHardcoreOrEdm()
        {
            string genre = Classify("Camellia - Exit This Earth's Atomosphere");

            // 依頼では「Hardcore または EDM」。どちらでも通す。
            Assert.IsTrue(genre == GenreDictionary.Hardcore || genre == GenreDictionary.Edm,
                          "Hardcore か EDM のはずが " + genre);
        }

        [Test]
        public void PianoCoverIsPianoOrCover()
        {
            string genre = Classify("Piano Cover");

            Assert.IsTrue(genre == GenreDictionary.Piano || genre == GenreDictionary.Cover,
                          "Piano か Cover のはずが " + genre);
        }

        // ───────── 「音楽」を返さない ─────────

        [Test]
        public void TheMusicCategoryNeverBecomesAGenre()
        {
            var signals = Of("なんの手がかりも無い曲");
            signals.SourceCategory = "音楽";

            Assert.AreEqual(GenreClassifier.Unknown, GenreClassifier.Shared.Classify(signals),
                            "「音楽」はほぼ全部の曲に付くので、ジャンルとして使わない");
        }

        [Test]
        public void TheDictionaryHasNoMusicGenre()
        {
            GenreDictionary dictionary = GenreDictionary.CreateDefault();

            Assert.IsNull(dictionary.Find("音楽"));
            Assert.IsNull(dictionary.Find("Music"));
        }

        [Test]
        public void NothingRecognisedFallsBackToOther()
        {
            Assert.AreEqual(GenreClassifier.Unknown, Classify("aaaa bbbb cccc"));
            Assert.AreEqual(GenreClassifier.Unknown, Classify(""));
            Assert.AreEqual(GenreClassifier.Unknown, GenreClassifier.Shared.Classify(null));
        }

        // ───────── カテゴリは補助だけ ─────────

        [Test]
        public void ACategoryCannotCreateAGenreOnItsOwn()
        {
            var signals = Of("よく分からない動画");
            signals.SourceCategory = "ゲーム";

            Assert.AreEqual(GenreClassifier.Unknown, GenreClassifier.Shared.Classify(signals),
                            "カテゴリだけで Game Music にしない");
        }

        [Test]
        public void ACategoryPushesAGenreThatAlreadyScored()
        {
            var plain = Of("Zelda BGM");
            var boosted = Of("Zelda BGM");
            boosted.SourceCategory = "ゲーム";

            GenreScore[] before = GenreClassifier.Shared.Rank(plain);
            GenreScore[] after = GenreClassifier.Shared.Rank(boosted);

            Assert.AreEqual(GenreDictionary.GameMusic, after[0].Genre);
            Assert.Greater(after[0].Score, before[0].Score, "すでに点が入っていれば押す");
        }

        // ───────── 区切りの誤爆 ─────────

        [Test]
        public void ShortEnglishWordsDoNotMatchInsideOtherWords()
        {
            Assert.AreNotEqual(GenreDictionary.Cover, Classify("Discovery Channel Theme"));
            Assert.AreNotEqual(GenreDictionary.Live, Classify("Delivery Truck Sound"));
            Assert.AreNotEqual(GenreDictionary.Rock, Classify("Rocket Launch Ambience"));
            Assert.AreNotEqual(GenreDictionary.Epic, Classify("Epicenter Report"));
            Assert.AreNotEqual(GenreDictionary.Trance, Classify("Entrance Hall"));
        }

        [Test]
        public void JapaneseKeywordsDoNotNeedWordBreaks()
        {
            Assert.AreEqual(GenreDictionary.Vocaloid, Classify("これは初音ミクの曲です"));
            Assert.AreEqual(GenreDictionary.Cover, Classify("有名曲を歌ってみた結果"));
        }

        [Test]
        public void AsciiKeywordsStillMatchNextToJapanese()
        {
            Assert.AreEqual(GenreDictionary.Rock, Classify("激しいrockな一曲"),
                            "日本語は区切りとして扱う");
        }

        // ───────── 欄ごとの重み ─────────

        [Test]
        public void TheTitleCountsMoreThanTheDescription()
        {
            var inTitle = new GenreSignals();
            inTitle.Title = "Jazz Session";

            var inDescription = new GenreSignals();
            inDescription.Title = "セッション録音";
            inDescription.Description = "jazz";

            GenreScore[] title = GenreClassifier.Shared.Rank(inTitle);
            GenreScore[] description = GenreClassifier.Shared.Rank(inDescription);

            Assert.Greater(title[0].Score, description[0].Score);
        }

        [Test]
        public void ADescriptionAloneIsTooWeakToDecide()
        {
            var signals = new GenreSignals();
            signals.Title = "曲";
            signals.Description = "score";   // 説明 1 × 重み 2 = 2 点

            Assert.AreEqual(GenreClassifier.Unknown, GenreClassifier.Shared.Classify(signals),
                            "説明に 1 語かすっただけで決めない");
        }

        [Test]
        public void TagsAreTrustedLikeTheTitle()
        {
            var signals = new GenreSignals();
            signals.Title = "名前のない曲";
            signals.Tags = new[] { "dubstep" };

            Assert.AreEqual(GenreDictionary.Dubstep, GenreClassifier.Shared.Classify(signals));
        }

        [Test]
        public void TagsDoNotBleedIntoEachOther()
        {
            var signals = new GenreSignals();
            signals.Title = "曲";

            // つなげると "…rock" + "et…" のように隣同士がくっついて誤爆しうる。
            signals.Tags = new[] { "space", "rocket" };

            Assert.AreNotEqual(GenreDictionary.Rock, GenreClassifier.Shared.Classify(signals));
        }

        [Test]
        public void TheChannelNameIsUsed()
        {
            Assert.AreEqual(GenreDictionary.Vocaloid, Classify("新曲", "初音ミク Official"));
        }

        // ───────── 同点のときの決まり ─────────

        [Test]
        public void TheMoreSpecificGenreWinsATie()
        {
            Assert.AreEqual(GenreDictionary.House, Classify("EDM House Mix"),
                            "辞書は細かいものが先。同点なら細かいほう");

            Assert.AreEqual(GenreDictionary.Metal, Classify("Rock Metal Mix"));
        }

        [Test]
        public void SubGenresAreNotSwallowedByEdm()
        {
            Assert.AreEqual(GenreDictionary.Trance, Classify("Uplifting Trance Mix"));
            Assert.AreEqual(GenreDictionary.DrumAndBass, Classify("Liquid DnB Mix"));
            Assert.AreEqual(GenreDictionary.Dubstep, Classify("Melodic Dubstep Mix"));
        }

        [Test]
        public void DaftPunkIsHouseNotRock()
        {
            Assert.AreEqual(GenreDictionary.House, Classify("Daft Punk - One More Time"),
                            "「punk」に引っぱられないこと");
        }

        // ───────── 全ジャンルがそろっているか ─────────

        [Test]
        public void EveryRequiredGenreCanBeReached()
        {
            Assert.AreEqual(GenreDictionary.JPop, Classify("J-POP メドレー"));
            Assert.AreEqual(GenreDictionary.KPop, Classify("BLACKPINK - How You Like That"));
            Assert.AreEqual(GenreDictionary.Rock, Classify("ONE OK ROCK - Wherever You Are"));
            Assert.AreEqual(GenreDictionary.Metal, Classify("BABYMETAL - Gimme Chocolate"));
            Assert.AreEqual(GenreDictionary.Edm, Classify("Martin Garrix - Animals"));
            Assert.AreEqual(GenreDictionary.House, Classify("Deep House Mix"));
            Assert.AreEqual(GenreDictionary.Trance, Classify("Armin van Buuren - Blah Blah Blah"));
            Assert.AreEqual(GenreDictionary.Dubstep, Classify("Skrillex - Bangarang"));
            Assert.AreEqual(GenreDictionary.DrumAndBass, Classify("Neurofunk DnB"));
            Assert.AreEqual(GenreDictionary.Hardcore, Classify("UK Hardcore Mix"));
            Assert.AreEqual(GenreDictionary.LoFi, Classify("lofi hip hop radio"));
            Assert.AreEqual(GenreDictionary.Jazz, Classify("Bossa Nova Cafe"));
            Assert.AreEqual(GenreDictionary.Classical, Classify("Chopin - Nocturne Op.9"));
            Assert.AreEqual(GenreDictionary.Orchestra, Classify("フィルハーモニー管弦楽団 演奏"));
            Assert.AreEqual(GenreDictionary.Piano, Classify("ピアノソロ 演奏"));
            Assert.AreEqual(GenreDictionary.GameMusic, Classify("Undertale OST Megalovania"));
            Assert.AreEqual(GenreDictionary.Anime, Classify("アニソンメドレー TVアニメ"));
            Assert.AreEqual(GenreDictionary.Vocaloid, Classify("ボカロメドレー"));
            Assert.AreEqual(GenreDictionary.Soundtrack, Classify("久石譲 - Summer"));
            Assert.AreEqual(GenreDictionary.Epic, Classify("Epic Trailer Music"));
            Assert.AreEqual(GenreDictionary.Remix, Classify("Bootleg Remix"));
            Assert.AreEqual(GenreDictionary.Cover, Classify("歌ってみた"));
            Assert.AreEqual(GenreDictionary.Live, Classify("武道館 ライブ映像"));
        }

        // ───────── 辞書をあとから育てられるか ─────────

        [Test]
        public void KeywordsCanBeAddedWithoutTouchingTheClassifier()
        {
            var dictionary = GenreDictionary.CreateDefault();
            var classifier = new GenreClassifier(dictionary);

            Assert.AreEqual(GenreClassifier.Unknown, classifier.Classify(Of("すこやか楽団の新譜")));

            dictionary.Rule(GenreDictionary.Orchestra).Add(5, "すこやか楽団");

            Assert.AreEqual(GenreDictionary.Orchestra, classifier.Classify(Of("すこやか楽団の新譜")),
                            "辞書に 1 行足すだけで効く");
        }

        [Test]
        public void ANewGenreCanBeAdded()
        {
            var dictionary = GenreDictionary.CreateDefault();
            var classifier = new GenreClassifier(dictionary);

            dictionary.Rule("Reggae").Add(5, "reggae", "レゲエ", "ska");

            Assert.AreEqual("Reggae", classifier.Classify(Of("Reggae Mix")));
        }

        [Test]
        public void TheSameKeywordIsNotCountedTwice()
        {
            var dictionary = GenreDictionary.CreateDefault();
            dictionary.Rule("Test").Add(5, "テスト").Add(3, "テスト");

            GenreRule rule = dictionary.Find("Test");

            Assert.AreEqual(1, rule.KeywordCount);
            Assert.AreEqual(5, rule.Keywords[0].Weight, "強いほうを残す");
        }

        [Test]
        public void RepeatingAWordDoesNotRaiseTheScore()
        {
            GenreScore[] once = GenreClassifier.Shared.Rank(Of("jazz"));
            GenreScore[] thrice = GenreClassifier.Shared.Rank(Of("jazz jazz jazz"));

            Assert.AreEqual(once[0].Score, thrice[0].Score,
                            "同じ言葉を並べただけの動画が上位に来ないこと");
        }

        [Test]
        public void KeywordsCanBeRemoved()
        {
            var dictionary = GenreDictionary.CreateDefault();
            var classifier = new GenreClassifier(dictionary);

            Assert.IsTrue(dictionary.Rule(GenreDictionary.Live).Remove("live"));
            Assert.AreEqual(GenreClassifier.Unknown, classifier.Classify(Of("Official Live")));
        }

        // ───────── 外の手がかり(将来の MusicBrainz)─────────

        [Test]
        public void AnExternalHintCanTipTheResult()
        {
            var classifier = new GenreClassifier();
            classifier.AddHintProvider(new FakeHints(GenreDictionary.Jazz, 30));

            Assert.AreEqual(GenreDictionary.Jazz, classifier.Classify(Of("ONE OK ROCK - Live")),
                            "外部 DB のほうが強ければそちらを採る");
        }

        [Test]
        public void AnExternalHintCanAddAGenreTheDictionaryDoesNotKnow()
        {
            var classifier = new GenreClassifier();
            classifier.AddHintProvider(new FakeHints("Shoegaze", 20));

            Assert.AreEqual("Shoegaze", classifier.Classify(Of("知らない曲")));
        }

        [Test]
        public void AnUnavailableProviderIsIgnored()
        {
            var classifier = new GenreClassifier();

            var offline = new FakeHints(GenreDictionary.Jazz, 99);
            offline.Available = false;
            classifier.AddHintProvider(offline);

            Assert.AreEqual(GenreDictionary.Vocaloid, classifier.Classify(Of("初音ミク")),
                            "外部が落ちていても辞書だけで動く");
        }

        [Test]
        public void ProvidersAreNotAddedTwice()
        {
            var classifier = new GenreClassifier();
            var provider = new FakeHints(GenreDictionary.Jazz, 10);

            classifier.AddHintProvider(provider);
            classifier.AddHintProvider(provider);
            classifier.AddHintProvider(null);

            Assert.AreEqual(1, classifier.HintProviderCount);
        }

        // ───────── 理由が出るか ─────────

        [Test]
        public void TheReasonIsReadable()
        {
            string explained = GenreClassifier.Shared.Explain(Of("初音ミク - メルト"));

            StringAssert.Contains(GenreDictionary.Vocaloid, explained);
            StringAssert.Contains("見出し", explained);
        }

        [Test]
        public void RankReturnsEverythingThatScored()
        {
            GenreScore[] ranked = GenreClassifier.Shared.Rank(Of("Piano Cover of a Rock song"));

            Assert.Greater(ranked.Length, 1, "2 位以下もタグに使えるよう全部返す");
            Assert.GreaterOrEqual(ranked[0].Score, ranked[1].Score, "点の高い順");
        }

        // ───────── まとめて判定 ─────────

        [Test]
        public void BulkClassifyFillsOnlyEmptyGenres()
        {
            var draft = new CatalogDraft();
            draft.Add(DraftItem("a", "初音ミク - メルト", ""));
            draft.Add(DraftItem("b", "ONE OK ROCK - Wherever", "手で入れたジャンル"));

            int changed = CatalogBulkEdit.ClassifyGenres(
                draft.Items, new[] { 0, 1 }, GenreClassifier.Shared, false);

            Assert.AreEqual(1, changed);
            Assert.AreEqual(GenreDictionary.Vocaloid, draft.GetAt(0).Genre);
            Assert.AreEqual("手で入れたジャンル", draft.GetAt(1).Genre, "手で直したものを潰さない");
        }

        [Test]
        public void BulkClassifyCanOverwriteWhenAsked()
        {
            var draft = new CatalogDraft();
            draft.Add(DraftItem("a", "初音ミク - メルト", "音楽"));

            CatalogBulkEdit.ClassifyGenres(
                draft.Items, new[] { 0 }, GenreClassifier.Shared, true);

            Assert.AreEqual(GenreDictionary.Vocaloid, draft.GetAt(0).Genre,
                            "Phase6-4 までの「音楽」を上書きできる");
        }

        [Test]
        public void BulkClassifyNeverWipesAGenreWithOther()
        {
            var draft = new CatalogDraft();
            draft.Add(DraftItem("a", "手がかりの無い曲", "自分で決めたジャンル"));

            CatalogBulkEdit.ClassifyGenres(
                draft.Items, new[] { 0 }, GenreClassifier.Shared, true);

            Assert.AreEqual("自分で決めたジャンル", draft.GetAt(0).Genre,
                            "分からなかったからといって「その他」で消さない");
        }

        // ───────── 辞書の大きさ ─────────

        [Test]
        public void TheDictionaryCoversEveryRequiredGenre()
        {
            GenreDictionary dictionary = GenreDictionary.CreateDefault();

            string[] required =
            {
                GenreDictionary.JPop, GenreDictionary.KPop, GenreDictionary.Rock,
                GenreDictionary.Metal, GenreDictionary.Edm, GenreDictionary.House,
                GenreDictionary.Trance, GenreDictionary.Dubstep, GenreDictionary.DrumAndBass,
                GenreDictionary.LoFi, GenreDictionary.Jazz, GenreDictionary.Classical,
                GenreDictionary.Orchestra, GenreDictionary.Piano, GenreDictionary.GameMusic,
                GenreDictionary.Anime, GenreDictionary.Vocaloid, GenreDictionary.Soundtrack,
                GenreDictionary.Epic, GenreDictionary.Remix, GenreDictionary.Cover,
                GenreDictionary.Live, GenreDictionary.Hardcore,
            };

            for (int i = 0; i < required.Length; i++)
            {
                Assert.IsNotNull(dictionary.Find(required[i]), required[i] + " が辞書に無い");
            }

            Assert.AreEqual(required.Length, dictionary.RuleCount,
                            "増やしたらこの数も直すこと(数え漏れ防止)");
            Assert.AreEqual(528, dictionary.KeywordCount,
                            "キーワードを足したらこの数も直すこと(数え漏れ防止)");
        }

        // ───────── 道具 ─────────

        private static CatalogDraftItem DraftItem(string id, string title, string genre)
        {
            var item = new CatalogDraftItem();
            item.Id = id;
            item.Title = title;
            item.Url = "https://example.com/" + id;
            item.Genre = genre;
            return item;
        }

        /// <summary>通信しない手がかり係。将来の MusicBrainz の代わり。</summary>
        private sealed class FakeHints : IGenreHintProvider
        {
            private readonly string _genre;
            private readonly int _score;

            public bool Available = true;

            public FakeHints(string genre, int score)
            {
                _genre = genre;
                _score = score;
            }

            public string Name { get { return "テスト"; } }

            public bool IsAvailable { get { return Available; } }

            public GenreHint[] Suggest(GenreSignals signals)
            {
                return new[] { new GenreHint(_genre, _score, "決め打ち") };
            }
        }
    }
}
