using HarmonyLib;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// FOUND LIVE (user caught it): after
    /// <see cref="GasMixtureFacade.EqualizePipeNetwork"/> shipped (forcing a whole connected pipe
    /// run to one shared, correct pressure -- see that method's own doc comment for the real
    /// Stationeers-vs-vanilla background), pipe pressure got stuck at exactly HALF the tank's
    /// instead of converging, and the vanilla gas-flow visual kept animating continuously through
    /// the pipe. Root cause, : real vanilla `ConduitFlow.Sim200ms` runs its OWN
    /// per-tile flow mover (`UpdateConduit`) on almost exactly the same ~1-second cadence this
    /// mod's own tank/pump polling uses -- moving mass in a fixed, already-established flow
    /// direction between adjacent conduit tiles, completely independently of anything this mod
    /// does. Two separate ~1 Hz movers were fighting over the same pipe: this mod pushes the whole
    /// network to one even, correct state, and vanilla's own mover immediately shoves mass along
    /// in its own established direction again before the next check -- a stable-looking "stuck at
    /// half" is exactly what two competing periodic movers settle into, not a bug in either
    /// formula on its own.
    ///
    /// Fixed by disabling vanilla's own per-tile mover ONLY on pipe tiles this mod actually
    /// manages (<see cref="GasMixtureFacade.ManagedConduitCells"/>, populated by
    /// <c>EqualizePipeNetwork</c> itself) -- every other pipe in the game, anywhere else on the
    /// map, is completely untouched.
    ///
    /// LIQUID TOO, for the liquid tank/pipe mechanic: checks
    /// <see cref="GasMixtureFacade.ManagedLiquidConduitCells"/> too, gated on which real
    /// <c>ConduitFlow</c> instance actually fired (<c>Game.Instance.gasConduitFlow</c> vs.
    /// <c>Game.Instance.liquidConduitFlow</c>) -- a SEPARATE set, not a shared one keyed on cell
    /// alone, because a single cell can legitimately host both a gas conduit and a liquid
    /// conduit at once (two different <c>ObjectLayer</c>s over the same tile); checking the
    /// wrong set for the wrong instance would either miss cells that need gating or wrongly gate
    /// an unmanaged conduit of the other type sharing that tile.
    ///
    /// A Prefix on the private <c>ConduitFlow.UpdateConduit</c>
    /// (signature/behavior: returning <c>false</c> for a conduit with nothing
    /// to move is the SAME value the real method itself returns for its own "no movable mass"
    /// branch, so skipping is behaviorally consistent with a legitimate real state, not a made-up
    /// sentinel) -- <c>__instance.soaInfo</c>/<c>Conduit.idx</c> are both real public members,
    /// , no reflection needed.
    ///
    /// Wrapped in try/catch: this runs on every real gas conduit tile in the game, roughly once a
    /// second. A mistake here must degrade to "vanilla's own flow runs, same as always," never to
    /// a broken pipe network game-wide.
    /// </summary>
    [HarmonyPatch(typeof(ConduitFlow), "UpdateConduit")]
    internal static class ConduitFlow_UpdateConduit
    {
        private static bool Prefix(ConduitFlow __instance, ConduitFlow.Conduit conduit, ref bool __result)
        {
            try
            {
                int cell = __instance.soaInfo.GetCell(conduit.idx);
                bool managed;
                if (ReferenceEquals(__instance, Game.Instance.gasConduitFlow))
                {
                    managed = GasMixtureFacade.ManagedConduitCells.Contains(cell);
                }
                else if (ReferenceEquals(__instance, Game.Instance.liquidConduitFlow))
                {
                    managed = GasMixtureFacade.ManagedLiquidConduitCells.Contains(cell);
                }
                else
                {
                    // A third conduit type (solid? -- not part of this mod's scope) fires this
                    // same private method too, unverified for that case. Degrade to vanilla,
                    // never guess.
                    managed = false;
                }

                if (managed)
                {
                    // Same value UpdateConduit's own "nothing movable" branch returns --
                    // consistent with a real state, not an invented sentinel.
                    __result = false;
                    return false;
                }
            }
            catch
            {
                // See class doc comment -- degrade to vanilla's own real behavior, never throw
                // mid-flow-update for the whole game.
            }

            return true;
        }
    }

    /// <summary>
    /// Raises ONI's per-tile gas-conduit mass cap so that pipe OVERPRESSURE is reachable by
    /// pumping gas in, the way it is in Stationeers, instead of only by superheating what is
    /// already in the pipe.
    ///
    /// THE FRAMING THAT MATTERS: this is NOT a
    /// formula difference. Stationeers uses <c>P = nRT/V</c> exactly as
    /// <see cref="PipeNetworkFacade"/> does, and in both games mass and temperature both drive
    /// pressure. The real difference is that Stationeers has no per-tile mass cap at all -- its
    /// pumps push until the network bursts -- whereas ONI hard-caps a conduit:
    ///
    /// <code>
    /// Game.OnPrefabInit()
    ///   gasConduitFlow    = new ConduitFlow(ConduitType.Gas,    Grid.CellCount, gasConduitSystem,     1f, 0.25f);
    ///   liquidConduitFlow = new ConduitFlow(ConduitType.Liquid, Grid.CellCount, liquidConduitSystem, 10f, 0.75f);
    /// </code>
    ///
    /// That 4th argument is <c>max_conduit_mass</c>, stored in the private field
    /// <c>ConduitFlow.MaxMass</c>. So the fix is raising ONI's cap, not changing any math.
    ///
    /// WHY 8 kg/tile, derived rather than picked. Stationeers rates a gas pipe at
    /// <see cref="PipeNetworkFacade.MaxGasPipePressurePa"/> = 60794998.17 Pa, and this project
    /// models a conduit tile at Stationeers' real 10 L
    /// (<c>GasMixtureFacade.GasConduitVolumeM3</c> = 0.01 m^3). At room temperature:
    ///
    ///   n    = PV/RT = 60794998.17 * 0.01 / (8.3144 * 293.15) = 249.43 mol
    ///   mass = 249.43 mol * 31.9988 g/mol (O2, MOLECULAR -- see the molecular-mass fix below)
    ///        = 7.98 kg
    ///
    /// So 8 kg/tile puts a completely full room-temperature oxygen pipe almost exactly AT the
    /// burst rating, and <see cref="PipeNetworkFacade.StressedRatio"/> (0.8) at 6.4 kg. The whole
    /// stress band this mod already warns about becomes reachable by pumping. The old 1 kg cap
    /// reached only ~7.6 MPa -- an eighth of the rating -- which is why every upper warning tier
    /// was, in practice, unreachable without an absurd 2500 K.
    ///
    /// (That "~7.6 MPa, not ~15" is post-molecular-mass-fix. Klei's element table stores ATOMIC
    /// mass for the diatomic gases, so every diatomic pressure this project reported used to be
    /// exactly 2x high; see <c>ElementTable::MolecularMassOf</c> and
    /// <c>MaterialPropertyRegistry.PushMolecularMassesToSim</c>. The sizing above uses the
    /// corrected number.)
    ///
    /// WHY A CONSTRUCTOR PREFIX rather than setting the private field in a Postfix on
    /// <c>Game.OnPrefabInit</c> -- all four points against the installed
    /// build, not assumed:
    ///   - <c>ConduitFlow.MaxMass</c> is assigned in exactly one place, this constructor, and the
    ///     constructor's own <c>Initialize(num_cells)</c> never reads it. Rewriting the incoming
    ///     <c>ref</c> argument is therefore the complete change, with no reflection and nothing
    ///     that breaks silently if Klei renames the private field.
    ///   - <c>conduit_type</c> is a real parameter here, so gating on gas is exact. A field
    ///     Postfix has to work out which of Game's two ConduitFlow instances it is holding.
    ///   - <c>ConduitFlow.OnDeserialized</c> clamps loaded contents with
    ///     <c>Math.Min(MaxMass, serializedContents.mass)</c>, and KSerialization deserializes into
    ///     the instance this constructor built (there is no parameterless constructor). Setting
    ///     the cap here is unconditionally before any load can read it; a Postfix elsewhere only
    ///     happens to be early enough today, which is an ordering assumption nobody would notice
    ///     breaking.
    ///   - <c>Game.OnPrefabInit</c> is a large, heavily mod-patched method and <c>1f</c> is a
    ///     common constant inside it, so a transpiler rewriting the argument at the call site is
    ///     fragile for no gain.
    ///
    /// WHY GLOBAL rather than only pipes this mod manages. <c>MaxMass</c> is one field on the
    /// single world-wide <c>Game.Instance.gasConduitFlow</c> -- it is per conduit TYPE, not per
    /// pipe. A per-pipe cap is not expressible through it at all; it would mean per-cell
    /// overrides in <c>AddElement</c>, <c>IsConduitFull</c>, <c>GetEffectiveCapacity</c> and the
    /// flow mover, four patches on hot paths, for a capacity that then depends on invisible
    /// state. The real choice is global or nothing.
    ///
    /// KNOWN CONSEQUENCES, each , none of them accidental:
    ///   - throughput rises as well as storage: <c>GetEffectiveCapacity(MaxMass)</c> and the flow
    ///     mover's <c>Mathf.Min(massDesired, MaxMass - to.mass)</c> both scale with the cap. In
    ///     practice most networks stay pump-limited (a Gas Pump emits 500 g/s), so this is much
    ///     less than an 8x throughput change outside high-pump-count builds.
    ///   - <c>IsConduitFull</c> and the visualizer's <c>MaxMass * 0.1f</c> ball threshold both
    ///     move with the cap, which is the intent: "full" should mean the new full.
    ///   - SAVE COMPATIBILITY: a save written with the raised cap and loaded WITHOUT this mod
    ///     loses the excess at <c>OnDeserialized</c>'s <c>Math.Min</c>. Accepted deliberately --
    ///     Mod 1 already calls <c>SIM_</c> exports that vanilla's SimDLL does not have, so these
    ///     saves are not vanilla-loadable regardless. This adds no new failure mode.
    /// </summary>
    [HarmonyPatch(typeof(ConduitFlow), MethodType.Constructor, new System.Type[]
    {
        typeof(ConduitType), typeof(int), typeof(IUtilityNetworkMgr), typeof(float), typeof(float)
    })]
    internal static class ConduitFlow_Constructor_MaxMass
    {
        /// <summary>
        /// Per-tile gas-conduit mass cap, in kg. See the class doc for the derivation; 8 kg puts
        /// a full room-temperature oxygen tile at Stationeers' own burst rating.
        /// </summary>
        public const float GasMaxMassKg = 8f;

        /// <summary>
        /// Vanilla's own value, kept so the log line below can say what actually changed rather
        /// than only what it changed to.
        /// </summary>
        private const float VanillaGasMaxMassKg = 1f;

        private static void Prefix(ConduitType conduit_type, ref float max_conduit_mass)
        {
            // Liquid conduits are left at vanilla's 10 kg: this mod's liquid path prices a line
            // by fill fraction rather than pressure (PipeNetworkFacade's deviation 2), so raising
            // the liquid cap would change vanilla balance without making any warning reachable.
            if (conduit_type != ConduitType.Gas)
            {
                return;
            }

            Debug.Log("[Mod1ThermoFluid] gas conduit per-tile mass cap "
                + $"{max_conduit_mass:F2} kg -> {GasMaxMassKg:F2} kg "
                + (max_conduit_mass == VanillaGasMaxMassKg
                    ? "(vanilla value seen, as expected)"
                    : "(NOTE: incoming value was not vanilla's 1 kg -- another mod patches this too)"));

            max_conduit_mass = GasMaxMassKg;
        }
    }
}
