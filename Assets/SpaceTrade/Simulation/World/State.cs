using System;
using System.Collections.Generic;

namespace SpaceTrade.Sim
{
    public sealed class SupplyPool
    {
        public int SystemId;
        public string Commodity;
        public int Available, Max, PerTick;
    }

    public sealed class DemandPool
    {
        public int SystemId;
        public string Commodity;
        public int Available, Reserved, Max, PerTick;
    }

    public sealed class PassengerPool
    {
        public int Origin, Destination;
        public int Available, Reserved, Max, PerTick;
    }

    public sealed class YardHullStock
    {
        public string HullId;
        public int Count, MaxCount;
        public long Price;
    }

    public sealed class YardDriveStock
    {
        public string DriveId;
        public int Count, MaxCount;
        public long Price;
    }

    public sealed class Shipyard
    {
        public int SystemId;
        public List<YardHullStock> Hulls = new List<YardHullStock>();
        public List<YardDriveStock> Drives = new List<YardDriveStock>();

        public YardHullStock Hull(string id) { foreach (var h in Hulls) if (h.HullId == id) return h; return null; }
        public YardDriveStock Drive(string id) { foreach (var d in Drives) if (d.DriveId == id) return d; return null; }
    }

    /// <summary>A quantity of one shipboard provision with its total acquisition cost (weighted-average cost basis).</summary>
    public sealed class SupplyLot
    {
        public double Quantity;
        public double CostBasis;

        public double AverageCost { get { return Quantity > 1e-9 ? CostBasis / Quantity : 0; } }

        public SupplyLot Clone() { return new SupplyLot { Quantity = Quantity, CostBasis = CostBasis }; }

        public void Add(double qty, double totalCost) { Quantity += qty; CostBasis += totalCost; }

        /// <summary>Removes qty and returns its share of the cost basis.</summary>
        public double Remove(double qty)
        {
            if (qty <= 0) return 0;
            if (qty >= Quantity - 1e-9)
            {
                double all = CostBasis;
                Quantity = 0; CostBasis = 0;
                return all;
            }
            double cost = CostBasis * (qty / Quantity);
            Quantity -= qty; CostBasis -= cost;
            return cost;
        }
    }

    public sealed class Supplies
    {
        public SupplyLot Fuel = new SupplyLot();
        public SupplyLot Food = new SupplyLot();
        public SupplyLot Water = new SupplyLot();

        public SupplyLot Get(string id)
        {
            switch (id) { case "fuel": return Fuel; case "food": return Food; case "water": return Water; }
            throw new ArgumentException(id);
        }

        public Supplies Clone() { return new Supplies { Fuel = Fuel.Clone(), Food = Food.Clone(), Water = Water.Clone() }; }
    }

    public sealed class FreightLine
    {
        public string Commodity;
        public int Tonnes;
        public int RatePerTonne;
        public long Amount { get { return (long)Tonnes * RatePerTonne; } }
    }

    public sealed class Voyage
    {
        public long Id;
        public int ShipId;
        public int Origin, Destination;
        public long DepartTick, ArriveTick;
        public double DistanceLy;
        public string DriveId;
        public double DriveRatingC;
        public List<FreightLine> Freight = new List<FreightLine>();
        public int Passengers;
        public int FarePerPassenger;
        public double FuelUsed, FoodUsed, WaterUsed;
        public long FuelCost, FoodCost, WaterCost; // acquisition cost of the provisions this voyage consumes
        public long PortFee;
        public long EmergencyAdvance;  // value of provisions supplied in kind, deducted from payment
        public long EmergencyPayment;  // contract payment for an emergency leg (replaces freight/fares)
        public bool Automated;
        public bool Emergency;

        public long FreightRevenue { get { long s = 0; foreach (var f in Freight) s += f.Amount; return s + EmergencyPayment; } }
        public long FareRevenue { get { return (long)Passengers * FarePerPassenger; } }
        public long SuppliesCost { get { return FuelCost + FoodCost + WaterCost; } }
        public int FreightTonnes { get { int s = 0; foreach (var f in Freight) s += f.Tonnes; return s; } }

        public double Progress(long now)
        {
            if (ArriveTick <= DepartTick) return 1;
            double p = (now - DepartTick) / (double)(ArriveTick - DepartTick);
            return p < 0 ? 0 : (p > 1 ? 1 : p);
        }
    }

    public sealed class Receipt
    {
        public long VoyageId;
        public int ShipId;
        public string ShipName;
        public int Origin, Destination;
        public long DepartTick, ArriveTick;
        public List<FreightLine> Freight = new List<FreightLine>();
        public int Passengers;
        public int FarePerPassenger;
        public long FareRevenue, FreightRevenue;
        public double FuelUsed, FoodUsed, WaterUsed;
        public long FuelCost, FoodCost, WaterCost, PortFee, EmergencyAdvance;
        public bool Emergency;

        public long GrossRevenue { get { return FareRevenue + FreightRevenue; } }
        public long SuppliesCost { get { return FuelCost + FoodCost + WaterCost; } }
        public long NetProfit { get { return GrossRevenue - SuppliesCost - PortFee; } }
        /// <summary>Cash actually credited at arrival (supplies were paid when bought or advanced in kind).</summary>
        public long CashCredited { get { return GrossRevenue - PortFee - EmergencyAdvance; } }
        public long ElapsedTicks { get { return ArriveTick - DepartTick; } }
    }

    public enum LoadUnit { Tonnes, Passengers }

    public sealed class RouteStop
    {
        public int SystemId;
        public List<string> Commodities = new List<string>(); // freight to load here for the next stop
        public bool Passengers;
        public bool AutoProvision = true;
        public int MinLoad;
        public LoadUnit MinLoadUnit = LoadUnit.Tonnes;
        public bool EmptyLeg; // explicit repositioning: depart with nothing aboard but crew

        public RouteStop Clone()
        {
            return new RouteStop
            {
                SystemId = SystemId, Commodities = new List<string>(Commodities), Passengers = Passengers,
                AutoProvision = AutoProvision, MinLoad = MinLoad, MinLoadUnit = MinLoadUnit, EmptyLeg = EmptyLeg
            };
        }
    }

    public sealed class RouteOrder
    {
        public string Name = "Route";
        public List<RouteStop> Stops = new List<RouteStop>();
        public int CurrentStop; // index of the stop the ship is at (docked) or departed from (in transit)

        public RouteOrder Clone()
        {
            var r = new RouteOrder { Name = Name, CurrentStop = CurrentStop };
            foreach (var s in Stops) r.Stops.Add(s.Clone());
            return r;
        }
    }

    public enum ShipStatus { Idle, InTransit, Waiting, Blocked }

    public sealed class Ship
    {
        public int Id;
        public string Name;
        public string HullId;
        public string DriveId;
        public int ColorIndex;
        public int Location; // system id when docked; origin while in transit
        public Supplies Supplies = new Supplies();
        public Voyage Voyage;
        public RouteOrder Route;
        public string WaitingReason;
        public string BlockedReason;

        public HullDef Hull { get { return Catalog.Hull(HullId); } }
        public DriveDef Drive { get { return Catalog.Drive(DriveId); } }
        public bool Docked { get { return Voyage == null; } }

        public ShipStatus Status
        {
            get
            {
                if (Voyage != null) return ShipStatus.InTransit;
                if (!string.IsNullOrEmpty(BlockedReason)) return ShipStatus.Blocked;
                if (!string.IsNullOrEmpty(WaitingReason)) return ShipStatus.Waiting;
                return ShipStatus.Idle;
            }
        }
    }

    public enum NoticeKind { Info, Arrival, Blocked, Waiting, Purchase, Debug }

    public sealed class Notification
    {
        public long Tick;
        public NoticeKind Kind;
        public int ShipId = -1;
        public string Text;
    }

    public sealed class GameState
    {
        public const int SaveVersion = 1;

        public ulong Seed;
        public Galaxy Galaxy;
        public long Now;
        public int Speed = 1;
        public bool Paused;
        public long Credits;
        public long NextMarketTick = Catalog.MarketTickInterval;
        public long MarketTickCount;
        public int NextShipId = 1;
        public long NextVoyageId = 1;
        public long LastWallClockUtcTicks; // DateTime.UtcNow.Ticks at last save/catch-up; never moves backward
        public long LifetimeEarnings;
        public string LastCatchUpSummary;
        public bool IsDemo;

        public List<SupplyPool> Supply = new List<SupplyPool>();
        public List<DemandPool> Demand = new List<DemandPool>();
        public List<PassengerPool> PassengerBookings = new List<PassengerPool>();
        public List<Shipyard> Yards = new List<Shipyard>();
        public List<Ship> Ships = new List<Ship>();
        public List<Notification> Notifications = new List<Notification>();
        public List<Receipt> Receipts = new List<Receipt>();
        public HashSet<long> SettledVoyages = new HashSet<long>();

        public Ship Ship(int id) { foreach (var s in Ships) if (s.Id == id) return s; return null; }
        public Shipyard Yard(int systemId) { foreach (var y in Yards) if (y.SystemId == systemId) return y; return null; }

        public SupplyPool SupplyAt(int system, string commodity)
        {
            foreach (var p in Supply) if (p.SystemId == system && p.Commodity == commodity) return p;
            return null;
        }

        public DemandPool DemandAt(int system, string commodity)
        {
            foreach (var p in Demand) if (p.SystemId == system && p.Commodity == commodity) return p;
            return null;
        }

        public PassengerPool Bookings(int origin, int dest)
        {
            foreach (var p in PassengerBookings) if (p.Origin == origin && p.Destination == dest) return p;
            return null;
        }

        public void Notify(NoticeKind kind, int shipId, string text)
        {
            Notifications.Add(new Notification { Tick = Now, Kind = kind, ShipId = shipId, Text = text });
            if (Notifications.Count > 300) Notifications.RemoveRange(0, Notifications.Count - 300);
        }
    }
}
