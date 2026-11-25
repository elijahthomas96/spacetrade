using System;
using System.IO;
using NUnit.Framework;
using SpaceTrade.Sim;

namespace SpaceTrade.Tests
{
    internal static class TestWorld
    {
        public static readonly DateTime Epoch = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

        public static Simulation Fresh(bool debugAllowed = false)
        {
            return new Simulation(Scenarios.NewGame(Scenarios.DefaultSeed, Epoch), debugAllowed);
        }

        public static Ship Starter(Simulation sim) { return sim.State.Ships[0]; }

        public static int Start(Simulation sim) { return sim.Galaxy.StartSystemId; }

        /// <summary>The mining colony exactly 4.5 ly from the start port; it imports food.</summary>
        public static int Mine(Simulation sim) { return sim.Galaxy.TimingTestPartnerId; }

        public static int OtherFoodImporter(Simulation sim)
        {
            int start = Start(sim), best = -1;
            double bestD = double.MaxValue;
            foreach (var s in sim.Galaxy.Systems)
            {
                if (s.Id == start || s.Id == Mine(sim) || Array.IndexOf(s.Role.Imports, "food") < 0) continue;
                double d = sim.Galaxy.Distance(start, s.Id);
                if (d < bestD) { bestD = d; best = s.Id; }
            }
            return best;
        }

        /// <summary>Canonical comparison form of a state: everything except wall-clock bookkeeping and notification text.</summary>
        public static string Fingerprint(GameState s)
        {
            var root = JsonNode.Parse(SaveSerializer.ToJson(s));
            var copy = JsonNode.Object();
            foreach (var key in new[] { "seed", "now", "speed", "paused", "credits", "nextMarketTick", "marketTickCount", "nextShipId",
                "nextVoyageId", "lifetimeEarnings", "supply", "demand", "passengers", "yards", "ships", "receipts", "settledVoyages" })
                copy.Set(key, root[key]);
            return copy.ToJson(true);
        }

        public static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "spacetrade-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static void AssertOk(CommandResult r)
        {
            Assert.IsTrue(r.Ok, r.Message);
        }

        public static void AssertPoolsSane(GameState s)
        {
            foreach (var p in s.Supply) Assert.That(p.Available, Is.InRange(0, p.Max), "supply " + p.Commodity + "@" + p.SystemId);
            foreach (var p in s.Demand)
            {
                Assert.GreaterOrEqual(p.Available, 0, "demand available");
                Assert.GreaterOrEqual(p.Reserved, 0, "demand reserved");
            }
            foreach (var p in s.PassengerBookings)
            {
                Assert.GreaterOrEqual(p.Available, 0);
                Assert.GreaterOrEqual(p.Reserved, 0);
            }
            Assert.GreaterOrEqual(s.Credits, 0, "credits never negative");
        }
    }
}
