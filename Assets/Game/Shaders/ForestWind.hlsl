// Gamex forest wind and foliage pushes (step W1, spec W1-spec.md section 3.5).
//
// Ideas ported from Unity's Shader Graph samples (com.unity.shadergraph Samples~, Unity Companion License); no file is copied:
// - FoliageWind.shadersubgraph: gusts as a field of the pivot position in a wind-aligned frame, scrolled downwind over time
//   (here a sum of three wrapped sines shaped into gusts and lulls, in a frame that never rotates);
// - RandomFromPosition.shadersubgraph: a per-object random value from the pivot position (here an integer PCG2D hash);
// - DistanceCutoff.shadersubgraph: the amplitude fades continuously to zero with the camera distance of the object;
// - BendDeformer.shadersubgraph: a bend that keeps the length (here the distance to the pivot is kept);
// - Wind.shadersubgraph: the whole plant moves about its pivot, weighted root to tip (here by baked vertex colours).
//
// Every global is zero when nothing writes it, and all zero means the rest pose (step 2 returns before any wind code).
//
// W1 I2b (issues/W1-runtime.md section 11): the terms that are the same for every vertex of an instance (the pivot hash, the gust,
// the size scale, the trunk's oscillators) are GamexInstance. Each registered renderer reads them from _GamexInstanceTex at its slot
// (Renderer.SetShaderUserValue, which the GPU Resident Drawer carries per instance), filled once a frame by FoliageInteraction.compute's
// Instances kernel; an unregistered renderer (slot 0) and the test probe compute them with GamexInstanceAnalytic. The per-vertex code
// is the same either way.
//
// W1 review round 1 (issues/W1-runtime.md section 13): a passable sapling's lean and a solid fir's swing are per-instance states the
// Instances kernel integrates once a frame from the bodies' stamps; branches bob with three incommensurate frequencies; whether an
// instance is near a body is one bit of its renderer user value (GAMEX_PRESSED_BIT).
//
// W1 review round 2 (issues/W1-runtime.md section 14):
// - a passable sapling no longer samples the field at all: it turns rigidly about its pivot (a root lean, so no needle is sheared by
//   a bend concentrated in its lower crown) with a gentle bow of the upper stem (a second spring: the crown lags, then whips), and a
//   smooth clearance translation, computed from the pressing body's axis segment that the kernel latches, moves its foliage out of the
//   body toward the stem's side with a gradient of at most GAMEX_CLEAR_SLOPE. Its pieces are neither torn nor folded, its state never
//   switches the side a vertex goes to, and its low branches ease onto the ground instead of being projected onto it;
// - a solid fir's swing turns the boughs that face the body (max(0, cos)^2), not the whole lower crown;
// - a fern frond or grass blade turns rigidly about its own base (the anchor baked in UV2: the base's offset and the frond's azimuth),
//   tilted away from the body and pitched down by the field's press at its base, so it neither stretches nor splits across a body's
//   axis; in the wind it nods with its own phase and frequency (from A, which is the same on both fern levels);
// - branches trace ellipses (the vertical bob a quarter cycle off the along-wind bob), and the activity averages 1;
// - the moon's cascades draw a pressed instance's push (its wind stays at rest there, as shipped), and a pressed instance pushes out to
//   the field's reach whatever the wind's distance fade.
#ifndef GAMEX_FOREST_WIND_HLSL
#define GAMEX_FOREST_WIND_HLSL

#define GAMEX_TAU 6.28318530717958647692
// Slots per row of the instance textures; each slot is six texels in a row (three of wind terms, three of an instance's push state:
// a passable sapling's lean, bow, clearance and the latched body, or a solid fir's swing).
#define GAMEX_INSTANCE_COLUMNS 1024
#define GAMEX_INSTANCE_TEXELS 6
// W1 R1: the renderer user value carries the slot in its low bits and this bit when a body is near the instance (ForestWindInstances).
// Bit 30, not 31: the value travels as a float's bits outside DOTS instancing, and bit 30 keeps that float normal.
#define GAMEX_PRESSED_BIT 0x40000000u
#define GAMEX_SLOT_MASK 0x3FFFFFFFu
// W1 R1: how far above the body's band top a push fades out (m).
#define GAMEX_BAND_FADE 0.6
// W1 R1: a solid fir's swing fades out over this much above the band top (m).
#define GAMEX_SWING_BAND_FADE 1.6
// W1 R1: beyond this distance from the trunk's axis (m), a solid fir's swing turns less (as this distance over the vertex's).
#define GAMEX_SWING_RADIUS 1.0
// W1 R2: a passable sapling's clearance translation (world m): GAMEX_CLEAR_BELOW short of the body's radius on its axis line, falling
// by GAMEX_CLEAR_SLOPE per metre either side of it (so a piece is squeezed or stretched by at most that share), over GAMEX_CLEAR_ALONG
// beyond the body's ends (from its rounded ends on), over GAMEX_CLEAR_BAND_FADE above its band top, and linearly from GAMEX_CLEAR_FOOT_BELOW
// below the pivot's plane to GAMEX_CLEAR_FOOT above it (the stem's foot stays nearly where it is: a quarter of the clearance at the
// pivot; the slope stays shallow enough not to squeeze the low needles below 0.67 of their size).
#define GAMEX_CLEAR_BELOW 0.03
#define GAMEX_CLEAR_SLOPE 0.22
#define GAMEX_CLEAR_ALONG 2.0
#define GAMEX_CLEAR_BAND_FADE 1.6
#define GAMEX_CLEAR_FOOT 0.3
#define GAMEX_CLEAR_FOOT_BELOW 0.1
// W1 R2: a frond's press is the larger of the field's at its base and at this distance (m at scale 1) out along its azimuth, so a frond
// whose base stands just outside a body's reach but which leans into its path still parts.
#define GAMEX_FROND_REACH 0.45
// W1 R2: what a frond turns below its base eases onto a floor GAMEX_GROUND_CLEARANCE under it within this (m at scale 1).
#define GAMEX_FROND_KNEE 0.1
// W1 R2: a passable sapling's foliage turned toward the ground eases onto a floor this far (world m) above the pivot's plane within
// GAMEX_GROUND_KNEE of it, instead of being projected flat onto it; a fern frond turned below its base eases onto a floor this far under
// it (GAMEX_FROND_KNEE).
#define GAMEX_GROUND_CLEARANCE 0.03
#define GAMEX_GROUND_KNEE 0.08

// ---- Globals (outside UnityPerMaterial), written only by Gamex.ForestWindGlobals ----
float4 _GamexWind;                  // leanX, leanZ (unit, world XZ), calm, peak
float4 _GamexWindGust;              // gustX, gustZ (unit, world XZ, never wandered), 1 / wavelength (1/m), front speed (m/s)
float4 _GamexWindParams;            // time offset (s), seed, torch shadow fade end (m; 0 = fade end), W1 I2c tests only: 1 = solid firs push each vertex to its own side
float4 _GamexWindFade;              // fadeStart (m), fadeEnd (m), flutterEnd (m), moon-cascade mode (0, 1, 2)
float4 _GamexGustRect;              // knob 2: origin X, origin Z, 1 / size, valid
float4 _GamexInteractionRect;       // origin X, origin Z, 1 / window size, valid
float4 _GamexInteractionRectPrev;
float4 _GamexInteractionBox;        // W1 I2: world XZ (minX, minZ, maxX, maxZ) the field stamped in the last 4 s; 0 = no box; min > max = none
float4 _GamexInteractionParams;     // W1 R1: x = 1 / the field's last step (1/s; 0 while paused)
float4 _GamexInstanceParams;        // W1 I2b: x = registered slots (0 = none: every instance computes its own terms)
TEXTURE2D(_GamexGustTex);
TEXTURE2D(_GamexInteractionTex);
TEXTURE2D(_GamexInteractionTexPrev);
TEXTURE2D(_GamexInstanceTex);       // W1 I2b: per slot, the per-instance terms at this frame's time and the push state this frame
TEXTURE2D(_GamexInstanceTexPrev);   // the same at last frame's time (HDRP's _LastTimeParameters) and last frame's push state

// A sine whose argument is in cycles, wrapped to [-pi, pi) first: no argument grows with play time, and Metal's fast-math sin
// is only specified on that range.
float GamexS(float cycles)
{
    return sin(GAMEX_TAU * (frac(cycles + 0.5) - 0.5));
}

// W1 R2: GamexS(cycles) and GamexS(cycles + 0.25) at once (the sine and cosine of the same wrapped argument).
void GamexSC(float cycles, out float s, out float c)
{
    sincos(GAMEX_TAU * (frac(cycles + 0.5) - 0.5), s, c);
}

// PCG2D (Jarzynski and Olano 2020) of the pivot quantised to centimetres; [0, 1).
float GamexHash01(float2 xz)
{
    uint2 v = asuint(int2(floor(xz * 100.0)));
    v = v * 1664525u + 1013904223u;
    v.x += v.y * 1664525u;
    v.y += v.x * 1664525u;
    v = v ^ (v >> 16u);
    v.x += v.y * 1664525u;
    v.y += v.x * 1664525u;
    v = v ^ (v >> 16u);
    return (float)(v.x >> 8) * (1.0 / 16777216.0);
}

// e: 0 in a lull, 1 at a gust's crest. The CPU mirror is ForestWindGlobals.SampleGust (same constants, same wraps).
float GamexGust(float2 xz, float t)
{
    float2 d = _GamexWindGust.xy;
    float2 c = float2(-d.y, d.x);
    float invL = _GamexWindGust.z;
    float v = _GamexWindGust.w;
    float seed = _GamexWindParams.y;
    float u = (dot(xz, d) - v * t) * invL;      // in wavelengths; fronts travel downwind at v m/s
    float w = dot(xz, c) * invL * 0.5;          // across the wind, fronts twice as wide as long
    float s = 0.70 * GamexS(u + (1.3 / GAMEX_TAU) * GamexS(w + seed))
            + 0.20 * GamexS(1.73 * u + 2.1 / GAMEX_TAU + (0.7 / GAMEX_TAU) * GamexS(1.31 * w + 2.0 * seed))
            + 0.10 * GamexS(2.71 * u - 0.6 * w + seed + 4.2 / GAMEX_TAU);
    float g = 0.5 + 0.5 * s;
    return smoothstep(0.62, 0.92, g);
}

float GamexSampleGustTex(float2 xz)
{
    float2 uv = (xz - _GamexGustRect.xy) * _GamexGustRect.z;
    return SAMPLE_TEXTURE2D_LOD(_GamexGustTex, s_linear_clamp_sampler, saturate(uv), 0).r;
}

// ---- Per-instance terms (W1 I2b) ----

// What GamexWindDeform needs that is the same for every vertex of an instance at one time.
struct GamexInstance
{
    float3 trunk;         // object space: Sway.x x (dir (0.6 lean + 0.4 osc x amplitude) + cross 0.15 S(0.77 fT t + h3) x amplitude); per vertex x R x fade
    float  wind;          // the amplitude of the oscillating terms: W before the distance fade ((calm + (peak - calm) e) x shelter; W1 R2: its envelope)
    float3 dir;           // the lean direction in object space, unit (0 without a lean); computed per vertex, never stored
    float  branchPhase;   // frac(fB t + spread h), cycles; per vertex + A - ripple
    // W1 R1: two more branch frequencies (1.61 fB and 0.53 fB: no common period) and a slow activity cycle (about 0.13 Hz).
    float  branchPhase2;
    float  branchPhase3;
    float  activityPhase;
    float  flutterPhase;  // frac(Flutter.y t + h), cycles; per vertex + 7.13 A + dot(p0, k)
    float3 up;            // world up in object space, unit (computed per vertex from the world-to-object matrix, never stored)
    // W1 R2, the Instances kernel's only (the wind's dynamics): the parts of the trunk's term, and the natural frequencies.
    float3 cross;         // crossOS, unit
    float  osc;           // 0.65 S(fT t + spread h) + 0.35 S(1.618 fT t + h2)
    float  crossOsc;      // 0.15 S(0.77 fT t + h3)
    float  trunkFrequency, branchFrequency;   // Hz (0 without that term)
};

// The per-instance terms of spec 3.5 steps 5 to 9, from the instance's pivot, scale, world-to-object 3x3 and profile, at time t
// (the Time input plus the time offset). allowGustTexture: knob 2's texture may stand in for GamexGust (the vertex shader's own
// fallback); the Instances kernel passes false and evaluates the gust exactly, once per instance.
GamexInstance GamexInstanceAnalytic(float3 pivotAWS, float s, float t, float4 Sway, float4 Flutter, float4 Motion, float3x3 w2o, bool allowGustTexture)
{
    GamexInstance inst;
    float h = GamexHash01(pivotAWS.xz);
    float h2 = frac(7.31 * h), h3 = frac(3.17 * h), h4 = frac(5.3 * h), h5 = frac(11.7 * h), h6 = frac(13.1 * h);
    float spread = Motion.w;
    float sizeScale = pow(max(s, 0.05), Motion.x);
    float3 leanOS = mul(w2o, float3(_GamexWind.x, 0.0, _GamexWind.y));
    float leanLength = length(leanOS);
    float3 dirOS = leanLength > 1e-6 ? leanOS / leanLength : float3(0.0, 0.0, 0.0);
    float3 upOS = normalize(mul(w2o, float3(0.0, 1.0, 0.0)));
    float3 crossOS = cross(upOS, dirOS);

    // A branch, not ?: (which evaluates both sides): I2's vertex function fetched knob 2's texture in every vertex even with the
    // knob off.
    float e;
    [branch] if (allowGustTexture && _GamexGustRect.w > 0.0)
        e = GamexSampleGustTex(pivotAWS.xz);
    else
        e = GamexGust(pivotAWS.xz, t);
    inst.wind = (_GamexWind.z + (_GamexWind.w - _GamexWind.z) * e) * Motion.y;

    inst.trunk = float3(0.0, 0.0, 0.0);
    inst.osc = 0.0;
    inst.crossOsc = 0.0;
    inst.trunkFrequency = 0.0;
    [branch] if (Sway.x > 0.0 && Sway.y > 0.0)
    {
        float fT = rcp(max(Sway.y * sizeScale * (0.85 + 0.30 * h), 0.05));
        inst.trunkFrequency = fT;
        inst.osc = 0.65 * GamexS(fT * t + spread * h) + 0.35 * GamexS(1.618 * fT * t + h2);
        inst.crossOsc = 0.15 * GamexS(0.77 * fT * t + h3);
        inst.trunk = (Sway.x * inst.wind) * (dirOS * (0.6 + 0.4 * inst.osc) + crossOS * inst.crossOsc);
    }
    inst.branchPhase = 0.0;
    inst.branchPhase2 = 0.0;
    inst.branchPhase3 = 0.0;
    inst.branchFrequency = 0.0;
    [branch] if (Sway.z > 0.0 && Sway.w > 0.0)
    {
        float fB = rcp(max(Sway.w * sizeScale * (0.9 + 0.2 * h4), 0.05));
        inst.branchFrequency = fB;
        inst.branchPhase = frac(fB * t + spread * h);
        inst.branchPhase2 = frac(1.61 * fB * t + h5);
        inst.branchPhase3 = frac(0.53 * fB * t + h6);
    }
    inst.activityPhase = frac(0.13 * (0.85 + 0.3 * h2) * t + h3);
    inst.flutterPhase = (Flutter.x > 0.0 && Flutter.y > 0.0) ? frac(Flutter.y * t + h) : 0.0;
    inst.dir = dirOS;
    inst.up = upOS;
    inst.cross = crossOS;
    return inst;
}

// The texel of a slot's k-th float4 (slot 1-based; k 0..5).
int2 GamexInstanceTexel(uint slot, uint k)
{
    uint i = slot - 1u;
    return int2((i % GAMEX_INSTANCE_COLUMNS) * GAMEX_INSTANCE_TEXELS + k, i / GAMEX_INSTANCE_COLUMNS);
}

// W1 R2: texel 1 carries the branch and activity phases, texel 2 the flutter phase; the lean direction is computed per vertex.
GamexInstance GamexInstanceUnpack(float4 a, float4 b, float4 c)
{
    GamexInstance inst = (GamexInstance)0;
    inst.trunk = a.xyz;
    inst.wind = a.w;
    inst.branchPhase = b.x;
    inst.branchPhase2 = b.y;
    inst.branchPhase3 = b.z;
    inst.activityPhase = b.w;
    inst.flutterPhase = c.x;
    inst.dir = float3(0.0, 0.0, 0.0);   // the caller computes it from the world-to-object matrix
    inst.up = float3(0.0, 1.0, 0.0);    // the caller computes it from the world-to-object matrix
    return inst;
}

#ifndef GAMEX_WIND_COMPUTE
// A registered renderer's terms, at this frame's time or (in the motion vector pass's previous-frame evaluation) at last frame's.
GamexInstance GamexInstanceLoad(uint slot, bool previous)
{
    float4 a, b, c;
    [branch] if (previous)
    {
        a = LOAD_TEXTURE2D_LOD(_GamexInstanceTexPrev, GamexInstanceTexel(slot, 0u), 0);
        b = LOAD_TEXTURE2D_LOD(_GamexInstanceTexPrev, GamexInstanceTexel(slot, 1u), 0);
        c = LOAD_TEXTURE2D_LOD(_GamexInstanceTexPrev, GamexInstanceTexel(slot, 2u), 0);
    }
    else
    {
        a = LOAD_TEXTURE2D_LOD(_GamexInstanceTex, GamexInstanceTexel(slot, 0u), 0);
        b = LOAD_TEXTURE2D_LOD(_GamexInstanceTex, GamexInstanceTexel(slot, 1u), 0);
        c = LOAD_TEXTURE2D_LOD(_GamexInstanceTex, GamexInstanceTexel(slot, 2u), 0);
    }
    return GamexInstanceUnpack(a, b, c);
}
#endif

// ---- Pushes ----

// W1 R2: an instance's push state in one frame (texels 3 to 5 of its slot).
// - A passable sapling: a = (lean direction, world XZ unit; the root lean, radians, signed; the object-space height while it moves,
//   else 0), b = (the upper stem's bow, radians, signed; the clearance press, signed; the latched body's radius; its band top, world Y),
//   c = (the latched body's axis segment, world XZ: from x, z, to x, z).
// - A solid fir: a = (its swing's sense times its press, signed; the direction from its trunk toward the body, world XZ unit; the body's
//   band top + 1000, 0 when unknown); b and c unused.
struct GamexPushState
{
    float4 a;
    float4 b;
    float4 c;
};

float4 GamexField(bool previous, float2 xz, float4 rect)
{
    float2 uv = (xz - rect.xy) * rect.z;
    if (any(uv < 0.0) || any(uv > 1.0))
        return float4(0.0, 0.0, 0.0, 0.0);
    return previous
        ? SAMPLE_TEXTURE2D_LOD(_GamexInteractionTexPrev, s_linear_clamp_sampler, uv, 0)
        : SAMPLE_TEXTURE2D_LOD(_GamexInteractionTex, s_linear_clamp_sampler, uv, 0);
}

// The direction to push at a point of the field: rg normalised, except within about a texel of a body's axis. There the bilinear
// filter averages the two sides' opposite directions to almost nothing, so the point takes the side of the nearest texel instead (one
// point sample, only next to a body's axis). W1 R2: the press is signed (the field's spring overshoots), so the test uses its size.
float2 GamexFieldDirection(bool previous, float2 xz, float4 rect, float4 f)
{
    float rgLength = length(f.rg);
    float2 direction = rgLength > 1e-4 ? f.rg / rgLength : float2(0.0, 0.0);
    [branch] if (abs(f.a) > 1e-3 && rgLength < 0.9 * abs(f.a))
    {
        float2 uv = saturate((xz - rect.xy) * rect.z);
        float2 nearest = previous
            ? SAMPLE_TEXTURE2D_LOD(_GamexInteractionTexPrev, s_point_clamp_sampler, uv, 0).rg
            : SAMPLE_TEXTURE2D_LOD(_GamexInteractionTex, s_point_clamp_sampler, uv, 0).rg;
        float nearestLength = length(nearest);
        if (nearestLength > 1e-4)
            direction = nearest / nearestLength;
    }
    return direction;
}

// Which per-instance push state a fir's slot carries: a passable sapling's lean, or a solid fir's swing (W1 I2c, W1 R1).
bool GamexIsSapling(float4 Stem, float s) { return Stem.x > 0.0 && s <= Stem.x + 1e-3; }
// _GamexWindParams.w (tests only, 0 as shipped) restores I2b's push for solid firs, the positive control of the tearing check.
bool GamexHasSolidSide(float4 Stem, float s) { return Stem.w > 0.0 && _GamexWindParams.w < 0.5 && !GamexIsSapling(Stem, s); }
// W1 R2: ferns and grass (no trunk term) turn each frond or blade about its own base, the anchor baked in UV2.
bool GamexIsAnchored(float4 Sway) { return Sway.x <= 0.0; }

// v = normalize(v + angle * cross(axis, v)) for the normal and the tangent; skipped for a tiny angle or a degenerate axis.
void GamexTilt(inout float3 n, inout float3 t, float3 axis, float angle)
{
    float axisLength = length(axis);
    if (angle < 1e-4 || axisLength < 1e-6)
        return;
    axis /= axisLength;
    float3 n2 = n + angle * cross(axis, n);
    float3 t2 = t + angle * cross(axis, t);
    float nl = length(n2), tl = length(t2);
    n = nl > 1e-6 ? n2 / nl : n;
    t = tl > 1e-6 ? t2 / tl : t;
}

// Rodrigues: v rotated about the unit axis k by theta.
float3 GamexRotate(float3 v, float3 k, float theta)
{
    float c = cos(theta), s = sin(theta);
    return v * c + cross(k, v) * s + k * dot(k, v) * (1.0 - c);
}

// W1 R2: a height (along up, from a plane) eased onto a floor: unchanged above min(floor + knee, its rest height), approaching the floor
// below that, never folding (monotone, slope 1 where the easing begins). A vertex at rest is never moved, so a push begins smoothly.
float GamexSoftFloor(float height, float restHeight, float floorHeight, float knee)
{
    float top = min(floorHeight + knee, restHeight);
    float k = top - floorHeight;
    if (height >= top)
        return height;
    return k > 1e-5 ? floorHeight + k * exp((height - top) / k) : max(height, floorHeight);
}

// W1 R2: a passable sapling's bend at height h (object space): its root lean (the whole sapling turns about its pivot, so no piece is
// sheared) plus the upper stem's bow, (h / H)^1.5 of it. R1's bend concentrated a large lean in the lowest 0.6 m, which sheared the
// lower crown's needles up to 4.4 times their size.
float GamexStemBend(float h, float root, float bow, float H)
{
    float u = saturate(h / max(H, 1e-3));
    return root + bow * u * sqrt(u);
}

// W1 R2: the passable sapling's push from its state alone (the field is never sampled for it): returns the displacement; (axis, angle)
// is the rotation of the vertex's frame for the normal and tangent.
float3 GamexSaplingPush(float3 p0, float3 positionAWS, float s, float3x3 w2o, float3 upOS, float fade, GamexPushState st,
                        out float3 axis, out float angle)
{
    axis = float3(0.0, 1.0, 0.0);
    angle = 0.0;
    if (st.a.w <= 0.5)
        return float3(0.0, 0.0, 0.0);
    float3 awayOS = mul(w2o, float3(st.a.x, 0.0, st.a.y));
    float awayLength = length(awayOS);
    float3 k = cross(upOS, awayLength > 1e-6 ? awayOS / awayLength : float3(1.0, 0.0, 0.0));
    float kLength = length(k);
    float3 P = p0;
    float h = dot(p0, upOS);
    if (kLength > 1e-6)
    {
        k /= kLength;
        float phi = GamexStemBend(h, st.a.z, st.b.x, st.a.w) * fade;
        P = GamexRotate(p0, k, phi);
        axis = k;
        angle = phi;
        // The low branches a lean turns toward the ground ease onto a floor just above the pivot's plane (a vertex resting below it
        // keeps its own rest height as its floor, so the floor is continuous across vertices).
        float floorHeight = min(GAMEX_GROUND_CLEARANCE / max(s, 1e-3), h);
        float height = dot(P, upOS);
        P += upOS * (GamexSoftFloor(height, h, floorHeight, GAMEX_GROUND_KNEE / max(s, 1e-3)) - height);
    }

    // The clearance: away from the latched body's axis line toward the stem's side (the lean's side), largest on the line and falling
    // by GAMEX_CLEAR_SLOPE per metre either side, so a piece across the line is stretched by at most that share and none is torn.
    float2 from = st.c.xy, along = st.c.zw - st.c.xy;
    float segment = length(along);
    float2 t = segment > 1e-4 ? along / segment : float2(-st.a.y, st.a.x);
    float2 n = float2(-t.y, t.x);
    if (dot(n, st.a.xy) < 0.0)
        n = -n;
    float2 x = positionAWS.xz - from;
    float side = dot(x, n);
    float onAxis = dot(x, t);
    float beyond = max(0.0, max(-onAxis, onAxis - segment));
    float clearance = max(0.0, max(st.b.z - GAMEX_CLEAR_BELOW, 0.0) - GAMEX_CLEAR_SLOPE * abs(side));
    // Full within the capsule's rounded ends (the body's radius beyond its axis segment), fading beyond them.
    clearance *= 1.0 - smoothstep(st.b.z, st.b.z + GAMEX_CLEAR_ALONG, beyond);
    clearance *= 1.0 - smoothstep(st.b.w, st.b.w + GAMEX_CLEAR_BAND_FADE, positionAWS.y);
    clearance *= saturate((h * s + GAMEX_CLEAR_FOOT_BELOW) / (GAMEX_CLEAR_FOOT + GAMEX_CLEAR_FOOT_BELOW));
    clearance *= st.b.y * fade;
    // Unnormalised: w2o divides by the scale, so the clearance is in world metres.
    return (P - p0) + mul(w2o, float3(n.x, 0.0, n.y)) * clearance;
}

// W1 R2: the field for a frond: the press is the larger of its base's and its middle's (signed, the larger in size), the direction the
// two points' directions weighted by their presses (continuous: they lie on the frond's side of a body's axis or near it), the band top
// the pressed point's. Both points are the same for every vertex of a frond, so the frond turns as one.
void GamexFrondField(bool previous, float2 baseXZ, float2 midXZ, float4 rect, out float4 f, out float2 away)
{
    float4 fb = GamexField(previous, baseXZ, rect);
    float4 fm = GamexField(previous, midXZ, rect);
    float2 db = abs(fb.a) > 1e-4 ? GamexFieldDirection(previous, baseXZ, rect, fb) : float2(0.0, 0.0);
    float2 dm = abs(fm.a) > 1e-4 ? GamexFieldDirection(previous, midXZ, rect, fm) : float2(0.0, 0.0);
    float2 blended = db * abs(fb.a) + dm * abs(fm.a);
    float blendedLength = length(blended);
    away = blendedLength > 1e-5 ? blended / blendedLength : (dot(db, db) > 0.5 ? db : dm);
    f = abs(fb.a) >= abs(fm.a) ? fb : fm;
}

// W1 R2: a fern frond or grass blade turns rigidly about its own base (anchor.xyz: the base's offset from the vertex, object space;
// anchor.w: the frond's azimuth, object space, radians), by the field's press sampled at the base: tilted away from the body (Push.x
// radians at full press) and pitched down about its own horizontal axis (Push.y radians), plus the contact sway (shiver radians).
// Returns the displacement; the two rotations are returned for the normal and tangent. The ground eases what turns below the base.
float3 GamexFrondPush(bool previous, float4 rect, float3 p0, float4 anchor, float3 positionAWS, float s, float4 Push, float3x3 w2o,
                      float fade, float press, float2 away, float bandTop, float shiver,
                      out float3 axisPitch, out float anglePitch, out float3 axisTilt, out float angleTilt)
{
    axisPitch = float3(1.0, 0.0, 0.0);
    axisTilt = float3(1.0, 0.0, 0.0);
    anglePitch = angleTilt = 0.0;
    float3 baseOS = p0 + anchor.xyz;
    float bandWeight = 1.0 - smoothstep(bandTop, bandTop + GAMEX_BAND_FADE, positionAWS.y + mul(transpose(w2o), anchor.xyz).y * s * s);
    float weight = bandWeight * fade;
    // The frond's own pitch axis: horizontal in the clump's frame, across its azimuth (a positive angle turns it down).
    float2 azimuth = float2(cos(anchor.w), sin(anchor.w));
    axisPitch = float3(azimuth.y, 0.0, -azimuth.x);
    anglePitch = Push.y * press * weight;
    float3 awayOS = mul(w2o, float3(away.x, 0.0, away.y));
    awayOS.y = 0.0;
    float awayLength = length(awayOS);
    float3 v = p0 - baseOS;
    float3 turned = GamexRotate(v, axisPitch, anglePitch);
    if (awayLength > 1e-6)
    {
        axisTilt = cross(float3(0.0, 1.0, 0.0), awayOS / awayLength);
        angleTilt = (Push.x * press + shiver) * weight;
        turned = GamexRotate(turned, axisTilt, angleTilt);
    }
    // What turned below a floor just under the base eases onto it (a frond pressed down lies on the ground), continuous and never
    // folding; a vertex resting below that floor keeps its own rest height as its floor, so the floor is continuous across the frond.
    // (A roll about the base that kept each vertex's distance to it tore fronds whose turned vertices pointed nearly straight down,
    // w1-r2-try4.)
    float floorHeight = min(-GAMEX_GROUND_CLEARANCE / max(s, 1e-3), v.y);
    turned.y = GamexSoftFloor(turned.y, v.y, floorHeight, GAMEX_FROND_KNEE / max(s, 1e-3));
    return (baseOS + turned) - p0;
}

// The push of one field (this frame's or last frame's) for a vertex that is neither a passable sapling's nor an anchored frond's:
// a solid fir's swing, or (a fir whose state is unknown, a renderer without a slot) I2's lateral push. Returns the displacement;
// lateralOnly is the lateral part, (stemAxis, stemAngle) the rotation of the vertex's frame.
float3 GamexPushAt(bool previous, float4 rect, float3 p0, float4 color, float3 positionAWS, float s,
                   float4 Push, float4 Stem, float4 lean, float3x3 w2o, float3 upOS, float fade,
                   out float3 lateralOnly, out float3 stemAxis, out float stemAngle)
{
    lateralOnly = float3(0.0, 0.0, 0.0);
    stemAxis = float3(0.0, 1.0, 0.0);
    stemAngle = 0.0;

    float4 f = GamexField(previous, positionAWS.xz, rect);
    float G = color.g;
    float wp = G > 0.0 ? saturate(Push.z * pow(G, 2.0 / 3.0)) : 0.0;
    float w = wp * (1.0 - smoothstep(f.b, f.b + GAMEX_BAND_FADE, positionAWS.y)) * fade;
    float drop = Push.y * f.a * w * min(p0.y, 1.0 / max(s, 1e-3));

    // W1 R1: a solid fir yields by swinging its pushed boughs about its own trunk's axis, by up to Push.w radians at full press, in the
    // sense that carries them along with the body (the Instances kernel's per-fir state). A swing keeps every vertex's distance to the
    // trunk's axis, so no bough crosses its trunk or changes length. W1 R2 (review of the R1 captures): the boughs that face the body
    // turn (max(0, cos)^2 of the angle between the bough and the direction toward the body), not the whole lower crown spread by
    // 0.5 + 0.5 cos, which swung the side and far boughs with no contact (a turnstile).
    [branch] if (GamexHasSolidSide(Stem, s) && Push.w > 0.0)
    {
        float3 q = p0 - upOS * dot(p0, upOS);
        float swing, sink;
        if (abs(lean.w) > 0.5)
        {
            // W1 R2: (press, signed: the spring's overshoot swings the boughs back past rest; toward x, z; the sense times the band
            // top + 1000).
            float sense = lean.w > 0.0 ? 1.0 : -1.0;
            float bandTop = abs(lean.w) - 1000.0;
            float3 towardOS = mul(w2o, float3(lean.y, 0.0, lean.z));
            float towardLength = length(towardOS), qLength = length(q);
            float facing = towardLength > 1e-6 && qLength > 1e-6 ? dot(q / qLength, towardOS / towardLength) : 0.0;
            float faces = max(facing, 0.0);
            float weight = wp * (1.0 - smoothstep(bandTop, bandTop + GAMEX_SWING_BAND_FADE, positionAWS.y)) * fade * faces * faces;
            float within = min(1.0, GAMEX_SWING_RADIUS / max(qLength * s, 1e-3));
            swing = sense * lean.x * Push.w * weight * within;
            sink = Push.y * lean.x * weight * min(p0.y, 1.0 / max(s, 1e-3));
        }
        else
        {
            swing = Push.w * f.a * w;
            sink = drop;
        }
        stemAxis = upOS;
        stemAngle = swing;
        return GamexRotate(q, upOS, swing) - q - upOS * sink;
    }

    // Every other vertex (a fir whose state is unknown) keeps its own side of the body's axis (I2's push).
    float2 dirW = GamexFieldDirection(previous, positionAWS.xz, rect, f);
    float3 lateral = mul(w2o, float3(dirW.x, 0.0, dirW.y)) * (f.a * Push.x * w);
    lateralOnly = lateral;
    return lateral - float3(0.0, drop, 0.0);
}

// The fallback of a renderer without a slot (none in the forest scene: every wind renderer is registered when the field is enabled;
// the per-instance terms knob off): a passable sapling's state from this one field, without the Instances kernel's springs: leaning
// away from the strongest press near its stem (the pivot and three rings of eight points over its lower crown), with the clearance
// centred on the pivot. W1 R2: in the slot's layout.
GamexPushState GamexSaplingFallback(bool previous, float3 pivotAWS, float s, float4 rect, float4 Stem)
{
    GamexPushState st = (GamexPushState)0;
    float4 fp = GamexField(previous, pivotAWS.xz, rect);
    float strongest = fp.a, bandTop = fp.b;
    float2 away = fp.a > 1e-3 ? GamexFieldDirection(previous, pivotAWS.xz, rect, fp) * fp.a : float2(0.0, 0.0);
    float crown = Stem.z * s;
    [unroll] for (int ring = 1; ring <= 3; ring++)
    {
        float weight = ring == 1 ? 0.7 : ring == 2 ? 0.35 : 0.15;
        [unroll] for (int k = 0; k < 8; k++)
        {
            float angle = (k + 0.5 * (ring & 1)) * (GAMEX_TAU / 8.0);
            float2 direction = float2(cos(angle), sin(angle));
            float4 f = GamexField(previous, pivotAWS.xz + direction * (0.3 * ring * crown), rect);
            if (f.a * weight > strongest)
            {
                strongest = f.a * weight;
                bandTop = f.b;
            }
            away -= direction * f.a;
        }
    }
    float awayLength = length(away);
    if (strongest <= 1e-3 || awayLength <= 1e-4)
        return st;
    float gb = smoothstep(pivotAWS.y + 0.2, pivotAWS.y + 0.6, bandTop);
    float theta = Stem.y * saturate(2.0 * strongest) * gb;
    float2 unit = away / awayLength;
    // 6.5 m: between the passable variants' heights at scale 1 (b 7.68 m, c 5.75 m); the kernel uses each renderer's own.
    st.a = float4(unit, theta, theta > 0.0 ? 6.5 : 0.0);
    st.b = float4(0.0, saturate(2.0 * strongest), 0.45, bandTop);
    st.c = float4(pivotAWS.xz - unit * 0.5, pivotAWS.xz - unit * 0.5 + float2(-unit.y, unit.x));
    return st;
}

// All wind and push maths. Uses no pipeline matrix and no SHADERPASS test, so ForestWindProbe.compute can run it too.
// w2o is the world-to-object 3x3 (GetWorldToObjectMatrix, DOTS-instancing safe). needNormals false: the pass never reads the normal
// or the tangent (shadows, the depth prepass), so they are returned at rest. slot: the renderer's instance slot (0 = compute the
// per-instance terms here); pressedBit: the renderer's GAMEX_PRESSED_BIT (a registered instance near a body); previousTime: this is
// the motion vector pass's evaluation at last frame's time. anchor: UV2 (W1 R2: a frond's or blade's base and azimuth; 0 for trees).
// stateGiven (the test probe only): the instance's push state this frame and last frame are given, as the Instances kernel had them,
// instead of read from a slot or computed here.
void GamexWindDeformCore(
    float3 p0, float3 n0, float3 t0, float4 color, float4 anchor,
    float3 positionAWS, float3 pivotAWS, float s, float3 cameraAWS, float time,
    float4 Sway, float4 Flutter, float4 Motion, float4 Push, float4 Stem,
    float3x3 w2o, bool moonPass, bool torchPass, bool needNormals, uint slot, bool pressedBit, bool previousTime,
    bool stateGiven, GamexPushState stateNowIn, GamexPushState statePrevIn,
    out float3 outPositionOS, out float3 outNormalOS, out float3 outTangentOS, out float3 outVelocityOS)
{
    // 1. Rest pose.
    outPositionOS = p0;
    outNormalOS = n0;
    outTangentOS = t0;
    outVelocityOS = float3(0.0, 0.0, 0.0);

    // 2. Global gate: no wind and no field, nothing moves (the static regression is bit-exact in position).
    [branch] if (_GamexWind.z <= 0.0 && _GamexWind.w <= 0.0 && _GamexInteractionRect.w <= 0.0)
        return;

    bool registered = slot > 0u && (float)slot <= _GamexInstanceParams.x;
    bool pushOn = _GamexInteractionRect.w > 0.0 && (Push.x > 0.0 || Stem.x > 0.0);
    // Which instances evaluate the push at all. W1 R1: a registered instance does only when its renderer's GAMEX_PRESSED_BIT is set
    // (a body within the instance's reach, held while its press and lean recover). The box stays for a renderer without a slot; an
    // all-zero box (no writer) samples everywhere.
    if (pushOn)
    {
        if (registered)
            pushOn = pressedBit;
        else if (any(_GamexInteractionBox != 0.0))
            pushOn = all(pivotAWS.xz >= _GamexInteractionBox.xy - 4.0) && all(pivotAWS.xz <= _GamexInteractionBox.zw + 4.0);
    }

    // 3. Instance distance. W1 R2 (review of the R1 captures): a pressed instance pushes out to the field's reach whatever the wind's
    // distance fade (a body bending a sapling far off still bends it); only its wind stops beyond the fade's end.
    float d = distance(cameraAWS, pivotAWS);
    float end = _GamexWindFade.y;
    float start = _GamexWindFade.x;
    if (torchPass && _GamexWindParams.z > 0.0)
    {
        end = min(end, _GamexWindParams.z);
        start = 0.7 * end;
    }
    bool windOn = d < end;
    [branch] if (!windOn && !pushOn)
        return;
    float fade = windOn ? 1.0 - smoothstep(start, end, d) : 0.0;
    float pushFade = 1.0;
    float flutterFade = 1.0 - smoothstep(0.7 * _GamexWindFade.z, _GamexWindFade.z, d);
    bool flutterOn = windOn && Flutter.x > 0.0 && Flutter.y > 0.0 && d < _GamexWindFade.z;

    // 4. Moon cascades: mode 0 rest pose (W1 R2: except a pressed instance's push), mode 1 sway only, mode 2 full.
    if (moonPass)
    {
        if (_GamexWindFade.w < 0.5)
        {
            [branch] if (!pushOn)
                return;
            windOn = false;
            flutterOn = false;
            fade = 0.0;
        }
        else if (_GamexWindFade.w < 1.5)
        {
            flutterOn = false;
            pushOn = false;
        }
    }

    // 5 and 6. Per-instance terms: a registered renderer's from the instance textures, else computed here.
    GamexInstance inst;
#ifndef GAMEX_WIND_COMPUTE
    [branch] if (registered)
        inst = GamexInstanceLoad(slot, previousTime);
    else
#endif
        inst = GamexInstanceAnalytic(pivotAWS, s, time + _GamexWindParams.x, Sway, Flutter, Motion, w2o, true);
    // W1 R1, R2: world up and the lean direction in object space, per vertex from the matrix (the slot's texels carry phases).
    inst.up = normalize(mul(w2o, float3(0.0, 1.0, 0.0)));
    float3 leanOS = mul(w2o, float3(_GamexWind.x, 0.0, _GamexWind.y));
    float leanLength = length(leanOS);
    inst.dir = leanLength > 1e-6 ? leanOS / leanLength : float3(0.0, 0.0, 0.0);
    float W = inst.wind * fade;
    float2 gustDir = _GamexWindGust.xy;
    bool anchored = GamexIsAnchored(Sway);

    float R = color.r, G = color.g, B = color.b, A = color.a;
    float3 P = p0;
    float3 N = n0, T = t0;

    // 7. Trunk (R): lean and sway, keeping the distance to the pivot.
    [branch] if (windOn && Sway.x > 0.0 && Sway.y > 0.0)
    {
        float3 trunk = (R * fade) * inst.trunk;
        float L = length(p0);
        float3 bent = p0 + trunk;
        float bentLength = length(bent);
        P = (L > 1e-3 && bentLength > 1e-6) ? bent * (L / bentLength) : bent;
        if (needNormals)
            GamexTilt(N, T, cross(inst.up, trunk), 2.0 * length(trunk) / max(p0.y, 0.5));
    }

    // 8. Branch or frond (G): bob.
    // W1 R1: three frequencies with no common period, mixed per bough by A, a slow per-bough activity, mostly along the wind.
    // W1 R2 (review of the R1 captures): the vertical bob runs a quarter cycle off the along-wind bob, so tips trace ellipses instead of
    // a line, and the activity averages 1 (it averaged 0.75, which with R1's smaller vertical share cut the motion the vectors see).
    // A fern's fronds (and grass blades) each nod at their own phase and frequency: 4 A spreads the clump phase over a whole cycle and
    // the fronds of a clump over half of one (A is the same on both fern levels, so a switch keeps every frond's phase), and a frond's
    // frequency varies by +-25 % with a hash of A; the 6 m ripple of R1 made a patch nod as one travelling sheet.
    [branch] if (windOn && Sway.z > 0.0 && Sway.w > 0.0)
    {
        float3 across = cross(inst.up, inst.dir);
        float b1, b2, b3, bv;
        if (anchored)
        {
            float t = time + _GamexWindParams.x;
            float ownPhase = frac(4.0 * A);
            float jitter = 0.75 + 0.5 * frac(37.13 * A + 0.71 * ownPhase);
            float fB = rcp(max(Sway.w * (0.9 + 0.2 * frac(5.3 * GamexHash01(pivotAWS.xz))), 0.05)) * jitter;
            float phase = frac(fB * t + ownPhase + Motion.w * GamexHash01(pivotAWS.xz));
            GamexSC(phase, b1, bv);
            b2 = GamexS(frac(1.61 * fB * t + 1.7 * ownPhase));
            b3 = GamexS(frac(0.53 * fB * t + 1.3 * ownPhase + 0.37));
            float bo = (0.75 * b1 + 0.25 * b2) * 1.26;
            P += (G * Sway.z * W) * (inst.up * (0.35 * bv) + inst.dir * (0.25 + 0.6 * bo) + across * (0.3 * b3));
        }
        else
        {
            float ripple = frac(Motion.z * dot(positionAWS.xz, gustDir));
            GamexSC(inst.branchPhase + A - ripple, b1, bv);
            b2 = GamexS(inst.branchPhase2 + 1.7 * A - ripple);
            b3 = GamexS(inst.branchPhase3 + 1.3 * A + 0.37);
            // The mix is scaled back to one sine's RMS (1 / 0.644).
            float bo = (0.55 * b1 + 0.30 * b2 + 0.15 * b3) * 1.55;
            float vertical = (0.55 * bv + 0.30 * b2 + 0.15 * b3) * 1.55;
            float activity = 1.0 + 0.35 * GamexS(inst.activityPhase + 2.0 * A);
            P += (G * Sway.z * W * activity) * (inst.up * (0.45 * vertical) + inst.dir * (0.2 + 0.5 * bo) + across * (0.3 * b3));
        }
    }

    // 9. Flutter (B), ferns and grass only (the conifer profiles have none).
    [branch] if (flutterOn)
    {
        float fl = GamexS(inst.flutterPhase + 7.13 * A + dot(p0, float3(2.753, 1.767, 2.180)));
        P += B * Flutter.x * W * flutterFade * fl * n0;
    }

    // 10. Pushes, with exact motion vectors through Custom Velocity (the push with this frame's field and state, minus the push with
    // last frame's).
    GamexPushState stateNow = (GamexPushState)0, statePrev = (GamexPushState)0;
    bool sapling = GamexIsSapling(Stem, s);
    bool solid = !sapling && GamexHasSolidSide(Stem, s);
    if (pushOn)
    {
        [branch] if (sapling || solid)
        {
            // The push state in this frame and in last frame: given (the probe), a registered renderer's from its slot (the Instances
            // kernel), else computed here (the fallback: a sapling's lean from the field without the springs; a solid fir's sense
            // unknown, so it swings the positive way).
            if (stateGiven)
            {
                stateNow = stateNowIn;
                statePrev = statePrevIn;
            }
#ifndef GAMEX_WIND_COMPUTE
            else if (registered)
            {
                stateNow.a = LOAD_TEXTURE2D_LOD(_GamexInstanceTex, GamexInstanceTexel(slot, 3u), 0);
                statePrev.a = LOAD_TEXTURE2D_LOD(_GamexInstanceTexPrev, GamexInstanceTexel(slot, 3u), 0);
                [branch] if (sapling && (stateNow.a.w > 0.5 || statePrev.a.w > 0.5))
                {
                    stateNow.b = LOAD_TEXTURE2D_LOD(_GamexInstanceTex, GamexInstanceTexel(slot, 4u), 0);
                    stateNow.c = LOAD_TEXTURE2D_LOD(_GamexInstanceTex, GamexInstanceTexel(slot, 5u), 0);
                    statePrev.b = LOAD_TEXTURE2D_LOD(_GamexInstanceTexPrev, GamexInstanceTexel(slot, 4u), 0);
                    statePrev.c = LOAD_TEXTURE2D_LOD(_GamexInstanceTexPrev, GamexInstanceTexel(slot, 5u), 0);
                }
            }
#endif
            else if (sapling)
            {
                stateNow = GamexSaplingFallback(false, pivotAWS, s, _GamexInteractionRect, Stem);
                // An if: HLSL has no ?: on structs.
                statePrev = stateNow;
                if (_GamexInteractionRectPrev.w > 0.0)
                    statePrev = GamexSaplingFallback(true, pivotAWS, s, _GamexInteractionRectPrev, Stem);
            }
        }
    }

    [branch] if (pushOn && sapling)
    {
        // W1 R2: a passable sapling moves only by its state (texels 3 to 5): a rigid root lean, the bow, the clearance.
        float3 axisNow, axisPrev;
        float angleNow, anglePrev;
        float3 pushNow = GamexSaplingPush(p0, positionAWS, s, w2o, inst.up, pushFade, stateNow, axisNow, angleNow);
        float3 pushPrev = GamexSaplingPush(p0, positionAWS, s, w2o, inst.up, pushFade, statePrev, axisPrev, anglePrev);
        P += pushNow;
        outVelocityOS = pushNow - pushPrev;
        if (needNormals && abs(angleNow) > 1e-6)
        {
            N = normalize(GamexRotate(N, axisNow, angleNow));
            float3 rotated = GamexRotate(T, axisNow, angleNow);
            float rotatedLength = length(rotated);
            T = rotatedLength > 1e-6 ? rotated / rotatedLength : T;
        }
    }
    else
    {
        [branch] if (pushOn && anchored)
        {
            // W1 R2: a frond or blade turns rigidly about its own base by the field's press there and a little way out along it (this
            // frame's and last frame's): both points are the same for every vertex of the frond.
            float3 baseAWS = positionAWS + mul(transpose(w2o), anchor.xyz) * (s * s);
            float3 outOS = float3(cos(anchor.w), 0.0, sin(anchor.w)) * GAMEX_FROND_REACH;
            float3 midAWS = baseAWS + mul(transpose(w2o), outOS) * (s * s);
            float4 fNow, fPrev;
            float2 awayNow, awayPrev;
            GamexFrondField(false, baseAWS.xz, midAWS.xz, _GamexInteractionRect, fNow, awayNow);
            fPrev = fNow;
            awayPrev = awayNow;
            if (_GamexInteractionRectPrev.w > 0.0)
                GamexFrondField(true, baseAWS.xz, midAWS.xz, _GamexInteractionRectPrev, fPrev, awayPrev);
            [branch] if (abs(fNow.a) > 1e-4 || abs(fPrev.a) > 1e-4)
            {
                if (dot(awayPrev, awayPrev) < 0.5) awayPrev = awayNow;
                if (dot(awayNow, awayNow) < 0.5) awayNow = awayPrev;
                // The contact sway (W1 R2, in place of R1's 7 Hz shiver along the normal): the frond swings a little about its base at
                // Flutter.w Hz, Flutter.z radians at most, by how fast the press at its base changes (none around a body standing still).
                float shiverNow = 0.0, shiverPrev = 0.0;
                [branch] if (Flutter.z > 0.0 && Flutter.w > 0.0)
                {
                    float rate = abs(fNow.a - fPrev.a) * _GamexInteractionParams.x;
                    float envelope = saturate(rate / 2.0) * saturate(abs(fNow.a) * 2.0);
                    float phase = 9.1 * A + 0.37 * ceil(4.0 * A);
                    shiverNow = Flutter.z * envelope * GamexS(Flutter.w * (time + _GamexWindParams.x) + phase);
                    shiverPrev = shiverNow;
                }
                float3 axisA, axisB, axisC, axisD;
                float angleA, angleB, angleC, angleD;
                float3 pushNow = GamexFrondPush(false, _GamexInteractionRect, p0, anchor, positionAWS, s, Push, w2o, pushFade, fNow.a, awayNow, fNow.b, shiverNow,
                                                axisA, angleA, axisB, angleB);
                float3 pushPrev = GamexFrondPush(true, _GamexInteractionRectPrev, p0, anchor, positionAWS, s, Push, w2o, pushFade, fPrev.a, awayPrev, fPrev.b, shiverPrev,
                                                 axisC, angleC, axisD, angleD);
                P += pushNow;
                outVelocityOS = pushNow - pushPrev;
                if (needNormals)
                {
                    if (abs(angleA) > 1e-6) { N = GamexRotate(N, axisA, angleA); T = GamexRotate(T, axisA, angleA); }
                    if (abs(angleB) > 1e-6) { N = GamexRotate(N, axisB, angleB); T = GamexRotate(T, axisB, angleB); }
                    N = normalize(N);
                    float tLength = length(T);
                    T = tLength > 1e-6 ? T / tLength : t0;
                }
            }
        }
        else
        {
            [branch] if (pushOn)
            {
                // A solid fir (its swing) or a fir whose state is unknown (I2's lateral push).
                bool pressed = GamexField(false, positionAWS.xz, _GamexInteractionRect).a > 1e-3;
                if (solid && (abs(stateNow.a.x) > 1e-6 || abs(statePrev.a.x) > 1e-6))
                    pressed = true;
                [branch] if (pressed)
                {
                    float3 lateralNow, axisNow, lateralPrev, axisPrev;
                    float angleNow, anglePrev;
                    float3 pushNow = GamexPushAt(false, _GamexInteractionRect, p0, color, positionAWS, s, Push, Stem, stateNow.a, w2o, inst.up, pushFade,
                                                 lateralNow, axisNow, angleNow);
                    float3 pushPrev = pushNow;
                    if (_GamexInteractionRectPrev.w > 0.0)
                        pushPrev = GamexPushAt(true, _GamexInteractionRectPrev, p0, color, positionAWS, s, Push, Stem, statePrev.a, w2o, inst.up, pushFade,
                                               lateralPrev, axisPrev, anglePrev);
                    P += pushNow;
                    outVelocityOS = pushNow - pushPrev;
                    if (needNormals)
                    {
                        GamexTilt(N, T, cross(inst.up, pushNow), min(length(lateralNow) / max(p0.y, 0.15), 0.8));
                        if (abs(angleNow) > 1e-6)
                        {
                            N = normalize(GamexRotate(N, axisNow, angleNow));
                            float3 rotated = GamexRotate(T, axisNow, angleNow);
                            float rotatedLength = length(rotated);
                            T = rotatedLength > 1e-6 ? rotated / rotatedLength : T;
                        }
                    }
                }
            }
        }
    }

    // W1 I2b: the displacement quantised to a 2^-12 m grid (0.24 mm) before it is added to the rest position (spec section 9's
    // fallback (a)), in place of I1's "precise" on the final position. The depth prepass and the G-buffer pass (ZTest Equal) must
    // place every vertex identically; a last-bit difference between the two compiled passes almost never crosses a grid step. The
    // speckle count of spec 8.1 checks it (issues/W1-runtime.md section 11).
    float3 displacement = round((P - p0) * 4096.0) * (1.0 / 4096.0);
    outPositionOS = p0 + displacement;
    outNormalOS = N;
    outTangentOS = T;
}

// The full evaluation (normals, per-instance terms computed here): what the test probe runs. stateGiven: the probe hands in an
// instance's push state as the Instances kernel had it (a passable sapling's lean, bow, clearance and body; a solid fir's swing).
void GamexWindDeform(
    float3 p0, float3 n0, float3 t0, float4 color, float4 anchor,
    float3 positionAWS, float3 pivotAWS, float s, float3 cameraAWS, float time,
    float4 Sway, float4 Flutter, float4 Motion, float4 Push, float4 Stem,
    float3x3 w2o, bool moonPass, bool torchPass, bool stateGiven, GamexPushState stateNow, GamexPushState statePrev,
    out float3 outPositionOS, out float3 outNormalOS, out float3 outTangentOS, out float3 outVelocityOS)
{
    GamexWindDeformCore(p0, n0, t0, color, anchor, positionAWS, pivotAWS, s, cameraAWS, time, Sway, Flutter, Motion, Push, Stem, w2o,
                        moonPass, torchPass, true, 0u, false, false, stateGiven, stateNow, statePrev,
                        outPositionOS, outNormalOS, outTangentOS, outVelocityOS);
}

#ifndef GAMEX_WIND_COMPUTE
// Shader Graph entry (File-mode Custom Function, float precision). Gathers what only the pipeline knows, then calls GamexWindDeformCore.
void ForestWindVertex_float(
    float3 PositionOS, float3 NormalOS, float3 TangentOS, float4 VertexColor,
    float3 PositionAWS,            // Position node, Absolute World, vertex stage: the rest position
    float3 PivotAWS,               // Object node, Position (absolute)
    float3 ObjectScale,            // Object node, Scale (uniform; .y is used)
    float3 CameraAWS,              // Camera node, Position (absolute; the main camera's in every shadow pass)
    float  Time,                   // Time node (HDRP substitutes the previous time in the motion vector pass)
    float4 Sway, float4 Flutter, float4 Motion, float4 Push, float4 Stem,
    float4 Anchor,                 // W1 R2: UV node, UV2 (a frond's or blade's base offset and azimuth; 0 on trees)
    out float3 OutPositionOS, out float3 OutNormalOS, out float3 OutTangentOS, out float3 OutVelocityOS)
{
    bool moonPass = false, torchPass = false;
    // W1 I2b: passes whose fragments never read the normal or the tangent (the shadow casters, and the depth prepass and the
    // motion vector pass unless they write the normal buffer) skip the wind's normal and tangent maths.
    bool needNormals = true;
#if defined(SHADERPASS) && defined(SHADERPASS_SHADOWS)
#if (SHADERPASS == SHADERPASS_SHADOWS)
    moonPass = UNITY_MATRIX_P._m33 > 0.5;      // orthographic: the directional light's cascades
    torchPass = !moonPass;                     // perspective: the torch's spot shadow, the only other shadow caster
    needNormals = false;
#endif
#endif
#if defined(SHADERPASS) && defined(SHADERPASS_DEPTH_ONLY) && !defined(WRITE_NORMAL_BUFFER)
#if (SHADERPASS == SHADERPASS_DEPTH_ONLY)
    needNormals = false;
#endif
#endif
#if defined(SHADERPASS) && defined(SHADERPASS_MOTION_VECTORS) && !defined(WRITE_NORMAL_BUFFER)
#if (SHADERPASS == SHADERPASS_MOTION_VECTORS)
    needNormals = false;
#endif
#endif
    // HDRP passes _TimeParameters.x, or _LastTimeParameters.x in the motion vector pass's previous-frame evaluation.
    bool previousTime = Time != _TimeParameters.x;
    uint userValue = unity_RendererUserValue;
    GamexPushState none = (GamexPushState)0;
    GamexWindDeformCore(PositionOS, NormalOS, TangentOS, VertexColor, Anchor, PositionAWS, PivotAWS, ObjectScale.y, CameraAWS, Time,
                        Sway, Flutter, Motion, Push, Stem, (float3x3)GetWorldToObjectMatrix(), moonPass, torchPass, needNormals,
                        userValue & GAMEX_SLOT_MASK, (userValue & GAMEX_PRESSED_BIT) != 0u, previousTime,
                        false, none, none,
                        OutPositionOS, OutNormalOS, OutTangentOS, OutVelocityOS);
}
#endif

#endif // GAMEX_FOREST_WIND_HLSL
