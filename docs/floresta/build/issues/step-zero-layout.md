# Step zero: where the files went

Written 2026-10-13 at step zero.

## What

Spec section 11.2 describes step zero as one commit that creates every version-1 file of 11.3 inside
the project, with the signatures of 11.4 and bodies that throw. This run did it differently, as the
step-zero task instructed.

## What was done

| Item | Where | Note |
|---|---|---|
| The frozen contract | `/Users/william/Documents/GitHub/gamex/Assets/Game/Editor/Forest/ForestContracts.cs` | Real project code, byte-identical to the block of spec 11.2 |
| The manifest copy | `/Users/william/Documents/GitHub/gamex/Assets/Game/Editor/Forest/forest-asset-manifest.json` | Byte-identical copy of `asset-manifest.json` (md5 `0f9fb4231d3b52c6189612cfcdde5b1b`). Spec 4.4 says this copy is the one the tools read "after step zero", and `ForestPaths.ManifestPath` in the contract points at it. WP1 still owns it and is its only later writer |
| The stubs of 11.4 | `stubs/WP1.cs` to `stubs/WP5.cs` in this build folder, **not** in the project | One file per package. Each owner creates the real files of 11.3 in the project |

No `.meta` file was written for the new folder or the two new files. Unity creates them the first time
it opens the project.

## Why it matters

- With only `ForestContracts.cs` present, the project's editor assembly still compiles
  (`logs/step0-selftest.log`, case "greybox sources + contract").
- While the packages are being written, the files in the project do not form a complete assembly:
  a file of one package can refer to a class another owner has not created yet. Unity must not be
  expected to compile the project until every one of the 26 remaining editor files of 11.3 exists.
  Until then each owner proves their own files with `compile.sh` against the other packages' stubs,
  and the first full check is `compile.sh <out> Assets/Game/Editor/Forest/*.cs` with no stubs.
