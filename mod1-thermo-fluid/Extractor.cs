using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// The reverse of GasMixtureInjectorComponent (Injector.cs): augments the vanilla Gas Vent
    /// building (GasVentConfig, real building, real kanim, no new BuildingDef needed -- same
    /// "augment, don't invent" move used for the Gas Element Sensor) so a placed vent pulls
    /// mass back OUT of the gas-mixture layer and releases it into vanilla atmosphere. Closes
    /// the loop the sensor opened one-directionally: sensor converts vanilla -> mixture, vent
    /// converts mixture -> vanilla, and GasMixtureFacade.ConvertToVanilla keeps the round trip
    /// conserving on both legs (see that method's own doc comment for why it's one atomic
    /// native message instead of a remove-then-add pair).
    ///
    /// Every second, checks its own cell's mixture composition and releases up to 10% of
    /// whatever's there back into vanilla, using the cell's own current temperature
    /// (Grid.Temperature) as the merge temperature -- the mixture layer doesn't track a
    /// per-species temperature independently (see gas_mixture_abi.h's atmosphere-only scope
    /// note), so the cell's own shared field is the only real value available. If the mixture
    /// holds more than one species, releases the currently-dominant one each tick rather than
    /// all of them at once -- a small, deliberate simplification for this slice.
    /// </summary>
    public class GasMixtureExtractorComponent : MonoBehaviour
    {
        private const float SampleIntervalSeconds = 1f;
        private const float ReleaseFraction = 0.1f; // 10% of the dominant species per sample

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

            GasMixtureFacade.GasComponent[] composition = GasMixtureFacade.TryGetComposition(cell);
            if (composition.Length == 0)
            {
                return;
            }

            // Release the dominant species -- the one holding the most mass this sample.
            GasMixtureFacade.GasComponent dominant = composition[0];
            for (int i = 1; i < composition.Length; i++)
            {
                if (composition[i].MassKg > dominant.MassKg)
                {
                    dominant = composition[i];
                }
            }

            float releaseKg = dominant.MassKg * ReleaseFraction;
            if (releaseKg <= 0f)
            {
                return;
            }

            float temperatureK = Grid.Temperature[cell];
            GasMixtureFacade.ConvertToVanilla(cell, dominant.ElementIdx, releaseKg, temperatureK);
        }
    }
}
