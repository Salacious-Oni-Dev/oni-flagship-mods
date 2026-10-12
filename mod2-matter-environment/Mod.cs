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
            // skew and returns false, and never throws. The overload with `out owner` also mints
            // the prefix the dissolved-sugar and ethanol properties register under.
            OniFramework.ExtOwner owner;
            OniFramework.FrameworkVersion.Require(0, 2, "Mod2MatterEnvironment", out owner);
            // Mod 2 owns the Hydrogen Generator (HydrogenCombustor.cs: one mixed inlet, water after
            // each burn). Another mod's patch to the same building is set aside at load; with both
            // applied, the building made its water twice.
            OniFramework.BuildingClaims.Claim(HydrogenGeneratorConfig.ID, "Mod2MatterEnvironment",
                "HydrogenCombustor: mixed-gas inlet, combustion water");
            // Mod 2 owns the Electrolyzer's power draw (ElectrolyzerPower.cs: 960 W closes the
            // electrolyzer and generator loop).
            OniFramework.BuildingClaims.Claim(ElectrolyzerConfig.ID, "Mod2MatterEnvironment",
                "ElectrolyzerPower: 960 W");
            FuelStoreOffSim.Bind();

            // Hygiene reads odour from the frame the sim publishes each tick rather than through a
            // worker barrier per duplicant; the binder is idempotent.
            OniFramework.SimExtFrame.Install(harmony);

            PollutantElements.Register();
            NitrogenElements.Register();

            // Smoke: the Soot element, Mod 2's fuel rows, and BurnAt armed. (The smoke system came from
            // an idea by Kaz.) Install only
            // hooks SimExtFrame's per-tick binder, which sends a burn's particulate a tick after its
            // gas; nothing burns unless something calls BurnAt.
            SootElement.Register();
            CombustionFuels.Register();
            OniFramework.Combustion.Install(harmony);
            // Combustion: fuel gas in the world now catches
            // fire by Stationeers' rule -- sparked, or above its auto-ignition temperature, with
            // oxygen beside it -- and burns through the same BurnAt. Unlike the line above this
            // DOES change an ordinary game: natural gas beside oxygen near magma will burn.
            OniFramework.Ignition.Install(harmony);
            CombustionFuels.RegisterAutoIgnition();
            // The fire visual: every burning cell gets a
            // shader flame, sparks, smoke puffs when it is starved, and a vanilla Light2D. Visual
            // only; both run off the per-tick binder Install just armed.
            OniFramework.FireLights.Enable();
            OniFramework.FireVisuals.Enable();
            // The smoke you can see: the sim's own airborne soot, ash and dust drawn as a haze that
            // rises and lingers with the gas carrying it.
            OniFramework.SmokeHaze.Enable(harmony);
            // The ignition source: the
            // Sparker's strings and build-menu entry. The BuildingDef registers itself.
            Sparker.Install();
            // The physical campfire (fuelled, lit and burned by dupes). See Campfire.cs.
            Campfire.Install();
            // Falling liquid trades heat with the gas it falls through, and boils or freezes on
            // the way down (for ALL falling liquid). The framework ships it off; with Mod 2 it is on,
            // in every game. Its in-flight boil needs promote-and-mix, which Mod 1 sends at game start; without Mod 1 the drops
            // still trade heat and boil where they land.
            OniFramework.FallingLiquidExchange.Install(harmony);
            OniFramework.FallingLiquidExchange.Enabled = true;
            // The Fire Sprinkler: a plumbed ceiling head with a
            // heat bulb. Strings and the Plumbing menu entry; the BuildingDef registers itself.
            FireSprinkler.Install();
            // Distillation: sugar and ethanol
            // carried by water, the Mash Tun that makes a sugary mash from fruit, and Klei's SugarWater
            // dissolving into Water as a sugar source. This DOES change an ordinary game: SugarWater
            // touching Water now dissolves into it instead of layering.
            Fermentables.Install(harmony, owner);
            MashTun.Install();
            SugarWaterSource.Enabled = true;
        }
    }
}
