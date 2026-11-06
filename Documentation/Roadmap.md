# Roadmap

| Phase | Content | Status |
|---|---|---|
| 1 | Core framework: configuration, key bindings, saves, menu, Life Hub, performance profiles | Done |
| 2 | Fuel system, NPC refuelling, traffic laws, Russian translation | Done |
| 3 | Police traffic stops, NPC stops, accidents, investigation, reports, dispatch | Done |
| 4 | NPC personalities, speed limits, mistakes, siren yielding, NPC accidents, city incidents | Done |
| 5 | Properties, garages, vehicle ownership, wear, insurance, repair shops, bills | Done |
| 5.1 | NPC passenger rides and taxi service | Done (not yet run in game) |
| 6 | Economy, banking and jobs | Planned |
| 7 | Emergency services and roadside assistance | Planned |
| 8 | World events and weather realism | Planned |
| 9 | Interface integration and persistent data | Planned |
| 10 | Optimisation and compatibility testing | Planned |

"Done" means compiled and covered by the core unit tests. See the README section
"Testing status" for what has and has not been seen working inside GTA V.

## Architecture rules

- New systems implement `IModule` (and `IFrameModule` only if they must draw every frame). They are registered in `RlsMain.StartGameSide`.
- NPC work must respect `PerformanceProfile` budgets: radius, checks per update, and tracked-NPC cap.
- Persistent data goes into `SaveData`. Increase `SaveData.CurrentSchema` and migrate older saves; never discard data.
- New keyboard actions are added to `RlsAction` and always start unassigned.
- Every menu page must be backed by working functionality. No placeholder pages.
- Source must stay C# 5 compatible (Windows' built-in compiler).
- Every visible string uses `L.S("English", "Русский")`. Never add a string in one language only.
- Game-independent rules live in `Source/Core` and are unit tested; game code lives in `Source/Game`.
