using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Gamex.Tests.Forest
{
    /// <summary>
    /// W1 (spec 5.2, 8.6): runs ForestWindProbe.compute, which includes ForestWind.hlsl, so the tests read the wind shader's own
    /// functions back from the GPU. Every value a kernel reads is set on the ComputeShader itself (whether a compute shader sees
    /// Shader.SetGlobal* values is not established for this Unity version). The same file is copied into ForestPlayMode, as the two
    /// test assemblies cannot reference each other. Buffers are released in finally.
    /// </summary>
    public static class ForestWindProbeSupport
    {
        public const string ProbePath = "Assets/Game/Tests/ForestEditMode/ForestWindProbe.compute";

        /// <summary>One instance of a wind prefab as the Deform kernel sees it.</summary>
        public struct Instance
        {
            public Vector3 pivot; public float scale, yaw;
            /// <summary>I2: when set, the instance's full rotation (trees tilt up to 3 degrees) instead of the yaw alone.</summary>
            public bool useRotation; public Quaternion rotation;
            public Vector3 camera; public float time;
            public Vector4 sway, flutter, motion, push, stem;
            public bool moonPass, torchPass;
            /// <summary>
            /// W1 R1: the instance's push state this frame and last frame (texel 3 of its slot: a passable sapling's lean, a solid fir's
            /// swing sense), as the Instances kernel had it; without it the probe computes the vertex shader's fallback.
            /// </summary>
            public bool leanGiven; public Vector4 lean, leanPrevious;
            /// <summary>
            /// W1 R2: texels 4 and 5 of the push state this frame and last frame (a passable sapling's bow, clearance press, latched body's
            /// radius and band top, and its axis segment); zero for a solid fir.
            /// </summary>
            public Vector4 leanB, leanC, leanPreviousB, leanPreviousC;
        }

        /// <summary>The interaction field and its rects (origin X, origin Z, 1 / size, valid). Default: no field.</summary>
        public struct Field
        {
            public Texture current, previous;
            public Vector4 rect, previousRect;
        }

#if UNITY_EDITOR
        public static ComputeShader Load()
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ProbePath);
            if (shader == null)
                throw new InvalidOperationException($"{ProbePath} did not import as a compute shader.");
            return shader;
        }
#endif

        /// <summary>GamexGust at each (x, z, t), read back from the GPU.</summary>
        public static float[] RunGust(ComputeShader shader, Gamex.ForestWindState state, Vector3[] xzt)
        {
            int kernel = shader.FindKernel("Gust");
            var input = new ComputeBuffer(Math.Max(1, xzt.Length), 16);
            var output = new ComputeBuffer(Math.Max(1, xzt.Length), 4);
            try
            {
                var data = new Vector4[xzt.Length];
                for (int i = 0; i < xzt.Length; i++) data[i] = new Vector4(xzt[i].x, xzt[i].y, xzt[i].z, 0f);
                input.SetData(data);
                SetGlobals(shader, state, default);
                shader.SetInt("_Count", xzt.Length);
                shader.SetBuffer(kernel, "_GustInput", input);
                shader.SetBuffer(kernel, "_GustOutput", output);
                shader.Dispatch(kernel, (xzt.Length + 63) / 64, 1, 1);
                var result = new float[xzt.Length];
                output.GetData(result);
                return result;
            }
            finally
            {
                input.Release();
                output.Release();
            }
        }

        /// <summary>
        /// GamexWindDeform on object-space vertices of one instance: displaced positions, normals and Custom Velocity, all in object space.
        /// </summary>
        public static void RunDeform(ComputeShader shader, Gamex.ForestWindState state, Instance instance, Field field,
            Vector3[] positions, Vector3[] normals, Vector3[] tangents, Color[] colours,
            out Vector3[] outPositions, out Vector3[] outNormals, out Vector3[] outVelocities) =>
            RunDeform(shader, state, instance, field, positions, normals, tangents, colours, null, out outPositions, out outNormals, out outVelocities);

        /// <summary>W1 R2: as RunDeform, with each vertex's UV2 (a fern frond's or grass blade's anchor; null = 0, as on trees).</summary>
        public static void RunDeform(ComputeShader shader, Gamex.ForestWindState state, Instance instance, Field field,
            Vector3[] positions, Vector3[] normals, Vector3[] tangents, Color[] colours, Vector4[] anchors,
            out Vector3[] outPositions, out Vector3[] outNormals, out Vector3[] outVelocities)
        {
            int n = positions.Length;
            int kernel = shader.FindKernel("Deform");
            var buffers = new ComputeBuffer[8];
            try
            {
                for (int i = 0; i < buffers.Length; i++) buffers[i] = new ComputeBuffer(Math.Max(1, n), 16);
                var data = new Vector4[n];
                for (int i = 0; i < n; i++) data[i] = positions[i];
                buffers[0].SetData(data);
                for (int i = 0; i < n; i++) data[i] = normals != null ? (Vector4)normals[i] : new Vector4(0f, 1f, 0f, 0f);
                buffers[1].SetData(data);
                for (int i = 0; i < n; i++) data[i] = tangents != null ? (Vector4)tangents[i] : new Vector4(1f, 0f, 0f, 0f);
                buffers[2].SetData(data);
                for (int i = 0; i < n; i++) data[i] = colours[i];
                buffers[3].SetData(data);
                for (int i = 0; i < n; i++) data[i] = anchors != null ? anchors[i] : Vector4.zero;
                buffers[7].SetData(data);

                SetGlobals(shader, state, field);
                shader.SetInt("_Count", n);
                Quaternion turn = instance.useRotation ? instance.rotation : Quaternion.Euler(0f, instance.yaw, 0f);
                Matrix4x4 o2w = Matrix4x4.TRS(Vector3.zero, turn, Vector3.one * instance.scale);
                Matrix4x4 w2o = o2w.inverse;
                shader.SetVector("_ProbePivot", new Vector4(instance.pivot.x, instance.pivot.y, instance.pivot.z, instance.scale));
                shader.SetVector("_ProbeCamera", new Vector4(instance.camera.x, instance.camera.y, instance.camera.z, instance.time));
                shader.SetVector("_ProbeO2W0", o2w.GetRow(0));
                shader.SetVector("_ProbeO2W1", o2w.GetRow(1));
                shader.SetVector("_ProbeO2W2", o2w.GetRow(2));
                shader.SetVector("_ProbeW2O0", w2o.GetRow(0));
                shader.SetVector("_ProbeW2O1", w2o.GetRow(1));
                shader.SetVector("_ProbeW2O2", w2o.GetRow(2));
                shader.SetVector("_ProbeSway", instance.sway);
                shader.SetVector("_ProbeFlutter", instance.flutter);
                shader.SetVector("_ProbeMotion", instance.motion);
                shader.SetVector("_ProbePush", instance.push);
                shader.SetVector("_ProbeStem", instance.stem);
                shader.SetVector("_ProbePass", new Vector4(instance.moonPass ? 1f : 0f, instance.torchPass ? 1f : 0f, instance.leanGiven ? 1f : 0f, 0f));
                shader.SetVector("_ProbeLean", instance.lean);
                shader.SetVector("_ProbeLeanPrev", instance.leanGiven ? instance.leanPrevious : instance.lean);
                shader.SetVector("_ProbeLeanB", instance.leanB);
                shader.SetVector("_ProbeLeanC", instance.leanC);
                shader.SetVector("_ProbeLeanPrevB", instance.leanGiven ? instance.leanPreviousB : instance.leanB);
                shader.SetVector("_ProbeLeanPrevC", instance.leanGiven ? instance.leanPreviousC : instance.leanC);
                shader.SetBuffer(kernel, "_DeformPosition", buffers[0]);
                shader.SetBuffer(kernel, "_DeformNormal", buffers[1]);
                shader.SetBuffer(kernel, "_DeformTangent", buffers[2]);
                shader.SetBuffer(kernel, "_DeformColour", buffers[3]);
                shader.SetBuffer(kernel, "_DeformAnchor", buffers[7]);
                shader.SetBuffer(kernel, "_DeformOutPosition", buffers[4]);
                shader.SetBuffer(kernel, "_DeformOutNormal", buffers[5]);
                shader.SetBuffer(kernel, "_DeformOutVelocity", buffers[6]);
                Texture current = field.current != null ? field.current : Texture2D.blackTexture;
                Texture previous = field.previous != null ? field.previous : current;
                shader.SetTexture(kernel, "_GamexInteractionTex", current);
                shader.SetTexture(kernel, "_GamexInteractionTexPrev", previous);
                shader.SetTexture(kernel, "_GamexGustTex", Texture2D.blackTexture);
                shader.Dispatch(kernel, (n + 63) / 64, 1, 1);

                var read = new Vector4[n];
                outPositions = new Vector3[n];
                outNormals = new Vector3[n];
                outVelocities = new Vector3[n];
                buffers[4].GetData(read);
                for (int i = 0; i < n; i++) outPositions[i] = read[i];
                buffers[5].GetData(read);
                for (int i = 0; i < n; i++) outNormals[i] = read[i];
                buffers[6].GetData(read);
                for (int i = 0; i < n; i++) outVelocities[i] = read[i];
            }
            finally
            {
                foreach (ComputeBuffer buffer in buffers) buffer?.Release();
            }
        }

        // The wind globals exactly as ForestWindGlobals.Write computes them: written, read back, set on the compute shader, then
        // every global cleared again (all zero is the rest pose the rest of the session expects).
        static void SetGlobals(ComputeShader shader, Gamex.ForestWindState state, Field field)
        {
            try
            {
                Gamex.ForestWindGlobals.Write(state);
                shader.SetVector(Gamex.ForestWindGlobals.Wind, Shader.GetGlobalVector(Gamex.ForestWindGlobals.Wind));
                shader.SetVector(Gamex.ForestWindGlobals.Gust, Shader.GetGlobalVector(Gamex.ForestWindGlobals.Gust));
                shader.SetVector(Gamex.ForestWindGlobals.Params, Shader.GetGlobalVector(Gamex.ForestWindGlobals.Params));
                shader.SetVector(Gamex.ForestWindGlobals.Fade, Shader.GetGlobalVector(Gamex.ForestWindGlobals.Fade));
            }
            finally
            {
                Gamex.ForestWindGlobals.Clear();
            }
            shader.SetVector(Gamex.ForestWindGlobals.GustRect, Vector4.zero);
            shader.SetVector(Gamex.ForestWindGlobals.InteractionRect, field.current != null ? field.rect : Vector4.zero);
            shader.SetVector(Gamex.ForestWindGlobals.InteractionRectPrev, field.current != null ? field.previousRect : Vector4.zero);
        }
    }
}
