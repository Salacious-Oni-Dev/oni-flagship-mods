using KSerialization;
using OniFramework;
using TUNING;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// STATIONEERS' VOLUME PUMP, ported as a real ONI building: a positive-displacement
    /// pipe-to-pipe gas pump, and the piece that turns the Condensation-Valve/Purge-Valve rig from
    /// a heat spreader into a heat pump.
    ///
    /// WHY IT IS NEEDED, established by measurement rather than by analogy. With the two valves
    /// plumbed and the loop closed on a fixed charge, `--mod1-phaseloop` measured the hot room
    /// rising 4.5 K and the COLD room rising 2.2 K -- both ends warming, the loop merely
    /// redistributing the heat its own charge carried in. That is not a bug to hunt: a heat pump
    /// with no work input cannot move heat from the colder body to the warmer one, and once the
    /// harness stopped injecting fresh steam the rig had no work input at all. The pump is the
    /// work. Stationeers' community phase-change guide opens its build with one for the same
    /// reason, and this project had been treating it as scenery.
    ///
    /// WHAT VANILLA ONI HAS AND WHY IT IS NOT THIS. ONI's Gas Pump moves gas from CELLS into a
    /// pipe -- `ElementConsumer` with a consumption radius, feeding a `ConduitDispenser`. There is
    /// no vanilla building anywhere that moves gas from one pipe network into another, which is
    /// exactly the operation a refrigeration loop is built out of, and is why Mod 1's existing
    /// compressor had to be a bare logic link between two tanks
    /// (RefrigerationLoop.cs's CompressorLinkComponent) rather than a building. This closes that
    /// gap with the real thing.
    ///
    /// THE RATE RULE IS STATIONEERS' OWN (`AtmosphereHelper.MoveVolume`): it is a POSITIVE-DISPLACEMENT
    /// pump, which moves a fixed VOLUME per tick regardless of the pressure difference, clamped
    /// only by what the source actually holds. That property is the entire point -- a pump that
    /// merely equalized could never raise the high side above the low side, and there would be no
    /// dew-point difference for the cycle to run on. The displaced fraction of the source network
    /// is `displacedVolume / sourceVolume`, and that fraction of its mass moves.
    ///
    /// POWER IS ALSO STATIONEERS' OWN SHAPE: `(Setting / MaxSetting) * UsedPower`, which scales
    /// with the player's rate dial and NOT with how much gas actually moved -- a pump set to full
    /// rate against an empty line still burns full power, same as the real device.
    ///
    /// WHERE THE ENERGY GOES, which vanilla ONI would simply lose. Heat is not deleted in this
    /// direction either: a pump that draws 240 W and does no work on
    /// anything is 240 W deleted. The joules drawn each tick are added to the gas being moved, so
    /// the pump genuinely raises the working fluid's enthalpy. That is not decoration either --
    /// it is the work input the second law requires before any of this can move heat uphill, and
    /// it is why the discharge side runs hotter than the suction side.
    ///
    /// DELIBERATELY NOT PORTED: the `TurboVolumePump` variant (same class plus a runtime-flippable
    /// direction and a flat 200 W idle overhead) and the liquid branch of `MoveVolume` (which
    /// calls `MoveLiquidVolume` plus a gas equalization). Neither is needed by the loop this
    /// exists for, and porting a device nothing uses is how a reference implementation turns into
    /// a museum.
    ///
    /// MASS ISOLATION (container-mass-isolation): this never touches a cell. It moves mass with
    /// `ConduitFlow.RemoveElement` / `AddElement` between two real pipe networks, and anything the
    /// destination will not accept goes straight back into the source rather than being dropped.
    /// Grepped for `AddRemoveSubstance` / `ReplaceElement` / `ModifyCell` / `SimMessages.` in this
    /// file: zero hits.
    /// </summary>
    public class VolumePumpConfig : IBuildingConfig
    {
        internal const string Id = "VolumePump";

        /// <summary>
        /// Full-rate power draw, watts. Vanilla's own Gas Pump number
        /// (`GasPumpConfig.EnergyConsumptionWhenActive = 240f`), reused rather
        /// than invented: this does a comparable job and should cost what the player already
        /// expects a gas pump to cost.
        /// </summary>
        internal const float FullRateWatts = 240f;

        public override BuildingDef CreateBuildingDef()
        {
            // minigaspump_kanim, borrowed from vanilla's Gas Mini Pump: same 1x2 footprint, and a
            // small pump reads as a small pump. The full-size gas pump's pumpgas_kanim is a 2x2
            // animation and looks wrong on a 1x2 building.
            BuildingDef def = BuildingTemplates.CreateBuildingDef(Id, 1, 2, "minigaspump_kanim", 30,
                30f, BUILDINGS.CONSTRUCTION_MASS_KG.TIER1, MATERIALS.ALL_METALS, 1600f,
                BuildLocationRule.Anywhere, BUILDINGS.DECOR.PENALTY.TIER1,
                NOISE_POLLUTION.NOISY.TIER2);

            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = FullRateWatts;
            def.ExhaustKilowattsWhenActive = 0f;

            // The pump's own waste heat is NOT declared here. Vanilla would dump it into the
            // building as a flat rate divorced from what the machine actually did; this pump
            // instead puts the energy it draws into the gas it moves, which is both where the work
            // physically goes and what makes the loop able to pump heat at all. Declaring it in
            // both places would pay for the same joules twice.
            def.SelfHeatKilowattsWhenActive = 0f;

            def.InputConduitType = ConduitType.Gas;
            def.OutputConduitType = ConduitType.Gas;
            def.Floodable = false;
            def.ViewMode = OverlayModes.GasConduits.ID;
            def.AudioCategory = "Metal";
            def.PermittedRotations = PermittedRotations.R360;

            // SUCTION ON TOP, DISCHARGE ON THE BOTTOM -- the reverse of vanilla's valves, and
            // chosen rather than inherited. In the loop this serves, the low-pressure return
            // descends from the Purge Valve and the high-pressure condenser line runs along the
            // bottom, so a pump that draws from above and discharges below drops into the pipe run
            // with no bends. The offsets are the building's to define; nothing about ONI prefers
            // one way round.
            def.UtilityInputOffset = new CellOffset(0, 1);
            def.UtilityOutputOffset = new CellOffset(0, 0);
            def.PowerInputOffset = new CellOffset(0, 0);

            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.GasVentIDs, Id);
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            BuildingConfigManager.Instance.IgnoreDefaultKComponent(typeof(RequiresFoundation),
                prefabTag);
            go.AddOrGet<VolumePumpComponent>();
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<LogicOperationalController>();
            go.AddOrGet<EnergyConsumer>();

            // ANIMATES ONLY WHILE IT IS ACTUALLY WORKING, the same way vanilla's machines do.
            // `ActiveController` is Klei's own state machine for this -- off, working_pre,
            // working_loop, working_pst, driven by `Operational.IsActive` -- so a building only
            // has to tell the truth about whether it moved anything this tick.
            go.AddOrGetDef<ActiveController.Def>();

            // No Storage, no ConduitConsumer, no ConduitDispenser. A positive-displacement pump
            // has no buffer -- it moves what it moves, straight from one network to the other --
            // and routing it through a Storage is exactly what leaked 21 kg of working fluid onto
            // the floor when the two valves were first built that way.
            Object.DestroyImmediate(go.GetComponent<ConduitConsumer>());
            Object.DestroyImmediate(go.GetComponent<ConduitDispenser>());

            // AND THE TWO GATES THAT EXIST TO GUARD THEM. Declaring InputConduitType and
            // OutputConduitType makes ONI attach `RequireInputs` and `RequireOutputs`, which add
            // the Operational requirement flags `output_connected` and `pipesHaveRoom`. Those
            // flags are answered by the ConduitConsumer/ConduitDispenser pair just destroyed
            // above, so with the pair gone `pipesHaveRoom` can never become true: it is false on
            // the prefab and nothing is left alive to set it.
            //
            // `Operational.IsOperational` is the AND of every flag, and `Pump()` returns
            // immediately when it is false, so the pump would silently never turn -- invisibly:
            // powered=True, building_enabled=True, ports_not_overlapping=True,
            // output_connected=True, LogicOperational=True, pipesHaveRoom=False. Every check a
            // human would think to make passes.
            //
            // Destroyed on the PREFAB rather than in OnSpawn on purpose: a flag already written
            // into Operational's dictionary stays in it after its owner is gone, so the only
            // moment at which removing these is clean is before any instance exists. The pump does
            // its own connection checking anyway -- `Pump()` will not run without HasConduit on
            // both ports and a source network with mass in it.
            Object.DestroyImmediate(go.GetComponent<RequireInputs>());
            Object.DestroyImmediate(go.GetComponent<RequireOutputs>());
            go.GetComponent<KPrefabID>().AddTag(GameTags.OverlayInFrontOfConduits);
        }
    }

    /// <summary>
    /// The pump's behaviour. See <see cref="VolumePumpConfig"/> for what this is and why the loop
    /// cannot work without it.
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class VolumePumpComponent : KMonoBehaviour, ISim200ms, IThresholdSwitch
    {
        private const string LogPrefix = "[Mod1ThermoFluid] VOLUMEPUMP: ";

        /// <summary>
        /// Displacement dial, litres per second. Default 10 L/s -- one ONI gas conduit tile's
        /// entire volume every second (`GasMixtureFacade.GasConduitVolumeM3` = 0.01 m^3 = 10 L),
        /// which is a rate the player can reason about directly rather than a tuned constant.
        /// </summary>
        private const float DefaultLitresPerSecond = 10f;

        private const float MaxLitresPerSecond = 100f;

        private const float LitresPerCubicMetre = 1000f;

        private const float GramsPerKilogram = 1000f;

        private const float WattsToKilowatts = 0.001f;

        private Building building;

        private Operational operational;

        private EnergyConsumer energyConsumer;

        [Serialize] private float litresPerSecond = DefaultLitresPerSecond;

        /// <summary>Total mass this pump has moved, for a harness to assert on.</summary>
        public float PumpedKg { get; private set; }

        /// <summary>Total work this pump has put into the gas, in kilojoules.</summary>
        public float WorkKJ { get; private set; }

        /// <summary>Litres per second actually displaced last tick, for the readout.</summary>
        public float MeasuredLitresPerSecond { get; private set; }

        /// <summary>
        /// Sensible heat (m*c*T, kJ) of the gas this pump has taken out of its suction network and
        /// delivered, counted at the suction temperature, before the pump's own work is added.
        /// With <see cref="PurgeValveComponent.DeliveredEnthalpyKJ"/> it closes an energy balance
        /// on the suction line between the two (a SUCTION LINE HEAT readout), which is
        /// how heat that line loses to the rooms it crosses gets measured rather than guessed.
        /// </summary>
        public float SuctionEnthalpyKJ { get; private set; }

        /// <summary>
        /// Work this pump put back into its SUCTION network while its discharge was full, in kJ:
        /// the other term that balance needs.
        /// </summary>
        public float SuctionChurnWorkKJ { get; private set; }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            building = GetComponent<Building>();
            operational = GetComponent<Operational>();
            energyConsumer = GetComponent<EnergyConsumer>();
            ApplyPowerRating();
        }

        /// <summary>
        /// Stationeers' own power rule: draw scales with the RATE DIAL, not with how much gas
        /// actually moved. A pump set to full rate against an empty line still burns full power,
        /// which is what a real positive-displacement pump does.
        /// </summary>
        private void ApplyPowerRating()
        {
            if (energyConsumer == null)
            {
                return;
            }
            float fraction = Mathf.Clamp01(litresPerSecond / MaxLitresPerSecond);
            energyConsumer.BaseWattageRating = fraction * VolumePumpConfig.FullRateWatts;
        }

        public void Sim200ms(float dt)
        {
            try
            {
                MeasuredLitresPerSecond = 0f;
                Pump(dt);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(LogPrefix + "pump tick failed: " + e);
            }
            if (operational != null)
            {
                operational.SetActive(MeasuredLitresPerSecond > 0f);
            }
        }

        private void Pump(float dt)
        {
            if (building == null || dt <= 0f)
            {
                return;
            }

            // Runs only when powered and enabled, matching Stationeers' own gate
            // (`OnOff && Powered && Error != 1 && both networks present`).
            if (operational != null && !operational.IsOperational)
            {
                return;
            }

            ConduitFlow gasFlow = Game.Instance.gasConduitFlow;
            if (gasFlow == null)
            {
                return;
            }

            int inputCell = building.GetUtilityInputCell();
            int outputCell = building.GetUtilityOutputCell();
            if (!gasFlow.HasConduit(inputCell) || !gasFlow.HasConduit(outputCell))
            {
                return;
            }

            if (!PipeNetworkFacade.TryReadNetwork(inputCell, PipeContentType.Gas,
                    out PipeNetworkReading source) || source.CellCount == 0
                || source.TotalMassKg <= 0f)
            {
                return;
            }

            // POSITIVE DISPLACEMENT. The pump sweeps a fixed volume per tick out of the suction
            // network, so the share of that network it moves is displacedVolume/networkVolume --
            // independent of the pressure on either side, which is precisely what lets it raise
            // the discharge side above the suction side. Clamped to 1: it cannot sweep more than
            // the whole network in one tick.
            float sourceVolumeLitres = source.CellCount * GasMixtureFacade.GasConduitVolumeM3
                * LitresPerCubicMetre;
            if (sourceVolumeLitres <= 0f)
            {
                return;
            }

            float displacedLitres = litresPerSecond * dt;
            float fraction = Mathf.Clamp01(displacedLitres / sourceVolumeLitres);
            if (fraction <= 0f)
            {
                return;
            }

            // THE WORK, and where it goes. The pump is drawing power this tick whether or not the
            // line is full, so those joules exist and have to land somewhere real; adding them to
            // the gas being moved is both the physical answer and the reason the discharge side
            // ends up hotter than the suction side. Vanilla would delete them.
            float wattage = energyConsumer != null
                ? energyConsumer.BaseWattageRating
                : VolumePumpConfig.FullRateWatts;
            float workJoules = wattage * dt;

            // The work goes into the gas THIS PUMP ACTUALLY MOVES, so the denominator is the mass
            // being displaced, not the whole suction network. Getting that wrong was a real energy
            // leak, measured on the first live run: the tick's joules were shared out across the
            // entire network's mass and only the moved fraction of them was ever applied, so with
            // a 2.5%-per-tick displacement 97.5% of the pump's power simply vanished. A machine
            // that draws 240 W and delivers 6 W of work is deleting energy, which is the same
            // violation as vanilla's heat deletion pointed the other way.
            float plannedKg = source.TotalMassKg * fraction;
            if (plannedKg <= 0f)
            {
                return;
            }

            float movedKg = 0f;
            float workAppliedJoules = 0f;

            foreach (int cell in source.Cells)
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

                // This tile's share of the tick's work, by mass, applied as a temperature rise
                // against the gas's own heat capacity.
                float shareJoules = workJoules * (taken.mass / plannedKg);
                Element element = ElementLoader.FindElementByHash(taken.element);
                float dischargeK = taken.temperature;
                if (element != null && element.specificHeatCapacity > 0f && taken.mass > 0f)
                {
                    dischargeK += shareJoules
                        / (taken.mass * element.specificHeatCapacity * GramsPerKilogram);
                }

                float accepted = gasFlow.AddElement(outputCell, taken.element, taken.mass,
                    dischargeK, taken.diseaseIdx, taken.diseaseCount);
                float rejected = taken.mass - accepted;
                if (rejected > 0f && accepted > 0f)
                {
                    // Straight back into the suction line. A pump whose discharge is full simply
                    // stops moving gas; it does not lose it -- nor the work it spent on it. The
                    // rejected share goes back at the DISCHARGE temperature, the work inside it,
                    // the same "a deadheading pump heats its own fluid" answer as the
                    // all-rejected case below; returning it at the suction temperature would
                    // delete that share of the tick's work.
                    gasFlow.AddElement(cell, taken.element, rejected, dischargeK,
                        taken.diseaseIdx, taken.diseaseCount);
                    SuctionChurnWorkKJ += shareJoules * (rejected / taken.mass) * WattsToKilowatts;
                    workAppliedJoules += shareJoules * (rejected / taken.mass);
                }
                else if (rejected > 0f)
                {
                    gasFlow.AddElement(cell, taken.element, rejected, taken.temperature,
                        taken.diseaseIdx, taken.diseaseCount);
                }

                if (accepted > 0f)
                {
                    movedKg += accepted;
                    workAppliedJoules += shareJoules * (accepted / taken.mass);
                    if (element != null)
                    {
                        SuctionEnthalpyKJ += accepted * element.specificHeatCapacity
                            * taken.temperature;
                    }
                }
                else if (shareJoules > 0f)
                {
                    // Discharge full: the gas went straight back, but the pump still spent the
                    // power trying. That energy is real and has to land somewhere, so it lands on
                    // the gas it churned -- a pump deadheading against a closed line heats its
                    // own working fluid, which is exactly what a real one does.
                    Element churned = ElementLoader.FindElementByHash(taken.element);
                    if (churned != null && churned.specificHeatCapacity > 0f)
                    {
                        PipeMatterFacade.BillLatentHeatToNetwork(cell, true, gasFlow, shareJoules);
                        workAppliedJoules += shareJoules;
                        SuctionChurnWorkKJ += shareJoules * WattsToKilowatts;
                    }
                }
            }

            if (movedKg > 0f)
            {
                PumpedKg += movedKg;
                WorkKJ += workAppliedJoules * WattsToKilowatts;
                MeasuredLitresPerSecond = displacedLitres / dt;
            }
        }

        // ----------------------------------------------------------------------------------
        // IThresholdSwitch -- the displacement dial, using vanilla's own slider UI for the same
        // reason the Purge Valve's setpoint does: ONI already has the screen, the formatting and
        // the save round-trip for a building with one numeric setting and a live measured value.
        // ----------------------------------------------------------------------------------

        public float Threshold
        {
            get { return litresPerSecond; }
            set
            {
                litresPerSecond = value;
                ApplyPowerRating();
            }
        }

        /// <summary>
        /// Not meaningful for a pump, which has one direction, but part of the interface.
        /// </summary>
        public bool ActivateAboveThreshold
        {
            get { return true; }
            set { }
        }

        public float CurrentValue => MeasuredLitresPerSecond;

        public float RangeMin => 0f;

        public float RangeMax => MaxLitresPerSecond;

        public LocString Title => new LocString("Displacement");

        public LocString ThresholdValueName => new LocString("Flow rate");

        public string AboveToolTip => "Pump at this rate";

        public string BelowToolTip => "Pump at this rate";

        public ThresholdScreenLayoutType LayoutType => ThresholdScreenLayoutType.SliderBar;

        public int IncrementScale => 1;

        public NonLinearSlider.Range[] GetRanges =>
            NonLinearSlider.GetDefaultRange(MaxLitresPerSecond);

        public float GetRangeMinInputField() => RangeMin;

        public float GetRangeMaxInputField() => RangeMax;

        public LocString ThresholdValueUnits() => new LocString("L/s");

        public string Format(float value, bool units)
        {
            string text = value.ToString("F1");
            return units ? text + " L/s" : text;
        }

        public float ProcessedSliderValue(float input) => Mathf.Round(input);

        public float ProcessedInputValue(float input) => input;
    }
}
