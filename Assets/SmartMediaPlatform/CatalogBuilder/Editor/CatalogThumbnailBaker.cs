#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Assets;
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.CatalogBuilder.EditorTools
{
    /// <summary>
    /// <b>曲の絵をワールドへ焼き込む。</b>Phase7。
    ///
    /// <b>なぜ 1 枚の大きな絵にまとめるのか</b><br/>
    /// 1 曲 1 枚のテクスチャにすると、
    /// <list type="bullet">
    /// <item>行を描くたびにテクスチャの切り替えが起きる(VR では効きます)</item>
    /// <item>16:9 は 2 のべき乗でないので<b>圧縮が効かない</b></item>
    /// <item>アセットが 200 個増えて Project が読みにくくなる</item>
    /// </list>
    /// まとめて 1 枚の <b>2 のべき乗</b>の板に並べると、圧縮が効いて描画も 1 回で済みます。
    ///
    /// <b>大きさは曲数に合わせて詰めます。</b>200 曲を 128 px で並べるなら
    /// 2048 × 1024 で足り、空きだらけの 2048 × 2048 を持たずに済みます。
    ///
    /// <b>絵はすでに手元にあります。</b>Phase6-5 でサムネイルを
    /// <c>Library/</c> に貯めてあるので、<b>焼き込みで通信は起きません</b>。
    /// </summary>
    public static class CatalogThumbnailBaker
    {
        /// <summary>板 1 枚の横幅。2 のべき乗であること。</summary>
        public const int AtlasWidth = 2048;

        /// <summary>板 1 枚の縦の上限。</summary>
        public const int AtlasMaxHeight = 2048;

        /// <summary>
        /// 焼いた絵の置き場所。<b>SmartMediaPlatform の外</b>です(Phase7-3 で移動)。
        /// 中に置くと、更新(フォルダ入れ替え)のたびに焼いた絵が消えるためです。
        /// </summary>
        public const string OutputFolder = "Assets/SmartMediaPlatform_Data/Thumbnails";

        // ───────── 大きさの選択肢 ─────────

        /// <summary>軽い。曲数が多いとき / Quest 向け。</summary>
        public const int SizeSmall = 128;

        /// <summary>標準。PC ワールドの既定。</summary>
        public const int SizeMedium = 256;

        /// <summary>きれい。曲数が少ないとき。</summary>
        public const int SizeLarge = 384;

        public static readonly int[] Sizes = { SizeSmall, SizeMedium, SizeLarge };

        public static readonly string[] SizeLabels =
        {
            "軽い (128 px)", "標準 (256 px)", "きれい (384 px)",
        };

        /// <summary>
        /// <b>その大きさで何 MB になるか。</b>
        ///
        /// DXT1(4 bpp の半分 = 0.5 バイト / px)で、ミップマップは作らない前提です。
        /// <b>ミップマップを作らないのは、表示する大きさと絵の大きさがほぼ同じ</b>だからで、
        /// 付けると 1.33 倍になるだけで見た目は変わりません。
        /// </summary>
        public static float EstimateMegabytes(int count, int tileWidth)
        {
            if (count <= 0 || tileWidth <= 0) return 0f;

            int tileHeight = HeightFor(tileWidth);
            int columns = AtlasWidth / tileWidth;
            if (columns <= 0) return 0f;

            int rows = (count + columns - 1) / columns;
            int rowsPerAtlas = AtlasMaxHeight / tileHeight;

            long pixels = 0;
            int remaining = rows;

            while (remaining > 0)
            {
                int take = remaining < rowsPerAtlas ? remaining : rowsPerAtlas;
                pixels += (long)AtlasWidth * NextPowerOfTwo(take * tileHeight);
                remaining -= take;
            }

            return pixels * 0.5f / (1024f * 1024f);
        }

        /// <summary>16:9 にそろえた高さ。</summary>
        public static int HeightFor(int tileWidth)
        {
            return Mathf.RoundToInt(tileWidth * 9f / 16f);
        }

        // ───────── 焼く ─────────

        public sealed class BakeReport
        {
            public bool Ok;
            public int Baked;
            public int Missing;
            public int Atlases;
            public float Megabytes;
            public string Message = "";
        }

        /// <summary>
        /// <paramref name="items"/> の絵を焼いて、<c>Sprite</c> の並びを返す。
        /// 並びは <paramref name="items"/> と同じで、<b>絵が無いところは null</b> です。
        /// </summary>
        public static Sprite[] Bake(
            MediaCatalogAsset asset, IReadOnlyList<CatalogDraftItem> items,
            int tileWidth, out BakeReport report)
        {
            report = new BakeReport();

            if (items == null || items.Count == 0)
            {
                report.Message = "曲がありません。";
                return new Sprite[0];
            }

            string folder = FolderFor(asset);
            Directory.CreateDirectory(folder);

            int tileHeight = HeightFor(tileWidth);
            int columns = AtlasWidth / tileWidth;
            int rowsPerAtlas = AtlasMaxHeight / tileHeight;
            int perAtlas = columns * rowsPerAtlas;

            var result = new Sprite[items.Count];
            int atlasCount = (items.Count + perAtlas - 1) / perAtlas;
            int baked = 0;

            for (int atlas = 0; atlas < atlasCount; atlas++)
            {
                int first = atlas * perAtlas;
                int count = items.Count - first;
                if (count > perAtlas) count = perAtlas;

                baked += BakeOneAtlas(
                    folder, atlas, items, first, count, tileWidth, tileHeight, columns, result);
            }

            AssetDatabase.Refresh();

            report.Ok = baked > 0;
            report.Baked = baked;
            report.Missing = items.Count - baked;
            report.Atlases = atlasCount;
            report.Megabytes = EstimateMegabytes(items.Count, tileWidth);

            report.Message = baked + " 曲の絵を焼きました("
                             + tileWidth + " px / 板 " + atlasCount + " 枚 / 約 "
                             + report.Megabytes.ToString("0.0") + " MB)。";

            if (report.Missing > 0)
            {
                report.Message += "\n" + report.Missing
                                  + " 曲は絵が取れていません(一覧を一度開くと集まります)。";
            }

            return result;
        }

        // ───────── 内部 ─────────

        /// <summary>板 1 枚ぶんを作る。焼けた枚数を返す。</summary>
        private static int BakeOneAtlas(
            string folder, int atlasIndex, IReadOnlyList<CatalogDraftItem> items,
            int first, int count, int tileWidth, int tileHeight, int columns, Sprite[] result)
        {
            int rows = (count + columns - 1) / columns;
            int height = NextPowerOfTwo(rows * tileHeight);

            var atlas = new Texture2D(AtlasWidth, height, TextureFormat.RGBA32, false);

            // 透明で塗りつぶす。空いた枠に前の絵の残りが出ないように。
            var blank = new Color32[AtlasWidth * height];
            atlas.SetPixels32(blank);

            var placed = new List<SpriteMetaData>();
            int baked = 0;

            for (int i = 0; i < count; i++)
            {
                CatalogDraftItem item = items[first + i];
                if (item == null) continue;

                Texture2D source = CatalogThumbnailCache.Get(item.ThumbnailPath);
                if (source == null) continue;

                int column = i % columns;
                int row = i / columns;

                // Texture2D は左下が原点。上の行から詰めたいので縦を反転する。
                int x = column * tileWidth;
                int y = height - (row + 1) * tileHeight;

                CopyScaled(source, atlas, x, y, tileWidth, tileHeight);

                var meta = new SpriteMetaData();
                meta.name = SafeName(item.Id);
                meta.rect = new Rect(x, y, tileWidth, tileHeight);
                meta.alignment = (int)SpriteAlignment.Center;
                meta.pivot = new Vector2(0.5f, 0.5f);

                placed.Add(meta);
                baked++;
            }

            atlas.Apply();

            string path = folder + "/atlas_" + atlasIndex + ".png";
            File.WriteAllBytes(path, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ConfigureImporter(path, placed.ToArray());

            // 設定を入れてから読み直す。先に読むと 1 枚の絵として返ってくる。
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            AssignSprites(path, items, first, count, result);

            return baked;
        }

        /// <summary>
        /// 板の読み込み方を決める。
        /// <b>ミップマップは作りません。</b>表示する大きさと絵の大きさがほぼ同じなので、
        /// 付けても見た目は変わらず容量だけ 1.33 倍になります。
        /// </summary>
        private static void ConfigureImporter(string path, SpriteMetaData[] sheet)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritesheet = sheet;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = AtlasMaxHeight;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.alphaIsTransparency = true;

            importer.SaveAndReimport();
        }

        /// <summary>板から切り出された絵を、曲の並びへ割り当てる。</summary>
        private static void AssignSprites(
            string path, IReadOnlyList<CatalogDraftItem> items,
            int first, int count, Sprite[] result)
        {
            Object[] loaded = AssetDatabase.LoadAllAssetsAtPath(path);
            if (loaded == null) return;

            for (int i = 0; i < count; i++)
            {
                CatalogDraftItem item = items[first + i];
                if (item == null) continue;

                string wanted = SafeName(item.Id);

                for (int a = 0; a < loaded.Length; a++)
                {
                    var sprite = loaded[a] as Sprite;
                    if (sprite == null || sprite.name != wanted) continue;

                    result[first + i] = sprite;
                    break;
                }
            }
        }

        /// <summary>
        /// 元の絵を切り取って縮めて写す。
        ///
        /// <b>まん中を 16:9 で切り取ります。</b>YouTube のサムネイルは 16:9 ですが、
        /// 取り込み元によっては 4:3 や正方形のこともあり、
        /// そのまま縮めると<b>縦につぶれた顔</b>になります。
        /// </summary>
        private static void CopyScaled(
            Texture2D source, Texture2D target, int x, int y, int width, int height)
        {
            float wanted = (float)width / height;
            float actual = (float)source.width / source.height;

            int cropW = source.width;
            int cropH = source.height;

            if (actual > wanted) cropW = Mathf.RoundToInt(source.height * wanted);
            else cropH = Mathf.RoundToInt(source.width / wanted);

            int offsetX = (source.width - cropW) / 2;
            int offsetY = (source.height - cropH) / 2;

            var pixels = new Color32[width * height];

            for (int row = 0; row < height; row++)
            {
                // 縦は下から数える(Texture2D の原点が左下のため)。
                float v = (row + 0.5f) / height;
                int sourceY = offsetY + Mathf.Clamp((int)(v * cropH), 0, cropH - 1);

                for (int column = 0; column < width; column++)
                {
                    float u = (column + 0.5f) / width;
                    int sourceX = offsetX + Mathf.Clamp((int)(u * cropW), 0, cropW - 1);

                    pixels[row * width + column] = source.GetPixel(sourceX, sourceY);
                }
            }

            target.SetPixels32(x, y, width, height, pixels);
        }

        private static string FolderFor(MediaCatalogAsset asset)
        {
            string name = asset != null ? asset.name : "Catalog";
            return OutputFolder + "/" + SafeName(name);
        }

        /// <summary>ファイル名にできない文字を落とす。</summary>
        private static string SafeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unnamed";

            var sb = new System.Text.StringBuilder();
            string trimmed = value.Trim();

            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];

                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                          || (c >= '0' && c <= '9') || c == '-' || c == '_';

                sb.Append(ok ? c : '_');
            }
            return sb.ToString();
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value) result *= 2;

            return result;
        }
    }
}
#endif
