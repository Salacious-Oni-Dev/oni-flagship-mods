using HarmonyLib;
using KMod;

namespace Mod5AutomationIc10
{
    // Flagship 3, Mod 5: Automation / IC10.
    //
    // The language is in Ic10.dll, which has no game in it and ships beside this assembly.
    // This assembly is the game side: the IC Housing building, the adapter that makes any
    // ONI building a device, the data cable, the logic units and the program editor.
    //
    // It needs OniFramework, and the SDK's replacement SimDLL for the atmosphere readings
    // (a mixed cell's temperature, pressure and gas ratios come through GasMixtureFacade).
    public class Mod : UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            // base.OnLoad applies every [HarmonyPatch] in this assembly.
            base.OnLoad(harmony);

            // The framework API level this mod was built against. Reports only: Require logs a
            // skew loudly and the mod carries on.
            OniFramework.FrameworkVersion.Require(0, 2, "Mod5AutomationIc10");
            Ic10HoverReadout.Install(harmony);

            Ic10HousingStrings.Install();
            Ic10DataCableStrings.Install();
            Ic10DataOverlayStrings.Install();
            Ic10TransformerCapStrings.Install();
            Ic10BrickStrings.Install();
            Ic10UnitStrings.Install();
            Ic10SorterStrings.Install();
            Mod5Log.Info("loaded: IC Housing, Data Cable and bridge, data overlay, " + Ic10Devices.Enums.LogicType.Count + " logic types");
        }
    }
}
