using System;
using HarmonyLib;
using Klei.AI;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// THE LIVING HALF OF THE ATMOSPHERE: what breathes it, what cleans it, and what makes it.
    ///
    /// Three augmentations, all of which exist because air is now a MIXTURE and vanilla's biology
    /// was written for cells that hold one element:
    ///
    /// 1. POLLUTED OXYGEN STOPS BEING AN ELEMENT. In a room the mixture layer owns, Contaminated
    ///    Oxygen arriving from anywhere -- a geyser, a Sublimation Station, a plant, a Stinky --
    ///    is split into what it actually is: oxygen with a little pollutant in it. The split is
    ///    1% by mass, taken from ONI's own `toxicity: 0.01` on that element in `gas.yaml` rather
    ///    than invented. Forty-five files reference `SimHashes.ContaminatedOxygen`, so the element
    ///    cannot be deleted; converting it where it lands is what makes it a mixture without
    ///    breaking every geyser and plant that emits it.
    ///
    ///    This also retires a stopgap. <see cref="AtmosphereFacade"/> counts any breathable-tagged
    ///    gas towards the oxygen a breather can use, purely so that a Polluted Oxygen room did not
    ///    read as having no oxygen at all. Once Polluted Oxygen IS oxygen plus pollutant, its
    ///    oxygen is counted on its own merits -- and a partly-spent room correctly reads as having
    ///    less of it, which the stopgap could never express.
    ///
    /// 2. THE PUFT EATS THE POLLUTION AND BREATHES THE REST BACK OUT. Vanilla's Puft consumes the
    ///    Contaminated Oxygen element whole and excretes Slime Mold. With the element gone, it
    ///    inhales the mixture in the proportions the room actually holds, turns only the Pollutant
    ///    fraction into slime, and returns everything else -- the oxygen it took in alongside --
    ///    to the cell unchanged. That makes a Puft a real air cleaner rather than a gas sink, and
    ///    it conserves: nothing it inhales disappears.
    ///
    /// 3. THE OXYFERN MAKES AIR, NOT OXYGEN. Vanilla's fern emits pure oxygen, which in a mixture
    ///    world builds an oxygen pocket rather than ventilating a room. It now emits a real 75/25
    ///    nitrogen/oxygen blend into the cell's mixture.
    ///
    /// THE ONE DELIBERATE EXCEPTION IN THIS FILE, and it is a real one. The Oxyfern emits nitrogen
    /// it never took in: 0.0525 kg/s per plant, from nothing. That is a MATTER SOURCE, which the
    /// conservation rule otherwise forbids outright, chosen deliberately over two conserving
    /// alternatives (recycling the room's own nitrogen, or emitting only oxygen into an
    /// already-mixed room). It is recorded here rather than buried
    /// because a nitrogen farm is now a way to manufacture atmosphere, and anyone balancing this
    /// later needs to know that on purpose. The carbon dioxide the plant consumes is real; the
    /// oxygen it makes is vanilla's own conversion; only the nitrogen is conjured.
    ///
    /// THE RATE IS DERIVED FROM A DUPLICANT, not from vanilla's fern. The specification is that
    /// five ferns offset one duplicant. A duplicant consumes 0.1 kg/s of oxygen and exhales
    /// 0.002 kg/s of carbon dioxide (`OXYGEN_USED_PER_SECOND` x `OXYGEN_TO_CO2_CONVERSION`, both
    /// from `DUPLICANTSTATS.BASESTATS`). So one fern takes 0.0004 kg/s of carbon dioxide and
    /// returns a fifth of a duplicant's air: 0.02 kg/s of oxygen, plus the 0.0525 kg/s of nitrogen
    /// that makes it 75/25 BY MOLE -- moles, not mass, because that is what partial pressure is
    /// computed from and therefore what decides whether the air is breathable.
    /// </summary>
    internal static class Biology
    {
        /// <summary>
        /// Pollutant fraction of Contaminated Oxygen, by mass. ONI's own `toxicity: 0.01` for that
        /// element, used as the split rather than a number of this project's choosing.
        /// </summary>
        internal const float PollutedOxygenPollutantFraction = 0.01f;

        /// <summary>Carbon dioxide one Oxyfern consumes, kg/s -- a fifth of one duplicant's exhalation.</summary>
        internal const float OxyfernCarbonDioxideKgPerSecond = 0.0004f;

        /// <summary>Oxygen one Oxyfern emits, kg/s -- a fifth of one duplicant's consumption.</summary>
        internal const float OxyfernOxygenKgPerSecond = 0.02f;

        /// <summary>
        /// Nitrogen one Oxyfern emits, kg/s. Derived, not chosen: 0.02 kg/s of oxygen is
        /// 0.625 mol/s, a quarter of the blend by mole, so the nitrogen is three times that in
        /// moles -- 1.875 mol/s -- which at 28.0134 g/mol is 0.0525 kg/s. THIS IS THE CONJURED
        /// MASS; see the class doc.
        /// </summary>
        internal const float OxyfernNitrogenKgPerSecond = 0.0525f;

        /// <summary>
        /// How much of its own food a Puft inhales per second, kg/s. What that food IS comes from
        /// the morph's own diet, not from here -- see <see cref="PuftMixtureDietComponent"/>.
        /// </summary>
        internal const float PuftFoodKgPerSecond = 0.002f;

        internal static ushort IndexOf(SimHashes id)
        {
            Element element = ElementLoader.FindElementByHash(id);
            if (element == null)
            {
                return ushort.MaxValue;
            }
            int idx = ElementLoader.elements.IndexOf(element);
            return idx >= 0 ? (ushort)idx : ushort.MaxValue;
        }

        internal static ushort PollutantIndex => IndexOf((SimHashes)Hash.SDBMLower("Pollutant"));

        internal static ushort NitrogenIndex => IndexOf((SimHashes)Hash.SDBMLower("Nitrogen"));
    }

    /// <summary>
    /// Splits Contaminated Oxygen into oxygen and pollutant wherever it would land in a room the
    /// mixture layer owns.
    ///
    /// `SimMessages.AddRemoveSubstance` is the seam because it is what every emitter in the game
    /// funnels through to put gas into a cell -- geysers, the Sublimation Station, Stinky, the
    /// plants that exhale it. One prefix here covers all of them, where patching the emitters
    /// would have meant forty-five configs and a permanent risk of missing the forty-sixth.
    ///
    /// Only promoted cells are touched. Everywhere else Polluted Oxygen stays exactly the element
    /// vanilla ships, so a colony that never promotes a room sees no change at all.
    /// </summary>
    [HarmonyPatch(typeof(SimMessages), "AddRemoveSubstance",
        new Type[]
        {
            typeof(int), typeof(ushort), typeof(CellAddRemoveSubstanceEvent),
            typeof(float), typeof(float), typeof(byte), typeof(int), typeof(bool), typeof(int),
        })]
    internal static class SimMessages_AddRemoveSubstance_PollutedOxygen
    {
        private static bool Prefix(int gameCell, ushort elementIdx, float mass, float temperature)
        {
            try
            {
                if (mass <= 0f || !Grid.IsValidCell(gameCell)
                    || !GasMixtureFacade.IsRoomOwned(gameCell))
                {
                    return true;
                }
                if (elementIdx >= ElementLoader.elements.Count)
                {
                    return true;
                }
                Element element = ElementLoader.elements[elementIdx];
                if (element == null || element.id != SimHashes.ContaminatedOxygen)
                {
                    return true;
                }

                ushort oxygenIdx = Biology.IndexOf(SimHashes.Oxygen);
                ushort pollutantIdx = Biology.PollutantIndex;
                if (oxygenIdx == ushort.MaxValue || pollutantIdx == ushort.MaxValue)
                {
                    // Mod 2's Pollutant is not registered in this game. Better to leave vanilla's
                    // own element alone than to drop the mass on the floor.
                    return true;
                }

                float temperatureK = temperature > 0f
                    ? temperature
                    : element.defaultValues.temperature;
                float pollutantKg = mass * Biology.PollutedOxygenPollutantFraction;
                GasMixtureFacade.Inject(gameCell, oxygenIdx, mass - pollutantKg, temperatureK);
                GasMixtureFacade.Inject(gameCell, pollutantIdx, pollutantKg, temperatureK);
                return false;
            }
            catch
            {
                // Emission paths run constantly. A mistake here degrades to vanilla's own
                // behaviour rather than losing an emitter's output.
            }
            return true;
        }
    }

    /// <summary>
    /// A Puft that eats the pollution out of a real mixture.
    ///
    /// It inhales in the proportions the cell actually holds, which is what "expels them equally"
    /// means: the ratio going in is the ratio coming back out, minus only the Pollutant it
    /// digests. Everything else is returned to the same cell at the same temperature, so a Puft
    /// standing in a room does not change that room's oxygen at all -- it changes its pollution.
    ///
    /// Vanilla's own diet machinery is left in place and simply never fires in a promoted room,
    /// because the element it is looking for is not there any more. In an ordinary vanilla room
    /// this component does nothing and the creature behaves exactly as it always has.
    /// </summary>
    public class PuftMixtureDietComponent : KMonoBehaviour, ISim1000ms
    {
        private const string LogPrefix = "[Mod1ThermoFluid] PUFT: ";

        /// <summary>Solids are only dropped once this much has accumulated, so they arrive as lumps.</summary>
        private const float MinimumDropKg = 0.5f;

        private float pendingSolidKg;

        /// <summary>Total gas digested, for a harness to assert on.</summary>
        public float DigestedKg { get; private set; }

        /// <summary>Total mass inhaled and breathed straight back out.</summary>
        public float ExhaledKg { get; private set; }

        /// <summary>The species this morph actually eats, resolved from its own diet.</summary>
        private ushort foodSpeciesIdx = ushort.MaxValue;

        /// <summary>What it excretes, and how much of what it eats becomes that.</summary>
        private SimHashes producedElement = SimHashes.Vacuum;

        private float conversionRate = 1f;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            ResolveDiet();
        }

        /// <summary>
        /// Reads the creature's OWN diet rather than assuming one, because the four Puft morphs do
        /// not eat the same thing and one of them is the reason this matters: the Puft Prince
        /// (`PuftOxyliteConfig`) eats OXYGEN and excretes OXYLITE -- a solid -- while the base and
        /// Dense Pufts eat Polluted Oxygen and excrete Slime Mold and the Squeaky Puft eats
        /// chlorine. A component that hardcoded pollutant-to-slime would have quietly turned a
        /// Puft Prince into a slime factory that ignores the oxygen it is supposed to be eating.
        ///
        /// `CreatureCalorieMonitor.Def.diet` is where `BasePuftConfig.SetupDiet` puts it, so every
        /// morph -- and any modded one built the same way -- answers for itself.
        ///
        /// The one translation this makes: a morph whose diet names Contaminated Oxygen is fed
        /// POLLUTANT instead, because in a promoted room that element has already been split into
        /// oxygen and pollutant and the pollutant is the part that was ever its food.
        /// </summary>
        private void ResolveDiet()
        {
            CreatureCalorieMonitor.Def def = gameObject.GetDef<CreatureCalorieMonitor.Def>();
            if (def == null || def.diet == null || def.diet.infos == null
                || def.diet.infos.Length == 0)
            {
                return;
            }

            Diet.Info info = def.diet.infos[0];
            producedElement = ElementLoader.GetElementID(info.producedElement);
            conversionRate = info.producedConversionRate;

            foreach (Tag consumed in info.consumedTags)
            {
                SimHashes consumedId = ElementLoader.GetElementID(consumed);
                if (consumedId == SimHashes.ContaminatedOxygen)
                {
                    foodSpeciesIdx = Biology.PollutantIndex;
                }
                else
                {
                    foodSpeciesIdx = Biology.IndexOf(consumedId);
                }
                if (foodSpeciesIdx != ushort.MaxValue)
                {
                    break;
                }
            }
        }

        public void Sim1000ms(float dt)
        {
            try
            {
                Feed(dt);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + "feeding tick failed: " + e);
            }
        }

        private void Feed(float dt)
        {
            if (dt <= 0f || foodSpeciesIdx == ushort.MaxValue
                || producedElement == SimHashes.Vacuum)
            {
                return;
            }
            int cell = Grid.PosToCell(this);
            if (!Grid.IsValidCell(cell) || !GasMixtureFacade.IsRoomOwned(cell))
            {
                return;
            }

            GasMixtureFacade.GasComponent[] composition = GasMixtureFacade.TryGetComposition(cell);
            float totalKg = 0f;
            float foodKg = 0f;
            for (int i = 0; i < composition.Length; i++)
            {
                totalKg += composition[i].MassKg;
                if (composition[i].ElementIdx == foodSpeciesIdx)
                {
                    foodKg += composition[i].MassKg;
                }
            }
            if (foodKg <= 0f || totalKg <= 0f)
            {
                return;
            }

            // INHALE IN PROPORTION. The creature does not sip its food out of the air; it takes a
            // breath, and a breath is whatever the room is made of. The fraction is sized so the
            // FOOD in that breath matches its diet rate.
            float wantedFoodKg = Mathf.Min(Biology.PuftFoodKgPerSecond * dt, foodKg);
            float breathFraction = Mathf.Clamp01(wantedFoodKg / foodKg);
            if (breathFraction <= 0f)
            {
                return;
            }

            float temperatureK = Grid.Temperature[cell];
            float digestedKg = 0f;
            float exhaledKg = 0f;
            for (int i = 0; i < composition.Length; i++)
            {
                ushort speciesIdx = composition[i].ElementIdx;
                float inhaledKg = composition[i].MassKg * breathFraction;
                if (inhaledKg <= 0f)
                {
                    continue;
                }
                if (speciesIdx == foodSpeciesIdx)
                {
                    // Digested: out of the mixture, and into the solid below.
                    GasMixtureFacade.ConvertToVanilla(cell, speciesIdx, inhaledKg, temperatureK);
                    GasMixtureFacade.RemoveVanillaMass(cell, inhaledKg);
                    digestedKg += inhaledKg;
                }
                else
                {
                    // BREATHED STRAIGHT BACK OUT, as gas, into the same cell at the same
                    // temperature. Taking it and immediately returning it is a no-op on the cell
                    // by construction, which is exactly the point: only a Puft Prince turns a gas
                    // into a solid, and only the gas its own diet names.
                    exhaledKg += inhaledKg;
                }
            }

            if (digestedKg <= 0f)
            {
                return;
            }
            DigestedKg += digestedKg;
            ExhaledKg += exhaledKg;

            // The morph's own conversion efficiency, dropped in lumps rather than a continuous
            // dribble -- the same way vanilla's own minimum poop size works.
            pendingSolidKg += digestedKg * conversionRate;
            if (pendingSolidKg < MinimumDropKg)
            {
                return;
            }

            Element produced = ElementLoader.FindElementByHash(producedElement);
            if (produced == null || produced.substance == null)
            {
                return;
            }
            produced.substance.SpawnResource(transform.GetPosition(), pendingSolidKg, temperatureK,
                byte.MaxValue, 0);
            pendingSolidKg = 0f;
        }
    }

    /// <summary>
    /// An Oxyfern that emits breathable AIR rather than pure oxygen.
    ///
    /// Rates are in <see cref="Biology"/> and are derived from a duplicant, not from vanilla's
    /// fern: five of these offset one duplicant's carbon dioxide and supply one duplicant's air.
    /// The nitrogen is CREATED -- see <see cref="Biology"/>'s class doc, where that deliberate
    /// exception to the project's conservation rule is recorded.
    ///
    /// It emits into the cell's MIXTURE, not into vanilla's single-element layer, because a room
    /// whose air is a mixture cannot receive a parcel of pure oxygen without the two layers
    /// disagreeing about what is in it.
    /// </summary>
    public class OxyfernAirEmitterComponent : KMonoBehaviour, ISim1000ms
    {
        private const string LogPrefix = "[Mod1ThermoFluid] OXYFERN: ";

        /// <summary>Total air emitted, for a harness to assert on.</summary>
        public float EmittedKg { get; private set; }

        /// <summary>Total carbon dioxide taken out of the room.</summary>
        public float ConsumedCarbonDioxideKg { get; private set; }

        private ElementConsumer vanillaConsumer;
        private ElementConverter vanillaConverter;

        protected override void OnSpawn()
        {
            base.OnSpawn();

            // Vanilla's own consumer and converter are switched off rather than removed. They
            // cannot see a promoted cell's contents anyway (an ElementConsumer registers a sample
            // cell with the native sim, which is gated on promoted rooms), so leaving them running
            // would do nothing in a mixture room and double the plant's output in a vanilla one.
            vanillaConsumer = GetComponent<ElementConsumer>();
            vanillaConverter = GetComponent<ElementConverter>();
            if (vanillaConsumer != null)
            {
                vanillaConsumer.EnableConsumption(false);
            }
            if (vanillaConverter != null)
            {
                // No on/off switch on this component -- each element carries its own IsActive
                // flag, so both sides of the conversion are switched off individually.
                for (int i = 0; i < vanillaConverter.consumedElements.Length; i++)
                {
                    vanillaConverter.consumedElements[i].IsActive = false;
                }
                for (int i = 0; i < vanillaConverter.outputElements.Length; i++)
                {
                    vanillaConverter.outputElements[i].IsActive = false;
                }
            }
        }

        public void Sim1000ms(float dt)
        {
            try
            {
                Photosynthesise(dt);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + "emission tick failed: " + e);
            }
        }

        private void Photosynthesise(float dt)
        {
            if (dt <= 0f)
            {
                return;
            }
            int cell = Grid.PosToCell(this);
            if (!Grid.IsValidCell(cell) || !GasMixtureFacade.IsRoomOwned(cell))
            {
                return;
            }

            ushort oxygenIdx = Biology.IndexOf(SimHashes.Oxygen);
            ushort nitrogenIdx = Biology.NitrogenIndex;
            ushort carbonDioxideIdx = Biology.IndexOf(SimHashes.CarbonDioxide);
            if (oxygenIdx == ushort.MaxValue || carbonDioxideIdx == ushort.MaxValue)
            {
                return;
            }

            float temperatureK = Grid.Temperature[cell];
            if (temperatureK <= 0f)
            {
                return;
            }

            // CARBON DIOXIDE FIRST, and the plant does nothing without it. A fern that emitted air
            // in a room with no carbon dioxide would be a pure matter source in BOTH gases rather
            // than one, and the exception this file records is deliberately only the nitrogen.
            float availableCarbonDioxideKg = 0f;
            GasMixtureFacade.GasComponent[] composition = GasMixtureFacade.TryGetComposition(cell);
            for (int i = 0; i < composition.Length; i++)
            {
                if (composition[i].ElementIdx == carbonDioxideIdx)
                {
                    availableCarbonDioxideKg += composition[i].MassKg;
                }
            }

            // CARBON DIOXIDE IS TAKEN OPPORTUNISTICALLY, NOT AS A GATE -- which is a correction a
            // live run forced. Throttling the plant's output by the carbon dioxide available
            // starves it, because ONI's own duplicant exhales only 2% of the oxygen it inhales
            // (0.1 kg/s in, 0.002 kg/s out). A plant that could only make oxygen from that carbon
            // could never balance the person who breathed it, in vanilla or here -- fifteen ferns
            // measured the same output as five, all of them fighting over 0.2 kg of carbon.
            //
            // Vanilla resolves this the same way and always has: its Oxyfern's oxygen comes from
            // the WATER it absorbs (a PlantElementAbsorber), with carbon dioxide as a smaller
            // second input. So this takes whatever carbon dioxide is there, up to what it wants,
            // and emits at its rated output regardless -- the plant is a real carbon sink and its
            // oxygen is paid for by its water, exactly as Klei's is.
            float wantedCarbonDioxideKg = Biology.OxyfernCarbonDioxideKgPerSecond * dt;
            float takenCarbonDioxideKg = Mathf.Min(wantedCarbonDioxideKg,
                availableCarbonDioxideKg);
            if (takenCarbonDioxideKg > 0f)
            {
                GasMixtureFacade.ConvertToVanilla(cell, carbonDioxideIdx, takenCarbonDioxideKg,
                    temperatureK);
                GasMixtureFacade.RemoveVanillaMass(cell, takenCarbonDioxideKg);
                ConsumedCarbonDioxideKg += takenCarbonDioxideKg;
            }

            float oxygenKg = Biology.OxyfernOxygenKgPerSecond * dt;
            GasMixtureFacade.Inject(cell, oxygenIdx, oxygenKg, temperatureK);
            EmittedKg += oxygenKg;

            if (nitrogenIdx != ushort.MaxValue)
            {
                float nitrogenKg = Biology.OxyfernNitrogenKgPerSecond * dt;
                GasMixtureFacade.Inject(cell, nitrogenIdx, nitrogenKg, temperatureK);
                EmittedKg += nitrogenKg;
            }
        }
    }

    /// <summary>
    /// PLANTS CHECK THE PRESSURE OF A ROOM THEY CANNOT SEE, and this fixes that.
    ///
    /// `PressureVulnerable` decides whether a plant will grow by flooding the cells around it and
    /// summing `Grid.Mass` for each one whose `Grid.Element` it considers a safe atmosphere. Both
    /// of those are vanilla's frozen view of a cell the mixture layer owns, so an Oxyfern standing
    /// in a perfectly good 118 kPa corridor reads whatever that cell held at promotion time --
    /// usually nothing -- decides the pressure is wrong and the atmosphere unsafe, and refuses to
    /// grow. Reported from a live run, where the plants were visibly sitting in breathable air and
    /// complaining about it.
    ///
    /// `GetPressureOverArea` is the seam: it is what both the growth check and the displayed
    /// pressure amount go through. For a promoted cell the answer comes from the real mixture
    /// instead -- the same units, kilograms in the cell, because that is what vanilla's own sum is
    /// despite the name.
    ///
    /// The safe-element test is answered from the mixture too. A plant whose safe list names any
    /// species actually present is standing in an atmosphere it can live in, which is exactly the
    /// judgement vanilla makes with one element and could not make with several.
    /// </summary>
    [HarmonyPatch(typeof(PressureVulnerable), "GetPressureOverArea")]
    internal static class PressureVulnerable_GetPressureOverArea_Biology
    {
        private static bool Prefix(PressureVulnerable __instance, int cell, ref float __result)
        {
            try
            {
                if (!Grid.IsValidCell(cell) || !GasMixtureFacade.IsRoomOwned(cell))
                {
                    return true;
                }

                GasMixtureFacade.GasComponent[] composition =
                    GasMixtureFacade.TryGetComposition(cell);
                if (composition.Length == 0)
                {
                    return true;
                }

                float massKg = 0f;
                bool safe = false;
                Element dominant = null;
                float dominantMassKg = 0f;
                for (int i = 0; i < composition.Length; i++)
                {
                    ushort idx = composition[i].ElementIdx;
                    if (idx >= ElementLoader.elements.Count)
                    {
                        continue;
                    }
                    Element element = ElementLoader.elements[idx];
                    if (element == null)
                    {
                        continue;
                    }
                    massKg += composition[i].MassKg;
                    if (composition[i].MassKg > dominantMassKg)
                    {
                        dominantMassKg = composition[i].MassKg;
                        dominant = element;
                    }
                    if (__instance.IsSafeElement(element))
                    {
                        safe = true;
                    }
                }

                __instance.testAreaElementSafe = safe;
                if (dominant != null)
                {
                    __instance.currentAtmoElement = dominant;
                }
                __result = massKg;
                return false;
            }
            catch
            {
                // Growth checks run constantly. Degrade to vanilla's own answer rather than
                // killing every plant in the colony.
            }
            return true;
        }
    }

    /// <summary>
    /// Puts <see cref="PuftMixtureDietComponent"/> on every Puft variant.
    ///
    /// Hooked on `BasePuftConfig.BasePuft`, which every Puft morph is built from, so the Dense
    /// Puft and the Squeaky Puft get it too without naming them.
    /// </summary>
    [HarmonyPatch(typeof(BasePuftConfig), "BasePuft")]
    internal static class BasePuftConfig_BasePuft_Biology
    {
        private static void Postfix(GameObject __result)
        {
            if (__result != null)
            {
                __result.AddOrGet<PuftMixtureDietComponent>();
            }
        }
    }

    /// <summary>
    /// Puts <see cref="OxyfernAirEmitterComponent"/> on the Oxyfern prefab.
    /// </summary>
    [HarmonyPatch(typeof(OxyfernConfig), "CreatePrefab")]
    internal static class OxyfernConfig_CreatePrefab_Biology
    {
        private static void Postfix(GameObject __result)
        {
            if (__result != null)
            {
                __result.AddOrGet<OxyfernAirEmitterComponent>();
            }
        }
    }
}
