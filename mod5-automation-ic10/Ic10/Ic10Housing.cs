using System;
using System.Collections.Generic;

namespace Ic10
{
    // What a chip plugs into: the logic half of Stationeers' CircuitHousing, with nothing of
    // the building around it. Six device sockets (d0-d5), one data network for the batch
    // instructions, a Setting dial, and the chip's stack exposed as device memory.
    //
    // An ONI building owns one of these, fills Devices and DataNetwork from whatever its
    // wiring model turns out to be, and calls Execute once per logic tick.
    public class Ic10Housing : IIc10Host, IIc10Device, IIc10MemoryReadable, IIc10MemoryWritable, IIc10Connected
    {
        // Lines a chip may run per tick: CircuitHousing.RUN_COUNT.
        public const int RunCount = 128;
        public const int SocketCount = 6;

        // A label is cut to this many characters (CircuitHousing.SetDeviceLabel).
        public const int LabelLength = 10;

        public readonly IIc10Device[] Devices = new IIc10Device[SocketCount];

        // Every device on the housing's data network, or null when it is wired to none.
        // While it is set, a socket whose device is not on it reads as empty. That test runs
        // on every device access, which is why this is a list with a lookup beside it.
        public Ic10DeviceList DataNetwork;

        // The same devices in the order the batch instructions walk them, when the owner has
        // one: CircuitHousing.GetBatchOutput hands back InputNetwork1DevicesSorted, the
        // network's devices sorted by display name (Logicable.RecalculateSortedDevicesList).
        // The order shows only in how a Sum or Average over many devices rounds. Null: the
        // batch instructions walk DataNetwork as it is.
        public IList<IIc10Device> BatchDevices;

        // The networks on the housing's own connections, in connection order, a null entry
        // for a connection with no cable. `db:0` is the first. Null: the housing has none.
        public IList<Ic10Network> Networks;

        // Reference id to device, for ld, sd, getd, putd, clrd and devices named by a
        // register. Null finds nothing.
        public Func<int, IIc10Device> FindById;

        public bool On = true;
        public bool Powered = true;
        public double Setting;

        public int PrefabHash { get; set; }
        public int NameHash { get; set; }

        // 1 while the chip is failing, 0 otherwise. What the error light shows.
        public int ErrorState { get; private set; }
        public bool PutSeen { get; private set; }
        public int FireCount { get; private set; }

        private readonly string[] _labels = { "", "", "", "", "", "" };
        private readonly int _settingType;
        private readonly int _lineNumberType;
        private readonly int _lineNumberSlotType;

        public Ic10Chip Chip { get; private set; }

        public Ic10Housing(Ic10EnumSet enums)
        {
            _settingType = enums.LogicType.ValueOf("Setting");
            _lineNumberType = enums.LogicType.ValueOf("LineNumber");
            _lineNumberSlotType = enums.LogicSlotType.ValueOf("LineNumber");
        }

        // CircuitHousing.OnChildEnterInventory: a chip that goes in starts from line 0.
        public void InsertChip(Ic10Chip chip)
        {
            Chip = chip;
            chip.Host = this;
            chip.Reset();
            RaiseError(0);
        }

        public void RemoveChip()
        {
            if (Chip != null)
            {
                Chip.Reset();
                Chip.Host = null;
                Chip = null;
            }
        }

        // The source goes through the housing so the three built-in aliases get made.
        public void SetSourceCode(string sourceCode)
        {
            if (Chip != null)
            {
                RaiseError(0);
                Chip.SetSourceCode(sourceCode);
            }
        }

        // One tick. A compile error stops the chip outright; a runtime error does not: the
        // chip is still ticked and retries the failing line.
        public void Execute()
        {
            if (On && Powered && Chip != null && !Chip.CompilationError)
            {
                Chip.Execute(RunCount);
            }
        }

        public string GetDeviceLabel(int index)
        {
            return _labels[index];
        }

        public IIc10Device GetDeviceFromIndex(int deviceIndex, int networkIndex)
        {
            if (deviceIndex == Ic10Chip.BaseUnitIndex)
            {
                return networkIndex != int.MinValue ? GetNetwork(networkIndex) : this;
            }
            // An index outside the sockets is not checked here; it throws, and the chip
            // reports Unknown. `l r0 d9 On` does exactly that in Stationeers.
            IIc10Device device = Devices[deviceIndex];
            if (DataNetwork != null && !DataNetwork.Contains(device))
            {
                return null;
            }
            if (networkIndex != int.MinValue)
            {
                IIc10Connected connected = device as IIc10Connected;
                return connected == null ? null : connected.GetNetwork(networkIndex);
            }
            return device;
        }

        public IIc10Device GetDeviceFromId(int deviceId, int networkIndex)
        {
            if (deviceId == 0)
            {
                return null;
            }
            IIc10Device device = FindById == null ? null : FindById(deviceId);
            if (DataNetwork != null && !DataNetwork.Contains(device))
            {
                return null;
            }
            if (networkIndex != int.MinValue)
            {
                IIc10Connected connected = device as IIc10Connected;
                return connected == null ? null : connected.GetNetwork(networkIndex);
            }
            return device;
        }

        public IList<IIc10Device> GetBatchDevices()
        {
            if (DataNetwork == null)
            {
                return null;
            }
            return BatchDevices ?? DataNetwork;
        }

        public bool IsValidDeviceIndex(int index)
        {
            return index == Ic10Chip.BaseUnitIndex || (index >= 0 && index < Devices.Length);
        }

        public void SetDeviceLabel(int index, string label)
        {
            if (index < 0 || index >= SocketCount)
            {
                return;
            }
            _labels[index] = label.Length > LabelLength ? label.Substring(0, LabelLength) : label;
        }

        public void RaiseError(int state)
        {
            ErrorState = state;
        }

        // Stationeers sparks the housing's atmosphere and sets off a small explosion. What an
        // ONI housing does is the building's business; this only counts it.
        public virtual void HaltAndCatchFire()
        {
            FireCount++;
        }

        public void HasPut()
        {
            PutSeen = true;
        }

        // The network on one of the housing's own connections (`db:0`): Device.GetNetwork.
        public virtual IIc10Device GetNetwork(int networkIndex)
        {
            return Ic10Network.Pick(Networks, networkIndex);
        }

        public virtual bool CanLogicRead(int logicType)
        {
            return logicType == _settingType || logicType == _lineNumberType;
        }

        public virtual bool CanLogicWrite(int logicType)
        {
            return logicType == _settingType || logicType == _lineNumberType;
        }

        public virtual double GetLogicValue(int logicType)
        {
            if (logicType == _settingType)
            {
                return Setting;
            }
            if (logicType == _lineNumberType)
            {
                return Chip != null ? Chip.LineNumber : -1.0;
            }
            return 0.0;
        }

        public virtual void SetLogicValue(int logicType, double value)
        {
            if (logicType == _settingType)
            {
                Setting = value;
            }
            else if (logicType == _lineNumberType && Chip != null)
            {
                Chip.LineNumber = value;
            }
        }

        // One slot: the chip.
        public virtual bool CanSlotRead(int logicSlotType, int slot)
        {
            return slot == 0 && logicSlotType == _lineNumberSlotType;
        }

        public virtual double GetSlotValue(int logicSlotType, int slot)
        {
            if (slot != 0)
            {
                return double.NaN;
            }
            if (logicSlotType == _lineNumberSlotType && Chip != null)
            {
                return Chip.LineNumber;
            }
            return 0.0;
        }

        // The chip's stack, as device memory. With no chip in the slot Stationeers throws a
        // NullReferenceException, which `put` reports as DeviceNotFound; so does this.
        public double ReadMemory(int address)
        {
            if (Chip == null)
            {
                throw new NullReferenceException();
            }
            return Chip.ReadMemory(address);
        }

        public void WriteMemory(int address, double value)
        {
            if (Chip == null)
            {
                throw new NullReferenceException();
            }
            Chip.WriteMemory(address, value);
        }

        public void ClearMemory()
        {
            if (Chip == null)
            {
                throw new NullReferenceException();
            }
            Chip.ClearMemory();
        }
    }
}
