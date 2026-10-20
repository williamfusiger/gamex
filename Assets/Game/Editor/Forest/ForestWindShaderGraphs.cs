using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Writes the two wind Shader Graphs (W1 spec sections 2.1, 3.1 to 3.3) from a fixed template, writes their .meta files
    /// before the first import, and checks the shaders (section 3.7).
    ///
    /// The graphs are HDRP Lit graphs written by us. Their JSON objects are modelled on graphs Unity itself wrote (the
    /// HDRPLit, Fern and PseudoSubsurface samples of com.unity.shadergraph, and HDRP's own SpeedTree9_HDRP graph for the
    /// Custom Velocity, Thickness and Diffusion Profile blocks), never with their ids or GUIDs. Object ids are derived from
    /// names, so the text is the same on every run. All vertex logic is in ForestWind.hlsl, all surface logic in
    /// ForestSurface.hlsl, each called through one File-mode Custom Function node. Do not open and re-save the graphs in the
    /// Shader Graph window: Unity would rewrite the ids and the file would differ from GraphText.
    /// </summary>
    public static class ForestWindShaderGraphs
    {
        public const string Folder = "Assets/Game/Shaders";
        public const string GraphPath = Folder + "/ForestWind.shadergraph";
        public const string TriplanarGraphPath = Folder + "/ForestWindTriplanar.shadergraph";
        public const string VertexIncludePath = Folder + "/ForestWind.hlsl";
        public const string SurfaceIncludePath = Folder + "/ForestSurface.hlsl";
        public const string ShaderName = "Gamex/ForestWind";
        public const string TriplanarShaderName = "Gamex/ForestWindTriplanar";

        // The ScriptedImporter of .shadergraph files (com.unity.shadergraph Editor/Importers/ShaderGraphImporter.cs.meta).
        const string ShaderGraphImporterGuid = "625f186215c104763be7675aa2d941aa";

        /// <summary>The fixed GUID of a W1 asset: the first 32 hex digits of MD5("gamex-w1/" + file name).</summary>
        public static string FixedGuid(string fileName) => Md5Hex("gamex-w1/" + fileName).Substring(0, 32);

        /// <summary>Reference names of every exposed property (section 3.3), in blackboard order. The triplanar graph adds _TexWorldScale.</summary>
        public static readonly string[] SurfaceProperties =
        {
            "_BaseColorMap", "_BaseColor", "_NormalMap", "_NormalScale", "_MaskMap",
            "_MetallicRemapMin", "_MetallicRemapMax", "_AORemapMin", "_AORemapMax", "_SmoothnessRemapMin", "_SmoothnessRemapMax",
            "_AlphaCutoff", "_Thickness",
        };
        public static readonly string[] WindProperties = { "_GamexSway", "_GamexFlutter", "_GamexMotion", "_GamexPush", "_GamexStem" };

        // ------------------------------------------------------------------------------------------------
        // Writing and importing
        // ------------------------------------------------------------------------------------------------

        /// <summary>The exact content of a graph file. Pure.</summary>
        public static string GraphText(bool triplanar) => new GraphWriter(triplanar).Write();

        public static string MetaText(bool triplanar)
        {
            string guid = FixedGuid(Path.GetFileName(triplanar ? TriplanarGraphPath : GraphPath));
            // The format of com.unity.shadergraph Samples~/ProductionReady/Lit/HDRPLit.shadergraph.meta, with no importer fields.
            return "fileFormatVersion: 2\n" +
                   $"guid: {guid}\n" +
                   "ScriptedImporter:\n" +
                   "  internalIDToNameTable: []\n" +
                   "  externalObjects: {}\n" +
                   "  serializedVersion: 2\n" +
                   "  userData: \n" +
                   "  assetBundleName: \n" +
                   "  assetBundleVariant: \n" +
                   $"  script: {{fileID: 11500000, guid: {ShaderGraphImporterGuid}, type: 3}}\n";
        }

        /// <summary>
        /// Writes graph and meta files where their content differs, then imports synchronously. True if anything was written.
        /// Throws on an import error (Shader Graph or shader compiler messages logged during the import).
        /// </summary>
        public static bool EnsureGraphs()
        {
            foreach (string include in new[] { VertexIncludePath, SurfaceIncludePath })
                if (!File.Exists(ForestManifest.ProjectDiskPath(include)))
                    throw new FileNotFoundException($"{include} is missing; the wind graphs call it.", include);

            bool written = false;
            var changed = new List<string>();
            foreach (bool triplanar in new[] { false, true })
            {
                string path = triplanar ? TriplanarGraphPath : GraphPath;
                // The meta first, so the first import already has the fixed GUID.
                if (WriteIfDifferent(path + ".meta", MetaText(triplanar))) written = true;
                if (WriteIfDifferent(path, GraphText(triplanar)) || Load(triplanar) == null)
                {
                    written = true;
                    changed.Add(path);
                }
            }
            if (changed.Count == 0)
                return written;

            // The includes must be known to the asset database before a graph that references them by GUID is imported.
            AssetDatabase.ImportAsset(VertexIncludePath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(SurfaceIncludePath, ImportAssetOptions.ForceSynchronousImport);
            List<string> errors = ImportCatchingErrors(changed);
            if (errors.Count > 0)
                throw new InvalidOperationException("Forest wind: the wind Shader Graphs did not import cleanly:\n  " + string.Join("\n  ", errors));
            foreach (string path in changed)
                Debug.Log($"Forest wind: {path} written and imported ({AssetDatabase.AssetPathToGUID(path)}).");
            return written;
        }

        [MenuItem("Gamex/Forest/Wind/Write Shader Graphs")]
        public static void WriteGraphsFromMenu()
        {
            bool written = EnsureGraphs();
            Debug.Log(written ? "Forest wind: shader graphs written." : "Forest wind: shader graphs already up to date.");
        }

        /// <summary>Batch entry: EnsureGraphs, then CheckShaders; throws on any problem.</summary>
        public static void WriteAndCheckFromBatch()
        {
            EnsureGraphs();
            CheckShadersFromMenu();
        }

        /// <summary>
        /// Makes sure the HDRP pipeline instance exists. Until the first render of an Editor session the engine has picked no
        /// sub-shader, so a Shader Graph material answers GetTag with its fallback sub-shader's tags ("FallbackError") and
        /// HDMaterial.ValidateMaterial, which identifies a graph by its ShaderGraphTargetId tag, refuses it. One render of a
        /// hidden 8 x 8 camera creates the instance, as ForestCapture's first render does.
        /// </summary>
        public static void EnsureRenderPipeline()
        {
            if (RenderPipelineManager.currentPipeline != null)
                return;
            var holder = new GameObject("ForestWindPipelineProbe") { hideFlags = HideFlags.HideAndDontSave };
            var target = new RenderTexture(8, 8, 16);
            try
            {
                var camera = holder.AddComponent<Camera>();
                camera.enabled = false;
                camera.targetTexture = target;
                camera.Render();
                camera.targetTexture = null;
            }
            finally
            {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(holder);
            }
            if (RenderPipelineManager.currentPipeline == null)
                Debug.LogWarning("Forest wind: no render pipeline instance after a camera render; Shader Graph materials may not validate.");
        }

        /// <summary>The imported shader, or null when the graph has not been imported.</summary>
        public static Shader Load(bool triplanar) => AssetDatabase.LoadAssetAtPath<Shader>(triplanar ? TriplanarGraphPath : GraphPath);

        public static bool IsWindShader(Shader shader) => shader != null && (shader.name == ShaderName || shader.name == TriplanarShaderName);

        // ------------------------------------------------------------------------------------------------
        // Checks (section 3.7, layers 1 to 3)
        // ------------------------------------------------------------------------------------------------

        static readonly string[] CheckedPasses = { "GBuffer", "DepthOnly", "ShadowCaster", "MotionVectors" };
        static readonly string[] FoliageKeywords = { "_ALPHATEST_ON", "_DOUBLESIDED_ON", "_MATERIAL_FEATURE_TRANSMISSION" };
        const string DotsKeyword = "DOTS_INSTANCING_ON";
        static readonly string[] MotionVectorExtras = { "WRITE_NORMAL_BUFFER", "WRITE_DECAL_BUFFER_AND_RENDERING_LAYER" };

        /// <summary>Problems of section 3.7 layers 1 to 3; empty = good. Saves each compiled variant's output under -forestMslOut (default Temp/ForestWind/msl).</summary>
        public static List<string> CheckShaders()
        {
            var problems = new List<string>();
            var paths = new List<string> { GraphPath, TriplanarGraphPath };
            foreach (string path in paths)
                if (!File.Exists(ForestManifest.ProjectDiskPath(path)))
                    problems.Add($"{path} does not exist (Gamex/Forest/Wind/Write Shader Graphs)");
            if (problems.Count > 0)
                return problems;

            // Layer 1: a synchronous import, with every error logged during it caught.
            foreach (string error in ImportCatchingErrors(paths))
                problems.Add("import: " + error);

            EnsureRenderPipeline();
            string mslRoot = ForestArgs.Value("-forestMslOut", "Temp/ForestWind/msl");
            int compiled = 0;
            foreach (bool triplanar in new[] { false, true })
            {
                string path = triplanar ? TriplanarGraphPath : GraphPath;
                string name = triplanar ? TriplanarShaderName : ShaderName;
                Shader shader = Load(triplanar);
                if (shader == null)
                {
                    problems.Add($"{path} did not import as a shader");
                    continue;
                }
                foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
                    if (message.severity == ShaderCompilerMessageSeverity.Error)
                        problems.Add($"{name}: {message.message} ({message.file}:{message.line}, {message.platform})");

                // Layer 2: not the error shader, not hidden.
                if (shader.name != name)
                    problems.Add($"{path} imported as '{shader.name}', not '{name}' (the error shader or a hidden template)");
                if (Shader.Find(name) == null)
                    problems.Add($"Shader.Find(\"{name}\") finds nothing: the importer hid the shader");
                if (ShaderUtil.ShaderHasError(shader))
                    problems.Add($"{name}: ShaderUtil.ShaderHasError is true");
                var material = new Material(shader);
                try
                {
                    string target = material.GetTag("ShaderGraphTargetId", false, "");
                    string declared = shader.FindSubshaderTagValue(0, new ShaderTagId("ShaderGraphTargetId")).name;
                    if (target != "HDLitSubTarget" || declared != "HDLitSubTarget")
                        problems.Add($"{name}: ShaderGraphTargetId is '{target}' on a material and '{declared}' on sub-shader 0, not HDLitSubTarget");
                    if (!HDMaterial.ValidateMaterial(material))
                        problems.Add($"{name}: HDMaterial.ValidateMaterial returned false");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }
                foreach (string property in SurfaceProperties.Concat(WindProperties).Concat(triplanar ? new[] { "_TexWorldScale" } : Array.Empty<string>()))
                    if (shader.FindPropertyIndex(property) < 0)
                        problems.Add($"{name}: property {property} is missing");

                // Layer 3: an explicit Metal compile of every pass the forest draws, with keyword sets built from what each pass declares.
                compiled += CompileVariants(shader, Path.GetFileNameWithoutExtension(path), Path.Combine(mslRoot, Path.GetFileNameWithoutExtension(path)), problems);
            }
            Debug.Log($"Forest wind: shader check compiled {compiled} Metal variant(s); {problems.Count} problem(s).");
            return problems;
        }

        [MenuItem("Gamex/Forest/Wind/Check Shaders")]
        public static void CheckShadersFromMenu()
        {
            List<string> problems = CheckShaders();
            if (problems.Count == 0)
            {
                Debug.Log("Forest wind: both wind shaders import, validate and compile for Metal.");
                return;
            }
            string text = $"Forest wind: {problems.Count} shader problem(s):\n  " + string.Join("\n  ", problems);
            if (Application.isBatchMode)
                throw new InvalidOperationException(text);
            Debug.LogError(text);
        }

        static int CompileVariants(Shader shader, string graphName, string outFolder, List<string> problems)
        {
            ShaderData data = ShaderUtil.GetShaderData(shader);
            if (data == null || data.SubshaderCount < 1)
            {
                problems.Add($"{shader.name}: no shader data / sub-shader");
                return 0;
            }
            ShaderData.Subshader sub = data.GetSubshader(0);
            Directory.CreateDirectory(outFolder);
            int compiled = 0;
            foreach (string passName in CheckedPasses)
            {
                int passIndex = -1;
                for (int i = 0; i < sub.PassCount; i++)
                    if (sub.GetPass(i).Name == passName) passIndex = i;
                if (passIndex < 0)
                {
                    problems.Add($"{shader.name}: pass {passName} is missing");
                    continue;
                }
                ShaderData.Pass pass = sub.GetPass(passIndex);
                var id = new PassIdentifier(0, (uint)passIndex);
                foreach (ShaderType stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                {
                    var declared = new HashSet<string>(ShaderUtil.GetPassKeywords(shader, id, stage).Select(k => k.name));
                    foreach ((string setName, string[] keywords) in KeywordSets(passName, declared))
                    {
                        ShaderData.VariantCompileInfo info = pass.CompileVariant(stage, keywords, ShaderCompilerPlatform.Metal, BuildTarget.StandaloneOSX, true);
                        compiled++;
                        if (!info.Success)
                        {
                            string messages = string.Join(" | ", (info.Messages ?? Array.Empty<ShaderMessage>())
                                .Where(m => m.severity == ShaderCompilerMessageSeverity.Error).Select(m => $"{m.message} ({m.file}:{m.line})"));
                            problems.Add($"{shader.name} {passName} {stage} [{string.Join(" ", keywords)}]: compile failed: {messages}");
                        }
                        string file = Path.Combine(outFolder, $"{passName}_{stage}_{setName}.txt");
                        File.WriteAllBytes(file, info.ShaderData ?? Array.Empty<byte>());
                    }
                }
            }
            return compiled;
        }

        static IEnumerable<(string, string[])> KeywordSets(string passName, HashSet<string> declared)
        {
            var bases = new List<(string, List<string>)>
            {
                ("foliage", FoliageKeywords.Where(declared.Contains).ToList()),
                ("opaque", new List<string>()),
            };
            var extras = new List<(string, string)> { ("", null) };
            if (passName == "MotionVectors")
            {
                if (declared.Contains(MotionVectorExtras[0])) extras.Add(("_normal", MotionVectorExtras[0]));
                if (declared.Contains(MotionVectorExtras[1])) extras.Add(("_decal", MotionVectorExtras[1]));
            }
            foreach ((string baseName, List<string> keywords) in bases)
                foreach (bool dots in declared.Contains(DotsKeyword) ? new[] { false, true } : new[] { false })
                    foreach ((string extraName, string extra) in extras)
                    {
                        var set = new List<string>(keywords);
                        if (dots) set.Add(DotsKeyword);
                        if (extra != null) set.Add(extra);
                        yield return (baseName + (dots ? "_dots" : "") + extraName, set.ToArray());
                    }
        }

        static List<string> ImportCatchingErrors(List<string> paths)
        {
            var errors = new List<string>();
            void OnLog(string condition, string stackTrace, LogType type)
            {
                bool error = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
                if (error || condition.Contains("Shader Graph at") || condition.Contains("Shader error in"))
                    errors.Add(condition.Split('\n')[0]);
            }
            Application.logMessageReceived += OnLog;
            try
            {
                foreach (string path in paths)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }
            return errors;
        }

        static bool WriteIfDifferent(string assetPath, string content)
        {
            string disk = ForestManifest.ProjectDiskPath(assetPath);
            if (File.Exists(disk) && File.ReadAllText(disk) == content)
                return false;
            Directory.CreateDirectory(Path.GetDirectoryName(disk));
            File.WriteAllText(disk, content, new UTF8Encoding(false));
            return true;
        }

        static string Md5Hex(string text)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(text));
                var builder = new StringBuilder(32);
                foreach (byte b in hash) builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        // ------------------------------------------------------------------------------------------------
        // The graph template
        // ------------------------------------------------------------------------------------------------

        /// <summary>A JSON object whose keys keep their order.</summary>
        sealed class J : List<KeyValuePair<string, object>>
        {
            public J Add(string key, object value)
            {
                Add(new KeyValuePair<string, object>(key, value));
                return this;
            }
        }

        static J Ref(string id) => new J().Add("m_Id", id);

        static object[] Refs(IEnumerable<string> ids) => ids.Select(id => (object)Ref(id)).ToArray();

        static J V(params float[] xyzw)
        {
            var names = new[] { "x", "y", "z", "w" };
            var value = new J();
            for (int i = 0; i < xyzw.Length; i++) value.Add(names[i], xyzw[i]);
            return value;
        }

        static J Rgba(float r, float g, float b, float a) => new J().Add("r", r).Add("g", g).Add("b", b).Add("a", a);

        static void WriteJson(StringBuilder text, object value, int indent)
        {
            switch (value)
            {
                case null: text.Append("null"); break;
                case string s: WriteString(text, s); break;
                case bool b: text.Append(b ? "true" : "false"); break;
                case int i: text.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: text.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case float f: text.Append(FloatText(f)); break;
                case J obj:
                    if (obj.Count == 0) { text.Append("{}"); break; }
                    text.Append("{\n");
                    for (int k = 0; k < obj.Count; k++)
                    {
                        text.Append(' ', 4 * (indent + 1));
                        WriteString(text, obj[k].Key);
                        text.Append(": ");
                        WriteJson(text, obj[k].Value, indent + 1);
                        text.Append(k + 1 < obj.Count ? ",\n" : "\n");
                    }
                    text.Append(' ', 4 * indent).Append('}');
                    break;
                case object[] array:
                    if (array.Length == 0) { text.Append("[]"); break; }
                    text.Append("[\n");
                    for (int k = 0; k < array.Length; k++)
                    {
                        text.Append(' ', 4 * (indent + 1));
                        WriteJson(text, array[k], indent + 1);
                        text.Append(k + 1 < array.Length ? ",\n" : "\n");
                    }
                    text.Append(' ', 4 * indent).Append(']');
                    break;
                default: throw new ArgumentException($"Cannot write {value.GetType()} as JSON.");
            }
        }

        static string FloatText(float f)
        {
            string s = f.ToString("R", CultureInfo.InvariantCulture);
            return s.IndexOf('.') >= 0 || s.IndexOf('E') >= 0 ? s : s + ".0";
        }

        static void WriteString(StringBuilder text, string s)
        {
            text.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    default: text.Append(c); break;
                }
            }
            text.Append('"');
        }

        const int StageVertex = 1, StageFragment = 2, StageAll = 3;
        const int SlotIn = 0, SlotOut = 1;

        /// <summary>Builds one graph: every object in a fixed order, ids from names.</summary>
        sealed class GraphWriter
        {
            readonly bool triplanar;
            readonly string graphKey;
            readonly List<J> objects = new();
            readonly List<string> properties = new(), nodes = new(), vertexBlocks = new(), fragmentBlocks = new();
            readonly List<object> edges = new();
            readonly Dictionary<string, (string node, int slot)> propertyOutputs = new();
            int column, row;

            public GraphWriter(bool triplanar)
            {
                this.triplanar = triplanar;
                graphKey = triplanar ? "ForestWindTriplanar" : "ForestWind";
            }

            string Id(string key) => Md5Hex("gamex-w1-graph/" + graphKey + "/" + key).Substring(0, 32);

            string GuidText(string key)
            {
                string h = Md5Hex("gamex-w1-property/" + graphKey + "/" + key);
                return $"{h.Substring(0, 8)}-{h.Substring(8, 4)}-{h.Substring(12, 4)}-{h.Substring(16, 4)}-{h.Substring(20, 12)}";
            }

            public string Write()
            {
                BuildProperties();
                BuildVertex();
                BuildFragment();

                string graphId = Id("graph"), targetId = Id("target");
                string categoryId = Id("category");
                var graph = new J()
                    .Add("m_SGVersion", 3)
                    .Add("m_Type", "UnityEditor.ShaderGraph.GraphData")
                    .Add("m_ObjectId", graphId)
                    .Add("m_Properties", Refs(properties))
                    .Add("m_Keywords", new object[0])
                    .Add("m_Dropdowns", new object[0])
                    .Add("m_CategoryData", Refs(new[] { categoryId }))
                    .Add("m_Nodes", Refs(nodes))
                    .Add("m_GroupDatas", new object[0])
                    .Add("m_StickyNoteDatas", new object[0])
                    .Add("m_Edges", edges.ToArray())
                    .Add("m_VertexContext", new J().Add("m_Position", V(1600f, 0f)).Add("m_Blocks", Refs(vertexBlocks)))
                    .Add("m_FragmentContext", new J().Add("m_Position", V(1600f, 600f)).Add("m_Blocks", Refs(fragmentBlocks)))
                    .Add("m_PreviewData", new J()
                        .Add("serializedMesh", new J().Add("m_SerializedMesh", "{\"mesh\":{\"instanceID\":0}}").Add("m_Guid", ""))
                        .Add("preventRotation", false))
                    .Add("m_Path", "Gamex")
                    .Add("m_GraphPrecision", 1)
                    .Add("m_PreviewMode", 2)
                    .Add("m_OutputNode", Ref(""))
                    .Add("m_SubDatas", new object[0])
                    .Add("m_ActiveTargets", Refs(new[] { targetId }));

                objects.Add(new J()
                    .Add("m_SGVersion", 0)
                    .Add("m_Type", "UnityEditor.ShaderGraph.CategoryData")
                    .Add("m_ObjectId", categoryId)
                    .Add("m_Name", "")
                    .Add("m_ChildObjectList", Refs(properties)));
                BuildTarget(targetId);

                var text = new StringBuilder();
                WriteJson(text, graph, 0);
                foreach (J obj in objects)
                {
                    text.Append("\n\n");
                    WriteJson(text, obj, 0);
                }
                text.Append('\n');
                return text.ToString();
            }

            // ---- properties (section 3.3) ----

            void BuildProperties()
            {
                Texture("_BaseColorMap", "Base Color Map", 0, useTilingAndOffset: true);
                Colour("_BaseColor", "Base Color");
                Texture("_NormalMap", "Normal Map", 3, useTilingAndOffset: false);
                Float("_NormalScale", "Normal Scale", 1f);
                Texture("_MaskMap", "Mask Map", 0, useTilingAndOffset: false);
                Float("_MetallicRemapMin", "Metallic Remap Min", 0f);
                Float("_MetallicRemapMax", "Metallic Remap Max", 1f);
                Float("_AORemapMin", "AO Remap Min", 0f);
                Float("_AORemapMax", "AO Remap Max", 1f);
                Float("_SmoothnessRemapMin", "Smoothness Remap Min", 0f);
                Float("_SmoothnessRemapMax", "Smoothness Remap Max", 1f);
                Float("_AlphaCutoff", "Alpha Cutoff", 0.5f);
                Float("_Thickness", "Thickness", 1f);
                if (triplanar) Float("_TexWorldScale", "Tex World Scale", 1f);
                Vector4("_GamexSway", "Gamex Sway");
                Vector4("_GamexFlutter", "Gamex Flutter");
                Vector4("_GamexMotion", "Gamex Motion");
                Vector4("_GamexPush", "Gamex Push");
                Vector4("_GamexStem", "Gamex Stem");
            }

            J PropertyHeader(int version, string type, string reference, string display)
            {
                string id = Id("property/" + reference);
                properties.Add(id);
                return new J()
                    .Add("m_SGVersion", version)
                    .Add("m_Type", "UnityEditor.ShaderGraph.Internal." + type)
                    .Add("m_ObjectId", id)
                    .Add("m_Guid", new J().Add("m_GuidSerialized", GuidText(reference)))
                    .Add("m_Name", display)
                    .Add("m_DefaultRefNameVersion", 1)
                    .Add("m_RefNameGeneratedByDisplayName", display)
                    .Add("m_DefaultReferenceName", "_" + display.Replace(' ', '_'))
                    .Add("m_OverrideReferenceName", reference)
                    .Add("m_GeneratePropertyBlock", true)
                    .Add("m_UseCustomSlotLabel", false)
                    .Add("m_CustomSlotLabel", "")
                    .Add("m_DismissedVersion", 0)
                    .Add("m_Precision", 0)
                    .Add("overrideHLSLDeclaration", false)
                    .Add("hlslDeclarationOverride", 0)
                    .Add("m_Hidden", false);
            }

            void Texture(string reference, string display, int defaultType, bool useTilingAndOffset)
            {
                objects.Add(PropertyHeader(0, "Texture2DShaderProperty", reference, display)
                    .Add("m_Value", new J().Add("m_SerializedTexture", "").Add("m_Guid", ""))
                    .Add("isMainTexture", false)
                    .Add("useTilingAndOffset", useTilingAndOffset)
                    .Add("useTexelSize", false)
                    .Add("m_Modifiable", true)
                    .Add("m_DefaultType", defaultType));
            }

            void Colour(string reference, string display)
            {
                objects.Add(PropertyHeader(3, "ColorShaderProperty", reference, display)
                    .Add("m_Value", Rgba(1f, 1f, 1f, 1f))
                    .Add("isMainColor", false)
                    .Add("m_ColorMode", 0));
            }

            void Float(string reference, string display, float value)
            {
                objects.Add(PropertyHeader(1, "Vector1ShaderProperty", reference, display)
                    .Add("m_Value", value)
                    .Add("m_FloatType", 0)
                    .Add("m_RangeValues", V(0f, 1f)));
            }

            void Vector4(string reference, string display)
            {
                objects.Add(PropertyHeader(1, "Vector4ShaderProperty", reference, display)
                    .Add("m_Value", V(0f, 0f, 0f, 0f)));
            }

            // ---- slots ----

            J SlotHeader(string type, string id, int slotId, string display, int slotType, string shaderName, int stage) =>
                new J()
                    .Add("m_SGVersion", 0)
                    .Add("m_Type", "UnityEditor.ShaderGraph." + type)
                    .Add("m_ObjectId", id)
                    .Add("m_Id", slotId)
                    .Add("m_DisplayName", display)
                    .Add("m_SlotType", slotType)
                    .Add("m_Hidden", false)
                    .Add("m_ShaderOutputName", shaderName)
                    .Add("m_StageCapability", stage);

            J VectorSlot(string id, int dimension, int slotId, string display, int slotType, string shaderName, int stage, params float[] value)
            {
                string type = dimension == 1 ? "Vector1MaterialSlot" : $"Vector{dimension}MaterialSlot";
                J slot = SlotHeader(type, id, slotId, display, slotType, shaderName, stage);
                if (dimension == 1)
                {
                    float v = value.Length > 0 ? value[0] : 0f;
                    slot.Add("m_Value", v).Add("m_DefaultValue", v);
                }
                else
                {
                    var zeros = new float[dimension];
                    for (int i = 0; i < dimension && i < value.Length; i++) zeros[i] = value[i];
                    slot.Add("m_Value", V(zeros)).Add("m_DefaultValue", V(zeros));
                }
                return slot.Add("m_Labels", new object[0]);
            }

            // ---- nodes ----

            string Node(string key, string type, int version, string name, List<J> slots, string[] synonyms, int precision, Action<J> extra)
            {
                string id = Id("node/" + key);
                var slotRefs = new List<object>();
                foreach (J slot in slots)
                {
                    objects.Add(slot);
                    slotRefs.Add(Ref((string)slot.First(kv => kv.Key == "m_ObjectId").Value));
                }
                var node = new J()
                    .Add("m_SGVersion", version)
                    .Add("m_Type", "UnityEditor.ShaderGraph." + type)
                    .Add("m_ObjectId", id)
                    .Add("m_Group", Ref(""))
                    .Add("m_Name", name)
                    .Add("m_DrawState", new J()
                        .Add("m_Expanded", true)
                        .Add("m_Position", new J()
                            .Add("serializedVersion", "2")
                            .Add("x", -1200f + 400f * column)
                            .Add("y", 120f * row++)
                            .Add("width", 200f)
                            .Add("height", 100f)))
                    .Add("m_Slots", slotRefs.ToArray())
                    .Add("synonyms", synonyms.Cast<object>().ToArray())
                    .Add("m_Precision", precision)
                    .Add("m_PreviewExpanded", false)
                    .Add("m_DismissedVersion", 0)
                    .Add("m_PreviewMode", 0)
                    .Add("m_CustomColors", new J().Add("m_SerializableColors", new object[0]));
                extra?.Invoke(node);
                objects.Add(node);
                nodes.Add(id);
                return id;
            }

            string SlotId(string node, string slot) => Id("slot/" + node + "/" + slot);

            void Edge(string fromNode, int fromSlot, string toNode, int toSlot)
            {
                edges.Add(new J()
                    .Add("m_OutputSlot", new J().Add("m_Node", Ref(fromNode)).Add("m_SlotId", fromSlot))
                    .Add("m_InputSlot", new J().Add("m_Node", Ref(toNode)).Add("m_SlotId", toSlot)));
            }

            string Block(string descriptor, J slot, bool vertex)
            {
                string id = Id("block/" + descriptor);
                objects.Add(slot);
                string name = descriptor.IndexOf('#') >= 0 ? "VertexDescription.CustomInterpolator" : descriptor;
                objects.Add(new J()
                    .Add("m_SGVersion", 0)
                    .Add("m_Type", "UnityEditor.ShaderGraph.BlockNode")
                    .Add("m_ObjectId", id)
                    .Add("m_Group", Ref(""))
                    .Add("m_Name", name)
                    .Add("m_DrawState", new J()
                        .Add("m_Expanded", true)
                        .Add("m_Position", new J().Add("serializedVersion", "2").Add("x", 0f).Add("y", 0f).Add("width", 0f).Add("height", 0f)))
                    .Add("m_Slots", new object[] { Ref((string)slot.First(kv => kv.Key == "m_ObjectId").Value) })
                    .Add("synonyms", new object[0])
                    .Add("m_Precision", 0)
                    .Add("m_PreviewExpanded", true)
                    .Add("m_DismissedVersion", 0)
                    .Add("m_PreviewMode", 0)
                    .Add("m_CustomColors", new J().Add("m_SerializableColors", new object[0]))
                    .Add("m_SerializedDescriptor", descriptor));
                (vertex ? vertexBlocks : fragmentBlocks).Add(id);
                // Block nodes are graph nodes too: without the entry the block has no owner graph (as in Fern.shadergraph).
                nodes.Add(id);
                return id;
            }

            J SpaceSlot(string type, string id, string display, string shaderName, int stage, int space, bool labels)
            {
                return SlotHeader(type, id, 0, display, SlotIn, shaderName, stage)
                    .Add("m_Value", V(0f, 0f, 0f)).Add("m_DefaultValue", V(0f, 0f, 0f))
                    .Add("m_Labels", labels ? new object[] { "X", "Y", "Z" } : new object[0])
                    .Add("m_Space", space);
            }

            /// <summary>A property node in a stage; returns its node id (output slot 0).</summary>
            string PropertyNode(string reference, string stageKey)
            {
                string key = "property/" + stageKey + "/" + reference;
                string slotId = SlotId(key, "out");
                J slot;
                string display = reference;
                if (reference.EndsWith("Map", StringComparison.Ordinal))
                    slot = SlotHeader("Texture2DMaterialSlot", slotId, 0, display, SlotOut, "Out", StageAll).Add("m_BareResource", false);
                else if (reference == "_BaseColor" || reference.StartsWith("_Gamex", StringComparison.Ordinal))
                    slot = VectorSlot(slotId, 4, 0, display, SlotOut, "Out", StageAll);
                else
                    slot = VectorSlot(slotId, 1, 0, display, SlotOut, "Out", StageAll);
                return Node(key, "PropertyNode", 0, "Property", new List<J> { slot }, Array.Empty<string>(), 0,
                    node => node.Add("m_Property", Ref(Id("property/" + reference))));
            }

            string Vector3Input(string key, string type, string name, int space, int version, string[] synonyms, bool position)
            {
                var slot = VectorSlot(SlotId(key, "out"), 3, 0, "Out", SlotOut, "Out", StageAll);
                return Node(key, type, version, name, new List<J> { slot }, synonyms, 0, node =>
                {
                    node.Add("m_Space", space);
                    if (position) node.Add("m_PositionSource", 0);
                });
            }

            /// <summary>A File-mode Custom Function node (modelled on PseudoSubsurface.shadersubgraph). Inputs first, then outputs; slot ids in that order.</summary>
            string CustomFunction(string key, string function, string sourceGuid, (string name, int dim)[] inputs, (string name, int dim)[] outputs)
            {
                var slots = new List<J>();
                int slotIndex = 0;
                foreach ((string name, int dim) in inputs)
                {
                    string sid = SlotId(key, "in/" + name);
                    slots.Add(dim == 0
                        ? SlotHeader("Texture2DInputMaterialSlot", sid, slotIndex, name, SlotIn, name, StageAll)
                            .Add("m_BareResource", false)
                            .Add("m_Texture", new J().Add("m_SerializedTexture", "").Add("m_Guid", ""))
                            .Add("m_DefaultType", 0)
                        : VectorSlot(sid, dim, slotIndex, name, SlotIn, name, StageAll));
                    slotIndex++;
                }
                foreach ((string name, int dim) in outputs)
                {
                    slots.Add(VectorSlot(SlotId(key, "out/" + name), dim, slotIndex, name, SlotOut, name, StageAll));
                    slotIndex++;
                }
                return Node(key, "CustomFunctionNode", 1, function + " (Custom Function)", slots, new[] { "code", "HLSL" }, 1, node => node
                    .Add("m_SourceType", 0)
                    .Add("m_FunctionName", function)
                    .Add("m_FunctionSource", sourceGuid)
                    .Add("m_FunctionBody", "Enter function body here..."));
            }

            // ---- vertex stage ----

            void BuildVertex()
            {
                column = 0; row = 0;
                string posOS = Vector3Input("vertex/positionOS", "PositionNode", "Position", 0, 1, new[] { "location" }, true);
                string posAWS = Vector3Input("vertex/positionAWS", "PositionNode", "Position", 4, 1, new[] { "location" }, true);
                string normalOS = Vector3Input("vertex/normalOS", "NormalVectorNode", "Normal Vector", 0, 0, new[] { "surface direction" }, false);
                string tangentOS = Vector3Input("vertex/tangentOS", "TangentVectorNode", "Tangent Vector", 0, 0, Array.Empty<string>(), false);
                string colour = Node("vertex/colour", "VertexColorNode", 0, "Vertex Color",
                    new List<J> { VectorSlot(SlotId("vertex/colour", "out"), 4, 0, "Out", SlotOut, "Out", StageAll, 1f, 1f, 1f, 1f) },
                    Array.Empty<string>(), 0, null);
                string[] timeOutputs = { "Time", "Sine Time", "Cosine Time", "Delta Time", "Smooth Delta" };
                var timeSlots = new List<J>();
                for (int i = 0; i < timeOutputs.Length; i++)
                    timeSlots.Add(VectorSlot(SlotId("vertex/time", timeOutputs[i]), 1, i, timeOutputs[i], SlotOut, timeOutputs[i], StageAll));
                string time = Node("vertex/time", "TimeNode", 0, "Time", timeSlots, Array.Empty<string>(), 0, null);
                string[] objectOutputs = { "Position", "Scale", "World Bounds Min", "World Bounds Max", "Bounds Size" };
                var objectSlots = new List<J>();
                for (int i = 0; i < objectOutputs.Length; i++)
                    objectSlots.Add(VectorSlot(SlotId("vertex/object", objectOutputs[i]), 3, i, objectOutputs[i], SlotOut, objectOutputs[i], StageAll));
                string obj = Node("vertex/object", "ObjectNode", 0, "Object", objectSlots, new[] { "position", "scale", "bounds", "size" }, 0, null);
                var cameraSlots = new List<J>
                {
                    VectorSlot(SlotId("vertex/camera", "Position"), 3, 0, "Position", SlotOut, "Position", StageAll),
                    VectorSlot(SlotId("vertex/camera", "Direction"), 3, 1, "Direction", SlotOut, "Direction", StageAll),
                    VectorSlot(SlotId("vertex/camera", "Orthographic"), 1, 2, "Orthographic", SlotOut, "Orthographic", StageAll),
                    VectorSlot(SlotId("vertex/camera", "Near Plane"), 1, 3, "Near Plane", SlotOut, "Near Plane", StageAll),
                    VectorSlot(SlotId("vertex/camera", "Far Plane"), 1, 4, "Far Plane", SlotOut, "Far Plane", StageAll, 1f),
                    VectorSlot(SlotId("vertex/camera", "Z Buffer Sign"), 1, 5, "Z Buffer Sign", SlotOut, "Z Buffer Sign", StageAll, 1f),
                    VectorSlot(SlotId("vertex/camera", "Width"), 1, 6, "Width", SlotOut, "Width", StageAll, 1f),
                    VectorSlot(SlotId("vertex/camera", "Height"), 1, 7, "Height", SlotOut, "Height", StageAll, 1f),
                };
                string camera = Node("vertex/camera", "CameraNode", 0, "Camera", cameraSlots,
                    new[] { "position", "direction", "orthographic", "near plane", "far plane", "width", "height" }, 0, null);
                var windProps = new string[WindProperties.Length];
                for (int i = 0; i < WindProperties.Length; i++) windProps[i] = PropertyNode(WindProperties[i], "vertex");
                // W1 R2: UV2, a fern frond's or grass blade's anchor (its base's offset and its azimuth); trees have none and read 0.
                string anchor = Node("vertex/uv2", "UVNode", 0, "UV",
                    new List<J> { VectorSlot(SlotId("vertex/uv2", "out"), 4, 0, "Out", SlotOut, "Out", StageAll) },
                    new[] { "texcoords", "coords", "coordinates" }, 0, node => node.Add("m_OutputChannel", 2));

                column = 1; row = 0;
                (string, int)[] inputs =
                {
                    ("PositionOS", 3), ("NormalOS", 3), ("TangentOS", 3), ("VertexColor", 4), ("PositionAWS", 3), ("PivotAWS", 3),
                    ("ObjectScale", 3), ("CameraAWS", 3), ("Time", 1), ("Sway", 4), ("Flutter", 4), ("Motion", 4), ("Push", 4), ("Stem", 4),
                    ("Anchor", 4),
                };
                (string, int)[] outputs = { ("OutPositionOS", 3), ("OutNormalOS", 3), ("OutTangentOS", 3), ("OutVelocityOS", 3) };
                string wind = CustomFunction("vertex/wind", "ForestWindVertex", FixedGuid(Path.GetFileName(VertexIncludePath)), inputs, outputs);
                Edge(posOS, 0, wind, 0);
                Edge(normalOS, 0, wind, 1);
                Edge(tangentOS, 0, wind, 2);
                Edge(colour, 0, wind, 3);
                Edge(posAWS, 0, wind, 4);
                Edge(obj, 0, wind, 5);
                Edge(obj, 1, wind, 6);
                Edge(camera, 0, wind, 7);
                Edge(time, 0, wind, 8);
                for (int i = 0; i < windProps.Length; i++) Edge(windProps[i], 0, wind, 9 + i);
                Edge(anchor, 0, wind, 9 + windProps.Length);
                int firstOutput = inputs.Length;

                string position = Block("VertexDescription.Position",
                    SpaceSlot("PositionMaterialSlot", Id("blockslot/position"), "Position", "Position", StageVertex, 0, true), true);
                string normal = Block("VertexDescription.Normal",
                    SpaceSlot("NormalMaterialSlot", Id("blockslot/normal"), "Normal", "Normal", StageVertex, 0, true), true);
                string tangent = Block("VertexDescription.Tangent",
                    SpaceSlot("TangentMaterialSlot", Id("blockslot/tangent"), "Tangent", "Tangent", StageVertex, 0, true), true);
                string velocity = Block("VertexDescription.CustomVelocity",
                    VectorSlot(Id("blockslot/velocity"), 3, 0, "Velocity", SlotIn, "CustomVelocity", StageVertex), true);
                Edge(wind, firstOutput, position, 0);
                Edge(wind, firstOutput + 1, normal, 0);
                Edge(wind, firstOutput + 2, tangent, 0);
                Edge(wind, firstOutput + 3, velocity, 0);

                if (triplanar)
                {
                    // The rest position and rest normal, built from the vertex attributes before the graph's outputs replace them.
                    string normalWS = Vector3Input("vertex/normalWS", "NormalVectorNode", "Normal Vector", 2, 0, new[] { "surface direction" }, false);
                    string restPosition = Block("VertexDescription.RestPositionAWS#3",
                        VectorSlot(Id("blockslot/restPosition"), 3, 0, "RestPositionAWS", SlotIn, "RestPositionAWS", StageVertex), true);
                    string restNormal = Block("VertexDescription.RestNormalWS#3",
                        VectorSlot(Id("blockslot/restNormal"), 3, 0, "RestNormalWS", SlotIn, "RestNormalWS", StageVertex), true);
                    Edge(posAWS, 0, restPosition, 0);
                    Edge(normalWS, 0, restNormal, 0);
                }
            }

            // ---- fragment stage ----

            void BuildFragment()
            {
                column = 2; row = 0;
                string baseMap = PropertyNode("_BaseColorMap", "fragment");
                string normalMap = PropertyNode("_NormalMap", "fragment");
                string maskMap = PropertyNode("_MaskMap", "fragment");
                string baseColour = PropertyNode("_BaseColor", "fragment");
                string normalScale = PropertyNode("_NormalScale", "fragment");
                string[] remapNames = { "_MetallicRemapMin", "_MetallicRemapMax", "_AORemapMin", "_AORemapMax", "_SmoothnessRemapMin", "_SmoothnessRemapMax" };
                var remaps = remapNames.Select(name => PropertyNode(name, "fragment")).ToArray();
                string cutoff = PropertyNode("_AlphaCutoff", "fragment");
                string thickness = PropertyNode("_Thickness", "fragment");
                string normalWS = Vector3Input("fragment/normalWS", "NormalVectorNode", "Normal Vector", 2, 0, new[] { "surface direction" }, false);

                (string, int)[] outputs = { ("Albedo", 3), ("Alpha", 1), ("NormalOutWS", 3), ("Metallic", 1), ("AO", 1), ("Smoothness", 1) };
                string surface;
                int firstOutput;
                string surfaceGuid = FixedGuid(Path.GetFileName(SurfaceIncludePath));
                column = 3; row = 0;
                if (!triplanar)
                {
                    column = 2;
                    string uv = Node("fragment/uv0", "UVNode", 0, "UV",
                        new List<J> { VectorSlot(SlotId("fragment/uv0", "out"), 4, 0, "Out", SlotOut, "Out", StageAll) },
                        new[] { "texcoords", "coords", "coordinates" }, 0, node => node.Add("m_OutputChannel", 0));
                    string tangentWS = Vector3Input("fragment/tangentWS", "TangentVectorNode", "Tangent Vector", 2, 0, Array.Empty<string>(), false);
                    string bitangentWS = Vector3Input("fragment/bitangentWS", "BitangentVectorNode", "Bitangent Vector", 2, 0, new[] { "binormal" }, false);
                    column = 3; row = 0;
                    (string, int)[] inputs =
                    {
                        ("BaseColorMap", 0), ("NormalMap", 0), ("MaskMap", 0), ("UV", 2), ("NormalWS", 3), ("TangentWS", 3), ("BitangentWS", 3),
                        ("BaseColor", 4), ("NormalScale", 1), ("MetallicRemapMin", 1), ("MetallicRemapMax", 1), ("AORemapMin", 1), ("AORemapMax", 1),
                        ("SmoothnessRemapMin", 1), ("SmoothnessRemapMax", 1),
                    };
                    surface = CustomFunction("fragment/surface", "ForestSurfaceUV", surfaceGuid, inputs, outputs);
                    Edge(baseMap, 0, surface, 0);
                    Edge(normalMap, 0, surface, 1);
                    Edge(maskMap, 0, surface, 2);
                    Edge(uv, 0, surface, 3);
                    Edge(normalWS, 0, surface, 4);
                    Edge(tangentWS, 0, surface, 5);
                    Edge(bitangentWS, 0, surface, 6);
                    Edge(baseColour, 0, surface, 7);
                    Edge(normalScale, 0, surface, 8);
                    for (int i = 0; i < remaps.Length; i++) Edge(remaps[i], 0, surface, 9 + i);
                    firstOutput = inputs.Length;
                }
                else
                {
                    column = 2;
                    string restPosition = Node("fragment/restPosition", "CustomInterpolatorNode", 0, "RestPositionAWS (Custom Interpolator)",
                        new List<J> { VectorSlot(SlotId("fragment/restPosition", "out"), 3, 0, "Out", SlotOut, "Out", StageFragment) },
                        Array.Empty<string>(), 0, node => node.Add("customBlockNodeName", "RestPositionAWS").Add("serializedType", 3));
                    string restNormal = Node("fragment/restNormal", "CustomInterpolatorNode", 0, "RestNormalWS (Custom Interpolator)",
                        new List<J> { VectorSlot(SlotId("fragment/restNormal", "out"), 3, 0, "Out", SlotOut, "Out", StageFragment) },
                        Array.Empty<string>(), 0, node => node.Add("customBlockNodeName", "RestNormalWS").Add("serializedType", 3));
                    string worldScale = PropertyNode("_TexWorldScale", "fragment");
                    column = 3; row = 0;
                    (string, int)[] inputs =
                    {
                        ("BaseColorMap", 0), ("NormalMap", 0), ("MaskMap", 0), ("RestPositionAWS", 3), ("RestNormalWS", 3), ("NormalWS", 3),
                        ("TexWorldScale", 1), ("BaseColor", 4), ("NormalScale", 1), ("MetallicRemapMin", 1), ("MetallicRemapMax", 1),
                        ("AORemapMin", 1), ("AORemapMax", 1), ("SmoothnessRemapMin", 1), ("SmoothnessRemapMax", 1),
                    };
                    surface = CustomFunction("fragment/surface", "ForestSurfaceTriplanar", surfaceGuid, inputs, outputs);
                    Edge(baseMap, 0, surface, 0);
                    Edge(normalMap, 0, surface, 1);
                    Edge(maskMap, 0, surface, 2);
                    Edge(restPosition, 0, surface, 3);
                    Edge(restNormal, 0, surface, 4);
                    Edge(normalWS, 0, surface, 5);
                    Edge(worldScale, 0, surface, 6);
                    Edge(baseColour, 0, surface, 7);
                    Edge(normalScale, 0, surface, 8);
                    for (int i = 0; i < remaps.Length; i++) Edge(remaps[i], 0, surface, 9 + i);
                    firstOutput = inputs.Length;
                }

                string baseColorBlock = Block("SurfaceDescription.BaseColor",
                    SlotHeader("ColorRGBMaterialSlot", Id("blockslot/baseColor"), 0, "Base Color", SlotIn, "BaseColor", StageFragment)
                        .Add("m_Value", V(0.5f, 0.5f, 0.5f)).Add("m_DefaultValue", V(0.5f, 0.5f, 0.5f))
                        .Add("m_Labels", new object[0]).Add("m_ColorMode", 0).Add("m_DefaultColor", Rgba(0.5f, 0.5f, 0.5f, 1f)), false);
                string normalBlock = Block("SurfaceDescription.NormalWS",
                    SpaceSlot("NormalMaterialSlot", Id("blockslot/normalWS"), "Normal (World Space)", "NormalWS", StageFragment, 2, false), false);
                string metallicBlock = Block("SurfaceDescription.Metallic",
                    VectorSlot(Id("blockslot/metallic"), 1, 0, "Metallic", SlotIn, "Metallic", StageFragment, 0f), false);
                string smoothnessBlock = Block("SurfaceDescription.Smoothness",
                    VectorSlot(Id("blockslot/smoothness"), 1, 0, "Smoothness", SlotIn, "Smoothness", StageFragment, 0.5f), false);
                string occlusionBlock = Block("SurfaceDescription.Occlusion",
                    VectorSlot(Id("blockslot/occlusion"), 1, 0, "Ambient Occlusion", SlotIn, "Occlusion", StageFragment, 1f), false);
                string alphaBlock = Block("SurfaceDescription.Alpha",
                    VectorSlot(Id("blockslot/alpha"), 1, 0, "Alpha", SlotIn, "Alpha", StageFragment, 1f), false);
                string clipBlock = Block("SurfaceDescription.AlphaClipThreshold",
                    VectorSlot(Id("blockslot/alphaClip"), 1, 0, "Alpha Clip Threshold", SlotIn, "AlphaClipThreshold", StageFragment, 0.5f), false);
                string thicknessBlock = Block("SurfaceDescription.Thickness",
                    VectorSlot(Id("blockslot/thickness"), 1, 0, "Thickness", SlotIn, "Thickness", StageFragment, 1f), false);
                // Left unconnected: the block declares the hidden _DiffusionProfileHash and _DiffusionProfileAsset, so
                // HDMaterial.SetDiffusionProfile works as on HDRP/Lit.
                var profileSlot = new J()
                    .Add("m_SGVersion", 0)
                    .Add("m_Type", "UnityEditor.Rendering.HighDefinition.DiffusionProfileInputMaterialSlot")
                    .Add("m_ObjectId", Id("blockslot/diffusionProfile"))
                    .Add("m_Id", 0)
                    .Add("m_DisplayName", "Diffusion Profile")
                    .Add("m_SlotType", SlotIn)
                    .Add("m_Hidden", false)
                    .Add("m_ShaderOutputName", "DiffusionProfileHash")
                    .Add("m_StageCapability", StageFragment)
                    .Add("m_Value", 0f)
                    .Add("m_DefaultValue", 0f)
                    .Add("m_Labels", new object[0])
                    .Add("m_DiffusionProfile", new J().Add("selectedEntry", 0).Add("popupEntries", new object[0]))
                    .Add("m_SerializedDiffusionProfile", "")
                    .Add("m_Version", 1);
                Block("SurfaceDescription.DiffusionProfileHash", profileSlot, false);

                Edge(surface, firstOutput + 0, baseColorBlock, 0);
                Edge(surface, firstOutput + 1, alphaBlock, 0);
                Edge(surface, firstOutput + 2, normalBlock, 0);
                Edge(surface, firstOutput + 3, metallicBlock, 0);
                Edge(surface, firstOutput + 4, occlusionBlock, 0);
                Edge(surface, firstOutput + 5, smoothnessBlock, 0);
                Edge(cutoff, 0, clipBlock, 0);
                Edge(thickness, 0, thicknessBlock, 0);
            }

            // ---- target (section 3.2) ----

            void BuildTarget(string targetId)
            {
                string subTarget = Id("target/lit"), builtin = Id("target/builtin"), lighting = Id("target/lighting"),
                    system = Id("target/system"), lit = Id("target/litdata");
                objects.Add(new J()
                    .Add("m_SGVersion", 0)
                    .Add("m_Type", "UnityEditor.Rendering.HighDefinition.ShaderGraph.HDTarget")
                    .Add("m_ObjectId", targetId)
                    .Add("m_ActiveSubTarget", Ref(subTarget))
                    .Add("m_Datas", Refs(new[] { system, builtin, lighting, lit }))
                    .Add("m_CustomEditorGUI", "")
                    .Add("m_SupportVFX", false)
                    .Add("m_SupportLineRendering", false));
                objects.Add(new J()
                    .Add("m_SGVersion", 0)
                    .Add("m_Type", "UnityEditor.Rendering.HighDefinition.ShaderGraph.HDLitSubTarget")
                    .Add("m_ObjectId", subTarget));
                objects.Add(new J()
                    .Add("m_SGVersion", 0)
                    .Add("m_Type", "UnityEditor.Rendering.HighDefinition.ShaderGraph.SystemData")
                    .Add("m_ObjectId", system)
                    .Add("m_MaterialNeedsUpdateHash", 0)
                    .Add("m_SurfaceType", 0)
                    .Add("m_RenderingPass", 1)
                    .Add("m_BlendMode", 0)
                    .Add("m_ZTest", 4)
                    .Add("m_ZWrite", false)
                    .Add("m_TransparentCullMode", 2)
                    .Add("m_OpaqueCullMode", 2)
                    .Add("m_SortPriority", 0)
                    .Add("m_AlphaTest", true)
                    .Add("m_ExcludeFromTUAndAA", false)
                    .Add("m_TransparentDepthPrepass", false)
                    .Add("m_TransparentDepthPostpass", false)
                    .Add("m_SupportLodCrossFade", false)
                    .Add("m_DoubleSidedMode", 3)
                    .Add("m_DOTSInstancing", false)
                    .Add("m_CustomVelocity", true)
                    .Add("m_Tessellation", false)
                    .Add("m_TessellationMode", 0)
                    .Add("m_TessellationFactorMinDistance", 20f)
                    .Add("m_TessellationFactorMaxDistance", 50f)
                    .Add("m_TessellationFactorTriangleSize", 100f)
                    .Add("m_TessellationShapeFactor", 0.75f)
                    .Add("m_TessellationBackFaceCullEpsilon", -0.25f)
                    .Add("m_TessellationMaxDisplacement", 0.01f)
                    .Add("m_DebugSymbols", false)
                    .Add("m_Version", 2)
                    .Add("inspectorFoldoutMask", 9));
                objects.Add(new J()
                    .Add("m_SGVersion", 0)
                    .Add("m_Type", "UnityEditor.Rendering.HighDefinition.ShaderGraph.BuiltinData")
                    .Add("m_ObjectId", builtin)
                    .Add("m_Distortion", false)
                    .Add("m_DistortionMode", 0)
                    .Add("m_DistortionDepthTest", true)
                    .Add("m_AddPrecomputedVelocity", false)
                    .Add("m_TransparentWritesMotionVec", false)
                    .Add("m_DepthOffset", false)
                    .Add("m_ConservativeDepthOffset", false)
                    .Add("m_TransparencyFog", true)
                    .Add("m_AlphaTestShadow", false)
                    .Add("m_BackThenFrontRendering", false)
                    .Add("m_TransparentDepthPrepass", false)
                    .Add("m_TransparentDepthPostpass", false)
                    .Add("m_TransparentPerPixelSorting", false)
                    .Add("m_SupportLodCrossFade", false));
                objects.Add(new J()
                    .Add("m_SGVersion", 0)
                    .Add("m_Type", "UnityEditor.Rendering.HighDefinition.ShaderGraph.LightingData")
                    .Add("m_ObjectId", lighting)
                    .Add("m_NormalDropOffSpace", 2)
                    .Add("m_BlendPreserveSpecular", true)
                    .Add("m_ReceiveDecals", true)
                    .Add("m_ReceiveSSR", true)
                    .Add("m_ReceiveSSRTransparent", false)
                    .Add("m_SpecularAA", false)
                    .Add("m_SpecularOcclusionMode", 1)
                    .Add("m_OverrideBakedGI", false));
                objects.Add(new J()
                    .Add("m_SGVersion", 0)
                    .Add("m_Type", "UnityEditor.Rendering.HighDefinition.ShaderGraph.HDLitData")
                    .Add("m_ObjectId", lit)
                    .Add("m_RayTracing", false)
                    .Add("m_MaterialType", 0)
                    .Add("m_MaterialTypeMask", 34)
                    .Add("m_RefractionModel", 0)
                    .Add("m_SSSTransmission", true)
                    .Add("m_EnergyConservingSpecular", true)
                    .Add("m_ClearCoat", false));
            }
        }
    }
}
