using KSerialization;
using OniFramework;
using TUNING;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// STATIONEERS' GAS MIXER, ported as a real ONI building: two gas pipe networks in, one gas
    /// pipe network out, blended to a ratio the player dials in.
    ///
    /// WHY IT EXISTS. Breathable air is a MIXTURE -- the project's target is 75% nitrogen to 25%
    /// oxygen by mole, which at a normal room pressure puts oxygen's partial pressure comfortably
    /// above the 16 kPa <see cref="AtmosphereFacade.BreathableOxygenPartialPressurePa"/> line.
    /// Vanilla ONI has no building that produces a mixture at all, because vanilla ONI has no
    /// mixtures: a cell holds one element and a pipe tile holds one element, so "air" can only
    /// ever be pure oxygen wearing a different name. Mod 1's mixture layer removes that limit for
    /// CELLS; this building is what actually manufactures a mixture to put in them.
    ///
    /// WHAT WAS PORTED, from Stationeers' mixer:
    ///
    /// - The ratio dial is `Ratio1 = OutputSetting` (0-100) with `Ratio2 = 100 - Ratio1`, one
    ///   number, exactly as Stationeers exposes it.
    /// - Flow is driven by `WeightedAverage(P_in1, P_in2, Ratio1/100) - P_out`, and
    ///   `WeightedAverage(a, b, w) = a*w + b*(1-w)` (in `RocketMath`, not
    ///   assumed to be a Lerp, which would have weighted the wrong input).
    /// - The per-tick rate cap is Stationeers' own
    ///   `MaxMolesPerTick = deltaP / (P_perMole_input + P_perMole_output) / FLOW_RATE_LIMITER`
    ///   with `FLOW_RATE_LIMITER = 5`.
    /// - That cap only ever SCALES THE NOMINAL RATE UP, never down: Stationeers writes
    ///   `num3 = Math.Max(1.0, num3)`, so a large pressure difference pushes more gas through but
    ///   a small one does not throttle the device. Ported as written rather than "fixed" -- it is
    ///   what makes the mixer a powered device that can push into a pressurised line rather than
    ///   a passive tee that stalls the moment the output is fuller than the inputs.
    /// - RATIO-PRESERVING STARVATION, which is the behaviour that actually matters: when either
    ///   input cannot supply its share, BOTH draws are scaled by the same fraction. A mixer short
    ///   of nitrogen starves; it does not quietly drift towards pure oxygen. For a device whose
    ///   output people breathe, silently drifting off-ratio is the dangerous failure and stalling
    ///   is the safe one, and Stationeers already chose correctly.
    ///
    /// WHERE THIS DELIBERATELY DEVIATES, and why each deviation is required rather than stylistic:
    ///
    /// 1. THE RATIO IS A MOLE RATIO, NOT A PRESSURE-PER-INPUT-TEMPERATURE RATIO. Stationeers
    ///    splits its `PressurePerTick` budget by the ratio and then converts each half to moles at
    ///    THAT INPUT'S OWN TEMPERATURE (`IdealGas.Quantity(P*ratio, PipeVolume, T_input)`), so two
    ///    inputs at different temperatures do not come out at the dialled mole ratio. This
    ///    project's scenario feeds the mixer from tanks deliberately held at DIFFERENT
    ///    temperatures, and grades the result by oxygen PARTIAL PRESSURE, which is total pressure
    ///    times MOLE fraction. Carrying Stationeers' temperature dependence through would mean the
    ///    dial said 25% oxygen and the room measured something else -- a correctness bug in
    ///    exactly the demonstration this building exists for. The dial here is therefore a true
    ///    mole ratio, independent of input temperature.
    /// 2. TEMPERATURE BLENDS BY HEAT CAPACITY, not by mass. <c>GasMixtureFacade.CombineTemperature</c>
    ///    is Klei's own emitter blend and is mass-weighted, which is exact only when both sides
    ///    share a specific heat. Nitrogen (1.04) and oxygen (1.005) do not, and conservation
    ///    applies to a few joules as much as to a heat-deleting building, so the blend here sums
    ///    m*c*T. This is also the piece that answers the scenario's requirement
    ///    directly: gases arriving from tanks at different temperatures leave the mixer at ONE
    ///    blended temperature, which is what makes the dialled ratio and a comfortable 0-50 C
    ///    breathing temperature achievable from the same device.
    /// 3. IT HAS A SMALL INTERNAL VOLUME, because ONI's pipes force it to. `ConduitFlow.AddElement`
    ///    returns 0 when the target tile already holds a different element,
    ///    so a single pipe tile physically cannot receive both species in the same tick. The mixer
    ///    therefore holds its blend in an internal volume of one conduit tile's worth
    ///    (<c>GasMixtureFacade.GasConduitVolumeM3</c>, which is also Stationeers' own
    ///    `Chemistry.PipeVolume`) and emits whichever species the output tile will currently
    ///    accept. Over the run the network carries the dialled ratio; each individual tile carries
    ///    one species, which is exactly how every other mixture in this project travels down a
    ///    pipe (see MixingShowcase.cs) and is reassembled into real air by the mixture layer at
    ///    the cell the vent feeds.
    ///
    /// POWER, AND WHERE IT GOES. Like VolumePump.cs, this draws real power and puts the joules it
    /// draws into the gas it moves rather than declaring `SelfHeatKilowattsWhenActive` and letting
    /// them appear from nowhere at the building. Energy drawn has to land somewhere real.
    ///
    /// MASS ISOLATION (container-mass-isolation): this never touches a world cell. It moves mass
    /// with `ConduitFlow.RemoveElement` / `AddElement` between real pipe networks and its own
    /// serialized internal volume. Grepped for `AddRemoveSubstance` / `ReplaceElement` /
    /// `ModifyCell` / `SimMessages.` in this file: zero hits.
    ///
    /// STUBBED ON PURPOSE, for the combustion work named in
    /// <see cref="AtmosphereFacade.EvaluateCombustionRisk"/>: a mixer is the obvious place to
    /// blend a fuel into an oxidiser, and once ignition is modelled this building becomes the way
    /// a player builds a bomb by accident. It deliberately does not police its own output today --
    /// no interlock, no warning -- because refusing mixtures before there is anything to refuse
    /// them for would be inventing a rule the simulation cannot yet justify.
    /// </summary>
    public class GasMixerConfig : IBuildingConfig
    {
        internal const string Id = "GasMixer";

        /// <summary>
        /// Full-rate power draw, watts. Vanilla's Gas Filter (`GasFilterConfig`, 120 W) rather
        /// than the Gas Pump's 240 W: like the filter, this device sorts gas that other machines
        /// have already pressurised, it does not lift it out of the world.
        /// </summary>
        internal const float FullRateWatts = 120f;

        /// <summary>Offset of the SECOND gas input, the one <see cref="ISecondaryInput"/> draws.</summary>
        internal static readonly CellOffset SecondaryInputOffset = new CellOffset(1, 1);

        public override BuildingDef CreateBuildingDef()
        {
            // 2x2, which is forced rather than chosen: the building needs three distinct utility
            // port cells (two gas inputs and one gas output) and a 1x2 valve footprint only has
            // two cells to put them in.
            //
            // PLACEHOLDER ART, same standing caveat as the valves and the pump: minigaspump_kanim
            // is vanilla's Gas Mini Pump animation, borrowed because a small blending device reads
            // as a small pump and it carries the working_pre/working_loop/working_pst animation
            // this building drives through Operational.
            BuildingDef def = BuildingTemplates.CreateBuildingDef(Id, 2, 2, "minigaspump_kanim", 30,
                30f, BUILDINGS.CONSTRUCTION_MASS_KG.TIER1, MATERIALS.ALL_METALS, 1600f,
                BuildLocationRule.Anywhere, BUILDINGS.DECOR.PENALTY.TIER1,
                NOISE_POLLUTION.NOISY.TIER2);

            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = FullRateWatts;
            def.ExhaustKilowattsWhenActive = 0f;

            // Not declared, on purpose. The power this draws is added to the gas it blends (see
            // the class doc); declaring waste heat here as well would pay for the same joules
            // twice, which is the mirror image of deleting them.
            def.SelfHeatKilowattsWhenActive = 0f;

            // The PRIMARY gas input. The second one is not a BuildingDef field at all -- ONI has
            // exactly one UtilityInputOffset -- and is added as a ConduitSecondaryInput component
            // in ConfigureBuildingTemplate below.
            def.InputConduitType = ConduitType.Gas;
            def.OutputConduitType = ConduitType.Gas;
            def.Floodable = false;
            def.ViewMode = OverlayModes.GasConduits.ID;
            def.AudioCategory = "Metal";
            def.PermittedRotations = PermittedRotations.R360;

            // Two supplies along the top, blended air out of the bottom left, power bottom right.
            // Utility and power are independent layers, so co-locating a port with another port
            // of a different kind is legal and normal (VolumePump does the same).
            def.UtilityInputOffset = new CellOffset(0, 1);
            def.UtilityOutputOffset = new CellOffset(0, 0);
            def.PowerInputOffset = new CellOffset(1, 0);

            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.GasVentIDs, Id);
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            BuildingConfigManager.Instance.IgnoreDefaultKComponent(typeof(RequiresFoundation),
                prefabTag);

            // THE SECOND GAS PORT, and it is a real one rather than a hand-drawn decoration:
            // BuildingCellVisualizer enumerates every ISecondaryInput on the finished building,
            // asks it HasSecondaryConduitType(ConduitType.Gas), and adds a genuine Ports.GasIn at
            // the offset it reports. The player sees, hovers and connects to
            // it exactly like any other gas input.
            ConduitSecondaryInput secondaryInput = go.AddOrGet<ConduitSecondaryInput>();
            secondaryInput.portInfo = new ConduitPortInfo(ConduitType.Gas, SecondaryInputOffset);

            go.AddOrGet<GasMixerComponent>();
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

            // No Storage, no ConduitConsumer, no ConduitDispenser -- the same rule the pump and
            // the valves follow. This building's internal volume is a serialized ledger of its
            // own (see GasMixerComponent), not an ONI Storage; routing conduit mass through a
            // Storage is what leaked working fluid onto the floor when the valves were first
            // built that way.
            Object.DestroyImmediate(go.GetComponent<ConduitConsumer>());
            Object.DestroyImmediate(go.GetComponent<ConduitDispenser>());
            AirHandling.AllowContinuousOutputFlow(go);
            go.GetComponent<KPrefabID>().AddTag(GameTags.OverlayInFrontOfConduits);
        }
    }

    /// <summary>
    /// The mixer's behaviour. See <see cref="GasMixerConfig"/> for what was ported, what was
    /// deliberately changed, and why.
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class GasMixerComponent : KMonoBehaviour, ISim200ms, IThresholdSwitch
    {
        private const string LogPrefix = "[Mod1ThermoFluid] GASMIXER: ";

        /// <summary>
        /// `Mixer.FLOW_RATE_LIMITER` (Stationeers), unchanged.
        /// </summary>
        private const float FlowRateLimiter = 5f;

        /// <summary>
        /// Nominal throughput with no pressure difference to boost it, in moles per second.
        ///
        /// Stationeers' equivalent is a per-prefab serialized `pressurePerTick` field, which is
        /// data this project does not have, so the number is derived from the job instead. A
        /// duplicant consumes 100 g of oxygen a second (`DUPLICANTSTATS.BASESTATS
        /// .OXYGEN_USED_PER_SECOND = 0.1`), which is 3.1 mol/s; three of them is 9.4 mol/s of
        /// oxygen, and a blend that is a quarter oxygen by mole therefore has to move about
        /// 37 mol/s to keep up. 40 mol/s is that, rounded, and it is roughly 1.2 kg/s of air --
        /// the scale of a room's air handler rather than a laboratory valve.
        ///
        /// FOUND BY MEASUREMENT. The first figure here was 0.5 mol/s, reasoned from one conduit
        /// tile of air per second. It was eighty times too small for anything with people in it:
        /// the live run delivered 1.4 kg of make-up air over a hundred seconds while three
        /// duplicants breathed 30 kg of oxygen out of the room, and the corridor's oxygen
        /// partial pressure fell from 25 kPa to 5 kPa with every mechanism working perfectly.
        /// </summary>
        private const float NominalMolesPerSecond = 40f;

        /// <summary>
        /// How much the mixer's internal volume may hold before it stops drawing, in moles. One
        /// four seconds of nominal throughput: enough that the device can always assemble a full
        /// ratio-correct parcel before the pipe takes the first half of it, small enough that the
        /// gas inside is a rounding error against any real line. That internal volume is one
        /// conduit tile's worth, which is also `Chemistry.PipeVolume` (Stationeers); see
        /// deviation 3 in <see cref="GasMixerConfig"/> for why the device needs one at all.
        /// </summary>
        private const float BufferCapacityMoles = NominalMolesPerSecond * 4f;

        /// <summary>
        /// Species slots in the internal volume. Two inputs normally means two species, but either
        /// input may itself be a mixed run, so the ledger is sized to hold a realistic worst case
        /// rather than exactly two. A species that does not fit is simply not drawn that tick.
        /// </summary>
        private const int BufferSlots = 8;

        private const float DefaultRatioPercent = 75f;

        /// <summary>
        /// <see cref="DefaultRatioPercent"/> for a harness that needs to say, in a log line, what
        /// dial the device shipped with. Exposed rather than duplicated so a rig cannot quote a
        /// stale number after the default moves.
        /// </summary>
        public const float DefaultRatioPercentForDisplay = DefaultRatioPercent;

        private const float MaxRatioPercent = 100f;

        private const float GramsPerKilogram = 1000f;

        private const float JoulesToKilojoules = 0.001f;

        private Building building;

        private Operational operational;

        private EnergyConsumer energyConsumer;

        private ConduitSecondaryInput secondaryInput;

        /// <summary>
        /// Share of the blend taken from the PRIMARY input, as a percentage, by mole. Stationeers'
        /// `Ratio1`; `Ratio2` is the remainder and is never stored separately.
        /// </summary>
        [Serialize] private float ratio1Percent = DefaultRatioPercent;

        /// <summary>
        /// The internal volume: element index and mass per slot, plus the one temperature the
        /// whole blend sits at. Serialized because it is real mass -- losing it across a save
        /// would be exactly the quiet matter leak container-mass-isolation exists to prevent.
        /// A slot with <c>elementIdx &lt; 0</c> is empty.
        /// </summary>
        [Serialize] private int[] bufferElementIdx = NewEmptySlots();

        [Serialize] private float[] bufferMassKg = new float[BufferSlots];

        [Serialize] private float bufferTemperatureK;

        /// <summary>Total mass this mixer has delivered to its output, for a harness to assert on.</summary>
        public float MixedKg { get; private set; }

        /// <summary>Moles delivered that came from the primary input.</summary>
        public float DeliveredPrimaryMoles { get; private set; }

        /// <summary>Moles delivered that came from the secondary input.</summary>
        public float DeliveredSecondaryMoles { get; private set; }

        /// <summary>Total work put into the blended gas, in kilojoules.</summary>
        public float WorkKJ { get; private set; }

        /// <summary>
        /// Moles the mixer ASKED each input for, after every scaling stage -- the pressure boost,
        /// the ratio-preserving starvation clamp and the buffer-headroom fit. Paired with
        /// <see cref="DeliveredPrimaryMoles"/> so a harness can tell a mixer that dialled the
        /// wrong split from a mixer that dialled the right one and did not get it. Without both
        /// numbers a delivered ratio off the dial is unattributable: the request and the draw are
        /// separate mechanisms and only one of them can be at fault at a time.
        /// </summary>
        public float RequestedPrimaryMoles { get; private set; }

        /// <summary>See <see cref="RequestedPrimaryMoles"/>.</summary>
        public float RequestedSecondaryMoles { get; private set; }

        /// <summary>
        /// Throws away everything the mixer has drawn and delivered so far, so a harness can
        /// measure a window rather than a lifetime. Returns the moles discarded, primary first.
        ///
        /// WHY A RIG NEEDS THIS. A Gas Mixer starts at <c>DefaultRatioPercent</c> and runs at it
        /// until somebody turns the dial -- correct behaviour for a building a player just placed,
        /// and a measurement error for a scenario that sets the dial from script. On a blueprint
        /// canvas the tanks are full and the pumps are running from the first tick, so the mixer
        /// blends at the DEFAULT ratio for the whole window between spawning and being configured,
        /// and a lifetime counter folds that into the answer: a 30% dial can read 36.8% delivered,
        /// genuinely, but not all of it under the settings being asserted on.
        /// </summary>
        public void ResetDeliveryAccounting(out float discardedPrimaryMoles,
            out float discardedSecondaryMoles)
        {
            discardedPrimaryMoles = DeliveredPrimaryMoles;
            discardedSecondaryMoles = DeliveredSecondaryMoles;
            DeliveredPrimaryMoles = 0f;
            DeliveredSecondaryMoles = 0f;
            RequestedPrimaryMoles = 0f;
            RequestedSecondaryMoles = 0f;
            MixedKg = 0f;
            WorkKJ = 0f;
        }

        /// <summary>
        /// The ratio the mixer has actually ASKED FOR so far, as a percentage of requested moles
        /// from the primary input. Sits between the dial and
        /// <see cref="MeasuredRatioPercent"/>: if this tracks the dial and the delivered ratio
        /// does not, the drift is in the draw; if this is already off, the drift is upstream in
        /// the split.
        /// </summary>
        public float RequestedRatioPercent
        {
            get
            {
                float total = RequestedPrimaryMoles + RequestedSecondaryMoles;
                if (total <= 0f)
                {
                    return 0f;
                }
                return RequestedPrimaryMoles / total * MaxRatioPercent;
            }
        }

        /// <summary>
        /// The ratio the mixer has actually DELIVERED so far, as a percentage of moles from the
        /// primary input -- not the dial. The two diverge exactly when the device is starved,
        /// which is the failure the player needs to be able to see.
        /// </summary>
        public float MeasuredRatioPercent
        {
            get
            {
                float total = DeliveredPrimaryMoles + DeliveredSecondaryMoles;
                if (total <= 0f)
                {
                    return 0f;
                }
                return DeliveredPrimaryMoles / total * MaxRatioPercent;
            }
        }

        private static int[] NewEmptySlots()
        {
            var slots = new int[BufferSlots];
            for (int i = 0; i < BufferSlots; i++)
            {
                slots[i] = -1;
            }
            return slots;
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            building = GetComponent<Building>();
            operational = GetComponent<Operational>();
            energyConsumer = GetComponent<EnergyConsumer>();
            secondaryInput = GetComponent<ConduitSecondaryInput>();

            // A save written before the ledger existed, or one whose arrays came back the wrong
            // length, must not be allowed to index out of range on the first tick.
            if (bufferElementIdx == null || bufferElementIdx.Length != BufferSlots)
            {
                bufferElementIdx = NewEmptySlots();
            }
            if (bufferMassKg == null || bufferMassKg.Length != BufferSlots)
            {
                bufferMassKg = new float[BufferSlots];
            }
        }

        public void Sim200ms(float dt)
        {
            float before = MixedKg;
            try
            {
                Mix(dt);
                Flush();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(LogPrefix + "mix tick failed: " + e);
            }
            if (operational != null)
            {
                operational.SetActive(MixedKg > before);
            }
        }

        /// <summary>
        /// The cell of the second gas input, read off the live <see cref="ConduitSecondaryInput"/>
        /// and rotated with the building, so a rotated mixer's ports move together and the cell
        /// this draws from is always the cell the player sees a port drawn on.
        /// </summary>
        private int SecondaryInputCell
        {
            get
            {
                CellOffset offset = secondaryInput != null
                    ? secondaryInput.GetSecondaryConduitOffset(ConduitType.Gas)
                    : GasMixerConfig.SecondaryInputOffset;
                return Grid.OffsetCell(building.GetBottomLeftCell(),
                    building.GetRotatedOffset(offset));
            }
        }

        private void Mix(float dt)
        {
            if (building == null || dt <= 0f)
            {
                return;
            }

            // Stationeers' own gate: powered, switched on, and all three connections valid.
            if (operational != null && !operational.IsOperational)
            {
                return;
            }

            ConduitFlow gasFlow = Game.Instance?.gasConduitFlow;
            if (gasFlow == null)
            {
                return;
            }

            int primaryCell = building.GetUtilityInputCell();
            int secondaryCell = SecondaryInputCell;
            int outputCell = building.GetUtilityOutputCell();
            if (!gasFlow.HasConduit(primaryCell) || !gasFlow.HasConduit(secondaryCell)
                || !gasFlow.HasConduit(outputCell))
            {
                return;
            }

            if (!PipeNetworkFacade.TryReadNetwork(primaryCell, PipeContentType.Gas,
                    out PipeNetworkReading primary)
                || !PipeNetworkFacade.TryReadNetwork(secondaryCell, PipeContentType.Gas,
                    out PipeNetworkReading secondary)
                || !PipeNetworkFacade.TryReadNetwork(outputCell, PipeContentType.Gas,
                    out PipeNetworkReading output))
            {
                return;
            }

            float ratio1 = Mathf.Clamp01(ratio1Percent / MaxRatioPercent);
            float ratio2 = 1f - ratio1;

            // THE DIALLED SPLIT, in moles. Deviation 1 in the class doc: Stationeers splits a
            // pressure budget and converts each half at its own input's temperature, which makes
            // the delivered mole ratio depend on how cold the tanks are. This splits moles
            // directly, so the dial means what it says regardless of temperature.
            float plannedMoles = NominalMolesPerSecond * dt;
            float moles1 = plannedMoles * ratio1;
            float moles2 = plannedMoles * ratio2;

            // STATIONEERS' PRESSURE BOOST. deltaP is the weighted average of the two input
            // pressures minus the output's; when it is positive the per-input rate caps are
            // computed and used to scale the draw UP. Math.Max(1.0, ...) is Stationeers' own --
            // the cap never throttles below nominal.
            float deltaP = (primary.PressurePa * ratio1 + secondary.PressurePa * ratio2)
                - output.PressurePa;
            if (deltaP > 0f)
            {
                float cap1 = MaxMolesPerTick(primary, output, deltaP);
                float cap2 = MaxMolesPerTick(secondary, output, deltaP);
                float scale = 1f;
                if (Mathf.Approximately(ratio1Percent, MaxRatioPercent))
                {
                    scale = moles1 > 0f ? cap1 / moles1 : 1f;
                }
                else if (Mathf.Approximately(ratio1Percent, 0f))
                {
                    scale = moles2 > 0f ? cap2 / moles2 : 1f;
                }
                else if (moles1 > 0f && moles2 > 0f)
                {
                    scale = Mathf.Min(cap1 / moles1, cap2 / moles2);
                }
                scale = Mathf.Max(1f, scale);
                moles1 *= scale;
                moles2 *= scale;
            }

            // RATIO-PRESERVING STARVATION, the behaviour the whole port is for. Both draws scale
            // by the SAME fraction when either input is short, so the mixer stalls at the dialled
            // ratio rather than drifting towards whichever gas is still available.
            float available1 = AvailableMoles(primaryCell);
            float available2 = AvailableMoles(secondaryCell);
            if (available1 < moles1 || available2 < moles2)
            {
                float fraction = 0f;
                if (moles1 > 0f && moles2 > 0f)
                {
                    fraction = Mathf.Min(available1 / moles1, available2 / moles2);
                }
                else if (moles2 <= 0f && moles1 > 0f)
                {
                    fraction = available1 / moles1;
                }
                else if (moles1 <= 0f && moles2 > 0f)
                {
                    fraction = available2 / moles2;
                }
                moles1 *= fraction;
                moles2 *= fraction;
            }

            // The internal volume is finite, so the whole parcel is scaled once more by whatever
            // headroom is left. Scaling BOTH halves keeps the ratio intact here too -- filling
            // only the species that happens to fit would be the same silent drift, arriving by a
            // different route.
            float headroom = BufferCapacityMoles - BufferedMoles();
            float requested = moles1 + moles2;
            if (requested <= 0f)
            {
                return;
            }
            if (headroom <= 0f)
            {
                return;
            }
            if (requested > headroom)
            {
                float fit = headroom / requested;
                moles1 *= fit;
                moles2 *= fit;
            }

            RequestedPrimaryMoles += moles1;
            RequestedSecondaryMoles += moles2;

            float drawn1 = Draw(gasFlow, primaryCell, primary, moles1);
            float drawn2 = Draw(gasFlow, secondaryCell, secondary, moles2);
            DeliveredPrimaryMoles += drawn1;
            DeliveredSecondaryMoles += drawn2;

            // THE WORK. The device drew power this tick whether or not anything moved, so those
            // joules exist. They go into the gas in the internal volume, which is both where the
            // work physically goes and why the blend leaves marginally warmer than a naive average
            // of its two inputs.
            float wattage = energyConsumer != null
                ? energyConsumer.BaseWattageRating
                : GasMixerConfig.FullRateWatts;
            float workJoules = wattage * dt;
            float heatCapacity = BufferHeatCapacityJPerK();
            if (workJoules > 0f && heatCapacity > 0f)
            {
                bufferTemperatureK += workJoules / heatCapacity;
                WorkKJ += workJoules * JoulesToKilojoules;
            }
        }

        /// <summary>
        /// `MaxMolesPerTick` (Stationeers), ported directly: the pressure differential divided by
        /// the sum of the input network's and the output network's pressure-per-mole, divided by
        /// the flow rate limiter.
        ///
        /// Note the second term uses the INPUT's temperature against the OUTPUT's volume, which is
        /// what Stationeers writes (`IdealGas.PressurePerMole(inputAtmosphere.Temperature,
        /// OutputNetwork.Atmosphere.Volume)`) and is kept rather than tidied.
        /// </summary>
        private static float MaxMolesPerTick(PipeNetworkReading input, PipeNetworkReading output,
            float deltaP)
        {
            if (input.TotalMassKg <= 0f)
            {
                return 0f;
            }
            float perMoleInput = PressurePerMolePa(input.TemperatureK, NetworkVolumeM3(input));
            float perMoleOutput = PressurePerMolePa(input.TemperatureK, NetworkVolumeM3(output));
            float sum = perMoleInput + perMoleOutput;
            if (sum <= 0f)
            {
                return 0f;
            }
            return deltaP / sum / FlowRateLimiter;
        }

        private static float NetworkVolumeM3(PipeNetworkReading state)
        {
            return state.CellCount * GasMixtureFacade.GasConduitVolumeM3;
        }

        /// <summary>
        /// Pressure one mole of gas exerts in the given volume at the given temperature, asked of
        /// the SIMULATION rather than computed from a hand-typed gas constant
        /// (native-sim-first-discipline). The species handed in is arbitrary because an ideal gas
        /// does not care -- P = nRT/V -- and oxygen is used simply because it is guaranteed to
        /// exist in every build; the point of routing through
        /// <see cref="GasMixtureFacade.TryComputePressure"/> is that this number cannot drift away
        /// from the pressures the rest of the game reports.
        /// </summary>
        private static float PressurePerMolePa(float temperatureK, float volumeM3)
        {
            if (temperatureK <= 0f || volumeM3 <= 0f)
            {
                return 0f;
            }
            int oxygenIdx = ElementLoader.elements.IndexOf(
                ElementLoader.FindElementByHash(SimHashes.Oxygen));
            if (oxygenIdx < 0)
            {
                return 0f;
            }
            float oneMoleKg = AtmosphereFacade.MolarMassGPerMol((ushort)oxygenIdx)
                / GramsPerKilogram;
            if (oneMoleKg <= 0f)
            {
                return 0f;
            }
            if (!GasMixtureFacade.TryComputePressure(new[] { oxygenIdx }, new[] { oneMoleKg },
                    temperatureK, volumeM3, out float pressurePa))
            {
                return 0f;
            }
            return pressurePa;
        }

        /// <summary>
        /// Moles a network holds, counted with the SIMULATION's molar masses
        /// (<see cref="AtmosphereFacade.MolarMassGPerMol"/>) rather than
        /// <see cref="PipeNetworkState.TotalMoles"/>, which uses ONI's element-table convention.
        /// The two disagree by a factor of two on diatomic gases -- oxygen and nitrogen, which is
        /// to say on both of the gases this building exists to blend -- and the ratio dialled here
        /// has to be the same ratio <see cref="AtmosphereFacade"/> later grades the room by.
        /// </summary>
        /// <summary>Buffers for <see cref="AvailableMoles"/>, sized to the element table and
        /// kept, so reading a composition every tick costs no allocation. A conduit network cannot
        /// hold more distinct elements than the table has entries, which is what makes a
        /// fixed-size buffer safe here -- <c>PipeNetworkFacade.ReadNetworkComposition</c> REFUSES
        /// rather than truncates when a buffer is too small, and a silent 0 would stall the
        /// mixer.</summary>
        private int[] compositionIdx;
        private float[] compositionMassKg;

        private float AvailableMoles(int portCell)
        {
            int capacity = ElementLoader.elements != null ? ElementLoader.elements.Count : 0;
            if (capacity <= 0)
            {
                return 0f;
            }
            if (compositionIdx == null || compositionIdx.Length < capacity)
            {
                compositionIdx = new int[capacity];
                compositionMassKg = new float[capacity];
            }

            int count = PipeNetworkFacade.ReadNetworkComposition(portCell, PipeContentType.Gas,
                compositionIdx, compositionMassKg, null);
            float moles = 0f;
            for (int i = 0; i < count; i++)
            {
                moles += MolesOf(compositionIdx[i], compositionMassKg[i]);
            }
            return moles;
        }

        private static float MolesOf(int elementIdx, float massKg)
        {
            if (massKg <= 0f || elementIdx < 0 || ElementLoader.elements == null
                || elementIdx >= ElementLoader.elements.Count)
            {
                return 0f;
            }
            float molarMass = AtmosphereFacade.MolarMassGPerMol((ushort)elementIdx);
            if (molarMass <= 0f)
            {
                return 0f;
            }
            return massKg * GramsPerKilogram / molarMass;
        }

        /// <summary>
        /// Takes <paramref name="targetMoles"/> out of one input network and into the internal
        /// volume, and returns how many moles were actually taken.
        ///
        /// The same proportional draw the pump uses: taking the same FRACTION of every tile's mass
        /// preserves the composition of a run that is itself mixed, so a mixer fed from a line
        /// that already carries two gases does not sort them.
        /// </summary>
        private float Draw(ConduitFlow gasFlow, int portCell, PipeNetworkReading source,
            float targetMoles)
        {
            if (targetMoles <= 0f || source.Cells == null || source.TotalMassKg <= 0f)
            {
                return 0f;
            }
            float availableMoles = AvailableMoles(portCell);
            if (availableMoles <= 0f)
            {
                return 0f;
            }
            float fraction = Mathf.Clamp01(targetMoles / availableMoles);
            if (fraction <= 0f)
            {
                return 0f;
            }

            float takenMoles = 0f;
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

                Element element = ElementLoader.FindElementByHash(taken.element);
                int elementIdx = element != null ? ElementLoader.elements.IndexOf(element) : -1;
                if (elementIdx < 0 || !AddToBuffer(elementIdx, taken.mass, taken.temperature))
                {
                    // No slot free, or an element the table does not know. Straight back where it
                    // came from at the temperature it left at -- the mixer refuses gas, it never
                    // loses it.
                    gasFlow.AddElement(cell, taken.element, taken.mass, taken.temperature,
                        taken.diseaseIdx, taken.diseaseCount);
                    continue;
                }
                takenMoles += MolesOf(elementIdx, taken.mass);
            }
            return takenMoles;
        }

        /// <summary>
        /// Adds one parcel to the internal volume and re-blends its temperature by HEAT CAPACITY
        /// (deviation 2 in <see cref="GasMixerConfig"/>): the new temperature is
        /// sum(m*c*T) / sum(m*c), which conserves energy across species with different specific
        /// heats where a mass-weighted average would not. Returns false when every slot is taken
        /// by some other species.
        /// </summary>
        private bool AddToBuffer(int elementIdx, float massKg, float temperatureK)
        {
            if (massKg <= 0f)
            {
                return true;
            }

            int slot = -1;
            for (int i = 0; i < BufferSlots; i++)
            {
                if (bufferElementIdx[i] == elementIdx)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0)
            {
                for (int i = 0; i < BufferSlots; i++)
                {
                    if (bufferElementIdx[i] < 0 || bufferMassKg[i] <= 0f)
                    {
                        slot = i;
                        break;
                    }
                }
            }
            if (slot < 0)
            {
                return false;
            }

            float existingCapacity = BufferHeatCapacityJPerK();
            float incomingCapacity = HeatCapacityJPerK(elementIdx, massKg);
            float totalCapacity = existingCapacity + incomingCapacity;
            if (totalCapacity > 0f)
            {
                bufferTemperatureK = (bufferTemperatureK * existingCapacity
                    + temperatureK * incomingCapacity) / totalCapacity;
            }
            else
            {
                bufferTemperatureK = temperatureK;
            }

            bufferElementIdx[slot] = elementIdx;
            bufferMassKg[slot] += massKg;
            return true;
        }

        private float BufferedMoles()
        {
            float moles = 0f;
            for (int i = 0; i < BufferSlots; i++)
            {
                if (bufferElementIdx[i] >= 0 && bufferMassKg[i] > 0f)
                {
                    moles += MolesOf(bufferElementIdx[i], bufferMassKg[i]);
                }
            }
            return moles;
        }

        private float BufferHeatCapacityJPerK()
        {
            float capacity = 0f;
            for (int i = 0; i < BufferSlots; i++)
            {
                if (bufferElementIdx[i] >= 0 && bufferMassKg[i] > 0f)
                {
                    capacity += HeatCapacityJPerK(bufferElementIdx[i], bufferMassKg[i]);
                }
            }
            return capacity;
        }

        private static float HeatCapacityJPerK(int elementIdx, float massKg)
        {
            if (massKg <= 0f || elementIdx < 0 || ElementLoader.elements == null
                || elementIdx >= ElementLoader.elements.Count)
            {
                return 0f;
            }
            Element element = ElementLoader.elements[elementIdx];
            if (element == null || element.specificHeatCapacity <= 0f)
            {
                return 0f;
            }
            // specificHeatCapacity is J/g/K, so mass has to be in grams. Same conversion the pump
            // uses when it turns its own work into a temperature rise.
            return massKg * GramsPerKilogram * element.specificHeatCapacity;
        }

        /// <summary>
        /// Empties as much of the internal volume into the output pipe as that pipe will take.
        ///
        /// A conduit tile holds ONE element (`ConduitFlow.AddElement` returns 0 outright when the
        /// element differs), so the species offered is whichever one the output tile already
        /// holds; when the tile is empty, the fullest slot goes first. The tile moves on down the
        /// network next tick and the other species follows it, which is how a two-gas blend
        /// travels a single-element pipe -- and why the RUN carries the dialled ratio while no
        /// individual tile does.
        /// </summary>
        private void Flush()
        {
            if (building == null)
            {
                return;
            }
            ConduitFlow gasFlow = Game.Instance?.gasConduitFlow;
            if (gasFlow == null)
            {
                return;
            }
            int outputCell = building.GetUtilityOutputCell();
            if (!gasFlow.HasConduit(outputCell))
            {
                return;
            }

            ConduitFlow.ConduitContents contents = gasFlow.GetContents(outputCell);
            int slot = -1;
            if (contents.mass > 0f && contents.element != SimHashes.Vacuum)
            {
                Element occupant = ElementLoader.FindElementByHash(contents.element);
                int occupantIdx = occupant != null
                    ? ElementLoader.elements.IndexOf(occupant)
                    : -1;
                for (int i = 0; i < BufferSlots; i++)
                {
                    if (bufferElementIdx[i] == occupantIdx && bufferMassKg[i] > 0f)
                    {
                        slot = i;
                        break;
                    }
                }
                if (slot < 0)
                {
                    // The tile is holding something this mixer is not carrying. Nothing can be
                    // delivered this tick; the blend waits rather than being dropped.
                    return;
                }
            }
            else
            {
                float best = 0f;
                for (int i = 0; i < BufferSlots; i++)
                {
                    if (bufferElementIdx[i] >= 0 && bufferMassKg[i] > best)
                    {
                        best = bufferMassKg[i];
                        slot = i;
                    }
                }
            }
            if (slot < 0)
            {
                return;
            }

            Element element = ElementLoader.elements[bufferElementIdx[slot]];
            if (element == null)
            {
                return;
            }
            float accepted = gasFlow.AddElement(outputCell, element.id, bufferMassKg[slot],
                bufferTemperatureK, 0, 0);
            if (accepted <= 0f)
            {
                return;
            }

            bufferMassKg[slot] -= accepted;
            MixedKg += accepted;
            if (bufferMassKg[slot] <= 0f)
            {
                bufferMassKg[slot] = 0f;
                bufferElementIdx[slot] = -1;
            }
        }

        // ----------------------------------------------------------------------------------
        // IThresholdSwitch -- the ratio dial, using vanilla's own slider UI for the same reason
        // the pump's displacement dial and the Purge Valve's setpoint do: ONI already has the
        // screen, the formatting and the save round-trip for a building with one numeric setting
        // and a live measured value. Stationeers exposes exactly one number here too.
        // ----------------------------------------------------------------------------------

        public float Threshold
        {
            get { return ratio1Percent; }
            set { ratio1Percent = Mathf.Clamp(value, 0f, MaxRatioPercent); }
        }

        /// <summary>
        /// Not meaningful for a mixer, which has no above/below behaviour, but part of the
        /// interface.
        /// </summary>
        public bool ActivateAboveThreshold
        {
            get { return true; }
            set { }
        }

        public float CurrentValue => MeasuredRatioPercent;

        public float RangeMin => 0f;

        public float RangeMax => MaxRatioPercent;

        public LocString Title => new LocString("Mix Ratio");

        public LocString ThresholdValueName => new LocString("Primary input share");

        public string AboveToolTip => "Blend at this ratio";

        public string BelowToolTip => "Blend at this ratio";

        public ThresholdScreenLayoutType LayoutType => ThresholdScreenLayoutType.SliderBar;

        public int IncrementScale => 1;

        public NonLinearSlider.Range[] GetRanges =>
            NonLinearSlider.GetDefaultRange(MaxRatioPercent);

        public float GetRangeMinInputField() => RangeMin;

        public float GetRangeMaxInputField() => RangeMax;

        public LocString ThresholdValueUnits() => new LocString("%");

        public string Format(float value, bool units)
        {
            string text = value.ToString("F0");
            return units ? text + " %" : text;
        }

        public float ProcessedSliderValue(float input) => Mathf.Round(input);

        public float ProcessedInputValue(float input) => input;
    }
}
