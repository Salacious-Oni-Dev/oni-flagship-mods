using System;

namespace Ic10
{
    // What a printer stack keeps between ticks, for the save.
    public sealed class Ic10PrinterStackState
    {
        public double[] Cells;
        public int Pointer;
        // The cell of the order in hand, -1 for none; its own copy of the cell's value; the
        // handler the instructions before it set.
        public int OrderCell = -1;
        public double OrderValue;
        public int OrderHandler;
    }

    // The instruction stack of a Stationeers printer (SimpleFabricatorBase: _stack,
    // OnLogicTick, Exported, ClearMemory), driving an ONI fabricator. A chip writes
    // instructions into cells 0 to 53 with `put`; the machine works through them one logic
    // tick at a time.
    //
    // A cell is a number: its low byte is the instruction (PrinterInstruction), the bits
    // above it the instruction's own fields (LogicStack.Unpack*). A counted order is
    // ExecuteRecipe: quantity in the second byte, the product's hash in the four above:
    //     hash * 65536 + quantity * 256 + 2
    //
    // Ported as read. Where ONI has nothing for an instruction to act on (a lock, a reagent
    // to eject, reagents that could be missing) the instruction is still taken and used up,
    // so a Stationeers stack does not stall here.
    public sealed class Ic10PrinterStack
    {
        public const int Size = 64;
        // STACK_TRANSACTION_END, STACK_REQUEST_BEGIN, STACK_REQUEST_END, STACK_POINTER_ADDRESS.
        public const int LastInstruction = 53;
        public const int FirstRequest = 54;
        public const int LastRequest = 62;
        public const int PointerAddress = 63;

        // PrinterInstruction.
        public const int OpNone = 0;
        public const int OpStackPointer = 1;
        public const int OpExecuteRecipe = 2;
        public const int OpWaitUntilNextValid = 3;
        public const int OpJumpIfNextInvalid = 4;
        public const int OpJumpToAddress = 5;
        public const int OpDeviceSetLock = 6;
        public const int OpEjectReagent = 7;
        public const int OpEjectAllReagents = 8;
        public const int OpMissingRecipeReagent = 9;

        // ComplexFabricator.MAX_QUEUE_SIZE: the most one recipe's order count shows. A
        // larger quantity is fed to the machine 99 at a time.
        public const int MostQueued = 99;

        // FabricatorCreateStackAddress.InvalidHandler: none, wait, or an address to jump to.
        private const int NoHandler = int.MinValue;
        private const int WaitHandler = -1;

        private readonly double[] _cells = new double[Size];
        private readonly Ic10OniFabricator _fabricator;
        private readonly Func<bool> _ready;

        private int _pointer;
        private int _orderCell = -1;
        private long _orderValue;
        private int _orderHandler = NoHandler;
        // The recipe the order is being made as, and the machine's count of items made of
        // it when last looked at; -1: not looked at yet.
        private int _orderRecipe = -1;
        private int _lastMade = -1;

        // ready: the machine is switched on and powered (OnLogicTick's first test).
        public Ic10PrinterStack(Ic10OniFabricator fabricator, Func<bool> ready)
        {
            _fabricator = fabricator;
            _ready = ready;
        }

        // LogicStack.PackByteInt32(opCode, byte1, int1).
        public static double Order(int productHash, int quantity)
        {
            return Ic10Math.LongToDouble(((long)productHash << 16) | ((long)(quantity & 0xFF) << 8) | OpExecuteRecipe);
        }

        public bool HasOrder
        {
            get { return _orderCell >= 0; }
        }

        public int Pointer
        {
            get { return _pointer; }
        }

        // LogicStack's indexer: StackUnderflowException below 0, StackOverflowException
        // past the end.
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

        // SimpleFabricatorBase.ClearMemory: every cell, the order in hand, the pointer. The
        // machine is not stopped: Stationeers leaves Activate at 1, so what was being made
        // goes on being made until its materials run out. Here that is an order without end
        // on the recipe, which a chip stops as it would there, with Activate 0.
        public void ClearMemory()
        {
            if (_orderCell >= 0 && _orderRecipe >= 0 && _orderRecipe < _fabricator.Count()
                && _fabricator.GetQueue(_orderRecipe) != 0)
            {
                _fabricator.SetQueue(_orderRecipe, Ic10OniFabricator.Forever);
            }
            Array.Clear(_cells, 0, Size);
            _orderCell = -1;
            _orderRecipe = -1;
            _lastMade = -1;
            SetPointer(0);
        }

        public Ic10PrinterStackState Save()
        {
            return new Ic10PrinterStackState
            {
                Cells = (double[])_cells.Clone(),
                Pointer = _pointer,
                OrderCell = _orderCell,
                OrderValue = Ic10Math.LongToDouble(_orderValue),
                OrderHandler = _orderHandler,
            };
        }

        public void Load(Ic10PrinterStackState state)
        {
            Array.Clear(_cells, 0, Size);
            if (state == null)
            {
                return;
            }
            if (state.Cells != null)
            {
                Array.Copy(state.Cells, _cells, Math.Min(Size, state.Cells.Length));
            }
            _pointer = state.Pointer < 0 ? 0 : state.Pointer > LastInstruction ? LastInstruction : state.Pointer;
            _orderCell = state.OrderCell >= 0 && state.OrderCell <= LastInstruction ? state.OrderCell : -1;
            _orderValue = Ic10Math.DoubleToLong(state.OrderValue, true);
            _orderHandler = state.OrderHandler;
            // The recipe is the machine's selection, which the machine saves itself; the
            // count of items made starts again with the game.
            _orderRecipe = -1;
            _lastMade = -1;
        }

        // StackPointerStackAddress is made with value 0, so its opcode byte is 0 and the
        // cell holds the index in the bits above it. The machine never reads the cell back:
        // a chip that writes cell 63 changes what `get` reads until the pointer next moves,
        // and nothing else.
        private void SetPointer(int index)
        {
            _pointer = index;
            _cells[PointerAddress] = (long)index << 8;
        }

        // AdvanceStack: past 53 it is 0 again.
        private void Advance()
        {
            SetPointer(_pointer >= LastInstruction ? 0 : _pointer + 1);
        }

        // _stack.Clear(54, 62), whose end is not included (LogicStack.Clear): cells 54 to
        // 61, and 62 is left as it is.
        private void ClearRequests()
        {
            Array.Clear(_cells, FirstRequest, LastRequest - FirstRequest);
        }

        private static int Quantity(long value)
        {
            return (int)((value >> 8) & 0xFF);
        }

        // LogicStack.UnpackUInt16, then ClampAddress.
        private static int Address(long value)
        {
            int address = (int)((value >> 8) & 0xFFFF);
            return address > LastInstruction ? LastInstruction : address;
        }

        // SetRecipeFromHash, as the adapter's RecipeHash write does it: the first recipe for
        // that product, else the recipe of that id. -1 when the hash names none.
        public static int Find(Ic10OniFabricator fabricator, int hash)
        {
            if (hash == 0)
            {
                return -1;
            }
            int count = fabricator.Count();
            for (int i = 0; i < count; i++)
            {
                if (fabricator.ProductHash(i) == hash)
                {
                    return i;
                }
            }
            for (int i = 0; i < count; i++)
            {
                if (fabricator.RecipeIdHash(i) == hash)
                {
                    return i;
                }
            }
            return -1;
        }

        // The selected recipe, if it is one the machine can be asked for.
        private int Selected()
        {
            int selected = _fabricator.GetSelected();
            if (selected < 0 || selected >= _fabricator.Count() || _fabricator.ProductHash(selected) == 0)
            {
                return -1;
            }
            return selected;
        }

        // OnLogicTick. Once per logic tick, half a second of game time.
        public void Tick()
        {
            if (_ready != null && !_ready())
            {
                return;
            }
            if (_orderCell >= 0)
            {
                TickOrder();
                return;
            }
            // IsBusy: while the machine is making something the stack waits. (With an order
            // in hand the tick above still runs: it is where items made are counted, which
            // Stationeers does in Exported.)
            if (_fabricator.Working() >= 0)
            {
                return;
            }
            int handler = NoHandler;
            for (int walked = 0; walked < LastInstruction; walked++)
            {
                int cell = _pointer;
                long value = Ic10Math.DoubleToLong(_cells[cell], true);
                int op = (int)(value & 0xFF);
                if (op == OpNone)
                {
                    Advance();
                    continue;
                }
                switch (op)
                {
                    case OpJumpToAddress:
                        SetPointer(Address(value));
                        return;

                    case OpDeviceSetLock:
                        // An ONI machine has no lock; the instruction is used up as it is
                        // in Stationeers.
                        Advance();
                        _cells[cell] = 0.0;
                        return;

                    case OpWaitUntilNextValid:
                        handler = WaitHandler;
                        Advance();
                        continue;

                    case OpJumpIfNextInvalid:
                        handler = Address(value);
                        Advance();
                        continue;

                    case OpExecuteRecipe:
                        if (Quantity(value) > 0)
                        {
                            // SetRecipeHashSafe(hash, start: true), then the order is in
                            // hand. A hash that names no recipe: the tick runs off the main
                            // thread (GameManager.GameTick switches to the thread pool
                            // before LogicStack.LogicStackTick), so SetRecipeFromHash runs
                            // after the order is in hand, finds nothing, drops the order
                            // and advances. The cell is kept and met again next time round.
                            int found = Find(_fabricator, (int)(value >> 16));
                            if (found < 0)
                            {
                                Advance();
                                return;
                            }
                            if (found != _fabricator.GetSelected())
                            {
                                _fabricator.SetSelected(found);
                            }
                            _orderCell = cell;
                            _orderValue = value;
                            _orderHandler = handler;
                            _orderRecipe = -1;
                            _lastMade = -1;
                            return;
                        }
                        break;

                    case OpEjectReagent:
                    case OpEjectAllReagents:
                        // Stationeers uses these up once the reagent is gone from the
                        // machine. An ONI machine holds no loose reagents to eject.
                        _cells[cell] = 0.0;
                        Advance();
                        return;
                }
                // A quantity of 0, or a number that is no instruction: passed over, and the
                // handler set for "the next" is spent.
                handler = NoHandler;
                Advance();
            }
        }

        // The order in hand. Stationeers splits this between OnLogicTick (start it, or deal
        // with missing reagents) and Exported (count one off for each item made).
        private void TickOrder()
        {
            int recipe = Selected();
            if (recipe != _orderRecipe)
            {
                // The selection moved under the order (a RecipeHash write): Stationeers
                // goes on with the same count as the new product. The old recipe is left
                // its item in hand.
                if (_orderRecipe >= 0 && _orderRecipe < _fabricator.Count() && _fabricator.GetQueue(_orderRecipe) > 0)
                {
                    _fabricator.SetQueue(_orderRecipe, _fabricator.Working() == _orderRecipe ? 1 : 0);
                }
                _orderRecipe = recipe;
                _lastMade = -1;
            }
            int quantity = Quantity(_orderValue);
            if (recipe >= 0 && _fabricator.Made != null)
            {
                int made = _fabricator.Made(recipe);
                if (_lastMade < 0 || made < _lastMade)
                {
                    _lastMade = made;
                }
                // Exported: Decrement, and write the cell.
                if (made > _lastMade)
                {
                    int done = Math.Min(made - _lastMade, quantity);
                    quantity -= done;
                    _lastMade = made;
                    _orderValue = (_orderValue & ~0xFF00L) | ((long)quantity << 8);
                    _cells[_orderCell] = Ic10Math.LongToDouble(_orderValue);
                }
            }
            if (quantity == 0)
            {
                // ClearAndAdvance.
                ClearRequests();
                if (recipe >= 0 && _fabricator.GetQueue(recipe) != 0)
                {
                    _fabricator.SetQueue(recipe, 0);
                }
                _cells[_orderCell] = 0.0;
                _orderCell = -1;
                _orderRecipe = -1;
                Advance();
                return;
            }
            if (recipe < 0)
            {
                // Nothing the machine can be asked for. This is where Stationeers has
                // "reagents missing"; ONI's machines are brought their materials once the
                // order stands, so the only order that cannot go ahead is one with no recipe.
                if (_orderHandler == NoHandler)
                {
                    // SkipAndAdvance: the cell stays, and is met again next time round.
                    ClearRequests();
                    _orderCell = -1;
                    Advance();
                }
                else if (_orderHandler >= 0)
                {
                    // SkipAndJump.
                    ClearRequests();
                    _orderCell = -1;
                    SetPointer(_orderHandler > LastInstruction ? LastInstruction : _orderHandler);
                }
                return;
            }
            // Stationeers sets Activate to 1 whenever the order stands and the machine is
            // idle; here the recipe's count is kept at what is still owed, so an order a
            // chip cancelled with Activate 0 is placed again, as it is there.
            int owed = quantity > MostQueued ? MostQueued : quantity;
            if (_fabricator.GetQueue(recipe) != owed)
            {
                _fabricator.SetQueue(recipe, owed);
            }
        }
    }
}
