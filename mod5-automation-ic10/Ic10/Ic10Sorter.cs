using System;

namespace Ic10
{
    // What the sorter asks of the item waiting at its input: LogicSorter reads
    // ImportingThing.PrefabHash, .SortingClass, .SlotType and IQuantity.GetQuantity.
    public struct Ic10SortItem
    {
        public int PrefabHash;
        public ushort SortingClass;
        public ushort SlotType;
        public ushort Quantity;
    }

    public sealed class Ic10SorterState
    {
        public double[] Cells;
        public int Mode;
        public int ImportCount;
        public int ExportCount;
        public int PendingLimit = -1;
        public double PendingLimitValue;
    }

    // Stationeers' LogicSorter: a list of 32 filters a chip writes into the device's memory,
    // evaluated against each item that waits at the input. A match leaves by the second
    // export, anything else by the first. The list is not a program: no pointer, no reserved
    // cell, nothing the device erases; the only cell it ever writes is a
    // LimitNextExecutionByCount cell it counts down.
    public sealed class Ic10Sorter
    {
        public const int Size = 32;

        // SorterInstruction.
        public const int OpNone = 0;
        public const int OpPrefabHashEquals = 1;
        public const int OpPrefabHashNotEquals = 2;
        public const int OpSortingClassCompare = 3;
        public const int OpSlotTypeCompare = 4;
        public const int OpQuantityCompare = 5;
        public const int OpLimitNextExecutionByCount = 6;

        // LogicOperator, the sorter's Mode.
        public const int ModeAll = 0;
        public const int ModeAny = 1;
        public const int ModeNone = 2;

        // ConditionOperation.
        public const int Equals_ = 0;
        public const int Greater = 1;
        public const int Less = 2;
        public const int NotEquals = 3;

        private readonly double[] _cells = new double[Size];
        private int _mode;

        // LogicSorter._stackExecutionLimitStackAddress: the limit cell met last, waiting for
        // the filter cell after it. Not cleared between evaluations, so a limit cell with no
        // filter cell after it applies to the first filter cell of the next evaluation.
        private int _limitCell = -1;
        private long _limitValue;

        public int ImportCount;
        public int ExportCount;

        public static readonly string[] ModeNames = { "All", "Any", "None" };

        public int Mode
        {
            get { return _mode; }
            // Device's Mode write, clamped to 0..ModeStrings.Length - 1.
            set { _mode = value < 0 ? 0 : value >= ModeNames.Length ? ModeNames.Length - 1 : value; }
        }

        public static double PrefabCell(int op, int prefabHash)
        {
            return Ic10Math.LongToDouble(((long)(uint)prefabHash << 8) | (long)(op & 0xFF));
        }

        public static double CompareCell(int op, int operation, int value)
        {
            return Ic10Math.LongToDouble(((long)(value & 0xFFFF) << 16) | ((long)(operation & 0xFF) << 8) | (long)(op & 0xFF));
        }

        public static double LimitCell(uint count)
        {
            return Ic10Math.LongToDouble(((long)count << 8) | OpLimitNextExecutionByCount);
        }

        // LogicStack's indexer: StackUnderflowException below 0, StackOverflowException past
        // the end.
        public double ReadMemory(int address)
        {
            Check(address);
            return _cells[address];
        }

        public void WriteMemory(int address, double value)
        {
            Check(address);
            _cells[address] = value;
        }

        // LogicSorter.ClearMemory: _stack.Clear(). The counters are the ClearMemory logic
        // type's, not this.
        public void ClearMemory()
        {
            Array.Clear(_cells, 0, Size);
        }

        // DeviceImport / DeviceImportExport SetLogicValue(ClearMemory, value > 0).
        public void ClearCounts()
        {
            ImportCount = 0;
            ExportCount = 0;
        }

        private static void Check(int address)
        {
            if (address < 0)
            {
                throw new Ic10MemoryException(false);
            }
            if (address >= Size)
            {
                throw new Ic10MemoryException(true);
            }
        }

        // LogicSorter.DoFilter: true sends the item to the second export. Run again on every
        // tick an item waits, and the limit counts down on each run that matches, not on each
        // item moved (kept, as Stationeers).
        public bool Matches(Ic10SortItem item)
        {
            switch (_mode)
            {
                case ModeAny:
                    for (int k = 0; k < Size; k++)
                    {
                        if (!Skip(k) && Execute(IsMatch(k, item)))
                        {
                            return true;
                        }
                    }
                    return false;
                case ModeAll:
                {
                    bool any = false;
                    for (int k = 0; k < Size; k++)
                    {
                        if (!Skip(k))
                        {
                            if (!Execute(IsMatch(k, item)))
                            {
                                return false;
                            }
                            any = true;
                        }
                    }
                    return any;
                }
                case ModeNone:
                {
                    bool any = false;
                    for (int k = 0; k < Size; k++)
                    {
                        if (!Skip(k))
                        {
                            if (Execute(IsMatch(k, item)))
                            {
                                return false;
                            }
                            any = true;
                        }
                    }
                    return any;
                }
                default:
                    return false;
            }
        }

        // LogicSorter.SkipInstruction.
        private bool Skip(int k)
        {
            long value = Ic10Math.DoubleToLong(_cells[k], true);
            int op = (int)(value & 0xFF);
            if (op == OpNone)
            {
                return true;
            }
            if (op == OpLimitNextExecutionByCount)
            {
                _limitCell = k;
                _limitValue = value;
                return true;
            }
            return false;
        }

        private uint LimitCount
        {
            get { return (uint)((_limitValue >> 8) & 0xFFFFFFFFL); }
        }

        // LogicSorter.IsFilterInstruction.
        private bool IsMatch(int k, Ic10SortItem item)
        {
            if (_limitCell >= 0 && LimitCount == 0)
            {
                return false;
            }
            long value = Ic10Math.DoubleToLong(_cells[k], true);
            switch ((int)(value & 0xFF))
            {
                case OpPrefabHashEquals:
                    return (int)(uint)((value >> 8) & 0xFFFFFFFFL) == item.PrefabHash;
                case OpPrefabHashNotEquals:
                    return (int)(uint)((value >> 8) & 0xFFFFFFFFL) != item.PrefabHash;
                case OpSortingClassCompare:
                    return Compare(value, item.SortingClass);
                case OpSlotTypeCompare:
                    return Compare(value, item.SlotType);
                case OpQuantityCompare:
                    return Compare(value, item.Quantity);
                default:
                    return false;
            }
        }

        // LogicStack.CompareUInt16: the cell's value on the left, the item's on the right, so
        // Greater reads "the item is greater than the cell's value".
        private static bool Compare(long packed, ushort right)
        {
            int operation = (int)((packed >> 8) & 0xFF);
            ushort left = (ushort)((packed >> 16) & 0xFFFF);
            switch (operation)
            {
                case Equals_:
                    return left == right;
                case Greater:
                    return left < right;
                case Less:
                    return left > right;
                case NotEquals:
                    return left != right;
                default:
                    return false;
            }
        }

        // LogicSorter.Execute: a match under a limit with count left takes one off and writes
        // the cell back (ExecutionLimitStackAddress.Decrement packs only the opcode and the
        // count). The limit is spent either way.
        private bool Execute(bool result)
        {
            if (result && _limitCell >= 0 && LimitCount != 0)
            {
                _limitValue = ((long)(LimitCount - 1) << 8) | (_limitValue & 0xFF);
                _cells[_limitCell] = Ic10Math.LongToDouble(_limitValue);
            }
            _limitCell = -1;
            return result;
        }

        public Ic10SorterState Save()
        {
            return new Ic10SorterState
            {
                Cells = (double[])_cells.Clone(),
                Mode = _mode,
                ImportCount = ImportCount,
                ExportCount = ExportCount,
                PendingLimit = _limitCell,
                PendingLimitValue = Ic10Math.LongToDouble(_limitValue),
            };
        }

        public void Load(Ic10SorterState state)
        {
            if (state == null)
            {
                return;
            }
            Array.Clear(_cells, 0, Size);
            if (state.Cells != null)
            {
                Array.Copy(state.Cells, _cells, Math.Min(Size, state.Cells.Length));
            }
            Mode = state.Mode;
            ImportCount = state.ImportCount;
            ExportCount = state.ExportCount;
            _limitCell = state.PendingLimit >= 0 && state.PendingLimit < Size ? state.PendingLimit : -1;
            _limitValue = Ic10Math.DoubleToLong(state.PendingLimitValue, true);
        }
    }
}
