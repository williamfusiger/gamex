using UnityEngine;

namespace Gamex
{
    /// <summary>
    /// The CPU map of how dense the foliage is (W1 spec 2.10), baked from the plan at scene build (ForestWindRig.BakeDensity): 0.5 m
    /// cells, a low layer for ferns and grass and a mid layer for young-fir lower crowns, one byte a cell (0 = none, 255 = full).
    /// Cell (i, j) covers origin + (i, j) x cellSize to one cell further; values sit at cell centres and are interpolated between.
    /// </summary>
    public sealed class FoliageDensityMap : ScriptableObject
    {
        public Vector2 origin;
        public float cellSize = 0.5f;
        public int width, height;
        public byte[] low, mid;

        /// <summary>Bilinear between cell centres, 0 outside the map.</summary>
        public float Sample(Vector2 xz, FoliageLayer layer)
        {
            byte[] cells = layer == FoliageLayer.Low ? low : layer == FoliageLayer.Mid ? mid : null;
            if (cells == null || width <= 0 || height <= 0 || cellSize <= 0f || cells.Length < width * height)
                return 0f;
            float fx = (xz.x - origin.x) / cellSize - 0.5f, fz = (xz.y - origin.y) / cellSize - 0.5f;
            int x0 = Mathf.FloorToInt(fx), z0 = Mathf.FloorToInt(fz);
            float tx = fx - x0, tz = fz - z0;
            float a = At(cells, x0, z0), b = At(cells, x0 + 1, z0), c = At(cells, x0, z0 + 1), d = At(cells, x0 + 1, z0 + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        float At(byte[] cells, int x, int z)
        {
            if (x < 0 || z < 0 || x >= width || z >= height) return 0f;
            return cells[z * width + x] * (1f / 255f);
        }
    }
}
