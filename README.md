**English** | [Русский](README_RU.md)

# Realistic Life Simulator

**A modular life-simulation overhaul for GTA V story mode.** Fuel and gas stations, traffic laws with automatic fines, more police on the streets, police traffic stops, accident scenes (vehicles and pedestrians) with ambulances and police investigations, NPC drivers with personalities, homes you can actually live in, garages, owned cars that wear out, a speed limiter, rides with NPC drivers and a metered taxi service. Every menu works with the mouse, and no keyboard shortcut is assigned unless you choose one.

Version **1.0** · Single-player (story mode) only · ScriptHookVDotNet v3 · English and Russian interface

> [!NOTE]
> **Testing status.** 1.0 is the first stable release. The game-independent logic passes **57 of 57 unit tests**, real 0.5.1 settings and save data upgrade without loss, and the author played the release in GTA V story mode before publishing it. See [Testing status](#14-testing-status). Please report any problem you find.

### New in 1.0

- **Traffic fines are issued automatically.** New default mode *Automatic*: a police officer who sees a violation pulls you over; without a witness, speeding, red lights (two or more cars waiting), wrong-way driving and not yielding to sirens are fined by traffic cameras. Uncertain detections are only recorded. Every fine shows the reason, amount, points and who issued it. All amounts are configurable.
- **More police in the city.** Extra patrol cars, slow neighbourhood patrols, roadside speed checks and foot patrols (Off / Low / Normal / High, at most 1–4 extra units). They appear far away and drive in on the roads; GTA's own police are counted so units are not duplicated.
- **Pedestrian accidents.** Hitting a pedestrian opens an accident scene: a witness calls 911, one ambulance comes, paramedics kneel and treat the person (non-graphic) and take them to hospital, police investigate. Fines, points and licence consequences; hit-and-run is punished.
- **Speed limiter.** *Life Hub > Vehicle controls > Speed limiter*: type a speed (e.g. 60) and switch it on.
- **Property interiors work.** Interiors are streamed before you are moved, with a fallback interior and a safe exit; inside you can sleep, nap, shower, watch TV, save and use the garage.
- **Driver's licence fixed.** One source of truth: *Valid*, *Suspended* or *Revoked*, shown correctly everywhere (police stops, accident investigations, Life Hub). A revoked licence is never accepted; reinstatement in the Life Hub.
- **Shorter red-light waits** when you are first in line, with cross traffic held.
- Bug fixes: police / ambulance / taxi dispatch "no road node found", duplicate speeding + reckless fines, stop-sign false positives on through roads, untranslated receipt categories, and more (see the [changelog](Documentation/Changelog.md)).

---

## Contents

1. [Project description](#1-project-description)
2. [What Realistic Life Simulator is](#2-what-realistic-life-simulator-is)
3. [Main features and gameplay systems](#3-main-features-and-gameplay-systems)
4. [NPC behaviour and interactions](#4-npc-behaviour-and-interactions)
5. [Accidents and emergency response](#5-accidents-and-emergency-response)
6. [Passenger rides with NPC drivers](#6-passenger-rides-with-npc-drivers)
7. [Taxi service and fares](#7-taxi-service-and-fares)
8. [Life Hub interface and controls](#8-life-hub-interface-and-controls)
9. [Installation](#9-installation)
10. [Requirements and dependencies](#10-requirements-and-dependencies)
11. [Configuration and customisation](#11-configuration-and-customisation)
12. [Performance](#12-performance)
13. [Known limitations](#13-known-limitations)
14. [Testing status](#14-testing-status)
15. [Troubleshooting](#15-troubleshooting)
16. [Credits and license](#16-credits-and-license)
17. [Development status](#17-development-status)

More detail: [Features](Documentation/Features.md) · [Installation guide](Documentation/Installation.md) · [Changelog](Documentation/Changelog.md) · [Roadmap](Documentation/Roadmap.md)

---

## 1. Project description

Realistic Life Simulator turns Los Santos into a place where everyday driving has consequences. You buy fuel, pay fines, deal with the police and with other drivers after a crash, look after the cars you own, pay bills for your homes, and can travel as a passenger. The mod is written in C# for ScriptHookVDotNet v3. Every system is a separate module that can be switched off in the settings.

## 2. What Realistic Life Simulator is

- **A story-mode script mod.** One DLL in the game's `scripts` folder, plus a `RealisticLifeSimulator` folder for settings, data, saves and logs that the mod creates itself.
- **Built from the game's own systems.** NPC behaviour uses GTA's AI tasks: police and ambulances drive in on real roads, and nothing spawns next to you.
- **Safe by default.** All shortcut keys are unassigned. Configuration files are validated and repaired (a copy of a damaged file is kept). Saves are verified before they are written, keep five rolling backups and seven daily snapshots, and are never overwritten by an older version of the mod.
- **Made for modest PCs.** Low / Balanced / High performance profiles limit how many NPCs are simulated and how often.
- **Bilingual.** Every visible text exists in English and Russian. *Auto* picks Russian when the game or Windows uses Russian.
- **Not for GTA Online.** The mod does nothing in multiplayer and must not be used there.

## 3. Main features and gameplay systems

| System | What it does |
|---|---|
| **Fuel** | Tank size and consumption by vehicle class (19 classes, electric cars use none), affected by distance, throttle, RPM and idling. Fuel is remembered per car. You get warnings at 15 % and 5 %; at 0 % the engine stops. |
| **Gas stations** | 27 stations. Stand at a real pump, press the Context key (**E** by default), pick an amount, watch the refuelling animation, pay from cash and get a receipt. GTA's jerry can also works. |
| **Traffic laws** | Speed limits by road type (freeway 110, highway 90, rural 80, city 50, residential 40 km/h; editable per street). Detects speeding, red lights, stop and give-way, wrong way, no headlights at night, reckless driving and failure to yield to sirens. |
| **Automatic enforcement** *(1.0)* | Fine mode *Automatic* (default): an officer who sees it stops you; otherwise reliable detections are fined by camera (one fine per speeding episode, duplicates blocked by type, place and time). Fines go to *Traffic & fines* (or are paid from cash, optional). |
| **Licence and reputation** | Michael, Franklin and Trevor each have their own licence, points, insurer and driver reputation (0–100). *Suspended* at 12 active points (points expire after 2 hours of play) or by the police; *Revoked* at 24 points, after driving while suspended twice, or after a serious pedestrian collision. Reinstatement: $1,500 after 3 hours of play, all fines paid. |
| **Police patrols** *(1.0)* | Extra patrol cars, slow patrols, roadside speed checks and foot patrols, budgeted and out of sight when created. Patrols can check the licence of the car in front of them. |
| **Speed limiter** *(1.0)* | Enter a maximum speed (10–300 km/h) for the vehicle you drive; on/off toggle, ±5 km/h buttons, works with cars, bikes, boats and bicycles. |
| **Traffic lights** *(1.0)* | If you are first in line at a red light for more than 20 s (configurable), your direction gets a short green and cross traffic waits. |
| **Police traffic stops** | The officer who saw the violation pulls you over, walks to your window, checks your documents and decides between a warning and a fine. Driving off counts as evading the police. You can also call the police yourself. |
| **Accidents** | Crashes with vehicles or pedestrians become scenes with evidence, an investigation, fault, a report, and an ambulance when needed. See [section 5](#5-accidents-and-emergency-response). |
| **Properties** | 18 homes, apartments and garages. View, tour the interior, buy, enter and walk around, sleep, nap, shower, watch TV, save, take cars out of the garage, rent out, sell for 85 %. |
| **Owned vehicles** | Buy at the dealership or register a car you already own. Tracks mileage, engine wear, service intervals, insurance and damage history. Parked cars stay where you left them and are restored later. There is a recovery service, and insured cars that are destroyed pay out. |
| **Repair shops** | Inspection, service and damage repair, with insurance cover for owned cars. |
| **Bills** | Maintenance, insurance premiums and rent income are settled every N minutes of play. Whatever your cash doesn't cover becomes debt, which is settled automatically later. |
| **Passenger rides** | Ask NPC drivers for a lift. See [section 6](#6-passenger-rides-with-npc-drivers). |
| **Taxi** | Call a taxi, pick a destination, pay the meter. See [section 7](#7-taxi-service-and-fares). |
| **Records** | Driving record, receipts, accident reports and city incidents, all in the Life Hub. |

The full list, with numbers taken from the source code, is in [Documentation/Features.md](Documentation/Features.md).

## 4. NPC behaviour and interactions

- **Driver personalities.** Nearby NPC drivers are *careful, average, aggressive, slow, professional* or *distracted*. Each personality sets reaction ability, aggressiveness, driving style and target speed relative to the road's real speed limit. Work vehicles get mostly professional drivers.
- **Believable mistakes.** Now and then a driver briefly runs a red light, speeds or brakes late. A police car that sees it can pull that NPC over.
- **Sirens.** Cars ahead of an emergency vehicle with its siren on slow down and make way.
- **NPC refuelling.** NPC drivers use fuel too, and when low they may drive to a nearby station, refuel and carry on.
- **NPC police stops.** Police stop speeding NPCs and write a ticket at the roadside. A few drivers try to flee.
- **Passenger attitudes.** When you ask for a ride, drivers are *friendly, neutral, cautious, unfriendly* or *suspicious*. The attitude follows their driving personality and stays the same for that driver.

## 5. Accidents and emergency response

**Your accidents.** A collision with another vehicle or a pedestrian above 12 km/h (configurable) opens an accident scene. The evidence is recorded at the moment of impact: both speeds, the direction of impact, whether the other car was stopped, and any violation you committed in the previous 10 seconds. The other driver brakes, gets out (or stays inside if injured), and after a serious crash calls 911. Press **E** at the scene to check on them, exchange details, call the police or an ambulance, wait, or leave.

**Investigation.** The officer drives in on the roads (urgently for serious crashes), inspects both cars, talks to the other driver and then to you, decides fault from the evidence, issues fines and points only if you are at fault, and files a report. Leaving the scene of a serious accident is an offence. Paramedics treat injured people (and you, for a bill).

**Pedestrians** *(1.0)*. Hitting a person at 5 km/h or more is detected by its own light check (the impact barely changes the car's speed, so 0.5 missed most of them). The person stays at the scene, sitting on the ground. If you do not call, a witness calls 911 after 7–11 s: one ambulance comes (never more than one per scene), the paramedics walk to the person even if they moved, kneel and treat them with the game's medic animations, and take them to hospital in the ambulance. Police come for moderate and severe cases. If the person walks away or disappears, a pending ambulance is cancelled; an ambulance that cannot reach the scene within 3 minutes gives up. The officer decides fault (you are at fault above 15 km/h or with a violation just before), fines *Striking a pedestrian* (by severity), adds 6 points, and suspends (severe) or revokes (fatal) the licence. Leaving the scene after hitting a pedestrian revokes the licence; leaving after a fatal one also gives a wanted level.

**NPC accidents.** Crashes between NPCs near you play out the same way: the drivers exchange details, and serious ones get police and an ambulance. Traffic slows around the scene. You can call 911 or give a witness statement. Far away, the outcome is worked out without simulating anyone.

**Dispatch.** A mod patrol car or free police car nearby takes the call first. Otherwise a unit is created on a road 200–500 m away, out of sight. Responders are handed back to the game when they leave. *(1.0: the road search now probes rings around the scene instead of counting road nodes, which fixes "no road node found" in dense city areas and the countryside.)*

## 6. Passenger rides with NPC drivers

*New in 0.5.1.*

1. Stand next to a car that is stopped or moving slowly and has an NPC driver.
2. Open **Life Hub > Rides and taxi > Ask a nearby driver for a ride**. Cars within about 14 m are listed, with the reason any of them can't be asked (taxi, emergency vehicle, no free seat, damaged, driver panicking, moving too fast…).
3. Choose a car, then a **free passenger seat**: front passenger, rear left, rear right, or extra seats in buses and vans. The driver's seat is never offered.
4. The driver decides. The decision depends on their attitude, the time of day, your driver reputation, your health, the weather, the type of vehicle and whether the car was moving. Each driver has a fixed level of patience, so asking the same driver again under the same conditions gives the same answer. Drivers always refuse if you are wanted, holding a weapon, or asked them less than 2 minutes ago.
5. If they agree, they wait while you walk to the seat with the game's own entry animation.

**During the ride** the NPC stays the driver and follows its own route. It stops at red lights and for traffic, keeps to the speed limit, and makes no "mistakes" while you are on board. The route changes only if you ask to be dropped at your map waypoint and the driver agrees (friendly up to 4 km, neutral 2 km, cautious 1.5 km; the others never).

**Passenger menu:** journey information, request a stop, stop at a safe place, exit, continue, drop me at my waypoint, let the driver go their own way. A friendly or neutral driver stops at once on a slow road. A cautious driver, or anyone on a fast road, pulls over a little further on. An unfriendly driver grumbles and pulls over 6–12 s later. **You are never moved out of a moving car**: asking to exit while moving makes the driver pull over first.

**Unusual situations.** A wanted level, a drawn weapon, shooting or a fire make the driver warn you, pull over and ask you out, or stop at once, depending on their attitude. If your car crashes into another, the existing NPC accident system takes over. You stay inside, and the ride continues afterwards if the car still drives.

## 7. Taxi service and fares

*New in 0.5.1.*

1. **Life Hub > Rides and taxi > Call a taxi** (or the optional *Call a taxi* key).
2. A free taxi within 400 m takes the job. Otherwise one is sent from a road 200–500 m away that you can't see. It drives to the nearest road on the street network and is shown on the map.
3. When it arrives it waits at the kerb for up to 2.5 minutes. Choose **Get in the taxi**, or walk up and take a rear seat.
4. Pick a destination: your **map waypoint** or **one of your properties**. An estimated fare is shown for each.
5. The taxi drives there obeying traffic rules, pulls over, and you pay. You can change the destination, stop early and pay the meter, or get out (it pulls over first).

**Fare** = `BaseFare` + `PricePerKm` × km actually driven + `PricePerMinuteWaiting` × minutes stopped in traffic. The total is rounded up to whole dollars.

| Setting | Default |
|---|---|
| Base fare | $3.00 |
| Per km | $2.20 |
| Per minute stopped in traffic | $0.35 |

Example: 5 km with 2 minutes in traffic = 3.00 + 11.00 + 0.70 = **$15**.

You pay from cash. If your cash doesn't cover the fare, the rest is added to your unpaid bills. Each payment appears under *Receipts* as "Taxi". There is no charge if you cancel before getting in, or if the taxi crashes and can't continue. No taxi comes while the police are after you. Taking the taxi's driver's seat cancels the booking.

## 8. Life Hub interface and controls

The **Life Hub** is the mod's menu. It is drawn in the game's own fonts and works entirely with the mouse.

| How to open it | Default |
|---|---|
| Walk into a **blue Life Hub marker** (blue map icon) and press the Context key | Always available. Markers at Legion Square, Michael's house, Franklin's two houses and Trevor's trailer (editable) |
| A key you assign to *Open Life Hub* | Unassigned |

| In the menu | Action |
|---|---|
| Left click | Select |
| Right click / Esc | Back |
| Mouse wheel | Scroll |
| Arrow keys + Enter | Also work |

**Life Hub pages:** **Traffic & fines** (was *Driving record*) · **Police & legal status** (was *Licence and documents*) · **Vehicle controls** (speed limiter) · My vehicles · **Properties** (was *Real estate*) · Accident reports · City incidents · Rides and taxi · Call police (911) · Receipts · Settings · Key bindings · Save and status.

**Speed limiter:** *Life Hub > Vehicle controls > Speed limiter* → *Enter a speed...* → type `60` → Enter → switch *Limiter* on. The vehicle then does not accelerate past 60 km/h; braking, steering and reversing are unaffected. Values must be whole numbers from 10 to 300. The limit can be changed while driving and applies to whichever vehicle you drive (not to aircraft or trains); it is removed from a car as soon as you leave it.

**In the world** you use the game's Context key (**E** by default; the prompt on screen shows it): at a fuel pump, at an accident scene, during a traffic stop, at a property door, at the door marker inside a property, at a garage and at a mechanic.

**Optional shortcuts.** All 11 actions start **unassigned**: Open Life Hub, Open settings, Open properties, Open vehicles, Call police, Passenger / rides menu, Call a taxi, Speed limiter on / off, and three reserved for later phases (bank, roadside assistance, job). Assign them in *Life Hub > Key bindings*. The mod warns you when a key is already used by GTA V, by another mod's INI file or by another action. Modifier keys, mouse buttons and Esc can't be assigned. The mod never changes GTA's own controls.

## 9. Installation

Short version below. The detailed step-by-step guide, with checks after each step, is in [Documentation/Installation.md](Documentation/Installation.md).

**For players**

1. Install **ScriptHookV** for your game version from its [official page](http://www.dev-c.com/gtav/scripthookv/) (`ScriptHookV.dll` and `dinput8.dll` go into the GTA V folder).
2. Install **ScriptHookVDotNet v3** from its [official releases](https://github.com/scripthookvdotnet/scripthookvdotnet/releases) (`ScriptHookVDotNet.asi`, `ScriptHookVDotNet2.dll`, `ScriptHookVDotNet3.dll` go into the GTA V folder).
3. Download `RealisticLifeSimulator.dll` from this repository's [Releases](https://github.com/richbetterson/Realistic-Life-Simulator/releases) page, or build it yourself (below).
4. Copy `RealisticLifeSimulator.dll` into `GTA V\scripts\` (create `scripts` if it doesn't exist).
5. Start **story mode**. On first start the mod creates `GTA V\RealisticLifeSimulator\` with `Config`, `Data`, `Saves`, `Logs` and `Backups`, and shows a one-time notification.
6. Walk into a blue Life Hub marker and press **E**.

**Uninstall:** delete `GTA V\scripts\RealisticLifeSimulator.dll`. To remove everything, also delete `GTA V\RealisticLifeSimulator\`. The mod never modifies game files.

**Building from source**

```powershell
cd "C:\Path\To\Grand Theft Auto V"
git clone https://github.com/richbetterson/Realistic-Life-Simulator.git RealisticLifeSimulator
powershell -ExecutionPolicy Bypass -File "RealisticLifeSimulator\Source\build.ps1"            # build + unit tests
powershell -ExecutionPolicy Bypass -File "RealisticLifeSimulator\Source\build.ps1" -Install   # also copy into scripts\ (game must be closed)
```

`build.ps1` uses the C# compiler that ships with Windows (.NET Framework 4.x, `csc.exe`), so nothing is downloaded. It expects `ScriptHookVDotNet3.dll` in the folder **above** the repository, which is why the repository is cloned into the GTA V folder under the name `RealisticLifeSimulator`. That is also the folder the mod uses for its data at runtime. The build writes `Source\bin\` and `Scripts\RealisticLifeSimulator.dll`; both are git-ignored.

## 10. Requirements and dependencies

| Requirement | Notes |
|---|---|
| GTA V for PC, **story mode** | Developed on the Legacy (DirectX 11) edition, build 1.0.3351.0. Other builds need a matching ScriptHookV; the Enhanced edition has not been tried. |
| [ScriptHookV](http://www.dev-c.com/gtav/scripthookv/) by Alexander Blade | Must match your game build. Not included here; get it only from the official page. |
| [ScriptHookVDotNet](https://github.com/scripthookvdotnet/scripthookvdotnet) v3 | Developed against the v3 API 3.7 (runtime 3.6). zlib license. Not included here. |
| .NET Framework 4.8 | Needed by ScriptHookVDotNet. Usually already installed on Windows 10/11. |
| Microsoft Visual C++ Redistributable (x64) | Needed by ScriptHookVDotNet. |
| *Building only:* Windows with .NET Framework 4.x | Provides `csc.exe`. No Visual Studio needed. |

The mod has **no other dependencies** and bundles no third-party code or game assets.

## 11. Configuration and customisation

Most options can be changed in game under **Life Hub > Settings**. They are saved immediately to `GTA V\RealisticLifeSimulator\Config\Settings.ini`, which you can also edit while the game is closed. Invalid or missing values are repaired, and a copy of the old file is kept in `Backups\Config\`.

| Section | Main options (default) |
|---|---|
| `[General]` | `Language` = Auto / English / Russian |
| `[Performance]` | `Profile` = **Low** / Balanced / High |
| `[Saving]` | `AutosaveMinutes` = 5 |
| `[Interface]` | Life Hub map icons, fuel gauge (Always / LowOnly / Off), speed-limit display |
| `[Fuel]` | Enabled, `PricePerLiter` = 1.65, `ConsumptionMultiplier` = 1.00, NPC refuelling, low-fuel warning 15 % |
| `[Traffic]` | Enabled, `FineMode` = **Automatic** / Witnessed / Always / Off, speed tolerance 10 km/h, fine multiplier, and on/off for each violation type |
| `[Enforcement]` *(1.0)* | Camera fines without a witness: `AutoSpeeding`, `AutoRedLight`, `AutoWrongWay`, `AutoFailureToYield` = true; `AutoStopSign`, `AutoHeadlights` = false; `AutoPayFromCash` = false |
| `[FineAmounts]` *(1.0)* | Dollars per violation: speeding 150 / 300 / 600 (up to 20 / 21–40 / over 40 km/h), red light 250, stop 150, wrong way 400, headlights 80, reckless 1000, sirens 350, leaving the scene 750, pedestrian 800, no documents 200, no valid licence 1000, evading 1500, false statement 250 |
| `[TrafficLights]` *(1.0)* | `ShorterRedLights` = true, `MaxRedWaitSeconds` = 20, `AssistGreenSeconds` = 10 |
| `[SpeedLimiter]` *(1.0)* | `Enabled` = false, `LimitKmh` = 60 |
| `[Police]` | Traffic stops for you, traffic stops for NPCs, `PatrolDensity` = Off / Low / **Normal** / High, `LicenceChecks` = true |
| `[Accidents]` | Enabled, minimum impact speed 12 km/h, other drivers and witnesses call 911, `PedestrianAccidents` = true |
| `[NpcTraffic]` | Personalities and limits, mistakes, making way for sirens, NPC accident scenes |
| `[Ownership]` | Bills every 60 min, vehicle wear and rate, service interval 400 km, properties on the map |
| `[Passenger]` | `Enabled` = true |
| `[Taxi]` | `Enabled` = true, `BaseFare` = 3.00, `PricePerKm` = 2.20, `PricePerMinuteWaiting` = 0.35 |
| `[Debug]` | `DebugLogging` = false |

**Upgrading from 0.5.x:** your values are kept and the new sections are added with their defaults (a copy of the old file goes to `Backups\Config\`). Only one value changes: `FineMode=Witnessed`, the old default, becomes `Automatic` once; choose *Witnessed* again in the settings if you prefer it, and it will stay.

**Key bindings** live in `Config\KeyBindings.ini` (every entry empty by default). **World data** lives in `Data\*.ini`: Life Hub points, gas stations, speed-limit overrides, properties, the vehicle catalogue and shops. These files are created on first run and can be edited to move or add locations.

## 12. Performance

The mod was written for a low-end laptop (4-core mobile CPU, 2 GB entry-level GPU, 720p).

- Each system runs on its own timer (100 ms to 10 s). Only drawing runs every frame, and only while something is shown.
- NPC work is limited by the performance profile:

| Profile | NPC radius | NPCs checked per update | Update interval | Tracked NPCs | Distant simulation |
|---|---|---|---|---|---|
| **Low** (default) | 120 m | 4 | 1000 ms | 24 | off |
| Balanced | 180 m | 8 | 600 ms | 48 | every 3 s |
| High | 250 m | 16 | 300 ms | 96 | every 1.5 s |

- Simultaneous NPC accident scenes and NPC refuelling trips: 1 / 2 / 3 for Low / Balanced / High.
- Passenger rides and the taxi check only their own state every 500 ms while idle. Nearby cars are scanned only when you open the ride menu.
- *Life Hub > Save and status* shows the mod's own frame time (average and peak).
- A module that keeps failing is switched off for the session instead of taking the whole mod down.
- Creating a police car, ambulance or taxi loads its models, which can cause a short hitch once per call.
- *1.0:* police patrols run every 2 s, create at most one unit every 15 s, and never more than 1–4 extra units (2 on the Low profile). The speed limiter only reads the speed of your own car each frame while it is on. The pedestrian check looks at people within 7 m every 300 ms while you drive. The red-light assist does nothing unless you are stopped at a traffic-light junction.

## 13. Known limitations

- **Game version.** Developed and tested on the Legacy (DirectX 11) edition, build 1.0.3351.0. The Enhanced edition has not been tried.
- **Red lights.** GTA exposes no traffic-light state, so a red light is inferred from stopped cars next to you. It can miss violations on empty roads. Camera fines need two or more waiting cars; one waiting car is only recorded. It can be switched off.
- **Automatic fines** are only as good as the detection: speed limits are inferred from road type, so a street may have a different limit than you expect (edit `Data\SpeedLimits.ini`). Stop / give-way and headlights are never fined by camera by default.
- **Wrong way** depends on GTA's road data and is disabled at junctions and off-road.
- **Not detected:** dangerous lane changes and overtaking.
- **Traffic lights:** the global signal timing of GTA V cannot be changed by a script. The mod only shortens your own wait when you are first in line; it changes the lights near you and holds cross traffic. Whether every AI driver obeys the changed lights has not been verified in the game.
- **Police patrols** are extra units on top of GTA's own; GTA's ambient police density itself is not changed.
- **Speed limiter** uses GTA's entity speed cap plus a throttle cut; very steep downhills or being pushed by another vehicle can briefly exceed the limit by a few km/h.
- **NPC behaviour is built on GTA's AI.** Ambient reactions (for example panic at gunfire) can interrupt a scene. Every sequence has timeouts and releases its NPCs.
- **Dispatch** may still fail where no road exists within 550 m (islands, mountains). The call then fails cleanly.
- **Interiors.** Properties use GTA Online interiors present in story mode. If one still doesn't load, a similar interior is tried, and if none loads you stay outside. Stored cars are not shown inside garage interiors. Houses use the high-end apartment interior.
- **Coordinates** for gas stations, Life Hubs and properties are approximate and editable.
- **Money** is the character's cash. A bank account is planned for a later phase.
- **Cyrillic text** needs GTA fonts that include it, which is certain when the game itself runs in Russian.
- **Passenger rides:** NPC drivers in GTA have no real destination, so "their own route" is GTA's wander driving. While you ride, that car is excluded from the mod's own siren-yielding and NPC police stops. Some voices lack the spoken line, so only the subtitle appears.
- **Taxi:** destinations are your map waypoint and your properties. Arrival is the nearest road point, which may be across the street from an entrance.
- **Saves** from 1.0 use save format 7 (0.5.1 used 6 and is upgraded automatically). An older DLL opens a newer save read-only to protect it, so roll back the DLL and the `Saves` folder together.
- **Licence on upgrade:** the 1.0 rules apply to existing points. A character with 24 or more active points becomes *Revoked* when the game starts, with a 3-hour waiting period before reinstatement.

## 14. Testing status

| Check | Result |
|---|---|
| Compiles against ScriptHookVDotNet 3.7 with the Windows C# compiler | ✅ 0 errors |
| Core unit tests (`Source/Tests/CoreTests.cs`, `V060Tests.cs`): configuration and repair, key bindings, saves and migrations (schema 1→7), fuel, speed limits, fines, licence points, fault rules, NPC personalities, ownership and insurance, passenger rules, taxi fares, and for 1.0: enforcement decisions, speeding episodes, duplicate protection, false-positive guards, fine amounts, settings upgrade, licence status and reinstatement, speed limiter input and units, patrol budgets, traffic-light rules, pedestrian fines | ✅ **57 / 57 passed** |
| Upgrade of the real 0.5.1 configuration, key bindings and save to 1.0 (on copies) | ✅ all values and records kept; saved and reloaded as schema 7 |
| Every native function hash used in 1.0, checked against ScriptHookVDotNet's own list | ✅ |
| **Played in GTA V by the author before the 1.0 release** (story mode, Legacy build 1.0.3351.0) | ✅ checked by the author |

Unit tests check the game-independent rules in `Source/Core`. Code that talks to the game (`Source/Game`) is verified by playing. Different game builds, other mods and unusual situations can still expose problems, so feedback from real play sessions is very welcome.

## 15. Troubleshooting

| Problem | What to check |
|---|---|
| Nothing happens, no notification | ScriptHookV must match your game version. Check `ScriptHookV.log` and `ScriptHookVDotNet.log` in the GTA V folder. The DLL must be in `GTA V\scripts\`. |
| ScriptHookVDotNet console (F4) shows an error for this mod | Read `GTA V\RealisticLifeSimulator\Logs\RealisticLifeSimulator.log`. Turn on *Settings > Debug logging* for more detail. |
| "… stopped after errors" notification | One module failed five times and was switched off for this session; the rest keeps working. The log names the module. |
| Can't find a Life Hub | Look for the blue icon near Legion Square or a main character's home, or assign a key to *Open Life Hub*. |
| Russian text shows as squares | Set *Settings > Language* to English, or run the game in Russian. |
| A key does nothing | Shortcuts are unassigned by default. Assign one in *Life Hub > Key bindings*. |
| Settings reset | A damaged `Settings.ini` is repaired automatically; the old copy is in `RealisticLifeSimulator\Backups\Config\`. |
| Save problems | `Saves\Backups\` holds five rolling backups and daily snapshots. A damaged save is moved aside, never deleted. |
| Low FPS | Keep the **Low** profile, or switch off NPC traffic, NPC accidents or NPC refuelling in Settings. |
| Taxi never arrives | Some places have no road the dispatcher can use; walk to a main road and call again. The booking cancels itself after 5 minutes. |
| Fines feel too strict / too lenient | *Life Hub > Traffic & fines > Enforcement and fine amounts*: change the fine mode, switch camera types off, or change amounts. |
| Too many / too few police | *Settings > Police and accidents > Police patrols* (Off / Low / Normal / High). |
| Can't enter my property | It must be yours and not rented out (*Rent it out: Off*), and you must be on foot without a wanted level. If the interior does not load, wait a few seconds and try again; the log shows "Interior not available". |
| The car won't go faster | The speed limiter is on: *Life Hub > Vehicle controls > Speed limiter*. |

When reporting a problem, please attach `RealisticLifeSimulator.log` (with Debug logging on), your game build and your ScriptHookV / ScriptHookVDotNet versions.

## 16. Credits and license

- **Realistic Life Simulator**: created and maintained by the owner of this repository. Development was assisted by Claude (Anthropic).
- **Grand Theft Auto V** © Rockstar Games / Take-Two Interactive. This project is not affiliated with or endorsed by them and contains no game files or assets.
- **ScriptHookV** by Alexander Blade. Not redistributed here.
- **ScriptHookVDotNet** by crosire and contributors, under the zlib license. Not redistributed here.

**License:** no license has been chosen for this project yet, so no `LICENSE` file is included. Until one is added, default copyright applies and the author keeps all rights. GitHub's terms of service allow viewing and forking public repositories; any other reuse of the code needs the author's permission.

## 17. Development status

Version 1.0 is the first stable release. It contains Phases 1–5, the 0.5.1 passenger/taxi update and the enforcement / police / accidents / interiors update (developed as 0.6.0). Next on the [roadmap](Documentation/Roadmap.md): Phase 6 (economy, banking and jobs), Phase 7 (emergency services and roadside assistance), Phase 8 (world events and weather), Phase 9 (interface integration), Phase 10 (optimisation and compatibility testing). See the [changelog](Documentation/Changelog.md) for the version history.
