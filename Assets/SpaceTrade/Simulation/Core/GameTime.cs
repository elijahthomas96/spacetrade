using System;
using System.Globalization;

namespace SpaceTrade.Sim
{
    /// <summary>
    /// Game time is an integer tick count. One tick is one real millisecond at 1x,
    /// so one game year (one real hour at 1x) is exactly 3,600,000 ticks.
    /// </summary>
    public static class GameTime
    {
        public const long TicksPerYear = 3600000L;
        public const int DaysPerYear = 365;
        public const int StartYear = 2400;
        public const double RealMillisecondsPerTickAt1x = 1.0;

        public static readonly int[] AllowedSpeeds = { 1, 3, 5 };

        public static bool IsAllowedSpeed(int speed)
        {
            return speed == 1 || speed == 3 || speed == 5;
        }

        public static long YearsToTicks(double years)
        {
            return (long)Math.Round(years * TicksPerYear, MidpointRounding.AwayFromZero);
        }

        public static double TicksToYears(long ticks)
        {
            return ticks / (double)TicksPerYear;
        }

        /// <summary>Alcubierre transit: game years = light-years / rating in multiples of c.</summary>
        public static double TransitYears(double distanceLy, double driveRatingC)
        {
            if (driveRatingC <= 0) throw new ArgumentOutOfRangeException("driveRatingC");
            return distanceLy / driveRatingC;
        }

        public static long TransitTicks(double distanceLy, double driveRatingC)
        {
            return YearsToTicks(TransitYears(distanceLy, driveRatingC));
        }

        /// <summary>Real seconds needed to cover the given game ticks at a speed multiplier.</summary>
        public static double RealSecondsFor(long ticks, int speed)
        {
            if (speed <= 0) throw new ArgumentOutOfRangeException("speed");
            return ticks * RealMillisecondsPerTickAt1x / 1000.0 / speed;
        }

        public static string FormatDate(long tick)
        {
            long year = StartYear + tick / TicksPerYear;
            long within = tick % TicksPerYear;
            if (within < 0) { within += TicksPerYear; year -= 1; }
            int day = (int)(within * DaysPerYear / TicksPerYear) + 1;
            long dayStart = (day - 1) * TicksPerYear / DaysPerYear;
            long dayLen = TicksPerYear / DaysPerYear;
            int hour = (int)Math.Min(23, (within - dayStart) * 24 / Math.Max(1, dayLen));
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1:000} {2:00}h", year, day, hour);
        }

        public static string FormatGameDuration(long ticks)
        {
            double years = TicksToYears(ticks);
            if (years >= 1.0) return years.ToString("0.00", CultureInfo.InvariantCulture) + " yr";
            double days = years * DaysPerYear;
            return days.ToString("0.0", CultureInfo.InvariantCulture) + " days";
        }

        public static string FormatRealDuration(double seconds)
        {
            if (seconds < 0) seconds = 0;
            long total = (long)Math.Round(seconds);
            long h = total / 3600, m = (total % 3600) / 60, s = total % 60;
            if (h > 0) return string.Format(CultureInfo.InvariantCulture, "{0}h {1:00}m {2:00}s", h, m, s);
            if (m > 0) return string.Format(CultureInfo.InvariantCulture, "{0}m {1:00}s", m, s);
            return string.Format(CultureInfo.InvariantCulture, "{0}s", s);
        }
    }

    public static class Fmt
    {
        public static string Credits(long cr)
        {
            return (cr < 0 ? "-" : "") + Math.Abs(cr).ToString("#,0", CultureInfo.InvariantCulture) + " cr";
        }

        public static string T(double tonnes)
        {
            return tonnes.ToString("0.0", CultureInfo.InvariantCulture) + " t";
        }

        public static string N1(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }
        public static string N2(double v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }
        public static string Pct(double fraction) { return (fraction * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%"; }
    }

    /// <summary>SplitMix64: deterministic across Mono, IL2CPP and CoreCLR (System.Random is not guaranteed to be).</summary>
    public sealed class DeterministicRng
    {
        private ulong _state;

        public DeterministicRng(ulong seed) { _state = seed; }

        public ulong NextULong()
        {
            ulong z = (_state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public double NextDouble() { return (NextULong() >> 11) * (1.0 / (1UL << 53)); }

        public int Range(int minInclusive, int maxExclusive)
        {
            return minInclusive + (int)(NextULong() % (ulong)(maxExclusive - minInclusive));
        }

        public double Range(double min, double max) { return min + (max - min) * NextDouble(); }
    }
}
