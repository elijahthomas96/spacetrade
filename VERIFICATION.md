# Verification report

This report records what was actually run, where it ran, and what still needs to be checked on a Windows PC with Unity 6000.6.10f1.

**Summary.** The simulation and its 53 deterministic tests were built and run with the .NET SDK on a Linux VM, and all of them passed. Unity was not available on the VM, so the project has **not** been opened, compiled by Unity, played or built in Unity. The Unity-facing C# was compiled against older Unity reference assemblies as a type check only. No Windows player or zip exists yet.

## 1. Tests that actually ran

| | |
|---|---|
| Where | Cloud Linux VM (`Linux 6.12.113+`, x64) |
| Toolchain | .NET SDK 8.0.428; NUnit 3.14.0, NUnit3TestAdapter 4.6.0, Microsoft.NET.Test.Sdk 17.11.1 |
| Command | `dotnet test dotnet/SpaceTrade.Simulation.Tests` |
| Build under test | The simulation sources in `Assets/SpaceTrade/Simulation/**`, compiled as `netstandard2.1` / C# 9 by `dotnet/SpaceTrade.Simulation`. These are the same files Unity compiles through `SpaceTrade.Simulation.asmdef`. Not a Unity build. |
| Result | **53 passed, 0 failed, 0 skipped** (about 130 ms) |

The test files in `Assets/SpaceTrade/Tests/Editor/` are also Unity's edit-mode tests, through `SpaceTrade.Tests.Editor.asmdef`. They have **not** been run inside Unity.

### Acceptance item 2: deterministic tests

| Requirement | Tests |
|---|---|
| Drive math | `AlcubierreTransitMatchesSpecTable` (×3: 4.5 ly at 1.5c, 2.25c and 3.75c; checks game years and real seconds at 1x, 3x and 5x)<br>`OneRealHourAt1xIsExactlyOneGameYear`<br>`OnlyThreeSpeedsAreAccepted`<br>`SpeedMultiplierAppliedOnce` (3x, 5x)<br>`FrameRateDoesNotChangeEventTiming`<br>`GameTimeNeverRunsBackward`<br>`SpeedChangeKeepsManifestSuppliesAndPayout`<br>`TimingScenario_SixMinutesPauseThenFiveX` (item 4 on a manual clock) |
| Capacity and provisioning | `ProvisionsScaleWithPeopleAndDuration`<br>`QuoteIncludesSupplyMassInHoldAndFuel`<br>`OverfullHoldExplainsTonnesToUnload`<br>`OneClickProvisioningBuysExactlyTheShortfall`<br>`PreviewMatchesActualDepartureAndReceipt`<br>`SuppliesBoughtEarlierAreExpensedOnceWhenConsumed` |
| Competing claims on 80 t | `CompetingClaimsNeverExceedEightyTonnes`<br>`CompetingClaimsAcrossDestinationsShareOneSupplyPool`<br>`CompetingAutomatedShipsResolveInShipIdOrder` |
| Failed dispatch rollback | `FailedDispatchConsumesNothing` |
| Exactly-once payouts | `PayoutHappensExactlyOnce`<br>`SettledVoyageIdsBlockDuplicateSettlement`<br>`ReopeningASettledSaveNeverPaysTwice` |
| Sustained automated circuits | `SustainedAutomatedCircuitKeepsRunningAndProfits`<br>`RouteSurvivesInTransitAssignment` |
| Minimum-load waiting | `MinimumLoadWaitsWithReasonThenDepartsWhenSupplyReplenishes`<br>`MinimumLoadLargerThanCapacityBlocksTheShip`<br>`InfeasibleNextLegKeepsShipDockedAndFlagged` |
| Empty return costs | `EmptyRepositioningLegCostsAreEstimatedAndCharged` |
| Emergency-contract recovery | `EmergencyContractRecoversFromInsolvency`<br>`EmergencyCannotBeFarmedForMoneyOrInventory`<br>`AfterRecoveryTheCompanyCanAffordARegularJob`<br>`NotOfferedWhenSolvent` |

Tests that support other parts of the spec:

- Debug skip gating and simultaneous arrivals: `DebugSkipIsRejectedUnlessAllowedAndEnabled` and `DebugSkipProcessesEveryArrivalAtThatTimestamp`.
- Item 1, in the simulation only: `TwoShipsTravelIndependently` and `LocalYardInstallsDriveAndRemoteUpgradeIsRejected`. `DriveIsLockedForTheVoyage` checks that a drive change cannot affect a voyage already under way.
- Galaxy and start design: `GalaxyIsReproducibleAndSized`, `OtherSeedsGeneratePlayableGalaxies` (×3), `TradeCircuitExistsOreToAlloysToMachineryToFood`, `StartOffersTwoAffordableFeasibleJobsWithDifferentTradeoffs` and `DemoShowsANetworkInMotion`.

### Acceptance item 3: saves and catch-up comparisons

| Requirement | Test |
|---|---|
| Running save made mid-transit; save round trip | `SaveRoundTripPreservesEverything` (seed, clock, speed, pause, markets, reservations, credits, ships, provisions, in-flight progress and routes) |
| Offline catch-up vs debug advancement vs ordinary simulation over the same game time | `OfflineCatchUpEqualsOrdinaryAndDebugAdvancement`. It saves mid-transit at 5x, then compares a full-state fingerprint after three paths: offline catch-up from the save, ordinary frame-by-frame advancement, and debug skips. The fingerprint covers balances, fleet positions and status, port stock and reservations, and route state. |
| Paused save | `PausedSaveStaysPaused` |
| Backward system-clock change | `BackwardClockChangeDoesNotReverseOrRegrantTime` |
| Reopening a settled save | `ReopeningASettledSaveNeverPaysTwice` |
| Atomic write and recovery | `CorruptMainSaveFallsBackToBackup`, `NewerSaveVersionIsRejected` |
| Separate fresh and demo saves | `FreshAndDemoSavesUseSeparateSlots` |

All save tests use a temporary directory and an injected wall clock and monotonic clock. They do not use Unity's `persistentDataPath`.

## 2. Static checks of the Unity side (not a Unity compile)

These are type checks only. They show that the code is consistent with the Unity API surface, not that Unity 6000.6.10f1 accepts it.

- `Assets/SpaceTrade/Game/**` and `Assets/SpaceTrade/Simulation/**` were compiled together with `DefineConstants=UNITY_EDITOR;DEVELOPMENT_BUILD`, against NuGet `UnityEngine.Modules` 2021.3.33 reference assemblies (netstandard2.1, C# 9). Result: 0 errors, 0 warnings.
- `Assets/Editor/BuildScript.cs` was compiled against NuGet `Unity3D.SDK` 2021.1.14.1, which supplies the UnityEngine and UnityEditor references. Result: 0 errors.
- The code avoids APIs that were removed or deprecated in Unity 6.x:
  - no `Object.GetInstanceID`
  - no `FindObjectOfType`
  - no legacy `UnityEngine.Input`; all input comes from IMGUI `Event.current`
  - no URP
- Every file and folder under `Assets/` has a `.meta` file: 39 assets, 39 metas, no duplicate GUIDs. The startup scene references `GameBootstrap` by its script GUID, and `EditorBuildSettings.asset` lists `Assets/Scenes/Main.unity` with the scene's GUID.
- `ProjectSettings/*.asset` was adapted from a working Unity 6000.6.3f1 project. Changes:
  - new product name, company and product GUID
  - built-in render pipeline, with the URP pipeline asset references removed from Graphics and Quality settings
  - Active Input Handling set to the Input System
  - Run In Background on, so time keeps flowing when the window is unfocused
  - a 1600×900 resizable window
- `tools/build-windows.ps1` was **not** executed or parsed: PowerShell is not installed on the VM.

## 3. Not verified: needs a Windows PC with Unity 6000.6.10f1

1. **Editor import.**
   - The project opens without errors or safe-mode prompts.
   - The packages resolve: `com.unity.test-framework` 1.8.0 and `com.unity.inputsystem` 1.20.1.
   - All four assemblies compile: `SpaceTrade.Simulation`, `SpaceTrade.Game`, `SpaceTrade.EditorTools` and `SpaceTrade.Tests.Editor`.
   - `Main.unity` loads with `GameBootstrap` attached and no "missing script".
2. **Unity edit-mode tests.** `-runTests -testPlatform EditMode` should report the same 53 passes.
3. **Windows x64 builds.**
   - Both `BuildWindowsRelease` and `BuildWindowsDevelopment` succeed.
   - `tools/build-windows.ps1` produces `Builds/SpaceTrade-Windows-x64.zip`.
   - The unzipped player launches on a clean folder.
4. **Acceptance item 1, a manual playthrough of a fresh game in the player.**
   - Quote, provision, launch, follow the vessel, and inspect its delivery receipt.
   - Buy a second ship at a yard and confirm the two ships travel independently.
   - Install a drive at a local yard.
   - Confirm a remote upgrade is refused, with the reason shown.
5. **Acceptance item 3 in the player.** Close and reopen a running save and a paused save. Check the return summary. Reopen again to confirm nothing is paid twice.
6. **Acceptance item 4, the real-time timing test in the normal (release) Windows build.** Use the template below.
7. **Acceptance item 5, deliverable.** No zipped Windows player was produced, because there was no Unity editor or license on the VM. The zip must be built on Windows with `tools/build-windows.ps1`.
8. **Visual and UX review.** Check readability at 1080p and 4K (IMGUI scales with screen height), the close-up view, and how the map looks with the demo network.

## 4. Template for the item 4 timing test (release build)

Setup: start a fresh campaign, either by deleting `campaign.sts.json` or with Menu > New campaign > Standard galaxy. The courier **Wayfarer** is docked at **Brightwater** with an Alcubierre Mk I (1.5c). **Vulcan Reach** is exactly 4.5 ly away, so the voyage takes 3.000 game years, which is 3 h at 1x. Plan the voyage, provision it and launch at 1x. Keep the window focused and do not touch the speed controls. Time with an external stopwatch.

| Step | Expected | Tolerance | Observed | Real time elapsed |
|---|---|---|---|---|
| Launch at 1x: ETA | Arrival date = departure + 3.000 yr; real ETA 3h 00m 00s | ±1 s | | 0:00 |
| After 6:00 at 1x: progress | 3.33 % (0.15 ly); real ETA 2h 54m 00s | ±0.02 % / ±2 s (the display resolves 0.01 %, about 1.1 s at 1x) | | 6:00 |
| Pause for 1:00 | Progress and real ETA unchanged | no change | | 7:00 |
| Resume, then switch to 5x | Real ETA = 1x value ÷ 5 = 34m 48s; arrival date unchanged | ±1 s | | |
| Switch to 3x | Real ETA = 1x value ÷ 3 = 58m 00s; arrival date unchanged | ±1 s | | |
| Release build has no debug toggle or "Skip to next arrival" | absent | n/a | | |

The ship panel shows progress as a percentage and as ly covered ("0.15 of 4.50 ly"), along with the game arrival date and the remaining real time. The close-up view (`C`) shows the same readout.

Record: build type (release), Windows version, monitor refresh rate, and whether vsync was on. The clock is independent of frame rate, so the refresh rate should not matter.

## 5. Risks expected on first import

- **Package versions.** The pinned versions are believed to match the 6.6 editor's built-in package set, but they were not resolved against a 6000.6.10f1 editor. If Package Manager reports a version error, accept the version the editor offers.
- **ProjectSettings drift.** The settings came from 6000.6.3f1. 6000.6.10f1 may upgrade or add fields silently on first open, which is expected. Unity also generates settings files that are missing, such as `PackageManagerSettings`, `NavMeshAreas` and `PresetManager`.
- **Input System prompt.** Active Input Handling is set to Input System only. If the editor asks to restart to enable the new backend, accept. The game does not depend on either backend.
- **Hand-written scene YAML.** `Main.unity` is minimal. If it fails to load, create a new empty scene with one GameObject that has `GameBootstrap`, save it over `Assets/Scenes/Main.unity`, and keep the existing `.meta`. As a fallback, `GameBootstrap` also creates itself after any scene load if it is missing.
- **API drift between 2021.3 and 6.6.** Some calls passed the type check against older reference assemblies but may raise obsolete-API warnings, or rarely errors, in 6.6. Check the Console after the first compile.
- **Fonts.** The IMGUI default font may lack a glyph, such as `→`, `·` or `…`. A missing glyph would render as a box, which is cosmetic only.
