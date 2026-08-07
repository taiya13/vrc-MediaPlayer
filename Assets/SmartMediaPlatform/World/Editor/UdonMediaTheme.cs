#if UNITY_EDITOR && VRC_SDK_VRCSDK3
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.World.EditorTools
{
    /// <summary>
    /// <b>見た目の決まりごとを 1 か所に集めたもの。</b>Phase7-3。
    ///
    /// ───────────────────────────────────────────────
    /// <b>Apple Music / Spotify / YouTube Music は、なぜあの見た目なのか</b>
    ///
    /// 3 つとも見た目はかなり違いますが、<b>効いている決まりごとは同じ</b>です。
    /// 真似したいのは色そのものではなく、この考え方のほうです。
    ///
    /// <list type="number">
    /// <item><b>背景は「ほぼ無彩色」。彩度があるのはジャケットだけ。</b><br/>
    ///       音楽アプリの画面でいちばん情報量が多いのは<b>絵</b>です。
    ///       周りに色を置くと絵と競合して、どれを見ればよいか分からなくなります。
    ///       だから UI 側は徹底して灰色にして、<b>絵だけが色を持つ</b>状態にします。
    ///       真っ黒にしないのは、<b>黒い絵の縁が背景に溶ける</b>のと、
    ///       影や段差が表現できなくなるためです</item>
    ///
    /// <item><b>強調色は 1 色だけ。しかも「いま」を指すためだけに使う。</b><br/>
    ///       Spotify の緑も Apple Music の赤も、<b>再生中・選択中にしか出ません</b>。
    ///       色数を増やすと「色が付いている = 大事」という手掛かりが壊れます。
    ///       1 色に絞ると、画面のどこに色があるかを見るだけで
    ///       <b>「いまどこにいるか」が分かる</b>ようになります</item>
    ///
    /// <item><b>文字の大きさは 3〜4 段だけ。差は太さと明るさで付ける。</b><br/>
    ///       サイズを細かく刻むと、量が増えたときに<b>階層が読めなくなります</b>。
    ///       曲名は明るく、それ以外(アーティスト・時間・件数)は<b>暗い灰色</b>。
    ///       同じ大きさでも、明るさが違うだけで主従がはっきりします</item>
    ///
    /// <item><b>行に枠線を引かない。余白と、触れたときの薄い塗りだけで区切る。</b><br/>
    ///       枠線は 1 本ごとに視線を止めます。100 行あれば 100 回止まります。
    ///       余白で区切ると、目は<b>止まらずに縦へ流れます</b></item>
    ///
    /// <item><b>Now Playing では絵がいちばん大きい。</b><br/>
    ///       操作ボタンより絵が大きいのは、
    ///       <b>「何が鳴っているか」を一目で答える</b>のが最優先だからです</item>
    ///
    /// <item><b>余白は 1 つの単位の倍数だけを使う。</b><br/>
    ///       目は「揃っていない」ことに敏感です。7px と 8px の混在は
    ///       理由が分からないまま雑に見えます。倍数だけにすると、
    ///       何も考えずに置いても揃います</item>
    /// </list>
    ///
    /// ───────────────────────────────────────────────
    /// <b>VRChat なので変えているところ</b>
    ///
    /// 上の考え方はそのままに、次の 3 点だけ画面用の常識から外しています。
    /// <list type="bullet">
    /// <item><b>文字を大きく。</b>ワールドでは 1〜3 m 離れて見ます。
    ///       スマホの距離を前提にした級数だと読めません</item>
    /// <item><b>当たり判定を大きく。</b>レーザーは手ぶれで揺れます。
    ///       見た目より<b>触れる範囲を広く</b>取ります</item>
    /// <item><b>明暗差を強く。</b>暗いワールドでは中間調がつぶれます。
    ///       灰色どうしの差は、画面で見て「やりすぎ」くらいがちょうどよい</item>
    /// </list>
    /// </summary>
    public static class UdonMediaTheme
    {
        // ───────── 余白(8 の倍数だけ)─────────

        /// <summary>基準となる 1 単位。ここに無い数を直接書かないこと。</summary>
        public const float Unit = 8f;

        public const float Space1 = Unit;          //  8 …… 文字と文字のあいだ
        public const float Space2 = Unit * 2f;     // 16 …… 部品どうしのあいだ
        public const float Space3 = Unit * 3f;     // 24 …… 節と節のあいだ
        public const float Space4 = Unit * 4f;     // 32 …… 画面のふち
        public const float Space6 = Unit * 6f;     // 48 …… 大きな区切り

        // ───────── 角丸 ─────────

        /// <summary>小さいもの(札・タグ)。</summary>
        public const int RadiusSmall = 6;

        /// <summary>ふつうのもの(ボタン・行)。</summary>
        public const int RadiusMedium = 10;

        /// <summary>大きいもの(カード・絵・パネル)。</summary>
        public const int RadiusLarge = 16;

        /// <summary>丸(円形ボタン)。使う側が高さの半分を渡す。</summary>
        public const int RadiusPill = 999;

        // ───────── 文字の大きさ ─────────
        //
        // 4 段だけ。これ以外を使わない。
        // 壁パネル(遠い)とリモコン(近い)で 1 段ぶんずらして使います。

        /// <summary>いちばん大きい。Now Playing の曲名。</summary>
        public const int TextDisplay = 34;

        /// <summary>見出し。節の名前、一覧の曲名。</summary>
        public const int TextTitle = 24;

        /// <summary>本文。ボタンの文字。</summary>
        public const int TextBody = 20;

        /// <summary>添え物。時間・件数・説明。</summary>
        public const int TextCaption = 16;

        /// <summary>リモコン(近くで見る)では 1 段小さくする。</summary>
        public static int ForRemote(int size)
        {
            return Mathf.RoundToInt(size * 0.78f);
        }

        // ───────── 色(Phase7-6 で明るい配色へ)─────────
        //
        // <b>Apple Music / iOS の考え方</b>を借りています。
        // <list type="bullet">
        // <item>地はオフホワイト、カードは白。段差は<b>影だけ</b>で作る</item>
        // <item>彩度を持つのは <see cref="Accent"/> ひとつだけ</item>
        // <item>色は「区別」ではなく<b>「大事な操作」と「いま鳴っている」だけ</b>に使う</item>
        // </list>
        //
        // <b>真っ白(#FFFFFF)を地にしません。</b>HMD の中では白い面が
        // そのまま光源になり、長く見ていられません。地は #F5F5F7 相当まで
        // 落として、白はカードにだけ使います。

        /// <summary>いちばん奥。パネルの地。#F5F5F7 相当のオフホワイト。</summary>
        public static readonly Color Base = new Color(0.961f, 0.961f, 0.969f, 0.98f);

        /// <summary>
        /// 1 段手前。<b>ボタンの面・入力欄の地。</b>
        /// 地(<see cref="Base"/>)より<b>わずかに濃く</b>します —— 明るい配色では、
        /// 白の上に白を置いても段差が見えないためです。
        /// </summary>
        public static readonly Color Surface = new Color(0.925f, 0.925f, 0.941f, 1f);

        /// <summary>2 段手前。カード・行・入力欄の地。ここだけ白。</summary>
        public static readonly Color SurfaceRaised = new Color(1f, 1f, 1f, 1f);

        /// <summary>触れたとき・選ばれているときの薄い塗り。</summary>
        public static readonly Color SurfaceHover = new Color(0f, 0f, 0f, 0.05f);

        /// <summary>押した瞬間だけ出る影。手応えの代わり。</summary>
        public static readonly Color SurfacePressed = new Color(0f, 0f, 0f, 0.12f);

        /// <summary>区切り線。iOS の separator と同じくらい薄く。</summary>
        public static readonly Color Outline = new Color(0f, 0f, 0f, 0.09f);

        /// <summary>
        /// <b>カードの影。</b>明るい配色では、段差を<b>濃さの違いでは作れません</b>
        /// (白の上の白は見えない)。カードの下に 1 枚だけ敷いて、
        /// 浮いていることを影で伝えます。
        /// </summary>
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.07f);

        /// <summary>
        /// <b>唯一の強調色。</b>「いま鳴っている」「いま選んでいる」にだけ使う。
        ///
        /// iOS の system blue(#007AFF)です。ジャンルや状態を
        /// 色で描き分けることはしません —— <b>色が付いている = いま</b>、
        /// という手掛かりを 1 つに保つためです。
        /// </summary>
        public static readonly Color Accent = new Color(0f, 0.478f, 1f, 1f);

        /// <summary>強調色の上に乗せる文字。</summary>
        public static readonly Color OnAccent = new Color(1f, 1f, 1f, 1f);

        /// <summary>強調色をうっすら敷くとき(再生中の行など)。</summary>
        public static readonly Color AccentWash = new Color(0f, 0.478f, 1f, 0.10f);

        /// <summary>主役の文字。曲名など。#1D1D1F 相当。</summary>
        public static readonly Color TextPrimary = new Color(0.114f, 0.114f, 0.122f, 1f);

        /// <summary>脇役の文字。アーティスト・時間・件数。#6E6E73 相当。</summary>
        public static readonly Color TextSecondary = new Color(0.431f, 0.431f, 0.451f, 1f);

        /// <summary>さらに弱い文字。説明書き・空のときの案内。#8E8E93 相当。</summary>
        public static readonly Color TextMuted = new Color(0.557f, 0.557f, 0.576f, 1f);

        // ───────── 大きさの目安 ─────────

        /// <summary>一覧の 1 行の高さ。絵が大きく見えるように高めに取る。</summary>
        public const float RowHeight = 88f;

        /// <summary>一覧のサムネイルの 1 辺。16:9 なので横はこの 16/9 倍。</summary>
        public const float RowArtHeight = 64f;

        /// <summary>ボタンの高さ(壁パネル)。</summary>
        public const float ButtonHeight = 56f;

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
