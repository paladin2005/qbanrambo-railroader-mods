# Map Plus

Paints the game's terrain under Railroader's map, with its rivers, lakes and mountains, and shows every car on it, not just locomotives, colored by the town it's waybilled to and flagged when something's wrong, and every industry, station and fuel stop with its cars. Click a train to select it and see its route, where its cars are going and its Train Orders. Shows every signal, the class of every track, and trains in trouble. Right-click the track to send the selected locomotive there or to place a fusee. Industry tracks and the mainline get their own colors. Hover any car, locomotive, place, signal or track for its details, find anything from the toolbar, jump the map to any town or train, follow a train, and zoom out further. And open a live map of the whole railroad in your browser, for a second monitor.

## Requirements

- Railroader 0.6.0 or later (the MoW Update; on the Steam **experimental** branch when this was released).
- **Allow Unsafe DLL Mods** turned on (see Install).
- Nothing else: no Unity Mod Manager, BepInEx or other loader. Railroader loads the mod itself.

## Install

1. Copy the mod's folder (the one with `Info.json`) into `Documents\Railroader\Mods` (create the folder if it doesn't exist).
2. In Railroader, turn on **Preferences > Features > Experimental > Allow Unsafe DLL Mods** and restart the game.
3. Open **Mods** on the main menu, enable **Map Plus** and click **Apply**.

This is a DLL mod: it runs compiled code with full access to your computer, which is why Railroader keeps DLL mods off by default. Only install it from a source you trust.

The mod is optional: other players don't need it to join your game, and you don't need it to join theirs.

## Use

### Terrain

Open the map and the land shows under the track: hills and valleys in relief, rivers and lakes with their shores and deeper water away from them, forests, fields, towns, and rock on the steepest slopes, all from the game's own terrain data. The forest is cleared along the track. The land beyond the railroad is filled in too, dimmer since it isn't part of the game's world, and fades out at the edges.

Rivers and creeks follow the game's own river paths, so even narrow ones show whole.

The first time you open the map, the whole railroad paints in a few seconds. As you zoom in, finer detail paints in around you, so it stays sharp up close. It paints in the background without slowing the game. The very first time, the mod reads the game's terrain for a few seconds and keeps it cached, shared with the web map. Turn it off from **Display > Terrain**.

### Cars on the map

Open the map. Every car shows as an outline as long as the car, with its reporting mark and number (tenders just the number) once you zoom in far enough that each label fits along its car:

- **Bright** with the color of the town it's waybilled to, the same colors as the town tags.
- **Darker** once it's spotted at its destination.
- **White** when it has no waybill, and for cars that don't carry freight, such as coaches and cabooses.

Click a car or locomotive to select its train and inspect the car you clicked; Shift-click only selects. Clicking a car selects the locomotive of its train (or the car itself, when no locomotive is coupled). The selected train is outlined in cyan.

Hover a car to see its reporting mark and number, type, load, destination and any hotbox, low oil or handbrake. Hover a locomotive to see its speed, cars, length, fuel, where its cars are headed and how many handbrakes are set.

A dot at one end of a car warns you without hovering:

- **Red**: hotbox.
- **Orange**: oil below 25%.
- **Purple**: handbrake set on a car coupled to a locomotive, which holds the train back.

### Places

Every industry, station, interchange and engine terminal you can reach shows as an icon by type: mines, logging, mills, freight customers, fuel dealers, track materials, interchanges, passenger stations and engine terminals. Water towers, coaling towers and diesel stands show up too once you zoom in. Next to each icon, a small tag counts the passengers waiting (blue), the cars spotted there and, in amber, the cars on the way. When icons would overlap, the busiest places are kept.

Hover an icon for its type and town, its contract tier, what it ships and receives, the cars on each of its tracks, the passengers waiting by destination, when the station was last served, and any storage warnings; a fuel stop shows its stock and the industry that supplies it. Click an icon to open the place in **Company > Locations** (a fuel stop opens its supplier).

**Display > Place Types** picks which types show; **Show all** brings them all back.

### The selected train

Select a locomotive, from the map, the world or the Trains list:

- **Destinations**: the tracks its cars are waybilled to light up, amber while they're on the way, green once they're spotted.
- **Train Orders**: if the train runs Train Orders, every order with a place (moves, couplings, stations, refuels, turntables) shows as a numbered amber marker, the current one larger. Hover one to read the orders there.
- **Route preview**: see below.

### Find

Type in the **Find** box at the left of the toolbar and press Enter: a car's mark and number, a locomotive, an industry, a station, a fuel stop or a town. With one match the map flies there and a gold pulse marks the spot; with several, pick one from the list. Picking a locomotive also makes it the one Follow follows.

### Right-click the track

Right-click any track on the map for a menu, the game's own kind:

- **Send <locomotive> here**: the selected locomotive's engineer gets a waypoint there, at its current speed limit, the same as picking the waypoint yourself, and a cyan pulse marks the spot. If there's no route, the pulse is red and the game's own message tells you why. Any handbrakes set in the train are released, and the message says how many. Greyed out until you select a locomotive.
- **Place fusee** or, near one, **Extinguish fusee**: the engineer of any train stops at a lit fusee, as with the game's own fusees. Greyed out when your role can't place them.

If you're already picking a waypoint the game's way, right-click works as before.

### Fusees

Every lit fusee shows on the map as a flame with a pulsing ring, so you see where trains will stop. Hover one to see it. Turn them off from **Display > Fusees**.

### Track colors

- **Blue**: the tracks of every industry, station and interchange you can reach, the same places listed under **Towns**.
- **Green**: the mainline, the track inside CTC blocks.

Hover any track for its details: what it is (an industry track's name, the mainline, a yard track), its town, how many cars are spotted there or on the way, its class and top speed, speed limit, condition, the class it's built for, its grade, and whether it's a bridge or a tunnel.

If you turn on the game's **Track Condition** option, worn track keeps its yellow or rust color over these.

### Track class

Turn on **Track Class** in the Display menu to color every built track by its class, the one on its speed signs that sets how fast trains may go: **1** pink-red (10 mph), **2** violet (25 mph), **3** blue (40 mph), **4** cyan (60 mph), **5** green (80 mph). The legend sits at the right of the map toolbar, and the colors follow wear and maintenance within a few seconds. It replaces the industry and mainline colors while it's on.

Track Class and the game's **Track Condition** take turns: turning one on turns the other off, so their colors never mix.

### Route preview

Select a locomotive in Waypoint mode and open the map: the route its engineer is about to take is drawn on the track, with cyan stripes flowing toward a flag with the locomotive's name at the waypoint. It's the engineer's own route, every switch and reversal included, and the part the train has already covered is left out. It disappears when the train arrives.

### Signals

Every signal shows as an arrow on its side of the track, pointing the way of the trains it governs, in the color of its aspect: **red** stop, **yellow** approach, **green** clear, **orange** restricting. Hover one for its interlocking and aspect. At a red signal at a switch (an interlocking) the engineer waits; at a red automatic signal out on the line it stops and then goes on slowly.

### Train alerts

A badge on a locomotive's icon tells you its train needs attention:

- **Red, pulsing**: a derailed car, a hotbox, or fuel or water at 5% or less.
- **Orange**: fuel or water below 20%, the level at which the engineer warns you, or the engineer stopped by a problem (handbrakes applied, the brake line).
- **Yellow**: waiting at a red signal at a switch for over a minute.

Hover the locomotive to read every alert, at the top of its details.

### Display options

Turn things on or off from the map's **Display** menu (the gear button): **Terrain** shows or hides the painted terrain, **Cars** the car icons, **Car Labels** their mark and number, **Car Alerts** the warning dots, **Industry Tracks** the blue industry tracks, **Mainline** the green mainline, **Track Class** the class colors (off by default), **Train Route** the selected train's route, **Signals** the signal arrows, **Train Alerts** the locomotive badges, **Places** the place icons (with **Place Types** right below to pick which types), **Train Destinations** the selected train's destination tracks, **Train Orders** its order markers, and **Fusees** the fusees.

### Toolbar

Next to the gear button at the bottom of the map (hover the icons for their names):

- **Find**: see above.
- **Towns**: the towns you've unlocked and connected with track. Pick one to center and zoom the map on it, or one of its industries, stations and engine facilities. Locked towns and unbuilt track stay hidden, so the list grows with your railroad.
- **Trains**: pick a locomotive to center the map on it. Each one shows its alerts, what its engineer is doing (manual, road, yard, or the waypoint it's heading to), how fast it's going and how many cars it has.
- **Follow**: keeps the map centered on the locomotive you picked under Trains, or the one you have selected, while it moves. Its dot fills in while it's following. Click it again, or drag the map, to stop.
- **Web Map**: opens the web map in your browser (see below).

A scale bar at the bottom left shows the distances in feet or miles.

To jump there in the world too, hover the map and use the game's teleport key (Ctrl+T by default).

### Zoom

The map zooms out about two and a half times as far as the game allows, enough to see the whole railroad. Track and icons keep their on-screen size past the game's limit.

### Web map (second monitor)

A live map of your railroad in your web browser, made to sit on a second monitor while you play.

1. Turn on **Preferences > Features > Experimental > HTTP Server**.
2. Open the map and click **Web Map** on its toolbar, or go to `http://localhost:8630/map-plus` in your browser. Map Plus is also listed on the game's own page at `http://localhost:8630/`.

The first time, the web map reads the game's terrain for a few seconds (the status at the bottom right says so); after that it's cached and opens at once.

- **Painted terrain**: hills, valleys, rivers, forests and fields from the game's own terrain data, painted in detail: rivers along the game's own river paths with deep water and muddy shores, forests of broadleaf and conifer trees with their shadows, rock on steep slopes, relief shading and clearings along the track. The land beyond the railroad is filled in too, so the map is whole from edge to edge. Close up it paints in over a few seconds.
- **Every train and car, moving live**, even during the game's fast forward. Cars take their destination colors and show their numbers when you zoom in; close up you see the rails and ties. Far out, each train is a marker you can't miss. Moving trains leave a fading trail and light the way ahead.
- **Tracks** in their colors, with tunnels, which way each switch is set, and wooden and steel bridges with their decks, trestles and railings up close. Hover a track for its name (industry tracks and stations), class, speed limit, condition, the class it's built for and the grade.
- **Places**: every industry, station, interchange and fuel stop gets an icon by type, with how many cars are spotted there and on the way, and how many passengers wait at stations. Hover one for its stock, contract and tracks.
- **Fusees** pulse on the track. Right-click a track for a menu to place a fusee there or put one out.
- **Click a train** for a panel with everything about it: speed, the engineer's mode and waypoint, alerts, every locomotive's fuel, tractive effort, boiler and controls, and every car's load and destination. If the train runs Train Orders, its orders are listed too. **Follow** keeps it in view. The selection follows the game: selecting a train in the game selects it here, and selecting it here selects it in the game.
- **Click a place** for its panel: what it ships and receives, its storage, contract, the cars on its tracks right now and the ones on the way, which light up across the map. Fuel stops show their stock and supplier.
- **Search** (`/`) for trains, towns, places and cars, and the map flies there.
- **Toolbar** on the right: zoom, the whole map, a list of every train (`T`), layers (`L`) to turn the terrain, track class, places by type, signals, car numbers and alerts on or off, and full screen. `Esc` closes things, `F` follows the selected train.
- **Windows on their own**: by default every window selects the train you select in the game, and the other way round. The link button (`G`) gives a window its own selection, so one can follow a train in the background or stay on the whole map while you play; each window remembers its own view.

The web map works on the same computer as the game. Besides showing it, it can only select trains and place or put out fusees.

### Web map only

If you use another map mod and only want the web map, uncheck **On the game's map** in **Preferences > Mods > Map Plus** (or set `EnableMapInGame=0` in `Documents\Railroader\map-plus.cfg`), then restart the game. Map Plus then leaves the game's map alone (no layers, toolbar buttons or right-click menu) and runs only the web map. Check it again (or set it back to `1`) to bring it back.

## Limits

- It relies on the game's internal code, not the supported modding API, so a Railroader update can break it. If cars stop showing on the map, check the Message Log and look for an update.
- Tested in single player.

## Update check

Once a session, when a game loads, the mod asks Nexus Mods whether the QbanRambo mods you have installed (Goto, Map Plus, Train Orders) have a newer version, and says so once in the game's console, with a line in the Message Log. Only the mods' Nexus numbers are sent. To turn it off, uncheck **Check Nexus for updates** in **Preferences > Mods > QbanRambo's mods** (or set `UpdateCheck=0` in `Documents\Railroader\QbanRamboGlobal.cfg`) and restart the game. The **Mods** menu links to the mod's Nexus page.

## Uninstall

Disable **Map Plus** in **Mods**, or delete its folder from `Documents\Railroader\Mods`, and restart the game. It doesn't change your saves.

## Changelog

### 1.6.0

- **Web map windows on their own**: the link button (`G`) on the toolbar gives a window its own selection, so it can follow a train in the background or stay on the whole map while you select other trains in the game; each window remembers its own view.
- **Settings in Preferences > Mods**: **On the game's map** (what `EnableMapInGame` in `map-plus.cfg` does) and the update check, in a Mods tab shared with other mods that put their settings there.
- The web map's train panel and the orders on the map describe Train Orders 1.7.0's new orders: Wait until a time and Couple to the cars left by an earlier order.
- Includes 1.5.1: no more warning in the game's log every second for a locomotive you bought that hasn't been delivered yet.

### 1.5.0

- **Fusees**: every fusee on the track shows on both maps with a pulsing flame, and you place one or put it out from a right-click on the track.
- Right-click on the game's map now opens a menu on the track under the mouse: **Send <locomotive> here** (what right-click used to do) and **Place** or **Extinguish fusee**.
- **Rivers** drawn from the game's own river paths on both maps, so narrow creeks no longer look broken or dry.
- **Web map**: the tree layer paints faster, with bigger, more natural trees, and panning is smoother.
- **Web map**: the train selected in the game is selected on the web map too, with its panel and route, and selecting one on the web map selects it in the game.
- Track condition reads like the game's own tooltip, as a share of what the track is built for.
- `map-plus.cfg`: `EnableMapInGame=0` leaves the game's map alone and runs only the web map, for players who use another map mod.
- The drawn route follows the train through switches that are still set the other way, instead of turning off down the wrong track.
- Fixed: hovering a signal arrow shows its details again.
- **Update check**: once a session, when a game loads, it asks Nexus whether QbanRambo's mods you have installed have a newer version and says so once in the game's console; `UpdateCheck=0` in `Documents\Railroader\QbanRamboGlobal.cfg` turns it off. The **Mods** menu now links to the mod's page.

### 1.4.0

- **Terrain on the game's map**: the land painted under the track from the game's own terrain data, with rivers and lakes, mountains in relief, forests, fields and towns. Finer detail paints in as you zoom, so it stays sharp up close. Turn it off from Display > Terrain.

### 1.3.0

- **Places on the game's map**: industries, stations, interchanges, engine terminals and fuel stops as icons, with cars spotted and on the way and passengers waiting. Hover for their details, click to open them in Company > Locations, and pick the types from Display > Place Types.
- **Find**: a search box on the map toolbar for cars, locomotives, industries, stations, fuel stops and towns.
- Click a train on the map to select it; the selected train is outlined in cyan.
- **Train destinations**: the selected train's destination tracks light up, amber on the way and green once spotted.
- **Train Orders on the map**: the selected train's orders as numbered markers.
- Hover any track for its details, now without turning on Track Class: what it is, its town, class, speed limit, condition, grade, bridge or tunnel.
- Toolbar buttons are now icons with tooltips, the Trains list shows each engineer's mode, destination, speed, cars and alerts, and a scale bar shows the distances.
- Web map: painted terrain with rivers, shores, forests with tree crowns, rock and relief, clearings along the track, and the land around the railroad filled in to the edges.
- Web map: wooden and steel bridges drawn with their decks, trestles and railings.
- Web map: the new Train Orders orders (turntable, pick up, keep, couple back, new waits) in the train panel.

### 1.2.0

- **Web map**: a live map of the railroad in your browser, for a second monitor, with the game's real terrain. Every train and car moves live, even during fast forward. Open it from the new **Web Map** button on the map toolbar, with the game's HTTP Server turned on.
- Click a train for a panel with everything about it: status, alerts, locomotives, every car, and its Train Orders.
- Industries, stations, interchanges and fuel stops as icons, with cars spotted and on the way and passengers waiting. Click one for its panel and to light up its cars across the map.
- Hover any track for its name, class, speed limit, condition and grade.
- Search, a list of trains, layers by type, a scale bar and full screen.

### 1.1.0

- **Track Class**: a new Display option colors every track by its class, 1 to 5, with a legend on the toolbar. It takes turns with the game's Track Condition.
- With Track Class on, hover any track for its class, speed limit, condition, the class it's built for and the grade.
- **Route preview**: the selected train's route to its waypoint, drawn on the track with animated stripes and a flag with its name.
- **Signals**: every signal as an arrow on the track, in its aspect's color, with its interlocking and aspect on hover.
- **Train alerts**: a badge on locomotives that are derailed, have a hotbox, are low on or out of fuel, are stopped by a problem, or have waited over a minute at a red signal. Red ones pulse, and hovering the locomotive lists them.

### 1.0.1

- Fixed car and locomotive labels on the map showing upside down until you zoomed, and after a train turned around.

### 1.0.0

- Every car on the map, colored by destination town.
- Hover tooltips for cars and locomotives.
- Warning dots for hotboxes, low oil and handbrakes set in a train.
- Right-click the track to send the selected locomotive there.
- Industry tracks in blue, with their name and cars on hover, and the mainline in green.
- **Cars**, **Car Labels**, **Car Alerts**, **Industry Tracks** and **Mainline** options in the map's Display menu.
- **Towns**, **Trains** and **Follow** on the map toolbar.
- Extended zoom.

## Credits

By QbanRambo. MIT license (see `LICENSE`). Uses [Harmony](https://github.com/pardeike/Harmony) by Andreas Pardeike (MIT, see `THIRD-PARTY-NOTICES.md`) to hook into the map. Inspired by Map Enhancer by Vanguard and its community fork by Refizar08; the car icons recreate Map Enhancer's (MIT, see `THIRD-PARTY-NOTICES.md`).
