using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// The first real (non-debug-key) trigger for gas-mixture tracking: augments the vanilla
    /// "Gas Element Sensor" building (LogicElementSensorGas) rather than inventing a new
    /// buildable: an existing building augmented rather than a new one with its own art. Every second, samples the vanilla element and mass already sitting in
    /// this building's own cell (Grid.Element[cell], Grid.Mass[cell] -- both real vanilla
    /// per-cell state, not anything this mod added) and moves a small amount of it into
    /// GasMixtureFacade via ConvertFromVanilla, so placing the sensor "switches on"
    /// volume-fractions tracking at that location using the atmosphere that's actually there.
    ///
    /// CONSERVATION. GasMixtureFacade.Inject only ever ADDS mass to the gas-mixture layer without
    /// removing the vanilla original, which would duplicate matter (the F9 debug injector in
    /// Patches.cs does that, as a debug tool only). This uses GasMixtureFacade.ConvertFromVanilla, backed by a new native message,
    /// ext::kRemoveVanillaMass (abi/sim_abi_ext.h), symmetric with kInjectGasSpecies -- it
    /// takes the same amount back out of the cell's vanilla PhaseEntry, natively clamped so
    /// the cell's mass can never go negative. Every sample this component takes is now a
    /// conserving transfer, not a duplication.
    /// </summary>
    public class GasMixtureInjectorComponent : MonoBehaviour
    {
        private const float SampleIntervalSeconds = 1f;
        private const float SampleFraction = 0.01f; // 1% of the cell's vanilla mass per sample

        private float timer;

        private void Update()
        {
            timer += Time.deltaTime;
            if (timer < SampleIntervalSeconds)
            {
                return;
            }
            timer = 0f;

            int cell = Grid.PosToCell(transform.position);
            if (!Grid.IsValidCell(cell))
            {
                return;
            }

            Element element = Grid.Element[cell];
            if (element == null || !element.IsGas)
            {
                return;
            }

            float cellMass = Grid.Mass[cell];
            if (cellMass <= 0f)
            {
                return;
            }

            int elementIdx = ElementLoader.elements.IndexOf(element);
            if (elementIdx < 0)
            {
                return;
            }

            GasMixtureFacade.ConvertFromVanilla(cell, elementIdx, cellMass * SampleFraction);

            // Promotion trigger: the Gas Element Sensor promotes its own room, as any building
            // that puts gas into the mixture should. SetRoomOwned on the native
            // side is idempotent (repeated promotion of an already-owned room is a no-op), so
            // calling this every sample tick alongside ConvertFromVanilla needs no separate
            // "have I already promoted this room" bookkeeping here.
            GasMixtureFacade.PromoteRoom(cell);
        }
    }
}
