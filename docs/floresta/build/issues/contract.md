# Contract and public surface: signature fixes (step zero)

Written 2026-10-13 at step zero. Unity was not started; everything below was established by compiling.

## Signature fixes

**None.** Every signature of spec section 11.2 and 11.4 compiles exactly as written. Package owners can
follow either the spec text or the stub files: they say the same thing, and `tools/check-stubs.py`
proves it word for word (104 methods and 241 declarations in total, no difference).

- `Assets/Game/Editor/Forest/ForestContracts.cs` is byte-identical to the code block of section 11.2
  (spec.md lines 934 to 1103, 8,537 bytes).
- The contract plus `stubs/WP1.cs` to `WP5.cs` plus the two greybox editor sources compile together
  with 0 errors and 0 warnings (`logs/step0-selftest.log`, case "contract + all five stubs").

## What the stubs add to the spec text (not signature changes)

The spec writes declarations in shorthand. The stubs complete them so that they compile:

| Spec text | In the stub | Why |
|---|---|---|
| Methods end in `;` | `=> throw new NotImplementedException();` | The task asks for throwing bodies |
| `public static ForestAssetEntry[] All { get; }` | `{ get { throw new NotImplementedException(); } }` | Same signature; a getter that throws instead of an auto-property that returns null |
| `public sealed class ForestAssetPostprocessor : AssetPostprocessor` with no body | `{ }` | The Unity callbacks are private and not part of the surface. They are left out on purpose: a postprocessor whose callbacks throw would break every import if it ever reached `Assets` |
| Fields whose default is given only in the trailing comment | The initialiser is written out: `UseCrossFade = true`, `CrownCoreFromLod = 99`, `LodDistanceScale = 1f`, `ForceTriplanar = Array.Empty<string>()`, `TerrainLayerCount = 8`, `AlphamapResolution = 2048`, `SpawnXZ = new Vector2(0f, -62f)` | The real files need the same values. The fields without a stated default (`UseUnityGenerator`, `ThinByGroup`, `LogPitchDegrees`) keep the C# default, which is what the comment says |
| `ForestLayout.Trail`, `WestTrail`, `EastTrail`, `Gully`, `Landmarks`, `Viewpoints` | Declared as in the spec, with no value (null) | Their contents are the tables of sections 3 and 12.2 and belong to the WP3 owner |
| `ForestBudgets { /* the constants of section 10 */ }` and `ForestAudit.Report { ... /* plus counts, totals and metrics */ }` | Copied as they are | The WP5 owner adds the members the comments announce. No other package may rely on them (spec 11.3: PlayMode tests keep their own copies of the budgets) |

## Checked and consistent

- The manifest's JSON keys and value types match the WP1 data classes field for field
  (`ForestAssetEntry`, `ForestAssetFile`, `ForestMaterialEntry`, `ForestMaterialMaps`, `ForestObjectEntry`,
  `ForestSubMeshEntry`; `fbxNodes` is an array of strings; the largest `bytes` value is 249,300,492 and
  fits the `long`).
- All 34 ids of `ForestPrefabIds` behave as the contract comment says: the 29 non-patch ids resolve to a
  manifest object as `id` or `id + "_LOD0"`; the 5 patch ids (`fern_patch_*`, `grass_patch_*`) have none.
- `probes/SurfaceUse.cs` calls every package from the others the way the "May assume from others" lists
  and the stage order of 11.5 describe (including `ForestAudit.Run(throwOnError: true)`, the cast on
  `PrefabUtility.InstantiatePrefab`, and `ForestLightingPresets.Get(...).keyBearing`). It compiles.
- `ForestMeshTools` has a method named `Transform`. That does not hide the Unity type inside the class:
  `Transform root` as a parameter or local still compiles there (tried). Only a static member access
  written as `Transform.Something` inside that class would need `UnityEngine.Transform`.

## Inconsistencies in the spec that the frozen surface does not resolve

These are not fixed here, because fixing them would change the public surface. They are recorded so
that nobody discovers them late.

1. **Section 5.3 against 11.4 and 12.3.** Section 5.3 says the test `Manifest_LoadsAndPathsAreUnique`
   checks the postprocessor's static per-asset table against the manifest. Section 11.4 gives
   `ForestAssetPostprocessor` no public members, and the row for that test in 12.3 does not list the
   check. As frozen, a test cannot read the table. What remains is `Textures_FollowPolicy` (needs the
   import), which checks the effect of the table on the real importers. If the static check is wanted,
   it needs a decision to add a public accessor to WP1; none was added.
2. **Step zero itself.** Section 11.2 says step zero creates every version-1 file in the project with
   throwing bodies. For this run the stubs live in the scratch build folder instead, one file per
   package, and each owner creates their real files. See `issues/step-zero-layout.md`.

## What a green compile does not prove

- Behaviour. Nothing was run.
- Unity's own analyzers and source generators are not part of the scripts. Tried once by hand with all
  seven attached: they reported nothing on the surface or on a deliberately sloppy MonoBehaviour.
- `JsonUtility` behaviour on the manifest: which value a field gets when its key is absent (for example
  `maps` on a material that only has `sameAs`) is not verified. Treat it as null or empty.
- The static fields of `ForestKitSpec` and `ForestLayout` are mutable, and the project enters play mode
  without a domain reload (spec Appendix A, `m_EnterPlayModeOptions: 3`). A value changed by a build
  argument or a test stays until the next script reload, so code that changes one should put it back.
