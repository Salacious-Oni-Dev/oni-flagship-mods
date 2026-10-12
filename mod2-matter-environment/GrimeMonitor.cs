using System;
using System.Collections.Generic;
using Klei.AI;
using KSerialization;
using OniFramework;
using UnityEngine;

namespace Mod2MatterEnvironment
{
    /// <summary>
    /// One standard duplicant's grime, once a second: what is adding to it, what it is made of,
    /// which morale band it puts them in, how much it slows them, the funk they give off, and
    /// what they smell. See <see cref="Grime"/> for the numbers.
    ///
    /// Only the ledger is saved here; the grime itself is the AmountInstance's. On a save loaded
    /// without this mod the component is skipped whole (SaveLoadRoot finds no component of the
    /// name and skips its bytes).
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class GrimeMonitor : KMonoBehaviour, ISim1000ms
    {
        // The ledger: grime units by source, summing to the amount's value. Parallel lists
        // rather than a dictionary so KSerialization round-trips it with nothing to trust.
        [Serialize] private List<string> ledgerKeys = new List<string>();
        [Serialize] private List<float> ledgerAmounts = new List<float>();

        private AmountInstance grime;
        private AttributeInstance delta;
        private Klei.AI.Effects effects;
        private Klei.AI.Attributes attributes;
        private WorkerBase worker;

        private AttributeModifier sweatRate;
        private AttributeModifier workRate;
        private readonly AttributeModifier[] slow = new AttributeModifier[Grime.SlowedAttributes.Length];

        private float lastGrime;
        private int band = -1;
        private bool accidentCounted;
        private bool failed;

        private readonly float[] odour = new float[Odour.Channels];

        /// <summary>Whether the amount was found and the monitor is running.</summary>
        public bool Ready
        {
            get { return grime != null && !failed; }
        }

        /// <summary>The grime, 0-100.</summary>
        public float Value
        {
            get { return grime != null ? grime.value : 0f; }
        }

        /// <summary>The current band: 0 clean, then Grubby, Grimy, Filthy.</summary>
        public int CurrentBand
        {
            get { return Mathf.Max(band, 0); }
        }

        /// <summary>The share of today's grime that <paramref name="key"/> put there, 0-1.</summary>
        public float Share(string key)
        {
            float total = 0f, mine = 0f;
            for (int i = 0; i < ledgerKeys.Count; i++)
            {
                total += ledgerAmounts[i];
                if (ledgerKeys[i] == key)
                {
                    mine += ledgerAmounts[i];
                }
            }
            if (total <= 0f)
            {
                return key == Grime.SweatKey ? 1f : 0f;
            }
            return mine / total;
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            try
            {
                grime = Grime.Amount != null ? Grime.Amount.Lookup(gameObject) : null;
                if (grime == null)
                {
                    Debug.LogWarning(Grime.LogPrefix + name + " has no Grime amount; monitor idle");
                    return;
                }
                attributes = this.GetAttributes();
                delta = attributes.Get(Grime.DeltaId);
                effects = GetComponent<Klei.AI.Effects>();
                worker = GetComponent<WorkerBase>();

                sweatRate = new AttributeModifier(Grime.DeltaId, Grime.BaseRatePerSecond, "Sweat");
                workRate = new AttributeModifier(Grime.DeltaId, 0f, "Dirty Work");
                attributes.Add(sweatRate);
                attributes.Add(workRate);

                lastGrime = grime.value;
                if (ledgerKeys.Count != ledgerAmounts.Count)
                {
                    ledgerKeys.Clear();
                    ledgerAmounts.Clear();
                }
                Reconcile();
                Settle();
            }
            catch (Exception e)
            {
                failed = true;
                Debug.LogWarning(Grime.LogPrefix + "spawn failed on " + name + ": " + e);
            }
        }

        protected override void OnCleanUp()
        {
            if (attributes != null)
            {
                for (int i = 0; i < slow.Length; i++)
                {
                    if (slow[i] != null)
                    {
                        attributes.Remove(slow[i]);
                    }
                }
            }
            base.OnCleanUp();
        }

        public void Sim1000ms(float dt)
        {
            if (!Ready)
            {
                return;
            }
            try
            {
                Tick(dt);
            }
            catch (Exception e)
            {
                failed = true;
                Debug.LogWarning(Grime.LogPrefix + "tick failed on " + name + "; monitor stopped: " + e);
            }
        }

        /// <summary>Take <paramref name="amount"/> off, every source in proportion, and settle
        /// the band at once -- the shower's completion relies on the band event coming now.</summary>
        public void Wash(float amount)
        {
            if (!Ready || amount <= 0f)
            {
                return;
            }
            grime.ApplyDelta(-amount);
            Reconcile();
            Settle();
        }

        private void Tick(float dt)
        {
            // The second just gone, attributed at the rates that produced it, before anything
            // below changes a rate or the value.
            Reconcile();

            // A bladder accident, counted once per accident.
            bool accident = effects != null && effects.HasEffect(Grime.AccidentEffectId);
            if (accident && !accidentCounted)
            {
                float before = grime.value;
                grime.ApplyDelta(Grime.AccidentGrime);
                AddToLedger(Grime.SweatKey, grime.value - before);
                lastGrime = grime.value;
            }
            accidentCounted = accident;

            // The work being done.
            string attr = null;
            Workable workable = worker != null ? worker.GetWorkable() : null;
            if (workable != null)
            {
                Klei.AI.Attribute a = workable.GetWorkAttribute();
                attr = a != null ? a.Id : null;
            }
            workRate.SetValue(Grime.BaseRatePerSecond * (Grime.WorkMultiplier(attr) - 1f));

            Settle();
            Smell(dt);
        }

        // Bring the ledger to the amount's value: growth is shared among the positive GrimeDelta
        // modifiers by their current rate, a fall scales every source down.
        private void Reconcile()
        {
            float now = grime.value;
            float grown = now - lastGrime;
            if (grown > 0f && delta != null)
            {
                float positive = 0f;
                for (int i = 0; i < delta.Modifiers.Count; i++)
                {
                    float v = delta.Modifiers[i].Value;
                    if (v > 0f)
                    {
                        positive += v;
                    }
                }
                if (positive > 0f)
                {
                    for (int i = 0; i < delta.Modifiers.Count; i++)
                    {
                        AttributeModifier m = delta.Modifiers[i];
                        if (m.Value > 0f)
                        {
                            AddToLedger(Grime.LedgerKeyFor(m.Description), grown * m.Value / positive);
                        }
                    }
                }
                else
                {
                    AddToLedger(Grime.SweatKey, grown);
                }
            }
            lastGrime = now;

            float total = 0f;
            for (int i = 0; i < ledgerAmounts.Count; i++)
            {
                total += ledgerAmounts[i];
            }
            if (now <= 0f)
            {
                ledgerKeys.Clear();
                ledgerAmounts.Clear();
            }
            else if (total <= 0f)
            {
                ledgerKeys.Clear();
                ledgerAmounts.Clear();
                AddToLedger(Grime.SweatKey, now);
            }
            else if (Mathf.Abs(total - now) > 1e-4f)
            {
                float k = now / total;
                for (int i = 0; i < ledgerAmounts.Count; i++)
                {
                    ledgerAmounts[i] *= k;
                }
            }
        }

        private void AddToLedger(string key, float amount)
        {
            if (amount <= 0f)
            {
                return;
            }
            int i = ledgerKeys.IndexOf(key);
            if (i < 0)
            {
                ledgerKeys.Add(key);
                ledgerAmounts.Add(amount);
            }
            else
            {
                ledgerAmounts[i] += amount;
            }
        }

        // The band ladder and the slowdown, both driven by the value now.
        private void Settle()
        {
            int next = Grime.Band(grime.value, band);
            if (next != band)
            {
                bool first = band < 0;
                band = next;
                if (effects != null && Grime.BandEffects != null)
                {
                    for (int i = 0; i < Grime.BandEffects.Length; i++)
                    {
                        if (i == band - 1)
                        {
                            effects.Add(Grime.BandEffects[i], should_save: false);
                        }
                        else if (effects.HasEffect(Grime.BandEffectIds[i]))
                        {
                            effects.Remove(Grime.BandEffectIds[i]);
                        }
                    }
                }
                if (!first)
                {
                    Trigger(Grime.BandChangedHash);
                }
            }

            float scale = Grime.SlowScale(grime.value);
            for (int i = 0; i < slow.Length; i++)
            {
                float want = scale * (i == 0 ? Grime.AthleticsAtMax : Grime.LabourAtMax);
                float have = slow[i] != null ? slow[i].Value : 0f;
                // Re-added rather than SetValue'd: SetValue fires no OnDirty, so a reader that
                // caches an attribute's total would never see it move.
                if (Mathf.Abs(want - have) < 0.25f && (want == 0f) == (have == 0f))
                {
                    continue;
                }
                if (slow[i] != null)
                {
                    attributes.Remove(slow[i]);
                    slow[i] = null;
                }
                if (want != 0f)
                {
                    slow[i] = new AttributeModifier(Grime.SlowedAttributes[i], want, "Grime");
                    attributes.Add(slow[i]);
                }
            }
        }

        // Give off funk, and react to the strongest weighted channel at the head.
        private void Smell(float dt)
        {
            if (!Odour.Available)
            {
                return;
            }
            int head = Grid.CellAbove(Grid.PosToCell(this));
            if (!Grid.IsValidCell(head))
            {
                return;
            }

            float funk = Grime.FunkPerGrimeSecond * Mathf.Max(0f, grime.value - Grime.FunkOnset)
                * Share(Grime.SweatKey) * dt;
            if (funk > 0f)
            {
                Odour.Emit(head, OdourChannel.Funk, funk);
            }

            if (effects == null || Grime.OdourEffects == null)
            {
                return;
            }
            Grime.ReadOdour(head, odour);
            int best = -1;
            float bestValue = Grime.ReactionThreshold;
            for (int c = 0; c < Odour.Channels; c++)
            {
                if (c == (int)OdourChannel.Funk && CurrentBand >= Grime.NoseBlindBand)
                {
                    continue;
                }
                float weighted = odour[c] * Grime.ChannelWeights[c];
                if (weighted >= bestValue)
                {
                    best = c;
                    bestValue = weighted;
                }
            }
            if (best >= 0)
            {
                effects.Add(Grime.OdourEffects[best], should_save: false);
            }
        }
    }
}
