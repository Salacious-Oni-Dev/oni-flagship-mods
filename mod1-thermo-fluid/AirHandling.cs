using KSerialization;
using OniFramework;
using TUNING;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// AIR HANDLING: the three buildings a room's atmosphere needs once air is a real mixture --
    /// an intake that draws the room's air into a pipe, a diffuser that puts air back into the
    /// room, and a carbon scrubber that takes the carbon dioxide out in between.
    ///
    /// WHY VANILLA'S OWN GAS PUMP AND GAS VENT CANNOT DO THIS, established by measurement rather
    /// than by argument. The first `--mod1-airloop` run put a vanilla Gas Pump at the far end of
    /// the corridor and it moved nothing useful: `ElementConsumer` reads `Grid.Element` and
    /// `Grid.Mass`, which for a cell the mixture layer owns are vanilla's frozen view of it, not
    /// the real composition. The same is true in reverse for the Gas Vent, which writes vanilla
    /// mass into a cell whose real contents live somewhere else. A room whose air is a mixture
    /// needs machines that speak mixture.
    ///
    /// WHERE THE DRAFT ACTUALLY COMES FROM, and it is not these buildings. The custom SimDLL
    /// already moves gas between adjacent promoted cells down each species' own partial-pressure
    /// gradient every mixing tick -- `MixPair` in sim/gas_mixture.h, driven by
    /// `TickRoomPooledMixing` from `StepPhysics`, Dalton's law per species. That is the transport
    /// model; what was missing was anything able to create a gradient for it to act on. An intake
    /// at one end of a corridor lowers the pressure there, a diffuser at the other raises it, and
    /// the air in between moves -- a real draft, made of the simulation's own physics rather than
    /// a scripted conveyor. The gradient itself is readable through
    /// <see cref="AtmosphereFacade.TryGetDraft"/>, so a third-party mod can see the draft these
    /// buildings create without knowing they exist.
    ///
    /// THE ONE INTERIM IN HERE, named rather than hidden: taking mass OUT of the mixture and into
    /// a pipe is done as `ConvertToVanilla` followed by `RemoveVanillaMass`, because the native
    /// layer has an atomic mixture-to-vanilla message and an atomic vanilla-removal message but no
    /// single mixture-extraction message yet. The pair is made safe by asking for no more than
    /// `ReadComposition` says is actually there, so the clamp in the first message can never
    /// bite and leave the second removing mass that was never added. A real `ExtractGasSpecies`
    /// message is the correct fix and is a SimDLL job.
    ///
    /// MASS ISOLATION (container-mass-isolation): every path here either moves mass between the
    /// mixture layer and a pipe through the facade's own conserving messages, or between two pipe
    /// tiles with `ConduitFlow.RemoveElement`/`AddElement`, and anything a destination refuses
    /// goes straight back where it came from.
    /// </summary>
    internal static class AirHandling
    {
        internal const string AirIntakeId = "AirIntake";
        internal const string AirDiffuserId = "AirDiffuser";
        internal const string CarbonScrubberId = "CarbonScrubber";

        /// <summary>
        /// Clears vanilla's "Pipe Blocked" gate off a CONTINUOUS-FLOW building.
        ///
        /// `BuildingLoader.CreateBuildingComplete` bolts a <see cref="RequireOutputs"/> onto ANY
        /// building whose def declares an output conduit type (`UpdateComponentRequirement&lt;
        /// RequireOutputs&gt;(go, def.OutputConduitType != ConduitType.None)`) -- it is nothing to do with the building's own config, which is why it is
        /// invisible when reading these files. Its test is not "is the pipe full":
        ///
        ///     private bool OutputPipeIsEmpty() {
        ///         if (ignoreFullPipe) return true;
        ///         bool result = true;
        ///         if (connected) result = GetConduitFlow().IsConduitEmpty(utilityCell);
        ///         return result;
        ///     }
        ///
        /// ANY mass in the port tile reads as blocked. That is correct for a discrete-batch
        /// producer, whose output pipe is supposed to clear between batches. Every mass mover in
        /// this mod is the opposite: it keeps its output pipe non-empty on purpose, so the gate
        /// is tripped essentially all the time.
        ///
        /// Every such building would show "pipe blocked" on hover, and it is NOT cosmetic --
        /// `RequireOutputs.UpdatePipeRoomState` also does
        /// `operational.SetFlag(pipesHaveRoomFlag, flag)` on a `Flag.Type.Requirement`, so
        /// `Operational.IsOperational` goes false with it, and every component in this mod gates
        /// its tick on exactly that, so the buildings would run off a flickering flag.
        ///
        /// `ignoreFullPipe` is vanilla's own public escape hatch for this case. Same fix already
        /// applied to the augmented Gas/Liquid Pump in Patches.cs; the valves in
        /// PhaseChangeValves.cs take the harder route of deleting the component outright.
        /// </summary>
        internal static void AllowContinuousOutputFlow(GameObject go)
        {
            RequireOutputs requireOutputs = go.GetComponent<RequireOutputs>();
            if (requireOutputs != null)
            {
                requireOutputs.ignoreFullPipe = true;
            }
        }

        /// <summary>
        /// Registers the three buildings' display strings and their build-menu entries, the same
        /// way <see cref="PhaseChangeValves.Install"/> does for the valves, the pump and the
        /// mixer. Called from <c>Mod.OnLoad</c>.
        /// </summary>
        internal static void Install()
        {
            AddStrings(AirIntakeId, "Air Intake",
                "Draws a room's air into a pipe, down to a pressure you set -- the whole mixture, "
                + "in the proportions the room actually holds.",
                "Vanilla's Gas Pump moves whichever single element the game thinks is in the "
                + "cell. This moves real air: every gas present, in the ratio present, which is "
                + "the only way to extract from a room whose atmosphere is a mixture. It pumps "
                + "towards a TARGET PRESSURE rather than at a fixed rate, so it stops when the "
                + "room reaches it instead of emptying the tile it stands on. Set it lower than "
                + "a diffuser at the far end of the room and the difference between the two is a "
                + "standing draught the air travels down.");

            AddStrings(AirDiffuserId, "Air Diffuser",
                "Releases piped air into a room's atmosphere as a real mixture, up to a pressure "
                + "you set.",
                "The other end of a ventilation loop, and the same device as the Air Intake "
                + "pointed the other way. Whatever the pipe delivers is added to the room's "
                + "mixture rather than replacing it, so blended air arrives as air, and it stops "
                + "at the pressure you set rather than emptying the pipe into the room. Set it "
                + "higher than an intake at the far end and the room has a draught across it.");

            AddStrings(CarbonScrubberId, "Carbon Scrubber",
                "Separates carbon dioxide out of a passing air stream into a second pipe.",
                "Recirculating a room's air without cleaning it only spreads the carbon dioxide "
                + "around. This takes it out of the stream and puts it in its own pipe -- it does "
                + "not destroy it, because it has to go somewhere. That second pipe is the "
                + "player's problem, and that is the point.");

            ModUtil.AddBuildingToPlanScreen(new HashedString("HVAC"), AirIntakeId,
                BUILDINGS.PlanSubcategoryName.valves.ToString());
            ModUtil.AddBuildingToPlanScreen(new HashedString("HVAC"), AirDiffuserId,
                BUILDINGS.PlanSubcategoryName.valves.ToString());
            ModUtil.AddBuildingToPlanScreen(new HashedString("HVAC"), CarbonScrubberId,
                BUILDINGS.PlanSubcategoryName.valves.ToString());

            AddStrings(InlineCarbonSkimmerConfig.Id, "Inline Carbon Skimmer",
                "Vanilla's Carbon Skimmer, plumbed into a duct: carbon dioxide out of a passing "
                + "air stream and into water.",
                "The same machine ONI already ships -- one kilogram of water per three hundred "
                + "grams of carbon dioxide, out as polluted water -- with its intake moved from "
                + "the open room onto a pipe, because in a ventilation loop the air is in the "
                + "duct and not in the room. Everything that is not carbon dioxide passes "
                + "straight through. The carbon leaves in the water, which a Water Sieve turns "
                + "back into clean water and toxic sand: the loop closes on water and the carbon "
                + "ends up as a solid somebody has to deal with.");
            ModUtil.AddBuildingToPlanScreen(new HashedString("HVAC"),
                InlineCarbonSkimmerConfig.Id, BUILDINGS.PlanSubcategoryName.valves.ToString());

            // The skimmer puts its whole draw into the gas it discharges (CarbonSkimmer.cs), so
            // PowerHeat's body heat on top of that would bill the same watts twice.
            OniFramework.PowerHeat.Exempt(InlineCarbonSkimmerConfig.Id,
                "puts its whole draw into the gas it discharges (CarbonSkimmer.cs)");
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
    /// Room air in, pipe out. See <see cref="AirHandling"/> for why vanilla's Gas Pump cannot do
    /// this and where the draft comes from.
    /// </summary>
    public class AirIntakeConfig : IBuildingConfig
    {
        internal const float FullRateWatts = 240f;

        public override BuildingDef CreateBuildingDef()
        {
            // ONE TILE, to match the art. This was 1x2 with its port on the upper cell while
            // wearing vanilla's 1x1 Gas Vent animation, so the machine was drawn in one tile and
            // its pipe attached to the other -- it read as unplumbed on screen when nothing was
            // actually wrong with it. Reported from a screenshot, twice.
            //
            // Vanilla's own Gas Vent animation, because that is what this is -- a vent. It carries
            // the working_pre/working_loop/working_pst set, which the Operational state machine
            // below drives so the vent visibly runs only while it is actually moving gas, exactly
            // as vanilla's does. The art is borrowed from vanilla, not authored.
            BuildingDef def = BuildingTemplates.CreateBuildingDef(AirHandling.AirIntakeId, 1, 1,
                "ventgas_kanim", 30, 30f, BUILDINGS.CONSTRUCTION_MASS_KG.TIER1,
                MATERIALS.ALL_METALS, 1600f, BuildLocationRule.Anywhere,
                BUILDINGS.DECOR.PENALTY.TIER1, NOISE_POLLUTION.NOISY.TIER2);

            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = FullRateWatts;
            def.ExhaustKilowattsWhenActive = 0f;
            def.SelfHeatKilowattsWhenActive = 0f;

            // Output only. There is no input conduit: the input is the room.
            def.OutputConduitType = ConduitType.Gas;
            def.Floodable = false;
            def.ViewMode = OverlayModes.GasConduits.ID;
            def.AudioCategory = "Metal";
            def.PermittedRotations = PermittedRotations.R360;
            def.UtilityOutputOffset = new CellOffset(0, 0);
            def.PowerInputOffset = new CellOffset(0, 0);

            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.GasVentIDs,
                AirHandling.AirIntakeId);
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            BuildingConfigManager.Instance.IgnoreDefaultKComponent(typeof(RequiresFoundation),
                prefabTag);
            AirVentComponent vent = go.AddOrGet<AirVentComponent>();
            vent.Direction = VentDirection.Inward;
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
            Object.DestroyImmediate(go.GetComponent<ConduitConsumer>());
            Object.DestroyImmediate(go.GetComponent<ConduitDispenser>());
            AirHandling.AllowContinuousOutputFlow(go);
            go.GetComponent<KPrefabID>().AddTag(GameTags.OverlayInFrontOfConduits);
        }
    }

    /// <summary>
    /// Stationeers' PASSIVE VENT: an unpowered hole between a duct and a room that simply lets
    /// the two equalise. The Air Intake is the Active Vent -- powered, setpoint-driven; this is
    /// its counterpart, and the pair is how a Stationeers room is actually ventilated.
    /// </summary>
    public class AirDiffuserConfig : IBuildingConfig
    {
        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(AirHandling.AirDiffuserId, 1, 1,
                "ventgas_kanim", 30, 30f, BUILDINGS.CONSTRUCTION_MASS_KG.TIER1,
                MATERIALS.ALL_METALS, 1600f, BuildLocationRule.Anywhere,
                BUILDINGS.DECOR.PENALTY.TIER1, NOISE_POLLUTION.NONE);

            // NO POWER. A passive vent has none in Stationeers and needs none here: it moves gas
            // down a pressure difference that something else created.
            def.RequiresPowerInput = false;
            def.ExhaustKilowattsWhenActive = 0f;
            def.SelfHeatKilowattsWhenActive = 0f;

            def.InputConduitType = ConduitType.Gas;
            def.Floodable = false;
            def.ViewMode = OverlayModes.GasConduits.ID;
            def.AudioCategory = "Metal";
            def.UtilityInputOffset = new CellOffset(0, 0);

            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.GasVentIDs,
                AirHandling.AirDiffuserId);
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            BuildingConfigManager.Instance.IgnoreDefaultKComponent(typeof(RequiresFoundation),
                prefabTag);
            AirVentComponent vent = go.AddOrGet<AirVentComponent>();

            // A PASSIVE VENT, which is a different Stationeers device from the Active Vent the
            // Air Intake ports: no power, no setpoints, no direction. It equalises the duct and
            // the room, and that is its whole body.
            vent.Direction = VentDirection.Passive;
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            Object.DestroyImmediate(go.GetComponent<ConduitConsumer>());
            Object.DestroyImmediate(go.GetComponent<ConduitDispenser>());
            AirHandling.AllowContinuousOutputFlow(go);
            go.GetComponent<KPrefabID>().AddTag(GameTags.OverlayInFrontOfConduits);
        }
    }

    /// <summary>
    /// Which way an <see cref="AirVentComponent"/> moves gas. `VentDirection` (Stationeers).
    /// </summary>
    public enum VentDirection
    {
        /// <summary>Room to pipe -- an extract. `Inward` (Stationeers).</summary>
        Inward,

        /// <summary>Pipe to room -- a supply. `Outward` (Stationeers).</summary>
        Outward,

        /// <summary>
        /// Neither: a Stationeers PASSIVE VENT, which simply equalises the pipe and the room.
        ///
        /// Its whole body is `AtmosphereHelper.Mix(PipeNetwork.Atmosphere, worldAtmosphere, Gas)`
        /// -- no power, no setpoints, no direction. Gas goes whichever way
        /// the pressure difference sends it, which is what makes it the right partner for an
        /// Active Vent at the other end of a room: the active one sets the pressure it wants and
        /// the passive one simply lets the duct and the room find each other.
        /// </summary>
        Passive,
    }

    /// <summary>
    /// STATIONEERS' ACTIVE VENT, ported: a SETPOINT-SEEKING pump between a room's atmosphere and
    /// a pipe network, in either direction, with a limit on each side.
    ///
    /// This replaced a pair of fixed-rate machines, and the reason is worth keeping. A vent that
    /// moves a fixed number of kilograms a second has no idea when to stop, so every way it can
    /// go wrong has to be patched separately: the extract dug its own tile down to 0 kPa and
    /// needed a minimum-mass floor; the supply pushed the corridor to 192 kPa and needed an
    /// overpressure ceiling; the ceiling then throttled it to a third of its rate and needed its
    /// own correction. Every one of those is the same missing idea -- the machine does not know
    /// what pressure it is aiming for. Stationeers' Active Vent does, and the two limits it
    /// carries are exactly the two that were being reinvented one bug at a time:
    ///
    /// - `ExternalPressure`: the pressure the ROOM should end up at. An inward vent stops drawing
    ///   once the room falls to it; an outward vent stops filling once the room reaches it.
    /// - `InternalPressure`: the pressure the PIPE is allowed to reach. An inward vent stops when
    ///   the pipe is that full; an outward vent stops when the pipe is that empty.
    ///
    /// THE ALGORITHM IS STATIONEERS' OWN: its active vent's `PumpGasToPipe` / `PumpGasToWorld`,
    /// ported term for term. Inward: the moles
    /// allowed by the room's own excess over its setpoint, capped by the rate, and the moles the
    /// pipe can still accept before ITS setpoint -- whichever is smaller. Outward: the moles the
    /// pipe holds above its setpoint, and the moles the room can still accept before its own --
    /// again whichever is smaller. Both are `IdealGas.Quantity` conversions between a pressure
    /// difference and an amount of gas, which is why <see cref="AtmosphereFacade
    /// .MolesFromPressurePa"/> exists.
    ///
    /// A DRAUGHT IS WHAT TWO OF THESE MAKE. Set an inward vent at one end of a room to a lower
    /// external pressure than an outward vent at the other, and the room has a pressure gradient
    /// across it; the custom SimDLL's own per-species transport (`MixPair`, sim/gas_mixture.h)
    /// carries the air down that gradient. Nothing scripts a flow -- the vents set the boundary
    /// conditions and the simulation does the rest, which is exactly how it works in Stationeers.
    ///
    /// TWO ONI-SPECIFIC BEHAVIOURS THAT ARE NOT IN STATIONEERS, both forced by ONI rather than
    /// chosen. A conduit tile holds ONE element and `ConduitFlow.AddElement` refuses a second, so
    /// an inward vent cannot move a whole mixture in one tick and instead moves one species per
    /// tick, whichever is furthest behind its share of what has been drawn -- which reproduces
    /// the room's real composition over a run, trace gases included. And both directions move
    /// mass over a small FOOTPRINT rather than a single tile, the same way vanilla's own Gas Pump
    /// has a consumption radius -- one tile stalls, because the pump can only take what that tile
    /// holds this tick.
    ///
    /// WHAT THE VENT READS AND WHAT IT BREATHES THROUGH ARE TWO DIFFERENT SETS, or there is no
    /// draught: a vent that reaches twenty-five cells is a vent nothing ever has to flow toward.
    /// The reading is the REAL ROOM, asked of the sim (`OniFramework.SimRooms`, one call, no
    /// allocation, wall-aware, so it can no longer draw out of the sealed room next door the way
    /// a diamond could), and the mass moves through the vent's own tile and its four neighbours.
    /// The port starves, the vent throttles, the gradient steepens, and `MixPair` carries gas
    /// across the room to it. See `CollectAndMeasure` for why widening the footprint to the whole
    /// room -- the obvious reading of "use the room aggregate" -- would have destroyed the
    /// draught rather than created it.
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class AirVentComponent : KMonoBehaviour, ISim200ms, IThresholdSwitch
    {
        private const string LogPrefix = "[Mod1ThermoFluid] AIRVENT: ";

        /// <summary>
        /// Species slots in the extraction ledger. A room's atmosphere in practice holds a
        /// handful of gases; anything past this simply never gets its own deficit tracked and
        /// falls back to being drawn whenever it happens to match the pipe.
        /// </summary>
        private const int LedgerSlots = 8;

        /// <summary>
        /// The rate cap, as the pressure this vent can move in one second -- Stationeers'
        /// `PressurePerTick`, which is a per-prefab serialized field there and a constant here.
        /// Five atmospheres per second: the setpoints, not the rate, should be what the player
        /// tunes, and at one atmosphere the rate was quietly doing the tuning instead. A conduit
        /// tile is ten litres against a twenty-five cubic metre room, so a rate expressed as a
        /// pressure per second is far more restrictive on the pipe side than it reads.
        /// </summary>
        private const float RatePressurePerSecondPa = 506625f;

        /// <summary>How far the vent reaches into the room, in tiles. Vanilla's Gas Pump has one.</summary>
        private const int Radius = 3;

        internal const float DefaultExternalPressurePa = 101325f;

        /// <summary>Upper bound of the ROOM setpoint dial. A room pressure, so a room-sized range.</summary>
        private const float MaxSetpointPa = 500000f;

        /// <summary>
        /// How full this vent is allowed to make its own DUCT, in pascals.
        ///
        /// It is a pipe rating, not a room pressure, and conflating the two was throttling the
        /// whole rig. At the old 500 kPa the return duct -- seven tiles, seventy litres -- held
        /// about 0.42 kg before the intake had to stop, so the intake drew exactly 1.678 kg in
        /// every run regardless of what the room was doing, and everything downstream starved on
        /// a trickle. Five megapascals is well under `PipeNetworkFacade.MaxGasPipePressurePa` and
        /// lets the duct be a duct.
        /// </summary>
        private const float DefaultDuctPressurePa = 5000000f;

        private const float GramsPerKilogram = 1000f;

        /// <summary>
        /// Which way this vent moves gas. Set by the building config rather than by a mode dial:
        /// Stationeers ships one device with a mode, and ONI has no in-world mode control to bind
        /// it to, so this mod ships the same device as two presets -- an Air Intake and an Air
        /// Diffuser -- that a player can tell apart on the build menu.
        /// </summary>
        /// <summary>
        /// PUBLIC AND `[SerializeField]`, and it has to be. Unity's `Instantiate` copies only the
        /// fields IT serializes -- public ones, or ones marked `[SerializeField]`. This started
        /// out `internal`, so the value a building config set on the PREFAB never reached the
        /// instances cloned from it: both Air Diffusers in the live rig came up as inward
        /// extracts with the default pipe limit, released 0.000 kg, and looked for all the world
        /// like a broken pump algorithm.
        /// </summary>
        [SerializeField] public VentDirection Direction = VentDirection.Inward;

        /// <summary>The pressure the ROOM should settle at. `ExternalPressure` (Stationeers).</summary>
        [Serialize][SerializeField] public float externalPressurePa = DefaultExternalPressurePa;

        /// <summary>The pressure the PIPE is allowed to reach. `InternalPressure` (Stationeers).</summary>
        [Serialize][SerializeField] public float internalPressurePa = DefaultDuctPressurePa;

        /// <summary>
        /// How much of each species an inward vent has taken, so the long-run proportions can be
        /// held to the room's own composition. Serialized because a save/load that forgot it
        /// would let the vent start sorting the mixture again.
        /// </summary>
        [Serialize] private int[] ledgerElementIdx = NewEmptyLedger();

        [Serialize] private float[] ledgerKg = new float[LedgerSlots];

        /// <summary>Total mass this vent has moved out of the room.</summary>
        public float DrawnKg { get; private set; }

        /// <summary>Total mass this vent has released into the room.</summary>
        public float ReleasedKg { get; private set; }

        public float MeasuredKgPerSecond { get; private set; }

        private Building building;
        private Operational operational;

        /// <summary>Scratch, reused every tick so a per-tick sweep allocates nothing.</summary>
        private readonly System.Collections.Generic.List<int> reach =
            new System.Collections.Generic.List<int>();

        private readonly System.Collections.Generic.Dictionary<ushort, float> massBySpecies =
            new System.Collections.Generic.Dictionary<ushort, float>();

        /// <summary>
        /// What the PORT holds, as opposed to what the room holds. Scratch, reused every tick.
        ///
        /// The two are different questions and the vent needs both. The room's composition says
        /// what the atmosphere IS -- the setpoint is compared against it and the species rotation
        /// aims at it. The port's composition says what this vent can physically move THIS TICK,
        /// because mass leaves through the tiles the vent is actually touching. Using the room's
        /// number for the second would let the vent draw eight kilograms out of a port holding
        /// two hundred grams, which is not a throughput limit being ignored, it is mass being
        /// invented.
        /// </summary>
        private readonly System.Collections.Generic.Dictionary<ushort, float> portMassBySpecies =
            new System.Collections.Generic.Dictionary<ushort, float>();

        /// <summary>
        /// Scratch for <see cref="GasMixtureFacade.ReadComposition"/>, which fills the caller's
        /// buffers instead of allocating an array per cell. Fields rather than locals because
        /// that is the whole point: this vent reads a composition several times per cell per
        /// tick, and the allocating overload would hand the GC a fresh array every time. Sized by
        /// the sim's own per-cell species cap, which is a compile-time constant.
        /// </summary>
        private readonly ushort[] compositionSpecies =
            new ushort[GasMixtureFacade.MaxSpeciesPerCell];

        private readonly float[] compositionMassKg =
            new float[GasMixtureFacade.MaxSpeciesPerCell];

        private static int[] NewEmptyLedger()
        {
            var slots = new int[LedgerSlots];
            for (int i = 0; i < LedgerSlots; i++)
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
            if (ledgerElementIdx == null || ledgerElementIdx.Length != LedgerSlots)
            {
                ledgerElementIdx = NewEmptyLedger();
            }
            if (ledgerKg == null || ledgerKg.Length != LedgerSlots)
            {
                ledgerKg = new float[LedgerSlots];
            }
        }

        public void Sim200ms(float dt)
        {
            try
            {
                MeasuredKgPerSecond = 0f;
                Tick(dt);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(LogPrefix + "vent tick failed: " + e);
            }

            // Tell Operational whether anything actually moved, which is what drives the
            // working animation. A vent that is powered but has nothing to move sits still,
            // exactly as a vanilla one does.
            if (operational != null)
            {
                operational.SetActive(MeasuredKgPerSecond > 0f);
            }
        }

        private void Tick(float dt)
        {
            if (building == null || dt <= 0f)
            {
                return;
            }
            // A passive vent has no Operational to satisfy -- it is not a machine, it is a hole.
            if (Direction != VentDirection.Passive && operational != null
                && !operational.IsOperational)
            {
                return;
            }

            ConduitFlow gasFlow = Game.Instance?.gasConduitFlow;
            if (gasFlow == null)
            {
                return;
            }

            int pipeCell = Direction == VentDirection.Inward
                ? building.GetUtilityOutputCell()
                : building.GetUtilityInputCell();
            if (!gasFlow.HasConduit(pipeCell))
            {
                return;
            }
            if (!PipeNetworkFacade.TryReadNetwork(pipeCell, PipeContentType.Gas,
                    out PipeNetworkReading pipe))
            {
                return;
            }

            int origin = Grid.PosToCell(this);
            if (!Grid.IsValidCell(origin))
            {
                return;
            }
            int roomCellCount;
            float roomPressurePa;
            if (!CollectAndMeasure(origin, out roomPressurePa, out roomCellCount))
            {
                return;
            }

            float roomVolumeM3 = roomCellCount * PipeNetworkFacade.CellVolumeM3;
            float pipeVolumeM3 = pipe.CellCount * GasMixtureFacade.GasConduitVolumeM3;
            if (roomVolumeM3 <= 0f || pipeVolumeM3 <= 0f)
            {
                return;
            }

            float roomTemperatureK = Grid.Temperature[origin];
            if (roomTemperatureK <= 0f)
            {
                return;
            }

            // Stationeers blends the two sides' temperatures by mole count and sizes both halves
            // of the transfer against that single number, so a hot pipe emptying into a cold room
            // does not get two different answers for the same parcel.
            float pipeTemperatureK = pipe.TemperatureK > 0f ? pipe.TemperatureK : roomTemperatureK;
            float blendTemperatureK = (roomTemperatureK + pipeTemperatureK) * 0.5f;

            float ratePa = RatePressurePerSecondPa * dt;
            if (Direction == VentDirection.Passive)
            {
                Equalise(gasFlow, pipeCell, pipe, roomPressurePa, roomVolumeM3, pipeVolumeM3,
                    blendTemperatureK, dt);
                return;
            }
            if (Direction == VentDirection.Inward)
            {
                PumpToPipe(gasFlow, pipeCell, pipe, roomPressurePa, roomVolumeM3, pipeVolumeM3,
                    blendTemperatureK, ratePa, dt);
            }
            else
            {
                PumpToRoom(gasFlow, pipeCell, pipe, roomPressurePa, roomVolumeM3, pipeVolumeM3,
                    blendTemperatureK, ratePa, dt);
            }
        }

        /// <summary>
        /// Stationeers' PASSIVE VENT, ported: equalise the pipe and the room, whichever way that
        /// happens to send the gas.
        ///
        /// The moles that equalise two volumes at different pressures is the pressure difference
        /// divided by the sum of their per-mole pressures -- the same arithmetic the Gas Mixer's
        /// own rate cap uses, and the reason <see cref="AtmosphereFacade.PressurePerMolePa"/> is
        /// public. No setpoint appears anywhere in it, because a passive vent has none: it is a
        /// hole between a duct and a room with a rate limit on it.
        /// </summary>
        private void Equalise(ConduitFlow gasFlow, int pipeCell, PipeNetworkReading pipe,
            float roomPressurePa, float roomVolumeM3, float pipeVolumeM3, float temperatureK,
            float dt)
        {
            float perMolePipe = AtmosphereFacade.PressurePerMolePa(temperatureK, pipeVolumeM3);
            float perMoleRoom = AtmosphereFacade.PressurePerMolePa(temperatureK, roomVolumeM3);
            float sum = perMolePipe + perMoleRoom;
            if (sum <= 0f)
            {
                return;
            }

            // NO RATE CLAMP HERE, deliberately. Stationeers' passive vent is
            // `AtmosphereHelper.Mix`, which EQUALISES -- the moles that level two volumes, in one
            // go. Clamping that to a pressure-per-second the way the active vent is clamped looks
            // reasonable and is badly wrong at these volumes: a conduit tile is 10 litres against
            // a 25 m3 room, so the duct's per-mole pressure is two hundred times the room's, the
            // difference runs to millions of pascals, and a 101 kPa/s clamp throttled the vent to
            // about one mole a tick. Measured: the mixer put 33 kg into the duct and the vents
            // returned 1.5 kg of it, with 131 kg standing in the pipes. A passive vent is a hole,
            // and a hole does not meter.
            // THE RATE CLAMP STAYS, measured both ways. Removing it -- on the reasoning that
            // Stationeers' Mix equalises in one go -- made the room WORSE, not better: with the
            // duct sometimes below the room, an unmetered passive vent strips the room back up
            // the pipe just as fast as it fills it, and the corridor fell to 6.3 kPa of oxygen
            // against 15.7 with the clamp in place. A vent that equalises a 10-litre pipe against
            // a 25 m3 room in one tick is not a vent, it is a step change.
            float differencePa = pipe.PressurePa - roomPressurePa;
            float rateLimitPa = RatePressurePerSecondPa * dt;
            if (differencePa > 0f)
            {
                // Duct is the higher side: it vents into the room.
                ReleaseToRoom(gasFlow, pipeCell, Mathf.Min(differencePa, rateLimitPa) / sum,
                    temperatureK, dt);
            }
            else if (differencePa < 0f)
            {
                // Room is the higher side: it flows back up the duct. A passive vent does this
                // too, and refusing to would make it a one-way valve, which it is not.
                DrawToPipe(gasFlow, pipeCell, Mathf.Min(-differencePa, rateLimitPa) / sum,
                    temperatureK, dt);
            }
        }

        /// <summary>
        /// `PumpGasToPipe` (Stationeers), ported: the room's excess over its own setpoint capped by
        /// the rate, against the headroom the pipe has left before ITS setpoint, whichever is
        /// less. This is what makes an extract stop instead of digging a hole.
        /// </summary>
        private void PumpToPipe(ConduitFlow gasFlow, int pipeCell, PipeNetworkReading pipe,
            float roomPressurePa, float roomVolumeM3, float pipeVolumeM3, float temperatureK,
            float ratePa, float dt)
        {
            float roomExcessPa = roomPressurePa - externalPressurePa;
            if (roomExcessPa <= 0f)
            {
                return;
            }
            float allowedByRoom = AtmosphereFacade.MolesFromPressurePa(
                Mathf.Min(ratePa, roomExcessPa), roomVolumeM3, temperatureK);
            float allowedByPipe = AtmosphereFacade.MolesFromPressurePa(
                internalPressurePa - pipe.PressurePa, pipeVolumeM3, temperatureK);
            float transferMoles = Mathf.Min(allowedByRoom, allowedByPipe);
            if (transferMoles <= 0f)
            {
                return;
            }

            // ONE SPECIES PER TICK, chosen so the LONG RUN matches the room's composition. Taking
            // a fixed fraction of every species in the same tick is the obvious approach and does
            // not work: a conduit tile holds one element and `AddElement` returns 0 outright for a
            // second, so whichever species was listed first won every tick. The live run drew
            // 5.042 kg out of air that was 1% carbon dioxide and extracted 0.000 kg of it.
            ushort speciesIdx = ChooseSpecies();
            if (speciesIdx == ushort.MaxValue || speciesIdx >= ElementLoader.elements.Count)
            {
                return;
            }
            Element element = ElementLoader.elements[speciesIdx];
            if (element == null)
            {
                return;
            }
            // THE PORT'S OWN STOCK, not the room's. The room may hold eight kilograms of this
            // species; what can leave this tick is what the tiles the vent is touching hold, and
            // the proportional removal below spreads exactly `accepted` back across those same
            // tiles. Using the room's total here would take a share of cells the loop never
            // visits, and the difference would be mass this vent created.
            portMassBySpecies.TryGetValue(speciesIdx, out float availableKg);
            if (availableKg <= 0f)
            {
                return;
            }

            float takeKg = Mathf.Min(
                transferMoles * AtmosphereFacade.MolarMassGPerMol(speciesIdx) / GramsPerKilogram,
                availableKg);
            if (takeKg <= 0f)
            {
                return;
            }

            // INTO ANY TILE ON THE OUTLET NETWORK THAT WILL TAKE IT, not just the one this vent is
            // bolted to.
            //
            // This is what was starving the trace gases, and through them the whole carbon loop. A
            // conduit tile holds one element, so the outlet tile almost always already holds
            // nitrogen or oxygen; the species picker then had to choose that species or be
            // refused outright, and carbon dioxide -- one percent of the room -- never got a turn
            // in a hundred seconds. The skimmer downstream consequently scrubbed 0.000 kg, the
            // water loop never cycled, and the sieve sat blocked. One refusal, four symptoms.
            //
            // Reaching along the vent's OWN outlet network is the same move the skimmer makes at
            // its inlet, and it is what a duct physically is: the vent is connected to all of it,
            // not to one tile of it.
            float accepted = AddToNetwork(gasFlow, pipeCell, element.id, takeKg,
                Grid.Temperature[reach[0]]);
            if (accepted <= 0f)
            {
                // The pipe tile is full, or holds a different species this tick. Nothing leaves
                // the room at all -- the vent stalls rather than losing air.
                return;
            }

            // Spread across the reach in proportion to what each cell holds of this species, so no
            // one tile is emptied while its neighbours stay full. Mixture -> nowhere, in two
            // steps, asking for exactly what the composition says is there so the atomic message's
            // own clamp can never bite (see AirHandling's class doc).
            float shareFraction = accepted / availableKg;
            for (int c = 0; c < reach.Count; c++)
            {
                int cell = reach[c];
                int slots = GasMixtureFacade.ReadComposition(cell, compositionSpecies,
                    compositionMassKg);
                for (int i = 0; i < slots; i++)
                {
                    if (compositionSpecies[i] != speciesIdx || compositionMassKg[i] <= 0f)
                    {
                        continue;
                    }
                    float fromThisCell = compositionMassKg[i] * shareFraction;
                    if (fromThisCell <= 0f)
                    {
                        continue;
                    }
                    float cellTemperatureK = Grid.Temperature[cell];
                    GasMixtureFacade.ConvertToVanilla(cell, speciesIdx, fromThisCell,
                        cellTemperatureK);
                    GasMixtureFacade.RemoveVanillaMass(cell, fromThisCell);
                }
            }

            RecordExtraction(speciesIdx, accepted);
            DrawnKg += accepted;
            MeasuredKgPerSecond = accepted / dt;
        }

        /// <summary>
        /// `PumpGasToWorld` (Stationeers), ported: what the pipe holds above its own setpoint,
        /// against what the room can still accept before ITS setpoint, whichever is less. This is
        /// what makes a supply stop at one atmosphere instead of at whatever the pipe had.
        /// </summary>
        private void PumpToRoom(ConduitFlow gasFlow, int pipeCell, PipeNetworkReading pipe,
            float roomPressurePa, float roomVolumeM3, float pipeVolumeM3, float temperatureK,
            float ratePa, float dt)
        {
            float pipeExcessMoles = AtmosphereFacade.MolesFromPressurePa(
                pipe.PressurePa - internalPressurePa, pipeVolumeM3, temperatureK);
            float roomHeadroomPa = Mathf.Clamp(
                Mathf.Min(ratePa, externalPressurePa - roomPressurePa), 0f, ratePa);
            float roomHeadroomMoles = AtmosphereFacade.MolesFromPressurePa(roomHeadroomPa,
                roomVolumeM3, temperatureK);
            float transferMoles = Mathf.Min(pipeExcessMoles, roomHeadroomMoles);
            ReleaseToRoom(gasFlow, pipeCell, transferMoles, temperatureK, dt);
        }

        /// <summary>Moves <paramref name="transferMoles"/> out of the duct and into the room.</summary>
        private void ReleaseToRoom(ConduitFlow gasFlow, int pipeCell, float transferMoles,
            float temperatureK, float dt)
        {
            if (transferMoles <= 0f)
            {
                return;
            }

            // FROM ANYWHERE ON THE DUCT, not only the tile this vent sits on. The same defect
            // that stalled the intake and the skimmer, found last on the release side: the supply
            // duct was holding 137 kg while the diffusers delivered 0.343 kg, because the gas
            // happened to be a few tiles along the run and the vent only ever looked underneath
            // itself. A vent is connected to the duct.
            int sourceCell = FindGasOnNetwork(gasFlow, pipeCell);
            if (sourceCell == Grid.InvalidCell)
            {
                return;
            }
            ConduitFlow.ConduitContents contents = gasFlow.GetContents(sourceCell);
            Element element = ElementLoader.FindElementByHash(contents.element);
            int speciesIdx = element != null ? ElementLoader.elements.IndexOf(element) : -1;
            if (speciesIdx < 0)
            {
                return;
            }

            float takeKg = Mathf.Min(
                transferMoles * AtmosphereFacade.MolarMassGPerMol((ushort)speciesIdx)
                    / GramsPerKilogram,
                contents.mass);
            if (takeKg <= 0f)
            {
                return;
            }

            ConduitFlow.ConduitContents taken = gasFlow.RemoveElement(sourceCell, takeKg);
            if (taken.mass <= 0f)
            {
                return;
            }

            // Spread over the reach by how much room each cell has left, so the emptiest tiles get
            // the most -- which is both what a diffuser physically does and the thing that levels
            // a corridor rather than building a pocket at one end of it.
            //
            // HEADROOM IS A WEIGHT HERE, NEVER A GATE. It used to be both, and being both was a
            // deadlock: `CellHeadroomKg` measures a cell against THIS VENT'S OWN
            // `externalPressurePa`, which defaults to one atmosphere, and the showcase corridor is
            // charged to exactly one atmosphere. So every reach cell read zero headroom on the
            // first tick, the whole parcel went straight back down the pipe, and a PASSIVE vent --
            // a hole, which has no setpoint at all -- refused to vent a duct standing at tens of
            // megapascals into a room at one hundred kilopascals. It showed up as a diffuser
            // pinned at EXACTLY 0.193 kg released across three separate runs while the intake drew
            // 14-20 kg and the supply line climbed to 47 kg: whatever slipped through before the
            // room reached its charge, and nothing afterwards, forever.
            //
            // The stopping condition a passive vent should have is the one `Equalise` already
            // applies -- flow ceases when the duct's pressure meets the room's -- and an ACTIVE
            // supply is already held to its setpoint by `PumpToRoom`'s own `roomHeadroomPa` term
            // before it ever calls this. Gating a second time here added nothing to the active
            // case and broke the passive one.
            //
            // When no cell is under the setpoint the fallback weights by RECIPROCAL PRESSURE
            // rather than splitting evenly, because the levelling property is the point: the
            // thinnest tile in the reach should still take the largest share whether or not the
            // room as a whole happens to sit above one atmosphere.
            float totalWeight = 0f;
            for (int c = 0; c < reach.Count; c++)
            {
                totalWeight += Mathf.Max(0f, CellHeadroomKg(reach[c], temperatureK));
            }
            bool byHeadroom = totalWeight > 0f;
            if (!byHeadroom)
            {
                for (int c = 0; c < reach.Count; c++)
                {
                    totalWeight += ReciprocalPressureWeight(reach[c]);
                }
            }
            if (totalWeight <= 0f)
            {
                // Genuinely nowhere to put it -- no reach at all. Straight back into the pipe
                // rather than lost, and if the pipe will not take it back, kept rather than
                // silently destroyed: MATTER IS CONSERVED is a project rule, and
                // `ConduitFlow.AddElement` returns how much it actually accepted.
                float returned = gasFlow.AddElement(sourceCell, taken.element, taken.mass,
                    taken.temperature, 0, 0);
                if (returned < taken.mass)
                {
                    Debug.LogWarning(LogPrefix + "could not return "
                        + (taken.mass - returned).ToString("0.######")
                        + " kg of " + taken.element + " to the duct; it was dropped");
                }
                return;
            }

            // Straight into the MIXTURE, not vanilla's single-element layer: air arriving in a
            // room whose atmosphere is a mixture has to join that mixture.
            for (int c = 0; c < reach.Count; c++)
            {
                float weight = byHeadroom
                    ? Mathf.Max(0f, CellHeadroomKg(reach[c], temperatureK))
                    : ReciprocalPressureWeight(reach[c]);
                float shareKg = taken.mass * (weight / totalWeight);
                if (shareKg <= 0f)
                {
                    continue;
                }
                GasMixtureFacade.Inject(reach[c], speciesIdx, shareKg, taken.temperature);
            }

            ReleasedKg += taken.mass;
            MeasuredKgPerSecond = taken.mass / dt;
        }

        /// <summary>
        /// Moves <paramref name="transferMoles"/> out of the room and into the duct, for a passive
        /// vent whose room is the higher-pressure side. Reuses the inward pump's own machinery --
        /// the species rotation and the proportional spread across the reach -- because those are
        /// forced by ONI's single-element conduits regardless of what drives the flow.
        /// </summary>
        private void DrawToPipe(ConduitFlow gasFlow, int pipeCell, float transferMoles,
            float temperatureK, float dt)
        {
            if (transferMoles <= 0f)
            {
                return;
            }
            ushort speciesIdx = ChooseSpecies();
            if (speciesIdx == ushort.MaxValue || speciesIdx >= ElementLoader.elements.Count)
            {
                return;
            }
            Element element = ElementLoader.elements[speciesIdx];
            if (element == null)
            {
                return;
            }
            // THE PORT'S OWN STOCK, not the room's. The room may hold eight kilograms of this
            // species; what can leave this tick is what the tiles the vent is touching hold, and
            // the proportional removal below spreads exactly `accepted` back across those same
            // tiles. Using the room's total here would take a share of cells the loop never
            // visits, and the difference would be mass this vent created.
            portMassBySpecies.TryGetValue(speciesIdx, out float availableKg);
            if (availableKg <= 0f)
            {
                return;
            }
            float takeKg = Mathf.Min(
                transferMoles * AtmosphereFacade.MolarMassGPerMol(speciesIdx) / GramsPerKilogram,
                availableKg);
            if (takeKg <= 0f)
            {
                return;
            }
            float accepted = AddToNetwork(gasFlow, pipeCell, element.id, takeKg, temperatureK);
            if (accepted <= 0f)
            {
                return;
            }
            float shareFraction = accepted / availableKg;
            for (int c = 0; c < reach.Count; c++)
            {
                int cell = reach[c];
                int slots = GasMixtureFacade.ReadComposition(cell, compositionSpecies,
                    compositionMassKg);
                for (int i = 0; i < slots; i++)
                {
                    if (compositionSpecies[i] != speciesIdx || compositionMassKg[i] <= 0f)
                    {
                        continue;
                    }
                    float fromThisCell = compositionMassKg[i] * shareFraction;
                    if (fromThisCell <= 0f)
                    {
                        continue;
                    }
                    GasMixtureFacade.ConvertToVanilla(cell, speciesIdx, fromThisCell,
                        Grid.Temperature[cell]);
                    GasMixtureFacade.RemoveVanillaMass(cell, fromThisCell);
                }
            }
            RecordExtraction(speciesIdx, accepted);
            DrawnKg += accepted;
            MeasuredKgPerSecond = accepted / dt;
        }

        /// <summary>
        /// Roughly how much more mass one cell could take before it reaches the external setpoint,
        /// in kilograms. Approximate on purpose: it is a WEIGHTING for spreading one parcel over
        /// several cells, not a limit -- the limit is the setpoint arithmetic above, which is
        /// exact.
        /// </summary>
        /// <summary>
        /// The share weight for a cell when NO cell in the reach is below this vent's setpoint --
        /// the reciprocal of its pressure, so the thinnest tile still takes the largest share.
        ///
        /// The floor of one pascal is there for a vacuum cell, whose reciprocal would otherwise be
        /// infinite and would take the entire parcel; at one pascal it takes almost all of it,
        /// which is the right answer for a hole venting into a vacuum.
        /// </summary>
        private static float ReciprocalPressureWeight(int cell)
        {
            if (!GasMixtureFacade.TryGetPressure(cell, out float pressurePa))
            {
                pressurePa = 0f;
            }
            return 1f / Mathf.Max(1f, pressurePa);
        }

        private float CellHeadroomKg(int cell, float temperatureK)
        {
            if (!GasMixtureFacade.TryGetPressure(cell, out float pressurePa))
            {
                pressurePa = 0f;
            }
            float headroomPa = externalPressurePa - pressurePa;
            if (headroomPa <= 0f)
            {
                return 0f;
            }
            return AtmosphereFacade.MolesFromPressurePa(headroomPa,
                PipeNetworkFacade.CellVolumeM3, temperatureK);
        }

        /// <summary>
        /// The first tile on <paramref name="pipeCell"/>'s network that actually holds gas, or
        /// <c>Grid.InvalidCell</c>. The reading counterpart to <see cref="AddToNetwork"/>.
        /// </summary>
        private static int FindGasOnNetwork(ConduitFlow gasFlow, int pipeCell)
        {
            ConduitFlow.ConduitContents own = gasFlow.GetContents(pipeCell);
            if (own.mass > 0f && own.element != SimHashes.Vacuum)
            {
                return pipeCell;
            }
            if (!PipeNetworkFacade.TryGetNetworkCells(pipeCell, PipeContentType.Gas,
                    out int[] cells))
            {
                return Grid.InvalidCell;
            }
            for (int i = 0; i < cells.Length; i++)
            {
                ConduitFlow.ConduitContents contents = gasFlow.GetContents(cells[i]);
                if (contents.mass > 0f && contents.element != SimHashes.Vacuum)
                {
                    return cells[i];
                }
            }
            return Grid.InvalidCell;
        }

        /// <summary>
        /// Pushes into the first tile of <paramref name="pipeCell"/>'s network that will accept
        /// this species, preferring the vent's own tile so a short run behaves exactly as before.
        /// Returns what was actually accepted.
        /// </summary>
        private static float AddToNetwork(ConduitFlow gasFlow, int pipeCell, SimHashes element,
            float massKg, float temperatureK)
        {
            float accepted = gasFlow.AddElement(pipeCell, element, massKg, temperatureK, 0, 0);
            if (accepted > 0f)
            {
                return accepted;
            }
            if (!PipeNetworkFacade.TryGetNetworkCells(pipeCell, PipeContentType.Gas,
                    out int[] cells))
            {
                return 0f;
            }
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == pipeCell)
                {
                    continue;
                }
                accepted = gasFlow.AddElement(cells[i], element, massKg, temperatureK,
                    0, 0);
                if (accepted > 0f)
                {
                    return accepted;
                }
            }
            return 0f;
        }

        /// <summary>
        /// THE ROOM IS WHAT THE VENT READS; THE PORT IS WHAT IT MOVES MASS THROUGH. Fills
        /// <see cref="massBySpecies"/> with the ROOM's composition, <see cref="portMassBySpecies"/>
        /// and <see cref="reach"/> with the vent's own local footprint, and returns the room's
        /// mean pressure and cell count. False when there is nothing to work with.
        ///
        /// <b>THIS IS WHAT LETS A DRAUGHT EXIST.</b> If one radius-3 diamond did both jobs -- the
        /// reading AND the set of cells mass enters and leaves through -- nothing would ever have
        /// to flow: the vent would take gas instantly from twenty-five cells at once, through
        /// walls (a diamond is not wall-aware) and out of the far side of a door. Widening that set
        /// to the whole room would be worse still: an entire room
        /// drained simultaneously from every tile is a room in which a pressure gradient can never
        /// form, and a gradient is the only thing <c>MixPair</c> responds to.
        ///
        /// So the two jobs are separated, which is the shape Stationeers actually has. A device
        /// knows the atmosphere it is plumbed into, and moves matter through its own port; the
        /// simulation carries gas across the room to that port, down the partial-pressure gradient
        /// the port itself creates by emptying. That transport is real and already ours:
        /// <c>MixPair</c> (<c>sim/gas_mixture.h</c>) computes each species' partial pressure on
        /// both sides of every adjacent pair, converts the difference back to moles through the
        /// ideal gas law, and moves it. The vents set the boundary conditions; the sim does the
        /// flow. This class's own doc claimed that was true well before it was.
        ///
        /// <b>THE ROOM COMES FROM THE SIM, NOT FROM A SHAPE GUESSED OUT HERE.</b>
        /// <see cref="SimRooms"/> answers with the real 4-connected open-cell component, which is
        /// wall-aware by construction and cannot leak into the sealed room next door the way the
        /// diamond could. One call, no allocation, in place of up to 75 boundary crossings and 25
        /// array allocations a tick.
        ///
        /// <b>The fallback is the OLD behaviour exactly.</b> On a SimDLL without the room exports,
        /// or in a room the mixture layer has not been asked to own, this collects the same
        /// radius-3 diamond and measures the same mean over it, port and room being one set again.
        /// A vent on an older DLL therefore behaves as it did rather than stopping.
        /// </summary>
        private bool CollectAndMeasure(int origin, out float roomPressurePa, out int roomCellCount)
        {
            massBySpecies.Clear();
            portMassBySpecies.Clear();
            reach.Clear();
            roomPressurePa = 0f;
            roomCellCount = 0;

            SimRooms.RoomAggregate room;
            if (SimRooms.TryGetAggregate(origin, out room) && room.Owned)
            {
                for (int i = 0; i < room.SpeciesCount; i++)
                {
                    float massKg = room.MassKgAt(i);
                    if (massKg > 0f)
                    {
                        massBySpecies[room.SpeciesAt(i)] = massKg;
                    }
                }
                roomPressurePa = room.MeanPressurePa;
                roomCellCount = room.CellCount;
                CollectPort(origin, room.RoomId);
            }
            else
            {
                CollectDiamond(origin);
                roomPressurePa = MeasureDiamond();
                roomCellCount = reach.Count;
            }

            SumPort();
            return reach.Count > 0 && roomCellCount > 0;
        }

        /// <summary>
        /// The cells this vent physically breathes through: its own tile and its four neighbours,
        /// kept only where they are in the SAME room the vent is in.
        ///
        /// <b>Radius one, not zero, and it is a throughput knob rather than a physical claim.</b>
        /// A single tile stalls: the inward pump can only take what that one tile holds this tick,
        /// so it empties it, waits for diffusion to refill it, and delivers a fraction of its
        /// rate. Five tiles is enough that the existing proportional spread still has somewhere to
        /// spread — the reason that spread exists is written on the loop below, "so no one tile is
        /// emptied while its neighbours stay full" — while staying small enough that the room has
        /// to actually move gas toward the vent. If throughput turns out short, this is the number
        /// to raise, and raising it trades draught for throughput; it does not fix anything else.
        ///
        /// The room-id filter is what makes this better than the old radius even at this size: a
        /// neighbour on the far side of a door belongs to another room, and the sim says so.
        /// </summary>
        private void CollectPort(int origin, int roomId)
        {
            AddPortCell(origin, roomId);
            AddPortCell(Grid.CellLeft(origin), roomId);
            AddPortCell(Grid.CellRight(origin), roomId);
            AddPortCell(Grid.CellAbove(origin), roomId);
            AddPortCell(Grid.CellBelow(origin), roomId);
        }

        private void AddPortCell(int cell, int roomId)
        {
            if (Grid.IsValidCell(cell) && SimRooms.RoomId(cell) == roomId)
            {
                reach.Add(cell);
            }
        }

        /// <summary>
        /// What the port cells hold, by species. This is the number that limits a transfer, and
        /// keeping it separate from the room's total is what makes the port a real bottleneck: a
        /// starved port throttles the vent, the room's gradient steepens, and the sim's own
        /// transport answers it. That feedback IS the draught.
        /// </summary>
        private void SumPort()
        {
            for (int c = 0; c < reach.Count; c++)
            {
                int slots = GasMixtureFacade.ReadComposition(reach[c], compositionSpecies,
                    compositionMassKg);
                for (int i = 0; i < slots; i++)
                {
                    ushort idx = compositionSpecies[i];
                    float massKg = compositionMassKg[i];
                    if (massKg <= 0f)
                    {
                        continue;
                    }
                    portMassBySpecies.TryGetValue(idx, out float existing);
                    portMassBySpecies[idx] = existing + massKg;
                }
            }
        }

        /// <summary>
        /// THE FALLBACK ONLY: every mixture-owned cell within <see cref="Radius"/>, itself
        /// included, by Manhattan distance. This was the vent's whole world until the sim could be
        /// asked what a room is; it is kept, unchanged, for a SimDLL that cannot answer, and it is
        /// the reason a diamond's flaws are described in the past tense above rather than removed
        /// from this file.
        /// </summary>
        private void CollectDiamond(int origin)
        {
            reach.Clear();
            for (int dy = -Radius; dy <= Radius; dy++)
            {
                int span = Radius - Mathf.Abs(dy);
                for (int dx = -span; dx <= span; dx++)
                {
                    int cell = Grid.OffsetCell(origin, dx, dy);
                    if (Grid.IsValidCell(cell) && GasMixtureFacade.IsRoomOwned(cell))
                    {
                        reach.Add(cell);
                    }
                }
            }
        }

        /// <summary>
        /// THE FALLBACK ONLY: sums the diamond's composition into <see cref="massBySpecies"/> and
        /// returns its MEAN pressure -- a mean rather than the vent's own tile, so a vent cannot
        /// chase its own local hole.
        /// </summary>
        private float MeasureDiamond()
        {
            massBySpecies.Clear();
            float pressureSum = 0f;
            for (int c = 0; c < reach.Count; c++)
            {
                int slots = GasMixtureFacade.ReadComposition(reach[c], compositionSpecies,
                    compositionMassKg);
                for (int i = 0; i < slots; i++)
                {
                    ushort idx = compositionSpecies[i];
                    float massKg = compositionMassKg[i];
                    if (massKg <= 0f)
                    {
                        continue;
                    }
                    massBySpecies.TryGetValue(idx, out float existing);
                    massBySpecies[idx] = existing + massKg;
                }
                if (GasMixtureFacade.TryGetPressure(reach[c], out float cellPressurePa))
                {
                    pressureSum += cellPressurePa;
                }
            }
            return reach.Count > 0 ? pressureSum / reach.Count : 0f;
        }

        /// <summary>
        /// The species to draw this tick: whatever the outlet tile already carries, since a tile
        /// mid-fill refuses anything else, and otherwise whichever species is furthest behind its
        /// share of everything drawn so far.
        /// </summary>
        private ushort ChooseSpecies()
        {
            // ALWAYS BY DEFICIT. There used to be a branch here that matched whatever the outlet
            // tile already held, because a tile mid-fill refuses any other element -- and that
            // branch is what starved the whole carbon loop. The outlet tile almost always holds
            // nitrogen or oxygen, so it answered nearly every tick, and carbon dioxide at one
            // percent of the room never got a turn in a hundred seconds of running. The skimmer
            // downstream scrubbed 0.000 kg, the water never cycled, and the sieve sat blocked on a
            // full output: one refusal, four symptoms.
            //
            // The branch is gone because it is no longer needed: `AddToNetwork` finds a tile
            // somewhere on the outlet run that will take the chosen species, so the pick is free
            // to be the honest one -- whichever gas is furthest behind its share of the room.
            float drawnSoFar = 0f;
            for (int i = 0; i < LedgerSlots; i++)
            {
                drawnSoFar += ledgerKg[i];
            }
            float totalMassKg = 0f;
            foreach (System.Collections.Generic.KeyValuePair<ushort, float> entry in massBySpecies)
            {
                totalMassKg += entry.Value;
            }
            if (totalMassKg <= 0f)
            {
                return ushort.MaxValue;
            }

            ushort best = ushort.MaxValue;
            float bestDeficit = float.NegativeInfinity;
            foreach (System.Collections.Generic.KeyValuePair<ushort, float> entry in massBySpecies)
            {
                if (entry.Value <= 0f)
                {
                    continue;
                }
                // THE TARGET IS THE ROOM'S COMPOSITION; THE CANDIDATES ARE WHAT THE PORT HOLDS.
                // Aiming at the room is the whole point of the rotation -- it is what makes a run
                // reproduce the room's real mixture, trace gases included. But a species that is
                // not at the port right now cannot be moved this tick, and picking it anyway
                // would spend the tick doing nothing: the transfer would find zero available and
                // return, over and over, while the gases that ARE there waited their turn behind
                // one that could never come. It stays in the running for later ticks, because the
                // deficit it accrues is what brings it back the moment diffusion delivers it.
                float atPortKg;
                if (!portMassBySpecies.TryGetValue(entry.Key, out atPortKg) || atPortKg <= 0f)
                {
                    continue;
                }
                float share = entry.Value / totalMassKg;
                float deficit = share * drawnSoFar - ExtractedKg(entry.Key);
                if (deficit > bestDeficit)
                {
                    bestDeficit = deficit;
                    best = entry.Key;
                }
            }
            return best;
        }

        private float ExtractedKg(ushort speciesIdx)
        {
            for (int i = 0; i < LedgerSlots; i++)
            {
                if (ledgerElementIdx[i] == speciesIdx)
                {
                    return ledgerKg[i];
                }
            }
            return 0f;
        }

        private void RecordExtraction(ushort speciesIdx, float massKg)
        {
            for (int i = 0; i < LedgerSlots; i++)
            {
                if (ledgerElementIdx[i] == speciesIdx)
                {
                    ledgerKg[i] += massKg;
                    return;
                }
            }
            for (int i = 0; i < LedgerSlots; i++)
            {
                if (ledgerElementIdx[i] < 0)
                {
                    ledgerElementIdx[i] = speciesIdx;
                    ledgerKg[i] = massKg;
                    return;
                }
            }
        }

        // ----------------------------------------------------------------------------------
        // IThresholdSwitch -- the ROOM setpoint, on vanilla's own slider. Stationeers exposes
        // both setpoints over its logic network; ONI has one numeric control per building, so the
        // dial carries the one a player actually tunes and the pipe limit stays a rating.
        // ----------------------------------------------------------------------------------

        public float Threshold
        {
            get { return externalPressurePa; }
            set { externalPressurePa = Mathf.Clamp(value, 0f, MaxSetpointPa); }
        }

        public bool ActivateAboveThreshold
        {
            get { return Direction == VentDirection.Inward; }
            set { }
        }

        public float CurrentValue => MeasuredKgPerSecond;

        public float RangeMin => 0f;

        public float RangeMax => MaxSetpointPa;

        public LocString Title => new LocString("Room Pressure");

        public LocString ThresholdValueName => new LocString("Target pressure");

        public string AboveToolTip => "Draw the room down to this pressure";

        public string BelowToolTip => "Fill the room up to this pressure";

        public ThresholdScreenLayoutType LayoutType => ThresholdScreenLayoutType.SliderBar;

        public int IncrementScale => 1000;

        public NonLinearSlider.Range[] GetRanges => NonLinearSlider.GetDefaultRange(MaxSetpointPa);

        public float GetRangeMinInputField() => RangeMin;

        public float GetRangeMaxInputField() => RangeMax;

        public LocString ThresholdValueUnits() => new LocString("kPa");

        public string Format(float value, bool units)
        {
            string text = (value / 1000f).ToString("F1");
            return units ? text + " kPa" : text;
        }

        public float ProcessedSliderValue(float input) => Mathf.Round(input / 100f) * 100f;

        public float ProcessedInputValue(float input) => input;
    }

    /// <summary>
    /// Carbon dioxide out of a passing air stream and into its own pipe. See
    /// <see cref="AirHandling"/>.
    /// </summary>
    public class CarbonScrubberConfig : IBuildingConfig
    {
        internal const float FullRateWatts = 120f;

        /// <summary>Where the separated carbon dioxide leaves. Drawn by the secondary-output port.</summary>
        internal static readonly CellOffset CarbonDioxideOutputOffset = new CellOffset(0, 0);

        public override BuildingDef CreateBuildingDef()
        {
            // 3x1 on the Gas Filter's own animation and its own port layout: in on the left, the
            // separated stream out of the middle, the cleaned stream out of the right. Borrowed
            // deliberately -- this IS a filter in every structural sense, and a player who has
            // built a Gas Filter already knows where its three pipes go.
            BuildingDef def = BuildingTemplates.CreateBuildingDef(AirHandling.CarbonScrubberId, 3,
                1, "filter_gas_kanim", 30, 10f, BUILDINGS.CONSTRUCTION_MASS_KG.TIER1,
                MATERIALS.ALL_METALS, 1600f, BuildLocationRule.Anywhere,
                BUILDINGS.DECOR.PENALTY.TIER0, NOISE_POLLUTION.NOISY.TIER1);

            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = FullRateWatts;
            def.ExhaustKilowattsWhenActive = 0f;
            def.SelfHeatKilowattsWhenActive = 0f;

            def.InputConduitType = ConduitType.Gas;
            def.OutputConduitType = ConduitType.Gas;
            def.Floodable = false;
            def.ViewMode = OverlayModes.GasConduits.ID;
            def.AudioCategory = "Metal";
            def.PermittedRotations = PermittedRotations.R360;

            // Vanilla's own filter offsets, which are relative to the middle cell of a 3-wide
            // building. All three ports are mutually adjacent and stay three separate networks --
            // that is exactly what the Gas Filter does, and it is why conduit connectivity is
            // decided by connection bits rather than by adjacency.
            def.UtilityInputOffset = new CellOffset(-1, 0);
            def.UtilityOutputOffset = new CellOffset(1, 0);
            def.PowerInputOffset = new CellOffset(1, 0);

            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.GasVentIDs,
                AirHandling.CarbonScrubberId);
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            BuildingConfigManager.Instance.IgnoreDefaultKComponent(typeof(RequiresFoundation),
                prefabTag);
            ConduitSecondaryOutput secondaryOutput = go.AddOrGet<ConduitSecondaryOutput>();
            secondaryOutput.portInfo = new ConduitPortInfo(ConduitType.Gas,
                CarbonDioxideOutputOffset);
            go.AddOrGet<CarbonScrubberComponent>();
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
            Object.DestroyImmediate(go.GetComponent<ConduitConsumer>());
            Object.DestroyImmediate(go.GetComponent<ConduitDispenser>());
            AirHandling.AllowContinuousOutputFlow(go);
            go.GetComponent<KPrefabID>().AddTag(GameTags.OverlayInFrontOfConduits);
        }
    }

    /// <summary>The scrubber's behaviour.</summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class CarbonScrubberComponent : KMonoBehaviour, ISim200ms
    {
        private const string LogPrefix = "[Mod1ThermoFluid] SCRUBBER: ";

        /// <summary>
        /// Kilograms of stream processed per second at full rate. Ahead of the intake's own draw
        /// so the scrubber is never the loop's bottleneck -- a scrubber that throttles the
        /// circulation would be measuring itself rather than the air.
        /// </summary>
        private const float ThroughputKgPerSecond = 2f;

        /// <summary>Carbon dioxide separated out so far, for a harness to assert on.</summary>
        public float ScrubbedKg { get; private set; }

        /// <summary>Everything else passed through so far.</summary>
        public float PassedKg { get; private set; }

        private Building building;
        private Operational operational;
        private ConduitSecondaryOutput secondaryOutput;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            building = GetComponent<Building>();
            operational = GetComponent<Operational>();
            secondaryOutput = GetComponent<ConduitSecondaryOutput>();
        }

        /// <summary>
        /// The cell the separated carbon dioxide leaves through, rotated with the building so a
        /// flipped scrubber's ports move together.
        /// </summary>
        private int CarbonDioxideOutputCell
        {
            get
            {
                CellOffset offset = secondaryOutput != null
                    ? secondaryOutput.GetSecondaryConduitOffset(ConduitType.Gas)
                    : CarbonScrubberConfig.CarbonDioxideOutputOffset;
                return Grid.OffsetCell(building.GetBottomLeftCell(),
                    building.GetRotatedOffset(offset));
            }
        }

        public void Sim200ms(float dt)
        {
            try
            {
                Scrub(dt);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(LogPrefix + "scrub tick failed: " + e);
            }
        }

        private void Scrub(float dt)
        {
            if (building == null || dt <= 0f)
            {
                return;
            }
            if (operational != null && !operational.IsOperational)
            {
                return;
            }

            ConduitFlow gasFlow = Game.Instance?.gasConduitFlow;
            if (gasFlow == null)
            {
                return;
            }

            int inputCell = building.GetUtilityInputCell();
            int cleanCell = building.GetUtilityOutputCell();
            int carbonCell = CarbonDioxideOutputCell;
            if (!gasFlow.HasConduit(inputCell))
            {
                return;
            }

            ConduitFlow.ConduitContents contents = gasFlow.GetContents(inputCell);
            if (contents.mass <= 0f || contents.element == SimHashes.Vacuum)
            {
                return;
            }

            // A conduit tile holds one element, so a stream that is 4% carbon dioxide arrives as
            // occasional carbon dioxide tiles among the rest rather than as a fraction of every
            // tile. Routing whole parcels by species is therefore the correct separation here,
            // not an approximation of one.
            bool isCarbonDioxide = contents.element == SimHashes.CarbonDioxide;
            int destination = isCarbonDioxide ? carbonCell : cleanCell;
            if (!gasFlow.HasConduit(destination))
            {
                // The stream it cannot place backs up rather than being dropped. A scrubber whose
                // carbon dioxide pipe is full stops scrubbing, which is the honest behaviour:
                // the gas has to go somewhere and this building does not destroy it.
                return;
            }

            float takeKg = Mathf.Min(ThroughputKgPerSecond * dt, contents.mass);
            if (takeKg <= 0f)
            {
                return;
            }

            ConduitFlow.ConduitContents taken = gasFlow.RemoveElement(inputCell, takeKg);
            if (taken.mass <= 0f)
            {
                return;
            }

            float accepted = gasFlow.AddElement(destination, taken.element, taken.mass,
                taken.temperature, taken.diseaseIdx, taken.diseaseCount);
            float rejected = taken.mass - accepted;
            if (rejected > 0f)
            {
                gasFlow.AddElement(inputCell, taken.element, rejected, taken.temperature,
                    taken.diseaseIdx, taken.diseaseCount);
            }

            if (accepted <= 0f)
            {
                return;
            }
            if (isCarbonDioxide)
            {
                ScrubbedKg += accepted;
            }
            else
            {
                PassedKg += accepted;
            }
        }
    }
}
