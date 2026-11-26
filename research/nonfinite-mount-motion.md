# Non-finite mount packets: VS 1.22.7

## Evidence, not attribution

The supplied Windows log reports `ArgumentException: Mount Motion is not a number` at `NetworkAPI.SendPlayerMountPositionPacket(Entity mount)`, reached from `EntityBehaviorPlayerPhysics.OnRenderFrame`. This is a different packet boundary from `SendPlayerPositionPacket`, already guarded on main. The stack does not identify the first writer of NaN.

Native 1.22.7 `VintagestoryLib.dll`, method RVA `0xc2b34`: `SendPlayerMountPositionPacket` first checks `double.IsNaN(mount.Pos.X)` and throws `Mount Position is not a number`; it then checks `double.IsNaN(mount.Pos.Motion.X)` and throws the exact reported `Mount Motion is not a number`. The latter IL loads Motion.X at IL_001d–0028, calls Double.IsNaN at IL_002d, branches at IL_0032, constructs ArgumentException at IL_0039 and throws at IL_003e. Packet construction follows at IL_003f. The predicate is not a speed cap, rotation check, player-position check, or every-component finiteness check.

Inspected the retained exact Rough Seas v5.0.1 source at commit `953797c053133d56b7b1ca6f7599e4f982547a6e`, native boat/passive physics, and Rivers 5.0.3's decompiled physics patch:

- Rough Seas `Floating/HullSet.cs:113–130` writes Motion.X/Z through retained set components and divisions by `rest` and `kept`. `Rowed` adds retained set at lines 145–151, `Take` replaces horizontal motion at 162–169. These are plausible propagation/writer sites, not proof of an invalid normal-game input.
- `DriftPhysics` wraps native passive motion/collision and `VesselDraught` changes drag. Native boat update also writes motion from its persistent forward speed. Nonfinite state can survive beyond one call.
- Rivers' liquid-physics prefix adds flow to entity motion. Presence in the mod list does not establish it was active on this mount or produced the first NaN.
- The prior zero-time BodyAfloat reproducer concerns player swimming and cannot honestly be repurposed as the cause of this mount failure.

The requested rowing SDK paths were absent in this environment. Equivalent retained `motion-server1227`, `motion-dotnet`, `motion-rough-seas` and `Rivers.dll` fixtures were discovered and used. Native API SHA256: `034283e7e9d98eae45ee63005576fd89badc3c995b531cc4c3fe46f3eb2d3296`.

## Narrow containment

Production StartPre adds one first-priority Harmony prefix to the exact mount send overload, under the existing exact Rough Seas 5.0.1 version gate. It rejects packets when any position or motion XYZ component is NaN or infinity. This deliberately extends native validation to Y/Z and infinity rather than allowing corrupt values through because the native exception happens to check only X.

Finite vanilla/non-Rough-Seas mounts and Rough Seas boats are untouched. No snapshot is recorded or restored; no speed is zeroed; no dismount, teleport, server-coordinate substitution or blanket exception catch is introduced. Weak, per-object state is diagnostic-only, with warning allowance reset by identity/dimension transition. It cannot resurrect a prior mount's coordinates, including same-ID replacement objects or dimension departure/return. Foreign exceptions still propagate.

This prevents the packet-validation crash; it **does not repair persistent vessel state** or identify its producer. Corrupted physics remains corrupted, and packets remain rejected until state becomes finite, e.g. by server correction/reconnect. A connected player could remain stuck. Arbitrary third-party postfixes/side-effect-free prefixes may still run after a Harmony skip and are not controlled here. A future producer fix needs first-invalid-write telemetry around the writer sites above, including dt, drag denominators, retained HullSet components and Rivers flow inputs.

## Executed verification

- `tests/MountProgram.cs`, invoked by `tests/run-motion.py`: before patching, execute the real native method and reproduce the exact two ArgumentException messages. Production StartPre then prevents the same mount Motion.X NaN crash, without a finite-send sentinel installed. A patch-removal subprocess reintroduces the exact exception.
- Full production motion harness: **122 assertions**, **5** independent patch-removal controls. Boats plus generic native EntityAgent mounts; NaN/+infinity/-infinity on all six components; finite exact-state preservation; same-object entity ID changes; dimension changes/return; same-ID replacement; finite correction; unrelated exception propagation. Finite sends stop at a last-priority sentinel, not a socket.
- Existing core suite: **195 assertions**, **5** negative controls; exact patch allowlist updated for one additional mount boundary.
- Existing animation suite: **85 assertions**, **11** negative controls.
- Existing currents suite: **65 assertions**, **2,000** seeded cases, **4** negative controls.
- Fresh native 1.22.7 dedicated server: packaged source ZIP compiled all eight production source files with native Roslyn; actual official Rough Seas 5.0.1 binary loaded; all Harmony patches installed; reached RunGame; `/stop`, exit 0, loopback port released. See `tests/native-smoke-mount-results.txt`.
- Independent read-only reviewer: no correctness blockers; independently reran 122-assertion harness and all five negative controls. Review correctly distinguished finite sentinel tests and headless native startup from a connected-client test.

No graphical Windows client, connected rider, complete boat physics loop, network transport, or user's naturally corrupt world was exercised. The red case injects NaN into the native mount; it proves the exact reported failure and containment boundary, not how the user's world first acquired NaN.

## Packaging

This scoped PR advances the addon to 1.8.4 so the mount guard is distinguishable from the installed 1.8.3. The ZIP is a PR test build, not a merged or published release. The installable ZIP contains the existing eight production C# files and metadata, not game/upstream binaries. No live pack/server was modified or deployed. No merge is authorized.
