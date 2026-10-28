# The compile scripts use Unity's real reference lists, not the simplified set

Written 2026-10-13 at step zero. Evidence: `logs/step0-refset.log`.

## What

The task text describes one reference set for all forest code: `netstandard.dll` 2.1, the
`mscorlib`, `System` and `System.Core` shims from `NetStandard/compat/2.1.0/shims/netfx`, every
`UnityEngine*.dll` and `UnityEditor*.dll`, and "as needed" a list of package assemblies that includes
`Unity.RenderPipelines.Core.Editor`, `Unity.RenderPipelines.HighDefinition.Editor`,
`Unity.AI.Navigation.Editor` and `Unity.InputSystem`.

`compile.sh` and `compile-tests.sh` do not use that set. They use three lists copied from the response
files Unity itself wrote when it last compiled this project
(`Library/Bee/artifacts/200b0aE.dag/Gamex.EditorTools.rsp` and `Gamex.Tests.PlayMode.rsp`).

## Why

The goal stated for the scripts is to prove that code compiles against the same references Unity
uses. The simplified set differs from Unity's in two ways, and the second one gives false greens.

1. **Editor assemblies are not compiled against .NET Standard.** Unity compiles `Gamex.EditorTools`
   against the `unity-4.8-api` profile (defines `NET_4_6`, `NET_UNITY_4_8`). The assembly Unity built
   names `mscorlib` as its core library; `Gamex.Runtime` and `Gamex.Tests.PlayMode` name `netstandard`.
   Compiled with the simplified set, the same greybox sources come out naming `netstandard`.
   In practice the two profiles agreed on all 38 base-library members that were tried (36 compile in
   both, 2 fail in both), so this difference is small. It is still a difference.
2. **Four of the listed package assemblies are not referenced by the asmdef.**
   `Gamex.EditorTools.asmdef` references `Gamex.Runtime`, `Unity.AI.Navigation`,
   `Unity.RenderPipelines.Core.Runtime` and `Unity.RenderPipelines.HighDefinition.Runtime`, and the
   asmdef may not be edited. A file that uses `AdaptiveProbeVolumes` (Core.Editor),
   `Unity.AI.Navigation.Editor` or `UnityEngine.InputSystem` compiles with 0 errors when those
   assemblies are on the list and would then fail in Unity, taking the whole editor assembly with it.
   With the scripts as built, that file fails with three errors (`probes/NotInAsmdef.cs`).

## What was done

- `refs/make-refs.py` reads Unity's two response files as data, keeps their `-define:` and `-r:`
  arguments, and writes `refs/editor.rsp`, `refs/editmode.rsp` and `refs/playmode.rsp`. The lists are
  frozen: the scripts read these copies, not Unity's build folder.
- Three deliberate differences from Unity's own arguments, each stricter or neutral:
  - Unity's reference assemblies in its build folder (`*.ref.dll`) are replaced by the full assemblies
    in `Library/ScriptAssemblies`, as the task text says. Same public surface, stable location.
  - `nunit.framework.dll` is taken from the Unity install (`BuiltInPackages/com.unity.ext.nunit`), as
    the task text says. It has the same md5 as the PackageCache copy Unity uses.
  - `Mono.Cecil.dll` and `Unity.Collections.LowLevel.ILSupport.dll` are left out of the editor list.
    Unity adds them to every assembly automatically; forest code has no use for them (spec 11.1), and
    their paths carry a hash that can change.
- The flags are the ones the task gives (`-nologo -nostdlib -target:library -langversion:9.0
  -nowarn:CS1701,CS1702,CS0649`) plus `-fullpaths -utf8output -preferreduilang:en-US`, and Unity's
  full list of defines instead of `UNITY_EDITOR` alone (which is in that list).
- The PlayMode list is Unity's list for the existing `Gamex.Tests.PlayMode` (which is .NET Standard 2.1,
  the profile the task text describes) plus the two render pipeline runtime assemblies that spec 12.3
  adds to the new asmdef. It does not hold the editor assembly.

## What this leaves open

- Unity's analyzers and source generators are not run by the scripts (tried once by hand: no output).
- If the project's packages or the Unity version change, the lists must be rebuilt with
  `python3 -I refs/make-refs.py /Users/william/Documents/GitHub/gamex /Applications/Unity/Hub/Editor/6000.6.0f1 refs`
  after Unity has compiled once, and `tools/selftest.sh` run again.
- The new test asmdefs do not exist yet, so the two test lists are derived from spec 12.3, not copied
  from a Unity compile of those assemblies. The first Unity launch that compiles them is the real check.
