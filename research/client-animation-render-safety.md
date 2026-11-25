# Client animation/render lifecycle audit — Quiet Rough Seas 1.8.3

## Evidence and scope

Exact upstream: Rough Seas **v5.0.1**, commit `953797c053133d56b7b1ca6f7599e4f982547a6e` (`https://github.com/hoblin/rough-seas/tree/953797c053133d56b7b1ca6f7599e4f982547a6e/RoughSeas`). `tests/setup.py` extracts every tagged C#/JSON file verbatim; `tests/run.py` verifies the complete manifest and bytes before compiling the fixture. No upstream source is changed. SDK/runtime is actual Vintage Story **1.22.7**, Harmony **2.4.2**. The official Rough Seas 5.0.1 ZIP is also exercised by the dedicated-server source-loader smoke.

Reported scenario: another player approaches in a canoe and first enters an observer's render distance. There is no connected user client/save here. This is a targeted safety audit and reproducer family, **not proof that a particular null was the user's observed failure**, and not a guarantee against every future crash. The supplied non-finite-position packet reports are distinct: the stacked 1.8.2 motion protections remain intact, not reclassified as animation evidence.

The observer executes `EntityVessel.OnRenderFrame` → native boat render behavior + `OwnOars.Follow`. This is not merely the local rider's `MountStrokes.Pace`. OwnOars previously dereferenced `vessel.AnimManager` before checking its Animator; a newly constructed/unloaded vessel manager can be absent. Player managers can also have an Animator yet lack the native initialization context that FP code requires.

## Native readiness and unload

Pinned native source: API snapshot `63d33f70018c606fb0f70ead900fe90625fbd7b2` identifies 1.22.7 in `Config/GameVersion.cs`:

- `Common/Model/Animation/AnimationManager.cs`: GetAnimationState dereferences Animator; Init installs api/entity; string-start and stop access initialized entity/world/metadata. LoadAnimator can return before creating an animator when no shape is ready.
- `Common/Entity/PlayerAnimationManager.cs`: GetAnimationState retains native FP/`-ifp` routing and dereferences player/client state; stop also coordinates the other player manager. Checking only Animator is insufficient.
- `Common/Entity/EntityPlayer.cs`: managers exist before loaded animation data; its AnimManager setter is not a usable test initializer. Tests bind its actual backing manager or use the native TP/FP managers and initialize them.
- `Common/Entity/Entity.cs`: ShouldDespawn derives from !Alive; World is needed by GetBehavior's SidedProperties selection. These are lifecycle predicates, not a replacement for the engine's load/despawn sequence.

Native manager initialization is checked by reflecting the exact API fields `api`, `entity`, `AnimationsByMetaCode`. The last reflection avoids the native source compiler's missing System.Collections reference: a normal dotnet build alone did not detect that loader constraint. A failed initial smoke exposed it; the corrected final smoke has no server errors.

## Inspected callsite map

Paths below are relative to upstream `RoughSeas/`. Every direct StartAnimation/StopAnimation/GetAnimationState callsite in the tagged source was enumerated, including methods with gameplay neighbors. Rewrites remain local to those callsites; there are **no global animation-manager or entity-renderer patches**.

| Inspected path / methods | Protection or deliberate boundary |
| --- | --- |
| `Propulsion/MountStrokes.cs`: DrawnShare, Pace, Show, Finish | Existing three state-read transpilers retained and strengthened for native player initialization. Show/Finish stop calls use ready-manager wrappers; original playing/showing/finishing bookkeeping executes. Pace/DrawnShare additionally have narrowly filtered Harmony finalizers for direct mod-owned NRE/index failures (missing running metadata/shape). Finish/Show are **not** wrapped wholesale: neither gameplay/pose configuration nor state transitions may be swallowed. |
| `Vessels/OwnOars.cs`: Of, Follow, Match, Stop | Follow prefix requires a live vessel, client properties, World, manager and Animator. Follow/Match state reads and Match/Stop start-stop calls use safe wrappers. Match/Stop retain their state/logging behavior. Follow has a direct-mod-owned NRE/index finalizer and retries on the next frame; no permanent disable. Of remains original, called after base initialization; errors constructing configuration are not concealed. |
| `Vessels/SeatPoses.cs`: Set, Show, Stand, Hold | Only Show's animation calls and local-player selection are rewritten. Missing local player/world is treated as no local yaw update; manager readiness can omit only cosmetic animation calls. Offsets/rotation/eye height, built/hold/stance precedence and animation config still update. Malformed seat configuration/foreign input failures are not swallowed. |
| `Shipyard/CollectibleBehaviorLightWithFirestarter.cs`: Begin, End, Catches | Begin/End start/stop calls guarded without skipping sound teardown/registration or interaction flow. Catches/randomness/durability untouched, no finalizer on any of these methods. |
| `Shipyard/EntityBehaviorBurnout.cs`: Chip, Chips, particle/sound Draw path, OnEntityDespawn | Chip's one state lookup uses the same native-readiness guard; its existing unavailable-animation branch removes stale swing history. No catch around hewing, particles, randomness or sound disposal. Those are not isolated animation-manager failures and retain their exceptions. |
| `Storms/BlockBehaviorBailer.cs`, `Shipyard/CollectibleBehaviorFraming.cs`, `CollectibleBehaviorBlows.cs`: GetHeldTpUseAnimation | Return animation names for vanilla dispatch, with no manager access. No additional patch needed. |
| `Vessels/EntityVessel.cs`: Initialize, AfterInitialized, OnRenderFrame | Native base behavior, interpolation reset and server cargo restow remain untouched. The mod-owned oars tail is protected at Follow, rather than catching this method and suppressing base engine exceptions. |
| `Cargo/CargoRenderer.cs`: Show, OnRenderFrame, Clear, Dispose | Live owner/properties/render API prerequisites; disposed tombstone rejects subsequent uploads/render callbacks; Dispose runs once. Original IsRendered/hull-renderer/piles checks remain. No catch around shader setup, upload/draw or mesh release, where swallowing could leave shader state/resources inconsistent. Clear remains the original release path. |
| `Sailing/RigRenderer.cs`: OnRenderFrame, Shadowed, Rebuild, Part, Line, Cloth, Shaped, Spar, Dispose; `Sailing/RigRopes.cs` | Same live/disposed boundary; ready render requires local player/camera context and initialized weave/ropes. Shaped is deliberately allowed before properties/player/render readiness: preserving shape invalidation lets the first ready frame rebuild. It is barred after disposal/despawn. No broad catch around rope simulation, Rebuild, shader/cull changes, texture upload, mesh disposal or foreign rig drawers: partly executed GPU operations are not safely recoverable by ignoring an exception. |
| `Storms/BilgeRenderer.cs`: Show, OnRenderFrame, Dispose | Same live/disposed boundary; tesselator/selection-box prerequisites added where used. Original shape/hull/mesh checks remain; no shader/mesh exception swallowing. |
| `Shipyard/CollectibleBehaviorIcon.cs`, `CollectibleBehaviorCarriedBuild.cs`: OnBeforeRender, OnUnloaded, mesh generation | Render prerequisites checked; OnUnloaded tombstones the behavior before original cleanup. Late callbacks cannot rebuild meshes. Native cleanup and foreign asset/tesselator errors remain visible. New collectible behavior instances are live; an already-unloaded instance deliberately does not revive itself. |
| Cargo/bilge/sail behavior renderer creation, shape notifications, OnEntityDespawn; `Floating/VesselMotion.cs`: lifecycle/frame Attach/Detach and lift paths | Owners' original event registration/unregistration and gameplay lifecycles remain; renderer tombstones protect late callbacks. VesselMotion mutates physical positions and coordinates physics, not isolated animation. Existing 1.8.2 finite-lift guard retained; no broad catch or owner-wide skip added. |
| `Storms/SurfaceClock.cs`: OnRenderFrame, Aim, Dispose | Shared surface simulation/shore shader upload, not per-vessel animation. Aim already tolerates a missing local player. No catch: hiding partial surface-model/shader updates is outside the bounded canoe animation fix. |
| Other sea/water/shader hooks and mod-system Dispose | No broad interception. These include shared engine state, network and gameplay rather than a recoverable per-entity cosmetic boundary. |

## Exception policy, diagnostics and recovery

`CosmeticFailure` is installed only on **Pace, DrawnShare and Follow**. Harmony emits the actual try/catch/finalizer around each method. It returns null only for NullReferenceException or IndexOutOfRangeException whose first stack frame is that exact original method or a Harmony replacement which Harmony itself maps to that method. Method-name resemblance or a namespace substring is **not** sufficient ownership evidence. Other exception types and errors originating in native/foreign animation callbacks propagate unchanged. Ready-manager wrappers are NoInlining so an inlined native/foreign callback cannot acquire the cosmetic method's ownership. Tests inject NRE, index, argument and invalid-operation exceptions and compare identity.

Per-boundary warnings use a monotonic Stopwatch, one warning per 30 seconds with a suppressed-repeat count. Keys are fixed boundary names, not unbounded entity IDs/exception messages. The first warning includes boundary, failure type/site/message (or missing prerequisite). No logger patch hides warnings. Suppression does not disable callbacks or gameplay; ready frames retry. Tests execute 1,000 failures, verify one warning/999 repeats, then expire the stored deadline deterministically and verify a second summary warning and same-instance recovery.

Disposed renderers are terminal, tracked weakly by instance. Normal fresh renderers/behavior instances start live. A transient missing prerequisite does not create a permanent tombstone. A skipped one-shot upload/update still needs the upstream next update callback; this addon does not queue or replay arbitrary mesh enumerables and cannot promise automatic restoration if upstream never sends another update. Cleanup exceptions are not swallowed; one-shot disposal prevents subsequent callback resurrection but cannot promise GPU resource recovery after a foreign cleanup failure.

## Independent review and feedback disposition

Independent review verdict: **approved with notes**. The review found insufficient evidence, not a production defect: TP/FP running states shared metadata and animator substitutes returned a state for any lookup code, so one path could falsely satisfy both speed assertions. Accepted and addressed without production changes: TP, FP and immersive FP now have distinct cloned metadata; substitutes record exact codes and return states only for `row`, `row-fp` or `row-ifp`. Native PlayerAnimationManager suffix routing executes with a stateful `immersiveFpMode` settings double. Both modes verify independent pace updates, untouched inactive/source metadata, and missing-FP/unload/recovery behavior. Three fresh-process fixture controls prove that missing FP state fails the independent-speed assertion and incorrect native suffix selection fails the exact routing assertion separately for normal and immersive FP. These supplement the eight production patch-removal controls; they are not patch-removal tests or connected-client evidence.

### Remaining pre-existing risks explicitly excluded

- `Propulsion/DrawnPole.cs:43-56`: nonfinite model/attachment transforms can propagate through rotation, `ModelMat`/`AnimModelMatrix`, vector length and translated held-item transforms. The length <= 0 check does not reject NaN. This bounded animation lifecycle change adds no finite-transform validation there and does not claim to resolve that rendering risk.
- `Soundscape/VesselSounds.cs:134-144`: `Silence` schedules a fade callback calling `faded.Stop()` while `Dispose` independently disposes the sound. A pending fade can therefore invoke Stop after dispose. No fade cancellation/disposal coordination or regression test for that pre-existing lifecycle risk is included.

These exclusions, shader/resource exceptions and the connected-client checks below remain outside this fix; this is not coverage of all canoe/render/audio crashes.

## Verification and remaining acceptance

Commands from repo root, with dependency environment variables from `tests/README.md`:

```
python tests/setup.py
python tests/run.py
python tests/run-animation.py
python tests/run-currents.py
python tests/run-motion.py
python package.py
python tests/native-smoke.py
```

Final runtime results: **415 passing assertions** (core 195, animation 85, currents 65, motion 70), **2,000 seeded projection cases**, and **24 intended negative-control failures** (5 + 11 + 4 + 4), with no unexpected failures. The review follow-up reran animation (85 assertions, 11 controls); other suite totals and native smoke refer to the preceding verified runs against unchanged production sources. The native smoke compiled **8 source files**, installed the new cosmetic lifecycle patches against the official binary, reached RunGame, stopped with exit 0 and released its loopback port.

Results are in `tests/results.txt`, `tests/animation-results.txt`, `tests/currents-results.txt`, `tests/motion-results.txt`, `tests/native-smoke-animation-results.txt`. Core/current/motion are unchanged in intent and execute against production sources, not replacement pseudocode. The animation runner includes pre-install RED reproductions and eight independent patch-removal controls (DrawnShare finalizer, firestarter call rewrite, seat pose rewrite, observer readiness, observer finalizer, pace finalizer, renderer disposal and item unload). Expected negative processes must fail the named behavioral assertion; a build error or unrelated exception does not count.

Headless execution uses actual Harmony, native manager/player/entity assemblies and exact upstream methods. Substitute client/world/animation services and a sentinel native ClientPlayer entity/camera getter supply context without creating a client loop. Renderer tests exercise callback gating and no-GPU-work assertions; they do **not** draw a canoe, create a GL context or validate appearance. Dedicated-server smoke proves native Roslyn compilation, official-binary patch installation, RunGame and clean stop/port release only.

Still required on actual Windows 1.22.7 clients with Rough Seas 5.0.1: remote ridden canoe repeatedly enter/leave observer render distance; rider versus observer TP, FP and immersive FP; changing paddling/sculling side and speed; dismount/reseat; chunk unload/reload and despawn; cargo, sail/rope and bilge appearance; reconnect/world reload; inspect diagnostic warnings and ensure other mods' failures remain reportable. None of these connected-client acceptance checks is claimed complete.
