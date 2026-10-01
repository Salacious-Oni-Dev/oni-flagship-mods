using System;
using System.Reflection;
using HarmonyLib;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// THE SPARGER: vanilla's Gas Vent, made to work under liquid -- an augmented Gas Vent rather
    /// than a new building; out of water it stays a plain Gas Vent.
    ///
    /// WHY VANILLA'S VENT CANNOT DO IT. <c>Vent.IsValidOutputCell</c> is
    /// <c>Grid.Mass[cell] &lt; overpressureMass</c>, and <c>GasVentConfig</c> sets that to 2 kg. A
    /// water cell holds about a tonne, so a flooded Gas Vent reads OverPressure forever and its
    /// pipe backs up.
    ///
    /// WHAT CHANGES, only while the vent's own cell is liquid:
    /// <list type="bullet">
    /// <item><b>The gate is pressure, not mass.</b> A postfix on <c>Vent.GetEndPointState</c> makes
    /// the vent Ready when the gas it has to push -- the packet in its pipe, or what it already
    /// holds in storage, each at Mod 1's 10 L conduit volume
    /// (<see cref="GasMixtureFacade.GasConduitVolumeM3"/>) -- is at a higher pressure than the
    /// liquid at the vent (<see cref="Bubbles.PressureAtPa"/>: the gas above the surface plus
    /// ρ·g·h). Otherwise it stays OverPressure, vanilla's own state and status line. With nothing
    /// to push it is Ready, so an idle flooded vent does not show a fault.</item>
    /// <item><b>What comes out is bubbles.</b> A prefix on <c>Exhaust.EmitGas</c> runs vanilla's own
    /// <c>EmitCommon</c> -- the exhaust's disease exchange, the lifetime vented mass, the
    /// animation trigger -- and hands it an emitter that calls <see cref="Bubbles.SpawnSplit"/>
    /// instead of <c>AddRemoveSubstance</c>. OniFramework.Bubbles then carries each bubble up at its real
    /// speed and trades its heat with the water.</item>
    /// </list>
    ///
    /// BACK-PRESSURE, and why it needs no compressor. When the vent cannot push, vanilla already
    /// does the right thing with it: <c>Exhaust</c> clears its <c>canExhaust</c> requirement flag,
    /// <c>ConduitConsumer</c> (whose <c>OperatingRequirement</c> is Operational) stops taking from
    /// the pipe, and the pipe backs up. Backed-up packets merge up to Mod 1's 8 kg a tile, so the
    /// pressure at the vent climbs until it wins. At 10 L a tile that is quick: 20 g of CO2 at
    /// 300 K is about 113 kPa, more than ten tiles of water. A deep vent on a slow line shows up as
    /// a backed-up, pressurised line -- which Mod 1's pipe stress warnings already read -- not as
    /// a vent that never runs.
    ///
    /// WHAT IS SIMPLIFIED. Once the vent is Ready it releases everything it holds, not only the
    /// share above the local pressure: the gate says whether gas flows, not how much. Liquid never
    /// enters the gas pipe; ONI's conduits have no reverse flow for it to use.
    /// </summary>
    internal static class SubmergedGasVent
    {
        /// <summary>Kilograms released as bubbles by every flooded vent since load. For rigs.</summary>
        internal static double BubbledKg;

        /// <summary>Releases that found no <c>BubbleManager</c> and went into the cell instead.</summary>
        internal static long FallbackReleases;

        private static readonly AccessTools.FieldRef<Vent, int> VentCell =
            AccessTools.FieldRefAccess<Vent, int>("cell");

        private static MethodInfo emitCommon;
        private static Delegate bubbleEmitter;

        internal struct Reading
        {
            internal bool Submerged;
            internal bool HasGas;
            internal float LiquidPa;
            internal float PipePa;
            internal float StoredPa;

            internal float PushPa => Mathf.Max(PipePa, StoredPa);
            internal bool CanPush => PushPa > LiquidPa;
        }

        internal static bool IsGasSink(Vent vent)
        {
            return vent != null && vent.conduitType == ConduitType.Gas && vent.endpointType == Endpoint.Sink;
        }

        internal static int CellOf(Vent vent)
        {
            return VentCell(vent);
        }

        internal static Vector2 ReleasePoint(int cell)
        {
            return Grid.CellToPosCCC(cell, Grid.SceneLayer.Front);
        }

        /// <summary>The numbers the gate compares. <see cref="Reading.Submerged"/> false means
        /// the vent is in gas and vanilla decides.</summary>
        internal static Reading Read(Vent vent)
        {
            Reading r = default;
            int cell = VentCell(vent);
            if (!Grid.IsValidCell(cell) || !Grid.IsLiquid(cell))
            {
                return r;
            }
            r.Submerged = true;
            r.LiquidPa = Bubbles.PressureAtPa(ReleasePoint(cell));
            if (GasMixtureFacade.TryGetConduitPressure(cell, out float pipePa))
            {
                r.PipePa = pipePa;
                r.HasGas = true;
            }
            Storage storage = vent.GetComponent<Storage>();
            if (storage != null)
            {
                // Dalton: each stored gas at its own mass and temperature, as partial pressures
                // over the one 10 L volume.
                foreach (GameObject item in storage.items)
                {
                    PrimaryElement pe = item != null ? item.GetComponent<PrimaryElement>() : null;
                    if (pe == null || pe.Mass <= 0f || !pe.Element.IsGas)
                    {
                        continue;
                    }
                    r.HasGas = true;
                    int idx = ElementLoader.GetElementIndex(pe.ElementID);
                    if (GasMixtureFacade.TryComputePressure(new[] { idx }, new[] { pe.Mass },
                            pe.Temperature, GasMixtureFacade.GasConduitVolumeM3, out float partialPa))
                    {
                        r.StoredPa += partialPa;
                    }
                }
            }
            return r;
        }

        [HarmonyPatch(typeof(Vent), nameof(Vent.GetEndPointState))]
        internal static class Vent_GetEndPointState
        {
            private static void Postfix(Vent __instance, ref Vent.State __result)
            {
                // Blocked is a solid cell and Invalid is not a sink; neither is ours to change.
                if (__result != Vent.State.OverPressure || !IsGasSink(__instance))
                {
                    return;
                }
                Reading r = Read(__instance);
                if (!r.Submerged)
                {
                    return;
                }
                __result = !r.HasGas || r.CanPush ? Vent.State.Ready : Vent.State.OverPressure;
            }
        }

        [HarmonyPatch(typeof(Exhaust), "EmitGas")]
        internal static class Exhaust_EmitGas
        {
            private static bool Prefix(Exhaust __instance, int cell)
            {
                if (!Grid.IsValidCell(cell) || !Grid.IsLiquid(cell) || !EnsureReflection())
                {
                    return true;
                }
                Storage storage = __instance.GetComponent<Storage>();
                if (storage == null)
                {
                    return true;
                }
                // Vanilla's EmitGas, with the emitter swapped: first gas item with mass, one per
                // update.
                object[] args = { cell, null, bubbleEmitter };
                foreach (GameObject item in storage.items)
                {
                    PrimaryElement pe = item != null ? item.GetComponent<PrimaryElement>() : null;
                    if (pe == null || !pe.Element.IsGas)
                    {
                        continue;
                    }
                    args[1] = pe;
                    if ((bool)emitCommon.Invoke(__instance, args))
                    {
                        break;
                    }
                }
                return false;
            }
        }

        /// <summary>Called by vanilla's <c>EmitCommon</c> in place of its
        /// <c>AddRemoveSubstance</c> emitter, with the item's post-disease-exchange state.</summary>
        private static void EmitAsBubbles(int cell, PrimaryElement pe)
        {
            int spawned = Bubbles.SpawnSplit(pe.ElementID, ReleasePoint(cell), pe.Mass,
                pe.Temperature, pe.DiseaseIdx, pe.DiseaseCount);
            if (spawned > 0)
            {
                BubbledKg += pe.Mass;
                return;
            }
            // No bubble manager: put it in the cell the way vanilla would, rather than let
            // EmitCommon zero a mass that went nowhere.
            FallbackReleases++;
            SimMessages.AddRemoveSubstance(cell, pe.ElementID, CellEventLogger.Instance.ExhaustSimUpdate,
                pe.Mass, pe.Temperature, pe.DiseaseIdx, pe.DiseaseCount);
        }

        private static bool EnsureReflection()
        {
            if (bubbleEmitter != null)
            {
                return true;
            }
            emitCommon = AccessTools.Method(typeof(Exhaust), "EmitCommon");
            Type emitDelegate = AccessTools.Inner(typeof(Exhaust), "EmitDelegate");
            MethodInfo target = AccessTools.Method(typeof(SubmergedGasVent), nameof(EmitAsBubbles));
            if (emitCommon == null || emitDelegate == null || target == null)
            {
                Debug.LogWarning("[Mod1ThermoFluid] SubmergedGasVent: Exhaust.EmitCommon or its "
                    + "EmitDelegate is missing; flooded gas vents fall back to vanilla");
                return false;
            }
            bubbleEmitter = Delegate.CreateDelegate(emitDelegate, target);
            return true;
        }
    }

    /// <summary>
    /// The flooded Gas Vent's readout: while under liquid, one status line saying what the vent
    /// has to beat and what it has. Neutral, not a fault -- vanilla's own OverPressure line is
    /// still the one that says the vent is stopped.
    /// </summary>
    public class SubmergedGasVentReadout : KMonoBehaviour, ISim1000ms
    {
        private static StatusItem item;

        private Guid handle;

        private sealed class Data
        {
            internal string Summary;
            internal string Detail;
        }

        private readonly Data data = new Data();

        public void Sim1000ms(float dt)
        {
            Vent vent = GetComponent<Vent>();
            KSelectable selectable = GetComponent<KSelectable>();
            if (vent == null || selectable == null || !SubmergedGasVent.IsGasSink(vent))
            {
                return;
            }
            SubmergedGasVent.Reading r = SubmergedGasVent.Read(vent);
            if (!r.Submerged)
            {
                if (handle != Guid.Empty)
                {
                    handle = selectable.RemoveStatusItem(handle);
                }
                return;
            }
            EnsureStatusItem();
            string push = r.HasGas ? PipeReadout.FormatPressure(r.PushPa) : "no gas";
            data.Summary = $"Underwater vent: needs {PipeReadout.FormatPressure(r.LiquidPa)}, has {push}";
            data.Detail = "This vent is under liquid, so it releases its gas as bubbles. To push "
                + "gas out it must beat the pressure of the liquid at its depth.\n\n"
                + $"Liquid pressure here: {PipeReadout.FormatPressure(r.LiquidPa)}\n"
                + $"Pipe: {(r.PipePa > 0f ? PipeReadout.FormatPressure(r.PipePa) : "empty")}\n"
                + $"Held in the vent: {(r.StoredPa > 0f ? PipeReadout.FormatPressure(r.StoredPa) : "nothing")}\n\n"
                + "A line that cannot push backs up, and its pressure rises until it can.";
            if (handle == Guid.Empty)
            {
                handle = selectable.AddStatusItem(item, data);
            }
        }

        private static void EnsureStatusItem()
        {
            if (item != null)
            {
                return;
            }
            // Explicit strings, for the reason PipeStressMonitor gives: CreateStatusItem looks
            // them up in Klei's table, which has no entries for a mod's item.
            item = new StatusItem(
                "Mod1SubmergedGasVent",
                "Underwater vent",
                "This vent is under liquid and releases its gas as bubbles.",
                "",
                StatusItem.IconType.Info,
                NotificationType.Neutral,
                allow_multiples: false,
                render_overlay: OverlayModes.GasConduits.ID);
            item.resolveStringCallback = (fallback, d) =>
                d is Data x && !string.IsNullOrEmpty(x.Summary) ? x.Summary : fallback;
            item.resolveTooltipCallback = (fallback, d) =>
                d is Data x && !string.IsNullOrEmpty(x.Detail) ? x.Detail : fallback;
        }
    }
}
