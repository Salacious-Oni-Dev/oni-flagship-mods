using HarmonyLib;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// Restores the ABSOLUTE mass floor that vanilla's freeze/boil trigger used to have, which
    /// <see cref="ConduitFlow_Constructor_MaxMass"/> moved by accident.
    ///
    /// `ConduitFlow.FreezeConduitContents` gates the whole thermal-dump path on
    /// `contents.mass > MaxMass * 0.1f` -- a RELATIVE floor. With vanilla's 1 kg gas cap that is
    /// 0.1 kg/tile. Raising the cap to 8 kg silently raised the floor to 0.8 kg/tile, which made
    /// every lightly-loaded gas pipe in the game immune to freeze and boil damage. Measured, not
    /// reasoned: FreezePathProbe reported `gate = 0.800 kg/tile ... over-gate=0/12;
    /// frozenEvents=0` on a 0.2 kg/tile run sitting at 77.5 K, well below Oxygen's 87.2 K dump
    /// threshold -- vanilla would have fired at the old floor and did not fire at the new one.
    ///
    /// Fixed by re-implementing the gate against the absolute 0.1 kg vanilla used, rather than by
    /// lowering the cap (the cap is doing its own job -- see its class doc) and rather than by
    /// leaving the two coupled. The Prefix reproduces the real method exactly apart from that one
    /// number: same null check, same `GetContents`, same `Trigger` hash, and returns false so the
    /// original does not then run its own weaker test.
    ///
    /// GAS ONLY. Liquid conduits keep vanilla's 10 kg cap, so their gate is still vanilla's own
    /// 1.0 kg and there is nothing to restore.
    /// </summary>
    [HarmonyPatch(typeof(ConduitFlow), "FreezeConduitContents")]
    internal static class ConduitFlow_FreezeConduitContents_Floor
    {
        /// <summary>
        /// Vanilla's effective floor: its own gas `MaxMass` (1 kg) times the `0.1f` in
        /// `FreezeConduitContents`. Written as the product it is, not as a bare 0.1, so the
        /// provenance survives.
        /// </summary>
        internal const float VanillaGasTriggerFloorKg = 1f * 0.1f;

        /// <summary>GameHashes.ConduitContentsFrozen, .</summary>
        internal const int ConduitContentsFrozen = -700727624;

        private static bool Prefix(ConduitFlow __instance, int conduit_idx)
        {
            if (!ReferenceEquals(__instance, Game.Instance.gasConduitFlow))
            {
                return true;
            }

            GameObject conduitGO = __instance.soaInfo.GetConduitGO(conduit_idx);
            if (conduitGO != null &&
                __instance.soaInfo.GetConduit(conduit_idx).GetContents(__instance).mass
                    > VanillaGasTriggerFloorKg)
            {
                conduitGO.Trigger(ConduitContentsFrozen);
            }

            return false;
        }
    }

    /// <summary>
    /// The boiling mirror of <see cref="ConduitFlow_FreezeConduitContents_Floor"/> -- same
    /// relative-gate problem, same fix, different event hash. Patched together with the freeze
    /// side because leaving one of a symmetric pair unfixed is how a subtle asymmetry ships.
    /// </summary>
    [HarmonyPatch(typeof(ConduitFlow), "MeltConduitContents")]
    internal static class ConduitFlow_MeltConduitContents_Floor
    {
        /// <summary>GameHashes.ConduitContentsBoiling, .</summary>
        internal const int ConduitContentsBoiling = -1152799878;

        private static bool Prefix(ConduitFlow __instance, int conduit_idx)
        {
            if (!ReferenceEquals(__instance, Game.Instance.gasConduitFlow))
            {
                // Vanilla's boiling handler EMPTIES the conduit into the world cell. That is the
                // right vanilla behaviour and this patch does not argue with it -- but a liquid
                // line in a phase-change loop is boiling ON PURPOSE, all the time, as the
                // mechanism by which it absorbs heat, and its condensate arrives at whatever
                // temperature the gas line was running at (often far above Water's flat
                // 372.75 K). Every delivery would trip vanilla's flat threshold and be dumped into
                // the room before this mod's own pressure-driven path could convert it.
                //
                // On a pipe this mod MANAGES, this mod owns the phase change:
                // PipeMatterFacade.TickNetworkPhaseChange boils the contents into standing gas
                // that a Purge Valve can remove, conserving the mass and billing the latent heat.
                // Vanilla's event is therefore suppressed there and only there. Every other liquid
                // pipe in the colony keeps vanilla's behaviour exactly, including the dump.

                if (ReferenceEquals(__instance, Game.Instance.liquidConduitFlow))
                {
                    int managedCell = __instance.soaInfo.GetCell(conduit_idx);
                    if (GasMixtureFacade.ManagedLiquidConduitCells.Contains(managedCell))
                    {
                        return false;
                    }
                }
                return true;
            }

            GameObject conduitGO = __instance.soaInfo.GetConduitGO(conduit_idx);
            if (conduitGO != null &&
                __instance.soaInfo.GetConduit(conduit_idx).GetContents(__instance).mass
                    > ConduitFlow_FreezeConduitContents_Floor.VanillaGasTriggerFloorKg)
            {
                conduitGO.Trigger(ConduitContentsBoiling);
            }

            return false;
        }
    }

    /// <summary>
    /// Replaces vanilla's "a cold gas pipe is deleted" with a real, gradual, latent-heat-paying
    /// condensation: a phase change that costs and releases energy, not a state toggle.
    ///
    /// WHAT VANILLA DOES. `Conduit.OnConduitFrozen`
    /// is the ONLY subscriber to `GameHashes.ConduitContentsFrozen` in the whole assembly, and
    /// `ConduitFlow.FreezeConduitContents` is that event's ONLY producer, so this one method is
    /// the entire thermal-dump path -- nothing else in the game can empty a pipe for temperature
    /// reasons. It does exactly two things:
    ///   1. `BuildingHP` damage 1, source `CONDUIT_CONTENTS_FROZE` -- and it fires every 200 ms
    ///      tick the contents stay cold, which is why holding a pipe at 60 K destroys it outright
    ///      rather than merely emptying it;
    ///   2. `ConduitFlow.EmptyConduit(cell)` -> `DumpPipeContents` ->
    ///      `SimMessages.AddRemoveSubstance(cell, contents.element, ...)` -- the gas is spat into
    ///      the world STILL AS GAS, at its old temperature, with no latent heat accounted for
    ///      anywhere. A pipe of oxygen chilled past its condensation point becomes a puff of cold
    ///      oxygen GAS in the room. That is the "magic" this project exists to remove.
    ///
    /// WHAT THIS DOES INSTEAD. Per freeze tick, for the one tile that fired:
    ///   - price the network's real pressure through the public API
    ///     (<see cref="PipeNetworkFacade.TryReadNetwork"/>) and take the CLAMPED Antoine
    ///     condensation point at that pressure
    ///     (<see cref="GasMixtureFacade.TryGetEvaporationTemperatureClampedK"/> -- always the
    ///     clamped form, per that method's own doc comment);
    ///   - convert one step's worth of gas to its low-temperature transition element through the
    ///     native kernel (<see cref="GasMixtureFacade.ComputePhaseChangeStep"/>), which pays the
    ///     latent heat out of the REMAINING gas -- so condensing warms what is left and the
    ///     process self-limits instead of flash-emptying the pipe;
    ///   - put the condensed LIQUID into the world at the boundary temperature, and leave the
    ///     un-condensed gas in the pipe at its new, warmer temperature.
    ///
    /// Net effect for the player: a cold gas line weeps liquid instead of vanishing, and stops on
    /// its own once latent heat has warmed the remainder back to the boundary. The condensation
    /// warning this mod already shows (PipeStressMonitor) now describes something the player can
    /// watch happen.
    ///
    /// NO DAMAGE when we handle it. Vanilla's 1 HP/tick is compensation for deleting the
    /// contents; under this model condensation is ordinary physics, not pipe failure. Stationeers
    /// agrees -- it damages a pipe for PRESSURE, not for holding a condensed phase. Overpressure
    /// damage is a separate concern this mod already reports on.
    ///
    /// FALLS THROUGH TO VANILLA whenever it cannot do the job honestly: no network, no
    /// low-temperature transition element, no registered latent heat, contents not actually below
    /// the boundary, or nothing converted this step. In every one of those cases the original
    /// method runs unchanged. "Fall back to doing nothing of our own" is the same contract the
    /// rest of this mod's facade calls use, and it means a gap in the material registry degrades
    /// to vanilla behavior rather than to a pipe that never empties.
    ///
    /// GAS ONLY, deliberately: a freezing LIQUID conduit is the solid transition, a different
    /// boundary with different (largely pressure-independent) physics, and Mod 1 has no solid
    /// handling yet. Those still get vanilla.
    /// </summary>
    [HarmonyPatch(typeof(Conduit), "OnConduitFrozen")]
    internal static class Conduit_OnConduitFrozen_Condense
    {
        private const string LogPrefix = "[Mod1ThermoFluid] CONDENSE: ";

        /// <summary>
        /// The tick this event fires on. `ConduitTemperatureManager.Sim200ms` is the only caller
        /// of the path that raises it, so one event is 200 ms of simulated time.
        /// </summary>
        private const float FreezeTickSeconds = 0.2f;

        /// <summary>
        /// Stationeers' own 10%-per-tick conversion shape, the same constant and the same
        /// reasoning as <c>OniFramework.PhaseVessel.PhaseChangeRatePerSecond</c> -- a gradual conversion rather
        /// than an instant flash. Duplicated rather than shared because the two are independently
        /// tunable and sharing them would couple a tank's rate to a pipe's.
        /// </summary>
        private const float PhaseChangeRatePerSecond = 0.1f;

        /// <summary>
        /// "Don't leave physically meaningless dust behind" floor, same role and value as
        /// <c>OniFramework.PhaseVessel.PhaseChangeMinRemainderKg</c>.
        /// </summary>
        private const float PhaseChangeMinRemainderKg = 0.01f;

        /// <summary>
        /// `Element.specificHeatCapacity` is in Klei's raw content-data units, J/(g*K), while the
        /// native phase-change kernel wants J/(kg*K), a 1000x error if confused -- see
        /// Compressor.cs's own comment on this constant. Never pass the raw field.
        /// </summary>
        private const float GramsPerKilogram = 1000f;

        /// <summary>
        /// Stationeers' two phase hazards, evaluated against what the network is ACTUALLY
        /// HOLDING and applied the way Stationeers applies them.
        ///
        /// Intercepting vanilla's handler must not drop the damage with it: Stationeers damages a
        /// pipe for holding a condensed phase as well as for pressure
        /// (`AtmosphericsNetwork.EvaluateIncorrectMatterState`). Both branches exist:
        ///
        ///   - SOLID, every network type: frozen matter above `MinFrozenMolesToDamage()`
        ///     (`0.05 * Volume` litres, as moles) damages by
        ///     `clamp(log2(frozenMoles / minMoles), 0.5, 10)`.
        ///   - LIQUID, gas networks only: `TotalVolumeLiquids / Volume` above `0.02` damages by
        ///     `clamp(log10(ratio * 100) - 0.8, 0.2, 10)`. Liquid and All networks are explicit
        ///     no-ops in Stationeers -- a liquid line is supposed to be full of liquid -- and are
        ///     no-ops here.
        ///
        /// Stationeers takes the WORSE of the two rather than stacking them, and so does this:
        /// they describe one pipe in one bad state, and a pipe full of ice should not be billed
        /// twice for also being full of liquid.
        ///
        /// Damage lands on ONE member of the network, the most damaged, exactly as Stationeers'
        /// `_weakestMember` / `ApplyPressureDamageToWeakest` does, rather than on every tile that
        /// happens to be cold this tick -- a whole run losing HP in lockstep is neither
        /// Stationeers' behaviour nor good gameplay. It is dealt through vanilla's own damage
        /// event with vanilla's own `CONDUIT_CONTENTS_FROZE` source, so every existing ONI
        /// notification, pop-up, FX and break path keeps working untouched.
        /// </summary>
        /// <summary>
        /// The LIQUID-conduit half of the same hazard: frozen contents damage a liquid pipe too,
        /// and can burst it quickly.
        ///
        /// Stationeers' SOLID rule is NOT gas-only -- only its liquid-in-a-gas-pipe rule is -- so
        /// a liquid line whose contents freeze is damaged by exactly the same
        /// `clamp(log2(frozenMoles / minMoles), 0.5, 10)` path. What vanilla ONI does instead is
        /// delete the contents and deal 1 HP, which is both less punishing and less interesting
        /// than a line that ices up and splits.
        ///
        /// So: freeze the contents into their own solid element, hold that as standing matter in
        /// the pipe (`PipeMatterState`), and let the accumulated frozen moles drive the damage.
        /// Ice is not returned to the world until the pipe ruptures, which is the "frozen
        /// material can produce ice at the rupture" behaviour.
        ///
        /// "RAPIDLY BURST" falls out of the numbers rather than being special-cased: a liquid
        /// conduit holds up to 10 kg per tile against a gas conduit's 8 kg cap of a far lighter
        /// substance, so the frozen mole count clears `MinFrozenMolesToDamage` immediately and by
        /// a wide margin, and the log2 curve is already the harsher of the two.
        ///
        /// Falls through to vanilla whenever it cannot do the job honestly -- no freezing point
        /// registered for the contents, or no solid transition element -- the same contract the
        /// gas path uses.
        /// </summary>
        private static bool FreezeLiquidConduit(Conduit conduit)
        {
            int cell = Grid.PosToCell(conduit.transform.GetPosition());
            ConduitFlow flow = Game.Instance.liquidConduitFlow;
            ConduitFlow.ConduitContents contents = flow.GetContents(cell);
            if (contents.mass <= 0f || contents.element == SimHashes.Vacuum)
            {
                return true;
            }

            Element element = ElementLoader.FindElementByHash(contents.element);
            if (element == null || !element.IsLiquid)
            {
                return true;
            }

            Element solid = element.lowTempTransition;
            if (solid == null || !solid.IsSolid)
            {
                return true;
            }

            if (!MaterialPropertyRegistry.TryGetFreezingTemperatureK(element.id,
                    out float freezingK) || contents.temperature >= freezingK)
            {
                // Vanilla thought this was frozen -- it tests the element's flat lowTemp -- but
                // the registry's own freezing point disagrees. Let vanilla have it rather than
                // inventing a third answer, same as the gas path does.
                return true;
            }

            if (!MaterialPropertyRegistry.TryGetLatentHeatOfFusionJPerKg(element.id,
                    out float latentHeatJPerKg) || latentHeatJPerKg <= 0f)
            {
                return true;
            }

            GasMixtureFacade.ComputePhaseChangeStep(contents.mass, contents.temperature,
                freezingK, latentHeatJPerKg,
                element.specificHeatCapacity * GramsPerKilogram, FreezeTickSeconds,
                PhaseChangeRatePerSecond, PhaseChangeMinRemainderKg,
                out float frozenMassKg, out float remainingTemperatureK);

            if (frozenMassKg <= 0f)
            {
                return true;
            }

            // Same latent-heat accounting as the gas path, for the same reason and with the same
            // history: the kernel bills the released heat to the ONE TILE's remainder, and a tile
            // that freezes solid has no remainder to bill, so the heat used to land on the ice
            // itself. Fusion heat is a fifth of vaporization heat here
            // (FUSION_TO_VAPORIZATION_LATENT_HEAT_DENOMINATOR = 5.0, Stationeers' own ratio), so
            // the distortion is smaller than the gas path's 24.75 K, but it is the same
            // distortion and it gets the same fix -- the ice leaves at the liquid's own
            // temperature and the released heat is spread across the network's shared body. See
            // BillLatentHeatToNetwork.
            float remainingMassKg = contents.mass - frozenMassKg;
            float iceTemperatureK = contents.temperature;

            int frozenDisease = 0;
            if (contents.diseaseCount > 0 && contents.mass > 0f)
            {
                frozenDisease = Mathf.Clamp(
                    Mathf.RoundToInt(contents.diseaseCount * (frozenMassKg / contents.mass)),
                    0, contents.diseaseCount);
            }

            PipeMatterState.Add(gasConduit: false, cell, ElementLoader.elements.IndexOf(solid),
                frozenMassKg, iceTemperatureK);

            if (remainingMassKg > 0f)
            {
                flow.SetContents(cell, new ConduitFlow.ConduitContents(contents.element,
                    remainingMassKg, contents.temperature, contents.diseaseIdx,
                    contents.diseaseCount - frozenDisease));
            }
            else
            {
                flow.SetContents(cell, ConduitFlow.ConduitContents.Empty);
            }

            bool haveNetwork = PipeNetworkFacade.TryGetNetworkCells(cell, PipeContentType.Liquid,
                out int[] networkCells);

            float latentHeatReleasedJ = frozenMassKg * latentHeatJPerKg;
            int[] billCells = haveNetwork && networkCells.Length > 0
                ? networkCells
                : new int[] { cell };
            if (!PipeMatterFacade.BillLatentHeatToCell(cell, gasConduit: false, flow,
                    latentHeatReleasedJ))
            {
                float iceHeatCapacity = frozenMassKg * solid.specificHeatCapacity * GramsPerKilogram;
                if (iceHeatCapacity > 0f)
                {
                    PipeMatterState.AddTemperatureK(gasConduit: false, cell,
                        latentHeatReleasedJ / iceHeatCapacity);
                }
            }

            if (haveNetwork)
            {
                EvaluateMatterStateDamage(networkCells, gasConduit: false);
            }

            Debug.Log(LogPrefix + $"cell {cell}: froze {frozenMassKg:F4} kg {element.tag} -> "
                + $"{solid.tag} at {iceTemperatureK:F1}K (pipe was {contents.mass:F3} kg at "
                + $"{contents.temperature:F1}K, remainder {remainingMassKg:F3} kg, "
                + $"{latentHeatReleasedJ / 1000f:F2} kJ latent heat billed across "
                + $"{billCells.Length} network tile(s))");

            return false;
        }

        /// <summary>
        /// Spreads latent heat across a whole conduit network in proportion to heat capacity.
        ///
        /// IN THE FRAMEWORK
        /// (<see cref="PipeMatterFacade.BillLatentHeatToNetwork(int[], bool, ConduitFlow, float)"/>),
        /// which now carries the full derivation: why the energy goes to the network's shared body
        /// rather than to the condensate or the conduit building, why it is applied as a uniform
        /// delta rather than a uniform temperature, and the sign convention (positive released,
        /// negative absorbed). It moved because the Condensation-Valve/Purge-Valve port needs the
        /// same billing from the framework's own pressure-driven phase change, and two copies of
        /// an energy-accounting rule is exactly the thing that drifts.
        ///
        /// These two overloads stay as forwarders: every call site in this file is verified
        /// behaviour, and relocating them buys nothing.
        /// </summary>
        internal static bool BillLatentHeatToNetwork(int[] cells, bool gasConduit, ConduitFlow flow,
            float joules)
        {
            return PipeMatterFacade.BillLatentHeatToNetwork(cells, gasConduit, flow, joules);
        }

        /// <summary>
        /// <see cref="BillLatentHeatToNetwork(int[], bool, ConduitFlow, float)"/> for a caller
        /// that holds a cell rather than a resolved network. Forwards to the framework.
        /// </summary>
        internal static bool BillLatentHeatToNetwork(int cell, bool gasConduit, ConduitFlow flow,
            float joules)
        {
            return PipeMatterFacade.BillLatentHeatToNetwork(cell, gasConduit, flow, joules);
        }


        internal static void EvaluateMatterStateDamage(int[] cells, bool gasConduit)
        {
            if (cells == null || cells.Length == 0)
            {
                return;
            }

            float volumeLitres = cells.Length
                * (gasConduit
                    ? GasMixtureFacade.GasConduitVolumeM3
                    : GasMixtureFacade.LiquidConduitVolumeM3)
                * LitresPerCubicMetre;
            if (volumeLitres <= 0f)
            {
                return;
            }

            float damage = PipeNetworkFacade.FrozenContentsDamage(
                PipeMatterState.TrappedFrozenMoles(gasConduit, cells), volumeLitres);

            if (gasConduit)
            {
                float liquidRatio =
                    PipeMatterState.TrappedLiquidLitres(gasConduit, cells) / volumeLitres;
                damage = Mathf.Max(damage, PipeNetworkFacade.LiquidInGasPipeDamage(liquidRatio));
            }

            if (damage <= 0f)
            {
                return;
            }

            DamageWeakestMember(cells, gasConduit, damage,
                STRINGS.BUILDINGS.DAMAGESOURCES.CONDUIT_CONTENTS_FROZE,
                STRINGS.UI.GAMEOBJECTEFFECTS.DAMAGE_POPS.CONDUIT_CONTENTS_FROZE,
                gasConduit ? SpawnFXHashes.BuildingLeakLiquid : SpawnFXHashes.BuildingFreeze,
                gasConduit ? "water_damage_kanim" : "ice_damage_kanim");
        }

        /// <summary>
        /// Stationeers' <c>_weakestMember</c> pick and the damage application that follows it,
        /// shared by every pipe hazard: the two matter-state branches
        /// (<see cref="EvaluateMatterStateDamage"/>) and the pressure branch
        /// (<see cref="PipeStressMonitor"/>). Returns the CELL of the member it damaged if that
        /// member is now broken, or -1 otherwise, so a caller that needs to relieve the network
        /// can vent it through the actual hole in the same tick it made it. The cell rather than
        /// a bool because "expel out of the broken pipe" is only true if the expulsion point is
        /// the broken tile -- the worst-pressure tile and the weakest tile are generally not the
        /// same one.
        ///
        /// WEAKEST BY HEALTH RATIO, not by raw hit points. Stationeers scans for the most-damaged
        /// eligible member by <c>DamageState.TotalRatioClamped</c>, falling back to the first
        /// member over the limit. Raw <c>HitPoints</c> gives the same answer for a run of one
        /// conduit type -- every gas conduit has <c>Def.HitPoints</c> = 10 -- but stops agreeing
        /// the moment a run mixes types with different maxima, and the ratio is what Stationeers
        /// actually means, and the pressure branch uses the same rule.
        ///
        /// A RUN WITH A HOLE IN IT TAKES NO FURTHER DAMAGE, from any hazard. In Stationeers it is
        /// rare for more than one segment to be destroyed by a burst, because the broken segment
        /// leaks the rest of the network out, or at least drops the pressure fast enough to
        /// prevent the next break. Once a segment is gone the network is open to the world, so it cannot hold the
        /// pressure or the standing condensate that were doing the damage, and the hazard stops
        /// before it can march down the pipe.
        ///
        /// <c>PipeMatterState.VentNetworkThroughRupture</c> already empties the run when it
        /// notices the break, but on the matter-state path it runs on the monitor's 1 Hz tick
        /// while freeze events arrive at 5 Hz -- so without this check a run kept cold by an
        /// upstream pump takes four more hits between the rupture and the vent, which is exactly
        /// the cascade that was observed (8 of 12 segments, then 12 of 12). Suppressing damage
        /// here is the continuous form of the same relief.
        ///
        /// Deliberately does NOT make the run invulnerable: the physics above still runs, the
        /// contents still condense or freeze, and damage already dealt stays dealt. Repair the
        /// segment and the run can break again -- "the rest of the network would drain the solid
        /// frozen liquid and would cool off, but potentially still get damaged."
        /// </summary>
        internal static int DamageWeakestMember(int[] cells, bool gasConduit, float damage,
            string source, string popString, SpawnFXHashes takeDamageEffect,
            string fullDamageEffectName)
        {
            if (cells == null || cells.Length == 0 || damage <= 0f)
            {
                return -1;
            }

            ObjectLayer conduitLayer = gasConduit
                ? ObjectLayer.GasConduit
                : ObjectLayer.LiquidConduit;

            BuildingHP weakest = null;
            int weakestCell = -1;
            float weakestRatio = float.MaxValue;
            foreach (int c in cells)
            {
                GameObject go = Grid.Objects[c, (int)conduitLayer];
                BuildingHP hp = go != null ? go.GetComponent<BuildingHP>() : null;
                if (go == null || (hp != null && hp.IsBroken))
                {
                    // The hole, still inside the run. Reachable while the network partition is
                    // dirty and the run has not been re-cut yet, and always reachable through the
                    // flood-fill fallback. Nothing on this run takes damage while it is open.
                    return -1;
                }
                if (hp == null)
                {
                    continue;
                }
                if (AdjoinsBrokenConduit(c, conduitLayer))
                {
                    // THE HOLE AT THE BOUNDARY, which is where it lives once `ConduitNetworks`
                    // is answering. ONI's own network rebuild drops a broken conduit from every
                    // network -- it is IDisconnectable and reports IsDisconnected, so
                    // RebuildNetworks skips it before stamping and never traverses through it --
                    // so a break does not put a broken member in the run, it CUTS the run in two
                    // and leaves the break just outside both halves.
                    //
                    // The loop above alone cannot implement "a run with a hole in it takes no
                    // further damage", because the partition changes hands at the break: every surviving fragment is a healthy run in its own right and
                    // takes the next hit, so a freezing pipe loses all its segments instead of one.
                    return -1;
                }

                // BuildingHP.MaxHitPoints is building.Def.HitPoints -- 10 for a gas conduit
                // (GasConduitConfig). Guarded anyway: a maximum of zero cannot be ranked by
                // ratio, so such a member ranks by absolute health instead.
                int maxHitPoints = hp.MaxHitPoints;
                float ratio = maxHitPoints > 0
                    ? (float)hp.HitPoints / maxHitPoints
                    : hp.HitPoints;
                if (ratio < weakestRatio)
                {
                    weakestRatio = ratio;
                    weakest = hp;
                    weakestCell = c;
                }
            }

            if (weakest == null)
            {
                return -1;
            }

            // ONI's BuildingHP is integer hit points, Stationeers' damage is a 0.2..10 float on
            // its own scale. Rounded, floored at 1: a hazard Stationeers considers worth damaging
            // should never round away to nothing.
            int hitPoints = Mathf.Max(1, Mathf.RoundToInt(damage));

            weakest.gameObject.BoxingTrigger(-794517298, new BuildingHP.DamageSourceInfo
            {
                damage = hitPoints,
                source = source,
                popString = popString,
                takeDamageEffect = takeDamageEffect,
                fullDamageEffectName = fullDamageEffectName
            });

            return weakest.IsBroken ? weakestCell : -1;
        }

        /// <summary>
        /// Whether a conduit tile has a BROKEN conduit of the same type immediately beside it.
        ///
        /// This is the boundary half of the hole test. It is deliberately adjacency and not
        /// network membership: the broken tile is by definition in no network, so no network
        /// query can find it, and the only thing that still knows it is there is the grid.
        ///
        /// Four lookups per member, and only on a tick that already decided there is damage to
        /// deal, so it costs nothing on a healthy colony.
        /// </summary>
        private static bool AdjoinsBrokenConduit(int cell, ObjectLayer conduitLayer)
        {
            return IsBrokenConduit(Grid.CellLeft(cell), conduitLayer)
                || IsBrokenConduit(Grid.CellRight(cell), conduitLayer)
                || IsBrokenConduit(Grid.CellAbove(cell), conduitLayer)
                || IsBrokenConduit(Grid.CellBelow(cell), conduitLayer);
        }

        private static bool IsBrokenConduit(int cell, ObjectLayer conduitLayer)
        {
            if (!Grid.IsValidCell(cell))
            {
                return false;
            }
            GameObject go = Grid.Objects[cell, (int)conduitLayer];
            if (go == null)
            {
                return false;
            }
            BuildingHP hp = go.GetComponent<BuildingHP>();
            return hp != null && hp.IsBroken;
        }

        private const float LitresPerCubicMetre = 1000f;

        /// <summary>
        /// Mass this interception DECLINED and handed back to vanilla, in kilograms, per conduit
        /// type. Vanilla's answer to a frozen conduit is to empty it into the world, so every
        /// gram counted here has left the pipe by a route this mod did not choose.
        ///
        /// Exists because "where did the charge go" is otherwise unanswerable. A rig can weigh
        /// what is standing in the pipe and it can scan the world nearby, but dumped GAS spreads
        /// along whatever corridor it lands in and walks straight out of any fixed search window
        /// -- PipeStressDemo phase 6 reported 2.203 kg of a 2.400 kg charge with 0.000 kg visible
        /// in the world, then 0.161 kg visible after the window was widened, on runs that were
        /// otherwise identical. Counting the mass at the moment it is handed over turns a chase
        /// into an identity: charge = standing + still in the pipe + handed to vanilla.
        ///
        /// The declines are deliberate, not failures. This mod refuses a tile when its own
        /// pressure-aware curve disagrees with vanilla's flat lowTemp, when the network has no
        /// resolvable pressure to put on that curve, or when the substance has no registered
        /// latent heat -- and in each case letting vanilla have it is better than inventing a
        /// third answer. What was missing was any way to see how often that happens and how much
        /// it costs.
        /// </summary>
        internal static float DeclinedToVanillaGasKg { get; private set; }

        internal static float DeclinedToVanillaLiquidKg { get; private set; }

        /// <summary>Records a decline. Called at every fall-through that still has mass on it.</summary>
        private static bool Decline(bool gasConduit, float massKg)
        {
            if (massKg > 0f)
            {
                if (gasConduit)
                {
                    DeclinedToVanillaGasKg += massKg;
                }
                else
                {
                    DeclinedToVanillaLiquidKg += massKg;
                }
            }
            return true;
        }

        private static bool Prefix(Conduit __instance)
        {
            try
            {
                if (__instance.ConduitType == ConduitType.Liquid)
                {
                    return FreezeLiquidConduit(__instance);
                }
                if (__instance.ConduitType != ConduitType.Gas)
                {
                    return true;
                }

                int cell = Grid.PosToCell(__instance.transform.GetPosition());
                ConduitFlow flow = Game.Instance.gasConduitFlow;
                ConduitFlow.ConduitContents contents = flow.GetContents(cell);
                if (contents.mass <= 0f || contents.element == SimHashes.Vacuum)
                {
                    return true;
                }

                Element element = ElementLoader.FindElementByHash(contents.element);
                if (element == null || !element.IsGas)
                {
                    return Decline(true, contents.mass);
                }

                // The element this gas becomes when it condenses. Klei stores it as an index into
                // the same table, so no name matching and no guessing at a "LiquidX" convention.
                Element liquid = element.lowTempTransition;
                if (liquid == null || !liquid.IsLiquid)
                {
                    return Decline(true, contents.mass);
                }

                // Pressure from the public API, so the boundary this uses is the SAME number the
                // player already sees in the pipe readout and in the condensation warning.
                if (!PipeNetworkFacade.TryReadNetwork(cell, PipeContentType.Gas,
                    out PipeNetworkReading state) || state.PressurePa <= 0f)
                {
                    return Decline(true, contents.mass);
                }

                if (!GasMixtureFacade.TryGetEvaporationTemperatureClampedK(contents.element,
                    state.PressurePa, out float boundaryK))
                {
                    return Decline(true, contents.mass);
                }

                if (contents.temperature >= boundaryK)
                {
                    // Vanilla thought this was frozen (it tests against the element's flat
                    // lowTemp) but our own pressure-aware curve says the gas is still gas. Let
                    // vanilla have it rather than inventing a third answer.
                    return Decline(true, contents.mass);
                }

                if (!GasMixtureFacade.TryGetLatentHeatJPerKg(contents.element,
                    out float latentHeatJPerKg) || latentHeatJPerKg <= 0f)
                {
                    return Decline(true, contents.mass);
                }

                GasMixtureFacade.ComputePhaseChangeStep(contents.mass, contents.temperature,
                    boundaryK, latentHeatJPerKg,
                    element.specificHeatCapacity * GramsPerKilogram, FreezeTickSeconds,
                    PhaseChangeRatePerSecond, PhaseChangeMinRemainderKg,
                    out float convertedMassKg, out float remainingTemperatureK);

                if (convertedMassKg <= 0f)
                {
                    return Decline(true, contents.mass);
                }

                // Germs ride the mass they are actually on. Vanilla hands the WHOLE disease
                // count to its dump because it empties the pipe completely; this only moves part
                // of the contents, so passing the full count to both halves would duplicate every
                // germ in the pipe on every freeze tick. Split by mass fraction, and give the
                // remainder whatever integer rounding left over so the total is exactly conserved.
                float remainingMassKg = contents.mass - convertedMassKg;
                int convertedDisease = 0;
                if (contents.diseaseCount > 0 && contents.mass > 0f)
                {
                    convertedDisease = Mathf.Clamp(
                        Mathf.RoundToInt(contents.diseaseCount * (convertedMassKg / contents.mass)),
                        0, contents.diseaseCount);
                }
                int remainingDisease = contents.diseaseCount - convertedDisease;

                // WHERE THE LATENT HEAT GOES -- the energy-conservation question, and this is
                // its THIRD answer. The first dumped the condensate at `boundaryK` while the
                // kernel had already paid the same heat into the remainder, inventing ~14 kJ per
                // tile per tick. The second billed a full conversion to the condensate itself,
                // which conserves but puts every joule into the smallest, coldest body present
                // and pinned Oxygen's condensate 24.75 K above the gas -- see
                // BillLatentHeatToNetwork for that derivation and for why Stationeers never has
                // this problem.
                //
                // The answer used now: the condensate and the remainder both leave at the gas's
                // own temperature, and the released heat is then spread across the whole
                // network's shared body -- exactly what Stationeers' state change bills against. The
                // kernel's `remainingTemperatureK` is therefore deliberately NOT used here; it
                // is the single-tile local billing this call site is replacing, and it is kept
                // only as the fallback below. Its clamp is dropped with it: clamping a release
                // at the boundary discards energy, and there is no longer any need to, because
                // the network's heat capacity is large enough that the rise is small.
                float condensateTemperatureK = contents.temperature;

                // Order matters: write the new state FIRST, then bill. The freshly condensed
                // liquid is part of the mixture that absorbs the heat, the same way Stationeers'
                // newly condensed moles are still inside the Atmosphere that pays for them.
                //
                // STAYS IN THE PIPE. Sending it straight into the world would make the hazard
                // memoryless -- a line weeping condensate for a minute would look identical to one
                // that had just started -- and nothing would accumulate the way Stationeers'
                // liquid volume in a network does. It accrues as
                // standing matter and only reaches the world when the pipe actually ruptures.
                // See PipeMatterState.
                PipeMatterState.Add(gasConduit: true, cell, ElementLoader.elements.IndexOf(liquid),
                    convertedMassKg, condensateTemperatureK);

                if (remainingMassKg > 0f)
                {
                    flow.SetContents(cell, new ConduitFlow.ConduitContents(contents.element,
                        remainingMassKg, contents.temperature, contents.diseaseIdx,
                        remainingDisease));
                }
                else
                {
                    flow.SetContents(cell, ConduitFlow.ConduitContents.Empty);
                }

                float latentHeatReleasedJ = convertedMassKg * latentHeatJPerKg;
                int[] billCells = state.Cells != null && state.Cells.Length > 0
                    ? state.Cells
                    : new int[] { cell };
                // LOCAL, not network-wide: see PipeMatterFacade.BillLatentHeatToCell for why
                // spreading a phase change's heat over a run that spans two rooms cancels a
                // refrigeration cycle exactly.
                if (!PipeMatterFacade.BillLatentHeatToCell(cell, gasConduit: true, flow,
                        latentHeatReleasedJ))
                {
                    // Nothing in the network could take it. Cannot happen while the condensate
                    // this call just banked is still there, but energy must never simply vanish,
                    // so fall back to the local accounting the kernel already computed.
                    float liquidHeatCapacity =
                        convertedMassKg * liquid.specificHeatCapacity * GramsPerKilogram;
                    if (liquidHeatCapacity > 0f)
                    {
                        PipeMatterState.AddTemperatureK(gasConduit: true, cell,
                            latentHeatReleasedJ / liquidHeatCapacity);
                    }
                }

                EvaluateMatterStateDamage(state.Cells, gasConduit: true);

                Debug.Log(LogPrefix + $"cell {cell}: condensed {convertedMassKg:F4} kg "
                    + $"{element.tag} -> {liquid.tag} at {boundaryK:F1}K "
                    + $"(pipe was {contents.mass:F3} kg at {contents.temperature:F1}K, "
                    + $"remainder {remainingMassKg:F3} kg, condensate left at "
                    + $"{condensateTemperatureK:F1}K, {latentHeatReleasedJ / 1000f:F2} kJ latent "
                    + $"heat billed across {billCells.Length} network tile(s))");

                // Handled: skip vanilla's damage AND its dump-as-gas entirely.
                return false;
            }
            catch (System.Exception e)
            {
                // This runs on a real vanilla event path for every gas conduit in the game. A
                // mistake here must degrade to vanilla's own behavior, never break every pipe.
                Debug.LogWarning(LogPrefix + "falling through to vanilla after an exception: " + e);
                return true;
            }
        }
    }
}
