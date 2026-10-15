# Third-party assets: Poly Haven

Everything in this folder was made by Poly Haven (https://polyhaven.com) and is published under
**CC0 1.0 Universal** (https://creativecommons.org/publicdomain/zero/1.0/). Poly Haven's licence page
(https://polyhaven.com/license) says the assets may be used for any purpose, including commercial
work, may be redistributed, and need no attribution. This file exists to record where each file
came from, not because the licence requires it.

Two things CC0 does not cover: trademarks and other third-party rights, and Poly Haven's own
website content (logos, thumbnails, preview renders). None of that is in this folder, and none of
it may be added.

## How the files got here

They are downloaded by `Tools/forest/fetch_assets.py` from the list in
`Assets/Game/Editor/Forest/forest-asset-manifest.json`. The manifest holds, for every file, the URL
that Poly Haven's `/files` API returned on 2026-10-13, the byte size and the md5. The tool checks
both after each download, and `Gamex/Forest/1 Import Assets` checks them again inside Unity.

```
python3 -I Tools/forest/fetch_assets.py Assets/Game/Editor/Forest/forest-asset-manifest.json . --milestone M2
```

Files are renamed at download time to `<material or id>__<map>.<ext>`, because Poly Haven's own
suffixes vary. A `Source~` folder holds maps that Unity must not import (it ignores names ending in
`~`): they are inputs of the texture packer and of the sky calibration only.

Nothing here is edited by hand, and no build step deletes or renames anything in this folder.
Only the mesh (FBX) and the maps listed in the manifest are fetched: never the glTF packages or
the official texture packages.

## Assets

"Milestone" is the build milestone that first needs the asset. A folder exists only for the
milestones that have been fetched. Version 1 of the forest is M1 to M4; M5 and M6 are deferred.

| Id | Kind | Source page | Licence | Milestone | Files | MB |
|---|---|---|---|---|---|---|
| `forest_leaves_04` | Ground texture, 2k | https://polyhaven.com/a/forest_leaves_04 | CC0 1.0 | M1 | 4 | 27.4 |
| `forest_ground_06` | Ground texture, 2k | https://polyhaven.com/a/forest_ground_06 | CC0 1.0 | M1 | 4 | 33.7 |
| `forest_leaves_02` | Ground texture, 2k | https://polyhaven.com/a/forest_leaves_02 | CC0 1.0 | M1 | 4 | 13.8 |
| `rocky_trail` | Ground texture, 2k | https://polyhaven.com/a/rocky_trail | CC0 1.0 | M1 | 4 | 37.3 |
| `forest_ground_04` | Ground texture, 2k | https://polyhaven.com/a/forest_ground_04 | CC0 1.0 | M1 | 4 | 18.6 |
| `mud_forest` | Ground texture, 2k | https://polyhaven.com/a/mud_forest | CC0 1.0 | M1 | 4 | 33.0 |
| `mossy_rock` | Ground texture, 2k | https://polyhaven.com/a/mossy_rock | CC0 1.0 | M1 | 4 | 17.7 |
| `forrest_ground_03` | Ground texture, 2k | https://polyhaven.com/a/forrest_ground_03 | CC0 1.0 | M1 | 4 | 17.1 |
| `overcast_soil_puresky` | Sky (HDRI), 4k | https://polyhaven.com/a/overcast_soil_puresky | CC0 1.0 | M1 | 2 | 22.0 |
| `rock_moss_set_01` | Model, 4k | https://polyhaven.com/a/rock_moss_set_01 | CC0 1.0 | M2 | 4 | 104.3 |
| `rock_moss_set_02` | Model, 4k | https://polyhaven.com/a/rock_moss_set_02 | CC0 1.0 | M2 | 4 | 108.0 |
| `fern_02` | Model, 2k | https://polyhaven.com/a/fern_02 | CC0 1.0 | M2 | 5 | 3.7 |
| `grass_medium_01` | Model, 2k | https://polyhaven.com/a/grass_medium_01 | CC0 1.0 | M2 | 5 | 9.6 |
| `tree_stump_01` | Model, 2k | https://polyhaven.com/a/tree_stump_01 | CC0 1.0 | M2 | 4 | 30.0 |
| `tree_stump_02` | Model, 2k | https://polyhaven.com/a/tree_stump_02 | CC0 1.0 | M2 | 4 | 32.7 |
| `dead_tree_trunk_02` | Model, 2k | https://polyhaven.com/a/dead_tree_trunk_02 | CC0 1.0 | M2 | 4 | 35.2 |
| `dry_branches_medium_01` | Model, 2k | https://polyhaven.com/a/dry_branches_medium_01 | CC0 1.0 | M2 | 4 | 11.0 |
| `fir_sapling_medium` | Model, 2k | https://polyhaven.com/a/fir_sapling_medium | CC0 1.0 | M3 | 10 | 158.6 |
| `fir_tree_01` | Model, 2k | https://polyhaven.com/a/fir_tree_01 | CC0 1.0 | M4 | 17 | 348.6 |
| `kloofendal_misty_morning_puresky` | Sky (HDRI), 4k | https://polyhaven.com/a/kloofendal_misty_morning_puresky | CC0 1.0 | M5 | 2 | 18.3 |
| `qwantani_dusk_2_puresky` | Sky (HDRI), 4k | https://polyhaven.com/a/qwantani_dusk_2_puresky | CC0 1.0 | M5 | 2 | 23.0 |
| `qwantani_night_puresky` | Sky (HDRI), 4k | https://polyhaven.com/a/qwantani_night_puresky | CC0 1.0 | M5 | 2 | 26.4 |
| `rock_face_01` | Model, 4k | https://polyhaven.com/a/rock_face_01 | CC0 1.0 | M6 | 4 | 85.6 |
| `rock_face_02` | Model, 4k | https://polyhaven.com/a/rock_face_02 | CC0 1.0 | M6 | 4 | 89.0 |

Totals: M1 221 MB, M2 335 MB, M3 85 MB, M4 349 MB (version 1: 988 MB); M5 68 MB, M6 175 MB.

`fir_sapling_medium` also has its glTF geometry (`Source~/fir_sapling_medium.gltf` and `.bin`, 74.2 MB, added at M3):
the FBX carries no UV layer, so the kit takes positions, normals and UVs from the glTF and checks them against the FBX.
The glTF's own textures are not downloaded. Totals with it: M3 159 MB, version 1 1,062 MB.

`fir_tree_01` also has its glTF geometry (`Source~/fir_tree_01.gltf` and `.bin`, 478.5 MB, added at M4) for the same
reason: its FBX carries no UV layer either. `fir_tree_01_trunk_c` has no UVs in the glTF as well, so that one material
is drawn with the triplanar variant of `fir_tree_01_bark` and its own `trunk_c` maps are downloaded but unused. Totals
with it: M4 827 MB (19 files), version 1 1,540 MB.

## Derived files

`Assets/Game/Scenes/ForestKit/` holds what the build derives from these assets: packed mask maps,
foliage base colour with alpha, materials, simplified and thinned meshes, prefabs. Those are our
own arrangement of CC0 data and carry no further restriction.
