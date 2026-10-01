using HarmonyLib;
using KMod;

namespace Mod2MatterEnvironment
{
    /// <summary>
    /// Mod 2 -- Matter / Environmental Physics.
    ///
    /// Adds the elements a physical atmosphere needs (nitrogen and a pollutant gas) and registers
    /// their physical properties. The property data and the registry live in the framework
    /// (OniFramework.MaterialPropertyRegistry) rather than here, so that any mod can read and
    /// extend them; a table private to a gameplay assembly would be reachable by nobody.
    ///
    /// UserMod2's own OnLoad already calls Harmony.PatchAll() against this assembly.
    /// </summary>
    public class Mod : UserMod2
    {
        /// <summary>
        /// Registers Mod 2's own elements before the game builds its element table.
        ///
        /// This is the one thing that genuinely cannot be done later: `Global.Awake` loads mod
        /// DLLs and calls OnLoad, and only afterwards does `Assets` call `ElementLoader.Load`. By
        /// the time any other hook this mod could use runs, the table is built, the substances
        /// are manifested and the native sim has already been sent the element list.
        ///
        /// base.OnLoad still runs, so Harmony.PatchAll against this assembly is unaffected.
        /// </summary>
        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);

            // The framework API level this mod was built against. Reports only: Require logs a
            // skew and returns false, and never throws.
            OniFramework.FrameworkVersion.Require(0, 1, "Mod2MatterEnvironment");

            PollutantElements.Register();
            NitrogenElements.Register();
        }
    }
}
