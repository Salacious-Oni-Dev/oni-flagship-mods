using System.Collections.Generic;
using Klei;
using KSerialization;
using OniFramework;
using TUNING;
using UnityEngine;

namespace Mod2MatterEnvironment
{
    /// <summary>
    /// THE PHYSICAL CAMPFIRE. (The smoke system came from an idea by Kaz.)
    /// A proof of concept for fire from SOLID fuel: dupes bring it wood, peat or coal, a dupe has to
    /// light it, and it burns for real until the fuel or the air runs out.
    ///
    /// VANILLA'S CAMPFIRE (Frosty Planet, <c>CampfireConfig</c>) is reused for its shape -- a 1x2 floor
    /// building, fuel brought by <c>ManualDeliveryKG</c> on FetchCritical into an insulated store,
    /// lit and unlit states, a <c>Light2D</c> -- and NOT for its physics, which is a magic conversion:
    /// its <c>ElementConverter</c> turns 0.025 kg/s of wood into 0.004 kg/s of CO2 and deletes the
    /// other 84 %, it needs no oxygen, a <c>DirectVolumeHeater</c> adds a flat 20 kW that has nothing
    /// to do with the fuel, and it lights itself whenever it holds wood.
    ///
    /// OURS BURNS. Every sim tick the stored fuel goes through <see cref="Combustion.BurnAt"/> -- the
    /// framework's burn -- at a POWER, not a mass rate: <see cref="PowerKW"/> divided by the fuel's
    /// heating value (<see cref="FuelKgPerSecondOf"/>), so wood burns about 3.3 g/s (about 2 kg a
    /// cycle), peat 3.0 and coal 2.0, and every fuel gives the same heat. The oxygen is drawn from the
    /// room, CO2, steam and soot (the smoke) go into the fire's cell, and the heat is the fuel's full
    /// heating value at <see cref="Combustion.EnergyScale"/> 1: nothing is deleted. Before this, the fire burned vanilla's 0.025 kg/s of wood and documented an energy
    /// scale of 0.044 for vanilla's 20 kW that nothing ever set. A deliberate difference from Stationeers: its nearest solid-fuel burner (<c>SolidFuelGenerator</c>) smelts a fuel
    /// item per fixed number of ticks with no oxidiser test.
    ///
    /// IT HAS TO BE LIT. It is built unlit, and fuel alone does not light it. While "Keep it lit" is
    /// on and it holds fuel, it raises a LIGHT THE FIRE errand (<see cref="CampfireLighter"/>, three
    /// seconds' work, vanilla's Toggle errand and its remote-use animation); when a dupe finishes it,
    /// the fire sparks its cell (<see cref="Ignition.Spark"/>) and starts burning.
    ///
    /// IT GOES OUT PHYSICALLY, and then needs lighting again:
    /// <list type="bullet">
    /// <item>OUT OF FUEL: nothing burnable left in its store;</item>
    /// <item>STARVED OF AIR: a burn came back extinguished (less than <see cref="Combustion.MinOxygenRatio"/>
    /// of its oxygen within reach); the fuel drawn for it goes back into the store, and a relight is
    /// not tried for <see cref="RelightAfterStarvedSeconds"/>;</item>
    /// <item>DROWNED: at least <see cref="Combustion.DrownKg"/> of liquid in its cell
    /// (<see cref="Combustion.IsDrowned"/>), seen here before a burn or by
    /// Combustion on a burn in flight. Less is a puddle (its own steam condenses on the floor), and
    /// the fire boils it off with its own heat before the products get the rest.</item>
    /// </list>
    ///
    /// FUELS: any solid with a <see cref="Combustion"/> row; the menu offers wood, peat and coal, one
    /// at a time (the button cycles them), and the dupes fetch whichever is chosen. Whatever is in
    /// the store burns, oldest first, so switching fuel lets the old fuel burn down.
    ///
    /// ART: <c>campfire_small_kanim</c> (vanilla's) when the game has it loaded; otherwise the Wood
    /// Burner's <c>generatorwood_kanim</c> as a 2x2 donor, on the kanim-needed list. The flame itself
    /// is <see cref="FireVisuals"/>, drawn from the burns.
    /// </summary>
    public class PhysicalCampfireConfig : IBuildingConfig
    {
        internal const string Id = "PhysicalCampfire";

        /// <summary>This building's own art: a log pile drawn for it, packed into
        /// <c>anim/assets/PhysicalCampfire</c>.</summary>
        internal const string Kanim = "PhysicalCampfire_kanim";

        /// <summary>What the building wore before it had art of its own. Kept only as a fallback
        /// for a load where our own kanim is missing -- a building whose anim does not resolve
        /// renders nothing at all and is invisible on the map, which is far worse than borrowed
        /// art. Neither of these ships: both belong to the game.</summary>
        internal const string VanillaKanim = "campfire_small_kanim";
        internal const string DonorKanim = "generatorwood_kanim";

        /// <summary>Vanilla's store: 45 kg, topped up below 18 kg.</summary>
        internal const float CapacityKg = 45f;
        internal const float RefillKg = 18f;

        public override BuildingDef CreateBuildingDef()
        {
            KAnimFile unused;
            // Our own art, with the borrowed art only as a fallback. ONE cell wide, which is both
            // what the sprite is drawn to fill and what this building has always been in a game
            // that has vanilla's campfire art, so no blueprint that places a campfire moves.
            bool ownArt = Assets.TryGetAnim(Kanim, out unused);
            bool vanillaArt = Assets.TryGetAnim(VanillaKanim, out unused);
            string anim = ownArt ? Kanim : (vanillaArt ? VanillaKanim : DonorKanim);
            BuildingDef def = BuildingTemplates.CreateBuildingDef(Id, (ownArt || vanillaArt) ? 1 : 2, 2,
                anim, 100, 10f,
                BUILDINGS.CONSTRUCTION_MASS_KG.TIER2, MATERIALS.RAW_MINERALS, 1600f,
                BuildLocationRule.OnFloor, BUILDINGS.DECOR.BONUS.TIER1, NOISE_POLLUTION.NONE);
            def.Floodable = false;
            def.Entombable = true;
            def.Overheatable = false;
            def.ExhaustKilowattsWhenActive = 0f;
            def.SelfHeatKilowattsWhenActive = 0f;
            def.ViewMode = OverlayModes.Temperature.ID;
            def.AudioCategory = "Metal";
            def.AudioSize = "small";
            def.DefaultAnimState = "off";
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            go.AddOrGet<LoopingSounds>();
            Storage storage = go.AddOrGet<Storage>();
            storage.capacityKg = CapacityKg;
            storage.showInUI = true;
            storage.allowItemRemoval = false;
            storage.SetDefaultStoredItemModifiers(Storage.StandardInsulatedStorage);

            ManualDeliveryKG delivery = go.AddOrGet<ManualDeliveryKG>();
            delivery.SetStorage(storage);
            delivery.RequestedItemTag = PhysicalCampfire.Fuels[0].CreateTag();
            delivery.capacity = CapacityKg;
            delivery.refillMass = RefillKg;
            delivery.MinimumMass = 1f;
            delivery.choreTypeIDHash = Db.Get().ChoreTypes.FetchCritical.IdHash;

            go.AddOrGet<Operational>();
            Light2D light = go.AddOrGet<Light2D>();
            light.Range = 6f;
            light.Color = new Color(0.8f, 0.6f, 0f, 1f);
            light.Lux = 450;

            go.AddOrGet<CampfireLighter>();
            go.AddOrGet<PhysicalCampfire>();
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
        }
    }

    /// <summary>Why a <see cref="PhysicalCampfire"/> last went out.</summary>
    public enum CampfireOut
    {
        None,
        OutOfFuel,
        StarvedOfAir,
        Drowned,
        LetGo,
    }

    /// <summary>The fire itself. See <see cref="PhysicalCampfireConfig"/>.</summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class PhysicalCampfire : KMonoBehaviour, ISim200ms
    {
        /// <summary>The fuels the menu offers, in the order its button cycles them.</summary>
        public static readonly SimHashes[] Fuels = { SimHashes.WoodLog, SimHashes.Peat, SimHashes.Carbon };

        private static readonly string[] FuelNames = { "Wood", "Peat", "Coal" };

        /// <summary>The fire's power, kW: what it burns its fuel at. Vanilla's
        /// campfire heats with a flat 20 kW that has nothing to do with its fuel; ours releases the
        /// fuel's whole heating value, at this rate.</summary>
        public const float PowerKW = 60f;

        /// <summary>Fuel burned per second at <see cref="PowerKW"/>, kg/s: the power over the fuel's
        /// heating value (<see cref="CombustionFuel.HeatingValueJPerKg"/>). Zero for a fuel with no
        /// combustion row or no heating value, which then never burns.</summary>
        public static float FuelKgPerSecondOf(SimHashes fuel)
        {
            CombustionFuel row;
            if (!Combustion.TryGet(fuel, out row) || !(row.HeatingValueJPerKg > 0f))
            {
                return 0f;
            }
            return PowerKW * 1000f / row.HeatingValueJPerKg;
        }

        /// <summary>How far a burn reaches for oxygen, cells.</summary>
        public static byte OxygenRadius = 4;

        /// <summary>After going out for want of air, the wait before a relight is asked for, game s.
        /// Without it a dupe would relight a choked fire every few seconds.</summary>
        public static float RelightAfterStarvedSeconds = 10f;

        /// <summary>Every campfire in the world, for a rig to find.</summary>
        public static readonly List<PhysicalCampfire> All = new List<PhysicalCampfire>();

        private static StatusItem unlitStatus, litStatus, noFuelStatus, starvedStatus, drownedStatus;

        [Serialize]
        private bool keepLit = true;

        [Serialize]
        private bool lit;

        [Serialize]
        private int fuelChoice;

        // Not saved: a counter for a rig, and the burn in flight (a burn lands about two ticks after
        // it is sent; one at a time, and the ticks it spans are owed to the next).
        private bool burnInFlight;
        private float owedSeconds;
        private float relightNotBefore;
        private Chore lightChore;

        [MyCmpGet] private Storage storage;
        [MyCmpGet] private ManualDeliveryKG delivery;
        [MyCmpGet] private Operational operational;
        [MyCmpGet] private Light2D light;
        [MyCmpGet] private KBatchedAnimController anim;
        [MyCmpGet] private KSelectable selectable;
        [MyCmpGet] private CampfireLighter lighter;

        private static readonly EventSystem.IntraObjectHandler<PhysicalCampfire> OnRefreshUserMenuDelegate =
            new EventSystem.IntraObjectHandler<PhysicalCampfire>((c, d) => c.OnRefreshUserMenu(d));

        /// <summary>Whether it is burning.</summary>
        public bool IsLit => lit;

        /// <summary>Whether "Keep it lit" is on (dupes relight it and keep it fed).</summary>
        public bool KeepLit => keepLit;

        /// <summary>Why it last went out.</summary>
        public CampfireOut LastOut { get; private set; }

        /// <summary>The fuel the dupes are fetching.</summary>
        public SimHashes FuelChoice => Fuels[Mathf.Clamp(fuelChoice, 0, Fuels.Length - 1)];

        /// <summary>Since spawn, not saved: times a dupe lit it.</summary>
        public int Lights { get; private set; }

        /// <summary>Since spawn, not saved: times it went out, by any cause.</summary>
        public int Outs { get; private set; }

        /// <summary>Since spawn, not saved: burns that came back lit.</summary>
        public long Burns { get; private set; }

        /// <summary>Since spawn, not saved: fuel the burns consumed, kg.</summary>
        public double FuelBurnedKg { get; private set; }

        /// <summary>Since spawn, not saved: fuel drawn for a burn that went out and put back, kg.</summary>
        public double FuelReturnedKg { get; private set; }

        /// <summary>Since spawn, not saved: fuel burned per element, kg.</summary>
        public readonly Dictionary<SimHashes, double> BurnedByFuel = new Dictionary<SimHashes, double>();

        /// <summary>The fire's cell: the building's own (bottom-left) cell.</summary>
        public int FireCell => Grid.PosToCell(this);

        /// <summary>Whether the fire's cell holds enough liquid to put it out: Combustion's rule
        /// (<see cref="Combustion.IsDrowned"/>, <see cref="Combustion.DrownKg"/>).
        /// The fire burns in its own cell, puddle or not: the products go to a
        /// cell that takes gas (OpenCell), so a liquid fire cell no longer meets the blocked-add path,
        /// and a puddle there is boiled off by the burn's heat. Before, the burn moved to the cell
        /// above a puddle, where there was never anything to boil.</summary>
        public bool IsDrowned => Combustion.IsDrowned(FireCell);

        /// <summary>Burnable fuel in the store, kg.</summary>
        public float FuelKg
        {
            get
            {
                float kg = 0f;
                foreach (GameObject item in storage.items)
                {
                    PrimaryElement pe = item != null ? item.GetComponent<PrimaryElement>() : null;
                    CombustionFuel unused;
                    if (pe != null && Combustion.TryGet(pe.ElementID, out unused))
                    {
                        kg += pe.Mass;
                    }
                }
                return kg;
            }
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            All.Add(this);
            EnsureStatusItems();
            Subscribe((int)GameHashes.RefreshUserMenu, OnRefreshUserMenuDelegate);
            delivery.RequestedItemTag = FuelChoice.CreateTag();
            delivery.Pause(!keepLit, "campfire let go");
            Refresh();
        }

        protected override void OnCleanUp()
        {
            All.Remove(this);
            CancelLightChore();
            base.OnCleanUp();
        }

        public void Sim200ms(float dt)
        {
            if (!lit)
            {
                MaybeAskForALight();
                return;
            }
            if (IsDrowned)
            {
                GoOut(CampfireOut.Drowned);
                return;
            }
            int cell = FireCell;
            owedSeconds += dt;
            if (burnInFlight)
            {
                return;
            }
            SimHashes fuel;
            if (!OldestFuel(out fuel))
            {
                GoOut(CampfireOut.OutOfFuel);
                return;
            }
            float want = FuelKgPerSecondOf(fuel) * owedSeconds;
            owedSeconds = 0f;
            float taken;
            SimUtil.DiseaseInfo disease;
            float temperature;
            storage.ConsumeAndGetDisease(fuel.CreateTag(), want, out taken, out disease, out temperature);
            if (!(taken > 0f))
            {
                GoOut(CampfireOut.OutOfFuel);
                return;
            }
            burnInFlight = true;
            if (!Combustion.BurnAt(cell, fuel, taken, temperature, OxygenRadius,
                r => OnBurned(r, fuel, taken, temperature, disease)))
            {
                burnInFlight = false;
                PutBack(fuel, taken, temperature, disease);
                Debug.LogWarning("[Mod2MatterEnvironment] Campfire: Combustion.BurnAt refused " + fuel
                    + " (" + taken + " kg); is Combustion installed?");
                GoOut(CampfireOut.StarvedOfAir);
            }
        }

        /// <summary>Called by <see cref="CampfireLighter"/> when a dupe finishes lighting it.</summary>
        internal void LitByDupe()
        {
            lightChore = null;
            if (lit)
            {
                return;
            }
            lit = true;
            Lights++;
            LastOut = CampfireOut.None;
            owedSeconds = 0f;
            Ignition.Spark(FireCell, "Campfire");
            Refresh();
        }

        /// <summary>"Keep it lit": dupes keep it fed and relight it. Off lets it burn down and stops
        /// the fetching; a lit fire is not put out by it.</summary>
        public void SetKeepLit(bool on)
        {
            keepLit = on;
            delivery.Pause(!on, "campfire let go");
            if (!on)
            {
                CancelLightChore();
            }
            Refresh();
        }

        /// <summary>Choose the fuel the dupes fetch, an index into <see cref="Fuels"/>.</summary>
        public void SetFuelChoice(int index)
        {
            fuelChoice = Mathf.Clamp(index, 0, Fuels.Length - 1);
            delivery.RequestedItemTag = FuelChoice.CreateTag();
            Refresh();
        }

        private void OnBurned(BurnResult r, SimHashes fuel, float kg, float temperature,
            SimUtil.DiseaseInfo disease)
        {
            burnInFlight = false;
            if (this == null)
            {
                return;
            }
            // Drowned first: a burn Combustion refused as drowned (at entry, or when its oxygen came
            // back) is also Extinguished, and must not be booked as a fire starved of air.
            if (r.Drowned)
            {
                PutBack(fuel, kg, temperature, disease);
                GoOut(CampfireOut.Drowned);
                return;
            }
            if (r.Extinguished)
            {
                PutBack(fuel, kg, temperature, disease);
                GoOut(CampfireOut.StarvedOfAir);
                return;
            }
            Burns++;
            FuelBurnedKg += r.FuelKg;
            double sum;
            BurnedByFuel.TryGetValue(fuel, out sum);
            BurnedByFuel[fuel] = sum + r.FuelKg;
        }

        private void PutBack(SimHashes fuel, float kg, float temperature, SimUtil.DiseaseInfo disease)
        {
            storage.AddOre(fuel, kg, temperature, disease.idx, disease.count);
            FuelReturnedKg += kg;
        }

        private void GoOut(CampfireOut why)
        {
            lit = false;
            LastOut = why;
            Outs++;
            owedSeconds = 0f;
            if (why == CampfireOut.StarvedOfAir && GameClock.Instance != null)
            {
                relightNotBefore = GameClock.Instance.GetTime() + RelightAfterStarvedSeconds;
            }
            Refresh();
        }

        private void MaybeAskForALight()
        {
            bool want = keepLit && FuelKg > 0f && !IsDrowned
                && (GameClock.Instance == null || GameClock.Instance.GetTime() >= relightNotBefore);
            if (want && lightChore == null)
            {
                lightChore = new WorkChore<CampfireLighter>(Db.Get().ChoreTypes.Toggle, lighter,
                    on_end: c => { if (lightChore == c) lightChore = null; },
                    only_when_operational: false, ignore_building_assignment: true);
            }
            else if (!want && lightChore != null)
            {
                CancelLightChore();
            }
        }

        private void CancelLightChore()
        {
            if (lightChore != null)
            {
                Chore c = lightChore;
                lightChore = null;
                c.Cancel("campfire no longer wants lighting");
            }
        }

        // The oldest burnable item in the store.
        private bool OldestFuel(out SimHashes fuel)
        {
            foreach (GameObject item in storage.items)
            {
                PrimaryElement pe = item != null ? item.GetComponent<PrimaryElement>() : null;
                CombustionFuel unused;
                if (pe != null && pe.Mass > 0f && Combustion.TryGet(pe.ElementID, out unused))
                {
                    fuel = pe.ElementID;
                    return true;
                }
            }
            fuel = SimHashes.Vacuum;
            return false;
        }

        private void Refresh()
        {
            operational.SetActive(lit);
            if (light != null)
            {
                light.enabled = lit;
            }
            anim.Play(lit ? "on" : "off", lit ? KAnim.PlayMode.Loop : KAnim.PlayMode.Once);
            StatusItem s;
            if (lit)
            {
                s = litStatus;
            }
            else if (LastOut == CampfireOut.StarvedOfAir)
            {
                s = starvedStatus;
            }
            else if (LastOut == CampfireOut.Drowned)
            {
                s = drownedStatus;
            }
            else if (FuelKg <= 0f)
            {
                s = noFuelStatus;
            }
            else
            {
                s = unlitStatus;
            }
            selectable.SetStatusItem(Db.Get().StatusItemCategories.Main, s, this);
        }

        private void OnRefreshUserMenu(object data)
        {
            Game.Instance.userMenu.AddButton(gameObject, keepLit
                ? new KIconButtonMenu.ButtonInfo("action_building_disabled", "Let It Go Out",
                    () => { SetKeepLit(false); Game.Instance.userMenu.Refresh(gameObject); },
                    tooltipText: "Stop fetching fuel and stop relighting. It burns what it has.")
                : new KIconButtonMenu.ButtonInfo("action_building_disabled", "Keep It Lit",
                    () => { SetKeepLit(true); Game.Instance.userMenu.Refresh(gameObject); },
                    tooltipText: "Duplicants fetch fuel and light the fire whenever it is out."));
            int next = (fuelChoice + 1) % Fuels.Length;
            Game.Instance.userMenu.AddButton(gameObject, new KIconButtonMenu.ButtonInfo(
                "action_switch_toggle", "Fuel: " + FuelNames[fuelChoice],
                () => { SetFuelChoice(next); Game.Instance.userMenu.Refresh(gameObject); },
                tooltipText: "Duplicants fetch " + FuelNames[fuelChoice].ToLowerInvariant()
                    + ". Click to fetch " + FuelNames[next].ToLowerInvariant()
                    + " instead; what is already in the fire burns first."));
        }

        private static void EnsureStatusItems()
        {
            if (litStatus != null)
            {
                return;
            }
            litStatus = Status("Mod2CampfireLit", "Burning",
                "Burning its fuel: drawing oxygen from the room, giving off heat, smoke and CO2.");
            unlitStatus = Status("Mod2CampfireUnlit", "Unlit",
                "Has fuel but nobody has lit it. With Keep It Lit on, a duplicant will.");
            noFuelStatus = Status("Mod2CampfireNoFuel", "Out of Fuel",
                "Nothing left to burn. With Keep It Lit on, duplicants bring more.");
            starvedStatus = Status("Mod2CampfireStarved", "Starved of Air",
                "Went out: too little oxygen within reach to keep burning.");
            drownedStatus = Status("Mod2CampfireDrowned", "Drowned", "Went out: liquid in the fire.");
        }

        private static StatusItem Status(string id, string name, string tooltip)
        {
            return new StatusItem(id, name, tooltip, "", StatusItem.IconType.Info,
                NotificationType.Neutral, false, OverlayModes.None.ID);
        }
    }

    /// <summary>
    /// LIGHT THE FIRE: the errand a <see cref="PhysicalCampfire"/> raises. Vanilla's Toggleable work,
    /// in shape: three seconds, the remote-use animation, the Toggle errand type.
    /// </summary>
    public class CampfireLighter : Workable
    {
        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            SetWorkTime(3f);
            workerStatusItem = Db.Get().DuplicantStatusItems.Toggling;
            overrideAnims = new KAnimFile[] { Assets.GetAnim("anim_use_remote_kanim") };
            synchronizeAnims = false;
        }

        protected override void OnCompleteWork(WorkerBase worker)
        {
            base.OnCompleteWork(worker);
            PhysicalCampfire fire = GetComponent<PhysicalCampfire>();
            if (fire != null)
            {
                fire.LitByDupe();
            }
        }
    }

    /// <summary>The campfire's strings and build-menu entry. Called from Mod.OnLoad.</summary>
    internal static class Campfire
    {
        internal static void Install()
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + PhysicalCampfireConfig.Id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", "Campfire");
            Strings.Add(prefix + "DESC",
                "A real fire: it needs fuel, a light, and air to breathe.");
            Strings.Add(prefix + "EFFECT",
                "Burns wood, peat or coal that duplicants bring, once a duplicant lights it. It draws "
                + "oxygen from the room and gives off heat, smoke and carbon dioxide. Goes out when the "
                + "fuel or the air runs out, or when it is flooded, and must be lit again.");
            ModUtil.AddBuildingToPlanScreen(new HashedString("Utilities"), PhysicalCampfireConfig.Id,
                BUILDINGS.PlanSubcategoryName.temperature.ToString(), "SpaceHeater");
        }
    }
}
