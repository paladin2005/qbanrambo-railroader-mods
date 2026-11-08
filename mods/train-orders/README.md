# Train Orders

Chained orders for Railroader's auto engineer (AE). Queue up waypoints, couplings, pick-ups, cuts, waits, passenger stations, refuelling and turntable moves, press Start, and the engineer works through them one after another, or over and over, handling the air hoses and handbrakes at every cut and coupling. Every order that changes the train shows the train before and after it, in the window and on the track, so you can plan a whole switching job before it starts.

## Requirements

- Railroader 0.6.0 or later (the MoW Update; on the Steam **experimental** branch when this was released).
- **Allow Unsafe DLL Mods** turned on (see Install).
- Nothing else: no Unity Mod Manager, BepInEx or other loader. Railroader loads the mod itself.
- Optional: **QbanRambo's Goto** for picking destinations, fuel loaders and turntables by name, coupling to waiting cars and a smart entrance into dead ends (it also adds a **Go to** button to the window), and **Map Plus** for setting waypoints with a right-click on the map and seeing a train's orders on the map. Both work with Train Orders.

## Install

1. Copy the mod's folder (the one with `Info.json`) into `Documents\Railroader\Mods` (create the folder if it doesn't exist).
2. In Railroader, turn on **Preferences > Features > Experimental > Allow Unsafe DLL Mods** and restart the game.
3. Open **Mods** on the main menu, enable **Train Orders** and click **Apply**.

This is a DLL mod: it runs compiled code with full access to your computer, which is why Railroader keeps DLL mods off by default. Only install it from a source you trust.

The orders are saved with your game, in each locomotive. In multiplayer every player needs the mod, and the host runs the orders.

## Use

Right-click a locomotive and pick **Train Orders**, or click the list button on its row in the **Engine Roster**, to open its orders window. Every locomotive has its own window, so you can keep several open at once, and each window remembers where you put it. If you'd rather have one window that shows the locomotive you select, turn on **One window for the selected train** in **Preferences > Mods > Train Orders**. Drag its edge to make it taller or shorter; the order list fills the space and scrolls when it's long.

### Adding orders

The **Add** buttons are icons; hover one for its name. From left to right:

- **Go to** (a pin, only with QbanRambo's Goto installed): the towns you've unlocked and their locations, the same as Goto's **Towns**. Pick one to add a trip there. Passenger stations become station orders, like the **Stations** button.
- **Pick up** (a car with an arrow): see Picking up cars below.
- **Leave cars > Last N cars**: the engineer stops, sets the handbrake on the car at the cut, closes the air on both sides, disconnects the hoses and uncouples the last N cars. When the engine has cars on both sides, or will have once the orders already in the list have run, **Leave cars** splits into **Front** and **Rear** (front is the engine's cab end), so you can drop the cars ahead of the engine; the window shows how many are in front and behind right now. **All cars** leaves every car on that end, counted when the cut happens. **Keep > Keep N cars** works the other way round: it keeps the N cars next to the engine and leaves all the rest, however long the train is. A cut never leaves a locomotive or tender: it stops at the first one, so with several engines in the train they all stay together.
- **Wait > Until loaded**: the train waits where it is until every freight car is full and the industry has finished with it (given it its next waybill), then goes on. **Until unloaded** waits until they're all empty, for example at the mill you delivered to. **Until repaired** waits on a repair track until every car reaches its repair limit; a heavily worn car only gets as far as the game allows until its next overhaul. These orders show how many cars are full, empty or repaired.
- **Wait > Wait time**: **Until tomorrow** waits until the next day starts at midnight, when industries take their next share of cars, so a log run doesn't fill the mill beyond what it takes in a day. **Until 06:00** (or whatever hour your company serves the interchange, in Company > Settings) waits until the morning, when the game's Sleep wakes you. **Until a time** waits for the hour and minutes you set on the order's **Time** line; a time already past today waits until tomorrow, so a list on Repeat runs to a schedule. **Minutes** waits 5 to 45 minutes and **Hours** 1 to 12 hours. The order counts down, and the game's Wait and Sleep buttons count too.
- **Leave cars > Run around the cars in front / behind**: see Run around below.
- **Go back** (a circular arrow): pick an order of the list, then **Until a time** or **Times** (once to 20). When the list reaches it, it goes back to that order until the time you set on its **Time** line, or that many times, then goes on with the next one. For example: go to the mill, wait until loaded, go to the interchange, wait until unloaded, go back to order 1 until 18:00, refuel, go to the shed, wait until 6:00, with Repeat on. A loop reached more than 12 hours before its time is skipped, so it doesn't run all night. The link to the order survives moving orders, Copy and Paste and saved jobs.
- **Set speed** (a gauge): 5 to the railroad's speed limit in steps of 5 mph, or **Max**. The trips after it, including the moves to refuel and on turntables, run at that speed until another Set speed order. For example: Set speed 10 mph before the stretch a work train crawls over, Set speed Max after it.
- **Stations**: pick a passenger station to add "Go to" that station. See Passengers below.
- **Refuel**: pick a water tower, coaling tower or diesel stand, or **Coal and water** in a yard that has both, to add a refuel order. See Refuel below.
- **Turntable**: pick a turntable to add a turntable order. See Turntable below.

Waypoints are added too: while the window is open, every waypoint you set for that locomotive is added to its orders instead of replacing the current one. That works with the game's waypoint picker (with the direction you chose), Map Plus's right-click and Goto. A couple-to-car waypoint becomes a coupling order. Moves are named after where they go: the industry track or station the waypoint is on ("Go to Ela House R1"), or else the nearest town ("Go to Ela waypoint").

### Running them

Click **Start**. The engineer runs the orders in turn:

- Before every move it releases any handbrakes set in the train.
- After a coupling it connects the air on the whole train and releases the handbrakes. It does the same, with **Air and handbrakes after coupling** on (Preferences > Mods > Train Orders, on by default), when you send the engine to couple with the game's waypoint tool while its window is closed.
- When it stops short of a waypoint at the end of a track, the order still counts as done.

Orders stay in the list when they're done: finished ones are dimmed, the current one is highlighted and the rest are white. When the last one is done, **Start** runs the list again from the top, and **Clear** empties it. Turn on **Repeat** to have it start over by itself, for runs like loading logs, taking them to the mill and waiting until tomorrow.

Each order has buttons to move it up or down and to remove it. **Pause**, **Skip** and **Clear** do what they say. The two buttons at the right copy and paste orders: **Copy** takes this locomotive's list, and **Paste** in another locomotive's window adds it to the end of that list (with Repeat, when that list was empty). Handy when an engine goes to the shop or is sold. The copy lasts until you load another game; to keep a list for good, save it as a job (see Saved jobs below). The **Engineer** line shows what the engineer is doing right now, so you can see when it's waiting at a switch or a signal.

The orders pause by themselves, with the reason shown, when the engineer can't find a route, stops on the wrong track, or you change its orders by hand, and when loading or unloading stops for two game hours (a car the industry doesn't load or unload, or it ran out of stock or room). Click **Start** to go on.

### Train preview

Under every order that couples, picks up or leaves cars, a thin line shows the train before and after that order: the locomotive on the left, and each car as a small picture of its type (locomotive, tender, boxcar, hopper, tank car, flatcar, gondola, coach, caboose) in the color of the town it's going to, the same colors as the game's waypoint picker. Cars that come in are underlined, cars left behind are faded. A long train is shortened in the middle with a "+N", always keeping the engines and the end that changes. Hover the line for the cars the order takes or leaves and the whole train after it. A red arrow means the order won't work as the list stands (for example, leaving 4 cars when only 2 will be there), with the reason in the tooltip.

Hover an order and the train and the cars it leaves appear on the track in cyan, right where they'll stand after that order, with arrows on the engines like the game's waypoint picker. With Map Plus they show on its maps too.

While the window is open, the game's waypoint picker draws the train you'll have after your last order instead of the one you have now, so you see its real length and whether it fits before you pick the spot.

It's a forecast from the orders as they stand: **Pick up** doesn't know which cars it will find until it runs, and after a station, refuel or turntable order, or when the engineer picks the end it couples on, where the train stands is a best guess. Once each order is done, the preview is worked out again from the real train.

### Saved jobs

The window's second tab, **Saved jobs**, keeps order lists for good. Type a name (it starts with the places of the list, such as "Whittier - Ela - Bryson") and click **Save** to save this engine's orders with their Repeat and Smart signals settings; saving under a name that's already there replaces that job. Each saved job shows how many orders it has and whether it repeats; hover it to read its orders.

- **Load** replaces this engine's orders with the job (pause the orders first).
- **Add** puts the job after the orders it already has.
- The trash button deletes the job.

Jobs are kept outside your saves, in the game's preferences, so every engine in every game can run them. Moves, stations, refuels, turntables, waits, Pick up, Leave cars and Couple back work anywhere. An order that couples to one particular car only works in the game where that car is: for a job you'll run again, such as a log run, use Pick up and Couple back instead.

### Run around

**Leave cars > Run around the cars in front** (or **behind**) leaves all the cars on that side, takes the engine around them and couples to their far end, so the engine ends up pulling the cars it was pushing:

- The engineer stops, cuts the cars the usual way (handbrake on the car at the cut, air closed, hoses off), runs around them on a siding or loop and couples to the far end of the last car, then connects the air and releases the handbrakes.
- It needs a siding or loop beside the cars, with a switch at each end and room for the engine past each switch. It checks the track before cutting: where there's no way around, the order pauses and nothing is cut. Use it where the track allows it, ideally next to a passing siding.
- If the nearest way around is far away, the engine goes the long way.

### Picking up cars

- A coupling order ("Couple to RDG 16379", from the game's picker, Goto's **Couple to existing cars** or Map Plus's right-click on a car) has a second line, **Then**, for what happens after coupling:
  - **Take all** (the default), or **Take 1** to **10**: the train keeps that many of the cars it coupled to, counted from the coupling, and leaves the rest behind the usual way, with a handbrake set.
  - **Take none**: it uncouples right away; the cars stay as they were.
  - **Leave all**, or **Leave 1** to **10**: it leaves that many of its own cars, the ones next to the coupling, with the cars that were there, and goes on with the rest. Leave all leaves every car on that side, up to the engine: the way to set out a cut on a track that already has cars.
- **Pick up > 1 to 10 cars, All cars** couples to the nearest standing cars without naming one. It looks on the train's own track first, the one the train is on and the one the previous order went to, out to the first switch; then within about 300 m. Cars with a locomotive are left alone. It takes that many and leaves the rest, like Take.
- **Pick up > Couple back** goes back for the cars this train left last, with Leave cars, Keep or a pick-up, even with other cars nearby. For example: Keep 6 cars, go switch the mill, then Couple back.
- **Pick up > Couple to the cars left by > Order N** goes back for exactly the cars that order (a Leave cars, Keep, Take or Pick up) left, even if the train has left others since. For example: Leave the last 3 cars, go to another track, Leave the last 2 cars, then Couple to the cars left by order 1. If that order hasn't left its cars yet when it's time, or they're gone, the orders pause and say so. Saved jobs and Copy and Paste keep the link.

After every coupling the engineer connects the air and releases the handbrakes.

### Turntable

**Add > Turntable** lists the turntables on your railroad, nearest first, and adds "Turn around" there. The order's second line changes what it does:

- **Turn around**: the engine goes onto the bridge, the bridge turns half a circle and the engine comes back off the way it went on, now facing the other way.
- **Park on Stall N**: the engine parks in that roundhouse stall, front first.
- **Leave on Lead …** (named after the direction the track leaves in): the engine leaves on that track, front first. Handy to bring an engine out of a stall.

Hover the order and the chosen track (or the bridge, for Turn around) lights up in the world, and on the maps with Map Plus.

The bridge is lined up with the track the engine comes from and turned for you, and the order shows what it's doing ("lining up", "getting on", "turning", "getting off"). It's for an engine on its own, with its tender: with cars coupled or an engine longer than the bridge, the orders pause and say why, so put a **Leave cars** before it. A turntable order carries on where it was after you load a save. A turntable move picked from Goto's **Turntable** menu while the window is open becomes a turntable order.

### Passengers

Add the stations of a route from **Add > Stations**, for example "Go to Sylva Depot" and "Go to Dillsboro Depot", turn on **Repeat** and click **Start**. You don't need to set any waypoints or tick any stations in the cars:

- The engineer heads for each station in turn, stops with the passenger cars at the platform and waits until everyone has got on and off, as the game's own passenger stops do.
- While the orders run, every station in the list stays ticked on all the passenger cars in the train. The game unticks a station when the train stops there, so the mod ticks it again once the train has left the platform, and passengers keep boarding for it lap after lap.
- Without **Repeat**, only the stations still ahead stay ticked: the ones the train has already visited are unticked, so nobody boards for a stop it won't come back to, and the coaches don't end the run full.
- The train also stops at any other station of the list it passes on the way.
- Put a **Wait time** after a station to stay longer: passengers keep boarding while the train waits.

At a station with more than one track, the order has a second line, **Track**: **Shortest route** (the default) takes whichever station track the engineer reaches first, which may not be the one by the station building; or pick a track by name. When the mod can tell them apart it calls them **Main track** and **Siding**, with the side they're on, such as "Main track (north)"; otherwise by their side, such as "East track". With a track picked, the train runs through on that track; if it can't reach it, it takes the shortest route and says so.

This needs **Company > Settings > Auto Engineer > Passenger Stops** turned on. The orders pause and say so if it's off.

### Refuel

A refuel order, such as "Refuel coal and water at Whittier", has a second line, **Only below**: 25%, 50% (the default), 75% or **Always**. When the order's turn comes:

- If any of its fuels is below that level, the engineer brings the train to the loader with the tender's filler right under the spout, lowers it, opens the water hatch and fills every fuel of the order to the top, then puts the spout away and closes the hatch. The order shows the level while it fills.
- If they're all above it, the order is done straight away and the next one starts. On a loop, the train only refuels when it needs to.

A loader out of stock is skipped with a message (coal and diesel come from the town's engine service industry; water never runs out). If the train can't line up with the spout after three tries, the orders pause. Pausing, skipping or clearing the orders while it fills puts the spout away. A refuel picked from Goto's **Refuel** menu while the window is open becomes a refuel order at 50%.

### Signals

The engineer obeys the signals. A red automatic signal out on the line only makes it stop and go on slowly, but a red signal at a switch (an interlocking) makes it wait until the signal clears. With the signal system in CTC mode, someone has to clear those on the CTC panel. In ABS mode they clear by themselves, but they can stay red when the train itself is in the block ahead or a switch in it is thrown.

Turn on **Smart signals**, next to Repeat, and when the train has waited 10 seconds at a red signal at a switch in ABS mode, the engineer passes it at restricted speed, as with the game's **Pass Signal** button, but only when the way is clear: no other train in the blocks past the signal, and no other running train (moving, on the engineer or running Train Orders) on the tracks around the interlocking, station tracks and sidings included, or in the blocks beyond it. Parked cars and idle engines don't hold it up. When two trains with Smart signals wait at the same interlocking, one goes first and the other waits for it. A message says which train it's waiting for. It does nothing in CTC mode, where you're the dispatcher.

### Smart entrance

An order added with Goto's dead-end submenu has a **Smart entrance** box. Checked, the engineer pushes the cars into the dead end so the engine stays at the open end and never gets boxed in. It's worked out when the order runs, with the train as it is then, so it still works when the cars are coupled by an earlier order. If the train can't get into that position, the engineer goes in the normal way and a message says why.

### Settings

**Preferences > Mods** (from the main menu or with Esc) has a **Train Orders** section with **Air and handbrakes after coupling** and **One window for the selected train**, and **QbanRambo's mods** with the update check. The tab is shared with other mods that put their settings there.

### On the web map

With Map Plus 1.7.0 or later, the web map has the same orders window: select a train and click **Orders** next to Follow, the toolbar's orders button or press O. A panel on the right shows its orders, in sync with the game, with every button of the window: the Add menus, the order settings, Start, Skip, Clear, Copy, Paste and the saved jobs. Hover an order to see the train after it on the web map. While the panel is open, right-click a track to add a trip there, or a car to add a coupling, as the game does with the window open.

## Limits

- Run arounds need a siding or loop beside the cars, with a switch at each end. Where there's none, the order pauses before cutting anything. Turntable orders are for an engine on its own.
- It relies on the game's internal code, not the supported modding API, so a Railroader update can break it. If the orders stop running, check the Message Log and look for an update.
- Tested in single player.

## Update check

Once a session, when a game loads, the mod asks Nexus Mods whether the QbanRambo mods you have installed (Goto, Map Plus, Train Orders) have a newer version, and says so once in the game's console, with a line in the Message Log. Only the mods' Nexus numbers are sent. To turn it off, uncheck **Check Nexus for updates** in **Preferences > Mods > QbanRambo's mods** (or set `UpdateCheck=0` in `Documents\Railroader\QbanRamboGlobal.cfg`) and restart the game. The **Mods** menu links to the mod's Nexus page.

## Uninstall

Clear the orders of your locomotives, then disable **Train Orders** in **Mods**, or delete its folder from `Documents\Railroader\Mods`, and restart the game.

## Changelog

### 1.8.0

- **Go back**: a new order that goes back to an earlier one until a time of day or a number of times, then goes on, so a train can run a job over and over until evening and then refuel and go to the shed.
- **Set speed**: a new order; the trips after it run at that speed, from 5 mph to the railroad's limit, until another one, for work trains that crawl over a stretch.
- **Couplings, Then**: after coupling, **Take none** uncouples right away, and **Leave all** or **Leave 1** to **10** leave the train's own cars with the cars that were there, besides Take all and Take 1 to 10.
- **Air and handbrakes after any coupling**: coupling with the game's waypoint tool while the window is closed now connects the air and releases the handbrakes too. An option in Preferences > Mods, on by default.
- **Turntable orders**: hovering one lights its track in the world, and on the maps with Map Plus.
- **On the web map**: with Map Plus 1.7.0, the whole orders window is on the web map too, in sync with the game.
- **Settings in Preferences > Mods** have a cleaner layout: each checkbox first, with its full name beside it.

### 1.7.0

- **Wait until a time**: **Wait > Wait time > Until a time** waits for a time you set on the order, hour and minutes; a time already past today waits until tomorrow, so a list on Repeat runs to a schedule.
- **Couple to the cars left by an earlier order**: **Pick up > Couple to the cars left by > Order N** goes back for exactly the cars that order left, not only the last ones, so you can drop cuts on several tracks and collect each one later.
- **One window for the selected train**: an option in **Preferences > Mods > Train Orders**; the window switches to the orders of the locomotive you select, and Train Orders on another locomotive uses the same window.
- **Passenger stations**: without Repeat, the coaches keep only the stations still ahead ticked, so no one boards for a station the train won't visit again.
- **Run arounds from Goto**: with Goto 1.6.0, its **Run around** becomes a run around order while the window is open.
- **Settings in Preferences > Mods**: the one-window option and the update check, in a Mods tab shared with other mods that put their settings there.
- Fixed: Smart signals no longer fills the game's log with warnings while a locomotive you bought hasn't been delivered yet.

### 1.6.0

- **Saved jobs**: a second tab in the orders window saves the orders under a name, and **Load** or **Add** puts them on any engine, in any game, with their Repeat and Smart signals.
- **Run around**: **Leave cars > Run around the cars in front** or **behind** leaves them, takes the engine around them on a siding or loop and couples to their far end, air and handbrakes included. It checks for a siding or loop first and, where there is none, pauses without cutting anything.
- **Station track**: at stations with more than one track, a station order picks the track to stop on, the main track or the siding, or the shortest route as before.
- **Leave cars** offers **Front** and **Rear** from the train as it will be after the orders already in the list, so you can plan a cut after couplings on both sides.
- **Smart signals** also waits while another running train is around the interlocking, on station tracks and sidings too, not only in the signal blocks past the signal.
- A **Deliver** picked in Goto with the window open becomes two orders, the trip and Leave the last N cars.
- Station orders aim past the platform along the station's own track, so the route no longer runs into a neighboring track.
- **Update check**: once a session, when a game loads, it asks Nexus whether QbanRambo's mods you have installed have a newer version and says so once in the game's console; `UpdateCheck=0` in `Documents\Railroader\QbanRamboGlobal.cfg` turns it off. The **Mods** menu now links to the mod's page.

### 1.5.0

- **Train preview**: every order that couples, picks up or leaves cars shows the train before and after it, each car as a small picture of its type in its destination's color, new cars underlined and the cars left behind faded. Hover it for the full list.
- **On the track**: hover an order to see the train and the cars it leaves, in cyan, where they'll stand after it.
- **Waypoint picker**: with the window open, the picker draws the train you'll have after your last order, with its real length.
- The window is a little wider.

### 1.4.0

- **Pick up cars**: couples to the nearest standing cars, on the train's own track first, and takes 1 to 10 of them or all, leaving the rest behind with a handbrake set.
- **Take**: coupling orders say how many of the coupled cars to keep; the rest are left behind.
- **Leave cars > Keep**: keeps 1 to 10 cars next to the engine and leaves all the rest.
- **Couple back**: goes back for the cars this train left last.
- **Turntable orders**: turn the engine around, park it in a roundhouse stall or send it out on another track, front first. A turntable move picked from Goto with the window open becomes one too.
- **Wait until repaired**: on a repair track, waits until every car reaches its repair limit, with live progress.
- **Wait time**: 5 to 45 minutes, and until the morning, when the interchange is first served.
- **Go to** button: Goto's towns right in the window, when QbanRambo's Goto is installed; passenger stations become station orders.
- The window can be made taller or shorter, and the Add buttons are now icons with tooltips.
- The up, down and remove buttons keep their size next to long orders.

### 1.3.1

- The order list now scrolls when there are many orders, so long lists no longer overlap the rest of the window. The status and the buttons stay in view.

### 1.3.0

- Refuel orders: **Add > Refuel** picks a water tower, coaling tower or diesel stand, or **Coal and water** in yards that have both. The engineer lines the tender up with the spout, fills up, then puts the spout away and closes the water hatch.
- **Only below** 25%, 50%, 75% or Always: a refuel order only runs when one of its fuels is below that level (and then fills them all), so a loop refuels just when it needs to.
- A refuel from Goto with the window open becomes a refuel order.
- **Smart signals**: in ABS mode, a train waiting at a red signal at a switch passes it at restricted speed when no other train is in the blocks ahead.

### 1.2.0

- Passenger trains wait at every station until passengers have got on and off, at least the company's Minimum Stop Duration, also with plain waypoints at a station.
- Station orders line the coaches up with the platform, even at short ones like Ela.
- Wait until loaded and Wait until unloaded wait until the industry has finished each car and given it its next waybill. Cars used to leave with the last bit of load and their old waybill, so the next industry wouldn't load or unload them.
- Leave cars > All cars, for the front or the rear: every car on that end, never a locomotive or tender.
- Copy and Paste buttons to move orders from one locomotive to another.
- A passenger station picked from Goto with the window open becomes a station order.

### 1.1.0

- Passenger stations: pick them from Add > Stations; the engineer stops at each one, waits for boarding and keeps the stations ticked on the passenger cars, lap after lap.
- Repeat, to run the orders over and over.
- Wait time: until tomorrow, or 1 to 12 hours.
- Orders stay in the list once done: finished ones dimmed, the current one highlighted, and Start runs a finished list again. Up and down buttons reorder them.
- One orders window per locomotive, several at once.
- Moves are named after the industry track or station they go to.
- Fixed: a waypoint with a direction, added before a coupling, could make the train go around and arrive facing the wrong way.

### 1.0.0

- Orders window per locomotive, opened from its context menu or the Engine Roster.
- Waypoints and couplings queued from the game's picker, Goto or Map Plus.
- Cuts from the front or rear, with handbrake, air and hoses handled.
- Wait until loaded and Wait until unloaded.
- Smart entrance into dead ends, with Goto.

## Credits

By QbanRambo. MIT license (see `LICENSE`). Uses [Harmony](https://github.com/pardeike/Harmony) by Andreas Pardeike (MIT, see `THIRD-PARTY-NOTICES.md`). Inspired by [Waypoint Queue](https://github.com/PrinceOfPluto/WaypointQueue) by PrinceOfPluto.
