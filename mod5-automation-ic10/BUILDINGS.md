# What each building answers to a chip

Generated from the game's building definitions.

Every building answers `PrefabHash`, `NameHash`, `ReferenceId` and `BodyTemperature`; those are left
out below. The first column is the id to give `HASH("...")`. `(write)` marks what a chip can set.
[`IC10-REFERENCE.md`](IC10-REFERENCE.md) says what each name means. Every building with a gas or liquid pipe port also
answers the `...Input` and `...Output` readings of the pipes there (`PressureInput`,
`TemperatureOutput` and the rest); the matrix this is made from does not list ports, so those
are not repeated below.

This table is read off the game's building configs, not measured in the game: it lists what the
mod should answer for the parts each building is made of. A part a building gains while the game
runs is not seen, and a control the game hides on one building may be listed here.

| Building | Answers | Needs |
|---|---|---|
| `AdvancedApothecary` | `On`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr` | Spaced Out |
| `AdvancedCraftingTable` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) | DLC3 |
| `AdvancedDoctorStation` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `AdvancedResearchCenter` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `CompletionRatio`; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `AirborneCreatureLure` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `AirBorneCritterCondo` | `On`; `Error`, `Activate` |  |
| `AirConditioner` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `AirFilter` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `AlgaeDistillery` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `AlgaeHabitat` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate` |  |
| `Apothecary` | `On`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `ArcadeMachine` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) |  |
| `ArtifactAnalysisStation` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) | Spaced Out |
| `ArtifactCargoBay` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `AstronautTrainingCenter` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) |  |
| `AtomicGarden` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `AutoMiner` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) |  |
| `Battery` | `On`; `Error`, `Activate`; `Charge`, `Maximum`, `Ratio`, `PowerActual`, `PowerPotential` |  |
| `BatteryMedium` | `On`; `Error`, `Activate`; `Charge`, `Maximum`, `Ratio`, `PowerActual`, `PowerPotential` |  |
| `BatteryModule` | `On`; `Error`, `Activate`; `Charge`, `Maximum`, `Ratio`, `PowerActual`, `PowerPotential`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `BatterySmart` | `On`; `Error`, `Activate`; `Charge`, `Maximum`, `Ratio`, `PowerActual`, `PowerPotential`; `ActivateAt`, `DeactivateAt` (write); `LogicOutput` |  |
| `BeachChair` | `On`; `Error`, `Activate` |  |
| `Bed` | `On`; `Error`, `Activate` |  |
| `BiodieselEngine` | `On`; `Error`, `Activate`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | forbidden with Spaced Out |
| `BiodieselEngineCluster` | `On`; `Error`, `Activate`; `PowerGeneration`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `BottleEmptier` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize` |  |
| `BottleEmptierConduitGas` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize` |  |
| `BottleEmptierConduitLiquid` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize` |  |
| `BottleEmptierGas` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize` |  |
| `BunkerDoor` | `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Open` (write), `Lock` (write), `OpenNow`; `LogicInput` (write) |  |
| `Campfire` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate` | DLC2 |
| `Canvas` | `On`; `Error`, `Activate` |  |
| `CanvasTall` | `On`; `Error`, `Activate` |  |
| `CanvasWide` | `On`; `Error`, `Activate` |  |
| `CargoBay` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | forbidden with Spaced Out |
| `CargoBayCluster` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `CeilingFossilSculpture` | `On`; `Error`, `Activate` | DLC4 |
| `CeilingLight` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) |  |
| `Checkpoint` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) |  |
| `ChemicalRefinery` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `Chlorinator` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `ClothingAlterationStation` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `ClothingFabricator` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `ClusterTelescope` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `Mode` (its checkbox, write); `LogicInput` (write) | Spaced Out |
| `ClusterTelescopeEnclosed` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `Mode` (its checkbox, write); `LogicInput` (write) | Spaced Out |
| `CO2Engine` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `CO2Scrubber` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `CometDetector` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicOutput` |  |
| `CommandModule` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch`; `LogicInput` (write); `LogicOutput` | forbidden with Spaced Out |
| `Compost` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate` |  |
| `ContactConductivePipeBridge` | `On`; `Error`, `Activate` |  |
| `CookingStation` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `CornerMoulding` | `On`; `Error`, `Activate` |  |
| `CosmicResearchCenter` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `CompletionRatio`; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) | forbidden with Spaced Out |
| `CraftingTable` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `CreatureAirTrap` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write); `LogicOutput` |  |
| `CreatureDeliveryPoint` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `Mode` (its checkbox, write) |  |
| `CreatureFeeder` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize` |  |
| `CreatureGroundTrap` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write); `LogicOutput` |  |
| `CreatureTrap` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `CrewCapsule` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `CritterCondo` | `On`; `Error`, `Activate` |  |
| `CritterDropOff` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicInput` (write) |  |
| `CritterPickUp` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `Mode` (its checkbox, write); `LogicInput` (write) |  |
| `CrownMoulding` | `On`; `Error`, `Activate` |  |
| `DataMiner` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) | DLC3 |
| `DecontaminationShower` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held | Spaced Out |
| `Deepfryer` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) | DLC2 |
| `Desalinator` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `DevGenerator` | `On`; `Error`, `Activate`; `PowerGeneration` |  |
| `DevHeater` | `On`; `Error`, `Activate` |  |
| `DevHEPSpawner` | `On`; `Error`, `Activate`; `Setting` (its slider, write), `Maximum`, `Ratio`; `Mode` (direction, 0 up, then by 45 degrees, write); `LogicInput` (write) | Spaced Out |
| `DevLifeSupport` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `DevLightGenerator` | `On`; `Error`, `Activate`; `Setting` (its slider, write), `Maximum`, `Ratio` |  |
| `DevPumpGas` | `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `DevPumpLiquid` | `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `DevPumpSolid` | `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `DevRadiationGenerator` | `On`; `Error`, `Activate`; `Setting` (its slider, write), `Maximum`, `Ratio` |  |
| `DiamondPress` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write); `LogicOutput` | Spaced Out |
| `DiningTable` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `DLC1CosmicResearchCenter` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `CompletionRatio`; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) | Spaced Out |
| `DoctorStation` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `Door` | `Error`, `Activate`; `Open` (write), `Lock` (write), `OpenNow`; `LogicInput` (write) |  |
| `EggCracker` | `On`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `EggIncubator` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `ElectrobankCharger` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) | DLC3 |
| `Electrolyzer` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `EspressoMachine` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `EthanolDistillery` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `ExobaseHeadquarters` | `On`; `Error`, `Activate` | Spaced Out |
| `FabricatedWoodMaker` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `FacilityBackWallWindow` | `On`; `Error`, `Activate` |  |
| `FarmStation` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicInput` (write) |  |
| `FarmTile` | `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; plant as slot 0 (`Mature`, `MaturityRatio`, `Health`), `Harvest` (write), `AutoHarvest` (write) |  |
| `FertilizerMaker` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `FishDeliveryPoint` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicInput` (write) |  |
| `FishFeeder` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize` |  |
| `FishPickUp` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `Mode` (its checkbox, write); `LogicInput` (write) |  |
| `FishTrap` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `FloorLamp` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) |  |
| `FloorSwitch` | `Setting` (threshold, write), `Mode` (above or below), `Reading`; `LogicOutput` |  |
| `FlowerVase` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; plant as slot 0 (`Mature`, `MaturityRatio`, `Health`), `Harvest` (write), `AutoHarvest` (write) |  |
| `FlowerVaseHanging` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; plant as slot 0 (`Mature`, `MaturityRatio`, `Health`), `Harvest` (write), `AutoHarvest` (write) |  |
| `FlowerVaseHangingFancy` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; plant as slot 0 (`Mature`, `MaturityRatio`, `Health`), `Harvest` (write), `AutoHarvest` (write) |  |
| `FlowerVaseWall` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; plant as slot 0 (`Mature`, `MaturityRatio`, `Health`), `Harvest` (write), `AutoHarvest` (write) |  |
| `FlushToilet` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `FlyingCreatureBait` | `On`; `Error`, `Activate` |  |
| `FoodDehydrator` | `On`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `FoodRehydrator` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `FossilDig` | `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr` |  |
| `FossilSculpture` | `On`; `Error`, `Activate` | DLC4 |
| `Gantry` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) |  |
| `GasBottler` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `GasCargoBay` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | forbidden with Spaced Out |
| `GasCargoBayCluster` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `GasCargoBaySmall` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `GasConduitDiseaseSensor` | `Setting` (threshold, write), `Mode` (above or below), `Reading`; `Pressure`, `TotalMoles`, the `Ratio...` types, `Temperature`, `Volume`, `VolumeOfLiquid`, `NetworkFault`, `lr` contents; `LogicOutput` |  |
| `GasConduitElementSensor` | `Filter` (write); `Pressure`, `TotalMoles`, the `Ratio...` types, `Temperature`, `Volume`, `VolumeOfLiquid`, `NetworkFault`, `lr` contents; `LogicOutput` |  |
| `GasConduitTemperatureSensor` | `Setting` (threshold, write), `Mode` (above or below), `Reading`; `Pressure`, `TotalMoles`, the `Ratio...` types, `Temperature`, `Volume`, `VolumeOfLiquid`, `NetworkFault`, `lr` contents; `LogicOutput` |  |
| `GasFilter` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Filter` (write); `LogicInput` (write) |  |
| `GasLimitValve` | `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Setting` (limit, write), `Maximum`, `Ratio`, `Quantity` (passed; write resets); `LogicInput` (write); `LogicOutput` |  |
| `GasLogicValve` | `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write); `On` drives its automation input |  |
| `GasMiniPump` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `GasPump` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `GasReservoir` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ActivateAt`, `DeactivateAt` (write); `LogicOutput` |  |
| `GasValve` | `Setting` (grams per second, write), `Maximum`, `Ratio` |  |
| `GasVent` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `VentState`; `LogicInput` (write) |  |
| `GasVentHighPressure` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `VentState`; `LogicInput` (write) |  |
| `Generator` | `On`; `Error`, `Activate`; `PowerGeneration`; `Setting` (its slider, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `GenericFabricator` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `GeneticAnalysisStation` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) | Spaced Out |
| `GeoTuner` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write); `LogicOutput` |  |
| `GlassCeilingLight` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) | DLC5 |
| `GlassForge` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `GourmetCookingStation` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `Grave` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `GravitasBathroomStall` | `On`; `Error`, `Activate` |  |
| `GravitasContainer` | `On`; `Error`, `Activate` |  |
| `GravitasCreatureManipulator` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `GravitasDoor` | `Error`, `Activate`; `Open` (write), `Lock` (write), `OpenNow`; `LogicInput` (write) |  |
| `GravitasLabLight` | `On`; `Error`, `Activate` |  |
| `GravitasPedestal` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held | Spaced Out |
| `GunkEmptier` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held | DLC3 |
| `HabitatModuleMedium` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `HabitatModuleSmall` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `HandSanitizer` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `Headquarters` | `On`; `Error`, `Activate` |  |
| `HeatCompressor` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `HEPBattery` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Setting` (its slider, write), `Maximum`, `Ratio`; `LogicInput` (write); `LogicOutput` | Spaced Out |
| `HEPBridgeTile` | `Mode` (direction, 0 up, then by 45 degrees, write) | Spaced Out |
| `HEPEngine` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch`; `LogicOutput` | Spaced Out |
| `HighEnergyParticleRedirector` | `On`; `Error`, `Activate`; `Mode` (direction, 0 up, then by 45 degrees, write); `LogicInput` (write) | Spaced Out |
| `HighEnergyParticleSpawner` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Setting` (its slider, write), `Maximum`, `Ratio`; `Mode` (direction, 0 up, then by 45 degrees, write); `LogicInput` (write) | Spaced Out |
| `HijackedHeadquarters` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `HotTub` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `HydrogenEngine` | `On`; `Error`, `Activate`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | forbidden with Spaced Out |
| `HydrogenEngineCluster` | `On`; `Error`, `Activate`; `PowerGeneration`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `HydrogenGenerator` | `On`; `Error`, `Activate`; `PowerGeneration`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `HydroponicFarm` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; plant as slot 0 (`Mature`, `MaturityRatio`, `Health`), `Harvest` (write), `AutoHarvest` (write) |  |
| `IceCooledFan` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `IceKettle` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held | DLC2 |
| `IceMachine` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `Mode` (its option, write); `LogicInput` (write) |  |
| `IceSculpture` | `On`; `Error`, `Activate` |  |
| `InsulatedDoor` | `Error`, `Activate`; `Open` (write), `Lock` (write), `OpenNow` |  |
| `ItemPedestal` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `JetSuitLocker` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `JetSuitMarker` | `On`; `Error`, `Activate`; `LogicInput` (write) |  |
| `Juicer` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `KeroseneEngine` | `On`; `Error`, `Activate`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | forbidden with Spaced Out |
| `KeroseneEngineCluster` | `On`; `Error`, `Activate`; `PowerGeneration`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `KeroseneEngineClusterSmall` | `On`; `Error`, `Activate`; `PowerGeneration`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `Kiln` | `On`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `LadderBed` | `On`; `Error`, `Activate` | Spaced Out |
| `LandingBeacon` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) | Spaced Out |
| `LargeBackwallFarm` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; plant as slot 0 (`Mature`, `MaturityRatio`, `Health`), `Harvest` (write), `AutoHarvest` (write) | DLC5 |
| `LargeElectrobankDischarger` | `On`; `Error`, `Activate`; `PowerGeneration`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicInput` (write) | DLC3 |
| `LaunchPad` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch`; `LogicInput` (write); `LogicOutput` | Spaced Out |
| `LeadSuitLocker` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) | Spaced Out |
| `LeadSuitMarker` | `On`; `Error`, `Activate`; `LogicInput` (write) | Spaced Out |
| `LiquidBottler` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `LiquidCargoBay` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | forbidden with Spaced Out |
| `LiquidCargoBayCluster` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `LiquidCargoBaySmall` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `LiquidConditioner` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `LiquidConduitDiseaseSensor` | `Setting` (threshold, write), `Mode` (above or below), `Reading`; `Pressure`, `TotalMoles`, the `Ratio...` types, `Temperature`, `Volume`, `VolumeOfLiquid`, `NetworkFault`, `lr` contents; `LogicOutput` |  |
| `LiquidConduitElementSensor` | `Filter` (write); `Pressure`, `TotalMoles`, the `Ratio...` types, `Temperature`, `Volume`, `VolumeOfLiquid`, `NetworkFault`, `lr` contents; `LogicOutput` |  |
| `LiquidConduitTemperatureSensor` | `Setting` (threshold, write), `Mode` (above or below), `Reading`; `Pressure`, `TotalMoles`, the `Ratio...` types, `Temperature`, `Volume`, `VolumeOfLiquid`, `NetworkFault`, `lr` contents; `LogicOutput` |  |
| `LiquidCooledFan` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `LiquidFilter` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Filter` (write); `LogicInput` (write) |  |
| `LiquidFuelTank` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | forbidden with Spaced Out |
| `LiquidFuelTankCluster` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `LiquidHeater` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Setting` (its slider, write), `Maximum`, `Ratio`; `LogicInput` (write) |  |
| `LiquidLimitValve` | `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Setting` (limit, write), `Maximum`, `Ratio`, `Quantity` (passed; write resets); `LogicInput` (write); `LogicOutput` |  |
| `LiquidLogicValve` | `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write); `On` drives its automation input |  |
| `LiquidMiniPump` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `LiquidPump` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `LiquidPumpingStation` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `LiquidReservoir` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ActivateAt`, `DeactivateAt` (write); `LogicOutput` |  |
| `LiquidValve` | `Setting` (grams per second, write), `Maximum`, `Ratio` |  |
| `LiquidVent` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `VentState`; `LogicInput` (write) |  |
| `LogicAlarm` | `LogicInput` (write); `On` drives its automation input |  |
| `LogicClusterLocationSensor` | `LogicOutput` | Spaced Out |
| `LogicCounter` | `Setting` (top count, write), `Quantity` (count, write), `Mode` (advanced); `LogicInput` (write); `LogicOutput` |  |
| `LogicCritterCountSensor` | `Setting` (threshold, write), `Mode` (above or below), `Reading`; `LogicOutput` |  |
| `LogicDiseaseSensor` | `Setting` (threshold, write), `Mode` (above or below), `Reading`; `LogicOutput` |  |
| `LogicDuplicantSensor` | `LogicOutput` |  |
| `LogicElementSensorGas` | `On`; `Error`, `Activate`; `Filter` (write); `Pressure`, `TotalMoles`, the `Ratio...` types, `Temperature`, `VolumeOfLiquid`, `lr` contents; `LogicOutput` |  |
| `LogicElementSensorLiquid` | `On`; `Error`, `Activate`; `Filter` (write); `Pressure`, `TotalMoles`, the `Ratio...` types, `Temperature`, `VolumeOfLiquid`, `lr` contents; `LogicOutput` |  |
| `LogicGateBUFFER` | `Setting` (its slider, write), `Maximum`, `Ratio` |  |
| `LogicGateFILTER` | `Setting` (its slider, write), `Maximum`, `Ratio` |  |
| `LogicHammer` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) |  |
| `LogicHEPSensor` | `Setting` (threshold, write), `Mode` (above or below), `Reading`; `LogicOutput` | Spaced Out |
| `LogicInterasteroidReceiver` | `On`; `Error`, `Activate`; `LogicOutput` | Spaced Out |
| `LogicInterasteroidSender` | `On`; `Error`, `Activate`; `LogicInput` (write) | Spaced Out |
| `LogicLightSensor` | `Setting` (threshold, write), `Mode` (above or below), `Reading`; `LogicOutput` |  |
| `LogicPowerRelay` | `Error`, `Activate`; `Open` (write), `Setting` (its lever); `LogicInput` (write) |  |
| `LogicPressureSensorGas` | `Setting` (threshold, write), `Mode` (above or below), `Reading`; `Pressure`, `TotalMoles`, the `Ratio...` types, `Temperature`, `VolumeOfLiquid`, `lr` contents; `LogicOutput` |  |
| `LogicPressureSensorLiquid` | `Setting` (threshold, write), `Mode` (above or below), `Reading`; `Pressure`, `TotalMoles`, the `Ratio...` types, `Temperature`, `VolumeOfLiquid`, `lr` contents; `LogicOutput` |  |
| `LogicRadiationSensor` | `Setting` (threshold, write), `Mode` (above or below), `Reading`; `LogicOutput` | Spaced Out |
| `LogicRibbonBridge` | `LogicInput` (write) |  |
| `LogicRibbonReader` | `Setting` (bit, write); `LogicInput` (write); `LogicOutput` |  |
| `LogicRibbonWriter` | `Setting` (bit, write); `LogicInput` (write); `LogicOutput` |  |
| `LogicSwitch` | `Open` (write), `Setting` (its lever); `LogicOutput` |  |
| `LogicTemperatureSensor` | `Setting` (threshold, write), `Mode` (above or below), `Reading`; `Pressure`, `TotalMoles`, the `Ratio...` types, `Temperature`, `VolumeOfLiquid`, `lr` contents; `LogicOutput` |  |
| `LogicTimeOfDaySensor` | `Cycle`, `CycleTime`, `Night`; `LogicOutput` |  |
| `LogicTimerSensor` | `LogicOutput` |  |
| `LogicWattageSensor` | `PowerActual`, `PowerPotential`, `PowerRequired`; `Setting` (threshold, write), `Mode` (above or below), `Reading`; `LogicOutput` |  |
| `LogicWireBridge` | `LogicInput` (write) |  |
| `LonelyMailBox` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `LonelyMinionHouse` | `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `LuxuryBed` | `On`; `Error`, `Activate` |  |
| `MachineShop` | `On`; `Error`, `Activate` |  |
| `ManualGenerator` | `On`; `Error`, `Activate`; `PowerGeneration`; `Setting` (its slider, write), `Maximum`, `Ratio`; `LogicInput` (write) |  |
| `ManualHighEnergyParticleSpawner` | `On`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `Mode` (direction, 0 up, then by 45 degrees, write); `LogicInput` (write) | Spaced Out |
| `ManualPressureDoor` | `Error`, `Activate`; `Open` (write), `Lock` (write), `OpenNow` |  |
| `MarbleSculpture` | `On`; `Error`, `Activate` |  |
| `MassageTable` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `ActivateAt`, `DeactivateAt` (write); `LogicInput` (write) |  |
| `MassiveHeatSink` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate` |  |
| `MechanicalSurfboard` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `MedicalCot` | `On`; `Error`, `Activate`; `Setting` (its slider, write), `Maximum`, `Ratio` |  |
| `MegaBrainTank` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate` |  |
| `MercuryCeilingLight` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) | DLC2 |
| `MetalRefinery` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `MetalSculpture` | `On`; `Error`, `Activate` |  |
| `MethaneGenerator` | `On`; `Error`, `Activate`; `PowerGeneration`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `MicrobeMusher` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `MilkFatSeparator` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `MilkFeeder` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `MilkingStation` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `MilkPress` | `On`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `MineralDeoxidizer` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `MiniFridge` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicOutput` | DLC5 |
| `MissileFabricator` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `MissileLauncher` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicInput` (write) |  |
| `MissionControl` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) | forbidden with Spaced Out |
| `MissionControlCluster` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) | Spaced Out |
| `ModularLaunchpadPortBridge` | `On`; `Error`, `Activate` | Spaced Out |
| `ModularLaunchpadPortGas` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicInput` (write) | Spaced Out |
| `ModularLaunchpadPortGasUnloader` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicInput` (write) | Spaced Out |
| `ModularLaunchpadPortLiquid` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicInput` (write) | Spaced Out |
| `ModularLaunchpadPortLiquidUnloader` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicInput` (write) | Spaced Out |
| `ModularLaunchpadPortSolid` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicInput` (write) | Spaced Out |
| `ModularLaunchpadPortSolidUnloader` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicInput` (write) | Spaced Out |
| `MonumentBottom` | `On`; `Error`, `Activate` |  |
| `MonumentMiddle` | `On`; `Error`, `Activate` |  |
| `MonumentTop` | `On`; `Error`, `Activate` |  |
| `MorbRoverMaker` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `MultiMinionDiningTable` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `NoseconeBasic` | `On`; `Error`, `Activate`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `NoseconeHarvest` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `NuclearReactor` | `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) | Spaced Out |
| `NuclearResearchCenter` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `CompletionRatio`; `LogicInput` (write); `LogicOutput` | Spaced Out |
| `ObjectDispenser` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicInput` (write); `Open` drives its automation input |  |
| `OilChanger` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) | DLC3 |
| `OilRefinery` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `OilWellCap` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Setting` (its slider, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `OrbitalCargoModule` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `OrbitalResearchCenter` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `CompletionRatio`; `LogicInput` (write) | Spaced Out |
| `OreScrubber` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `Outhouse` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `FlushesLeft` |  |
| `OxidizerTank` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | forbidden with Spaced Out |
| `OxidizerTankCluster` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `OxidizerTankLiquid` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | forbidden with Spaced Out |
| `OxidizerTankLiquidCluster` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `OxygenMaskLocker` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `OxygenMaskMarker` | `On`; `Error`, `Activate`; `LogicInput` (write) |  |
| `OxygenMaskStation` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicInput` (write) |  |
| `OxyliteRefinery` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `Oxysconce` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held | DLC2 |
| `ParkSign` | `On`; `Error`, `Activate` |  |
| `PeatGenerator` | `On`; `Error`, `Activate`; `PowerGeneration`; `Setting` (its slider, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) | DLC4 |
| `PetroleumGenerator` | `On`; `Error`, `Activate`; `PowerGeneration`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `Phonobox` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) |  |
| `PioneerModule` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `PixelPack` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) |  |
| `PlanterBox` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; plant as slot 0 (`Mature`, `MaturityRatio`, `Health`), `Harvest` (write), `AutoHarvest` (write) |  |
| `POIBunkerExteriorDoor` | `On`; `Error`, `Activate`; `Open` (write), `Lock` (write), `OpenNow` |  |
| `POIDlc2ShowroomDoor` | `On`; `Error`, `Activate`; `Open` (write), `Lock` (write), `OpenNow` |  |
| `POIDoorInternal` | `Error`, `Activate`; `Open` (write), `Lock` (write), `OpenNow`; `LogicInput` (write) |  |
| `POIFacilityDoor` | `On`; `Error`, `Activate`; `Open` (write), `Lock` (write), `OpenNow` |  |
| `Polymerizer` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `PowerControlStation` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicInput` (write) |  |
| `PowerTransformer` | `On`; `Error`, `Activate`; `Setting` (watt limit, write), `Maximum`, `Ratio`; `LogicInput` (write) |  |
| `PowerTransformerSmall` | `On`; `Error`, `Activate`; `Setting` (watt limit, write), `Maximum`, `Ratio`; `LogicInput` (write) |  |
| `PressureDoor` | `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Open` (write), `Lock` (write), `OpenNow`; `LogicInput` (write) |  |
| `PressureSwitchGas` | `Setting` (threshold, write), `Mode` (above or below), `Reading` |  |
| `PressureSwitchLiquid` | `Setting` (threshold, write), `Mode` (above or below), `Reading` |  |
| `PropBeachChair` | `On`; `Error`, `Activate` | DLC5 |
| `PropGravitasLabWall` | `On`; `Error`, `Activate` |  |
| `PropGravitasLabWindow` | `On`; `Error`, `Activate` |  |
| `PropGravitasLabWindowHorizontal` | `On`; `Error`, `Activate` | Spaced Out |
| `PropGravitasWall` | `On`; `Error`, `Activate` |  |
| `PropGravitasWallPurple` | `On`; `Error`, `Activate` |  |
| `PropGravitasWallPurpleWhiteDiagonal` | `On`; `Error`, `Activate` |  |
| `RadiationLight` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) | Spaced Out |
| `RailGun` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write); `LogicOutput` | Spaced Out |
| `RailGunPayloadOpener` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) | Spaced Out |
| `RanchStation` | `On`; `Error`, `Activate`; `LogicInput` (write) |  |
| `RationBox` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize` |  |
| `ReefGenerator` | `On`; `Error`, `Activate`; `PowerGeneration`; `LogicInput` (write) | DLC5 |
| `Refrigerator` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicOutput` |  |
| `RemoteWorkerDock` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) | DLC3 |
| `RemoteWorkTerminal` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) | DLC3 |
| `ResearchCenter` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `CompletionRatio`; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `ResearchClusterModule` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `ResearchModule` | `On`; `Error`, `Activate`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | forbidden with Spaced Out |
| `ResetSkillsStation` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) |  |
| `RoboPilotCommandModule` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch`; `LogicInput` (write); `LogicOutput` | DLC3; forbidden with Spaced Out |
| `RoboPilotModule` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out+DLC3 |
| `RockCrusher` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `RocketControlStation` | `On`; `Error`, `Activate`; `LogicInput` (write) | Spaced Out |
| `RocketInteriorGasInput` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held | Spaced Out |
| `RocketInteriorGasInputPort` | `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held | Spaced Out |
| `RocketInteriorGasOutput` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `Filter` (write); `LogicInput` (write) | Spaced Out |
| `RocketInteriorLiquidInput` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held | Spaced Out |
| `RocketInteriorLiquidInputPort` | `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held | Spaced Out |
| `RocketInteriorLiquidOutput` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `Filter` (write); `LogicInput` (write) | Spaced Out |
| `RocketInteriorPowerPlug` | `On`; `Error`, `Activate` | Spaced Out |
| `RocketInteriorSolidInput` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held | Spaced Out |
| `RocketInteriorSolidOutput` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `Filter` (write); `LogicInput` (write) | Spaced Out |
| `RoleStation` | `On`; `Error`, `Activate` |  |
| `RubberMaker` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) | DLC5 |
| `RustDeoxidizer` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `Sauna` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `ScannerModule` | `On`; `Error`, `Activate`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `ScoutModule` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `Sculpture` | `On`; `Error`, `Activate` |  |
| `ShearingStation` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) |  |
| `Shelf` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `Shower` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate` |  |
| `SludgePress` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) | IsExpansion1Active+Spaced Out+DLC5 |
| `SmallElectrobankDischarger` | `On`; `Error`, `Activate`; `PowerGeneration`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicInput` (write) | DLC3 |
| `SmallOxidizerTank` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `SmallSculpture` | `On`; `Error`, `Activate` |  |
| `Smoker` | `On`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `ConsumedRate`, `ProducedRate` | DLC4 |
| `SodaFountain` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `SolarPanel` | `On`; `Error`, `Activate`; `PowerGeneration`, `Charge`, `Maximum`, `Ratio` |  |
| `SolarPanelModule` | `On`; `Error`, `Activate`; `PowerGeneration`, `Charge`, `Maximum`, `Ratio`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `SolidBooster` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | forbidden with Spaced Out |
| `SolidCargoBaySmall` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `SolidConduitDiseaseSensor` | `Setting` (threshold, write), `Mode` (above or below), `Reading`; `LogicOutput` |  |
| `SolidConduitElementSensor` | `Filter` (write); `LogicOutput` |  |
| `SolidConduitInbox` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `Mode` (1 automation only, write); `LogicInput` (write) |  |
| `SolidConduitOutbox` | `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `Mode` (1 automation only, write) |  |
| `SolidConduitTemperatureSensor` | `Setting` (threshold, write), `Mode` (above or below), `Reading`; `LogicOutput` |  |
| `SolidFilter` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Filter` (write); `LogicInput` (write) |  |
| `SolidLimitValve` | `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Setting` (limit, write), `Maximum`, `Ratio`, `Quantity` (passed; write resets); `LogicInput` (write); `LogicOutput` |  |
| `SolidLogicValve` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write); `On` drives its automation input |  |
| `SolidTransferArm` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) |  |
| `SolidVent` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `SpaceHeater` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Setting` (its slider, write), `Maximum`, `Ratio`; `LogicInput` (write) |  |
| `SpecialCargoBay` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | forbidden with Spaced Out |
| `SpecialCargoBayCluster` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `SpiceGrinder` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicInput` (write) |  |
| `StaterpillarGasConnector` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held | Spaced Out |
| `StaterpillarGenerator` | `On`; `Error`, `Activate`; `PowerGeneration` | Spaced Out |
| `StaterpillarLiquidConnector` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held | Spaced Out |
| `SteamEngine` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | forbidden with Spaced Out |
| `SteamEngineCluster` | `On`; `Error`, `Activate`; `PowerGeneration`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `SteamTurbine` | `On`; `Error`, `Activate`; `PowerGeneration`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `SteamTurbine2` | `On`; `Error`, `Activate`; `PowerGeneration`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `StorageLocker` | `On`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize` |  |
| `StorageLockerSmart` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize`; `LogicOutput` |  |
| `StorageTile` | `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; item checklist set by `put`/`get`/`clr` with the sorter's item-kind filter, `StackSize` |  |
| `SublimationStation` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) | Spaced Out |
| `SugarEngine` | `On`; `Error`, `Activate`; `PowerGeneration`; `Setting` (capacity, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | Spaced Out |
| `SuitFabricator` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `SuitLocker` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `SuitMarker` | `On`; `Error`, `Activate`; `LogicInput` (write) |  |
| `SunLamp` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) |  |
| `SupermaterialRefinery` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) |  |
| `SushiBar` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) | DLC5 |
| `SweepBotStation` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `Switch` | `Open` (write), `Setting` (its lever) |  |
| `Telephone` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) | Spaced Out |
| `TeleportalPad` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) |  |
| `Telescope` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) | forbidden with Spaced Out |
| `TemperatureControlledSwitch` | `Setting` (threshold, write), `Mode` (above or below), `Reading` |  |
| `TemporalTearOpener` | `On`; `Error`, `Activate`; `LogicOutput` | Spaced Out |
| `ThermalBlock` | `On`; `Error`, `Activate` |  |
| `TouristModule` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; on a Spaced Out rocket: `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` | forbidden with Spaced Out |
| `TravelTubeEntrance` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `UnderwaterBreathingStation` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held | DLC5 |
| `UnderwaterCritterCondo` | `On`; `Error`, `Activate` |  |
| `UnderwaterMilkFeeder` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `UnderwaterMilkingStation` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) | DLC5 |
| `UnderwaterRanchStation` | `On`; `Error`, `Activate`; `LogicInput` (write) | DLC5 |
| `UnderwaterShearingStation` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) | DLC5 |
| `UnderwaterVentDrill` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) | DLC5 |
| `UraniumCentrifuge` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, `put`/`get`/`clr`; `LogicInput` (write) | Spaced Out |
| `VerticalWindTunnel` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `LogicInput` (write) |  |
| `WallToilet` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held | Spaced Out |
| `WarpConduitReceiver` | `On`; `Error`, `Activate` | Spaced Out |
| `WarpConduitSender` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held | Spaced Out |
| `WashBasin` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `WashSink` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held |  |
| `WaterCooler` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `Mode` (its option, write) |  |
| `WaterPurifier` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `ConsumedRate`, `ProducedRate`; `LogicInput` (write) |  |
| `WaterTrap` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write); `LogicOutput` |  |
| `WideFarmTile` | `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; plant as slot 0 (`Mature`, `MaturityRatio`, `Health`), `Harvest` (write), `AutoHarvest` (write) | DLC5 |
| `WoodenDoor` | `Error`, `Activate`; `Open` (write), `Lock` (write), `OpenNow` | DLC2 |
| `WoodGasGenerator` | `On`; `Error`, `Activate`; `PowerGeneration`; `Setting` (its slider, write), `Maximum`, `Ratio`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held; `LogicInput` (write) |  |
| `WoodSculpture` | `On`; `Error`, `Activate` | DLC2 |
| `WoodStorage` | `On`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and ratios of the fluid held | DLC2 |

## Nothing beyond the four every building answers

Wires, pipes, conveyor rails, travel tubes and their bridges, tiles and back walls are not devices
at all and answer nothing; the rest of this list, such as ladders and the logic gates, answers
the four.

`BunkerTile`, `CarpetTile`, `ExteriorWall`, `FirePole`, `GasConduit`, `GasConduitBridge`, `GasConduitOverflow`, `GasConduitPreferentialFlow`, `GasConduitRadiant`, `GasPermeableMembrane`, `GlassExteriorWall`, `GlassTile`, `HighWattageWire`, `InsulatedGasConduit`, `InsulatedLiquidConduit`, `InsulationTile`, `Ladder`, `LadderFast`, `LiquidConduit`, `LiquidConduitBridge`, `LiquidConduitOverflow`, `LiquidConduitPreferentialFlow`, `LiquidConduitRadiant`, `LogicGateAND`, `LogicGateDemultiplexer`, `LogicGateMultiplexer`, `LogicGateNOT`, `LogicGateOR`, `LogicGateXOR`, `LogicMemory`, `LogicRibbon`, `LogicWire`, `MeshTile`, `MetalTile`, `MouldingTile`, `PlasticTile`, `RocketEnvelopeWindowTile`, `RocketInteriorGasOutputPort`, `RocketInteriorLiquidOutputPort`, `RocketWallTile`, `RubberTile`, `SnowTile`, `SolidConduit`, `SolidConduitBridge`, `Tile`, `TilePOI`, `TravelTube`, `TravelTubeWallBridge`, `Wire`, `WireBridge`, `WireBridgeHighWattage`, `WireRefined`, `WireRefinedBridge`, `WireRefinedBridgeHighWattage`, `WireRefinedHighWattage`, `WireRubber`, `WireRubberBridge`, `WoodTile`
