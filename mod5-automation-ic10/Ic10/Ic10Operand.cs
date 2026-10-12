using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ic10
{
    internal enum AliasTarget
    {
        None = 0,
        Register = 1,
        Device = 2
    }

    internal struct AliasValue
    {
        public readonly AliasTarget Target;
        public readonly int Index;

        public AliasValue(AliasTarget target, int index)
        {
            Target = target;
            Index = index;
        }
    }

    // One operand of one instruction. What can be decided from the text alone (is it a
    // register, a number, an enum name) is decided when the line compiles; aliases, defines,
    // jump tags and register contents are looked up each time the instruction runs.
    //
    // Stationeers has a class per operand kind (ProgrammableChip._Operation.Variable and its
    // subclasses). The kinds differ in the ORDER they try the readings, and in whether a
    // register value is rounded or truncated, so each kind keeps its own reader here:
    //
    //   Number()   define, alias, jump tag, register, literal          DoubleValueVariable
    //   Int()      alias, define, jump tag, register (rounded), literal  IntValuedVariable
    //   Line()     the same order as Int()                             LineNumberVariable
    //   Enum()     alias, define, jump tag, register (truncated), name EnumValuedVariable<T>
    //   Store()    alias, register: an index, never a value            IndexVariable
    internal sealed class Operand
    {
        private enum DeviceKind
        {
            None,
            Direct,
            Alias,
            Index
        }

        private readonly Ic10Chip _chip;
        private readonly int _line;
        private readonly int _flags;

        private readonly int _registerIndex = -1;
        private readonly int _registerRecurse = -1;
        private readonly string _alias;

        private bool _intSet;
        private int _intValue = -1;

        private double _number = double.NaN;

        private bool _lineSet;
        private int _lineValue;

        private Ic10EnumTable _enumTable;
        private bool _enumSet;
        private int _enumValue = -1;

        private DeviceKind _deviceKind;
        private int _deviceIndex = -1;
        private int _deviceRecurse = -1;
        private int _deviceNetwork = int.MinValue;

        private static readonly Regex _plainRegister = new Regex("^r+(?:1[0-5]|[0-9])$");
        private static readonly Regex _socket = new Regex("^(d[0-9]|dr*[r0-9][0-9])$");

        private Operand(Ic10Chip chip, int line, string code, int flags)
        {
            _chip = chip;
            _line = line;
            _flags = flags;
            if ((flags & Include.RegisterIndex) != 0)
            {
                _registerIndex = ParseIndex(code, 'r', out _registerRecurse);
            }
            if ((flags & (Include.Alias | Include.JumpTag)) != 0)
            {
                _alias = BeforeColon(code);
            }
        }

        private static string BeforeColon(string code)
        {
            int colon = code.IndexOf(':');
            return colon < 0 ? code : code.Substring(0, colon);
        }

        // `rr5`: the number after a run of the same letter, and how long the run was. A token
        // that is nothing but that letter runs off its end, as in Stationeers, where the
        // resulting IndexOutOfRangeException fails the compile with Unknown.
        private static int ParseIndex(string code, char letter, out int recurse)
        {
            int i = 0;
            while (code[i] == letter)
            {
                i++;
            }
            if (i == 0)
            {
                recurse = -1;
                return -1;
            }
            int result;
            if (!ParseInt(BeforeColon(code.Substring(i)), out result))
            {
                recurse = -1;
                return -1;
            }
            recurse = i;
            return result;
        }

        // Integers are read in the current culture, as Stationeers reads them
        // (int.TryParse with no format provider). Decimals are not: see Number().
        private static bool ParseInt(string code, out int value)
        {
            return int.TryParse(code, out value);
        }

        private void InitInt(string code)
        {
            int resolved;
            if (_chip.Enums.TryResolve(code, _flags, out resolved))
            {
                _intValue = resolved;
                _intSet = true;
            }
            if (_intSet || (_flags & Include.Value) == 0)
            {
                return;
            }
            double constant;
            if (Ic10Chip.TryGetConstant(code, out constant))
            {
                // Stationeers stores the constant and returns without marking the value as
                // set, so a constant never works where an integer is expected.
                _intValue = Ic10Math.ToInt32(constant);
                return;
            }
            if (ParseInt(code, out _intValue))
            {
                _intSet = true;
            }
        }

        public static Operand Number(Ic10Chip chip, int line, string code, int flags = Include.MaskDoubleValue)
        {
            Operand operand = new Operand(chip, line, code, flags);
            int resolved;
            if (chip.Enums.TryResolve(code, flags, out resolved))
            {
                operand._number = resolved;
                return operand;
            }
            if ((flags & Include.Value) == 0)
            {
                return operand;
            }
            double constant;
            if (Ic10Chip.TryGetConstant(code, out constant))
            {
                operand._number = constant;
                return operand;
            }
            double parsed;
            if (double.TryParse(code, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out parsed))
            {
                operand._number = parsed;
            }
            return operand;
        }

        public static Operand Int(Ic10Chip chip, int line, string code, int flags = Include.MaskDoubleValue)
        {
            Operand operand = new Operand(chip, line, code, flags);
            operand.InitInt(code);
            return operand;
        }

        public static Operand Store(Ic10Chip chip, int line, string code, int flags = Include.MaskStoreIndex)
        {
            return Int(chip, line, code, flags);
        }

        public static Operand Line(Ic10Chip chip, int line, string code)
        {
            Operand operand = new Operand(chip, line, code, Include.MaskDoubleValue);
            operand._lineSet = ParseInt(code, out operand._lineValue);
            return operand;
        }

        public static Operand Enum(Ic10Chip chip, int line, string code, Ic10EnumTable table)
        {
            Operand operand = new Operand(chip, line, code, Include.MaskDoubleValue);
            operand._enumTable = table;
            int value;
            if (table.TryGet(code, out value))
            {
                operand._enumValue = value;
                operand._enumSet = true;
                return operand;
            }
            double constant;
            if (Ic10Chip.TryGetConstant(code, out constant))
            {
                operand._enumValue = Ic10Math.ToInt32(constant);
                operand._enumSet = true;
                return operand;
            }
            if (ParseInt(code, out operand._enumValue))
            {
                operand._enumSet = true;
            }
            return operand;
        }

        // A device socket named outright: `d0`, `dr3`, `db`, with an optional `:network`.
        public static Operand DeviceIndex(Ic10Chip chip, int line, string code, int flags = Include.MaskDeviceIndex)
        {
            Operand operand = new Operand(chip, line, code, flags);
            operand.InitInt(code);
            operand._deviceKind = DeviceKind.Index;
            if ((flags & Include.DeviceIndex) != 0)
            {
                operand._deviceIndex = ParseDeviceIndex(code, out operand._deviceRecurse);
            }
            if ((flags & Include.NetworkIndex) != 0)
            {
                operand._deviceNetwork = ParseNetworkIndex(code);
            }
            return operand;
        }

        // The device operand of l, s, ls, get and the rest: ProgrammableChip._MakeDeviceVariable.
        // A register or a number is a reference id; d? is a socket; anything else is an alias,
        // which may turn out to be either.
        public static Operand Device(Ic10Chip chip, int line, string code)
        {
            const int direct = Include.MaskDoubleValue | Include.DeviceIndex | Include.NetworkIndex;
            if (_plainRegister.IsMatch(code))
            {
                return MakeDirect(chip, line, code, Include.MaskDoubleValue | Include.DeviceIndex);
            }
            if (chip.Defines.ContainsKey(code))
            {
                return MakeDirect(chip, line, code, Include.MaskDoubleValue);
            }
            if (code.Length > 0 && (code[0] == '$' || code[0] == '%' || char.IsDigit(code[0])))
            {
                return MakeDirect(chip, line, code, direct);
            }
            if (code.Length > 1 && code[0] == 'r' && char.IsDigit(code[1]))
            {
                return MakeDirect(chip, line, code, direct);
            }
            string head = BeforeColon(code);
            if (head.StartsWith("d", StringComparison.Ordinal) && (head == "db" || _socket.IsMatch(head)))
            {
                return DeviceIndex(chip, line, code);
            }
            Operand alias = new Operand(chip, line, code, direct);
            alias.InitInt(code);
            alias._deviceKind = DeviceKind.Alias;
            alias._deviceNetwork = ParseNetworkIndex(code);
            return alias;
        }

        private static Operand MakeDirect(Ic10Chip chip, int line, string code, int flags)
        {
            Operand operand = new Operand(chip, line, code, flags);
            operand.InitInt(code);
            operand._deviceKind = DeviceKind.Direct;
            if ((flags & Include.NetworkIndex) != 0)
            {
                operand._deviceNetwork = ParseNetworkIndex(code);
            }
            return operand;
        }

        private static int ParseDeviceIndex(string code, out int recurse)
        {
            if (code.StartsWith("db", StringComparison.Ordinal))
            {
                recurse = 0;
                return int.MaxValue;
            }
            if (code.Length >= 2 && code[1] == 'r')
            {
                return ParseIndex(code.Substring(1), 'r', out recurse);
            }
            int ignored;
            recurse = 0;
            return ParseIndex(code, 'd', out ignored);
        }

        private static int ParseNetworkIndex(string code)
        {
            int colon = code.IndexOf(':');
            if (colon < 0)
            {
                return int.MinValue;
            }
            int result;
            return ParseInt(code.Substring(colon + 1), out result) ? result : int.MinValue;
        }

        private Ic10Exception Error(Ic10ErrorType type)
        {
            return new Ic10Exception(type, _line);
        }

        private bool TryDefine(out double value)
        {
            // A null key throws, as Dictionary.TryGetValue does in Stationeers. No instruction
            // builds an operand that reaches here without an alias.
            return _chip.Defines.TryGetValue(_alias, out value);
        }

        private bool TryJumpTag(out int value)
        {
            if (_alias == null)
            {
                value = 0;
                return false;
            }
            return _chip.JumpTags.TryGetValue(_alias, out value);
        }

        private bool TryAliasAsIndex(AliasTarget type, out int index)
        {
            AliasValue alias;
            if (_alias == null || !_chip.Aliases.TryGetValue(_alias, out alias) || alias.Target != type)
            {
                index = -1;
                return false;
            }
            index = alias.Index;
            return true;
        }

        private bool TryAliasAsValue(out double value, bool throwError)
        {
            AliasValue alias;
            if (_alias == null || !_chip.Aliases.TryGetValue(_alias, out alias) || alias.Target != AliasTarget.Register)
            {
                value = double.NaN;
                return false;
            }
            if (alias.Index < 0 || alias.Index >= _chip.Registers.Length)
            {
                if (throwError)
                {
                    throw Error(Ic10ErrorType.OutOfRegisterBounds);
                }
                value = double.NaN;
                return false;
            }
            value = _chip.Registers[alias.Index];
            return true;
        }

        // Follows `rr5` down to the register it finally names. Each step but the last is
        // bounds-checked; the last is not, so `r99` gets as far as the caller.
        private bool TryRegisterAsIndex(out int index, bool throwError)
        {
            if (_registerIndex < 0 || _registerRecurse < 0)
            {
                index = -1;
                return false;
            }
            int current = _registerIndex;
            int recurse = _registerRecurse;
            while (recurse-- > 1)
            {
                current = Ic10Math.ToInt32(Math.Round(_chip.Registers[current]));
                if (current < 0 || current >= _chip.Registers.Length)
                {
                    if (throwError)
                    {
                        throw Error(Ic10ErrorType.OutOfRegisterBounds);
                    }
                    index = -1;
                    return false;
                }
            }
            index = current;
            return true;
        }

        private bool TryRegisterAsValue(out double value, bool throwError)
        {
            int index;
            if (!TryRegisterAsIndex(out index, throwError))
            {
                value = double.NaN;
                return false;
            }
            // Unchecked on purpose: `r99` as a value indexes past the registers, which
            // Stationeers reports as Unknown rather than OutOfRegisterBounds.
            value = _chip.Registers[index];
            return true;
        }

        private bool TrySocketAsIndex(out int index, bool throwError)
        {
            if (_deviceIndex < 0 || _deviceRecurse < 0)
            {
                index = -1;
                return false;
            }
            int current = _deviceIndex;
            int recurse = _deviceRecurse;
            while (recurse-- > 0)
            {
                current = Ic10Math.ToInt32(Math.Round(_chip.Registers[current]));
                if (current < 0 || current >= _chip.Registers.Length)
                {
                    if (throwError)
                    {
                        throw Error(Ic10ErrorType.OutOfDeviceBounds);
                    }
                    index = -1;
                    return false;
                }
            }
            index = current;
            return true;
        }

        // IndexVariable.GetVariableIndex, and DeviceIndexVariable's override of it.
        public int Index(AliasTarget type, bool throwError = true)
        {
            int index;
            if (_deviceKind == DeviceKind.Index)
            {
                if ((_flags & Include.Alias) != 0 && TryAliasAsIndex(type, out index))
                {
                    return index;
                }
                if ((_flags & Include.DeviceIndex) != 0 && TrySocketAsIndex(out index, throwError))
                {
                    return index;
                }
                if ((_flags & Include.NetworkIndex) != 0)
                {
                    return _deviceNetwork;
                }
                if (throwError)
                {
                    throw Error(Ic10ErrorType.IncorrectVariable);
                }
                return 0;
            }
            int result = 0;
            double defined;
            if ((_flags & Include.Define) != 0 && TryDefine(out defined))
            {
                result = Ic10Math.ToInt32(defined);
            }
            else if ((_flags & Include.Alias) != 0 && TryAliasAsIndex(type, out index))
            {
                result = index;
            }
            else if ((_flags & Include.JumpTag) != 0 && TryJumpTag(out index))
            {
                result = index;
            }
            else if ((_flags & Include.RegisterIndex) != 0 && TryRegisterAsIndex(out index, throwError))
            {
                result = index;
            }
            else if (throwError)
            {
                throw Error(Ic10ErrorType.IncorrectVariable);
            }
            if (throwError)
            {
                if ((type & AliasTarget.Register) != 0 && (result < 0 || result >= _chip.Registers.Length))
                {
                    throw Error(Ic10ErrorType.OutOfRegisterBounds);
                }
                if ((type & AliasTarget.Device) != 0 && !_chip.Host.IsValidDeviceIndex(result))
                {
                    throw Error(Ic10ErrorType.OutOfDeviceBounds);
                }
            }
            return result;
        }

        // The register an instruction writes its result to.
        public int Store()
        {
            return Index(AliasTarget.Register);
        }

        // The compile-time value, with no lookup at all: what `define` stores.
        public double Raw()
        {
            return _number;
        }

        public double Number()
        {
            double value;
            int tag;
            if ((_flags & Include.Define) != 0 && TryDefine(out value))
            {
                return value;
            }
            if ((_flags & Include.Alias) != 0 && TryAliasAsValue(out value, true))
            {
                return value;
            }
            if ((_flags & Include.JumpTag) != 0 && TryJumpTag(out tag))
            {
                return tag;
            }
            if ((_flags & Include.RegisterIndex) != 0 && TryRegisterAsValue(out value, true))
            {
                return value;
            }
            // A literal that is NaN counts as "no literal". That includes the `nan` constant:
            // Stationeers has a flag meant to let it through (_qNaN) which is never set.
            if ((_flags & Include.AnyValue) != 0 && !double.IsNaN(_number))
            {
                return _number;
            }
            throw Error(Ic10ErrorType.IncorrectVariable);
        }

        // DoubleValueVariable.GetVariableLong: the operand of a bitwise instruction.
        public long Long(bool signed = true)
        {
            double value = Number();
            if (value < -9.223372036854776E+18)
            {
                throw Error(Ic10ErrorType.ShiftUnderflow);
            }
            if (value > 9.223372036854776E+18)
            {
                throw Error(Ic10ErrorType.ShiftOverflow);
            }
            return Ic10Math.DoubleToLong(value, signed);
        }

        // DoubleValueVariable.GetVariableInt: a shift count or bit position, truncated.
        public int Int32()
        {
            double value = Number();
            if (value < -2147483648.0)
            {
                throw Error(Ic10ErrorType.ShiftUnderflow);
            }
            if (value > 2147483647.0)
            {
                throw Error(Ic10ErrorType.ShiftOverflow);
            }
            return Ic10Math.ToInt32(value);
        }

        // IntValuedVariable.GetVariableValue: a hash, a slot, an address, a reference id.
        public int Int(bool throwError = true)
        {
            double value;
            int tag;
            if (TryAliasAsValue(out value, throwError))
            {
                return Ic10Math.ToInt32(value);
            }
            if ((_flags & Include.Define) != 0 && TryDefine(out value))
            {
                return Ic10Math.ToInt32(value);
            }
            if ((_flags & Include.JumpTag) != 0 && TryJumpTag(out tag))
            {
                return tag;
            }
            if ((_flags & Include.RegisterIndex) != 0 && TryRegisterAsValue(out value, throwError))
            {
                return Ic10Math.ToInt32(Math.Round(value));
            }
            if ((_flags & Include.AnyValue) != 0 && _intSet)
            {
                return _intValue;
            }
            if (throwError)
            {
                throw Error(Ic10ErrorType.InvalidInteger);
            }
            return 0;
        }

        // LineNumberVariable.GetVariableValue: a jump target.
        public int Line()
        {
            double value;
            int tag;
            if (TryAliasAsValue(out value, true))
            {
                return Ic10Math.ToInt32(value);
            }
            if ((_flags & Include.Define) != 0 && TryDefine(out value))
            {
                return Ic10Math.ToInt32(value);
            }
            if ((_flags & Include.JumpTag) != 0 && TryJumpTag(out tag))
            {
                return tag;
            }
            if ((_flags & Include.RegisterIndex) != 0 && TryRegisterAsValue(out value, true))
            {
                return Ic10Math.ToInt32(Math.Round(value));
            }
            if ((_flags & Include.AnyValue) != 0 && _lineSet)
            {
                return _lineValue;
            }
            throw Error(Ic10ErrorType.IncorrectVariable);
        }

        // EnumValuedVariable<T>.GetVariableValue: a logic type, slot type or mode.
        public int Enum()
        {
            double value;
            int tag;
            if (TryAliasAsValue(out value, true))
            {
                return _enumTable.Apply(Ic10Math.ToInt32(value));
            }
            if ((_flags & Include.Define) != 0 && TryDefine(out value))
            {
                return _enumTable.Apply(Ic10Math.ToInt32(value));
            }
            if ((_flags & Include.JumpTag) != 0 && TryJumpTag(out tag))
            {
                return _enumTable.Apply(tag);
            }
            if ((_flags & Include.RegisterIndex) != 0 && TryRegisterAsValue(out value, true))
            {
                return _enumTable.Apply(Ic10Math.ToInt32(value));
            }
            if ((_flags & Include.Value) != 0 && _enumSet)
            {
                return _enumTable.Apply(_enumValue);
            }
            throw Error(Ic10ErrorType.IncorrectVariable);
        }

        // IDeviceVariable.GetDevice. Null means no device there, which each instruction
        // reports (or branches on) in its own way.
        public IIc10Device GetDevice()
        {
            IIc10Host host = _chip.Host;
            switch (_deviceKind)
            {
                case DeviceKind.Direct:
                    return host.GetDeviceFromId(Int(), _deviceNetwork);
                case DeviceKind.Index:
                    return host.GetDeviceFromIndex(Index(AliasTarget.Device, false), _deviceNetwork);
                default:
                    if (string.IsNullOrEmpty(_alias))
                    {
                        return host.GetDeviceFromIndex(Index(AliasTarget.Device, false), _deviceNetwork);
                    }
                    AliasValue alias;
                    if (!_chip.Aliases.TryGetValue(_alias, out alias))
                    {
                        throw Error(Ic10ErrorType.IncorrectVariable);
                    }
                    switch (alias.Target)
                    {
                        case AliasTarget.Device:
                            return host.GetDeviceFromIndex(Index(AliasTarget.Device, false), _deviceNetwork);
                        case AliasTarget.Register:
                            return host.GetDeviceFromId(Int(false), _deviceNetwork);
                        default:
                            throw Error(Ic10ErrorType.AliasNotFound);
                    }
            }
        }
    }
}
