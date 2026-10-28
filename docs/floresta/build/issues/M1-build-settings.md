# M1: the scene builder no longer appends the forest scene to Build Settings by default

Written 2026-10-13 by the M1 integrator.

## What

Spec 11.5, step 12, says `ForestSceneBuilder.Build` appends `Assets/Game/Scenes/Forest.unity` to
`EditorBuildSettings.scenes`. That rewrites `ProjectSettings/EditorBuildSettings.asset`.

The build rules for this effort say: never edit a file that existed before it, and they name
`ProjectSettings`. That file is one of the 59 in `backup/pre-forest.md5`. The two instructions contradict
each other (the spec also says, in section 0, "No existing file is edited").

The first M1 build (`logs/M1-02-scene-build.log`) did append the scene. The file then differed from the
backup by exactly three lines:

```
>   - enabled: 1
>     path: Assets/Game/Scenes/Forest.unity
>     guid: 45b19c56372d14efd9bc478f2336fdcc
```

## What I did

- Put `ProjectSettings/EditorBuildSettings.asset` back from `backup/pre-forest.tgz` (md5
  `b6d9ab800e0f7d6c9db9d9dc91da1aca`, equal to the list). The changed copy is kept as
  `out/M1/EditorBuildSettings.after-first-build.asset`.
- `ForestSceneBuilder.cs`: the append now happens only with the argument `-forestAddToBuildSettings`.
  Without it the builder logs one line saying the scene was not added. `AppendToBuildSettings` itself is
  unchanged (it appends, never moves or removes; the greybox stays first).

`tools/check-preexisting.sh` after the final M1 runs: 59 files, 0 differ.

## Why this side of the contradiction

It is the one that can be undone by anybody in one step, and it costs nothing today: nothing in the
forest needs the scene in Build Settings. The forest PlayMode tests load the scene by path
(`EditorSceneManager.LoadSceneAsyncInPlayMode`), the EditMode tests open it by path, and
`Greybox_StillFirstInBuildSettings` only asks that the greybox is first (it passes either way).
A player build of the forest (M6) would need it.

## Needs a decision

Whether the forest scene belongs in Build Settings is for the owner. To apply the spec's step as written:

```
Unity -batchmode -projectPath . -executeMethod Gamex.EditorTools.ForestSceneBuilder.Build -forestAddToBuildSettings -quit
```

or add the scene by hand in File > Build Profiles. A menu run ("Gamex/Forest/3 Rebuild Forest Scene") has
no arguments and therefore does not add it.
