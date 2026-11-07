**English** | [Русский](Installation_RU.md)

# Installation guide

Realistic Life Simulator is a ScriptHookVDotNet v3 script for **GTA V story mode on PC**. It never modifies game files, so the only file it places in the game's script folder is `RealisticLifeSimulator.dll`.

> Never use script mods in GTA Online.

## 1. Requirements

| Component | Where to get it | Check |
|---|---|---|
| GTA V (PC), story mode | Your legal copy | Developed on the Legacy (DirectX 11) edition, build 1.0.3351.0 |
| ScriptHookV + ASI loader | [dev-c.com/gtav/scripthookv](http://www.dev-c.com/gtav/scripthookv/) (official page only) | Must support your game build |
| ScriptHookVDotNet **v3** | [github.com/scripthookvdotnet/scripthookvdotnet/releases](https://github.com/scripthookvdotnet/scripthookvdotnet/releases) | Developed against API 3.7 / runtime 3.6 |
| .NET Framework 4.8 | Windows Update / Microsoft | Usually already installed on Windows 10/11 |
| Visual C++ Redistributable (x64) | Microsoft | Needed by ScriptHookVDotNet |

This repository does **not** contain ScriptHookV, ScriptHookVDotNet or any game files.

## 2. Install ScriptHookV

1. Download the version that matches your game build.
2. Copy `ScriptHookV.dll` and `dinput8.dll` from its `bin` folder into the GTA V folder (the one that contains `GTA5.exe`).

**Check:** start the game once. A `ScriptHookV.log` file appears in the GTA V folder without errors.

## 3. Install ScriptHookVDotNet v3

1. Download the latest release.
2. Copy `ScriptHookVDotNet.asi`, `ScriptHookVDotNet2.dll`, `ScriptHookVDotNet3.dll` (and the `.ini`, if included) into the GTA V folder.
3. Create a folder named `scripts` in the GTA V folder if it doesn't exist.

**Check:** after starting story mode, `ScriptHookVDotNet.log` appears in the GTA V folder; the SHVDN console opens with F4.

## 4. Install Realistic Life Simulator

1. Get `RealisticLifeSimulator.dll`, either from the [Releases](https://github.com/richbetterson/Realistic-Life-Simulator/releases) page of this repository or by building it yourself (section 7).
2. Copy it to `GTA V\scripts\RealisticLifeSimulator.dll`.
3. *(Optional)* Copy the repository's `Config` and `Data` folders to `GTA V\RealisticLifeSimulator\`. You don't have to: the mod creates both with the same defaults on first start.

Resulting layout:

```
Grand Theft Auto V\
├─ GTA5.exe
├─ ScriptHookV.dll, dinput8.dll                  (ScriptHookV)
├─ ScriptHookVDotNet.asi, ScriptHookVDotNet3.dll (ScriptHookVDotNet)
├─ scripts\
│   └─ RealisticLifeSimulator.dll                (this mod)
└─ RealisticLifeSimulator\                       (created by the mod on first start)
    ├─ Config\    Settings.ini, KeyBindings.ini
    ├─ Data\      LifeHubs, GasStations, SpeedLimits, Properties, VehicleCatalog, Shops (.ini)
    ├─ Saves\     profile.xml + Backups\
    ├─ Logs\      RealisticLifeSimulator.log
    └─ Backups\   copies of repaired config files
```

## 5. First start

1. Start **story mode** and wait until you control your character.
2. A one-time notification appears: *"Realistic Life Simulator installed. No keys are assigned…"*.
3. Open the map and look for the **blue Life Hub icon** (Legion Square, Michael's house, Franklin's houses, Trevor's trailer).
4. Walk into the blue marker and press the Context key (**E** by default; the on-screen prompt shows the key).
5. The menu works with the mouse: left click selects, right click or Esc goes back.

**Check:** `GTA V\RealisticLifeSimulator\Logs\RealisticLifeSimulator.log` contains `Game-side start complete. Modules: 14`.

Recommended first steps in the menu:
- *Settings > Language*: Auto, English or Russian.
- *Settings > Performance profile*: keep **Low** on weaker PCs.
- *Key bindings*: optionally assign a key to *Open Life Hub* so you can open the menu anywhere.

## 6. Updating

1. Close the game.
2. Replace `GTA V\scripts\RealisticLifeSimulator.dll` with the new version.
3. Keep the `GTA V\RealisticLifeSimulator\` folder. Settings, key bindings and saves upgrade automatically: new options are added with their defaults, your values are kept, and a copy of any rewritten file goes to `Backups\Config\`.

**Updating from 0.5.x to 0.6.0:**
- The save is upgraded from format 6 to 7 automatically (licence status fields, the property you are in). Nothing is removed.
- `Settings.ini` gets the new sections `[Enforcement]`, `[FineAmounts]`, `[TrafficLights]`, `[SpeedLimiter]` and new entries in `[Police]` and `[Accidents]`. One value changes: `FineMode=Witnessed` (the old default) becomes `Automatic` once, because with *Witnessed* almost no fine was ever issued. Choose *Witnessed* again in *Settings > Traffic & fines* if you want it; it is then kept.
- `KeyBindings.ini` gets an empty `ToggleSpeedLimiter=` entry; your assigned keys are kept.
- Licence rules apply to existing points at the first start: 24 or more active points means *Revoked* (reinstatement after 3 hours of play in *Life Hub > Police & legal status*).
- No new files are installed outside `GTA V\scripts\RealisticLifeSimulator.dll` and the mod's own folder.

**Downgrading:** a save written by a newer version is opened read-only by an older version, to protect it. If you go back to an older DLL, also restore the `Saves` folder from that time (the mod keeps `Saves\Backups\`).

## 7. Building from source

Requirements: Windows with .NET Framework 4.x (provides `csc.exe`), Git, and ScriptHookVDotNet installed in your GTA V folder. Visual Studio is not needed.

```powershell
cd "C:\Path\To\Grand Theft Auto V"
git clone https://github.com/richbetterson/Realistic-Life-Simulator.git RealisticLifeSimulator
cd RealisticLifeSimulator
powershell -ExecutionPolicy Bypass -File Source\build.ps1            # compile + run the unit tests
powershell -ExecutionPolicy Bypass -File Source\build.ps1 -Install   # also copy the DLL into ..\scripts\ (game closed)
```

What `build.ps1` does:
1. Compiles `Source\Core\*.cs` and `Source\Game\*.cs` into `Source\bin\RealisticLifeSimulator.dll`, referencing `..\ScriptHookVDotNet3.dll`. This is why the repository must sit directly inside the GTA V folder.
2. Compiles `Source\Core` with `Source\Tests` into `Source\bin\RlsCoreTests.exe` and runs it. The build fails if any test fails.
3. Copies the DLL to `Scripts\RealisticLifeSimulator.dll` inside the repository.
4. With `-Install`, copies it to `GTA V\scripts\` (refuses while GTA V is running).

Expected output ends with `Passed: 40  Failed: 0`. Deprecation warnings from ScriptHookVDotNet APIs are expected and harmless.

## 8. Uninstalling

- Delete `GTA V\scripts\RealisticLifeSimulator.dll`.
- To remove all data too, delete `GTA V\RealisticLifeSimulator\`.

Nothing else is changed by the mod.
