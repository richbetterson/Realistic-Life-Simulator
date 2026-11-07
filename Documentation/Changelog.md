**English** | [Русский](Changelog_RU.md)

# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/). "Tests" lists the core unit-test count at each version. In-game status is described in the [README](../README.md#14-testing-status).

## [1.1.0] - 2026-11-07 - Rental properties, no pedestrian accidents, automatic speed limit

Not yet tested in GTA V: the changes below are covered by unit tests only.

### Changed
- **Apartments and houses are rental investments** (`OwnershipRules.IsRentalInvestment / EarnsRent / CanEnter`, `PropertySystem`). They can no longer be entered: no *Tour the interior*, no *Enter*, no teleport or interior loading. Once bought, their rent is paid with every bill whether or not the old *Rent it out* switch was on (no save migration needed). Their menu shows *Rental income*, *Net per bill* and *Next payment*; map icons and door prompts say "rental". Parking spaces at apartments and houses still work at the door marker.
- **Garages are unchanged**: tour, enter (with the 0.6.0 interior fallbacks), store and take out cars, *Rent it out* toggle.
- A player who loads an old save while standing inside an apartment interior is offered *Leave*, so nobody gets stuck.
- *Properties* page: owned rentals show "rent +$X"; new row *Next rent / bills*.
- Version 1.1.0; the one-time update notice no longer mentions pedestrian accidents or interiors.

### Added
- **Automatic road limit** for the speed limiter (`AutoSpeedLimitRules`, `AutoSpeedLimitTracker`, `SpeedLimiter`). Setting *Life Hub > Vehicle controls > Speed limiter > Automatic road limit* (`[SpeedLimiter] AutoRoadLimit`, off by default). The limiter follows the detected road limit **+ 10 km/h**, read once per second from the same classification as the LIMIT panel (`TrafficLawSystem.RoadLimitForAutoLimiter`, which also works with traffic laws off). A new limit must be read twice in a row; lower limits are approached in 5 km/h steps; an unknown road keeps the last limit for 15 s, then the manual value is the fallback. No notification per road change. Boats are excluded. With the setting off the manual limiter is unchanged. The *Speed limiter on / off* key switches the automatic mode off as well.
- Driving panel: second line **AUTO 60** / **LIMITER 60** while the limiter is active. Speed limiter menu: *Applied limit* row; *Limiter* renamed *Manual limiter*.
- 9 new unit tests (`V110Tests.cs`).

### Removed
- **Pedestrian accidents.** The pedestrian detection (`TryDetectPedestrian`, `IsStruckPedestrian`, the 7 m scan every 300 ms) and the pedestrian fallback in the impact check are gone, so hitting a pedestrian never opens a scene, calls 911, writes a report, issues fines/points or affects the licence. Removed the *Pedestrian accidents* setting (an old `PedestrianAccidents=` entry is ignored and dropped on the next save), the *Striking a pedestrian* entry in *Fine amounts*, the pedestrian notification and the pedestrian wording in the licence help. Vehicle accidents, ambulances, police and reports are unchanged; old pedestrian reports still display.

### Tests
- 66 / 66. Updated: the billing test now uses a garage for the "not rented out" case; the 0.6.0 settings round trip no longer sets the removed pedestrian setting.

## [1.0.0] - 2026-11-07 - First stable release

The first public stable version. It contains everything from 0.6.0 below, which was never published on its own.

### Changed
- Version number is now 1.0.0 (Life Hub, log and save files). The one-time update notice now says "Updated to 1.0".
- README testing status updated: the author played the release in GTA V story mode before publishing it.

### Tests
- 57 / 57.

## [0.6.0] - 2026-11-06 - Enforcement, police, pedestrian accidents, interiors, speed limiter

### Fixed
- **Traffic fines were almost never issued.** The default mode *Witnessed* fined only violations a police officer saw, so the 0.5 logs show nearly every violation as "fine 0". New default mode **Automatic** (`EnforcementRules`): a witnessing officer still pulls you over; without a witness, reliable detections are fined automatically ("traffic camera"); uncertain detections are recorded only. Existing `Settings.ini` files with the old default `FineMode=Witnessed` are moved to `Automatic` once (`SettingsVersion=2`); any explicit later choice is kept.
- **Driver's licence logic.** A suspended licence was shown as "ЛИШЁН" (revoked) in the Russian Life Hub, and in accident investigations the officer accepted any licence ("Licence N, name"). There is now one source of truth, `LicenceRules.Status()`: *Valid*, *Suspended* or *Revoked*, used by the Life Hub, police stops, accident investigations and patrol checks. The officer always states the real status; a revoked or suspended licence is never reported as valid. The status is also checked in the police database if the licence is not handed over.
- **Dispatch "no road node found"** (seen many times in the 0.5 logs) for police, ambulances and taxis: `Dispatch.FindSpawnNode` probed only the 30th-220th closest road nodes, which in the dense city are all closer than 200 m and in the countryside all further than 500 m. It now snaps points on rings around the target to the nearest road (16 probes at most), with the old search as a last resort.
- **Property interiors failed to load** ("Interior not available", seen for Integrity Way and the Sandy Shores garage). The interior is now pinned and must be ready, the scene and collision are streamed while the player is frozen, and only then is the result checked. A similar interior is used as a fallback (HighApt → MidApt → LowApt; garages likewise); if none loads the player stays outside. Leaving puts the player on the ground at the door.
- **Pedestrian collisions were mostly missed**: the impact check needs a sudden change of velocity or body damage, which hitting a person rarely produces. A separate light check (people within 7 m, every 300 ms while driving) now detects them.
- Speeding 50+ km/h over the limit produced **two fines** (speeding and reckless driving) for one act; it is now one speeding violation (highest tier) marked "excessive speed".
- **Stop / give-way false positives** on through roads (0.5 log: "lowest speed 100 km/h"): ignored above 65 km/h, and never fined by camera by default.
- Speeding right after leaving a freeway was fined against the new, lower limit at once; there is now a 5 s grace period.
- The *Receipts* page showed most categories (Property, Medical, Rent, Insurance…) in English only.
- The *Traffic & fines* page and settings still described Phase 3 as "coming".
- The accident menu said repairs "arrive with a later phase" although repair shops exist.
- Identical notifications within 4 s are now posted once.

### Added
- **Automatic traffic enforcement**: per-violation camera switches (`[Enforcement]`), optional immediate payment from cash, duplicate protection by type, 80 m area and time (`ViolationDeduper`), one fine per speeding episode (`SpeedingMonitor`), red-light camera fines only with two or more waiting cars, wrong-way fines only when road geometry and oncoming traffic agree. Every fine notification states the violation, details, amount, points, who issued it and where to pay.
- **Configurable fine amounts** (`[FineAmounts]`, Life Hub > Traffic & fines > Enforcement and fine amounts), multiplied by `FineMultiplier`.
- **Police patrols** (`PolicePatrolSystem`): cruising patrols, slow neighbourhood patrols, roadside speed checks and two-officer foot patrols; city police or county sheriffs by area. Density Off / Low / Normal / High (`[Police] PatrolDensity`), at most 1-4 extra units (2 on the Low profile), one new unit per 15 s at most, always created 200-500 m away out of sight; GTA's own police within 300 m are counted. Patrols witness violations, can be handed a traffic stop or an NPC stop, are sent to accidents first, and check the licence of drivers they follow (`LicenceChecks`).
- **Pedestrian accidents**: struck pedestrians stay at the scene; a witness calls 911 if you don't; one ambulance per scene; paramedics follow the patient, kneel and treat (non-graphic medic scenarios) and take them to hospital; police for moderate and severe cases; ambulance cancelled if the person leaves; gives up after 3 min if the scene can't be reached. New violation *Striking a pedestrian* (fine by severity, 6 points), licence suspended (severe) or revoked (fatal), hit-and-run revokes the licence (and a fatal one gives a wanted level).
- **Licence status**: timed police suspensions, revocation at 24 points, after driving while suspended twice or after a serious pedestrian collision; *Apply for reinstatement* ($1,500, after 3 h of play, no unpaid fines; clears points). New violation *Driving with a revoked licence*. A reminder when you start driving without a valid licence; a notification whenever the status changes (`LicenceWatchModule`).
- **Speed limiter** (`SpeedLimiter`): Life Hub > Vehicle controls > Speed limiter: on/off, enter a value with GTA's on-screen keyboard (whole numbers 10-300 km/h, invalid input rejected with a message), ±5 km/h, works while driving and with any vehicle you drive except aircraft and trains; the cap is removed from a vehicle when you leave it. Optional unassigned key *Speed limiter on / off*.
- **Shorter red-light waits** (`TrafficLightAssist`): if you are first in line at a red light with cross traffic flowing for `MaxRedWaitSeconds` (20 s), your axis gets `AssistGreenSeconds` (10 s) of green plus 3 s amber, the cross axis is set to red and approaching cross traffic is held. GTA's own cycle is not changed; no red-light violation is recorded during the assisted phase.
- **Usable property interiors**: entry checks ownership, rental and wanted level; inside: leave, sleep 8 h, nap 1 h, shower, watch TV, save, garage (take a car out to the entrance). The property you are in is saved, so the exit works after loading a game inside.
- **Life Hub**: *Traffic & fines* (was Driving record), *Police & legal status* (was Licence and documents; status, reason and time left, reinstatement, points, fines, wanted level, record), new *Vehicle controls* with *Speed limiter*, *Properties* (was Real estate). Settings: fine modes, automatic enforcement, fine amounts, traffic lights, patrol density, patrol licence checks, pedestrian accidents. Status page: patrol count and red-light assists.
- Settings `[Enforcement]`, `[FineAmounts]`, `[TrafficLights]`, `[SpeedLimiter]`, `[Police] PatrolDensity, LicenceChecks`, `[Accidents] PedestrianAccidents`, `[General] SettingsVersion`.
- Save format 7 (licence status fields, property the player is in), upgraded automatically from 6.

### Tests
- 57 / 57 (17 new: enforcement decisions, speeding episodes, duplicates, false-positive guards, fine amounts, settings upgrade, settings round trip, licence status, escalation and reinstatement, officer lines EN/RU, save migration 6→7 with licence persistence, stable violation names, speed limiter input and units, patrol budgets, traffic-light rules, pedestrian fines, limiter key). Two existing tests were updated for intended changes (default fine mode Automatic; schema after migration is now 7).
- Real 0.5.1 settings, key bindings and save upgraded on copies without data loss.
- **Not yet run in the game.**

## [0.5.1] - 2026-11-06 - Passenger rides and taxi service

### Added
- **Passenger rides with NPC drivers** (`PassengerSystem`, `RideSession`, `PassengerRules`): ask a nearby NPC driver for a ride and choose a free passenger seat (front, rear left, rear right, extra seats). The driver's seat is never offered.
- Five driver attitudes (Friendly, Neutral, Cautious, Unfriendly, Suspicious) derived from the existing driving personalities. The accept/refuse decision is based on the situation and is deterministic for each driver.
- Passenger menu: journey information, request a stop, stop at a safe place, exit (only when stopped), continue, drop me at my waypoint (if the driver agrees), let the driver go their own way.
- Driver reactions to a wanted level, drawn weapons, shooting and fire.
- Crashes during a ride are handed to the existing NPC accident system; the ride continues afterwards if possible.
- **Taxi service** (`TaxiService`): call a taxi (ambient taxi nearby, or one sent from out of sight), pickup at the nearest road, destination choice (map waypoint, owned properties), metered fare (base + per km + per minute stopped in traffic), payment from cash with any shortfall added to debt, receipts.
- Life Hub page **Rides and taxi**, and a **Passengers and taxi** settings page.
- Settings `[Passenger] Enabled` and `[Taxi] Enabled, BaseFare, PricePerKm, PricePerMinuteWaiting`.
- Optional key actions *Passenger / rides menu* and *Call a taxi* (unassigned).
- Ride statistics in the save (save format 6, upgraded automatically from 5).
- `NpcAccidentSystem.IsInvolved()`; `Dispatch.FindSpawnNode()` made public for reuse by the taxi.

### Tests
- 40 / 40 (8 new: key upgrade, settings upgrade, save migration 5→6, attitudes, acceptance, reactions, seats, fares).
- Not yet run in the game.

## [0.5.0] - 2026-11-05 - Properties and vehicle ownership

### Added
- 18 properties (apartments, houses, garages): view, interior tour, buy, enter, rest, rent out, sell, garage storage.
- Owned vehicles: dealership, registration of existing cars, GTA personal vehicles as managed cars, mileage, engine wear and service intervals, performance and fuel penalties for worn engines, damage and service history, persistence of parked cars, recovery service.
- Insurance (None / Basic / Full): premiums, repair cover with deductibles, total-loss payouts.
- Repair shops: inspection, service, damage repair.
- Bills every N minutes of play (maintenance, premiums, rent income) and automatic debt settlement.
- Life Hub pages *My vehicles* and *Real estate*; settings page *Property and vehicles*.
- Save format 5.

### Tests
- 32 / 32. Run once in the game together with the earlier phases (see README testing status).

## [0.4.0] - 2026-11-05 - NPC traffic

### Added
- Six NPC driver personalities with ability, aggressiveness, driving style and speed based on the road's real limit.
- Occasional NPC mistakes (red light, speeding burst, late braking); police can stop NPCs for them.
- NPCs make way for emergency vehicles with sirens.
- NPC-vs-NPC accident scenes with details exchange, 911 calls, police investigation, ambulance and slowed traffic; simplified resolution far from the player.
- Witness statement and 911 call at NPC accidents; *City incidents* page.
- New violation: failure to yield to an emergency vehicle.
- Save format 4.

### Tests
- 27 / 27.

## [0.3.0] - 2026-11-05 - Police and accidents

### Added
- Police traffic stops by the officer who witnessed the violation: documents, questions, warning or citation, evading the police.
- Per-character licence, points (suspension at 12), insurance and driver reputation.
- Player accident detection with evidence, severity, the other driver's reactions, the accident menu, police investigation and fault, reports, ambulance, leaving the scene.
- Dispatch: borrow a nearby police car or create units out of sight that drive in on the roads.
- NPC police stops; *Call police (911)*.
- Life Hub pages *Licence and documents*, *Accident reports*; save format 3.

### Tests
- 24 / 24.

## [0.2.0] - 2026-11-05 - Fuel, traffic laws, Russian

### Added
- Fuel system by vehicle class, 27 gas stations with pump refuelling, jerry can, receipts.
- NPC fuel and refuelling trips.
- Speed limits by road type and traffic-violation detection with fines (witnessed / always / off).
- Driving panel above the SpeedoSignals speedometer (if installed).
- Full Russian interface with automatic language selection.
- Save format 2.

### Tests
- 18 / 18.

## [0.1.0] - 2026-11-05 - Core framework

### Added
- Module scheduler with failure isolation, validated configuration, unassigned key bindings with conflict detection, verified saves with backups, logging, performance profiles, mouse menu, Life Hub markers, play time and autosave.

### Tests
- 11 / 11. Loaded cleanly in the game.
