using System.Collections.Generic;
using KSerialization;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// A gas tank with real physics, on two augmented vanilla buildings (the same "augment, don't
    /// invent" move used for the Gas Element Sensor, Injector.cs, and Gas Vent, Extractor.cs):
    ///
    ///   GasMixtureTankComponent (augments the vanilla Gas Reservoir, GasReservoirConfig) holds
    ///   the compressed gas.
    ///
    ///   CompressorIntakeComponent (augments the vanilla Gas Pump, GasPumpConfig) is the source
    ///   side: every second it pulls vanilla atmosphere out of its own cell and dispenses it into
    ///   a connected gas pipe.
    ///
    /// ISOLATED STORAGE. The tank tracks its own mass and temperature in managed state
    /// (<see cref="speciesMassKg"/>/<see cref="temperatureK"/>), decoupled from any cell. A Gas
    /// Reservoir is a 5x3 building, so any one cell of it is one tile of a room the building
    /// shares with its own footprint; a real reservoir's storage is isolated from room
    /// atmosphere, which is why it needs pipes. Pressure is still real physics (P = nRT/V, the
    /// SimDLL's own formula, see <see cref="TankVolumeM3"/>).
    ///
    /// SAVE/LOAD. KSerialization round-trips List&lt;int&gt;/List&lt;float&gt; natively but not
    /// Dictionary&lt;K,V&gt;, so the two parallel lists (<see cref="savedElementIdx"/>/
    /// <see cref="savedMassKg"/>) ARE the tank's storage, not a save-time mirror. Species count
    /// per tank stays tiny, so linear lookups cost nothing that matters.
    ///
    /// PIPES. The intake dispenses into a gas conduit at its building's utility output cell
    /// (<c>Building.GetUtilityOutputCell()</c>) through <c>Game.Instance.gasConduitFlow.AddElement</c>
    /// -- the API vanilla's <c>ConduitDispenser</c> uses -- and the tank pulls from its utility
    /// input cell with <c>ConduitFlow.RemoveElement</c>, the API vanilla's <c>ConduitConsumer</c>
    /// uses. A pump only reaches a tank the player has piped it to: the conduit network IS the
    /// pairing.
    ///
    /// HEAT TRANSFER TO ENVIRONMENT. A tank that only ever heats up would be magic heat
    /// accumulation, so the tank registers one vanilla <c>ElementChunk</c> at its cell -- the
    /// mechanism every non-insulated stored item already uses (<c>SimTemperatureTransfer</c>) to
    /// conduct with the cell it sits in -- through <see cref="OniFramework.ElementChunkFacade"/>.
    /// Each tick it syncs its temperature IN from the chunk (conduction that already happened),
    /// runs its own logic, and pushes the result back OUT. Registration is asynchronous:
    /// <see cref="chunkHandle"/> holds one of Klei's sentinels until the callback fires, and every
    /// touch point tests it with <c>Sim.IsValidHandle</c>, which also accepts the valid handle 0.
    ///
    /// PHASE CHANGE with real latent heat. Vanilla's own phase change conserves temperature and
    /// lets the energy jump; neither the native nor the managed <c>Element</c> has a latent-heat
    /// field. The tank uses <see cref="OniFramework.GasMixtureFacade.ComputePhaseChangeStep"/>,
    /// which follows Stationeers' energy accounting, against its own mass-based storage.
    ///
    /// <see cref="ApplyPhaseChange"/> runs once per tick, after
    /// <see cref="SyncChunkTemperatureIn"/> and before the pipe exchange -- the same order as the
    /// sim's own kernels (conduction, then transition, then flow) -- and converts at most ONE
    /// species per tick, keeping the tank's single shared temperature well-defined.
    ///
    /// REFRIGERATION. <see cref="ApplyPhaseChange"/> gates boiling and condensation on the
    /// pressure-dependent vapour curve (<see cref="GasMixtureFacade.TryGetEvaporationTemperatureClampedK"/>
    /// at <see cref="TryGetVaporPressurePa"/>), so compressing the working fluid raises its
    /// condensation point. See RefrigerationLoop.cs for the two link components that turn a
    /// pair of these tanks into a closed loop, and <see cref="TryRemoveMass"/>, the extraction
    /// API they use.
    ///
    /// PHASE BOUNDARIES. The vapour curve applies ONLY to the liquid&lt;-&gt;gas boundary, and it is
    /// the CLAMPED curve (bounded to freezing..critical): the raw power-law fit returns an
    /// unphysical 89.28 K for water at 1 Pa. Freezing and melting are a different boundary with a
    /// flat threshold and their own, smaller, latent heat. A tank holding liquid with no vapour
    /// above it is simply the low-pressure end of the curve, which the clamp resolves to the
    /// freezing point.
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class GasMixtureTankComponent : KMonoBehaviour
    {
        private int cell;
        private Building building;

        // Not [Serialize] -- matches real vanilla SimTemperatureTransfer's own convention
        // ([SkipSaveFileSerialization] on the whole class): a chunk handle is a live native
        // registration, meaningless across a save/load boundary. OnSpawn re-registers a fresh
        // chunk on load if the tank already holds mass, the same way vanilla re-registers its
        // own chunks on every spawn regardless of save state.
        //
        // Test it with `Sim.IsValidHandle` (`h != -1 && h != -2`), never `> 0`: handles are
        // `slot | (version << 24)` with the first version 0, so the first ElementChunk registered
        // on a map gets handle exactly 0, and a `> 0` test would leave that tank a dead chunk that
        // never syncs, never pushes and never unregisters.
        private int chunkHandle = Sim.InvalidHandle;

        // Registration is asynchronous (see ElementChunkFacade.Register). Tracking the in-flight
        // state EXPLICITLY rather than inferring it from chunkHandle == Sim.QueuedRegisterHandle
        // is what prevents a chunk leak: resetting the handle when the tank empties
        // mid-registration would let a refill issue a SECOND Register, and the first callback
        // would then arrive with a real handle nothing holds -- an orphaned chunk permanently
        // exchanging heat with this cell, unreachable even by OnCleanUp.
        private bool chunkRegistrationPending;

        // Set once in OnCleanUp so a registration still in flight at teardown releases its handle
        // when the callback finally arrives, instead of leaking it the same way.
        private bool cleanedUp;

        private const float PullIntervalSeconds = 1f;
        private float pullTimer;

        // THE CLOBBER GUARD. These two fields exist so this component never writes
        // a value back into the sim that the sim itself gave it.
        //
        // `syncedInTemperatureK` is the chunk temperature SyncChunkTemperatureIn read at the top
        // of THIS pull; `lastPushedHeatCapacity` is the heat capacity this component last
        // actually pushed. Both are float.NaN whenever the answer is unknown, and NaN compares
        // false against everything, so an unknown always falls through to a real push.
        //
        // Why they are needed, measured: reading the chunk's temperature at the top of a pull and
        // pushing the identical value back at the bottom accounted for 99.8% of an energy rig's
        // imbalance, in the SimDLL's SetElementChunkData ledger bucket. That write is a QUEUED
        // message drained a frame later, by which time native StepElementChunks has already
        // moved the chunk -- so the write rolls the chunk BACK while the cell keeps the heat it
        // just received, manufacturing roughly one tick of conduction per pull.
        //
        // This is vanilla's own contract, not a new invention. SimTemperatureTransfer has NO
        // per-tick push at all -- it writes only
        // on events (OnDataChanged, OnSetTemperature, OnSimRegistered, OnCmpDisable) and guards
        // even its registration write behind `Mathf.Abs(temperature - internalTemperature) > 0.1f`.
        // We keep the per-pull CALL, because this tank really does change its own state
        // (ApplyPhaseChange, AddMass, RemoveMass); we drop the pushes that carry no such change.
        //
        // Exact equality is deliberate, not sloppy: SyncChunkTemperatureIn assigns the chunk's
        // temperature straight into temperatureK with no arithmetic, so "the managed side did not
        // touch it" is bit-exact, and any real managed change -- however small -- is a change we
        // are obliged to communicate. A tolerance here would silently swallow slow drift.
        private float syncedInTemperatureK = float.NaN;
        private float lastPushedHeatCapacity = float.NaN;

        // Placeholder volume for this tank, same reasoning as gas_mixture.h's own
        // kCellVolumeM3 -- there is no per-tank volume field. Pressure uses the shared native
        // formula (GasMixtureFacade.TryComputePressure). Internal so that a caller solving for a
        // temperature inside the liquid's stable band can use the real V.
        internal const float TankVolumeM3 = 1.0f;

        // Real gas conduit volume (Stationeers' 10 L pipe), from the framework rather than a
        // second copy here, so a tank equalizing against a pipe prices that pipe at its true
        // size.
        private const float PipeVolumeM3 = GasMixtureFacade.GasConduitVolumeM3;

        // A tank plumbed to a pipe with no valve between them is one connected system sharing
        // pressure, not an active pump moving mass at a fixed rate: as in Stationeers,
        // directionality lives in explicit valve BUILDINGS, never in a storage tank. So the
        // exchange is GasMixtureFacade.EqualizeSingleSpeciesMass, the native mixing kernel's own
        // equalization formula (gas::MixPair, generalized off two cells sharing one volume to
        // two containers with their own volumes) -- mass now flows whichever direction actually
        // equalizes pressure, tank<->pipe, same physics the per-cell atmosphere layer already
        // uses to equalize two neighboring cells. rate=1.0 here means "no artificial extra
        // throttle" (the connection is a plain open pipe, not a restrictor) -- the formula's own
        // built-in damping (see its doc comment) still converges gradually over repeated ticks
        // rather than teleporting to equilibrium instantly, which is why this still polls once a
        // second rather than needing a smaller rate.
        private const float EqualizeRate = 1.0f;

        // Parallel arrays, index-aligned: savedElementIdx[i] (an ElementLoader.elements index)
        // holds savedMassKg[i] kg. This tank's entire inventory -- nothing outside
        // AddMass/Composition/TryGetPressurePa touches it. [Serialize] is what makes this
        // survive save/load (see class doc comment's SAVE/LOAD note for why these are lists,
        // not a Dictionary).
        [Serialize] private List<int> savedElementIdx = new List<int>();
        [Serialize] private List<float> savedMassKg = new List<float>();
        [Serialize] private float temperatureK;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            cell = Grid.PosToCell(transform.position);
            building = GetComponent<Building>();
            // Loaded from a save with mass already in it -- register a fresh chunk now, the
            // same way vanilla's own SimTemperatureTransfer.OnSpawn always re-registers
            // regardless of whether this is a fresh spawn or a load.
            RegisterChunkIfNeeded();
        }

        protected override void OnCleanUp()
        {
            cleanedUp = true;
            if (Sim.IsValidHandle(chunkHandle))
            {
                ElementChunkFacade.Unregister(chunkHandle);
            }
            chunkHandle = Sim.InvalidHandle;
            lastPushedHeatCapacity = float.NaN;
            syncedInTemperatureK = float.NaN;
            base.OnCleanUp();
        }

        /// <summary>
        /// Registers this tank's real ElementChunk the first time it has any mass to represent
        /// (matches vanilla's own <c>SimTemperatureTransfer.SimRegister</c> mass&gt;0 gate --
        /// <c>SimMessages.AddElementChunk</c> is itself a no-op below that). Safe to call
        /// repeatedly -- does nothing once a registration is already pending or complete.
        /// </summary>
        private void RegisterChunkIfNeeded()
        {
            if (chunkRegistrationPending || Sim.IsValidHandle(chunkHandle) || TotalMassKg <= 0f)
            {
                return;
            }
            // Dominant species only flavors the registration -- SyncChunkDataOut immediately
            // corrects the real heat capacity every tick afterward regardless of which element
            // was picked here (see ElementChunkFacade.Register's own doc comment).
            int bestIdx = 0;
            for (int k = 1; k < savedMassKg.Count; k++)
            {
                if (savedMassKg[k] > savedMassKg[bestIdx])
                {
                    bestIdx = k;
                }
            }
            SimHashes dominant = ElementLoader.elements[savedElementIdx[bestIdx]].id;
            chunkRegistrationPending = true;
            // A registration's heat capacity is only FLAVORED by the dominant species (see the
            // comment just above), so the first SyncChunkDataOut after it must not be skipped --
            // it is the one that installs this tank's real mixed-species heat capacity.
            lastPushedHeatCapacity = float.NaN;
            syncedInTemperatureK = float.NaN;
            // Klei's own in-flight sentinel (-2); Sim.IsValidHandle rejects it alongside
            // InvalidHandle.
            chunkHandle = Sim.QueuedRegisterHandle;
            ElementChunkFacade.Register(cell, dominant, TotalMassKg, temperatureK,
                onRegistered: OnChunkRegistered);
        }

        /// <summary>
        /// Landing point for <see cref="ElementChunkFacade.Register"/>'s asynchronous callback.
        /// A registration in flight can outlive the reason it was issued -- the tank can empty,
        /// or the building can be deconstructed, before the handle ever arrives -- and in both
        /// cases the handle IS real by the time it gets here, so it has to be released or it
        /// leaks a chunk that keeps exchanging heat with this cell forever (the exact leak this
        /// method was added to close).
        /// </summary>
        private void OnChunkRegistered(int handle)
        {
            chunkRegistrationPending = false;
            if (cleanedUp || TotalMassKg <= 0f)
            {
                ElementChunkFacade.Unregister(handle);
                chunkHandle = Sim.InvalidHandle;
                return;
            }
            chunkHandle = handle;
        }

        /// <summary>
        /// Pulls in whatever real conduction vanilla's own native <c>StepElementChunks</c>
        /// already applied against this cell's grid temperature since the last sync -- called at
        /// the top of Update(), before this tick's own pipe-exchange/compression logic runs.
        /// </summary>
        private void SyncChunkTemperatureIn()
        {
            // Unknown until proven otherwise -- a pull that could not read the chunk must not let
            // this pull's push be skipped on the strength of some EARLIER pull's read.
            syncedInTemperatureK = float.NaN;
            if (Sim.IsValidHandle(chunkHandle) &&
                ElementChunkFacade.TryGetTemperature(chunkHandle, out float chunkTempK))
            {
                temperatureK = chunkTempK;
                syncedInTemperatureK = chunkTempK;
            }
        }

        /// <summary>
        /// Pushes this tick's resulting mass-weighted temperature and real heat capacity back
        /// into the chunk (or registers one for the first time, or unregisters an emptied tank's
        /// chunk) -- called once at the end of Update(), after any AddMass/RemoveMass this tick.
        /// </summary>
        private void SyncChunkDataOut()
        {
            float totalMass = TotalMassKg;
            if (totalMass <= 0f)
            {
                if (Sim.IsValidHandle(chunkHandle))
                {
                    ElementChunkFacade.Unregister(chunkHandle);
                }
                // A registration still in flight is deliberately NOT abandoned here -- clearing
                // the handle mid-flight is precisely what used to orphan a chunk (see
                // chunkRegistrationPending's own comment). OnChunkRegistered sees the emptied
                // tank and releases the handle itself.
                if (!chunkRegistrationPending)
                {
                    chunkHandle = Sim.InvalidHandle;
                }
                // The mirror describes a chunk that no longer exists.
                lastPushedHeatCapacity = float.NaN;
                syncedInTemperatureK = float.NaN;
                return;
            }
            if (!Sim.IsValidHandle(chunkHandle))
            {
                // Either never registered or still pending -- RegisterChunkIfNeeded no-ops in the
                // pending case.
                RegisterChunkIfNeeded();
                return;
            }

            // NO GramsPerKilogram HERE: that would be a 1000x energy error. Vanilla's own
            // ElementChunk heat capacity is the RAW, unscaled `mass_kg * specificHeatCapacity`,
            // i.e. Klei's kDTU/K, not J/K, on both sides:
            //   * real vanilla managed code -- SimTemperatureTransfer.OnDataChanged/OnMassChanged/
            //     OnSimRegistered each
            //     compute `primary_element.Mass * primary_element.Element.specificHeatCapacity`
            //     and pass exactly that into SetElementChunkData, unscaled.
            //   * the native kernel -- the SimDLL's sim/chunks.h stores it verbatim
            //     (ModifyChunk: `d->heat_capacity = m.heatCapacity;`) and hands it to
            //     CalculateTemperatureExchange against the CELL's own `mass * specificHeatCapacity`,
            //     also unscaled. Both sides of that exchange must share units.
            // Passing J/K would make this tank read as 1000x more thermally massive than it is, so
            // it would barely respond to environment conduction while the energy split against the
            // cell came out wrong in the other direction. The g->kg factor belongs only where
            // J/kg latent-heat math happens.
            float heatCapacity = 0f;
            for (int i = 0; i < savedElementIdx.Count; i++)
            {
                heatCapacity += savedMassKg[i] * ElementLoader.elements[savedElementIdx[i]].specificHeatCapacity;
            }
            // See syncedInTemperatureK's own comment. If the sim handed us this temperature at
            // the top of the pull and nothing managed-side moved it, and the heat capacity is
            // still the one we last pushed, then this write would carry no information -- and
            // because it lands a frame late it would carry MISinformation, rolling the chunk back
            // over conduction the sim has already done. Say nothing instead.
            if (temperatureK == syncedInTemperatureK && heatCapacity == lastPushedHeatCapacity)
            {
                return;
            }
            ElementChunkFacade.SetData(chunkHandle, temperatureK, heatCapacity);
            lastPushedHeatCapacity = heatCapacity;
        }

        // THE PHASE CHANGE ITSELF LIVES IN THE FRAMEWORK: OniFramework.PhaseVessel carries the
        // rule -- Stationeers' 10 %-per-tick rate
        // (PhaseVessel.PhaseChangeRatePerSecond), the 0.01 kg dust floor
        // (PhaseVessel.PhaseChangeMinRemainderKg), the per-boundary thresholds and latent heats,
        // and the g->kg factor below -- so the phase chambers (PhaseChambers.cs) and any
        // third-party vessel run this exact code rather than a copy of it.
        //
        // UNITS: `Element.specificHeatCapacity` is in
        // Klei's own raw content-data units, J/(g*K) -- e.g. Water=4.179, deliberately chosen to
        // read as "the familiar real-world J/(g*K) constant" -- NOT J/(kg*K). Every phase-change
        // energy calculation in this class multiplies it against a KG-based mass
        // (<see cref="savedMassKg"/>), so every one needs this explicit g->kg factor or the
        // resulting heat capacity (and therefore every derived convertedMassKg/temperature delta)
        // undercounts by exactly 1000x.
        //
        // SCOPE: it belongs ONLY where a heat capacity is about to meet a J/kg latent heat (the two
        // ApplyPhaseChange call sites). It does NOT belong on the heat capacity handed to
        // ElementChunkFacade.SetData -- vanilla's own chunk kernel works in unscaled kDTU/K on
        // BOTH sides of its exchange, so scaling one side there is a straight 1000x energy error.
        // See SyncChunkDataOut's own comment for the full ground truth. The rule: convert at the
        // point where OUR J/kg data meets Klei's per-gram data, never when handing a number back
        // to Klei's own kernels.
        //
        // The constant itself is PhaseVessel's now (private there); the rule it carries still
        // governs this class's own chunk handoff above.

        /// <summary>
        /// One tick's worth of real phase change -- see the class doc comment's "PHASE CHANGE"
        /// section. Scans this
        /// tank's own species for one whose temperature has crossed the threshold of a real
        /// vanilla transition, converts the energy-affordable share of its mass to the real
        /// vanilla transition-target element (same container, same list -- a phase change relabels
        /// matter already inside this tank, it doesn't move it anywhere), and pays the latent-heat
        /// cost against the TANK'S TOTAL heat capacity (every species' own share, not just the
        /// converting species' remaining mass) since this tank shares one temperature across its
        /// whole blend. Converts at most one species per call (real vanilla's own "one transition
        /// per cell per substep" rule).
        ///
        /// Each candidate transition is classified by which phase boundary it crosses
        /// (<see cref="PhaseVessel.ClassifyTransition"/>) so that boiling/condensation, melting/freezing and
        /// sublimation each get their own threshold rule and their own latent-heat cost, the way
        /// Stationeers' own model separates them. A single pressure-dependent vapor threshold for
        /// every transition would move a liquid's freezing point with its vapor pressure --
        /// physically wrong, and enough on its own to make freezing unreachable in practice.
        /// </summary>
        private void ApplyPhaseChange()
        {
            // Hoisted out of the per-species loop: this tank has ONE shared vapor
            // space, so its vapor pressure does not depend on which species the loop is currently
            // examining, and recomputing it per species would allocate for an answer that never
            // varies.
            TryGetVaporPressurePa(out float vaporPressurePa);

            PhaseVessel.ApplyPhaseChangeStep(savedElementIdx, savedMassKg, ref temperatureK,
                vaporPressurePa, PullIntervalSeconds, out PhaseTransitionKind _, out bool _,
                out float _, out float _);
        }

        // Still useful for anyone identifying which building this is -- just no longer where
        // its gas actually lives.
        public int Cell => cell;

        /// <summary>
        /// This tank's real current temperature, K -- exposed read-only so a caller (e.g.
        /// CompressorScreenshotDemo.cs) can verify <see cref="GasMixtureFacade.AdiabaticFillTemperature"/>'s
        /// real compression-heat effect directly, the same way <see cref="TryGetPressurePa"/>
        /// already exposes the pressure side.
        /// </summary>
        public float TemperatureK => temperatureK;

        private void Update()
        {
            pullTimer += Time.deltaTime;
            if (pullTimer < PullIntervalSeconds)
            {
                return;
            }
            pullTimer = 0f;

            // Real environment conduction (ElementChunkFacade) applies regardless of whether a
            // pipe is connected -- sync it in before any early return below.
            SyncChunkTemperatureIn();

            // Real vanilla substep order for the equivalent kernels: "conduction, then this
            // [state change], then flow" (sim/physics.h) -- ApplyPhaseChange sees this tick's
            // real post-conduction temperature and runs before any pipe exchange moves mass.
            ApplyPhaseChange();

            // EVERY early return from here on must still push data out (fixed -- four
            // of them didn't). ApplyPhaseChange has already mutated this tank's mass and
            // temperature by this point, so returning without syncing leaves vanilla's own native
            // conduction running against stale state for that tick. The two returns that already
            // did this correctly said so; the rest were simply inconsistent.
            if (building == null)
            {
                SyncChunkDataOut();
                return;
            }

            // Real vanilla per-building offset (rotation-aware), same call the real Gas
            // Reservoir's own ConduitConsumer makes -- not a hardcoded neighbor cell.
            int inputCell = building.GetUtilityInputCell();
            ConduitFlow gasFlow = Game.Instance.gasConduitFlow;
            if (!Grid.IsValidCell(inputCell) || !gasFlow.HasConduit(inputCell))
            {
                // No pipe connected here -- nothing to equalize against, same as the real
                // ConduitConsumer's own IsConnected gate. Still push out whatever
                // SyncChunkTemperatureIn just pulled in, so a disconnected tank keeps its chunk
                // (and therefore its own thermal exchange with the environment) alive.
                SyncChunkDataOut();
                return;
            }

            ConduitFlow.ConduitContents contents = gasFlow.GetContents(inputCell);

            // A real pipe segment only ever carries ONE element at a time (vanilla's own
            // AddElement/RemoveElement constraint), so at most one species can exchange with
            // the tank this tick regardless of how many species the tank itself holds.
            int exchangeElementIdx;
            float tankMassForSpecies;
            float pipeMassForSpecies;
            float pipeTempK;

            if (contents.mass > 0f)
            {
                Element pipeElement = ElementLoader.FindElementByHash(contents.element);
                if (pipeElement == null)
                {
                    SyncChunkDataOut();
                    return;
                }
                exchangeElementIdx = ElementLoader.elements.IndexOf(pipeElement);
                if (exchangeElementIdx < 0)
                {
                    SyncChunkDataOut();
                    return;
                }
                pipeMassForSpecies = contents.mass;
                pipeTempK = contents.temperature;
                int idx = savedElementIdx.IndexOf(exchangeElementIdx);
                tankMassForSpecies = idx >= 0 ? savedMassKg[idx] : 0f;
            }
            else
            {
                // Empty pipe -- nothing constrains which species could flow out, so offer the
                // tank's own dominant species (highest mass), the same "pick the biggest one"
                // simplification Extractor.cs already uses for its own single-species release.
                // A tank holding nothing has nothing to equalize either way.
                if (savedElementIdx.Count == 0)
                {
                    // Empty tank -- nothing to push to the pipe, but SyncChunkDataOut still
                    // needs to run (it's what actually unregisters an emptied tank's chunk).
                    SyncChunkDataOut();
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
                // Only meaningful if mass actually flows into the (currently empty) pipe --
                // the pipe's own ambient cell temperature is the closest real value available.
                pipeTempK = Grid.Temperature[inputCell];
            }

            Element exchangeElement = ElementLoader.elements[exchangeElementIdx];
            float molarMass = exchangeElement.molarMass;
            if (molarMass <= 0f)
            {
                SyncChunkDataOut();
                return;
            }

            // Positive: mass should flow FROM the pipe INTO the tank. Negative: the tank is the
            // higher-pressure side and should push OUT into the pipe instead -- both directions
            // are real now, not just "tank always pulls."
            float delta = GasMixtureFacade.EqualizeSingleSpeciesMass(
                molarMass, tankMassForSpecies, temperatureK, TankVolumeM3,
                pipeMassForSpecies, pipeTempK, PipeVolumeM3, EqualizeRate);

            if (delta > 0f)
            {
                ConduitFlow.ConduitContents removed = gasFlow.RemoveElement(inputCell, delta);
                if (removed.mass > 0f)
                {
                    AddMass(exchangeElementIdx, removed.mass, removed.temperature);
                }
            }
            else if (delta < 0f)
            {
                float pushKg = -delta;
                float accepted = gasFlow.AddElement(inputCell, exchangeElement.id, pushKg, temperatureK, 0, 0);
                if (accepted > 0f)
                {
                    RemoveMass(exchangeElementIdx, accepted);
                }
            }

            // Real vanilla conduits are per-tile (see this method's own EqualizePipeNetwork
            // call site below for the full writeup) -- without this, only the ONE tile at
            // inputCell would ever reflect the exchange, and the rest of the run would stay
            // wherever vanilla's own slow per-tile flow last left it. Forces the whole connected
            // network back to one shared, correct state immediately.
            GasMixtureFacade.EqualizePipeNetwork(inputCell);

            // Push this tick's resulting mass/temperature back into the real ElementChunk so
            // vanilla's own native conduction acts on the correct state next substep.
            SyncChunkDataOut();
        }

        /// <summary>
        /// The tank whose building footprint occupies <paramref name="cell"/>, or null.
        /// Real vanilla building lookup (Grid.Objects/ObjectLayer.Building -- same way any
        /// other system asks "what's built here"), used by HoverPatch.cs/Overlay.cs to show
        /// THIS tank's own isolated data when the cursor is anywhere over the Reservoir's
        /// footprint, not just its one anchor cell.
        /// </summary>
        public static GasMixtureTankComponent AtCell(int cell)
        {
            if (!Grid.IsValidCell(cell))
            {
                return null;
            }
            GameObject go = Grid.Objects[cell, (int)ObjectLayer.Building];
            return go == null ? null : go.GetComponent<GasMixtureTankComponent>();
        }

        /// <summary>
        /// Adds <paramref name="massKg"/> of vanilla element <paramref name="elementIdx"/>,
        /// arriving at <paramref name="sourceTempK"/>, to this tank's own isolated storage.
        ///
        /// Blends the tank's single shared temperature via
        /// <see cref="GasMixtureFacade.AdiabaticFillTemperature"/> -- real compression heat. This
        /// is deliberately NOT <see cref="GasMixtureFacade.CombineTemperature"/> (plain
        /// calorimetry): a compressor forcing gas into an already-pressurized tank
        /// does real compression work on it, so the incoming gas must arrive carrying its full
        /// enthalpy, not just its bare temperature. Neither ONI nor Stationeers models this
        /// (Stationeers' volume pumps touch neither temperature nor energy), so this is new,
        /// physically justified math, not a port; see <see cref="GasMixtureFacade.AdiabaticFillTemperature"/>'s own doc
        /// comment for the full derivation).
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
            temperatureK = GasMixtureFacade.AdiabaticFillTemperature(
                GasMixtureFacade.DefaultGasHeatCapacityRatio, existingMass, temperatureK, massKg, sourceTempK);
        }

        /// <summary>
        /// Removes up to <paramref name="massKg"/> of <paramref name="elementIdx"/> from this
        /// tank's own storage -- the push-out counterpart to <see cref="AddMass"/>, needed now
        /// that pressure equalization (Update()) is bidirectional, not just an inbound pull.
        /// Silently does nothing if the tank doesn't hold that species at all. Clamped to
        /// whatever the tank actually has (never goes negative); the entry is dropped entirely
        /// once its mass reaches zero, same as a species that was never added.
        /// </summary>
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

        /// <summary>
        /// Public counterpart to <see cref="RemoveMass"/> for the refrigeration-loop link
        /// components (RefrigerationLoop.cs) -- a compressor/expansion-valve link needs to pull a
        /// SPECIFIC phase's mass out of this tank's own isolated storage directly (not via the
        /// pipe-equalization Update() path, which only ever talks to ONE connected gas conduit).
        /// Still container-mass-isolation-safe: this only ever touches <see cref="savedMassKg"/>/
        /// <see cref="savedElementIdx"/>, this tank's own managed lists -- never a cell, never
        /// <c>SimMessages</c>. Returns the tank's own current shared <see cref="temperatureK"/> as
        /// the removed mass's temperature (this tank has one shared temperature across its whole
        /// blend, same as everywhere else in this class).
        /// </summary>
        public bool TryRemoveMass(int elementIdx, float requestKg, out float removedKg, out float removedTempK)
        {
            removedKg = 0f;
            removedTempK = temperatureK;
            if (requestKg <= 0f)
            {
                return false;
            }
            int i = savedElementIdx.IndexOf(elementIdx);
            if (i < 0 || savedMassKg[i] <= 0f)
            {
                return false;
            }
            removedKg = Mathf.Min(requestKg, savedMassKg[i]);
            RemoveMass(elementIdx, removedKg);
            return removedKg > 0f;
        }

        /// <summary>
        /// This tank's real vapor-space pressure in Pa -- gas-phase species only, unlike
        /// <see cref="TryGetPressurePa"/> which (for a pure single-phase tank, the only shape that
        /// existed before the refrigeration loop) folds in every species regardless of state.
        /// FIXED as part of building the refrigeration loop: a tank that holds BOTH
        /// liquid and gas of the same working fluid (an evaporator/condenser, unlike the plain
        /// single-phase reservoir mechanic) would otherwise have its liquid mass counted as if it
        /// were gas moles occupying the tank's full volume -- wildly overstating pressure, and
        /// therefore corrupting <see cref="ApplyPhaseChange"/>'s pressure-dependent threshold
        /// look-up below. Real physics: only the vapor above a liquid contributes partial
        /// pressure; the liquid's own volume is not part of this simplified model's "gas volume"
        /// at all (same simplification real vanilla ONI itself makes -- liquid mass never
        /// contributes to CellPressure either).
        /// </summary>
        public bool TryGetVaporPressurePa(out float pressurePa)
        {
            var gasIdx = new List<int>();
            var gasMass = new List<float>();
            for (int i = 0; i < savedElementIdx.Count; i++)
            {
                if (ElementLoader.elements[savedElementIdx[i]].IsGas)
                {
                    gasIdx.Add(savedElementIdx[i]);
                    gasMass.Add(savedMassKg[i]);
                }
            }
            if (gasIdx.Count == 0)
            {
                pressurePa = 0f;
                return false;
            }
            return GasMixtureFacade.TryComputePressure(gasIdx.ToArray(), gasMass.ToArray(), temperatureK, TankVolumeM3, out pressurePa);
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

        /// <summary>
        /// This tank's real composition, elementIdx -> mass in kg. A fresh array each call
        /// (same contract as GasMixtureFacade.TryGetComposition), safe for a caller to hold
        /// onto without aliasing this tank's own live storage.
        /// </summary>
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
        /// This tank's real pressure in Pa -- a thin wrapper over
        /// <see cref="GasMixtureFacade.TryComputePressure"/>, the SAME native P=nRT/V formula
        /// <c>SIM_GasPressure</c>/<c>CellPressure</c> use (<c>sim/gas_mixture.h</c>), rather than
        /// a hand-copied managed formula that could drift. The tank's mass/temperature correctly stay
        /// managed state (matches real vanilla's own design -- a real Gas Reservoir's own
        /// Storage is plain managed code too), only the physics formula moved to the one native
        /// source of truth.
        /// </summary>
        public bool TryGetPressurePa(out float pressurePa)
        {
            return GasMixtureFacade.TryComputePressure(savedElementIdx.ToArray(), savedMassKg.ToArray(), temperatureK, TankVolumeM3, out pressurePa);
        }

    }

    /// <summary>
    /// The source side of the compressor pair -- see GasMixtureTankComponent's doc comment
    /// above for the full mechanic. Augments the vanilla Gas Pump (GasPumpConfig).
    ///
    /// CONDUIT OUTPUT: dispenses into
    /// a real connected gas pipe at this building's real utility output cell, via
    /// <c>Game.Instance.gasConduitFlow.AddElement</c> -- the exact same vanilla API the real
    /// Gas Pump's own <c>ConduitDispenser</c> uses. No pipe connected means no mass moves at
    /// all (checked via <c>ConduitFlow.HasConduit</c> before touching the source cell), and
    /// source mass is only ever removed for however much the pipe actually accepted (a full or
    /// wrong-element pipe segment can partially or fully reject a delivery), so it conserves.
    /// </summary>
    public class GasMixtureCompressorIntakeComponent : MonoBehaviour
    {
        private const float SampleIntervalSeconds = 1f;

        // FIXED rate, not a percentage of the source: a percentage-based transfer is
        // asymptotic, never reaching zero, so a depleted room would read as "frozen" rather
        // than "stopped". Vanilla's Gas Pump (`ElementConsumer.consumptionRate = 0.5f` kg/s,
        // `GasPumpConfig.DoPostConfigureComplete`) and Stationeers' volume pump (a fixed
        // liters-per-tick dial, clamped only to what the source has) agree: a pump moves a
        // FIXED rate, not a fraction of what's left. This reuses vanilla's own number.
        private const float PumpRateKgPerSecond = 0.5f;

        // Matches vanilla Gas Pump's own ElementConsumer.consumptionRadius (2) -- see the
        // Update() comment at the search call site for why this exists at all.
        private const int ConsumptionRadius = 2;

        private float timer;
        private Operational operational;
        private EnergyConsumer energyConsumer;
        private Building building;

        private void Start()
        {
            // GasPumpConfig.CreateBuildingDef sets RequiresPowerInput = true and
            // DoPostConfigureComplete adds EnergyConsumer/LogicOperationalController -- a real
            // Gas Pump does nothing while unpowered, broken, or logic-disabled, and running
            // unconditionally would let a player compress gas for free with no power connected.
            // Operational is the standard vanilla component every gated building reads for
            // exactly this check; cached once since GetComponent is not free every Update.
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

            // NOT `operational.IsOperational`, which ANDs together every Operational
            // Requirement flag -- including `Pump.PumpableFlag`, which is
            // driven by `Pump.IsPumpable()`'s own scan. That scan is a real vanilla quirk: it
            // only checks cells at `+j, +i*Grid.WidthInCells` for `i,j` in
            // `[0, consumptionRadius)` -- a quarter-QUADRANT (only up/right), not a symmetric
            // radius square -- so it can report "nothing to pump" even with abundant real
            // material sitting adjacent in any other direction, exactly the case this mod's own
            // (correct, full-radius) FindGasSourceCell below already searches for itself.
            // Depending on vanilla's narrower flag for "is there material" would be redundant
            // with, and strictly worse than, what this component does on its own -- so this
            // checks only the things Operational.IsOperational SHOULD have meant here
            // (destroyed/broken, powered, logic-enabled), bypassing PumpableFlag entirely.
            // LogicOperationalController.LogicOperationalFlag defaults to enabled
            // (unNetworkedValue=1) when nothing is wired to the logic port, matching the
            // original "no logic wire means always allowed" vanilla behavior.
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

            // Search a small neighborhood, not just the pump's own single cell: as vanilla gas
            // diffuses between cells (no PromoteRoom on the source room, by design), the pump's
            // own cell can sit empty while 1 kg of O2 sits one tile away. Vanilla
            // Gas Pump doesn't have this gap: `ElementConsumer.consumptionRadius = 2`
            // (`GasPumpConfig.DoPostConfigureComplete`) is sent to the
            // native sim via `SimMessages.AddElementConsumer`, which does its own radius search
            // -- not reachable from here (that's vanilla's own native kernel, not something this
            // mod's API exposes), so this reproduces the same real radius vanilla already
            // configured for this exact building, in managed code instead.
            int sourceCell = FindGasSourceCell(pumpCell, ConsumptionRadius);
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

            // Real vanilla per-building offset (rotation-aware), same call the real Gas Pump's
            // own ConduitDispenser makes -- not a hardcoded neighbor cell.
            int outputCell = building.GetUtilityOutputCell();
            ConduitFlow gasFlow = Game.Instance.gasConduitFlow;
            if (!Grid.IsValidCell(outputCell) || !gasFlow.HasConduit(outputCell))
            {
                // No pipe connected here -- nothing to dispense into, same as the real
                // ConduitDispenser's own IsConnected gate. Source atmosphere is left untouched.
                return;
            }

            // Fixed rate, clamped to what the source actually has -- same shape as vanilla's own
            // `Mathf.Min(a, space_remaining_kg)` (ElementConsumer.Consume) and Stationeers'
            // `MoveVolume`'s own "clamped only by how much matter the source actually has."
            float transferKg = Mathf.Min(sourceMass, PumpRateKgPerSecond * SampleIntervalSeconds);

            // Read before AddElement/RemoveVanillaMass touch anything -- this is the gas's own
            // real temperature, carried into the pipe rather than an arbitrary default.
            float sourceTempK = Grid.Temperature[sourceCell];

            // Try the pipe FIRST: AddElement returns however much it actually accepted (0 if
            // the pipe segment is full or already carrying a different element), so source mass
            // is only ever removed for what really left -- still conserving, same as before.
            float accepted = gasFlow.AddElement(outputCell, element.id, transferKg, sourceTempK, 0, 0);
            if (accepted <= 0f)
            {
                return;
            }

            GasMixtureFacade.RemoveVanillaMass(sourceCell, accepted);

            // Real vanilla conduits are per-tile, not per-network (see
            // GasMixtureFacade.EqualizePipeNetwork's own doc comment) -- without this, only the
            // ONE tile at outputCell would ever see what was just dispensed, and the rest of the
            // run would stay wherever vanilla's own slow per-tile flow last left it. Forces the
            // whole connected network back to one shared, correct state immediately, same call
            // the tank's own Update() makes after its side of an exchange.
            GasMixtureFacade.EqualizePipeNetwork(outputCell);

            // No PromoteRoom call here: this component never writes
            // gas-mixture data at sourceCell -- RemoveVanillaMass only ever touches vanilla
            // PhaseEntry.mass -- so there is nothing at the source to protect via promotion.
            // Promoting it anyway would have gated OFF the player's own oxygen room's normal
            // vanilla gas pressure/conduction (see VANILLA-REPLACEMENT items 1/3/4's promotion
            // gates) for no benefit -- a real, if quiet, side effect the original copy-paste
            // from Injector.cs's pattern didn't actually need here.
        }

        /// <summary>
        /// The pump's own cell if it already holds real gas; otherwise the highest-mass real gas
        /// cell within <paramref name="radius"/> tiles (a plain square neighborhood, not a
        /// flood-fill -- vanilla's own native radius search isn't reachable from here, see the
        /// call site's doc comment). Returns -1 if nothing in range has any gas at all.
        /// </summary>
        private static int FindGasSourceCell(int centerCell, int radius)
        {
            Element centerElement = Grid.Element[centerCell];
            if (centerElement != null && centerElement.IsGas && Grid.Mass[centerCell] > 0f)
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
                    if (element == null || !element.IsGas)
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
