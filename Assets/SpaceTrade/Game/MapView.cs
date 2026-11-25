using System.Collections.Generic;
using SpaceTrade.Sim;
using UnityEngine;

namespace SpaceTrade.Game
{
    /// <summary>2D galaxy map: systems, ships at their simulated positions, active routes, camera control and the ship close-up.</summary>
    public sealed class MapView : MonoBehaviour
    {
        public static readonly Color[] ShipPalette =
        {
            new Color(0.35f, 0.85f, 1f), new Color(1f, 0.72f, 0.3f), new Color(0.6f, 1f, 0.5f), new Color(1f, 0.45f, 0.6f),
            new Color(0.8f, 0.6f, 1f), new Color(1f, 0.95f, 0.45f), new Color(0.4f, 1f, 0.85f), new Color(1f, 0.55f, 0.35f),
        };

        public Camera Cam { get; private set; }
        public int FollowShipId = -1;
        public bool CloseUp;
        public int SelectedShipId = -1;
        public int SelectedSystemId = -1;

        private const float MinSize = 0.8f, MaxSize = 16f, CloseUpSize = 0.55f;
        private float _targetSize = 10f;
        private float _mapSize = 10f;
        private Vector3 _mapPos;
        private Transform _root;
        private readonly Dictionary<int, Transform> _systems = new Dictionary<int, Transform>();
        private readonly Dictionary<int, SpriteRenderer> _ships = new Dictionary<int, SpriteRenderer>();
        private readonly List<SpriteRenderer> _lines = new List<SpriteRenderer>();
        private int _linesUsed;
        private SpriteRenderer _selectRing, _shipRing;
        private Transform _closeUpRoot;
        private SpriteRenderer _closeUpHull, _closeUpGlow;
        private readonly List<SpriteRenderer> _streaks = new List<SpriteRenderer>();
        private Simulation _builtFor;

        private GameBootstrap Boot { get { return GameBootstrap.Instance; } }

        public static Color RoleColor(RoleDef role)
        {
            switch (role.Id)
            {
                case "agri": return new Color(0.45f, 0.9f, 0.45f);
                case "mining": return new Color(0.9f, 0.6f, 0.3f);
                case "refinery": return new Color(0.65f, 0.7f, 0.85f);
                case "industrial": return new Color(0.75f, 0.75f, 0.7f);
                case "tech": return new Color(0.35f, 0.85f, 1f);
                case "core": return new Color(1f, 0.85f, 0.35f);
                default: return new Color(0.75f, 0.5f, 1f);
            }
        }

        private void Awake()
        {
            Cam = Camera.main;
            if (Cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                Cam = go.AddComponent<Camera>();
            }
            Cam.orthographic = true;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.02f, 0.03f, 0.07f);
            Cam.nearClipPlane = 0.1f;
            Cam.farClipPlane = 100f;
            Cam.transform.position = new Vector3(13, 9, -10);
            Cam.orthographicSize = _targetSize;
        }

        private void OnEnable() { if (Boot != null) Boot.WorldChanged += Rebuild; }
        private void OnDisable() { if (Boot != null) Boot.WorldChanged -= Rebuild; }

        private void Rebuild()
        {
            var sim = Boot.Sim;
            if (sim == null) return;
            _builtFor = sim;
            if (_root != null) Destroy(_root.gameObject);
            _root = new GameObject("Map").transform;
            _systems.Clear();
            _ships.Clear();
            _lines.Clear();
            _streaks.Clear();

            var rng = new DeterministicRng(sim.State.Seed ^ 0xABCDEFUL);
            var bg = new GameObject("Starfield").transform;
            bg.SetParent(_root, false);
            for (int i = 0; i < 500; i++)
            {
                var sr = NewSprite("star", SpriteFactory.Disc, bg, -5);
                sr.transform.position = new Vector3((float)rng.Range(-14.0, 40.0), (float)rng.Range(-12.0, 30.0), 5);
                float s = (float)rng.Range(0.02, 0.07);
                sr.transform.localScale = new Vector3(s, s, 1);
                sr.color = new Color(0.7f, 0.8f, 1f, (float)rng.Range(0.15, 0.6));
            }

            foreach (var sys in sim.Galaxy.Systems)
            {
                var t = new GameObject(sys.Name).transform;
                t.SetParent(_root, false);
                t.position = new Vector3((float)sys.X, (float)sys.Y, 0);
                var glow = NewSprite("glow", SpriteFactory.Glow, t, 0);
                glow.color = RoleColor(sys.Role) * new Color(1, 1, 1, 0.35f);
                glow.transform.localScale = Vector3.one * 1.6f;
                var disc = NewSprite("disc", SpriteFactory.Disc, t, 2);
                disc.color = RoleColor(sys.Role);
                disc.transform.localScale = Vector3.one * 0.42f;
                if (sys.HasShipyard)
                {
                    var yard = NewSprite("yard", SpriteFactory.Ring, t, 1);
                    yard.color = new Color(1, 1, 1, 0.5f);
                    yard.transform.localScale = Vector3.one * 0.7f;
                }
                _systems[sys.Id] = t;
            }

            _selectRing = NewSprite("selection", SpriteFactory.Ring, _root, 3);
            _selectRing.color = new Color(1f, 1f, 1f, 0.9f);
            _shipRing = NewSprite("shipSelection", SpriteFactory.Ring, _root, 9);
            _shipRing.color = new Color(1f, 1f, 1f, 0.85f);

            _closeUpRoot = new GameObject("CloseUp").transform;
            _closeUpRoot.SetParent(_root, false);
            for (int i = 0; i < 24; i++)
            {
                var st = NewSprite("streak", SpriteFactory.Streak, _closeUpRoot, 10);
                st.color = new Color(0.7f, 0.85f, 1f, 0.5f);
                _streaks.Add(st);
            }
            _closeUpGlow = NewSprite("engine", SpriteFactory.Glow, _closeUpRoot, 11);
            _closeUpHull = NewSprite("hull", SpriteFactory.ShipDetail, _closeUpRoot, 12);
            _closeUpRoot.gameObject.SetActive(false);

            var start = sim.Galaxy.Get(sim.Galaxy.StartSystemId);
            _mapPos = new Vector3((float)start.X, (float)start.Y, -10);
            _targetSize = _mapSize = 8f;
            Cam.transform.position = _mapPos;
            Cam.orthographicSize = _targetSize;
        }

        private SpriteRenderer NewSprite(string name, Sprite sprite, Transform parent, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        // ------------------------------------------------------------------ camera API (screen coords: pixels, origin bottom-left)

        public Vector2 ScreenToWorld(Vector2 screen)
        {
            var w = Cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10));
            return new Vector2(w.x, w.y);
        }

        public Vector2 WorldToScreen(Vector2 world)
        {
            var s = Cam.WorldToScreenPoint(new Vector3(world.x, world.y, 0));
            return new Vector2(s.x, s.y);
        }

        public void Zoom(float factor, Vector2 screenPos)
        {
            if (CloseUp) { if (factor > 1) ExitCloseUp(); return; }
            var before = ScreenToWorld(screenPos);
            _targetSize = Mathf.Clamp(_targetSize * factor, MinSize, MaxSize);
            Cam.orthographicSize = _targetSize;
            var after = ScreenToWorld(screenPos);
            if (FollowShipId < 0)
            {
                var d = before - after;
                Cam.transform.position += new Vector3(d.x, d.y, 0);
            }
        }

        public void Pan(Vector2 screenDelta)
        {
            if (CloseUp) return;
            FollowShipId = -1;
            float unitsPerPixel = Cam.orthographicSize * 2f / Screen.height;
            Cam.transform.position -= new Vector3(screenDelta.x * unitsPerPixel, screenDelta.y * unitsPerPixel, 0);
        }

        public void FocusOn(Vector2 world)
        {
            if (CloseUp) return;
            Cam.transform.position = new Vector3(world.x, world.y, -10);
        }

        public void EnterCloseUp(int shipId)
        {
            if (!CloseUp) { _mapSize = _targetSize; _mapPos = Cam.transform.position; }
            CloseUp = true;
            FollowShipId = shipId;
            SelectedShipId = shipId;
        }

        public void ExitCloseUp()
        {
            if (!CloseUp) return;
            CloseUp = false;
            _targetSize = _mapSize;
            Cam.orthographicSize = _targetSize;
        }

        /// <summary>World units per screen pixel; map icons are sized in pixels so they read the same at every zoom.</summary>
        public float IconScale { get { return Cam.orthographicSize * 2f / Mathf.Max(1, Screen.height); } }

        /// <summary>Returns the ship or system under the cursor, preferring ships.</summary>
        public void Pick(Vector2 screen, out int shipId, out int systemId)
        {
            shipId = -1; systemId = -1;
            var sim = Boot.Sim;
            if (sim == null) return;
            float best = 18f; // pixels
            foreach (var kv in _ships)
            {
                var p = WorldToScreen(kv.Value.transform.position);
                float d = Vector2.Distance(p, screen);
                if (d < best) { best = d; shipId = kv.Key; }
            }
            if (shipId >= 0) return;
            best = 24f;
            foreach (var kv in _systems)
            {
                var p = WorldToScreen(kv.Value.position);
                float d = Vector2.Distance(p, screen);
                if (d < best) { best = d; systemId = kv.Key; }
            }
        }

        public Vector3 ShipWorld(int shipId)
        {
            SpriteRenderer sr;
            return _ships.TryGetValue(shipId, out sr) ? sr.transform.position : Vector3.zero;
        }

        // ------------------------------------------------------------------ per-frame sync

        private void LateUpdate()
        {
            var sim = Boot != null ? Boot.Sim : null;
            if (sim == null) return;
            if (_builtFor != sim) Rebuild();
            _linesUsed = 0;
            float icon = IconScale;

            foreach (var kv in _systems)
            {
                var disc = kv.Value;
                float k = Mathf.Max(0.25f, icon * 16f);
                disc.GetChild(1).localScale = Vector3.one * k;
                disc.GetChild(0).localScale = Vector3.one * k * 3.6f;
                if (disc.childCount > 2) disc.GetChild(2).localScale = Vector3.one * k * 1.7f;
            }

            // Route circuits (faint) and active legs (bright), in the ship's colour.
            foreach (var ship in sim.State.Ships)
            {
                var col = ShipPalette[ship.ColorIndex % ShipPalette.Length];
                if (ship.Route != null && ship.Route.Stops.Count >= 2)
                    for (int i = 0; i < ship.Route.Stops.Count; i++)
                    {
                        var a = sim.Galaxy.Get(ship.Route.Stops[i].SystemId);
                        var b = sim.Galaxy.Get(ship.Route.Stops[(i + 1) % ship.Route.Stops.Count].SystemId);
                        Line(new Vector2((float)a.X, (float)a.Y), new Vector2((float)b.X, (float)b.Y), icon * 1.5f, new Color(col.r, col.g, col.b, 0.22f), 1);
                    }
                if (ship.Voyage != null)
                {
                    var o = sim.Galaxy.Get(ship.Voyage.Origin);
                    var d = sim.Galaxy.Get(ship.Voyage.Destination);
                    var pos = sim.ShipPosition(ship);
                    Line(new Vector2((float)o.X, (float)o.Y), new Vector2((float)pos.X, (float)pos.Y), icon * 2.5f, new Color(col.r, col.g, col.b, 0.75f), 4);
                    Line(new Vector2((float)pos.X, (float)pos.Y), new Vector2((float)d.X, (float)d.Y), icon * 1.5f, new Color(col.r, col.g, col.b, 0.35f), 4);
                }
            }
            for (int i = _linesUsed; i < _lines.Count; i++) _lines[i].enabled = false;

            // Ships.
            var dockedCount = new Dictionary<int, int>();
            var alive = new HashSet<int>();
            foreach (var ship in sim.State.Ships)
            {
                alive.Add(ship.Id);
                SpriteRenderer sr;
                if (!_ships.TryGetValue(ship.Id, out sr))
                {
                    sr = NewSprite("ship " + ship.Id, SpriteFactory.ShipIcon, _root, 8);
                    _ships[ship.Id] = sr;
                }
                sr.color = ShipPalette[ship.ColorIndex % ShipPalette.Length];
                var p = sim.ShipPosition(ship);
                Vector3 world = new Vector3((float)p.X, (float)p.Y, -0.5f);
                float angle;
                if (ship.Voyage != null)
                {
                    var d = sim.Galaxy.Get(ship.Voyage.Destination);
                    angle = Mathf.Atan2((float)(d.Y - p.Y), (float)(d.X - p.X)) * Mathf.Rad2Deg;
                }
                else
                {
                    int k;
                    dockedCount.TryGetValue(ship.Location, out k);
                    dockedCount[ship.Location] = k + 1;
                    float a = (k * 55f + 90f) * Mathf.Deg2Rad;
                    float r = Mathf.Max(0.25f, icon * 16f) * 0.6f + icon * 10f;
                    world += new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0);
                    angle = k * 55f + 180f;
                }
                sr.transform.position = world;
                sr.transform.rotation = Quaternion.Euler(0, 0, angle);
                sr.transform.localScale = Vector3.one * icon * 20f;
                sr.enabled = !(CloseUp && ship.Id == FollowShipId);
            }
            var dead = new List<int>();
            foreach (var id in _ships.Keys) if (!alive.Contains(id)) dead.Add(id);
            foreach (var id in dead) { Destroy(_ships[id].gameObject); _ships.Remove(id); }

            // Selection markers.
            _selectRing.enabled = SelectedSystemId >= 0 && _systems.ContainsKey(SelectedSystemId);
            if (_selectRing.enabled)
            {
                _selectRing.transform.position = _systems[SelectedSystemId].position;
                _selectRing.transform.localScale = Vector3.one * Mathf.Max(0.25f, icon * 16f) * 2.3f;
            }
            _shipRing.enabled = SelectedShipId >= 0 && _ships.ContainsKey(SelectedShipId) && !CloseUp;
            if (_shipRing.enabled)
            {
                _shipRing.transform.position = _ships[SelectedShipId].transform.position;
                _shipRing.transform.localScale = Vector3.one * icon * 34f;
                _shipRing.transform.Rotate(0, 0, 40f * Time.unscaledDeltaTime);
            }

            UpdateCamera(sim);
            UpdateCloseUp(sim);
        }

        private void UpdateCamera(Simulation sim)
        {
            var ship = FollowShipId >= 0 ? sim.State.Ship(FollowShipId) : null;
            if (FollowShipId >= 0 && ship == null) { FollowShipId = -1; CloseUp = false; }
            float size = CloseUp ? CloseUpSize : _targetSize;
            Cam.orthographicSize = Mathf.Lerp(Cam.orthographicSize, size, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            if (ship != null)
            {
                var target = _ships[ship.Id].transform.position;
                var cur = Cam.transform.position;
                var goal = new Vector3(target.x, target.y, -10);
                Cam.transform.position = Vector3.Lerp(cur, goal, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            }
        }

        private void UpdateCloseUp(Simulation sim)
        {
            var ship = CloseUp && FollowShipId >= 0 ? sim.State.Ship(FollowShipId) : null;
            _closeUpRoot.gameObject.SetActive(ship != null);
            if (ship == null) return;
            var col = ShipPalette[ship.ColorIndex % ShipPalette.Length];
            var center = _ships[ship.Id].transform.position;
            float angle = _ships[ship.Id].transform.eulerAngles.z;
            _closeUpRoot.position = new Vector3(center.x, center.y, -1);
            _closeUpRoot.rotation = Quaternion.Euler(0, 0, angle);
            float hullLen = ship.HullId == "freighter" ? 0.95f : ship.HullId == "liner" ? 0.85f : 0.65f;
            _closeUpHull.transform.localScale = new Vector3(hullLen, hullLen * (ship.HullId == "freighter" ? 0.9f : 0.7f), 1);
            _closeUpHull.color = Color.Lerp(Color.white, col, 0.45f);
            bool moving = ship.Voyage != null && !sim.State.Paused;
            float flicker = moving ? 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 31f) : 0.25f;
            _closeUpGlow.transform.localPosition = new Vector3(-hullLen * 0.47f, 0, 0);
            _closeUpGlow.transform.localScale = Vector3.one * hullLen * 0.6f * flicker;
            _closeUpGlow.color = new Color(col.r, col.g, col.b, 0.9f);
            // Warp streaks stream past while the ship is in transit and the clock runs; speed scales with the game speed.
            float speed = moving ? 1.2f * sim.State.Speed : 0f;
            for (int i = 0; i < _streaks.Count; i++)
            {
                var st = _streaks[i];
                st.enabled = ship.Voyage != null;
                float lane = ((i * 7919) % 100) / 100f - 0.5f;
                float phase = ((i * 104729) % 100) / 100f;
                float x = Mathf.Repeat(phase * 3f - Time.unscaledTime * speed * (0.6f + 0.4f * phase), 3f) - 1.5f;
                st.transform.localPosition = new Vector3(x, lane * 1.6f, 0.1f);
                st.transform.localRotation = Quaternion.Euler(0, 0, 180f);
                st.transform.localScale = new Vector3(0.25f + 0.25f * phase, 0.02f, 1);
            }
        }

        private void Line(Vector2 a, Vector2 b, float width, Color c, int order)
        {
            SpriteRenderer sr;
            if (_linesUsed < _lines.Count) sr = _lines[_linesUsed];
            else { sr = NewSprite("line", SpriteFactory.Pixel, _root, order); _lines.Add(sr); }
            _linesUsed++;
            var d = b - a;
            float len = d.magnitude;
            sr.enabled = len > 1e-4f;
            sr.sortingOrder = order;
            sr.color = c;
            sr.transform.position = new Vector3((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f, 0.5f);
            sr.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            sr.transform.localScale = new Vector3(len, width, 1);
        }
    }
}
