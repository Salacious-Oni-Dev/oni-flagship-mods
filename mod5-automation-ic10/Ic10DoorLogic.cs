using HarmonyLib;
using Ic10;
using KSerialization;
using UnityEngine;

namespace Mod5AutomationIc10
{
    // A door's Stationeers button mode: Mode 0 Operate, 1 Logic, and the Setting a
    // press leaves behind.
    //
    // Stationeers' Door.InteractWith in ButtonMode.Logic does not open the door: a press sets
    // Setting = IsOpen ? 0 : 1 and a program decides. ONI has no hand press; a duplicant walking
    // up to a door is the press. DoorTransitionLayer.BeginTransition calls door.Open() on every
    // door in the step and holds the duplicant idle until each IsOpen(). On a Logic door in its
    // Auto state that call records the press and leaves the door shut, so the duplicant waits
    // at it until a program writes Open 1. A door that is open, or that a program holds open
    // (Opened), is walked through as ever and records no press, as a Stationeers player walks
    // through an open door without touching its button
    // (see Ic10DoorButton.Presses). A locked door is not walked through at all, and Stationeers'
    // press on a locked door fails.
    //
    // Operate (0, the default) is the vanilla door, untouched.
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class Ic10DoorLogic : KMonoBehaviour
    {
        [Serialize]
        private int mode;

        [Serialize]
        private double setting;

        private Door door;

        public int Mode => mode;
        public double Setting => setting;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            door = GetComponent<Door>();
        }

        public void SetMode(double value)
        {
            mode = Ic10DoorButton.ClampMode(value);
        }

        public void SetSetting(double value)
        {
            setting = Ic10DoorButton.ClampSetting(value);
        }

        // The rules are Ic10DoorButton's, which say where each comes from.
        public void OpenWritten(bool open)
        {
            setting = Ic10DoorButton.AfterOpenWritten(mode, setting, open);
        }

        internal void Press()
        {
            setting = Ic10DoorButton.Press(door != null && door.IsOpen());
        }

        // Set by the transition patch around BeginTransition, so a press is a duplicant's
        // Open() and never Door.ApplyRequestedControlState's own Open(); Close() pair.
        internal static bool InTransition;

        internal static readonly AccessTools.FieldRef<Door, int> OpenCount =
            AccessTools.FieldRefAccess<Door, int>("openCount");
    }

    // Every finished door but a sealed one. Not at BuildingLoader.CreateBuildingComplete, where
    // Ic10PortDrive attaches: the door configs add their Door in DoPostConfigureComplete, which
    // runs after it (otherwise no door has the component, and a program stops at its first
    // `s door Mode 1`). ConfigurePost runs once, after every
    // building's DoPostConfigureComplete.
    [HarmonyPatch(typeof(BuildingConfigManager), nameof(BuildingConfigManager.ConfigurePost))]
    internal static class Ic10DoorLogicPrefabPatch
    {
        private static void Postfix()
        {
            foreach (BuildingDef def in Assets.BuildingDefs)
            {
                GameObject complete = def == null ? null : def.BuildingComplete;
                Door door = complete == null ? null : complete.GetComponent<Door>();
                // The ruins' sealed doors open by their own unseal errand, not by a press.
                if (door != null && door.doorType != Door.DoorType.Sealed)
                {
                    complete.AddOrGet<Ic10DoorLogic>();
                }
            }
        }
    }

    [HarmonyPatch(typeof(DoorTransitionLayer), nameof(DoorTransitionLayer.BeginTransition))]
    internal static class DoorTransitionLayer_BeginTransition_Ic10
    {
        private static void Prefix()
        {
            Ic10DoorLogic.InTransition = true;
        }

        private static void Finalizer()
        {
            Ic10DoorLogic.InTransition = false;
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.Open))]
    internal static class Door_Open_Ic10
    {
        private static bool Prefix(Door __instance)
        {
            if (!Ic10DoorLogic.InTransition)
            {
                return true;
            }
            Ic10DoorLogic logic = __instance.GetComponent<Ic10DoorLogic>();
            bool open = __instance.IsOpen() || __instance.CurrentState == Door.ControlState.Opened;
            if (logic == null || !Ic10DoorButton.Presses(logic.Mode, open))
            {
                return true;
            }
            logic.Press();
            if (!Ic10DoorButton.HoldsShut(logic.Mode, __instance.CurrentState == Door.ControlState.Auto))
            {
                return true;
            }
            // Count the duplicant as vanilla's Open() does, so its Close() at EndTransition
            // balances, and leave isOpen alone.
            Ic10DoorLogic.OpenCount(__instance)++;
            return false;
        }
    }
}
