using System;
using System.Collections.Generic;

namespace Ic10
{
    // A data network addressed as a device: what `d0:0` or `db:0` reaches. This is the logic
    // half of Stationeers' CableNetwork, which is an ILogicable and an IMemoryReadable.
    //
    // It answers eight numbers every chip on the network can read and write (Channel0 to
    // Channel7), the count of data devices on it (StackSize, read only), and through `get`
    // the reference id of the device at a position in its list. Nothing else: no PrefabHash,
    // no name, no slots.
    //
    // The channels start as NaN and belong to the network object. Stationeers does not save
    // them and does not carry them to the network that replaces this one when cables change
    // (CableNetwork._channels is touched only by its own get and set).
    public sealed class Ic10Network : IIc10Device, IIc10MemoryReadable
    {
        public const int ChannelCount = 8;

        // CableNetwork.DataDeviceList. A housing on this network uses the same list as its
        // Ic10Housing.DataNetwork.
        public readonly Ic10DeviceList Devices;

        private readonly double[] _channels =
        {
            double.NaN, double.NaN, double.NaN, double.NaN,
            double.NaN, double.NaN, double.NaN, double.NaN,
        };

        private readonly int _channel0Type;
        private readonly int _stackSizeType;
        private readonly int _referenceIdType;

        public Ic10Network(Ic10EnumSet enums) : this(enums, new Ic10DeviceList())
        {
        }

        public Ic10Network(Ic10EnumSet enums, Ic10DeviceList devices)
        {
            Devices = devices;
            // CableNetwork._baseIndex: the eight channels are consecutive from Channel0.
            _channel0Type = enums.LogicType.ValueOf("Channel0");
            _stackSizeType = enums.LogicType.ValueOf("StackSize");
            _referenceIdType = enums.LogicType.ValueOf("ReferenceId");
        }

        // CableNetwork.GetPrefabHash throws NotImplementedException. The chip never asks: a
        // network is not in any device list the batch instructions walk.
        public int PrefabHash
        {
            get { return 0; }
        }

        public int NameHash
        {
            get { return 0; }
        }

        private bool IsChannel(int logicType)
        {
            return logicType >= _channel0Type && logicType < _channel0Type + ChannelCount;
        }

        public bool CanLogicRead(int logicType)
        {
            return IsChannel(logicType) || logicType == _stackSizeType;
        }

        public bool CanLogicWrite(int logicType)
        {
            return IsChannel(logicType);
        }

        public double GetLogicValue(int logicType)
        {
            if (IsChannel(logicType))
            {
                return _channels[logicType - _channel0Type];
            }
            if (logicType == _stackSizeType)
            {
                return Devices.Count;
            }
            return double.NaN;
        }

        // The chip checks CanLogicWrite first, so only a channel arrives here. For anything
        // else CableNetwork.SetLogicValue indexes its array out of range; this does nothing.
        public void SetLogicValue(int logicType, double value)
        {
            if (IsChannel(logicType))
            {
                _channels[logicType - _channel0Type] = value;
            }
        }

        public bool CanSlotRead(int logicSlotType, int slot)
        {
            return false;
        }

        public double GetSlotValue(int logicSlotType, int slot)
        {
            return double.NaN;
        }

        // CableNetwork.ReadMemory: `_dataDeviceList[address]?.ReferenceId ?? -1`. An address
        // outside the list throws from the list itself, which the chip reports as Unknown;
        // it is not a stack error, so this is not an Ic10MemoryException.
        public double ReadMemory(int address)
        {
            if (address < 0 || address >= Devices.Count)
            {
                throw new ArgumentOutOfRangeException("address");
            }
            IIc10Device device = Devices[address];
            if (device == null)
            {
                return -1.0;
            }
            return device.CanLogicRead(_referenceIdType) ? device.GetLogicValue(_referenceIdType) : 0.0;
        }

        // Device.GetNetwork over a device's connections, in order: which network `:n` means.
        // int.MinValue is "no network asked for"; int.MaxValue is the first connection that
        // has one; any other index is that connection, or nothing when there is no such
        // connection or it has no cable.
        public static IIc10Device Pick(IList<Ic10Network> connections, int networkIndex)
        {
            if (connections == null || networkIndex == int.MinValue)
            {
                return null;
            }
            if (networkIndex == int.MaxValue)
            {
                for (int i = 0; i < connections.Count; i++)
                {
                    if (connections[i] != null)
                    {
                        return connections[i];
                    }
                }
                return null;
            }
            if (networkIndex < 0 || networkIndex >= connections.Count)
            {
                return null;
            }
            return connections[networkIndex];
        }
    }
}
