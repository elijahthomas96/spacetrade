using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SpaceTrade.Sim
{
    /// <summary>Versioned JSON serialization of the full game state. The galaxy itself is regenerated from its seed.</summary>
    public static class SaveSerializer
    {
        public static string ToJson(GameState s)
        {
            var root = JsonNode.Object();
            root.Set("format", "spacetrade-save");
            root.Set("version", GameState.SaveVersion);
            root.Set("seed", s.Seed.ToString(CultureInfo.InvariantCulture));
            root.Set("now", s.Now);
            root.Set("speed", s.Speed);
            root.Set("paused", s.Paused);
            root.Set("credits", s.Credits);
            root.Set("nextMarketTick", s.NextMarketTick);
            root.Set("marketTickCount", s.MarketTickCount);
            root.Set("nextShipId", s.NextShipId);
            root.Set("nextVoyageId", s.NextVoyageId);
            root.Set("lastWallClockUtcTicks", s.LastWallClockUtcTicks);
            root.Set("lifetimeEarnings", s.LifetimeEarnings);
            root.Set("lastCatchUpSummary", JsonNode.From(s.LastCatchUpSummary));
            root.Set("isDemo", s.IsDemo);

            var supply = JsonNode.Array();
            foreach (var p in s.Supply)
                supply.Add(JsonNode.Object().Set("sys", p.SystemId).Set("c", p.Commodity).Set("avail", p.Available).Set("max", p.Max).Set("perTick", p.PerTick));
            root.Set("supply", supply);

            var demand = JsonNode.Array();
            foreach (var p in s.Demand)
                demand.Add(JsonNode.Object().Set("sys", p.SystemId).Set("c", p.Commodity).Set("avail", p.Available).Set("reserved", p.Reserved).Set("max", p.Max).Set("perTick", p.PerTick));
            root.Set("demand", demand);

            var pax = JsonNode.Array();
            foreach (var p in s.PassengerBookings)
                pax.Add(JsonNode.Object().Set("from", p.Origin).Set("to", p.Destination).Set("avail", p.Available).Set("reserved", p.Reserved).Set("max", p.Max).Set("perTick", p.PerTick));
            root.Set("passengers", pax);

            var yards = JsonNode.Array();
            foreach (var y in s.Yards)
            {
                var hulls = JsonNode.Array();
                foreach (var h in y.Hulls) hulls.Add(JsonNode.Object().Set("id", h.HullId).Set("count", h.Count).Set("max", h.MaxCount).Set("price", h.Price));
                var drives = JsonNode.Array();
                foreach (var d in y.Drives) drives.Add(JsonNode.Object().Set("id", d.DriveId).Set("count", d.Count).Set("max", d.MaxCount).Set("price", d.Price));
                yards.Add(JsonNode.Object().Set("sys", y.SystemId).Set("hulls", hulls).Set("drives", drives));
            }
            root.Set("yards", yards);

            var ships = JsonNode.Array();
            foreach (var sh in s.Ships) ships.Add(WriteShip(sh));
            root.Set("ships", ships);

            var receipts = JsonNode.Array();
            foreach (var r in s.Receipts) receipts.Add(WriteReceipt(r));
            root.Set("receipts", receipts);

            var settled = new List<long>(s.SettledVoyages);
            settled.Sort();
            var settledNode = JsonNode.Array();
            foreach (var id in settled) settledNode.Add(JsonNode.From(id));
            root.Set("settledVoyages", settledNode);

            var notes = JsonNode.Array();
            foreach (var n in s.Notifications)
                notes.Add(JsonNode.Object().Set("tick", n.Tick).Set("kind", n.Kind.ToString()).Set("ship", n.ShipId).Set("text", n.Text));
            root.Set("notifications", notes);
            return root.ToJson(true);
        }

        private static JsonNode WriteLot(SupplyLot l) { return JsonNode.Object().Set("qty", l.Quantity).Set("cost", l.CostBasis); }

        private static JsonNode WriteFreight(List<FreightLine> lines)
        {
            var a = JsonNode.Array();
            foreach (var f in lines) a.Add(JsonNode.Object().Set("c", f.Commodity).Set("t", f.Tonnes).Set("rate", f.RatePerTonne));
            return a;
        }

        private static JsonNode WriteShip(Ship sh)
        {
            var o = JsonNode.Object()
                .Set("id", sh.Id).Set("name", sh.Name).Set("hull", sh.HullId).Set("drive", sh.DriveId)
                .Set("color", sh.ColorIndex).Set("location", sh.Location)
                .Set("fuel", WriteLot(sh.Supplies.Fuel)).Set("food", WriteLot(sh.Supplies.Food)).Set("water", WriteLot(sh.Supplies.Water))
                .Set("waiting", JsonNode.From(sh.WaitingReason)).Set("blocked", JsonNode.From(sh.BlockedReason));
            if (sh.Voyage != null)
            {
                var v = sh.Voyage;
                o.Set("voyage", JsonNode.Object()
                    .Set("id", v.Id).Set("origin", v.Origin).Set("dest", v.Destination)
                    .Set("depart", v.DepartTick).Set("arrive", v.ArriveTick).Set("distance", v.DistanceLy)
                    .Set("drive", v.DriveId).Set("rating", v.DriveRatingC)
                    .Set("freight", WriteFreight(v.Freight)).Set("pax", v.Passengers).Set("fare", v.FarePerPassenger)
                    .Set("fuelUsed", v.FuelUsed).Set("foodUsed", v.FoodUsed).Set("waterUsed", v.WaterUsed)
                    .Set("fuelCost", v.FuelCost).Set("foodCost", v.FoodCost).Set("waterCost", v.WaterCost)
                    .Set("portFee", v.PortFee).Set("emAdvance", v.EmergencyAdvance).Set("emPayment", v.EmergencyPayment)
                    .Set("automated", v.Automated).Set("emergency", v.Emergency));
            }
            else o.Set("voyage", JsonNode.Null());
            if (sh.Route != null)
            {
                var stops = JsonNode.Array();
                foreach (var st in sh.Route.Stops)
                {
                    var cs = JsonNode.Array();
                    foreach (var c in st.Commodities) cs.Add(JsonNode.From(c));
                    stops.Add(JsonNode.Object().Set("sys", st.SystemId).Set("commodities", cs).Set("pax", st.Passengers)
                        .Set("autoProvision", st.AutoProvision).Set("minLoad", st.MinLoad).Set("unit", st.MinLoadUnit.ToString()).Set("empty", st.EmptyLeg));
                }
                o.Set("route", JsonNode.Object().Set("name", sh.Route.Name).Set("current", sh.Route.CurrentStop).Set("stops", stops));
            }
            else o.Set("route", JsonNode.Null());
            return o;
        }

        private static JsonNode WriteReceipt(Receipt r)
        {
            return JsonNode.Object()
                .Set("voyage", r.VoyageId).Set("ship", r.ShipId).Set("shipName", r.ShipName).Set("origin", r.Origin).Set("dest", r.Destination)
                .Set("depart", r.DepartTick).Set("arrive", r.ArriveTick).Set("freight", WriteFreight(r.Freight))
                .Set("pax", r.Passengers).Set("fare", r.FarePerPassenger).Set("fareRevenue", r.FareRevenue).Set("freightRevenue", r.FreightRevenue)
                .Set("fuelUsed", r.FuelUsed).Set("foodUsed", r.FoodUsed).Set("waterUsed", r.WaterUsed)
                .Set("fuelCost", r.FuelCost).Set("foodCost", r.FoodCost).Set("waterCost", r.WaterCost)
                .Set("portFee", r.PortFee).Set("emAdvance", r.EmergencyAdvance).Set("emergency", r.Emergency);
        }

        public static GameState FromJson(string json)
        {
            var root = JsonNode.Parse(json);
            if (root.GetString("format", "") != "spacetrade-save") throw new FormatException("Not a Space Trade save file.");
            int version = root["version"].AsInt();
            if (version > GameState.SaveVersion) throw new FormatException("Save version " + version + " is newer than this game supports (" + GameState.SaveVersion + ").");
            if (version < 1) throw new FormatException("Unsupported save version " + version);

            var s = new GameState();
            s.Seed = ulong.Parse(root["seed"].AsString(), CultureInfo.InvariantCulture);
            s.Galaxy = Galaxy.Generate(s.Seed);
            s.Now = root["now"].AsLong();
            s.Speed = root["speed"].AsInt();
            if (!GameTime.IsAllowedSpeed(s.Speed)) s.Speed = 1;
            s.Paused = root["paused"].AsBool();
            s.Credits = root["credits"].AsLong();
            s.NextMarketTick = root["nextMarketTick"].AsLong();
            s.MarketTickCount = root.GetLong("marketTickCount", 0);
            s.NextShipId = root["nextShipId"].AsInt();
            s.NextVoyageId = root["nextVoyageId"].AsLong();
            s.LastWallClockUtcTicks = root["lastWallClockUtcTicks"].AsLong();
            s.LifetimeEarnings = root.GetLong("lifetimeEarnings", 0);
            s.LastCatchUpSummary = root.GetString("lastCatchUpSummary", null);
            s.IsDemo = root.GetBool("isDemo", false);

            foreach (var p in root["supply"].Items)
                s.Supply.Add(new SupplyPool { SystemId = p["sys"].AsInt(), Commodity = p["c"].AsString(), Available = p["avail"].AsInt(), Max = p["max"].AsInt(), PerTick = p["perTick"].AsInt() });
            foreach (var p in root["demand"].Items)
                s.Demand.Add(new DemandPool { SystemId = p["sys"].AsInt(), Commodity = p["c"].AsString(), Available = p["avail"].AsInt(), Reserved = p["reserved"].AsInt(), Max = p["max"].AsInt(), PerTick = p["perTick"].AsInt() });
            foreach (var p in root["passengers"].Items)
                s.PassengerBookings.Add(new PassengerPool { Origin = p["from"].AsInt(), Destination = p["to"].AsInt(), Available = p["avail"].AsInt(), Reserved = p["reserved"].AsInt(), Max = p["max"].AsInt(), PerTick = p["perTick"].AsInt() });
            foreach (var y in root["yards"].Items)
            {
                var yard = new Shipyard { SystemId = y["sys"].AsInt() };
                foreach (var h in y["hulls"].Items) yard.Hulls.Add(new YardHullStock { HullId = h["id"].AsString(), Count = h["count"].AsInt(), MaxCount = h["max"].AsInt(), Price = h["price"].AsLong() });
                foreach (var d in y["drives"].Items) yard.Drives.Add(new YardDriveStock { DriveId = d["id"].AsString(), Count = d["count"].AsInt(), MaxCount = d["max"].AsInt(), Price = d["price"].AsLong() });
                s.Yards.Add(yard);
            }
            foreach (var sh in root["ships"].Items) s.Ships.Add(ReadShip(sh));
            foreach (var r in root["receipts"].Items) s.Receipts.Add(ReadReceipt(r));
            foreach (var id in root["settledVoyages"].Items) s.SettledVoyages.Add(id.AsLong());
            foreach (var n in root["notifications"].Items)
            {
                NoticeKind kind;
                if (!Enum.TryParse(n["kind"].AsString(), out kind)) kind = NoticeKind.Info;
                s.Notifications.Add(new Notification { Tick = n["tick"].AsLong(), Kind = kind, ShipId = n["ship"].AsInt(), Text = n["text"].AsString() });
            }
            return s;
        }

        private static SupplyLot ReadLot(JsonNode n) { return new SupplyLot { Quantity = n["qty"].AsDouble(), CostBasis = n["cost"].AsDouble() }; }

        private static List<FreightLine> ReadFreight(JsonNode a)
        {
            var list = new List<FreightLine>();
            foreach (var f in a.Items) list.Add(new FreightLine { Commodity = f["c"].AsString(), Tonnes = f["t"].AsInt(), RatePerTonne = f["rate"].AsInt() });
            return list;
        }

        private static Ship ReadShip(JsonNode o)
        {
            var sh = new Ship
            {
                Id = o["id"].AsInt(), Name = o["name"].AsString(), HullId = o["hull"].AsString(), DriveId = o["drive"].AsString(),
                ColorIndex = o["color"].AsInt(), Location = o["location"].AsInt(),
                WaitingReason = o["waiting"].AsString(), BlockedReason = o["blocked"].AsString(),
            };
            sh.Supplies.Fuel = ReadLot(o["fuel"]);
            sh.Supplies.Food = ReadLot(o["food"]);
            sh.Supplies.Water = ReadLot(o["water"]);
            var v = o["voyage"];
            if (v.Kind != JsonKind.Null)
            {
                sh.Voyage = new Voyage
                {
                    Id = v["id"].AsLong(), ShipId = sh.Id, Origin = v["origin"].AsInt(), Destination = v["dest"].AsInt(),
                    DepartTick = v["depart"].AsLong(), ArriveTick = v["arrive"].AsLong(), DistanceLy = v["distance"].AsDouble(),
                    DriveId = v["drive"].AsString(), DriveRatingC = v["rating"].AsDouble(), Freight = ReadFreight(v["freight"]),
                    Passengers = v["pax"].AsInt(), FarePerPassenger = v["fare"].AsInt(),
                    FuelUsed = v["fuelUsed"].AsDouble(), FoodUsed = v["foodUsed"].AsDouble(), WaterUsed = v["waterUsed"].AsDouble(),
                    FuelCost = v["fuelCost"].AsLong(), FoodCost = v["foodCost"].AsLong(), WaterCost = v["waterCost"].AsLong(),
                    PortFee = v["portFee"].AsLong(), EmergencyAdvance = v["emAdvance"].AsLong(), EmergencyPayment = v["emPayment"].AsLong(),
                    Automated = v["automated"].AsBool(), Emergency = v["emergency"].AsBool(),
                };
            }
            var r = o["route"];
            if (r.Kind != JsonKind.Null)
            {
                var route = new RouteOrder { Name = r["name"].AsString(), CurrentStop = r["current"].AsInt() };
                foreach (var st in r["stops"].Items)
                {
                    var stop = new RouteStop
                    {
                        SystemId = st["sys"].AsInt(), Passengers = st["pax"].AsBool(), AutoProvision = st["autoProvision"].AsBool(),
                        MinLoad = st["minLoad"].AsInt(), EmptyLeg = st["empty"].AsBool(),
                        MinLoadUnit = st["unit"].AsString() == "Passengers" ? LoadUnit.Passengers : LoadUnit.Tonnes,
                    };
                    foreach (var c in st["commodities"].Items) stop.Commodities.Add(c.AsString());
                    route.Stops.Add(stop);
                }
                sh.Route = route;
            }
            return sh;
        }

        private static Receipt ReadReceipt(JsonNode r)
        {
            return new Receipt
            {
                VoyageId = r["voyage"].AsLong(), ShipId = r["ship"].AsInt(), ShipName = r["shipName"].AsString(),
                Origin = r["origin"].AsInt(), Destination = r["dest"].AsInt(), DepartTick = r["depart"].AsLong(), ArriveTick = r["arrive"].AsLong(),
                Freight = ReadFreight(r["freight"]), Passengers = r["pax"].AsInt(), FarePerPassenger = r["fare"].AsInt(),
                FareRevenue = r["fareRevenue"].AsLong(), FreightRevenue = r["freightRevenue"].AsLong(),
                FuelUsed = r["fuelUsed"].AsDouble(), FoodUsed = r["foodUsed"].AsDouble(), WaterUsed = r["waterUsed"].AsDouble(),
                FuelCost = r["fuelCost"].AsLong(), FoodCost = r["foodCost"].AsLong(), WaterCost = r["waterCost"].AsLong(),
                PortFee = r["portFee"].AsLong(), EmergencyAdvance = r["emAdvance"].AsLong(), Emergency = r["emergency"].AsBool(),
            };
        }
    }

    /// <summary>Local save slots in a directory (Unity passes Application.persistentDataPath). Writes are atomic.</summary>
    public sealed class SaveStore
    {
        public const string MainSlot = "campaign";
        public const string DemoSlot = "demo";

        public readonly string Directory;

        public SaveStore(string directory) { Directory = directory; }

        public string PathFor(string slot) { return Path.Combine(Directory, slot + ".sts.json"); }

        public bool Exists(string slot) { return File.Exists(PathFor(slot)); }

        /// <summary>Write to a temp file, flush to disk, then atomically replace the previous save (keeping one .bak).</summary>
        public void Save(string slot, GameState state)
        {
            System.IO.Directory.CreateDirectory(Directory);
            string path = PathFor(slot);
            string tmp = path + ".tmp";
            string bak = path + ".bak";
            byte[] bytes = new UTF8Encoding(false).GetBytes(SaveSerializer.ToJson(state));
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, bak, true); }
                catch (PlatformNotSupportedException) { File.Copy(path, bak, true); File.Delete(path); File.Move(tmp, path); }
            }
            else File.Move(tmp, path);
        }

        public GameState Load(string slot)
        {
            string path = PathFor(slot);
            try
            {
                return SaveSerializer.FromJson(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception) when (File.Exists(path + ".bak"))
            {
                return SaveSerializer.FromJson(File.ReadAllText(path + ".bak", Encoding.UTF8));
            }
        }

        public void Delete(string slot)
        {
            foreach (var p in new[] { PathFor(slot), PathFor(slot) + ".bak", PathFor(slot) + ".tmp" })
                if (File.Exists(p)) File.Delete(p);
        }
    }
}
