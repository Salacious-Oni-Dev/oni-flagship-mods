using System;
using HarmonyLib;
using OniFramework;
using UnityEngine;

namespace Mod2MatterEnvironment
{
    /// <summary>
    /// SUGAR AND ALCOHOL CARRIED BY WATER. Two cell properties, kilograms per cell,
    /// both <see cref="CellPropertyTransport.FollowsLiquidMass"/> and both mixing: <c>sugar</c> and
    /// <c>ethanol</c> under this mod's owner prefix (<c>mod2matterenvironment.sugar</c>,
    /// <c>mod2matterenvironment.ethanol</c> -- the prefix is the one
    /// <see cref="FrameworkVersion.Require(int, int, string, out ExtOwner)"/> mints from the mod's
    /// name, and it is a permanent key in every save that holds the property). A water cell carrying
    /// them is a mash or a wash; it is still Water, exactly as carbonated water is still Water.
    ///
    /// THE SIM CARRIES THEM ON THE GRID; THE FRAMEWORK CARRIES THEM OFF IT. Every liquid kernel moves
    /// a FollowsLiquidMass amount with its liquid, and the mixing sweep evens it out through still
    /// water. Off the grid, <see cref="DissolvedCargo.RegisterPayload"/> gives them
    /// the ride dissolved gas has: pumps, pipes, storage, bottles and saves.
    ///
    /// WHERE THEY GO WHEN THEIR WATER CANNOT KEEP THEM -- the one decision only this mod can make,
    /// because the framework knows them only as kilograms. Both exits run through <see cref="Exit"/>:
    /// <list type="bullet">
    /// <item>a cell that still holds liquid takes the amount back (it dissolves again);</item>
    /// <item>otherwise sugar drops as Sucrose, the solid Klei's own SugarWater leaves when it boils
    /// or freezes (<c>lowTempTransitionOreId</c>/<c>highTempTransitionOreId: Sucrose</c>);</item>
    /// <item>and ethanol leaves as Klei's Ethanol in the phase its temperature gives: vapour
    /// above Ethanol's boiling point, a falling drop of liquid below it.</item>
    /// </list>
    /// Every kilogram is counted on one of the ledgers below; <see cref="UnroutedKg"/> is the only
    /// one that means matter was lost, and a rig asserts it stays zero.
    ///
    /// Known simplification, the same one dissolved gas makes: a payload has mass but no heat
    /// capacity of its own, so a kilogram of sugar carries no heat while it rides the water.
    /// </summary>
    internal static class Fermentables
    {
        internal const string SugarLeaf = "sugar";
        internal const string EthanolLeaf = "ethanol";

        internal enum Kind
        {
            Sugar,
            Ethanol,
        }

        private static ExtOwner owner;
        private static bool installed;

        /// <summary>Full property names, <c>owner.leaf</c>; null until <see cref="Install"/>.</summary>
        internal static string SugarName { get; private set; }
        internal static string EthanolName { get; private set; }

        /// <summary>The sim's property index in the current world, or negative.</summary>
        internal static int SugarIndex { get; private set; } = -1;
        internal static int EthanolIndex { get; private set; } = -1;

        /// <summary>The carriage lanes <see cref="DissolvedCargo"/> carries them on, or -1.</summary>
        internal static int SugarLane { get; private set; } = -1;
        internal static int EthanolLane { get; private set; } = -1;

        /// <summary>Generation of the registrar window the indices above belong to.</summary>
        internal static long Generation { get; private set; } = -1;

        // ---- the exit ledger, kilograms since load -----------------------------------------

        internal static double SugarRedissolvedKg { get; private set; }
        internal static double SugarToSucroseKg { get; private set; }
        internal static double EthanolRedissolvedKg { get; private set; }
        internal static double EthanolToVapourKg { get; private set; }
        internal static double EthanolToLiquidKg { get; private set; }

        /// <summary>Kilograms that reached an exit with no cell to put them in. Must stay zero.</summary>
        internal static double UnroutedKg { get; private set; }

        /// <summary>Released records seen for the two properties, and the kilograms they carried.
        /// </summary>
        internal static long ReleasedRecords { get; private set; }
        internal static double ReleasedSugarKg { get; private set; }
        internal static double ReleasedEthanolKg { get; private set; }

        /// <summary>Kilograms the framework's carriage handed back to <see cref="Exit"/>.</summary>
        internal static double CargoExitSugarKg { get; private set; }
        internal static double CargoExitEthanolKg { get; private set; }

        internal static void ResetAccounting()
        {
            SugarRedissolvedKg = 0.0;
            SugarToSucroseKg = 0.0;
            EthanolRedissolvedKg = 0.0;
            EthanolToVapourKg = 0.0;
            EthanolToLiquidKg = 0.0;
            UnroutedKg = 0.0;
            ReleasedRecords = 0;
            ReleasedSugarKg = 0.0;
            ReleasedEthanolKg = 0.0;
            CargoExitSugarKg = 0.0;
            CargoExitEthanolKg = 0.0;
        }

        /// <summary>
        /// From <c>OnLoad</c>: names the two payloads to the carriage (before any world exists, as
        /// <see cref="DissolvedCargo.RegisterPayload"/> requires) and subscribes the per-world
        /// registration and the released stream.
        /// </summary>
        internal static void Install(Harmony harmony, ExtOwner extOwner)
        {
            if (installed)
            {
                return;
            }
            owner = extOwner;
            SugarName = owner.Compose(SugarLeaf);
            EthanolName = owner.Compose(EthanolLeaf);
            if (SugarName == null || EthanolName == null)
            {
                Debug.LogWarning("[Mod2MatterEnvironment] Fermentables: the owner token cannot compose "
                    + "the payload names; sugar and ethanol are off.");
                return;
            }

            DissolvedCargo.Install(harmony);
            SugarLane = DissolvedCargo.RegisterPayload(SugarName,
                (cell, kg, t) => FromCargo(Kind.Sugar, cell, kg, t));
            EthanolLane = DissolvedCargo.RegisterPayload(EthanolName,
                (cell, kg, t) => FromCargo(Kind.Ethanol, cell, kg, t));

            SimExtRegistrar.Install(harmony);
            SimExtRegistrar.Opening += Register;
            LiquidPayload.Released += OnReleased;
            installed = true;
        }

        // Once per world, inside the registrar's window. Transport and mixing describe the property
        // and are not saved, so they are sent every time with the registration.
        private static void Register()
        {
            SugarIndex = RegisterOne(SugarLeaf);
            EthanolIndex = RegisterOne(EthanolLeaf);
            Generation = SimExtRegistrar.Generation;
        }

        private static int RegisterOne(string leaf)
        {
            int idx = SimExtCellProperties.RegisterFloat(owner, leaf, ExtPersistence.Saved, 0f);
            if (idx < 0)
            {
                // RegisterFloat has already logged why.
                return idx;
            }
            SimExtCellProperties.SetTransport(idx, CellPropertyTransport.FollowsLiquidMass);
            SimExtCellProperties.SetMixing(idx, SimExtCellProperties.DefaultMixingShare);
            return idx;
        }

        /// <summary>Kilograms of <paramref name="kind"/> in the liquid at <paramref name="cell"/>,
        /// through the barrier; 0 if unavailable. For rigs and tooltips, not a per-tick sweep.</summary>
        internal static float Read(Kind kind, int cell)
        {
            int idx = kind == Kind.Sugar ? SugarIndex : EthanolIndex;
            float value;
            return idx >= 0 && Grid.IsValidCell(cell)
                && SimExtCellProperties.TryReadFloat(idx, cell, out value) ? value : 0f;
        }

        /// <summary>Put <paramref name="kg"/> of <paramref name="kind"/> into the liquid at
        /// <paramref name="cell"/>. Queued. The caller owns the matter.</summary>
        internal static void Add(Kind kind, int cell, float kg)
        {
            int idx = kind == Kind.Sugar ? SugarIndex : EthanolIndex;
            if (idx >= 0 && Grid.IsValidCell(cell) && kg != 0f && !float.IsNaN(kg))
            {
                SimExtCellProperties.AddFloat(idx, cell, kg);
            }
        }

        private static void OnReleased(LiquidPayloadReleased r)
        {
            Kind kind;
            if (r.PropertyIdx >= 0 && r.PropertyIdx == SugarIndex)
            {
                kind = Kind.Sugar;
            }
            else if (r.PropertyIdx >= 0 && r.PropertyIdx == EthanolIndex)
            {
                kind = Kind.Ethanol;
            }
            else
            {
                return;
            }
            if (!(r.Amount > 0f))
            {
                return;
            }
            ReleasedRecords++;
            if (kind == Kind.Sugar)
            {
                ReleasedSugarKg += r.Amount;
            }
            else
            {
                ReleasedEthanolKg += r.Amount;
            }
            Exit(kind, r.Cell, r.Amount, r.TemperatureK);
        }

        private static void FromCargo(Kind kind, int cell, float kg, float temperatureK)
        {
            if (kind == Kind.Sugar)
            {
                CargoExitSugarKg += kg;
            }
            else
            {
                CargoExitEthanolKg += kg;
            }
            Exit(kind, cell, kg, temperatureK);
        }

        /// <summary>
        /// THE ONE EXIT for both payloads, whichever path found them: back into the liquid if the
        /// cell has any, otherwise into their own element. Game thread.
        /// </summary>
        internal static void Exit(Kind kind, int cell, float kg, float temperatureK)
        {
            if (!(kg > 0f) || float.IsNaN(kg))
            {
                return;
            }
            if (!Grid.IsValidCell(cell))
            {
                UnroutedKg += kg;
                Debug.LogWarning("[Mod2MatterEnvironment] Fermentables: " + (kg * 1000f).ToString("F3")
                    + " g of " + kind + " reached its exit with no cell to go to.");
                return;
            }
            if (!(temperatureK > 0f))
            {
                temperatureK = Grid.Temperature[cell] > 0f ? Grid.Temperature[cell] : 293.15f;
            }
            if (Grid.IsLiquid(cell) && (kind == Kind.Sugar ? SugarIndex : EthanolIndex) >= 0)
            {
                Add(kind, cell, kg);
                if (kind == Kind.Sugar)
                {
                    SugarRedissolvedKg += kg;
                }
                else
                {
                    EthanolRedissolvedKg += kg;
                }
                return;
            }
            int open = OpenCellNear(cell);
            if (kind == Kind.Sugar)
            {
                Element sucrose = ElementLoader.FindElementByHash(SimHashes.Sucrose);
                if (sucrose == null || sucrose.substance == null)
                {
                    UnroutedKg += kg;
                    return;
                }
                sucrose.substance.SpawnResource(Grid.CellToPosCCC(open, Grid.SceneLayer.Ore), kg,
                    temperatureK, byte.MaxValue, 0);
                SugarToSucroseKg += kg;
                return;
            }
            Element ethanol = ElementLoader.FindElementByHash(SimHashes.Ethanol);
            if (ethanol == null)
            {
                UnroutedKg += kg;
                return;
            }
            if (temperatureK >= ethanol.highTemp)
            {
                SimMessages.AddRemoveSubstance(open, SimHashes.EthanolGas,
                    CellEventLogger.Instance.ExhaustSimUpdate, kg, temperatureK, byte.MaxValue, 0);
                EthanolToVapourKg += kg;
                return;
            }
            FallingWater.instance.AddParticle(open, ethanol.idx, kg, temperatureK, byte.MaxValue, 0,
                skip_sound: true, skip_decor: true);
            EthanolToLiquidKg += kg;
        }

        // The cell itself unless it is solid, else the first open neighbour (DissolvedGas's rule).
        private static int OpenCellNear(int cell)
        {
            if (!Grid.Solid[cell])
            {
                return cell;
            }
            int[] around = { Grid.CellAbove(cell), Grid.CellLeft(cell), Grid.CellRight(cell),
                Grid.CellBelow(cell) };
            foreach (int c in around)
            {
                if (Grid.IsValidCell(c) && !Grid.Solid[c])
                {
                    return c;
                }
            }
            return cell;
        }
    }
}
