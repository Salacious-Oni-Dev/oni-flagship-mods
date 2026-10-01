using OniFramework;

namespace Mod2MatterEnvironment
{
	/// <summary>
	/// POLLUTANT -- a real ONI element, gas and liquid, ported from Stationeers.
	///
	/// WHY. Vanilla ONI models contamination as whole separate substances: Polluted Oxygen is its
	/// own element, Polluted Water is its own element, and the "polluted" part is not a thing that
	/// exists on its own or can be separated out. Stationeers models the same idea as a real gas,
	/// `GasType.Pollutant`, that mixes with everything else and can be filtered back out of a
	/// mixture. That difference is the whole point: once Pollutant is a substance, Polluted Oxygen
	/// can become what it physically is -- Oxygen with Pollutant mixed into it -- and scrubbing
	/// becomes separating a mixture rather than converting one magic element into another.
	///
	/// This is the element half of that work. Converting vanilla's Polluted Oxygen over to an
	/// Oxygen+Pollutant mixture is a separate, larger job that depends on this existing first.
	///
	/// WHERE THE NUMBERS COME FROM, and which ones are choices. Everything Stationeers states is
	/// taken from it directly, via this project's own reference notes
	///:
	///
	///   - molar mass 28.0 g/mol
	///   - specific heat 24.8 J/(mol*K), which is 0.886 J/(g*K) in the units ONI's element data
	///     uses -- ONI wants per gram, Stationeers states per mole, and dividing by the molar mass
	///     is the whole conversion
	///   - latent heat of vaporisation 2000 J/mol
	///   - liquid molar volume 0.04 L/mol
	///   - vapour curve A = 2.079033884, B = 1.31202194555, in Stationeers' own
	///     `EvaporationPressure(T) = A * T^B` (kPa) form
	///   - heat capacity ratio 1.26, Stationeers' polyatomic bucket, which is the bucket it puts
	///     Pollutant in
	///
	/// What that curve IMPLIES, computed rather than assumed: Pollutant condenses at 19.34 K at one
	/// atmosphere and its critical point lands at 433.9 K. It is a permanent gas at any temperature
	/// an ONI colony will ever see, which is exactly how ONI itself writes Hydrogen (condenses at
	/// 20.28 K). The liquid phase exists for completeness and for cryogenic work, not as something
	/// a player will meet by accident.
	///
	/// Two values are CHOICES, because Stationeers does not model them per-gas and ONI requires
	/// them, and they are called out rather than buried:
	///   - thermal conductivity 0.02 W/(m*K) for the gas, picked to sit between ONI's own
	///     CarbonDioxide (0.0146) and ContaminatedOxygen (0.024), since Pollutant is that kind of
	///     heavy contaminant gas; 0.1 for the liquid.
	///   - toxicity 0.5, higher than ONI's ContaminatedOxygen (0.01) or Polluted Water (0.1),
	///     because those are dilute contaminated substances and this is the contaminant itself.
	///
	/// NO SOLID PHASE. Stationeers has none, so inventing one would be inventing data. The liquid
	/// declares itself as its own freezing target, which is vanilla's own way of writing "does not
	/// transition" -- Vacuum does exactly this in `special.yaml`.
	/// </summary>
	internal static class PollutantElements
	{
		internal const string GasId = "Pollutant";
		internal const string LiquidId = "LiquidPollutant";

		/// <summary>
		/// `MoleHelper.EvaporationCoefficientA/B` (Stationeers) for Pollutant. Named here as well as
		/// passed below, because the phase points further down are derived from exactly these two
		/// numbers and a reader should be able to check the arithmetic.
		/// </summary>
		private const double EvaporationCoefficientA = 2.079033884;

		private const double EvaporationCoefficientB = 1.31202194555;

		/// <summary>
		/// (101.325 / A) ^ (1 / B) -- the temperature at which Pollutant's own vapour curve puts
		/// its saturation pressure at one atmosphere. ONI's flat `lowTemp` is a single number, and
		/// the one-atmosphere point is the honest choice for it: it is the same convention ONI uses
		/// for its own gases, and the pressure-dependent behaviour comes from the framework's
		/// vapour curve rather than from this field.
		/// </summary>
		private const float CondensationPointK = 19.34f;

		/// <summary>
		/// Stationeers' specific heat converted from J/(mol*K) to the J/(g*K) ONI's element data
		/// uses: 24.8 / 28.0.
		/// </summary>
		private const float SpecificHeatJPerGramK = 0.886f;

		private const float MolarMassGPerMol = 28f;

		private const float Toxicity = 0.5f;

		/// <summary>
		/// Registers both phases and their thermodynamic properties. Must run from the mod's
		/// OnLoad: `Global.Awake` loads mod DLLs before `Assets` calls `ElementLoader.Load`,
		/// so OnLoad is early enough and nothing later is.
		/// </summary>
		internal static void Register()
		{
			ElementRegistry.AddStrings(GasId, "Pollutant",
				"Industrial contaminant in gas form. Mixes with breathable atmospheres and has to "
				+ "be separated back out of them rather than converted away.");
			ElementRegistry.AddStrings(LiquidId, "Liquid Pollutant",
				"Pollutant condensed into a liquid. Requires cryogenic temperatures: Pollutant "
				+ "condenses at 19.34 K at one atmosphere.");

			ElementRegistry.RegisterGas(
				elementId: GasId,
				localizationID: "STRINGS.ELEMENTS.POLLUTANT.NAME",
				specificHeatCapacity: SpecificHeatJPerGramK,
				thermalConductivity: 0.02f,
				molarMass: MolarMassGPerMol,
				condensesTo: LiquidId,
				condensationPointK: CondensationPointK,
				toxicity: Toxicity);

			ElementRegistry.RegisterLiquid(
				elementId: LiquidId,
				localizationID: "STRINGS.ELEMENTS.LIQUIDPOLLUTANT.NAME",
				specificHeatCapacity: SpecificHeatJPerGramK,
				thermalConductivity: 0.1f,
				molarMass: MolarMassGPerMol,
				// Its own id as the freezing target: vanilla's "no transition" convention.
				freezesTo: LiquidId,
				freezingPointK: 0f,
				boilsTo: GasId,
				boilingPointK: CondensationPointK,
				toxicity: Toxicity,
				// A liquid whose boiling point is 19.34 K cannot default to the 300 K every other
				// liquid defaults to; it would flash to gas the instant anything spawned it.
				defaultTemperatureK: 15f);

			// The thermodynamic half. Registered against the same hashes ElementLoader will derive
			// from the ids above, so the framework's phase-change, pressure and latent-heat paths
			// treat Pollutant exactly as they treat a vanilla substance -- which is the point of
			// the property registry being in the framework rather than in a mod.
			MaterialPropertyRegistry.RegisterFamily(
				new MaterialProperties(
					molecularMassGPerMol: MolarMassGPerMol,
					// KEPT AT STATIONEERS' 2000 J/mol while every other family is physical.
					// Pollutant is a Stationeers invention with no physical referent, so there is
					// nothing to be faithful to but the game it came from. For the same reason it
					// leaves latentHeatOfFusionJPerMol unset and keeps Stationeers' vap / 5.
					latentHeatOfVaporizationJPerMol: 2000f,
					evaporationCoefficientA: EvaporationCoefficientA,
					evaporationCoefficientB: EvaporationCoefficientB,
					// Stationeers' own default floor for most substances, the real-world Armstrong
					// limit; and the 6 MPa critical pressure this project uses across the board.
					// Against Pollutant's curve that critical pressure puts its critical
					// temperature at 433.9 K.
					minLiquidPressurePa: 6300f,
					criticalPointPressurePa: 6000000f,
					// Stationeers' polyatomic bucket, which is the one it puts Pollutant in.
					heatCapacityRatio: 1.26f,
					liquidMolarVolumeLPerMol: 0.04f),
				(SimHashes)Hash.SDBMLower(GasId), (SimHashes)Hash.SDBMLower(LiquidId));
		}
	}
}
