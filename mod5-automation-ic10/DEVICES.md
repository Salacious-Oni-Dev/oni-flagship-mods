# Devices

What an IC10 chip can reach in Oxygen Not Included: Mod 5's own 24 buildings, and every vanilla
building through one generic adapter. The meaning of each logic type is in the
[IC10 reference](IC10-REFERENCE.md#logic-types-onis-buildings-answer). `BUILDINGS.md`, beside
this page, lists every vanilla building by id with exactly what it answers.

## How a building becomes a device

- **On the data network.** A building is on a data network when a Data Cable runs under any
  cell of its footprint. It needs no port. Buildings from other mods join the same way.
- **Which buildings.** Every finished building except wires, pipes, tiles, back walls and
  foundation buildings other than doors. A building under construction answers nothing.
- **What it answers.** The adapter looks at the parts a building is made of (an enable button,
  a power connection, a storage, a slider, a threshold, a door, a recipe list, a planter) and
  answers the logic types those parts stand for. A building answers only what it has; anything
  else is `IncorrectLogicType`.
- **What a write does.** It is the same change as the player's click in the side screen, made at
  once, without an errand for a duplicant. A value written is clamped and rounded as the side
  screen would.
- **Every building** answers `PrefabHash` (`HASH` of its id, the first column of `BUILDINGS.md`),
  `NameHash`, `ReferenceId` and `BodyTemperature`.
- **Another mod's building** can declare its own values through `OniFramework.DataDevices`, by
  logic-type name, without referencing Mod 5. Those are applied after the adapter's own.

## Mod 5's buildings

### IC Housing

Runs one IC10 program. Six sockets, `d0` to `d5`, set in its details panel; Edit Program opens
the editor. Needs 10 W whenever it is switched on; the watts become heat. It has no automation
port: it talks over the data cable. 1×1, refined metal. Research `Multiplexing`.

Other chips read: `On` (write), `Power`, `Error`, `Setting` (write: a number any chip can use),
`LineNumber`, `Cycle`, `CycleTime`, `Night`. Its stack is readable and writable with `get` and
`put`.

### Data Cable and Data Cable Bridge

The Data Cable is a third kind of wire, dragged like automation wire. It carries no power and no
automation signal: only data. The Data Cable Bridge (3×1) joins the cable under its two ends and
passes over whatever is under its middle; it is part of the cable, not a device, and two data
bridges cannot cross. Research `LogicControl`.

A network answers as a device itself (`d0:0`, `db:0`): `Channel0` to `Channel7`, `StackSize`
(its device count) and each member's reference id through `get`.

### Logic bricks

Each keeps one number, read by a chip as `Setting`. 1×1, refined metal, no power except the
display.

| building | answers | research |
|---|---|---|
| IC Memory | `Setting` (write): one number a program keeps | `LogicCircuits` |
| IC Dial | `Setting` 0 to `Mode`, set by hand or written; `Ratio`; `Mode` (write) is the range, 100 when built | `LogicControl` |
| IC Switch | `Setting` 1 or 0, thrown by hand; writing `Open` throws it | `LogicControl` |
| IC Button | `Setting` 1 for half a second after a press; writing `Activate` 1 presses it | `LogicControl` |
| IC Display | shows `Setting` (write) in the form `Mode` (write) picks: number, percent, watts, kelvin, Celsius, metres, credits, seconds, minutes, days, text, Fahrenheit, litres, moles, pascals, newtons, degrees; `Color` (write). 10 W; dark while off or unpowered | `LogicControl` |

### Logic units

Stationeers' discrete logic, for automation without a program. Each unit runs every half second
of game time, keeps its result in its `Setting` for other units and chips to read, and shows a
lamp: red off, orange error, green running. Inputs and the buildings they read or write are on
the unit's own data cable. Set up in the details panel. 1×1 (the Logic Mirror 2×1), refined
metal, no power.

| building | does | research |
|---|---|---|
| IC Reader | `Setting` follows one logic type of one building | `LogicControl` |
| IC Writer | copies one unit's `Setting` to one logic type of one building when it changes; `ForceWrite` 1 writes now | `LogicControl` |
| IC Writer Switch | a hand switch that writes 1 or 0 to one logic type of one building each time it is flipped | `LogicControl` |
| IC Math | two units' `Setting`s added, subtracted, multiplied, divided and more; `Mode` is the operation | `LogicCircuits` |
| IC Math (Unary) | one unit's `Setting` rounded, rooted, negated and more; angles in degrees | `LogicCircuits` |
| IC Compare | 1 while the first `Setting` equals, exceeds, is under or differs from the second | `LogicCircuits` |
| IC Select | the second unit's `Setting` while a third's is above 0, else the first's | `LogicCircuits` |
| IC Min/Max | the greater, or the lesser, of two `Setting`s | `LogicCircuits` |
| IC Slot Reader | `Setting` follows one slot logic type of one slot of one building | `ParallelAutomation` |
| IC Batch Reader | one logic type over every building of one kind: average, sum, lowest or highest | `ParallelAutomation` |
| IC Batch Slot Reader | one slot logic type over every building of one kind | `ParallelAutomation` |
| IC Batch Writer | copies one unit's `Setting` to one logic type of every building of one kind | `ParallelAutomation` |
| IC Hash Generator | `Setting` is the hash of a building or element the player picks, for a batch unit | `ParallelAutomation` |
| IC Logic Mirror | two cells: on the cable under its right cell it answers as a building on the cable under its left cell, every logic type read and written | `ParallelAutomation` |
| IC PID Controller | drives one building's value toward `Setpoint`; `ProportionalGain`, `IntegralGain`, `DerivativeGain`, `Maximum`, `Minimum`, `Reset`. Set the output maximum and minimum before use | `Multiplexing` |

### IC Sorter

ONI's conveyor filter with its one-item choice replaced by a list of up to 32 filters that a
program writes into its memory (see the [reference](IC10-REFERENCE.md#device-memory)). An item
that passes the list leaves by the middle port; anything else by the far end; with no filters,
everything goes to the far end. `Mode` 0 all, 1 any, 2 none; `ImportCount`, `ExportCount`;
`ClearMemory` 1 zeroes the counts. 3×1, raw metal, 120 W. In the Conveyance menu beside the
Conveyor Filter. Research `SolidManagement`.

## Vanilla buildings

`BUILDINGS.md` lists 424 vanilla buildings with what each answers, and 58 more that answer
nothing beyond the four every building answers. Of those 58, the wires, pipes, conveyor rails,
travel tubes and their bridges, the tiles and the back walls are not devices at all (see above)
and answer nothing; the rest, such as ladders and the logic gates, answer the four. It is read off the game's building definitions, not
measured in the game: a part a building gains while the game runs is not listed, and a control
the game hides on one building may be. Every building with a gas or liquid pipe port also
answers the readings of the pipes at its ports (`PressureInput`, `TemperatureOutput` and the
rest).

What a player can expect, by kind, summarised from `BUILDINGS.md` and the reference (`(write)`
marks what a chip can set; power means `Power`, `RequiredPower` and `PowerDraw`):

| kind | example | answers |
|---|---|---|
| Pumps | `LiquidPump`, `GasPump` | `On`; `Power`, `RequiredPower`, `PowerDraw`; `Error`, `Activate`; `Quantity`; stored items as slots, `lr` contents, `Temperature`, `TotalMoles` and the ratios of the fluid held; `LogicInput` (write) |
| Valves | `LiquidValve`, `GasValve` | `Setting` (grams per second, write), `Maximum`, `Ratio` |
| Logic valves | `GasLogicValve`, `LiquidLogicValve` | power; `Error`, `Activate`; `On` drives its automation input |
| Limit valves | `GasLimitValve`, `LiquidLimitValve`, `SolidLimitValve` | `Setting` (the limit, write), `Quantity` (passed; writing 0 resets it); `LogicInput` (write), `LogicOutput` |
| Filters | `GasFilter`, `LiquidFilter`, `SolidFilter` | `On`; power; `Filter` (write); `LogicInput` (write) |
| Vents | `GasVent`, `GasVentHighPressure`, `LiquidVent` | `On`; `Error`, `Activate`; the fluid held; `VentState`; `LogicInput` (write) |
| Area sensors | `LogicTemperatureSensor`, `LogicPressureSensorGas`, `LogicPressureSensorLiquid` | `Setting` (threshold, write), `Mode` (above or below), `Reading`; `Pressure`, `TotalMoles`, the ratios, `Temperature`, `VolumeOfLiquid`, `lr` contents; `LogicOutput` |
| Element sensors | `LogicElementSensorGas`, `LogicElementSensorLiquid` | `Filter` (write) and the same readings of their cell |
| Pipe sensors | `GasConduitTemperatureSensor`, `LiquidConduitElementSensor` and the rest | the threshold; `Pressure`, `TotalMoles`, the ratios, `Temperature`, `Volume`, `VolumeOfLiquid`, `NetworkFault` of the whole pipe network |
| Other sensors | `LogicWattageSensor` | `PowerActual`, `PowerPotential`, `PowerRequired`; the threshold |
| | `LogicTimeOfDaySensor` | `Cycle`, `CycleTime`, `Night`; `WindowStart`, `WindowLength` (write) |
| | `LogicTimerSensor` | `OnDuration`, `OffDuration` (write) |
| | disease, critter count, light, radiation sensors | the threshold and `Reading` |
| Switches | `Switch`, `LogicSwitch`, `LogicPowerRelay` | `Open` (write), `Setting` (its lever) |
| | `PressureSwitchGas`, `TemperatureControlledSwitch` | the threshold and `Reading` |
| Automation parts | `LogicCounter` | `Setting` (top count, write), `Quantity` (count, write), `Mode` |
| | `LogicRibbonReader`, `LogicRibbonWriter` | `Setting` (the bit, write) |
| | `LogicGateBUFFER`, `LogicGateFILTER` | `Setting` (its slider, write) |
| Doors | `Door`, `PressureDoor`, `BunkerDoor` | `Open` (write), `Lock` (write), `OpenNow`, `Mode` and `Setting` for logic mode; `LogicInput` (write) |
| Generators | `Generator` (coal) | `On`; `PowerGeneration`; `Setting` (its slider, write); the fuel held; `LogicInput` (write) |
| | `SolarPanel` | `On`; `PowerGeneration`, `Charge`, `Maximum`, `Ratio` |
| Batteries | `Battery`, `BatteryMedium` | `On`; `Charge`, `Maximum`, `Ratio`, `PowerActual`, `PowerPotential` |
| | `BatterySmart` | the same, `ActivateAt` and `DeactivateAt` (write), `LogicOutput` |
| Transformers | `PowerTransformer`, `PowerTransformerSmall` | `On`; `Setting` (watt limit, write), `Maximum`, `Ratio`; `LogicInput` (write) |
| Storage | `StorageLocker`, `Refrigerator`, `RationBox` | `Setting` (capacity, write); `Quantity`; stored items as slots; an item checklist set through memory; `StackSize` |
| | `LiquidReservoir`, `GasReservoir` | the fluid held; `ActivateAt`, `DeactivateAt` (write) |
| Lights | `CeilingLight`, `FloorLamp`, `SunLamp` | `On`; power; `LogicInput` (write) |
| Machines with recipes | `MetalRefinery`, `Kiln`, `RockCrusher`, `CookingStation` and the rest | `On`; power; `RecipeHash` (write), `Activate` (write), `CompletionRatio`, `StackSize`, counted orders through `put`, `get`, `clr`; what it stores |
| Converters | `Electrolyzer`, `WaterPurifier`, `OilRefinery`, `AlgaeHabitat` and the rest | `ConsumedRate`, `ProducedRate`; what it stores; `On` and power where it has them |
| Temperature control | `SpaceHeater`, `LiquidHeater` | `On`; power; `Setting` (its slider, write) |
| | `AirConditioner`, `LiquidConditioner` | `On`; power; what it holds |
| | `IceMachine` | `Mode` (its option, write) |
| Research | `ResearchCenter` and the rest | `CompletionRatio` |
| Farming | `PlanterBox`, `FarmTile`, `HydroponicFarm` | the plant as slot 0 (`Mature`, `MaturityRatio`, `Health`), `Harvest` (write), `AutoHarvest` (write) |
| Ranching | `CritterDropOff` | capacity, storage, item checklist |
| | `CritterPickUp`, `CreatureDeliveryPoint` | `Mode` (its checkbox, write) |
| Conveyor | `SolidConduitInbox`, `SolidConduitOutbox` | storage; `Mode` (1 automation only, write) |
| Rockets (Spaced Out) | every rocket module, `LaunchPad` | `FlightState`, `TimeToDestination`, `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` |
| Radbolts (Spaced Out) | `HighEnergyParticleSpawner`, `HighEnergyParticleRedirector` | `Mode` (direction: 0 up, then by 45 degrees, write) |
| Furniture and recreation | beds, sculptures, `WaterCooler`, `Outhouse` | `On`, `Error`, `Activate`; the water cooler's `Mode`; the outhouse's `FlushesLeft` |

`On` appears only on buildings that have the game's enable button. Sensors, doors, manual valves
and many automation parts have none, and writing `On` to them is `IncorrectLogicType`.

## Driving a building's automation input

Most vanilla buildings are switched by an automation wire. A chip can drive the same input over
data: write `LogicInput` (0 or 1; 0 to 15 on a ribbon input), or `On` on a logic valve or the
Automated Notifier. While a wire is attached to the port, the wire wins; when the wire is
removed, the chip's value takes over again. A driven value stays, and is saved, until a chip
writes it again, as a Stationeers device keeps a written setting. A building on a data cable no
longer shows the "No Automation Wire Connected" warning.

Limits: only a building's first automation input can be driven. A building added by another mod
whose automation ports are created late in loading cannot be driven. The Teleporter's ports are
not covered.

## Not answered

- `Pressure` on a building's contents: ONI gives a storage no volume. `TotalMoles` and the
  ratios are there.
- `Combustion`, anywhere.
- A door's `Idle`; a planter's seeding values; a fabricator's import and export counts.
- Geysers, which are not buildings.
- Base-game (non-Spaced Out) rockets.
