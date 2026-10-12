using System;
using System.Collections.Generic;
using HarmonyLib;
using Klei.AI;
using OniFramework;
using UnityEngine;

namespace Mod2MatterEnvironment
{
    /// <summary>
    /// HYGIENE: duplicant grime, what it costs, and the odour it puts in the air.
    ///
    /// <c>Grime</c> is an ordinary ONI <see cref="Amount"/>, 0-100, on standard duplicants only
    /// (<c>HygieneMonitor</c> is on <c>MinionConfig</c> and nowhere else, so a bionic duplicant
    /// would have no shower to clear it with). Being an Amount buys the vitals bar, save/load,
    /// and the extension point: any mod adds a grime source with an <see cref="AttributeModifier"/>
    /// on <see cref="DeltaId"/>, the way it adds any rate in ONI. Nothing here is a framework
    /// surface, by choice.
    ///
    /// <b>The ledger.</b> One scalar carries every consequence, but the monitor also keeps what
    /// the grime is MADE of, by source, because that decides what a duplicant gives off: the
    /// <see cref="SweatKey"/> share becomes funk. A source that is not sweat registers its
    /// modifier's description with <see cref="RegisterLedgerSource"/> -- soot from smoke
    /// is the intended first, and smells of nothing.
    ///
    /// Every rate below is a balance placeholder; the band thresholds and their margin are the
    /// one set of numbers that is a correctness decision.
    /// </summary>
    public static class Grime
    {
        internal const string LogPrefix = "[Mod2MatterEnvironment] GRIME: ";

        public const string AmountId = "Grime";
        public const string DeltaId = AmountId + "Delta";
        public const float Max = 100f;

        /// <summary>The ledger key a GrimeDelta modifier counts under unless registered otherwise.</summary>
        public const string SweatKey = "Sweat";

        // ---------------------------------------------------------------- accumulation

        /// <summary>+40 a cycle at rest.</summary>
        public const float BaseRatePerSecond = 40f / 600f;

        /// <summary>A bladder accident (`StressfulyEmptyingBladder`), once.</summary>
        public const float AccidentGrime = 30f;

        internal const string AccidentEffectId = "StressfulyEmptyingBladder";

        /// <summary>The work being done multiplies the base rate, keyed by the workable's attribute.</summary>
        private static readonly Dictionary<string, float> WorkMultipliers = new Dictionary<string, float>
        {
            { "Digging", 3f },
            { "Ranching", 2f },
            { "Botanist", 1.5f },
            { "Construction", 1.5f },
        };

        internal static float WorkMultiplier(string attributeId)
        {
            float m;
            return attributeId != null && WorkMultipliers.TryGetValue(attributeId, out m) ? m : 1f;
        }

        // ---------------------------------------------------------------- washing

        /// <summary>50 grime takes vanilla's 15 s shower; 100 takes 30 s.</summary>
        public const float ShowerGrimePerSecond = 50f / 15f;
        public const float ShowerMinSeconds = 3f;

        /// <summary>A wash basin or sink (HandSanitizer.Work) takes this much, once.</summary>
        public const float BasinGrime = 10f;

        /// <summary>The shower urge also fires here, not only when `Showered` has expired.</summary>
        public const float NeedsShowerAt = 50f;

        internal static float ShowerSeconds(float grime)
        {
            return Mathf.Max(ShowerMinSeconds, grime / ShowerGrimePerSecond);
        }

        // ---------------------------------------------------------------- the band ladder

        /// <summary>Band b (1..3) is entered at Enter[b-1] and left below Enter[b-1] - Margin.</summary>
        public static readonly float[] BandEnter = { 25f, 50f, 75f };
        public const float BandMargin = 5f;
        public const int BandGrubby = 1;
        public const int BandGrimy = 2;
        public const int BandFilthy = 3;

        internal static readonly string[] BandEffectIds = { "GrimeGrubby", "GrimeGrimy", "GrimeFilthy" };
        private static readonly string[] BandNames = { "Grubby", "Grimy", "Filthy" };
        private static readonly float[] BandMorale = { -1f, -2f, -4f };
        private static readonly string[] BandTooltips =
        {
            "This Duplicant could do with a wash.",
            "This Duplicant is visibly dirty and knows it.",
            "This Duplicant is caked in grime and miserable about it.",
        };

        /// <summary>The band for <paramref name="grime"/>, moving from <paramref name="current"/>
        /// with hysteresis; a current below 0 means "no history" and takes the plain band.</summary>
        internal static int Band(float grime, int current)
        {
            if (current < 0)
            {
                int plain = 0;
                while (plain < BandEnter.Length && grime >= BandEnter[plain])
                {
                    plain++;
                }
                return plain;
            }
            int b = current;
            while (b < BandEnter.Length && grime >= BandEnter[b])
            {
                b++;
            }
            while (b > 0 && grime < BandEnter[b - 1] - BandMargin)
            {
                b--;
            }
            return b;
        }

        /// <summary>Raised on the duplicant whenever its band changes; both HygieneMonitor states
        /// listen, because `Effects.Add` of a held effect raises nothing.</summary>
        public static readonly int BandChangedHash = Hash.SDBMLower("Mod2GrimeBandChanged");

        // ---------------------------------------------------------------- working slower

        public const float AthleticsAtMax = -4f;
        public const float LabourAtMax = -3f;
        internal static readonly string[] SlowedAttributes = { "Athletics", "Digging", "Construction", "Machinery" };

        internal static float SlowScale(float grime)
        {
            return Mathf.Clamp01((grime - BandEnter[0]) / (Max - BandEnter[0]));
        }

        // ---------------------------------------------------------------- odour

        /// <summary>No funk below this grime: a clean duplicant does not smell.</summary>
        public const float FunkOnset = 25f;

        /// <summary>Funk units a second per grime point above <see cref="FunkOnset"/>. At the
        /// default 120 s half-life one Filthy duplicant holds about emission x 173 s of funk in
        /// its air: ~6 a cell in a 72-cell room, well past <see cref="ReactionThreshold"/>, and
        /// under it once spread through a base-sized volume.</summary>
        public const float FunkPerGrimeSecond = 0.05f;

        /// <summary>Indexed by OdourChannel. Rot is the most offensive.</summary>
        internal static readonly float[] ChannelWeights = { 1f, 1f, 1.5f };
        public const float ReactionThreshold = 2f;
        public const float ReactionSeconds = 60f;

        /// <summary>At this band or worse a duplicant's own cell is mostly itself, so funk goes unnoticed.</summary>
        public const int NoseBlindBand = BandGrimy;

        internal static readonly string[] OdourEffectIds = { "OdourFunk", "OdourStink", "OdourRot" };
        private static readonly string[] OdourNames = { "Body Odour", "Stale Air", "Rotten Smell" };
        private static readonly string[] OdourTooltips =
        {
            "Somebody around here badly needs a shower.",
            "The air here smells of slime and polluted oxygen.",
            "Something nearby is rotting.",
        };
        private const string OdourStompGroup = "Mod2OdourReaction";

        // ---------------------------------------------------------------- the ledger registry

        private static readonly Dictionary<string, string> LedgerKeys = new Dictionary<string, string>();

        /// <summary>
        /// Count grime that a GrimeDelta modifier described as <paramref name="modifierDescription"/>
        /// adds under <paramref name="ledgerKey"/> rather than <see cref="SweatKey"/>. Only the
        /// sweat share emits funk, so a source registered here gives off nothing until something
        /// reads its key.
        /// </summary>
        public static void RegisterLedgerSource(string modifierDescription, string ledgerKey)
        {
            if (string.IsNullOrEmpty(modifierDescription) || string.IsNullOrEmpty(ledgerKey))
            {
                throw new ArgumentException("both a modifier description and a ledger key are required");
            }
            LedgerKeys[modifierDescription] = ledgerKey;
        }

        internal static string LedgerKeyFor(string modifierDescription)
        {
            string key;
            return modifierDescription != null && LedgerKeys.TryGetValue(modifierDescription, out key)
                ? key
                : SweatKey;
        }

        // ---------------------------------------------------------------- registration

        /// <summary>The amount, once <c>Db.Initialize</c> has run.</summary>
        public static Amount Amount { get; private set; }

        internal static Effect[] BandEffects { get; private set; }

        internal static Effect[] OdourEffects { get; private set; }

        /// <summary>
        /// Builds the amount and the six effects into the current Db. Called from a
        /// <c>Db.Initialize</c> postfix, so a second Db (which ONI does not normally make) gets a
        /// fresh set rather than objects carrying another table's ids.
        /// </summary>
        internal static void Register()
        {
            // CreateAmount reads the Delta name and description with a bare Strings.Get
            //: missing, they do not fall back the way Min and Max do.
            Strings.Add("STRINGS.DUPLICANTS.STATS.GRIME.NAME", "Grime");
            Strings.Add("STRINGS.DUPLICANTS.STATS.GRIME.TOOLTIP",
                "How dirty this Duplicant is. Grime builds up with work, faster with dirty work, "
                + "and only washing takes it off. A grimy Duplicant is slower, unhappier, takes "
                + "longer to shower and smells.");
            Strings.Add("STRINGS.DUPLICANTS.ATTRIBUTES.GRIMEDELTA.NAME", "Grime");
            Strings.Add("STRINGS.DUPLICANTS.ATTRIBUTES.GRIMEDELTA.DESC",
                "How fast this Duplicant is getting dirty.");

            Amount amount = Db.Get().Amounts.CreateAmount(AmountId, 0f, Max, show_max: false,
                Units.Flat, 0.5f, show_in_ui: true, "STRINGS.DUPLICANTS", "ui_icon_bladder",
                null, "mod_bladder");
            amount.SetDisplayer(new StandardAmountDisplayer(GameUtil.UnitClass.Percent,
                GameUtil.TimeSlice.PerCycle));
            Amount = amount;

            var bands = new Effect[BandEffectIds.Length];
            for (int i = 0; i < bands.Length; i++)
            {
                bands[i] = new Effect(BandEffectIds[i], BandNames[i], BandTooltips[i], 0f,
                    show_in_ui: true, trigger_floating_text: true, is_bad: true);
                bands[i].Add(new AttributeModifier("QualityOfLife", BandMorale[i], BandNames[i]));
                Db.Get().effects.Add(bands[i]);
            }
            BandEffects = bands;

            Emote cringe = Db.Get().Emotes.Minion.Cringe;
            var odours = new Effect[OdourEffectIds.Length];
            for (int i = 0; i < odours.Length; i++)
            {
                odours[i] = new Effect(OdourEffectIds[i], OdourNames[i], OdourTooltips[i],
                    ReactionSeconds, show_in_ui: true, trigger_floating_text: false, is_bad: true,
                    cringe, emote_cooldown: 30f, max_initial_delay: 0f, stompGroup: OdourStompGroup);
                odours[i].Add(new AttributeModifier("QualityOfLife", -1f, OdourNames[i]));
                Db.Get().effects.Add(odours[i]);
            }
            OdourEffects = odours;
        }

        // ---------------------------------------------------------------- reading odour

        private static long publishRequestedTick = long.MinValue;

        /// <summary>
        /// The three channels at <paramref name="cell"/> from the frame the sim published this
        /// tick, or through the worker barrier while that is not yet there -- the pattern of
        /// DissolvedCargo.SourceLane. A barrier per channel per duplicant per second is what the
        /// published copy avoids; it is requested the first time it is missing.
        /// </summary>
        internal static void ReadOdour(int cell, float[] byChannel)
        {
            SimExtFrame.PublishedProperty published;
            if (SimExtFrame.TryGetProperty(Odour.PropertyName, out published) && published.IsCurrent)
            {
                int padded = SimExtFrame.PaddedCell(cell);
                bool all = true;
                for (int c = 0; c < Odour.Channels && all; c++)
                {
                    all = published.TryReadFloat(padded, out byChannel[c], c);
                }
                if (all)
                {
                    return;
                }
            }
            if (Odour.Available && SimExtFrame.TickId != publishRequestedTick)
            {
                publishRequestedTick = SimExtFrame.TickId;
                SimExtCellProperties.Publish(Odour.PropertyIndex, true);
            }
            Odour.Composition(cell, byChannel);
        }
    }

    // ==================================================================== patches

    [HarmonyPatch(typeof(Db), "Initialize")]
    internal static class Db_Initialize_Grime
    {
        private static void Postfix()
        {
            try
            {
                Grime.Register();
            }
            catch (Exception e)
            {
                Debug.LogWarning(Grime.LogPrefix + "registration failed: " + e);
            }
        }
    }

    /// <summary>Standard duplicants carry the amount. `Modifiers.OnPrefabInit` makes it from this
    /// list BEFORE a save is read, so a duplicant from a pre-mod save loads at 0.</summary>
    [HarmonyPatch(typeof(MinionConfig), nameof(MinionConfig.GetAmounts))]
    internal static class MinionConfig_GetAmounts_Grime
    {
        private static void Postfix(ref string[] __result)
        {
            if (Grime.Amount == null || __result == null || Array.IndexOf(__result, Grime.AmountId) >= 0)
            {
                return;
            }
            __result = __result.Append(new[] { Grime.AmountId });
        }
    }

    /// <summary>At prefab time: a component added to a spawned duplicant never gets OnSpawn, and
    /// ISim1000ms registers there (as in AirPhysiology).</summary>
    [HarmonyPatch(typeof(MinionConfig), nameof(MinionConfig.CreatePrefab))]
    internal static class MinionConfig_CreatePrefab_Grime
    {
        private static void Postfix(GameObject __result)
        {
            if (__result != null && Grime.Amount != null)
            {
                __result.AddOrGet<GrimeMonitor>();
            }
        }
    }

    /// <summary>The shower lasts as long as the grime takes to drain. The
    /// resume token is no longer needed for duration -- an aborted shower keeps what it washed
    /// off -- so OnAbortWork is left alone.</summary>
    [HarmonyPatch(typeof(Shower), "OnStartWork")]
    internal static class Shower_OnStartWork_Grime
    {
        private static void Postfix(Shower __instance, WorkerBase worker)
        {
            GrimeMonitor monitor = worker != null ? worker.GetComponent<GrimeMonitor>() : null;
            if (monitor != null && monitor.Ready)
            {
                __instance.WorkTimeRemaining = Grime.ShowerSeconds(monitor.Value);
            }
        }
    }

    [HarmonyPatch(typeof(Shower), "OnWorkTick")]
    internal static class Shower_OnWorkTick_Grime
    {
        private static void Postfix(WorkerBase worker, float dt)
        {
            GrimeMonitor monitor = worker != null ? worker.GetComponent<GrimeMonitor>() : null;
            if (monitor != null)
            {
                monitor.Wash(Grime.ShowerGrimePerSecond * dt);
            }
        }
    }

    [HarmonyPatch(typeof(Shower), "OnCompleteWork")]
    internal static class Shower_OnCompleteWork_Grime
    {
        private static void Postfix(WorkerBase worker)
        {
            GrimeMonitor monitor = worker != null ? worker.GetComponent<GrimeMonitor>() : null;
            if (monitor != null)
            {
                monitor.Wash(Grime.Max);
            }
        }
    }

    [HarmonyPatch(typeof(HandSanitizer.Work), "OnCompleteWork")]
    internal static class HandSanitizerWork_OnCompleteWork_Grime
    {
        private static void Postfix(WorkerBase worker)
        {
            GrimeMonitor monitor = worker != null ? worker.GetComponent<GrimeMonitor>() : null;
            if (monitor != null)
            {
                monitor.Wash(Grime.BasinGrime);
            }
        }
    }

    /// <summary>Vanilla wants a shower only once `Showered` has expired; a Grimy duplicant wants
    /// one regardless.</summary>
    [HarmonyPatch(typeof(HygieneMonitor.Instance), nameof(HygieneMonitor.Instance.NeedsShower))]
    internal static class HygieneMonitor_NeedsShower_Grime
    {
        private static void Postfix(HygieneMonitor.Instance __instance, ref bool __result)
        {
            if (__result)
            {
                return;
            }
            GrimeMonitor monitor = __instance.gameObject.GetComponent<GrimeMonitor>();
            __result = monitor != null && monitor.Ready && monitor.Value >= Grime.NeedsShowerAt;
        }
    }

    /// <summary>Both states re-evaluate on a band change: vanilla's transitions listen only for
    /// EffectAdded / EffectRemoved, and nothing about grime raises either.</summary>
    [HarmonyPatch(typeof(HygieneMonitor), nameof(HygieneMonitor.InitializeStates))]
    internal static class HygieneMonitor_InitializeStates_Grime
    {
        private static void Postfix(HygieneMonitor __instance)
        {
            var changed = (GameHashes)Grime.BandChangedHash;
            __instance.clean.EventTransition(changed, __instance.needsshower,
                smi => smi.NeedsShower());
            __instance.needsshower.EventTransition(changed, __instance.clean,
                smi => !smi.NeedsShower());
        }
    }
}
