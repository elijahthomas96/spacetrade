using System;
using System.Collections.Generic;

namespace SpaceTrade.Sim
{
    public sealed class CommandResult
    {
        public bool Ok;
        public string Message;
        public Quote Quote;
        public Voyage Voyage;
        public Ship Ship;

        public static CommandResult Success(string msg) { return new CommandResult { Ok = true, Message = msg }; }
        public static CommandResult Failure(string msg) { return new CommandResult { Ok = false, Message = msg }; }
    }

    public sealed class DriveUpgradePreview
    {
        public bool Allowed;
        public string Reason;
        public long Price;
        public string RouteLabel;
        public long OldTicks, NewTicks;
        public long SavedTicks { get { return OldTicks - NewTicks; } }
        public double OldFuel, NewFuel;
    }

    /// <summary>
    /// Authoritative game simulation. Owns a GameState and mutates it only through commands and AdvanceTo.
    /// Has no notion of real time: hosts convert real elapsed time to game ticks (see SimulationSession).
    /// </summary>
    public sealed partial class Simulation
    {
        public readonly GameState State;

        /// <summary>True only when the host is the Unity Editor or a Development Build.</summary>
        public bool DebugAllowed { get; private set; }
        private bool _debugEnabled;

        /// <summary>Debug mode must be explicitly enabled, and can only be enabled when allowed by the host.</summary>
        public bool DebugEnabled
        {
            get { return DebugAllowed && _debugEnabled; }
            set { _debugEnabled = DebugAllowed && value; }
        }

        public event Action<Receipt> Settled;

        public Simulation(GameState state, bool debugAllowed = false)
        {
            State = state;
            DebugAllowed = debugAllowed;
        }

        public Galaxy Galaxy { get { return State.Galaxy; } }

        // ------------------------------------------------------------------ clock

        public CommandResult SetSpeed(int speed)
        {
            if (!GameTime.IsAllowedSpeed(speed)) return CommandResult.Failure("Speed must be 1x, 3x or 5x.");
            State.Speed = speed;
            return CommandResult.Success("Speed " + speed + "x");
        }

        public void SetPaused(bool paused) { State.Paused = paused; }

        public long? NextArrivalTick()
        {
            long? best = null;
            foreach (var s in State.Ships)
                if (s.Voyage != null && (best == null || s.Voyage.ArriveTick < best.Value)) best = s.Voyage.ArriveTick;
            return best;
        }

        public bool CanSkipToNextArrival { get { return DebugEnabled && NextArrivalTick().HasValue; } }

        /// <summary>Debug-only: jump to the earliest pending arrival, processing every intervening event and every arrival at that tick.</summary>
        public CommandResult SkipToNextArrival()
        {
            if (!DebugAllowed) return CommandResult.Failure("Skip to next arrival is only available in the Editor or a Development Build.");
            if (!DebugEnabled) return CommandResult.Failure("Enable debug mode first.");
            var next = NextArrivalTick();
            if (!next.HasValue) return CommandResult.Failure("Nothing is in flight.");
            AdvanceTo(next.Value);
            State.Notify(NoticeKind.Debug, -1, "Debug: skipped to " + GameTime.FormatDate(next.Value));
            return CommandResult.Success("Skipped to " + GameTime.FormatDate(next.Value));
        }

        /// <summary>
        /// The single event loop. Ordinary play, offline catch-up and debug skips all call this.
        /// Events at the same tick run in a fixed order: arrivals (by ship id), market replenishment, then automated departures (by ship id).
        /// </summary>
        public void AdvanceTo(long target)
        {
            if (target < State.Now) return; // game time never runs backward
            while (true)
            {
                long next = State.NextMarketTick;
                foreach (var s in State.Ships)
                    if (s.Voyage != null && s.Voyage.ArriveTick < next) next = s.Voyage.ArriveTick;
                if (next > target) break;
                State.Now = next;
                ProcessTick(next);
            }
            State.Now = target;
        }

        public void AdvanceBy(long ticks) { if (ticks > 0) AdvanceTo(State.Now + ticks); }

        private void ProcessTick(long tick)
        {
            var arriving = new List<Ship>();
            foreach (var s in State.Ships) if (s.Voyage != null && s.Voyage.ArriveTick == tick) arriving.Add(s);
            arriving.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var s in arriving) SettleArrival(s);

            if (tick == State.NextMarketTick)
            {
                Replenish();
                State.NextMarketTick += Catalog.MarketTickInterval;
            }

            RunAutomation();
        }

        private void Replenish()
        {
            State.MarketTickCount++;
            foreach (var p in State.Supply) p.Available = Math.Min(p.Max, p.Available + p.PerTick);
            foreach (var p in State.Demand) p.Available = Math.Min(Math.Max(0, p.Max - p.Reserved), p.Available + p.PerTick);
            foreach (var p in State.PassengerBookings) p.Available = Math.Min(p.Max, p.Available + p.PerTick);
            if (State.MarketTickCount % 40 == 0)
            {
                foreach (var y in State.Yards)
                {
                    foreach (var h in y.Hulls) if (h.Count < h.MaxCount) h.Count++;
                    foreach (var d in y.Drives) if (d.Count < d.MaxCount) d.Count++;
                }
            }
        }

        // ------------------------------------------------------------------ voyages

        public Quote Quote(VoyagePlan plan) { return Planner.Build(State, plan); }

        /// <summary>One-click provisioning: buy exactly the shortfall for the planned leg at this port's prices.</summary>
        public CommandResult ProvisionFor(VoyagePlan plan)
        {
            var q = Planner.Build(State, plan);
            var ship = State.Ship(plan.ShipId);
            if (ship == null || !ship.Docked) return CommandResult.Failure("Ship must be docked to take on provisions.");
            if (!q.NeedsProvisioning) return new CommandResult { Ok = true, Message = "Already fully provisioned.", Quote = q };
            if (!q.CanAffordProvisioning)
                return new CommandResult { Ok = false, Quote = q, Message = string.Format("Top-up costs {0}; the company has {1}.", Fmt.Credits(q.TopUpCost), Fmt.Credits(State.Credits)) };
            double tankRoom = ship.Hull.TankTonnes - ship.Supplies.Fuel.Quantity;
            if (q.Fuel.TopUp > tankRoom + 1e-9)
                return new CommandResult { Ok = false, Quote = q, Message = string.Format("Tank can take only {0} more fuel; this leg needs {1} more.", Fmt.T(tankRoom), Fmt.T(q.Fuel.TopUp)) };
            ApplyTopUp(ship, q, chargeCredits: true);
            return new CommandResult { Ok = true, Quote = Planner.Build(State, plan), Message = "Provisioned for " + Fmt.Credits(q.TopUpCost) + "." };
        }

        private void ApplyTopUp(Ship ship, Quote q, bool chargeCredits)
        {
            foreach (var line in q.ProvisionLines)
                if (line.TopUp > 0) ship.Supplies.Get(line.Id).Add(line.TopUp, line.TopUpCost);
            if (chargeCredits) State.Credits -= q.TopUpCost;
        }

        /// <summary>
        /// Transactional dispatch: validates everything first, then reserves freight, delivery demand and seats,
        /// buys any shortfall (when autoProvision), locks provisions, manifest, drive and destination. A failure changes nothing.
        /// </summary>
        public CommandResult Dispatch(VoyagePlan plan, bool autoProvision)
        {
            var q = Planner.Build(State, plan);
            if (!q.LoadFeasible)
                return new CommandResult { Ok = false, Quote = q, Message = string.Join(" ", q.Problems.ToArray()) };
            if (q.NeedsProvisioning && !plan.Emergency)
            {
                if (!autoProvision)
                    return new CommandResult { Ok = false, Quote = q, Message = "Provisions are short; provision the ship first (top-up " + Fmt.Credits(q.TopUpCost) + ")." };
                if (!q.CanAffordProvisioning)
                    return new CommandResult { Ok = false, Quote = q, Message = string.Format("Cannot afford provisions: top-up costs {0}, the company has {1}.", Fmt.Credits(q.TopUpCost), Fmt.Credits(State.Credits)) };
            }

            // ---- validated: mutate
            var ship = State.Ship(plan.ShipId);
            ApplyTopUp(ship, q, chargeCredits: !plan.Emergency);

            foreach (var l in q.FreightLines)
            {
                State.SupplyAt(q.Origin, l.Commodity).Available -= l.Tonnes;
                var d = State.DemandAt(q.Destination, l.Commodity);
                d.Available -= l.Tonnes;
                d.Reserved += l.Tonnes;
            }
            int pax = plan.Emergency ? 0 : plan.Passengers;
            if (pax > 0)
            {
                var b = State.Bookings(q.Origin, q.Destination);
                b.Available -= pax;
                b.Reserved += pax;
            }

            foreach (var line in q.ProvisionLines)
            {
                var lot = ship.Supplies.Get(line.Id);
                lot.Quantity -= line.Required;
                lot.CostBasis -= line.ConsumedCostExact;
                if (lot.Quantity < 1e-6) { lot.Quantity = 0; lot.CostBasis = 0; }
                if (lot.CostBasis < 0) lot.CostBasis = 0;
            }

            var v = new Voyage
            {
                Id = State.NextVoyageId++,
                ShipId = ship.Id,
                Origin = q.Origin,
                Destination = q.Destination,
                DepartTick = State.Now,
                ArriveTick = q.ArrivalTick,
                DistanceLy = q.DistanceLy,
                DriveId = ship.DriveId,
                DriveRatingC = ship.Drive.RatingC,
                Freight = q.FreightLines,
                Passengers = pax,
                FarePerPassenger = q.FarePerPassenger,
                FuelUsed = q.Fuel.Required, FoodUsed = q.Food.Required, WaterUsed = q.Water.Required,
                FuelCost = q.Fuel.ConsumedCost, FoodCost = q.Food.ConsumedCost, WaterCost = q.Water.ConsumedCost,
                PortFee = q.PortFee,
                EmergencyAdvance = q.EmergencyAdvance,
                EmergencyPayment = q.EmergencyPayment,
                Automated = plan.Automated,
                Emergency = plan.Emergency,
            };
            ship.Voyage = v;
            ship.WaitingReason = null;
            ship.BlockedReason = null;
            return new CommandResult { Ok = true, Quote = q, Voyage = v, Ship = ship, Message = ship.Name + " departed for " + Galaxy.Get(v.Destination).Name + "." };
        }

        private void SettleArrival(Ship ship)
        {
            var v = ship.Voyage;
            ship.Voyage = null;
            ship.Location = v.Destination;
            if (!State.SettledVoyages.Add(v.Id)) return; // exactly-once: never pay a voyage twice

            foreach (var l in v.Freight)
            {
                var d = State.DemandAt(v.Destination, l.Commodity);
                if (d != null) d.Reserved = Math.Max(0, d.Reserved - l.Tonnes);
            }
            if (v.Passengers > 0)
            {
                var b = State.Bookings(v.Origin, v.Destination);
                if (b != null) b.Reserved = Math.Max(0, b.Reserved - v.Passengers);
            }

            var r = new Receipt
            {
                VoyageId = v.Id, ShipId = ship.Id, ShipName = ship.Name, Origin = v.Origin, Destination = v.Destination,
                DepartTick = v.DepartTick, ArriveTick = v.ArriveTick, Freight = v.Freight, Passengers = v.Passengers,
                FarePerPassenger = v.FarePerPassenger, FareRevenue = v.FareRevenue, FreightRevenue = v.FreightRevenue,
                FuelUsed = v.FuelUsed, FoodUsed = v.FoodUsed, WaterUsed = v.WaterUsed,
                FuelCost = v.FuelCost, FoodCost = v.FoodCost, WaterCost = v.WaterCost, PortFee = v.PortFee,
                EmergencyAdvance = v.EmergencyAdvance, Emergency = v.Emergency,
            };
            State.Credits += r.CashCredited;
            State.LifetimeEarnings += r.NetProfit;
            State.Receipts.Add(r);
            if (State.Receipts.Count > 200) State.Receipts.RemoveRange(0, State.Receipts.Count - 200);

            if (v.Automated && ship.Route != null && ship.Route.Stops.Count > 0)
                ship.Route.CurrentStop = (ship.Route.CurrentStop + 1) % ship.Route.Stops.Count;

            State.Notify(NoticeKind.Arrival, ship.Id, string.Format("{0} arrived at {1}: net {2}.", ship.Name, Galaxy.Get(v.Destination).Name, Fmt.Credits(r.NetProfit)));
            var h = Settled;
            if (h != null) h(r);
        }

        public Receipt LastReceiptFor(int shipId)
        {
            for (int i = State.Receipts.Count - 1; i >= 0; i--) if (State.Receipts[i].ShipId == shipId) return State.Receipts[i];
            return null;
        }

        // ------------------------------------------------------------------ shipyards

        public CommandResult BuyShip(int yardSystemId, string hullId, string name = null)
        {
            var yard = State.Yard(yardSystemId);
            if (yard == null) return CommandResult.Failure(Galaxy.Get(yardSystemId).Name + " has no shipyard.");
            var stock = yard.Hull(hullId);
            if (stock == null) return CommandResult.Failure("This yard does not build that class.");
            if (stock.Count <= 0) return CommandResult.Failure("Out of stock; the yard restocks every two game years.");
            if (State.Credits < stock.Price) return CommandResult.Failure(string.Format("Costs {0}; the company has {1}.", Fmt.Credits(stock.Price), Fmt.Credits(State.Credits)));
            State.Credits -= stock.Price;
            stock.Count--;
            var ship = NewShip(hullId, "alc-150", yardSystemId, name);
            State.Notify(NoticeKind.Purchase, ship.Id, string.Format("Bought {0} ({1}) at {2}.", ship.Name, ship.Hull.ClassName, Galaxy.Get(yardSystemId).Name));
            return new CommandResult { Ok = true, Ship = ship, Message = "Delivered " + ship.Name + " docked at " + Galaxy.Get(yardSystemId).Name + "." };
        }

        private static readonly string[] ShipNames =
        {
            "Wayfarer", "Long Patience", "Halcyon", "Ember Tide", "Quiet Margin", "Second Wind", "Marigold", "Corvid",
            "Tern", "Old Reliable", "Lantern", "Kite", "Juniper", "Sable", "Perihelion", "Ostrava", "Bellwether", "Magpie"
        };

        public Ship NewShip(string hullId, string driveId, int location, string name = null)
        {
            int id = State.NextShipId++;
            var ship = new Ship
            {
                Id = id,
                Name = string.IsNullOrEmpty(name) ? ShipNames[(id - 1) % ShipNames.Length] + ((id - 1) >= ShipNames.Length ? " " + ((id - 1) / ShipNames.Length + 1) : "") : name,
                HullId = hullId,
                DriveId = driveId,
                ColorIndex = (id - 1) % 8,
                Location = location,
            };
            State.Ships.Add(ship);
            return ship;
        }

        /// <summary>Drive installation needs the ship docked at a yard that stocks the drive. Distant ships are rejected.</summary>
        public CommandResult InstallDrive(int shipId, int yardSystemId, string driveId)
        {
            var check = CheckDriveInstall(shipId, yardSystemId, driveId);
            if (!check.Ok) return check;
            var ship = State.Ship(shipId);
            var stock = State.Yard(yardSystemId).Drive(driveId);
            State.Credits -= stock.Price;
            stock.Count--;
            ship.DriveId = driveId;
            State.Notify(NoticeKind.Purchase, ship.Id, string.Format("{0} fitted with {1} at {2}.", ship.Name, Catalog.Drive(driveId).Name, Galaxy.Get(yardSystemId).Name));
            return CommandResult.Success(ship.Name + " now has the " + Catalog.Drive(driveId).Name + ".");
        }

        public CommandResult CheckDriveInstall(int shipId, int yardSystemId, string driveId)
        {
            var ship = State.Ship(shipId);
            if (ship == null) return CommandResult.Failure("Unknown ship.");
            var yard = State.Yard(yardSystemId);
            if (yard == null) return CommandResult.Failure(Galaxy.Get(yardSystemId).Name + " has no shipyard.");
            if (!ship.Docked) return CommandResult.Failure(ship.Name + " is in transit; drives can only be installed while docked at the yard.");
            if (ship.Location != yardSystemId)
                return CommandResult.Failure(string.Format("{0} is docked at {1}; the {2} yard can only work on ships docked there.", ship.Name, Galaxy.Get(ship.Location).Name, Galaxy.Get(yardSystemId).Name));
            var stock = yard.Drive(driveId);
            if (stock == null) return CommandResult.Failure("This yard does not stock that drive.");
            if (stock.Count <= 0) return CommandResult.Failure("Drive out of stock at this yard.");
            if (ship.DriveId == driveId) return CommandResult.Failure("That drive is already installed.");
            if (State.Credits < stock.Price) return CommandResult.Failure(string.Format("Costs {0}; the company has {1}.", Fmt.Credits(stock.Price), Fmt.Credits(State.Credits)));
            return CommandResult.Success("OK");
        }

        /// <summary>Cost and time saved on a known route: the ship's route circuit, else its last voyage, else the given leg.</summary>
        public DriveUpgradePreview PreviewDriveUpgrade(int shipId, int yardSystemId, string driveId, int fallbackDestination = -1)
        {
            var ship = State.Ship(shipId);
            var p = new DriveUpgradePreview();
            var check = CheckDriveInstall(shipId, yardSystemId, driveId);
            p.Allowed = check.Ok;
            p.Reason = check.Ok ? null : check.Message;
            var yard = State.Yard(yardSystemId);
            var stock = yard != null ? yard.Drive(driveId) : null;
            p.Price = stock != null ? stock.Price : 0;
            var legs = new List<int[]>();
            if (ship.Route != null && ship.Route.Stops.Count >= 2)
            {
                for (int i = 0; i < ship.Route.Stops.Count; i++)
                    legs.Add(new[] { ship.Route.Stops[i].SystemId, ship.Route.Stops[(i + 1) % ship.Route.Stops.Count].SystemId });
                p.RouteLabel = "route circuit '" + ship.Route.Name + "'";
            }
            else
            {
                var last = LastReceiptFor(shipId);
                if (last != null) { legs.Add(new[] { last.Origin, last.Destination }); p.RouteLabel = "last voyage " + Galaxy.Get(last.Origin).Name + " to " + Galaxy.Get(last.Destination).Name; }
                else if (fallbackDestination >= 0 && fallbackDestination != ship.Location)
                {
                    legs.Add(new[] { ship.Location, fallbackDestination });
                    p.RouteLabel = Galaxy.Get(ship.Location).Name + " to " + Galaxy.Get(fallbackDestination).Name;
                }
            }
            var oldD = ship.Drive;
            var newD = Catalog.Drive(driveId);
            double mass = ship.Hull.HullMassTonnes + ship.Hull.HoldTonnes;
            foreach (var leg in legs)
            {
                double d = Galaxy.Distance(leg[0], leg[1]);
                p.OldTicks += GameTime.TransitTicks(d, oldD.RatingC);
                p.NewTicks += GameTime.TransitTicks(d, newD.RatingC);
                p.OldFuel += Planner.FuelRequired(d, oldD, mass);
                p.NewFuel += Planner.FuelRequired(d, newD, mass);
            }
            return p;
        }

        // ------------------------------------------------------------------ emergency contract

        /// <summary>True when the company can afford to provision at least one revenue job for this docked ship.</summary>
        public bool CanAffordAnyViableJob(Ship ship)
        {
            if (!ship.Docked) return true;
            foreach (var dest in Galaxy.Systems)
            {
                if (dest.Id == ship.Location) continue;
                foreach (var plan in CandidateMinimalJobs(ship, dest.Id))
                {
                    var q = Planner.Build(State, plan);
                    if (q.LoadFeasible && q.CanAffordProvisioning) return true;
                }
            }
            return false;
        }

        private IEnumerable<VoyagePlan> CandidateMinimalJobs(Ship ship, int dest)
        {
            var book = State.Bookings(ship.Location, dest);
            if (book != null && book.Available > 0 && ship.Hull.Berths > 0)
                yield return new VoyagePlan(ship.Id, dest).WithPassengers(1);
            foreach (var sp in State.Supply)
            {
                if (sp.SystemId != ship.Location || sp.Available <= 0) continue;
                var dp = State.DemandAt(dest, sp.Commodity);
                if (dp != null && dp.Available > 0) yield return new VoyagePlan(ship.Id, dest).WithFreight(sp.Commodity, 1);
            }
        }

        public Quote EmergencyOffer(int shipId)
        {
            var ship = State.Ship(shipId);
            if (ship == null || !ship.Docked || ship.Route != null) return null;
            if (CanAffordAnyViableJob(ship)) return null;
            Quote best = null;
            foreach (var dest in Galaxy.Systems)
            {
                if (dest.Id == ship.Location) continue;
                var q = Planner.Build(State, new VoyagePlan(ship.Id, dest.Id) { Emergency = true });
                if (!q.LoadFeasible) continue;
                if (best == null || q.DistanceLy < best.DistanceLy - 1e-9) best = q;
            }
            return best;
        }

        public CommandResult AcceptEmergency(int shipId)
        {
            var offer = EmergencyOffer(shipId);
            if (offer == null) return CommandResult.Failure("No emergency contract is available: the company can afford a regular job, or the ship is not idle at a port.");
            return Dispatch(offer.Plan, autoProvision: true);
        }

        // ------------------------------------------------------------------ misc

        public CommandResult RenameShip(int shipId, string name)
        {
            var ship = State.Ship(shipId);
            if (ship == null || string.IsNullOrEmpty(name)) return CommandResult.Failure("Invalid name.");
            ship.Name = name.Trim();
            return CommandResult.Success("Renamed.");
        }

        public Vec2 ShipPosition(Ship ship)
        {
            if (ship.Voyage == null) { var s = Galaxy.Get(ship.Location); return new Vec2(s.X, s.Y); }
            var v = ship.Voyage;
            var a = Galaxy.Get(v.Origin);
            var b = Galaxy.Get(v.Destination);
            double t = v.Progress(State.Now);
            return new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        /// <summary>Remaining real seconds to arrival at the current speed (frozen value while paused).</summary>
        public double RealSecondsToArrival(Ship ship)
        {
            if (ship.Voyage == null) return 0;
            return GameTime.RealSecondsFor(Math.Max(0, ship.Voyage.ArriveTick - State.Now), State.Speed);
        }
    }

    public struct Vec2
    {
        public double X, Y;
        public Vec2(double x, double y) { X = x; Y = y; }
    }
}
