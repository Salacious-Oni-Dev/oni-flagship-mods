using System.Collections.Generic;

namespace Ic10
{
    // What a chip can read and write on a device: Stationeers' ILogicable, reduced to the
    // members ProgrammableChip actually calls. Logic types travel as their numeric value, so
    // this assembly needs no knowledge of which types a given game defines.
    public interface IIc10Device
    {
        int PrefabHash { get; }
        int NameHash { get; }

        bool CanLogicRead(int logicType);
        bool CanLogicWrite(int logicType);
        double GetLogicValue(int logicType);
        void SetLogicValue(int logicType, double value);

        bool CanSlotRead(int logicSlotType, int slot);
        double GetSlotValue(int logicSlotType, int slot);
    }

    // ISlotWriteable.
    public interface IIc10SlotWriteable : IIc10Device
    {
        bool CanSlotWrite(int logicSlotType, int slot);
        void SetSlotValue(int logicSlotType, int slot, double value);
    }

    // IMemoryReadable / IMemoryWritable: a device with a stack `get`, `put`, `clr` reach.
    // Out-of-range addresses throw Ic10MemoryException.
    public interface IIc10MemoryReadable
    {
        double ReadMemory(int address);
    }

    public interface IIc10MemoryWritable
    {
        void WriteMemory(int address, double value);
        void ClearMemory();
    }

    // The reagent side of `lr`: Contents and TotalContents (any Device in Stationeers).
    public interface IIc10ReagentContents
    {
        double GetReagentContents(int reagentHash);
        double TotalReagents { get; }
    }

    // IRequireReagent: `lr` Required and Recipe, and `rmap`.
    public interface IIc10ReagentRequirer
    {
        double GetReagentRequired(int reagentHash);
        double GetReagentRecipe(int reagentHash);
        int GetPrefabHashFromReagentHash(int reagentHash);
    }

    // IConnected: `d0:1` addresses the network on a device's connection 1 instead of the
    // device.
    public interface IIc10Connected
    {
        IIc10Device GetNetwork(int networkIndex);
    }

    // What the chip is plugged into: Stationeers' ICircuitHolder, again reduced to what
    // ProgrammableChip calls. Ic10Housing is the reference implementation.
    public interface IIc10Host
    {
        // deviceIndex int.MaxValue is the housing itself (`db`). networkIndex int.MinValue
        // means "the device, not one of its networks".
        IIc10Device GetDeviceFromIndex(int deviceIndex, int networkIndex);
        IIc10Device GetDeviceFromId(int deviceId, int networkIndex);

        // Every device on the housing's data network, or null when it is on none.
        IList<IIc10Device> GetBatchDevices();

        bool IsValidDeviceIndex(int index);
        void SetDeviceLabel(int index, string label);

        // 1 while the chip is in error, 0 once an instruction has run cleanly.
        void RaiseError(int state);
        void HaltAndCatchFire();
        void HasPut();
    }
}
