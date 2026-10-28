# CC0 vegetation models for the forest base scenery

Research date: 2026-10-13. Author: vegetation-models researcher. Nothing in the Unity project was touched and Unity was not launched.

## How to read this report

Every claim carries one of three labels.

- **[V]** verified: I read it in a file that is on disk. The evidence path is given.
- **[I]** inferred: follows from verified facts plus a stated assumption.
- **UNVERIFIED**: could not be confirmed in this phase. Treat as a risk, not a fact.

All evidence lives under `/private/tmp/claude-501/-Users-william-Documents-GitHub-gamex/e48fad55-bf2b-4ea1-ae11-7561f14da1a4/scratchpad/forest` (called `FOREST` below).

| Evidence | Path | What it is |
|---|---|---|
| Catalogue | `FOREST/catalog/ph_models.json` | 521 Poly Haven models (given) |
| Per-asset API answers | `FOREST/catalog/ph_info/<id>.json`, `FOREST/catalog/ph_files/<id>.json` | `/info` and `/files` for 61 ids (list in `FOREST/catalog/veg_ids.txt`) |
| glTF manifests | `FOREST/catalog/ph_gltf/<id>.gltf` | The JSON part of each glTF only (6 to 33 KB each, no geometry). Gives object names, exact triangle and vertex counts, bounding boxes, vertex attributes, materials |
| FBX probes | `FOREST/catalog/ph_fbx_probe/<id>.head16k.bin`, `<id>.tail96k.bin` | First 16 KB and last 96 KB of each FBX by HTTP range request. Gives FBX version, axes, unit scale, object counts, object names, object transforms, texture links |
| PNG probes | `FOREST/catalog/ph_png_probe/*.hdr64` | First 64 bytes of 327 texture PNGs (IHDR: size, bit depth, channels) |
| Blend probe | `FOREST/catalog/ph_blend_probe/moss_01_2k.blend` | One 174 KB .blend, used to find where LODs live |
| Poly Haven standards | `FOREST/catalog/ph_web/models.html`, `license.html` | docs.polyhaven.com model standards page and polyhaven.com/license |
| ambientCG | `FOREST/catalog/acg_veg/all_*.json`, `detail_veg.json`, `*.zip.tail` | Full asset list (2,902 assets) and zip directory listings |
| Thumbnails | `FOREST/thumbs/veg/<id>.png` (61), `FOREST/thumbs/acg_veg/*.png` (8) | 512 px renders. I opened and looked at all 61 Poly Haven ones and 5 of the ambientCG ones |
| Derived text | `FOREST/research/_gltf_full.txt`, `_fbx_headers.txt`, `_fbx_tails.txt`, `_png_headers.txt`, `_tree_submeshes.txt` | Parser output the tables below are generated from |
| Scripts | `FOREST/scripts/veg_summary.py`, `gltf_manifest.py`, `fbx_probe.py`, `fbx_tail.py`, `veg_recipe.py`, `veg_tables.py`, `doctext.py` | Re-runnable with `python3 -I` |

Unity install is `/Applications/Unity/Hub/Editor/6000.6.0f1` (called `UNITY`). HDRP source is `UNITY/Unity.app/Contents/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.high-definition` (called `HDRP`, version 17.6.0 per its `package.json`).

### Deviation from the limits of this phase (please read)

My first FBX header probe asked for bytes 0 to 32767 of `pine_tree_01_2k.fbx`. The server answered `HTTP 200` with the whole 648 MB body instead of `206`, and curl received 559 MB before its 40 second timeout stopped it. That was an unintended bulk transfer. I scanned the received bytes once for object and layer names, then truncated the file to its first 64 KB (`FOREST/catalog/ph_fbx_probe/pine_tree_01.first64k.bin`) and deleted the rest. Every later request used `curl --max-filesize`, which aborts before the body when the server ignores the range. All 449 later range requests to the same host succeeded under that guard (60 FBX heads, 61 FBX tails, 327 PNG headers, 1 test), including the tail of that same file. The response headers of the failed request are kept in `FOREST/catalog/ph_fbx_probe/pine_tree_01.head.hdr`.

Small non-thumbnail files I fetched on purpose, all under 400 KB each: 61 glTF JSON manifests (0.68 MB total), FBX head and tail ranges (6.7 MB total), 64-byte PNG headers, one 174 KB `.blend`, one 23 KB `.usdc`, two 64 KB zip tails from ambientCG, and ambientCG catalogue pages (about 21 MB of JSON, more than I expected because the type filter is ignored, see section 1).

---

## 1. What CC0 vegetation exists (Poly Haven and ambientCG)

### Poly Haven

**[V]** The catalogue has 521 models. Vegetation and woody debris are 61 of them, counted by the `category` field in `FOREST/catalog/ph_models.json`. I fetched `/info`, `/files`, the glTF manifest and the FBX probes for 59 of those (all except the two tiny `dead_quiver_branch_*` twigs) and for the two `rock_moss_set_*` rock sets that ship inside the `pine_forest` collection, which makes the 61 ids recorded in section 2.

| Category | Count | Ids |
|---|---|---|
| Nature/Trees/Coniferous Trees | 2 | `fir_tree_01`, `pine_tree_01` |
| Nature/Trees/Saplings | 4 | `fir_sapling`, `fir_sapling_medium`, `pine_sapling_small`, `pine_sapling_medium` |
| Nature/Trees/Broadleaf Trees | 2 | `jacaranda_tree`, `tree_small_02` |
| Nature/Trees/Coastal Trees | 3 | `island_tree_01`, `island_tree_02`, `island_tree_03` |
| Nature/Trees/Desert Trees | 2 | `quiver_tree_01`, `quiver_tree_02` |
| Nature/Plants/Ferns | 1 | `fern_02` |
| Nature/Plants/Shrubs & Bushes | 9 | `shrub_01` to `shrub_04`, `shrub_sorrel_01`, `nettle_plant`, `didelta_spinosa`, `searsia_burchellii`, `searsia_lucida` |
| Nature/Plants/Tropical Plants | 3 | `anthurium_botany_01`, `calathea_orbifolia_01`, `pachira_aquatica_01` |
| Nature/Plants/Potted Plants | 3 | `potted_plant_01`, `potted_plant_02`, `potted_plant_04` |
| Nature/Plants/Wildflowers | 8 | `celandine_01`, `dandelion_01`, `periwinkle_plant`, `flower_empodium`, `flower_gazania`, `flower_heliophila`, `flower_stinkkruid`, `flower_ursinia` |
| Nature/Plants/Succulents & Cacti, Desert & Arid | 5 | `cheiridopsis_succulent`, `crystalline_iceplant`, `leipoldtia_schultzei`, `othonna_cerarioides`, `wild_rooibos_bush` |
| Nature/Ground Cover & Moss | 5 | `grass_medium_01`, `grass_medium_02`, `grass_bermuda_01`, `weed_plant_02`, `moss_01` |
| Nature/Logs, Branches & Roots | 14 | `tree_stump_01`, `tree_stump_02`, `dead_tree_trunk`, `dead_tree_trunk_02`, `dead_quiver_trunk`, `pine_roots`, `root_cluster_01`, `root_cluster_02`, `single_root`, `dry_branches_medium_01`, `dead_quiver_branch_01`, `dead_quiver_branch_02`, `bark_debris_01`, `dry_quiver_leaf` |

**[V]** Collections: `pine_forest` has exactly 16 models and `verdant_trail` exactly 25, matching the brief. `pine_forest` is 14 vegetation or debris models plus two mossy rock sets. `verdant_trail` is 11 rock or cliff models and 14 vegetation or debris models.

**[V]** There is no palm, tree fern, cycad, bamboo, banana, liana, vine, ivy, mangrove, mushroom or large-leaved jungle plant anywhere in the catalogue. I searched names, tags and categories of all 521 models for those words; the only hits were fruit, a machete and wine bottles.

**[V]** All Poly Haven assets are CC0 (`FOREST/catalog/ph_web/license.html`: "Our assets are all licensed as CC0"). Downloads need no account: every request above was anonymous.

### ambientCG

**[V]** ambientCG has 2,902 assets, of which 34 are of type `3DModel`. 32 of those are food. The only two vegetation-related models are `3DTreeStump001` (a sawn stump with roots on a patch of forest floor) and `3DStick001` (a short birch log). There are no trees, shrubs, ferns or grass meshes.

**[V]** Each of those two comes as a zip per quality tier (`LQ`, `SQ`, `HQ`) and texture size (1K, 2K, 4K, JPG or PNG). The `LQ-1K-JPG` zips contain `.obj`, `.mtl`, `.usdc`, and `Color`, `NormalGL`, `NormalDX`, `AmbientOcclusion` JPGs (read from the zip central directory, `FOREST/catalog/acg_veg/*.zip.tail`). There is no roughness map and no FBX. Sizes: stump 5.4 MB (LQ-1K) to 181.9 MB (HQ-4K-PNG).

**[V]** ambientCG also has 39 foliage texture atlases: 30 `LeafSet`, 8 `Foliage`, 1 `PineNeedles001`. They are flat cut-out sheets with `color`, `normal`, `roughness`, `opacity`, and on newer ones `scattering` maps. They are textures, not meshes. They would only be useful if an editor script builds card geometry for them.

**[V]** API note for whoever scripts ambientCG: `type=3DModel` is silently ignored by `https://ambientcg.com/api/v2/full_json` (the echoed `searchQuery.dataType` comes back empty and all 2,902 assets are returned). `type=Atlas` works. I found the models by paging through everything and filtering on the `dataType` field locally.

### Poly Haven API as actually used

**[V]** `https://api.polyhaven.com/info/<id>` and `https://api.polyhaven.com/files/<id>` work as described. `/files` is a tree: top-level keys are map names (for example `Diffuse`, `nor_gl`, `arm`, `Alpha`, `leaves_alpha`, `twig_diff`) and the four formats `blend`, `fbx`, `gltf`, `usd`. Below that comes the resolution (`1k`, `2k`, `4k`, `8k`), then the file type, then `{url, size, md5}`. Format entries also carry `include`, the list of sibling files the package needs, each with `url`, `size` and `md5`.

**[V]** Every map can be downloaded on its own in `jpg`, `png` and `exr`. This matters: the official "package" for a format is not the best download (section 6.4).

**[V]** Thumbnails: `https://cdn.polyhaven.com/asset_img/thumbs/<id>.png?width=512&height=512` returns a PNG with alpha.

---

## 2. Per-candidate technical record

How each column was obtained:

- **Objects in FBX, LOD levels in FBX**: object names parsed out of the last 96 KB of the FBX (`FOREST/research/_fbx_tails.txt`). The counts match the `Definitions` block in the first 16 KB (`FOREST/research/_fbx_headers.txt`).
- **LOD0 triangles, per-object triangles, height**: summed from the index and position accessors in the glTF manifest (`FOREST/research/_gltf_full.txt`). glTF is Y-up in metres, so the Y extent is the height. For 59 of 61 assets the glTF object names equal the FBX LOD0 object names (ignoring the `_LOD0` suffix). The two exceptions, `shrub_01` and `shrub_04`, are flagged in the table.
- **Catalogue polycount**: the `polycount` field as published. It is shown only to demonstrate that it cannot be trusted (section 6.9).
- **Diffuse PNG channels**: colour type and bit depth from the PNG header of the 2k diffuse files (for multi-material assets, the leaf or twig diffuse and the plain `Diffuse` where one exists). `RGBA16` means the file has an alpha channel. Whether that channel really holds the leaf mask is UNVERIFIED (I read headers, not pixels).
- **Recipe size**: FBX mesh plus, for each material, `diff` JPG, `nor_gl` JPG, `arm` JPG, and the alpha map as PNG. This is what I recommend downloading (section 6.4).

All 61 assets exist in all four formats (`fbx`, `gltf`, `blend`, `usd`). None has an animation or a skin in its glTF.

### 2.1 Geometry, LODs, maps

| Asset id | Collection(s) | Objects in FBX | LOD levels in FBX | LOD0 triangles (all objects) | Per-object LOD0 triangles | Per-object height (m) | Catalogue polycount | Alpha map | Diffuse PNG channels | Translucency map | Max res |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **Conifers (pine_forest)** | | | | | | | | | | | |
| `pine_tree_01` | pine_forest | 3 | LOD0 | 17,182,252 | 4,234,432 - 6,951,524 | 14.88 - 20.37 | 17,427,094 | twig_alpha | RGBA16 | none | 8k |
| `fir_tree_01` | pine_forest | 3 | LOD0 | 6,982,937 | 505,494 - 4,176,819 | 14.06 - 18.93 | 7,853,731 | twig_alpha | RGB16 | none | 8k |
| `pine_sapling_medium` | pine_forest | 3 | LOD0 | 6,038,139 | 1,502,888 - 2,662,894 | 7.01 - 11.49 | 9,784,670 | twig_alpha | RGBA16 | none | 8k |
| `fir_sapling_medium` | pine_forest | 3 | LOD0 | 1,533,513 | 420,822 - 685,046 | 5.91 - 8.83 | 2,308,249 | twigs_alpha | RGBA16 | none | 8k |
| `pine_sapling_small` | pine_forest | 3 | none (unsuffixed) | 398,144 | 122,235 - 142,506 | 0.96 - 1.30 | 398,144 | twig_alpha | RGB16 | none | 8k |
| `fir_sapling` | pine_forest | 3 | none (unsuffixed) | 433,021 | 124,743 - 157,402 | 0.74 - 1.30 | 433,021 | twigs_alpha | RGB16 | none | 8k |
| **Broadleaf / coastal trees** | | | | | | | | | | | |
| `jacaranda_tree` | verdant_trail | 1 | LOD0 | 3,863,832 | single object | 19.47 | 312,356 | leaves_alpha | RGBA16 | none | 8k |
| `tree_small_02` | verdant_trail | 1 | LOD0 | 2,062,487 | single object | 4.56 | 4,652,585 | leaves_alpha | RGB16/RGBA16 | none | 8k |
| `island_tree_01` | verdant_trail | 1 | LOD0 | 1,599,403 | single object | 5.03 | 3,729,692 | leaves_alpha | RGB16/RGBA16 | none | 8k |
| `island_tree_02` | - | 1 | LOD0 | 1,072,213 | single object | 3.41 | 1,762,064 | leaves_alpha | RGB16/RGBA16 | none | 8k |
| `island_tree_03` | - | 1 | LOD0 | 2,085,320 | single object | 2.62 | 4,760,490 | leaves_alpha | RGB16/RGBA16 | none | 8k |
| `quiver_tree_01` | namaqualand | 1 | LOD0 | 150,124 | single object | 2.72 | 269,522 | none | RGB16 | none | 8k |
| `quiver_tree_02` | namaqualand | 1 | LOD0 | 82,074 | single object | 1.47 | 153,887 | none | RGB16 | none | 8k |
| **Shrubs** | | | | | | | | | | | |
| `searsia_burchellii` | namaqualand | 9 | LOD0, LOD1, LOD2 | 616,336 | 44,516 - 361,558 | 1.94 - 3.24 | 1,427,042 | Alpha | RGBA16 | none | 8k |
| `searsia_lucida` | namaqualand | 7 | LOD0 | 377,192 | 6,302 - 113,405 | 0.74 - 2.34 | 841,648 | Alpha | RGBA16 | none | 8k |
| `othonna_cerarioides` | namaqualand | 28 | LOD0, LOD1, LOD2, LOD3 | 118,149 | 3,124 - 57,067 | 0.46 - 1.97 | 651,641 | Alpha | RGBA16 | none | 8k |
| `didelta_spinosa` | namaqualand | 9 | LOD0, LOD1, LOD2 | 417,187 | 38,888 - 234,552 | 0.94 - 2.07 | 694,671 | Alpha | RGBA16 | translucency | 8k |
| `wild_rooibos_bush` | namaqualand | 5 | none (unsuffixed) | 27,519 | 1,193 - 13,151 | 0.31 - 0.54 | 76,521 | Alpha | RGBA16 | none | 8k |
| `shrub_02` | verdant_trail | 4 (non-standard object rotations) | LOD0 | 27,254 | 5,188 - 9,234 | 1.17 - 1.71 | 52,317 | Alpha | RGB16 | none | 4k |
| `pachira_aquatica_01` | smugglers_cove | 8 | none (unsuffixed) | 76,914 | 584 - 24,341 | 0.52 - 1.65 | 76,914 | leaves_alpha | RGB16 | none | 8k |
| **Ferns, herbs, tropical leaf plants** | | | | | | | | | | | |
| `fern_02` | pine_forest, smugglers_cove | 4 | none (unsuffixed) | 6,232 | 784 - 2,384 | 0.21 - 0.43 | 6,232 | Alpha | RGB16 | none | 8k |
| `anthurium_botany_01` | smugglers_cove, the_shed | 6 | none (unsuffixed) | 67,152 | 7,664 - 15,808 | 0.14 - 0.50 | 4,134 | Alpha | RGB16 | none | 8k |
| `calathea_orbifolia_01` | smugglers_cove | 5 | none (unsuffixed) | 16,688 | 1,802 - 5,904 | 0.13 - 0.42 | 16,688 | Alpha | RGB16 | none | 8k |
| `potted_plant_02` | - | 3 | none (unsuffixed) | 69,806 | 5,184 - 53,930 | 0.04 - 0.63 | 69,806 | leaves_alpha | RGBA16 | none | 8k |
| `potted_plant_01` | - | 4 | none (unsuffixed) | 176,226 | 5,440 - 106,900 | 0.53 - 0.86 | 96,030 | leaves_alpha | RGBA16 | none | 8k |
| `shrub_01` | verdant_trail | 9 | LOD0 | 156,012 (glTF has 1 objects; FBX differs) | single object | 0.40 | 282,224 | Alpha | RGB8 | none | 4k |
| `shrub_03` | verdant_trail | 4 | LOD0 | 8,287 | 1,790 - 2,385 | 0.26 - 0.40 | 16,955 | Alpha | RGB16 | none | 4k |
| `shrub_04` | verdant_trail | 4 | LOD0 | 27,327 (glTF has 1 objects; FBX differs) | single object | 0.22 | 47,813 | Alpha | RGB16 | none | 4k |
| `nettle_plant` | the_shed | 6 | LOD0 | 31,304 | 1,088 - 8,080 | 0.02 - 0.22 | 46,195 | Alpha | RGBA16 | none | 8k |
| `weed_plant_02` | the_shed | 5 | LOD0 | 11,116 | 1,766 - 2,967 | 0.04 - 0.07 | 17,062 | Alpha | RGBA16 | none | 8k |
| `periwinkle_plant` | - | 6 | LOD0 | 34,350 | 1,478 - 11,252 | 0.16 - 0.41 | 67,388 | opacity | RGBA16 | translucency | 4k |
| `shrub_sorrel_01` | smugglers_cove | 11 | none (unsuffixed) | 3,319 | 63 - 990 | 0.02 - 0.06 | 3,319 | Alpha | RGB16 | none | 8k |
| `celandine_01` | - | 5 | LOD0 | 8,966 | 442 - 4,301 | 0.12 - 0.19 | 1,746,764 | Alpha | RGBA16 | none | 8k |
| `dandelion_01` | - | 20 | LOD0, LOD1, LOD2, LOD3 | 54,764 | 2,080 - 23,542 | 0.05 - 0.16 | 64,099 | Alpha | RGBA16 | none | 8k |
| **Grass and moss** | | | | | | | | | | | |
| `grass_medium_01` | pine_forest | 17 | LOD0 | 24,730 | 28 - 6,422 | 0.05 - 0.32 | 1,606,633 | Alpha | RGBA16 | none | 8k |
| `grass_medium_02` | verdant_trail | 5 | none (unsuffixed) | 7,842 | 714 - 2,489 | 0.16 - 0.40 | 1,043,926 | Alpha | RGBA16 | none | 4k |
| `grass_bermuda_01` | smugglers_cove | 21 | none (unsuffixed) | 941 | 8 - 268 | 0.02 - 0.15 | 223,596 | Alpha | RGBA16 | none | 8k |
| `moss_01` | pine_forest | 12 | LOD0 | 204 | 6 - 24 | 0.01 - 0.04 | 246,170 | Alpha | RGBA16 | none | 4k |
| **Stumps, logs, roots, debris** | | | | | | | | | | | |
| `tree_stump_01` | pine_forest | 1 | none (unsuffixed) | 41,046 | single object | 0.57 | 41,046 | none | RGB16 | none | 8k |
| `tree_stump_02` | pine_forest | 1 | none (unsuffixed) | 62,345 | single object | 0.52 | 62,345 | none | RGB16 | none | 8k |
| `pine_roots` | pine_forest | 2 | none (unsuffixed) | 162,693 | 81,068 - 81,625 | 0.14 - 0.15 | 162,693 | none | RGB16 | none | 8k |
| `dead_tree_trunk` | pine_forest, verdant_trail | 1 | none (unsuffixed) | 101,802 | single object | 0.29 | 101,802 | none | RGB16 | none | 8k |
| `dead_tree_trunk_02` | verdant_trail | 4 | LOD0, LOD1, LOD2, LOD3 | 83,128 | single object | 1.06 | 155,864 | none | RGB16 | none | 8k |
| `dry_branches_medium_01` | pine_forest, verdant_trail | 3 | none (unsuffixed) | 16,803 | 5,254 - 5,933 | 0.14 - 0.34 | 16,803 | none | RGB16 | none | 8k |
| `bark_debris_01` | namaqualand | 4 | LOD0 | 193,406 | 38,488 - 65,062 | 0.07 - 0.11 | 362,632 | none | RGB16 | none | 16k |
| `root_cluster_01` | verdant_trail | 1 | none (unsuffixed) | 225,261 | single object | 1.51 | 225,261 | none | RGB16 | none | 8k |
| `root_cluster_02` | verdant_trail | 8 | none (unsuffixed) | 339,641 | 5,958 - 213,939 | 0.06 - 0.19 | 339,641 | none | RGB16 | none | 8k |
| `single_root` | verdant_trail | 4 | LOD0, LOD1, LOD2, LOD3 | 61,027 | single object | 0.18 | 114,424 | none | RGB16 | none | 8k |
| `dead_quiver_trunk` | namaqualand | 1 | LOD0 | 17,978 | single object | 1.99 | 33,706 | none | RGB16 | none | 8k |
| `dry_quiver_leaf` | namaqualand | 2 | LOD0 | 78,720 | 25,700 - 53,020 | 0.40 - 0.42 | 147,596 | none | RGB16 | none | 8k |
| **Mossy rocks shipped in pine_forest** | | | | | | | | | | | |
| `rock_moss_set_01` | pine_forest | 6 | none (unsuffixed) | 63,127 | 5,000 - 16,548 | 1.10 - 1.77 | 63,127 | none | RGB8 | none | 8k |
| `rock_moss_set_02` | pine_forest | 7 | none (unsuffixed) | 57,647 | 7,894 - 9,826 | 0.53 - 1.31 | 57,647 | none | RGBA8 | none | 8k |

Note on heights: they are the Y extent of each mesh in its own space. That is the upright height for every asset except `shrub_02`, whose four objects are stored rotated (section 6.7), and for lying objects such as `dead_tree_trunk` it is the thickness, not the length.

### 2.2 Download sizes

| Asset id | FBX mesh (MB) | Official FBX package 2k / 4k (MB) | Official glTF package 2k / 4k (MB) | Recipe 2k / 4k (MB) | Formats |
|---|---|---|---|---|---|
| **Conifers (pine_forest)** | | | | | |
| `pine_tree_01` | 618.4 | 814 / 1320 | 937 / 1011 | 660.4 / 759.7 | fbx, gltf, blend, usd |
| `fir_tree_01` | 237.8 | 314 / 510 | 487 / 557 | 278.5 / 373.8 | fbx, gltf, blend, usd |
| `pine_sapling_medium` | 189.3 | 269 / 476 | 266 / 297 | 204.5 / 237.0 | fbx, gltf, blend, usd |
| `fir_sapling_medium` | 49.2 | 119 / 301 | 83 / 110 | 62.9 / 90.9 | fbx, gltf, blend, usd |
| `pine_sapling_small` | 15.4 | 90 / 281 | 31 / 60 | 29.4 / 59.5 | fbx, gltf, blend, usd |
| `fir_sapling` | 18.2 | 83 / 247 | 33 / 58 | 30.7 / 56.2 | fbx, gltf, blend, usd |
| **Broadleaf / coastal trees** | | | | | |
| `jacaranda_tree` | 126.3 | 241 / 512 | 219 / 265 | 147.8 / 195.7 | fbx, gltf, blend, usd |
| `tree_small_02` | 72.0 | 134 / 262 | 110 / 153 | 91.9 / 136.0 | fbx, gltf, blend, usd |
| `island_tree_01` | 45.8 | 122 / 313 | 76 / 119 | 64.1 / 107.0 | fbx, gltf, blend, usd |
| `island_tree_02` | 30.8 | 106 / 294 | 56 / 97 | 48.5 / 88.9 | fbx, gltf, blend, usd |
| `island_tree_03` | 59.6 | 135 / 322 | 93 / 133 | 77.0 / 117.2 | fbx, gltf, blend, usd |
| `quiver_tree_01` | 4.6 | 87 / 295 | 21 / 66 | 21.0 / 66.3 | fbx, gltf, blend, usd |
| `quiver_tree_02` | 2.5 | 14 / 46 | 11 / 34 | 11.1 / 34.6 | fbx, gltf, blend, usd |
| **Shrubs** | | | | | |
| `searsia_burchellii` | 50.9 | 57 / 74 | 37 / 51 | 55.8 / 70.0 | fbx, gltf, blend, usd |
| `searsia_lucida` | 13.0 | 23 / 48 | 24 / 41 | 19.7 / 37.5 | fbx, gltf, blend, usd |
| `othonna_cerarioides` | 25.4 | 37 / 69 | 13 / 35 | 33.6 / 55.8 | fbx, gltf, blend, usd |
| `didelta_spinosa` | 17.2 | 26 / 40 | 16 / 23 | 20.9 / 28.0 | fbx, gltf, blend, usd |
| `wild_rooibos_bush` | 1.0 | 7 / 25 | 6 / 19 | 5.7 / 19.2 | fbx, gltf, blend, usd |
| `shrub_02` | 0.8 | 7 / 19 | 5 / 13 | 4.8 / 13.9 | fbx, gltf, blend, usd |
| `pachira_aquatica_01` | 2.4 | 49 / 149 | 12 / 35 | 12.4 / 35.4 | fbx, gltf, blend, usd |
| **Ferns, herbs, tropical leaf plants** | | | | | |
| `fern_02` | 0.2 | 5 / 16 | 3 / 10 | 3.5 / 10.8 | fbx, gltf, blend, usd |
| `anthurium_botany_01` | 1.7 | 13 / 41 | 7 / 20 | 6.9 / 20.1 | fbx, gltf, blend, usd |
| `calathea_orbifolia_01` | 0.5 | 10 / 34 | 5 / 19 | 5.7 / 19.7 | fbx, gltf, blend, usd |
| `potted_plant_02` | 2.1 | 74 / 267 | 5 / 13 | 5.1 / 13.5 | fbx, gltf, blend, usd |
| `potted_plant_01` | 5.1 | 75 / 255 | 9 / 18 | 8.8 / 18.6 | fbx, gltf, blend, usd |
| `shrub_01` | 4.4 | 16 / 40 | 10 / 23 | 10.0 / 22.7 | fbx, gltf, blend, usd |
| `shrub_03` | 0.3 | 6 / 15 | 4 / 13 | 4.5 / 13.0 | fbx, gltf, blend, usd |
| `shrub_04` | 0.8 | 6 / 16 | 4 / 12 | 4.4 / 11.9 | fbx, gltf, blend, usd |
| `nettle_plant` | 1.0 | 20 / 59 | 6 / 17 | 6.3 / 18.1 | fbx, gltf, blend, usd |
| `weed_plant_02` | 0.4 | 10 / 33 | 6 / 22 | 6.7 / 22.5 | fbx, gltf, blend, usd |
| `periwinkle_plant` | 1.1 | 27 / 75 | 5 / 12 | 5.6 / 15.1 | fbx, gltf, blend, usd |
| `shrub_sorrel_01` | 0.2 | 6 / 20 | 4 / 11 | 3.7 / 11.7 | fbx, gltf, blend, usd |
| `celandine_01` | 0.3 | 7 / 19 | 4 / 12 | 4.6 / 13.4 | fbx, gltf, blend, usd |
| `dandelion_01` | 1.9 | 8 / 22 | 6 / 16 | 6.7 / 17.1 | fbx, gltf, blend, usd |
| **Grass and moss** | | | | | |
| `grass_medium_01` | 1.0 | 13 / 40 | 8 / 24 | 9.1 / 27.0 | fbx, gltf, blend, usd |
| `grass_medium_02` | 0.3 | 3 / 7 | 2 / 4 | 2.0 / 5.3 | fbx, gltf, blend, usd |
| `grass_bermuda_01` | 0.1 | 8 / 25 | 5 / 15 | 4.7 / 15.4 | fbx, gltf, blend, usd |
| `moss_01` | 0.0 | 6 / 20 | 3 / 11 | 3.3 / 11.4 | fbx, gltf, blend, usd |
| **Stumps, logs, roots, debris** | | | | | |
| `tree_stump_01` | 2.7 | 18 / 53 | 11 / 33 | 12.7 / 35.0 | fbx, gltf, blend, usd |
| `tree_stump_02` | 4.2 | 21 / 65 | 12 / 41 | 15.3 / 44.1 | fbx, gltf, blend, usd |
| `pine_roots` | 10.7 | 103 / 341 | 25 / 83 | 32.6 / 90.0 | fbx, gltf, blend, usd |
| `dead_tree_trunk` | 6.8 | 21 / 51 | 11 / 31 | 15.4 / 35.5 | fbx, gltf, blend, usd |
| `dead_tree_trunk_02` | 6.3 | 21 / 62 | 13 / 44 | 17.1 / 48.5 | fbx, gltf, blend, usd |
| `dry_branches_medium_01` | 0.7 | 16 / 56 | 10 / 37 | 10.5 / 37.3 | fbx, gltf, blend, usd |
| `bark_debris_01` | 5.8 | 20 / 57 | 16 / 45 | 17.0 / 45.8 | fbx, gltf, blend, usd |
| `root_cluster_01` | 11.5 | 27 / 69 | 17 / 49 | 22.7 / 54.5 | fbx, gltf, blend, usd |
| `root_cluster_02` | 15.9 | 27 / 56 | 19 / 41 | 23.8 / 45.5 | fbx, gltf, blend, usd |
| `single_root` | 5.6 | 15 / 40 | 9 / 27 | 12.7 / 30.9 | fbx, gltf, blend, usd |
| `dead_quiver_trunk` | 0.6 | 9 / 32 | 7 / 25 | 7.2 / 25.0 | fbx, gltf, blend, usd |
| `dry_quiver_leaf` | 2.5 | 15 / 46 | 11 / 34 | 11.5 / 34.6 | fbx, gltf, blend, usd |
| **Mossy rocks shipped in pine_forest** | | | | | |
| `rock_moss_set_01` | 2.5 | 10 / 33 | 3 / 8 | 4.3 / 9.1 | fbx, gltf, blend, usd |
| `rock_moss_set_02` | 2.0 | 10 / 35 | 3 / 9 | 4.2 / 9.9 | fbx, gltf, blend, usd |

### 2.3 Individual objects inside the packs that matter

Each tree "model" is a pack of one to three separate trees. These are the real units the builder will place.

| Object (glTF node name; the FBX object has the same name, with `_LOD0` appended for `othonna_cerarioides`) | LOD0 triangles | Vertices | Bounding box X x Y(up) x Z (m) | Sub-meshes (materials) |
|---|---|---|---|---|
| `pine_tree_01_a_LOD0` | 6,951,524 | 6,845,315 | 7.60 x 20.37 x 8.31 | 4 |
| `pine_tree_01_b_LOD0` | 4,234,432 | 4,155,948 | 7.22 x 14.88 x 7.60 | 4 |
| `pine_tree_01_c_LOD0` | 5,996,296 | 5,917,095 | 7.63 x 17.54 x 7.86 | 4 |
| `fir_tree_01_a_LOD0` | 4,176,819 | 5,428,923 | 6.53 x 18.93 x 6.43 | 4 |
| `fir_tree_01_b_LOD0` | 2,300,624 | 2,951,462 | 5.61 x 14.06 x 6.06 | 4 |
| `fir_tree_01_c_LOD0` | 505,494 | 630,314 | 6.31 x 14.52 x 5.97 | 4 |
| `pine_sapling_medium_a_LOD0` | 2,662,894 | 2,636,311 | 5.18 x 11.49 x 4.74 | 3 |
| `pine_sapling_medium_b_LOD0` | 1,872,357 | 1,853,081 | 4.76 x 9.12 x 5.41 | 3 |
| `pine_sapling_medium_c_LOD0` | 1,502,888 | 1,487,415 | 3.82 x 7.01 x 3.52 | 3 |
| `fir_sapling_medium_a_LOD0` | 685,046 | 779,700 | 4.99 x 8.83 x 5.28 | 3 |
| `fir_sapling_medium_b_LOD0` | 420,822 | 479,822 | 4.27 x 7.80 x 3.96 | 3 |
| `fir_sapling_medium_c_LOD0` | 427,645 | 487,585 | 4.31 x 5.91 x 4.01 | 3 |
| `pine_sapling_small_b` | 122,235 | 124,796 | 0.53 x 1.05 x 0.73 | 2 |
| `pine_sapling_small_a` | 142,506 | 145,495 | 0.79 x 1.30 x 0.73 | 2 |
| `pine_sapling_small_c` | 133,403 | 136,065 | 0.51 x 0.96 x 0.58 | 2 |
| `fir_sapling_a` | 157,402 | 187,306 | 0.86 x 1.30 x 0.82 | 2 |
| `fir_sapling_b` | 150,876 | 179,580 | 0.64 x 0.95 x 0.82 | 2 |
| `fir_sapling_c` | 124,743 | 148,413 | 0.62 x 0.74 x 0.63 | 2 |
| `fern_02_b` | 2,384 | 1,660 | 0.99 x 0.43 x 0.89 | 1 |
| `fern_02_c` | 2,248 | 1,568 | 0.87 x 0.35 x 0.77 | 1 |
| `fern_02_a` | 784 | 544 | 0.55 x 0.29 x 0.61 | 1 |
| `fern_02_d` | 816 | 565 | 0.57 x 0.21 x 0.59 | 1 |
| `jacaranda_tree_LOD0` | 3,863,832 | 3,680,496 | 24.42 x 19.47 x 19.15 | 3 |
| `tree_small_02_LOD0` | 2,062,487 | 1,777,278 | 2.92 x 4.56 x 4.29 | 3 |
| `island_tree_01_LOD0` | 1,599,403 | 1,303,928 | 4.76 x 5.03 x 4.82 | 3 |
| `island_tree_02_LOD0` | 1,072,213 | 874,494 | 4.21 x 3.41 x 4.07 | 3 |
| `island_tree_03_LOD0` | 2,085,320 | 1,708,346 | 2.97 x 2.62 x 2.91 | 3 |
| `searsia_burchellii_large_LOD0` | 361,558 | 484,482 | 4.16 x 3.24 x 5.05 | 3 |
| `searsia_burchellii_medium_LOD0` | 210,262 | 289,378 | 3.58 x 2.59 x 2.61 | 3 |
| `searsia_burchellii_small_LOD0` | 44,516 | 57,854 | 1.65 x 1.94 x 2.27 | 3 |
| `searsia_lucida_a_LOD0` | 113,405 | 136,311 | 1.81 x 2.34 x 1.97 | 3 |
| `searsia_lucida_b_LOD0` | 88,629 | 110,904 | 1.46 x 1.99 x 1.52 | 3 |
| `searsia_lucida_c_LOD0` | 77,603 | 95,774 | 1.51 x 1.68 x 1.25 | 3 |
| `searsia_lucida_d_LOD0` | 56,320 | 69,852 | 1.10 x 1.42 x 0.95 | 3 |
| `searsia_lucida_e_LOD0` | 27,701 | 34,182 | 1.03 x 1.02 x 0.72 | 3 |
| `searsia_lucida_f_LOD0` | 7,232 | 8,754 | 0.66 x 0.74 x 0.48 | 3 |
| `searsia_lucida_g_LOD0` | 6,302 | 7,761 | 0.38 x 0.79 x 0.32 | 3 |
| `othonna_cerarioides_a` | 57,067 | 74,214 | 1.10 x 1.97 x 1.03 | 2 |
| `othonna_cerarioides_b` | 29,175 | 37,458 | 1.01 x 1.77 x 1.05 | 2 |
| `othonna_cerarioides_c` | 12,491 | 15,621 | 0.72 x 1.44 x 0.95 | 2 |
| `othonna_cerarioides_d` | 9,320 | 11,863 | 0.66 x 1.15 x 0.57 | 2 |
| `othonna_cerarioides_e` | 3,834 | 4,735 | 0.56 x 0.96 x 0.46 | 2 |
| `othonna_cerarioides_f` | 3,138 | 3,802 | 0.39 x 0.68 x 0.46 | 2 |
| `othonna_cerarioides_g` | 3,124 | 3,684 | 0.50 x 0.46 x 0.44 | 2 |
| `didelta_spinosa_large_LOD0` | 234,552 | 154,841 | 2.12 x 2.07 x 1.86 | 1 |
| `didelta_spinosa_medium_LOD0` | 143,747 | 96,179 | 1.27 x 1.30 x 1.57 | 1 |
| `didelta_spinosa_small_LOD0` | 38,888 | 26,503 | 0.82 x 0.94 x 1.06 | 1 |
| `pachira_aquatica_01_bark_a` | 13,453 | 7,468 | 0.23 x 0.85 x 0.24 | 1 |
| `pachira_aquatica_01_bark_b` | 584 | 353 | 0.16 x 0.58 x 0.23 | 1 |
| `pachira_aquatica_01_bark_c` | 3,496 | 2,138 | 0.59 x 1.00 x 0.49 | 1 |
| `pachira_aquatica_01_bark_d` | 3,764 | 2,280 | 0.60 x 1.63 x 0.43 | 1 |
| `pachira_aquatica_01_leaves_a` | 9,714 | 6,412 | 0.74 x 0.74 x 0.65 | 1 |
| `pachira_aquatica_01_leaves_b` | 4,507 | 2,988 | 0.50 x 0.52 x 0.61 | 1 |
| `pachira_aquatica_01_leaves_c` | 17,055 | 11,396 | 0.98 x 0.89 x 0.92 | 1 |
| `pachira_aquatica_01_leaves_d` | 24,341 | 16,193 | 1.05 x 1.65 x 1.00 | 1 |
| `anthurium_botany_01_a` | 15,808 | 9,703 | 0.90 x 0.50 x 1.14 | 1 |
| `anthurium_botany_01_b` | 15,544 | 9,568 | 0.76 x 0.47 x 0.57 | 1 |
| `anthurium_botany_04_d` | 10,360 | 6,557 | 0.48 x 0.30 x 0.35 | 1 |
| `anthurium_botany_01_c` | 9,072 | 5,656 | 0.60 x 0.36 x 0.66 | 1 |
| `anthurium_botany_05_e` | 7,664 | 4,863 | 0.32 x 0.16 x 0.29 | 1 |
| `anthurium_botany_06_f` | 8,704 | 5,570 | 0.31 x 0.14 x 0.28 | 1 |
| `calathea_orbifolia_01_a` | 5,904 | 3,388 | 0.60 x 0.42 x 0.53 | 1 |
| `calathea_orbifolia_01_b` | 3,446 | 1,990 | 0.35 x 0.30 x 0.44 | 1 |
| `calathea_orbifolia_01_c` | 3,542 | 2,042 | 0.38 x 0.28 x 0.38 | 1 |
| `calathea_orbifolia_01_d` | 1,802 | 1,048 | 0.23 x 0.13 x 0.16 | 1 |
| `calathea_orbifolia_01_e` | 1,994 | 1,152 | 0.21 x 0.15 x 0.25 | 1 |
| `rock_moss_set_01_rock01` | 11,000 | 6,078 | 2.24 x 1.47 x 3.37 | 1 |
| `rock_moss_set_01_rock02` | 10,996 | 6,192 | 2.66 x 1.26 x 3.26 | 1 |
| `rock_moss_set_01_rock03` | 5,000 | 2,741 | 2.11 x 1.10 x 2.08 | 1 |
| `rock_moss_set_01_rock04` | 10,589 | 5,691 | 2.13 x 1.77 x 1.96 | 1 |
| `rock_moss_set_01_rock05` | 16,548 | 8,538 | 1.82 x 1.21 x 3.00 | 1 |
| `rock_moss_set_01_rock06` | 8,994 | 4,748 | 2.13 x 1.26 x 2.76 | 1 |

---

## 3. Visual judgement from the thumbnails

I opened the 512 px thumbnails and looked at them. A thumbnail shows shape, colour and leaf density. It does not show close-up bark quality, so "quality" below means silhouette and botanical plausibility.

**Caution about five thumbnails.** `grass_medium_01`, `grass_medium_02`, `grass_bermuda_01`, `moss_01` and `celandine_01` are rendered as a dense ball of scattered plants. That ball is Blender's geometry-nodes scatter. The FBX does not contain it. The FBX contains only the individual clumps (section 6.9).

### Conifers and their debris (collection `pine_forest`)

| Id | What I see | Judgement |
|---|---|---|
| `pine_tree_01` | Three tall Scots-pine-like trees, bare lower trunk, layered crown, dead lower twigs, small root mound | Excellent. Reads as a photographed tree |
| `fir_tree_01` | Three firs: one dense, one mid, one sparse and half dead | Excellent, and the half-dead one adds believable variety |
| `pine_sapling_medium` | Three young pines, 7 to 11.5 m, open branching | Very good |
| `fir_sapling_medium` | Three young firs, 6 to 9 m, branches down to the ground | Very good. The only conifer here that blocks sight lines at eye height |
| `pine_sapling_small`, `fir_sapling` | Three knee-to-chest-high seedlings each | Good |
| `fern_02` | Four fern clumps, dark green pinnate fronds, photo-scanned leaves on cards | Good. Clearly the same species four times |
| `tree_stump_01`, `tree_stump_02` | Photoscanned rotten stumps on a mound of bark litter, with moss | Excellent. The ground patch is baked into the scan |
| `pine_roots` | Two photoscanned ground patches with surface roots, dark brown soil | Very good. Also baked ground |
| `dead_tree_trunk` | A thin 3 m dead branch | Good, small |
| `dry_branches_medium_01` | Three dead branches, 0.8 to 1.3 m | Good |
| `rock_moss_set_01`, `rock_moss_set_02` | Six and seven grey-brown rocks with moss tint | Good (rocks are another teammate's topic) |

Biome and season: one coherent temperate European pine wood in the green season. Same two authors throughout (Rob Tuytel photography, Rico Cilliers modelling), same soil colour (dark brown needle litter), same light. The collection also ships matching textures `forest_ground_04`, `forest_leaves_04`, `pine_bark`, `rocky_trail` (**[V]** `FOREST/catalog/ph_textures.json`).

### Broadleaf and "island" trees

| Id | What I see | Judgement |
|---|---|---|
| `jacaranda_tree` | One big spreading tree, fine pinnate foliage, no flowers. 19.5 m tall, 24 m crown | Very good. The only large broadleaf tree in the catalogue |
| `tree_small_02` | One slim small tree, 4.6 m | Good |
| `island_tree_01`, `island_tree_02`, `island_tree_03` | Low, gnarled, twisted-trunk trees with small leathery leaves. 5.0, 3.4 and 2.6 m tall. `island_tree_03` stands on a pale sand patch | Good models, but they are coastal scrub (they look like milkwood or olive). They are not jungle trees, and at 2.6 to 5 m they are not a canopy |
| `quiver_tree_01` | Tall aloe tree, thin pale smooth trunk, small leaf rosettes on top, 2.7 m | Good model. Reads as desert |
| `quiver_tree_02` | Short aloe: thick scaly trunk and one big rosette of stiff pointed leaves, 1.47 m, 82,074 triangles, no alpha | Good model. It is a desert plant, but its silhouette is the closest thing to a **cycad** in the whole catalogue, and cycads are the classic dinosaur-era plant |

### Shrubs

| Id | What I see | Judgement |
|---|---|---|
| `searsia_burchellii` | Three dense dark evergreen bushes, 1.9 to 3.2 m | Very good. Generic enough to pass outside its desert home |
| `searsia_lucida` | Seven upright glossy-leaved bushes, 0.7 to 2.3 m | Very good, generic |
| `othonna_cerarioides` | Seven dense upright evergreen bushes, 0.5 to 2.0 m | Very good, generic |
| `didelta_spinosa` | Three lime-green broad-leaved bushes, 0.9 to 2.1 m | Good. The bright green is the closest thing to "lush" |
| `wild_rooibos_bush` | Five needle-leaved tufts, 0.3 to 0.5 m | Reads as arid |
| `shrub_02` | Four drooping willow-leaved sprigs with brownish leaves | Mediocre, looks wilted |
| `shrub_01`, `shrub_03`, `shrub_04` | Small flowering herbs and woody sprigs, 0.2 to 0.4 m tall despite the name "shrub" | Good as ground detail only |
| `pachira_aquatica_01` | Four small "money trees", 0.5 to 1.65 m, one with a braided trunk | Good, but the braided trunk is a houseplant giveaway |

### Low plants and ground cover

| Id | What I see | Judgement |
|---|---|---|
| `anthurium_botany_01` | Six rosettes of big dark glossy leaves, up to 0.5 m tall and 1.1 m across | Very good. The most jungle-like leaf in the catalogue |
| `calathea_orbifolia_01` | Five small plants with round striped leaves, up to 0.42 m | Good |
| `potted_plant_02` | Arrowhead-leaved plant in a terracotta pot. The leaves are a separate object (`potted_plant_02_leaves`) | Leaves usable without the pot |
| `nettle_plant` | Six nettles. Real height in the file is only 0.02 to 0.22 m | Good, but tiny |
| `weed_plant_02` | Five flat dock-like weeds with some yellowed leaves | Good |
| `periwinkle_plant` | Six pink-flowered plants, 0.16 to 0.41 m | Good. Pink flowers will repeat visibly |
| `shrub_sorrel_01` | Eleven tiny wood-sorrel sprigs, 2 to 6 cm | Fine filler |
| `dandelion_01`, `celandine_01` | Lawn weeds with yellow flowers | Wrong mood for a horror forest |

### Roots and logs from `verdant_trail`

`root_cluster_01`, `root_cluster_02`, `single_root` are photoscans with **red-orange soil** baked in. `dead_tree_trunk_02` is a pale, sun-bleached 4 m log. They do not match the dark brown floor of the pine collection without a matching red ground texture.

### Namaqualand flowers and succulents

`flower_empodium`, `flower_gazania`, `flower_heliophila`, `flower_stinkkruid`, `flower_ursinia` are small yellow, orange and white desert wildflowers. `cheiridopsis_succulent`, `leipoldtia_schultzei` (purple flowers) and `crystalline_iceplant` are low succulents. `dead_quiver_trunk` and `dry_quiver_leaf` are pale aloe remains. `potted_plant_04` is a haworthia in a pot. All are well made and none belongs in a forest. They are recorded in section 2 for completeness only.

---

## 4. The two kits

### Kit A: conifer and fern "prehistoric" forest

Eighteen ids. Sixteen of them are the complete `pine_forest` collection, so they were made to sit together. The other two (`dead_tree_trunk_02`, `bark_debris_01`) are neutral dead wood.

| Layer | Asset id | Objects | Role |
|---|---|---|---|
| Canopy | `pine_tree_01` | 3 trees, 14.9 to 20.4 m | Main canopy, open trunks |
| Canopy | `fir_tree_01` | 3 trees, 14.1 to 18.9 m | Darker canopy, one half-dead tree |
| Mid storey | `pine_sapling_medium` | 3 trees, 7.0 to 11.5 m | Fill between trunks |
| Mid storey | `fir_sapling_medium` | 3 trees, 5.9 to 8.8 m | Sight-line blockers, hiding cover |
| Under storey | `pine_sapling_small` | 3 seedlings, 1.0 to 1.3 m | Regrowth |
| Under storey | `fir_sapling` | 3 seedlings, 0.7 to 1.3 m | Regrowth |
| Under storey | `fern_02` | 4 clumps, 0.55 to 0.99 m across | The "prehistoric" signal. Use in large drifts |
| Ground | `grass_medium_01` | 17 clumps, 0.05 to 0.32 m | Sparse tufts in clearings only |
| Ground | `moss_01` | 12 sprig cards, 1 to 4 cm | Hero spots only (section 6.10) |
| Debris | `tree_stump_01`, `tree_stump_02` | 1 each | Rotten stumps |
| Debris | `pine_roots` | 2 patches | Root patches at trunk bases |
| Debris | `dead_tree_trunk` | 1 | Thin fallen branch, 3 m |
| Debris | `dead_tree_trunk_02` | 1 object in 4 LODs | Fallen log, 4 m. From `verdant_trail`; paler wood |
| Debris | `dry_branches_medium_01` | 3 | Dead branches |
| Debris | `bark_debris_01` | 4 pieces | Bark chunks. From `namaqualand`; grey weathered wood, generic |
| Rocks | `rock_moss_set_01`, `rock_moss_set_02` | 6 and 7 rocks, up to 3.4 m long and 1.8 m high | Crouch cover |

**[V]** Download with the recipe of section 6.4: 1,411 MB at 2k, 1,982 MB at 4k. The FBX files are 1,171 MB of that, and the four big tree packs alone are 1,095 MB. Without `pine_tree_01`, `fir_tree_01` and `pine_sapling_medium` the kit is 268 MB at 2k and 611 MB at 4k.

**[V]** Sum of LOD0 geometry, one copy of each object: 33.4 million triangles and 35.0 million vertices, of which the four big tree packs are 31.7 million triangles. The twelve non-tree assets together are 0.81 million triangles.

Optional additions:

- `quiver_tree_02` as a stand-in cycad, a handful of copies at most. It is an aloe from a desert collection with grey-green leaves, so this needs a visual check in the real lighting. If it passes, it is the cheapest way to say "prehistoric" (82,074 triangles, 11 MB at 2k).
- `searsia_lucida` and `othonna_cerarioides` as head-high evergreen hide bushes. They are from an arid collection. They would read as a laurel or rhododendron-like understory. Needs a visual check in the real lighting before committing.
- ambientCG `3DTreeStump001`. It is a clean saw cut, so it tells the player that people logged here. Use only if the story wants that.
- Matching Poly Haven textures from the same collection: `forest_ground_04`, `forest_leaves_04`, `pine_bark`, `rocky_trail`.

### Kit B: tropical island jungle

The honest name for what can be built is "subtropical coastal thicket". Thirty ids from five unrelated collections.

| Layer | Asset id | Objects | Role |
|---|---|---|---|
| Canopy | `jacaranda_tree` | 1 tree, 19.5 m tall, 24 m crown | The only real canopy tree |
| Small trees | `tree_small_02` | 1 tree, 4.6 m | Understory tree |
| Small trees | `island_tree_01`, `island_tree_02`, `island_tree_03` | 1 each, 5.0, 3.4, 2.6 m | Gnarled coastal trees |
| Shrubs | `searsia_burchellii` | 3 sizes, 1.9 to 3.2 m, LOD0 to LOD2 in the FBX | Dense cover |
| Shrubs | `searsia_lucida` | 7, 0.7 to 2.3 m | Glossy cover |
| Shrubs | `othonna_cerarioides` | 7, 0.5 to 2.0 m, LOD0 to LOD3 in the FBX | Upright cover |
| Shrubs | `didelta_spinosa` | 3 sizes, 0.9 to 2.1 m, LOD0 to LOD2, has a translucency map | Bright green cover |
| Shrubs | `pachira_aquatica_01` | 4, 0.5 to 1.65 m | The one truly tropical woody plant |
| Shrubs | `shrub_02` | 4 | Thin filler |
| Leaf plants | `anthurium_botany_01` | 6, up to 0.5 m | Big glossy leaves |
| Leaf plants | `calathea_orbifolia_01` | 5, up to 0.42 m | Round leaves |
| Leaf plants | `potted_plant_02` | use only the object `potted_plant_02_leaves` | Arrowhead leaves |
| Leaf plants | `fern_02` | 4 | Ferns |
| Herbs | `periwinkle_plant`, `nettle_plant`, `weed_plant_02`, `shrub_01`, `shrub_03`, `shrub_04` | 6, 6, 5, 9, 4, 4 | Ground detail |
| Ground | `grass_medium_02`, `grass_bermuda_01`, `moss_01` | 5, 21, 12 | Grass tufts and moss |
| Debris | `root_cluster_01`, `root_cluster_02`, `single_root` | 1, 8, 1 (4 LODs) | Roots in red soil |
| Debris | `dead_tree_trunk_02`, `dead_tree_trunk`, `dry_branches_medium_01` | 1, 1, 3 | Logs and branches |

**[V]** Download with the same recipe: 747 MB at 2k, 1,337 MB at 4k. Sum of LOD0 geometry: about 13.6 million triangles (glTF counts; `shrub_01` and `shrub_04` are approximate, section 6.11).

One trick that might rescue some jungle feeling, **UNVERIFIED**: scale `anthurium_botany_01` and `calathea_orbifolia_01` by 2.5 to 3. The source textures go up to 8k, so the leaves would stay sharp, and `anthurium_botany_01_a` would become about 2.3 m across and 1.25 m high. Whether that looks like a giant-leaved jungle plant or like an inflated houseplant has to be judged in a render.

---

## 5. Verdict: which kit reaches higher realism

**Kit A, clearly.** The reasons, in order of weight:

1. **It is one real place.** Fourteen of its eighteen assets were photographed and built as one set, by the same people, with the same soil and the same season. Kit B mixes a South African semi-desert, a Dutch garden shed, houseplants and a coastal trail.
2. **It has a canopy.** Kit A has twelve trees between 6 and 20 m in two species and two age classes, plus six seedlings. Kit B has one tree above 5 m. A forest built from one repeated 24 m jacaranda crown will look cloned from every angle.
3. **Nothing in it says "wrong".** A player does not need to know botany to feel that a jungle without palms, lianas or huge leaves is not a jungle. Poly Haven has none of those (section 1). Conifers over ferns, on the other hand, is a real and familiar forest type, and it is also what the Mesozoic looked like, which suits dinosaurs.

What will look repetitive or wrong in Kit A, honestly:

- **Two tree species.** Conifers hide repetition well because real ones look alike, but with only three mature shapes per species an attentive player will spot twins. Random rotation and plus or minus 15 percent scale are needed. `fir_tree_01_c` (the half-dead one) is so distinctive that it must be used sparingly.
- **One fern, and it is small.** Four clumps of one species, at most 1 m across and 0.43 m high. There is no waist-high bracken and no tree fern. The "prehistoric" feeling has to come from density and from scale variation of about 0.8 to 1.6. Larger than that and the frond anatomy starts to look fake.
- **It is a northern forest, not an island.** It reads as Scandinavia or the Pacific Northwest. If the story insists on a tropical island, this is the wrong look. If "misty conifer island" is acceptable, it is the right one.
- **Long sight lines.** Mature pines here have bare trunks. At eye height the forest is open. For stealth that means little natural cover: only `fir_sapling_medium`, the rocks and the stumps block a standing or crouching player. The optional evergreen bushes exist to fix this.
- **No big deadfall.** There is no thick mossy fallen trunk and no upturned root plate. `dead_tree_trunk` is only 0.28 m thick.
- **Scan patches have edges.** `tree_stump_01`, `tree_stump_02` and `pine_roots` carry their own ground. Their rims will show unless the terrain texture matches and ferns or litter hide the rim.
- **Grass is slightly out of place** on a needle floor. Keep it to clearings.

What will look wrong in Kit B, in addition to the missing species: red soil on the roots against pale sand under `island_tree_03`; knee-high houseplants standing in for jungle understory; pink periwinkle flowers repeating; bare, sun-loving shrubs under a "jungle" canopy.

---

## 6. Technical limitations that affect Unity

### 6.1 Polycount: the FBX and glTF files of the trees contain LOD0 only, and LOD0 is enormous

**[V]** The site flags 78 models with `lods: true`, including every large tree. But for all eleven tree packs the FBX holds only objects named `*_LOD0` (or unsuffixed), and so does the glTF. Example: `pine_tree_01_2k.fbx` declares 3 Geometry and 3 Model objects in its `Definitions`, named `pine_tree_01_a_LOD0`, `_b_LOD0`, `_c_LOD0` (`FOREST/research/_fbx_headers.txt`, `_fbx_tails.txt`).

**[V]** The lower LODs exist only inside the `.blend`. In `moss_01_2k.blend` the collections are `moss_01_LOD0`, `moss_01_LOD1`, `moss_01_LOD2`, `moss_01_geometry_nodes`, with objects `moss_01_a_LOD0`, `moss_01_a_LOD1`, `moss_01_a_LOD2` and so on, while its FBX has only the twelve `_LOD0` objects. Poly Haven's own standard says why: "Our add-on (and FBX/etc. exporters) will look for the slug_LOD0 collection first and mark that as an asset" and "LOD1 is the next level down, containing about half or less the detail and polycount. LOD2 is half that again" (`FOREST/catalog/ph_web/models.html`).

**[I]** So the tree `.blend` files very likely contain `_LOD1` and `_LOD2` trees. **UNVERIFIED**: I did not open a tree `.blend` (they are 39 to 571 MB), so I do not know how many triangles those LODs have or how good they look.

**[V]** A few older exports do carry their LODs in the FBX. Among forest-relevant assets: `dead_tree_trunk_02` (LOD0 to LOD3), `single_root` (LOD0 to LOD3), `searsia_burchellii` (LOD0 to LOD2), `othonna_cerarioides` (LOD0 to LOD3), `didelta_spinosa` (LOD0 to LOD2), `dandelion_01` (LOD0 to LOD3).

**[V]** LOD0 triangle counts per single tree (glTF index accessors):

| Tree | Triangles | Height |
|---|---|---|
| `pine_tree_01_a_LOD0` | 6,951,524 | 20.4 m |
| `pine_tree_01_c_LOD0` | 5,996,296 | 17.5 m |
| `pine_tree_01_b_LOD0` | 4,234,432 | 14.9 m |
| `fir_tree_01_a_LOD0` | 4,176,819 | 18.9 m |
| `jacaranda_tree_LOD0` | 3,863,832 | 19.5 m |
| `pine_sapling_medium_a_LOD0` | 2,662,894 | 11.5 m |
| `fir_tree_01_b_LOD0` | 2,300,624 | 14.1 m |
| `island_tree_03_LOD0` | 2,085,320 | 2.6 m |
| `tree_small_02_LOD0` | 2,062,487 | 4.6 m |
| `island_tree_01_LOD0` | 1,599,403 | 5.0 m |
| `island_tree_02_LOD0` | 1,072,213 | 3.4 m |
| `fir_sapling_medium_a_LOD0` | 685,046 | 8.8 m |
| `fir_tree_01_c_LOD0` | 505,494 | 14.5 m |
| `fir_sapling_medium_b_LOD0`, `_c_LOD0` | 420,822 and 427,645 | 7.8 and 5.9 m |
| `fir_sapling_a` (a seedling) | 157,402 | 1.3 m |
| `pine_sapling_small_a` (a seedling) | 142,506 | 1.3 m |

**[V]** Almost all of that is foliage. Split by sub-mesh (`FOREST/research/_tree_submeshes.txt`, from the glTF index accessors):

| Tree | Wood (bark, trunk, branches, dead branches) | Foliage (twig or leaves sub-mesh) | Foliage share |
|---|---|---|---|
| `pine_tree_01_a_LOD0` | 112,702 triangles | 6,838,822 | 98.4 % |
| `pine_tree_01_b_LOD0` | 97,482 | 4,136,950 | 97.7 % |
| `pine_tree_01_c_LOD0` | 71,366 | 5,924,930 | 98.8 % |
| `fir_tree_01_a_LOD0` | 110,700 | 4,066,119 | 97.3 % |
| `fir_tree_01_b_LOD0` | 93,328 | 2,207,296 | 95.9 % |
| `fir_tree_01_c_LOD0` | 59,420 | 446,074 | 88.2 % |
| `pine_sapling_medium_a_LOD0` | 13,979 | 2,648,915 | 99.5 % |
| `fir_sapling_medium_a_LOD0` | 10,300 | 674,746 | 98.5 % |
| `fir_sapling_a` | 546 | 156,856 | 99.7 % |
| `jacaranda_tree_LOD0` | 1,461,398 | 2,402,434 | 62.2 % |
| `island_tree_01_LOD0` | 539,371 | 1,060,032 | 66.3 % |

**[I]** This is the most useful fact for the build. The woody skeleton of a 20 m pine is only about 110 thousand triangles and is a connected surface. The cost is in millions of small, separate needle pieces. Whatever reduces these trees has to treat the foliage sub-mesh on its own. The pine twig material is opaque in the glTF and has no alpha link in the FBX, so in `pine_tree_01` the needles are probably modelled as geometry rather than as alpha cards (**UNVERIFIED** without the mesh data); in `fir_tree_01` the glTF twig material uses alpha.

**[I]** The greybox places 240 trees in 120 by 120 m (`Assets/Game/Editor/GreyboxSceneBuilder.cs` lines 26 to 28). Even twenty LOD0 pines in view would be about 100 million triangles before shadow passes. These meshes cannot be placed as they are. Even the 1 m seedlings, at 120 to 160 thousand triangles and with no LODs anywhere (`lods` is false for them), are far too heavy to scatter.

**[I]** Memory estimate for all of Kit A at LOD0: 35.0 million vertices at an assumed 48 bytes each (position, normal, tangent, one UV, uncompressed) is about 1.7 GB, plus 33.4 million triangles of 32-bit indices at 0.4 GB. That fits this Mac. It does not fit a console budget.

**What Unity 6000.6 offers:**

- **[V]** **Mesh LOD**, generated at import. `UnityEditor.ModelImporter.generateMeshLods` (bool), `.maximumMeshLod` (int, negative means no limit), `.meshLodGenerationFlags` (`UnityEditor.MeshLodUtility.LodGenerationFlags`, one member `DiscardOddLevels`). Also `UnityEditor.MeshLodUtility.GenerateMeshLods(Mesh mesh, int meshLodLimit)` and `GenerateMeshLods(Mesh mesh, LodGenerationFlags flags, int meshLodLimit)`. Runtime control: `QualitySettings.meshLodThreshold`, `Renderer.meshLodSelectionBias`, `Renderer.forceMeshLod` (short, -1 is automatic), `Mesh.lodCount`. Sources: `UNITY/Documentation/en/ScriptReference/ModelImporter-generateMeshLods.html`, `ModelImporter-maximumMeshLod.html`, `ModelImporter-meshLodGenerationFlags.html`, `MeshLodUtility.GenerateMeshLods.html`, `MeshLodUtility.LodGenerationFlags.html`, `QualitySettings-meshLodThreshold.html`, `Renderer-meshLodSelectionBias.html`, `Renderer-forceMeshLod.html`; member names also in `UNITY/Unity.app/Contents/Resources/Scripting/Managed/UnityEngine/UnityEditor.CoreModule.xml` lines 36025, 36046, 36051, 36343, 36498, 36508 and `UnityEngine.CoreModule.xml` lines 26419, 33414, 35469, 35534.
- **[V]** How it works: "Each subsequent LOD contains approximately half the number of indices as the previous one", "The generator doesn't add any new vertices. All LODs reuse the same vertex buffer", "The maximum increase in size of the original index buffer is 100%", and the mesh needs at least 256 triangles (`UNITY/Documentation/en/Manual/lod/mesh-lod-generator.html`). So `pine_tree_01_a` would reach about 217 thousand triangles at Mesh LOD 5 and about 54 thousand at Mesh LOD 7, but the full 6.8 million vertex buffer stays in memory.
- **[V]** The documented weakness is exactly our case: "The generator might produce undesired results on meshes with a high curvature or meshes that consist of many disconnected pieces" (same page). The troubleshooting page uses a tree as its bad example: "a significant loss of detail in the automatically generated Mesh LOD4. For instance, the foliage parts disappear" (`UNITY/Documentation/en/Manual/lod/mesh-lod-troubleshooting.html`). Needle clusters and leaf cards are disconnected pieces. **UNVERIFIED** how these particular trees survive it.
- **[V]** Other Mesh LOD limits: static batching, Particle System and VFX Graph always draw LOD0; cross-fade needs the GPU Resident Drawer; `Graphics.RenderMeshInstanced` needs an explicit `forceMeshLod` in `RenderParams`; "Using Mesh LOD in combination with LOD Group might lead to unexpected outcomes and is not recommended" (`UNITY/Documentation/en/Manual/lod/mesh-lod-introduction.html`).
- **[V]** **LOD Group** from names: a model whose meshes are suffixed `_LODX` is imported with a LOD Group configured automatically (`UNITY/Documentation/en/Manual/lod/lod-group-configure.html`). This is what makes the authored LODs usable if they are exported from Blender.

**[V]** Blender was not found on this machine (nothing matching in `/Applications` or `/opt/homebrew/bin`). `zstd` is installed at `/opt/homebrew/bin/zstd`, which is how I read the `.blend`.

### 6.2 Leaf alpha

**[V]** Every foliage asset has a separate greyscale alpha map that can be downloaded on its own. Its `/files` key is `Alpha`, or `leaves_alpha`, `twig_alpha`, `twigs_alpha`, or `opacity` (only `periwinkle_plant`). Most are greyscale PNG (34 at 16 bit, 2 at 8 bit); seven are RGB or RGBA PNGs (`fir_tree_01`, `pine_tree_01`, `pine_sapling_medium`, `fir_sapling_medium`, `wild_rooibos_bush`, `didelta_spinosa`, `jacaranda_tree`), so the composer must read one channel rather than assume a single-channel file (`FOREST/research/_png_headers.txt`).

**[V]** The diffuse PNG sometimes has the alpha baked in and sometimes not. `RGBA16`: `grass_medium_01`, `grass_medium_02`, `grass_bermuda_01`, `moss_01`, `pine_tree_01` twig, `pine_sapling_medium` twig, `fir_sapling_medium` twigs, all `island_tree_*` leaves, `jacaranda_tree` leaves, `tree_small_02` leaves, `searsia_*`, `othonna_cerarioides`, `didelta_spinosa`, `nettle_plant`, `weed_plant_02`, `periwinkle_plant`. Plain `RGB16` with no alpha channel: `fern_02`, `fir_tree_01` twig, `fir_sapling` twigs, `pine_sapling_small` twig, `shrub_02`, `shrub_03`, `shrub_04`, `anthurium_botany_01`, `calathea_orbifolia_01`, `pachira_aquatica_01` leaves. Poly Haven's standard asks for both ("Any alpha or opacity maps should be included as individual maps as well as baked into the alpha channel of the diffuse map") but the published files do not follow it consistently.

**[V]** The glTF packages reference `*_diff_*.jpg` as base colour and list no alpha file in `include`, even where the material says `alphaMode: MASK` or `BLEND`. JPEG has no alpha channel, so the glTF route loses the leaf mask. Example: `fern_02` material is `MASK`, cutoff 0.5, base colour `fern_02_diff_2k.jpg`.

**[V]** The FBX materials are no help either. 23 of the 61 FBX files have no texture linked at all (`Texture` count absent in `Definitions`), including `fern_02`, `moss_01`, `anthurium_botany_01`, `calathea_orbifolia_01`, all `shrub_*`, `nettle_plant`, `periwinkle_plant`. Where textures are linked, the normal and roughness links often point at `.exr` files, and the tree FBX files re-exported in May 2025 use a relative path that climbs four folders (`..\..\..\..\textures\pine_tree_01_bark_diff_2k.png`). Materials have to be built by the editor script.

**[V]** HDRP Lit reads alpha from the alpha channel of the base colour map: `float alphaTex = SAMPLE_UVMAPPING_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, layerTexCoord.base).a;` (`HDRP/Runtime/Material/Lit/LitData.hlsl` line 232). There is no separate alpha slot. So the builder must hand HDRP an RGBA base colour texture.

**[V]** Relevant HDRP Lit properties (`HDRP/Runtime/Material/Lit/Lit.shader`): `_BaseColorMap` (line 10), `_MaskMap` (14), `_NormalMap` (24), `_AlphaCutoffEnable` (97), `_AlphaCutoff` (98), `_DoubleSidedEnable` (152), `_DoubleSidedNormalMode` with Flip 0, Mirror 1, None 2 (153), `_MaterialID` with Translucent 5 (167), `_Thickness` (64), `_ThicknessMap` (65), `_DiffusionProfileAsset` and `_DiffusionProfileHash` (216, 217). The forward pass sets `AlphaToMask [_AlphaCutoffEnable]` (lines 644, 718).

**[V]** Public helpers in `HDRP/Runtime/Material/MaterialExtension.cs`, namespace `UnityEngine.Rendering.HighDefinition`: `HDMaterial.ValidateMaterial(Material)` (line 369), `HDMaterial.SetAlphaClipping(Material, bool)` (495), `HDMaterial.SetAlphaCutoff(Material, float)` (504), `HDMaterial.SetDiffusionProfile(Material, DiffusionProfileSettings)` (513), extension `material.SetMaterialType(MaterialId)` (553), enum `MaterialId` with `LitTranslucent = 5` (lines 117 to 133).

**[V]** Thin distant foliage: `TextureImporter.mipMapsPreserveCoverage` and `TextureImporter.alphaTestReferenceValue` exist for this; the reference value should match the material cutoff (`UNITY/Documentation/en/ScriptReference/TextureImporter-mipMapsPreserveCoverage.html`, `TextureImporter-alphaTestReferenceValue.html`).

**[I]** The glTF files mark many leaf materials `BLEND`. In a game those must be alpha-clipped opaque materials, not transparent ones, or sorting and shadows break.

### 6.3 Translucency

**[V]** Only five assets ship a translucency map, and none is a core forest asset: `periwinkle_plant`, `didelta_spinosa`, `flower_heliophila`, `cheiridopsis_succulent`, `leipoldtia_schultzei`. No tree, fern or grass has one, and none has a thickness map.

**[V]** HDRP's Translucent material type is meant for leaves ("to simulate a translucent object, such as a plant leaf") and needs a Diffusion Profile ("If you do not assign a Diffusion Profile, HDRP does not process the subsurface scattering"): `HDRP/Documentation~/Material-Type.md` lines 12 and 31. HDRP ships one for foliage: `HDRP/Runtime/RenderPipelineResources/FoliageDiffusionProfile.asset`. **[I]** With no thickness maps, leaves get a constant `_Thickness`.

### 6.4 Texture formats, and the recipe I recommend

**[V]** Official FBX packages are wasteful. The tree packages include 16-bit PNGs of every map (the `pine_tree_01` bark diffuse alone is 21.9 MB at 2k) and skip the alpha for some trees. Other FBX packages include the normal and roughness maps as EXR. That is why the FBX package of `pine_roots` is 103 MB at 2k while the same content as JPG is 33 MB.

**Recipe** (the "Recipe" column in 2.2): take the FBX whose `url` sits under the keys `fbx`, `2k`, `fbx` of the `/files` JSON (**[I]** the mesh is the same at every resolution: the FBX sizes at 1k, 2k, 4k and 8k are identical for most assets and differ by at most 3.4 KB for the rest, which fits changed texture path strings), then per material `diff` as JPG, `nor_gl` as JPG, `arm` as JPG, and the alpha as PNG. Read every URL and `md5` from the `/files` JSON; do not build URLs by hand, because map key names differ per asset (`Diffuse`, `leaves_diff`, `twig_diff`, `branches_diff`, `a_diff`).

**[V]** Normal maps: use `nor_gl`. "Unity uses Y+ normal maps, sometimes known as OpenGL format" (`UNITY/Documentation/en/Manual/StandardShaderMaterialParameterNormalMap.html` line 131).

**[V]** `arm` is "Packed: Ambient, Roughness, Metallic" (`FOREST/catalog/ph_web/models.html`). HDRP's mask map is metallic in R, ambient occlusion in G, detail mask in B, smoothness in A (`HDRP/Documentation~/Mask-Map-and-Detail-Map.md` lines 13 to 29). The builder must repack: R from `arm` blue, G from `arm` red, A as one minus `arm` green.

**[I]** JPG normals show compression blocks in close-ups. For tree bark at eye level, the `nor_gl` PNG is the safer choice. With PNG normals Kit A grows from 1,411 to 1,817 MB at 2k and from 1,982 to 3,488 MB at 4k.

### 6.5 No wind data

**[V]** No asset has a skeleton, a skin or an animation (glTF: zero animations and skins in all 61; no deformer or animation object types in any FBX `Definitions`).

**[V]** `pine_tree_01.fbx` has no vertex colours: in the 559 MB of it that I received, the first two tree geometries carry only `LayerElementNormal`, `LayerElementUV` and `LayerElementMaterial`.

**[V]** The glTF of `pine_tree_01`, `fir_tree_01` and `jacaranda_tree` does carry two colour sets, `COLOR_0` (8-bit RGBA) and `COLOR_1` (16-bit RGBA), on every sub-mesh including the foliage. No other tree has them. Their meaning is not documented anywhere I could read, and I did not download the binary data, so **UNVERIFIED** whether they encode anything useful for wind (they may be leftovers of the tree generator). They do not reach Unity through the FBX of `pine_tree_01`. **UNVERIFIED** for the FBX of `fir_tree_01` and `jacaranda_tree`. Plan as if there is no wind data.

**[I]** Wind therefore has to be procedural: a vertex shader that bends by height above the tree's origin and adds flutter on the leaf material only. The separate bark and twig materials give a free mask for that. HDRP's stock `Lit.shader` has no wind, so this needs a Shader Graph. Whether a Shader Graph can be produced by a headless editor script is outside what I checked.

### 6.6 Scale, units, axes of the FBX files

**[V]** All 61 FBX files are binary FBX 7400 written by Blender's exporter ("Blender (stable FBX IO)", Blender 3.0 to 4.5). All have `UpAxis = 1` (Y), `UpAxisSign = 1`, `FrontAxis = 2` (Z), `CoordAxis = 0` (X), `UnitScaleFactor = 1.0` and `OriginalUnitScaleFactor = 1.0` (`FOREST/research/_fbx_headers.txt`). A unit scale factor of 1 means the file's unit is the centimetre.

**[V]** Every object carries `Lcl Rotation = (-90, 0, 0)` and `Lcl Scaling = (100, 100, 100)`, with these exceptions: `shrub_02` (four different arbitrary rotations), `flower_gazania` (arbitrary rotations, scales 13 to 21), `leipoldtia_schultzei` (scale 45.49), one object in `rock_moss_set_02` (scale 67.9), one in `potted_plant_01` (scale 100.2).

**[I]** That is the standard Blender pattern: mesh data is stored Z-up in metres and the object node converts it with a -90 degree X rotation and a scale of 100 in a centimetre file. Net real-world size is correct. The glTF bounds confirm the intended sizes in metres (a 20.37 m pine, a 0.43 m fern).

**[V]** Unity side: `ModelImporter.useFileScale` applies `fileScale`, which for FBX is "the multiplier that converts the unit of measurement, of the imported FBX file, to meters". `ModelImporter.bakeAxisConversion`: "When enabled, Unity transforms the geometry and animation data in order to convert the axis. When disabled, Unity transforms the root GameObject of the hierarchy" (`UNITY/Documentation/en/ScriptReference/ModelImporter-fileScale.html`, `ModelImporter-useFileScale.html`, `ModelImporter-bakeAxisConversion.html`).

**UNVERIFIED**: the exact transform values Unity will show on the imported child objects (the widely reported result for Blender files like these is rotation -90 on X and scale 100 on each child, with a correct world size). I could not import anything in this phase.

**The trap that follows [I]:** if the builder takes a `Mesh` out of the imported model and puts it on a fresh GameObject with identity rotation and scale 1, the tree may come out lying on its side and a hundred times too small. The builder should keep each imported child's `localRotation` and `localScale`, and a test should assert the world-space bounds against the numbers in table 2.3.

### 6.7 Layout of the packs

**[V]** The variants in a pack are separate objects offset from each other, not at the origin. `pine_tree_01`: translations of -600, +100 and +800 cm on X. `fern_02`: a 1 m grid. The builder must zero each variant's position when it makes prefabs.

**[V]** Each object's own origin is at ground level, and the mesh reaches slightly below it (lowest Y between -0.01 and -0.21 m). Trees have a small root mound. That is useful: they can be sunk a few centimetres into the terrain.

**[I]** `shrub_02` needs care: its four objects are rotated arbitrarily, so their mesh-local bounds in table 2.3 are not their upright size.

### 6.8 The `_LOD0` suffix will trigger Unity's LOD Group import

**[V]** "If the model contains meshes named _LODx, Unity imports them as LOD Group components" (`UNITY/Documentation/en/Manual/lod/mesh-lod-generate-lods.html`).

**[I]** Most of the tree and plant packs name every object `<variant>_LOD0`. Unity will probably create one LOD Group and put all variants of the pack into its level 0. **UNVERIFIED** in practice. The builder should not instantiate the imported model as a whole; it should pick the child objects by name and build its own prefabs.

**[V]** The same page adds: "If you select Generate mesh LODs for such models, Unity generates Mesh LODs for each imported LOD Group index. This workflow is not recommended". **[I]** So turning on `generateMeshLods` for a `_LOD0`-named Poly Haven model lands in the combination Unity advises against. Two ways out: build prefabs that use only the imported `Mesh` on a plain `MeshRenderer` with no LOD Group, or skip the importer option and call `MeshLodUtility.GenerateMeshLods` on a copy of the mesh from the editor script. `ModelImporter` has no property to switch the `_LOD` name detection off (I listed all its documented members in `UNITY/Documentation/en/ScriptReference`).

### 6.9 The catalogue's `polycount` and `dimensions` are not reliable

**[V]** For geometry-nodes assets the published numbers describe the scattered Blender scene, not the exported file:

| Asset | Catalogue polycount | Triangles actually in the export |
|---|---|---|
| `moss_01` | 246,170 | 204 (twelve cards of 6 to 24 triangles) |
| `grass_medium_01` | 1,606,633 | 24,730 (seventeen clumps) |
| `grass_medium_02` | 1,043,926 | 7,842 |
| `grass_bermuda_01` | 223,596 | 941 |
| `celandine_01` | 1,746,764 | 8,966 |
| `jacaranda_tree` | 312,356 | 3,863,832 |
| `anthurium_botany_01` | 4,134 | 67,152 |

**[V]** For plain LOD assets it is the sum over all LODs: `dead_tree_trunk_02` publishes 155,864 and its LOD0 is 83,128; `single_root` publishes 114,424 and its LOD0 is 61,027. Both ratios are 1.875, which is 1 + 1/2 + 1/4 + 1/8.

**[V]** `dimensions` of `pine_tree_01` is 26.7 by 1.3 by 17.6 m while its tallest tree is 20.37 m.

Use the numbers in section 2, not the catalogue.

### 6.10 Some "assets" are scatter ingredients, not placeable props

**[V]** `moss_01` is twelve cards 1 to 4 cm in size. `grass_bermuda_01` is twenty-one tufts 2 to 15 cm tall with 8 to 268 triangles. `grass_medium_01` clumps are 5 to 32 cm tall. `nettle_plant` is at most 22 cm tall. These are meant to be scattered by the hundred thousand.

**[I]** For a 120 by 120 m area, moss sprigs cannot be placed one by one. Either combine a few hundred into "moss patch" meshes in the editor script and place those at hero spots, or leave moss to the ground texture and the mossy rocks.

### 6.11 glTF and FBX exports do not always match

**[V]** `shrub_01`: the FBX has nine objects (`shrub_01_a_LOD0` to `_i_LOD0`), the glTF has one (`shrub_01_a`, 156,012 triangles). `shrub_04`: FBX four objects, glTF one. Unity has no glTF importer in this project anyway (`Packages/manifest.json` lists no glTF package), so FBX is the format to use. Treat the triangle counts of those two as approximate.

### 6.12 Materials and draw calls per tree

**[V]** Each tree is one mesh with several sub-meshes: four for the trees of `pine_tree_01` and `fir_tree_01`, three for the medium saplings, two for the small ones. `pine_tree_01` defines six materials: `_bark`, `_trunk_a`, `_trunk_b`, `_trunk_c`, `_twig`, `_dead_branches`; in the glTF, `_trunk_c` and `_dead_branches` reuse the bark textures. Island trees, `jacaranda_tree` and `tree_small_02` have three sub-meshes each. So a tree costs three or four draw calls per pass before instancing.

### 6.13 No collision meshes

**[I]** No asset ships a collider or a simplified proxy (object lists in `_fbx_tails.txt` show only render meshes). Using multi-million-triangle render meshes as `MeshCollider`s is not an option. Trunk colliders and NavMesh obstacles must be generated by the builder (capsules sized from the trunk radius).

### 6.14 Import cost and download robustness

**[I]** `pine_tree_01_2k.fbx` is 618 MB with a single geometry node of 262 MB compressed. Importing it will be slow and memory-hungry. Import the big trees one at a time and budget minutes, not seconds, per file. **UNVERIFIED** how long.

**[V]** `dl.polyhaven.org` once ignored an HTTP range and sent a whole file (see the deviation note). Download scripts should check the received size and `md5` against `/files` and should never assume a range is honoured.

---

## Recommendations for the build

1. **Build Kit A.** It is the only option that can reach photographic believability with CC0 assets. Decide with the owner whether a misty conifer island fits the game. If a tropical look is mandatory, CC0 scans cannot deliver it and the plan must change (for example a paid or login-gated source, which the asset rule currently forbids).

2. **Start with the light half of Kit A, 268 MB at 2k.** `fir_sapling_medium`, `pine_sapling_small`, `fir_sapling`, `fern_02`, `grass_medium_01`, `moss_01`, `tree_stump_01`, `tree_stump_02`, `pine_roots`, `dead_tree_trunk`, `dead_tree_trunk_02`, `dry_branches_medium_01`, `bark_debris_01`, `rock_moss_set_01`, `rock_moss_set_02`. This already gives a believable forest floor and a mid storey.
   - `fern_02` (784 to 2,384 triangles per clump), `grass_medium_01`, `dry_branches_medium_01` (about 5,500 each) and the rocks (5,000 to 16,548 each) are light enough to scatter as they are.
   - The scans are heavy for what they are: `dead_tree_trunk` is 101,802 triangles for a thin 3 m branch, each `pine_roots` patch is about 81,000, each 0.5 m chunk of `bark_debris_01` is 38,488 to 65,062, the stumps are 41,046 and 62,345. Turn on `ModelImporter.generateMeshLods` for these. They are connected organic surfaces, which is the case Unity's documentation says the generator handles best. `dead_tree_trunk_02` already has LOD0 to LOD3 in its FBX.

3. **Run one LOD experiment before downloading the giants.** Import `fir_sapling_medium` (49 MB FBX, three trees of 0.42 to 0.69 million triangles) with `ModelImporter.generateMeshLods = true`, then render each tree at `Renderer.forceMeshLod` 0, 2, 4, 6 with the existing capture harness and look at the images. This answers the biggest open question (does Unity's Mesh LOD keep conifer foliage?) for 63 MB of download.
   - If it holds up: add `fir_tree_01` (278 MB), then `pine_sapling_medium` (205 MB), then `pine_tree_01` (660 MB), in that order of cost per benefit.
   - If foliage vanishes, as Unity's own documentation warns it can, there are two fallbacks. Both rely on the fact that 96 to 99.7 percent of each conifer is the foliage sub-mesh (section 6.1).
     - **Without new software:** an editor script splits each tree by sub-mesh, keeps the wood as it is (at most about 113,000 triangles), and thins the foliage sub-mesh by deleting a random share of its separate pieces. That is how foliage LODs are usually made, and it keeps the silhouette better than edge collapse. **UNVERIFIED** how it looks on these meshes; it needs the same render check.
     - **With Blender:** ask the owner for permission to install Blender (free, no account) and export the authored `_LOD1` and `_LOD2` trees from the `.blend` files headlessly, named so that Unity builds LOD Groups. The `.blend` files are 42 MB (`fir_sapling_medium`), 219 MB (`fir_tree_01`), 200 MB (`pine_sapling_medium`) and 571 MB (`pine_tree_01`) at 2k. First check on the smallest one that tree LODs really are in there and how heavy they are.
   - Either way, plan a billboard or impostor for trees beyond roughly 60 to 80 m (my planning figure, not a measurement). No such asset exists; it would be baked by an editor script.

4. **Do not scatter the seedlings.** `pine_sapling_small` and `fir_sapling` are 120 to 160 thousand triangles each with no LODs. Use them only if the Mesh LOD experiment works on them too, and then only near paths.

5. **Use FBX, never glTF, and never the official packages.** Download the FBX plus `diff` JPG, `nor_gl` JPG (PNG for bark seen up close), `arm` JPG and the alpha PNG, taking URLs and md5 from `/files`. Compose an RGBA base colour from `diff` and alpha in the editor script for every foliage material, even where the diffuse PNG claims to have alpha; that is one code path and it is deterministic. Repack `arm` into an HDRP mask map.

6. **Build materials in code.** Ignore the FBX materials (`ModelImporterMaterialImportMode.None` exists for this). For foliage: alpha clipping on, double-sided on, Translucent material type with HDRP's `FoliageDiffusionProfile.asset`, constant thickness, and coverage-preserving mipmaps on the base colour texture with the same cutoff as the material.

7. **Build prefabs from named child objects, not from the imported model.** Keep each child's imported rotation and scale, zero its position, and add a test that checks world-space bounds against table 2.3 (for example `pine_tree_01_a_LOD0` is 7.60 by 20.37 by 8.31 m and `fern_02_b` is 0.99 by 0.43 by 0.89 m). This one test catches the scale-100, the -90 degree and the LOD-Group traps together.

8. **Fight repetition where it is cheap.** Random yaw and 0.85 to 1.15 scale on trees; 0.8 to 1.6 on ferns; a small per-instance colour shift; keep `fir_tree_01_c` rare; plant ferns in drifts rather than evenly. Try a few `quiver_tree_02` as cycad stand-ins and keep them only if they pass a render check.

9. **Give the stealth game its cover on purpose.** The pine wood is open at eye height. Place `fir_sapling_medium` in thickets, use the 1.1 to 1.8 m rocks of `rock_moss_set_01` and the stumps as crouch cover, and test `searsia_lucida` and `othonna_cerarioides` as hide bushes in the real lighting before adopting them.

10. **Hide the scan rims.** Sink `tree_stump_01`, `tree_stump_02` and `pine_roots` a few centimetres, use `forest_ground_04` (same collection) under them, and ring them with ferns and bark debris.

11. **Leave out**: all `flower_*`, the succulents, `dandelion_01`, `celandine_01`, `quiver_tree_*`, the red-soil `root_cluster_*` and `single_root`, and every glTF package.

12. **Accept for the first version**: no wind (or a very simple height-based sway if a Shader Graph is available), constant leaf thickness, and moss only on rocks and in the ground texture.

### Open questions for the next phase

| Question | Why it matters | Cheapest way to answer |
|---|---|---|
| Does Unity Mesh LOD keep conifer foliage? | Decides whether Blender is needed | Recommendation 3 |
| How many triangles do the `.blend` LOD1 and LOD2 trees have? | Decides whether the authored LODs are game-ready | Download `fir_sapling_medium_2k.blend` (42 MB), `zstd -d`, list objects; or open in Blender |
| What transform does Unity give the imported children? | Prefab building | Import `fern_02` (0.2 MB FBX) and log `localRotation`, `localScale`, `Renderer.bounds` |
| Does the alpha channel of the RGBA diffuse PNGs hold the mask? | Could save the compose step | Not worth answering; always compose |
| Do the optional evergreen bushes pass in a conifer forest? | Stealth cover | One capture with and without them |
