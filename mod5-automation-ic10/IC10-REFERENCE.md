# IC10 reference

IC10 is the language of Stationeers' integrated circuits. Mod 5 implements all 154 of its
instructions. A program written for Stationeers compiles here and runs against ONI's buildings;
this page says what the chip does, what ONI's buildings answer, and every place where the
behaviour differs from Stationeers on purpose.

## The chip

- **Program.** At most 128 lines, 90 characters a line, 4096 characters in all, ASCII only.
  One instruction per line. Lines are numbered from 0. `#` starts a comment. A label is a name
  followed by a colon on a line of its own.
- **Tick.** The IC Housing runs its chip twice a second of game time: each tick runs up to 128
  lines, or until a `yield` or a `sleep`. Game speed and pause scale it. The housing runs only
  while it is switched on and powered, and draws 10 W whenever it is switched on.
- **Registers.** `r0` to `r15`, plus `sp` (`r16`, the stack pointer) and `ra` (`r17`, the
  return address). Every register holds a double.
- **Stack.** 512 cells. `push`, `pop` and `peek` use `sp`; `poke` writes any cell. Other chips
  read and write it with `get` and `put`.
- **Errors.** The first line that does not compile stops the compile, and the chip does not
  run; the housing shows the line and the error. A runtime error holds the chip on the failing
  line and tries it again every tick. The 39 error names are Stationeers' own
  (`IncorrectLogicType`, `DeviceNotFound`, `OutOfRegisterBounds` and the rest).
- **Recompiling** (pressing Apply) sets `sp` to 0 and keeps the other registers and the stack.

### Numbers and names

- `12`, `-3.5`, `1,000` (a comma is a thousands separator), `$FF` hexadecimal, `%1010` binary.
  No exponent notation.
- `HASH("LiquidPump")` is the number a building's id hashes to (CRC-32, signed). Batch
  instructions and `PrefabHash` use it for a building; `Filter`, `RecipeHash` and `lr` use it
  for an element or a product.
- `STR("text")` packs up to six ASCII characters into one number.
- `pinf` and `ninf` are the infinities. `nan` does not work as a constant, as in Stationeers.
- `define name 295` names a number when the line compiles. `alias name r0` or `alias name d0`
  names a register or a device when the line runs.
- Named constants: `LogicType.<name>` and `LogicSlotType.<name>` (the type's number);
  `Color.Blue` (0) to `Color.Purple` (11), in Stationeers' order; `Vent.Outward` (0) and
  `Vent.Inward` (1); for the IC Sorter `SorterInstruction.*`, `SortingClass.*`, `SlotClass.*`
  and the bare comparisons `Equals`, `Greater`, `Less`, `NotEquals`; for counted orders
  `PrinterInstruction.*`.

### Devices

- `d0` to `d5` are the housing's six sockets, set in the housing's details panel. With a data
  cable under the housing, a socket can point only at a building on that cable; a socket that
  was set before the cable was cut still answers. A building that leaves the network reads as
  absent.
- `db` is the housing itself.
- `d0:0` is the data network under the device in socket 0, and `db:0` the network under the
  housing. A network answers eight channels, `Channel0` to `Channel7`, that every chip on it
  can read and write, its device count as `StackSize`, and each member's reference id through
  `get`.
- `ld`, `sd`, `getd`, `putd` and `clrd` take a reference id (from `ReferenceId`). With a cable
  under the housing they reach only buildings on that cable; with none they reach any finished
  building.
- Batch instructions (`lb`, `sb` and the rest) act on every building of a kind on the housing's
  network, in name order. With no cable they find nothing.

## Instructions

154 instructions, grouped as Stationeers players know them: device I/O, batch, math, bitwise,
logic, flow control, stack and memory, names. Operand words: `r` a
register to write (or its alias); `a` `b` `c` a value (a register, a number, a define, a
constant, `HASH(...)`); `d` a device; `id` a reference id; `type` a logic type by name or
number; `slotType` a slot logic type; `hash` a building's hash; `name` a name hash; `mode` a
batch mode (`Average`, `Sum`, `Minimum`, `Maximum`); `line` a line number or label; `offset` a
number of lines.

### Device I/O (13)

| instruction | does |
|---|---|
| `l r d type` | read a logic type from a device |
| `s d type a` | write a logic type of a device |
| `ld r id type` | read a logic type from the device with that reference id |
| `sd id type a` | write a logic type of the device with that reference id |
| `ls r d slot slotType` | read a slot logic type from one slot |
| `ss d slot slotType a` | write a slot logic type of one slot |
| `lr r d reagentMode a` | how much of an element or product a device holds: mode `Contents` (0) with its hash gives kilograms; `TotalContents` (3) gives all of it |
| `rmap r d a` | the prefab a reagent hash stands for on that device |
| `sdse r d` | 1 if the device is set, else 0 |
| `sdns r d` | 1 if the device is not set |
| `get r d address` | read a cell of a device's memory: another housing's stack, a machine's order stack, a sorter's filters, a network's device list |
| `getd r id address` | as `get`, by reference id |
| `put d address a` | write a cell of a device's memory |

`putd id address a`, `clr d` (empty a device's memory) and `clrd id` complete the memory group,
counted under Stack below with `poke`.

### Batch (7)

| instruction | does |
|---|---|
| `lb r hash type mode` | read a logic type from every building of that kind on the network, combined by the mode |
| `lbn r hash name type mode` | as `lb`, for those that also carry that name |
| `sb hash type a` | write a logic type of every building of that kind |
| `sbn hash name type a` | as `sb`, for those that also carry that name |
| `lbs r hash slot slotType mode` | read a slot logic type from every building of that kind |
| `lbns r hash name slot slotType mode` | as `lbs`, for those that also carry that name |
| `sbs hash slot slotType a` | write a slot logic type of every building of that kind |

### Math (28)

| instruction | does |
|---|---|
| `add r a b`, `sub r a b`, `mul r a b`, `div r a b` | r = a + b, a - b, a * b, a / b |
| `mod r a b` | the remainder of a / b, never negative for a positive b |
| `pow r a b` | a to the power b |
| `sqrt r a`, `exp r a`, `log r a` | square root; e to the power a; natural logarithm |
| `abs r a`, `sgn r a` | a without its sign; -1, 0 or 1 by its sign |
| `round r a`, `trunc r a`, `ceil r a`, `floor r a` | nearest whole number (a half to the even one); fraction cut off; up; down |
| `max r a b`, `min r a b` | the larger; the smaller |
| `sin r a`, `cos r a`, `tan r a` | trigonometry, a in radians |
| `asin r a`, `acos r a`, `atan r a` | the angle, in radians |
| `atan2 r a b` | the angle of the point (b, a) |
| `rand r` | a random number from 0 up to 1 |
| `lerp r a b c` | a + (b - a) * c, with c held between 0 and 1 |
| `clamp r a min max` | a, held between min and max |
| `move r a` | r = a |

### Bitwise (13)

Values are taken as 53-bit signed integers (54-bit unsigned for the shifts that need it).

| instruction | does |
|---|---|
| `and r a b`, `or r a b`, `xor r a b`, `nor r a b` | bit by bit |
| `not r a` | every bit flipped |
| `sll r a b`, `sla r a b` | shift left b bits (the two are the same) |
| `srl r a b` | shift right, zeros coming in |
| `sra r a b` | shift right, the sign kept |
| `rol r a b`, `ror r a b` | rotate left, right |
| `ext r a start length` | `length` bits of a, from bit `start` |
| `ins r a start length` | put the low `length` bits of a into r at bit `start` |

### Logic and selection (19)

Each sets r to 1 or 0.

| instruction | 1 when |
|---|---|
| `seq r a b`, `sne r a b` | a equals b; a does not equal b |
| `slt r a b`, `sgt r a b`, `sle r a b`, `sge r a b` | a < b; a > b; a <= b; a >= b |
| `seqz r a`, `snez r a`, `sltz r a`, `sgtz r a`, `slez r a`, `sgez r a` | the same against 0 |
| `sap r a b c`, `sna r a b c` | a and b are about equal (within c of the larger, as a fraction); are not |
| `sapz r a b`, `snaz r a b` | a is about 0, by the fraction b; is not |
| `snan r a`, `snanz r a` | a is not a number; a is a number |
| `select r a b c` | (not 1 or 0) r = b if a is not 0, else c |

### Flow control (64)

Jumps:

| instruction | does |
|---|---|
| `j line` | go to a line or label |
| `jr offset` | go that many lines on from this one; back if negative |
| `jal line` | go to a line and keep the next line's number in `ra`; come back with `j ra` |

Branches, to a line or label (21): `beq a b line`, `bne`, `blt`, `bgt`, `ble`, `bge`;
`beqz a line`, `bnez`, `bltz`, `bgtz`, `blez`, `bgez`; `bap a b c line`, `bna`;
`bapz a b line`, `bnaz`; `bnan a line`; `bdse d line` (the device is set), `bdns d line` (it is
not); `bdnvl d type line` (the device cannot be read for that type), `bdnvs d type line` (it
cannot be written).

Relative branches, by an offset (19): `breq a b offset`, `brne`, `brlt`, `brgt`, `brle`,
`brge`; `breqz a offset`, `brnez`, `brltz`, `brgtz`, `brlez`, `brgez`; `brap a b c offset`,
`brna`; `brapz a b offset`, `brnaz`; `brnan a offset`; `brdse d offset`, `brdns d offset`.

Branch and link, keeping the way back in `ra` (18): `beqal`, `bneal`, `bltal`, `bgtal`,
`bleal`, `bgeal`; `beqzal`, `bnezal`, `bltzal`, `bgtzal`, `blezal`, `bgezal`; `bapal`,
`bnaal`; `bapzal a b line`, `bnazal a b line`; `bdseal d line`, `bdnsal d line`.

The conditions are those of the comparison of the same letters: `eq` equal, `ne` not equal,
`lt` less, `gt` greater, `le` less or equal, `ge` greater or equal, `z` against 0, `ap` about
equal, `na` not about equal, `nan` not a number, `dse` device set, `dns` device not set.

Waiting (3):

| instruction | does |
|---|---|
| `yield` | stop until the next tick |
| `sleep a` | stop for a seconds of game time |
| `hcf` | halt and catch fire: the program stops and the housing shows as broken |

### Stack and memory (7)

| instruction | does |
|---|---|
| `push a` | put a on the stack and move `sp` up one |
| `pop r` | move `sp` down one and take the value there |
| `peek r` | read the top of the stack and leave it |
| `poke address a` | write a cell of this chip's own stack |
| `putd id address a` | as `put`, by reference id |
| `clr d` | empty a device's memory |
| `clrd id` | as `clr`, by reference id |

### Names (3)

| instruction | does |
|---|---|
| `alias name r\|d` | give a register or a device a name |
| `define name number` | give a number a name |
| `label d name` | give a socket a name the housing shows (cut to 10 characters) |

Count: 13 + 7 + 28 + 13 + 19 + 3 + 21 + 19 + 18 + 3 + 7 + 3 = 154.

## Logic types ONI's buildings answer

A building answers only what it has: reading or writing anything else stops the program with
`IncorrectLogicType`. A building still under construction answers nothing. A chip's write is
the same change as the player's click in the side screen, made at once, with no errand for a
duplicant. Names marked **ONI** do not exist in Stationeers; their numbers start at 1024, so no
Stationeers number is ever reused. [Devices](DEVICES.md) says which buildings answer what.

| name | access | meaning |
|---|---|---|
| `PrefabHash` | read | every building: `HASH` of its id |
| `NameHash` | read | every building: `HASH` of the name the player gave it |
| `ReferenceId` | read | every building: its own number, for `ld`, `sd`, `getd`, `putd` |
| `BodyTemperature` **ONI** | read | every building: its own temperature, K |
| `On` | read, write | a building with an enable button. On a logic valve or the Automated Notifier, `On` drives its automation input instead |
| `Power` | read | 1 while it has power |
| `Error` | read | 1 while something keeps it from working |
| `Activate` | read | 1 while it is working. Written: on a machine with recipes, 1 orders the selected recipe without end and 0 cancels; on an IC Button, 1 presses it |
| `RequiredPower` | read | the watts it is rated to draw; 0 switched off, -1 with no wire |
| `PowerDraw` **ONI** | read | the watts it is drawing at this moment |
| `PowerGeneration` | read | a generator: the watts it is making now |
| `PowerActual`, `PowerPotential` | read | a battery or wattage sensor: the watts its circuit's consumers draw; the watts its circuit's generators make now |
| `PowerRequired` | read | a wattage sensor: the watts its circuit's consumers are rated to draw |
| `Charge` | read | a battery: what it holds, in watt-ticks (joules times two, as Stationeers counts half-second ticks). A solar panel: the watts it makes now |
| `Maximum`, `Ratio` | read | the top of the range `Setting` or `Charge` moves in; the value over it |
| `Setting` | read, write | the building's one number, in the unit its side screen shows: a valve's grams per second, a transformer's watt limit, a sensor's threshold, a bin's capacity, a counter's top count, a brick's number |
| `Mode` | read, write | a sensor: 1 signals above the threshold, 0 below. A building with a few options: the option's number, from 0, in the side screen's order. A door: 0 opens for duplicants, 1 waits for a program (see Doors below) |
| `Color` | read, write | the IC Display's text colour, `Color.Blue` 0 to `Color.Purple` 11; green (2) when built |
| `Reading` **ONI** | read | a sensor: what it measures, in its own unit |
| `Temperature` | read | K: of what a sensor watches, or of the gas and liquid a building holds (0 if it holds none) |
| `Pressure` | read | kPa: of an atmo, thermo or element sensor's cell, or of a pipe sensor's pipe network. Needs the replacement SimDLL |
| `TotalMoles` | read | moles of everything there |
| `RatioOxygen`, `RatioCarbonDioxide`, ... | read | the share of each gas or liquid by moles, 0 to 1: Stationeers' 28 ratio types |
| `Volume` | read | a pipe sensor: litres of its pipe network (10 L a gas pipe cell, 20 L a liquid one) |
| `VolumeOfLiquid` | read | a cell or pipe sensor: litres of liquid there |
| `NetworkFault` | read | a pipe sensor: 1 while the pipe network is under stress (with Mod 1) |
| `PressureInput`, `TemperatureInput`, `TotalMolesInput`, `Ratio...Input`; `...Output`; `...Input2`, `...Output2` | read | any building with a gas or liquid pipe port: the pipe at that port; 0 while no pipe is there |
| `Quantity` | read, write | kilograms stored. A limit valve: what has passed, and writing 0 resets it. A signal counter: its count |
| `Open` | read, write | a door: 1 asks it to stand open. A switch: its lever. The Object Dispenser: 1 opens it |
| `Lock` | read, write | a door: 1 locks it |
| `OpenNow` **ONI** | read | a door: 1 while it stands open at this moment, whoever opened it |
| `Filter` **ONI** | read, write | a filter or element sensor: the hash of the element it passes or looks for; 0 for none |
| `ActivateAt`, `DeactivateAt` **ONI** | read, write | a smart battery, a reservoir: the percentages at which its signal comes on and goes off |
| `RecipeHash` | read, write | a machine with recipes: the hash of the product of the selected recipe. Written, it selects that recipe and starts nothing |
| `CompletionRatio` | read | a machine with recipes, a research station: how far along the item or research in hand is, 0 to 1 |
| `StackSize` | read | the cells of a device's memory: 64 on a machine, 32 on the IC Sorter or a building with an item checklist; a network's device count |
| `Harvest` | write | a planter: above 0 asks for the ripe plant to be harvested |
| `AutoHarvest` **ONI** | read, write | a planter: 1 while the plant is harvested as soon as it is ripe |
| `VentState` **ONI** | read | a vent: 0 invalid, 1 venting, 2 blocked, 3 over pressure, 4 closed |
| `ConsumedRate`, `ProducedRate` **ONI** | read | a converter (electrolyzer, oil refinery and the rest): grams per second taken in, given out, all inputs or outputs added |
| `FlushesLeft` **ONI** | read | an outhouse: uses left before it must be emptied |
| `Cycle`, `CycleTime`, `Night` **ONI** | read | the IC Housing and the cycle sensor: the cycle number from 1; seconds into the cycle, 0 to 600; 1 at night |
| `OnDuration`, `OffDuration` **ONI** | read, write | a timer sensor: seconds green, seconds red, 0 to 6000 |
| `WindowStart`, `WindowLength` **ONI** | read, write | a cycle sensor: where its window starts and how long it lasts, 0 to 1 of a cycle |
| `FlightState` **ONI** | read | a Spaced Out rocket module or launch pad: 0 grounded, 1 launching, 2 in flight, 3 landing |
| `TimeToDestination` | read | the same: seconds of game time until it arrives |
| `FuelRemaining`, `OxidizerRemaining`, `RocketRange`, `ReadyToLaunch` **ONI** | read | the same: kilograms of fuel; oxidizer weighted by its strength; hexes it can fly; 1 while every launch check passes |
| `LogicInput` **ONI** | read, write | the value on a building's first automation input. Written, a chip drives that input as a wire would (0 or 1; 0 to 15 on a ribbon input) while no wire is attached: a wire wins. The value is kept, and saved, until written again |
| `LogicOutput` **ONI** | read | the value on a building's first automation output |
| `Channel0` to `Channel7` | read, write | a data network: eight shared numbers, NaN until written |
| `LineNumber` | read, write | the IC Housing: the line its chip runs next |

`LogicInput` and `LogicOutput` are a bridge to ONI's automation wire and will be retired once
every building answers its own values; their numbers stay reserved.

### Slot logic types

Read with `ls r d slot slotType`. A building's stored items are its slots, in the order it keeps
them. A planter's slot 0 is its plant; its stored items follow from slot 1.

| name | meaning |
|---|---|
| `Occupied` | 1 if the slot holds something |
| `OccupantHash`, `PrefabHash` | the hash of what is in the slot |
| `Quantity` | kilograms in the slot; 1 for a plant |
| `Temperature` | K of what is in the slot |
| `TotalSlots` | how many slots are in use |
| `Mature` | planter slot 0: 1 harvestable, 0 not, -1 no plant |
| `MaturityRatio` | planter slot 0: how far grown, 0 to 1; -1 no plant |
| `Health` | planter slot 0: 0 while wilting, else 1; -1 no plant |

### Device memory

- **A machine's order stack.** Every machine with recipes has Stationeers' 64-cell printer
  stack. An order is `product hash * 65536 + quantity * 256 + 2`; `put machine 0 r0` places it.
  The machine works through the cells, counts each order down as it makes the product, and
  erases the cell when the order is done. An order for a hash that names no recipe the machine
  can make is dropped. `clr` empties the stack.
- **The IC Sorter's filters.** 32 cells. Each filter's low byte says what it tests:
  `hash * 256 + 1` the item is this kind; `hash * 256 + 2` it is not;
  `value * 65536 + comparison * 256 + 3, 4 or 5` its sorting class, slot type or amount compared
  (`Equals`, `Greater`, `Less`, `NotEquals`); `count * 256 + 6` only `count` more of the next
  filter. `Mode` 0 needs every filter, 1 any, 2 none.
- **An item checklist.** A storage bin, fridge, ration box, critter feeder or conveyor loader
  takes the sorter's 32-cell memory; its checklist becomes every item kind named by a
  `hash * 256 + 1` filter, within a fifth of a second of game time. `clr` empties it. A
  building no program has written keeps the player's checklist.

### Doors

ONI's door has one state of three (operate, open, locked); Stationeers' has two independent
ones. A chip writes `Open 1` to ask for open and `Lock 1` to ask for locked; whichever was
written last wins, and `Open 0` or `Lock 0` returns the door to operating for duplicants. A
locked door bars duplicants. `Mode 1` puts the door in Stationeers' logic mode: a duplicant
arriving at the shut door presses it (`Setting` goes to 1) and waits until a program opens it.

## Where it matches Stationeers

These were checked against how Stationeers is written, one test per behaviour. They have not yet
been compared against a running copy of Stationeers.

- Registers, the 512-cell stack, `sp` and `ra`.
- All 154 instructions and the operands each takes; how each kind of operand is read; rounding
  against truncation.
- `alias` takes effect when it runs; `define` and labels when the line compiles.
- 128 lines a tick; `yield` and `sleep`; how errors hold or stop the chip; the 39 error names.
- The 53-bit integer rule of the bitwise instructions.
- `s` reads first and writes only if the value differs.
- Batch modes, including their edge cases: `Count` ignores the name hash; the minimum of no
  devices is 0 and the maximum is -infinity; the average of none is NaN.
- Six sockets and `db`; a socketed device off the network reads as absent; labels cut to 10
  characters; `Setting` and `LineNumber` on the housing.
- A network's channels: lost when a cable is removed or networks merge, and not saved.
- The socket picker's step order, by name, and a network's batch order, by name.
- Battery charge in watt-ticks; `PowerActual` and `PowerPotential` answered by batteries and
  the wattage sensor, not by a single machine.
- The logic units' arithmetic, the PID controller, the IC Display's text for each of its 17
  forms, the IC Sorter's filter codes, the printer stack's packing and walk.
- Units: kelvin, kilopascals, mole fractions, watts.

Stationeers' own quirks are kept so that its programs behave the same here. Among them: `nan`
cannot be used as a constant; `1e3` is not a number and `1,000` is 1000; a constant never works
where an integer is expected (hash, slot, address); a failing `pop` still moves `sp`; `ld` on a
missing device is `Unknown` but `sd` is `DeviceNotFound`; `hcf` fires again every tick the chip
stays on it; recompiling keeps `r0` to `r15`, `ra` and the stack; `define x notanumber` defines
NaN; an alias drops a network suffix.

## Where it differs from Stationeers

| what | Stationeers | Mod 5 | why |
|---|---|---|---|
| `brapz`, `brnaz`, `bapzal`, `bnazal` | cannot be compiled: the parser asks for three operands and then reads a fourth | take three operands (`a b line`), as `bapz` and `bnaz` do and as Stationeers' own help shows them | no working Stationeers program can contain them, so nothing changes meaning |
| what a device is | a Stationeers item or structure | any finished ONI building, through a generic adapter over its components, plus anything a mod registers through `OniFramework.DataDevices` | ONI's buildings are not Stationeers things |
| the data network | carried by the power cable | a separate Data Cable, and a building is on it when cable runs under any of its cells | many ONI buildings that a chip should reach take no power; ONI buildings have no data ports |
| crossing cables | — | a three-cell Data Cable Bridge, part of the cable, not a device | ONI's grid needs a way over other cable |
| `:1`, `:2` on a device | the device's second and third connection | the second and third data network under the building, in cell order | an ONI building has no connection points; `:1` on a single network finds nothing |
| the chip | an item made in a printer and put in a housing | the program is held by the IC Housing itself | ONI has no hand-carried items for this |
| setting sockets | a screwdriver | the details panel: the same step order with `<` and `>`, plus a link tool and Pick | ONI has no hand tools |
| tick | every 500 ms of real game ticks | every 500 ms of game time on ONI's 200 ms simulation steps, so ticks land 0.6 s and 0.4 s apart; the same average | ONI's simulation step |
| thread | a worker thread | the game's main thread | a read touches the game's objects |
| `hcf` | the housing explodes and burns | the chip stops and the housing shows as broken | not built yet |
| the code editor | 128 line fields | one text box with Stationeers' limits, a compile check, live registers and the reference line | ONI's UI |
| `Activate` | writable on most devices | read only, except on machines with recipes and the IC Button | in ONI it is a state, not a command |
| `Quantity` | a slot's count | kilograms | ONI stores mass |
| `lr ... Contents hash` | a fabricator's reagents | kilograms of that element or product the building stores | ONI has no reagents |
| `Temperature` on a building | the gas inside it | the gas and liquid in its storage; the building's own temperature is `BodyTemperature` | ONI keeps fluids in a building's storage |
| `Pressure` on a building's contents | its internal atmosphere | not answered | ONI gives a storage no volume |
| slots | fixed slots | the items in the building's storage, in order; slots shift as items come and go | ONI storage is a list |
| a door's `Lock` | keeps players' hands off; a chip can still open a locked door | bars duplicants; the last of `Open 1` and `Lock 1` written wins | ONI's door has one state of three |
| `Combustion` | answered | not answered | no combustion readings yet |
| a network's device count after an edit | can lag until the network next updates | always current | ONI rebuilds a network on every edit |
| batch order after a rename | re-sorted when the housing's own network changes | re-sorted on any data network change | visible only in the rounding of a sum |
| ONI's own values | — | new logic types from 1024 (`BodyTemperature`, `PowerDraw`, `Filter`, `Cycle`, the rocket readings and the rest), never reusing a Stationeers number | ONI buildings have state Stationeers' list has no name for |
| gas ratio names | Stationeers' species | the same 28 types, read from the ONI element of the same name; `RatioPollutedWater` reads Polluted Water, `RatioLiquidAlcohol` reads Ethanol, `RatioLiquidSodiumChloride` reads Molten Salt; a species ONI does not have reads 0 | different element names |
| a transformer | has a settable output | ONI's transformers gain a saved output limit, set on a slider or by a chip through `Setting`; untouched, a transformer behaves as the game's own | so that `Setting` means what it means in Stationeers |
| the solar panel | `Charge`, `Maximum`, `Ratio`, aiming | `Charge` and `Maximum` as Stationeers, `Ratio` 1, no aiming; it also answers `PowerGeneration`. With Mod 2 installed, `Maximum` reads half the panel's rating (a known issue) | ONI's panels do not aim |
| `PowerPotential` | counts generators and stored battery charge | counts generators, including a transformer at its limit | ONI's circuit sums |
| logic units' power | some draw power | none of the units or bricks draws power, except the IC Display (10 W) | simpler wiring in ONI's grid |
| two Logic Mirrors facing each other | overflow | answer nothing | avoids a hang |
| a bin's or fridge's checklist | Stationeers' lockers have no filter | a chip sets it with the sorter's "this kind" filter | ONI's storage filters |
| driving an automation input | — | a chip drives the first automation input of a building over data (`LogicInput`, or `On` on a logic valve); an attached wire wins; the value stays until written again | so a chip can drive most vanilla buildings; the Rocket Control Station's drive is untested |
| a building on data cable | — | no longer shows the "No Automation Wire Connected" warning | the cable stands in for the wire |
| Mod 1's Volume Pump | `Setting` in litres per 500 ms tick | `Setting` in litres per second, 0 to 100 | the pump's own unit; the same number moves half as much here as a Stationeers volume pump would |
| Stationeers' other name tables (`GasType.`, `Sound.`, `AirCon.` and the rest) | constants a program can name | only `Color.` and `Vent.`, plus ONI's own; a program that names another table does not compile here | not built yet |
| in-game help pages, achievements, the chip's memory light | part of the game | none | not built yet |
| the program's random numbers and clock | the game's own | the housing's own, from game time | ONI's clock |
