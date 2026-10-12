using System.Collections.Generic;
using HarmonyLib;
using KSerialization;
using UnityEngine;

namespace Mod5AutomationIc10
{
    // A chip drives a building's automation input over the data cable; an attached wire wins
    // while it is attached. The data cable is to replace
    // ONI's automation wire, and until it does a chip must be able to do at a port whatever
    // a wire does there.
    //
    // The value a chip writes is delivered the way a wire delivers one: through the port's
    // own LogicEventHandler.ReceiveLogicEvent, which sets the value LogicPorts.GetInputValue
    // answers and fires the LogicValueChanged event each building's handler listens for. So
    // every building, a modded one too, reacts with its own code. A wire wins while attached:
    // a port with a wire network is never written here, and once the wire is gone the chip's
    // value is delivered again.
    //
    // Some buildings also ask whether a wire is attached, and with none fall back to a
    // default (a logic valve shut, a checkpoint open). LogicPorts.IsPortConnected answers yes
    // for a driven port; the classes that look up the network themselves are patched below,
    // one each. Not covered: Teleporter's pad ports (it reads the networks' values directly).
    //
    // The value is saved with the building, as a Stationeers device keeps a written setting.
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class Ic10PortDrive : KMonoBehaviour
    {
        // In step: the port's HashedString hash and the value a chip last wrote to it.
        [Serialize]
        private List<int> _ports = new List<int>();

        [Serialize]
        private List<int> _values = new List<int>();

        [MyCmpGet]
        private LogicPorts _logic;

        private static readonly HashSet<Ic10PortDrive> Driving = new HashSet<Ic10PortDrive>();
        private static readonly List<Ic10PortDrive> Snapshot = new List<Ic10PortDrive>();

        // Deliver even a value the handler already holds: a handler starts at 0 whatever the
        // building takes an unwired port to mean, so the first delivery after a port's first
        // write or after a load must fire the event for the building to look again. Only
        // those: a chip loop writing the same value every tick must not re-fire the event
        // (ObjectDispenser, RocketControlStation and Checkpoint do work on each one).
        private bool _force = true;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            if (_ports.Count > 0)
            {
                Driving.Add(this);
            }
        }

        protected override void OnCleanUp()
        {
            Driving.Remove(this);
            base.OnCleanUp();
        }

        // What a chip writes: one bit on a plain port, four on a ribbon.
        public void Drive(HashedString port, double value)
        {
            int index = _ports.IndexOf(port.HashValue);
            int bits = Bits(port, value);
            if (index < 0)
            {
                _ports.Add(port.HashValue);
                _values.Add(bits);
                _force = true;
            }
            else
            {
                _values[index] = bits;
            }
            Driving.Add(this);
            Deliver();
        }

        // The value the chip holds this port at, while no wire is attached to it.
        public bool TryDriven(HashedString port, out int value)
        {
            value = 0;
            int index = _ports.IndexOf(port.HashValue);
            if (index < 0 || _logic == null || Wired(port))
            {
                return false;
            }
            value = _values[index];
            return true;
        }

        public static bool TryDriven(Component building, HashedString port, out int value)
        {
            value = 0;
            Ic10PortDrive drive = building == null ? null : building.GetComponent<Ic10PortDrive>();
            return drive != null && drive.TryDriven(port, out value);
        }

        // Turns on a building's "controlled by automation" status item, read from its private
        // static field; a missing field (a game update) only loses the text.
        public static void ShowStatus(Component building, System.Reflection.FieldInfo field)
        {
            StatusItem item = field == null ? null : field.GetValue(null) as StatusItem;
            KSelectable selectable = building.GetComponent<KSelectable>();
            if (item != null && selectable != null)
            {
                selectable.ToggleStatusItem(item, true, building);
            }
        }

        private bool Wired(HashedString port)
        {
            return Game.Instance.logicCircuitManager.GetNetworkForCell(_logic.GetPortCell(port)) != null;
        }

        private int Bits(HashedString port, double value)
        {
            LogicPorts.Port info;
            if (FindPort(port, out info) && info.spriteType == LogicPortSpriteType.RibbonInput)
            {
                return (int)System.Math.Round(System.Math.Max(0.0, System.Math.Min(15.0, value)));
            }
            return Ic10.Ic10MappedDevice.State(value) ? 1 : 0;
        }

        private bool FindPort(HashedString port, out LogicPorts.Port info)
        {
            info = default(LogicPorts.Port);
            if (_logic == null || _logic.inputPortInfo == null)
            {
                return false;
            }
            for (int i = 0; i < _logic.inputPortInfo.Length; i++)
            {
                if (_logic.inputPortInfo[i].id == port)
                {
                    info = _logic.inputPortInfo[i];
                    return true;
                }
            }
            return false;
        }

        // Each unwired driven port whose handler does not hold the chip's value gets it, as a
        // wire would deliver it. A wire's own value replaces it while the wire is there, and
        // when the wire goes the port is set back here on the next frame.
        private void Deliver()
        {
            if (_logic == null || _logic.inputPorts == null || _logic.inputPortInfo == null)
            {
                return;
            }
            bool force = _force;
            _force = false;
            for (int i = 0; i < _ports.Count; i++)
            {
                for (int p = 0; p < _logic.inputPortInfo.Length && p < _logic.inputPorts.Count; p++)
                {
                    HashedString id = _logic.inputPortInfo[p].id;
                    if (id.HashValue != _ports[i] || Wired(id))
                    {
                        continue;
                    }
                    // LogicEventHandler is internal to the game; ILogicEventReceiver is its
                    // public face, and GetInputValue reads its value.
                    ILogicEventReceiver handler = _logic.inputPorts[p] as ILogicEventReceiver;
                    if (handler != null && (force || _logic.GetInputValue(id) != _values[i]))
                    {
                        handler.ReceiveLogicEvent(_values[i]);
                    }
                }
            }
        }

        // Once a frame, from Game.Update, before the sim callbacks.
        public static void DeliverAll()
        {
            if (Driving.Count == 0)
            {
                return;
            }
            // A delivery runs the building's own handler, which may destroy the building and
            // so take it out of Driving; walk a copy.
            Snapshot.Clear();
            Snapshot.AddRange(Driving);
            for (int i = 0; i < Snapshot.Count; i++)
            {
                if (Snapshot[i] != null)
                {
                    Snapshot[i].Deliver();
                }
            }
            Snapshot.Clear();
        }
    }

    // Every finished building with an automation input can be driven.
    [HarmonyPatch(typeof(BuildingLoader), "CreateBuildingComplete")]
    internal static class Ic10PortDrivePrefabPatch
    {
        private static void Postfix(GameObject __result)
        {
            LogicPorts ports = __result == null ? null : __result.GetComponent<LogicPorts>();
            if (ports != null && ports.inputPortInfo != null && ports.inputPortInfo.Length > 0)
            {
                __result.AddOrGet<Ic10PortDrive>();
            }
        }
    }

    // A driven port counts as connected: Gantry and ObjectDispenser (IsAutomated), Reactor,
    // CreatureDeliveryPoint and FixedCapturePoint ask this, then read GetInputValue.
    [HarmonyPatch(typeof(LogicPorts), nameof(LogicPorts.IsPortConnected))]
    internal static class LogicPorts_IsPortConnected_Ic10
    {
        private static void Postfix(LogicPorts __instance, HashedString port_id, ref bool __result)
        {
            int value;
            if (!__result && Ic10PortDrive.TryDriven(__instance, port_id, out value))
            {
                __result = true;
            }
        }
    }

    // The standard on/off port: unwired it reads unNetworkedValue (1, or 0 on the logic
    // valves); driven it reads the chip's value.
    [HarmonyPatch(typeof(LogicOperationalController), "CheckWireState")]
    internal static class LogicOperationalController_CheckWireState_Ic10
    {
        private static void Postfix(LogicOperationalController __instance, LogicCircuitNetwork __result)
        {
            int value;
            if (__result == null && __instance.operational != null
                && Ic10PortDrive.TryDriven(__instance, LogicOperationalController.PORT_ID, out value))
            {
                __instance.operational.SetFlag(LogicOperationalController.LogicOperationalFlag,
                    LogicCircuitNetwork.IsBitActive(0, value));
            }
        }
    }

    // Vanilla shows "Enabled/Disabled by automation" only while a wire is attached; a driven
    // port shows it too, so a building a chip has stopped says why.
    [HarmonyPatch(typeof(LogicOperationalController), "OnLogicValueChanged")]
    internal static class LogicOperationalController_OnLogicValueChanged_Ic10
    {
        private static readonly System.Reflection.FieldInfo Info =
            AccessTools.Field(typeof(LogicOperationalController), "infoStatusItem");

        private static void Postfix(LogicOperationalController __instance, object data)
        {
            int value;
            if (((LogicValueChanged)data).portID == LogicOperationalController.PORT_ID
                && Ic10PortDrive.TryDriven(__instance, LogicOperationalController.PORT_ID, out value))
            {
                Ic10PortDrive.ShowStatus(__instance, Info);
            }
        }
    }

    // Checkpoint, the Radbolt Chamber and the Radbolt Reflector ignore their input unless
    // hasLogicWire, which they set from the network.
    [HarmonyPatch(typeof(Checkpoint), "OnLogicValueChanged")]
    internal static class Checkpoint_OnLogicValueChanged_Ic10
    {
        private static void Postfix(Checkpoint __instance)
        {
            int value;
            if (Ic10PortDrive.TryDriven(__instance, Checkpoint.PORT_ID, out value))
            {
                Traverse.Create(__instance).Field("hasLogicWire").SetValue(true);
            }
        }
    }

    [HarmonyPatch(typeof(HEPBattery.Instance), "OnLogicValueChanged")]
    internal static class HEPBattery_OnLogicValueChanged_Ic10
    {
        private static void Postfix(HEPBattery.Instance __instance)
        {
            int value;
            if (Ic10PortDrive.TryDriven(__instance.GetComponent<KPrefabID>(), HEPBattery.FIRE_PORT_ID, out value))
            {
                Traverse.Create(__instance).Field("hasLogicWire").SetValue(true);
            }
        }
    }

    [HarmonyPatch(typeof(HighEnergyParticleRedirector), "OnLogicValueChanged")]
    internal static class HighEnergyParticleRedirector_OnLogicValueChanged_Ic10
    {
        private static void Postfix(HighEnergyParticleRedirector __instance)
        {
            int value;
            if (Ic10PortDrive.TryDriven(__instance, HighEnergyParticleRedirector.PORT_ID, out value))
            {
                Traverse.Create(__instance).Field("hasLogicWire").SetValue(true);
            }
        }
    }

    // The Rail Gun reads its network's value, 1 with none.
    [HarmonyPatch(typeof(RailGun), "CheckLogicWireState")]
    internal static class RailGun_CheckLogicWireState_Ic10
    {
        private static void Postfix(RailGun __instance)
        {
            int value;
            if (!Ic10PortDrive.TryDriven(__instance, RailGun.PORT_ID, out value))
            {
                return;
            }
            Traverse rail = Traverse.Create(__instance);
            rail.Field("hasLogicWire").SetValue(true);
            rail.Field("isLogicActive").SetValue(LogicCircuitNetwork.IsBitActive(0, value));
            __instance.smi.sm.allowedFromLogic.Set(__instance.AllowLaunchingFromLogic, __instance.smi);
            Ic10PortDrive.ShowStatus(__instance, Info);
        }

        private static readonly System.Reflection.FieldInfo Info =
            AccessTools.Field(typeof(RailGun), "infoStatusItemLogic");
    }

    // So does the Rocket Control Station (green restricts use).
    [HarmonyPatch(typeof(RocketControlStation), nameof(RocketControlStation.OnLogicValueChanged))]
    internal static class RocketControlStation_OnLogicValueChanged_Ic10
    {
        private static void Postfix(RocketControlStation __instance, object data)
        {
            int value;
            if (((LogicValueChanged)data).portID == RocketControlStation.PORT_ID
                && Ic10PortDrive.TryDriven(__instance, RocketControlStation.PORT_ID, out value))
            {
                Traverse.Create(__instance).Field("m_logicUsageRestrictionState").SetValue(LogicCircuitNetwork.IsBitActive(0, value));
                __instance.Trigger(1861523068);
            }
        }
    }

    // Every reader of that state (BuildingRestrictionsActive, RocketUsageRestriction, the
    // side screen, the anim symbol) asks this first, and with no wire ignores the state.
    [HarmonyPatch(typeof(RocketControlStation), nameof(RocketControlStation.IsLogicInputConnected))]
    internal static class RocketControlStation_IsLogicInputConnected_Ic10
    {
        private static void Postfix(RocketControlStation __instance, ref bool __result)
        {
            int value;
            if (!__result && Ic10PortDrive.TryDriven(__instance, RocketControlStation.PORT_ID, out value))
            {
                __result = true;
            }
        }
    }

    // The launch pad launches on its trigger port only when it is connected.
    [HarmonyPatch(typeof(LaunchPad), nameof(LaunchPad.IsLogicInputConnected))]
    internal static class LaunchPad_IsLogicInputConnected_Ic10
    {
        private static void Postfix(LaunchPad __instance, ref bool __result)
        {
            int value;
            if (!__result && Ic10PortDrive.TryDriven(__instance, __instance.triggerPort, out value))
            {
                __result = true;
            }
        }
    }
}
