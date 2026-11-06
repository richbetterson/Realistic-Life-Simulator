**English** | [Русский](Features_RU.md)

# Features

Every number on this page is taken from the source code of version 0.5.1. The source file for each system is named so you can check it.
In-game testing status is in the [README](../README.md#14-testing-status).

- [Core framework](#core-framework)
- [Fuel and gas stations](#fuel-and-gas-stations)
- [Traffic laws and fines](#traffic-laws-and-fines)
- [Licence, points and reputation](#licence-points-and-reputation)
- [Police traffic stops](#police-traffic-stops)
- [Player accidents](#player-accidents)
- [Dispatch: police and ambulances](#dispatch-police-and-ambulances)
- [NPC traffic](#npc-traffic)
- [NPC accidents and city incidents](#npc-accidents-and-city-incidents)
- [Properties and bills](#properties-and-bills)
- [Owned vehicles, wear and insurance](#owned-vehicles-wear-and-insurance)
- [Repair shops](#repair-shops)
- [Passenger rides with NPC drivers](#passenger-rides-with-npc-drivers)
- [Taxi service](#taxi-service)
- [Life Hub menu](#life-hub-menu)
- [Saves and data safety](#saves-and-data-safety)

---

## Core framework
`Game/RlsMain.cs`, `Game/Modules.cs`, `Core/ConfigurationManager.cs`, `Core/KeyBindings.cs`, `Core/KeyConflictChecker.cs`, `Core/Log.cs`, `Core/Localization.cs`

- One script (`RlsMain`) schedules all systems. Each module has its own update interval; only drawing runs every frame.
- A module that throws 5 times in a row (or fails while starting) is disabled for the session, with a notification. The rest of the mod keeps running.
- Settings (`Config/Settings.ini`) are validated on load. A missing entry is added with its default; an invalid value is reset; a damaged file is replaced and a copy kept in `Backups/Config/`.
- 10 keyboard actions, **all unassigned by default**. Modifier keys, mouse buttons and Esc are rejected. The key-conflict checker warns about GTA's default controls, keys found in `ScriptHookVDotNet.ini` and other mods' `scripts/*.ini`, and the mod's own actions. Alt/Ctrl combinations are left to Windows.
- The log (`Logs/RealisticLifeSimulator.log`) is buffered, flushed every 5 s and rotated at 1 MB. Optional debug logging.
- Every visible text exists in English and Russian. *Auto* language follows the game language or Windows. Money and number formats follow the language.

## Fuel and gas stations
`Game/FuelSystem.cs`, `Core/FuelModel.cs`, `Game/NpcFuelModule.cs`, `Game/DriveHud.cs`

- Tank size and consumption for 19 road vehicle classes (for example compact 45 L, sedan 60 L, truck 300 L, motorcycle 16 L). Bicycles, boats, aircraft and trains are excluded; known electric models use no fuel.
- Consumption depends on distance, throttle, RPM, engine running and idling, times a configurable multiplier (default 1.0: a sedan tank lasts roughly an hour of driving).
- Fuel level is saved per vehicle (plate + model), for the 60 most recently used vehicles. A car you have never driven starts at 25–85 %.
- Warnings at the configured level (default 15 %) and at 5 %. At 0 % the engine stops and won't restart.
- **27 gas stations** (`Data/GasStations.ini`). Refuelling is offered next to the game's real pump props: stand at a pump, press Context, choose fill-up / +10 L / +25 L. An animation plays with a progress bar (2.5 L/s), you pay only for what was pumped (default $1.65/L), and you get a receipt.
- **Jerry can:** GTA's petrol can refuels a vehicle anywhere (1 L/s, 20 L per full can).
- **Driving panel:** speed limit and fuel gauge. If the SpeedoSignals speedometer script is installed, the panel is placed directly above it (its position is read from `scripts/SpeedoSignals.ini`, read only); otherwise it uses a default position. The mod draws no speedometer of its own.
- **NPC refuelling:** nearby NPC drivers have fuel that drains at a personal rate (0.6x–1.6x). When low, they may drive to a station within 900 m, get out, wait, get back in and drive on. 1/2/3 simultaneous trips (Low/Balanced/High), with a cooldown between trips.

## Traffic laws and fines
`Game/TrafficLawSystem.cs`, `Core/TrafficRules.cs`

- **Speed limits:** freeway 110, highway/route 90, rural 80, city 50, residential 40 km/h, from GTA road-node flags and street names. Overrides per street or zone in `Data/SpeedLimits.ini`.
- **Violations and base fines** (multiplied by the fine multiplier):

| Violation | Base fine |
|---|---|
| Speeding (sustained 3 s, over the tolerance) | $150 / $300 (>20 km/h over) / $600 (>40 km/h over) |
| Running a red light | $250 |
| Failure to stop / yield | $150 |
| Driving the wrong way | $400 |
| No headlights at night | $80 |
| Reckless driving | $1,000 |
| Failure to yield to an emergency vehicle | $350 |
| Causing a collision | $600 |
| Leaving the scene of an accident | $750 |
| Failure to present documents | $200 |
| Driving with a suspended licence | $1,000 |
| Evading the police | $1,500 |
| False statement to police | $250 |

- **Fine modes:** *Witnessed* (default: a police officer within 80 m with line of sight), *Always*, or *Off* (record only). Speed tolerance default 10 km/h. Each violation type can be switched off.
- Every violation is recorded with time, street, zone and details. Fines are paid in *Life Hub > Driving record* or at a traffic stop.
- Paused during story missions. Emergency vehicles with the siren on are exempt.

## Licence, points and reputation
`Core/PoliceRules.cs`

- Michael, Franklin and Trevor each have their own licence number, insurer and policy, points, reputation, stops and warnings.
- Fines add licence points (for example 2/4/6 for the speeding tiers). **Suspended at 12 active points.** Points expire after 2 hours of play.
- Reputation 0–100, starting at 70. It drops with fines, warnings and accidents you cause, and recovers with distance driven without violations. It affects warnings at traffic stops and whether NPC drivers give you a ride.

## Police traffic stops
`Game/PoliceStopSystem.cs`

- The police car that **saw** the violation pulls you over (lights, then a prompt). Once you stop, it parks behind you and the officer walks to your window.
- Mouse menu with the officer's lines as subtitles: ask why, hand over licence / registration / insurance, refuse, answer "Do you know why I stopped you?", ask for directions to a gas station.
- A stolen car (GTA's stolen flag) gives a 2-star wanted level.
- **Warning or citation**, decided from your reputation, attitude, severity, unpaid fines and recent stops. Some offences never get a warning (speeding over 40 km/h, reckless driving, evading the police).
- Pay on the spot or later. Driving off = evading the police ($1,500, 6 points, 1-star wanted level).
- **Call police (911)** from the Life Hub or an optional key: a unit drives to you for directions, your record, or paying fines.
- **NPC stops:** police pull over NPCs that speed at least 15 km/h over the limit or are seen making a mistake. The officer writes a ticket at the window; about 8 % of drivers flee and the chase is given up after 30 s.

## Player accidents
`Game/AccidentSystem.cs`, `Core/PoliceRules.cs`

- Checked every 100 ms while you drive: a sudden change in velocity or body/engine damage, with another vehicle touching or damaged by your car, or a pedestrian hit. Walls and poles alone are ignored. Minimum impact speed 12 km/h (configurable).
- **Evidence** at impact: both speeds, whether the other party was ahead, whether it was stopped, and any moving violation of yours in the previous 10 s.
- **Severity:** minor / moderate / severe, from speed, damage, injury and death.
- The other driver brakes, gets out (an injured driver stays inside) and waits. After a serious crash they call 911. About 10 % drive off (never after a severe crash).
- **Accident menu** (Context near the scene): check on the other driver, exchange information, call police, request an ambulance (if someone is hurt), wait, leave.
- **Investigation:** the officer inspects both cars, talks to the other driver and then to you (statement and documents), decides fault from the evidence, fines you only if you are at fault (a false statement adds a fine), and files a report you can read under *Accident reports*.
- **Leaving the scene** of a moderate or severe accident with another party present: $750 and 6 points (always if police were present, otherwise 60 % chance that your plate was noted).
- Paramedics treat injured people; if you are hurt they treat you for a bill of up to $250.

## Dispatch: police and ambulances
`Game/Dispatch.cs`

- Police: a free police car within 350 m is used first; otherwise a unit is created on a road node 200–500 m away that you cannot see (sheriff in the county, LSPD in the city). Ambulances are always created that way, with two paramedics.
- Responders always drive in on the road network; priority calls use lights and siren. You see an ETA and a map marker.
- Units are released back to the game after they drive away, and when scripts unload.

## NPC traffic
`Game/NpcTrafficModule.cs`, `Core/NpcRules.cs`

| Personality | Speed vs limit | Ability | Aggressiveness | Mistakes / min |
|---|---|---|---|---|
| Careful | 92 % | 1.0 | 0.10 | 0.01 |
| Average | 100 % | 0.8 | 0.40 | 0.03 |
| Aggressive | 120 % | 0.75 | 0.90 | 0.12 |
| Slow | 80 % | 0.9 | 0.00 | 0.01 |
| Professional | 97 % | 1.0 | 0.30 | 0.005 |
| Distracted | 97 % | 0.45 | 0.50 | 0.10 |

- Ordinary cars: about 20 % careful, 12 % aggressive (24 % in sports cars and on motorcycles), 10 % slow, 6 % professional, 10 % distracted, the rest average. Work vehicles: mostly professional, never aggressive.
- All personalities stop at lights and for cars and pedestrians. Target speed is recalculated from the road's real limit every 6–8 s.
- **Mistakes:** running a red light (stop-at-lights removed for 12 s), speeding burst (+35 % for 20 s), late braking (ability near zero for 15 s).
- **Sirens:** cars up to 45 m ahead of an emergency vehicle with its siren on, going the same way, slow right down for 5 s.
- Budget per performance profile (see README §12). Police, mission entities and NPCs taken over by another system are never touched.

## NPC accidents and city incidents
`Game/NpcAccidentSystem.cs`

- NPC collisions are detected from latched body damage (drop above 25) and the other vehicle involved.
- **Full scene** within 250 m (max 1/2/3 scenes for Low/Balanced/High): both drivers brake, get out (the injured stay in), inspect their cars and exchange details. Aggressive drivers may flee (15 %, never after a severe crash). Minor crashes end there. Serious crashes, injuries or a hit-and-run → 911 → police inspect both cars, talk to both drivers and write the report; an ambulance treats the injured.
- A road speed zone (28 m, about 11 km/h) slows traffic around the scene until it clears.
- **Simplified scene** further away: the outcome is resolved without simulating people and all entities are released.
- You can press Context to **call 911**, or to **give a witness statement** to the investigating officer (+2 reputation).
- The last 50 NPC accidents and NPC stops are listed in *Life Hub > City incidents*.

## Properties and bills
`Game/PropertySystem.cs`, `Core/OwnershipRules.cs`, `Data/Properties.ini`

- 18 apartments, houses and garages. Walk to the door marker to see details, tour the interior and buy.
- Owned: enter, rest (8 hours pass), garage (store or take out owned cars; drive an owned car to the door marker of a property with garage slots and press Context), rent it out, sell for 85 % of the price.
- Interiors are GTA Online interiors present in story mode. If one fails to load, you are returned outside straight away.
- **Bills** every N minutes of play (default 60): maintenance and insurance premiums out, rent in. Unpaid amounts become debt, settled automatically when cash allows.

## Owned vehicles, wear and insurance
`Game/VehicleOwnership.cs`, `Core/OwnershipRules.cs`, `Data/VehicleCatalog.ini`, `Data/Shops.ini`

- Buy at the dealership (catalogue in `Data/VehicleCatalog.ini`) or register a car you already have, such as your character's own car or one you bought ($150; stolen cars can't be registered).
- GTA's own personal vehicles can be registered as *managed*: tracked (mileage, wear, insurance, service) while GTA keeps spawning them, so nothing is duplicated.
- Tracks mileage and engine condition. Wear is 0.02 %/km × wear rate, three times faster when the service is overdue (default interval 400 km). Below 50 % condition the car loses top speed (up to 20 %); below 25 % it uses 20 % more fuel. Very high mileage limits what a service can restore.
- Owned cars stay where you parked them. Far away they are saved and removed from the world, and they reappear with their colours and mods when you return.
- **Insurance:** None, Basic or Full. Premium per billing cycle is 0.4 % (Basic) or 0.9 % (Full) of the car's value, minimum $25. Repairs: the insurer pays 50 % (Basic) or 80 % (Full) above a $500 / $250 deductible. Total loss pays 40 % / 80 % of the current value.
- **Recovery service** ($250) brings a lost car to you. Damage history and service history are kept per car.

## Repair shops
`Game/RepairShops.cs`

- Park within 25 m of a repair shop and talk to the mechanic on foot: inspection ($50), service (from $180, by vehicle class) and damage repair.
- Owned cars get insurance cover and a service history entry; any other car can still be repaired at full price.

## Passenger rides with NPC drivers
`Game/PassengerSystem.cs`, `Game/RideSession.cs`, `Core/PassengerRules.cs` (new in 0.5.1)

- **Who can be asked:** an ordinary NPC driver within about 14 m, not police, emergency, military, mission or dispatch vehicles, not a taxi, not a car in an accident scene. Bicycles, boats, aircraft and trains are excluded, as are burning, upside-down or badly damaged cars, panicking drivers and cars moving faster than 9 m/s (about 32 km/h).
- **Seats:** the free passenger seats reported by the game: 0 front passenger, 1 rear left, 2 rear right, 3+ extra seats. The driver's seat is never offered; occupied seats are never offered.
- **Attitude:** Friendly, Neutral, Cautious, Unfriendly or Suspicious, weighted by driving personality (careful → mostly cautious, aggressive → mostly unfriendly, professional → mostly neutral). A fixed per-driver value picks within those weights, so each driver is consistent.
- **Decision:** base willingness (Friendly 0.85, Neutral 0.60, Cautious 0.42, Unfriendly 0.22, Suspicious 0.12), adjusted for night (22:00–06:00), your reputation, injury (+), bad weather (+), sports or super car (×0.6), motorcycle (×0.7), work vehicle (×0.55) and a moving car (×0.7). The result is compared with that driver's fixed patience (0.2–0.8). Always "no" when you are wanted, holding a weapon, or asked the same driver within 2 minutes.
- **Reactions:** a spoken line and subtitle. Unfriendly drivers honk. Suspicious or frightened drivers drive off quickly (still obeying lights).
- **Boarding:** the driver brakes and waits; you walk to the chosen seat with the game's enter animation. Cancelled cleanly if you walk away or it takes longer than 20 s.
- **Journey:** the NPC stays the driver. Its own route is GTA's wander driving; there is no teleporting. Law-abiding driving style with indicators; speed capped at the road's limit; driver ability at least 0.85 and aggressiveness at most 0.35 while you are on board. If it hasn't moved for 45 s, the route is re-issued.
- **Passenger actions:** journey information (driver attitude, driving style, seat, status, speed/limit, location, destination, distance, time) · request a stop · stop at a safe place (kerb point ahead) · exit (only when stopped; otherwise the driver pulls over first) · continue · drop me at my waypoint (Friendly ≤ 4 km, Neutral ≤ 2 km, Cautious ≤ 1.5 km) · let the driver go their own way.
- **Stop response:** urgent stop on a slow road = immediate for Friendly, Neutral and Suspicious drivers. Cautious drivers, or anyone on a fast road (limit ≥ 80 or speed > 55 km/h), pull over ahead. Unfriendly drivers pull over after 6–12 s.
- **Unusual situations:** wanted level, weapon out / drive-by, shooting, vehicle on fire. Depending on attitude the driver warns once, pulls over and asks you out, or stops at once. Once stopped, a driver who wants you out insists after 10 s (1.5 s if the car is on fire).
- **Accidents:** a collision with another vehicle is reported to the NPC accident system (the same scene, police and ambulance). You stay inside; when the scene closes the driver gets back in and the ride continues (or stops if the car is wrecked). A crash with no second vehicle gives a short stop, a reaction and a city-incident entry.
- **Safety:** GTA's automatic shuffle into the driver's seat is blocked during the ride and restored afterwards. The driver and car are reserved (persistent) so other NPC systems don't take them over, and are released on every ending path.

## Taxi service
`Game/TaxiService.cs`, `Core/PassengerRules.cs` (`TaxiRules`) (new in 0.5.1)

- **Booking:** not while wanted, during a mission or in a vehicle. A free ambient taxi within 400 m (empty, drivable) takes the job; otherwise one is created on a road 200–500 m away out of sight.
- **Pickup:** drives to the nearest road node to you (re-routes if you move more than 60 m, retries if stuck for 25 s, gives up after 5 minutes). Waits at the kerb up to 2.5 minutes with a short horn.
- **Boarding:** *Get in the taxi* (a free rear seat) or walk up yourself. Taking the driver's seat cancels the booking.
- **Destinations:** map waypoint and your owned properties, each with an estimate (straight distance × 1.35 road factor).
- **Meter:** distance actually driven + minutes stopped in traffic (paused during accident scenes). Fare = BaseFare + PricePerKm × km + PricePerMinuteWaiting × minutes, rounded up to whole dollars. Defaults $3.00 / $2.20 / $0.35, configurable in game and in `[Taxi]`.
- **Payment:** from cash; any shortfall is added to unpaid bills (debt). Receipt notification plus a "Taxi" entry in *Receipts*. Statistics: taxi rides and total spent.
- **No charge** if you cancel before boarding, or if the taxi crashes and can't continue.
- Change destination, stop here and pay, or exit (pulls over first) at any time.

## Life Hub menu
`Game/MenuSystem.cs`, `Game/LifeHubMenus.cs`, `Game/LifeHubModule.cs`, `Game/RideMenus.cs`

- Mouse-driven panel in the game's own fonts, laid out on a 1280×720 virtual screen so it scales with any resolution. Left click selects, right click / Esc goes back, the wheel scrolls; arrow keys and Enter also work.
- Opened at 5 Life Hub markers (Legion Square, Michael's house, Franklin's two houses, Trevor's trailer; `Data/LifeHubs.ini`) with the Context key, or with an optional key.
- Pages: Driving record · My vehicles · Real estate · Licence and documents · Accident reports · City incidents · Rides and taxi · Call police (911) · Receipts · Settings (with sub-pages for fuel, traffic, police and accidents, NPC traffic, property and vehicles, passengers and taxi) · Key bindings · Save and status (save state, play time, mod frame cost, NPC counts).

## Saves and data safety
`Core/SaveManager.cs`, `Core/FileUtil.cs`

- `Saves/profile.xml`, written atomically after the new data has been verified by reading it back.
- 5 rolling backups and 7 daily snapshots. A save that can't be read is restored from the newest good backup; the damaged file is moved aside, never deleted.
- Save format version 6. Older saves (versions 1–5) upgrade automatically without losing data. A save made by a newer version of the mod is never overwritten: saving is disabled to protect it.
- Autosave every 5 minutes by default, and on script unload.
