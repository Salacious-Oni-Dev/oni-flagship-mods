using System;
using System.Collections.Generic;

namespace Ic10
{
    // The instruction set: one entry per Stationeers ScriptCommand. Each builder checks the
    // token count, parses the operands once, and returns the function the chip calls every
    // time the line runs.
    //
    // Operand ORDER inside each instruction is Stationeers' order, because it decides which
    // error a line with two problems reports.
    public sealed partial class Ic10Chip
    {
        private delegate Func<int, int> Builder(Ic10Chip chip, string[] t, int line, CompiledLine compiled);

        private static readonly Dictionary<string, Builder> _builders = MakeBuilders();

        private const int BatchAverage = 0;
        private const int BatchSum = 1;
        private const int BatchMinimum = 2;
        private const int BatchMaximum = 3;
        private const int BatchCount = 4;

        private const int ReagentContents = 0;
        private const int ReagentRequired = 1;
        private const int ReagentRecipe = 2;
        private const int ReagentTotalContents = 3;

        // Every mnemonic the chip accepts, for tooling and tests.
        public static IEnumerable<string> Mnemonics
        {
            get { return _builders.Keys; }
        }

        private static void Need(string[] t, int count, int line)
        {
            if (t.Length != count)
            {
                throw new Ic10Exception(Ic10ErrorType.IncorrectArgumentCount, line);
            }
        }

        private Ic10Exception Error(Ic10ErrorType type, int line)
        {
            return new Ic10Exception(type, line);
        }

        private Operand Num(int line, string code)
        {
            return Operand.Number(this, line, code);
        }

        private Operand Whole(int line, string code)
        {
            return Operand.Int(this, line, code);
        }

        private Operand Dest(int line, string code)
        {
            return Operand.Store(this, line, code);
        }

        private Operand LogicTypeOf(int line, string code)
        {
            return Operand.Enum(this, line, code, Enums.LogicType);
        }

        private Operand SlotTypeOf(int line, string code)
        {
            return Operand.Enum(this, line, code, Enums.LogicSlotType);
        }

        // r? = f(a)
        private Func<int, int> Set1(int line, string store, string a, Func<double, double> f)
        {
            Operand s = Dest(line, store);
            Operand x = Num(line, a);
            return i =>
            {
                int target = s.Store();
                Registers[target] = f(x.Number());
                return i + 1;
            };
        }

        // r? = f(a, b)
        private Func<int, int> Set2(int line, string store, string a, string b, Func<double, double, double> f)
        {
            Operand s = Dest(line, store);
            Operand x = Num(line, a);
            Operand y = Num(line, b);
            return i =>
            {
                int target = s.Store();
                double first = x.Number();
                double second = y.Number();
                Registers[target] = f(first, second);
                return i + 1;
            };
        }

        // r? = f(a, b, c)
        private Func<int, int> Set3(int line, string store, string a, string b, string c, Func<double, double, double, double> f)
        {
            Operand s = Dest(line, store);
            Operand x = Num(line, a);
            Operand y = Num(line, b);
            Operand z = Num(line, c);
            return i =>
            {
                int target = s.Store();
                double first = x.Number();
                double second = y.Number();
                double third = z.Number();
                Registers[target] = f(first, second, third);
                return i + 1;
            };
        }

        // r? = a op b on the 53-bit integers the doubles hold.
        private Func<int, int> Bits2(int line, string store, string a, string b, Func<long, long, long> f)
        {
            Operand s = Dest(line, store);
            Operand x = Num(line, a);
            Operand y = Num(line, b);
            return i =>
            {
                int target = s.Store();
                long first = x.Long();
                long second = y.Long();
                Registers[target] = Ic10Math.LongToDouble(f(first, second));
                return i + 1;
            };
        }

        // r? = a shifted by b. `unsigned` reads a as its low 54 bits.
        private Func<int, int> Shift(int line, string store, string a, string b, bool unsigned, Func<long, int, long> f)
        {
            Operand s = Dest(line, store);
            Operand x = Num(line, a);
            Operand y = Num(line, b);
            return i =>
            {
                int target = s.Store();
                long value = x.Long(!unsigned);
                int count = y.Int32();
                Registers[target] = Ic10Math.LongToDouble(f(value, count));
                return i + 1;
            };
        }

        // The landing line of a taken jump. Relative jumps add to the current line; absolute
        // ones cancel it, exactly as Stationeers' `index + offset + target` with offset -index.
        private static int Land(int index, bool relative, Operand jump)
        {
            return index + (relative ? 0 : -index) + jump.Line();
        }

        private Func<int, int> Branch(Func<bool> taken, Operand jump, bool relative, bool link)
        {
            return i =>
            {
                if (!taken())
                {
                    return i + 1;
                }
                int target = Land(i, relative, jump);
                if (link)
                {
                    Registers[ReturnAddressIndex] = i + 1;
                }
                return target;
            };
        }

        private Func<int, int> Branch2(int line, string a, string b, string jump, Func<double, double, bool> test, bool relative, bool link)
        {
            Operand x = Num(line, a);
            Operand y = Num(line, b);
            Operand target = Operand.Line(this, line, jump);
            return Branch(() =>
            {
                double first = x.Number();
                double second = y.Number();
                return test(first, second);
            }, target, relative, link);
        }

        private Func<int, int> BranchApprox(int line, string a, string b, string c, string jump, bool negate, bool relative, bool link)
        {
            Operand x = Num(line, a);
            Operand y = Num(line, b);
            Operand z = Num(line, c);
            Operand target = Operand.Line(this, line, jump);
            return Branch(() =>
            {
                double first = x.Number();
                double second = y.Number();
                double third = z.Number();
                return Ic10Math.Approximately(first, second, third) != negate;
            }, target, relative, link);
        }

        private Func<int, int> BranchDevice(int line, string device, string jump, bool whenSet, bool relative, bool link)
        {
            Operand d = Operand.Device(this, line, device);
            Operand target = Operand.Line(this, line, jump);
            return Branch(() => (d.GetDevice() != null) == whenSet, target, relative, link);
        }

        private Func<int, int> CompileAlias(int line, string name, string targetCode)
        {
            AliasTarget type = AliasTarget.None;
            Operand target = null;
            if (targetCode[0] == 'r')
            {
                type = AliasTarget.Register;
                target = Operand.Store(this, line, targetCode, Include.MaskStoreIndex | Include.JumpTag);
            }
            else if (targetCode[0] == 'd')
            {
                type = AliasTarget.Device;
                target = Operand.DeviceIndex(this, line, targetCode, Include.Alias | Include.JumpTag | Include.DeviceIndex);
            }
            return i =>
            {
                // A target that is neither r? nor d? leaves `target` null, and the line fails
                // with Unknown when it runs, not when it compiles.
                AliasValue value = new AliasValue(type, target.Index(type));
                if (value.Index < 0
                    || (value.Target == AliasTarget.Register && value.Index >= Registers.Length)
                    || (value.Target == AliasTarget.Device && !Host.IsValidDeviceIndex(value.Index)))
                {
                    throw Error(Ic10ErrorType.IndexOutOfRange, line);
                }
                AliasValue previous;
                if (Aliases.TryGetValue(name, out previous))
                {
                    if (previous.Target == AliasTarget.Device)
                    {
                        Host.SetDeviceLabel(previous.Index, "");
                    }
                    Aliases[name] = value;
                }
                else
                {
                    Aliases.Add(name, value);
                }
                if (value.Target == AliasTarget.Device)
                {
                    Host.SetDeviceLabel(value.Index, name);
                }
                return i + 1;
            };
        }

        private IList<IIc10Device> Batch(int line)
        {
            IList<IIc10Device> devices = Host.GetBatchDevices();
            if (devices == null)
            {
                throw Error(Ic10ErrorType.DeviceListNull, line);
            }
            return devices;
        }

        // Device.BatchRead, all four overloads. Three things in it are not what the names
        // suggest, and all three are kept: Count ignores the name hash and does not skip null
        // entries; Minimum of nothing is 0 but Maximum of nothing is -infinity; Average of
        // nothing is 0/0.
        // For a batch reader brick (Ic10LogicUnits): Device.BatchRead with no name hash.
        internal static double BatchReadPlain(int method, IList<IIc10Device> devices, int hash, int logicType)
        {
            return BatchRead(method, devices, hash, false, 0, d => d.GetLogicValue(logicType));
        }

        private static double BatchRead(int method, IList<IIc10Device> devices, int hash, bool useName, int nameHash, Func<IIc10Device, double> read)
        {
            int matched = 0;
            double result = 0.0;
            int n = devices.Count;
            switch (method)
            {
                case BatchCount:
                    while (n-- > 0)
                    {
                        if (devices[n].PrefabHash == hash)
                        {
                            matched++;
                        }
                    }
                    result = matched;
                    break;
                case BatchAverage:
                case BatchSum:
                    while (n-- > 0)
                    {
                        IIc10Device device = devices[n];
                        if (device != null && device.PrefabHash == hash && (!useName || device.NameHash == nameHash))
                        {
                            result += read(device);
                            matched++;
                        }
                    }
                    if (method == BatchAverage)
                    {
                        result /= matched;
                    }
                    break;
                case BatchMinimum:
                    result = double.PositiveInfinity;
                    while (n-- > 0)
                    {
                        IIc10Device device = devices[n];
                        if (device != null && device.PrefabHash == hash && (!useName || device.NameHash == nameHash))
                        {
                            double value = read(device);
                            if (!(value >= result))
                            {
                                result = value;
                            }
                        }
                    }
                    if (result >= double.PositiveInfinity)
                    {
                        result = 0.0;
                    }
                    break;
                case BatchMaximum:
                    result = double.NegativeInfinity;
                    while (n-- > 0)
                    {
                        IIc10Device device = devices[n];
                        if (device != null && device.PrefabHash == hash && (!useName || device.NameHash == nameHash))
                        {
                            double value = read(device);
                            if (!(value <= result))
                            {
                                result = value;
                            }
                        }
                    }
                    break;
            }
            return result;
        }

        private Func<int, int> BatchLoad(int line, string store, string hash, string name, string slot, string type, string mode)
        {
            bool bySlot = slot != null;
            bool byName = name != null;
            Operand s = Dest(line, store);
            Operand h = Whole(line, hash);
            Operand nm = byName ? Whole(line, name) : null;
            Operand sl = bySlot ? Whole(line, slot) : null;
            Operand lt = bySlot ? SlotTypeOf(line, type) : LogicTypeOf(line, type);
            Operand bm = Operand.Enum(this, line, mode, Enums.BatchMethod);
            return i =>
            {
                int target = s.Store();
                int deviceHash = h.Int();
                int nameHash = byName ? nm.Int() : 0;
                int slotIndex = bySlot ? sl.Int() : 0;
                int logicType = lt.Enum();
                // Only plain `lb` refuses logic type None; lbn, lbs and lbns do not check.
                if (!bySlot && !byName && logicType == 0)
                {
                    throw Error(Ic10ErrorType.LogicTypeIsNone, line);
                }
                int method = bm.Enum();
                IList<IIc10Device> devices = Batch(line);
                int n = devices.Count;
                while (n-- > 0)
                {
                    IIc10Device device = devices[n];
                    if (device == null || device.PrefabHash != deviceHash || (byName && device.NameHash != nameHash))
                    {
                        continue;
                    }
                    if (bySlot ? !device.CanSlotRead(logicType, slotIndex) : !device.CanLogicRead(logicType))
                    {
                        throw Error(bySlot ? Ic10ErrorType.IncorrectLogicSlotType : Ic10ErrorType.IncorrectLogicType, line);
                    }
                }
                Registers[target] = bySlot
                    ? BatchRead(method, devices, deviceHash, byName, nameHash, d => d.GetSlotValue(logicType, slotIndex))
                    : BatchRead(method, devices, deviceHash, byName, nameHash, d => d.GetLogicValue(logicType));
                return i + 1;
            };
        }

        private Func<int, int> BatchStore(int line, string hash, string name, string type, string value)
        {
            bool byName = name != null;
            Operand h = Whole(line, hash);
            Operand nm = byName ? Whole(line, name) : null;
            Operand v = Num(line, value);
            Operand lt = LogicTypeOf(line, type);
            return i =>
            {
                int deviceHash = h.Int();
                int nameHash = byName ? nm.Int() : 0;
                double wanted = v.Number();
                int logicType = lt.Enum();
                if (logicType == 0)
                {
                    throw Error(Ic10ErrorType.LogicTypeIsNone, line);
                }
                IList<IIc10Device> devices = Batch(line);
                int n = devices.Count;
                while (n-- > 0)
                {
                    IIc10Device device = devices[n];
                    if (device == null || device.PrefabHash != deviceHash || (byName && device.NameHash != nameHash))
                    {
                        continue;
                    }
                    if (!device.CanLogicWrite(logicType))
                    {
                        throw Error(Ic10ErrorType.IncorrectLogicType, line);
                    }
                    if (device.GetLogicValue(logicType) != wanted)
                    {
                        device.SetLogicValue(logicType, wanted);
                    }
                }
                return i + 1;
            };
        }

        // put / putd: the one place a memory device's own exception is translated.
        private void WriteDeviceMemory(IIc10MemoryWritable memory, int address, double value, int line)
        {
            try
            {
                memory.WriteMemory(address, value);
                Host.HasPut();
            }
            catch (NullReferenceException)
            {
                throw Error(Ic10ErrorType.DeviceNotFound, line);
            }
            catch (Ic10MemoryException e)
            {
                throw Error(e.Overflow ? Ic10ErrorType.StackOverFlow : Ic10ErrorType.StackUnderFlow, line);
            }
            catch (Exception)
            {
                throw Error(Ic10ErrorType.Unknown, line);
            }
        }

        private int StackSlot(int line, int offset)
        {
            int slot = Ic10Math.ToInt32(Math.Round(Registers[StackPointerIndex])) + offset;
            if (slot < 0)
            {
                throw Error(Ic10ErrorType.StackUnderFlow, line);
            }
            if (slot >= Stack.Length)
            {
                throw Error(Ic10ErrorType.StackOverFlow, line);
            }
            return slot;
        }

        private static Dictionary<string, Builder> MakeBuilders()
        {
            Dictionary<string, Builder> d = new Dictionary<string, Builder>(StringComparer.Ordinal);

            // ---- arithmetic -------------------------------------------------------------
            AddSet1(d, "move", x => x);
            AddSet1(d, "sqrt", Math.Sqrt);
            AddSet1(d, "round", Math.Round);
            AddSet1(d, "trunc", Math.Truncate);
            AddSet1(d, "ceil", Math.Ceiling);
            AddSet1(d, "floor", Math.Floor);
            AddSet1(d, "abs", Math.Abs);
            AddSet1(d, "log", Math.Log);
            AddSet1(d, "exp", Math.Exp);
            AddSet1(d, "sin", Math.Sin);
            AddSet1(d, "asin", Math.Asin);
            AddSet1(d, "cos", Math.Cos);
            AddSet1(d, "acos", Math.Acos);
            AddSet1(d, "tan", Math.Tan);
            AddSet1(d, "atan", Math.Atan);
            AddSet1(d, "sgn", x => x > 0.0 ? 1.0 : (x < 0.0 ? -1.0 : 0.0));
            AddSet1(d, "snan", x => double.IsNaN(x) ? 1.0 : 0.0);
            AddSet1(d, "snanz", x => double.IsNaN(x) ? 0.0 : 1.0);

            AddSet2(d, "add", (a, b) => a + b);
            AddSet2(d, "sub", (a, b) => a - b);
            AddSet2(d, "mul", (a, b) => a * b);
            // No divide-by-zero error: the result is an IEEE infinity or NaN.
            AddSet2(d, "div", (a, b) => a / b);
            // A negative remainder has the divisor added once, so the result takes the
            // divisor's sign only when the divisor is positive.
            AddSet2(d, "mod", (a, b) =>
            {
                double r = a % b;
                return r < 0.0 ? r + b : r;
            });
            AddSet2(d, "max", Ic10Math.Max);
            AddSet2(d, "min", Ic10Math.Min);
            AddSet2(d, "pow", Math.Pow);
            AddSet2(d, "atan2", Math.Atan2);

            d["lerp"] = (c, t, l, _) =>
            {
                Need(t, 5, l);
                return c.Set3(l, t[1], t[2], t[3], t[4], Ic10Math.Lerp);
            };
            d["clamp"] = (c, t, l, _) =>
            {
                Need(t, 5, l);
                return c.Set3(l, t[1], t[2], t[3], t[4], (a, lo, hi) => Ic10Math.Min(Ic10Math.Max(a, lo), hi));
            };
            d["select"] = (c, t, l, _) =>
            {
                Need(t, 5, l);
                return c.Set3(l, t[1], t[2], t[3], t[4], (cond, a, b) => cond != 0.0 ? a : b);
            };
            d["rand"] = (c, t, l, _) =>
            {
                Need(t, 2, l);
                Operand s = c.Dest(l, t[1]);
                return i =>
                {
                    int target = s.Store();
                    c.Registers[target] = c.Random();
                    return i + 1;
                };
            };

            // ---- bitwise ----------------------------------------------------------------
            AddBits2(d, "and", (a, b) => a & b);
            AddBits2(d, "or", (a, b) => a | b);
            AddBits2(d, "xor", (a, b) => a ^ b);
            AddBits2(d, "nor", (a, b) => ~(a | b));
            d["not"] = (c, t, l, _) =>
            {
                Need(t, 3, l);
                Operand s = c.Dest(l, t[1]);
                Operand x = c.Num(l, t[2]);
                return i =>
                {
                    int target = s.Store();
                    c.Registers[target] = Ic10Math.LongToDouble(~x.Long());
                    return i + 1;
                };
            };
            AddShift(d, "srl", true, (v, n) => v >> n);
            AddShift(d, "sra", false, (v, n) => v >> n);
            AddShift(d, "sll", false, (v, n) => v << n);
            AddShift(d, "sla", false, (v, n) => v << n);
            // Rotates work on 54 bits, one more than a double holds exactly.
            AddShift(d, "rol", true, (v, n) =>
            {
                long value = v & 0x3FFFFFFFFFFFFFL;
                int count = (n % 54 + 54) % 54;
                return ((value << count) | (value >> (54 - count))) & 0x3FFFFFFFFFFFFFL;
            });
            AddShift(d, "ror", true, (v, n) =>
            {
                long value = v & 0x3FFFFFFFFFFFFFL;
                int count = (n % 54 + 54) % 54;
                return ((value >> count) | (value << (54 - count))) & 0x3FFFFFFFFFFFFFL;
            });
            d["ext"] = (c, t, l, _) =>
            {
                Need(t, 5, l);
                Operand s = c.Dest(l, t[1]);
                Operand x = c.Num(l, t[2]);
                Operand y = c.Num(l, t[3]);
                Operand z = c.Num(l, t[4]);
                return i =>
                {
                    int target = s.Store();
                    long source = x.Long(false);
                    int start = y.Int32();
                    int length = z.Int32();
                    CheckField(c, l, start, length);
                    long low = source & 0x1FFFFFFFFFFFFFL;
                    ulong mask = (ulong)((length == 53 ? 9007199254740991L : ((1L << length) - 1)) << start);
                    c.Registers[target] = Ic10Math.LongToDouble((long)(((ulong)low & mask) >> start));
                    return i + 1;
                };
            };
            d["ins"] = (c, t, l, _) =>
            {
                Need(t, 5, l);
                Operand s = c.Dest(l, t[1]);
                Operand x = c.Num(l, t[2]);
                Operand y = c.Num(l, t[3]);
                Operand z = c.Num(l, t[4]);
                return i =>
                {
                    int target = s.Store();
                    long field = x.Long(false);
                    int start = y.Int32();
                    int length = z.Int32();
                    CheckField(c, l, start, length);
                    const ulong all = 9007199254740991uL;
                    long current = Ic10Math.DoubleToLong(c.Registers[target], false) & (long)all;
                    ulong width = length == 53 ? all : (ulong)((1L << length) - 1);
                    ulong mask = width << start;
                    ulong inserted = ((((ulong)field & all) & width) << start) & mask;
                    c.Registers[target] = Ic10Math.LongToDouble((long)((((ulong)current & ~mask) | inserted) & all));
                    return i + 1;
                };
            };

            // ---- compare, and branch on compare -----------------------------------------
            KeyValuePair<string, Func<double, double, bool>>[] tests =
            {
                new KeyValuePair<string, Func<double, double, bool>>("lt", (a, b) => a < b),
                new KeyValuePair<string, Func<double, double, bool>>("gt", (a, b) => a > b),
                new KeyValuePair<string, Func<double, double, bool>>("le", (a, b) => a <= b),
                new KeyValuePair<string, Func<double, double, bool>>("ge", (a, b) => a >= b),
                new KeyValuePair<string, Func<double, double, bool>>("eq", (a, b) => a == b),
                new KeyValuePair<string, Func<double, double, bool>>("ne", (a, b) => a != b)
            };
            foreach (KeyValuePair<string, Func<double, double, bool>> pair in tests)
            {
                string x = pair.Key;
                Func<double, double, bool> test = pair.Value;
                Func<double, double, double> flag = (a, b) => test(a, b) ? 1.0 : 0.0;
                d["s" + x] = (c, t, l, _) =>
                {
                    Need(t, 4, l);
                    return c.Set2(l, t[1], t[2], t[3], flag);
                };
                d["s" + x + "z"] = (c, t, l, _) =>
                {
                    Need(t, 3, l);
                    return c.Set2(l, t[1], t[2], "0", flag);
                };
                d["b" + x] = (c, t, l, _) =>
                {
                    Need(t, 4, l);
                    return c.Branch2(l, t[1], t[2], t[3], test, false, false);
                };
                d["b" + x + "z"] = (c, t, l, _) =>
                {
                    Need(t, 3, l);
                    return c.Branch2(l, t[1], "0", t[2], test, false, false);
                };
                d["br" + x] = (c, t, l, _) =>
                {
                    Need(t, 4, l);
                    return c.Branch2(l, t[1], t[2], t[3], test, true, false);
                };
                d["br" + x + "z"] = (c, t, l, _) =>
                {
                    Need(t, 3, l);
                    return c.Branch2(l, t[1], "0", t[2], test, true, false);
                };
                d["b" + x + "al"] = (c, t, l, _) =>
                {
                    Need(t, 4, l);
                    return c.Branch2(l, t[1], t[2], t[3], test, false, true);
                };
                d["b" + x + "zal"] = (c, t, l, _) =>
                {
                    Need(t, 3, l);
                    return c.Branch2(l, t[1], "0", t[2], test, false, true);
                };
            }

            foreach (bool negate in new[] { false, true })
            {
                string x = negate ? "na" : "ap";
                bool inverted = negate;
                Func<double, double, double, double> flag = (a, b, tol) => Ic10Math.Approximately(a, b, tol) != inverted ? 1.0 : 0.0;
                d["s" + x] = (c, t, l, _) =>
                {
                    Need(t, 5, l);
                    return c.Set3(l, t[1], t[2], t[3], t[4], flag);
                };
                d["s" + x + "z"] = (c, t, l, _) =>
                {
                    Need(t, 4, l);
                    return c.Set3(l, t[1], t[2], "0", t[3], flag);
                };
                d["b" + x] = (c, t, l, _) =>
                {
                    Need(t, 5, l);
                    return c.BranchApprox(l, t[1], t[2], t[3], t[4], inverted, false, false);
                };
                d["b" + x + "z"] = (c, t, l, _) =>
                {
                    Need(t, 4, l);
                    return c.BranchApprox(l, t[1], "0", t[2], t[3], inverted, false, false);
                };
                d["br" + x] = (c, t, l, _) =>
                {
                    Need(t, 5, l);
                    return c.BranchApprox(l, t[1], t[2], t[3], t[4], inverted, true, false);
                };
                d["b" + x + "al"] = (c, t, l, _) =>
                {
                    Need(t, 5, l);
                    return c.BranchApprox(l, t[1], t[2], t[3], t[4], inverted, false, true);
                };
                // DIVERGENCE. In Stationeers brapz, brnaz, bapzal and
                // bnazal cannot be compiled: the parser demands three tokens and then reads a
                // fourth. Here they take the three operands the in-game help documents, the
                // same shape as bapz and bnaz.
                d["br" + x + "z"] = (c, t, l, _) =>
                {
                    Need(t, 4, l);
                    return c.BranchApprox(l, t[1], "0", t[2], t[3], inverted, true, false);
                };
                d["b" + x + "zal"] = (c, t, l, _) =>
                {
                    Need(t, 4, l);
                    return c.BranchApprox(l, t[1], "0", t[2], t[3], inverted, false, true);
                };
            }

            foreach (bool relative in new[] { false, true })
            {
                bool rel = relative;
                d[rel ? "brnan" : "bnan"] = (c, t, l, _) =>
                {
                    Need(t, 3, l);
                    Operand x = c.Num(l, t[1]);
                    return c.Branch(() => double.IsNaN(x.Number()), Operand.Line(c, l, t[2]), rel, false);
                };
            }

            // ---- jumps ------------------------------------------------------------------
            d["j"] = (c, t, l, _) =>
            {
                Need(t, 2, l);
                Operand jump = Operand.Line(c, l, t[1]);
                return i => Land(i, false, jump);
            };
            d["jr"] = (c, t, l, _) =>
            {
                Need(t, 2, l);
                Operand jump = Operand.Line(c, l, t[1]);
                return i => Land(i, true, jump);
            };
            d["jal"] = (c, t, l, _) =>
            {
                Need(t, 2, l);
                Operand jump = Operand.Line(c, l, t[1]);
                return i =>
                {
                    // ra is written before the target is read, so a bad target still moves ra.
                    c.Registers[ReturnAddressIndex] = i + 1;
                    return Land(i, false, jump);
                };
            };

            // ---- branch on a device -----------------------------------------------------
            d["bdse"] = (c, t, l, _) =>
            {
                Need(t, 3, l);
                return c.BranchDevice(l, t[1], t[2], true, false, false);
            };
            d["bdns"] = (c, t, l, _) =>
            {
                Need(t, 3, l);
                return c.BranchDevice(l, t[1], t[2], false, false, false);
            };
            d["brdse"] = (c, t, l, _) =>
            {
                Need(t, 3, l);
                return c.BranchDevice(l, t[1], t[2], true, true, false);
            };
            d["brdns"] = (c, t, l, _) =>
            {
                Need(t, 3, l);
                return c.BranchDevice(l, t[1], t[2], false, true, false);
            };
            d["bdseal"] = (c, t, l, _) =>
            {
                Need(t, 3, l);
                return c.BranchDevice(l, t[1], t[2], true, false, true);
            };
            d["bdnsal"] = (c, t, l, _) =>
            {
                Need(t, 3, l);
                return c.BranchDevice(l, t[1], t[2], false, false, true);
            };
            foreach (bool forWrite in new[] { false, true })
            {
                bool write = forWrite;
                // "branch if device not valid for load / store": jumps when the device cannot
                // read (bdnvl) or write (bdnvs) the logic type. A missing device is an error.
                d[write ? "bdnvs" : "bdnvl"] = (c, t, l, _) =>
                {
                    Need(t, 4, l);
                    Operand dev = Operand.Device(c, l, t[1]);
                    Operand lt = c.LogicTypeOf(l, t[2]);
                    Operand jump = Operand.Line(c, l, t[3]);
                    return i =>
                    {
                        int logicType = lt.Enum();
                        if (logicType == 0)
                        {
                            throw c.Error(Ic10ErrorType.LogicTypeIsNone, l);
                        }
                        IIc10Device device = dev.GetDevice();
                        if (device == null)
                        {
                            throw c.Error(Ic10ErrorType.DeviceNotFound, l);
                        }
                        if (write ? !device.CanLogicWrite(logicType) : !device.CanLogicRead(logicType))
                        {
                            return jump.Line();
                        }
                        return i + 1;
                    };
                };
            }

            // ---- device set / not set -----------------------------------------------------
            foreach (bool whenSet in new[] { true, false })
            {
                bool set = whenSet;
                d[set ? "sdse" : "sdns"] = (c, t, l, _) =>
                {
                    Need(t, 3, l);
                    Operand s = c.Dest(l, t[1]);
                    Operand dev = Operand.Device(c, l, t[2]);
                    return i =>
                    {
                        int target = s.Store();
                        c.Registers[target] = (dev.GetDevice() != null) == set ? 1.0 : 0.0;
                        return i + 1;
                    };
                };
            }

            // ---- device logic values ----------------------------------------------------
            d["l"] = (c, t, l, _) =>
            {
                Need(t, 4, l);
                Operand s = c.Dest(l, t[1]);
                Operand dev = Operand.Device(c, l, t[2]);
                Operand lt = c.LogicTypeOf(l, t[3]);
                return i =>
                {
                    int target = s.Store();
                    IIc10Device device = dev.GetDevice();
                    if (device == null)
                    {
                        throw c.Error(Ic10ErrorType.DeviceNotFound, l);
                    }
                    int logicType = lt.Enum();
                    if (logicType == 0)
                    {
                        throw c.Error(Ic10ErrorType.LogicTypeIsNone, l);
                    }
                    if (!device.CanLogicRead(logicType))
                    {
                        throw c.Error(Ic10ErrorType.IncorrectLogicType, l);
                    }
                    c.Registers[target] = device.GetLogicValue(logicType);
                    return i + 1;
                };
            };
            d["ld"] = (c, t, l, _) =>
            {
                Need(t, 4, l);
                Operand s = c.Dest(l, t[1]);
                Operand id = c.Whole(l, t[2]);
                Operand lt = c.LogicTypeOf(l, t[3]);
                return i =>
                {
                    int target = s.Store();
                    // No null check: a missing device is a null dereference, reported as
                    // Unknown. `sd` does check. Both as in Stationeers.
                    IIc10Device device = c.Host.GetDeviceFromId(id.Int(), int.MinValue);
                    int logicType = lt.Enum();
                    if (logicType == 0)
                    {
                        throw c.Error(Ic10ErrorType.LogicTypeIsNone, l);
                    }
                    if (!device.CanLogicRead(logicType))
                    {
                        throw c.Error(Ic10ErrorType.IncorrectLogicType, l);
                    }
                    c.Registers[target] = device.GetLogicValue(logicType);
                    return i + 1;
                };
            };
            d["s"] = (c, t, l, _) =>
            {
                Need(t, 4, l);
                Operand dev = Operand.Device(c, l, t[1]);
                Operand lt = c.LogicTypeOf(l, t[2]);
                Operand v = c.Num(l, t[3]);
                return i =>
                {
                    double wanted = v.Number();
                    int logicType = lt.Enum();
                    if (logicType == 0)
                    {
                        throw c.Error(Ic10ErrorType.LogicTypeIsNone, l);
                    }
                    IIc10Device device = dev.GetDevice();
                    if (device == null)
                    {
                        throw c.Error(Ic10ErrorType.DeviceNotFound, l);
                    }
                    // Read before the write check, and written only when it would change.
                    double current = device.GetLogicValue(logicType);
                    if (!device.CanLogicWrite(logicType))
                    {
                        throw c.Error(Ic10ErrorType.IncorrectLogicType, l);
                    }
                    if (current != wanted)
                    {
                        device.SetLogicValue(logicType, wanted);
                    }
                    return i + 1;
                };
            };
            d["sd"] = (c, t, l, _) =>
            {
                Need(t, 4, l);
                Operand id = c.Whole(l, t[1]);
                Operand lt = c.LogicTypeOf(l, t[2]);
                Operand v = c.Num(l, t[3]);
                return i =>
                {
                    int deviceId = id.Int();
                    double wanted = v.Number();
                    int logicType = lt.Enum();
                    IIc10Device device = c.Host.GetDeviceFromId(deviceId, int.MinValue);
                    if (device == null)
                    {
                        throw c.Error(Ic10ErrorType.DeviceNotFound, l);
                    }
                    double current = device.GetLogicValue(logicType);
                    if (!device.CanLogicWrite(logicType))
                    {
                        throw c.Error(Ic10ErrorType.IncorrectLogicType, l);
                    }
                    if (current != wanted)
                    {
                        device.SetLogicValue(logicType, wanted);
                    }
                    return i + 1;
                };
            };
            d["ls"] = (c, t, l, _) =>
            {
                Need(t, 5, l);
                Operand s = c.Dest(l, t[1]);
                Operand dev = Operand.Device(c, l, t[2]);
                Operand slot = c.Whole(l, t[3]);
                Operand lt = c.SlotTypeOf(l, t[4]);
                return i =>
                {
                    int target = s.Store();
                    int slotIndex = slot.Int();
                    IIc10Device device = dev.GetDevice();
                    if (device == null)
                    {
                        throw c.Error(Ic10ErrorType.DeviceNotFound, l);
                    }
                    int slotType = lt.Enum();
                    if (!device.CanSlotRead(slotType, slotIndex))
                    {
                        throw c.Error(Ic10ErrorType.IncorrectLogicType, l);
                    }
                    c.Registers[target] = device.GetSlotValue(slotType, slotIndex);
                    return i + 1;
                };
            };
            d["ss"] = (c, t, l, _) =>
            {
                Need(t, 5, l);
                Operand dev = Operand.Device(c, l, t[1]);
                Operand slot = c.Whole(l, t[2]);
                Operand lt = c.SlotTypeOf(l, t[3]);
                Operand v = c.Num(l, t[4]);
                return i =>
                {
                    double wanted = v.Number();
                    int slotType = lt.Enum();
                    int slotIndex = slot.Int();
                    IIc10Device device = dev.GetDevice();
                    if (device == null)
                    {
                        throw c.Error(Ic10ErrorType.DeviceNotFound, l);
                    }
                    IIc10SlotWriteable writeable = device as IIc10SlotWriteable;
                    if (writeable == null)
                    {
                        throw c.Error(Ic10ErrorType.DeviceNotSlotWriteable, l);
                    }
                    if (!writeable.CanSlotWrite(slotType, slotIndex))
                    {
                        throw c.Error(Ic10ErrorType.IncorrectLogicSlotType, l);
                    }
                    if (device.GetSlotValue(slotType, slotIndex) != wanted)
                    {
                        writeable.SetSlotValue(slotType, slotIndex, wanted);
                    }
                    return i + 1;
                };
            };
            d["lr"] = (c, t, l, _) =>
            {
                Need(t, 5, l);
                Operand s = c.Dest(l, t[1]);
                Operand dev = Operand.Device(c, l, t[2]);
                Operand mode = Operand.Enum(c, l, t[3], c.Enums.ReagentMode);
                Operand reagent = c.Whole(l, t[4]);
                return i =>
                {
                    int target = s.Store();
                    int reagentMode = mode.Enum();
                    IIc10Device device = dev.GetDevice();
                    if (device == null)
                    {
                        throw c.Error(Ic10ErrorType.DeviceNotFound, l);
                    }
                    IIc10ReagentContents contents = device as IIc10ReagentContents;
                    IIc10ReagentRequirer requirer = device as IIc10ReagentRequirer;
                    switch (reagentMode)
                    {
                        case ReagentContents:
                            if (contents == null)
                            {
                                throw c.Error(Ic10ErrorType.IncorrectReagentDevice, l);
                            }
                            c.Registers[target] = contents.GetReagentContents(reagent.Int());
                            break;
                        case ReagentRequired:
                            if (requirer == null)
                            {
                                throw c.Error(Ic10ErrorType.IncorrectReagentDevice, l);
                            }
                            c.Registers[target] = requirer.GetReagentRequired(reagent.Int());
                            break;
                        case ReagentRecipe:
                            if (requirer == null)
                            {
                                throw c.Error(Ic10ErrorType.IncorrectReagentDevice, l);
                            }
                            c.Registers[target] = requirer.GetReagentRecipe(reagent.Int());
                            break;
                        case ReagentTotalContents:
                            if (contents == null)
                            {
                                throw c.Error(Ic10ErrorType.IncorrectReagentDevice, l);
                            }
                            c.Registers[target] = contents.TotalReagents;
                            break;
                        default:
                            throw c.Error(Ic10ErrorType.UnhandledReagentMode, l);
                    }
                    return i + 1;
                };
            };
            d["rmap"] = (c, t, l, _) =>
            {
                Need(t, 4, l);
                Operand s = c.Dest(l, t[1]);
                Operand dev = Operand.DeviceIndex(c, l, t[2]);
                Operand reagent = c.Num(l, t[3]);
                return i =>
                {
                    int target = s.Store();
                    int socket = dev.Index(AliasTarget.Device);
                    double reagentHash = reagent.Number();
                    IIc10Device device = c.Host.GetDeviceFromIndex(socket, int.MinValue);
                    if (device == null)
                    {
                        throw c.Error(Ic10ErrorType.DeviceNotFound, l);
                    }
                    IIc10ReagentRequirer requirer = device as IIc10ReagentRequirer;
                    if (requirer == null)
                    {
                        // Stationeers reports MemoryNotReadable here, not a reagent error.
                        throw c.Error(Ic10ErrorType.MemoryNotReadable, l);
                    }
                    c.Registers[target] = Ic10Math.LongToDouble(requirer.GetPrefabHashFromReagentHash(Ic10Math.ToInt32(reagentHash)));
                    return i + 1;
                };
            };

            // ---- batch (every matching device on the data network) ------------------------
            d["lb"] = (c, t, l, _) =>
            {
                Need(t, 5, l);
                return c.BatchLoad(l, t[1], t[2], null, null, t[3], t[4]);
            };
            d["lbn"] = (c, t, l, _) =>
            {
                Need(t, 6, l);
                return c.BatchLoad(l, t[1], t[2], t[3], null, t[4], t[5]);
            };
            d["lbs"] = (c, t, l, _) =>
            {
                Need(t, 6, l);
                return c.BatchLoad(l, t[1], t[2], null, t[3], t[4], t[5]);
            };
            d["lbns"] = (c, t, l, _) =>
            {
                Need(t, 7, l);
                return c.BatchLoad(l, t[1], t[2], t[3], t[4], t[5], t[6]);
            };
            d["sb"] = (c, t, l, _) =>
            {
                Need(t, 4, l);
                return c.BatchStore(l, t[1], null, t[2], t[3]);
            };
            d["sbn"] = (c, t, l, _) =>
            {
                Need(t, 5, l);
                return c.BatchStore(l, t[1], t[2], t[3], t[4]);
            };
            d["sbs"] = (c, t, l, _) =>
            {
                Need(t, 5, l);
                Operand h = c.Whole(l, t[1]);
                Operand slot = c.Whole(l, t[2]);
                Operand lt = c.SlotTypeOf(l, t[3]);
                Operand v = c.Num(l, t[4]);
                return i =>
                {
                    int deviceHash = h.Int();
                    double wanted = v.Number();
                    int slotType = lt.Enum();
                    int slotIndex = slot.Int();
                    IList<IIc10Device> devices = c.Batch(l);
                    int n = devices.Count;
                    while (n-- > 0)
                    {
                        IIc10Device device = devices[n];
                        if (device == null || device.PrefabHash != deviceHash)
                        {
                            continue;
                        }
                        IIc10SlotWriteable writeable = device as IIc10SlotWriteable;
                        if (writeable == null)
                        {
                            throw c.Error(Ic10ErrorType.DeviceNotSlotWriteable, l);
                        }
                        if (!writeable.CanSlotWrite(slotType, slotIndex))
                        {
                            throw c.Error(Ic10ErrorType.IncorrectLogicSlotType, l);
                        }
                        if (writeable.GetSlotValue(slotType, slotIndex) != wanted)
                        {
                            writeable.SetSlotValue(slotType, slotIndex, wanted);
                        }
                    }
                    return i + 1;
                };
            };

            // ---- the stack, and other devices' stacks -------------------------------------
            d["push"] = (c, t, l, _) =>
            {
                Need(t, 2, l);
                Operand v = c.Num(l, t[1]);
                return i =>
                {
                    double value = v.Number();
                    c.Stack[c.StackSlot(l, 0)] = value;
                    c.Registers[StackPointerIndex] += 1.0;
                    return i + 1;
                };
            };
            d["pop"] = (c, t, l, _) =>
            {
                Need(t, 2, l);
                Operand s = c.Dest(l, t[1]);
                return i =>
                {
                    // sp moves first, and stays moved if the pop then fails: a chip stuck on
                    // an underflowing pop walks sp down by one every tick.
                    c.Registers[StackPointerIndex] -= 1.0;
                    int target = s.Store();
                    c.Registers[target] = c.Stack[c.StackSlot(l, 0)];
                    return i + 1;
                };
            };
            d["peek"] = (c, t, l, _) =>
            {
                Need(t, 2, l);
                Operand s = c.Dest(l, t[1]);
                return i =>
                {
                    int target = s.Store();
                    c.Registers[target] = c.Stack[c.StackSlot(l, -1)];
                    return i + 1;
                };
            };
            d["poke"] = (c, t, l, _) =>
            {
                Need(t, 3, l);
                Operand address = c.Whole(l, t[1]);
                Operand v = c.Num(l, t[2]);
                return i =>
                {
                    double value = v.Number();
                    int slot = address.Int();
                    if (slot < 0)
                    {
                        throw c.Error(Ic10ErrorType.StackUnderFlow, l);
                    }
                    if (slot >= c.Stack.Length)
                    {
                        throw c.Error(Ic10ErrorType.StackOverFlow, l);
                    }
                    c.Stack[slot] = value;
                    return i + 1;
                };
            };
            d["get"] = (c, t, l, _) =>
            {
                Need(t, 4, l);
                Operand s = c.Dest(l, t[1]);
                Operand dev = Operand.Device(c, l, t[2]);
                Operand address = c.Whole(l, t[3]);
                return i =>
                {
                    int target = s.Store();
                    int slot = address.Int();
                    IIc10Device device = dev.GetDevice();
                    if (device == null)
                    {
                        throw c.Error(Ic10ErrorType.DeviceNotFound, l);
                    }
                    IIc10MemoryReadable memory = device as IIc10MemoryReadable;
                    if (memory == null)
                    {
                        throw c.Error(Ic10ErrorType.MemoryNotReadable, l);
                    }
                    c.Registers[target] = memory.ReadMemory(slot);
                    return i + 1;
                };
            };
            d["getd"] = (c, t, l, _) =>
            {
                Need(t, 4, l);
                Operand s = c.Dest(l, t[1]);
                Operand id = c.Whole(l, t[2]);
                Operand address = c.Whole(l, t[3]);
                return i =>
                {
                    int target = s.Store();
                    int deviceId = id.Int();
                    int slot = address.Int();
                    IIc10Device device = c.Host.GetDeviceFromId(deviceId, int.MinValue);
                    if (device == null)
                    {
                        throw c.Error(Ic10ErrorType.DeviceNotFound, l);
                    }
                    IIc10MemoryReadable memory = device as IIc10MemoryReadable;
                    if (memory == null)
                    {
                        throw c.Error(Ic10ErrorType.MemoryNotReadable, l);
                    }
                    c.Registers[target] = memory.ReadMemory(slot);
                    return i + 1;
                };
            };
            d["put"] = (c, t, l, _) =>
            {
                Need(t, 4, l);
                Operand v = c.Num(l, t[3]);
                Operand dev = Operand.Device(c, l, t[1]);
                Operand address = c.Whole(l, t[2]);
                return i =>
                {
                    double value = v.Number();
                    int slot = address.Int();
                    IIc10Device device = dev.GetDevice();
                    if (device == null)
                    {
                        throw c.Error(Ic10ErrorType.DeviceNotFound, l);
                    }
                    IIc10MemoryWritable memory = device as IIc10MemoryWritable;
                    if (memory == null)
                    {
                        throw c.Error(Ic10ErrorType.MemoryNotWriteable, l);
                    }
                    c.WriteDeviceMemory(memory, slot, value, l);
                    return i + 1;
                };
            };
            d["putd"] = (c, t, l, _) =>
            {
                Need(t, 4, l);
                Operand v = c.Num(l, t[3]);
                Operand id = c.Whole(l, t[1]);
                Operand address = c.Whole(l, t[2]);
                return i =>
                {
                    double value = v.Number();
                    int deviceId = id.Int();
                    int slot = address.Int();
                    IIc10Device device = c.Host.GetDeviceFromId(deviceId, int.MinValue);
                    if (device == null)
                    {
                        throw c.Error(Ic10ErrorType.DeviceNotFound, l);
                    }
                    IIc10MemoryWritable memory = device as IIc10MemoryWritable;
                    if (memory == null)
                    {
                        throw c.Error(Ic10ErrorType.MemoryNotWriteable, l);
                    }
                    c.WriteDeviceMemory(memory, slot, value, l);
                    return i + 1;
                };
            };
            d["clr"] = (c, t, l, _) =>
            {
                Need(t, 2, l);
                Operand dev = Operand.DeviceIndex(c, l, t[1]);
                return i =>
                {
                    IIc10Device device = c.Host.GetDeviceFromIndex(dev.Index(AliasTarget.Device), int.MinValue);
                    if (device == null)
                    {
                        throw c.Error(Ic10ErrorType.DeviceNotFound, l);
                    }
                    IIc10MemoryWritable memory = device as IIc10MemoryWritable;
                    if (memory == null)
                    {
                        // MemoryNotReadable for a device that cannot be written: Stationeers'
                        // wording, where clrd says MemoryNotWriteable for the same thing.
                        throw c.Error(Ic10ErrorType.MemoryNotReadable, l);
                    }
                    memory.ClearMemory();
                    return i + 1;
                };
            };
            d["clrd"] = (c, t, l, _) =>
            {
                Need(t, 2, l);
                Operand id = c.Whole(l, t[1]);
                return i =>
                {
                    IIc10Device device = c.Host.GetDeviceFromId(id.Int(), int.MinValue);
                    if (device == null)
                    {
                        throw c.Error(Ic10ErrorType.DeviceNotFound, l);
                    }
                    IIc10MemoryWritable memory = device as IIc10MemoryWritable;
                    if (memory == null)
                    {
                        throw c.Error(Ic10ErrorType.MemoryNotWriteable, l);
                    }
                    memory.ClearMemory();
                    return i + 1;
                };
            };

            // ---- names ------------------------------------------------------------------
            d["alias"] = (c, t, l, _) =>
            {
                Need(t, 3, l);
                return c.CompileAlias(l, t[1], t[2]);
            };
            d["label"] = (c, t, l, _) =>
            {
                Need(t, 3, l);
                return c.CompileAlias(l, t[2], t[1]);
            };
            d["define"] = (c, t, l, _) =>
            {
                Need(t, 3, l);
                // Takes effect when the line compiles, not when it runs, and stores whatever
                // the text parsed to: a value that is not a number defines NaN without error.
                Operand v = Operand.Number(c, l, t[2], Include.MaskDefineValue);
                if (c.Defines.ContainsKey(t[1]))
                {
                    throw c.Error(Ic10ErrorType.ExtraDefine, l);
                }
                c.Defines.Add(t[1], v.Raw());
                return Noop;
            };

            // ---- time -------------------------------------------------------------------
            d["yield"] = (c, t, l, _) =>
            {
                Need(t, 1, l);
                return i => -i - 1;
            };
            d["sleep"] = (c, t, l, compiled) =>
            {
                Need(t, 2, l);
                Operand duration = c.Num(l, t[1]);
                SleepState state = new SleepState();
                compiled.Sleep = state;
                return i =>
                {
                    // Parks on its own line (-i, not -i - 1) and is run again every tick until
                    // the game clock has moved by the requested number of seconds.
                    if (double.IsNaN(state.Remaining))
                    {
                        state.LastTimeSet = c.Clock();
                        state.Remaining = duration.Number();
                        return -i;
                    }
                    float now = c.Clock();
                    state.Remaining -= now - state.LastTimeSet;
                    if (state.Remaining < 0.0)
                    {
                        state.LastTimeSet = 0f;
                        state.Remaining = double.NaN;
                        return i + 1;
                    }
                    state.LastTimeSet = now;
                    return -i;
                };
            };
            d["hcf"] = (c, t, l, _) =>
            {
                Need(t, 1, l);
                return i =>
                {
                    c.HaltAndCatchFire();
                    throw c.Error(Ic10ErrorType.ChipCatchingFire, l);
                };
            };

            return d;
        }

        private static void CheckField(Ic10Chip c, int line, int start, int length)
        {
            if (length <= 0 || start < 0)
            {
                throw c.Error(Ic10ErrorType.ShiftUnderflow, line);
            }
            if (start >= 53)
            {
                throw c.Error(Ic10ErrorType.ShiftOverflow, line);
            }
            if (length > 53 || start + length > 53)
            {
                throw c.Error(Ic10ErrorType.PayloadOverflow, line);
            }
        }

        private static void AddSet1(Dictionary<string, Builder> d, string name, Func<double, double> f)
        {
            d[name] = (c, t, l, _) =>
            {
                Need(t, 3, l);
                return c.Set1(l, t[1], t[2], f);
            };
        }

        private static void AddSet2(Dictionary<string, Builder> d, string name, Func<double, double, double> f)
        {
            d[name] = (c, t, l, _) =>
            {
                Need(t, 4, l);
                return c.Set2(l, t[1], t[2], t[3], f);
            };
        }

        private static void AddBits2(Dictionary<string, Builder> d, string name, Func<long, long, long> f)
        {
            d[name] = (c, t, l, _) =>
            {
                Need(t, 4, l);
                return c.Bits2(l, t[1], t[2], t[3], f);
            };
        }

        private static void AddShift(Dictionary<string, Builder> d, string name, bool unsigned, Func<long, int, long> f)
        {
            d[name] = (c, t, l, _) =>
            {
                Need(t, 4, l);
                return c.Shift(l, t[1], t[2], t[3], unsigned, f);
            };
        }
    }
}
