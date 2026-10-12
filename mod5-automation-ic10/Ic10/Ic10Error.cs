using System;

namespace Ic10
{
    // The error a chip reports, by the name and in the order Stationeers reports it
    // (ProgrammableChipException.ICExceptionType). The names are what a player reads on the
    // housing, so they are kept verbatim, including the spellings StackOverFlow and
    // InvalidProcessBinary.
    public enum Ic10ErrorType : byte
    {
        None,
        Unknown,
        IncorrectVariable,
        IncorrectVariableType,
        IncorrectLogicType,
        IncorrectLogicSlotType,
        IncorrectArgumentCount,
        DeviceNotSet,
        JumpTagDuplicate,
        UnrecognisedInstruction,
        IndexOutOfRange,
        OutOfRegisterBounds,
        OutOfDeviceBounds,
        IncorrectReagentMode,
        UnhandledReagentMode,
        IncorrectReagentDevice,
        ChipCatchingFire,
        StackOverFlow,
        StackUnderFlow,
        ExtraDefine,
        DeviceListNull,
        IncorrectReagentType,
        DeviceNotSlotWriteable,
        ShiftOverflow,
        ShiftUnderflow,
        DeviceNotFound,
        MemoryNotReadable,
        MemoryNotWriteable,
        InvalidStringLength,
        InvalidStringNull,
        InvalidStringNonAscii,
        InvalidPreprocessHash,
        InvalidProcessBinary,
        InvalidPreprocessHex,
        LogicTypeIsNone,
        PayloadOverflow,
        PayloadUnderflow,
        InvalidInteger,
        AliasNotFound
    }

    // An error the language itself defines. Anything else an instruction throws (an array
    // index, a null device) is caught by the chip and reported as Unknown, as in Stationeers.
    public sealed class Ic10Exception : Exception
    {
        public readonly Ic10ErrorType Type;

        // A ushort, as in Stationeers: a line number past 65535 wraps in the readout.
        public readonly ushort Line;

        public Ic10Exception(Ic10ErrorType type, int line)
            : base(type.ToString())
        {
            Type = type;
            Line = unchecked((ushort)line);
        }
    }

    // What a memory device throws for an address outside its stack. `put` and `putd` turn it
    // into StackUnderFlow / StackOverFlow; `get` and `getd` have no handler for it, so there it
    // surfaces as Unknown (ProgrammableChip._GET_Operation has no try block).
    public sealed class Ic10MemoryException : Exception
    {
        public readonly bool Overflow;

        public Ic10MemoryException(bool overflow)
            : base(overflow ? "stack overflow" : "stack underflow")
        {
            Overflow = overflow;
        }
    }
}
