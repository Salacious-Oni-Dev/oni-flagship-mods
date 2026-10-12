using System;
using System.Collections.Generic;

namespace Ic10
{
    // One named-value table a script can spell: LogicType, LogicSlotType, a mode enum.
    // Lookup is exact and case-sensitive, which is what Enum.IsDefined(Type, string) does in
    // Stationeers.
    public sealed class Ic10EnumTable
    {
        public readonly string Name;

        // The width Stationeers' enum has. Enum.ToObject keeps only the low bits that fit the
        // underlying type, so on a ushort enum the value 65542 reads as 6. 0 means no mask.
        public readonly int Mask;

        private readonly Dictionary<string, int> _values = new Dictionary<string, int>(StringComparer.Ordinal);

        public Ic10EnumTable(string name, IEnumerable<KeyValuePair<string, int>> values, int mask = 0)
        {
            Name = name;
            Mask = mask;
            foreach (KeyValuePair<string, int> pair in values)
            {
                _values[pair.Key] = pair.Value;
            }
        }

        public int Count
        {
            get { return _values.Count; }
        }

        // Every name and its value, for a list a player picks from.
        public IEnumerable<KeyValuePair<string, int>> Pairs
        {
            get { return _values; }
        }

        public bool TryGet(string name, out int value)
        {
            return _values.TryGetValue(name, out value);
        }

        // The numeric value of a name the caller knows is there (a housing looking up
        // "Setting"); -1 if the table has no such name.
        public int ValueOf(string name)
        {
            int value;
            return _values.TryGetValue(name, out value) ? value : -1;
        }

        internal int Apply(int value)
        {
            return Mask == 0 ? value : value & Mask;
        }
    }

    // Every table the chip resolves names against: ProgrammableChip.InternalEnums.
    //
    // Two kinds, in one ordered list, first match wins:
    //   a script enum   matches a bare name, but only in an operand of that kind
    //                   (`Temperature` is a logic type only where a logic type is expected);
    //   a basic enum    matches `Prefix.Name` in any value operand, or the bare name when the
    //                   prefix is empty.
    public sealed class Ic10EnumSet
    {
        private struct Entry
        {
            public Ic10EnumTable Table;
            public int IncludeFlag;
            public string Prefix;
            public bool Basic;
        }

        public readonly Ic10EnumTable LogicType;
        public readonly Ic10EnumTable LogicSlotType;
        public readonly Ic10EnumTable ReagentMode;
        public readonly Ic10EnumTable BatchMethod;

        private readonly List<Entry> _entries = new List<Entry>();

        public Ic10EnumSet(Ic10EnumTable logicType, Ic10EnumTable logicSlotType, Ic10EnumTable reagentMode, Ic10EnumTable batchMethod)
        {
            LogicType = logicType;
            LogicSlotType = logicSlotType;
            ReagentMode = reagentMode;
            BatchMethod = batchMethod;
            _entries.Add(new Entry { Table = logicType, IncludeFlag = Include.LogicType });
            _entries.Add(new Entry { Table = logicSlotType, IncludeFlag = Include.LogicSlotType });
            _entries.Add(new Entry { Table = reagentMode, IncludeFlag = Include.LogicReagentMode });
            _entries.Add(new Entry { Table = batchMethod, IncludeFlag = Include.LogicBatchMethod });
            AddBasic("LogicType", logicType);
            AddBasic("LogicSlotType", logicSlotType);
            AddBasic("Color", new Ic10EnumTable("Color", Ic10StationeersTables.ColorType));
            AddBasic(string.Empty, new Ic10EnumTable("ConditionOperation", Ic10StationeersTables.ConditionOperation));
            AddBasic("Vent", new Ic10EnumTable("Vent", Ic10StationeersTables.VentDirection));
            AddBasic("SortingClass", new Ic10EnumTable("SortingClass", Ic10StationeersTables.SortingClass));
            AddBasic("SlotClass", new Ic10EnumTable("SlotClass", Ic10StationeersTables.SlotClass));
            AddBasic("SorterInstruction", new Ic10EnumTable("SorterInstruction", Ic10StationeersTables.SorterInstruction));
            AddBasic("PrinterInstruction", new Ic10EnumTable("PrinterInstruction", Ic10StationeersTables.PrinterInstruction));
        }

        // The four tables Stationeers scripts are written against, with Stationeers' own
        // names and numbers, and the basic enums: the five the sorter and the printer are
        // written against, and Color and Vent, which real programs use most (41 and
        // 18 uses in a set of published programs). The other device-specific
        // basic enums Stationeers also registers (Sound, GasType, ...) are not added here:
        // which of them an ONI device model wants is that model's decision, made with AddBasic.
        public static Ic10EnumSet Stationeers()
        {
            return new Ic10EnumSet(
                new Ic10EnumTable("LogicType", Ic10StationeersTables.LogicType, 0xFFFF),
                new Ic10EnumTable("LogicSlotType", Ic10StationeersTables.LogicSlotType, 0xFF),
                new Ic10EnumTable("LogicReagentMode", Ic10StationeersTables.LogicReagentMode),
                new Ic10EnumTable("LogicBatchMethod", Ic10StationeersTables.LogicBatchMethod));
        }

        // Stationeers' tables with ONI's own logic types added to LogicType. Everything a
        // Stationeers script spells keeps its number. See Ic10OniTables.
        public static Ic10EnumSet Oni()
        {
            List<KeyValuePair<string, int>> logicType = new List<KeyValuePair<string, int>>(Ic10StationeersTables.LogicType);
            logicType.AddRange(Ic10OniTables.LogicType);
            return new Ic10EnumSet(
                new Ic10EnumTable("LogicType", logicType, 0xFFFF),
                new Ic10EnumTable("LogicSlotType", Ic10StationeersTables.LogicSlotType, 0xFF),
                new Ic10EnumTable("LogicReagentMode", Ic10StationeersTables.LogicReagentMode),
                new Ic10EnumTable("LogicBatchMethod", Ic10StationeersTables.LogicBatchMethod));
        }

        public void AddBasic(string prefix, Ic10EnumTable table)
        {
            _entries.Add(new Entry { Table = table, Prefix = prefix ?? string.Empty, Basic = true });
        }

        internal bool TryResolve(string code, int flags, out int value)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                string name = code;
                if (entry.Basic)
                {
                    if ((flags & Include.Enum) == 0)
                    {
                        continue;
                    }
                    if (entry.Prefix.Length != 0)
                    {
                        string[] parts = code.Split('.');
                        if (parts.Length != 2 || !parts[0].Equals(entry.Prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        name = parts[1];
                    }
                }
                else if ((flags & entry.IncludeFlag) == 0)
                {
                    continue;
                }
                if (entry.Table.TryGet(name, out value))
                {
                    return true;
                }
            }
            value = 0;
            return false;
        }
    }

    // Which ways an operand may be read: Stationeers' InstructionInclude, same bit values.
    internal static class Include
    {
        public const int RegisterIndex = 1;
        public const int Alias = 2;
        public const int Value = 4;
        public const int JumpTag = 8;
        public const int DeviceIndex = 0x10;
        public const int Define = 0x20;
        public const int Enum = 0x40;
        public const int LogicType = 0x80;
        public const int LogicSlotType = 0x100;
        public const int LogicReagentMode = 0x200;
        public const int LogicBatchMethod = 0x400;
        public const int NetworkIndex = 0x800;

        public const int MaskDefineValue = 0x64;
        public const int MaskDeviceIndex = 0x812;
        public const int MaskStoreIndex = 3;
        public const int MaskDoubleValue = 0x6F;

        public const int AnyValue = Value | Enum | LogicType | LogicSlotType | LogicReagentMode | LogicBatchMethod;
    }
}
