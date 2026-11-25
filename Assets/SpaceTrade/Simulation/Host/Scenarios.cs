using System;
using System.Collections.Generic;

namespace SpaceTrade.Sim
{
    public static class Scenarios
    {
        public const ulong DefaultSeed = 1977;

        /// <summary>Fresh campaign: one docked courier at the start port and enough credits to provision a first voyage.</summary>
        public static GameState NewGame(ulong seed, DateTime utcNow)
        {
            var s = CreateWorld(seed, utcNow);
            var sim = new Simulation(s);
            var start = s.Galaxy.Get(s.Galaxy.StartSystemId);
            var ship = sim.NewShip("courier", "alc-150", start.Id, "Wayfarer");
            ship.Supplies.Fuel.Add(6, 6 * start.FuelPrice);
            ship.Supplies.Food.Add(1, 1 * start.FoodPrice);
            ship.Supplies.Water.Add(0.5, 0.5 * start.WaterPrice);
            s.Notify(NoticeKind.Info, ship.Id, "Welcome aboard. " + ship.Name + " is docked at " + start.Name + ". Select it, pick a destination and review the quote.");
            return s;
        }

        public static GameState CreateWorld(ulong seed, DateTime utcNow)
        {
            var g = Galaxy.Generate(seed);
            var s = new GameState
            {
                Seed = seed,
                Galaxy = g,
                Credits = Catalog.StartingCredits,
                Speed = 1,
                Paused = false,
                LastWallClockUtcTicks = utcNow.Ticks,
            };
            foreach (var sys in g.Systems)
            {
                foreach (var e in sys.Role.Exports)
                    s.Supply.Add(new SupplyPool { SystemId = sys.Id, Commodity = e, Available = 180, Max = 300, PerTick = 6 });
                foreach (var i in sys.Role.Imports)
                    s.Demand.Add(new DemandPool { SystemId = sys.Id, Commodity = i, Available = 160, Max = 240, PerTick = 5 });
            }
            foreach (var a in g.Systems)
                foreach (var b in g.Systems)
                {
                    if (a.Id == b.Id || a.DistanceTo(b) > Catalog.MaxContractRangeLy) continue;
                    int max = 6 + 3 * Math.Min(a.Role.Population, b.Role.Population);
                    s.PassengerBookings.Add(new PassengerPool { Origin = a.Id, Destination = b.Id, Available = max / 2, Max = max, PerTick = 1 });
                }
            foreach (var sys in g.Systems)
            {
                if (!sys.HasShipyard) continue;
                var y = new Shipyard { SystemId = sys.Id };
                if (sys.Role == Galaxy.Core)
                {
                    y.Hulls.Add(new YardHullStock { HullId = "courier", Count = 2, MaxCount = 2, Price = 95000 });
                    y.Hulls.Add(new YardHullStock { HullId = "liner", Count = 1, MaxCount = 1, Price = 420000 });
                    y.Drives.Add(new YardDriveStock { DriveId = "alc-225", Count = 3, MaxCount = 3, Price = 60000 });
                    y.Drives.Add(new YardDriveStock { DriveId = "alc-375", Count = 2, MaxCount = 2, Price = 180000 });
                }
                else if (sys.Role == Galaxy.Industrial)
                {
                    y.Hulls.Add(new YardHullStock { HullId = "courier", Count = 1, MaxCount = 1, Price = 92000 });
                    y.Hulls.Add(new YardHullStock { HullId = "freighter", Count = 2, MaxCount = 2, Price = 380000 });
                    y.Drives.Add(new YardDriveStock { DriveId = "alc-225", Count = 3, MaxCount = 3, Price = 58000 });
                }
                else
                {
                    y.Hulls.Add(new YardHullStock { HullId = "courier", Count = 2, MaxCount = 2, Price = 98000 });
                    y.Hulls.Add(new YardHullStock { HullId = "liner", Count = 1, MaxCount = 1, Price = 430000 });
                    y.Drives.Add(new YardDriveStock { DriveId = "alc-225", Count = 2, MaxCount = 2, Price = 62000 });
                    y.Drives.Add(new YardDriveStock { DriveId = "alc-375", Count = 2, MaxCount = 2, Price = 175000 });
                }
                s.Yards.Add(y);
            }
            return s;
        }

        /// <summary>
        /// Demonstration save: the same galaxy with a funded company running several automated services, already in motion.
        /// Lives in its own slot, so it never touches the pacing of a fresh campaign.
        /// </summary>
        public static GameState Demo(ulong seed, DateTime utcNow)
        {
            var s = CreateWorld(seed, utcNow);
            s.IsDemo = true;
            s.Credits = 650000;
            var sim = new Simulation(s);
            var g = s.Galaxy;

            int start = g.StartSystemId;
            int mine = g.TimingTestPartnerId;
            int refinery = Nearest(g, mine, Galaxy.Refinery);
            int industrial = Nearest(g, refinery, Galaxy.Industrial);
            int core = Nearest(g, start, Galaxy.Core);
            int tech = Nearest(g, core, Galaxy.Tech);

            // 1. Food shuttle: food and passengers out to the mining colony, passengers only on the way back.
            var c1 = sim.NewShip("courier", "alc-225", start, "Wayfarer");
            Provision(c1, g.Get(start));
            var food = new RouteOrder { Name = "Food shuttle" };
            food.Stops.Add(new RouteStop { SystemId = start, Commodities = { "food" }, Passengers = true, MinLoad = 10 });
            food.Stops.Add(new RouteStop { SystemId = mine, Passengers = true, MinLoad = 0 });
            sim.SetRoute(c1.Id, food);

            // 2. Bulk circuit: ore -> refinery, alloys -> industrial, machinery -> mining colony.
            var f1 = sim.NewShip("freighter", "alc-150", mine, "Long Patience");
            var bulk = new RouteOrder { Name = "Ore-alloy-machinery circuit" };
            bulk.Stops.Add(new RouteStop { SystemId = mine, Commodities = { "ore" }, MinLoad = 60 });
            bulk.Stops.Add(new RouteStop { SystemId = refinery, Commodities = { "alloys" }, MinLoad = 0 });
            bulk.Stops.Add(new RouteStop { SystemId = industrial, Commodities = { "machinery" }, MinLoad = 0 });
            sim.SetRoute(f1.Id, bulk);

            // 3. Passenger liner between the core world and the tech centre.
            var l1 = sim.NewShip("liner", "alc-225", core, "Halcyon");
            var pax = new RouteOrder { Name = "Core express" };
            pax.Stops.Add(new RouteStop { SystemId = core, Commodities = { "medicine" }, Passengers = true, MinLoad = 12, MinLoadUnit = LoadUnit.Passengers });
            pax.Stops.Add(new RouteStop { SystemId = tech, Commodities = { "electronics" }, Passengers = true, MinLoad = 0 });
            sim.SetRoute(l1.Id, pax);

            // 4. A manual courier mid-voyage from the start port to the core world.
            var c2 = sim.NewShip("courier", "alc-150", start, "Ember Tide");
            var manual = new VoyagePlan(c2.Id, core).WithFreight("food", 20).WithPassengers(4);
            if (!sim.Dispatch(manual, autoProvision: true).Ok)
                sim.Dispatch(new VoyagePlan(c2.Id, mine).WithFreight("food", 20), autoProvision: true);

            // 5. An idle courier waiting at the core yard for orders.
            sim.NewShip("courier", "alc-375", core, "Quiet Margin");

            // Let the network run for a little while so ships are spread along their legs.
            sim.AdvanceTo(GameTime.YearsToTicks(0.6));
            s.Notifications.Clear();
            s.Notify(NoticeKind.Info, -1, "Demonstration company: automated services are running. This save is separate from your campaign.");
            return s;
        }

        private static void Provision(Ship ship, StarSystem port)
        {
            ship.Supplies.Fuel.Add(10, 10 * port.FuelPrice);
            ship.Supplies.Food.Add(3, 3 * port.FoodPrice);
            ship.Supplies.Water.Add(4, 4 * port.WaterPrice);
        }

        private static int Nearest(Galaxy g, int from, RoleDef role)
        {
            int best = -1;
            double bestD = double.MaxValue;
            foreach (var s in g.Systems)
            {
                if (s.Role != role || s.Id == from) continue;
                double d = g.Distance(from, s.Id);
                if (d < bestD) { bestD = d; best = s.Id; }
            }
            return best;
        }
    }
}
