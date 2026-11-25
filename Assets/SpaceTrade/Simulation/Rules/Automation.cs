using System;
using System.Collections.Generic;

namespace SpaceTrade.Sim
{
    public sealed class LegEstimate
    {
        public int From, To;
        public double DistanceLy;
        public long Ticks;
        public int FreightTonnes, Passengers;
        public long Revenue, SuppliesCost, PortFee;
        public bool Empty, Feasible = true, DependsOnBookings;
        public string Problem;
        public long Net { get { return Revenue - SuppliesCost - PortFee; } }
    }

    public sealed class CircuitEstimate
    {
        public List<LegEstimate> Legs = new List<LegEstimate>();
        public long Revenue, Costs, TotalTicks;
        public bool DependsOnBookings, ExcludesWaiting = true, AllFeasible = true;
        public long Net { get { return Revenue - Costs; } }
        public double Years { get { return GameTime.TicksToYears(TotalTicks); } }
        public double ProfitPerYear { get { return TotalTicks > 0 ? Net / Years : 0; } }
    }

    public sealed partial class Simulation
    {
        public CommandResult SetRoute(int shipId, RouteOrder route)
        {
            var ship = State.Ship(shipId);
            if (ship == null) return CommandResult.Failure("Unknown ship.");
            if (route == null || route.Stops.Count < 2) return CommandResult.Failure("A route needs at least two stops.");
            for (int i = 0; i < route.Stops.Count; i++)
            {
                var a = route.Stops[i];
                var b = route.Stops[(i + 1) % route.Stops.Count];
                if (a.SystemId == b.SystemId) return CommandResult.Failure("Consecutive stops must be different systems (stop " + (i + 1) + ").");
                if (a.MinLoad < 0) return CommandResult.Failure("Minimum load cannot be negative.");
            }
            var copy = route.Clone();
            if (ship.Docked)
            {
                int idx = -1;
                for (int i = 0; i < copy.Stops.Count; i++) if (copy.Stops[i].SystemId == ship.Location) { idx = i; break; }
                if (idx < 0)
                    return CommandResult.Failure(string.Format("{0} is docked at {1}, which is not a stop on this route. Add it as a stop or move the ship first.", ship.Name, Galaxy.Get(ship.Location).Name));
                copy.CurrentStop = idx;
            }
            else
            {
                // In transit: the route takes over after this voyage arrives, starting from its destination stop.
                int idx = -1;
                for (int i = 0; i < copy.Stops.Count; i++) if (copy.Stops[i].SystemId == ship.Voyage.Destination) { idx = i; break; }
                if (idx < 0) return CommandResult.Failure("The ship's current destination must be a stop on the new route.");
                copy.CurrentStop = idx;
                ship.Voyage.Automated = false; // arrival keeps CurrentStop at idx
            }
            ship.Route = copy;
            ship.BlockedReason = null;
            ship.WaitingReason = null;
            if (ship.Docked) TryAutoDepart(ship);
            return CommandResult.Success("Route assigned to " + ship.Name + ".");
        }

        public CommandResult ClearRoute(int shipId)
        {
            var ship = State.Ship(shipId);
            if (ship == null) return CommandResult.Failure("Unknown ship.");
            ship.Route = null;
            ship.WaitingReason = null;
            ship.BlockedReason = null;
            if (ship.Voyage != null) ship.Voyage.Automated = false;
            return CommandResult.Success(ship.Name + " is under manual orders.");
        }

        private void RunAutomation()
        {
            var ships = new List<Ship>(State.Ships);
            ships.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var s in ships)
                if (s.Route != null && s.Docked) TryAutoDepart(s);
        }

        /// <summary>Builds the load for the ship's next leg from current market availability (same rules as manual dispatch).</summary>
        public VoyagePlan BuildRoutePlan(Ship ship, out int loadUnits, out int capacityUnits)
        {
            var route = ship.Route;
            var stop = route.Stops[route.CurrentStop];
            var next = route.Stops[(route.CurrentStop + 1) % route.Stops.Count];
            var plan = new VoyagePlan(ship.Id, next.SystemId) { Automated = true };
            loadUnits = 0;
            capacityUnits = 0;
            if (stop.EmptyLeg) return plan;

            if (stop.Passengers && ship.Hull.Berths > 0)
            {
                var book = State.Bookings(ship.Location, next.SystemId);
                int pax = book == null ? 0 : Math.Min(ship.Hull.Berths, book.Available);
                plan.Passengers = Planner.MaxPassengers(State, ship, next.SystemId, pax);
            }
            int room = Planner.MaxFreightTonnes(State, ship, next.SystemId, plan.Passengers);
            int freightCapacity = room;
            foreach (var c in stop.Commodities)
            {
                if (room <= 0) break;
                var sp = State.SupplyAt(ship.Location, c);
                var dp = State.DemandAt(next.SystemId, c);
                if (sp == null || dp == null) continue;
                int qty = Math.Min(room, Math.Min(sp.Available, dp.Available));
                if (qty > 0) { plan.WithFreight(c, qty); room -= qty; }
            }
            if (stop.MinLoadUnit == LoadUnit.Passengers)
            {
                loadUnits = plan.Passengers;
                capacityUnits = Planner.MaxPassengers(State, ship, next.SystemId, ship.Hull.Berths);
            }
            else
            {
                loadUnits = plan.FreightTonnes;
                capacityUnits = freightCapacity;
            }
            return plan;
        }

        private void TryAutoDepart(Ship ship)
        {
            var route = ship.Route;
            if (route == null || !ship.Docked) return;
            if (route.CurrentStop < 0 || route.CurrentStop >= route.Stops.Count) route.CurrentStop = 0;
            var stop = route.Stops[route.CurrentStop];
            var nextStop = route.Stops[(route.CurrentStop + 1) % route.Stops.Count];
            if (ship.Location != stop.SystemId)
            {
                SetBlocked(ship, string.Format("Docked at {0} but the route expects {1}. Change orders.", Galaxy.Get(ship.Location).Name, Galaxy.Get(stop.SystemId).Name));
                return;
            }

            int load, capacity;
            var plan = BuildRoutePlan(ship, out load, out capacity);
            if (!stop.EmptyLeg && stop.MinLoad > 0)
            {
                if (stop.MinLoad > capacity)
                {
                    SetBlocked(ship, string.Format("Minimum load {0} {1} exceeds what fits on this leg ({2} {1}). Lower the minimum or change the stop.",
                        stop.MinLoad, UnitName(stop.MinLoadUnit), capacity));
                    return;
                }
                if (load < stop.MinLoad)
                {
                    string what = stop.MinLoadUnit == LoadUnit.Passengers
                        ? "passengers to " + Galaxy.Get(nextStop.SystemId).Name
                        : "tonnes of " + CommodityList(stop.Commodities);
                    SetWaiting(ship, string.Format("Waiting for {0} more {1}.", stop.MinLoad - load, what));
                    return;
                }
            }

            var q = Planner.Build(State, plan);
            if (!q.LoadFeasible)
            {
                SetBlocked(ship, "Next leg infeasible: " + string.Join(" ", q.Problems.ToArray()));
                return;
            }
            if (q.NeedsProvisioning)
            {
                if (!stop.AutoProvision)
                {
                    SetBlocked(ship, "Needs provisions (top-up " + Fmt.Credits(q.TopUpCost) + ") and automatic provisioning is off at this stop.");
                    return;
                }
                if (!q.CanAffordProvisioning)
                {
                    SetBlocked(ship, string.Format("Cannot afford provisions: top-up {0}, company has {1}.", Fmt.Credits(q.TopUpCost), Fmt.Credits(State.Credits)));
                    return;
                }
            }
            var res = Dispatch(plan, autoProvision: true);
            if (!res.Ok) SetBlocked(ship, res.Message);
        }

        private static string UnitName(LoadUnit u) { return u == LoadUnit.Passengers ? "passengers" : "t"; }

        private static string CommodityList(List<string> ids)
        {
            if (ids.Count == 0) return "freight";
            var names = new List<string>();
            foreach (var id in ids) names.Add(Catalog.Commodity(id).Name.ToLowerInvariant());
            return string.Join(" or ", names.ToArray());
        }

        private void SetWaiting(Ship ship, string reason)
        {
            ship.BlockedReason = null;
            if (ship.WaitingReason != reason)
            {
                bool wasWaiting = !string.IsNullOrEmpty(ship.WaitingReason);
                ship.WaitingReason = reason;
                if (!wasWaiting) State.Notify(NoticeKind.Waiting, ship.Id, ship.Name + ": " + reason);
            }
        }

        private void SetBlocked(Ship ship, string reason)
        {
            ship.WaitingReason = null;
            if (ship.BlockedReason != reason)
            {
                ship.BlockedReason = reason;
                State.Notify(NoticeKind.Blocked, ship.Id, ship.Name + " blocked at " + Galaxy.Get(ship.Location).Name + ": " + reason);
            }
        }

        /// <summary>Whole-circuit estimate including empty legs. Loads use current availability, so they are labelled as depending on bookings.</summary>
        public CircuitEstimate EstimateCircuit(Ship ship, RouteOrder route)
        {
            var est = new CircuitEstimate();
            if (route == null || route.Stops.Count < 2) return est;
            var hull = ship.Hull;
            var drive = ship.Drive;
            for (int i = 0; i < route.Stops.Count; i++)
            {
                var stop = route.Stops[i];
                var next = route.Stops[(i + 1) % route.Stops.Count];
                var from = Galaxy.Get(stop.SystemId);
                var to = Galaxy.Get(next.SystemId);
                var leg = new LegEstimate { From = from.Id, To = to.Id, DistanceLy = from.DistanceTo(to) };
                leg.Ticks = GameTime.TransitTicks(leg.DistanceLy, drive.RatingC);
                double years = GameTime.TicksToYears(leg.Ticks);
                leg.Empty = stop.EmptyLeg || (stop.Commodities.Count == 0 && !stop.Passengers);
                if (!leg.Empty)
                {
                    if (stop.Passengers && hull.Berths > 0)
                    {
                        var book = State.Bookings(from.Id, to.Id);
                        leg.Passengers = book == null ? 0 : Math.Min(hull.Berths, Math.Max(book.Available, book.Max / 2));
                        while (leg.Passengers > 0)
                        {
                            int ppl = hull.Crew + leg.Passengers;
                            if (Planner.FoodRequired(ppl, years) + Planner.WaterRequired(ppl, years) <= hull.HoldTonnes) break;
                            leg.Passengers--;
                        }
                    }
                    int people0 = hull.Crew + leg.Passengers;
                    double fw0 = Planner.FoodRequired(people0, years) + Planner.WaterRequired(people0, years);
                    double perTonne = leg.DistanceLy * drive.FuelPerLyPer100t / 100.0;
                    double byTank = (hull.TankTonnes - 0.02) / perTonne - (hull.HullMassTonnes + fw0 + leg.Passengers * Catalog.PassengerMassTonnes);
                    int room = (int)Math.Max(0, Math.Floor(Math.Min(hull.HoldTonnes - fw0, byTank)));
                    foreach (var c in stop.Commodities)
                    {
                        var sp = State.SupplyAt(from.Id, c);
                        var dp = State.DemandAt(to.Id, c);
                        if (sp == null || dp == null) continue;
                        int qty = Math.Min(room, Math.Min(Math.Max(sp.Available, sp.Max / 2), Math.Max(dp.Available, dp.Max / 2)));
                        if (qty <= 0) continue;
                        leg.FreightTonnes += qty;
                        room -= qty;
                        if (leg.DistanceLy <= Catalog.MaxContractRangeLy) leg.Revenue += (long)qty * Catalog.Commodity(c).ContractRate(leg.DistanceLy);
                    }
                    leg.Revenue += (long)leg.Passengers * Planner.Fare(leg.DistanceLy);
                    leg.DependsOnBookings = true;
                }
                int people = hull.Crew + leg.Passengers;
                double food = Planner.FoodRequired(people, years), water = Planner.WaterRequired(people, years);
                double mass = hull.HullMassTonnes + leg.FreightTonnes + food + water + leg.Passengers * Catalog.PassengerMassTonnes;
                double fuel = Planner.FuelRequired(leg.DistanceLy, drive, mass);
                leg.SuppliesCost = Planner.Cr(fuel * from.FuelPrice) + Planner.Cr(food * from.FoodPrice) + Planner.Cr(water * from.WaterPrice);
                leg.PortFee = Catalog.PortFee;
                if (fuel > hull.TankTonnes + 1e-9) { leg.Feasible = false; leg.Problem = "Out of range: needs " + Fmt.T(fuel) + " fuel, tank " + Fmt.T(hull.TankTonnes); }
                else if (food + water + leg.FreightTonnes > hull.HoldTonnes + 1e-9) { leg.Feasible = false; leg.Problem = "Provisions exceed hold"; }
                if (stop.MinLoad > 0 && !leg.Empty) est.ExcludesWaiting = true;
                est.Legs.Add(leg);
                est.Revenue += leg.Revenue;
                est.Costs += leg.SuppliesCost + leg.PortFee;
                est.TotalTicks += leg.Ticks;
                est.DependsOnBookings |= leg.DependsOnBookings;
                est.AllFeasible &= leg.Feasible;
            }
            return est;
        }
    }
}
