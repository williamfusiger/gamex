using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Gamex.EditorTools
{
    /// <summary>
    /// First stage of the forest build (spec 11.5): takes what Tools/forest/fetch_assets.py downloaded
    /// and leaves imported textures and finished materials behind. It downloads nothing and never
    /// deletes or renames a source file.
    /// <c>Unity -batchmode -projectPath . -executeMethod Gamex.EditorTools.ForestImport.Run -quit</c>
    /// </summary>
    public static class ForestImport
    {
        // Part of the verification stamps: raise it to check every downloaded file again.
        const string ToolVersion = "import-1";

        const string FetchHint =
            "python3 -I Tools/forest/fetch_assets.py Assets/Game/Editor/Forest/forest-asset-manifest.json . --milestone <M1..M6>";

        /// <summary>
        /// Verifies size and md5 of the files that are present, packs the textures, refreshes the asset
        /// database and builds the materials. Work whose inputs are unchanged is skipped;
        /// <c>-forestForce</c> re-does everything.
        /// </summary>
        [MenuItem("Gamex/Forest/1 Import Assets")]
        public static void Run()
        {
            bool force = ForestArgs.Flag("-forestForce");
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // The fast compressor is fine while iterating, but the choice persists in Library and changes
            // every texture, so each run says which one is active. Final captures are made with None.
            Debug.Log($"Forest import: texture compression override is {EditorUserBuildSettings.overrideTextureCompression}" +
                      (force ? "; -forestForce: everything is redone." : "."));

            ForestAssetEntry[] manifest = ForestManifest.All;

            List<string> tableProblems = ForestAssetPostprocessor.TableProblems(manifest);
            if (tableProblems.Count > 0)
                throw new InvalidDataException(
                    "ForestAssetPostprocessor's per-asset table and the manifest disagree. Correct the table " +
                    "(and raise its Version) or the manifest:\n  " + string.Join("\n  ", tableProblems));

            VerifyDownloads(manifest, force, out List<string> complete, out List<string> incomplete, out List<string> unmeasured);

            int packed = ForestTexturePacker.PackAll(force);

            // Imports new downloads and the textures the packer has just written. ForestAssetPostprocessor
            // sets their import settings on the way in.
            AssetDatabase.Refresh();

            ForestMaterials.BuildAll(force);

            Debug.Log($"Forest import finished in {watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s: " +
                      $"{complete.Count} of {manifest.Length} assets downloaded ({string.Join(", ", complete)}); {packed} textures packed.");
            if (incomplete.Count > 0)
                Debug.LogWarning("Forest import: these assets are only partly downloaded and were left out: " +
                                 string.Join(", ", incomplete) + ". Fetch them with: " + FetchHint);
            if (unmeasured.Count > 0)
                Debug.LogWarning("Forest import: these models are downloaded but their manifest sizes were never measured on the FBX " +
                                 "(objectsSource is not \"fbx\"), so the kit build will refuse them: " + string.Join(", ", unmeasured) +
                                 ". Run Tools/forest/fbx_objects.py on each (the fetch tool does it).");
        }

        /// <summary>
        /// Checks every manifest file that is on disk against the manifest's byte size and md5, and throws
        /// if any differs: a wrong file must never reach the packer or a prefab. A file that was verified
        /// before and has not been written since is not hashed again.
        /// </summary>
        static void VerifyDownloads(ForestAssetEntry[] manifest, bool force,
            out List<string> complete, out List<string> incomplete, out List<string> unmeasured)
        {
            complete = new List<string>();
            incomplete = new List<string>();
            unmeasured = new List<string>();
            var wrong = new List<string>();
            int hashed = 0;

            foreach (ForestAssetEntry entry in manifest)
            {
                int present = 0;
                foreach (ForestAssetFile file in entry.files)
                {
                    var onDisk = new FileInfo(ForestManifest.DiskPath(file.relPath));
                    if (!onDisk.Exists) continue;
                    present++;

                    if (onDisk.Length != file.bytes)
                    {
                        wrong.Add($"{file.relPath}: {onDisk.Length} bytes, the manifest says {file.bytes}");
                        continue;
                    }
                    // The key has its own prefix, so it cannot collide with a stamp another stage keeps for
                    // the same path. The stamp changes when the file is written again.
                    string product = "verified:" + file.relPath;
                    string stamp = ForestStamps.Hash(ToolVersion, file.md5, file.bytes.ToString(CultureInfo.InvariantCulture),
                        onDisk.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
                    if (!force && ForestStamps.IsCurrent(product, stamp)) continue;

                    string actual = Md5Of(onDisk.FullName);
                    hashed++;
                    if (!string.Equals(actual, file.md5, StringComparison.OrdinalIgnoreCase))
                    {
                        wrong.Add($"{file.relPath}: md5 {actual}, the manifest says {file.md5}");
                        continue;
                    }
                    ForestStamps.Set(product, stamp);
                }

                if (present == entry.files.Length)
                {
                    complete.Add(entry.id);
                    if (entry.kind == "model" && !ForestManifest.IsMeasured(entry)) unmeasured.Add(entry.id);
                }
                else if (present > 0)
                {
                    incomplete.Add($"{entry.id} ({present} of {entry.files.Length} files)");
                }
            }

            if (wrong.Count > 0)
                throw new InvalidDataException(
                    $"{wrong.Count} downloaded files do not match the manifest:\n  " + string.Join("\n  ", wrong) +
                    "\nFetch them again (a file that differs is replaced): " + FetchHint);
            Debug.Log($"Forest import: {complete.Count} assets complete on disk, {incomplete.Count} partly downloaded; " +
                      $"{hashed} files hashed in this run.");
        }

        static string Md5Of(string diskPath)
        {
            using (MD5 md5 = MD5.Create())
            using (var stream = new FileStream(diskPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            {
                byte[] digest = md5.ComputeHash(stream);
                var hex = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest)
                    hex.Append(value.ToString("x2"));
                return hex.ToString();
            }
        }
    }
}
