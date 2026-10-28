# WP1 (Assets in): what differs from the spec text, and why

Written 2026-10-13. Unity was not started. Nothing here changes the frozen surface: the public
surface of the six real files equals `stubs/WP1.cs` member for member (`out/WP1/public-surface.*.txt`),
except `ForestAssetPostprocessor.GetVersion()`, which is a Unity callback override.

No contract type was insufficient. WP1 references the contract only: it compiles with the contract
and no other package (`out/WP1/alone/`).

## 1. The postprocessor table is checked by `ForestImport.Run`, not by a test

- What: spec 5.3 says the test `Manifest_LoadsAndPathsAreUnique` checks the postprocessor's static
  per-asset table against the manifest. Spec 11.4 gives the postprocessor no public member, so no test
  can read the table (`issues/contract.md`, inconsistency 1).
- Did: `ForestAssetPostprocessor.TableProblems(manifest)` is `internal`. `ForestImport.Run` calls it
  first and throws, listing every difference, before anything is packed or built. Today: 0 differences.
- The table lists the two rock faces (M6) at 4096 as well, because the manifest gives them
  `maxTextureSize: 4096`. Through M4 only the two moss rock sets are on disk: 4 textures at 4096.

## 2. `fbx_objects.py`

- A third exit status: 1 = the file could not be measured (unreadable, not binary FBX, a transform the
  tool does not understand, unknown id). 0 and 2 are as specified.
- It refuses, with status 1, more than "a rotation other than (-90, 0, 0)": also an axis system other
  than right-handed Y-up, a parent node that rotates or scales, a pre/post rotation, pivots or offsets, a
  geometric transform, and a non-positive scale. A parent that only translates is accepted. A
  non-uniform scale is applied per axis, which is exact, not a guess.
- Bounds are taken over the control points that polygons use. A loose point is ignored and reported.
- An FBX mesh that the manifest does not list is a mismatch. The nodes of `fbxNodes` are the exception:
  they are measured and printed, not compared, and not stored (the manifest has no field for them).
- `--write` fills `vertices` with the FBX control points per material (the glTF number counted split
  vertices) and `hasUv0` with the usable-UV rule of spec 5.5 applied to the FBX's first UV layer, and
  appends one sentence to the entry's `note` saying so. It refuses if a manifest object has no node in
  the FBX: that needs a manifest edit by hand.

## 3. `fetch_assets.py`

- Extra option `--dry-run`. `--id` may be repeated, and an asset named by id is fetched whatever its
  milestone (an explicit `--milestone` still narrows).
- The manifest is treated as input: a URL must start with `https://dl.polyhaven.org/`, and a `relPath`
  must be a file under `PolyHaven/<id>/` with no `..`. Otherwise it stops before downloading anything.
- curl gets the flags of spec 5.1 plus `--proto =https --proto-redir =https --connect-timeout 30
  --speed-limit 1024 --speed-time 120 --silent --show-error`.
- A model's mesh is fetched and measured before its textures. The measuring runs on every fetch, also
  when the FBX was already in place.
- A file that is in place but differs from the manifest is downloaded again and replaced only after
  the new copy has passed the size and md5 check.

## 4. `ForestManifest`

- "Cached" means: until the file changes. `All` compares the file's write time and length on each call
  and reads it again when they differ, because `fbx_objects.py` rewrites the file while the Editor may
  be open. `FindOwner` therefore works by object name, not by reference.
- After loading, no string, array or `maps` object is null (an absent key reads as "" or as empty),
  whatever `JsonUtility` leaves there.
- "Malformed" is checked on load: ids, relPaths, object and material names unique; kind known; models
  have `boundsSpace` "source-rh-yup", `objectsSource` fbx or gltf, one mesh file, materials whose maps
  are files of the model, `sameAs` and `noUvFallback` naming materials of the same model, objects with
  three-number bounds and sub-meshes that sum to the triangle count; textures have a layer index 0 to 7
  used once and the four map tags; skies have both files and a preset.

## 5. `ForestStamps`

- `IsCurrent` is also false when the product key is a path under `Assets/` that no longer exists.
- An `internal` `Forget(product)` exists beside the three public methods. The packer and the material
  factory drop a product's stamp before they rewrite it, and record the new one only once the product
  is safe: after the PNG is on disk, and for materials and terrain layers after `AssetDatabase.SaveAssets`.
  A run that stops half-way (also a `-forestForce` run) therefore never leaves a stamp that says
  "current" on work that was not finished.
- `ForestImport` keeps its "this download was verified" stamps under keys that start with `verified:`,
  so they cannot collide with a stamp another package keeps for the same path.

## 6. `ForestTexturePacker`

- "Sizes differ between the maps of a set" also covers the maps Unity imports directly (base colour,
  normal): their size is read from the PNG or JPG header. If a header cannot be read the packer logs a
  warning and goes on; it does not throw.
- Foliage coverage is the share of texels at or above the material's alpha cutoff.
- The mask is read from the alpha file's red channel; from its alpha channel only if red is constant
  and alpha is not.
- `MeanFoliageColor` is computed from the packed atlas when asked (and cached), so it also works in a
  later session. It throws if the atlas has not been packed.

## 7. `ForestMaterials`

- Crown core: `BuildAll` does not build it (WP1 may assume nothing from others, so it cannot read
  `ForestKitSpec.CrownCoreFromLod`). `GetCrownCore` creates the asset the first time it is asked, if the
  twig atlas is packed, and `BuildAll` keeps an existing one up to date.
- Tint variant 1 exists for `fern_02`, `fir_sapling_medium_twigs` and `fir_tree_01_twig` only.
  `Get(name, 1)` is null for every other material.
- The triplanar variant also sets `_UVMappingMask = (0, 0, 0, 0)` and `_InvTilingScale = 1`, which is
  what HDRP's inspector writes next to `_UVBase = 5` (`LitSurfaceInputsUIBlock.cs:520-545`).
- An asset that exists is updated in place (reset to the shader's defaults, recipe applied), never
  deleted and recreated, so GUIDs are stable. Files: `<material>.mat`, `<material>__tint1.mat`,
  `<material>__triplanar.mat`, `<material>__crowncore.mat`, `ForestTerrain.mat`,
  `TerrainLayers/<id>.terrainlayer`.
- If the foliage diffusion profile cannot be loaded, or the material ends with a zero profile hash,
  the material is Standard and a warning is logged (spec 5.5, "guard").

## 8. Downloads of this run (M1 and M2)

- 68 files, 555.2 MB, every size and md5 equal to the manifest (`logs/WP1-fetch-M2.log`, and again
  with the final tools in `logs/WP1-fetch-M2-verify.log`).
- All eight models match the manifest, so `--write` was never needed. For `tree_stump_01`,
  `tree_stump_02` and `dead_tree_trunk_02` the tool changed `objectsSource` from "gltf" to "fbx" in
  the project's manifest copy. Those three lines are the only change to that file.
- The firs were not fetched. `fir_sapling_medium` and `fir_tree_01` still say "gltf".
