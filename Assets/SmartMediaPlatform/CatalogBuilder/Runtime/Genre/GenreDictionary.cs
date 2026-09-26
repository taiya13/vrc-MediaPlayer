using System.Collections.Generic;

namespace SmartMediaPlatform.CatalogBuilder.Genres
{
    /// <summary>
    /// <b>ジャンルの辞書。</b>Phase6-6。
    ///
    /// <b>ここが「あとから足す場所」です。</b>
    /// キーワードを増やしたいときは <see cref="CreateDefault"/> の中に 1 行足すか、
    /// 外から <see cref="Rule"/> で引いて <c>Add</c> を呼ぶだけで、
    /// 判定の仕組み(<see cref="GenreClassifier"/>)には手を触れません。
    ///
    /// <b>並び順に意味があります。</b>点が同じだったときは<b>先にあるほう</b>を採るので、
    /// <b>細かいジャンルを先、大きいジャンルを後</b>に置いてあります
    /// (House を EDM より先、Metal を Rock より先、など)。
    /// 「EDM House Mix」が House になるのはこのためです。
    ///
    /// <b>「音楽」は入っていません。</b>YouTube のカテゴリ「音楽」は
    /// <b>ほぼ全部の曲に付く</b>ので、ジャンルとしては何も言っていないのと同じです。
    /// 分からないときは <see cref="GenreClassifier.Unknown"/>(その他)を返します。
    /// </summary>
    public sealed class GenreDictionary
    {
        // ───────── ジャンル名(打ち間違い防止に定数で持つ)─────────

        public const string JPop = "J-POP";
        public const string KPop = "K-POP";
        public const string Rock = "Rock";
        public const string Metal = "Metal";
        public const string Edm = "EDM";
        public const string House = "House";
        public const string Trance = "Trance";
        public const string Dubstep = "Dubstep";
        public const string DrumAndBass = "Drum & Bass";
        public const string Hardcore = "Hardcore";
        public const string LoFi = "Lo-fi";
        public const string Jazz = "Jazz";
        public const string Classical = "Classical";
        public const string Orchestra = "Orchestra";
        public const string Piano = "Piano";
        public const string GameMusic = "Game Music";
        public const string Anime = "Anime";
        public const string Vocaloid = "Vocaloid";
        public const string Soundtrack = "Soundtrack";
        public const string Epic = "Epic";
        public const string Remix = "Remix";
        public const string Cover = "Cover";
        public const string Live = "Live";

        private readonly List<GenreRule> _rules = new List<GenreRule>();
        private readonly List<CategoryBias> _bias = new List<CategoryBias>();

        public IReadOnlyList<GenreRule> Rules { get { return _rules; } }

        public int RuleCount { get { return _rules.Count; } }

        /// <summary>辞書に入っている言葉の総数。報告と回帰の目安に使います。</summary>
        public int KeywordCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _rules.Count; i++) count += _rules[i].KeywordCount;
                return count;
            }
        }

        /// <summary>
        /// そのジャンルの決まりを引く。<b>無ければ作って返します</b>ので、
        /// 新しいジャンルを足すのも同じ 1 行で済みます。
        /// </summary>
        public GenreRule Rule(string genre)
        {
            for (int i = 0; i < _rules.Count; i++)
            {
                if (_rules[i].Genre == genre) return _rules[i];
            }

            var rule = new GenreRule(genre);
            _rules.Add(rule);
            return rule;
        }

        public GenreRule Find(string genre)
        {
            for (int i = 0; i < _rules.Count; i++)
            {
                if (_rules[i].Genre == genre) return _rules[i];
            }
            return null;
        }

        public bool RemoveRule(string genre)
        {
            GenreRule found = Find(genre);
            return found != null && _rules.Remove(found);
        }

        // ───────── 取り込み元のカテゴリ(補助だけ)─────────

        /// <summary>取り込み元のカテゴリが、あるジャンルを少し押す。</summary>
        public sealed class CategoryBias
        {
            public string Category = "";
            public string Genre = "";
            public int Bonus;
        }

        public IReadOnlyList<CategoryBias> Biases { get { return _bias; } }

        /// <summary>
        /// カテゴリの後押しを足す。
        ///
        /// <b>これだけではジャンルになりません。</b>
        /// <see cref="GenreClassifier"/> は<b>すでに点が入っているジャンル</b>にしか
        /// この分を足しません。「ゲーム」カテゴリの動画が全部 Game Music に
        /// なってしまうのを防ぐためです。
        /// </summary>
        public GenreDictionary AddCategoryBias(string category, string genre, int bonus)
        {
            var bias = new CategoryBias();
            bias.Category = category == null ? "" : category.Trim();
            bias.Genre = genre;
            bias.Bonus = bonus;

            _bias.Add(bias);
            return this;
        }

        // ───────── 既定の辞書 ─────────

        /// <summary>
        /// はじめから入っている辞書。
        ///
        /// <b>重みの目安</b>
        /// <list type="bullet">
        /// <item>5 …… これが出たらほぼ確定(<c>vocaloid</c> / <c>dnb</c> / アーティスト名)</item>
        /// <item>4 …… かなり強い(<c>cover</c> / <c>live</c> / <c>piano</c>)</item>
        /// <item>3 …… 強いが単独では危うい(<c>chill</c> / <c>opening</c>)</item>
        /// <item>2 …… 添えるだけ(<c>edit</c> / <c>score</c> / <c>fusion</c>)</item>
        /// </list>
        ///
        /// <b>短い英単語は「区切り」が自動で効きます。</b>
        /// <c>rock</c> が <c>rocket</c> に、<c>live</c> が <c>delivery</c> に
        /// 当たらないのはそのためです(<see cref="GenreKeyword.WholeWord"/>)。
        /// </summary>
        public static GenreDictionary CreateDefault()
        {
            var dictionary = new GenreDictionary();

            // ── 細かいものから先に。点が並んだら先にあるほうを採るため。

            dictionary.Rule(Vocaloid)
                .Add(5, "vocaloid", "ボカロ", "ボーカロイド", "初音ミク", "hatsune miku",
                        "鏡音リン", "鏡音レン", "kagamine", "巡音ルカ", "megurine",
                        "重音テト", "kasane teto", "synthesizer v", "synthv", "cevio",
                        "voisona", "可不", "kafu", "花譜", "音街ウナ", "結月ゆかり",
                        "vsinger", "ボカロP", "ボカコレ")
                .Add(4, "miku", "utau", "うたう", "deco*27", "wowaka", "kemu", "n-buna",
                        "ボカロ曲", "vocaloid original", "ボカロオリジナル")
                .Add(3, "gumi", "meiko", "kaito", "flower", "ボイスロイド", "voiceroid",
                        "synthetic voice");

            dictionary.Rule(Anime)
                .Add(5, "アニソン", "anison", "アニメ主題歌", "tvアニメ", "テレビアニメ",
                        "ノンクレジット", "ncop", "nced", "non credit")
                .Add(4, "anime", "アニメ", "劇場版", "アニメop", "アニメed", "tv size",
                        "tvサイズ", "オープニングテーマ", "エンディングテーマ")
                .Add(3, "opening theme", "ending theme", "主題歌", "挿入歌",
                        "キャラクターソング", "キャラソン", "声優");

            dictionary.Rule(GameMusic)
                .Add(5, "game music", "ゲーム音楽", "ゲームサントラ", "video game music",
                        "chiptune", "チップチューン", "8bit music", "ファミコン音楽")
                .Add(4, "bgm", "原神", "genshin", "ゼルダ", "zelda", "final fantasy",
                        "ファイナルファンタジー", "undertale", "アンダーテール", "東方project",
                        "東方アレンジ", "splatoon", "スプラトゥーン", "ポケモン", "pokemon",
                        "minecraft", "マインクラフト", "モンスターハンター", "monster hunter",
                        "ペルソナ", "persona 5", "nier", "ニーア", "kirby", "カービィ")
                .Add(3, "東方", "nintendo", "任天堂", "capcom", "カプコン", "konami",
                        "ゲーム実況bgm", "sound track game");

            dictionary.Rule(Soundtrack)
                .Add(5, "soundtrack", "サウンドトラック", "サントラ", "劇伴",
                        "澤野弘之", "hiroyuki sawano", "久石譲", "joe hisaishi",
                        "hans zimmer", "ハンス・ジマー", "john williams", "菅野よう子",
                        "yoko kanno", "梶浦由記", "yuki kajiura", "original score")
                .Add(4, "ost", "film music", "映画音楽", "劇中歌", "ドラマ音楽")
                .AddRaw("score", 2, true)
                .AddRaw("theme", 2, true);

            dictionary.Rule(Epic)
                .Add(5, "two steps from hell", "audiomachine", "thomas bergersen",
                        "really slow motion", "position music", "immediate music",
                        "brand x music", "epic music", "trailer music", "予告編音楽")
                .Add(4, "epic orchestral", "cinematic music", "battle music", "壮大な")
                .AddRaw("epic", 4, true)
                .AddRaw("cinematic", 3, true);

            dictionary.Rule(Orchestra)
                .Add(5, "orchestra", "orchestral", "オーケストラ", "管弦楽", "交響楽団",
                        "philharmonic", "フィルハーモニー", "吹奏楽", "wind orchestra")
                .Add(3, "brass band", "ブラスバンド", "弦楽四重奏", "string quartet",
                        "指揮", "conductor");

            dictionary.Rule(Classical)
                .Add(5, "classical music", "クラシック音楽", "bach", "バッハ", "mozart",
                        "モーツァルト", "beethoven", "ベートーヴェン", "chopin", "ショパン",
                        "tchaikovsky", "チャイコフスキー", "debussy", "ドビュッシー",
                        "vivaldi", "ヴィヴァルディ", "schubert", "シューベルト",
                        "brahms", "ブラームス", "ravel", "ラヴェル", "handel", "ヘンデル")
                .Add(4, "classical", "クラシック", "交響曲", "symphony", "協奏曲",
                        "concerto", "ソナタ", "sonata", "ノクターン", "nocturne",
                        "レクイエム", "requiem", "前奏曲", "prelude", "fugue", "フーガ")
                .AddRaw("opus", 2, true)
                .AddRaw("liszt", 5, true);

            dictionary.Rule(Piano)
                .Add(5, "ピアノ", "pianist", "ピアニスト", "piano solo", "ピアノソロ",
                        "piano arrangement", "ピアノアレンジ", "grand piano", "連弾",
                        "piano version", "ピアノ演奏")
                .AddRaw("piano", 4, true)
                .AddRaw("keyboard", 2, true);

            dictionary.Rule(Jazz)
                .Add(5, "jazz", "ジャズ", "bebop", "ビバップ", "big band", "ビッグバンド",
                        "bossa nova", "ボサノバ", "blue note", "ragtime", "ラグタイム",
                        "smooth jazz", "ジャズピアノ", "jazz hop")
                .Add(3, "saxophone", "サックス", "trumpet", "トランペット", "セッション")
                .AddRaw("swing", 3, true)
                .AddRaw("fusion", 2, true);

            dictionary.Rule(LoFi)
                .Add(5, "lo-fi", "lofi", "lo fi", "ローファイ", "chillhop", "lofi hip hop",
                        "study beats", "sleep music", "作業用bgm")
                .Add(3, "chill out", "チルアウト", "relaxing music", "ambient", "アンビエント",
                        "睡眠用", "勉強用", "beats to")
                .AddRaw("chill", 3, true);

            dictionary.Rule(Hardcore)
                .Add(5, "hardcore", "ハードコア", "gabber", "ガバキック", "speedcore",
                        "uk hardcore", "happy hardcore", "j-core", "jcore", "frenchcore",
                        "camellia", "かめりあ", "hardstyle", "ハードスタイル")
                .Add(3, "nightcore", "ナイトコア", "rawstyle", "ガバ");

            dictionary.Rule(DrumAndBass)
                .Add(5, "drum & bass", "drum and bass", "drum n bass", "drum'n'bass",
                        "ドラムンベース", "ドラムン", "neurofunk", "liquid dnb",
                        "breakcore", "ブレイクコア")
                .AddRaw("dnb", 5, true)
                .AddRaw("jungle", 2, true);

            dictionary.Rule(Dubstep)
                .Add(5, "dubstep", "ダブステップ", "ダブステ", "brostep", "riddim",
                        "skrillex", "スクリレックス", "melodic dubstep")
                .Add(3, "bass drop", "heavy bass", "ベースミュージック");

            dictionary.Rule(Trance)
                .Add(5, "psytrance", "サイトランス", "サイケデリックトランス",
                        "uplifting trance", "vocal trance", "goa trance",
                        "armin van buuren", "above & beyond", "ferry corsten",
                        "トランス音楽")
                .AddRaw("trance", 5, true);

            dictionary.Rule(House)
                .Add(5, "deep house", "progressive house", "future house", "tech house",
                        "tropical house", "electro house", "bass house", "ハウスミュージック",
                        "daft punk", "ダフトパンク", "house music")
                .Add(3, "ハウス", "disco house", "funky house")
                // 細かいジャンルの言葉は 5。大きいほう(EDM)と並んだとき、
                // 辞書の並び順で細かいほうが勝つようにするため。
                .AddRaw("house", 5, true);

            dictionary.Rule(Edm)
                .Add(5, "alan walker", "アランウォーカー", "marshmello", "martin garrix",
                        "avicii", "アヴィーチー", "david guetta", "calvin harris",
                        "zedd", "kygo", "illenium", "tiesto", "ティエスト",
                        "electronic dance music", "future bass", "フューチャーベース")
                .Add(3, "big room", "electro pop", "エレクトロ", "dance music",
                        "synthwave", "シンセウェイブ", "club music", "クラブミュージック")
                .AddRaw("edm", 5, true)
                .AddRaw("electro", 3, true);

            dictionary.Rule(Metal)
                .Add(5, "heavy metal", "ヘヴィメタル", "ヘビメタ", "death metal",
                        "デスメタル", "black metal", "metalcore", "メタルコア",
                        "thrash metal", "スラッシュメタル", "djent", "babymetal",
                        "ベビーメタル", "metallica", "メタリカ", "slipknot",
                        "iron maiden", "doom metal")
                .Add(3, "screamo", "growl", "デスボイス", "headbang", "ヘドバン")
                .AddRaw("metal", 5, true);

            dictionary.Rule(Rock)
                .Add(5, "hard rock", "alternative rock", "オルタナティブロック", "オルタナ",
                        "indie rock", "visual kei", "ヴィジュアル系", "ビジュアル系",
                        "j-rock", "jrock", "one ok rock", "ワンオク", "radwimps",
                        "asian kung-fu generation", "ellegarden", "bump of chicken",
                        "unison square garden", "凛として時雨", "my first story",
                        "linkin park", "リンキンパーク", "nirvana", "foo fighters",
                        "green day", "パンクロック", "ロックバンド")
                .Add(3, "guitar riff", "ギターリフ", "ギターソロ", "ロック", "バンドサウンド")
                .AddRaw("rock", 4, true)
                .AddRaw("punk", 3, true)
                .AddRaw("band", 2, true);

            dictionary.Rule(KPop)
                .Add(5, "k-pop", "kpop", "ケーポップ", "케이팝", "bts", "방탄소년단",
                        "blackpink", "ブラックピンク", "stray kids", "newjeans",
                        "le sserafim", "red velvet", "girls generation", "소녀시대",
                        "enhypen", "seventeen", "aespa", "itzy", "韓国アイドル",
                        "korean pop", "케이 팝")
                .Add(3, "韓国", "アイドルグループ")
                .AddRaw("exo", 3, true)
                .AddRaw("nct", 3, true)
                .AddRaw("twice", 3, true);

            dictionary.Rule(JPop)
                .Add(5, "j-pop", "jpop", "ジェイポップ", "邦楽", "yoasobi", "ヨアソビ",
                        "米津玄師", "kenshi yonezu", "あいみょん", "aimyon", "king gnu",
                        "キングヌー", "official髭男dism", "ヒゲダン", "髭男",
                        "back number", "sekai no owari", "セカオワ", "サカナクション",
                        "星野源", "宇多田ヒカル", "utada hikaru", "椎名林檎",
                        "藤井風", "fujii kaze", "vaundy", "ヨルシカ", "yorushika",
                        "ずっと真夜中でいいのに", "ずとまよ", "緑黄色社会", "milet",
                        "乃木坂46", "櫻坂46", "日向坂46", "akb48", "perfume")
                .Add(3, "日本のポップス", "歌謡曲", "シティポップ", "city pop", "アイドルソング")
                .AddRaw("ado", 4, true)
                .AddRaw("aimer", 3, true)
                .AddRaw("lisa", 3, true)
                .AddRaw("tuyu", 4, true);

            // ── 修飾ジャンル(何をしたか)。曲そのものの種類より後ろに置く。

            dictionary.Rule(Remix)
                .Add(5, "remix", "リミックス", "bootleg", "mashup", "マッシュアップ",
                        "vip mix", "extended mix", "rework", "リアレンジ")
                .Add(3, "アレンジ", "arrange", "re-arrange", "flip")
                .AddRaw("edit", 2, true)
                .AddRaw("mix", 2, true);

            dictionary.Rule(Cover)
                .Add(5, "歌ってみた", "弾いてみた", "叩いてみた", "演奏してみた",
                        "covered by", "cover song", "acoustic cover", "カヴァー",
                        "耳コピ", "tribute", "トリビュート")
                .Add(3, "カバー", "cover ver", "vocal cover")
                .AddRaw("cover", 4, true);

            dictionary.Rule(Live)
                .Add(5, "ライブ映像", "ライヴ", "生演奏", "live performance",
                        "live session", "live at", "武道館", "ドームツアー",
                        "一発撮り", "unplugged", "アンプラグド", "live version")
                .Add(3, "ライブ", "concert", "コンサート", "公演", "フェス")
                .AddRaw("live", 4, true)
                .AddRaw("tour", 2, true)
                .AddRaw("festival", 2, true);

            // ── 取り込み元のカテゴリは「後押し」だけ。
            //    「音楽」はほぼ全部の曲に付くので、何の後押しにもしない。
            dictionary.AddCategoryBias("ゲーム", GameMusic, 3);
            dictionary.AddCategoryBias("映画・アニメ", Anime, 3);
            dictionary.AddCategoryBias("映画・アニメ", Soundtrack, 2);

            return dictionary;
        }
    }
}
