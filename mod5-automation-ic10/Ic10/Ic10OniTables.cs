using System.Collections.Generic;

namespace Ic10
{
    // Logic types ONI has and Stationeers does not. Written by hand, unlike the generated
    // Stationeers table beside it.
    //
    // A deliberate divergence from Stationeers. The rule it follows:
    //   - a Stationeers name is reused only when the meaning is the same;
    //   - an ONI-only value gets a new name and a number from FirstValue upward. Stationeers'
    //     highest is 358 and the enum is a ushort, so a number has to stay below 65536;
    //   - a number, once given, is never given to another name: scripts and saves hold them.
    public static class Ic10OniTables
    {
        public const int FirstValue = 1024;

        public static readonly KeyValuePair<string, int>[] LogicType =
        {
            // A sensor's live number, in the sensor's own unit.
            P("Reading", 1024),
            // The value on the building's first automation input and output port. Kept for
            // now, to be retired when every vanilla building has its full data output.
            // The numbers stay taken after that.
            P("LogicInput", 1025),
            P("LogicOutput", 1026),
            // The building's own temperature in kelvin. Stationeers' Temperature is the gas
            // and liquid a device holds, and keeps that meaning here.
            P("BodyTemperature", 1027),
            // 1 while a door stands open at this moment, whoever opened it. Stationeers'
            // Open is the state the door was asked for.
            P("OpenNow", 1028),
            // The watts this one machine is drawing at this moment. Stationeers' PowerActual
            // is a whole network's load and a plain machine refuses it;
            // RequiredPower is the rated draw, working or not.
            P("PowerDraw", 1029),
            // What a filter lets through or an element sensor looks for, as the hash of the
            // element or item id; 0 for none.
            P("Filter", 1030),
            // The two ends of an activation range, percent: where a smart battery or a
            // reservoir switches its signal on and where it switches it off.
            P("ActivateAt", 1031),
            P("DeactivateAt", 1032),
            // 1 while a planter's plant is harvested by a duplicant as soon as it is ripe,
            // ONI's "harvest when ready". 0 leaves the harvest to Harvest.
            P("AutoHarvest", 1033),
            // The rest are ONI-only readings.
            // A vent's state, ONI's Vent.State: 0 Invalid, 1 Ready, 2 Blocked, 3 OverPressure,
            // 4 Closed.
            P("VentState", 1034),
            // What a converter takes in and gives out, grams per second, averaged as its
            // status line is, summed over every element (ElementConverter).
            P("ConsumedRate", 1035),
            P("ProducedRate", 1036),
            // Uses an outhouse has left before it must be emptied.
            P("FlushesLeft", 1037),
            // The game clock: the cycle as the HUD numbers it (from 1), seconds into the
            // cycle (0 to 600), and 1 at night.
            P("Cycle", 1038),
            P("CycleTime", 1039),
            P("Night", 1040),
            // A Spaced Out rocket, read on any of its modules or the launch pad it stands on:
            // Clustercraft.CraftStatus (0 Grounded, 1 Launching, 2 InFlight, 3 Landing), fuel
            // and oxidizer left in kilograms, range in hexes, and 1 while every launch check
            // passes. Its time to destination is Stationeers' TimeToDestination.
            P("FlightState", 1041),
            P("FuelRemaining", 1042),
            P("OxidizerRemaining", 1043),
            P("RocketRange", 1044),
            P("ReadyToLaunch", 1045),
            // Settings a chip may write: a timer
            // sensor's green and red durations in seconds, and a cycle sensor's window, its
            // start and its length as fractions of a cycle.
            P("OnDuration", 1046),
            P("OffDuration", 1047),
            P("WindowStart", 1048),
            P("WindowLength", 1049),
        };

        private static KeyValuePair<string, int> P(string name, int value)
        {
            return new KeyValuePair<string, int>(name, value);
        }
    }
}
