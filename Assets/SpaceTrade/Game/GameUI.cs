using System;
using System.Collections.Generic;
using SpaceTrade.Sim;
using UnityEngine;

namespace SpaceTrade.Game
{
    /// <summary>
    /// The whole interface, drawn with IMGUI from code. Every action goes through Simulation commands,
    /// so the UI never mutates game state directly and uses the same calculations as departures.
    /// </summary>
    public sealed partial class GameUI : MonoBehaviour
    {
        private enum Panel { None, Ship, System, Plan, Route, Yard, Receipt }
        private enum Modal { None, Menu, Help, CatchUp }

        private GameBootstrap _boot;
        private MapView _map;
        private Panel _panel = Panel.Ship;
        private Modal _modal = Modal.None;
        private int _shipId = -1;
        private int _systemId = -1;
        private Receipt _receipt;
        private string _flash;
        private bool _flashBad;
        private float _flashUntil;
        private float _scale = 1f;
        private Vector2 _fleetScroll, _rightScroll, _noticeScroll;
        private Rect _topRect, _leftRect, _rightRect, _modalRect, _cardRect;
        private bool _dragging, _pressOnMap;
        private Vector2 _pressPos;
        private int _pressButton;
        private bool _confirmNew;

        private readonly List<KeyValuePair<Notification, float>> _toasts = new List<KeyValuePair<Notification, float>>();
        private Notification _lastSeenNotice;

        private Simulation Sim { get { return _boot != null ? _boot.Sim : null; } }
        private GameState S { get { return Sim.State; } }

        private void Start()
        {
            _boot = GameBootstrap.Instance;
            _map = GetComponent<MapView>();
            _boot.WorldChanged += OnWorldChanged;
            OnWorldChanged();
        }

        private void OnDestroy() { if (_boot != null) _boot.WorldChanged -= OnWorldChanged; }

        private void OnWorldChanged()
        {
            ResetPlanner();
            _draft = null;
            _toasts.Clear();
            _lastSeenNotice = S.Notifications.Count > 0 ? S.Notifications[S.Notifications.Count - 1] : null;
            var first = S.Ships.Count > 0 ? S.Ships[0] : null;
            SelectShip(first != null ? first.Id : -1, follow: false);
            if (first != null) _map.FocusOn(ToV2(Sim.ShipPosition(first)));
            _modal = _boot.PendingCatchUp != null ? Modal.CatchUp : Modal.None;
        }

        private static Vector2 ToV2(Vec2 v) { return new Vector2((float)v.X, (float)v.Y); }

        private void Flash(string msg, bool bad)
        {
            _flash = msg;
            _flashBad = bad;
            _flashUntil = Time.unscaledTime + 6f;
        }

        private void Report(CommandResult r) { Flash(r.Message, !r.Ok); }

        // ------------------------------------------------------------------ selection

        private void SelectShip(int id, bool follow)
        {
            _shipId = id;
            _map.SelectedShipId = id;
            _panel = id >= 0 ? Panel.Ship : Panel.None;
            if (follow && id >= 0) _map.FollowShipId = id;
            ResetPlanner();
        }

        private void SelectSystem(int id)
        {
            _systemId = id;
            _map.SelectedSystemId = id;
            _panel = id >= 0 ? Panel.System : Panel.None;
        }

        private Ship SelectedShip { get { return _shipId >= 0 ? S.Ship(_shipId) : null; } }

        // ------------------------------------------------------------------ main loop

        private void Update()
        {
            if (Sim == null) return;
            // Collect new notifications as toasts.
            var list = S.Notifications;
            int found = _lastSeenNotice == null ? -1 : list.LastIndexOf(_lastSeenNotice);
            int start = _lastSeenNotice == null ? 0 : (found < 0 ? Math.Max(0, list.Count - 3) : found + 1);
            for (int i = start; i < list.Count; i++)
            {
                if (list[i].Kind == NoticeKind.Arrival || list[i].Kind == NoticeKind.Blocked || list[i].Kind == NoticeKind.Purchase)
                    _toasts.Add(new KeyValuePair<Notification, float>(list[i], Time.unscaledTime + 7f));
                if (list[i].Kind == NoticeKind.Arrival && list[i].ShipId == _shipId && (_panel == Panel.Ship || _panel == Panel.None) && _modal == Modal.None)
                    ShowReceiptFor(_shipId);
            }
            if (list.Count > 0) _lastSeenNotice = list[list.Count - 1];
            _toasts.RemoveAll(t => t.Value < Time.unscaledTime);
            while (_toasts.Count > 4) _toasts.RemoveAt(0);
        }

        private void OnGUI()
        {
            if (Sim == null) return;
            UiKit.Init();
            _scale = Mathf.Clamp(Screen.height / 900f, 0.7f, 2.5f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(_scale, _scale, 1));
            float w = Screen.width / _scale, h = Screen.height / _scale;

            _topRect = new Rect(0, 0, w, 44);
            _leftRect = new Rect(8, 52, 320, h - 60);
            _rightRect = _panel == Panel.None ? new Rect(-1000, 0, 0, 0) : new Rect(w - 448, 52, 440, h - 60);
            _cardRect = _map.CloseUp ? new Rect(w * 0.5f - 230, h - 150, 460, 140) : new Rect(-1000, 0, 0, 0);

            DrawMapLabels();
            bool modalOpen = _modal != Modal.None;
            GUI.enabled = !modalOpen;
            DrawTopBar();
            DrawFleet();
            DrawRightPanel();
            DrawCloseUpCard();
            DrawToasts(w);
            GUI.enabled = true;
            DrawModal(w, h);
            HandleInput();
        }

        // ------------------------------------------------------------------ top bar

        private void DrawTopBar()
        {
            GUI.Box(_topRect, GUIContent.none, UiKit.Panel);
            GUILayout.BeginArea(new Rect(_topRect.x + 10, _topRect.y + 6, _topRect.width - 20, _topRect.height - 8));
            GUILayout.BeginHorizontal();
            GUILayout.Label(S.IsDemo ? "SPACE TRADE  <color=#FFC94D>[DEMO]</color>" : "SPACE TRADE", UiKit.Title, GUILayout.Width(200));
            GUILayout.Label("<b>" + Fmt.Credits(S.Credits) + "</b>", UiKit.Label, GUILayout.Width(150));
            GUILayout.Label("Date " + GameTime.FormatDate(S.Now), UiKit.Label, GUILayout.Width(150));

            if (GUILayout.Button(S.Paused ? "Play" : "Pause", S.Paused ? UiKit.BtnOn : UiKit.Btn, GUILayout.Width(90))) TogglePause();
            foreach (int sp in GameTime.AllowedSpeeds)
                if (GUILayout.Button(sp + "x", S.Speed == sp ? UiKit.BtnOn : UiKit.Btn, GUILayout.Width(44))) SetSpeed(sp);
            GUILayout.Label(S.Paused ? UiKit.Colored("  PAUSED", UiKit.WarnC) : "  1 real hour = " + Fmt.N1(S.Speed) + " game yr", UiKit.Small, GUILayout.Width(170));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DebugGate.Allowed)
            {
                bool dbg = GUILayout.Toggle(Sim.DebugEnabled, " Debug mode", UiKit.Toggle, GUILayout.Width(100));
                if (dbg != Sim.DebugEnabled) Sim.DebugEnabled = dbg;
                if (Sim.DebugEnabled)
                {
                    bool was = GUI.enabled;
                    GUI.enabled = was && Sim.CanSkipToNextArrival;
                    if (GUILayout.Button("Skip to next arrival", UiKit.Btn, GUILayout.Width(150)))
                    {
                        _boot.Session.Update();
                        Report(Sim.SkipToNextArrival());
                        _boot.SaveNow();
                    }
                    GUI.enabled = was;
                }
            }
#endif
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Help", UiKit.Btn, GUILayout.Width(56))) _modal = Modal.Help;
            if (GUILayout.Button("Menu", UiKit.Btn, GUILayout.Width(60))) { _modal = Modal.Menu; _confirmNew = false; }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void TogglePause() { _boot.Session.SetPaused(!S.Paused); _boot.SaveNow(); }

        private void SetSpeed(int sp) { _boot.Session.SetSpeed(sp); }

        // ------------------------------------------------------------------ fleet list and notifications

        private static string StatusText(ShipStatus st)
        {
            switch (st)
            {
                case ShipStatus.InTransit: return UiKit.Colored("IN TRANSIT", UiKit.Accent);
                case ShipStatus.Waiting: return UiKit.Colored("WAITING FOR LOAD", UiKit.WarnC);
                case ShipStatus.Blocked: return UiKit.Colored("BLOCKED", UiKit.BadC);
                default: return UiKit.Colored("IDLE", UiKit.MutedC);
            }
        }

        private string ShipOneLiner(Ship s)
        {
            var g = Sim.Galaxy;
            switch (s.Status)
            {
                case ShipStatus.InTransit:
                    return "→ " + g.Get(s.Voyage.Destination).Name + "  " + Fmt.Pct(s.Voyage.Progress(S.Now)) + "  ETA " + RealEta(s);
                case ShipStatus.Waiting: return "At " + g.Get(s.Location).Name + ": " + s.WaitingReason;
                case ShipStatus.Blocked: return "At " + g.Get(s.Location).Name + ": " + s.BlockedReason;
                default: return "Docked at " + g.Get(s.Location).Name + (s.Route == null ? " · awaiting orders" : "");
            }
        }

        private string RealEta(Ship s)
        {
            string t = GameTime.FormatRealDuration(Sim.RealSecondsToArrival(s));
            return S.Paused ? t + " (paused)" : t + " @" + S.Speed + "x";
        }

        private void DrawFleet()
        {
            GUI.Box(_leftRect, GUIContent.none, UiKit.Panel);
            GUILayout.BeginArea(new Rect(_leftRect.x + 10, _leftRect.y + 8, _leftRect.width - 20, _leftRect.height - 16));
            int transit = 0, waiting = 0, idle = 0, blocked = 0;
            foreach (var s in S.Ships)
                switch (s.Status) { case ShipStatus.InTransit: transit++; break; case ShipStatus.Waiting: waiting++; break; case ShipStatus.Blocked: blocked++; break; default: idle++; break; }
            GUILayout.Label("Fleet (" + S.Ships.Count + ")", UiKit.Title);
            GUILayout.Label(string.Format("{0} in transit · {1} waiting · {2} idle · {3}", transit, waiting, idle,
                blocked > 0 ? UiKit.Colored(blocked + " blocked", UiKit.BadC) : "0 blocked"), UiKit.Small);

            float listH = Mathf.Max(160, (_leftRect.height - 120) * 0.58f);
            _fleetScroll = GUILayout.BeginScrollView(_fleetScroll, GUILayout.Height(listH));
            foreach (var s in S.Ships)
            {
                var col = MapView.ShipPalette[s.ColorIndex % MapView.ShipPalette.Length];
                string text = "      <b>" + s.Name + "</b>  " + StatusText(s.Status) + "\n      <size=11>" + s.Hull.ClassName.Split('-')[0] + " · " + ShipOneLiner(s) + "</size>";
                var r = GUILayoutUtility.GetRect(new GUIContent(text), s.Id == _shipId ? UiKit.RowOn : UiKit.Row, GUILayout.MinHeight(44));
                if (GUI.Button(r, text, s.Id == _shipId ? UiKit.RowOn : UiKit.Row))
                {
                    SelectShip(s.Id, follow: true);
                    _map.ExitCloseUp();
                }
                UiKit.Swatch(new Rect(r.x + 8, r.y + 10, 10, 10), col);
            }
            GUILayout.EndScrollView();

            GUILayout.Label("Notifications", UiKit.H2);
            _noticeScroll = GUILayout.BeginScrollView(_noticeScroll);
            for (int i = S.Notifications.Count - 1, n = 0; i >= 0 && n < 40; i--, n++)
            {
                var note = S.Notifications[i];
                Color c = note.Kind == NoticeKind.Blocked ? UiKit.BadC : note.Kind == NoticeKind.Arrival ? UiKit.GoodC : note.Kind == NoticeKind.Waiting ? UiKit.WarnC : UiKit.MutedC;
                string line = UiKit.Colored(GameTime.FormatDate(note.Tick), UiKit.MutedC) + "  " + UiKit.Colored(note.Text, c);
                if (GUILayout.Button(line, UiKit.Row) && note.ShipId >= 0 && S.Ship(note.ShipId) != null)
                {
                    SelectShip(note.ShipId, follow: true);
                    if (note.Kind == NoticeKind.Arrival) ShowReceiptFor(note.ShipId);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void ShowReceiptFor(int shipId)
        {
            var r = Sim.LastReceiptFor(shipId);
            if (r == null) return;
            _receipt = r;
            _panel = Panel.Receipt;
        }

        // ------------------------------------------------------------------ right panel

        private void DrawRightPanel()
        {
            if (_panel == Panel.None) return;
            GUI.Box(_rightRect, GUIContent.none, UiKit.Panel);
            GUILayout.BeginArea(new Rect(_rightRect.x + 10, _rightRect.y + 8, _rightRect.width - 20, _rightRect.height - 16));
            GUILayout.BeginHorizontal();
            if (_shipId >= 0 && GUILayout.Button("Ship", _panel == Panel.Ship ? UiKit.BtnOn : UiKit.BtnSmall)) _panel = Panel.Ship;
            if (_systemId >= 0 && GUILayout.Button("System", _panel == Panel.System ? UiKit.BtnOn : UiKit.BtnSmall)) _panel = Panel.System;
            var ship = SelectedShip;
            if (ship != null && ship.Docked && GUILayout.Button("Plan voyage", _panel == Panel.Plan ? UiKit.BtnOn : UiKit.BtnSmall)) OpenPlanner();
            if (ship != null && GUILayout.Button("Route", _panel == Panel.Route ? UiKit.BtnOn : UiKit.BtnSmall)) OpenRouteEditor();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", UiKit.BtnSmall, GUILayout.Width(26))) _panel = Panel.None;
            GUILayout.EndHorizontal();

            if (_flash != null && Time.unscaledTime < _flashUntil)
                GUILayout.Label(_flash, _flashBad ? UiKit.Bad : UiKit.Good);

            _rightScroll = GUILayout.BeginScrollView(_rightScroll);
            switch (_panel)
            {
                case Panel.Ship: DrawShipPanel(); break;
                case Panel.System: DrawSystemPanel(); break;
                case Panel.Plan: DrawPlanner(); break;
                case Panel.Route: DrawRouteEditor(); break;
                case Panel.Yard: DrawYard(); break;
                case Panel.Receipt: DrawReceipt(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void KV(string key, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(key, UiKit.Muted, GUILayout.Width(150));
            GUILayout.Label(value, UiKit.Label);
            GUILayout.EndHorizontal();
        }

        private static void BarRow(string label, float frac, string value, Color c)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, UiKit.Muted, GUILayout.Width(110));
            var r = GUILayoutUtility.GetRect(120, 14, GUILayout.ExpandWidth(true));
            r.y += 3; r.height = 10;
            UiKit.Bar(r, frac, c);
            GUILayout.Label(value, UiKit.Small, GUILayout.Width(120));
            GUILayout.EndHorizontal();
        }

        private void DrawShipPanel()
        {
            var ship = SelectedShip;
            if (ship == null) { GUILayout.Label("Select a ship in the fleet list or on the map.", UiKit.Muted); return; }
            var g = Sim.Galaxy;
            var hull = ship.Hull;
            var col = MapView.ShipPalette[ship.ColorIndex % MapView.ShipPalette.Length];
            GUILayout.Label(UiKit.Colored("■ ", col) + ship.Name, UiKit.Title);
            GUILayout.Label(hull.ClassName + " · " + hull.Role, UiKit.Muted);
            GUILayout.Label(StatusText(ship.Status), UiKit.Label);
            if (ship.WaitingReason != null) GUILayout.Label(ship.WaitingReason, UiKit.Warn);
            if (ship.BlockedReason != null) GUILayout.Label(ship.BlockedReason, UiKit.Bad);

            GUILayout.BeginHorizontal();
            bool following = _map.FollowShipId == ship.Id;
            if (GUILayout.Button(following ? "Stop following" : "Follow", following ? UiKit.BtnOn : UiKit.Btn)) _map.FollowShipId = following ? -1 : ship.Id;
            if (GUILayout.Button(_map.CloseUp ? "Back to map" : "Close-up view", _map.CloseUp ? UiKit.BtnOn : UiKit.Btn))
            {
                if (_map.CloseUp) _map.ExitCloseUp(); else _map.EnterCloseUp(ship.Id);
            }
            if (Sim.LastReceiptFor(ship.Id) != null && GUILayout.Button("Last receipt", UiKit.Btn)) ShowReceiptFor(ship.Id);
            GUILayout.EndHorizontal();

            GUILayout.Label("Drive", UiKit.H2);
            KV("Installed", ship.Drive.Name);
            KV("Fuel burn", Fmt.N2(ship.Drive.FuelPerLyPer100t) + " t per ly per 100 t loaded");

            if (ship.Voyage != null)
            {
                var v = ship.Voyage;
                double p = v.Progress(S.Now);
                GUILayout.Label("Voyage", UiKit.H2);
                KV("Route", g.Get(v.Origin).Name + " → " + g.Get(v.Destination).Name + (v.Automated ? "  (automated)" : v.Emergency ? "  (emergency contract)" : ""));
                BarRow("Progress", (float)p, Fmt.Pct(p), col);
                KV("Distance", Fmt.N2(v.DistanceLy * p) + " of " + Fmt.N2(v.DistanceLy) + " ly at " + Fmt.N2(v.DriveRatingC) + "c");
                KV("Departed", GameTime.FormatDate(v.DepartTick));
                KV("Arrives (game date)", GameTime.FormatDate(v.ArriveTick));
                KV("Remaining (real)", RealEta(ship));
                KV("Remaining (game)", GameTime.FormatGameDuration(v.ArriveTick - S.Now));
                GUILayout.Label("Manifest", UiKit.H2);
                if (v.Freight.Count == 0 && v.Passengers == 0 && !v.Emergency) GUILayout.Label("Empty repositioning leg.", UiKit.Muted);
                foreach (var f in v.Freight)
                    KV(Catalog.Commodity(f.Commodity).Name, f.Tonnes + " t × " + f.RatePerTonne + " cr = " + Fmt.Credits(f.Amount));
                if (v.Emergency) KV("Relief contract", Fmt.Credits(v.EmergencyPayment) + " (less " + Fmt.Credits(v.EmergencyAdvance) + " advanced)");
                if (v.Passengers > 0) KV("Passengers", v.Passengers + " × " + v.FarePerPassenger + " cr = " + Fmt.Credits(v.FareRevenue));
                KV("Crew / passengers", hull.Crew + " crew, " + v.Passengers + " passengers");
                GUILayout.Label("Supplies remaining", UiKit.H2);
                SupplyBar("Fuel", ship.Supplies.Fuel.Quantity + v.FuelUsed * (1 - p), hull.TankTonnes);
                SupplyBar("Food", ship.Supplies.Food.Quantity + v.FoodUsed * (1 - p), Math.Max(1, v.FoodUsed + ship.Supplies.Food.Quantity));
                SupplyBar("Water", ship.Supplies.Water.Quantity + v.WaterUsed * (1 - p), Math.Max(1, v.WaterUsed + ship.Supplies.Water.Quantity));
            }
            else
            {
                var here = g.Get(ship.Location);
                GUILayout.Label("Docked", UiKit.H2);
                KV("Port", here.Name + " (" + here.Role.Title + ")");
                KV("Crew", hull.Crew.ToString());
                GUILayout.Label("Supplies aboard", UiKit.H2);
                SupplyBar("Fuel", ship.Supplies.Fuel.Quantity, hull.TankTonnes);
                SupplyBar("Food", ship.Supplies.Food.Quantity, hull.HoldTonnes);
                SupplyBar("Water", ship.Supplies.Water.Quantity, hull.HoldTonnes);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Plan voyage", UiKit.BtnOn)) OpenPlanner();
                if (GUILayout.Button("View " + here.Name, UiKit.Btn)) SelectSystem(here.Id);
                GUILayout.EndHorizontal();
                var offer = Sim.EmergencyOffer(ship.Id);
                if (offer != null)
                {
                    GUILayout.BeginVertical(UiKit.Card);
                    GUILayout.Label(UiKit.Colored("Emergency contract available", UiKit.WarnC), UiKit.Label);
                    GUILayout.Label(string.Format("The company cannot afford to provision any job here. Carry relief supplies to {0} ({1} ly, {2}). Provisions worth {3} are supplied in kind for this departure only; you are paid {4} on delivery after they are deducted.",
                        g.Get(offer.Destination).Name, Fmt.N2(offer.DistanceLy), GameTime.FormatGameDuration(offer.DurationTicks),
                        Fmt.Credits(offer.EmergencyAdvance), Fmt.Credits(offer.EmergencyPayment - offer.EmergencyAdvance - offer.PortFee)), UiKit.Small);
                    if (GUILayout.Button("Accept emergency contract", UiKit.BtnOn))
                    {
                        _boot.Session.Update();
                        Report(Sim.AcceptEmergency(ship.Id));
                        _boot.SaveNow();
                    }
                    GUILayout.EndVertical();
                }
            }

            GUILayout.Label("Orders", UiKit.H2);
            if (ship.Route != null)
            {
                var names = new List<string>();
                for (int i = 0; i < ship.Route.Stops.Count; i++)
                    names.Add((i == ship.Route.CurrentStop ? "> " : "") + g.Get(ship.Route.Stops[i].SystemId).Name);
                KV("Route", ship.Route.Name);
                GUILayout.Label(string.Join(" → ", names.ToArray()) + " → (repeat)", UiKit.Small);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Edit route", UiKit.Btn)) OpenRouteEditor();
                if (GUILayout.Button("Switch to manual", UiKit.Btn)) { Report(Sim.ClearRoute(ship.Id)); _boot.SaveNow(); }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label(ship.Docked ? "Manual orders: the ship stays docked until you dispatch it." : "Manual voyage: the ship will stay docked on arrival.", UiKit.Muted);
                if (GUILayout.Button("Set up automated route", UiKit.Btn)) OpenRouteEditor();
            }

            GUILayout.Label("Shipyards", UiKit.H2);
            GUILayout.Label("Drive upgrades are installed only at a yard where this ship is docked. You can open any yard to compare.", UiKit.Muted);
            GUILayout.BeginHorizontal();
            foreach (var y in S.Yards)
                if (GUILayout.Button(g.Get(y.SystemId).Name + (ship.Docked && ship.Location == y.SystemId ? " (here)" : ""), UiKit.BtnSmall)) OpenYard(y.SystemId);
            GUILayout.EndHorizontal();
        }

        private static void SupplyBar(string label, double qty, double cap)
        {
            float frac = cap > 0 ? (float)(qty / cap) : 0;
            BarRow(label, frac, Fmt.T(qty), frac < 0.15f ? UiKit.WarnC : UiKit.GoodC);
        }

        // ------------------------------------------------------------------ system view

        private void DrawSystemPanel()
        {
            if (_systemId < 0) return;
            var g = Sim.Galaxy;
            var sys = g.Get(_systemId);
            GUILayout.Label(UiKit.Colored("● ", MapView.RoleColor(sys.Role)) + sys.Name, UiKit.Title);
            GUILayout.Label(sys.Role.Title + " · (" + Fmt.N2(sys.X) + ", " + Fmt.N2(sys.Y) + ") ly", UiKit.Muted);
            var ship = SelectedShip;
            if (ship != null) KV("From " + ship.Name, Fmt.N2(g.Distance(ship.Docked ? ship.Location : ship.Voyage.Destination, sys.Id)) + " ly");
            KV("Industries", string.Join(", ", sys.Role.Industries));

            GUILayout.Label("Exports (freight available here)", UiKit.H2);
            if (sys.Role.Exports.Length == 0) GUILayout.Label("None", UiKit.Muted);
            foreach (var e in sys.Role.Exports)
            {
                var p = S.SupplyAt(sys.Id, e);
                KV(Catalog.Commodity(e).Name, p.Available + " t available (max " + p.Max + ", +" + p.PerTick * 20 + " t/yr)");
            }
            GUILayout.Label("Imports (delivery demand)", UiKit.H2);
            foreach (var i in sys.Role.Imports)
            {
                var p = S.DemandAt(sys.Id, i);
                KV(Catalog.Commodity(i).Name, p.Available + " t wanted" + (p.Reserved > 0 ? ", " + p.Reserved + " t reserved in flight" : ""));
            }

            GUILayout.Label("Job board (contracted rates)", UiKit.H2);
            int jobs = 0;
            foreach (var e in sys.Role.Exports)
            {
                var sp = S.SupplyAt(sys.Id, e);
                foreach (var d in g.Systems)
                {
                    if (d.Id == sys.Id) continue;
                    var dp = S.DemandAt(d.Id, e);
                    double dist = sys.DistanceTo(d);
                    if (dp == null || dist > Catalog.MaxContractRangeLy) continue;
                    jobs++;
                    GUILayout.Label(string.Format("{0} → <b>{1}</b> · {2} ly · {3} t open · {4} cr/t", Catalog.Commodity(e).Name, d.Name, Fmt.N2(dist),
                        Math.Min(sp.Available, dp.Available), Catalog.Commodity(e).ContractRate(dist)), UiKit.Small);
                }
            }
            foreach (var b in S.PassengerBookings)
            {
                if (b.Origin != sys.Id || b.Available <= 0) continue;
                jobs++;
                double dist = g.Distance(b.Origin, b.Destination);
                GUILayout.Label(string.Format("Passengers → <b>{0}</b> · {1} ly · {2} booked · {3} cr each", g.Get(b.Destination).Name, Fmt.N2(dist), b.Available, Planner.Fare(dist)), UiKit.Small);
            }
            if (jobs == 0) GUILayout.Label("No open jobs.", UiKit.Muted);

            GUILayout.Label("Provisions for sale (shipboard supplies)", UiKit.H2);
            KV("Fuel", sys.FuelPrice + " cr/t");
            KV("Food", sys.FoodPrice + " cr/t");
            KV("Water", sys.WaterPrice + " cr/t");

            GUILayout.Label("Docked ships", UiKit.H2);
            int docked = 0;
            foreach (var s in S.Ships)
                if (s.Docked && s.Location == sys.Id)
                {
                    docked++;
                    if (GUILayout.Button(s.Name + "  " + StatusText(s.Status), UiKit.Row)) SelectShip(s.Id, false);
                }
            if (docked == 0) GUILayout.Label("None", UiKit.Muted);

            GUILayout.Label("Shipyard", UiKit.H2);
            if (sys.HasShipyard) { if (GUILayout.Button("Open shipyard", UiKit.BtnOn)) OpenYard(sys.Id); }
            else GUILayout.Label("No shipyard facilities.", UiKit.Muted);
        }

        // ------------------------------------------------------------------ receipt

        private void DrawReceipt()
        {
            var r = _receipt;
            if (r == null) { GUILayout.Label("No receipt.", UiKit.Muted); return; }
            var g = Sim.Galaxy;
            GUILayout.Label("Delivery receipt", UiKit.Title);
            GUILayout.Label(r.ShipName + " · voyage #" + r.VoyageId + (r.Emergency ? " · emergency contract" : ""), UiKit.Muted);
            KV("Route", g.Get(r.Origin).Name + " → " + g.Get(r.Destination).Name);
            KV("Departed", GameTime.FormatDate(r.DepartTick));
            KV("Arrived", GameTime.FormatDate(r.ArriveTick));
            KV("Elapsed", GameTime.FormatGameDuration(r.ElapsedTicks) + " (" + GameTime.FormatRealDuration(GameTime.RealSecondsFor(r.ElapsedTicks, 1)) + " real at 1x)");
            GUILayout.Label("Revenue", UiKit.H2);
            foreach (var f in r.Freight) KV(Catalog.Commodity(f.Commodity).Name, f.Tonnes + " t × " + f.RatePerTonne + " = " + Fmt.Credits(f.Amount));
            if (r.Emergency) KV("Relief contract", Fmt.Credits(r.FreightRevenue));
            KV("Fares", r.Passengers + " × " + r.FarePerPassenger + " = " + Fmt.Credits(r.FareRevenue));
            KV("Freight revenue", Fmt.Credits(r.FreightRevenue));
            KV("<b>Gross</b>", "<b>" + Fmt.Credits(r.GrossRevenue) + "</b>");
            GUILayout.Label("Supplies consumed (at acquisition cost)", UiKit.H2);
            KV("Fuel", Fmt.T(r.FuelUsed) + " = " + Fmt.Credits(r.FuelCost));
            KV("Food", Fmt.T(r.FoodUsed) + " = " + Fmt.Credits(r.FoodCost));
            KV("Water", Fmt.T(r.WaterUsed) + " = " + Fmt.Credits(r.WaterCost));
            KV("Port fee", Fmt.Credits(r.PortFee));
            if (r.EmergencyAdvance > 0) KV("Advance deducted", Fmt.Credits(r.EmergencyAdvance) + " (provisions supplied in kind)");
            GUILayout.Label("Result", UiKit.H2);
            KV("<b>Net profit</b>", UiKit.Colored("<b>" + Fmt.Credits(r.NetProfit) + "</b>", r.NetProfit >= 0 ? UiKit.GoodC : UiKit.BadC));
            KV("Cash credited", Fmt.Credits(r.CashCredited));
            GUILayout.Label("Each supply cost is counted once, when it is consumed, even if bought at an earlier stop.", UiKit.Muted);
        }

        // ------------------------------------------------------------------ toasts, close-up card, labels

        private void DrawToasts(float w)
        {
            float y = 52;
            foreach (var t in _toasts)
            {
                var n = t.Key;
                var r = new Rect(w * 0.5f - 230, y, 460, 30);
                Color c = n.Kind == NoticeKind.Blocked ? UiKit.BadC : n.Kind == NoticeKind.Arrival ? UiKit.GoodC : UiKit.Accent;
                if (GUI.Button(r, UiKit.Colored(n.Text, c), UiKit.Row) && n.ShipId >= 0 && S.Ship(n.ShipId) != null)
                {
                    SelectShip(n.ShipId, true);
                    if (n.Kind == NoticeKind.Arrival) ShowReceiptFor(n.ShipId);
                }
                y += 34;
            }
        }

        private void DrawCloseUpCard()
        {
            if (!_map.CloseUp) return;
            var ship = S.Ship(_map.FollowShipId);
            if (ship == null) return;
            GUI.Box(_cardRect, GUIContent.none, UiKit.Panel);
            GUILayout.BeginArea(new Rect(_cardRect.x + 12, _cardRect.y + 8, _cardRect.width - 24, _cardRect.height - 16));
            var col = MapView.ShipPalette[ship.ColorIndex % MapView.ShipPalette.Length];
            GUILayout.Label(UiKit.Colored("■ ", col) + ship.Name + "  <size=12>" + ship.Hull.ClassName + " · " + ship.Drive.Name + "</size>", UiKit.Title);
            GUILayout.Label(StatusText(ship.Status) + "   " + ShipOneLiner(ship), UiKit.Small);
            if (ship.Voyage != null)
            {
                BarRow("Progress", (float)ship.Voyage.Progress(S.Now), Fmt.Pct(ship.Voyage.Progress(S.Now)), col);
                GUILayout.Label("Arrives " + GameTime.FormatDate(ship.Voyage.ArriveTick) + " · " + RealEta(ship) + " · " + ship.Voyage.FreightTonnes + " t freight, " + ship.Voyage.Passengers + " passengers", UiKit.Small);
            }
            if (GUILayout.Button("Back to map (Esc)", UiKit.BtnSmall, GUILayout.Width(150))) _map.ExitCloseUp();
            GUILayout.EndArea();
        }

        private Vector2 WorldToGui(Vector3 world)
        {
            var s = _map.WorldToScreen(world);
            return new Vector2(s.x / _scale, (Screen.height - s.y) / _scale);
        }

        private void DrawMapLabels()
        {
            if (_map.CloseUp) return;
            var g = Sim.Galaxy;
            float zoom = _map.Cam.orthographicSize;
            foreach (var sys in g.Systems)
            {
                var p = WorldToGui(new Vector3((float)sys.X, (float)sys.Y, 0));
                string text = "<b>" + sys.Name + "</b>" + (zoom < 9 ? "\n<size=10>" + sys.Role.Title + (sys.HasShipyard ? " · yard" : "") + "</size>" : "");
                var r = new Rect(p.x - 90, p.y + 12, 180, 34);
                var style = UiKit.Center;
                var old = GUI.color;
                GUI.color = new Color(0, 0, 0, 0.8f);
                GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
                GUI.color = sys.Id == _systemId ? Color.white : new Color(0.85f, 0.9f, 1f, 0.9f);
                GUI.Label(r, text, style);
                GUI.color = old;
            }
            foreach (var s in S.Ships)
            {
                if (zoom > 6 && s.Id != _shipId) continue;
                var p = WorldToGui(_map.ShipWorld(s.Id));
                GUI.Label(new Rect(p.x + 10, p.y - 18, 160, 18), "<size=11>" + s.Name + "</size>", UiKit.Small);
            }
        }

        // ------------------------------------------------------------------ modals

        private void DrawModal(float w, float h)
        {
            if (_modal == Modal.None) return;
            var shade = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, w, h), UiKit.White);
            GUI.color = shade;
            _modalRect = new Rect(w * 0.5f - 280, h * 0.5f - 250, 560, 500);
            GUI.Box(_modalRect, GUIContent.none, UiKit.Panel);
            GUILayout.BeginArea(new Rect(_modalRect.x + 16, _modalRect.y + 12, _modalRect.width - 32, _modalRect.height - 24));
            switch (_modal)
            {
                case Modal.Menu: DrawMenu(); break;
                case Modal.Help: DrawHelp(); break;
                case Modal.CatchUp: DrawCatchUp(); break;
            }
            GUILayout.EndArea();
        }

        private void DrawCatchUp()
        {
            var rep = _boot.PendingCatchUp;
            GUILayout.Label("Welcome back", UiKit.Title);
            if (rep == null) { _modal = Modal.None; return; }
            GUILayout.Label(rep.Summary(Sim.Galaxy), UiKit.Label);
            if (rep.Deliveries.Count > 0)
            {
                GUILayout.Label("Deliveries while away", UiKit.H2);
                for (int i = 0; i < rep.Deliveries.Count && i < 10; i++)
                {
                    var r = rep.Deliveries[i];
                    GUILayout.Label(string.Format("{0}: {1} → {2}, net {3}", r.ShipName, Sim.Galaxy.Get(r.Origin).Name, Sim.Galaxy.Get(r.Destination).Name, Fmt.Credits(r.NetProfit)), UiKit.Small);
                }
                if (rep.Deliveries.Count > 10) GUILayout.Label("… and " + (rep.Deliveries.Count - 10) + " more.", UiKit.Muted);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Continue", UiKit.BtnOn, GUILayout.Height(32))) { _boot.PendingCatchUp = null; _modal = Modal.None; }
        }

        private void DrawMenu()
        {
            GUILayout.Label("Menu", UiKit.Title);
            GUILayout.Label("Current save: " + (_boot.Slot == SaveStore.DemoSlot ? "demonstration company" : "campaign") + (_boot.LastSaveError != null ? UiKit.Colored("  (last save failed: " + _boot.LastSaveError + ")", UiKit.BadC) : ""), UiKit.Muted);
            GUILayout.Label("Saves: " + _boot.Store.Directory, UiKit.Muted);
            GUILayout.Space(8);
            if (GUILayout.Button("Resume", UiKit.BtnOn, GUILayout.Height(30))) _modal = Modal.None;
            if (GUILayout.Button("Save now", UiKit.Btn, GUILayout.Height(30))) { _boot.SaveNow(); Flash(_boot.LastSaveError ?? "Saved.", _boot.LastSaveError != null); _modal = Modal.None; }
            if (_boot.Slot != SaveStore.MainSlot && GUILayout.Button("Switch to campaign", UiKit.Btn, GUILayout.Height(30))) { _boot.OpenSlot(SaveStore.MainSlot, false, Scenarios.DefaultSeed); }
            if (_boot.Slot != SaveStore.DemoSlot && GUILayout.Button("Open demonstration company (separate save)", UiKit.Btn, GUILayout.Height(30))) { _boot.OpenSlot(SaveStore.DemoSlot, false, Scenarios.DefaultSeed); }
            if (_boot.Slot == SaveStore.DemoSlot && GUILayout.Button("Reset demonstration company", UiKit.Btn, GUILayout.Height(30))) { _boot.OpenSlot(SaveStore.DemoSlot, true, Scenarios.DefaultSeed); }
            GUILayout.Space(8);
            if (!_confirmNew)
            {
                if (GUILayout.Button("New campaign…", UiKit.Btn, GUILayout.Height(30))) _confirmNew = true;
            }
            else
            {
                GUILayout.Label(UiKit.Colored("This replaces your campaign save.", UiKit.WarnC), UiKit.Label);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Standard galaxy", UiKit.BtnOn, GUILayout.Height(30))) { _boot.OpenSlot(SaveStore.MainSlot, true, Scenarios.DefaultSeed); }
                if (GUILayout.Button("Random galaxy", UiKit.Btn, GUILayout.Height(30))) { _boot.OpenSlot(SaveStore.MainSlot, true, (ulong)DateTime.UtcNow.Ticks); }
                if (GUILayout.Button("Cancel", UiKit.Btn, GUILayout.Height(30))) _confirmNew = false;
                GUILayout.EndHorizontal();
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Save and quit", UiKit.Btn, GUILayout.Height(30)))
            {
                _boot.SaveNow();
                Application.Quit();
            }
        }

        private void DrawHelp()
        {
            GUILayout.Label("Controls", UiKit.Title);
            string[] lines =
            {
                "Left-click a star or ship: select it.  Left/right/middle-drag: pan.  Mouse wheel: zoom at cursor.",
                "Arrow keys / WASD: pan.  + / -: zoom.  Home: centre on selected ship.",
                "Space: pause / play.  1, 2, 3: speed 1x, 3x, 5x (1 real hour = 1 game year at 1x).",
                "F: follow selected ship.  C: close-up of selected ship.  Tab: next ship.  Esc: back / close.",
                "Fleet list: click a ship to select and follow it. Notifications: click to jump to the ship (arrivals open the receipt).",
                "Plan voyage: choose a destination, freight and passengers, review the quote, Provision, then Launch.",
                "Route: build a circuit of two or more stops with loads, minimum departure loads and auto-provisioning.",
                "Shipyards (ring around a star): buy ships, and install drives on ships docked there.",
                "The game keeps running while closed and catches up when reopened (a paused save stays paused).",
            };
            foreach (var l in lines) GUILayout.Label("• " + l, UiKit.Label);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", UiKit.BtnOn, GUILayout.Height(30))) _modal = Modal.None;
        }

        // ------------------------------------------------------------------ map input

        private bool OverUi(Vector2 p)
        {
            return _topRect.Contains(p) || _leftRect.Contains(p) || _rightRect.Contains(p) || _cardRect.Contains(p) || _modal != Modal.None || OverToast(p);
        }

        private bool OverToast(Vector2 p)
        {
            float w = Screen.width / _scale;
            return _toasts.Count > 0 && new Rect(w * 0.5f - 230, 52, 460, 34 * _toasts.Count).Contains(p);
        }

        private Vector2 GuiToScreen(Vector2 gui) { return new Vector2(gui.x * _scale, Screen.height - gui.y * _scale); }

        private void HandleInput()
        {
            var e = Event.current;
            if (e == null) return;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (OverUi(e.mousePosition)) break;
                    _pressOnMap = true;
                    _dragging = false;
                    _pressPos = e.mousePosition;
                    _pressButton = e.button;
                    GUIUtility.keyboardControl = 0;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (!_pressOnMap) break;
                    if ((e.mousePosition - _pressPos).magnitude > 4f) _dragging = true;
                    if (_dragging) _map.Pan(new Vector2(e.delta.x * _scale, -e.delta.y * _scale));
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (!_pressOnMap) break;
                    _pressOnMap = false;
                    if (!_dragging && _pressButton == 0) ClickMap(GuiToScreen(e.mousePosition));
                    _dragging = false;
                    e.Use();
                    break;
                case EventType.ScrollWheel:
                    if (OverUi(e.mousePosition)) break;
                    _map.Zoom(e.delta.y > 0 ? 1.15f : 1f / 1.15f, GuiToScreen(e.mousePosition));
                    e.Use();
                    break;
                case EventType.KeyDown:
                    if (GUIUtility.keyboardControl != 0 || _modal == Modal.Menu || _modal == Modal.Help) break;
                    if (HandleKey(e.keyCode)) e.Use();
                    break;
            }
        }

        private void ClickMap(Vector2 screen)
        {
            int ship, sys;
            _map.Pick(screen, out ship, out sys);
            if (ship >= 0) { SelectShip(ship, follow: false); }
            else if (sys >= 0) SelectSystem(sys);
        }

        private bool HandleKey(KeyCode k)
        {
            float step = 60f;
            switch (k)
            {
                case KeyCode.Space: TogglePause(); return true;
                case KeyCode.Alpha1: case KeyCode.Keypad1: SetSpeed(1); return true;
                case KeyCode.Alpha2: case KeyCode.Keypad2: SetSpeed(3); return true;
                case KeyCode.Alpha3: case KeyCode.Keypad3: SetSpeed(5); return true;
                case KeyCode.F: if (_shipId >= 0) _map.FollowShipId = _map.FollowShipId == _shipId ? -1 : _shipId; return true;
                case KeyCode.C: if (_shipId >= 0) { if (_map.CloseUp) _map.ExitCloseUp(); else _map.EnterCloseUp(_shipId); } return true;
                case KeyCode.Home: if (_shipId >= 0) { _map.FollowShipId = -1; _map.FocusOn(_map.ShipWorld(_shipId)); } return true;
                case KeyCode.Escape:
                    if (_modal == Modal.CatchUp) { _boot.PendingCatchUp = null; _modal = Modal.None; }
                    else if (_map.CloseUp) _map.ExitCloseUp();
                    else if (_panel != Panel.None) _panel = Panel.None;
                    else _modal = Modal.Menu;
                    return true;
                case KeyCode.Tab:
                    if (S.Ships.Count == 0) return true;
                    int idx = S.Ships.FindIndex(s => s.Id == _shipId);
                    SelectShip(S.Ships[(idx + 1) % S.Ships.Count].Id, follow: true);
                    return true;
                case KeyCode.LeftArrow: case KeyCode.A: _map.Pan(new Vector2(step, 0)); return true;
                case KeyCode.RightArrow: case KeyCode.D: _map.Pan(new Vector2(-step, 0)); return true;
                case KeyCode.UpArrow: case KeyCode.W: _map.Pan(new Vector2(0, -step)); return true;
                case KeyCode.DownArrow: case KeyCode.S: _map.Pan(new Vector2(0, step)); return true;
                case KeyCode.Equals: case KeyCode.KeypadPlus: _map.Zoom(1f / 1.2f, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)); return true;
                case KeyCode.Minus: case KeyCode.KeypadMinus: _map.Zoom(1.2f, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)); return true;
            }
            return false;
        }
    }
}
