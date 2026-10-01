using System.Collections.Generic;
using KSerialization;
using OniFramework;
using TUNING;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// STATIONEERS' CONDENSATION VALVE AND PURGE VALVE, the two devices its phase-change cooling
    /// loop is built out of, ported as real ONI buildings.
    ///
    /// WHAT THE LOOP IS, and the thing the device names actively mislead about. Stationeers'
    /// community phase-change guide builds cooling as: a volume pump, then a high-pressure pipe
    /// with a tank on it, then MANY condensation valves feeding a liquid buffer, then radiators or
    /// evaporation chambers -- with a purge valve on the liquid side bleeding off pressurant and
    /// boil-off. Stationeers' condensation valve performs NO
    /// PHASE CHANGE AT ALL: it is a pure liquid drain, `DrainLiquids` capped at
    /// `min(Chemistry.PipeVolume, sourceLiquidVolume / 2)` per tick, one-way, unpowered. The
    /// condensation happens in the PIPE, because the pump raises network pressure, the
    /// pressure-dependent vapour curve raises the dew point above the fluid's temperature, and the
    /// network converts gradually. The valve only harvests what the pipe has already condensed --
    /// which is also why the guide tells players to build many of them along one line. A port that
    /// put the phase change inside the valve would have ported the name rather than the mechanism.
    ///
    /// So the physics is not here. It is <see cref="PipeMatterFacade.TickNetworkPhaseChange"/>,
    /// driven once a second from <see cref="PipeStressMonitor"/>, and these two buildings are
    /// exactly what Stationeers' are: mass movers with a rate rule.
    ///
    ///   CONDENSATION VALVE -- gas pipe in, liquid pipe out. Drains standing condensate out of the
    ///   gas network at Stationeers' own rate cap and dispenses it into a liquid conduit.
    ///
    ///   PURGE VALVE -- liquid pipe in, gas pipe out. `english.xml`'s own description: "moves gas
    ///   from the input liquid pipe to the output gas pipe aiming to keep the pressure of the
    ///   input at the target setting." There is no PurgeValve class in Stationeers at all; it is a
    ///   prefab of the `PressureRegulator` family in `RegulatorType.Downstream` mode, which
    ///   targets the INPUT side and stops once input pressure is at or below setpoint. Moving gas
    ///   only, out of a liquid line, is the whole point: it is what stops the line gas-locking as
    ///   boil-off accumulates, and it is the control input for the cooling.
    ///
    /// WHY THE PURGE VALVE IS A CONTROL AND NOT A DRAIN. Boiling absorbs latent heat -- that is
    /// the cooling. Boil-off raises the liquid line's headspace pressure, which raises its boiling
    /// point, which stops the boiling. Purge that gas and the pressure falls, the boiling point
    /// falls with it, and the line boils again. How hard the valve purges therefore sets how hard
    /// the line cools, which is why the setpoint is the building's one player-facing control.
    ///
    /// PLACEHOLDER ART. Both borrow vanilla kanims (`valvegas_kanim` / `valveliquid_kanim`).
    /// Everything else
    /// about them is a real building: real BuildingDef, real conduit ports, real storage, real
    /// vanilla `ConduitDispenser` on the output side.
    ///
    /// MASS ISOLATION: neither component ever touches a cell. Mass
    /// enters through <see cref="PipeMatterFacade.TryDrain"/> -- the framework store's own audited
    /// API -- and leaves through a real vanilla `ConduitDispenser` into a real pipe. Grepped for
    /// `AddRemoveSubstance` / `ReplaceElement` / `ModifyCell` / `SimMessages.` in this file before
    /// calling it done: zero hits.
    /// </summary>
    public static class PhaseChangeValves
    {
        internal const string CondensationValveId = "CondensationValve";
        internal const string PurgeValveId = "PurgeValve";

        /// <summary>
        /// Registers both buildings' display strings and their build-menu entries.
        ///
        /// Called from <c>Mod.OnLoad</c>. `IBuildingConfig` implementors are discovered and
        /// registered automatically -- `GeneratedBuildings.LoadGeneratedBuildings` scans every
        /// loaded assembly's types for the interface -- so the configs below
        /// need no registration call of their own. What is NOT automatic is the strings table and
        /// the plan screen, hence this.
        ///
        /// NO TECH REQUIREMENT, deliberately. `BuildingDef.IsAvailable` gates only on `Deprecated`
        /// and `DebugOnly`, and a building named by no tech is simply
        /// buildable from the start. For a demonstration of a mechanic that is the right default;
        /// a real release would slot these into the research tree alongside the rest of the
        /// phase-change kit rather than handing them to the player at hour zero.
        /// </summary>
        internal static void Install()
        {
            AddStrings(CondensationValveId, "Condensation Valve",
                "Drains liquid that has condensed inside a gas pipe into a liquid pipe.",
                "Raising a gas line's pressure raises the temperature at which it condenses. This "
                + "valve harvests the liquid that results. It performs no cooling itself -- it "
                + "moves what the pipe has already produced, and it moves only a share of it per "
                + "cycle, so a heavily condensing line wants several.");

            AddStrings(PurgeValveId, "Purge Valve",
                "Removes pressurant and boiled-off gas from a liquid pipe, holding the liquid "
                + "line at a target pressure.",
                "Liquid boiling inside a pipe absorbs heat -- that is the cooling -- but the gas "
                + "it becomes raises the line's pressure, which raises its boiling point and stops "
                + "the boiling. Purging that gas lets the line boil again. The target pressure is "
                + "therefore the cooling control: the lower it is set, the harder the line boils "
                + "and the more heat it absorbs.");

            // "HVAC" for the one that sits on a gas line, "Plumbing" for the one that sits on a
            // liquid line -- the same categories vanilla files GasValve and LiquidValve under,
            // and the same "valves" subcategory (PlanSubcategoryName.valves), all read out of
            // TUNING.BUILDINGS.PLANORDER rather than guessed.
            ModUtil.AddBuildingToPlanScreen(new HashedString("HVAC"), CondensationValveId,
                BUILDINGS.PlanSubcategoryName.valves.ToString());
            ModUtil.AddBuildingToPlanScreen(new HashedString("Plumbing"), PurgeValveId,
                BUILDINGS.PlanSubcategoryName.valves.ToString());

            AddStrings(VolumePumpConfig.Id, "Volume Pump",
                "Moves a fixed volume of gas per second from one pipe network into another, "
                + "against any pressure difference.",
                "Vanilla's Gas Pump moves gas out of the room and into a pipe. This moves it from "
                + "one PIPE to another, which is the operation a refrigeration loop is built out "
                + "of: it raises the pressure of the line it discharges into, and a line at higher "
                + "pressure condenses at a higher temperature. The power it draws is added to the "
                + "gas it moves, so the discharge runs hotter than the suction -- that work is "
                + "what lets the loop carry heat from a cold room to a warm one.");
            ModUtil.AddBuildingToPlanScreen(new HashedString("HVAC"), VolumePumpConfig.Id,
                BUILDINGS.PlanSubcategoryName.valves.ToString());

            AddStrings(GasMixerConfig.Id, "Gas Mixer",
                "Blends two gas supplies into one, at a ratio you set.",
                "Duplicants do not breathe oxygen, they breathe AIR -- and what makes air "
                + "breathable is not how much of it there is but how much of it is oxygen. Set "
                + "the ratio, feed it two gases, and it delivers a blend at a single temperature "
                + "taken from both supplies. Starved of either input it slows down rather than "
                + "drifting off ratio, because a mixer that quietly gives up on the nitrogen is "
                + "a mixer that fills a room with pure oxygen.");
            ModUtil.AddBuildingToPlanScreen(new HashedString("HVAC"), GasMixerConfig.Id,
                BUILDINGS.PlanSubcategoryName.valves.ToString());

            // EACH OF THESE PUTS ITS WHOLE DRAW INTO THE FLUID IT MOVES, so PowerHeat's body
            // heat on top of that would bill the same watts twice. The Purge Valve sets itself
            // active while it moves gas, like the Volume Pump, so the grid pays for the 100 W it
            // puts into its gas, and is exempted with the rest.
            OniFramework.PowerHeat.Exempt(PurgeValveId,
                "puts its whole draw into the gas it lifts (PhaseChangeValves.cs)");
            OniFramework.PowerHeat.Exempt(VolumePumpConfig.Id,
                "puts its whole draw into the gas it moves (VolumePump.cs)");
            OniFramework.PowerHeat.Exempt(GasMixerConfig.Id,
                "puts its whole draw into its mixing buffer (GasMixer.cs)");
        }

        private static void AddStrings(string id, string name, string description, string effect)
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", name);
            Strings.Add(prefix + "DESC", description);
            Strings.Add(prefix + "EFFECT", effect);
        }
    }

    /// <summary>
    /// Gas pipe in, liquid pipe out. See <see cref="PhaseChangeValves"/> for what this is and why
    /// the phase change deliberately does not live in it.
    /// </summary>
    public class CondensationValveConfig : IBuildingConfig
    {
        public override BuildingDef CreateBuildingDef()
        {
            // Shaped on GasValveConfig, which is the closest vanilla analogue: same 1x2 footprint,
            // same tier-1 raw-metal cost, same anywhere/rotatable placement, same in-front-of-
            // conduits draw order. The one real difference is the point of the building -- its two
            // ports are different conduit TYPES, which BuildingDef supports directly because
            // InputConduitType and OutputConduitType are independent fields.
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                PhaseChangeValves.CondensationValveId, 1, 2, "valvegas_kanim", 30, 10f,
                BUILDINGS.CONSTRUCTION_MASS_KG.TIER1, MATERIALS.RAW_METALS, 1600f,
                BuildLocationRule.Anywhere, BUILDINGS.DECOR.PENALTY.TIER0,
                NOISE_POLLUTION.NOISY.TIER1);

            def.InputConduitType = ConduitType.Gas;
            def.OutputConduitType = ConduitType.Liquid;
            def.Floodable = false;
            def.ViewMode = OverlayModes.GasConduits.ID;
            def.AudioCategory = "Metal";
            def.PermittedRotations = PermittedRotations.R360;
            def.UtilityInputOffset = new CellOffset(0, 0);
            def.UtilityOutputOffset = new CellOffset(0, 1);
            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.GasVentIDs,
                PhaseChangeValves.CondensationValveId);
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            GeneratedBuildings.MakeBuildingAlwaysOperational(go);
            BuildingConfigManager.Instance.IgnoreDefaultKComponent(typeof(RequiresFoundation),
                prefabTag);

            // NO Storage AND NO ConduitDispenser, and that is a correction rather than a
            // simplification. Both valves originally buffered through a Storage and let vanilla's
            // ConduitDispenser move mass on: measured live, that leaked 21.6 kg of working fluid
            // onto the cold room's floor in a 60 s run, because a dispenser that cannot place its
            // chunk (the destination network was at its per-tile mass cap) leaves a real substance
            // chunk at the building, which then releases into the room.
            //
            // Stationeers has no such failure mode because it has no buffer: every mass-flow
            // device does one discrete transfer straight from one network to the other and moves
            // only what fits. These
            // valves now do the same -- ConduitFlow.AddElement returns what it accepted, and
            // anything it did not accept stays where it was.
            go.AddOrGet<CondensationValveComponent>();
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            // No ConduitConsumer: the input side does not read the conduit's own contents at all,
            // it reads the standing condensate the conduit cannot represent. Leaving a consumer on
            // would have it draining the working gas out of the line.
            Object.DestroyImmediate(go.GetComponent<RequireInputs>());
            Object.DestroyImmediate(go.GetComponent<ConduitConsumer>());
            Object.DestroyImmediate(go.GetComponent<ConduitDispenser>());
            go.AddOrGet<BuildingComplete>().isManuallyOperated = true;
            go.GetComponent<KPrefabID>().AddTag(GameTags.OverlayInFrontOfConduits);
        }
    }

    /// <summary>
    /// Liquid pipe in, gas pipe out. See <see cref="PhaseChangeValves"/>.
    /// </summary>
    public class PurgeValveConfig : IBuildingConfig
    {
        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                PhaseChangeValves.PurgeValveId, 1, 2, "valveliquid_kanim", 30, 10f,
                BUILDINGS.CONSTRUCTION_MASS_KG.TIER1, MATERIALS.RAW_METALS, 1600f,
                BuildLocationRule.Anywhere, BUILDINGS.DECOR.PENALTY.TIER0,
                NOISE_POLLUTION.NOISY.TIER1);

            def.InputConduitType = ConduitType.Liquid;
            def.OutputConduitType = ConduitType.Gas;
            def.Floodable = false;
            def.ViewMode = OverlayModes.LiquidConduits.ID;
            def.AudioCategory = "Metal";
            def.PermittedRotations = PermittedRotations.R360;
            def.UtilityInputOffset = new CellOffset(0, 0);
            def.UtilityOutputOffset = new CellOffset(0, 1);

            // POWERED, 100 W, because Stationeers' own Purge Valve is. That is not decoration:
            // this device takes gas out of a liquid line at low pressure and pushes it into a gas
            // line at a higher one, which is compression, and compression costs work. Unpowered, it
            // would give the phase-change loop a free-energy source: the pressure split the whole
            // cycle runs on would be maintained for nothing.
            def.RequiresPowerInput = true;
            def.PowerInputOffset = new CellOffset(0, 0);
            def.EnergyConsumptionWhenActive = PurgeValveComponent.WattageWhenActive;
            def.ExhaustKilowattsWhenActive = 0f;
            def.SelfHeatKilowattsWhenActive = 0f;

            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.LiquidVentIDs,
                PhaseChangeValves.PurgeValveId);
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            BuildingConfigManager.Instance.IgnoreDefaultKComponent(typeof(RequiresFoundation),
                prefabTag);

            // NOT MakeBuildingAlwaysOperational any more: this device is powered now, so it has to
            // be allowed to stop when it has no power. An always-operational powered building is
            // the same free-energy hole in a different place.
            go.AddOrGet<EnergyConsumer>();

            // ANIMATES ONLY WHILE IT IS ACTUALLY WORKING, the same way vanilla's machines do.
            // `ActiveController` is Klei's own state machine for this -- off, working_pre,
            // working_loop, working_pst, driven by `Operational.IsActive` -- so a building only
            // has to tell the truth about whether it moved anything this tick.
            go.AddOrGetDef<ActiveController.Def>();
            go.AddOrGet<PurgeValveComponent>();
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            Object.DestroyImmediate(go.GetComponent<RequireInputs>());
            Object.DestroyImmediate(go.GetComponent<ConduitConsumer>());
Object.DestroyImmediate(go.GetComponent<ConduitDispenser>());

            // AND RequireOutputs with it. Its `pipesHaveRoom` Operational flag is maintained by
            // the ConduitDispenser that was just destroyed, so with the dispenser gone nothing
            // ever sets that flag true and the building is held non-operational forever. That was
            // invisible while this valve was MakeBuildingAlwaysOperational, and it is what stopped
            // the valve dead the moment it became a powered device honouring IsOperational: a full
            // run reporting 0.0000 kg purged, with the flags reading
            // "powered=True ... pipesHaveRoom=False". This valve moves mass itself, network to
            // network, so it has no dispenser to speak for it and no business being gated on one.
            Object.DestroyImmediate(go.GetComponent<RequireOutputs>());
            // NOT isManuallyOperated. That flag marks a building as operated BY A DUPLICANT, which
            // adds an Operational requirement that a worker be present -- and this rig has no
            // duplicants. It was harmless while the valve was MakeBuildingAlwaysOperational, and
            // the moment the valve became powered and started honouring IsOperational it meant the
            // valve never ran at all: a whole run reporting powered=False, 0.0000 kg purged and
            // 0.00 kJ of work, with the wire and the generator both verified present.
            go.AddOrGet<BuildingComplete>();
            go.GetComponent<KPrefabID>().AddTag(GameTags.OverlayInFrontOfConduits);
        }
    }

    /// <summary>
    /// The Condensation Valve's behaviour: once a second, drain standing condensate out of the gas
    /// network at its input and hand it to the building's storage, from which vanilla's own
    /// `ConduitDispenser` puts it into the liquid pipe at its output.
    ///
    /// THE RATE RULE IS STATIONEERS' OWN, not a new one:
    /// `min(Chemistry.PipeVolume = 10 L, sourceLiquidVolume / 2)` per tick. The half is what makes
    /// a single valve unable to dry a line out, and it is the reason its guide says to build a lot
    /// of them. Stationeers ticks atmospherics at 0.5 s against this class's 1 s, so a valve here
    /// is half as fast per second of simulated time as one there; the cap is kept as written
    /// rather than rescaled, because the cap is the design decision and the cadence is ours.
    ///
    /// Drains across the whole INPUT NETWORK rather than the one tile the valve touches, which is
    /// Stationeers' own shape -- there, a network is one `Atmosphere` and "the liquid in the pipe"
    /// is a single quantity. ONI's condensate is genuinely per-tile, so this walks the run and
    /// takes from each tile in turn until the cap is spent.
    /// </summary>
    public class CondensationValveComponent : KMonoBehaviour, ISim1000ms
    {
        private const string LogPrefix = "[Mod1ThermoFluid] CONDVALVE: ";

        /// <summary>
        /// `Chemistry.PipeVolume` (Stationeers), the absolute per-tick cap on
        /// `CondensationValve.OnAtmosphericTick`'s `DrainLiquids` call.
        /// </summary>
        private const float MaxDrainLitresPerTick = 10f;

        /// <summary>
        /// The other half of the same cap: never take more than half of what is standing there.
        /// </summary>
        private const float MaxDrainFraction = 0.5f;

        private const float LitresPerCubicMetre = 1000f;

        // Resolved in OnSpawn rather than declared with Klei's [MyCmpGet]/[MyCmpReq]: those are
        // filled by reflection, which the compiler cannot see, so every one of them raises CS0649
        // in a tree that builds at zero warnings. GetComponent in OnSpawn is the same lookup at
        // the same moment, and it is what the rest of this mod's components already do.
        private Building building;

        /// <summary>Total mass this valve has moved, for a harness to assert on.</summary>
        public float HarvestedKg { get; private set; }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            building = GetComponent<Building>();
        }

        public void Sim1000ms(float dt)
        {
            try
            {
                Harvest();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(LogPrefix + "harvest failed: " + e);
            }
        }

        private void Harvest()
        {
            if (building == null)
            {
                return;
            }

            int inputCell = building.GetUtilityInputCell();
            if (!Grid.IsValidCell(inputCell))
            {
                return;
            }

            if (!PipeNetworkFacade.TryGetNetworkCells(inputCell, PipeContentType.Gas,
                    out int[] cells))
            {
                return;
            }

            float standingLitres = PipeMatterFacade.TrappedLiquidLitres(true, cells);
            if (standingLitres <= 0f)
            {
                return;
            }

            float budgetLitres = Mathf.Min(MaxDrainLitresPerTick,
                standingLitres * MaxDrainFraction);
            if (budgetLitres <= 0f)
            {
                return;
            }

            int outputCell = building.GetUtilityOutputCell();
            ConduitFlow liquidFlow = Game.Instance.liquidConduitFlow;
            if (liquidFlow == null || !liquidFlow.HasConduit(outputCell))
            {
                return;
            }

            float movedKg = 0f;

            foreach (int cell in cells)
            {
                if (budgetLitres <= 0f)
                {
                    break;
                }

                if (!PipeMatterFacade.TryGet(true, cell, out PipeMatterFacade.TrappedMatter matter)
                    || matter.MassKg <= 0f)
                {
                    continue;
                }

                Element element = ElementLoader.elements[matter.ElementIdx];
                if (!element.IsLiquid)
                {
                    continue;
                }

                if (!MaterialPropertyRegistry.TryGetLiquidDensityKgPerM3(element.id,
                        out float densityKgM3) || densityKgM3 <= 0f)
                {
                    continue;
                }

                // Budget is in litres because Stationeers' cap is a volume; the store is in
                // kilograms because ONI's everything is. Density is the only bridge, and it comes
                // from the same registry the pipe's own fill fraction is computed from, so a
                // valve and the readout beside it cannot disagree about how much is in there.
                float wantKg = budgetLitres / LitresPerCubicMetre * densityKgM3;
                if (wantKg <= 0f)
                {
                    continue;
                }

                if (!PipeMatterFacade.TryDrain(true, cell, wantKg, out int _, out float drainedKg,
                        out float temperatureK) || drainedKg <= 0f)
                {
                    continue;
                }

                // ONE DIRECT TRANSFER, and put back whatever the destination would not take.
                // AddElement returns the mass it accepted -- it caps at the conduit's own per-tile
                // limit and refuses outright if the tile already holds a different element -- so
                // the remainder has to go back where it came from rather than being dropped. The
                // buffered version of this valve dropped it, and that leak put 21.6 kg of working
                // fluid on the floor of the room it was supposed to be cooling.
                float acceptedKg = liquidFlow.AddElement(outputCell, element.id, drainedKg,
                    temperatureK, byte.MaxValue, 0);
                float rejectedKg = drainedKg - acceptedKg;
                if (rejectedKg > 0f)
                {
                    PipeMatterFacade.Add(true, cell, matter.ElementIdx, rejectedKg, temperatureK);
                }
                if (acceptedKg <= 0f)
                {
                    // The output tile is full. Nothing this valve can do until it drains, and
                    // taking more from the line would only mean putting more back.
                    break;
                }

                movedKg += acceptedKg;
                budgetLitres -= acceptedKg / densityKgM3 * LitresPerCubicMetre;
            }

            if (movedKg > 0f)
            {
                HarvestedKg += movedKg;

                // The stored/dispensed half is logged too, because the first live run of this
                // valve harvested 49 kg while the liquid line it feeds stayed at 0.000 kg, and
                // nothing in the harvest log could distinguish "storage is not accepting it" from
                // "the dispenser is not moving it on" from "it is arriving and something else is
                // taking it away". Storage mass and the outlet tile's own contents separate all
                // three.
                ConduitFlow.ConduitContents outlet = liquidFlow.GetContents(outputCell);
                Debug.Log(LogPrefix + $"cell {inputCell}: harvested {movedKg:F4} kg of condensate "
                    + $"from a {cells.Length}-tile gas run holding {standingLitres:F2} L "
                    + $"(budget was {Mathf.Min(MaxDrainLitresPerTick, standingLitres * MaxDrainFraction):F2} L); "
                    + $"outlet cell {outputCell} now holds {outlet.mass:F3} kg of "
                    + $"{outlet.element} at {outlet.temperature:F1}K");
            }
        }
    }

    /// <summary>
    /// The Purge Valve's behaviour: once a second, if the liquid network at its input is holding
    /// gas above the target pressure, move enough of that gas out into the gas pipe at its output
    /// to bring the input back to target.
    ///
    /// SETPOINT-SEEKING, NOT EQUALIZING, which is the whole difference between a regulator and a
    /// valve and is what `MoveRegulatedGas` (Stationeers) in `RegulatorType.Downstream` does: it
    /// targets the INPUT side and is a no-op the moment input pressure is already at or below
    /// setting. It can therefore run against the instantaneous gradient, and it stops on its own
    /// rather than oscillating.
    ///
    /// The amount to move follows from the gas law at fixed volume and temperature, where pressure
    /// is proportional to moles: to take a run from P down to the setpoint, remove the fraction
    /// (1 - setpoint/P) of the gas in it. Rate-capped per second, because an uncapped regulator
    /// would empty a line's headspace in one tick and slam its boiling point down with it.
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class PurgeValveComponent : KMonoBehaviour, ISim1000ms, IThresholdSwitch
    {
        private const string LogPrefix = "[Mod1ThermoFluid] PURGEVALVE: ";

        /// <summary>
        /// Default target pressure for the liquid line, in pascals.
        ///
        /// 10 kPa, and the first live run is why it is not the 150 kPa this shipped with. A
        /// regulator only acts ABOVE its setpoint, and the measured headspace of a working
        /// evaporator line was 11-13 kPa -- two orders of magnitude below 150 kPa -- so the valve
        /// purged once during an early transient and then sat idle for the rest of the run while
        /// the line it was supposed to be controlling did nothing. The setpoint has to sit inside
        /// the pressure range the line actually operates at or the control does not exist.
        ///
        /// 10 kPa is also the right ballpark on the physics: it is just above Stationeers' own
        /// `Chemistry.ArmstrongLimit` (6.3 kPa), the floor below which its substances have no
        /// liquid phase at all, so a line held here still HAS liquid to boil -- which is the point
        /// -- while sitting low enough that its boiling point stays well under room temperature.
        /// </summary>
        private const float DefaultThresholdPa = 10000f;

        /// <summary>
        /// Ceiling for the slider, in pascals. 200 kPa rather than the 2 MPa this first shipped
        /// with: a purge valve regulates the LOW-pressure side of a loop, whose whole working
        /// range was measured at tens of kPa, and a slider whose useful settings all live in its
        /// first half-percent is not a control. The high-pressure side is the compressor's
        /// business, not this valve's.
        /// </summary>
        private const float MaxThresholdPa = 200000f;

        /// <summary>
        /// Rate cap, kg of gas per second. Stationeers caps its regulators with a per-tick volume
        /// (`BaseVolumePerTick`) rather than a mass, but the purpose is the same -- stop a single
        /// tick from moving the whole line -- and mass is the unit ONI's storage and conduits both
        /// speak.
        /// </summary>
        private const float MaxPurgeKgPerSecond = 0.5f;

        /// <summary>
        /// Power draw while purging, in watts. Stationeers' own Purge Valve is a powered 100 W
        /// device, and this is the same number for the same reason: moving gas out of a liquid
        /// line at low pressure and into a gas line at a higher one is compression, and
        /// compression is work.
        /// </summary>
        public const float WattageWhenActive = 100f;

        /// <summary>
        /// The output-pressure ratio at which the valve stalls completely.
        ///
        /// Stationeers' Purge Valve is explicitly NOT a pump in this respect: a pump forces gas
        /// forward regardless of what is downstream, while the purge valve "slows down or
        /// completely stops if the pressure in the output gas pipe network is too high". No
        /// purge-valve class exists in Stationeers to read an exact curve from (it is a prefab), so
        /// the SHAPE
        /// is taken from that description and the number is a choice, called out here rather than
        /// buried: full rate while the output is no higher than the input, tapering linearly, and
        /// nothing at all once the output is this many times the input pressure.
        /// </summary>
        private const float StallPressureRatio = 8f;

        private const float PascalsPerKilopascal = 1000f;

        // See CondensationValveComponent for why these are resolved rather than attributed.
        private Building building;

        [Serialize] private float thresholdPa = DefaultThresholdPa;

        /// <summary>Total mass this valve has purged, for a harness to assert on.</summary>
        public float PurgedKg { get; private set; }

        /// <summary>
        /// Electrical work this valve has put into the gas it moved, in kilojoules. This is the
        /// loop's real work input, and reporting it is the point: a refrigeration cycle that
        /// cannot name where its work comes from is not a refrigeration cycle.
        /// </summary>
        public float WorkKJ { get; private set; }

        /// <summary>
        /// Sensible heat (m*c*T, kJ) of the gas this valve has delivered into its output line,
        /// counted at the temperature it left the liquid line's headspace, before
        /// <see cref="WorkKJ"/> is added on top. The inflow half of the suction-line energy
        /// balance whose outflow half is <see cref="VolumePumpComponent.SuctionEnthalpyKJ"/>.
        /// </summary>
        public float DeliveredEnthalpyKJ { get; private set; }

        /// <summary>
        /// How much the output line's pressure is currently throttling this valve: 1 is
        /// unrestricted, 0 is stalled. See <see cref="StallPressureRatio"/>.
        /// </summary>
        public float OutputBackoffFraction { get; private set; } = 1f;

        /// <summary>
        /// Whether this valve currently has what it needs to run. Exposed so a harness can tell
        /// "the valve chose not to move anything" apart from "the valve never ran at all" -- the
        /// two look identical in a mass total, and confusing them cost a debugging round the
        /// moment this device became powered.
        /// </summary>
        public bool IsPowered
        {
            get { return operational == null || operational.IsOperational; }
        }

        /// <summary>
        /// Every Operational flag on this building and its current value, for diagnosis.
        ///
        /// `Operational.IsOperational` is the AND of a private dictionary of named flags, and when
        /// it is false it says nothing about WHICH requirement is unmet. Making this valve powered
        /// turned that into a real dead end: two rounds were spent on candidate causes -- a missing
        /// wire, then the isManuallyOperated flag -- when reading the flags directly would have
        /// named the culprit immediately. Reflection because Klei keeps the dictionary private;
        /// this is a diagnostic, not a hot path.
        /// </summary>
        public string DescribeOperationalFlags()
        {
            return DescribeOperationalFlags(operational);
        }

        /// <summary>
        /// The same dump for any building at all, so "the building is not operational and
        /// nothing says why" is never a dead end.
        /// </summary>
        public static string DescribeOperationalFlags(Operational operational)
        {
            if (operational == null)
            {
                return "no Operational component";
            }

            // The dictionary is `Flags` (capital F), reached as a property or a field depending
            // on the build. UpdateOperational walks exactly this and ANDs every value.
            const System.Reflection.BindingFlags Any =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic;
            object raw = null;
            var property = typeof(Operational).GetProperty("Flags", Any);
            if (property != null)
            {
                raw = property.GetValue(operational, null);
            }
            else
            {
                var field = typeof(Operational).GetField("Flags", Any);
                if (field != null)
                {
                    raw = field.GetValue(operational);
                }
            }
            if (raw == null)
            {
                return "Operational.Flags not found";
            }

            var dictionary = raw as System.Collections.IDictionary;
            if (dictionary == null)
            {
                return "Operational.flags not readable";
            }

            var text = new System.Text.StringBuilder();
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                if (text.Length > 0)
                {
                    text.Append(", ");
                }
                // Operational.Flag has a Name field; its ToString does not use it, so every key
                // prints as "Operational+Flag" and the dump names nothing.
                var nameField = entry.Key.GetType().GetField("Name");
                string flagName = nameField != null
                    ? (nameField.GetValue(entry.Key) as string)
                    : entry.Key.ToString();
                text.Append(flagName + "=" + entry.Value);
            }
            return text.Length > 0 ? text.ToString() : "(no flags)";
        }

        /// <summary>The pressure setting this valve is regulating its input down to, in pascals.</summary>
        public float ThresholdPa
        {
            get { return thresholdPa; }
        }

        private Operational operational;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            building = GetComponent<Building>();
            operational = GetComponent<Operational>();
        }

        /// <summary>
        /// The pressure the input line was last measured at, in pascals. Cached rather than
        /// recomputed on demand because <see cref="CurrentValue"/> is read every frame the side
        /// screen is open and the measurement walks the whole network.
        /// </summary>
        public float MeasuredPressurePa { get; private set; }

        public void Sim1000ms(float dt)
        {
            float before = PurgedKg;
            try
            {
                Purge(dt);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(LogPrefix + "purge failed: " + e);
            }

            // THE GRID PAYS ONLY FOR AN ACTIVE BUILDING: EnergyConsumer.WattsUsed is 0 unless
            // Operational.IsActive, so without this the 100 W this valve bills into its gas would
            // never be drawn from any circuit. Active exactly while it moved gas, as the Volume
            // Pump does.
            if (operational != null)
            {
                operational.SetActive(PurgedKg > before);
            }
        }

        private void Purge(float dt)
        {
            if (building == null || dt <= 0f)
            {
                return;
            }

            // Powered now, so it stops without power. Before this the valve ran for free and the
            // whole loop's pressure split came from nowhere.
            if (operational != null && !operational.IsOperational)
            {
                return;
            }

            int inputCell = building.GetUtilityInputCell();
            if (!Grid.IsValidCell(inputCell))
            {
                return;
            }

            if (!PipeNetworkFacade.TryReadNetwork(inputCell, PipeContentType.Liquid,
                    out PipeNetworkReading state) || state.CellCount == 0)
            {
                return;
            }

            if (!PipeMatterFacade.TryGetLiquidHeadspacePressurePa(state, out float pressurePa))
            {
                MeasuredPressurePa = 0f;
                return;
            }
            MeasuredPressurePa = pressurePa;

            // The regulator's own early-out. Stationeers' Downstream branch returns without moving
            // anything when the input is already at or below setting, and so does this.
            if (pressurePa <= thresholdPa)
            {
                return;
            }

            float totalGasKg = PipeMatterFacade.TrappedGasKg(false, state.Cells);
            if (totalGasKg <= 0f)
            {
                return;
            }

            // OUTPUT PRESSURE RESTRICTION. Unlike a pump, this device gives up when the
            // downstream gas line is already too full to push into. See StallPressureRatio.
            float outputBackoff = 1f;
            int backoffCell = building.GetUtilityOutputCell();
            if (Grid.IsValidCell(backoffCell)
                && PipeNetworkFacade.TryReadNetwork(backoffCell, PipeContentType.Gas,
                    out PipeNetworkReading outState)
                && outState.PressurePa > pressurePa && pressurePa > 0f)
            {
                float ratio = outState.PressurePa / pressurePa;
                outputBackoff = Mathf.Clamp01((StallPressureRatio - ratio)
                    / (StallPressureRatio - 1f));
            }
            OutputBackoffFraction = outputBackoff;
            if (outputBackoff <= 0f)
            {
                return;
            }

            float excessFraction = 1f - thresholdPa / pressurePa;
            float wantKg = Mathf.Min(totalGasKg * excessFraction,
                MaxPurgeKgPerSecond * dt * outputBackoff);
            if (wantKg <= 0f)
            {
                return;
            }

            int outputCell = building.GetUtilityOutputCell();
            ConduitFlow gasFlow = Game.Instance.gasConduitFlow;
            if (gasFlow == null || !gasFlow.HasConduit(outputCell))
            {
                return;
            }

            float movedKg = 0f;

            foreach (int cell in state.Cells)
            {
                if (wantKg - movedKg <= 0f)
                {
                    break;
                }

                if (!PipeMatterFacade.TryGet(false, cell,
                        out PipeMatterFacade.TrappedMatter matter) || matter.MassKg <= 0f)
                {
                    continue;
                }

                Element element = ElementLoader.elements[matter.ElementIdx];
                if (!element.IsGas)
                {
                    continue;
                }

                if (!PipeMatterFacade.TryDrain(false, cell, wantKg - movedKg, out int _,
                        out float drainedKg, out float temperatureK) || drainedKg <= 0f)
                {
                    continue;
                }

                // Direct transfer, and put back what the gas network will not take. See
                // CondensationValveComponent: routing this through a Storage and vanilla's
                // ConduitDispenser is what leaked the purged mass into the room, because a
                // dispenser with nowhere to put its chunk leaves the chunk at the building.
                float acceptedKg = gasFlow.AddElement(outputCell, element.id, drainedKg,
                    temperatureK, byte.MaxValue, 0);
                float rejectedKg = drainedKg - acceptedKg;
                if (rejectedKg > 0f)
                {
                    PipeMatterFacade.Add(false, cell, matter.ElementIdx, rejectedKg, temperatureK);
                }
                if (acceptedKg <= 0f)
                {
                    // The return line is full. A regulator that cannot move gas simply does not
                    // move it -- the pressure it is holding down will rise, which is the correct
                    // and visible consequence.
                    break;
                }

                movedKg += acceptedKg;
                DeliveredEnthalpyKJ += acceptedKg * element.specificHeatCapacity * temperatureK;
            }

            if (movedKg > 0f)
            {
                PurgedKg += movedKg;

                // WHERE THE WORK GOES. The valve drew WattageWhenActive for this tick and used it
                // to lift gas from the liquid line's headspace to the gas line's higher pressure.
                // That energy does not vanish into the pressure difference -- it ends up as heat
                // in the gas that was moved, exactly as it does in the Volume Pump, which is why
                // a compressor's discharge is hotter than its suction. Billed to the destination
                // cell rather than across the network, for the reason
                // PipeMatterFacade.BillLatentHeatToCell exists.
                float workJoules = WattageWhenActive * dt;
                WorkKJ += workJoules / 1000f;
                ConduitFlow destinationFlow = Game.Instance.gasConduitFlow;
                if (destinationFlow != null)
                {
                    PipeMatterFacade.BillLatentHeatToCell(outputCell, true, destinationFlow,
                        workJoules);
                }
                Debug.Log(LogPrefix + $"cell {inputCell}: purged {movedKg:F4} kg of gas from a "
                    + $"{state.Cells.Length}-tile liquid run at "
                    + $"{pressurePa / PascalsPerKilopascal:F1} kPa "
                    + $"(target {thresholdPa / PascalsPerKilopascal:F1} kPa, "
                    + $"{totalGasKg:F4} kg standing)");
            }
        }

        // ----------------------------------------------------------------------------------
        // IThresholdSwitch -- the setpoint's player-facing control.
        //
        // Implemented rather than invented because ONI already has the whole UI for exactly this
        // shape: a building with one numeric setpoint, a live measured value beside it, and a
        // slider. Vanilla's own pressure and temperature sensors are the same interface, so the
        // side screen, the number formatting and the save round-trip all come for free.
        //
        // Values are stored and returned in PASCALS, which is what the rest of this project's
        // pressure API speaks, and displayed in kilopascals, which is what ONI shows players.
        // ----------------------------------------------------------------------------------

        public float Threshold
        {
            get { return thresholdPa; }
            set { thresholdPa = value; }
        }

        /// <summary>
        /// Meaningless for a regulator, which acts in exactly one direction, but part of the
        /// interface. Fixed true: this valve acts when the line is ABOVE its target.
        /// </summary>
        public bool ActivateAboveThreshold
        {
            get { return true; }
            set { }
        }

        public float CurrentValue => MeasuredPressurePa;

        public float RangeMin => 0f;

        public float RangeMax => MaxThresholdPa;

        public LocString Title => new LocString("Target Pressure");

        public LocString ThresholdValueName => new LocString("Pressure");

        public string AboveToolTip =>
            "Purge gas while the liquid line is above this pressure";

        public string BelowToolTip =>
            "Hold gas while the liquid line is below this pressure";

        public ThresholdScreenLayoutType LayoutType => ThresholdScreenLayoutType.SliderBar;

        public int IncrementScale => 1;

        public NonLinearSlider.Range[] GetRanges => NonLinearSlider.GetDefaultRange(MaxThresholdPa);

        public float GetRangeMinInputField() => RangeMin / PascalsPerKilopascal;

        public float GetRangeMaxInputField() => RangeMax / PascalsPerKilopascal;

        public LocString ThresholdValueUnits() => new LocString("kPa");

        public string Format(float value, bool units)
        {
            string text = (value / PascalsPerKilopascal).ToString("F1");
            return units ? text + " kPa" : text;
        }

        public float ProcessedSliderValue(float input) => Mathf.Round(input);

        public float ProcessedInputValue(float input) => input * PascalsPerKilopascal;
    }
}
