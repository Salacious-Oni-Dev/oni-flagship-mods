using System;
using System.Collections.Generic;

namespace Ic10
{
    public sealed class Ic10ChecklistState
    {
        public double[] Cells;
        public bool Dirty;
    }

    // A bin's or a fridge's item checklist set by a chip (ONI's TreeFilterable).
    //
    // Stationeers' lockers take no filter; this mod lets a program set
    // the checklist with the IC Sorter's "this item kind" filter. So the building gets the
    // sorter's memory, 32 cells a chip writes with `put`, and the checklist becomes every item
    // kind named by a FilterPrefabHashEquals cell (Ic10Sorter.PrefabCell(1, hash)). Every other
    // cell is kept as written and means nothing to the bin.
    //
    // A write marks the list dirty and the game side applies it a fifth of a second of game
    // time later, so a program that writes all its cells in one chip tick changes the
    // checklist once. Each change drops the items no longer accepted
    // (TreeFilterable.UpdateFilters). A bin no chip has written keeps the checklist the
    // player set.
    public sealed class Ic10Checklist
    {
        public const int Size = Ic10Sorter.Size;

        private readonly double[] _cells = new double[Size];

        // True from a write until the game side has applied the list.
        public bool Dirty;

        // Called on each write and clear, for the game side to schedule the apply.
        public Action Changed;

        public double ReadMemory(int address)
        {
            Check(address);
            return _cells[address];
        }

        public void WriteMemory(int address, double value)
        {
            Check(address);
            _cells[address] = value;
            MarkChanged();
        }

        // `clr`: an empty list, so the checklist is emptied and the bin accepts nothing.
        public void ClearMemory()
        {
            Array.Clear(_cells, 0, Size);
            MarkChanged();
        }

        private void MarkChanged()
        {
            Dirty = true;
            if (Changed != null)
            {
                Changed();
            }
        }

        // True while every cell is 0.
        public bool IsEmpty
        {
            get
            {
                for (int k = 0; k < Size; k++)
                {
                    if (_cells[k] != 0.0)
                    {
                        return false;
                    }
                }
                return true;
            }
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

        // The item kinds the list names, in cell order, each once.
        public List<int> Hashes()
        {
            List<int> hashes = new List<int>();
            for (int k = 0; k < Size; k++)
            {
                long value = Ic10Math.DoubleToLong(_cells[k], true);
                if ((value & 0xFF) != Ic10Sorter.OpPrefabHashEquals)
                {
                    continue;
                }
                int hash = (int)(uint)((value >> 8) & 0xFFFFFFFFL);
                if (!hashes.Contains(hash))
                {
                    hashes.Add(hash);
                }
            }
            return hashes;
        }

        public Ic10ChecklistState Save()
        {
            return new Ic10ChecklistState { Cells = (double[])_cells.Clone(), Dirty = Dirty };
        }

        public void Load(Ic10ChecklistState state)
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
            Dirty = state.Dirty;
        }
    }
}
