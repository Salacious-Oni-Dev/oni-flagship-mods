using OniFramework;

namespace Mod2MatterEnvironment
{
	/// <summary>
	/// NITROGEN -- a real ONI element, gas and liquid, and the missing three quarters of air.
	///
	/// WHY. Vanilla Oxygen Not Included has no nitrogen at all. Its breathable atmosphere is pure
	/// oxygen, because a vanilla cell can hold exactly one element and "breathable" is a tag on
	/// that element, so the only way for a room to be breathable is for it to be entirely made of
	/// a breathable gas. Real breathable air is about 78% nitrogen, 21% oxygen and 1% argon, and
	/// the nitrogen is not filler: it is what keeps an oxygen atmosphere from igniting everything
	/// in it, it carries heat capacity the oxygen alone would not, and it is why breathing is
	/// governed by oxygen's PARTIAL pressure rather than by how much oxygen a room contains.
	///
	/// Once the custom SimDLL lets one cell hold a real mixture, that whole set of behaviours
	/// becomes expressible -- and needs a nitrogen to express it with. See
	/// <c>OniFramework.AtmosphereFacade</c> for the physiology this element makes possible, and
	/// <c>Mod1ThermoFluid.GasMixer</c> for the device that blends it into air.
	///
	/// WHERE THE NUMBERS COME FROM, and which ones are choices. This follows exactly the split
	/// PollutantElements established, because the two tables answer different questions:
	///
	///   ONI's element table (`ElementLoader`) gets ONI's own conventions, because the vanilla
	///   game reads it and vanilla behaviour has to stay coherent. In particular ONI stores
	///   ATOMIC mass for its diatomic gases -- Oxygen is 15.9994 and Hydrogen 1.00794, while
	///   CarbonDioxide is the real 44.01 -- so Nitrogen is registered at 14.0067 rather than the
	///   real 28.0134. That is not an error being copied: ONI uses `molarMass` for gas density
	///   and layering, and 14.0067 against Oxygen's 15.9994 preserves the true 28:32 mass ratio,
	///   which is what puts nitrogen just above oxygen in a room instead of sinking it to the
	///   floor. Registering the real 28.0134 here would invert the layering the mixture is
	///   supposed to demonstrate.
	///
	///   The framework's property registry (`MaterialPropertyRegistry`) gets REAL molecular
	///   masses -- 28.0134 -- because that is the table the native sim's gas law is given, and
	///   every other family already in it is real (Oxygen 31.9988, Water 18.01528, CO2 44.01).
	///
	/// From Stationeers, via this project's own reference notes
	///:
	///
	///   - latent heat of vaporisation 500 J/mol
	///   - liquid molar volume 0.0348 L/mol
	///   - vapour curve A = 5.5757107833e-07, B = 4.40221368946, in Stationeers'
	///     `EvaporationPressure(T) = A * T^B` (kPa) form
	///   - heat capacity ratio 1.333333, Stationeers' simple-gas bucket, which is where it puts N2
	///
	/// Stationeers' own molar mass for Nitrogen is 64.0, which matches neither the real molecular
	/// 28.0134 nor ONI's atomic convention 14.0067, and which its own reference note already flags
	/// as unexplained. It is deliberately NOT used in either table here.
	///
	/// What that curve IMPLIES, computed rather than assumed: `(101.325 / A) ^ (1 / B)` puts
	/// nitrogen's one-atmosphere boiling point at 75.20 K, against the real 77.36 K -- close
	/// enough to confirm the fit is a real one and not a gameplay number. Against this project's
	/// standard 6 MPa critical pressure the same curve puts the critical point at 190.03 K.
	///
	/// Two values are CHOICES, called out rather than buried:
	///   - specific heat 1.040 J/(g*K) for the gas and 2.04 for the liquid: the real-world values,
	///     matching how ONI writes its own gases (its CarbonDioxide 0.846 and Methane 2.191 are
	///     both the real figures) and sitting naturally beside ONI's own Oxygen at 1.005.
	///     Stationeers' 20.6 J/(mol*K) is not used, because dividing it by either candidate molar
	///     mass gives a number that matches neither reality nor ONI's own oxygen.
	///   - thermal conductivity 0.026 W/(m*K) for the gas (real N2 at 300 K, next to ONI's Oxygen
	///     at 0.024) and 0.14 for the liquid (real liquid nitrogen).
	///
	/// NOT BREATHABLE, deliberately. Nitrogen carries no `Breathable` tag and sits in ONI's
	/// `Unbreathable` material category, which is correct -- it is an asphyxiant on its own. A
	/// duplicant in a nitrogen/oxygen mixture breathes the oxygen; the nitrogen is inert, and the
	/// framework's partial-pressure model is what makes "inert" mean "dilutes but does not poison"
	/// instead of vanilla's "displaces entirely".
	///
	/// NO SOLID PHASE. Real nitrogen freezes at 63.15 K, but a solid needs material and texture
	/// work an element registration cannot supply, and Stationeers models none either. The liquid
	/// declares itself as its own freezing target, which is vanilla's own way of writing "does not
	/// transition" (Vacuum does exactly this in `special.yaml`). Adding SolidNitrogen later is
	/// additive and breaks nothing that depends on this.
	/// </summary>
	internal static class NitrogenElements
	{
		internal const string GasId = "Nitrogen";
		internal const string LiquidId = "LiquidNitrogen";

		/// <summary>
		/// `MoleHelper.EvaporationCoefficientA/B` (Stationeers) for Nitrogen. Named here as well as
		/// passed below, because the phase points are derived from exactly these two numbers and a
		/// reader should be able to check the arithmetic.
		/// </summary>
		private const double EvaporationCoefficientA = 5.5757107833e-07;

		private const double EvaporationCoefficientB = 4.40221368946;

		/// <summary>
		/// (101.325 / A) ^ (1 / B) -- the temperature at which nitrogen's own vapour curve puts its
		/// saturation pressure at one atmosphere. ONI's flat `lowTemp` is a single number and the
		/// one-atmosphere point is the honest choice for it; the pressure dependence lives in the
		/// framework's vapour curve, not in this field.
		/// </summary>
		private const float CondensationPointK = 75.2f;

		/// <summary>
		/// ONI's diatomic convention: atomic rather than molecular mass, matching Oxygen's 15.9994.
		/// See the class comment -- this is what keeps nitrogen layering just above oxygen.
		/// </summary>
		private const float OniMolarMassGPerMol = 14.0067f;

		/// <summary>Real molecular mass, for the framework's own property registry and the sim's gas law.</summary>
		private const float RealMolecularMassGPerMol = 28.0134f;

		/// <summary>
		/// Registers both phases and their thermodynamic properties. Must run from the mod's
		/// OnLoad: `Global.Awake` loads mod DLLs before `Assets` calls `ElementLoader.Load`,
		/// so OnLoad is early enough and nothing later is.
		/// </summary>
		internal static void Register()
		{
			ElementRegistry.AddStrings(GasId, "Nitrogen",
				"Inert gas, and three quarters of breathable air. It carries no oxygen of its own, "
				+ "but it dilutes an oxygen atmosphere down out of the range where ordinary "
				+ "materials ignite, and it carries heat the oxygen alone would not.");
			ElementRegistry.AddStrings(LiquidId, "Liquid Nitrogen",
				"Nitrogen condensed into a liquid. Requires cryogenic temperatures: nitrogen boils "
				+ "at 75.2 K at one atmosphere.");

			ElementRegistry.RegisterGas(
				elementId: GasId,
				localizationID: "STRINGS.ELEMENTS.NITROGEN.NAME",
				specificHeatCapacity: 1.04f,
				thermalConductivity: 0.026f,
				molarMass: OniMolarMassGPerMol,
				condensesTo: LiquidId,
				condensationPointK: CondensationPointK,
				toxicity: 0f);

			ElementRegistry.RegisterLiquid(
				elementId: LiquidId,
				localizationID: "STRINGS.ELEMENTS.LIQUIDNITROGEN.NAME",
				specificHeatCapacity: 2.04f,
				thermalConductivity: 0.14f,
				molarMass: OniMolarMassGPerMol,
				// Its own id as the freezing target: vanilla's "no transition" convention.
				freezesTo: LiquidId,
				freezingPointK: 0f,
				boilsTo: GasId,
				boilingPointK: CondensationPointK,
				toxicity: 0f,
				// A liquid that boils at 75.2 K cannot default to the 300 K every other liquid
				// defaults to; it would flash to gas the instant anything spawned it.
				defaultTemperatureK: 70f);

			// The thermodynamic half, against the same hashes ElementLoader derives from the ids
			// above. Real molecular mass here, ONI's atomic convention in the element entry above;
			// see the class comment for why the two tables deliberately disagree.
			MaterialPropertyRegistry.RegisterFamily(
				new MaterialProperties(
					molecularMassGPerMol: RealMolecularMassGPerMol,
					// Physical: NIST's 199.2 kJ/kg at the 77.35 K normal boiling point, stored as J/mol
					// against the REAL molecular mass above, so the J/kg every consumer reads is
					// NIST's own figure. Stationeers' tuned value was 500 J/mol, 11.2x smaller.
					latentHeatOfVaporizationJPerMol: 5580f,
					evaporationCoefficientA: EvaporationCoefficientA,
					evaporationCoefficientB: EvaporationCoefficientB,
					// Stationeers' own default floor for most substances, the real-world Armstrong
					// limit; and the 6 MPa critical pressure this project uses across the board.
					// Against nitrogen's curve that critical pressure puts its critical temperature
					// at 190.03 K.
					minLiquidPressurePa: 6300f,
					criticalPointPressurePa: 6000000f,
					// Stationeers' simple-gas bucket, which is the one it puts N2 in.
					heatCapacityRatio: 1.333333f,
					liquidMolarVolumeLPerMol: 0.0348f,
					// 0.72 kJ/mol (CRC-derived element table), 25.7 kJ/kg: 0.129 of vaporization,
					// where Stationeers' flat rule would have made it 0.2.
					latentHeatOfFusionJPerMol: 720f),
				(SimHashes)Hash.SDBMLower(GasId), (SimHashes)Hash.SDBMLower(LiquidId));
		}
	}
}
