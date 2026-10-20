// Gamex forest wind graphs, fragment side (step W1, spec W1-spec.md section 3.6): HDRP/Lit's surface sampling, ported so that a
// material on Gamex/ForestWind renders as the same material on HDRP/Lit does.
//
// Sampling follows LitDataIndividualLayer.hlsl: every map with its own sampler, HDRP's global mip bias (HDRP's SAMPLE_TEXTURE2D
// adds _GlobalMipBias), the base map's scale and offset on every map, the mask's remaps. The normal is resolved with the
// surface-gradient maths HDRP/Lit uses (SURFACE_GRADIENT): UnpackDerivativeNormalRGorAG, SurfaceGradientFromTBN (UV0) or
// SurfaceGradientFromVolumeGradient (triplanar), then SurfaceGradientResolveNormal. The output normal is in world space.
#ifndef GAMEX_FOREST_SURFACE_HLSL
#define GAMEX_FOREST_SURFACE_HLSL

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/NormalSurfaceGradient.hlsl"

void ForestSurfaceUV_float(
    UnityTexture2D BaseColorMap, UnityTexture2D NormalMap, UnityTexture2D MaskMap, float2 UV,
    float3 NormalWS, float3 TangentWS, float3 BitangentWS, float4 BaseColor, float NormalScale,
    float MetallicRemapMin, float MetallicRemapMax, float AORemapMin, float AORemapMax,
    float SmoothnessRemapMin, float SmoothnessRemapMax,
    out float3 Albedo, out float Alpha, out float3 NormalOutWS, out float Metallic, out float AO, out float Smoothness)
{
    float2 uv = UV * BaseColorMap.scaleTranslate.xy + BaseColorMap.scaleTranslate.zw;

    float4 colour = SAMPLE_TEXTURE2D(BaseColorMap.tex, BaseColorMap.samplerstate, uv) * BaseColor;
    Albedo = colour.rgb;
    Alpha = colour.a;

    float2 deriv = UnpackDerivativeNormalRGorAG(SAMPLE_TEXTURE2D(NormalMap.tex, NormalMap.samplerstate, uv), NormalScale);
    float3 gradient = SurfaceGradientFromTBN(deriv, TangentWS, BitangentWS);
    NormalOutWS = SurfaceGradientResolveNormal(NormalWS, gradient);

    float4 mask = SAMPLE_TEXTURE2D(MaskMap.tex, MaskMap.samplerstate, uv);
    Metallic = lerp(MetallicRemapMin, MetallicRemapMax, mask.r);
    AO = lerp(AORemapMin, AORemapMax, mask.g);
    Smoothness = lerp(SmoothnessRemapMin, SmoothnessRemapMax, mask.a);
}

// HDRP/Lit's triplanar path (UV_MAPPING_TRIPLANAR with SURFACE_GRADIENT), projected from the rest position and weighted by the
// rest normal (Custom Interpolators), so the bark does not swim when the tree sways. The gradient is resolved against the
// current normal, so the lighting follows the swayed surface.
float4 GamexTriplanarSample(UnityTexture2D map, float2 uvZY, float2 uvXZ, float2 uvXY, float3 weights)
{
    float4 value = float4(0.0, 0.0, 0.0, 0.0);
    if (weights.x > 0.0) value += weights.x * SAMPLE_TEXTURE2D(map.tex, map.samplerstate, uvZY);
    if (weights.y > 0.0) value += weights.y * SAMPLE_TEXTURE2D(map.tex, map.samplerstate, uvXZ);
    if (weights.z > 0.0) value += weights.z * SAMPLE_TEXTURE2D(map.tex, map.samplerstate, uvXY);
    return value;
}

void ForestSurfaceTriplanar_float(
    UnityTexture2D BaseColorMap, UnityTexture2D NormalMap, UnityTexture2D MaskMap,
    float3 RestPositionAWS, float3 RestNormalWS, float3 NormalWS, float TexWorldScale,
    float4 BaseColor, float NormalScale,
    float MetallicRemapMin, float MetallicRemapMax, float AORemapMin, float AORemapMax,
    float SmoothnessRemapMin, float SmoothnessRemapMax,
    out float3 Albedo, out float Alpha, out float3 NormalOutWS, out float Metallic, out float AO, out float Smoothness)
{
    float2 scale = BaseColorMap.scaleTranslate.xy, bias = BaseColorMap.scaleTranslate.zw;
    float3 position = RestPositionAWS * TexWorldScale;
    float2 uvXZ, uvXY, uvZY;
    GetTriplanarCoordinate(position, uvXZ, uvXY, uvZY);
    uvXZ = uvXZ * scale + bias;
    uvXY = uvXY * scale + bias;
    uvZY = uvZY * scale + bias;

    float restLength = length(RestNormalWS);
    float3 weights = ComputeTriplanarWeights(restLength > 1e-6 ? RestNormalWS / restLength : NormalWS);

    float4 colour = GamexTriplanarSample(BaseColorMap, uvZY, uvXZ, uvXY, weights) * BaseColor;
    Albedo = colour.rgb;
    Alpha = colour.a;

    float2 derivX = float2(0.0, 0.0), derivY = float2(0.0, 0.0), derivZ = float2(0.0, 0.0);
    if (weights.x > 0.0) derivX = weights.x * UnpackDerivativeNormalRGorAG(SAMPLE_TEXTURE2D(NormalMap.tex, NormalMap.samplerstate, uvZY), NormalScale);
    if (weights.y > 0.0) derivY = weights.y * UnpackDerivativeNormalRGorAG(SAMPLE_TEXTURE2D(NormalMap.tex, NormalMap.samplerstate, uvXZ), NormalScale);
    if (weights.z > 0.0) derivZ = weights.z * UnpackDerivativeNormalRGorAG(SAMPLE_TEXTURE2D(NormalMap.tex, NormalMap.samplerstate, uvXY), NormalScale);
    // (z, y), (x, z) and (x, y) planes, Morten's convention, as SampleUVMappingNormalInternal.hlsl.
    float3 volumeGradient = float3(derivZ.x + derivY.x, derivZ.y + derivX.y, derivX.x + derivY.y);
    float3 gradient = SurfaceGradientFromVolumeGradient(NormalWS, volumeGradient);
    NormalOutWS = SurfaceGradientResolveNormal(NormalWS, gradient);

    float4 mask = GamexTriplanarSample(MaskMap, uvZY, uvXZ, uvXY, weights);
    Metallic = lerp(MetallicRemapMin, MetallicRemapMax, mask.r);
    AO = lerp(AORemapMin, AORemapMax, mask.g);
    Smoothness = lerp(SmoothnessRemapMin, SmoothnessRemapMax, mask.a);
}

#endif // GAMEX_FOREST_SURFACE_HLSL
