using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// THE SODA FOUNTAIN STOPS DELETING CARBON DIOXIDE.
    ///
    /// Vanilla: <c>SodaFountainConfig</c> sets
    /// <c>ingredientMassPerUse = 1f</c> and <c>waterMassPerUse = 5f</c>, and
    /// <c>SodaFountainWorkable.OnCompleteWork</c> calls <c>Storage.ConsumeAndGetDisease</c> on both.
    /// The water is drunk, which is the same thing eating is. The kilogram of carbon dioxide is
    /// simply gone. Nothing emits it again, so every serving deletes a kilogram of gas, which
    /// breaks conservation of matter.
    ///
    /// Two changes, and both are needed:
    /// <list type="bullet">
    /// <item><b>A realistic dose.</b> Soda is carbonated to roughly 7-8 g of CO2 per litre (it is
    /// filled cold at 3-4 atm, and Henry's law gives about 3.3 g/kg per atm at 4 °C). Five litres
    /// of water therefore takes <see cref="CarbonationKgPerServing"/>, 40 g, not 1 kg. That
    /// is 25 times less than vanilla, and the fountain's own descriptor and <c>IsReady</c> both
    /// read the field, so the UI and the readiness gate follow on their own.</item>
    /// <item><b>The gas comes back.</b> What the drink consumed is released at the drinker's
    /// mouth when the drink is finished, through <c>CO2Manager.SpawnBreath</c>. That is the
    /// exact call an exhalation makes, so the burp inherits everything a breath already does:
    /// a puff in open air, a bubble under water, and a straight injection into the mixture
    /// when Mod 1 owns the room (<see cref="CO2Manager_SpawnBreath_AirPhysiology"/>).</item>
    /// </list>
    ///
    /// WHY ONE BURP AT THE END rather than a slow release. Dissolved gas exists, but it lives in
    /// liquid cells and carriers, and a duplicant is neither, so nothing can hold the CO2 inside
    /// the duplicant between drinking and exhaling. Releasing it all at once is the honest
    /// version of what can be represented.
    ///
    /// WHY THE STORED TEMPERATURE and not body heat. A real burp leaves at body temperature, but
    /// in this game warming the gas would create energy that nothing pays for. The CO2 leaves
    /// at the temperature it was held at, which is the mass-weighted temperature of the stored
    /// chunks it came from.
    ///
    /// WHAT IS NOT CHANGED: the 5 kg of water, which is drunk (the same as food being eaten), and
    /// the manual delivery (4 kg capacity). A full delivery now lasts 100 servings instead of 4.
    /// That is a consequence of the dose, not a separate tuning.
    /// </summary>
    internal static class SodaFountainCarbonation
    {
        /// <summary>
        /// The CO2 dose one 5 kg serving takes: 8 g/L × 5 L. This is also the mass
        /// released when the drink is finished.
        /// </summary>
        internal const float CarbonationKgPerServing = 0.04f;

        /// <summary>
        /// Where a duplicant's breath leaves them, used when the drinker has no
        /// <c>OxygenBreather</c> to ask. It matches the breather's own <c>mouthOffset.y</c>.
        /// </summary>
        private const float FallbackMouthHeight = 0.97f;

        /// <summary>
        /// THE FOUNTAIN NOW DRINKS CARBONATED WATER. Kilograms of carbon dioxide DISSOLVED in the
        /// water this storage holds -- the dissolved cargo, riding the stored chunk
        /// (<c>DissolvedCargo.ItemGas</c>) -- as opposed to the loose carbon dioxide a duplicant
        /// fetched, which <see cref="Weigh"/> counts.
        ///
        /// This is the whole point of the carbonation chain. A pond bubbled with CO2 under
        /// pressure reaches soda strength, a pump lifts that water, DissolvedCargo carries the dissolved gas
        /// through the pipe, and the fountain's own vanilla <c>ConduitConsumer</c> puts it in
        /// storage still dissolved. Nothing has to be delivered by hand.
        /// </summary>
        internal static float DissolvedCarbonDioxideKg(Storage storage)
        {
            float kg = 0f;
            List<GameObject> items = storage != null ? storage.items : null;
            if (items == null)
            {
                return 0f;
            }
            for (int i = 0; i < items.Count; i++)
            {
                GameObject item = items[i];
                if (item == null || !item.HasTag(GameTags.Water))
                {
                    continue;
                }
                kg += DissolvedCargo.ItemGas(item, SimHashes.CarbonDioxide);
            }
            return kg;
        }

        /// <summary>Kilograms and mass-weighted temperature of one element held in a storage.</summary>
        internal static void Weigh(Storage storage, SimHashes element, out float kg,
            out float temperature)
        {
            kg = 0f;
            float heat = 0f;
            List<GameObject> items = storage != null ? storage.items : null;
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    GameObject item = items[i];
                    PrimaryElement pe = item != null ? item.GetComponent<PrimaryElement>() : null;
                    if (pe == null || pe.ElementID != element || pe.Mass <= 0f)
                    {
                        continue;
                    }
                    kg += pe.Mass;
                    heat += pe.Mass * pe.Temperature;
                }
            }
            temperature = kg > 0f ? heat / kg : 0f;
        }

        /// <summary>
        /// Puts <paramref name="kg"/> of carbon dioxide back into the world at the drinker's
        /// mouth, by the same arithmetic <c>OxygenBreather.Sim200ms</c> uses to place a breath:
        /// the mouth offset mirrored by facing, clamped back into the duplicant's own column.
        /// </summary>
        internal static void Release(WorkerBase worker, float kg, float temperature)
        {
            Vector3 feet = worker.transform.GetPosition();
            Vector3 mouth = feet;
            Facing facing = worker.GetComponent<Facing>();
            bool flip = facing != null && facing.GetFacing();
            OxygenBreather breather = worker.GetComponent<OxygenBreather>();
            if (breather != null)
            {
                mouth.x += flip ? -breather.mouthOffset.x : breather.mouthOffset.x;
                mouth.y += breather.mouthOffset.y;
            }
            else
            {
                mouth.y += FallbackMouthHeight;
            }
            mouth.z -= 0.5f;
            if (Mathf.FloorToInt(mouth.x) != Mathf.FloorToInt(feet.x))
            {
                mouth.x = Mathf.Floor(feet.x) + (flip ? 0.01f : 0.99f);
            }

            if (CO2Manager.instance != null)
            {
                CO2Manager.instance.SpawnBreath(mouth, kg, temperature, flip);
                return;
            }
            // No CO2Manager means no running game world to exhale into. Put the mass in the
            // cell directly rather than lose it.
            int cell = Grid.PosToCell(mouth);
            if (Grid.IsValidCell(cell))
            {
                // The same message, and the same event tag, CO2Manager uses when a puff lands.
                SimMessages.ModifyMass(cell, kg, byte.MaxValue, 0,
                    CellEventLogger.Instance.CO2ManagerFixedUpdate, temperature,
                    SimHashes.CarbonDioxide);
            }
        }
    }

    /// <summary>The 40 g dose, set where vanilla sets its 1 kg.</summary>
    [HarmonyPatch(typeof(SodaFountainConfig), nameof(SodaFountainConfig.ConfigureBuildingTemplate))]
    internal static class SodaFountainConfig_ConfigureBuildingTemplate_Carbonation
    {
        private static void Postfix(GameObject go)
        {
            SodaFountain fountain = go != null ? go.GetComponent<SodaFountain>() : null;
            if (fountain != null)
            {
                fountain.ingredientMassPerUse = SodaFountainCarbonation.CarbonationKgPerServing;
            }
        }
    }

    /// <summary>
    /// A SERVING CAN BE MADE FROM CARBONATED WATER, not only from a kilogram of carbon dioxide
    /// somebody carried over in their arms.
    ///
    /// Vanilla's readiness test (<c>SodaFountain.States.IsReady</c>) asks two questions: is there
    /// <c>waterMassPerUse</c> of water in the storage, and is there <c>ingredientMassPerUse</c> of
    /// the ingredient tag. The second one can only ever see a loose CHUNK of carbon dioxide,
    /// because that is the only form vanilla has. Dissolved gas is not a chunk -- it is a record
    /// riding the water (DissolvedCargo) -- so a fountain fed by a carbonated pond reads as having no
    /// ingredient at all and never offers the chore.
    ///
    /// This postfix adds the second source. The fountain is ready when the water is there AND the
    /// dose is available EITHER as a chunk (vanilla, untouched) OR dissolved in that water. The
    /// dissolved test is the carbonation criterion stated exactly:
    /// <c>ingredientMassPerUse</c> of CO2 inside <c>waterMassPerUse</c> of water is 40 g in 5 kg,
    /// which is 8 g/kg -- soda strength, and the number the Dissolved Gas Sensor's threshold is
    /// set to in the showcase.
    ///
    /// WHY A POSTFIX AND NOT A REPLACEMENT: vanilla's answer is never overturned. A fountain that
    /// vanilla calls ready stays ready. This only turns a false into a true, so with no dissolved
    /// gas anywhere in the world the fountain behaves exactly as it does in an unmodded game.
    /// </summary>
    internal static class SodaFountainStates_IsReady_Dissolved
    {
        internal static void Install(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(SodaFountain.States), "IsReady");
            if (target == null)
            {
                Mod1Log.Warn("soda fountain: SodaFountain.States.IsReady was not found, so a "
                    + "fountain fed only carbonated water will never offer a drink.");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(
                AccessTools.Method(typeof(SodaFountainStates_IsReady_Dissolved), "Postfix")));
        }

        private static void Postfix(SodaFountain.StatesInstance smi, ref bool __result)
        {
            if (__result || smi == null || smi.master == null)
            {
                return;
            }
            try
            {
                SodaFountain fountain = smi.master;
                if (fountain.ingredientTag != SimHashes.CarbonDioxide.CreateTag())
                {
                    return;
                }
                Storage storage = fountain.GetComponent<Storage>();
                if (storage == null)
                {
                    return;
                }
                PrimaryElement water = storage.FindPrimaryElement(SimHashes.Water);
                if (water == null || water.Mass < fountain.waterMassPerUse)
                {
                    return;
                }
                if (SodaFountainCarbonation.DissolvedCarbonDioxideKg(storage)
                    < fountain.ingredientMassPerUse)
                {
                    return;
                }
                __result = true;
            }
            catch (System.Exception e)
            {
                Mod1Log.Warn("soda fountain: could not test the dissolved CO2 for readiness ("
                    + e.Message + ").");
            }
        }
    }

    /// <summary>
    /// Releases what the drink actually consumed, measured rather than assumed, FROM BOTH SOURCES.
    ///
    /// <list type="bullet">
    /// <item><b>The chunk.</b> The prefix weighs the stored CO2 and the postfix weighs it again;
    /// the difference is what vanilla's <c>ConsumeAndGetDisease</c> took. A fountain short of a
    /// full dose, or another mod changing the dose, still conserves exactly.</item>
    /// <item><b>The dissolved gas.</b> Vanilla consumes 5 kg of water without knowing that
    /// anything is dissolved in it. The framework does know: <c>Storage.ConsumeAndGetDisease</c>
    /// takes the matching share of a chunk's cargo off it,
    /// and hands it to whatever <c>DissolvedCargo.ClaimConsumedCargo</c> scope is open -- this
    /// one. So the carbon dioxide that was dissolved in the five kilograms the duplicant drank
    /// comes back out of the duplicant, which is what happens when a person drinks a fizzy drink
    /// and is the entire behaviour this file was written for.</item>
    /// </list>
    ///
    /// Anything else dissolved in that water -- oxygen from an aerated pond, say -- is not burped:
    /// the claim's fallback releases it at the fountain, because a duplicant exhaling somebody's
    /// dissolved oxygen as a gas puff is a claim this mod cannot support. Nothing is deleted
    /// either way.
    ///
    /// This only acts when the ingredient is carbon dioxide. If another mod has put something
    /// else in the fountain, this patch does not guess what that should turn into.
    /// </summary>
    [HarmonyPatch(typeof(SodaFountainWorkable), "OnCompleteWork")]
    internal static class SodaFountainWorkable_OnCompleteWork_Carbonation
    {
        internal struct Before
        {
            public bool Active;
            public float Kg;
            public float Temperature;
            public float WaterTemperature;
            public DissolvedCargo.ConsumedCargoClaim Claim;
        }

        private static void Prefix(SodaFountainWorkable __instance, out Before __state)
        {
            __state = default(Before);
            try
            {
                SodaFountain fountain = __instance.GetComponent<SodaFountain>();
                if (fountain == null
                    || fountain.ingredientTag != SimHashes.CarbonDioxide.CreateTag())
                {
                    return;
                }
                Storage storage = __instance.GetComponent<Storage>();
                SodaFountainCarbonation.Weigh(storage, SimHashes.CarbonDioxide, out __state.Kg,
                    out __state.Temperature);
                SodaFountainCarbonation.Weigh(storage, SimHashes.Water, out float _,
                    out __state.WaterTemperature);

                // The fountain's own cell is where anything this serving does not account for
                // goes back to the world, which is the closest honest place: it came out of a
                // tank standing there.
                int cell = Grid.PosToCell(__instance.transform.GetPosition());
                float fallbackK = __state.WaterTemperature > 0f
                    ? __state.WaterTemperature
                    : (Grid.IsValidCell(cell) && Grid.Temperature[cell] > 0f
                        ? Grid.Temperature[cell] : 293.15f);
                __state.Claim = DissolvedCargo.ClaimConsumedCargo(cell, fallbackK);
                __state.Active = true;
            }
            catch (System.Exception e)
            {
                Mod1Log.Warn("soda fountain: could not weigh the stored CO2 before a serving ("
                    + e.Message + "); this serving's CO2 is not released.");
            }
        }

        private static void Postfix(SodaFountainWorkable __instance, WorkerBase worker,
            Before __state)
        {
            if (!__state.Active)
            {
                return;
            }
            try
            {
                SodaFountainCarbonation.Weigh(__instance.GetComponent<Storage>(),
                    SimHashes.CarbonDioxide, out float after, out float _);
                float fromChunks = __state.Kg - after;
                if (fromChunks < 0f)
                {
                    fromChunks = 0f;
                }

                float fromWater = 0f;
                if (__state.Claim != null)
                {
                    fromWater = __state.Claim.Gas(SimHashes.CarbonDioxide);
                    if (fromWater > 0f)
                    {
                        // Taken out of the claim so the scope's own release does not also send
                        // it: this serving is now responsible for it.
                        __state.Claim.TakeGas(SimHashes.CarbonDioxide);
                    }
                }

                float consumed = fromChunks + fromWater;
                if (consumed > 0f && worker != null)
                {
                    // The chunk's CO2 leaves at the temperature it was held at; the dissolved
                    // CO2 at the water's. One release, so the two are mass-weighted together.
                    float temperature = fromChunks > 0f && fromWater > 0f
                        ? (fromChunks * __state.Temperature
                            + fromWater * __state.WaterTemperature) / consumed
                        : (fromChunks > 0f ? __state.Temperature : __state.WaterTemperature);
                    SodaFountainCarbonation.Release(worker, consumed, temperature);
                }
            }
            catch (System.Exception e)
            {
                Mod1Log.Warn("soda fountain: could not release a serving's CO2 (" + e.Message
                    + ").");
            }
            finally
            {
                if (__state.Claim != null)
                {
                    // Closes the scope and releases anything this serving did not account for at
                    // the fountain's own cell.
                    __state.Claim.Dispose();
                }
            }
        }
    }
}
