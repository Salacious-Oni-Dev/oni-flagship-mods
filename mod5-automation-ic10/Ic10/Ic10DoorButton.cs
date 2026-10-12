namespace Ic10
{
    // A Stationeers door's button mode, as rules with no game in them. The game half
    // is Ic10DoorLogic (the saved Mode and Setting, and the patch on a duplicant's door.Open()).
    public static class Ic10DoorButton
    {
        public const int Operate = 0;
        public const int Logic = 1;

        // Device.SetLogicValue clamps Mode to the device's modes; Door has two (ButtonMode).
        public static int ClampMode(double value)
        {
            return value >= 1.0 ? Logic : Operate;
        }

        // Door.SetLogicValue: Setting = value.Clamp(0, 1).
        public static double ClampSetting(double value)
        {
            return value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
        }

        // Door.InteractWith, ButtonMode.Logic: Setting = IsOpen ? 0 : 1.
        public static double Press(bool isOpen)
        {
            return isOpen ? 0.0 : 1.0;
        }

        // Door.OnInteractableUpdated: an Open interaction in Logic mode, a program's write
        // included, leaves Setting at the state written. In Operate it leaves Setting alone.
        public static double AfterOpenWritten(int mode, double setting, bool open)
        {
            return mode == Logic ? (open ? 1.0 : 0.0) : setting;
        }

        // What a duplicant's door.Open() does. A press is recorded on a shut Logic door only:
        // walking through a door that is already open, or held open by a program, presses
        // nothing, as a Stationeers player walking through an open door presses nothing. The
        // door is kept shut only when it is Logic and in its Auto state. A locked one is never
        // walked through. A walk through an open door is not a press: counted as one, it
        // would cycle an airlock behind every duplicant that left it, filling the chamber with the exterior door still open.
        public static bool Presses(int mode, bool open)
        {
            return mode == Logic && !open;
        }

        public static bool HoldsShut(int mode, bool autoState)
        {
            return mode == Logic && autoState;
        }
    }
}
