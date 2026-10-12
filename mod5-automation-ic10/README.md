# Mod 5 — Automation / IC10

Mod 5 brings Stationeers' programmable automation into Oxygen Not Included. A building called
the IC Housing holds a program written in IC10, the language of Stationeers' integrated
circuits, and runs it against the buildings on its data cable. The program reads values out of
them and writes values back: switch a building on, move a slider, open a door, pick a recipe,
order ten bricks. Most programs written for Stationeers compile here unchanged and run on ONI's
own buildings; the [reference](IC10-REFERENCE.md#where-it-differs-from-stationeers) lists the
exceptions.

It is built only on the public `OniFramework` API. Any other mod can make its own buildings
readable and writable from a chip through `OniFramework.DataDevices`, without referencing this
mod.

Requires the SDK's replacement `SimDLL.dll` and the `OniFramework` and `OniFramework.Library`
mods, from the same SDK release, on the game's Windows version. Mod 5 does not need Mod 1 or Mod 2; with Mod 1
installed, a chip also reads and drives Mod 1's buildings (its Volume Pump, its pipe pressure).

## What you can build

All of it is in the Automation build menu, except the IC Sorter, which is with the conveyor
buildings. Each building is unlocked by research.

| building | does | research |
|---|---|---|
| IC Housing | runs one IC10 program, 128 lines, two ticks per second of game time; six sockets, `d0` to `d5` | `Multiplexing` |
| Data Cable | a third kind of wire; every building with cable under any of its cells joins that cable's data network | `LogicControl` |
| Data Cable Bridge | carries data cable over three cells | `LogicControl` |
| IC Dial, IC Switch, IC Button, IC Display | small bricks that each keep one number a chip reads or writes; the display draws its number in the world | `LogicControl` |
| IC Memory | a brick that keeps one number a program writes | `LogicCircuits` |
| IC Reader, IC Writer, IC Writer Switch | copy one value from a building, or to one | `LogicControl` |
| IC Math, IC Math (Unary), IC Compare, IC Select, IC Min/Max | compute from other units' values, with no program | `LogicCircuits` |
| IC Slot Reader, IC Batch Reader, IC Batch Slot Reader, IC Batch Writer, IC Hash Generator, IC Logic Mirror | the same over every building of a kind, or across two networks | `ParallelAutomation` |
| IC PID Controller | drives a value toward a set point | `Multiplexing` |
| IC Sorter | a conveyor filter with a list of 32 filters a program writes | `SolidManagement` |

[Devices](DEVICES.md) lists each building and what a chip can read and write on the game's own
buildings.

Alongside the buildings:

- **A program editor inside the game.** Open it from the housing's details panel. It holds
  you to Stationeers' limits (128 lines, 90 characters a line, 4096 in all), numbers the lines
  from 0, says whether the text compiles and on which line it fails, shows the reference line
  for the instruction under the caret, and shows the chip's registers and sockets while it runs.
- **A data overlay**, with filters for which layer's buildings are shown, and lines from a chip
  to whatever its sockets point at.
- **A link tool**: press Link beside a socket and click the building.
- **Save and load**: the program, registers, stack, sockets and every setting a chip wrote
  come back with the save.

## A first program

Build an IC Housing and give it power. Lay Data Cable from under the housing to under a
Thermo Sensor and under a Space Heater. In the housing's details panel, set socket `d0` to
the sensor and `d1` to the heater (Link, then click each building). Press Edit Program and type:

```
alias sensor d0          # socket 0: the temperature sensor
alias heater d1          # socket 1: the space heater
define target 295        # kelvin, about 22 °C
loop:
l r0 sensor Temperature  # read the sensor
slt r1 r0 target         # r1 = 1 while it is colder than the target
s heater On r1           # switch the heater on or off
yield                    # wait for the next tick
j loop
```

Press Apply. The heater switches on below 295 K and off above it. `examples/` in this folder
has twelve more programs to paste in: a thermostat with a dead band, a standby generator, a
scrubber switch, a pressure regulator, a readout on an IC Display, a two-door airlock, a power
total over every generator on the cable, counted fabricator orders, keeping a stock level,
harvesting, sorting ores on a conveyor, and a fridge's menu.

The language itself, every instruction and every value ONI's buildings answer, is in the
[IC10 reference](IC10-REFERENCE.md).

## How it is checked

The language is checked by automated tests that run without the game, each expected value
worked out by hand from how Stationeers behaves. In the game, test scenarios run programs
against vanilla buildings, save, load in a second game process and check that everything came
back, and run *Salacious Airlock v0.2*, a program written for Stationeers, cycling a real
two-door airlock through Mod 1's Active Vent. For this release every check passed except one,
listed under known limitations below (a Solar Panel's `Maximum` with Mod 2). The tests and test
scenarios are developer tooling and are not part of this repository.

## Known limitations

- The building art is AI placeholder art, to be replaced. The final art is still being made.
- With Mod 2 installed, a Solar Panel's `Maximum` reads half of the panel's real rating. A fix
  is planned.
- Only the first automation input of a building can be driven by a chip. A mod's building that
  adds its automation ports late in loading cannot be driven at all.
- `Pressure` is answered only on the replacement `SimDLL.dll`. A storage building has no volume
  in ONI, so it answers no `Pressure` either; its `TotalMoles` and gas ratios are there.
- `hcf` stops the chip and shows the housing as broken. It does not start a fire yet.
- A data network's eight channels are not saved: they read NaN after a load, as in Stationeers.
- The IC Memory and the IC Sorter have no controls of their own for the player yet; a program
  sets them.
- Not every building has been driven in the game; the Rocket Control Station's drive is untested. `BUILDINGS.md` is read off the game's
  building definitions; a part a building gains only while the game runs is not listed.
- IC10 here is compared against how Stationeers is written, not yet against a running
  Stationeers. Where the two differ on purpose, the [reference](IC10-REFERENCE.md#where-it-differs-from-stationeers)
  lists it.

## Building and installing

See the repository's README.

## Credits

IC10, its instruction set and the logic units are Stationeers', by RocketWerkz. The interpreter
in `Ic10/` follows Stationeers' own closely so that Stationeers programs run here; those parts are
not under the MIT licence and will be rewritten later (see the repository's `NOTICE`). This mod
is not affiliated with or endorsed by RocketWerkz.
