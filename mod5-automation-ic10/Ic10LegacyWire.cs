using System.Collections.Generic;
using HarmonyLib;

namespace Mod5AutomationIc10
{
    // The data cable is to replace ONI's automation wire; until it does, the two coexist and
    // the wire stays as the legacy path, for A/B testing. So a
    // building that is a device on a data network counts as connected: the "No Automation
    // Wire Connected" warning that a sensor's or gate's port raises when no wire reaches it
    // (LogicPorts.ShowMissingWireIcon, ports built with show_wire_missing_icon) is not shown
    // for it. A wire still works as before; this only drops the warning.
    //
    // Vanilla re-checks the warning only when its own wire network changes at a port
    // (OnLogicNetworkConnectionChanged) or the building spawns, so a data cable laid or
    // lifted would leave it stale. Refresh re-checks every building whose data connection
    // changed since the grid last moved.
    internal static class Ic10LegacyWire
    {
        // Private in LogicPorts. If a game update renames it, the warning goes stale after a
        // cable edit (as it would with no Refresh), and nothing throws.
        private static readonly System.Action<LogicPorts> UpdateIcon = IconUpdater();

        private static System.Action<LogicPorts> IconUpdater()
        {
            System.Reflection.MethodInfo method = AccessTools.Method(typeof(LogicPorts), "UpdateMissingWireIcon");
            if (method == null)
            {
                Mod5Log.Warn("LogicPorts.UpdateMissingWireIcon not found; the missing-wire warning will not follow data cable edits");
                return null;
            }
            return AccessTools.MethodDelegate<System.Action<LogicPorts>>(method);
        }

        private static HashSet<int> _connected = new HashSet<int>();
        private static HashSet<int> _scratch = new HashSet<int>();
        private static readonly List<KPrefabID> Buildings = new List<KPrefabID>();
        private static Ic10DataGrid _grid;
        private static int _version = -1;

        public static bool DataConnected(LogicPorts ports)
        {
            Ic10DataGrid grid = Ic10DataGrid.Current;
            if (grid == null || ports == null)
            {
                return false;
            }
            KPrefabID id = ports.GetComponent<KPrefabID>();
            return id != null && grid.OnAnyNetwork(id.InstanceID);
        }

        // Once a frame, after the grid's own tick.
        public static void Refresh(Ic10DataGrid grid)
        {
            if (grid != _grid)
            {
                // A new colony: nothing from the last one is still connected.
                _grid = grid;
                _version = -1;
                _connected.Clear();
            }
            if (grid.Version == _version)
            {
                return;
            }
            _version = grid.Version;
            Buildings.Clear();
            grid.BuildingsOnNetworks(Buildings);
            _scratch.Clear();
            for (int i = 0; i < Buildings.Count; i++)
            {
                KPrefabID id = Buildings[i];
                _scratch.Add(id.InstanceID);
                if (!_connected.Contains(id.InstanceID))
                {
                    Recheck(id);
                }
            }
            foreach (int gone in _connected)
            {
                if (!_scratch.Contains(gone))
                {
                    KPrefabID id = Ic10Devices.Building(gone);
                    if (id != null)
                    {
                        Recheck(id);
                    }
                }
            }
            HashSet<int> swap = _connected;
            _connected = _scratch;
            _scratch = swap;
        }

        private static void Recheck(KPrefabID id)
        {
            LogicPorts ports = id.GetComponent<LogicPorts>();
            if (ports != null && UpdateIcon != null)
            {
                UpdateIcon(ports);
            }
        }
    }

    [HarmonyPatch(typeof(LogicPorts), "ShowMissingWireIcon")]
    internal static class LogicPorts_ShowMissingWireIcon_Ic10
    {
        private static void Postfix(LogicPorts __instance, ref bool __result)
        {
            if (__result && Ic10LegacyWire.DataConnected(__instance))
            {
                __result = false;
            }
        }
    }
}
