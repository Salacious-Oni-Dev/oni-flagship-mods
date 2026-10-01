using System.Collections.Generic;
using KSerialization;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// Liquid counterpart to Compressor.cs's gas tank/pump mechanic. Same two augmented vanilla buildings, same "augment, don't
    /// invent" move:
    ///
    ///   LiquidMixtureTankComponent (augments the vanilla Liquid Reservoir, LiquidReservoirConfig)
    ///   holds the stored liquid.
    ///
    ///   LiquidMixtureCompressorIntakeComponent (augments the vanilla Liquid Pump,
    ///   LiquidPumpConfig) pulls real vanilla liquid out of its own cell and dispenses it into a
    ///   real, connected liquid pipe.
    ///
    /// NOT a straight copy of the gas code: liquids are tracked by VOLUME (fill fraction), not
    /// ideal-gas pressure. Stationeers equalizes liquid networks by volume ratio, never the gas
    /// pressure formula -- the SimDLL's `EqualizeLiquidVolume` is the native kernel for that, exposed
    /// here as `GasMixtureFacade.EqualizeLiquidVolumeMass`. Everything else about the SHAPE
    /// mirrors the gas mechanic exactly: per-tile vanilla ConduitFlow, real
    /// GetUtilityInputCell/GetUtilityOutputCell, real AddElement/RemoveElement, the same
    /// competing-mover fix (GasMixtureFacade.EqualizeLiquidNetwork /
    /// ManagedLiquidConduitCells / ConduitFlowPatch.cs).
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class LiquidMixtureTankComponent : KMonoBehaviour
    {
        private Building building;

        // ONCE PER SIM TICK, not once per second. At a one-second cadence this tank drained its
        // own inlet more slowly than a vanilla Water Sieve fills it -- the sieve pushes 5 kg/s and
        // this took one equalising bite a second -- so the line backed up, the sieve reported a
        // blocked output and stopped, and the water loop stalled with the tank sitting half empty.
        // Vanilla's own reservoir consumer is `alwaysConsume` for exactly this reason: a tank's
        // job is to accept whatever the pipe offers.
        private const float PullIntervalSeconds = 0.2f;
        private float pullTimer;

        // Vanilla Liquid Reservoir's own Storage.capacityKg = 5000f
        // (LiquidReservoirConfig.ConfigureBuildingTemplate) -- reused directly, not invented.
        // Expressed as a fixed VOLUME (5 m^3, ~5000 kg of water-density liquid) rather than a
        // mass cap: a real tank's physical size doesn't change with what's poured into it, and
        // EqualizeLiquidVolumeMass needs a volume capacity, not a mass one.
        internal const float TankCapacityM3 = 5.0f;

        // Stationeers' liquid pipe volume, 20 L = 0.02 m^3, from the framework
        // (GasMixtureFacade.LiquidConduitVolumeM3) so both pipe volumes have one definition.
        internal const float PipeCapacityM3 = GasMixtureFacade.LiquidConduitVolumeM3;

        // Fallback density for a liquid the material registry has no record for. Vanilla ONI's
        // own `Element` class has NO density field at all, so
        // there is nothing to fall through TO; water-equivalent is the least-wrong single number
        // and the least-wrong single number.
        //
        // MaterialPropertyRegistry is the primary path: see LiquidDensityKgM3For below. This is
        // kept because the registry is
        // deliberately allowed to have no record for a substance -- an unregistered liquid must
        // still get SOME density rather than a divide-by-zero or a silent zero-volume tank.
        internal const float FallbackLiquidDensityKgM3 = 1000f;

        /// <summary>
        /// This liquid's real density (kg/m^3) from the material registry, falling back to
        /// <see cref="FallbackLiquidDensityKgM3"/> for a substance with no record.
        ///
        /// The liquid phase keys are registered, so `LiquidOxygen` and `LiquidCarbonDioxide`
        /// resolve to their real derived densities (1066 and 1100 kg/m^3) and `Water` to
        /// 1001 kg/m^3. The fallback only covers a substance genuinely absent from the registry.
        /// </summary>
        internal static float LiquidDensityKgM3For(SimHashes id)
        {
            return MaterialPropertyRegistry.TryGetLiquidDensityKgPerM3(id, out float densityKgM3)
                ? densityKgM3
                : FallbackLiquidDensityKgM3;
        }

        // Same reasoning as GasMixtureTankComponent.EqualizeRate: no artificial extra throttle,
        // this is a plain open pipe, not a valve/restrictor. The kernel's own built-in damping
        // still converges gradually over repeated ticks.
        private const float EqualizeRate = 1.0f;

        [Serialize] private List<int> savedElementIdx = new List<int>();
        [Serialize] private List<float> savedMassKg = new List<float>();
        [Serialize] private float temperatureK;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            building = GetComponent<Building>();
        }

        private void Update()
        {
            pullTimer += Time.deltaTime;
            if (pullTimer < PullIntervalSeconds)
            {
                return;
            }
            pullTimer = 0f;

            if (building == null)
            {
                return;
            }

            // GIVE BACK WHAT IT HOLDS, before taking any more in.
            //
            // FOUND LIVE, and it broke a whole water loop. This component augments vanilla's
            // Liquid Reservoir by pulling liquid out of the inlet pipe into its own mixture store
            // -- which is what lets a tank hold a real multi-species mixture instead of one
            // element. What it never did was DISPENSE, because vanilla's own `ConduitDispenser`
            // was supposed to do that from vanilla's `Storage`. But the mass never reaches that
            // Storage: it is in our store instead. So the tank filled, vanilla's contents panel
            // read "None", its dispenser had nothing to push, the pipe out of it stayed empty and
            // the Water Sieve downstream never ran. The two tooltips disagreeing -- ours saying
            // 306.72 kg, Klei's saying None -- was the whole diagnosis.
            //
            // A tank that takes liquid in and cannot give it back is not a tank, it is a hole.
            //
            // CONSERVATION AUDIT AROUND THE WHOLE TICK. Everything from here to the end of this
            // method is a closed system: mass may move between this tank's store and the liquid
            // network it is plumbed into, and it may move BETWEEN cells of that network, but the
            // sum of the two must not change. Nothing else runs between these two reads -- this
            // is a synchronous Unity `Update`.
            //
            // WHY IT IS HERE. A transfer that drops a packet shows up in a matter balance as a
            // loss in discrete steps rather than a leak, and reading the code cannot say which
            // mover did it; this measures instead of guessing.
            float auditBefore = AuditEnabled ? AuditMassKg() : 0f;
            Dispense();

            int inputCell = building.GetUtilityInputCell();
            ConduitFlow liquidFlow = Game.Instance.liquidConduitFlow;
            if (!Grid.IsValidCell(inputCell) || !liquidFlow.HasConduit(inputCell))
            {
                return;
            }

            ConduitFlow.ConduitContents contents = liquidFlow.GetContents(inputCell);

            int exchangeElementIdx;
            float tankMassForSpecies;
            float pipeMassForSpecies;
            float pipeTempK;

            if (contents.mass > 0f)
            {
                Element pipeElement = ElementLoader.FindElementByHash(contents.element);
                if (pipeElement == null)
                {
                    return;
                }
                exchangeElementIdx = ElementLoader.elements.IndexOf(pipeElement);
                if (exchangeElementIdx < 0)
                {
                    return;
                }
                pipeMassForSpecies = contents.mass;
                pipeTempK = contents.temperature;
                int idx = savedElementIdx.IndexOf(exchangeElementIdx);
                tankMassForSpecies = idx >= 0 ? savedMassKg[idx] : 0f;
            }
            else
            {
                if (savedElementIdx.Count == 0)
                {
                    return;
                }
                int bestIdx = 0;
                for (int k = 1; k < savedMassKg.Count; k++)
                {
                    if (savedMassKg[k] > savedMassKg[bestIdx])
                    {
                        bestIdx = k;
                    }
                }
                exchangeElementIdx = savedElementIdx[bestIdx];
                tankMassForSpecies = savedMassKg[bestIdx];
                pipeMassForSpecies = 0f;
                pipeTempK = Grid.Temperature[inputCell];
            }

            Element exchangeElement = ElementLoader.elements[exchangeElementIdx];

            float delta = GasMixtureFacade.EqualizeLiquidVolumeMass(
                LiquidDensityKgM3For(exchangeElement.id), tankMassForSpecies, TankCapacityM3,
                pipeMassForSpecies, PipeCapacityM3, EqualizeRate);

            // ONE TRANSFER, ONE SHADOW. Both branches below move liquid through `ConduitFlow`
            // themselves and move the dissolved cargo themselves, so they run inside
            // `DissolvedCargo.OwnMove()` -- which switches off the framework's own
            // `RemoveElement`/`AddElement` patches for the duration.
            //
            // WITHOUT IT THIS TANK VENTS DISSOLVED GAS INTO THE ROOM. The framework's
            // `RemoveElementPostfix` lifts share S of the inlet tile's cargo into a parcel, and
            // `SettleParcel` gives that parcel to the `ConduitConsumer` that took the liquid --
            // but this tank is deliberately NOT a `ConduitConsumer` (this mod turns the vanilla
            // one off in `LiquidReservoirConfig_DoPostConfigureComplete` and drinks with a direct
            // `RemoveElement`), so `SettleParcel` finds none and releases the parcel to the
            // world. The `ConduitToItem` call below then takes S of what is LEFT: about 15% of
            // what a pump lifts would go back to the room as gas.
            //
            // The scope wraps the cargo move as well as the `ConduitFlow` call, which is the
            // point: the framework must stay out of the WHOLE transfer, not just half of it.
            using (DissolvedCargo.OwnMove())
            {
                if (delta > 0f)
                {
                    // DISSOLVED CARGO, the intake half: what leaves the pipe brings its dissolved gas
                    // with it. Read the tile's mass BEFORE the removal, because the share is over
                    // what was there to take from, and `RemoveElement` may hand back less than
                    // `delta`.
                    float pipeMassBefore = liquidFlow.GetContents(inputCell).mass;
                    ConduitFlow.ConduitContents removed = liquidFlow.RemoveElement(inputCell, delta);
                    if (removed.mass > 0f)
                    {
                        float tookCargo = 0f;
                        if (pipeMassBefore > 0f)
                        {
                            tookCargo = DissolvedCargo.ConduitToItem(inputCell, gameObject,
                                removed.mass / pipeMassBefore);
                        }
                        AddMass(exchangeElementIdx, removed.mass, removed.temperature);
                    }
                }
                else if (delta < 0f)
                {
                    float pushKg = -delta;
                    // DISSOLVED CARGO, the outfall half. Same rule, same order: the share is over the
                    // tank's own mass before any of it leaves.
                    float tankMassBefore = TotalMassKg;
                    float accepted = liquidFlow.AddElement(inputCell, exchangeElement.id, pushKg, temperatureK, 0, 0);
                    if (accepted > 0f)
                    {
                        if (tankMassBefore > 0f)
                        {
                            DissolvedCargo.ItemToConduit(gameObject, inputCell,
                                accepted / tankMassBefore);
                        }
                        RemoveMass(exchangeElementIdx, accepted);
                    }
                }
            }

            if (EqualiseNetworkEnabled)
            {
                GasMixtureFacade.EqualizeLiquidNetwork(inputCell);
            }

            float auditAfter = AuditEnabled ? AuditMassKg() : auditBefore;
            if (Mathf.Abs(auditAfter - auditBefore) > AuditToleranceKg)
            {
                Mod1Log.Error(string.Format(
                    "LiquidMixtureTankComponent at cell {0}: this tick changed "
                    + "the total mass of (tank + its liquid network) by {1:F4} kg -- {2:F4} kg "
                    + "before, {3:F4} kg after. Tank {4:F4} kg. Matter is conserved is a project "
                    + "rule; one of Dispense, the pipe equalise or EqualizeLiquidNetwork is not "
                    + "keeping it.",
                    building != null ? building.GetUtilityInputCell() : -1,
                    auditAfter - auditBefore, auditBefore, auditAfter, TotalMassKg));
            }
        }

        /// <summary>
        /// Tolerance for the conservation audit in <c>Update</c>. One milligram, the same value
        /// <c>GasMixtureFacade.NegligibleTileMassKg</c> uses as the floor below which a network's
        /// remainder is consolidated rather than smeared -- so an audit trip means real mass, not
        /// float dust from a per-tile division.
        /// </summary>
        private const float AuditToleranceKg = 1e-3f;

        /// <summary>
        /// Kill switch for the network-wide liquid equalise, so an experiment can separate THIS
        /// mod's redistribution from vanilla's own per-tile mover without rebuilding twice. Left
        /// in place after the experiment that used it, because the question "is it ours or
        /// theirs" recurs every time a conduit loses mass.
        /// </summary>
        public static bool EqualiseNetworkEnabled = true;

        /// <summary>
        /// This tank's own store plus every kilogram in the liquid conduit network(s) it is
        /// plumbed into. Input and output are summed as a SET, because on a loop like a steam turbine's
        /// they are the same network and double-counting it would make a conserving tick look like
        /// it doubled the world's water.
        /// </summary>
        /// <summary>
        /// Whether the closed-system conservation audit around each tick actually runs.
        ///
        /// OFF BY DEFAULT, AND THE COST WAS THE REASON -- the same reason `LiquidLedger` is behind
        /// `--mod1-liquid-ledger`. <see cref="AuditMassKg"/> resolves this tank's INPUT and OUTPUT
        /// networks twice per tick for every Liquid Reservoir on the map. The run comes from
        /// <c>ConduitNetworks</c>' cached partition through <c>TryGetNetworkCells</c>, which
        /// allocates nothing, but the audit also logs, and how noisy the mod is by default is a
        /// gameplay decision.
        ///
        /// Measured on `harness-big` (23 reservoirs, ~970 liquid conduits): about 460 network
        /// flood-fills a second, 1.18 MB/s of garbage -- the largest single allocator this mod
        /// has, and the third largest in the whole game behind two of Klei's own. It does not show
        /// up as CPU time (0.083 ms average) because the cost is not paid here: it is paid later,
        /// in a stop-the-world collection on a 2.6 GB heap, which is where the ~700 ms frame
        /// spikes came from. A profiler sorted by time will never find this; one sorted by
        /// allocation finds it immediately.
        ///
        /// It is a DEBUGGING INSTRUMENT, added for an intermittent matter
        /// loss, and that investigation is closed -- the loss was vapour the rig was not counting.
        /// Keep the instrument, because the next transfer bug will want it; just do not make every
        /// colony pay for it. Arm it with `--mod1-tank-audit`.
        /// </summary>
        public static bool AuditEnabled =
            System.Array.IndexOf(System.Environment.GetCommandLineArgs(),
                "--mod1-tank-audit") >= 0;

        private float AuditMassKg()
        {
            float total = TotalMassKg;
            ConduitFlow flow = Game.Instance != null ? Game.Instance.liquidConduitFlow : null;
            if (flow == null || building == null)
            {
                return total;
            }

            var seen = new HashSet<int>();
            AccumulateNetwork(flow, building.GetUtilityInputCell(), seen, ref total);
            AccumulateNetwork(flow, building.GetUtilityOutputCell(), seen, ref total);
            return total;
        }

        private static void AccumulateNetwork(ConduitFlow flow, int cell, HashSet<int> seen,
            ref float total)
        {
            if (!Grid.IsValidCell(cell) || !flow.HasConduit(cell))
            {
                return;
            }
            if (!PipeNetworkFacade.TryGetNetworkCells(cell, PipeContentType.Liquid,
                    out int[] cells))
            {
                if (seen.Add(cell))
                {
                    total += flow.GetContents(cell).mass;
                }
                return;
            }
            for (int i = 0; i < cells.Length; i++)
            {
                if (seen.Add(cells[i]))
                {
                    total += flow.GetContents(cells[i]).mass;
                }
            }
        }

        /// <summary>
        /// Pushes this tank's contents out of its own output port, one species per tick.
        ///
        /// One species per tick because a liquid conduit tile holds a single element, exactly as a
        /// gas one does -- the same constraint the Gas Mixer and the Air Intake work around. The
        /// heaviest species goes first, so a tank holding mostly one thing empties it steadily
        /// rather than dribbling every trace in turn.
        ///
        /// It targets the FAR end of the outlet network before its own port, for the reason the
        /// Carbon Skimmer's discharge does: ONI fixes a conduit run's flow direction when the
        /// network is built, and a machine has no say in which way that came out. Putting the
        /// discharge beside itself works or does not work depending on that decision.
        /// </summary>
        private void Dispense()
        {
            if (building == null || savedElementIdx.Count == 0)
            {
                return;
            }
            ConduitFlow liquidFlow = Game.Instance.liquidConduitFlow;
            int outputCell = building.GetUtilityOutputCell();
            if (!Grid.IsValidCell(outputCell) || !liquidFlow.HasConduit(outputCell))
            {
                return;
            }

            int best = -1;
            for (int i = 0; i < savedElementIdx.Count && i < savedMassKg.Count; i++)
            {
                if (savedMassKg[i] > 0f && (best < 0 || savedMassKg[i] > savedMassKg[best]))
                {
                    best = i;
                }
            }
            if (best < 0)
            {
                return;
            }

            int elementIdx = savedElementIdx[best];
            if (elementIdx < 0 || elementIdx >= ElementLoader.elements.Count)
            {
                return;
            }
            Element element = ElementLoader.elements[elementIdx];
            if (element == null)
            {
                return;
            }

            float offerKg = Mathf.Min(savedMassKg[best], DispenseKgPerSecond * PullIntervalSeconds);
            if (offerKg <= 0f)
            {
                return;
            }
            float sendTemperatureK = temperatureK > 0f
                ? temperatureK
                : Grid.Temperature[outputCell];

            // WHAT GOES BACK OUT TAKES ITS DISSOLVED GAS WITH IT. The tank pulls water in through
            // the inlet WITH its cargo (`ConduitToItem`, above); pushing the same water back into
            // the network WITHOUT it would be a ratchet, concentrating the gas in the tank while
            // the pipe that feeds it runs lean.
            //
            // The whole transfer sits in `DissolvedCargo.OwnMove()` for the reason given at the
            // tank's intake: this method calls `AddElement` itself and accounts for the cargo
            // itself, so the framework's own patch must stay out of it. One transfer, one shadow.
            float tankMassBefore = TotalMassKg;
            float accepted = 0f;
            int landedCell = Grid.InvalidCell;
            using (DissolvedCargo.OwnMove())
            {
                if (PipeNetworkFacade.TryGetNetworkCells(outputCell, PipeContentType.Liquid,
                        out int[] outCells))
                {
                    // Farthest first, then everything else: same discharge-pump reasoning as
                    // CarbonSkimmer.PushDownstream, and asked for by name now that the cached
                    // partition no longer arrives in a per-caller order.
                    int farthest = PipeNetworkFacade.FarthestNetworkCell(outCells, outputCell);
                    if (farthest != Grid.InvalidCell)
                    {
                        accepted = liquidFlow.AddElement(farthest, element.id, offerKg,
                            sendTemperatureK, 0, 0);
                        if (accepted > 0f)
                        {
                            landedCell = farthest;
                        }
                    }
                    for (int i = outCells.Length - 1; i >= 0 && accepted <= 0f; i--)
                    {
                        if (outCells[i] == outputCell || outCells[i] == farthest)
                        {
                            continue;
                        }
                        accepted = liquidFlow.AddElement(outCells[i], element.id, offerKg,
                            sendTemperatureK, 0, 0);
                        if (accepted > 0f)
                        {
                            landedCell = outCells[i];
                        }
                    }
                }
                if (accepted <= 0f)
                {
                    accepted = liquidFlow.AddElement(outputCell, element.id, offerKg,
                        sendTemperatureK, 0, 0);
                    if (accepted > 0f)
                    {
                        landedCell = outputCell;
                    }
                }
                if (accepted <= 0f)
                {
                    return;
                }

                // The share is over the tank's WHOLE mass, not this species' share of it, because
                // that is what `ItemToConduit` divides: DissolvedCargo keeps one cargo record per carrier, not
                // one per element. Same convention as the outfall branch of the intake above.
                if (tankMassBefore > 0f && Grid.IsValidCell(landedCell))
                {
                    DissolvedCargo.ItemToConduit(gameObject, landedCell,
                        accepted / tankMassBefore);
                }

                savedMassKg[best] -= accepted;
                if (savedMassKg[best] <= 0f)
                {
                    savedMassKg.RemoveAt(best);
                    savedElementIdx.RemoveAt(best);
                }
            }
        }

        /// <summary>
        /// How fast the tank gives its contents back, kg/s. Vanilla's own Liquid Reservoir has no
        /// stated rate -- its `ConduitDispenser` simply fills the pipe -- so this is sized to a
        /// liquid conduit's own throughput rather than invented: 10 kg/s is what a vanilla liquid
        /// pipe carries.
        /// </summary>
        private const float DispenseKgPerSecond = 10f;

        public static LiquidMixtureTankComponent AtCell(int cell)
        {
            if (!Grid.IsValidCell(cell))
            {
                return null;
            }
            GameObject go = Grid.Objects[cell, (int)ObjectLayer.Building];
            return go == null ? null : go.GetComponent<LiquidMixtureTankComponent>();
        }

        /// <summary>
        /// Deliberately still <see cref="GasMixtureFacade.CombineTemperature"/> (plain
        /// calorimetry), NOT <see cref="GasMixtureFacade.AdiabaticFillTemperature"/> like the
        /// gas tank's own <c>AddMass</c> (<c>Compressor.cs</c>) uses: a liquid is
        /// practically incompressible at game-relevant pressures, so pumping it into an existing
        /// store does negligible real compression work compared to a gas -- the physical
        /// justification for the gas-side fix doesn't apply here, this isn't an oversight.
        /// </summary>
        public void AddMass(int elementIdx, float massKg, float sourceTempK)
        {
            if (massKg <= 0f)
            {
                return;
            }
            float existingMass = TotalMassKg;
            int i = savedElementIdx.IndexOf(elementIdx);
            if (i >= 0)
            {
                savedMassKg[i] += massKg;
            }
            else
            {
                savedElementIdx.Add(elementIdx);
                savedMassKg.Add(massKg);
            }
            temperatureK = GasMixtureFacade.CombineTemperature(existingMass, temperatureK, massKg, sourceTempK);
        }

        private void RemoveMass(int elementIdx, float massKg)
        {
            if (massKg <= 0f)
            {
                return;
            }
            int i = savedElementIdx.IndexOf(elementIdx);
            if (i < 0)
            {
                return;
            }
            savedMassKg[i] -= massKg;
            if (savedMassKg[i] <= 0f)
            {
                savedMassKg.RemoveAt(i);
                savedElementIdx.RemoveAt(i);
            }
        }

        public float TotalMassKg
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < savedMassKg.Count; i++)
                {
                    total += savedMassKg[i];
                }
                return total;
            }
        }

        public KeyValuePair<int, float>[] Composition()
        {
            var result = new KeyValuePair<int, float>[savedElementIdx.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new KeyValuePair<int, float>(savedElementIdx[i], savedMassKg[i]);
            }
            return result;
        }

        /// <summary>
        /// This tank's fill fraction (0..1) -- the liquid readout equivalent to
        /// GasMixtureTankComponent.TryGetPressurePa. Liquids don't have a meaningful "pressure"
        /// in this mod's scope (see class doc comment); fill fraction is the real quantity
        /// Stationeers itself reports for a liquid network.
        /// </summary>
        public bool TryGetFillFraction(out float fraction)
        {
            fraction = 0f;
            float totalMass = TotalMassKg;
            if (totalMass <= 0f || savedElementIdx.Count == 0)
            {
                return false;
            }
            // Real per-species volume (the material registry): a mixed tank's
            // occupied volume is the SUM of each species' own mass/density, not the total mass
            // divided by one shared density. Those agree only while every species resolves to the
            // same number, which was the case while a single placeholder density covered
            // everything -- so this is the correct form now that densities genuinely differ per
            // substance, not a rewrite of working math.
            float volumeM3 = 0f;
            for (int i = 0; i < savedElementIdx.Count; i++)
            {
                Element element = ElementLoader.elements[savedElementIdx[i]];
                volumeM3 += GasMixtureFacade.LiquidVolumeFromMass(savedMassKg[i],
                    LiquidDensityKgM3For(element.id));
            }
            fraction = volumeM3 / TankCapacityM3;
            return true;
        }
    }

    /// <summary>
    /// The source side of the liquid compressor pair -- see LiquidMixtureTankComponent's doc
    /// comment above. Augments the vanilla Liquid Pump (LiquidPumpConfig).
    /// </summary>
    public class LiquidMixtureCompressorIntakeComponent : MonoBehaviour
    {
        // WHETHER THE CARGO ACTUALLY CAME WITH THE WATER, counted rather than assumed.
        //
        // This component moves water straight into `ConduitFlow` and then asks
        // `DissolvedCargo.ConsumeToConduit` to move the dissolved gas that was in it. When that
        // returns false the water still goes -- the fallback below writes the mass off the grid
        // directly -- and the dissolved gas stays in the pond. Water arrives carrying nothing,
        // the global ledger still closes because nothing was destroyed, and the only symptom is
        // a destination that reads leaner than the source.
        //
        // These counters make that measurable: a destination reading leaner than its source
        // can be attributed here instead of argued about.
        //
        // Diagnostic only. Nothing reads them but rigs, and they are never persisted.
        public static double CarriedKg;
        public static double CargolessKg;
        public static int CargolessHits;

        /// <summary>
        /// Kilograms of DISSOLVED GAS that went with the water counted in
        /// <see cref="CarriedKg"/>. Taken from the source cell's own concentration at the moment
        /// of transfer rather than from the destination tile, because `ConsumeToConduit` works by
        /// sim message and the cargo does not land until the next tick -- a delta read here would
        /// always be zero. The sim splits the cell's payload in proportion to the mass it takes,
        /// which is the same proportion computed here.
        ///
        /// Paired with <see cref="CarriedKg"/> this gives the concentration actually DELIVERED
        /// over a window, which is the only honest thing to compare a destination against when
        /// the source is still enriching. Comparing against the source's final reading can make
        /// a delivery look like a transport loss when nothing was lost.
        /// </summary>
        public static double CarriedCargoKg;

        /// <summary>Zero the carriage counters. A rig calls this before the phase it measures.</summary>
        public static void ResetCarriageCounters()
        {
            CarriedKg = 0.0;
            CargolessKg = 0.0;
            CargolessHits = 0;
            CarriedCargoKg = 0.0;
        }

        private const float SampleIntervalSeconds = 1f;

        // Real vanilla Liquid Pump's own ElementConsumer.consumptionRate = 10f kg/s
        // (LiquidPumpConfig.DoPostConfigureComplete) -- vanilla's real
        // number, kept as the documented provenance for this constant.
        //
        // NOT used directly: vanilla's own conduit tiles have a real,
        // hard per-tile cap (`ConduitFlow.MAX_LIQUID_MASS = 10f`) --
        // dispensing at exactly that rate straight into ONE output tile every second (this
        // mod's simplified single-tile-per-dispense model, unlike vanilla's own multi-tile
        // conduit network which spreads the same 10kg/s consumption across a whole pipe run)
        // means a single dispense tick can peg that one tile at its own hard cap, which vanilla
        // reads as "conduit full" -- pausing the pump's own dispense animation and flashing the
        // blocked status, even though the tank's own equalization keeps draining it and real net
        // throughput stays healthy (confirmed live: ~2.5 kg/s sustained into the tank, tank mass
        // climbed 669->684 kg over 6 real seconds). Throttled to half of vanilla's real rate so
        // a single tick can never alone reach the tile's 10kg cap, removing the flicker while
        // keeping meaningful throughput -- the tank-side equalization damping was already the
        // real bottleneck at 10kg/s anyway (per-tick capacity was never actually the limiting
        // factor for real transfer volume, only for the cosmetic per-tile spike).
        private const float PumpRateKgPerSecond = 5f;

        // Real vanilla Liquid Pump's own ElementConsumer.consumptionRadius = 2,
        // same as the gas pump.
        private const int ConsumptionRadius = 2;

        private float timer;
        private Operational operational;
        private EnergyConsumer energyConsumer;
        private Building building;

        private void Start()
        {
            operational = GetComponent<Operational>();
            energyConsumer = GetComponent<EnergyConsumer>();
            building = GetComponent<Building>();
        }

        private void Update()
        {
            timer += Time.deltaTime;
            if (timer < SampleIntervalSeconds)
            {
                return;
            }
            timer = 0f;

            // Same fix as GasMixtureCompressorIntakeComponent's own Update() -- see its doc
            // comment for the full mechanism. `operational.IsOperational` ANDs in
            // `Pump.PumpableFlag`, driven by a vanilla quirk
            // (`Pump.IsPumpable()` only scans a +x/+y quarter-quadrant, not a real radius
            // square), which can report "nothing to pump" even with abundant real liquid
            // adjacent in any other direction -- exactly what this component's own
            // FindLiquidSourceCell already searches correctly. Bypasses PumpableFlag entirely;
            // checks only what the gate should have meant (broken, powered, logic-enabled).
            if (operational != null)
            {
                if (!operational.IsFunctional)
                {
                    return;
                }
                if (energyConsumer != null && !energyConsumer.IsPowered)
                {
                    return;
                }
                if (!operational.GetFlag(LogicOperationalController.LogicOperationalFlag))
                {
                    return;
                }
            }

            int pumpCell = Grid.PosToCell(transform.position);
            if (!Grid.IsValidCell(pumpCell))
            {
                return;
            }

            int sourceCell = FindLiquidSourceCell(pumpCell, ConsumptionRadius);
            if (sourceCell < 0)
            {
                return;
            }

            Element element = Grid.Element[sourceCell];
            float sourceMass = Grid.Mass[sourceCell];
            if (sourceMass <= 0f)
            {
                return;
            }

            if (building == null)
            {
                return;
            }

            int outputCell = building.GetUtilityOutputCell();
            ConduitFlow liquidFlow = Game.Instance.liquidConduitFlow;
            if (!Grid.IsValidCell(outputCell) || !liquidFlow.HasConduit(outputCell))
            {
                return;
            }

            // FOUND LIVE, right after the PumpRateKgPerSecond halving above turned
            // out NOT to actually fix the "Pipe Blocked" status flicker: `ConduitFlow.AddElement`
            // clamps its own accepted amount to `MaxMass - contents.mass` (// see the class doc comment's own quote of the real source), so as long as
            // `transferKg` exceeds whatever headroom the tile has left, the tile gets topped off
            // to EXACTLY `MAX_LIQUID_MASS` every single tick regardless of whether the ask was
            // 5kg or 10kg -- halving the ask amount doesn't matter once it's still bigger than
            // the remaining headroom, which it almost always is a tick after the tank last
            // drained it. Real fix: read the tile's own current mass first and only ever ask for
            // HALF its remaining headroom, so this component itself never tops a tile all the way
            // to the real vanilla cap -- `Pump.UpdateOperational`'s own `IsConduitFull`-driven
            // "Pipe Blocked" status (`ConduitFlow.IsConduitFull`: `MaxMass - contents.mass <= 0`)
            // then has no way to trigger from this mod's own dispensing.
            ConduitFlow.ConduitContents existingPipeContents = liquidFlow.GetContents(outputCell);
            float headroomKg = ConduitFlow.MAX_LIQUID_MASS - existingPipeContents.mass;
            if (headroomKg <= 0.01f)
            {
                // Already essentially at the real vanilla cap (draining via the tank's own
                // equalization, not this component) -- skip this tick rather than ask for
                // something that would immediately re-peg it.
                return;
            }

            float transferKg = Mathf.Min(sourceMass, PumpRateKgPerSecond * SampleIntervalSeconds);
            transferKg = Mathf.Min(transferKg, headroomKg * 0.5f);
            float sourceTempK = Grid.Temperature[sourceCell];

            // SAME RULE AS THE TANK: this component moves the liquid and accounts for the cargo
            // itself, so the framework's `AddElement` patch stays out of it. Nothing has been
            // measured coming through that patch on this path -- the cargo arrives by ticket from
            // `ConsumeToConduit` below, not from a parcel -- but a parcel left in flight by some
            // other building's `RemoveElement` earlier in the same frame would be claimed onto
            // this tile, and a shadow that only sometimes fires is worse than one that always
            // does. See DissolvedCargo.OwnMove.
            float accepted;
            using (DissolvedCargo.OwnMove())
            {
                accepted = liquidFlow.AddElement(outputCell, element.id, transferKg, sourceTempK, 0, 0);
            }
            if (accepted <= 0f)
            {
                return;
            }

            // THE GAS DISSOLVED IN THAT WATER COMES WITH IT.
            //
            // This component is why the carriage needed a third way in. DissolvedCargo patches vanilla's two
            // conduit endpoints -- `ElementConsumer` and `ConduitConsumer`/`ConduitDispenser` --
            // and this mod DISABLES both of them on the liquid pump (see
            // LiquidPumpConfig_DoPostConfigureComplete) and does the transfer itself, straight
            // into `ConduitFlow`. Without this, nothing dissolved would be carried end to end
            // through this mod's pump.
            //
            // `ConsumeToConduit` sends vanilla's own `SimMessages.ConsumeMass` for the same
            // kilograms this used to take with `kRemoveVanillaMass`, and the sim splits the cell's
            // dissolved payload in the same message that takes the mass. That atomicity is the
            // whole point: reading the lanes here and subtracting them in a second message would
            // leave a tick in which the cell held its full dissolved gas in less water than ever
            // held it. The mass still leaves on the next tick either way.
            if (!DissolvedCargo.ConsumeToConduit(sourceCell, element.id, accepted, outputCell))
            {
                // No carriage installed (a SimDLL without the property, or the framework not
                // asked to install DissolvedCargo). Fall back to the direct write this always did; the
                // dissolved gas, if any, is the sim's to release.
                //
                // COUNTED, because this is the one place where water moves and its dissolved
                // gas does not, and a destination reading lean has no other symptom.
                CargolessKg += accepted;
                CargolessHits++;
                GasMixtureFacade.RemoveVanillaMass(sourceCell, accepted);
            }
            else
            {
                CarriedKg += accepted;
                // The dissolved gas that went with it, in the same proportion the sim takes it:
                // what the source cell held, times the share of its mass that left.
                float sourceWaterKg = Grid.Mass[sourceCell];
                if (sourceWaterKg > 0f)
                {
                    CarriedCargoKg +=
                        OniFramework.DissolvedGas.Read(sourceCell, SimHashes.CarbonDioxide)
                        * (accepted / sourceWaterKg);
                }
            }
            GasMixtureFacade.EqualizeLiquidNetwork(outputCell);
        }

        /// <summary>
        /// Liquid counterpart to GasMixtureCompressorIntakeComponent.FindGasSourceCell -- same
        /// square-neighborhood search (vanilla's own native radius search isn't reachable from
        /// here, same reasoning as the gas version), filtered to Element.IsLiquid instead of
        /// IsGas.
        /// </summary>
        private static int FindLiquidSourceCell(int centerCell, int radius)
        {
            Element centerElement = Grid.Element[centerCell];
            if (centerElement != null && centerElement.IsLiquid && Grid.Mass[centerCell] > 0f)
            {
                return centerCell;
            }

            int bestCell = -1;
            float bestMass = 0f;
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }
                    int cell = Grid.OffsetCell(centerCell, dx, dy);
                    if (!Grid.IsValidCell(cell))
                    {
                        continue;
                    }
                    Element element = Grid.Element[cell];
                    if (element == null || !element.IsLiquid)
                    {
                        continue;
                    }
                    float mass = Grid.Mass[cell];
                    if (mass > bestMass)
                    {
                        bestMass = mass;
                        bestCell = cell;
                    }
                }
            }
            return bestCell;
        }
    }
}
