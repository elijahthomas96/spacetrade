using System;
using System.Collections.Generic;

namespace SpaceTrade.Sim
{
    public sealed class CommodityDef
    {
        public readonly string Id;
        public readonly string Name;
        public readonly int BaseRate;   // cr per tonne, flat
        public readonly int RatePerLy;  // cr per tonne per light-year

        public CommodityDef(string id, string name, int baseRate, int ratePerLy)
        {
            Id = id; Name = name; BaseRate = baseRate; RatePerLy = ratePerLy;
        }

        public int ContractRate(double distanceLy)
        {
            return BaseRate + (int)Math.Round(RatePerLy * distanceLy);
        }
    }

    public sealed class DriveDef
    {
        public readonly string Id;
        public readonly string Name;
        public readonly double RatingC;
        /// <summary>Fuel tonnes burned per light-year per 100 t of loaded mass.</summary>
        public readonly double FuelPerLyPer100t;

        public DriveDef(string id, string name, double ratingC, double fuelPerLyPer100t)
        {
            Id = id; Name = name; RatingC = ratingC; FuelPerLyPer100t = fuelPerLyPer100t;
        }
    }

    public sealed class HullDef
    {
        public readonly string Id;
        public readonly string ClassName;
        public readonly string Role;
        public readonly double HoldTonnes;
        public readonly double TankTonnes;
        public readonly int Berths;
        public readonly int Crew;
        public readonly double HullMassTonnes;

        public HullDef(string id, string className, string role, double hold, double tank, int berths, int crew, double hullMass)
        {
            Id = id; ClassName = className; Role = role; HoldTonnes = hold; TankTonnes = tank;
            Berths = berths; Crew = crew; HullMassTonnes = hullMass;
        }
    }

    /// <summary>All tuning constants and static definitions. Units: tonnes, light-years, c, credits, game years.</summary>
    public static class Catalog
    {
        public const double FoodPerPersonYear = 0.25;   // t
        public const double WaterPerPersonYear = 0.15;  // t of make-up water; the rest is recycled aboard
        public const double PassengerMassTonnes = 0.1;  // person + luggage, carried in berths
        public const long PortFee = 200;                // cr per arrival
        public const int PassengerBaseFare = 150;       // cr
        public const int PassengerFarePerLy = 260;      // cr per ly
        public const double MaxContractRangeLy = 12.0;
        public const long MarketTickInterval = GameTime.TicksPerYear / 20; // 0.05 yr
        public const long StartingCredits = 6000;
        public const long EmergencyMarginCredits = 1500;

        public static readonly CommodityDef[] Commodities =
        {
            new CommodityDef("ore", "Ore", 30, 38),
            new CommodityDef("alloys", "Alloys", 45, 50),
            new CommodityDef("machinery", "Machinery", 60, 58),
            new CommodityDef("food", "Foodstuffs", 40, 45),
            new CommodityDef("electronics", "Electronics", 80, 66),
            new CommodityDef("medicine", "Medicines", 90, 72),
        };

        public static readonly DriveDef[] Drives =
        {
            new DriveDef("alc-150", "Alcubierre Mk I (1.5c)", 1.5, 1.6),
            new DriveDef("alc-225", "Alcubierre Mk II (2.25c)", 2.25, 2.0),
            new DriveDef("alc-375", "Alcubierre Mk III (3.75c)", 3.75, 2.8),
        };

        public static readonly HullDef[] Hulls =
        {
            new HullDef("courier", "Kestrel-class courier", "Fast mixed loads, small hold, few berths", 60, 40, 6, 2, 80),
            new HullDef("freighter", "Ox-class bulk freighter", "Large hold for bulk freight, no berths", 400, 180, 0, 4, 320),
            new HullDef("liner", "Heron-class passenger liner", "Many berths for passenger services", 200, 160, 60, 6, 260),
        };

        public static readonly string[] Provisions = { "fuel", "food", "water" };

        public static CommodityDef Commodity(string id)
        {
            foreach (var c in Commodities) if (c.Id == id) return c;
            throw new KeyNotFoundException("Unknown commodity " + id);
        }

        public static DriveDef Drive(string id)
        {
            foreach (var d in Drives) if (d.Id == id) return d;
            throw new KeyNotFoundException("Unknown drive " + id);
        }

        public static HullDef Hull(string id)
        {
            foreach (var h in Hulls) if (h.Id == id) return h;
            throw new KeyNotFoundException("Unknown hull " + id);
        }
    }
}
