using System;
using NUnit.Framework;
using SpaceTrade.Sim;

namespace SpaceTrade.Tests
{
    public class DispatchTests
    {
        [Test]
        public void ProvisionsScaleWithPeopleAndDuration()
        {
            Assert.AreEqual(2 * 3 * Catalog.FoodPerPersonYear, Planner.FoodRequired(2, 3), 1e-9);
            Assert.AreEqual(8 * 3 * Catalog.WaterPerPersonYear, Planner.WaterRequired(8, 3), 1e-9);
            var mk1 = Catalog.Drive("alc-150");
            Assert.AreEqual(4.5 * 1.6 * 140 / 100.0, Planner.FuelRequired(4.5, mk1, 140), 0.01);
            Assert.Greater(Planner.FuelRequired(4.5, mk1, 300), Planner.FuelRequired(4.5, mk1, 140), "heavier loads burn more fuel");
            Assert.Greater(Planner.FuelRequired(4.5, Catalog.Drive("alc-375"), 140), Planner.FuelRequired(4.5, mk1, 140), "faster drive tiers burn more per ly");
        }

        [Test]
        public void QuoteIncludesSupplyMassInHoldAndFuel()
        {
            var sim = TestWorld.Fresh();
            var ship = TestWorld.Starter(sim);
            var q = sim.Quote(new VoyagePlan(ship.Id, TestWorld.Mine(sim)).WithFreight("food", 30).WithPassengers(4));
            Assert.IsTrue(q.LoadFeasible, string.Join(" ", q.Problems.ToArray()));
            Assert.AreEqual(6, q.People);
            Assert.AreEqual(3.0, q.DurationYears, 1e-9);
            Assert.AreEqual(Planner.FoodRequired(6, 3), q.Food.Required, 1e-9);
            Assert.AreEqual(30 + q.Food.AfterTopUp + q.Water.AfterTopUp, q.HoldUsed, 1e-9);
            double mass = ship.Hull.HullMassTonnes + q.HoldUsed + 4 * Catalog.PassengerMassTonnes;
            Assert.AreEqual(mass, q.LoadedMass, 1e-9);
            Assert.AreEqual(Planner.FuelRequired(4.5, ship.Drive, mass), q.Fuel.Required, 1e-9);
            Assert.IsTrue(q.NeedsProvisioning);
            Assert.AreEqual(q.Fuel.TopUpCost + q.Food.TopUpCost + q.Water.TopUpCost, q.TopUpCost);
            Assert.AreEqual(q.GrossRevenue - q.SuppliesCost - q.PortFee, q.NetProfit);
            Assert.AreEqual(q.NetProfit / 3.0, q.ProfitPerYear, 1e-9);
        }

        [Test]
        public void OverfullHoldExplainsTonnesToUnload()
        {
            var sim = TestWorld.Fresh();
            var ship = TestWorld.Starter(sim);
            var q = sim.Quote(new VoyagePlan(ship.Id, TestWorld.Mine(sim)).WithFreight("food", 58));
            Assert.IsFalse(q.LoadFeasible);
            double over = Math.Round(q.HoldUsed - ship.Hull.HoldTonnes, 1);
            StringAssert.Contains("unload " + Fmt.T(over) + " of freight to make room for", string.Join(" ", q.Problems.ToArray()));
            StringAssert.Contains("drinking water", q.Problems[0]);
            var fixedQ = sim.Quote(new VoyagePlan(ship.Id, TestWorld.Mine(sim)).WithFreight("food", 58 - (int)Math.Ceiling(over)));
            Assert.IsTrue(fixedQ.LoadFeasible, string.Join(" ", fixedQ.Problems.ToArray()));
        }

        [Test]
        public void OneClickProvisioningBuysExactlyTheShortfall()
        {
            var sim = TestWorld.Fresh();
            var ship = TestWorld.Starter(sim);
            var plan = new VoyagePlan(ship.Id, TestWorld.Mine(sim)).WithFreight("food", 20);
            var q = sim.Quote(plan);
            long credits = sim.State.Credits;
            TestWorld.AssertOk(sim.ProvisionFor(plan));
            Assert.AreEqual(credits - q.TopUpCost, sim.State.Credits);
            Assert.IsTrue(q.Fuel.TopUp > 0, "starter fuel is short for this leg");
            Assert.AreEqual(q.Fuel.AfterTopUp, ship.Supplies.Fuel.Quantity, 1e-9);
            Assert.AreEqual(q.Food.AfterTopUp, ship.Supplies.Food.Quantity, 1e-9);
            Assert.AreEqual(q.Water.AfterTopUp, ship.Supplies.Water.Quantity, 1e-9);
            var after = sim.Quote(plan);
            Assert.IsTrue(after.ReadyToLaunch);
            Assert.AreEqual(0, after.TopUpCost);
            Assert.AreEqual(q.SuppliesCost, after.SuppliesCost, "consumed cost is the acquisition cost either way");
        }

        [Test]
        public void PreviewMatchesActualDepartureAndReceipt()
        {
            var sim = TestWorld.Fresh();
            var ship = TestWorld.Starter(sim);
            var plan = new VoyagePlan(ship.Id, TestWorld.Mine(sim)).WithFreight("food", 30).WithPassengers(3);
            var q = sim.Quote(plan);
            long credits = sim.State.Credits;
            var res = sim.Dispatch(plan, autoProvision: true);
            TestWorld.AssertOk(res);
            var v = res.Voyage;
            Assert.AreEqual(q.ArrivalTick, v.ArriveTick);
            Assert.AreEqual(q.FreightRevenue, v.FreightRevenue);
            Assert.AreEqual(q.FareRevenue, v.FareRevenue);
            Assert.AreEqual(q.SuppliesCost, v.SuppliesCost);
            Assert.AreEqual(credits - q.TopUpCost, sim.State.Credits);
            sim.AdvanceTo(v.ArriveTick);
            var r = sim.LastReceiptFor(ship.Id);
            Assert.AreEqual(q.NetProfit, r.NetProfit);
            Assert.AreEqual(q.GrossRevenue, r.GrossRevenue);
            Assert.AreEqual(v.ArriveTick - v.DepartTick, r.ElapsedTicks);
            Assert.AreEqual(credits - q.TopUpCost + r.CashCredited, sim.State.Credits);
        }

        [Test]
        public void SuppliesBoughtEarlierAreExpensedOnceWhenConsumed()
        {
            var sim = TestWorld.Fresh();
            var ship = TestWorld.Starter(sim);
            long credits0 = sim.State.Credits;
            double basis0 = ship.Supplies.Fuel.CostBasis + ship.Supplies.Food.CostBasis + ship.Supplies.Water.CostBasis;
            // Over-buy at the start port, then fly two legs: costs must be recognised only as supplies are consumed.
            ship.Supplies.Fuel.Add(30, 30 * 100); ship.Supplies.Food.Add(5, 5 * 40); ship.Supplies.Water.Add(6, 6 * 10);
            sim.State.Credits -= 30 * 100 + 5 * 40 + 6 * 10;
            TestWorld.AssertOk(sim.Dispatch(new VoyagePlan(ship.Id, TestWorld.Mine(sim)).WithPassengers(1), true));
            sim.AdvanceTo(ship.Voyage.ArriveTick);
            TestWorld.AssertOk(sim.Dispatch(new VoyagePlan(ship.Id, TestWorld.Start(sim)).WithPassengers(1), true));
            sim.AdvanceTo(ship.Voyage.ArriveTick);
            long expensed = 0, gross = 0, fees = 0;
            foreach (var r in sim.State.Receipts) { expensed += r.SuppliesCost; gross += r.GrossRevenue; fees += r.PortFee; }
            double remainingBasis = ship.Supplies.Fuel.CostBasis + ship.Supplies.Food.CostBasis + ship.Supplies.Water.CostBasis;
            double totalSpentOnSupplies = (credits0 - sim.State.Credits) + gross - fees + basis0;
            // Every credit spent on supplies is either expensed on a receipt or still aboard as inventory, never both.
            Assert.AreEqual(totalSpentOnSupplies, expensed + remainingBasis, 2.0);
        }

        [Test]
        public void CompetingClaimsNeverExceedEightyTonnes()
        {
            var sim = TestWorld.Fresh();
            int start = TestWorld.Start(sim), mine = TestWorld.Mine(sim);
            sim.State.SupplyAt(start, "food").Available = 80;
            var a = TestWorld.Starter(sim);
            var b = sim.NewShip("courier", "alc-150", start);
            TestWorld.AssertOk(sim.Dispatch(new VoyagePlan(a.Id, mine).WithFreight("food", 50), true));
            var fail = sim.Dispatch(new VoyagePlan(b.Id, mine).WithFreight("food", 50), true);
            Assert.IsFalse(fail.Ok);
            StringAssert.Contains("Only 30 t of Foodstuffs", fail.Message);
            TestWorld.AssertOk(sim.Dispatch(new VoyagePlan(b.Id, mine).WithFreight("food", 30), true));
            Assert.AreEqual(0, sim.State.SupplyAt(start, "food").Available);
            Assert.AreEqual(80, a.Voyage.FreightTonnes + b.Voyage.FreightTonnes);
            Assert.AreEqual(80, sim.State.DemandAt(mine, "food").Reserved);
        }

        [Test]
        public void CompetingClaimsAcrossDestinationsShareOneSupplyPool()
        {
            var sim = TestWorld.Fresh();
            int start = TestWorld.Start(sim);
            sim.State.SupplyAt(start, "food").Available = 80;
            var a = TestWorld.Starter(sim);
            var b = sim.NewShip("courier", "alc-150", start);
            TestWorld.AssertOk(sim.Dispatch(new VoyagePlan(a.Id, TestWorld.Mine(sim)).WithFreight("food", 45), true));
            Assert.IsFalse(sim.Dispatch(new VoyagePlan(b.Id, TestWorld.OtherFoodImporter(sim)).WithFreight("food", 45), true).Ok);
            Assert.AreEqual(35, sim.State.SupplyAt(start, "food").Available);
        }

        [Test]
        public void FailedDispatchConsumesNothing()
        {
            var sim = TestWorld.Fresh();
            var ship = TestWorld.Starter(sim);
            int mine = TestWorld.Mine(sim);
            string before = TestWorld.Fingerprint(sim.State);

            // Valid freight line plus an invalid one: nothing may be reserved.
            Assert.IsFalse(sim.Dispatch(new VoyagePlan(ship.Id, mine).WithFreight("food", 20).WithFreight("ore", 5), true).Ok);
            Assert.AreEqual(before, TestWorld.Fingerprint(sim.State));

            // Too many passengers.
            Assert.IsFalse(sim.Dispatch(new VoyagePlan(ship.Id, mine).WithFreight("food", 20).WithPassengers(50), true).Ok);
            Assert.AreEqual(before, TestWorld.Fingerprint(sim.State));

            // Cannot afford provisions: nothing bought, nothing reserved.
            sim.State.Credits = 10;
            before = TestWorld.Fingerprint(sim.State);
            var r = sim.Dispatch(new VoyagePlan(ship.Id, mine).WithFreight("food", 20).WithPassengers(2), true);
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("Cannot afford", r.Message);
            Assert.AreEqual(before, TestWorld.Fingerprint(sim.State));

            // Short provisions without auto-provision.
            sim.State.Credits = 5000;
            before = TestWorld.Fingerprint(sim.State);
            Assert.IsFalse(sim.Dispatch(new VoyagePlan(ship.Id, mine).WithFreight("food", 20), false).Ok);
            Assert.AreEqual(before, TestWorld.Fingerprint(sim.State));
        }

        [Test]
        public void PayoutHappensExactlyOnce()
        {
            var sim = TestWorld.Fresh();
            var ship = TestWorld.Starter(sim);
            var v = sim.Dispatch(new VoyagePlan(ship.Id, TestWorld.Mine(sim)).WithFreight("food", 30).WithPassengers(2), true).Voyage;
            long afterDepart = sim.State.Credits;
            sim.AdvanceTo(v.ArriveTick - 1);
            Assert.AreEqual(afterDepart, sim.State.Credits);
            sim.AdvanceTo(v.ArriveTick);
            long paid = sim.State.Credits;
            var r = sim.LastReceiptFor(ship.Id);
            Assert.AreEqual(afterDepart + r.CashCredited, paid);
            sim.AdvanceTo(v.ArriveTick + GameTime.TicksPerYear * 5);
            Assert.AreEqual(paid, sim.State.Credits, "a manually dispatched ship stays docked and is never paid again");
            Assert.AreEqual(1, sim.State.Receipts.Count);
            Assert.IsTrue(ship.Docked);
            Assert.AreEqual(TestWorld.Mine(sim), ship.Location);
            Assert.AreEqual(0, sim.State.DemandAt(TestWorld.Mine(sim), "food").Reserved);
        }

        [Test]
        public void TwoShipsTravelIndependently()
        {
            var sim = TestWorld.Fresh();
            var a = TestWorld.Starter(sim);
            var b = sim.NewShip("courier", "alc-150", TestWorld.Start(sim));
            sim.State.Credits = 50000;
            TestWorld.AssertOk(sim.Dispatch(new VoyagePlan(a.Id, TestWorld.Mine(sim)).WithFreight("food", 20), true));
            sim.AdvanceBy(GameTime.TicksPerYear / 2);
            TestWorld.AssertOk(sim.Dispatch(new VoyagePlan(b.Id, TestWorld.OtherFoodImporter(sim)).WithFreight("food", 20), true));
            Assert.AreNotEqual(a.Voyage.ArriveTick, b.Voyage.ArriveTick);
            var first = a.Voyage.ArriveTick < b.Voyage.ArriveTick ? a : b;
            var second = first == a ? b : a;
            sim.AdvanceTo(first.Voyage.ArriveTick);
            Assert.IsTrue(first.Docked);
            Assert.IsFalse(second.Docked);
            Assert.Greater(second.Voyage.Progress(sim.State.Now), 0);
            Assert.Less(second.Voyage.Progress(sim.State.Now), 1);
            sim.AdvanceTo(second.Voyage.ArriveTick);
            Assert.IsTrue(second.Docked);
            Assert.AreEqual(2, sim.State.Receipts.Count);
        }

        [Test]
        public void LocalYardInstallsDriveAndRemoteUpgradeIsRejected()
        {
            var sim = TestWorld.Fresh();
            sim.State.Credits = 1000000;
            var yard = sim.State.Yards[0];
            var ship = TestWorld.Starter(sim);
            Assert.AreNotEqual(yard.SystemId, ship.Location);

            var remote = sim.InstallDrive(ship.Id, yard.SystemId, "alc-225");
            Assert.IsFalse(remote.Ok);
            StringAssert.Contains("can only work on ships docked there", remote.Message);
            Assert.AreEqual("alc-150", ship.DriveId);
            Assert.AreEqual(1000000, sim.State.Credits);

            var bought = sim.BuyShip(yard.SystemId, yard.Hulls[0].HullId);
            TestWorld.AssertOk(bought);
            Assert.AreEqual(yard.SystemId, bought.Ship.Location, "purchased vessels are delivered docked at the selling yard");
            long credits = sim.State.Credits;
            var preview = sim.PreviewDriveUpgrade(bought.Ship.Id, yard.SystemId, "alc-225", TestWorld.Start(sim));
            Assert.IsTrue(preview.Allowed);
            Assert.Greater(preview.SavedTicks, 0);
            TestWorld.AssertOk(sim.InstallDrive(bought.Ship.Id, yard.SystemId, "alc-225"));
            Assert.AreEqual("alc-225", bought.Ship.DriveId);
            Assert.AreEqual(credits - preview.Price, sim.State.Credits);

            // A ship in transit cannot be upgraded even if bound for the yard.
            yard.Hull("courier").Count = 1;
            var other = sim.BuyShip(yard.SystemId, "courier");
            TestWorld.AssertOk(other);
            TestWorld.AssertOk(sim.Dispatch(new VoyagePlan(other.Ship.Id, TestWorld.Start(sim)), true));
            Assert.IsFalse(sim.InstallDrive(other.Ship.Id, yard.SystemId, "alc-225").Ok);
        }

        [Test]
        public void DriveIsLockedForTheVoyage()
        {
            var sim = TestWorld.Fresh();
            sim.State.Credits = 1000000;
            var yard = sim.State.Yards[0];
            var ship = sim.BuyShip(yard.SystemId, "courier").Ship;
            TestWorld.AssertOk(sim.Dispatch(new VoyagePlan(ship.Id, TestWorld.Start(sim)), true));
            Assert.IsFalse(sim.InstallDrive(ship.Id, yard.SystemId, "alc-225").Ok);
            Assert.AreEqual("alc-150", ship.Voyage.DriveId);
        }
    }
}
