using System.Collections.Generic;
using Ic10;
using OniFramework;
using UnityEngine;

namespace Mod5AutomationIc10
{
    // The air of a cell and the contents of a pipe network, handed to the adapter
    // (Ic10/Ic10OniAtmosphere.cs) through the framework's facades. Nothing here decides what a logic type means.
    //
    // Run in the game with the SDK's replacement SimDLL only (TotalMoles, Temperature, Pressure
    // from its gas law, pipe sensors). Without it, Pressure is not answered and the other
    // readings read the game's own cell; that is read from the code, not checked in the game.
    //
    // Both readings are taken once per frame and kept: a chip reads a sensor several times
    // in one tick (pressure, temperature, three ratios), and a pipe network reading walks
    // the whole network.
    internal static class Ic10OniAtmospheres
    {
        private static readonly Dictionary<int, int> ElementHashes = new Dictionary<int, int>();

        // Kelvin of a cell, whichever layer holds its air. 0 when it cannot be stated.
        public static double CellTemperature(int cell)
        {
            if (!GasMixtureFacade.IsRoomOwned(cell))
            {
                return Grid.Temperature[cell];
            }
            // GasMixtureFacade.TryGetTemperature: the temperature of a cell the sim holds as a
            // mixture, which the game's own grid cannot give (such a cell reads vacuum there).
            // False without the SDK's SimDLL.
            float kelvin;
            return GasMixtureFacade.TryGetTemperature(cell, out kelvin) ? kelvin : 0.0;
        }

        // Ic10Math.Hash of the element's id, by its index in ElementLoader.elements.
        public static int HashOf(int elementIdx)
        {
            int hash;
            if (!ElementHashes.TryGetValue(elementIdx, out hash))
            {
                List<Element> elements = ElementLoader.elements;
                hash = elementIdx >= 0 && elementIdx < elements.Count ? Ic10Math.Hash(elements[elementIdx].id.ToString()) : 0;
                ElementHashes[elementIdx] = hash;
            }
            return hash;
        }

        // Kilograms to moles, with the molar mass the framework's material table holds for
        // the element where it has one and ONI's own otherwise.
        public static double Moles(Element element, double massKg)
        {
            if (element == null)
            {
                return 0.0;
            }
            float molarMass = AtmosphereFacade.MolarMassGPerMol(element.idx);
            return molarMass > 0f ? massKg * 1000.0 / molarMass : 0.0;
        }

        // Which sensors watch what. ONI's atmo, hydro, thermo and element sensors stand in a
        // cell, as Stationeers' gas sensor does; its pipe sensors sit on a pipe, as the pipe
        // analyser does. A sensor on a conveyor rail watches no fluid.
        public static void Fill(GameObject go, Ic10OniBuildingView view)
        {
            ConduitSensor pipeSensor = go.GetComponent<ConduitSensor>();
            if (pipeSensor != null)
            {
                if (pipeSensor.conduitType == ConduitType.Gas || pipeSensor.conduitType == ConduitType.Liquid)
                {
                    PipeAir pipe = new PipeAir(() => Grid.PosToCell(go), pipeSensor.conduitType == ConduitType.Liquid
                        ? PipeContentType.Liquid : PipeContentType.Gas);
                    view.Atmosphere = pipe.Current;
                    view.AtmosphereKind = Ic10OniAtmosphereKind.Pipe;
                    view.NetworkFault = pipe.Fault;
                }
                return;
            }
            Ports(go, view);
            if (go.GetComponent<LogicPressureSensor>() != null || go.GetComponent<LogicTemperatureSensor>() != null
                || go.GetComponent<LogicElementSensor>() != null)
            {
                CellAir air = new CellAir(go);
                view.Atmosphere = air.Current;
                view.AtmosphereKind = Ic10OniAtmosphereKind.World;
            }
        }

        // The pipe at each gas or liquid port of a building: the def's own input
        // and output (BuildingDef.InputConduitType, Building.GetUtilityInputCell), and the
        // first second port of each side (ISecondaryInput, ISecondaryOutput: a filter's
        // filtered output, an overflow valve's overflow, a preferential flow valve's second
        // input). A building with several second ports (rail gun, warp conduits, habitat
        // modules) gives the first, gas before liquid.
        private static void Ports(GameObject go, Ic10OniBuildingView view)
        {
            Building building = go.GetComponent<Building>();
            if (building == null || building.Def == null)
            {
                return;
            }
            BuildingDef def = building.Def;
            if (IsFluid(def.InputConduitType))
            {
                view.PipeInput = new PipeAir(building.GetUtilityInputCell, Content(def.InputConduitType)).Current;
            }
            if (IsFluid(def.OutputConduitType))
            {
                view.PipeOutput = new PipeAir(building.GetUtilityOutputCell, Content(def.OutputConduitType)).Current;
            }
            foreach (ISecondaryInput second in go.GetComponents<ISecondaryInput>())
            {
                ConduitType type;
                if (view.PipeInput2 == null && Second(second.HasSecondaryConduitType, out type))
                {
                    CellOffset offset = second.GetSecondaryConduitOffset(type);
                    view.PipeInput2 = new PipeAir(() => PortCell(building, offset), Content(type)).Current;
                }
            }
            foreach (ISecondaryOutput second in go.GetComponents<ISecondaryOutput>())
            {
                ConduitType type;
                if (view.PipeOutput2 == null && Second(second.HasSecondaryConduitType, out type))
                {
                    CellOffset offset = second.GetSecondaryConduitOffset(type);
                    view.PipeOutput2 = new PipeAir(() => PortCell(building, offset), Content(type)).Current;
                }
            }
        }

        private static bool IsFluid(ConduitType type)
        {
            return type == ConduitType.Gas || type == ConduitType.Liquid;
        }

        private static PipeContentType Content(ConduitType type)
        {
            return type == ConduitType.Liquid ? PipeContentType.Liquid : PipeContentType.Gas;
        }

        private static bool Second(System.Func<ConduitType, bool> has, out ConduitType type)
        {
            type = has(ConduitType.Gas) ? ConduitType.Gas : has(ConduitType.Liquid) ? ConduitType.Liquid : ConduitType.None;
            return type != ConduitType.None;
        }

        // Building.GetUtilityInputCell's arithmetic, for an offset of any port.
        private static int PortCell(Building building, CellOffset offset)
        {
            return Grid.OffsetCell(building.GetBottomLeftCell(), building.GetRotatedOffset(offset));
        }

        // The cell a sensor stands in. A cell the custom SimDLL holds as a mixture gives its
        // species; any other cell gives its one element, gas or liquid. A solid cell and a
        // vacuum give nothing.
        private sealed class CellAir : IIc10OniAtmosphere
        {
            private readonly GameObject _go;
            private readonly ushort[] _species = new ushort[GasMixtureFacade.MaxSpeciesPerCell];
            private readonly float[] _mass = new float[GasMixtureFacade.MaxSpeciesPerCell];
            private int _count;
            private double _temperature;
            private double _pressurePa;
            private double _liquidLitres;
            private int _frame = -1;

            public CellAir(GameObject go)
            {
                _go = go;
            }

            public IIc10OniAtmosphere Current()
            {
                return this;
            }

            private void Refresh()
            {
                if (_frame == Time.frameCount)
                {
                    return;
                }
                _frame = Time.frameCount;
                _count = 0;
                _temperature = 0.0;
                _pressurePa = 0.0;
                _liquidLitres = 0.0;
                int cell = Grid.PosToCell(_go);
                if (!Grid.IsValidCell(cell))
                {
                    return;
                }
                bool roomOwned = GasMixtureFacade.IsRoomOwned(cell);
                if (roomOwned)
                {
                    _count = GasMixtureFacade.ReadComposition(cell, _species, _mass);
                }
                else
                {
                    Element element = Grid.Element[cell];
                    if (element != null && (element.IsGas || element.IsLiquid) && Grid.Mass[cell] > 0f)
                    {
                        _species[0] = element.idx;
                        _mass[0] = Grid.Mass[cell];
                        _count = 1;
                        if (element.IsLiquid)
                        {
                            float density;
                            if (!MaterialPropertyRegistry.TryGetLiquidDensityKgPerM3(element.id, out density) || density <= 0f)
                            {
                                density = PipeNetworkFacade.FallbackLiquidDensityKgM3;
                            }
                            _liquidLitres = Grid.Mass[cell] / density * 1000.0;
                        }
                    }
                }
                if (_count > 0)
                {
                    // Not Grid.Temperature outright: a mixture cell reads vacuum there
                    // (found by reading).
                    _temperature = CellTemperature(cell);
                }
                // Pascals from the sim's own gas law. On a SimDLL without it the facade
                // answers 0 and switches itself off; the pressure then cannot be stated.
                float pascals = PipeNetworkFacade.AmbientPressurePa(cell);
                _pressurePa = GasMixtureFacade.Available ? pascals : double.NaN;
            }

            public int Count
            {
                get { Refresh(); return _count; }
            }

            public int ElementHash(int index)
            {
                Refresh();
                return HashOf(_species[index]);
            }

            public double Moles(int index)
            {
                Refresh();
                return PipeNetworkFacade.MolesOf(_species[index], _mass[index]);
            }

            public double Mass(int index)
            {
                Refresh();
                return _mass[index];
            }

            public double Temperature
            {
                get { Refresh(); return _temperature; }
            }

            public double PressurePa
            {
                get { Refresh(); return _pressurePa; }
            }

            public double VolumeLitres
            {
                get { return double.NaN; }
            }

            public double LiquidLitres
            {
                get { Refresh(); return _liquidLitres; }
            }
        }

        // The pipe network under a pipe sensor: PipeNetworkFacade.TryGetNetworkState, which
        // sums every pipe cell joined to this one.
        private sealed class PipeAir : IIc10OniAtmosphere
        {
            private readonly System.Func<int> _cell;
            private readonly PipeContentType _type;
            private PipeNetworkState _state;
            private bool _fault;
            private int _frame = -1;

            public PipeAir(System.Func<int> cell, PipeContentType type)
            {
                _cell = cell;
                _type = type;
            }

            // Null while there is no pipe under the sensor.
            public IIc10OniAtmosphere Current()
            {
                Refresh();
                return _state == null ? null : this;
            }

            public bool Fault()
            {
                Refresh();
                return _fault;
            }

            private void Refresh()
            {
                if (_frame == Time.frameCount)
                {
                    return;
                }
                _frame = Time.frameCount;
                _fault = false;
                PipeNetworkState state;
                _state = PipeNetworkFacade.TryGetNetworkState(_cell(), _type, out state) ? state : null;
                if (_state != null)
                {
                    PipeStressReport report;
                    _fault = PipeNetworkFacade.TryEvaluateStress(_state, out report) && report.Level != PipeStressLevel.None;
                }
            }

            public int Count
            {
                get { return _state.Contents.Length; }
            }

            public int ElementHash(int index)
            {
                return HashOf(_state.Contents[index].ElementIdx);
            }

            public double Moles(int index)
            {
                return _state.Contents[index].Moles;
            }

            public double Mass(int index)
            {
                return _state.Contents[index].MassKg;
            }

            public double Temperature
            {
                get { return _state.TemperatureK; }
            }

            public double PressurePa
            {
                get { return GasMixtureFacade.Available ? _state.PressurePa : double.NaN; }
            }

            public double VolumeLitres
            {
                get { return _state.VolumeLitres; }
            }

            public double LiquidLitres
            {
                get { return _state.LiquidVolumeLitres; }
            }
        }
    }
}
