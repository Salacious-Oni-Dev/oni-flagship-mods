using System.Collections.Generic;
using System.Runtime.Serialization;
using KSerialization;
using OniFramework;
using TUNING;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// STATIONEERS' PHASE CHAMBERS: the Evaporation Chamber and the Condensation Chamber, the two
    /// buildings its minimum phase-change loop is made of.
    ///
    /// Each is a sealed 400 L vessel with a pressure regulator built in and a secondary
    /// heat-exchanger port. The phase change happens INSIDE the vessel, not in the pipes, so the
    /// pipes between two chambers only carry working fluid between two vessels held at two
    /// pressures:
    ///
    ///   EVAPORATION CHAMBER  liquid in, gas out, stands where heat is TAKEN IN.
    ///     Admits liquid until the vessel is 10 % liquid by volume and bleeds gas out whenever the
    ///     vessel is above its setting (a back-pressure regulator), so the setting IS the boiling
    ///     point. Per tick: MoveRegulatedGas(internal -> output, Downstream) then
    ///     MoveRegulatedLiquidVolume(input -> internal, 0.25 L/tick, 10 %, Upstream).
    ///   CONDENSATION CHAMBER gas in, liquid out, stands where heat is GIVEN OUT.
    ///     Pumps gas in until the vessel reaches its setting -- this is the compressor -- takes in
    ///     any liquid the gas line carried, and drains its own liquid out. Per tick:
    ///     DrainLiquids(internal -> output, 10 L) then DrainLiquids(input -> internal,
    ///     min(standing / 2, 10 L)) then MoveRegulatedGas(input -> internal, Upstream).
    ///
    /// THE HEAT-EXCHANGER PORT is a gas pipe connection and exchanges ENERGY ONLY, by
    /// <see cref="PipeHeatExchange"/>: 100 W/m^2K x 45 m^2 x both sides' exchange ratio, clamped
    /// to equilibrium, against the whole port network as one body. Like Stationeers it runs
    /// whether or not the chamber is on or powered -- it is a surface, not a machine -- while the
    /// regulators run only when the chamber is operational.
    ///
    /// NUMBERS ARE STATIONEERS' PREFAB NUMBERS: 50 W, setting
    /// 0..6000 kPa defaulting to 100 kPa, pressurePerTick 1500 kPa. Per-tick caps are kept as
    /// written against this class's 1 s tick (Stationeers ticks every 0.5 s), the same call the
    /// Condensation Valve makes: the cap is the design decision, the cadence is ours.
    ///
    /// TWO DEPARTURES, both from the no-magic-energy rule:
    ///   * BOTH GAS REGULATORS ARE WORK-LIMITED. Stationeers charges
    ///     a flat UsedPower and bills no work into the gas. Here the Condensation Chamber moves no
    ///     more gas per tick than its 50 W can lift isothermally from the line's pressure to the
    ///     vessel's (<see cref="PipeHeatExchange.MolesAffordable"/>), and the Evaporation
    ///     Chamber's bleed is priced the same way whenever it pushes into a line at a higher
    ///     pressure than the vessel (downhill stays free). A free bleed can lift about 3 kg of
    ///     steam from 9 kPa to about 1.4 MPa in the gas line, roughly 2 MJ of isothermal work
    ///     against 21.6 kJ drawn, and the line does the condensing. Either chamber can only pump heat at the
    ///     cost the second law sets, and at a high pressure ratio it slows down on its own.
    ///   * Both chambers are exempted from <see cref="OniFramework.PowerHeat"/> for that reason:
    ///     each puts all 50 W into its own vessel as heat, which is the case PowerHeat.Exempt
    ///     exists for.
    ///
    /// THE VESSEL IS THE FRAMEWORK'S <see cref="PhaseVessel"/>, the tank's phase change lifted into
    /// the API -- Stationeers' state-change port on the clamped vapour curve, 10 % per second,
    /// one species per tick. Gas volume is the vessel minus its liquid (Stationeers'
    /// Atmosphere.GetGasVolume, floored at Chemistry.MinimumGasVolume).
    /// </summary>
    public static class PhaseChambers
    {
        internal const string EvaporationChamberId = "EvaporationChamber";
        internal const string CondensationChamberId = "CondensationChamber";

        /// <summary>UsedPower (Stationeers) for both chambers, from the prefab.</summary>
        internal const float WattageWhenActive = 50f;

        /// <summary>
        /// Registers both buildings' strings, their build-menu entries, and both chambers'
        /// PowerHeat exemptions. Called from Mod.OnLoad next to the valves'.
        /// </summary>
        internal static void Install()
        {
            AddStrings(EvaporationChamberId, "Evaporation Chamber",
                "A sealed vessel that boils its working fluid at a pressure you set, drawing heat "
                + "in through its heat-exchanger port.",
                "The setting is a back-pressure regulator: gas leaves the chamber whenever it is "
                + "above the setting, so the setting is the pressure the liquid inside boils at, "
                + "and therefore the temperature. Pipe liquid in, pipe the gas it makes to a "
                + "Condensation Chamber, and connect the port to the gas network you want cooled. "
                + "Boiling takes heat, so the lower the setting, the colder the chamber runs.");

            AddStrings(CondensationChamberId, "Condensation Chamber",
                "A sealed vessel that compresses its working fluid up to a pressure you set and "
                + "condenses it, giving heat out through its heat-exchanger port.",
                "The setting is a pressure regulator: the chamber pumps gas in from its input until "
                + "it reaches the setting, and condenses it once the port has carried enough heat "
                + "away. Raising the setting raises the temperature it condenses at, so heat can "
                + "be rejected into somewhere warmer -- which costs work, and the chamber's power "
                + "limits how fast it can do it.");

            ModUtil.AddBuildingToPlanScreen(new HashedString("HVAC"), EvaporationChamberId,
                BUILDINGS.PlanSubcategoryName.valves.ToString());
            ModUtil.AddBuildingToPlanScreen(new HashedString("HVAC"), CondensationChamberId,
                BUILDINGS.PlanSubcategoryName.valves.ToString());

            PowerHeat.Exempt(EvaporationChamberId,
                "puts its whole draw into its vessel; its bleed's uphill work is paid from it "
                + "(PhaseChambers.cs)");
            PowerHeat.Exempt(CondensationChamberId,
                "puts its whole draw into its vessel as compression heat (PhaseChambers.cs)");
        }

        private static void AddStrings(string id, string name, string description, string effect)
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", name);
            Strings.Add(prefix + "DESC", description);
            Strings.Add(prefix + "EFFECT", effect);
        }

        /// <summary>
        /// The shared 2x2 def. The main pipe ports are different conduit types, which BuildingDef
        /// supports directly (the valves do the same); the heat-exchanger port is a
        /// ConduitSecondaryInput added in <see cref="ConfigureTemplate"/>, because ONI has
        /// exactly one UtilityInputOffset. Each chamber places its gas main port DIAGONAL to its
        /// gas heat-exchanger port, never beside it, so two gas pipes laid to the ports cannot
        /// join into one network by accident.
        /// </summary>
        internal static BuildingDef CreateDef(string id, string anim, ConduitType inputType,
            CellOffset inputOffset, ConduitType outputType, CellOffset outputOffset,
            CellOffset powerOffset)
        {
            // PLACEHOLDER ART, the standing caveat for this mod's buildings: the Thermo
            // Aquatuner's and Thermo Regulator's animations, both 2x2 temperature machines that
            // carry the working_pre/working_loop/working_pst this building drives through
            // Operational.
            BuildingDef def = BuildingTemplates.CreateBuildingDef(id, 2, 2, anim, 30, 30f,
                BUILDINGS.CONSTRUCTION_MASS_KG.TIER2, MATERIALS.ALL_METALS, 1600f,
                BuildLocationRule.Anywhere, BUILDINGS.DECOR.PENALTY.TIER1,
                NOISE_POLLUTION.NOISY.TIER1);

            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = WattageWhenActive;
            def.ExhaustKilowattsWhenActive = 0f;
            def.SelfHeatKilowattsWhenActive = 0f;
            def.InputConduitType = inputType;
            def.OutputConduitType = outputType;
            def.Floodable = false;
            def.ViewMode = inputType == ConduitType.Gas
                ? OverlayModes.GasConduits.ID
                : OverlayModes.LiquidConduits.ID;
            def.AudioCategory = "Metal";
            def.PermittedRotations = PermittedRotations.R360;
            def.UtilityInputOffset = inputOffset;
            def.UtilityOutputOffset = outputOffset;
            def.PowerInputOffset = powerOffset;
            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.GasVentIDs, id);
            return def;
        }

        internal static void ConfigureTemplate(GameObject go, Tag prefabTag, PhaseChamberKind kind,
            CellOffset portOffset)
        {
            BuildingConfigManager.Instance.IgnoreDefaultKComponent(typeof(RequiresFoundation),
                prefabTag);
            go.AddOrGet<EnergyConsumer>();
            go.AddOrGetDef<ActiveController.Def>();

            ConduitSecondaryInput port = go.AddOrGet<ConduitSecondaryInput>();
            port.portInfo = new ConduitPortInfo(ConduitType.Gas, portOffset);

            PhaseChamberComponent chamber = go.AddOrGet<PhaseChamberComponent>();
            chamber.Kind = kind;
            chamber.PortOffset = portOffset;
        }

        /// <summary>
        /// The same four removals the Purge Valve and the Volume Pump needed, for the same
        /// measured reasons: the chamber moves matter itself, straight between its vessel and
        /// the networks, so a vanilla consumer/dispenser pair would drain the line into a buffer
        /// that leaks, and the two Require* components would gate the building on flags nothing
        /// is left alive to set (PhaseChangeValves.cs, PurgeValveConfig).
        /// </summary>
        internal static void PostConfigure(GameObject go)
        {
            Object.DestroyImmediate(go.GetComponent<RequireInputs>());
            Object.DestroyImmediate(go.GetComponent<RequireOutputs>());
            Object.DestroyImmediate(go.GetComponent<ConduitConsumer>());
            Object.DestroyImmediate(go.GetComponent<ConduitDispenser>());
            go.AddOrGet<BuildingComplete>();
            go.GetComponent<KPrefabID>().AddTag(GameTags.OverlayInFrontOfConduits);
        }
    }

    /// <summary>Which of the two chambers a <see cref="PhaseChamberComponent"/> is.</summary>
    public enum PhaseChamberKind
    {
        Evaporation,
        Condensation,
    }

    /// <summary>
    /// Liquid in at the top left, gas out at the bottom left, the heat-exchanger port at the top
    /// right (diagonal to the gas outlet), power at the bottom right.
    /// </summary>
    public class EvaporationChamberConfig : IBuildingConfig
    {
        internal static readonly CellOffset PortOffset = new CellOffset(1, 1);

        public override BuildingDef CreateBuildingDef()
        {
            return PhaseChambers.CreateDef(PhaseChambers.EvaporationChamberId,
                "liquidconditioner_kanim", ConduitType.Liquid, new CellOffset(0, 1),
                ConduitType.Gas, new CellOffset(0, 0), new CellOffset(1, 0));
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            PhaseChambers.ConfigureTemplate(go, prefabTag, PhaseChamberKind.Evaporation, PortOffset);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            PhaseChambers.PostConfigure(go);
        }
    }

    /// <summary>
    /// Gas in at the top left, liquid out at the bottom left, the heat-exchanger port at the
    /// bottom right (diagonal to the gas inlet), power at the top right.
    /// </summary>
    public class CondensationChamberConfig : IBuildingConfig
    {
        internal static readonly CellOffset PortOffset = new CellOffset(1, 0);

        public override BuildingDef CreateBuildingDef()
        {
            return PhaseChambers.CreateDef(PhaseChambers.CondensationChamberId,
                "airconditioner_kanim", ConduitType.Gas, new CellOffset(0, 1),
                ConduitType.Liquid, new CellOffset(0, 0), new CellOffset(1, 1));
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            PhaseChambers.ConfigureTemplate(go, prefabTag, PhaseChamberKind.Condensation, PortOffset);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            PhaseChambers.PostConfigure(go);
        }
    }

    /// <summary>
    /// One chamber: the vessel, its phase change, the heat-exchanger port and the two regulators.
    /// See <see cref="PhaseChambers"/> for what each chamber is and where every number comes from.
    /// One component for both kinds, chosen by <see cref="Kind"/>, so the serialized vessel has
    /// one declaration rather than two that could drift.
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class PhaseChamberComponent : KMonoBehaviour, ISim1000ms, IThresholdSwitch
    {
        private const string LogPrefix = "[Mod1ThermoFluid] PHASECHAMBER: ";

        /// <summary>StateChangeDevice.Volume.</summary>
        internal const float VesselVolumeLitres = 400f;

        /// <summary>Chemistry.MinimumGasVolume: the gas space a vessel never drops below.</summary>
        internal const float MinimumGasVolumeLitres = 0.1f;

        /// <summary>Chemistry.PipeVolume, the volume MoveRegulatedGas prices its per-tick
        /// pressure cap in, and DrainLiquids' 10 L cap.</summary>
        internal const float PipeVolumeLitres = 10f;

        /// <summary>The prefab's outputSetting, in Pa.</summary>
        internal const float DefaultSettingPa = 100000f;

        /// <summary>The prefab's MaxSetting, in Pa.</summary>
        internal const float MaxSettingPa = 6000000f;

        /// <summary>The prefab's pressurePerTick, in Pa.</summary>
        internal const float PressurePerTickPa = 1500000f;

        /// <summary>EvaporationChamber._volumePerTick.</summary>
        internal const float LiquidAdmitLitresPerTick = 0.25f;

        /// <summary>EvaporationChamber._targetLiquidVolumePercent, as a fraction.</summary>
        internal const float TargetLiquidVolumeFraction = 0.10f;

        /// <summary>The 45 in StateChangeDevice's GetConvectionHeat call, m^2.</summary>
        internal const float HeatExchangeAreaM2 = 45f;

        /// <summary>DrainLiquids' default safetyRatio: never fill a vessel past 99 %.</summary>
        private const float DrainSafetyRatio = 0.99f;

        /// <summary>The Condensation Chamber's DrainLiquids(input) share of standing liquid.</summary>
        private const float InputDrainFraction = 0.5f;

        /// <summary>Share of a bleed's moles the outlet must take for the tick to count as
        /// delivered: float rounding in the per-species split, not a design margin.</summary>
        private const float BleedDeliveredFraction = 0.999f;

        private const float LitresPerCubicMetre = 1000f;
        private const float JoulesPerKilojoule = 1000f;
        private const float PascalsPerKilopascal = 1000f;

        /// <summary>Set on the prefab by the config; not serialized, because it is the
        /// building's identity rather than its state.</summary>
        public PhaseChamberKind Kind;

        /// <summary>The heat-exchanger port's unrotated offset, set on the prefab.</summary>
        public CellOffset PortOffset;

        [Serialize] private float settingPa = DefaultSettingPa;

        // THE VESSEL'S STORAGE. Same shape and same serialization as GasMixtureTankComponent's
        // own: element index -> kg, one shared temperature. The framework's PhaseVessel wraps the
        // two lists by reference and holds the temperature while the building runs; the field
        // below is its save copy, written back in OnSerializing.
        [Serialize] private List<int> vesselElementIdx = new List<int>();
        [Serialize] private List<float> vesselMassKg = new List<float>();
        [Serialize] private float vesselTemperatureK;

        private PhaseVessel vessel;

        private Building building;
        private Operational operational;
        private ConduitSecondaryInput secondaryInput;

        /// <summary>Energy into the vessel through the port since spawn, kJ (negative out).</summary>
        public float PortHeatKJ { get; private set; }

        /// <summary>The port's heat flow on the last tick, W (positive into the vessel).</summary>
        public float LastPortWatts { get; private set; }

        /// <summary>Both sides' exchange ratio multiplied, last tick.</summary>
        public float LastExchangeRatio { get; private set; }

        /// <summary>The port network's body temperature last tick, K (0 if none).</summary>
        public float LastPortTemperatureK { get; private set; }

        /// <summary>Mass boiled inside the vessel since spawn, kg.</summary>
        public float BoiledKg { get; private set; }

        /// <summary>Mass condensed inside the vessel since spawn, kg.</summary>
        public float CondensedKg { get; private set; }

        /// <summary>Latent heat the vessel's phase change took (+) or released (-), kJ.</summary>
        public float LatentKJ { get; private set; }

        /// <summary>Gas moved by the gas regulator since spawn, kg.</summary>
        public float GasMovedKg { get; private set; }

        /// <summary>Liquid moved in or out by the liquid side since spawn, kg.</summary>
        public float LiquidMovedKg { get; private set; }

        /// <summary>Electrical energy the chamber put into its vessel, kJ.</summary>
        public float ElectricalHeatKJ { get; private set; }

        /// <summary>Isothermal compression work of the gas actually moved uphill, kJ -- the part
        /// of <see cref="ElectricalHeatKJ"/> that lifted gas; never more than it.</summary>
        public float CompressionWorkKJ { get; private set; }

        /// <summary>Ticks on which the power budget, not the regulator, set the flow.</summary>
        public int WorkLimitedTicks { get; private set; }

        /// <summary>Evaporation Chamber: operational ticks that began above the setting, so the
        /// regulator owed a bleed.</summary>
        public int AboveSettingTicks { get; private set; }

        /// <summary>Of <see cref="AboveSettingTicks"/>, those a cap (pressurePerTick or the power
        /// budget) stopped short of the setting -- Stationeers' regulator rule allows these.</summary>
        public int CappedAboveSettingTicks { get; private set; }

        /// <summary>Of <see cref="AboveSettingTicks"/>, those that did not deliver what the
        /// regulator asked for after its caps -- no readable outlet, or an outlet that refused
        /// gas. Stationeers' rule allows none.</summary>
        public int BlockedAboveSettingTicks { get; private set; }

        /// <summary>Ticks the regulators ran.</summary>
        public int ActiveTicks { get; private set; }

        /// <summary>
        /// Sensible heat, m*c*T in kJ, carried INTO the vessel by matter (gas and liquid in),
        /// counted at the arriving temperature. With <see cref="EnthalpyOutKJ"/> it closes a
        /// vessel energy balance for a harness.
        /// </summary>
        public float EnthalpyInKJ { get; private set; }

        /// <summary>Sensible heat carried OUT of the vessel by matter, kJ.</summary>
        public float EnthalpyOutKJ { get; private set; }

        public float SettingPa => settingPa;

        public float VesselTemperatureK => Vessel.TemperatureK;

        /// <summary>
        /// The vessel: OniFramework.PhaseVessel over this building's serialized lists. Built in
        /// OnSpawn, once the deserializer has put the lists in place; built here instead for a
        /// caller that reaches it first.
        /// </summary>
        public PhaseVessel Vessel
        {
            get
            {
                if (vessel == null)
                {
                    vessel = new PhaseVessel(vesselElementIdx, vesselMassKg, vesselTemperatureK,
                        VesselVolumeLitres, MinimumGasVolumeLitres);
                }
                return vessel;
            }
        }

        [OnSerializing]
        private void OnSerializing()
        {
            if (vessel != null)
            {
                vesselTemperatureK = vessel.TemperatureK;
            }
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            building = GetComponent<Building>();
            operational = GetComponent<Operational>();
            secondaryInput = GetComponent<ConduitSecondaryInput>();
            if (vesselTemperatureK <= 0f)
            {
                int cell = Grid.PosToCell(this);
                vesselTemperatureK = Grid.IsValidCell(cell) ? Grid.Temperature[cell] : 293.15f;
            }
            vessel = new PhaseVessel(vesselElementIdx, vesselMassKg, vesselTemperatureK,
                VesselVolumeLitres, MinimumGasVolumeLitres);
        }

        /// <summary>
        /// Sets the regulator's setting, Pa, clamped to the prefab's range. The rig's handle on
        /// the same number the side screen writes through <see cref="Threshold"/>.
        /// </summary>
        public void SetSettingPa(float pa)
        {
            settingPa = Mathf.Clamp(pa, 0f, MaxSettingPa);
        }

        /// <summary>
        /// Puts working fluid straight into the vessel at <paramref name="temperatureK"/>,
        /// mixing calorimetrically. For a rig priming a loop; the building itself never calls it.
        /// </summary>
        public void PrimeVessel(SimHashes element, float massKg, float temperatureK)
        {
            int idx = ElementLoader.GetElementIndex(element);
            if (idx < 0 || massKg <= 0f)
            {
                return;
            }
            AddToVessel(idx, massKg, temperatureK);
        }

        /// <summary>The cell of the heat-exchanger port, rotated with the building.</summary>
        public int PortCell
        {
            get
            {
                if (building == null)
                {
                    return Grid.InvalidCell;
                }
                CellOffset offset = secondaryInput != null
                    ? secondaryInput.GetSecondaryConduitOffset(ConduitType.Gas)
                    : PortOffset;
                return Grid.OffsetCell(building.GetBottomLeftCell(),
                    building.GetRotatedOffset(offset));
            }
        }

        public void Sim1000ms(float dt)
        {
            bool active = false;
            try
            {
                // Stationeers' order: the vessel's own phase change is the atmosphere simulation,
                // then the device tick does its port exchange unconditionally and its regulators
                // only when operable.
                ApplyPhaseChange(dt);
                ExchangeThroughPort(dt);

                if (building != null && (operational == null || operational.IsOperational))
                {
                    if (Kind == PhaseChamberKind.Evaporation)
                    {
                        BleedGasToOutput(dt);
                        AdmitLiquidFromInput();
                    }
                    else
                    {
                        DrainLiquidToOutput();
                        DrainLiquidFromInput();
                        CompressGasFromInput(dt);
                    }

                    // THE DRAW HAS TO LAND SOMEWHERE, and both chambers are exempt from PowerHeat
                    // because this is where it lands: each one's gas regulator is a pump whose
                    // work is paid from this draw. With nothing in the vessel to take it there is
                    // nowhere honest to put it, so the chamber does not run (and so draws
                    // nothing) until it has something to hold.
                    float joules = PhaseChambers.WattageWhenActive * dt;
                    if (Vessel.AddHeat(joules))
                    {
                        ElectricalHeatKJ += joules / JoulesPerKilojoule;
                        active = true;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(LogPrefix + "tick failed: " + e);
            }

            if (active)
            {
                ActiveTicks++;
            }
            if (operational != null)
            {
                operational.SetActive(active);
            }
        }

        // ----------------------------------------------------------------------------------
        // The vessel.
        // ----------------------------------------------------------------------------------

        public float VesselHeatCapacityJPerK() => Vessel.HeatCapacityJPerK();

        public float VesselLiquidLitres() => Vessel.LiquidLitres();

        public float VesselGasVolumeLitres() => Vessel.GasVolumeLitres();

        public float VesselMassKg(bool gas) => gas ? Vessel.GasMassKg() : Vessel.LiquidMassKg();

        public float VesselTotalMassKg() => Vessel.TotalMassKg();

        /// <summary>Sensible heat of everything in the vessel, m*c*T, kJ.</summary>
        public float VesselSensibleKJ() => Vessel.SensibleHeatJ() / JoulesPerKilojoule;

        /// <summary>
        /// The vessel's gas pressure, Pa, over its gas volume: PressureGasses (Stationeers),
        /// through the one native P = nRT/V this project uses everywhere.
        /// </summary>
        public float VesselPressurePa() => Vessel.PressurePa();

        private float VesselGasMoles() => Vessel.GasMoles();

        /// <summary>
        /// Adds matter at its own temperature and mixes calorimetrically. NOT the tank's
        /// adiabatic fill: this chamber bills its compression explicitly as electrical heat, and
        /// adding the adiabatic temperature rise as well would charge the same work twice.
        /// </summary>
        private void AddToVessel(int elementIdx, float massKg, float temperatureK)
        {
            EnthalpyInKJ += Vessel.Add(elementIdx, massKg, temperatureK) / JoulesPerKilojoule;
        }

        private void RemoveFromVessel(int slot, float massKg)
        {
            EnthalpyOutKJ += Vessel.Remove(slot, massKg) / JoulesPerKilojoule;
        }

        private void ApplyPhaseChange(float dt)
        {
            if (Vessel.ElementIdx.Count == 0)
            {
                return;
            }
            if (!Vessel.StepPhaseChange(Vessel.PressurePa(), dt, out PhaseTransitionKind kind,
                    out bool rising, out float convertedKg, out float latentHeatJ))
            {
                return;
            }
            LatentKJ += latentHeatJ / JoulesPerKilojoule;
            if (kind == PhaseTransitionKind.Vaporization)
            {
                if (rising)
                {
                    BoiledKg += convertedKg;
                }
                else
                {
                    CondensedKg += convertedKg;
                }
            }
        }

        // ----------------------------------------------------------------------------------
        // The heat-exchanger port. Energy only; runs whether or not the chamber is on.
        // ----------------------------------------------------------------------------------

        private void ExchangeThroughPort(float dt)
        {
            LastPortWatts = 0f;
            LastExchangeRatio = 0f;
            LastPortTemperatureK = 0f;

            int portCell = PortCell;
            if (!Grid.IsValidCell(portCell)
                || !PipeHeatExchange.TryReadNetworkBody(portCell, PipeContentType.Gas,
                    out PipeHeatExchange.NetworkBody port))
            {
                return;
            }
            LastPortTemperatureK = port.TemperatureK;

            float vesselHeatCapacity = VesselHeatCapacityJPerK();
            if (vesselHeatCapacity <= 0f)
            {
                return;
            }

            float vesselRatio = PipeHeatExchange.HeatExchangeRatio(VesselPressurePa(),
                VesselLiquidLitres() / VesselVolumeLitres);
            float ratio = port.HeatExchangeRatio * vesselRatio;
            LastExchangeRatio = ratio;
            if (ratio <= 0f)
            {
                return;
            }

            float requestedJ = PipeHeatExchange.ConvectionHeatW(port.TemperatureK,
                Vessel.TemperatureK, HeatExchangeAreaM2 * ratio) * dt;
            float joules = PipeHeatExchange.ClampToEquilibriumJ(requestedJ, port.TemperatureK,
                port.HeatCapacityJPerK, Vessel.TemperatureK, vesselHeatCapacity);
            if (joules == 0f)
            {
                return;
            }

            // The network's half first: if it cannot take the bill, neither half happens.
            if (!PipeHeatExchange.AddHeatToNetwork(port, -joules))
            {
                return;
            }
            Vessel.TemperatureK += joules / vesselHeatCapacity;
            PortHeatKJ += joules / JoulesPerKilojoule;
            LastPortWatts = dt > 0f ? joules / dt : 0f;
        }

        // ----------------------------------------------------------------------------------
        // Evaporation Chamber regulators.
        // ----------------------------------------------------------------------------------

        /// <summary>
        /// MoveRegulatedGas(internal -> output, Downstream): bleed gas out while the vessel is
        /// above the setting, the moles that would bring it to the setting, capped at
        /// pressurePerTick priced in one pipe volume -- or, while the vessel is above the line,
        /// at a quarter of what would equalize the two, whichever is larger. A push against a
        /// line at higher pressure is paid for from the chamber's draw.
        /// </summary>
        private void BleedGasToOutput(float dt)
        {
            float vesselPa = VesselPressurePa();
            if (vesselPa <= settingPa || Vessel.TemperatureK <= 0f)
            {
                return;
            }
            // From here on the regulator owes a bleed: Stationeers' rule is that every tick above
            // the setting moves gas toward it, as far as a cap allows. A tick that ends without
            // doing that for any reason other than a cap is counted in BlockedAboveSettingTicks.
            AboveSettingTicks++;

            int outputCell = building.GetUtilityOutputCell();
            ConduitFlow gasFlow = Game.Instance.gasConduitFlow;
            // A line that cannot be read cannot be priced, so nothing moves into it this tick.
            if (gasFlow == null || !gasFlow.HasConduit(outputCell)
                || !PipeNetworkFacade.TryReadNetwork(outputCell, PipeContentType.Gas,
                    out PipeNetworkReading line) || line.VolumeLitres <= 0f)
            {
                BlockedAboveSettingTicks++;
                return;
            }

            float rt = PipeHeatExchange.GasConstantJPerMolK * Vessel.TemperatureK;
            float gasVolumeM3 = VesselGasVolumeLitres() / LitresPerCubicMetre;
            float lineVolumeM3 = line.VolumeLitres / LitresPerCubicMetre;
            float neededMoles = (vesselPa - settingPa) * gasVolumeM3 / rt;
            float capMoles = PressurePerTickPa * (PipeVolumeLitres / LitresPerCubicMetre) / rt;
            if (vesselPa > line.PressurePa)
            {
                capMoles = Mathf.Max(capMoles,
                    (vesselPa - line.PressurePa) / (rt / gasVolumeM3 + rt / lineVolumeM3) / 4f);
            }

            float totalMoles = VesselGasMoles();
            bool capped = capMoles < neededMoles;
            float moles = Mathf.Min(neededMoles, capMoles);
            if (moles <= 0f || totalMoles <= 0f)
            {
                BlockedAboveSettingTicks++;
                return;
            }

            // THE POWER BUDGET: work-limit uphill. Stationeers' bleed pushes into the line at any
            // line pressure for a flat UsedPower, which would let this chamber lift steam from
            // 9 kPa to 1.4 MPa for nothing. The moles that only bring the
            // vessel and the line to one pressure flow downhill and stay free, as through a valve.
            // Only the moles pushed past that point are priced, from the vessel's pressure after
            // the whole bleed to the line's after it -- the widest gap the tick can see -- so the
            // chamber can only be slower than the second law allows, never faster.
            float freeMoles = vesselPa > line.PressurePa
                ? (vesselPa - line.PressurePa) / (rt / gasVolumeM3 + rt / lineVolumeM3)
                : 0f;
            float fromPa = Mathf.Max(settingPa, vesselPa - moles * rt / gasVolumeM3);
            float toPa = LinePressureAfter(line, lineVolumeM3, moles, Vessel.TemperatureK);
            if (moles > freeMoles)
            {
                float affordable = PipeHeatExchange.MolesAffordable(
                    PhaseChambers.WattageWhenActive * dt, Vessel.TemperatureK, fromPa, toPa);
                if (affordable < moles - freeMoles)
                {
                    moles = freeMoles + affordable;
                    capped = true;
                    WorkLimitedTicks++;
                    if (moles <= 0f)
                    {
                        CappedAboveSettingTicks++;
                        return;
                    }
                    fromPa = Mathf.Max(settingPa, vesselPa - moles * rt / gasVolumeM3);
                    toPa = LinePressureAfter(line, lineVolumeM3, moles, Vessel.TemperatureK);
                }
            }
            if (capped)
            {
                CappedAboveSettingTicks++;
            }
            float fraction = Mathf.Clamp01(moles / totalMoles);
            float movedMoles = 0f;

            // Every gas species leaves in proportion, as Stationeers' Atmosphere.Remove does. A
            // conduit tile holds one element, so a species the outlet tile refuses stays inside.
            for (int i = Vessel.ElementIdx.Count - 1; i >= 0; i--)
            {
                Element element = ElementLoader.elements[Vessel.ElementIdx[i]];
                if (!element.IsGas)
                {
                    continue;
                }
                float wantKg = Vessel.MassKg[i] * fraction;
                if (wantKg <= 0f)
                {
                    continue;
                }
                int elementIdx = Vessel.ElementIdx[i];
                float acceptedKg = gasFlow.AddElement(outputCell, element.id, wantKg,
                    Vessel.TemperatureK, byte.MaxValue, 0);
                if (acceptedKg > 0f)
                {
                    RemoveFromVessel(i, acceptedKg);
                    GasMovedKg += acceptedKg;
                    movedMoles += PipeNetworkFacade.MolesOf(elementIdx, acceptedKg);
                }
            }

            // The outlet refused part of what the regulator, after its caps, asked it to take.
            if (movedMoles < moles * BleedDeliveredFraction)
            {
                BlockedAboveSettingTicks++;
            }

            CompressionWorkKJ += PipeHeatExchange.IsothermalCompressionWorkJ(
                Mathf.Max(movedMoles - freeMoles, 0f), Vessel.TemperatureK, fromPa, toPa)
                / JoulesPerKilojoule;
        }

        /// <summary>
        /// The line's pressure once <paramref name="moles"/> more are in it, at the line's own
        /// temperature (the incoming gas's, for an empty line): the pressure a push into it has
        /// to beat.
        /// </summary>
        private static float LinePressureAfter(PipeNetworkReading line, float lineVolumeM3,
            float moles, float incomingTemperatureK)
        {
            if (lineVolumeM3 <= 0f)
            {
                return line.PressurePa;
            }
            float lineTemperatureK = line.TemperatureK > 0f ? line.TemperatureK
                : incomingTemperatureK;
            return line.PressurePa
                + moles * PipeHeatExchange.GasConstantJPerMolK * lineTemperatureK / lineVolumeM3;
        }

        /// <summary>
        /// MoveRegulatedLiquidVolume(input -> internal, 0.25 L/tick, 10 %, Upstream): admit
        /// liquid while the vessel is under 10 % liquid, 0.25 L a tick -- raised, while the line
        /// is wetter than the vessel, to a quarter of what would level the two. Taken from every
        /// tile of the line in proportion, because the line is one body.
        /// </summary>
        private void AdmitLiquidFromInput()
        {
            int inputCell = building.GetUtilityInputCell();
            ConduitFlow liquidFlow = Game.Instance.liquidConduitFlow;
            if (liquidFlow == null || !liquidFlow.HasConduit(inputCell))
            {
                return;
            }
            if (!PipeNetworkFacade.TryReadNetwork(inputCell, PipeContentType.Liquid,
                    out PipeNetworkReading line) || line.LiquidVolumeLitres <= 0f
                || line.VolumeLitres <= 0f)
            {
                return;
            }

            float vesselLiquidLitres = VesselLiquidLitres();
            float vesselRatio = vesselLiquidLitres / VesselVolumeLitres;
            if (vesselRatio >= TargetLiquidVolumeFraction)
            {
                return;
            }

            float maxLitres = LiquidAdmitLitresPerTick;
            float lineRatio = line.LiquidVolumeLitres / line.VolumeLitres;
            if (lineRatio > vesselRatio)
            {
                float levelRatio = (line.LiquidVolumeLitres + vesselLiquidLitres)
                    / (line.VolumeLitres + VesselVolumeLitres);
                maxLitres = Mathf.Max(maxLitres,
                    (VesselVolumeLitres * levelRatio - vesselLiquidLitres) / 4f);
            }
            float wantLitres = Mathf.Min(
                (TargetLiquidVolumeFraction - vesselRatio) * VesselVolumeLitres, maxLitres);
            if (wantLitres <= 0f)
            {
                return;
            }
            float fraction = Mathf.Clamp01(wantLitres / line.LiquidVolumeLitres);

            if (!PipeNetworkFacade.TryGetNetworkCells(inputCell, PipeContentType.Liquid,
                    out int[] cells))
            {
                return;
            }
            foreach (int cell in cells)
            {
                ConduitFlow.ConduitContents contents = liquidFlow.GetContents(cell);
                if (contents.mass <= 0f || contents.element == SimHashes.Vacuum)
                {
                    continue;
                }
                Element element = ElementLoader.FindElementByHash(contents.element);
                if (element == null || !element.IsLiquid)
                {
                    continue;
                }
                float takeKg = contents.mass * fraction;
                if (takeKg <= 0f)
                {
                    continue;
                }
                ConduitFlow.ConduitContents taken = liquidFlow.RemoveElement(cell, takeKg);
                if (taken.mass <= 0f)
                {
                    continue;
                }
                AddToVessel(ElementLoader.GetElementIndex(element.id), taken.mass, taken.temperature);
                LiquidMovedKg += taken.mass;
            }
        }

        // ----------------------------------------------------------------------------------
        // Condensation Chamber regulators.
        // ----------------------------------------------------------------------------------

        /// <summary>
        /// DrainLiquids(internal -> output, Chemistry.PipeVolume): up to 10 L of the vessel's
        /// liquid a tick into the liquid line, every liquid species in proportion. The outlet
        /// tile's own acceptance is the room check: whatever it refuses stays in the vessel.
        /// </summary>
        private void DrainLiquidToOutput()
        {
            int outputCell = building.GetUtilityOutputCell();
            ConduitFlow liquidFlow = Game.Instance.liquidConduitFlow;
            if (liquidFlow == null || !liquidFlow.HasConduit(outputCell))
            {
                return;
            }
            float liquidLitres = VesselLiquidLitres();
            if (liquidLitres <= 0f)
            {
                return;
            }
            float fraction = Mathf.Clamp01(PipeVolumeLitres / liquidLitres);

            for (int i = Vessel.ElementIdx.Count - 1; i >= 0; i--)
            {
                Element element = ElementLoader.elements[Vessel.ElementIdx[i]];
                if (!element.IsLiquid)
                {
                    continue;
                }
                float wantKg = Vessel.MassKg[i] * fraction;
                if (wantKg <= 0f)
                {
                    continue;
                }
                float acceptedKg = liquidFlow.AddElement(outputCell, element.id, wantKg,
                    Vessel.TemperatureK, byte.MaxValue, 0);
                if (acceptedKg > 0f)
                {
                    RemoveFromVessel(i, acceptedKg);
                    LiquidMovedKg += acceptedKg;
                }
            }
        }

        /// <summary>
        /// DrainLiquids(input -> internal, min(standing / 2, 10 L)): the Condensation Chamber
        /// takes in liquid its gas line has already condensed, as the guide notes -- "both
        /// liquid and gas enter the Condensation Chamber". The standing condensate is the
        /// framework store's, so this drains it exactly as the Condensation Valve does, bounded
        /// by the room left in the vessel.
        /// </summary>
        private void DrainLiquidFromInput()
        {
            int inputCell = building.GetUtilityInputCell();
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
            float roomLitres = Mathf.Min(VesselVolumeLitres * DrainSafetyRatio,
                VesselVolumeLitres - MinimumGasVolumeLitres - VesselLiquidLitres());
            float budgetLitres = Mathf.Min(Mathf.Min(standingLitres * InputDrainFraction,
                PipeVolumeLitres), roomLitres);
            if (budgetLitres <= 0f)
            {
                return;
            }

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
                if (!element.IsLiquid
                    || !MaterialPropertyRegistry.TryGetLiquidDensityKgPerM3(element.id,
                        out float density) || density <= 0f)
                {
                    continue;
                }
                float wantKg = budgetLitres / LitresPerCubicMetre * density;
                if (!PipeMatterFacade.TryDrain(true, cell, wantKg, out int drainedIdx,
                        out float drainedKg, out float temperatureK) || drainedKg <= 0f)
                {
                    continue;
                }
                AddToVessel(drainedIdx, drainedKg, temperatureK);
                LiquidMovedKg += drainedKg;
                budgetLitres -= drainedKg / density * LitresPerCubicMetre;
            }
        }

        /// <summary>
        /// MoveRegulatedGas(input -> internal, Upstream), work-limited. The regulator asks for
        /// the moles that would bring the vessel to its setting, capped as Stationeers caps it;
        /// the power budget then caps it again at what 50 W can lift isothermally from the
        /// line's pressure to the pressure the vessel will be at once they arrive. The gas is
        /// taken from every tile of the line in proportion.
        /// </summary>
        private void CompressGasFromInput(float dt)
        {
            int inputCell = building.GetUtilityInputCell();
            ConduitFlow gasFlow = Game.Instance.gasConduitFlow;
            if (gasFlow == null || !gasFlow.HasConduit(inputCell))
            {
                return;
            }
            if (!PipeNetworkFacade.TryReadNetwork(inputCell, PipeContentType.Gas,
                    out PipeNetworkReading line) || line.TotalMoles <= 0f
                || line.TemperatureK <= 0f || line.VolumeLitres <= 0f)
            {
                return;
            }

            float vesselPa = VesselPressurePa();
            if (vesselPa >= settingPa)
            {
                return;
            }

            float rt = PipeHeatExchange.GasConstantJPerMolK * line.TemperatureK;
            float gasVolumeM3 = VesselGasVolumeLitres() / LitresPerCubicMetre;
            float neededMoles = (settingPa - vesselPa) * gasVolumeM3 / rt;
            float capMoles = PressurePerTickPa * (PipeVolumeLitres / LitresPerCubicMetre) / rt;
            if (line.PressurePa > vesselPa)
            {
                float lineVolumeM3 = line.VolumeLitres / LitresPerCubicMetre;
                capMoles = Mathf.Max(capMoles,
                    (line.PressurePa - vesselPa) / (rt / lineVolumeM3 + rt / gasVolumeM3) / 4f);
            }
            float moles = Mathf.Min(Mathf.Min(neededMoles, capMoles), line.TotalMoles);
            if (moles <= 0f)
            {
                return;
            }

            // THE POWER BUDGET. Priced against the pressure the vessel reaches once this tick's
            // gas is in, which over-charges slightly against the true filling integral -- the
            // conservative side, so the chamber can only ever be slower than the second law
            // allows, never faster.
            float budgetJ = PhaseChambers.WattageWhenActive * dt;
            float endPa = Mathf.Min(settingPa, vesselPa + moles * rt / gasVolumeM3);
            float affordable = PipeHeatExchange.MolesAffordable(budgetJ, line.TemperatureK,
                line.PressurePa, endPa);
            if (affordable < moles)
            {
                moles = affordable;
                WorkLimitedTicks++;
                if (moles <= 0f)
                {
                    return;
                }
                endPa = Mathf.Min(settingPa, vesselPa + moles * rt / gasVolumeM3);
            }

            float fraction = Mathf.Clamp01(moles / line.TotalMoles);
            if (!PipeNetworkFacade.TryGetNetworkCells(inputCell, PipeContentType.Gas,
                    out int[] cells))
            {
                return;
            }

            float movedMoles = 0f;
            foreach (int cell in cells)
            {
                ConduitFlow.ConduitContents contents = gasFlow.GetContents(cell);
                if (contents.mass <= 0f || contents.element == SimHashes.Vacuum)
                {
                    continue;
                }
                float takeKg = contents.mass * fraction;
                if (takeKg <= 0f)
                {
                    continue;
                }
                ConduitFlow.ConduitContents taken = gasFlow.RemoveElement(cell, takeKg);
                if (taken.mass <= 0f)
                {
                    continue;
                }
                int idx = ElementLoader.GetElementIndex(taken.element);
                if (idx < 0)
                {
                    gasFlow.AddElement(cell, taken.element, taken.mass, taken.temperature,
                        taken.diseaseIdx, taken.diseaseCount);
                    continue;
                }
                AddToVessel(idx, taken.mass, taken.temperature);
                GasMovedKg += taken.mass;
                movedMoles += PipeNetworkFacade.MolesOf(idx, taken.mass);
            }

            CompressionWorkKJ += PipeHeatExchange.IsothermalCompressionWorkJ(movedMoles,
                line.TemperatureK, line.PressurePa, endPa) / JoulesPerKilojoule;
        }

        // ----------------------------------------------------------------------------------
        // IThresholdSwitch -- the setting wheel, on vanilla's slider, stored in Pa and shown in
        // kPa exactly as the Purge Valve's is.
        // ----------------------------------------------------------------------------------

        public float Threshold
        {
            get { return settingPa; }
            set { SetSettingPa(value); }
        }

        public bool ActivateAboveThreshold
        {
            get { return Kind == PhaseChamberKind.Evaporation; }
            set { }
        }

        public float CurrentValue => VesselPressurePa();

        public float RangeMin => 0f;

        public float RangeMax => MaxSettingPa;

        public LocString Title => new LocString("Chamber Pressure");

        public LocString ThresholdValueName => new LocString("Pressure");

        public string AboveToolTip => Kind == PhaseChamberKind.Evaporation
            ? "Release gas while the chamber is above this pressure"
            : "Hold the chamber at this pressure";

        public string BelowToolTip => Kind == PhaseChamberKind.Evaporation
            ? "Hold gas while the chamber is below this pressure"
            : "Pump gas in while the chamber is below this pressure";

        public ThresholdScreenLayoutType LayoutType => ThresholdScreenLayoutType.SliderBar;

        public int IncrementScale => 1;

        public NonLinearSlider.Range[] GetRanges => NonLinearSlider.GetDefaultRange(MaxSettingPa);

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
