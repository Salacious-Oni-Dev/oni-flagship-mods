using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Mod2MatterEnvironment
{
    /// <summary>
    /// KLEI'S SUGARWATER AS A SOURCE OF SUGAR. Vanilla keeps SugarWater and Water apart
    /// as two liquids that layer. In this mod they are one solution: where SugarWater touches Water,
    /// it dissolves into it -- its water joins the Water cell, and its sugar rides that water as
    /// <see cref="Fermentables"/> sugar, which is then carried, mixed and piped like any other.
    ///
    /// THE SPLIT IS KLEI'S OWN NUMBER. SugarWater boils and freezes into Sucrose at a mass conversion
    /// of 0.77 (<see cref="FruitSugar.SugarWaterSugarFraction"/>), so a kilogram of it is 0.77 kg of
    /// sugar and 0.23 kg of water.
    ///
    /// NOTHING IS CREATED. The SugarWater is taken with vanilla's own ConsumeMass, and the water and
    /// sugar are added only in its mass-consumed callback, for the mass the sim says it actually
    /// removed and at the temperature it had. If the Water cell has gone by then, the water falls as
    /// a drop there and the sugar takes <see cref="Fermentables.Exit"/>.
    ///
    /// PERF: one sixteenth of the grid is looked at per 200 ms tick -- an element-index compare per
    /// cell -- so a full sweep takes 3.2 s, and a cell gives up at most
    /// <see cref="KgPerCellPerSweep"/> per sweep. Known simplification, shared with every payload:
    /// the sugar carries no heat capacity while dissolved, so SugarWater's sugar share of heat
    /// capacity is not carried over.
    /// </summary>
    internal static class SugarWaterSource
    {
        internal const int Slices = 16;

        /// <summary>The most one SugarWater cell gives up per sweep.</summary>
        internal const float KgPerCellPerSweep = 5f;

        private static int slice;

        private static readonly Dictionary<int, int> pending = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> pendingOrigin = new Dictionary<int, int>();
        private static readonly HashSet<int> pendingCells = new HashSet<int>();

        private static System.Action<Sim.MassConsumedCallback, object> callback;

        internal static double SugarWaterTakenKg { get; private set; }
        internal static double WaterAddedKg { get; private set; }
        internal static double SugarAddedKg { get; private set; }
        internal static double FallbackKg { get; private set; }
        internal static long Requests { get; private set; }

        /// <summary>Off by default so a rig can isolate it; Mod 2 turns it on at load.</summary>
        internal static bool Enabled { get; set; }

        internal static void ResetAccounting()
        {
            SugarWaterTakenKg = 0.0;
            WaterAddedKg = 0.0;
            SugarAddedKg = 0.0;
            FallbackKg = 0.0;
            Requests = 0;
        }

        /// <summary>Requests consumed but not yet answered.</summary>
        internal static int Pending
        {
            get { return pending.Count; }
        }

        internal static void Tick()
        {
            if (!Enabled || Game.Instance == null || Fermentables.SugarIndex < 0 || Grid.CellCount <= 0)
            {
                return;
            }
            Element sugarWater = ElementLoader.FindElementByHash(SimHashes.SugarWater);
            Element water = ElementLoader.FindElementByHash(SimHashes.Water);
            if (sugarWater == null || water == null)
            {
                return;
            }
            int n = Grid.CellCount;
            int from = (int)((long)n * slice / Slices);
            int to = (int)((long)n * (slice + 1) / Slices);
            slice = (slice + 1) % Slices;
            for (int cell = from; cell < to; cell++)
            {
                if (Grid.ElementIdx[cell] != sugarWater.idx || pendingCells.Contains(cell))
                {
                    continue;
                }
                float mass = Grid.Mass[cell];
                if (!(mass > 0f))
                {
                    continue;
                }
                int into = WaterNeighbour(cell, water.idx);
                if (into < 0)
                {
                    continue;
                }
                Request(cell, into, Mathf.Min(mass, KgPerCellPerSweep));
            }
        }

        private static int WaterNeighbour(int cell, ushort waterIdx)
        {
            int[] around = { Grid.CellAbove(cell), Grid.CellLeft(cell), Grid.CellRight(cell),
                Grid.CellBelow(cell) };
            foreach (int c in around)
            {
                if (Grid.IsValidCell(c) && Grid.ElementIdx[c] == waterIdx && Grid.Mass[c] > 0f)
                {
                    return c;
                }
            }
            return -1;
        }

        private static void Request(int cell, int into, float kg)
        {
            if (callback == null)
            {
                callback = OnConsumed;
            }
            var handle = Game.Instance.massConsumedCallbackManager.Add(callback, null,
                "Mod2.SugarWaterSource");
            pending[handle.index] = into;
            pendingOrigin[handle.index] = cell;
            pendingCells.Add(cell);
            Requests++;
            // Radius 1 visits the origin alone (see DissolvedCargo.Consume on why 0 visits nothing).
            SimMessages.ConsumeMass(cell, SimHashes.SugarWater, kg, 1, handle.index);
        }

        private static void OnConsumed(Sim.MassConsumedCallback cb, object data)
        {
            int into;
            if (!pending.TryGetValue(cb.callbackIdx, out into))
            {
                return;
            }
            pending.Remove(cb.callbackIdx);
            int origin;
            if (pendingOrigin.TryGetValue(cb.callbackIdx, out origin))
            {
                pendingOrigin.Remove(cb.callbackIdx);
                pendingCells.Remove(origin);
            }
            float kg = cb.mass;
            if (!(kg > 0f))
            {
                return;
            }
            float temperatureK = cb.temperature > 0f ? cb.temperature
                : (Grid.IsValidCell(into) ? Grid.Temperature[into] : 293.15f);
            float sugar = kg * FruitSugar.SugarWaterSugarFraction;
            float water = kg - sugar;
            SugarWaterTakenKg += kg;

            Element waterElement = ElementLoader.FindElementByHash(SimHashes.Water);
            if (Grid.IsValidCell(into) && Grid.ElementIdx[into] == waterElement.idx && Grid.Mass[into] > 0f)
            {
                SimMessages.AddRemoveSubstance(into, SimHashes.Water,
                    CellEventLogger.Instance.ExhaustSimUpdate, water, temperatureK, cb.diseaseIdx,
                    cb.diseaseCount);
                Fermentables.Add(Fermentables.Kind.Sugar, into, sugar);
                WaterAddedKg += water;
                SugarAddedKg += sugar;
                return;
            }
            // The Water cell moved on before the consume landed. The water falls as a drop where it
            // was meant to go, and the sugar takes the payload exit from the same cell.
            int at = Grid.IsValidCell(into) ? into : origin;
            FallingWater.instance.AddParticle(at, waterElement.idx, water, temperatureK, cb.diseaseIdx,
                cb.diseaseCount, skip_sound: true, skip_decor: true);
            Fermentables.Exit(Fermentables.Kind.Sugar, at, sugar, temperatureK);
            FallbackKg += kg;
        }
    }

    [HarmonyPatch(typeof(Game), "UnsafeSim200ms")]
    internal static class SugarWaterSourceTick
    {
        private static void Postfix()
        {
            SugarWaterSource.Tick();
        }
    }
}
