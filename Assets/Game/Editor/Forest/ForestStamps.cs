using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Remembers which inputs each expensive product was last built from, so a stage can skip work
    /// that would give the same result (spec 11.5). The record lives in Library, next to Unity's own
    /// import cache: deleting Library forgets every stamp and everything is rebuilt once.
    /// A stamp is only ever a reason to skip. When in doubt this class answers "not current".
    /// </summary>
    public static class ForestStamps
    {
        [Serializable] sealed class Stamp { public string product, hash; }
        [Serializable] sealed class Store { public Stamp[] stamps = Array.Empty<Stamp>(); }

        static Dictionary<string, string> cache;

        /// <summary>
        /// True when the product was last stamped with exactly this hash. A product named by a path
        /// under Assets/ must also still exist, so a file deleted by hand is rebuilt.
        /// </summary>
        public static bool IsCurrent(string product, string inputHash)
        {
            if (string.IsNullOrEmpty(product) || string.IsNullOrEmpty(inputHash)) return false;
            if (!Load().TryGetValue(product, out string stored) || stored != inputHash) return false;
            if (!product.StartsWith("Assets/", StringComparison.Ordinal)) return true;
            string onDisk = ForestManifest.ProjectDiskPath(product);
            return File.Exists(onDisk) || Directory.Exists(onDisk);
        }

        /// <summary>Records that the product now reflects these inputs. Call it only after the product was written.</summary>
        public static void Set(string product, string inputHash)
        {
            if (string.IsNullOrEmpty(product)) throw new ArgumentException("A stamp needs a product key.", nameof(product));
            if (string.IsNullOrEmpty(inputHash)) throw new ArgumentException("A stamp needs an input hash.", nameof(inputHash));
            Dictionary<string, string> stamps = Load();
            if (stamps.TryGetValue(product, out string stored) && stored == inputHash) return;
            stamps[product] = inputHash;
            Save(stamps);
        }

        /// <summary>
        /// Drops a product's stamp. Called before a product is rewritten, so that work which is
        /// interrupted half-way is never mistaken for finished work on the next run.
        /// </summary>
        internal static void Forget(string product)
        {
            if (string.IsNullOrEmpty(product)) return;
            Dictionary<string, string> stamps = Load();
            if (stamps.Remove(product)) Save(stamps);
        }

        /// <summary>
        /// One hash for a list of inputs. Each part is written with its length, so ("ab", "c") and
        /// ("a", "bc") differ. A null part counts as "".
        /// </summary>
        public static string Hash(params string[] parts)
        {
            var text = new StringBuilder();
            foreach (string part in parts ?? Array.Empty<string>())
            {
                string value = part ?? "";
                text.Append(value.Length).Append(':').Append(value).Append('|');
            }
            using (MD5 md5 = MD5.Create())
            {
                byte[] digest = md5.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
                var hex = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest)
                    hex.Append(value.ToString("x2"));
                return hex.ToString();
            }
        }

        static string StorePath => ForestManifest.ProjectDiskPath(ForestPaths.StampsPath);

        static Dictionary<string, string> Load()
        {
            if (cache != null) return cache;
            cache = new Dictionary<string, string>();
            string path = StorePath;
            if (!File.Exists(path)) return cache;

            // The file is a cache. If it cannot be read, forgetting it costs one rebuild and can never
            // cause a wrong skip, so that is what happens, and the log says so.
            Store store = null;
            try
            {
                store = JsonUtility.FromJson<Store>(File.ReadAllText(path));
            }
            catch (ArgumentException error)
            {
                Debug.LogWarning($"Forest stamps: {ForestPaths.StampsPath} is unreadable ({error.Message}). Every product will be rebuilt once.");
            }
            if (store?.stamps != null)
            {
                foreach (Stamp stamp in store.stamps)
                    if (stamp != null && !string.IsNullOrEmpty(stamp.product) && !string.IsNullOrEmpty(stamp.hash))
                        cache[stamp.product] = stamp.hash;
            }
            return cache;
        }

        static void Save(Dictionary<string, string> stamps)
        {
            // Sorted, so the file does not depend on the order the products were built in.
            var products = new List<string>(stamps.Keys);
            products.Sort(StringComparer.Ordinal);
            var store = new Store { stamps = new Stamp[products.Count] };
            for (int i = 0; i < products.Count; i++)
                store.stamps[i] = new Stamp { product = products[i], hash = stamps[products[i]] };

            string path = StorePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // Written beside the file and moved into place, so a crash cannot leave half a file.
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(store, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }
    }
}
