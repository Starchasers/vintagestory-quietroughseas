# Quiet Rough Seas

A small Vintage Story patch mod for
[Rough Seas](https://github.com/hoblin/rough-seas). It does four things:

- **Silences the debug logs.** Rough Seas writes a lot of debug lines
  (`[storms]`, `[seas]`, `[shipyard]`, `[vessels]`, ...) to the console and
  logs. This mod skips them. Rough Seas' notifications, warnings and errors
  still show.
- **Stops accidental boat damage.** In Rough Seas every left click on a boat
  damages its hull, even with an empty hand. With this mod a hit only counts
  when you hold an axe, so you can still break a boat up on purpose but not
  with a stray click or a weapon swing. This part runs on the server.
- **Stops rivers pushing boats** (only if the
  [Rivers](https://mods.vintagestory.at/rivers) mod is installed). Rivers
  pushes rowed boats along the river's flow. With this mod it does not, so a
  boat moves on a river exactly as on still water: no slowdown upstream, no
  boost downstream, no sideways drift. Swimmers are still pushed.
- **Makes paddled boats faster.** Rough Seas boats paddle at about 2 m/s,
  slower than a player swims. The power a paddle stroke puts into the water is
  multiplied by 8 (configurable), which about doubles the speed. Each boat's
  own speed still comes from Rough Seas' hull physics, so sleek canoes are
  faster than rafts. Hunger from paddling is unchanged.

## Config

`ModConfig/quietroughseas.json` is written on first start:

```json
{
  "RiversPushBoats": false,
  "StrokePowerMultiplier": {
    "paddling": 8.0,
    "sculling": 8.0
  }
}
```

- `RiversPushBoats`: `false` stops rivers pushing boats; `true` leaves Rivers as
  it is. (Before 1.5.0 this was `RiverUpstreamDivisor`, which is now ignored.)
- `StrokePowerMultiplier` maps a Rough Seas stroke to a multiplier on its power.
  The strokes are `paddling` (raft, bark canoe, dugout, outrigger, reed boat),
  `sculling` (coracle), `rowing` (oars, as on the currach), `pole` and
  `seatedpole`. Top speed grows with roughly the cube root of the power: 8
  about doubles it, 3.4 gives about 1.5 times. Leave a stroke out, or set it to
  `1`, to keep it as Rough Seas has it.

Restart the game after changing the file. The values are not synced between
server and client, so set the same values on both.

## Install

Put the mod zip in your `Mods` folder next to Rough Seas.

- Works on client and server. Each install only quiets the process it runs in,
  so install it on both to quiet both. The boat damage change needs it on the
  server (or in single player). For the river change install it on both.
- Not required on either side: players without it can still join.

## Build

No build step. This is a source mod, the game compiles `src/QuietRoughSeas.cs`
at startup. To package it, zip `modinfo.json` and the `src` folder:

```
quietroughseas_1.5.0.zip
├── modinfo.json
└── src/QuietRoughSeas.cs
```

## How it works

All Rough Seas debug output goes through `Log` helper methods on its mod
systems. At startup this mod finds every such method and puts a Harmony prefix
on it that skips the call. The methods are found by name, not hard-coded; if a
Rough Seas update changes how it logs, this mod writes a warning saying it found
nothing to silence.

Hull hits go through a `Swing` method on Rough Seas' vessel behavior. A second
prefix skips it unless the held item is an axe.

Rivers pushes boats from its own Harmony postfix on the vanilla boat motion
step. A third prefix, on that postfix itself, skips it.

Rough Seas gives a rower a fixed body power and each stroke a gross efficiency,
the share of that power that reaches the blade; hull drag then settles the
speed. After Rough Seas has loaded its propulsion config, this mod multiplies
the efficiency of the configured strokes in place. Hunger is charged on the body
power, so it does not change.

Tested with Rough Seas 4.4.0 and Rivers 5.0.3 on Vintage Story 1.22.
