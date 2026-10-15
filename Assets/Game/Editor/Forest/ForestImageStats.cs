using System;
using UnityEngine;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Numbers that describe a rendered frame, so a capture can be judged without anyone opening it:
    /// is it black, blown out, or full of the colours HDRP paints when a shader is broken?
    /// Everything works on the 8-bit pixels of the saved image, which is what a reviewer sees.
    /// </summary>
    public static class ForestImageStats
    {
        public struct Stats { public float meanLuminance, medianLuminance, shareBlack, shareWhite, meanR, meanG, meanB, shareMagenta, shareCyan; }

        // Rec. 709 weights, applied to the encoded values: "how bright does the picture look", not scene light.
        const float WeightR = 0.2126f, WeightG = 0.7152f, WeightB = 0.0722f;

        // A pixel darker than 2/255 is crushed to black; one brighter than 253/255 is clipped to white.
        const float BlackBelow = 2f, WhiteAbove = 253f;

        /// <summary>All values are shares or levels between 0 and 1. An empty image gives zeros.</summary>
        public static Stats Compute(Color32[] pixels)
        {
            var stats = new Stats();
            if (pixels == null || pixels.Length == 0)
                return stats;

            long sumR = 0, sumG = 0, sumB = 0;
            double sumLuminance = 0.0;
            long black = 0, white = 0, magenta = 0, cyan = 0;
            // The median comes from a histogram: sorting 3.7 million values per capture would be wasteful.
            var histogram = new int[256];

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                sumR += pixel.r;
                sumG += pixel.g;
                sumB += pixel.b;

                float luminance = WeightR * pixel.r + WeightG * pixel.g + WeightB * pixel.b;
                sumLuminance += luminance;
                histogram[Mathf.Clamp(Mathf.RoundToInt(luminance), 0, 255)]++;
                if (luminance < BlackBelow) black++;
                else if (luminance > WhiteAbove) white++;

                if (IsNearMagenta(pixel)) magenta++;
                else if (IsNearCyan(pixel)) cyan++;
            }

            double count = pixels.Length;
            stats.meanLuminance = (float)(sumLuminance / count / 255.0);
            stats.medianLuminance = Median(histogram, pixels.Length) / 255f;
            stats.shareBlack = (float)(black / count);
            stats.shareWhite = (float)(white / count);
            stats.meanR = (float)(sumR / count / 255.0);
            stats.meanG = (float)(sumG / count / 255.0);
            stats.meanB = (float)(sumB / count / 255.0);
            stats.shareMagenta = (float)(magenta / count);
            stats.shareCyan = (float)(cyan / count);
            return stats;
        }

        /// <summary>
        /// Root mean square difference of two images over the three colour channels, between 0 (identical)
        /// and 1. Returns -1 when the images cannot be compared: one is missing or their sizes differ.
        /// </summary>
        public static float Rmse(Color32[] a, Color32[] b)
        {
            if (a == null || b == null || a.Length == 0 || a.Length != b.Length)
                return -1f;

            double sum = 0.0;
            for (int i = 0; i < a.Length; i++)
            {
                int dr = a[i].r - b[i].r;
                int dg = a[i].g - b[i].g;
                int db = a[i].b - b[i].b;
                sum += dr * dr + dg * dg + db * db;
            }
            return (float)(Math.Sqrt(sum / (3.0 * a.Length)) / 255.0);
        }

        // HDRP draws a material it cannot render in magenta, and a missing diffusion profile tints foliage magenta too.
        static bool IsNearMagenta(Color32 pixel) => pixel.r > 230 && pixel.g < 60 && pixel.b > 230;

        // The Editor shows cyan while a shader variant is still compiling.
        static bool IsNearCyan(Color32 pixel) => pixel.r < 60 && pixel.g > 230 && pixel.b > 230;

        static float Median(int[] histogram, int count)
        {
            // The lower median: the first level at which half of the pixels have been passed.
            long half = (count + 1L) / 2L;
            long seen = 0;
            for (int level = 0; level < histogram.Length; level++)
            {
                seen += histogram[level];
                if (seen >= half)
                    return level;
            }
            return 255f;
        }
    }
}
