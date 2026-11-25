using System;
using System.Collections.Generic;

namespace SpaceTrade.Sim
{
    public sealed class RoleDef
    {
        public readonly string Id;
        public readonly string Title;
        public readonly string[] Industries;
        public readonly string[] Exports;
        public readonly string[] Imports;
        public readonly int Population; // relative passenger weight 1..9

        public RoleDef(string id, string title, string[] industries, string[] exports, string[] imports, int population)
        {
            Id = id; Title = title; Industries = industries; Exports = exports; Imports = imports; Population = population;
        }
    }

    public sealed class StarSystem
    {
        public int Id;
        public string Name;
        public double X, Y; // light-years
        public RoleDef Role;
        public int FuelPrice, FoodPrice, WaterPrice; // cr per tonne
        public bool HasShipyard;

        public double DistanceTo(StarSystem other)
        {
            double dx = X - other.X, dy = Y - other.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public int ProvisionPrice(string provision)
        {
            switch (provision)
            {
                case "fuel": return FuelPrice;
                case "food": return FoodPrice;
                case "water": return WaterPrice;
            }
            throw new ArgumentException(provision);
        }
    }

    public sealed class Galaxy
    {
        public readonly ulong Seed;
        public readonly List<StarSystem> Systems = new List<StarSystem>();
        public int StartSystemId;
        public int TimingTestPartnerId; // exactly 4.5 ly from the start port

        public const double TimingTestDistanceLy = 4.5;

        public static readonly RoleDef Agricultural = new RoleDef("agri", "Agricultural world",
            new[] { "Hydroponic farms", "Grain terraces" }, new[] { "food" }, new[] { "machinery", "electronics" }, 4);
        public static readonly RoleDef Mining = new RoleDef("mining", "Mining colony",
            new[] { "Ore extraction", "Asteroid smelter camps" }, new[] { "ore" }, new[] { "food", "machinery", "medicine" }, 2);
        public static readonly RoleDef Refinery = new RoleDef("refinery", "Refinery station",
            new[] { "Ore refinery", "Alloy foundry" }, new[] { "alloys" }, new[] { "ore", "food" }, 3);
        public static readonly RoleDef Industrial = new RoleDef("industrial", "Industrial hub",
            new[] { "Machine works", "Orbital shipyard" }, new[] { "machinery" }, new[] { "alloys", "food" }, 5);
        public static readonly RoleDef Tech = new RoleDef("tech", "Technology centre",
            new[] { "Chip fabs", "Research campus" }, new[] { "electronics" }, new[] { "alloys", "food" }, 6);
        public static readonly RoleDef Core = new RoleDef("core", "Core world",
            new[] { "Pharmaceutical labs", "Financial district" }, new[] { "medicine" }, new[] { "food", "electronics" }, 9);
        public static readonly RoleDef Frontier = new RoleDef("frontier", "Frontier outpost",
            new[] { "Survey base" }, new string[0], new[] { "food", "medicine", "machinery" }, 1);

        // Order matters: index 0 is the start port, index 1 its 4.5 ly mining partner.
        private static readonly RoleDef[] RoleLayout =
        {
            Agricultural, Mining, Refinery, Industrial, Core, Tech, Mining, Agricultural, Frontier, Refinery
        };

        private static readonly string[] NamePool =
        {
            "Ceres Verdant", "Kallisto Deep", "Hadley Forge", "Vulcan Reach", "Aurelia Prime", "Nyx Array",
            "Tannhauser Gap", "Elysium Fields", "Farpoint", "Brightwater", "Okonkwo's Rest", "Meridian", "Tycho Drift", "Saga Minor"
        };

        private Galaxy(ulong seed) { Seed = seed; }

        public StarSystem Get(int id) { return Systems[id]; }

        public double Distance(int a, int b) { return Systems[a].DistanceTo(Systems[b]); }

        public static Galaxy Generate(ulong seed)
        {
            for (ulong attempt = 0; attempt < 1000; attempt++)
            {
                var g = TryGenerate(seed, attempt);
                if (g != null) return g;
            }
            throw new InvalidOperationException("Could not generate a valid galaxy for seed " + seed);
        }

        private static Galaxy TryGenerate(ulong seed, ulong attempt)
        {
            var rng = new DeterministicRng(seed * 7919UL + attempt * 104729UL + 17UL);
            var g = new Galaxy(seed);
            var names = new List<string>(NamePool);
            const double width = 26, height = 18;

            for (int i = 0; i < RoleLayout.Length; i++)
            {
                double x = 0, y = 0;
                bool placed = false;
                for (int tries = 0; tries < 400 && !placed; tries++)
                {
                    if (i == 0)
                    {
                        x = rng.Range(8.0, 18.0); y = rng.Range(6.0, 12.0);
                    }
                    else if (i == 1)
                    {
                        // 2.7^2 + 3.6^2 = 4.5^2, so the partner sits exactly 4.5 ly away on two-decimal coordinates.
                        double sx = rng.NextDouble() < 0.5 ? -1 : 1, sy = rng.NextDouble() < 0.5 ? -1 : 1;
                        bool swap = rng.NextDouble() < 0.5;
                        x = g.Systems[0].X + sx * (swap ? 3.6 : 2.7);
                        y = g.Systems[0].Y + sy * (swap ? 2.7 : 3.6);
                    }
                    else
                    {
                        x = rng.Range(0.0, width); y = rng.Range(0.0, height);
                    }
                    if (x < 0 || x > width || y < 0 || y > height) continue;
                    placed = true;
                    foreach (var s in g.Systems)
                    {
                        double dx = s.X - x, dy = s.Y - y;
                        if (Math.Sqrt(dx * dx + dy * dy) < 3.0) { placed = false; break; }
                    }
                }
                if (!placed) return null;

                int nameIdx = rng.Range(0, names.Count);
                var sys = new StarSystem
                {
                    Id = i,
                    Name = names[nameIdx],
                    X = i == 1 ? x : Math.Round(x, 2),
                    Y = i == 1 ? y : Math.Round(y, 2),
                    Role = RoleLayout[i],
                    FuelPrice = rng.Range(80, 131),
                    FoodPrice = RoleLayout[i] == Agricultural ? rng.Range(30, 40) : rng.Range(50, 81),
                    WaterPrice = rng.Range(8, 26),
                    HasShipyard = RoleLayout[i] == Core || RoleLayout[i] == Industrial || RoleLayout[i] == Tech,
                };
                names.RemoveAt(nameIdx);
                g.Systems.Add(sys);
            }

            g.StartSystemId = 0;
            g.TimingTestPartnerId = 1;
            if (!g.IsPlayable()) return null;
            return g;
        }

        /// <summary>Every system can trade with at least one partner within contract range, and the network is connected.</summary>
        private bool IsPlayable()
        {
            foreach (var s in Systems)
            {
                bool hasPartner = false;
                foreach (var o in Systems)
                {
                    if (o == s || s.DistanceTo(o) > Catalog.MaxContractRangeLy) continue;
                    if (Trades(s.Role, o.Role) || Trades(o.Role, s.Role)) { hasPartner = true; break; }
                }
                if (!hasPartner) return false;
            }
            var seen = new HashSet<int> { 0 };
            var queue = new Queue<int>();
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                var cur = Systems[queue.Dequeue()];
                foreach (var o in Systems)
                    if (!seen.Contains(o.Id) && cur.DistanceTo(o) <= 10.0) { seen.Add(o.Id); queue.Enqueue(o.Id); }
            }
            if (seen.Count != Systems.Count) return false;
            // Start port must have at least two food destinations so the opening offers a choice.
            int foodDest = 0;
            foreach (var o in Systems)
                if (o.Id != 0 && Array.IndexOf(o.Role.Imports, "food") >= 0 && Systems[0].DistanceTo(o) <= 9.0) foodDest++;
            return foodDest >= 2;
        }

        private static bool Trades(RoleDef from, RoleDef to)
        {
            foreach (var e in from.Exports) if (Array.IndexOf(to.Imports, e) >= 0) return true;
            return false;
        }
    }
}
