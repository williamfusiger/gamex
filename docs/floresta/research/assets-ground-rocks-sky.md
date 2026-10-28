# CC0 ground textures, rock models, HDRI skies, licensing and download mechanics

Research date: 2026-10-13. Unity 6000.6.0f1 / HDRP 17.6.0. Nothing in the project was touched and Unity was not launched.

Short names used for citations:

- `HDRP/` = `/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.high-definition/`
- `CORE/` = same folder, `com.unity.render-pipelines.core/`
- `DOC/` = `/Applications/Unity/Hub/Editor/6000.6.0f1/Documentation/en/`
- `CAT/` = `/private/tmp/claude-501/-Users-william-Documents-GitHub-gamex/e48fad55-bf2b-4ea1-ae11-7561f14da1a4/scratchpad/forest/catalog/`
- `THUMBS/` = `/private/tmp/claude-501/-Users-william-Documents-GitHub-gamex/e48fad55-bf2b-4ea1-ae11-7561f14da1a4/scratchpad/forest/thumbs/`

How the facts were obtained: Poly Haven API JSON (`/assets`, `/info`, `/files`, `/collections`, swagger), ambientCG API v2 JSON, licence pages, HEAD requests, thumbnails viewed as contact sheets, and a handful of small byte-range probes (listed in section 5.6). Anything not confirmed this way is marked **UNVERIFIED**.

---

## 1. Terrain textures

### 1.1 What every Poly Haven texture ships (verified from `/files/<id>`)

All 52 ground/rock textures I pulled have the same core set, each at 1k, 2k, 4k, 8k (some also 16k) and each as `jpg`, `png` and `exr`:

| `/files` key | File suffix | Meaning |
|---|---|---|
| `Diffuse` | `_diff_` | albedo (shading removed, per Poly Haven FAQ) |
| `nor_gl` | `_nor_gl_` | normal map, OpenGL (Y+) convention |
| `nor_dx` | `_nor_dx_` | normal map, DirectX (Y-) convention |
| `Rough` | `_rough_` | roughness, greyscale |
| `AO` | `_ao_` | ambient occlusion, greyscale |
| `arm` | `_arm_` | packed: R = AO, G = roughness, B = metallic |
| `Displacement` | `_disp_` | height, greyscale |

Extras on a few older assets: `rough_ao` (forrest_ground_01, rocks_ground_01, rocks_ground_05, rocks_ground_08, aerial_*), `Bump` + `spec` (forrest_ground_03, forrest_sand_01, brown_mud*), `Mask` (rock_path, leafy_grass, sparse_grass). Containers `blend`, `gltf`, `mtlx` also exist but are not needed.

Note the keys are mixed case (`Diffuse`, `AO`, `Rough`, `Displacement`) while file suffixes are lower case (`diff`, `ao`, `rough`, `disp`). Always read the `url` from the JSON; do not build it from the key.

Measured, not assumed:

- **`arm` packing.** On `forest_leaves_04` 1k: `arm.R` mean 0.598 = `ao` mean 0.598; `arm.G` mean 0.759 = `rough` mean 0.759; `arm.B` mean 0.015. So `arm` = (AO, Roughness, Metallic). Probe files in `CAT/probe_arm/`.
- **GL vs DX normals.** `nor_gl.G` is the inverse of `nor_dx.G` (mean |gl.G - (1 - dx.G)| = 0.063 versus 0.259 without the flip). Unity wants the GL one: "Unity uses Y+ normal maps, sometimes known as OpenGL format." (`DOC/Manual/StandardShaderMaterialParameterNormalMap.html`, line 131).
- **PNG bit depth.** 2k PNGs are 16-bit: `disp` and `rough` are 16-bit grey, `nor_gl`, `arm` and `diff` are 16-bit RGB (IHDR read from the first 64 bytes; `CAT/probe_png/`).
- **JPG normals are noisy.** The independently encoded GL and DX JPGs disagree by a mean of 0.11 (on a 0 to 1 scale) in R and in B, channels that should be identical. That is JPEG chroma loss, roughly 5% per file at 1k on high-frequency leaf litter (my interpretation of the measurement). Prefer PNG normals; re-test at 2k once downloads are allowed.
- **Tile size.** `dimensions` is "the dimensions of this asset on each axis in millimeters" (Poly Haven swagger, `CAT/licence/ph_swagger.json`). Use it directly as `TerrainLayer.tileSize` in metres.

### 1.2 Candidates looked at

Contact sheets (sphere previews): `THUMBS/sheets/tex_A.png`, `tex_B.png`, `tex_C.png`; flat lit previews of the shortlist: `THUMBS/sheets/primary_A.png`; ambientCG: `THUMBS/sheets/acg_A.png`. Full per-map size listing for all 52: `CAT/tex_table.txt`.

Every row below has all seven core maps (diffuse, nor_gl, nor_dx, rough, AO, arm, displacement). Sizes in MB. `nor_dx` is within 2% of `nor_gl`.

| Id | Tile (m) | Max | 2k diff jpg | 2k nor_gl jpg / png | 2k arm jpg / png | 2k disp jpg / png | 4k diff jpg | 4k nor_gl jpg / png | 4k arm jpg / png | 4k disp jpg / png | What it looks like |
|---|---|---|---|---|---|---|---|---|---|---|---|
| forest_leaves_04 | 1.50 | 8k | 1.5 | 2.1 / 23.1 | 1.2 / 17.6 | 0.4 / 6.4 | 4.7 | 7.1 / 90.5 | 4.0 / 65.1 | 1.1 / 24.0 | red-brown needle and leaf litter with sticks |
| forrest_ground_01 | 2.00 | 8k | 2.9 | 5.3 / 25.8 | 1.1 / 17.4 | 1.0 / 7.9 | 9.6 | 17.8 / 100.8 | 4.0 / 67.3 | 2.9 / 29.2 | sparse green grass over soil, twigs |
| forest_ground_06 | 2.11 | 8k | 3.2 | 5.5 / 23.1 | 4.0 / 17.4 | 1.8 / 6.6 | 12.2 | 20.8 / 89.8 | 14.6 / 67.5 | 5.2 / 25.2 | dark compact humus |
| forest_ground_04 | 3.15 | 8k | 4.3 | 5.0 / 9.5 | 3.3 / 6.3 | 0.7 / 5.6 | 15.8 | 18.5 / 36.1 | 11.6 / 22.5 | 1.9 / 19.8 | grey-brown soil with stones |
| mud_forest | 2.35 | 8k | 3.5 | 5.6 / 23.2 | 3.2 / 16.0 | 1.5 / 8.9 | 13.2 | 20.4 / 89.0 | 12.0 / 62.8 | 4.5 / 33.3 | very dark damp mud with small leaves |
| forest_leaves_02 | 3.00 | 8k | 1.6 | 2.2 / 10.2 | 1.0 / 6.6 | 0.3 / 6.2 | 5.5 | 8.2 / 39.5 | 3.3 / 23.1 | 0.9 / 23.5 | moss cushions in needle litter |
| rocky_trail | 2.00 | 8k | 4.0 | 5.3 / 26.6 | 3.8 / 15.1 | 1.2 / 6.2 | 14.8 | 20.7 / 106.2 | 13.5 / 51.1 | 3.5 / 23.6 | grey-brown angular gravel |
| mossy_rock | 3.00 | 8k | 4.3 | 3.8 / 8.3 | 3.6 / 6.3 | 0.6 / 6.6 | 16.1 | 15.9 / 34.0 | 14.3 / 25.5 | 1.8 / 23.0 | grey cracked rock, yellow-green moss and lichen |
| forest_leaves_03 | 2.30 | 8k | 3.2 | 5.0 / 22.6 | 3.4 / 16.6 | 1.1 / 6.2 | 10.4 | 18.5 / 89.4 | 11.1 / 59.8 | 3.2 / 23.5 | dark fine broadleaf litter |
| forrest_ground_03 | 2.00 | 8k | 3.4 | 6.5 / 10.6 | 1.2 / 7.5 | 1.2 / 8.2 | 12.5 | 23.0 / 39.9 | 5.3 / 29.4 | 3.8 / 31.2 | pale dry pine needles (also has Bump, spec) |
| brown_mud_03 | 1.30 | 8k | 2.0 | 3.6 / 8.0 | 1.0 / 6.2 | 0.3 / 8.0 | 7.9 | 10.4 / 27.6 | 3.9 / 24.3 | 0.7 / 28.9 | wet churned mud, contains human boot prints |
| ganges_river_pebbles | 2.16 | 16k | 3.1 | 4.4 / 25.7 | 2.7 / 14.9 | 1.1 / 6.2 | 11.8 | 17.5 / 103.0 | 9.7 / 53.8 | 3.3 / 23.2 | rounded creek pebbles |
| rock_face_03 | 2.70 | 16k | 3.6 | 4.3 / 22.1 | 3.1 / 16.7 | 0.8 / 5.8 | 14.6 | 15.5 / 86.5 | 12.2 / 65.9 | 2.3 / 20.8 | tan-brown fractured rock, dry |
| lichen_rock | 2.00 | 8k | 3.8 | 4.6 / 22.4 | 2.8 / 16.1 | 0.9 / 7.9 | 13.7 | 17.0 / 88.3 | 9.9 / 62.6 | 2.7 / 28.3 | dark layered rock with lichen |
| dirt_floor | 2.07 | 8k | 3.8 | 4.7 / 22.5 | 2.6 / 16.2 | 1.5 / 8.9 | 16.7 | 17.8 / 88.3 | 10.5 / 64.4 | 4.5 / 33.6 | light tan packed dirt |
| roots | 1.67 | 8k | 3.6 | 5.4 / 23.1 | 3.3 / 16.3 | 1.0 / 6.1 | 13.9 | 21.0 / 91.5 | 12.7 / 63.4 | 3.2 / 22.7 | beige cracked clay with thin roots; reads dry, not forest |
| forest_floor | 2.14 | 8k | 5.2 | 5.1 / 22.8 | 3.7 / 17.3 | 1.8 / 9.2 | 21.0 | 17.6 / 88.3 | 15.2 / 68.2 | 5.2 / 34.8 | sandy soil with bright orange autumn leaves |
| leaves_forest_ground | 1.26 | 8k | 4.1 | 5.0 / 22.7 | 3.4 / 16.6 | 1.5 / 6.3 | 14.8 | 19.2 / 87.4 | 12.6 / 64.7 | 4.5 / 22.4 | dense orange and yellow autumn leaves |
| forest_ground_05 | 2.00 | 16k | 2.6 | 4.6 / 22.4 | 3.0 / 16.1 | 1.2 / 6.3 | 10.7 | 17.0 / 87.6 | 10.8 / 63.9 | 3.6 / 23.8 | smooth dark brown soil |
| brown_mud_leaves_01 | 1.30 | 8k | 4.3 | 5.4 / 23.1 | 3.5 / 17.3 | 1.7 / 6.6 | 14.8 | 18.9 / 89.8 | 12.5 / 66.2 | 4.9 / 24.7 | red mud with green ground cover |
| river_small_rocks | 2.90 | 16k | 3.7 | 4.8 / 9.2 | 3.2 / 6.1 | 0.8 / 7.4 | 13.2 | 16.6 / 34.1 | 11.2 / 21.4 | 2.1 / 25.5 | fine brown river gravel |
| rocks_ground_01 | 2.00 | 8k | 2.5 | 4.9 / 9.0 | 0.8 / 6.0 | 0.6 / 7.1 | 8.1 | 17.8 / 34.4 | 3.1 / 23.5 | 1.6 / 26.2 | pale chunky stone rubble |

Poly Haven has no pure moss texture and no convincing root-mat texture. The closest are `forest_leaves_02` (categorised "Mossy Ground") and `roots` (looks like dry clay). Use root meshes for roots.

ambientCG fills the moss gap. All ambientCG materials listed have color, normal (both GL and DX in the zip), roughness, AO and displacement; there is no packed map. Zip sizes from the API:

| Id | Method | Tile | 2K-JPG zip | 4K-JPG zip | 2K-PNG zip | 4K-PNG zip | Look |
|---|---|---|---|---|---|---|---|
| Ground037 | photogrammetry | 210 (cm, unit UNVERIFIED) | 36 MB | 133 MB | 69 MB | 267 MB | bright yellow-green moss over damp soil; 455k downloads |
| Ground068 | photogrammetry | not given | 34 MB | 118 MB | 67 MB | 256 MB | moss streaks on red-brown lumpy dirt |
| Ground024 | photogrammetry | not given | 33 MB | 113 MB | 67 MB | 253 MB | grey soil, sticks, traces of moss |
| Ground023 | photogrammetry | not given | 33 MB | 106 MB | 67 MB | 246 MB | dark soil with scattered leaves |
| Moss001 | photogrammetry | 45 | 36 MB | 128 MB | 69 MB | 262 MB | close-up moss, speckled pink and purple, very small tile |
| Moss002 | procedural | not given | 38 MB | 150 MB | 70 MB | 277 MB | uniform lush green moss carpet |
| Rock064 | photogrammetry | not given | 27 MB | 102 MB | 68 MB | 267 MB | stacked grey stones with moss in the joints |

### 1.3 Recommended layer set (8 layers, all Poly Haven)

HDRP's terrain shader takes "up to eight Terrain Layers" (`HDRP/Documentation~/terrain-lit-material.md`, line 3; shader tag `"SplatCount" = "8"` at `HDRP/Runtime/Material/TerrainLit/TerrainLit.shader`, line 97).

| # | Role | Id | Tile (m) | Mean preview colour (sRGB) | Where to paint it |
|---|---|---|---|---|---|
| 0 | base litter | forest_leaves_04 | 1.50 | 0.44 / 0.35 / 0.28 | default everywhere under trees |
| 1 | grassy clearing | forrest_ground_01 | 2.00 | 0.50 / 0.47 / 0.36 | clearings and wherever grass meshes stand |
| 2 | humus | forest_ground_06 | 2.11 | 0.28 / 0.22 / 0.18 | dense shade, around trunks, animal paths |
| 3 | stony soil | forest_ground_04 | 3.15 | 0.37 / 0.32 / 0.27 | slopes, around boulders |
| 4 | wet mud | mud_forest | 2.35 | 0.23 / 0.19 / 0.16 | hollows, stream banks |
| 5 | mossy ground | forest_leaves_02 | 3.00 | 0.50 / 0.43 / 0.29 | north sides, around rocks and logs |
| 6 | gravel | rocky_trail | 2.00 | 0.46 / 0.42 / 0.37 | trail, creek bed, scree |
| 7 | rock | mossy_rock | 3.00 | 0.43 / 0.42 / 0.37 | steep faces by slope angle |

Why this set blends: layers 0 and 3 are in both Poly Haven collections "Pine Forest" and "A Verdant Trail", layer 6 is in "Pine Forest", and the recommended rock models come from the same two collections, so scan lighting and colour grading are consistent. The colour means above are from identically lit preview renders, so they are a fair relative guide: three dark layers (2, 4 and optionally forest_leaves_03) and five mid layers. Layers 1 and 5 are the brightest; darken them 15 to 25% with `TerrainLayer.diffuseRemapMax` for a horror mood.

Swaps:

- Conifer-heavy forest: replace layer 1 with `forrest_ground_03` (dry pine needles).
- Broadleaf-heavy forest: replace layer 1 with `forest_leaves_03`.
- Real creek: replace layer 6 with `ganges_river_pebbles`.
- Stronger moss: replace layer 5 with ambientCG `Ground037` and tint it down; it is noticeably more saturated than the Poly Haven set.
- Avoid `brown_mud_03` (visible boot prints on an uninhabited island), `forest_floor` and `leaves_forest_ground` (bright autumn orange), `roots`.

A leaner first pass is layers 0, 2, 3, 5, 6, 7 (six layers), adding 1 and 4 when clearings and water exist.

Resolution: 2k. A 2k map over a 1.5 to 3.15 m tile is 650 to 1365 px per metre, more than a first-person camera at 1.7 m needs. Download cost for the eight layers at 2k: 89 MB all-JPG, or 332 MB with JPG diffuse plus PNG normal, arm and displacement (recommended). At 4k the same choices cost 321 MB and 1264 MB.

### 1.4 What Unity needs from these maps

- **Terrain layer mask map** = R metallic, G AO, B height, A smoothness (`HDRP/Runtime/Material/TerrainLit/TerrainLit.shader`, lines 98 to 101; tooltip at `HDRP/Editor/Material/TerrainLit/TerrainLitGUI.cs`, line 60; consumed at `HDRP/Runtime/Material/TerrainLit/TerrainLit_Splatmap.hlsl`, lines 250 to 254, height read from `.z` at lines 174 to 182).
- Poly Haven gives AO, roughness, metallic in `arm` and height separately, so each layer needs one repacked texture: `R = 0`, `G = arm.R`, `B = disp`, `A = 1 - arm.G`. Smoothness is `1 - roughness` (`CORE/ShaderLibrary/CommonMaterial.hlsl`, lines 50 to 53).
- Height-based blending reads that blue channel (`HDRP/Documentation~/terrain-lit-material-inspector-reference.md`, line 17), so the displacement map is worth packing; it is what stops layer borders looking airbrushed.
- Mask maps must be imported with sRGB off (`HDRP/Documentation~/Mask-Map-and-Detail-Map.md`, line 7).
- `TerrainLayer` fields that exist in this Unity version (pages under `DOC/ScriptReference/`): `diffuseTexture`, `normalMapTexture`, `maskMapTexture`, `tileSize`, `tileOffset`, `normalScale`, `metallic`, `smoothness`, `diffuseRemapMin`, `diffuseRemapMax`, `maskMapRemapMin`, `maskMapRemapMax`.
- **UNVERIFIED:** which code turns on the `_TERRAIN_8_LAYERS`, `_MASKMAP` and `_NORMALMAP` keywords for the non-ShaderGraph TerrainLit material. No C# in the HDRP package sets them (only `Editor/Material/TerrainLit/ShaderGraph/TerrainLitSubTarget.cs` names them), so it is presumably the native terrain renderer. Check the first headless render with more than four layers.

---

## 2. Rock and cliff models

### 2.1 Facts common to all of them

- Formats per model: `fbx`, `gltf` (+ one `.bin`), `blend`, `usd` (`.usdc`), each at 1k, 2k, 4k, 8k. The FBX byte size is identical at 2k and 4k, so the mesh is evidently the same and only the texture references differ.
- Texture maps: `Diffuse`, `nor_gl`, `nor_dx`, `Rough`, `AO`, `arm`, as jpg, png, exr. Some also have `Displacement` (rock_moss_set_02, rock_07, rock_09, stone_01, most namaqualand) and `Mask` (stone_01, namaqualand_rocks_01, namaqualand_stones_01).
- **`polycount` is triangles, and for LOD models it is the sum of all LODs.** For models without LODs the API number equals the triangle count in the glTF exactly (rock_moss_set_01: 63127 = 63127). For LOD models the glTF holds only LOD0 and the API number is larger (boulder_01: API 123976, glTF 66122). The remainder fits three further LODs at half, quarter and eighth (rock_09: 23280 - 12416 = 10864 = 6208 + 3104 + 1552). Per-LOD counts beyond LOD0 are inferred, not read.
- The FBX holds the LODs as separate objects named `<id>_LOD0` to `<id>_LOD3` (read from the FBX of boulder_01, rock_07, rock_09, stone_01). Unity builds a LOD Group from that naming on import: "Add the suffix _LODX to the name of each mesh ... Unity automatically creates a LOD Group component" (`DOC/Manual/lod-group-configure.html`). The Poly Haven names are on the object nodes, and the geometry nodes have other names (for example `realCap_Model_1.003`); that Unity keys on the object name is expected but **UNVERIFIED** until the first import.
- The API `dimensions` field disagrees with the glTF bounds for some models (rock_face_01: API 7.1 x 5.6 x 5.0 m, glTF 4.95 x 3.56 x 3.83 m; rock_face_02 and coast_rocks_05 also differ). The sizes below are glTF bounds. Measure `Renderer.bounds` after import rather than trusting either.

Contact sheets: `THUMBS/sheets/rocks_A.png`, `rocks_B.png`, `rocks_C.png`, `rocks_big.png`. Full table: `CAT/rock_table.txt`.

### 2.2 Forest-suitable models

W x H x D in metres from glTF bounds (Y up). "Buried" is how far the mesh extends below its pivot.

| Id | Pieces | LOD0 triangles | API total | LODs in FBX | Size (m) | FBX | Texel density at 8k (px/m, unit inferred) | Reads as |
|---|---|---|---|---|---|---|---|---|
| rock_moss_set_01 | 6 rocks | 5000 to 16548 each, 63127 total | 63127 | none | each 1.8 to 2.7 wide, 1.1 to 1.8 high, 0.4 to 0.7 buried | 2.5 MB | 606 | **forest**: grey-brown boulders with moss; "Pine Forest" collection |
| rock_moss_set_02 | 7 rocks | 7894 to 9826 each, 57647 total | 57647 | none | each 1.2 to 2.2 wide, 0.5 to 1.3 high | 2.0 MB | 796 | **forest**: smaller mossy grey rocks |
| rock_face_01 | 1 | 20174 | 20174 | none | 4.95 x 3.56 x 3.83 | 0.7 MB | 855 | **forest outcrop**: layered grey-tan rock, moss on top |
| rock_face_02 | 1 | 29566 | 29566 | none | 2.71 x 2.47 x 2.13 | 1.1 MB | 1157 | **forest outcrop**: layered rock, most moss of the cliffs |
| boulder_01 | 1 | 66122 | 123976 | LOD0 to LOD3 | 1.27 x 1.00 x 1.83 | 6.2 MB | 2771 | neutral: jagged red-brown with lichen; works if tinted darker |
| mountainside | 1 | 153472 | 287755 | yes | 10.21 x 10.52 x 5.25 | 13.2 MB | 505 | neutral: craggy brown cliff with sparse lichen; backdrop wall |
| rock_07 | 1 | 14844 | 27830 | LOD0 to LOD3 | 0.17 x 0.14 x 0.32 | 1.1 MB | 14662 | neutral small stone, dark red-brown |
| rock_09 | 1 | 12416 | 23280 | LOD0 to LOD3 | 0.07 x 0.03 x 0.14 | 0.8 MB | 40150 | neutral hand-size stone; good throwable |
| stone_01 | 1 | 53520 | 71916 | LOD0 to LOD3 | 0.15 x 0.07 x 0.09 | 3.0 MB | 36264 | pink quartz; odd in a forest |
| coastal_cliff_02 | 1 | 943284 | 1768655 | yes | 40.93 x 10.05 x 8.65 | 74.4 MB | 261 | grey cliff with grass on top; could wall a ravine, but heavy |

Texture download sizes for the recommended ones (MB):

| Id | 2k diff jpg | 2k nor_gl jpg / png | 2k arm jpg / png | 4k diff jpg | 4k nor_gl jpg / png | 4k arm jpg / png |
|---|---|---|---|---|---|---|
| rock_moss_set_01 | 0.6 | 0.8 / 23.6 | 0.4 / 2.2 | 2.4 | 3.0 / 93.4 | 1.1 / 7.3 |
| rock_moss_set_02 | 0.7 | 1.0 / 24.4 | 0.5 / 2.8 | 2.8 | 3.4 / 96.5 | 1.7 / 9.5 |
| boulder_01 | 3.5 | 4.4 / 21.1 | 2.2 / 13.1 | 12.7 | 18.3 / 80.2 | 7.4 / 36.7 |
| rock_face_01 | 3.0 | 3.8 / 17.5 | 2.5 / 12.2 | 10.9 | 14.5 / 60.5 | 9.5 / 41.1 |
| rock_face_02 | 3.2 | 3.8 / 18.9 | 2.8 / 13.9 | 11.5 | 13.3 / 62.1 | 10.1 / 44.5 |
| rock_07 | 2.1 | 2.7 / 13.6 | 0.8 / 5.0 | 7.3 | 9.5 / 44.4 | 2.2 / 10.3 |
| rock_09 | 2.0 | 2.4 / 13.5 | 0.8 / 5.2 | 6.5 | 7.6 / 41.4 | 2.3 / 11.2 |
| mountainside | 3.4 | 4.4 / 19.7 | 2.7 / 13.7 | 12.9 | 15.5 / 66.5 | 10.1 / 43.8 |

### 2.3 Models that read as desert or coast

| Group | Ids | LOD0 triangles | Size (m) | Verdict |
|---|---|---|---|---|
| Namaqualand boulders | namaqualand_boulder_02, _03, _04, _05, _06 | 97964, 64826, 59066, 89618, 109114 | 0.7 to 2.5 wide, 0.5 to 1.9 high | orange-tan sun-baked granite; desert. Only use if recoloured |
| Namaqualand small sets | namaqualand_boulders_01 (2 pieces), namaqualand_rocks_01 (4), namaqualand_stones_01 (5) | 40850, 85716, 70124 | pieces 0.55 to 0.59, 0.20 to 0.23 and 0.08 to 0.15 long | mixed quartz pebbles; desert |
| Namaqualand cliffs | namaqualand_cliff_01, namaqualand_cliff_02 | 94369, 194080 | 8.3 x 5.0 x 4.4 and 20.2 x 7.2 x 6.6 | rounded tan granite stacks; desert, though cliff_01 is the only large "pile of boulders" shape available |
| Coastal cliffs | coastal_cliff_01, coastal_cliff_04 | 461824, 1537926 | 92 and 87 long | low sandy sea ledges; coast, and very heavy |
| Coast slabs | coast_land_rocks_02, _03, _04, coast_rocks_01, _02, _03, coast_line_01, _02, sand_rocks_small_01 | 540k to 1.29M each | 4.6 to 60 across, under 3.3 high | sand and shore-rock terrain patches; keep for a later beach, FBX files are 40 to 98 MB each |
| Coast boulder | coast_rocks_05 | 771724 | 4.0 x 1.3 x 3.8 | pale sandstone slab; coast, far too dense for its size |

### 2.4 Things the build must handle

- **Rock sets are several rocks in one file, laid out side by side.** rock_moss_set_01 has objects `rock_moss_set_01_rock01` to `rock06` roughly 3 m apart. Treat each child as its own prefab and zero its local position.
- **Cover height.** Above their pivot the rock_moss_set_01 rocks stand only about 0.6 to 1.1 m: crouch cover. Standing cover (1.8 m and up) needs rock_face_01, rock_face_02, mountainside, or the moss rocks scaled 1.5x to 2x.
- **Texture sharpness.** A rock set shares one atlas, so at 4k rock_moss_set_01 is about 300 px/m and at 2k about 150 px/m. Use 4k for the two moss sets and add a tiling detail normal if they look soft up close; boulder_01 is fine at 2k (about 690 px/m).
- **No LODs on the best forest rocks** (both moss sets, both rock faces). Unity 6 can generate them at import: `ModelImporter.generateMeshLods` ("If true, Unity generates Mesh LOD levels for this mesh during import", `DOC/ScriptReference/ModelImporter-generateMeshLods.html`).
- **Imported transforms.** Every FBX object carries `Lcl Rotation (-90, 0, 0)` and `Lcl Scaling (100, 100, 100)`, with file `UnitScaleFactor 1.0`, `UpAxis 1` (Y) (parsed from rock_09's FBX; Blender FBX IO 4.0.2, FBX 7400). `ModelImporter.bakeAxisConversion` exists to bake the rotation into the mesh (`DOC/ScriptReference/ModelImporter-bakeAxisConversion.html`). The resulting child scale in Unity is **UNVERIFIED**; place by measured world bounds, never by assuming scale 1.

---

## 3. HDRI skies

### 3.1 Why "Pure Sky" versions

Poly Haven has 59 HDRIs in the "Pure Skies" category: the same captures with the ground and horizon objects edited out ("Edited sky-only version of ...", `info` field). Any other HDRI bakes distant trees, hills or fields into the image, which would sit at infinity behind the real 3D forest. Forest-interior HDRIs (59 in "Forest & Woodland") have the same problem and are only useful as lighting reference.

One caveat seen in the thumbnails: the lower half of a pure-sky image is a blurred mirror of the sky, not a dark ground. Sky light therefore also arrives from below. Terrain and trees block most of it in a real scene, but undersides of objects may read too bright and too blue until occlusion or probes are in place. How much this shows in HDRP is **UNVERIFIED**. (`HDRISky.upperHemisphereOnly` does not help: it only concerns the cloud-distortion flowmap, `HDRP/Runtime/Sky/HDRISky/HDRISky.cs`, lines 37 to 39.)

`evs_cap` is "the number of exposure brackets captured ... the difference between the brightest and darkest shots, an indication of dynamic range" (swagger); the FAQ adds that all Poly Haven HDRIs are unclipped and that EV figures are not comparable across sites. `whitebalance` is the capture white balance in Kelvin. Poly Haven does not publish absolute luminance, so intensity must be set in HDRP (section 3.3).

Contact sheet: `THUMBS/sheets/hdri_A.png`. Formats: every HDRI as `.hdr` and `.exr` at 1k, 2k, 4k, 8k, 16k, and 24k where the source allows.

### 3.2 Candidates per mood

| Mood | Id | EVs | WB (K) | Max res | 2k hdr / exr (MB) | 4k hdr / exr (MB) | 8k hdr / exr (MB) | Look |
|---|---|---|---|---|---|---|---|---|
| Overcast day | **overcast_soil_puresky** | 12 | 6900 | 16384 x 8192 | 4 / 17 | 16 / 66 | 57 / 253 | heavy, dark, textured grey cloud; the moodiest |
| Overcast day | kloofendal_overcast_puresky | 6 | 5303 | 24576 x 12288 | 4 / 17 | 16 / 66 | 62 / 256 | softer grey-blue cloud with a faint sun glow |
| Overcast day | mud_road_puresky | 6 | not given | 16384 x 8192 | 4 / 18 | 16 / 68 | 56 / 262 | bright white overcast; flat and cheerful |
| Overcast, fog | kloofendal_misty_morning_puresky | 12 | 5313 | 24576 x 12288 | 3 / 21 | 12 / 81 | 48 / 314 | featureless fog sky; pairs with volumetric fog |
| Dusk, blue hour | **qwantani_dusk_2_puresky** | 12 | 5303 | 24576 x 12288 | 4 / 18 | 17 / 67 | 66 / 263 | violet-blue sky, pink afterglow on one horizon |
| Dusk, blue hour | qwantani_dusk_1_puresky | 12 | 5303 | 24576 x 12288 | 4 / 17 | 15 / 66 | 58 / 256 | cooler blue, one dark cloud band |
| Dusk, overcast | kloppenheim_01_puresky | 8 | not given | 24576 x 12288 | 3 / 18 | 12 / 67 | 48 / 261 | grey sunset through cloud, small warm patch |
| Dawn | qwantani_dawn_puresky | 12 | 5303 | 24576 x 12288 | 4 / 17 | 16 / 66 | 63 / 258 | blue with a clean warm horizon band |
| Moonlit night | **qwantani_moon_noon_puresky** | 19 | 4339 | 24576 x 12288 | 4 / 18 | 18 / 68 | 72 / 266 | deep blue, moon high overhead, stars |
| Moonlit night | qwantani_moonrise_puresky | 20 | 4088 | 24576 x 12288 | 5 / 18 | 18 / 69 | 72 / 269 | deep blue, moon low; long shadows |
| Moonlit night | kloppenheim_02_puresky | 19 | 3776 | 24576 x 12288 | 5 / 19 | 21 / 74 | 80 / 290 | hazy pale blue-grey, moon visible, few stars |
| Starry, no moon | **qwantani_night_puresky** | 23 | 4901 | 24576 x 12288 | 5 / 18 | 20 / 70 | 78 / 272 | Milky Way; bright town-light glows on the horizon |
| Night, cloudy | kloppenheim_07_puresky | 22 | 3776 | 24576 x 12288 | 5 / 19 | 18 / 71 | 70 / 278 | moon behind broken cloud; tagged "artificial light" |

Not pure sky, listed only because they are the usual "starry night" picks: moonless_golf (23 EVs), rogland_clear_night (12), satara_night_no_lamps (8), dikhololo_night (12). All have visible ground, trees or lit horizons.

Picks: overcast_soil_puresky, qwantani_dusk_2_puresky, qwantani_moon_noon_puresky, with qwantani_night_puresky as the moonless option (its horizon glows need the tree line to hide them). The moon-high sky matches the greybox, whose moon light is pitched 48 degrees (`Assets/Game/Editor/GreyboxSceneBuilder.cs`, line 121).

Resolution: `.hdr` is a quarter of the `.exr` size for these files and Unity imports both ("When importing from an EXR or HDR file containing HDR information, the Texture Importer automatically chooses the right HDR format", `DOC/Manual/ImportingTextures.html`). 4k is enough for cloud skies seen through a canopy. Take the two night skies at 8k: a 4k panorama is about 11 px per degree, coarser than a 1600 px wide view at 60 degrees (about 27 px per degree), and stars smear.

### 3.3 Using them from the scene builder

- The sky texture must be a cubemap: `public CubemapParameter hdriSky` (`HDRP/Runtime/Sky/HDRISky/HDRISky.cs`, line 30). Importer pages that exist: `TextureImporter.textureShape`, `TextureImporterShape.TextureCube`, `TextureImporter.generateCubemap`, `TextureImporterGenerateCubemap.Cylindrical`.
- Sky type: `SkyType.HDRI = 1` (`HDRP/Runtime/Sky/VisualEnvironment.cs`, lines 83 to 86). The greybox already sets `skyType` this way with `SkyType.Gradient` (`GreyboxSceneBuilder.cs`, line 126).
- Intensity fields (`HDRP/Runtime/Sky/SkySettings.cs`): `rotation` (107), `skyIntensityMode` (110), `exposure` (113), `multiplier` (116), `desiredLuxValue` (125); modes `Exposure`, `Lux`, `Multiplier` (44 to 51).
- **Do not use Lux mode from a headless script as is.** Lux mode divides by `upperHemisphereLuxValue` (`SkySettings.cs`, line 220; the docs say the same: "The final multiplier HDRP applies for intensity is Desired Lux Value / Upper Hemisphere Lux Value", `HDRP/Documentation~/hdri-sky-volume-override-reference.md`, line 73). Its default is 1.0 (`SkySettings.cs`, line 119), and the real value is only computed by the inspector (`HDRP/Editor/Sky/HDRISky/HDRISkyEditor.cs`, `GetUpperHemisphereLuxValue`, lines 134 to 163, called on an inspector change at 210 to 212). A script-built profile in Lux mode would therefore multiply the sky by the raw lux number. Use `Exposure` or `Multiplier` and calibrate against renders, or compute the hemisphere integral in the builder and set `upperHemisphereLuxValue` yourself.
- Reference levels from HDRP's own table (`HDRP/Documentation~/Physical-Light-Units.md`, lines 49 to 56): overcast sky at midday 1000 to 2000 lux; moonlight with a clear night sky under 1 lux; starry night without moon 0.002 lux. No dusk figure is given there.
- **UNVERIFIED:** the cubemap face size Unity derives from a 4k or 8k lat-long image, and the resulting memory.

---

## 4. Licensing

### 4.1 Poly Haven (https://polyhaven.com/license, fetched 2026-10-13; copy in `CAT/licence/polyhaven_license.html`)

> "All assets (HDRIs, textures and 3D models) on this site are the original work of Poly Haven staff, or artists who willingly and directly donate/sell their work to Poly Haven."

> "Our assets are all licensed as CC0, which is effectively Public Domain even in jurisdictions that do not support the Public Domain."

> "You can use our assets for any purpose, including commercial work."

> "You do not need to give credit or attribution when using them (although it is appreciated)."

> "You can redistribute them, share them around, include them when sharing your own work, or even in a product you sell."

FAQ (https://docs.polyhaven.com/en/faq): "Can I include these assets in a product I sell? Yes. The only thing you can't do is claim to be the original author or re-license them."

### 4.2 ambientCG (https://docs.ambientcg.com/license/; `https://ambientcg.com/license` redirects there)

> "All ambientCG assets are provided under the Creative Commons CC0 1.0 Universal License. This applies to the downloadable asset files and the material preview renders shown for each asset on the site."

> "You can copy, modify, distribute and perform the assets, even for commercial purposes, all without asking permission."

> "You can include the raw files in your project, for example a video game."

> "You don't need to give credit but I would of course appreciate it, if you did it anyways."

### 4.3 Exceptions and conditions

- **Trademarks and third-party rights are not covered by CC0.** CC0 1.0 legal code, section 4: "No trademark or patent rights held by Affirmer are waived, abandoned, surrendered, licensed or otherwise affected by this document", and the affirmer "disclaims responsibility for clearing rights of other persons that may apply to the Work". Neither site's licence page lists specific excepted assets. This matters for props showing brands or for HDRIs of recognisable places; it does not touch ground scans, rocks or sky-only HDRIs.
- **Poly Haven's website content is not CC0.** Terms of Service 4.1 and 4.2 on the licence page: "All content on this website, excluding the CC0 assets themselves, is protected by intellectual property laws", with examples including "Logos", "Asset example renders", "User submitted renders". The thumbnails downloaded for this research are therefore reference only and must not ship. ambientCG's preview renders are CC0.
- **Poly Haven API terms** (https://github.com/Poly-Haven/Public-API/blob/master/ToS.md; copy in `CAT/licence/polyhaven_api_tos.md`): 2.1 "The API is free to access and use by anyone ... for any purpose, including commercial use, at no charge." 2.4 "all API calls must be made with a unique 'Referer' header or user-agent that matches your software name". 2.5 attribution "applies only to use of the live API service - it does not apply to the CC0 assets themselves once downloaded or self-hosted". The page https://polyhaven.com/our-api carries a note dated 18 July 2026: "The Poly Haven API is now free for everyone - including commercial use." A build script that downloads once and commits or caches the files only needs the unique User-Agent.
- Poly Haven site ToS 3.2 forbids "Web scraping or data mining without express permission" of the website. Use the API, not the HTML pages.
- ambientCG's API page warns the API is run by one person and "potentially not as reliable or stable as needed for an 'enterprise-level' application". Cache what is downloaded.

---

## 5. Download mechanics

### 5.1 Endpoints (all returned 200 with `User-Agent: gamex-research/1.0`)

| Purpose | URL |
|---|---|
| Catalogue | `https://api.polyhaven.com/assets?t=textures` (868 assets), `?t=hdris` (997), `?t=models` (521) |
| One asset's metadata | `https://api.polyhaven.com/info/<id>` |
| File tree with `url`, `size`, `md5`, `include` | `https://api.polyhaven.com/files/<id>` |
| Collections | `https://api.polyhaven.com/collections` |
| Schema | `https://api.polyhaven.com/api-docs/swagger.json` |
| Thumbnail | `https://cdn.polyhaven.com/asset_img/thumbs/<id>.png?width=256&height=256` (5 of 52 came back as WebP despite the `.png` name) |
| Flat preview render | `https://cdn.polyhaven.com/asset_img/primary/<id>.png?height=380` |
| ambientCG search | `https://ambientcg.com/api/v2/full_json?type=Material&q=<term>&limit=<n>&include=downloadData,dimensionsData,tagData,mapData,imageData` |

There is no server-side zip on Poly Haven: the website builds zips in the browser with a service worker (FAQ, "Model and texture downloads are not working for me"). A script must fetch the individual files.

### 5.2 URL patterns returned by `/files`

Texture sets:

```
https://dl.polyhaven.org/file/ph-assets/Textures/<jpg|png|exr>/<1k|2k|4k|8k>/<id>/<id>_<map>_<res>.<ext>
  map = diff | nor_gl | nor_dx | arm | ao | rough | disp   (plus rough_ao, bump, spec, mask on some)
  e.g. .../Textures/jpg/2k/forest_leaves_04/forest_leaves_04_diff_2k.jpg
       .../Textures/png/2k/forest_leaves_04/forest_leaves_04_nor_gl_2k.png
```

Models:

```
https://dl.polyhaven.org/file/ph-assets/Models/fbx/<res>/<id>/<id>_<res>.fbx
https://dl.polyhaven.org/file/ph-assets/Models/<jpg|png|exr>/<res>/<id>/<id>_<map>_<res>.<ext>
https://dl.polyhaven.org/file/ph-assets/Models/gltf/<res>/<id>/<id>_<res>.gltf
https://dl.polyhaven.org/file/ph-assets/Models/gltf/8k/<id>/<id>.bin        (same .bin for every resolution)
https://dl.polyhaven.org/file/ph-assets/Models/usd/<res>/<id>/<id>_<res>.usdc
```

HDRIs:

```
https://dl.polyhaven.org/file/ph-assets/HDRIs/<hdr|exr>/<1k|2k|4k|8k>/<id>_<res>.<ext>
https://dl.polyhaven.org/file/ph-assets/HDRIs/<hdr|exr>/16k%2B/<id>_<16k|24k>.<ext>     (16k and 24k share the "16k+" folder)
https://dl.polyhaven.org/file/ph-assets/HDRIs/extra/Tonemapped%20JPG/<id>.jpg
```

ambientCG:

```
https://ambientcg.com/get?file=<AssetId>_<1K|2K|4K|8K>-<JPG|PNG>.zip
  -> 302 -> https://acg-download.struffelproductions.com/file/ambientCG-Web/download/<AssetId>_<token>/<same name>.zip
```

### 5.3 HEAD checks

| URL (tail) | Status | Content-Length | Matches API `size` | Content-Type |
|---|---|---|---|---|
| `Models/fbx/2k/rock_moss_set_01/rock_moss_set_01_2k.fbx` | 200 | 2652988 | yes | application/octet-stream |
| `Models/jpg/2k/rock_moss_set_01/rock_moss_set_01_diff_2k.jpg` | 200 | 649415 | yes | image/jpeg |
| `Models/png/2k/rock_moss_set_01/rock_moss_set_01_nor_gl_2k.png` | 200 | 24703035 | yes | image/png |
| `Textures/jpg/2k/forrest_ground_01/forrest_ground_01_diff_2k.jpg` | 200 | 3077552 | yes | image/jpeg |
| `Textures/png/2k/forrest_ground_01/forrest_ground_01_arm_2k.png` | 200 | 18233670 | yes | image/png |
| `HDRIs/hdr/4k/kloofendal_overcast_puresky_4k.hdr` | 200 | 16953534 | yes | image/vnd.radiance |
| `HDRIs/exr/4k/kloofendal_overcast_puresky_4k.exr` | 200 | 69187040 | yes | image/aces |
| `HDRIs/hdr/16k%2B/kloofendal_overcast_puresky_16k.hdr` | 200 | 211592363 | yes | image/vnd.radiance |
| `Models/fbx/2k/rock_09/rock_09_2k.fbx` | 200 | 890732 | yes | application/octet-stream |
| `ambientcg.com/get?file=Ground037_2K-JPG.zip` | 302 then 200 | 37874033 | yes | application/zip |
| `ambientcg.com/get?file=Moss001_2K-JPG.zip` | 302 then 200 | 37548890 | yes | application/zip |

All Poly Haven responses send `accept-ranges: bytes` and `x-bz-content-sha1`. `/files` gives an `md5` per file ("MD5 checksum for verifying file integrity", swagger). curl needs `-L` for ambientCG.

### 5.4 What the FBX expects

Read from the FBX files themselves (tail bytes of rock_09, boulder_01, rock_07, rock_face_01, stone_01, rock_moss_set_01):

- **Yes, textures are referenced by relative path, in a `textures/` folder beside the FBX.** Example from `rock_09_2k.fbx`: `RelativeFilename` = `textures/rock_09_diff_2k.jpg`, `textures/rock_09_nor_gl_2k.exr`, `textures/rock_09_rough_2k.exr`. The absolute `FileName` is a path on Poly Haven's build server and is useless.
- This matches the `include` map that `/files` returns for the FBX, whose keys are those relative paths. Expected layout:

```
<id>_<res>.fbx
textures/<id>_diff_<res>.jpg
textures/<id>_nor_gl_<res>.exr
textures/<id>_rough_<res>.exr      (or .jpg on older assets)
```

- The set is not uniform. The FBX references three slots named `base_color_texture`, `normalmap_texture`, `roughness_texture`. The normal is always EXR. Roughness is EXR on 2024 assets (boulder_01, rock_07, rock_09, rock_face_01) and JPG on 2021 assets (rock_moss_set_01 read from the FBX; rock_moss_set_02 per its `include` list). `stone_01` shows no diffuse reference in the bytes probed, although its `include` list has one. `rock_moss_set_01` writes the path with a Windows backslash (`textures\rock_moss_set_01_diff_2k.jpg`).
- The FBX never references the `arm` or AO maps; the glTF does use `arm` (as `metallicRoughnessTexture`) on 2024 assets.
- The material is exported as `ShadingModel` "Phong", one material per file, named after the asset.

**Do not rely on the FBX material in Unity.** HDRP's importer hook (`HDRP/Editor/AssetProcessors/FBXMaterialDescriptionPostprocessor.cs`) creates an HDRP/Lit material but only maps `DiffuseColor` to `_BaseColorMap` (lines 85 to 94), `Bump` or `NormalMap` to `_NormalMap` (104 to 117) and a scalar `Shininess` to `_Smoothness` (148 to 154). There is no roughness-texture or mask-map path, so imported rocks would have flat smoothness and no AO. Whether Unity resolves the `textures/` relative path, the backslash variant, or the EXR normal on import is **UNVERIFIED** and does not need to be found out if materials are built in code.

### 5.5 ambientCG zip contents

Read from the zip central directory (last 4 KB of each file): `Ground037_2K-JPG.zip` and `Moss001_2K-JPG.zip` each hold 11 stored (uncompressed) entries:

```
<Id>_2K-JPG_Color.jpg   _NormalGL.jpg   _NormalDX.jpg   _Roughness.jpg   _AmbientOcclusion.jpg   _Displacement.jpg
<Id>_2K-JPG.usdc   <Id>_2K-JPG.blend   <Id>_2K-JPG.mtlx   <Id>_2K-JPG.tres   <Id>.png
```

Normal maps are about 10 MB each at 2K-JPG, which is why the zips are larger than the Poly Haven JPG sets. Use `_NormalGL`.

### 5.6 Disclosure: small probes beyond catalogue JSON and thumbnails

To answer the FBX and channel questions without bulk downloads I fetched: byte ranges of six FBX files (16 KB head of one, 64 to 96 KB tails), the last 4 KB of two ambientCG zips, the first 64 bytes of seven PNGs, five 1k JPG maps of forest_leaves_04 (0.3 to 0.6 MB each), and 32 glTF descriptors (3 to 12 KB JSON each). All are under `CAT/probe_*`, `CAT/gltf/` and `CAT/gltf_tex/` and were only parsed as data.

---

## Recommendations for the build

1. **Download with a manifest, not hand-built URLs.** `CAT/starter_manifest.json` lists 77 files (URL, byte size, md5, destination name) for the kit below: 1196 MB in total. Send a unique User-Agent, follow the manifest, verify md5, and keep the raw downloads in a cache folder outside `Assets`.

   | Part | Contents | Size |
   |---|---|---|
   | Terrain, 8 layers at 2k | forest_leaves_04, forrest_ground_01, forest_ground_06, forest_ground_04, mud_forest, forest_leaves_02, rocky_trail, mossy_rock; diff jpg + nor_gl png + arm png + disp png | 332 MB |
   | Terrain alternates at 2k | forest_leaves_03, forrest_ground_03, ganges_river_pebbles | 128 MB |
   | Rocks | rock_moss_set_01 and _02 at 4k, rock_face_01 and _02 at 4k, boulder_01, rock_07, rock_09 at 2k; fbx + diff jpg + nor_gl png + arm png | 536 MB |
   | Skies | overcast_soil_puresky, kloofendal_overcast_puresky, qwantani_dusk_2_puresky at 4k hdr; qwantani_moon_noon_puresky, qwantani_night_puresky at 8k hdr | 199 MB |

   If disk in the repository matters, taking every map as JPG cuts the terrain part to 89 MB and the rock part to about 120 MB, at the cost of the JPEG noise measured in 1.1.

2. **Fetch the FBX alone and build materials in code.** Skip the FBX `include` textures (EXR normals, no AO). Create one `HDRP/Lit` material per asset: `_BaseColorMap` (line 10 of `HDRP/Runtime/Material/Lit/Lit.shader`), `_NormalMap` (24), `_MaskMap` (14), then call `HDMaterial.ValidateMaterial(material)` (`HDRP/Runtime/Material/MaterialExtension.cs`, line 369) so keywords are set. Lit mask map is R metallic, G AO, B detail mask, A smoothness (`HDRP/Documentation~/Mask-Map-and-Detail-Map.md`, lines 15 to 20). Consider `ModelImporterMaterialImportMode.None` so no throwaway materials are created.

3. **Add one repack step in the editor script.** For rocks: `R = 0, G = arm.R, B = 1, A = 1 - arm.G`. For terrain layers: `R = 0, G = arm.R, B = disp, A = 1 - arm.G`. `ImageConversion.LoadImage` reads PNG and JPG from bytes into 8-bit textures without going through the importer (`DOC/ScriptReference/ImageConversion.LoadImage.html`), and `ImageConversion.EncodeToPNG` exists. Import the result with sRGB off. Do this in Unity rather than with macOS image tools, which premultiply alpha and would corrupt the RGB channels.

4. **Use only `nor_gl`.** Import as `TextureImporterType.NormalMap`. Never mix in `nor_dx`; if a DX map is ever the only one available, `TextureImporter.flipGreenChannel` exists.

5. **Terrain layers.** Set `tileSize` from the table in 1.3. Enable height-based blend so the packed displacement drives transitions. Start with six layers (0, 2, 3, 5, 6, 7) and confirm in the first capture that layers beyond the fourth render before adding the rest. Darken layers 1 and 5 with `diffuseRemapMax`. Tiles of 1.5 to 3 m will repeat visibly in open ground; rely on layer mixing, ground cover and scattered rocks to break it.

6. **Rocks.** Primary kit: the 13 mossy rocks of rock_moss_set_01 and _02 (one prefab per child, positions zeroed), rock_face_01 and rock_face_02 for outcrops and standing cover, boulder_01 tinted darker for variety, rock_09 and rock_07 (low LODs) for throwable and scatter stones. Turn on `generateMeshLods` for the models that ship without LODs. Leave Namaqualand and coast models out of the forest; keep the coast set in mind for a beach later. mountainside or coastal_cliff_02 can wall the playable area but cost 153k and 943k triangles at LOD0.

7. **Skies.** One volume profile per mood using the three bold picks in 3.2. Import as cubemap, set intensity by `Exposure` or `Multiplier` (not `Lux` unless the builder computes `upperHemisphereLuxValue`), calibrate with `SceneCapture` renders against the lux table in 3.3, and rotate the sky so its moon or glow lines up with the directional light.

8. **Licence hygiene.** Keep a `THIRD_PARTY_ASSETS.md` listing each id, source URL and "CC0 1.0"; not required, but both sites ask for it politely and it documents provenance. Do not ship Poly Haven thumbnails or preview renders.

## Not verified

- Which component enables `_TERRAIN_8_LAYERS`, `_MASKMAP`, `_NORMALMAP` on a script-created TerrainLit material.
- That Unity's LOD Group auto-setup triggers on Poly Haven's object-level `_LODn` names, and the triangle counts of LOD1 to LOD3.
- Final transform scale and rotation of imported Poly Haven FBX objects in Unity, and real-world sizes where the API and glTF disagree.
- Cubemap face size and memory for 4k and 8k lat-long HDRIs in Unity.
- Unit of ambientCG `dimensionX/Y` (assumed centimetres) and of Poly Haven `texel_density` (assumed pixels per metre at maximum resolution).
- JPG normal-map quality at 2k and 4k (only measured at 1k).
- ambientCG 3D rock models were not evaluated; the v2 `type=3DModel` query returned materials.
- The glTF convention that `metallicRoughnessTexture` uses G for roughness and B for metallic is from the glTF 2.0 specification, not read locally; the `arm` packing itself was measured.

## Files produced

| Path | What |
|---|---|
| `CAT/ph_textures.json`, `ph_hdris.json`, `ph_collections.json` | catalogues (the models catalogue was already there) |
| `CAT/files/<id>.json` | `/files` responses for 101 assets |
| `CAT/tex_table.txt`, `CAT/rock_table.txt` | per-map sizes |
| `CAT/gltf/<id>.gltf` | glTF descriptors used for exact triangle counts and bounds |
| `CAT/starter_manifest.json` | download list with sizes and md5 |
| `CAT/licence/` | licence, ToS, FAQ, API docs, CC0 legal code as fetched |
| `CAT/acg/` | ambientCG API responses |
| `THUMBS/sheets/*.png` | labelled contact sheets: `tex_A/B/C`, `primary_A`, `acg_A`, `rocks_A/B/C`, `rocks_big`, `hdri_A` |
| `.../forest/probes/assets_tools/` | helper scripts (`fetch_files.sh`, `tex_table.py`, `model_table.py`, `gltf_stats.py`, `zip_tail.py`, `fbx_head.py`, `montage.swift`, `chanstats.swift`) |
