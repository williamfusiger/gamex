# WP4 Light: decisions the spec left open, and where the code differs from its wording

Written 2026-10-13 by the WP4 owner. Unity was not started. The three files compile with zero errors and
zero warnings against the contract and the stubs of WP1, WP2, WP3 and WP5 (`out/WP4/Gamex.EditorTools.dll.log`
is empty). The public surface is identical to `stubs/WP4.cs` (compared with `monop`, `out/WP4/monop.*.txt`).

Nothing was needed from another package that it does not expose, and no contract type was insufficient.

## Differences from the spec's wording

| # | What | Why | What was done |
|---|---|---|---|
| 1 | `ForestSky.Integrate` does not sample `cos(theta) * sin(theta) * (pi / H)` at the row centre (section 8.4, step 2). It integrates that weight exactly over each row's band of polar angle | The midpoint sum is 2.6 % high on an 8-row image and 0.0006 % high at 512 rows. A unit test may feed a small image; the exact weight gives pi for a constant image at any size, and splits a row that straddles the horizon when the height is odd | Same integral, same result at 1k: on a non-constant 1024 x 512 test image the two agree to 6 parts per million (`out/WP4/harness/harness.log`, "spec's midpoint formula agrees at 1k") |
| 2 | `ForestSky.LoadCubemap` throws when the `.hdr` is on disk but does not load as a `Cubemap` (after one `AssetDatabase.ImportAsset` of that path). The spec says only "null when not downloaded" | Returning null would make a wrongly imported sky look like a missing one, and the preset would be skipped without a word | Null only when the file is not on disk or the id is not a sky of the manifest |
| 3 | The exposure override also sets `histogramUseCurveRemapping` to false. Section 8.3 and 9.2 do not list that field | With it on, HDRP replaces `limitMin` and `limitMax` by values from a curve (`HD/Runtime/RenderPipeline/HDRenderPipeline.PostProcess.cs:1519, 1538-1539`). The project default leaves it off today; the clamp is the guard against black and white frames, so it is pinned | One extra override in every automatic-exposure preset profile |

## Decisions where the spec names no value or no switch

| Topic | Decision |
|---|---|
| Switch for the sky fallback of section 8.4 (`SkyIntensityMode.Multiplier`) | `ForestLighting.CalibrateSkyInLux` (private, default true). When false the sky uses `multiplier = skyLux / AssumedRawSkyLux`, with `AssumedRawSkyLux = pi` (a sky of uniform radiance 1). That number has never been rendered: tune it from a capture before relying on it. A failed calibration **throws**; it does not fall back by itself, because a silent fallback would pass the audit with an untuned sky |
| `exposureFixed` of OvercastMist (the table of 8.2 has no row) | 9.5 EV, between the two limits. It is read only if `fixedExposure` is switched on (the last fallback of risk 5), so that switch alone cannot give a white frame |
| `Atmosphere_Inspect` values ("no fog, fixed exposure, neutral grading, the overcast sky") | The default preset's sky and shadow settings; fog, bloom, grain, vignette and motion blur overridden to off; contrast, saturation and white balance 0; fixed exposure `log2(skyLux / 3.14)` = 10.64 EV (neutral for the sky alone) |
| When `Atmosphere_Inspect` is written | Every scene build rewrites it. `InspectProfile()` returns an existing profile untouched when it still has its sky and a fixed exposure (captures must not write inside Assets) and creates it otherwise |
| Rewriting a profile | The existing asset is emptied and refilled in place, so its GUID is stable and nothing in `ForestData` is deleted |
| `Build` with an unknown preset name, or a preset whose sky is not downloaded | Throws. Other presets with a missing sky are skipped with one log line, as the spec says. Null or empty means the default preset |
| "Brightest sky column" | The column with the most luminance weighted by solid angle over the upper hemisphere; the first one wins a tie. Returned as 0 to 360, 0 at the centre column, increasing to the right |
| `ApplyPresetFromArgs` in batch mode | If the scene Unity opened has no forest lighting and `Forest.unity` exists, it opens that scene first. It throws when the preset is absent or the scene cannot be saved |
| `ConfigureCamera` and custom frame settings ("none") | Sets `customRenderingSettings = false`. Adds `HDAdditionalCameraData` if the camera has none |
| `ForestLightingPresets.Get` | Returns a new record on every call, so a caller cannot change a preset for the others |

## Guards added beyond the ones the spec lists

- `Validate` throws on a preset whose values lie outside the ranges of the HDRP parameters they are written to
  (`Override` bypasses the clamping), or whose exposure limits are not ordered. Checked outside Unity: it accepts
  OvercastMist and rejects 23 deliberately broken copies.
- A warning (not an error: the thresholds are heuristics) when a preset's exposure cannot give a readable frame on
  open ground: `exposureMax` more than 3 stops under neutral, `exposureMin` more than 1 stop over, or a fixed
  exposure more than 3 stops from neutral.
- A warning when the calibration image's lower hemisphere gives less than half or more than twice the light of
  the upper one, and when the sky cubemap is not in an HDR format.
- A warning at build time for any other active global volume or shadow-casting directional light in the scene
  (the audit makes these errors).

## Checked without Unity, and not checked

Run on Unity's bundled Mono against the compiled assembly (`out/WP4/harness/Harness.cs`, 54 checks, all pass):
`ForestSky.Integrate` (constant image gives pi at six sizes including odd heights; row order; colour rule;
a cosine lobe gives 2 pi / 3; agreement with the spec's formula; azimuth of a bright column; bad input),
the values of section 8.2 in the preset record, and the private `Validate` and `NeutralEv`.

Not run, because it needs the Editor: everything that touches a Unity object. In particular
`ImageConversion.LoadImage` on Poly Haven's 1k EXR (still UNVERIFIED, Appendix B), refilling a profile's
sub-assets in place, `AssetDatabase.ImportAsset` on a folder that exists on disk only (`EnsureFolder` falls back
to `Refresh`), and every rendered result.
