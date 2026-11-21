# Kuboczoch: Quiet Rough Seas 1.8.0

Compatibility update of **Hubert's Quiet Rough Seas**, retaining Hubert attribution.
For **exactly Rough Seas 5.0.1**, tested against Vintage Story API 1.22.7.0 and Harmony 2.4.2.0. The mod metadata dependency is a minimum; the runtime guard is exact and rejects legacy, future and prerelease versions without patches or power edits.

## Install / existing world — not performed by this delivery

1. Fully stop client/server. Back up the world, installed mod ZIPs, ModConfig and the existing values of the world settings below.
2. Remove the old Quiet Rough Seas ZIP. Install `quietroughseas_1.8.0.zip` in Mods on both client and server with Rough Seas **5.0.1**. Do not install duplicate copies. This is a source mod: it contains C# source, not a compiled DLL.
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
- **All legacy Rough Seas swimming suppression hooks were removed.** No attachment/lifecycle/breath/wave hook is skipped. `DisableRealisticSwimming` is no longer a config field; replace the old JSON with the supplied config. A stale JSON property is ignored by ordinary deserialization and cannot disable any hook.
- Native `realisticSwimming=false` restores Rough Seas' ordinary swimming path only. It does **not** remove Rivers 5.0.3's separate swimmer current, Jaunt stamina, vanilla drowning, rendered waves or unrelated mods' effects. `RiversPushPlayers=false` (recommended/default) narrowly skips Rivers' player-current prefix body, explicitly returning true to its caller so vanilla liquid physics still runs. `true` retains original Rivers swimmer push. Nonplayers, boats, flow rendering and saved flowvectors are unchanged. Keep Rivers `riverSpeed=3.0` and `disableFlow=false` unchanged unless you intentionally choose otherwise. This release does not promise neutral/no-current river swimming.

## Tests and provenance

First configure external dependencies as described in [tests/README.md](tests/README.md). Then, from the project directory:

```
python tests/setup.py
python tests/run.py
python tests/run-currents.py
python package.py
```

- Full tagged 5.0.1 upstream source (524 C#/JSON files) is byte-checked against git and compiled, not replaced with extracted stubs.
- Exact original 1.6 source first failed the new 5.0.1 acceptance check: `tests/red-1.6-results.txt`. It refuses 5.0.1 as unsupported.
- `tests/results.txt`: **178** passing runtime assertions, plus four intended failing log/hull/power/lightning negative controls. The 1.8.0 lightning additions (8 assertions, one negative control) were written without a .NET SDK at hand and have not been run yet.
- `tests/currents-results.txt`: **65** passing runtime assertions, **2000** seeded geometry cases, plus four intended failing current-hook negative controls. Includes actual Rivers 5.0.3 DLL, actual production reflection/Harmony installation, preserved initial/vertical motion, reverse/idle cases and a same-boat composite path: actual RS Current → actual HullSet.Relax/Rowed → actual Rivers postfix, repeated after explicit propulsion resets. Each native contribution appears once.
- Tests show no custom swimming/breath/wave patches and preserve vanilla DoApply/Rivers boat registrations. Actual Rivers player-prefix false/true behavior, dt/shift scaling, nonplayers and real vanilla DoApply continuation are tested, with a patch-removal negative control.

Headless interface doubles and deterministic RS sea sampling are used. Composite coverage exercises real selected physics bodies, **not the complete engine tick or Harmony ordering in a running game**. No game launch, multiplayer transport, rendered-water check, saved-world smoke test, physical damage amount or movement-feel test was performed. Those remain required in-game acceptance checks. Wind, ropes and wave immersion are not disabled; no universal penalty-free movement is promised.

Tests require .NET SDK 10, actual game assemblies, a Rivers DLL and an upstream checkout supplied through the environment variables documented in `tests/README.md`; game/Rivers binaries are not redistributed. Generated source/test archives include upstream source fixtures and their provenance/license, not an installable Rough Seas replacement.

## Rollback

Stop client/server. Remove 1.8.0 and restore backed-up matching mod ZIPs and ModConfig files on both ends. Restore the saved world-setting values using `/worldconfig <name> <previous-value>`, then fully restart. If rolling Rough Seas back to 4.x, use its matching older Quiet Rough Seas and saved configuration, not this exact-5.0.1 build. Restore the world backup if the upstream mod upgrade itself requires rollback; this addon makes no worldgen changes.
