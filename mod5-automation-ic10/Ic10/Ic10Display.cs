using System;
using System.Globalization;

namespace Ic10
{
    // What a logic display shows for a number: LogicDisplay.RenderText, with the helpers it
    // calls (ExtensionMethods.ToStringDisplay, ToStringPrefix, ToStringSimple and
    // StringManager.Get).
    //
    // Ported as read, quirks included. Two things are fixed here that Stationeers leaves to
    // the machine: numbers are written with the invariant culture (a full stop, never a
    // comma), and "G" means fifteen significant digits, which is what it means on the
    // runtime Stationeers ships on.
    public static class Ic10Display
    {
        // LogicDisplay.DisplayMode, same order and numbers.
        public const int Default = 0;
        public const int Percent = 1;
        public const int Power = 2;
        public const int Kelvin = 3;
        public const int Celsius = 4;
        public const int Meters = 5;
        public const int Credits = 6;
        public const int Seconds = 7;
        public const int Minutes = 8;
        public const int Days = 9;
        public const int String = 10;
        public const int Fahrenheit = 11;
        public const int Litres = 12;
        public const int Mol = 13;
        public const int Pa = 14;
        public const int Newtons = 15;
        public const int Degrees = 16;

        public const int ModeCount = 17;

        // Color: Stationeers' ColorType, Blue 0 to Purple 11 (Ic10StationeersTables.ColorType).
        // A new display draws green, the colour its text always had. The shades are ours, bright
        // enough to read on the panel: Stationeers' own swatches (GameManager.CustomColors) are
        // asset data not yet read. Black is near-black, as unreadable as there.
        public const int ColorCount = 12;
        public const int DefaultColor = 2;

        private static readonly float[] ColorRgb =
        {
            0.30f, 0.55f, 1.00f, // Blue
            0.62f, 0.62f, 0.62f, // Gray
            0.45f, 1.00f, 0.55f, // Green
            1.00f, 0.60f, 0.15f, // Orange
            1.00f, 0.25f, 0.22f, // Red
            1.00f, 0.92f, 0.25f, // Yellow
            1.00f, 1.00f, 1.00f, // White
            0.12f, 0.12f, 0.12f, // Black
            0.60f, 0.40f, 0.22f, // Brown
            0.76f, 0.69f, 0.50f, // Khaki
            1.00f, 0.55f, 0.78f, // Pink
            0.65f, 0.40f, 1.00f, // Purple
        };

        // The text colour for a Color number; out of range draws the default.
        public static void Rgb(int color, out float r, out float g, out float b)
        {
            if (color < 0 || color >= ColorCount)
            {
                color = DefaultColor;
            }
            r = ColorRgb[color * 3];
            g = ColorRgb[color * 3 + 1];
            b = ColorRgb[color * 3 + 2];
        }

        // LogicDisplay.StandardDigitMax and ExpDigits as the class sets them. They are
        // fields a prefab can override; the prefab's values are not known here.
        public const double StandardDigitMax = 99999.0;
        public const int ExpDigits = 2;

        // LogicDisplay.Awake: StandardDigitMax * 0.1f - 1f.
        private const double MaxPercent = 9998.9;

        public static string Format(double setting, int mode)
        {
            switch (mode)
            {
                case Percent:
                    return Math.Max(-MaxPercent, Math.Min(MaxPercent, setting * 100.0)).ToString("F0", CultureInfo.InvariantCulture) + "%";
                case Power:
                    return Prefix(setting, "W");
                case Kelvin:
                    return Prefix(setting, "K");
                case Celsius:
                    return Prefix(setting, "°C");
                case Fahrenheit:
                    return Prefix(setting, "°F");
                case Meters:
                    return Prefix(setting, "m");
                case Credits:
                    return Prefix(setting, "€");
                case Seconds:
                    return Simple(setting, "sec");
                case Minutes:
                    return Simple(setting, "min");
                case Days:
                    return Simple(setting, "days");
                case Litres:
                    return Prefix(setting, "L");
                case Mol:
                    return Prefix(setting, "mol");
                case Pa:
                    return Prefix(setting, "Pa");
                case Newtons:
                    return Prefix(setting, "N");
                case Degrees:
                    return Prefix(setting, "°");
                case String:
                    return Ic10Math.UnpackAscii6(setting, true);
                default:
                    return Plain(setting, StandardDigitMax, ExpDigits);
            }
        }

        // ExtensionMethods.ToStringDisplay(double, double, int): the number alone, cut to as
        // many characters as the largest number has digits.
        public static string Plain(double value, double maxValue, int expDigits)
        {
            if (Math.Abs(value) < 1E-07)
            {
                return "0";
            }
            double size = Math.Abs(value);
            if (size < 0.009999999776482582 || size > maxValue)
            {
                return value.ToString("G" + expDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            }
            if (size < 0.10000000149011612)
            {
                return value.ToString("F2", CultureInfo.InvariantCulture);
            }
            string text = value.ToString("G15", CultureInfo.InvariantCulture);
            int digits = (int)(Math.Floor(Math.Log10(maxValue)) + 1.0);
            return text.Length > digits ? text.Substring(0, digits) : text;
        }

        // ExtensionMethods.ToStringPrefix(double, string). The micro branch can never be
        // reached, because the milli test before it is true for everything micro is: kept.
        public static string Prefix(double value, string unit)
        {
            double size = Math.Abs(value);
            if (size >= 999999995904.0)
            {
                return Number(value / 999999995904.0) + " T" + unit;
            }
            if (size >= 1000000000.0)
            {
                return Number(value / 1000000000.0) + " G" + unit;
            }
            if (size >= 1000000.0)
            {
                return Number(value / 1000000.0) + " M" + unit;
            }
            if (size >= 1000.0)
            {
                return Number(value / 1000.0) + " k" + unit;
            }
            return Simple(value, unit);
        }

        // ExtensionMethods.ToStringSimple(double, string).
        public static string Simple(double value, string unit)
        {
            double size = Math.Abs(value);
            if (size < 1E-07)
            {
                return "0 " + unit;
            }
            if (size <= 0.0010000000474974513)
            {
                return Number(value / 0.0010000000474974513) + " m" + unit;
            }
            return Number(value) + " " + unit;
        }

        // StringManager.Get(float): three significant digits below 1000, a whole number
        // above 999. Stationeers looks the text up in a table built in steps of 0.001 below
        // 10, 0.01 below 100 and 0.1 below 1000; the same grid is computed here.
        public static string Number(double number)
        {
            float value = (float)number;
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                value = 0f;
            }
            if (value < -999f || value > 999f)
            {
                return ((int)Math.Round((double)value)).ToString(CultureInfo.InvariantCulture);
            }
            value = RoundToSignificantDigits(value, 3);
            float size = Math.Abs(value);
            string text;
            if (size < 10f)
            {
                text = (RoundToInt(size * 1000f) / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
            }
            else if (size < 100f)
            {
                text = (10.0 + RoundToInt((size - 10f) * 100f) / 100.0).ToString("0.##", CultureInfo.InvariantCulture);
            }
            else
            {
                text = (100.0 + RoundToInt((size - 100f) * 10f) / 10.0).ToString("0.#", CultureInfo.InvariantCulture);
            }
            // A negative number too small to show: Stationeers' table holds the text of
            // negative zero there, which is "-0" or "0" by runtime. Not read; "0" here.
            return value < 0f && text != "0" ? "-" + text : text;
        }

        // RocketMath.RoundToSignificantDigits(float, int).
        private static float RoundToSignificantDigits(float d, int digits)
        {
            if (d == 0f)
            {
                return 0f;
            }
            bool negative = d < 0f;
            if (negative)
            {
                d = -d;
            }
            double scale = Math.Pow(10.0, Math.Floor(Math.Log10(Math.Abs(d))) + 1.0);
            float rounded = (float)(scale * Math.Round((double)d / scale, digits));
            return negative ? -rounded : rounded;
        }

        // Mathf.RoundToInt: to the nearest whole number, a half to the even one.
        private static int RoundToInt(float value)
        {
            return (int)Math.Round((double)value);
        }
    }
}
