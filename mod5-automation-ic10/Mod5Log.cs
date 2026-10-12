using UnityEngine;

namespace Mod5AutomationIc10
{
    // Mod 5's log lines. Never Debug.LogError: ONI's KCrashReporter treats LogType.Error as a
    // crash (see mod1-thermo-fluid/Mod1Log.cs, and the guard in build.sh that fails the build
    // on one). An error is written at warning level with ERROR in the text.
    internal static class Mod5Log
    {
        public const string Prefix = "[Mod5AutomationIc10] ";

        public static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message)
        {
            Debug.LogWarning(Prefix + "ERROR " + message);
        }
    }
}
