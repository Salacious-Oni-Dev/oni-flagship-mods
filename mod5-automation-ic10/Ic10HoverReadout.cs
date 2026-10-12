using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Ic10;
using OniFramework;
using UnityEngine;

namespace Mod5AutomationIc10
{
    // The hover card of any IC10 device: every value a chip could read from it, live. Mod 5's own buildings and every vanilla or other mod's
    // building that is a device on the data network alike, since it asks the device, not the
    // building type. A display adds the text it shows; a housing adds its program line, error and
    // registers.
    //
    // Drawn as a section of the framework's hover card (OniFramework.HoverCard),
    // the one prefix on HoverTextDrawer.EndDrawing that Mod 1 shares, so the two mods' sections
    // stack instead of fighting over the card. Stationeers shows a device's values by pointing a
    // tablet or by its own screen; ONI's way to show a building's numbers is its hover card.
    internal static class Ic10HoverReadout
    {
        // Identity numbers every device answers: noise on a card a player reads.
        private static readonly HashSet<string> Skipped = new HashSet<string> { "PrefabHash", "NameHash" };

        private static readonly string[] ColourNames =
        {
            "Blue", "Gray", "Green", "Orange", "Red", "Yellow", "White", "Black", "Brown", "Khaki", "Pink", "Purple",
        };

        private static IDisposable section;

        public static void Install(Harmony harmony)
        {
            HoverCard.Install(harmony);
            if (section == null)
            {
                section = HoverCard.AddSection("Mod5AutomationIc10", AppliesTo, Draw, order: 50);
            }
        }

        private static GameObject DeviceBuilding(int cell, out IIc10Device device)
        {
            device = null;
            if (!Grid.IsValidCell(cell))
            {
                return null;
            }
            GameObject go = Grid.Objects[cell, (int)ObjectLayer.Building];
            KPrefabID id = go != null ? go.GetComponent<KPrefabID>() : null;
            if (id == null)
            {
                return null;
            }
            device = Ic10Devices.Find(id.InstanceID);
            return device != null ? go : null;
        }

        private static bool AppliesTo(int cell)
        {
            IIc10Device device;
            return DeviceBuilding(cell, out device) != null;
        }

        private static void Draw(HoverTextDrawer drawer, int cell)
        {
            IIc10Device device;
            GameObject go = DeviceBuilding(cell, out device);
            if (go == null)
            {
                return;
            }
            drawer.BeginShadowBar();
            drawer.DrawText(("IC10 device: " + go.GetProperName()).ToUpperInvariant(), HoverCard.TitleStyle);
            Ic10DisplayBrick display = go.GetComponent<Ic10DisplayBrick>();
            if (display != null)
            {
                Line(drawer, "Shows: " + (display.Lit ? "'" + display.Text + "'" : "nothing (dark)"));
            }
            Ic10HousingBuilding housing = go.GetComponent<Ic10HousingBuilding>();
            if (housing != null && housing.Chip != null)
            {
                Line(drawer, "Program line: " + housing.Chip.LineNumber + ", error: " + (housing.ErrorText() ?? "none"));
                System.Text.StringBuilder registers = new System.Text.StringBuilder();
                for (int i = 0; i < 16; i++)
                {
                    if (i == 8)
                    {
                        Line(drawer, registers.ToString());
                        registers.Length = 0;
                    }
                    registers.Append(i % 8 == 0 ? "" : "  ").Append('r').Append(i).Append(' ').Append(Number(housing.Chip.GetRegister(i)));
                }
                Line(drawer, registers.ToString());
            }
            foreach (KeyValuePair<string, int> type in Ic10Devices.Enums.LogicType.Pairs)
            {
                if (Skipped.Contains(type.Key) || !device.CanLogicRead(type.Value))
                {
                    continue;
                }
                double value;
                try
                {
                    value = device.GetLogicValue(type.Value);
                }
                catch (Exception)
                {
                    continue;
                }
                Line(drawer, type.Key + ": " + Format(type.Key, value));
            }
            drawer.EndShadowBar();
        }

        private static void Line(HoverTextDrawer drawer, string text)
        {
            drawer.NewLine();
            drawer.DrawIcon(HoverCard.DashIcon);
            drawer.DrawText(text, HoverCard.BodyStyle);
        }

        private static string Number(double value)
        {
            if (double.IsNaN(value))
            {
                return "NaN";
            }
            double abs = Math.Abs(value);
            return abs != 0.0 && (abs >= 1e6 || abs < 1e-3)
                ? value.ToString("G4", CultureInfo.InvariantCulture)
                : value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Format(string name, double value)
        {
            if (name.StartsWith("Ratio", StringComparison.Ordinal))
            {
                return (value * 100.0).ToString("0.##", CultureInfo.InvariantCulture) + " %";
            }
            if (name == "Temperature")
            {
                return Number(value) + " K (" + (value - 273.15).ToString("0.#", CultureInfo.InvariantCulture) + " °C)";
            }
            if (name.StartsWith("Pressure", StringComparison.Ordinal))
            {
                return Number(value) + " kPa";
            }
            if (name == "Color")
            {
                int colour = (int)value;
                return colour >= 0 && colour < ColourNames.Length ? ColourNames[colour] + " (" + colour + ")" : Number(value);
            }
            return Number(value);
        }
    }
}
