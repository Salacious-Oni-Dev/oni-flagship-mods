using HarmonyLib;
using OniFramework;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// The hover card's mass line for a promoted cell, which would otherwise show vanilla's
    /// stale mass (e.g. "3225.6g") beside a mixture holding 0.01 kg. Patched at its real choke
    /// point rather than in the giant method that draws the card (see HoverPatch.cs's own doc
    /// comment on <c>SelectToolHoverTextCard.UpdateHoverElements</c>
    /// finalizing its drawer before a Postfix could run).
    ///
    /// <c>UpdateHoverElements</c> draws the mass
    /// line via one small static helper, <c>HoverTextHelper.MassStringsReadOnly(cell)</c> --
    /// reads <c>Grid.Element[cell]</c>/<c>Grid.Mass[cell]</c> directly, returns a reused
    /// 4-string array (`[0]` integer part, `[1]` ".decimal", `[2]` unit suffix, `[3]` the
    /// breathable-string, space-prefixed). A Postfix here runs regardless of whether the
    /// original hit its own element/mass equality cache and returned early -- Harmony
    /// postfixes wrap every return path -- so that cache is not a concern for this patch.
    ///
    /// For a promoted cell, this substitutes the real mixture total
    /// (<see cref="GasMixtureFacade.TryGetComposition"/>) for vanilla's frozen
    /// <c>Grid.Mass</c> reading -- items 1/3's own gates are exactly why that field stops
    /// being current the moment a room is promoted. The breathable-string slot reuses
    /// vanilla's own <c>GameUtil.GetBreathableString</c> tiering (positive/warning/etc.)
    /// against the mixture's dominant species and the REAL total mass, rather than
    /// reinventing that ladder.
    ///
    /// Deliberately NOT a full fix for the hover card, and this is not glossed over:
    ///
    ///   * The cell name/category lines above this one (<c>element.nameUpperCase</c>,
    ///     <c>GetMaterialCategoryTag()</c>) are drawn directly in `UpdateHoverElements`
    ///     from vanilla `Grid.Element[cell]`, not through this helper -- still frozen/stale
    ///     for a promoted cell. Fixing those needs patching `UpdateHoverElements` itself,
    ///     the big method this slice is explicitly avoiding.
    ///   * `UpdateHoverElements` also gates whether it draws indices 0-2 of this array AT
    ///     ALL on vanilla `element.IsVacuum` (its own local, from the same stale
    ///     `Grid.Element[cell]`) -- a room promoted while its vanilla element still read
    ///     Vacuum would have its mass line suppressed by that outer check regardless of what
    ///     this patch puts in the array. Only index 3 (breathable string) would still show.
    ///   * Not live-tested -- same precedent as everything else in this mod so far (see
    ///     README.md); this is a UI patch, so an offline suite run cannot verify it and this
    ///     comment does not claim otherwise.
    ///
    /// Wrapped in try/catch: this runs on every hover, for every cell -- a mistake here must
    /// degrade to "vanilla's own (stale) numbers show," never to a broken tooltip.
    /// </summary>
    [HarmonyPatch(typeof(HoverTextHelper), nameof(HoverTextHelper.MassStringsReadOnly))]
    internal static class HoverTextHelper_MassStringsReadOnly
    {
        private static void Postfix(int cell, ref string[] __result)
        {
            try
            {
                if (!GasMixtureFacade.IsRoomOwned(cell))
                {
                    return;
                }
                if (!GasMixtureFacade.TryGetDominant(cell, out ushort dominantIdx, out float totalMassKg))
                {
                    return;
                }

                Element dominant = ElementLoader.elements[dominantIdx];
                __result = new string[4];
                __result[0] = GameUtil.GetFormattedMass(totalMassKg);
                __result[1] = "";
                __result[2] = "";
                __result[3] = " " + GameUtil.GetBreathableString(dominant, totalMassKg);
            }
            catch
            {
                // See class doc comment -- runs every hover frame, degrade silently.
            }
        }
    }
}
