using System;
using HarmonyLib;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// A debug key for the gas mixture. Press F9 over any cell:
    ///   - if the cell has no gas-mixture state yet, convert up to 1 kg of whatever vanilla
    ///     gas is actually there (via GasMixtureFacade.ConvertFromVanilla, same conserving
    ///     transfer GasMixtureInjectorComponent in Injector.cs uses), which also activates
    ///     volume-fractions tracking for the whole world (see ActivateVolumeFractions in
    ///     the SimDLL's sim/simdll.cpp);
    ///   - read the dominant species back through the facade and log it.
    /// </summary>
    /// <summary>
    /// Spawns GasMixtureOverlay once when the game object graph comes up -- the standard
    /// "own GameObject, not a vanilla one" pattern, so the overlay never depends on a specific
    /// Klei UI object surviving a scene change.
    /// </summary>
    [HarmonyPatch(typeof(Game), "OnSpawn")]
    internal static class Game_OnSpawn
    {
        private static void Postfix()
        {
            // Hand the SimDLL the registry's own molecular masses before anything reads a
            // pressure (MaterialPropertyRegistry.PushMolecularMassesToSim). Klei
            // stores ATOMIC mass for the diatomic gases, so every diatomic pressure this project
            // reported was exactly 2x high until this landed. The native side seeds the same four
            // corrections itself and is correct without this call -- what the call buys is that
            // the MANAGED registry is the authority, so a third-party mod registering a new gas
            // gets a correct native pressure for it too. First thing in OnSpawn deliberately:
            // message effects are deferred one tick, and everything below this line can read a
            // pressure.
            OniFramework.MaterialPropertyRegistry.PushMolecularMassesToSim();

            // A gas add with nowhere to go mixes instead of deleting
            // (ext::kSetBlockedGasAddPolicy). Vanilla's AddIntoBlockedCell deletes the smaller of
            // the incoming gas and the pocket in its way -- a bubble pop or an exhaled breath onto
            // a foreign-gas pocket with no vacuum or same-gas neighbour. Allowing gases to mix is
            // this mod's answer to that one-cell-one-element loss, so
            // the blocked cell's room is promoted and its gas moved into the mixture. The policy
            // lives on the sim, is not saved, and is sent on every game load.
            OniFramework.GasMixtureFacade.SetBlockedGasAddPolicy(
                OniFramework.GasMixtureFacade.BlockedGasAddPolicy.PromoteAndMix);

            // WATER THAT LOOKS LIKE IT IS CARRYING SOMETHING. The liquid property texture blends a cell's rendered colour towards the colour of
            // whatever is dissolved in it, so a carbonated pond stops rendering byte-identically
            // to a plain one. Sent here rather than at OnLoad because it reads element colours
            // and molar masses out of the live element table, and because the lane assignment it
            // describes is remade every session -- the same reason the two messages above are
            // sent here.
            //
            // Pushed at the framework's own defaults (10 g/kg full scale, 0.45 blend ceiling),
            // which are the numbers the carbonation chain actually operates at. A saturated pond
            // is unmistakably not plain water; it is still unmistakably water.
            OniFramework.LiquidTint.Push();

            // SUPERSATURATED WATER FIZZES. A cell
            // holding more dissolved gas than the pressure on it can keep in solution gives the
            // excess back as bubbles: a carbonated pond whose headspace is vented, or that warms,
            // or that a pump lifts out of a deep tank, visibly bubbles instead of staying
            // supersaturated for ever. Sent here for the tint's reason: it reads the live element
            // table (molar masses, solvent densities) and the lane assignment, both remade every
            // session. Framework defaults: fizz at 10% over saturation, half the excess in ~35 s.
            OniFramework.Effervescence.Push();

            // oni-mod-compat decision 0003: the Stock Bug Fix mutual-detection warning has to
            // run from here, not from GeneratorEnthalpy.Install (called at THIS mod's own
            // OnLoad) -- see WarnIfStockBugFixPresent's own doc comment for the live boot that
            // found why. By Game.OnSpawn every mod's OnLoad has run, regardless of mods.json
            // order, so this is the first point that can answer "is Stock Bug Fix here" for real.
            if (OniFramework.GeneratorEnthalpy.IsInstalled)
            {
                OniFramework.GeneratorEnthalpy.WarnIfStockBugFixPresent();
            }

            var overlayObject = new GameObject("Mod1ThermoFluidOverlay");
            overlayObject.AddComponent<GasMixtureOverlay>();
            // Stationeers-style pipe-stress warnings (PipeStressMonitor.cs). Same
            // GameObject and lifetime as the overlays above: it holds no per-save state, scans
            // once a second, and does nothing at all until Mod 1 physics has actually touched a
            // pipe, so bundling it here costs nothing on a save that never builds one.
            overlayObject.AddComponent<PipeStressMonitor>();

            // The framework's live-inspection debug server (OniFramework.DebugInspectorServer):
            // HTTP on port 9788, queryable with a plain curl call while the game runs. It has no
            // authentication and listens on every interface, so it starts only when the launch
            // asks for it with --oni-debug-inspector.
            if (RigRegistry.AnyFlag("--oni-debug-inspector"))
            {
                OniFramework.DebugInspectorServer.Start(9788);
                overlayObject.AddComponent<OniFramework.DebugInspectorPump>();
            }

            UnityEngine.Object.DontDestroyOnLoad(overlayObject);
        }
    }

    /// <summary>
    /// Augments the vanilla Gas Element Sensor building (LogicElementSensorGasConfig) with
    /// GasMixtureInjectorComponent (see Injector.cs) instead of adding a new buildable --
    /// no new BuildingDef, no new kanim, no tech-tree entry. Every sensor a player places
    /// starts feeding its own cell's vanilla atmosphere into the gas-mixture layer.
    /// </summary>
    [HarmonyPatch(typeof(LogicElementSensorGasConfig), "DoPostConfigureComplete")]
    internal static class LogicElementSensorGasConfig_DoPostConfigureComplete
    {
        private static void Postfix(GameObject go)
        {
            if (go.GetComponent<GasMixtureInjectorComponent>() == null)
            {
                go.AddComponent<GasMixtureInjectorComponent>();
            }
        }
    }

    /// <summary>
    /// Augments the vanilla Gas Vent building (GasVentConfig) with
    /// GasMixtureExtractorComponent (see Extractor.cs) -- the reverse of
    /// LogicElementSensorGasConfig_DoPostConfigureComplete below. Every placed vent starts
    /// releasing gas-mixture mass back into its own cell's vanilla atmosphere, closing the
    /// loop the sensor opened.
    /// </summary>
    [HarmonyPatch(typeof(GasVentConfig), "DoPostConfigureComplete")]
    internal static class GasVentConfig_DoPostConfigureComplete
    {
        private static void Postfix(GameObject go)
        {
            if (go.GetComponent<GasMixtureExtractorComponent>() == null)
            {
                go.AddComponent<GasMixtureExtractorComponent>();
            }
            // The flooded-vent readout (SubmergedGasVent.cs). The gate and the bubbles are
            // patches on Vent and Exhaust themselves; this is only the status line.
            go.AddOrGet<SubmergedGasVentReadout>();
        }
    }

    /// <summary>
    /// Augments the vanilla Gas Pump building (GasPumpConfig) with
    /// GasMixtureCompressorIntakeComponent (see Compressor.cs) -- the source side of Mod 1's
    /// first real gameplay mechanic (not a debug key): every placed pump starts pulling its own
    /// cell's real vanilla atmosphere and dispensing it into a real connected gas pipe.
    ///
    /// FOUND LIVE (user caught it): a real Gas Pump already ships its OWN working
    /// `ElementConsumer` (0.5 kg/s from its own cell into a 1 kg vanilla Storage) and
    /// `ConduitDispenser` (`alwaysDispense=true`, in `GasPumpConfig.
    /// DoPostConfigureComplete`) -- meaning vanilla's Gas Pump already moves real gas into a
    /// real pipe with zero mod code. Left running alongside our own intake, both would be
    /// independently pulling from the same source cell and pushing into the same output cell --
    /// not a conservation bug (each only ever removes real mass it actually has), but pointless
    /// double bookkeeping our own mechanic doesn't need. Disabled here (`ElementConsumer.
    /// EnableConsumption(false)`, `ConduitDispenser.SetOnState(false)` -- vanilla's own public
    /// on/off switches, not a component removal) so our intake is the only thing moving gas.
    ///
    /// WITHOUT THE STORAGE FIX BELOW the pump silently stops moving anything even with full power and abundant real material
    /// right next to it. Root cause, in `Pump.UpdateOperational()`: it sets
    /// an `Operational.Flag.Type.Requirement` flag to `!storage.IsFull() && IsPumpable(...)` --
    /// vanilla's own internal `Storage` (a 20kg cap, unrelated to this mod's own tank) can pick
    /// up one real item during the brief window before/around this postfix's own disable calls
    /// take effect (`ElementConsumer.OnActiveChanged` re-calls `EnableConsumption` on every
    /// `Operational.IsActive` transition, so the disable above isn't perfectly sticky). With
    /// `ConduitDispenser` also disabled, nothing ever drains that Storage back out once
    /// something lands in it -- once full, this Requirement flag goes permanently false,
    /// `Operational.IsOperational` goes permanently false, and this mod's OWN intake component
    /// (which gates on `operational.IsOperational`, matching a real Gas Pump's own power/
    /// logic-enable gating) refuses to run forever, real local material or not. Fixed by
    /// setting vanilla's own `Storage.capacityKg` to effectively infinite so it can never report
    /// full -- the Requirement flag then only ever reflects `IsPumpable`'s own live radius scan,
    /// which is the real, correct "is there material nearby" signal this mod actually wants.
    /// </summary>
    /// <summary>
    /// oni-mod-compat decision 0004: Priority.Low so this always runs LAST among
    /// DoPostConfigureComplete postfixes on this building. The `Storage.capacityKg = infinity`
    /// line above is not a tuning value this mod can afford to lose -- it is the fix for the
    /// Pump.UpdateOperational() stuck-Requirement-flag bug described above, and a capacity mod
    /// (Customize Buildings, confirmed by field-level IL diff to write the same Storage.
    /// capacityKg) setting a finite value after this patch runs would silently reopen that bug.
    /// Running last means this always has the final say on that one field, regardless of what
    /// value a capacity-rebalance mod wants it to have -- which costs that mod nothing real,
    /// since vanilla's own Storage on this building no longer does live work once this mod's
    /// compressor-intake mechanic is attached (ElementConsumer/ConduitDispenser both disabled
    /// above).
    /// </summary>
    [HarmonyPatch(typeof(GasPumpConfig), "DoPostConfigureComplete")]
    [HarmonyPriority(Priority.Low)]
    internal static class GasPumpConfig_DoPostConfigureComplete
    {
        private static void Postfix(GameObject go)
        {
            if (go.GetComponent<GasMixtureCompressorIntakeComponent>() == null)
            {
                go.AddComponent<GasMixtureCompressorIntakeComponent>();
            }

            ElementConsumer elementConsumer = go.GetComponent<ElementConsumer>();
            if (elementConsumer != null)
            {
                elementConsumer.EnableConsumption(false);
            }

            ConduitDispenser conduitDispenser = go.GetComponent<ConduitDispenser>();
            if (conduitDispenser != null)
            {
                conduitDispenser.SetOnState(false);
            }

            Storage storage = go.GetComponent<Storage>();
            if (storage != null)
            {
                storage.capacityKg = float.PositiveInfinity;
            }

            // THE "Pipe Blocked" STATUS is not Pump.OnConduitUpdate's ConduitBlocked (that stays
            // False) but `ConduitBlockedMultiples` ("Output Pipe is blocked"), owned by
            // `RequireOutputs` -- added by `BuildingLoader`'s `UpdateComponentRequirement<RequireOutputs>(go,
            // def.OutputConduitType != ConduitType.None)` -- added generically to ANY building
            // with a real output conduit type, completely separate from GasPumpConfig/
            // LiquidPumpConfig's own code, which is why it was never found by grepping either
            // config). `RequireOutputs.UpdatePipeRoomState()` checks
            // `GetConduitFlow().IsConduitEmpty(utilityCell)` -- NOT a mass-cap check at all --
            // and flags "blocked" whenever the output pipe holds ANY real mass. That's the
            // correct vanilla behavior for a discrete-batch producer (a pipe that should clear
            // between outputs), but wrong for this mod's own continuous-flow mechanic, which
            // deliberately keeps the pipe non-empty at all times.
            //
            // AND THIS ONE KEEPS VANILLA'S ESCAPE HATCH, where the Water Sieve and the Steam
            // Turbine no longer need it. `OniFramework.ConduitBackpressure` redefines the gate
            // from "the port tile is empty" to "the port tile has room", which is enough for a
            // producer that fills a line. It is NOT enough for a pump whose whole job is to hold
            // a network AT capacity: a gas conduit's MaxMass is 1 kg, this mod's air loop runs
            // its ring saturated on purpose, and the tile at the port therefore has no room by
            // any honest reading of the word. Without this line the air loop's diffusers stop
            // dead.
            //
            // An open question: the right gate for a
            // saturated continuous-flow network is whether the NETWORK can accept, not whether
            // one tile can, and neither this hatch nor ConduitBackpressure asks that yet.
            RequireOutputs requireOutputs = go.GetComponent<RequireOutputs>();
            if (requireOutputs != null)
            {
                requireOutputs.ignoreFullPipe = true;
            }
        }
    }

    /// <summary>
    /// Augments the vanilla Gas Reservoir building (GasReservoirConfig) with
    /// GasMixtureTankComponent (see Compressor.cs) -- the sealed destination side of the same
    /// mechanic. Storage-themed building, so "this is where compressed gas accumulates" needs
    /// no new kanim or flavor text to make sense.
    ///
    /// FOUND LIVE (user caught it): pipe pressure showed real gas moving, but the
    /// tank never filled. Root cause, : a real Gas Reservoir already ships
    /// its OWN working `ConduitConsumer` (`GasReservoirConfig.ConfigureBuildingTemplate`),
    /// `consumptionRate` left at its class default of `float.PositiveInfinity` -- it drains the
    /// ENTIRE pipe segment into vanilla's own `Storage` every single frame (no once-per-second
    /// gate the way our tank has). Our tank's own once-per-second pull almost never won that
    /// race against an unthrottled every-frame consumer -- vanilla was taking the gas first,
    /// nearly every time. Disabled here (`ConduitConsumer.SetOnState(false)` -- vanilla's own
    /// public on/off switch, not a component removal) so our tank is the only thing pulling from
    /// the pipe. Known cosmetic side effect, named not hidden: vanilla's own Storage-based fill
    /// status item on this building will now permanently read empty, since nothing feeds it
    /// anymore -- this mod's own tooltip/overlay is the real readout now.
    /// </summary>
    [HarmonyPatch(typeof(GasReservoirConfig), "DoPostConfigureComplete")]
    internal static class GasReservoirConfig_DoPostConfigureComplete
    {
        private static void Postfix(GameObject go)
        {
            if (go.GetComponent<GasMixtureTankComponent>() == null)
            {
                go.AddComponent<GasMixtureTankComponent>();
            }

            ConduitConsumer conduitConsumer = go.GetComponent<ConduitConsumer>();
            if (conduitConsumer != null)
            {
                conduitConsumer.SetOnState(false);
            }
        }
    }

    /// <summary>
    /// Liquid counterpart to GasPumpConfig_DoPostConfigureComplete -- augments the vanilla
    /// Liquid Pump (LiquidPumpConfig) with LiquidMixtureCompressorIntakeComponent (see
    /// LiquidCompressor.cs). Same reasoning for disabling vanilla's own
    /// ElementConsumer/ConduitDispenser: a real Liquid Pump already ships both
    /// (LiquidPumpConfig.DoPostConfigureComplete: consumptionRate=10f,
    /// conduitDispenser.alwaysDispense=true) -- left running, both would independently move the
    /// same real liquid, pointless double bookkeeping this mod's own mechanic doesn't need.
    ///
    /// WITHOUT THE STORAGE FIX BELOW the pump silently stops moving anything even
    /// with abundant real material available. Root cause, in
    /// `Pump.UpdateOperational()` -- see `GasPumpConfig_DoPostConfigureComplete`'s own doc
    /// comment above for the full mechanism (identical `Pump` component, same bug, same fix):
    /// vanilla's own internal `Storage` (20kg cap) can pick up one real item before this
    /// postfix's disable calls fully take hold, nothing ever drains it back out once
    /// `ConduitDispenser` is off, and a full `Storage` makes `Pump.UpdateOperational()`'s
    /// Requirement flag permanently false -- which permanently blocks this mod's own intake
    /// (gated on `operational.IsOperational`) regardless of real local material. Fixed the same
    /// way: `Storage.capacityKg` bumped to infinite so it can never report full.
    /// </summary>
    /// <summary>
    /// oni-mod-compat decision 0004: Priority.Low, same reasoning as
    /// GasPumpConfig_DoPostConfigureComplete -- Storage.capacityKg=infinity here is the fix for
    /// the identical Pump.UpdateOperational() bug, and Customize Buildings writes the same field
    /// (confirmed by field-level IL diff). Running last keeps the fix in effect no matter what a
    /// capacity mod sets it to.
    /// </summary>
    [HarmonyPatch(typeof(LiquidPumpConfig), "DoPostConfigureComplete")]
    [HarmonyPriority(Priority.Low)]
    internal static class LiquidPumpConfig_DoPostConfigureComplete
    {
        private static void Postfix(GameObject go)
        {
            if (go.GetComponent<LiquidMixtureCompressorIntakeComponent>() == null)
            {
                go.AddComponent<LiquidMixtureCompressorIntakeComponent>();
            }

            ElementConsumer elementConsumer = go.GetComponent<ElementConsumer>();
            if (elementConsumer != null)
            {
                elementConsumer.EnableConsumption(false);
            }

            ConduitDispenser conduitDispenser = go.GetComponent<ConduitDispenser>();
            if (conduitDispenser != null)
            {
                conduitDispenser.SetOnState(false);
            }

            Storage storage = go.GetComponent<Storage>();
            if (storage != null)
            {
                storage.capacityKg = float.PositiveInfinity;
            }

            // THE "Pipe Blocked" STATUS is not Pump.OnConduitUpdate's ConduitBlocked (that stays
            // False) but `ConduitBlockedMultiples` ("Output Pipe is blocked"), owned by
            // `RequireOutputs` -- added by `BuildingLoader`'s `UpdateComponentRequirement<RequireOutputs>(go,
            // def.OutputConduitType != ConduitType.None)` -- added generically to ANY building
            // with a real output conduit type, completely separate from GasPumpConfig/
            // LiquidPumpConfig's own code, which is why it was never found by grepping either
            // config). `RequireOutputs.UpdatePipeRoomState()` checks
            // `GetConduitFlow().IsConduitEmpty(utilityCell)` -- NOT a mass-cap check at all --
            // and flags "blocked" whenever the output pipe holds ANY real mass. That's the
            // correct vanilla behavior for a discrete-batch producer (a pipe that should clear
            // between outputs), but wrong for this mod's own continuous-flow mechanic, which
            // deliberately keeps the pipe non-empty at all times.
            //
            // AND THIS ONE KEEPS VANILLA'S ESCAPE HATCH, where the Water Sieve and the Steam
            // Turbine no longer need it. `OniFramework.ConduitBackpressure` redefines the gate
            // from "the port tile is empty" to "the port tile has room", which is enough for a
            // producer that fills a line. It is NOT enough for a pump whose whole job is to hold
            // a network AT capacity: a gas conduit's MaxMass is 1 kg, this mod's air loop runs
            // its ring saturated on purpose, and the tile at the port therefore has no room by
            // any honest reading of the word. Without this line the air loop's diffusers stop
            // dead.
            //
            // An open question: the right gate for a
            // saturated continuous-flow network is whether the NETWORK can accept, not whether
            // one tile can, and neither this hatch nor ConduitBackpressure asks that yet.
            RequireOutputs requireOutputs = go.GetComponent<RequireOutputs>();
            if (requireOutputs != null)
            {
                requireOutputs.ignoreFullPipe = true;
            }
        }
    }

    /// <summary>
    /// Liquid counterpart to GasReservoirConfig_DoPostConfigureComplete -- augments the vanilla
    /// Liquid Reservoir (LiquidReservoirConfig) with LiquidMixtureTankComponent (see
    /// LiquidCompressor.cs). Same reasoning for disabling vanilla's own
    /// ConduitConsumer: a real Liquid Reservoir already ships one
    /// (LiquidReservoirConfig.ConfigureBuildingTemplate: alwaysConsume=true,
    /// forceAlwaysSatisfied=true, no once-per-second gate) that would otherwise win the race
    /// against this mod's own once-per-second pull almost every time -- same bug the gas side
    /// hit and fixed the same way. Same known cosmetic side effect: vanilla's own Storage-based
    /// fill status item reads empty since nothing feeds it anymore.
    /// </summary>
    [HarmonyPatch(typeof(LiquidReservoirConfig), "DoPostConfigureComplete")]
    internal static class LiquidReservoirConfig_DoPostConfigureComplete
    {
        private static void Postfix(GameObject go)
        {
            if (go.GetComponent<LiquidMixtureTankComponent>() == null)
            {
                go.AddComponent<LiquidMixtureTankComponent>();
            }

            ConduitConsumer conduitConsumer = go.GetComponent<ConduitConsumer>();
            if (conduitConsumer != null)
            {
                conduitConsumer.SetOnState(false);
            }
        }
    }

    /// <summary>
    /// Gives the Petroleum Generator back the matter it destroys, as soot.
    ///
    /// THE DEFECT, from Klei's own config (build 744825). The formula takes 2 kg/s of
    /// combustible liquid and emits 0.5 kg/s of carbon dioxide and 0.75 kg/s of polluted
    /// water. 2.0 in, 1.25 out: **0.75 kg/s of matter simply ceases to exist.** The Natural
    /// Gas Generator, by contrast, balances exactly (0.09 -> 0.0675 + 0.0225), so this is not
    /// a property of the recipe engine -- it is this one building.
    ///
    /// The headline number understates it. Petroleum as the standard CH2 repeat unit
    /// (14.027 g/mol) against those same rates, per second:
    ///
    ///     IN    2.000 kg petroleum  = 142.58 mol CH2  ->  C 142.58 mol,  H 285.16 mol
    ///     OUT   0.500 kg CO2        =  11.36 mol      ->  C  11.36 mol,  O  22.72 mol
    ///           0.750 kg DirtyWater =  41.63 mol H2O  ->  H  83.26 mol,  O  41.63 mol
    ///
    ///     carbon    142.58 -> 11.36 mol   1.576 kg/s DESTROYED  (92.0% of the fuel's carbon)
    ///     hydrogen  285.16 -> 83.26 mol   0.204 kg/s DESTROYED  (70.8%)
    ///     oxygen      0    -> 64.35 mol   1.030 kg/s CREATED from nothing
    ///                                     ------
    ///                                     -0.750 kg/s net
    ///
    /// The net closes on the headline exactly, which is what makes the decomposition worth
    /// trusting: the building is not leaking 0.75 kg/s, it is deleting 1.78 kg/s of fuel atoms
    /// and conjuring 1.03 kg/s of oxygen, and those very nearly cancel.
    ///
    /// WHY THIS FIXES MASS AND NOT ATOMS, DELIBERATELY. With no oxidiser input there is no
    /// oxygen in the system, so every gram of O in the products is invented and no added
    /// output can un-invent it. Real stoichiometry closes perfectly --
    /// `CH2 + 1.5 O2 -> CO2 + H2O` means 2 kg/s of petroleum wants 6.843 kg/s of oxygen and
    /// makes 6.274 kg/s of CO2 -- and 12.5x this building's carbon dioxide against an intake
    /// larger than a colony's entire oxygen production is a different building, not a bug fix.
    ///
    /// The design requires that MATTER IS CONSERVED, not that atoms are, and the reference
    /// implementation does not conserve atoms either: Stationeers' combustion table is
    /// `2 CH4 + 1 O2 -> 3 Pollutant + 6 CO2`, two carbons in and six out. Copy the shape, not
    /// the numbers.
    ///
    /// WHY SOOT. Incomplete combustion of a heavy hydrocarbon in an oxygen-starved chamber
    /// genuinely produces particulate carbon, so this is the correct product rather than
    /// filler, and it needs no new element: `SimHashes.Carbon` is vanilla, and the Kiln already
    /// consumes it into Refined Carbon, so the waste stream has a sink. Routing the whole
    /// 0.75 kg/s there balances mass exactly and lifts carbon accounting from 8% to 52%.
    ///
    /// THE OUTLET IS COPIED FROM THE FERTILIZER SYNTHESIZER, not designed. `FertilizerMaker`
    /// has exactly this problem -- a solid byproduct accumulating inside a machine -- and
    /// solves it with a stored output plus an `ElementDropper` that emits a chunk each time the
    /// storage reaches a threshold, which a duplicant then sweeps. Both constants below are
    /// borrowed from it rather than invented:
    ///
    ///     elementDropper.emitMass   = 10f;                      // FertilizerMakerConfig
    ///     elementDropper.emitOffset = new Vector3(0f, 1f, 0f);  // FertilizerMakerConfig
    ///
    /// At 0.75 kg/s that is one 10 kg chunk every 13.3 s, and the generator never stalls,
    /// because it always has room to keep producing.
    ///
    /// TWO THINGS DELIBERATELY NOT COPIED FROM THAT CONFIG. It also calls
    /// `SetDefaultStoredItemModifiers(Storage.StandardSealedStorage)`; this does not, because
    /// the Petroleum Generator's `Storage` already holds its liquid fuel and re-sealing it
    /// would change how the fuel behaves for a reason that has nothing to do with soot.
    /// Nothing else is needed to keep duplicants out of the storage directly: `Storage`
    /// defaults to `allowItemRemoval = false`, so the dropper is the only outlet either way.
    /// Capacity is likewise left alone -- `Storage.capacityKg` defaults to 20000f, so 10 kg of
    /// accumulating carbon cannot crowd out a 20 kg fuel tank.
    ///
    /// THE TEMPERATURE IS NOT SET HERE. `minTemperature` is left at zero rather than given a
    /// plausible-looking figure, for two reasons. It would be dead data -- `EnergyGenerator.Emit`
    /// never reads the floor on the `store: true` path (see `OniFramework.GeneratorEnthalpy`
    /// for the IL that shows it) -- and the soot's real temperature is decided by the
    /// conversion-enthalpy rule from the fuel that produced it, which is where it belongs.
    /// </summary>
    /// <summary>
    /// oni-mod-compat decision 0004: Priority.Low so this always runs LAST among
    /// DoPostConfigureComplete postfixes on this building (Harmony executes postfixes in the
    /// same priority-descending order as prefixes -- lower priority = later). This patch reads
    /// generator.formula fresh at call time and measures the mass gap rather than assuming one,
    /// so running after a rebalance mod (e.g. Carbon Revolution, which edits formula.inputs/
    /// outputs directly, confirmed by field-level IL diff) means the soot output is derived from
    /// whatever conversion ratio that mod leaves behind, not stale pre-rebalance numbers. Running
    /// BEFORE such a mod risks the opposite: a mod that replaces the whole Formula wholesale
    /// (Carbon Revolution's Postfix never reads formula.outputs first) would silently drop this
    /// output again.
    /// </summary>
    [HarmonyPatch(typeof(PetroleumGeneratorConfig), "DoPostConfigureComplete")]
    [HarmonyPriority(Priority.Low)]
    internal static class PetroleumGeneratorConfig_DoPostConfigureComplete
    {
        /// <summary>
        /// Soot mass rate, kg/s. Not a tuning choice: it is exactly the mass Klei's own formula
        /// destroys (2.0 consumed less 0.5 CO2 less 0.75 polluted water), so the reworked
        /// building conserves mass by construction rather than by a number that happens to look
        /// right. If Klei ever changes the rates, this becomes wrong and the check below says so.
        /// </summary>
        private const float SootRateKgPerSecond = 0.75f;

        private static void Postfix(GameObject go)
        {
            EnergyGenerator generator = go.GetComponent<EnergyGenerator>();
            if (generator == null || generator.formula.inputs == null
                || generator.formula.outputs == null)
            {
                Debug.LogWarning("[Mod1ThermoFluid] PetroleumGenerator has no EnergyGenerator "
                    + "formula to correct; the mass-conservation fix did not apply.");
                return;
            }

            // Do not add the output twice if some other mod, or a second patch pass, got here
            // first. Matching on the element rather than on the count, because the count is
            // exactly what another mod might also have changed.
            EnergyGenerator.OutputItem[] existing = generator.formula.outputs;
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i].element == SimHashes.Carbon)
                {
                    return;
                }
            }

            // Measure the gap rather than assuming it. If another mod has already rebalanced
            // this building, closing it with a hardcoded 0.75 would overshoot and start
            // CREATING matter -- which is the same defect pointing the other way.
            float inputRate = 0f;
            for (int i = 0; i < generator.formula.inputs.Length; i++)
            {
                inputRate += generator.formula.inputs[i].consumptionRate;
            }
            float outputRate = 0f;
            for (int i = 0; i < existing.Length; i++)
            {
                outputRate += existing[i].creationRate;
            }
            float gap = inputRate - outputRate;

            if (Mathf.Abs(gap - SootRateKgPerSecond) > 1e-4f)
            {
                Debug.LogWarning("[Mod1ThermoFluid] PetroleumGenerator's mass gap is "
                    + gap.ToString("0.####") + " kg/s, not the " + SootRateKgPerSecond
                    + " kg/s this fix was derived against (" + inputRate.ToString("0.####")
                    + " in, " + outputRate.ToString("0.####") + " out). Closing the measured gap "
                    + "instead, so the building still conserves mass; re-derive the soot rate "
                    + "against the current config.");
            }
            if (!(gap > 0f))
            {
                // Nothing to close, or the formula already creates matter. Either way adding an
                // output would make it worse, and silently doing nothing would hide that.
                Debug.LogWarning("[Mod1ThermoFluid] PetroleumGenerator's formula emits "
                    + outputRate.ToString("0.####") + " kg/s from " + inputRate.ToString("0.####")
                    + " kg/s of fuel, so there is no matter deficit for soot to close. Leaving "
                    + "the formula alone.");
                return;
            }

            EnergyGenerator.OutputItem[] outputs =
                new EnergyGenerator.OutputItem[existing.Length + 1];
            Array.Copy(existing, outputs, existing.Length);
            outputs[existing.Length] = new EnergyGenerator.OutputItem(
                SimHashes.Carbon, gap, store: true, CellOffset.none, 0f);

            EnergyGenerator.Formula formula = generator.formula;
            formula.outputs = outputs;
            generator.formula = formula;

            ElementDropper dropper = go.AddOrGet<ElementDropper>();
            dropper.emitTag = SimHashes.Carbon.CreateTag();
            dropper.emitMass = 10f;
            dropper.emitOffset = new Vector3(0f, 1f, 0f);

            Debug.Log("[Mod1ThermoFluid] PetroleumGenerator now emits " + gap.ToString("0.####")
                + " kg/s of soot into its own storage, dropped in 10 kg chunks. Fuel "
                + inputRate.ToString("0.####") + " kg/s, products "
                + (outputRate + gap).ToString("0.####") + " kg/s -- mass conserves.");
        }
    }

    [HarmonyPatch(typeof(Game), "Update")]
    internal static class Game_Update
    {
        private static void Postfix()
        {
            if (!Input.GetKeyDown(KeyCode.F9))
            {
                return;
            }

            Camera main = Camera.main;
            if (main == null)
            {
                return;
            }

            Vector3 mouseWorld = main.ScreenToWorldPoint(KInputManager.GetMousePos());
            int cell = Grid.PosToCell(mouseWorld);
            if (!Grid.IsValidCell(cell))
            {
                return;
            }

            if (!GasMixtureFacade.TryGetDominant(cell, out ushort elementIdx, out float massKg))
            {
                // No gas-mixture state on this cell yet -- seed it from whatever vanilla
                // atmosphere is actually there, same as GasMixtureInjectorComponent
                // (Injector.cs), instead of a synthetic seed unrelated to the real cell. Keeps
                // this debug tool conserving too: GasMixtureFacade.ConvertFromVanilla removes
                // the same amount from Grid.Mass that it adds to the mixture layer.
                Element vanillaElement = Grid.Element[cell];
                float cellMass = Grid.Mass[cell];
                if (vanillaElement == null || !vanillaElement.IsGas || cellMass <= 0f)
                {
                    Debug.Log($"[Mod1ThermoFluid] cell {cell} has no vanilla gas mass to convert -- nothing to do");
                    return;
                }
                int elemIdx = ElementLoader.elements.IndexOf(vanillaElement);
                if (elemIdx < 0)
                {
                    return;
                }
                float convertKg = Mathf.Min(1f, cellMass);
                GasMixtureFacade.ConvertFromVanilla(cell, elemIdx, convertKg);
                Debug.Log($"[Mod1ThermoFluid] converted {convertKg:F3}kg {vanillaElement.tag} into cell {cell}'s mixture, volume-fractions now active there");
                return;
            }

            Element element = ElementLoader.elements[elementIdx];
            int rawOwned = GasMixtureFacade.DebugRoomOwnedRaw(cell);
            Debug.Log($"[Mod1ThermoFluid] cell {cell} dominant species: {element.tag} ({massKg:F3} kg total), room status={rawOwned} (-1=no room recognized here at all, 0=vanilla-owned, 1=promoted) -- see F10 to promote");
        }
    }

    /// <summary>
    /// Second debug key, added after a user's own manual live test found there was
    /// no way to actually trigger this at all: F9 (above) has NEVER called
    /// <see cref="GasMixtureFacade.PromoteRoom"/> -- it only ever seeds a cell's gas-mixture
    /// state (<c>ConvertFromVanilla</c>) or logs it. Pressing F9 on every tile in a room gives
    /// the appearance of "promoting" each one, but `RoomGraph.owned` is never touched by that
    /// key at all, so none of the promoted-room redirects (CO2 mixing, breathing, overlay) can ever
    /// engage no matter how many cells get F9'd -- exactly the confusion that motivated adding
    /// this. Press F10 over any cell to actually promote whichever room contains it.
    /// </summary>
    [HarmonyPatch(typeof(Game), "Update")]
    internal static class Game_Update_PromoteRoom
    {
        private static void Postfix()
        {
            if (!Input.GetKeyDown(KeyCode.F10))
            {
                return;
            }

            Camera main = Camera.main;
            if (main == null)
            {
                return;
            }

            Vector3 mouseWorld = main.ScreenToWorldPoint(KInputManager.GetMousePos());
            int cell = Grid.PosToCell(mouseWorld);
            if (!Grid.IsValidCell(cell))
            {
                return;
            }

            int rawBefore = GasMixtureFacade.DebugRoomOwnedRaw(cell);
            GasMixtureFacade.PromoteRoom(cell);
            Debug.Log($"[Mod1ThermoFluid] F10: sent promote for the room containing cell {cell}. Room status was {rawBefore} before this message (-1=NO ROOM RECOGNIZED HERE AT ALL -- promotion will silently do nothing, this cell was not open when the room graph was last built; 0=vanilla-owned room, promotion should land; 1=already promoted). Promotion takes one real tick to land -- press F9 over this same spot a moment later and check its logged room status to confirm.");
        }
    }
}
