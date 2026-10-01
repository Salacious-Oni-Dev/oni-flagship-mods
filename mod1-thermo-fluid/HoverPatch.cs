using System.Collections.Generic;
using HarmonyLib;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// The real vanilla-UI read site this mod has deferred twice already (see Patches.cs and
    /// README.md's earlier "Not done" note): gas-mixture cells now show up in Klei's own
    /// hover tooltip, not just this mod's standalone overlay.
    ///
    /// The blocker that killed the first attempt (patching
    /// SelectToolHoverTextCard.UpdateHoverElements) was real: that method calls
    /// hoverTextDrawer.EndDrawing() itself before returning, finalizing the drawer, so a
    /// Postfix on it has nothing left to draw into.
    ///
    /// The fix: patch HoverTextDrawer.EndDrawing() (the outer, parameterless one -- not
    /// HoverTextDrawer.Skin.Pool's own EndDrawing, a different nested type) with a PREFIX
    /// instead. Its body starts by finalizing four widget pools
    /// (shadowBars/iconWidgets/textWidgets/selectBorders); a Prefix runs before any of that,
    /// while the drawer -- HoverTextScreen.Instance.drawer, the same object __instance is
    /// here -- is still writable. Whichever tool's hover card is active this frame has
    /// already made all of its own BeginShadowBar/DrawText/EndShadowBar calls into that same
    /// drawer by the time EndDrawing runs, so appending one more shadow bar here just adds
    /// another line to whatever's already on screen.
    ///
    /// Tradeoff accepted for this slice: EndDrawing runs for every tool's hover card, not
    /// just the select tool, so this line can appear regardless of which tool is active --
    /// there is no cheap way to ask "is the select tool the active one" from inside this
    /// method without walking PlayerController.Instance.ActiveTool, which is a bigger check
    /// than this slice needs. Gated entirely on "is there gas-mixture state under the
    /// cursor right now" instead, which is the actually-interesting condition.
    ///
    /// TextStyleSetting instances aren't reachable statically -- HoverTextConfiguration's
    /// Styles_Title/Styles_BodyText fields are serialized per-prefab-instance, not static --
    /// so this finds the live SelectToolHoverTextCard component once (it's a scene singleton
    /// in practice) and reuses its style fields, the same ones Klei's own hover cards draw
    /// with (see the DrawText call sites in that class).
    ///
    /// Wrapped in try/catch: this patches a method that runs every frame for every tool, so a
    /// mistake here must degrade to "no extra line," never to a broken tooltip for the whole
    /// game.
    /// </summary>
    /// <summary>
    /// Replaces vanilla's hover card with an empty one while a narrated run has pinned the
    /// readout (<see cref="DemoNarrator.PinHoverCard"/>), leaving this mod's own shadow bar as
    /// the only card on screen.
    ///
    /// WHY NOT JUST SKIP THE METHOD. <c>UpdateHoverElements</c> owns the whole card.
    /// It calls <c>HoverTextScreen.Instance.BeginDrawing()</c>, draws about twenty
    /// <c>BeginShadowBar</c>/<c>EndShadowBar</c> blocks, and calls <c>EndDrawing()</c> itself as
    /// its last statement. <see cref="HoverTextDrawer_EndDrawing"/> -- the whole reason this mod
    /// appears in the vanilla tooltip at all -- is a Prefix on that <c>EndDrawing</c>. A Prefix
    /// here that simply returned false would therefore delete this mod's readout along with
    /// vanilla's boxes. Issuing the bare <c>BeginDrawing()</c>/<c>EndDrawing()</c> pair keeps the
    /// hook point intact and drops only the content.
    ///
    /// Suppressing ONE of vanilla's boxes instead (the ambient-element one is noise; the conduit
    /// one is a genuinely useful contrast) is not reachable without reaching into the middle of a
    /// 650-line method. The contrast is kept, better, by DemoNarrator's own comparison panel,
    /// which can also state the per-tile-versus-per-network difference that made the two stacked
    /// cards read as a contradiction.
    ///
    /// Off unless a demo has explicitly pinned the card, so ordinary play and every un-narrated
    /// session keep vanilla's tooltip exactly as it was.
    /// </summary>
    /// <summary>
    /// oni-mod-compat decision 0001 part 3:
    /// <c>[HarmonyPriority(Priority.First)]</c> so that, during a capture run
    /// (<see cref="DemoNarrator.DemoSuppressVanillaHoverCard"/> pinned), this always wins before
    /// any tooltip mod's own prefix on the same method -- deterministic demo output regardless
    /// of load order or what else is subscribed. Free in ordinary play: the guard below still
    /// returns <c>true</c> there, so priority never matters outside a narrated run.
    /// </summary>
    [HarmonyPatch(typeof(SelectToolHoverTextCard), nameof(SelectToolHoverTextCard.UpdateHoverElements))]
    [HarmonyPriority(Priority.First)]
    internal static class SelectToolHoverTextCard_UpdateHoverElements
    {
        private static bool Prefix()
        {
            if (!DemoNarrator.DemoSuppressVanillaHoverCard)
            {
                return true;
            }

            try
            {
                if (HoverTextScreen.Instance != null)
                {
                    HoverTextScreen.Instance.BeginDrawing().EndDrawing();
                }
            }
            catch
            {
                // Same rule as the readout below: this runs every frame, so a mistake here must
                // cost a tooltip, never the game.
            }
            return false;
        }
    }

    /// <summary>
    /// oni-mod-compat decision 0001 part 4: this used to be Mod 1's own Prefix on
    /// <c>HoverTextDrawer.EndDrawing</c> (see git history for the version with its own
    /// <c>[HarmonyPatch]</c>, priority, drawer-identity guard, cell resolution and
    /// <c>DebugForcedCell</c>/<c>DebugForcedCells</c>). All of that is now generic and lives in
    /// <see cref="OniFramework.HoverCard"/> -- the framework owns the one prefix on
    /// <c>EndDrawing</c>, resolves which cell every registered section describes (real cursor or
    /// a narrated run's pinned cells), and drains registered sections in order. This class is now
    /// just the one section Mod 1 registers with it: <see cref="Install"/>, called from
    /// <c>Mod.cs</c>, is the only thing that runs at load; <see cref="AppliesTo"/> and
    /// <see cref="Draw"/> are the two delegates <c>HoverCard.AddSection</c> asks for.
    ///
    /// Kept as its own class (rather than an anonymous lambda at the call site) so the readout
    /// logic -- unchanged from the original Prefix, tank/liquid/pipe/gas-mixture blocks and all
    /// -- stays exactly as readable as it always was, and so this file's own
    /// <c>SelectToolHoverTextCard</c> style-source caching (needed because
    /// <see cref="PipeReadout.Draw"/> takes the whole styles object, not just one field) stays
    /// local to the mod that uses it.
    /// </summary>
    internal static class HoverTextDrawer_EndDrawing
    {
        private static SelectToolHoverTextCard styleSource;

        /// <summary>
        /// Registers this mod's hover-card section with the framework. Idempotent the same way
        /// every other <c>Install(harmony)</c> in this mod is: call it once from <c>Mod.cs</c>.
        /// </summary>
        internal static void Install()
        {
            OniFramework.HoverCard.AddSection("Mod1ThermoFluid", AppliesTo, Draw);
        }

        /// <summary>
        /// Exactly the condition the original <c>TryResolvePinnedCell</c> used to pick which
        /// pinned candidate to resolve to -- moved here unchanged because it names Mod 1's own
        /// component types (<see cref="GasMixtureTankComponent"/>,
        /// <see cref="LiquidMixtureTankComponent"/>), which <c>OniFramework.HoverCard</c> must
        /// not depend on. Doubles as the real-cursor gate: a cell nothing here applies to costs
        /// one call into this method, not a full <see cref="Draw"/>.
        /// </summary>
        private static bool AppliesTo(int cell)
        {
            return GasMixtureTankComponent.AtCell(cell) != null
                || LiquidMixtureTankComponent.AtCell(cell) != null
                || Game.Instance?.gasConduitFlow?.HasConduit(cell) == true
                || Game.Instance?.liquidConduitFlow?.HasConduit(cell) == true
                || GasMixtureFacade.TryGetComposition(cell).Length > 0;
        }

        private static void Draw(HoverTextDrawer __instance, int cell)
        {
            try
            {
                if (styleSource == null)
                {
                    styleSource = Object.FindFirstObjectByType<SelectToolHoverTextCard>();
                    if (styleSource == null)
                    {
                        return;
                    }
                }

                // iconDash/iconWarning are [NonSerialized] on SelectToolHoverTextCard and only
                // filled in by ConfigureHoverScreen. Vanilla's own UpdateHoverElements opens
                // with exactly this guard ("if (iconWarning == null) ConfigureHoverScreen()"),
                // because the card can be handed out before it has ever drawn. Copied rather
                // than assumed: every readout below draws its lines with those sprites, and a
                // null one silently degrades to a bare indent.
                if (styleSource.iconDash == null && HoverTextScreen.Instance != null)
                {
                    styleSource.ConfigureHoverScreen();
                }

                // Compressor/tank mechanic (Compressor.cs): the Reservoir's storage is its own
                // isolated managed state, not a cell's
                // gas-mixture data -- AtCell finds it from ANYWHERE in the building's real
                // (5x3) footprint, not just one anchor tile. Checked first/separately from the
                // generic per-cell facade below, which is a different mechanic (the sensor/
                // vent round trip) reading real per-cell mixture data.
                GasMixtureTankComponent tank = GasMixtureTankComponent.AtCell(cell);
                if (tank != null)
                {
                    KeyValuePair<int, float>[] tankComposition = tank.Composition();
                    if (tankComposition.Length > 0)
                    {
                        __instance.BeginShadowBar();
                        __instance.DrawText("Gas Reservoir Tank (Mod 1)".ToUpperInvariant(), styleSource.Styles_Title.Standard);
                        foreach (KeyValuePair<int, float> c in tankComposition)
                        {
                            PipeReadout.DrawProperty(__instance, styleSource.iconDash,
                                $"{PipeReadout.ElementName(c.Key)}: {c.Value:F2} kg",
                                styleSource.Styles_BodyText.Standard);
                        }
                        if (tank.TryGetPressurePa(out float tankPressurePa))
                        {
                            PipeReadout.DrawProperty(__instance, styleSource.iconDash,
                                $"Pressure: {PipeReadout.FormatPressure(tankPressurePa)}",
                                styleSource.Styles_BodyText.Standard);
                        }
                        __instance.EndShadowBar();
                    }
                    return;
                }

                // Liquid counterpart (LiquidCompressor.cs): same AtCell lookup
                // shape, fill fraction instead of pressure (liquids are volume-tracked, not
                // ideal-gas-pressure -- see LiquidMixtureTankComponent's own doc comment).
                LiquidMixtureTankComponent liquidTank = LiquidMixtureTankComponent.AtCell(cell);
                if (liquidTank != null)
                {
                    KeyValuePair<int, float>[] liquidComposition = liquidTank.Composition();
                    if (liquidComposition.Length > 0)
                    {
                        __instance.BeginShadowBar();
                        __instance.DrawText("Liquid Reservoir Tank (Mod 1)".ToUpperInvariant(), styleSource.Styles_Title.Standard);
                        foreach (KeyValuePair<int, float> c in liquidComposition)
                        {
                            PipeReadout.DrawProperty(__instance, styleSource.iconDash,
                                $"{PipeReadout.ElementName(c.Key)}: {c.Value:F2} kg",
                                styleSource.Styles_BodyText.Standard);
                        }
                        if (liquidTank.TryGetFillFraction(out float fillFraction))
                        {
                            PipeReadout.DrawProperty(__instance, styleSource.iconDash,
                                $"Fill: {fillFraction * 100f:F0}%", styleSource.Styles_BodyText.Standard);
                        }
                        __instance.EndShadowBar();
                    }
                    return;
                }

                // FULL STATIONEERS PIPE READOUT. A per-tile fill percentage or pressure answers
                // for ONE TILE; Stationeers reports at the NETWORK, because
                // its Atmosphere belongs to the PipeNetwork rather than to any single pipe. The
                // whole line set, the mole/percent contents breakdown, and the stress warnings
                // now come from OniFramework.PipeNetworkFacade -- see PipeReadout.Draw.
                //
                // Not gated behind the tank checks above: a conduit is a different object layer
                // than a room cell, so a pipe readout and a gas-mixture readout can and do
                // coexist on the same cell, exactly as the old per-tile blocks did.
                if (PipeNetworkFacade.TryGetNetworkState(cell, PipeContentType.Liquid,
                    out PipeNetworkState liquidNetwork))
                {
                    PipeReadout.Draw(__instance, styleSource, "Liquid Pipe (Mod 1)", liquidNetwork);
                }

                if (PipeNetworkFacade.TryGetNetworkState(cell, PipeContentType.Gas,
                    out PipeNetworkState gasNetwork))
                {
                    PipeReadout.Draw(__instance, styleSource, "Gas Pipe (Mod 1)", gasNetwork);
                }

                GasMixtureFacade.GasComponent[] composition = GasMixtureFacade.TryGetComposition(cell);
                if (composition.Length == 0)
                {
                    return;
                }

                __instance.BeginShadowBar();
                __instance.DrawText("Gas Mixture (Mod 1)".ToUpperInvariant(), styleSource.Styles_Title.Standard);
                foreach (GasMixtureFacade.GasComponent c in composition)
                {
                    PipeReadout.DrawProperty(__instance, styleSource.iconDash,
                        $"{PipeReadout.ElementName(c.ElementIdx)}: {c.MassKg:F2} kg",
                        styleSource.Styles_BodyText.Standard);
                }
                // Sensor/vent mechanic's readout -- only meaningful once a real reading exists,
                // same TryGetPressure/-1-sentinel collapse the overlay uses.
                if (GasMixtureFacade.TryGetPressure(cell, out float pressurePa))
                {
                    PipeReadout.DrawProperty(__instance, styleSource.iconDash,
                        $"Pressure: {PipeReadout.FormatPressure(pressurePa)}",
                        styleSource.Styles_BodyText.Standard);
                }
                __instance.EndShadowBar();
            }
            catch
            {
                // See class doc comment -- this runs every frame for every tool, degrade
                // silently rather than break vanilla's own tooltip.
            }
        }
    }
}
