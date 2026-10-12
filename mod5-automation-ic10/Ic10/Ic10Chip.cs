using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ic10
{
    // Everything a chip has to carry across a save: ProgrammableChipSaveData, minus the two
    // legacy alias formats Stationeers still reads for its own old saves.
    public sealed class Ic10ChipState
    {
        public string SourceCode;
        public double[] Registers;
        public int NextAddr;
        public string[] AliasKeys;
        public int[] AliasTargets;
        public int[] AliasIndices;
        public string[] JumpTagKeys;
        public int[] JumpTagValues;
        public string[] DefineKeys;
        public double[] DefineValues;

        // Trailing zeros are trimmed.
        public double[] Stack;

        // Only meaningful while the chip is parked on a `sleep`. NaN otherwise.
        public double SleepDurationRemaining = double.NaN;
    }

    // An IC10 chip: Stationeers' ProgrammableChip. It holds a program, 18 registers and a
    // 512-slot stack, and runs a bounded number of lines each time the housing ticks it.
    //
    // The language rules here follow Stationeers' IC10 closely (game build 0.2.6428.27798),
    // including the parts that read like accidents, so that its programs run unchanged. Where
    // this differs, the code says DIVERGENCE. See NOTICE.
    public sealed partial class Ic10Chip : IIc10MemoryReadable, IIc10MemoryWritable
    {
        public const int RegisterCount = 18;
        public const int StackPointerIndex = 16;
        public const int ReturnAddressIndex = 17;
        public const int StackSize = 512;

        // `db`: the housing itself.
        public const int BaseUnitIndex = int.MaxValue;

        internal sealed class SleepState
        {
            public float LastTimeSet;
            public double Remaining = double.NaN;
        }

        internal sealed class CompiledLine
        {
            public Func<int, int> Run;
            public SleepState Sleep;
        }

        private struct Constant
        {
            public string Literal;
            public double Value;
        }

        private static readonly Constant[] _constants =
        {
            new Constant { Literal = "nan", Value = double.NaN },
            new Constant { Literal = "pinf", Value = double.PositiveInfinity },
            new Constant { Literal = "ninf", Value = double.NegativeInfinity },
            new Constant { Literal = "pi", Value = Math.PI },
            new Constant { Literal = "tau", Value = Math.PI * 2.0 },
            new Constant { Literal = "deg2rad", Value = 0.01745329238474369 },
            new Constant { Literal = "rad2deg", Value = 57.295780181884766 },
            new Constant { Literal = "epsilon", Value = double.Epsilon },
            new Constant { Literal = "rgas", Value = 8.31446261815324 }
        };

        private static readonly Regex _comment = new Regex("#([^\\n]*)");
        private static readonly Regex _strings = new Regex("STR\\(\"([^\"]+)\"\\)");
        private static readonly Regex _hashes = new Regex("HASH\\(\"([^\"]+)\"\\)");
        private static readonly Regex _binary = new Regex("\\%([01_]+)");
        private static readonly Regex _hex = new Regex("\\$([0-9A-Fa-f_]+)+");

        internal readonly double[] Registers = new double[RegisterCount];
        internal readonly double[] Stack = new double[StackSize];
        internal readonly Dictionary<string, AliasValue> Aliases = new Dictionary<string, AliasValue>();
        internal readonly Dictionary<string, int> JumpTags = new Dictionary<string, int>();
        internal readonly Dictionary<string, double> Defines = new Dictionary<string, double>();

        private readonly List<CompiledLine> _lines = new List<CompiledLine>();
        private int _nextAddr;

        public readonly Ic10EnumSet Enums;

        // The housing the chip sits in. Null is a chip in nobody's slot: it still compiles,
        // but `sp`, `ra` and `db` are not defined for it and anything touching a device fails.
        public IIc10Host Host;

        // Game time in seconds, for `sleep`. Stationeers reads GameManager.GameTime.
        public Func<float> Clock = () => 0f;

        // `rand`. Stationeers shares one time-seeded System.Random across every chip.
        public Func<double> Random = new System.Random().NextDouble;

        public string SourceCode { get; private set; }
        public Ic10ErrorType ErrorType { get; private set; }
        public ushort ErrorLine { get; private set; }
        public Ic10ErrorType CompileErrorType { get; private set; }
        public ushort CompileErrorLine { get; private set; }
        public bool IsBurning { get; private set; }

        public Ic10Chip(Ic10EnumSet enums)
        {
            if (enums == null)
            {
                throw new ArgumentNullException("enums");
            }
            Enums = enums;
            SourceCode = string.Empty;
        }

        public bool CompilationError
        {
            get { return CompileErrorType != Ic10ErrorType.None; }
        }

        public int LineCount
        {
            get { return _lines.Count; }
        }

        // The line the chip runs next. Writing it is how a script jumps its own housing
        // (`s db LineNumber n`); the value is clamped to the program.
        public double LineNumber
        {
            get
            {
                return _nextAddr;
            }
            set
            {
                // (uint)double then Math.Clamp(long, 0, Count - 1) in Stationeers. An empty
                // program makes the clamp's bounds invalid there, which it swallows.
                if (_lines.Count == 0)
                {
                    return;
                }
                long target = unchecked((uint)value);
                long last = _lines.Count - 1;
                _nextAddr = (int)(target < 0 ? 0 : (target > last ? last : target));
            }
        }

        public double GetRegister(int index)
        {
            return Registers[index];
        }

        public void SetRegister(int index, double value)
        {
            Registers[index] = value;
        }

        internal static bool TryGetConstant(string code, out double value)
        {
            if (!string.IsNullOrEmpty(code))
            {
                for (int i = 0; i < _constants.Length; i++)
                {
                    if (code.Equals(_constants[i].Literal, StringComparison.OrdinalIgnoreCase))
                    {
                        value = _constants[i].Value;
                        return true;
                    }
                }
            }
            value = 0.0;
            return false;
        }

        // Recompiles from scratch. Registers other than sp, and the stack, keep their values:
        // Stationeers clears neither.
        public void SetSourceCode(string sourceCode)
        {
            _lines.Clear();
            Aliases.Clear();
            Defines.Clear();
            JumpTags.Clear();
            ErrorType = Ic10ErrorType.None;
            ErrorLine = 0;
            CompileErrorType = Ic10ErrorType.None;
            CompileErrorLine = 0;
            Registers[StackPointerIndex] = 0.0;
            if (string.IsNullOrEmpty(sourceCode))
            {
                sourceCode = string.Empty;
            }
            SourceCode = sourceCode;
            if (Host != null)
            {
                Host.RaiseError(0);
                CompileAlias(0, "db", "d" + BaseUnitIndex)(0);
                CompileAlias(0, "sp", "r" + StackPointerIndex)(0);
                CompileAlias(0, "ra", "r" + ReturnAddressIndex)(0);
            }
            string[] source = sourceCode.Split('\n');
            for (int i = 0; i < source.Length; i++)
            {
                try
                {
                    _lines.Add(CompileLine(source[i].IndexOf('#') == 0 ? string.Empty : source[i], i));
                }
                catch (Ic10Exception e)
                {
                    // The first bad line ends the compile; the lines after it do not exist.
                    if (Host != null)
                    {
                        Host.RaiseError(1);
                    }
                    CompileErrorLine = e.Line;
                    CompileErrorType = e.Type;
                    break;
                }
                catch (Exception)
                {
                    if (Host != null)
                    {
                        Host.RaiseError(1);
                    }
                    CompileErrorLine = unchecked((ushort)i);
                    CompileErrorType = Ic10ErrorType.Unknown;
                    break;
                }
            }
            _nextAddr = 0;
        }

        private static string ReplaceMatches(string line, Regex regex, Func<string, string> replacement)
        {
            // Matched on the line with any comment removed, then replaced by plain string
            // substitution, one match at a time. `$FF $FFFF` therefore turns the second number
            // into 255FF, which is what Stationeers does too.
            MatchCollection matches = regex.Matches(_comment.Replace(line, ""));
            for (int i = 0; i < matches.Count; i++)
            {
                line = line.Replace(matches[i].Groups[0].Value, replacement(matches[i].Groups[1].Value));
            }
            return line;
        }

        private CompiledLine CompileLine(string text, int line)
        {
            int comment = text.IndexOf('#');
            string code = comment < 0 ? text : text.Substring(0, comment);
            code = ReplaceMatches(code, _strings, s => Ic10Math.PackAscii6(s, line).ToString(CultureInfo.InvariantCulture));
            try
            {
                code = ReplaceMatches(code, _hashes, s => Ic10Math.Hash(s).ToString());
            }
            catch (Exception)
            {
                throw new Ic10Exception(Ic10ErrorType.InvalidPreprocessHash, line);
            }
            try
            {
                code = ReplaceMatches(code, _binary, s => Convert.ToInt64(s.Replace("_", ""), 2).ToString());
            }
            catch (Exception)
            {
                throw new Ic10Exception(Ic10ErrorType.InvalidProcessBinary, line);
            }
            try
            {
                code = ReplaceMatches(code, _hex, s => Convert.ToInt64(s.Replace("_", ""), 16).ToString());
            }
            catch (Exception)
            {
                throw new Ic10Exception(Ic10ErrorType.InvalidPreprocessHex, line);
            }
            string[] tokens = code.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            CompiledLine compiled = new CompiledLine();
            if (tokens.Length == 0)
            {
                compiled.Run = Noop;
            }
            else if (tokens.Length == 1 && tokens[0].Length >= 2 && tokens[0][tokens[0].Length - 1] == ':')
            {
                // A label is not an instruction of its own: the line it sits on is a no-op.
                compiled.Run = Noop;
                string tag = tokens[0].Substring(0, tokens[0].Length - 1);
                if (JumpTags.ContainsKey(tag))
                {
                    throw new Ic10Exception(Ic10ErrorType.JumpTagDuplicate, line);
                }
                JumpTags.Add(tag, line);
            }
            else
            {
                Builder builder;
                if (!_builders.TryGetValue(tokens[0], out builder))
                {
                    throw new Ic10Exception(Ic10ErrorType.UnrecognisedInstruction, line);
                }
                compiled.Run = builder(this, tokens, line, compiled);
            }
            return compiled;
        }

        private static int Noop(int index)
        {
            return index + 1;
        }

        // Runs up to runCount lines. An instruction returns the next line to run; a NEGATIVE
        // return means "stop for this tick and resume at the absolute value", which is how
        // `yield` and `sleep` give the tick back. Stationeers encodes it the same way, so the
        // side effects come along: `sleep` on line 0 returns -0 and does not end the tick, and
        // a relative jump to before line 0 lands on the mirrored line and ends it.
        public void Execute(int runCount)
        {
            if (_nextAddr < 0 || _nextAddr >= _lines.Count || _lines.Count == 0)
            {
                return;
            }
            int budget = runCount;
            while (budget-- > 0 && _nextAddr >= 0 && _nextAddr < _lines.Count)
            {
                int current = _nextAddr;
                try
                {
                    _nextAddr = _lines[_nextAddr].Run(_nextAddr);
                }
                catch (Ic10Exception e)
                {
                    // The chip stays on the failing line and tries it again next tick.
                    if (Host != null)
                    {
                        Host.RaiseError(1);
                    }
                    ErrorLine = e.Line;
                    ErrorType = e.Type;
                    _nextAddr = current;
                    break;
                }
                catch (Exception)
                {
                    if (Host != null)
                    {
                        Host.RaiseError(1);
                    }
                    ErrorLine = unchecked((ushort)current);
                    ErrorType = Ic10ErrorType.Unknown;
                    _nextAddr = current;
                    break;
                }
                if (Host != null)
                {
                    ErrorLine = 0;
                    ErrorType = Ic10ErrorType.None;
                    Host.RaiseError(0);
                }
                if (_nextAddr < 0)
                {
                    _nextAddr = -_nextAddr;
                    break;
                }
            }
        }

        // What Stationeers does when the chip enters or leaves a housing.
        public void Reset()
        {
            _nextAddr = 0;
            Registers[StackPointerIndex] = 0.0;
            ErrorType = Ic10ErrorType.None;
            ErrorLine = 0;
        }

        private void HaltAndCatchFire()
        {
            IsBurning = true;
            if (Host != null)
            {
                Host.HaltAndCatchFire();
            }
        }

        public double ReadMemory(int address)
        {
            if (address < 0)
            {
                throw new Ic10MemoryException(false);
            }
            if (address >= Stack.Length)
            {
                throw new Ic10MemoryException(true);
            }
            return Stack[address];
        }

        public void WriteMemory(int address, double value)
        {
            if (address < 0)
            {
                throw new Ic10MemoryException(false);
            }
            if (address >= Stack.Length)
            {
                throw new Ic10MemoryException(true);
            }
            Stack[address] = value;
        }

        public void ClearMemory()
        {
            for (int i = 0; i < Stack.Length; i++)
            {
                Stack[i] = 0.0;
            }
        }

        public Ic10ChipState Save()
        {
            Ic10ChipState state = new Ic10ChipState();
            state.SourceCode = SourceCode;
            state.Registers = (double[])Registers.Clone();
            state.NextAddr = _nextAddr;
            state.AliasKeys = new string[Aliases.Count];
            state.AliasTargets = new int[Aliases.Count];
            state.AliasIndices = new int[Aliases.Count];
            int n = 0;
            foreach (KeyValuePair<string, AliasValue> alias in Aliases)
            {
                state.AliasKeys[n] = alias.Key;
                state.AliasTargets[n] = (int)alias.Value.Target;
                state.AliasIndices[n] = alias.Value.Index;
                n++;
            }
            state.JumpTagKeys = new string[JumpTags.Count];
            state.JumpTagValues = new int[JumpTags.Count];
            n = 0;
            foreach (KeyValuePair<string, int> tag in JumpTags)
            {
                state.JumpTagKeys[n] = tag.Key;
                state.JumpTagValues[n] = tag.Value;
                n++;
            }
            state.DefineKeys = new string[Defines.Count];
            state.DefineValues = new double[Defines.Count];
            n = 0;
            foreach (KeyValuePair<string, double> define in Defines)
            {
                state.DefineKeys[n] = define.Key;
                state.DefineValues[n] = define.Value;
                n++;
            }
            int used = Stack.Length;
            while (used > 0 && Stack[used - 1] == 0.0)
            {
                used--;
            }
            state.Stack = new double[used];
            Array.Copy(Stack, state.Stack, used);
            SleepState sleep = CurrentSleep();
            if (sleep != null)
            {
                state.SleepDurationRemaining = sleep.Remaining;
            }
            return state;
        }

        // Recompiles the saved source, then lays the saved run state over it. The aliases are
        // restored rather than re-derived because they are made by running `alias` lines, and
        // a chip saved mid-program has run only some of them.
        public void Load(Ic10ChipState state)
        {
            SetSourceCode(state.SourceCode);
            if (state.Registers != null)
            {
                for (int i = 0; i < state.Registers.Length; i++)
                {
                    Registers[i] = state.Registers[i];
                }
            }
            _nextAddr = state.NextAddr;
            Aliases.Clear();
            if (state.AliasKeys != null)
            {
                for (int i = 0; i < state.AliasKeys.Length; i++)
                {
                    Aliases.Add(state.AliasKeys[i], new AliasValue((AliasTarget)state.AliasTargets[i], state.AliasIndices[i]));
                }
            }
            JumpTags.Clear();
            if (state.JumpTagKeys != null)
            {
                for (int i = 0; i < state.JumpTagKeys.Length; i++)
                {
                    JumpTags.Add(state.JumpTagKeys[i], state.JumpTagValues[i]);
                }
            }
            if (state.Stack != null)
            {
                for (int i = 0; i < Stack.Length && i < state.Stack.Length; i++)
                {
                    Stack[i] = state.Stack[i];
                }
            }
            Defines.Clear();
            if (state.DefineKeys != null)
            {
                for (int i = 0; i < state.DefineKeys.Length; i++)
                {
                    Defines.Add(state.DefineKeys[i], state.DefineValues[i]);
                }
            }
            SleepState sleep = CurrentSleep();
            if (sleep != null)
            {
                sleep.LastTimeSet = Clock();
                sleep.Remaining = state.SleepDurationRemaining;
            }
        }

        private SleepState CurrentSleep()
        {
            if (_nextAddr >= 0 && _nextAddr < _lines.Count)
            {
                return _lines[_nextAddr].Sleep;
            }
            return null;
        }
    }
}
