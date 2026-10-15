using UnityEngine;

namespace Gamex.Tests.Forest
{
    /// <summary>
    /// The numbers the PlayMode suite needs from the editor code, which this assembly cannot reference.
    /// Every constant that has the name of one in ForestBudgets or ForestLayout is a copy of it; the
    /// EditMode test Budgets_PlayModeCopyMatches fails when a copy drifts. One constant per line, with a
    /// plain literal, because that test reads this file as text.
    /// </summary>
    public static class ForestPlayBudgets
    {
        // ---- Copies of ForestBudgets ----

        public const int FrameWidth = 2560;
        public const int FrameHeight = 1440;

        public const float GpuFrameMsBudget = 14.0f;
        public const float GpuFrameMsWarn = 13.0f;

        public const long TrianglesBudget = 21500000L;
        public const long TrianglesWarn = 20000000L;
        public const long VerticesBudget = 36000000L;
        public const long VerticesWarn = 33000000L;
        public const long DrawCallsBudget = 4000L;
        public const long DrawCallsWarn = 3000L;
        public const long SetPassCallsBudget = 250L;
        public const long SetPassCallsWarn = 200L;
        public const long ShadowCastersBudget = 2500L;
        public const long ShadowCastersWarn = 2000L;
        public const long TextureMemoryBudget = 1073741824L;
        public const long TextureMemoryWarn = 858993459L;
        public const long MeshMemoryBudget = 419430400L;
        public const long MeshMemoryWarn = 335544320L;

        public const float ErrorPixelShareLimit = 0.001f;
        public const float OvercastLuminanceMin = 0.10f;
        public const float OvercastLuminanceMax = 0.45f;
        public const float ClippedWhiteShareLimit = 0.05f;
        public const float CrushedBlackShareLimit = 0.20f;
        public const float NightDarkLuminanceMin = 0.02f;
        public const float NightDarkLuminanceMax = 0.10f;
        public const float NightLitLuminanceMin = 0.04f;
        public const float NightLitLuminanceMax = 0.20f;
        public const float NightLitAreaShareMin = 0.05f;
        public const float NightLitAreaLuminanceMin = 0.15f;
        public const float NightClippedWhiteShareLimit = 0.01f;

        public const float CorridorCapsuleHeightMetres = 4f;
        public const float CorridorCapsuleLiftMetres = 0.3f;

        // ---- Copies of ForestLayout ----

        public const float HalfExtentX = 65f;
        public const float HalfExtentZ = 75f;
        public const float CornerRadius = 30f;
        public const float CorridorAgentRadius = 2.0f;

        // ---- This suite's own numbers ----

        /// <summary>Render tests: a quarter-size frame after this many frames at a fixed step of 1/60 s.</summary>
        public const int RenderWidth = 960;
        public const int RenderHeight = 540;
        public const int RenderFrames = 32;
        public const int StillFrames = 64;

        /// <summary>Night exposure test: a 16-pixel block of the frame belongs to the lit area when the torch makes it this much brighter (0 to 1).</summary>
        public const int LitBlockSize = 16;
        public const float LitBlockGain = 0.03f;

        /// <summary>How the player moves: PlayerController's walk and sprint speed, gravity, and the pull that keeps it grounded.</summary>
        public const float WalkSpeed = 3.2f;
        public const float SprintSpeed = 6f;
        public const float Gravity = -20f;
        public const float GroundedPull = -2f;

        /// <summary>The spawn sits this far above the terrain; a controller rests one skin width above the ground it stands on.</summary>
        public const float SpawnHeightTolerance = 0.05f;

        /// <summary>Largest share of frames a sprint along the trail may spend off the ground.</summary>
        public const float AirborneShareLimit = 0.02f;

        /// <summary>Where a probe pushed against the edge for 20 s must still be: the walls stand 1.5 m outside the boundary.</summary>
        public const float EscapeLimitX = 67f;
        public const float EscapeLimitZ = 77f;
        public const float EscapeSeconds = 20f;

        /// <summary>Negative inside the playable core: a rounded rectangle, as ForestLayout.SignedDistanceToBoundary.</summary>
        public static float SignedDistanceToBoundary(Vector2 xz)
        {
            float qx = Mathf.Abs(xz.x) - (HalfExtentX - CornerRadius);
            float qz = Mathf.Abs(xz.y) - (HalfExtentZ - CornerRadius);
            float outsideX = Mathf.Max(qx, 0f), outsideZ = Mathf.Max(qz, 0f);
            float outside = Mathf.Sqrt(outsideX * outsideX + outsideZ * outsideZ);
            float inside = Mathf.Min(Mathf.Max(qx, qz), 0f);
            return outside + inside - CornerRadius;
        }
    }
}
