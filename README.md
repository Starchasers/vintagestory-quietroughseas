# Kuboczoch: Quiet Rough Seas 1.8.3

Compatibility update of **Hubert's Quiet Rough Seas**, retaining Hubert attribution.
For **exactly Rough Seas 5.0.1**, tested against Vintage Story API 1.22.7.0 and Harmony 2.4.2.0. The mod metadata dependency is a minimum; the runtime guard is exact and rejects legacy, future and prerelease versions without patches or power edits.

## Install / existing world — not performed by this delivery

1. Fully stop client/server. Back up the world, installed mod ZIPs, ModConfig and the existing values of the world settings below.
2. Remove the old Quiet Rough Seas ZIP. Install `quietroughseas_1.8.3.zip` in Mods on both client and server with Rough Seas **5.0.1**. Do not install duplicate copies. This is a source mod: it contains C# source, not a compiled DLL.
3. Copy the supplied `quietroughseas.json` to **ModConfig/quietroughseas.json**, not Mods. Keep matching client/server versions and config. Restart fully.
4. As world administrator, run in the existing world:

```
/worldconfig realisticSwimming false
/worldconfig swimmerBreakers false
/worldconfig oceanCurrents true
```

Then stop/reload the world (full server/client restart recommended). All three settings are editable after world creation in 5.0.1. No regeneration or worldgen edits are required. These are native world settings, **not fields in a Rough Seas ModConfig JSON**. The same command reference is supplied in `config/roughseas-5.0.1-worldconfig.txt`.

Inspect startup for supported version, log suppression, axe-only hull gating and current filtering. Investigate any QuietRoughSeas warning. Publishing this source does not deploy it to a game server; an in-game smoke test is still required.

## Settings and behavior

```json
{
  "RiversPushBoats": false,
  "RiversPushPlayers": false,
  "LightningDamagesAnimals": false,
  "StrokePowerMultiplier": { "paddling": 8.0, "sculling": 8.0 }
}
```

- `RiversPushBoats=false` retains only the positive longitudinal component of **both Rough Seas and Rivers boat currents** along the hull/control heading. Opposed and lateral components are removed, forward assistance is retained. Reverse control reverses the axis. Motion already influenced by a current is never used to choose heading. `true` leaves both current sources unfiltered. This retains 1.6 semantics; Hubert's original 1.5 used false to block Rivers boat push entirely.
- Both upstream current systems still contribute their own filtered force; this mod does not inject another current or deduplicate distinct native physics systems. Rough Seas' current hook moved from `Storms` to `Floating` in 5.x. Its HullSet/drift mechanics and wave/wind forces remain upstream.
- Paddling/sculling power is multiplied by **8**, not speed. Other strokes and hunger/fatigue settings remain unchanged. Scaling is idempotent across single-player client/server startup.
- `LightningDamagesAnimals=false` (default) spares every living creature except players from lightning damage. Rough Seas strikes its storm lightning through the game's own lightning flash, so the guard sits on the game's entity damage entry and covers Rough Seas storms and the game's own thunderstorms alike. Players are still hurt, a strike still breaks a hull, and lightning-set fires (the game's `lightningFires` world setting) are unchanged. `true` leaves lightning as the game has it. Related native switches, not set by this mod: `/worldconfig stormLightning false` stops Rough Seas storm strikes entirely, and `/worldconfig lightningDamage false` stops lightning damage to players too.
- Rough Seas internal debug logs remain suppressed. Player hull Swing is allowed only with an axe; environmental/non-player damage remains upstream. Tests prove gating/resumption, not a particular damage amount.
- **All legacy Rough Seas swimming suppression hooks were removed.** No attachment/lifecycle/breath/wave hook is skipped. The 1.8.3 safety guard bypasses only a nonpositive/nonfinite swimming time step; ordinary positive finite steps remain upstream. `DisableRealisticSwimming` is no longer a config field; replace the old JSON with the supplied config. A stale JSON property is ignored by ordinary deserialization and cannot disable any hook.
- Native `realisticSwimming=false` restores Rough Seas' ordinary swimming path only. It does **not** remove Rivers 5.0.3's separate swimmer current, Jaunt stamina, vanilla drowning, rendered waves or unrelated mods' effects. `RiversPushPlayers=false` (recommended/default) narrowly skips Rivers' player-current prefix body, explicitly returning true to its caller so vanilla liquid physics still runs. `true` retains original Rivers swimmer push. Nonplayers, boats, flow rendering and saved flowvectors are unchanged. Keep Rivers `riverSpeed=3.0` and `disableFlow=false` unchanged unless you intentionally choose otherwise. This release does not promise neutral/no-current river swimming.

## Mount packet containment (development 1.8.3 replacement)

The separate native `SendPlayerMountPositionPacket` boundary now rejects nonfinite mount position/motion packets before serialization. Valid mounts, including non-Rough-Seas mounts, remain unchanged. This is crash containment, **not repair or identification of the NaN producer**: no mount state is reconstructed, and server correction/reconnect may still be needed. See [native evidence, lifecycle tests and limits](research/nonfinite-mount-motion.md). No live deployment is included.

## Tests and provenance

First configure external dependencies as described in [tests/README.md](tests/README.md). Then, from the project directory:

```
python tests/setup.py
python tests/run.py
python tests/run-currents.py
python tests/run-motion.py
python package.py
```

- Full tagged 5.0.1 upstream source (524 C#/JSON files) is byte-checked against git and compiled, not replaced with extracted stubs.
- Exact original 1.6 source first failed the new 5.0.1 acceptance check: `tests/red-1.6-results.txt`. It refuses 5.0.1 as unsupported.
- `tests/results.txt`: **195** passing runtime assertions, plus five intended failing log/hull/power/lightning/rowing negative controls. Includes the existing 1.8.0 lightning tests, two pre-patch native rowing NRE reproductions and post-patch lifecycle checks.
- `tests/currents-results.txt`: **65** passing runtime assertions, **2000** seeded geometry cases, plus four intended failing current-hook negative controls. Includes actual Rivers 5.0.3 DLL, actual production reflection/Harmony installation, preserved initial/vertical motion, reverse/idle cases and a same-boat composite path: actual RS Current → actual HullSet.Relax/Rowed → actual Rivers postfix, repeated after explicit propulsion resets. Each native contribution appears once.
- The only Rough Seas swimming patch is the 1.8.3 invalid-time-step guard on `BodyAfloat.Afloat`; breath/wave hooks and vanilla DoApply/Rivers boat registrations remain unchanged. Actual Rivers player-prefix false/true behavior, dt/shift scaling, nonplayers and real vanilla DoApply continuation are tested, with a patch-removal negative control.

Headless interface doubles and deterministic RS sea sampling are used. Composite coverage exercises real selected physics bodies, **not the complete engine tick or Harmony ordering in a running game**. A fresh native dedicated-server startup/source-compilation smoke was performed, but no connected-client launch, multiplayer transport, rendered-water check, saved-world smoke test, physical damage amount or movement-feel test was performed. Those remain required in-game acceptance checks. Wind, ropes and wave immersion are not disabled; no universal penalty-free movement is promised.

Tests require .NET SDK 10, actual game assemblies, a Rivers DLL and an upstream checkout supplied through the environment variables documented in `tests/README.md`; game/Rivers binaries are not redistributed. Generated source/test archives include upstream source fixtures and their provenance/license, not an installable Rough Seas replacement.

## Non-finite player motion safety (1.8.3)

- Reject zero, negative or nonfinite Rough Seas swimming `dt` before its `SecondsIn(dt, speed) / dt` calculation can poison horizontal motion. Return the ordinary vanilla-physics continuation result; valid swimming remains upstream.
- Treat a nonfinite cosmetic vessel lift as zero. Finite wave lift/unlift/restore behavior is unchanged.
- Before player integration, retain a finite local position/motion snapshot for that player object and entity ID. Before packet sending, recover a corrupted XYZ/MotionXYZ from that sample only in the same dimension. Entity/dimension transitions invalidate old samples. This can snap the player back to a recent valid location. With no usable snapshot, skip the invalid packet rather than inventing coordinates; server correction or reconnect may still be needed.
- Run the packet guard before Rough Seas' temporary lift subtraction and warn once per affected player object. Do not suppress exceptions or disable valid packet sending, swimming, boats or waves.

The reported `Position is not a number` means native **Pos.X was NaN**. Rough Seas' packet patch changes **Y**, so its Harmony ID does not establish who corrupted X. The reproducible zero-time swimming defect is one proven source candidate, **not a proven cause of the reported incidents**; the stock client normally uses positive fixed physics steps. See [evidence, limits and commands](research/nonfinite-player-motion.md).

`tests/motion-results.txt`: **70** passing headless runtime assertions and four intended patch-removal failures (time step, lift, packet guard, pre-physics snapshot). Real native NaN validation and upstream swimming equations are exercised; finite serializer and complete physics bodies are not. A fresh 1.22.7 dedicated server compiled all seven source files, installed the patches against the official Rough Seas 5.0.1 binary, reached RunGame and stopped cleanly. Client rendering and the user's full modpack still need an in-game check.

## Client animation and cosmetic lifecycle safety (1.8.3)

Extends 1.8.1's three rowing reads to all Rough Seas 5.0.1 animation-manager callsites, including observer-side own-oar synchronization, seat poses, firestarter animation and hewing particles. Guards native manager initialization, missing animators, player/FP context and unload; ready calls still use native virtual routing. Seat/stroke bookkeeping is not skipped. TP and FP readiness remain independent.

Cargo, rig and bilge renderer callbacks reject despawning/disposed owners; disposal becomes one-shot, and late item-render callbacks cannot recreate unloaded icon/carried-build meshes. Shape invalidation stays live before rendering is ready. These are prerequisite guards, **not blanket render exception suppression**.

Harmony-generated try/catch finalizers contain only mod-owned NRE/index failures thrown directly inside `MountStrokes.Pace`, `DrawnShare` or `OwnOars.Follow`. Native/foreign manager, shader/GPU, gameplay and network exceptions still propagate. Diagnostic warnings are limited to one per boundary per 30 seconds, with suppressed-repeat counts. Transient animation failures retry on the next callback; disposed instances never revive. See the [complete source audit, tests and remaining client acceptance checks](research/client-animation-render-safety.md).

Native dedicated-server verification checks source compilation and official-binary Harmony installation, **not** the reported observer-client crash. Remote spawn, FP and unload transitions are exercised headlessly against actual native managers/upstream methods with substitute client services. A connected Windows 1.22.7 observer/client reproduction remains unverified.


## Rollback

Stop client/server. Remove 1.8.3 and restore backed-up matching mod ZIPs and ModConfig files on both ends. Restore the saved world-setting values using `/worldconfig <name> <previous-value>`, then fully restart. If rolling Rough Seas back to 4.x, use its matching older Quiet Rough Seas and saved configuration, not this exact-5.0.1 build. Restore the world backup if the upstream mod upgrade itself requires rollback; this addon makes no worldgen changes.
