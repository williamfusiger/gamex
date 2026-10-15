using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Finds the sky of a lighting preset and measures how much light it gives. HDRI Sky in Lux mode
    /// divides the wanted lux by the lux of the unscaled image, and only the Inspector computes that
    /// number (with a shader blit). A script-built profile left at the default of 1 would multiply the
    /// sky by the raw lux value, so the builder integrates a 1k copy of the sky on the CPU instead, which
    /// works the same in batch mode.
    /// </summary>
    public static class ForestSky
    {
        public struct Calibration { public float upperHemisphereLux; public Vector3 luxColor; public float lowerHemisphereLux; public float brightestAzimuthDegrees; }

        // The manifest's tags for the two files of a sky entry.
        const string SkyMap = "sky";
        const string CalibrationMap = "skyCalibration";

        // Rec. 709, as HDRP's own integration shader weighs the result.
        const double LuminanceR = 0.2126, LuminanceG = 0.7152, LuminanceB = 0.0722;

        /// <summary>
        /// Illuminance a horizontal surface receives from each half of a lat-long sky image, in the image's
        /// own units. Pure: rows run bottom to top (as Texture2D.GetPixels returns them), columns left to right.
        /// </summary>
        public static Calibration Integrate(Color[] latLongPixels, int width, int height)
        {
            if (latLongPixels == null)
                throw new ArgumentNullException(nameof(latLongPixels));
            if (width < 1 || height < 2 || (long)width * height != latLongPixels.Length)
                throw new ArgumentException($"A lat-long image of {width} x {height} cannot have {latLongPixels.Length} pixels.");

            const double halfPi = Math.PI / 2.0;
            double upperR = 0.0, upperG = 0.0, upperB = 0.0;
            double lowerR = 0.0, lowerG = 0.0, lowerB = 0.0;
            var columnLight = new double[width];

            for (int row = 0; row < height; row++)
            {
                // The last row touches the zenith. Each row is a band of polar angle, measured from the zenith.
                double bandTop = Math.PI * (height - 1 - row) / height;
                double bandBottom = Math.PI * (height - row) / height;

                // E = sum of L * cos(theta) * sin(theta) * dTheta * dPhi. The cos * sin * dTheta part is integrated
                // exactly over the band instead of sampled at its centre, so a small test image is as accurate as
                // a 1k sky, and a row that straddles the horizon is split between the two halves.
                double upperWeight = 0.5 * (Square(Math.Sin(Math.Min(bandBottom, halfPi))) - Square(Math.Sin(Math.Min(bandTop, halfPi))));
                double lowerWeight = 0.5 * (Square(Math.Sin(Math.Max(bandTop, halfPi))) - Square(Math.Sin(Math.Max(bandBottom, halfPi))));
                // Solid angle of the band above the horizon: what a column's pixel in this row stands for in the sky.
                double skyWeight = Math.Cos(Math.Min(bandTop, halfPi)) - Math.Cos(Math.Min(bandBottom, halfPi));

                double rowR = 0.0, rowG = 0.0, rowB = 0.0;
                int offset = row * width;
                for (int column = 0; column < width; column++)
                {
                    Color pixel = latLongPixels[offset + column];
                    rowR += pixel.r;
                    rowG += pixel.g;
                    rowB += pixel.b;
                    if (skyWeight > 0.0)
                        columnLight[column] += (LuminanceR * pixel.r + LuminanceG * pixel.g + LuminanceB * pixel.b) * skyWeight;
                }

                upperR += rowR * upperWeight;
                upperG += rowG * upperWeight;
                upperB += rowB * upperWeight;
                lowerR += rowR * lowerWeight;
                lowerG += rowG * lowerWeight;
                lowerB += rowB * lowerWeight;
            }

            double columnAngle = 2.0 * Math.PI / width;
            upperR *= columnAngle;
            upperG *= columnAngle;
            upperB *= columnAngle;

            // HDRISkyEditor's colour rule: normalise by the largest channel, then halve.
            double largest = Math.Max(upperR, Math.Max(upperG, upperB));
            if (!(largest > 0.0))
                largest = 1.0;

            // The first of equally bright columns wins, so the answer never depends on anything but the image.
            int brightest = 0;
            for (int column = 1; column < width; column++)
            {
                if (columnLight[column] > columnLight[brightest])
                    brightest = column;
            }
            double azimuth = ((brightest + 0.5) / width - 0.5) * 360.0;
            if (azimuth < 0.0)
                azimuth += 360.0;

            return new Calibration
            {
                upperHemisphereLux = (float)(LuminanceR * upperR + LuminanceG * upperG + LuminanceB * upperB),
                luxColor = new Vector3((float)(upperR / largest), (float)(upperG / largest), (float)(upperB / largest)) * 0.5f,
                lowerHemisphereLux = (float)((LuminanceR * lowerR + LuminanceG * lowerG + LuminanceB * lowerB) * columnAngle),
                brightestAzimuthDegrees = (float)azimuth,
            };
        }

        /// <summary>
        /// Integrates the 1k EXR of a sky (Source~/&lt;id&gt;__sky1k.exr). Throws when the file is missing or the
        /// result could not be used: a wrong number here is a black or a white frame.
        /// </summary>
        public static Calibration Calibrate(string skyId)
        {
            ForestAssetFile file = FindFile(skyId, CalibrationMap)
                ?? throw new ArgumentException($"'{skyId}' is not a sky of the asset manifest: no file of it is tagged '{CalibrationMap}'.");
            string diskPath = ForestManifest.DiskPath(file.relPath);
            if (!File.Exists(diskPath))
                throw new FileNotFoundException($"The calibration image of sky '{skyId}' has not been downloaded: {diskPath}", diskPath);

            // Size and format are placeholders: LoadImage replaces both.
            var image = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                if (!ImageConversion.LoadImage(image, File.ReadAllBytes(diskPath), false))
                    throw new InvalidOperationException($"Unity could not read the calibration image of sky '{skyId}': {diskPath}");
                // An EXR loads as RGBAFloat. Anything else was clipped to 8 bits and cannot hold sky radiance.
                if (image.format != TextureFormat.RGBAFloat && image.format != TextureFormat.RGBAHalf)
                    throw new InvalidOperationException($"The calibration image of sky '{skyId}' loaded as {image.format}, not as a float image: {diskPath}");
                if (image.width != image.height * 2)
                    throw new InvalidOperationException($"The calibration image of sky '{skyId}' is {image.width} x {image.height}, not a 2:1 lat-long panorama: {diskPath}");

                Calibration result = Integrate(image.GetPixels(), image.width, image.height);

                if (!IsUsableLux(result.upperHemisphereLux))
                    throw new InvalidOperationException($"Sky '{skyId}' integrates to {result.upperHemisphereLux} lux over the upper hemisphere. " +
                        "The value must be finite and positive; the image holds invalid pixels or is not a sky.");

                Debug.Log($"Forest sky {skyId}: {image.width} x {image.height} {image.format}, upper hemisphere {result.upperHemisphereLux} lux " +
                    $"in image units, lower hemisphere {result.lowerHemisphereLux}, colour {result.luxColor}, brightest at azimuth {result.brightestAzimuthDegrees} degrees.");

                // In a pure-sky HDRI the lower half is a blurred mirror of the upper half, which is what makes the
                // number safe even against a row-order mistake. A lopsided image has lost that protection.
                float ratio = result.lowerHemisphereLux / result.upperHemisphereLux;
                if (!(ratio > 0.5f && ratio < 2f))
                    Debug.LogWarning($"Forest sky {skyId}: the lower hemisphere gives {ratio} times the light of the upper one. " +
                        "A pure-sky HDRI gives about the same from both; check that this is one and that it is the right way up.");

                return result;
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        /// <summary>
        /// The imported sky cubemap, or null when the sky has not been downloaded. Throws when the file is on
        /// disk but is not a cubemap, because that sky would otherwise be mistaken for a missing one.
        /// </summary>
        public static Cubemap LoadCubemap(string skyId)
        {
            ForestAssetFile file = FindFile(skyId, SkyMap);
            if (file == null || !File.Exists(ForestManifest.DiskPath(file.relPath)))
                return null;

            string assetPath = ForestManifest.AssetPath(file.relPath);
            var cubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(assetPath);
            if (cubemap == null)
            {
                // Downloaded while the Editor was open and not picked up yet.
                AssetDatabase.ImportAsset(assetPath);
                cubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(assetPath);
            }
            if (cubemap == null)
                throw new InvalidOperationException($"{assetPath} is on disk but did not import as a cubemap. " +
                    "Its importer must have the texture shape Cube (ForestAssetPostprocessor sets that for files ending in __sky.hdr).");
            return cubemap;
        }

        static ForestAssetFile FindFile(string skyId, string map)
        {
            if (string.IsNullOrEmpty(skyId))
                return null;
            ForestAssetEntry entry = ForestManifest.Find(skyId);
            if (entry == null || entry.files == null)
                return null;
            foreach (ForestAssetFile file in entry.files)
            {
                if (file != null && file.map == map)
                    return file;
            }
            return null;
        }

        // NaN fails the comparison too.
        static bool IsUsableLux(float lux) => lux > 0f && !float.IsInfinity(lux);

        static double Square(double value) => value * value;
    }
}
