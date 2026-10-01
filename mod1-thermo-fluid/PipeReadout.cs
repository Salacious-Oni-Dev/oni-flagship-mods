using System.Text;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// Rendering for Mod 1's pipe readout -- the Stationeers pipe tooltip, ported to ONI's own
    /// hover card.
    ///
    /// Stationeers builds this from three methods:
    /// <c>AtmosphericsManager.DisplayBasicAtmosphere</c> writes Pressure / Temperature / Volume
    /// / Liquids Volume; <c>AtmosphericsManager.DisplayGas</c> writes one line per species as
    /// "name, percent of total moles, mole count"; and <c>Pipe.GetExtendedText</c> appends the
    /// stress warnings in red. This reproduces that line set and that ordering. What it does NOT
    /// reproduce is Stationeers' rich-text markup (<c>&lt;color=yellow&gt;</c> and friends) --
    /// ONI's <c>HoverTextDrawer</c> takes a <c>TextStyleSetting</c> per line instead of inline
    /// tags, so the colour comes from Klei's own <c>Styles_Warning</c> pair, which is what every
    /// vanilla hover card already uses to say "this is a problem."
    ///
    /// All numbers come from <see cref="PipeNetworkFacade"/>; nothing is computed here. This
    /// file is presentation only, and lives in the flagship rather than the framework for that
    /// reason -- a third-party mod consuming the API should get the numbers, not our wording.
    /// </summary>
    internal static class PipeReadout
    {
        /// <summary>
        /// SI-prefixed pressure, the same shape Stationeers' own <c>ToStringPrefix("Pa")</c>
        /// produces. Ported because the corrected pipe volume
        /// (<see cref="GasMixtureFacade.GasConduitVolumeM3"/>, 10 L) puts real pipe pressures in
        /// the megapascal range, where a raw "60794998 Pa" is unreadable.
        /// </summary>
        internal static string FormatPressure(float pascals)
        {
            float magnitude = Mathf.Abs(pascals);
            if (magnitude >= 1e9f)
            {
                return $"{pascals / 1e9f:F2} GPa";
            }
            if (magnitude >= 1e6f)
            {
                return $"{pascals / 1e6f:F2} MPa";
            }
            if (magnitude >= 1e3f)
            {
                return $"{pascals / 1e3f:F2} kPa";
            }
            return $"{pascals:F1} Pa";
        }

        /// <summary>
        /// Mole counts span a huge range in practice (a near-empty pipe holds millimoles, a
        /// charged one holds hundreds), so this switches precision rather than printing "0.00"
        /// for a segment that genuinely has something in it.
        /// </summary>
        internal static string FormatMoles(float moles)
        {
            if (moles >= 100f)
            {
                return $"{moles:F0} mol";
            }
            if (moles >= 1f)
            {
                return $"{moles:F1} mol";
            }
            return $"{moles:F3} mol";
        }

        internal static string FormatVolume(float litres)
        {
            return litres >= 1000f ? $"{litres / 1000f:F2} m3" : $"{litres:F0} L";
        }

        /// <summary>
        /// The player-facing name of an element -- <c>Element.name</c>, the localized proper
        /// name vanilla's own hover cards print ("Carbon Dioxide"), not <c>Element.tag</c>,
        /// which is the internal identifier and renders as "CarbonDioxide". Falls back to the
        /// tag, then to the raw index, so an element this mod introduces before it has strings
        /// still shows something.
        /// </summary>
        internal static string ElementName(int elementIdx)
        {
            if (elementIdx < 0 || elementIdx >= ElementLoader.elements.Count)
            {
                return elementIdx.ToString();
            }
            Element element = ElementLoader.elements[elementIdx];
            if (element == null)
            {
                return elementIdx.ToString();
            }
            return string.IsNullOrEmpty(element.name) ? element.tag.ToString() : element.name;
        }

        /// <summary>
        /// The same name with TMP rich-text markup removed. <c>Element.name</c> carries a
        /// <c>&lt;link="CARBONDIOXIDE"&gt;</c> wrapper:
        /// harmless inside a LocText, which renders it exactly the way vanilla's own hover card
        /// does, but it leaks as literal angle brackets into a log line or a notification body.
        /// Stripped with Klei's own <c>Util.StripTextFormatting</c> rather than a hand-rolled
        /// regex.
        /// </summary>
        internal static string ElementNamePlain(int elementIdx)
        {
            return Util.StripTextFormatting(ElementName(elementIdx));
        }

        /// <summary>
        /// One-line human description of a stress report, reused verbatim by both the hover
        /// tooltip and the notification (<see cref="PipeStressMonitor"/>) so a player never sees
        /// two different wordings for the same condition.
        /// </summary>
        internal static string DescribeStress(PipeStressReport stress)
        {
            string element = stress.ElementIdx >= 0 &&
                stress.ElementIdx < ElementLoader.elements.Count
                ? ElementNamePlain(stress.ElementIdx)
                : "contents";

            switch (stress.Kind)
            {
                case PipeStressKind.Overpressure:
                    // A liquid run reports a fill fraction, not a pressure -- see
                    // PipeNetworkFacade's own deviation note.
                    return stress.Limit <= 1.001f
                        ? (stress.Level == PipeStressLevel.Critical
                            ? $"Pipe network full: {stress.Value * 100f:F0}% of capacity"
                            : $"Pipe network nearly full: {stress.Value * 100f:F0}% of capacity")
                        : (stress.Level == PipeStressLevel.Critical
                            ? $"Pipe network OVERPRESSURED: {FormatPressure(stress.Value)} vs {FormatPressure(stress.Limit)} rating"
                            : $"Pipe network stressed: {FormatPressure(stress.Value)} of {FormatPressure(stress.Limit)} rating");

                case PipeStressKind.Condensation:
                    return stress.Level == PipeStressLevel.Critical
                        ? $"CONDENSING in gas pipe: {element} at {stress.Value:F1} K, below {stress.Limit:F1} K"
                        : $"Approaching condensation: {element} at {stress.Value:F1} K, condenses at {stress.Limit:F1} K";

                case PipeStressKind.Freezing:
                    return stress.Level == PipeStressLevel.Critical
                        ? $"FREEZING in pipe: {element} at {stress.Value:F1} K, below {stress.Limit:F1} K"
                        : $"Approaching freezing: {element} at {stress.Value:F1} K, freezes at {stress.Limit:F1} K";

                case PipeStressKind.Vaporization:
                    return stress.Level == PipeStressLevel.Critical
                        ? $"BOILING in liquid pipe: {element} at {stress.Value:F1} K, above {stress.Limit:F1} K"
                        : $"Approaching boiling: {element} at {stress.Value:F1} K, boils at {stress.Limit:F1} K";

                // The two standing-matter kinds report a QUANTITY HELD, not a temperature, so
                // they get their own wording rather than reusing the thermal lines above -- a
                // "CONDENSING ... at 7.5 K, below 0.2 K" line built out of litres and a threshold
                // is worse than no line at all.
                case PipeStressKind.StandingLiquid:
                    return stress.Level == PipeStressLevel.Critical
                        ? $"LIQUID IN GAS PIPE: {stress.Value:F2} L of {element} held, past the {stress.Limit:F2} L this network can take"
                        : $"Liquid collecting in gas pipe: {stress.Value:F2} L of {element} held, damage starts at {stress.Limit:F2} L";

                case PipeStressKind.StandingSolid:
                    return stress.Level == PipeStressLevel.Critical
                        ? $"PIPE PLUGGED WITH SOLID: {stress.Value:F2} mol of {element} held, past the {stress.Limit:F2} mol this network can take"
                        : $"Solid collecting in pipe: {stress.Value:F2} mol of {element} held, damage starts at {stress.Limit:F2} mol";

                default:
                    return "Pipe network stressed";
            }
        }

        /// <summary>
        /// One property line, drawn the way every vanilla hover card draws one: a
        /// <c>NewLine</c> to actually move down, a dash icon, then the text.
        ///
        /// The NewLine is the whole point. <c>HoverTextDrawer.DrawText</c> only advances
        /// <c>currentPos.x</c> -- it never wraps -- so a run of DrawText calls with nothing
        /// between them lays every line end-to-end horizontally, which is exactly what this
        /// readout used to do. <c>NewLine</c> is the only thing that drops <c>currentPos.y</c>
        /// and resets x to the shadow bar's left edge.
        ///
        /// <paramref name="dash"/> is <c>SelectToolHoverTextCard.iconDash</c>, which is
        /// [NonSerialized] and only populated by <c>ConfigureHoverScreen</c>. It can therefore
        /// legitimately be null on a card that has not drawn yet, so a null falls back to a
        /// plain indent of the same width rather than skipping the line.
        /// </summary>
        internal static void DrawProperty(HoverTextDrawer drawer, Sprite dash, string text,
            TextStyleSetting style)
        {
            drawer.NewLine();
            if (dash != null)
            {
                drawer.DrawIcon(dash);
            }
            else
            {
                drawer.AddIndent(22);
            }
            drawer.DrawText(text, style);
        }

        /// <summary>
        /// Draws the whole readout as one shadow bar. Caller has already established that there
        /// is a network here; an EMPTY network still draws (with its volume and a "Contents:
        /// none" line), because "this pipe is empty" is exactly the answer a player hovering an
        /// unexpectedly-dead line needs, and Stationeers says it too
        /// (<c>GameStrings.None.AsColor("yellow")</c> in <c>DisplayBasicAtmosphere</c>).
        ///
        /// Layout follows vanilla's own hover cards rather than inventing one: a title with no
        /// dash, then dashed property lines, then a two-level list for the species breakdown
        /// (undashed header, dashed items) -- the same shape SelectToolHoverTextCard's decor
        /// block uses -- and finally the stress warning behind the warning icon, the way vanilla
        /// draws a status-item warning.
        /// </summary>
        internal static void Draw(HoverTextDrawer drawer, SelectToolHoverTextCard styles,
            string title, PipeNetworkState state)
        {
            TextStyleSetting body = styles.Styles_BodyText.Standard;
            // Styles_Warning is a serialized per-prefab field like the others, so it can be
            // unset on whatever SelectToolHoverTextCard instance the scene happens to hand us.
            // A null TextStyleSetting reaching DrawText would throw inside HoverPatch's
            // catch-all and silently drop this entire block -- the readout would just stop
            // appearing, with no error anywhere. Falling back to body text costs the red colour
            // and nothing else.
            TextStyleSetting warning = styles.Styles_Warning.Standard ?? body;
            Sprite dash = styles.iconDash;

            drawer.BeginShadowBar();
            // Vanilla hover-card titles are upper case (SelectToolHoverTextCard draws
            // element.nameUpperCase, and every UI string it uses for a title is capitalised in
            // the string table). Applied here rather than at the call sites so the plain-text
            // form -- which goes into a notification body, where shouting reads badly -- keeps
            // its normal casing.
            drawer.DrawText(title.ToUpperInvariant(), styles.Styles_Title.Standard);

            if (state.ContentType == PipeContentType.Gas)
            {
                DrawProperty(drawer, dash, state.TotalMoles > 0f
                    ? $"Pressure: {FormatPressure(state.PressurePa)}"
                    : "Pressure: none", body);
            }

            DrawProperty(drawer, dash, state.TotalMassKg > 0f
                ? $"Temperature: {state.TemperatureK:F1} K"
                : "Temperature: none", body);

            // Stationeers prints the WHOLE network's volume here, not one pipe's -- its
            // Atmosphere belongs to the PipeNetwork. The tile count is ours, added because ONI
            // players build pipe runs tile by tile and "18 tiles" explains the volume figure in
            // a way "180 L" alone does not.
            DrawProperty(drawer, dash,
                $"Volume: {FormatVolume(state.VolumeLitres)} ({state.Cells.Length} tiles)", body);

            if (state.ContentType == PipeContentType.Liquid)
            {
                DrawProperty(drawer, dash,
                    $"Liquid Volume: {FormatVolume(state.LiquidVolumeLitres)} ({state.FillFraction * 100f:F0}% full)",
                    body);
            }

            if (state.Contents.Length == 0)
            {
                DrawProperty(drawer, dash, "Contents: none", body);
            }
            else
            {
                // Undashed header, then one dashed line per species. NewLine(18) on the items
                // rather than the default 26 is vanilla's own value for a nested icon row
                // (SelectToolHoverTextCard's decor breakdown), and keeps a long mixture from
                // stretching the card.
                drawer.NewLine();
                drawer.DrawText("Contents:", body);
                foreach (PipeSpecies species in state.Contents)
                {
                    string name = ElementName(species.ElementIdx);
                    drawer.NewLine(18);
                    if (dash != null)
                    {
                        drawer.DrawIcon(dash);
                    }
                    else
                    {
                        drawer.AddIndent(22);
                    }
                    drawer.DrawText(
                        $"{name}  {species.MoleFraction * 100f:F1}%  {FormatMoles(species.Moles)}  ({species.MassKg:F3} kg)",
                        body);
                }
            }

            if (PipeNetworkFacade.TryEvaluateStress(state, out PipeStressReport stress))
            {
                drawer.NewLine();
                if (styles.iconWarning != null)
                {
                    drawer.DrawIcon(styles.iconWarning);
                }
                else
                {
                    drawer.AddIndent(22);
                }
                drawer.DrawText(DescribeStress(stress), warning);
            }

            drawer.EndShadowBar();
        }

        /// <summary>
        /// Plain-text form of the same readout, for log lines and the notification tooltip --
        /// anywhere a <c>HoverTextDrawer</c> is not available.
        /// </summary>
        internal static string ToPlainText(string title, PipeNetworkState state)
        {
            var sb = new StringBuilder();
            sb.AppendLine(title);
            if (state.ContentType == PipeContentType.Gas)
            {
                sb.AppendLine($"Pressure: {FormatPressure(state.PressurePa)}");
            }
            sb.AppendLine($"Temperature: {state.TemperatureK:F1} K");
            sb.AppendLine($"Volume: {FormatVolume(state.VolumeLitres)} ({state.Cells.Length} tiles)");
            if (state.ContentType == PipeContentType.Liquid)
            {
                sb.AppendLine(
                    $"Liquid Volume: {FormatVolume(state.LiquidVolumeLitres)} ({state.FillFraction * 100f:F0}% full)");
            }
            if (state.Contents.Length == 0)
            {
                // Same line Draw emits -- the two must agree, since this text is what a test
                // reads to check what the tooltip shows.
                sb.AppendLine("Contents: none");
            }
            else
            {
                sb.AppendLine("Contents:");
                foreach (PipeSpecies species in state.Contents)
                {
                    string name = ElementNamePlain(species.ElementIdx);
                    sb.AppendLine(
                        $"  {name}  {species.MoleFraction * 100f:F1}%  {FormatMoles(species.Moles)}  ({species.MassKg:F3} kg)");
                }
            }
            if (PipeNetworkFacade.TryEvaluateStress(state, out PipeStressReport stress))
            {
                sb.AppendLine(DescribeStress(stress));
            }
            return sb.ToString();
        }
    }
}
