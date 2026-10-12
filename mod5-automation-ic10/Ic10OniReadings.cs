using System;
using Ic10;
using UnityEngine;

namespace Mod5AutomationIc10
{
    // ONI-only readings: a vent's state, a converter's rates, an outhouse's flushes, the game
    // clock, a rocket.
    //
    // Run in the game for the vent, converter, toilet and clock readings, an empty rocket
    // platform, and a grounded CO2-engine rocket on the pad, read from the pad and from the
    // engine. Not run: a rocket in flight. Read from Vent.GetEndPointState, ElementConverter's
    // accumulators, Toilet.maxFlushes and FlushesUsed, GameClock, CraftModuleInterface,
    // Clustercraft and ClusterTraveler.TravelETA. The converter rates are measured averages,
    // not the configured rates.
    internal static class Ic10OniReadings
    {
        private const double GramsPerKilogram = 1000.0;

        public static void Fill(GameObject go, Ic10OniBuildingView view)
        {
            Vent vent = go.GetComponent<Vent>();
            if (vent != null)
            {
                view.VentState = () => (int)vent.GetEndPointState();
            }

            // A building may carry more than one converter (the algae terrarium's two); the
            // rates are summed, as the building's whole intake and output.
            ElementConverter[] converters = go.GetComponents<ElementConverter>();
            if (converters != null && converters.Length > 0)
            {
                view.ConsumedRate = () => ConsumedRate(converters) * GramsPerKilogram;
                view.ProducedRate = () => ProducedRate(converters) * GramsPerKilogram;
            }

            Toilet toilet = go.GetComponent<Toilet>();
            if (toilet != null)
            {
                view.FlushesLeft = () => toilet.maxFlushes - toilet.FlushesUsed;
            }

            LogicTimeOfDaySensor clock = go.GetComponent<LogicTimeOfDaySensor>();
            if (clock != null)
            {
                FillClock(view);
                // TimeRangeSideScreen's two sliders, 0 to 1 of a cycle; it sets the fields and
                // nothing else, and the sensor reads them on its next Sim200ms.
                view.WindowStart = new Ic10OniControl
                {
                    Get = () => clock.startTime,
                    Set = value => clock.startTime = (float)value,
                    Min = () => 0.0,
                    Max = () => 1.0,
                };
                view.WindowLength = new Ic10OniControl
                {
                    Get = () => clock.duration,
                    Set = value => clock.duration = (float)value,
                    Min = () => 0.0,
                    Max = () => 1.0,
                };
            }

            // TimerSideScreen: seconds from 0 to 600, or 0 to 10 cycles in its cycles mode,
            // so 6000 s is the most it can set; it sets the two fields and nothing else.
            LogicTimerSensor timer = go.GetComponent<LogicTimerSensor>();
            if (timer != null)
            {
                view.OnDuration = new Ic10OniControl
                {
                    Get = () => timer.onDuration,
                    Set = value => timer.onDuration = (float)value,
                    Min = () => 0.0,
                    Max = () => TimerMaxSeconds,
                };
                view.OffDuration = new Ic10OniControl
                {
                    Get = () => timer.offDuration,
                    Set = value => timer.offDuration = (float)value,
                    Min = () => 0.0,
                    Max = () => TimerMaxSeconds,
                };
            }

            RocketModuleCluster module = go.GetComponent<RocketModuleCluster>();
            if (module != null)
            {
                view.Rocket = Rocket(() => module.CraftInterface);
            }
            LaunchPad pad = go.GetComponent<LaunchPad>();
            if (pad != null && view.Rocket == null)
            {
                view.Rocket = Rocket(() =>
                {
                    RocketModuleCluster landed = pad.LandedRocket;
                    return landed != null ? landed.CraftInterface : null;
                });
            }
        }

        // TimerSideScreen.maxCycles (10) times a cycle's 600 seconds.
        private const double TimerMaxSeconds = 6000.0;

        public static void FillClock(Ic10OniBuildingView view)
        {
            view.Cycle = Cycle;
            view.CycleTime = CycleTime;
            view.Night = Night;
        }

        // GameClock.GetCycle is 0-based; the HUD and GameUtil show it plus one.
        public static double Cycle()
        {
            return GameClock.Instance != null ? GameClock.Instance.GetCycle() + 1 : 0.0;
        }

        public static double CycleTime()
        {
            return GameClock.Instance != null ? GameClock.Instance.GetTimeSinceStartOfCycle() : 0.0;
        }

        public static bool Night()
        {
            return GameClock.Instance != null && GameClock.Instance.IsNighttime();
        }

        private static double ConsumedRate(ElementConverter[] converters)
        {
            double sum = 0.0;
            foreach (ElementConverter converter in converters)
            {
                if (converter == null || converter.consumedElements == null)
                {
                    continue;
                }
                foreach (ElementConverter.ConsumedElement element in converter.consumedElements)
                {
                    sum += element.Rate;
                }
            }
            return sum;
        }

        private static double ProducedRate(ElementConverter[] converters)
        {
            double sum = 0.0;
            foreach (ElementConverter converter in converters)
            {
                if (converter == null || converter.outputElements == null)
                {
                    continue;
                }
                foreach (ElementConverter.OutputElement element in converter.outputElements)
                {
                    sum += element.Rate;
                }
            }
            return sum;
        }

        private static Ic10OniRocket Rocket(Func<CraftModuleInterface> craftInterface)
        {
            Func<Clustercraft> craft = () =>
            {
                CraftModuleInterface face = craftInterface();
                return face != null ? face.GetComponent<Clustercraft>() : null;
            };
            return new Ic10OniRocket
            {
                Present = () => craft() != null,
                FlightState = () => (int)craft().Status,
                TimeToDestination = () =>
                {
                    ClusterTraveler traveler = craft().GetComponent<ClusterTraveler>();
                    return traveler != null ? traveler.TravelETA() : 0.0;
                },
                FuelRemaining = () => craftInterface().FuelRemaining,
                OxidizerRemaining = () => craftInterface().OxidizerPowerRemaining,
                Range = () => craftInterface().RangeInTiles,
                ReadyToLaunch = () => craftInterface().CheckReadyToLaunch(),
            };
        }
    }
}
