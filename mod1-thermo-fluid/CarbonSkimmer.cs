using KSerialization;
using OniFramework;
using TUNING;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// VANILLA'S CARBON SKIMMER, MADE INLINE: the same machine ONI already ships, with its air
    /// intake moved from the open room onto a PIPE.
    ///
    /// WHY THIS REPLACED THE MOD'S OWN CARBON SCRUBBER. That earlier building separated carbon
    /// dioxide out of a passing stream into a second pipe, which is honest -- it did not destroy
    /// anything -- but it is not a machine that exists. Vanilla already has the real one:
    /// `CO2ScrubberConfig` consumes 1 kg of water and 0.3 kg of carbon dioxide a second and
    /// produces 1 kg of polluted water, which is where the carbon actually goes. Those three
    /// numbers are taken from that config, not invented, and so is the 120 W draw.
    ///
    /// THE ONE CHANGE, and it is the whole point of the building: vanilla's skimmer eats carbon
    /// dioxide out of the air around it with a `PassiveElementConsumer` on a three-tile radius.
    /// That works when a room's air is one element and the machine can stand in it. It cannot
    /// work in a ventilation LOOP, where the air is in a pipe and the room's own atmosphere is a
    /// mixture the `ElementConsumer` cannot read (it sees a promoted cell's frozen vanilla view --
    /// the same defect that made a vanilla Gas Pump extract 0.000 kg from this project's own
    /// corridor). So the intake is a gas conduit, everything that is not carbon dioxide passes
    /// straight through to the outlet, and the machine sits in the duct where an air handler's
    /// scrubber belongs.
    ///
    /// FOUR PORTS ON A 2x2, which ONI supports but not through `BuildingDef` alone: it has one
    /// input and one output offset, so the gas pair are the def's own and the water pair are a
    /// `ConduitSecondaryInput` and a `ConduitSecondaryOutput`. `BuildingCellVisualizer` draws
    /// both from `ISecondaryInput`/`ISecondaryOutput`, so the player sees and connects four real
    /// ports.
    ///
    /// WHERE THE CARBON ENDS UP, which is the reason this is better than the building it
    /// replaced: in the water. A Water Sieve turns that polluted water back into clean water and
    /// toxic sand, so the loop closes on water and the carbon leaves as a solid a duplicant has to
    /// deal with -- and the sieve needs sand delivered to it, which is a chore, which is a person.
    /// The scrubber it replaced put carbon dioxide in a pipe and left it there.
    ///
    /// THE HEAT GOES INTO THE WATER, not into a `SelfHeatKilowattsWhenActive` declaration. Same
    /// rule the Volume Pump and the Gas Mixer follow: the joules a machine draws have to land on
    /// something real, and for this machine that is the polluted water it discharges.
    ///
    /// MASS ISOLATION (container-mass-isolation): every transfer here is conduit-to-conduit
    /// through `ConduitFlow`, and anything a destination refuses goes straight back where it came
    /// from. It never touches a world cell.
    /// </summary>
    public class InlineCarbonSkimmerConfig : IBuildingConfig
    {
        internal const string Id = "InlineCarbonSkimmer";

        /// <summary>Vanilla's own `CO2ScrubberConfig.EnergyConsumptionWhenActive`.</summary>
        internal const float FullRateWatts = 120f;

        /// <summary>Water in. Vanilla puts its liquid input at (0,0) too.</summary>
        internal static readonly CellOffset WaterInputOffset = new CellOffset(0, 0);

        /// <summary>Polluted water out.</summary>
        internal static readonly CellOffset PollutedWaterOutputOffset = new CellOffset(1, 0);

        public override BuildingDef CreateBuildingDef()
        {
            // 2x2 on vanilla's own co2scrubber_kanim, which is the right shape and the right
            // picture -- this IS that machine, plumbed differently.
            BuildingDef def = BuildingTemplates.CreateBuildingDef(Id, 2, 2, "co2scrubber_kanim",
                30, 30f, BUILDINGS.CONSTRUCTION_MASS_KG.TIER2, MATERIALS.RAW_METALS, 800f,
                BuildLocationRule.Anywhere, BUILDINGS.DECOR.PENALTY.TIER1,
                NOISE_POLLUTION.NOISY.TIER3);

            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = FullRateWatts;
            def.ExhaustKilowattsWhenActive = 0f;

            // Declared as zero because the heat is put into the discharged water instead -- see
            // the class doc. Declaring it here as well would pay for the same joules twice.
            def.SelfHeatKilowattsWhenActive = 0f;

            // The GAS pair are the def's own ports; the water pair are secondary components.
            def.InputConduitType = ConduitType.Gas;
            def.OutputConduitType = ConduitType.Gas;
            def.Floodable = false;
            def.ViewMode = OverlayModes.GasConduits.ID;
            def.AudioCategory = "Metal";
            def.AudioSize = "large";
            def.PermittedRotations = PermittedRotations.R360;
            def.UtilityInputOffset = new CellOffset(0, 1);
            def.UtilityOutputOffset = new CellOffset(1, 1);
            def.PowerInputOffset = new CellOffset(1, 0);

            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.GasVentIDs, Id);
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            BuildingConfigManager.Instance.IgnoreDefaultKComponent(typeof(RequiresFoundation),
                prefabTag);

            ConduitSecondaryInput waterInput = go.AddOrGet<ConduitSecondaryInput>();
            waterInput.portInfo = new ConduitPortInfo(ConduitType.Liquid, WaterInputOffset);

            ConduitSecondaryOutput pollutedOutput = go.AddOrGet<ConduitSecondaryOutput>();
            pollutedOutput.portInfo = new ConduitPortInfo(ConduitType.Liquid,
                PollutedWaterOutputOffset);

            go.AddOrGet<InlineCarbonSkimmerComponent>();
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

            // No Storage, no ConduitConsumer, no ConduitDispenser -- the same rule every other
            // mass mover in this mod follows. Vanilla's skimmer buffers through a Storage; this
            // one moves conduit to conduit directly, because a dispenser that cannot place its
            // chunk leaves it on the floor, which reads as mass appearing from nowhere.
            Object.DestroyImmediate(go.GetComponent<ConduitConsumer>());
            Object.DestroyImmediate(go.GetComponent<ConduitDispenser>());
            AirHandling.AllowContinuousOutputFlow(go);
            go.GetComponent<KPrefabID>().AddTag(GameTags.OverlayInFrontOfConduits);
        }
    }

    /// <summary>The skimmer's behaviour. See <see cref="InlineCarbonSkimmerConfig"/>.</summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class InlineCarbonSkimmerComponent : KMonoBehaviour, ISim200ms
    {
        private const string LogPrefix = "[Mod1ThermoFluid] SKIMMER: ";

        /// <summary>
        /// Vanilla's `CO2_CONSUMPTION_RATE`, kg/s, kept as-is. The scrubbing CAPACITY is therefore
        /// vanilla's, and in this rig it is deliberately barely enough for three duplicants --
        /// well under what the real machine can do in a real colony. That is on purpose: a
        /// scrubber comfortably outrunning its room proves nothing, and a marginal one shows the
        /// player where the limit is.
        /// </summary>
        private const float CarbonDioxideKgPerSecond = 0.3f;

        /// <summary>
        /// Water per second, and this is DELIBERATELY AND HUGELY OFF VANILLA'S RATIO -- 175 kg
        /// per 300 g of carbon dioxide against Klei's 1 kg. It is a demonstration cheat, stated
        /// as one, and it is the only number in this file that is not vanilla's.
        ///
        /// The reason is arithmetic that cannot be tuned away. Three duplicants exhale 0.006 kg
        /// of carbon dioxide a second, and a vanilla Water Sieve consumes 5 kg of polluted water
        /// a second. At Klei's 1:0.3 ratio those three people can keep a sieve busy for about
        /// four thousandths of its duty cycle -- the loop is real, correct, and completely
        /// invisible, which is indistinguishable from broken and cost several runs of guessing at
        /// it. This ratio puts the sieve at roughly 70% uptime so the whole water loop can be
        /// watched working: skimmer to tank to sieve to tank and back, with sand being carried.
        ///
        /// A shipping version uses Klei's ratio. The carbon side is untouched -- the scrubbing
        /// CAPACITY here is vanilla's, and deliberately marginal for three duplicants.
        /// </summary>
        private const float WaterKgPerSecond = 175f;

        /// <summary>
        /// How much gas that is NOT carbon dioxide the machine will pass through per second. Well
        /// clear of the ventilation rate it sits in, so the skimmer is never the loop's
        /// bottleneck -- a scrubber that throttles the circulation measures itself rather than
        /// the air.
        /// </summary>
        private const float PassThroughKgPerSecond = 60f;

        private const float JoulesToKilojoules = 0.001f;

        private const float GramsPerKilogram = 1000f;

        /// <summary>Carbon dioxide taken out of the air stream, for a harness to assert on.</summary>
        public float ScrubbedKg { get; private set; }

        /// <summary>Everything else passed straight through.</summary>
        public float PassedKg { get; private set; }

        /// <summary>Polluted water produced.</summary>
        public float PollutedWaterKg { get; private set; }

        /// <summary>Work put into the discharged water, in kilojoules.</summary>
        public float WorkKJ { get; private set; }

        private Building building;
        private Operational operational;
        private EnergyConsumer energyConsumer;
        private ConduitSecondaryInput waterInput;
        private ConduitSecondaryOutput pollutedOutput;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            building = GetComponent<Building>();
            operational = GetComponent<Operational>();
            energyConsumer = GetComponent<EnergyConsumer>();
            waterInput = GetComponent<ConduitSecondaryInput>();
            pollutedOutput = GetComponent<ConduitSecondaryOutput>();
        }

        /// <summary>The cell water arrives at, rotated with the building.</summary>
        public int WaterInputCell
        {
            get
            {
                CellOffset offset = waterInput != null
                    ? waterInput.GetSecondaryConduitOffset(ConduitType.Liquid)
                    : InlineCarbonSkimmerConfig.WaterInputOffset;
                return Grid.OffsetCell(building.GetBottomLeftCell(),
                    building.GetRotatedOffset(offset));
            }
        }

        /// <summary>The cell polluted water leaves by, rotated with the building.</summary>
        public int PollutedWaterOutputCell
        {
            get
            {
                CellOffset offset = pollutedOutput != null
                    ? pollutedOutput.GetSecondaryConduitOffset(ConduitType.Liquid)
                    : InlineCarbonSkimmerConfig.PollutedWaterOutputOffset;
                return Grid.OffsetCell(building.GetBottomLeftCell(),
                    building.GetRotatedOffset(offset));
            }
        }

        public void Sim200ms(float dt)
        {
            float before = ScrubbedKg + PassedKg;
            try
            {
                Skim(dt);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(LogPrefix + "skim tick failed: " + e);
            }
            if (operational != null)
            {
                operational.SetActive(ScrubbedKg + PassedKg > before);
            }
        }

        private void Skim(float dt)
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
            ConduitFlow liquidFlow = Game.Instance?.liquidConduitFlow;
            if (gasFlow == null || liquidFlow == null)
            {
                return;
            }

            int gasInCell = building.GetUtilityInputCell();
            int gasOutCell = building.GetUtilityOutputCell();
            if (!gasFlow.HasConduit(gasInCell) || !gasFlow.HasConduit(gasOutCell))
            {
                return;
            }

            // CARBON DIOXIDE FIRST, AND FROM ANYWHERE ON THE INLET NETWORK -- not just from the
            // one tile the machine is bolted to.
            //
            // This is the correction a live run forced. A conduit tile holds one element, so a
            // duct carrying air that is one percent carbon dioxide presents a carbon dioxide tile
            // to the skimmer roughly one tick in a hundred, and only if that parcel happens to be
            // the one sitting at the inlet at that instant. The rig ran a hundred seconds and the
            // skimmer scrubbed 0.000 kg while carbon dioxide was demonstrably going past it. A
            // real scrubber pulls its reagent out of the stream; it does not wait for the stream
            // to hand it one. So it looks along its own inlet network for carbon dioxide and takes
            // it from wherever in that duct it actually is.
            int carbonCell = FindCarbonDioxide(gasInCell);
            if (carbonCell != Grid.InvalidCell)
            {
                Scrub(gasFlow, liquidFlow, carbonCell, gasFlow.GetContents(carbonCell), dt);
            }

            // Everything else still passes through the inlet tile, so the duct keeps flowing while
            // the machine works.
            ConduitFlow.ConduitContents incoming = gasFlow.GetContents(gasInCell);
            if (incoming.mass > 0f && incoming.element != SimHashes.Vacuum
                && incoming.element != SimHashes.CarbonDioxide)
            {
                PassThrough(gasFlow, gasInCell, gasOutCell, incoming, dt);
            }
        }

        /// <summary>
        /// Discharges into the tile of the network FURTHEST from this machine's own port, falling
        /// back towards itself.
        ///
        /// ONI's `ConduitFlow` fixes a flow direction per run when the network is built, and a
        /// machine has no say in which way that came out. Dropping the discharge at its own port
        /// therefore works or does not work depending on which end of the pipe the game decided
        /// was downstream -- and here it did not: the skimmer made 310 kg of polluted water, the
        /// pipe held 2.26 kg of it at the machine's end, and the reservoir eight tiles away read
        /// empty on its own tooltip while our readout showed the PIPE filling. Two tooltips
        /// disagreeing was the giveaway.
        ///
        /// Putting it at the far end instead reaches the consumer regardless of which way the run
        /// flows, which is what a discharge pump does: it does not put fluid down beside itself
        /// and hope the pipe is pointing the right way.
        /// </summary>
        private static float PushDownstream(ConduitFlow flow, int portCell, SimHashes element,
            float massKg, float temperatureK, byte diseaseIdx, int diseaseCount)
        {
            PipeContentType contentType = flow == Game.Instance.liquidConduitFlow
                ? PipeContentType.Liquid
                : PipeContentType.Gas;
            if (PipeNetworkFacade.TryGetNetworkCells(portCell, contentType, out int[] cells))
            {
                // Asked for by name rather than taken from the array's order: the cached partition
                // has no per-caller order, so the intent is stated.
                int farthest = PipeNetworkFacade.FarthestNetworkCell(cells, portCell);
                if (farthest != Grid.InvalidCell)
                {
                    float atFarthest = flow.AddElement(farthest, element, massKg, temperatureK,
                        diseaseIdx, diseaseCount);
                    if (atFarthest > 0f)
                    {
                        return atFarthest;
                    }
                }
                for (int i = cells.Length - 1; i >= 0; i--)
                {
                    if (cells[i] == portCell || cells[i] == farthest)
                    {
                        continue;
                    }
                    float far = flow.AddElement(cells[i], element, massKg, temperatureK,
                        diseaseIdx, diseaseCount);
                    if (far > 0f)
                    {
                        return far;
                    }
                }
            }
            return flow.AddElement(portCell, element, massKg, temperatureK, diseaseIdx,
                diseaseCount);
        }

        /// <summary>
        /// The first tile on <paramref name="portCell"/>'s network holding the wanted element, or
        /// <c>Grid.InvalidCell</c>. This is the "pump" half of a machine that draws its own
        /// working fluid rather than waiting to be fed.
        /// </summary>
        private static int FindOnNetwork(ConduitFlow flow, int portCell, SimHashes wanted)
        {
            PipeContentType contentType = flow == Game.Instance.liquidConduitFlow
                ? PipeContentType.Liquid
                : PipeContentType.Gas;
            if (!PipeNetworkFacade.TryGetNetworkCells(portCell, contentType, out int[] cells))
            {
                return Grid.InvalidCell;
            }
            for (int i = 0; i < cells.Length; i++)
            {
                ConduitFlow.ConduitContents contents = flow.GetContents(cells[i]);
                if (contents.mass > 0f && contents.element == wanted)
                {
                    return cells[i];
                }
            }
            return Grid.InvalidCell;
        }

        /// <summary>
        /// Pushes into the first tile of a network that will accept this species, preferring the
        /// machine's own port tile so a short run behaves exactly as before. Returns what was
        /// actually accepted. Used on both the gas outlet and the polluted-water outlet, because a
        /// full single tile is a permanent stall on either of them.
        /// </summary>
        private static float AddToNetwork(ConduitFlow flow, int portCell, SimHashes element,
            float massKg, float temperatureK, byte diseaseIdx, int diseaseCount)
        {
            float accepted = flow.AddElement(portCell, element, massKg, temperatureK, diseaseIdx,
                diseaseCount);
            if (accepted > 0f)
            {
                return accepted;
            }
            PipeContentType contentType = flow == Game.Instance.liquidConduitFlow
                ? PipeContentType.Liquid
                : PipeContentType.Gas;
            if (!PipeNetworkFacade.TryGetNetworkCells(portCell, contentType, out int[] cells))
            {
                return 0f;
            }
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == portCell)
                {
                    continue;
                }
                accepted = flow.AddElement(cells[i], element, massKg, temperatureK,
                    diseaseIdx, diseaseCount);
                if (accepted > 0f)
                {
                    return accepted;
                }
            }
            return 0f;
        }

        /// <summary>
        /// The tile on this skimmer's own inlet network that currently holds carbon dioxide, or
        /// <c>Grid.InvalidCell</c>. See the note in <see cref="Skim"/> for why reaching along the
        /// duct is the correct behaviour rather than a convenience.
        ///
        /// It reaches only along the network its inlet is ON -- never into the room, and never
        /// into the clean side downstream of itself -- so it stays an inline duct machine rather
        /// than quietly becoming vanilla's radius-based room scrubber wearing different pipes.
        /// </summary>
        private int FindCarbonDioxide(int gasInCell)
        {
            if (!PipeNetworkFacade.TryGetNetworkCells(gasInCell, PipeContentType.Gas,
                    out int[] cells))
            {
                return Grid.InvalidCell;
            }
            ConduitFlow gasFlow = Game.Instance.gasConduitFlow;
            for (int i = 0; i < cells.Length; i++)
            {
                ConduitFlow.ConduitContents contents = gasFlow.GetContents(cells[i]);
                if (contents.mass > 0f && contents.element == SimHashes.CarbonDioxide)
                {
                    return cells[i];
                }
            }
            return Grid.InvalidCell;
        }

        /// <summary>
        /// Everything that is not carbon dioxide goes straight out of the other side. A conduit
        /// tile holds one element, so the stream arrives as whole parcels of one gas at a time and
        /// routing by species is the correct behaviour here rather than an approximation of one.
        /// </summary>
        private void PassThrough(ConduitFlow gasFlow, int gasInCell, int gasOutCell,
            ConduitFlow.ConduitContents incoming, float dt)
        {
            float takeKg = Mathf.Min(PassThroughKgPerSecond * dt, incoming.mass);
            if (takeKg <= 0f)
            {
                return;
            }
            ConduitFlow.ConduitContents taken = gasFlow.RemoveElement(gasInCell, takeKg);
            if (taken.mass <= 0f)
            {
                return;
            }
            // INTO ANY TILE ON THE OUTLET NETWORK, not only the one tile this machine is bolted
            // to. Pushing into its own outlet tile alone is what jammed the whole rig: once that
            // single tile filled, the skimmer stalled permanently, the return duct backed up
            // behind it, and the intake stopped -- the giveaway was the intake drawing exactly
            // 1.675 kg across four runs whose rooms were nothing alike. A machine in a duct is
            // connected to the duct, not to one tile of it.
            float accepted = AddToNetwork(gasFlow, gasOutCell, taken.element, taken.mass,
                taken.temperature, taken.diseaseIdx, taken.diseaseCount);
            float rejected = taken.mass - accepted;
            if (rejected > 0f)
            {
                gasFlow.AddElement(gasInCell, taken.element, rejected, taken.temperature,
                    taken.diseaseIdx, taken.diseaseCount);
            }
            PassedKg += accepted;
        }

        /// <summary>
        /// The reaction: carbon dioxide plus water becomes polluted water, at vanilla's own
        /// one-kilogram-of-water-per-three-hundred-grams ratio.
        ///
        /// It is limited by all three of the things that can actually run short -- the carbon
        /// dioxide in the duct, the water in the pipe, and the room left in the polluted-water
        /// line -- and it does nothing at all unless every one of them is available. A skimmer
        /// whose discharge is backed up stops scrubbing rather than destroying the carbon, which
        /// is the same rule every other machine in this mod follows.
        /// </summary>
        private void Scrub(ConduitFlow gasFlow, ConduitFlow liquidFlow, int gasInCell,
            ConduitFlow.ConduitContents incoming, float dt)
        {
            int waterCell = WaterInputCell;
            int pollutedCell = PollutedWaterOutputCell;
            if (!liquidFlow.HasConduit(waterCell) || !liquidFlow.HasConduit(pollutedCell))
            {
                return;
            }

            // A BUILT-IN PUMP, and this is the piece that was missing. A pipe in this project is
            // not a directional thing any more -- the ported Stationeers model moves fluid down a
            // pressure gradient, and a Liquid Reservoir's "input" and "output" are just two ports
            // on the same tank. So a machine that sits waiting for water to be PUSHED into the one
            // tile under its inlet waits forever, which is exactly what happened: water was
            // observed flowing the wrong way between the tank and this machine, and the skimmer
            // reacted 0.000 kg across five runs.
            //
            // Vanilla's own Carbon Skimmer has a `ConduitConsumer`, which is a pump: it PULLS from
            // the network its inlet is on. This does the same thing directly -- it reaches along
            // its own water network and takes what it needs from wherever the water actually is,
            // which in this rig is a reservoir several tiles away. A real scrubber draws its
            // working fluid; it does not hope for it.
            int waterSourceCell = FindOnNetwork(liquidFlow, waterCell, SimHashes.Water);
            if (waterSourceCell == Grid.InvalidCell)
            {
                return;
            }
            ConduitFlow.ConduitContents water = liquidFlow.GetContents(waterSourceCell);
            if (water.mass <= 0f)
            {
                return;
            }

            float carbonWanted = Mathf.Min(CarbonDioxideKgPerSecond * dt, incoming.mass);
            float waterWanted = carbonWanted * (WaterKgPerSecond / CarbonDioxideKgPerSecond);
            if (waterWanted > water.mass)
            {
                // Short of water: scale the carbon down to what the water on hand can actually
                // react with, rather than running the reaction at the wrong ratio.
                waterWanted = water.mass;
                carbonWanted = waterWanted * (CarbonDioxideKgPerSecond / WaterKgPerSecond);
            }
            if (carbonWanted <= 0f || waterWanted <= 0f)
            {
                return;
            }

            // The polluted water is one kilogram out per kilogram of water in (vanilla's own
            // output ratio), so ask the destination first: if it will not take the discharge,
            // nothing is consumed at either input.
            ConduitFlow.ConduitContents takenWater = liquidFlow.RemoveElement(waterSourceCell,
                waterWanted);
            if (takenWater.mass <= 0f)
            {
                return;
            }
            float carbonKg = takenWater.mass * (CarbonDioxideKgPerSecond / WaterKgPerSecond);
            ConduitFlow.ConduitContents takenCarbon = gasFlow.RemoveElement(gasInCell, carbonKg);

            // THE WORK, and where it goes. The machine draws power whether or not the reaction
            // ran, so those joules exist; they are added to the water it discharges, which is
            // both where the heat physically appears and why a skimmer warms the loop it sits in.
            float wattage = energyConsumer != null
                ? energyConsumer.BaseWattageRating
                : InlineCarbonSkimmerConfig.FullRateWatts;
            float workJoules = wattage * dt;
            float dischargeK = takenWater.temperature;
            Element dirtyWater = ElementLoader.FindElementByHash(SimHashes.DirtyWater);
            if (dirtyWater != null && dirtyWater.specificHeatCapacity > 0f && takenWater.mass > 0f)
            {
                dischargeK += workJoules
                    / (takenWater.mass * dirtyWater.specificHeatCapacity * GramsPerKilogram);
            }

            float accepted = PushDownstream(liquidFlow, pollutedCell, SimHashes.DirtyWater,
                takenWater.mass, dischargeK, takenWater.diseaseIdx, takenWater.diseaseCount);
            float rejected = takenWater.mass - accepted;
            if (rejected > 0f)
            {
                // Discharge is full. Put the water back where it came from, and the carbon
                // dioxide back into the duct -- the reaction simply did not happen this tick.
                liquidFlow.AddElement(waterSourceCell, takenWater.element, rejected,
                    takenWater.temperature, takenWater.diseaseIdx, takenWater.diseaseCount);
                if (takenCarbon.mass > 0f)
                {
                    float carbonBack = takenCarbon.mass * (rejected / takenWater.mass);
                    gasFlow.AddElement(gasInCell, takenCarbon.element, carbonBack,
                        takenCarbon.temperature, takenCarbon.diseaseIdx,
                        takenCarbon.diseaseCount);
                    ScrubbedKg += takenCarbon.mass - carbonBack;
                }
            }
            else if (takenCarbon.mass > 0f)
            {
                ScrubbedKg += takenCarbon.mass;
            }

            if (accepted > 0f)
            {
                PollutedWaterKg += accepted;
                WorkKJ += workJoules * JoulesToKilojoules;
            }
        }
    }
}
