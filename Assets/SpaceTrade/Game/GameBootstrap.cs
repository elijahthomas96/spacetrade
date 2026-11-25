using System;
using SpaceTrade.Sim;
using UnityEngine;

namespace SpaceTrade.Game
{
    /// <summary>
    /// Debug features (skip to next arrival) exist only in the Editor and Development Builds.
    /// In a normal player build this compiles to a constant false.
    /// </summary>
    public static class DebugGate
    {
        public static bool Allowed
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return Application.isEditor || Debug.isDebugBuild;
#else
                return false;
#endif
            }
        }
    }

    /// <summary>
    /// Scene entry point. Owns the save store and the simulation session, drives the clock every frame,
    /// and builds the map and interface from code so the scene file stays minimal.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }

        public SimulationSession Session { get; private set; }
        public Simulation Sim { get { return Session != null ? Session.Sim : null; } }
        public SaveStore Store { get; private set; }
        public string Slot { get; private set; }
        public CatchUpReport PendingCatchUp { get; set; }
        public string LastSaveError { get; private set; }
        public DateTime LastSaveUtc { get; private set; }

        public event Action WorldChanged;

        private readonly StopwatchClock _mono = new StopwatchClock();
        private readonly SystemWallClock _wall = new SystemWallClock();
        private float _autosaveTimer;
        private const float AutosaveSeconds = 30f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrap()
        {
            // Safety net: the startup scene contains a GameBootstrap, but the game still starts if the scene reference breaks.
            if (FindAnyObjectByType<GameBootstrap>() == null)
                new GameObject("GameBootstrap").AddComponent<GameBootstrap>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = 120;
        }

        private void Start()
        {
            Store = new SaveStore(Application.persistentDataPath);
            gameObject.AddComponent<MapView>();
            gameObject.AddComponent<GameUI>();
            OpenSlot(SaveStore.MainSlot, false, Scenarios.DefaultSeed);
        }

        /// <summary>Opens a save slot. Existing saves are caught up for time spent closed and immediately re-saved with their new stamp.</summary>
        public void OpenSlot(string slot, bool forceNew, ulong seed)
        {
            if (Session != null) SaveNow();
            GameState state = null;
            bool loaded = false;
            if (!forceNew && Store.Exists(slot))
            {
                try { state = Store.Load(slot); loaded = true; }
                catch (Exception e) { Debug.LogError("Could not load save '" + slot + "': " + e.Message); }
            }
            if (state == null)
                state = slot == SaveStore.DemoSlot ? Scenarios.Demo(seed, _wall.UtcNow) : Scenarios.NewGame(seed, _wall.UtcNow);

            Session = new SimulationSession(new Simulation(state, DebugGate.Allowed), _mono, _wall);
            Slot = slot;
            PendingCatchUp = null;
            if (loaded)
            {
                var report = Session.CatchUpOffline();
                if (report.WasPaused || report.GameTicksAdvanced > 0 || report.ClockWentBackward) PendingCatchUp = report;
            }
            SaveNow(); // the catch-up result and its timestamp are persisted together
            var h = WorldChanged;
            if (h != null) h();
        }

        public void SaveNow()
        {
            if (Session == null || Store == null) return;
            try
            {
                Session.StampForSave();
                Store.Save(Slot, Session.State);
                LastSaveError = null;
                LastSaveUtc = DateTime.UtcNow;
            }
            catch (Exception e)
            {
                LastSaveError = e.Message;
                Debug.LogError("Save failed: " + e);
            }
        }

        private void Update()
        {
            if (Session == null) return;
            Session.Update();
            _autosaveTimer += Time.unscaledDeltaTime;
            if (_autosaveTimer >= AutosaveSeconds)
            {
                _autosaveTimer = 0;
                SaveNow();
            }
        }

        private void OnApplicationPause(bool paused) { if (paused) SaveNow(); }
        private void OnApplicationFocus(bool focused) { if (!focused) SaveNow(); }
        private void OnApplicationQuit() { SaveNow(); }
    }
}
