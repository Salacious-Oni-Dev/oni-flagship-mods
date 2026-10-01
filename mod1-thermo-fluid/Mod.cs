using UnityEngine;
using System;
using HarmonyLib;
using KMod;
using OniFramework;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// Mod 1 -- Physical Thermodynamics + Fluid Dynamics. Everything here goes through the
    /// public OniFramework API; see Patches.cs for the per-load setup.
    ///
    /// UserMod2's own OnLoad already calls Harmony.PatchAll() against this assembly, so the
    /// override below exists only for what PatchAll cannot do.
    /// </summary>
    public class Mod : UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);

            // The framework API level this mod was built against, checked BEFORE anything
            // composes. Against an older framework the first call into a missing surface fails as
            // a TypeLoadException or MissingMethodException naming a type that plainly exists in
            // this mod's own source, so Require's log line, written first, names the real cause.
            //
            // Also runs the duplicate-copy check: if some other mod has bundled its own
            // OniFramework.dll, Mono has already bound ONE of them for the whole process, and
            // this is where that gets named rather than diagnosed later from a stack trace in
            // whichever mod happened to throw. This mod is referenced Private=false precisely so
            // it is never the one causing that.
            //
            // Deliberately does not return early on false: Require only reports, and a skew may
            // touch one surface out of the many below, so bailing here would cost every mechanic
            // for the sake of one. The error is logged loudly by Require itself.
            OniFramework.FrameworkVersion.Require(0, 1, "Mod1ThermoFluid");

            // The build watermark, top-left of every screen from the main menu onward, restamped
            // to name the SimDLL, the framework and the game build instead of Klei's distribution
            // channel.
            //
            // First on purpose, and outside the --mod1-vanillaheat early return below. It is the
            // one thing that should be true of EVERY launch including a control arm: a screenshot
            // or a recorded run that does not say which SimDLL and which framework produced it is
            // evidence of nothing, and a control arm mislabelled as the experiment is worse than
            // no label at all. Cosmetic and self-contained -- OniFramework.BuildStamp swallows its
            // own failures rather than letting a label take the mod down on load.
            OniFramework.BuildStamp.Install(harmony);

            // THE CRASH SCREEN A RIG DESERVES, installed here for the same reason as the
            // watermark above and immediately after it: it must be true of EVERY launch,
            // including a control arm, because the launch that crashes is never the one you
            // prepared for. Inert unless a rig is armed -- the postfix decides at dialog time,
            // when the answer is knowable -- so a player's crash screen is untouched.
            OniFramework.RigCrashScreen.Install(harmony);

            // THE PHASE RULES, IN THE SIM BEFORE ANY LOAD. The OnSpawn push (Patches.cs) arrives
            // after SaveLoader has loaded the sim, and the SimDLL's Load runs Klei's load-time
            // state transition gated by these rules: without this, every cell they hold (CO2 under
            // its condensation floor, liquid kept from boiling by pressure) takes one
            // ungated transition on every load. Pushed at every registration window, because
            // SIM_Initialize discards them. Outside the --mod1-vanillaheat early return like the
            // two above: the control arm keeps the OnSpawn push, so it keeps this one too, or
            // the two arms would load different worlds.
            //
            // --mod1-no-opening-push is the one exception, and it exists to be the control arm
            // for exactly this line: worldgen's settle frames and every Load then run with no
            // phase rules in the sim.
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(),
                "--mod1-no-opening-push") >= 0)
            {
                Debug.Log("[Mod1ThermoFluid] --mod1-no-opening-push: the phase rules are NOT "
                    + "pushed at the registration window; worldgen settles and Load run without them.");
            }
            else
            {
                OniFramework.MaterialPropertyRegistry.PushToSimAtEveryOpening(harmony);
            }

            // THE OVERLAY PROPERTY-TEXTURE CACHE, installed inert. Klei recomputes every visible
            // cell of four overlays every frame: dig amount and fog of war, whose output is
            // byte-identical on 99.4 % of frames for 0.285 ms of work, and the two mass passes,
            // 86.4 % for a further 0.426 ms. The patches go in here because this mod is already
            // the one on that path, but OverlayTextureCache.Enabled stays FALSE: it changes how
            // the base game renders, and nothing turns it on until the verification mode has read
            // zero false cleans on a live run. /overlaycache on the inspector port is the
            // switch.
            OniFramework.OverlayTextureCache.Install(harmony);

            // STATIONEERS-STYLE BUILDING PAINT, ON EVERY BUILDING. This attaches
            // OniFramework.PaintableBuilding to every building registered from here on --
            // vanilla, DLC, ours, and any other mod's -- and adds the swatch picker to the
            // details panel. It must run in OnLoad, before GeneratedBuildings registers
            // vanilla's buildings; anything registered earlier is simply not paintable.
            //
            // Buildings that draw through KAnimGridTileVisualizer are excluded: tiles, wires,
            // all three conduit families, logic wire, travel tubes and ladders, plus the bridges
            // and connectors that sit in those runs. See BuildingPaintAll for the rule.
            //
            // Nothing here changes how a building looks by itself -- an unpainted building's
            // swatch is White, which is the identity for a multiply.
            OniFramework.BuildingPaintAll.Install(harmony);

            // --mod1-vanillaheat is not a scenario, it is which ARM of the A/B this launch is:
            // both arms run the same rig, so a label showing only the rig would render a control
            // run and its experiment identically. Declared here rather than known to the
            // framework, which ships no gameplay and no opinion about this mod's flags.
            OniFramework.BuildStamp.MarkControlFlag("--mod1-vanillaheat", "VANILLAHEAT");

            // The SAVE FILE MIGRATION prompt, handled here instead of shown. It is vanilla's, and
            // it is triggered by loose saves in the save root. The prompt is not merely cosmetic:
            // it activates LoadScreen, which spends about 12 s in WorldGen.LoadSettings before an
            // automated run can hand off to AutoResumeSaveFile.
            //
            // Install() is a no-op unless this is a DEVELOPMENT install (a DevData directory
            // beside OxygenNotIncluded_Data), so a player's own
            // migration prompt is never touched -- stranding real saves in an old layout would
            // be far worse than a dialog. Outside the --mod1-vanillaheat return below for the
            // same reason as the watermark: it must be true of every launch, control arm
            // included, or the two arms differ in how they boot.
            OniFramework.SaveMigrationHandler.Install(harmony);

            // Registers the serialized write-through for the framework's conduit-matter store.
            // Deliberately OUTSIDE the --mod1-vanillaheat early return below: that flag controls
            // the HEAT RULES being measured by the A/B, and dropping the store's persistence hook
            // with them would mean a control run silently failed to save standing condensate --
            // a difference in save behaviour, not in heat behaviour, and one that would corrupt
            // the comparison rather than clean it up. See PipeMatterFacade.MirrorHook.
            PipeMatterState.Install();

            // THE SIM WORKS ON THIS MOD'S PIPE RUNS ITSELF. The policy says what the sim
            // does to every run this mod manages -- the runs GasMixtureFacade.EqualizePipeNetwork /
            // EqualizeLiquidNetwork take over, re-applied on each of those calls so it follows the
            // run through every rebuild:
            //
            //   Mix   -- heat exchange between neighbouring conduits after each 200 ms update, at
            //            ManagedRunMixFraction's default of 0.25, the sim's cap. Neighbour to
            //            neighbour rather than one temperature per run, so a run that crosses both
            //            rooms of a heat pump keeps its gradient between equalises. The equalise
            //            itself still levels the run's temperature whenever it shares the mass out
            //            -- that is the managed flow model, and flow stays managed.
            //
            // MIX | PHASE: the sim decides phase change in this mod's pipe runs as well as mixing
            // them, and SimConduitNetworks.Install carries those decisions out managed-side.
            //
            // The sim holds pipes on the vapour curve every 200 ms, so PipeMatterState defers to
            // the sim's own EvaporateTrapped proposals rather than returning condensate on its own
            // cadence and fighting it.
            //
            // Where the sim decides, PipeMatterFacade.TickNetworkPhaseChange stands down
            // (IsNativePhaseInForce answers true); on a run the sim does not cover it keeps
            // deciding, so the managed path is still the fallback, not dead code.
            //
            // Safe on a SimDLL without these exports (stock, or an older custom build):
            // the first SetPolicy logs one warning and every later one is refused, and this mod
            // behaves exactly as it did before.
            //
            // Outside the --mod1-vanillaheat return for the same reason as the store above: this
            // changes what the pipes do, not the heat-deletion rules that A/B measures, and the
            // control arm has to run the same pipes.
            //
            // CONVECTION: each pipe's exchange with its own building, Klei's conductivity mean,
            // scaled by the run's and the cell's Stationeers heat-exchange ratio. A pipe in vacuum
            // stops exchanging and a low-pressure gas line exchanges in proportion to its
            // pressure. An older SimDLL refuses the one bit and the framework pushes Mix | Phase
            // without it.
            OniFramework.GasMixtureFacade.ManagedRunPolicy =
                OniFramework.ConduitNetworkPolicy.Mix | OniFramework.ConduitNetworkPolicy.Phase
                | OniFramework.ConduitNetworkPolicy.Convection;
            OniFramework.SimConduitNetworks.Install(harmony);

            // The blank-canvas world generator (OniFramework.BlueprintWorld). Installing it only
            // patches SaveLoader.LoadFromWorldGen; that prefix returns straight to vanilla unless a
            // rig has armed a blueprint, so this changes nothing about an ordinary load. Outside
            // the --mod1-vanillaheat return below because it is scenario construction, not a heat
            // rule -- a control arm still needs to be able to build the same scenario.
            BlueprintWorld.Install(harmony);

            // The Condensation Valve and Purge Valve: display strings and build-menu entries.
            // The BuildingDefs themselves need no registration -- GeneratedBuildings scans every
            // loaded assembly for IBuildingConfig -- but the strings table and the plan screen are
            // not automatic. Outside the --mod1-vanillaheat return for the same reason the store's
            // persistence hook is: a control arm that could not place the same buildings would not
            // be the same colony.
            PhaseChangeValves.Install();
            PipeRadiators.Install();
            PhaseChambers.Install();
            AirHandling.Install();

            // The Dissolved Gas Sensor: the instrument that makes dissolved gas readable from
            // inside the game. Same treatment as the valves above -- the
            // BuildingDef registers itself, the strings and the plan screen do not.
            DissolvedGasSensorStrings.Install();

            // ...and the one patch that lets a VANILLA Soda Fountain serve carbonated water:
            // vanilla's readiness test can only see a loose chunk of carbon dioxide, and
            // dissolved gas is not a chunk. Hand-patched rather than attributed because it
            // targets a private method of a nested state-machine class, which
            // `[HarmonyPatch]` cannot name.
            SodaFountainStates_IsReady_Dissolved.Install(harmony);

            // The framework owns the one prefix on HoverTextDrawer.EndDrawing, so several mods can
            // share the hover card; this registers Mod 1's gas/liquid-tank and pipe/
            // gas-mixture readout as one section on it. See HoverPatch.cs.
            OniFramework.HoverCard.Install(harmony);
            HoverTextDrawer_EndDrawing.Install();

            // THE TWO MIXTURE OVERLAYS' own hover section -- the exact per-species breakdown of
            // the cell under the cursor, drawn only while one of those overlays is open. The
            // overlays themselves need no install: their four registration points are ordinary
            // attributed Harmony patches (MixtureOverlayModes.cs) and PatchAll has already found
            // them by the time this runs.
            MixtureOverlays.InstallHoverSection();

            // CONTROL ARM FOR A HEAT A/B (--mod1-vanillaheat). Skips all
            // three installs below, leaving vanilla's own heat rules in sole possession, so the
            // same save can be measured with and without this mod's changes and the two censuses
            // subtracted. There is no other way to get a vanilla reading: the rules are installed
            // from here unconditionally, and PowerHeat.GlobalRatio = 0 would silence only one of
            // the three.
            //
            // A flag rather than a config setting on purpose -- it exists to make one
            // measurement honest, not to offer players a half-installed mod, and a launch
            // argument cannot be left switched on by accident across sessions.
            if (RigRegistry.AnyFlag("--mod1-vanillaheat"))
            {
                Debug.Log("[Mod1ThermoFluid] --mod1-vanillaheat: skipping PowerHeat, ExhaustHeat "
                    + "and Radiation installs. This process is the CONTROL arm -- vanilla heat "
                    + "rules only, including every energy-deletion path they carry.");
                return;
            }

            // The rounding of a heat payment. The game writes every ModifyCellEnergy as one float
            // temperature step, so a payment small against the cell's heat capacity lands a whole
            // number of float steps: 0.1 kJ into 1000 kg of water at 283 K lands 27% too much,
            // 5 kJ lands 0.5% too little, the same way every time. This mod pays heat into cells
            // that way constantly (bubbles, dissolving gas, the effervescence surface). With the
            // simulation's CellEnergyCarry row on, each cell keeps what did not land and adds it to
            // its next payment. The row is not saved, so it is re-sent at every opening; inside
            // the --mod1-vanillaheat return because it is one of this mod's heat rules.
            OniFramework.ExtOwner carryOwner = OniFramework.SimExtRegistry.ClaimOwner("Mod1ThermoFluid");
            OniFramework.SimExtRegistrar.Install(harmony);
            OniFramework.SimExtRegistrar.Opening += () =>
                OniFramework.SimTunables.Set(OniFramework.SimTunable.CellEnergyCarry, 1, carryOwner);

            // The power -> heat rule: energy a building draws becomes heat in its own structure
            // temperature, and none is deleted. Lives in the framework so a
            // third-party mod can reach it, and is opt-in rather than applied on load, because
            // OniFramework must never patch anything merely by being installed -- so this
            // explicit call is the only way it turns on. See OniFramework.PowerHeat.
            OniFramework.PowerHeat.Install(harmony);

            // The other half of the same requirement. Klei's ExhaustHeat destroys energy on
            // four paths -- the 1.5 kg mass factor, vacuum, cells under 0.001 kg, and the
            // building's own overheat temperature as a delivery ceiling (348.15 K by default,
            // so an ordinary machine in a hot room delivers nothing at all). This moves the
            // delivery into the sim and puts the undeliverable remainder in the building's own
            // body. See OniFramework.ExhaustHeat.
            OniFramework.ExhaustHeat.Install(harmony);

            // ...and the outlet, without which the two rules above would simply cook every
            // machine standing in a vacuum. The same structure as Stationeers' radiative loss,
            // with one deliberate departure: the medium scales the FLUX toward zero rather than
            // retargeting the sink, so a building sealed in a full cell radiates exactly nothing
            // and conduction keeps sole ownership of that exchange. Following Stationeers
            // literally there costs a real colony about 1.7 MW of continuous cooling. The
            // environment reservoir has a fixed temperature. See OniFramework.Radiation.
            OniFramework.Radiation.Install(harmony, OniFramework.Radiation.DefaultEmissivity,
                OniFramework.Radiation.DefaultEnvironmentKelvin);

            // CONVECTION, the other half of heat rejection, and what the pipe radiators are
            // (PipeRadiators.cs). Klei's building <-> cell conduction is driven by the CELL's
            // material conductivity, which for a gas is about 0.2 W/K against Stationeers' 105
            // for the same radiator, so a radiator that had to reach the room through it alone
            // would do nothing in air. This installs the sweep that hands every registered
            // radiator prefab its Stationeers factors; a building without a profile is untouched,
            // and Radiation above keeps giving everything else the global emissivity of 1.
            OniFramework.Convection.Install(harmony);

            // BUBBLE PHYSICS. Every vanilla bubble
            // -- a breath under water, a converter's output, a boiling Tepidizer -- rises at a
            // speed set by its real size instead of a flat tile a second, grows as the water
            // above it thins, and trades heat with the liquid it climbs through, paid out of
            // that liquid's cells so none is created. Inside the --mod1-vanillaheat return
            // because the heat exchange is a heat rule: the control arm keeps vanilla bubbles.
            // See OniFramework.Bubbles.
            OniFramework.Bubbles.Install(harmony);

            // WHAT THE BUBBLES ARE REALLY MADE OF, for the mass exchange only.
            // `BubbleManager` draws at most sixteen spheres per release, so a vent's 100 g is
            // drawn as sixteen 12 cm bubbles. Gas pushed through water does not come in 12 cm
            // spheres -- an exhaled breath breaks up, and a sparger exists precisely to disperse
            // gas into millimetre bubbles -- and since interfacial area goes as 6m/(rho*d), the
            // difference between 12 cm and 3 mm is a factor of forty in how fast anything
            // dissolves. On the geometric diameter a three-vent plume carries about 0.45 m2 of
            // interface, which is a week of simulated time to carbonate one tank.
            //
            // 3 mm is a coarse-pore sparger, the conservative end of the range (fine-pore plates
            // run under 1 mm). Rise speed, growth, heat exchange and what surfaces are all still
            // the drawn sphere's; only the transfer area is the dispersion's.
            OniFramework.Bubbles.InterfacialDiameterM = 0.003f;

            // DISSOLVED GAS. The sim
            // carries gas dissolved in a liquid cell with the liquid; when the liquid leaves the
            // grid without a consumer -- it boils, freezes, is replaced or removed -- its gas
            // comes back as bubbles (or as cell gas where there is no liquid left), so none is
            // deleted. Right after Bubbles because that is where it goes. Inert on a SimDLL
            // without the property, and a no-op until something dissolves.
            OniFramework.DissolvedGas.Install(harmony);

            // DISSOLVED CARGO. DissolvedGas ends
            // at the edge of the grid: past it, ONI's carriers hold one element and one mass and
            // have nowhere to put a dissolved payload, so without this a pump is where carbonated
            // water stops being carbonated. DissolvedCargo carries the record beside the mass
            // through the conduit mover, the pipe endpoints, the two storage crossings and an
            // item's own split and merge -- and gives it back to the world as gas at any exit it
            // does not model, so no path deletes it. Installed AFTER DissolvedGas because it
            // subscribes to that class's consumed stream.
            OniFramework.DissolvedCargo.Install(harmony);

            // Klei invokes NotificationManager.notificationRemoved without a null check, while
            // guarding notificationAdded two methods away, so removing any status item that
            // carries a notification throws whenever the notification UI has not spawned.
            // PipeStressMonitor removes those markers once a second, and without this every one
            // of those passes would die on it. See OniFramework.NotificationSafety
            // for why the fix is a no-op subscriber rather than a patch on Klei's method body.
            OniFramework.NotificationSafety.Install(harmony);

            // Vanilla's `pipesHaveRoom` is a Requirement flag whose test is
            // `IsConduitEmpty(utilityCell)` -- mass <= 0 in the ONE tile at the building's output
            // port. That is correct for vanilla, whose consumers drain a whole segment every
            // frame, and unsatisfiable here, because this mod's networks equalise toward a shared
            // fill fraction and a tile converges to a nonzero share it never leaves. Every
            // producer feeding one of these networks would otherwise be held non-operational for
            // good. Redefined to the question ONI's own flow solver asks before accepting mass --
            // is there capacity left -- which keeps the half of the gate that is real backpressure
            // instead of switching it off per building. See OniFramework.ConduitBackpressure.
            OniFramework.ConduitBackpressure.Install(harmony);

            // The third energy-deletion path, and the one that is not about heat leaving a
            // building at all. Klei's ElementConverter.ConvertMass sets every output to
            // max(minOutputTemperature, the inputs' mass*cp-weighted average temperature) --
            // a temperature, chosen without reference to how much heat capacity the outputs
            // actually have. So an Electrolyzer taking 1 kg of 300 K water emits 0.888 kg of
            // oxygen and 0.112 kg of hydrogen at 343.15 K, and the energy in the outputs is
            // whatever that happens to come to. Feed it hotter water and the surplus is
            // deleted; feed it a recipe with a high floor and energy is created.
            //
            // The fix is NOT to conserve the inputs' enthalpy outright, which is the obvious
            // move and is wrong twice over: these are chemical conversions, so the reaction's
            // own enthalpy is a real term vanilla has no representation for, and the recipes
            // do not conserve mass either (the Oil Well Cap turns 1 kg of water into 3.333 kg
            // of crude oil). Doing it naively puts the Electrolyzer's output at 1079.6 K.
            //
            // Instead each chemical recipe carries a calibrated reaction enthalpy, defined so
            // that at its calibration temperature it reproduces vanilla's number exactly. That
            // is what lets this ship without a balance argument: a converter running near
            // 300 K is untouched, and the deletion and the creation are removed away from it.
            //
            // Recipes that are pure physical separation -- mass conserves and every active
            // floor is zero, so there is no chemistry to have an enthalpy -- get true enthalpy
            // conservation with a zero reaction term instead. Those DO move at 300 K, and are
            // meant to: the Desalinator emits 5 kg of water and salt whose heat capacity is
            // 4% below the brine's, so vanilla's "same temperature out" was deleting that 4%
            // every conversion. It now lands at 312.54 K.
            //
            // See OniFramework.ConversionEnthalpy for the derivation and the Stationeers
            // provenance; ConversionEnthalpy.TemperatureGate is off by default and is its own
            // A/B, not part of this one.
            OniFramework.ConversionEnthalpy.Install(harmony);

            // THE GATE ARM OF THAT A/B. Stationeers does not heat a product up to a floor: its
            // recipes declare a temperature window the reagents must already be inside, and a
            // machine outside the window does not run. Read against ONI, "the Polymerizer emits
            // steam at 473.15 K" ought to be "the Polymerizer needs 473.15 K to run" -- which
            // removes minOutputTemperature as an energy source at the root, rather than
            // compensating for it afterwards the way the rule above does.
            //
            // It changes when roughly twenty buildings work at all, so it is a launch argument
            // and not a config setting, for the same reason --mod1-vanillaheat is: it exists to
            // make one measurement, and an argument cannot be left switched on by accident
            // across sessions.
            if (RigRegistry.AnyFlag("--mod1-conversion-gate"))
            {
                OniFramework.ConversionEnthalpy.TemperatureGate = true;
                Debug.Log("[Mod1ThermoFluid] --mod1-conversion-gate: ConversionEnthalpy's "
                    + "temperature gate is ON. Recipes whose inputs are colder than their "
                    + "highest output floor will refuse to run -- no inputs consumed, no outputs "
                    + "produced. This is the experimental arm.");
            }

            // The single largest act of energy deletion in the game, and the last of the four.
            // Klei's Steam Turbine removes the heat needed to bring its steam down to a fixed
            // 368.15 K -- 877.6 kW at the design point -- hands a tenth of it to the building
            // body, and deletes the other 789.8 kW. It converts 0.097% of that heat into
            // electricity, against a Carnot ceiling of 22.2%, so the building players use as
            // their primary cooling tool is a heat destroyer with a generator bolted on.
            //
            // Electricity and the output water temperature are both left exactly as Klei wrote
            // them, so neither the turbine's power nor its cooling capacity changes. What
            // changes is that the heat it removes now has to exist afterwards: the body gets
            // Q_hot - W, the same place vanilla was already putting a tenth of it. Modelled on
            // StirlingEngine (Stationeers), whose cold side exchanges with the world rather than
            // with its own output.
            //
            // It needs no new mechanism. SteamTurbine already refuses to run above a 373.15 K
            // body temperature; vanilla's 87.8 kW rarely makes that gate bind and 789.8 kW binds
            // it at once, so the turbine self-throttles until the player carries the heat away.
            // The gameplay was built into the building already and was only slack because nine
            // tenths of the heat was thrown away before it could reach the gate. See
            // OniFramework.TurbineHeat.
            OniFramework.TurbineHeat.Install(harmony);

            // The same conversion-enthalpy rule, applied to the OTHER recipe engine. ONI has
            // two, and they share nothing: ElementConverter is behind the 29 buildings above,
            // EnergyGenerator is behind the fuel generators, and neither class references the
            // other. So the rule installed further up reaches none of the generators.
            //
            // EnergyGenerator.Emit writes its products at
            // Mathf.Max(root_pe.Temperature, output.minTemperature) -- the building's body
            // temperature, floored by a constant -- while the fuel's own sensible heat is
            // discarded at ConsumeIgnoringDisease. Cold methane in, 383.15 K carbon dioxide out,
            // for free.
            //
            // How big, from Klei's own rates at a 300 K calibration point: the Natural Gas
            // Generator emits 95.61 kW and its fuel brings 59.16 kW, so 36.45 kW is created
            // from nothing every second. That figure is the heat of combustion, and it is
            // already in the game -- which is the measured reason these buildings are given no
            // fuel-enthalpy term of their own. The calibrated reaction enthalpy IS the
            // combustion energy, derived from Klei's emission rates rather than conjured from a
            // table; adding a second one would count it twice.
            //
            // Vanilla's hand-tuned waste heat is left exactly as Klei wrote it, and so is the
            // electricity. Implied efficiency W/(W+heat) is already 7.4% for natural gas and
            // 9.1% for petroleum, against the 17% cut PowerGeneratorPipe (Stationeers) takes --
            // so a fuel-energy model would LOWER these buildings' heat output, not raise it.
            // PowerHeat already gives that heat a real destination; what was missing was only that
            // the products carry what the fuel brought. See OniFramework.GeneratorEnthalpy.
            OniFramework.GeneratorEnthalpy.Install(harmony);

            // The last of the four heat-deletion channels, and the smallest.
            // A Wheezewort inhales gas, exhales it five degrees colder, and the energy that
            // removal took is deleted with no accounting of any kind -- the plant's own
            // temperature is never touched. This deposits that heat into the plant's body (the
            // SimTemperatureTransfer element chunk every plant already registers), from which it
            // conducts into whatever the plant is standing in. Same "reject into the body, let
            // the body conduct" model as TurbineHeat; the plant's own
            // 368.15 K wilt threshold then becomes a self-throttle, so a Wheezewort in a space
            // it cannot reject heat from cooks itself and one whose body is actively cooled
            // keeps working. No custom SimDLL needed -- the deposit is a stock vanilla sim
            // message. See OniFramework.ColdBreatherHeat.
            OniFramework.ColdBreatherHeat.Install(harmony);
        }
    }
}
