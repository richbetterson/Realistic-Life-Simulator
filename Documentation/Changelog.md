**English** | [Русский](Changelog_RU.md)

# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/). "Tests" lists the core unit-test count at each version. In-game status is described in the [README](../README.md#14-testing-status).

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
