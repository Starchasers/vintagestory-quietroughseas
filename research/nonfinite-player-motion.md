# Non-finite player motion: evidence and bounded compatibility fix

## Reported symptom, not established attribution

The supplied incidents use Vintage Story 1.22.7, Rough Seas 5.0.1 and Quiet Rough Seas 1.8.1. The second log, timestamped **24/11/2026 21:46:57**, again has `ArgumentException: Position is not a number`, `EntityBehaviorPlayerPhysics.OnRenderFrame` line 363 and involved Harmony ID `roughseas.storms.motion`. Its generated method is `SendPlayerPositionPacket_Patch1`; the earlier report has `_Patch4`. Those dynamic suffixes are not mod versions or distinct diagnoses. The second incident corroborates recurrence on the unchanged addon version.

Neither log contains the corrupting write, actual position/motion components, time step, mount/swimming state or a precise reproduction. The full modpack was not reproduced. A Harmony owner in the stack is **not proof that owner produced the invalid value**.

## Actual inspected code

Upstream fixture: **v5.0.1**, commit `953797c053133d56b7b1ca6f7599e4f982547a6e`, complete tagged C#/JSON files copied verbatim and verified by `tests/run.py` against Git. Fixture sources are not modified.

### Native packet validation

Decompiling the actual 1.22.7 `VintagestoryLib.dll`, `Vintagestory.Client.NoObf.NetworkAPI.SendPlayerPositionPacket` shows:

```csharp
if (double.IsNaN(game.EntityPlayer.Pos.X))
    throw new ArgumentException("Position is not a number");
if (double.IsNaN(game.EntityPlayer.Pos.Motion.X))
    throw new ArgumentException("Motion is not a number");
```

The first message therefore identifies **NaN position X** at that boundary. It does not identify a writer. Native validation checks only X and does not reject all infinities/other axes. The harness invokes the real original native method to reproduce both exceptions before installation.

### Vessel motion cannot alone explain this X error

`Floating/VesselMotion.cs` patches the network method with `SendUnlifted`/`RestoreLift`: subtract boat cosmetic `Lift` from **player Pos.Y**, then add it back. It does not write player Pos.X. A NaN lift can poison Y (and NaN != 0 passes the upstream condition), but that is **not a sufficient explanation** for the reported native X exception.

The fix postprocesses `VesselMotion.Lift` only: NaN and either infinity become zero. All finite lift values pass unchanged. This guards cosmetic/render and packet users without suppressing exceptions, replacing waves, or changing the finite lift/unlift/restore pair. Tests inject the lift value at the actual patched Lift boundary, and execute actual upstream SendUnlifted/RestoreLift; they do not reproduce a naturally corrupt sea pose.

### One reproducible upstream horizontal-corruption source

`Person/BodyAfloat.cs`, Afloat line 109:

```csharp
var rate = sea!.Clock.SecondsIn(dt, entity.World.Calendar.SpeedOfTime) / dt;
```

At `dt == 0`, actual `SeaClock.SecondsIn(0,60)` is zero and the division is NaN. Actual `FloatingBody.Step` passes that rate into water-motion/current calculations: even a still sea/current can become NaN through multiplication by the rate. The harness runs the real SeaClock and real FloatingBody equations with ordinary upstream person configuration, demonstrating NaN horizontal result; its positive finite-time control remains finite.

`BodyInWater.Meet` also contains `share * changeX / seconds` and `...changeZ / seconds` (lines 46–54). Zero time can therefore enter further undefined divisions downstream; simply skipping network output is not a complete prevention strategy. Normal hull integration already has positive-time checks in relevant entry points. The bounded source fix is at Afloat, before it creates or advances persistent swimming state: bypass only dt <= 0 or nonfinite dt and return `true`, Afloat's normal vanilla-liquid-physics continuation value. No ordinary positive finite step or swimming feature is replaced.

**This is a proven source candidate, not a proven cause of these incidents.** The native 1.22.7 player render loop normally invokes `OnPhysicsTick(1f / 60f)` and then sends the player packet. The supplied stack does not show zero dt. It may involve a different earlier physics/current/mount/mod writer. No global attribution is asserted.

## Packet containment independent of the unknown writer

Four new Harmony targets, owned by `quietroughseas`:

1. `BodyAfloat.Afloat`: reject invalid time steps as above.
2. `VesselMotion.Lift`: reject nonfinite cosmetic lift.
3. Native `EntityBehaviorPlayerPhysics.OnPhysicsTick`: prefix retains finite XYZ/MotionXYZ before integration.
4. Native `NetworkAPI.SendPlayerPositionPacket`: prefix validates all six components. Finite sends also refresh the snapshot. Corrupt values roll back to the last finite **local** snapshot for the same entity object, entity ID and dimension. Observed entity/dimension transitions invalidate older samples, even if subsequently returning. With no sample, skip the packet until correction/reconnect; never invent an origin or teleport to potentially stale ServerPos.

Snapshots use weak entity keys, validate the copied retained values, and synchronize snapshot updates/reads. The packet prefix has priority 800 and explicitly runs before `roughseas.storms.motion`, so recovery happens **before** temporary unlift, not halfway through the lift pair. Valid calls retain the original serializer path; foreign exceptions are not caught or suppressed. One warning per affected player object reports recovery versus packet rejection, explicitly calling it containment rather than identification of the corrupting mod.

This recovery may produce a short snap back to a recent valid location. It does not repair arbitrary persistent internal state in another mod, a corrupt mount, or save data. It is not a guarantee against another mod deliberately writing NaN later in its own network prefix/serializer. Without a usable sample, client physics may remain invalid until server correction/reconnect; suppressing the bad packet avoids the reported exception but is not a full state reconstruction.

The new installation runs after existing currents/lightning compatibility, so failure to find a motion target cannot prevent those previously-working features from being installed. Supported upstream version remains exactly 5.0.1. No configuration defaults/world settings changed.

## Reproduction commands and actual outcomes

Working directory: `/home/brunette/projects/vs-motion-fix`, branch `fix/nonfinite-player-motion`. No commit or publication was made.

The old scratch SDK paths in the reference worktree no longer existed. A fresh official 1.22.7 dedicated-server archive was downloaded to a new private scratch tree; no shared SDK/reference fixture was edited. Archive URL:

`https://cdn.vintagestory.at/gamefiles/stable/vs_server_linux-x64_1.22.7.tar.gz`

Archive MD5: `3031212903b1c283084d5fe0cda6bce6`. The official Rough Seas ZIP was downloaded from `https://mods.vintagestory.at/download/139256/roughseas_5.0.1.zip` (metadata verified 5.0.1). Rivers 5.0.3 was obtained via the ModDB API release entry and extracted only to private scratch. None of these game/mod binaries is redistributed in the addon.

```sh
export DOTNET_ROOT=/home/brunette/.hermes/cache/scratch/motion-dotnet
export VINTAGE_STORY_DIR=/home/brunette/.hermes/cache/scratch/motion-server1227
export ROUGH_SEAS_REPO=/home/brunette/.hermes/cache/scratch/motion-rough-seas
export RIVERS_DLL=/home/brunette/.hermes/cache/scratch/Rivers.dll
export ROUGH_SEAS_ZIP=/home/brunette/.hermes/cache/scratch/motion-roughseas_5.0.1.zip
python tests/setup.py
python tests/run.py
python tests/run-currents.py
python tests/run-motion.py
python package.py
python tests/native-smoke.py
```

- Core: **195** runtime assertions passed; **5** process-local negative controls failed at their required assertion.
- Currents/player-flow: **65** assertions plus **2,000** seeded projection cases; **4** negative controls failed as intended.
- Motion: **70** assertions; **4** negative controls independently remove the dt guard, lift filter, packet guard or pre-physics snapshot and fail at their intended behavioral assertion.
- Combined: **330** runtime assertions, **13** intended negative controls, no unexpected failures.
- Motion coverage includes real equations/native NaN validation, all XYZ/MotionXYZ NaN/positive-infinity/negative-infinity cases with and without recovery, preservation of actual finite unlift/restore, safe skipped packets with the Rough Seas packet patches installed, snapshot retention through an invalid tick, dimension/identity invalidation and unchanged foreign exceptions.
- The finite Afloat test executes its inactive early-return path; it does not simulate full swimming. The native physics body and finite serializer body are skipped by clearly-labelled last-priority test sentinels. No client loop or network I/O is claimed.
- Native smoke: a fresh 1.22.7 dedicated server Roslyn-compiled **7** production source files, installed the production Harmony patches against the **official Rough Seas 5.0.1 binary**, reached `Dedicated Server now running`, accepted `/stop`, exited **0**, and released its loopback port. See `tests/native-smoke-motion-results.txt` for the exact dataPath and archive/API hashes.
- Packaging checks ZIP CRC, byte-for-byte contents, 1.8.2 metadata and exact unchanged config. `dist/manifest.json` contains final artifact hashes and sizes.

Remaining acceptance: connected client on Windows 1.22.7 with the actual modpack/world; observe swimming/wading, rowing/mounting, finite wave lift and restored player/server state after any warning. The user's original corrupting write is still unproven. No game deployment was performed.
