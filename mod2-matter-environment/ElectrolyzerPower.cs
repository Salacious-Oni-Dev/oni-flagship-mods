using HarmonyLib;

namespace Mod2MatterEnvironment
{
    /// <summary>
    /// The Electrolyzer draws 960 W, not 120 W, in every game.
    ///
    /// Once the Hydrogen Generator gives its water back as steam (HydrogenCombustor.cs), electrolyzer
    /// plus generator is a loop closed in mass: water -> H2 + O2 -> steam -> water. At vanilla's
    /// 120 W it made about +690 W per generator from nothing but a heat sink. The generator burns
    /// 0.9 kg/s of reactants at 1:8, which is 0.9 kg/s of water, so 0.9 of an electrolyzer:
    /// 0.9 x 960 = 864 W against the generator's 800 W, net -64 W before any pump. The hydrogen side
    /// alone needs 0.1 / 0.112 = 0.893 of an electrolyzer, 857 W, also above 800 W. The break-even
    /// is 800 / 0.9 = 889 W; 960 W is the first multiple of vanilla's 120 W above it, and it still
    /// fits one 1 kW basic wire.
    ///
    /// Stationeers prices electrolysis by enthalpy (ElectrolysisMachine.OnAtmosphericTick: water
    /// split per tick = ElectricEfficiency 40 x UsedPower / (product energy - feed energy), at
    /// ActiveUsedPower 3600 W). ONI's ElementConverter runs at a fixed rate, so this keeps the
    /// fixed rate and charges one fixed wattage, by choice.
    /// </summary>
    [HarmonyPatch(typeof(ElectrolyzerConfig), nameof(ElectrolyzerConfig.CreateBuildingDef))]
    internal static class ElectrolyzerConfig_CreateBuildingDef
    {
        public const float ActiveWatts = 960f;

        private static void Postfix(BuildingDef __result)
        {
            __result.EnergyConsumptionWhenActive = ActiveWatts;
        }
    }
}
