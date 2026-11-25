using System;
using NUnit.Framework;
using SpaceTrade.Sim;

namespace SpaceTrade.Tests
{
    public class ClockAndDriveTests
    {
        // Spec timing table: distance, drive, game years, real seconds at 1x / 3x / 5x.
        [TestCase(4.5, 1.5, 3.0, 10800.0, 3600.0, 2160.0)]
        [TestCase(4.5, 3.75, 1.2, 4320.0, 1440.0, 864.0)]
        [TestCase(4.5, 2.25, 2.0, 7200.0, 2400.0, 1440.0)]
        public void AlcubierreTransitMatchesSpecTable(double ly, double c, double years, double real1, double real3, double real5)
        {
            Assert.AreEqual(years, GameTime.TransitYears(ly, c), 1e-12);
            long ticks = GameTime.TransitTicks(ly, c);
            Assert.AreEqual(GameTime.YearsToTicks(years), ticks);
            Assert.AreEqual(real1, GameTime.RealSecondsFor(ticks, 1), 1e-9);
            Assert.AreEqual(real3, GameTime.RealSecondsFor(ticks, 3), 1e-9);
            Assert.AreEqual(real5, GameTime.RealSecondsFor(ticks, 5), 1e-9);
        }

        [Test]
        public void OneRealHourAt1xIsExactlyOneGameYear()
        {
            var sim = TestWorld.Fresh();
            var clock = new ManualClock(TestWorld.Epoch);
            var session = new SimulationSession(sim, clock, clock);
            long t0 = sim.State.Now;
            for (int i = 0; i < 3600; i++) { clock.AdvanceReal(TimeSpan.FromSeconds(1)); session.Update(); }
            Assert.AreEqual(GameTime.TicksPerYear, sim.State.Now - t0);
        }

        [TestCase(3)]
        [TestCase(5)]
        public void SpeedMultiplierAppliedOnce(int speed)
        {
            var sim = TestWorld.Fresh();
            var clock = new ManualClock(TestWorld.Epoch);
            var session = new SimulationSession(sim, clock, clock);
            session.SetSpeed(speed);
            clock.AdvanceReal(TimeSpan.FromMinutes(12));
            session.Update();
            Assert.AreEqual(GameTime.YearsToTicks(0.2 * speed), sim.State.Now);
        }

        [Test]
        public void OnlyThreeSpeedsAreAccepted()
        {
            var sim = TestWorld.Fresh();
            Assert.IsTrue(sim.SetSpeed(1).Ok);
            Assert.IsTrue(sim.SetSpeed(3).Ok);
            Assert.IsTrue(sim.SetSpeed(5).Ok);
            Assert.IsFalse(sim.SetSpeed(2).Ok);
            Assert.IsFalse(sim.SetSpeed(10).Ok);
            Assert.AreEqual(5, sim.State.Speed);
        }

        [Test]
        public void FrameRateDoesNotChangeEventTiming()
        {
            var a = TestWorld.Fresh();
            var b = TestWorld.Fresh();
            foreach (var sim in new[] { a, b })
                TestWorld.AssertOk(sim.Dispatch(new VoyagePlan(1, TestWorld.Mine(sim)).WithFreight("food", 30), true));
            var ca = new ManualClock(TestWorld.Epoch);
            var cb = new ManualClock(TestWorld.Epoch);
            var sa = new SimulationSession(a, ca, ca);
            var sb = new SimulationSession(b, cb, cb);
            sa.SetSpeed(5); sb.SetSpeed(5);
            var rng = new DeterministicRng(5);
            long total = TimeSpan.FromMinutes(40).Ticks;
            for (long t = 0; t < total;) { long step = 1000 + (long)(rng.NextDouble() * 900000); ca.AdvanceReal(TimeSpan.FromTicks(step)); sa.Update(); t += step; }
            cb.AdvanceReal(TimeSpan.FromTicks(ca.ElapsedTicks100ns));
            sb.Update();
            Assert.AreEqual(a.State.Now, b.State.Now);
            Assert.AreEqual(TestWorld.Fingerprint(a.State), TestWorld.Fingerprint(b.State));
        }

        /// <summary>Simulated version of acceptance item 4 (the real-time check itself must run in the Windows player).</summary>
        [Test]
        public void TimingScenario_SixMinutesPauseThenFiveX()
        {
            var sim = TestWorld.Fresh();
            var clock = new ManualClock(TestWorld.Epoch);
            var session = new SimulationSession(sim, clock, clock);
            var ship = TestWorld.Starter(sim);
            int mine = TestWorld.Mine(sim);
            Assert.AreEqual(4.5, sim.Galaxy.Distance(TestWorld.Start(sim), mine), 1e-9);
            Assert.AreEqual(1.5, ship.Drive.RatingC);
            TestWorld.AssertOk(sim.Dispatch(new VoyagePlan(ship.Id, mine).WithFreight("food", 20), true));
            long arrival = ship.Voyage.ArriveTick;
            Assert.AreEqual(10800000L, arrival - ship.Voyage.DepartTick);
            Assert.AreEqual(10800.0, sim.RealSecondsToArrival(ship), 1e-9);

            clock.AdvanceReal(TimeSpan.FromMinutes(6));
            session.Update();
            Assert.AreEqual(1.0 / 30.0, ship.Voyage.Progress(sim.State.Now), 1e-9);
            var pos = sim.ShipPosition(ship);
            var start = sim.Galaxy.Get(TestWorld.Start(sim));
            double travelled = Math.Sqrt(Math.Pow(pos.X - start.X, 2) + Math.Pow(pos.Y - start.Y, 2));
            Assert.AreEqual(0.15, travelled, 1e-9);
            double eta1x = sim.RealSecondsToArrival(ship);
            Assert.AreEqual(10440.0, eta1x, 1e-6);

            session.SetPaused(true);
            long frozen = sim.State.Now;
            clock.AdvanceReal(TimeSpan.FromMinutes(1));
            session.Update();
            Assert.AreEqual(frozen, sim.State.Now, "no progress while paused");
            Assert.AreEqual(eta1x, sim.RealSecondsToArrival(ship), 1e-9, "countdown frozen while paused");

            session.SetSpeed(5);
            session.SetPaused(false);
            Assert.AreEqual(eta1x / 5.0, sim.RealSecondsToArrival(ship), 1e-6);
            Assert.AreEqual(arrival, ship.Voyage.ArriveTick, "scheduled arrival date unchanged by speed");
            session.SetSpeed(3);
            Assert.AreEqual(eta1x / 3.0, sim.RealSecondsToArrival(ship), 1e-6);
            Assert.AreEqual(arrival, ship.Voyage.ArriveTick);
        }

        [Test]
        public void SpeedChangeKeepsManifestSuppliesAndPayout()
        {
            var sim = TestWorld.Fresh();
            var clock = new ManualClock(TestWorld.Epoch);
            var session = new SimulationSession(sim, clock, clock);
            var ship = TestWorld.Starter(sim);
            TestWorld.AssertOk(sim.Dispatch(new VoyagePlan(ship.Id, TestWorld.Mine(sim)).WithFreight("food", 25).WithPassengers(2), true));
            var v = ship.Voyage;
            long gross = v.FreightRevenue + v.FareRevenue, supplies = v.SuppliesCost, arrive = v.ArriveTick;
            clock.AdvanceReal(TimeSpan.FromMinutes(10)); session.Update();
            session.SetSpeed(5);
            clock.AdvanceReal(TimeSpan.FromMinutes(10)); session.Update();
            session.SetSpeed(1);
            Assert.AreEqual(arrive, v.ArriveTick);
            Assert.AreEqual(gross, v.FreightRevenue + v.FareRevenue);
            Assert.AreEqual(supplies, v.SuppliesCost);
            Assert.AreEqual(25, v.FreightTonnes);
        }

        [Test]
        public void DebugSkipIsRejectedUnlessAllowedAndEnabled()
        {
            var release = TestWorld.Fresh(debugAllowed: false);
            TestWorld.AssertOk(release.Dispatch(new VoyagePlan(1, TestWorld.Mine(release)).WithFreight("food", 10), true));
            release.DebugEnabled = true; // a release host cannot turn it on
            Assert.IsFalse(release.DebugEnabled);
            Assert.IsFalse(release.CanSkipToNextArrival);
            long before = release.State.Now;
            Assert.IsFalse(release.SkipToNextArrival().Ok);
            Assert.AreEqual(before, release.State.Now);

            var dev = TestWorld.Fresh(debugAllowed: true);
            Assert.IsFalse(dev.SkipToNextArrival().Ok, "must be explicitly enabled");
            dev.DebugEnabled = true;
            Assert.IsFalse(dev.CanSkipToNextArrival, "disabled when nothing is in flight");
            Assert.IsFalse(dev.SkipToNextArrival().Ok);
            TestWorld.AssertOk(dev.Dispatch(new VoyagePlan(1, TestWorld.Mine(dev)).WithFreight("food", 10), true));
            Assert.IsTrue(dev.CanSkipToNextArrival);
            long arrival = dev.State.Ships[0].Voyage.ArriveTick;
            TestWorld.AssertOk(dev.SkipToNextArrival());
            Assert.AreEqual(arrival, dev.State.Now);
            Assert.IsTrue(dev.State.Ships[0].Docked);
        }

        [Test]
        public void DebugSkipProcessesEveryArrivalAtThatTimestamp()
        {
            var sim = TestWorld.Fresh(debugAllowed: true);
            sim.DebugEnabled = true;
            var second = sim.NewShip("courier", "alc-150", TestWorld.Start(sim));
            int mine = TestWorld.Mine(sim);
            TestWorld.AssertOk(sim.Dispatch(new VoyagePlan(1, mine).WithFreight("food", 20), true));
            TestWorld.AssertOk(sim.Dispatch(new VoyagePlan(second.Id, mine).WithFreight("food", 20), true));
            Assert.AreEqual(sim.State.Ships[0].Voyage.ArriveTick, second.Voyage.ArriveTick);
            TestWorld.AssertOk(sim.SkipToNextArrival());
            Assert.IsTrue(sim.State.Ships[0].Docked);
            Assert.IsTrue(second.Docked);
            Assert.AreEqual(2, sim.State.Receipts.Count);
            Assert.AreEqual(1, sim.State.Receipts[0].ShipId, "simultaneous arrivals settle in ship-id order");
        }

        [Test]
        public void GameTimeNeverRunsBackward()
        {
            var sim = TestWorld.Fresh();
            sim.AdvanceTo(5000);
            sim.AdvanceTo(100);
            Assert.AreEqual(5000, sim.State.Now);
        }
    }
}
