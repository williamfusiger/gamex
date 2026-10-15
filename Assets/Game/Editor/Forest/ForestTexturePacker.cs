using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Turns Poly Haven's maps into the three textures HDRP wants and Poly Haven does not ship
    /// (spec 5.4): the Lit mask map, the TerrainLit mask map, and a foliage base colour that carries
    /// the leaf mask in its alpha channel. It reads the downloaded files straight from disk, shuffles
    /// bytes, and writes PNGs into ForestKit/Textures; Unity imports those afterwards.
    /// One texture set is in memory at a time: a 4k image is 67 MB per array.
    /// </summary>
    public static class ForestTexturePacker
    {
        // Part of every stamp: raise it when the packing changes, so everything is packed again.
        const string ToolVersion = "packer-1";

        const int MaxMaskSize = 2048;             // masks of 4k sets are halved to this

        // Materials whose "arm" map has an AO channel that was baked for another UV layout than the one
        // the downloaded FBX carries. Measured on the files (build notes, M2): under 7 to 32 % of the
        // triangles of each rock of rock_moss_set_01 the AO is zero, and a third of the AO islands lie
        // where no triangle has UVs, while roughness, base colour and normal fit the mesh. Zero AO takes
        // away all sky light, so those rocks rendered with black patches. The mask of such a material
        // carries full AO (255): screen-space AO still shades it. The kit build measures every mask
        // against the UVs of the mesh that uses it (ForestPrefabs.RequireAoUnderUvs), so a material
        // that belongs here and is missing stops that build.
        static readonly string[] UnusableAo = { "rock_moss_set_01" };

        // Materials whose AO fits their mesh where it was baked, but whose mesh also reaches texels where nothing was
        // baked (AO 0). Measured at M4 (build/issues/M4-tall-firs.md): fir_tree_01_a maps its upper trunk (8 to 18 m, inside
        // the crown) to a thin UV strip along the left edge of the trunk_a atlas that crosses two islands and the empty
        // gaps between them, so 17.7 % of the trunk's surface has AO 0, all but a few percent of it above 8 m; the lower
        // trunk the player sees sits on the islands. Those empty texels take the mean AO of the baked ones; every baked
        // texel keeps its value. AoGapFloor is the kit gate's own floor (ForestPrefabs.RequireAoUnderUvs).
        static readonly string[] UnbakedAoFilled = { "fir_tree_01_trunk_a" };
        const byte AoGapFloor = 8;
        const double MaxMeanMetallic = 0.1;       // above this, the blue channel of an "arm" map is not a metal map
        const double MinFoliageCoverage = 0.03;
        const double MaxFoliageCoverage = 0.97;

        static readonly Dictionary<string, (DateTime written, Color colour)> meanColours = new();
        static float[] srgbToLinear;

        /// <summary>
        /// Packs every texture of every downloaded asset. A product whose sources and packer version are
        /// unchanged is skipped unless <paramref name="force"/> is set. Returns the number of files written.
        /// The caller refreshes the asset database afterwards.
        /// </summary>
        public static int PackAll(bool force)
        {
            int written = 0, upToDate = 0, assets = 0;
            Directory.CreateDirectory(ForestManifest.ProjectDiskPath(ForestPaths.KitTextures));

            foreach (ForestAssetEntry entry in ForestManifest.All)
            {
                if (entry.kind != "texture" && entry.kind != "model") continue;
                if (!ForestManifest.IsDownloaded(entry)) continue;
                assets++;

                if (entry.kind == "texture")
                {
                    Count(PackTerrainMask(entry, force), ref written, ref upToDate);
                    continue;
                }
                foreach (ForestMaterialEntry material in entry.materials)
                {
                    if (material.sameAs != "") continue;   // uses another material's textures
                    Count(PackMask(entry, material, force), ref written, ref upToDate);
                    if (material.surface == "foliage")
                        Count(PackFoliageBaseColor(entry, material, force), ref written, ref upToDate);
                }
            }

            Debug.Log($"Forest packer: {written} textures written, {upToDate} already up to date, from {assets} downloaded assets.");
            return written;
        }

        /// <summary>Asset path of a material's HDRP mask map (metallic, AO, detail mask, smoothness).</summary>
        public static string MaskPath(string materialName) => $"{ForestPaths.KitTextures}/{materialName}__mask.png";

        /// <summary>Asset path of a foliage material's base colour with the leaf mask in alpha.</summary>
        public static string FoliageBaseColorPath(string materialName) => $"{ForestPaths.KitTextures}/{materialName}__basecolor.png";

        /// <summary>Asset path of a terrain layer's mask map (metallic, AO, height, smoothness).</summary>
        public static string TerrainMaskPath(string layerId) => $"{ForestPaths.KitTextures}/{layerId}__tmask.png";

        /// <summary>
        /// Mean colour of a foliage atlas, weighted by alpha, in linear space. Read from the packed
        /// atlas, so it is available in any session after the import stage has run; throws before that.
        /// </summary>
        public static Color MeanFoliageColor(string materialName)
        {
            ForestMaterialEntry material = ForestManifest.ResolveMaterial(materialName);
            string name = material != null ? material.name : materialName;
            string diskPath = ForestManifest.ProjectDiskPath(FoliageBaseColorPath(name));
            if (!File.Exists(diskPath))
                throw new FileNotFoundException(
                    $"No packed foliage atlas for material '{materialName}'. Run Gamex/Forest/1 Import Assets first.", diskPath);

            DateTime written = File.GetLastWriteTimeUtc(diskPath);
            if (meanColours.TryGetValue(name, out (DateTime written, Color colour) cached) && cached.written == written)
                return cached.colour;

            Color32[] pixels = ReadPixels(diskPath, out int _, out int _);
            Color colour = AlphaWeightedLinearMean(pixels, name);
            meanColours[name] = (written, colour);
            return colour;
        }

        /// <summary>
        /// Decodes a JPG or PNG from disk at 8 bits per channel, without gamma correction and without
        /// going through the importer. Rows run bottom to top, as in every Unity pixel array.
        /// </summary>
        public static Color32[] ReadPixels(string diskPath, out int width, out int height)
        {
            byte[] bytes = File.ReadAllBytes(diskPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!ImageConversion.LoadImage(texture, bytes, false))
                    throw new InvalidDataException($"Cannot decode the image {diskPath}");
                width = texture.width;
                height = texture.height;
                return texture.GetPixels32();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>Writes an RGBA PNG. The alpha channel is kept as data: nothing is premultiplied.</summary>
        public static void WritePng(string diskPath, Color32[] pixels, int width, int height)
        {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (width <= 0 || height <= 0 || pixels.Length != width * height)
                throw new ArgumentException($"{pixels.Length} pixels do not fill {width} x {height} for {diskPath}");

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                texture.SetPixels32(pixels);   // EncodeToPNG reads this CPU copy; no upload to the GPU is needed
                byte[] png = texture.EncodeToPNG();
                if (png == null || png.Length == 0)
                    throw new IOException($"Cannot encode {diskPath} as PNG");

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(diskPath)));
                // Written over the existing file, never deleted and recreated: the file's .meta, and with
                // it the GUID every material refers to, must survive whatever interrupts this.
                File.WriteAllBytes(diskPath, png);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        // ---- the three products ----

        enum Outcome { Written, UpToDate }

        static void Count(Outcome outcome, ref int written, ref int upToDate)
        {
            if (outcome == Outcome.Written) written++;
            else upToDate++;
        }

        static Outcome PackMask(ForestAssetEntry entry, ForestMaterialEntry material, bool force)
        {
            string output = MaskPath(material.name);
            ForestAssetFile arm = RequireFile(entry, material.maps.arm, material.name, "arm");
            // The extra part is added only for the listed materials, so every other mask keeps its stamp.
            bool discardAo = Array.IndexOf(UnusableAo, material.name) >= 0;
            bool fillAo = Array.IndexOf(UnbakedAoFilled, material.name) >= 0;
            string stamp = discardAo
                ? ForestStamps.Hash(ToolVersion, "mask", arm.md5, MaxMaskSize.ToString(), "ao-discarded")
                : ForestStamps.Hash(ToolVersion, "mask", arm.md5, MaxMaskSize.ToString());
            if (fillAo) stamp = ForestStamps.Hash(stamp, "ao-gaps-filled", AoGapFloor.ToString());
            ForestKitSpec.TwigSprite[] sprites = ForestKitSpec.AoMatchedSprites(material.name);
            bool matchSprites = sprites != null && ForestKitSpec.TwigNeedleBox(material.name, out ForestKitSpec.TwigSprite needleBox);
            if (matchSprites)
            {
                var boxes = new System.Text.StringBuilder("sprite-ao");
                foreach (ForestKitSpec.TwigSprite box in sprites) boxes.Append('|').Append(box.uvMin.ToString("R")).Append(box.uvMax.ToString("R"));
                ForestKitSpec.TwigNeedleBox(material.name, out needleBox);
                boxes.Append("|needles").Append(needleBox.uvMin.ToString("R")).Append(needleBox.uvMax.ToString("R"));
                stamp = ForestStamps.Hash(stamp, boxes.ToString());
            }
            if (IsUpToDate(output, stamp, force)) return Outcome.UpToDate;

            Color32[] pixels = ReadPixels(ForestManifest.DiskPath(arm.relPath), out int width, out int height);
            RequireSameSize(entry, material.surface == "foliage" ? material.maps.diffuse : material.maps.basecolor, width, height, arm.relPath);
            RequireSameSize(entry, material.maps.normal, width, height, arm.relPath);
            RequireNotMetal(pixels, arm.relPath);

            ToLitMask(pixels);
            if (matchSprites)
                MatchSpriteAo(pixels, width, height, entry, material, sprites);
            if (fillAo)
                FillUnbakedAo(pixels, arm.relPath, material.name);
            if (discardAo)
            {
                for (int i = 0; i < pixels.Length; i++) pixels[i].g = 255;
                Debug.Log($"Forest packer: the AO channel of '{arm.relPath}' is not used (it does not fit the UVs of the mesh); the mask of {material.name} carries full AO.");
            }
            ShrinkToMaskSize(ref pixels, ref width, ref height);
            return WriteProduct(output, stamp, pixels, width, height);
        }

        /// <summary>
        /// Twig cards (review 1 of the M3 captures) are drawn with the photographed twig sprays of an atlas whose mesh uses
        /// only its single needles. The sprays' AO is much higher than the needles' (0.74 to 0.79 against 0.43 on the young
        /// firs), so the card levels read lighter than the needle level they follow. Inside the sprays' boxes the AO is
        /// scaled to the needles' alpha-weighted mean. The needles, the stems and everything the scan maps stay as they are.
        /// </summary>
        static void MatchSpriteAo(Color32[] pixels, int width, int height, ForestAssetEntry entry, ForestMaterialEntry material,
            ForestKitSpec.TwigSprite[] sprites)
        {
            ForestAssetFile alphaFile = RequireFile(entry, material.maps.alpha, material.name, "alpha");
            Color32[] alpha = ReadPixels(ForestManifest.DiskPath(alphaFile.relPath), out int alphaWidth, out int alphaHeight);
            RequireEqual(width, height, alphaWidth, alphaHeight, material.maps.arm, alphaFile.relPath);
            ForestKitSpec.TwigNeedleBox(material.name, out ForestKitSpec.TwigSprite needles);
            byte cut = (byte)Mathf.RoundToInt(material.alphaCutoff * 255f);

            bool Inside(ForestKitSpec.TwigSprite box, int x, int y)
            {
                float u = (x + 0.5f) / width, v = (y + 0.5f) / height;   // rows run bottom to top, as v does
                return u >= box.uvMin.x && u <= box.uvMax.x && v >= box.uvMin.y && v <= box.uvMax.y;
            }
            double needleSum = 0, spriteSum = 0;
            long needleCount = 0, spriteCount = 0;
            var inSprite = new bool[pixels.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    foreach (ForestKitSpec.TwigSprite sprite in sprites)
                        if (Inside(sprite, x, y)) { inSprite[i] = true; break; }
                    if (alpha[i].r < cut) continue;
                    if (inSprite[i]) { spriteSum += pixels[i].g; spriteCount++; }
                    else if (Inside(needles, x, y)) { needleSum += pixels[i].g; needleCount++; }
                }
            }
            if (needleCount == 0 || spriteCount == 0)
                throw new InvalidDataException($"The twig sprites or the needle box of {material.name} cover no leaf texel of its alpha map; check ForestKitSpec.");
            double factor = Math.Min(1.0, (needleSum / needleCount) / (spriteSum / spriteCount));
            for (int i = 0; i < pixels.Length; i++)
                if (inSprite[i]) pixels[i].g = (byte)Math.Round(pixels[i].g * factor);
            Debug.Log($"Forest packer: the twig sprites of {material.name} have their AO scaled by {factor:0.00} " +
                $"(needles {needleSum / needleCount / 255.0:0.00}, sprites {spriteSum / spriteCount / 255.0:0.00}).");
        }

        /// <summary>
        /// The AO (green channel of the lit mask) of every texel where none was baked takes the mean of the texels where
        /// it was. See UnbakedAoFilled.
        /// </summary>
        static void FillUnbakedAo(Color32[] pixels, string armPath, string materialName)
        {
            double sum = 0;
            long baked = 0;
            foreach (Color32 pixel in pixels)
                if (pixel.g >= AoGapFloor) { sum += pixel.g; baked++; }
            if (baked == 0) throw new InvalidDataException($"The AO channel of {armPath} is empty; nothing to fill its gaps with.");
            var mean = (byte)Math.Round(sum / baked);
            long filled = 0;
            for (int i = 0; i < pixels.Length; i++)
                if (pixels[i].g < AoGapFloor) { pixels[i].g = mean; filled++; }
            Debug.Log($"Forest packer: {filled * 100.0 / pixels.Length:0.0} % of the texels of '{armPath}' had no baked AO; the mask of "
                + $"{materialName} carries the mean of the baked ones there ({mean / 255.0:0.00}).");
        }

        static Outcome PackTerrainMask(ForestAssetEntry entry, bool force)
        {
            string output = TerrainMaskPath(entry.id);
            ForestAssetFile arm = ForestManifest.FileWithMap(entry, "arm");
            ForestAssetFile heightMap = ForestManifest.FileWithMap(entry, "height");
            string stamp = ForestStamps.Hash(ToolVersion, "tmask", arm.md5, heightMap.md5, MaxMaskSize.ToString());
            if (IsUpToDate(output, stamp, force)) return Outcome.UpToDate;

            Color32[] pixels = ReadPixels(ForestManifest.DiskPath(arm.relPath), out int width, out int height);
            Color32[] heights = ReadPixels(ForestManifest.DiskPath(heightMap.relPath), out int heightWidth, out int heightHeight);
            RequireEqual(width, height, heightWidth, heightHeight, arm.relPath, heightMap.relPath);
            RequireSameSize(entry, ForestManifest.FileWithMap(entry, "basecolor").relPath, width, height, arm.relPath);
            RequireSameSize(entry, ForestManifest.FileWithMap(entry, "normal").relPath, width, height, arm.relPath);
            RequireNotMetal(pixels, arm.relPath);

            ToTerrainMask(pixels, heights);
            ShrinkToMaskSize(ref pixels, ref width, ref height);
            return WriteProduct(output, stamp, pixels, width, height);
        }

        static Outcome PackFoliageBaseColor(ForestAssetEntry entry, ForestMaterialEntry material, bool force)
        {
            string output = FoliageBaseColorPath(material.name);
            ForestAssetFile diffuse = RequireFile(entry, material.maps.diffuse, material.name, "diffuse");
            ForestAssetFile alpha = RequireFile(entry, material.maps.alpha, material.name, "alpha");
            string stamp = ForestStamps.Hash(ToolVersion, "foliage", diffuse.md5, alpha.md5,
                material.alphaCutoff.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (IsUpToDate(output, stamp, force)) return Outcome.UpToDate;

            Color32[] pixels = ReadPixels(ForestManifest.DiskPath(diffuse.relPath), out int width, out int height);
            Color32[] mask = ReadPixels(ForestManifest.DiskPath(alpha.relPath), out int maskWidth, out int maskHeight);
            RequireEqual(width, height, maskWidth, maskHeight, diffuse.relPath, alpha.relPath);

            // Always the separate alpha file, even where a diffuse PNG claims to carry alpha.
            SetAlphaFromMask(pixels, mask);
            double coverage = Coverage(pixels, material.alphaCutoff);
            if (coverage < MinFoliageCoverage || coverage > MaxFoliageCoverage)
                throw new InvalidDataException(
                    $"Foliage material {material.name}: {coverage:P1} of the atlas passes the alpha cutoff {material.alphaCutoff}, " +
                    $"outside {MinFoliageCoverage:P0} to {MaxFoliageCoverage:P0}. '{alpha.relPath}' is not a leaf mask for '{diffuse.relPath}'.");

            return WriteProduct(output, stamp, pixels, width, height);
        }

        static Outcome WriteProduct(string outputAssetPath, string stamp, Color32[] pixels, int width, int height)
        {
            // The old stamp goes first: if the write is cut short, the next run must not skip this file.
            ForestStamps.Forget(outputAssetPath);
            WritePng(ForestManifest.ProjectDiskPath(outputAssetPath), pixels, width, height);
            ForestStamps.Set(outputAssetPath, stamp);
            return Outcome.Written;
        }

        static bool IsUpToDate(string outputAssetPath, string stamp, bool force)
        {
            return !force && File.Exists(ForestManifest.ProjectDiskPath(outputAssetPath)) && ForestStamps.IsCurrent(outputAssetPath, stamp);
        }

        // ---- pixel maths: plain arrays in, plain arrays out ----

        /// <summary>
        /// Poly Haven "arm" is AO, roughness, metallic. HDRP/Lit wants metallic, AO, detail mask, smoothness.
        /// </summary>
        internal static void ToLitMask(Color32[] arm)
        {
            for (int i = 0; i < arm.Length; i++)
            {
                Color32 source = arm[i];
                arm[i] = new Color32(source.b, source.r, 255, (byte)(255 - source.g));
            }
        }

        /// <summary>
        /// TerrainLit wants metallic, AO, height, smoothness. Metallic is written as 0: no ground layer is metal.
        /// </summary>
        internal static void ToTerrainMask(Color32[] arm, Color32[] heights)
        {
            for (int i = 0; i < arm.Length; i++)
            {
                Color32 source = arm[i];
                arm[i] = new Color32(0, source.r, heights[i].r, (byte)(255 - source.g));
            }
        }

        /// <summary>
        /// Copies one channel of the mask image into the alpha of the colour image. Alpha files are grey,
        /// RGB or RGBA: the red channel is the mask in all of them. Only if red is constant and alpha is
        /// not (a file decoded as alpha-only) is the alpha channel taken instead.
        /// </summary>
        internal static void SetAlphaFromMask(Color32[] colour, Color32[] mask)
        {
            bool redVaries = false, alphaVaries = false;
            Color32 first = mask[0];
            for (int i = 1; i < mask.Length && !redVaries; i++)
            {
                if (mask[i].r != first.r) redVaries = true;
                else if (mask[i].a != first.a) alphaVaries = true;
            }
            bool useAlpha = !redVaries && alphaVaries;
            for (int i = 0; i < colour.Length; i++)
                colour[i].a = useAlpha ? mask[i].a : mask[i].r;
        }

        /// <summary>Share of texels that survive the alpha test at this cutoff.</summary>
        internal static double Coverage(Color32[] pixels, float cutoff)
        {
            float threshold = cutoff * 255f;
            long kept = 0;
            for (int i = 0; i < pixels.Length; i++)
                if (pixels[i].a >= threshold) kept++;
            return pixels.Length == 0 ? 0.0 : (double)kept / pixels.Length;
        }

        /// <summary>Mean of the blue channel, 0 to 1.</summary>
        internal static double MeanBlue(Color32[] pixels)
        {
            long sum = 0;
            for (int i = 0; i < pixels.Length; i++)
                sum += pixels[i].b;
            return pixels.Length == 0 ? 0.0 : sum / (255.0 * pixels.Length);
        }

        /// <summary>Halves an image with a 2 x 2 box filter. Width and height must be even.</summary>
        internal static Color32[] HalveBox(Color32[] source, int width, int height)
        {
            int halfWidth = width / 2, halfHeight = height / 2;
            var result = new Color32[halfWidth * halfHeight];
            for (int y = 0; y < halfHeight; y++)
            {
                int upper = 2 * y * width, lower = upper + width;
                for (int x = 0; x < halfWidth; x++)
                {
                    Color32 a = source[upper + 2 * x], b = source[upper + 2 * x + 1];
                    Color32 c = source[lower + 2 * x], d = source[lower + 2 * x + 1];
                    result[y * halfWidth + x] = new Color32(
                        (byte)((a.r + b.r + c.r + d.r + 2) >> 2),
                        (byte)((a.g + b.g + c.g + d.g + 2) >> 2),
                        (byte)((a.b + b.b + c.b + d.b + 2) >> 2),
                        (byte)((a.a + b.a + c.a + d.a + 2) >> 2));
                }
            }
            return result;
        }

        internal static Color AlphaWeightedLinearMean(Color32[] pixels, string what)
        {
            if (srgbToLinear == null)
            {
                // The sRGB transfer function, written out so the table does not depend on the engine.
                srgbToLinear = new float[256];
                for (int i = 0; i < 256; i++)
                {
                    double encoded = i / 255.0;
                    srgbToLinear[i] = (float)(encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4));
                }
            }
            double red = 0, green = 0, blue = 0, weight = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                if (pixel.a == 0) continue;
                double w = pixel.a / 255.0;
                red += w * srgbToLinear[pixel.r];
                green += w * srgbToLinear[pixel.g];
                blue += w * srgbToLinear[pixel.b];
                weight += w;
            }
            if (weight <= 0)
                throw new InvalidDataException($"The foliage atlas of '{what}' is fully transparent: it has no mean colour.");
            return new Color((float)(red / weight), (float)(green / weight), (float)(blue / weight), 1f);
        }

        /// <summary>
        /// Reads the pixel size of a PNG or JPG from its header, without decoding it (a 4k normal map is
        /// 100 MB on disk). False when the header is not understood.
        /// </summary>
        internal static bool TryReadImageSize(string diskPath, out int width, out int height)
        {
            width = height = 0;
            using (FileStream stream = File.OpenRead(diskPath))
            {
                var head = new byte[24];
                int read = stream.Read(head, 0, head.Length);

                // PNG: signature, then the IHDR chunk with width and height as big-endian 32-bit numbers.
                if (read >= 24 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47
                    && head[12] == 0x49 && head[13] == 0x48 && head[14] == 0x44 && head[15] == 0x52)
                {
                    width = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                    height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
                    return width > 0 && height > 0;
                }

                // JPG: walk the segments to the first start-of-frame, which holds height then width.
                if (read < 4 || head[0] != 0xFF || head[1] != 0xD8) return false;
                stream.Position = 2;
                var segment = new byte[7];
                while (true)
                {
                    int marker = stream.ReadByte();
                    if (marker < 0) return false;
                    if (marker != 0xFF) continue;
                    do { marker = stream.ReadByte(); } while (marker == 0xFF);   // fill bytes
                    if (marker < 0) return false;
                    if (marker == 0x00 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD8)) continue;   // no length
                    if (marker == 0xD9 || marker == 0xDA) return false;   // image data starts: no frame header seen

                    if (stream.Read(segment, 0, 2) != 2) return false;
                    int length = (segment[0] << 8) | segment[1];
                    if (length < 2) return false;
                    bool startOfFrame = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                    if (startOfFrame)
                    {
                        if (stream.Read(segment, 0, 5) != 5) return false;
                        height = (segment[1] << 8) | segment[2];
                        width = (segment[3] << 8) | segment[4];
                        return width > 0 && height > 0;
                    }
                    stream.Position += length - 2;
                }
            }
        }

        // ---- guards ----

        static ForestAssetFile RequireFile(ForestAssetEntry entry, string relPath, string materialName, string map)
        {
            return ForestManifest.FileAt(entry, relPath)
                ?? throw new InvalidDataException($"Material {materialName} of {entry.id} has no '{map}' file in the manifest.");
        }

        static void RequireEqual(int width, int height, int otherWidth, int otherHeight, string relPath, string otherRelPath)
        {
            if (width != otherWidth || height != otherHeight)
                throw new InvalidDataException(
                    $"Texture sizes differ inside one set: '{relPath}' is {width} x {height}, '{otherRelPath}' is {otherWidth} x {otherHeight}.");
        }

        // The maps Unity imports directly are not decoded here; their size is read from the header.
        static void RequireSameSize(ForestAssetEntry entry, string relPath, int width, int height, string referenceRelPath)
        {
            if (ForestManifest.FileAt(entry, relPath) == null) return;   // this material has no such map
            if (!TryReadImageSize(ForestManifest.DiskPath(relPath), out int otherWidth, out int otherHeight))
            {
                Debug.LogWarning($"Forest packer: the size of '{relPath}' could not be read from its header, " +
                                 $"so it was not compared with '{referenceRelPath}'.");
                return;
            }
            RequireEqual(width, height, otherWidth, otherHeight, referenceRelPath, relPath);
        }

        static void RequireNotMetal(Color32[] arm, string relPath)
        {
            double metallic = MeanBlue(arm);
            if (metallic > MaxMeanMetallic)
                throw new InvalidDataException(
                    $"'{relPath}': the blue channel has a mean of {metallic:F3}, above {MaxMeanMetallic}. " +
                    "That is not the metal channel of an AO/roughness/metallic map of rock, soil or plants.");
        }

        static void ShrinkToMaskSize(ref Color32[] pixels, ref int width, ref int height)
        {
            while ((width > MaxMaskSize || height > MaxMaskSize) && width % 2 == 0 && height % 2 == 0)
            {
                pixels = HalveBox(pixels, width, height);
                width /= 2;
                height /= 2;
            }
        }
    }
}
