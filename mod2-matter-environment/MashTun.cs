using System.Collections.Generic;
using HarmonyLib;
using Klei;
using OniFramework;
using TUNING;
using UnityEngine;

namespace Mod2MatterEnvironment
{
    /// <summary>
    /// THE MASH TUN.
    /// Fruit goes in; its own water comes out down a pipe carrying its sugar
    /// (<see cref="Fermentables"/>), and the rest drops as pulp. Nothing is converted by a ratio
    /// someone picked: each kilogram splits by its own composition (<see cref="FruitSugar"/>), and
    /// the sugar is matter the fermentation slices will later turn into ethanol and CO2.
    ///
    /// THE SHAPE IS THE JUICER'S (JuicerConfig: 3x4, a liquid input, fruit by ManualDeliveryKG into a
    /// fabricator storage), and the art is the Juicer's until it has its own. Unlike the Juicer it
    /// is not a recreation building and needs no power: crushing fruit in a vat is a slow, passive
    /// job. Water piped in is stored with the juice and leaves with it, so a mash can be diluted.
    ///
    /// STATIONEERS has no mash tun. Its sugar is a dry item made by grinding a whole plant
    /// (ReagentProcessor, rocketstation_Data/StreamingAssets/Data/reagentgrinder.xml:13-19 in the
    /// Stationeers install: 1 ItemSugarCane -> 5 ItemSugar), not a solution, because its
    /// liquids are atmosphere moles with no room for a solute -- STRUCTURAL; the mash tun itself
    /// has no twin in Stationeers.
    /// </summary>
    public class MashTunConfig : IBuildingConfig
    {
        internal const string Id = "Mod2MashTun";

        internal const string DonorKanim = "juicer_kanim";

        /// <summary>Fruit the tun holds, and refills below.</summary>
        internal const float FruitCapacityKg = 20f;

        internal const float FruitRefillKg = 10f;

        /// <summary>Water held before the output pipe takes it; enough for several seconds' juice.
        /// </summary>
        internal const float WaterCapacityKg = 20f;

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(Id, 3, 4, DonorKanim, 30, 30f,
                TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER4, MATERIALS.RAW_METALS, 1600f,
                BuildLocationRule.OnFloor, TUNING.BUILDINGS.DECOR.NONE, NOISE_POLLUTION.NONE);
            def.ViewMode = OverlayModes.LiquidConduits.ID;
            def.Floodable = true;
            def.Overheatable = false;
            def.AudioCategory = "Metal";
            def.RequiresPowerInput = false;
            def.InputConduitType = ConduitType.Liquid;
            def.UtilityInputOffset = new CellOffset(-1, 1);
            def.OutputConduitType = ConduitType.Liquid;
            def.UtilityOutputOffset = new CellOffset(1, 1);
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            Storage storage = go.AddOrGet<Storage>();
            storage.capacityKg = FruitCapacityKg + WaterCapacityKg;
            storage.showInUI = true;
            storage.SetDefaultStoredItemModifiers(Storage.StandardFabricatorStorage);

            ConduitConsumer consumer = go.AddOrGet<ConduitConsumer>();
            consumer.conduitType = ConduitType.Liquid;
            consumer.capacityTag = ElementLoader.FindElementByHash(SimHashes.Water).tag;
            consumer.capacityKG = WaterCapacityKg;
            consumer.forceAlwaysSatisfied = true;
            consumer.wrongElementResult = ConduitConsumer.WrongElementResult.Dump;

            ConduitDispenser dispenser = go.AddOrGet<ConduitDispenser>();
            dispenser.conduitType = ConduitType.Liquid;
            dispenser.elementFilter = new[] { SimHashes.Water };
            dispenser.alwaysDispense = true;

            ManualDeliveryKG delivery = go.AddComponent<ManualDeliveryKG>();
            delivery.SetStorage(storage);
            delivery.RequestedItemTag = FruitSugar.MashableTag;
            delivery.capacity = FruitCapacityKg;
            delivery.refillMass = FruitRefillKg;
            delivery.MinimumMass = 1f;
            delivery.choreTypeIDHash = Db.Get().ChoreTypes.MachineFetch.IdHash;
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            RequireInputs inputs = go.GetComponent<RequireInputs>();
            if (inputs != null)
            {
                // Fruit alone makes juice; an empty input pipe is not a fault.
                inputs.SetRequirements(power: false, conduit: false);
            }
            go.AddOrGet<MashTun>();
        }
    }

    /// <summary>
    /// The tun's work: each second, up to <see cref="KgPerSecond"/> of stored fruit split into water
    /// (into the tun's own water, which the dispenser pipes out), sugar riding that water, and pulp
    /// dropped beside the tun as Polluted Dirt. The split is exact -- water + sugar + pulp is the
    /// fruit's mass -- and the counters below let a rig close the ledger.
    /// </summary>
    public class MashTun : KMonoBehaviour, ISim1000ms
    {
        /// <summary>Fruit mashed per second.</summary>
        internal const float KgPerSecond = 1f;

#pragma warning disable CS0649
        [MyCmpGet] private Storage storage;
        [MyCmpGet] private Operational operational;
#pragma warning restore CS0649

        internal static double FruitMashedKg { get; private set; }
        internal static double WaterMadeKg { get; private set; }
        internal static double SugarMadeKg { get; private set; }
        internal static double PulpMadeKg { get; private set; }

        /// <summary>Sugar that had no water chunk to ride and went to its exit instead.</summary>
        internal static double SugarToExitKg { get; private set; }

        internal static void ResetAccounting()
        {
            FruitMashedKg = 0.0;
            WaterMadeKg = 0.0;
            SugarMadeKg = 0.0;
            PulpMadeKg = 0.0;
            SugarToExitKg = 0.0;
        }

        public void Sim1000ms(float dt)
        {
            if (storage == null || (operational != null && !operational.IsOperational))
            {
                return;
            }
            float budget = KgPerSecond * dt;
            foreach (Tag fruit in FruitSugar.Fruits)
            {
                if (!(budget > 0f))
                {
                    break;
                }
                FruitSugar.Composition c;
                if (!FruitSugar.TryGet(fruit, out c))
                {
                    continue;
                }
                GameObject item = storage.FindFirst(fruit);
                PrimaryElement pe = item != null ? item.GetComponent<PrimaryElement>() : null;
                if (pe == null || !(pe.MassPerUnit > 0f))
                {
                    continue;
                }
                float units;
                SimUtil.DiseaseInfo disease;
                float temperatureK;
                storage.ConsumeAndGetDisease(fruit, budget / pe.MassPerUnit, out units, out disease,
                    out temperatureK);
                float kg = units * pe.MassPerUnit;
                if (!(kg > 0f))
                {
                    continue;
                }
                budget -= kg;
                Mash(c, kg, temperatureK, disease);
            }
        }

        private void Mash(FruitSugar.Composition c, float kg, float temperatureK,
            SimUtil.DiseaseInfo disease)
        {
            float water = kg * c.Water;
            float sugar = kg * c.Sugar;
            float pulp = kg - water - sugar;
            FruitMashedKg += kg;

            // The fruit's germs go with its water: that is where they would be in a real mash.
            PrimaryElement chunk = storage.AddLiquid(SimHashes.Water, water, temperatureK,
                disease.idx, disease.count, false, true);
            WaterMadeKg += water;
            if (chunk != null && Fermentables.SugarLane >= 0)
            {
                DissolvedCargo.ItemAdd(chunk.gameObject, Fermentables.SugarLane, sugar);
                SugarMadeKg += sugar;
            }
            else
            {
                SugarToExitKg += sugar;
                Fermentables.Exit(Fermentables.Kind.Sugar, Grid.PosToCell(this), sugar,
                    temperatureK);
            }

            if (pulp > 0f)
            {
                Element dirt = ElementLoader.FindElementByHash(SimHashes.ToxicSand);
                int cell = Grid.PosToCell(this);
                dirt.substance.SpawnResource(Grid.CellToPosCCC(cell, Grid.SceneLayer.Ore), pulp,
                    temperatureK, byte.MaxValue, 0);
                PulpMadeKg += pulp;
            }
        }

        /// <summary>Strings, the build-menu entry, and the Mashable tag on every fruit in the table.
        /// </summary>
        internal static void Install()
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + MashTunConfig.Id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", "Mash Tun");
            Strings.Add(prefix + "DESC",
                "A vat that crushes fruit into its own juice. The sugar in the fruit stays dissolved in "
                + "the juice, and the pulp is left behind.");
            Strings.Add(prefix + "EFFECT",
                "Mashes 1 kg of fruit a second into water carrying the fruit's sugar, piped out of its "
                + "output, and Polluted Dirt pulp. Each fruit splits by its real composition: about "
                + "80-86 % water, 10-16 % sugar, the rest pulp. Water piped in leaves with the juice.");
            ModUtil.AddBuildingToPlanScreen(new HashedString("Food"), MashTunConfig.Id,
                TUNING.BUILDINGS.PlanSubcategoryName.cooking.ToString(), JuicerConfig.ID);
        }
    }

    /// <summary>Puts <see cref="FruitSugar.MashableTag"/> on every fruit the table knows, as its
    /// prefab is registered, so one ManualDeliveryKG can ask for any of them.</summary>
    [HarmonyPatch(typeof(Assets), nameof(Assets.AddPrefab))]
    internal static class MashableFruitTag
    {
        private static readonly HashSet<Tag> fruits = new HashSet<Tag>(FruitSugar.Fruits);

        private static void Postfix(KPrefabID prefab)
        {
            if (prefab != null && fruits.Contains(prefab.PrefabTag))
            {
                prefab.AddTag(FruitSugar.MashableTag);
            }
        }
    }
}
