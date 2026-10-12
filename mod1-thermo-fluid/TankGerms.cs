using System.Collections.Generic;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// GERMS RIDE WITH THE MASS in Mod 1's two tank stores Before this, <see cref="LiquidMixtureTankComponent"/> and
    /// <see cref="GasMixtureTankComponent"/> held element, mass and one temperature and nothing else,
    /// so every germ that entered a Liquid or Gas Reservoir was deleted: the intake dropped
    /// RemoveElement's disease fields and every outfall added `0, 0`. A vanilla reservoir keeps them
    /// (its Storage items are PrimaryElements, and DiseaseContainers grows and kills what they carry).
    ///
    /// THE ONE RULE, used by both tanks; the tanks differ only in where they call it. Klei's own
    /// functions, called rather than copied:
    /// <list type="bullet">
    /// <item>add: `SimUtil.CalculateFinalDiseaseInfo` (Klei/SimUtil.cs), the combine every vanilla
    /// container uses when two germ loads meet;</item>
    /// <item>take: `(int)(fraction * count)`, the split `ConduitFlow.AddElement` applies to a
    /// partial accept (ConduitFlow.cs AddElement), so a pipe and the store it fed never disagree by
    /// a germ;</item>
    /// <item>grow and die: `DiseaseContainers.CalculateDelta` (element growth rule, temperature
    /// half-life, cell exposure, radiation) with the accumulated-error carry of
    /// `DiseaseContainers.Sim200ms`. The tag multiplier is 1, exactly: the only TagGrowthRules in
    /// the game are FoodGerms' Edible and Pickled (Klei.AI/FoodGerms.cs, applied in
    /// Disease.GetGrowthRateForTags), and no liquid or gas element carries either tag.</item>
    /// </list>
    ///
    /// KEYED BY ELEMENT, not by the store's entry index. One germ load per element in the store,
    /// the way a vanilla Storage carries one load per PrimaryElement. The gas tank's phase step
    /// (PhaseVessel.ApplyPhaseChangeStep) appends and removes entries, so an index-aligned list
    /// would slide out of step with the mass it describes.
    ///
    /// NO NEW SAVE TYPE. The rows live in each tank's own [Serialize] primitive lists; this struct
    /// only wraps them, built on demand (KSerialization assigns fresh lists on load, so a held
    /// reference would go stale). On a save from before this the lists keep their empty initializers
    /// (DeserializationMapping maps only the members the SAVE holds), which is exactly what that
    /// save contained: no germs. An older Mod 1 skips the new members on a newer save.
    ///
    /// WHERE GERMS STOP. A load whose element has left the store folds into the store's largest
    /// remaining entry (that happens only on a rounding remainder or a mass-only removal: the
    /// refrigeration loop's TryRemoveMass, which keeps germs in the tank). A store with no mass at
    /// all drops its germs, as vanilla does when a PrimaryElement or a conduit cell reaches zero
    /// mass.
    /// </summary>
    internal struct TankGermBook
    {
        private readonly List<int> element;
        private readonly List<byte> disease;
        private readonly List<int> count;
        private readonly List<float> error;

        internal TankGermBook(List<int> element, List<byte> disease, List<int> count, List<float> error)
        {
            this.element = element;
            this.disease = disease;
            this.count = count;
            this.error = error;
            Fit();
        }

        /// <summary>The rows stay one to one; a save cut between list writes cannot misalign them.</summary>
        private void Fit()
        {
            int n = Mathf.Min(element.Count, Mathf.Min(disease.Count, count.Count));
            if (element.Count > n) element.RemoveRange(n, element.Count - n);
            if (disease.Count > n) disease.RemoveRange(n, disease.Count - n);
            if (count.Count > n) count.RemoveRange(n, count.Count - n);
            while (error.Count < n) error.Add(0f);
            if (error.Count > n) error.RemoveRange(n, error.Count - n);
        }

        internal bool Empty => element.Count == 0;

        /// <summary>The germ load riding with <paramref name="elementIdx"/>: (255, 0) when none.</summary>
        internal void Get(int elementIdx, out byte diseaseIdx, out int diseaseCount)
        {
            int j = element.IndexOf(elementIdx);
            diseaseIdx = j >= 0 ? disease[j] : byte.MaxValue;
            diseaseCount = j >= 0 ? count[j] : 0;
        }

        /// <summary>Germs of one disease across every element in the store.</summary>
        internal int Count(byte diseaseIdx)
        {
            int total = 0;
            for (int j = 0; j < element.Count; j++)
            {
                if (disease[j] == diseaseIdx)
                {
                    total += count[j];
                }
            }
            return total;
        }

        /// <summary>Combines a germ load into <paramref name="elementIdx"/>'s, Klei's combine.</summary>
        internal void Add(int elementIdx, byte diseaseIdx, int diseaseCount)
        {
            if (diseaseIdx == byte.MaxValue || diseaseCount <= 0)
            {
                return;
            }
            int j = element.IndexOf(elementIdx);
            if (j < 0)
            {
                element.Add(elementIdx);
                disease.Add(diseaseIdx);
                count.Add(diseaseCount);
                error.Add(0f);
                return;
            }
            Klei.SimUtil.DiseaseInfo combined = Klei.SimUtil.CalculateFinalDiseaseInfo(disease[j], count[j], diseaseIdx, diseaseCount);
            Set(j, combined.idx, combined.count);
        }

        /// <summary>
        /// The germs that go with <paramref name="kg"/> of an element the store holds
        /// <paramref name="entryKg"/> of: all of them for the whole entry, else Klei's split.
        /// </summary>
        internal int Share(int elementIdx, float kg, float entryKg)
        {
            int j = element.IndexOf(elementIdx);
            if (j < 0 || kg <= 0f || entryKg <= 0f)
            {
                return 0;
            }
            return kg >= entryKg ? count[j] : (int)(kg / entryKg * count[j]);
        }

        /// <summary>Takes <paramref name="germs"/> of <paramref name="elementIdx"/>'s load.</summary>
        internal void Take(int elementIdx, int germs)
        {
            int j = element.IndexOf(elementIdx);
            if (j >= 0 && germs > 0)
            {
                Set(j, disease[j], count[j] - germs);
            }
        }

        /// <summary>
        /// THE WAY OUT TO A PIPE. Offers <paramref name="kg"/> of <paramref name="elementIdx"/> (the
        /// store holds <paramref name="entryKg"/> of it) to a conduit cell with its germ share, and
        /// takes from the book exactly what the conduit kept: `(int)(accepted / offered * share)`,
        /// AddElement's own expression. Returns the mass accepted; the caller removes that mass from
        /// its store as it always has.
        /// </summary>
        internal float Emit(ConduitFlow flow, int cell, int elementIdx, float kg, float entryKg, float temperatureK)
        {
            int share = Share(elementIdx, kg, entryKg);
            byte diseaseIdx = byte.MaxValue;
            if (share > 0)
            {
                Get(elementIdx, out diseaseIdx, out int _);
            }
            float accepted = flow.AddElement(cell, ElementLoader.elements[elementIdx].id, kg, temperatureK,
                share > 0 ? diseaseIdx : byte.MaxValue, share > 0 ? share : 0);
            if (accepted > 0f && share > 0)
            {
                Take(elementIdx, accepted >= kg ? share : (int)(accepted / kg * share));
            }
            return accepted;
        }

        /// <summary>
        /// A phase step moved <paramref name="convertedKg"/> from one element to another: the germs
        /// on the converted part go with it (Klei keeps a cell's and a conduit's germs through a
        /// state change). Source and target are read off the before/after lists: the one entry
        /// that lost mass, and the one that gained it.
        /// </summary>
        internal void FollowConversion(int[] beforeIdx, float[] beforeKg, List<int> afterIdx, List<float> afterKg,
            float convertedKg)
        {
            int source = -1;
            float sourceBeforeKg = 0f;
            float lost = 0f;
            for (int i = 0; i < beforeIdx.Length; i++)
            {
                int k = afterIdx.IndexOf(beforeIdx[i]);
                float drop = beforeKg[i] - (k >= 0 ? afterKg[k] : 0f);
                if (drop > lost)
                {
                    lost = drop;
                    source = beforeIdx[i];
                    sourceBeforeKg = beforeKg[i];
                }
            }
            int target = -1;
            float gained = 0f;
            for (int k = 0; k < afterIdx.Count; k++)
            {
                int i = System.Array.IndexOf(beforeIdx, afterIdx[k]);
                float rise = afterKg[k] - (i >= 0 ? beforeKg[i] : 0f);
                if (rise > gained)
                {
                    gained = rise;
                    target = afterIdx[k];
                }
            }
            if (source < 0 || target < 0 || source == target)
            {
                return;
            }
            int moved = Share(source, convertedKg, sourceBeforeKg);
            if (moved <= 0)
            {
                return;
            }
            Get(source, out byte diseaseIdx, out int _);
            Take(source, moved);
            Add(target, diseaseIdx, moved);
        }

        /// <summary>
        /// One store tick, after the tank's own work: loads whose element has left fold into the
        /// largest remaining entry (or go with the last of the mass), then every load grows or dies
        /// by DiseaseContainers.CalculateDelta over <paramref name="dt"/> with the error carry.
        /// </summary>
        internal void Tick(List<int> storeIdx, List<float> storeKg, int cell, float temperatureK, float dt)
        {
            if (element.Count == 0)
            {
                return;
            }
            Settle(storeIdx, storeKg);
            bool radiation = Sim.IsRadiationEnabled();
            for (int j = element.Count - 1; j >= 0; j--)
            {
                int i = storeIdx.IndexOf(element[j]);
                Klei.AI.Disease germ = Db.Get().Diseases[disease[j]];
                float delta = DiseaseContainers.CalculateDelta(count[j], (ushort)element[j], storeKg[i], cell,
                    temperatureK, 1f, germ, dt, radiation) + error[j];
                int whole = (int)delta;
                error[j] = delta - whole;
                Set(j, disease[j], count[j] + whole);
            }
        }

        private void Settle(List<int> storeIdx, List<float> storeKg)
        {
            int dominant = -1;
            for (int i = 0; i < storeIdx.Count; i++)
            {
                if (storeKg[i] > 0f && (dominant < 0 || storeKg[i] > storeKg[dominant]))
                {
                    dominant = i;
                }
            }
            for (int j = element.Count - 1; j >= 0; j--)
            {
                int i = storeIdx.IndexOf(element[j]);
                if (i >= 0 && storeKg[i] > 0f)
                {
                    continue;
                }
                byte diseaseIdx = disease[j];
                int orphans = count[j];
                RemoveRow(j);
                if (dominant >= 0)
                {
                    Add(storeIdx[dominant], diseaseIdx, orphans);
                }
            }
        }

        /// <summary>Rig setup only: forget every load (the tank's store was just emptied).</summary>
        internal void Clear()
        {
            element.Clear();
            disease.Clear();
            count.Clear();
            error.Clear();
        }

        private void Set(int j, byte diseaseIdx, int diseaseCount)
        {
            if (diseaseIdx == byte.MaxValue || diseaseCount <= 0)
            {
                RemoveRow(j);
                return;
            }
            disease[j] = diseaseIdx;
            count[j] = diseaseCount;
        }

        private void RemoveRow(int j)
        {
            element.RemoveAt(j);
            disease.RemoveAt(j);
            count.RemoveAt(j);
            error.RemoveAt(j);
        }
    }
}
