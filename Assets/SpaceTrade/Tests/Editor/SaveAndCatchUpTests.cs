using System;
using System.IO;
using NUnit.Framework;
using SpaceTrade.Sim;

namespace SpaceTrade.Tests
{
    public class SaveAndCatchUpTests
    {
        /// <summary>A small network: starter on a manual voyage, a second courier on an automated food shuttle.</summary>
        private static Simulation Network(bool debugAllowed = false)
        {
            var sim = TestWorld.Fresh(debugAllowed);
            sim.State.Credits = 60000;
            var a = TestWorld.Starter(sim);
            var b = sim.NewShip("courier", "alc-150", TestWorld.Start(sim));
            TestWorld.AssertOk(sim.Dispatch(new VoyagePlan(a.Id, TestWorld.OtherFoodImporter(sim)).WithFreight("food", 25).WithPassengers(2), true));
            var r = new RouteOrder { Name = "Shuttle" };
            r.Stops.Add(new RouteStop { SystemId = TestWorld.Start(sim), Commodities = { "food" }, Passengers = true, MinLoad = 30 });
            r.Stops.Add(new RouteStop { SystemId = TestWorld.Mine(sim), Passengers = true });
            TestWorld.AssertOk(sim.SetRoute(b.Id, r));
            sim.AdvanceBy(GameTime.YearsToTicks(0.37)); // mid-transit
            return sim;
        }

        [Test]
        public void SaveRoundTripPreservesEverything()
        {
            var sim = Network();
            string dir = TestWorld.TempDir();
            var store = new SaveStore(dir);
            store.Save(SaveStore.MainSlot, sim.State);
            var loaded = store.Load(SaveStore.MainSlot);
            Assert.AreEqual(SaveSerializer.ToJson(sim.State), SaveSerializer.ToJson(loaded));
            Assert.AreEqual(sim.State.Seed, loaded.Seed);
            Assert.AreEqual(sim.Galaxy.Systems.Count, loaded.Galaxy.Systems.Count);
            Assert.IsFalse(File.Exists(store.PathFor(SaveStore.MainSlot) + ".tmp"), "atomic write leaves no temp file");
            Directory.Delete(dir, true);
        }

        [Test]
        public void OfflineCatchUpEqualsOrdinaryAndDebugAdvancement()
        {
            var realAway = TimeSpan.FromHours(2) + TimeSpan.FromMinutes(17) + TimeSpan.FromSeconds(41);

            // Saved mid-transit at 5x.
            var original = Network();
            original.State.Speed = 5;
            var stampClock = new ManualClock(TestWorld.Epoch);
            new SimulationSession(original, stampClock, stampClock).StampForSave();
            string saveJson = SaveSerializer.ToJson(original.State);

            // 1. Offline catch-up after reopening.
            var offline = new Simulation(SaveSerializer.FromJson(saveJson));
            var reopenClock = new ManualClock(TestWorld.Epoch + realAway);
            var report = new SimulationSession(offline, reopenClock, reopenClock).CatchUpOffline();
            long target = offline.State.Now;
            Assert.AreEqual(SaveSerializer.FromJson(saveJson).Now + realAway.Ticks * 5 / 10000, target);
            Assert.Greater(report.Deliveries.Count, 0, "deliveries happened while away");

            // 2. Ordinary play: the same real time delivered as jittery frames.
            var ordinary = new Simulation(SaveSerializer.FromJson(saveJson));
            var frameClock = new ManualClock(TestWorld.Epoch);
            var session = new SimulationSession(ordinary, frameClock, frameClock);
            var rng = new DeterministicRng(99);
            while (frameClock.ElapsedTicks100ns < realAway.Ticks)
            {
                long step = Math.Min(realAway.Ticks - frameClock.ElapsedTicks100ns, 50000 + (long)(rng.NextDouble() * 400000));
                frameClock.AdvanceReal(TimeSpan.FromTicks(step));
                session.Update();
            }
            Assert.AreEqual(target, ordinary.State.Now);

            // 3. Debug advancement: skip arrival to arrival, then finish to the same game time.
            var debug = new Simulation(SaveSerializer.FromJson(saveJson), debugAllowed: true) { DebugEnabled = true };
            int skips = 0;
            while (debug.NextArrivalTick().HasValue && debug.NextArrivalTick().Value <= target) { TestWorld.AssertOk(debug.SkipToNextArrival()); skips++; }
            debug.AdvanceTo(target);
            Assert.Greater(skips, 0);

            string expect = TestWorld.Fingerprint(offline.State);
            Assert.AreEqual(expect, TestWorld.Fingerprint(ordinary.State), "ordinary vs offline");
            Assert.AreEqual(expect, TestWorld.Fingerprint(debug.State), "debug vs offline");
            Assert.AreEqual(offline.State.Credits, ordinary.State.Credits);
            for (int i = 0; i < offline.State.Ships.Count; i++)
            {
                var p1 = offline.ShipPosition(offline.State.Ships[i]);
                var p2 = ordinary.ShipPosition(ordinary.State.Ships[i]);
                Assert.AreEqual(p1.X, p2.X, 1e-12);
                Assert.AreEqual(p1.Y, p2.Y, 1e-12);
            }
        }

        [Test]
        public void PausedSaveStaysPaused()
        {
            var sim = Network();
            sim.State.Paused = true;
            var c = new ManualClock(TestWorld.Epoch);
            new SimulationSession(sim, c, c).StampForSave();
            string json = SaveSerializer.ToJson(sim.State);
            var loaded = new Simulation(SaveSerializer.FromJson(json));
            var later = new ManualClock(TestWorld.Epoch + TimeSpan.FromDays(3));
            var report = new SimulationSession(loaded, later, later).CatchUpOffline();
            Assert.IsTrue(report.WasPaused);
            Assert.IsTrue(loaded.State.Paused);
            Assert.AreEqual(sim.State.Now, loaded.State.Now);
            Assert.AreEqual(sim.State.Credits, loaded.State.Credits);
        }

        [Test]
        public void BackwardClockChangeDoesNotReverseOrRegrantTime()
        {
            var sim = Network();
            var c = new ManualClock(TestWorld.Epoch);
            new SimulationSession(sim, c, c).StampForSave();
            long now0 = sim.State.Now;
            long stamp0 = sim.State.LastWallClockUtcTicks;

            var back = new ManualClock(TestWorld.Epoch - TimeSpan.FromHours(5));
            var report = new SimulationSession(sim, back, back).CatchUpOffline();
            Assert.IsTrue(report.ClockWentBackward);
            Assert.AreEqual(now0, sim.State.Now);
            Assert.AreEqual(stamp0, sim.State.LastWallClockUtcTicks, "stamp never moves backward");

            // When the clock returns to normal, only genuinely new time is applied.
            var fwd = new ManualClock(TestWorld.Epoch + TimeSpan.FromMinutes(30));
            new SimulationSession(sim, fwd, fwd).CatchUpOffline();
            Assert.AreEqual(now0 + TimeSpan.FromMinutes(30).Ticks * sim.State.Speed / 10000, sim.State.Now);
        }

        [Test]
        public void ReopeningASettledSaveNeverPaysTwice()
        {
            var sim = Network();
            string dir = TestWorld.TempDir();
            var store = new SaveStore(dir);
            var c = new ManualClock(TestWorld.Epoch);
            new SimulationSession(sim, c, c).StampForSave();
            store.Save(SaveStore.MainSlot, sim.State);

            // Reopen 5 real hours later: catch-up settles voyages, then the result and its stamp are saved together.
            var reopen = new ManualClock(TestWorld.Epoch + TimeSpan.FromHours(5));
            var first = new Simulation(store.Load(SaveStore.MainSlot));
            var rep = new SimulationSession(first, reopen, reopen).CatchUpOffline();
            Assert.Greater(rep.Deliveries.Count, 0);
            store.Save(SaveStore.MainSlot, first.State);
            long credits = first.State.Credits;
            int receipts = first.State.Receipts.Count;

            // Load the same file twice at the same wall time: no further payout.
            for (int i = 0; i < 2; i++)
            {
                var again = new Simulation(store.Load(SaveStore.MainSlot));
                var rep2 = new SimulationSession(again, reopen, reopen).CatchUpOffline();
                Assert.AreEqual(0, rep2.Deliveries.Count);
                Assert.AreEqual(credits, again.State.Credits);
                Assert.AreEqual(receipts, again.State.Receipts.Count);
                store.Save(SaveStore.MainSlot, again.State);
            }

            // A crash before the post-catch-up save is also safe: the old file never contained the payout.
            Directory.Delete(dir, true);
        }

        [Test]
        public void SettledVoyageIdsBlockDuplicateSettlement()
        {
            var sim = TestWorld.Fresh();
            var ship = TestWorld.Starter(sim);
            var v = sim.Dispatch(new VoyagePlan(ship.Id, TestWorld.Mine(sim)).WithFreight("food", 20), true).Voyage;
            sim.State.SettledVoyages.Add(v.Id); // e.g. a tampered save that still lists the voyage in flight
            long credits = sim.State.Credits;
            sim.AdvanceTo(v.ArriveTick);
            Assert.AreEqual(credits, sim.State.Credits);
            Assert.IsTrue(ship.Docked);
        }

        [Test]
        public void CorruptMainSaveFallsBackToBackup()
        {
            var sim = Network();
            string dir = TestWorld.TempDir();
            var store = new SaveStore(dir);
            store.Save(SaveStore.MainSlot, sim.State);
            sim.AdvanceBy(1000);
            store.Save(SaveStore.MainSlot, sim.State); // previous file becomes .bak
            File.WriteAllText(store.PathFor(SaveStore.MainSlot), "{ truncated");
            var loaded = store.Load(SaveStore.MainSlot);
            Assert.AreEqual(sim.State.Now - 1000, loaded.Now);
            Directory.Delete(dir, true);
        }

        [Test]
        public void NewerSaveVersionIsRejected()
        {
            var json = SaveSerializer.ToJson(TestWorld.Fresh().State).Replace("\"version\": 1,", "\"version\": 99,");
            Assert.Throws<FormatException>(() => SaveSerializer.FromJson(json));
        }

        [Test]
        public void FreshAndDemoSavesUseSeparateSlots()
        {
            string dir = TestWorld.TempDir();
            var store = new SaveStore(dir);
            var fresh = Scenarios.NewGame(Scenarios.DefaultSeed, TestWorld.Epoch);
            var demo = Scenarios.Demo(Scenarios.DefaultSeed, TestWorld.Epoch);
            store.Save(SaveStore.MainSlot, fresh);
            store.Save(SaveStore.DemoSlot, demo);
            Assert.AreNotEqual(store.PathFor(SaveStore.MainSlot), store.PathFor(SaveStore.DemoSlot));
            var f = store.Load(SaveStore.MainSlot);
            Assert.AreEqual(1, f.Ships.Count);
            Assert.AreEqual(0, f.Now);
            Assert.AreEqual(Catalog.StartingCredits, f.Credits);
            Assert.IsFalse(f.IsDemo);
            Assert.IsTrue(store.Load(SaveStore.DemoSlot).IsDemo);
            Directory.Delete(dir, true);
        }
    }

    public class GalaxyAndScenarioTests
    {
        [Test]
        public void GalaxyIsReproducibleAndSized()
        {
            var a = Galaxy.Generate(Scenarios.DefaultSeed);
            var b = Galaxy.Generate(Scenarios.DefaultSeed);
            Assert.That(a.Systems.Count, Is.InRange(8, 12));
            for (int i = 0; i < a.Systems.Count; i++)
            {
                Assert.AreEqual(a.Systems[i].Name, b.Systems[i].Name);
                Assert.AreEqual(a.Systems[i].X, b.Systems[i].X);
                Assert.AreEqual(a.Systems[i].Y, b.Systems[i].Y);
            }
            Assert.AreEqual(4.5, a.Distance(a.StartSystemId, a.TimingTestPartnerId), 1e-9);
        }

        [TestCase(1UL)]
        [TestCase(42UL)]
        [TestCase(123456789UL)]
        public void OtherSeedsGeneratePlayableGalaxies(ulong seed)
        {
            var sim = new Simulation(Scenarios.NewGame(seed, TestWorld.Epoch));
            Assert.That(sim.Galaxy.Systems.Count, Is.InRange(8, 12));
            Assert.IsTrue(sim.CanAffordAnyViableJob(sim.State.Ships[0]));
        }

        [Test]
        public void TradeCircuitExistsOreToAlloysToMachineryToFood()
        {
            var g = Galaxy.Generate(Scenarios.DefaultSeed);
            Assert.IsTrue(g.Systems.Exists(s => s.Role == Galaxy.Mining));
            Assert.IsTrue(g.Systems.Exists(s => s.Role == Galaxy.Refinery));
            Assert.Contains("ore", Galaxy.Refinery.Imports);
            Assert.Contains("machinery", Galaxy.Agricultural.Imports);
            Assert.Contains("food", Galaxy.Mining.Imports);
            Assert.Contains("machinery", Galaxy.Industrial.Exports);
        }

        [Test]
        public void StartOffersTwoAffordableFeasibleJobsWithDifferentTradeoffs()
        {
            var sim = TestWorld.Fresh();
            var ship = TestWorld.Starter(sim);
            var near = sim.Quote(new VoyagePlan(ship.Id, TestWorld.Mine(sim)).WithFreight("food", 40).WithPassengers(2));
            var far = sim.Quote(new VoyagePlan(ship.Id, TestWorld.OtherFoodImporter(sim)).WithFreight("food", 30).WithPassengers(4));
            Assert.IsTrue(near.CanLaunchWithProvisioning, string.Join(" ", near.Problems.ToArray()) + string.Join(" ", near.Notes.ToArray()));
            Assert.IsTrue(far.CanLaunchWithProvisioning, string.Join(" ", far.Problems.ToArray()) + string.Join(" ", far.Notes.ToArray()));
            Assert.Greater(near.NetProfit, 0);
            Assert.Greater(far.NetProfit, 0);
            Assert.AreNotEqual(near.DurationTicks, far.DurationTicks);
            Assert.AreNotEqual(Math.Sign(near.NetProfit - far.NetProfit), Math.Sign(near.ProfitPerYear - far.ProfitPerYear),
                "one job pays more in total, the other pays more per year");
        }

        [Test]
        public void DemoShowsANetworkInMotion()
        {
            var demo = new Simulation(Scenarios.Demo(Scenarios.DefaultSeed, TestWorld.Epoch));
            int moving = 0, routed = 0;
            foreach (var s in demo.State.Ships) { if (!s.Docked) moving++; if (s.Route != null) routed++; }
            Assert.GreaterOrEqual(demo.State.Ships.Count, 5);
            Assert.GreaterOrEqual(moving, 3);
            Assert.GreaterOrEqual(routed, 3);
            demo.AdvanceBy(GameTime.YearsToTicks(20));
            TestWorld.AssertPoolsSane(demo.State);
            Assert.Greater(demo.State.Receipts.Count, 10);
        }
    }
}
