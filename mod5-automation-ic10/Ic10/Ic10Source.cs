using System.Text;

namespace Ic10
{
    // The limits Stationeers' code editor puts on a program (InputSourceCode): 128 lines,
    // 90 characters a line, ASCII only, 4096 characters in all. They are the editor's, not
    // the chip's: ProgrammableChip.SetSourceCode compiles whatever it is handed, and so does
    // Ic10Chip. An ONI editor applies these so that a program written here also fits there.
    public static class Ic10Source
    {
        public const int MaxLines = 128;        // InputSourceCode.MAX_LINES
        public const int MaxLineLength = 90;    // InputSourceCode.LINE_LENGTH_LIMIT
        public const int MaxSize = 4096;        // InputSourceCode.MAX_FILE_SIZE

        // What the editor's line fields hold a program to while it is typed: 128 fields,
        // each with a character limit of 90, each passed through AsciiString.ParseLine (a
        // tab becomes a space, anything outside ASCII is dropped). Text past either limit
        // is cut. Carriage returns are dropped, as the editor's split on '\n' and TrimEnd
        // drop them from pasted text.
        public static string Limit(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            StringBuilder result = new StringBuilder(text.Length);
            int lines = 1;
            int length = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n')
                {
                    if (lines == MaxLines)
                    {
                        break;
                    }
                    lines++;
                    length = 0;
                    result.Append('\n');
                    continue;
                }
                if (c == '\r' || c > 0x7F)
                {
                    continue;
                }
                if (length == MaxLineLength)
                {
                    continue;
                }
                result.Append(c == '\t' ? ' ' : c);
                length++;
            }
            return result.ToString();
        }

        // The size the editor shows against 4096 (InputSourceCode.UpdateFileSize): every
        // character of every line, and two for each line break before the last line that
        // has anything on it. Two, because the editor joins lines with a Windows newline.
        public static int Size(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }
            string[] lines = text.Split('\n');
            int last = 0;
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                last = i;
                if (lines[i].Length > 0)
                {
                    break;
                }
            }
            int size = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                size += lines[i].Length;
            }
            return size + 2 * last;
        }

        // True when Stationeers' editor would let the program be submitted.
        public static bool Fits(string text)
        {
            return Size(text) <= MaxSize;
        }

        // What the editor hands the chip (InputSourceCode.Copy): the limited lines joined
        // with Windows newlines, cut to 4096 characters, trailing whitespace removed. The
        // newlines come back as '\n', which is what the chip splits on; the carriage
        // returns it would have seen at the ends of lines are whitespace to it.
        public static string Clean(string text)
        {
            string joined = Limit(text).Replace("\n", "\r\n");
            if (joined.Length > MaxSize)
            {
                joined = joined.Substring(0, MaxSize);
            }
            return joined.TrimEnd().Replace("\r", string.Empty);
        }
    }
}
