using System;
using NUnit.Framework;
using SpaceTrade.Sim;

namespace SpaceTrade.Tests
{
    public class RouteTests
    {
        private static RouteOrder FoodShuttle(Simulation sim, int minLoad, bool emptyReturn)
        {
            var r = new RouteOrder { Name = "Food shuttle" };
            r.Stops.Add(new RouteStop { SystemId = TestWorld.Start(sim), Commodities = { "food" }, Passengers = true, MinLoad = minLoad });
            r.Stops.Add(new RouteStop { SystemId = TestWorld.Mine(sim), Passengers = !emptyReturn, EmptyLeg = emptyReturn });
            return r;
        }

        [Test]
        public void SustainedAutomatedCircuitKeepsRunningAndProfits()
        {
            var sim = TestWorld.Fresh();
            var ship = TestWorld.Starter(sim);
            long credits0 = sim.State.Credits;
            TestWorld.AssertOk(sim.SetRoute(ship.Id, FoodShuttle(sim, 20, false)));
            Assert.IsFalse(ship.Docked, "departs immediately when the minimum load is available");
            sim.AdvanceTo(GameTime.YearsToTicks(60));
            Assert.GreaterOrEqual(sim.State.Receipts.Count, 16, "about 20 legs in 60 years");
            Assert.Greater(sim.State.Credits, credits0 + 100000);
            foreach (var r in sim.State.Receipts) Assert.IsTrue(r.Origin == TestWorld.Start(sim) || r.Origin == TestWorld.Mine(sim));
            TestWorld.AssertPoolsSane(sim.State);
            int reservedFood = sim.State.DemandAt(TestWorld.Mine(sim), "food").Reserved;
            int inFlight = ship.Voyage != null ? ship.Voyage.FreightTonnes : 0;
            Assert.AreEqual(inFlight, reservedFood, "reservations always equal freight in flight");
            Assert.AreEqual(sim.State.Receipts.Count, sim.State.SettledVoyages.Count);
        }

        [Test]
        public void MinimumLoadWaitsWithReasonThenDepartsWhenSupplyReplenishes()
        {
            var sim = TestWorld.Fresh();
            var ship = TestWorld.Starter(sim);
            int start = TestWorld.Start(sim);
            sim.State.SupplyAt(start, "food").Available = 8;
            TestWorld.AssertOk(sim.SetRoute(ship.Id, FoodShuttle(sim, 20, false)));
            Assert.IsTrue(ship.Docked);
            Assert.AreEqual(ShipStatus.Waiting, ship.Status);
            Assert.AreEqual("Waiting for 12 more tonnes of foodstuffs.", ship.WaitingReason);
            sim.AdvanceTo(sim.State.NextMarketTick); // +6 t
            Assert.AreEqual("Waiting for 6 more tonnes of foodstuffs.", ship.WaitingReason);
            sim.AdvanceTo(sim.State.NextMarketTick); // +6 t -> 20 t
            Assert.IsFalse(ship.Docked, "rechecked and departed when supply replenished");
            Assert.AreEqual(20, ship.Voyage.FreightTonnes);
            Assert.IsTrue(ship.Voyage.Automated);
        }

        [Test]
        public void MinimumLoadLargerThanCapacityBlocksTheShip()
        {
            var sim = TestWorld.Fresh();
            var ship = TestWorld.Starter(sim);
            TestWorld.AssertOk(sim.SetRoute(ship.Id, FoodShuttle(sim, 500, false)));
            Assert.AreEqual(ShipStatus.Blocked, ship.Status);
            StringAssert.Contains("exceeds what fits", ship.BlockedReason);
            Assert.IsTrue(sim.State.Notifications.Exists(n => n.Kind == NoticeKind.Blocked && n.ShipId == ship.Id));
            // The player changes orders and the ship continues.
            TestWorld.AssertOk(sim.SetRoute(ship.Id, FoodShuttle(sim, 10, false)));
            Assert.IsFalse(ship.Docked);
        }

        [Test]
        public void InfeasibleNextLegKeepsShipDockedAndFlagged()
        {
            var sim = TestWorld.Fresh();
            var ship = TestWorld.Starter(sim);
            sim.State.Credits = 0;
            ship.Supplies = new Supplies();
            TestWorld.AssertOk(sim.SetRoute(ship.Id, FoodShuttle(sim, 0, false)));
            Assert.IsTrue(ship.Docked);
            Assert.AreEqual(ShipStatus.Blocked, ship.Status);
            StringAssert.Contains("Cannot afford provisions", ship.BlockedReason);
        }

        [Test]
        public void EmptyRepositioningLegCostsAreEstimatedAndCharged()
        {
            var sim = TestWorld.Fresh();
            var ship = TestWorld.Starter(sim);
            var route = FoodShuttle(sim, 20, true);
            var est = sim.EstimateCircuit(ship, route);
            Assert.AreEqual(2, est.Legs.Count);
            var emptyLeg = est.Legs[1];
            Assert.IsTrue(emptyLeg.Empty);
            Assert.AreEqual(0, emptyLeg.Revenue);
            Assert.Greater(emptyLeg.SuppliesCost, 0);
            Assert.Less(emptyLeg.Net, 0);
            Assert.AreEqual(est.Legs[0].Ticks + est.Legs[1].Ticks, est.TotalTicks);
            Assert.AreEqual(est.Revenue - est.Costs, est.Net);
            Assert.AreEqual(est.Net / est.Years, est.ProfitPerYear, 1e-6);
            Assert.IsTrue(est.DependsOnBookings);
            Assert.IsTrue(est.ExcludesWaiting);

            TestWorld.AssertOk(sim.SetRoute(ship.Id, route));
            sim.AdvanceTo(ship.Voyage.ArriveTick);       // loaded leg arrives, empty leg departs
            Assert.IsFalse(ship.Docked);
            Assert.AreEqual(0, ship.Voyage.FreightTonnes);
            Assert.AreEqual(0, ship.Voyage.Passengers);
            sim.AdvanceTo(ship.Voyage.ArriveTick);
            var r = sim.State.Receipts[1];
            Assert.AreEqual(0, r.GrossRevenue);
            Assert.AreEqual(-(r.SuppliesCost + r.PortFee), r.NetProfit);
            Assert.Greater(r.SuppliesCost, 0);
        }

        [Test]
        public void CompetingAutomatedShipsResolveInShipIdOrder()
        {
            for (int run = 0; run < 2; run++)
            {
                var sim = TestWorld.Fresh();
                int start = TestWorld.Start(sim);
                sim.State.Credits = 100000;
                sim.State.SupplyAt(start, "food").Available = 0;
                var a = TestWorld.Starter(sim);
                var b = sim.NewShip("courier", "alc-150", start);
                TestWorld.AssertOk(sim.SetRoute(b.Id, FoodShuttle(sim, 30, false)));
                TestWorld.AssertOk(sim.SetRoute(a.Id, FoodShuttle(sim, 30, false)));
                Assert.AreEqual(ShipStatus.Waiting, a.Status);
                Assert.AreEqual(ShipStatus.Waiting, b.Status);
                sim.State.SupplyAt(start, "food").Available = 50; // enough for one ship only
                sim.AdvanceTo(sim.State.NextMarketTick);
                Assert.IsFalse(a.Docked, "lower ship id claims first");
                Assert.IsTrue(b.Docked);
                Assert.AreEqual(ShipStatus.Waiting, b.Status);
                Assert.LessOrEqual(a.Voyage.FreightTonnes, 56);
            }
        }

        [Test]
        public void RouteSurvivesInTransitAssignment()
        {
            var sim = TestWorld.Fresh();
            var ship = TestWorld.Starter(sim);
            TestWorld.AssertOk(sim.Dispatch(new VoyagePlan(ship.Id, TestWorld.Mine(sim)).WithFreight("food", 20), true));
            TestWorld.AssertOk(sim.SetRoute(ship.Id, FoodShuttle(sim, 0, false)));
            sim.AdvanceTo(ship.Voyage.ArriveTick);
            Assert.IsFalse(ship.Docked, "route takes over at arrival");
            Assert.AreEqual(TestWorld.Start(sim), ship.Voyage.Destination);
        }
    }

    public class EmergencyTests
    {
        private static Simulation Broke(out Ship ship)
        {
            var sim = TestWorld.Fresh();
            ship = TestWorld.Starter(sim);
            sim.State.Credits = 0;
            ship.Supplies = new Supplies();
            return sim;
        }

        [Test]
        public void NotOfferedWhenSolvent()
        {
            var sim = TestWorld.Fresh();
            Assert.IsTrue(sim.CanAffordAnyViableJob(TestWorld.Starter(sim)));
            Assert.IsNull(sim.EmergencyOffer(1));
            Assert.IsFalse(sim.AcceptEmergency(1).Ok);
        }

        [Test]
        public void EmergencyContractRecoversFromInsolvency()
        {
            Ship ship;
            var sim = Broke(out ship);
            Assert.IsFalse(sim.CanAffordAnyViableJob(ship));
            var offer = sim.EmergencyOffer(ship.Id);
            Assert.IsNotNull(offer);
            Assert.IsTrue(offer.LoadFeasible, string.Join(" ", offer.Problems.ToArray()));
            Assert.Greater(offer.EmergencyAdvance, 0);
            Assert.AreEqual(0, offer.TopUpCost);

            var res = sim.AcceptEmergency(ship.Id);
            TestWorld.AssertOk(res);
            Assert.AreEqual(0, sim.State.Credits, "provisions are supplied in kind, not paid in cash");
            Assert.AreEqual(0, ship.Supplies.Fuel.Quantity, 1e-9);
            Assert.AreEqual(0, ship.Supplies.Food.Quantity, 1e-9);
            Assert.AreEqual(0, ship.Supplies.Water.Quantity, 1e-9);
            Assert.IsNull(sim.EmergencyOffer(ship.Id), "no second contract while the first is in flight");

            sim.AdvanceTo(ship.Voyage.ArriveTick);
            Assert.AreEqual(Catalog.EmergencyMarginCredits, sim.State.Credits, "modest positive balance on delivery");
            var r = sim.LastReceiptFor(ship.Id);
            Assert.IsTrue(r.Emergency);
            Assert.AreEqual(offer.EmergencyAdvance, r.EmergencyAdvance);
            Assert.AreEqual(Catalog.EmergencyMarginCredits, r.CashCredited);
            Assert.AreEqual(0, ship.Supplies.Fuel.Quantity + ship.Supplies.Food.Quantity + ship.Supplies.Water.Quantity, 1e-9, "no inventory generated");
        }

        [Test]
        public void EmergencyCannotBeFarmedForMoneyOrInventory()
        {
            Ship ship;
            var sim = Broke(out ship);
            // Repeatedly asking for an offer creates nothing.
            for (int i = 0; i < 5; i++) Assert.IsNotNull(sim.EmergencyOffer(ship.Id));
            Assert.AreEqual(0, sim.State.Credits);
            Assert.AreEqual(0, ship.Supplies.Fuel.Quantity, 1e-9);
            // A failed emergency dispatch (ship in transit) changes nothing.
            TestWorld.AssertOk(sim.AcceptEmergency(ship.Id));
            string before = TestWorld.Fingerprint(sim.State);
            Assert.IsFalse(sim.AcceptEmergency(ship.Id).Ok);
            Assert.AreEqual(before, TestWorld.Fingerprint(sim.State));
        }

        [Test]
        public void AfterRecoveryTheCompanyCanAffordARegularJob()
        {
            Ship ship;
            var sim = Broke(out ship);
            TestWorld.AssertOk(sim.AcceptEmergency(ship.Id));
            sim.AdvanceTo(ship.Voyage.ArriveTick + GameTime.TicksPerYear);
            Assert.IsTrue(sim.CanAffordAnyViableJob(ship), "the margin funds at least one short regular job");
        }
    }
}
