using System.Collections.Generic;

namespace Mod2MatterEnvironment
{
    /// <summary>
    /// WHAT A FRUIT IS MADE OF, for mashing. Per kilogram of fruit: the water that
    /// becomes the mash's water, the sugar that rides it (<see cref="Fermentables"/>), and the pulp
    /// -- everything else, fibre and the other solids -- which drops as Polluted Dirt.
    ///
    /// WHY POLLUTED DIRT. Klei's own precedent for organic process waste: the Ethanol Distillery's
    /// solid by-product is ToxicSand (EthanolDistilleryConfig: 1 kg wood -> 1/3 kg ToxicSand).
    ///
    /// WHERE THE NUMBERS COME FROM. ONI's fruits are invented, so each takes the composition of
    /// a real fruit it resembles, per 100 g, from USDA FoodData Central SR Legacy (water, and
    /// "Sugars, total including NLEA"). Every vanilla fruit item is 1 kg per unit
    /// (CreateLooseEntity mass 1f in each *Config), so these fractions are per unit too.
    /// <list type="table">
    /// <item>Bristle Berry (PrickleFruit) -- blueberries, raw, FDC 171711: water 84.2, sugars 9.96</item>
    /// <item>Bog Jelly (SwampFruit) -- litchis, raw, FDC 169086: water 81.8, sugars 15.2</item>
    /// <item>Grubfruit (WormSuperFruit) -- grapes, red or green, raw, FDC 174683: water 80.5, sugars 15.5</item>
    /// <item>Spindly Grubfruit (WormBasicFruit) -- pears, raw, FDC 169118: water 84.0, sugars 9.8</item>
    /// <item>Pikeapple (HardSkinBerry) -- pineapple, raw, FDC 169124: water 86.0, sugars 9.85</item>
    /// <item>Ovagro Fig (VineFruit) -- figs, raw, FDC 173021: water 79.1, sugars 16.3</item>
    /// </list>
    /// The analogue for each is a judgement (berry for berry, a jelly-fleshed fruit for Bog Jelly,
    /// a fig for a fig); the composition of the analogue is not.
    ///
    /// STATIONEERS: its per-crop data table is <c>AlcoholPerUnit</c> (plants.xml, read by
    /// <c>Plant.GenerateSpawnGases</c>): alcohol straight out of the Fermenter, with no sugar step.
    /// Ours is sugar per crop, and the alcohol comes from fermenting it. Same shape of
    /// table, different quantity, by choice.
    ///
    /// Grains, vegetables and mushrooms are not here: their carbohydrate is starch, not sugar, and
    /// starch needs a conversion step before it ferments. A later slice.
    /// </summary>
    internal static class FruitSugar
    {
        internal struct Composition
        {
            public float Water;
            public float Sugar;

            /// <summary>Everything that is neither water nor sugar.</summary>
            public float Pulp
            {
                get { return 1f - Water - Sugar; }
            }

            public string Source;
        }

        private static readonly Dictionary<Tag, Composition> table = new Dictionary<Tag, Composition>
        {
            { PrickleFruitConfig.ID, Row(0.842f, 0.0996f, "blueberries, raw (USDA FDC 171711)") },
            { SwampFruitConfig.ID, Row(0.818f, 0.152f, "litchis, raw (USDA FDC 169086)") },
            { "WormSuperFruit", Row(0.805f, 0.155f, "grapes, red or green, raw (USDA FDC 174683)") },
            { "WormBasicFruit", Row(0.840f, 0.098f, "pears, raw (USDA FDC 169118)") },
            { "HardSkinBerry", Row(0.860f, 0.0985f, "pineapple, raw (USDA FDC 169124)") },
            { VineFruitConfig.ID, Row(0.791f, 0.163f, "figs, raw (USDA FDC 173021)") },
        };

        /// <summary>Klei's SugarWater is 77 % sucrose by mass: it boils and freezes into Sucrose ore
        /// at a mass conversion of 0.77 (elements/liquid.yaml, high- and lowTempTransitionOreMass-
        /// Conversion), and the sim reads that number as the ORE's share (the simulation
        /// library's physics, measured both ways).</summary>
        internal const float SugarWaterSugarFraction = 0.77f;

        /// <summary>The tag every mashable fruit carries, for the Mash Tun's delivery request.</summary>
        internal static readonly Tag MashableTag = TagManager.Create("Mod2Mashable");

        private static Composition Row(float water, float sugar, string source)
        {
            return new Composition { Water = water, Sugar = sugar, Source = source };
        }

        internal static IEnumerable<Tag> Fruits
        {
            get { return table.Keys; }
        }

        internal static bool TryGet(Tag fruit, out Composition composition)
        {
            return table.TryGetValue(fruit, out composition);
        }
    }
}
