# Rough Seas 5.0.1 rowing animation lifecycle

## Source diagnosis

The supplied two crash reports identify Vintage Story 1.22.7, Rough Seas 5.0.1
and Quiet Rough Seas 1.8.0. Their call chains enter `MountStrokes.Pace` line 39
through `EntityBehaviorPropulsion.Drive` and rowing during boat rendering.

Pinned upstream source is Rough Seas tag `v5.0.1`, commit
`953797c053133d56b7b1ca6f7599e4f982547a6e`:

- [MountStrokes.cs, lines 24–42](https://github.com/hoblin/rough-seas/blob/953797c053133d56b7b1ca6f7599e4f982547a6e/RoughSeas/Propulsion/MountStrokes.cs#L24-L42):
  `animator?.GetAnimationState` checks the **manager**, not its `Animator`.
  Local client players use both TP and self-FP managers. Other entities use the
  selected manager. `DrawnShare` has the same lookup at line 25.
- [Finish, lines 86–105](https://github.com/hoblin/rough-seas/blob/953797c053133d56b7b1ca6f7599e4f982547a6e/RoughSeas/Propulsion/MountStrokes.cs#L86-L105):
  another state lookup precedes essential stroke/finishing bookkeeping.
- [Drive](https://github.com/hoblin/rough-seas/blob/953797c053133d56b7b1ca6f7599e4f982547a6e/RoughSeas/Propulsion/EntityBehaviorPropulsion.cs#L124)
  invokes Pace on the rendering route identified in the reports.

Official game API source snapshot `63d33f70018c606fb0f70ead900fe90625fbd7b2`
identifies itself as 1.22.7 in [GameVersion.cs](https://github.com/anegostudios/vsapi/blob/63d33f70018c606fb0f70ead900fe90625fbd7b2/Config/GameVersion.cs).
This is a pinned source snapshot, not an invented release tag:

- [PlayerAnimationManager](https://github.com/anegostudios/vsapi/blob/63d33f70018c606fb0f70ead900fe90625fbd7b2/Common/Entity/PlayerAnimationManager.cs#L148-L155)
  performs FP/-ifp routing and calls the base lookup. An uninitialized manager
  with FP enabled also dereferences the initialization context in this route.
- [AnimationManager](https://github.com/anegostudios/vsapi/blob/63d33f70018c606fb0f70ead900fe90625fbd7b2/Common/Model/Animation/AnimationManager.cs#L130-L133)
  dereferences `Animator` directly. `LoadAnimator` first calls `Init`; shape-less
  entities can return before an animator is created.
- [EntityPlayer](https://github.com/anegostudios/vsapi/blob/63d33f70018c606fb0f70ead900fe90625fbd7b2/Common/Entity/EntityPlayer.cs)
  constructs both player animation managers before animator loading. Its
  `AnimManager` setter intentionally does nothing; tests must initialize its
  real backing manager rather than assume assigning that property worked.

Thus a non-null player animation manager is not proof that animation state can
be read yet. The reports do not prove which particular creation/unload event
triggered the gap in the user's world, nor rule out every other mod interaction.

## Narrow fix

The exact-version production Harmony loader installs three transpilers only on
`MountStrokes.Pace`, `DrawnShare` and `Finish`. Each changes exactly one
`IAnimationManager.GetAnimationState(string)` call into a guarded static call:
return null while that manager/Animator is absent; otherwise call the original
**virtual manager lookup**. This preserves FP/-ifp routing instead of bypassing
it via `Animator.GetAnimationState`.

Each manager is checked independently, so a missing FP animator cannot gate a
ready TP sibling (or vice versa). The original null-animation behavior, clone,
speed calculation and Finish bookkeeping are retained. There is no catch, no
finalizer, no `EntityPlayer` or global animation patch, no permanent disabling,
and no rowing/current/power algorithm replacement. A changed target IL shape
fails the transpiler's exact-one-call assertion rather than silently patching
an unexpected method. Unsupported Rough Seas versions retain the existing
inert lifecycle.

1.8.0's animal-lightning protection is retained. This work is stacked on
`lightning-spares-animals` (PR #2) because main still identifies itself as 1.7.0;
installing an unrelated main-only fix would otherwise remove an installed
1.8.0 feature. No PR is merged by this work.

## Executed verification

Dependencies: .NET SDK 10.0.404, official server 1.22.7 game assemblies, exact
Rough Seas source tag, official Rough Seas 5.0.1 ZIP and Rivers 5.0.3 DLL.
The game identifies the API as 1.22.7.0. API SHA256:
`034283e7e9d98eae45ee63005576fd89badc3c995b531cc4c3fe46f3eb2d3296`.

With `DOTNET_ROOT`, `ROUGH_SEAS_REPO`, `VINTAGE_STORY_DIR`, `RIVERS_DLL` set:

```
python tests/setup.py
python tests/run.py
python tests/run-currents.py
python package.py
# ROUGH_SEAS_ZIP points to the downloaded official 5.0.1 archive:
python tests/native-smoke.py
```

- `run.py`: **195 assertions**, zero unexpected failures; five deliberate
  process-local negative controls fail at their intended assertions. The new
  rowing control removes the production transpilers, then the same lifecycle
  test crashes. Before installation, two real `PlayerAnimationManager` states
  reproduce native NREs through actual upstream Pace. After production
  `StartPre`, absent manager/Animator paths, DrawnShare, Finish bookkeeping,
  ready native player managers, cloned pace metadata, unload/reload recovery
  and unrelated exception propagation pass. Existing log/hull/power/lightning
  tests also pass. Exact Harmony target allowlist excludes global animation
  methods. All 524 upstream source/JSON files are byte-checked against the tag.
- `run-currents.py`: **65 assertions**, **2000 seeded geometry cases**, zero
  unexpected failures; four intended failing current-hook removal controls.
- Native dedicated server: the **installable source ZIP**, not the harness,
  is compiled by the game's Roslyn loader (six source files) alongside the
  official 5.0.1 binary. It logs successful rowing installation, reaches
  RunGame, accepts `/stop`, exits 0 and releases the loopback port. Curated
  output is in `tests/native-smoke-results.txt`; generated worlds/configs are
  deliberately not committed. The script retains the full raw log only in
  its fresh scratch dataPath.
- ZIP creation validates CRC and per-entry bytes. It preserves all current
  source files and config values. No game/Rivers/Harmony DLL is committed or
  redistributed in either archive.

## Limits and manual acceptance

The headless tests mock interfaces and animator state; they are not a connected
client or a rendering loop. Attempting to proxy the native client-world
interface hit a .NET DispatchProxy visibility error, so no passing local-client
simulation is claimed. Both managers' selection and FP/-ifp routing are left
as original IL; actual local-player TP/FP/immersive-FP visuals, mounted rowing,
side changes, dismount/reseat, entity unload/reload and multiplayer still need
native client acceptance. The native dedicated-server run validates actual
source compilation and official-binary Harmony compatibility, not the original
boat-render crash reproducer. No existing user world/server was modified.
