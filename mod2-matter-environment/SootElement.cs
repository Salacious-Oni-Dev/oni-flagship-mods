using OniFramework;

namespace Mod2MatterEnvironment
{
    /// <summary>
    /// SOOT -- a solid element: the carbon a smoky fire did not burn, once it is something a
    /// duplicant can pick up. Mod 2 registers it. (The smoke system came from an idea by Kaz.) Nothing makes it yet: the
    /// airborne soot and the deposit it settles into are numbers on cells, and turning a scrubbed
    /// deposit into Soot debris is planned. It is in the fuel table already
    /// (<see cref="CombustionFuels"/>), so what is collected then can be burned.
    ///
    /// WHAT IT IS, physically: amorphous carbon, a fine powder. Molar mass and specific heat are
    /// carbon's (12.011 g/mol, 0.71 J/(g K), graphite's). Thermal conductivity is a CHOICE: a loose
    /// powder conducts far less than solid carbon, so 0.2 W/(m K), between ONI's Sand-like debris
    /// and an insulator. Its melt is Klei's own carbon melt -- Refined Carbon goes to Molten
    /// Carbon at 4600 K, and so does this -- and Molten Carbon freezes back to Refined Carbon, so
    /// Soot never becomes a world tile, which is the condition
    /// <see cref="ElementRegistry.RegisterSolid"/> sets.
    ///
    /// Its chunks borrow Refined Carbon's art, so a pile of soot looks like carbon and keeps its own
    /// colour; real art is a kanim-port-list item.
    /// </summary>
    internal static class SootElement
    {
        internal const string Id = "Soot";

        internal static readonly SimHashes Hash = (SimHashes)global::Hash.SDBMLower(Id);

        internal static void Register()
        {
            ElementRegistry.AddStrings(Id, "Soot",
                "Fine carbon left behind by a fire that did not get enough air. Blackens walls, "
                + "clogs filters, and burns the rest of the way given the chance.");
            ElementRegistry.RegisterSolid(
                elementId: Id,
                localizationID: "STRINGS.ELEMENTS.SOOT.NAME",
                specificHeatCapacity: 0.71f,
                thermalConductivity: 0.2f,
                molarMass: 12.011f,
                meltsTo: "MoltenCarbon",
                meltingPointK: 4600f,
                hardness: 1f,
                strength: 0.1f,
                materialCategory: "ConsumableOre");
            ElementRegistry.SetArtDonor(Id, SimHashes.RefinedCarbon);
        }
    }
}
