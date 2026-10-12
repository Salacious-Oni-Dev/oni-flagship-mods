namespace Mod1ThermoFluid
{
    /// <summary>
    /// The clock every Mod 1 device that works "once per interval" runs on: SIM time, delivered by
    /// vanilla's <c>ISim200ms</c> / <c>ISim1000ms</c> buckets, with the overshoot kept.
    ///
    /// WHY. These used to be
    /// devices counted <c>Time.deltaTime</c> in <c>Update()</c> and reset to 0 on firing. That was
    /// wrong in two directions, and both scaled with the player's frame rate:
    ///   - the reset threw away up to one frame of overshoot per firing, about d/(2I+d) of the work
    ///     (0.3 % at 1x and 170 fps; ~4 % for the 0.2 s liquid reservoir at 60 fps; ~20 % at 3x and
    ///     30 fps);
    ///   - <c>Time.deltaTime</c> (capped at 1/3 s) is not what the sim advanced
    ///     (<c>Game.SimEveryTick</c> clamps a frame to 0.2 s), so a hitch frame credited time the sim
    ///     never simulated.
    /// So a gas pump's or a reservoir's throughput depended on the frame rate.
    ///
    /// DIVERGENCE REGISTER against Stationeers (devices act in <c>OnAtmosphericTick</c>, called once per
    /// game tick by GameManager's tick loop, <c>DefaultTickSpeedMs = 500</c>; e.g.
    /// <c>VolumePump.OnAtmosphericTick</c> -> <c>FlowInDirection</c>, a fixed flow per tick):
    ///   T1 MATCH        device work is driven by the simulation tick, not by render frames.
    ///   T2 MATCH        a fixed amount of work per tick (rate x interval), no dt scaling inside it.
    ///   T3 STRUCTURAL   periods of 1 s and 0.2 s (unchanged) against Stationeers' 0.5 s: ONI's
    ///                   scheduler has 33 ms / 200 ms / 1000 ms / 4000 ms buckets and no 500 ms one.
    ///   T4 PREFERENCE   this accumulator keeps the overshoot (<c>elapsed -= interval</c>) and catches up
    ///                   at most <see cref="MaxCatchUp"/> intervals per call; Stationeers' tick cannot
    ///                   skip, so it has no equivalent. A deliberate choice. With a sim
    ///                   bucket the dt is already ~one interval, so it matters only at the edges (an
    ///                   updater's short first call; bucket jitter).
    ///   T5 MATCH        nothing runs while paused (the buckets do not advance); unverified for every
    ///                   Stationeers device.
    ///   T6 MATCH        a disabled or stood-down device does no work and banks no time.
    ///                   The plain-MonoBehaviour sites leave their bucket in OnDisable; the two
    ///                   KMonoBehaviour tanks stay registered (KMonoBehaviour.Spawn adds, only OnDestroy
    ///                   removes, and UpdateBucketWithUpdater.Update never checks <c>enabled</c>), so they
    ///                   return on <c>!isActiveAndEnabled</c> before <see cref="Advance"/>. Restores the older
    ///                   Update() semantics, which a device's stand-down relies on. Stationeers gates device work on OnOff/Powered inside OnAtmosphericTick:
    ///                   a match in intent, unverified per device.
    ///
    /// Vanilla's own shape for the same job: <c>Pump : ISim1000ms</c> (elapsedTime += dt),
    /// <c>ElementConverter</c> on <c>UpdateRate.SIM_1000ms</c>; Mod 1's own
    /// <c>VolumePumpComponent : ISim200ms</c> already ran this way.
    /// </summary>
    internal struct DeviceTick
    {
        /// <summary>The most whole intervals one call may run (T4).</summary>
        public const int MaxCatchUp = 4;

        /// <summary>
        /// Whole intervals dropped by the catch-up cap, all devices, since the game started. Zero in any
        /// healthy run: a sim bucket hands over about one interval per call. Nonzero means a bucket
        /// delivered more than <see cref="MaxCatchUp"/> intervals at once, which is worth knowing.
        /// </summary>
        public static long DroppedIntervals;

        private float elapsed;

        /// <summary>
        /// Adds <paramref name="simDt"/> seconds of SIM time and returns how many whole intervals are
        /// now due (at most <see cref="MaxCatchUp"/>). The remainder carries to the next call; that
        /// is the overshoot the old <c>timer = 0f</c> threw away.
        /// </summary>
        public int Advance(float simDt, float interval)
        {
            if (!(simDt > 0f) || !(interval > 0f))
            {
                return 0;
            }
            elapsed += simDt;
            int due = 0;
            while (elapsed >= interval && due < MaxCatchUp)
            {
                elapsed -= interval;
                due++;
            }
            if (elapsed >= interval)
            {
                // Past the cap: drop the whole intervals, keep the fraction.
                long dropped = (long)(elapsed / interval);
                DroppedIntervals += dropped;
                elapsed -= dropped * interval;
            }
            return due;
        }
    }
}
