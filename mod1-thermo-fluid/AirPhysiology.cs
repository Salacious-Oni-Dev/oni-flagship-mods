using System;
using System.Collections.Generic;
using HarmonyLib;
using Klei.AI;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// DUPLICANTS BREATHE AIR, NOT OXYGEN -- the gameplay half of
    /// <see cref="AtmosphereFacade"/>, which measures air but deliberately applies nothing (the
    /// framework grades, the gameplay mod acts).
    ///
    /// WHAT VANILLA DOES. Every breathing decision in ONI funnels
    /// through one small private helper, `GasBreatherFromWorldProvider.GetBreathableCellMass`:
    /// it looks at `Grid.Element[cell]`, asks whether that ONE element carries the `Breathable`
    /// tag, and returns `Grid.Mass[cell]` if it does. `HasOxygen`, `IsLowOxygen` and `ConsumeGas`
    /// all read that number and compare it against two constants from `DUPLICANTSTATS.BASESTATS`:
    /// `LOW_OXYGEN_THRESHOLD = 0.52` kg and `NO_OXYGEN_THRESHOLD = 0.05` kg per cell.
    ///
    /// So vanilla asks "is this cell FULL OF a gas tagged breathable?" -- and it has to, because a
    /// vanilla cell holds exactly one element. Nitrogen in the room would not dilute the oxygen,
    /// it would DISPLACE it entirely, and a 75/25 air mix is simply not expressible.
    ///
    /// WHAT THIS CHANGES. Breathing is graded by OXYGEN PARTIAL PRESSURE, which is the quantity
    /// that actually decides whether a lung works: a 100 kPa room at 25% oxygen and a 25 kPa room
    /// at 100% oxygen are the same breath, and vanilla's mass rule cannot say so. The thresholds
    /// are <see cref="AtmosphereFacade"/>'s -- 16 kPa safe, 12 kPa low, 5 kPa critical -- and for
    /// scale, vanilla's own 0.52 kg/cell low-oxygen line works out to about 39.6 kPa of oxygen in
    /// a 1 m^3 cell at 20 C. The new rule is therefore considerably MORE permissive about thin
    /// air and considerably stricter about everything else, which is the point: air can now be
    /// mostly nitrogen and still be perfectly breathable.
    ///
    /// THREE THINGS VANILLA HAS NO CONCEPT OF AT ALL, added here:
    ///
    /// - A THIRD OXYGEN TIER. Vanilla has "fine" and "low". The specification calls for
    ///   16-12 kPa LOW and 12-5 kPa CRITICAL before suffocation at last begins, so the critical
    ///   band is a new status item and a new effect rather than a re-use of vanilla's.
    /// - AIR TEMPERATURE AS A BREATHING PROBLEM. Comfortable is 0-50 C; outside it the duplicant
    ///   is warned, and below -10 C or above 50 C the lungs actually take damage. Vanilla models
    ///   scalding and hypothermia against the duplicant's own body temperature and has nothing to
    ///   say about the temperature of the gas going into them.
    /// - CARBON DIOXIDE AS A POISON RATHER THAN MERELY NOT-OXYGEN. Above 0.5% it is a headache,
    ///   above 1% it impairs, and above 7% it suffocates a duplicant EVEN IN A ROOM WITH PLENTY OF
    ///   OXYGEN -- which is the rule that makes a sealed corridor with a working air loop
    ///   meaningfully different from a sealed corridor with an oxygen tap.
    ///
    /// WHERE EACH PIECE IS ATTACHED, and why not somewhere simpler:
    ///
    /// - Suffocation is decided in a prefix on `ConsumeGas`, because `ConsumeGas`'s return value
    ///   is what actually sets `OxygenBreather.hasAir` (in
    ///   `OxygenBreather.Sim200ms` -- `HasOxygen()` alone does NOT suffocate anyone; it only
    ///   steers which gas provider gets picked).
    /// - The low-oxygen warning is a postfix on `IsLowOxygen`, so vanilla's own warning UI, its
    ///   notifications and every consumer of that flag keep working and simply start being told
    ///   the truth about partial pressure.
    /// - Everything else lives on <see cref="AirPhysiologyMonitor"/>, one component per duplicant,
    ///   added at PREFAB time via `BaseMinionConfig.BaseMinion` so its KMonoBehaviour lifecycle is
    ///   ordinary and `ISim1000ms` registration happens the normal way.
    ///
    /// CRITTERS ARE NOT AFFECTED, and checking rather than assuming is why this paragraph
    /// changed. `GasBreatherFromWorldProvider` is added in exactly one place in the whole game --
    /// `MinionConfig` -- so every patch in this file is duplicant-only. ONI
    /// critters have no oxygen requirement at all; fish suffocate on `Grid.IsSubstantialLiquid`
    /// (`AquaticCreatureSuffocationMonitor`), which is about being out of water, not about air.
    ///
    /// There IS a related gap, and it is not a breathing one: gas-EATING critters and plants
    /// (Puft, Oxyfern and the rest) use `ElementConsumer`, which registers a sample cell with the
    /// native sim and has that consume for it. On a cell the mixture layer owns, that path is
    /// gated -- the same reason a vanilla Gas Pump extracted 0.000 kg from this project's own
    /// corridor. Closing it is a SimDLL job in the element-consumer kernel, not a managed patch
    /// here, and it would fix all twenty-one of that component's users at once.
    ///
    /// DUPLICANTS IN SUITS ARE UNAFFECTED, on both halves. An airtight suit swaps in its own gas
    /// provider (`IsBlocked` returns true for `HasSuitTank`), so the patches never see them, and
    /// the monitor skips anyone wearing one -- matching the specification's "with open (or
    /// without) helmet" wording, and keeping suit tanks out of a rule the specification
    /// explicitly says not to apply to them.
    /// </summary>
    internal static class AirPhysiology
    {
        internal const string OxygenCriticalEffectId = "Mod1AirOxygenCritical";
        internal const string AirColdEffectId = "Mod1AirCold";
        internal const string AirFrigidEffectId = "Mod1AirFrigid";
        internal const string AirHotEffectId = "Mod1AirHot";
        internal const string ContaminantHeadacheEffectId = "Mod1AirHeadache";
        internal const string ContaminantImpairedEffectId = "Mod1AirImpaired";

        /// <summary>
        /// Lung damage per second while the air is below -10 C or above 50 C. A duplicant has 100
        /// hit points, so half a point a second is a hundred seconds of unprotected exposure
        /// before it kills -- long enough to notice the red warning and walk out, short enough
        /// that ignoring it is fatal. Deliberately a round, legible number rather than a tuned
        /// one; balancing belongs to a play test, not to this file.
        /// </summary>
        private const float LungDamagePerSecond = 0.5f;

        /// <summary>
        /// The effect objects this file has built, kept so a rebuilt database can be handed the
        /// SAME objects back rather than a second set carrying the same ids.
        /// </summary>
        private static readonly List<Effect> CreatedEffects = new List<Effect>();

        private static StatusItem oxygenLowItem;
        private static StatusItem oxygenCriticalItem;
        private static StatusItem airColdItem;
        private static StatusItem airFrigidItem;
        private static StatusItem airHotItem;
        private static StatusItem carbonDioxideHeadacheItem;
        private static StatusItem carbonDioxideImpairedItem;
        private static StatusItem carbonDioxideSuffocatingItem;
        private static StatusItem toxicHeadacheItem;
        private static StatusItem toxicImpairedItem;
        private static StatusItem toxicSuffocatingItem;

        /// <summary>Number of distinct conditions a monitor tracks; see <see cref="Condition"/>.</summary>
        internal const int ConditionCount = 11;

        /// <summary>
        /// The conditions a duplicant can be in, one status item each. Ordered worst-last within
        /// each axis so a reader can see the tiers.
        /// </summary>
        internal enum Condition
        {
            OxygenLow = 0,
            OxygenCritical = 1,
            AirCold = 2,
            AirFrigid = 3,
            AirHot = 4,
            CarbonDioxideHeadache = 5,
            CarbonDioxideImpaired = 6,
            CarbonDioxideSuffocating = 7,
            ToxicHeadache = 8,
            ToxicImpaired = 9,
            ToxicSuffocating = 10,
        }

        /// <summary>
        /// The air a breather at <paramref name="cell"/> can actually reach: the best of the six
        /// cells vanilla itself considers, judged on oxygen partial pressure.
        ///
        /// The offsets are vanilla's own `GasBreatherFromWorldProvider.DEFAULT_BREATHABLE_OFFSETS`
        /// rather than a copy, so a duplicant's reach cannot silently diverge from the reach
        /// vanilla's own code uses in the paths this does not patch.
        /// </summary>
        internal static AirAssessment BestAir(int cell)
        {
            AirAssessment best = AtmosphereFacade.Assess(cell);
            CellOffset[] offsets = GasBreatherFromWorldProvider.DEFAULT_BREATHABLE_OFFSETS;
            if (offsets == null)
            {
                return best;
            }
            for (int i = 0; i < offsets.Length; i++)
            {
                int candidateCell = Grid.OffsetCell(cell, offsets[i]);
                if (!Grid.IsValidCell(candidateCell))
                {
                    continue;
                }
                AirAssessment candidate = AtmosphereFacade.Assess(candidateCell);
                if (candidate.OxygenPartialPressurePa > best.OxygenPartialPressurePa)
                {
                    best = candidate;
                }
            }
            return best;
        }

        /// <summary>
        /// True when the air is unbreathable outright -- no usable oxygen, or so much carbon
        /// dioxide or toxic gas that oxygen no longer helps. The second half is the
        /// specification's "more than about 7% causes suffocation (even if there is also
        /// sufficient Oxygen)", which has no vanilla equivalent at all.
        /// </summary>
        internal static bool IsSuffocating(AirAssessment air)
        {
            return air.Oxygen == OxygenGrade.None
                || air.CarbonDioxide == ContaminantGrade.Suffocating
                || air.Toxic == ContaminantGrade.Suffocating;
        }

        /// <summary>
        /// Registers the six new effects into the live database.
        ///
        /// Built exactly the way Klei builds its own `CenterOfAttention` effect at the bottom of
        /// `Db`'s constructor: construct an <see cref="Effect"/> with the
        /// text inline, hang <see cref="AttributeModifier"/>s on it, and add it to
        /// <c>Db.Get().effects</c>. Duration 0 means "until removed", which is what a condition
        /// the duplicant is standing in wants -- the monitor takes each one off the moment the
        /// air improves, rather than leaving a timer to expire in clean air.
        ///
        /// STRESS RATES are given per second, which is the unit `StressDelta` is expressed in
        /// throughout vanilla (Klei writes its own as `-1f / 120f`). The magnitudes are eyeballed
        /// against that one reference point and deliberately modest: this is a physical model
        /// being introduced, not a difficulty increase, and a headache that empties a stress bar
        /// in a minute would be the latter.
        /// </summary>
        internal static void RegisterEffects()
        {
            if (CreatedEffects.Count > 0)
            {
                // `Db.Initialize` has run a second time, which means a second Db: hand the same
                // effect objects to the new table rather than building a second set that would
                // carry the same ids.
                foreach (Effect existing in CreatedEffects)
                {
                    Db.Get().effects.Add(existing);
                }
                return;
            }

            AddEffect(OxygenCriticalEffectId, "Starved Of Oxygen",
                "There is barely enough oxygen here to stay conscious.",
                new AttributeModifier("StressDelta", 1f / 60f, "Starved Of Oxygen"));

            AddEffect(AirColdEffectId, "Breathing Cold Air",
                "The air here is uncomfortably cold to breathe.",
                new AttributeModifier("StressDelta", 1f / 300f, "Breathing Cold Air"));

            AddEffect(AirFrigidEffectId, "Breathing Frigid Air",
                "The air here is cold enough to injure lungs.",
                new AttributeModifier("StressDelta", 1f / 60f, "Breathing Frigid Air"));

            AddEffect(AirHotEffectId, "Breathing Scorching Air",
                "The air here is hot enough to injure lungs.",
                new AttributeModifier("StressDelta", 1f / 60f, "Breathing Scorching Air"));

            AddEffect(ContaminantHeadacheEffectId, "Headache",
                "Something in this air is giving them a headache.",
                new AttributeModifier("StressDelta", 1f / 300f, "Headache"));

            // Cognitive impairment, modelled as what it would actually cost a colony: the
            // duplicant is slower on their feet and learns nothing while they are in it.
            Effect impaired = AddEffect(ContaminantImpairedEffectId, "Impaired",
                "The air here is bad enough to blunt their thinking.",
                new AttributeModifier("StressDelta", 1f / 120f, "Impaired"));
            if (impaired != null)
            {
                impaired.Add(new AttributeModifier("Athletics", -3f, "Impaired"));
                impaired.Add(new AttributeModifier("Learning", -3f, "Impaired"));
            }
        }

        /// <summary>
        /// Builds one effect and adds it to the database.
        ///
        /// It deliberately does NOT ask the database whether the id already exists first.
        /// `ResourceSet.Get` LOGS AN ERROR for an id it does not hold rather than returning null
        /// quietly, so an existence check written the obvious way prints six red "Could not find
        /// Klei.AI.Effect" lines on every launch -- which is exactly what the first
        /// `--mod1-airloop` run did, and they read like a failure when nothing had failed.
        /// <see cref="CreatedEffects"/> is the guard instead.
        /// </summary>
        private static Effect AddEffect(string id, string name, string description,
            AttributeModifier modifier)
        {
            Effect effect = new Effect(id, name, description, 0f, show_in_ui: true,
                trigger_floating_text: false, is_bad: true);
            if (modifier != null)
            {
                effect.Add(modifier);
            }
            Db.Get().effects.Add(effect);
            CreatedEffects.Add(effect);
            return effect;
        }

        /// <summary>
        /// Builds the status items on first use.
        ///
        /// Deferred out of static construction for the same reason PipeStressMonitor.cs defers
        /// its own: the <see cref="StatusItem"/> constructor reaches `Assets.GetTintedSprite`,
        /// which needs an asset database that is not up yet when a static field initializer runs.
        /// The explicit name/tooltip constructor is used rather than vanilla's `CreateStatusItem`
        /// helper, which would look the text up under a "STRINGS.DUPLICANTS.STATUSITEMS.*" prefix
        /// a mod's own item has no entries under.
        /// </summary>
        private static void EnsureStatusItems()
        {
            if (oxygenLowItem != null)
            {
                return;
            }

            oxygenLowItem = MakeItem("Mod1AirOxygenLow", "Oxygen Low",
                "The oxygen here is below 16 kPa. Breathable, but not by much.",
                NotificationType.BadMinor);
            oxygenCriticalItem = MakeItem("Mod1AirOxygenCritical", "Oxygen Critical",
                "The oxygen here is below 12 kPa. Below 5 kPa they will suffocate.",
                NotificationType.Bad);
            airColdItem = MakeItem("Mod1AirCold", "Cold Air",
                "This air is below 0 C. Uncomfortable to breathe.",
                NotificationType.BadMinor);
            airFrigidItem = MakeItem("Mod1AirFrigid", "Frigid Air",
                "This air is below -10 C. It is damaging their lungs.",
                NotificationType.Bad);
            airHotItem = MakeItem("Mod1AirHot", "Scorching Air",
                "This air is above 50 C. It is damaging their lungs.",
                NotificationType.Bad);
            carbonDioxideHeadacheItem = MakeItem("Mod1AirCO2Headache", "Stale Air",
                "Carbon dioxide here is above 0.5%. Enough to cause headaches.",
                NotificationType.BadMinor);
            carbonDioxideImpairedItem = MakeItem("Mod1AirCO2Impaired", "Carbon Dioxide Buildup",
                "Carbon dioxide here is above 1%. Enough to blunt their thinking.",
                NotificationType.Bad);
            carbonDioxideSuffocatingItem = MakeItem("Mod1AirCO2Suffocating", "Carbon Dioxide Poisoning",
                "Carbon dioxide here is above 7%. They cannot breathe here no matter how much "
                + "oxygen there is.",
                NotificationType.Bad);
            toxicHeadacheItem = MakeItem("Mod1AirToxicHeadache", "Traces Of Something",
                "There is enough toxic gas in this air to cause headaches.",
                NotificationType.BadMinor);
            toxicImpairedItem = MakeItem("Mod1AirToxicImpaired", "Toxic Air",
                "There is enough toxic gas in this air to blunt their thinking.",
                NotificationType.Bad);
            toxicSuffocatingItem = MakeItem("Mod1AirToxicSuffocating", "Poisonous Air",
                "There is so much toxic gas here that they cannot breathe at all.",
                NotificationType.Bad);
        }

        private static StatusItem MakeItem(string id, string name, string tooltip,
            NotificationType notificationType)
        {
            return new StatusItem(id, name, tooltip, "status_item_exclamation",
                StatusItem.IconType.Custom, notificationType, allow_multiples: false,
                render_overlay: OverlayModes.None.ID);
        }

        /// <summary>The status item for one condition, or null before the assets are up.</summary>
        internal static StatusItem ItemFor(Condition condition)
        {
            EnsureStatusItems();
            switch (condition)
            {
                case Condition.OxygenLow: return oxygenLowItem;
                case Condition.OxygenCritical: return oxygenCriticalItem;
                case Condition.AirCold: return airColdItem;
                case Condition.AirFrigid: return airFrigidItem;
                case Condition.AirHot: return airHotItem;
                case Condition.CarbonDioxideHeadache: return carbonDioxideHeadacheItem;
                case Condition.CarbonDioxideImpaired: return carbonDioxideImpairedItem;
                case Condition.CarbonDioxideSuffocating: return carbonDioxideSuffocatingItem;
                case Condition.ToxicHeadache: return toxicHeadacheItem;
                case Condition.ToxicImpaired: return toxicImpairedItem;
                case Condition.ToxicSuffocating: return toxicSuffocatingItem;
                default: return null;
            }
        }

        /// <summary>
        /// The effect a condition carries, or null when the condition is a warning only. The
        /// suffocating tiers carry none because suffocation itself is already the consequence,
        /// and the two contaminant axes deliberately share one headache and one impairment effect
        /// -- a duplicant with both carbon dioxide and a toxic gas around them has one headache,
        /// not two stacking ones.
        /// </summary>
        internal static string EffectFor(Condition condition)
        {
            switch (condition)
            {
                case Condition.OxygenCritical: return OxygenCriticalEffectId;
                case Condition.AirCold: return AirColdEffectId;
                case Condition.AirFrigid: return AirFrigidEffectId;
                case Condition.AirHot: return AirHotEffectId;
                case Condition.CarbonDioxideHeadache:
                case Condition.ToxicHeadache: return ContaminantHeadacheEffectId;
                case Condition.CarbonDioxideImpaired:
                case Condition.ToxicImpaired: return ContaminantImpairedEffectId;
                default: return null;
            }
        }

        /// <summary>Lung damage rate for the air, in hit points per second.</summary>
        internal static float LungDamageRate(AirAssessment air)
        {
            if (air.Temperature == AirTemperatureGrade.Frigid
                || air.Temperature == AirTemperatureGrade.Hot)
            {
                return LungDamagePerSecond;
            }
            return 0f;
        }
    }

    /// <summary>
    /// One per duplicant. Grades the air they are standing in once a second, keeps their status
    /// items and effects in step with it, and applies lung damage while the air is cold or hot
    /// enough to cause it.
    ///
    /// ONCE A SECOND, not every 200 ms: the breathing DECISIONS (suffocation, the low-oxygen flag)
    /// are on vanilla's own 200 ms path where they belong, and everything this component does is
    /// UI and slow-acting physiology. <see cref="AtmosphereFacade.Assess"/> reads a whole cell
    /// composition and computes three pressures, and running that seven times per duplicant five
    /// times a second, for a colony of thirty, would be a real cost for no visible gain.
    ///
    /// See <see cref="AirPhysiology"/> for what is being modelled and why.
    /// </summary>
    public class AirPhysiologyMonitor : KMonoBehaviour, ISim1000ms
    {
        private const string LogPrefix = "[Mod1ThermoFluid] AIRPHYSIOLOGY: ";

        private KSelectable selectable;

        private KPrefabID prefabID;

        private Klei.AI.Effects effects;

        private Health health;

        private readonly Guid[] statusHandles = new Guid[AirPhysiology.ConditionCount];

        private readonly bool[] active = new bool[AirPhysiology.ConditionCount];

        protected override void OnSpawn()
        {
            base.OnSpawn();
            selectable = GetComponent<KSelectable>();
            prefabID = GetComponent<KPrefabID>();
            effects = GetComponent<Klei.AI.Effects>();
            health = GetComponent<Health>();
        }

        protected override void OnCleanUp()
        {
            ClearAll();
            base.OnCleanUp();
        }

        public void Sim1000ms(float dt)
        {
            try
            {
                Evaluate(dt);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + "air assessment failed: " + e);
            }
        }

        private void Evaluate(float dt)
        {
            if (prefabID == null)
            {
                return;
            }

            // A duplicant in an airtight suit is breathing their own tank, and a dead one is past
            // caring. Both drop every condition rather than merely stopping updating them, so a
            // duplicant who suits up does not keep a stale warning and a stale effect forever.
            if (prefabID.HasTag(GameTags.Dead) || prefabID.HasTag(GameTags.HasSuitTank))
            {
                ClearAll();
                return;
            }

            int cell = Grid.PosToCell(this);
            if (!Grid.IsValidCell(cell))
            {
                ClearAll();
                return;
            }

            AirAssessment air = AirPhysiology.BestAir(cell);

            Apply(AirPhysiology.Condition.OxygenLow,
                air.Oxygen == OxygenGrade.Low);
            Apply(AirPhysiology.Condition.OxygenCritical,
                air.Oxygen == OxygenGrade.Critical);

            Apply(AirPhysiology.Condition.AirCold,
                air.Temperature == AirTemperatureGrade.Cold);
            Apply(AirPhysiology.Condition.AirFrigid,
                air.Temperature == AirTemperatureGrade.Frigid);
            Apply(AirPhysiology.Condition.AirHot,
                air.Temperature == AirTemperatureGrade.Hot);

            Apply(AirPhysiology.Condition.CarbonDioxideHeadache,
                air.CarbonDioxide == ContaminantGrade.Headache);
            Apply(AirPhysiology.Condition.CarbonDioxideImpaired,
                air.CarbonDioxide == ContaminantGrade.Impaired);
            Apply(AirPhysiology.Condition.CarbonDioxideSuffocating,
                air.CarbonDioxide == ContaminantGrade.Suffocating);

            Apply(AirPhysiology.Condition.ToxicHeadache,
                air.Toxic == ContaminantGrade.Headache);
            Apply(AirPhysiology.Condition.ToxicImpaired,
                air.Toxic == ContaminantGrade.Impaired);
            Apply(AirPhysiology.Condition.ToxicSuffocating,
                air.Toxic == ContaminantGrade.Suffocating);

            float damageRate = AirPhysiology.LungDamageRate(air);
            if (damageRate > 0f && health != null)
            {
                health.Damage(damageRate * dt);
            }
        }

        /// <summary>
        /// Turns one condition on or off, and does nothing at all when it is already in the state
        /// asked for -- status items and effects are both add/remove APIs with real cost, and a
        /// duplicant standing in a cold room for ten minutes should not be re-adding the same
        /// effect six hundred times.
        /// </summary>
        private void Apply(AirPhysiology.Condition condition, bool wanted)
        {
            int index = (int)condition;
            if (active[index] == wanted)
            {
                return;
            }
            active[index] = wanted;

            StatusItem item = AirPhysiology.ItemFor(condition);
            if (item != null && selectable != null)
            {
                if (wanted)
                {
                    statusHandles[index] = selectable.AddStatusItem(item);
                }
                else if (statusHandles[index] != Guid.Empty)
                {
                    selectable.RemoveStatusItem(statusHandles[index]);
                    statusHandles[index] = Guid.Empty;
                }
            }

            string effectId = AirPhysiology.EffectFor(condition);
            if (effectId == null || effects == null)
            {
                return;
            }
            if (wanted)
            {
                effects.Add(effectId, should_save: false);
                return;
            }

            // Two conditions can share one effect (the headache and impairment effects are shared
            // between the carbon dioxide and toxic axes), so it may only be removed once the
            // OTHER condition using it has gone too. Removing it the moment either clears would
            // let a duplicant standing in both walk away from the one that is still true.
            for (int i = 0; i < AirPhysiology.ConditionCount; i++)
            {
                if (i != index && active[i]
                    && AirPhysiology.EffectFor((AirPhysiology.Condition)i) == effectId)
                {
                    return;
                }
            }
            effects.Remove(effectId);
        }

        private void ClearAll()
        {
            for (int i = 0; i < AirPhysiology.ConditionCount; i++)
            {
                Apply((AirPhysiology.Condition)i, wanted: false);
            }
        }
    }

    /// <summary>
    /// Registers the new effects once the database exists.
    ///
    /// `Db.Initialize` is where Klei builds every resource set INCLUDING the effects table, and
    /// where it adds its own late effect (`CenterOfAttention`) at the very bottom -- so a postfix
    /// on it runs with a complete table and is the same seam Klei uses itself. It is reached from
    /// `Db.Get()`, which assigns `_Instance` BEFORE calling it, so a `Db.Get()` from inside this
    /// postfix returns the instance rather than recursing.
    /// </summary>
    [HarmonyPatch(typeof(Db), "Initialize")]
    internal static class Db_Initialize_AirPhysiology
    {
        private static void Postfix()
        {
            try
            {
                AirPhysiology.RegisterEffects();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Mod1ThermoFluid] air physiology effect registration failed: "
                    + e);
            }
        }
    }

    /// <summary>
    /// Puts <see cref="AirPhysiologyMonitor"/> on the duplicant prefab, next to the
    /// <c>OxygenBreather</c> this file's rules act on.
    ///
    /// At PREFAB time rather than on a live duplicant: a KMonoBehaviour added to an
    /// already-spawned GameObject never gets its `OnSpawn` called, and `ISim1000ms` registration
    /// happens there. Hooking the prefab builder is the difference between a component that ticks
    /// and one that silently never runs.
    /// </summary>
    [HarmonyPatch(typeof(BaseMinionConfig), "BaseMinion")]
    internal static class BaseMinionConfig_BaseMinion_AirPhysiology
    {
        private static void Postfix(GameObject __result)
        {
            if (__result == null)
            {
                return;
            }
            __result.AddOrGet<AirPhysiologyMonitor>();
        }
    }

    /// <summary>
    /// SUFFOCATION, decided by oxygen partial pressure instead of by how many kilograms of a
    /// breathable-tagged element happen to be in the cell.
    ///
    /// `ConsumeGas` is the right place and `HasOxygen` is not: `OxygenBreather.Sim200ms` sets its
    /// `hasAir` flag from THIS method's return value, while `HasOxygen` only
    /// influences which gas provider gets selected. Returning false here is exactly what vanilla
    /// itself does when the breather is standing in nothing breathable.
    ///
    /// Returning false also SKIPS the original, which is deliberate -- a duplicant who cannot
    /// breathe the air must not simultaneously be consuming it.
    /// </summary>
    [HarmonyPatch(typeof(GasBreatherFromWorldProvider), "ConsumeGas")]
    internal static class GasBreatherFromWorldProvider_ConsumeGas_AirPhysiology
    {
        private static bool Prefix(OxygenBreather oxygen_breather, ref bool __result)
        {
            try
            {
                if (oxygen_breather == null)
                {
                    return true;
                }
                int cell = Grid.PosToCell(oxygen_breather);
                if (!Grid.IsValidCell(cell))
                {
                    return true;
                }
                if (AirPhysiology.IsSuffocating(AirPhysiology.BestAir(cell)))
                {
                    __result = false;
                    return false;
                }
            }
            catch
            {
                // Runs on every breather's 200 ms tick. A mistake here degrades to vanilla's own
                // answer rather than killing anyone.
            }
            return true;
        }
    }

    /// <summary>
    /// EXHALED CARBON DIOXIDE GOES INTO THE MIXTURE, not into vanilla's single-element layer.
    ///
    /// Without this a duplicant standing in a room the mixture layer owns breathes out into
    /// vanilla's frozen view of that cell, where nothing downstream can see it: the room's real
    /// composition never changes, <see cref="AtmosphereFacade"/> keeps reporting the carbon
    /// dioxide fraction it started with, and an Air Intake drawing the room's air draws air that
    /// nobody has breathed. The whole point of a scrubber is to remove what the colony produces,
    /// so the colony has to actually produce it where the scrubber can find it.
    ///
    /// `SpawnBreath` is the seam because it is the one call `OxygenBreather.Sim200ms` makes for
    /// every exhalation, and it already carries the mass, the temperature and the position. On a
    /// promoted cell the mass is injected straight into that cell's mixture and vanilla's own
    /// spawn is skipped entirely -- running both would exhale the same breath twice.
    ///
    /// THE COST, stated rather than hidden: skipping the original also skips the little breath
    /// puff animation and the drifting CO2 particle, because both are built inside the method
    /// being replaced and neither is reachable on its own. A cell vanilla still owns takes the
    /// original path untouched, animation and all.
    /// </summary>
    [HarmonyPatch(typeof(CO2Manager), "SpawnBreath")]
    internal static class CO2Manager_SpawnBreath_AirPhysiology
    {
        private static bool Prefix(Vector3 position, float mass, float temperature)
        {
            try
            {
                if (mass <= 0f)
                {
                    return true;
                }
                int cell = Grid.PosToCell(position);
                if (!Grid.IsValidCell(cell))
                {
                    return true;
                }
                if (Grid.IsLiquid(cell) || Grid.IsVisiblyInLiquid(position))
                {
                    // A MOUTH UNDER LIQUID IS NEVER INJECTED INTO. The room graph counts liquid
                    // cells as open, so in a promoted room the water cell reads as owned, and
                    // injecting there put the breath into the water cell's gas slots, where
                    // gas_rooms.h's occupied mask freezes it. Vanilla decides instead: a bubble
                    // (OniFramework.Bubbles gives it its physics) or, if a breathable cell
                    // touches the mouth, a puff into that cell. That puff is the one case where
                    // the breath is injected, into the gas cell rather than the liquid one.
                    int spawnCell;
                    if (!TryFindBreathableSpawnCell(cell, out spawnCell)
                        || !GasMixtureFacade.IsRoomOwned(spawnCell))
                    {
                        return true;
                    }
                    cell = spawnCell;
                }
                else if (!GasMixtureFacade.IsRoomOwned(cell))
                {
                    return true;
                }
                Element carbonDioxide =
                    ElementLoader.FindElementByHash(SimHashes.CarbonDioxide);
                int speciesIdx = carbonDioxide != null
                    ? ElementLoader.elements.IndexOf(carbonDioxide)
                    : -1;
                if (speciesIdx < 0)
                {
                    return true;
                }
                GasMixtureFacade.Inject(cell, speciesIdx, mass, temperature);
                return false;
            }
            catch
            {
                // Degrade to vanilla's own exhalation rather than throwing on a breathing tick.
            }
            return true;
        }

        private static readonly System.Reflection.MethodInfo FindBreathableSpawnCell =
            AccessTools.Method(typeof(CO2Manager), "TryFindBreathableSpawnCell");

        /// <summary>
        /// Vanilla's own private search: the first of the six
        /// <c>DEFAULT_BREATHABLE_OFFSETS</c> around the mouth holding CO2 or a breathable gas.
        /// Asked through reflection so the two cannot disagree about which cell the puff goes to.
        /// </summary>
        private static bool TryFindBreathableSpawnCell(int cell, out int spawnCell)
        {
            spawnCell = Grid.InvalidCell;
            if (FindBreathableSpawnCell == null)
            {
                return false;
            }
            object[] args = { cell, Grid.InvalidCell };
            bool found = (bool)FindBreathableSpawnCell.Invoke(null, args);
            spawnCell = (int)args[1];
            return found && Grid.IsValidCell(spawnCell);
        }
    }

    /// <summary>
    /// WHERE A DUPLICANT GOES LOOKING FOR AIR, re-graded on partial pressure.
    ///
    /// This is the seam every air-SEEKING behaviour in the game runs through, and it is a
    /// different question from "can they breathe here" -- which is why patching `ConsumeGas` and
    /// `IsLowOxygen` was not enough on its own. `SafetyConditions` builds two conditions out of
    /// this method, `HasSomeOxygen` and `HasSomeOxygenAround`, and those two go into
    /// `RecoverBreathChecker`, `SafeCellChecker` and `IdleCellChecker`.
    /// So this one method decides where a suffocating duplicant runs to, where anyone flees to
    /// when a room turns unsafe, and where they choose to stand around when idle.
    ///
    /// Vanilla ranks the candidate cells by MASS and accepts any above `noOxygenThreshold`
    /// (0.05 kg). In a world where air is a mixture that is the wrong question twice over: the
    /// heaviest cell is not the most breathable one -- carbon dioxide is heavier than oxygen --
    /// and 0.05 kg of oxygen in a cell is about 4.6 kPa, well below the 5 kPa line where
    /// suffocation begins. A duplicant fleeing suffocation would run to a cell that suffocates
    /// them slightly more slowly.
    ///
    /// So the candidates are re-ranked by OXYGEN PARTIAL PRESSURE and gated on the same rule
    /// everything else in this file uses: not graded None, and not drowning in carbon dioxide or
    /// a toxic gas. `ElementID` and `Mass` are then taken from the winning cell through vanilla's
    /// own helper, so the cell a duplicant walks to is a cell `ConsumeGas` can actually draw from
    /// -- the two must not disagree about what is in it.
    /// </summary>
    [HarmonyPatch(typeof(GasBreatherFromWorldProvider), "GetBestBreathableCellAroundSpecificCell",
        new System.Type[]
        {
            typeof(int), typeof(CellOffset[]), typeof(OxygenBreather), typeof(float),
        },
        new ArgumentType[]
        {
            ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out,
        })]
    internal static class GasBreatherFromWorldProvider_GetBestBreathableCell_AirPhysiology
    {
        private static void Postfix(int theSpecificCell, CellOffset[] breathRange,
            ref GasBreatherFromWorldProvider.BreathableCellData __result)
        {
            try
            {
                CellOffset[] offsets = breathRange
                    ?? GasBreatherFromWorldProvider.DEFAULT_BREATHABLE_OFFSETS;
                if (offsets == null)
                {
                    return;
                }

                int bestCell = Grid.InvalidCell;
                float bestOxygenPa = 0f;
                for (int i = 0; i < offsets.Length; i++)
                {
                    int candidate = Grid.OffsetCell(theSpecificCell, offsets[i]);
                    if (!Grid.IsValidCell(candidate))
                    {
                        continue;
                    }
                    AirAssessment air = AtmosphereFacade.Assess(candidate);
                    if (AirPhysiology.IsSuffocating(air))
                    {
                        continue;
                    }
                    if (air.OxygenPartialPressurePa > bestOxygenPa)
                    {
                        bestOxygenPa = air.OxygenPartialPressurePa;
                        bestCell = candidate;
                    }
                }

                if (bestCell == Grid.InvalidCell)
                {
                    // Nowhere within reach is breathable on the partial-pressure rule. Say so,
                    // rather than leaving vanilla's mass-based answer to send someone to a cell
                    // that will suffocate them.
                    __result.Cell = theSpecificCell;
                    __result.ElementID = SimHashes.Vacuum;
                    __result.Mass = 0f;
                    __result.IsBreathable = false;
                    return;
                }

                // Mass and element come from vanilla's own helper for the cell we picked, so the
                // cell a duplicant is sent to and the mass ConsumeGas can draw from it stay the
                // same number. That helper is the one BreathingPatch.cs already makes
                // mixture-aware.
                float massKg = (float)AccessTools.Method(typeof(GasBreatherFromWorldProvider),
                        "GetBreathableCellMass")
                    .Invoke(null, MassArgs(bestCell));
                __result.Cell = bestCell;
                __result.ElementID = (SimHashes)MassArgsBuffer[1];
                __result.Mass = massKg;
                __result.IsBreathable = __result.ElementID != SimHashes.Vacuum && massKg > 0f;
            }
            catch
            {
                // Runs on every breathing tick and on every safety query. A mistake here degrades
                // to vanilla's own answer rather than stranding anyone.
            }
        }

        /// <summary>
        /// Reused argument buffer for the private helper's `out SimHashes` parameter, so a
        /// per-tick reflective call does not allocate one every time.
        /// </summary>
        private static readonly object[] MassArgsBuffer = new object[2];

        private static object[] MassArgs(int cell)
        {
            MassArgsBuffer[0] = cell;
            MassArgsBuffer[1] = SimHashes.Vacuum;
            return MassArgsBuffer;
        }
    }

    /// <summary>
    /// The low-oxygen warning, re-graded on partial pressure. A postfix rather than a replacement
    /// so vanilla's own notification, its UI and every other consumer of this flag keep working
    /// unchanged and simply start being told the truth.
    ///
    /// Both the LOW and CRITICAL bands report true here, because vanilla has exactly one flag and
    /// a duplicant in the critical band is certainly also low. The distinction between the two is
    /// carried by <see cref="AirPhysiologyMonitor"/>'s separate status items, which is where a new
    /// tier can exist without inventing a second vanilla flag nothing would read.
    /// </summary>
    [HarmonyPatch(typeof(GasBreatherFromWorldProvider), "IsLowOxygen")]
    internal static class GasBreatherFromWorldProvider_IsLowOxygen_AirPhysiology
    {
        private static void Postfix(ref bool __result, OxygenBreather ___oxygenBreather)
        {
            try
            {
                if (___oxygenBreather == null)
                {
                    return;
                }
                int cell = Grid.PosToCell(___oxygenBreather);
                if (!Grid.IsValidCell(cell))
                {
                    return;
                }
                OxygenGrade grade = AirPhysiology.BestAir(cell).Oxygen;
                __result = grade == OxygenGrade.Low
                    || grade == OxygenGrade.Critical;
            }
            catch
            {
                // See the ConsumeGas patch -- degrade to vanilla's answer, never throw mid-tick.
            }
        }
    }
}
