# QbanRambo's Railroader mods

Source code of three DLL mods for [Railroader](https://store.steampowered.com/app/1683150/Railroader/) 0.6, built on the game's own modding system with [Harmony](https://github.com/pardeike/Harmony). No Unity Mod Manager, BepInEx or other loader is needed.

| Mod | What it does | Download |
| --- | --- | --- |
| [QbanRambo's Goto](mods/goto/README.md) | Go to in a locomotive's menu: send the AE to waybill destinations and town locations, deliver cars, couple, refuel, turntables, run arounds | [Nexus Mods](https://www.nexusmods.com/railroader/mods/1887) |
| [Map Plus](mods/map-plus/README.md) | More on the game's map (cars, tracks, routes, signals, places, terrain, search) and a live web map for a second screen | [Nexus Mods](https://www.nexusmods.com/railroader/mods/1894) |
| [Train Orders](mods/train-orders/README.md) | Chained AE orders: trips, couplings, cuts, waits, stations, refuels, turntables, saved jobs | [Nexus Mods](https://www.nexusmods.com/railroader/mods/1901) |

To play, download them from Nexus Mods. This repository is for reading the code and building it yourself. It holds the code of the versions released on Nexus.

## Layout

- `src/<mod>`: the C# project of each mod.
- `mods/<mod>`: the mod's folder as it's installed (`Info.json`, README, license and notices). Map Plus's web map is in `mods/map-plus/web`. Building a mod copies its DLL and `0Harmony.dll` here.
- `tools`: PowerShell scripts to deploy a mod to the game and to make the release zip.

## Building

You need the [.NET SDK](https://dotnet.microsoft.com/download) (6 or later) and Railroader 0.6 installed. The projects target .NET Framework 4.7.2 and reference the game's own assemblies in `Railroader_Data/Managed`, which aren't part of this repository. Tell the build where the game is with the `RAILROADER_DIR` environment variable or the `RailroaderDir` property:

```
dotnet build src/goto -c Release -p:RailroaderDir="C:\Program Files (x86)\Steam\steamapps\common\Railroader"
```

That leaves `QbanRambo.Goto.dll` and `0Harmony.dll` in `mods/goto`. To try it, copy the `mods/goto` folder into `Documents\Railroader\ModDev` (a mod there overrides the same mod in `Mods`), turn on **Preferences > Features > Experimental > Allow Unsafe DLL Mods**, restart Railroader and enable the mod in **Mods**.

With PowerShell, `tools/deploy.ps1 goto` builds and copies a mod to `ModDev` in one step, and `tools/package.ps1 goto` builds it and writes the release zip to `dist`.

## Credits

- [Harmony](https://github.com/pardeike/Harmony) by Andreas Pardeike, MIT, shipped with each mod.
- Map Plus started as a take on Map Enhancer by Vanguard and Refizar08 (MIT); see `mods/map-plus/THIRD-PARTY-NOTICES.md`.
- The Preferences > Mods tab follows ashenmare's shared tab convention, so all mods that use it share one tab.
- Railroader is made by Giraffe Lab. This repository contains none of the game's files or code.

## How it's made

I've been a professional programmer for over 30 years. I design every feature (most of them come from your requests on Nexus), and Claude (AI) writes the code under my direction. I review every line it writes, to learn from it and to make sure it's correct, clean and free of duplicated or spaghetti code, and I test every change in game before it's released.

## License

MIT, see [LICENSE](LICENSE).
