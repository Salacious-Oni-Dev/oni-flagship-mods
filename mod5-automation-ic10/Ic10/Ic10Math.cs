using System;
using System.Text;

namespace Ic10
{
    // The arithmetic the chip leans on, gathered in one place.
    //
    // A few corners of it are left to the runtime ON PURPOSE. A double that is NaN or out of
    // range has no defined integer value in C#, and Math.Max / Math.Min order -0 and +0
    // differently on old and new runtimes. Stationeers takes whatever Unity's Mono gives; in
    // the game this library runs on Unity's Mono too, so plain casts and plain Math calls
    // inherit Stationeers' answer instead of a guess at it. The offline tests run on modern
    // .NET, which answers those corners differently, so the tests do not assert them.
    public static class Ic10Math
    {
        // (int)double: truncates toward zero. NaN and out of range: the runtime's answer.
        public static int ToInt32(double d)
        {
            return unchecked((int)d);
        }

        // (long)double, same rule.
        public static long ToInt64(double d)
        {
            return unchecked((long)d);
        }

        public static double Max(double a, double b)
        {
            return Math.Max(a, b);
        }

        public static double Min(double a, double b)
        {
            return Math.Min(a, b);
        }

        // RocketMath.Lerp: t is clamped to [0, 1].
        public static double Lerp(double a, double b, double t)
        {
            double clamped = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
            return a + (b - a) * clamped;
        }

        // The tolerance test behind sap, sna, bap, bna and their variants.
        public static bool Approximately(double a, double b, double c)
        {
            return Math.Abs(a - b) <= Max(c * Max(Math.Abs(a), Math.Abs(b)), 1.1210387714598537E-44);
        }

        // ProgrammableChip.DoubleToLong: the integer a bitwise instruction sees. 53 bits of
        // magnitude survive; an unsigned read keeps the low 54 bits of the two's complement.
        public static long DoubleToLong(double d, bool signed)
        {
            long value = ToInt64(d % 9007199254740992.0);
            if (!signed)
            {
                value &= 0x3FFFFFFFFFFFFFL;
            }
            return value;
        }

        // ProgrammableChip.LongToDouble: bit 53 is the sign, the low 53 bits the value.
        public static double LongToDouble(long l)
        {
            bool negative = (l & 0x20000000000000L) != 0;
            l &= 0x1FFFFFFFFFFFFFL;
            if (negative)
            {
                l |= -9007199254740992L;
            }
            return l;
        }

        // STR("..."): up to six ASCII characters packed big-endian, eight bits each.
        public static double PackAscii6(string text, int line)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new Ic10Exception(Ic10ErrorType.InvalidStringNull, line);
            }
            if (text.Length > 6)
            {
                throw new Ic10Exception(Ic10ErrorType.InvalidStringLength, line);
            }
            long packed = 0L;
            foreach (char c in text)
            {
                if (c > '\u007f')
                {
                    throw new Ic10Exception(Ic10ErrorType.InvalidStringNonAscii, line);
                }
                packed = (packed << 8) | (byte)c;
            }
            return packed;
        }

        public static string UnpackAscii6(double packed, bool signed)
        {
            ulong value = (ulong)DoubleToLong(packed, signed);
            int length = 0;
            ulong rest = value;
            while (rest != 0L && length < 8)
            {
                length++;
                rest >>= 8;
            }
            char[] chars = new char[length];
            for (int i = length - 1; i >= 0; i--)
            {
                chars[i] = (char)(value & 0xFF);
                value >>= 8;
            }
            return new string(chars);
        }

        private static readonly uint[] _crcTable = MakeCrcTable();

        private static uint[] MakeCrcTable()
        {
            uint[] table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }
                table[n] = c;
            }
            return table;
        }

        // HASH("..."): Unity's Animator.StringToHash, which is CRC-32 (the zlib polynomial)
        // read as a signed int. Checked against a hash Stationeers players quote:
        // HASH("StructureVolumePump") = -321403609. The byte encoding of a non-ASCII name is
        // assumed UTF-8 and is UNVERIFIED.
        public static int Hash(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < bytes.Length; i++)
            {
                crc = _crcTable[(crc ^ bytes[i]) & 0xFF] ^ (crc >> 8);
            }
            return unchecked((int)(crc ^ 0xFFFFFFFFu));
        }
    }
}
