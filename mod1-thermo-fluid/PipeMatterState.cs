using System.Collections.Generic;
using OniFramework;
using UnityEngine;
using TrappedMatter = OniFramework.PipeMatterFacade.TrappedMatter;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// STANDING condensed/frozen matter inside a conduit, and the Stationeers damage rules that
    /// read it.
    ///
    /// WHY THIS EXISTS. Stationeers evaluates a pipe's phase hazards against what the pipe is
    /// ACTUALLY HOLDING right now -- `Atmosphere.TotalVolumeLiquids / Volume` for liquid in a gas
    /// pipe, and `CheckForFreezing` for solid contents. ONI has nowhere to put that: a
    /// `ConduitFlow.ConduitContents` is a single element with a single mass, so a gas conduit
    /// cannot hold "mostly oxygen plus a little liquid oxygen" at all.
    ///
    /// Pricing damage off the mass condensed in the current STEP and pushing the liquid straight
    /// into the world would make the hazard memoryless: a pipe that had been weeping condensate
    /// for a minute would look exactly like one that had just started, and nothing would
    /// accumulate the way it does in Stationeers. So the condensate STAYS IN THE PIPE as tracked
    /// state, accumulates, drives the damage rules off the standing total, and
    /// only reaches the world when the pipe actually ruptures -- which is the behaviour being
    /// modelled ("when it finally bursts, the contents can leak into the world").
    ///
    /// Keyed by cell and split by conduit type, because a single cell can legitimately host both
    /// a gas conduit and a liquid conduit (two different `ObjectLayer`s over one tile) -- the
    /// same reason `ConduitFlow_UpdateConduit` keeps two separate managed-cell sets.
    ///
    /// SERIALIZED. This dictionary stays the runtime authority because it is read on hot paths, and every
    /// mutation writes through to a `[Serialize]` mirror on the conduit's own GameObject
    /// (`PipeTrappedMatterComponent`). KSerialization then round-trips the matter with the
    /// building that holds it, and destroys it with that building too -- no bookkeeping of our
    /// own, and the same shape the compressor's `GasMixtureTankComponent` already uses.
    /// </summary>
    internal static class PipeMatterState
    {
        private const string LogPrefix = "[Mod1ThermoFluid] PIPEMATTER: ";

        private const float GramsPerKilogram = 1000f;
        private const float LitresPerCubicMetre = 1000f;

        /// <summary>
        /// Registers this class's serialized write-through with the framework store. Called once
        /// from Mod 1's own load path; the store works without it, it simply would not persist.
        /// </summary>
        internal static void Install()
        {
            PipeMatterFacade.MirrorHook = Mirror;
        }

        static PipeMatterState()
        {
            // Belt and braces: whichever of Install() or the first store access happens first,
            // the hook is in place before anything can be written.
            PipeMatterFacade.MirrorHook = Mirror;
        }

        internal static float ReleasedGasConduitKg => PipeMatterFacade.ReleasedGasConduitKg;

        internal static float ReleasedLiquidConduitKg => PipeMatterFacade.ReleasedLiquidConduitKg;

        internal static float SolidFormedGasConduitKg => PipeMatterFacade.SolidFormedGasConduitKg;

        internal static float SolidFormedLiquidConduitKg =>
            PipeMatterFacade.SolidFormedLiquidConduitKg;

        /// <summary>
        /// Write-through to the tile's own serialized mirror
        /// (<see cref="PipeTrappedMatterComponent"/>), so a save always carries the current
        /// trapped matter. Silent when the conduit has no component -- a tile with no conduit
        /// building has nothing to persist and nothing to lose.
        ///
        /// This is the one piece of the store that could not move to the framework with the rest
        /// of it: KSerialization keys a component by its type name, so relocating
        /// <see cref="PipeTrappedMatterComponent"/> to another assembly would silently invalidate
        /// every existing save. It is registered as <see cref="PipeMatterFacade.MirrorHook"/>
        /// instead.
        /// </summary>
        private static void Mirror(bool gasConduit, int cell)
        {
            if (!Grid.IsValidCell(cell))
            {
                return;
            }
            ObjectLayer layer = gasConduit ? ObjectLayer.GasConduit : ObjectLayer.LiquidConduit;
            GameObject go = Grid.Objects[cell, (int)layer];
            PipeTrappedMatterComponent mirror =
                go != null ? go.GetComponent<PipeTrappedMatterComponent>() : null;
            if (mirror == null)
            {
                return;
            }

            if (PipeMatterFacade.TryGet(gasConduit, cell, out TrappedMatter matter))
            {
                mirror.Mirror(matter.ElementIdx, matter.MassKg, matter.TemperatureK);
            }
            else
            {
                mirror.Mirror(-1, 0f, 0f);
            }
        }

        internal static bool TryGet(bool gasConduit, int cell, out TrappedMatter matter)
        {
            return PipeMatterFacade.TryGet(gasConduit, cell, out matter);
        }

        internal static IEnumerable<int> TrackedCells(bool gasConduit)
        {
            return PipeMatterFacade.TrackedCells(gasConduit);
        }

        internal static void Add(bool gasConduit, int cell, int elementIdx, float massKg,
            float temperatureK)
        {
            PipeMatterFacade.Add(gasConduit, cell, elementIdx, massKg, temperatureK);
        }

        /// <summary>
        /// Puts a tile's trapped matter into the world and forgets it -- the rupture path. Uses
        /// vanilla's own `ConduitFlowEmptyConduit` cell-event logger, so the leak reads in ONI's
        /// own event log exactly like the dump it replaces.
        /// </summary>
        internal static void Release(bool gasConduit, int cell)
        {
            if (!PipeMatterFacade.RemoveAll(gasConduit, cell, out TrappedMatter matter))
            {
                return;
            }

            if (matter.MassKg <= 0f || !Grid.IsValidCell(cell))
            {
                return;
            }

            Element element = ElementLoader.elements[matter.ElementIdx];
            SimMessages.AddRemoveSubstance(cell, element.id,
                CellEventLogger.Instance.ConduitFlowEmptyConduit, matter.MassKg,
                matter.TemperatureK, byte.MaxValue, 0);

            PipeMatterFacade.NoteReleased(gasConduit, matter.MassKg);

            Debug.Log(LogPrefix + $"cell {cell}: released {matter.MassKg:F4} kg {element.tag} "
                + $"at {matter.TemperatureK:F1}K into the world");
        }

        /// <summary>
        /// Empties a whole connected run through the tile that just broke.
        ///
        /// USER-REPORTED: the harness was destroying several segments per burst, and
        /// in Stationeers it is rare for more than one pipe or vent segment to go. The reason is
        /// physical rather than a damage-targeting detail -- "the broken or destroyed segment
        /// leaks the rest of the network out, or at the very least will reduce the pressure fast
        /// enough to prevent the break." A ruptured pipe is a hole: the network drains through
        /// it, and once it has drained there is nothing left to over-pressure, condense or
        /// freeze, so the hazard that was doing the damage stops on its own.
        ///
        /// For a liquid line the same thing happens with the solid: "the frozen liquid would
        /// expel out of the broken pipe and the rest of the network would drain the solid frozen
        /// liquid and would cool off, but potentially still get damaged." So this deliberately
        /// does NOT make the rest of the run invulnerable -- it removes the CAUSE, and whatever
        /// damage was already dealt stays dealt. A run that ices up again later can break again.
        ///
        /// Everything is expelled AT THE BREAK CELL rather than each tile venting where it sits,
        /// which is what "expel out of the broken pipe" means and also gives the player a single
        /// visible puddle at the rupture instead of a smear along the whole run.
        ///
        /// Walks conduit adjacency directly rather than asking PipeNetworkFacade for the network:
        /// the tile that broke has already been disconnected (`Conduit.OnBuildingBroken` calls
        /// `Disconnect()`), so the network query would either fail or return the wrong side of
        /// the break.
        /// </summary>
        internal static void VentNetworkThroughRupture(bool gasConduit, int breakCell,
            ObjectLayer layer, ConduitFlow flow)
        {
            var seen = new HashSet<int>();
            var queue = new Queue<int>();
            seen.Add(breakCell);
            queue.Enqueue(breakCell);

            float ventedMatterKg = 0f;
            float ventedContentsKg = 0f;
            float ventedHeatSum = 0f;
            int tiles = 0;

            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                tiles++;

                if (PipeMatterFacade.RemoveAll(gasConduit, cell, out TrappedMatter matter)
                    && matter.MassKg > 0f)
                {
                    ventedMatterKg += matter.MassKg;
                    Expel(breakCell, ElementLoader.elements[matter.ElementIdx].id, matter.MassKg,
                        matter.TemperatureK, byte.MaxValue, 0);
                    PipeMatterFacade.NoteReleased(gasConduit, matter.MassKg);
                }

                // The pressure relief itself: the run's own contents follow the trapped matter
                // out of the hole. This is what stops the cascade -- an empty pipe cannot
                // condense or freeze anything on the next tick.
                if (flow.HasConduit(cell))
                {
                    ConduitFlow.ConduitContents contents = flow.GetContents(cell);
                    if (contents.mass > 0f && contents.element != SimHashes.Vacuum)
                    {
                        ventedContentsKg += contents.mass;
                        ventedHeatSum += contents.mass * contents.temperature;
                        Expel(breakCell, contents.element, contents.mass, contents.temperature,
                            contents.diseaseIdx, contents.diseaseCount);
                        flow.SetContents(cell, ConduitFlow.ConduitContents.Empty);

                        // THE LIQUID LEAVING THIS TILE TAKES ITS DISSOLVED GAS WITH IT. This vent is
                        // a mass-mover like any other: `SetContents(Empty)` is not one of the
                        // `ConduitFlow` entry points DissolvedCargo patches, so without this line
                        // the water goes out of the hole and the gas stays in the store, in tiles
                        // that no longer have anything to carry it.
                        //
                        // Gathered at the break rather than released tile by tile, because that
                        // is where the water goes: `Expel` puts every tile's contents into
                        // `breakCell`, so the gas that was dissolved in it comes out of the same
                        // hole. `ConduitMoveShare` ignores a move to the cell it started from, so
                        // the break tile's own cargo simply stays where it already is.
                        DissolvedCargo.ConduitMoveShare(cell, breakCell, 1f);
                    }
                }

                foreach (int neighbour in Neighbours(cell))
                {
                    if (!seen.Contains(neighbour) && Grid.IsValidCell(neighbour) &&
                        Grid.Objects[neighbour, (int)layer] != null)
                    {
                        seen.Add(neighbour);
                        queue.Enqueue(neighbour);
                    }
                }
            }

            // And out it goes, at the temperature the vented liquid actually had rather than the
            // break tile's own: the gas was in that water, so the water's heat is what it leaves
            // with. Falls back to the grid reading when nothing measurable was vented.
            float ventedGasKg = 0f;
            if (ventedContentsKg > 0f)
            {
                float ventTemperatureK = ventedHeatSum / ventedContentsKg;
                if (!(ventTemperatureK > 0f))
                {
                    ventTemperatureK = Grid.IsValidCell(breakCell) && Grid.Temperature[breakCell] > 0f
                        ? Grid.Temperature[breakCell] : 293.15f;
                }
                ventedGasKg = DissolvedCargo.ConduitDegas(breakCell, ventTemperatureK);
            }

            if (ventedMatterKg > 0f || ventedContentsKg > 0f)
            {
                Debug.Log(LogPrefix + $"rupture at cell {breakCell}: vented {tiles} tile(s) -- "
                    + $"{ventedMatterKg:F3} kg trapped matter and {ventedContentsKg:F3} kg pipe "
                    + $"contents expelled through the break, releasing {ventedGasKg * 1000f:F3} g "
                    + "of dissolved gas with them; the run is now empty, so the hazard "
                    + "that caused it has stopped");
            }
        }

        /// <summary>
        /// Relieve a network through a break at <paramref name="breakCell"/>, resolving the
        /// object layer and <c>ConduitFlow</c> for the caller.
        ///
        /// Exists for the PRESSURE branch. <see cref="TickRecovery"/> reaches the vent by walking
        /// <c>TrackedCells</c> -- the tiles this class is holding condensate or ice in -- which is
        /// exactly right for a hazard whose cause is standing matter, and useless for one whose
        /// cause is pressure: a run that bursts from overpressure alone is holding nothing, so
        /// nothing would ever notice the rupture and the run would stay pressurised behind a
        /// broken segment forever. Stationeers is explicit that this is the relief -- "the broken
        /// or destroyed segment leaks the rest of the network out" -- so the pressure path calls
        /// it directly on the tick it breaks a member.
        /// </summary>
        internal static void RuptureAt(bool gasConduit, int breakCell)
        {
            ConduitFlow flow = gasConduit
                ? Game.Instance?.gasConduitFlow
                : Game.Instance?.liquidConduitFlow;
            if (flow == null || !Grid.IsValidCell(breakCell))
            {
                return;
            }

            VentNetworkThroughRupture(gasConduit, breakCell,
                gasConduit ? ObjectLayer.GasConduit : ObjectLayer.LiquidConduit, flow);
        }

        /// <summary>
        /// The trapped matter's own heat capacity (J/K) on this tile, or 0 if the tile holds
        /// none. Exists so a caller distributing energy across a whole network can weight this
        /// tile correctly: `Atmosphere` (Stationeers) holds the condensed liquid and the gas in
        /// ONE body at ONE temperature, so the condensate is part of what absorbs a latent-heat
        /// release, not a bystander to it. See Conduit_OnConduitFrozen_Condense.BillLatentHeatToNetwork.
        /// </summary>
        internal static float HeatCapacityJPerK(bool gasConduit, int cell)
        {
            return PipeMatterFacade.HeatCapacityJPerK(gasConduit, cell);
        }

        /// <summary>
        /// Shifts this tile's trapped matter by <paramref name="deltaK"/>. The caller owns the
        /// energy accounting -- this only applies an already-computed temperature change, which
        /// is why it takes a delta rather than a target: a network-wide distribution produces a
        /// UNIFORM delta, and applying it as a delta is what preserves the per-tile temperature
        /// differences ONI has and Stationeers (one temperature per network) does not.
        /// </summary>
        internal static void AddTemperatureK(bool gasConduit, int cell, float deltaK)
        {
            PipeMatterFacade.AddTemperatureK(gasConduit, cell, deltaK);
        }

        /// <summary>
        /// Brings a tile's trapped matter and the conduit's own contents to their shared
        /// equilibrium temperature, weighted by heat capacity, writing both sides back. Energy
        /// conserved by construction: the equilibrium is the heat-capacity-weighted mean, so
        /// whatever one side loses the other gains exactly.
        /// </summary>
        private static void EqualizeWithContents(bool gasConduit, int cell, ConduitFlow flow)
        {
            if (!PipeMatterFacade.TryGet(gasConduit, cell, out TrappedMatter matter)
                || matter.MassKg <= 0f)
            {
                return;
            }

            ConduitFlow.ConduitContents contents = flow.GetContents(cell);
            if (contents.mass <= 0f || contents.element == SimHashes.Vacuum)
            {
                return;
            }

            Element contentsElement = ElementLoader.FindElementByHash(contents.element);
            if (contentsElement == null)
            {
                return;
            }

            float hcMatter = matter.MassKg
                * ElementLoader.elements[matter.ElementIdx].specificHeatCapacity;
            float hcContents = contents.mass * contentsElement.specificHeatCapacity;
            float total = hcMatter + hcContents;
            if (total <= 0f)
            {
                return;
            }

            float equilibriumK =
                (matter.TemperatureK * hcMatter + contents.temperature * hcContents) / total;

            matter.TemperatureK = equilibriumK;
            PipeMatterFacade.Set(gasConduit, cell, matter);
            flow.SetContents(cell, new ConduitFlow.ConduitContents(contents.element, contents.mass,
                equilibriumK, contents.diseaseIdx, contents.diseaseCount));
        }

        private static IEnumerable<int> Neighbours(int cell)
        {
            yield return Grid.CellLeft(cell);
            yield return Grid.CellRight(cell);
            yield return Grid.CellAbove(cell);
            yield return Grid.CellBelow(cell);
        }

        private static void Expel(int cell, SimHashes element, float massKg, float temperatureK,
            byte diseaseIdx, int diseaseCount)
        {
            if (massKg <= 0f || !Grid.IsValidCell(cell))
            {
                return;
            }
            SimMessages.AddRemoveSubstance(cell, element,
                CellEventLogger.Instance.ConduitFlowEmptyConduit, massKg, temperatureK,
                diseaseIdx, diseaseCount);
        }

        /// <summary>
        /// Total trapped LIQUID volume across a run, in litres -- the numerator of Stationeers'
        /// `TotalVolumeLiquids / Volume`. Solid matter is deliberately excluded: Stationeers
        /// prices those two hazards separately and so does this.
        /// </summary>
        internal static float TrappedLiquidLitres(bool gasConduit, IEnumerable<int> cells)
        {
            return PipeMatterFacade.TrappedLiquidLitres(gasConduit, cells);
        }

        /// <summary>
        /// Total trapped SOLID matter across a run, in moles -- what Stationeers compares against
        /// `MinFrozenMolesToDamage()`. Forwards to the framework store.
        /// </summary>
        internal static float TrappedFrozenMoles(bool gasConduit, IEnumerable<int> cells)
        {
            return PipeMatterFacade.TrappedFrozenMoles(gasConduit, cells);
        }

        /// <summary>
        /// Freezes trapped LIQUID that has fallen below its own freezing point into the
        /// corresponding solid element, and melts trapped solid that has risen back above it.
        /// This is what lets a gas pipe reach Stationeers' SOLID hazard at all: the condensate
        /// has to actually become ice before `PipeBurst.Solid` means anything.
        ///
        /// Freezing/melting is treated as very nearly pressure-independent, matching
        /// Stationeers' own flat threshold for it (`Mole.cs`, and the same distinction
        /// `PipeNetworkFacade` already draws between the flat freezing point and the
        /// pressure-dependent vapour curve).
        /// </summary>
        internal static void UpdatePhase(bool gasConduit, int cell)
        {
            if (!PipeMatterFacade.TryGet(gasConduit, cell, out TrappedMatter matter)
                || matter.MassKg <= 0f)
            {
                return;
            }

            Element element = ElementLoader.elements[matter.ElementIdx];
            if (!MaterialPropertyRegistry.TryGetFreezingTemperatureK(element.id,
                out float freezingK))
            {
                return;
            }

            Element target = null;
            if (element.IsLiquid && matter.TemperatureK < freezingK)
            {
                target = element.lowTempTransition;
                if (target == null || !target.IsSolid)
                {
                    return;
                }
            }
            else if (element.IsSolid && matter.TemperatureK > freezingK)
            {
                target = element.highTempTransition;
                if (target == null || !target.IsLiquid)
                {
                    return;
                }
            }

            if (target == null)
            {
                return;
            }

            int targetIdx = ElementLoader.elements.IndexOf(target);
            if (targetIdx < 0)
            {
                return;
            }

            if (target.IsSolid)
            {
                PipeMatterFacade.NoteSolidFormed(gasConduit, matter.MassKg);
            }

            matter.ElementIdx = targetIdx;
            PipeMatterFacade.Set(gasConduit, cell, matter);

            // FREEZING AND MELTING ARE NOT FREE: swapping the element index alone would be.
            // Latent heat of fusion is real (each family carries its own measured value), it is
            // released when this freezes and absorbed when it melts, and charging neither
            // direction leaves a tile that can cycle across its freezing point for nothing.
            // Billed to the network, the same shared body condensation is billed to, so the
            // trapped matter only takes its own proportional share and a single step cannot
            // slam it back across the boundary it just crossed.
            if (MaterialPropertyRegistry.TryGetLatentHeatOfFusionJPerKg(element.id,
                    out float latentHeatOfFusionJPerKg) && latentHeatOfFusionJPerKg > 0f)
            {
                float joules = matter.MassKg * latentHeatOfFusionJPerKg;
                Conduit_OnConduitFrozen_Condense.BillLatentHeatToNetwork(cell, gasConduit,
                    gasConduit ? Game.Instance.gasConduitFlow : Game.Instance.liquidConduitFlow,
                    target.IsSolid ? joules : -joules);
            }

            Debug.Log(LogPrefix + $"cell {cell}: trapped {matter.MassKg:F4} kg became "
                + $"{target.tag} at {matter.TemperatureK:F1}K (freezing point {freezingK:F1}K)");
        }

        /// <summary>
        /// Returns trapped matter to the pipe once the pipe is no longer cold enough to hold it
        /// -- the counterpart of condensing it out, and what stops a line accumulating condensate
        /// forever after a transient chill. Handles the two dead cases too: a conduit that is
        /// gone (deconstructed) or broken leaks its trapped matter into the world instead.
        ///
        /// Called once a second from <see cref="PipeStressMonitor"/> rather than from the freeze
        /// path, because the freeze path only runs while the pipe IS cold -- nothing there would
        /// ever observe the recovery.
        /// </summary>
        /// <summary>
        /// How many times, and how much mass, <see cref="TickRecovery"/> has put standing matter
        /// in a GAS conduit back into the pipe since load, so a rig can see this housekeeping
        /// fighting the sim's phase decisions without grepping per-return log lines.
        /// </summary>
        internal static int GasReturns { get; private set; }

        /// <summary>See <see cref="GasReturns"/>.</summary>
        internal static float GasReturnedKg { get; private set; }

        internal static void TickRecovery(bool gasConduit)
        {
            ObjectLayer layer = gasConduit ? ObjectLayer.GasConduit : ObjectLayer.LiquidConduit;
            ConduitFlow flow = gasConduit
                ? Game.Instance.gasConduitFlow
                : Game.Instance.liquidConduitFlow;

            foreach (int cell in TrackedCells(gasConduit))
            {
                if (!Grid.IsValidCell(cell))
                {
                    Release(gasConduit, cell);
                    continue;
                }

                GameObject go = Grid.Objects[cell, (int)layer];
                BuildingHP hp = go != null ? go.GetComponent<BuildingHP>() : null;
                if (go == null || (hp != null && hp.IsBroken))
                {
                    // THE RUPTURE, and it relieves the WHOLE network -- not just this tile.
                    VentNetworkThroughRupture(gasConduit, cell, layer, flow);
                    continue;
                }

                // Let the trapped matter exchange heat with the gas or liquid it is sitting in
                // BEFORE deciding whether it has frozen. Without this it keeps forever whatever
                // temperature it condensed at, which had a real consequence: condensate leaves at
                // the contents' temperature plus any latent heat it could not hand to a
                // remainder (~24.8 K for oxygen), so a 60 K pipe made ~85 K liquid oxygen that
                // could never reach LiquidOxygen's 54.36 K freezing point no matter how long the
                // pipe stayed cold, and the SOLID hazard was unreachable below about 25 K. A
                // puddle of condensate physically sits in contact with the stream around it, so
                // it should equilibrate with it -- and the equilibration is heat-capacity
                // weighted and written back to BOTH sides, so it moves energy rather than
                // inventing or destroying it.
                EqualizeWithContents(gasConduit, cell, flow);

                UpdatePhase(gasConduit, cell);

                if (!TryGet(gasConduit, cell, out TrappedMatter matter) || matter.MassKg <= 0f)
                {
                    continue;
                }

                ConduitFlow.ConduitContents contents = flow.GetContents(cell);
                Element trapped = ElementLoader.elements[matter.ElementIdx];

                // Back into the pipe, as the phase the pipe is meant to carry, once the conduit's
                // own contents are warm enough that the trapped phase could not survive. A gas
                // conduit gets the gas back; a liquid conduit gets the liquid back.
                // HEADSPACE GAS IN A LIQUID CONDUIT IS NOT RECOVERABLE HERE, and getting this
                // wrong would have been ugly: `restored` for a trapped gas resolves to the gas
                // itself, whose freezing point any warm liquid line is trivially above, so this
                // path would have written STEAM into a liquid conduit's contents every second.
                // Boil-off is the Purge Valve's business, and its physical way back is
                // re-condensation at the run's own headspace pressure
                // (PipeMatterFacade.TickNetworkPhaseChange), not restoration by this housekeeping
                // pass.
                if (!gasConduit && trapped.IsGas)
                {
                    continue;
                }

                // CONDENSATE IN A GAS RUN THE SIM DECIDES IS THE SIM'S TO RETURN. Under
                // ConduitNetworkPolicy.Phase the sim decides both directions of a gas run every
                // 200 ms, against one boundary: it condenses, and it evaporates standing liquid
                // back (SimConduitNetworks.PhaseChangeKind.EvaporateTrapped). Returning it here as
                // well, the whole tile at once and once a second, would fight it: the sim holds a
                // tile on its dew point, this would find it a hair above and empty it, and the sim
                // would condense it again.
                // The heat exchange and the freeze/melt above still run here; only the return
                // moves. Solid is not the sim's to return, so it still comes through below.
                if (gasConduit && trapped.IsLiquid
                    && SimConduitNetworks.IsNativePhaseInForce(cell, PipeContentType.Gas))
                {
                    continue;
                }

                Element restored = gasConduit
                    ? trapped.highTempTransition
                    : (trapped.IsSolid ? trapped.highTempTransition : trapped);
                if (restored == null)
                {
                    continue;
                }

                float boundaryK;
                if (gasConduit)
                {
                    if (!PipeNetworkFacade.TryGetNetworkState(cell, PipeContentType.Gas,
                            out PipeNetworkState state) || state.PressurePa <= 0f ||
                        !GasMixtureFacade.TryGetEvaporationTemperatureClampedK(restored.id,
                            state.PressurePa, out boundaryK))
                    {
                        continue;
                    }
                }
                else if (!MaterialPropertyRegistry.TryGetFreezingTemperatureK(restored.id,
                    out boundaryK))
                {
                    continue;
                }

                // WHOSE TEMPERATURE DECIDES. The trapped phase survives while the tile it sits
                // in is at or below the boundary; the question is what "the tile" means when the
                // tile has no contents left.
                //
                // This used to read `contents.mass > 0f && contents.temperature <= boundaryK`,
                // which looks like a null guard and behaves as an unconditional release: a tile
                // whose contents had ENTIRELY condensed fell straight through to the return leg
                // and had its condensate evaporated back, however cold it was. Two things hid it.
                // A run where EVERY tile empties resolves to no network at all, so the pressure
                // guard above bails first and the matter stands. It is the MIXED case that breaks
                // -- one tile fully condensed inside a run that still holds gas elsewhere has a
                // real network pressure, a real boundary and no contents, so it was released on
                // every single sweep.
                //
                // The condensate has its own temperature and it is the physical one to judge: a
                // liquid at or below its boiling point at the line's pressure stays a liquid,
                // whether or not there is any gas beside it.
                float phaseJudgeK = contents.mass > 0f ? contents.temperature : matter.TemperatureK;
                if (phaseJudgeK <= boundaryK)
                {
                    continue;
                }

                // Put it back. SetContents rather than AddElement: the cap is not the constraint
                // here and AddElement would blend a temperature we have already accounted for.
                float restoredMass = contents.mass + matter.MassKg;
                float cpContents = contents.mass > 0f
                    ? contents.mass * ElementLoader.FindElementByHash(contents.element).specificHeatCapacity
                    : 0f;
                float cpTrapped = matter.MassKg * restored.specificHeatCapacity;
                float blendedK = (cpContents + cpTrapped) > 0f
                    ? (contents.temperature * cpContents + matter.TemperatureK * cpTrapped)
                        / (cpContents + cpTrapped)
                    : matter.TemperatureK;

                PipeMatterFacade.RemoveAll(gasConduit, cell, out TrappedMatter _);
                flow.SetContents(cell, new ConduitFlow.ConduitContents(restored.id, restoredMass,
                    blendedK, contents.diseaseIdx, contents.diseaseCount));
                if (gasConduit)
                {
                    GasReturns++;
                    GasReturnedKg += matter.MassKg;
                }

                // THE RETURN TRIP COSTS WHAT THE OUTBOUND ONE PAID. Condensing into the pipe
                // releases the full latent heat of vaporization into the network; evaporating
                // straight back out for free would make a pump -- a run cycling condense/return
                // several times a second heats itself while being actively cooled. Charging the reverse
                // transition is what closes it, and it is also just the physics: evaporation
                // cools its surroundings.
                //
                // A gas conduit is restoring liquid to gas, so it owes vaporization; a liquid
                // conduit is restoring solid to liquid, so it owes fusion.
                bool haveLatentHeat = gasConduit
                    ? MaterialPropertyRegistry.TryGetLatentHeatOfVaporizationJPerKg(restored.id,
                        out float latentHeatJPerKg)
                    : MaterialPropertyRegistry.TryGetLatentHeatOfFusionJPerKg(restored.id,
                        out latentHeatJPerKg);
                if (haveLatentHeat && latentHeatJPerKg > 0f)
                {
                    Conduit_OnConduitFrozen_Condense.BillLatentHeatToNetwork(cell, gasConduit, flow,
                        -(matter.MassKg * latentHeatJPerKg));
                }

                Debug.Log(LogPrefix + $"cell {cell}: returned {matter.MassKg:F4} kg to the pipe as "
                    + $"{restored.tag} at {blendedK:F1}K (pipe was above {boundaryK:F1}K)");
            }
        }
    }
}
