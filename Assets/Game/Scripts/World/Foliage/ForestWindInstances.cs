using System.Collections.Generic;
using UnityEngine;

namespace Gamex
{
    /// <summary>
    /// W1 I2b (owner's decision of 2026-10-17, issues/W1-runtime.md section 11): the wind terms that are the same for every vertex of an
    /// instance (pivot hash, gust, size scale, the trunk's oscillators, the wind directions in object space) computed once a frame per
    /// renderer instead of in every vertex of every pass. Each mesh renderer drawn with a wind material gets a slot through
    /// <see cref="MeshRenderer.SetShaderUserValue"/>, which the GPU Resident Drawer carries per instance (MaterialPropertyBlocks would take the
    /// renderers off the drawer). FoliageInteraction.compute's Instances kernel evaluates the vertex shader's own GamexInstanceAnalytic for
    /// every slot at this frame's time and at last frame's (HDRP's _TimeParameters and _LastTimeParameters: Time.time and
    /// Time.time - Time.deltaTime), into two float textures; ForestWind.hlsl reads a renderer's slot there. A renderer without a slot
    /// (0: registered after <see cref="Register"/>, or edit mode) computes the same terms itself, so nothing is drawn differently.
    /// Foliage is static: a registered renderer's pivot, rotation, scale and profile are read once.
    /// W1 review round 1 (issues/W1-runtime.md section 13):
    /// - each passable sapling and solid fir keeps a push state from frame to frame (<see cref="StateStride"/> float4 per slot): the
    ///   sapling's lean is a spring the kernel integrates from the bodies' stamps, the solid fir's swing sense is held while its press
    ///   decays; last frame's state goes to the previous texture for motion vectors;
    /// - <see cref="UpdatePressed"/> sets <see cref="PressedBit"/> in the user value of every registered renderer within reach of a stamp
    ///   (held <see cref="PressedMemorySeconds"/> after the last contact), and the vertex shader evaluates pushes only there.
    /// W1 review round 2 (issues/W1-runtime.md section 14): every slot also keeps the wind's dynamics (the mean lean a damped oscillator
    /// driven by the gust, the oscillating terms an envelope of it); a passable sapling's state is a root lean, an upper-stem bow, a
    /// clearance press and the latched body (six texels per slot, six float4 of state); a fir's crown reach in the push band is measured
    /// from its LOD0 mesh at registration.
    /// </summary>
    public sealed class ForestWindInstances
    {
        /// <summary>Slots per row of the instance textures (GAMEX_INSTANCE_COLUMNS in ForestWind.hlsl).</summary>
        public const int Columns = 1024;
        /// <summary>Texels per slot (GAMEX_INSTANCE_TEXELS): three of wind terms and three of the instance's push state (W1 R2: was 4).</summary>
        public const int TexelsPerSlot = 6;
        /// <summary>float4 values of static input per slot (the Instances kernel's _InstanceStatic).</summary>
        public const int StaticStride = 8;
        /// <summary>
        /// W1 R1: float4 values of persistent state per slot (the Instances kernel's _InstanceState, GAMEX_STATE_STRIDE). W1 R2: 6 (was 2):
        /// 0 and 1 the push state of R1 (a sapling's root lean, a solid fir's swing), 2 the wind's dynamics, 3 to 5 a sapling's bow,
        /// clearance press and latched body.
        /// </summary>
        public const int StateStride = 6;
        /// <summary>W1 R2: the push band whose crown reach is measured for each fir (metres above its pivot): where bodies press it.</summary>
        public const float BandBottom = 0.3f, BandTop = 2.2f;
        /// <summary>W1 R1: the user-value bit of a registered renderer near a body (GAMEX_PRESSED_BIT); the slot is the low 30 bits.</summary>
        public const uint PressedBit = 0x40000000u;
        /// <summary>
        /// W1 R1: how long a renderer stays pressed after the last stamp within its reach: the field's press ends in about 3.3 s (tau
        /// 0.8 s and the tail), and a released sapling's spring settles within about 3 s.
        /// </summary>
        public const float PressedMemorySeconds = 5f;
        /// <summary>W1 R1: a stamp this much farther than an instance's reach already presses it (it marks the instance a few frames early).</summary>
        public const float PressedMargin = 1f;
        const float GridCell = 8f;

        static readonly int SwayId = Shader.PropertyToID("_GamexSway"), FlutterId = Shader.PropertyToID("_GamexFlutter"), MotionId = Shader.PropertyToID("_GamexMotion");
        static readonly int StemId = Shader.PropertyToID("_GamexStem");
        static readonly int StaticId = Shader.PropertyToID("_InstanceStatic"), OutId = Shader.PropertyToID("_InstanceOut");
        static readonly int CountId = Shader.PropertyToID("_InstanceCount"), TimeId = Shader.PropertyToID("_InstanceTime");
        static readonly int StateId = Shader.PropertyToID("_InstanceState"), StepId = Shader.PropertyToID("_InstanceStep");

        readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
        Vector4[] statics = new Vector4[0];
        ComputeBuffer staticBuffer, stateBuffer;
        RenderTexture current, previous;
        int skipped;
        // W1 R1: the pressed bits.
        Vector2[] pivots = new Vector2[0];
        float[] reaches = new float[0], lastContact = new float[0];
        bool[] pressed = new bool[0];
        float maxReach;
        readonly List<int> pressedSlots = new List<int>();
        readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        // W1 R2: each LOD0 mesh's largest horizontal radius per 0.1 m of object-space height.
        readonly Dictionary<Mesh, float[]> radiusProfiles = new Dictionary<Mesh, float[]>();
        const float ProfileStep = 0.1f;

        /// <summary>Registered renderers; slot i + 1 is renderers[i].</summary>
        public int Count => renderers.Count;
        /// <summary>Renderers with a wind material that were left to compute their own terms (their wind materials disagree on the profile).</summary>
        public int Skipped => skipped;
        public IReadOnlyList<MeshRenderer> Renderers => renderers;
        public RenderTexture CurrentTexture => current;
        public RenderTexture PreviousTexture => previous;
        /// <summary>W1 R1: registered renderers whose pressed bit is set now.</summary>
        public int PressedCount => pressedSlots.Count;
        /// <summary>W1 R1 (tests): whether slot <paramref name="slot"/> (1-based) has its pressed bit set.</summary>
        public bool IsPressed(int slot) => slot >= 1 && slot <= pressed.Length && pressed[slot - 1];
        /// <summary>
        /// W1 R1 (tests): every slot's persistent push state (<see cref="StateStride"/> float4 per slot; a passable sapling's: (lean,
        /// angular velocity, direction x, z), (latched side x, z, this frame's target lean, its clearance need)), or null before the first
        /// dispatch.
        /// </summary>
        public ComputeBuffer StateBuffer => stateBuffer;
        /// <summary>W1 R1 (tests): the 1-based slot of a registered renderer, or 0.</summary>
        public int SlotOf(Renderer renderer) => renderer is MeshRenderer mesh ? renderers.IndexOf(mesh) + 1 : 0;

        /// <summary>
        /// Gives every loaded mesh renderer drawn with a wind material a slot (its shader user value) and reads its static input. Any
        /// earlier registration is dropped first. Returns the number of slots.
        /// </summary>
        public int Register()
        {
            Unregister();
            var materials = new List<Material>();
            var rows = new List<Vector4>();
            var pivotList = new List<Vector2>();
            var reachList = new List<float>();
            foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                renderer.GetSharedMaterials(materials);
                Material first = null;
                bool consistent = true;
                foreach (Material material in materials)
                {
                    if (!ForestWind.IsWindMaterial(material)) continue;
                    if (first == null) { first = material; continue; }
                    if (material.GetVector(SwayId) != first.GetVector(SwayId) || material.GetVector(FlutterId) != first.GetVector(FlutterId) ||
                        material.GetVector(MotionId) != first.GetVector(MotionId) || material.GetVector(StemId) != first.GetVector(StemId))
                        consistent = false;
                }
                if (first == null) continue;
                if (!consistent)
                {
                    skipped++;
                    renderer.SetShaderUserValue(0u);
                    continue;
                }
                renderers.Add(renderer);
                renderer.SetShaderUserValue((uint)renderers.Count);
                Matrix4x4 o2w = renderer.localToWorldMatrix, w2o = renderer.worldToLocalMatrix;
                Vector3 pivot = o2w.GetColumn(3);
                float scale = ((Vector3)o2w.GetColumn(1)).magnitude;   // the Object node's Scale.y: the length of the second column
                rows.Add(new Vector4(pivot.x, pivot.y, pivot.z, scale));
                // W1 R1: the first row carries the instance's object-space height (its LOD0 mesh's top, the same for every level, so a
                // bent sapling bends alike at its LOD switches); the second its horizontal reach from the pivot (m, world).
                // W1 R2: the third its crown's reach in the push band (m, world; 0 for ferns and grass): a solid fir's contact begins where
                // a body's surface meets it, a passable sapling's lean where the body enters its lower crown.
                float reach = HorizontalReach(renderer.bounds, pivot);
                Vector4 stemVector = first.GetVector(StemId);
                float crown = stemVector.x > 0f || stemVector.w > 0f ? CrownReach(renderer, scale) : 0f;
                float[] extra = { ObjectHeight(renderer), reach, crown };
                for (int r = 0; r < 3; r++)
                {
                    Vector4 row = w2o.GetRow(r);
                    rows.Add(new Vector4(row.x, row.y, row.z, extra[r]));
                }
                rows.Add(first.GetVector(SwayId));
                rows.Add(first.GetVector(FlutterId));
                rows.Add(first.GetVector(MotionId));
                rows.Add(first.GetVector(StemId));
                pivotList.Add(new Vector2(pivot.x, pivot.z));
                reachList.Add(reach);
            }
            if (skipped > 0)
                Debug.LogWarning($"ForestWindInstances: {skipped} renderer(s) carry wind materials with different profiles; they compute their own wind terms.");
            statics = rows.ToArray();
            pivots = pivotList.ToArray();
            reaches = reachList.ToArray();
            lastContact = new float[renderers.Count];
            pressed = new bool[renderers.Count];
            maxReach = 0f;
            grid.Clear();
            for (int i = 0; i < pivots.Length; i++)
            {
                maxReach = Mathf.Max(maxReach, reaches[i]);
                long key = Cell(Mathf.FloorToInt(pivots[i].x / GridCell), Mathf.FloorToInt(pivots[i].y / GridCell));
                if (!grid.TryGetValue(key, out List<int> list)) grid[key] = list = new List<int>();
                list.Add(i);
            }
            return renderers.Count;
        }

        // The top of the instance's LOD0 mesh in its own space (every level of a wind prefab shares the instance's transform), else the
        // renderer's own mesh's.
        static float ObjectHeight(MeshRenderer renderer)
        {
            LODGroup group = renderer.GetComponentInParent<LODGroup>();
            if (group != null)
            {
                LOD[] lods = group.GetLODs();
                if (lods.Length > 0)
                    foreach (Renderer first in lods[0].renderers)
                    {
                        MeshFilter filter = first != null ? first.GetComponent<MeshFilter>() : null;
                        if (filter != null && filter.sharedMesh != null) return filter.sharedMesh.bounds.max.y;
                    }
            }
            MeshFilter own = renderer.GetComponent<MeshFilter>();
            return own != null && own.sharedMesh != null ? own.sharedMesh.bounds.max.y : 0f;
        }

        // W1 R2: the largest horizontal distance from the pivot of the instance's LOD0 vertices between BandBottom and BandTop above the
        // pivot, in world metres (every level of an instance takes LOD0's, so its levels agree).
        float CrownReach(MeshRenderer renderer, float scale)
        {
            Mesh mesh = Lod0Mesh(renderer);
            if (mesh == null || !mesh.isReadable || scale <= 0f) return 0f;
            if (!radiusProfiles.TryGetValue(mesh, out float[] profile))
            {
                Vector3[] vertices = mesh.vertices;
                float top = 0f;
                foreach (Vector3 v in vertices) top = Mathf.Max(top, v.y);
                profile = new float[Mathf.Max(1, Mathf.CeilToInt(top / ProfileStep) + 1)];
                foreach (Vector3 v in vertices)
                {
                    if (v.y < 0f) continue;
                    int bin = Mathf.Min(profile.Length - 1, Mathf.FloorToInt(v.y / ProfileStep));
                    profile[bin] = Mathf.Max(profile[bin], Mathf.Sqrt(v.x * v.x + v.z * v.z));
                }
                radiusProfiles[mesh] = profile;
            }
            int from = Mathf.Max(0, Mathf.FloorToInt(BandBottom / scale / ProfileStep)), to = Mathf.Min(profile.Length - 1, Mathf.FloorToInt(BandTop / scale / ProfileStep));
            float largest = 0f;
            for (int bin = from; bin <= to; bin++) largest = Mathf.Max(largest, profile[bin]);
            return largest * scale;
        }

        static Mesh Lod0Mesh(MeshRenderer renderer)
        {
            LODGroup group = renderer.GetComponentInParent<LODGroup>();
            if (group != null)
            {
                LOD[] lods = group.GetLODs();
                if (lods.Length > 0)
                    foreach (Renderer first in lods[0].renderers)
                    {
                        MeshFilter filter = first != null ? first.GetComponent<MeshFilter>() : null;
                        if (filter != null && filter.sharedMesh != null) return filter.sharedMesh;
                    }
            }
            MeshFilter own = renderer.GetComponent<MeshFilter>();
            return own != null ? own.sharedMesh : null;
        }

        static float HorizontalReach(Bounds bounds, Vector3 pivot)
        {
            float dx = Mathf.Max(Mathf.Abs(bounds.min.x - pivot.x), Mathf.Abs(bounds.max.x - pivot.x));
            float dz = Mathf.Max(Mathf.Abs(bounds.min.z - pivot.z), Mathf.Abs(bounds.max.z - pivot.z));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static long Cell(int x, int z) => ((long)x << 32) ^ (uint)z;

        /// <summary>Every registered renderer's slot back to 0 and the GPU resources released.</summary>
        public void Unregister()
        {
            foreach (MeshRenderer renderer in renderers)
                if (renderer != null)
                    renderer.SetShaderUserValue(0u);
            renderers.Clear();
            statics = new Vector4[0];
            pivots = new Vector2[0];
            reaches = lastContact = new float[0];
            pressed = new bool[0];
            pressedSlots.Clear();
            grid.Clear();
            radiusProfiles.Clear();
            skipped = 0;
            Release();
        }

        /// <summary>
        /// W1 R1: sets the pressed bit of every registered renderer whose reach a stamp comes within <see cref="PressedMargin"/> of (the
        /// stamps as the field's Step kernel has them: per stamp (ax, az, bx, bz), then (radius, band top, strength, softness)), and clears
        /// it <see cref="PressedMemorySeconds"/> after the last such stamp. <paramref name="fieldOn"/> false clears every bit.
        /// </summary>
        public void UpdatePressed(Vector4[] stamps, int count, float now, bool fieldOn)
        {
            if (!fieldOn) count = 0;
            for (int i = 0; i < count; i++)
            {
                Vector4 segment = stamps[2 * i], body = stamps[2 * i + 1];
                if (body.z <= 0f) continue;
                var a = new Vector2(segment.x, segment.y);
                var b = new Vector2(segment.z, segment.w);
                float extent = body.x + body.w + PressedMargin;
                float grow = extent + maxReach;
                int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - grow) / GridCell), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + grow) / GridCell);
                int z0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - grow) / GridCell), z1 = Mathf.FloorToInt((Mathf.Max(a.y, b.y) + grow) / GridCell);
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        if (!grid.TryGetValue(Cell(x, z), out List<int> list)) continue;
                        foreach (int k in list)
                        {
                            if (SegmentDistance(pivots[k], a, b) >= reaches[k] + extent) continue;
                            lastContact[k] = now;
                            if (pressed[k]) continue;
                            pressed[k] = true;
                            pressedSlots.Add(k);
                            if (renderers[k] != null) renderers[k].SetShaderUserValue((uint)(k + 1) | PressedBit);
                        }
                    }
            }
            for (int j = pressedSlots.Count - 1; j >= 0; j--)
            {
                int k = pressedSlots[j];
                if (fieldOn && now - lastContact[k] <= PressedMemorySeconds) continue;
                pressed[k] = false;
                pressedSlots.RemoveAt(j);
                if (renderers[k] != null) renderers[k].SetShaderUserValue((uint)(k + 1));
            }
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>
        /// Evaluates every slot's terms at <paramref name="previousTime"/> into the previous texture (with each slot's push state as it
        /// stands: last frame's), then at <paramref name="time"/> into the current one (both already include the controller's time
        /// offset), with the wind vectors of <paramref name="state"/>. W1 R1: with <paramref name="integrate"/>, the second dispatch first
        /// advances every push state by <paramref name="deltaTime"/> seconds from the first <paramref name="stampCount"/> stamps the field's
        /// Step kernel has on <paramref name="shader"/> (0: no body), and writes the new state.
        /// </summary>
        /// <summary>W1 R2: <see cref="Dispatch"/>'s flags: the wind's dynamics start again from this frame's wind, or are off (tests).</summary>
        public const int ResetDynamicsFlag = 1, NoDynamicsFlag = 2;

        public bool Dispatch(ComputeShader shader, int kernel, in ForestWindState state, float time, float previousTime,
            float deltaTime = 0f, int stampCount = 0, bool integrate = true, int flags = 0)
        {
            int count = renderers.Count;
            if (shader == null || kernel < 0 || count == 0)
                return false;
            if (staticBuffer == null || staticBuffer.count != statics.Length)
            {
                staticBuffer?.Release();
                staticBuffer = new ComputeBuffer(statics.Length, 16);
                staticBuffer.SetData(statics);
            }
            if (stateBuffer == null || stateBuffer.count != count * StateStride)
            {
                stateBuffer?.Release();
                stateBuffer = new ComputeBuffer(count * StateStride, 16);
                stateBuffer.SetData(new Vector4[count * StateStride]);
            }
            int height = (count + Columns - 1) / Columns;
            current = Ensure(current, "Foliage Wind Instances", height);
            previous = Ensure(previous, "Foliage Wind Instances Prev", height);
            ForestWindGlobals.WriteTo(shader, state);
            shader.SetBuffer(kernel, StaticId, staticBuffer);
            shader.SetBuffer(kernel, StateId, stateBuffer);
            shader.SetInt(CountId, count);
            int groups = (count + 63) / 64;
            shader.SetVector(StepId, new Vector4(0f, 0f, 0f, flags & NoDynamicsFlag));
            shader.SetFloat(TimeId, previousTime);
            shader.SetTexture(kernel, OutId, previous);
            shader.Dispatch(kernel, groups, 1, 1);
            shader.SetVector(StepId, new Vector4(integrate ? 1f : 0f, Mathf.Max(0f, deltaTime), Mathf.Clamp(stampCount, 0, FoliageInteractionField.MaxStamps), integrate ? flags : flags & NoDynamicsFlag));
            shader.SetFloat(TimeId, time);
            shader.SetTexture(kernel, OutId, current);
            shader.Dispatch(kernel, groups, 1, 1);
            return true;
        }

        /// <summary>W1 R1 (tests): every slot's state back to rest (a sapling upright and still, a solid fir's sense unknown; W1 R2: the wind's
        /// dynamics start again from the next frame's wind).</summary>
        public void ResetState()
        {
            if (stateBuffer != null) stateBuffer.SetData(new Vector4[stateBuffer.count]);
        }

        static RenderTexture Ensure(RenderTexture texture, string name, int height)
        {
            if (texture != null && texture.IsCreated() && texture.height == height)
                return texture;
            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
            }
            texture = new RenderTexture(TexelsPerSlot * Columns, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                name = name, enableRandomWrite = true, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false,
            };
            texture.Create();
            return texture;
        }

        /// <summary>Releases the textures and the buffers (the registration stays).</summary>
        public void Release()
        {
            staticBuffer?.Release();
            staticBuffer = null;
            stateBuffer?.Release();
            stateBuffer = null;
            foreach (RenderTexture texture in new[] { current, previous })
            {
                if (texture == null) continue;
                texture.Release();
                Destroy(texture);
            }
            current = previous = null;
        }

        static void Destroy(Object target)
        {
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
