using HarmonyLib;
using OniFramework;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// DUPLICANT BREATHING IN A PROMOTED ROOM. The SimDLL gates the write side of breathing
    /// natively (a consumer in a promoted room draws from the mixture, see
    /// <c>ApplyMassConsumption</c>); this patch is the READ side, the method that decides whether
    /// to consume at all, which would otherwise keep reading vanilla's stale reservoir.
    ///
    /// `GasBreatherFromWorldProvider.GetBreathableCellMass`
    /// is a small private static helper --
    /// <c>elementID = Vacuum; if (Grid.Element[cell].HasTag(Breathable)) { elementID =
    /// element.id; return Grid.Mass[cell]; } return 0f;</c> -- called from both
    /// `HasOxygen()`/`IsLowOxygen()` (via `GetBestBreathableCellAroundSpecificCell`) and
    /// `ConsumeGas()` itself, so patching this one spot covers the whole breathing decision,
    /// not just the display.
    ///
    /// For a promoted cell, this Postfix replaces vanilla's answer with the REAL dominant
    /// breathable species in the mixture -- not a sum across species. That is deliberate, not
    /// an oversight: `ConsumeGas`'s own `SimMessages.ConsumeMass` call only ever removes ONE
    /// element per call (this method's own `elementID` out-param), so reporting a sum across
    /// two breathable species (e.g. Oxygen + ContaminatedOxygen both present) would inflate the
    /// mass a dupe believes is available relative to what a single `ConsumeMass` call could
    /// actually draw down -- the same class of mismatch a naive fix would introduce. Picking
    /// the single heaviest breathable species keeps the reported mass and the consumable mass
    /// the same number, by construction.
    ///
    /// A promoted cell with NO breathable species in its real mixture is reported as truly
    /// unbreathable (Vacuum, 0 kg) -- overriding whatever vanilla's stale `Grid.Element` said,
    /// on purpose. Vanilla's own field stopped being this cell's source of truth the moment it
    /// was promoted; reporting its old, possibly-still-breathable-looking value would let a
    /// duplicant breathe gas the mixture no longer holds.
    ///
    /// Wrapped in try/catch: this runs on every dupe's 200ms breathing tick. A mistake here
    /// must degrade to vanilla's own (possibly wrong, but never crashing) answer.
    /// </summary>
    [HarmonyPatch(typeof(GasBreatherFromWorldProvider), "GetBreathableCellMass")]
    internal static class GasBreatherFromWorldProvider_GetBreathableCellMass
    {
        private static void Postfix(int cell, ref SimHashes elementID, ref float __result)
        {
            try
            {
                if (!GasMixtureFacade.IsRoomOwned(cell))
                {
                    return;
                }

                GasMixtureFacade.GasComponent[] composition = GasMixtureFacade.TryGetComposition(cell);
                ushort bestIdx = 0;
                float bestMass = 0f;
                bool found = false;
                for (int i = 0; i < composition.Length; i++)
                {
                    GasMixtureFacade.GasComponent c = composition[i];
                    Element element = ElementLoader.elements[c.ElementIdx];
                    if (element.HasTag(GameTags.Breathable) && c.MassKg > bestMass)
                    {
                        bestIdx = c.ElementIdx;
                        bestMass = c.MassKg;
                        found = true;
                    }
                }

                if (!found)
                {
                    elementID = SimHashes.Vacuum;
                    __result = 0f;
                    return;
                }

                elementID = ElementLoader.elements[bestIdx].id;
                __result = bestMass;
            }
            catch
            {
                // See class doc comment -- degrade to vanilla's own answer, never throw
                // mid-breath-check.
            }
        }
    }
}
