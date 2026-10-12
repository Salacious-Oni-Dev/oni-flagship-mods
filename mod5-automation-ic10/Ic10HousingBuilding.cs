using System.Runtime.Serialization;
using Ic10;
using KSerialization;
using STRINGS;
using TUNING;
using UnityEngine;

namespace Mod5AutomationIc10
{
    // The IC Housing: the building a program lives on (there is no chip item). It owns one
    // Ic10Housing and one Ic10Chip, runs the chip once per half second of game time, and saves
    // the program, the chip's state and the six socket targets.
    public class Ic10HousingConfig : IBuildingConfig
    {
        public const string Id = "Ic10Housing";

        // Stationeers' Device.UsedPower defaults to 10 W. Whether the CircuitHousing prefab
        // overrides it is not known; UNVERIFIED.
        public const float Watts = 10f;

        public override BuildingDef CreateBuildingDef()
        {
            // PLACEHOLDER ART: "off" with the status lamp red, "on" with it lit green and "error"
            // with it lit orange, as Stationeers' housing lamp does.
            BuildingDef def = BuildingTemplates.CreateBuildingDef(Id, 1, 1,
                "StructureCircuitHousing_kanim", 30, 30f, TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER0,
                MATERIALS.REFINED_METALS, 1600f, BuildLocationRule.Anywhere,
                TUNING.BUILDINGS.DECOR.PENALTY.TIER0, NOISE_POLLUTION.NONE);

            def.Overheatable = false;
            def.Floodable = false;
            def.Entombable = true;
            // Placing one shows the data cables: it has no automation port.
            def.ViewMode = Ic10DataOverlay.ID;
            def.AudioCategory = "Metal";
            def.SceneLayer = Grid.SceneLayer.Building;

            // CircuitHousing.Execute runs the chip only while the housing is switched on and
            // powered; so does this one. The watts it draws become heat in the building.
            def.RequiresPowerInput = true;
            // BuildingLoader gives every powered building an automation input port and a
            // LogicOperationalController unless told not to. The housing has no automation
            // ports, so not this one.
            def.AddLogicPowerPort = false;
            def.EnergyConsumptionWhenActive = Watts;
            def.PowerInputOffset = new CellOffset(0, 0);
            def.SelfHeatKilowattsWhenActive = Watts / 1000f;
            def.ExhaustKilowattsWhenActive = 0f;

            def.AddSearchTerms(SEARCH_TERMS.AUTOMATION);
            return def;
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<Ic10HousingBuilding>();
            go.AddOrGet<UserNameable>();
        }
    }

    [SerializationConfig(MemberSerialization.OptIn)]
    public class Ic10HousingBuilding : KMonoBehaviour, ISim200ms
    {
        // GameManager.DefaultTickSpeedMs: Stationeers runs each housing once per 500 ms.
        public const int TickMs = 500;

        // Resolved in OnSpawn with GetComponent, as the other flagship components do, rather
        // than with [MyCmpGet]: those are filled by reflection and raise CS0649.
        private Operational operational;
        private KSelectable selectable;
        private KBatchedAnimController animController;
        private BuildingEnabledButton enabledButton;
        private EnergyConsumer energyConsumer;

        // The six socket targets as KPrefabID.InstanceIDs, 0 for an empty socket.
        [Serialize]
        private int[] sockets = new int[Ic10Housing.SocketCount];

        [Serialize]
        private double setting;

        // Game time since the last chip tick, in milliseconds.
        [Serialize]
        private int tickDebtMs;

        // Ic10ChipState, field by field: KSerialization writes strings, numbers and arrays
        // of them without being taught a type from another assembly.
        [Serialize]
        private string source = string.Empty;

        [Serialize]
        private bool hasChipState;

        [Serialize]
        private double[] registers;

        [Serialize]
        private int nextAddr;

        [Serialize]
        private string[] aliasKeys;

        [Serialize]
        private int[] aliasTargets;

        [Serialize]
        private int[] aliasIndices;

        [Serialize]
        private string[] jumpTagKeys;

        [Serialize]
        private int[] jumpTagValues;

        [Serialize]
        private string[] defineKeys;

        [Serialize]
        private double[] defineValues;

        [Serialize]
        private double[] stack;

        [Serialize]
        private double sleepRemaining = double.NaN;

        private OniHousing housing;
        private Ic10Chip chip;
        // The data network under the housing, or null with no cable there.
        private Ic10Network wiredTo;
        private readonly System.Collections.Generic.List<Ic10Network> wiredList = new System.Collections.Generic.List<Ic10Network>(1);
        private readonly System.Collections.Generic.List<IIc10Device> batchList = new System.Collections.Generic.List<IIc10Device>();
        private int batchVersion = -1;
        private string hashedName;
        private string shownAnim;
        private bool shownError;
        private System.Guid errorGuid;

        private static StatusItem errorStatus;

        public Ic10Housing Housing
        {
            get { return housing; }
        }

        public Ic10Chip Chip
        {
            get { return chip; }
        }

        public string Source
        {
            get { return chip != null ? chip.SourceCode : source; }
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            operational = GetComponent<Operational>();
            selectable = GetComponent<KSelectable>();
            animController = GetComponent<KBatchedAnimController>();
            enabledButton = GetComponent<BuildingEnabledButton>();
            energyConsumer = GetComponent<EnergyConsumer>();
            if (sockets == null || sockets.Length != Ic10Housing.SocketCount)
            {
                sockets = new int[Ic10Housing.SocketCount];
            }

            KPrefabID id = GetComponent<KPrefabID>();
            housing = new OniHousing(Ic10Devices.Enums, this);
            housing.PrefabHash = Ic10Math.Hash(id.PrefabTag.Name);
            housing.ReferenceId = id.InstanceID;
            housing.Setting = setting;
            housing.FindById = FindById;

            chip = new Ic10Chip(Ic10Devices.Enums);
            chip.Clock = () => GameClock.Instance != null ? GameClock.Instance.GetTime() : 0f;
            housing.InsertChip(chip);
            if (hasChipState)
            {
                chip.Load(SavedState());
            }
            else
            {
                housing.SetSourceCode(source);
            }

            Ic10Links.Register(this);
            Subscribe((int)GameHashes.RefreshUserMenu, OnRefreshUserMenu);
            // Every building joins the cable under it as it spawns, but this one has no
            // device to offer until here.
            Ic10DataGrid grid = Ic10DataGrid.Current;
            if (grid != null)
            {
                grid.Attach(gameObject);
            }
            RefreshInputs();
            Show(force: true);
        }

        protected override void OnCleanUp()
        {
            Ic10Editor.CloseFor(this);
            Ic10Links.Unregister(this);
            base.OnCleanUp();
        }

        // ONI's sim callback is 200 ms of game time, so chip ticks land 600 and 400 ms apart
        // in turn and average 500. Game speed and pause scale it as they
        // scale everything else.
        public void Sim200ms(float dt)
        {
            if (housing == null)
            {
                return;
            }
            RefreshInputs();
            tickDebtMs += Mathf.RoundToInt(dt * 1000f);
            if (tickDebtMs >= TickMs)
            {
                tickDebtMs -= TickMs;
                if (tickDebtMs >= TickMs)
                {
                    tickDebtMs = 0;
                }
                housing.Execute();
            }
            Show(force: false);
        }

        // What the housing needs from the building before a tick: its switch, its power, its
        // name, and the device behind each socket. A socket whose building is gone or not
        // finished is empty for this tick and comes back if the building does.
        private void RefreshInputs()
        {
            housing.On = enabledButton == null || enabledButton.IsEnabled;
            housing.Powered = energyConsumer == null || energyConsumer.IsPowered;

            string name = gameObject.GetProperName();
            if (!string.Equals(name, hashedName, System.StringComparison.Ordinal))
            {
                hashedName = name;
                housing.NameHash = Ic10Math.Hash(name ?? string.Empty);
            }

            for (int i = 0; i < sockets.Length; i++)
            {
                housing.Devices[i] = sockets[i] > 0 ? Ic10Devices.Find(sockets[i]) : null;
            }

            // The network of the cable under the housing. While there is one, the batch
            // instructions reach every device on it, an id finds any of them, and a socket
            // whose building is not on it reads as empty (Ic10Housing.DataNetwork).
            // The grid is brought up to date first: a cable finished or removed earlier in
            // this same frame is in effect for this tick, as it is in Stationeers, where a
            // cable edit changes the networks at once.
            Ic10DataGrid grid = Ic10DataGrid.Current;
            if (grid != null)
            {
                grid.Tick();
            }
            Ic10Network network = grid == null ? null : grid.NetworkAt(Grid.PosToCell(transform.GetPosition()));
            bool rewired = !ReferenceEquals(network, wiredTo);
            if (rewired)
            {
                wiredTo = network;
                wiredList.Clear();
                if (network == null)
                {
                    housing.DataNetwork = null;
                    housing.Networks = null;
                    housing.BatchDevices = null;
                }
                else
                {
                    wiredList.Add(network);
                    housing.DataNetwork = network.Devices;
                    housing.Networks = wiredList;
                }
            }

            // The batch instructions walk the network by name. Stationeers sorts
            // again only when the network changes (LogicUnitBase.OnNetworkChange), so a
            // rename alone does not reorder; here it is whenever any network changed.
            if (network != null && (rewired || batchVersion != grid.Version))
            {
                batchVersion = grid.Version;
                grid.SortedDevices(network, batchList);
                housing.BatchDevices = batchList;
            }

            // The housing draws its watts whenever it is switched on, with or without a program
            // that compiles: Stationeers' Device.GetUsedPower asks only OnOff, and CircuitHousing
            // does not override it.
            bool running = housing.On && operational.IsOperational;
            operational.SetActive(running);
        }

        // ld, sd and the other by-id instructions: CircuitHousing.GetLogicableFromId. An id
        // finds any building anywhere (Referencable.Find), and Ic10Housing then refuses one
        // that is not on the housing's network. With no cable under the housing there is no
        // network and nothing is refused, in Stationeers as here.
        private IIc10Device FindById(int id)
        {
            return id == housing.ReferenceId ? housing : Ic10Devices.Find(id);
        }

        public void SetEnabled(bool value)
        {
            if (enabledButton != null)
            {
                enabledButton.IsEnabled = value;
            }
            housing.On = value;
        }

        public void SetSource(string text)
        {
            source = text ?? string.Empty;
            housing.SetSourceCode(source);
            Show(force: false);
        }

        // The data network under this housing, or null.
        public Ic10Network Network
        {
            get { return wiredTo; }
        }

        // What the socket picker offers: the buildings on the housing's network by name, as
        // Stationeers' screwdriver steps through InputNetwork1DevicesSorted
        // (Logicable.RecalculateSortedDevicesList: by DisplayName). With no cable under the
        // housing that list is null there and empty here: nothing can be chosen.
        // A socket chosen earlier still answers; see Ic10Housing.DataNetwork.
        public System.Collections.Generic.List<KPrefabID> SocketCandidates()
        {
            Ic10DataGrid grid = Ic10DataGrid.Current;
            if (wiredTo == null || grid == null)
            {
                return new System.Collections.Generic.List<KPrefabID>();
            }
            System.Collections.Generic.List<KPrefabID> found = grid.BuildingsOn(wiredTo, gameObject);
            // The plain name: ONI's proper names carry link markup, which is not what is shown.
            found.Sort((x, y) => string.Compare(
                UI.StripLinkFormatting(x.gameObject.GetProperName()),
                UI.StripLinkFormatting(y.gameObject.GetProperName()),
                System.StringComparison.CurrentCulture));
            return found;
        }

        public int GetSocket(int index)
        {
            return sockets[index];
        }

        public void SetSocket(int index, int instanceId)
        {
            sockets[index] = instanceId;
            housing.Devices[index] = instanceId > 0 ? Ic10Devices.Find(instanceId) : null;
        }

        // "line 4: IncorrectLogicType", or null while nothing is wrong. Lines count from 0,
        // as Stationeers' editor numbers them.
        public string ErrorText()
        {
            if (chip == null)
            {
                return null;
            }
            if (chip.CompilationError)
            {
                return "line " + chip.CompileErrorLine + ": " + chip.CompileErrorType + " (the program does not compile)";
            }
            if (chip.ErrorType != Ic10ErrorType.None)
            {
                return "line " + chip.ErrorLine + ": " + chip.ErrorType;
            }
            return null;
        }

        private void Show(bool force)
        {
            // The status lamp, as LogicOnOffButton.RefreshState lights it: "off" (red) while not
            // Powered, "error" (orange) while powered with Error 1 (CircuitHousing.RefreshError:
            // Error is !IsOperable, a compile or runtime error here; the chip is never missing),
            // "on" (green) otherwise. IsActive is powered and switched on, which is Stationeers'
            // Powered (Device.AssessPower clears it while switched off).
            bool error = ErrorText() != null;
            string anim = !operational.IsActive ? "off" : error ? "error" : "on";
            if (force || anim != shownAnim)
            {
                shownAnim = anim;
                if (animController != null)
                {
                    animController.Play(anim);
                }
            }

            if (force || error != shownError)
            {
                shownError = error;
                errorGuid = selectable.ToggleStatusItem(ErrorStatus(), errorGuid, error, this);
            }
        }

        private static StatusItem ErrorStatus()
        {
            if (errorStatus == null)
            {
                errorStatus = new StatusItem(
                    "Mod5Ic10ChipError",
                    "Program Error",
                    "The program on this housing has stopped on an error: {Error}",
                    "status_item_exclamation",
                    StatusItem.IconType.Custom,
                    NotificationType.BadMinor,
                    allow_multiples: false,
                    render_overlay: OverlayModes.None.ID);
                errorStatus.resolveTooltipCallback = (text, data) =>
                {
                    Ic10HousingBuilding building = data as Ic10HousingBuilding;
                    string error = building == null ? null : building.ErrorText();
                    return text.Replace("{Error}", error ?? "none");
                };
            }
            return errorStatus;
        }

        private void OnRefreshUserMenu(object data)
        {
            Game.Instance.userMenu.AddButton(gameObject, new KIconButtonMenu.ButtonInfo(
                "action_switch_toggle", "Edit Program", OpenEditor,
                tooltipText: "Write this housing's IC10 program and choose the devices in its six sockets."));
        }

        private void OpenEditor()
        {
            Ic10Editor.Open(this);
        }

        [OnSerializing]
        private void OnSerializing()
        {
            if (chip == null)
            {
                return;
            }
            setting = housing.Setting;
            Ic10ChipState state = chip.Save();
            source = state.SourceCode ?? string.Empty;
            registers = state.Registers;
            nextAddr = state.NextAddr;
            aliasKeys = state.AliasKeys;
            aliasTargets = state.AliasTargets;
            aliasIndices = state.AliasIndices;
            jumpTagKeys = state.JumpTagKeys;
            jumpTagValues = state.JumpTagValues;
            defineKeys = state.DefineKeys;
            defineValues = state.DefineValues;
            stack = state.Stack;
            sleepRemaining = state.SleepDurationRemaining;
            hasChipState = true;
        }

        private Ic10ChipState SavedState()
        {
            Ic10ChipState state = new Ic10ChipState();
            state.SourceCode = source;
            state.Registers = registers;
            state.NextAddr = nextAddr;
            state.AliasKeys = aliasKeys;
            state.AliasTargets = aliasTargets;
            state.AliasIndices = aliasIndices;
            state.JumpTagKeys = jumpTagKeys;
            state.JumpTagValues = jumpTagValues;
            state.DefineKeys = defineKeys;
            state.DefineValues = defineValues;
            state.Stack = stack;
            state.SleepDurationRemaining = sleepRemaining;
            return state;
        }
    }

    // Display strings and the build-menu entry.
    internal static class Ic10HousingStrings
    {
        internal static void Install()
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + Ic10HousingConfig.Id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", "IC Housing");
            Strings.Add(prefix + "DESC",
                "Runs an IC10 program that reads and controls the buildings in its six sockets.");
            Strings.Add(prefix + "EFFECT",
                "Holds a program written in IC10, the language of Stationeers' integrated "
                + "circuits. Each half second it runs up to 128 lines. The program reads values "
                + "from the buildings chosen for sockets d0 to d5, a sensor's reading or a "
                + "battery's charge, and writes to them: switch a building on, move a slider, "
                + "open a door. Needs power.");

            // Unlocked by research (Ic10Research.cs, Multiplexing).
            ModUtil.AddBuildingToPlanScreen(new HashedString("Automation"), Ic10HousingConfig.Id,
                TUNING.BUILDINGS.PlanSubcategoryName.logicmanager.ToString());
        }
    }
}
