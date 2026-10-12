using System;
using System.Collections.Generic;
using HarmonyLib;
using Ic10;
using UnityEngine;

namespace Mod5AutomationIc10
{
    // Fills the adapter's view (Ic10/Ic10OniBuilding.cs) from a building's real components:
    // one line per thing a vanilla component can answer, left null when the building has no
    // such component. What a logic type means is decided in the adapter, where the offline
    // tests reach it; nothing here decides anything.
    //
    // Every line is read from the game's code. Run in the game by the development tests for
    // the sensor, the housing, the battery, the wattage sensor, the generator, the solar panel
    // and the transformer; the rest (doors, storage, sliders, capacity, logic ports) is read
    // and not run.
    internal static class Ic10OniViews
    {
        // A power circuit's watts: 0 what its consumers drew last tick
        // (CircuitManager.GetWattsUsedByCircuit), 1 what its generators are making now, 2
        // what its consumers are rated to draw, through transformers
        // (GetWattsNeededWhenActive). CircuitManager answers -1 for "no circuit";
        // Stationeers' analyser answers 0 with no network, and so does this.
        //
        // The supply is not CircuitManager.GetWattsGeneratedByCircuit, which
        // counts every running generator at its rating: a solar panel in half light and a
        // transformer turned down both counted in full. CableNetwork.PotentialLoad sums
        // what each provider offers (GetGeneratedPower), so this sums WattsMadeNow.
        //
        // Transformers feeding the circuit are added here: CircuitManager keeps them in its
        // private outputTransformers list, not among the generators, so
        // GetGeneratorsOnCircuit never has them (it reads 0 behind a
        // transformer). Every transformer carries an Ic10TransformerCap, whose registry
        // lists them; Generator.CircuitID is the output side.
        private static double CircuitWatts(ushort circuit, int which)
        {
            CircuitManager manager = Game.Instance.circuitManager;
            if (which == 1)
            {
                List<Generator> generators = manager.GetGeneratorsOnCircuit(circuit);
                double made = 0.0;
                if (generators != null)
                {
                    for (int i = 0; i < generators.Count; i++)
                    {
                        if (generators[i] != null)
                        {
                            made += WattsMadeNow(generators[i]);
                        }
                    }
                }
                foreach (PowerTransformer transformer in Ic10TransformerCap.Live.Keys)
                {
                    if (transformer != null && transformer.CircuitID == circuit)
                    {
                        made += WattsMadeNow(transformer);
                    }
                }
                return made;
            }
            float watts = which == 0 ? manager.GetWattsUsedByCircuit(circuit) : manager.GetWattsNeededWhenActive(circuit);
            return watts < 0f ? 0.0 : watts;
        }

        // Watts one generator makes now. A generator that burns something makes its rating
        // whenever it runs (EnergyGenerator.EnergySim200ms); a solar panel and a steam
        // turbine make what the light or the steam gives, and keep that figure themselves
        // (CurrentWattage, the 3 s average their own status line shows); a transformer
        // passes its output cap at most (Ic10TransformerCap). ElectrobankDischarger also makes less than its rating when
        // its cells run low and keeps no figure; it still reads its rating.
        internal static double WattsMadeNow(Generator generator)
        {
            SolarPanel solar = generator as SolarPanel;
            if (solar != null)
            {
                return solar.CurrentWattage;
            }
            ModuleSolarPanel moduleSolar = generator as ModuleSolarPanel;
            if (moduleSolar != null)
            {
                return moduleSolar.CurrentWattage;
            }
            SteamTurbine turbine = generator as SteamTurbine;
            if (turbine != null)
            {
                return turbine.CurrentWattage;
            }
            // A transformer never sets Operational active, so IsProducingPower is always
            // false for it. It offers its cap (else its rating) while it is operational and
            // its input side holds energy (PowerTransformer.EnergySim200ms offers
            // Min(input joules, rating * dt)): Stationeers' Transformer.GetGeneratedPower.
            PowerTransformer transformer = generator as PowerTransformer;
            if (transformer != null)
            {
                Operational operational = transformer.GetComponent<Operational>();
                Battery input = transformer.GetComponent<Battery>();
                if (operational == null || !operational.IsOperational || input == null || input.JoulesAvailable <= 0f)
                {
                    return 0.0;
                }
                Ic10TransformerCap cap;
                return Ic10TransformerCap.Live.TryGetValue(transformer, out cap) ? cap.Cap : transformer.WattageRating;
            }
            if (!generator.IsProducingPower())
            {
                return 0.0;
            }
            return generator.WattageRating;
        }

        public static Ic10OniBuildingView Build(GameObject go)
        {
            KPrefabID id = go.GetComponent<KPrefabID>();
            Ic10OniBuildingView view = new Ic10OniBuildingView();
            view.PrefabId = id.PrefabTag.Name;
            view.InstanceId = id.InstanceID;
            view.DisplayName = () => go.GetProperName();

            // BuildingConfigManager puts a BuildingEnabledButton on the base building template,
            // and some configs take it off again (doors, the valves with their own control, and
            // everything that calls GeneratedBuildings.MakeBuildingAlwaysOperational, which is
            // every automation sensor). Those have no On. The setter is the flag the player's
            // button owns.
            BuildingEnabledButton button = go.GetComponent<BuildingEnabledButton>();
            if (button != null)
            {
                view.GetEnabled = () => button.IsEnabled;
                view.SetEnabled = value => button.IsEnabled = value;
            }

            // `d0:0`: the data networks under the building's cells.
            Building placed = go.GetComponent<Building>();
            if (placed != null)
            {
                view.Networks = () =>
                {
                    Ic10DataGrid grid = Ic10DataGrid.Current;
                    return grid == null ? null : grid.NetworksUnder(placed);
                };
            }

            Operational operational = go.GetComponent<Operational>();
            if (operational != null)
            {
                view.IsFunctional = () => operational.IsFunctional;
                view.IsActive = () => operational.IsActive;
            }

            PrimaryElement body = go.GetComponent<PrimaryElement>();
            if (body != null)
            {
                view.BodyTemperature = () => body.Temperature;
            }

            EnergyConsumer consumer = go.GetComponent<EnergyConsumer>();
            if (consumer != null)
            {
                view.IsPowered = () => consumer.IsPowered;
                view.RatedPower = () => consumer.WattsNeededWhenActive;
                view.IsWired = () => consumer.IsConnected;
                view.PowerDraw = () => consumer.WattsUsed;
            }

            Battery battery = go.GetComponent<Battery>();
            if (battery != null)
            {
                view.Charge = () => battery.JoulesAvailable;
                view.ChargeMaximum = () => battery.Capacity;
                // Battery.GetLogicValue: the load and the supply of its network.
                view.CircuitActual = () => CircuitWatts(battery.CircuitID, 0);
                view.CircuitPotential = () => CircuitWatts(battery.CircuitID, 1);
            }

            // ONI's wattage sensor is Stationeers' cable analyser: it watches the wire it
            // sits on (CableAnalyser.GetLogicValue).
            LogicWattageSensor wattage = go.GetComponent<LogicWattageSensor>();
            if (wattage != null)
            {
                System.Func<ushort> circuit = () => Game.Instance.circuitManager.GetCircuitID(Grid.PosToCell(go));
                view.CircuitActual = () => CircuitWatts(circuit(), 0);
                view.CircuitPotential = () => CircuitWatts(circuit(), 1);
                view.CircuitRequired = () => CircuitWatts(circuit(), 2);
            }

            // Watts made now: WattsMadeNow above.
            Generator generator = go.GetComponent<Generator>();
            if (generator != null)
            {
                view.PowerGeneration = () => WattsMadeNow(generator);
                SolarPanel solar = generator as SolarPanel;
                ModuleSolarPanel moduleSolar = generator as ModuleSolarPanel;
                if (solar != null)
                {
                    view.SolarOutput = view.PowerGeneration;
                    if (SolarCells.Available)
                    {
                        view.SolarPotential = () => SolarCells.Watts(solar);
                    }
                }
                else if (moduleSolar != null)
                {
                    view.SolarOutput = view.PowerGeneration;
                    if (SolarCells.Available)
                    {
                        view.SolarPotential = () => SolarCells.Watts(moduleSolar);
                    }
                }
                if (generator is PowerTransformer)
                {
                    view.TransformerRating = () => generator.WattageRating;
                }
            }

            // One of Mod 5's own logic bricks. Its number is the brick's, so the dial's
            // slider is not also mapped as a slider.
            Ic10Brick brick = go.GetComponent<Ic10Brick>();
            if (brick != null)
            {
                view.Brick = brick.Describe();
            }

            // The single slider of the side screen, found as the side screen finds it
            // (SingleSliderSideScreen.IsValidForTarget): on a component, else on a state
            // machine instance (HEPBattery), and not on the buildings the screen hides it
            // for. The clamp to the slider's range and the rounding to its decimal places
            // are in the screen (SliderSet.SetValue), not in the component, so the adapter
            // applies both. No vanilla slider uses an index above 0.
            ISliderControl slider = go.GetComponent<ISingleSliderControl>();
            if (slider == null)
            {
                slider = go.GetSMI<ISingleSliderControl>();
            }
            if (slider != null && brick == null && !SliderHidden(id))
            {
                view.Slider = new Ic10OniControl
                {
                    Get = () => slider.GetSliderValue(0),
                    Set = value => slider.SetSliderValue((float)value, 0),
                    Min = () => slider.GetSliderMin(0),
                    Max = () => slider.GetSliderMax(0),
                    Decimals = () => slider.SliderDecimalPlaces(0),
                };
            }

            // A valve's flow, in the grams per second its side screen shows
            // (ValveSideScreen), whole grams. Set at once: Valve.ChangeFlow stores the wish
            // and queues a duplicant errand, and Valve.UpdateFlow is what the errand does
            // when it ends.
            Valve valve = go.GetComponent<Valve>();
            if (valve != null && view.Slider == null)
            {
                view.Slider = new Ic10OniControl
                {
                    Get = () => valve.DesiredFlow * 1000.0,
                    Set = value =>
                    {
                        valve.ChangeFlow((float)(value / 1000.0));
                        valve.UpdateFlow();
                    },
                    Min = () => 0.0,
                    Max = () => valve.MaxFlow * 1000.0,
                    Decimals = () => 0,
                };
            }

            // A limit valve: its limit, in kilograms or units, and the amount it has let
            // through, which a write resets (LimitValve.Limit, Amount). The limit's range
            // is on the side screen (LimitValveSideScreen), 0 to maxLimitKg, whole numbers.
            LimitValve limitValve = go.GetComponent<LimitValve>();
            if (limitValve != null)
            {
                if (view.Slider == null)
                {
                    view.Slider = new Ic10OniControl
                    {
                        Get = () => limitValve.Limit,
                        Set = value => limitValve.Limit = (float)value,
                        Min = () => 0.0,
                        Max = () => limitValve.maxLimitKg,
                        Decimals = () => 0,
                    };
                }
                view.Counter = new Ic10OniControl
                {
                    Get = () => limitValve.Amount,
                    Set = value => limitValve.Amount = (float)value,
                };
            }

            // A signal counter: the count it counts to, 1 to 10 (CounterSideScreen.SetMaxCount,
            // which also lowers a count above the new top and refreshes the output), its
            // count, and advanced mode.
            LogicCounter counter = go.GetComponent<LogicCounter>();
            if (counter != null)
            {
                System.Action refresh = () =>
                {
                    counter.SetCounterState();
                    counter.UpdateLogicCircuit();
                    counter.UpdateVisualState(force: true);
                    counter.UpdateMeter();
                };
                view.Slider = new Ic10OniControl
                {
                    Get = () => counter.maxCount,
                    Set = value =>
                    {
                        int top = (int)value;
                        if (top < counter.currentCount)
                        {
                            counter.currentCount = top;
                        }
                        counter.maxCount = top;
                        refresh();
                    },
                    Min = () => 1.0,
                    Max = () => 10.0,
                    Decimals = () => 0,
                };
                view.Counter = new Ic10OniControl
                {
                    Get = () => counter.currentCount,
                    Set = value =>
                    {
                        if (value <= 0.0)
                        {
                            counter.ResetCounter();
                            return;
                        }
                        counter.currentCount = (int)System.Math.Min(value, counter.maxCount);
                        refresh();
                    },
                };
                view.Choice = new Ic10OniChoice
                {
                    Get = () => counter.advancedMode ? 1 : 0,
                    Set = value =>
                    {
                        counter.advancedMode = value != 0;
                        refresh();
                    },
                    Count = () => 2,
                };
            }

            // A ribbon reader or writer: which bit of the ribbon it works on
            // (LogicBitSelectorSideScreen.SelectToggle).
            ILogicRibbonBitSelector bits = go.GetComponent<ILogicRibbonBitSelector>();
            if (bits != null && view.Slider == null)
            {
                view.Slider = new Ic10OniControl
                {
                    Get = () => bits.GetBitSelection(),
                    Set = value =>
                    {
                        bits.SetBitSelection((int)value);
                        bits.UpdateVisuals();
                    },
                    Min = () => 0.0,
                    Max = () => bits.GetBitDepth() - 1,
                    Decimals = () => 0,
                };
            }

            // One of a few: an ice maker's product, a water cooler's drink
            // (FewOptionSideScreen), a radbolt generator's direction, a checkbox.
            if (view.Choice == null)
            {
                view.Choice = Choice(go);
            }

            // What a filter lets through or an element sensor looks for.
            Filterable filterable = go.GetComponent<Filterable>();
            if (filterable != null)
            {
                view.GetFilter = () => FilterHash(filterable.SelectedTag);
                view.SetFilter = hash => SetFilter(filterable, hash);
            }

            // A smart battery, a reservoir, a massage table: where the signal switches on and
            // off. The 0 to 100 range and the whole numbers are on the side screen
            // (ActiveRangeSideScreen).
            //
            // BatterySmart and SmartReservoir name the two the other way round: their
            // ActivateValue is the field deactivateValue, the HIGH mark where the signal goes
            // off, and their labels are swapped to match (BatterySmart.UpdateLogicCircuit;
            // found by reading). ActivateAt is where the signal comes on, so for
            // those two it is the interface's DeactivateValue. MassageTable is as named.
            IActivationRangeTarget range = go.GetComponent<IActivationRangeTarget>();
            if (range != null)
            {
                bool swapped = range is BatterySmart || range is SmartReservoir;
                System.Func<int> places = range.UseWholeNumbers ? (System.Func<int>)(() => 0) : null;
                Ic10OniControl asNamedActivate = new Ic10OniControl
                {
                    Get = () => range.ActivateValue,
                    Set = value => range.ActivateValue = (float)value,
                    Min = () => range.MinValue,
                    Max = () => range.MaxValue,
                    Decimals = places,
                };
                Ic10OniControl asNamedDeactivate = new Ic10OniControl
                {
                    Get = () => range.DeactivateValue,
                    Set = value => range.DeactivateValue = (float)value,
                    Min = () => range.MinValue,
                    Max = () => range.MaxValue,
                    Decimals = places,
                };
                view.ActivateAt = swapped ? asNamedDeactivate : asNamedActivate;
                view.DeactivateAt = swapped ? asNamedActivate : asNamedDeactivate;
            }

            // ONI's signal switch and power switch: a lever thrown at once, mapped as the IC
            // Switch brick is (LogicSwitch: Setting read, Open read and written).
            // Not a pressure or temperature switch: those derive from CircuitSwitch and so
            // are toggles too, but their Setting is their threshold, mapped below.
            IPlayerControlledToggle toggle = go.GetComponent<IPlayerControlledToggle>();
            if (toggle != null && view.Brick == null && go.GetComponent<IThresholdSwitch>() == null)
            {
                view.Brick = new Ic10OniBrick
                {
                    Kind = Ic10OniBrickKind.Switch,
                    GetSetting = () => toggle.ToggledOn() ? 1.0 : 0.0,
                    SetSetting = value =>
                    {
                        if ((value != 0.0) != toggle.ToggledOn())
                        {
                            toggle.ToggledByPlayer();
                        }
                    },
                };
            }

            IThresholdSwitch threshold = go.GetComponent<IThresholdSwitch>();
            if (threshold != null)
            {
                view.Threshold = new Ic10OniControl
                {
                    Get = () => threshold.Threshold,
                    Set = value => threshold.Threshold = (float)value,
                    Min = () => threshold.RangeMin,
                    Max = () => threshold.RangeMax,
                };
                view.GetActivateAbove = () => threshold.ActivateAboveThreshold;
                view.SetActivateAbove = value => threshold.ActivateAboveThreshold = value;
                view.Reading = () => threshold.CurrentValue;
                view.ReadingIsTemperature = threshold is LogicTemperatureSensor
                    || threshold is TemperatureControlledSwitch
                    || threshold is ConduitTemperatureSensor;
            }

            IUserControlledCapacity capacity = go.GetComponent<IUserControlledCapacity>();
            if (capacity == null)
            {
                // CapacityControlSideScreen falls back to a state machine instance (StorageTile).
                capacity = go.GetSMI<IUserControlledCapacity>();
            }
            if (capacity != null)
            {
                view.Capacity = new Ic10OniControl
                {
                    Get = () => capacity.UserMaxCapacity,
                    Set = value => capacity.UserMaxCapacity = (float)value,
                    Min = () => capacity.MinCapacity,
                    Max = () => capacity.MaxCapacity,
                };
                view.AmountStored = () => capacity.AmountStored;
            }

            // The first Storage. A building with more than one (a refinery's input and
            // output) shows only this one; which to show is not decided.
            Storage storage = go.GetComponent<Storage>();
            if (storage != null)
            {
                view.Storage = new OniStorage(storage);
            }

            Door door = go.GetComponent<Door>();
            if (door != null)
            {
                view.DoorIsOpen = () => door.IsOpen();
                view.GetDoorState = () => DoorState(door);
                // A sealed door (the ruins' security doors) is opened by a duplicant's unseal
                // errand, which its own state machine runs. Writing its control state would
                // go round that, so a sealed door can be read and not set.
                if (DoorFields.Available && door.doorType != Door.DoorType.Sealed)
                {
                    view.SetDoorState = state => DoorFields.Set(door, state);
                }
                Ic10DoorLogic logic = go.GetComponent<Ic10DoorLogic>();
                if (logic != null)
                {
                    view.GetDoorMode = () => logic.Mode;
                    view.SetDoorMode = logic.SetMode;
                    view.GetDoorSetting = () => logic.Setting;
                    view.SetDoorSetting = logic.SetSetting;
                    view.DoorOpenWritten = logic.OpenWritten;
                }
            }

            // A machine that makes things from recipes, and a planter.
            Ic10FabricatorSelect fabricator = go.GetComponent<Ic10FabricatorSelect>();
            if (fabricator != null && go.GetComponent<ComplexFabricator>() != null)
            {
                view.Fabricator = fabricator.Describe();
            }
            // The IC Sorter.
            Ic10SorterFilter sorter = go.GetComponent<Ic10SorterFilter>();
            if (sorter != null)
            {
                view.Sorter = sorter.Sorter;
            }
            // A bin's or a fridge's checklist.
            Ic10ChecklistFilter checklist = go.GetComponent<Ic10ChecklistFilter>();
            if (checklist != null && checklist.Checklist != null && view.Storage != null)
            {
                view.Checklist = checklist.Checklist;
            }
            PlantablePlot plot = go.GetComponent<PlantablePlot>();
            if (plot != null)
            {
                view.Plot = Ic10Plots.Describe(plot);
            }

            // A research station: how far the research in hand is, in this station's kind
            // of point (ResearchCenter.GetPercentComplete).
            ResearchCenter research = go.GetComponent<ResearchCenter>();
            if (research != null)
            {
                view.Progress = () => research.GetPercentComplete();
            }
            // The nuclear research center is its own class, with its own workable.
            NuclearResearchCenterWorkable nuclear = go.GetComponent<NuclearResearchCenterWorkable>();
            if (nuclear != null && view.Progress == null)
            {
                view.Progress = () => nuclear.GetPercentComplete();
            }

            // ONI-only readings.
            Ic10OniReadings.Fill(go, view);

            // The air a sensor stands in, or the pipe it sits on.
            Ic10OniAtmospheres.Fill(go, view);

            LogicPorts ports = go.GetComponent<LogicPorts>();
            if (ports != null)
            {
                if (ports.inputPortInfo != null && ports.inputPortInfo.Length > 0)
                {
                    HashedString input = ports.inputPortInfo[0].id;
                    view.LogicInput = () => ports.GetInputValue(input);
                    Ic10PortDrive drive = go.GetComponent<Ic10PortDrive>();
                    if (drive != null)
                    {
                        view.SetLogicInput = value => drive.Drive(input, value);
                    }
                }
                if (ports.outputPortInfo != null && ports.outputPortInfo.Length > 0)
                {
                    HashedString output = ports.outputPortInfo[0].id;
                    view.LogicOutput = () => ports.GetOutputValue(output);
                }
            }

            view.Declared = Declared(go);
            return view;
        }

        // What other mods declared for this building through the framework's data-device
        // registry: its prefab's describers, then its IDataDeviceSource
        // components. Null when nothing was asked.
        private static List<Ic10OniDeclared> Declared(GameObject go)
        {
            OniFramework.DataDeviceMap map = new OniFramework.DataDeviceMap();
            if (!OniFramework.DataDevices.Describe(go, map))
            {
                return null;
            }
            List<Ic10OniDeclared> declared = new List<Ic10OniDeclared>();
            foreach (OniFramework.DataDeviceValue value in map.Values)
            {
                declared.Add(new Ic10OniDeclared
                {
                    LogicType = value.LogicType,
                    Read = value.Read,
                    Readable = value.Readable,
                    Write = value.Write,
                    Hidden = value.Hidden,
                });
            }
            return declared;
        }

        private static Ic10OniChoice Choice(GameObject go)
        {
            // Not the food rehydrator (DehydratedManager): its options are the dried foods
            // discovered so far, in no fixed order, so a number would not name one.
            FewOptionSideScreen.IFewOptionSideScreen few = go.GetComponent<FewOptionSideScreen.IFewOptionSideScreen>();
            if (few != null && few.GetType().Name != "DehydratedManager")
            {
                // GetOptions builds the screen's sprites and tooltips on every call; the
                // options themselves do not change, so they are asked for once.
                FewOptionSideScreen.IFewOptionSideScreen.Option[] options = null;
                System.Func<FewOptionSideScreen.IFewOptionSideScreen.Option[]> all = () => options ?? (options = few.GetOptions());
                return new Ic10OniChoice
                {
                    Get = () =>
                    {
                        Tag selected = few.GetSelectedOption();
                        FewOptionSideScreen.IFewOptionSideScreen.Option[] list = all();
                        for (int i = 0; i < list.Length; i++)
                        {
                            if (list[i].tag == selected)
                            {
                                return i;
                            }
                        }
                        return 0;
                    },
                    Set = value =>
                    {
                        FewOptionSideScreen.IFewOptionSideScreen.Option[] list = all();
                        if (value >= 0 && value < list.Length && list[value].tag != few.GetSelectedOption())
                        {
                            few.OnOptionSelected(list[value]);
                        }
                    },
                    Count = () => all().Length,
                };
            }

            // HighEnergyParticleDirectionSideScreen.IsValidForTarget: a redirector only when
            // its direction can be set. EightDirection, 0 Up, then anticlockwise by 45 degrees.
            IHighEnergyParticleDirection direction = go.GetComponent<IHighEnergyParticleDirection>();
            HighEnergyParticleRedirector redirector = go.GetComponent<HighEnergyParticleRedirector>();
            if (direction != null && (redirector == null || redirector.directionControllable))
            {
                return new Ic10OniChoice
                {
                    Get = () => (int)direction.Direction,
                    Set = value => direction.Direction = (EightDirection)value,
                    Count = () => 8,
                };
            }

            // SingleCheckboxSideScreen: a component, else a state machine instance (the
            // telescope's meteor identification, a critter drop-off's auto-wrangle).
            ICheckboxControl checkbox = go.GetComponent<ICheckboxControl>();
            if (checkbox == null)
            {
                checkbox = go.GetSMI<ICheckboxControl>();
            }
            if (checkbox != null)
            {
                return new Ic10OniChoice
                {
                    Get = () => checkbox.GetCheckboxValue() ? 1 : 0,
                    Set = value => checkbox.SetCheckboxValue(value != 0),
                    Count = () => 2,
                };
            }

            // A conveyor loader or receptacle: 1 is "automation only", duplicants keep off
            // (Automatable; the side screen's box is the opposite, "allow manual use").
            Automatable automatable = go.GetComponent<Automatable>();
            if (automatable != null)
            {
                return new Ic10OniChoice
                {
                    Get = () => automatable.GetAutomationOnly() ? 1 : 0,
                    Set = value => automatable.SetAutomationOnly(value != 0),
                    Count = () => 2,
                };
            }
            return null;
        }

        // SingleSliderSideScreen.IsValidForTarget: the generators' refill slider is not shown
        // for these, so it is not a setting the player has.
        private static bool SliderHidden(KPrefabID id)
        {
            return id.IsPrefabID("HydrogenGenerator".ToTag()) || id.IsPrefabID("MethaneGenerator".ToTag())
                || id.IsPrefabID("PetroleumGenerator".ToTag()) || id.IsPrefabID("DevGenerator".ToTag())
                || id.HasTag(GameTags.DeadReactor);
        }

        private static readonly Dictionary<Tag, int> TagHashes = new Dictionary<Tag, int>();

        // Ic10Math.Hash of a tag's name; 0 for no tag and for Void, which is "nothing chosen".
        private static int FilterHash(Tag tag)
        {
            if (!tag.IsValid || tag == GameTags.Void)
            {
                return 0;
            }
            int hash;
            if (!TagHashes.TryGetValue(tag, out hash))
            {
                hash = Ic10Math.Hash(tag.Name);
                TagHashes[tag] = hash;
            }
            return hash;
        }

        // The option of this filter whose id hashes to the number, if it has one; 0 is Void.
        // The options are the side screen's own list (Filterable.GetTagOptions).
        private static void SetFilter(Filterable filterable, int hash)
        {
            if (hash == FilterHash(filterable.SelectedTag))
            {
                return;
            }
            if (hash == 0)
            {
                filterable.SelectedTag = GameTags.Void;
                return;
            }
            foreach (KeyValuePair<Tag, HashSet<Tag>> category in filterable.GetTagOptions())
            {
                foreach (Tag option in category.Value)
                {
                    if (FilterHash(option) == hash)
                    {
                        filterable.SelectedTag = option;
                        return;
                    }
                }
            }
        }

        // Door.RequestedState is NumStates until the door has spawned and settled it to the
        // current state; the two agree whenever nothing is pending.
        private static Ic10OniDoorState DoorState(Door door)
        {
            Door.ControlState state = door.RequestedState;
            if (state == Door.ControlState.NumStates)
            {
                state = door.CurrentState;
            }
            return (Ic10OniDoorState)(int)state;
        }

        // Sets a door's state the way Door.OnLogicValueChanged does: cancel a pending
        // duplicant errand, write the requested state, and let Door.Sim200ms apply it. That
        // method only knows Opened and Locked, and the three fields are private, hence the
        // field access. If a game update renames one, doors become read-only instead of the
        // mod failing to load.
        private static class DoorFields
        {
            private static readonly AccessTools.FieldRef<Door, Door.ControlState> Requested;
            private static readonly AccessTools.FieldRef<Door, bool> ApplyLogicChange;
            private static readonly AccessTools.FieldRef<Door, Chore> ChangeStateChore;

            public static readonly bool Available;

            static DoorFields()
            {
                try
                {
                    Requested = AccessTools.FieldRefAccess<Door, Door.ControlState>("requestedState");
                    ApplyLogicChange = AccessTools.FieldRefAccess<Door, bool>("applyLogicChange");
                    ChangeStateChore = AccessTools.FieldRefAccess<Door, Chore>("changeStateChore");
                    Available = true;
                }
                catch (Exception e)
                {
                    Mod5Log.Error("a door's state fields were not found, so Open and Lock are read only: " + e.Message);
                }
            }

            public static void Set(Door door, Ic10OniDoorState state)
            {
                Chore chore = ChangeStateChore(door);
                if (chore != null)
                {
                    chore.Cancel("Change state");
                    ChangeStateChore(door) = null;
                    door.GetComponent<KSelectable>().RemoveStatusItem(Db.Get().BuildingStatusItems.ChangeDoorControlState);
                }
                Requested(door) = (Door.ControlState)(int)state;
                ApplyLogicChange(door) = true;
            }
        }

        // What the light on a solar panel could make now, in watts: the sum the panel itself
        // takes in EnergySim200ms, whether or not it is running. The cells it reads are a
        // private field of each class, hence the field access; if a game update renames one,
        // Maximum answers with the panel's output and the mod still loads.
        private static class SolarCells
        {
            // ModuleSolarPanel.EnergySim200ms: clamped to 60 W, and 60 W outright while the
            // rocket is off the grid. The literals are in the method, not in a constant.
            private const float ModuleMaxWatts = 60f;

            private static readonly AccessTools.FieldRef<SolarPanel, CellOffset[]> Panel;
            private static readonly AccessTools.FieldRef<ModuleSolarPanel, CellOffset[]> Module;

            public static readonly bool Available;

            static SolarCells()
            {
                try
                {
                    Panel = AccessTools.FieldRefAccess<SolarPanel, CellOffset[]>("solarCellOffsets");
                    Module = AccessTools.FieldRefAccess<ModuleSolarPanel, CellOffset[]>("solarCellOffsets");
                    Available = true;
                }
                catch (Exception e)
                {
                    Mod5Log.Error("a solar panel's light cells were not found, so its Maximum reads its output: " + e.Message);
                }
            }

            public static double Watts(SolarPanel panel)
            {
                return Sum(Grid.PosToCell(panel), Panel(panel), SolarPanelConfig.MAX_WATTS);
            }

            public static double Watts(ModuleSolarPanel panel)
            {
                int cell = Grid.PosToCell(panel);
                if (!Grid.IsValidCell(cell) || Grid.WorldIdx[cell] == byte.MaxValue)
                {
                    return ModuleMaxWatts;
                }
                return Sum(cell, Module(panel), ModuleMaxWatts);
            }

            private static double Sum(int origin, CellOffset[] offsets, float cap)
            {
                float watts = 0f;
                foreach (CellOffset offset in offsets)
                {
                    int cell = Grid.OffsetCell(origin, offset);
                    if (Grid.IsValidCell(cell))
                    {
                        watts += Grid.LightIntensity[cell] * SolarPanelConfig.WATTS_PER_LUX;
                    }
                }
                return Mathf.Clamp(watts, 0f, cap);
            }
        }

        // A Storage's items by position. Storage.items is the list the Storage itself keeps.
        private sealed class OniStorage : IIc10OniStorage
        {
            private static readonly Dictionary<Tag, int> PrefabHashes = new Dictionary<Tag, int>();
            private static readonly Dictionary<SimHashes, int> ElementHashes = new Dictionary<SimHashes, int>();

            private readonly Storage _storage;

            public OniStorage(Storage storage)
            {
                _storage = storage;
            }

            public int Count
            {
                get { return _storage.items.Count; }
            }

            public int PrefabHash(int index)
            {
                GameObject item = _storage.items[index];
                KPrefabID id = item == null ? null : item.GetComponent<KPrefabID>();
                if (id == null)
                {
                    return 0;
                }
                int hash;
                if (!PrefabHashes.TryGetValue(id.PrefabTag, out hash))
                {
                    hash = Ic10Math.Hash(id.PrefabTag.Name);
                    PrefabHashes[id.PrefabTag] = hash;
                }
                return hash;
            }

            public int ElementHash(int index)
            {
                PrimaryElement element = Element(index);
                if (element == null)
                {
                    return 0;
                }
                int hash;
                if (!ElementHashes.TryGetValue(element.ElementID, out hash))
                {
                    hash = Ic10Math.Hash(element.ElementID.ToString());
                    ElementHashes[element.ElementID] = hash;
                }
                return hash;
            }

            public double Mass(int index)
            {
                PrimaryElement element = Element(index);
                return element == null ? 0.0 : element.Mass;
            }

            public double Temperature(int index)
            {
                PrimaryElement element = Element(index);
                return element == null ? 0.0 : element.Temperature;
            }

            public bool IsFluid(int index)
            {
                PrimaryElement element = Element(index);
                return element != null && (element.Element.IsGas || element.Element.IsLiquid);
            }

            // Element.specificHeatCapacity is DTU per gram per kelvin, so kilograms times it
            // times 1000 is joules per kelvin.
            public double HeatCapacity(int index)
            {
                PrimaryElement element = Element(index);
                return element == null ? 0.0 : (double)element.Mass * element.Element.specificHeatCapacity * 1000.0;
            }

            public double Moles(int index)
            {
                PrimaryElement element = Element(index);
                return element == null ? 0.0 : Ic10OniAtmospheres.Moles(element.Element, element.Mass);
            }

            private PrimaryElement Element(int index)
            {
                GameObject item = _storage.items[index];
                return item == null ? null : item.GetComponent<PrimaryElement>();
            }
        }
    }
}
