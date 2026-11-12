# Quiet Rough Seas

A small Vintage Story patch mod for
[Rough Seas](https://github.com/hoblin/rough-seas). It does two things:

- **Silences the debug logs.** Rough Seas writes a lot of debug lines
  (`[storms]`, `[seas]`, `[shipyard]`, `[vessels]`, ...) to the console and
  logs. This mod skips them. Rough Seas' notifications, warnings and errors
  still show.
- **Stops accidental boat damage.** In Rough Seas every left click on a boat
  damages its hull, even with an empty hand. With this mod a hit only counts
  when you hold an axe, so you can still break a boat up on purpose but not
  with a stray click or a weapon swing. This part runs on the server.

## Install

Put the mod zip in your `Mods` folder next to Rough Seas.

- Works on client and server. Each install only quiets the process it runs in,
  so install it on both to quiet both. The boat damage change needs it on the
  server (or in single player).
- Not required on either side: players without it can still join.

## Build

No build step. This is a source mod, the game compiles `src/QuietRoughSeas.cs`
at startup. To package it, zip `modinfo.json` and the `src` folder:

```
quietroughseas_1.1.0.zip
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

Tested with Rough Seas 4.4.0 on Vintage Story 1.22.
