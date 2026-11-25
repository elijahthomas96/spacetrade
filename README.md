# Space Trading Simulator

A single-player Windows game about running an interstellar transport company. Start with one docked courier and 6,000 credits. Quote, provision and launch long Alcubierre-drive voyages, then collect payment on delivery. Reinvest in ships and drives, and grow into a network of automated freight and passenger routes. The galaxy keeps running while the game is closed.

The design spec is in [`space-trading-simulator.md`](space-trading-simulator.md). What has and has not been verified is recorded in [`VERIFICATION.md`](VERIFICATION.md).

## Editor version

**Unity 6000.6.10f1** (revision `40e81493c67d`), as pinned in `ProjectSettings/ProjectVersion.txt`. Install it through Unity Hub with the **Windows Build Support (IL2CPP or Mono)** module. The default scripting backend is Mono, which is always installed with the Windows editor.

Packages (`Packages/manifest.json`):

- `com.unity.test-framework` 1.8.0
- `com.unity.inputsystem` 1.20.1 (Active Input Handling is set to the Input System; the game reads input through IMGUI events)
- built-in modules: imgui, jsonserialize, ui, uielements

The project uses the built-in render pipeline. There is one startup scene, `Assets/Scenes/Main.unity`, containing a single `GameBootstrap` object. The camera, starfield, map, ship sprites and the entire IMGUI interface are created from code at runtime.

## Project layout

| Path | Contents |
|---|---|
| `Assets/SpaceTrade/Simulation/` | Authoritative simulation. Plain C# with no UnityEngine references (`SpaceTrade.Simulation.asmdef`, `noEngineReferences: true`). The real-time clock is injected. |
| `Assets/SpaceTrade/Game/` | Unity layer: bootstrap, save and autosave, the procedural map, and the IMGUI panels (`SpaceTrade.Game.asmdef`). |
| `Assets/SpaceTrade/Tests/Editor/` | Deterministic NUnit tests. Unity runs them as edit-mode tests; `dotnet test` compiles the same files. |
| `Assets/Editor/BuildScript.cs` | Batch-mode build entry points. |
| `dotnet/` | .NET SDK projects that compile the simulation and tests outside Unity. |
| `tools/build-windows.ps1` | Builds and zips the Windows x64 player. |

## Opening the project (Windows)

1. In Unity Hub choose **Add > Add project from disk** and select the repository root. Open it with 6000.6.10f1.
2. The first import resolves packages and compiles the four assemblies.
3. Open `Assets/Scenes/Main.unity` and press **Play**. The editor counts as a debug-capable build, so the **Debug mode** toggle appears in the top bar.

Headless import, which also confirms that the scripts compile:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.6.10f1\Editor\Unity.exe" -batchmode -quit -projectPath "C:\src\spacetrade" -logFile "C:\src\spacetrade\Builds\import.log"
```

## Running tests

Simulation tests with the .NET 8 SDK. This needs no Unity install:

```powershell
dotnet test dotnet\SpaceTrade.Simulation.Tests
```

The same tests as Unity edit-mode tests (do not pass `-quit` with `-runTests`):

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.6.10f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\src\spacetrade" -runTests -testPlatform EditMode -testResults "C:\src\spacetrade\Builds\editmode-results.xml" -logFile "C:\src\spacetrade\Builds\editmode-tests.log"
```

In the editor, use **Window > General > Test Runner > EditMode > Run All**.

## Building the Windows player

The PowerShell wrapper builds the player and zips it:

```powershell
# Release build: Builds\Windows-x64\SpaceTrade.exe and Builds\SpaceTrade-Windows-x64.zip
.\tools\build-windows.ps1 -UnityExe "C:\Program Files\Unity\Hub\Editor\6000.6.10f1\Editor\Unity.exe"

# Development build (debug skip available): Builds\Windows-x64-Dev\ and Builds\SpaceTrade-Windows-x64-Dev.zip
.\tools\build-windows.ps1 -UnityExe "C:\Program Files\Unity\Hub\Editor\6000.6.10f1\Editor\Unity.exe" -Development

# Run the edit-mode tests first and stop if any fail
.\tools\build-windows.ps1 -UnityExe "C:\Program Files\Unity\Hub\Editor\6000.6.10f1\Editor\Unity.exe" -RunTests
```

If script execution is blocked, run `powershell -ExecutionPolicy Bypass -File tools\build-windows.ps1 -UnityExe ...`.

To call Unity directly:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.6.10f1\Editor\Unity.exe" -batchmode -quit -projectPath "C:\src\spacetrade" -buildTarget Win64 -executeMethod SpaceTrade.EditorTools.BuildScript.BuildWindowsRelease -logFile "C:\src\spacetrade\Builds\build.log"
& "C:\Program Files\Unity\Hub\Editor\6000.6.10f1\Editor\Unity.exe" -batchmode -quit -projectPath "C:\src\spacetrade" -buildTarget Win64 -executeMethod SpaceTrade.EditorTools.BuildScript.BuildWindowsDevelopment -logFile "C:\src\spacetrade\Builds\build-dev.log"
```

`-buildOutput <path\to\SpaceTrade.exe>` overrides the output location. The methods exit with code 1 if the build fails. Both builds are also on the **Space Trade** menu in the editor. Everything under `Builds/` is gitignored.

To launch, run `SpaceTrade.exe` from the build folder, or unzip the archive anywhere and run it. Keep `SpaceTrade_Data`, `UnityPlayer.dll` and the other companion files next to the executable. The player opens in a 1600×900 resizable window.

## Playing

The game opens on the galaxy map with your courier **Wayfarer** selected, docked at **Brightwater** (default seed 1977). **Vulcan Reach** is exactly 4.5 ly away; at 1.5c that is 3 game years, or 3 real hours at 1x.

**The clock.** At 1x, one real hour is one game year. Pause, 1x, 3x and 5x are in the top bar. Speed changes apply to every ship at once. Arrival dates, manifests and payouts never change after launch. ETAs show both the game arrival date and the remaining real time at the current speed.

**A first voyage.**

1. Select the ship, then choose **Plan voyage**.
2. Pick a destination. The list shows distance, transit time and whether there is freight or passenger work.
3. Choose freight and passengers with the steppers. The quote shows hold and fuel use, provisions aboard, required and short, the top-up cost, and revenue, costs, net profit and profit per game year. It also lists likely return loads.
4. Choose **Provision now**, or leave "Buy any missing provisions at launch" ticked, then **Launch**.
5. The map follows the ship. If it is still selected when it arrives, its delivery receipt opens. Otherwise click the arrival notification, or use **Last receipt** in the ship panel.

**Growing.** Shipyards are at the core world, industrial hub and technology centre. They sell a courier, a bulk freighter and a passenger liner, plus drive upgrades. A yard can only install a drive on a ship docked at that yard. A remote ship shows the reason in red. Use **Route** (or **Set up automated route**) in the ship panel to give a ship a repeating circuit. For each stop you set:

- which freight to load
- whether to carry passengers
- whether to provision automatically
- a minimum departure load, in tonnes or passengers
- whether the leg is an empty repositioning leg

The editor estimates the whole circuit. If you cannot afford provisions for any viable job, the panel of a docked ship offers an emergency contract.

**Fleet list** (left). Status shows as In transit, Waiting (with the unmet condition), Blocked (with the problem) or Idle. Click a ship to select it.

### Controls

| Input | Action |
|---|---|
| Left-click | Select a ship or system on the map |
| Left-drag | Pan the map |
| Mouse wheel, `+` / `-` | Zoom |
| Arrow keys / WASD | Pan |
| `Space` | Pause / play |
| `1` / `2` / `3` | 1x / 3x / 5x |
| `Tab` | Select and follow the next ship |
| `F` | Toggle following the selected ship |
| `C` | Close-up view of the selected ship |
| `Home` | Centre on the selected ship |
| `Esc` | Close the close-up view, panel or summary; otherwise open the menu |

The **Menu** button opens:

- switching between the campaign and the demonstration company, which is a separate save
- a new game, either with the default seed or a random one
- quit

**Help** shows these controls in-game.

### Debug mode

The **Debug mode** toggle and the **Skip to next arrival** button exist only in the Unity Editor and in Development Builds. They are compiled out of release builds. The simulation also rejects the skip command unless debug mode is allowed by the build and enabled by the player. Skip jumps to the earliest pending arrival and processes every event up to and at that time. It is disabled when no ship is in flight. A normal release build has no player-triggered time jump.

## Saves

Saves are versioned JSON files in Unity's `Application.persistentDataPath`. On Windows that is:

```
%USERPROFILE%\AppData\LocalLow\SpaceTradeDev\Space Trading Simulator\
    campaign.sts.json     fresh-start campaign (created on first launch)
    demo.sts.json         populated demonstration network (Menu > Open demonstration company)
    *.sts.json.bak        previous good copy, used if the main file is unreadable
    *.unreadable-<time>   kept if both the save and its .bak fail to load (a new game starts)
```

Writes are atomic: the game writes a temporary file, flushes it, then swaps it in with `File.Replace`, keeping the previous save as `.bak`. The game saves every 30 real seconds, after each launch, purchase or route change, on focus loss and on quit.

**Offline progress.** Reopening a running save applies the real time elapsed since the last save, at the saved speed. A paused save stays paused. Deliveries, replenishment, automated departures and later arrivals are processed in order. A return summary lists deliveries, earnings and ships needing attention. The catch-up result is saved immediately together with its timestamp, and every voyage is paid exactly once, so reloading cannot pay twice. Moving the system clock backwards never reverses game time.

Delete the folder above to reset all progress.
