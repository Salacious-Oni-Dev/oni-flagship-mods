using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using Ic10;
using KSerialization;
using UnityEngine;

namespace Mod5AutomationIc10
{
    // Stationeers' discrete logic devices as ONI buildings: the reader, slot reader, batch
    // reader, batch slot reader, writer, batch writer, writer switch, math, unary math,
    // compare, select, min/max, hash generator, logic mirror and PID controller. What each
    // computes is in
    // Ic10/Ic10LogicUnits.cs, where the offline tests reach it; this file holds a unit's
    // choices, saves them and runs it once per half second; the player makes the choices on
    // its side screen (Ic10UnitSideScreen).
    //
    // ALL ART IS PLACEHOLDER ART. A unit is set up on its side screen (Ic10UnitSideScreen).
    public enum Ic10UnitKind
    {
        Reader,
        SlotReader,
        BatchReader,
        Writer,
        BatchWriter,
        Math,
        MathUnary,
        Compare,
        Select,
        MinMax,
        Pid,
        BatchSlotReader,
        HashGen,
        WriterSwitch,
        Mirror,
    }

    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class Ic10UnitBuilding : KMonoBehaviour, ISim200ms, IIc10DeviceCells
    {
        private const float LogicTickSeconds = 0.5f;

        // Set on the prefab by the building's config.
        public Ic10UnitKind kind;

        // The unit's choices, whatever its kind uses of them. Reference ids are
        // KPrefabID.InstanceID, as a housing's sockets are.
        [Serialize] private int deviceA;
        [Serialize] private int deviceB;
        [Serialize] private int deviceC;
        [Serialize] private int logicType;
        [Serialize] private int slot = -1;
        [Serialize] private int slotType;
        [Serialize] private int method;
        [Serialize] private int prefab;
        [Serialize] private int mode = -1;
        [Serialize] private double setting;
        [Serialize] private int lastInput;
        [Serialize] private int lastTarget;
        [Serialize] private double lastSetting;
        [Serialize] private int activate;
        [Serialize] private float proportionalGain = 1f;
        [Serialize] private float integralGain = 0.1f;
        [Serialize] private float derivativeGain = 0.01f;
        [Serialize] private double setpoint;
        [Serialize] private float outputMaximum = float.MinValue;
        [Serialize] private float outputMinimum = float.MaxValue;

        private Ic10LogicUnit unit;
        private BuildingEnabledButton enabledButton;
        private KBatchedAnimController animController;
        private Ic10Network network;
        private string hashedName;
        private float sinceTick;
        private string shownAnim;

        public Ic10LogicUnit Unit
        {
            get { return unit; }
        }

        public Ic10Network Network
        {
            get { return network; }
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            enabledButton = GetComponent<BuildingEnabledButton>();
            animController = GetComponent<KBatchedAnimController>();
            KPrefabID id = GetComponent<KPrefabID>();
            unit = Make(kind, id.PrefabTag.Name);
            unit.ReferenceId = id.InstanceID;
            unit.On = () => enabledButton == null || enabledButton.IsEnabled;
            unit.SwitchOn = value =>
            {
                if (enabledButton != null)
                {
                    enabledButton.IsEnabled = value;
                }
            };
            unit.Find = FindOnNetwork;
            unit.Devices = () => network == null ? null : network.Devices;
            Load();
            Ic10Links.Register(this);
            Subscribe((int)GameHashes.RefreshUserMenu, OnRefreshUserMenu);
            // Every building joins the cable under it as it spawns, but this one has no
            // device to offer until here (as the housing).
            Ic10DataGrid grid = Ic10DataGrid.Current;
            if (grid != null)
            {
                grid.Attach(gameObject);
            }
            Show(force: true);
        }

        protected override void OnCleanUp()
        {
            Ic10Links.Unregister(this);
            base.OnCleanUp();
        }

        private static Ic10LogicUnit Make(Ic10UnitKind kind, string prefabId)
        {
            Ic10EnumSet enums = Ic10Devices.Enums;
            switch (kind)
            {
                case Ic10UnitKind.Reader: return new Ic10LogicReader(enums, prefabId);
                case Ic10UnitKind.SlotReader: return new Ic10LogicSlotReader(enums, prefabId);
                case Ic10UnitKind.BatchReader: return new Ic10LogicBatchReader(enums, prefabId);
                case Ic10UnitKind.Writer: return new Ic10LogicWriter(enums, prefabId);
                case Ic10UnitKind.BatchWriter: return new Ic10LogicBatchWriter(enums, prefabId);
                case Ic10UnitKind.Math: return new Ic10LogicMath(enums, prefabId);
                case Ic10UnitKind.MathUnary: return new Ic10LogicMathUnary(enums, prefabId) { Random = new System.Random().NextDouble };
                case Ic10UnitKind.Compare: return new Ic10LogicCompare(enums, prefabId);
                case Ic10UnitKind.Select: return new Ic10LogicSelect(enums, prefabId);
                case Ic10UnitKind.MinMax: return new Ic10LogicMinMax(enums, prefabId);
                case Ic10UnitKind.BatchSlotReader: return new Ic10LogicBatchSlotReader(enums, prefabId);
                case Ic10UnitKind.HashGen: return new Ic10LogicHashGen(enums, prefabId);
                case Ic10UnitKind.WriterSwitch: return new Ic10LogicWriterSwitch(enums, prefabId);
                case Ic10UnitKind.Mirror: return new Ic10LogicMirror(enums, prefabId) { FindAnywhere = Ic10Devices.Find };
                default: return new Ic10LogicPid(enums, prefabId);
            }
        }

        // The cell whose cable is this unit's own network: the one it stands on, and for the
        // mirror its input (left) cell. LogicMirror's InputNetwork1.
        public int HomeCell()
        {
            int cell = Grid.PosToCell(transform.GetPosition());
            if (kind != Ic10UnitKind.Mirror)
            {
                return cell;
            }
            Building building = GetComponent<Building>();
            return building != null ? Mathf.Min(building.PlacementCells) : cell;
        }

        // IIc10DeviceCells: a unit is a device on the cable under every cell of it, except
        // the mirror, which is one only on its output (right) cell; its input cable sees
        // no device there (LogicMirror's two connections: the device list of the input
        // network does not hold the mirror).
        public bool IsDeviceCell(int cell)
        {
            if (kind != Ic10UnitKind.Mirror)
            {
                return true;
            }
            Building building = GetComponent<Building>();
            return building == null || cell == Mathf.Max(building.PlacementCells);
        }

        // LogicReader.IsOperable and its like: the device must be on this unit's network
        // (InputNetwork1.DataDeviceList.Contains). A unit on no cable has no network and
        // finds nothing, as in Stationeers.
        private IIc10Device FindOnNetwork(int id)
        {
            if (network == null)
            {
                return null;
            }
            IIc10Device device = Ic10Devices.Find(id);
            return device != null && network.Devices.Contains(device) ? device : null;
        }

        public void Sim200ms(float dt)
        {
            if (unit == null)
            {
                return;
            }
            sinceTick += dt;
            if (sinceTick < LogicTickSeconds)
            {
                return;
            }
            sinceTick -= LogicTickSeconds;
            Ic10DataGrid grid = Ic10DataGrid.Current;
            if (grid != null)
            {
                grid.Tick();
            }
            network = grid == null ? null : grid.NetworkAt(HomeCell());
            string name = gameObject.GetProperName();
            if (!string.Equals(name, hashedName, StringComparison.Ordinal))
            {
                hashedName = name;
                unit.NameHash = Ic10Math.Hash(name ?? string.Empty);
            }
            unit.Tick();
            Show(force: false);
        }

        // The status lamp, as Stationeers' LogicOnOffButton.RefreshState lights it: "off" (red)
        // while not Powered, "error" (orange) while powered with Error 1, "on" (green) otherwise.
        // Our units take no power, so Powered is the on/off switch alone, which is what
        // Device.AssessPower leaves of it on a supplied network (isOn false clears Powered).
        private void Show(bool force)
        {
            if (unit == null)
            {
                return;
            }
            bool on = unit.On == null || unit.On();
            string anim = !on ? "off" : unit.Error != 0 ? "error" : "on";
            if ((force || anim != shownAnim) && animController != null)
            {
                shownAnim = anim;
                animController.Play(anim);
            }
        }

        private void Load()
        {
            unit.Setting = setting;
            if (mode >= 0)
            {
                unit.Mode = mode;
            }
            Ic10LogicPid pid = unit as Ic10LogicPid;
            Ic10LogicReader reader = unit as Ic10LogicReader;
            Ic10LogicSlotReader slotReader = unit as Ic10LogicSlotReader;
            Ic10LogicBatchReader batchReader = unit as Ic10LogicBatchReader;
            Ic10LogicWriter writer = unit as Ic10LogicWriter;
            Ic10LogicBatchWriter batchWriter = unit as Ic10LogicBatchWriter;
            Ic10LogicProcessor processor = unit as Ic10LogicProcessor;
            Ic10LogicMathUnary unary = unit as Ic10LogicMathUnary;
            Ic10LogicSelect select = unit as Ic10LogicSelect;
            if (reader != null)
            {
                reader.Target = deviceA;
                reader.LogicType = logicType;
            }
            if (pid != null)
            {
                pid.ProportionalGain = proportionalGain;
                pid.IntegralGain = integralGain;
                pid.DerivativeGain = derivativeGain;
                pid.Setpoint = setpoint;
                pid.OutputMaximum = outputMaximum;
                pid.OutputMinimum = outputMinimum;
            }
            if (slotReader != null)
            {
                slotReader.Target = deviceA;
                slotReader.Slot = slot;
                slotReader.SlotType = slotType;
            }
            if (batchReader != null)
            {
                batchReader.Prefab = prefab;
                batchReader.LogicType = logicType;
                batchReader.Method = method;
            }
            if (writer != null)
            {
                writer.InputUnit = deviceA;
                writer.Target = deviceB;
                writer.LogicType = logicType;
                writer.LastInput = lastInput;
                writer.LastTarget = lastTarget;
                writer.LastSetting = lastSetting;
            }
            if (batchWriter != null)
            {
                batchWriter.Prefab = prefab;
            }
            Ic10LogicBatchSlotReader batchSlot = unit as Ic10LogicBatchSlotReader;
            if (batchSlot != null)
            {
                batchSlot.Prefab = prefab;
                batchSlot.Slot = slot;
                batchSlot.SlotType = slotType;
                batchSlot.Method = method;
            }
            Ic10LogicMirror mirror = unit as Ic10LogicMirror;
            if (mirror != null)
            {
                mirror.Target = deviceA;
            }
            Ic10LogicWriterSwitch writerSwitch = unit as Ic10LogicWriterSwitch;
            if (writerSwitch != null)
            {
                writerSwitch.Target = deviceA;
                writerSwitch.LogicType = logicType;
                writerSwitch.Activate = activate;
                writerSwitch.LastTarget = lastTarget;
                writerSwitch.LastSetting = lastSetting;
            }
            if (processor != null)
            {
                processor.Input1 = deviceA;
                processor.Input2 = deviceB;
            }
            if (unary != null)
            {
                unary.Input1 = deviceA;
            }
            if (select != null)
            {
                select.Input1 = deviceA;
                select.Input2 = deviceB;
                select.Selector = deviceC;
            }
        }

        [OnSerializing]
        private void OnSerializing()
        {
            if (unit == null)
            {
                return;
            }
            setting = unit.Setting;
            mode = unit.Mode;
            Ic10LogicPid pid = unit as Ic10LogicPid;
            Ic10LogicReader reader = unit as Ic10LogicReader;
            Ic10LogicSlotReader slotReader = unit as Ic10LogicSlotReader;
            Ic10LogicBatchReader batchReader = unit as Ic10LogicBatchReader;
            Ic10LogicWriter writer = unit as Ic10LogicWriter;
            Ic10LogicBatchWriter batchWriter = unit as Ic10LogicBatchWriter;
            Ic10LogicProcessor processor = unit as Ic10LogicProcessor;
            Ic10LogicMathUnary unary = unit as Ic10LogicMathUnary;
            Ic10LogicSelect select = unit as Ic10LogicSelect;
            if (reader != null)
            {
                deviceA = reader.Target;
                logicType = reader.LogicType;
            }
            if (pid != null)
            {
                proportionalGain = pid.ProportionalGain;
                integralGain = pid.IntegralGain;
                derivativeGain = pid.DerivativeGain;
                setpoint = pid.Setpoint;
                outputMaximum = pid.OutputMaximum;
                outputMinimum = pid.OutputMinimum;
            }
            if (slotReader != null)
            {
                deviceA = slotReader.Target;
                slot = slotReader.Slot;
                slotType = slotReader.SlotType;
            }
            if (batchReader != null)
            {
                prefab = batchReader.Prefab;
                logicType = batchReader.LogicType;
                method = batchReader.Method;
            }
            if (writer != null)
            {
                deviceA = writer.InputUnit;
                deviceB = writer.Target;
                logicType = writer.LogicType;
                lastInput = writer.LastInput;
                lastTarget = writer.LastTarget;
                lastSetting = writer.LastSetting;
            }
            if (batchWriter != null)
            {
                prefab = batchWriter.Prefab;
            }
            Ic10LogicBatchSlotReader batchSlot = unit as Ic10LogicBatchSlotReader;
            if (batchSlot != null)
            {
                prefab = batchSlot.Prefab;
                slot = batchSlot.Slot;
                slotType = batchSlot.SlotType;
                method = batchSlot.Method;
            }
            Ic10LogicMirror mirror = unit as Ic10LogicMirror;
            if (mirror != null)
            {
                deviceA = mirror.Target;
            }
            Ic10LogicWriterSwitch writerSwitch = unit as Ic10LogicWriterSwitch;
            if (writerSwitch != null)
            {
                deviceA = writerSwitch.Target;
                logicType = writerSwitch.LogicType;
                activate = writerSwitch.Activate;
                lastTarget = writerSwitch.LastTarget;
                lastSetting = writerSwitch.LastSetting;
            }
            if (processor != null)
            {
                deviceA = processor.Input1;
                deviceB = processor.Input2;
            }
            if (unary != null)
            {
                deviceA = unary.Input1;
            }
            if (select != null)
            {
                deviceA = select.Input1;
                deviceB = select.Input2;
                deviceC = select.Selector;
            }
        }

        // The buildings on this unit's cable, by name: what the screwdriver steps through
        // in Stationeers (InputNetwork1DevicesSorted). unitsOnly: the ones whose Setting
        // another unit may take, which there are the logic units (Logicable.GetNextReadable
        // returns a LogicUnitBase) and here are Mod 5's units and bricks.
        public List<KPrefabID> Candidates(bool unitsOnly)
        {
            List<KPrefabID> found = new List<KPrefabID>();
            Ic10DataGrid grid = Ic10DataGrid.Current;
            if (grid == null)
            {
                return found;
            }
            grid.Tick();
            Ic10Network here = grid.NetworkAt(HomeCell());
            if (here == null)
            {
                return found;
            }
            foreach (KPrefabID building in grid.BuildingsOn(here, gameObject))
            {
                if (!unitsOnly || building.GetComponent<Ic10UnitBuilding>() != null || building.GetComponent<Ic10Brick>() != null)
                {
                    found.Add(building);
                }
            }
            found.Sort((x, y) => string.Compare(
                STRINGS.UI.StripLinkFormatting(x.gameObject.GetProperName()),
                STRINGS.UI.StripLinkFormatting(y.gameObject.GetProperName()),
                StringComparison.CurrentCulture));
            return found;
        }

        private void OnRefreshUserMenu(object data)
        {
            // The choices are on the side screen (Ic10UnitSideScreen).
            // The writer switch's own switch (InteractableType.Activate in Stationeers).
            Ic10LogicWriterSwitch writerSwitch = unit as Ic10LogicWriterSwitch;
            if (writerSwitch != null)
            {
                Game.Instance.userMenu.AddButton(gameObject, new KIconButtonMenu.ButtonInfo(
                    "action_power", writerSwitch.Activate == 1 ? "Switch Off" : "Switch On", () =>
                    {
                        writerSwitch.Flip();
                        Game.Instance.userMenu.Refresh(gameObject);
                    },
                    tooltipText: "Flip the switch: 1 or 0 is written to the chosen logic type of the chosen building."));
            }
        }
    }

    // The fifteen buildings. One shape for all, the bricks' (Ic10BrickConfig): one cell, no
    // power, buildable from the start in Automation, shown in the data overlay. The mirror
    // alone is two cells wide.
    public abstract class Ic10UnitConfig : Ic10BrickConfig
    {
        protected abstract Ic10UnitKind Kind { get; }

        protected override void AddBrick(GameObject go)
        {
            go.AddOrGet<Ic10UnitBuilding>().kind = Kind;
        }
    }

    public sealed class Ic10LogicReaderConfig : Ic10UnitConfig
    {
        public const string Id = "Ic10LogicReader";
        protected override string BrickId { get { return Id; } }
        protected override Ic10UnitKind Kind { get { return Ic10UnitKind.Reader; } }
        protected override string Anim { get { return "StructureLogicReader_kanim"; } }
    }

    public sealed class Ic10LogicSlotReaderConfig : Ic10UnitConfig
    {
        public const string Id = "Ic10LogicSlotReader";
        protected override string BrickId { get { return Id; } }
        protected override Ic10UnitKind Kind { get { return Ic10UnitKind.SlotReader; } }
        protected override string Anim { get { return "StructureLogicSlotReader_kanim"; } }
    }

    public sealed class Ic10LogicBatchReaderConfig : Ic10UnitConfig
    {
        public const string Id = "Ic10LogicBatchReader";
        protected override string BrickId { get { return Id; } }
        protected override Ic10UnitKind Kind { get { return Ic10UnitKind.BatchReader; } }
        protected override string Anim { get { return "StructureLogicBatchReader_kanim"; } }
    }

    public sealed class Ic10LogicWriterConfig : Ic10UnitConfig
    {
        public const string Id = "Ic10LogicWriter";
        protected override string BrickId { get { return Id; } }
        protected override Ic10UnitKind Kind { get { return Ic10UnitKind.Writer; } }
        protected override string Anim { get { return "StructureLogicWriter_kanim"; } }
    }

    public sealed class Ic10LogicBatchWriterConfig : Ic10UnitConfig
    {
        public const string Id = "Ic10LogicBatchWriter";
        protected override string BrickId { get { return Id; } }
        protected override Ic10UnitKind Kind { get { return Ic10UnitKind.BatchWriter; } }
        protected override string Anim { get { return "StructureLogicBatchWriter_kanim"; } }
    }

    public sealed class Ic10LogicMathConfig : Ic10UnitConfig
    {
        public const string Id = "Ic10LogicMath";
        protected override string BrickId { get { return Id; } }
        protected override Ic10UnitKind Kind { get { return Ic10UnitKind.Math; } }
        protected override string Anim { get { return "StructureLogicMath_kanim"; } }
    }

    public sealed class Ic10LogicMathUnaryConfig : Ic10UnitConfig
    {
        public const string Id = "Ic10LogicMathUnary";
        protected override string BrickId { get { return Id; } }
        protected override Ic10UnitKind Kind { get { return Ic10UnitKind.MathUnary; } }
        protected override string Anim { get { return "StructureLogicMathUnary_kanim"; } }
    }

    public sealed class Ic10LogicCompareConfig : Ic10UnitConfig
    {
        public const string Id = "Ic10LogicCompare";
        protected override string BrickId { get { return Id; } }
        protected override Ic10UnitKind Kind { get { return Ic10UnitKind.Compare; } }
        protected override string Anim { get { return "StructureLogicCompare_kanim"; } }
    }

    public sealed class Ic10LogicSelectConfig : Ic10UnitConfig
    {
        public const string Id = "Ic10LogicSelect";
        protected override string BrickId { get { return Id; } }
        protected override Ic10UnitKind Kind { get { return Ic10UnitKind.Select; } }
        protected override string Anim { get { return "StructureLogicSelect_kanim"; } }
    }

    public sealed class Ic10LogicMinMaxConfig : Ic10UnitConfig
    {
        public const string Id = "Ic10LogicMinMax";
        protected override string BrickId { get { return Id; } }
        protected override Ic10UnitKind Kind { get { return Ic10UnitKind.MinMax; } }
        protected override string Anim { get { return "StructureLogicMinMax_kanim"; } }
    }

    public sealed class Ic10LogicBatchSlotReaderConfig : Ic10UnitConfig
    {
        public const string Id = "Ic10LogicBatchSlotReader";
        protected override string BrickId { get { return Id; } }
        protected override Ic10UnitKind Kind { get { return Ic10UnitKind.BatchSlotReader; } }
        protected override string Anim { get { return "StructureLogicBatchSlotReader_kanim"; } }
    }

    public sealed class Ic10LogicHashGenConfig : Ic10UnitConfig
    {
        public const string Id = "Ic10LogicHashGen";
        protected override string BrickId { get { return Id; } }
        protected override Ic10UnitKind Kind { get { return Ic10UnitKind.HashGen; } }
        protected override string Anim { get { return "StructureLogicHashGen_kanim"; } }
    }

    public sealed class Ic10LogicWriterSwitchConfig : Ic10UnitConfig
    {
        public const string Id = "Ic10LogicWriterSwitch";
        protected override string BrickId { get { return Id; } }
        protected override Ic10UnitKind Kind { get { return Ic10UnitKind.WriterSwitch; } }
        protected override string Anim { get { return "StructureLogicWriterSwitch_kanim"; } }
    }

    public sealed class Ic10LogicMirrorConfig : Ic10UnitConfig
    {
        public const string Id = "Ic10LogicMirror";
        protected override string BrickId { get { return Id; } }
        protected override int Width { get { return 2; } }
        protected override Ic10UnitKind Kind { get { return Ic10UnitKind.Mirror; } }
        protected override string Anim { get { return "StructureLogicMirror_kanim"; } }
    }

    public sealed class Ic10LogicPidConfig : Ic10UnitConfig
    {
        public const string Id = "Ic10LogicPid";
        protected override string BrickId { get { return Id; } }
        protected override Ic10UnitKind Kind { get { return Ic10UnitKind.Pid; } }
        protected override string Anim { get { return "StructureLogicPidController_kanim"; } }
    }

    internal static class Ic10UnitStrings
    {
        internal static void Install()
        {
            Add(Ic10LogicReaderConfig.Id, "IC Reader", "Reads one value from one building.",
                "Its Setting follows one logic type of one building on its data cable: a sensor's reading, a battery's charge. Other units take that Setting as their input.");
            Add(Ic10LogicSlotReaderConfig.Id, "IC Slot Reader", "Reads one value from one slot of a building.",
                "Its Setting follows one slot logic type of one slot: how much a bin holds of its first item, whether a planter's plant is ripe.");
            Add(Ic10LogicBatchReaderConfig.Id, "IC Batch Reader", "Reads one value from every building of a kind.",
                "Its Setting is one logic type over every building of one kind on its data cable: their average, sum, lowest or highest.");
            Add(Ic10LogicWriterConfig.Id, "IC Writer", "Writes a unit's value to a building.",
                "Copies the Setting of one unit to one logic type of one building on its data cable, whenever that Setting changes.");
            Add(Ic10LogicBatchWriterConfig.Id, "IC Batch Writer", "Writes a unit's value to every building of a kind.",
                "Copies the Setting of one unit to one logic type of every building of one kind on its data cable.");
            Add(Ic10LogicMathConfig.Id, "IC Math", "Computes with two units' values.",
                "Its Setting is two units' Settings added, subtracted, multiplied, divided, and more.");
            Add(Ic10LogicMathUnaryConfig.Id, "IC Math (Unary)", "Computes with one unit's value.",
                "Its Setting is one unit's Setting rounded, rooted, negated, and more. Angles are in degrees.");
            Add(Ic10LogicCompareConfig.Id, "IC Compare", "Compares two units' values.",
                "Its Setting is 1 while the first unit's Setting equals, exceeds, is under or differs from the second's, else 0.");
            Add(Ic10LogicSelectConfig.Id, "IC Select", "Passes on one of two values.",
                "Its Setting is the second unit's Setting while a third unit's is above 0, else the first's.");
            Add(Ic10LogicMinMaxConfig.Id, "IC Min/Max", "The greater or the lesser of two values.",
                "Its Setting is the greater, or the lesser, of two units' Settings.");
            Add(Ic10LogicBatchSlotReaderConfig.Id, "IC Batch Slot Reader", "Reads one slot of every building of a kind.",
                "Its Setting is one slot logic type of one slot over every building of one kind on its data cable: their average, sum, lowest, highest or count.");
            Add(Ic10LogicHashGenConfig.Id, "IC Hash Generator", "Holds the hash of a building or an element.",
                "Choose a building or an element; its Setting is the hash of that id, for a batch reader or writer to take as its kind.");
            Add(Ic10LogicWriterSwitchConfig.Id, "IC Writer Switch", "Writes 1 or 0 to a building, by hand.",
                "A switch: while on, 1 is written to one logic type of one building on its data cable, while off 0, each time it is flipped.");
            Add(Ic10LogicMirrorConfig.Id, "IC Logic Mirror", "Shows a building on another data cable.",
                "Two cells. Choose a building on the cable under its left cell; on the cable under its right cell it answers as that building, read and written.");
            Add(Ic10LogicPidConfig.Id, "IC PID Controller", "Steers a value toward a setpoint.",
                "Reads one value from one building and gives, as its Setting, the output of a PID loop that drives the value toward its setpoint. Set its output maximum and minimum before use.");
        }

        private static void Add(string id, string name, string desc, string effect)
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", name);
            Strings.Add(prefix + "DESC", desc);
            Strings.Add(prefix + "EFFECT", effect);
            ModUtil.AddBuildingToPlanScreen(new HashedString("Automation"), id,
                TUNING.BUILDINGS.PlanSubcategoryName.logicmanager.ToString());
        }
    }
}
