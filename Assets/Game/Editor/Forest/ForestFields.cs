using UnityEngine;

namespace Gamex.EditorTools
{
    /// <summary>
    /// The ground of the forest as plain arrays, before any Unity object exists: heights and the
    /// fields derived from them, one sample per heightmap sample. Arrays are indexed [z, x], the
    /// order <c>TerrainData.SetHeights</c> wants. The scatter planner, the terrain painter and the
    /// tests all read the same arrays, so a pivot is never grounded on a height the terrain does
    /// not have.
    /// </summary>
    public sealed class ForestFields              // plain arrays; no Unity objects
    {
        public int resolution;                    // 1025
        public float[,] height;                   // world metres, [z, x]
        public float[,] slopeDegrees, thicket, trailDistance, gullyDistance, boundaryDistance;
        // D2 (density pass): the thicket field as it was before the pass (ThicketCentre 0.4525), for the ground paint only, so the
        // humus of the thickets keeps its extent while the young firs follow the denser field. Null: the painter uses thicket.
        internal float[,] paintThicket;

        /// <summary>World X or Z in metres to a (fractional) sample index. The tile is centred on the origin.</summary>
        public float WorldToIndex(float worldCoordinate)
        {
            return (worldCoordinate + ForestLayout.TileSize * 0.5f) / ForestLayout.TileSize * (resolution - 1);
        }

        /// <summary>World height in metres, interpolated bilinearly between the four samples around the point.</summary>
        public float HeightAt(float x, float z) => Sample(height, x, z);

        /// <summary>Steepness in degrees. Zero until the slope field has been computed.</summary>
        public float SlopeAt(float x, float z) => slopeDegrees != null ? Sample(slopeDegrees, x, z) : 0f;

        /// <summary>Unit normal of the ground, from the heights one sample to each side.</summary>
        public Vector3 NormalAt(float x, float z)
        {
            float step = SampleSpacing;
            float gradientX = (HeightAt(x + step, z) - HeightAt(x - step, z)) / (2f * step);
            float gradientZ = (HeightAt(x, z + step) - HeightAt(x, z - step)) / (2f * step);
            return new Vector3(-gradientX, 1f, -gradientZ).normalized;
        }

        // ---- Shared by the files of work package 3 ----

        /// <summary>Metres between two neighbouring samples: 0.25 on the 1025 grid.</summary>
        internal float SampleSpacing => ForestLayout.TileSize / (resolution - 1);

        /// <summary>World X or Z of a sample index.</summary>
        internal float IndexToWorld(int index) => index * SampleSpacing - ForestLayout.TileSize * 0.5f;

        /// <summary>Bilinear read of any of the fields at a world position. Positions off the tile read its edge.</summary>
        internal float Sample(float[,] field, float x, float z)
        {
            int last = resolution - 1;
            float fx = Mathf.Clamp(WorldToIndex(x), 0f, last);
            float fz = Mathf.Clamp(WorldToIndex(z), 0f, last);
            int x0 = Mathf.Min((int)fx, last - 1), z0 = Mathf.Min((int)fz, last - 1);
            float tx = fx - x0, tz = fz - z0;
            float south = field[z0, x0] + (field[z0, x0 + 1] - field[z0, x0]) * tx;
            float north = field[z0 + 1, x0] + (field[z0 + 1, x0 + 1] - field[z0 + 1, x0]) * tx;
            return south + (north - south) * tz;
        }

        /// <summary>
        /// Recomputes the slope field from the heights inside a rectangle of sample indices (inclusive).
        /// Central differences, one-sided on the edge of the tile.
        /// </summary>
        internal void RefreshSlope(int minX, int maxX, int minZ, int maxZ)
        {
            int last = resolution - 1;
            float step = SampleSpacing;
            minX = Mathf.Max(minX, 0); minZ = Mathf.Max(minZ, 0);
            maxX = Mathf.Min(maxX, last); maxZ = Mathf.Min(maxZ, last);
            for (int z = minZ; z <= maxZ; z++)
            {
                int zLow = z > 0 ? z - 1 : z, zHigh = z < last ? z + 1 : z;
                for (int x = minX; x <= maxX; x++)
                {
                    int xLow = x > 0 ? x - 1 : x, xHigh = x < last ? x + 1 : x;
                    float gradientX = (height[z, xHigh] - height[z, xLow]) / ((xHigh - xLow) * step);
                    float gradientZ = (height[zHigh, x] - height[zLow, x]) / ((zHigh - zLow) * step);
                    slopeDegrees[z, x] = Mathf.Atan(Mathf.Sqrt(gradientX * gradientX + gradientZ * gradientZ)) * Mathf.Rad2Deg;
                }
            }
        }

        internal void RefreshSlope() => RefreshSlope(0, resolution - 1, 0, resolution - 1);
    }
}
