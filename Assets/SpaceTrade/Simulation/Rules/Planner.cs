using System;
using System.Collections.Generic;

namespace SpaceTrade.Sim
{
    public sealed class FreightRequest
    {
        public string Commodity;
        public int Tonnes;
        public FreightRequest(string commodity, int tonnes) { Commodity = commodity; Tonnes = tonnes; }
    }

    public sealed class VoyagePlan
    {
        public int ShipId;
        public int Destination;
        public List<FreightRequest> Freight = new List<FreightRequest>();
        public int Passengers;
        public bool Emergency;
        public bool Automated;

        public VoyagePlan() { }
        public VoyagePlan(int shipId, int destination) { ShipId = shipId; Destination = destination; }

        public VoyagePlan WithFreight(string commodity, int tonnes) { Freight.Add(new FreightRequest(commodity, tonnes)); return this; }
        public VoyagePlan WithPassengers(int n) { Passengers = n; return this; }

        public int FreightTonnes { get { int s = 0; foreach (var f in Freight) s += f.Tonnes; return s; } }
    }

    public sealed class ProvisionLine
    {
        public string Id;
        public string Label;
        public double Aboard;
        public double Required;
        public double TopUp;
        public int Price;
        public long TopUpCost;
        public double ConsumedCostExact;
        public long ConsumedCost;
        public double AfterTopUp { get { return Math.Max(Aboard, Required); } }
        public double Shortfall { get { return TopUp; } }
    }

    public sealed class Quote
    {
        public VoyagePlan Plan;
        public bool LoadFeasible = true;
        public List<string> Problems = new List<string>();
        public List<string> Notes = new List<string>();

        public int Origin, Destination;
        public double DistanceLy;
        public DriveDef Drive;
        public long DurationTicks;
        public long DepartTick, ArrivalTick;
        public int People;

        public ProvisionLine Fuel, Food, Water;
        public double HoldCapacity, HoldUsed, FreightTonnes, TankCapacity, LoadedMass;
        public int Berths;

        public List<FreightLine> FreightLines = new List<FreightLine>();
        public int FarePerPassenger;
        public long FreightRevenue, FareRevenue, PortFee, TopUpCost, EmergencyAdvance, EmergencyPayment;
        public long Credits;

        public ProvisionLine[] ProvisionLines { get { return new[] { Fuel, Food, Water }; } }
        public bool NeedsProvisioning { get { return Fuel.TopUp > 1e-9 || Food.TopUp > 1e-9 || Water.TopUp > 1e-9; } }
        public bool CanAffordProvisioning { get { return TopUpCost <= Credits; } }
        public bool ReadyToLaunch { get { return LoadFeasible && !NeedsProvisioning; } }
        public bool CanLaunchWithProvisioning { get { return LoadFeasible && CanAffordProvisioning; } }
        public long GrossRevenue { get { return FreightRevenue + FareRevenue; } }
        public long SuppliesCost { get { return Fuel.ConsumedCost + Food.ConsumedCost + Water.ConsumedCost; } }
        public long OperatingCost { get { return SuppliesCost + PortFee; } }
        public long NetProfit { get { return GrossRevenue - OperatingCost; } }
        public double DurationYears { get { return GameTime.TicksToYears(DurationTicks); } }
        public double ProfitPerYear { get { return DurationYears > 0 ? NetProfit / DurationYears : 0; } }

        public void Fail(string problem) { LoadFeasible = false; Problems.Add(problem); }
    }

    /// <summary>Pure quote calculation. Used verbatim by the preview UI, manual dispatch, automated routes and emergency contracts.</summary>
    public static class Planner
    {
        public const double EmergencyReliefTonnes = 5;

        public static long Cr(double v) { return (long)Math.Round(v, MidpointRounding.AwayFromZero); }

        private static double Ceil2(double v) { return Math.Ceiling(v * 100 - 1e-7) / 100.0; }

        public static double FoodRequired(int people, double years) { return Ceil2(people * years * Catalog.FoodPerPersonYear); }
        public static double WaterRequired(int people, double years) { return Ceil2(people * years * Catalog.WaterPerPersonYear); }

        public static double FuelRequired(double distanceLy, DriveDef drive, double loadedMass)
        {
            return Ceil2(distanceLy * drive.FuelPerLyPer100t * loadedMass / 100.0);
        }

        public static int Fare(double distanceLy) { return Catalog.PassengerBaseFare + (int)Math.Round(Catalog.PassengerFarePerLy * distanceLy); }

        public static Quote Build(GameState s, VoyagePlan plan)
        {
            var q = new Quote { Plan = plan, Credits = s.Credits, Destination = plan.Destination, DepartTick = s.Now };
            var ship = s.Ship(plan.ShipId);
            if (ship == null) { q.Fail("Unknown ship."); FillEmpty(q); return q; }
            var hull = ship.Hull;
            var drive = ship.Drive;
            q.Drive = drive;
            q.Origin = ship.Location;
            q.HoldCapacity = hull.HoldTonnes;
            q.TankCapacity = hull.TankTonnes;
            q.Berths = hull.Berths;

            if (!ship.Docked) q.Fail(ship.Name + " is in transit; orders can change once it docks.");
            if (plan.Destination < 0 || plan.Destination >= s.Galaxy.Systems.Count) { q.Fail("Choose a destination."); FillEmpty(q); return q; }
            if (plan.Destination == ship.Location) { q.Fail("Destination is the current port."); FillEmpty(q); return q; }
            if (plan.Passengers < 0) q.Fail("Passenger count cannot be negative.");

            var origin = s.Galaxy.Get(q.Origin);
            var dest = s.Galaxy.Get(plan.Destination);
            q.DistanceLy = origin.DistanceTo(dest);
            q.DurationTicks = GameTime.TransitTicks(q.DistanceLy, drive.RatingC);
            q.ArrivalTick = s.Now + q.DurationTicks;
            double years = q.DurationYears;

            // Freight: customers' cargo, priced per tonne by contract.
            var seen = new HashSet<string>();
            foreach (var req in plan.Freight)
            {
                if (req.Tonnes <= 0) continue;
                if (!seen.Add(req.Commodity)) { q.Fail("Freight line for " + req.Commodity + " is listed twice."); continue; }
                var def = Catalog.Commodity(req.Commodity);
                var supply = s.SupplyAt(q.Origin, req.Commodity);
                var demand = s.DemandAt(plan.Destination, req.Commodity);
                if (plan.Emergency) continue;
                if (supply == null) { q.Fail(origin.Name + " does not export " + def.Name + "."); continue; }
                if (demand == null) { q.Fail(dest.Name + " does not import " + def.Name + "."); continue; }
                if (q.DistanceLy > Catalog.MaxContractRangeLy)
                {
                    q.Fail(string.Format("{0} is {1} ly away; freight contracts cover at most {2} ly.", dest.Name, Fmt.N2(q.DistanceLy), Catalog.MaxContractRangeLy));
                    continue;
                }
                if (req.Tonnes > supply.Available)
                    q.Fail(string.Format("Only {0} t of {1} available at {2} (requested {3} t).", supply.Available, def.Name, origin.Name, req.Tonnes));
                if (req.Tonnes > demand.Available)
                    q.Fail(string.Format("{0} can take only {1} t more {2} (requested {3} t).", dest.Name, demand.Available, def.Name, req.Tonnes));
                q.FreightLines.Add(new FreightLine { Commodity = req.Commodity, Tonnes = req.Tonnes, RatePerTonne = def.ContractRate(q.DistanceLy) });
            }
            double freight = 0;
            foreach (var l in q.FreightLines) freight += l.Tonnes;
            if (plan.Emergency) freight = Math.Min(EmergencyReliefTonnes, Math.Max(0, hull.HoldTonnes * 0.1));
            q.FreightTonnes = freight;

            // Passengers: booked seats between this origin and destination.
            int pax = plan.Emergency ? 0 : Math.Max(0, plan.Passengers);
            if (pax > 0)
            {
                var book = s.Bookings(q.Origin, plan.Destination);
                if (pax > hull.Berths) q.Fail(string.Format("{0} has {1} berths; {2} passengers requested.", ship.Name, hull.Berths, pax));
                if (book == null) q.Fail("No passenger service is booked from " + origin.Name + " to " + dest.Name + ".");
                else if (pax > book.Available) q.Fail(string.Format("Only {0} passengers booked to {1} (requested {2}).", book.Available, dest.Name, pax));
                q.FarePerPassenger = Fare(q.DistanceLy);
            }
            q.People = hull.Crew + pax;

            // Provisions: food and water from people x duration, sharing the hold with freight.
            q.Food = Line(ship, origin, "food", "Food", FoodRequired(q.People, years));
            q.Water = Line(ship, origin, "water", "Drinking water", WaterRequired(q.People, years));
            q.HoldUsed = freight + q.Food.AfterTopUp + q.Water.AfterTopUp;
            if (q.HoldUsed > hull.HoldTonnes + 1e-9)
            {
                double over = Ceil2(q.HoldUsed - hull.HoldTonnes);
                string supplies = string.Format("{0} of drinking water and {1} of food", Fmt.T(q.Water.AfterTopUp), Fmt.T(q.Food.AfterTopUp));
                if (freight >= over)
                    q.Fail(string.Format("Hold over capacity by {0}: unload {0} of freight to make room for {1}.", Fmt.T(over), supplies));
                else
                    q.Fail(string.Format("This leg needs {0} ({1} total) but the hold is {2}: carry fewer passengers or choose a closer destination.",
                        supplies, Fmt.T(q.Food.AfterTopUp + q.Water.AfterTopUp), Fmt.T(hull.HoldTonnes)));
            }

            // Fuel from distance, drive tier and loaded mass (hull + hold + passengers).
            q.LoadedMass = hull.HullMassTonnes + q.HoldUsed + pax * Catalog.PassengerMassTonnes;
            double fuelReq = FuelRequired(q.DistanceLy, drive, q.LoadedMass);
            q.Fuel = Line(ship, origin, "fuel", "Fuel", fuelReq);
            if (fuelReq > hull.TankTonnes + 1e-9)
            {
                double perTonne = q.DistanceLy * drive.FuelPerLyPer100t / 100.0;
                double reduce = Ceil2((fuelReq - hull.TankTonnes) / perTonne);
                double emptyMass = hull.HullMassTonnes + q.Food.Required + q.Water.Required;
                double maxRange = hull.TankTonnes / (drive.FuelPerLyPer100t * emptyMass / 100.0);
                if (reduce <= freight)
                    q.Fail(string.Format("Needs {0} of fuel but the tank holds {1}: unload {2} of freight to bring the fuel within capacity.",
                        Fmt.T(fuelReq), Fmt.T(hull.TankTonnes), Fmt.T(reduce)));
                else
                    q.Fail(string.Format("Needs {0} of fuel but the tank holds {1}: {2} is {3} ly away and this ship's range with the {4} is about {5} ly.",
                        Fmt.T(fuelReq), Fmt.T(hull.TankTonnes), dest.Name, Fmt.N2(q.DistanceLy), drive.Name, Fmt.N1(maxRange)));
            }

            // Costs: supplies consumed at acquisition cost (aboard at its basis, top-up at this port's price).
            long topUpCost = 0;
            foreach (var line in q.ProvisionLines) topUpCost += line.TopUpCost;
            if (plan.Emergency)
            {
                q.EmergencyAdvance = topUpCost;
                q.TopUpCost = 0;
                q.EmergencyPayment = q.EmergencyAdvance + Catalog.PortFee + Catalog.EmergencyMarginCredits;
                q.FreightRevenue = q.EmergencyPayment;
                q.Notes.Add("Emergency contract: provisions worth " + Fmt.Credits(q.EmergencyAdvance) + " are supplied in kind for this departure only and deducted from the payment.");
            }
            else
            {
                q.TopUpCost = topUpCost;
                foreach (var l in q.FreightLines) q.FreightRevenue += l.Amount;
                q.FareRevenue = (long)pax * q.FarePerPassenger;
            }
            q.PortFee = Catalog.PortFee;

            if (q.NeedsProvisioning)
            {
                foreach (var line in q.ProvisionLines)
                    if (line.TopUp > 1e-9)
                        q.Notes.Add(string.Format("Short {0} of {1}: top-up costs {2}.", Fmt.T(line.TopUp), line.Label.ToLowerInvariant(),
                            Fmt.Credits(line.TopUpCost)));
                if (!plan.Emergency && !q.CanAffordProvisioning)
                    q.Notes.Add(string.Format("Top-up costs {0} but the company has {1} (short {2}).", Fmt.Credits(q.TopUpCost),
                        Fmt.Credits(s.Credits), Fmt.Credits(q.TopUpCost - s.Credits)));
            }
            if (q.FreightTonnes <= 0 && pax == 0 && !plan.Emergency) q.Notes.Add("Empty repositioning leg: costs only, no revenue.");
            return q;
        }

        private static ProvisionLine Line(Ship ship, StarSystem port, string id, string label, double required)
        {
            var lot = ship.Supplies.Get(id);
            var line = new ProvisionLine { Id = id, Label = label, Aboard = lot.Quantity, Required = required, Price = port.ProvisionPrice(id) };
            line.TopUp = Math.Max(0, Ceil2(required - lot.Quantity));
            if (line.TopUp < 0.005) line.TopUp = 0;
            line.TopUpCost = Cr(line.TopUp * line.Price);
            double qty = lot.Quantity + line.TopUp;
            double basis = lot.CostBasis + line.TopUpCost;
            line.ConsumedCostExact = qty > 1e-9 ? basis * Math.Min(1.0, required / qty) : 0;
            line.ConsumedCost = Cr(line.ConsumedCostExact);
            return line;
        }

        private static void FillEmpty(Quote q)
        {
            q.Fuel = q.Fuel ?? new ProvisionLine { Id = "fuel", Label = "Fuel" };
            q.Food = q.Food ?? new ProvisionLine { Id = "food", Label = "Food" };
            q.Water = q.Water ?? new ProvisionLine { Id = "water", Label = "Drinking water" };
        }

        /// <summary>Largest passenger count up to 'desired' whose food and water fit the hold and whose mass fits the tank.</summary>
        public static int MaxPassengers(GameState s, Ship ship, int destination, int desired)
        {
            for (int pax = Math.Min(desired, ship.Hull.Berths); pax > 0; pax--)
                if (FitsWithFreight(s, ship, destination, pax)) return pax;
            return 0;
        }

        private static bool FitsWithFreight(GameState s, Ship ship, int destination, int passengers)
        {
            var hull = ship.Hull;
            double dist = s.Galaxy.Distance(ship.Location, destination);
            double years = GameTime.TicksToYears(GameTime.TransitTicks(dist, ship.Drive.RatingC));
            int people = hull.Crew + passengers;
            double food = Math.Max(ship.Supplies.Food.Quantity, FoodRequired(people, years));
            double water = Math.Max(ship.Supplies.Water.Quantity, WaterRequired(people, years));
            if (food + water > hull.HoldTonnes + 1e-9) return false;
            double mass = hull.HullMassTonnes + food + water + passengers * Catalog.PassengerMassTonnes;
            return FuelRequired(dist, ship.Drive, mass) <= hull.TankTonnes + 1e-9;
        }

        /// <summary>Largest whole tonnage of freight that fits the hold and tank for a leg with the given passengers.</summary>
        public static int MaxFreightTonnes(GameState s, Ship ship, int destination, int passengers)
        {
            var hull = ship.Hull;
            var drive = ship.Drive;
            double dist = s.Galaxy.Distance(ship.Location, destination);
            double years = GameTime.TransitYears(dist, drive.RatingC);
            years = GameTime.TicksToYears(GameTime.YearsToTicks(years));
            int people = hull.Crew + passengers;
            double food = Math.Max(ship.Supplies.Food.Quantity, FoodRequired(people, years));
            double water = Math.Max(ship.Supplies.Water.Quantity, WaterRequired(people, years));
            double byHold = hull.HoldTonnes - food - water;
            double perTonne = dist * drive.FuelPerLyPer100t / 100.0;
            double baseMass = hull.HullMassTonnes + food + water + passengers * Catalog.PassengerMassTonnes;
            double byTank = perTonne > 0 ? (hull.TankTonnes - 0.02) / perTonne - baseMass : double.MaxValue;
            int max = (int)Math.Floor(Math.Min(byHold, byTank) + 1e-9);
            return Math.Max(0, max);
        }
    }
}
