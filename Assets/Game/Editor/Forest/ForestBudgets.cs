namespace Gamex.EditorTools
{
    /// <summary>
    /// What the forest may cost and what counts as wrong. Every number is provisional: a starting value
    /// for 60 frames per second at 2560 x 1440 on the M4 Max, in play mode in the Editor, until a
    /// measurement replaces it. A check fails above the budget and warns at the warning level.
    ///
    /// The PlayMode test assembly cannot reference this assembly, so it keeps a copy
    /// (Assets/Game/Tests/ForestPlayMode/ForestPlayBudgets.cs). The EditMode test
    /// Budgets_PlayModeCopyMatches compares the two; a number changed here must be changed there.
    /// </summary>
    public static class ForestBudgets
    {
        // ---- Per frame, read from ProfilerRecorder counters by the PlayMode suite ----

        /// <summary>The resolution the per-frame budgets are stated for.</summary>
        public const int FrameWidth = 2560;
        public const int FrameHeight = 1440;

        /// <summary>
        /// Median frame time in milliseconds as Budget_FrameTime measures it in the Editor (a stopwatch around a render and a
        /// one-pixel readback at trail_start, an upper bound that serialises CPU and GPU).
        /// </summary>
        /// <remarks>
        /// D2 (build/issues/D2-density.md): the frame budget is no longer this Editor proxy (11.0 ms, warning 9.5 ms) but a real
        /// measurement: in a macOS development player at 2560 x 1440 with vsync off, the worst of the 29 standard views must keep its
        /// median frame time at or under 12.5 ms and its p95 at or under 15 ms (coordinator's decision, 2026-10-15; 60 frames per
        /// second with room for dinosaur AI and animation). The Editor figure is re-based on the measured relation between the two:
        /// over 58 view pairs (29 views, the scene before and after the density pass, measured the same day) the player's frame time
        /// was 1.078 x the stopwatch - 3.05 ms (r = 0.995, residuals 0.13 ms); a player frame of 12.5 ms is a stopwatch of 14.4 ms,
        /// one of 11.0 ms a stopwatch of 13.0 ms. With a doubled stand as a stress load (stopwatch up to 18.4 ms) the line holds
        /// (1.032 x - 2.61, r = 0.998: 14.6 ms). The player's speed drifts between sessions, and other sessions gave flatter lines
        /// (0.671 x + 0.58 and 0.752 x - 0.33: 17.8 and 17.1 ms); the steepest, which protects the player budget, is used. Budget
        /// 14.0 ms (below every fit), warning 13.0 ms.
        /// </remarks>
        public const float GpuFrameMsBudget = 14.0f;
        public const float GpuFrameMsWarn = 13.0f;

        /// <summary>"Triangles Count" over all passes: decals force a depth prepass, so every opaque triangle counts twice, plus shadows.</summary>
        /// <remarks>
        /// Re-based on experiment E1 as spec section 10 asks ("the triangle, vertex and mesh-memory budgets are re-based on
        /// the measured numbers of the chosen arm in the same change; the frame-time budget stays where it is"). The spec's
        /// 8.0 M (warning 6.5 M) assumed arm A. The chosen young firs (the scan to 6 m, then flattened levels) measured
        /// 12,482,801 triangles and 17,019,198 vertices at crouch_hide, the worst budget view, against 8,901,125 and
        /// 8,058,338 for arm A in the same scene: +3.6 M triangles and +9.0 M vertices (a flattened needle is a quad of four
        /// vertices). Both budgets and warnings rise by that much. build/issues/M3-E1.md.
        /// <para>
        /// D2, the density pass (build/issues/D2-density.md), re-based it again on frame-time evidence: with about 280 tall and 660 young
        /// firs a hectare the worst budget view (crouch_hide) draws 16.86 M triangles and 27.48 M vertices (Editor counter), and the
        /// worst view of the 29 (pano_90, 18.39 M) costs 9.6 to 10.5 ms a frame in the macOS development player against the player
        /// budget of 12.5 ms (see GpuFrameMsBudget). Budget about 13 % above the measured worst budget view, warning about 4 % above it.
        /// </para>
        /// </remarks>
        public const long TrianglesBudget = 19000000L;
        public const long TrianglesWarn = 17500000L;

        /// <summary>"Vertices Count". Re-based with the triangles (see there): E1 19.0 M and 17.0 M; D2 31.0 M and 28.5 M.</summary>
        public const long VerticesBudget = 31000000L;
        public const long VerticesWarn = 28500000L;

        /// <summary>"Draw Calls Count" without the GPU Resident Drawer.</summary>
        public const long DrawCallsBudget = 4000L;
        public const long DrawCallsWarn = 3000L;

        /// <summary>"SetPass Calls Count".</summary>
        public const long SetPassCallsBudget = 250L;
        public const long SetPassCallsWarn = 200L;

        /// <summary>"Shadow Casters Count".</summary>
        public const long ShadowCastersBudget = 2500L;
        public const long ShadowCastersWarn = 2000L;

        /// <summary>"Texture Memory" above an empty scene, in bytes: 1.0 GiB and 0.8 GiB.</summary>
        public const long TextureMemoryBudget = 1073741824L;
        public const long TextureMemoryWarn = 858993459L;

        /// <summary>"Mesh Memory" above an empty scene, in bytes: 400 MiB and 320 MiB.</summary>
        /// <remarks>
        /// The spec's 300 and 220 MiB, re-based on experiment E1 (spec section 10): the three young-fir scans the chosen
        /// arm keeps as a nearest level hold 1,747,107 vertices of 48 bytes and 1,533,513 triangles of 32-bit indices,
        /// 97.6 MiB (the spec estimated about 105), rounded to 100 MiB.
        /// </remarks>
        public const long MeshMemoryBudget = 419430400L;
        public const long MeshMemoryWarn = 335544320L;

        // ---- Scene totals, counted by the audit on every build ----

        public const int UniqueMaterialsBudget = 40;
        public const int UniqueMaterialsWarn = 30;

        /// <summary>Meshes that renderers draw. Collision meshes are not drawn and are not counted.</summary>
        public const int UniqueMeshesBudget = 120;
        public const int UniqueMeshesWarn = 100;

        /// <summary>Two-dimensional textures imported at 4096. The sky cubemap is not one of them.</summary>
        public const int Textures4096Budget = 8;
        public const int Textures4096Warn = 6;

        /// <summary>No texture may be imported larger than this.</summary>
        public const int LargestTextureSize = 4096;

        /// <summary>
        /// Re-based by D2, the density pass (build/issues/D2-density.md; spec 3,500 / 14,000): the owner asked for a much denser forest,
        /// and the final plan holds 5,825 instances and 23,274 GameObjects, at 9.6 to 10.5 ms a frame at the worst view in the macOS
        /// development player (budget 12.5 ms). The Editor stayed responsive (scene load in play mode 1.9 s before, 2.1 s after; scene
        /// build 20 s). Budget about 12 % above the measurement, warning about 5 % above it.
        /// </summary>
        public const int PrefabInstancesBudget = 6500;
        public const int PrefabInstancesWarn = 6100;

        public const int GameObjectsBudget = 26000;
        public const int GameObjectsWarn = 24500;

        // ---- Limits the audit enforces on the built scene ----

        /// <summary>Steepest gradient along the trail centre line. Sprinting leaves the ground at about 18 degrees.</summary>
        public const float TrailSlopeLimitDegrees = 12f;

        /// <summary>Steepest slope anywhere inside the boundary.</summary>
        public const float CoreSlopeLimitDegrees = 27f;

        /// <summary>
        /// What a slope read back from the terrain may exceed its limit by. The heightmap is 16 bits over
        /// 60 m (0.9 mm per step); over the 0.5 m of a central difference that rounding is worth 0.1 degrees.
        /// It is a property of the measurement, not slack in the limit.
        /// </summary>
        public const float SlopeReadbackToleranceDegrees = 0.15f;

        /// <summary>How far a placement's pivot may sit from "terrain height minus its sink".</summary>
        public const float GroundingToleranceMetres = 0.03f;

        /// <summary>The player starts this far above the terrain, and may be off by the tolerance.</summary>
        public const float SpawnLiftMetres = 0.05f;
        public const float SpawnLiftToleranceMetres = 0.02f;

        /// <summary>Half the width of the trail's tread: a spawn point further from the centre line is not on the trail.</summary>
        public const float TrailTreadHalfWidthMetres = 0.8f;

        /// <summary>The capsule the physical corridor check sweeps along every trail, and how high it floats.</summary>
        public const float CorridorCapsuleHeightMetres = 4f;
        public const float CorridorCapsuleLiftMetres = 0.3f;

        // ---- What a rendered frame must look like ----

        /// <summary>Share of near-magenta or near-cyan pixels a frame may have before it counts as broken (0.1 %).</summary>
        public const float ErrorPixelShareLimit = 0.001f;

        /// <summary>Mean luminance window of a tonemapped first-person frame under the OvercastMist preset.</summary>
        public const float OvercastLuminanceMin = 0.10f;
        public const float OvercastLuminanceMax = 0.45f;

        /// <summary>Largest share of a frame that may be clipped to white, and crushed to black.</summary>
        public const float ClippedWhiteShareLimit = 0.05f;
        public const float CrushedBlackShareLimit = 0.20f;
    }
}
