using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace Mod2MatterEnvironment
{
    /// <summary>
    /// The Solar Panel makes twice the power, in every game: up
    /// to 760 W instead of 380 W, to pay for the Electrolyzer's 960 W (ElectrolyzerPower.cs). The
    /// Mars lander keeps its two panels and one battery.
    ///
    /// The OUTPUT doubles, not the light: the cells still read the same lux. Vanilla's
    /// SolarPanel.EnergySim200ms inlines both numbers as literals (SolarPanelConfig.WATTS_PER_LUX
    /// 0.00053 and MAX_WATTS 380 are consts), so a transpiler swaps them there: 0.00053 W per lux
    /// per cell becomes 0.00106, and the 380 W clamp and the meter's 380 W full scale become 760.
    /// The def's GeneratorWattageRating and GeneratorBaseCapacity follow, so the tooltips, the
    /// power overlay and the circuit's capacity read the new maximum.
    ///
    /// One 760 W panel fits a 1 kW basic wire alone; two do not (1520 W), and vanilla's circuit
    /// overload rule applies to them as to any other generators.
    ///
    /// Stationeers' solar panel output is irradiance x panel area x the solar ratio, softly
    /// capped through a log near MaxPowerGenerated (500 W on the class; a prefab may set its own,
    /// UNVERIFIED), times the tracking efficiency and the damage. This keeps ONI's per-cell lux sum
    /// and hard clamp and doubles both numbers, by choice.
    /// </summary>
    public static class SolarPanelPower
    {
        public const float VanillaWattsPerLux = 0.00053f;
        public const float VanillaMaxWatts = 380f;
        public const float WattsPerLux = 2f * VanillaWattsPerLux;
        public const float MaxWatts = 2f * VanillaMaxWatts;

        /// <summary>How many literals the transpiler replaced at load: 1 per-lux factor and 2
        /// maxima (the clamp and the meter) on the supported game build. Logged, so a game update that
        /// moves them is visible.</summary>
        public static int PerLuxReplaced { get; internal set; }
        public static int MaxReplaced { get; internal set; }
    }

    [HarmonyPatch(typeof(SolarPanelConfig), nameof(SolarPanelConfig.CreateBuildingDef))]
    internal static class SolarPanelConfig_CreateBuildingDef
    {
        private static void Postfix(BuildingDef __result)
        {
            __result.GeneratorWattageRating = SolarPanelPower.MaxWatts;
            __result.GeneratorBaseCapacity = __result.GeneratorWattageRating;
        }
    }

    [HarmonyPatch(typeof(SolarPanel), nameof(SolarPanel.EnergySim200ms))]
    internal static class SolarPanel_EnergySim200ms
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int perLux = 0;
            int max = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value)
                {
                    if (value == SolarPanelPower.VanillaWattsPerLux)
                    {
                        instruction.operand = SolarPanelPower.WattsPerLux;
                        perLux++;
                    }
                    else if (value == SolarPanelPower.VanillaMaxWatts)
                    {
                        instruction.operand = SolarPanelPower.MaxWatts;
                        max++;
                    }
                }
                yield return instruction;
            }
            SolarPanelPower.PerLuxReplaced = perLux;
            SolarPanelPower.MaxReplaced = max;
            if (perLux != 1 || max != 2)
            {
                Debug.LogWarning("[Mod2MatterEnvironment] SolarPanel.EnergySim200ms: expected 1 per-lux and 2 "
                    + "max-watt literals, replaced " + perLux + " and " + max + "; the game may have changed.");
            }
            else
            {
                Debug.Log("[Mod2MatterEnvironment] Solar Panel output doubled: "
                    + SolarPanelPower.WattsPerLux + " W/lux, " + SolarPanelPower.MaxWatts + " W max.");
            }
        }
    }
}
