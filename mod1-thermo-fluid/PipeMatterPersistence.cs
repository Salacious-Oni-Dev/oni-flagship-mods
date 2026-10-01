using System.Collections.Generic;
using KSerialization;
using HarmonyLib;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// Makes <see cref="PipeMatterState"/> survive a save/load.
    ///
    /// WHY A COMPONENT ON THE CONDUIT rather than one blob somewhere central. Trapped condensate
    /// or ice belongs to a specific pipe, and ONI already solves "state that belongs to a
    /// building" -- KSerialization round-trips a <c>KMonoBehaviour</c>'s <c>[Serialize]</c>
    /// fields with the building itself, so the matter is saved, loaded, and DESTROYED alongside
    /// the pipe holding it with no bookkeeping of our own. It is the same pattern
    /// <c>GasMixtureTankComponent</c> uses for the compressor's tank, and for the same reason.
    ///
    /// The dictionary in <see cref="PipeMatterState"/> stays the runtime authority -- it is read
    /// on hot paths -- and this component is a write-through mirror of it. Keeping both is
    /// deliberate: a per-cell dictionary lookup is far cheaper than a GetComponent on every
    /// freeze tick, and the component only has to be correct at save time.
    ///
    /// The component must exist on the PREFAB, not be added at runtime, or a loaded save would
    /// have nothing to deserialize into.
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class PipeTrappedMatterComponent : KMonoBehaviour
    {
        [Serialize] private int elementIdx = -1;
        [Serialize] private float massKg;
        [Serialize] private float temperatureK;

        /// <summary>
        /// Exactly what deserialization handed back, per cell, for gas conduits and for liquid
        /// ones -- recorded in <see cref="OnSpawn"/> before anything else can touch the runtime
        /// store. For a save round-trip test (PIPESAVE), and only that: the runtime store is the
        /// wrong thing for such a test to read, because the game has run by the time it reads it.
        ///
        /// Under the sim's PHASE policy a save holds tiles sitting on their dew point, and a
        /// couple of seconds of play after loading is enough for PipeMatterState's housekeeping
        /// to legitimately return the warmest of them -- which would say nothing about
        /// serialization. Never cleared: one process loads one
        /// save in every place this is read.
        /// </summary>
        internal static readonly Dictionary<int, OniFramework.PipeMatterFacade.TrappedMatter>
            RestoredGas = new Dictionary<int, OniFramework.PipeMatterFacade.TrappedMatter>();

        /// <summary>The liquid-conduit half of <see cref="RestoredGas"/>.</summary>
        internal static readonly Dictionary<int, OniFramework.PipeMatterFacade.TrappedMatter>
            RestoredLiquid = new Dictionary<int, OniFramework.PipeMatterFacade.TrappedMatter>();

        /// <summary>
        /// True for a gas conduit, false for a liquid one -- which of
        /// <see cref="PipeMatterState"/>'s two stores this tile belongs to. Read off the real
        /// <c>Conduit</c> component rather than stored, so it cannot go stale.
        /// </summary>
        private bool GasConduit
        {
            get
            {
                Conduit conduit = GetComponent<Conduit>();
                return conduit == null || conduit.ConduitType == ConduitType.Gas;
            }
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            if (massKg <= 0f || elementIdx < 0 || elementIdx >= ElementLoader.elements.Count)
            {
                return;
            }

            // Restore into the runtime store. Deliberately AFTER base.OnSpawn so the Conduit
            // component this reads its type from is itself spawned.
            bool gasConduit = GasConduit;
            int cell = Grid.PosToCell(transform.GetPosition());
            (gasConduit ? RestoredGas : RestoredLiquid)[cell] =
                new OniFramework.PipeMatterFacade.TrappedMatter
                {
                    ElementIdx = elementIdx,
                    MassKg = massKg,
                    TemperatureK = temperatureK,
                };
            PipeMatterState.Add(gasConduit, cell, elementIdx, massKg, temperatureK);
        }

        /// <summary>
        /// Write-through from the runtime store. Called by <see cref="PipeMatterState"/> whenever
        /// a tile's trapped matter changes, so the serialized fields are always current without
        /// needing a serialization callback.
        /// </summary>
        internal void Mirror(int newElementIdx, float newMassKg, float newTemperatureK)
        {
            elementIdx = newElementIdx;
            massKg = newMassKg;
            temperatureK = newTemperatureK;
        }
    }

    /// <summary>
    /// Puts <see cref="PipeTrappedMatterComponent"/> on every conduit prefab in the game.
    ///
    /// ONE hook over the finished building list rather than a patch per conduit config: ONI ships
    /// gas and liquid conduits in plain, insulated, radiant, bridge, overflow and preferential
    /// flavours, and any of them can hold condensate. Selecting on "the prefab has a
    /// <c>Conduit</c> component" catches every one of those, plus any a future update or another
    /// mod adds, and cannot silently miss one the way an explicit list would.
    ///
    /// <c>AddOrGet</c>, not <c>AddComponent</c>: harmless if this ever runs twice.
    /// </summary>
    [HarmonyPatch(typeof(GeneratedBuildings), "LoadGeneratedBuildings")]
    internal static class GeneratedBuildings_LoadGeneratedBuildings_PipeMatter
    {
        private static void Postfix()
        {
            int patched = 0;
            foreach (BuildingDef def in Assets.BuildingDefs)
            {
                if (def == null || def.BuildingComplete == null)
                {
                    continue;
                }
                if (def.BuildingComplete.GetComponent<Conduit>() == null)
                {
                    continue;
                }
                def.BuildingComplete.AddOrGet<PipeTrappedMatterComponent>();
                patched++;
            }

            Debug.Log($"[Mod1ThermoFluid] trapped-matter persistence attached to {patched} "
                + "conduit building(s)");
        }
    }
}
