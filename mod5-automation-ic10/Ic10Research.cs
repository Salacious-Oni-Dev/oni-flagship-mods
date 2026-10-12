using System.Collections.Generic;
using HarmonyLib;

namespace Mod5AutomationIc10
{
    // Where each Mod 5 building sits in ONI's research tree.
    //
    // A building no tech names is buildable from the first cycle. Stationeers has no
    // research tree: what a player can make is gated by the Electronics Printer and by the
    // materials each kit needs (StreamingAssets/Data/electronics.xml): the logic I/O and
    // processor kits are 2 gold and 2 copper, the IC Housing kit (ItemKitLogicCircuit) needs
    // steel and solder, and the IC10 chip (ItemIntegratedCircuit10) gold, steel, electrum and
    // solder. That order is kept on ONI's automation techs:
    //
    //   LogicControl        ONI's first automation tech (switch, wire): the data cable and its
    //                       bridge, the hand bricks, the reader, the writer, the writer switch
    //   LogicCircuits       ONI's gates: the arithmetic and comparison units, the memory
    //   ParallelAutomation  ONI's ribbon, several signals at once: the batch and slot units,
    //                       the hash generator, the mirror
    //   Multiplexing        later automation: the IC Housing and the PID controller, the parts
    //                       Stationeers makes from alloys
    //   SolidManagement     beside vanilla's SolidFilter: the IC Sorter
    //
    // The ids are added in a postfix on Db.Initialize, before Techs.PostProcess (called from
    // Assets) turns them into TechItems, which the buildings register as they are made. A tech
    // a game update renames is logged and its buildings stay buildable from the start.
    [HarmonyPatch(typeof(Db), nameof(Db.Initialize))]
    public static class Ic10Research
    {
        public static readonly Dictionary<string, string[]> ByTech = new Dictionary<string, string[]>
        {
            {
                "LogicControl", new[]
                {
                    Ic10DataCableConfig.Id, Ic10DataBridgeConfig.Id,
                    Ic10SwitchConfig.Id, Ic10ButtonConfig.Id, Ic10DialConfig.Id, Ic10DisplayConfig.Id,
                    Ic10LogicReaderConfig.Id, Ic10LogicWriterConfig.Id, Ic10LogicWriterSwitchConfig.Id,
                }
            },
            {
                "LogicCircuits", new[]
                {
                    Ic10LogicMathConfig.Id, Ic10LogicMathUnaryConfig.Id, Ic10LogicCompareConfig.Id,
                    Ic10LogicSelectConfig.Id, Ic10LogicMinMaxConfig.Id, Ic10MemoryConfig.Id,
                }
            },
            {
                "ParallelAutomation", new[]
                {
                    Ic10LogicBatchReaderConfig.Id, Ic10LogicBatchWriterConfig.Id, Ic10LogicSlotReaderConfig.Id,
                    Ic10LogicBatchSlotReaderConfig.Id, Ic10LogicHashGenConfig.Id, Ic10LogicMirrorConfig.Id,
                }
            },
            { "Multiplexing", new[] { Ic10HousingConfig.Id, Ic10LogicPidConfig.Id } },
            { "SolidManagement", new[] { Ic10SorterConfig.Id } },
        };

        public static void Postfix(Db __instance)
        {
            int placed = 0;
            foreach (KeyValuePair<string, string[]> pair in ByTech)
            {
                Tech tech = __instance.Techs.TryGet(pair.Key);
                if (tech == null)
                {
                    Mod5Log.Warn("research: no tech " + pair.Key + "; " + string.Join(", ", pair.Value) + " stay buildable from the start");
                    continue;
                }
                foreach (string id in pair.Value)
                {
                    if (!tech.unlockedItemIDs.Contains(id))
                    {
                        tech.unlockedItemIDs.Add(id);
                        placed++;
                    }
                }
            }
            Mod5Log.Info("research: " + placed + " buildings placed in " + ByTech.Count + " techs");
        }
    }
}
