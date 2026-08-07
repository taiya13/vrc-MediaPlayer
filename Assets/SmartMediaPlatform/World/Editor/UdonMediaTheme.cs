#if UNITY_EDITOR && VRC_SDK_VRCSDK3
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.World.EditorTools
{
    /// <summary>
    /// <b>Smart Media Player のデザイン言語 ——「Frost」。</b>Phase7-7。
    ///
    /// ───────────────────────────────────────────────
    /// <b>なぜ「真似」をやめたのか</b>
    ///
    /// Apple Music も Spotify も、<b>手のひらか机の上</b>で見るために作られています。
    /// ワールドの中は前提が違います。
    /// <list type="bullet">
    /// <item>見る距離は <b>1〜3 m</b>。腕の長さではない</item>
    /// <item>板の<b>後ろにワールドが透けている</b>。画面には無い層がある</item>
    /// <item>ポインターは<b>手ぶれで揺れる</b>。1 px を狙えない</item>
    /// <item>白い面は<b>光源になる</b>。眩しさが疲れに直結する</item>
    /// </list>
    /// この 4 つに合わせて作り直すと、<b>どうやっても既存アプリとは別の形</b>に
    /// なります。「Frost」はその答えです。
    ///
    /// ───────────────────────────────────────────────
    /// <b>Frost の 5 つの決まりごと</b>
    ///
    /// <list type="number">
    /// <item><b>面は「凍った硝子」。</b>不透明な板を置きません。
    ///       白を <b>55〜88%</b> の濃さで重ね、<b>後ろのワールドを透かします</b>。
    ///       板が空間に浮いて見えるので、パネルがワールドから浮き上がります。
    ///       段差は濃さではなく<b>「透け具合」と「縁の光」</b>で作ります
    ///       —— これが Frost のいちばんの特徴です</item>
    ///
    /// <item><b>縁が光る。</b>硝子の上端に<b>白い細い線</b>を 1 本引きます。
    ///       たったこれだけで、板が「面」ではなく<b>「厚みのある硝子」</b>に見えます。
    ///       影(下)と縁の光(上)が対になって、はじめて浮いて見えます</item>
    ///
    /// <item><b>色は「いま」を指すためだけにある。</b>
    ///       彩度を持つのは <see cref="Accent"/> ひとつ、
    ///       出る場所は<b>再生中・選択中・押せる主役</b>だけです。
    ///       ジャンルも状態も色で描き分けません</item>
    ///
    /// <item><b>絵が主役、ボタンは脇役。</b>
    ///       いちばん面積を取るのはアートワークです。
    ///       ボタンは硝子に溶かし、押せるものだけ縁を立てます</item>
    ///
    /// <item><b>2 m から読める。</b>文字は画面用の常識より 1.5 段大きく、
    ///       いちばん小さい文字でも <b>18 px(≒ 2.3 cm)</b>を下回りません。
    ///       触れる所は<b>短辺 4.5 cm 以上</b></item>
    /// </list>
    ///
    /// ───────────────────────────────────────────────
    /// <b>動き</b>
    ///
    /// ワールドでは<b>「押した気がしない」が最大の不満</b>になります。
    /// 画面と違ってカーソルの影も指の感触も無いからです。
    /// そこで<b>状態が変わったときは必ず動かします</b>
    /// (<see cref="MotionFast"/> / <see cref="MotionBase"/> / <see cref="MotionSlow"/>)。
    ///
    /// <b>ホバー(指しただけ)では動かしません。</b>
    /// ワールド内 uGUI のポインター通知が実機で届かないため、
    /// 「指している」を知る手段が無いからです。代わりに
    /// <b>押した瞬間・選ばれた瞬間・曲が変わった瞬間</b>を動かします。
    /// </summary>
    public static class UdonMediaTheme
    {
        // ═════════ 余白 ═════════
        //
        // 8 の倍数だけ。<b>Frost は余白が多い</b>のが特徴です。
        // 罫線を引かないぶん、区切りは全部ここが担います。

        /// <summary>基準となる 1 単位。ここに無い数を直接書かないこと。</summary>
        public const float Unit = 8f;

        public const float Space1 = Unit;          //  8 …… 文字と文字のあいだ
        public const float Space2 = Unit * 2f;     // 16 …… 部品どうしのあいだ
        public const float Space3 = Unit * 3f;     // 24 …… 節と節のあいだ
        public const float Space4 = Unit * 4f;     // 32 …… 画面のふち
        public const float Space5 = Unit * 5f;     // 40 …… カードの中の余白
        public const float Space6 = Unit * 6f;     // 48 …… 大きな区切り

        // ═════════ 角丸 ═════════
        //
        // <b>Frost の角は大きい。</b>硝子板は角が丸いほど「厚み」に見えます。
        // 小さい角丸は、遠くから見ると<b>ただの四角</b>にしか見えません。

        /// <summary>小さいもの(タグ・つまみ)。</summary>
        public const int RadiusSmall = 8;

        /// <summary>ふつうのもの(ボタン・行カード)。</summary>
        public const int RadiusMedium = 14;

        /// <summary>大きいもの(カード・絵)。</summary>
        public const int RadiusLarge = 22;

        /// <summary>いちばん大きいもの(パネルそのもの)。</summary>
        public const int RadiusXLarge = 30;

        /// <summary>丸(円形ボタン)。使う側が高さの半分を渡す。</summary>
        public const int RadiusPill = 999;

        // ═════════ 文字の大きさ ═════════
        //
        // 4 段だけ。これ以外を使わない。
        // <b>いちばん小さい段でも 18 px</b>(1 px ≒ 1.3 mm なので約 2.3 cm)。
        // 2 m 離れて読める下限がここです。

        /// <summary>いちばん大きい。Now Playing の曲名。</summary>
        public const int TextDisplay = 40;

        /// <summary>見出し。節の名前、一覧の曲名。</summary>
        public const int TextTitle = 27;

        /// <summary>本文。ボタンの文字。</summary>
        public const int TextBody = 22;

        /// <summary>添え物。時間・件数・説明。<b>ここが下限</b>。</summary>
        public const int TextCaption = 18;

        /// <summary>リモコン(近くで見る)では 1 段小さくする。</summary>
        public static int ForRemote(int size)
        {
            return Mathf.RoundToInt(size * 0.78f);
        }

        // ═════════ 色 ═════════
        //
        // <b>Frost の面はすべて半透明の白</b>です。不透明な板はありません。
        // 濃さ(アルファ)が層の深さを表します。
        //
        //   0.55  いちばん奥(パネルの地)…… ワールドがよく透ける
        //   0.72  カード・行 ……………………… 少し透ける
        //   0.88  持ち上がったもの ………… ほぼ白いが、まだ透ける
        //
        // 白の <b>色みは中立ではありません</b>。ほんのわずかに青を混ぜてあります。
        // 完全な中立灰は、暗いワールドの中では<b>黄ばんで見えます</b>。

        /// <summary>いちばん奥。パネルの地。<b>ワールドが透ける</b>。</summary>
        public static readonly Color Base = new Color(0.965f, 0.969f, 0.980f, 0.55f);

        /// <summary>1 段手前。硝子のカード・行・ボタンの面。</summary>
        public static readonly Color Surface = new Color(0.980f, 0.984f, 0.992f, 0.72f);

        /// <summary>2 段手前。持ち上がったもの(主役のカード・入力欄)。</summary>
        public static readonly Color SurfaceRaised = new Color(1f, 1f, 1f, 0.88f);

        /// <summary>
        /// <b>硝子の縁の光。</b>カードの上端に 2 px だけ引きます。
        /// これが無いと、半透明の板は<b>ただの「薄い色」</b>にしか見えません。
        /// </summary>
        public static readonly Color GlassEdge = new Color(1f, 1f, 1f, 0.85f);

        /// <summary>
        /// <b>硝子の影。</b>カードの下に 1 枚敷きます。
        /// 縁の光(上)と対にして、はじめて「浮いている」ように見えます。
        /// </summary>
        public static readonly Color Shadow = new Color(0.05f, 0.07f, 0.12f, 0.10f);

        /// <summary>触れたとき・選ばれているときの薄い塗り。</summary>
        public static readonly Color SurfaceHover = new Color(0.05f, 0.07f, 0.12f, 0.05f);

        /// <summary>押した瞬間だけ出る影。手応えの代わり。</summary>
        public static readonly Color SurfacePressed = new Color(0.05f, 0.07f, 0.12f, 0.14f);

        /// <summary>区切り線。ほとんど引きませんが、要るときはこの濃さ。</summary>
        public static readonly Color Outline = new Color(0.05f, 0.07f, 0.12f, 0.10f);

        /// <summary>
        /// <b>唯一の強調色。</b>Smart Media Player のシグナルブルー(#2F6BFF)。
        ///
        /// iOS の青(#007AFF)より<b>わずかに紫寄りで濃い</b>色にしてあります。
        /// 半透明の白い硝子の上では、明るい青は<b>沈んで見える</b>ためです。
        /// 出る場所は<b>再生中・選択中・主役のボタン</b>だけ。
        /// </summary>
        public static readonly Color Accent = new Color(0.184f, 0.420f, 1f, 1f);

        /// <summary>強調色の上に乗せる文字。</summary>
        public static readonly Color OnAccent = new Color(1f, 1f, 1f, 1f);

        /// <summary>強調色をうっすら敷くとき(再生中の行など)。</summary>
        public static readonly Color AccentWash = new Color(0.184f, 0.420f, 1f, 0.13f);

        /// <summary>強調色の縁(選ばれているカードの枠)。</summary>
        public static readonly Color AccentEdge = new Color(0.184f, 0.420f, 1f, 0.55f);

        /// <summary>
        /// 主役の文字。曲名など。<b>ほぼ黒</b>。
        /// 半透明の面の上に載るので、画面用より<b>1 段濃く</b>してあります。
        /// </summary>
        public static readonly Color TextPrimary = new Color(0.063f, 0.067f, 0.078f, 1f);

        /// <summary>脇役の文字。アーティスト・時間・件数。</summary>
        public static readonly Color TextSecondary = new Color(0.290f, 0.306f, 0.341f, 1f);

        /// <summary>さらに弱い文字。説明書き・空のときの案内。</summary>
        public static readonly Color TextMuted = new Color(0.447f, 0.467f, 0.510f, 1f);

        // ═════════ 動き ═════════
        //
        // <b>状態が変わったら必ず動かす。</b>ワールドには
        // カーソルの影も指の感触も無いので、動き以外に手応えがありません。
        //
        // 3 段だけ。速いほど「すぐ効いた」、遅いほど「大きく変わった」と読まれます。

        /// <summary>押した手応え。<b>速い</b>ほど効いた感じがする(秒)。</summary>
        public const float MotionFast = 0.08f;

        /// <summary>ふつうの切り替え(タブ・選択)(秒)。</summary>
        public const float MotionBase = 0.16f;

        /// <summary>大きく変わったとき(曲が変わった・画面が入れ替わった)(秒)。</summary>
        public const float MotionSlow = 0.30f;

        /// <summary>押した瞬間に縮む量。<b>1 割も縮めると壊れて見える</b>。</summary>
        public const float PressScale = 0.96f;

        /// <summary>選ばれたときに持ち上がる量。</summary>
        public const float SelectScale = 1.04f;

        // ═════════ 大きさの目安 ═════════

        /// <summary>
        /// 一覧の 1 行(カード)の高さ。
        /// <b>Frost では行はカード</b>です。サムネイルが主役になる高さを取ります。
        /// </summary>
        public const float RowHeight = 108f;

        /// <summary>一覧のサムネイルの 1 辺。16:9 なので横はこの 16/9 倍。</summary>
        public const float RowArtHeight = 80f;

        /// <summary>ボタンの高さ(壁パネル)。</summary>
        public const float ButtonHeight = 60f;

        /// <summary>おすすめカードの大きさ。</summary>
        public const float CardWidth = 260f;
        public const float CardHeight = 232f;
        // ───────── 角丸の絵 ─────────

        /// <summary>
        /// <b>角の丸い板の絵を作る(または作ってあるものを返す)。</b>
        ///
        /// uGUI の <c>Image</c> は四角しか描けないので、
        /// <b>9 分割(9-slice)の絵</b>を用意して引き伸ばします。
        /// 中央 1px を伸ばすだけなので、どんな大きさでも角の丸みは変わりません。
        ///
        /// 作った絵は <c>SmartMediaPlatform_Data/UI</c> に置きます。
        /// <b>更新でシステムを入れ替えても消えない場所</b>です。
        /// </summary>
        public static Sprite RoundedSprite(int radius)
        {
            if (radius <= 0) return null;
            if (radius > 64) radius = 64;

            if (_spriteCache.ContainsKey(radius))
            {
                Sprite cached = _spriteCache[radius];
                if (cached != null) return cached;
            }

            string path = UiFolder + "/Rounded" + radius + ".png";

            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null)
            {
                _spriteCache[radius] = existing;
                return existing;
            }

            Sprite created = CreateRoundedSprite(radius, path);
            _spriteCache[radius] = created;
            return created;
        }

        /// <summary>作った絵の置き場。システムの外なので更新で消えない。</summary>
        public const string UiFolder = "Assets/SmartMediaPlatform_Data/UI";

        private static readonly Dictionary<int, Sprite> _spriteCache = new Dictionary<int, Sprite>();

        /// <summary>覚えているものを捨てる(作り直すときに呼ぶ)。</summary>
        public static void ForgetSprites()
        {
            _spriteCache.Clear();
        }

        private static Sprite CreateRoundedSprite(int radius, string path)
        {
            // 角 2 つぶん + 伸ばすための 1px。これが 9 分割の最小の形。
            int size = radius * 2 + 1;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = new Color32(255, 255, 255, Coverage(x, y, size, radius));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            if (!AssetDatabase.IsValidFolder(UiFolder)) Directory.CreateDirectory(UiFolder);

            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;

                // 9 分割の境目。角 radius ぶんは伸ばさない。
                importer.spriteBorder = new Vector4(radius, radius, radius, radius);
                importer.spritePixelsPerUnit = 100f;

                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// その画素が角丸の内側にどれだけ入っているか(0〜255)。
        ///
        /// <b>縁をなめらかにします。</b>入っているか / いないかの 2 択で塗ると、
        /// 角が階段状にギザギザになります。1 画素を 4×4 に分けて数え、
        /// その割合を濃さにすると、拡大しても縁がきれいなままです。
        /// </summary>
        private static byte Coverage(int x, int y, int size, int radius)
        {
            // 角から離れているところは、調べるまでもなく内側。
            bool nearCornerX = x < radius || x >= size - radius;
            bool nearCornerY = y < radius || y >= size - radius;
            if (!nearCornerX || !nearCornerY) return 255;

            // その角の中心。
            float cx = x < radius ? radius : size - radius - 1;
            float cy = y < radius ? radius : size - radius - 1;

            const int Samples = 4;
            int inside = 0;

            for (int sy = 0; sy < Samples; sy++)
            {
                for (int sx = 0; sx < Samples; sx++)
                {
                    float px = x + (sx + 0.5f) / Samples;
                    float py = y + (sy + 0.5f) / Samples;

                    float dx = px - (cx + 0.5f);
                    float dy = py - (cy + 0.5f);

                    if (dx * dx + dy * dy <= radius * radius) inside++;
                }
            }

            return (byte)(inside * 255 / (Samples * Samples));
        }

        private static Color Gray(float level)
        {
            return new Color(level, level, level, 1f);
        }

        private static Color Gray(float level, float alpha)
        {
            return new Color(level, level, level, alpha);
        }
    }
}
#endif
