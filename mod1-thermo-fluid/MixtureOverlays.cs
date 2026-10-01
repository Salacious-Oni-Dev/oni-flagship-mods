using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// TWO NEW OVERLAYS, appended to the vanilla overlay menu: one for liquid that is carrying
    /// dissolved gas (<c>sim.dissolved_mass</c>) and one for gas cells holding more than
    /// one species (the volume-fractions layer, <c>sim.gas_species</c> / <c>sim.gas_mass</c>).
    ///
    /// WHY TWO AND NOT ONE. They answer different questions and they read different cells: a
    /// liquid cell has a solvent and a dissolved load, and the number a player wants is a
    /// CONCENTRATION in the water; a gas cell has no solvent at all, and the number is a
    /// composition of the cell's whole mass. Folding them together would mean one legend with two
    /// unrelated scales on it.
    ///
    /// THE COLOUR RULE MATCHES STATIONEERS. Its <c>Atmosphere.GetGasColor()</c> is a
    /// straight fraction-weighted linear blend of per-species colours -- every species' own colour
    /// times its share of the mixture's total quantity, summed -- and
    /// <c>Atmosphere.GetLiquidColor()</c> immediately above it does exactly the same for a liquid
    /// mixture. That is the whole mechanism, it is proven, and there is no reason to invent a
    /// second one. Two details are deliberate:
    ///
    ///   * STATIONEERS WEIGHTS BY QUANTITY, WHICH IS MOLES (<c>Quantity.ToFloat()</c> on a
    ///     <c>GasMixture</c> is mole-valued), not by mass. Our lanes and slots are kilograms, so
    ///     the blend below divides each by <c>Element.molarMass</c> first. Blending the kilograms
    ///     directly would be a silent divergence -- carbon dioxide is 2.75x hydrogen's molar mass,
    ///     so a 50/50 mole mixture of them would paint 73% CO2-coloured.
    ///   * <c>GetLiquidColor</c> leaves <c>LiquidHydrogen</c> out of its own denominator while
    ///     still adding its term to the sum, so a mixture containing it can return a colour whose
    ///     weights do not total 1. That is a bug in the reference and is deliberately NOT
    ///     reproduced.
    ///
    /// THE ONE ADDITION ON TOP OF THE REFERENCE: FRACTION
    /// SETS THE HUE, TOTAL CONCENTRATION SETS THE INTENSITY. A Stationeers <c>Atmosphere</c> is
    /// one uniform volume and its colour only ever has to say WHAT is in it. An ONI overlay tints
    /// a grid, where "how much is in THIS tile" is the entire question the player opened the
    /// overlay to ask, and a blend alone cannot say it -- a cell with a trace of CO2 and a cell
    /// that is saturated with it produce the identical blend. So the blend decides RGB and a
    /// separate scalar decides alpha, which is exactly the shape vanilla's own density overlays
    /// use (<c>SimDebugView.GetDiseaseColour</c> takes the disease's colour by name and puts the
    /// count in the alpha; <c>GetRadiationColour</c> and <c>GetLightColour</c> do the same).
    ///
    /// WHERE THE DATA COMES FROM, and why it is not a per-cell query. Both layers live in the
    /// SimDLL's extension cell properties, and a per-cell read of either takes the sim's worker
    /// barrier (<c>DissolvedGas.Read</c> says so in its own doc comment). A colour function runs
    /// once per visible cell per frame, across Klei's job workers, so a barrier per cell is out
    /// twice over -- once on cost and once because <c>WaitIdle()</c> is game-thread-only.
    /// <see cref="MixtureSnapshot"/> subscribes the properties to the per-frame publish
    /// (<c>SimExtCellProperties.Publish</c>) and copies the whole published array once per tick on
    /// the game thread, inside <c>SimExtFrame.Bound</c>. Every read below is then a managed array
    /// index with no synchronisation at all, which is the same trade
    /// <c>DissolveRig.OnBound</c> already makes.
    /// </summary>
    internal static class MixtureOverlays
    {
        /// <summary>The dissolved-gas-in-liquid overlay's view mode.</summary>
        public static readonly HashedString MixedLiquidsID = new HashedString("Mod1MixedLiquids");

        /// <summary>The gas-mixture overlay's view mode.</summary>
        public static readonly HashedString MixedGasesID = new HashedString("Mod1MixedGases");

        public const string LiquidsTitle = "DISSOLVED GAS";
        public const string GasesTitle = "GAS MIXTURE";

        public const string LiquidsButton = "Dissolved Gas";
        public const string GasesButton = "Gas Mixture";

        public const string LiquidsTooltip =
            "Liquid carrying dissolved gas. The tint is the mixture of gases dissolved in that "
            + "cell's liquid, blended by mole fraction; the strength is the concentration.";

        public const string GasesTooltip =
            "Gas composition per cell. The tint is the mixture of species in that cell, blended "
            + "by mole fraction; the strength is the cell's total gas mass.";

        /// <summary>
        /// The concentration a liquid cell has to reach to paint at full strength, in grams of
        /// dissolved gas per kilogram of liquid.
        ///
        /// 10 g/kg, chosen against what the chain actually produces rather than against a round
        /// number: carbon dioxide's Henry saturation in water is about 1.7 g/kg at one atmosphere
        /// and 293 K, the carbonation vessel holds its pond at 6 g/kg under four atmospheres
        ///, and a soda fountain draws at that. A full scale of 10 puts
        /// ordinary carbonated water at roughly four fifths of the ramp, leaves headroom above it,
        /// and does not wash out a pond that is merely in contact with air.
        ///
        /// Public and settable so a rig or a screenshot demo can rescale it for a vessel that
        /// works somewhere else on the curve; the legend prints whatever it is set to, so a
        /// screenshot can never claim a scale it was not taken at.
        /// </summary>
        public static float LiquidFullScaleGPerKg = 10f;

        /// <summary>
        /// The total gas mass a cell needs to paint at full strength, in kilograms. Two
        /// kilograms: an ordinary pressurised colony room runs near one to two, and a cell over
        /// that is already in the range where vanilla's own pressure overlay saturates.
        /// </summary>
        public static float GasFullScaleKg = 2f;

        /// <summary>
        /// The ramp both overlays run their intensity through: <c>pow(min(x, 1), 0.45)</c>.
        ///
        /// NOT A NEW CURVE. 0.45 is the exponent Klei's own liquid property texture uses to turn a
        /// cell's mass into its rendered fill (<c>LiquidFillAlpha</c>, reproduced in
        /// the SimDLL's sim/textures.h), so a liquid tinted by this overlay
        /// gains opacity on the same curve the liquid itself gains it on. Reusing it costs nothing
        /// and means one fewer number in this file that nobody can re-derive.
        /// </summary>
        public static float Ramp(float x)
        {
            if (!(x > 0f))
            {
                return 0f;
            }
            if (x > 1f)
            {
                x = 1f;
            }
            return Mathf.Pow(x, 0.45f);
        }

        // Alpha 0 with a black RGB is what every vanilla colour function returns for "nothing
        // here" -- see GetDiseaseColour's else branch. The compositor treats it as untinted.
        private static readonly Color Nothing = new Color(0f, 0f, 0f, 0f);

        // Per-thread scratch for the blend. The colour functions run across Klei's whole worker
        // pool, once per visible cell, so a shared buffer would be a race and a fresh one would be
        // garbage at tens of thousands of allocations a frame.
        [ThreadStatic] private static ushort[] scratchElementIdx;
        [ThreadStatic] private static float[] scratchKg;

        private static int MaxEntries
        {
            get
            {
                int lanes = DissolvedGas.Lanes;
                return lanes > MixtureSnapshot.GasSlots ? lanes : MixtureSnapshot.GasSlots;
            }
        }

        private static void EnsureScratch()
        {
            int n = MaxEntries;
            if (scratchElementIdx == null || scratchElementIdx.Length < n)
            {
                scratchElementIdx = new ushort[n];
                scratchKg = new float[n];
            }
        }

        /// <summary>
        /// The Stationeers blend, in moles: every species' own UI colour times its share of the
        /// mixture's total amount of substance, summed. Returns <c>Color.black</c> for an empty or
        /// massless mixture, which no caller paints.
        ///
        /// <c>substance.uiColour</c> rather than <c>substance.colour</c>: the latter is the colour
        /// the world renderer draws the element with, which for several gases is a near-neutral
        /// grey chosen to look right behind everything else (carbon dioxide's is literally
        /// (30,30,30)), while <c>uiColour</c> is the one Klei's own overlays and UI use to tell
        /// elements apart -- <c>SimDebugView.GetTileTypeColour</c> and <c>GetTileColour</c> both
        /// read it. A blend of near-greys carries no information at all, which is the same defect
        /// OverlayTexturePatch.BoostColour was written to fix on the other side of the mod.
        /// </summary>
        public static Color Blend(ushort[] elementIdx, float[] kg, int count)
        {
            float totalMoles = 0f;
            float r = 0f;
            float g = 0f;
            float b = 0f;
            List<Element> elements = ElementLoader.elements;
            for (int i = 0; i < count; i++)
            {
                int idx = elementIdx[i];
                if (idx < 0 || idx >= elements.Count || !(kg[i] > 0f))
                {
                    continue;
                }
                Element element = elements[idx];
                float molarMass = element.molarMass;
                if (!(molarMass > 0f))
                {
                    // Element.molarMass defaults to 1 and is never legitimately zero; guarding
                    // rather than dividing keeps one bad content row from painting infinities
                    // across the grid.
                    continue;
                }
                float moles = kg[i] / molarMass;
                Color32 ui = element.substance != null
                    ? element.substance.uiColour : new Color32(255, 255, 255, 255);
                totalMoles += moles;
                r += ui.r * (1f / 255f) * moles;
                g += ui.g * (1f / 255f) * moles;
                b += ui.b * (1f / 255f) * moles;
            }
            if (!(totalMoles > 0f))
            {
                return Color.black;
            }
            float inv = 1f / totalMoles;
            return new Color(r * inv, g * inv, b * inv, 1f);
        }

        /// <summary>
        /// The dissolved-gas overlay's per-cell colour, called once per visible cell per frame
        /// from inside Klei's own <c>UpdateSimViewWorkItem.Run</c>.
        ///
        /// Reads nothing but the tick's snapshot, <c>Grid.Mass</c> and <c>Grid.Element</c> -- the
        /// same two grid arrays every vanilla colour function on this path already reads from
        /// these same threads.
        /// </summary>
        public static Color LiquidColour(SimDebugView instance, int cell)
        {
            if (!Grid.IsValidCell(cell) || !Grid.IsLiquid(cell))
            {
                return Nothing;
            }
            float liquidKg = Grid.Mass[cell];
            if (!(liquidKg > 0f))
            {
                return Nothing;
            }
            EnsureScratch();
            float dissolvedKg;
            int count = MixtureSnapshot.Instance.ReadDissolved(
                cell, scratchElementIdx, scratchKg, out dissolvedKg);
            if (count == 0 || !(dissolvedKg > 0f))
            {
                return Nothing;
            }
            Color colour = Blend(scratchElementIdx, scratchKg, count);
            float gPerKg = dissolvedKg / liquidKg * 1000f;
            float scale = LiquidFullScaleGPerKg;
            colour.a = Ramp(scale > 0f ? gPerKg / scale : 0f);
            return colour;
        }

        /// <summary>
        /// The gas-mixture overlay's per-cell colour. A cell the mixture layer owns is painted
        /// from its real composition; an ordinary vanilla gas cell is painted from its single
        /// element, which is a mixture of one and belongs on the same map -- leaving it blank
        /// would make an unpromoted world look like a broken overlay rather than a single-species
        /// one, and it is the contrast between the two that shows what the layer does.
        /// </summary>
        public static Color GasColour(SimDebugView instance, int cell)
        {
            if (!Grid.IsValidCell(cell) || Grid.Solid[cell] || Grid.IsLiquid(cell))
            {
                return Nothing;
            }
            EnsureScratch();
            float totalKg;
            int count = MixtureSnapshot.Instance.ReadGas(
                cell, scratchElementIdx, scratchKg, out totalKg);
            if (count == 0)
            {
                Element element = Grid.Element[cell];
                float massKg = Grid.Mass[cell];
                if (element == null || !element.IsGas || !(massKg > 0f))
                {
                    return Nothing;
                }
                scratchElementIdx[0] = (ushort)ElementLoader.GetElementIndex(element.id);
                scratchKg[0] = massKg;
                count = 1;
                totalKg = massKg;
            }
            if (!(totalKg > 0f))
            {
                return Nothing;
            }
            Color colour = Blend(scratchElementIdx, scratchKg, count);
            float scale = GasFullScaleKg;
            colour.a = Ramp(scale > 0f ? totalKg / scale : 0f);
            return colour;
        }

        // ------------------------------------------------------------------ the legend census

        /// <summary>One species' share of everything the camera can currently see.</summary>
        internal struct Share
        {
            public int ElementIdx;
            public float Kg;
            public float Fraction;
        }

        /// <summary>
        /// Every species in view with its share, richest first, plus how many visible cells carry
        /// more than one of them. Runs on the game thread from a mode's <c>Update</c>, throttled by
        /// the caller -- it is a sweep of the visible rectangle, which is the same order of work
        /// one frame of the overlay itself costs, and it does not need to be done every frame.
        ///
        /// Shares are by MOLE, matching the blend: a legend that reported mass shares beside a
        /// tint computed from mole shares would disagree with the picture it is explaining.
        /// </summary>
        internal static int Census(bool dissolved, List<Share> into, out int mixedCells,
            out float peak)
        {
            into.Clear();
            mixedCells = 0;
            peak = 0f;

            int minX, minY, maxX, maxY;
            Grid.GetVisibleExtents(out minX, out minY, out maxX, out maxY);

            ushort[] idx = new ushort[MaxEntries];
            float[] kg = new float[MaxEntries];
            List<Element> elements = ElementLoader.elements;
            Dictionary<int, float> moles = new Dictionary<int, float>();
            float totalMoles = 0f;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell) || !Grid.IsActiveWorld(cell))
                    {
                        continue;
                    }
                    int count;
                    float total;
                    if (dissolved)
                    {
                        if (!Grid.IsLiquid(cell))
                        {
                            continue;
                        }
                        count = MixtureSnapshot.Instance.ReadDissolved(cell, idx, kg, out total);
                        float liquidKg = Grid.Mass[cell];
                        if (count > 0 && liquidKg > 0f)
                        {
                            float gPerKg = total / liquidKg * 1000f;
                            if (gPerKg > peak)
                            {
                                peak = gPerKg;
                            }
                        }
                    }
                    else
                    {
                        if (Grid.Solid[cell] || Grid.IsLiquid(cell))
                        {
                            continue;
                        }
                        count = MixtureSnapshot.Instance.ReadGas(cell, idx, kg, out total);
                        if (count == 0)
                        {
                            Element element = Grid.Element[cell];
                            float massKg = Grid.Mass[cell];
                            if (element == null || !element.IsGas || !(massKg > 0f))
                            {
                                continue;
                            }
                            idx[0] = (ushort)ElementLoader.GetElementIndex(element.id);
                            kg[0] = massKg;
                            count = 1;
                            total = massKg;
                        }
                        if (total > peak)
                        {
                            peak = total;
                        }
                    }
                    if (count == 0)
                    {
                        continue;
                    }
                    if (count > 1)
                    {
                        mixedCells++;
                    }
                    for (int i = 0; i < count; i++)
                    {
                        int e = idx[i];
                        if (e < 0 || e >= elements.Count || !(kg[i] > 0f))
                        {
                            continue;
                        }
                        float molarMass = elements[e].molarMass;
                        if (!(molarMass > 0f))
                        {
                            continue;
                        }
                        float m = kg[i] / molarMass;
                        float had;
                        moles[e] = moles.TryGetValue(e, out had) ? had + m : m;
                        totalMoles += m;
                    }
                }
            }

            if (!(totalMoles > 0f))
            {
                return 0;
            }
            foreach (KeyValuePair<int, float> pair in moles)
            {
                into.Add(new Share
                {
                    ElementIdx = pair.Key,
                    Kg = pair.Value * elements[pair.Key].molarMass,
                    Fraction = pair.Value / totalMoles
                });
            }
            into.Sort((a, b) => b.Fraction.CompareTo(a.Fraction));
            return into.Count;
        }

        // ------------------------------------------------------------------ the cell breakdown

        /// <summary>
        /// THE EXACT NUMBERS, ON THE CELL UNDER THE CURSOR. A blend answers "what is in here" to
        /// within a hue; it cannot answer "how much of each", and two mixtures a player would
        /// want to tell apart can paint the same colour -- that is inherent to any scheme that
        /// projects several quantities onto one, and it is why Stationeers' own colour blend sits
        /// beside a readout rather than instead of one.
        ///
        /// Registered as its own hover-card section rather than folded into
        /// <c>HoverTextDrawer_EndDrawing</c>'s, because its gate is completely different: that
        /// one draws whenever Mod 1 has something to say about a cell, this one draws only while
        /// one of the two overlays is open. Order -10 puts it above the rest of Mod 1's card --
        /// when a player has an overlay open, the overlay's own numbers are what they are looking
        /// for.
        /// </summary>
        internal static void InstallHoverSection()
        {
            HoverCard.AddSection("Mod1ThermoFluid.MixtureOverlays", HoverAppliesTo, HoverDraw,
                order: -10);
        }

        private static bool ActiveMode(out bool dissolved)
        {
            dissolved = false;
            if (OverlayScreen.Instance == null)
            {
                return false;
            }
            HashedString mode = OverlayScreen.Instance.GetMode();
            if (mode == MixedLiquidsID)
            {
                dissolved = true;
                return true;
            }
            return mode == MixedGasesID;
        }

        private static bool HoverAppliesTo(int cell)
        {
            bool dissolved;
            if (!ActiveMode(out dissolved) || !Grid.IsValidCell(cell))
            {
                return false;
            }
            return dissolved ? Grid.IsLiquid(cell) : (!Grid.Solid[cell] && !Grid.IsLiquid(cell));
        }

        private static void HoverDraw(HoverTextDrawer drawer, int cell)
        {
            bool dissolved;
            if (!ActiveMode(out dissolved) || !Grid.IsValidCell(cell))
            {
                return;
            }

            ushort[] idx = new ushort[MaxEntries];
            float[] kg = new float[MaxEntries];
            float totalKg;
            int count;
            bool vanillaFallback = false;
            if (dissolved)
            {
                count = MixtureSnapshot.Instance.ReadDissolved(cell, idx, kg, out totalKg);
            }
            else
            {
                count = MixtureSnapshot.Instance.ReadGas(cell, idx, kg, out totalKg);
                if (count == 0)
                {
                    Element element = Grid.Element[cell];
                    float massKg = Grid.Mass[cell];
                    if (element != null && element.IsGas && massKg > 0f)
                    {
                        idx[0] = (ushort)ElementLoader.GetElementIndex(element.id);
                        kg[0] = massKg;
                        count = 1;
                        totalKg = massKg;
                        vanillaFallback = true;
                    }
                }
            }

            Sprite dash = HoverCard.DashIcon;
            TextStyleSetting title = HoverCard.TitleStyle;
            TextStyleSetting body = HoverCard.BodyStyle;
            if (title == null || body == null)
            {
                return;
            }

            drawer.BeginShadowBar();
            drawer.DrawText(dissolved ? LiquidsTitle : GasesTitle, title);

            if (count == 0)
            {
                PipeReadout.DrawProperty(drawer, dash,
                    dissolved ? "Nothing dissolved in this cell" : "No gas in this cell", body);
                drawer.EndShadowBar();
                return;
            }

            if (dissolved)
            {
                float liquidKg = Grid.Mass[cell];
                PipeReadout.DrawProperty(drawer, dash, string.Format(
                    "{0:F3} g of gas in {1:F1} kg of liquid ({2:F2} g/kg)",
                    totalKg * 1000f, liquidKg,
                    liquidKg > 0f ? totalKg / liquidKg * 1000f : 0f), body);
            }
            else
            {
                PipeReadout.DrawProperty(drawer, dash, string.Format(
                    "{0:F3} kg of gas in {1} species{2}",
                    totalKg, count, vanillaFallback ? " (not a mixture cell)" : string.Empty),
                    body);
            }

            // Per-species lines carry BOTH numbers on purpose. The mass is the quantity the
            // player's machinery moves; the mole fraction is the number the tint was computed
            // from, and printing only the first would leave the colour unexplainable for any
            // mixture whose species differ in molar mass -- which is most of them.
            List<Element> elements = ElementLoader.elements;
            float totalMoles = 0f;
            for (int i = 0; i < count; i++)
            {
                int e = idx[i];
                if (e >= 0 && e < elements.Count && elements[e].molarMass > 0f)
                {
                    totalMoles += kg[i] / elements[e].molarMass;
                }
            }
            for (int i = 0; i < count; i++)
            {
                int e = idx[i];
                if (e < 0 || e >= elements.Count)
                {
                    continue;
                }
                float molarMass = elements[e].molarMass;
                float fraction = totalMoles > 0f && molarMass > 0f
                    ? kg[i] / molarMass / totalMoles : 0f;
                PipeReadout.DrawProperty(drawer, dash, string.Format(
                    "{0}: {1} ({2:P1})", PipeReadout.ElementName(e),
                    dissolved ? string.Format("{0:F3} g", kg[i] * 1000f)
                              : string.Format("{0:F3} kg", kg[i]),
                    fraction), body);
            }
            drawer.EndShadowBar();
        }

        // ------------------------------------------------------------------ the menu icons

        // Drawn in code rather than shipped as art: two 32x32 white glyphs in the shape vanilla's
        // own overlay buttons use (a white silhouette on transparent, tinted by the toggle). A
        // sprite asset would mean a texture file, a loader and an atlas entry for forty pixels of
        // line work, and Assets.GetSprite cannot be given a new name from outside the game's own
        // asset load anyway -- KIconToggleMenu.ToggleInfo.getSpriteCB is the documented way past
        // that, and it takes a Sprite we can build here.
        private static Sprite liquidsIcon;
        private static Sprite gasesIcon;

        private const int IconSize = 32;

        public static Sprite LiquidsIcon()
        {
            if (liquidsIcon == null)
            {
                liquidsIcon = BuildIcon(DrawDropletGlyph);
            }
            return liquidsIcon;
        }

        public static Sprite GasesIcon()
        {
            if (gasesIcon == null)
            {
                gasesIcon = BuildIcon(DrawBubblesGlyph);
            }
            return gasesIcon;
        }

        private static Sprite BuildIcon(Func<float, float, float> coverage)
        {
            Texture2D texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false)
            {
                name = "mod1_overlay_icon",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] pixels = new Color32[IconSize * IconSize];
            for (int y = 0; y < IconSize; y++)
            {
                for (int x = 0; x < IconSize; x++)
                {
                    // Pixel centres in a [-1, 1] square, so the glyphs below can be written in
                    // ordinary geometry and stay centred at any size.
                    float u = (x + 0.5f) / IconSize * 2f - 1f;
                    float v = (y + 0.5f) / IconSize * 2f - 1f;
                    float a = Mathf.Clamp01(coverage(u, v));
                    pixels[y * IconSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, IconSize, IconSize),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
        }

        // Antialiasing for every glyph below: one pixel of the [-1,1] square is 2/IconSize wide,
        // so a signed distance is turned into coverage over exactly that much.
        private static float Edge(float signedDistance)
        {
            float pixel = 2f / IconSize;
            return Mathf.Clamp01(0.5f - signedDistance / pixel);
        }

        private static float Ring(float dx, float dy, float radius, float halfWidth)
        {
            float d = Mathf.Sqrt(dx * dx + dy * dy) - radius;
            return Edge(Mathf.Abs(d) - halfWidth);
        }

        private static float Disc(float dx, float dy, float radius)
        {
            return Edge(Mathf.Sqrt(dx * dx + dy * dy) - radius);
        }

        // A droplet with three dots in it: liquid, carrying something. The droplet is a disc with
        // a cone above it, which is the standard construction and needs no curve table.
        private static float DrawDropletGlyph(float u, float v)
        {
            // v up is positive; the droplet's point is at the top.
            float bodyR = 0.52f;
            float bodyY = -0.22f;
            float body = Mathf.Sqrt(u * u + (v - bodyY) * (v - bodyY)) - bodyR;

            // The cone: a line from the tip down to the body's widest point, mirrored in u.
            float tipY = 0.82f;
            float t = Mathf.Clamp01((tipY - v) / (tipY - bodyY));
            float halfWidth = bodyR * t;
            float cone = Mathf.Abs(u) - halfWidth;
            if (v < bodyY)
            {
                cone = 1f;
            }

            float outline = Edge(Mathf.Abs(Mathf.Min(body, cone)) - 0.085f);

            // Three dots inside, the three species a mixture might hold.
            float dots = Disc(u + 0.22f, v - bodyY + 0.14f, 0.095f);
            dots = Mathf.Max(dots, Disc(u - 0.22f, v - bodyY + 0.14f, 0.095f));
            dots = Mathf.Max(dots, Disc(u, v - bodyY - 0.18f, 0.095f));

            return Mathf.Max(outline, dots);
        }

        // Three overlapping rings: a gas cell holding more than one species.
        private static float DrawBubblesGlyph(float u, float v)
        {
            float a = Ring(u + 0.30f, v + 0.26f, 0.38f, 0.085f);
            float b = Ring(u - 0.32f, v + 0.18f, 0.30f, 0.085f);
            float c = Ring(u - 0.02f, v - 0.42f, 0.24f, 0.085f);
            return Mathf.Max(a, Mathf.Max(b, c));
        }
    }
}
