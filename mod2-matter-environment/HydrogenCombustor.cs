using System.Collections.Generic;
using HarmonyLib;
using Klei;
using OniFramework;
using STRINGS;
using UnityEngine;

namespace Mod2MatterEnvironment
{
    /// <summary>
    /// The Hydrogen Generator burns hydrogen WITH OXYGEN and gives back the water as HOT LIQUID
    /// (part of the Mars survival loop). Vanilla's generator
    /// deletes 100 g/s of hydrogen for 800 W and emits nothing. Patched, it takes 2 H2 + O2 -> 2 H2O
    /// by mass: 0.1 kg/s H2 and 0.8 kg/s O2 into 0.9 kg/s of water, at 800 W when the mix is right.
    /// The idea: use the combustor to make water, then the player has to cool the water.
    ///
    /// This augments the vanilla building rather than adding one: same art, same size, same 800 W, same research.
    /// - ONE mixed gas input pipe carries both reactants, as Stationeers' combustors take
    ///   one input network's mixture (MATCH).
    /// - OFF MIX: the burn is limited by whichever reactant is short and scales linearly: a fraction
    ///   f = min(H2 / one tick's H2, O2 / one tick's O2, 1) of the full burn, giving f x 800 W and
    ///   f x 0.9 kg/s water. That is CombustionResult.RunCombustion's min(fuel / 2, oxidiser / 1)
    ///   and PowerGeneratorPipe's power linear in what burned (MATCH). Stationeers burns 0.9
    ///   (PowerGeneratorPipe) or 0.99 (H2CombustorMachine) of the pair per tick; this burns all of
    ///   it, f = 1 on a correct mix (by choice).
    /// - LIQUID WATER OUT, not steam: steam booked at
    ///   m c T carries water's 2.26 MJ/kg latent heat unpaid, which the physical latent heats
    ///   release when it condenses -- about 2 MW per generator from nothing. Liquid in at the
    ///   electrolyzer and liquid out here, with AddLiquid making liquid directly, so no phase change
    ///   happens anywhere in the loop and no latent term is owed.
    /// - WATER TEMPERATURE: 353.15 K when the reactants arrive at 300 K, plus
    ///   their heat relative to 300 K spread over the water (k = (0.1 x 2.4 + 0.8 x 1.005) /
    ///   (0.9 x 4.179) = 0.2776 K per K), and never above 363.15 K: Water's highTemp is 372.5 K and
    ///   the conduit boil trigger 3 K above it, so the line keeps 9 K of margin. Heat above the clamp
    ///   goes into the generator's own body (StructureTemperatures.ProduceEnergy, the self-heat path)
    ///   and is counted as ClampExcessKJ; it is never deleted. No floor: a 150 K Mars feed gives
    ///   311.5 K water, and the freeze trigger (269.5 K) would need a feed below 0 K.
    /// - NOW A LIQUID PORT at (2,0). Vanilla's BuildingLoader adds RequireOutputs to any def with
    ///   an output conduit, so with no water pipe or a full one the generator
    ///   stops and the water is never deleted.
    /// - PASS-THROUGH ON A SECOND, GAS PORT at (2,2) (vanilla's ConduitSecondaryOutput, as the Gas
    ///   Filter's): gas the building cannot burn (a reactant over its store, any inert gas) leaves
    ///   unburned there, never into the room. A correct mix never uses it. Its store holds 5 kg; with
    ///   the line missing or full the excess stays at the inlet and an off mix backs up and stops,
    ///   with a status. Stationeers' machines send one mixture out of one port (STRUCTURAL: ONI pipes
    ///   carry one phase).
    /// - A reactant is taken only up to its store (100 s: 10 kg H2, 80 kg O2); a full store waits in
    ///   the input pipe while the other reactant still holds a burn tick, so an oversupplied correct
    ///   mix backs up without leaking fuel; once the other would run short by the next inlet take,
    ///   the excess goes straight into the pass-through store and out (if the gas line has room).
    ///
    /// Three storages: the input one (vanilla's, which the ConduitConsumer and EnergyGenerator use)
    /// holds only the reactants; the water store feeds the liquid dispenser; the pass-through store
    /// feeds the gas dispenser on the secondary port. The water is not a formula output (vanilla's
    /// Emit would write it at the body temperature): this class adds it after a burn.
    /// GeneratorEnthalpy is told to leave the building alone so the two rules never both apply.
    ///
    /// - FUEL TEMPERATURE: each burn reads the reactants' managed
    ///   InternalTemperature (TryReadFuel with useStoredTemperature), not the sim chunk behind
    ///   PrimaryElement.Temperature. The input store is not insulated, so the items keep their chunks,
    ///   and one water test read the chunk a set behind on every other burn
    ///   (257-270 K against 345 K). Storage's Insulate modifier does not help: it zeroes the chunk's
    ///   heat capacity once, keeps the handle, and the next set restores it. Stationeers'
    ///   PowerGeneratorPipe.OnAtmosphericTick burns its own InternalAtmosphere mixture at that
    ///   mixture's temperature (MATCH: the fuel is read from the machine's own store).
    /// - FUEL OFF THE SIM: reading the stored temperature alone did not hold, because vanilla's
    ///   intake merge (Storage.AddGasChunk) reads the chunk and writes it into InternalTemperature.
    ///   The input store's items are now unregistered from the sim while stored (FuelStoreOffSim).
    ///
    /// Framework surface: TryReadFuel's useStoredTemperature overload and
    /// Exempt.
    /// </summary>
    public sealed class HydrogenCombustor : KMonoBehaviour, ISim200ms, IGameObjectEffectDescriptor
    {
        public const string PrefabId = HydrogenGeneratorConfig.ID;

        /// <summary>Hydrogen burned per second at full output, kg (vanilla's rate).</summary>
        public const float HydrogenKgPerSecond = 0.1f;

        /// <summary>Oxygen burned per second at full output, kg: 8 x the hydrogen, 2 H2 + O2 by mass.</summary>
        public const float OxygenKgPerSecond = 0.8f;

        /// <summary>Water made per second at full output, kg: everything burned, so mass closes.</summary>
        public const float WaterKgPerSecond = HydrogenKgPerSecond + OxygenKgPerSecond;

        /// <summary>The water's temperature when the reactants arrive at the calibration
        /// temperature (300 K).</summary>
        public const float WaterTemperatureAtCalibrationK = 353.15f;

        /// <summary>The water is never made hotter than this: Water's highTemp 372.5 K, the conduit
        /// boil trigger 3 K above, 9 K of margin under that.</summary>
        public const float WaterMaxTemperatureK = 363.15f;

        /// <summary>Most hydrogen held, kg: 100 s of fuel. Ten seconds was too
        /// little: in an oversupplied pipe the packets queue first in, first out, and while one
        /// hydrogen packet waited at the inlet the oxygen behind it could not arrive, the oxygen
        /// store ran out, and the deadlock escape passed most of a packet of hydrogen out AT A
        /// CORRECT MIX (about 0.8 kg per nine packets). A store larger than any packet (8 kg with
        /// Mod 1's gas pipe) and longer than a feed's pattern rides that out.</summary>
        public const float HydrogenStoreKg = 10f;

        /// <summary>Most oxygen held, kg: the same 100 s.</summary>
        public const float OxygenStoreKg = 80f;

        /// <summary>The input pipe's fill limit, kg: both reactants' stores.</summary>
        public const float InputCapacityKg = HydrogenStoreKg + OxygenStoreKg;

        /// <summary>Most unburned gas waiting for the secondary gas port, kg.</summary>
        public const float PassThroughStoreKg = 5f;

        /// <summary>Most water waiting for the liquid port, kg: a few seconds of output.</summary>
        public const float WaterStoreKg = 10f;

        /// <summary>The generator's burn tick, s (EnergySim200ms).</summary>
        public const float BurnTickSeconds = 0.2f;

        /// <summary>Less room than this counts as none, kg.</summary>
        public const float MinTakeKg = 0.001f;

        /// <summary>Below this fraction of a full burn the tick does not burn; the crumbs wait in
        /// store for the next one.</summary>
        public const float MinBurnFraction = 0.01f;

        /// <summary>The secondary gas port: the pass-through line.</summary>
        public static readonly ConduitPortInfo PassThroughPort =
            new ConduitPortInfo(ConduitType.Gas, new CellOffset(2, 2));

        // R-WATER's books. Static, summed over every patched generator since the last reset.
        public static double WaterKg { get; private set; }
        public static double WaterKJ { get; private set; }
        public static double ReactantKJ { get; private set; }
        public static double ReactantHeatCapacityKJPerK { get; private set; }
        public static double BurnedHydrogenKg { get; private set; }
        public static double BurnedOxygenKg { get; private set; }
        public static double GeneratedJ { get; private set; }
        public static long Burns { get; private set; }
        public static long PartialBurns { get; private set; }
        public static double PassedHydrogenKg { get; private set; }
        public static double PassedOxygenKg { get; private set; }
        public static double PassedOtherKg { get; private set; }
        /// <summary>Inlet ticks on which a reactant packet stayed in the pipe, wholly or partly,
        /// because its store had no room for it (0.5.0: the partial holds count too, so an
        /// oversupplied correct mix that keeps burning shows its backlog).</summary>
        public static long HeldInPipe { get; private set; }
        /// <summary>Reactant packets taken by the deadlock escape (the other reactant had run short
        /// behind them) straight into the pass-through store, as against a store's ordinary
        /// surplus.</summary>
        public static long EscapeTakes { get; private set; }
        /// <summary>Ticks the inlet held because the pass-through line had no room.</summary>
        public static long PassThroughBlockedTicks { get; private set; }
        /// <summary>Heat above the 363.15 K clamp, put into the generator's body, kJ.</summary>
        public static double ClampExcessKJ { get; private set; }
        public static long ClampTicks { get; private set; }

        public static void ResetTotals()
        {
            WaterKg = 0;
            WaterKJ = 0;
            ReactantKJ = 0;
            ReactantHeatCapacityKJPerK = 0;
            BurnedHydrogenKg = 0;
            BurnedOxygenKg = 0;
            GeneratedJ = 0;
            Burns = 0;
            PartialBurns = 0;
            PassedHydrogenKg = 0;
            PassedOxygenKg = 0;
            PassedOtherKg = 0;
            HeldInPipe = 0;
            EscapeTakes = 0;
            PassThroughBlockedTicks = 0;
            ClampExcessKJ = 0;
            ClampTicks = 0;
        }

        /// <summary>The water on its way to the liquid port. Set by the config.</summary>
        public Storage waterStorage;

        /// <summary>The unburned gas on its way to the secondary gas port. Set by the config.</summary>
        public Storage passStorage;

        [MyCmpReq] private Storage storage;
        [MyCmpReq] private Operational operational;
        [MyCmpReq] private EnergyGenerator generator;
        [MyCmpReq] private ConduitConsumer consumer;
        [MyCmpReq] private Building building;
        [MyCmpReq] private KSelectable selectable;

        // The generator whose tick is running, and the fraction of a full burn it may make; read
        // by the GenerateJoules prefix. Only ever set between this class's prefix and postfix.
        internal static EnergyGenerator TickGenerator;
        internal static float TickFraction;

        private static readonly Dictionary<ConduitConsumer, HydrogenCombustor> byConsumer =
            new Dictionary<ConduitConsumer, HydrogenCombustor>();
        private static readonly List<SimHashes> held = new List<SimHashes>();
        private static StatusItem blockedStatus;

        /// <summary>The line in the building's heat breakdown for the clamp's excess.</summary>
        private const string ClampHeatSource = "Water too hot for its pipe";

        private int inputCell;
        private int passCell;
        private FlowUtilityNetwork.NetworkItem passEndpoint;
        private HandleVector<int>.Handle structureTemperature;
        private bool passThroughBlocked;
        private System.Guid blockedStatusHandle;

        // This tick's burn, decided before EnergySim200ms consumes anything.
        private float fraction;
        private bool fuelRead;
        private float fuelHeatCapacityKJPerK;
        private float fuelTemperatureK;

        internal static StatusItem BlockedStatus => blockedStatus ?? (blockedStatus = new StatusItem(
            "HYDROGENGENERATORPASSBLOCKED", "BUILDING", "", StatusItem.IconType.Exclamation,
            NotificationType.BadMinor, false, OverlayModes.GasConduits.ID));

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            // Before any Storage.OnSpawn re-applies its items' modifiers on a load.
            FuelStoreOffSim.AddStore(storage);
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            // The formula's inputs array came from the prefab; this instance scales its rates every
            // tick, so it needs its own copy.
            EnergyGenerator.Formula formula = generator.formula;
            formula.inputs = (EnergyGenerator.InputItem[])formula.inputs.Clone();
            generator.formula = formula;
            inputCell = building.GetUtilityInputCell();
            structureTemperature = GameComps.StructureTemperatures.GetHandle(gameObject);
            passCell = Grid.OffsetCell(building.NaturalBuildingCell(), PassThroughPort.offset);
            // The port joins the gas network as a SOURCE, as the Gas Filter's secondary port does
            // (ElementFilter.OnSpawn): ConduitFlow computes flow from sources toward sinks
            // (ComputeFlow), so an unregistered port's gas would have no direction to go.
            passEndpoint = new FlowUtilityNetwork.NetworkItem(ConduitType.Gas, Endpoint.Source, passCell, gameObject);
            Conduit.GetNetworkManager(ConduitType.Gas).AddToNetworks(passCell, passEndpoint, is_endpoint: true);
            Game.Instance.gasConduitFlow.AddConduitUpdater(PassUpdate, ConduitFlowPriority.Dispense);
            byConsumer[consumer] = this;
        }

        protected override void OnCleanUp()
        {
            Game.Instance.gasConduitFlow.RemoveConduitUpdater(PassUpdate);
            if (passEndpoint != null)
            {
                Conduit.GetNetworkManager(ConduitType.Gas).RemoveFromNetworks(passCell, passEndpoint, is_endpoint: true);
            }
            byConsumer.Remove(consumer);
            FuelStoreOffSim.RemoveStore(storage);
            base.OnCleanUp();
        }

        /// <summary>
        /// Empties the pass-through store into the secondary gas port, whether or not the generator
        /// is running. This is the generator's own updater and NOT a second ConduitDispenser: every
        /// ConduitDispenser writes the building's one shared "output_conduit" Functional flag on
        /// every conduit tick (ConduitDispenser.ConduitUpdate), so a second one with no gas line
        /// would switch the whole generator off, and the gas port is meant to be optional.
        /// </summary>
        private void PassUpdate(float dt)
        {
            ConduitFlow flow = Game.Instance.gasConduitFlow;
            if (!flow.HasConduit(passCell))
            {
                return;
            }
            List<GameObject> items = passStorage.items;
            for (int i = 0; i < items.Count; i++)
            {
                PrimaryElement primary = items[i] == null ? null : items[i].GetComponent<PrimaryElement>();
                if (primary == null || !(primary.Mass > 0f) || primary.Element == null || !primary.Element.IsGas)
                {
                    continue;
                }
                float added = flow.AddElement(passCell, primary.ElementID, primary.Mass, primary.Temperature,
                    primary.DiseaseIdx, primary.DiseaseCount);
                if (added > 0f)
                {
                    int germs = (int)(added / primary.Mass * primary.DiseaseCount);
                    primary.ModifyDiseaseCount(-germs, "HydrogenCombustor.PassUpdate");
                    primary.KeepZeroMassObject = true;
                    primary.Mass -= added;
                    passStorage.Trigger((int)GameHashes.OnStorageChange, primary.gameObject);
                }
                // One packet per conduit tick, as a dispenser does.
                return;
            }
        }

        /// <summary>
        /// The water's temperature by the rule alone, before the clamp: 353.15 K plus the
        /// reactants' heat relative to the calibration temperature, spread over the water. The fw
        /// GeneratorEnthalpy rule, at the global calibration (300 K unless a mod moved it).
        /// </summary>
        public static float WaterRuleTemperatureK(float reactantHeatCapacityKJPerK, float reactantTemperatureK,
            float waterKg)
        {
            float waterHeatCapacity = waterKg * ElementLoader.FindElementByHash(SimHashes.Water).specificHeatCapacity;
            if (!(waterHeatCapacity > 0f))
            {
                return WaterTemperatureAtCalibrationK;
            }
            float calibrationK = GeneratorEnthalpy.GlobalCalibrationTemperature;
            return WaterTemperatureAtCalibrationK
                + reactantHeatCapacityKJPerK * (reactantTemperatureK - calibrationK) / waterHeatCapacity;
        }

        /// <summary>
        /// The fraction of a full burn the stored reactants allow this tick: the limiting reagent.
        /// </summary>
        public static float BurnFraction(float hydrogenKg, float oxygenKg, float dt)
        {
            if (!(dt > 0f))
            {
                return 0f;
            }
            float f = Mathf.Min(hydrogenKg / (HydrogenKgPerSecond * dt), oxygenKg / (OxygenKgPerSecond * dt));
            // A hair under the store, so vanilla's IsConvertible (mass >= rate * dt) never refuses
            // the exact limiting amount on rounding.
            f = Mathf.Min(f * 0.999999f, 1f);
            return f >= MinBurnFraction ? f : 0f;
        }

        internal void BeforeTick(float dt)
        {
            // Normally a no-op: items are taken off the sim as they are stored. This catches the
            // ones a load left on it (SimUnregister does nothing while a scene loads).
            FuelStoreOffSim.Sweep(storage);
            fraction = BurnFraction(storage.GetMassAvailable(SimHashes.Hydrogen.CreateTag()),
                storage.GetMassAvailable(SimHashes.Oxygen.CreateTag()), dt);
            // f = 0 leaves the full rates, so vanilla's IsConvertible refuses the tick and shows
            // its "needs resource" status, as for an empty vanilla generator.
            float scale = fraction > 0f ? fraction : 1f;
            EnergyGenerator.InputItem[] inputs = generator.formula.inputs;
            inputs[0].consumptionRate = HydrogenKgPerSecond * scale;
            inputs[1].consumptionRate = OxygenKgPerSecond * scale;
            fuelRead = fraction > 0f
                && GeneratorEnthalpy.TryReadFuel(generator, dt, true, out fuelHeatCapacityKJPerK, out fuelTemperatureK);
            TickGenerator = generator;
            TickFraction = fraction;
        }

        internal void AfterTick(float dt)
        {
            TickGenerator = null;
            // Back to the full rates, which is what the building's descriptors show.
            EnergyGenerator.InputItem[] inputs = generator.formula.inputs;
            inputs[0].consumptionRate = HydrogenKgPerSecond;
            inputs[1].consumptionRate = OxygenKgPerSecond;

            // EnergySim200ms ends with operational.SetActive(burned this tick).
            if (!operational.IsActive || !(fraction > 0f) || !(dt > 0f))
            {
                return;
            }
            float waterKg = WaterKgPerSecond * fraction * dt;
            float waterShc = ElementLoader.FindElementByHash(SimHashes.Water).specificHeatCapacity;
            float ruleK = fuelRead
                ? WaterRuleTemperatureK(fuelHeatCapacityKJPerK, fuelTemperatureK, waterKg)
                : WaterTemperatureAtCalibrationK;
            float temperatureK = ruleK;
            if (temperatureK > WaterMaxTemperatureK)
            {
                // Above the clamp: the rest of the heat goes into the generator's body, named.
                temperatureK = WaterMaxTemperatureK;
                float excessKJ = waterKg * waterShc * (ruleK - temperatureK);
                GameComps.StructureTemperatures.ProduceEnergy(structureTemperature, excessKJ,
                    ClampHeatSource, dt);
                ClampExcessKJ += excessKJ;
                ClampTicks++;
            }
            waterStorage.AddLiquid(SimHashes.Water, waterKg, temperatureK, byte.MaxValue, 0,
                keep_zero_mass: true, do_disease_transfer: false);
            WaterKg += waterKg;
            WaterKJ += (double)waterKg * waterShc * temperatureK;
            BurnedHydrogenKg += HydrogenKgPerSecond * fraction * dt;
            BurnedOxygenKg += OxygenKgPerSecond * fraction * dt;
            if (fuelRead)
            {
                ReactantKJ += (double)fuelHeatCapacityKJPerK * fuelTemperatureK;
                ReactantHeatCapacityKJPerK += fuelHeatCapacityKJPerK;
            }
            Burns++;
            if (fraction < 0.999f)
            {
                PartialBurns++;
            }
        }

        internal static void CountJoules(float joules)
        {
            GeneratedJ += joules;
        }

        private float PassRoomKg()
        {
            return Mathf.Max(0f, PassThroughStoreKg - passStorage.MassStored());
        }

        /// <summary>
        /// Whether the input may take from the packet at the head of its pipe this conduit tick,
        /// and how much: the consumer's capacity is set so it takes no more than there is room for.
        /// - A reactant is taken only up to its store, so a correct mix that is oversupplied never
        ///   pushes fuel past the store. When the store's room is less than the packet, the rest of
        ///   it waits in the pipe (counted as HeldInPipe).
        /// - A reactant whose store is full waits in the pipe only while the other reactant will
        ///   still hold one burn tick at the next inlet take (HeldInPipe). Otherwise the store takes
        ///   what it can and the rest goes STRAIGHT into the pass-through store, as far as it has
        ///   room, in the same tick: an off mix passes its excess on
        ///   promptly and burns as often as the short reactant's supply allows, and the inlet cannot
        ///   deadlock behind one element while that line has room.
        /// - Anything else goes straight into the pass-through store the same way.
        /// The straight path never goes through the input store's capacity: one water test
        /// jammed the inlet for three phases with the oxygen store at 80 kg,
        /// no hydrogen and an oxygen sliver at the head. Vanilla Consume takes min(rate x dt,
        /// capacityKG - stored), and at 80 kg a float step is ~7.6e-6 kg, so a capacity of
        /// "stored + sliver" left no room at all. A sliver the pass store can hold is taken whole, so
        /// MinTakeKg never strands one at the head.
        /// With the pass-through line full or missing, gas it would need waits in the input pipe and
        /// the generator shows why.
        /// </summary>
        internal bool MayTake(ConduitFlow flow, float dt)
        {
            ConduitFlow.ConduitContents head = flow.GetContents(inputCell);
            if (!(head.mass > 0f))
            {
                SetPassThroughBlocked(false);
                return true;
            }
            float hydrogen = storage.GetMassAvailable(SimHashes.Hydrogen.CreateTag());
            float oxygen = storage.GetMassAvailable(SimHashes.Oxygen.CreateTag());
            float roomKg;
            bool otherCanBurn;
            bool reactant = true;
            switch (head.element)
            {
                // TRACK THE MIX: a held packet blocks every packet behind it, so
                // it may wait only while the other reactant will still hold a burn tick at the NEXT
                // inlet take, one conduit tick away. Checking only "a burn tick now" let the
                // other run down to its last tick with its own packets queued behind the hold, and
                // one water test burned 74 % of ticks on a 10 % H2-rich mix,
                // every missed tick short of oxygen.
                case SimHashes.Hydrogen:
                    roomKg = HydrogenStoreKg - hydrogen;
                    otherCanBurn = oxygen >= OxygenKgPerSecond * (dt + BurnTickSeconds);
                    break;
                case SimHashes.Oxygen:
                    roomKg = OxygenStoreKg - oxygen;
                    otherCanBurn = hydrogen >= HydrogenKgPerSecond * (dt + BurnTickSeconds);
                    break;
                default:
                    reactant = false;
                    roomKg = 0f;
                    otherCanBurn = false;
                    break;
            }
            if (roomKg > MinTakeKg)
            {
                bool storeLimits = roomKg < head.mass && roomKg < consumer.consumptionRate * dt;
                if (storeLimits && reactant && !otherCanBurn)
                {
                    // The rest of the packet would block the other reactant's packets while it runs
                    // short: fill the store and pass the rest on now (track the mix).
                    float restKg = Mathf.Min(head.mass - roomKg, PassRoomKg());
                    if (restKg > MinTakeKg || restKg >= head.mass - roomKg)
                    {
                        SetPassThroughBlocked(false);
                        ConduitFlow.ConduitContents kept = flow.RemoveElement(inputCell, roomKg);
                        if (kept.mass > 0f)
                        {
                            storage.AddGasChunk(kept.element, kept.mass, kept.temperature, kept.diseaseIdx,
                                kept.diseaseCount, keep_zero_mass: false, do_disease_transfer: false);
                        }
                        ConduitFlow.ConduitContents passed = flow.RemoveElement(inputCell, restKg);
                        if (passed.mass > 0f)
                        {
                            passStorage.AddGasChunk(passed.element, passed.mass, passed.temperature, passed.diseaseIdx,
                                passed.diseaseCount, keep_zero_mass: false, do_disease_transfer: false);
                            BookPassed(passed.element, passed.mass);
                            EscapeTakes++;
                        }
                        MirrorConsumeBooks(kept.mass > 0f || passed.mass > 0f);
                        return false;
                    }
                    // The pass-through line has no room: take what the store can, the rest waits,
                    // and the generator says why.
                    PassThroughBlockedTicks++;
                    SetPassThroughBlocked(true);
                    HeldInPipe++;
                    consumer.capacityKG = storage.MassStored() + roomKg;
                    return true;
                }
                // Into the input store, up to its room. Room this large is far above a float step
                // at any store mass, so vanilla's capacity arithmetic sees it.
                if (storeLimits)
                {
                    // The store, not the inlet's rate, limits the take: part of the packet waits.
                    HeldInPipe++;
                }
                SetPassThroughBlocked(false);
                consumer.capacityKG = storage.MassStored() + roomKg;
                return true;
            }
            if (reactant && otherCanBurn)
            {
                // An oversupplied correct mix: wait in the pipe; the burn makes room, and the other
                // reactant lasts past the next take.
                HeldInPipe++;
                SetPassThroughBlocked(false);
                MirrorConsumeBooks(false);
                return false;
            }
            // The deadlock escape for a reactant, or an inert gas: it can only go out of the
            // pass-through port, so it goes straight to that store, as far as it has room.
            float takeKg = Mathf.Min(head.mass, PassRoomKg());
            if (takeKg < head.mass && !(takeKg > MinTakeKg))
            {
                PassThroughBlockedTicks++;
                SetPassThroughBlocked(true);
                MirrorConsumeBooks(false);
                return false;
            }
            SetPassThroughBlocked(false);
            ConduitFlow.ConduitContents taken = flow.RemoveElement(inputCell, takeKg);
            if (taken.mass > 0f)
            {
                passStorage.AddGasChunk(taken.element, taken.mass, taken.temperature, taken.diseaseIdx,
                    taken.diseaseCount, keep_zero_mass: false, do_disease_transfer: false);
                BookPassed(taken.element, taken.mass);
                if (reactant)
                {
                    EscapeTakes++;
                }
            }
            // Vanilla's Consume does not run: the packet is already where it goes.
            MirrorConsumeBooks(taken.mass > 0f);
            return false;
        }

        /// <summary>
        /// What vanilla ConduitConsumer.Consume would have left on the consumer, for the paths that
        /// skip it. Every such path has gas at the inlet, so the inlet
        /// is satisfied if it is connected; RequireInputs reads IsSatisfied
        /// for the pipesHaveMass flag, and a value left over from an earlier call that saw an empty
        /// pipe would stop the generator with gas at its inlet (release review of 24f4633; a
        /// candidate for run 2's stop after 12.6 s, unverified).
        /// </summary>
        private void MirrorConsumeBooks(bool took)
        {
            consumer.IsSatisfied = consumer.IsConnected;
            consumer.consumedLastTick = took;
        }

        private static void BookPassed(SimHashes id, float kg)
        {
            switch (id)
            {
                case SimHashes.Hydrogen: PassedHydrogenKg += kg; break;
                case SimHashes.Oxygen: PassedOxygenKg += kg; break;
                default: PassedOtherKg += kg; break;
            }
        }

        private void SetPassThroughBlocked(bool blocked)
        {
            if (blocked == passThroughBlocked)
            {
                return;
            }
            passThroughBlocked = blocked;
            blockedStatusHandle = selectable.ToggleStatusItem(BlockedStatus, blockedStatusHandle, blocked, this);
        }

        internal static bool TryGet(ConduitConsumer consumer, out HydrogenCombustor combustor)
        {
            return byConsumer.TryGetValue(consumer, out combustor);
        }

        /// <summary>
        /// Keep the input store to what the generator can burn. Anything that is not hydrogen or
        /// oxygen, and hydrogen or oxygen above its store, moves to the pass-through store and leaves
        /// by the secondary gas port, unburned, as far as that store has room. Mass, heat and germs
        /// move with it. What does not fit waits in the input store, and MayTake holds the inlet.
        /// </summary>
        public void Sim200ms(float dt)
        {
            if (storage.items.Count == 0)
            {
                return;
            }
            held.Clear();
            for (int i = 0; i < storage.items.Count; i++)
            {
                GameObject item = storage.items[i];
                PrimaryElement primary = item == null ? null : item.GetComponent<PrimaryElement>();
                if (primary != null && primary.Mass > 0f && !held.Contains(primary.ElementID))
                {
                    held.Add(primary.ElementID);
                }
            }
            for (int i = 0; i < held.Count; i++)
            {
                SimHashes id = held[i];
                float keepKg;
                switch (id)
                {
                    case SimHashes.Hydrogen: keepKg = HydrogenStoreKg; break;
                    case SimHashes.Oxygen: keepKg = OxygenStoreKg; break;
                    default: keepKg = 0f; break;
                }
                Element element = ElementLoader.FindElementByHash(id);
                if (element == null || !element.IsGas)
                {
                    // A gas pipe delivers only gas; nothing else can be here.
                    continue;
                }
                float surplusKg = Mathf.Min(storage.GetMassAvailable(element.tag) - keepKg, PassRoomKg());
                if (!(surplusKg > MinTakeKg))
                {
                    continue;
                }
                storage.ConsumeAndGetDisease(element.tag, surplusKg, out float movedKg,
                    out SimUtil.DiseaseInfo disease, out float temperatureK);
                if (!(movedKg > 0f))
                {
                    continue;
                }
                passStorage.AddGasChunk(id, movedKg, temperatureK, disease.idx, disease.count,
                    keep_zero_mass: false, do_disease_transfer: false);
                BookPassed(id, movedKg);
            }
        }

        public List<Descriptor> GetDescriptors(GameObject go)
        {
            string water = ElementLoader.FindElementByHash(SimHashes.Water).tag.ProperName();
            string rate = GameUtil.GetFormattedMass(WaterKgPerSecond, GameUtil.TimeSlice.PerSecond);
            string temperature = GameUtil.GetFormattedTemperature(WaterTemperatureAtCalibrationK);
            Descriptor descriptor = default(Descriptor);
            descriptor.SetupDescriptor(
                string.Format(UI.BUILDINGEFFECTS.ELEMENTEMITTED_MINORENTITYTEMP, water, rate, temperature),
                string.Format(UI.BUILDINGEFFECTS.TOOLTIPS.ELEMENTEMITTED_MINORENTITYTEMP, water, rate, temperature));
            return new List<Descriptor> { descriptor };
        }

        internal static void AttachPassThroughPort(GameObject go)
        {
            go.AddComponent<ConduitSecondaryOutput>().portInfo = PassThroughPort;
        }
    }

    [HarmonyPatch(typeof(HydrogenGeneratorConfig), nameof(HydrogenGeneratorConfig.CreateBuildingDef))]
    internal static class HydrogenGeneratorConfig_CreateBuildingDef
    {
        private static void Postfix(BuildingDef __result)
        {
            // The water leaves by a LIQUID pipe, bottom right. The secondary gas port (2,2) is
            // attached as a component (HydrogenCombustor.AttachPassThroughPort), as the Gas Filter's.
            __result.OutputConduitType = ConduitType.Liquid;
            __result.UtilityOutputOffset = new CellOffset(2, 0);

            // Here and not in OnLoad: these keys are Klei's own, registered by the time the
            // building defs are made, so an earlier add would be overwritten.
            const string prefix = "STRINGS.BUILDINGS.PREFABS.HYDROGENGENERATOR.";
            Strings.Add(prefix + "DESC",
                "Hydrogen generators burn hydrogen with oxygen. The only exhaust is hot water, which has to be cooled.");
            Strings.Add(prefix + "EFFECT",
                "Burns " + UI.FormatAsLink("Hydrogen Gas", "HYDROGEN") + " with " + UI.FormatAsLink("Oxygen", "OXYGEN")
                + " (1 part to 8 by mass) from one mixed gas pipe into electrical " + UI.FormatAsLink("Power", "POWER") + ".\n\n"
                + "Pipes out hot " + UI.FormatAsLink("Water", "WATER") + " (about 80 °C) from a liquid port at the bottom "
                + "right, and stops when that pipe is missing or full. An off mix makes less power, and the unused gas "
                + "leaves by a second, gas port at the top right; pipe it away or the generator backs up. "
                + "Holds up to 90 kg of gas inside.");

            string status = "STRINGS.BUILDING.STATUSITEMS.HYDROGENGENERATORPASSBLOCKED.";
            Strings.Add(status + "NAME", "Unused gas has nowhere to go");
            Strings.Add(status + "TOOLTIP",
                "The feed carries gas this generator cannot burn (too much of one reactant, or another gas). It "
                + "leaves by the gas port at the top right, and that line is missing or full, so the input pipe "
                + "is backing up.");

            // The fw rule would price nothing here (the formula has no outputs), but say so
            // rather than leave its books to find out.
            GeneratorEnthalpy.Exempt(HydrogenCombustor.PrefabId,
                "Mod 2 burns hydrogen with oxygen and writes the water itself at the same calibrated rule");
        }
    }

    [HarmonyPatch(typeof(HydrogenGeneratorConfig), nameof(HydrogenGeneratorConfig.DoPostConfigureComplete))]
    internal static class HydrogenGeneratorConfig_DoPostConfigureComplete
    {
        private static void Postfix(GameObject go)
        {
            // One mixed pipe into vanilla's storage (the first Storage, which the consumer
            // and the generator both resolve to).
            ConduitConsumer consumer = go.GetComponent<ConduitConsumer>();
            consumer.capacityTag = GameTags.Any;
            consumer.capacityKG = HydrogenCombustor.InputCapacityKg;

            EnergyGenerator generator = go.GetComponent<EnergyGenerator>();
            EnergyGenerator.Formula formula = default(EnergyGenerator.Formula);
            // Hydrogen first: the generator's meter reads inputs[0]. HydrogenCombustor scales both
            // rates each tick to the limiting reagent.
            formula.inputs = new EnergyGenerator.InputItem[2]
            {
                new EnergyGenerator.InputItem(SimHashes.Hydrogen.CreateTag(), HydrogenCombustor.HydrogenKgPerSecond,
                    HydrogenCombustor.HydrogenStoreKg),
                new EnergyGenerator.InputItem(SimHashes.Oxygen.CreateTag(), HydrogenCombustor.OxygenKgPerSecond,
                    HydrogenCombustor.OxygenStoreKg),
            };
            // Not null: EnergySim200ms walks it on every burn.
            formula.outputs = new EnergyGenerator.OutputItem[0];
            generator.formula = formula;

            // The water: its own storage, emptied by the primary (liquid) dispenser, which runs while
            // the generator is operational (vanilla); RequireOutputs stops the generator when that
            // line is missing or full.
            Storage waterStorage = go.AddComponent<Storage>();
            waterStorage.showInUI = true;
            waterStorage.capacityKg = HydrogenCombustor.WaterStoreKg;
            ConduitDispenser water = go.AddOrGet<ConduitDispenser>();
            water.conduitType = ConduitType.Liquid;
            water.storage = waterStorage;
            water.elementFilter = null;

            // The unburned gas: a third storage, emptied into the secondary gas port by
            // HydrogenCombustor.PassUpdate even while the generator is stopped, so an off mix drains
            // whenever its line can. Not a second ConduitDispenser: see PassUpdate.
            Storage passStorage = go.AddComponent<Storage>();
            passStorage.showInUI = true;
            passStorage.capacityKg = HydrogenCombustor.PassThroughStoreKg;
            HydrogenCombustor.AttachPassThroughPort(go);

            HydrogenCombustor combustor = go.AddOrGet<HydrogenCombustor>();
            combustor.waterStorage = waterStorage;
            combustor.passStorage = passStorage;
        }
    }

    /// <summary>The secondary port's icon while placing and building, as GasFilterConfig attaches
    /// its own. HydrogenGeneratorConfig does not override these, so the base methods are patched
    /// and gated to it.</summary>
    [HarmonyPatch(typeof(IBuildingConfig), nameof(IBuildingConfig.DoPostConfigurePreview))]
    internal static class IBuildingConfig_DoPostConfigurePreview_Combustor
    {
        private static void Postfix(IBuildingConfig __instance, GameObject go)
        {
            if (__instance is HydrogenGeneratorConfig)
            {
                HydrogenCombustor.AttachPassThroughPort(go);
            }
        }
    }

    [HarmonyPatch(typeof(IBuildingConfig), nameof(IBuildingConfig.DoPostConfigureUnderConstruction))]
    internal static class IBuildingConfig_DoPostConfigureUnderConstruction_Combustor
    {
        private static void Postfix(IBuildingConfig __instance, GameObject go)
        {
            if (__instance is HydrogenGeneratorConfig)
            {
                HydrogenCombustor.AttachPassThroughPort(go);
            }
        }
    }

    [HarmonyPatch(typeof(EnergyGenerator), nameof(EnergyGenerator.EnergySim200ms))]
    internal static class EnergyGenerator_EnergySim200ms_Combustor
    {
        private static void Prefix(EnergyGenerator __instance, float dt, out HydrogenCombustor __state)
        {
            __state = __instance.GetComponent<HydrogenCombustor>();
            if (__state != null)
            {
                __state.BeforeTick(dt);
            }
        }

        private static void Postfix(float dt, HydrogenCombustor __state)
        {
            if (__state != null)
            {
                __state.AfterTick(dt);
            }
        }
    }

    /// <summary>
    /// Keeps the Hydrogen Generator's input-store fuel OFF the sim. A stored gas item keeps a sim element chunk unless it is unregistered, and
    /// PrimaryElement.Temperature reads that chunk (SimTemperatureTransfer.OnGetTemperature).
    /// Storage.AddGasChunk merges every intake at GetFinalTemperature(primaryElement.Temperature,
    /// ...) and writes the result into InternalTemperature, so a chunk value a publish had put back
    /// (OnSetTemperature writes the managed copy at once; SetElementChunkData lands a call or two
    /// later) leaked into the managed temperature on every intake: one water test
    /// burned the 150 K feed at 221 K and the 345 K feed at 308 K, even with the stored-temperature read.
    /// Klei's Insulate modifier is not enough: it only sets STT.enabled = false, and OnCmpDisable
    /// keeps the handle, so the getter still reads the chunk. This unregisters it.
    ///
    /// - Stored (Storage.ApplyStoredItemModifiers, is_stored): the managed InternalTemperature is
    ///   saved, the STT disabled, SimUnregister called, and InternalTemperature restored AFTER it
    ///   (SimUnregister and OnCmpDisable both copy the chunk's value in first). A registration
    ///   still pending (handle -2) is cancelled the same way: SimUnregister sets -1 and Klei's
    ///   OnSimRegistered then removes the chunk it was handed.
    /// - Unstored (Remove, Transfer, every Drop through MakeWorldActive, deconstruction's DropAll):
    ///   the STT gets back the enabled state it had, and OnCmpEnable re-registers it once
    ///   (SimRegister runs only at handle -1) at InternalTemperature.
    /// - Consumed to zero: a zero-mass item stays stored and off the sim; one that is destroyed
    ///   is unregistered by its own OnCleanUp (handle already -1) and dropped from the table.
    /// - Loaded: Storage.OnSpawn re-applies the modifiers, but SimUnregister does nothing while a
    ///   scene loads, so Sweep (every burn tick) finishes the job and counts it.
    /// With no chunk the stored fuel trades no heat with the building's cell; Stationeers'
    /// generator atmosphere does.
    /// </summary>
    internal static class FuelStoreOffSim
    {
        private static readonly HashSet<Storage> stores = new HashSet<Storage>();

        // Each item taken off the sim, with the enabled state its STT had before.
        private static readonly Dictionary<SimTemperatureTransfer, bool> offSim =
            new Dictionary<SimTemperatureTransfer, bool>();

        // SimTemperatureTransfer's `protected void SimUnregister()` (no parameters, the only
        // overload), as an open-instance delegate. Bound in Bind, not a field initialiser: a
        // throwing type initialiser would kill the whole class silently.
        private static System.Action<SimTemperatureTransfer> simUnregister;

        /// <summary>Chunks Sweep had to unregister (left on the sim by a load). The rig reads it.</summary>
        internal static int SweptChunks;

        /// <summary>Binds SimUnregister and logs the result once (Mod.OnLoad). Without it, items are
        /// still disabled while stored but keep their chunks, as Insulate does.</summary>
        internal static void Bind()
        {
            try
            {
                simUnregister = AccessTools.MethodDelegate<System.Action<SimTemperatureTransfer>>(
                    AccessTools.Method(typeof(SimTemperatureTransfer), "SimUnregister", System.Type.EmptyTypes));
            }
            catch (System.Exception e)
            {
                simUnregister = null;
                Debug.LogWarning("[Mod2MatterEnvironment] FuelStoreOffSim: SimUnregister did not bind: " + e.Message);
            }
            Debug.Log("[Mod2MatterEnvironment] FuelStoreOffSim: SimUnregister bound " + (simUnregister != null));
        }

        /// <summary>Drops both tables (Game.DestroyInstances, on leaving a game): every stored item
        /// and store of that world is gone, and a destroyed key would otherwise linger until a purge.</summary>
        internal static void Clear()
        {
            stores.Clear();
            offSim.Clear();
        }

        internal static void AddStore(Storage store)
        {
            if (store != null)
            {
                stores.Add(store);
            }
        }

        internal static void RemoveStore(Storage store)
        {
            stores.Remove(store);
            Purge();
        }

        internal static bool IsFuelStore(Storage store) => store != null && stores.Contains(store);

        /// <summary>Items in the store whose chunk is registered or pending (handle != -1).</summary>
        internal static int OnSimCount(Storage store)
        {
            int n = 0;
            foreach (GameObject item in store.items)
            {
                SimTemperatureTransfer stt = item == null ? null : item.GetComponent<SimTemperatureTransfer>();
                if (stt != null && stt.SimHandle != -1)
                {
                    n++;
                }
            }
            return n;
        }

        internal static void TakeOff(GameObject go)
        {
            SimTemperatureTransfer stt = go == null ? null : go.GetComponent<SimTemperatureTransfer>();
            PrimaryElement primary = go == null ? null : go.GetComponent<PrimaryElement>();
            if (stt == null || primary == null)
            {
                return;
            }
            if (!offSim.ContainsKey(stt))
            {
                offSim[stt] = stt.enabled;
            }
            float managedK = primary.InternalTemperature;
            stt.enabled = false;
            if (stt.SimHandle != -1 && !KMonoBehaviour.isLoadingScene && simUnregister != null)
            {
                simUnregister(stt);
            }
            // After both: OnCmpDisable and SimUnregister each copy the chunk's value in.
            primary.InternalTemperature = managedK;
        }

        internal static void PutBack(GameObject go)
        {
            if (offSim.Count == 0)
            {
                return;
            }
            SimTemperatureTransfer stt = go == null ? null : go.GetComponent<SimTemperatureTransfer>();
            if (stt == null || !offSim.TryGetValue(stt, out bool wasEnabled))
            {
                return;
            }
            offSim.Remove(stt);
            stt.enabled = wasEnabled;
        }

        internal static void Sweep(Storage store)
        {
            if (KMonoBehaviour.isLoadingScene)
            {
                return;
            }
            if (offSim.Count > 64)
            {
                Purge();
            }
            foreach (GameObject item in store.items)
            {
                SimTemperatureTransfer stt = item == null ? null : item.GetComponent<SimTemperatureTransfer>();
                if (stt == null || (stt.SimHandle == -1 && offSim.ContainsKey(stt)))
                {
                    continue;
                }
                if (stt.SimHandle != -1)
                {
                    SweptChunks++;
                }
                TakeOff(item);
            }
        }

        private static void Purge()
        {
            List<SimTemperatureTransfer> dead = null;
            foreach (SimTemperatureTransfer stt in offSim.Keys)
            {
                if (stt == null)
                {
                    (dead ?? (dead = new List<SimTemperatureTransfer>())).Add(stt);
                }
            }
            if (dead != null)
            {
                foreach (SimTemperatureTransfer stt in dead)
                {
                    offSim.Remove(stt);
                }
            }
        }
    }

    /// <summary>Clears FuelStoreOffSim's tables when a game is torn down (main menu, load).</summary>
    [HarmonyPatch(typeof(Game), "DestroyInstances")]
    internal static class Game_DestroyInstances_FuelStore
    {
        private static void Postfix() => FuelStoreOffSim.Clear();
    }

    /// <summary>Takes the input store's fuel off the sim as it is stored, and puts it back as it
    /// leaves (FuelStoreOffSim).</summary>
    // Storage's private method, the only overload: (GameObject go, bool is_stored, bool is_initializing).
    [HarmonyPatch(typeof(Storage), "ApplyStoredItemModifiers", new[] { typeof(GameObject), typeof(bool), typeof(bool) })]
    internal static class Storage_ApplyStoredItemModifiers_FuelStore
    {
        private static void Postfix(Storage __instance, GameObject go, bool is_stored)
        {
            if (is_stored)
            {
                if (FuelStoreOffSim.IsFuelStore(__instance))
                {
                    FuelStoreOffSim.TakeOff(go);
                }
            }
            else
            {
                // Not gated on the store: a deconstruction may drop the items after the
                // generator's OnCleanUp has removed it. PutBack acts only on an item it took off.
                FuelStoreOffSim.PutBack(go);
            }
        }
    }

    /// <summary>Power linear in what burned: f x the generator's joules for this tick.</summary>
    [HarmonyPatch(typeof(Generator), nameof(Generator.GenerateJoules))]
    internal static class Generator_GenerateJoules_Combustor
    {
        private static void Prefix(Generator __instance, ref float joulesAvailable)
        {
            if (HydrogenCombustor.TickGenerator != null && ReferenceEquals(__instance, HydrogenCombustor.TickGenerator))
            {
                joulesAvailable *= HydrogenCombustor.TickFraction;
                HydrogenCombustor.CountJoules(joulesAvailable);
            }
        }
    }

    /// <summary>The inlet takes each gas only as far as there is room for it (see
    /// HydrogenCombustor.MayTake).</summary>
    [HarmonyPatch(typeof(ConduitConsumer), "Consume")]
    internal static class ConduitConsumer_Consume_Combustor
    {
        private static bool Prefix(ConduitConsumer __instance, float dt, ConduitFlow conduit_mgr)
        {
            if (!HydrogenCombustor.TryGet(__instance, out HydrogenCombustor combustor))
            {
                return true;
            }
            return combustor.MayTake(conduit_mgr, dt);
        }
    }
}
