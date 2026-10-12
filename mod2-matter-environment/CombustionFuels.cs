using OniFramework;

namespace Mod2MatterEnvironment
{
    /// <summary>
    /// MOD 2'S FUEL ROWS for <see cref="Combustion"/>. (The smoke system came from an idea by Kaz.)
    /// The framework holds the mechanism and no data; these are the first rows, and any mod may replace them.
    ///
    /// WHERE THE NUMBERS COME FROM. Each row is an ultimate analysis by mass (carbon, hydrogen,
    /// oxygen, the rest as ash) and a lower heating value, from standard combustion references for
    /// a TYPICAL sample of the material. Real fuels vary by tens of percent; these are representative,
    /// not precise, and the composition matters more than the last digit because it is what sets
    /// the oxygen a burn needs and the products it makes. Lower heating value = higher heating value
    /// minus 2.44 MJ per kg of water formed (9 x hydrogen), so every row's water leaves as steam,
    /// which is what the products say.
    ///
    /// The pure substances are exact from their formulas: methane CH4 (C 0.7487, H 0.2513, 50.0
    /// MJ/kg), hydrogen H2 (120.0 MJ/kg), ethanol C2H5OH (C 0.5214, H 0.1313, O 0.3473, 26.8
    /// MJ/kg), carbon to CO2 (32.76 MJ/kg) for soot.
    ///
    /// CLEAN-BURN SOOT is a placeholder per row, ordered by how smoky the fuel is in practice (coal
    /// and peat above wood, gases far below), and is balance for the slices that use it.
    ///
    /// GAS PRODUCT SPECIES follow Stationeers' <c>Combustion</c> table where it has the fuel:
    /// methane burns to CO2 and Pollutant (its hydrogen leaves as Pollutant, not
    /// water), hydrogen and alcohol burn to steam. The masses are this table's, not Stationeers'.
    ///
    /// Not saved: <see cref="Register"/> runs on every load.
    /// </summary>
    internal static class CombustionFuels
    {
        internal static void Register()
        {
            // Dry wood. C 50 / H 6 / O 43 / ash 1 is the textbook ultimate analysis; HHV about
            // 19.5 MJ/kg dry, so LHV 19.5 - 2.44 x 0.54 = 18.2.
            Combustion.Register(new CombustionFuel(SimHashes.WoodLog,
                carbon: 0.50f, hydrogen: 0.06f, oxygen: 0.43f, ash: 0.01f,
                heatingValueJPerKg: 18.2e6f, cleanSootFraction: 0.005f,
                source: "dry wood, typical ultimate analysis; LHV from HHV 19.5 MJ/kg"));

            // Dry peat: C 55 / H 5.5 / O 33, nitrogen and mineral matter 6.5 folded into ash.
            // HHV about 21 MJ/kg dry, LHV 21 - 2.44 x 0.495 = 19.8.
            Combustion.Register(new CombustionFuel(SimHashes.Peat,
                carbon: 0.55f, hydrogen: 0.055f, oxygen: 0.33f, ash: 0.065f,
                heatingValueJPerKg: 19.8e6f, cleanSootFraction: 0.01f,
                source: "dry peat, typical; LHV from HHV 21 MJ/kg"));

            // ONI's Coal is SimHashes.Carbon. Bituminous: C 78 / H 5 / O 8, N + S + ash 9.
            // HHV about 31 MJ/kg, LHV 31 - 2.44 x 0.45 = 29.9.
            Combustion.Register(new CombustionFuel(SimHashes.Carbon,
                carbon: 0.78f, hydrogen: 0.05f, oxygen: 0.08f, ash: 0.09f,
                heatingValueJPerKg: 29.9e6f, cleanSootFraction: 0.01f,
                source: "bituminous coal, typical; LHV from HHV 31 MJ/kg"));

            // ONI's Natural Gas is SimHashes.Methane. Stationeers' methane makes Pollutant + CO2.
            Combustion.Register(new CombustionFuel(SimHashes.Methane,
                carbon: 0.7487f, hydrogen: 0.2513f, oxygen: 0f, ash: 0f,
                heatingValueJPerKg: 50.0e6f, cleanSootFraction: 0.0005f,
                hydrogenProduct: PollutantElements.GasHash,
                source: "CH4; product species from Stationeers Combustion.ResultMethaneOxygen"));

            Combustion.Register(new CombustionFuel(SimHashes.Hydrogen,
                carbon: 0f, hydrogen: 1f, oxygen: 0f, ash: 0f,
                heatingValueJPerKg: 120.0e6f,
                source: "H2; Stationeers Combustion.ResultHydrogenOxygen makes steam"));

            Combustion.Register(new CombustionFuel(SimHashes.Ethanol,
                carbon: 0.5214f, hydrogen: 0.1313f, oxygen: 0.3473f, ash: 0f,
                heatingValueJPerKg: 26.8e6f, cleanSootFraction: 0.001f,
                source: "C2H5OH; Stationeers Combustion.ResultAlcoholOxygen makes CO2 + steam"));

            // Ethanol VAPOUR, the same molecule: what ignites in the air. Liquid
            // ethanol burning in place is not done yet; the gas burns now.
            Combustion.Register(new CombustionFuel(SimHashes.EthanolGas,
                carbon: 0.5214f, hydrogen: 0.1313f, oxygen: 0.3473f, ash: 0f,
                heatingValueJPerKg: 26.8e6f, cleanSootFraction: 0.001f,
                source: "C2H5OH vapour; as the liquid row"));

            // Soot is carbon: what a smoky fire left behind burns the rest of the way.
            Combustion.Register(new CombustionFuel(SootElement.Hash,
                carbon: 1f, hydrogen: 0f, oxygen: 0f, ash: 0f,
                heatingValueJPerKg: Combustion.CarbonHeatingValueJPerKg,
                source: "carbon to CO2, 393.5 kJ/mol"));
        }

        /// <summary>
        /// Which fuel GASES catch fire on their own, and at what. Stationeers'
        /// <c>GasMixture.IsAutoIgnition</c> and its <c>Chemistry</c> constants:
        /// methane and hydrogen above 573.15 K, alcohol above
        /// 673.15 K, with more than <c>MinCombustionMoles</c> (one mole) of fuel. A cell counts mass,
        /// so the mole is converted with the real molar mass: CH4 16.043 g, H2 2.016 g, C2H5OH
        /// 46.069 g (as Stationeers; the conversion is the unit change). Not saved: runs on
        /// every load, after <see cref="Register"/>.
        /// </summary>
        internal static void RegisterAutoIgnition()
        {
            Ignition.SetAutoIgnition(SimHashes.Methane, 573.15f, 0.016043f);
            Ignition.SetAutoIgnition(SimHashes.Hydrogen, 573.15f, 0.002016f);
            Ignition.SetAutoIgnition(SimHashes.EthanolGas, 673.15f, 0.046069f);
        }
    }
}
