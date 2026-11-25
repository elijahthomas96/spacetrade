using System;
using System.Collections.Generic;

namespace SpaceTrade.Sim
{
    /// <summary>Monotonic real-time source (never jumps with system clock changes). Unity uses a Stopwatch.</summary>
    public interface IMonotonicClock
    {
        /// <summary>Elapsed real time in 100 ns units since an arbitrary origin.</summary>
        long ElapsedTicks100ns { get; }
    }

    /// <summary>Wall clock for offline catch-up between sessions.</summary>
    public interface IWallClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class SystemWallClock : IWallClock
    {
        public DateTime UtcNow { get { return DateTime.UtcNow; } }
    }

    public sealed class StopwatchClock : IMonotonicClock
    {
        private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
        public long ElapsedTicks100ns { get { return (long)(_sw.ElapsedTicks * (10000000.0 / System.Diagnostics.Stopwatch.Frequency)); } }
    }

    /// <summary>Manually driven clocks for tests and tools.</summary>
    public sealed class ManualClock : IMonotonicClock, IWallClock
    {
        public long ElapsedTicks100ns { get; set; }
        public DateTime UtcNow { get; set; }

        public ManualClock(DateTime utcNow) { UtcNow = utcNow; }

        public void AdvanceReal(TimeSpan span)
        {
            ElapsedTicks100ns += span.Ticks;
            UtcNow = UtcNow + span;
        }
    }

    public sealed class CatchUpReport
    {
        public TimeSpan RealElapsed;
        public long GameTicksAdvanced;
        public bool WasPaused;
        public bool ClockWentBackward;
        public List<Receipt> Deliveries = new List<Receipt>();
        public long Earnings;
        public List<string> NeedsAttention = new List<string>();

        public string Summary(Galaxy g)
        {
            if (WasPaused) return "The save was paused; no game time passed while the game was closed.";
            var parts = new List<string>();
            parts.Add(string.Format("Away {0} real time ({1} game time).", GameTime.FormatRealDuration(RealElapsed.TotalSeconds), GameTime.FormatGameDuration(GameTicksAdvanced)));
            if (ClockWentBackward) parts.Add("The system clock moved backward; no time was applied and game time did not reverse.");
            parts.Add(string.Format("{0} deliveries, net earnings {1}.", Deliveries.Count, Fmt.Credits(Earnings)));
            if (NeedsAttention.Count > 0) parts.Add("Needs attention: " + string.Join("; ", NeedsAttention.ToArray()) + ".");
            return string.Join(" ", parts.ToArray());
        }
    }

    /// <summary>
    /// Couples the simulation to real time. The speed multiplier is applied exactly once, here.
    /// Elapsed real time comes from a monotonic clock, independent of frame rate.
    /// </summary>
    public sealed class SimulationSession
    {
        public readonly Simulation Sim;
        private readonly IMonotonicClock _mono;
        private readonly IWallClock _wall;
        private long _lastMono;
        private long _carry; // speed-scaled 100 ns remainder below one game tick

        private const long Units100nsPerTick = 10000; // one tick = 1 ms of real time at 1x

        public SimulationSession(Simulation sim, IMonotonicClock mono, IWallClock wall)
        {
            Sim = sim;
            _mono = mono;
            _wall = wall;
            _lastMono = mono.ElapsedTicks100ns;
        }

        public GameState State { get { return Sim.State; } }

        /// <summary>Call every frame. Converts real elapsed time to game ticks at the current speed and advances the simulation.</summary>
        public void Update()
        {
            long nowMono = _mono.ElapsedTicks100ns;
            long delta = nowMono - _lastMono;
            _lastMono = nowMono;
            if (delta <= 0 || State.Paused) return;
            _carry += delta * State.Speed;
            long ticks = _carry / Units100nsPerTick;
            _carry -= ticks * Units100nsPerTick;
            if (ticks > 0) Sim.AdvanceTo(State.Now + ticks);
        }

        public void SetSpeed(int speed)
        {
            Update(); // time so far is applied at the old speed
            Sim.SetSpeed(speed);
        }

        public void SetPaused(bool paused)
        {
            Update();
            Sim.SetPaused(paused);
            _carry = 0;
        }

        /// <summary>
        /// Applies real time elapsed since the save's wall-clock stamp at the saved speed. Paused saves stay paused.
        /// A backward clock change applies no time and the stamp never moves backward, so time cannot be granted twice.
        /// </summary>
        public CatchUpReport CatchUpOffline()
        {
            var report = new CatchUpReport { WasPaused = State.Paused };
            long nowWall = _wall.UtcNow.Ticks;
            long stamp = State.LastWallClockUtcTicks;
            if (stamp == 0) stamp = nowWall;
            long elapsed = nowWall - stamp;
            if (elapsed < 0) { report.ClockWentBackward = true; elapsed = 0; }
            report.RealElapsed = TimeSpan.FromTicks(elapsed);

            if (!State.Paused && elapsed > 0)
            {
                long gameTicks = elapsed * State.Speed / Units100nsPerTick;
                Action<Receipt> collect = r => { report.Deliveries.Add(r); report.Earnings += r.NetProfit; };
                Sim.Settled += collect;
                long before = State.Now;
                Sim.AdvanceTo(State.Now + gameTicks);
                Sim.Settled -= collect;
                report.GameTicksAdvanced = State.Now - before;
            }
            State.LastWallClockUtcTicks = Math.Max(stamp, nowWall);
            foreach (var s in State.Ships)
            {
                if (s.Status == ShipStatus.Blocked) report.NeedsAttention.Add(s.Name + " blocked: " + s.BlockedReason);
                else if (s.Status == ShipStatus.Idle) report.NeedsAttention.Add(s.Name + " idle at " + Sim.Galaxy.Get(s.Location).Name);
            }
            State.LastCatchUpSummary = report.Summary(Sim.Galaxy);
            _lastMono = _mono.ElapsedTicks100ns;
            _carry = 0;
            return report;
        }

        /// <summary>Stamps the state with the current wall clock (never backward) right before it is written.</summary>
        public void StampForSave()
        {
            Update();
            long nowWall = _wall.UtcNow.Ticks;
            State.LastWallClockUtcTicks = Math.Max(State.LastWallClockUtcTicks, nowWall);
        }
    }
}
