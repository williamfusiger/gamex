using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Gamex.EditorTools
{
    /// <summary>
    /// The project-wide settings the GPU Resident Drawer needs (spec Appendix C.4; the owner's decision of 2026-10-15, step D1,
    /// build/issues/D1-optimisation.md). Batch only and no menu item, on purpose: every method that writes refuses to run
    /// without -forestAllowProjectSettings, logs the settings before and after, and first copies the four files it may touch to
    /// Library/GamexForest/ProjectSettingsBackup/&lt;UTC time&gt;/.
    /// <code>
    /// Unity -batchmode -projectPath . -executeMethod Gamex.EditorTools.ForestProjectSettings.EnableGpuResidentDrawer -forestAllowProjectSettings -quit
    /// Unity -batchmode -projectPath . -executeMethod Gamex.EditorTools.ForestProjectSettings.Revert -forestAllowProjectSettings -quit
    /// Unity -batchmode -projectPath . -executeMethod Gamex.EditorTools.ForestProjectSettings.Report -quit
    /// </code>
    /// EnableGpuResidentDrawer sets, as HDRP 17.6 asks (HD Documentation~/gpu-resident-drawer.md:19-22,
    /// HD Editor/RenderPipeline/HDRenderPipelineUI.cs:1631-1669):
    ///   the HDRP asset's GPU Resident Drawer to Instanced Drawing; GPU occlusion culling only with -forestGpuOcclusion
    ///   (D1 measured it as a loss in this forest: it culled nothing and cost 0.7 to 1 ms);
    ///   Graphics settings, BatchRendererGroup Variants to Keep All (without it the drawer refuses to start:
    ///   Core Runtime/GPUDriven/GPUResidentDrawer.Validator.cs:61-66);
    ///   Player settings, static batching off for the standalone targets (statically batched renderers stay off the drawer).
    /// Quality settings are not touched: LOD cross-fade is already on (the drawer reads it, HD HDRenderPipelineAsset.cs:250)
    /// and no quality level overrides the pipeline asset.
    /// Revert puts the values of the pre-D1 project back (drawer off, occlusion off, BatchRendererGroup Variants
    /// "Keep If Entities Graphics", static batching on). It is a revert of the values, not of the bytes: the exact pre-D1
    /// files are in build/backup/pre-D1 and Library/GamexForest/ProjectSettingsBackup.
    /// </summary>
    public static class ForestProjectSettings
    {
        const string HdrpAssetPath = "Assets/Game/Settings/HDRP.asset";
        const string ModePath = "m_RenderPipelineSettings.gpuResidentDrawerSettings.mode";
        const string OcclusionPath = "m_RenderPipelineSettings.gpuResidentDrawerSettings.enableOcclusionCullingInCameras";
        const string SmallMeshPath = "m_RenderPipelineSettings.gpuResidentDrawerSettings.smallMeshScreenPercentage";
        const string OccludersFromPrepassPath = "m_RenderPipelineSettings.gpuResidentDrawerSettings.useDepthPrepassForOccluders";
        const string GraphicsSettingsPath = "ProjectSettings/GraphicsSettings.asset";
        const string BrgStrippingPath = "m_BrgStripping";

        // GPUResidentDrawerMode lives in Unity.RenderPipelines.GPUDriven.Runtime, which Gamex.EditorTools does not reference:
        // Disabled = 0, InstancedDrawing = 1 (Core Runtime/GPUDriven/IGPUResidentRenderPipeline.cs:9-20).
        const int ModeDisabled = 0, ModeInstancedDrawing = 1;

        static readonly BuildTarget[] StandaloneTargets = { BuildTarget.StandaloneOSX, BuildTarget.StandaloneWindows64, BuildTarget.StandaloneLinux64 };
        static readonly string[] TouchedFiles =
        {
            HdrpAssetPath, GraphicsSettingsPath, "ProjectSettings/ProjectSettings.asset", "ProjectSettings/QualitySettings.asset",
        };

        public static void EnableGpuResidentDrawer() => Apply(drawer: true, occlusion: ForestArgs.Flag("-forestGpuOcclusion"));

        public static void Revert() => Apply(drawer: false, occlusion: false);

        public static void Report() => Debug.Log("Forest project settings: " + Describe());

        static void Apply(bool drawer, bool occlusion)
        {
            if (!ForestArgs.Flag("-forestAllowProjectSettings"))
                throw new InvalidOperationException("ForestProjectSettings changes project-wide settings and runs only with -forestAllowProjectSettings " +
                    "(spec Appendix C.4: an explicit batch command after an owner decision).");

            HDRenderPipelineAsset asset = RequireAsset();
            Debug.Log("Forest project settings before: " + Describe());
            Debug.Log("Forest project settings: backup written to " + Backup());

            var serialized = new SerializedObject(asset);
            Require(serialized, ModePath).intValue = drawer ? ModeInstancedDrawing : ModeDisabled;
            Require(serialized, OcclusionPath).boolValue = drawer && occlusion;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            // EditorGraphicsSettings.batchRendererGroupShaderStrippingMode has no setter: the Graphics settings object is written
            // through its serialized field m_BrgStripping (KeepIfEntitiesGraphics 0, StripAll 1, KeepAll 2).
            var graphics = new SerializedObject(RequireGraphicsSettings());
            Require(graphics, BrgStrippingPath).intValue = (int)(drawer ? BatchRendererGroupStrippingMode.KeepAll : BatchRendererGroupStrippingMode.KeepIfEntitiesGraphics);
            graphics.ApplyModifiedPropertiesWithoutUndo();
            foreach (BuildTarget target in StandaloneTargets)
                PlayerSettings.SetStaticBatchingForPlatform(target, !drawer);

            AssetDatabase.SaveAssets();
            Debug.Log("Forest project settings after: " + Describe());

            // Read back what was asked; a setting that did not take is an error, not a warning.
            var check = new SerializedObject(asset);
            var problems = new List<string>();
            if (Require(check, ModePath).intValue != (drawer ? ModeInstancedDrawing : ModeDisabled)) problems.Add("GPU Resident Drawer mode");
            if (Require(check, OcclusionPath).boolValue != (drawer && occlusion)) problems.Add("GPU occlusion culling");
            if ((EditorGraphicsSettings.batchRendererGroupShaderStrippingMode == BatchRendererGroupStrippingMode.KeepAll) != drawer) problems.Add("BatchRendererGroup Variants");
            foreach (BuildTarget target in StandaloneTargets)
                if (PlayerSettings.GetStaticBatchingForPlatform(target) == drawer) problems.Add("static batching for " + target);
            if (problems.Count > 0)
                throw new InvalidOperationException("Forest project settings: these did not take: " + string.Join(", ", problems));
        }

        static HDRenderPipelineAsset RequireAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(HdrpAssetPath);
            if (asset == null)
                throw new InvalidOperationException($"{HdrpAssetPath} is not an HDRP asset.");
            if (GraphicsSettings.defaultRenderPipeline != asset)
                throw new InvalidOperationException($"The project's default render pipeline is not {HdrpAssetPath} " +
                    $"but {(GraphicsSettings.defaultRenderPipeline != null ? AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline) : "none")}.");
            for (int level = 0; level < QualitySettings.names.Length; level++)
            {
                RenderPipelineAsset own = QualitySettings.GetRenderPipelineAssetAt(level);
                if (own != null && own != asset)
                    throw new InvalidOperationException($"Quality level {QualitySettings.names[level]} uses its own pipeline asset {AssetDatabase.GetAssetPath(own)}; " +
                        "the drawer settings would have to be set there too.");
            }
            return asset;
        }

        static UnityEngine.Object RequireGraphicsSettings()
        {
            foreach (UnityEngine.Object candidate in AssetDatabase.LoadAllAssetsAtPath(GraphicsSettingsPath))
                if (candidate is GraphicsSettings)
                    return candidate;
            throw new InvalidOperationException($"{GraphicsSettingsPath} holds no GraphicsSettings object.");
        }

        static SerializedProperty Require(SerializedObject serialized, string path)
        {
            SerializedProperty property = serialized.FindProperty(path);
            if (property == null)
                throw new InvalidOperationException($"{serialized.targetObject.name} has no serialized property {path} (Unity or HDRP version changed?).");
            return property;
        }

        static string Backup()
        {
            string folder = Path.Combine("Library/GamexForest/ProjectSettingsBackup", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            foreach (string file in TouchedFiles)
            {
                string destination = Path.Combine(folder, file);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(file, destination, overwrite: true);
            }
            return Path.GetFullPath(folder);
        }

        static string Describe()
        {
            HDRenderPipelineAsset asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(HdrpAssetPath);
            var serialized = asset != null ? new SerializedObject(asset) : null;
            string Value(string path)
            {
                SerializedProperty property = serialized?.FindProperty(path);
                if (property == null) return "missing";
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Boolean: return property.boolValue ? "on" : "off";
                    case SerializedPropertyType.Float: return property.floatValue.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    default: return property.intValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            var batching = new List<string>();
            foreach (BuildTarget target in StandaloneTargets)
                batching.Add($"{target} {(PlayerSettings.GetStaticBatchingForPlatform(target) ? "on" : "off")}");
            BatchRendererGroupStrippingMode stripping = EditorGraphicsSettings.batchRendererGroupShaderStrippingMode;
            return $"HDRP asset {HdrpAssetPath}: GPU Resident Drawer mode {Value(ModePath)} (0 disabled, 1 instanced drawing), " +
                $"GPU occlusion culling {Value(OcclusionPath)}, occluders from depth prepass {Value(OccludersFromPrepassPath)}, small-mesh screen % {Value(SmallMeshPath)}; " +
                $"BatchRendererGroup Variants {stripping} ({(int)stripping}); static batching: {string.Join(", ", batching)} (active target {EditorUserBuildSettings.activeBuildTarget} " +
                $"{(PlayerSettings.GetStaticBatchingForPlatform(EditorUserBuildSettings.activeBuildTarget) ? "on" : "off")}); " +
                $"LOD cross-fade {QualitySettings.enableLODCrossFade}; quality level {QualitySettings.names[QualitySettings.GetQualityLevel()]}; " +
                $"default pipeline {(GraphicsSettings.defaultRenderPipeline != null ? AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline) : "none")}";
        }
    }
}
