using System;
using System.Collections.Generic;

namespace Ic10
{
    // Stationeers' discrete logic devices, for the player who will not write a program:
    // readers that copy a device's value into their own Setting, processors that compute a
    // Setting from other units' Settings, and writers that copy a unit's Setting to a
    // device.
    //
    // Each is a port of the class named above it. All of them work once per power tick
    // (Device.OnPowerTick, half a second), and only while switched on, powered and
    // "operable", which for each means its chosen devices are still on its data network;
    // while it is not, its Error is 1.
    //
    // A unit keeps its choices as reference ids and plain numbers, which is what the game
    // side saves. What is on the network is asked of the environment on every tick.
    public abstract class Ic10LogicUnit : Ic10MappedDevice
    {
        // RocketMath.Approximately(double, double): a difference under 1e-7.
        public const double Tolerance = 1E-07;

        // LogicUnitBase.IsSet.
        public const double SetAbove = 0.001;

        public double Setting;
        public int Mode;
        public int Error;

        // The environment. On and Powered null: always. Find: the device with that
        // reference id on this unit's data network, or null. Devices: everything on it.
        public Func<bool> On;
        public Func<bool> Powered;
        public Action<bool> SwitchOn;
        public Func<int, IIc10Device> Find;
        public Func<IList<IIc10Device>> Devices;
        public int ReferenceId;

        protected readonly Ic10EnumTable Types;
        protected readonly int SettingType;

        protected Ic10LogicUnit(Ic10EnumSet enums, string prefabId)
        {
            Types = enums.LogicType;
            SettingType = Types.ValueOf("Setting");
            PrefabHash = Ic10Math.Hash(prefabId);
            Read(Types.ValueOf("PrefabHash"), () => PrefabHash);
            Read(Types.ValueOf("NameHash"), () => NameHash);
            Read(Types.ValueOf("ReferenceId"), () => ReferenceId);
            Read(Types.ValueOf("On"), () => IsOn ? 1.0 : 0.0);
            Write(Types.ValueOf("On"), value =>
            {
                if (SwitchOn != null)
                {
                    SwitchOn(State(value));
                }
            });
            Read(Types.ValueOf("Power"), () => IsPowered ? 1.0 : 0.0);
            Read(Types.ValueOf("Error"), () => Error);
            // LogicUnitProcessor.CanLogicRead and the reader and writer bases: Setting.
            Read(SettingType, () => Setting);
        }

        protected bool IsOn
        {
            get { return On == null || On(); }
        }

        protected bool IsPowered
        {
            get { return Powered == null || Powered(); }
        }

        // Mode with Device.SetLogicValue's clamp to the unit's ModeStrings.
        protected void MapMode(int lowest, int highest)
        {
            int mode = Types.ValueOf("Mode");
            Read(mode, () => Mode);
            Write(mode, value =>
            {
                if (!double.IsNaN(value))
                {
                    Mode = (int)(value < lowest ? lowest : value > highest ? highest : value);
                }
            });
        }

        protected static bool Approximately(double a, double b)
        {
            return Math.Abs(a - b) < Tolerance;
        }

        protected void Set(double value)
        {
            if (!Approximately(value, Setting))
            {
                Setting = value;
            }
        }

        protected IIc10Device Device(int id)
        {
            return id == 0 || Find == null ? null : Find(id);
        }

        // A unit whose Setting another unit may take: here, any device that answers Setting.
        protected bool Input(int id, out double setting)
        {
            IIc10Device device = Device(id);
            if (device == null || !device.CanLogicRead(SettingType))
            {
                setting = 0.0;
                return false;
            }
            setting = device.GetLogicValue(SettingType);
            return true;
        }

        // Device.IsLogicReadable / IsLogicWritable: the device answers at least one logic
        // type, read or written.
        protected bool AnyType(IIc10Device device, bool write)
        {
            if (device == null)
            {
                return false;
            }
            foreach (KeyValuePair<string, int> pair in Types.Pairs)
            {
                if (write ? device.CanLogicWrite(pair.Value) : device.CanLogicRead(pair.Value))
                {
                    return true;
                }
            }
            return false;
        }

        // The first device of a prefab on the network (the SampleDevice of the batch units).
        protected IIc10Device Sample(IList<IIc10Device> devices, int prefab)
        {
            if (devices == null || prefab == 0)
            {
                return null;
            }
            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i] != null && devices[i].PrefabHash == prefab)
                {
                    return devices[i];
                }
            }
            return null;
        }

        // The IsOperable getters: what each unit needs, and Error following it.
        protected bool Operable(bool ok)
        {
            Error = ok ? 0 : 1;
            return ok;
        }

        // Device.OnPowerTick. The test for "operable" is made only while the unit is on and
        // powered, as `OnOff && Powered && IsOperable` makes it: a unit switched off keeps
        // the Error it had.
        public void Tick()
        {
            BeforeTick();
            if (IsOn && IsPowered)
            {
                Work();
            }
        }

        protected virtual void BeforeTick()
        {
        }

        protected abstract void Work();
    }

    // LogicReader: Setting follows one logic type of one device.
    public class Ic10LogicReader : Ic10LogicUnit
    {
        public int Target;
        public int LogicType;

        public Ic10LogicReader(Ic10EnumSet enums, string prefabId) : base(enums, prefabId)
        {
        }

        protected bool TargetReadable(out IIc10Device device)
        {
            device = Device(Target);
            return Operable(device != null && device.CanLogicRead(LogicType));
        }

        protected override void Work()
        {
            IIc10Device device;
            if (TargetReadable(out device))
            {
                Set(device.GetLogicValue(LogicType));
            }
        }
    }

    // LogicSlotReader: Setting follows one slot logic type of one slot of one device.
    public sealed class Ic10LogicSlotReader : Ic10LogicUnit
    {
        public int Target;
        public int Slot = -1;
        public int SlotType;

        public Ic10LogicSlotReader(Ic10EnumSet enums, string prefabId) : base(enums, prefabId)
        {
        }

        protected override void Work()
        {
            IIc10Device device = Device(Target);
            if (Operable(device != null && Slot >= 0 && device.CanSlotRead(SlotType, Slot)))
            {
                Set(device.GetSlotValue(SlotType, Slot));
            }
        }
    }

    // LogicBatchReader: Setting is one logic type over every device of one prefab on the
    // network, combined by a batch method (LogicBatchMethod, which is not its Mode).
    public sealed class Ic10LogicBatchReader : Ic10LogicUnit
    {
        public int Prefab;
        public int LogicType;
        public int Method;

        public Ic10LogicBatchReader(Ic10EnumSet enums, string prefabId) : base(enums, prefabId)
        {
        }

        protected override void Work()
        {
            IList<IIc10Device> devices = Devices == null ? null : Devices();
            // LogicBatchReader.IsOperable: a device of the prefab there, and one that
            // answers some logic type (SampleDevice.IsLogicReadable).
            if (Operable(AnyType(Sample(devices, Prefab), false)))
            {
                Set(Ic10Chip.BatchReadPlain(Method, devices, Prefab, LogicType));
            }
        }
    }

    // LogicBatchSlotReader: one slot logic type of one slot over every device of one
    // prefab. Its own loop, not Device.BatchRead, and two things in it are kept as read:
    // Count never resets its counter, so it grows by the number of devices every tick
    // (only Average or Sum resets it); and Maximum starts from 0, so over values that are
    // all negative it is 0. Minimum of nothing is 0.
    public sealed class Ic10LogicBatchSlotReader : Ic10LogicUnit
    {
        public int Prefab;
        public int Slot = -1;
        public int SlotType;
        public int Method;
        private int _counter;

        public Ic10LogicBatchSlotReader(Ic10EnumSet enums, string prefabId) : base(enums, prefabId)
        {
        }

        protected override void Work()
        {
            IList<IIc10Device> devices = Devices == null ? null : Devices();
            IIc10Device sample = null;
            if (devices != null && Prefab != 0)
            {
                for (int i = 0; i < devices.Count && sample == null; i++)
                {
                    if (devices[i] != null && devices[i].PrefabHash == Prefab)
                    {
                        sample = devices[i];
                    }
                }
            }
            if (!Operable(sample != null && Slot >= 0 && sample.CanSlotRead(SlotType, Slot)))
            {
                return;
            }
            double value = 0.0;
            int n = devices.Count;
            switch (Method)
            {
                case 4:
                    while (n-- > 0)
                    {
                        if (devices[n] != null && devices[n].PrefabHash == Prefab)
                        {
                            _counter++;
                        }
                    }
                    value = _counter;
                    break;
                case 0:
                case 1:
                    _counter = 0;
                    while (n-- > 0)
                    {
                        if (devices[n] != null && devices[n].PrefabHash == Prefab)
                        {
                            value += devices[n].GetSlotValue(SlotType, Slot);
                            _counter++;
                        }
                    }
                    if (Method == 0)
                    {
                        value /= _counter;
                    }
                    break;
                case 2:
                    value = double.PositiveInfinity;
                    while (n-- > 0)
                    {
                        if (devices[n] != null && devices[n].PrefabHash == Prefab)
                        {
                            double v = devices[n].GetSlotValue(SlotType, Slot);
                            if (!(v >= value))
                            {
                                value = v;
                            }
                        }
                    }
                    if (value >= double.PositiveInfinity)
                    {
                        value = 0.0;
                    }
                    break;
                case 3:
                    while (n-- > 0)
                    {
                        if (devices[n] != null && devices[n].PrefabHash == Prefab)
                        {
                            double v = devices[n].GetSlotValue(SlotType, Slot);
                            if (!(v <= value))
                            {
                                value = v;
                            }
                        }
                    }
                    break;
                default:
                    value = Setting;
                    break;
            }
            Set(value);
        }
    }

    // LogicHashGen: holds one prefab hash, chosen by the player from a list of things or
    // written by a chip. No tick of its own.
    public sealed class Ic10LogicHashGen : Ic10LogicUnit
    {
        public Ic10LogicHashGen(Ic10EnumSet enums, string prefabId) : base(enums, prefabId)
        {
            Write(SettingType, value => Setting = value);
        }

        protected override void Work()
        {
        }
    }

    // LogicWriter: one logic type of one device follows the Setting of one unit. It writes
    // when the input, the target or the value has changed since it last wrote, and when a
    // chip writes ForceWrite above 0.
    public class Ic10LogicWriter : Ic10LogicUnit
    {
        public int InputUnit;
        public int Target;
        public int LogicType;

        // What it last wrote, from where, to where: saved, as LogicWriterSaveData saves them.
        public int LastInput;
        public int LastTarget;
        public double LastSetting;
        protected bool Dirty;

        public Ic10LogicWriter(Ic10EnumSet enums, string prefabId) : base(enums, prefabId)
        {
            // LogicWriterBase.SetLogicValue: taken only while the writer could write.
            Write(Types.ValueOf("ForceWrite"), value =>
            {
                if (IsOn && IsPowered && CanWork() && value > 0.0)
                {
                    Dirty = true;
                }
            });
        }

        // LogicWriter.IsOperable: an input, and a target that can be written at all
        // (Device.IsLogicWritable). Stationeers hands the write to the device whatever the
        // type and the device ignores what it has not got; here a type the target does not
        // have is not sent.
        protected virtual bool CanWork()
        {
            double unused;
            return Operable(Input(InputUnit, out unused) && AnyType(Device(Target), true));
        }

        protected virtual bool SameTarget()
        {
            return LastTarget == Target && LastTarget != 0;
        }

        protected virtual void Send(double value)
        {
            IIc10Device target = Device(Target);
            if (target != null && target.CanLogicWrite(LogicType))
            {
                target.SetLogicValue(LogicType, value);
            }
            LastTarget = Target;
        }

        protected override void Work()
        {
            if (!CanWork())
            {
                return;
            }
            double value;
            Input(InputUnit, out value);
            if (!Dirty && (LastInput == 0 || LastInput != InputUnit || !SameTarget() || !Approximately(LastSetting, value)))
            {
                Dirty = true;
            }
            if (Dirty)
            {
                Send(value);
                Setting = value;
                LastSetting = value;
                LastInput = InputUnit;
                Dirty = false;
            }
        }
    }

    // LogicWriterSwitch: a switch the player flips, whose Activate (0 or 1) is written to one
    // logic type of one device; Setting follows it. It is its own input (`Input1 => this`),
    // so it writes when the target or the value has changed since it last wrote, and on
    // ForceWrite above 0. QUIRK kept: it also writes once after every load, because
    // LogicWriterSwitchSaveData keeps the last output and the last setting but not the last
    // input, so OnFinishedLoad finds no `_lastInput` and the first tick is dirty.
    public sealed class Ic10LogicWriterSwitch : Ic10LogicUnit
    {
        public int Target;
        public int LogicType;
        public int Activate;

        // What it last wrote, and where: saved, as LogicWriterSwitchSaveData saves them.
        public int LastTarget;
        public double LastSetting;

        // `_lastInput`: set by the first write, not saved.
        private bool wrote;
        private bool dirty;

        public Ic10LogicWriterSwitch(Ic10EnumSet enums, string prefabId) : base(enums, prefabId)
        {
            int activate = Types.ValueOf("Activate");
            Read(activate, () => Activate);
            // Device.SetLogicValue: taken only while the switch is on, as
            // (int)Mathf.Clamp((float)value, 0, 1), which is 1 only from 1 up.
            Write(activate, value =>
            {
                if (IsOn)
                {
                    Activate = State(value) ? 1 : 0;
                }
            });
            // LogicWriterBase.SetLogicValue: taken only while the switch could write.
            Write(Types.ValueOf("ForceWrite"), value =>
            {
                if (IsOn && IsPowered && CanWork() && value > 0.0)
                {
                    dirty = true;
                }
            });
        }

        // The player's flip (InteractableType.Activate): on while off, else off.
        public void Flip()
        {
            Activate = Activate != 1 ? 1 : 0;
        }

        // The screwdriver on the target (Button1): the logic type goes back to none and the
        // last setting to 0.
        public void ChooseTarget(int target)
        {
            LogicType = 0;
            LastSetting = 0.0;
            Target = target;
        }

        // The screwdriver on the logic type (Button2): the last setting goes back to 0.
        public void ChooseLogicType(int logicType)
        {
            LastSetting = 0.0;
            LogicType = logicType;
        }

        // LogicWriterSwitch.IsOperable: a target on this network that can be written at all.
        private bool CanWork()
        {
            return Operable(AnyType(Device(Target), true));
        }

        protected override void Work()
        {
            if (!CanWork())
            {
                return;
            }
            if (!dirty && (!wrote || LastTarget == 0 || LastTarget != Target || !Approximately(LastSetting, Activate)))
            {
                dirty = true;
            }
            if (dirty)
            {
                // _WriteValue. A type the target does not have is not sent (as the writer's).
                IIc10Device target = Device(Target);
                if (target != null && target.CanLogicWrite(LogicType))
                {
                    target.SetLogicValue(LogicType, Activate);
                }
                Setting = Activate;
                LastSetting = Activate;
                LastTarget = Target;
                wrote = true;
                dirty = false;
            }
        }
    }

    // LogicMirror (a LogicInputBase): shows one device on its input network as itself on its
    // output network. It answers every logic type, read and written, with the mirrored
    // device's own answer, and nothing of its own: CanLogicRead, GetLogicValue, CanLogicWrite
    // and SetLogicValue are all the device's. With no device, it answers no logic type; while
    // not powered, a read is 0 and a write is dropped. Find (this unit's network) is the
    // input network. Slots are not mirrored: LogicMirror does not override them.
    //
    // QUIRK kept: what it forwards to is the device it was pointed at, wherever that device
    // is now (CurrentDevice is a reference). IsOperable only sets Error: a device that has
    // left the input network gives Error 1 and is still mirrored.
    public sealed class Ic10LogicMirror : Ic10LogicUnit
    {
        public int Target;

        // A device by reference id, on any network or none (Thing.Find). Null: Find.
        public Func<int, IIc10Device> FindAnywhere;

        public Ic10LogicMirror(Ic10EnumSet enums, string prefabId) : base(enums, prefabId)
        {
        }

        private IIc10Device Current
        {
            get
            {
                if (Target == 0)
                {
                    return null;
                }
                return FindAnywhere != null ? FindAnywhere(Target) : Device(Target);
            }
        }

        // Two mirrors pointed at each other would forward forever (in Stationeers too, to a
        // stack overflow). Here a mirror reached again while it is already forwarding
        // answers as a mirror with no device.
        private bool forwarding;

        public override bool CanLogicRead(int logicType)
        {
            IIc10Device device = Current;
            if (device == null || forwarding)
            {
                return false;
            }
            forwarding = true;
            try
            {
                return device.CanLogicRead(logicType);
            }
            finally
            {
                forwarding = false;
            }
        }

        public override double GetLogicValue(int logicType)
        {
            IIc10Device device = Current;
            if (device == null || !IsPowered || forwarding)
            {
                return 0.0;
            }
            forwarding = true;
            try
            {
                return device.GetLogicValue(logicType);
            }
            finally
            {
                forwarding = false;
            }
        }

        public override bool CanLogicWrite(int logicType)
        {
            IIc10Device device = Current;
            if (device == null || forwarding)
            {
                return false;
            }
            forwarding = true;
            try
            {
                return device.CanLogicWrite(logicType);
            }
            finally
            {
                forwarding = false;
            }
        }

        public override void SetLogicValue(int logicType, double value)
        {
            IIc10Device device = Current;
            if (device == null || !IsPowered || forwarding)
            {
                return;
            }
            forwarding = true;
            try
            {
                device.SetLogicValue(logicType, value);
            }
            finally
            {
                forwarding = false;
            }
        }

        // CheckOperable: the device must be on the input network.
        protected override void Work()
        {
            Operable(Device(Target) != null);
        }
    }

    // LogicBatchWriter: one logic type of every device of one prefab follows the Setting of
    // one unit.
    public sealed class Ic10LogicBatchWriter : Ic10LogicWriter
    {
        public int Prefab;

        public Ic10LogicBatchWriter(Ic10EnumSet enums, string prefabId) : base(enums, prefabId)
        {
        }

        protected override bool CanWork()
        {
            double unused;
            IList<IIc10Device> devices = Devices == null ? null : Devices();
            // LogicBatchWriter.IsOperable: SampleDevice.IsLogicWritable.
            return Operable(Input(InputUnit, out unused) && AnyType(Sample(devices, Prefab), true));
        }

        // LogicBatchWriter.OnPowerTick does not compare the output: there is no one target.
        protected override bool SameTarget()
        {
            return true;
        }

        protected override void Send(double value)
        {
            IList<IIc10Device> devices = Devices();
            int n = devices.Count;
            while (n-- > 0)
            {
                IIc10Device device = devices[n];
                if (device != null && device.PrefabHash == Prefab && device.CanLogicWrite(LogicType))
                {
                    device.SetLogicValue(LogicType, value);
                }
            }
        }
    }

    // LogicUnitProcessor with two inputs: LogicMath, LogicCompare, LogicMinMax.
    public abstract class Ic10LogicProcessor : Ic10LogicUnit
    {
        public int Input1;
        public int Input2;

        protected Ic10LogicProcessor(Ic10EnumSet enums, string prefabId) : base(enums, prefabId)
        {
        }

        protected override void Work()
        {
            double a;
            double b;
            bool first = Input(Input1, out a);
            bool second = Input(Input2, out b);
            if (Operable(first && second))
            {
                Set(Compute(a, b));
            }
        }

        protected abstract double Compute(double a, double b);
    }

    // LogicMath. MathOperators: Add, Subtract, Multiply, Divide, Mod, Atan2, Pow, Log.
    public sealed class Ic10LogicMath : Ic10LogicProcessor
    {
        public static readonly string[] Modes = { "Add", "Subtract", "Multiply", "Divide", "Mod", "Atan2", "Pow", "Log" };

        public Ic10LogicMath(Ic10EnumSet enums, string prefabId) : base(enums, prefabId)
        {
            MapMode(0, Modes.Length - 1);
        }

        protected override double Compute(double a, double b)
        {
            switch (Mode)
            {
                case 0: return a + b;
                case 1: return a - b;
                case 2: return a * b;
                case 3: return a / b;
                // C#'s remainder, which takes the sign of a: not the chip's `mod`.
                case 4: return a % b;
                // Math.Atan2(Input2, Input1), in degrees.
                case 5: return Math.Atan2(b, a) * 180.0 / Math.PI;
                case 6: return Math.Pow(a, b);
                // Math.Log(Input2, Input1): the logarithm of the second to the base of the first.
                case 7: return Math.Log(b, a);
                default: return Setting;
            }
        }
    }

    // LogicCompare. ConditionOperation: Equals, Greater, Less, NotEquals.
    public sealed class Ic10LogicCompare : Ic10LogicProcessor
    {
        public static readonly string[] Modes = { "Equals", "Greater", "Less", "NotEquals" };

        public Ic10LogicCompare(Ic10EnumSet enums, string prefabId) : base(enums, prefabId)
        {
            MapMode(0, Modes.Length - 1);
        }

        protected override double Compute(double a, double b)
        {
            switch (Mode)
            {
                case 0: return Approximately(a, b) ? 1.0 : 0.0;
                case 1: return a > b ? 1.0 : 0.0;
                case 2: return a < b ? 1.0 : 0.0;
                case 3: return Approximately(a, b) ? 0.0 : 1.0;
                default: return 0.0;
            }
        }
    }

    // LogicMinMax. Its own enum starts at 1: Greater = 1, Less = 2; Mode 2 is the lesser,
    // any other the greater. ModeStrings has two names, so a written Mode is held to 0 or 1
    // by Device.SetLogicValue and only the player's screwdriver reaches 2: kept.
    public sealed class Ic10LogicMinMax : Ic10LogicProcessor
    {
        public static readonly string[] Modes = { "Greater", "Less" };

        public Ic10LogicMinMax(Ic10EnumSet enums, string prefabId) : base(enums, prefabId)
        {
            Mode = 1;
            MapMode(0, Modes.Length - 1);
        }

        protected override double Compute(double a, double b)
        {
            bool first = Mode != 2 ? a > b : a < b;
            return first ? a : b;
        }
    }

    // LogicMathUnary. MathOperatorsUnary: Ceil, Floor, Abs, Log, Exp, Round, Rand, Sqrt, Sin,
    // Cos, Tan, Asin, Acos, Atan, Not. Angles are degrees.
    public sealed class Ic10LogicMathUnary : Ic10LogicUnit
    {
        public static readonly string[] Modes =
        {
            "Ceil", "Floor", "Abs", "Log", "Exp", "Round", "Rand", "Sqrt", "Sin", "Cos", "Tan", "Asin", "Acos", "Atan", "Not",
        };

        public int Input1;

        // System.Random.NextDouble in Stationeers; handed in so a test can fix it.
        public Func<double> Random;

        public Ic10LogicMathUnary(Ic10EnumSet enums, string prefabId) : base(enums, prefabId)
        {
            MapMode(0, Modes.Length - 1);
        }

        protected override void Work()
        {
            double a;
            if (!Operable(Input(Input1, out a)))
            {
                return;
            }
            const double ToRadians = Math.PI / 180.0;
            const double ToDegrees = 180.0 / Math.PI;
            double value = Setting;
            switch (Mode)
            {
                case 0: value = Math.Ceiling(a); break;
                case 1: value = Math.Floor(a); break;
                case 2: value = Math.Abs(a); break;
                case 3: value = Math.Log(a); break;
                case 4: value = Math.Exp(a); break;
                case 5: value = Math.Round(a); break;
                case 6: value = (Random != null ? Random() : 0.0) * a; break;
                case 7: value = Math.Sqrt(a); break;
                case 8: value = Math.Sin(a * ToRadians); break;
                case 9: value = Math.Cos(a * ToRadians); break;
                case 10: value = Math.Tan(a * ToRadians); break;
                case 11: value = Math.Asin(a) * ToDegrees; break;
                case 12: value = Math.Acos(a) * ToDegrees; break;
                case 13: value = Math.Atan(a) * ToDegrees; break;
                // !Input1.IsSet.
                case 14: value = a > SetAbove ? 0.0 : 1.0; break;
            }
            Set(value);
        }
    }

    // LogicSelect: the second input while the third is above 0, else the first.
    public sealed class Ic10LogicSelect : Ic10LogicUnit
    {
        public int Input1;
        public int Input2;
        public int Selector;

        // ModeStrings are ConditionOperation's four names, which Operation never reads: a
        // chip may read and write Mode all the same.
        public Ic10LogicSelect(Ic10EnumSet enums, string prefabId) : base(enums, prefabId)
        {
            MapMode(0, 3);
        }

        protected override void Work()
        {
            double a;
            double b;
            double pick;
            bool first = Input(Input1, out a);
            bool second = Input(Input2, out b);
            bool third = Input(Selector, out pick);
            if (Operable(first && second && third))
            {
                Set(pick > 0.0 ? b : a);
            }
        }
    }

    // LogicPidController: a reader whose Setting is the output of a PID loop on what it
    // reads, toward Setpoint. PidController.Iterate, coefficient for coefficient.
    public sealed class Ic10LogicPid : Ic10LogicReader
    {
        private const double Step = 0.5;
        private const double N = 1.0;

        public float ProportionalGain = 1f;
        public float IntegralGain = 0.1f;
        public float DerivativeGain = 0.01f;
        public double Setpoint;
        // As the class starts them, each at the other's extreme: no limit either way.
        public float OutputMaximum = float.MinValue;
        public float OutputMinimum = float.MaxValue;

        private double _y0;
        private double _y1;
        private double _y2;
        private double _e0;
        private double _e1;
        private double _e2;

        public Ic10LogicPid(Ic10EnumSet enums, string prefabId) : base(enums, prefabId)
        {
            Map("ProportionalGain", () => ProportionalGain, v => ProportionalGain = (float)v);
            Map("IntegralGain", () => IntegralGain, v => IntegralGain = (float)v);
            Map("DerivativeGain", () => DerivativeGain, v => DerivativeGain = (float)v);
            Map("Setpoint", () => Setpoint, v => Setpoint = (float)v);
            Map("Maximum", () => OutputMaximum, v => OutputMaximum = (float)v);
            Map("Minimum", () => OutputMinimum, v => OutputMinimum = (float)v);
            Write(Types.ValueOf("Reset"), value =>
            {
                if (value > 0.0)
                {
                    Reset();
                }
            });
        }

        private void Map(string type, Func<double> read, Action<double> write)
        {
            Read(Types.ValueOf(type), read);
            Write(Types.ValueOf(type), write);
        }

        public void Reset()
        {
            _y0 = _y1 = _y2 = 0.0;
            _e0 = _e1 = _e2 = 0.0;
        }

        protected override void Work()
        {
            IIc10Device device;
            if (!TargetReadable(out device))
            {
                return;
            }
            Set(Iterate(Setpoint, device.GetLogicValue(LogicType)));
        }

        // PidController.Iterate(setPoint, processValue, half a second).
        public double Iterate(double setpoint, double processValue)
        {
            double kp = ProportionalGain;
            double ki = IntegralGain;
            double kd = DerivativeGain;
            double k = 2.0 / Step;
            double k2 = k * k;
            double b0 = k2 * kp + k * ki + ki * N + k * kp * N + k2 * kd * N;
            double b1 = 2.0 * ki * N - 2.0 * k2 * kp - 2.0 * k2 * kd * N;
            double b2 = k2 * kp - k * ki + ki * N - k * kp * N + k2 * kd * N;
            double a0 = k2 + N * k;
            double a1 = -2.0 * k2;
            double a2 = k2 - k * N;
            _e2 = _e1;
            _e1 = _e0;
            _e0 = setpoint - processValue;
            _y2 = _y1;
            _y1 = _y0;
            _y0 = (0.0 - a1) / a0 * _y1 - a2 / a0 * _y2 + b0 / a0 * _e0 + b1 / a0 * _e1 + b2 / a0 * _e2;
            if (double.IsNaN(_y0) || double.IsInfinity(_y0))
            {
                Reset();
                return 0.0;
            }
            if (OutputMaximum < 3.4028234663852886E+38 && _y0 > OutputMaximum)
            {
                _y0 = OutputMaximum;
            }
            if (OutputMinimum > -3.4028234663852886E+38 && _y0 < OutputMinimum)
            {
                _y0 = OutputMinimum;
            }
            return _y0;
        }
    }
}
