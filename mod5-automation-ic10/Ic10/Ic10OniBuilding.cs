using System;
using System.Collections.Generic;

namespace Ic10
{
    // The generic adapter of the device model: any ONI building as an IC10 device,
    // from the components it happens to have.
    //
    // It is written with no game in it, so the mapping can be tested offline;
    // nothing in ONI uses it yet, and nothing here has run against ONI's components.
    //
    // This assembly references no game code, so a building arrives as a view: one delegate
    // per thing a vanilla component can answer, null when the building lacks the component.
    // The game side fills a view in a line per component; every decision about what a logic
    // type means is here.

    // A setting with a range: ISliderControl, IThresholdSwitch.Threshold,
    // IUserControlledCapacity.UserMaxCapacity.
    public sealed class Ic10OniControl
    {
        public Func<double> Get;
        public Action<double> Set;
        public Func<double> Min;
        public Func<double> Max;

        // The decimal places the side screen keeps (SliderSet.SetValue rounds to them); a
        // written number is rounded the same way. Null: kept as written.
        public Func<int> Decimals;
    }

    // Mod 5's own logic bricks, each the ONI form of a Stationeers logic unit. The building
    // keeps the state and shows it to the player; what a chip may read and write, and how a
    // written number is clamped, is in the adapter.
    public enum Ic10OniBrickKind
    {
        // LogicMemory: a number a chip or a player stores.
        Memory = 0,
        // LogicDial: a whole number from 0 to Mode, turned by the player or written.
        Dial = 1,
        // LogicSwitch: a lever, 0 or 1, thrown by the player or through Open.
        Switch = 2,
        // LogicButton: 1 for a moment after it is pressed, by the player or through Activate.
        Button = 3,
        // LogicDisplay: shows the number written to it, in the form Mode chooses.
        Display = 4,
    }

    public sealed class Ic10OniBrick
    {
        public Ic10OniBrickKind Kind;
        public Func<double> GetSetting;
        public Action<double> SetSetting;

        // Dial: the top of its range. Display: LogicDisplay.DisplayMode.
        public Func<int> GetMode;
        public Action<int> SetMode;

        // Button: press it.
        public Action Press;

        // Display: the colour its text is drawn in, a ColorType number (Color.Blue 0 to
        // Color.Purple 11). Null on the other bricks.
        public Func<int> GetColor;
        public Action<int> SetColor;
    }

    // Door.ControlState, same order and numbers.
    public enum Ic10OniDoorState
    {
        Auto = 0,
        Opened = 1,
        Locked = 2,
    }

    // A Storage's items, by position. The hashes are Ic10Math.Hash of the item's prefab id
    // and of its element id, so HASH("Water") in a script finds water.
    public interface IIc10OniStorage
    {
        int Count { get; }
        int PrefabHash(int index);
        int ElementHash(int index);
        double Mass(int index);
        double Temperature(int index);

        // True for a gas or a liquid, and the item's heat capacity in joules per kelvin
        // (mass times specific heat). Both are used only for the device's Temperature.
        bool IsFluid(int index);
        double HeatCapacity(int index);

        // Moles of the item: its mass over its element's molar mass. Used for TotalMoles
        // and the gas ratios of what the building holds.
        double Moles(int index);
    }

    public sealed class Ic10OniBuildingView
    {
        // KPrefabID.PrefabTag.Name, the building's displayed name, KPrefabID.InstanceID.
        public string PrefabId;
        public Func<string> DisplayName;
        public int InstanceId;

        // BuildingEnabledButton.IsEnabled.
        public Func<bool> GetEnabled;
        public Action<bool> SetEnabled;

        // EnergyConsumer.IsPowered; Operational.IsFunctional and IsActive.
        public Func<bool> IsPowered;
        public Func<bool> IsFunctional;
        public Func<bool> IsActive;

        // PrimaryElement.Temperature, kelvin: the building itself, not what it holds.
        public Func<double> BodyTemperature;

        // The first of these the building has is its Setting. Slider is any one number with
        // a range that the building's side screen sets: an ISliderControl, a valve's flow in
        // grams per second, a limit valve's limit.
        public Ic10OniControl Slider;
        public Ic10OniControl Threshold;
        public Ic10OniControl Capacity;

        // A count the building keeps and the player can reset: a limit valve's amount passed
        // (LimitValve.Amount). Stationeers' ChuteDigitalValve.Quantity, read and written.
        public Ic10OniControl Counter;

        // The one thing a filter lets through or a sensor looks for (Filterable.SelectedTag),
        // as Ic10Math.Hash of its id; 0 for none. The setter is given a hash and does
        // nothing when no option of the building hashes to it.
        public Func<int> GetFilter;
        public Action<int> SetFilter;

        // The two ends of an activation range (IActivationRangeTarget: a smart battery, a
        // reservoir), in the building's own unit, percent.
        public Ic10OniControl ActivateAt;
        public Ic10OniControl DeactivateAt;

        // How far along the work in hand is, 0 to 1, on a building that is not a machine
        // with recipes: a research station's current research.
        public Func<double> Progress;

        // The pipe network at each of the building's pipe ports, gas or liquid; null for a
        // port the building has not got, and the function answers null while no pipe is
        // there. Stationeers' InputNetwork, InputNetwork2, OutputNetwork, OutputNetwork2.
        public Func<IIc10OniAtmosphere> PipeInput;
        public Func<IIc10OniAtmosphere> PipeInput2;
        public Func<IIc10OniAtmosphere> PipeOutput;
        public Func<IIc10OniAtmosphere> PipeOutput2;

        // One of a few named settings the side screen offers: an ice maker's product, a
        // radbolt generator's direction, a checkbox. Stationeers' Device.Mode.
        public Ic10OniChoice Choice;

        // A machine that makes things from recipes (ComplexFabricator), and a planter's plot
        // (PlantablePlot).
        public Ic10OniFabricator Fabricator;
        public Ic10OniPlot Plot;

        // Mod 5's IC Sorter: Stationeers' LogicSorter on a conveyor rail.
        public Ic10Sorter Sorter;

        // ONI-only readings. VentState is
        // Vent.State; the two rates are grams per second; FlushesLeft is an outhouse's.
        public Func<int> VentState;
        public Func<double> ConsumedRate;
        public Func<double> ProducedRate;
        public Func<int> FlushesLeft;

        // The game clock, on a cycle sensor: GameClock's cycle as the HUD numbers it,
        // seconds into the cycle, and night.
        public Func<double> Cycle;
        public Func<double> CycleTime;
        public Func<bool> Night;

        // Read and written, each in its side screen's own range: a timer
        // sensor's on and off durations in seconds (LogicTimerSensor.onDuration, offDuration),
        // a cycle sensor's window as fractions of a cycle (LogicTimeOfDaySensor.startTime,
        // duration).
        public Ic10OniControl OnDuration;
        public Ic10OniControl OffDuration;
        public Ic10OniControl WindowStart;
        public Ic10OniControl WindowLength;

        public Ic10OniRocket Rocket;

        // A bin's or a fridge's item checklist, set through the sorter's memory.
        public Ic10Checklist Checklist;

        // IThresholdSwitch.ActivateAboveThreshold and CurrentValue.
        public Func<bool> GetActivateAbove;
        public Action<bool> SetActivateAbove;
        public Func<double> Reading;

        // True on a temperature sensor: its Reading is a temperature in kelvin, and
        // Temperature answers with it, as a Stationeers gas sensor's does.
        public bool ReadingIsTemperature;

        // Battery, Generator, EnergyConsumer; joules and watts. The adapter turns the joules
        // into Stationeers' unit (Ic10OniBuilding.PowerTickSeconds).
        public Func<double> Charge;
        // Battery.Capacity, joules.
        public Func<double> ChargeMaximum;
        // Watts the generator is making now, not what it is rated for: a solar panel or a
        // steam turbine makes less than its rating most of the time.
        public Func<double> PowerGeneration;

        // A solar panel, watts: what it is making now, and what the light on it could make
        // now (SolarPanel.GenerationRate and PowerGenerated()). SolarPotential may be null
        // when the game side cannot read the light; Maximum then answers with SolarOutput.
        public Func<double> SolarOutput;
        public Func<double> SolarPotential;

        // A power transformer: the most it can pass, watts (Transformer.OutputMaximum). Its
        // output cap, when it has one, is its Slider. A
        // building with this set is a transformer and nothing else, whatever components
        // ONI builds one from (a Battery and a Generator).
        public Func<double> TransformerRating;

        // The power circuit a battery or a wattage sensor is on, in watts: what its
        // consumers drew last tick, what its generators are putting out, and what its
        // consumers are rated to draw. Null on every other building.
        public Func<double> CircuitActual;
        public Func<double> CircuitPotential;
        public Func<double> CircuitRequired;

        // EnergyConsumer.WattsNeededWhenActive, the draw the building is rated for, and
        // EnergyConsumer.IsConnected, whether a wire reaches it.
        public Func<double> RatedPower;
        public Func<bool> IsWired;

        // EnergyConsumer.WattsUsed: the watts the building is drawing at this moment, 0
        // while it is not working.
        public Func<double> PowerDraw;

        // IUserControlledCapacity.AmountStored, kilograms.
        public Func<double> AmountStored;

        // The building's Storage, null when it has none. A building with one is a building
        // that holds fluid (Stationeers' HasReadableAtmosphere): its Temperature
        // is the temperature of the gas and liquid stored. A pipe connection alone does not
        // make a building one; a building that ought to hold fluid is given a Storage.
        public IIc10OniStorage Storage;

        // Set on one of Mod 5's logic bricks, null on every other building.
        public Ic10OniBrick Brick;

        // The air or the pipe a building watches, and which of the two it is (World for a
        // sensor standing in a cell, Pipe for one sitting on a pipe). Null on a building
        // that watches neither; a building with a Storage then answers for what it holds.
        // The function returns null while there is nothing to watch: a pipe sensor with no
        // pipe under it. NetworkFault is the pipe network's stress, 1 when it is at risk.
        public Func<IIc10OniAtmosphere> Atmosphere;
        public Ic10OniAtmosphereKind AtmosphereKind;
        public Func<bool> NetworkFault;

        // A door: the control state the door has been asked for, Door.RequestedState. The
        // requested one, not the current one, so that two writes in one tick see each
        // other. The setter must change the state the way an automation signal does
        // (Door.OnLogicValueChanged), with no duplicant errand; Door.QueueStateChange is
        // the player's button and queues one. DoorIsOpen is Door.IsOpen(): open at this
        // moment, whoever opened it.
        public Func<bool> DoorIsOpen;
        public Func<Ic10OniDoorState> GetDoorState;
        public Action<Ic10OniDoorState> SetDoorState;

        // A door's Stationeers button mode: Mode 0 Operate, 1 Logic, and the
        // Setting a duplicant's press leaves (Setting = IsOpen ? 0 : 1). The setters clamp as
        // Door.SetLogicValue does. DoorOpenWritten is told every Open the program writes, so
        // that in Logic mode Setting follows it (Door.OnInteractableUpdated).
        public Func<double> GetDoorMode;
        public Action<double> SetDoorMode;
        public Func<double> GetDoorSetting;
        public Action<double> SetDoorSetting;
        public Action<bool> DoorOpenWritten;

        // LogicPorts.GetInputValue and GetOutputValue on the building's first port of each.
        // Kept for now; to be retired once every vanilla building has its full data output.
        public Func<double> LogicInput;
        public Func<double> LogicOutput;

        // Drives the first automation input from a chip, as a wire would while none is
        // attached. Null with no input port.
        public Action<double> SetLogicInput;

        // The data networks under the building, for `d0:0` (Device.GetNetwork). Null until
        // the data cable exists: the operand then finds nothing, as it does in Stationeers
        // for a device with no cable.
        public Func<IList<Ic10Network>> Networks;

        // What other mods declared for this building through the framework's data-device
        // registry (OniFramework.DataDevices), by logic-type name, in order. Applied
        // after everything above, so a later entry replaces an earlier one. Null for none.
        public List<Ic10OniDeclared> Declared;
    }

    // One entry of OniFramework.DataDeviceMap, copied so the core needs no framework
    // reference. Hidden takes the type away, read and write.
    public sealed class Ic10OniDeclared
    {
        public string LogicType;
        public Func<double> Read;
        public Func<bool> Readable;
        public Action<double> Write;
        public bool Hidden;
    }

    // ONI's ComplexFabricator as Stationeers' SimpleFabricatorBase sees a machine: a list
    // of recipes, one of them selected, and a run flag. ONI keeps a count on every recipe at
    // once instead (ComplexFabricator.recipeQueueCounts), so the selected recipe is a number
    // the game side keeps and saves for the chip, and running is that recipe's count.
    public sealed class Ic10OniFabricator
    {
        // ComplexFabricator.QUEUE_INFINITE.
        public const int Forever = -1;

        // The recipes the machine offers, in its own order (ComplexFabricator.GetRecipes).
        public Func<int> Count;

        // Ic10Math.Hash of the id of the recipe's first product, and of the recipe's own
        // id. 0 for a recipe the player cannot order yet.
        public Func<int, int> ProductHash;
        public Func<int, int> RecipeIdHash;

        // The recipe's order count: 0 none, Forever, or 1 to 99.
        public Func<int, int> GetQueue;
        public Action<int, int> SetQueue;

        // The index of the recipe being made at this moment, -1 for none, and how far along
        // that one item is, 0 to 1 (CurrentWorkingOrder, OrderProgress).
        public Func<int> Working;
        public Func<double> Progress;

        // The index of the selected recipe. Kept by the game side, saved with the building.
        public Func<int> GetSelected;
        public Action<int> SetSelected;

        // How many items of that recipe the machine has finished since the game started
        // (the game side counts ComplexFabricator's order-complete event). Null: not counted.
        public Func<int, int> Made;

        // The machine's instruction stack, if it has one: `put`, `get`, `clr`.
        public Ic10PrinterStack Stack;
    }

    // A setting that is one of Count named options, numbered from 0 in the side screen's
    // own order.
    public sealed class Ic10OniChoice
    {
        public Func<int> Get;
        public Action<int> Set;
        public Func<int> Count;
    }

    // The plant in a planter. Null delegates are never called while HasPlant is false.
    public sealed class Ic10OniPlot
    {
        public Func<bool> HasPlant;

        // Ic10Math.Hash of the plant's prefab id.
        public Func<int> PlantHash;

        // Growing.PercentGrown, 0 to 1; Harvestable.CanBeHarvested; WiltCondition.IsWilting.
        public Func<double> Grown;
        public Func<bool> Harvestable;
        public Func<bool> Wilting;

        // HarvestDesignatable.MarkForHarvest: a duplicant errand, and only for a plant that
        // can be harvested.
        public Action Harvest;

        // HarvestDesignatable.HarvestWhenReady and SetHarvestWhenReady.
        public Func<bool> GetAutoHarvest;
        public Action<bool> SetAutoHarvest;
    }

    // A Spaced Out rocket as one of its modules or its launch pad sees it. Every
    // function answers for the rocket there now; Present is false while there is none (an
    // empty pad), and every reading is then 0.
    public sealed class Ic10OniRocket
    {
        public Func<bool> Present;
        // Clustercraft.CraftStatus: 0 Grounded, 1 Launching, 2 InFlight, 3 Landing.
        public Func<int> FlightState;
        // ClusterTraveler.TravelETA, seconds of game time; 0 while not travelling.
        public Func<double> TimeToDestination;
        // CraftModuleInterface.FuelRemaining and OxidizerPowerRemaining, kilograms.
        public Func<double> FuelRemaining;
        public Func<double> OxidizerRemaining;
        // CraftModuleInterface.RangeInTiles, hexes.
        public Func<double> Range;
        // CraftModuleInterface.CheckReadyToLaunch.
        public Func<bool> ReadyToLaunch;
    }

    public class Ic10OniBuilding : Ic10MappedDevice, IIc10Connected
    {
        // Stationeers' power tick, in seconds (GameManager.DefaultTickSpeedMs = 500). Its
        // stored energy is counted in watts times ticks, so joules divide by this.
        public const double PowerTickSeconds = 0.5;

        // the Stationeers name that drives a building's automation input, where
        // Stationeers has one. A logic valve flows while On, as Stationeers' Valve does
        // (OnOff); the Automated Notifier fires as On rises; the Object Dispenser opens.
        // Every other input is driven through LogicInput.
        public static readonly Dictionary<string, string> PortNames = new Dictionary<string, string>
        {
            { "GasLogicValve", "On" },
            { "LiquidLogicValve", "On" },
            { "SolidLogicValve", "On" },
            { "LogicAlarm", "On" },
            { "ObjectDispenser", "Open" },
        };

        private readonly Ic10OniBuildingView _view;
        private readonly int _prefabHash;
        private string _hashedName;
        private int _nameHash;

        // A building with a Storage also answers slots and `lr`; one that watches air or a
        // pipe answers `lr` for what is there; any other must not, or `lr` would read 0
        // where Stationeers reports IncorrectReagentDevice.
        public static Ic10OniBuilding Create(Ic10OniBuildingView view, Ic10EnumSet enums)
        {
            Ic10OniBuilding building;
            if (view.Sorter != null)
            {
                building = new Ic10OniSorterBuilding(view, enums);
            }
            else if (view.Fabricator != null && view.Fabricator.Stack != null)
            {
                building = new Ic10OniFabricatorBuilding(view, enums);
            }
            else if (view.Checklist != null)
            {
                building = new Ic10OniChecklistBuilding(view, enums);
            }
            else if (view.Storage != null || view.Plot != null)
            {
                building = new Ic10OniStorageBuilding(view, enums);
            }
            else
            {
                building = view.Atmosphere != null ? new Ic10OniAtmosphereBuilding(view, enums) : new Ic10OniBuilding(view, enums);
            }
            // Last, after every subclass has mapped its own: what other mods declared.
            building.ApplyDeclared(view.Declared, enums.LogicType);
            return building;
        }

        // Names in Declared that the enum table does not have; they were skipped.
        public IList<string> UnknownDeclared { get; private set; }

        private void ApplyDeclared(List<Ic10OniDeclared> declared, Ic10EnumTable t)
        {
            if (declared == null)
            {
                return;
            }
            foreach (Ic10OniDeclared entry in declared)
            {
                int logicType = entry == null || string.IsNullOrEmpty(entry.LogicType) ? -1 : t.ValueOf(entry.LogicType);
                if (logicType < 0)
                {
                    if (entry != null)
                    {
                        if (UnknownDeclared == null)
                        {
                            UnknownDeclared = new List<string>();
                        }
                        UnknownDeclared.Add(entry.LogicType);
                    }
                    continue;
                }
                if (entry.Hidden)
                {
                    Forget(logicType);
                    continue;
                }
                if (entry.Read != null)
                {
                    Read(logicType, entry.Read, entry.Readable);
                }
                if (entry.Write != null)
                {
                    Write(logicType, entry.Write);
                }
            }
        }

        public IIc10Device GetNetwork(int networkIndex)
        {
            return _view.Networks == null ? null : Ic10Network.Pick(_view.Networks(), networkIndex);
        }

        protected Ic10OniBuilding(Ic10OniBuildingView view, Ic10EnumSet enums)
        {
            _view = view;
            _prefabHash = Ic10Math.Hash(view.PrefabId ?? string.Empty);
            Ic10EnumTable t = enums.LogicType;

            // Device.CanLogicRead: these three are readable on every device.
            Read(t.ValueOf("PrefabHash"), () => PrefabHash);
            Read(t.ValueOf("NameHash"), () => NameHash);
            Read(t.ValueOf("ReferenceId"), () => view.InstanceId);

            if (view.GetEnabled != null)
            {
                Read(t.ValueOf("On"), () => view.GetEnabled() ? 1.0 : 0.0);
                if (view.SetEnabled != null)
                {
                    Write(t.ValueOf("On"), value => view.SetEnabled(State(value)));
                }
            }
            if (view.IsPowered != null)
            {
                Read(t.ValueOf("Power"), () => view.IsPowered() ? 1.0 : 0.0);
            }
            if (view.IsFunctional != null)
            {
                Read(t.ValueOf("Error"), () => view.IsFunctional() ? 0.0 : 1.0);
            }
            if (view.IsActive != null)
            {
                Read(t.ValueOf("Activate"), () => view.IsActive() ? 1.0 : 0.0);
            }

            // Device.CanLogicRead: Temperature is readable only with HasReadableAtmosphere,
            // and Device.GetLogicValue answers InternalAtmosphere.Temperature. A building
            // with no Storage has no Temperature; its own is BodyTemperature.
            if (view.ReadingIsTemperature && view.Reading != null)
            {
                Read(t.ValueOf("Temperature"), view.Reading);
            }
            else if (view.Storage != null)
            {
                Read(t.ValueOf("Temperature"), () => FluidTemperature(view.Storage));
            }
            Read(t.ValueOf("BodyTemperature"), view.BodyTemperature);
            MapAtmosphere(view, t);

            Ic10OniControl setting = view.Slider ?? view.Threshold ?? view.Capacity;
            if (setting != null && setting.Get != null)
            {
                // SingleSliderSideScreen.IsValidForTarget hides a slider whose minimum is its
                // maximum (a space heater that makes no heat): there is nothing to set, and a
                // chip cannot read it either.
                Func<bool> shown = null;
                if (setting.Min != null && setting.Max != null)
                {
                    shown = () => setting.Min() != setting.Max();
                }
                Read(t.ValueOf("Setting"), setting.Get, shown);
                if (setting.Set != null)
                {
                    Write(t.ValueOf("Setting"), value => WriteSetting(setting, value));
                }
                if (setting.Max != null)
                {
                    Read(t.ValueOf("Maximum"), setting.Max);
                    Read(t.ValueOf("Ratio"), () =>
                    {
                        double max = setting.Max();
                        return max == 0.0 ? 0.0 : setting.Get() / max;
                    });
                }
            }

            if (view.GetActivateAbove != null)
            {
                Read(t.ValueOf("Mode"), () => view.GetActivateAbove() ? 1.0 : 0.0);
                if (view.SetActivateAbove != null)
                {
                    // Device.SetLogicValue clamps Mode to the device's modes; there are two.
                    Write(t.ValueOf("Mode"), value => view.SetActivateAbove(State(value)));
                }
            }

            // Stationeers has two independent booleans, Open and Lock; an ONI door has one
            // state of three. Locked wins: a door that is locked stays locked
            // when Open 0 is written, so a script that writes both every tick does not
            // fight itself.
            //
            // Open reads the state asked for, not whether the door stands open at this
            // moment. It has to: the chip writes only when the value differs from what it
            // reads (as Stationeers' does), so a read that could disagree with the last
            // write would make the next write vanish, for instance Open 0 sent while the
            // door is still swinging open. An Auto door a duplicant is walking through
            // reads Open 0, and OpenNow 1.
            if (view.DoorIsOpen != null)
            {
                Read(t.ValueOf("OpenNow"), () => view.DoorIsOpen() ? 1.0 : 0.0);
            }
            if (view.GetDoorState != null)
            {
                Read(t.ValueOf("Open"), () => view.GetDoorState() == Ic10OniDoorState.Opened ? 1.0 : 0.0);
                Read(t.ValueOf("Lock"), () => view.GetDoorState() == Ic10OniDoorState.Locked ? 1.0 : 0.0);
                if (view.SetDoorState != null)
                {
                    Write(t.ValueOf("Open"), value =>
                    {
                        if (State(value))
                        {
                            view.SetDoorState(Ic10OniDoorState.Opened);
                        }
                        else if (view.GetDoorState() == Ic10OniDoorState.Opened)
                        {
                            view.SetDoorState(Ic10OniDoorState.Auto);
                        }
                        if (view.DoorOpenWritten != null)
                        {
                            view.DoorOpenWritten(State(value));
                        }
                    });
                    Write(t.ValueOf("Lock"), value =>
                    {
                        if (State(value))
                        {
                            view.SetDoorState(Ic10OniDoorState.Locked);
                        }
                        else if (view.GetDoorState() == Ic10OniDoorState.Locked)
                        {
                            view.SetDoorState(Ic10OniDoorState.Auto);
                        }
                    });
                }
            }

            // a door's button mode. Stationeers' Door answers Mode (ButtonMode,
            // Operate 0 / Logic 1) and Setting (what the last press left).
            if (view.GetDoorMode != null)
            {
                Read(t.ValueOf("Mode"), view.GetDoorMode);
                if (view.SetDoorMode != null)
                {
                    Write(t.ValueOf("Mode"), view.SetDoorMode);
                }
            }
            if (view.GetDoorSetting != null)
            {
                Read(t.ValueOf("Setting"), view.GetDoorSetting);
                if (view.SetDoorSetting != null)
                {
                    Write(t.ValueOf("Setting"), view.SetDoorSetting);
                }
            }

            // Battery.GetLogicValue: Charge is PowerStored, Maximum is PowerMaximum, Ratio
            // their quotient. Stationeers' stored energy is not in joules: PowerTick moves a
            // device's watts into or out of a battery once per power tick with no time
            // factor (PowerTick.ConsumePower, Battery.UsePower), so one unit of Charge is one
            // watt for one tick, half a joule. A script that divides Charge by a load gets
            // ticks, and it gets ticks here too.
            //
            // Two buildings are not what their ONI components say. A solar panel's Charge is
            // the watts it is making (SolarPanel.GetLogicValue: Charge is GenerationRate,
            // Maximum is PowerGenerated(), what the sun could give it now, and Ratio is
            // GenerationEfficiency, how well it is aimed). An ONI panel does not aim and
            // takes all the light that falls on it, so Ratio is 1. A transformer is built in
            // ONI from a Battery and a Generator, and in Stationeers answers neither one's
            // types: Transformer.GetLogicValue has Setting (the output cap), Maximum
            // (OutputMaximum) and Ratio (their quotient). The cap is the transformer's
            // slider (the game side's Ic10TransformerCap), which the
            // Setting rule above has already taken: written, and clamped to 0..Maximum as
            // Transformer.SetLogicValue clamps it. A transformer with no slider reads its
            // rating and cannot be written.
            bool transformer = view.TransformerRating != null;
            if (transformer)
            {
                if (setting == null || setting.Max == null)
                {
                    Func<double> rating = view.TransformerRating;
                    Read(t.ValueOf("Setting"), rating);
                    Read(t.ValueOf("Maximum"), rating);
                    Read(t.ValueOf("Ratio"), () => rating() == 0.0 ? 0.0 : 1.0);
                }
            }
            else if (view.SolarOutput != null)
            {
                Read(t.ValueOf("Charge"), view.SolarOutput);
                Read(t.ValueOf("Maximum"), view.SolarPotential ?? view.SolarOutput);
                Read(t.ValueOf("Ratio"), () => 1.0);
            }
            else if (view.Charge != null)
            {
                Func<double> joules = view.Charge;
                Read(t.ValueOf("Charge"), () => joules() / PowerTickSeconds);
                if (view.ChargeMaximum != null && (setting == null || setting.Max == null))
                {
                    Func<double> capacity = view.ChargeMaximum;
                    Read(t.ValueOf("Maximum"), () => capacity() / PowerTickSeconds);
                    Read(t.ValueOf("Ratio"), () =>
                    {
                        double max = capacity();
                        return max == 0.0 ? 0.0 : joules() / max;
                    });
                }
            }
            // PowerActual, PowerPotential and PowerRequired are a cable network's numbers
            // (CableNetwork.CurrentLoad, PotentialLoad, RequiredLoad), answered only by the
            // devices that watch a network: Battery and AreaPowerControl the first two,
            // CableAnalyser all three. A plain consumer answers none of them, and
            // neither does a transformer, which answers PowerGeneration no more than they.
            if (!transformer)
            {
                Read(t.ValueOf("PowerGeneration"), view.PowerGeneration);
                Read(t.ValueOf("PowerActual"), view.CircuitActual);
                Read(t.ValueOf("PowerPotential"), view.CircuitPotential);
                Read(t.ValueOf("PowerRequired"), view.CircuitRequired);
            }

            // Device.CanLogicRead: RequiredPower needs UsedPower > 0 and a power state.
            // Device.GetUsedPower: -1 with no cable, 0 while switched off, else the rated
            // draw, whether or not the device is working at the moment.
            if (view.RatedPower != null && view.IsPowered != null)
            {
                Read(t.ValueOf("RequiredPower"), () =>
                {
                    if (view.IsWired != null && !view.IsWired())
                    {
                        return -1.0;
                    }
                    if (view.GetEnabled != null && !view.GetEnabled())
                    {
                        return 0.0;
                    }
                    return view.RatedPower();
                }, () => view.RatedPower() > 0.0);
            }

            // ONI only: what this one machine draws now. No Stationeers device
            // answers that for itself.
            Read(t.ValueOf("PowerDraw"), view.PowerDraw);

            if (view.Counter != null && view.Counter.Get != null)
            {
                // ChuteDigitalValve: Quantity is what has passed, and writing it sets the
                // count, which is how it is reset. No clamp there; a NaN is dropped here.
                Ic10OniControl counter = view.Counter;
                Read(t.ValueOf("Quantity"), counter.Get);
                if (counter.Set != null)
                {
                    Write(t.ValueOf("Quantity"), value =>
                    {
                        if (!double.IsNaN(value))
                        {
                            counter.Set(value);
                        }
                    });
                }
            }
            else if (view.AmountStored != null)
            {
                Read(t.ValueOf("Quantity"), view.AmountStored);
            }
            else if (view.Storage != null)
            {
                Read(t.ValueOf("Quantity"), () => TotalMass(view.Storage));
            }

            Read(t.ValueOf("Reading"), view.Reading);
            Read(t.ValueOf("LogicInput"), view.LogicInput);
            Read(t.ValueOf("LogicOutput"), view.LogicOutput);
            if (view.LogicInput != null && view.SetLogicInput != null)
            {
                Write(t.ValueOf("LogicInput"), view.SetLogicInput);
                // where Stationeers has a name for what the port does, the name
                // drives it too. It replaces what the name meant above (the solid logic
                // valve's On was its enable button, which leaves the valve shut).
                string name;
                if (view.PrefabId != null && PortNames.TryGetValue(view.PrefabId, out name))
                {
                    Read(t.ValueOf(name), () => view.LogicInput() != 0.0 ? 1.0 : 0.0);
                    Write(t.ValueOf(name), value => view.SetLogicInput(State(value) ? 1.0 : 0.0));
                }
            }

            // ONI only: the filter's element, and the two ends of an activation
            // range.
            if (view.GetFilter != null)
            {
                Read(t.ValueOf("Filter"), () => view.GetFilter());
                if (view.SetFilter != null)
                {
                    Write(t.ValueOf("Filter"), value =>
                    {
                        // A hash is a whole number in int range; anything else names nothing.
                        if (!double.IsNaN(value) && value >= int.MinValue && value <= int.MaxValue && value == Math.Floor(value))
                        {
                            view.SetFilter((int)value);
                        }
                    });
                }
            }
            MapRangeEnd(view.ActivateAt, t.ValueOf("ActivateAt"));
            MapRangeEnd(view.DeactivateAt, t.ValueOf("DeactivateAt"));

            if (view.Brick != null)
            {
                MapBrick(view.Brick, t);
            }
            // Device.GetLogicValue: Mode is the device's mode number; SetLogicValue:
            // (int)value.Clamp(0.0, ModeStrings.Length - 1). A sensor's above/below and a
            // brick's Mode come first; a NaN is dropped.
            if (view.Choice != null && view.Choice.Get != null && !CanLogicRead(t.ValueOf("Mode")))
            {
                Ic10OniChoice choice = view.Choice;
                Read(t.ValueOf("Mode"), () => choice.Get());
                if (choice.Set != null)
                {
                    Write(t.ValueOf("Mode"), value =>
                    {
                        int count = choice.Count();
                        if (double.IsNaN(value) || count <= 0)
                        {
                            return;
                        }
                        choice.Set((int)(value < 0.0 ? 0.0 : value > count - 1 ? count - 1 : value));
                    });
                }
            }
            // CompletionRatio, as on a machine with recipes: hundredths, cut.
            if (view.Progress != null && view.Fabricator == null)
            {
                Read(t.ValueOf("CompletionRatio"), () =>
                {
                    double progress = view.Progress();
                    if (double.IsNaN(progress) || progress <= 0.0)
                    {
                        return 0.0;
                    }
                    return progress >= 1.0 ? 1.0 : Math.Floor(progress * 100.0) / 100.0;
                });
            }
            MapOniReadings(view, t);
            MapPort(view.PipeInput, "Input", t);
            MapPort(view.PipeInput2, "Input2", t);
            MapPort(view.PipeOutput, "Output", t);
            MapPort(view.PipeOutput2, "Output2", t);
            if (view.Fabricator != null)
            {
                MapFabricator(view.Fabricator, t);
            }
            if (view.Plot != null)
            {
                MapPlot(view.Plot, t);
            }
        }

        // DeviceOutput.GetLogicValue and DeviceInputOutput.GetLogicValue: the pressure,
        // temperature, moles and ratios of the pipe network at a port, under the plain names
        // with the port's suffix. 0 while no pipe is there (GetValueOrDefault on a null
        // network). Stationeers has no suffixed name for every species; the ones it has not
        // got are not answered.
        private void MapPort(Func<IIc10OniAtmosphere> port, string suffix, Ic10EnumTable t)
        {
            if (port == null)
            {
                return;
            }
            Read(t.ValueOf("TotalMoles" + suffix), () => Ic10OniAtmosphere.TotalMoles(port()));
            Read(t.ValueOf("Temperature" + suffix), () =>
            {
                IIc10OniAtmosphere a = port();
                return a == null ? 0.0 : a.Temperature;
            });
            Read(t.ValueOf("Pressure" + suffix), () =>
            {
                IIc10OniAtmosphere a = port();
                return a == null ? 0.0 : a.PressurePa / 1000.0;
            }, () =>
            {
                IIc10OniAtmosphere a = port();
                return a == null || !double.IsNaN(a.PressurePa);
            });
            foreach (KeyValuePair<string, string> ratio in Ic10OniAtmosphere.Ratios)
            {
                int element = Ic10Math.Hash(ratio.Value);
                Read(t.ValueOf(ratio.Key + suffix), () => Ic10OniAtmosphere.Ratio(port(), element));
            }
        }

        // SimpleFabricatorBase. RecipeHash written selects a recipe and does not
        // start it; Activate 1 makes the selected recipe until its materials run out;
        // CompletionRatio is how far along the one item being made is.
        // ONI-only readings, read only.
        private void MapOniReadings(Ic10OniBuildingView view, Ic10EnumTable t)
        {
            if (view.VentState != null)
            {
                Read(t.ValueOf("VentState"), () => view.VentState());
            }
            if (view.ConsumedRate != null)
            {
                Read(t.ValueOf("ConsumedRate"), () => Finite(view.ConsumedRate()));
            }
            if (view.ProducedRate != null)
            {
                Read(t.ValueOf("ProducedRate"), () => Finite(view.ProducedRate()));
            }
            if (view.FlushesLeft != null)
            {
                Read(t.ValueOf("FlushesLeft"), () => Math.Max(0, view.FlushesLeft()));
            }
            if (view.Cycle != null)
            {
                Read(t.ValueOf("Cycle"), () => view.Cycle());
                Read(t.ValueOf("CycleTime"), () => view.CycleTime());
                Read(t.ValueOf("Night"), () => view.Night() ? 1.0 : 0.0);
            }
            MapControl(view.OnDuration, t.ValueOf("OnDuration"));
            MapControl(view.OffDuration, t.ValueOf("OffDuration"));
            MapControl(view.WindowStart, t.ValueOf("WindowStart"));
            MapControl(view.WindowLength, t.ValueOf("WindowLength"));
            Ic10OniRocket rocket = view.Rocket;
            if (rocket != null)
            {
                Read(t.ValueOf("FlightState"), () => rocket.Present() ? rocket.FlightState() : 0.0);
                Read(t.ValueOf("TimeToDestination"), () => rocket.Present() ? Finite(rocket.TimeToDestination()) : 0.0);
                Read(t.ValueOf("FuelRemaining"), () => rocket.Present() ? Finite(rocket.FuelRemaining()) : 0.0);
                Read(t.ValueOf("OxidizerRemaining"), () => rocket.Present() ? Finite(rocket.OxidizerRemaining()) : 0.0);
                Read(t.ValueOf("RocketRange"), () => rocket.Present() ? Finite(rocket.Range()) : 0.0);
                Read(t.ValueOf("ReadyToLaunch"), () => rocket.Present() && rocket.ReadyToLaunch() ? 1.0 : 0.0);
            }
        }

        // One setting under its own name: read, and written through the side screen's range.
        private void MapControl(Ic10OniControl control, int logicType)
        {
            if (control == null || control.Get == null)
            {
                return;
            }
            Read(logicType, control.Get);
            if (control.Set != null)
            {
                Write(logicType, value => WriteSetting(control, value));
            }
        }

        private static double Finite(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : value;
        }

        private void MapFabricator(Ic10OniFabricator f, Ic10EnumTable t)
        {
            // SimpleFabricatorBase.CurrentHash: the product being made while it runs, else
            // the selected product; 0 only when the machine has no recipe.
            Read(t.ValueOf("RecipeHash"), () =>
            {
                int working = f.Working();
                if (working >= 0)
                {
                    return f.ProductHash(working);
                }
                int selected = f.GetSelected();
                return selected >= 0 && selected < f.Count() ? f.ProductHash(selected) : 0.0;
            });
            // SetRecipeFromHash: the first recipe whose product has that hash; a hash that
            // names none, 0 included, changes nothing. ONI adds: a recipe's own id is taken
            // too, for a machine with two recipes for one product.
            Write(t.ValueOf("RecipeHash"), value =>
            {
                if (double.IsNaN(value) || value < int.MinValue || value > int.MaxValue)
                {
                    return;
                }
                int hash = (int)value;
                if (hash == 0)
                {
                    return;
                }
                int count = f.Count();
                int found = Ic10PrinterStack.Find(f, hash);
                int selected = f.GetSelected();
                if (found < 0 || found == selected)
                {
                    return;
                }
                // WaitThenMake stops after the item in hand when the selection changes, and
                // does not start the new one. An order of one is the item in hand: ONI
                // drops the item being made when its recipe's count goes to 0
                // (ComplexFabricator.ValidateWorkingOrder).
                if (selected >= 0 && selected < count && f.GetQueue(selected) == Ic10OniFabricator.Forever)
                {
                    f.SetQueue(selected, f.Working() == selected ? 1 : 0);
                }
                f.SetSelected(found);
            });
            // Activate reads 1 while the selected recipe is on order. Not "working at this
            // moment", which every other building's Activate is: the chip writes only what
            // differs from what it reads, so a read that could say 0 while the order stands
            // (the machine waiting for materials) would swallow the Activate 0 that cancels it.
            Read(t.ValueOf("Activate"), () =>
            {
                int selected = f.GetSelected();
                return selected >= 0 && selected < f.Count() && f.GetQueue(selected) != 0 ? 1.0 : 0.0;
            });
            // Device.SetLogicValue: 0 or 1, and passed on only while the device is switched on.
            Write(t.ValueOf("Activate"), value =>
            {
                if (double.IsNaN(value) || (_view.GetEnabled != null && !_view.GetEnabled()))
                {
                    return;
                }
                int selected = f.GetSelected();
                if (selected < 0 || selected >= f.Count() || f.ProductHash(selected) == 0)
                {
                    return;
                }
                if (State(value))
                {
                    f.SetQueue(selected, Ic10OniFabricator.Forever);
                }
                else if (f.GetQueue(selected) != 0)
                {
                    f.SetQueue(selected, 0);
                }
            });
            // Mathf.Clamp01(Processing / 100f), Processing a byte: hundredths, cut not rounded.
            Read(t.ValueOf("CompletionRatio"), () =>
            {
                if (f.Working() < 0)
                {
                    return 0.0;
                }
                double progress = f.Progress();
                if (double.IsNaN(progress) || progress <= 0.0)
                {
                    return 0.0;
                }
                return progress >= 1.0 ? 1.0 : Math.Floor(progress * 100.0) / 100.0;
            });
        }

        // HydroponicsAutomated.SetLogicValue: Harvest written above 0 is one
        // harvest. AutoHarvest is ONI's own "harvest when ready" switch.
        private void MapPlot(Ic10OniPlot plot, Ic10EnumTable t)
        {
            if (plot.Harvest != null)
            {
                Write(t.ValueOf("Harvest"), value =>
                {
                    if (value > 0.0 && plot.HasPlant())
                    {
                        plot.Harvest();
                    }
                });
            }
            if (plot.GetAutoHarvest != null)
            {
                Read(t.ValueOf("AutoHarvest"), () => plot.HasPlant() && plot.GetAutoHarvest() ? 1.0 : 0.0);
                if (plot.SetAutoHarvest != null)
                {
                    Write(t.ValueOf("AutoHarvest"), value =>
                    {
                        if (!double.IsNaN(value) && plot.HasPlant())
                        {
                            plot.SetAutoHarvest(State(value));
                        }
                    });
                }
            }
        }

        private void MapRangeEnd(Ic10OniControl control, int logicType)
        {
            if (control != null && control.Get != null)
            {
                Read(logicType, control.Get);
                if (control.Set != null)
                {
                    Write(logicType, value => WriteSetting(control, value));
                }
            }
        }

        // LogicDial.MaxMode: a dial's Mode, the top of its range, is a mode number, and
        // Device.SetLogicValue clamps a written Mode to 0..ModeStrings.Length - 1.
        public const int DialMaxMode = 1000;

        private void MapBrick(Ic10OniBrick brick, Ic10EnumTable t)
        {
            int setting = t.ValueOf("Setting");
            int mode = t.ValueOf("Mode");
            Read(setting, brick.GetSetting);
            switch (brick.Kind)
            {
                case Ic10OniBrickKind.Memory:
                    // LogicMemory.SetLogicValue: stored as written, with no clamp.
                    Write(setting, brick.SetSetting);
                    break;

                case Ic10OniBrickKind.Dial:
                    // LogicDial.SetLogicValue: (int)Mathf.Clamp((float)value, 0f, Mode).
                    // GetLogicValue: Ratio is Setting / (double)Mode, with no guard, so a
                    // dial whose Mode is 0 reads NaN there. A NaN written is dropped.
                    Write(setting, value =>
                    {
                        if (!double.IsNaN(value))
                        {
                            float top = brick.GetMode();
                            float clamped = (float)value < 0f ? 0f : (float)value > top ? top : (float)value;
                            brick.SetSetting((int)clamped);
                        }
                    });
                    Read(t.ValueOf("Ratio"), () => brick.GetSetting() / (double)brick.GetMode());
                    Read(mode, () => brick.GetMode());
                    Write(mode, value => WriteMode(brick, value, DialMaxMode - 1));
                    break;

                case Ic10OniBrickKind.Switch:
                    // LogicSwitch: Setting is read only and follows the lever, which is the
                    // device's Open state (OnInteractableUpdated), and Open can be written.
                    Read(t.ValueOf("Open"), brick.GetSetting);
                    Write(t.ValueOf("Open"), value => brick.SetSetting(State(value) ? 1.0 : 0.0));
                    break;

                case Ic10OniBrickKind.Button:
                    // LogicButton: Setting is read only, 1 for 550 ms after a press. A press
                    // is the device's Activate state; Device.SetLogicValue passes a written
                    // Activate on only while the device is switched on.
                    Read(t.ValueOf("Activate"), brick.GetSetting);
                    Write(t.ValueOf("Activate"), value =>
                    {
                        if (State(value) && brick.Press != null && (_view.GetEnabled == null || _view.GetEnabled()))
                        {
                            brick.Press();
                        }
                    });
                    break;

                case Ic10OniBrickKind.Display:
                    // LogicDisplay.SetLogicValue: stored as written. Mode is the form it is
                    // shown in, one of LogicDisplay.DisplayMode.
                    Write(setting, brick.SetSetting);
                    Read(mode, () => brick.GetMode());
                    Write(mode, value => WriteMode(brick, value, Ic10Display.ModeCount - 1));
                    // Device.SetLogicValue: Color is (int)value.Clamp(0, ColorCount - 1), set
                    // only when GameManager.IsLogicSelectableColor; GetLogicValue reads it back.
                    if (brick.GetColor != null)
                    {
                        Read(t.ValueOf("Color"), () => brick.GetColor());
                        if (brick.SetColor != null)
                        {
                            Write(t.ValueOf("Color"), value =>
                            {
                                if (!double.IsNaN(value))
                                {
                                    brick.SetColor((int)(value < 0.0 ? 0.0 : value > Ic10Display.ColorCount - 1 ? Ic10Display.ColorCount - 1 : value));
                                }
                            });
                        }
                    }
                    break;
            }
        }

        // Device.SetLogicValue: (int)value.Clamp(0.0, ModeStrings.Length - 1). A NaN is
        // dropped.
        private static void WriteMode(Ic10OniBrick brick, double value, int highest)
        {
            if (double.IsNaN(value))
            {
                return;
            }
            brick.SetMode((int)(value < 0.0 ? 0.0 : value > highest ? highest : value));
        }

        // Pressure, TotalMoles, the gas ratios and the volumes, about what the building holds
        // (Device.GetLogicValue), the air it stands in (GasSensor.GetLogicValue) or the pipe
        // it sits on (PipeAnalysizer.GetLogicValue). Units are Stationeers': kilopascals,
        // moles, mole fractions, litres.
        private void MapAtmosphere(Ic10OniBuildingView view, Ic10EnumTable t)
        {
            Func<IIc10OniAtmosphere> air = view.Atmosphere;
            Ic10OniAtmosphereKind kind = view.AtmosphereKind;
            if (air == null)
            {
                if (view.Storage == null)
                {
                    return;
                }
                IIc10OniAtmosphere held = Ic10OniAtmosphere.Of(view.Storage);
                air = () => held;
                kind = Ic10OniAtmosphereKind.Held;
            }

            // PipeAnalysizer answers -1 for these while it has no network; a gas sensor with
            // no atmosphere answers 0.
            double none = kind == Ic10OniAtmosphereKind.Pipe ? -1.0 : 0.0;

            Read(t.ValueOf("TotalMoles"), () =>
            {
                IIc10OniAtmosphere a = air();
                return a == null ? none : Ic10OniAtmosphere.TotalMoles(a);
            });
            Read(t.ValueOf("Pressure"), () =>
            {
                IIc10OniAtmosphere a = air();
                return a == null ? none : a.PressurePa / 1000.0;
            }, () =>
            {
                IIc10OniAtmosphere a = air();
                return a == null || !double.IsNaN(a.PressurePa);
            });
            // A temperature sensor keeps its own reading and a storage its fluid's
            // temperature, both mapped before this.
            if (!CanLogicRead(t.ValueOf("Temperature")))
            {
                Read(t.ValueOf("Temperature"), () =>
                {
                    IIc10OniAtmosphere a = air();
                    return a == null ? none : a.Temperature;
                });
            }
            foreach (KeyValuePair<string, string> ratio in Ic10OniAtmosphere.Ratios)
            {
                int element = Ic10Math.Hash(ratio.Value);
                Read(t.ValueOf(ratio.Key), () => Ic10OniAtmosphere.Ratio(air(), element));
            }
            if (kind != Ic10OniAtmosphereKind.Held)
            {
                Read(t.ValueOf("VolumeOfLiquid"), () =>
                {
                    IIc10OniAtmosphere a = air();
                    return a == null ? none : a.LiquidLitres;
                }, () =>
                {
                    IIc10OniAtmosphere a = air();
                    return a == null || !double.IsNaN(a.LiquidLitres);
                });
            }
            if (kind == Ic10OniAtmosphereKind.Pipe)
            {
                Read(t.ValueOf("Volume"), () =>
                {
                    IIc10OniAtmosphere a = air();
                    return a == null ? none : a.VolumeLitres;
                }, () =>
                {
                    IIc10OniAtmosphere a = air();
                    return a == null || !double.IsNaN(a.VolumeLitres);
                });
                if (view.NetworkFault != null)
                {
                    Read(t.ValueOf("NetworkFault"), () => view.NetworkFault() ? 1.0 : 0.0);
                }
            }
        }

        public override int PrefabHash
        {
            get { return _prefabHash; }
            set { }
        }

        // The name can change while the chip runs (a player renames the building), so it is
        // asked for on every read and hashed again only when it differs.
        public override int NameHash
        {
            get
            {
                string name = _view.DisplayName == null ? null : _view.DisplayName();
                if (name == null)
                {
                    return 0;
                }
                if (!string.Equals(name, _hashedName, StringComparison.Ordinal))
                {
                    _hashedName = name;
                    _nameHash = Ic10Math.Hash(name);
                }
                return _nameHash;
            }
            set { }
        }

        // The side screen's own setter, inside the control's own range. A NaN is dropped: no
        // slider has a position for it.
        private static void WriteSetting(Ic10OniControl setting, double value)
        {
            if (double.IsNaN(value))
            {
                return;
            }
            if (setting.Min != null)
            {
                double min = setting.Min();
                if (value < min)
                {
                    value = min;
                }
            }
            if (setting.Max != null)
            {
                double max = setting.Max();
                if (value > max)
                {
                    value = max;
                }
            }
            if (setting.Decimals != null)
            {
                // SliderSet.SetValue: Mathf.Round(value * 10^places) / 10^places.
                double scale = Math.Pow(10.0, setting.Decimals());
                value = Math.Round(value * scale) / scale;
            }
            setting.Set(value);
        }

        // GasMixture.Temperature: total energy over total heat capacity, and zero when
        // there is nothing to have a temperature.
        internal static double FluidTemperature(IIc10OniStorage storage)
        {
            double energy = 0.0;
            double capacity = 0.0;
            int count = storage.Count;
            for (int i = 0; i < count; i++)
            {
                if (storage.IsFluid(i))
                {
                    double c = storage.HeatCapacity(i);
                    capacity += c;
                    energy += c * storage.Temperature(i);
                }
            }
            return capacity > 0.0 ? energy / capacity : 0.0;
        }

        internal static double TotalMass(IIc10OniStorage storage)
        {
            double total = 0.0;
            int count = storage.Count;
            for (int i = 0; i < count; i++)
            {
                total += storage.Mass(i);
            }
            return total;
        }
    }

    // A building with a Storage, a planter's plot, or both. The storage's items are slots,
    // in the storage's own order, and its contents by element are what `lr ... Contents`
    // reads.
    //
    // ONI storage has no fixed slots: there are as many as there are items, the
    // position of an item moves when another leaves, and every position past the last item
    // is an empty slot rather than a missing one, so a script can ask slot 0 of an empty bin
    // whether it is occupied without stopping on an error.
    //
    // A planter's slot 0 is its plant, as a hydroponics tray's is (HydroponicsTrayDevice,
    // InputSlot); what its storage holds, fertiliser and water, follows from slot 1.
    public class Ic10OniStorageBuilding : Ic10OniBuilding, IIc10ReagentContents
    {
        private readonly IIc10OniStorage _storage;
        private readonly Ic10OniPlot _plot;
        private readonly int _first;
        private readonly int _occupied;
        private readonly int _occupantHash;
        private readonly int _quantity;
        private readonly int _temperature;
        private readonly int _prefabHashSlot;
        private readonly int _totalSlots;
        private readonly int _mature;
        private readonly int _maturityRatio;
        private readonly int _health;
        private readonly int _growth;

        internal Ic10OniStorageBuilding(Ic10OniBuildingView view, Ic10EnumSet enums) : base(view, enums)
        {
            _storage = view.Storage;
            _plot = view.Plot;
            _first = _plot != null ? 1 : 0;
            Ic10EnumTable t = enums.LogicSlotType;
            _occupied = t.ValueOf("Occupied");
            _occupantHash = t.ValueOf("OccupantHash");
            _quantity = t.ValueOf("Quantity");
            _temperature = t.ValueOf("Temperature");
            _prefabHashSlot = t.ValueOf("PrefabHash");
            _totalSlots = t.ValueOf("TotalSlots");
            _mature = t.ValueOf("Mature");
            _maturityRatio = t.ValueOf("MaturityRatio");
            _health = t.ValueOf("Health");
            _growth = t.ValueOf("Growth");
        }

        public override bool CanSlotRead(int logicSlotType, int slot)
        {
            if (slot < 0)
            {
                return false;
            }
            if (_plot != null && slot == 0)
            {
                // HydroponicsTrayDevice.CanLogicRead for its plant slot, less what ONI's
                // plants do not have: Efficiency, Seeding and SeedingRatio. Growth is a
                // percentage here, ONI having no stage number.
                return logicSlotType == _occupied || logicSlotType == _occupantHash || logicSlotType == _quantity
                    || logicSlotType == _prefabHashSlot || logicSlotType == _mature || logicSlotType == _maturityRatio
                    || logicSlotType == _growth || logicSlotType == _health || logicSlotType == _totalSlots;
            }
            return logicSlotType == _occupied || logicSlotType == _occupantHash || logicSlotType == _quantity
                || logicSlotType == _temperature || logicSlotType == _prefabHashSlot || logicSlotType == _totalSlots;
        }

        // An empty slot answers 0 to everything, as Device.GetLogicValue(LogicSlotType, int)
        // does for a slot with no occupant.
        public override double GetSlotValue(int logicSlotType, int slot)
        {
            if (slot < 0)
            {
                return 0.0;
            }
            int count = _storage == null ? 0 : _storage.Count;
            if (logicSlotType == _totalSlots)
            {
                return count + _first;
            }
            if (_plot != null && slot == 0)
            {
                return PlantValue(logicSlotType);
            }
            slot -= _first;
            if (slot >= count)
            {
                return 0.0;
            }
            if (logicSlotType == _occupied)
            {
                return 1.0;
            }
            if (logicSlotType == _occupantHash || logicSlotType == _prefabHashSlot)
            {
                return _storage.PrefabHash(slot);
            }
            if (logicSlotType == _quantity)
            {
                return _storage.Mass(slot);
            }
            if (logicSlotType == _temperature)
            {
                return _storage.Temperature(slot);
            }
            return 0.0;
        }

        // HydroponicsTrayDevice.GetLogicValue(LogicSlotType, int): -1 for the plant's own
        // numbers while there is no plant, 0 for the slot's.
        private double PlantValue(int logicSlotType)
        {
            bool planted = _plot.HasPlant();
            if (logicSlotType == _occupied || logicSlotType == _quantity)
            {
                return planted ? 1.0 : 0.0;
            }
            if (logicSlotType == _occupantHash || logicSlotType == _prefabHashSlot)
            {
                return planted ? _plot.PlantHash() : 0.0;
            }
            if (!planted)
            {
                return -1.0;
            }
            if (logicSlotType == _mature)
            {
                return _plot.Harvestable() ? 1.0 : 0.0;
            }
            if (logicSlotType == _maturityRatio || logicSlotType == _growth)
            {
                double grown = _plot.Grown();
                grown = double.IsNaN(grown) ? 0.0 : grown < 0.0 ? 0.0 : grown > 1.0 ? 1.0 : grown;
                return logicSlotType == _growth ? grown * 100.0 : grown;
            }
            if (logicSlotType == _health)
            {
                return _plot.Wilting() ? 0.0 : 1.0;
            }
            return 0.0;
        }

        // Kilograms stored of the element whose id hashes to reagentHash, over every item.
        public double GetReagentContents(int reagentHash)
        {
            double total = 0.0;
            int count = _storage == null ? 0 : _storage.Count;
            for (int i = 0; i < count; i++)
            {
                if (_storage.ElementHash(i) == reagentHash)
                {
                    total += _storage.Mass(i);
                }
            }
            return total;
        }

        public double TotalReagents
        {
            get { return _storage == null ? 0.0 : TotalMass(_storage); }
        }
    }

    // A machine with an instruction stack: Stationeers' printers are IMemoryReadable and
    // IMemoryWritable, and answer StackSize (Device.CanLogicRead: `this is IMemory`).
    public sealed class Ic10OniFabricatorBuilding : Ic10OniStorageBuilding, IIc10MemoryReadable, IIc10MemoryWritable
    {
        private readonly Ic10PrinterStack _stack;

        internal Ic10OniFabricatorBuilding(Ic10OniBuildingView view, Ic10EnumSet enums) : base(view, enums)
        {
            _stack = view.Fabricator.Stack;
            Read(enums.LogicType.ValueOf("StackSize"), () => Ic10PrinterStack.Size);
        }

        public double ReadMemory(int address)
        {
            return _stack.ReadMemory(address);
        }

        public void WriteMemory(int address, double value)
        {
            _stack.WriteMemory(address, value);
        }

        public void ClearMemory()
        {
            _stack.ClearMemory();
        }
    }

    // A bin or a fridge whose item checklist a chip sets through the IC Sorter's memory.
    // Its slots answer as any storage's; the memory is the sorter's 32 cells.
    public sealed class Ic10OniChecklistBuilding : Ic10OniStorageBuilding, IIc10MemoryReadable, IIc10MemoryWritable
    {
        private readonly Ic10Checklist _checklist;

        internal Ic10OniChecklistBuilding(Ic10OniBuildingView view, Ic10EnumSet enums) : base(view, enums)
        {
            _checklist = view.Checklist;
            Read(enums.LogicType.ValueOf("StackSize"), () => Ic10Checklist.Size);
        }

        public double ReadMemory(int address)
        {
            return _checklist.ReadMemory(address);
        }

        public void WriteMemory(int address, double value)
        {
            _checklist.WriteMemory(address, value);
        }

        public void ClearMemory()
        {
            _checklist.ClearMemory();
        }
    }

    // The IC Sorter: LogicSorter's memory and the logic types it answers through
    // DeviceImportExport2 (ImportCount, ExportCount, ClearMemory) and Device (Mode, here
    // LogicOperator's All, Any, None; StackSize).
    public sealed class Ic10OniSorterBuilding : Ic10OniBuilding, IIc10MemoryReadable, IIc10MemoryWritable
    {
        private readonly Ic10Sorter _sorter;

        internal Ic10OniSorterBuilding(Ic10OniBuildingView view, Ic10EnumSet enums) : base(view, enums)
        {
            _sorter = view.Sorter;
            Ic10EnumTable t = enums.LogicType;
            Read(t.ValueOf("Mode"), () => _sorter.Mode);
            Write(t.ValueOf("Mode"), value => _sorter.Mode = (int)value);
            Read(t.ValueOf("ImportCount"), () => _sorter.ImportCount);
            Read(t.ValueOf("ExportCount"), () => _sorter.ExportCount);
            Write(t.ValueOf("ClearMemory"), value =>
            {
                if (value > 0.0)
                {
                    _sorter.ClearCounts();
                }
            });
            Read(t.ValueOf("StackSize"), () => Ic10Sorter.Size);
        }

        public double ReadMemory(int address)
        {
            return _sorter.ReadMemory(address);
        }

        public void WriteMemory(int address, double value)
        {
            _sorter.WriteMemory(address, value);
        }

        public void ClearMemory()
        {
            _sorter.ClearMemory();
        }
    }

    // How an ONI item answers the sorter's class tests. Stationeers gives every item a
    // SortingClass and a Slot.Class in its prefab; ONI has neither, so they are read off the
    // item's category tags, the first rule that matches winning. The tag names are
    // GameTags' own.
    public static class Ic10SortClasses
    {
        private struct Rule
        {
            public string Tag;
            public ushort Value;
        }

        private static Rule R(string tag, string value, KeyValuePair<string, int>[] table)
        {
            foreach (KeyValuePair<string, int> pair in table)
            {
                if (pair.Key == value)
                {
                    return new Rule { Tag = tag, Value = (ushort)pair.Value };
                }
            }
            throw new ArgumentException(value);
        }

        private static Rule S(string tag, string value)
        {
            return R(tag, value, Ic10StationeersTables.SortingClass);
        }

        private static Rule L(string tag, string value)
        {
            return R(tag, value, Ic10StationeersTables.SlotClass);
        }

        private static readonly Rule[] Sorting =
        {
            S("Edible", "Food"),
            S("Clothes", "Clothing"), S("Atmo_Suit", "Clothing"), S("Jet_Suit", "Clothing"),
            S("Lead_Suit", "Clothing"), S("Oxygen_Mask", "Clothing"),
            S("Liquifiable", "Ices"),
            S("Metal", "Ores"), S("BuildableRaw", "Ores"), S("ConsumableOre", "Ores"),
            S("Filter", "Ores"), S("Farmable", "Ores"),
            S("RefinedMetal", "Resources"), S("Alloy", "Resources"), S("ManufacturedMaterial", "Resources"),
            S("IndustrialProduct", "Resources"), S("IndustrialIngredient", "Resources"),
            S("RareMaterials", "Resources"), S("BuildingFiber", "Resources"), S("BuildingWood", "Resources"),
            S("Plastic", "Resources"), S("Glass", "Resources"), S("Organics", "Resources"),
            S("Agriculture", "Resources"), S("Compostable", "Resources"),
        };

        private static readonly Rule[] Slots =
        {
            L("Egg", "Egg"),
            L("Seed", "Plant"),
            L("Oxygen_Mask", "Helmet"),
            L("Atmo_Suit", "Suit"), L("Jet_Suit", "Suit"), L("Lead_Suit", "Suit"),
            L("Clothes", "Uniform"),
            L("Metal", "Ore"), L("BuildableRaw", "Ore"), L("ConsumableOre", "Ore"), L("Filter", "Ore"),
            L("Farmable", "Ore"), L("Liquifiable", "Ore"),
            L("RefinedMetal", "Ingot"), L("Alloy", "Ingot"),
        };

        // 0 (Default, None) when no rule matches.
        public static ushort SortingClass(Func<string, bool> hasTag)
        {
            return First(Sorting, hasTag);
        }

        public static ushort SlotType(Func<string, bool> hasTag)
        {
            return First(Slots, hasTag);
        }

        private static ushort First(Rule[] rules, Func<string, bool> hasTag)
        {
            for (int i = 0; i < rules.Length; i++)
            {
                if (hasTag(rules[i].Tag))
                {
                    return rules[i].Value;
                }
            }
            return 0;
        }

        // IQuantity.GetQuantity, cast to ushort: ONI's amount (units for a countable item,
        // kilograms for an element), whole units, 0..65535.
        public static ushort Quantity(double amount)
        {
            if (double.IsNaN(amount) || amount <= 0.0)
            {
                return 0;
            }
            return amount >= 65535.0 ? (ushort)65535 : (ushort)Math.Floor(amount);
        }
    }

    // A building that watches air or a pipe and has no Storage: `lr ... Contents` reads the
    // kilograms there of the element whose id hashes to the number given, as it does for a
    // Storage. This is how an element with no Stationeers ratio name is reached.
    public sealed class Ic10OniAtmosphereBuilding : Ic10OniBuilding, IIc10ReagentContents
    {
        private readonly Func<IIc10OniAtmosphere> _air;

        internal Ic10OniAtmosphereBuilding(Ic10OniBuildingView view, Ic10EnumSet enums) : base(view, enums)
        {
            _air = view.Atmosphere;
        }

        public double GetReagentContents(int reagentHash)
        {
            return Ic10OniAtmosphere.MassOf(_air(), reagentHash);
        }

        public double TotalReagents
        {
            get { return Ic10OniAtmosphere.TotalMass(_air()); }
        }
    }
}
