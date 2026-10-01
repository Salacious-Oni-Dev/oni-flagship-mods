using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// The shared half of the two overlay modes: the legend census, its throttle, and the
    /// change detector that decides when the legend is worth rebuilding.
    ///
    /// A mode's <c>Update</c> runs every frame from <c>OverlayScreen.LateUpdate</c>. The census
    /// is a sweep of the visible rectangle -- the same order of work one frame of the overlay
    /// itself costs -- so it runs on a timer instead, and the legend is only torn down and
    /// rebuilt when what it would say has actually changed. Without the second half the legend
    /// would rebuild its UI objects twice a second forever, on a screen whose whole job is to sit
    /// still and be read.
    /// </summary>
    internal abstract class MixtureOverlayMode : OverlayModes.Mode
    {
        /// <summary>Seconds between censuses. Unscaled: the overlay is a UI, and it should not
        /// update faster because the game is running at 3x or freeze because it is paused.</summary>
        private const float CensusIntervalSeconds = 0.5f;

        private readonly List<MixtureOverlays.Share> shares = new List<MixtureOverlays.Share>();
        private float nextCensusAt;
        private int lastDigest;
        private int mixedCells;
        private float peak;
        private bool everCensused;

        /// <summary>True for the dissolved-gas overlay, false for the gas-mixture one.</summary>
        protected abstract bool Dissolved { get; }

        protected abstract string ScaleLine { get; }

        protected abstract string PeakLine { get; }

        protected abstract string MixedLine { get; }

        public override string GetSoundName()
        {
            // Klei plays a per-overlay stinger by name through GlobalAssets.GetSound, and an
            // unknown name logs. Empty is the documented "no sound" -- UpdateOverlaySounds tests
            // for it explicitly -- and is honest: these two overlays have no sound of their own.
            return "";
        }

        public override void Enable()
        {
            base.Enable();
            MixtureSnapshot.Instance.Retain(Dissolved, !Dissolved);
            // Force the first census on the next frame rather than waiting out the interval, and
            // force the first legend rebuild with it: the publish this Retain asked for is
            // queued, so the legend built during ToggleOverlay itself is necessarily empty.
            nextCensusAt = 0f;
            lastDigest = 0;
            everCensused = false;
        }

        public override void Disable()
        {
            base.Disable();
            MixtureSnapshot.Instance.Release(Dissolved, !Dissolved);
        }

        public override void Update()
        {
            base.Update();
            if (Time.unscaledTime < nextCensusAt)
            {
                return;
            }
            nextCensusAt = Time.unscaledTime + CensusIntervalSeconds;

            MixtureOverlays.Census(Dissolved, shares, out mixedCells, out peak);
            everCensused = true;

            int digest = Digest();
            if (digest == lastDigest)
            {
                return;
            }
            lastDigest = digest;
            if (OverlayLegend.Instance != null)
            {
                OverlayLegend.Instance.SetLegend(this, refreshing: true);
            }
        }

        /// <summary>
        /// What the legend would say, as one integer. Shares are quantised to a whole percent and
        /// the two scalars to what they are actually printed at, so the detector fires exactly
        /// when the printed text would differ and never merely because a float moved.
        /// </summary>
        private int Digest()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + shares.Count;
                for (int i = 0; i < shares.Count; i++)
                {
                    hash = hash * 31 + shares[i].ElementIdx;
                    hash = hash * 31 + Mathf.RoundToInt(shares[i].Fraction * 100f);
                }
                hash = hash * 31 + mixedCells;
                hash = hash * 31 + Mathf.RoundToInt(peak * 100f);
                return hash;
            }
        }

        public override List<LegendEntry> GetCustomLegendData()
        {
            List<LegendEntry> entries = new List<LegendEntry>();

            entries.Add(new LegendEntry(ScaleLine,
                "The concentration a cell has to reach before it paints at full strength. "
                + "Below it the tint fades on the same curve the game's own liquid fill uses.",
                Color.white, null, null, displaySprite: false));

            if (!everCensused)
            {
                return entries;
            }

            entries.Add(new LegendEntry(string.Format(PeakLine, peak),
                "The strongest cell the camera can currently see.",
                Color.white, null, null, displaySprite: false));

            entries.Add(new LegendEntry(string.Format(MixedLine, mixedCells),
                "Cells in view holding more than one species at once. The tint on those is a "
                + "blend of every species present, weighted by its mole fraction -- the same "
                + "rule Stationeers' Atmosphere.GetGasColor uses.",
                Color.white, null, null, displaySprite: false));

            if (shares.Count == 0)
            {
                entries.Add(new LegendEntry("Nothing in view",
                    "No cell the camera can see carries anything this overlay draws.",
                    Color.white, null, null, displaySprite: false));
                return entries;
            }

            List<Element> elements = ElementLoader.elements;
            for (int i = 0; i < shares.Count; i++)
            {
                MixtureOverlays.Share share = shares[i];
                if (share.ElementIdx < 0 || share.ElementIdx >= elements.Count)
                {
                    continue;
                }
                Element element = elements[share.ElementIdx];
                Color colour = element.substance != null
                    ? (Color)element.substance.uiColour : Color.white;
                colour.a = 1f;
                entries.Add(new LegendEntry(
                    string.Format("{0} -- {1:P1}", element.name, share.Fraction),
                    string.Format(
                        "{0:F3} kg in view, {1:P1} of everything in view by mole fraction. "
                        + "This is the colour it contributes to the blend.",
                        share.Kg, share.Fraction),
                    colour));
            }
            return entries;
        }
    }

    /// <summary>
    /// GAS DISSOLVED IN LIQUID -- dissolved gas made visible. Tints every liquid cell by what is
    /// dissolved in it: the hue is the mixture of dissolved species, the strength is the
    /// concentration in grams per kilogram of liquid.
    /// </summary>
    internal sealed class MixedLiquidsOverlayMode : MixtureOverlayMode
    {
        protected override bool Dissolved
        {
            get { return true; }
        }

        protected override string ScaleLine
        {
            get
            {
                return string.Format("Full strength at {0:F1} g/kg",
                    MixtureOverlays.LiquidFullScaleGPerKg);
            }
        }

        protected override string PeakLine
        {
            get { return "Strongest cell in view: {0:F2} g/kg"; }
        }

        protected override string MixedLine
        {
            get { return "Cells carrying 2+ dissolved gases: {0}"; }
        }

        public override HashedString ViewMode()
        {
            return MixtureOverlays.MixedLiquidsID;
        }
    }

    /// <summary>
    /// GAS MIXTURE PER CELL -- the volume-fractions layer made visible. Tints every gas cell by
    /// its composition: the hue is the mixture of species, the strength is the cell's total gas
    /// mass. A cell the mixture layer does not own still paints, from its single vanilla element;
    /// it is a mixture of one, and the contrast with a promoted cell is the whole demonstration.
    /// </summary>
    internal sealed class MixedGasesOverlayMode : MixtureOverlayMode
    {
        protected override bool Dissolved
        {
            get { return false; }
        }

        protected override string ScaleLine
        {
            get
            {
                return string.Format("Full strength at {0:F1} kg per cell",
                    MixtureOverlays.GasFullScaleKg);
            }
        }

        protected override string PeakLine
        {
            get { return "Heaviest cell in view: {0:F2} kg"; }
        }

        protected override string MixedLine
        {
            get { return "Cells holding 2+ species: {0}"; }
        }

        public override HashedString ViewMode()
        {
            return MixtureOverlays.MixedGasesID;
        }
    }

    /// <summary>
    /// Where the two modes are wired into the four vanilla objects that have to know about them.
    /// All four are postfixes on private members, which is what appending to a closed list costs;
    /// none of them changes anything vanilla already put there.
    /// </summary>
    internal static class MixtureOverlayRegistration
    {
        internal static readonly MixedLiquidsOverlayMode Liquids = new MixedLiquidsOverlayMode();
        internal static readonly MixedGasesOverlayMode Gases = new MixedGasesOverlayMode();
    }

    /// <summary>
    /// 1 of 4: the MODE TABLE. <c>OverlayScreen.ToggleOverlay</c> looks the requested view mode up
    /// in <c>modeInfos</c> and falls back to <c>None</c> when it misses, so without this the
    /// button would light up and nothing would happen.
    ///
    /// <c>ModeInfo</c> is a private nested STRUCT, so it is created boxed and its one field is set
    /// through reflection; assigning the box back into the dictionary unboxes it. The dictionary
    /// is reached as the non-generic <c>IDictionary</c> for the same reason -- its value type
    /// cannot be named from outside the assembly.
    /// </summary>
    [HarmonyPatch(typeof(OverlayScreen), "RegisterModes")]
    internal static class OverlayScreen_RegisterModes
    {
        private static void Postfix(OverlayScreen __instance)
        {
            try
            {
                Type modeInfoType = AccessTools.Inner(typeof(OverlayScreen), "ModeInfo");
                FieldInfo modeField = modeInfoType == null
                    ? null : AccessTools.Field(modeInfoType, "mode");
                FieldInfo infosField = AccessTools.Field(typeof(OverlayScreen), "modeInfos");
                if (modeInfoType == null || modeField == null || infosField == null)
                {
                    Mod1Log.Warn("MixtureOverlays: OverlayScreen has moved -- no mode table "
                        + "(ModeInfo/mode/modeInfos). The two overlays will not open.");
                    return;
                }
                IDictionary infos = infosField.GetValue(__instance) as IDictionary;
                if (infos == null)
                {
                    return;
                }
                Add(infos, modeInfoType, modeField, MixtureOverlayRegistration.Liquids);
                Add(infos, modeInfoType, modeField, MixtureOverlayRegistration.Gases);
            }
            catch (Exception e)
            {
                Mod1Log.Warn("MixtureOverlays: could not register the overlay modes: " + e);
            }
        }

        private static void Add(IDictionary infos, Type modeInfoType, FieldInfo modeField,
            OverlayModes.Mode mode)
        {
            object boxed = Activator.CreateInstance(modeInfoType);
            modeField.SetValue(boxed, mode);
            infos[mode.ViewMode()] = boxed;
        }
    }

    /// <summary>
    /// 2 of 4: the BUTTONS. Appended to <c>overlayToggleInfos</c> after
    /// <c>OverlayMenu.InitializeToggles</c> has filled it and before <c>Setup</c> reads it.
    ///
    /// <c>OverlayToggleInfo</c> is a private nested class and <c>RefreshButtons</c> casts every
    /// entry of the list to it unconditionally, so a mod cannot substitute its own subclass -- the
    /// real type is constructed through its own constructor instead.
    ///
    /// No hotkey: vanilla's <c>Action</c> enum stops at <c>Overlay15</c> and every one of them is
    /// already taken, so both take <c>Action.NumActions</c>, which is what the base
    /// <c>ToggleInfo</c> constructor defaults to and what
    /// <c>GameUtil.ReplaceHotkeyString</c> renders as no key.
    /// </summary>
    [HarmonyPatch(typeof(OverlayMenu), "InitializeToggles")]
    internal static class OverlayMenu_InitializeToggles
    {
        private static void Postfix(OverlayMenu __instance)
        {
            try
            {
                Type infoType = AccessTools.Inner(typeof(OverlayMenu), "OverlayToggleInfo");
                ConstructorInfo ctor = infoType == null ? null : AccessTools.Constructor(infoType,
                    new Type[]
                    {
                        typeof(string), typeof(string), typeof(HashedString), typeof(string),
                        typeof(global::Action), typeof(string), typeof(string)
                    });
                FieldInfo listField = AccessTools.Field(typeof(OverlayMenu), "overlayToggleInfos");
                if (ctor == null || listField == null)
                {
                    Mod1Log.Warn("MixtureOverlays: OverlayMenu has moved -- no toggle list "
                        + "(OverlayToggleInfo/overlayToggleInfos). The two overlays will have "
                        + "no buttons.");
                    return;
                }
                List<KIconToggleMenu.ToggleInfo> list =
                    listField.GetValue(__instance) as List<KIconToggleMenu.ToggleInfo>;
                if (list == null)
                {
                    return;
                }

                list.Add(Build(ctor, MixtureOverlays.LiquidsButton,
                    MixtureOverlays.MixedLiquidsID, MixtureOverlays.LiquidsTooltip,
                    MixtureOverlays.LiquidsIcon));
                list.Add(Build(ctor, MixtureOverlays.GasesButton,
                    MixtureOverlays.MixedGasesID, MixtureOverlays.GasesTooltip,
                    MixtureOverlays.GasesIcon));
            }
            catch (Exception e)
            {
                Mod1Log.Warn("MixtureOverlays: could not append the overlay buttons: " + e);
            }
        }

        private static KIconToggleMenu.ToggleInfo Build(ConstructorInfo ctor, string text,
            HashedString view, string tooltip, Func<Sprite> icon)
        {
            KIconToggleMenu.ToggleInfo info = (KIconToggleMenu.ToggleInfo)ctor.Invoke(
                new object[] { text, null, view, "", global::Action.NumActions, tooltip, text });
            // Setup prefers getSpriteCB over the icon NAME, which is the only way in: a sprite
            // this mod builds at runtime is not in Assets' own name table and never can be.
            info.getSpriteCB = icon;
            return info;
        }
    }

    /// <summary>
    /// 3 of 4: the LEGEND. <c>OverlayLegend.SetLegend</c> finds its panel by looking the view mode
    /// up in the serialized <c>overlayInfoList</c>; a mode with no entry there gets
    /// <c>ClearLegend</c> and no panel at all.
    ///
    /// Injected in a POSTFIX of <c>OnSpawn</c> rather than a prefix, because vanilla's own
    /// <c>OnSpawn</c> walks the list running <c>Strings.Get</c> over every name and every info
    /// unit -- an entry added before that would have its literal title looked up as a string key,
    /// and an entry with a null <c>infoUnits</c> would take the loop down with a null reference.
    ///
    /// <c>isProgrammaticallyPopulated</c> is what routes the panel through
    /// <c>PopulateGeneratedLegend</c>, which is the path that calls
    /// <c>Mode.GetCustomLegendData</c>. Without it a legend with no static info units is treated
    /// as empty and cleared.
    /// </summary>
    [HarmonyPatch(typeof(OverlayLegend), "OnSpawn")]
    internal static class OverlayLegend_OnSpawn
    {
        private static void Postfix(OverlayLegend __instance)
        {
            try
            {
                if (OverlayLegend.Instance != __instance)
                {
                    // A duplicate legend destroys itself in OnSpawn; do not add to it.
                    return;
                }
                FieldInfo listField = AccessTools.Field(typeof(OverlayLegend), "overlayInfoList");
                List<OverlayLegend.OverlayInfo> list = listField == null
                    ? null : listField.GetValue(__instance) as List<OverlayLegend.OverlayInfo>;
                if (list == null)
                {
                    Mod1Log.Warn("MixtureOverlays: OverlayLegend has moved -- no overlayInfoList. "
                        + "The two overlays will open with no legend.");
                    return;
                }
                list.Add(Info(MixtureOverlays.MixedLiquidsID, MixtureOverlays.LiquidsTitle));
                list.Add(Info(MixtureOverlays.MixedGasesID, MixtureOverlays.GasesTitle));
            }
            catch (Exception e)
            {
                Mod1Log.Warn("MixtureOverlays: could not add the overlay legends: " + e);
            }
        }

        private static OverlayLegend.OverlayInfo Info(HashedString mode, string name)
        {
            return new OverlayLegend.OverlayInfo
            {
                name = name,
                mode = mode,
                // Both lists are walked without a null check on several paths, so they are empty
                // rather than null. Everything this legend shows comes from GetCustomLegendData.
                infoUnits = new List<OverlayLegend.OverlayInfoUnit>(),
                diagrams = new List<GameObject>(),
                isProgrammaticallyPopulated = true
            };
        }
    }

    /// <summary>
    /// 4 of 4: the COLOURS. <c>SimDebugView.UpdateSimViewWorkItem.Run</c> looks the view mode up
    /// in <c>getColourFuncs</c> and paints the whole visible world black when it misses, so this
    /// is what makes the overlay draw anything.
    ///
    /// The dictionary's type is nameable from outside (both its key and its value are public), so
    /// unlike the other three this one needs reflection only to reach the private FIELD.
    ///
    /// Nothing is added to <c>dataUpdateFuncs</c>: its fallback is <c>SetDefaultPoint</c>, which
    /// is the filtering these two want. Bilinear is right for a field that is continuous between
    /// cells, like temperature; a dissolved concentration is a property OF a cell, and the
    /// question the overlay answers is "how much is in THIS tile", so the cell edges should stay
    /// where the cells are.
    /// </summary>
    [HarmonyPatch(typeof(SimDebugView), "OnPrefabInit")]
    internal static class SimDebugView_OnPrefabInit
    {
        private static void Postfix(SimDebugView __instance)
        {
            try
            {
                FieldInfo field = AccessTools.Field(typeof(SimDebugView), "getColourFuncs");
                Dictionary<HashedString, Func<SimDebugView, int, Color>> funcs = field == null
                    ? null
                    : field.GetValue(__instance)
                        as Dictionary<HashedString, Func<SimDebugView, int, Color>>;
                if (funcs == null)
                {
                    Mod1Log.Warn("MixtureOverlays: SimDebugView has moved -- no getColourFuncs. "
                        + "The two overlays would paint the world black, so they are left "
                        + "unregistered.");
                    return;
                }
                funcs[MixtureOverlays.MixedLiquidsID] = MixtureOverlays.LiquidColour;
                funcs[MixtureOverlays.MixedGasesID] = MixtureOverlays.GasColour;
            }
            catch (Exception e)
            {
                Mod1Log.Warn("MixtureOverlays: could not register the overlay colours: " + e);
            }
        }
    }
}
