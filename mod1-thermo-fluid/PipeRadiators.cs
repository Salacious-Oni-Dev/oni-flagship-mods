using System.Collections.Generic;
using OniFramework;
using TUNING;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// PIPE RADIATORS -- the heat-rejection end of the loop, and the first buildings in this mod
    /// whose whole behaviour is a SimDLL term rather than a component.
    ///
    /// Four buildings, two pairs. Each pair is a vanilla Radiant Pipe with one of Stationeers'
    /// radiator prefabs' numbers attached:
    ///
    ///   CONVECTION RADIATOR (`PipeRadiator`, ConvectionFactor 1, RadiationFactor 0.75, 1.05 m^2)
    ///     -- 105 W/K into a room at one atmosphere, which is roughly five hundred times what a
    ///     copper Radiant Pipe reaches a gas cell with through Klei's conduction. It is the
    ///     building for dumping heat into air, and it falls off with the room's pressure exactly
    ///     as Stationeers' does, because the sim scales it by the cell's HeatExchangeRatio().
    ///
    ///   RADIATOR PANEL (`PipePanelRadiator`, ConvectionFactor 0.2, RadiationFactor 3, 1.15 m^2)
    ///     -- a quarter of the convection and four times the radiation. Nearly useless in air,
    ///     and in vacuum the only thing in the game that sheds heat at a useful rate.
    ///
    /// A DEVICE CLAMPED ONTO A PIPE, NOT A PIPE. Stationeers' own pipe radiators are pipe-mounted
    /// devices with no internal volume: the network's own atmosphere is what they exchange with
    /// (`PipeRadiator.ThermalAtmosphere => NetworkAtmosphere`). These are the same shape. A
    /// radiator is an ordinary 1x1 building on `ObjectLayer.Building` that is placed ON TOP of an
    /// existing pipe and leaves it standing -- the pipe keeps carrying the fluid, keeps its own
    /// connection art, and the radiator reads the contents of the cell it shares with it.
    ///
    /// THEY SIT ON AN EXISTING PIPE rather than replacing a conduit segment. Two things follow. The design is closer to the Stationeers building it is ported
    /// from -- a clamp-on device rather than a length of special pipe. And it stops being a kanim
    /// tile, which is what was blocking its art: `KAnimGraphTileVisualizer.Refresh` plays an anim
    /// named for the tile's live connections (L/R/U/D, or "None"), so a conduit-segment radiator
    /// needs SIXTEEN anims, where an ordinary building needs exactly one pose.
    ///
    /// PLACEMENT WAS MEASURED, NOT ASSUMED. `BuildingDef.IsAreaClear` tests only the def's own
    /// `ObjectLayer` and `TileLayer` (plus AttachableBuilding and Gantry for a Building-layer
    /// def) and never looks at the conduit layers, so a building and a pipe can share a cell.
    /// Confirmed live through the debug inspector's `/placement/{id}/{cell}` route: a 1x1
    /// Building-layer def places cleanly on cells already holding a
    /// gas pipe, a liquid pipe, both at once, and both plus a wire -- and vanilla already lets a
    /// Liquid Reservoir and a Liquid Pipe stand in one cell together.
    ///
    /// THE CHAIN A RADIATOR ACTUALLY RUNS, and each link is somewhere different:
    ///
    ///   pipe contents -> radiator   <see cref="PipeRadiatorExchange"/>, this file. A direct
    ///                               conduit-updater term on the shared cell, using Klei's own
    ///                               exchange math from `ContactConductivePipeBridge`.
    ///   radiator -> cell            OniFramework.Convection, the SimDLL term: 100 W/(m^2 K) x
    ///                               area x factor x the CELL's ratio. Zero in vacuum.
    ///   radiator -> space           OniFramework.Radiation, Stefan-Boltzmann against the
    ///                               environment temperature, and only where the cell is empty
    ///                               enough to be transparent. Zero in a full room.
    ///
    /// So the two outlets hand over to each other as the room empties, and a radiator in a
    /// half-vacuum gets part of each.
    ///
    /// WHY THE FIRST LINK IS OURS NOW AND NOT KLEI'S. While a radiator WAS the pipe, its contents
    /// reached its body through Klei's conduit kernel, and <c>RadiatorNetworkPolicy</c> existed to
    /// force <see cref="ConduitNetworkPolicy.Convection"/> onto the network so that link was
    /// scaled by pressure the way Stationeers scales it. A radiator that is no longer the pipe
    /// cannot use that link at all: the pipe under it is a plain pipe with a plain pipe's
    /// conductivity, and the sim's "drop the cell ratio for a building that convects for itself"
    /// rule keys on the conduit building, which is now somebody else. So the policy push is gone
    /// and the intake is an explicit term instead. That is a behaviour change the RADIATOR rig
    /// has to re-measure, not a refactor.
    ///
    /// EMISSIVITY IS FOLDED INTO THE AREA. RadiationFactor (Stationeers) is a tuning multiplier
    /// (the panel's is 3), not an emissivity, and only its PRODUCT with the surface area is
    /// physical. The framework's API says 0..1 and means it, so these register emissivity 1 and
    /// an area of factor x surface area. Mod 1 gives every other building emissivity 1 over its
    /// own footprint, so a 1 m^2 pipe in
    /// vacuum already radiates ~460 W at 300 K: the convection radiator is deliberately no better
    /// than that in vacuum (0.79 m^2), and the panel is 3.5x it.
    /// </summary>
    public static class PipeRadiators
    {
        internal const string GasRadiatorId = "GasConduitRadiator";
        internal const string LiquidRadiatorId = "LiquidConduitRadiator";
        internal const string GasPanelId = "GasConduitRadiatorPanel";
        internal const string LiquidPanelId = "LiquidConduitRadiatorPanel";

        // Stationeers' prefab numbers. The class supplies the factors, the prefab the area.
        /// <summary>
        /// `PipeRadiator.ConvectionFactor` (Stationeers) is 1. This is FOUR TIMES that, and the
        /// departure is deliberate: the radiator should be the clear winner over a Radiant Pipe.
        ///
        /// Stationeers' number is calibrated against Stationeers' own pipes, which convect at
        /// 0.48 W/K; ONI's Radiant Pipe reaches a 1 kg/cell room at about 95 W/K through Klei's
        /// conduction, measured on the RADIATOR rig. Carrying the factor over unchanged made the
        /// radiator a 1.26x building, which is not a building anybody would go and unlock.
        /// </summary>
        private const float ConvectionRadiatorFactor = 4f;       // 4x PipeRadiator.ConvectionFactor
        private const float ConvectionRadiatorRadiation = 0.75f; // PipeRadiator.RadiationFactor
        private const float GasRadiatorAreaM2 = 1.053f;          // StructurePipeRadiator
        private const float LiquidRadiatorAreaM2 = 1.018f;       // StructureLiquidPipeRadiator

        private const float PanelFactor = 0.8f;                  // 4x PipePanelRadiator's 0.2
        private const float PanelRadiation = 3f;                 // PipePanelRadiator.RadiationFactor
        private const float PanelAreaM2 = 1.154f;                // StructurePipeRadiatorFlat

        /// <summary>
        /// How far a radiator's convection reaches, in cells, and the single thing that separates
        /// one of these from a Radiant Pipe.
        ///
        /// Stationeers' radiators convect with the ROOM's atmosphere; ONI has no room object in
        /// the sim, so the sim spreads the same conductance over the open cells within this many
        /// cells of the building. Measured on the RADIATOR rig before this existed: a radiator at
        /// Stationeers' factor shed 1.03x what a Radiant Pipe did, and a hundredfold factor only
        /// 1.14x, because both had pinned the one cell they stood in and were waiting on ONI's
        /// own conduction to drain it. A Radiant Pipe stays a tile device, which is the contrast
        /// the buildings are for.
        ///
        /// 3 is 25 cells around a 1x1 pipe -- a small room -- and the sim caps it at 4.
        /// </summary>
        private const int RadiatorReachCells = 3;

        /// <summary>
        /// The radiator's own thermal conductivity multiplier, against a Radiant Pipe's 2.
        ///
        /// This is the OTHER leg, and the rig found it the hard way. Heat gets out of a pipe in
        /// series: contents -> pipe body -> room. `BuildingDef.ThermalConductivity` scales the
        /// first leg as well as Klei's own building-to-cell conduction
        /// (`ConduitTemperatureManager.Allocate` multiplies the element's conductivity by it), so
        /// however good the room side gets, a radiator on a Radiant Pipe's conductivity is
        /// throttled by how fast its own contents can reach its body. A radiator is a finned,
        /// high-conductivity pipe; this is that, and it is the vanilla knob for it rather than
        /// anything new.
        /// </summary>
        private const float RadiatorThermalConductivity = 8f;

        /// <summary>
        /// Registers the four buildings' strings, their build-menu entries and their thermal
        /// profiles. Called from <c>Mod.OnLoad</c>; the configs themselves are discovered by
        /// <c>GeneratedBuildings.LoadGeneratedBuildings</c>, as the valves' are.
        /// </summary>
        internal static void Install()
        {
            AddStrings(GasRadiatorId, "Gas Radiator",
                "A gas pipe that exchanges heat with the room around it about five hundred times "
                + "as well as a Radiant Pipe does.",
                "Heat has to end up somewhere, and a Radiant Pipe is poor at handing it to air -- "
                + "gases conduct badly, which is why radiant pipes are usually run through water "
                + "or tiles. This one convects instead: it moves about 105 W per degree into a "
                + "room at one atmosphere, and proportionally less as the room empties, because "
                + "convection needs something to convect into. In vacuum it does nothing at all "
                + "and its own radiation is what is left.");

            AddStrings(LiquidRadiatorId, "Liquid Radiator",
                "A liquid pipe that exchanges heat with the room around it about five hundred "
                + "times as well as a Radiant Pipe does.",
                "The liquid side of the same building. Liquid carries far more heat per kilogram "
                + "than gas does, so this is what a working loop rejects its heat through -- and "
                + "it rejects it into the room, at a rate set by the room's pressure.");

            AddStrings(GasPanelId, "Gas Radiator Panel",
                "A gas pipe built to radiate to open space rather than convect into a room.",
                "A quarter of the Gas Radiator's grip on the surrounding air, and four times its "
                + "radiating power. In a pressurised room it is the worse building. In a vacuum, "
                + "where convection carries nothing and radiation is the only way out, it is the "
                + "best one -- and it works better the hotter you are willing to run it, because "
                + "radiated power climbs with the fourth power of temperature.");

            AddStrings(LiquidPanelId, "Liquid Radiator Panel",
                "A liquid pipe built to radiate to open space rather than convect into a room.",
                "The liquid side of the same building. Run it hot, run it in vacuum, and it is "
                + "the end of the line for the colony's waste heat.");

            // Same categories vanilla files the radiant pipes under: HVAC for gas, Plumbing for
            // liquid, and the pipes subcategory both radiant pipes sit in.
            ModUtil.AddBuildingToPlanScreen(new HashedString("HVAC"), GasRadiatorId,
                BUILDINGS.PlanSubcategoryName.pipes.ToString());
            ModUtil.AddBuildingToPlanScreen(new HashedString("HVAC"), GasPanelId,
                BUILDINGS.PlanSubcategoryName.pipes.ToString());
            ModUtil.AddBuildingToPlanScreen(new HashedString("Plumbing"), LiquidRadiatorId,
                BUILDINGS.PlanSubcategoryName.pipes.ToString());
            ModUtil.AddBuildingToPlanScreen(new HashedString("Plumbing"), LiquidPanelId,
                BUILDINGS.PlanSubcategoryName.pipes.ToString());

            Register(GasRadiatorId, ConvectionRadiatorFactor, GasRadiatorAreaM2,
                ConvectionRadiatorRadiation);
            Register(LiquidRadiatorId, ConvectionRadiatorFactor, LiquidRadiatorAreaM2,
                ConvectionRadiatorRadiation);
            Register(GasPanelId, PanelFactor, PanelAreaM2, PanelRadiation);
            Register(LiquidPanelId, PanelFactor, PanelAreaM2, PanelRadiation);
        }

        /// <summary>
        /// One prefab's profile. The radiation factor is folded into the radiating area so the
        /// framework's emissivity stays the 0..1 it is documented as -- see the class comment.
        /// </summary>
        private static void Register(string prefabId, float convectionFactor, float areaM2,
            float radiationFactor)
        {
            BuildingThermalProfile profile = default(BuildingThermalProfile);
            profile.ConvectionFactor = convectionFactor;
            profile.ConvectionAreaM2 = areaM2;
            profile.ConvectionReachCells = RadiatorReachCells;
            profile.RadiationFactor = 1f;
            profile.RadiationAreaM2 = radiationFactor * areaM2;
            Convection.RegisterPrefab(prefabId, profile);
        }

        private static void AddStrings(string id, string name, string description, string effect)
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", name);
            Strings.Add(prefix + "DESC", description);
            Strings.Add(prefix + "EFFECT", effect);
        }

        /// <summary>
        /// INTERIM ART. Both defs still wear the Radiant Pipe's kanim, which is a connection set
        /// rather than a standalone pose, so there is no "idle" in it to fall back on and
        /// `BuildingLoader` would leave the controller with nothing to play. `DefaultAnimState`
        /// names one of the connection anims explicitly: "LRUD", the four-way junction, chosen
        /// over the straight "LR" only because it does not look identical to the pipe underneath
        /// it. `UtilityNetworkManager.GetVisualizerString` is what proves those names -- L, R, U,
        /// D in that order, "None" for an isolated tile.
        ///
        /// This is the placeholder the whole redesign exists to retire: with the radiators off
        /// the tile layer, `StructurePipeRadiator`, `StructureLiquidPipeRadiator` and
        /// `StructurePipeRadiatorFlat{,Liquid}` -- all four already in the pipeline's
        /// `extract/targets.json` -- are ordinary single-pose ports.
        /// </summary>
        private const string PlaceholderAnim = "LRUD";

        /// <summary>
        /// The parts every one of the four shares: a 1x1 building on the Building layer that
        /// stands in the same cell as a pipe without replacing it, with a Radiant Pipe's
        /// materials and 4x its thermal conductivity. Only the thermal profile registered above
        /// and <see cref="PipeRadiatorExchange"/> tell them apart from any other small machine,
        /// and the profile is not part of the def at all -- it is a message to the sim.
        ///
        /// WHAT WENT AWAY WITH THE CONDUIT SHAPE, and why none of it is missed:
        /// `ObjectLayer.GasConduit`/`TileLayer`/`ReplacementLayer` (the radiator no longer owns
        /// the conduit layers, so it cannot be laid as pipe and cannot be swapped in over one),
        /// `isKAnimTile`, `isUtility`, the `Conduit` component, `KAnimGraphTileVisualizer` and
        /// the utility port offsets. `DragBuild` went with them: `PlanScreen` routes to
        /// `UtilityBuildTool` only when `isKAnimTile && isUtility`, so a radiator is now placed
        /// one click at a time like any other building.
        /// </summary>
        internal static BuildingDef CreateGasDef(string id)
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(id, 1, 1,
                "utilities_gas_radiant_kanim", 10, 10f, BUILDINGS.CONSTRUCTION_MASS_KG.TIER0,
                MATERIALS.RAW_METALS, 1600f, BuildLocationRule.Anywhere,
                noise: NOISE_POLLUTION.NONE, decor: BUILDINGS.DECOR.PENALTY.TIER0);
            def.ThermalConductivity = RadiatorThermalConductivity;
            def.Overheatable = false;
            def.Floodable = false;
            def.Entombable = false;
            def.ViewMode = OverlayModes.GasConduits.ID;
            def.ObjectLayer = ObjectLayer.Building;
            def.SceneLayer = Grid.SceneLayer.Building;
            def.DefaultAnimState = PlaceholderAnim;
            def.AudioCategory = "Metal";
            def.AudioSize = "small";
            def.BaseTimeUntilRepair = 0f;
            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.GasVentIDs, id);
            return def;
        }

        internal static BuildingDef CreateLiquidDef(string id)
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(id, 1, 1,
                "utilities_liquid_radiant_kanim", 10, 10f, BUILDINGS.CONSTRUCTION_MASS_KG.TIER1,
                MATERIALS.REFINED_METALS, 3200f, BuildLocationRule.Anywhere,
                noise: NOISE_POLLUTION.NONE, decor: BUILDINGS.DECOR.PENALTY.TIER0);
            def.ThermalConductivity = RadiatorThermalConductivity;
            def.Overheatable = false;
            def.Floodable = false;
            def.Entombable = false;
            def.ViewMode = OverlayModes.LiquidConduits.ID;
            def.ObjectLayer = ObjectLayer.Building;
            def.SceneLayer = Grid.SceneLayer.Building;
            def.DefaultAnimState = PlaceholderAnim;
            def.AudioCategory = "Metal";
            def.AudioSize = "small";
            def.BaseTimeUntilRepair = -1f;
            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.LiquidVentIDs, id);
            return def;
        }

        /// <summary>
        /// A radiator is always on -- there is no state in which it should stop exchanging -- and
        /// it carries the tag of the network family it reads so anything filtering on Vents or
        /// Pipes still finds it.
        /// </summary>
        internal static void ConfigureRadiator(GameObject go, ConduitType type)
        {
            GeneratedBuildings.MakeBuildingAlwaysOperational(go);
            go.GetComponent<KPrefabID>().AddTag(
                type == ConduitType.Gas ? GameTags.Vents : GameTags.Pipes);
        }

        internal static void CompleteRadiator(GameObject go, ConduitType type)
        {
            go.AddOrGet<PipeRadiatorExchange>().type = type;
            go.GetComponent<Building>().Def.BuildingUnderConstruction
                .GetComponent<Constructable>().isDiggingRequired = false;
        }
    }

    /// <summary>
    /// THE RADIATOR'S INTAKE: heat out of the pipe standing in the same cell, into the radiator's
    /// own thermal mass. Everything downstream of that -- convection into the room, radiation to
    /// space -- is the SimDLL's, and both of those act on the building temperature this component
    /// feeds.
    ///
    /// WHY THIS EXISTS AT ALL. While a radiator WAS the pipe, its contents reached its body
    /// through Klei's conduit kernel and `BuildingDef.ThermalConductivity` scaled that link
    /// (`ConduitTemperatureManager.Allocate` multiplies the element's conductivity by it), which
    /// is what a finned pipe is. A radiator clamped onto an ordinary pipe gets none of that: the
    /// contents are exchanging with the PIPE's body at the PIPE's conductivity, and nothing at
    /// all reaches the radiator. Without this component a radiator would convect beautifully and
    /// have nothing to convect.
    ///
    /// THE MATH IS KLEI'S, from `ContactConductivePipeBridge`, and deliberately not reinvented.
    /// That building is the one place in the game where a structure exchanges temperature with
    /// conduit contents it does not own, and its `ExchangeStorageTemperatureWithBuilding` already
    /// solves the awkward half: how to move heat between two bodies of very different heat
    /// capacity in one tick without either overshooting the other. Its guard rails are kept --
    /// the transfer is clamped to the smaller of the two bodies' capacities and to the
    /// temperatures' own interval, so a tick can equalise but never invert.
    ///
    /// WHAT IS DIFFERENT: the bridge is moving mass from an input cell to an output cell and
    /// tempers it in passing, so it hands the new temperature to `AddElement`. A radiator moves
    /// no mass. It reads the contents of its own cell, changes the temperature and writes them
    /// back with `ConduitFlow.SetContents`.
    ///
    /// ONE CONDUIT UPDATER, NOT A SIM TICK. `ConduitFlow.AddConduitUpdater` runs inside the flow
    /// manager's own update, which is where the contents are coherent; reading them from
    /// `Sim200ms` would race the packet movement. `ContactConductivePipeBridge` registers the
    /// same way for the same reason.
    ///
    /// A RADIATOR WITH NO PIPE UNDER IT IS LEGAL. Nothing in ONI's placement rules can require a
    /// conduit in the cell, and inventing a rule that could would mean a custom
    /// `BuildLocationRule`. So the building can be built anywhere and simply does nothing when
    /// there is no pipe to read, and says so on its status line rather than failing silently.
    /// Deconstructing the pipe out from under a live radiator lands in the same state.
    /// </summary>
    public class PipeRadiatorExchange : KMonoBehaviour, ISim1000ms
    {
        /// <summary>Which conduit family this radiator reads. Set by the config.</summary>
        public ConduitType type = ConduitType.Gas;

        private static StatusItem noPipeItem;

        private int cell = -1;
        private bool hasPipe;
        private HandleVector<int>.Handle structureHandle;
        private System.Action<float> updater;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            cell = Grid.PosToCell(this);
            structureHandle = HandleVector<int>.InvalidHandle;
            updater = Exchange;
            Conduit.GetFlowManager(type).AddConduitUpdater(updater);
        }

        protected override void OnCleanUp()
        {
            if (updater != null)
            {
                Conduit.GetFlowManager(type).RemoveConduitUpdater(updater);
                updater = null;
            }
            base.OnCleanUp();
        }

        /// <summary>
        /// Keeps the "no pipe" status line honest. The exchange itself notices a missing pipe
        /// every tick; this is only the part the player can see, and it is on a slow tick because
        /// a pipe appearing or disappearing under a radiator is a build action, not a flow.
        /// </summary>
        public void Sim1000ms(float dt)
        {
            KSelectable selectable = GetComponent<KSelectable>();
            if (selectable == null)
            {
                return;
            }

            EnsureStatusItems();
            selectable.SetStatusItem(Db.Get().StatusItemCategories.Main,
                hasPipe ? null : noPipeItem);
        }

        private void Exchange(float dt)
        {
            if (!Grid.IsValidCell(cell))
            {
                return;
            }

            ConduitFlow flow = Conduit.GetFlowManager(type);
            hasPipe = flow.HasConduit(cell);
            if (!hasPipe)
            {
                return;
            }

            // LAZILY, NOT AT SPAWN. A building's structure-temperature entry is created by its
            // own spawn path, and this component's OnSpawn is not guaranteed to run after it --
            // `ContactConductivePipeBridge` sidesteps the same question by taking its handle in
            // StartSM. Re-fetching until the handle is valid costs a dictionary lookup on the
            // ticks before the entry exists and nothing afterwards, and it cannot silently
            // deliver every joule to an invalid handle the way a single early fetch could.
            if (!structureHandle.IsValid())
            {
                structureHandle = GameComps.StructureTemperatures.GetHandle(gameObject);
                if (!structureHandle.IsValid())
                {
                    return;
                }
            }

            ConduitFlow.ConduitContents contents = flow.GetContents(cell);
            if (contents.mass <= 0f)
            {
                return;
            }

            PrimaryElement primary = GetComponent<PrimaryElement>();
            if (primary == null || primary.Element == null)
            {
                return;
            }

            Element carried = ElementLoader.FindElementByHash(contents.element);
            if (carried == null || carried.specificHeatCapacity <= 0f)
            {
                return;
            }

            Building building = GetComponent<Building>();
            float contentCapacity = contents.mass * carried.specificHeatCapacity;
            float buildingCapacity =
                building.Def.MassForTemperatureModification * primary.Element.specificHeatCapacity;
            if (contentCapacity <= 0f || buildingCapacity <= 0f)
            {
                return;
            }

            float buildingTemperature = primary.Temperature;
            float contentTemperature = contents.temperature;

            // Klei's own coupling: the mean of the two conductivities against a fixed 50, with
            // the building's own def multiplier folded in. RadiatorThermalConductivity is the
            // knob that makes a radiator a finned pipe rather than a length of metal.
            float buildingConductivity =
                primary.Element.thermalConductivity * building.Def.ThermalConductivity;
            float watts = (contentTemperature - buildingTemperature)
                * (carried.thermalConductivity + buildingConductivity) * 0.5f * 50f;

            float kilojoules = ClampedKilojoules(watts, dt, buildingTemperature, buildingCapacity,
                contentTemperature, contentCapacity);
            if (kilojoules == 0f)
            {
                return;
            }

            float finalContentTemperature = FinalContentTemperature(kilojoules,
                buildingTemperature, buildingCapacity, contentTemperature, contentCapacity);
            float finalBuildingTemperature = FinalBuildingTemperature(contentTemperature,
                finalContentTemperature, contentCapacity, buildingTemperature, buildingCapacity);

            // The same sanity window the bridge uses. A temperature outside it means the
            // arithmetic has gone wrong somewhere upstream, and the right response is to move
            // nothing rather than to write a number that would then have to be chased.
            if (finalBuildingTemperature < 1f || finalBuildingTemperature > 10000f
                || finalContentTemperature < 1f || finalContentTemperature > 10000f)
            {
                return;
            }

            float deltaKilojoules = Mathf.Sign(watts)
                * Mathf.Abs(finalBuildingTemperature - buildingTemperature) * buildingCapacity;

            contents.temperature = finalContentTemperature;
            flow.SetContents(cell, contents);
            GameComps.StructureTemperatures.ProduceEnergy(structureHandle, deltaKilojoules,
                STRINGS.BUILDING.STATUSITEMS.OPERATINGENERGY.PIPECONTENTS_TRANSFER, Time.time);
        }

        /// <summary>
        /// Energy this tick, clamped so neither body can pass the other. Transcribed from
        /// <c>ContactConductivePipeBridge.GetKilloJoulesTransfered</c>.
        /// </summary>
        private static float ClampedKilojoules(float watts, float dt, float buildingTemperature,
            float buildingCapacity, float contentTemperature, float contentCapacity)
        {
            float kilojoules = watts * dt / 1000f;
            float low = Mathf.Min(contentTemperature, buildingTemperature);
            float high = Mathf.Max(contentTemperature, buildingTemperature);

            float contentAfter = Mathf.Clamp(
                contentTemperature - kilojoules / contentCapacity, low, high);
            float buildingAfter = Mathf.Clamp(
                buildingTemperature + kilojoules / buildingCapacity, low, high);

            float contentSide = Mathf.Abs(contentAfter - contentTemperature) * contentCapacity;
            float buildingSide = Mathf.Abs(buildingAfter - buildingTemperature) * buildingCapacity;
            return Mathf.Min(contentSide, buildingSide) * Mathf.Sign(watts);
        }

        /// <summary>
        /// Transcribed from <c>ContactConductivePipeBridge.GetFinalContentTemperature</c>. The
        /// crossover branch is the one that matters: if the naive result would put the two bodies
        /// on the wrong sides of each other, they are given their shared equilibrium instead.
        /// </summary>
        private static float FinalContentTemperature(float kilojoules, float buildingTemperature,
            float buildingCapacity, float contentTemperature, float contentCapacity)
        {
            float signed = 0f - kilojoules;
            float contentAfter = Mathf.Max(0f, contentTemperature + signed / contentCapacity);
            float buildingAfter = Mathf.Max(0f, buildingTemperature - signed / buildingCapacity);

            if ((contentTemperature - buildingTemperature) * (contentAfter - buildingAfter) < 0f)
            {
                return contentTemperature * contentCapacity / (contentCapacity + buildingCapacity)
                    + buildingTemperature * buildingCapacity
                        / (contentCapacity + buildingCapacity);
            }
            return contentAfter;
        }

        /// <summary>
        /// Transcribed from <c>ContactConductivePipeBridge.GetFinalBuildingTemperature</c>. The
        /// building gets exactly the energy the contents lost, which is what makes the pair
        /// conserve.
        /// </summary>
        private static float FinalBuildingTemperature(float contentTemperature,
            float finalContentTemperature, float contentCapacity, float buildingTemperature,
            float buildingCapacity)
        {
            float moved = (contentTemperature - finalContentTemperature) * contentCapacity;
            float low = Mathf.Min(contentTemperature, buildingTemperature);
            float high = Mathf.Max(contentTemperature, buildingTemperature);
            return Mathf.Clamp(buildingTemperature + moved / buildingCapacity, low, high);
        }

        /// <summary>
        /// Explicit name and tooltip rather than vanilla's <c>CreateStatusItem</c>, for the reason
        /// <see cref="PipeStressMonitor"/> spells out: that helper looks its strings up in Klei's
        /// table, which a mod's own status item has no entries in.
        /// </summary>
        private static void EnsureStatusItems()
        {
            if (noPipeItem != null)
            {
                return;
            }

            noPipeItem = new StatusItem(
                "Mod1RadiatorNoPipe",
                "No Pipe",
                "This radiator is not standing on a pipe, so there is nothing for it to take heat "
                + "from. Build it over an existing pipe.",
                "status_item_exclamation",
                StatusItem.IconType.Custom,
                NotificationType.BadMinor,
                allow_multiples: false,
                render_overlay: OverlayModes.None.ID);
        }
    }

    /// <summary>Gas pipe that convects hard into the room. See <see cref="PipeRadiators"/>.</summary>
    public class GasConduitRadiatorConfig : IBuildingConfig
    {
        public override BuildingDef CreateBuildingDef()
        {
            return PipeRadiators.CreateGasDef(PipeRadiators.GasRadiatorId);
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            PipeRadiators.ConfigureRadiator(go, ConduitType.Gas);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            PipeRadiators.CompleteRadiator(go, ConduitType.Gas);
        }

    }

    /// <summary>Liquid pipe that convects hard into the room. See <see cref="PipeRadiators"/>.</summary>
    public class LiquidConduitRadiatorConfig : IBuildingConfig
    {
        public override BuildingDef CreateBuildingDef()
        {
            return PipeRadiators.CreateLiquidDef(PipeRadiators.LiquidRadiatorId);
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            PipeRadiators.ConfigureRadiator(go, ConduitType.Liquid);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            PipeRadiators.CompleteRadiator(go, ConduitType.Liquid);
        }

    }

    /// <summary>Gas pipe built to radiate to vacuum. See <see cref="PipeRadiators"/>.</summary>
    public class GasConduitRadiatorPanelConfig : IBuildingConfig
    {
        public override BuildingDef CreateBuildingDef()
        {
            return PipeRadiators.CreateGasDef(PipeRadiators.GasPanelId);
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            PipeRadiators.ConfigureRadiator(go, ConduitType.Gas);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            PipeRadiators.CompleteRadiator(go, ConduitType.Gas);
        }

    }

    /// <summary>Liquid pipe built to radiate to vacuum. See <see cref="PipeRadiators"/>.</summary>
    public class LiquidConduitRadiatorPanelConfig : IBuildingConfig
    {
        public override BuildingDef CreateBuildingDef()
        {
            return PipeRadiators.CreateLiquidDef(PipeRadiators.LiquidPanelId);
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            PipeRadiators.ConfigureRadiator(go, ConduitType.Liquid);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            PipeRadiators.CompleteRadiator(go, ConduitType.Liquid);
        }

    }
}
