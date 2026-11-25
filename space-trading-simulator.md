# Build a space trading simulator in Unity and C#

Build and deliver a playable single-player Windows game about running an interstellar transport company. The player starts with one docked ship and enough money to provision a first voyage. They choose what to carry, commit the ship to a long transit, collect payment at the destination, and reinvest in ships and drives. As the company grows, the player's main task becomes designing and supervising a network of recurring passenger and freight services.

Use Unity for the 2D map, camera, ship views, and interface. Write the simulation and gameplay in C#. The game runs locally. Make routine implementation and visual decisions yourself. Work through implementation, playtesting, fixes, and packaging.

## Design the galaxy around trade

Generate a reproducible galaxy of roughly 8–12 named star systems with positions measured in light-years. Give each system persistent industries, exports, imports, and passenger connections. For example, a mining colony supplies ore to a refinery, the refinery ships machinery to an agricultural system, and the agricultural system ships food to the colony. Give the player enough information to discover and evaluate this circuit on the map.

Model finite freight supply, destination demand, and passenger bookings that replenish over game time. Publish available quantities, destinations, and rates at each port. Pay a contracted rate per tonne or passenger delivered. Distinguish customers' freight from supplies bought for shipboard consumption. Reserve both freight and the corresponding delivery demand, or passenger seats booked, when dispatch is confirmed. A failed dispatch consumes nothing. If two ships compete for 80 tonnes, their combined reservations must never exceed 80 tonnes.

Start the player at a port with at least two affordable, feasible jobs offering different tradeoffs. Let the player inspect destinations and likely return loads before committing. Preserve the normal distances and travel rules from the first departure. Long transits are an intentional part of the game.

## Give the player a fleet-wide view

Open on the galaxy map with the starter ship selected. Support panning, zooming, selecting a system, selecting a moving ship, and following it as it travels. Show active routes and ships at their actual positions along each leg. Zoom in to a close-up of the selected vessel that preserves its identity and motion. Display its manifest, crew and passengers, remaining supplies, installed drive, destination, progress, and ETA.

A system view shows industries, local jobs, prices, docked ships, provisions, and shipyard facilities. A fleet list lets the player locate any ship and distinguish ships in transit, ships waiting for a load, idle ships, and ships blocked by a problem. Keep map inspection and route planning available while vessels travel and while the clock is paused.

## Use a single galaxy clock

At **1x, exactly one real hour equals one game year**. Provide pause/play and exactly three game speeds: **1x, 3x, and 5x**. Play resumes at the selected speed. A speed change affects every ship immediately. The voyage's in-game arrival date, manifest, required supplies, and payout stay fixed.

Every ship uses an Alcubierre drive with a rated speed such as 1.5c, 2.25c, or 3.75c. Divide distance in light-years by the drive rating in multiples of c to get game years in transit. Use these timing cases:

| Distance | Drive | Game years | Real time at 1x | At 3x | At 5x |
|---|---|---:|---:|---:|---:|
| 4.5 ly | 1.5c | 3 | 3 hours | 1 hour | 36 minutes |
| 4.5 ly | 3.75c | 1.2 | 72 minutes | 24 minutes | 14 minutes 24 seconds |

Show both the in-game arrival date and the remaining real time at the current speed. Freeze the countdown while paused. An arrival or blocked route generates a notification while other ships continue.

**Continue simulation while the game is closed.** On reopening a running save, apply elapsed real time at its saved speed. A paused save stays paused. Process deliveries, replenishment, automated departures, and subsequent arrivals in chronological order. A manually dispatched ship that arrives stays docked until ordered out again. Present a concise return summary of deliveries, earnings, and ships needing attention. Persist the catch-up result and its timestamp together so loading twice cannot award a second payout. Handle a backward system-clock change without reversing game time.

**Only debug mode gets “Skip to next arrival.”** Make that command available exclusively in the Unity Editor and Development Builds with debug mode explicitly enabled. Enforce the restriction in the simulation command handler and the UI. Skip to the earliest pending arrival, processing all intervening events and every arrival at that timestamp. Disable the button when nothing is in flight. The normal Windows player build must have no player-triggered time jump.

## Plan and settle a voyage

Let the player pick a destination, choose cargo and passengers, and review a quote before launch. Ships have a hold, a fuel tank, passenger berths, and crew. Freight, food, and water share hold space. Calculate food and water from the number of people aboard and voyage duration. Calculate fuel from distance, drive tier, and loaded mass. Use consistent units. Include the supplies' own mass when determining whether the load is feasible.

Show what is aboard, what is required, the cost to top up, and any shortfall. Offer one-click provisioning. Explain failures quantitatively, for example “Unload 4 tonnes to make room for drinking water.” Show gross revenue, expected operating costs, net profit, and profit per game year. Use the same C# calculations for the preview and actual departure.

Validate dispatch as one transaction. Lock the ship's manifest, bookings, drive, and destination for the voyage. At arrival, unload the contracted cargo and passengers and pay once for actual delivery. Show the receipt with fares, freight revenue, supplies consumed at their acquisition cost, elapsed time, and net profit. Count each expense once even when supplies were bought on a previous stop.

## Grow from one ship to a network

Give selected systems shipyards with local stock and prices. Include at least a small courier, a bulk freighter, and a passenger liner with distinct uses. Deliver a purchased vessel docked at its selling yard. A drive upgrade requires that specific ship to be docked at a yard stocking the upgrade. The player can manage a distant vessel from the map. The yard's location still determines which work it can perform. Preview the cost and time saved on a known route before purchase.

Let ships repeat ordered routes of two or more stops. At each stop configure eligible freight, passengers, automatic provisioning, and a minimum departure load measured in the relevant capacity units. Allow an explicitly empty repositioning leg. Estimate revenue, costs, total transit time, and profit per game year for the **whole circuit**, including empty legs. Label estimates that depend on future bookings or waiting time.

Apply manual dispatch rules to automated routes. Recheck waiting ships when supply or bookings replenish. Show the unmet condition, such as “Waiting for 12 more tonnes of ore.” If the next leg is infeasible, keep the ship docked, flag it, and let the player change its orders. Resolve simultaneous arrivals and competing reservations in a deterministic order.

Provide a way back from insolvency. When the player has a docked ship and cannot afford to provision any viable job, offer an emergency contract at that port. Supply the necessary provisions in kind against the voyage payment. Guarantee the leg is feasible and leaves a modest positive balance on delivery. Redeem supplies only for that departure so repeatedly accepting the job cannot generate money or inventory.

## Implementation and saves

Keep the authoritative simulation in a plain C# assembly that can run with an injected clock outside Unity scenes. Advance one shared simulation clock from unscaled elapsed time, applying the selected multiplier once. Use the same event processing for ordinary play, offline catch-up, and debug stepping. Keep event timing independent of rendering frame rate.

Store versioned local saves under Unity's persistent data path. Preserve the galaxy seed, clock, speed, pause state, market availability, reservations, credits, ships, provisions, in-flight progress, and route orders. Write saves atomically. Provide separate fresh-start and populated demonstration saves. The demo should showcase a network in motion without altering the pacing of a fresh game.

## Acceptance and delivery

1. Play a fresh game through the normal interface. Quote, provision, launch, follow a vessel, and inspect its delivery receipt. Verify two ships can travel independently, a local yard can install a drive, and a remote upgrade is rejected.
2. Run deterministic tests for drive math, capacity and provisioning, competing claims on 80 tonnes, failed dispatch rollback, exactly-once payouts, sustained automated circuits, minimum-load waiting, empty return costs, and emergency-contract recovery.
3. Test running and paused saves, including a save made mid-transit. Compare offline catch-up, debug advancement, and ordinary simulation over the same elapsed game time. Check balances, fleet positions, port stock, and route state. Reopen a settled save to check for duplicate payment.
4. Perform a **real-time timing test in the normal Windows build**. Launch a 4.5-light-year trip at 1.5c. Measure six uninterrupted real minutes at 1x. Expect about 3.33% of the voyage completed (0.15 ly). Pause for one real minute and verify no progress. Resume at 5x and verify the remaining real ETA falls to one fifth of the 1x value, with the same scheduled arrival date. Record observed values, elapsed times, and tolerance. Also verify 3x and the absence of debug skip in that build.
5. Deliver a zipped, runnable **standalone Windows x64 build** including the executable and its companion files, plus the editable Unity project with assets, metadata, packages, project settings, and startup scene. Include the exact Editor version, launch and build instructions, controls, save locations, and a verification report. Clearly identify tests actually run, which build they ran in, and anything the available environment prevented you from building or verifying.
