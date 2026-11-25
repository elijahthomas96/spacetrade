using System;
using System.Collections.Generic;
using SpaceTrade.Sim;
using UnityEngine;

namespace SpaceTrade.Game
{
    public sealed partial class GameUI
    {
        // ------------------------------------------------------------------ voyage planner

        private int _planDest = -1;
        private readonly Dictionary<string, int> _planFreight = new Dictionary<string, int>();
        private int _planPax;
        private bool _autoProvision = true;
        private Vector2 _destScroll;

        private void ResetPlanner()
        {
            _planDest = -1;
            _planFreight.Clear();
            _planPax = 0;
        }

        private void OpenPlanner() { _panel = Panel.Plan; }

        private VoyagePlan CurrentPlan(Ship ship)
        {
            var plan = new VoyagePlan(ship.Id, _planDest) { Passengers = _planPax };
            foreach (var kv in _planFreight) if (kv.Value > 0) plan.WithFreight(kv.Key, kv.Value);
            return plan;
        }

        private static int Stepper(int value, int max)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-10", UiKit.BtnSmall, GUILayout.Width(34))) value -= 10;
            if (GUILayout.Button("-1", UiKit.BtnSmall, GUILayout.Width(28))) value -= 1;
            GUILayout.Label(value.ToString(), UiKit.Center, GUILayout.Width(44));
            if (GUILayout.Button("+1", UiKit.BtnSmall, GUILayout.Width(28))) value += 1;
            if (GUILayout.Button("+10", UiKit.BtnSmall, GUILayout.Width(34))) value += 10;
            if (GUILayout.Button("Max", UiKit.BtnSmall, GUILayout.Width(38))) value = max;
            if (GUILayout.Button("0", UiKit.BtnSmall, GUILayout.Width(24))) value = 0;
            GUILayout.EndHorizontal();
            return Mathf.Clamp(value, 0, Math.Max(0, max));
        }

        private string TransitText(double ly, DriveDef drive)
        {
            long ticks = GameTime.TransitTicks(ly, drive.RatingC);
            return GameTime.FormatGameDuration(ticks) + " (" + GameTime.FormatRealDuration(GameTime.RealSecondsFor(ticks, S.Speed)) + " real @" + S.Speed + "x)";
        }

        private void DrawPlanner()
        {
            var ship = SelectedShip;
            if (ship == null) { GUILayout.Label("Select a ship first.", UiKit.Muted); return; }
            if (!ship.Docked) { GUILayout.Label(ship.Name + " is in transit. Orders can be planned once it docks.", UiKit.Muted); return; }
            var g = Sim.Galaxy;
            var here = g.Get(ship.Location);
            GUILayout.Label("Plan voyage", UiKit.Title);
            GUILayout.Label(ship.Name + " at " + here.Name + " · " + ship.Drive.Name + " · hold " + ship.Hull.HoldTonnes + " t, tank " + ship.Hull.TankTonnes + " t, " + ship.Hull.Berths + " berths, " + ship.Hull.Crew + " crew", UiKit.Muted);

            GUILayout.Label("Destination", UiKit.H2);
            _destScroll = GUILayout.BeginScrollView(_destScroll, GUILayout.Height(170));
            foreach (var d in g.Systems)
            {
                if (d.Id == here.Id) continue;
                double dist = here.DistanceTo(d);
                int jobs = 0;
                foreach (var e in here.Role.Exports) if (S.DemandAt(d.Id, e) != null && dist <= Catalog.MaxContractRangeLy) jobs++;
                var book = S.Bookings(here.Id, d.Id);
                string tags = (jobs > 0 ? UiKit.Colored(" freight", UiKit.GoodC) : "") + (book != null && book.Available > 0 ? UiKit.Colored(" passengers", UiKit.Accent) : "");
                string text = "<b>" + d.Name + "</b> · " + Fmt.N2(dist) + " ly · " + TransitText(dist, ship.Drive) + tags;
                if (GUILayout.Button(text, d.Id == _planDest ? UiKit.RowOn : UiKit.Row))
                {
                    if (_planDest != d.Id) { _planFreight.Clear(); _planPax = 0; }
                    _planDest = d.Id;
                    SelectSystemSilently(d.Id);
                }
            }
            GUILayout.EndScrollView();
            if (_planDest < 0 || _planDest == here.Id) { GUILayout.Label("Pick a destination to see its jobs, likely return loads and a full quote.", UiKit.Muted); return; }
            var dest = g.Get(_planDest);
            double ly = here.DistanceTo(dest);

            GUILayout.Label("Freight (customers' cargo, paid per tonne delivered)", UiKit.H2);
            int lines = 0;
            foreach (var e in here.Role.Exports)
            {
                var dp = S.DemandAt(dest.Id, e);
                if (dp == null) continue;
                var sp = S.SupplyAt(here.Id, e);
                var def = Catalog.Commodity(e);
                int open = Math.Min(sp.Available, dp.Available);
                lines++;
                GUILayout.Label(string.Format("{0}: {1} t open ({2} t here, {3} t wanted) · {4}", def.Name, open, sp.Available, dp.Available,
                    ly <= Catalog.MaxContractRangeLy ? def.ContractRate(ly) + " cr/t" : UiKit.Colored("beyond contract range", UiKit.BadC)), UiKit.Small);
                int cur;
                _planFreight.TryGetValue(e, out cur);
                _planFreight[e] = Stepper(cur, open);
            }
            if (lines == 0) GUILayout.Label("No freight runs from " + here.Name + " to " + dest.Name + ".", UiKit.Muted);
            else if (GUILayout.Button("Fill hold with available freight", UiKit.BtnSmall))
            {
                int room = Planner.MaxFreightTonnes(S, ship, dest.Id, _planPax);
                foreach (var e in here.Role.Exports)
                {
                    var dp = S.DemandAt(dest.Id, e);
                    if (dp == null) continue;
                    int q = Math.Min(room, Math.Min(S.SupplyAt(here.Id, e).Available, dp.Available));
                    _planFreight[e] = q;
                    room -= q;
                }
            }

            GUILayout.Label("Passengers (booked seats)", UiKit.H2);
            var bk = S.Bookings(here.Id, dest.Id);
            if (bk == null || ship.Hull.Berths == 0)
                GUILayout.Label(bk == null ? "No passenger service between these systems." : "This ship has no berths.", UiKit.Muted);
            else
            {
                GUILayout.Label(bk.Available + " booked · " + Planner.Fare(ly) + " cr each · " + ship.Hull.Berths + " berths", UiKit.Small);
                _planPax = Stepper(_planPax, Math.Min(bk.Available, ship.Hull.Berths));
            }

            var plan = CurrentPlan(ship);
            var q2 = Sim.Quote(plan);
            DrawQuote(ship, q2);

            GUILayout.Label("Likely return loads from " + dest.Name, UiKit.H2);
            int ret = 0;
            foreach (var e in dest.Role.Exports)
            {
                var sp = S.SupplyAt(dest.Id, e);
                foreach (var o in g.Systems)
                {
                    var dp = S.DemandAt(o.Id, e);
                    if (dp == null || o.Id == dest.Id || dest.DistanceTo(o) > Catalog.MaxContractRangeLy) continue;
                    if (o.Id != here.Id && ret >= 6) continue;
                    ret++;
                    GUILayout.Label(string.Format("{0}{1} → {2} · {3} ly · {4} t open · {5} cr/t", o.Id == here.Id ? "<b>Back here:</b> " : "", Catalog.Commodity(e).Name, o.Name,
                        Fmt.N2(dest.DistanceTo(o)), Math.Min(sp.Available, dp.Available), Catalog.Commodity(e).ContractRate(dest.DistanceTo(o))), UiKit.Small);
                }
            }
            var back = S.Bookings(dest.Id, here.Id);
            if (back != null) { ret++; GUILayout.Label(string.Format("<b>Back here:</b> {0} passengers booked · {1} cr each", back.Available, Planner.Fare(ly)), UiKit.Small); }
            if (ret == 0) GUILayout.Label("Nothing obvious: plan an empty repositioning leg or a route onward.", UiKit.Muted);

            GUILayout.Space(6);
            _autoProvision = GUILayout.Toggle(_autoProvision, " Buy any missing provisions at launch", UiKit.Toggle);
            bool canLaunch = q2.LoadFeasible && (q2.ReadyToLaunch || (_autoProvision && q2.CanAffordProvisioning));
            bool was = GUI.enabled;
            GUI.enabled = was && canLaunch;
            if (GUILayout.Button("Launch to " + dest.Name + (q2.NeedsProvisioning && _autoProvision ? " (provisions " + Fmt.Credits(q2.TopUpCost) + ")" : ""), UiKit.BtnOn, GUILayout.Height(34)))
            {
                _boot.Session.Update();
                var res = Sim.Dispatch(CurrentPlan(ship), _autoProvision);
                Report(res);
                if (res.Ok)
                {
                    ResetPlanner();
                    _panel = Panel.Ship;
                    _map.FollowShipId = ship.Id;
                    _boot.SaveNow();
                }
            }
            GUI.enabled = was;
        }

        private void SelectSystemSilently(int id)
        {
            _systemId = id;
            _map.SelectedSystemId = id;
        }

        private void DrawQuote(Ship ship, Quote q)
        {
            GUILayout.Label("Quote", UiKit.H2);
            GUILayout.BeginVertical(UiKit.Card);
            KV("Transit", GameTime.FormatGameDuration(q.DurationTicks) + " at " + Fmt.N2(q.Drive.RatingC) + "c (" + Fmt.N2(q.DistanceLy) + " ly)");
            KV("Arrival (game date)", GameTime.FormatDate(q.ArrivalTick));
            KV("Real time", GameTime.FormatRealDuration(GameTime.RealSecondsFor(q.DurationTicks, S.Speed)) + " at " + S.Speed + "x · " + GameTime.FormatRealDuration(GameTime.RealSecondsFor(q.DurationTicks, 1)) + " at 1x");
            KV("People aboard", q.People + " (" + ship.Hull.Crew + " crew)");
            BarRow("Hold", (float)(q.HoldUsed / q.HoldCapacity), Fmt.N1(q.HoldUsed) + " / " + Fmt.N1(q.HoldCapacity) + " t", q.HoldUsed > q.HoldCapacity + 1e-9 ? UiKit.BadC : UiKit.Accent);
            GUILayout.Label("   freight " + Fmt.T(q.FreightTonnes) + " + food " + Fmt.T(q.Food.AfterTopUp) + " + water " + Fmt.T(q.Water.AfterTopUp), UiKit.Muted);
            BarRow("Fuel", (float)(q.Fuel.Required / q.TankCapacity), Fmt.N1(q.Fuel.Required) + " / " + Fmt.N1(q.TankCapacity) + " t", q.Fuel.Required > q.TankCapacity + 1e-9 ? UiKit.BadC : UiKit.GoodC);
            GUILayout.Label("   loaded mass " + Fmt.T(q.LoadedMass) + " · " + Fmt.N2(q.Drive.FuelPerLyPer100t) + " t fuel per ly per 100 t", UiKit.Muted);

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Provision</b>", UiKit.Small, GUILayout.Width(90));
            GUILayout.Label("<b>Aboard</b>", UiKit.Small, GUILayout.Width(62));
            GUILayout.Label("<b>Required</b>", UiKit.Small, GUILayout.Width(66));
            GUILayout.Label("<b>Short</b>", UiKit.Small, GUILayout.Width(56));
            GUILayout.Label("<b>Top-up</b>", UiKit.Small);
            GUILayout.EndHorizontal();
            foreach (var line in q.ProvisionLines)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(line.Label, UiKit.Small, GUILayout.Width(90));
                GUILayout.Label(Fmt.N1(line.Aboard), UiKit.Small, GUILayout.Width(62));
                GUILayout.Label(Fmt.N2(line.Required), UiKit.Small, GUILayout.Width(66));
                GUILayout.Label(line.TopUp > 0 ? UiKit.Colored(Fmt.N2(line.TopUp), UiKit.WarnC) : "-", UiKit.Small, GUILayout.Width(56));
                GUILayout.Label(line.TopUp > 0 ? Fmt.Credits(line.TopUpCost) + " @" + line.Price : "-", UiKit.Small);
                GUILayout.EndHorizontal();
            }
            if (q.NeedsProvisioning)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Top-up cost " + UiKit.Colored(Fmt.Credits(q.TopUpCost), q.CanAffordProvisioning ? UiKit.TextC : UiKit.BadC), UiKit.Label);
                bool was = GUI.enabled;
                GUI.enabled = was && q.CanAffordProvisioning;
                if (GUILayout.Button("Provision now", UiKit.Btn, GUILayout.Width(120)))
                {
                    Report(Sim.ProvisionFor(q.Plan));
                    _boot.SaveNow();
                }
                GUI.enabled = was;
                GUILayout.EndHorizontal();
            }
            else GUILayout.Label(UiKit.Colored("Fully provisioned for this leg.", UiKit.GoodC), UiKit.Small);

            foreach (var p in q.Problems) GUILayout.Label("! " + p, UiKit.Bad);
            foreach (var n in q.Notes) GUILayout.Label(n, UiKit.Warn);

            GUILayout.Space(4);
            KV("Freight revenue", Fmt.Credits(q.FreightRevenue));
            KV("Passenger fares", Fmt.Credits(q.FareRevenue));
            KV("<b>Gross revenue</b>", "<b>" + Fmt.Credits(q.GrossRevenue) + "</b>");
            KV("Supplies consumed", Fmt.Credits(q.SuppliesCost) + " (at acquisition cost)");
            KV("Port fee", Fmt.Credits(q.PortFee));
            KV("Operating costs", Fmt.Credits(q.OperatingCost));
            KV("<b>Net profit</b>", UiKit.Colored("<b>" + Fmt.Credits(q.NetProfit) + "</b>", q.NetProfit >= 0 ? UiKit.GoodC : UiKit.BadC));
            KV("Profit per game year", Fmt.Credits((long)Math.Round(q.ProfitPerYear)));
            GUILayout.EndVertical();
        }

        // ------------------------------------------------------------------ route editor

        private RouteOrder _draft;
        private int _draftShip = -1;

        private void OpenRouteEditor()
        {
            var ship = SelectedShip;
            if (ship == null) return;
            _panel = Panel.Route;
            if (_draft == null || _draftShip != ship.Id)
            {
                _draftShip = ship.Id;
                _draft = ship.Route != null ? ship.Route.Clone() : DefaultRoute(ship);
            }
        }

        private RouteOrder DefaultRoute(Ship ship)
        {
            var g = Sim.Galaxy;
            int a = ship.Docked ? ship.Location : ship.Voyage.Destination;
            int b = -1;
            double best = double.MaxValue;
            foreach (var s in g.Systems)
            {
                if (s.Id == a) continue;
                double d = g.Distance(a, s.Id);
                if (d < best) { best = d; b = s.Id; }
            }
            var r = new RouteOrder { Name = ship.Name + " circuit" };
            r.Stops.Add(NewStop(a, b, ship));
            r.Stops.Add(NewStop(b, a, ship));
            return r;
        }

        private RouteStop NewStop(int sys, int next, Ship ship)
        {
            var st = new RouteStop { SystemId = sys, Passengers = ship.Hull.Berths > 0, AutoProvision = true };
            foreach (var e in Sim.Galaxy.Get(sys).Role.Exports) if (S.DemandAt(next, e) != null) st.Commodities.Add(e);
            return st;
        }

        private void DrawRouteEditor()
        {
            var ship = SelectedShip;
            if (ship == null) { GUILayout.Label("Select a ship first.", UiKit.Muted); return; }
            if (_draft == null || _draftShip != ship.Id) OpenRouteEditor();
            var g = Sim.Galaxy;
            GUILayout.Label("Route orders", UiKit.Title);
            GUILayout.Label(ship.Name + " repeats the stops in order. At each stop it loads what you allow for the next stop and departs once the minimum load is aboard.", UiKit.Muted);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", UiKit.Muted, GUILayout.Width(50));
            _draft.Name = GUILayout.TextField(_draft.Name ?? "", 40, UiKit.Field);
            GUILayout.EndHorizontal();

            int remove = -1, moveUp = -1;
            for (int i = 0; i < _draft.Stops.Count; i++)
            {
                var st = _draft.Stops[i];
                var next = _draft.Stops[(i + 1) % _draft.Stops.Count];
                var sys = g.Get(st.SystemId);
                var nsys = g.Get(next.SystemId);
                GUILayout.BeginVertical(UiKit.Card);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b>Stop " + (i + 1) + "</b>", UiKit.Label, GUILayout.Width(56));
                if (GUILayout.Button("<", UiKit.BtnSmall, GUILayout.Width(24))) st.SystemId = (st.SystemId + g.Systems.Count - 1) % g.Systems.Count;
                GUILayout.Label(sys.Name, UiKit.Center, GUILayout.Width(130));
                if (GUILayout.Button(">", UiKit.BtnSmall, GUILayout.Width(24))) st.SystemId = (st.SystemId + 1) % g.Systems.Count;
                GUILayout.FlexibleSpace();
                if (i > 0 && GUILayout.Button("Up", UiKit.BtnSmall)) moveUp = i;
                if (_draft.Stops.Count > 2 && GUILayout.Button("Remove", UiKit.BtnSmall)) remove = i;
                GUILayout.EndHorizontal();
                double ly = sys.DistanceTo(nsys);
                GUILayout.Label("Next: " + nsys.Name + " · " + Fmt.N2(ly) + " ly · " + TransitText(ly, ship.Drive), UiKit.Muted);
                st.EmptyLeg = GUILayout.Toggle(st.EmptyLeg, " Empty repositioning leg (carry nothing)", UiKit.Toggle);
                if (!st.EmptyLeg)
                {
                    var valid = new List<string>();
                    foreach (var e in sys.Role.Exports) if (S.DemandAt(nsys.Id, e) != null) valid.Add(e);
                    st.Commodities.RemoveAll(c => !valid.Contains(c));
                    if (valid.Count == 0) GUILayout.Label("No freight from " + sys.Name + " to " + nsys.Name + ".", UiKit.Muted);
                    foreach (var e in valid)
                    {
                        bool on = st.Commodities.Contains(e);
                        bool now = GUILayout.Toggle(on, " Load " + Catalog.Commodity(e).Name + " for " + nsys.Name, UiKit.Toggle);
                        if (now && !on) st.Commodities.Add(e);
                        if (!now && on) st.Commodities.Remove(e);
                    }
                    if (ship.Hull.Berths > 0) st.Passengers = GUILayout.Toggle(st.Passengers, " Carry booked passengers", UiKit.Toggle);
                    else st.Passengers = false;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Minimum departure load", UiKit.Small, GUILayout.Width(150));
                    if (GUILayout.Button(st.MinLoadUnit == LoadUnit.Tonnes ? "tonnes" : "passengers", UiKit.BtnSmall, GUILayout.Width(80)))
                        st.MinLoadUnit = st.MinLoadUnit == LoadUnit.Tonnes ? LoadUnit.Passengers : LoadUnit.Tonnes;
                    GUILayout.EndHorizontal();
                    int cap = st.MinLoadUnit == LoadUnit.Tonnes ? (int)ship.Hull.HoldTonnes : ship.Hull.Berths;
                    st.MinLoad = Stepper(st.MinLoad, cap);
                }
                st.AutoProvision = GUILayout.Toggle(st.AutoProvision, " Provision automatically here", UiKit.Toggle);
                GUILayout.EndVertical();
            }
            if (remove >= 0) _draft.Stops.RemoveAt(remove);
            if (moveUp > 0) { var t = _draft.Stops[moveUp]; _draft.Stops.RemoveAt(moveUp); _draft.Stops.Insert(moveUp - 1, t); }
            if (GUILayout.Button("+ Add stop", UiKit.Btn))
            {
                var last = _draft.Stops[_draft.Stops.Count - 1];
                int pick = (last.SystemId + 1) % g.Systems.Count;
                if (pick == _draft.Stops[0].SystemId) pick = (pick + 1) % g.Systems.Count;
                _draft.Stops.Add(NewStop(pick, _draft.Stops[0].SystemId, ship));
            }

            var est = Sim.EstimateCircuit(ship, _draft);
            GUILayout.Label("Whole-circuit estimate", UiKit.H2);
            GUILayout.BeginVertical(UiKit.Card);
            foreach (var leg in est.Legs)
            {
                string load = leg.Empty ? "empty leg" : leg.FreightTonnes + " t, " + leg.Passengers + " pax";
                GUILayout.Label(string.Format("{0} → {1} · {2} ly · {3} · {4} · revenue {5} · costs {6} · net {7}{8}",
                    g.Get(leg.From).Name, g.Get(leg.To).Name, Fmt.N2(leg.DistanceLy), GameTime.FormatGameDuration(leg.Ticks), load,
                    Fmt.Credits(leg.Revenue), Fmt.Credits(leg.SuppliesCost + leg.PortFee), Fmt.Credits(leg.Net),
                    leg.Feasible ? "" : UiKit.Colored("  ! " + leg.Problem, UiKit.BadC)), UiKit.Small);
            }
            KV("Total transit", GameTime.FormatGameDuration(est.TotalTicks) + " (" + GameTime.FormatRealDuration(GameTime.RealSecondsFor(est.TotalTicks, 1)) + " real at 1x)");
            KV("Revenue / costs", Fmt.Credits(est.Revenue) + " / " + Fmt.Credits(est.Costs));
            KV("<b>Net per circuit</b>", UiKit.Colored("<b>" + Fmt.Credits(est.Net) + "</b>", est.Net >= 0 ? UiKit.GoodC : UiKit.BadC));
            KV("Profit per game year", Fmt.Credits((long)Math.Round(est.ProfitPerYear)));
            if (est.DependsOnBookings) GUILayout.Label("Estimate: loads depend on future supply and bookings.", UiKit.Warn);
            if (est.ExcludesWaiting) GUILayout.Label("Estimate: time spent waiting for minimum loads is not included.", UiKit.Warn);
            GUILayout.EndVertical();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Assign route", UiKit.BtnOn, GUILayout.Height(32)))
            {
                _boot.Session.Update();
                var r = Sim.SetRoute(ship.Id, _draft);
                Report(r);
                if (r.Ok) { _draft = null; _panel = Panel.Ship; _boot.SaveNow(); }
            }
            if (GUILayout.Button("Revert", UiKit.Btn, GUILayout.Height(32))) _draft = ship.Route != null ? ship.Route.Clone() : DefaultRoute(ship);
            if (ship.Route != null && GUILayout.Button("Clear route", UiKit.Btn, GUILayout.Height(32))) { Report(Sim.ClearRoute(ship.Id)); _draft = null; _boot.SaveNow(); }
            GUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ shipyard

        private int _yardId = -1;
        private int _yardShip = -1;

        private void OpenYard(int systemId)
        {
            _yardId = systemId;
            if (_shipId >= 0) _yardShip = _shipId;
            _panel = Panel.Yard;
            SelectSystemSilently(systemId);
        }

        private void DrawYard()
        {
            var yard = S.Yard(_yardId);
            var g = Sim.Galaxy;
            if (yard == null) { GUILayout.Label("No shipyard selected.", UiKit.Muted); return; }
            var sys = g.Get(yard.SystemId);
            GUILayout.Label("Shipyard · " + sys.Name, UiKit.Title);
            GUILayout.Label("New ships are delivered docked here with an Alcubierre Mk I drive and empty supplies. This yard can only work on ships docked at " + sys.Name + ".", UiKit.Muted);

            GUILayout.Label("Ships for sale", UiKit.H2);
            foreach (var h in yard.Hulls)
            {
                var hd = Catalog.Hull(h.HullId);
                GUILayout.BeginVertical(UiKit.Card);
                GUILayout.Label("<b>" + hd.ClassName + "</b> · " + hd.Role, UiKit.Label);
                GUILayout.Label(string.Format("Hold {0} t · tank {1} t · {2} berths · {3} crew · hull {4} t", hd.HoldTonnes, hd.TankTonnes, hd.Berths, hd.Crew, hd.HullMassTonnes), UiKit.Small);
                GUILayout.BeginHorizontal();
                GUILayout.Label(Fmt.Credits(h.Price) + " · " + h.Count + " in stock", UiKit.Label);
                bool was = GUI.enabled;
                GUI.enabled = was && h.Count > 0 && S.Credits >= h.Price;
                if (GUILayout.Button("Buy", UiKit.BtnOn, GUILayout.Width(70)))
                {
                    var r = Sim.BuyShip(yard.SystemId, h.HullId);
                    Report(r);
                    if (r.Ok) { _yardShip = r.Ship.Id; _boot.SaveNow(); }
                }
                GUI.enabled = was;
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            GUILayout.Label("Drives in stock", UiKit.H2);
            foreach (var d in yard.Drives)
            {
                var dd = Catalog.Drive(d.DriveId);
                GUILayout.Label(string.Format("<b>{0}</b> · {1} t fuel/ly/100 t · {2} · {3} in stock", dd.Name, Fmt.N2(dd.FuelPerLyPer100t), Fmt.Credits(d.Price), d.Count), UiKit.Small);
            }

            GUILayout.Label("Install a drive", UiKit.H2);
            if (S.Ships.Count == 0) return;
            var ship = S.Ship(_yardShip) ?? S.Ships[0];
            _yardShip = ship.Id;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Ship", UiKit.Muted, GUILayout.Width(40));
            int idx = S.Ships.IndexOf(ship);
            if (GUILayout.Button("<", UiKit.BtnSmall, GUILayout.Width(24))) _yardShip = S.Ships[(idx + S.Ships.Count - 1) % S.Ships.Count].Id;
            GUILayout.Label("<b>" + ship.Name + "</b> (" + ship.Drive.Name + ")", UiKit.Center);
            if (GUILayout.Button(">", UiKit.BtnSmall, GUILayout.Width(24))) _yardShip = S.Ships[(idx + 1) % S.Ships.Count].Id;
            GUILayout.EndHorizontal();
            GUILayout.Label(ship.Docked ? "Docked at " + g.Get(ship.Location).Name : "In transit to " + g.Get(ship.Voyage.Destination).Name, UiKit.Muted);

            int fallback = NearestOther(ship.Docked ? ship.Location : ship.Voyage.Destination);
            foreach (var d in yard.Drives)
            {
                var dd = Catalog.Drive(d.DriveId);
                var pv = Sim.PreviewDriveUpgrade(ship.Id, yard.SystemId, d.DriveId, fallback);
                GUILayout.BeginVertical(UiKit.Card);
                GUILayout.Label("<b>" + dd.Name + "</b> · " + Fmt.Credits(pv.Price), UiKit.Label);
                if (pv.RouteLabel != null)
                    GUILayout.Label(string.Format("On {0}: {1} → {2}, saves {3} game time ({4} real at 1x). Fuel at full load {5} → {6} t.",
                        pv.RouteLabel, GameTime.FormatGameDuration(pv.OldTicks), GameTime.FormatGameDuration(pv.NewTicks),
                        GameTime.FormatGameDuration(Math.Max(0, pv.SavedTicks)), GameTime.FormatRealDuration(GameTime.RealSecondsFor(Math.Max(0, pv.SavedTicks), 1)),
                        Fmt.N1(pv.OldFuel), Fmt.N1(pv.NewFuel)), UiKit.Small);
                if (!pv.Allowed) GUILayout.Label(pv.Reason, UiKit.Bad);
                bool was = GUI.enabled;
                GUI.enabled = was && pv.Allowed;
                if (GUILayout.Button("Install on " + ship.Name, UiKit.BtnOn))
                {
                    Report(Sim.InstallDrive(ship.Id, yard.SystemId, d.DriveId));
                    _boot.SaveNow();
                }
                GUI.enabled = was;
                GUILayout.EndVertical();
            }
        }

        private int NearestOther(int from)
        {
            var g = Sim.Galaxy;
            int best = -1;
            double bd = double.MaxValue;
            foreach (var s in g.Systems)
            {
                if (s.Id == from) continue;
                double d = g.Distance(from, s.Id);
                if (d < bd) { bd = d; best = s.Id; }
            }
            return best;
        }
    }
}
