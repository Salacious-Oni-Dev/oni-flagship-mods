using System.Collections.Generic;
using HarmonyLib;
using Ic10;
using STRINGS;
using TUNING;
using UnityEngine;

namespace Mod5AutomationIc10
{
    // The data cable: a third
    // wire kind beside power and automation. A building is on a data network when a cable
    // lies under any cell of its footprint.
    //
    // HOW IT FITS INTO ONI. A wire kind in ONI is a UtilityNetworkManager, two object layers
    // (the wire object and its "tile" entry, which the manager's flood fill reads) and a
    // KAnimGraphTileVisualizer whose ConnectionSource names the manager. The manager is
    // generic and a mod can own one. The layers are the hard part: ObjectLayer is a fixed
    // enum of 45, and growing it is about ten patches (the device model). This file
    // does not grow it. Two of the 45 are used by nothing in the game, by name or by number
    // (TravelTube 34 and TravelTubeConnection 37; travel tubes themselves
    // sit on Building and TravelTubeTile), and the cable takes those. Every tool that walks
    // "all 45 layers" therefore already sees a cable, and the scene partitioner already has
    // a mask for it. What is left to patch is four small things, at the foot of this file.
    //
    // WHAT ONI'S MANAGER IS USED FOR. Only for what the player sees and edits: which cells
    // hold cable, which neighbours are joined, the wire art, the build, cut and deconstruct
    // tools. It rebuilds all its network objects on any edit, so it cannot carry a network's
    // channels. The networks a chip talks to are Ic10Cables', and Ic10CableSync
    // turns each of the manager's rebuilds into the cable edits that changed.
    public class Ic10DataCableConfig : IBuildingConfig
    {
        public const string Id = "Ic10DataCable";

        public override BuildingDef CreateBuildingDef()
        {
            // PLACEHOLDER ART, drawn in code: a charcoal cable with a green core, one animation for every join in
            // UtilityNetworkManager.GetVisualizerString's names, each with a "_place" ghost.
            // Stationeers has no model for it: there, data rides the power cable.
            // As BaseLogicWireConfig.CreateBuildingDef, except for the layers.
            BuildingDef def = BuildingTemplates.CreateBuildingDef(Id, 1, 1, "Ic10DataCable_kanim", 10, 3f,
                TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER_TINY, MATERIALS.REFINED_METALS, 1600f,
                BuildLocationRule.Anywhere, TUNING.BUILDINGS.DECOR.PENALTY.TIER0, NOISE_POLLUTION.NONE);
            def.ViewMode = Ic10DataOverlay.ID;
            def.ObjectLayer = Ic10DataGrid.CableLayer;
            def.TileLayer = Ic10DataGrid.CableTileLayer;
            // ReplacementLayer stays NumLayers, "none": one tier of cable never replaces itself.
            def.SceneLayer = Grid.SceneLayer.LogicWires;
            def.Floodable = false;
            def.Overheatable = false;
            def.Entombable = false;
            def.AudioCategory = "Metal";
            def.AudioSize = "small";
            def.BaseTimeUntilRepair = -1f;
            def.isKAnimTile = true;
            def.isUtility = true;
            def.DragBuild = true;
            def.AddSearchTerms(SEARCH_TERMS.AUTOMATION);
            // Not registered with the automation overlay: the cable has an overlay of its own
            // (Ic10DataOverlay).
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            GeneratedBuildings.MakeBuildingAlwaysOperational(go);
            BuildingConfigManager.Instance.IgnoreDefaultKComponent(typeof(RequiresFoundation), prefab_tag);
            go.AddOrGet<Ic10DataCable>();
            KAnimGraphTileVisualizer visualizer = go.AddOrGet<KAnimGraphTileVisualizer>();
            visualizer.connectionSource = Ic10DataGrid.Source;
            visualizer.isPhysicalBuilding = true;
        }

        public override void DoPostConfigureUnderConstruction(GameObject go)
        {
            base.DoPostConfigureUnderConstruction(go);
            go.GetComponent<Constructable>().isDiggingRequired = false;
            KAnimGraphTileVisualizer visualizer = go.AddOrGet<KAnimGraphTileVisualizer>();
            visualizer.connectionSource = Ic10DataGrid.Source;
            visualizer.isPhysicalBuilding = false;
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
        }
    }

    // ONI's network object for the cable. It carries nothing: the manager makes new ones on
    // every rebuild, and the networks with channels and devices are Ic10Cables'.
    public class Ic10DataNetwork : UtilityNetwork
    {
    }

    // One cell of cable. As LogicWire, without the signal.
    [SkipSaveFileSerialization]
    public class Ic10DataCable : KMonoBehaviour, IHaveUtilityNetworkMgr
    {
        // The cable's tint, in the world and in its overlay: none, the art carries its colour.
        internal static readonly Color32 Tint = new Color32(255, 255, 255, 255);

        // The overlay legend's swatch: the cable's green core (5DC155, Stationeers' color_green).
        internal static readonly Color32 LegendColour = new Color32(0x5D, 0xC1, 0x55, 255);

        private int cell = Grid.InvalidCell;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            cell = Grid.PosToCell(transform.GetPosition());
            Ic10DataGrid grid = Ic10DataGrid.Current;
            if (grid != null)
            {
                grid.Manager.AddToNetworks(cell, this, is_endpoint: false);
            }
            KBatchedAnimController anim = GetComponent<KBatchedAnimController>();
            if (anim != null)
            {
                anim.TintColour = Tint;
            }
        }

        protected override void OnCleanUp()
        {
            Ic10DataGrid grid = Ic10DataGrid.Current;
            if (grid != null && Grid.IsValidCell(cell))
            {
                grid.Manager.RemoveFromNetworks(cell, this, is_endpoint: false);
            }
            base.OnCleanUp();
        }

        // Asked of the prefab by the build, cut and deconstruct tools.
        public IUtilityNetworkMgr GetNetworkManager()
        {
            Ic10DataGrid grid = Ic10DataGrid.Current;
            return grid == null ? null : grid.Manager;
        }
    }

    // The cable bridge: three cells long, it joins the cable
    // under its two ends and passes over whatever lies under its middle, as the automation
    // wire bridge does (LogicWireBridgeConfig, LogicUtilityNetworkLink).
    //
    // To the networks it is a cable with an id of its own (a bridge is
    // a cable), so it is not a device, and placing or removing one is a cable edit like any
    // other.
    //
    // WHERE IT DIFFERS FROM VANILLA'S BRIDGE. Vanilla's sits on the automation gate layer
    // and claims only its two end cells there, through its automation ports. This one has
    // no ports, so it takes a layer of its own (a third unused one, see Ic10DataGrid) and
    // claims all three of its cells on it: two data bridges cannot cross each other, and
    // nothing else is ever in their way.
    public class Ic10DataBridgeConfig : IBuildingConfig
    {
        public const string Id = "Ic10DataCableBridge";

        public override BuildingDef CreateBuildingDef()
        {
            // DRAFT ART, code-drawn (draw/data_cable.py): the cable arched between two junction
            // blocks on the end cells, which are the cells it joins.
            BuildingDef def = BuildingTemplates.CreateBuildingDef(Id, 3, 1, "Ic10DataBridge_kanim", 30, 3f,
                TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER_TINY, MATERIALS.REFINED_METALS, 1600f,
                BuildLocationRule.Anywhere, TUNING.BUILDINGS.DECOR.PENALTY.TIER0, NOISE_POLLUTION.NONE);
            def.ViewMode = Ic10DataOverlay.ID;
            def.ObjectLayer = Ic10DataGrid.BridgeLayer;
            def.SceneLayer = Grid.SceneLayer.LogicGates;
            def.Floodable = false;
            def.Overheatable = false;
            def.Entombable = false;
            def.AudioCategory = "Metal";
            def.AudioSize = "small";
            def.BaseTimeUntilRepair = -1f;
            def.PermittedRotations = PermittedRotations.R360;
            def.AlwaysOperational = true;
            def.AddSearchTerms(SEARCH_TERMS.AUTOMATION);
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            BuildingConfigManager.Instance.IgnoreDefaultKComponent(typeof(RequiresFoundation), prefab_tag);
        }

        public override void DoPostConfigurePreview(BuildingDef def, GameObject go)
        {
            base.DoPostConfigurePreview(def, go);
            AddLink(go).visualizeOnly = true;
        }

        public override void DoPostConfigureUnderConstruction(GameObject go)
        {
            base.DoPostConfigureUnderConstruction(go);
            AddLink(go).visualizeOnly = true;
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            AddLink(go).visualizeOnly = false;
        }

        private static Ic10DataBridge AddLink(GameObject go)
        {
            Ic10DataBridge bridge = go.AddOrGet<Ic10DataBridge>();
            bridge.link1 = new CellOffset(-1, 0);
            bridge.link2 = new CellOffset(1, 0);
            return bridge;
        }
    }

    // The link itself. UtilityNetworkLink works out the two end cells from the rotation and
    // calls OnConnect when the bridge is built or repaired, OnDisconnect when it is broken
    // or removed.
    [SkipSaveFileSerialization]
    public class Ic10DataBridge : UtilityNetworkLink, IHaveUtilityNetworkMgr
    {
        private int id = -1;

        // The bridge's id as a cable: no cell has it.
        public int CableId
        {
            get { return id; }
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            KBatchedAnimController anim = GetComponent<KBatchedAnimController>();
            if (anim != null)
            {
                anim.TintColour = Ic10DataCable.Tint;
            }
        }

        protected override void OnConnect(int cell1, int cell2)
        {
            Ic10DataGrid grid = Ic10DataGrid.Current;
            if (grid != null)
            {
                id = Ic10DataGrid.BridgeId(Grid.PosToCell(transform.GetPosition()));
                grid.AddBridge(id, cell1, cell2);
            }
        }

        protected override void OnDisconnect(int cell1, int cell2)
        {
            Ic10DataGrid grid = Ic10DataGrid.Current;
            if (grid != null && id >= 0)
            {
                grid.RemoveBridge(id);
            }
            id = -1;
        }

        public IUtilityNetworkMgr GetNetworkManager()
        {
            Ic10DataGrid grid = Ic10DataGrid.Current;
            return grid == null ? null : grid.Manager;
        }
    }

    // The cable grid of the loaded colony: ONI's manager for the cable, and the Stationeers
    // bookkeeping fed from it. One per Game.
    // A building that is a device on the cables under only some of its cells (the IC Logic
    // Mirror: its output cell). Without it, a building is one on every cable under it.
    public interface IIc10DeviceCells
    {
        bool IsDeviceCell(int cell);
    }

    internal sealed class Ic10DataGrid : IIc10CableSource
    {
        // A ConnectionSource no vanilla wire kind uses; the two visualizer patches below
        // answer for it. The number is arbitrary and only has to differ from the six vanilla
        // values and from any other mod's.
        public const KAnimGraphTileVisualizer.ConnectionSource Source = (KAnimGraphTileVisualizer.ConnectionSource)7184;

        // Two object layers the game declares and never uses (see the top of this file).
        public const ObjectLayer CableLayer = ObjectLayer.TravelTube;
        public const ObjectLayer CableTileLayer = ObjectLayer.TravelTubeConnection;

        // And a third for the bridge (PlasticTile, 10, is named nowhere and
        // indexed nowhere in the game's assembly; so are Canvases 4, FillPlacer 6, Critter 42).
        public const ObjectLayer BridgeLayer = ObjectLayer.PlasticTile;

        private static Ic10DataGrid _current;
        private static Game _game;

        public static Ic10DataGrid Current
        {
            get
            {
                Game game = Game.Instance;
                if (game == null)
                {
                    return null;
                }
                if (!ReferenceEquals(_game, game))
                {
                    _game = game;
                    _current = new Ic10DataGrid();
                }
                return _current;
            }
        }

        public readonly UtilityNetworkManager<Ic10DataNetwork, Ic10DataCable> Manager;
        public readonly Ic10CableSync Sync;

        // Goes up whenever a network or its devices may have changed.
        public int Version { get; private set; }

        private ICollection<int> _nodes;
        private bool _rebuilt;
        private readonly Dictionary<int, IIc10Device> _deviceOf = new Dictionary<int, IIc10Device>();
        private readonly Dictionary<IIc10Device, int> _idOf = new Dictionary<IIc10Device, int>();
        private readonly List<int> _cells = new List<int>();
        private readonly List<Ic10Network> _networks = new List<Ic10Network>();

        // The bridges by their cable id, each with its two end cells (lower cell first), and
        // the bridge that ends on a cell.
        private readonly Dictionary<int, KeyValuePair<int, int>> _bridges = new Dictionary<int, KeyValuePair<int, int>>();
        private readonly Dictionary<int, int> _bridgeAt = new Dictionary<int, int>();
        private bool _bridged;

        private Ic10DataGrid()
        {
            Manager = new UtilityNetworkManager<Ic10DataNetwork, Ic10DataCable>(
                Grid.WidthInCells, Grid.HeightInCells, (int)CableTileLayer);
            Manager.AddNetworksRebuiltListener(OnRebuilt);
            Sync = new Ic10CableSync(Ic10Devices.Enums, this);
            // Components.BuildingCompletes outlives a colony; a grid that is no longer the
            // current one ignores what it hears.
            Components.BuildingCompletes.Register(OnBuildingAdded, OnBuildingRemoved);
        }

        private void OnRebuilt(IList<UtilityNetwork> networks, ICollection<int> nodes)
        {
            _nodes = nodes;
            _rebuilt = true;
        }

        // Once per frame, as Game.Update does for the pipe managers.
        public void Tick()
        {
            Manager.Update();
            if (_rebuilt || _bridged)
            {
                _rebuilt = false;
                _bridged = false;
                if (Sync.Sync() > 0)
                {
                    Version++;
                }
            }
        }

        public Ic10Network NetworkAt(int cell)
        {
            return Sync.Model.NetworkOf(cell);
        }

        // A bridge's id as a cable: past the last cell, so it is no cell's, and it sorts
        // after every cable that is one.
        public static int BridgeId(int centreCell)
        {
            return Grid.CellCount + centreCell;
        }

        // The manager is told too, so that its own networks agree with ours. It keeps one
        // link per cell, which the bridge's layer already guarantees.
        public void AddBridge(int id, int cell1, int cell2)
        {
            int low = Mathf.Min(cell1, cell2);
            int high = Mathf.Max(cell1, cell2);
            _bridges[id] = new KeyValuePair<int, int>(low, high);
            _bridgeAt[low] = id;
            _bridgeAt[high] = id;
            Manager.AddLink(low, high);
            _bridged = true;
        }

        public void RemoveBridge(int id)
        {
            KeyValuePair<int, int> ends;
            if (!_bridges.TryGetValue(id, out ends))
            {
                return;
            }
            _bridges.Remove(id);
            _bridgeAt.Remove(ends.Key);
            _bridgeAt.Remove(ends.Value);
            Manager.RemoveLink(ends.Key, ends.Value);
            _bridged = true;
        }

        // the networks under a building's cells, each once, in cell order.
        public List<Ic10Network> NetworksUnder(Building building)
        {
            List<Ic10Network> found = new List<Ic10Network>();
            if (building != null)
            {
                Sync.Model.NetworksUnder(building.PlacementCells, found);
            }
            return found;
        }

        // The same, into a list the caller owns.
        public void NetworksUnder(Building building, List<Ic10Network> into)
        {
            if (building != null)
            {
                Sync.Model.NetworksUnder(building.PlacementCells, into);
            }
        }

        // The networks an object in the world is part of: a cable's own, a building's by the
        // cells it stands on. For the overlay's hover.
        public void NetworksTouching(GameObject go, List<Ic10Network> into)
        {
            if (go == null)
            {
                return;
            }
            Ic10DataBridge bridge = go.GetComponent<Ic10DataBridge>();
            if (bridge != null || go.GetComponent<Ic10DataCable>() != null)
            {
                Ic10Network network = NetworkAt(bridge != null ? bridge.CableId : Grid.PosToCell(go.transform.GetPosition()));
                if (network != null)
                {
                    into.Add(network);
                }
                return;
            }
            NetworksUnder(go.GetComponent<BuildingComplete>(), into);
        }

        // A network's devices by display name, as Stationeers sorts them for the batch
        // instructions (Logicable.RecalculateSortedDevicesList: List.Sort over
        // DataDeviceList with DisplayName.CompareTo, so the same sort from the same starting
        // order). The name is the one the socket picker shows.
        public void SortedDevices(Ic10Network network, List<IIc10Device> into)
        {
            into.Clear();
            if (network == null)
            {
                return;
            }
            for (int i = 0; i < network.Devices.Count; i++)
            {
                into.Add(network.Devices[i]);
            }
            into.Sort((x, y) => DisplayName(x).CompareTo(DisplayName(y)));
        }

        private string DisplayName(IIc10Device device)
        {
            int id;
            string name = device != null && _idOf.TryGetValue(device, out id) ? Ic10Devices.Name(id) : null;
            return name == null ? string.Empty : UI.StripLinkFormatting(name);
        }

        // Every finished building that is a device on some network, for the overlay.
        public void BuildingsOnNetworks(List<KPrefabID> into)
        {
            foreach (KeyValuePair<IIc10Device, int> pair in _idOf)
            {
                _networks.Clear();
                Sync.Model.NetworksOf(pair.Key, _networks);
                if (_networks.Count == 0)
                {
                    continue;
                }
                KPrefabID building = Ic10Devices.Building(pair.Value);
                if (building != null)
                {
                    into.Add(building);
                }
            }
        }

        // Whether the building with this instance id is a device on some network: what
        // Ic10LegacyWire counts as "data connected".
        public bool OnAnyNetwork(int instanceId)
        {
            IIc10Device device;
            if (!_deviceOf.TryGetValue(instanceId, out device))
            {
                return false;
            }
            _onAny.Clear();
            Sync.Model.NetworksOf(device, _onAny);
            return _onAny.Count > 0;
        }

        private readonly List<Ic10Network> _onAny = new List<Ic10Network>();

        // The finished buildings on a network, in the network's device order, for the
        // socket picker.
        public List<KPrefabID> BuildingsOn(Ic10Network network, GameObject except)
        {
            List<KPrefabID> found = new List<KPrefabID>();
            if (network == null)
            {
                return found;
            }
            for (int i = 0; i < network.Devices.Count; i++)
            {
                int id;
                if (_idOf.TryGetValue(network.Devices[i], out id))
                {
                    KPrefabID building = Ic10Devices.Building(id);
                    if (building != null && building.gameObject != except)
                    {
                        found.Add(building);
                    }
                }
            }
            return found;
        }

        // A finished building joins the network of every cable under it
        // (Device.InitializeDevice). Called for every building as it spawns, and by a
        // housing for itself once its chip exists.
        public void Attach(GameObject go)
        {
            BuildingComplete building = go == null ? null : go.GetComponent<BuildingComplete>();
            IIc10Device device = DeviceOf(building);
            if (device == null)
            {
                return;
            }
            _cells.Clear();
            int[] cells = building.PlacementCells;
            IIc10DeviceCells only = go.GetComponent<IIc10DeviceCells>();
            for (int i = 0; i < cells.Length; i++)
            {
                if (Sync.Model.HasCable(cells[i]) && (only == null || only.IsDeviceCell(cells[i])))
                {
                    _cells.Add(cells[i]);
                }
            }
            if (_cells.Count > 0)
            {
                Sync.Model.AddDevice(device, _cells);
                Version++;
            }
        }

        private void OnBuildingAdded(BuildingComplete building)
        {
            if (ReferenceEquals(this, _current) && ReferenceEquals(_game, Game.Instance))
            {
                Attach(building.gameObject);
            }
        }

        // Device.OnDeregistered.
        private void OnBuildingRemoved(BuildingComplete building)
        {
            if (!ReferenceEquals(this, _current) || building == null)
            {
                return;
            }
            KPrefabID id = building.GetComponent<KPrefabID>();
            IIc10Device device;
            if (id != null && _deviceOf.TryGetValue(id.InstanceID, out device))
            {
                _deviceOf.Remove(id.InstanceID);
                _idOf.Remove(device);
                Sync.Model.RemoveDevice(device);
                Version++;
            }
        }

        private IIc10Device DeviceOf(BuildingComplete building)
        {
            if (building == null || !Ic10Devices.IsDevice(building))
            {
                return null;
            }
            KPrefabID id = building.GetComponent<KPrefabID>();
            if (id == null || id.InstanceID <= 0)
            {
                return null;
            }
            IIc10Device device = Ic10Devices.Find(id.InstanceID);
            if (device != null)
            {
                _deviceOf[id.InstanceID] = device;
                _idOf[device] = id.InstanceID;
            }
            return device;
        }

        // IIc10CableSource: every cell that holds a finished cable, lowest first, then the
        // bridges.
        public void Cables(List<int> into)
        {
            if (_nodes != null)
            {
                foreach (int cell in _nodes)
                {
                    if (Grid.IsValidCell(cell) && Grid.Objects[cell, (int)CableTileLayer] != null)
                    {
                        into.Add(cell);
                    }
                }
            }
            foreach (KeyValuePair<int, KeyValuePair<int, int>> bridge in _bridges)
            {
                into.Add(bridge.Key);
            }
            into.Sort();
        }

        // The joined neighbours in the order UtilityNetworkManager's flood fill visits them:
        // left, right, below, above. A join needs the bit on both ends, as the flood does
        // (QueueCellForVisit). Then the bridge that ends here, as the flood follows a link
        // last. A bridge's own links are its two ends, lower cell first.
        public void Links(int cell, List<int> into)
        {
            KeyValuePair<int, int> ends;
            if (_bridges.TryGetValue(cell, out ends))
            {
                into.Add(ends.Key);
                into.Add(ends.Value);
                return;
            }
            UtilityConnections here = Manager.GetConnections(cell, is_physical_building: true);
            Vector2I xy = Grid.CellToXY(cell);
            if (xy.x > 0)
            {
                Link(here, UtilityConnections.Left, Grid.CellLeft(cell), UtilityConnections.Right, into);
            }
            if (xy.x < Grid.WidthInCells - 1)
            {
                Link(here, UtilityConnections.Right, Grid.CellRight(cell), UtilityConnections.Left, into);
            }
            if (xy.y > 0)
            {
                Link(here, UtilityConnections.Down, Grid.CellBelow(cell), UtilityConnections.Up, into);
            }
            if (xy.y < Grid.HeightInCells - 1)
            {
                Link(here, UtilityConnections.Up, Grid.CellAbove(cell), UtilityConnections.Down, into);
            }
            int bridge;
            if (_bridgeAt.TryGetValue(cell, out bridge))
            {
                into.Add(bridge);
            }
        }

        private void Link(UtilityConnections here, UtilityConnections side, int other, UtilityConnections back, List<int> into)
        {
            if ((here & side) != 0 && (Manager.GetConnections(other, is_physical_building: true) & back) != 0)
            {
                into.Add(other);
            }
        }

        // The buildings standing on this cell, in object layer order, each once (several
        // buildings can cover one cable cell only in different layers).
        public void Devices(int cell, List<IIc10Device> into)
        {
            if (_bridges.ContainsKey(cell))
            {
                // Nothing stands on a bridge.
                return;
            }
            for (int layer = 0; layer < (int)ObjectLayer.NumLayers; layer++)
            {
                GameObject go = Grid.Objects[cell, layer];
                if (go == null)
                {
                    continue;
                }
                IIc10Device device = DeviceOf(go.GetComponent<BuildingComplete>());
                IIc10DeviceCells only = device == null ? null : go.GetComponent<IIc10DeviceCells>();
                if (only != null && !only.IsDeviceCell(cell))
                {
                    continue;
                }
                if (device != null && !into.Contains(device))
                {
                    into.Add(device);
                }
            }
        }
    }

    // Display strings and the build-menu entry.
    internal static class Ic10DataCableStrings
    {
        internal static void Install()
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + Ic10DataCableConfig.Id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", "Data Cable");
            Strings.Add(prefix + "DESC", "Carries data between an IC Housing and the buildings it runs.");
            Strings.Add(prefix + "EFFECT",
                "Every building with data cable under it is on that cable's network. An IC Housing "
                + "on the network can read and control all of them, and its six sockets are chosen "
                + "from them. Runs behind buildings, like wire. Carries no power and no automation "
                + "signal.");
            ModUtil.AddBuildingToPlanScreen(new HashedString("Automation"), Ic10DataCableConfig.Id,
                TUNING.BUILDINGS.PlanSubcategoryName.wires.ToString());

            prefix = "STRINGS.BUILDINGS.PREFABS." + Ic10DataBridgeConfig.Id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", "Data Cable Bridge");
            Strings.Add(prefix + "DESC", "Lets one run of data cable cross another without joining it.");
            Strings.Add(prefix + "EFFECT",
                "Joins the data cable under its two ends into one network and passes over whatever "
                + "is under its middle. It is part of the cable, not a device on it.");
            ModUtil.AddBuildingToPlanScreen(new HashedString("Automation"), Ic10DataBridgeConfig.Id,
                TUNING.BUILDINGS.PlanSubcategoryName.wires.ToString());
        }
    }

    // ---- the four patches a new wire kind on existing layers needs ------------------------

    // KAnimGraphTileVisualizer names its manager through a switch over the six vanilla kinds.
    [HarmonyPatch(typeof(KAnimGraphTileVisualizer), "ConnectionManager", MethodType.Getter)]
    internal static class KAnimGraphTileVisualizer_ConnectionManager_Ic10
    {
        private static bool Prefix(KAnimGraphTileVisualizer __instance, ref IUtilityNetworkMgr __result)
        {
            if (__instance.connectionSource != Ic10DataGrid.Source)
            {
                return true;
            }
            Ic10DataGrid grid = Ic10DataGrid.Current;
            __result = grid == null ? null : grid.Manager;
            return false;
        }
    }

    // And its neighbour lookup maps each kind to a tile layer, throwing on any other.
    [HarmonyPatch(typeof(KAnimGraphTileVisualizer), "GetNeighbour")]
    internal static class KAnimGraphTileVisualizer_GetNeighbour_Ic10
    {
        private static bool Prefix(KAnimGraphTileVisualizer __instance, Direction d, ref KAnimGraphTileVisualizer __result)
        {
            if (__instance.connectionSource != Ic10DataGrid.Source)
            {
                return true;
            }
            __result = null;
            int cell = Grid.PosToCell(__instance.transform.GetPosition());
            Vector2I xy = Grid.CellToXY(cell);
            int other = -1;
            switch (d)
            {
                case Direction.Up:
                    other = xy.y < Grid.HeightInCells - 1 ? Grid.CellAbove(cell) : -1;
                    break;
                case Direction.Down:
                    other = xy.y > 0 ? Grid.CellBelow(cell) : -1;
                    break;
                case Direction.Left:
                    other = xy.x > 0 ? Grid.CellLeft(cell) : -1;
                    break;
                case Direction.Right:
                    other = xy.x < Grid.WidthInCells - 1 ? Grid.CellRight(cell) : -1;
                    break;
            }
            if (other != -1)
            {
                GameObject go = Grid.Objects[other, (int)Ic10DataGrid.CableTileLayer];
                if (go != null)
                {
                    __result = go.GetComponent<KAnimGraphTileVisualizer>();
                }
            }
            return false;
        }
    }

    // The deconstruct, cancel and cut tools filter by object layer; a layer they do not know
    // is "Default", which their filter menu has no row for. The cable goes with automation.
    [HarmonyPatch(typeof(FilteredDragTool), "GetFilterLayerFromObjectLayer")]
    internal static class FilteredDragTool_GetFilterLayerFromObjectLayer_Ic10
    {
        private static void Postfix(ObjectLayer gamer_layer, ref string __result)
        {
            if (gamer_layer == Ic10DataGrid.CableLayer || gamer_layer == Ic10DataGrid.BridgeLayer)
            {
                __result = "Logic";
            }
        }
    }

    // The pipe managers are updated by Game.Update; so is this one. Before it, not after:
    // Game.Update runs the sim callbacks, and a chip that ticks there must not see the
    // networks of the frame before (a program could run once against a network whose cable
    // had just been removed).
    [HarmonyPatch(typeof(Game), "Update")]
    internal static class Game_Update_Ic10DataGrid
    {
        private static void Prefix()
        {
            Ic10DataGrid grid = Ic10DataGrid.Current;
            if (grid != null)
            {
                grid.Tick();
                Ic10LegacyWire.Refresh(grid);
            }
            Ic10PortDrive.DeliverAll();
        }
    }
}
