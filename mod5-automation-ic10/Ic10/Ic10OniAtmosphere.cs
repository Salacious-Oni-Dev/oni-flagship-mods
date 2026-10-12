using System;
using System.Collections.Generic;

namespace Ic10
{
    // The gas and liquid a device holds or watches: Stationeers' Atmosphere, as the adapter
    // needs it.
    //
    // In Stationeers three kinds of device answer Pressure, Temperature, TotalMoles and the
    // gas ratios, each about a different atmosphere:
    //   - any device with HasReadableAtmosphere, about what it holds (Device.GetLogicValue,
    //     Thing.InternalAtmosphere);
    //   - a gas sensor, about the air of the cell it stands in (GasSensor.GetLogicValue,
    //     WorldAtmosphere), which also answers VolumeOfLiquid;
    //   - a pipe analyser, about the pipe network it sits on (PipeAnalysizer.GetLogicValue,
    //     NetworkAtmosphere), which also answers Volume and VolumeOfLiquid, and -1 for
    //     Pressure, Temperature, TotalMoles and the two volumes while it has no network.
    // The game side says which of the three a building is and hands over the numbers; what
    // each logic type reads from them is decided here.
    public enum Ic10OniAtmosphereKind
    {
        Held = 0,
        World = 1,
        Pipe = 2,
    }

    public interface IIc10OniAtmosphere
    {
        // The species present, gas and liquid alike. ElementHash is Ic10Math.Hash of the
        // ONI element id, so HASH("Oxygen") names oxygen.
        int Count { get; }
        int ElementHash(int index);
        double Moles(int index);
        double Mass(int index);

        // Kelvin; 0 when there is nothing to have a temperature.
        double Temperature { get; }

        // Pascals, litres, litres. NaN when the number cannot be stated: ONI gives a
        // storage no volume, so what it holds has no pressure.
        double PressurePa { get; }
        double VolumeLitres { get; }
        double LiquidLitres { get; }
    }

    public static class Ic10OniAtmosphere
    {
        // GasMixtureHelper.GasRatio: the logic type names Stationeers gives its species, and
        // the ONI element id each one reads. A name whose element is in neither ONI nor a
        // loaded mod is still answered and reads 0, as Stationeers reads a species that is
        // not there. Mod 2 adds Nitrogen, LiquidNitrogen, Pollutant and LiquidPollutant
        // under these ids.
        //
        // Same id on both sides, except three: Stationeers' PollutedWater is ONI's
        // DirtyWater, its LiquidAlcohol is Ethanol, its LiquidSodiumChloride is MoltenSalt.
        public static readonly KeyValuePair<string, string>[] Ratios =
        {
            P("RatioOxygen", "Oxygen"),
            P("RatioCarbonDioxide", "CarbonDioxide"),
            P("RatioNitrogen", "Nitrogen"),
            P("RatioPollutant", "Pollutant"),
            P("RatioMethane", "Methane"),
            P("RatioWater", "Water"),
            P("RatioNitrousOxide", "NitrousOxide"),
            P("RatioLiquidNitrogen", "LiquidNitrogen"),
            P("RatioLiquidOxygen", "LiquidOxygen"),
            P("RatioLiquidMethane", "LiquidMethane"),
            P("RatioSteam", "Steam"),
            P("RatioLiquidCarbonDioxide", "LiquidCarbonDioxide"),
            P("RatioLiquidPollutant", "LiquidPollutant"),
            P("RatioLiquidNitrousOxide", "LiquidNitrousOxide"),
            P("RatioHydrogen", "Hydrogen"),
            P("RatioLiquidHydrogen", "LiquidHydrogen"),
            P("RatioPollutedWater", "DirtyWater"),
            P("RatioHydrazine", "Hydrazine"),
            P("RatioLiquidHydrazine", "LiquidHydrazine"),
            P("RatioLiquidAlcohol", "Ethanol"),
            P("RatioHelium", "Helium"),
            P("RatioLiquidSodiumChloride", "MoltenSalt"),
            P("RatioSilanol", "Silanol"),
            P("RatioLiquidSilanol", "LiquidSilanol"),
            P("RatioHydrochloricAcid", "HydrochloricAcid"),
            P("RatioLiquidHydrochloricAcid", "LiquidHydrochloricAcid"),
            P("RatioOzone", "Ozone"),
            P("RatioLiquidOzone", "LiquidOzone"),
        };

        private static KeyValuePair<string, string> P(string logicType, string element)
        {
            return new KeyValuePair<string, string>(logicType, element);
        }

        // Atmosphere.TotalMoles: gases and liquids together.
        public static double TotalMoles(IIc10OniAtmosphere atmosphere)
        {
            double total = 0.0;
            if (atmosphere != null)
            {
                int count = atmosphere.Count;
                for (int i = 0; i < count; i++)
                {
                    total += atmosphere.Moles(i);
                }
            }
            return total;
        }

        // GasMixtureHelper.GasRatio: the species' moles over the moles of every gas and
        // liquid, and 0 when there are none.
        public static double Ratio(IIc10OniAtmosphere atmosphere, int elementHash)
        {
            if (atmosphere == null)
            {
                return 0.0;
            }
            double total = 0.0;
            double species = 0.0;
            int count = atmosphere.Count;
            for (int i = 0; i < count; i++)
            {
                double moles = atmosphere.Moles(i);
                total += moles;
                if (atmosphere.ElementHash(i) == elementHash)
                {
                    species += moles;
                }
            }
            return total > 0.0 ? species / total : 0.0;
        }

        public static double MassOf(IIc10OniAtmosphere atmosphere, int elementHash)
        {
            double total = 0.0;
            if (atmosphere != null)
            {
                int count = atmosphere.Count;
                for (int i = 0; i < count; i++)
                {
                    if (atmosphere.ElementHash(i) == elementHash)
                    {
                        total += atmosphere.Mass(i);
                    }
                }
            }
            return total;
        }

        public static double TotalMass(IIc10OniAtmosphere atmosphere)
        {
            double total = 0.0;
            if (atmosphere != null)
            {
                int count = atmosphere.Count;
                for (int i = 0; i < count; i++)
                {
                    total += atmosphere.Mass(i);
                }
            }
            return total;
        }

        // What a building's Storage holds as an atmosphere: its gas and liquid items, with no
        // volume and so no pressure.
        public static IIc10OniAtmosphere Of(IIc10OniStorage storage)
        {
            return storage == null ? null : new Held(storage);
        }

        private sealed class Held : IIc10OniAtmosphere
        {
            private readonly IIc10OniStorage _storage;
            private readonly List<int> _fluid = new List<int>();

            public Held(IIc10OniStorage storage)
            {
                _storage = storage;
            }

            // The storage's items move about; the fluid ones are found again on each use.
            private List<int> Fluid()
            {
                _fluid.Clear();
                int count = _storage.Count;
                for (int i = 0; i < count; i++)
                {
                    if (_storage.IsFluid(i))
                    {
                        _fluid.Add(i);
                    }
                }
                return _fluid;
            }

            public int Count
            {
                get { return Fluid().Count; }
            }

            public int ElementHash(int index)
            {
                return _storage.ElementHash(Fluid()[index]);
            }

            public double Moles(int index)
            {
                return _storage.Moles(Fluid()[index]);
            }

            public double Mass(int index)
            {
                return _storage.Mass(Fluid()[index]);
            }

            public double Temperature
            {
                get { return Ic10OniBuilding.FluidTemperature(_storage); }
            }

            public double PressurePa
            {
                get { return double.NaN; }
            }

            public double VolumeLitres
            {
                get { return double.NaN; }
            }

            public double LiquidLitres
            {
                get { return double.NaN; }
            }
        }
    }
}
