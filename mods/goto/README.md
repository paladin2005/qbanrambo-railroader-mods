# QbanRambo's Goto

Adds **Go to** to a locomotive's context menu in Railroader 0.6. Pick a destination and the AE switches to Waypoint mode and drives the train there: it can deliver the cars waybilled there and leave them, couple to the cars waiting on the track, line the tender up under a fuel loader, or turn the engine on a turntable.

## Requirements

- Railroader 0.6.0 or later (the MoW Update; on the Steam **experimental** branch when this was released).
- **Allow Unsafe DLL Mods** turned on (see Install).
- Nothing else: no Unity Mod Manager, BepInEx or other loader. Railroader loads the mod itself.

## Install

1. Copy the mod's folder (the one with `Info.json`) into `Documents\Railroader\Mods` (create the folder if it doesn't exist).
2. In Railroader, turn on **Preferences > Features > Experimental > Allow Unsafe DLL Mods** and restart the game.
3. Open **Mods** on the main menu, enable **QbanRambo's Goto** and click **Apply**.

This is a DLL mod: it runs compiled code with full access to your computer, which is why Railroader keeps DLL mods off by default. Only install it from a source you trust.

The mod is optional: other players don't need it to join your game, and you don't need it to join theirs.

## Use

Click a locomotive to open its context menu and hover **Go to**:

- **Waybills** lists where the cars of that train are waybilled, with how many cars are going to each and the town it's in. Each one opens a submenu with **Deliver** (see below) and the ways to go there.
- **Towns** lists the towns you've unlocked and connected with track. Hover one to see its industries, stations, coaling towers and diesel stands. Interchanges with several customers on the same tracks show once, under the interchange's name. Locked towns and locations on unbuilt track don't show up.

Click a destination. The AE gets a waypoint at the far end of the nearest track of that location it can reach, so the train pulls onto it, at the AE's current speed limit. If the AE can't find a route, the reason shows at the bottom of the screen and nothing changes.

The same menu also opens from the engineer's panel: in Waypoint mode, a **Go to** button sits next to the buttons that set and clear the waypoint.

### Deliver

Each destination under **Waybills** opens a small submenu:

- **Deliver N cars**: the train goes to that industry, puts the cars waybilled there on its track with the engine facing out, and leaves them: handbrake set on the car at the cut, air closed on both sides, hoses off, uncoupled. Then the engine pulls a few meters away, so you see them part. The cars have to be together at one end of the train (otherwise the item is greyed out and says so); locomotives and tenders never count. If the train is already standing there with those cars on the track, it leaves them right away.
- **Just go**, or the entrance and coupling items described below: the train goes there and stops with the whole train, as before.

### Couple to existing cars

When the location you pick already has cars standing on its tracks, it opens a small submenu:

- **Couple to existing cars** (with how many cars are there): the train ties on to them instead of stopping short. It couples at the nearest free coupler it can reach, with whichever end of the train gets there first. Once coupled, it connects the air hoses and, with **Release handbrakes** on, releases the handbrakes, and the message at the bottom says how many cars it took.
- **Smart coupling** (on a dead-end track, when your train has cars): your own cars couple to the ones waiting, so the engine stays at the open end.
- **Don't couple**: the train stops short of the cars, as it always did. On a dead-end track you get **Smart entrance** and **Normal entrance** instead (see below).

### Dead ends

When the destination is a dead-end track and the train has cars, the destination opens a small submenu instead (an engine on its own just goes):

- **Smart entrance**: the AE pushes the cars in first, so the engine ends up at the open end and can leave the cars there without getting boxed in. The engine keeps its facing. If the train can't get into that position (it doesn't fit, or there's no way to turn the cars around without a run-around), Go to sends a normal waypoint and says why.
- **Normal entrance**: a normal waypoint. The train goes in the way it's facing, which may already mean pushing the cars in; nothing is forced. Handy when you'll wait there, for example while cars load.

### Passenger stations

When the train has passenger cars and you pick a passenger station, Goto ticks that station on the cars and sends the train through it, so the game's own passenger stop lines the cars up with the platform, even at short ones like Ela. Once the train is standing at the platform, Goto clears the rest of the order: the train stays there while passengers get on and off. This needs **Company > Settings > Auto Engineer > Passenger Stops** turned on, as the game's passenger stops do. With it off, the train stops at the end of the platform instead, and the message says the cars may not line up. A station stop in progress carries on after you load a save.

### Refuel

**Go to > Refuel** shows the locomotive's fuel levels and lists the loaders it can use: water towers, water columns and coaling towers for a steam locomotive, diesel stands for a diesel. The nearest are at the top, with the town and distance. Yards with coal and water next to each other also get a **Coal and water** entry that does both in one go. A fuel that's already full, or a loader whose stock has run out, is greyed out.

Click one and the engineer brings the train in so the tender's filler (or the locomotive's, on a tank engine or a diesel) stops right under the spout, with cars coupled or not, engine first or tender first. Then the spout comes down, the water hatch opens and the tender fills up. When it's full, the spout goes back up and the hatch closes. If the train stops a little off, it backs up and tries again, up to three times. Moving the train while it fills stops the refuelling.

Coal and diesel come from the stock of the town's engine service industry, which you keep up by delivering coal and diesel there. Water never runs out.

A refuel in progress carries on after you load a save.

### Turntable

**Go to > Turntable** lists the turntables on your railroad, nearest first. Each one offers:

- **Turn around**: the engine goes onto the bridge, the bridge turns half a circle and the engine comes back off the way it went on, now facing the other way.
- **Stall 1, 2, …**: the engine goes on, the bridge turns to that stall and the engine parks in it, front first. Stalls with something already in them are greyed out.
- **Lead** tracks (named after the direction they leave in, such as **Lead NE**): the engine goes on, the bridge turns to that track and the engine leaves on it, front first. Handy to bring an engine out of a stall.

Go to lines the bridge up with the track the engine comes from, drives the engine onto the middle of it, turns it and drives it off. It's for an engine on its own, with its tender: with cars coupled or an engine longer than the bridge, it says so. Messages at the bottom tell you each step, and a move in progress carries on after you load a save.

The base map has two turntables, both unlocked later in the game: the roundhouse at Bryson and the engine service at Dillsboro (built as a project; in sandbox, turn on **Dillsboro Engine Service Lead** and then **Dillsboro Engine Service** in Company > Settings > Advanced > Map Features). Turntables added by map mods work too.

### Run around

**Go to > Run around** offers **The cars in front** and **The cars behind**, for each side of the engine that has cars. The engineer cuts those cars the usual way (handbrake on the car at the cut, air closed, hoses off), runs around them on a siding or loop and couples to the far end of the last car, then connects the air and releases the handbrakes, so the engine ends up pulling the cars it was pushing.

- The train has to be stopped.
- It needs a siding or loop beside the cars, with a switch at each end and room for the engine past each switch. It checks before cutting: where there's no way around, the option is greyed out with "No siding or loop beside them".
- If the nearest way around is far away, the engine goes the long way.

### Release handbrakes

A train with a handbrake set on any car won't move. With **Release handbrakes** checked at the bottom of the **Go to** menu, every car of the train with its handbrake set has it released as soon as the AE gets the order, and the message at the bottom says how many. Click it to turn it on or off, or use **Preferences > Mods > QbanRambo's Goto**; it's on by default and the choice is saved with your game preferences.

### Settings

**Preferences > Mods** (from the main menu or with Esc) has a **QbanRambo's Goto** section with **Release handbrakes**, and **QbanRambo's mods** with the update check. The tab is shared with other mods that put their settings there.

### With Train Orders

If **Train Orders** (also by QbanRambo) is installed and the locomotive's orders window is open, **Go to** adds the destination to its orders instead of sending the train right away. Picking **Smart entrance** or **Normal entrance** sets that order's **Smart entrance** box, a passenger station becomes a station order, the same as picking it from the **Stations** menu in Train Orders, a refuel becomes a Refuel order, a coupling becomes a couple order and a turntable move becomes a Turntable order (refuel with Train Orders 1.3.0 or later, the rest with 1.4.0 or later). Train Orders 1.4.0 also has its own **Go to** button with the same towns; with Train Orders 1.6.0 it offers **Smart entrance** when the train will have cars by the time that order runs. With Train Orders 1.6.0, a **Deliver** becomes two orders: the trip and Leave the last N cars. With Train Orders 1.7.0, a **Run around** becomes a run around order.

## Limits

- It works on any map, including modded ones, because it reads destinations and tracks from the game itself.
- It relies on the game's internal code, not the supported modding API, so a Railroader update can break it. If the menu stops showing **Go to**, check the Message Log and look for an update.
- Tested in single player. In multiplayer the orders go through the game's normal permission checks.

## Update check

Once a session, when a game loads, the mod asks Nexus Mods whether the QbanRambo mods you have installed (Goto, Map Plus, Train Orders) have a newer version, and says so once in the game's console, with a line in the Message Log. Only the mods' Nexus numbers are sent. To turn it off, uncheck **Check Nexus for updates** in **Preferences > Mods > QbanRambo's mods** (or set `UpdateCheck=0` in `Documents\Railroader\QbanRamboGlobal.cfg`) and restart the game. The **Mods** menu links to the mod's Nexus page.

## Uninstall

Disable **QbanRambo's Goto** in **Mods**, or delete its folder from `Documents\Railroader\Mods`, and restart the game. It doesn't change your saves.

## Changelog

### 1.6.0

- **Go to button on the engineer's panel**: in Waypoint mode, next to the buttons that set and clear the waypoint, a Go to button opens the same menu for that locomotive (thanks lionkinggod9000).
- **Run around**: Go to > Run around > The cars in front or behind leaves them, takes the engine around them on a siding or loop and couples to their far end, air and handbrakes included; it checks for a siding or loop first and doesn't cut anything where there's none.
- **Settings in Preferences > Mods**: Release handbrakes and the update check, in a Mods tab shared with other mods that put their settings there.
- With Train Orders 1.7.0's window open, a run around becomes a run around order.

### 1.5.0

- **Deliver**: each Waybills destination opens a submenu with **Deliver N cars**. The train puts the cars waybilled there on the industry's track with the engine facing out, leaves them with a handbrake set, the air closed and the hoses off, and pulls a few meters away so you see them part. The cars have to be together at one end of the train.
- With Train Orders 1.6.0's window open, a Deliver becomes two orders, the trip and Leave the last N cars.
- **Passenger stations**: the point past the platform follows the station's own track, so the route no longer runs into Whittier House; with Passenger Stops off, the train stops at the end of the platform and says so; a station stop in progress carries on after you load a save.
- Train Orders' **Go to** button offers **Smart entrance** when the train will have cars by the time that order runs, not only when it has them now (with Train Orders 1.6.0).
- **Update check**: once a session, when a game loads, it asks Nexus whether QbanRambo's mods you have installed have a newer version and says so once in the game's console; `UpdateCheck=0` in `Documents\Railroader\QbanRamboGlobal.cfg` turns it off. The **Mods** menu now links to the mod's page.

### 1.4.0

- **Couple to existing cars**: a location with cars standing on it opens a submenu to couple to them instead of stopping short. The air is connected and the handbrakes released once coupled.
- **Smart coupling**: on a dead end, with cars in your train, your cars couple first so the engine stays at the open end.
- **Turntables**: **Go to > Turntable** turns the engine around, parks it in a roundhouse stall or sends it out on another track, front first, lining up and turning the bridge for you.
- A refuel or a turntable move in progress carries on after you load a save.
- An engine without cars goes straight into a dead end, without the Smart entrance submenu.
- With Train Orders' window open, couplings and turntable moves become orders too, and Train Orders' Go to button shows Go to's towns.
- Fixed: when Train Orders queued an order, Go to still released the handbrakes and showed its own message.
- Fixed: as a client in multiplayer, a refuel could stop right away saying another mod took the order.

### 1.3.0

- Refuel: **Go to > Refuel** lists the water, coal and diesel loaders for the locomotive, nearest first, plus **Coal and water** in yards that have both.
- The engineer stops with the tender's filler right under the spout, lowers it, opens the water hatch and fills up, then puts the spout away and closes the hatch. It backs up and tries again if it stops a little off.
- Full fuels and loaders out of stock are greyed out; if coal runs out halfway, it still takes the water.
- With Train Orders' window open, a refuel becomes a Refuel order.

### 1.2.0

- Passenger stations: with passenger cars in the train, Go to lines them up with the platform and the train stays there while passengers get on and off.
- With Train Orders' window open, a passenger station becomes a station order.

### 1.1.0

- Dead-end destinations open a submenu: **Smart entrance** pushes the cars in so the engine stays at the open end; **Normal entrance** goes in the way the train is facing.
- Works with Train Orders: with its window open, Go to adds the destination to the locomotive's orders.

### 1.0.0

- **Go to** submenu on locomotives: **Waybills** and **Towns**.
- **Release handbrakes** option.

## Credits

By QbanRambo. MIT license (see `LICENSE`). Uses [Harmony](https://github.com/pardeike/Harmony) by Andreas Pardeike (MIT, see `THIRD-PARTY-NOTICES.md`) to extend the context menu.
